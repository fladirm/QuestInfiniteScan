using System;
using System.Collections.Generic;
using System.IO;

namespace Genesis.RoomScan
{
    public sealed partial class MerkabaGrid
    {
        private static List<MerkabaAppendRecord> CollectWritebackRecords(
            IReadOnlyList<MerkabaTileSnapshot> tiles)
        {
            var records = new List<MerkabaAppendRecord>(tiles.Count * 4);
            foreach (MerkabaTileSnapshot tile in tiles)
            foreach (MerkabaAppendRecord record in tile.Sidecars)
            {
                MerkabaSphereFlowerReplayIndex.ValidateRecord(record);
                records.Add(record);
            }
            // Fine identities include owner and key; deduplicate only identical
            // records from the same frozen tile generation.
            records.Sort(CompareWritebackRecordIdentity);
            int unique = 0;
            for (int index = 0; index < records.Count; index++)
            {
                MerkabaAppendRecord record = records[index];
                if (unique != 0 && CompareWritebackRecordIdentity(
                        records[unique - 1], record) == 0)
                {
                    if (!records[unique - 1].Payload.AsSpan().SequenceEqual(record.Payload))
                        throw new InvalidDataException(
                            "One persistent identity changed inside a frozen writeback batch.");
                    continue;
                }
                records[unique++] = record;
            }
            if (unique < records.Count) records.RemoveRange(unique, records.Count - unique);
            return records;
        }

        private static int CompareWritebackRecordIdentity(MerkabaAppendRecord left,
            MerkabaAppendRecord right)
        {
            int order = left.Kind.CompareTo(right.Kind);
            if (order != 0) return order;
            order = left.Address.AsSpan().SequenceCompareTo(right.Address);
            if (order != 0) return order;
            switch (left.Kind)
            {
                case MerkabaRecordKind.FlowerOwnerEpoch:
                case MerkabaRecordKind.FlowerDetail:
                case MerkabaRecordKind.FlowerSkinMetricRun:
                case MerkabaRecordKind.ThreadRun:
                case MerkabaRecordKind.Tombstone:
                    order = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(left.Payload, 0)
                        .CompareTo(MerkabaSphereFlowerPersistenceAbi.ReadUInt32(right.Payload, 0));
                    if (order == 0 && left.Kind == MerkabaRecordKind.Tombstone)
                        order = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(left.Payload, 4)
                            .CompareTo(MerkabaSphereFlowerPersistenceAbi.ReadUInt32(right.Payload, 4));
                    return order;
                default:
                    return 0; // Group/program identity is already in its exact address.
            }
        }

    }
}
