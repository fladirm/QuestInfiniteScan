using System;
using System.IO;
using System.Reflection;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Tests
{
    public sealed class DepthPreprocessTests
    {
        [Test]
        public void DilationSequence_IncludesFinalUnitStepExactlyOnce()
        {
            Assert.That(DepthCapture.BuildDilationStepSequence(8), Is.EqualTo(new[]
            {
                256, 128, 64, 32, 16, 8, 4, 2, 1
            }));
            Assert.That(DepthCapture.BuildDilationStepSequence(0),
                Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void PreprocessCadence_ConsumesOnlyNewRawFrameVersions()
        {
            Assert.That(DepthCapture.ShouldPreprocessFrame(0, 0), Is.False);
            Assert.That(DepthCapture.ShouldPreprocessFrame(1, 0), Is.True);
            Assert.That(DepthCapture.ShouldPreprocessFrame(1, 1), Is.False);
            Assert.That(DepthCapture.ShouldPreprocessFrame(7, 1), Is.True,
                "Dropped/intermediate sensor versions must collapse into one latest-frame consume.");
            Assert.That(DepthCapture.ShouldPreprocessFrame(7, 7), Is.False);
        }

        [Test]
        public void RequestedDepthSnapshot_LatchesExactlyOneOwnedFrame()
        {
            var host = new GameObject("Depth snapshot test");
            DepthCapture capture = host.AddComponent<DepthCapture>();
            Texture2DArray first = MakeDepth(4, 4, 0.2f, 0.3f);
            Texture2DArray second = MakeDepth(4, 4, 0.7f, 0.8f);
            RenderTexture owned = null;
            try
            {
                capture.StartDepthCapture();
                Assert.That(capture.TryLatchDepthSnapshot(first), Is.False,
                    "An unrequested producer frame must be discarded.");
                Assert.That(capture.OwnedRawDepthSnapshot, Is.Null);

                capture.RequestNextDepthFrame();
                Assert.That(capture.DepthFrameRequested, Is.True);
                Assert.That(capture.TryLatchDepthSnapshot(first), Is.True);
                owned = capture.OwnedRawDepthSnapshot;
                int version = capture.LatestRawFrameVersion;
                Assert.That(owned, Is.Not.Null);
                Assert.That(owned, Is.Not.SameAs(first));
                Assert.That(capture.DepthFrameRequested, Is.False);
                Assert.That(capture.OwnedDepthSnapshotReady, Is.True);
                Assert.That(capture.HasUnprocessedFrame, Is.True);

                Assert.That(capture.TryLatchDepthSnapshot(second), Is.False,
                    "An unconsumed owned snapshot must not be overwritten.");
                Assert.That(capture.OwnedRawDepthSnapshot, Is.SameAs(owned));
                Assert.That(capture.LatestRawFrameVersion, Is.EqualTo(version));
            }
            finally
            {
                capture.StopDepthCapture();
                if (owned != null) UnityEngine.Object.DestroyImmediate(owned);
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OwnedDepthSnapshot_IsPreprocessedOnlyOnConsume()
        {
            string source = RuntimeSource("Runtime/Core/DepthCapture.cs");
            string callback = Slice(source, "private void OnDepthFrame(",
                "public bool ConsumeLatestDepthFrame()");
            int requestGuard = callback.IndexOf(
                "if (!_depthFrameRequested || _ownedDepthSnapshotReady) return;",
                StringComparison.Ordinal);
            Assert.That(requestGuard, Is.GreaterThanOrEqualTo(0));
            Assert.That(callback, Does.Not.Contain("ApplyBilateralFilter()"));
            Assert.That(callback, Does.Not.Contain("ComputeNormals()"));
            Assert.That(callback, Does.Not.Contain("ComputeDilation()"));

            string consume = Slice(source, "public bool ConsumeLatestDepthFrame()",
                "internal static bool ShouldPreprocessFrame");
            Assert.That(consume, Does.Contain("_depthTex = _ownedRawDepthTex;"));
            Assert.That(consume, Does.Contain("ApplyBilateralFilter();"));
            Assert.That(consume, Does.Contain("ComputeNormals();"));
            Assert.That(consume, Does.Contain("ComputeDilation();"));
            Assert.That(consume, Does.Contain("_ownedDepthSnapshotReady = false;"));
            Assert.That(source, Does.Not.Contain("private Texture _rawDepthTex"));
        }

        [Test]
        public void ObservationCadence_LatchesPcaCopyAndMatchingMetadataOnce()
        {
            var host = new GameObject("PCA snapshot test");
            DepthCapture depth = host.AddComponent<DepthCapture>();
            MerkabaIntegrator integrator = host.AddComponent<MerkabaIntegrator>();
            var external = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            external.Apply(false, false);
            RenderTexture owned = null;
            Texture2D dummy = null;
            Vector3 sampledPosition = new(1f, 2f, 3f);
            try
            {
                typeof(MerkabaIntegrator).GetField("_depthCapture",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(integrator, depth);
                integrator.SetCameraData(external, sampledPosition,
                    Quaternion.Euler(4f, 5f, 6f), new Vector2(7f, 8f),
                    new Vector2(9f, 10f), new Vector2(11f, 12f),
                    new Vector2(13f, 14f));
                owned = integrator.OwnedCameraFrame;
                Assert.That(owned, Is.Not.Null);
                Assert.That(owned, Is.Not.SameAs(external));
                Assert.That(integrator.CameraFrameAvailable, Is.True);
                Assert.That(depth.RGBGuide, Is.SameAs(owned));
                Assert.That(PrivateField<Vector3>(integrator,
                    "_pendingCameraPosition"), Is.EqualTo(sampledPosition));
                Assert.That(typeof(MerkabaIntegrator).GetField("_pendingCameraFrame",
                    BindingFlags.Instance | BindingFlags.NonPublic), Is.Null,
                    "The producer-owned PCA texture must not be retained.");

                string scanner = RuntimeSource("Runtime/Core/RoomScanner.cs");
                string update = Slice(scanner, "private void Update()",
                    "private void OnDisable()");
                Assert.That(update, Does.Not.Contain("ProvideColorFrame();"));
                string arm = Slice(scanner, "private void ArmNextObservation()",
                    "private void OnIntegrated()");
                Assert.That(arm.IndexOf("ProvideColorFrame();", StringComparison.Ordinal),
                    Is.LessThan(arm.IndexOf("RequestNextDepthFrame();",
                        StringComparison.Ordinal)));
            }
            finally
            {
                dummy = PrivateField<Texture2D>(integrator, "_dummyCameraTexture");
                if (RenderTexture.active == owned) RenderTexture.active = null;
                if (owned != null) UnityEngine.Object.DestroyImmediate(owned);
                if (dummy != null) UnityEngine.Object.DestroyImmediate(dummy);
                UnityEngine.Object.DestroyImmediate(external);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test, Timeout(30000)]
        public void DepthNormals_BordersDispatchPaddingAndBothEyes_AreFinite()
        {
            const int width = 13;
            const int height = 11;
            ComputeShader compute = LoadCompute("DepthNormals.compute");
            int kernel = compute.FindKernel("DepthNorm");
            Matrix4x4 projection = Matrix4x4.Perspective(90f,
                width / (float)height, 0.1f, 10f);
            float depthNdc = DepthNdc(projection, 1f);
            Texture2DArray depth = MakeDepth(width, height, depthNdc, depthNdc);
            RenderTexture normals = MakeArrayTarget(width, height);

            try
            {
                BindDepthMatrices(compute, projection, width, height);
                compute.SetTexture(kernel, DepthCapture.DepthTexID, depth);
                compute.SetTexture(kernel, DepthCapture.NormTexRWID, normals);
                compute.Dispatch(kernel, Mathf.CeilToInt(width / 8f),
                    Mathf.CeilToInt(height / 8f), 2);

                for (int eye = 0; eye < 2; eye++)
                {
                    AsyncGPUReadbackRequest request = ReadArrayLayer(
                        normals, width, height, eye);
                    var values = request.GetData<float4>();
                    Assert.That(values.Length, Is.EqualTo(width * height));
                    for (int index = 0; index < values.Length; index++)
                    {
                        float4 value = values[index];
                        AssertFinite(value, $"normal[{eye},{index}]");
                        Assert.That(value.w, Is.GreaterThan(0.5f),
                            $"border/padded normal [{eye},{index}] was not written");
                        Assert.That(math.lengthsq(value.xyz),
                            Is.EqualTo(1f).Within(2e-3f));
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(depth);
                UnityEngine.Object.DestroyImmediate(normals);
            }
        }

        [Test, Timeout(30000)]
        public void DepthDilation_SignedBorderTapsDispatchPaddingAndBothEyes_AreSafe()
        {
            const int width = 13;
            const int height = 11;
            const float leftDepth = 0.42f;
            const float rightDepth = 0.73f;
            ComputeShader compute = LoadCompute("DepthDilation.compute");
            int init = compute.FindKernel("InitDepthDilation");
            int step = compute.FindKernel("DilateDepthStep");
            Matrix4x4 projection = Matrix4x4.Perspective(90f,
                width / (float)height, 0.1f, 10f);
            Texture2DArray depth = MakeDepth(width, height, leftDepth, rightDepth);
            RenderTexture a = MakeArrayTarget(width, height);
            RenderTexture b = MakeArrayTarget(width, height);

            try
            {
                BindDepthMatrices(compute, projection, width, height);
                compute.SetFloat(DepthCapture.VoxDistID, 0.075f);
                compute.SetFloat(DepthCapture.VoxSizeShaderID, 0.025f);
                compute.SetTexture(init, DepthCapture.DepthTexID, depth);
                compute.SetTexture(init, DepthCapture.DilateSrcID, a);
                compute.SetTexture(init, DepthCapture.DilateDestID, b);
                int groupsX = Mathf.CeilToInt(width / 8f);
                int groupsY = Mathf.CeilToInt(height / 8f);
                compute.Dispatch(init, groupsX, groupsY, 2);

                foreach (int stepSize in DepthCapture.BuildDilationStepSequence(8))
                {
                    compute.SetTexture(step, DepthCapture.DilateSrcID, a);
                    compute.SetTexture(step, DepthCapture.DilateDestID, b);
                    compute.SetInt(DepthCapture.DilateStepSizeID, stepSize);
                    compute.Dispatch(step, groupsX, groupsY, 2);
                    (a, b) = (b, a);
                }

                var leftValues = ReadArrayLayer(a, width, height, 0)
                    .GetData<float4>();
                var rightValues = ReadArrayLayer(a, width, height, 1)
                    .GetData<float4>();
                Assert.That(leftValues.Length, Is.EqualTo(width * height));
                Assert.That(rightValues.Length, Is.EqualTo(width * height));
                for (int index = 0; index < leftValues.Length; index++)
                {
                    AssertFinite(leftValues[index], $"dilation[0,{index}]");
                    AssertFinite(rightValues[index], $"dilation[1,{index}]");
                }
                Assert.That(leftValues[0].z,
                    Is.EqualTo(leftDepth).Within(2e-4f));
                Assert.That(rightValues[0].z,
                    Is.EqualTo(rightDepth).Within(2e-4f),
                    "The second depth eye was not dilated independently.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(depth);
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
            }
        }

        private static ComputeShader LoadCompute(string file)
        {
            string path = "Packages/com.genesis.roomscan/Runtime/Shaders/" + file;
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            Assert.That(shader, Is.Not.Null, path);
            return shader;
        }

        private static string RuntimeSource(string relativePath) =>
            File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/" + relativePath));

        private static string Slice(string source, string start, string end)
        {
            int first = source.IndexOf(start, StringComparison.Ordinal);
            int last = source.IndexOf(end, first + start.Length,
                StringComparison.Ordinal);
            Assert.That(first, Is.GreaterThanOrEqualTo(0), start);
            Assert.That(last, Is.GreaterThan(first), end);
            return source.Substring(first, last - first);
        }

        private static T PrivateField<T>(object target, string name) =>
            (T)target.GetType().GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(target);

        private static void BindDepthMatrices(ComputeShader compute,
            Matrix4x4 projection, int width, int height)
        {
            Matrix4x4[] projections = { projection, projection };
            Matrix4x4[] inverseProjections =
                { projection.inverse, projection.inverse };
            Matrix4x4[] views = { Matrix4x4.identity, Matrix4x4.identity };
            compute.SetMatrixArray(DepthCapture.ProjID, projections);
            compute.SetMatrixArray(DepthCapture.ProjInvID, inverseProjections);
            compute.SetMatrixArray(DepthCapture.ViewID, views);
            compute.SetMatrixArray(DepthCapture.ViewInvID, views);
            compute.SetVector(DepthCapture.ZParamsID,
                new Vector4(0.1f, 10f, 0f, 0f));
            compute.SetVector(DepthCapture.TexSizeID,
                new Vector4(width, height, 0f, 0f));
        }

        private static Texture2DArray MakeDepth(int width, int height,
            float leftDepth, float rightDepth)
        {
            var texture = new Texture2DArray(width, height, 2,
                TextureFormat.RFloat, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[width * height];
            Array.Fill(pixels, new Color(leftDepth, 0f, 0f, 0f));
            texture.SetPixels(pixels, 0, 0);
            Array.Fill(pixels, new Color(rightDepth, 0f, 0f, 0f));
            texture.SetPixels(pixels, 1, 0);
            texture.Apply(false, false);
            return texture;
        }

        private static RenderTexture MakeArrayTarget(int width, int height)
        {
            var descriptor = new RenderTextureDescriptor(width, height)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 2,
                graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat,
                enableRandomWrite = true,
                msaaSamples = 1
            };
            var target = new RenderTexture(descriptor);
            target.Create();
            return target;
        }

        private static AsyncGPUReadbackRequest ReadArrayLayer(
            RenderTexture texture, int width, int height, int layer)
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(
                texture, 0, 0, width, 0, height, layer, 1);
            request.WaitForCompletion();
            Assert.That(request.hasError, Is.False,
                $"GPU readback failed for texture-array layer {layer}.");
            return request;
        }

        private static float DepthNdc(Matrix4x4 projection, float distance)
        {
            Vector4 clip = projection * new Vector4(0, 0, -distance, 1);
            return clip.z / clip.w * 0.5f + 0.5f;
        }

        private static void AssertFinite(float4 value, string label)
        {
            Assert.That(float.IsNaN(value.x) || float.IsInfinity(value.x), Is.False, label);
            Assert.That(float.IsNaN(value.y) || float.IsInfinity(value.y), Is.False, label);
            Assert.That(float.IsNaN(value.z) || float.IsInfinity(value.z), Is.False, label);
            Assert.That(float.IsNaN(value.w) || float.IsInfinity(value.w), Is.False, label);
        }
    }
}
