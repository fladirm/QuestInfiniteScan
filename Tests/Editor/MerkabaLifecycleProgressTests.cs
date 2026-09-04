using System;
using System.Collections.Generic;
using System.IO;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaLifecycleProgressTests
    {
        [Test]
        public void PauseAndDisableUseOneOrderedLifecycleAuthority()
        {
            string scanner = Source("Runtime/Core/RoomScanner.cs");
            string pause = Slice(scanner,
                "private void OnApplicationPause(bool paused)",
                "private void OnDisable()");
            string transition = Slice(scanner,
                "private async Task ApplyApplicationPauseAsync",
                "private void BeginDisableTeardown()");
            Assert.That(pause, Does.Contain("_resumeAfterPause = IsScanning || IsScanStarting"));
            Assert.That(transition, Does.Contain(
                "if (!await QuiesceScanningAsync()) return;"));
            Assert.That(transition, Does.Contain(
                "SuspendEnvironmentDepthForApplicationPause()"));
            Assert.That(transition, Does.Contain(
                "RestoreEnvironmentDepthAfterApplicationResumeAsync()"));
            Assert.That(transition, Does.Contain("await StartScanningAsync();"));
            Assert.That(transition.IndexOf("QuiesceScanningAsync()",
                    StringComparison.Ordinal),
                Is.LessThan(transition.IndexOf("await StartScanningAsync();",
                    StringComparison.Ordinal)));
            Assert.That(transition.IndexOf(
                    "SuspendEnvironmentDepthForApplicationPause()",
                    StringComparison.Ordinal),
                Is.LessThan(transition.IndexOf(
                    "RestoreEnvironmentDepthAfterApplicationResumeAsync()",
                    StringComparison.Ordinal)));
            Assert.That(transition.IndexOf(
                    "RestoreEnvironmentDepthAfterApplicationResumeAsync()",
                    StringComparison.Ordinal),
                Is.LessThan(transition.IndexOf("await StartScanningAsync();",
                    StringComparison.Ordinal)));

            string teardown = Slice(scanner,
                "private async Task DisableTeardownCoreAsync",
                "private uint NextLifecycleGeneration()");
            int quiesce = teardown.IndexOf("await QuiesceScanningAsync()",
                StringComparison.Ordinal);
            int suspend = teardown.IndexOf("BeginGpuSubmissionQuiesce()",
                StringComparison.Ordinal);
            int retire = teardown.IndexOf("RetireSubmittedGpuWorkAsync()",
                StringComparison.Ordinal);
            int release = teardown.IndexOf(
                "release?.Invoke()",
                StringComparison.Ordinal);
            Assert.That(quiesce, Is.GreaterThanOrEqualTo(0));
            Assert.That(suspend, Is.GreaterThan(quiesce));
            Assert.That(retire, Is.GreaterThan(suspend));
            Assert.That(release, Is.GreaterThan(retire));
            Assert.That(teardown, Does.Not.Contain("if (_destroyed) return;"));
            int capture = teardown.IndexOf(
                "CaptureOwnedGpuResourceRelease()", StringComparison.Ordinal);
            Assert.That(capture, Is.GreaterThan(quiesce));
            Assert.That(capture, Is.LessThan(suspend));
        }

        [Test]
        public void OnlyNewSessionMayCreateTheRoomAnchor()
        {
            string scanner = Source("Runtime/Core/RoomScanner.cs");
            string ensure = Slice(scanner,
                "private async Task EnsureRoomAnchorAsync()",
                "private void UpdateFineActionBoundary()");
            Assert.That(ensure, Does.Contain(
                "EnsureSessionAnchorAsync(requiredUuid,\n" +
                "                    false)"));
            Assert.That(ensure, Does.Not.Contain("IsRoomLoaded"));
            Assert.That(ensure, Does.Contain(
                "Create or open a scan session before starting."));
            Assert.That(ensure, Does.Contain("Room anchor not localized"));

            string create = Slice(scanner,
                "public async Task NewClearAsync()",
                "public async Task<bool> ExportGlbAsync()");
            Assert.That(create, Does.Contain(
                "EnsureSessionAnchorAsync(Guid.Empty,\n" +
                "                        true)"));
            Assert.That(create, Does.Contain("BeginNewSessionAsync("));

            string manager = Source("Runtime/Core/RoomAnchorManager.cs");
            string admission = Slice(manager,
                "internal async Task<bool> EnsureSessionAnchorAsync(",
                "/// <summary>\n        /// Creates an");
            Assert.That(admission, Does.Contain(
                "CreateAndSaveSpatialAnchorAsync"));
            Assert.That(admission, Does.Contain(
                "LoadSpatialAnchorAsync(requiredUuid)"));
            Assert.That(admission, Does.Contain(
                "requiredUuid == Guid.Empty && !allowCreate"));
            Assert.That(admission, Does.Contain(
                "RoomSpaceRoot.WaitForAnchorBindAsync("));
            Assert.That(admission, Does.Not.Contain("IsRoomLoaded"));
        }

        [Test]
        public void ResumeLocalizesExactAnchorBeforeRestoringSensors()
        {
            string scanner = Source("Runtime/Core/RoomScanner.cs");
            string transition = Slice(scanner,
                "private async Task ApplyApplicationPauseAsync",
                "private void BeginDisableTeardown()");
            int anchor = transition.IndexOf(
                "EnsureSessionAnchorAsync(\n" +
                "                            _resumeAnchorUuid, false)",
                StringComparison.Ordinal);
            int depth = transition.IndexOf(
                "RestoreEnvironmentDepthAfterApplicationResumeAsync()",
                StringComparison.Ordinal);
            Assert.That(anchor, Is.GreaterThanOrEqualTo(0));
            Assert.That(depth, Is.GreaterThan(anchor));
            Assert.That(transition, Does.Contain(
                "LastScanStartError = \"Room anchor not localized\""));
            Assert.That(transition, Does.Not.Contain(
                "EnsureSessionAnchorAsync(Guid.Empty"));
        }

        [Test]
        public void ArtifactLocalizationCannotReplaceTheScannerAnchor()
        {
            string manager = Source("Runtime/Core/RoomAnchorManager.cs");
            string artifact = Slice(manager,
                "LocalizeArtifactAnchorAsync(Guid uuid)",
                "public async Task<bool> EraseSpatialAnchorAsync");
            Assert.That(artifact, Does.Not.Contain(
                "_activeSpatialAnchor = anchor"));
            Assert.That(artifact, Does.Not.Contain(
                "RoomSpaceRoot.WaitForAnchorBindAsync"));
            Assert.That(artifact, Does.Contain(
                "return (anchor.transform, true)"));
        }

        [Test]
        public void PreSuspensionStorageCallbacksCannotSubmitAfterQuiesce()
        {
            string storage = Source(
                "Runtime/Merkaba/MerkabaGrid.Storage.cs");
            string gpu = Source("Runtime/Merkaba/MerkabaGrid.Gpu.cs");
            string pump = Slice(storage, "private void PumpStorage()",
                "private void ApplySampledCounters");
            int callbackGate = pump.IndexOf(
                "if (_storageReplacementPending || !GpuSubmissionAllowed ||",
                pump.IndexOf("AsyncGPUReadback.Request", StringComparison.Ordinal),
                StringComparison.Ordinal);
            Assert.That(callbackGate, Is.GreaterThanOrEqualTo(0));
            foreach (string submission in new[]
                     {
                         "BeginLoadAddressReadback()",
                         "BeginWritebackReadback(",
                         "SelectEvictionVictims("
                     })
            {
                Assert.That(pump.IndexOf(submission, StringComparison.Ordinal),
                    Is.GreaterThan(callbackGate), submission);
            }

            foreach ((string begin, string end) in new[]
                     {
                         ("private void BeginLoadAddressReadback()",
                          "private void CompleteStorageTasks()"),
                         ("private void CompleteStorageTasks()",
                          "private void SubmitLoadedTiles("),
                         ("private void SubmitLoadedTiles(",
                          "private void UploadLoadAddresses("),
                         ("private void BeginWritebackReadback(",
                          "internal void CaptureStorageMetrics(")
                     })
            {
                Assert.That(Slice(storage, begin, end),
                    Does.Contain("GpuSubmissionAllowed"), begin);
            }
            string acknowledge = Slice(storage,
                "private void AcknowledgeLoadRequests()",
                "private void BeginWritebackReadback(");
            Assert.That(acknowledge, Does.Contain("_loadAcknowledgePending"));
            Assert.That(acknowledge.IndexOf("GpuSubmissionAllowed",
                    StringComparison.Ordinal),
                Is.LessThan(acknowledge.IndexOf("SetData",
                    StringComparison.Ordinal)));
            Assert.That(storage, Does.Contain(
                "_deferredWritebackFailureCount"));

            string beginQuiesce = Slice(gpu,
                "internal void BeginGpuSubmissionQuiesce()",
                "internal void ResumeGpuSubmission()");
            Assert.That(beginQuiesce, Does.Contain(
                "_gpuSubmissionSuspended = true"));
            foreach (string method in new[]
                     {
                         "internal void SelectEvictionVictims",
                         "internal void AcknowledgeWritebackBatch",
                         "internal void FailWritebackBatch",
                         "internal void InstallLoadedTiles",
                         "internal void FailLoadedTiles",
                         "internal void RegisterLoadedTileAddresses"
                     })
            {
                int start = gpu.IndexOf(method, StringComparison.Ordinal);
                int next = gpu.IndexOf("\n        internal ", start + 1,
                    StringComparison.Ordinal);
                string body = next > start
                    ? gpu.Substring(start, next - start)
                    : gpu.Substring(start);
                Assert.That(body, Does.Contain(
                    "if (!GpuSubmissionAllowed) return;"), method);
            }
        }

        [Test]
        public void RetiredCapturedResourcesReleaseEvenAfterOwnerDestruction()
        {
            string scanner = Source("Runtime/Core/RoomScanner.cs");
            string teardown = Slice(scanner,
                "private async Task DisableTeardownCoreAsync",
                "private Action CaptureOwnedGpuResourceRelease()");
            Assert.That(teardown, Does.Contain(
                "await _grid.RetireSubmittedGpuWorkAsync()"));
            Assert.That(teardown, Does.Contain("release?.Invoke()"));
            Assert.That(teardown, Does.Not.Contain("if (_destroyed) return"));
            Assert.That(teardown, Does.Contain(
                "if (!ReferenceEquals(_grid, null))"));

            foreach (string path in new[]
                     {
                         "Runtime/Core/DepthCapture.cs",
                         "Runtime/Merkaba/MerkabaIntegrator.cs",
                         "Runtime/Merkaba/MerkabaGridRenderer.cs"
                     })
            {
                string capture = Slice(Source(path),
                    "internal Action CaptureOwnedGpuResourceRelease()",
                    "\n        }") + "\n        }";
                Assert.That(capture, Does.Contain("if (this != null)"), path);
                Assert.That(capture, Does.Contain(
                    "UnityEngine.Object.Destroy"), path);
            }
            string gridCapture = Slice(
                Source("Runtime/Merkaba/MerkabaGrid.Gpu.cs"),
                "internal Action CaptureOwnedGpuResourceRelease()",
                "internal void ReleaseOwnedResourcesAfterGpuRetirement()");
            Assert.That(gridCapture, Does.Contain("if (this != null)"));
            Assert.That(gridCapture, Does.Contain("buffer?.Release()"));
            string aggregateCapture = Slice(scanner,
                "private Action CaptureOwnedGpuResourceRelease()",
                "private uint NextLifecycleGeneration()");
            Assert.That(aggregateCapture, Does.Not.Contain("_renderer != null"));
            Assert.That(aggregateCapture, Does.Not.Contain("_depthCapture != null"));
            Assert.That(aggregateCapture, Does.Not.Contain("_integrator != null"));
            Assert.That(aggregateCapture, Does.Not.Contain("_grid != null"));
            Assert.That(aggregateCapture, Does.Contain(
                "!ReferenceEquals(_grid, null)"));
        }

        [Test]
        public void ComponentsDoNotReleaseGpuOwnershipFromOnDestroy()
        {
            Assert.That(Source("Runtime/Merkaba/MerkabaGrid.cs"),
                Does.Not.Contain("OnDestroy()\n        {\n            ReleaseGpuResources"));
            Assert.That(Source("Runtime/Merkaba/MerkabaIntegrator.cs"),
                Does.Not.Contain("private void OnDestroy()"));
            Assert.That(Source("Runtime/Camera/PassthroughCameraProvider.cs"),
                Does.Not.Contain("private void OnDestroy()"));
            string depth = Source("Runtime/Core/DepthCapture.cs");
            string destroy = Slice(depth, "private void OnDestroy()",
                "/// <summary>");
            Assert.That(destroy, Does.Not.Contain("Release"));

            foreach (string source in new[]
                     {
                         Source("Runtime/Core/RoomScanner.cs"), depth,
                         Source("Runtime/Merkaba/MerkabaGrid.Gpu.cs")
                     })
            {
                Assert.That(source, Does.Not.Contain("WaitForCompletion"));
                Assert.That(source, Does.Not.Contain("Thread.Sleep"));
                Assert.That(source, Does.Not.Contain("Task.Delay"));
            }
        }

        [Test]
        public void OperationProgressIsBoundedAndMonotonicWithinAStage()
        {
            var tracker = new ScanOperationProgressTracker();
            tracker.Begin(ScanOperationKind.Save);
            Assert.That(tracker.Report(ScanOperationKind.Save,
                ScanOperationStage.SynchronizingScan, 0, -1), Is.EqualTo(-1f));
            float synchronized = tracker.Report(ScanOperationKind.Save,
                ScanOperationStage.SynchronizingScan, 1, 1);
            float half = tracker.Report(ScanOperationKind.Save,
                ScanOperationStage.FlushingTiles, 5, 10);
            float regressed = tracker.Report(ScanOperationKind.Save,
                ScanOperationStage.FlushingTiles, 2, 10);
            float clamped = tracker.Report(ScanOperationKind.Save,
                ScanOperationStage.FlushingTiles, 20, 10);

            Assert.That(half, Is.GreaterThan(synchronized));
            Assert.That(regressed, Is.EqualTo(half));
            Assert.That(clamped, Is.GreaterThanOrEqualTo(half));
            Assert.That(clamped, Is.LessThan(1f),
                "100% is reserved for durable operation completion.");
        }

        [Test]
        public void CheckpointProgressUsesActualBytesAndRecords()
        {
            MerkabaSessionSnapshot snapshot = Snapshot(2);
            var written = new RecordingProgress();
            using var stream = new MemoryStream();
            MerkabaSsdStore.WriteCheckpoint(stream, snapshot, written);
            AssertStageIsMeasured(written.Values,
                ScanOperationStage.WritingFile);
            OperationWorkProgress writeLast = Last(written.Values,
                ScanOperationStage.WritingFile);
            Assert.That(writeLast.Completed, Is.EqualTo(stream.Length));
            Assert.That(writeLast.Total, Is.EqualTo(stream.Length));

            stream.Position = 0;
            var read = new RecordingProgress();
            MerkabaSessionSnapshot restored = MerkabaSsdStore.ReadCheckpoint(
                stream, read);
            Assert.That(restored.Tiles.Count, Is.EqualTo(2));
            AssertStageIsMeasured(read.Values, ScanOperationStage.ReadingFile);
            OperationWorkProgress readLast = Last(read.Values,
                ScanOperationStage.ReadingFile);
            Assert.That(readLast.Completed, Is.EqualTo(stream.Length));
            Assert.That(readLast.Total, Is.EqualTo(stream.Length));
        }

        [Test]
        public void FailedAndCompletedOperationsRemainDistinct()
        {
            var failed = new ScanOperationState(ScanOperationKind.ExportGlb,
                ScanOperationStage.Failed, 0.42f, false, "Failed: disk full");
            var complete = new ScanOperationState(ScanOperationKind.ExportGlb,
                ScanOperationStage.Complete, 1f, false, "Exported");
            Assert.That(failed.Stage, Is.EqualTo(ScanOperationStage.Failed));
            Assert.That(failed.Progress01, Is.LessThan(1f));
            Assert.That(failed.StatusText, Does.StartWith("Failed"));
            Assert.That(complete.Stage, Is.EqualTo(ScanOperationStage.Complete));
            Assert.That(complete.Progress01, Is.EqualTo(1f));
        }

        [Test]
        public void ExportReportsMeasuredGeometryAndOutputBytes()
        {
            KernelState state = new()
            {
                OccupancyEvidence = MerkabaConstants.OccupiedOnThreshold,
                PackedColor = KernelState.PackColor(
                    new UnityEngine.Color32(1, 2, 3, 255)),
                ColorConfidence = 1,
                Flags = MerkabaConstants.OccupiedFlag
            };
            state.Flags = KernelState.SetSurfacePlane(state.Flags,
                new float3(1, 0, 0), 0f);
            var evidence = new Dictionary<int3, KernelState>
            {
                [new int3(0, 0, 0)] = state
            };
            MerkabaExportMembraneResult membrane = MerkabaExportMembrane.Build(
                MerkabaExportShell.Build(evidence));
            var progress = new RecordingProgress();
            using var stream = new MemoryStream();
            MerkabaGlbResult result = MerkabaGlbWriter.Write(stream, membrane,
                progress);
            Assert.That(result.ByteLength, Is.EqualTo(stream.Length));
            AssertStageIsMeasured(progress.Values,
                ScanOperationStage.BuildingMerkabaGeometry);
            AssertStageIsMeasured(progress.Values,
                ScanOperationStage.WritingFile);
            OperationWorkProgress last = Last(progress.Values,
                ScanOperationStage.WritingFile);
            Assert.That(last.Completed, Is.EqualTo(stream.Length));
            Assert.That(last.Total, Is.EqualTo(stream.Length));
        }

        [Test]
        public void DurablePublishPrecedesSuccessfulHundredPercentState()
        {
            string store = Source("Runtime/Merkaba/MerkabaSsdStore.cs");
            string publish = Slice(store,
                "internal void PublishCheckpoint(MerkabaSessionSnapshot snapshot,",
                "internal void Clear()");
            int flush = publish.IndexOf("stream.Flush(true);",
                StringComparison.Ordinal);
            int atomicPublish = publish.IndexOf("MerkabaFilePublishing.Publish(",
                StringComparison.Ordinal);
            int progressComplete = publish.LastIndexOf(
                "ScanOperationStage.PublishingFile, 1, 1",
                StringComparison.Ordinal);
            Assert.That(flush, Is.GreaterThanOrEqualTo(0));
            Assert.That(atomicPublish, Is.GreaterThan(flush));
            Assert.That(progressComplete, Is.GreaterThan(atomicPublish));

            string scanner = Source("Runtime/Core/RoomScanner.cs");
            string finish = Slice(scanner,
                "internal void FinishOperation(ScanOperationKind kind",
                "private void SetOperation");
            Assert.That(finish, Does.Contain("success ? 1f"));
            Assert.That(finish, Does.Contain("ScanOperationStage.Failed"));
        }

        private static MerkabaSessionSnapshot Snapshot(int count)
        {
            var snapshot = new MerkabaSessionSnapshot();
            for (int item = 0; item < count; item++)
            {
                var states = new KernelState[MerkabaSpatial.KernelsPerTile];
                states[item].OccupancyEvidence =
                    MerkabaConstants.OccupiedOnThreshold;
                states[item].Flags = MerkabaConstants.OccupiedFlag;
                snapshot.Tiles.Add(new MerkabaTileSnapshot
                {
                    Address = new MerkabaTileAddress(new int3(item, -item, 0),
                        (uint)item),
                    Generation = 1,
                    States = states
                });
            }
            return snapshot;
        }

        private static void AssertStageIsMeasured(
            IReadOnlyList<OperationWorkProgress> values,
            ScanOperationStage stage)
        {
            long prior = -1;
            bool found = false;
            foreach (OperationWorkProgress value in values)
            {
                if (value.Stage != stage) continue;
                found = true;
                Assert.That(value.Total, Is.GreaterThanOrEqualTo(0));
                Assert.That(value.Completed, Is.InRange(0L, value.Total));
                Assert.That(value.Completed, Is.GreaterThanOrEqualTo(prior));
                prior = value.Completed;
            }
            Assert.That(found, Is.True);
        }

        private static OperationWorkProgress Last(
            IReadOnlyList<OperationWorkProgress> values,
            ScanOperationStage stage)
        {
            for (int index = values.Count - 1; index >= 0; index--)
                if (values[index].Stage == stage) return values[index];
            throw new AssertionException("Progress stage was not reported.");
        }

        private sealed class RecordingProgress : IProgress<OperationWorkProgress>
        {
            internal readonly List<OperationWorkProgress> Values = new();
            public void Report(OperationWorkProgress value) => Values.Add(value);
        }

        private static string Source(string relative) => File.ReadAllText(
            Path.GetFullPath("Packages/com.genesis.roomscan/" + relative));

        private static string Slice(string source, string start, string end)
        {
            int from = source.IndexOf(start, StringComparison.Ordinal);
            int to = source.IndexOf(end, from + start.Length,
                StringComparison.Ordinal);
            Assert.That(from, Is.GreaterThanOrEqualTo(0), start);
            Assert.That(to, Is.GreaterThan(from), end);
            return source.Substring(from, to - from);
        }
    }
}
