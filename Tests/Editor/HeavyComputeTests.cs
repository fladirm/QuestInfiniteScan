using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.HeavyCompute;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class HeavyComputeTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "QuestInfiniteScanTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Test]
        public void JobIdentityAndFingerprintMatchFrozenPythonGoldenVector()
        {
            var key = new HeavyComputeJobKey("world-01", "chunk-000042", 7);
            var descriptor = new HeavyComputeBlobDescriptor
            {
                mediaType = HeavyComputeProtocol.ChunkBundleMediaType,
                formatVersion = 1,
                byteLength = 1024,
                sha256 = new string('a', 64)
            };

            Assert.That(HeavyComputeSubmission.TryCreate(key, descriptor, "balanced", true,
                null, out HeavyComputeSubmission submission, out string error), Is.True, error);
            Assert.That(submission.jobId, Is.EqualTo(
                "30b28e11e4d78ea8765a83b544a41c55ccc1397d0a16be9b3700792ee910993c"));
            Assert.That(submission.requestFingerprint, Is.EqualTo(
                "90a6517f476541d6b68905c8b04dda0497fdf188d0630f64614c90e3f988b9ba"));
            string wire = HeavyComputeContract.BuildSubmissionJson(submission);
            Assert.That(wire, Does.Contain("\"warmStart\":null"));
            Assert.That(wire, Does.Contain("\"allowFreshFallback\":true"));
        }

        [Test]
        public async Task NoneBackendLeavesDurableJobOfflineWithoutNetworkWork()
        {
            HeavyComputeQueueStore store = CreateQueue(out HeavyComputeSubmission submission);
            var scheduler = new HeavyComputeJobScheduler(store,
                new NoneHeavyComputeBackend(), (_, _, _) => true);

            Assert.That(await scheduler.PumpOnceAsync(2_000), Is.False);
            Assert.That(store.Snapshot()[0].localState,
                Is.EqualTo(HeavyComputeLocalState.PendingCreate));

            var restarted = new HeavyComputeQueueStore(_root);
            Assert.That(restarted.Snapshot(), Has.Count.EqualTo(1));
            Assert.That(restarted.Snapshot()[0].submission.requestFingerprint,
                Is.EqualTo(submission.requestFingerprint));
        }

        [Test]
        public async Task LanLifecycleStreamsVerifiedArtifactAndSurvivesQueueRestart()
        {
            HeavyComputeQueueStore store = CreateQueue(out HeavyComputeSubmission submission);
            var backend = new RecordingBackend(submission);
            var scheduler = new HeavyComputeJobScheduler(store, backend, (_, _, _) => true);

            Assert.That(await scheduler.PumpOnceAsync(2_000), Is.True); // create
            Assert.That(await scheduler.PumpOnceAsync(2_001), Is.True); // upload
            Assert.That(await scheduler.PumpOnceAsync(2_002), Is.True); // enqueue
            Assert.That(await scheduler.PumpOnceAsync(3_002), Is.True); // succeeded poll

            var restarted = new HeavyComputeQueueStore(_root);
            var resumed = new HeavyComputeJobScheduler(restarted, backend, (_, _, _) => true);
            Assert.That(await resumed.PumpOnceAsync(3_003), Is.True); // download

            HeavyComputeQueueItem ready = restarted.Snapshot()[0];
            Assert.That(ready.localState, Is.EqualTo(HeavyComputeLocalState.Ready));
            Assert.That(File.ReadAllBytes(restarted.GetArtifactPath(submission.jobId)),
                Is.EqualTo(backend.Artifact));
            Assert.That(backend.Calls, Is.EqualTo(5));
        }

        [Test]
        public async Task NewerRevisionSupersedesLateResultBeforeDownload()
        {
            HeavyComputeQueueStore store = CreateQueue(out HeavyComputeSubmission submission);
            var backend = new RecordingBackend(submission);
            bool current = true;
            var scheduler = new HeavyComputeJobScheduler(store, backend,
                (_, _, _) => current);
            await scheduler.PumpOnceAsync(2_000);
            await scheduler.PumpOnceAsync(2_001);
            await scheduler.PumpOnceAsync(2_002);
            await scheduler.PumpOnceAsync(3_002);
            Assert.That(store.Snapshot()[0].localState,
                Is.EqualTo(HeavyComputeLocalState.PendingDownload));

            current = false;
            Assert.That(await scheduler.PumpOnceAsync(3_003), Is.True);
            HeavyComputeQueueItem stale = store.Snapshot()[0];
            Assert.That(stale.localState, Is.EqualTo(HeavyComputeLocalState.Superseded));
            Assert.That(stale.errorCode, Is.EqualTo("superseded_revision"));
            Assert.That(File.Exists(store.GetArtifactPath(submission.jobId)), Is.False);
            Assert.That(backend.DownloadCalls, Is.Zero);
        }

        [Test]
        public async Task TransientFailureBacksOffAndRemainsRetryableAfterRestart()
        {
            HeavyComputeQueueStore store = CreateQueue(out HeavyComputeSubmission submission);
            var backend = new RecordingBackend(submission) { FailCreateOnce = true };
            var scheduler = new HeavyComputeJobScheduler(store, backend, (_, _, _) => true);

            Assert.That(await scheduler.PumpOnceAsync(2_000), Is.True);
            HeavyComputeQueueItem delayed = store.Snapshot()[0];
            Assert.That(delayed.localState, Is.EqualTo(HeavyComputeLocalState.PendingCreate));
            Assert.That(delayed.retryCount, Is.EqualTo(1));
            Assert.That(delayed.nextAttemptUnixMs, Is.EqualTo(3_000));

            var restarted = new HeavyComputeQueueStore(_root);
            var resumed = new HeavyComputeJobScheduler(restarted, backend, (_, _, _) => true);
            Assert.That(await resumed.PumpOnceAsync(2_999), Is.False);
            Assert.That(await resumed.PumpOnceAsync(3_000), Is.True);
            Assert.That(restarted.Snapshot()[0].localState,
                Is.EqualTo(HeavyComputeLocalState.PendingUpload));
        }

        [Test]
        public void LanUrlRejectsCredentialsQueriesAndNonHttpSchemes()
        {
            Assert.That(LanDiffSoupBackend.TryNormalizeBaseUri("http://192.0.2.8:8000",
                out Uri valid, out _), Is.True);
            Assert.That(valid.AbsoluteUri, Is.EqualTo("http://192.0.2.8:8000/"));
            Assert.That(LanDiffSoupBackend.TryNormalizeBaseUri("ftp://host", out _, out _),
                Is.False);
            Assert.That(LanDiffSoupBackend.TryNormalizeBaseUri("http://u:p@host", out _, out _),
                Is.False);
            Assert.That(LanDiffSoupBackend.TryNormalizeBaseUri("http://host/?x=1", out _, out _),
                Is.False);
        }

        [Test]
        public void SchedulerBuildConfigurationIsOfflineByDefaultAndValidatesLanOrigin()
        {
            var host = new GameObject("refinement-scheduler-test");
            try
            {
                var scheduler = host.AddComponent<ChunkRefinementScheduler>();
                Assert.That(scheduler.BackendMode,
                    Is.EqualTo(HeavyComputeBackendMode.None));
                Assert.That(scheduler.TryConfigureBeforeInitialization(
                    HeavyComputeBackendMode.Lan, "ftp://unsafe.invalid",
                    out string invalidError), Is.False);
                Assert.That(invalidError, Is.Not.Empty);
                Assert.That(scheduler.BackendMode,
                    Is.EqualTo(HeavyComputeBackendMode.None));

                Assert.That(scheduler.TryConfigureBeforeInitialization(
                    HeavyComputeBackendMode.Lan, "http://192.0.2.8:8420/", "preview",
                    out string validError), Is.True, validError);
                Assert.That(scheduler.BackendMode,
                    Is.EqualTo(HeavyComputeBackendMode.Lan));
                Assert.That(scheduler.ServerUrl,
                    Is.EqualTo("http://192.0.2.8:8420"));
                Assert.That(scheduler.Profile, Is.EqualTo("preview"));

                Assert.That(scheduler.TryConfigureBeforeInitialization(
                    HeavyComputeBackendMode.None, null, "ultra",
                    out string profileError), Is.False);
                Assert.That(profileError, Does.Contain("profile"));
                Assert.That(scheduler.BackendMode,
                    Is.EqualTo(HeavyComputeBackendMode.Lan));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public async Task OptInLanBackendCompletesRealProtocolV2Lifecycle()
        {
            string serverUrl = Environment.GetEnvironmentVariable("QIS_LAN_SERVER_URL");
            string fixture = Environment.GetEnvironmentVariable("QIS_UNITY_BUNDLE_FIXTURE");
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(fixture))
                Assert.Ignore("Set QIS_LAN_SERVER_URL and QIS_UNITY_BUNDLE_FIXTURE.");
            byte[] bundle = File.ReadAllBytes(fixture);
            var key = new HeavyComputeJobKey("world-bundle", "chunk-000000", 1);
            var descriptor = new HeavyComputeBlobDescriptor
            {
                mediaType = HeavyComputeProtocol.ChunkBundleMediaType,
                formatVersion = HeavyComputeProtocol.ChunkBundleVersion,
                byteLength = bundle.Length,
                sha256 = Digest(bundle)
            };
            Assert.That(HeavyComputeSubmission.TryCreate(key, descriptor, "preview", true,
                null, out HeavyComputeSubmission submission, out string error), Is.True, error);
            var backend = new LanDiffSoupBackend(serverUrl, 30);

            HeavyComputeCapabilities capabilities =
                await backend.GetCapabilitiesAsync(CancellationToken.None);
            Assert.That(capabilities.protocolVersions,
                Does.Contain(HeavyComputeProtocol.Version));
            HeavyComputeJobStatus status = await backend.CreateOrReplayAsync(submission,
                CancellationToken.None);
            Assert.That(status.RemoteState, Is.EqualTo(HeavyComputeRemoteState.AwaitingUpload));
            status = await backend.UploadInputAsync(submission, fixture,
                CancellationToken.None);
            status = await backend.EnqueueAsync(submission, CancellationToken.None);
            for (int poll = 0; poll < 100 &&
                 status.RemoteState != HeavyComputeRemoteState.Succeeded; poll++)
            {
                await Task.Delay(20);
                status = await backend.GetStatusAsync(submission, CancellationToken.None);
            }
            Assert.That(status.RemoteState, Is.EqualTo(HeavyComputeRemoteState.Succeeded),
                status.message);
            string artifact = Path.Combine(_root, "lan-artifact.zip");
            await backend.DownloadArtifactAsync(submission, status.artifactBundle, artifact,
                CancellationToken.None);
            Assert.That(new FileInfo(artifact).Length,
                Is.EqualTo(status.artifactBundle.byteLength));
            Assert.That(Digest(File.ReadAllBytes(artifact)),
                Is.EqualTo(status.artifactBundle.sha256));
        }

        private HeavyComputeQueueStore CreateQueue(out HeavyComputeSubmission submission)
        {
            var store = new HeavyComputeQueueStore(_root);
            var key = new HeavyComputeJobKey("world-test", "chunk-000001", 4);
            string input = store.GetInputPath(key.JobId);
            byte[] bytes = Encoding.ASCII.GetBytes("durable-input-bundle");
            File.WriteAllBytes(input, bytes);
            var descriptor = new HeavyComputeBlobDescriptor
            {
                mediaType = HeavyComputeProtocol.ChunkBundleMediaType,
                formatVersion = HeavyComputeProtocol.ChunkBundleVersion,
                byteLength = bytes.Length,
                sha256 = Digest(bytes)
            };
            Assert.That(HeavyComputeSubmission.TryCreate(key, descriptor, "preview", true,
                null, out submission, out string submissionError), Is.True, submissionError);
            Assert.That(store.TryEnqueue(submission, input, 1_000, out _,
                out string queueError), Is.True, queueError);
            return store;
        }

        private static string Digest(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "")
                .ToLowerInvariant();
        }

        private sealed class RecordingBackend : IHeavyComputeBackend
        {
            private readonly HeavyComputeSubmission _submission;
            internal readonly byte[] Artifact = Encoding.ASCII.GetBytes("diffsoup-artifact");
            internal int Calls;
            internal int DownloadCalls;
            internal bool FailCreateOnce;

            internal RecordingBackend(HeavyComputeSubmission submission)
            {
                _submission = submission;
            }

            public string Name => "recording";
            public bool IsEnabled => true;

            public Task<HeavyComputeCapabilities> GetCapabilitiesAsync(
                CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task<HeavyComputeJobStatus> CreateOrReplayAsync(
                HeavyComputeSubmission submission, CancellationToken cancellationToken)
            {
                Calls++;
                if (FailCreateOnce)
                {
                    FailCreateOnce = false;
                    throw new HeavyComputeBackendException("offline", "offline", true);
                }
                return Task.FromResult(Status("awaiting_upload", 0f));
            }

            public Task<HeavyComputeJobStatus> UploadInputAsync(
                HeavyComputeSubmission submission, string inputPath,
                CancellationToken cancellationToken)
            {
                Calls++;
                Assert.That(File.Exists(inputPath), Is.True);
                return Task.FromResult(Status("awaiting_upload", 0f));
            }

            public Task<HeavyComputeJobStatus> EnqueueAsync(
                HeavyComputeSubmission submission, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(Status("queued", 0f));
            }

            public Task<HeavyComputeJobStatus> GetStatusAsync(
                HeavyComputeSubmission submission, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(Status("succeeded", 1f));
            }

            public Task<HeavyComputeJobStatus> CancelAsync(
                HeavyComputeSubmission submission, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(Status("canceled", 0f));
            }

            public Task DownloadArtifactAsync(HeavyComputeSubmission submission,
                HeavyComputeBlobDescriptor descriptor, string destinationPath,
                CancellationToken cancellationToken)
            {
                Calls++;
                DownloadCalls++;
                Assert.That(descriptor.sha256, Is.EqualTo(Digest(Artifact)));
                File.WriteAllBytes(destinationPath, Artifact);
                return Task.CompletedTask;
            }

            private HeavyComputeJobStatus Status(string state, float progress)
            {
                bool succeeded = state == "succeeded";
                return new HeavyComputeJobStatus
                {
                    schemaVersion = HeavyComputeProtocol.Version,
                    jobId = _submission.jobId,
                    requestFingerprint = _submission.requestFingerprint,
                    key = _submission.key,
                    state = state,
                    progress = progress,
                    createdUnixMs = 1_000,
                    updatedUnixMs = 2_000,
                    message = state,
                    artifactBundle = succeeded ? new HeavyComputeBlobDescriptor
                    {
                        mediaType = HeavyComputeProtocol.DiffSoupArtifactMediaType,
                        formatVersion = HeavyComputeProtocol.DiffSoupArtifactVersion,
                        byteLength = Artifact.Length,
                        sha256 = Digest(Artifact)
                    } : null,
                    errorCode = state == "failed" ? "worker_failed" : null
                };
            }
        }
    }
}
