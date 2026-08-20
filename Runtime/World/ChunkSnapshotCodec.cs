using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// CPU-owned snapshot of the reusable local TSDF. The pose is redundant with the
    /// manifest on purpose: restore rejects a payload captured in the wrong chunk frame.
    /// </summary>
    public sealed class ChunkVolumeSnapshot
    {
        public Vector3Int VoxelCount { get; set; }
        public float VoxelSize { get; set; }
        public int IntegrationCount { get; set; }
        public RigidPoseData WorldFromVolume { get; set; } = RigidPoseData.Identity;
        public byte[] TsdfBytes { get; set; } = Array.Empty<byte>();
        public byte[] ColorBytes { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Compact copy of QRS' Surface Nets buffers. Vertices retain the upstream 32-byte
    /// layout: float3 position, float3 normal, packed RGBA8 color, voxel index.
    /// </summary>
    public sealed class ChunkLiveMeshSnapshot
    {
        public const int VertexStride = 32;

        public int VertexCount { get; set; }
        public int IndexCount { get; set; }
        public BoundsData LocalBounds { get; set; } = BoundsData.Empty;
        public byte[] VertexBytes { get; set; } = Array.Empty<byte>();
        public byte[] IndexBytes { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Versioned little-endian artifact codec. It has strict allocation limits and checks
    /// exact payload lengths before allocating, because chunk files may have crossed a LAN
    /// or survived an interrupted write. SHA verification is handled by <see cref="WorldStore"/>.
    /// </summary>
    public static class ChunkSnapshotCodec
    {
        public const int VolumeFormatVersion = 1;
        public const int LiveMeshFormatVersion = 1;

        private const uint VolumeMagic = 0x56534951; // "QISV"
        private const uint LiveMeshMagic = 0x4D534951; // "QISM"
        private const int MaximumVoxelAxis = 512;
        private const int MaximumLiveMeshVertices = 2_000_000;
        private const int MaximumLiveMeshIndices = 36_000_000;

        public static bool TryWriteVolume(Stream stream, ChunkVolumeSnapshot snapshot,
            out string error)
        {
            error = null;
            if (stream == null || !stream.CanWrite)
            {
                error = "Volume destination is not writable.";
                return false;
            }
            if (!TryValidateVolume(snapshot, out error))
                return false;

            try
            {
                using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
                writer.Write(VolumeMagic);
                writer.Write(VolumeFormatVersion);
                writer.Write(snapshot.VoxelCount.x);
                writer.Write(snapshot.VoxelCount.y);
                writer.Write(snapshot.VoxelCount.z);
                writer.Write(snapshot.VoxelSize);
                writer.Write(snapshot.IntegrationCount);
                WritePose(writer, snapshot.WorldFromVolume);
                writer.Write(snapshot.TsdfBytes.Length);
                writer.Write(snapshot.ColorBytes.Length);
                writer.Write(snapshot.TsdfBytes);
                writer.Write(snapshot.ColorBytes);
                writer.Flush();
                return true;
            }
            catch (Exception exception)
            {
                error = $"Volume serialization failed: {exception.Message}";
                return false;
            }
        }

        public static bool TryReadVolume(Stream stream, out ChunkVolumeSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            error = null;
            if (!CanReadBoundedStream(stream, out error))
                return false;

            try
            {
                using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                if (reader.ReadUInt32() != VolumeMagic)
                    throw new InvalidDataException("Volume artifact magic is invalid.");
                int version = reader.ReadInt32();
                if (version != VolumeFormatVersion)
                    throw new InvalidDataException($"Unsupported volume format {version}.");

                var candidate = new ChunkVolumeSnapshot
                {
                    VoxelCount = new Vector3Int(reader.ReadInt32(), reader.ReadInt32(),
                        reader.ReadInt32()),
                    VoxelSize = reader.ReadSingle(),
                    IntegrationCount = reader.ReadInt32(),
                    WorldFromVolume = ReadPose(reader)
                };
                int tsdfLength = reader.ReadInt32();
                int colorLength = reader.ReadInt32();
                if (!TryExpectedVolumeLengths(candidate.VoxelCount, out int expectedTsdf,
                        out int expectedColor) || tsdfLength != expectedTsdf ||
                    colorLength != expectedColor)
                    throw new InvalidDataException("Volume byte lengths do not match dimensions.");
                EnsureRemainingLength(stream, (long)tsdfLength + colorLength);
                candidate.TsdfBytes = ReadExact(reader, tsdfLength);
                candidate.ColorBytes = ReadExact(reader, colorLength);
                EnsureEndOfStream(stream);
                if (!TryValidateVolume(candidate, out error))
                    return false;
                snapshot = candidate;
                return true;
            }
            catch (Exception exception) when (exception is InvalidDataException ||
                                              exception is IOException ||
                                              exception is ArgumentException ||
                                              exception is OverflowException)
            {
                error = $"Volume artifact rejected: {exception.Message}";
                return false;
            }
        }

        public static bool TryWriteLiveMesh(Stream stream, ChunkLiveMeshSnapshot snapshot,
            out string error)
        {
            error = null;
            if (stream == null || !stream.CanWrite)
            {
                error = "Live-mesh destination is not writable.";
                return false;
            }
            if (!TryValidateLiveMesh(snapshot, true, out error))
                return false;

            try
            {
                using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
                writer.Write(LiveMeshMagic);
                writer.Write(LiveMeshFormatVersion);
                writer.Write(ChunkLiveMeshSnapshot.VertexStride);
                writer.Write(snapshot.VertexCount);
                writer.Write(snapshot.IndexCount);
                WriteBounds(writer, snapshot.LocalBounds);
                writer.Write(snapshot.VertexBytes.Length);
                writer.Write(snapshot.IndexBytes.Length);
                writer.Write(snapshot.VertexBytes);
                writer.Write(snapshot.IndexBytes);
                writer.Flush();
                return true;
            }
            catch (Exception exception)
            {
                error = $"Live-mesh serialization failed: {exception.Message}";
                return false;
            }
        }

        public static bool TryReadLiveMesh(Stream stream, out ChunkLiveMeshSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            error = null;
            if (!CanReadBoundedStream(stream, out error))
                return false;

            try
            {
                using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                if (reader.ReadUInt32() != LiveMeshMagic)
                    throw new InvalidDataException("Live-mesh artifact magic is invalid.");
                int version = reader.ReadInt32();
                if (version != LiveMeshFormatVersion)
                    throw new InvalidDataException($"Unsupported live-mesh format {version}.");
                int stride = reader.ReadInt32();
                if (stride != ChunkLiveMeshSnapshot.VertexStride)
                    throw new InvalidDataException($"Unsupported live-mesh stride {stride}.");

                var candidate = new ChunkLiveMeshSnapshot
                {
                    VertexCount = reader.ReadInt32(),
                    IndexCount = reader.ReadInt32(),
                    LocalBounds = ReadBounds(reader)
                };
                int vertexLength = reader.ReadInt32();
                int indexLength = reader.ReadInt32();
                if (candidate.VertexCount < 0 || candidate.VertexCount > MaximumLiveMeshVertices ||
                    candidate.IndexCount < 0 || candidate.IndexCount > MaximumLiveMeshIndices ||
                    candidate.IndexCount % 3 != 0 ||
                    vertexLength != candidate.VertexCount * ChunkLiveMeshSnapshot.VertexStride ||
                    indexLength != candidate.IndexCount * sizeof(uint))
                    throw new InvalidDataException("Live-mesh counts or byte lengths are invalid.");
                EnsureRemainingLength(stream, (long)vertexLength + indexLength);
                candidate.VertexBytes = ReadExact(reader, vertexLength);
                candidate.IndexBytes = ReadExact(reader, indexLength);
                EnsureEndOfStream(stream);
                if (!TryValidateLiveMesh(candidate, true, out error))
                    return false;
                snapshot = candidate;
                return true;
            }
            catch (Exception exception) when (exception is InvalidDataException ||
                                              exception is IOException ||
                                              exception is ArgumentException ||
                                              exception is OverflowException)
            {
                error = $"Live-mesh artifact rejected: {exception.Message}";
                return false;
            }
        }

        private static bool TryValidateVolume(ChunkVolumeSnapshot snapshot, out string error)
        {
            error = null;
            if (snapshot == null)
            {
                error = "Volume snapshot is null.";
                return false;
            }
            if (!TryExpectedVolumeLengths(snapshot.VoxelCount, out int tsdfLength,
                    out int colorLength))
            {
                error = "Volume dimensions are outside supported limits.";
                return false;
            }
            if (!IsFinite(snapshot.VoxelSize) || snapshot.VoxelSize <= 0f ||
                snapshot.IntegrationCount < 0 || !IsFinitePose(snapshot.WorldFromVolume))
            {
                error = "Volume metadata is invalid.";
                return false;
            }
            if (snapshot.TsdfBytes == null || snapshot.TsdfBytes.Length != tsdfLength ||
                snapshot.ColorBytes == null || snapshot.ColorBytes.Length != colorLength)
            {
                error = "Volume payload lengths do not match dimensions.";
                return false;
            }
            return true;
        }

        private static bool TryValidateLiveMesh(ChunkLiveMeshSnapshot snapshot,
            bool inspectIndices, out string error)
        {
            error = null;
            if (snapshot == null || snapshot.VertexCount <= 0 ||
                snapshot.VertexCount > MaximumLiveMeshVertices || snapshot.IndexCount <= 0 ||
                snapshot.IndexCount > MaximumLiveMeshIndices || snapshot.IndexCount % 3 != 0)
            {
                error = "Live-mesh counts are outside supported limits.";
                return false;
            }
            if (snapshot.VertexBytes == null ||
                snapshot.VertexBytes.Length != snapshot.VertexCount *
                    ChunkLiveMeshSnapshot.VertexStride || snapshot.IndexBytes == null ||
                snapshot.IndexBytes.Length != snapshot.IndexCount * sizeof(uint) ||
                !IsFiniteBounds(snapshot.LocalBounds))
            {
                error = "Live-mesh payload lengths or bounds are invalid.";
                return false;
            }
            if (!inspectIndices)
                return true;
            for (int i = 0; i < snapshot.IndexCount; i++)
            {
                uint index = BitConverter.ToUInt32(snapshot.IndexBytes, i * sizeof(uint));
                if (index >= snapshot.VertexCount)
                {
                    error = $"Live-mesh index {i} is outside the vertex array.";
                    return false;
                }
            }
            return true;
        }

        private static bool TryExpectedVolumeLengths(Vector3Int count, out int tsdfLength,
            out int colorLength)
        {
            tsdfLength = 0;
            colorLength = 0;
            if (count.x <= 0 || count.y <= 0 || count.z <= 0 ||
                count.x > MaximumVoxelAxis || count.y > MaximumVoxelAxis ||
                count.z > MaximumVoxelAxis)
                return false;
            try
            {
                int voxels = checked(count.x * count.y * count.z);
                tsdfLength = checked(voxels * 2);
                colorLength = checked(voxels * 4);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool CanReadBoundedStream(Stream stream, out string error)
        {
            error = null;
            if (stream == null || !stream.CanRead || !stream.CanSeek)
            {
                error = "Artifact source must be a readable seekable stream.";
                return false;
            }
            long remaining = stream.Length - stream.Position;
            if (remaining < 0 || remaining > WorldSchema.MaximumArtifactBytes)
            {
                error = "Artifact source length is outside supported limits.";
                return false;
            }
            return true;
        }

        private static void EnsureRemainingLength(Stream stream, long expected)
        {
            if (expected < 0 || stream.Length - stream.Position != expected)
                throw new InvalidDataException("Artifact payload length does not match the file.");
        }

        private static void EnsureEndOfStream(Stream stream)
        {
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Artifact has trailing bytes.");
        }

        private static byte[] ReadExact(BinaryReader reader, int count)
        {
            byte[] bytes = reader.ReadBytes(count);
            if (bytes.Length != count)
                throw new EndOfStreamException("Artifact ended before its declared payload.");
            return bytes;
        }

        private static void WritePose(BinaryWriter writer, RigidPoseData pose)
        {
            writer.Write(pose.position.x);
            writer.Write(pose.position.y);
            writer.Write(pose.position.z);
            writer.Write(pose.rotation.x);
            writer.Write(pose.rotation.y);
            writer.Write(pose.rotation.z);
            writer.Write(pose.rotation.w);
        }

        private static RigidPoseData ReadPose(BinaryReader reader)
        {
            return new RigidPoseData(
                new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle()));
        }

        private static void WriteBounds(BinaryWriter writer, BoundsData bounds)
        {
            writer.Write(bounds.center.x);
            writer.Write(bounds.center.y);
            writer.Write(bounds.center.z);
            writer.Write(bounds.extents.x);
            writer.Write(bounds.extents.y);
            writer.Write(bounds.extents.z);
        }

        private static BoundsData ReadBounds(BinaryReader reader)
        {
            return new BoundsData(
                new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
        }

        private static bool IsFinitePose(RigidPoseData pose)
        {
            float norm = pose.rotation.x * pose.rotation.x + pose.rotation.y * pose.rotation.y +
                         pose.rotation.z * pose.rotation.z + pose.rotation.w * pose.rotation.w;
            return IsFinite(pose.position.x) && IsFinite(pose.position.y) &&
                   IsFinite(pose.position.z) && IsFinite(norm) && Mathf.Abs(norm - 1f) <= 0.01f;
        }

        private static bool IsFiniteBounds(BoundsData bounds)
        {
            return IsFinite(bounds.center.x) && IsFinite(bounds.center.y) &&
                   IsFinite(bounds.center.z) && IsFinite(bounds.extents.x) &&
                   IsFinite(bounds.extents.y) && IsFinite(bounds.extents.z) &&
                   bounds.extents.x >= 0f && bounds.extents.y >= 0f &&
                   bounds.extents.z >= 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
