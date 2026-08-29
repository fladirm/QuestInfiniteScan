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
        public void SampledFrame_RecordsActualDispatchSequence()
        {
            ComputeShader frame = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.genesis.roomscan/Runtime/Shaders/" +
                "MerkabaReadout.compute");
            int query = frame.FindProfiledKernel("QueryM8Readout",
                MerkabaGpuStage.WorldQuery);
            int compile = frame.FindProfiledKernel("CompileReadoutVertices",
                MerkabaGpuStage.ReadoutBuild);
            using var command = new CommandBuffer();
            using var arguments = new ComputeBuffer(4, sizeof(uint),
                ComputeBufferType.IndirectArguments);
            var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
            RasterCommandBuffer raster =
                CommandBufferHelpers.GetRasterCommandBuffer(command);
            try
            {
                MerkabaGpuTimestamps.SetAvailableForTests(true);
                Assert.That(MerkabaGpuTimestamps.TryBeginFrame(73), Is.True);
                MerkabaGpuTimestamps.RecordProfileBegin(command);
                command.DispatchComputeProfiled(frame, query, 1, 1, 1);
                command.DispatchComputeProfiled(frame, compile, 1, 1, 1);
                raster.DrawProceduralIndirectProfiled(Matrix4x4.identity,
                    material, 0, MeshTopology.Triangles, arguments, 0);
                MerkabaGpuTimestamps.RecordProfileEnd(raster);
                MerkabaGpuTimestamps.CompleteFrameSubmission(true);

                Assert.That(MerkabaGpuTimestamps.RecordedStagesForTests(),
                    Is.EqualTo(new[]
                    {
                        MerkabaGpuStage.WorldQuery,
                        MerkabaGpuStage.ReadoutBuild,
                        MerkabaGpuStage.MerkabaDraw
                    }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
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
            Assert.That(MerkabaGpuTimestamps.TryBeginFrame(1), Is.False);
            command.DispatchComputeProfiled(frame, query, 1, 1, 1);
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
            Assert.That(native, Does.Contain("VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT"));
            Assert.That(native, Does.Contain(
                "VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT"));
            Assert.That(native, Does.Contain(
                "VK_QUERY_RESULT_WITH_AVAILABILITY_BIT"));
            Assert.That(integrator, Does.Contain("DispatchComputeProfiled"));
            Assert.That(renderer, Does.Contain("DispatchComputeProfiled"));
            Assert.That(renderer, Does.Contain("DrawProceduralIndirectProfiled"));
            Assert.That(renderer, Does.Not.Contain(
                "Graphics.DrawProceduralIndirect"));
            Assert.That(feature, Does.Contain("AddRasterRenderPass"));
            Assert.That(feature, Does.Contain("RecordRenderPass(context.cmd)"));
            Assert.That(build, Does.Contain(
                "build_merkaba_vulkan_timestamps.sh"));
        }

        private static string Source(string relative) =>
            File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/" + relative));
    }
}
