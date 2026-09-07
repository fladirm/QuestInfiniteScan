using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>Transient exact FREE-side identity; never stored scan truth.</summary>
    internal readonly struct MerkabaDirtTriangle
    {
        internal readonly int3 Cell;
        internal readonly int Face;
        internal readonly int Half;

        internal MerkabaDirtTriangle(int3 cell, int face, int half)
        {
            if ((uint)face >= 6u || (uint)half >= 2u)
                throw new ArgumentOutOfRangeException(nameof(face));
            Cell = cell;
            Face = face;
            Half = half;
        }
    }

    /// <summary>
    /// Actual direct/COMPLETED footprint coverage in the shared face diagonal.
    /// A partially covered half must remain unresolved until exact clipping is
    /// defined; an unknown measured surface is not itself coverage.
    /// </summary>
    internal readonly struct MerkabaDirtFaceCoverage
    {
        internal readonly uint CoveredHalves;
        internal readonly uint UnresolvedHalves;

        internal MerkabaDirtFaceCoverage(uint coveredHalves,
            uint unresolvedHalves)
        {
            if (((coveredHalves | unresolvedHalves) & ~3u) != 0u ||
                (coveredHalves & unresolvedHalves) != 0u)
                throw new ArgumentException("Invalid DIRT half coverage.");
            CoveredHalves = coveredHalves;
            UnresolvedHalves = unresolvedHalves;
        }
    }

    /// <summary>
    /// Read-only bounded materialization of the exact §13.4 predicate. Uniform
    /// sparse nodes contribute only their boundary, not a dense world grid.
    /// All classification and face geometry use the shared Flower authority.
    /// </summary>
    internal static class MerkabaDirtExtraction
    {
        internal const int BatchTriangleCount = 4096;

        private readonly struct SupportBox
        {
            internal readonly int3 Minimum;
            internal readonly int3 Maximum;

            internal SupportBox(int3 minimum, int span)
            {
                Minimum = minimum;
                Maximum = new int3(checked(minimum.x + span - 1),
                    checked(minimum.y + span - 1),
                    checked(minimum.z + span - 1));
                // U+1 and the outside-neighbour support must both be exact.
                if (math.any(Minimum <= new int3(int.MinValue + 1)) ||
                    math.any(Maximum >= new int3(int.MaxValue - 1)))
                    throw new InvalidDataException(
                        "DIRT support exceeds the signed cell address domain.");
            }

            internal bool Contains(int3 value) =>
                math.all(value >= Minimum) && math.all(value <= Maximum);
        }

        internal static long Stream(MerkabaSphereFlowerReplayIndex replay,
            Func<int3, int, MerkabaDirtFaceCoverage> directCoverage,
            Action<IReadOnlyList<MerkabaDirtTriangle>> consume,
            CancellationToken cancellationToken = default)
        {
            if (replay == null) throw new ArgumentNullException(nameof(replay));
            if (directCoverage == null)
                throw new ArgumentNullException(nameof(directCoverage));
            if (consume == null) throw new ArgumentNullException(nameof(consume));
            var batch = new List<MerkabaDirtTriangle>(BatchTriangleCount);
            long count = 0;
            foreach (SupportBox box in ThroughSupportBoxes(replay))
                for (int face = 0; face < 6; face++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int axis = face >> 1;
                    int other0 = (axis + 1) % 3;
                    int other1 = (axis + 2) % 3;
                    int3 lower = box.Minimum - 1;
                    int3 upper = box.Maximum;
                    int3 cell = lower;
                    cell[axis] = (face & 1) == 0
                        ? upper[axis] : lower[axis];
                    for (int first = lower[other0]; first <= upper[other0]; first++)
                        for (int second = lower[other1]; second <= upper[other1]; second++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            cell[other0] = first;
                            cell[other1] = second;
                            if (!OwnsFreeCell(replay, box, cell)) continue;
                            int3 neighbour = cell +
                                MerkabaSphereFlowerAuthority.DirtFaceDirection(face);
                            if (ReadCell(replay, neighbour) !=
                                MerkabaSphereFlowerAuthority.ExcavationCellState.Full)
                                continue;
                            MerkabaDirtFaceCoverage coverage =
                                directCoverage(cell, face);
                            if (coverage.UnresolvedHalves != 0u)
                                throw new InvalidDataException(
                                    $"DIRT face {cell}/{face} has unresolved direct footprint coverage.");
                            for (int half = 0; half < 2; half++)
                            {
                                if ((coverage.CoveredHalves & (1u << half)) != 0u)
                                    continue;
                                batch.Add(new MerkabaDirtTriangle(cell, face, half));
                                count = checked(count + 1);
                                if (batch.Count != BatchTriangleCount) continue;
                                consume(batch);
                                batch.Clear();
                            }
                        }
                }
            cancellationToken.ThrowIfCancellationRequested();
            if (batch.Count != 0) consume(batch);
            return count;
        }

        private static bool OwnsFreeCell(MerkabaSphereFlowerReplayIndex replay,
            SupportBox box, int3 cell)
        {
            // Through boxes partition the certified kernel set. Its first
            // covering support assigns a boundary face to exactly one source
            // box without a second coordinate index or vertex welding.
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                    {
                        int3 kernel = cell + new int3(x, y, z);
                        if (ReadSupport(replay, kernel) ==
                            MerkabaDualReadResult.CertainThrough)
                            return box.Contains(kernel);
                    }
            return false;
        }

        private static MerkabaSphereFlowerAuthority.ExcavationCellState ReadCell(
            MerkabaSphereFlowerReplayIndex replay, int3 cell)
        {
            Span<MerkabaDualReadResult> supports =
                stackalloc MerkabaDualReadResult[8];
            for (int bit = 0; bit < 8; bit++)
                supports[bit] = ReadSupport(replay, cell +
                    new int3(bit & 1, (bit >> 1) & 1, (bit >> 2) & 1));
            return MerkabaSphereFlowerAuthority.ClassifyFreeCell(supports);
        }

        private static MerkabaDualReadResult ReadSupport(
            MerkabaSphereFlowerReplayIndex replay, int3 kernel)
        {
            MerkabaSpatial.Address address = MerkabaSpatial.Encode(kernel);
            return replay.ReadDual(new MerkabaTileAddress(address.BlockCoord,
                address.LocalAddress), address.KernelLocal);
        }

        private static IEnumerable<SupportBox> ThroughSupportBoxes(
            MerkabaSphereFlowerReplayIndex replay)
        {
            foreach (MerkabaAppendRecord record in replay.CanonicalDualRecords())
                switch (record.Kind)
                {
                    case MerkabaRecordKind.DualBlock:
                        if ((Read32(record.Payload, 0) & 3u) == 1u)
                        {
                            int3 block = MerkabaSphereFlowerPersistenceAbi
                                .ReadBlockAddress(record.Address);
                            yield return new SupportBox(BlockOrigin(block),
                                MerkabaSpatial.BlockKernelSpan);
                        }
                        break;
                    case MerkabaRecordKind.DualBlockChildren:
                    {
                        int3 block = MerkabaSphereFlowerPersistenceAbi
                            .ReadBlockAddress(record.Address);
                        for (int child = 0; child < 512; child++)
                            if (((Read32(record.Payload, (child >> 4) * 4) >>
                                  ((child & 15) * 2)) & 3u) == 1u)
                                yield return new SupportBox(
                                    Decode(block, (uint)child, 0), 32);
                        break;
                    }
                    case MerkabaRecordKind.DualChunk:
                    {
                        MerkabaSphereFlowerPersistenceAbi.ReadChunkAddress(
                            record.Address, out int3 block, out int child);
                        ulong through = Read64(record.Payload, 0) &
                            ~Read64(record.Payload, 8);
                        for (int tile = 0; tile < 64; tile++)
                            if ((through & (1ul << tile)) != 0ul)
                                yield return new SupportBox(Decode(
                                    block, (uint)(child | (tile << 9)), 0), 8);
                        break;
                    }
                    case MerkabaRecordKind.DualLeaf:
                    {
                        MerkabaTileAddress tile = MerkabaSphereFlowerPersistenceAbi
                            .ReadTileAddress(record.Address);
                        for (int kernel = 0; kernel < 512; kernel++)
                            if ((Read32(record.Payload, (kernel >> 5) * 4) &
                                 (1u << (kernel & 31))) != 0u)
                                yield return new SupportBox(Decode(
                                    tile.BlockCoord, tile.LocalAddress, kernel), 1);
                        break;
                    }
                }
        }

        private static int3 BlockOrigin(int3 block) => new(
            checked(block.x * MerkabaSpatial.BlockKernelSpan),
            checked(block.y * MerkabaSpatial.BlockKernelSpan),
            checked(block.z * MerkabaSpatial.BlockKernelSpan));

        private static int3 Decode(int3 block, uint localAddress, int kernel)
        {
            int3 origin = BlockOrigin(block);
            int3 local = MerkabaSpatial.Decode(int3.zero, localAddress, kernel);
            return new int3(checked(origin.x + local.x),
                checked(origin.y + local.y), checked(origin.z + local.z));
        }

        private static uint Read32(byte[] value, int offset) =>
            BinaryPrimitives.ReadUInt32LittleEndian(value.AsSpan(offset, 4));

        private static ulong Read64(byte[] value, int offset) =>
            BinaryPrimitives.ReadUInt64LittleEndian(value.AsSpan(offset, 8));
    }
}
