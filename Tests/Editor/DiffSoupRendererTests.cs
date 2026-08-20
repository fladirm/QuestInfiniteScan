using System;
using System.IO;
using System.Linq;
using Genesis.RoomScan.HeavyCompute;
using Genesis.RoomScan.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Tests
{
    public sealed class DiffSoupRendererTests
    {
        [Test]
        public void CpuShaderContractMatchesPinnedUpstreamGoldenVector()
        {
            DiffSoupMlpWeights weights = PatternWeights();
            Assert.That(DiffSoupShaderContract.TryEvaluate(weights,
                    new Vector4(0.2f, 0.4f, 0.6f, 0.35f),
                    new Vector3(0.1f, 0.3f, 0.7f),
                    new Vector3(0.3f, -0.4f, 0.866025403784f),
                    out Vector3 color, out string error), Is.True, error);
            Assert.That(color.x, Is.EqualTo(0.313410994747f).Within(2e-6f));
            Assert.That(color.y, Is.EqualTo(0.418007638792f).Within(2e-6f));
            Assert.That(color.z, Is.EqualTo(0.568819003405f).Within(2e-6f));

            Assert.That(DiffSoupShaderContract.TryPackMlp(weights,
                out DiffSoupPackedMlp packed, out error), Is.True, error);
            Assert.That(packed.W1[0][0, 0], Is.EqualTo(weights.W1[0]));
            Assert.That(packed.W1[6][2, 3], Is.EqualTo(weights.W1[6 * 16 + 11]));
            Assert.That(packed.W3[3][2, 1], Is.EqualTo(weights.W3[2 * 16 + 13]));
        }

        [Test]
        public void LutAddressMatchesPinnedUpstreamSubdivisionGoldenVectors()
        {
            AssertAddress(0, new Vector3(0.2f, 0.3f, 0.5f),
                new Vector3Int(1, 2, 0), new Vector3(0.2f, 0.3f, 0.5f));
            AssertAddress(1, new Vector3(0.2f, 0.25f, 0.55f),
                new Vector3Int(1, 2, 0), new Vector3(0.4f, 0.5f, 0.1f));
            AssertAddress(2, new Vector3(0.73f, 0.11f, 0.16f),
                new Vector3Int(6, 7, 11), new Vector3(0.56f, 0.08f, 0.36f));
            AssertAddress(3, new Vector3(0.12f, 0.81f, 0.07f),
                new Vector3Int(34, 35, 43), new Vector3(0.52f, 0.04f, 0.44f));
        }

        [Test]
        public void LutDecoderMatchesPillowAndWebGlFlipYFalseRowOrder()
        {
            byte[] pillowPng = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAGElEQVR4nAXBAQEAAAjDIG7/zhNE0k3CAz7tBf5/xlWuAAAAAElFTkSuQmCC");
            Assert.That(DiffSoupRendererCache.TryDecodeLut(pillowPng, 2, 2,
                out Texture2D texture, out string error, false), Is.True, error);
            try
            {
                Assert.That(GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat), Is.False);
                Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Point));
                AssertColor(texture.GetPixel(0, 0), Color.red);
                AssertColor(texture.GetPixel(1, 0), Color.green);
                AssertColor(texture.GetPixel(0, 1), Color.blue);
                AssertColor(texture.GetPixel(1, 1), new Color(1f, 1f, 0f, 1f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void RendererPromotionIsAtomicPoseAwareAndDisposesResources()
        {
            var root = new GameObject("DiffSoup renderer test");
            try
            {
                var cache = root.AddComponent<DiffSoupRendererCache>();
                cache.Initialize(null, null, null, 2);
                ChunkRecord chunk = Chunk(1, new Vector3(1f, 2f, 3f));
                DiffSoupArtifactData firstData = Data(1);
                ChunkArtifactRecord firstArtifact = Artifact(1, 'a');

                Assert.That(cache.TryPromote(chunk, firstData, firstArtifact,
                    out string firstError), Is.True, firstError);
                Assert.That(cache.Count, Is.EqualTo(1));
                Assert.That(cache.TryGetEntryInfo(chunk.chunkId, out int revision,
                    out string hash, out Transform child, out MeshRenderer renderer), Is.True);
                Assert.That((revision, hash), Is.EqualTo((1, new string('a', 64))));
                Assert.That(child.position, Is.EqualTo(new Vector3(1f, 2f, 3f)));
                Assert.That(renderer.enabled, Is.True);
                Assert.That(renderer.sharedMaterial.shader.name,
                    Is.EqualTo("Genesis/RoomScan/DiffSoup"));

                chunk.revision = 2;
                DiffSoupArtifactData invalid = Data(2);
                invalid.Manifest.model.featureEncoding = "unsupported-network";
                Assert.That(cache.TryPromote(chunk, invalid, Artifact(2, 'b'),
                    out string invalidError), Is.False);
                Assert.That(invalidError, Does.Contain("unsupported"));
                Assert.That(cache.TryGetEntryInfo(chunk.chunkId, out revision,
                    out hash, out Transform preserved, out _), Is.True);
                Assert.That((revision, hash), Is.EqualTo((1, new string('a', 64))));
                Assert.That(preserved, Is.SameAs(child));

                DiffSoupArtifactData replacement = Data(2);
                Assert.That(cache.TryPromote(chunk, replacement, Artifact(2, 'b'),
                    out string replaceError), Is.True, replaceError);
                Assert.That(cache.TryGetEntryInfo(chunk.chunkId, out revision,
                    out hash, out Transform replaced, out renderer), Is.True);
                Assert.That((revision, hash), Is.EqualTo((2, new string('b', 64))));
                Assert.That(replaced, Is.Not.SameAs(child));

                cache.SetRenderMode(ScanRenderMode.None);
                Assert.That(renderer.enabled, Is.False);
                cache.SetRenderMode(ScanRenderMode.Occlusion);
                Assert.That(renderer.enabled, Is.True);
                Assert.That(renderer.sharedMaterial.GetFloat("_DepthOnly"), Is.EqualTo(1f));
                Assert.That(renderer.sharedMaterial.GetInt("_ColorMask"), Is.Zero);

                chunk.worldFromChunk = new RigidPoseData(new Vector3(5f, 0f, -2f),
                    Quaternion.Euler(0f, 45f, 0f));
                var manifest = new WorldManifest { chunks = new System.Collections.Generic.List<ChunkRecord> { chunk } };
                cache.RefreshTransforms(manifest);
                Assert.That(replaced.position, Is.EqualTo(chunk.worldFromChunk.position));
                Assert.That(Quaternion.Angle(replaced.rotation,
                    chunk.worldFromChunk.rotation), Is.LessThan(0.01f));

                cache.Clear();
                Assert.That(cache.Count, Is.Zero);
                Assert.That(root.transform.childCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RendererRejectsMobileBufferAndLutCapacityViolations()
        {
            DiffSoupArtifactData data = Data(1);
            data.Manifest.model.lutWidth = 2;
            Assert.That(DiffSoupShaderContract.TryValidateRendererData(data,
                out string lutError), Is.False);
            Assert.That(lutError, Does.Contain("LUT capacity"));

            data = Data(1);
            data.Manifest.model.numFaces = DiffSoupShaderContract.MaximumRenderedFaces + 1;
            Assert.That(DiffSoupShaderContract.TryValidateRendererData(data,
                out string faceError), Is.False);
            Assert.That(faceError, Does.Contain("counts"));
        }

        [Test]
        public void ActualCudaArtifactBuildsRendererAndMatchesUpstreamMlpGolden()
        {
            string fixtureRoot = Environment.GetEnvironmentVariable(
                "QIS_DIFFSOUP_CUDA_FIXTURE");
            if (string.IsNullOrEmpty(fixtureRoot))
                Assert.Ignore("Set QIS_DIFFSOUP_CUDA_FIXTURE to a completed server job root.");
            const string jobId =
                "9cf9ccbcdcd863c5372a6bec1844552c4917be555f93ce4512cff2e479bade1c";
            string inputPath = Path.Combine(fixtureRoot, "uploads", jobId + ".zip");
            string artifactPath = Path.Combine(fixtureRoot, "artifacts", jobId + ".zip");
            var inputDescriptor = new HeavyComputeBlobDescriptor
            {
                mediaType = HeavyComputeProtocol.ChunkBundleMediaType,
                formatVersion = 1,
                byteLength = new FileInfo(inputPath).Length,
                sha256 = Hashing.ComputeSha256(inputPath)
            };
            Assert.That(HeavyComputeSubmission.TryCreate(new HeavyComputeJobKey(
                    "world-bundle", "chunk-000000", 1), inputDescriptor, "preview", true,
                null, out HeavyComputeSubmission submission, out string submissionError),
                Is.True, submissionError);
            var artifactDescriptor = new HeavyComputeBlobDescriptor
            {
                mediaType = HeavyComputeProtocol.DiffSoupArtifactMediaType,
                formatVersion = 1,
                byteLength = new FileInfo(artifactPath).Length,
                sha256 = Hashing.ComputeSha256(artifactPath)
            };
            DiffSoupArtifactImportResult imported = DiffSoupArtifactImporter.Import(
                artifactPath, submission, artifactDescriptor);
            Assert.That(imported.Success, Is.True, imported.Error);
            Assert.That(DiffSoupShaderContract.TryEvaluate(imported.Data.Mlp,
                    new Vector4(0.2f, 0.4f, 0.6f, 0.35f),
                    new Vector3(0.1f, 0.3f, 0.7f),
                    new Vector3(0.3f, -0.4f, 0.866025403784f),
                    out Vector3 color, out string evaluateError), Is.True, evaluateError);
            Assert.That(color.x, Is.EqualTo(0.257376051514f).Within(2e-6f));
            Assert.That(color.y, Is.EqualTo(0.428257062395f).Within(2e-6f));
            Assert.That(color.z, Is.EqualTo(0.509848047696f).Within(2e-6f));

            var root = new GameObject("Actual CUDA DiffSoup renderer test");
            try
            {
                var cache = root.AddComponent<DiffSoupRendererCache>();
                cache.Initialize(null, null, null, 1);
                ChunkRecord chunk = Chunk(1, Vector3.zero);
                var record = Artifact(1, 'c');
                record.sha256 = artifactDescriptor.sha256;
                record.byteLength = artifactDescriptor.byteLength;
                Assert.That(cache.TryPromote(chunk, imported.Data, record,
                    out string rendererError), Is.True, rendererError);
                Assert.That(cache.TryGetEntryInfo(chunk.chunkId, out _, out _, out _,
                    out MeshRenderer renderer), Is.True);
                Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount,
                    Is.EqualTo(3));
                Assert.That(renderer.sharedMaterial.GetTexture("_Lut0").width, Is.EqualTo(3));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void UnityGpuShaderMatchesCpuReferenceAtRenderedTriangleCenter()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Run this parity gate with a real Unity graphics device.");

            var root = new GameObject("DiffSoup GPU parity root");
            var cameraHost = new GameObject("DiffSoup GPU parity camera");
            RenderTexture target = null;
            Texture2D readback = null;
            try
            {
                var cache = root.AddComponent<DiffSoupRendererCache>();
                cache.Initialize(null, null, null, 1);
                DiffSoupArtifactData data = Data(1);
                ChunkRecord chunk = Chunk(1, Vector3.zero);
                Assert.That(cache.TryPromote(chunk, data, Artifact(1, 'd'),
                    out string promotionError), Is.True, promotionError);
                Assert.That(cache.TryGetEntryInfo(chunk.chunkId, out _, out _, out _,
                    out MeshRenderer renderer), Is.True);

                var camera = cameraHost.AddComponent<Camera>();
                camera.transform.SetPositionAndRotation(new Vector3(0.25f, 0.25f, -2f),
                    Quaternion.identity);
                camera.orthographic = true;
                camera.orthographicSize = 0.75f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 10f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Linear)
                {
                    antiAliasing = 1,
                    name = "DiffSoup GPU parity target"
                };
                Assert.That(target.Create(), Is.True);
                camera.targetTexture = target;
                Shader.SetGlobalFloat("_RSWireframe", 0f);
                renderer.enabled = true;
                camera.Render();

                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    readback = new Texture2D(64, 64, TextureFormat.RGBA32,
                        false, true);
                    readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0, false);
                    readback.Apply(false, false);
                }
                finally
                {
                    RenderTexture.active = previous;
                }

                float value = 128f / 255f;
                Assert.That(DiffSoupShaderContract.TryEvaluate(data.Mlp,
                        new Vector4(value, value, value, value),
                        new Vector3(value, value, value), Vector3.back,
                        out Vector3 encoded, out string evaluateError), Is.True,
                    evaluateError);
                Color expected = new Color(encoded.x, encoded.y, encoded.z, 1f).linear;
                Color actual = readback.GetPixel(32, 32);
                Assert.That(actual.a, Is.GreaterThan(0.95f));
                Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.025f));
                Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.025f));
                Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.025f));
                Assert.That(readback.GetPixel(0, 0).maxColorComponent,
                    Is.LessThan(0.02f), "Triangle coordinate coverage is inverted.");
            }
            finally
            {
                Camera cleanupCamera = cameraHost != null
                    ? cameraHost.GetComponent<Camera>()
                    : null;
                if (cleanupCamera != null)
                    cleanupCamera.targetTexture = null;
                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }
                UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(cameraHost);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void AssertAddress(int level, Vector3 barycentric,
            Vector3Int expectedIndices, Vector3 expectedWeights)
        {
            Assert.That(DiffSoupShaderContract.TryLutAddress(level, 0, barycentric,
                4096, 1, out Vector3Int indices, out Vector3 weights,
                out string error), Is.True, error);
            Assert.That(indices, Is.EqualTo(expectedIndices));
            Assert.That(weights.x, Is.EqualTo(expectedWeights.x).Within(1e-6f));
            Assert.That(weights.y, Is.EqualTo(expectedWeights.y).Within(1e-6f));
            Assert.That(weights.z, Is.EqualTo(expectedWeights.z).Within(1e-6f));
        }

        private static DiffSoupMlpWeights PatternWeights() => new()
        {
            W1 = Enumerable.Range(0, 256).Select(i => ((i % 17) - 8) * 0.01f).ToArray(),
            b1 = Enumerable.Range(0, 16).Select(i => ((i % 5) - 2) * 0.02f).ToArray(),
            W2 = Enumerable.Range(0, 256).Select(i => ((i % 13) - 6) * 0.015f).ToArray(),
            b2 = Enumerable.Range(0, 16).Select(i => ((i % 7) - 3) * 0.01f).ToArray(),
            W3 = Enumerable.Range(0, 48).Select(i => ((i % 11) - 5) * 0.02f).ToArray(),
            b3 = new[] { 0.1f, -0.2f, 0.05f }
        };

        private static DiffSoupArtifactData Data(int revision)
        {
            byte[] png = Png(3, 1);
            return new DiffSoupArtifactData
            {
                Manifest = new DiffSoupArtifactManifest
                {
                    key = new HeavyComputeJobKey("world-render", "chunk-000000", revision),
                    model = new DiffSoupModelDescription
                    {
                        meshSpace = "chunk-local",
                        coordinateSystem = "unity-lh-y-up-z-forward",
                        units = "meter",
                        frontFace = "clockwise",
                        featureEncoding = DiffSoupShaderContract.FeatureEncoding,
                        level = 0,
                        numVertices = 3,
                        numFaces = 1,
                        lutWidth = 3,
                        lutHeight = 1
                    }
                },
                Positions = new[] { Vector3.zero, Vector3.right, Vector3.up },
                Indices = new[] { 0, 1, 2 },
                Lut0Png = png,
                Lut1Png = png,
                Mlp = PatternWeights(),
                Metadata = new DiffSoupMetadata
                {
                    up = new[] { 0f, 1f, 0f }, background = new float[3],
                    level = 0, num_faces = 1, num_verts = 3
                }
            };
        }

        private static ChunkRecord Chunk(int revision, Vector3 position) => new()
        {
            chunkId = "chunk-000000",
            revision = revision,
            state = ChunkLifecycleState.Persisted,
            worldFromChunk = new RigidPoseData(position, Quaternion.identity),
            localBounds = new BoundsData(Vector3.zero, Vector3.one)
        };

        private static ChunkArtifactRecord Artifact(int revision, char hashCharacter) => new()
        {
            kind = ChunkArtifactKind.DiffSoup,
            formatVersion = HeavyComputeProtocol.DiffSoupArtifactVersion,
            chunkRevision = revision,
            relativePath = "chunks/chunk-000000/enhancements/" + revision + "/diffsoup.zip",
            sha256 = new string(hashCharacter, 64),
            byteLength = 1
        };

        private static byte[] Png(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                texture.SetPixels32(Enumerable.Repeat(new Color32(128, 128, 128, 255),
                    width * height).ToArray());
                texture.Apply(false, false);
                return texture.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void AssertColor(Color actual, Color expected)
        {
            const float tolerance = 1f / 255f;
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(tolerance));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(tolerance));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(tolerance));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(tolerance));
        }
    }
}
