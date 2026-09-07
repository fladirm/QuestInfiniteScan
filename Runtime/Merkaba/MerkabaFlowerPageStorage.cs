using System;
using System.Collections.Generic;
using System.IO;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Bounded storage transport for canonical fine records. Page allocation,
    /// owner lookup tables and derived draw packets never enter this stream.
    /// Each uint4 header is (kind, kernelLocal, localKey/program, payloadBytes),
    /// followed by the exact persistent payload and zero padding to 16 bytes.
    /// </summary>
    internal static class MerkabaFlowerPageStorage
    {
        internal const int HeaderWords = 4;
        internal const int MaximumRecordWords = HeaderWords +
            MerkabaThreadColorGroup.ByteSize / sizeof(uint);
        internal const uint ProgramOwner = uint.MaxValue;

        internal static uint[] EncodeLoadRecords(MerkabaTileAddress tile,
            IReadOnlyList<MerkabaAppendRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            records = OrderLoadRecords(tile, records);
            int length = 0;
            foreach (MerkabaAppendRecord record in records)
            {
                if (record.Kind < MerkabaRecordKind.FlowerOwnerEpoch) continue;
                TransportIdentity(tile, record, out _, out _);
                length = checked(length + HeaderWords + PaddedWords(record.Payload.Length));
            }
            var words = new uint[length];
            int cursor = 0;
            foreach (MerkabaAppendRecord record in records)
            {
                if (record.Kind < MerkabaRecordKind.FlowerOwnerEpoch) continue;
                TransportIdentity(tile, record, out uint owner, out uint key);
                words[cursor] = (uint)record.Kind;
                words[cursor + 1] = owner;
                words[cursor + 2] = key;
                words[cursor + 3] = checked((uint)record.Payload.Length);
                for (int offset = 0; offset < record.Payload.Length; offset += sizeof(uint))
                    words[cursor + HeaderWords + offset / sizeof(uint)] =
                        MerkabaSphereFlowerPersistenceAbi.ReadUInt32(record.Payload, offset);
                cursor += HeaderWords + PaddedWords(record.Payload.Length);
            }
            return words;
        }

        // Owner-local preorder makes resident address relocation a bounded
        // streaming operation: a run is immediately followed by its exact
        // seven-child groups, and a Thread program immediately precedes its
        // referencing run. No physical allocator addresses are imported.
        private static IReadOnlyList<MerkabaAppendRecord> OrderLoadRecords(
            MerkabaTileAddress tile, IReadOnlyList<MerkabaAppendRecord> records)
        {
            var byIdentity = new Dictionary<(MerkabaRecordKind, uint, uint), MerkabaAppendRecord>();
            var epochs = new SortedDictionary<uint, MerkabaAppendRecord>();
            var ownerRecords = new Dictionary<uint, List<MerkabaAppendRecord>>();
            foreach (MerkabaAppendRecord record in records)
            {
                if (record.Kind < MerkabaRecordKind.FlowerOwnerEpoch) continue;
                TransportIdentity(tile, record, out uint owner, out uint key);
                var identity = (record.Kind, owner, key);
                if (byIdentity.TryGetValue(identity, out MerkabaAppendRecord previous))
                {
                    if (!previous.Payload.AsSpan().SequenceEqual(record.Payload))
                        throw new InvalidDataException("Fine load has conflicting canonical records.");
                    continue;
                }
                byIdentity.Add(identity, record);
                if (record.Kind == MerkabaRecordKind.FlowerOwnerEpoch) epochs.Add(owner, record);
                else if (record.Kind == MerkabaRecordKind.FlowerDetail ||
                    record.Kind == MerkabaRecordKind.FlowerSkinMetricRun ||
                    record.Kind == MerkabaRecordKind.ThreadRun)
                {
                    if (!ownerRecords.TryGetValue(owner, out List<MerkabaAppendRecord> list))
                        ownerRecords.Add(owner, list = new List<MerkabaAppendRecord>());
                    list.Add(record);
                }
            }
            var ordered = new List<MerkabaAppendRecord>(records.Count);
            foreach (KeyValuePair<uint, MerkabaAppendRecord> epoch in epochs)
            {
                ordered.Add(epoch.Value);
                if (!ownerRecords.TryGetValue(epoch.Key, out List<MerkabaAppendRecord> list)) continue;
                list.Sort((left, right) =>
                {
                    int kind = left.Kind.CompareTo(right.Kind);
                    return kind != 0 ? kind :
                        MerkabaSphereFlowerPersistenceAbi.ReadUInt32(left.Payload, 0).CompareTo(
                            MerkabaSphereFlowerPersistenceAbi.ReadUInt32(right.Payload, 0));
                });
                foreach (MerkabaAppendRecord run in list)
                {
                    if (run.Kind == MerkabaRecordKind.FlowerDetail)
                    {
                        ordered.Add(run);
                        continue;
                    }
                    bool thread = run.Kind == MerkabaRecordKind.ThreadRun;
                    if (thread)
                    {
                        uint program = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(run.Payload, 4);
                        if (program != MerkabaThreadRun.InvalidRef)
                        {
                            if (!byIdentity.TryGetValue((MerkabaRecordKind.ThreadProgram,
                                    ProgramOwner, program), out MerkabaAppendRecord programRecord))
                                throw new InvalidDataException("Fine load is missing its certified Thread program.");
                            ordered.Add(programRecord);
                        }
                    }
                    ordered.Add(run);
                    int splitOffset = thread ? 12 : 8;
                    uint low = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(run.Payload, splitOffset);
                    uint high = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(run.Payload, splitOffset + 4);
                    int count = MerkabaFlowerSkinSplitBits.GroupCount(low, high);
                    uint first = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(run.Payload, thread ? 8 : 4);
                    MerkabaRecordKind groupKind = thread ? MerkabaRecordKind.ThreadColorGroup :
                        MerkabaRecordKind.FlowerVGroup;
                    for (int group = 0; group < count; group++)
                    {
                        if (!byIdentity.TryGetValue((groupKind, epoch.Key, checked(first + (uint)group)),
                                out MerkabaAppendRecord value))
                            throw new InvalidDataException("Fine load is missing a scan-authored child group.");
                        ordered.Add(value);
                    }
                }
            }
            foreach (uint owner in ownerRecords.Keys)
                if (!epochs.ContainsKey(owner))
                    throw new InvalidDataException("Fine load has no parent owner epoch.");
            return ordered;
        }

        internal static MerkabaAppendRecord[] DecodeCaptureRecords(MerkabaTileAddress tile,
            ReadOnlySpan<uint> words)
        {
            var records = new List<MerkabaAppendRecord>();
            int cursor = 0;
            while (cursor < words.Length)
            {
                if (words.Length - cursor < HeaderWords)
                    throw new InvalidDataException("Truncated fine-page transport header.");
                uint rawKind = words[cursor];
                uint owner = words[cursor + 1];
                uint key = words[cursor + 2];
                uint length = words[cursor + 3];
                if (rawKind < (uint)MerkabaRecordKind.FlowerOwnerEpoch ||
                    rawKind > (uint)MerkabaRecordKind.ThreadColorGroup ||
                    length > MerkabaThreadColorGroup.ByteSize || (length & 3u) != 0u)
                    throw new InvalidDataException("Invalid fine-page transport record.");
                var kind = (MerkabaRecordKind)rawKind;
                int bodyWords = PaddedWords((int)length);
                if (bodyWords > words.Length - cursor - HeaderWords)
                    throw new InvalidDataException("Truncated fine-page transport payload.");
                bool rebased = kind == MerkabaRecordKind.FlowerOwnerEpoch && key == 1u;
                byte[] address = CreateAddress(tile, kind, owner, rebased ? 0u : key);
                var payload = new byte[(int)length];
                for (int offset = 0; offset < payload.Length; offset += sizeof(uint))
                    MerkabaSphereFlowerPersistenceAbi.WriteUInt32(payload, offset,
                        words[cursor + HeaderWords + offset / sizeof(uint)]);
                for (int word = payload.Length / sizeof(uint); word < bodyWords; word++)
                    if (words[cursor + HeaderWords + word] != 0u)
                        throw new InvalidDataException("Fine-page transport padding is nonzero.");
                // GPU capture emits an owner epoch only when hasFineHistory
                // is set. The SSD append still requires this entire tile's
                // completed image before accepting the receipt.
                var record = new MerkabaAppendRecord(kind, address, payload, rebased,
                    ownerEpochSnapshot: kind == MerkabaRecordKind.FlowerOwnerEpoch);
                TransportIdentity(tile, record, out uint decodedOwner, out uint decodedKey);
                if (decodedOwner != owner || decodedKey != (rebased ? 0u : key))
                    throw new InvalidDataException("Fine-page transport identity disagrees with its payload.");
                records.Add(record);
                cursor += HeaderWords + bodyWords;
            }
            return records.ToArray();
        }

        // A completed frozen image is authoritative about absence. Converting
        // that absence to tombstones must happen inside the SSD append lock,
        // against the same replay version that the resulting batch updates.
        internal static void AppendRemovedRecordTombstones(MerkabaTileAddress tile,
            IReadOnlyList<MerkabaAppendRecord> previous,
            IReadOnlyList<MerkabaAppendRecord> current,
            List<MerkabaAppendRecord> append)
        {
            var present = new Dictionary<(MerkabaRecordKind Kind, uint Owner, uint Key), byte[]>();
            foreach (MerkabaAppendRecord record in current)
            {
                if (record.Kind < MerkabaRecordKind.FlowerOwnerEpoch) continue;
                TransportIdentity(tile, record, out uint owner, out uint key);
                var identity = (record.Kind, owner, key);
                if (present.TryGetValue(identity, out byte[] payload))
                {
                    if (!payload.AsSpan().SequenceEqual(record.Payload))
                        throw new InvalidDataException("Complete fine image has conflicting record identities.");
                }
                else present.Add(identity, record.Payload);
            }
            foreach (MerkabaAppendRecord record in previous)
            {
                if (record.Kind < MerkabaRecordKind.FlowerOwnerEpoch ||
                    record.Kind == MerkabaRecordKind.ThreadProgram) continue;
                TransportIdentity(tile, record, out uint owner, out uint key);
                if (present.ContainsKey((record.Kind, owner, key))) continue;
                if (record.Kind == MerkabaRecordKind.FlowerOwnerEpoch)
                    throw new InvalidDataException("Complete fine image lost a persistent owner epoch.");
                var address = new byte[MerkabaSphereFlowerPersistenceAbi.OwnerAddressBytes];
                MerkabaSphereFlowerPersistenceAbi.WriteOwnerAddress(address, tile, (int)owner);
                var tombstone = new byte[MerkabaTombstoneRecord.ByteSize];
                MerkabaSphereFlowerPersistenceAbi.WriteUInt32(tombstone, 0, (uint)record.Kind);
                MerkabaSphereFlowerPersistenceAbi.WriteUInt32(tombstone, 4, key);
                append.Add(new MerkabaAppendRecord(MerkabaRecordKind.Tombstone, address, tombstone));
            }
        }

        private static int PaddedWords(int bytes) => ((bytes + 15) / 16) * 4;

        private static void TransportIdentity(MerkabaTileAddress tile,
            MerkabaAppendRecord record, out uint owner, out uint key)
        {
            MerkabaSphereFlowerReplayIndex.ValidateRecord(record);
            owner = 0u;
            key = 0u;
            MerkabaTileAddress actual;
            switch (record.Kind)
            {
                case MerkabaRecordKind.FlowerOwnerEpoch:
                    actual = MerkabaSphereFlowerPersistenceAbi.ReadTileAddress(record.Address);
                    owner = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(record.Payload, 0);
                    break;
                case MerkabaRecordKind.FlowerDetail:
                case MerkabaRecordKind.FlowerSkinMetricRun:
                case MerkabaRecordKind.ThreadRun:
                    MerkabaSphereFlowerPersistenceAbi.ReadOwnerAddress(record.Address,
                        out actual, out int kernel);
                    owner = checked((uint)kernel);
                    key = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(record.Payload, 0);
                    break;
                case MerkabaRecordKind.FlowerVGroup:
                case MerkabaRecordKind.ThreadColorGroup:
                    MerkabaSphereFlowerPersistenceAbi.ReadGroupAddress(record.Address,
                        out actual, out int groupOwner, out key);
                    owner = checked((uint)groupOwner);
                    break;
                case MerkabaRecordKind.ThreadProgram:
                    owner = ProgramOwner;
                    key = MerkabaSphereFlowerPersistenceAbi.ReadProgramAddress(record.Address);
                    return;
                default:
                    throw new InvalidDataException("A complete fine-page image contains only live canonical records.");
            }
            if (owner >= MerkabaSpatial.KernelsPerTile || !actual.Equals(tile))
                throw new InvalidDataException("Fine-page transport record belongs to another M8 tile.");
        }

        private static byte[] CreateAddress(MerkabaTileAddress tile,
            MerkabaRecordKind kind, uint owner, uint key)
        {
            if (kind == MerkabaRecordKind.ThreadProgram)
            {
                if (owner != ProgramOwner)
                    throw new InvalidDataException("Thread program transport has an M8 owner.");
                var programAddress = new byte[MerkabaSphereFlowerPersistenceAbi.ProgramAddressBytes];
                MerkabaSphereFlowerPersistenceAbi.WriteProgramAddress(programAddress, key);
                return programAddress;
            }
            if (owner >= MerkabaSpatial.KernelsPerTile)
                throw new InvalidDataException("Fine-page owner is outside its tile.");
            if (kind == MerkabaRecordKind.FlowerOwnerEpoch)
            {
                if (key != 0u) throw new InvalidDataException("Owner epoch has a local record key.");
                var tileAddress = new byte[MerkabaSphereFlowerPersistenceAbi.TileAddressBytes];
                MerkabaSphereFlowerPersistenceAbi.WriteTileAddress(tileAddress, tile);
                return tileAddress;
            }
            bool group = kind == MerkabaRecordKind.FlowerVGroup ||
                kind == MerkabaRecordKind.ThreadColorGroup;
            var address = new byte[group ? MerkabaSphereFlowerPersistenceAbi.GroupAddressBytes :
                MerkabaSphereFlowerPersistenceAbi.OwnerAddressBytes];
            if (group)
                MerkabaSphereFlowerPersistenceAbi.WriteGroupAddress(address, tile, (int)owner, key);
            else
                MerkabaSphereFlowerPersistenceAbi.WriteOwnerAddress(address, tile, (int)owner);
            return address;
        }
    }
}
