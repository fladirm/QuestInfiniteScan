// Host (linux x86_64) unit tests for the driver-free executor logic (contract §15.4, §11, §15.7, §20):
// class rings, budget accounting, deferral (ring / budget / dependency), lease generation checks,
// root-last publish ordering, timeline bookkeeping, deferred destruction, latest-only scan requests,
// pipeline cache / binary pack files, telemetry counters + stage ring + clock fit + JSON.
// HOST_TEST_SOURCES: host/executor/sched_core.cpp host/executor/pipeline_store.cpp host/telemetry.cpp
#include "../src/host/executor/sched_core.h"
#include "../src/host/executor/pipeline_store.h"
#include "../src/host/telemetry.h"
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <unistd.h>

static int g_failures = 0;
#define CHECK(cond) do { if (!(cond)) { std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); ++g_failures; } } while (0)
#define CHECK_EQ(a, b) do { auto _a = (a); auto _b = (b); if (!(_a == _b)) { std::printf("FAIL %s:%d: %s == %s (got %lld vs %lld)\n", __FILE__, __LINE__, #a, #b, (long long)_a, (long long)_b); ++g_failures; } } while (0)

using namespace fs;
using namespace fs::sched;

static CoreConfig DefaultConfig(bool fallback = false) { return NormalizeConfig(nullptr, fallback); }

static void TestNormalizeConfig() {
    CoreConfig c = DefaultConfig();
    CHECK_EQ(c.frameBudgetUs, kDefaultFrameBudgetUs);
    CHECK_EQ(c.inFlightMax[FS_JOB_PUBLISH], 4u); CHECK_EQ(c.inFlightMax[FS_JOB_SCAN], 2u); CHECK_EQ(c.inFlightMax[FS_JOB_INNER_RESIDENCY], 2u);
    CHECK_EQ(c.inFlightMax[FS_JOB_APPEARANCE], 1u); CHECK_EQ(c.inFlightMax[FS_JOB_COLD], 1u);
    CHECK(c.quantumUs[FS_JOB_SCAN] <= 2000u);   // C01 design point: quantum <= 2 ms
    CHECK(!c.queueFallback);
    // explicit config, clamping, fallback cap
    FsHostConfig cfg = {}; cfg.structSize = sizeof(FsHostConfig); cfg.frameBudgetUs = 1800;
    for (uint32_t i = 0; i < kClassCount; ++i) { cfg.inFlightMax[i] = 100; cfg.quantumUs[i] = 5000; }
    CoreConfig f = NormalizeConfig(&cfg, true);
    CHECK_EQ(f.frameBudgetUs, 1800u);
    for (uint32_t i = 0; i < kClassCount; ++i) { CHECK_EQ(f.inFlightMax[i], kMaxInFlightPerClass); CHECK_EQ(f.quantumUs[i], kFallbackQuantumCapUs); }
    CHECK(f.queueFallback);
    // a too-small struct is treated as absent
    FsHostConfig tiny = {}; tiny.structSize = 8; tiny.frameBudgetUs = 1;
    CHECK_EQ(NormalizeConfig(&tiny, false).frameBudgetUs, kDefaultFrameBudgetUs);
    CHECK(std::strcmp(ClassName(FS_JOB_PUBLISH), "PUBLISH") == 0 && std::strcmp(ClassName(FS_JOB_COLD), "COLD") == 0 && std::strcmp(ClassName(99), "?") == 0);
}

static void TestClassRing() {
    ClassRing r; r.Init(2);
    CHECK_EQ(r.Capacity(), 2u); CHECK(!r.Full()); CHECK_EQ(r.OldestInFlight(), -1);
    const int32_t a = r.Acquire(); const int32_t b = r.Acquire();
    CHECK(a == 0 && b == 1); CHECK(r.Full()); CHECK_EQ(r.Acquire(), -1);
    r.Slot(0).submitEpoch = 5; r.Slot(1).submitEpoch = 3;
    CHECK_EQ(r.OldestInFlight(), 1);
    r.Release(0); CHECK(!r.Full()); CHECK_EQ(r.InFlight(), 1u);
    r.Release(0);   // double release is a no-op
    CHECK_EQ(r.InFlight(), 1u);
    CHECK_EQ(r.Acquire(), 0);   // round-robin resumes after the last acquired slot (1) → 0
    r.Release(1); r.Release(0);
    CHECK_EQ(r.InFlight(), 0u);
    ClassRing z; z.Init(0); CHECK_EQ(z.Capacity(), 1u);   // never a zero-capacity ring
}

static void TestTimelineTracker() {
    TimelineTracker t;
    CHECK_EQ(t.Allocate(), 1u); CHECK_EQ(t.Allocate(), 2u); CHECK_EQ(t.Allocate(), 3u);
    CHECK_EQ(t.PendingCount(), 3u);
    t.MarkRetired(2);                  // out of order: nothing visible yet
    CHECK_EQ(t.LastRetired(), 0u);
    t.MarkRetired(1);                  // absorbs 2
    CHECK_EQ(t.LastRetired(), 2u);
    t.MarkRetired(9);                  // beyond allocation: ignored
    CHECK_EQ(t.LastRetired(), 2u);
    t.MarkRetired(3); CHECK_EQ(t.LastRetired(), 3u); CHECK_EQ(t.PendingCount(), 0u);
    t.MarkRetired(3);                  // duplicate: ignored
    CHECK_EQ(t.LastRetired(), 3u);
    t.Reset(); CHECK_EQ(t.LastAllocated(), 0u);
}

static void TestBudget() {
    BudgetLedger b; b.BeginFrame(2500);
    CHECK(b.TryConsume(2000)); CHECK_EQ(b.Remaining(), 500u);
    CHECK(!b.TryConsume(501)); CHECK(b.TryConsume(500)); CHECK_EQ(b.Remaining(), 0u);
    CHECK(!b.TryConsume(1)); CHECK(b.TryConsume(0));
    b.BeginFrame(100); CHECK_EQ(b.Used(), 0u); CHECK_EQ(b.Frames(), 2u);
}

static void TestSchedulerSubmitDeferRetire() {
    SchedulerCore core; core.Init(DefaultConfig());
    core.BeginFrame(1);
    // SCAN ring = 2, quantum 2000, budget 2500: first accepted, second deferred by budget, then by ring
    SubmitDecision a = core.TrySubmit(FS_JOB_SCAN, 0, FS_INDEX_NONE, 0, nullptr, 100);
    CHECK(a.outcome == SubmitOutcome::Accepted); CHECK_EQ(a.quantumUs, core.Config().quantumUs[FS_JOB_SCAN]); CHECK_EQ(a.timelineValue, 1u); CHECK_EQ(a.jobId, 1u);
    SubmitDecision b = core.TrySubmit(FS_JOB_SCAN, 0, FS_INDEX_NONE, 0, nullptr, 101);
    CHECK(b.outcome == SubmitOutcome::DeferredBudget);
    CHECK_EQ(core.Stats(FS_JOB_SCAN).deferredBudget, 1); CHECK_EQ(core.Stats(FS_JOB_SCAN).Deferred(), 1);
    // a PUBLISH micro-job (500 us) still fits the remaining 500 us
    SubmitDecision p = core.TrySubmit(FS_JOB_PUBLISH, 0, FS_INDEX_NONE, 0, nullptr, 102);
    CHECK(p.outcome == SubmitOutcome::Accepted); CHECK_EQ(core.Budget().Remaining(), 0u);
    // next frame: budget resets; SCAN slot 2 accepted, SCAN slot 3 deferred by ring (2 in flight)
    core.BeginFrame(2);
    SubmitDecision c = core.TrySubmit(FS_JOB_SCAN, 0, FS_INDEX_NONE, 0, nullptr, 200);
    CHECK(c.outcome == SubmitOutcome::Accepted); CHECK_EQ(c.slot, 1u); CHECK_EQ(c.timelineValue, 2u);
    SubmitDecision d = core.TrySubmit(FS_JOB_SCAN, 1, FS_INDEX_NONE, 0, nullptr, 201);   // 1 us would fit the budget: ring is the limit
    CHECK(d.outcome == SubmitOutcome::DeferredRing);
    CHECK_EQ(core.Stats(FS_JOB_SCAN).deferredRing, 1);
    CHECK(core.AnyInFlight()); CHECK_EQ(core.MinInFlightEpoch(), 1u);
    // retire out of order: slot 1 (value 2) first → timeline stays at 0 until value 1 retires
    core.Retire(FS_JOB_SCAN, 1, true, 1500.0);
    CHECK_EQ(core.Timeline(FS_JOB_SCAN).LastRetired(), 0u);
    CHECK_EQ(core.Stats(FS_JOB_SCAN).retired, 1); CHECK_EQ(core.Stats(FS_JOB_SCAN).gpuUsLast, 1500);
    core.Retire(FS_JOB_SCAN, 0, true, 1000.0);
    CHECK_EQ(core.Timeline(FS_JOB_SCAN).LastRetired(), 2u);
    CHECK_EQ(core.Stats(FS_JOB_SCAN).gpuUsTotal, 2500);
    CHECK_EQ(core.Ring(FS_JOB_SCAN).InFlight(), 0u);
    CHECK_EQ(core.MinInFlightEpoch(), 2u);   // the PUBLISH job (epoch 2) is still in flight
    core.Retire(FS_JOB_PUBLISH, 0, false, 0.0);   // failure retires the timeline value too
    CHECK_EQ(core.Stats(FS_JOB_PUBLISH).failed, 1); CHECK_EQ(core.Timeline(FS_JOB_PUBLISH).LastRetired(), 1u);
    CHECK(!core.AnyInFlight()); CHECK_EQ(core.MinInFlightEpoch(), UINT64_MAX);
    // retire of a free slot is a no-op
    core.Retire(FS_JOB_SCAN, 0, true, 5.0); CHECK_EQ(core.Stats(FS_JOB_SCAN).retired, 2);
    // GetClassStats layout
    int64_t st[8] = {};
    CHECK(core.GetClassStats(FS_JOB_SCAN, st));
    CHECK_EQ(st[0], 2); CHECK_EQ(st[1], 2); CHECK_EQ(st[2], 2); CHECK_EQ(st[3], 0); CHECK_EQ(st[4], 2500); CHECK_EQ(st[5], 1000); CHECK_EQ(st[6], 0); CHECK_EQ(st[7], (int64_t)core.Config().quantumUs[FS_JOB_SCAN]);
    CHECK(!core.GetClassStats(7, st)); CHECK(!core.GetClassStats(0, nullptr));
    // invalid class / not accepting
    CHECK(core.TrySubmit(static_cast<FsJobClass>(9), 0, FS_INDEX_NONE, 0, nullptr, 0).outcome == SubmitOutcome::Refused);
    core.SetAcceptingSubmits(false);
    CHECK(core.TrySubmit(FS_JOB_SCAN, 0, FS_INDEX_NONE, 0, nullptr, 0).outcome == SubmitOutcome::Refused);
}

static void TestLeaseValidation() {
    SchedulerCore core; core.Init(DefaultConfig());
    core.BeginFrame(1);
    uint32_t currentGen[4] = { 7, 8, 9, 10 };
    LeaseValidator v = [&](uint32_t slot, uint32_t gen) { return slot < 4 && currentGen[slot] == gen; };
    // matching lease: accepted; moved generation: dropped (counted, no ring slot consumed, no budget consumed)
    CHECK(core.TrySubmit(FS_JOB_PUBLISH, 0, 2, 9, &v, 0).outcome == SubmitOutcome::Accepted);
    const uint32_t remaining = core.Budget().Remaining();
    CHECK(core.TrySubmit(FS_JOB_PUBLISH, 0, 2, 8, &v, 0).outcome == SubmitOutcome::DroppedLease);
    CHECK_EQ(core.Stats(FS_JOB_PUBLISH).droppedLease, 1); CHECK_EQ(core.Ring(FS_JOB_PUBLISH).InFlight(), 1u); CHECK_EQ(core.Budget().Remaining(), remaining);
    // out-of-range slot: dropped; no lease (FS_INDEX_NONE): validator not consulted
    CHECK(core.TrySubmit(FS_JOB_PUBLISH, 0, 99, 1, &v, 0).outcome == SubmitOutcome::DroppedLease);
    int calls = 0; LeaseValidator counting = [&](uint32_t, uint32_t) { ++calls; return false; };
    CHECK(core.TrySubmit(FS_JOB_PUBLISH, 0, FS_INDEX_NONE, 0, &counting, 0).outcome == SubmitOutcome::Accepted);
    CHECK_EQ(calls, 0);
    LeaseValidator empty;   // unset validator → accepted
    CHECK(core.TrySubmit(FS_JOB_PUBLISH, 0, 3, 123, &empty, 0).outcome == SubmitOutcome::Accepted);
    CHECK(core.TrySubmit(FS_JOB_PUBLISH, 0, 3, 123, nullptr, 0).outcome == SubmitOutcome::Accepted);
    // the lease is recorded on the slot
    const SlotState& s = core.Ring(FS_JOB_PUBLISH).Slot(0);
    CHECK(s.inFlight && s.leaseSlot == 2 && s.leaseGeneration == 9);
}

static void TestDependencyDeferral() {
    SchedulerCore core; core.Init(DefaultConfig());
    core.BeginFrame(1);
    // PUBLISH waiting on SCAN value 1 before any SCAN job exists → deferred (a GPU wait must never dangle)
    SubmitDecision d = core.TrySubmit(FS_JOB_PUBLISH, 0, FS_INDEX_NONE, 0, nullptr, 0, FS_JOB_SCAN, 1);
    CHECK(d.outcome == SubmitOutcome::DeferredDependency); CHECK_EQ(core.Stats(FS_JOB_PUBLISH).deferredDependency, 1);
    CHECK_EQ(core.Stats(FS_JOB_PUBLISH).Deferred(), 1);
    SubmitDecision s = core.TrySubmit(FS_JOB_SCAN, 0, FS_INDEX_NONE, 0, nullptr, 0);
    CHECK(s.outcome == SubmitOutcome::Accepted && s.timelineValue == 1);
    // now the dependency is allocated (submitted) → accepted even though not yet retired
    CHECK(core.TrySubmit(FS_JOB_PUBLISH, 0, FS_INDEX_NONE, 0, nullptr, 0, FS_JOB_SCAN, 1).outcome == SubmitOutcome::Accepted);
    // waiting on a future value stays deferred; invalid wait class refused
    CHECK(core.TrySubmit(FS_JOB_PUBLISH, 0, FS_INDEX_NONE, 0, nullptr, 0, FS_JOB_SCAN, 2).outcome == SubmitOutcome::DeferredDependency);
    CHECK(core.TrySubmit(FS_JOB_PUBLISH, 0, FS_INDEX_NONE, 0, nullptr, 0, static_cast<FsJobClass>(5), 1).outcome == SubmitOutcome::Refused);
}

static void TestQuarantine() {
    SchedulerCore core; core.Init(DefaultConfig());
    core.BeginFrame(1);
    CHECK(core.TrySubmit(FS_JOB_PUBLISH, 0, 1, 1, nullptr, 0).outcome == SubmitOutcome::Accepted);
    CHECK(core.TrySubmit(FS_JOB_SCAN, 0, 2, 2, nullptr, 0).outcome == SubmitOutcome::Accepted);
    std::vector<std::pair<FsJobClass, SlotState>> failed;
    core.FailAllInFlight(failed);
    CHECK_EQ(failed.size(), (size_t)2);
    CHECK(failed[0].first == FS_JOB_PUBLISH && failed[0].second.leaseSlot == 1);
    CHECK(failed[1].first == FS_JOB_SCAN && failed[1].second.leaseSlot == 2);
    CHECK(!core.AnyInFlight()); CHECK(!core.AcceptingSubmits());
    CHECK_EQ(core.Stats(FS_JOB_PUBLISH).failed, 1); CHECK_EQ(core.Stats(FS_JOB_SCAN).failed, 1);
    CHECK_EQ(core.Timeline(FS_JOB_SCAN).LastRetired(), 1u);   // host-signalled retirement keeps the timeline consistent
    CHECK(core.TrySubmit(FS_JOB_SCAN, 0, FS_INDEX_NONE, 0, nullptr, 0).outcome == SubmitOutcome::Refused);
    core.Init(DefaultConfig());   // rebuild after device re-init
    CHECK(core.AcceptingSubmits()); CHECK_EQ(core.Stats(FS_JOB_SCAN).failed, 0);
}

static void TestRootLastPublish() {
    PublishLedger led; led.Reset(4);
    CHECK_EQ(led.FrontGeneration(0), 0u); CHECK_EQ(led.RootEpoch(), 0u);
    led.BeginPublish(10, 1, 5);          // job 10 writes FRONT of slot 1, generation 5
    led.BeginPublish(11, 1, 6);          // a later publish of the same slot
    led.BeginPublish(12, 2, 1);
    CHECK_EQ(led.PendingCount(), 3u);
    CHECK_EQ(led.FrontGeneration(1), 0u);            // nothing visible until the fence proves the data
    CHECK(led.Retire(11, true));                     // out-of-order: generation 6 lands first
    CHECK_EQ(led.FrontGeneration(1), 6u); CHECK_EQ(led.RootEpoch(), 1u);
    CHECK(!led.Retire(10, true));                    // older generation must not regress the root
    CHECK_EQ(led.FrontGeneration(1), 6u); CHECK_EQ(led.RootEpoch(), 1u);
    CHECK(!led.Retire(12, false));                   // failed publish: root untouched
    CHECK_EQ(led.FrontGeneration(2), 0u);
    CHECK(!led.Retire(99, true));                    // unknown job
    CHECK_EQ(led.PendingCount(), 0u);
    led.BeginPublish(13, 9, 2);                      // slot beyond Reset(): grows
    CHECK(led.Retire(13, true)); CHECK_EQ(led.FrontGeneration(9), 2u);
    CHECK(led.FrontGenerationXor() != 0u);
    const uint32_t x1 = led.FrontGenerationXor();
    led.BeginPublish(14, 9, 3); led.Retire(14, true);
    CHECK(led.FrontGenerationXor() != x1);
}

static void TestGarbageLedger() {
    GarbageLedger g;
    g.Push(1, 3, 100);   // destroyed after job epoch 3 was submitted, last used in Unity frame 100
    g.Push(2, 5, 100);
    g.Push(3, 5, 102);
    std::vector<uint64_t> freed;
    g.Collect(4, 101, freed);            // jobs <4 retired, safe frame 101 → only token 1
    CHECK_EQ(freed.size(), (size_t)1); CHECK_EQ(freed[0], 1u); CHECK_EQ(g.Pending(), (size_t)2);
    freed.clear(); g.Collect(UINT64_MAX, 101, freed);   // nothing in flight: token 2 (frame ok), token 3 waits for frame 102
    CHECK_EQ(freed.size(), (size_t)1); CHECK_EQ(freed[0], 2u);
    freed.clear(); g.Collect(UINT64_MAX, UINT64_MAX, freed);
    CHECK_EQ(freed.size(), (size_t)1); CHECK_EQ(freed[0], 3u); CHECK_EQ(g.Pending(), (size_t)0);
    g.Push(7, 1, 1); g.Push(8, 1, 1);
    freed.clear(); g.TakeAll(freed); CHECK_EQ(freed.size(), (size_t)2); CHECK_EQ(g.Pending(), (size_t)0);
}

static void TestScanRequests() {
    ScanRequestSlot s; uint32_t id = 0;
    CHECK(!s.Take(id));
    s.Request(10); s.Request(11); s.Request(12);   // latest-only: two superseded
    CHECK_EQ(s.Requested(), 3); CHECK_EQ(s.Skipped(), 2);
    CHECK(s.Take(id)); CHECK_EQ(id, 12u); CHECK(!s.Take(id));
    s.Request(13); CHECK(s.Take(id)); CHECK_EQ(id, 13u); CHECK_EQ(s.Skipped(), 2);
}

static void TestPipelineStore() {
    char tmpl[] = "/tmp/fs-store-XXXXXX";
    const char* dir = mkdtemp(tmpl);
    CHECK(dir != nullptr);
    if (!dir) return;
    const std::string storage = std::string(dir) + "/finalscan";
    store::CacheIdentity id; id.driverVersion = 0x80345009; id.vendorID = 0x5143; id.deviceID = 0x43050B00;
    for (uint32_t i = 0; i < store::kUuidSize; ++i) id.pipelineCacheUUID[i] = static_cast<uint8_t>(i * 7 + 1);
    // Vulkan pipeline cache header (32 B) + payload
    std::vector<uint8_t> blob(32 + 100, 0xAB);
    uint32_t hs = 32, hv = 1; std::memcpy(blob.data(), &hs, 4); std::memcpy(blob.data() + 4, &hv, 4); std::memcpy(blob.data() + 8, &id.vendorID, 4); std::memcpy(blob.data() + 12, &id.deviceID, 4); std::memcpy(blob.data() + 16, id.pipelineCacheUUID, 16);
    const std::vector<uint8_t> file = store::EncodeCacheFile(id, blob.data(), blob.size());
    CHECK(store::WriteFileAtomic(store::CacheFilePath(storage), file.data(), file.size()));
    std::vector<uint8_t> read, out;
    CHECK(store::ReadFile(store::CacheFilePath(storage), read));
    CHECK(store::DecodeCacheFile(read, id, out)); CHECK(out == blob);
    // foreign driver / uuid / vendor mismatch / truncated / garbage: ignored, never an error
    store::CacheIdentity other = id; other.driverVersion += 1;
    CHECK(!store::DecodeCacheFile(read, other, out)); CHECK(out.empty());
    other = id; other.pipelineCacheUUID[3] ^= 1; CHECK(!store::DecodeCacheFile(read, other, out));
    std::vector<uint8_t> bad = read; bad[sizeof(uint32_t) * 6 + 16 + 8 + 8] ^= 0xFF;   // corrupt the inner vendor id
    CHECK(!store::DecodeCacheFile(bad, id, out));
    bad = read; bad.resize(bad.size() - 1); CHECK(!store::DecodeCacheFile(bad, id, out));
    bad.assign(10, 0); CHECK(!store::DecodeCacheFile(bad, id, out));
    CHECK(!store::DecodeCacheFile(std::vector<uint8_t>(), id, out));
    // binary pack
    std::vector<store::BinaryBlob> blobs(2);
    blobs[0].keySize = 4; blobs[0].key[0] = 1; blobs[0].key[1] = 2; blobs[0].data = { 9, 8, 7 };
    blobs[1].keySize = 32; for (uint32_t i = 0; i < 32; ++i) blobs[1].key[i] = static_cast<uint8_t>(i); blobs[1].data.assign(1000, 0x5A);
    const std::vector<uint8_t> pack = store::EncodeBinaryPack(id, blobs);
    const std::string path = store::BinaryFilePath(storage, "0102", "abcd");
    CHECK(path == storage + "/binaries/0102/abcd.bin");
    CHECK(store::WriteFileAtomic(path, pack.data(), pack.size()));
    std::vector<uint8_t> packRead; std::vector<store::BinaryBlob> decoded;
    CHECK(store::ReadFile(path, packRead));
    CHECK(store::DecodeBinaryPack(packRead, id, decoded));
    CHECK_EQ(decoded.size(), (size_t)2);
    if (decoded.size() == 2) {
        CHECK_EQ(decoded[0].keySize, 4u); CHECK(decoded[0].data == blobs[0].data); CHECK(std::memcmp(decoded[0].key, blobs[0].key, 32) == 0);
        CHECK_EQ(decoded[1].keySize, 32u); CHECK(decoded[1].data == blobs[1].data);
    }
    CHECK(!store::DecodeBinaryPack(packRead, other, decoded)); CHECK(decoded.empty());
    bad = packRead; bad.push_back(0); CHECK(!store::DecodeBinaryPack(bad, id, decoded));   // trailing bytes
    bad = packRead; bad.resize(bad.size() - 10); CHECK(!store::DecodeBinaryPack(bad, id, decoded));
    CHECK(!store::DecodeBinaryPack(read, id, decoded));   // cache file is not a pack
    CHECK(!store::ReadFile(storage + "/missing.bin", packRead));
    CHECK(store::HexString(blobs[0].key, 3) == "010200");
    CHECK(store::MakeDirs(storage + "/a/b/c"));
    // cleanup
    std::remove(path.c_str()); std::remove(store::CacheFilePath(storage).c_str());
    rmdir((storage + "/a/b/c").c_str()); rmdir((storage + "/a/b").c_str()); rmdir((storage + "/a").c_str());
    rmdir((storage + "/binaries/0102").c_str()); rmdir((storage + "/binaries").c_str()); rmdir(storage.c_str()); rmdir(dir);
}

static void TestTelemetry() {
    Telemetry t;
    t.CounterAdd(FS_CTR_SCAN_TICK, 3); t.CounterAdd(FS_CTR_SCAN_TICK, 2); t.CounterAdd(-1, 5); t.CounterAdd(FS_CTR_COUNT, 5);
    CHECK_EQ(t.CounterGet(FS_CTR_SCAN_TICK), 5); CHECK_EQ(t.CounterGet(FS_CTR_COUNT), 0);
    CHECK(std::strcmp(CounterName(FS_CTR_ORIENTATION_RESIDENCY_REQUESTS), "orientationResidencyRequests") == 0);
    CHECK(std::strcmp(CounterName(FS_CTR_TIMESTAMP_NONMONO), "timestampNonMonotonic") == 0);
    for (uint32_t i = 0; i < kStageRingSize + 10; ++i) t.Stage(FS_JOB_SCAN, "integrate", 1000 * i, 1000 * i + 500, i);
    CHECK_EQ(t.StageSequence(), (uint64_t)(kStageRingSize + 10));
    t.Error("first"); for (int i = 0; i < 20; ++i) t.Error("e");
    // clock fit: cpu = 1e9 + 1.0000005 * gpu, 64 marks 13.9 ms apart (refit every 16 marks)
    for (int i = 0; i < 64; ++i) { const double gpu = 5e9 + i * 13.9e6; t.FrameMark(gpu, gpu + 2e6, static_cast<int64_t>(1e9 + 1.0000005 * gpu), 1000 + i); }
    CHECK(t.ClockValid());
    const double est = t.GpuNowEstimateNs(static_cast<int64_t>(1e9 + 1.0000005 * (5e9 + 70 * 13.9e6)));
    CHECK(std::fabs(est - (5e9 + 70 * 13.9e6)) < 1e3);
    t.FrameMark(1.0, 2.0, 1, 2000);                   // non-monotonic GPU mark is counted
    CHECK_EQ(t.CounterGet(FS_CTR_TIMESTAMP_NONMONO), 1);
    const std::string json = t.Json(true, "\"status\":2,\"frame\":{\"index\":7}");
    CHECK(json.find("\"host\":{\"status\":2,\"frame\":{\"index\":7}}") != std::string::npos);
    CHECK(json.find("\"scanTick\":5") != std::string::npos);
    CHECK(json.find("\"stagesTotal\":266") != std::string::npos);
    CHECK(json.find("\"nonMonotonicMarks\":1") != std::string::npos);
    CHECK(json.find("\"total\":21") != std::string::npos);
    CHECK(json.find("\"first\"") == std::string::npos);   // only the last 8 errors are kept
    // stage ring holds the last 256 in order
    const size_t firstStage = json.find("\"seq\":11,");
    CHECK(firstStage != std::string::npos);
    CHECK(json.find("\"seq\":10,") == std::string::npos);
    const std::string noStages = t.Json(false, "");
    CHECK(noStages.find("\"stages\"") == std::string::npos && noStages.find("\"host\"") == std::string::npos);
    // balanced braces
    int depth = 0; bool ok = true; bool inStr = false;
    for (size_t i = 0; i < json.size(); ++i) { char c = json[i]; if (c == '"' && (i == 0 || json[i - 1] != '\\')) inStr = !inStr; if (inStr) continue; if (c == '{' || c == '[') ++depth; if (c == '}' || c == ']') { --depth; if (depth < 0) ok = false; } }
    CHECK(ok && depth == 0);
    t.Reset(); CHECK_EQ(t.CounterGet(FS_CTR_SCAN_TICK), 0); CHECK(!t.ClockValid());
    // ClockFit degenerate: identical gpu samples → offset only; two samples → exact
    ClockFit f; f.Add(100, 1100); f.Add(100, 1100); CHECK(f.Fit()); CHECK(f.Slope() == 1.0 && f.Offset() == 1000.0);
    ClockFit g; CHECK(!g.Fit()); g.Add(0, 10); g.Add(1000, 1010); CHECK(g.Fit()); CHECK(std::fabs(g.CpuFromGpu(500) - 510) < 1e-9); CHECK(std::fabs(g.GpuFromCpu(510) - 500) < 1e-9);
    ClockFit h; h.Add(0, 0); h.Add(1, 1000); CHECK(h.Fit()); CHECK(h.Slope() == 1.0);   // absurd slope guarded
}

// Scheduling loop model (design §4): ticks run in class order with the remaining budget; SubmitJob consumes it.
static void TestClassOrderBudgetModel() {
    SchedulerCore core; core.Init(DefaultConfig());
    core.BeginFrame(1);
    std::vector<int> order;
    auto tick = [&](FsJobClass cls) { order.push_back(cls); return core.TrySubmit(cls, 0, FS_INDEX_NONE, 0, nullptr, 0); };
    SubmitDecision r[5];
    for (uint32_t c = 0; c < kClassCount; ++c) r[c] = tick(static_cast<FsJobClass>(c));
    CHECK(order == std::vector<int>({0, 1, 2, 3, 4}));
    CHECK(r[0].outcome == SubmitOutcome::Accepted);                       // PUBLISH 500
    CHECK(r[1].outcome == SubmitOutcome::Accepted);                       // SCAN 2000 → 2500 used
    CHECK(r[2].outcome == SubmitOutcome::DeferredBudget);                 // residency has no budget left this frame
    CHECK(r[3].outcome == SubmitOutcome::DeferredBudget);
    CHECK(r[4].outcome == SubmitOutcome::DeferredBudget);
    CHECK_EQ(core.Budget().Used(), 2500u);
    core.BeginFrame(2);                                                   // new frame: residency first in line after publish/scan
    core.Retire(FS_JOB_SCAN, 0, true, 1900.0);
    for (uint32_t c = 0; c < kClassCount; ++c) r[c] = tick(static_cast<FsJobClass>(c));
    CHECK(r[1].outcome == SubmitOutcome::Accepted && r[2].outcome == SubmitOutcome::DeferredBudget);
    core.SetFrameBudgetUs(6000); core.BeginFrame(3);
    for (uint32_t c = 0; c < kClassCount; ++c) r[c] = tick(static_cast<FsJobClass>(c));
    // SCAN ring has one free slot (frame 1's job retired); with 6 ms everything but COLD fits
    CHECK(r[0].outcome == SubmitOutcome::Accepted); CHECK(r[1].outcome == SubmitOutcome::Accepted);
    CHECK(r[2].outcome == SubmitOutcome::Accepted); CHECK(r[3].outcome == SubmitOutcome::Accepted); CHECK(r[4].outcome == SubmitOutcome::DeferredBudget);
    CHECK_EQ(core.Budget().Used(), 500u + 2000u + 2000u + 1500u);
    core.BeginFrame(4);
    for (uint32_t c = 0; c < kClassCount; ++c) r[c] = tick(static_cast<FsJobClass>(c));
    // rings now bound the classes: SCAN (2/2), INNER_RESIDENCY (1/2 → accepted), APPEARANCE (1/1), COLD free
    CHECK(r[0].outcome == SubmitOutcome::Accepted); CHECK(r[1].outcome == SubmitOutcome::DeferredRing);
    CHECK(r[2].outcome == SubmitOutcome::Accepted); CHECK(r[3].outcome == SubmitOutcome::DeferredRing); CHECK(r[4].outcome == SubmitOutcome::Accepted);
    CHECK_EQ(core.Ring(FS_JOB_PUBLISH).InFlight(), 4u); CHECK_EQ(core.Stats(FS_JOB_SCAN).deferredRing, 1); CHECK_EQ(core.Stats(FS_JOB_APPEARANCE).deferredRing, 1);
}

int main() {
    TestNormalizeConfig();
    TestClassRing();
    TestTimelineTracker();
    TestBudget();
    TestSchedulerSubmitDeferRetire();
    TestLeaseValidation();
    TestDependencyDeferral();
    TestQuarantine();
    TestRootLastPublish();
    TestGarbageLedger();
    TestScanRequests();
    TestPipelineStore();
    TestTelemetry();
    TestClassOrderBudgetModel();
    if (g_failures == 0) std::printf("executor host tests: all passed\n");
    else std::printf("executor host tests: %d failure(s)\n", g_failures);
    return g_failures == 0 ? 0 : 1;
}
