using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Durable registration of package-space metres in one persisted spatial
    /// anchor. It is export metadata only and never participates in canonical
    /// M8 state.
    /// </summary>
    internal readonly struct MerkabaSpatialBinding
    {
        internal const int CurrentVersion = 1;

        internal readonly Guid AnchorUuid;
        internal readonly Matrix4x4 AnchorFromPackage;

        internal MerkabaSpatialBinding(Guid anchorUuid,
            Matrix4x4 anchorFromPackage)
        {
            AnchorUuid = anchorUuid;
            AnchorFromPackage = anchorFromPackage;
        }

        internal bool IsValid => AnchorUuid != Guid.Empty &&
            IsFinite(AnchorFromPackage);

        private static bool IsFinite(Matrix4x4 matrix)
        {
            for (int column = 0; column < 4; column++)
            for (int row = 0; row < 4; row++)
                if (float.IsNaN(matrix[row, column]) ||
                    float.IsInfinity(matrix[row, column]))
                    return false;
            return true;
        }
    }

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

    internal readonly struct MerkabaTilesetLeaf
    {
        internal readonly int Index;
        internal readonly int3 MinimumCoord;
        internal readonly int3 MaximumCoord;
        internal readonly float3 LocalOrigin;
        internal readonly Vector3 ContentMinimum;
        internal readonly Vector3 ContentMaximum;
        internal readonly long ByteLength;
        internal readonly int VertexCount;
        internal readonly int TriangleCount;

        internal MerkabaTilesetLeaf(int index, int3 minimumCoord,
            int3 maximumCoord, float3 localOrigin, Vector3 contentMinimum,
            Vector3 contentMaximum, long byteLength, int vertexCount,
            int triangleCount)
        {
            Index = index;
            MinimumCoord = minimumCoord;
            MaximumCoord = maximumCoord;
            LocalOrigin = localOrigin;
            ContentMinimum = contentMinimum;
            ContentMaximum = contentMaximum;
            ByteLength = byteLength;
            VertexCount = vertexCount;
            TriangleCount = triangleCount;
        }
    }

    /// <summary>
    /// Dependency-free 3D Tiles 1.1 packaging over the shared Sphere-Flower/excavation
    /// evaluator. Spatial ownership partitions emission only; every leaf retains
    /// the complete canonical occupancy context used by the monolithic writer.
    /// </summary>
    internal static class MerkabaTilesetWriter
    {
        internal const long DefaultHardLeafBytes = 256L * 1024 * 1024;
        private const string EmptyNodeGeometricError = "1e30";

        private sealed class Node
        {
            internal int3 MinimumCoord;
            internal int3 MaximumCoord;
            internal bool Leaf;
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
            internal bool IsLeaf => Leaf;
        }

        internal static void BeginStreamingPackage(string directory)
        {
            if (Directory.Exists(directory))
                throw new IOException("Tileset staging directory already exists.");
            Directory.CreateDirectory(Path.Combine(directory, "tiles"));
        }

        internal static MerkabaTilesetLeaf WriteStreamingLeaf(
            string directory, int leafIndex, MerkabaFlowerPresentation presentation,
            IProgress<OperationWorkProgress> progress = null,
            long hardLeafBytes = DefaultHardLeafBytes)
        {
            if (presentation == null) throw new ArgumentNullException(nameof(presentation));
            if (presentation.TriangleCount == 0)
                throw new InvalidDataException("3D Tiles Flower leaf is empty.");
            int3 minimum = MerkabaSpatial.Decode(presentation.Tile.BlockCoord,
                presentation.Tile.LocalAddress, 0);
            int3 maximum = minimum + 7;
            int3 centre = minimum + 4;
            float3 origin = new(
                MerkabaSphereFlowerAuthority.DirtGridCoordinate(centre.x),
                MerkabaSphereFlowerAuthority.DirtGridCoordinate(centre.y),
                MerkabaSphereFlowerAuthority.DirtGridCoordinate(centre.z));
            return WriteStreamingLeaf(directory, leafIndex, minimum, maximum, origin,
                stream => MerkabaGlbWriter.Write(stream, presentation, origin, progress), hardLeafBytes);
        }

        internal static MerkabaTilesetLeaf WriteStreamingDirtLeaf(
            string directory, int leafIndex,
            IReadOnlyList<MerkabaDirtTriangle> triangles,
            IProgress<OperationWorkProgress> progress = null,
            long hardLeafBytes = DefaultHardLeafBytes)
        {
            if (triangles == null) throw new ArgumentNullException(nameof(triangles));
            if (triangles.Count == 0)
                throw new InvalidDataException("3D Tiles DIRT leaf is empty.");
            int3 minimum = triangles[0].Cell;
            int3 maximum = minimum;
            for (int index = 1; index < triangles.Count; index++)
            {
                minimum = math.min(minimum, triangles[index].Cell);
                maximum = math.max(maximum, triangles[index].Cell);
            }
            int3 center = new(
                (int)(((long)minimum.x + maximum.x) >> 1),
                (int)(((long)minimum.y + maximum.y) >> 1),
                (int)(((long)minimum.z + maximum.z) >> 1));
            float3 origin = new(
                MerkabaSphereFlowerAuthority.DirtGridCoordinate(center.x),
                MerkabaSphereFlowerAuthority.DirtGridCoordinate(center.y),
                MerkabaSphereFlowerAuthority.DirtGridCoordinate(center.z));
            return WriteStreamingLeaf(directory, leafIndex, minimum, maximum,
                origin, stream => MerkabaGlbWriter.WriteDirt(stream, triangles,
                    origin, progress), hardLeafBytes);
        }

        private static MerkabaTilesetLeaf WriteStreamingLeaf(
            string directory, int leafIndex, int3 minimum, int3 maximum,
            float3 localOrigin, Func<Stream, MerkabaGlbResult> write,
            long hardLeafBytes)
        {
            string name = leafIndex.ToString("D6",
                CultureInfo.InvariantCulture) + ".glb";
            string finalPath = Path.Combine(directory, "tiles", name);
            string temporaryPath = finalPath + ".tmp";
            MerkabaGlbResult result;
            using (var stream = new FileStream(temporaryPath,
                       FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       1024 * 1024, FileOptions.SequentialScan))
            {
                result = write(stream);
                stream.Flush(true);
            }
            if (result.ByteLength > hardLeafBytes)
                throw new InvalidDataException($"3D Tiles leaf {name} is " +
                    $"{result.ByteLength} bytes, above {hardLeafBytes}.");
            File.Move(temporaryPath, finalPath);
            ConvertGlbBoundsToTileset(result.Minimum, result.Maximum,
                out Vector3 contentMinimum, out Vector3 contentMaximum);
            return new MerkabaTilesetLeaf(leafIndex, minimum, maximum,
                localOrigin, contentMinimum, contentMaximum,
                result.ByteLength, result.VertexCount,
                result.PrimitiveCount);
        }

        internal static MerkabaTilesetResult CompleteStreamingPackage(
            string directory, IReadOnlyList<MerkabaTilesetLeaf> leaves,
            MerkabaSpatialBinding? spatialBinding = null)
        {
            if (leaves == null || leaves.Count == 0)
                throw new InvalidDataException("3D Tiles Flower geometry is empty.");
            var nodes = new List<Node>(leaves.Count);
            long totalBytes = 0L;
            long totalVertices = 0L;
            long totalTriangles = 0L;
            foreach (MerkabaTilesetLeaf leaf in leaves)
            {
                Vector3 translation = ConvertOriginToTileset(leaf.LocalOrigin);
                nodes.Add(new Node
                {
                    Leaf = true,
                    LeafIndex = leaf.Index,
                    MinimumCoord = leaf.MinimumCoord,
                    MaximumCoord = leaf.MaximumCoord,
                    LocalOrigin = leaf.LocalOrigin,
                    ContentMinimum = leaf.ContentMinimum,
                    ContentMaximum = leaf.ContentMaximum,
                    Minimum = leaf.ContentMinimum + translation,
                    Maximum = leaf.ContentMaximum + translation,
                    ContentBytes = leaf.ByteLength,
                    VertexCount = leaf.VertexCount,
                    TriangleCount = leaf.TriangleCount
                });
                totalBytes = checked(totalBytes + leaf.ByteLength);
                totalVertices = checked(totalVertices + leaf.VertexCount);
                totalTriangles = checked(totalTriangles + leaf.TriangleCount);
            }
            Node root = BuildStreamingHierarchy(nodes, 0, nodes.Count);
            ResolveBounds(root);
            string json = BuildTileset(root, spatialBinding);
            string manifest = Path.Combine(directory, "tileset.json");
            string temporary = manifest + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None, 64 * 1024,
                       FileOptions.None))
            using (var writer = new StreamWriter(stream,
                       new UTF8Encoding(false), 64 * 1024, true))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }
            File.Move(temporary, manifest);
            totalBytes = checked(totalBytes + new FileInfo(manifest).Length);
            return new MerkabaTilesetResult(leaves.Count, totalBytes,
                totalVertices, totalTriangles);
        }

        private static Node BuildStreamingHierarchy(List<Node> leaves,
            int offset, int count)
        {
            if (count == 1) return leaves[offset];
            int lowCount = count / 2;
            return new Node
            {
                Low = BuildStreamingHierarchy(leaves, offset, lowCount),
                High = BuildStreamingHierarchy(leaves, offset + lowCount,
                    count - lowCount)
            };
        }

        private static void ResolveBounds(Node node)
        {
            if (node.IsLeaf) return;
            ResolveBounds(node.Low);
            ResolveBounds(node.High);
            node.Minimum = Vector3.Min(node.Low.Minimum, node.High.Minimum);
            node.Maximum = Vector3.Max(node.Low.Maximum, node.High.Maximum);
        }

        private static string BuildTileset(Node root,
            MerkabaSpatialBinding? spatialBinding)
        {
            var json = new StringBuilder(4096);
            json.Append("{\"asset\":{\"version\":\"1.1\",\"generator\":")
                .Append("\"Quest Infinite Merkaba\"");
            if (spatialBinding.HasValue && spatialBinding.Value.IsValid)
                AppendSpatialBinding(json, spatialBinding.Value);
            json.Append("},\"geometricError\":")
                .Append(root.IsLeaf ? "0" : EmptyNodeGeometricError)
                .Append(',')
                .Append("\"root\":");
            AppendNode(json, root);
            json.Append('}');
            return json.ToString();
        }

        private static void AppendSpatialBinding(StringBuilder json,
            MerkabaSpatialBinding binding)
        {
            json.Append(",\"extras\":{\"questMerkabaSpatialBinding\":{")
                .Append("\"version\":")
                .Append(MerkabaSpatialBinding.CurrentVersion)
                .Append(",\"anchorUuid\":\"")
                .Append(binding.AnchorUuid.ToString("D",
                    CultureInfo.InvariantCulture))
                .Append("\",\"anchorFromPackage\":[");
            for (int column = 0; column < 4; column++)
            for (int row = 0; row < 4; row++)
            {
                if (column != 0 || row != 0) json.Append(',');
                json.Append(Number(binding.AnchorFromPackage[row, column]));
            }
            json.Append("]}}");
        }

        private static void AppendNode(StringBuilder json, Node node)
        {
            json.Append("{\"boundingVolume\":{\"box\":[");
            AppendBox(json, node.IsLeaf ? node.ContentMinimum : node.Minimum,
                node.IsLeaf ? node.ContentMaximum : node.Maximum);
            json.Append("]},\"geometricError\":")
                .Append(node.IsLeaf ? "0" : EmptyNodeGeometricError);
            if (node.IsLeaf)
            {
                Vector3 translation = ConvertOriginToTileset(node.LocalOrigin);
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

        // Leaf GLBs retain glTF's Y-up convention. 3D Tiles consumers rotate
        // glTF content into the tileset's Z-up frame, so the separately stored
        // local origin and bounds must undergo the identical fixed rotation.
        private static Vector3 ConvertOriginToTileset(float3 unity) =>
            new(-unity.x, -unity.z, unity.y);

        private static void ConvertGlbBoundsToTileset(Vector3 minimum,
            Vector3 maximum, out Vector3 convertedMinimum,
            out Vector3 convertedMaximum)
        {
            convertedMinimum = new Vector3(minimum.x, -maximum.z, minimum.y);
            convertedMaximum = new Vector3(maximum.x, -minimum.z, maximum.y);
        }

        private static string Number(float value) =>
            value.ToString("R", CultureInfo.InvariantCulture);

    }
}
