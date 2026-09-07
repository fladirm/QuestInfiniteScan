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
            internal readonly bool Completed;
            internal readonly int Texture;

            internal PrimitiveRange(int firstIndex, int indexCount, bool dirt,
                bool completed = false, int texture = -1)
            {
                FirstIndex = firstIndex;
                IndexCount = indexCount;
                Dirt = dirt;
                Completed = completed;
                Texture = texture;
            }
        }

        private readonly struct ImageRange
        {
            internal readonly long Offset;
            internal readonly int Length;
            internal ImageRange(long offset, int length) { Offset = offset; Length = length; }
        }

        private readonly struct BakedMaterial
        {
            internal readonly int ColorImage, NormalImage;
            internal readonly MerkabaFlowerSkinDrawSample Source;
            internal BakedMaterial(int colorImage, int normalImage, MerkabaFlowerSkinDrawSample source)
            { ColorImage = colorImage; NormalImage = normalImage; Source = source; }
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
            internal readonly float2 Uv;

            internal GeometryVertex(float3 position, float3 normal,
                uint packedColor, float2 uv = default)
            {
                Position = position;
                Normal = normal;
                PackedColor = packedColor;
                Uv = uv;
            }
        }

        /// <summary>
        /// Bounded-memory GLB assembly. Each spatial Flower batch is indexed
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
            private readonly FileStream _uvs;
            private readonly FileStream _images;
            private readonly BinaryWriter _positionWriter;
            private readonly BinaryWriter _normalWriter;
            private readonly BinaryWriter _colorWriter;
            private readonly BinaryWriter _indexWriter;
            private readonly BinaryWriter _uvWriter;
            private int _vertexCount;
            private int _indexCount;
            private int _primitiveCount;
            private readonly List<PrimitiveRange> _ranges = new();
            private readonly List<ImageRange> _imageRanges = new();
            private readonly List<BakedMaterial> _materials = new();
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
                _uvs = Open("uv.bin");
                _images = Open("images.bin");
                var encoding = new UTF8Encoding(false);
                _positionWriter = new BinaryWriter(_positions, encoding, true);
                _normalWriter = new BinaryWriter(_normals, encoding, true);
                _colorWriter = new BinaryWriter(_colors, encoding, true);
                _indexWriter = new BinaryWriter(_indices, encoding, true);
                _uvWriter = new BinaryWriter(_uvs, encoding, true);
            }

            internal void AppendDirt(IReadOnlyList<MerkabaDirtTriangle> triangles,
                IProgress<OperationWorkProgress> progress = null)
                => AppendPlan(PlanDirt(triangles, float3.zero, progress), true);

            internal void Append(MerkabaFlowerPresentation presentation,
                IProgress<OperationWorkProgress> progress = null, float3 localOrigin = default)
            {
                if (presentation == null) throw new ArgumentNullException(nameof(presentation));
                foreach (MerkabaFlowerPresentation.Carrier carrier in presentation.Carriers)
                {
                    bool bake = MerkabaFlowerMaterialBake.HasChromaticDetail(carrier);
                    bool metric = MerkabaFlowerMaterialBake.HasMetricDetail(carrier);
                    bool optical = (carrier.SkinSamples[0].Flags & 1u) != 0u;
                    for (int wedge = 0; wedge < 6; wedge++)
                    {
                        if ((carrier.Symbol.ActiveWedgeMask & (1u << wedge)) == 0u) continue;
                        int texture = -1;
                        if (bake || metric || optical)
                        {
                            byte[] colorPng = null, normalPng = null;
                            if (bake || metric)
                                MerkabaFlowerMaterialBake.Bake(presentation, carrier, wedge, bake, metric,
                                    out colorPng, out normalPng);
                            texture = _materials.Count;
                            _materials.Add(new BakedMaterial(StoreImage(colorPng), StoreImage(normalPng), carrier.SkinSamples[0]));
                        }
                        presentation.WedgeFrame(carrier, wedge, out _, out _, out float3 normal, out _, out _);
                        bool reverse = (carrier.Symbol.ReverseWedgeMask & (1u << wedge)) != 0u;
                        if (reverse) normal = -normal;
                        float3 color = bake ? new float3(1f) : carrier.SkinSamples[0].CapturedRgb;
                        uint packed = MerkabaFlowerMaterialBake.LinearByte(color.x) |
                            ((uint)MerkabaFlowerMaterialBake.LinearByte(color.y) << 8) |
                            ((uint)MerkabaFlowerMaterialBake.LinearByte(color.z) << 16) | 0xff000000u;
                        float edge = 0.5f / MerkabaFlowerMaterialBake.Resolution;
                        var vertices = new List<GeometryVertex>(3)
                        {
                            new(presentation.Position(carrier, 0) - localOrigin, normal, packed, new float2(edge, edge)),
                            new(presentation.Position(carrier, 1 + wedge) - localOrigin, normal, packed, new float2(1f - edge, edge)),
                            new(presentation.Position(carrier, 1 + (wedge + 1) % 6) - localOrigin, normal, packed, new float2(edge, 1f - edge))
                        };
                        // glTF has a single index for the attribute tuple. It
                        // may materialize a corner more than once for chart/
                        // normal attributes; every position came from the ONE
                        // symbolic position index, never a float weld.
                        var indices = reverse ? new List<uint> { 0u, 1u, 2u } : new List<uint> { 0u, 2u, 1u };
                        Vector3 lo = Convert(vertices[0].Position), hi = lo;
                        for (int vertex = 1; vertex < 3; vertex++)
                        { lo = Vector3.Min(lo, Convert(vertices[vertex].Position)); hi = Vector3.Max(hi, Convert(vertices[vertex].Position)); }
                        AppendPlan(new GeometryPlan(vertices, indices, 1, lo, hi), false,
                            (carrier.Symbol.CompletedWedgeMask & (1u << wedge)) != 0u, texture);
                    }
                }
                Report(progress, ScanOperationStage.BuildingMerkabaGeometry, presentation.TriangleCount,
                    presentation.TriangleCount, "Materialized shared L2 Flower positions and captured-radiance materials");
            }

            private int StoreImage(byte[] png)
            {
                if (png == null) return -1;
                int index = _imageRanges.Count;
                _imageRanges.Add(new ImageRange(_images.Position, png.Length));
                _images.Write(png, 0, png.Length);
                while ((_images.Position & 3L) != 0L) _images.WriteByte(0);
                return index;
            }

            private void AppendPlan(GeometryPlan plan, bool dirt, bool completed = false, int texture = -1)
            {
                ThrowIfClosed();
                if (_completed)
                    throw new InvalidOperationException(
                        "The bounded GLB stream is already complete.");
                int baseVertex = _vertexCount;
                AddRange(_ranges, _indexCount, plan.IndexCount, dirt, completed, texture);
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
                    _uvWriter.Write(vertex.Uv.x);
                    _uvWriter.Write(vertex.Uv.y);
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
                    throw new InvalidDataException("GLB Flower geometry is empty.");
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
                long uvsOffset = checked(indicesOffset + indicesLength);
                long uvsLength = _uvs.Length;
                long imagesOffset = checked(uvsOffset + uvsLength);
                long binaryLength = checked(imagesOffset + _images.Length);
                if (binaryLength > uint.MaxValue)
                    throw new InvalidDataException(
                        "GLB binary chunk exceeds the 4 GiB container limit; " +
                        "use the 3D Tiles export for a larger scan.");

                string json = BuildJson(_vertexCount, _indexCount,
                    binaryLength, positionsOffset, positionsLength,
                    normalsOffset, normalsLength, colorsOffset, colorsLength,
                    indicesOffset, indicesLength, _minimum, _maximum, _ranges,
                    uvsOffset, uvsLength, imagesOffset, _imageRanges, _materials);
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
                Copy(_uvs, destination);
                completed += uvsLength;
                Copy(_images, destination);
                completed += _images.Length;
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
                Report(progress, ScanOperationStage.WritingFile, totalLength, totalLength, "GLB bytes written");
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
                _uvWriter.Dispose();
                _positions.Dispose();
                _normals.Dispose();
                _colors.Dispose();
                _indices.Dispose();
                _uvs.Dispose();
                _images.Dispose();
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
                _uvWriter.Flush();
                _positions.Flush();
                _normals.Flush();
                _colors.Flush();
                _indices.Flush();
                _uvs.Flush();
                _images.Flush();
                if (_positions.Length != checked((long)_vertexCount * 12L) ||
                    _normals.Length != checked((long)_vertexCount * 12L) ||
                    _colors.Length != checked((long)_vertexCount * 4L) ||
                    _indices.Length != checked((long)_indexCount * 4L) ||
                    _uvs.Length != checked((long)_vertexCount * 8L))
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
            MerkabaFlowerPresentation presentation, float3 localOrigin,
            IProgress<OperationWorkProgress> progress = null)
        {
            string spool = Path.Combine(Path.GetTempPath(), "m8-flower-glb-" + Guid.NewGuid().ToString("N"));
            using var session = new StreamingSession(spool);
            session.Append(presentation, progress, localOrigin);
            return session.Complete(destination, progress);
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
                throw new InvalidDataException("GLB Flower geometry is empty.");
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
            int count, bool dirt, bool completed = false, int texture = -1)
        {
            if (count == 0) return;
            if (ranges.Count != 0)
            {
                PrimitiveRange previous = ranges[ranges.Count - 1];
                if (previous.Dirt == dirt && previous.Completed == completed && previous.Texture == texture &&
                    checked(previous.FirstIndex + previous.IndexCount) == first)
                {
                    ranges[ranges.Count - 1] = new PrimitiveRange(
                        previous.FirstIndex, checked(previous.IndexCount + count),
                        dirt, completed, texture);
                    return;
                }
            }
            ranges.Add(new PrimitiveRange(first, count, dirt, completed, texture));
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
            IReadOnlyList<PrimitiveRange> ranges, long uvsOffset = 0,
            long uvsLength = 0, long imagesOffset = 0,
            IReadOnlyList<ImageRange> images = null, IReadOnlyList<BakedMaterial> materials = null)
        {
            string min = $"[{Number(minimum.x)},{Number(minimum.y)},{Number(minimum.z)}]";
            string max = $"[{Number(maximum.x)},{Number(maximum.y)},{Number(maximum.z)}]";
            bool hasDirt = false;
            foreach (PrimitiveRange range in ranges) hasDirt |= range.Dirt;
            int imageCount = images?.Count ?? 0;
            int materialCount = materials?.Count ?? 0;
            int textureMaterial = hasDirt ? 2 : 1;
            int uvAccessor = 3 + ranges.Count;
            var json = new StringBuilder(1400);
            json.Append("{\"asset\":{\"version\":\"2.0\",\"generator\":\"Quest Infinite Merkaba\"},");
            json.Append("\"extensionsUsed\":[\"KHR_materials_unlit\"],");
            json.Append("\"scene\":0,\"scenes\":[{\"nodes\":[0]}],");
            json.Append("\"nodes\":[{\"name\":\"MerkabaGrid\",\"mesh\":0}],");
            json.Append("\"meshes\":[{\"name\":\"M8 Presentation\",\"primitives\":[");
            for (int primitive = 0; primitive < ranges.Count; primitive++)
            {
                if (primitive != 0) json.Append(',');
                PrimitiveRange range = ranges[primitive];
                json.Append("{\"attributes\":{\"POSITION\":0,\"NORMAL\":1,\"COLOR_0\":2");
                if (range.Texture >= 0)
                {
                    if (range.Texture >= materialCount || uvsLength == 0)
                        throw new InvalidDataException("A Flower material references no completed texture bake.");
                    json.Append(",\"TEXCOORD_0\":").Append(uvAccessor);
                }
                json.Append("},");
                json.Append("\"indices\":").Append(3 + primitive)
                    .Append(",\"material\":").Append(range.Dirt ? 1 : range.Texture >= 0 ? textureMaterial + range.Texture : 0)
                    .Append(",\"mode\":4");
                if (range.Dirt)
                    json.Append(",\"extras\":{\"m8Provenance\":\"DIRT\",\"inferredSupport\":true,\"capturedRadiance\":false}");
                else
                    json.Append(",\"extras\":{\"m8Provenance\":\"")
                        .Append(range.Completed ? "COMPLETED" : "CONFIRMED")
                        .Append("\",\"capturedRadiance\":true}");
                json.Append('}');
            }
            json.Append("]}],");
            json.Append("\"materials\":[{\"name\":\"M8 Captured Radiance\",");
            json.Append("\"pbrMetallicRoughness\":{\"baseColorFactor\":[1,1,1,1],");
            json.Append("\"metallicFactor\":0,\"roughnessFactor\":1},\"doubleSided\":true,\"extensions\":{\"KHR_materials_unlit\":{}}}");
            if (hasDirt)
            {
                float4 support = MerkabaSphereFlowerAuthority.DirtSupportLinearRgba;
                json.Append(",{\"name\":\"M8 DIRT Inferred Support\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[")
                    .Append(Number(support.x)).Append(',').Append(Number(support.y))
                    .Append(',').Append(Number(support.z)).Append(',')
                    .Append(Number(support.w)).Append("],\"metallicFactor\":0,\"roughnessFactor\":1},\"doubleSided\":false,")
                    .Append("\"extras\":{\"m8Provenance\":\"DIRT\",\"capturedRadiance\":false,\"opticalValid\":false}}");
            }
            for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
            {
                BakedMaterial material = materials[materialIndex];
                bool optical = (material.Source.Flags & 1u) != 0u;
                json.Append(",{\"name\":\"M8 Fixed Flower Thread\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[1,1,1,1]");
                if (material.ColorImage >= 0)
                    json.Append(",\"baseColorTexture\":{\"index\":").Append(material.ColorImage).Append('}');
                json.Append(",\"metallicFactor\":0,\"roughnessFactor\":")
                    .Append(Number(optical ? math.clamp(material.Source.Optical.w, 0f, 1f) : 1f)).Append('}');
                if (material.NormalImage >= 0)
                    json.Append(",\"normalTexture\":{\"index\":").Append(material.NormalImage).Append('}');
                json.Append(",\"doubleSided\":true,\"extensions\":{\"KHR_materials_unlit\":{}},\"extras\":{\"capturedRadiance\":true,\"flowerDescents\":3,\"bakeWidth\":")
                    .Append(MerkabaFlowerMaterialBake.Resolution)
                    .Append(",\"normalBakeAvailable\":").Append(material.NormalImage >= 0 ? "true" : "false")
                    .Append(",\"opticalValid\":").Append(optical ? "true" : "false");
                if (optical)
                {
                    json.Append(",\"certifiedOpticalMidpoint\":"); Vector4Json(json, material.Source.Optical);
                    json.Append(",\"certifiedCaptureViewMidpoint\":"); Vector4Json(json, material.Source.CaptureView);
                }
                // Standard unlit viewers preserve captured RGB. The baked
                // metric normal and certified optical metadata remain available
                // to presentation renderers; they never relight it as albedo.
                json.Append("}}");
            }
            json.Append("],");
            if (imageCount > 0)
            {
                json.Append("\"samplers\":[{\"magFilter\":9729,\"minFilter\":9729,\"wrapS\":33071,\"wrapT\":33071}],\"textures\":[");
                for (int image = 0; image < imageCount; image++)
                {
                    if (image != 0) json.Append(',');
                    json.Append("{\"sampler\":0,\"source\":").Append(image).Append('}');
                }
                json.Append("],\"images\":[");
                for (int image = 0; image < imageCount; image++)
                {
                    if (image != 0) json.Append(',');
                    json.Append("{\"bufferView\":").Append(5 + image).Append(",\"mimeType\":\"image/png\"}");
                }
                json.Append("],");
            }
            json.Append("\"buffers\":[{\"byteLength\":").Append(binaryLength).Append("}],");
            json.Append("\"bufferViews\":[");
            BufferView(json, positionsOffset, positionsLength, 34962); json.Append(',');
            BufferView(json, normalsOffset, normalsLength, 34962); json.Append(',');
            BufferView(json, colorsOffset, colorsLength, 34962); json.Append(',');
            BufferView(json, indicesOffset, indicesLength, 34963);
            if (uvsLength != 0)
            { json.Append(','); BufferView(json, uvsOffset, uvsLength, 34962); }
            for (int image = 0; image < imageCount; image++)
            {
                json.Append(',');
                BufferView(json, checked(imagesOffset + images[image].Offset), images[image].Length, 0);
            }
            json.Append("],");
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
            if (uvsLength != 0)
                json.Append(",{\"bufferView\":4,\"componentType\":5126,\"count\":")
                    .Append(vertexCount).Append(",\"type\":\"VEC2\"}");
            json.Append("]}");
            return json.ToString();
        }

        private static void BufferView(StringBuilder json, long offset,
            long length, int target)
        {
            json.Append("{\"buffer\":0,\"byteOffset\":").Append(offset)
                .Append(",\"byteLength\":").Append(length);
            if (target != 0) json.Append(",\"target\":").Append(target);
            json.Append('}');
        }

        private static string Number(float value) =>
            value.ToString("R", CultureInfo.InvariantCulture);
        private static void Vector4Json(StringBuilder json, float4 value) =>
            json.Append('[').Append(Number(value.x)).Append(',').Append(Number(value.y)).Append(',')
                .Append(Number(value.z)).Append(',').Append(Number(value.w)).Append(']');
        private static int Align4(int value) => (value + 3) & ~3;
    }
}
