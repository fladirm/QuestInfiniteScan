using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>
    /// One already encoded REV-B append record. The address and payload use the
    /// exact little-endian shapes frozen by MerkabaSphereFlowerPersistenceAbi.
    /// </summary>
    internal sealed class MerkabaAppendRecord
    {
        internal readonly MerkabaRecordKind Kind;
        internal readonly byte[] Address;
        internal readonly byte[] Payload;

        internal MerkabaAppendRecord(MerkabaRecordKind kind, byte[] address,
            byte[] payload)
        {
            Address = address ?? throw new ArgumentNullException(nameof(address));
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            MerkabaSphereFlowerPersistenceAbi.ValidateRecordShape(kind,
                address.Length, payload.Length);
            Kind = kind;
        }
    }

    internal readonly struct MerkabaOwnerAddress :
        IEquatable<MerkabaOwnerAddress>
    {
        internal readonly MerkabaTileAddress Tile;
        internal readonly int KernelLocal;

        internal MerkabaOwnerAddress(MerkabaTileAddress tile, int kernelLocal)
        {
            if ((uint)kernelLocal >= MerkabaSpatial.KernelsPerTile)
                throw new ArgumentOutOfRangeException(nameof(kernelLocal));
            Tile = tile;
            KernelLocal = kernelLocal;
        }

        public bool Equals(MerkabaOwnerAddress other) =>
            Tile.Equals(other.Tile) && KernelLocal == other.KernelLocal;
        public override bool Equals(object obj) =>
            obj is MerkabaOwnerAddress other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Tile,
            KernelLocal);
    }

    internal readonly struct MerkabaRecordVersion :
        IComparable<MerkabaRecordVersion>
    {
        internal readonly ulong Generation;
        internal readonly ulong Sequence;

        internal MerkabaRecordVersion(ulong generation, ulong sequence)
        {
            Generation = generation;
            Sequence = sequence;
        }

        public int CompareTo(MerkabaRecordVersion other)
        {
            int generation = Generation.CompareTo(other.Generation);
            return generation != 0 ? generation : Sequence.CompareTo(
                other.Sequence);
        }
    }

    /// <summary>
    /// Replayed subordinate state. It is keyed only by existing M8 addresses;
    /// invalid parent epochs and descendants hidden by uniform dual ancestors
    /// are ignored rather than becoming orphan authorities.
    /// </summary>
    internal sealed class MerkabaSphereFlowerReplayIndex
    {
        private readonly struct ChunkAddress : IEquatable<ChunkAddress>
        {
            internal readonly int3 Block;
            internal readonly int Local;

            internal ChunkAddress(int3 block, int local)
            {
                Block = block;
                Local = local;
            }

            public bool Equals(ChunkAddress other) =>
                math.all(Block == other.Block) && Local == other.Local;
            public override bool Equals(object obj) =>
                obj is ChunkAddress other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Block.x,
                Block.y, Block.z, Local);
        }

        private readonly struct FineAddress : IEquatable<FineAddress>
        {
            internal readonly MerkabaOwnerAddress Owner;
            internal readonly MerkabaRecordKind Kind;
            internal readonly uint LocalKey;

            internal FineAddress(MerkabaOwnerAddress owner,
                MerkabaRecordKind kind, uint localKey)
            {
                Owner = owner;
                Kind = kind;
                LocalKey = localKey;
            }

            public bool Equals(FineAddress other) => Owner.Equals(other.Owner) &&
                Kind == other.Kind && LocalKey == other.LocalKey;
            public override bool Equals(object obj) =>
                obj is FineAddress other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Owner,
                (int)Kind, LocalKey);
        }

        private sealed class VersionedPayload
        {
            internal readonly MerkabaRecordVersion Version;
            internal readonly byte[] Payload;

            internal VersionedPayload(MerkabaRecordVersion version,
                byte[] payload)
            {
                Version = version;
                Payload = payload;
            }
        }

        private readonly Dictionary<int3, VersionedPayload> _dualBlocks = new();
        private readonly Dictionary<int3, VersionedPayload> _dualChildren = new();
        private readonly Dictionary<ChunkAddress, VersionedPayload> _dualChunks =
            new();
        private readonly Dictionary<MerkabaTileAddress, VersionedPayload>
            _dualLeaves = new();
        private readonly Dictionary<int3, MerkabaRecordVersion>
            _blockDescendantFloors = new();
        private readonly Dictionary<ChunkAddress, MerkabaRecordVersion>
            _chunkDescendantFloors = new();
        private readonly Dictionary<MerkabaTileAddress, MerkabaRecordVersion>
            _leafFloors = new();
        private readonly Dictionary<int3, HashSet<int>> _chunksByBlock = new();
        private readonly Dictionary<ChunkAddress, HashSet<MerkabaTileAddress>>
            _leavesByChunk = new();
        private readonly Dictionary<int3, HashSet<int>> _leafChunksByBlock = new();
        private readonly Dictionary<MerkabaOwnerAddress, VersionedPayload>
            _epochs = new();
        private readonly Dictionary<MerkabaOwnerAddress, MerkabaRecordVersion>
            _ownerRebases = new();
        private readonly Dictionary<MerkabaOwnerAddress, HashSet<FineAddress>>
            _rebaseTombstonesRequired = new();
        private readonly Dictionary<FineAddress, VersionedPayload> _fine = new();
        private readonly Dictionary<MerkabaOwnerAddress, HashSet<FineAddress>>
            _fineByOwner = new();
        private readonly Dictionary<FineAddress, MerkabaRecordVersion>
            _tombstones = new();
        private readonly Dictionary<uint, VersionedPayload> _programs = new();

        internal int ValidFlowerDetailCount => CountValid(
            MerkabaRecordKind.FlowerDetail);
        internal int ValidThreadRunCount => CountValid(
            MerkabaRecordKind.ThreadRun);
        internal int ValidThreadResidualCount => CountValid(
            MerkabaRecordKind.ThreadResidual);
        internal int ThreadProgramCount => _programs.Count;

        internal long CanonicalDualRecordBytes
        {
            get
            {
                int implicitFullBlocks = 0;
                foreach (VersionedPayload block in _dualBlocks.Values)
                    if (BlockState(block) == MerkabaDualNodeState.AllFull)
                        implicitFullBlocks++;
                return checked(
                    (long)(_dualBlocks.Count - implicitFullBlocks) *
                    EncodedRecordBytes(
                        MerkabaSphereFlowerPersistenceAbi.BlockAddressBytes,
                        MerkabaDualBlockMeta.ByteSize) +
                    (long)_dualChildren.Count * EncodedRecordBytes(
                        MerkabaSphereFlowerPersistenceAbi.BlockAddressBytes,
                        MerkabaDualBlockChildren.ByteSize) +
                    (long)_dualChunks.Count * EncodedRecordBytes(
                        MerkabaSphereFlowerPersistenceAbi.ChunkAddressBytes,
                        MerkabaDualChunkPayload.ByteSize) +
                    (long)_dualLeaves.Count * EncodedRecordBytes(
                        MerkabaSphereFlowerPersistenceAbi.TileAddressBytes,
                        MerkabaDualLeaf.ByteSize));
            }
        }

        internal void Clear()
        {
            _dualBlocks.Clear();
            _dualChildren.Clear();
            _dualChunks.Clear();
            _dualLeaves.Clear();
            _blockDescendantFloors.Clear();
            _chunkDescendantFloors.Clear();
            _leafFloors.Clear();
            _chunksByBlock.Clear();
            _leavesByChunk.Clear();
            _leafChunksByBlock.Clear();
            _epochs.Clear();
            _ownerRebases.Clear();
            _rebaseTombstonesRequired.Clear();
            _fine.Clear();
            _fineByOwner.Clear();
            _tombstones.Clear();
            _programs.Clear();
        }

        internal void CopyFrom(MerkabaSphereFlowerReplayIndex source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (ReferenceEquals(this, source)) return;
            Clear();
            Copy(source._dualBlocks, _dualBlocks);
            Copy(source._dualChildren, _dualChildren);
            Copy(source._dualChunks, _dualChunks);
            Copy(source._dualLeaves, _dualLeaves);
            CopyVersions(source._blockDescendantFloors,
                _blockDescendantFloors);
            CopyVersions(source._chunkDescendantFloors,
                _chunkDescendantFloors);
            CopyVersions(source._leafFloors, _leafFloors);
            foreach (ChunkAddress chunk in _dualChunks.Keys) IndexChunk(chunk);
            foreach (MerkabaTileAddress leaf in _dualLeaves.Keys) IndexLeaf(leaf);
            Copy(source._epochs, _epochs);
            CopyVersions(source._ownerRebases, _ownerRebases);
            foreach (KeyValuePair<MerkabaOwnerAddress, HashSet<FineAddress>> pair
                     in source._rebaseTombstonesRequired)
                _rebaseTombstonesRequired.Add(pair.Key,
                    new HashSet<FineAddress>(pair.Value));
            Copy(source._fine, _fine);
            foreach (FineAddress address in _fine.Keys)
                IndexFineAddress(address);
            foreach (KeyValuePair<FineAddress, MerkabaRecordVersion> pair in
                     source._tombstones)
                _tombstones.Add(pair.Key, pair.Value);
            Copy(source._programs, _programs);
        }

        internal void ValidateClosedHierarchy(
            Func<MerkabaOwnerAddress, bool> isCanonicalR1Owner)
        {
            if (isCanonicalR1Owner == null) throw new ArgumentNullException(
                nameof(isCanonicalR1Owner));

            var dualBlocks = new HashSet<int3>(_dualBlocks.Keys);
            foreach (int3 block in _dualChildren.Keys) dualBlocks.Add(block);
            foreach (int3 block in _chunksByBlock.Keys) dualBlocks.Add(block);
            foreach (int3 block in _leafChunksByBlock.Keys) dualBlocks.Add(block);
            foreach (int3 block in dualBlocks)
                ValidateDualBlock(block);
            var fineOwners = new HashSet<MerkabaOwnerAddress>(_epochs.Keys);
            foreach (MerkabaOwnerAddress owner in _fineByOwner.Keys)
                fineOwners.Add(owner);
            foreach (MerkabaOwnerAddress owner in fineOwners)
                ValidateFineOwner(owner, isCanonicalR1Owner);
            ValidateThreadProgramReferences();
        }

        /// <summary>
        /// Enumerates the one canonical sparse dual hierarchy in deterministic
        /// signed-address order. Parent records always precede the exact children
        /// whose MIXED bits require them. This is the base-compaction image of the
        /// existing M8-addressed dual, not a second coordinate hierarchy.
        /// </summary>
        internal IEnumerable<MerkabaAppendRecord> CanonicalDualRecords()
        {
            var allBlocks = new HashSet<int3>(_dualBlocks.Keys);
            foreach (int3 block in _dualChildren.Keys) allBlocks.Add(block);
            foreach (ChunkAddress chunk in _dualChunks.Keys)
                allBlocks.Add(chunk.Block);
            foreach (MerkabaTileAddress leaf in _dualLeaves.Keys)
                allBlocks.Add(leaf.BlockCoord);
            var blocks = new List<int3>(allBlocks);
            blocks.Sort(CompareBlock);

            // Validate the complete image before yielding its first byte so a
            // malformed hierarchy can never create a partial base segment.
            foreach (int3 block in blocks) ValidateDualBlock(block);

            foreach (int3 block in blocks)
            {
                VersionedPayload blockRecord = _dualBlocks[block];
                if (BlockState(blockRecord) == MerkabaDualNodeState.AllFull)
                    continue;
                yield return DualRecord(MerkabaRecordKind.DualBlock, block,
                    0, default, blockRecord.Payload);
                if (BlockState(blockRecord) != MerkabaDualNodeState.Mixed)
                    continue;

                VersionedPayload children = _dualChildren[block];
                yield return DualRecord(
                    MerkabaRecordKind.DualBlockChildren, block, 0, default,
                    children.Payload);
                for (int chunkLocal = 0;
                     chunkLocal < MerkabaSpatial.BlockChunkCount; chunkLocal++)
                {
                    if (PackedDualState(children.Payload, chunkLocal) !=
                        MerkabaDualNodeState.Mixed) continue;
                    var chunkAddress = new ChunkAddress(block, chunkLocal);
                    VersionedPayload chunk = _dualChunks[chunkAddress];
                    yield return DualRecord(MerkabaRecordKind.DualChunk, block,
                        chunkLocal, default, chunk.Payload);

                    ulong mixed = Read64(chunk.Payload, 8);
                    for (int tileLocal = 0;
                         tileLocal < MerkabaSpatial.TilesPerChunk; tileLocal++)
                    {
                        if ((mixed & (1ul << tileLocal)) == 0ul) continue;
                        var tile = new MerkabaTileAddress(block,
                            (uint)(chunkLocal | (tileLocal << 9)));
                        yield return DualRecord(MerkabaRecordKind.DualLeaf,
                            block, 0, tile, _dualLeaves[tile].Payload);
                    }
                }
            }
        }

        internal static void AccumulateTouched(
            IReadOnlyList<MerkabaAppendRecord> records,
            ISet<int3> blocks, ISet<MerkabaOwnerAddress> owners)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (blocks == null) throw new ArgumentNullException(nameof(blocks));
            if (owners == null) throw new ArgumentNullException(nameof(owners));
            foreach (MerkabaAppendRecord record in records)
            {
                switch (record.Kind)
                {
                    case MerkabaRecordKind.DualBlock:
                    case MerkabaRecordKind.DualBlockChildren:
                        blocks.Add(
                            MerkabaSphereFlowerPersistenceAbi.ReadBlockAddress(
                                record.Address));
                        break;
                    case MerkabaRecordKind.DualChunk:
                        MerkabaSphereFlowerPersistenceAbi.ReadChunkAddress(
                            record.Address, out int3 chunkBlock, out _);
                        blocks.Add(chunkBlock);
                        break;
                    case MerkabaRecordKind.DualLeaf:
                        blocks.Add(MerkabaSphereFlowerPersistenceAbi
                            .ReadTileAddress(record.Address).BlockCoord);
                        break;
                    case MerkabaRecordKind.FlowerOwnerEpoch:
                        MerkabaTileAddress epochTile =
                            MerkabaSphereFlowerPersistenceAbi.ReadTileAddress(
                                record.Address);
                        owners.Add(new MerkabaOwnerAddress(epochTile,
                            checked((int)Read32(record.Payload, 0))));
                        break;
                    case MerkabaRecordKind.FlowerDetail:
                    case MerkabaRecordKind.ThreadRun:
                    case MerkabaRecordKind.ThreadResidual:
                    case MerkabaRecordKind.Tombstone:
                        MerkabaSphereFlowerPersistenceAbi.ReadOwnerAddress(
                            record.Address, out MerkabaTileAddress ownerTile,
                            out int kernelLocal);
                        owners.Add(new MerkabaOwnerAddress(ownerTile,
                            kernelLocal));
                        break;
                }
            }
        }

        internal void ValidateDualBlocks(IEnumerable<int3> blocks)
        {
            if (blocks == null) throw new ArgumentNullException(nameof(blocks));
            foreach (int3 block in blocks) ValidateDualBlock(block);
        }

        internal void ValidateFineOwners(
            IEnumerable<MerkabaOwnerAddress> owners,
            Func<MerkabaOwnerAddress, bool> isCanonicalR1Owner)
        {
            if (owners == null) throw new ArgumentNullException(nameof(owners));
            if (isCanonicalR1Owner == null) throw new ArgumentNullException(
                nameof(isCanonicalR1Owner));
            foreach (MerkabaOwnerAddress owner in owners)
                ValidateFineOwner(owner, isCanonicalR1Owner);
        }

        internal void ValidateFineOwnersInTiles(
            IEnumerable<MerkabaTileAddress> tiles,
            Func<MerkabaOwnerAddress, bool> isCanonicalR1Owner)
        {
            if (tiles == null) throw new ArgumentNullException(nameof(tiles));
            if (isCanonicalR1Owner == null) throw new ArgumentNullException(
                nameof(isCanonicalR1Owner));
            foreach (MerkabaTileAddress tile in tiles)
                for (int kernel = 0;
                     kernel < MerkabaSpatial.KernelsPerTile; kernel++)
                {
                    var owner = new MerkabaOwnerAddress(tile, kernel);
                    if (_fineByOwner.ContainsKey(owner))
                        ValidateFineOwner(owner, isCanonicalR1Owner);
                }
        }

        internal void Apply(MerkabaAppendRecord record,
            MerkabaRecordVersion version)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            ValidatePayload(record);
            switch (record.Kind)
            {
                case MerkabaRecordKind.DualBlock:
                    ApplyDualBlock(record, version);
                    return;
                case MerkabaRecordKind.DualBlockChildren:
                    ApplyDualChildren(record, version);
                    return;
                case MerkabaRecordKind.DualChunk:
                    ApplyDualChunk(record, version);
                    return;
                case MerkabaRecordKind.DualLeaf:
                    ApplyDualLeaf(record, version);
                    return;
                case MerkabaRecordKind.FlowerOwnerEpoch:
                    ApplyOwnerEpoch(record, version);
                    return;
                case MerkabaRecordKind.FlowerDetail:
                case MerkabaRecordKind.ThreadRun:
                case MerkabaRecordKind.ThreadResidual:
                    ApplyFine(record, version);
                    return;
                case MerkabaRecordKind.ThreadProgram:
                    Put(_programs,
                        MerkabaSphereFlowerPersistenceAbi.ReadProgramAddress(
                            record.Address), version, record.Payload);
                    return;
                case MerkabaRecordKind.Tombstone:
                    ApplyTombstone(record, version);
                    return;
                default:
                    throw new InvalidDataException(
                        "M8 tile records do not belong to subordinate replay.");
            }
        }

        internal static void ValidateRecord(MerkabaAppendRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            ValidatePayload(record);
        }

        internal bool TryGetOwnerEpoch(MerkabaTileAddress tile,
            int kernelLocal, out uint epoch)
        {
            if (_epochs.TryGetValue(new MerkabaOwnerAddress(tile, kernelLocal),
                    out VersionedPayload record))
            {
                epoch = Read32(record.Payload, 4);
                return epoch != 0u;
            }
            epoch = 0u;
            return false;
        }

        internal bool TryGetFine(MerkabaTileAddress tile, int kernelLocal,
            MerkabaRecordKind kind, uint localKey, out byte[] payload)
        {
            var owner = new MerkabaOwnerAddress(tile, kernelLocal);
            var key = new FineAddress(owner, kind, localKey);
            if (!_fine.TryGetValue(key, out VersionedPayload value) ||
                (_tombstones.TryGetValue(key, out MerkabaRecordVersion deleted) &&
                 deleted.CompareTo(value.Version) >= 0) ||
                !TryGetOwnerEpoch(tile, kernelLocal, out uint epoch) ||
                ParentEpoch(kind, value.Payload) != epoch)
            {
                payload = null;
                return false;
            }
            payload = (byte[])value.Payload.Clone();
            return true;
        }

        internal MerkabaDualReadResult ReadDual(MerkabaTileAddress tile,
            int kernelLocal)
        {
            if ((uint)kernelLocal >= MerkabaSpatial.KernelsPerTile)
                throw new ArgumentOutOfRangeException(nameof(kernelLocal));
            if (!_dualBlocks.TryGetValue(tile.BlockCoord,
                    out VersionedPayload blockRecord))
                return MerkabaDualReadResult.CertainFull;
            MerkabaDualNodeState blockState = BlockState(blockRecord);
            if (blockState != MerkabaDualNodeState.Mixed)
                return Result(blockState);
            if (!_dualChildren.TryGetValue(tile.BlockCoord,
                    out VersionedPayload children))
                return MerkabaDualReadResult.AmbiguousCold;
            MerkabaDualNodeState chunkState = PackedDualState(children.Payload,
                tile.ChunkLocal);
            if (chunkState != MerkabaDualNodeState.Mixed)
                return Result(chunkState);
            var chunkKey = new ChunkAddress(tile.BlockCoord, tile.ChunkLocal);
            if (!_dualChunks.TryGetValue(chunkKey,
                    out VersionedPayload chunk))
                return MerkabaDualReadResult.AmbiguousCold;
            ulong nonFull = Read64(chunk.Payload, 0);
            ulong mixed = Read64(chunk.Payload, 8);
            ulong tileBit = 1ul << tile.TileLocal;
            MerkabaDualNodeState tileState = (nonFull & tileBit) == 0ul
                ? MerkabaDualNodeState.AllFull
                : (mixed & tileBit) == 0ul
                    ? MerkabaDualNodeState.AllThrough
                    : MerkabaDualNodeState.Mixed;
            if (tileState != MerkabaDualNodeState.Mixed)
                return Result(tileState);
            if (!_dualLeaves.TryGetValue(tile, out VersionedPayload leaf))
                return MerkabaDualReadResult.AmbiguousCold;
            uint word = Read32(leaf.Payload, (kernelLocal >> 5) * 4);
            return ((word >> (kernelLocal & 31)) & 1u) != 0u
                ? MerkabaDualReadResult.CertainThrough
                : MerkabaDualReadResult.CertainFull;
        }

        private void ValidateDualBlock(int3 block)
        {
            bool hasChildren = _dualChildren.TryGetValue(block,
                out VersionedPayload children);
            bool hasChunks = _chunksByBlock.TryGetValue(block,
                out HashSet<int> chunkLocals) && chunkLocals.Count != 0;
            bool hasLeaves = _leafChunksByBlock.TryGetValue(block,
                out HashSet<int> leafChunks) && leafChunks.Count != 0;
            if (!_dualBlocks.TryGetValue(block, out VersionedPayload blockRecord))
            {
                if (hasChildren || hasChunks || hasLeaves)
                    throw new InvalidDataException(
                        "Dual descendants exist without a block node.");
                return;
            }
            if (BlockState(blockRecord) != MerkabaDualNodeState.Mixed)
            {
                if (hasChildren || hasChunks || hasLeaves)
                    throw new InvalidDataException(
                        "A uniform dual block retains descendant payloads.");
                return;
            }
            if (!hasChildren)
                throw new InvalidDataException(
                    "A MIXED dual block has no child-state payload.");
            bool everyChunkFull = true;
            bool everyChunkThrough = true;
            for (int child = 0;
                 child < MerkabaSpatial.BlockChunkCount; child++)
            {
                var chunkAddress = new ChunkAddress(block, child);
                MerkabaDualNodeState childState = PackedDualState(
                    children.Payload, child);
                everyChunkFull &= childState == MerkabaDualNodeState.AllFull;
                everyChunkThrough &= childState ==
                    MerkabaDualNodeState.AllThrough;
                bool childIsMixed = childState == MerkabaDualNodeState.Mixed;
                bool hasChunk = _dualChunks.TryGetValue(chunkAddress,
                    out VersionedPayload chunk);
                if (childIsMixed != hasChunk)
                    throw new InvalidDataException(
                        childIsMixed
                            ? "A MIXED dual chunk has no tile-state payload."
                            : "A uniform dual chunk retains a tile-state payload.");
                if (!childIsMixed) continue;
                ulong nonFull = Read64(chunk.Payload, 0);
                ulong mixed = Read64(chunk.Payload, 8);
                if (mixed == 0ul &&
                    (nonFull == 0ul || nonFull == ulong.MaxValue))
                    throw new InvalidDataException(
                        "A uniform dual chunk was not collapsed into its " +
                        "parent child-state payload.");
                for (int tileLocal = 0;
                     tileLocal < MerkabaSpatial.TilesPerChunk; tileLocal++)
                {
                    var tile = new MerkabaTileAddress(block,
                        (uint)(child | (tileLocal << 9)));
                    bool tileIsMixed = (mixed & (1ul << tileLocal)) != 0ul;
                    if (tileIsMixed != _dualLeaves.ContainsKey(tile))
                        throw new InvalidDataException(
                            tileIsMixed
                                ? "A MIXED dual tile has no SEE_THROUGH leaf."
                                : "A uniform dual tile retains a leaf payload.");
                }
            }
            if (everyChunkFull || everyChunkThrough)
                throw new InvalidDataException(
                    "A uniform dual block was not collapsed into its block " +
                    "header.");
        }

        private void ValidateFineOwner(MerkabaOwnerAddress owner,
            Func<MerkabaOwnerAddress, bool> isCanonicalR1Owner)
        {
            if (_ownerRebases.TryGetValue(owner,
                    out MerkabaRecordVersion rebase) &&
                _rebaseTombstonesRequired.TryGetValue(owner,
                    out HashSet<FineAddress> required))
                foreach (FineAddress address in required)
                    if (!_tombstones.TryGetValue(address,
                            out MerkabaRecordVersion tombstone) ||
                        tombstone.Generation != rebase.Generation)
                        throw new InvalidDataException(
                            "Flower owner epoch wrap requires same-transaction " +
                            "tombstones for every existing descendant.");
            bool hasEpoch = _epochs.ContainsKey(owner);
            if (!_fineByOwner.TryGetValue(owner,
                    out HashSet<FineAddress> addresses))
            {
                if (hasEpoch)
                    throw new InvalidDataException(
                        "A sparse Flower owner epoch has no fine-state " +
                        "history.");
                return;
            }
            if (!hasEpoch)
                throw new InvalidDataException(
                    "Persistent fine-state history has no sparse Flower " +
                    "owner epoch.");
            bool canonicalOwnerChecked = false;
            foreach (FineAddress address in addresses)
            {
                if (!TryGetFine(owner.Tile, owner.KernelLocal, address.Kind,
                        address.LocalKey, out byte[] payload)) continue;
                if (!canonicalOwnerChecked)
                {
                    if (!isCanonicalR1Owner(owner))
                        throw new InvalidDataException(
                            "Persistent fine truth has no canonical R1 owner.");
                    canonicalOwnerChecked = true;
                }
                if (address.Kind == MerkabaRecordKind.ThreadRun &&
                    !_programs.ContainsKey(Read32(payload, 4)))
                    throw new InvalidDataException(
                        "A live ThreadRun references no committed " +
                        "ThreadProgram.");
            }
        }

        private void ApplyDualBlock(MerkabaAppendRecord record,
            MerkabaRecordVersion version)
        {
            int3 block = MerkabaSphereFlowerPersistenceAbi.ReadBlockAddress(
                record.Address);
            _dualBlocks.TryGetValue(block, out VersionedPayload prior);
            if (prior != null && version.CompareTo(prior.Version) < 0) return;

            MerkabaDualNodeState previousState = prior == null
                ? MerkabaDualNodeState.AllFull : BlockState(prior);
            Put(_dualBlocks, block, version, record.Payload);
            MerkabaDualNodeState nextState = (MerkabaDualNodeState)(
                Read32(record.Payload, 0) & 3u);

            // A uniform ancestor is the exact tombstone for its complete
            // descendant range. A later uniform->MIXED expansion starts with no
            // children, so descendants hidden by the older uniform generation
            // can never become visible again.
            if (nextState != MerkabaDualNodeState.Mixed ||
                previousState != MerkabaDualNodeState.Mixed)
            {
                RaiseFloor(_blockDescendantFloors, block, version);
                RemoveBlockDescendants(block);
            }
        }

        private void ApplyDualChildren(MerkabaAppendRecord record,
            MerkabaRecordVersion version)
        {
            int3 block = MerkabaSphereFlowerPersistenceAbi.ReadBlockAddress(
                record.Address);
            if (IsBeforeFloor(_blockDescendantFloors, block, version)) return;
            _dualChildren.TryGetValue(block, out VersionedPayload prior);
            if (prior != null && version.CompareTo(prior.Version) < 0) return;
            byte[] previous = prior?.Payload;
            Put(_dualChildren, block, version, record.Payload);

            for (int child = 0; child < MerkabaSpatial.BlockChunkCount; child++)
            {
                MerkabaDualNodeState oldState = previous == null
                    ? MerkabaDualNodeState.AllFull
                    : PackedDualState(previous, child);
                MerkabaDualNodeState nextState = PackedDualState(record.Payload,
                    child);
                if (nextState != MerkabaDualNodeState.Mixed ||
                    oldState != MerkabaDualNodeState.Mixed)
                {
                    RaiseFloor(_chunkDescendantFloors,
                        new ChunkAddress(block, child), version);
                    RemoveChunkDescendants(block, child);
                }
            }
        }

        private void ApplyDualChunk(MerkabaAppendRecord record,
            MerkabaRecordVersion version)
        {
            MerkabaSphereFlowerPersistenceAbi.ReadChunkAddress(record.Address,
                out int3 block, out int chunkLocal);
            var key = new ChunkAddress(block, chunkLocal);
            if (IsBeforeFloor(_chunkDescendantFloors, key, version)) return;
            _dualChunks.TryGetValue(key, out VersionedPayload prior);
            if (prior != null && version.CompareTo(prior.Version) < 0) return;
            byte[] previous = prior?.Payload;
            Put(_dualChunks, key, version, record.Payload);
            if (_dualChunks.ContainsKey(key)) IndexChunk(key);

            ulong nextMixed = Read64(record.Payload, 8);
            ulong oldMixed = previous == null ? 0ul : Read64(previous, 8);
            for (int tileLocal = 0;
                 tileLocal < MerkabaSpatial.TilesPerChunk; tileLocal++)
            {
                ulong bit = 1ul << tileLocal;
                bool oldWasMixed = (oldMixed & bit) != 0ul;
                bool nextIsMixed = (nextMixed & bit) != 0ul;
                if (!nextIsMixed || !oldWasMixed)
                {
                    var tile = new MerkabaTileAddress(block,
                        (uint)(chunkLocal | (tileLocal << 9)));
                    RaiseFloor(_leafFloors, tile, version);
                    RemoveLeaf(tile);
                }
            }
        }

        private void ApplyDualLeaf(MerkabaAppendRecord record,
            MerkabaRecordVersion version)
        {
            MerkabaTileAddress tile =
                MerkabaSphereFlowerPersistenceAbi.ReadTileAddress(
                    record.Address);
            if (IsBeforeFloor(_leafFloors, tile, version)) return;
            Put(_dualLeaves, tile, version, record.Payload);
            if (_dualLeaves.ContainsKey(tile)) IndexLeaf(tile);
        }

        private void RemoveBlockDescendants(int3 block)
        {
            _dualChildren.Remove(block);
            if (_chunksByBlock.TryGetValue(block, out HashSet<int> chunks))
            {
                int[] locals = new int[chunks.Count];
                chunks.CopyTo(locals);
                foreach (int chunkLocal in locals)
                    RemoveChunkDescendants(block, chunkLocal);
            }
            if (_leafChunksByBlock.TryGetValue(block,
                    out HashSet<int> leafChunks))
            {
                int[] locals = new int[leafChunks.Count];
                leafChunks.CopyTo(locals);
                foreach (int chunkLocal in locals)
                    RemoveLeaves(new ChunkAddress(block, chunkLocal));
            }
            _chunksByBlock.Remove(block);
        }

        private void RemoveChunkDescendants(int3 block, int chunkLocal)
        {
            var chunk = new ChunkAddress(block, chunkLocal);
            _dualChunks.Remove(chunk);
            if (_chunksByBlock.TryGetValue(block, out HashSet<int> chunks))
            {
                chunks.Remove(chunkLocal);
                if (chunks.Count == 0) _chunksByBlock.Remove(block);
            }
            RemoveLeaves(chunk);
        }

        private void RemoveLeaves(ChunkAddress chunk)
        {
            if (!_leavesByChunk.TryGetValue(chunk,
                    out HashSet<MerkabaTileAddress> leaves)) return;
            foreach (MerkabaTileAddress leaf in leaves) _dualLeaves.Remove(leaf);
            _leavesByChunk.Remove(chunk);
            if (_leafChunksByBlock.TryGetValue(chunk.Block,
                    out HashSet<int> leafChunks))
            {
                leafChunks.Remove(chunk.Local);
                if (leafChunks.Count == 0)
                    _leafChunksByBlock.Remove(chunk.Block);
            }
        }

        private void RemoveLeaf(MerkabaTileAddress tile)
        {
            if (!_dualLeaves.Remove(tile)) return;
            var chunk = new ChunkAddress(tile.BlockCoord, tile.ChunkLocal);
            if (!_leavesByChunk.TryGetValue(chunk,
                    out HashSet<MerkabaTileAddress> leaves)) return;
            leaves.Remove(tile);
            if (leaves.Count != 0) return;
            _leavesByChunk.Remove(chunk);
            if (_leafChunksByBlock.TryGetValue(tile.BlockCoord,
                    out HashSet<int> leafChunks))
            {
                leafChunks.Remove(tile.ChunkLocal);
                if (leafChunks.Count == 0)
                    _leafChunksByBlock.Remove(tile.BlockCoord);
            }
        }

        private void IndexChunk(ChunkAddress chunk)
        {
            if (!_chunksByBlock.TryGetValue(chunk.Block,
                    out HashSet<int> chunks))
            {
                chunks = new HashSet<int>();
                _chunksByBlock.Add(chunk.Block, chunks);
            }
            chunks.Add(chunk.Local);
        }

        private void IndexLeaf(MerkabaTileAddress tile)
        {
            var chunk = new ChunkAddress(tile.BlockCoord, tile.ChunkLocal);
            if (!_leavesByChunk.TryGetValue(chunk,
                    out HashSet<MerkabaTileAddress> leaves))
            {
                leaves = new HashSet<MerkabaTileAddress>();
                _leavesByChunk.Add(chunk, leaves);
            }
            leaves.Add(tile);
            if (!_leafChunksByBlock.TryGetValue(tile.BlockCoord,
                    out HashSet<int> leafChunks))
            {
                leafChunks = new HashSet<int>();
                _leafChunksByBlock.Add(tile.BlockCoord, leafChunks);
            }
            leafChunks.Add(tile.ChunkLocal);
        }

        private static MerkabaDualNodeState BlockState(
            VersionedPayload record) => (MerkabaDualNodeState)(
            Read32(record.Payload, 0) & 3u);

        private void ApplyFine(MerkabaAppendRecord record,
            MerkabaRecordVersion version)
        {
            MerkabaSphereFlowerPersistenceAbi.ReadOwnerAddress(record.Address,
                out MerkabaTileAddress tile, out int kernel);
            uint localKey = Read32(record.Payload, 0);
            var address = new FineAddress(new MerkabaOwnerAddress(tile, kernel),
                record.Kind, localKey);
            Put(_fine, address, version, record.Payload);
            if (_fine.ContainsKey(address)) IndexFineAddress(address);
        }

        private void ApplyOwnerEpoch(MerkabaAppendRecord record,
            MerkabaRecordVersion version)
        {
            MerkabaTileAddress tile =
                MerkabaSphereFlowerPersistenceAbi.ReadTileAddress(record.Address);
            int kernel = checked((int)Read32(record.Payload, 0));
            var owner = new MerkabaOwnerAddress(tile, kernel);
            uint nextEpoch = Read32(record.Payload, 4);
            if (_epochs.TryGetValue(owner, out VersionedPayload prior))
            {
                if (version.CompareTo(prior.Version) < 0) return;
                uint previousEpoch = Read32(prior.Payload, 4);
                bool same = nextEpoch == previousEpoch;
                bool increment = previousEpoch != uint.MaxValue &&
                    nextEpoch == previousEpoch + 1u;
                bool rebase = previousEpoch == uint.MaxValue && nextEpoch == 1u;
                if (!same && !increment && !rebase)
                    throw new InvalidDataException(
                        "Flower owner epoch must remain stable, increment once, " +
                        "or transactionally rebase from 0xffffffff to 1.");
                if (rebase)
                {
                    _ownerRebases[owner] = version;
                    if (_fineByOwner.TryGetValue(owner,
                            out HashSet<FineAddress> descendants) &&
                        descendants.Count != 0)
                        _rebaseTombstonesRequired[owner] =
                            new HashSet<FineAddress>(descendants);
                    else
                        _rebaseTombstonesRequired.Remove(owner);
                }
            }
            else if (nextEpoch != 1u)
                throw new InvalidDataException(
                    "The first sparse Flower owner epoch must be 1.");
            Put(_epochs, owner, version, record.Payload);
        }

        private void IndexFineAddress(FineAddress address)
        {
            if (!_fineByOwner.TryGetValue(address.Owner,
                    out HashSet<FineAddress> addresses))
            {
                addresses = new HashSet<FineAddress>();
                _fineByOwner.Add(address.Owner, addresses);
            }
            addresses.Add(address);
        }

        private void ApplyTombstone(MerkabaAppendRecord record,
            MerkabaRecordVersion version)
        {
            MerkabaSphereFlowerPersistenceAbi.ReadOwnerAddress(record.Address,
                out MerkabaTileAddress tile, out int kernel);
            var target = (MerkabaRecordKind)Read32(record.Payload, 0);
            uint localKey = Read32(record.Payload, 4);
            var address = new FineAddress(new MerkabaOwnerAddress(tile, kernel),
                target, localKey);
            if (!_tombstones.TryGetValue(address,
                    out MerkabaRecordVersion prior) ||
                version.CompareTo(prior) >= 0)
                _tombstones[address] = version;
        }

        private int CountValid(MerkabaRecordKind kind)
        {
            int count = 0;
            foreach (FineAddress address in _fine.Keys)
                if (address.Kind == kind && TryGetFine(address.Owner.Tile,
                        address.Owner.KernelLocal, kind, address.LocalKey,
                        out _))
                    count++;
            return count;
        }

        private void ValidateThreadProgramReferences()
        {
            foreach (KeyValuePair<FineAddress, VersionedPayload> pair in _fine)
            {
                FineAddress address = pair.Key;
                if (address.Kind != MerkabaRecordKind.ThreadRun ||
                    !TryGetFine(address.Owner.Tile, address.Owner.KernelLocal,
                        address.Kind, address.LocalKey, out byte[] payload))
                    continue;
                uint programRef = Read32(payload, 4);
                if (!_programs.ContainsKey(programRef))
                    throw new InvalidDataException(
                        "A live ThreadRun references no committed ThreadProgram.");
            }
        }

        private static uint ParentEpoch(MerkabaRecordKind kind,
            byte[] payload) => kind switch
        {
            MerkabaRecordKind.FlowerDetail => Read32(payload, 12),
            MerkabaRecordKind.ThreadRun => Read32(payload, 12),
            MerkabaRecordKind.ThreadResidual => Read32(payload, 4),
            _ => 0u
        };

        private static MerkabaDualReadResult Result(
            MerkabaDualNodeState state) => state switch
        {
            MerkabaDualNodeState.AllFull =>
                MerkabaDualReadResult.CertainFull,
            MerkabaDualNodeState.AllThrough =>
                MerkabaDualReadResult.CertainThrough,
            _ => MerkabaDualReadResult.AmbiguousCold
        };

        private static MerkabaDualNodeState PackedDualState(byte[] payload,
            int child)
        {
            uint word = Read32(payload, (child >> 4) * 4);
            return (MerkabaDualNodeState)((word >> ((child & 15) * 2)) & 3u);
        }

        private static void ValidatePayload(MerkabaAppendRecord record)
        {
            byte[] payload = record.Payload;
            switch (record.Kind)
            {
                case MerkabaRecordKind.DualBlock:
                    uint stateAndGeneration = Read32(payload, 0);
                    var state = (MerkabaDualNodeState)(stateAndGeneration & 3u);
                    uint generation = stateAndGeneration >> 2;
                    uint payloadIndex = Read32(payload, 4);
                    if (state == MerkabaDualNodeState.Invalid || generation == 0u ||
                        ((state == MerkabaDualNodeState.Mixed) !=
                         (payloadIndex != MerkabaDualBlockMeta.NoPayload)))
                        throw new InvalidDataException(
                            "Noncanonical dual block record.");
                    break;
                case MerkabaRecordKind.DualBlockChildren:
                    for (int child = 0;
                         child < MerkabaSpatial.BlockChunkCount; child++)
                        if (PackedDualState(payload, child) ==
                            MerkabaDualNodeState.Invalid)
                            throw new InvalidDataException(
                                "Noncanonical dual block children record.");
                    break;
                case MerkabaRecordKind.DualChunk:
                    ulong nonFull = Read64(payload, 0);
                    ulong mixed = Read64(payload, 8);
                    uint leafRef = Read32(payload, 16);
                    if ((mixed & ~nonFull) != 0ul || Read32(payload, 20) == 0u ||
                        Read32(payload, 24) != 0u || Read32(payload, 28) != 0u ||
                        ((mixed != 0ul) !=
                         (leafRef != MerkabaDualBlockMeta.NoPayload)))
                        throw new InvalidDataException(
                            "Noncanonical dual chunk record.");
                    break;
                case MerkabaRecordKind.DualLeaf:
                    bool anyThrough = false;
                    bool anyFull = false;
                    for (int word = 0; word < MerkabaSpatial.TileWordCount;
                         word++)
                    {
                        uint bits = Read32(payload, word * 4);
                        anyThrough |= bits != 0u;
                        anyFull |= bits != uint.MaxValue;
                    }
                    if (!anyThrough || !anyFull)
                        throw new InvalidDataException(
                            "A persisted dual leaf must be MIXED.");
                    break;
                case MerkabaRecordKind.FlowerOwnerEpoch:
                    if (Read32(payload, 0) >= MerkabaSpatial.KernelsPerTile ||
                        Read32(payload, 4) == 0u)
                        throw new InvalidDataException(
                            "Noncanonical Flower owner epoch.");
                    break;
                case MerkabaRecordKind.FlowerDetail:
                    if (!MerkabaFlowerDetailKey.TryDecode(Read32(payload, 0),
                            out _) || ReadInt32(payload, 4) >
                        ReadInt32(payload, 8) || Read32(payload, 12) == 0u)
                        throw new InvalidDataException(
                            "Noncanonical Flower detail record.");
                    break;
                case MerkabaRecordKind.ThreadRun:
                    if (!MerkabaFlowerDetailKey.TryDecode(Read32(payload, 0),
                            out _) || Read32(payload, 12) == 0u)
                        throw new InvalidDataException(
                            "Noncanonical Thread run record.");
                    break;
                case MerkabaRecordKind.ThreadResidual:
                    if (Read32(payload, 4) == 0u || Read32(payload, 28) != 0u ||
                        !HalfIntervalsCanonical(payload, 8, 16))
                        throw new InvalidDataException(
                            "Noncanonical Thread residual record.");
                    break;
                case MerkabaRecordKind.ThreadProgram:
                    if ((Read32(payload, 12) &
                         ~(uint)MerkabaThreadProgramFlags.OpticalValid) != 0u ||
                        !HalfIntervalsCanonical(payload, 16, 24) ||
                        !HalfIntervalsCanonical(payload, 32, 40))
                        throw new InvalidDataException(
                            "Noncanonical Thread program record.");
                    break;
                case MerkabaRecordKind.Tombstone:
                    var target = (MerkabaRecordKind)Read32(payload, 0);
                    if (target != MerkabaRecordKind.FlowerDetail &&
                        target != MerkabaRecordKind.ThreadRun &&
                        target != MerkabaRecordKind.ThreadResidual)
                        throw new InvalidDataException(
                            "Noncanonical fine-state tombstone.");
                    break;
                default:
                    throw new InvalidDataException(
                        "Unexpected subordinate record kind.");
            }
        }

        private static void Put<TKey>(Dictionary<TKey, VersionedPayload> target,
            TKey key, MerkabaRecordVersion version, byte[] payload)
        {
            if (!target.TryGetValue(key, out VersionedPayload prior) ||
                version.CompareTo(prior.Version) >= 0)
                target[key] = new VersionedPayload(version,
                    (byte[])payload.Clone());
        }

        private static void Copy<TKey>(
            Dictionary<TKey, VersionedPayload> source,
            Dictionary<TKey, VersionedPayload> destination)
        {
            foreach (KeyValuePair<TKey, VersionedPayload> pair in source)
                destination.Add(pair.Key, new VersionedPayload(
                    pair.Value.Version, (byte[])pair.Value.Payload.Clone()));
        }

        private static void CopyVersions<TKey>(
            Dictionary<TKey, MerkabaRecordVersion> source,
            Dictionary<TKey, MerkabaRecordVersion> destination)
        {
            foreach (KeyValuePair<TKey, MerkabaRecordVersion> pair in source)
                destination.Add(pair.Key, pair.Value);
        }

        private static bool IsBeforeFloor<TKey>(
            Dictionary<TKey, MerkabaRecordVersion> floors, TKey key,
            MerkabaRecordVersion version) =>
            floors.TryGetValue(key, out MerkabaRecordVersion floor) &&
            version.CompareTo(floor) < 0;

        private static void RaiseFloor<TKey>(
            Dictionary<TKey, MerkabaRecordVersion> floors, TKey key,
            MerkabaRecordVersion version)
        {
            if (!floors.TryGetValue(key, out MerkabaRecordVersion floor) ||
                version.CompareTo(floor) > 0)
                floors[key] = version;
        }

        private static int EncodedRecordBytes(int addressBytes,
            int payloadBytes) => checked(MerkabaRecordHeader.ByteSize +
                addressBytes + payloadBytes);

        private static int CompareBlock(int3 left, int3 right)
        {
            int x = left.x.CompareTo(right.x);
            if (x != 0) return x;
            int y = left.y.CompareTo(right.y);
            return y != 0 ? y : left.z.CompareTo(right.z);
        }

        private static MerkabaAppendRecord DualRecord(MerkabaRecordKind kind,
            int3 block, int chunkLocal, MerkabaTileAddress tile,
            byte[] payload)
        {
            int addressBytes = kind switch
            {
                MerkabaRecordKind.DualBlock or
                    MerkabaRecordKind.DualBlockChildren =>
                    MerkabaSphereFlowerPersistenceAbi.BlockAddressBytes,
                MerkabaRecordKind.DualChunk =>
                    MerkabaSphereFlowerPersistenceAbi.ChunkAddressBytes,
                MerkabaRecordKind.DualLeaf =>
                    MerkabaSphereFlowerPersistenceAbi.TileAddressBytes,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            var address = new byte[addressBytes];
            switch (kind)
            {
                case MerkabaRecordKind.DualBlock:
                case MerkabaRecordKind.DualBlockChildren:
                    MerkabaSphereFlowerPersistenceAbi.WriteBlockAddress(address,
                        block);
                    break;
                case MerkabaRecordKind.DualChunk:
                    MerkabaSphereFlowerPersistenceAbi.WriteChunkAddress(address,
                        block, chunkLocal);
                    break;
                case MerkabaRecordKind.DualLeaf:
                    MerkabaSphereFlowerPersistenceAbi.WriteTileAddress(address,
                        tile);
                    break;
            }
            return new MerkabaAppendRecord(kind, address,
                (byte[])payload.Clone());
        }

        private static ushort Read16(byte[] bytes, int offset) =>
            MerkabaSphereFlowerPersistenceAbi.ReadUInt16(bytes, offset);

        private static uint Read32(byte[] bytes, int offset) =>
            MerkabaSphereFlowerPersistenceAbi.ReadUInt32(bytes, offset);

        private static int ReadInt32(byte[] bytes, int offset) =>
            MerkabaSphereFlowerPersistenceAbi.ReadInt32(bytes, offset);

        private static ulong Read64(byte[] bytes, int offset) =>
            MerkabaSphereFlowerPersistenceAbi.ReadUInt64(bytes, offset);

        private static bool HalfIntervalsCanonical(byte[] payload,
            int lowerOffset, int upperOffset)
        {
            for (int lane = 0; lane < 4; lane++)
            {
                ushort lower = Read16(payload, lowerOffset + lane * 2);
                ushort upper = Read16(payload, upperOffset + lane * 2);
                if (!HalfFinite(lower) || !HalfFinite(upper) ||
                    HalfToSingle(lower) > HalfToSingle(upper))
                    return false;
            }
            return true;
        }

        private static bool HalfFinite(ushort bits) =>
            (bits & 0x7c00u) != 0x7c00u;

        private static float HalfToSingle(ushort bits)
        {
            uint sign = (uint)(bits & 0x8000u) << 16;
            uint exponent = (uint)(bits >> 10) & 0x1fu;
            uint mantissa = (uint)bits & 0x3ffu;
            uint single;
            if (exponent == 0u)
            {
                if (mantissa == 0u)
                    single = sign;
                else
                {
                    int shift = 0;
                    while ((mantissa & 0x400u) == 0u)
                    {
                        mantissa <<= 1;
                        shift++;
                    }
                    mantissa &= 0x3ffu;
                    single = sign | (uint)(113 - shift) << 23 |
                        mantissa << 13;
                }
            }
            else
                single = sign | (exponent + 112u) << 23 | mantissa << 13;
            return BitConverter.Int32BitsToSingle(unchecked((int)single));
        }

    }
}
