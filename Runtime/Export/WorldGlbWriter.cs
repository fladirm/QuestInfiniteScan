using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Genesis.RoomScan.World;
using UnityEngine;

namespace Genesis.RoomScan.Exporting
{
    /// <summary>
    /// One already validated local chunk GLB plus the pose-graph transform that places it
    /// in the world. Geometry in <see cref="GlbPath"/> remains chunk-local; the transform
    /// is emitted once on the corresponding glTF node.
    /// </summary>
    public sealed class WorldGlbChunkInput
    {
        public string ChunkId { get; set; }
        public int Revision { get; set; }
        public RigidPoseData WorldFromChunk { get; set; }
        public string GlbPath { get; set; }
        public ChunkGlbWriteResult ChunkLayout { get; set; }
    }

    public sealed class WorldGlbWriteOptions
    {
        /// <summary>
        /// Explicit output bound. The default is the GLB uint32 container ceiling, not an
        /// artificial Quest RAM cap: BIN sections are copied with a fixed-size buffer.
        /// </summary>
        public long MaximumByteLength { get; set; } = uint.MaxValue;
        public ChunkGlbWriteOptions Material { get; set; } = new();
    }

    public sealed class WorldGlbWriteResult
    {
        public long ByteLength { get; internal set; }
        public int JsonChunkLength { get; internal set; }
        public long BinaryChunkLength { get; internal set; }
        public int ChunkCount { get; internal set; }
        public long PeakCopyBufferBytes { get; internal set; }
    }

    /// <summary>
    /// Creates a standards-compliant multi-node GLB by concatenating the BIN sections of
    /// deterministic chunk GLBs. Meshes and textures are never decoded together, so world
    /// size affects output bytes rather than resident geometry memory.
    /// </summary>
    public static class WorldGlbWriter
    {
        private const uint GlbMagic = 0x46546C67;
        private const uint JsonChunkType = 0x4E4F534A;
        private const uint BinaryChunkType = 0x004E4942;
        private const int CopyBufferBytes = 1024 * 1024;

        private sealed class SourceLayout
        {
            internal WorldGlbChunkInput Input;
            internal long BinarySourceOffset;
            internal long BinaryDestinationOffset;
        }

        public static bool TryWrite(Stream destination,
            IReadOnlyList<WorldGlbChunkInput> chunks, WorldGlbWriteOptions options,
            out WorldGlbWriteResult result, out string error,
            CancellationToken cancellationToken = default)
        {
            result = null;
            options ??= new WorldGlbWriteOptions();
            error = Validate(destination, chunks, options, out List<SourceLayout> layouts,
                out long binaryLength);
            if (error != null)
                return false;

            byte[] jsonBytes;
            int paddedJsonLength;
            long totalLength;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                jsonBytes = Encoding.UTF8.GetBytes(BuildJson(layouts, options.Material,
                    binaryLength));
                paddedJsonLength = Align4(jsonBytes.Length);
                totalLength = checked(12L + 8L + paddedJsonLength + 8L + binaryLength);
            }
            catch (OperationCanceledException)
            {
                error = "World GLB export was canceled.";
                return false;
            }
            catch (Exception exception) when (exception is OverflowException ||
                                              exception is OutOfMemoryException)
            {
                error = "World GLB layout failed: " + exception.Message;
                return false;
            }

            if (totalLength > uint.MaxValue || totalLength > options.MaximumByteLength)
            {
                error = $"Monolithic world GLB requires {totalLength} bytes, exceeding " +
                        $"the configured {options.MaximumByteLength}-byte bound; use the " +
                        "already generated sharded building.json + chunks/*.glb export.";
                return false;
            }

            long start = destination.CanSeek ? destination.Position : 0L;
            try
            {
                using var writer = new BinaryWriter(destination, new UTF8Encoding(false), true);
                writer.Write(GlbMagic);
                writer.Write(2u);
                writer.Write((uint)totalLength);
                writer.Write((uint)paddedJsonLength);
                writer.Write(JsonChunkType);
                writer.Write(jsonBytes);
                for (int i = jsonBytes.Length; i < paddedJsonLength; i++)
                    writer.Write((byte)0x20);
                writer.Write((uint)binaryLength);
                writer.Write(BinaryChunkType);
                writer.Flush();

                var copyBuffer = new byte[CopyBufferBytes];
                long writtenBinary = 0;
                for (int i = 0; i < layouts.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SourceLayout sourceLayout = layouts[i];
                    while (writtenBinary < sourceLayout.BinaryDestinationOffset)
                    {
                        destination.WriteByte(0);
                        writtenBinary++;
                    }
                    using var source = new FileStream(sourceLayout.Input.GlbPath,
                        FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes,
                        FileOptions.SequentialScan);
                    source.Position = sourceLayout.BinarySourceOffset;
                    long remaining = sourceLayout.Input.ChunkLayout.BinaryChunkLength;
                    while (remaining > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int requested = (int)Math.Min(copyBuffer.Length, remaining);
                        int read = source.Read(copyBuffer, 0, requested);
                        if (read <= 0)
                            throw new EndOfStreamException(
                                $"Chunk '{sourceLayout.Input.ChunkId}' GLB BIN is truncated.");
                        destination.Write(copyBuffer, 0, read);
                        remaining -= read;
                        writtenBinary += read;
                    }
                }
                while (writtenBinary < binaryLength)
                {
                    destination.WriteByte(0);
                    writtenBinary++;
                }
                destination.Flush();

                long actualLength = destination.CanSeek
                    ? destination.Position - start
                    : totalLength;
                if (actualLength != totalLength || writtenBinary != binaryLength)
                    throw new InvalidDataException(
                        $"World GLB length mismatch: expected {totalLength}, wrote " +
                        $"{actualLength}.");

                result = new WorldGlbWriteResult
                {
                    ByteLength = actualLength,
                    JsonChunkLength = paddedJsonLength,
                    BinaryChunkLength = binaryLength,
                    ChunkCount = layouts.Count,
                    PeakCopyBufferBytes = CopyBufferBytes
                };
                return true;
            }
            catch (OperationCanceledException)
            {
                error = "World GLB export was canceled.";
                return false;
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is InvalidDataException ||
                                              exception is OverflowException ||
                                              exception is UnauthorizedAccessException ||
                                              exception is ObjectDisposedException ||
                                              exception is NotSupportedException)
            {
                error = "World GLB write failed: " + exception.Message;
                return false;
            }
        }

        private static string Validate(Stream destination,
            IReadOnlyList<WorldGlbChunkInput> chunks, WorldGlbWriteOptions options,
            out List<SourceLayout> layouts, out long binaryLength)
        {
            layouts = null;
            binaryLength = 0;
            if (destination == null || !destination.CanWrite)
                return "World GLB destination is not writable.";
            if (chunks == null || chunks.Count <= 0 ||
                chunks.Count > WorldSchema.MaximumChunks)
                return "World GLB requires a bounded non-empty chunk list.";
            if (options.MaximumByteLength <= 0 ||
                options.MaximumByteLength > uint.MaxValue)
                return "World GLB maximum byte length must be in (0, 2^32-1].";
            ChunkGlbWriteOptions material = options.Material ?? new ChunkGlbWriteOptions();
            options.Material = material;
            if (!IsFinite(material.RoughnessFactor) || material.RoughnessFactor < 0f ||
                material.RoughnessFactor > 1f || !IsFinite(material.NormalScale) ||
                material.NormalScale < 0f || material.NormalScale > 16f)
                return "World GLB material options are invalid.";

            var ids = new HashSet<string>(StringComparer.Ordinal);
            layouts = new List<SourceLayout>(chunks.Count);
            try
            {
                long offset = 0;
                for (int i = 0; i < chunks.Count; i++)
                {
                    WorldGlbChunkInput input = chunks[i];
                    if (input == null || string.IsNullOrWhiteSpace(input.ChunkId) ||
                        input.ChunkId.Length > 64 || !ids.Add(input.ChunkId) ||
                        input.Revision < 0 || input.ChunkLayout == null ||
                        string.IsNullOrEmpty(input.GlbPath) || !File.Exists(input.GlbPath))
                        return $"World GLB chunk input {i} is invalid or duplicated.";
                    if (!IsFinite(input.WorldFromChunk.position) ||
                        !IsFinite(input.WorldFromChunk.rotation) ||
                        QuaternionLengthSquared(input.WorldFromChunk.rotation) <= 1e-12f)
                        return $"World GLB chunk '{input.ChunkId}' pose is invalid.";
                    if (!TryValidateChunkGlb(input, out long sourceBinaryOffset,
                            out string sourceError))
                        return sourceError;
                    offset = Align4(offset);
                    layouts.Add(new SourceLayout
                    {
                        Input = input,
                        BinarySourceOffset = sourceBinaryOffset,
                        BinaryDestinationOffset = offset
                    });
                    offset = checked(offset + input.ChunkLayout.BinaryChunkLength);
                }
                binaryLength = Align4(offset);
                if (binaryLength > uint.MaxValue)
                    return "World GLB BIN exceeds the GLB uint32 limit; use sharded export.";
                return null;
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is OverflowException ||
                                              exception is UnauthorizedAccessException)
            {
                return "World GLB source validation failed: " + exception.Message;
            }
        }

        private static bool TryValidateChunkGlb(WorldGlbChunkInput input,
            out long binarySourceOffset, out string error)
        {
            binarySourceOffset = 0;
            error = null;
            ChunkGlbWriteResult receipt = input.ChunkLayout;
            var info = new FileInfo(input.GlbPath);
            if (receipt.ByteLength <= 0 || receipt.ByteLength != info.Length ||
                receipt.JsonChunkLength <= 0 || receipt.BinaryChunkLength <= 0 ||
                receipt.BinaryChunkLength > uint.MaxValue ||
                receipt.VertexCount <= 0 || receipt.IndexCount <= 0 ||
                receipt.IndexCount % 3 != 0)
            {
                error = $"Chunk '{input.ChunkId}' GLB receipt is inconsistent.";
                return false;
            }
            using var stream = new FileStream(input.GlbPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.SequentialScan);
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            if (reader.ReadUInt32() != GlbMagic || reader.ReadUInt32() != 2u ||
                reader.ReadUInt32() != (uint)receipt.ByteLength ||
                reader.ReadUInt32() != (uint)receipt.JsonChunkLength ||
                reader.ReadUInt32() != JsonChunkType)
            {
                error = $"Chunk '{input.ChunkId}' GLB header does not match its receipt.";
                return false;
            }
            binarySourceOffset = checked(20L + receipt.JsonChunkLength + 8L);
            stream.Position = 20L + receipt.JsonChunkLength;
            if (reader.ReadUInt32() != (uint)receipt.BinaryChunkLength ||
                reader.ReadUInt32() != BinaryChunkType ||
                binarySourceOffset + receipt.BinaryChunkLength != receipt.ByteLength)
            {
                error = $"Chunk '{input.ChunkId}' GLB BIN header is inconsistent.";
                return false;
            }
            if (!ReceiptRangeIsValid(receipt.PositionsOffset,
                    receipt.PositionsByteLength, receipt.BinaryChunkLength) ||
                !ReceiptRangeIsValid(receipt.NormalsOffset,
                    receipt.NormalsByteLength, receipt.BinaryChunkLength) ||
                !ReceiptRangeIsValid(receipt.TangentsOffset,
                    receipt.TangentsByteLength, receipt.BinaryChunkLength) ||
                !ReceiptRangeIsValid(receipt.TexCoordsOffset,
                    receipt.TexCoordsByteLength, receipt.BinaryChunkLength) ||
                !ReceiptRangeIsValid(receipt.IndicesOffset,
                    receipt.IndicesByteLength, receipt.BinaryChunkLength) ||
                !ReceiptRangeIsValid(receipt.BaseColorPngOffset,
                    receipt.BaseColorPngByteLength, receipt.BinaryChunkLength) ||
                !ReceiptRangeIsValid(receipt.NormalPngOffset,
                    receipt.NormalPngByteLength, receipt.BinaryChunkLength))
            {
                error = $"Chunk '{input.ChunkId}' GLB buffer layout is out of range.";
                return false;
            }
            return true;
        }

        private static bool ReceiptRangeIsValid(long offset, long length, long total) =>
            offset >= 0 && length > 0 && offset <= total && length <= total - offset;

        private static string BuildJson(IReadOnlyList<SourceLayout> layouts,
            ChunkGlbWriteOptions material, long binaryLength)
        {
            var json = new StringBuilder(Math.Max(4096, layouts.Count * 1700));
            json.Append('{');
            json.Append("\"asset\":{\"version\":\"2.0\",\"generator\":" +
                        "\"QuestInfiniteScan deterministic world GLB v1\"},");
            json.Append("\"scene\":0,\"scenes\":[{\"name\":\"QuestInfiniteScan World\"," +
                        "\"nodes\":[");
            for (int i = 0; i < layouts.Count; i++)
            {
                if (i > 0) json.Append(',');
                json.Append(i);
            }
            json.Append("]}],\"nodes\":[");
            for (int i = 0; i < layouts.Count; i++)
            {
                if (i > 0) json.Append(',');
                WorldGlbChunkInput input = layouts[i].Input;
                json.Append("{\"name\":\"")
                    .Append(ChunkGlbWriter.EscapeJson(ChunkName(input)))
                    .Append("\",\"mesh\":").Append(i).Append(",\"matrix\":[");
                AppendMatrix(json, ToGltfMatrix(input.WorldFromChunk));
                json.Append("]}");
            }
            json.Append("],\"meshes\":[");
            for (int i = 0; i < layouts.Count; i++)
            {
                if (i > 0) json.Append(',');
                json.Append("{\"name\":\"")
                    .Append(ChunkGlbWriter.EscapeJson(ChunkName(layouts[i].Input)))
                    .Append("\",\"primitives\":[{\"attributes\":{")
                    .Append("\"POSITION\":").Append(i * 5)
                    .Append(",\"NORMAL\":").Append(i * 5 + 1)
                    .Append(",\"TANGENT\":").Append(i * 5 + 2)
                    .Append(",\"TEXCOORD_0\":").Append(i * 5 + 3)
                    .Append("},\"indices\":").Append(i * 5 + 4)
                    .Append(",\"material\":").Append(i)
                    .Append(",\"mode\":4}]}");
            }
            json.Append("],\"materials\":[");
            for (int i = 0; i < layouts.Count; i++)
            {
                if (i > 0) json.Append(',');
                json.Append("{\"name\":\"")
                    .Append(ChunkGlbWriter.EscapeJson(ChunkName(layouts[i].Input)))
                    .Append(" PBR\",\"pbrMetallicRoughness\":{")
                    .Append("\"baseColorFactor\":[1,1,1,1],\"baseColorTexture\":{")
                    .Append("\"index\":").Append(i * 2).Append("},")
                    .Append("\"metallicFactor\":0,\"roughnessFactor\":")
                    .Append(ChunkGlbWriter.JsonFloat(material.RoughnessFactor)).Append("},")
                    .Append("\"normalTexture\":{\"index\":").Append(i * 2 + 1)
                    .Append(",\"scale\":")
                    .Append(ChunkGlbWriter.JsonFloat(material.NormalScale)).Append("},")
                    .Append("\"alphaMode\":\"OPAQUE\",\"doubleSided\":")
                    .Append(material.DoubleSided ? "true" : "false").Append('}');
            }
            json.Append("],\"textures\":[");
            for (int i = 0; i < layouts.Count * 2; i++)
            {
                if (i > 0) json.Append(',');
                json.Append("{\"sampler\":0,\"source\":").Append(i).Append('}');
            }
            json.Append("],\"samplers\":[{\"magFilter\":9729,\"minFilter\":9729,")
                .Append("\"wrapS\":33071,\"wrapT\":33071}],\"images\":[");
            for (int i = 0; i < layouts.Count; i++)
            {
                if (i > 0) json.Append(',');
                string name = ChunkGlbWriter.EscapeJson(ChunkName(layouts[i].Input));
                json.Append("{\"name\":\"").Append(name)
                    .Append(" baseColor\",\"bufferView\":").Append(i * 7 + 5)
                    .Append(",\"mimeType\":\"image/png\"},{\"name\":\"")
                    .Append(name).Append(" normal\",\"bufferView\":")
                    .Append(i * 7 + 6).Append(",\"mimeType\":\"image/png\"}");
            }
            json.Append("],\"accessors\":[");
            for (int i = 0; i < layouts.Count; i++)
            {
                if (i > 0) json.Append(',');
                ChunkGlbWriteResult receipt = layouts[i].Input.ChunkLayout;
                AppendAccessor(json, i * 7, 5126, receipt.VertexCount, "VEC3",
                    receipt.PositionMinimum, receipt.PositionMaximum);
                json.Append(',');
                AppendAccessor(json, i * 7 + 1, 5126, receipt.VertexCount, "VEC3");
                json.Append(',');
                AppendAccessor(json, i * 7 + 2, 5126, receipt.VertexCount, "VEC4");
                json.Append(',');
                AppendAccessor(json, i * 7 + 3, 5126, receipt.VertexCount, "VEC2");
                json.Append(",{")
                    .Append("\"bufferView\":").Append(i * 7 + 4)
                    .Append(",\"byteOffset\":0,\"componentType\":5125,\"count\":")
                    .Append(receipt.IndexCount)
                    .Append(",\"type\":\"SCALAR\",\"min\":[0],\"max\":[")
                    .Append(receipt.MaximumIndex).Append("]}");
            }
            json.Append("],\"bufferViews\":[");
            for (int i = 0; i < layouts.Count; i++)
            {
                if (i > 0) json.Append(',');
                SourceLayout layout = layouts[i];
                ChunkGlbWriteResult receipt = layout.Input.ChunkLayout;
                AppendBufferView(json, layout.BinaryDestinationOffset +
                    receipt.PositionsOffset, receipt.PositionsByteLength, 34962);
                json.Append(',');
                AppendBufferView(json, layout.BinaryDestinationOffset +
                    receipt.NormalsOffset, receipt.NormalsByteLength, 34962);
                json.Append(',');
                AppendBufferView(json, layout.BinaryDestinationOffset +
                    receipt.TangentsOffset, receipt.TangentsByteLength, 34962);
                json.Append(',');
                AppendBufferView(json, layout.BinaryDestinationOffset +
                    receipt.TexCoordsOffset, receipt.TexCoordsByteLength, 34962);
                json.Append(',');
                AppendBufferView(json, layout.BinaryDestinationOffset +
                    receipt.IndicesOffset, receipt.IndicesByteLength, 34963);
                json.Append(',');
                AppendBufferView(json, layout.BinaryDestinationOffset +
                    receipt.BaseColorPngOffset, receipt.BaseColorPngByteLength, null);
                json.Append(',');
                AppendBufferView(json, layout.BinaryDestinationOffset +
                    receipt.NormalPngOffset, receipt.NormalPngByteLength, null);
            }
            json.Append("],\"buffers\":[{\"byteLength\":")
                .Append(binaryLength).Append("}]}");
            return json.ToString();
        }

        private static string ChunkName(WorldGlbChunkInput input) =>
            input.ChunkId + "_r" + input.Revision.ToString("D10",
                CultureInfo.InvariantCulture);

        internal static Matrix4x4 ToGltfMatrix(RigidPoseData worldFromChunk)
        {
            Matrix4x4 reflection = Matrix4x4.identity;
            reflection.m00 = -1f;
            return reflection * worldFromChunk.ToMatrix() * reflection;
        }

        private static void AppendMatrix(StringBuilder json, Matrix4x4 value)
        {
            // glTF stores matrices column-major.
            float[] elements =
            {
                value.m00, value.m10, value.m20, value.m30,
                value.m01, value.m11, value.m21, value.m31,
                value.m02, value.m12, value.m22, value.m32,
                value.m03, value.m13, value.m23, value.m33
            };
            for (int i = 0; i < elements.Length; i++)
            {
                if (i > 0) json.Append(',');
                json.Append(ChunkGlbWriter.JsonFloat(elements[i]));
            }
        }

        private static void AppendAccessor(StringBuilder json, int bufferView,
            int componentType, int count, string type, Vector3? minimum = null,
            Vector3? maximum = null)
        {
            json.Append("{\"bufferView\":").Append(bufferView)
                .Append(",\"byteOffset\":0,\"componentType\":")
                .Append(componentType).Append(",\"count\":").Append(count)
                .Append(",\"type\":\"").Append(type).Append('"');
            if (minimum.HasValue && maximum.HasValue)
            {
                json.Append(",\"min\":[")
                    .Append(ChunkGlbWriter.JsonFloat(minimum.Value.x)).Append(',')
                    .Append(ChunkGlbWriter.JsonFloat(minimum.Value.y)).Append(',')
                    .Append(ChunkGlbWriter.JsonFloat(minimum.Value.z)).Append("],\"max\":[")
                    .Append(ChunkGlbWriter.JsonFloat(maximum.Value.x)).Append(',')
                    .Append(ChunkGlbWriter.JsonFloat(maximum.Value.y)).Append(',')
                    .Append(ChunkGlbWriter.JsonFloat(maximum.Value.z)).Append(']');
            }
            json.Append('}');
        }

        private static void AppendBufferView(StringBuilder json, long offset,
            long length, int? target)
        {
            json.Append("{\"buffer\":0,\"byteOffset\":").Append(offset)
                .Append(",\"byteLength\":").Append(length);
            if (target.HasValue)
                json.Append(",\"target\":").Append(target.Value);
            json.Append('}');
        }

        private static int Align4(int value) => checked((value + 3) & ~3);
        private static long Align4(long value) => checked((value + 3L) & ~3L);
        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        private static bool IsFinite(Quaternion value) => IsFinite(value.x) &&
            IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        private static float QuaternionLengthSquared(Quaternion value) =>
            value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
    }
}
