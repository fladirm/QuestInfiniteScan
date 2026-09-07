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

    /// <summary>GLB presentation writer; DIRT remains explicitly inferred.</summary>
    internal static class MerkabaGlbWriter
    {
        private const uint GlbMagic = 0x46546C67u;
        private const uint JsonChunkType = 0x4E4F534Au;
        private const uint BinaryChunkType = 0x004E4942u;

        private readonly struct PrimitiveRange
        {
            internal readonly int FirstIndex;
            internal readonly int IndexCount;
            internal readonly bool Dirt;

            internal PrimitiveRange(int firstIndex, int indexCount, bool dirt)
            {
                FirstIndex = firstIndex;
                IndexCount = indexCount;
                Dirt = dirt;
            }
        }

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

        /// <summary>
        /// Bounded-memory GLB assembly. Each spatial membrane batch is indexed
        /// independently and appended to sequential attribute/index spools; the
        /// complete GLB is published only after all batches have succeeded.
        /// </summary>
        internal sealed class StreamingSession : IDisposable
        {
            private readonly string _directory;
            private readonly FileStream _positions;
            private readonly FileStream _normals;
            private readonly FileStream _colors;
            private readonly FileStream _indices;
            private readonly BinaryWriter _positionWriter;
            private readonly BinaryWriter _normalWriter;
            private readonly BinaryWriter _colorWriter;
            private readonly BinaryWriter _indexWriter;
            private int _vertexCount;
            private int _indexCount;
            private int _primitiveCount;
            private readonly List<PrimitiveRange> _ranges = new();
            private Vector3 _minimum = new(float.PositiveInfinity,
                float.PositiveInfinity, float.PositiveInfinity);
            private Vector3 _maximum = new(float.NegativeInfinity,
                float.NegativeInfinity, float.NegativeInfinity);
            private bool _completed;
            private bool _disposed;

            internal StreamingSession(string directory)
            {
                if (string.IsNullOrWhiteSpace(directory))
                    throw new ArgumentException(
                        "GLB spool directory is required.", nameof(directory));
                if (Directory.Exists(directory))
                    throw new IOException(
                        "GLB spool directory already exists: " + directory);
                _directory = directory;
                Directory.CreateDirectory(directory);
                _positions = Open("positions.bin");
                _normals = Open("normals.bin");
                _colors = Open("colors.bin");
                _indices = Open("indices.bin");
                var encoding = new UTF8Encoding(false);
                _positionWriter = new BinaryWriter(_positions, encoding, true);
                _normalWriter = new BinaryWriter(_normals, encoding, true);
                _colorWriter = new BinaryWriter(_colors, encoding, true);
                _indexWriter = new BinaryWriter(_indices, encoding, true);
            }

            internal void Append(MerkabaExportMembraneResult membrane,
                IProgress<OperationWorkProgress> progress = null)
                => AppendPlan(Plan(membrane, float3.zero, progress), false);

            internal void AppendDirt(IReadOnlyList<MerkabaDirtTriangle> triangles,
                IProgress<OperationWorkProgress> progress = null)
                => AppendPlan(PlanDirt(triangles, float3.zero, progress), true);

            private void AppendPlan(GeometryPlan plan, bool dirt)
            {
                ThrowIfClosed();
                if (_completed)
                    throw new InvalidOperationException(
                        "The bounded GLB stream is already complete.");
                int baseVertex = _vertexCount;
                AddRange(_ranges, _indexCount, plan.IndexCount, dirt);
                _vertexCount = checked(_vertexCount + plan.VertexCount);
                _indexCount = checked(_indexCount + plan.IndexCount);
                _primitiveCount = checked(_primitiveCount +
                    plan.PrimitiveCount);
                _minimum = Vector3.Min(_minimum, plan.Minimum);
                _maximum = Vector3.Max(_maximum, plan.Maximum);

                foreach (GeometryVertex vertex in plan.Vertices)
                {
                    Vector3 position = Convert(vertex.Position);
                    _positionWriter.Write(position.x);
                    _positionWriter.Write(position.y);
                    _positionWriter.Write(position.z);
                    Vector3 normal = Convert(vertex.Normal).normalized;
                    _normalWriter.Write(normal.x);
                    _normalWriter.Write(normal.y);
                    _normalWriter.Write(normal.z);
                    Color32 color = KernelState.UnpackColor(
                        vertex.PackedColor);
                    _colorWriter.Write(color.r);
                    _colorWriter.Write(color.g);
                    _colorWriter.Write(color.b);
                    _colorWriter.Write((byte)255);
                }
                foreach (uint index in plan.Indices)
                    _indexWriter.Write(checked((uint)baseVertex + index));
            }

            internal MerkabaGlbResult Complete(Stream destination,
                IProgress<OperationWorkProgress> progress = null)
            {
                ThrowIfClosed();
                if (_completed)
                    throw new InvalidOperationException(
                        "The bounded GLB stream is already complete.");
                if (_primitiveCount == 0)
                    throw new InvalidDataException("GLB membrane is empty.");
                if (destination == null || !destination.CanWrite)
                    throw new ArgumentException(
                        "GLB destination must be writable.",
                        nameof(destination));

                FlushSpools();
                long positionsOffset = 0L;
                long positionsLength = _positions.Length;
                long normalsOffset = positionsLength;
                long normalsLength = _normals.Length;
                long colorsOffset = checked(normalsOffset + normalsLength);
                long colorsLength = _colors.Length;
                long indicesOffset = checked(colorsOffset + colorsLength);
                long indicesLength = _indices.Length;
                long binaryLength = checked(indicesOffset + indicesLength);
                if (binaryLength > uint.MaxValue)
                    throw new InvalidDataException(
                        "GLB binary chunk exceeds the 4 GiB container limit; " +
                        "use the 3D Tiles export for a larger scan.");

                string json = BuildJson(_vertexCount, _indexCount,
                    binaryLength, positionsOffset, positionsLength,
                    normalsOffset, normalsLength, colorsOffset, colorsLength,
                    indicesOffset, indicesLength, _minimum, _maximum, _ranges);
                byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
                int paddedJsonLength = Align4(jsonBytes.Length);
                long totalLength = checked(12L + 8L + paddedJsonLength + 8L +
                    binaryLength);
                if (totalLength > uint.MaxValue)
                    throw new InvalidDataException(
                        "GLB exceeds the 4 GiB container limit; use the 3D " +
                        "Tiles export for a larger scan.");

                long start = destination.CanSeek ? destination.Position : 0L;
                using var writer = new BinaryWriter(destination,
                    new UTF8Encoding(false), true);
                writer.Write(GlbMagic);
                writer.Write(2u);
                writer.Write((uint)totalLength);
                writer.Write((uint)paddedJsonLength);
                writer.Write(JsonChunkType);
                writer.Write(jsonBytes);
                for (int index = jsonBytes.Length; index < paddedJsonLength;
                     index++)
                    writer.Write((byte)0x20);
                writer.Write((uint)binaryLength);
                writer.Write(BinaryChunkType);
                writer.Flush();

                long completed = 12L + 8L + paddedJsonLength + 8L;
                Copy(_positions, destination);
                completed += positionsLength;
                Report(progress, ScanOperationStage.WritingFile, completed,
                    totalLength, "Wrote bounded POSITION data");
                Copy(_normals, destination);
                completed += normalsLength;
                Report(progress, ScanOperationStage.WritingFile, completed,
                    totalLength, "Wrote bounded NORMAL data");
                Copy(_colors, destination);
                completed += colorsLength;
                Report(progress, ScanOperationStage.WritingFile, completed,
                    totalLength, "Wrote bounded COLOR_0 data");
                Copy(_indices, destination);
                completed += indicesLength;
                writer.Flush();
                if (completed != totalLength)
                    throw new InvalidDataException(
                        $"GLB length mismatch: {completed} != {totalLength}.");
                long written = destination.CanSeek
                    ? destination.Position - start : totalLength;
                if (written != totalLength)
                    throw new InvalidDataException(
                        $"GLB length mismatch: {written} != {totalLength}.");
                _completed = true;
                return new MerkabaGlbResult(written, _vertexCount,
                    _indexCount, _primitiveCount, _minimum, _maximum);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _positionWriter.Dispose();
                _normalWriter.Dispose();
                _colorWriter.Dispose();
                _indexWriter.Dispose();
                _positions.Dispose();
                _normals.Dispose();
                _colors.Dispose();
                _indices.Dispose();
                if (Directory.Exists(_directory))
                    Directory.Delete(_directory, true);
            }

            private FileStream Open(string name) => new(
                Path.Combine(_directory, name), FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None, 1024 * 1024,
                FileOptions.SequentialScan);

            private void FlushSpools()
            {
                _positionWriter.Flush();
                _normalWriter.Flush();
                _colorWriter.Flush();
                _indexWriter.Flush();
                _positions.Flush();
                _normals.Flush();
                _colors.Flush();
                _indices.Flush();
                if (_positions.Length != checked((long)_vertexCount * 12L) ||
                    _normals.Length != checked((long)_vertexCount * 12L) ||
                    _colors.Length != checked((long)_vertexCount * 4L) ||
                    _indices.Length != checked((long)_indexCount * 4L))
                    throw new InvalidDataException(
                        "Bounded GLB spool length mismatch.");
            }

            private static void Copy(FileStream source, Stream destination)
            {
                source.Position = 0L;
                source.CopyTo(destination, 1024 * 1024);
            }

            private void ThrowIfClosed()
            {
                if (_disposed) throw new ObjectDisposedException(
                    nameof(StreamingSession));
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
            if (membrane.Patches.Count == 0)
                throw new InvalidDataException("GLB membrane is empty.");

            GeometryPlan plan = Plan(membrane, localOrigin, progress);
            return WritePlan(destination, plan, false, progress);
        }

        internal static MerkabaGlbResult WriteDirt(Stream destination,
            IReadOnlyList<MerkabaDirtTriangle> triangles, float3 localOrigin,
            IProgress<OperationWorkProgress> progress = null)
        {
            if (destination == null || !destination.CanWrite)
                throw new ArgumentException("GLB destination must be writable.",
                    nameof(destination));
            return WritePlan(destination, PlanDirt(triangles, localOrigin,
                progress), true, progress);
        }

        private static MerkabaGlbResult WritePlan(Stream destination,
            GeometryPlan plan, bool dirt,
            IProgress<OperationWorkProgress> progress)
        {
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
                indicesLength, plan.Minimum, plan.Maximum,
                new[] { new PrimitiveRange(0, plan.IndexCount, dirt) });
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
            int primitiveCount = checked(membrane.Patches.Count * 2);
            int indexCapacity = CheckedIndexCountForPrimitiveCount(
                primitiveCount);
            var vertices = new List<GeometryVertex>(Math.Min(indexCapacity,
                checked(membrane.Patches.Count * 4)));
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

        private static GeometryPlan PlanDirt(
            IReadOnlyList<MerkabaDirtTriangle> triangles, float3 localOrigin,
            IProgress<OperationWorkProgress> progress)
        {
            if (triangles == null)
                throw new ArgumentNullException(nameof(triangles));
            int indexCount = CheckedIndexCountForPrimitiveCount(triangles.Count);
            var vertices = new List<GeometryVertex>(indexCount);
            var indices = new List<uint>(indexCount);
            Vector3 minimum = new(float.PositiveInfinity,
                float.PositiveInfinity, float.PositiveInfinity);
            Vector3 maximum = new(float.NegativeInfinity,
                float.NegativeInfinity, float.NegativeInfinity);
            for (int triangleIndex = 0; triangleIndex < triangles.Count;
                 triangleIndex++)
            {
                MerkabaDirtTriangle triangle = triangles[triangleIndex];
                float3 normal = -MerkabaSphereFlowerAuthority
                    .DirtFaceDirection(triangle.Face);
                uint first = checked((uint)vertices.Count);
                for (int vertex = 0; vertex < 3; vertex++)
                {
                    // Canonical rounding happens before subtracting RTC. No
                    // floating weld or measured-plane surrogate is involved.
                    float3 position = MerkabaSphereFlowerAuthority
                        .DirtFaceGridPosition(triangle.Cell, triangle.Face,
                            triangle.Half, vertex) - localOrigin;
                    vertices.Add(new GeometryVertex(position, normal, 0xffffffffu));
                    Vector3 converted = Convert(position);
                    minimum = Vector3.Min(minimum, converted);
                    maximum = Vector3.Max(maximum, converted);
                }
                // Unity -> glTF reflects X; reverse the shared face winding.
                AddTriangle(indices, first, first + 2u, first + 1u);
            }
            Report(progress, ScanOperationStage.BuildingMerkabaGeometry,
                triangles.Count, triangles.Count,
                $"Materialized {triangles.Count} inferred DIRT triangles");
            return new GeometryPlan(vertices, indices, triangles.Count,
                minimum, maximum);
        }

        private static void AddRange(List<PrimitiveRange> ranges, int first,
            int count, bool dirt)
        {
            if (count == 0) return;
            if (ranges.Count != 0)
            {
                PrimitiveRange previous = ranges[ranges.Count - 1];
                if (previous.Dirt == dirt &&
                    checked(previous.FirstIndex + previous.IndexCount) == first)
                {
                    ranges[ranges.Count - 1] = new PrimitiveRange(
                        previous.FirstIndex, checked(previous.IndexCount + count),
                        dirt);
                    return;
                }
            }
            ranges.Add(new PrimitiveRange(first, count, dirt));
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
            Vector3 minimum, Vector3 maximum,
            IReadOnlyList<PrimitiveRange> ranges)
        {
            string min = $"[{Number(minimum.x)},{Number(minimum.y)},{Number(minimum.z)}]";
            string max = $"[{Number(maximum.x)},{Number(maximum.y)},{Number(maximum.z)}]";
            bool hasDirt = false;
            foreach (PrimitiveRange range in ranges) hasDirt |= range.Dirt;
            var json = new StringBuilder(1400);
            json.Append("{\"asset\":{\"version\":\"2.0\",\"generator\":\"Quest Infinite Merkaba\"},");
            json.Append("\"scene\":0,\"scenes\":[{\"nodes\":[0]}],");
            json.Append("\"nodes\":[{\"name\":\"MerkabaGrid\",\"mesh\":0}],");
            json.Append("\"meshes\":[{\"name\":\"M8 Presentation\",\"primitives\":[");
            for (int primitive = 0; primitive < ranges.Count; primitive++)
            {
                if (primitive != 0) json.Append(',');
                PrimitiveRange range = ranges[primitive];
                json.Append("{\"attributes\":{\"POSITION\":0,\"NORMAL\":1,\"COLOR_0\":2},");
                json.Append("\"indices\":").Append(3 + primitive)
                    .Append(",\"material\":").Append(range.Dirt ? 1 : 0)
                    .Append(",\"mode\":4");
                if (range.Dirt)
                    json.Append(",\"extras\":{\"m8Provenance\":\"DIRT\",\"inferredSupport\":true,\"capturedRadiance\":false}");
                json.Append('}');
            }
            json.Append("]}],");
            json.Append("\"materials\":[{\"name\":\"M8 Membrane Matte\",");
            json.Append("\"pbrMetallicRoughness\":{\"baseColorFactor\":[1,1,1,1],");
            json.Append("\"metallicFactor\":0,\"roughnessFactor\":0.85},\"doubleSided\":true}");
            if (hasDirt)
            {
                float4 support = MerkabaSphereFlowerAuthority.DirtSupportLinearRgba;
                json.Append(",{\"name\":\"M8 DIRT Inferred Support\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[")
                    .Append(Number(support.x)).Append(',').Append(Number(support.y))
                    .Append(',').Append(Number(support.z)).Append(',')
                    .Append(Number(support.w)).Append("],\"metallicFactor\":0,\"roughnessFactor\":1},\"doubleSided\":false,")
                    .Append("\"extras\":{\"m8Provenance\":\"DIRT\",\"capturedRadiance\":false,\"opticalValid\":false}}");
            }
            json.Append("],");
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
            int coveredIndices = 0;
            for (int primitive = 0; primitive < ranges.Count; primitive++)
            {
                PrimitiveRange range = ranges[primitive];
                if (range.FirstIndex != coveredIndices || range.IndexCount <= 0 ||
                    range.IndexCount % 3 != 0)
                    throw new InvalidDataException("GLB primitive ranges are not a contiguous triangle partition.");
                coveredIndices = checked(coveredIndices + range.IndexCount);
                if (primitive != 0) json.Append(',');
                json.Append("{\"bufferView\":3,\"byteOffset\":")
                    .Append(checked((long)range.FirstIndex * 4L))
                    .Append(",\"componentType\":5125,\"count\":")
                    .Append(range.IndexCount).Append(",\"type\":\"SCALAR\"}");
            }
            if (coveredIndices != indexCount)
                throw new InvalidDataException("GLB primitive ranges do not cover the index buffer.");
            json.Append("]}");
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
