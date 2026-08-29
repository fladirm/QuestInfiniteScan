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
            Assert.That(transition, Does.Contain("await QuiesceScanningAsync();"));
            Assert.That(transition, Does.Contain("await StartScanningAsync();"));
            Assert.That(transition.IndexOf("await QuiesceScanningAsync();",
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
                "ReleaseOwnedResourcesAfterGpuRetirement()",
                StringComparison.Ordinal);
            Assert.That(quiesce, Is.GreaterThanOrEqualTo(0));
            Assert.That(suspend, Is.GreaterThan(quiesce));
            Assert.That(retire, Is.GreaterThan(suspend));
            Assert.That(release, Is.GreaterThan(retire));
            Assert.That(teardown, Does.Contain("if (_destroyed) return;"));
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
            var kernels = new List<MerkabaKernelSnapshot>
            {
                new(new int3(0, 0, 0), new KernelState
                {
                    OccupancyEvidence = MerkabaConstants.OccupiedOnThreshold,
                    PackedColor = KernelState.PackColor(
                        new UnityEngine.Color32(1, 2, 3, 255)),
                    ColorConfidence = 1,
                    Flags = MerkabaConstants.OccupiedFlag
                })
            };
            var progress = new RecordingProgress();
            using var stream = new MemoryStream();
            MerkabaGlbResult result = MerkabaGlbWriter.Write(stream, kernels,
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
