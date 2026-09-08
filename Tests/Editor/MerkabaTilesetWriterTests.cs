using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Genesis.RoomScan;
using Genesis.RoomScan.UI;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaTilesetWriterTests
    {
        [Test]
        public void QuestArtifactPreviewReadsTheFrozenWriterAbiBackIntoUnitySpace()
        {
            MerkabaFlowerPresentation flower = Fixture();
            Vector3 origin = new(0.25f, -0.5f, 0.75f);
            Vector3 center = new(0.1f, 0.2f, 0.3f);
            using var stream = new MemoryStream();
            _ = MerkabaGlbWriter.Write(stream, flower, origin);
            byte[] glb = stream.ToArray();

            MerkabaArtifactViewer.ParsedGlb parsed =
                MerkabaArtifactViewer.ParseGlbForPreview(glb);
            using var streamed = new MemoryStream(glb, false);
            MerkabaArtifactViewer.ParsedGlb streamedParsed =
                MerkabaArtifactViewer.ParseGlbForPreview(streamed,
                    streamed.Length);

            Assert.That(parsed.Positions.Length, Is.GreaterThan(0));
            Assert.That(parsed.Normals.Length, Is.EqualTo(parsed.Positions.Length));
            Assert.That(parsed.Colors.Length, Is.EqualTo(parsed.Positions.Length));
            Assert.That(parsed.Indices.Length % 3, Is.Zero);
            Assert.That(parsed.Indices.Length, Is.EqualTo(3 * flower.TriangleCount),
                "Every current primitive/index accessor must be consumed, not only accessor 3.");
            Assert.That(parsed.Primitives.Sum(value => value.IndexCount), Is.EqualTo(parsed.Indices.Length));
            Assert.That(parsed.Images.All(value => value == null), Is.True,
                "Uniform captured RGB needs no decoded texture.");
            Assert.That(parsed.Materials.All(value => value.Image == -1), Is.True,
                "An absent baseColorTexture is not JsonUtility's constructed default texture reference.");
            Assert.That(parsed.Positions.Any(value =>
                Vector3.Distance(value + origin - center,
                    (Vector3)flower.Positions[0] - center) < 1e-6f),
                Is.True);
            Color32 expected = new(25, 100, 220, 255);
            Assert.That(parsed.Colors.Any(value => value.r == expected.r &&
                value.g == expected.g && value.b == expected.b), Is.True);
            Assert.That(streamedParsed.Positions, Is.EqualTo(parsed.Positions));
            Assert.That(streamedParsed.Normals, Is.EqualTo(parsed.Normals));
            Assert.That(streamedParsed.Colors, Is.EqualTo(parsed.Colors));
            Assert.That(streamedParsed.Indices, Is.EqualTo(parsed.Indices));
            Assert.That(streamedParsed.DecodedBytes, Is.EqualTo(
                parsed.DecodedBytes));
        }

        [Test]
        public void QuestArtifactPreviewConsumesCapturedTextureSubmeshAndKeepsStyleSeparate()
        {
            MerkabaFlowerPresentation flower = MerkabaFlowerWriterFixture.Create();
            var carrier = flower.Carriers[0];
            carrier.Symbol = MerkabaFlowerSymbolRecord.CreateCarrier(0, 0, 1, false, false, 1u, 0u, 0, 0u, 0u);
            carrier.SkinHeader = new MerkabaFlowerSkinDrawHeader { SplitBitsLo = 1u, ParentEpoch = 1u };
            // The atlas cell size comes from the scan-authored RGB split, not
            // from differing sample values alone. Declare the L2 split this
            // fixture is claiming instead of leaving the RGB mask empty.
            carrier.RgbSplitBits = new uint2(1u, 0u);
            carrier.SkinSamples = new MerkabaFlowerSkinDrawSample[8];
            for (int sample = 0; sample < carrier.SkinSamples.Length; sample++)
                carrier.SkinSamples[sample].CapturedRgb = sample == 0 ? new float3(0f, 0f, 1f) :
                    (sample & 1) == 0 ? new float3(1f, 0f, 0f) : new float3(0f, 1f, 0f);
            flower.TriangleCount = 1;
            using var stream = new MemoryStream();
            MerkabaGlbWriter.Write(stream, flower, float3.zero);
            var parsed = MerkabaArtifactViewer.ParseGlbForPreview(stream.ToArray());
            Assert.That(parsed.Indices.Length, Is.EqualTo(3));
            Assert.That(parsed.Uvs.Length, Is.EqualTo(parsed.Positions.Length));
            Assert.That(parsed.Primitives.Length, Is.EqualTo(1));
            int materialIndex = parsed.Primitives[0].Material;
            int imageIndex = parsed.Materials[materialIndex].Image;
            Assert.That(imageIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(parsed.Images[imageIndex], Is.Not.Null);
            Assert.That(parsed.Materials[0].Image, Is.EqualTo(-1),
                "A uniform material remains untextured even when another material embeds a texture.");
            foreach (string uri in new[] { "\"\"", "\"https://invalid.example/capture.png\"", "null" })
            {
                byte[] external = RewritePreviewJson(stream.ToArray(), json => json.Replace(
                    "\"mimeType\":\"image/png\"", "\"mimeType\":\"image/png\",\"uri\":" + uri));
                Assert.Throws<InvalidDataException>(() => MerkabaArtifactViewer.ParseGlbForPreview(external),
                    "Image URI presence is forbidden, including empty/default-like values.");
            }
            Assert.That(parsed.DecodedBytes, Is.GreaterThanOrEqualTo(4L * 256 * 256));
            var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(
                "Packages/com.genesis.roomscan/Runtime/Shaders/MerkabaArtifactPreview.shader");
            Assert.That(shader, Is.Not.Null);
            var template = new Material(shader);
            try
            {
                MerkabaArtifactViewer.ConfigureMaterial(template, Color.white, true);
                using var preview = MerkabaArtifactViewer.PreviewGlb.Create(parsed, template, "Captured fixture");
                Assert.That(preview.Mesh.subMeshCount, Is.EqualTo(1));
                Assert.That(preview.Mesh.triangles, Is.EqualTo(parsed.Indices));
                Assert.That(preview.Mesh.uv, Is.EqualTo(parsed.Uvs));
                Texture texture = preview.Materials[0].GetTexture("_BaseMap");
                Assert.That(texture, Is.Not.SameAs(Texture2D.whiteTexture));
                // One atlas page carries every carrier cell; the cell size,
                // not the page, follows the scan-authored RGB split.
                Assert.That(texture.width,
                    Is.EqualTo(MerkabaFlowerMaterialBake.Resolution));
                var pixels = ((Texture2D)texture).GetPixels32();
                Assert.That(pixels.Any(value => value.r != 255 || value.g != 255 || value.b != 255), Is.True);
                const int n = MerkabaFlowerMaterialBake.Resolution;
                float edge = 0.5f / n;
                // Seven shared chart sites carry seven UVs, not one tuple per
                // triangle corner. Every UV must land inside this carrier's
                // atlas cell, and the hub must sit at the cell centre.
                Assert.That(parsed.Uvs.Length, Is.EqualTo(parsed.Positions.Length));
                Assert.That(parsed.Uvs.Length, Is.EqualTo(7));
                Vector2 hub = parsed.Uvs[0];
                foreach (Vector2 uv in parsed.Uvs)
                {
                    Assert.That(uv.x, Is.InRange(0f, 1f));
                    Assert.That(uv.y, Is.InRange(0f, 1f));
                    Assert.That(Vector2.Distance(uv, hub),
                        Is.LessThanOrEqualTo((float)MerkabaFlowerMaterialBake.MaximumCellSize / n),
                        "every shared site stays inside one atlas cell");
                }
                flower.WedgeFrame(carrier, 0, out _, out _, out _, out float3 du, out float3 dv);
                // The carrier owns one atlas cell, not the whole page, so the
                // chart is read back in cell-local texels. The invariants are
                // unchanged: no row flip and no filtering blend.
                int cellSize = MerkabaFlowerMaterialBake.CellSize(carrier);
                int gutter = MerkabaFlowerMaterialBake.Gutter;
                int inner = cellSize - 2 * gutter;
                int probed = 0;
                for (int ty = gutter; ty < gutter + inner; ty += 3)
                for (int tx = gutter; tx < gutter + inner; tx += 3)
                {
                    float2 chart = (new float2(tx + 0.5f, ty + 0.5f) - gutter)
                        / inner * 2f - 1f;
                    if (!MerkabaSphereFlowerAuthority.TryL2CarrierChartWedge(chart,
                            out int wedge, out float3 bc, out _, out _) || wedge != 0)
                        continue;
                    var source = MerkabaSphereFlowerAuthority.EvaluateSkinDrawSignal(carrier.SkinHeader,
                        carrier.SkinSamples, 0, bc, du, dv, out _, out _, out _);
                    // This fixture uses exact 0/1 channels, so PNG quantization
                    // and sRGB decode preserve the expected channels exactly.
                    Color expected = new(source.CapturedRgb.x, source.CapturedRgb.y, source.CapturedRgb.z, 1f);
                    int row = n - 1 - ty;
                    Assert.That(((Texture2D)texture).GetPixel(tx, row).linear, Is.EqualTo(expected),
                        "Baker PNG row must be the actual vertically corresponding Unity row.");
                    Vector2 center = new((tx + 0.5f) / n, (row + 0.5f) / n);
                    Assert.That(preview.SampleCapture(0, center), Is.EqualTo(expected),
                        "A raster texel center must not blend with the next CPU texel.");
                    probed++;
                }
                Assert.That(probed, Is.GreaterThan(0),
                    "the carrier cell must contain sampled interior texels");
                int patch = -1;
                for (int y = 0; y < n - 1 && patch < 0; y++)
                for (int x = 0; x < n - 1; x++)
                {
                    int p = y * n + x;
                    if (pixels[p].Equals(pixels[p + 1]) && pixels[p].Equals(pixels[p + n]) &&
                        pixels[p].Equals(pixels[p + n + 1])) continue;
                    patch = p; break;
                }
                Assert.That(patch, Is.GreaterThanOrEqualTo(0), "Fixture must exercise actual chromatic filtering.");
                Color mean = (((Color)pixels[patch]).linear + ((Color)pixels[patch + 1]).linear +
                    ((Color)pixels[patch + n]).linear + ((Color)pixels[patch + n + 1]).linear) * 0.25f;
                Assert.That(preview.SampleCapture(0, new Vector2((patch % n + 1f) / n, (patch / n + 1f) / n)),
                    Is.EqualTo(mean), "Decode all four sRGB taps BEFORE the linear blend.");
                Assert.That(preview.SampleCapture(0, new Vector2(-1f, -1f)),
                    Is.EqualTo(((Color)pixels[0]).linear));
                Assert.That(preview.SampleCapture(0, new Vector2(2f, 2f)),
                    Is.EqualTo(((Color)pixels[pixels.Length - 1]).linear));
                template.SetColor("_BaseColor", new Color(0.2f, 0.4f, 0.6f, 0.5f));
                preview.ApplyStyle(template);
                Assert.That(preview.Materials[0].GetTexture("_BaseMap"), Is.SameAs(texture));
                Assert.That(preview.Materials[0].GetColor("_CapturedColorFactor"),
                    Is.EqualTo(parsed.Materials[materialIndex].ColorFactor));
            }
            finally { UnityEngine.Object.DestroyImmediate(template); }
        }

        [TestCase("\"baseColorTexture\":null")]
        [TestCase("\"baseColorTexture\":{}")]
        [TestCase("\"baseColorTexture\":[]")]
        [TestCase("\"baseColorTexture\":{\"index\":-1}")]
        [TestCase("\"baseColorTexture\":{\"index\":2147483648}")]
        [TestCase("\"baseColorTexture\":{\"index\":\"0\"}")]
        [TestCase("\"baseColorTexture\":{\"index\":0}")]
        [TestCase("\"_m8PreviewHasBaseColorTexture\":true")]
        [TestCase("\"_m8PreviewHasIndex\":true")]
        public void QuestArtifactPreviewRejectsMalformedPresentTextureWithoutFallback(string property)
        {
            using var stream = new MemoryStream();
            MerkabaGlbWriter.Write(stream, MerkabaFlowerWriterFixture.Create(), float3.zero);
            byte[] malformed = RewritePreviewJson(stream.ToArray(), json => json.Replace(
                "\"pbrMetallicRoughness\":{", "\"pbrMetallicRoughness\":{" + property + ","));
            Assert.Throws<InvalidDataException>(() => MerkabaArtifactViewer.ParseGlbForPreview(malformed));
        }

        [Test]
        public void QuestArtifactPreviewIgnoresTextureWordsInsideJsonStringValues()
        {
            using var stream = new MemoryStream();
            MerkabaGlbWriter.Write(stream, MerkabaFlowerWriterFixture.Create(), float3.zero);
            byte[] glb = RewritePreviewJson(stream.ToArray(), json =>
                "{\"note\":\"\\\"baseColorTexture\\\":{\\\"index\\\":0},_m8PreviewHasIndex\"," + json.Substring(1));
            var parsed = MerkabaArtifactViewer.ParseGlbForPreview(glb);
            Assert.That(parsed.Materials.All(value => value.Image == -1), Is.True);
            Assert.That(parsed.Images.All(value => value == null), Is.True);
        }

        private static byte[] RewritePreviewJson(byte[] glb, Func<string, string> rewrite)
        {
            int oldLength = checked((int)BitConverter.ToUInt32(glb, 12));
            string json = Encoding.UTF8.GetString(glb, 20, oldLength).TrimEnd(' ', '\0');
            byte[] changed = Encoding.UTF8.GetBytes(rewrite(json));
            int length = checked((changed.Length + 3) & ~3);
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output);
            writer.Write(0x46546c67u); writer.Write(2u);
            writer.Write(checked((uint)(glb.Length - oldLength + length)));
            writer.Write(length); writer.Write(0x4e4f534au); writer.Write(changed);
            for (int pad = changed.Length; pad < length; pad++) writer.Write((byte)' ');
            writer.Write(glb, 20 + oldLength, glb.Length - 20 - oldLength);
            return output.ToArray();
        }

        [Test]
        public void QuestArtifactPreviewConsumesDirtFactorWithoutAllocatingTexture()
        {
            using var stream = new MemoryStream();
            MerkabaGlbWriter.WriteDirt(stream, new[] { new MerkabaDirtTriangle(new int3(-8), 0, 0) }, float3.zero);
            var parsed = MerkabaArtifactViewer.ParseGlbForPreview(stream.ToArray());
            ParsedDirtAssertions(parsed);
        }

        private static void ParsedDirtAssertions(MerkabaArtifactViewer.ParsedGlb parsed)
        {
            Assert.That(parsed.Indices.Length, Is.EqualTo(3));
            Assert.That(parsed.Images.All(value => value == null), Is.True);
            var material = parsed.Materials[parsed.Primitives[0].Material];
            Assert.That(material.Image, Is.EqualTo(-1));
            float4 support = MerkabaSphereFlowerAuthority.DirtSupportLinearRgba;
            Assert.That(material.ColorFactor, Is.EqualTo(new Color(support.x, support.y, support.z, support.w)));
        }

        [Test]
        public void QuestArtifactPreviewStreamsUnboundedPackageIntoBoundedCache()
        {
            string viewer = Source(
                "Runtime/UI/MerkabaArtifactViewer.cs");
            string setup = Source("Editor/RoomScanSetupWizard.cs");
            string menu = Source("Runtime/UI/DebugMenu.uxml");
            string shader = Source(
                "Runtime/Shaders/MerkabaArtifactPreview.shader");
            string picker = Source(
                "Runtime/Plugins/Android/MerkabaPackagePicker.java");

            Assert.That(viewer, Does.Contain(
                "systemBytes * 3L / 8L"));
            Assert.That(viewer, Does.Contain(
                "MaximumConcurrentTileLoads = 4"));
            Assert.That(viewer, Does.Contain(
                "GlbReadBufferBytes = 1024 * 1024"));
            Assert.That(viewer, Does.Contain("ReadPackageIndex(archivePath)"));
            Assert.That(viewer, Does.Contain("ReadGlbTile("));
            Assert.That(viewer, Does.Contain("new BufferedStream(entryStream"));
            Assert.That(viewer, Does.Not.Contain(
                "new byte[(int)entry.Length]"));
            Assert.That(viewer, Does.Not.Contain(
                "Preview tile is too large"));
            Assert.That(viewer, Does.Contain("TouchScreenKeyboard.Open("));
            Assert.That(viewer, Does.Contain("AnnotationMode.Select"));
            Assert.That(viewer, Does.Contain("BeginAnnotationDrag(ray)"));
            Assert.That(viewer, Does.Contain("DeleteSelectedAnnotation()"));
            Assert.That(viewer, Does.Contain("DestroyTile(tile)"));
            Assert.That(viewer, Does.Contain("_scanner.ReadoutDrawEnabled = false"));
            Assert.That(viewer, Does.Contain("RoomSpaceRoot.Instance"));
            Assert.That(viewer, Does.Contain("OneHandGrab"));
            Assert.That(viewer, Does.Contain("TwoHandGrab"));
            Assert.That(viewer, Does.Contain("TryBuildTwoHandFrame("));
            Assert.That(viewer, Does.Not.Contain("_grabDistance"));
            Assert.That(viewer, Does.Not.Contain(
                "moveDirection * initialPreviewDistance"));
            Assert.That(viewer, Does.Contain(
                "ComposeAlignedModelLocal("));
            Assert.That(viewer, Does.Contain(
                "LocalizeArtifactAnchorAsync("));
            Assert.That(viewer, Does.Contain(
                "public void RequestPackageFromDisk()"));
            Assert.That(viewer, Does.Contain(
                "ReadGlbTile(\n                    archivePath, tile, decodedLimit)"));
            Assert.That(viewer, Does.Contain(
                "Physics.queriesHitBackfaces = true"));
            Assert.That(viewer, Does.Contain(
                "AnnotationPointRadius = 0.025f"));
            Assert.That(viewer, Does.Contain(
                "AnnotationLineWidth = 0.01f"));
            Assert.That(viewer, Does.Contain(
                "AnnotationPlaneAlpha = 0.2f"));
            Assert.That(viewer, Does.Contain(
                "TouchScreenKeyboardType.Default, false, false, false, false"));
            Assert.That(viewer, Does.Contain(
                "keyboardStatus ==\n                TouchScreenKeyboard.Status.Done"));
            Assert.That(viewer, Does.Not.Contain(
                "SetAnnotationNote(_noteAnnotationId, _noteBeforeKeyboard)"));
            Assert.That(viewer, Does.Contain(
                "MerkabaArtifactPaintTool.SurfaceBrush"));
            Assert.That(viewer, Does.Contain(
                "MerkabaArtifactPaintTool.SpatialBrush"));
            Assert.That(viewer, Does.Contain("MerkabaPaintEngine"));
            Assert.That(viewer, Does.Contain("SaveDesign()"));
            Assert.That(viewer, Does.Not.Contain("_spatialPaintDistance"));
            Assert.That(viewer, Does.Not.Contain("_paintDraftLine"));
            Assert.That(viewer, Does.Contain("version = 2"));
            Assert.That(viewer, Does.Contain("public bool PlanViewEnabled"));
            Assert.That(viewer, Does.Contain(
                "_modelMaterial.EnableKeyword(PlanKeyword)"));
            Assert.That(viewer, Does.Not.Contain("KernelState"));
            Assert.That(shader, Does.Contain("_AlphaDither"));
            Assert.That(shader, Does.Contain(
                "#pragma multi_compile_local_fragment _ M8_ARTIFACT_PLAN"));
            Assert.That(shader, Does.Contain(
                "displayColor = _PlanColor"));
            Assert.That(shader, Does.Contain(
                "clip(displayColor.a - threshold)"));
            Assert.That(setup, Does.Contain(
                "GetOrAdd<MerkabaArtifactViewer>(scannerObject)"));
            Assert.That(setup, Does.Contain("requiresSystemKeyboard = true"));
            Assert.That(setup, Does.Contain(
                "AndroidApplicationEntry.Activity"));
            Assert.That(setup, Does.Contain("UnityPlayerActivity"));
            Assert.That(setup, Does.Not.Contain("UnityPlayerGameActivity"));
            Assert.That(setup, Does.Contain("@style/UnityThemeSelector"));
            Assert.That(setup, Does.Not.Contain("@style/Theme.AppCompat"));
            Assert.That(menu, Does.Contain("btn-artifact-view"));
            Assert.That(menu, Does.Contain("btn-artifact-load"));
            Assert.That(menu, Does.Contain("btn-annotation-mode"));
            Assert.That(menu, Does.Contain("btn-annotation-edit"));
            Assert.That(menu, Does.Contain("btn-annotation-delete"));
            Assert.That(menu, Does.Contain("artifact-world-lock"));
            Assert.That(menu, Does.Contain("artifact-room-align"));
            Assert.That(menu, Does.Contain("annotation-note"));
            Assert.That(menu, Does.Contain("btn-tab-design"));
            Assert.That(menu, Does.Contain("paint-color-swatch"));
            Assert.That(menu, Does.Contain("btn-tab-view"));
            Assert.That(menu, Does.Contain("btn-plan-style"));
            Assert.That(menu, Does.Contain("paint-color-wheel"));
            Assert.That(menu, Does.Contain("btn-paint-eyedropper"));
            Assert.That(picker, Does.Contain("Intent.ACTION_OPEN_DOCUMENT"));
            Assert.That(picker, Does.Contain("byte[] buffer = new byte[1024 * 1024]"));
            Assert.That(picker, Does.Contain("MessageDigest.getInstance("));
            Assert.That(picker, Does.Not.Contain("readAllBytes"));
        }

        [Test]
        public void PackageSpatialBindingRoundTripsAndPlacesPackagePointsExactly()
        {
            string root = TemporaryDirectory();
            try
            {
                Guid uuid = Guid.Parse(
                    "3d2504e0-4f89-41d3-9a0c-0305e82c3301");
                Matrix4x4 anchorFromPackage = Matrix4x4.TRS(
                    new Vector3(1.25f, -0.4f, 3.5f),
                    Quaternion.Euler(0f, 37f, 0f), Vector3.one);
                var expected = new MerkabaSpatialBinding(uuid,
                    anchorFromPackage);
                _ = MerkabaFlowerWriterFixture.StreamPackage(root, Fixture(),
                    hardLeafBytes: 2_000_000,
                    binding: expected);

                string json = File.ReadAllText(Path.Combine(root,
                    "tileset.json"));
                Assert.That(json, Does.Contain(
                    "\"questMerkabaSpatialBinding\""));
                Assert.That(MerkabaArtifactViewer.TryParseSpatialBinding(
                    json, out MerkabaSpatialBinding actual), Is.True);
                Assert.That(actual.AnchorUuid, Is.EqualTo(uuid));
                for (int column = 0; column < 4; column++)
                for (int row = 0; row < 4; row++)
                    Assert.That(actual.AnchorFromPackage[row, column],
                        Is.EqualTo(anchorFromPackage[row, column]).Within(1e-6f));

                Vector3 center = new(4f, 1f, -2f);
                Vector3 packagePoint = new(4.8f, 1.2f, -1.7f);
                Matrix4x4 modelToAnchor =
                    MerkabaArtifactViewer.ComposeAlignedModelLocal(
                        actual.AnchorFromPackage, center);
                Vector3 actualPoint = modelToAnchor.MultiplyPoint3x4(
                    packagePoint - center);
                Vector3 expectedPoint = anchorFromPackage.MultiplyPoint3x4(
                    packagePoint);
                Assert.That(Vector3.Distance(actualPoint, expectedPoint),
                    Is.LessThan(1e-5f));
                Assert.That(MerkabaArtifactViewer.TryDecomposeTransform(
                    modelToAnchor, out _, out _, out Vector3 scale), Is.True);
                Assert.That(Vector3.Distance(scale, Vector3.one),
                    Is.LessThan(1e-5f));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void QuestArtifactPreviewLoadsEveryTileThatFitsAndOnlyMarksLargeContinuation()
        {
            const long gibibyte = 1024L * 1024L * 1024L;

            Assert.That(MerkabaArtifactViewer.FitsResidentBudget(
                gibibyte, gibibyte), Is.True);
            Assert.That(MerkabaArtifactViewer.FitsResidentBudget(
                gibibyte + 1L, gibibyte), Is.False);
            Assert.That(MerkabaArtifactViewer.EstimateResidentBytes(
                64L * 1024L * 1024L),
                Is.EqualTo(128L * 1024L * 1024L));
            Assert.That(MerkabaArtifactViewer.NeedsContinuationMarker(
                gibibyte, 1, 2), Is.False);
            Assert.That(MerkabaArtifactViewer.NeedsContinuationMarker(
                gibibyte + 1L, 1, 2), Is.True);
            Assert.That(MerkabaArtifactViewer.NeedsContinuationMarker(
                gibibyte + 1L, 2, 2), Is.False);
        }

        [Test]
        public void QuestArtifactAnnotationsSelectPointsLinesAndPlaneInterior()
        {
            var ray = new Ray(Vector3.zero, Vector3.forward);
            float pointDistance = MerkabaArtifactViewer.RayPointDistance(ray,
                new Vector3(0.02f, 0f, 2f), out float pointAlong);
            float lineDistance = MerkabaArtifactViewer.RaySegmentDistance(ray,
                new Vector3(-1f, 0f, 2f), new Vector3(1f, 0f, 2f),
                out float lineAlong);
            bool triangle = MerkabaArtifactViewer.RayTriangleDistance(ray,
                new Vector3(-1f, -1f, 2f), new Vector3(1f, -1f, 2f),
                new Vector3(0f, 1f, 2f), out float triangleAlong);

            Assert.That(pointDistance, Is.EqualTo(0.02f).Within(1e-6f));
            Assert.That(pointAlong, Is.EqualTo(2f).Within(1e-6f));
            Assert.That(lineDistance, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(lineAlong, Is.EqualTo(2f).Within(1e-6f));
            Assert.That(triangle, Is.True);
            Assert.That(triangleAlong, Is.EqualTo(2f).Within(1e-6f));
        }

        [Test]
        public void QuestArtifactPlaneDragBuildsOneFilledSurfaceRectangle()
        {
            Vector3[] points = MerkabaArtifactViewer.SurfaceRectangle(
                new Vector3(1f, 2f, 3f), new Vector3(3f, 5f, 7f),
                Vector3.right, Vector3.up);

            Assert.That(points, Has.Length.EqualTo(4));
            Assert.That(points[0], Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(points[1], Is.EqualTo(new Vector3(3f, 2f, 3f)));
            Assert.That(points[2], Is.EqualTo(new Vector3(3f, 5f, 3f)));
            Assert.That(points[3], Is.EqualTo(new Vector3(1f, 5f, 3f)));
        }

        [Test]
        public void QuestArtifactTwoHandFrameTracksMidpointScaleAndFullPose()
        {
            bool valid = MerkabaArtifactViewer.TryBuildTwoHandFrame(
                new Vector3(-0.2f, 1f, 2f), Quaternion.identity,
                new Vector3(0.2f, 1f, 2f), Quaternion.identity,
                out Vector3 midpoint, out Quaternion frame,
                out float separation);

            Assert.That(valid, Is.True);
            Assert.That(midpoint, Is.EqualTo(new Vector3(0f, 1f, 2f)));
            Assert.That(separation, Is.EqualTo(0.4f).Within(1e-6f));
            Assert.That(Vector3.Distance(
                (frame * Vector3.right).normalized, Vector3.right),
                Is.LessThan(1e-6f));
        }

        [Test]
        public void QuestArtifactPlaneCornerHandlePreservesOneRectangle()
        {
            Vector3[] original =
            {
                new(0f, 0f, 0f),
                new(2f, 0f, 0f),
                new(2f, 1f, 0f),
                new(0f, 1f, 0f)
            };
            Vector3[] resized = MerkabaArtifactViewer.ResizePlaneCorner(
                original, 2, new Vector3(3f, 2f, 0f), 0.01f);

            Assert.That(resized[0], Is.EqualTo(original[0]));
            Assert.That(resized[1], Is.EqualTo(new Vector3(3f, 0f, 0f)));
            Assert.That(resized[2], Is.EqualTo(new Vector3(3f, 2f, 0f)));
            Assert.That(resized[3], Is.EqualTo(new Vector3(0f, 2f, 0f)));
        }

        [Test]
        public void TiledLeavesComposeTheExactMonolithicTriangleUnion()
        {
            MerkabaFlowerPresentation flower = Fixture();
            string root = TemporaryDirectory();
            try
            {
                MerkabaTilesetResult result = MerkabaFlowerWriterFixture.StreamPackage(
                    root, flower, hardLeafBytes: 8000);
                Assert.That(result.TileCount, Is.GreaterThan(1));
                Assert.That(result.TriangleCount,
                    Is.EqualTo(flower.TriangleCount));

                List<string> monolithic;
                using (var stream = new MemoryStream())
                {
                    _ = MerkabaGlbWriter.Write(stream, flower, float3.zero);
                    monolithic = Triangles(stream.ToArray(), Vector3.zero,
                        rotateGlbToTileset: true);
                }
                List<string> tiled = TiledTriangles(root);
                Assert.That(tiled, Is.EqualTo(monolithic));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void PackageIsStandardBoundedAndByteDeterministic()
        {
            MerkabaFlowerPresentation flower = Fixture();
            string first = TemporaryDirectory();
            string second = TemporaryDirectory();
            try
            {
                MerkabaTilesetResult a = MerkabaFlowerWriterFixture.StreamPackage(
                    first, flower, hardLeafBytes: 8000);
                MerkabaTilesetResult b = MerkabaFlowerWriterFixture.StreamPackage(
                    second, flower, hardLeafBytes: 8000);
                Assert.That(b.TileCount, Is.EqualTo(a.TileCount));
                string json = File.ReadAllText(Path.Combine(first,
                    "tileset.json"));
                Assert.That(json, Does.Contain("\"version\":\"1.1\""));
                Assert.That(json, Does.Contain("\"geometricError\":1e30"));
                Assert.That(json, Does.Contain("\"geometricError\":0"));
                Assert.That(json, Does.Contain("\"boundingVolume\":{" +
                    "\"box\""));
                Assert.That(json, Does.Not.Contain("region"));
                Assert.That(json, Does.Not.Contain("scan.json"));

                string[] firstFiles = RelativeFiles(first);
                string[] secondFiles = RelativeFiles(second);
                Assert.That(secondFiles, Is.EqualTo(firstFiles));
                foreach (string relative in firstFiles)
                {
                    byte[] left = File.ReadAllBytes(Path.Combine(first,
                        relative));
                    byte[] right = File.ReadAllBytes(Path.Combine(second,
                        relative));
                    Assert.That(right, Is.EqualTo(left), relative);
                    if (relative.EndsWith(".glb", StringComparison.Ordinal))
                    {
                        Assert.That(left.Length, Is.LessThanOrEqualTo(8000));
                        Assert.That(BitConverter.ToUInt32(left, 0),
                            Is.EqualTo(0x46546C67u));
                        Assert.That(BitConverter.ToUInt32(left, 8),
                            Is.EqualTo((uint)left.Length));
                    }
                }
                Assert.That(firstFiles.Count(value =>
                    value.EndsWith(".glb", StringComparison.Ordinal)),
                    Is.EqualTo(a.TileCount));
            }
            finally
            {
                if (Directory.Exists(first)) Directory.Delete(first, true);
                if (Directory.Exists(second)) Directory.Delete(second, true);
            }
        }

        [Test]
        public void StreamingPackagePublishesManifestAfterBoundedLeaves()
        {
            MerkabaFlowerPresentation flower = Fixture();
            string root = TemporaryDirectory();
            try
            {
                MerkabaTilesetWriter.BeginStreamingPackage(root);
                MerkabaTilesetLeaf leaf =
                    MerkabaTilesetWriter.WriteStreamingLeaf(root, 0,
                        flower, hardLeafBytes: 1_000_000);
                Assert.That(File.Exists(Path.Combine(root, "tiles",
                    "000000.glb")), Is.True);
                Assert.That(File.Exists(Path.Combine(root,
                    "tileset.json")), Is.False);

                MerkabaTilesetResult result =
                    MerkabaTilesetWriter.CompleteStreamingPackage(root,
                        new[] { leaf });
                Assert.That(result.TileCount, Is.EqualTo(1));
                Assert.That(result.TriangleCount,
                    Is.EqualTo(flower.TriangleCount));
                string json = File.ReadAllText(Path.Combine(root,
                    "tileset.json"));
                Assert.That(json, Does.Contain(
                    "\"uri\":\"tiles/000000.glb\""));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void StreamingPackageRejectsEmptyPresentationWithoutPublishingManifest()
        {
            string root = TemporaryDirectory();
            try
            {
                MerkabaTilesetWriter.BeginStreamingPackage(root);
                Assert.Throws<InvalidDataException>(() =>
                    MerkabaTilesetWriter.CompleteStreamingPackage(root,
                        Array.Empty<MerkabaTilesetLeaf>()));
                var empty = new MerkabaFlowerPresentation(default);
                Assert.Throws<InvalidDataException>(() =>
                    MerkabaTilesetWriter.WriteStreamingLeaf(root, 0, empty));
                Assert.That(File.Exists(Path.Combine(root, "tileset.json")), Is.False);
                Assert.That(Directory.GetFiles(Path.Combine(root, "tiles")), Is.Empty);

                MerkabaFlowerPresentation flower = MerkabaFlowerWriterFixture.Create();
                MerkabaTilesetLeaf leaf = MerkabaTilesetWriter.WriteStreamingLeaf(root, 0, flower);
                MerkabaTilesetResult result = MerkabaTilesetWriter.CompleteStreamingPackage(root,
                    new[] { leaf });
                Assert.That(result.TriangleCount, Is.EqualTo(flower.TriangleCount));
                Assert.That(File.Exists(Path.Combine(root, "tileset.json")), Is.True);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void SourceKeepsOneGlbAuthorityAndDurableManifestLast()
        {
            string tileset = Source(
                "Runtime/Merkaba/MerkabaTilesetWriter.cs");
            string exporter = Source("Runtime/Merkaba/MerkabaExporter.cs");
            string storage = Source("Runtime/Merkaba/MerkabaGrid.Storage.cs");
            Assert.That(tileset, Does.Contain("MerkabaGlbWriter.Write"));
            Assert.That(tileset, Does.Not.Contain("LargeGlbWriter"));
            Assert.That(tileset, Does.Contain("float3 LocalOrigin"));
            int leafPublish = tileset.IndexOf("File.Move(temporaryPath, finalPath)", StringComparison.Ordinal);
            int manifestPublish = tileset.IndexOf("File.Move(temporary, manifest)", StringComparison.Ordinal);
            Assert.That(leafPublish, Is.GreaterThanOrEqualTo(0), "Bounded GLB leaf must be published.");
            Assert.That(manifestPublish, Is.GreaterThan(leafPublish), "Durable manifest publication must follow its leaves.");
            Assert.That(exporter, Does.Contain(
                "MerkabaFilePublishing.Publish(temporaryArchive,"));
            Assert.That(exporter, Does.Contain(
                "CompressionLevel.NoCompression"));
            Assert.That(exporter, Does.Not.Contain("PublishDirectory("));
            Assert.That(exporter, Does.Contain(
                "public Task<bool> ExportViewerPackageAsync()"));
            Assert.That(exporter, Does.Contain("Streamed "));
            Assert.That(exporter, Does.Contain(
                "BuildStreamingTilesetAsync(\n" +
                "                    staging, spatialBinding, progress, cancellationToken, nativePackage)"));
            Assert.That(exporter, Does.Contain(
                "await CaptureSpatialBindingAsync()"));
            Assert.That(exporter, Does.Contain(
                "await anchor.EnsureSessionAnchorAsync(requiredUuid, false)"));
            Assert.That(exporter, Does.Contain(
                "Active session has no persisted room anchor."));
            Assert.That(exporter, Does.Not.Contain(
                "EnsureSessionAnchorAsync(Guid.Empty, true)"));
            Assert.That(exporter, Does.Contain(
                "anchor.SpatialAnchorMatrix.inverse *\n" +
                "                _grid.GridToWorldMatrix"));
            Assert.That(exporter, Does.Contain(
                "MerkabaTilesetWriter.BeginStreamingPackage(staging, cancellationToken)"));
            Assert.That(exporter, Does.Contain(
                "MerkabaTilesetWriter.WriteStreamingLeaf(staging"));
            Assert.That(exporter, Does.Contain(
                "MerkabaTilesetWriter.CompleteStreamingPackage(staging"));
            Assert.That(exporter, Does.Contain("CaptureStoredFlowerSource(out _exportPosition)"));
            Assert.That(exporter, Does.Contain("ReadStoredFlowerContextAsync(addresses[index], addresses,\n                        _exportPosition, cancellationToken)"));
            Assert.That(exporter, Does.Contain("MerkabaFlowerPresentation.Build(reader, address, planeBounds,\n                        cancellationToken)"));
            foreach (string field in new[]
                     {
                         "occupiedOwners=", "carriers=", "triangles=", "unresolvedWedges="
                     })
                Assert.That(exporter, Does.Contain(field), field);
            Assert.That(exporter, Does.Contain("AppendDirtToTilesetAsync(staging, leaves, progress, cancellationToken)"));
            Assert.That(exporter, Does.Contain("StreamStoredFlowerDirtAsync(_exportPlaneBounds, _exportTiles,"));
            Assert.That(exporter, Does.Not.Contain(
                "CaptureStoredSnapshotAsync(anchorUuid"));
            int viewerExport = exporter.IndexOf(
                "public Task<bool> ExportViewerPackageAsync()",
                StringComparison.Ordinal);
            int nextMethod = exporter.IndexOf(
                "private async Task StreamOwnedFlowersAsync(", viewerExport,
                StringComparison.Ordinal);
            string scalablePath = exporter.Substring(viewerExport,
                nextMethod - viewerExport);
            Assert.That(exporter, Does.Not.Contain("BuildMembraneAsync("));
            Assert.That(exporter, Does.Contain(
                "new MerkabaGlbWriter.StreamingSession("));
            Assert.That(exporter, Does.Contain(
                "StreamOwnedFlowersAsync(async (flower"));
            Assert.That(exporter, Does.Contain(
                "StreamOwnedFlowersAsync(async (owned"));
            Assert.That(scalablePath, Does.Contain(
                "BuildStreamingTilesetAsync("));
            string reader = Source("Runtime/Merkaba/MerkabaGrid.Reader.cs");
            string store = Source("Runtime/Merkaba/MerkabaSsdStore.cs");
            Assert.That(reader, Does.Contain("FlowerContextAddresses("));
            Assert.That(reader, Does.Contain("z = -1; z <= 1"));
            Assert.That(reader, Does.Contain("Array.BinarySearch(capturedIndex"));
            Assert.That(store, Does.Contain("RequireFlowerPosition(position)"));
            Assert.That(store, Does.Contain("_subordinate.CaptureTile("));
            Assert.That(storage, Does.Contain("ReadStoredTilesAsync("));
        }

        [Test]
        public void OfflineViewerResourceIsSelfContainedAndUsesLocalRoot()
        {
            TextAsset viewer = Resources.Load<TextAsset>(
                "Merkaba/QuestMerkabaScanViewer");
            TextAsset threeLicense = Resources.Load<TextAsset>(
                "Merkaba/QuestMerkabaScanViewerThreeLicense");
            TextAsset tilesLicense = Resources.Load<TextAsset>(
                "Merkaba/QuestMerkabaScanViewerTilesLicense");
            try
            {
                Assert.That(viewer, Is.Not.Null);
                Assert.That(viewer.bytes.Length, Is.GreaterThan(500_000));
                Assert.That(viewer.text, Does.Contain("showDirectoryPicker"));
                Assert.That(viewer.text, Does.Contain(
                    "location.protocol===\"file:\""));
                Assert.That(viewer.text, Does.Contain(
                    "merkabaOpenLocalExport"));
                Assert.That(viewer.text, Does.Contain(
                    "merkaba://scan/tileset.json"));
                Assert.That(viewer.text, Does.Contain("Open this export"));
                Assert.That(viewer.text, Does.Contain(
                    "alphaHash=r||s.alphaHash"));
                Assert.That(viewer.text, Does.Contain(
                    "transparent=s.transparent"));
                Assert.That(viewer.text, Does.Contain(
                    "depthWrite=s.depthWrite"));
                Assert.That(viewer.text, Does.Contain(
                    "merkabaShootCrosshairPoint"));
                Assert.That(viewer.text, Does.Contain(
                    "merkabaPickLoadedSceneAtCenter"));
                Assert.That(viewer.text, Does.Contain(
                    "function merkabaPickLoadedScene(i,e)"));
                Assert.That(viewer.text, Does.Contain(
                    "merkabaWorldUp=new R(0,0,1)"));
                Assert.That(viewer.text, Does.Contain(
                    "merkabaWalkEyeHeight=1.7"));
                Assert.That(viewer.text, Does.Contain(
                    "merkabaWalkStepHeight=.32"));
                Assert.That(viewer.text, Does.Contain(
                    "function merkabaBuildWalkFloorLevels"));
                Assert.That(viewer.text, Does.Contain(
                    "function merkabaArchitectureDirection(i){let e=Math.abs(i.z)"));
                Assert.That(viewer.text, Does.Contain(
                    "merkabaArchitectureCellSize=.075"));
                Assert.That(viewer.text, Does.Contain(
                    "function merkabaCollectArchitectureSamples()"));
                Assert.That(viewer.text, Does.Contain(
                    "function merkabaEstimateArchitectureFrame(i)"));
                Assert.That(viewer.text, Does.Contain(
                    "function merkabaArchitecturePlaneHypotheses(i,e)"));
                Assert.That(viewer.text, Does.Contain(
                    "function merkabaArchitectureRasterizePlane(i,e)"));
                Assert.That(viewer.text, Does.Contain(
                    "function merkabaArchitectureBridgeSingleCellCracks(i)"));
                Assert.That(viewer.text, Does.Contain(
                    "function merkabaArchitectureConnectedComponents(i)"));
                Assert.That(viewer.text, Does.Contain(
                    "function merkabaArchitectureCloseSmallHoles(i)"));
                Assert.That(viewer.text, Does.Contain(
                    "function merkabaArchitectureBuildRegion(i,e,t,n)"));
                Assert.That(viewer.text, Does.Contain(
                    "function merkabaArchitecturalSupportJoints(i,e)"));
                Assert.That(viewer.text, Does.Contain(
                    "function merkabaArchitectureLevelBands(i)"));
                Assert.That(viewer.text, Does.Contain(
                    "function merkabaSelectStructuralEnvelope(i,e)"));
                Assert.That(viewer.text, Does.Contain(
                    "version:5,gridMeters:merkabaArchitectureCellSize"));
                Assert.That(viewer.text, Does.Contain(
                    "rects:merkabaArchitectureGridRectangles(t)"));
                Assert.That(viewer.text, Does.Contain(
                    "meshVertices:f,meshIndices:g"));
                Assert.That(viewer.text, Does.Contain(
                    "closedHoleCells"));
                Assert.That(viewer.text, Does.Not.Contain(
                    "merkabaCollectArchitectureMesh"));
                Assert.That(viewer.text, Does.Not.Contain(
                    "merkabaGrowArchitecturalRegions"));
                Assert.That(viewer.text, Does.Not.Contain(
                    "neighbours:new Set"));
                Assert.That(viewer.text, Does.Contain(
                    "function merkabaWalkFloorBelow"));
                Assert.That(viewer.text, Does.Contain(
                    "l.floorZ+l.jumpOffset"));
                Assert.That(viewer.text, Does.Contain(
                    "a.floorZ=f,a.jumpOffset=0,a.jumpVelocity=0"));
                Assert.That(viewer.text, Does.Not.Contain("<script src="));
                Assert.That(viewer.text, Does.Not.Contain("cdn.jsdelivr"));
                Assert.That(threeLicense, Is.Not.Null);
                Assert.That(threeLicense.text, Does.Contain("The MIT License"));
                Assert.That(tilesLicense, Is.Not.Null);
                Assert.That(tilesLicense.text, Does.Contain(
                    "Apache License"));
            }
            finally
            {
                if (viewer != null) Resources.UnloadAsset(viewer);
                if (threeLicense != null) Resources.UnloadAsset(threeLicense);
                if (tilesLicense != null) Resources.UnloadAsset(tilesLicense);
            }
        }

        [Test]
        public void OfflineArchiveIsDeterministicAndContainsCompletePackage()
        {
            MerkabaFlowerPresentation flower = Fixture();
            string first = TemporaryDirectory();
            string second = TemporaryDirectory();
            string firstArchive = first + ".zip";
            string secondArchive = second + ".zip";
            try
            {
                MerkabaTilesetResult package = MerkabaFlowerWriterFixture.StreamPackage(
                    first, flower, hardLeafBytes: 8000);
                _ = MerkabaFlowerWriterFixture.StreamPackage(second, flower,
                    hardLeafBytes: 8000);
                WriteViewerAssets(first);
                WriteViewerAssets(second);

                long firstBytes = MerkabaExporter.WriteViewerArchive(first,
                    firstArchive);
                long secondBytes = MerkabaExporter.WriteViewerArchive(second,
                    secondArchive);
                Assert.That(firstBytes, Is.EqualTo(new FileInfo(firstArchive).Length));
                Assert.That(secondBytes, Is.EqualTo(firstBytes));
                Assert.That(File.ReadAllBytes(secondArchive),
                    Is.EqualTo(File.ReadAllBytes(firstArchive)));

                using var stream = File.OpenRead(firstArchive);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                string[] names = archive.Entries.Select(entry => entry.FullName)
                    .OrderBy(name => name, StringComparer.Ordinal).ToArray();
                Assert.That(names, Does.Contain("index.html"));
                Assert.That(names, Does.Contain("tileset.json"));
                Assert.That(names, Does.Contain(
                    "THIRD_PARTY_THREE_LICENSE.txt"));
                Assert.That(names, Does.Contain(
                    "THIRD_PARTY_3DTILESRENDERERJS_LICENSE.txt"));
                Assert.That(names.Count(name => name.EndsWith(".glb",
                    StringComparison.Ordinal)), Is.EqualTo(package.TileCount));
                foreach (ZipArchiveEntry entry in archive.Entries)
                    Assert.That(entry.Length, Is.GreaterThan(0), entry.FullName);
            }
            finally
            {
                if (Directory.Exists(first)) Directory.Delete(first, true);
                if (Directory.Exists(second)) Directory.Delete(second, true);
                if (File.Exists(firstArchive)) File.Delete(firstArchive);
                if (File.Exists(secondArchive)) File.Delete(secondArchive);
            }
        }

        private static MerkabaFlowerPresentation Fixture()
        {
            var owners = new List<int3>();
            for (int y = -10; y < 10; y++)
            for (int z = -2; z < 2; z++)
                owners.Add(new int3(-3, y, z));
            return MerkabaFlowerWriterFixture.Create(owners.ToArray());
        }

        private static List<string> TiledTriangles(string root)
        {
            string json = File.ReadAllText(Path.Combine(root, "tileset.json"));
            var result = new List<string>();
            const string pattern =
                "\\\"transform\\\":\\[1,0,0,0,0,1,0,0,0,0,1,0," +
                "([^,]+),([^,]+),([^,]+),1\\],\\\"content\\\":" +
                "\\{\\\"uri\\\":\\\"([^\\\"]+)\\\"\\}";
            foreach (Match match in Regex.Matches(json, pattern))
            {
                var translation = new Vector3(Parse(match.Groups[1].Value),
                    Parse(match.Groups[2].Value),
                    Parse(match.Groups[3].Value));
                string path = Path.Combine(root,
                    match.Groups[4].Value.Replace('/', Path.DirectorySeparatorChar));
                result.AddRange(Triangles(File.ReadAllBytes(path), translation,
                    rotateGlbToTileset: true));
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static List<string> Triangles(byte[] glb, Vector3 translation,
            bool rotateGlbToTileset = false)
        {
            int jsonLength = checked((int)BitConverter.ToUInt32(glb, 12));
            string json = Encoding.UTF8.GetString(glb, 20, jsonLength)
                .TrimEnd(' ');
            Match count = Regex.Match(json,
                "\\\"count\\\":(\\d+),\\\"type\\\":\\\"VEC3\\\"");
            int vertexCount = int.Parse(count.Groups[1].Value,
                CultureInfo.InvariantCulture);
            int binaryStart = 20 + jsonLength + 8;
            var vertices = new Vector3[vertexCount];
            for (int index = 0; index < vertexCount; index++)
            {
                int offset = binaryStart + index * 12;
                Vector3 vertex = new Vector3(
                    BitConverter.ToSingle(glb, offset),
                    BitConverter.ToSingle(glb, offset + 4),
                    BitConverter.ToSingle(glb, offset + 8));
                if (rotateGlbToTileset)
                    vertex = new Vector3(vertex.x, -vertex.z, vertex.y);
                vertices[index] = vertex + translation;
            }
            int indexOffset = binaryStart + vertexCount * 28;
            int indexCount = 0;
            foreach (Match scalar in Regex.Matches(json,
                         @"""componentType"":5125,""count"":(\d+),""type"":""SCALAR"""))
                indexCount = checked(indexCount + int.Parse(scalar.Groups[1].Value,
                    CultureInfo.InvariantCulture));
            Assert.That(indexCount, Is.GreaterThan(0));
            Assert.That(indexCount % 3, Is.Zero);
            var triangles = new List<string>(indexCount / 3);
            for (int index = 0; index < indexCount; index += 3)
            {
                Vector3 a = vertices[BitConverter.ToUInt32(glb,
                    indexOffset + index * 4)];
                Vector3 b = vertices[BitConverter.ToUInt32(glb,
                    indexOffset + (index + 1) * 4)];
                Vector3 c = vertices[BitConverter.ToUInt32(glb,
                    indexOffset + (index + 2) * 4)];
                triangles.Add(Key(a) + "|" + Key(b) + "|" + Key(c));
            }
            triangles.Sort(StringComparer.Ordinal);
            return triangles;
        }

        private static string Key(Vector3 value) =>
            $"{Mathf.RoundToInt(value.x * 1_000_000f)}," +
            $"{Mathf.RoundToInt(value.y * 1_000_000f)}," +
            $"{Mathf.RoundToInt(value.z * 1_000_000f)}";

        private static float Parse(string value) => float.Parse(value,
            CultureInfo.InvariantCulture);

        private static string[] RelativeFiles(string root) => Directory
            .GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path)
                .Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();

        private static void WriteViewerAssets(string root)
        {
            File.Copy(SourcePath("Runtime/Resources/Merkaba/" +
                "QuestMerkabaScanViewer.txt"), Path.Combine(root,
                "index.html"));
            File.Copy(SourcePath("Runtime/Resources/Merkaba/" +
                "QuestMerkabaScanViewerThreeLicense.txt"), Path.Combine(root,
                "THIRD_PARTY_THREE_LICENSE.txt"));
            File.Copy(SourcePath("Runtime/Resources/Merkaba/" +
                "QuestMerkabaScanViewerTilesLicense.txt"), Path.Combine(root,
                "THIRD_PARTY_3DTILESRENDERERJS_LICENSE.txt"));
        }

        private static string TemporaryDirectory() => Path.Combine(
            Path.GetTempPath(), "merkaba-tiles-" + Guid.NewGuid().ToString("N"));

        private static string Source(string relative) =>
            File.ReadAllText(SourcePath(relative));

        [Test]
        public void ClearExportDiscardsOnlyItsOwnResumeReceipt()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "merkaba-clear-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string owned = Path.Combine(root, "scan.glb");
                string receipt = owned + ".resume";
                Directory.CreateDirectory(Path.Combine(receipt, "content"));
                File.WriteAllText(Path.Combine(receipt, "source.json"), "{}");
                File.WriteAllText(Path.Combine(receipt, "content", "positions.bin"), "x");
                Assert.That(MerkabaExportJournal.Exists(receipt), Is.True);

                // A directory that carries no journal record is not a receipt.
                // Clearing an export must never remove it, however it is named.
                string foreign = Path.Combine(root, "other.glb");
                string foreignDirectory = foreign + ".resume";
                Directory.CreateDirectory(foreignDirectory);
                File.WriteAllText(Path.Combine(foreignDirectory, "notes.txt"), "keep");

                MerkabaExporter.DiscardResumeReceipt(owned);
                MerkabaExporter.DiscardResumeReceipt(foreign);
                Assert.That(Directory.Exists(receipt), Is.False,
                    "The selected owned resume receipt must be discarded.");
                Assert.That(File.Exists(Path.Combine(foreignDirectory, "notes.txt")),
                    Is.True, "A directory without a journal record is not a receipt.");

                // A missing destination and a destination with no receipt at
                // all are both ordinary, not failures.
                MerkabaExporter.DiscardResumeReceipt(Path.Combine(root, "absent.glb"));
                MerkabaExporter.DiscardResumeReceipt(null);
                MerkabaExporter.DiscardResumeReceipt(string.Empty);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static string SourcePath(string relative) =>
            Path.GetFullPath("Packages/com.genesis.roomscan/" + relative);
    }
}
