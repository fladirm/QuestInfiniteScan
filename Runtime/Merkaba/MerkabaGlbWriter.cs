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
        public readonly Vector3 Minimum;
        public readonly Vector3 Maximum;

        public MerkabaGlbResult(long byteLength, int vertexCount, int indexCount,
            int primitiveCount, Vector3 minimum, Vector3 maximum)
        {
            ByteLength = byteLength;
            VertexCount = vertexCount;
            IndexCount = indexCount;
            PrimitiveCount = primitiveCount;
            Minimum = minimum;
            Maximum = maximum;
        }
    }

    /// <summary>
    /// Multi-pass streaming GLB 2.0 writer for the read-only export membrane. No
    /// vertex/index collection proportional to export size is retained in RAM.
    /// </summary>
    internal static class MerkabaGlbWriter
    {
        private const uint GlbMagic = 0x46546C67u;
        private const uint JsonChunkType = 0x4E4F534Au;
        private const uint BinaryChunkType = 0x004E4942u;

        private readonly struct GeometryPlan
        {
            internal readonly List<GeometryVertex> Vertices;
            internal readonly List<uint> Indices;
            internal readonly int PrimitiveCount;
            internal readonly Vector3 Minimum;
            internal readonly Vector3 Maximum;

            internal GeometryPlan(List<GeometryVertex> vertices,
                List<uint> indices, int primitiveCount, Vector3 minimum,
                Vector3 maximum)
            {
                Vertices = vertices;
                Indices = indices;
                PrimitiveCount = primitiveCount;
                Minimum = minimum;
                Maximum = maximum;
            }

            internal int VertexCount => Vertices.Count;
            internal int IndexCount => Indices.Count;
        }

        private readonly struct GeometryVertex
        {
            internal readonly float3 Position;
            internal readonly float3 Normal;
            internal readonly uint PackedColor;

            internal GeometryVertex(float3 position, float3 normal,
                uint packedColor)
            {
                Position = position;
                Normal = normal;
                PackedColor = packedColor;
            }
        }

        private readonly struct VertexKey : IEquatable<VertexKey>
        {
            private readonly int _px, _py, _pz;
            private readonly int _nx, _ny, _nz;
            private readonly uint _color;

            internal VertexKey(in GeometryVertex vertex)
            {
                _px = BitConverter.SingleToInt32Bits(vertex.Position.x);
                _py = BitConverter.SingleToInt32Bits(vertex.Position.y);
                _pz = BitConverter.SingleToInt32Bits(vertex.Position.z);
                _nx = BitConverter.SingleToInt32Bits(vertex.Normal.x);
                _ny = BitConverter.SingleToInt32Bits(vertex.Normal.y);
                _nz = BitConverter.SingleToInt32Bits(vertex.Normal.z);
                _color = vertex.PackedColor;
            }

            public bool Equals(VertexKey other) =>
                _px == other._px && _py == other._py && _pz == other._pz &&
                _nx == other._nx && _ny == other._ny && _nz == other._nz &&
                _color == other._color;

            public override bool Equals(object obj) =>
                obj is VertexKey other && Equals(other);

            public override int GetHashCode()
            {
                var hash = new HashCode();
                hash.Add(_px); hash.Add(_py); hash.Add(_pz);
                hash.Add(_nx); hash.Add(_ny); hash.Add(_nz);
                hash.Add(_color);
                return hash.ToHashCode();
            }
        }

        internal static MerkabaGlbResult Write(Stream destination,
            MerkabaExportMembraneResult membrane,
            IProgress<OperationWorkProgress> progress = null) =>
            Write(destination, membrane, float3.zero, progress);

        internal static MerkabaGlbResult Write(Stream destination,
            MerkabaExportMembraneResult membrane, float3 localOrigin,
            IProgress<OperationWorkProgress> progress = null)
        {
            if (destination == null || !destination.CanWrite)
                throw new ArgumentException("GLB destination must be writable.",
                    nameof(destination));
            if (membrane == null) throw new ArgumentNullException(nameof(membrane));
            if (membrane.Patches.Count == 0 && membrane.LegacyKernels.Count == 0)
                throw new InvalidDataException("GLB membrane is empty.");

            GeometryPlan plan = Plan(membrane, localOrigin, progress);
            long positionsOffset = 0;
            long positionsLength = checked((long)plan.VertexCount * 12);
            long normalsOffset = positionsOffset + positionsLength;
            long normalsLength = checked((long)plan.VertexCount * 12);
            long colorsOffset = normalsOffset + normalsLength;
            long colorsLength = checked((long)plan.VertexCount * 4);
            long indicesOffset = colorsOffset + colorsLength;
            long indicesLength = checked((long)plan.IndexCount * 4);
            long binaryLength = checked(indicesOffset + indicesLength);
            if (binaryLength > uint.MaxValue)
                throw new InvalidDataException(
                    "GLB binary chunk exceeds the 4 GiB container limit.");

            string json = BuildJson(plan.VertexCount, plan.IndexCount,
                binaryLength, positionsOffset, positionsLength, normalsOffset,
                normalsLength, colorsOffset, colorsLength, indicesOffset,
                indicesLength, plan.Minimum, plan.Maximum);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            int paddedJsonLength = Align4(jsonBytes.Length);
            long totalLength = checked(12L + 8L + paddedJsonLength + 8L +
                binaryLength);
            if (totalLength > uint.MaxValue)
                throw new InvalidDataException("GLB exceeds the 4 GiB container limit.");

            long start = destination.CanSeek ? destination.Position : 0;
            using var writer = new BinaryWriter(destination,
                new UTF8Encoding(false), true);
            writer.Write(GlbMagic);
            writer.Write(2u);
            writer.Write((uint)totalLength);
            writer.Write((uint)paddedJsonLength);
            writer.Write(JsonChunkType);
            writer.Write(jsonBytes);
            for (int index = jsonBytes.Length; index < paddedJsonLength; index++)
                writer.Write((byte)0x20);
            writer.Write((uint)binaryLength);
            writer.Write(BinaryChunkType);
            long binaryHeaderBytes = 12L + 8L + paddedJsonLength + 8L;
            Report(progress, ScanOperationStage.WritingFile,
                binaryHeaderBytes, totalLength, "Wrote GLB header");

            long passBytes = 0;
            foreach (GeometryVertex vertex in plan.Vertices)
            {
                Vector3 value = Convert(vertex.Position);
                writer.Write(value.x);
                writer.Write(value.y);
                writer.Write(value.z);
                passBytes += 12;
                ReportVertexPass(progress, binaryHeaderBytes + passBytes,
                    totalLength, (int)(passBytes / 12), plan.VertexCount,
                    "Writing POSITION data");
            }

            passBytes = 0;
            foreach (GeometryVertex vertex in plan.Vertices)
            {
                Vector3 value = Convert(vertex.Normal).normalized;
                writer.Write(value.x);
                writer.Write(value.y);
                writer.Write(value.z);
                passBytes += 12;
                ReportVertexPass(progress, binaryHeaderBytes + normalsOffset +
                    passBytes, totalLength, (int)(passBytes / 12),
                    plan.VertexCount, "Writing NORMAL data");
            }

            passBytes = 0;
            foreach (GeometryVertex vertex in plan.Vertices)
            {
                Color32 color = KernelState.UnpackColor(vertex.PackedColor);
                writer.Write(color.r);
                writer.Write(color.g);
                writer.Write(color.b);
                writer.Write((byte)255);
                passBytes += 4;
                ReportVertexPass(progress, binaryHeaderBytes + colorsOffset +
                    passBytes, totalLength, (int)(passBytes / 4),
                    plan.VertexCount, "Writing COLOR_0 data");
            }

            WriteIndices(writer, plan, progress,
                binaryHeaderBytes + indicesOffset, totalLength);
            writer.Flush();
            Report(progress, ScanOperationStage.WritingFile, totalLength,
                totalLength, "GLB bytes written");
            long written = destination.CanSeek ? destination.Position - start :
                totalLength;
            if (written != totalLength)
                throw new InvalidDataException(
                    $"GLB length mismatch: {written} != {totalLength}.");
            return new MerkabaGlbResult(written, plan.VertexCount,
                plan.IndexCount, plan.PrimitiveCount, plan.Minimum,
                plan.Maximum);
        }

        internal static int CheckedIndexCountForPrimitiveCount(long primitiveCount)
        {
            if (primitiveCount <= 0)
                throw new InvalidDataException("GLB membrane is empty.");
            try
            {
                long indices = checked(primitiveCount * 3L);
                long worstCaseBinaryBytes = checked(indices * 32L);
                if (worstCaseBinaryBytes > uint.MaxValue || indices > int.MaxValue)
                    throw new InvalidDataException(
                        "GLB geometry exceeds the 4 GiB container limit.");
                return (int)indices;
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException(
                    "GLB geometry exceeds the 4 GiB container limit.", exception);
            }
        }

        private static GeometryPlan Plan(MerkabaExportMembraneResult membrane,
            float3 localOrigin, IProgress<OperationWorkProgress> progress)
        {
            var occupied = new HashSet<int3>(
                membrane.CanonicalOccupiedCoordinates);
            int primitiveCount = checked(membrane.Patches.Count * 2);
            foreach (MerkabaKernelSnapshot kernel in membrane.LegacyKernels)
                foreach (int _ in MerkabaCanonicalGeometry.VisiblePrimitives(
                             kernel.Coord, occupied.Contains))
                    primitiveCount = checked(primitiveCount + 1);
            int indexCapacity = CheckedIndexCountForPrimitiveCount(
                primitiveCount);
            var vertices = new List<GeometryVertex>(Math.Min(indexCapacity,
                checked(membrane.Patches.Count * 4 +
                    membrane.LegacyKernels.Count * 3)));
            var indices = new List<uint>(indexCapacity);
            var vertexLookup = new Dictionary<VertexKey, uint>();

            Vector3 minimum = new(float.PositiveInfinity, float.PositiveInfinity,
                float.PositiveInfinity);
            Vector3 maximum = new(float.NegativeInfinity, float.NegativeInfinity,
                float.NegativeInfinity);
            uint AddVertex(float3 position, float3 normal, uint packedColor)
            {
                var vertex = new GeometryVertex(position - localOrigin,
                    normal, packedColor);
                var key = new VertexKey(vertex);
                if (vertexLookup.TryGetValue(key, out uint existing))
                    return existing;
                uint index = checked((uint)vertices.Count);
                vertices.Add(vertex);
                vertexLookup.Add(key, index);
                Vector3 value = Convert(vertex.Position);
                minimum = Vector3.Min(minimum, value);
                maximum = Vector3.Max(maximum, value);
                return index;
            }

            int completedPrimitives = 0;
            foreach (MerkabaExportMembranePatch patch in membrane.Patches)
            {
                uint v0 = AddVertex(patch.Corner00, patch.Normal,
                    patch.PackedColor);
                uint v1 = AddVertex(patch.Corner10, patch.Normal,
                    patch.PackedColor);
                uint v2 = AddVertex(patch.Corner11, patch.Normal,
                    patch.PackedColor);
                uint v3 = AddVertex(patch.Corner01, patch.Normal,
                    patch.PackedColor);
                AddTriangle(indices, v0, v2, v1);
                AddTriangle(indices, v0, v3, v2);
                completedPrimitives += 2;
                if (completedPrimitives == primitiveCount ||
                    (completedPrimitives & 0xffff) == 0)
                    Report(progress, ScanOperationStage.BuildingMerkabaGeometry,
                        completedPrimitives, primitiveCount,
                        $"Built {completedPrimitives}/{primitiveCount} export triangles");
            }
            foreach (MerkabaKernelSnapshot kernel in membrane.LegacyKernels)
            foreach (int primitiveId in MerkabaCanonicalGeometry.VisiblePrimitives(
                         kernel.Coord, occupied.Contains))
            {
                float3 center = MerkabaConstants.WorldCenter(kernel.Coord);
                MerkabaCanonicalGeometry.PrimitiveVertex(primitiveId, 0,
                    out float3 local0, out float3 normal0);
                MerkabaCanonicalGeometry.PrimitiveVertex(primitiveId, 1,
                    out float3 local1, out float3 normal1);
                MerkabaCanonicalGeometry.PrimitiveVertex(primitiveId, 2,
                    out float3 local2, out float3 normal2);
                uint v0 = AddVertex(center + local0, normal0,
                    kernel.State.PackedColor);
                uint v1 = AddVertex(center + local1, normal1,
                    kernel.State.PackedColor);
                uint v2 = AddVertex(center + local2, normal2,
                    kernel.State.PackedColor);
                AddTriangle(indices, v0, v2, v1);
                completedPrimitives++;
            }
            if (completedPrimitives != primitiveCount ||
                indices.Count != indexCapacity)
                throw new InvalidDataException(
                    "GLB indexed geometry construction count mismatch.");
            return new GeometryPlan(vertices, indices, primitiveCount,
                minimum, maximum);
        }

        private static void AddTriangle(List<uint> indices, uint a, uint b,
            uint c)
        {
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        private static void WriteIndices(BinaryWriter writer,
            GeometryPlan plan, IProgress<OperationWorkProgress> progress,
            long binaryOffset, long totalLength)
        {
            int completed = 0;
            foreach (uint index in plan.Indices)
            {
                writer.Write(index);
                completed++;
                ReportIndexPass(progress, binaryOffset, totalLength, completed,
                    plan.IndexCount);
            }
            if (completed != plan.IndexCount)
                throw new InvalidDataException("GLB index streaming count mismatch.");
        }

        private static void ReportVertexPass(
            IProgress<OperationWorkProgress> progress, long completedBytes,
            long totalBytes, int completed, int total, string text)
        {
            if (completed != total && (completed & 0x3ffff) != 0) return;
            Report(progress, ScanOperationStage.WritingFile, completedBytes,
                totalBytes, text);
        }

        private static void ReportIndexPass(
            IProgress<OperationWorkProgress> progress, long binaryOffset,
            long totalLength, int completed, int total)
        {
            if (completed != total && (completed & 0x3ffff) != 0) return;
            Report(progress, ScanOperationStage.WritingFile,
                binaryOffset + (long)completed * 4, totalLength,
                "Writing triangle indices");
        }

        private static void Report(IProgress<OperationWorkProgress> progress,
            ScanOperationStage stage, long completed, long total, string text) =>
            progress?.Report(new OperationWorkProgress(stage, completed, total,
                text));

        private static Vector3 Convert(Vector3 unity) =>
            new(-unity.x, unity.y, unity.z);
        private static Vector3 Convert(float3 unity) => Convert((Vector3)unity);

        private static string BuildJson(int vertexCount, int indexCount,
            long binaryLength, long positionsOffset, long positionsLength,
            long normalsOffset, long normalsLength, long colorsOffset,
            long colorsLength, long indicesOffset, long indicesLength,
            Vector3 minimum, Vector3 maximum)
        {
            string min = $"[{Number(minimum.x)},{Number(minimum.y)},{Number(minimum.z)}]";
            string max = $"[{Number(maximum.x)},{Number(maximum.y)},{Number(maximum.z)}]";
            var json = new StringBuilder(1400);
            json.Append("{\"asset\":{\"version\":\"2.0\",\"generator\":\"Quest Infinite Merkaba\"},");
            json.Append("\"scene\":0,\"scenes\":[{\"nodes\":[0]}],");
            json.Append("\"nodes\":[{\"name\":\"MerkabaGrid\",\"mesh\":0}],");
            json.Append("\"meshes\":[{\"name\":\"M8 Measured Membrane\",\"primitives\":[{");
            json.Append("\"attributes\":{\"POSITION\":0,\"NORMAL\":1,\"COLOR_0\":2},");
            json.Append("\"indices\":3,\"material\":0,\"mode\":4}]}],");
            json.Append("\"materials\":[{\"name\":\"M8 Membrane Matte\",");
            json.Append("\"pbrMetallicRoughness\":{\"baseColorFactor\":[1,1,1,1],");
            json.Append("\"metallicFactor\":0,\"roughnessFactor\":0.85},\"doubleSided\":true}],");
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

        private static void BufferView(StringBuilder json, long offset,
            long length, int target)
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
