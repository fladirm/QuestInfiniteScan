using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaCarrierPagerTests
    {
        [Test]
        public void RehydratePublishesLocatorLastAndRetainsPinnedRoot()
        {
            using Fixture fixture = Fixture.Create(SummaryKind.Valid);
            SigmaResidencyKey key = fixture.Key;

            Assert.That(fixture.Pager.StateOf(key),
                Is.EqualTo(SigmaResidencyState.ColdDurable));
            Assert.That(fixture.Pager.StateOf(new SigmaResidencyKey(
                new SigmaCarrierPageCoordinate(99, 100), key.RootContext, 1u)),
                Is.EqualTo(SigmaResidencyState.AbsentInRoot));

            Assert.That(fixture.Pager.BeginRehydrate(
                new[] { fixture.Coordinate }), Is.True);
            Assert.That(fixture.Pager.Activity,
                Is.EqualTo(SigmaPagerActivity.IndexingCold));
            Assert.That(fixture.Store.PinnedRootCount, Is.EqualTo(1));
            AssertColdIndex(fixture.Updates.Batches[0][0], key);
            Assert.That(fixture.Pager.StateOf(key),
                Is.EqualTo(SigmaResidencyState.Loading));

            fixture.Updates.Operations[0].Complete();
            fixture.Pager.Poll();
            WaitUntil(() =>
            {
                fixture.Pager.Poll();
                return fixture.Upload.Batches.Count == 1;
            });
            Assert.That(fixture.Pager.Activity,
                Is.EqualTo(SigmaPagerActivity.Uploading));
            Assert.That(fixture.Store.PinnedRootCount, Is.EqualTo(1));
            Assert.That(fixture.Upload.Batches[0].Pages.Length, Is.EqualTo(1));
            Assert.That(fixture.Upload.Batches[0].Pages[0].CurrentGlobalSlot,
                Is.EqualTo(8u));
            Assert.That(fixture.Upload.Batches[0].Pages[0].ShadowGlobalSlot,
                Is.EqualTo(9u));
            Assert.That(fixture.Pager.StateOf(key),
                Is.EqualTo(SigmaResidencyState.Loading),
                "Decoded/uploaded bytes are not visible before locator publish.");

            fixture.Upload.Operations[0].Complete();
            fixture.Pager.Poll();
            Assert.That(fixture.Pager.Activity,
                Is.EqualTo(SigmaPagerActivity.PublishingLoad));
            Assert.That(fixture.Updates.Batches, Has.Count.EqualTo(2));
            SigmaResidencyUpdate publish = fixture.Updates.Batches[1][0];
            Assert.That(publish.Operation,
                Is.EqualTo(SigmaResidencyOperation.PublishLoaded));
            Assert.That(publish.ExpectedState,
                Is.EqualTo(SigmaResidencyState.ColdDurable));
            Assert.That(publish.State, Is.EqualTo(SigmaResidencyState.HotClean));
            Assert.That(publish.GlobalSlot, Is.EqualTo(8));
            Assert.That(publish.PairedGlobalSlot, Is.EqualTo(9));

            fixture.Updates.Operations[1].Complete();
            fixture.Pager.Poll();
            Assert.That(fixture.Pager.Activity, Is.EqualTo(SigmaPagerActivity.Idle));
            Assert.That(fixture.Pager.StateOf(key),
                Is.EqualTo(SigmaResidencyState.HotClean));
            Assert.That(fixture.Pager.ResidentPairCount, Is.EqualTo(1));
            Assert.That(fixture.Store.PinnedRootCount, Is.Zero);
            Assert.That(fixture.Pager.TryTakeCompletedLoad(
                out SigmaStagedDurablePage completed), Is.True);
            Assert.That(completed.VisibleSlot, Is.Zero);
            Assert.That(completed.Update.Page.Coordinate,
                Is.EqualTo(fixture.Coordinate));
            Assert.That(completed.Update.PageGeneration,
                Is.EqualTo(key.PageGeneration));
            Assert.That(fixture.Pager.TryTakeCompletedLoad(out _), Is.False);
        }

        [Test]
        public void EveryLifetimeLeaseAndGpuCompletionBlocksSlotReuse()
        {
            using Fixture fixture = Fixture.CreateLoaded(SummaryKind.Valid);
            SigmaResidencyKey key = fixture.Key;

            foreach (SigmaResidencyLeaseKind kind in
                (SigmaResidencyLeaseKind[])Enum.GetValues(
                    typeof(SigmaResidencyLeaseKind)))
            {
                using (fixture.Pager.AcquireLease(key, kind))
                    Assert.That(fixture.Pager.TryBeginEviction(key), Is.False,
                        kind.ToString());
            }

            var pending = new ManualOperation();
            fixture.Pager.SetLastReader(key, pending);
            Assert.That(fixture.Pager.TryBeginEviction(key), Is.False);
            fixture.Pager.SetLastReader(key,
                SigmaCompletedResidencyCompletion.Instance);
            fixture.Pager.SetLastWriter(key, pending);
            Assert.That(fixture.Pager.TryBeginEviction(key), Is.False);
            fixture.Pager.SetLastWriter(key,
                SigmaCompletedResidencyCompletion.Instance);

            Assert.That(fixture.Pager.TryBeginEviction(key), Is.True);
            Assert.That(fixture.Pager.Activity,
                Is.EqualTo(SigmaPagerActivity.PublishingEviction));
            SigmaResidencyUpdate update = fixture.Updates.Batches[^1][0];
            Assert.That(update.Operation, Is.EqualTo(SigmaResidencyOperation.Evict));
            Assert.That(update.ExpectedState,
                Is.EqualTo(SigmaResidencyState.HotClean));
            Assert.That(update.State,
                Is.EqualTo(SigmaResidencyState.ColdDurable));
            fixture.Updates.Operations[^1].Complete();
            fixture.Pager.Poll();
            Assert.That(fixture.Pager.ResidentPairCount, Is.Zero);
            Assert.That(fixture.Pager.StateOf(key),
                Is.EqualTo(SigmaResidencyState.ColdDurable));
        }

        [Test]
        public void FaultedGpuCompletionQuarantinesInsteadOfRecycling()
        {
            using Fixture fixture = Fixture.CreateLoaded(SummaryKind.Valid);
            var fault = new ManualOperation();
            fault.Fault("injected reader fault");
            fixture.Pager.SetLastReader(fixture.Key, fault);

            Assert.That(fixture.Pager.TryBeginEviction(fixture.Key), Is.False);
            Assert.That(fixture.Pager.StateOf(fixture.Key),
                Is.EqualTo(SigmaResidencyState.Quarantined));
            Assert.That(fixture.Pager.ResidentPairCount, Is.Zero);
        }

        [Test]
        public void PendingColdOwnerDefersResourceRetirementUntilCompletion()
        {
            var pending = new ManualOperation();
            int released = 0;
            int before = SigmaGpuRetirement.PendingCount;

            SigmaGpuRetirement.Retire(pending, () => released++,
                "N5 pending-owner fixture");
            Assert.That(SigmaGpuRetirement.PendingCount,
                Is.EqualTo(before + 1));
            SigmaGpuRetirement.Poll();
            Assert.That(released, Is.Zero);

            pending.Complete();
            SigmaGpuRetirement.Poll();
            Assert.That(released, Is.EqualTo(1));
            Assert.That(SigmaGpuRetirement.PendingCount, Is.EqualTo(before));
        }

        [Test]
        public void ProtectedResidentGenerationCannotBeSelectedForEviction()
        {
            using Fixture fixture = Fixture.CreateLoaded(SummaryKind.Valid);
            var protectedKeys = new HashSet<SigmaResidencyKey>
            {
                fixture.Key,
            };
            Assert.That(fixture.Pager.TryBeginAnyEviction(protectedKeys,
                out _), Is.False);
            Assert.That(fixture.Pager.StateOf(fixture.Key),
                Is.EqualTo(SigmaResidencyState.HotClean));
            Assert.That(fixture.Pager.TryBeginAnyEviction(out _), Is.True);
            fixture.Updates.Operations[^1].Complete();
            fixture.Pager.Poll();
            Assert.That(fixture.Pager.Activity,
                Is.EqualTo(SigmaPagerActivity.Idle));
        }

        [Test]
        public void SubmissionUncertaintyQuarantinesReservedPairAndRootUnpins()
        {
            using Fixture fixture = Fixture.Create(SummaryKind.Valid,
                throwOnFirstUpdate: true);

            Assert.Throws<InvalidOperationException>(() =>
                fixture.Pager.BeginRehydrate(new[] { fixture.Coordinate }));
            Assert.That(fixture.Pager.Activity,
                Is.EqualTo(SigmaPagerActivity.Faulted));
            Assert.That(fixture.Pager.StateOf(fixture.Key),
                Is.EqualTo(SigmaResidencyState.Quarantined));
            Assert.That(fixture.Store.PinnedRootCount, Is.Zero);
            Assert.That(fixture.Pager.BeginRehydrate(
                new[] { fixture.Coordinate }), Is.False,
                "A quarantined uncertain submission cannot recycle its pair.");
        }

        [TestCase(SummaryKind.Valid)]
        [TestCase(SummaryKind.Missing)]
        [TestCase(SummaryKind.Stale)]
        [TestCase(SummaryKind.Corrupt)]
        public void MissingStaleOrCorruptSummaryNeverOmitsColdLoad(
            SummaryKind kind)
        {
            using Fixture fixture = Fixture.Create(kind);
            Assert.That(fixture.Pager.BeginRehydrate(
                new[] { fixture.Coordinate }), Is.True);
            fixture.CompleteLoad();
            Assert.That(fixture.Pager.StateOf(fixture.Key),
                Is.EqualTo(SigmaResidencyState.HotClean));

            bool eviction = fixture.Pager.TryBeginEviction(fixture.Key);
            Assert.That(eviction, Is.EqualTo(kind == SummaryKind.Valid),
                "Only a complete verified summary may authorize eviction; " +
                "all summary failures still conservatively rehydrate.");
            if (eviction)
            {
                fixture.Updates.Operations[^1].Complete();
                fixture.Pager.Poll();
            }
        }

        [Test]
        public void StaleRootLoadCannotSatisfyNewRootContext()
        {
            using Fixture fixture = Fixture.CreateLoaded(SummaryKind.Valid,
                pageCapacity: 4);
            SigmaResidencyKey oldKey = fixture.Key;
            SigmaCarrierPageCoordinate other =
                new SigmaCarrierPageCoordinate(-20, 40);
            fixture.CommitAdditional(other, 2u, 2UL);
            Assert.That(fixture.Pager.StateOf(oldKey),
                Is.EqualTo(SigmaResidencyState.HotClean),
                "An unchanged exact PageRecord remains a valid cache entry " +
                "after the selected durable root advances.");
            fixture.CommitAdditional(fixture.Coordinate, 2u, 3UL);
            var newKey = new SigmaResidencyKey(fixture.Coordinate, 2UL, 2u);

            Assert.That(fixture.Pager.StateOf(oldKey),
                Is.EqualTo(SigmaResidencyState.HotClean));
            Assert.That(fixture.Pager.StateOf(newKey),
                Is.EqualTo(SigmaResidencyState.ColdDurable));
            Assert.That(fixture.Pager.BeginRehydrate(
                new[] { fixture.Coordinate }), Is.True);
            Assert.That(fixture.Updates.Batches[^1][0].Key.RootContext,
                Is.EqualTo(2UL));
            fixture.CompleteLoad();
        }

        [Test]
        public void MissingDurablePageBlobQuarantinesLoadAndPreservesHead()
        {
            using Fixture fixture = Fixture.Create(SummaryKind.Valid);
            SigmaDurableHash selectedHead = fixture.Store.HeadHash;
            fixture.DeletePageBlob();

            Assert.That(fixture.Pager.BeginRehydrate(
                new[] { fixture.Coordinate }), Is.True);
            fixture.Updates.Operations[^1].Complete();
            fixture.Pager.Poll();
            WaitUntil(() =>
            {
                fixture.Pager.Poll();
                return fixture.Pager.Activity == SigmaPagerActivity.Faulted;
            });

            Assert.That(fixture.Store.HeadHash, Is.EqualTo(selectedHead));
            Assert.That(fixture.Store.Head.Revision, Is.EqualTo(1UL));
            Assert.That(fixture.Pager.StateOf(fixture.Key),
                Is.EqualTo(SigmaResidencyState.Quarantined));
            StringAssert.Contains("PageBlob", fixture.Pager.Fault);
        }

        [Test]
        public void TwoBindingBanksShareOneCacheAndFreeOneWholeNativeTarget()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "sigma-n5r-two-bank-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                var coordinates = new[]
                {
                    new SigmaCarrierPageCoordinate(-3, 7),
                    new SigmaCarrierPageCoordinate(11, -13),
                    new SigmaCarrierPageCoordinate(17, 19),
                };
                var store = new SigmaDurableStore(path);
                var pages = new SigmaDurablePageUpdate[coordinates.Length];
                for (int index = 0; index < pages.Length; ++index)
                    pages[index] = MakeUpdate(coordinates[index], 1u,
                        SummaryKind.Valid, out _);
                store.Commit(new SigmaDurableCommitRequest(1UL, pages));

                var runtime = new SigmaCarrierRuntimeState();
                SigmaCarrierReadBatch[] targets =
                {
                    Target(0, 4, 0, runtime),
                    Target(1, 4, 2, runtime),
                };
                var upload = new FakeUploadBackend();
                var updates = new FakeUpdateBackend();
                using var pager = new SigmaCarrierPager(store, targets,
                    upload, updates);

                Assert.That(pager.PairCapacity, Is.EqualTo(4));
                Assert.That(pager.BeginRehydrate(new[]
                {
                    coordinates[0], coordinates[1],
                }), Is.True);
                CompleteLoad(pager, upload, updates);
                Assert.That(updates.Targets[0].SegmentIndex, Is.EqualTo(1),
                    "Cold load should prefer the second execution bank.");
                Assert.That(upload.Targets[0].SegmentIndex, Is.EqualTo(1));
                Assert.That(upload.Batches[0].Pages[0].CurrentGlobalSlot,
                    Is.EqualTo(4u));
                Assert.That(upload.Batches[0].Pages[1].CurrentGlobalSlot,
                    Is.EqualTo(6u));

                Assert.That(pager.BeginRehydrate(new[] { coordinates[2] }),
                    Is.True);
                CompleteLoad(pager, upload, updates);
                Assert.That(upload.Targets[1].SegmentIndex, Is.EqualTo(0));
                Assert.That(upload.Batches[1].Pages[0].CurrentGlobalSlot,
                    Is.EqualTo(0u));
                Assert.That(pager.ResidentPairCount, Is.EqualTo(3));
                Assert.That(pager.FreePairCountInSegment(0), Is.EqualTo(1));
                Assert.That(pager.FreePairCountInSegment(1), Is.Zero);

                var protectedKeys = new HashSet<SigmaResidencyKey>();
                for (int index = 0; index < 2; ++index)
                {
                    SigmaCarrierPageCoordinate coordinate =
                        coordinates[index];
                    Assert.That(store.TryGetPageRecord(coordinate,
                        out SigmaDurablePageRecord record), Is.True);
                    protectedKeys.Add(new SigmaResidencyKey(coordinate,
                        record.Revision, record.PageGeneration));
                }
                Assert.That(pager.TryBeginNativeTargetEviction(2,
                    protectedKeys, out SigmaResidencyKey evicted), Is.True);
                Assert.That(evicted.Coordinate, Is.EqualTo(coordinates[2]));
                Assert.That(updates.Targets[^1].SegmentIndex, Is.EqualTo(0));
                updates.Operations[^1].Complete();
                pager.Poll();
                Assert.That(pager.FreePairCountInSegment(0), Is.EqualTo(2));
                Assert.That(pager.FreePairCountInSegment(1), Is.Zero);
            }
            finally
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
        }

        [Test]
        public void PartialAppendTailPinsNativeTargetAndItsEvictionBank()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "sigma-n5r-append-tail-bank-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                var tail = new SigmaCarrierPageCoordinate(0, 0);
                var other = new SigmaCarrierPageCoordinate(-3, 7);
                var store = new SigmaDurableStore(path);
                store.Commit(new SigmaDurableCommitRequest(1UL, new[]
                {
                    MakeUpdate(tail, 1u, SummaryKind.Valid, out _),
                    MakeUpdate(other, 1u, SummaryKind.Valid, out _),
                }));

                var runtime = new SigmaCarrierRuntimeState
                {
                    DurableLogicalExtent = 1u,
                    DurableRevision = 1UL,
                };
                SigmaCarrierReadBatch[] targets =
                {
                    Target(0, 4, 0, runtime),
                    Target(1, 4, 2, runtime),
                };
                var upload = new FakeUploadBackend();
                var updates = new FakeUpdateBackend();
                using var pager = new SigmaCarrierPager(store, targets,
                    upload, updates);

                Assert.That(pager.BeginRehydrate(new[] { tail, other }),
                    Is.True);
                CompleteLoad(pager, upload, updates);
                Assert.That(pager.FreePairCountInSegment(0), Is.EqualTo(2));
                Assert.That(pager.FreePairCountInSegment(1), Is.Zero);
                Assert.That(pager.SelectNativeTarget().SegmentIndex,
                    Is.EqualTo(1),
                    "A partial durable append page must be cloned from the " +
                    "bank that owns its exact resident generation.");
                Assert.That(pager.NativeTargetFreePairCount, Is.Zero);

                Assert.That(store.TryGetPageRecord(tail,
                    out SigmaDurablePageRecord tailRecord), Is.True);
                var protectedKeys = new HashSet<SigmaResidencyKey>
                {
                    new(tail, tailRecord.Revision,
                        tailRecord.PageGeneration),
                };
                Assert.That(pager.TryBeginNativeTargetEviction(1,
                    protectedKeys, out SigmaResidencyKey evicted), Is.True);
                Assert.That(evicted.Coordinate, Is.EqualTo(other));
                Assert.That(updates.Targets[^1].SegmentIndex, Is.EqualTo(1),
                    "Native-capacity eviction must free the selected tail bank, " +
                    "not an unrelated bank with more free pairs.");
                updates.Operations[^1].Complete();
                pager.Poll();
                Assert.That(pager.FreePairCountInSegment(1), Is.EqualTo(1));
                Assert.That(pager.SelectNativeTarget().SegmentIndex,
                    Is.EqualTo(1));
            }
            finally
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
        }

        private static void AssertColdIndex(SigmaResidencyUpdate update,
            SigmaResidencyKey key)
        {
            Assert.That(update.Key, Is.EqualTo(key));
            Assert.That(update.Operation,
                Is.EqualTo(SigmaResidencyOperation.Upsert));
            Assert.That(update.State,
                Is.EqualTo(SigmaResidencyState.ColdDurable));
            Assert.That(update.ResidentGeneration, Is.Zero);
            Assert.That(update.GlobalSlot, Is.EqualTo(-1));
        }

        private static void WaitUntil(Func<bool> condition)
        {
            Assert.That(SpinWait.SpinUntil(condition,
                TimeSpan.FromSeconds(10)), Is.True,
                "Timed out waiting for the bounded background decode.");
        }

        private static void CompleteLoad(SigmaCarrierPager pager,
            FakeUploadBackend upload, FakeUpdateBackend updates)
        {
            int priorUploads = upload.Operations.Count;
            updates.Operations[^1].Complete();
            pager.Poll();
            WaitUntil(() =>
            {
                pager.Poll();
                return upload.Operations.Count > priorUploads;
            });
            upload.Operations[^1].Complete();
            pager.Poll();
            Assert.That(pager.Activity,
                Is.EqualTo(SigmaPagerActivity.PublishingLoad));
            updates.Operations[^1].Complete();
            pager.Poll();
            Assert.That(pager.Activity, Is.EqualTo(SigmaPagerActivity.Idle));
        }

        public enum SummaryKind
        {
            Valid,
            Missing,
            Stale,
            Corrupt,
        }

        private sealed class Fixture : IDisposable
        {
            private readonly string _path;

            private Fixture(string path, SigmaDurableStore store,
                SigmaCarrierPager pager, FakeUploadBackend upload,
                FakeUpdateBackend updates, SigmaCarrierPageCoordinate coordinate,
                uint pageGeneration)
            {
                _path = path;
                Store = store;
                Pager = pager;
                Upload = upload;
                Updates = updates;
                Coordinate = coordinate;
                Assert.That(store.TryGetPageRecord(coordinate,
                    out SigmaDurablePageRecord record), Is.True);
                Key = new SigmaResidencyKey(coordinate, record.Revision,
                    pageGeneration);
            }

            internal SigmaDurableStore Store { get; }
            internal SigmaCarrierPager Pager { get; }
            internal FakeUploadBackend Upload { get; }
            internal FakeUpdateBackend Updates { get; }
            internal SigmaCarrierPageCoordinate Coordinate { get; }
            internal SigmaResidencyKey Key { get; private set; }

            internal static Fixture Create(SummaryKind kind,
                bool throwOnFirstUpdate = false, int pageCapacity = 2)
            {
                string path = Path.Combine(Path.GetTempPath(),
                    "sigma-n5r-pager-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(path);
                try
                {
                    var store = new SigmaDurableStore(path);
                    SigmaCarrierPageCoordinate coordinate =
                        new SigmaCarrierPageCoordinate(-7, 19);
                    SigmaDurablePageUpdate update = MakeUpdate(coordinate, 1u,
                        kind, out SigmaDurableHash summaryHash);
                    store.Commit(new SigmaDurableCommitRequest(1UL,
                        new[] { update }));
                    if (kind == SummaryKind.Corrupt)
                        CorruptObject(path, summaryHash);
                    var upload = new FakeUploadBackend();
                    var updates = new FakeUpdateBackend
                    {
                        ThrowOnNextBegin = throwOnFirstUpdate,
                    };
                    SigmaCarrierReadBatch target = Target(pageCapacity);
                    var pager = new SigmaCarrierPager(store, target, upload,
                        updates);
                    return new Fixture(path, store, pager, upload, updates,
                        coordinate, 1u);
                }
                catch
                {
                    Directory.Delete(path, true);
                    throw;
                }
            }

            internal static Fixture CreateLoaded(SummaryKind kind,
                int pageCapacity = 2)
            {
                Fixture fixture = Create(kind, pageCapacity: pageCapacity);
                Assert.That(fixture.Pager.BeginRehydrate(
                    new[] { fixture.Coordinate }), Is.True);
                fixture.CompleteLoad();
                return fixture;
            }

            internal void CompleteLoad()
            {
                int priorUploads = Upload.Operations.Count;
                if (Pager.Activity == SigmaPagerActivity.IndexingCold)
                {
                    Updates.Operations[^1].Complete();
                    Pager.Poll();
                }
                WaitUntil(() =>
                {
                    Pager.Poll();
                    return Upload.Operations.Count > priorUploads;
                });
                Upload.Operations[^1].Complete();
                Pager.Poll();
                Assert.That(Pager.Activity,
                    Is.EqualTo(SigmaPagerActivity.PublishingLoad));
                Updates.Operations[^1].Complete();
                Pager.Poll();
                Assert.That(Pager.Activity, Is.EqualTo(SigmaPagerActivity.Idle));
            }

            internal void CommitAdditional(SigmaCarrierPageCoordinate coordinate,
                uint generation, ulong revision)
            {
                Store.Commit(new SigmaDurableCommitRequest(revision,
                    new[] { MakeUpdate(coordinate, generation,
                        SummaryKind.Valid, out _) }));
            }

            internal void DeletePageBlob()
            {
                Assert.That(Store.TryGetPageRecord(Coordinate,
                    out SigmaDurablePageRecord record), Is.True);
                string hash = record.PageBlobHash.ToString();
                string path = Path.Combine(_path, "objects",
                    hash.Substring(0, 2), hash + ".s5o");
                Assert.That(File.Exists(path), Is.True);
                File.Delete(path);
            }

            public void Dispose()
            {
                Pager.Dispose();
                if (Directory.Exists(_path)) Directory.Delete(_path, true);
            }
        }

        private sealed class ManualOperation : ISigmaColdBatchOperation
        {
            private SigmaGpuCompletionStatus _status =
                SigmaGpuCompletionStatus.Pending;
            private string _error;
            internal bool Disposed { get; private set; }
            internal void Complete()
            {
                _status = SigmaGpuCompletionStatus.Complete;
                _error = null;
            }
            internal void Fault(string error)
            {
                _status = SigmaGpuCompletionStatus.Faulted;
                _error = error;
            }
            public SigmaGpuCompletionStatus Poll(out string error)
            {
                error = _error;
                return _status;
            }
            public void Dispose() => Disposed = true;
        }

        private sealed class FakeUploadBackend : ISigmaColdUploadBackend
        {
            internal List<SigmaColdDecodedBatch> Batches { get; } = new();
            internal List<SigmaCarrierReadBatch> Targets { get; } = new();
            internal List<ManualOperation> Operations { get; } = new();
            public ISigmaColdBatchOperation Begin(SigmaCarrierReadBatch target,
                SigmaColdDecodedBatch batch)
            {
                Targets.Add(target);
                Batches.Add(batch);
                var operation = new ManualOperation();
                Operations.Add(operation);
                return operation;
            }
        }

        private sealed class FakeUpdateBackend : ISigmaResidencyUpdateBackend
        {
            internal bool ThrowOnNextBegin { get; set; }
            internal List<List<SigmaResidencyUpdate>> Batches { get; } = new();
            internal List<SigmaCarrierReadBatch> Targets { get; } = new();
            internal List<ManualOperation> Operations { get; } = new();
            public ISigmaColdBatchOperation Begin(
                SigmaCarrierReadBatch target,
                IReadOnlyList<SigmaResidencyUpdate> updates)
            {
                Targets.Add(target);
                Batches.Add(new List<SigmaResidencyUpdate>(updates));
                if (ThrowOnNextBegin)
                {
                    ThrowOnNextBegin = false;
                    throw new InvalidOperationException(
                        "Injected uncertain locator submission.");
                }
                var operation = new ManualOperation();
                Operations.Add(operation);
                return operation;
            }
        }

        private static SigmaCarrierReadBatch Target(int pageCapacity) =>
            new(0, pageCapacity, 4, null, null, null, null, null, null,
                null, null, 1024, 32, new SigmaCarrierRuntimeState());

        private static SigmaCarrierReadBatch Target(int segmentIndex,
            int pageCapacity, int pairFirst,
            SigmaCarrierRuntimeState runtimeState) =>
            new(segmentIndex, pageCapacity, pairFirst, null, null, null, null,
                null, null, null, null, 1024, 32, runtimeState);

        private static SigmaDurablePageUpdate MakeUpdate(
            SigmaCarrierPageCoordinate coordinate, uint generation,
            SummaryKind kind, out SigmaDurableHash summaryHash)
        {
            SigmaDecodedPage page = MakePage(coordinate, generation);
            byte[] summary = SigmaQuerySupportSummary.FromPage(page,
                generation,
                SigmaQuerySupportFlags.Verified |
                SigmaQuerySupportFlags.MayContribute).Encode();
            summaryHash = SigmaDurableHash.Compute(summary);
            SigmaQuerySupportFlags flags = SigmaQuerySupportFlags.MayContribute;
            uint supportGeneration = generation;
            byte[] stagedSummary = null;
            switch (kind)
            {
                case SummaryKind.Valid:
                case SummaryKind.Corrupt:
                    flags |= SigmaQuerySupportFlags.Verified;
                    stagedSummary = summary;
                    break;
                case SummaryKind.Missing:
                    summaryHash = SigmaDurableHash.Zero;
                    break;
                case SummaryKind.Stale:
                    flags |= SigmaQuerySupportFlags.Verified;
                    supportGeneration = checked(generation + 1u);
                    summaryHash = SigmaDurableHash.Zero;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
            var receipt = new SigmaQuerySupportReceipt(supportGeneration,
                flags, summaryHash);
            return new SigmaDurablePageUpdate(page, generation, receipt,
                SigmaCarrierCodec.EncodePage(page), stagedSummary);
        }

        private static SigmaDecodedPage MakePage(
            SigmaCarrierPageCoordinate coordinate, uint generation)
        {
            var samples = new SigmaS16[SigmaDecodedPage.SampleCount];
            var lanes = new long[SigmaS16.LaneCount];
            lanes[0] = 17L * generation;
            samples[0] = SigmaS16.FromArray(lanes);
            var words = new uint[SigmaCarrierRepresentationRecord.WordCount];
            words[0] = 1u;
            words[4] = 0u;
            words[5] = (uint)(SigmaNativeGaugeCellFlags.Active |
                SigmaNativeGaugeCellFlags.Normalized);
            words[6] = Convert.ToUInt32(
                SigmaGeneratedFrame.ChiFingerprint.Substring(0, 8), 16);
            words[7] = Convert.ToUInt32(
                SigmaGeneratedFrame.KappaFingerprint.Substring(0, 8), 16);
            words[8] = (uint)(SigmaNativeCertificateFlags.Valid |
                SigmaNativeCertificateFlags.Minimized);
            words[11] = generation;
            return new SigmaDecodedPage(coordinate, generation, generation,
                0UL, 1u, generation, generation, 3u, 1u,
                new[] { new SigmaCarrierRepresentationRecord(0, words) },
                samples);
        }

        private static void CorruptObject(string root,
            SigmaDurableHash hash)
        {
            string text = hash.ToString();
            string path = Path.Combine(root, "objects", text.Substring(0, 2),
                text + ".s5o");
            byte[] bytes = File.ReadAllBytes(path);
            bytes[0] ^= 0x80;
            File.WriteAllBytes(path, bytes);
        }
    }
}
