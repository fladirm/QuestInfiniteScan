using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaTilesetWriterTests
    {
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
                    monolithic = Triangles(stream.ToArray(), Vector3.zero);
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
            Assert.That(exporter, Does.Contain("PublishDirectory(staging, " +
                "destination)"));
            Assert.That(exporter, Does.Contain(
                "public async Task<bool> ExportViewerPackageAsync()"));
            Assert.That(exporter, Does.Contain("Streamed "));
            Assert.That(exporter, Does.Not.Contain(
                "CaptureStoredSnapshotAsync(anchorUuid"));
            Assert.That(storage, Does.Contain("CaptureStoredTileIndex()"));
            Assert.That(storage, Does.Contain("ReadStoredTilesAsync("));
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
                result.AddRange(Triangles(File.ReadAllBytes(path), translation));
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static List<string> Triangles(byte[] glb, Vector3 translation)
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
                vertices[index] = new Vector3(
                    BitConverter.ToSingle(glb, offset),
                    BitConverter.ToSingle(glb, offset + 4),
                    BitConverter.ToSingle(glb, offset + 8)) + translation;
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

        private static string TemporaryDirectory() => Path.Combine(
            Path.GetTempPath(), "merkaba-tiles-" + Guid.NewGuid().ToString("N"));

        private static string Source(string relative) => File.ReadAllText(
            Path.GetFullPath("Packages/com.genesis.roomscan/" + relative));
    }
}
