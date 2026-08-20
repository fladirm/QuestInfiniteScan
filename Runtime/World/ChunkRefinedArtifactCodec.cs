using System;
using System.IO;
using UnityEngine;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// Bounded, versioned persistence format for a chunk-local UV mesh and its raw RGBA
    /// textures. Mesh, base-color atlas, and normal atlas are separate artifacts so a
    /// consumer can stream only the payloads it needs.
    /// </summary>
    internal static class ChunkRefinedArtifactCodec
    {
        public const int MeshFormatVersion = 1;
        public const int TextureFormatVersion = 1;

        private const uint MeshMagic = 0x4D524951;    // QIRM
        private const uint TextureMagic = 0x54524951; // QIRT
        private const int MaximumVertices = 8_000_000;
        private const int MaximumIndices = 24_000_000;
        private const int MaximumTextureDimension = 8_192;
        private const long MaximumTextureBytes = 256L * 1024 * 1024;

        internal static bool TryWriteMesh(Stream stream, RefinedTextureResult mesh,
            out string error)
        {
            error = ValidateMesh(mesh);
            if (error != null)
                return false;
            if (stream == null || !stream.CanWrite)
            {
                error = "Refined mesh destination is not writable.";
                return false;
            }

            try
            {
                using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
                writer.Write(MeshMagic);
                writer.Write(MeshFormatVersion);
                writer.Write(mesh.Positions.Length);
                writer.Write(mesh.Indices.Length);
                writer.Write(mesh.AtlasWidth);
                writer.Write(mesh.AtlasHeight);
                for (int i = 0; i < mesh.Positions.Length; i++)
                {
                    Vector3 position = mesh.Positions[i];
                    Vector3 normal = mesh.Normals[i];
                    Vector2 uv = mesh.UVs[i];
                    writer.Write(position.x);
                    writer.Write(position.y);
                    writer.Write(position.z);
                    writer.Write(normal.x);
                    writer.Write(normal.y);
                    writer.Write(normal.z);
                    writer.Write(uv.x);
                    writer.Write(uv.y);
                }
                for (int i = 0; i < mesh.Indices.Length; i++)
                    writer.Write(mesh.Indices[i]);
                writer.Flush();
                return true;
            }
            catch (Exception exception)
            {
                error = "Refined mesh write failed: " + exception.Message;
                return false;
            }
        }

        internal static bool TryReadMesh(Stream stream, out RefinedTextureResult mesh,
            out string error)
        {
            mesh = default;
            error = null;
            if (stream == null || !stream.CanRead)
            {
                error = "Refined mesh source is not readable.";
                return false;
            }

            try
            {
                using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
                if (reader.ReadUInt32() != MeshMagic)
                    throw new InvalidDataException("Refined mesh magic is invalid.");
                int version = reader.ReadInt32();
                if (version != MeshFormatVersion)
                    throw new InvalidDataException(
                        $"Refined mesh format {version} is unsupported.");
                int vertexCount = reader.ReadInt32();
                int indexCount = reader.ReadInt32();
                int atlasWidth = reader.ReadInt32();
                int atlasHeight = reader.ReadInt32();
                if (vertexCount <= 0 || vertexCount > MaximumVertices ||
                    indexCount <= 0 || indexCount > MaximumIndices || indexCount % 3 != 0)
                    throw new InvalidDataException("Refined mesh counts are invalid.");
                if (!IsValidTextureSize(atlasWidth, atlasHeight, out _))
                    throw new InvalidDataException("Refined mesh atlas dimensions are invalid.");

                long requiredBytes = checked((long)vertexCount * 32L +
                                             (long)indexCount * sizeof(int));
                if (stream.CanSeek && stream.Length - stream.Position != requiredBytes)
                    throw new InvalidDataException("Refined mesh byte length is inconsistent.");

                var positions = new Vector3[vertexCount];
                var normals = new Vector3[vertexCount];
                var uvs = new Vector2[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    positions[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(),
                        reader.ReadSingle());
                    normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(),
                        reader.ReadSingle());
                    uvs[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    if (!IsFinite(positions[i]) || !IsFinite(normals[i]) ||
                        !IsFinite(uvs[i]))
                        throw new InvalidDataException(
                            "Refined mesh contains non-finite vertex data.");
                }
                var indices = new int[indexCount];
                for (int i = 0; i < indexCount; i++)
                {
                    indices[i] = reader.ReadInt32();
                    if ((uint)indices[i] >= (uint)vertexCount)
                        throw new InvalidDataException(
                            "Refined mesh contains an out-of-range index.");
                }
                if (stream.CanSeek && stream.Position != stream.Length)
                    throw new InvalidDataException("Refined mesh contains trailing bytes.");

                mesh = new RefinedTextureResult
                {
                    Positions = positions,
                    Normals = normals,
                    UVs = uvs,
                    Indices = indices,
                    AtlasWidth = atlasWidth,
                    AtlasHeight = atlasHeight
                };
                return true;
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is InvalidDataException ||
                                              exception is OverflowException ||
                                              exception is OutOfMemoryException)
            {
                mesh = default;
                error = "Refined mesh rejected: " + exception.Message;
                return false;
            }
        }

        internal static bool TryWriteRgbaTexture(Stream stream, byte[] pixels, int width,
            int height, out string error)
        {
            error = ValidateTexture(pixels, width, height);
            if (error != null)
                return false;
            if (stream == null || !stream.CanWrite)
            {
                error = "Refined texture destination is not writable.";
                return false;
            }

            try
            {
                using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
                writer.Write(TextureMagic);
                writer.Write(TextureFormatVersion);
                writer.Write(width);
                writer.Write(height);
                writer.Write(pixels.Length);
                writer.Write(pixels);
                writer.Flush();
                return true;
            }
            catch (Exception exception)
            {
                error = "Refined texture write failed: " + exception.Message;
                return false;
            }
        }

        internal static bool TryReadRgbaTexture(Stream stream, out byte[] pixels,
            out int width, out int height, out string error)
        {
            pixels = null;
            width = 0;
            height = 0;
            error = null;
            if (stream == null || !stream.CanRead)
            {
                error = "Refined texture source is not readable.";
                return false;
            }

            try
            {
                using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
                if (reader.ReadUInt32() != TextureMagic)
                    throw new InvalidDataException("Refined texture magic is invalid.");
                int version = reader.ReadInt32();
                if (version != TextureFormatVersion)
                    throw new InvalidDataException(
                        $"Refined texture format {version} is unsupported.");
                width = reader.ReadInt32();
                height = reader.ReadInt32();
                int byteLength = reader.ReadInt32();
                if (!IsValidTextureSize(width, height, out long expectedBytes) ||
                    byteLength != expectedBytes)
                    throw new InvalidDataException("Refined texture dimensions are invalid.");
                if (stream.CanSeek && stream.Length - stream.Position != byteLength)
                    throw new InvalidDataException("Refined texture byte length is inconsistent.");
                pixels = reader.ReadBytes(byteLength);
                if (pixels.Length != byteLength)
                    throw new EndOfStreamException("Refined texture payload is truncated.");
                if (stream.CanSeek && stream.Position != stream.Length)
                    throw new InvalidDataException("Refined texture contains trailing bytes.");
                return true;
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is InvalidDataException ||
                                              exception is OverflowException ||
                                              exception is OutOfMemoryException)
            {
                pixels = null;
                width = 0;
                height = 0;
                error = "Refined texture rejected: " + exception.Message;
                return false;
            }
        }

        internal static bool TryValidateMesh(RefinedTextureResult mesh, out string error)
        {
            error = ValidateMesh(mesh);
            return error == null;
        }

        internal static bool TryValidateRgbaTexture(byte[] pixels, int width, int height,
            out string error)
        {
            error = ValidateTexture(pixels, width, height);
            return error == null;
        }

        private static string ValidateMesh(RefinedTextureResult mesh)
        {
            if (mesh.Positions == null || mesh.Normals == null || mesh.UVs == null ||
                mesh.Indices == null)
                return "Refined mesh arrays are required.";
            int vertexCount = mesh.Positions.Length;
            if (vertexCount <= 0 || vertexCount > MaximumVertices ||
                mesh.Normals.Length != vertexCount || mesh.UVs.Length != vertexCount)
                return "Refined mesh vertex arrays have invalid counts.";
            if (mesh.Indices.Length <= 0 || mesh.Indices.Length > MaximumIndices ||
                mesh.Indices.Length % 3 != 0)
                return "Refined mesh index count is invalid.";
            if (!IsValidTextureSize(mesh.AtlasWidth, mesh.AtlasHeight, out _))
                return "Refined mesh atlas dimensions are invalid.";
            for (int i = 0; i < vertexCount; i++)
            {
                if (!IsFinite(mesh.Positions[i]) || !IsFinite(mesh.Normals[i]) ||
                    !IsFinite(mesh.UVs[i]))
                    return "Refined mesh contains non-finite vertex data.";
            }
            for (int i = 0; i < mesh.Indices.Length; i++)
            {
                if ((uint)mesh.Indices[i] >= (uint)vertexCount)
                    return "Refined mesh contains an out-of-range index.";
            }
            return null;
        }

        private static string ValidateTexture(byte[] pixels, int width, int height)
        {
            if (pixels == null)
                return "Refined texture pixels are required.";
            if (!IsValidTextureSize(width, height, out long expectedBytes) ||
                pixels.LongLength != expectedBytes)
                return "Refined texture dimensions do not match its RGBA32 payload.";
            return null;
        }

        private static bool IsValidTextureSize(int width, int height, out long byteLength)
        {
            byteLength = 0;
            if (width <= 0 || height <= 0 || width > MaximumTextureDimension ||
                height > MaximumTextureDimension)
                return false;
            try
            {
                byteLength = checked((long)width * height * 4L);
                return byteLength <= MaximumTextureBytes && byteLength <= int.MaxValue;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
