using System;
using System.Runtime.InteropServices;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaPoseGaugeTests
    {
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
        private struct Float2
        {
            public float X;
            public float Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Float4
        {
            public float X;
            public float Y;
            public float Z;
            public float W;
        }

        [Test]
        public void ExactGpuResultSelectsMinimumMagnitudeMeetPoint()
        {
            long[] twist =
            {
                SigmaNumericDomain.Quantize(0.01),
                SigmaNumericDomain.Quantize(-0.02), 0L,
                SigmaNumericDomain.Quantize(0.01), 0L, 0L
            };
            var words = new NativeArray<uint>(16, Allocator.Temp);
            try
            {
                words[0] = 1u;
                for (int component = 0; component < 6; ++component)
                {
                    ulong raw = unchecked((ulong)twist[component]);
                    words[4 + component * 2] = unchecked((uint)raw);
                    words[5 + component * 2] = unchecked((uint)(raw >> 32));
                }

                SigmaPoseGaugeState gauge = SigmaPoseGaugeState.FromGpu(words,
                    7u, 9u);

                Assert.That(gauge.Resolved, Is.True);
                Assert.That(gauge.CalibrationEpoch, Is.EqualTo(7u));
                Assert.That(gauge.Revision, Is.EqualTo(9u));
                for (int component = 0; component < 6; ++component)
                    Assert.That(gauge.Raw(component), Is.EqualTo(twist[component]));
            }
            finally { words.Dispose(); }
            Assert.That(SigmaPoseGaugeState.MinimumMagnitude(-2L, 4L), Is.Zero);
            Assert.That(SigmaPoseGaugeState.MinimumMagnitude(2L, 4L), Is.EqualTo(2L));
            Assert.That(SigmaPoseGaugeState.MinimumMagnitude(-4L, -2L), Is.EqualTo(-2L));
        }

        [Test]
        public void UnresolvedMeetIsIdentityAndResolvedGaugePreservesRigExtrinsics()
        {
            var words = new NativeArray<uint>(16, Allocator.Temp);
            try
            {
                words[1] = 1u;
                Assert.That(SigmaPoseGaugeState.FromGpu(words, 3u, 4u).Resolved,
                    Is.False);
            }
            finally { words.Dispose(); }

            var gauge = new SigmaPoseGaugeState(3u, 4u, true,
                SigmaNumericDomain.Quantize(0.02), 0L, 0L,
                0L, SigmaNumericDomain.Quantize(0.01), 0L);
            Pose left = new(new Vector3(1f, 2f, 3f),
                Quaternion.Euler(4f, 5f, 6f));
            Pose right = new(left.position + left.rotation * Vector3.right * 0.064f,
                left.rotation);
            Matrix4x4 rawRelative = Matrix(left).inverse * Matrix(right);
            Matrix4x4 correctedRelative = Matrix(gauge.Apply(left, left)).inverse *
                Matrix(gauge.Apply(left, right));

            Assert.That((rawRelative.GetColumn(3) -
                correctedRelative.GetColumn(3)).magnitude, Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(rawRelative.rotation,
                correctedRelative.rotation), Is.LessThan(1e-4f));
        }

        [Test]
        public void VulkanPoseGaugeKernelIsPresent()
        {
            ComputeShader shader = Resources.Load<ComputeShader>(
                "SigmaPrism/SigmaPoseGauge");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.FindKernel("BuildPoseGaugePartials"),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("ReducePoseGauge"),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("BuildCorrectedCalibration"),
                Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void VulkanPoseKernelsSolveAndCorrectTheSameRigGauge()
        {
            const int width = 6;
            const int height = 2;
            const int pixels = width * height;
            const int depthStride = 36;
            const int rgbStride = 8;
            ComputeShader shader = Resources.Load<ComputeShader>(
                "SigmaPrism/SigmaPoseGauge");
            Assert.That(shader, Is.Not.Null);
            int build = shader.FindKernel("BuildPoseGaugePartials");
            int reduce = shader.FindKernel("ReducePoseGauge");
            int correct = shader.FindKernel("BuildCorrectedCalibration");

            Float4[] rays = BuildPoseRays(width, height);
            Float2[][] metric = new Float2[2][];
            Float2[][] prediction = new Float2[2][];
            Float4[][] carrier = new Float4[2][];
            for (int eye = 0; eye < 2; ++eye)
            {
                metric[eye] = new Float2[pixels];
                prediction[eye] = new Float2[pixels];
                carrier[eye] = new Float4[pixels];
                FillPoseEye(metric[eye], prediction[eye], carrier[eye], rays);
            }

            Texture2DArray metricTexture = CreateArrayTexture(width, height,
                GraphicsFormat.R32G32_SFloat, metric);
            Texture2DArray predictionTexture = CreateArrayTexture(width, height,
                GraphicsFormat.R32G32_SFloat, prediction);
            Texture2DArray carrierTexture = CreateArrayTexture(width, height,
                GraphicsFormat.R32G32B32A32_SFloat, carrier);
            Texture2D rayTexture = CreateTexture(width, height,
                GraphicsFormat.R32G32B32A32_SFloat, rays);
            float[][] rawDepth = new float[2][];
            for (int eye = 0; eye < 2; ++eye)
            {
                rawDepth[eye] = new float[pixels];
                Array.Fill(rawDepth[eye], 0.5f);
            }
            var validityRays = new Float4[pixels];
            Array.Fill(validityRays, new Float4 { Z = 1f, W = 1f });
            Texture2DArray rawDepthTexture = CreateArrayTexture(width, height,
                GraphicsFormat.R32_SFloat, rawDepth);
            Texture2D validityRayTexture = CreateTexture(width, height,
                GraphicsFormat.R32G32B32A32_SFloat, validityRays);
            RenderTexture normalizedMetric = CreateArrayRenderTexture(width,
                height, GraphicsFormat.R32G32_SFloat);
            RenderTexture flagTexture = CreateArrayRenderTexture(width, height,
                GraphicsFormat.R32_UInt);

            ComputeShader normalize = Resources.Load<ComputeShader>(
                "SigmaPrism/DepthNormalize");
            Assert.That(normalize, Is.Not.Null);
            int normalizeKernel = normalize.FindKernel("NormalizeStereoDepth");
            normalize.SetInts("_Resolution", width, height);
            normalize.SetVector("_NearFar", new Vector4(0.1f, 10f, 0f, 0f));
            normalize.SetTexture(normalizeKernel, "_RawDepth", rawDepthTexture);
            normalize.SetTexture(normalizeKernel, "_DepthRayCenterLeft",
                validityRayTexture);
            normalize.SetTexture(normalizeKernel, "_DepthRayCenterRight",
                validityRayTexture);
            normalize.SetTexture(normalizeKernel, "_MetricDepth",
                normalizedMetric);
            normalize.SetTexture(normalizeKernel, "_DepthFlags", flagTexture);
            normalize.Dispatch(normalizeKernel, 1, 1, 2);

            UInt2[] depthCalibration = BuildDepthCalibration();
            UInt2[] rgbCalibration = BuildRgbCalibration();
            UInt2[] prior = BuildPosePrior();
            var gpuResult = new UInt4[4];
            var correctedDepth = new UInt2[depthStride * 2];
            var correctedRgb = new UInt2[rgbStride * 2];
            using SigmaExactBackendGate gate = SigmaExactBackendGate.Dispatch();
            using var depthBuffer = Buffer(depthCalibration);
            using var rgbBuffer = Buffer(rgbCalibration);
            using var priorBuffer = Buffer(prior);
            using var partialBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 7, Marshal.SizeOf<UInt4>());
            using var resultBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 4, Marshal.SizeOf<UInt4>());
            using var correctedDepthBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, correctedDepth.Length,
                Marshal.SizeOf<UInt2>());
            using var correctedRgbBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, correctedRgb.Length,
                Marshal.SizeOf<UInt2>());
            try
            {
                shader.SetBuffer(build, "_DepthCalibrationQ48", depthBuffer);
                shader.SetBuffer(build, "_PosePrior", priorBuffer);
                shader.SetBuffer(build, "_PosePartials", partialBuffer);
                shader.SetTexture(build, "_PoseMetricDepth", metricTexture);
                shader.SetTexture(build, "_PoseDepthFlags", flagTexture);
                shader.SetTexture(build, "_PosePredDepthSupport",
                    predictionTexture);
                shader.SetTexture(build, "_PosePredCarrierUvNormal",
                    carrierTexture);
                shader.SetTexture(build, "_PoseRayLeft", rayTexture);
                shader.SetTexture(build, "_PoseRayRight", rayTexture);
                shader.SetInts("_PoseResolution", width, height);
                shader.SetInt("_PoseSampleStride", 1);
                shader.SetInt("_PoseRevision", 23);
                shader.SetInt("_PosePartialCount", 1);
                gate.Bind(shader, build);
                shader.Dispatch(build, 1, 1, 1);

                shader.SetBuffer(reduce, "_PosePrior", priorBuffer);
                shader.SetBuffer(reduce, "_PosePartials", partialBuffer);
                shader.SetBuffer(reduce, "_PoseResult", resultBuffer);
                shader.SetInt("_PoseRevision", 23);
                shader.SetInt("_PosePartialCount", 1);
                gate.Bind(shader, reduce);
                shader.Dispatch(reduce, 1, 1, 1);
                resultBuffer.GetData(gpuResult);

                Assert.That(gpuResult[0].X, Is.EqualTo(1u),
                    "the real build/reduce kernels must resolve both eyes and all axes");
                Assert.That(gpuResult[0].Z & 0x3f3fu, Is.EqualTo(0x3f3fu));
                long solvedZ = Unpack(gpuResult[2].X, gpuResult[2].Y);
                Assert.That(ToDouble(solvedZ), Is.InRange(0.02, 0.04),
                    "the exact point-to-plane meet must exclude identity in Z");

                shader.SetBuffer(correct, "_DepthCalibrationQ48", depthBuffer);
                shader.SetBuffer(correct, "_PoseRgbCalibrationQ48", rgbBuffer);
                shader.SetBuffer(correct, "_CorrectedDepthCalibrationQ48",
                    correctedDepthBuffer);
                shader.SetBuffer(correct, "_CorrectedRgbCalibrationQ48",
                    correctedRgbBuffer);
                shader.SetBuffer(correct, "_PoseResult", resultBuffer);
                shader.SetMatrix("_PoseConsumeReferenceFromWorld",
                    Matrix4x4.identity);
                shader.SetMatrix("_PoseConsumeWorldFromReference",
                    Matrix4x4.identity);
                shader.Dispatch(correct, 2, 1, 1);
                correctedDepthBuffer.GetData(correctedDepth);
                correctedRgbBuffer.GetData(correctedRgb);

                double leftZ = ToDouble(Unpack(correctedDepth[15]));
                double rightZ = ToDouble(Unpack(correctedDepth[depthStride + 15]));
                Assert.That(leftZ, Is.EqualTo(ToDouble(solvedZ)).Within(2e-6));
                Assert.That(rightZ, Is.EqualTo(leftZ).Within(2e-6));
                double baseline = ToDouble(Unpack(
                    correctedDepth[depthStride + 13])) -
                    ToDouble(Unpack(correctedDepth[13]));
                Assert.That(baseline, Is.EqualTo(0.064).Within(2e-6),
                    "one rigid correction must preserve fixed rig extrinsics");
                Assert.That(ToDouble(Unpack(correctedRgb[2])),
                    Is.EqualTo(leftZ).Within(2e-6));
                Assert.That(ToDouble(Unpack(correctedRgb[rgbStride + 2])),
                    Is.EqualTo(leftZ).Within(2e-6));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(metricTexture);
                UnityEngine.Object.DestroyImmediate(predictionTexture);
                UnityEngine.Object.DestroyImmediate(carrierTexture);
                UnityEngine.Object.DestroyImmediate(rayTexture);
                UnityEngine.Object.DestroyImmediate(rawDepthTexture);
                UnityEngine.Object.DestroyImmediate(validityRayTexture);
                normalizedMetric.Release();
                UnityEngine.Object.DestroyImmediate(normalizedMetric);
                flagTexture.Release();
                UnityEngine.Object.DestroyImmediate(flagTexture);
            }
        }

        private static Matrix4x4 Matrix(Pose pose) => Matrix4x4.TRS(
            pose.position, pose.rotation, Vector3.one);

        private static Float4[] BuildPoseRays(int width, int height)
        {
            Vector3[] basis =
            {
                Vector3.right,
                Vector3.up,
                Vector3.forward,
                new Vector3(0f, 1f, 1f).normalized,
                new Vector3(1f, 0f, 1f).normalized,
                new Vector3(1f, 1f, 0f).normalized
            };
            var result = new Float4[width * height];
            for (int index = 0; index < result.Length; ++index)
            {
                Vector3 ray = basis[index % basis.Length];
                result[index] = new Float4
                {
                    X = ray.x,
                    Y = ray.y,
                    Z = ray.z,
                    W = 1f
                };
            }
            return result;
        }

        private static void FillPoseEye(Float2[] metric, Float2[] prediction,
            Float4[] carrier, Float4[] rays)
        {
            Vector3[] normals =
            {
                Vector3.right,
                Vector3.up,
                Vector3.forward,
                Vector3.forward,
                Vector3.forward,
                Vector3.up
            };
            Vector3 trueTranslation = Vector3.forward * 0.029f;
            for (int index = 0; index < metric.Length; ++index)
            {
                Vector3 ray = new(rays[index].X, rays[index].Y, rays[index].Z);
                Vector3 normal = normals[index % normals.Length];
                float incidence = Vector3.Dot(ray, normal);
                float residual = -Vector3.Dot(normal, trueTranslation);
                metric[index] = new Float2
                {
                    X = 5f + residual / incidence,
                    Y = 1f
                };
                prediction[index] = new Float2 { X = 5f, Y = 8f };
                Vector2 oct = EncodeOctahedral(normal);
                carrier[index] = new Float4 { Z = oct.x, W = oct.y };
            }
        }

        private static Vector2 EncodeOctahedral(Vector3 normal)
        {
            normal /= Mathf.Abs(normal.x) + Mathf.Abs(normal.y) +
                Mathf.Abs(normal.z);
            if (normal.z < 0f)
            {
                float x = (1f - Mathf.Abs(normal.y)) * Mathf.Sign(normal.x);
                float y = (1f - Mathf.Abs(normal.x)) * Mathf.Sign(normal.y);
                normal.x = x;
                normal.y = y;
            }
            return new Vector2(normal.x, normal.y);
        }

        private static UInt2[] BuildPosePrior()
        {
            var result = new UInt2[15];
            for (int component = 0; component < 6; ++component)
                result[6 + component] = Pack(Q(0.05));
            result[12] = Pack(Q(0.00005));
            result[13] = Pack(Q(0.15));
            result[14] = Pack(Q(0.03));
            return result;
        }

        private static UInt2[] BuildDepthCalibration()
        {
            const int stride = 36;
            var result = new UInt2[stride * 2];
            double[] thresholds = { 0.5, 1.0, 2.0, 3.0, 5.0, 32767.0 };
            for (int eye = 0; eye < 2; ++eye)
            {
                int offset = eye * stride;
                result[offset + 4] = Pack(Q(1.0));
                result[offset + 8] = Pack(Q(1.0));
                result[offset + 12] = Pack(Q(1.0));
                result[offset + 13] = Pack(Q(eye == 0 ? 0.0 : 0.064));
                result[offset + 16] = Pack(Q(0.1));
                result[offset + 17] = Pack(Q(100.0));
                result[offset + 18] = Pack(Q(0.0001));
                for (int bin = 0; bin < 6; ++bin)
                {
                    result[offset + 19 + bin] = Pack(Q(thresholds[bin]));
                    result[offset + 25 + bin] = Pack(Q(0.0005));
                }
                result[offset + 31] = Pack(Q(0.0001));
                result[offset + 32] = Pack(Q(0.01));
                result[offset + 33] = Pack(
                    SigmaNumericDomain.FromRatio(1, 64));
            }
            return result;
        }

        private static UInt2[] BuildRgbCalibration()
        {
            const int stride = 8;
            var result = new UInt2[stride * 2];
            result[stride] = Pack(Q(0.064));
            return result;
        }

        private static Texture2DArray CreateArrayTexture<T>(int width,
            int height, GraphicsFormat format, T[][] layers) where T : struct
        {
            var texture = new Texture2DArray(width, height, layers.Length,
                format, TextureCreationFlags.None);
            for (int layer = 0; layer < layers.Length; ++layer)
                texture.SetPixelData(layers[layer], 0, layer);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D CreateTexture<T>(int width, int height,
            GraphicsFormat format, T[] data) where T : struct
        {
            var texture = new Texture2D(width, height, format,
                TextureCreationFlags.None);
            texture.SetPixelData(data, 0);
            texture.Apply(false, false);
            return texture;
        }

        private static RenderTexture CreateArrayRenderTexture(int width,
            int height, GraphicsFormat format)
        {
            Assert.That(SystemInfo.IsFormatSupported(format,
                GraphicsFormatUsage.LoadStore), Is.True, format.ToString());
            var descriptor = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = format,
                depthBufferBits = 0,
                msaaSamples = 1,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                volumeDepth = 2,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false
            };
            var texture = new RenderTexture(descriptor)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Assert.That(texture.Create(), Is.True, format.ToString());
            return texture;
        }

        private static GraphicsBuffer Buffer<T>(T[] data) where T : struct
        {
            var result = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                data.Length, Marshal.SizeOf<T>());
            result.SetData(data);
            return result;
        }

        private static long Q(double value) => SigmaNumericDomain.Quantize(value);

        private static UInt2 Pack(long value) => new()
        {
            X = unchecked((uint)value),
            Y = unchecked((uint)(value >> 32))
        };

        private static long Unpack(UInt2 value) => Unpack(value.X, value.Y);

        private static long Unpack(uint low, uint high) => unchecked((long)(
            ((ulong)high << 32) | low));

        private static double ToDouble(long raw) =>
            raw / (double)(1L << SigmaNumericDomain.FractionBits);
    }
}
