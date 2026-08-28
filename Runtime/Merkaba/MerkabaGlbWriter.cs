using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan
{
    internal readonly struct MerkabaGlbResult
    {
        public readonly long ByteLength;
        public readonly int VertexCount;
        public readonly int IndexCount;
        public readonly int PrimitiveCount;

        public MerkabaGlbResult(long byteLength, int vertexCount, int indexCount,
            int primitiveCount)
        {
            ByteLength = byteLength;
            VertexCount = vertexCount;
            IndexCount = indexCount;
            PrimitiveCount = primitiveCount;
        }
    }

    /// <summary>
    /// Dependency-free GLB 2.0 writer adapted from target history e9f37c1. It emits the
    /// live kernel authority only as an offline indexed POSITION/NORMAL/COLOR_0 readout.
    /// </summary>
    internal static class MerkabaGlbWriter
    {
        private const uint GlbMagic = 0x46546C67u;
        private const uint JsonChunkType = 0x4E4F534Au;
        private const uint BinaryChunkType = 0x004E4942u;
        private const int MaximumVertices = 24_000_000;

        private readonly struct ExportPrimitive
        {
            public readonly int3 Coord;
            public readonly KernelState State;
            public readonly byte PrimitiveId;

            public ExportPrimitive(int3 coord, KernelState state, int primitiveId)
            {
                Coord = coord;
                State = state;
                PrimitiveId = checked((byte)primitiveId);
            }
        }

        internal static MerkabaGlbResult Write(Stream destination,
            IReadOnlyList<MerkabaKernelSnapshot> occupiedKernels,
            Action onGeometryReadyForWriting = null)
        {
            if (destination == null || !destination.CanWrite)
                throw new ArgumentException("GLB destination must be writable.", nameof(destination));
            if (occupiedKernels == null) throw new ArgumentNullException(nameof(occupiedKernels));

            var occupied = new HashSet<int3>();
            foreach (MerkabaKernelSnapshot kernel in occupiedKernels)
            {
                if (!kernel.State.IsOccupied || !occupied.Add(kernel.Coord))
                    throw new InvalidDataException("GLB input contains duplicate/non-occupied kernels.");
            }

            var export = new List<ExportPrimitive>(occupied.Count * 8);
            foreach (MerkabaKernelSnapshot kernel in occupiedKernels)
            {
                foreach (int primitiveId in MerkabaCanonicalGeometry.VisiblePrimitives(
                             kernel.Coord, occupied.Contains))
                    export.Add(new ExportPrimitive(kernel.Coord, kernel.State,
                        primitiveId));
            }
            long primitiveCount = export.Count;
            long vertexCountLong = checked(primitiveCount *
                MerkabaCanonicalGeometry.VerticesPerPrimitive);
            if (vertexCountLong <= 0 || vertexCountLong > MaximumVertices)
                throw new InvalidDataException("GLB boundary vertex count is empty or too large.");
            int vertexCount = checked((int)vertexCountLong);
            int indexCount = vertexCount;

            Vector3 minimum = new(float.PositiveInfinity, float.PositiveInfinity,
                float.PositiveInfinity);
            Vector3 maximum = new(float.NegativeInfinity, float.NegativeInfinity,
                float.NegativeInfinity);
            VisitVertices(export, (position, _, _) =>
            {
                Vector3 converted = Convert(position);
                minimum = Vector3.Min(minimum, converted);
                maximum = Vector3.Max(maximum, converted);
            });

            int positionsOffset = 0;
            int positionsLength = checked(vertexCount * 12);
            int normalsOffset = positionsOffset + positionsLength;
            int normalsLength = checked(vertexCount * 12);
            int colorsOffset = normalsOffset + normalsLength;
            int colorsLength = checked(vertexCount * 4);
            int indicesOffset = colorsOffset + colorsLength;
            int indicesLength = checked(indexCount * 4);
            int binaryLength = checked(indicesOffset + indicesLength);

            string json = BuildJson(vertexCount, indexCount, binaryLength,
                positionsOffset, positionsLength, normalsOffset, normalsLength,
                colorsOffset, colorsLength, indicesOffset, indicesLength,
                minimum, maximum);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            int paddedJsonLength = Align4(jsonBytes.Length);
            long totalLength = checked(12L + 8L + paddedJsonLength + 8L + binaryLength);
            if (totalLength > uint.MaxValue)
                throw new InvalidDataException("GLB exceeds the 4 GiB container limit.");

            onGeometryReadyForWriting?.Invoke();
            long start = destination.CanSeek ? destination.Position : 0;
            using var writer = new BinaryWriter(destination, new UTF8Encoding(false), true);
            writer.Write(GlbMagic);
            writer.Write(2u);
            writer.Write((uint)totalLength);
            writer.Write((uint)paddedJsonLength);
            writer.Write(JsonChunkType);
            writer.Write(jsonBytes);
            for (int i = jsonBytes.Length; i < paddedJsonLength; i++) writer.Write((byte)0x20);
            writer.Write((uint)binaryLength);
            writer.Write(BinaryChunkType);

            VisitVertices(export, (position, _, _) =>
            {
                Vector3 value = Convert(position);
                writer.Write(value.x); writer.Write(value.y); writer.Write(value.z);
            });
            VisitVertices(export, (_, normal, _) =>
            {
                Vector3 value = Convert(normal).normalized;
                writer.Write(value.x); writer.Write(value.y); writer.Write(value.z);
            });
            VisitVertices(export, (_, _, color) =>
            {
                writer.Write(color.r); writer.Write(color.g); writer.Write(color.b);
                writer.Write((byte)255);
            });
            for (uint triangle = 0; triangle < indexCount; triangle += 3)
            {
                // Mirroring X changes handedness; reverse every source triangle.
                writer.Write(triangle);
                writer.Write(triangle + 2);
                writer.Write(triangle + 1);
            }
            writer.Flush();
            long written = destination.CanSeek ? destination.Position - start : totalLength;
            if (written != totalLength)
                throw new InvalidDataException($"GLB length mismatch: {written} != {totalLength}.");
            return new MerkabaGlbResult(written, vertexCount, indexCount,
                checked((int)primitiveCount));
        }

        private static void VisitVertices(IReadOnlyList<ExportPrimitive> primitives,
            Action<Vector3, Vector3, Color32> visitor)
        {
            foreach (ExportPrimitive primitive in primitives)
            for (int vertex = 0;
                 vertex < MerkabaCanonicalGeometry.VerticesPerPrimitive; vertex++)
            {
                MerkabaCanonicalGeometry.PrimitiveVertex(primitive.PrimitiveId,
                    vertex, out float3 local, out float3 normal);
                float3 position = MerkabaConstants.WorldCenter(primitive.Coord) + local;
                visitor(position, normal, primitive.State.Color);
            }
        }

        private static Vector3 Convert(Vector3 unity) =>
            new(-unity.x, unity.y, unity.z);
        private static Vector3 Convert(float3 unity) => Convert((Vector3)unity);

        private static string BuildJson(int vertexCount, int indexCount, int binaryLength,
            int positionsOffset, int positionsLength, int normalsOffset, int normalsLength,
            int colorsOffset, int colorsLength, int indicesOffset, int indicesLength,
            Vector3 minimum, Vector3 maximum)
        {
            string min = $"[{Number(minimum.x)},{Number(minimum.y)},{Number(minimum.z)}]";
            string max = $"[{Number(maximum.x)},{Number(maximum.y)},{Number(maximum.z)}]";
            var json = new StringBuilder(1400);
            json.Append("{\"asset\":{\"version\":\"2.0\",\"generator\":\"Quest Infinite Merkaba\"},");
            json.Append("\"scene\":0,\"scenes\":[{\"nodes\":[0]}],");
            json.Append("\"nodes\":[{\"name\":\"MerkabaGrid\",\"mesh\":0}],");
            json.Append("\"meshes\":[{\"name\":\"Merkaba Boundary\",\"primitives\":[{");
            json.Append("\"attributes\":{\"POSITION\":0,\"NORMAL\":1,\"COLOR_0\":2},");
            json.Append("\"indices\":3,\"material\":0,\"mode\":4}]}],");
            json.Append("\"materials\":[{\"name\":\"Merkaba Matte\",");
            json.Append("\"pbrMetallicRoughness\":{\"baseColorFactor\":[1,1,1,1],");
            json.Append("\"metallicFactor\":0,\"roughnessFactor\":0.85},\"doubleSided\":false}],");
            json.Append("\"buffers\":[{\"byteLength\":").Append(binaryLength).Append("}],");
            json.Append("\"bufferViews\":[");
            BufferView(json, positionsOffset, positionsLength, 34962); json.Append(',');
            BufferView(json, normalsOffset, normalsLength, 34962); json.Append(',');
            BufferView(json, colorsOffset, colorsLength, 34962); json.Append(',');
            BufferView(json, indicesOffset, indicesLength, 34963); json.Append("],");
            json.Append("\"accessors\":[");
            json.Append("{\"bufferView\":0,\"componentType\":5126,\"count\":")
                .Append(vertexCount).Append(",\"type\":\"VEC3\",\"min\":")
                .Append(min).Append(",\"max\":").Append(max).Append("},");
            json.Append("{\"bufferView\":1,\"componentType\":5126,\"count\":")
                .Append(vertexCount).Append(",\"type\":\"VEC3\"},");
            json.Append("{\"bufferView\":2,\"componentType\":5121,\"normalized\":true,\"count\":")
                .Append(vertexCount).Append(",\"type\":\"VEC4\"},");
            json.Append("{\"bufferView\":3,\"componentType\":5125,\"count\":")
                .Append(indexCount).Append(",\"type\":\"SCALAR\"}]}");
            return json.ToString();
        }

        private static void BufferView(StringBuilder json, int offset, int length, int target)
        {
            json.Append("{\"buffer\":0,\"byteOffset\":").Append(offset)
                .Append(",\"byteLength\":").Append(length)
                .Append(",\"target\":").Append(target).Append('}');
        }

        private static string Number(float value) =>
            value.ToString("R", CultureInfo.InvariantCulture);
        private static int Align4(int value) => (value + 3) & ~3;
    }
}
