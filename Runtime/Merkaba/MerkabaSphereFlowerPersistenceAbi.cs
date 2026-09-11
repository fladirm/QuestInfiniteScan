using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    internal enum MerkabaRecordKind : ushort
    {
        M8Tile = 1,
        FlowerOwnerEpoch = 2,
        FlowerDetail = 3,
        FlowerSkinMetricRun = 4,
        FlowerVGroup = 5,
        ThreadProgram = 6,
        ThreadRun = 7,
        ThreadColorGroup = 8,
        Tombstone = 9
    }

    /// <summary>
    /// Owner-local deletion of one append-only fine record. TargetKind is
    /// deliberately restricted to records whose identity is one uint key under
    /// the owner address.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaTombstoneRecord
    {
        internal const int ByteSize = 8;

        internal uint TargetKind;
        internal uint LocalKey;

        internal static MerkabaTombstoneRecord Create(
            MerkabaRecordKind targetKind, uint localKey)
        {
            if (targetKind != MerkabaRecordKind.FlowerDetail &&
                targetKind != MerkabaRecordKind.FlowerSkinMetricRun &&
                targetKind != MerkabaRecordKind.FlowerVGroup &&
                targetKind != MerkabaRecordKind.ThreadRun &&
                targetKind != MerkabaRecordKind.ThreadColorGroup)
                throw new ArgumentOutOfRangeException(nameof(targetKind));
            return new MerkabaTombstoneRecord
            {
                TargetKind = (uint)targetKind,
                LocalKey = localKey
            };
        }

        internal readonly bool IsCanonical =>
            TargetKind == (uint)MerkabaRecordKind.FlowerDetail ||
            TargetKind == (uint)MerkabaRecordKind.FlowerSkinMetricRun ||
            TargetKind == (uint)MerkabaRecordKind.FlowerVGroup ||
            TargetKind == (uint)MerkabaRecordKind.ThreadRun ||
            TargetKind == (uint)MerkabaRecordKind.ThreadColorGroup;
    }

    /// <summary>Exact 28-byte little-endian append-record header.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaRecordHeader
    {
        internal const int ByteSize = 28;
        internal const uint MagicValue = 0x4653384du; // bytes "M8SF"
        internal const ushort CurrentVersion = 6;

        internal uint Magic;
        internal ushort Version;
        internal ushort Kind;
        internal ulong CommitGeneration;
        internal uint AddressBytes;
        internal uint PayloadBytes;
        internal uint Crc32;

        internal static MerkabaRecordHeader Create(MerkabaRecordKind kind,
            ulong commitGeneration, ReadOnlySpan<byte> address,
            ReadOnlySpan<byte> payload)
        {
            ValidateKind(kind);
            if (commitGeneration == 0ul)
                throw new ArgumentOutOfRangeException(nameof(commitGeneration));
            MerkabaSphereFlowerPersistenceAbi.ValidateRecordShape(kind,
                address.Length, payload.Length);
            var header = new MerkabaRecordHeader
            {
                Magic = MagicValue,
                Version = CurrentVersion,
                Kind = (ushort)kind,
                CommitGeneration = commitGeneration,
                AddressBytes = checked((uint)address.Length),
                PayloadBytes = checked((uint)payload.Length)
            };
            header.Crc32 = MerkabaSphereFlowerPersistenceAbi.ComputeCrc(
                header, address, payload);
            return header;
        }

        internal readonly bool IsCanonical => Magic == MagicValue &&
            Version == CurrentVersion && CommitGeneration != 0ul &&
            IsDefinedKind((MerkabaRecordKind)Kind) &&
            AddressBytes <= int.MaxValue && PayloadBytes <= int.MaxValue &&
            MerkabaSphereFlowerPersistenceAbi.HasCanonicalRecordShape(
                (MerkabaRecordKind)Kind, (int)AddressBytes,
                (int)PayloadBytes);

        internal static void ValidateKind(MerkabaRecordKind kind)
        {
            if (!IsDefinedKind(kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        internal static bool IsDefinedKind(MerkabaRecordKind kind) =>
            kind >= MerkabaRecordKind.M8Tile &&
            kind <= MerkabaRecordKind.Tombstone;
    }

    /// <summary>
    /// Exact little-endian record and address codec. Addresses are the existing
    /// M8 block/chunk/tile coordinates, never a second hierarchy.
    /// </summary>
    internal static class MerkabaSphereFlowerPersistenceAbi
    {
        internal const int BlockAddressBytes = 12;
        internal const int ChunkAddressBytes = 16;
        internal const int TileAddressBytes = 16;
        internal const int OwnerAddressBytes = 20;
        internal const int GroupAddressBytes = 24;
        internal const int ProgramAddressBytes = 4;
        internal const int TombstonePayloadBytes =
            MerkabaTombstoneRecord.ByteSize;

        internal static void ValidateRecordShape(MerkabaRecordKind kind,
            int addressBytes, int payloadBytes)
        {
            int expectedAddress;
            int expectedPayload;
            switch (kind)
            {
                case MerkabaRecordKind.M8Tile:
                    expectedAddress = TileAddressBytes;
                    expectedPayload = MerkabaSpatial.KernelsPerTile *
                        KernelState.ByteSize;
                    break;
                case MerkabaRecordKind.FlowerOwnerEpoch:
                    expectedAddress = TileAddressBytes;
                    expectedPayload = MerkabaFlowerOwnerEpoch.ByteSize;
                    break;
                case MerkabaRecordKind.FlowerDetail:
                    expectedAddress = OwnerAddressBytes;
                    expectedPayload = MerkabaFlowerDetailRecord.ByteSize;
                    break;
                case MerkabaRecordKind.FlowerSkinMetricRun:
                    expectedAddress = OwnerAddressBytes;
                    expectedPayload = MerkabaFlowerSkinMetricRun.ByteSize;
                    break;
                case MerkabaRecordKind.FlowerVGroup:
                    expectedAddress = GroupAddressBytes;
                    expectedPayload = MerkabaFlowerVGroup.ByteSize;
                    break;
                case MerkabaRecordKind.ThreadProgram:
                    expectedAddress = ProgramAddressBytes;
                    expectedPayload = MerkabaThreadProgramRecord.ByteSize;
                    break;
                case MerkabaRecordKind.ThreadRun:
                    expectedAddress = OwnerAddressBytes;
                    expectedPayload = MerkabaThreadRun.ByteSize;
                    break;
                case MerkabaRecordKind.ThreadColorGroup:
                    expectedAddress = GroupAddressBytes;
                    expectedPayload = MerkabaThreadColorGroup.ByteSize;
                    break;
                case MerkabaRecordKind.Tombstone:
                    expectedAddress = OwnerAddressBytes;
                    expectedPayload = TombstonePayloadBytes;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (addressBytes != expectedAddress ||
                payloadBytes != expectedPayload)
                throw new ArgumentException(
                    $"{kind} requires {expectedAddress} address bytes and " +
                    $"{expectedPayload} payload bytes.");
        }

        internal static void WriteHeader(Span<byte> destination,
            in MerkabaRecordHeader header)
        {
            if (destination.Length < MerkabaRecordHeader.ByteSize)
                throw new ArgumentException("Record header buffer is short.",
                    nameof(destination));
            WriteUInt32(destination, 0, header.Magic);
            WriteUInt16(destination, 4, header.Version);
            WriteUInt16(destination, 6, header.Kind);
            WriteUInt64(destination, 8, header.CommitGeneration);
            WriteUInt32(destination, 16, header.AddressBytes);
            WriteUInt32(destination, 20, header.PayloadBytes);
            WriteUInt32(destination, 24, header.Crc32);
        }

        internal static bool TryReadHeader(ReadOnlySpan<byte> source,
            out MerkabaRecordHeader header)
        {
            if (source.Length < MerkabaRecordHeader.ByteSize)
            {
                header = default;
                return false;
            }
            header = new MerkabaRecordHeader
            {
                Magic = ReadUInt32(source, 0),
                Version = ReadUInt16(source, 4),
                Kind = ReadUInt16(source, 6),
                CommitGeneration = ReadUInt64(source, 8),
                AddressBytes = ReadUInt32(source, 16),
                PayloadBytes = ReadUInt32(source, 20),
                Crc32 = ReadUInt32(source, 24)
            };
            return header.IsCanonical;
        }

        internal static bool ValidateRecord(in MerkabaRecordHeader header,
            ReadOnlySpan<byte> address, ReadOnlySpan<byte> payload) =>
            header.IsCanonical && header.AddressBytes == (uint)address.Length &&
            header.PayloadBytes == (uint)payload.Length &&
            HasCanonicalRecordShape((MerkabaRecordKind)header.Kind,
                address.Length, payload.Length) &&
            header.Crc32 == ComputeCrc(header, address, payload);

        /// <summary>
        /// IEEE CRC-32 over every header field after Magic and before Crc32,
        /// followed by address and payload. Magic is checked independently.
        /// </summary>
        internal static uint ComputeCrc(in MerkabaRecordHeader header,
            ReadOnlySpan<byte> address, ReadOnlySpan<byte> payload)
        {
            uint crc = uint.MaxValue;
            crc = UpdateUInt16(crc, header.Version);
            crc = UpdateUInt16(crc, header.Kind);
            crc = UpdateUInt64(crc, header.CommitGeneration);
            crc = UpdateUInt32(crc, header.AddressBytes);
            crc = UpdateUInt32(crc, header.PayloadBytes);
            crc = Update(crc, address);
            crc = Update(crc, payload);
            return ~crc;
        }

        internal static uint ComputeBytesCrc(ReadOnlySpan<byte> bytes) =>
            ~Update(uint.MaxValue, bytes);

        internal static void WriteBlockAddress(Span<byte> destination,
            int3 blockCoord)
        {
            RequireSize(destination, BlockAddressBytes);
            WriteInt32(destination, 0, blockCoord.x);
            WriteInt32(destination, 4, blockCoord.y);
            WriteInt32(destination, 8, blockCoord.z);
        }

        internal static void WriteProgramAddress(Span<byte> destination,
            uint programRef)
        {
            RequireSize(destination, ProgramAddressBytes);
            WriteUInt32(destination, 0, programRef);
        }

        internal static uint ReadProgramAddress(ReadOnlySpan<byte> source)
        {
            RequireSize(source, ProgramAddressBytes);
            return ReadUInt32(source, 0);
        }

        internal static int3 ReadBlockAddress(ReadOnlySpan<byte> source)
        {
            RequireSize(source, BlockAddressBytes);
            return new int3(ReadInt32(source, 0), ReadInt32(source, 4),
                ReadInt32(source, 8));
        }

        internal static void WriteChunkAddress(Span<byte> destination,
            int3 blockCoord, int chunkLocal)
        {
            if ((uint)chunkLocal >= MerkabaSpatial.BlockChunkCount)
                throw new ArgumentOutOfRangeException(nameof(chunkLocal));
            RequireSize(destination, ChunkAddressBytes);
            WriteBlockAddress(destination.Slice(0, BlockAddressBytes),
                blockCoord);
            WriteUInt32(destination, 12, (uint)chunkLocal);
        }

        internal static void ReadChunkAddress(ReadOnlySpan<byte> source,
            out int3 blockCoord, out int chunkLocal)
        {
            RequireSize(source, ChunkAddressBytes);
            blockCoord = ReadBlockAddress(source.Slice(0, BlockAddressBytes));
            uint local = ReadUInt32(source, 12);
            if (local >= MerkabaSpatial.BlockChunkCount)
                throw new FormatException("Invalid M8 chunk-local address.");
            chunkLocal = (int)local;
        }

        internal static void WriteTileAddress(Span<byte> destination,
            in MerkabaTileAddress address)
        {
            RequireSize(destination, TileAddressBytes);
            WriteBlockAddress(destination.Slice(0, BlockAddressBytes),
                address.BlockCoord);
            WriteUInt32(destination, 12, address.LocalAddress);
        }

        internal static MerkabaTileAddress ReadTileAddress(
            ReadOnlySpan<byte> source)
        {
            RequireSize(source, TileAddressBytes);
            return new MerkabaTileAddress(ReadBlockAddress(
                    source.Slice(0, BlockAddressBytes)),
                ReadUInt32(source, 12));
        }

        internal static void WriteOwnerAddress(Span<byte> destination,
            in MerkabaTileAddress tile, int kernelLocal)
        {
            if ((uint)kernelLocal >= MerkabaSpatial.KernelsPerTile)
                throw new ArgumentOutOfRangeException(nameof(kernelLocal));
            RequireSize(destination, OwnerAddressBytes);
            WriteTileAddress(destination.Slice(0, TileAddressBytes), tile);
            WriteUInt32(destination, 16, (uint)kernelLocal);
        }

        internal static void ReadOwnerAddress(ReadOnlySpan<byte> source,
            out MerkabaTileAddress tile, out int kernelLocal)
        {
            RequireSize(source, OwnerAddressBytes);
            tile = ReadTileAddress(source.Slice(0, TileAddressBytes));
            uint local = ReadUInt32(source, 16);
            if (local >= MerkabaSpatial.KernelsPerTile)
                throw new FormatException("Invalid M8 kernel-local address.");
            kernelLocal = (int)local;
        }

        internal static void WriteGroupAddress(Span<byte> destination,
            in MerkabaTileAddress tile, int kernelLocal, uint groupIndex)
        {
            RequireSize(destination, GroupAddressBytes);
            WriteOwnerAddress(destination.Slice(0, OwnerAddressBytes), tile,
                kernelLocal);
            WriteUInt32(destination, OwnerAddressBytes, groupIndex);
        }

        internal static void ReadGroupAddress(ReadOnlySpan<byte> source,
            out MerkabaTileAddress tile, out int kernelLocal,
            out uint groupIndex)
        {
            RequireSize(source, GroupAddressBytes);
            ReadOwnerAddress(source.Slice(0, OwnerAddressBytes), out tile,
                out kernelLocal);
            groupIndex = ReadUInt32(source, OwnerAddressBytes);
        }

        private static uint Update(uint crc, ReadOnlySpan<byte> bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
                crc = UpdateByte(crc, bytes[i]);
            return crc;
        }

        internal static bool HasCanonicalRecordShape(MerkabaRecordKind kind,
            int addressBytes, int payloadBytes)
        {
            try
            {
                ValidateRecordShape(kind, addressBytes, payloadBytes);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static uint UpdateUInt16(uint crc, ushort value)
        {
            crc = UpdateByte(crc, (byte)value);
            return UpdateByte(crc, (byte)(value >> 8));
        }

        private static uint UpdateUInt32(uint crc, uint value)
        {
            for (int shift = 0; shift < 32; shift += 8)
                crc = UpdateByte(crc, (byte)(value >> shift));
            return crc;
        }

        private static uint UpdateUInt64(uint crc, ulong value)
        {
            for (int shift = 0; shift < 64; shift += 8)
                crc = UpdateByte(crc, (byte)(value >> shift));
            return crc;
        }

        private static uint UpdateByte(uint crc, byte value)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xedb88320u &
                    (uint)-(int)(crc & 1u));
            return crc;
        }

        private static void RequireSize(ReadOnlySpan<byte> source, int size)
        {
            if (source.Length != size)
                throw new ArgumentException(
                    $"Address must be exactly {size} bytes.");
        }

        private static void RequireSize(Span<byte> destination, int size)
        {
            if (destination.Length != size)
                throw new ArgumentException(
                    $"Address must be exactly {size} bytes.");
        }

        internal static void WriteUInt16(Span<byte> bytes, int offset,
            ushort value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
        }

        internal static void WriteUInt32(Span<byte> bytes, int offset,
            uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        internal static void WriteInt32(Span<byte> bytes, int offset,
            int value) => WriteUInt32(bytes, offset, unchecked((uint)value));

        internal static void WriteUInt64(Span<byte> bytes, int offset,
            ulong value)
        {
            WriteUInt32(bytes, offset, (uint)value);
            WriteUInt32(bytes, offset + 4, (uint)(value >> 32));
        }

        internal static ushort ReadUInt16(ReadOnlySpan<byte> bytes,
            int offset) =>
            (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

        internal static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
            bytes[offset] | ((uint)bytes[offset + 1] << 8) |
            ((uint)bytes[offset + 2] << 16) |
            ((uint)bytes[offset + 3] << 24);

        internal static int ReadInt32(ReadOnlySpan<byte> bytes, int offset) =>
            unchecked((int)ReadUInt32(bytes, offset));

        internal static ulong ReadUInt64(ReadOnlySpan<byte> bytes, int offset) =>
            ReadUInt32(bytes, offset) |
            ((ulong)ReadUInt32(bytes, offset + 4) << 32);
    }
}
