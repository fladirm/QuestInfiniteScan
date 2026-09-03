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
            MerkabaExportMembraneResult membrane = Fixture();
            Vector3 origin = new(0.25f, -0.5f, 0.75f);
            Vector3 center = new(0.1f, 0.2f, 0.3f);
            using var stream = new MemoryStream();
            _ = MerkabaGlbWriter.Write(stream, membrane, origin);
            byte[] glb = stream.ToArray();

            MerkabaArtifactViewer.ParsedGlb parsed =
                MerkabaArtifactViewer.ParseGlbForPreview(glb,
                    origin, center);
            using var streamed = new MemoryStream(glb, false);
            MerkabaArtifactViewer.ParsedGlb streamedParsed =
                MerkabaArtifactViewer.ParseGlbForPreview(streamed,
                    streamed.Length, origin, center);

            Assert.That(parsed.Positions.Length, Is.GreaterThan(0));
            Assert.That(parsed.Normals.Length, Is.EqualTo(parsed.Positions.Length));
            Assert.That(parsed.Colors.Length, Is.EqualTo(parsed.Positions.Length));
            Assert.That(parsed.Indices.Length % 3, Is.Zero);
            Assert.That(parsed.Positions.Any(value =>
                Vector3.Distance(value,
                    (Vector3)membrane.Patches[0].Corner00 - center) < 1e-6f),
                Is.True);
            Color32 expected = KernelState.UnpackColor(
                membrane.Patches[0].PackedColor);
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
        public void QuestArtifactPreviewStreamsUnboundedPackageIntoBoundedCache()
        {
            string viewer = Source(
                "Runtime/UI/MerkabaArtifactViewer.cs");
            string setup = Source("Editor/RoomScanSetupWizard.cs");
            string menu = Source("Runtime/UI/DebugMenu.uxml");

            Assert.That(viewer, Does.Contain(
                "MaximumResidentDecodedBytes = 512L * 1024L * 1024L"));
            Assert.That(viewer, Does.Contain("ReadPackageIndex(archivePath)"));
            Assert.That(viewer, Does.Contain("ReadGlbTile("));
            Assert.That(viewer, Does.Contain("new BufferedStream(entryStream"));
            Assert.That(viewer, Does.Not.Contain(
                "new byte[(int)entry.Length]"));
            Assert.That(viewer, Does.Not.Contain(
                "Preview tile is too large"));
            Assert.That(viewer, Does.Contain("TouchScreenKeyboard.Open("));
            Assert.That(viewer, Does.Contain("AnnotationMode.Select"));
            Assert.That(viewer, Does.Contain("DestroyTile(tile)"));
            Assert.That(viewer, Does.Contain("_scanner.ReadoutDrawEnabled = false"));
            Assert.That(viewer, Does.Not.Contain("MerkabaGrid"));
            Assert.That(viewer, Does.Not.Contain("KernelState"));
            Assert.That(setup, Does.Contain(
                "GetOrAdd<MerkabaArtifactViewer>(scannerObject)"));
            Assert.That(setup, Does.Contain("requiresSystemKeyboard = true"));
            Assert.That(menu, Does.Contain("btn-artifact-view"));
            Assert.That(menu, Does.Contain("btn-annotation-mode"));
            Assert.That(menu, Does.Contain("btn-annotation-edit"));
            Assert.That(menu, Does.Contain("annotation-note"));
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
        public void TiledLeavesComposeTheExactMonolithicTriangleUnion()
        {
            MerkabaExportMembraneResult membrane = Fixture();
            string root = TemporaryDirectory();
            try
            {
                MerkabaTilesetResult result = MerkabaTilesetWriter.WritePackage(
                    root, membrane, targetLeafBytes: 3000,
                    hardLeafBytes: 8000);
                Assert.That(result.TileCount, Is.GreaterThan(1));
                Assert.That(result.TriangleCount,
                    Is.EqualTo(membrane.Patches.Count * 2));

                List<string> monolithic;
                using (var stream = new MemoryStream())
                {
                    _ = MerkabaGlbWriter.Write(stream, membrane);
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
            MerkabaExportMembraneResult membrane = Fixture();
            string first = TemporaryDirectory();
            string second = TemporaryDirectory();
            try
            {
                MerkabaTilesetResult a = MerkabaTilesetWriter.WritePackage(
                    first, membrane, targetLeafBytes: 3000,
                    hardLeafBytes: 8000);
                MerkabaTilesetResult b = MerkabaTilesetWriter.WritePackage(
                    second, membrane, targetLeafBytes: 3000,
                    hardLeafBytes: 8000);
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
            MerkabaExportMembraneResult membrane = Fixture();
            string root = TemporaryDirectory();
            try
            {
                MerkabaTilesetWriter.BeginStreamingPackage(root);
                MerkabaTilesetLeaf leaf =
                    MerkabaTilesetWriter.WriteStreamingLeaf(root, 0,
                        membrane, hardLeafBytes: 1_000_000);
                Assert.That(File.Exists(Path.Combine(root, "tiles",
                    "000000.glb")), Is.True);
                Assert.That(File.Exists(Path.Combine(root,
                    "tileset.json")), Is.False);

                MerkabaTilesetResult result =
                    MerkabaTilesetWriter.CompleteStreamingPackage(root,
                        new[] { leaf });
                Assert.That(result.TileCount, Is.EqualTo(1));
                Assert.That(result.TriangleCount,
                    Is.EqualTo(membrane.Patches.Count * 2));
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
        public void SourceKeepsOneGlbAuthorityAndDurableManifestLast()
        {
            string tileset = Source(
                "Runtime/Merkaba/MerkabaTilesetWriter.cs");
            string exporter = Source("Runtime/Merkaba/MerkabaExporter.cs");
            string storage = Source("Runtime/Merkaba/MerkabaGrid.Storage.cs");
            Assert.That(tileset, Does.Contain("MerkabaGlbWriter.Write"));
            Assert.That(tileset, Does.Not.Contain("LargeGlbWriter"));
            Assert.That(tileset, Does.Contain("float3 LocalOrigin"));
            Assert.That(tileset.IndexOf("File.Move(temporaryPath, finalPath)",
                    StringComparison.Ordinal), Is.LessThan(tileset.IndexOf(
                    "File.Move(manifestTemporary, manifest)",
                    StringComparison.Ordinal)));
            Assert.That(exporter, Does.Contain(
                "MerkabaFilePublishing.Publish(temporaryArchive,"));
            Assert.That(exporter, Does.Contain(
                "CompressionLevel.NoCompression"));
            Assert.That(exporter, Does.Not.Contain("PublishDirectory("));
            Assert.That(exporter, Does.Contain(
                "public async Task<bool> ExportViewerPackageAsync()"));
            Assert.That(exporter, Does.Contain("Streamed "));
            Assert.That(exporter, Does.Contain(
                "BuildStreamingTilesetAsync(\n" +
                "                    staging, progress)"));
            Assert.That(exporter, Does.Contain(
                "MerkabaTilesetWriter.BeginStreamingPackage(staging)"));
            Assert.That(exporter, Does.Contain(
                "MerkabaTilesetWriter.WriteStreamingLeaf(staging"));
            Assert.That(exporter, Does.Contain(
                "MerkabaTilesetWriter.CompleteStreamingPackage(staging"));
            Assert.That(exporter, Does.Contain(
                "offset += MerkabaGrid.StreamBatchCapacity"));
            Assert.That(exporter, Does.Not.Contain(
                "CaptureStoredSnapshotAsync(anchorUuid"));
            int viewerExport = exporter.IndexOf(
                "public async Task<bool> ExportViewerPackageAsync()",
                StringComparison.Ordinal);
            int nextMethod = exporter.IndexOf(
                "private async Task StreamOwnedMembranesAsync(", viewerExport,
                StringComparison.Ordinal);
            string scalablePath = exporter.Substring(viewerExport,
                nextMethod - viewerExport);
            Assert.That(exporter, Does.Not.Contain("BuildMembraneAsync("));
            Assert.That(exporter, Does.Contain(
                "new MerkabaGlbWriter.StreamingSession("));
            Assert.That(exporter, Does.Contain(
                "StreamOwnedMembranesAsync(async (membrane"));
            Assert.That(exporter, Does.Contain(
                "StreamOwnedMembranesAsync(async (owned"));
            Assert.That(scalablePath, Does.Contain(
                "BuildStreamingTilesetAsync("));
            Assert.That(storage, Does.Contain("CaptureStoredTileIndex()"));
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
            MerkabaExportMembraneResult membrane = Fixture();
            string first = TemporaryDirectory();
            string second = TemporaryDirectory();
            string firstArchive = first + ".zip";
            string secondArchive = second + ".zip";
            try
            {
                MerkabaTilesetResult package = MerkabaTilesetWriter.WritePackage(
                    first, membrane, targetLeafBytes: 3000,
                    hardLeafBytes: 8000);
                _ = MerkabaTilesetWriter.WritePackage(second, membrane,
                    targetLeafBytes: 3000, hardLeafBytes: 8000);
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

        private static MerkabaExportMembraneResult Fixture()
        {
            var evidence = new Dictionary<int3, KernelState>();
            for (int y = -10; y < 10; y++)
            for (int z = -2; z < 2; z++)
            {
                int3 coord = new(-3, y, z);
                KernelState state = default;
                state.SetOccupiedForFixture(true,
                    new Color32((byte)(y + 32), (byte)(z + 32), 180, 255));
                state.Flags = KernelState.SetSurfacePlane(state.Flags,
                    new float3(1f, 0.15f, 0.05f), 0.004f);
                evidence.Add(coord, state);
            }
            return MerkabaExportMembrane.Build(
                MerkabaExportShell.Build(evidence));
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
            int indexCount = checked((glb.Length - indexOffset) / 4);
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

        private static string SourcePath(string relative) =>
            Path.GetFullPath("Packages/com.genesis.roomscan/" + relative);
    }
}
