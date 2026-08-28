using System;
using System.IO;
using Genesis.RoomScan;
using NUnit.Framework;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaGpuTimestampTests
    {
        [TearDown]
        public void ResetTimingState() =>
            MerkabaGpuTimestamps.SetAvailableForTests(false);

        [Test]
        public void FixedStageContract_IsExactlyTheSixRequiredGpuSpans()
        {
            Assert.That(Enum.GetNames(typeof(MerkabaGpuStage)), Is.EqualTo(new[]
            {
                "DepthPreprocess",
                "SurfaceIntegration",
                "CarveIntegration",
                "TopologyUpdate",
                "PublicationCompaction",
                "MerkabaDraw",
                "Count"
            }));
            Assert.That((int)MerkabaGpuStage.Count, Is.EqualTo(6));
        }

        [Test]
        public void SampledFrame_RecordsFixedParallelStagesWithoutDispatchingWork()
        {
            MerkabaGpuTimestamps.SetAvailableForTests(true);
            Assert.That(MerkabaGpuTimestamps.TryBeginFrame(73), Is.True);

            MerkabaGpuTimestamps.BeginCompute(MerkabaGpuStage.DepthPreprocess);
            MerkabaGpuTimestamps.EndCompute(MerkabaGpuStage.DepthPreprocess);
            MerkabaGpuTimestamps.BeginCompute(MerkabaGpuStage.SurfaceIntegration);
            MerkabaGpuTimestamps.EndCompute(MerkabaGpuStage.SurfaceIntegration);
            MerkabaGpuTimestamps.BeginCompute(MerkabaGpuStage.CarveIntegration);
            MerkabaGpuTimestamps.EndCompute(MerkabaGpuStage.CarveIntegration);
            MerkabaGpuTimestamps.BeginCompute(MerkabaGpuStage.TopologyUpdate);
            MerkabaGpuTimestamps.EndCompute(MerkabaGpuStage.TopologyUpdate);
            MerkabaGpuTimestamps.BeginCompute(MerkabaGpuStage.PublicationCompaction);
            MerkabaGpuTimestamps.EndCompute(MerkabaGpuStage.PublicationCompaction);
            MerkabaGpuTimestamps.BeginGraphics(MerkabaGpuStage.MerkabaDraw);
            MerkabaGpuTimestamps.EndGraphics(MerkabaGpuStage.MerkabaDraw);
            MerkabaGpuTimestamps.EndFrame();

            Assert.That(MerkabaGpuTimestamps.RecordedStagesForTests(),
                Is.EqualTo(new[]
                {
                    MerkabaGpuStage.DepthPreprocess,
                    MerkabaGpuStage.SurfaceIntegration,
                    MerkabaGpuStage.CarveIntegration,
                    MerkabaGpuStage.TopologyUpdate,
                    MerkabaGpuStage.PublicationCompaction,
                    MerkabaGpuStage.MerkabaDraw
                }));
        }

        [Test]
        public void UnsampledFrame_RecordsNoTimingEvents()
        {
            MerkabaGpuTimestamps.SetAvailableForTests(false);
            Assert.That(MerkabaGpuTimestamps.TryBeginFrame(1), Is.False);
            MerkabaGpuTimestamps.BeginCompute(MerkabaGpuStage.TopologyUpdate);
            MerkabaGpuTimestamps.EndCompute(MerkabaGpuStage.TopologyUpdate);
            MerkabaGpuTimestamps.EndFrame();
            Assert.That(MerkabaGpuTimestamps.RecordedStagesForTests(), Is.Empty);
        }

        [Test]
        public void TimestampDelta_HandlesQuestValidBitWrapExactly()
        {
            const int validBits = 48;
            ulong range = 1UL << validBits;
            double elapsed = MerkabaGpuTimestamps.ElapsedNanoseconds(
                range - 5, 3, 2.5, validBits);
            Assert.That(elapsed, Is.EqualTo(20.0));
        }

        [Test]
        public void TelemetryUsesNativeVulkanQueriesAndNoFrameTimingSubstitute()
        {
            string managed = Source("Runtime/Telemetry/MerkabaGpuTimestamps.cs");
            string native = Source(
                "Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp");
            string integrator = Source("Runtime/Merkaba/MerkabaIntegrator.cs");
            string renderer = Source("Runtime/Merkaba/MerkabaGridRenderer.cs");
            string build = Source("Tools/unity/build_merkaba_apk.sh");

            foreach (string forbidden in new[]
                     {
                         "UnityEngine.Profiling", "ProfilerRecorder", "Stopwatch",
                         "BeginSample", "EndSample", "Sigma"
                     })
            {
                Assert.That(managed, Does.Not.Contain(forbidden));
                Assert.That(native, Does.Not.Contain(forbidden));
            }
            Assert.That(native, Does.Contain("VK_QUERY_TYPE_TIMESTAMP"));
            Assert.That(native, Does.Contain("vkCmdWriteTimestamp"));
            Assert.That(native, Does.Contain("VK_QUERY_RESULT_WITH_AVAILABILITY_BIT"));
            Assert.That(integrator, Does.Contain("MerkabaGpuStage.DepthPreprocess"));
            Assert.That(integrator, Does.Contain("MerkabaGpuStage.SurfaceIntegration"));
            Assert.That(integrator, Does.Contain("MerkabaGpuStage.CarveIntegration"));
            Assert.That(renderer, Does.Contain("MerkabaGpuStage.TopologyUpdate"));
            Assert.That(renderer, Does.Contain(
                "MerkabaGpuStage.PublicationCompaction"));
            Assert.That(renderer, Does.Contain("MerkabaGpuStage.MerkabaDraw"));
            Assert.That(build, Does.Contain("build_merkaba_vulkan_timestamps.sh"));
        }

        private static string Source(string relative) =>
            File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/" + relative));
    }
}
