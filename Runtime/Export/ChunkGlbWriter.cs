using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Genesis.RoomScan.Exporting
{
    /// <summary>Complete chunk-local inputs for the baseline glTF 2.0 export.</summary>
    public sealed class ChunkGlbExportData
    {
        public string Name { get; set; } = "chunk";
        public Vector3[] Positions { get; set; }
        public Vector3[] Normals { get; set; }
        public Vector2[] TexCoords0 { get; set; }
        public int[] Indices { get; set; }
        public byte[] BaseColorRgba32 { get; set; }
        public byte[] NormalRgba32 { get; set; }
        public int TextureWidth { get; set; }
        public int TextureHeight { get; set; }
    }

    /// <summary>
    /// Material values that are honest for an RGB-D scan. Metallic is deliberately not
    /// configurable and is always exported as zero; roughness stays an explicit constant
    /// until a measured map exists.
    /// </summary>
    public sealed class ChunkGlbWriteOptions
    {
        public float RoughnessFactor { get; set; } = 0.8f;
        public float NormalScale { get; set; } = 1f;
        public bool DoubleSided { get; set; } = true;
    }

    public sealed class ChunkGlbWriteResult
    {
        public long ByteLength { get; internal set; }
        public int JsonChunkLength { get; internal set; }
        public long BinaryChunkLength { get; internal set; }
        public int VertexCount { get; internal set; }
        public int IndexCount { get; internal set; }

        // Small immutable layout receipt used by the bounded world writer. It lets a
        // monolithic export concatenate already-validated chunk BIN sections without
        // decoding every mesh and texture back into RAM.
        internal Vector3 PositionMinimum { get; set; }
        internal Vector3 PositionMaximum { get; set; }
        internal int MaximumIndex { get; set; }
        internal long PositionsOffset { get; set; }
        internal long PositionsByteLength { get; set; }
        internal long NormalsOffset { get; set; }
        internal long NormalsByteLength { get; set; }
        internal long TangentsOffset { get; set; }
        internal long TangentsByteLength { get; set; }
        internal long TexCoordsOffset { get; set; }
        internal long TexCoordsByteLength { get; set; }
        internal long IndicesOffset { get; set; }
        internal long IndicesByteLength { get; set; }
        internal long BaseColorPngOffset { get; set; }
        internal long BaseColorPngByteLength { get; set; }
        internal long NormalPngOffset { get; set; }
        internal long NormalPngByteLength { get; set; }
    }

    /// <summary>
    /// Deterministic, dependency-free GLB 2.0 writer for one QRS chunk. Unity's +X-right,
    /// +Y-up, +Z-forward basis is converted to glTF's +Y-up, +Z-forward, -X-right basis by
    /// mirroring X and reversing every triangle. QRS row zero is emitted as PNG row zero,
    /// matching glTF's upper-left texture-coordinate origin without changing UV values.
    /// </summary>
    public static class ChunkGlbWriter
    {
        public const int ArtifactFormatVersion = 1;

        private const uint GlbMagic = 0x46546C67; // glTF
        private const uint JsonChunkType = 0x4E4F534A; // JSON
        private const uint BinaryChunkType = 0x004E4942; // BIN\0
        private const int MaximumVertices = 8_000_000;
        private const int MaximumIndices = 24_000_000;
        private const int MaximumNameCharacters = 256;
        private const float VectorEpsilonSquared = 1e-20f;

        public static bool TryWrite(Stream destination, ChunkGlbExportData data,
            ChunkGlbWriteOptions options, out ChunkGlbWriteResult result,
            out string error, CancellationToken cancellationToken = default)
        {
            result = null;
            options ??= new ChunkGlbWriteOptions();
            error = Validate(destination, data, options, out Vector3 minimum,
                out Vector3 maximum, out int maximumIndex);
            if (error != null)
                return false;

            if (!Layout.TryCreate(data, out Layout layout, out error))
                return false;
            string json = BuildJson(data.Name, options, layout, data.Positions.Length,
                data.Indices.Length, maximumIndex, minimum, maximum);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            int paddedJsonLength = Align4(jsonBytes.Length);
            long totalLength;
            try
            {
                totalLength = checked(12L + 8L + paddedJsonLength + 8L +
                                      layout.BinaryByteLength);
            }
            catch (OverflowException)
            {
                error = "GLB total length overflowed.";
                return false;
            }
            if (totalLength > uint.MaxValue || layout.BinaryByteLength > uint.MaxValue)
            {
                error = "GLB exceeds the 4 GiB container limit; use sharded world export.";
                return false;
            }

            Vector3[] tangentS;
            Vector3[] tangentT;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                BuildTangentAccumulators(data, cancellationToken, out tangentS,
                    out tangentT);
            }
            catch (OperationCanceledException)
            {
                error = "GLB export was canceled.";
                return false;
            }
            catch (Exception exception) when (exception is OverflowException ||
                                              exception is OutOfMemoryException)
            {
                error = "GLB tangent generation failed: " + exception.Message;
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

                writer.Write((uint)layout.BinaryByteLength);
                writer.Write(BinaryChunkType);
                WritePositions(writer, data.Positions, cancellationToken);
                WriteNormals(writer, data.Normals, cancellationToken);
                WriteTangents(writer, data.Normals, tangentS, tangentT,
                    cancellationToken);
                tangentS = null;
                tangentT = null;
                WriteTexCoords(writer, data.TexCoords0, cancellationToken);
                WriteIndices(writer, data.Indices, cancellationToken);

                long binaryWritten = layout.IndicesOffset + layout.IndicesByteLength;
                PadBinary(writer, ref binaryWritten, layout.BaseColorPngOffset);
                if (!DeterministicPngWriter.TryWriteRgba8(destination,
                        data.BaseColorRgba32, data.TextureWidth, data.TextureHeight,
                        cancellationToken, out long baseBytes, out string baseError))
                    throw new InvalidDataException(baseError);
                if (baseBytes != layout.BaseColorPngByteLength)
                    throw new InvalidDataException("Base-color PNG length changed after layout.");
                binaryWritten = checked(binaryWritten + baseBytes);
                PadBinary(writer, ref binaryWritten, layout.NormalPngOffset);
                if (!DeterministicPngWriter.TryWriteRgba8(destination,
                        data.NormalRgba32, data.TextureWidth, data.TextureHeight,
                        cancellationToken, out long normalBytes, out string normalError))
                    throw new InvalidDataException(normalError);
                if (normalBytes != layout.NormalPngByteLength)
                    throw new InvalidDataException("Normal PNG length changed after layout.");
                binaryWritten = checked(binaryWritten + normalBytes);
                PadBinary(writer, ref binaryWritten, layout.BinaryByteLength);
                writer.Flush();

                long actualLength = destination.CanSeek
                    ? destination.Position - start
                    : totalLength;
                if (binaryWritten != layout.BinaryByteLength || actualLength != totalLength)
                    throw new InvalidDataException(
                        $"GLB length mismatch: expected {totalLength}, wrote {actualLength}.");

                result = new ChunkGlbWriteResult
                {
                    ByteLength = actualLength,
                    JsonChunkLength = paddedJsonLength,
                    BinaryChunkLength = layout.BinaryByteLength,
                    VertexCount = data.Positions.Length,
                    IndexCount = data.Indices.Length,
                    PositionMinimum = minimum,
                    PositionMaximum = maximum,
                    MaximumIndex = maximumIndex,
                    PositionsOffset = layout.PositionsOffset,
                    PositionsByteLength = layout.PositionsByteLength,
                    NormalsOffset = layout.NormalsOffset,
                    NormalsByteLength = layout.NormalsByteLength,
                    TangentsOffset = layout.TangentsOffset,
                    TangentsByteLength = layout.TangentsByteLength,
                    TexCoordsOffset = layout.TexCoordsOffset,
                    TexCoordsByteLength = layout.TexCoordsByteLength,
                    IndicesOffset = layout.IndicesOffset,
                    IndicesByteLength = layout.IndicesByteLength,
                    BaseColorPngOffset = layout.BaseColorPngOffset,
                    BaseColorPngByteLength = layout.BaseColorPngByteLength,
                    NormalPngOffset = layout.NormalPngOffset,
                    NormalPngByteLength = layout.NormalPngByteLength
                };
                return true;
            }
            catch (OperationCanceledException)
            {
                error = "GLB export was canceled.";
                return false;
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is InvalidDataException ||
                                              exception is OverflowException ||
                                              exception is ObjectDisposedException ||
                                              exception is NotSupportedException)
            {
                error = "GLB write failed: " + exception.Message;
                return false;
            }
        }

        private static string Validate(Stream destination, ChunkGlbExportData data,
            ChunkGlbWriteOptions options, out Vector3 minimum, out Vector3 maximum,
            out int maximumIndex)
        {
            minimum = default;
            maximum = default;
            maximumIndex = 0;
            if (destination == null || !destination.CanWrite)
                return "GLB destination is not writable.";
            if (data == null || data.Positions == null || data.Normals == null ||
                data.TexCoords0 == null || data.Indices == null)
                return "GLB mesh arrays are required.";
            int vertexCount = data.Positions.Length;
            if (vertexCount <= 0 || vertexCount > MaximumVertices ||
                data.Normals.Length != vertexCount ||
                data.TexCoords0.Length != vertexCount)
                return "GLB vertex arrays have invalid counts.";
            if (data.Indices.Length <= 0 || data.Indices.Length > MaximumIndices ||
                data.Indices.Length % 3 != 0)
                return "GLB index count is invalid.";
            if (data.Name != null && data.Name.Length > MaximumNameCharacters)
                return $"GLB name exceeds {MaximumNameCharacters} characters.";
            if (!IsFinite(options.RoughnessFactor) || options.RoughnessFactor < 0f ||
                options.RoughnessFactor > 1f)
                return "GLB roughnessFactor must be finite and in [0,1].";
            if (!IsFinite(options.NormalScale) || options.NormalScale < 0f ||
                options.NormalScale > 16f)
                return "GLB normal scale must be finite and in [0,16].";
            if (!TryValidateTexture(data.BaseColorRgba32, data.TextureWidth,
                    data.TextureHeight, "Base-color", out string textureError))
                return textureError;
            if (!TryValidateTexture(data.NormalRgba32, data.TextureWidth,
                    data.TextureHeight, "Normal", out textureError))
                return textureError;

            for (int i = 0; i < vertexCount; i++)
            {
                if (!IsFinite(data.Positions[i]) || !IsFinite(data.Normals[i]) ||
                    !IsFinite(data.TexCoords0[i]))
                    return "GLB mesh contains non-finite vertex data.";
                if (data.Normals[i].sqrMagnitude <= VectorEpsilonSquared)
                    return "GLB mesh contains a zero-length normal.";
                Vector3 converted = ConvertVector(data.Positions[i]);
                if (i == 0)
                {
                    minimum = converted;
                    maximum = converted;
                }
                else
                {
                    minimum = Vector3.Min(minimum, converted);
                    maximum = Vector3.Max(maximum, converted);
                }
            }
            for (int i = 0; i < data.Indices.Length; i++)
            {
                int index = data.Indices[i];
                if ((uint)index >= (uint)vertexCount)
                    return "GLB mesh contains an out-of-range index.";
                if (index > maximumIndex)
                    maximumIndex = index;
            }
            return null;
        }

        private static bool TryValidateTexture(byte[] rgba, int width, int height,
            string role, out string error)
        {
            error = null;
            if (!DeterministicPngWriter.TryGetEncodedLength(width, height, out _,
                    out string pngError))
            {
                error = role + " texture is invalid: " + pngError;
                return false;
            }
            long expected = checked((long)width * height * 4L);
            if (rgba == null || rgba.LongLength != expected)
            {
                error = role + " RGBA8 payload does not match its dimensions.";
                return false;
            }
            return true;
        }

        private static void BuildTangentAccumulators(ChunkGlbExportData data,
            CancellationToken cancellationToken, out Vector3[] tangentS,
            out Vector3[] tangentT)
        {
            tangentS = new Vector3[data.Positions.Length];
            tangentT = new Vector3[data.Positions.Length];
            for (int triangle = 0; triangle < data.Indices.Length; triangle += 3)
            {
                if ((triangle & 0xFFFF) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                int i0 = data.Indices[triangle];
                int i1 = data.Indices[triangle + 1];
                int i2 = data.Indices[triangle + 2];
                Vector3 p0 = ConvertVector(data.Positions[i0]);
                Vector3 edge1 = ConvertVector(data.Positions[i1]) - p0;
                Vector3 edge2 = ConvertVector(data.Positions[i2]) - p0;
                Vector2 uv0 = data.TexCoords0[i0];
                Vector2 duv1 = data.TexCoords0[i1] - uv0;
                Vector2 duv2 = data.TexCoords0[i2] - uv0;
                float determinant = duv1.x * duv2.y - duv1.y * duv2.x;
                if (!IsFinite(determinant) || Mathf.Abs(determinant) <= 1e-20f)
                    continue;
                float reciprocal = 1f / determinant;
                Vector3 s = (edge1 * duv2.y - edge2 * duv1.y) * reciprocal;
                Vector3 t = (edge2 * duv1.x - edge1 * duv2.x) * reciprocal;
                if (!IsFinite(s) || !IsFinite(t))
                    continue;
                tangentS[i0] += s;
                tangentS[i1] += s;
                tangentS[i2] += s;
                tangentT[i0] += t;
                tangentT[i1] += t;
                tangentT[i2] += t;
            }
        }

        private static void WritePositions(BinaryWriter writer, Vector3[] positions,
            CancellationToken cancellationToken)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                if ((i & 0xFFFF) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                Vector3 value = ConvertVector(positions[i]);
                writer.Write(value.x);
                writer.Write(value.y);
                writer.Write(value.z);
            }
        }

        private static void WriteNormals(BinaryWriter writer, Vector3[] normals,
            CancellationToken cancellationToken)
        {
            for (int i = 0; i < normals.Length; i++)
            {
                if ((i & 0xFFFF) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                Vector3 value = ConvertVector(normals[i]).normalized;
                writer.Write(value.x);
                writer.Write(value.y);
                writer.Write(value.z);
            }
        }

        private static void WriteTangents(BinaryWriter writer, Vector3[] normals,
            Vector3[] tangentS, Vector3[] tangentT,
            CancellationToken cancellationToken)
        {
            for (int i = 0; i < normals.Length; i++)
            {
                if ((i & 0xFFFF) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                Vector3 normal = ConvertVector(normals[i]).normalized;
                Vector3 tangent = tangentS[i] - normal * Vector3.Dot(normal, tangentS[i]);
                if (tangent.sqrMagnitude <= VectorEpsilonSquared || !IsFinite(tangent))
                    tangent = FallbackTangent(normal);
                else
                    tangent.Normalize();
                float handedness = tangentT[i].sqrMagnitude > VectorEpsilonSquared &&
                                   Vector3.Dot(Vector3.Cross(normal, tangent), tangentT[i]) < 0f
                    ? -1f
                    : 1f;
                writer.Write(tangent.x);
                writer.Write(tangent.y);
                writer.Write(tangent.z);
                writer.Write(handedness);
            }
        }

        private static void WriteTexCoords(BinaryWriter writer, Vector2[] texCoords,
            CancellationToken cancellationToken)
        {
            for (int i = 0; i < texCoords.Length; i++)
            {
                if ((i & 0xFFFF) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                // Do not flip V: QRS row zero becomes encoded PNG row zero.
                writer.Write(texCoords[i].x);
                writer.Write(texCoords[i].y);
            }
        }

        private static void WriteIndices(BinaryWriter writer, int[] indices,
            CancellationToken cancellationToken)
        {
            for (int i = 0; i < indices.Length; i += 3)
            {
                if ((i & 0xFFFF) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                writer.Write((uint)indices[i]);
                writer.Write((uint)indices[i + 2]);
                writer.Write((uint)indices[i + 1]);
            }
        }

        private static void PadBinary(BinaryWriter writer, ref long current,
            long target)
        {
            if (current > target)
                throw new InvalidDataException("GLB binary section exceeded its layout.");
            while (current < target)
            {
                writer.Write((byte)0);
                current++;
            }
        }

        private static Vector3 ConvertVector(Vector3 value) =>
            new(-value.x, value.y, value.z);

        private static Vector3 FallbackTangent(Vector3 normal)
        {
            Vector3 reference = Mathf.Abs(normal.y) < 0.999f
                ? Vector3.up
                : Vector3.right;
            Vector3 tangent = Vector3.Cross(reference, normal);
            return tangent.sqrMagnitude <= VectorEpsilonSquared
                ? Vector3.forward
                : tangent.normalized;
        }

        private static string BuildJson(string sourceName, ChunkGlbWriteOptions options,
            Layout layout, int vertexCount, int indexCount, int maximumIndex,
            Vector3 minimum, Vector3 maximum)
        {
            string name = string.IsNullOrWhiteSpace(sourceName) ? "chunk" : sourceName;
            string escapedName = EscapeJson(name);
            var json = new StringBuilder(2_048);
            json.Append('{');
            json.Append("\"asset\":{\"version\":\"2.0\",\"generator\":" +
                        "\"QuestInfiniteScan deterministic GLB v1\"},");
            json.Append("\"scene\":0,");
            json.Append("\"scenes\":[{\"name\":\"").Append(escapedName)
                .Append("\",\"nodes\":[0]}],");
            json.Append("\"nodes\":[{\"name\":\"").Append(escapedName)
                .Append("\",\"mesh\":0}],");
            json.Append("\"meshes\":[{\"name\":\"").Append(escapedName)
                .Append("\",\"primitives\":[{\"attributes\":{")
                .Append("\"POSITION\":0,\"NORMAL\":1,\"TANGENT\":2,\"TEXCOORD_0\":3},")
                .Append("\"indices\":4,\"material\":0,\"mode\":4}]}],");
            json.Append("\"materials\":[{\"name\":\"").Append(escapedName)
                .Append(" PBR\",\"pbrMetallicRoughness\":{")
                .Append("\"baseColorFactor\":[1,1,1,1],\"baseColorTexture\":{\"index\":0},")
                .Append("\"metallicFactor\":0,\"roughnessFactor\":")
                .Append(JsonFloat(options.RoughnessFactor)).Append("},")
                .Append("\"normalTexture\":{\"index\":1,\"scale\":")
                .Append(JsonFloat(options.NormalScale)).Append("},")
                .Append("\"alphaMode\":\"OPAQUE\",\"doubleSided\":")
                .Append(options.DoubleSided ? "true" : "false").Append("}],");
            json.Append("\"textures\":[{\"sampler\":0,\"source\":0},{\"sampler\":0,\"source\":1}],");
            json.Append("\"samplers\":[{\"magFilter\":9729,\"minFilter\":9729,")
                .Append("\"wrapS\":33071,\"wrapT\":33071}],");
            json.Append("\"images\":[{\"name\":\"").Append(escapedName)
                .Append(" baseColor\",\"bufferView\":5,\"mimeType\":\"image/png\"},")
                .Append("{\"name\":\"").Append(escapedName)
                .Append(" normal\",\"bufferView\":6,\"mimeType\":\"image/png\"}],");
            json.Append("\"accessors\":[");
            AppendAccessor(json, 0, 5126, vertexCount, "VEC3", minimum, maximum);
            json.Append(',');
            AppendAccessor(json, 1, 5126, vertexCount, "VEC3");
            json.Append(',');
            AppendAccessor(json, 2, 5126, vertexCount, "VEC4");
            json.Append(',');
            AppendAccessor(json, 3, 5126, vertexCount, "VEC2");
            json.Append(" ,{\"bufferView\":4,\"byteOffset\":0,\"componentType\":5125,")
                .Append("\"count\":").Append(indexCount)
                .Append(",\"type\":\"SCALAR\",\"min\":[0],\"max\":[")
                .Append(maximumIndex).Append("]}],");
            json.Append("\"bufferViews\":[");
            AppendBufferView(json, layout.PositionsOffset, layout.PositionsByteLength, 34962);
            json.Append(',');
            AppendBufferView(json, layout.NormalsOffset, layout.NormalsByteLength, 34962);
            json.Append(',');
            AppendBufferView(json, layout.TangentsOffset, layout.TangentsByteLength, 34962);
            json.Append(',');
            AppendBufferView(json, layout.TexCoordsOffset, layout.TexCoordsByteLength, 34962);
            json.Append(',');
            AppendBufferView(json, layout.IndicesOffset, layout.IndicesByteLength, 34963);
            json.Append(',');
            AppendBufferView(json, layout.BaseColorPngOffset,
                layout.BaseColorPngByteLength, null);
            json.Append(',');
            AppendBufferView(json, layout.NormalPngOffset,
                layout.NormalPngByteLength, null);
            json.Append("],\"buffers\":[{\"byteLength\":")
                .Append(layout.BinaryByteLength).Append("}]}");
            return json.ToString();
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
                    .Append(JsonFloat(minimum.Value.x)).Append(',')
                    .Append(JsonFloat(minimum.Value.y)).Append(',')
                    .Append(JsonFloat(minimum.Value.z)).Append("],\"max\":[")
                    .Append(JsonFloat(maximum.Value.x)).Append(',')
                    .Append(JsonFloat(maximum.Value.y)).Append(',')
                    .Append(JsonFloat(maximum.Value.z)).Append(']');
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

        internal static string EscapeJson(string value)
        {
            var escaped = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                switch (character)
                {
                    case '"': escaped.Append("\\\""); break;
                    case '\\': escaped.Append("\\\\"); break;
                    case '\b': escaped.Append("\\b"); break;
                    case '\f': escaped.Append("\\f"); break;
                    case '\n': escaped.Append("\\n"); break;
                    case '\r': escaped.Append("\\r"); break;
                    case '\t': escaped.Append("\\t"); break;
                    default:
                        if (character < 0x20 || character > 0x7E)
                            escaped.Append("\\u").Append(((int)character).ToString("x4"));
                        else
                            escaped.Append(character);
                        break;
                }
            }
            return escaped.ToString();
        }

        internal static string JsonFloat(float value)
        {
            if (value == 0f)
                return "0";
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static int Align4(int value) => checked((value + 3) & ~3);
        private static long Align4(long value) => checked((value + 3L) & ~3L);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsFinite(Vector2 value) =>
            IsFinite(value.x) && IsFinite(value.y);

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private sealed class Layout
        {
            internal long PositionsOffset;
            internal long PositionsByteLength;
            internal long NormalsOffset;
            internal long NormalsByteLength;
            internal long TangentsOffset;
            internal long TangentsByteLength;
            internal long TexCoordsOffset;
            internal long TexCoordsByteLength;
            internal long IndicesOffset;
            internal long IndicesByteLength;
            internal long BaseColorPngOffset;
            internal long BaseColorPngByteLength;
            internal long NormalPngOffset;
            internal long NormalPngByteLength;
            internal long BinaryByteLength;

            internal static bool TryCreate(ChunkGlbExportData data, out Layout layout,
                out string error)
            {
                layout = null;
                error = null;
                if (!DeterministicPngWriter.TryGetEncodedLength(data.TextureWidth,
                        data.TextureHeight, out long baseLength, out error) ||
                    !DeterministicPngWriter.TryGetEncodedLength(data.TextureWidth,
                        data.TextureHeight, out long normalLength, out error))
                    return false;
                try
                {
                    var value = new Layout();
                    value.PositionsByteLength = checked((long)data.Positions.Length * 12L);
                    value.PositionsOffset = 0;
                    value.NormalsOffset = Align4(value.PositionsOffset +
                                                 value.PositionsByteLength);
                    value.NormalsByteLength = checked((long)data.Normals.Length * 12L);
                    value.TangentsOffset = Align4(value.NormalsOffset +
                                                  value.NormalsByteLength);
                    value.TangentsByteLength = checked((long)data.Normals.Length * 16L);
                    value.TexCoordsOffset = Align4(value.TangentsOffset +
                                                   value.TangentsByteLength);
                    value.TexCoordsByteLength = checked((long)data.TexCoords0.Length * 8L);
                    value.IndicesOffset = Align4(value.TexCoordsOffset +
                                                 value.TexCoordsByteLength);
                    value.IndicesByteLength = checked((long)data.Indices.Length * 4L);
                    value.BaseColorPngOffset = Align4(value.IndicesOffset +
                                                      value.IndicesByteLength);
                    value.BaseColorPngByteLength = baseLength;
                    value.NormalPngOffset = Align4(value.BaseColorPngOffset + baseLength);
                    value.NormalPngByteLength = normalLength;
                    value.BinaryByteLength = Align4(value.NormalPngOffset + normalLength);
                    layout = value;
                    return true;
                }
                catch (OverflowException)
                {
                    error = "GLB binary layout overflowed.";
                    return false;
                }
            }
        }
    }
}
