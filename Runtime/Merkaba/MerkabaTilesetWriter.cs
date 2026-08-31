using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan
{
    internal readonly struct MerkabaTilesetResult
    {
        internal readonly int TileCount;
        internal readonly long ByteLength;
        internal readonly long VertexCount;
        internal readonly long TriangleCount;

        internal MerkabaTilesetResult(int tileCount, long byteLength,
            long vertexCount, long triangleCount)
        {
            TileCount = tileCount;
            ByteLength = byteLength;
            VertexCount = vertexCount;
            TriangleCount = triangleCount;
        }
    }

    /// <summary>
    /// Dependency-free 3D Tiles 1.1 packaging over the finished membrane/GLB
    /// authority. Spatial ownership partitions emission only; every leaf retains
    /// the complete canonical occupancy context used by the monolithic writer.
    /// </summary>
    internal static class MerkabaTilesetWriter
    {
        internal const long DefaultTargetLeafBytes = 128L * 1024 * 1024;
        internal const long DefaultHardLeafBytes = 256L * 1024 * 1024;
        private const long GlbHeaderReserve = 2L * 1024;
        private const long MeasuredPatchBytes = 4L * 28L + 6L * 4L;
        private const long LegacyPrimitiveBytes = 3L * 28L + 3L * 4L;

        private readonly struct Item
        {
            internal readonly int3 Coord;
            internal readonly int Index;
            internal readonly bool IsPatch;
            internal readonly long EstimatedBytes;

            internal Item(int3 coord, int index, bool isPatch,
                long estimatedBytes)
            {
                Coord = coord;
                Index = index;
                IsPatch = isPatch;
                EstimatedBytes = estimatedBytes;
            }
        }

        private sealed class Node
        {
            internal int3 MinimumCoord;
            internal int3 MaximumCoord;
            internal List<Item> Items;
            internal Node Low;
            internal Node High;
            internal int LeafIndex = -1;
            internal float3 LocalOrigin;
            internal Vector3 Minimum;
            internal Vector3 Maximum;
            internal Vector3 ContentMinimum;
            internal Vector3 ContentMaximum;
            internal long ContentBytes;
            internal int VertexCount;
            internal int TriangleCount;
            internal bool IsLeaf => Items != null;
        }

        internal static MerkabaTilesetResult WritePackage(string directory,
            MerkabaExportMembraneResult membrane,
            IProgress<OperationWorkProgress> progress = null,
            long targetLeafBytes = DefaultTargetLeafBytes,
            long hardLeafBytes = DefaultHardLeafBytes)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Tileset directory is required.",
                    nameof(directory));
            if (membrane == null) throw new ArgumentNullException(nameof(membrane));
            if (targetLeafBytes <= GlbHeaderReserve ||
                hardLeafBytes < targetLeafBytes)
                throw new ArgumentOutOfRangeException(nameof(targetLeafBytes));
            if (Directory.Exists(directory))
                throw new IOException("Tileset staging directory already exists.");

            List<Item> items = BuildItems(membrane);
            if (items.Count == 0)
                throw new InvalidDataException("3D Tiles membrane is empty.");
            long leafBudget = targetLeafBytes - GlbHeaderReserve;
            Node root = Partition(items, leafBudget);
            var leaves = new List<Node>();
            AssignLeaves(root, leaves);

            Directory.CreateDirectory(Path.Combine(directory, "tiles"));
            long totalBytes = 0L;
            long totalVertices = 0L;
            long totalTriangles = 0L;
            for (int leafIndex = 0; leafIndex < leaves.Count; leafIndex++)
            {
                Node leaf = leaves[leafIndex];
                leaf.LocalOrigin = LocalOrigin(leaf.MinimumCoord,
                    leaf.MaximumCoord);
                MerkabaExportMembraneResult owned = OwnedMembrane(membrane,
                    leaf.Items);
                string name = leafIndex.ToString("D6",
                    CultureInfo.InvariantCulture) + ".glb";
                string finalPath = Path.Combine(directory, "tiles", name);
                string temporaryPath = finalPath + ".tmp";
                MerkabaGlbResult result;
                using (var stream = new FileStream(temporaryPath,
                           FileMode.CreateNew, FileAccess.Write, FileShare.None,
                           1024 * 1024, FileOptions.WriteThrough))
                {
                    result = MerkabaGlbWriter.Write(stream, owned,
                        leaf.LocalOrigin, progress);
                    stream.Flush(true);
                }
                if (result.ByteLength > hardLeafBytes)
                    throw new InvalidDataException($"3D Tiles leaf {name} is " +
                        $"{result.ByteLength} bytes, above {hardLeafBytes}.");
                File.Move(temporaryPath, finalPath);
                leaf.ContentBytes = result.ByteLength;
                leaf.VertexCount = result.VertexCount;
                leaf.TriangleCount = result.PrimitiveCount;
                leaf.ContentMinimum = result.Minimum;
                leaf.ContentMaximum = result.Maximum;
                Vector3 translation = Convert(leaf.LocalOrigin);
                leaf.Minimum = result.Minimum + translation;
                leaf.Maximum = result.Maximum + translation;
                totalBytes = checked(totalBytes + result.ByteLength);
                totalVertices = checked(totalVertices + result.VertexCount);
                totalTriangles = checked(totalTriangles + result.PrimitiveCount);
                progress?.Report(new OperationWorkProgress(
                    ScanOperationStage.WritingFile, leafIndex + 1, leaves.Count,
                    $"Wrote 3D Tiles leaf {leafIndex + 1}/{leaves.Count}"));
            }

            ResolveBounds(root);
            string json = BuildTileset(root);
            string manifest = Path.Combine(directory, "tileset.json");
            string manifestTemporary = manifest + ".tmp";
            using (var stream = new FileStream(manifestTemporary,
                       FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       64 * 1024, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream,
                       new UTF8Encoding(false), 64 * 1024, true))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }
            File.Move(manifestTemporary, manifest);
            totalBytes = checked(totalBytes + new FileInfo(manifest).Length);
            return new MerkabaTilesetResult(leaves.Count, totalBytes,
                totalVertices, totalTriangles);
        }

        private static List<Item> BuildItems(
            MerkabaExportMembraneResult membrane)
        {
            var occupied = new HashSet<int3>(
                membrane.CanonicalOccupiedCoordinates);
            var items = new List<Item>(membrane.Patches.Count +
                membrane.LegacyKernels.Count);
            for (int index = 0; index < membrane.Patches.Count; index++)
                items.Add(new Item(membrane.Patches[index].Coord, index, true,
                    MeasuredPatchBytes));
            for (int index = 0; index < membrane.LegacyKernels.Count; index++)
            {
                MerkabaKernelSnapshot kernel = membrane.LegacyKernels[index];
                int primitiveCount = 0;
                foreach (int _ in MerkabaCanonicalGeometry.VisiblePrimitives(
                             kernel.Coord, occupied.Contains))
                    primitiveCount++;
                if (primitiveCount != 0)
                    items.Add(new Item(kernel.Coord, index, false,
                        checked(primitiveCount * LegacyPrimitiveBytes)));
            }
            items.Sort(CompareItems);
            return items;
        }

        private static Node Partition(List<Item> items, long leafBudget)
        {
            Bounds(items, out int3 minimum, out int3 maximum,
                out long estimatedBytes);
            var node = new Node
            {
                MinimumCoord = minimum,
                MaximumCoord = maximum
            };
            if (estimatedBytes <= leafBudget)
            {
                node.Items = items;
                return node;
            }

            int3 span = maximum - minimum;
            int axis = span.x >= span.y && span.x >= span.z ? 0 :
                span.y >= span.z ? 1 : 2;
            if (span[axis] == 0)
                throw new InvalidDataException(
                    "One canonical owner exceeds the 3D Tiles leaf budget.");
            int cut = minimum[axis] + span[axis] / 2;
            var low = new List<Item>();
            var high = new List<Item>();
            foreach (Item item in items)
            {
                if (item.Coord[axis] <= cut) low.Add(item);
                else high.Add(item);
            }
            if (low.Count == 0 || high.Count == 0)
                throw new InvalidDataException(
                    "Deterministic M8 spatial partition did not advance.");
            node.Low = Partition(low, leafBudget);
            node.High = Partition(high, leafBudget);
            return node;
        }

        private static void Bounds(List<Item> items, out int3 minimum,
            out int3 maximum, out long bytes)
        {
            minimum = new int3(int.MaxValue);
            maximum = new int3(int.MinValue);
            bytes = 0L;
            foreach (Item item in items)
            {
                minimum = math.min(minimum, item.Coord);
                maximum = math.max(maximum, item.Coord);
                bytes = checked(bytes + item.EstimatedBytes);
            }
        }

        private static void AssignLeaves(Node node, List<Node> leaves)
        {
            if (node.IsLeaf)
            {
                node.LeafIndex = leaves.Count;
                leaves.Add(node);
                return;
            }
            AssignLeaves(node.Low, leaves);
            AssignLeaves(node.High, leaves);
        }

        private static MerkabaExportMembraneResult OwnedMembrane(
            MerkabaExportMembraneResult source, List<Item> items)
        {
            var patches = new List<MerkabaExportMembranePatch>();
            var legacy = new List<MerkabaKernelSnapshot>();
            foreach (Item item in items)
            {
                if (item.IsPatch) patches.Add(source.Patches[item.Index]);
                else legacy.Add(source.LegacyKernels[item.Index]);
            }
            patches.Sort((left, right) => CompareCoords(left.Coord, right.Coord));
            legacy.Sort((left, right) => CompareCoords(left.Coord, right.Coord));
            int measured = 0, inferred = 0;
            foreach (MerkabaExportMembranePatch patch in patches)
            {
                if (patch.IsInferred) inferred++;
                else measured++;
            }
            return new MerkabaExportMembraneResult(patches, legacy,
                source.CanonicalOccupiedCoordinates,
                source.CanonicalOccupiedCount,
                source.MeasuredPlaneOccupiedCount, measured, inferred,
                legacy.Count, 0, 0, source.PartitionCutCount);
        }

        private static float3 LocalOrigin(int3 minimum, int3 maximum)
        {
            int3 center = minimum + (maximum - minimum) / 2;
            return (float3)center * MerkabaConstants.LatticeStep;
        }

        private static void ResolveBounds(Node node)
        {
            if (node.IsLeaf) return;
            ResolveBounds(node.Low);
            ResolveBounds(node.High);
            node.Minimum = Vector3.Min(node.Low.Minimum, node.High.Minimum);
            node.Maximum = Vector3.Max(node.Low.Maximum, node.High.Maximum);
        }

        private static string BuildTileset(Node root)
        {
            var json = new StringBuilder(4096);
            json.Append("{\"asset\":{\"version\":\"1.1\",\"generator\":")
                .Append("\"Quest Infinite Merkaba\"},\"geometricError\":0,")
                .Append("\"root\":");
            AppendNode(json, root);
            json.Append('}');
            return json.ToString();
        }

        private static void AppendNode(StringBuilder json, Node node)
        {
            json.Append("{\"boundingVolume\":{\"box\":[");
            AppendBox(json, node.IsLeaf ? node.ContentMinimum : node.Minimum,
                node.IsLeaf ? node.ContentMaximum : node.Maximum);
            json.Append("]},\"geometricError\":0");
            if (node.IsLeaf)
            {
                Vector3 translation = Convert(node.LocalOrigin);
                json.Append(",\"transform\":[1,0,0,0,0,1,0,0,0,0,1,0,")
                    .Append(Number(translation.x)).Append(',')
                    .Append(Number(translation.y)).Append(',')
                    .Append(Number(translation.z)).Append(",1]")
                    .Append(",\"content\":{\"uri\":\"tiles/")
                    .Append(node.LeafIndex.ToString("D6",
                        CultureInfo.InvariantCulture)).Append(".glb\"}");
            }
            else
            {
                json.Append(",\"children\":[");
                AppendNode(json, node.Low);
                json.Append(',');
                AppendNode(json, node.High);
                json.Append(']');
            }
            json.Append('}');
        }

        private static void AppendBox(StringBuilder json, Vector3 minimum,
            Vector3 maximum)
        {
            Vector3 center = (minimum + maximum) * 0.5f;
            Vector3 half = (maximum - minimum) * 0.5f;
            json.Append(Number(center.x)).Append(',').Append(Number(center.y))
                .Append(',').Append(Number(center.z)).Append(',')
                .Append(Number(half.x)).Append(",0,0,0,")
                .Append(Number(half.y)).Append(",0,0,0,")
                .Append(Number(half.z));
        }

        private static Vector3 Convert(float3 unity) =>
            new(-unity.x, unity.y, unity.z);

        private static string Number(float value) =>
            value.ToString("R", CultureInfo.InvariantCulture);

        private static int CompareItems(Item left, Item right)
        {
            int coordinate = CompareCoords(left.Coord, right.Coord);
            if (coordinate != 0) return coordinate;
            if (left.IsPatch != right.IsPatch) return left.IsPatch ? -1 : 1;
            return left.Index.CompareTo(right.Index);
        }

        private static int CompareCoords(int3 left, int3 right)
        {
            if (left.x != right.x) return left.x.CompareTo(right.x);
            if (left.y != right.y) return left.y.CompareTo(right.y);
            return left.z.CompareTo(right.z);
        }
    }
}
