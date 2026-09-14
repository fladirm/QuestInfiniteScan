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
            public uint GaugeGeneration;
            public uint CertificateGeneration;
            public uint RepresentationFlags;
            public uint RepresentationFingerprint;
            public uint ActiveSampleCount;
            public uint Reserved0;
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

        [StructLayout(LayoutKind.Sequential)]
        private struct PureReadout
        {
            public Vector4 LeftPosition;
            public Vector4 LeftColourOrder;
            public Vector4 RightPosition;
            public Vector4 RightColourOrder;
        }

        [Test]
        public void GpuPureReadoutPreservesFullCodeNullMetadataAndFirstHit()
        {
            ComputeShader readout = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.genesis.roomscan/Tests/Editor/Generated/SigmaPureEyeReadoutFixture.compute");
            Shader predictionShader = Resources.Load<Shader>("SigmaPrism/SigmaPredict");
            Assert.That(readout, Is.Not.Null);
            Assert.That(predictionShader, Is.Not.Null);
            int kernel = readout.FindKernel("BuildPureEyeReadout");
            var page = new SigmaS16[PageSize * PageSize];
            for (int i = 0; i < page.Length; ++i)
                page[i] = SigmaS16Operators.NullState;
            page[10] = CompleteCodeState(0.75, 0.0, 0.0, 0.375);
            page[40] = CompleteCodeState(0.5, 0.0, 0.0, 0.25);
            UInt2[] packed = Pack(page);
            var metadata = new PageMeta
            {
                PageXLo = unchecked((uint)-3), PageXHi = uint.MaxValue,
                PageYLo = 7u, Generation = 9u, Revision = 17u,
                Flags = 3u, ActiveSampleCount = 4096u,
            };
            using SigmaExactBackendGate gate = SigmaExactBackendGate.Dispatch();
            using var state = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                packed.Length, Marshal.SizeOf<UInt2>());
            using var meta = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                1, Marshal.SizeOf<PageMeta>());
            using var renderMeta = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                1, Marshal.SizeOf<PageMeta>());
            using var root = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                1, sizeof(uint));
            using var samples = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                PageSize * PageSize, Marshal.SizeOf<PureReadout>());
            using var slots = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                1, sizeof(uint));
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 4, sizeof(uint));
            using var poseResult = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                4, sizeof(uint) * 4);
            state.SetData(packed);
            meta.SetData(new[] { metadata });
            root.SetData(new uint[] { 17u });
            args.SetData(new uint[4]);
            poseResult.SetData(new UInt4[4]);
            gate.Bind(readout, kernel);
            for (int bank = 0; bank < 2; ++bank)
            {
                readout.SetBuffer(kernel, "_CarrierState" + bank, state);
                readout.SetBuffer(kernel, "_PageMetadata" + bank, meta);
                readout.SetBuffer(kernel, "_ReadoutSamples" + bank, samples);
                readout.SetBuffer(kernel, "_CurrentPageSlots" + bank, slots);
                readout.SetBuffer(kernel, "_RenderPageMetadata" + bank, renderMeta);
                readout.SetBuffer(kernel, "_ReadoutDrawArguments" + bank, args);
            }
            readout.SetBuffer(kernel, "_PublishedRevisionRoot", root);
            // Exactly the 176-byte no-matrix production uniform ABI.
            var constants = new int[44];
            void SetQ(int word, double value)
            {
                long raw = SigmaNumericDomain.Quantize(value);
                constants[word] = unchecked((int)raw);
                constants[word + 1] = unchecked((int)(raw >> 32));
            }
            for (int eye = 0; eye < 2; ++eye)
            {
                SetQ(5 * 4 + eye * 2, 1.0);
                SetQ(6 * 4 + eye * 2, 0.1);
                SetQ(7 * 4 + eye * 2, 2.0);
            }
            SetQ(9 * 4 + 2, 6.0);
            constants[40] = 1;
            constants[41] = 1;
            constants[42] = 1;
            readout.SetInts("_N6ReadoutConstants", constants);
            // Only bank 0 executes; alias bindings supply the unused bank ABI.
            readout.Dispatch(kernel, 1, 64, 1);
            var gpu = new PureReadout[4096];
            samples.GetData(gpu);
            float expectedNear = ExactProjectionZ(0.75);
            float expectedFar = ExactProjectionZ(0.875);
            Assert.That(gpu[40].LeftPosition.z, Is.EqualTo(expectedNear).Within(1e-6));
            Assert.That(gpu[10].LeftPosition.z, Is.EqualTo(expectedFar).Within(1e-6));
            Assert.That(gpu[40].LeftPosition.w, Is.EqualTo(1f));
            Assert.That(gpu[40].RightPosition, Is.EqualTo(gpu[40].LeftPosition));
            Assert.That(gpu[40].LeftColourOrder.x, Is.EqualTo(0.625f));
            Assert.That(gpu[40].LeftColourOrder.y, Is.EqualTo(0.5f));
            Assert.That(gpu[40].LeftColourOrder.z, Is.EqualTo(0.5f));
            Assert.That(gpu[40].LeftColourOrder.w,
                Is.EqualTo(expectedNear).Within(1e-6));
            Assert.That(gpu[28].LeftPosition, Is.EqualTo(Vector4.zero));
            Assert.That(gpu[28].RightPosition, Is.EqualTo(Vector4.zero));
            var captured = new PageMeta[1];
            renderMeta.GetData(captured);
            Assert.That(captured[0].PageXLo, Is.EqualTo(metadata.PageXLo));
            Assert.That(captured[0].PageXHi, Is.EqualTo(metadata.PageXHi));
            Assert.That(captured[0].PageYLo, Is.EqualTo(metadata.PageYLo));
            Assert.That(captured[0].Generation, Is.EqualTo(metadata.Generation));
            Assert.That(captured[0].Revision, Is.EqualTo(metadata.Revision));
            var draw = new uint[4];
            args.GetData(draw);
            Assert.That(draw, Is.EqualTo(new uint[] { VerticesPerPage, 1u, 0u, 0u }));
            var unchanged = new UInt2[packed.Length];
            state.GetData(unchanged);
            Assert.That(unchanged, Is.EqualTo(packed), "readout must not mutate Psi");

            const int size = 96;
            RenderTexture depth = CreateColor(size, GraphicsFormat.R32G32_SFloat);
            RenderTexture carrierPage = CreateColor(size, GraphicsFormat.R32G32B32A32_UInt);
            RenderTexture uv = CreateColor(size, GraphicsFormat.R32G32B32A32_SFloat);
            RenderTexture key = CreateColor(size, GraphicsFormat.R32G32B32A32_UInt);
            RenderTexture hardwareDepth = CreateDepth(size);
            var material = new Material(predictionShader);
            try
            {
                var properties = new MaterialPropertyBlock();
                Matrix4x4 projection = GL.GetGPUProjectionMatrix(
                    Matrix4x4.Perspective(90f, 1f, 0.1f, 2f), true);
                properties.SetMatrix("_ClipFromWorld", projection *
                    Matrix4x4.Scale(new Vector3(1f, 1f, -1f)));
                properties.SetMatrix("_OpticalFromWorld", Matrix4x4.identity);
                properties.SetMatrix("_PoseConsumeReferenceFromWorld", Matrix4x4.identity);
                properties.SetMatrix("_PoseConsumeWorldFromReference", Matrix4x4.identity);
                properties.SetInt("_ReadoutEye", 0);
                properties.SetInt("_SegmentIndex", 5);
                properties.SetFloat("_ContactFootprintPixels", 2f);
                properties.SetBuffer("_ReadoutSamples", samples);
                properties.SetBuffer("_CurrentPageSlots", slots);
                properties.SetBuffer("_PageMetadata", renderMeta);
                properties.SetBuffer("_PoseResult", poseResult);
                DrawPrediction(material, args, properties, depth, carrierPage, uv,
                    key, hardwareDepth);
                Assert.That(MinPositiveDepth(Readback<float>(depth)),
                    Is.EqualTo(expectedNear).Within(1e-5),
                    "hardware Z must select the nearer complete query contribution");
                uint[] keys = Readback<uint>(key);
                int hit = Array.FindIndex(keys, value => value == 9u);
                Assert.That(hit, Is.GreaterThanOrEqualTo(0));
                Assert.That(keys[hit + 1], Is.EqualTo(17u));
                Assert.That(keys[hit + 2], Is.EqualTo(5u));

                // A deleted/rebuilt disposable cache reproduces identical bytes.
                args.SetData(new uint[4]);
                readout.Dispatch(kernel, 1, 64, 1);
                var rebuilt = new PureReadout[4096];
                samples.GetData(rebuilt);
                Assert.That(rebuilt, Is.EqualTo(gpu));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
                Destroy(depth); Destroy(carrierPage); Destroy(uv);
                Destroy(key); Destroy(hardwareDepth);
            }
        }

        private static SigmaS16 CompleteCodeState(params double[] values)
        {
            long[] leaves = Array.ConvertAll(values, SigmaNumericDomain.Quantize);
            long common = 0;
            foreach (long value in leaves)
                common = SigmaNumericDomain.QAdd(common, value);
            long[] tangent = Array.ConvertAll(leaves, value =>
                SigmaNumericDomain.QSub(SigmaNumericDomain.QMul(
                    value, SigmaNumericDomain.FromInteger(4)), common));
            return SigmaGeneratedMerkabaProgram.LiftMerkabaComplete(tangent, common);
        }

        private static float ExactProjectionZ(double code)
        {
            long near = SigmaNumericDomain.Quantize(0.1);
            long far = SigmaNumericDomain.Quantize(2.0);
            long denominator = SigmaNumericDomain.QSub(far, SigmaNumericDomain.QMul(
                SigmaNumericDomain.Quantize(code), SigmaNumericDomain.QSub(far, near)));
            return (float)SigmaNumericDomain.ToDouble(SigmaNumericDomain.QDiv(
                SigmaNumericDomain.QMul(near, far), denominator));
        }

        [Test]
        public void SegmentReadoutCacheOwnsExactlyTwoNonAliasedGenerations()
        {
            using var state = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                1, sizeof(uint) * 2);
            using var representation = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 1, sizeof(uint) * 4);
            using var metadata = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2,
                SigmaCarrier.PageMetadataStride);
            using var dirty = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                2, sizeof(uint));
            using var readoutDirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            using var root = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                1, sizeof(uint));
            using var residentLocator = new GraphicsBuffer(
                GraphicsBuffer.Target.Raw, 16,
                SigmaCarrierResidencyAbi.LocatorStride);
            using var residentSlotTable = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2,
                SigmaCarrierResidencyAbi.SlotStride);
            var batch = new SigmaCarrierReadBatch(0, 2, 0, state,
                representation, metadata, dirty, readoutDirty, root,
                residentLocator, residentSlotTable, 16, 2,
                new SigmaCarrierRuntimeState());
            using var cache = new SigmaRenderer.SegmentReadoutCache(batch);

            Assert.That(cache.GenerationCount, Is.EqualTo(2));
            Assert.That(cache.FrontIndex, Is.Zero);
            Assert.That(cache.BackIndex, Is.EqualTo(1));
            Assert.That(cache.Front.Samples, Is.Not.SameAs(cache.Back.Samples));
            Assert.That(cache.Front.Samples.stride, Is.EqualTo(64));
            Assert.That(cache.Front.CurrentPageSlots,
                Is.Not.SameAs(cache.Back.CurrentPageSlots));
            Assert.That(cache.Front.DrawArguments,
                Is.Not.SameAs(cache.Back.DrawArguments));
            Assert.That(cache.Front.RenderPageMetadata,
                Is.Not.SameAs(cache.Back.RenderPageMetadata));
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

        private static float MinPositiveDepth(float[] depthSupport)
        {
            float minimum = float.PositiveInfinity;
            for (int index = 0; index < depthSupport.Length; index += 2)
            {
                float depth = depthSupport[index];
                if (depth > 0f)
                    minimum = Mathf.Min(minimum, depth);
            }
            Assert.That(float.IsFinite(minimum), Is.True,
                "prediction fixture must contain a supported first hit");
            return minimum;
        }

        private static void DrawPrediction(Material material,
            GraphicsBuffer arguments, MaterialPropertyBlock properties,
            RenderTexture depthSupport, RenderTexture carrierPage,
            RenderTexture carrierUvNormal, RenderTexture stateKey,
            RenderTexture hardwareDepth, int pass = 0)
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
                command.DrawProceduralIndirect(Matrix4x4.identity, material, pass,
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
