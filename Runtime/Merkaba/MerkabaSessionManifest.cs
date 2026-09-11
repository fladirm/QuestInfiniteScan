using System;
using System.IO;
using UnityEngine;

namespace Genesis.RoomScan
{
    internal enum MerkabaStorageStream : byte
    {
        M8Base = 0,
        M8Live = 1,
        FlowerDetail = 2,
        ThreadAtlas = 3,
        Count = 4
    }

    internal enum MerkabaCommitStage : byte
    {
        BeforeDataFlush = 0,
        AfterDataFlush = 1,
        AfterManifestFlush = 2,
        AfterManifestPublish = 3
    }

    internal enum MerkabaCompactionStage : byte
    {
        BeforeRecoveryDataFlush = 0,
        AfterRecoveryDataFlush = 1,
        AfterRecoveryManifestFlush = 2,
        AfterRecoveryManifestPublish = 3,
        AfterM8BasePublish = 4,
        AfterFinalManifestFlush = 5,
        AfterFinalManifestPublish = 6
    }

    /// <summary>
    /// The only durable transaction boundary of one direct M8 session. Stream
    /// bytes beyond the four recorded end offsets are not part of the world.
    /// </summary>
    internal sealed class MerkabaSessionManifest
    {
        internal const uint Magic = 0x464d384du; // M8MF
        internal const ushort Version = 5;
        internal const int ByteSize = 184;
        internal const int CrcOffset = 176;

        internal Guid SessionUuid;
        internal Guid AnchorUuid;
        internal Matrix4x4 AnchorAtSave = Matrix4x4.identity;
        internal ulong CommitGeneration;
        internal int IntegrationCount;
        internal uint OccupiedKernelCount;
        internal uint CanonicalTileCount;
        internal ulong M8BaseGeneration;
        internal readonly long[] ValidEnds = new long[(int)
            MerkabaStorageStream.Count];

        internal long ValidEnd(MerkabaStorageStream stream) =>
            ValidEnds[ValidateStream(stream)];

        internal MerkabaSessionManifest CloneForSession(Guid sessionUuid)
        {
            var result = new MerkabaSessionManifest
            {
                SessionUuid = sessionUuid,
                AnchorUuid = AnchorUuid,
                AnchorAtSave = AnchorAtSave,
                CommitGeneration = CommitGeneration,
                IntegrationCount = IntegrationCount,
                OccupiedKernelCount = OccupiedKernelCount,
                CanonicalTileCount = CanonicalTileCount,
                M8BaseGeneration = M8BaseGeneration
            };
            Array.Copy(ValidEnds, result.ValidEnds, ValidEnds.Length);
            result.Validate();
            return result;
        }

        internal void Validate()
        {
            if (SessionUuid == Guid.Empty || AnchorUuid == Guid.Empty)
                throw new InvalidDataException(
                    "Session manifest requires session and anchor UUIDs.");
            if (CommitGeneration == 0ul || IntegrationCount < 0)
                throw new InvalidDataException(
                    "Session manifest generation/count is invalid.");
            if (M8BaseGeneration > CommitGeneration)
                throw new InvalidDataException(
                    "A base generation cannot exceed the committed generation.");
            for (int i = 0; i < ValidEnds.Length; i++)
                if (ValidEnds[i] < 0L)
                    throw new InvalidDataException(
                        "Session manifest stream end is negative.");
            for (int i = 0; i < 16; i++)
                if (!IsFinite(AnchorAtSave[i]))
                    throw new InvalidDataException(
                        "Session manifest anchor transform is not finite.");
        }

        internal static void Write(Stream destination,
            MerkabaSessionManifest manifest)
        {
            if (destination == null || !destination.CanWrite)
                throw new ArgumentException(
                    "Manifest destination is not writable.",
                    nameof(destination));
            if (manifest == null) throw new ArgumentNullException(
                nameof(manifest));
            manifest.Validate();

            var bytes = new byte[ByteSize];
            MerkabaSphereFlowerPersistenceAbi.WriteUInt32(bytes, 0, Magic);
            MerkabaSphereFlowerPersistenceAbi.WriteUInt16(bytes, 4, Version);
            MerkabaSphereFlowerPersistenceAbi.WriteUInt16(bytes, 6,
                (ushort)ByteSize);
            MerkabaSphereFlowerPersistenceAbi.WriteUInt32(bytes, 8,
                MerkabaSphereFlowerAuthority.FrozenTableHash);
            MerkabaSphereFlowerPersistenceAbi.WriteUInt64(bytes, 16,
                manifest.CommitGeneration);
            WriteGuid(bytes, 24, manifest.SessionUuid);
            WriteGuid(bytes, 40, manifest.AnchorUuid);
            for (int i = 0; i < 16; i++)
                MerkabaSphereFlowerPersistenceAbi.WriteInt32(bytes,
                    56 + i * 4,
                    BitConverter.SingleToInt32Bits(manifest.AnchorAtSave[i]));
            MerkabaSphereFlowerPersistenceAbi.WriteInt32(bytes, 120,
                manifest.IntegrationCount);
            MerkabaSphereFlowerPersistenceAbi.WriteUInt32(bytes, 124,
                manifest.OccupiedKernelCount);
            MerkabaSphereFlowerPersistenceAbi.WriteUInt32(bytes, 128,
                manifest.CanonicalTileCount);
            MerkabaSphereFlowerPersistenceAbi.WriteUInt64(bytes, 136,
                manifest.M8BaseGeneration);
            for (int i = 0; i < manifest.ValidEnds.Length; i++)
                MerkabaSphereFlowerPersistenceAbi.WriteUInt64(bytes,
                    144 + i * 8, checked((ulong)manifest.ValidEnds[i]));
            uint crc = MerkabaSphereFlowerPersistenceAbi.ComputeBytesCrc(
                bytes.AsSpan(0, CrcOffset));
            MerkabaSphereFlowerPersistenceAbi.WriteUInt32(bytes, CrcOffset,
                crc);
            destination.Write(bytes, 0, bytes.Length);
        }

        internal static MerkabaSessionManifest Read(Stream source)
        {
            if (source == null || !source.CanRead)
                throw new ArgumentException("Manifest source is not readable.",
                    nameof(source));
            var bytes = new byte[ByteSize];
            int read = 0;
            while (read < bytes.Length)
            {
                int count = source.Read(bytes, read, bytes.Length - read);
                if (count == 0) throw new EndOfStreamException(
                    "Session manifest is truncated.");
                read += count;
            }
            if (source.ReadByte() != -1)
                throw new InvalidDataException(
                    "Session manifest has trailing bytes.");
            if (MerkabaSphereFlowerPersistenceAbi.ReadUInt32(bytes, 0) !=
                    Magic ||
                MerkabaSphereFlowerPersistenceAbi.ReadUInt16(bytes, 4) !=
                    Version ||
                MerkabaSphereFlowerPersistenceAbi.ReadUInt16(bytes, 6) !=
                    ByteSize)
                throw new InvalidDataException(
                    "Unsupported session manifest format.");
            if (MerkabaSphereFlowerPersistenceAbi.ReadUInt32(bytes, 8) !=
                MerkabaSphereFlowerAuthority.FrozenTableHash)
                throw new InvalidDataException(
                    "Session Sphere-Flower table authority differs from this build.");
            if (MerkabaSphereFlowerPersistenceAbi.ReadUInt32(bytes,
                    CrcOffset) !=
                MerkabaSphereFlowerPersistenceAbi.ComputeBytesCrc(
                    bytes.AsSpan(0, CrcOffset)))
                throw new InvalidDataException("Session manifest CRC mismatch.");
            if (MerkabaSphereFlowerPersistenceAbi.ReadUInt32(bytes, 12) != 0u ||
                MerkabaSphereFlowerPersistenceAbi.ReadUInt32(bytes, 132) != 0u ||
                MerkabaSphereFlowerPersistenceAbi.ReadUInt32(bytes, 180) != 0u)
                throw new InvalidDataException(
                    "Session manifest reserved bits are nonzero.");

            var result = new MerkabaSessionManifest
            {
                CommitGeneration =
                    MerkabaSphereFlowerPersistenceAbi.ReadUInt64(bytes, 16),
                SessionUuid = ReadGuid(bytes, 24),
                AnchorUuid = ReadGuid(bytes, 40),
                IntegrationCount =
                    MerkabaSphereFlowerPersistenceAbi.ReadInt32(bytes, 120),
                OccupiedKernelCount =
                    MerkabaSphereFlowerPersistenceAbi.ReadUInt32(bytes, 124),
                CanonicalTileCount =
                    MerkabaSphereFlowerPersistenceAbi.ReadUInt32(bytes, 128),
                M8BaseGeneration =
                    MerkabaSphereFlowerPersistenceAbi.ReadUInt64(bytes, 136)
            };
            Matrix4x4 matrix = default;
            for (int i = 0; i < 16; i++)
                matrix[i] = BitConverter.Int32BitsToSingle(
                    MerkabaSphereFlowerPersistenceAbi.ReadInt32(bytes,
                        56 + i * 4));
            result.AnchorAtSave = matrix;
            for (int i = 0; i < result.ValidEnds.Length; i++)
            {
                ulong end = MerkabaSphereFlowerPersistenceAbi.ReadUInt64(bytes,
                    144 + i * 8);
                if (end > long.MaxValue)
                    throw new InvalidDataException(
                        "Session manifest stream end exceeds platform range.");
                result.ValidEnds[i] = (long)end;
            }
            result.Validate();
            return result;
        }

        private static int ValidateStream(MerkabaStorageStream stream)
        {
            int index = (int)stream;
            if ((uint)index >= (uint)MerkabaStorageStream.Count)
                throw new ArgumentOutOfRangeException(nameof(stream));
            return index;
        }

        private static void WriteGuid(byte[] destination, int offset,
            Guid value)
        {
            byte[] bytes = value.ToByteArray();
            Buffer.BlockCopy(bytes, 0, destination, offset, bytes.Length);
        }

        private static Guid ReadGuid(byte[] source, int offset)
        {
            var bytes = new byte[16];
            Buffer.BlockCopy(source, offset, bytes, 0, bytes.Length);
            return new Guid(bytes);
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    internal sealed class MerkabaSessionOpenState
    {
        internal readonly MerkabaSessionManifest Manifest;
        internal readonly int IndexedTileCount;

        internal MerkabaSessionOpenState(MerkabaSessionManifest manifest,
            int indexedTileCount)
        {
            Manifest = manifest ?? throw new ArgumentNullException(
                nameof(manifest));
            IndexedTileCount = indexedTileCount;
        }
    }

    /// <summary>
    /// An exact live append prefix, not a proof that GPU work has drained. The
    /// coordinator must pair it with the generation-bound GPU drain receipt.
    /// </summary>
    internal readonly struct MerkabaStorageAppendPosition
    {
        internal readonly ulong Generation;
        internal readonly ulong RecordSequence;
        internal readonly object Authority;

        internal MerkabaStorageAppendPosition(object authority,
            ulong generation, ulong recordSequence)
        {
            Authority = authority;
            Generation = generation;
            RecordSequence = recordSequence;
        }
    }

    internal readonly struct MerkabaStorageCommitResult
    {
        // default is Retry: it must never authorize a durable GPU watermark.
        internal readonly bool Committed;
        internal readonly ulong Generation;
        internal readonly int CanonicalTileCount;
        internal readonly long DirtyBytes;

        internal MerkabaStorageCommitResult(ulong generation,
            int canonicalTileCount, long dirtyBytes)
        {
            Committed = true;
            Generation = generation;
            CanonicalTileCount = canonicalTileCount;
            DirtyBytes = dirtyBytes;
        }
    }
}
