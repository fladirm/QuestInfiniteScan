using System;
using System.IO;
using Genesis.RoomScan;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaGpuTimestampTests
    {
        [TearDown]
        public void ResetTimingState() =>
            MerkabaGpuTimestamps.SetAvailableForTests(false);

        [Test]
        public void StageContract_IncludesFiveComputeDomainsAndActualDraw()
        {
            Assert.That(Enum.GetNames(typeof(MerkabaGpuStage)), Is.EqualTo(new[]
            {
                "DepthPreprocess",
                "SurfaceIntegration",
                "CarveIntegration",
                "WorldQuery",
                "ReadoutBuild",
                "MerkabaDraw",
                "Count"
            }));
            Assert.That((int)MerkabaGpuStage.Count, Is.EqualTo(6));
        }

        [Test]
        public void OwnerContract_IsTheFrozenRoundRobinOrder()
        {
            Assert.That(Enum.GetNames(typeof(CaptureOwner)), Is.EqualTo(new[]
            {
                "Observation",
                "ReadoutBuild",
                "Draw",
                "DepthSnapshotCopy",
                "PcaObservationCopy",
                "PcaHistoryCopy",
                "Count"
            }));
        }

        [Test]
        public void OwnerScheduler_AdvancesOneStepAndWrapsOnlyAfterValidSample()
        {
            using var command = new CommandBuffer();
            MerkabaGpuTimestamps.SetAvailableForTests(true);
            CaptureOwner[] order =
            {
                CaptureOwner.Observation,
                CaptureOwner.ReadoutBuild,
                CaptureOwner.Draw,
                CaptureOwner.DepthSnapshotCopy,
                CaptureOwner.PcaObservationCopy,
                CaptureOwner.PcaHistoryCopy
            };
            for (int index = 0; index < order.Length; index++)
            {
                Assert.That(MerkabaGpuTimestamps.ScheduledOwnerForTests,
                    Is.EqualTo(order[index]));
                bool acquired = MerkabaGpuTimestamps.TryAcquire(order[index],
                    unchecked((uint)(index + 1)), command);
                Assert.That(acquired, Is.True);
                MerkabaGpuTimestamps.End(order[index], command, acquired);
                MerkabaGpuTimestamps.Complete(order[index], acquired, true);
                MerkabaGpuTimestamps.ResolveSampleForTests(true);
            }
            Assert.That(MerkabaGpuTimestamps.ScheduledOwnerForTests,
                Is.EqualTo(CaptureOwner.Observation));
        }

        [Test]
        public void ScheduledOwner_RecordsOnlyItsSingleSubmission()
        {
            ComputeShader frame = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.genesis.roomscan/Runtime/Shaders/" +
                "MerkabaReadout.compute");
            int query = frame.FindProfiledKernel("QueryM8Readout",
                MerkabaGpuStage.WorldQuery);
            int compile = frame.FindProfiledKernel("EmitReadoutVertices",
                MerkabaGpuStage.ReadoutBuild);
            using var command = new CommandBuffer();
            MerkabaGpuTimestamps.SetAvailableForTests(true);
            MerkabaGpuTimestamps.SetScheduledOwnerForTests(
                CaptureOwner.ReadoutBuild);
            bool acquired = MerkabaGpuTimestamps.TryAcquire(
                CaptureOwner.ReadoutBuild, 73u, command);
            Assert.That(acquired, Is.True);
            command.DispatchComputeProfiled(frame, query, 1, 1, 1);
            command.DispatchComputeProfiled(frame, compile, 1, 1, 1);
            MerkabaGpuTimestamps.End(CaptureOwner.ReadoutBuild, command,
                acquired);
            MerkabaGpuTimestamps.Complete(CaptureOwner.ReadoutBuild,
                acquired, true);

            Assert.That(MerkabaGpuTimestamps.RecordedStagesForTests(),
                Is.EqualTo(new[]
                {
                    MerkabaGpuStage.WorldQuery,
                    MerkabaGpuStage.ReadoutBuild
                }));
        }

        [Test]
        public void UnsampledFrame_RecordsNoTimingEvents()
        {
            ComputeShader frame = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.genesis.roomscan/Runtime/Shaders/" +
                "MerkabaReadout.compute");
            int query = frame.FindProfiledKernel("QueryM8Readout",
                MerkabaGpuStage.WorldQuery);
            using var command = new CommandBuffer();

            MerkabaGpuTimestamps.SetAvailableForTests(false);
            Assert.That(MerkabaGpuTimestamps.TryAcquire(
                CaptureOwner.Observation, 1u, command), Is.False);
            command.DispatchComputeProfiled(frame, query, 1, 1, 1);
            Assert.That(MerkabaGpuTimestamps.RecordedStagesForTests(), Is.Empty);
        }

        [Test]
        public void PcaHistoryCopy_CannotStealObservationOwner()
        {
            var source = new Texture2D(2, 2);
            var destination = new RenderTexture(2, 2, 0);
            destination.Create();
            using var command = new CommandBuffer();
            try
            {
                MerkabaGpuTimestamps.SetAvailableForTests(true);
                bool rejected = MerkabaGpuTimestamps.TryAcquire(
                    CaptureOwner.PcaHistoryCopy, 91u, command);
                Assert.That(rejected, Is.False);
                command.BlitPcaHistoryProfiled(source, destination, rejected);
                Assert.That(MerkabaGpuTimestamps.RecordedStagesForTests(),
                    Is.Empty);

                bool acquired = MerkabaGpuTimestamps.TryAcquire(
                    CaptureOwner.Observation, 92u, command);
                Assert.That(acquired, Is.True);
                command.BlitPcaHistoryProfiled(source, destination, false);
                MerkabaGpuTimestamps.End(CaptureOwner.Observation, command,
                    acquired);
                MerkabaGpuTimestamps.Complete(CaptureOwner.Observation,
                    acquired, true);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                destination.Release();
                UnityEngine.Object.DestroyImmediate(destination);
            }
        }

        [Test]
        public void ValidAndInvalidSamplesAdvanceOwnerExactlyAsSpecified()
        {
            var source = new Texture2D(2, 2);
            var destination = new RenderTexture(2, 2, 0);
            destination.Create();
            using var command = new CommandBuffer();
            try
            {
                MerkabaGpuTimestamps.SetAvailableForTests(true);
                bool acquired = MerkabaGpuTimestamps.TryAcquire(
                    CaptureOwner.Observation, 92u, command);
                Assert.That(acquired, Is.True);
                command.BlitPcaObservationProfiled(source, destination, false);
                MerkabaGpuTimestamps.End(CaptureOwner.Observation, command,
                    acquired);
                MerkabaGpuTimestamps.Complete(CaptureOwner.Observation,
                    acquired, true);
                MerkabaGpuTimestamps.ResolveSampleForTests(false);
                Assert.That(MerkabaGpuTimestamps.ScheduledOwnerForTests,
                    Is.EqualTo(CaptureOwner.Observation));
                Assert.That(MerkabaGpuTimestamps.SessionSampleCountForTests,
                    Is.Zero);

                acquired = MerkabaGpuTimestamps.TryAcquire(
                    CaptureOwner.Observation, 93u, command);
                Assert.That(acquired, Is.True);
                MerkabaGpuTimestamps.End(CaptureOwner.Observation, command,
                    acquired);
                MerkabaGpuTimestamps.Complete(CaptureOwner.Observation,
                    acquired, true);
                MerkabaGpuTimestamps.ResolveSampleForTests(true);
                Assert.That(MerkabaGpuTimestamps.ScheduledOwnerForTests,
                    Is.EqualTo(CaptureOwner.ReadoutBuild));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                destination.Release();
                UnityEngine.Object.DestroyImmediate(destination);
            }
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
        public void TimestampSample_RejectsEntryCountMismatchBeforeAggregation()
        {
            Assert.That(MerkabaGpuTimestamps.IsTimestampSampleValid(1, false,
                CaptureOwner.Draw, CaptureOwner.Draw,
                7u, 7u, 12, 12), Is.True);
            Assert.That(MerkabaGpuTimestamps.IsTimestampSampleValid(1, false,
                CaptureOwner.Draw, CaptureOwner.Draw,
                7u, 7u, 11, 12), Is.False);
            Assert.That(MerkabaGpuTimestamps.IsTimestampSampleValid(1, false,
                CaptureOwner.Draw, CaptureOwner.Draw,
                7u, 7u, 13, 12), Is.False);
        }

        [Test]
        public void TimestampSample_RejectsWrongOwnerAndEntryOverrun()
        {
            Assert.That(MerkabaGpuTimestamps.IsTimestampSampleValid(1, false,
                CaptureOwner.ReadoutBuild, CaptureOwner.Draw,
                9u, 9u, 1, 1), Is.False);
            Assert.That(MerkabaGpuTimestamps.IsEntryTotalWithinSubmission(
                10_000.0, 11_000.0), Is.True);
            Assert.That(MerkabaGpuTimestamps.IsEntryTotalWithinSubmission(
                10_000.0, 11_001.0), Is.False);
        }

        [Test]
        public void OwnerQueryRanges_AreDisjointDuringSyntheticReset()
        {
            int stride = MerkabaGpuTimestamps.OwnerStrideForTests;
            var queries = new ulong[(int)CaptureOwner.Count * stride];
            Array.Fill(queries, 0x5a5a5a5a5a5a5a5aUL);
            int ownerABase = MerkabaGpuTimestamps.OwnerQueryBaseForTests(
                CaptureOwner.Observation);
            int ownerBBase = MerkabaGpuTimestamps.OwnerQueryBaseForTests(
                CaptureOwner.ReadoutBuild);

            Array.Clear(queries, ownerABase, stride);

            for (int index = ownerABase; index < ownerABase + stride; index++)
                Assert.That(queries[index], Is.Zero);
            for (int index = ownerBBase; index < ownerBBase + stride; index++)
                Assert.That(queries[index],
                    Is.EqualTo(0x5a5a5a5a5a5a5a5aUL));
        }

        [Test]
        public void TelemetryTimesComputeAndActualUrpRasterCommands()
        {
            string managed = Source("Runtime/Telemetry/MerkabaGpuTimestamps.cs");
            string native = Source(
                "Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp");
            string integrator = Source("Runtime/Merkaba/MerkabaIntegrator.cs");
            string renderer = Source("Runtime/Merkaba/MerkabaGridRenderer.cs");
            string feature = Source("Runtime/Merkaba/MerkabaRenderFeature.cs");
            string build = Source("Tools/unity/build_merkaba_apk.sh");

            foreach (string forbidden in new[]
                     {
                         "UnityEngine.Profiling", "ProfilerRecorder", "Stopwatch",
                         "BeginSample", "EndSample", "GL.IssuePluginEvent",
                         "GraphicsFence"
                     })
            {
                Assert.That(managed, Does.Not.Contain(forbidden));
                Assert.That(native, Does.Not.Contain(forbidden));
            }
            Assert.That(managed, Does.Contain("command.IssuePluginEvent"));
            Assert.That(managed, Does.Contain("command.DispatchCompute"));
            Assert.That(managed, Does.Contain(
                "command.DrawProceduralIndirect"));
            Assert.That(native, Does.Contain("VK_QUERY_TYPE_TIMESTAMP"));
            Assert.That(native, Does.Contain("vkCmdWriteTimestamp"));
            Assert.That(native, Does.Contain(
                "VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT"));
            Assert.That(native, Does.Contain(
                "VK_PIPELINE_STAGE_TRANSFER_BIT"));
            Assert.That(native, Does.Contain("VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT"));
            Assert.That(native, Does.Contain(
                "VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT"));
            Assert.That(native, Does.Contain(
                "VK_QUERY_RESULT_WITH_AVAILABILITY_BIT"));
            Assert.That(native, Does.Contain("kOwnerCount = 6"));
            Assert.That(native, Does.Contain("kOwnerStride"));
            Assert.That(native, Does.Contain(
                "ownerBase, kOwnerStride"));
            Assert.That(native, Does.Not.Contain(
                "g_queryPool, 0,\n                kMaximumQueries"));
            Assert.That(integrator, Does.Contain("DispatchComputeProfiled"));
            Assert.That(integrator, Does.Contain(
                "BlitPcaObservationProfiled"));
            Assert.That(managed, Does.Contain("BlitPcaHistoryProfiled"));
            Assert.That(managed, Does.Contain("Native.CopyBegin"));
            Assert.That(renderer, Does.Contain("DispatchComputeProfiled"));
            Assert.That(renderer, Does.Contain("DrawProceduralIndirectProfiled"));
            Assert.That(renderer, Does.Not.Contain(
                "Graphics.DrawProceduralIndirect"));
            Assert.That(feature, Does.Contain("AddRasterRenderPass"));
            Assert.That(feature, Does.Contain("RecordRenderPass(context.cmd)"));
            Assert.That(build, Does.Contain(
                "build_merkaba_vulkan_timestamps.sh"));
        }

        [Test]
        public void ProducerContract_HasNoFreeForAllCaptureEntryPoint()
        {
            string timestamps = Source(
                "Runtime/Telemetry/MerkabaGpuTimestamps.cs");
            string provider = Source(
                "Runtime/Camera/PassthroughCameraProvider.cs");
            string depth = Source("Runtime/Core/DepthCapture.cs");
            string integrator = Source(
                "Runtime/Merkaba/MerkabaIntegrator.cs");
            string renderer = Source(
                "Runtime/Merkaba/MerkabaGridRenderer.cs");
            string scanner = Source("Runtime/Core/RoomScanner.cs");
            string[] producers = { provider, depth, integrator, renderer,
                scanner };
            foreach (string producer in producers)
            {
                Assert.That(producer, Does.Not.Contain("TryBeginFrame"));
                Assert.That(producer, Does.Not.Contain(
                    "TryBeginStandaloneSubmission"));
                Assert.That(producer, Does.Not.Contain(
                    "CompleteFrameSubmission"));
                Assert.That(producer, Does.Not.Contain(
                    "CompleteStandaloneSubmission"));
                Assert.That(producer, Does.Not.Contain(
                    "CloseIncompleteFrame"));
            }
            Assert.That(timestamps, Does.Not.Contain("TryBeginFrame"));
            Assert.That(timestamps, Does.Not.Contain(
                "TryBeginStandaloneSubmission"));
            Assert.That(depth, Does.Contain(
                "IsOwnerRecording(\n                CaptureOwner.Observation)"));
            Assert.That(depth, Does.Not.Contain(
                "MerkabaGpuTimestamps.IsRecording"));
            Assert.That(renderer, Does.Not.Contain("IsOwnerEligible"));
            Assert.That(renderer, Does.Not.Contain(
                "RecordHashBenchmark(command)"));
            Assert.That(timestamps, Does.Contain(
                "SampleIntervalSeconds = 5f"));
        }

        [Test]
        public void ProducerContract_EndsEachCaptureBeforeItsSubmission()
        {
            AssertEndBeforeExecute(Source(
                    "Runtime/Camera/PassthroughCameraProvider.cs"),
                "End(CaptureOwner.PcaHistoryCopy",
                "Graphics.ExecuteCommandBuffer(command)");
            AssertEndBeforeExecute(Source("Runtime/Core/DepthCapture.cs"),
                "End(CaptureOwner.DepthSnapshotCopy",
                "Graphics.ExecuteCommandBuffer(command)");
            AssertEndBeforeExecute(Source(
                    "Runtime/Merkaba/MerkabaIntegrator.cs"),
                "End(CaptureOwner.PcaObservationCopy",
                "Graphics.ExecuteCommandBuffer(command)");
            AssertEndBeforeExecute(Source(
                    "Runtime/Merkaba/MerkabaIntegrator.cs"),
                "End(CaptureOwner.Observation",
                "Graphics.ExecuteCommandBuffer(command)");
            AssertEndBeforeExecute(Source(
                    "Runtime/Merkaba/MerkabaGridRenderer.cs"),
                "End(CaptureOwner.ReadoutBuild",
                "Graphics.ExecuteCommandBuffer(command)");

            string renderer = Source(
                "Runtime/Merkaba/MerkabaGridRenderer.cs");
            AssertOrdered(renderer,
                "TryAcquire(\n                CaptureOwner.Draw",
                "DrawProceduralIndirectProfiled",
                "End(CaptureOwner.Draw",
                "Complete(CaptureOwner.Draw");
        }

        private static void AssertEndBeforeExecute(string source,
            string endToken, string submitToken)
        {
            int end = source.IndexOf(endToken, StringComparison.Ordinal);
            int submit = source.IndexOf(submitToken, end,
                StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThanOrEqualTo(0), endToken);
            Assert.That(submit, Is.GreaterThan(end), submitToken);
        }

        private static void AssertOrdered(string source, params string[] tokens)
        {
            int previous = -1;
            foreach (string token in tokens)
            {
                int current = source.IndexOf(token, previous + 1,
                    StringComparison.Ordinal);
                Assert.That(current, Is.GreaterThan(previous), token);
                previous = current;
            }
        }

        private static string Source(string relative) =>
            File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/" + relative));
    }
}
