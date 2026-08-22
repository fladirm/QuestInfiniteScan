using System;
using System.Runtime.InteropServices;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaForwardReadoutTests
    {
        private const int PageSize = 64;
        private const int ReadoutExtent = 65;
        private const int ReadoutSampleCount = ReadoutExtent * ReadoutExtent;
        private const int VerticesPerPage = PageSize * PageSize * 6;

        [StructLayout(LayoutKind.Sequential)]
        private struct UInt2
        {
            public uint X;
            public uint Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UInt4
        {
            public uint X;
            public uint Y;
            public uint Z;
            public uint W;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PageMeta
        {
            public uint PageXLo;
            public uint PageXHi;
            public uint PageYLo;
            public uint PageYHi;
            public uint Generation;
            public uint Revision;
            public uint CertificateOffsetLo;
            public uint CertificateOffsetHi;
            public uint CertificateCount;
            public uint Flags;
            public uint Reserved0;
            public uint Reserved1;
        }

        [Test]
        public void ExactLiftReadoutRoundTripsAndNullHasNoContact()
        {
            long mass = SigmaNumericDomain.FromInteger(8);
            long x = SigmaNumericDomain.Quantize(-0.375);
            long y = SigmaNumericDomain.Quantize(0.125);
            long z = SigmaNumericDomain.Quantize(0.75);
            SigmaS16 state = SigmaGeometryReadout.LiftFixture(mass, x, y, z);

            Assert.That(SigmaGeometryReadout.TryRead(state, out var sample), Is.True);
            Assert.That(sample.InformationMassRaw, Is.EqualTo(mass));
            Assert.That(sample.Position.x, Is.EqualTo(-0.375f).Within(1e-6f));
            Assert.That(sample.Position.y, Is.EqualTo(0.125f).Within(1e-6f));
            Assert.That(sample.Position.z, Is.EqualTo(0.75f).Within(1e-6f));
            Assert.That(SigmaGeometryReadout.TryRead(
                SigmaS16Operators.NullState, out _), Is.False);
        }

        [Test]
        public void GpuReadoutAndRasterSelectNearFoldWhileNullStaysEmpty()
        {
            ComputeShader readout = Resources.Load<ComputeShader>(
                "SigmaPrism/SigmaForwardReadout");
            Shader predictionShader = Resources.Load<Shader>(
                "SigmaPrism/SigmaPredict");
            Assert.That(readout, Is.Not.Null);
            Assert.That(predictionShader, Is.Not.Null);

            SigmaS16[] page = BuildFoldedPage();
            UInt2[] packed = Pack(page);
            var metadata = new[]
            {
                new PageMeta
                {
                    PageXLo = unchecked((uint)-3),
                    PageXHi = uint.MaxValue,
                    PageYLo = 7u,
                    PageYHi = 0u,
                    Generation = 9u,
                    Revision = 17u,
                    Flags = 3u
                }
            };

            using SigmaExactBackendGate gate = SigmaExactBackendGate.Dispatch();
            using var state = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                packed.Length, Marshal.SizeOf<UInt2>());
            using var meta = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                1, Marshal.SizeOf<PageMeta>());
            using var current = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                1, sizeof(uint));
            using var readoutDirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            using var vertices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                ReadoutSampleCount, sizeof(float) * 4);
            using var activeSlots = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                1, sizeof(uint));
            using var dirtySlots = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                1, sizeof(uint));
            using var topologyCellFlags = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, PageSize * PageSize,
                sizeof(uint));
            using var topologyPageKeys = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 1, sizeof(uint) * 4);
            using var poseResult = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 4, sizeof(uint) * 4);
            using var arguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 4, sizeof(uint));
            using var buildArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 3, sizeof(uint));
            using var haloArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 3, sizeof(uint));
            state.SetData(packed);
            meta.SetData(metadata);
            current.SetData(new uint[] { 1u });
            readoutDirty.SetData(new uint[] { 1u });
            arguments.SetData(new uint[] { 0u, 1u, 0u, 0u });
            buildArguments.SetData(new uint[] { 64u, 0u, 1u });
            haloArguments.SetData(new uint[] { 1u, 0u, 1u });
            topologyCellFlags.SetData(new uint[PageSize * PageSize]);
            topologyPageKeys.SetData(new[]
            {
                new UInt4 { X = 9u, Y = 17u, Z = 1u, W = 0u }
            });
            poseResult.SetData(new UInt4[4]);

            int build = readout.FindKernel("BuildCarrierReadout");
            int compact = readout.FindKernel("CompactCurrentPages");
            int resolveHalo = readout.FindKernel("ResolveCarrierHalos");
            readout.SetInt("_PageCapacity", 1);
            readout.SetBuffer(compact, "_CurrentFlags", current);
            readout.SetBuffer(compact, "_ReadoutDirtyFlags", readoutDirty);
            readout.SetBuffer(compact, "_CurrentPageSlots", activeSlots);
            readout.SetBuffer(compact, "_ReadoutDrawArguments", arguments);
            readout.SetBuffer(compact, "_ReadoutDirtyPageSlots", dirtySlots);
            readout.SetBuffer(compact, "_ReadoutBuildArguments", buildArguments);
            readout.SetBuffer(compact, "_ReadoutHaloArguments", haloArguments);
            readout.Dispatch(compact, 1, 1, 1);

            gate.Bind(readout, build);
            readout.SetBuffer(build, "_CarrierState", state);
            readout.SetBuffer(build, "_PageMetadata", meta);
            readout.SetBuffer(build, "_CurrentFlags", current);
            readout.SetBuffer(build, "_ReadoutDirtyFlags", readoutDirty);
            readout.SetBuffer(build, "_ReadoutDirtyPageSlots", dirtySlots);
            readout.SetBuffer(build, "_ReadoutVertices", vertices);
            readout.DispatchIndirect(build, buildArguments);
            readout.SetBuffer(resolveHalo, "_PageMetadata", meta);
            readout.SetBuffer(resolveHalo, "_CurrentFlags", current);
            readout.SetBuffer(resolveHalo, "_CurrentPageSlots", activeSlots);
            readout.SetBuffer(resolveHalo, "_ReadoutVertices", vertices);
            readout.DispatchIndirect(resolveHalo, haloArguments);

            var gpuReadout = new Vector4[ReadoutSampleCount];
            vertices.GetData(gpuReadout);
            Vector4 farSample = gpuReadout[10 * ReadoutExtent + 10];
            Vector4 nearSample = gpuReadout[40 * ReadoutExtent + 40];
            Vector4 nullSample = gpuReadout[28 * ReadoutExtent + 28];
            Assert.That(farSample.w, Is.EqualTo(8f).Within(1e-4f));
            Assert.That(farSample.z, Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(nearSample.w, Is.EqualTo(8f).Within(1e-4f));
            Assert.That(nearSample.z, Is.EqualTo(0.3f).Within(1e-4f));
            Assert.That(nullSample, Is.EqualTo(Vector4.zero));

            const int targetSize = 96;
            RenderTexture depthSupport = CreateColor(targetSize,
                GraphicsFormat.R32G32_SFloat);
            RenderTexture carrierPage = CreateColor(targetSize,
                GraphicsFormat.R32G32B32A32_UInt);
            RenderTexture carrierUvNormal = CreateColor(targetSize,
                GraphicsFormat.R32G32B32A32_SFloat);
            RenderTexture stateKey = CreateColor(targetSize,
                GraphicsFormat.R32G32B32A32_UInt);
            RenderTexture hardwareDepth = CreateDepth(targetSize);
            var material = new Material(predictionShader);
            var properties = new MaterialPropertyBlock();
            try
            {
                Matrix4x4 projection = GL.GetGPUProjectionMatrix(
                    Matrix4x4.Perspective(90f, 1f, 0.1f, 2f), true);
                Matrix4x4 graphicsFromOptical = Matrix4x4.Scale(
                    new Vector3(1f, 1f, -1f));
                properties.SetMatrix("_ClipFromWorld",
                    projection * graphicsFromOptical);
                properties.SetMatrix("_OpticalFromWorld", Matrix4x4.identity);
                properties.SetInt("_SegmentIndex", 5);
                properties.SetBuffer("_ReadoutVertices", vertices);
                properties.SetBuffer("_CurrentPageSlots", activeSlots);
                properties.SetBuffer("_PageMetadata", meta);
                properties.SetBuffer("_TopologyCellFlags", topologyCellFlags);
                properties.SetBuffer("_TopologyPageKeys", topologyPageKeys);
                properties.SetBuffer("_PoseResult", poseResult);
                properties.SetMatrix("_PoseConsumeReferenceFromWorld",
                    Matrix4x4.identity);
                properties.SetMatrix("_PoseConsumeWorldFromReference",
                    Matrix4x4.identity);
                DrawPrediction(material, arguments, properties, depthSupport,
                    carrierPage, carrierUvNormal, stateKey, hardwareDepth);

                int pixel = 50 * targetSize + 52;
                float[] depth = Readback<float>(depthSupport);
                float[] uvNormal = Readback<float>(carrierUvNormal);
                Assert.That(depth[pixel * 2], Is.InRange(0.28f, 0.40f),
                    "hardware Z must select the near folded carrier branch");
                Assert.That(depth[pixel * 2 + 1], Is.EqualTo(8f).Within(1e-3f));
                Assert.That(uvNormal[pixel * 4], Is.GreaterThan(38f),
                    "CarrierUV must identify the near branch, not the far branch");

                int emptyPixel = 2 * targetSize + 2;
                Assert.That(depth[emptyPixel * 2], Is.Zero,
                    "implicit/null carrier may not emit physical contact");

                topologyPageKeys.SetData(new[]
                {
                    new UInt4 { X = 8u, Y = 17u, Z = 1u, W = 0u }
                });
                DrawPrediction(material, arguments, properties, depthSupport,
                    carrierPage, carrierUvNormal, stateKey, hardwareDepth);
                depth = Readback<float>(depthSupport);
                Assert.That(depth[pixel * 2], Is.Zero,
                    "a topology cache from another carrier generation must fail closed");

                topologyPageKeys.SetData(new[]
                {
                    new UInt4 { X = 9u, Y = 17u, Z = 1u, W = 0u }
                });
                var allCut = new uint[PageSize * PageSize];
                Array.Fill(allCut, 3u);
                topologyCellFlags.SetData(allCut);
                DrawPrediction(material, arguments, properties, depthSupport,
                    carrierPage, carrierUvNormal, stateKey, hardwareDepth);
                depth = Readback<float>(depthSupport);
                Assert.That(depth[pixel * 2], Is.Zero,
                    "a supported intrinsic singular cut must not be interpolated");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
                Destroy(depthSupport);
                Destroy(carrierPage);
                Destroy(carrierUvNormal);
                Destroy(stateKey);
                Destroy(hardwareDepth);
            }
        }

        private static SigmaS16[] BuildFoldedPage()
        {
            var page = new SigmaS16[PageSize * PageSize];
            for (int index = 0; index < page.Length; ++index)
                page[index] = SigmaS16Operators.NullState;
            FillPatch(page, 2, 2, 18, 0.6);
            FillPatch(page, 32, 32, 48, 0.3);
            return page;
        }

        private static void FillPatch(SigmaS16[] page, int minX, int minY,
            int maxExclusive, double z)
        {
            long mass = SigmaNumericDomain.FromInteger(8);
            for (int y = minY; y < maxExclusive; ++y)
            {
                for (int x = minX; x < maxExclusive; ++x)
                {
                    double nx = ((x - minX) / 15.0 - 0.5) * 0.2;
                    double ny = ((y - minY) / 15.0 - 0.5) * 0.2;
                    page[y * PageSize + x] = SigmaGeometryReadout.LiftFixture(
                        mass, SigmaNumericDomain.Quantize(nx),
                        SigmaNumericDomain.Quantize(ny),
                        SigmaNumericDomain.Quantize(z));
                }
            }
        }

        private static UInt2[] Pack(SigmaS16[] page)
        {
            var packed = new UInt2[page.Length * SigmaS16.LaneCount];
            for (int sample = 0; sample < page.Length; ++sample)
            {
                for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                {
                    long raw = page[sample][lane];
                    packed[sample * SigmaS16.LaneCount + lane] = new UInt2
                    {
                        X = unchecked((uint)raw),
                        Y = unchecked((uint)(raw >> 32))
                    };
                }
            }
            return packed;
        }

        private static RenderTexture CreateColor(int size, GraphicsFormat format)
        {
            var descriptor = new RenderTextureDescriptor(size, size)
            {
                graphicsFormat = format,
                depthBufferBits = 0,
                msaaSamples = 1,
                enableRandomWrite = false
            };
            var result = new RenderTexture(descriptor);
            Assert.That(result.Create(), Is.True);
            return result;
        }

        private static RenderTexture CreateDepth(int size)
        {
            var descriptor = new RenderTextureDescriptor(size, size)
            {
                graphicsFormat = GraphicsFormat.None,
                depthStencilFormat = GraphicsFormat.D32_SFloat,
                msaaSamples = 1
            };
            var result = new RenderTexture(descriptor);
            Assert.That(result.Create(), Is.True);
            return result;
        }

        private static T[] Readback<T>(RenderTexture texture) where T : struct
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(texture, 0);
            request.WaitForCompletion();
            Assert.That(request.hasError, Is.False, texture.graphicsFormat.ToString());
            return request.GetData<T>().ToArray();
        }

        private static void DrawPrediction(Material material,
            GraphicsBuffer arguments, MaterialPropertyBlock properties,
            RenderTexture depthSupport, RenderTexture carrierPage,
            RenderTexture carrierUvNormal, RenderTexture stateKey,
            RenderTexture hardwareDepth)
        {
            var command = new CommandBuffer
            {
                name = "Sigma folded readout fixture"
            };
            try
            {
                var mrt = new RenderTargetIdentifier[]
                {
                    depthSupport, carrierPage, carrierUvNormal, stateKey
                };
                command.SetRenderTarget(mrt, hardwareDepth);
                command.ClearRenderTarget(true, true, Color.clear, 1f);
                command.DrawProceduralIndirect(Matrix4x4.identity, material, 0,
                    MeshTopology.Triangles, arguments, 0, properties);
                Graphics.ExecuteCommandBuffer(command);
            }
            finally
            {
                command.Dispose();
            }
        }

        private static void Destroy(RenderTexture texture)
        {
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
