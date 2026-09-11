using System;
using System.Collections.Generic;
using System.IO;

namespace Genesis.RoomScan
{
    /// <summary>
    /// One already encoded REV-C append record. The address and payload use the
    /// exact little-endian shapes frozen by MerkabaSphereFlowerPersistenceAbi.
    /// </summary>
    internal sealed class MerkabaAppendRecord
    {
        internal readonly MerkabaRecordKind Kind;
        internal readonly byte[] Address;
        internal readonly byte[] Payload;
        // Capture-only receipt. It is expanded into canonical epoch/tombstone
        // records inside the append lock, never written as another ABI field.
        internal readonly bool OwnerEpochRebased;
        // An immutable fine image certifies that this sparse owner actually
        // had fine state, even if every descendant was invalidated before
        // writeback. This receipt is transport/index metadata, not log ABI.
        internal readonly bool OwnerEpochSnapshot;

        internal MerkabaAppendRecord(MerkabaRecordKind kind, byte[] address,
            byte[] payload, bool ownerEpochRebased = false,
            bool ownerEpochSnapshot = false)
        {
            Address = address ?? throw new ArgumentNullException(nameof(address));
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            MerkabaSphereFlowerPersistenceAbi.ValidateRecordShape(kind,
                address.Length, payload.Length);
            Kind = kind;
            if ((ownerEpochRebased || ownerEpochSnapshot) &&
                kind != MerkabaRecordKind.FlowerOwnerEpoch)
                throw new ArgumentException("Only an owner epoch can carry a capture receipt.");
            OwnerEpochRebased = ownerEpochRebased;
            OwnerEpochSnapshot = ownerEpochSnapshot;
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
    /// descendants with invalid parent epochs are never orphan authorities.
    /// </summary>
    internal sealed class MerkabaSphereFlowerReplayIndex
    {
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
            internal readonly bool OwnerHistoryCertified;

            internal VersionedPayload(MerkabaRecordVersion version,
                byte[] payload, bool ownerHistoryCertified = false)
            {
                Version = version;
                Payload = payload;
                OwnerHistoryCertified = ownerHistoryCertified;
            }
        }

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
        internal int ValidFlowerSkinMetricRunCount => CountValid(
            MerkabaRecordKind.FlowerSkinMetricRun);
        internal int ValidFlowerVGroupCount => CountValid(
            MerkabaRecordKind.FlowerVGroup);
        internal int ValidThreadRunCount => CountValid(
            MerkabaRecordKind.ThreadRun);
        internal int ValidThreadColorGroupCount => CountValid(
            MerkabaRecordKind.ThreadColorGroup);
        internal int ThreadProgramCount => _programs.Count;

        internal void Clear()
        {
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

            var fineOwners = new HashSet<MerkabaOwnerAddress>(_epochs.Keys);
            foreach (MerkabaOwnerAddress owner in _fineByOwner.Keys)
                fineOwners.Add(owner);
            foreach (MerkabaOwnerAddress owner in fineOwners)
                ValidateFineOwner(owner, isCanonicalR1Owner);
            ValidateThreadProgramReferences();
        }

        internal static void AccumulateTouched(
            IReadOnlyList<MerkabaAppendRecord> records,
            ISet<MerkabaOwnerAddress> owners)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (owners == null) throw new ArgumentNullException(nameof(owners));
            foreach (MerkabaAppendRecord record in records)
            {
                switch (record.Kind)
                {
                    case MerkabaRecordKind.FlowerOwnerEpoch:
                        MerkabaTileAddress epochTile =
                            MerkabaSphereFlowerPersistenceAbi.ReadTileAddress(
                                record.Address);
                        owners.Add(new MerkabaOwnerAddress(epochTile,
                            checked((int)Read32(record.Payload, 0))));
                        break;
                    case MerkabaRecordKind.FlowerDetail:
                    case MerkabaRecordKind.FlowerSkinMetricRun:
                    case MerkabaRecordKind.ThreadRun:
                    case MerkabaRecordKind.Tombstone:
                        MerkabaSphereFlowerPersistenceAbi.ReadOwnerAddress(
                            record.Address, out MerkabaTileAddress ownerTile,
                            out int kernelLocal);
                        owners.Add(new MerkabaOwnerAddress(ownerTile,
                            kernelLocal));
                        break;
                    case MerkabaRecordKind.FlowerVGroup:
                    case MerkabaRecordKind.ThreadColorGroup:
                        MerkabaSphereFlowerPersistenceAbi.ReadGroupAddress(
                            record.Address, out MerkabaTileAddress groupTile,
                            out int groupKernel, out _);
                        owners.Add(new MerkabaOwnerAddress(groupTile,
                            groupKernel));
                        break;
                }
            }
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
                case MerkabaRecordKind.FlowerOwnerEpoch:
                    ApplyOwnerEpoch(record, version);
                    return;
                case MerkabaRecordKind.FlowerDetail:
                case MerkabaRecordKind.FlowerSkinMetricRun:
                case MerkabaRecordKind.FlowerVGroup:
                case MerkabaRecordKind.ThreadRun:
                case MerkabaRecordKind.ThreadColorGroup:
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

        internal void ValidateAppendBatch(IReadOnlyList<MerkabaAppendRecord> records,
            ulong generation, ulong firstSequence)
        {
            // Apply has only one state-dependent rejection: owner epoch
            // transition. Copy those touched epochs, not the complete world,
            // and preflight the exact ordered batch before appending any byte.
            var staged = new MerkabaSphereFlowerReplayIndex();
            var owners = new HashSet<MerkabaOwnerAddress>();
            AccumulateTouched(records, owners);
            foreach (MerkabaOwnerAddress owner in owners)
                if (_epochs.TryGetValue(owner, out VersionedPayload epoch))
                    staged._epochs.Add(owner, epoch);
            ulong sequence = firstSequence;
            foreach (MerkabaAppendRecord record in records)
                staged.Apply(record, new MerkabaRecordVersion(generation,
                    checked(++sequence)));
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

        // Bounded residency/export packet: this tile's live owner-local fine
        // records. No whole-world copy or reconstruction is performed.
        internal MerkabaAppendRecord[] CaptureTile(MerkabaTileAddress tile)
        {
            var records = new List<MerkabaAppendRecord>();
            CaptureFineTile(tile, records);
            return records.ToArray();
        }

        internal void AppendOwnerRebaseRecords(MerkabaTileAddress tile, int kernel,
            List<MerkabaAppendRecord> records)
        {
            var owner = new MerkabaOwnerAddress(tile, kernel);
            byte[] address = new byte[MerkabaSphereFlowerPersistenceAbi.TileAddressBytes];
            MerkabaSphereFlowerPersistenceAbi.WriteTileAddress(address, tile);
            byte[] maximum = new byte[MerkabaFlowerOwnerEpoch.ByteSize];
            MerkabaSphereFlowerPersistenceAbi.WriteUInt32(maximum, 0, (uint)kernel);
            MerkabaSphereFlowerPersistenceAbi.WriteUInt32(maximum, 4, uint.MaxValue);
            byte[] initial = (byte[])maximum.Clone();
            MerkabaSphereFlowerPersistenceAbi.WriteUInt32(initial, 4, 1u);
            // A GPU snapshot can cross the wrap before its previous high epoch
            // was appended. Record the actual wrap boundary explicitly.
            records.Add(new MerkabaAppendRecord(MerkabaRecordKind.FlowerOwnerEpoch,
                address, maximum, ownerEpochSnapshot: true));
            records.Add(new MerkabaAppendRecord(MerkabaRecordKind.FlowerOwnerEpoch,
                address, initial, ownerEpochSnapshot: true));
            if (!_fineByOwner.TryGetValue(owner, out HashSet<FineAddress> history)) return;
            var ordered = new List<FineAddress>(history);
            ordered.Sort((left, right) =>
            {
                int kind = left.Kind.CompareTo(right.Kind);
                return kind != 0 ? kind : left.LocalKey.CompareTo(right.LocalKey);
            });
            foreach (FineAddress value in ordered)
            {
                byte[] target = new byte[MerkabaSphereFlowerPersistenceAbi.OwnerAddressBytes];
                MerkabaSphereFlowerPersistenceAbi.WriteOwnerAddress(target, tile, kernel);
                byte[] payload = new byte[MerkabaTombstoneRecord.ByteSize];
                MerkabaSphereFlowerPersistenceAbi.WriteUInt32(payload, 0, (uint)value.Kind);
                MerkabaSphereFlowerPersistenceAbi.WriteUInt32(payload, 4, value.LocalKey);
                records.Add(new MerkabaAppendRecord(MerkabaRecordKind.Tombstone, target, payload));
            }
        }

        private void CaptureFineTile(MerkabaTileAddress tile,
            List<MerkabaAppendRecord> records)
        {
            var programs = new HashSet<uint>();
            for (int kernel = 0; kernel < MerkabaSpatial.KernelsPerTile; kernel++)
            {
                var owner = new MerkabaOwnerAddress(tile, kernel);
                if (!_epochs.TryGetValue(owner, out VersionedPayload epoch))
                    continue;
                bool hasDescendantHistory = _fineByOwner.TryGetValue(owner,
                    out HashSet<FineAddress> values) && values.Count != 0;
                if (!epoch.OwnerHistoryCertified && !hasDescendantHistory)
                    throw new InvalidDataException(
                        "Cannot capture a sparse owner epoch without fine-state history.");
                byte[] ownerAddress = new byte[
                    MerkabaSphereFlowerPersistenceAbi.TileAddressBytes];
                MerkabaSphereFlowerPersistenceAbi.WriteTileAddress(
                    ownerAddress, tile);
                records.Add(new MerkabaAppendRecord(
                    MerkabaRecordKind.FlowerOwnerEpoch, ownerAddress,
                    (byte[])epoch.Payload.Clone(), ownerEpochSnapshot: true));
                if (!hasDescendantHistory) continue;
                var ordered = new List<FineAddress>(values);
                ordered.Sort((left, right) =>
                {
                    int kind = left.Kind.CompareTo(right.Kind);
                    return kind != 0 ? kind : left.LocalKey.CompareTo(
                        right.LocalKey);
                });
                foreach (FineAddress value in ordered)
                {
                    if (!TryGetFine(tile, kernel, value.Kind, value.LocalKey,
                            out byte[] payload)) continue;
                    bool isGroup = value.Kind == MerkabaRecordKind.FlowerVGroup ||
                        value.Kind == MerkabaRecordKind.ThreadColorGroup;
                    byte[] address = new byte[isGroup
                        ? MerkabaSphereFlowerPersistenceAbi.GroupAddressBytes
                        : MerkabaSphereFlowerPersistenceAbi.OwnerAddressBytes];
                    if (isGroup)
                        MerkabaSphereFlowerPersistenceAbi.WriteGroupAddress(
                            address, tile, kernel, value.LocalKey);
                    else
                        MerkabaSphereFlowerPersistenceAbi.WriteOwnerAddress(
                            address, tile, kernel);
                    records.Add(new MerkabaAppendRecord(value.Kind,
                        address, payload));
                    if (value.Kind == MerkabaRecordKind.ThreadRun)
                    {
                        uint program = Read32(payload, 4);
                        if (program != MerkabaThreadRun.InvalidRef)
                            programs.Add(program);
                    }
                }
            }
            var orderedPrograms = new List<uint>(programs);
            orderedPrograms.Sort();
            foreach (uint program in orderedPrograms)
            {
                if (!_programs.TryGetValue(program,
                        out VersionedPayload payload))
                    throw new InvalidDataException(
                        "Live ThreadRun has no optical program.");
                byte[] address = new byte[
                    MerkabaSphereFlowerPersistenceAbi.ProgramAddressBytes];
                MerkabaSphereFlowerPersistenceAbi.WriteProgramAddress(address,
                    program);
                records.Add(new MerkabaAppendRecord(
                    MerkabaRecordKind.ThreadProgram, address,
                    (byte[])payload.Payload.Clone()));
            }
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
                !IsFineReachable(key, value, epoch))
            {
                payload = null;
                return false;
            }
            payload = (byte[])value.Payload.Clone();
            return true;
        }

        private bool IsFineReachable(FineAddress address,
            VersionedPayload value, uint epoch)
        {
            if (_ownerRebases.TryGetValue(address.Owner, out MerkabaRecordVersion rebase) &&
                value.Version.Generation < rebase.Generation)
                return false;
            switch (address.Kind)
            {
                case MerkabaRecordKind.FlowerDetail:
                case MerkabaRecordKind.FlowerSkinMetricRun:
                case MerkabaRecordKind.ThreadRun:
                    return ParentEpoch(address.Kind, value.Payload) == epoch;
                case MerkabaRecordKind.FlowerVGroup:
                    return IsGroupReferenced(address, value, epoch,
                        MerkabaRecordKind.FlowerSkinMetricRun);
                case MerkabaRecordKind.ThreadColorGroup:
                    return IsGroupReferenced(address, value, epoch,
                        MerkabaRecordKind.ThreadRun);
                default:
                    return false;
            }
        }

        private bool IsGroupReferenced(FineAddress groupAddress,
            VersionedPayload group, uint epoch, MerkabaRecordKind runKind)
        {
            if (!_fineByOwner.TryGetValue(groupAddress.Owner,
                    out HashSet<FineAddress> addresses)) return false;
            foreach (FineAddress runAddress in addresses)
            {
                if (runAddress.Kind != runKind) continue;
                VersionedPayload run = _fine[runAddress];
                if ((_tombstones.TryGetValue(runAddress,
                         out MerkabaRecordVersion deleted) &&
                     deleted.CompareTo(run.Version) >= 0) ||
                    ParentEpoch(runKind, run.Payload) != epoch)
                    continue;
                int baseOffset = runKind ==
                    MerkabaRecordKind.FlowerSkinMetricRun ? 4 : 8;
                int splitOffset = runKind ==
                    MerkabaRecordKind.FlowerSkinMetricRun ? 8 : 12;
                uint groupBase = Read32(run.Payload, baseOffset);
                uint splitLow = Read32(run.Payload, splitOffset);
                uint splitHigh = Read32(run.Payload, splitOffset + 4);
                int count = MerkabaFlowerSkinSplitBits.GroupCount(splitLow,
                    splitHigh);
                if (count <= 0) continue;
                ulong first = groupBase;
                ulong end = first + (uint)count;
                if (groupAddress.LocalKey >= first &&
                    groupAddress.LocalKey < end &&
                    group.Version.Generation == run.Version.Generation)
                    return true;
            }
            return false;
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
            bool hasEpoch = _epochs.TryGetValue(owner, out VersionedPayload epoch);
            if (!_fineByOwner.TryGetValue(owner,
                    out HashSet<FineAddress> addresses))
            {
                if (hasEpoch && !epoch.OwnerHistoryCertified)
                    throw new InvalidDataException(
                        "Sparse Flower owner epoch has no fine-state history or complete capture receipt.");
                // Only a certified complete snapshot may coalesce the first
                // fine payload and its retirement before the first append.
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
                    Read32(payload, 4) != MerkabaThreadRun.InvalidRef &&
                    !_programs.ContainsKey(Read32(payload, 4)))
                    throw new InvalidDataException(
                        "A live ThreadRun references no committed " +
                        "ThreadProgram.");
                if (address.Kind == MerkabaRecordKind.FlowerSkinMetricRun ||
                    address.Kind == MerkabaRecordKind.ThreadRun)
                    ValidateRunGroups(owner, address.Kind, _fine[address]);
            }
        }

        private void ValidateRunGroups(MerkabaOwnerAddress owner,
            MerkabaRecordKind runKind, VersionedPayload run)
        {
            byte[] payload = run.Payload;
            int baseOffset = runKind == MerkabaRecordKind.FlowerSkinMetricRun
                ? 4 : 8;
            int splitOffset = runKind == MerkabaRecordKind.FlowerSkinMetricRun
                ? 8 : 12;
            uint groupBase = Read32(payload, baseOffset);
            int count = MerkabaFlowerSkinSplitBits.GroupCount(
                Read32(payload, splitOffset), Read32(payload, splitOffset + 4));
            if (count < 0)
                throw new InvalidDataException(
                    "A skin run contains a noncanonical split mask.");
            MerkabaRecordKind groupKind = runKind ==
                MerkabaRecordKind.FlowerSkinMetricRun
                ? MerkabaRecordKind.FlowerVGroup
                : MerkabaRecordKind.ThreadColorGroup;
            for (int group = 0; group < count; group++)
            {
                uint local = checked(groupBase + (uint)group);
                var address = new FineAddress(owner, groupKind, local);
                if (!_fine.TryGetValue(address, out VersionedPayload value) ||
                    value.Version.Generation != run.Version.Generation ||
                    (_tombstones.TryGetValue(address,
                         out MerkabaRecordVersion deleted) &&
                     deleted.CompareTo(value.Version) >= 0))
                    throw new InvalidDataException(
                        "A skin split run requires its own same-transaction " +
                        "seven-child groups.");
            }
        }

        private void ApplyFine(MerkabaAppendRecord record,
            MerkabaRecordVersion version)
        {
            MerkabaTileAddress tile;
            int kernel;
            uint localKey;
            if (record.Kind == MerkabaRecordKind.FlowerVGroup ||
                record.Kind == MerkabaRecordKind.ThreadColorGroup)
                MerkabaSphereFlowerPersistenceAbi.ReadGroupAddress(
                    record.Address, out tile, out kernel, out localKey);
            else
            {
                MerkabaSphereFlowerPersistenceAbi.ReadOwnerAddress(
                    record.Address, out tile, out kernel);
                localKey = Read32(record.Payload, 0);
            }
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
            bool historyCertified = record.OwnerEpochSnapshot;
            if (_epochs.TryGetValue(owner, out VersionedPayload prior))
            {
                if (version.CompareTo(prior.Version) < 0) return;
                uint previousEpoch = Read32(prior.Payload, 4);
                if (version.CompareTo(prior.Version) == 0 && nextEpoch != previousEpoch)
                    throw new InvalidDataException("Conflicting owner epoch snapshots share a record version.");
                bool same = nextEpoch == previousEpoch;
                bool increment = previousEpoch != uint.MaxValue &&
                    nextEpoch == previousEpoch + 1u;
                bool snapshotAdvance = record.OwnerEpochSnapshot && nextEpoch > previousEpoch;
                bool rebase = previousEpoch == uint.MaxValue && nextEpoch == 1u;
                if (!same && !increment && !snapshotAdvance && !rebase)
                    throw new InvalidDataException(
                        "Flower owner epoch must remain stable, advance one step, " +
                        "advance by a certified complete snapshot, or transactionally rebase.");
                historyCertified |= prior.OwnerHistoryCertified;
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
            else if (nextEpoch != 1u && !record.OwnerEpochSnapshot)
                throw new InvalidDataException(
                    "An initial epoch beyond 1 requires a certified complete fine snapshot.");
            Put(_epochs, owner, version, record.Payload, historyCertified);
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
                if (programRef != MerkabaThreadRun.InvalidRef &&
                    !_programs.ContainsKey(programRef))
                    throw new InvalidDataException(
                        "A live ThreadRun references no committed ThreadProgram.");
            }
        }

        private static uint ParentEpoch(MerkabaRecordKind kind,
            byte[] payload) => kind switch
        {
            MerkabaRecordKind.FlowerDetail => Read32(payload, 12),
            MerkabaRecordKind.FlowerSkinMetricRun => Read32(payload, 16),
            MerkabaRecordKind.ThreadRun => Read32(payload, 20),
            _ => 0u
        };

        private static void ValidatePayload(MerkabaAppendRecord record)
        {
            byte[] payload = record.Payload;
            switch (record.Kind)
            {
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
                case MerkabaRecordKind.FlowerSkinMetricRun:
                    if (!MerkabaFlowerL2Key.TryDecode(Read32(payload, 0),
                            out _) || Read32(payload, 4) ==
                        MerkabaFlowerSkinMetricRun.InvalidGroupBase ||
                        !MerkabaFlowerSkinSplitBits.SplitL2(
                            Read32(payload, 8)) ||
                        !MerkabaFlowerSkinSplitBits.IsCanonical(
                            Read32(payload, 8), Read32(payload, 12)) ||
                        !MerkabaFlowerSkinSplitBits.GroupRangeFits(
                            Read32(payload, 4), Read32(payload, 8),
                            Read32(payload, 12)) ||
                        Read32(payload, 16) == 0u ||
                        Read32(payload, 20) != 0u)
                        throw new InvalidDataException(
                            "Noncanonical Flower skin metric run.");
                    break;
                case MerkabaRecordKind.FlowerVGroup:
                    for (int child = 0; child < 7; child++)
                        if (ReadInt32(payload, child * 8) >
                            ReadInt32(payload, child * 8 + 4))
                            throw new InvalidDataException(
                                "Noncanonical Flower V interval group.");
                    break;
                case MerkabaRecordKind.ThreadRun:
                    uint threadSplitLow = Read32(payload, 12);
                    uint threadSplitHigh = Read32(payload, 16);
                    bool threadSplit = MerkabaFlowerSkinSplitBits.SplitL2(
                        threadSplitLow);
                    if (!MerkabaFlowerL2Key.TryDecode(Read32(payload, 0),
                            out _) || !MerkabaFlowerSkinSplitBits.IsCanonical(
                            threadSplitLow, threadSplitHigh) ||
                        threadSplit != (Read32(payload, 8) !=
                            MerkabaThreadRun.InvalidRef) ||
                        !MerkabaFlowerSkinSplitBits.GroupRangeFits(
                            Read32(payload, 8), threadSplitLow,
                            threadSplitHigh) ||
                        !threadSplit && Read32(payload, 4) ==
                            MerkabaThreadRun.InvalidRef ||
                        Read32(payload, 20) == 0u)
                        throw new InvalidDataException(
                            "Noncanonical Thread run record.");
                    break;
                case MerkabaRecordKind.ThreadColorGroup:
                    for (int child = 0; child < 7; child++)
                        if (!HalfIntervalsCanonical(payload, child * 16,
                                child * 16 + 8))
                            throw new InvalidDataException(
                                "Noncanonical Thread color interval group.");
                    break;
                case MerkabaRecordKind.ThreadProgram:
                    if ((Read32(payload, 0) &
                         ~(uint)MerkabaThreadProgramFlags.OpticalValid) != 0u ||
                        Read32(payload, 4) != 0u ||
                        Read32(payload, 8) != 0u ||
                        Read32(payload, 12) != 0u ||
                        !HalfIntervalsCanonical(payload, 16, 24) ||
                        !HalfIntervalsCanonical(payload, 32, 40) ||
                        (Read32(payload, 0) == 0u &&
                         !AllHalfZero(payload, 16, 16)))
                        throw new InvalidDataException(
                            "Noncanonical Thread program record.");
                    break;
                case MerkabaRecordKind.Tombstone:
                    var target = (MerkabaRecordKind)Read32(payload, 0);
                    if (target != MerkabaRecordKind.FlowerDetail &&
                        target != MerkabaRecordKind.FlowerSkinMetricRun &&
                        target != MerkabaRecordKind.FlowerVGroup &&
                        target != MerkabaRecordKind.ThreadRun &&
                        target != MerkabaRecordKind.ThreadColorGroup)
                        throw new InvalidDataException(
                            "Noncanonical fine-state tombstone.");
                    break;
                default:
                    throw new InvalidDataException(
                        "Unexpected subordinate record kind.");
            }
        }

        private static void Put<TKey>(Dictionary<TKey, VersionedPayload> target,
            TKey key, MerkabaRecordVersion version, byte[] payload,
            bool ownerHistoryCertified = false)
        {
            if (!target.TryGetValue(key, out VersionedPayload prior) ||
                version.CompareTo(prior.Version) >= 0)
                target[key] = new VersionedPayload(version,
                    (byte[])payload.Clone(), ownerHistoryCertified);
        }

        private static void Copy<TKey>(
            Dictionary<TKey, VersionedPayload> source,
            Dictionary<TKey, VersionedPayload> destination)
        {
            foreach (KeyValuePair<TKey, VersionedPayload> pair in source)
                destination.Add(pair.Key, new VersionedPayload(
                    pair.Value.Version, (byte[])pair.Value.Payload.Clone(),
                    pair.Value.OwnerHistoryCertified));
        }

        private static void CopyVersions<TKey>(
            Dictionary<TKey, MerkabaRecordVersion> source,
            Dictionary<TKey, MerkabaRecordVersion> destination)
        {
            foreach (KeyValuePair<TKey, MerkabaRecordVersion> pair in source)
                destination.Add(pair.Key, pair.Value);
        }

        private static ushort Read16(byte[] bytes, int offset) =>
            MerkabaSphereFlowerPersistenceAbi.ReadUInt16(bytes, offset);

        private static uint Read32(byte[] bytes, int offset) =>
            MerkabaSphereFlowerPersistenceAbi.ReadUInt32(bytes, offset);

        private static int ReadInt32(byte[] bytes, int offset) =>
            MerkabaSphereFlowerPersistenceAbi.ReadInt32(bytes, offset);

        private static bool AllHalfZero(byte[] bytes, int offset, int count)
        {
            for (int i = 0; i < count; i++)
                if (HalfToSingle(Read16(bytes, offset + 2 * i)) != 0f)
                    return false;
            return true;
        }

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
