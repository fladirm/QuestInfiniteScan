using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaSphereFlowerDataAbiTests
    {
        [Test]
        public void R2RecordRead_RequiresExactKeyEpochAndNonzeroInnovation()
        {
            var key = MerkabaFlowerDetailKey.Create(1, 0, 0, 3,
                MerkabaFlowerDetailKind.R2Phase, true, 0);
            var other = MerkabaFlowerDetailKey.Create(1, 1, 0, 3,
                MerkabaFlowerDetailKind.R2Phase, true, 0);
            foreach ((int lower, int upper) in new[] {
                (1, 3), (int.MaxValue - 2, int.MaxValue),
                (int.MinValue, int.MinValue + 2), (-3, -1) })
            {
                var record = MerkabaFlowerDetailRecord.Create(key, lower, upper, 7u);
                Assert.That(record.TryReadR2Phase(key, 7u, out int lo, out int hi), Is.True);
                Assert.That(lo, Is.EqualTo(lower));
                Assert.That(hi, Is.EqualTo(upper));
                Assert.That(record.TryReadR2Phase(other, 7u, out _, out _), Is.False);
                Assert.That(record.TryReadR2Phase(key, 0u, out _, out _), Is.False);
                Assert.That(record.TryReadR2Phase(key, 8u, out _, out _), Is.False);
            }
            foreach ((int lower, int upper) in new[] { (0, 0), (-1, 1), (0, 3), (-3, 0) })
            {
                var record = MerkabaFlowerDetailRecord.Create(key, lower, upper, 7u);
                Assert.That(record.TryReadR2Phase(key, 7u, out _, out _), Is.False);
            }
            var r3 = MerkabaFlowerDetailKey.Create(1, 0, 0, 3,
                MerkabaFlowerDetailKind.R3Phase, true, 0);
            Assert.That(MerkabaFlowerDetailRecord.Create(r3, 1, 3, 7u)
                .TryReadR2Phase(r3, 7u, out _, out _), Is.False);
        }

        [Test]
        public void PackedLayouts_AreExactAndKernelStateRemainsSixteenBytes()
        {
            Assert.That(MerkabaSphereFlowerDataAbi.R1SeedFlag,
                Is.EqualTo(1u << 1));
            AssertSize<KernelState>(16);
            AssertOffset<KernelState>(nameof(KernelState.OccupancyEvidence), 0);
            AssertOffset<KernelState>(nameof(KernelState.PackedColor), 4);
            AssertOffset<KernelState>(nameof(KernelState.ColorConfidence), 8);
            AssertOffset<KernelState>(nameof(KernelState.Flags), 12);

            AssertSize<MerkabaDualBlockMeta>(8);
            AssertSize<MerkabaDualBlockChildren>(128);
            AssertSize<MerkabaDualChunkPayload>(32);
            AssertSize<MerkabaDualLeaf>(64);
            AssertSize<MerkabaFlowerOwnerEpoch>(8);
            AssertSize<MerkabaFlowerDetailRecord>(16);
            AssertSize<MerkabaFlowerSkinMetricRun>(24);
            AssertSize<MerkabaFlowerVInterval>(8);
            AssertSize<MerkabaFlowerVGroup>(56);
            AssertSize<MerkabaThreadRun>(24);
            AssertSize<MerkabaThreadColorInterval>(16);
            AssertSize<MerkabaThreadColorGroup>(112);
            AssertSize<MerkabaThreadProgramRecord>(48);
            AssertSize<MerkabaFlowerSymbolKey>(16);
            AssertSize<MerkabaObservationRecord>(16);
            AssertSize<MerkabaRecordHeader>(28);
            AssertSize<MerkabaTombstoneRecord>(8);

            AssertOffset<MerkabaFlowerSkinMetricRun>(
                nameof(MerkabaFlowerSkinMetricRun.ParentEpoch), 16);
            AssertOffset<MerkabaFlowerVGroup>(
                nameof(MerkabaFlowerVGroup.Child6), 48);
            AssertOffset<MerkabaThreadRun>(nameof(MerkabaThreadRun.GroupBase),
                8);
            AssertOffset<MerkabaThreadRun>(nameof(MerkabaThreadRun.ParentEpoch),
                20);
            AssertOffset<MerkabaThreadColorGroup>(
                nameof(MerkabaThreadColorGroup.Child6), 96);
            AssertOffset<MerkabaRecordHeader>(
                nameof(MerkabaRecordHeader.CommitGeneration), 8);
            AssertOffset<MerkabaRecordHeader>(
                nameof(MerkabaRecordHeader.Crc32), 24);
        }

        [Test]
        public void DualBlockAndLeaf_ExpandMutateAndCollapseExactly()
        {
            var children = MerkabaDualBlockChildren.CreateUniform(
                MerkabaDualNodeState.AllFull);
            Assert.That(children.IsCanonical, Is.True);
            Assert.That(children.CollapseState(),
                Is.EqualTo(MerkabaDualNodeState.AllFull));
            for (int i = 0; i < MerkabaSpatial.BlockChunkCount; i++)
                children.Set(i, MerkabaDualNodeState.AllThrough);
            Assert.That(children.CollapseState(),
                Is.EqualTo(MerkabaDualNodeState.AllThrough));
            children.Set(257, MerkabaDualNodeState.Mixed);
            Assert.That(children.Get(257),
                Is.EqualTo(MerkabaDualNodeState.Mixed));
            Assert.That(children.CollapseState(),
                Is.EqualTo(MerkabaDualNodeState.Mixed));

            var leaf = MerkabaDualLeaf.CreateUniform(
                MerkabaDualNodeState.AllFull);
            Assert.That(leaf.CollapseState(),
                Is.EqualTo(MerkabaDualNodeState.AllFull));
            for (int i = 0; i < MerkabaSpatial.KernelsPerTile; i++)
                leaf.SetThrough(i, true);
            Assert.That(leaf.ThroughCount(), Is.EqualTo(512));
            Assert.That(leaf.CollapseState(),
                Is.EqualTo(MerkabaDualNodeState.AllThrough));
            leaf.SetThrough(511, false);
            Assert.That(leaf.IsThrough(511), Is.False);
            Assert.That(leaf.ThroughCount(), Is.EqualTo(511));
            Assert.That(leaf.CollapseState(),
                Is.EqualTo(MerkabaDualNodeState.Mixed));
        }

        [Test]
        public void DualChunk_DenseLeafRankAndUniformCollapseAreCanonical()
        {
            ulong mixed = (1ul << 0) | (1ul << 31) | (1ul << 32) |
                (1ul << 63);
            var chunk = MerkabaDualChunkPayload.Create(ulong.MaxValue,
                mixed, 100u, 7u);
            Assert.That(chunk.IsCanonical, Is.True);
            Assert.That(chunk.TryGetLeafRef(0, out uint leaf0), Is.True);
            Assert.That(leaf0, Is.EqualTo(100u));
            Assert.That(chunk.TryGetLeafRef(31, out uint leaf31), Is.True);
            Assert.That(leaf31, Is.EqualTo(101u));
            Assert.That(chunk.TryGetLeafRef(32, out uint leaf32), Is.True);
            Assert.That(leaf32, Is.EqualTo(102u));
            Assert.That(chunk.TryGetLeafRef(63, out uint leaf63), Is.True);
            Assert.That(leaf63, Is.EqualTo(103u));
            Assert.That(chunk.TryGetLeafRef(4, out _), Is.False);
            Assert.That(chunk.GetTileState(4),
                Is.EqualTo(MerkabaDualNodeState.AllThrough));

            Assert.That(MerkabaDualChunkPayload.Create(0ul, 0ul,
                MerkabaDualBlockMeta.NoPayload, 1u).CollapseState(),
                Is.EqualTo(MerkabaDualNodeState.AllFull));
            Assert.That(MerkabaDualChunkPayload.CreateUniform(
                MerkabaDualNodeState.AllThrough, 1u).CollapseState(),
                Is.EqualTo(MerkabaDualNodeState.AllThrough));
            Assert.Throws<ArgumentException>(() =>
                MerkabaDualChunkPayload.Create(0ul, 1ul, 0u, 1u));
        }

        [Test]
        public void DualBlockHeader_UsesTwoStateBitsAndNoUniformPayload()
        {
            var full = MerkabaDualBlockMeta.Create(
                MerkabaDualNodeState.AllFull, 1u,
                MerkabaDualBlockMeta.NoPayload);
            Assert.That(full.State, Is.EqualTo(MerkabaDualNodeState.AllFull));
            Assert.That(full.Generation, Is.EqualTo(1u));
            Assert.That(full.IsCanonical, Is.True);

            var mixed = MerkabaDualBlockMeta.Create(
                MerkabaDualNodeState.Mixed,
                MerkabaDualBlockMeta.MaximumGeneration, 0u);
            Assert.That(mixed.State, Is.EqualTo(MerkabaDualNodeState.Mixed));
            Assert.That(mixed.Generation,
                Is.EqualTo(MerkabaDualBlockMeta.MaximumGeneration));
            Assert.Throws<ArgumentException>(() =>
                MerkabaDualBlockMeta.Create(MerkabaDualNodeState.AllThrough,
                    1u, 0u));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MerkabaDualBlockMeta.Create(MerkabaDualNodeState.Invalid,
                    1u, MerkabaDualBlockMeta.NoPayload));

            MerkabaDualGenerationAdvance ordinary =
                MerkabaDualGeneration.AdvanceBlock(8u);
            Assert.That(ordinary.Generation, Is.EqualTo(9u));
            Assert.That(ordinary.RequiresTransactionalRebase, Is.False);
            MerkabaDualGenerationAdvance rebase =
                MerkabaDualGeneration.AdvanceBlock(
                    MerkabaDualBlockMeta.MaximumGeneration);
            Assert.That(rebase.Generation, Is.EqualTo(1u));
            Assert.That(rebase.RequiresTransactionalRebase, Is.True);
            Assert.That(MerkabaDualGeneration.AdvanceChunk(uint.MaxValue)
                .RequiresTransactionalRebase, Is.True);
        }

        [Test]
        public void FlowerDetailKey_RoundTripsEveryFieldAndRejectsAliases()
        {
            for (int level = 0;
                 level < MerkabaSphereFlowerAuthority.GeometryLevelCount;
                 level++)
            foreach (int channel in new[] { 0, 2, 3, 8, 9, 12 })
            foreach (bool rootSign in new[] { false, true })
            foreach (MerkabaFlowerDetailKind kind in Enum.GetValues(
                         typeof(MerkabaFlowerDetailKind)))
            {
                int pathLimit = 1 << (2 * level);
                int[] paths = pathLimit == 1
                    ? new[] { 0 }
                    : new[] { 0, pathLimit / 2, pathLimit - 1 };
                int sectorCount = MerkabaSphereFlowerAuthority.Lines[channel]
                    .SectorCount;
                foreach (int path in paths)
                foreach (int sector in new[] { 0, sectorCount - 1 })
                {
                    var key = MerkabaFlowerDetailKey.Create(level, path, 47,
                        channel, kind, rootSign, sector);
                    Assert.That(MerkabaFlowerDetailKey.TryDecode(key.Value,
                        out var decoded), Is.True);
                    Assert.That(decoded, Is.EqualTo(key));
                    Assert.That(decoded.GeometryLevel, Is.EqualTo(level));
                    Assert.That(decoded.GeometryChildPath, Is.EqualTo(path));
                    Assert.That(decoded.PetalClass, Is.EqualTo(47));
                    Assert.That(decoded.Channel, Is.EqualTo(channel));
                    Assert.That(decoded.Kind, Is.EqualTo(kind));
                    Assert.That(decoded.RootSign, Is.EqualTo(rootSign));
                    Assert.That(decoded.Sector, Is.EqualTo(sector));
                }
            }

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MerkabaFlowerDetailKey.Create(0, 1, 0, 0,
                    MerkabaFlowerDetailKind.R2Phase, false, 0));
            Assert.That(MerkabaFlowerDetailKey.TryDecode(uint.MaxValue,
                out _), Is.False);
        }

        [Test]
        public void FixedPointIntervals_AreOutwardAndMidpointDoesNotOverflow()
        {
            const double value = 0.1;
            int phaseLo = MerkabaFlowerDetailRecord.EncodePhaseLower(value);
            int phaseHi = MerkabaFlowerDetailRecord.EncodePhaseUpper(value);
            Assert.That(MerkabaFlowerDetailRecord.DecodePhase(phaseLo),
                Is.LessThanOrEqualTo(value));
            Assert.That(MerkabaFlowerDetailRecord.DecodePhase(phaseHi),
                Is.GreaterThanOrEqualTo(value));

            int metricLo = MerkabaFlowerVInterval.EncodeLower(-value);
            int metricHi = MerkabaFlowerVInterval.EncodeUpper(-value);
            Assert.That(MerkabaFlowerVInterval.Decode(metricLo),
                Is.LessThanOrEqualTo(-value));
            Assert.That(MerkabaFlowerVInterval.Decode(metricHi),
                Is.GreaterThanOrEqualTo(-value));

            var key = MerkabaFlowerDetailKey.Create(2, 15, 47, 12,
                MerkabaFlowerDetailKind.KnotMetric, true,
                MerkabaSphereFlowerAuthority.Lines[12].SectorCount - 1);
            var record = MerkabaFlowerDetailRecord.Create(key, int.MinValue,
                int.MaxValue, 9u);
            Assert.That(record.DrawMidpoint, Is.EqualTo(-1));
        }

        [Test]
        public void SkinCarrierKey_SixSourceWedgesShareOneCanonicalPersistentIdentity()
        {
            var keys = new HashSet<uint>();
            for (int carrier = 0; carrier < MerkabaSphereFlowerAuthority.L2HubCount; carrier++)
            foreach (bool rootSign in new[] { false, true })
            foreach (int sector in new[] { 0, 31 })
            {
                uint canonicalSource = MerkabaSphereFlowerAuthority.L2Wedges[6 * carrier].Source;
                uint canonical = canonicalSource |
                    (rootSign ? 1u << MerkabaFlowerL2Key.RootSignShift : 0u) |
                    ((uint)sector << MerkabaFlowerL2Key.SectorShift);
                Assert.That(keys.Add(canonical), Is.True,
                    "Different carriers/root signs/sectors must not alias.");
                for (int wedge = 0; wedge < 6; wedge++)
                {
                    var source = MerkabaSphereFlowerAuthority.L2Wedges[6 * carrier + wedge];
                    Assert.That(MerkabaSphereFlowerAuthority.L2WedgeIndex(
                        source.Petal, source.ChildPath) / 6, Is.EqualTo(carrier));
                    MerkabaFlowerL2Key key = MerkabaFlowerL2Key.Create(
                        source.ChildPath, source.Petal, rootSign, sector);
                    Assert.That(key.Value, Is.EqualTo(canonical));
                    uint spelling = source.Source |
                        (rootSign ? 1u << MerkabaFlowerL2Key.RootSignShift : 0u) |
                        ((uint)sector << MerkabaFlowerL2Key.SectorShift);
                    Assert.That(MerkabaFlowerL2Key.TryDecode(spelling, out _),
                        Is.EqualTo(wedge == 0),
                        "Persisted wedge aliases must be rejected, not retained as parallel runs.");
                }
            }
            Assert.That(keys.Count, Is.EqualTo(4 * MerkabaSphereFlowerAuthority.L2HubCount));
        }

        [Test]
        public void FixedSkinRuns_EncodeOnlyThreadOrderedSplitsAndAtomicGroups()
        {
            var flower = MerkabaFlowerL2Key.Create(15, 47, true, 31);
            Assert.That(MerkabaFlowerL2Key.TryDecode(flower.Value,
                out MerkabaFlowerL2Key decoded), Is.True);
            Assert.That(decoded, Is.EqualTo(flower));
            int carrier = MerkabaSphereFlowerAuthority.L2WedgeIndex(47, 15) / 6;
            var canonical = MerkabaSphereFlowerAuthority.L2Wedges[6 * carrier];
            Assert.That(decoded.GeometryChildPath, Is.EqualTo(canonical.ChildPath));
            Assert.That(decoded.PetalClass, Is.EqualTo(canonical.Petal));
            Assert.That(decoded.RootSign, Is.True);
            Assert.That(decoded.Sector, Is.EqualTo(31));
            Assert.That(MerkabaFlowerL2Key.TryDecode(flower.Value |
                (1u << MerkabaFlowerL2Key.ReservedShift), out _), Is.False);

            // Root, L3 parents j3=1/5 and one L4 parent beneath each.
            uint low = 1u | ((1u << 1 | 1u << 5) << 1) |
                (1u << (8 + 7));
            uint high = 1u << (8 + 35 - 32);
            Assert.That(MerkabaFlowerSkinSplitBits.IsCanonical(low, high),
                Is.True);
            Assert.That(MerkabaFlowerSkinSplitBits.GroupCount(low, high),
                Is.EqualTo(5));
            Assert.That(MerkabaFlowerSkinSplitBits.IsCanonical(
                low | (1u << 8), 0u), Is.False);
            Assert.That(MerkabaFlowerSkinSplitBits.IsCanonical(low,
                1u << 25), Is.False);

            var metric = new MerkabaFlowerSkinMetricRun
            {
                FlowerKey = flower.Value,
                GroupBase = 20u,
                SplitBitsLo = low,
                SplitBitsHi = high,
                ParentEpoch = 9u
            };
            Assert.That(metric.IsValidFor(9u), Is.True);
            Assert.That(metric.GroupCount, Is.EqualTo(5));
            for (int c3 = 0; c3 < 7; c3++)
            for (int c4 = 0; c4 < 7; c4++)
            for (int c5 = 0; c5 < 7; c5++)
            {
                int j3 = MerkabaSphereFlowerAuthority
                    .SkinL3ParentThreadIndex(c3);
                Assert.That(MerkabaFlowerSkinSplitBits.TryCompactChildAddress(
                    metric.GroupBase, low, high, 3, c3, c4, c5,
                    out uint group3, out int child3), Is.True);
                Assert.That(group3, Is.EqualTo(20u));
                Assert.That(child3, Is.EqualTo(j3));

                bool has4 = j3 == 1 || j3 == 5;
                Assert.That(MerkabaFlowerSkinSplitBits.TryCompactChildAddress(
                    metric.GroupBase, low, high, 4, c3, c4, c5,
                    out uint group4, out int child4), Is.EqualTo(has4));
                if (has4)
                {
                    Assert.That(group4, Is.EqualTo((uint)(j3 == 1 ? 21 : 22)));
                    Assert.That(child4, Is.EqualTo(
                        MerkabaSphereFlowerAuthority
                            .SkinL4CompactChildRank(c3, c4)));
                }

                int j4 = MerkabaSphereFlowerAuthority
                    .SkinL4ParentThreadIndex(c3, c4);
                bool has5 = j4 == 7 || j4 == 35;
                Assert.That(MerkabaFlowerSkinSplitBits.TryCompactChildAddress(
                    metric.GroupBase, low, high, 5, c3, c4, c5,
                    out uint group5, out int child5), Is.EqualTo(has5));
                if (has5)
                {
                    Assert.That(group5, Is.EqualTo((uint)(j4 == 7 ? 23 : 24)));
                    Assert.That(child5, Is.EqualTo(
                        MerkabaSphereFlowerAuthority
                            .SkinL5CompactChildRank(c3, c4, c5)));
                }
            }

            var thread = new MerkabaThreadRun
            {
                FlowerKey = flower.Value,
                ProgramRef = MerkabaThreadRun.InvalidRef,
                GroupBase = 40u,
                SplitBitsLo = low,
                SplitBitsHi = high,
                ParentEpoch = 9u
            };
            Assert.That(thread.IsValidFor(9u), Is.True);
            thread.SplitBitsLo = 0u;
            thread.SplitBitsHi = 0u;
            thread.GroupBase = MerkabaThreadRun.InvalidRef;
            Assert.That(thread.IsValidFor(9u), Is.False);
            thread.ProgramRef = 3u;
            Assert.That(thread.IsValidFor(9u), Is.True);
        }

        [Test]
        public void ParentEpoch_RejectsOrphansAndWrapRequiresAtomicRebase()
        {
            var key = MerkabaFlowerDetailKey.Create(1, 3, 5, 4,
                MerkabaFlowerDetailKind.R2Phase, false, 0);
            var detail = MerkabaFlowerDetailRecord.Create(key, -2, 4, 8u);
            Assert.That(detail.IsValidFor(8u), Is.True);
            Assert.That(detail.IsValidFor(9u), Is.False);
            Assert.That(detail.IsValidFor(0u), Is.False);

            var run = new MerkabaThreadRun
            {
                FlowerKey = MerkabaFlowerL2Key.Create(3, 5, false, 0).Value,
                ProgramRef = MerkabaThreadRun.InvalidRef,
                GroupBase = 12u,
                SplitBitsLo = 1u,
                ParentEpoch = 8u
            };
            Assert.That(run.IsValidFor(8u), Is.True);
            Assert.That(run.IsValidFor(9u), Is.False);

            MerkabaEpochAdvance first = MerkabaFlowerOwnerEpoch.Advance(0u);
            Assert.That(first.Epoch, Is.EqualTo(1u));
            Assert.That(first.RequiresTransactionalRebase, Is.False);
            MerkabaEpochAdvance next = MerkabaFlowerOwnerEpoch.Advance(8u);
            Assert.That(next.Epoch, Is.EqualTo(9u));
            Assert.That(next.RequiresTransactionalRebase, Is.False);
            MerkabaEpochAdvance wrap =
                MerkabaFlowerOwnerEpoch.Advance(uint.MaxValue);
            Assert.That(wrap.Epoch, Is.EqualTo(1u));
            Assert.That(wrap.RequiresTransactionalRebase, Is.True);
        }

        [Test]
        public void StructuralAnchor_ChangesOnlyForStructuralIdentity()
        {
            var original = new MerkabaFlowerAnchorIdentity(0, 2, false, 4, 9);
            var compatible = new MerkabaFlowerAnchorIdentity(0, 2, false, 4, 9);
            var changedSector = new MerkabaFlowerAnchorIdentity(0, 3,
                false, 4, 9);
            var changedRoot = new MerkabaFlowerAnchorIdentity(0, 2,
                true, 4, 9);
            var changedSide = new MerkabaFlowerAnchorIdentity(0, 2,
                false, 5, 9);
            var changedSheet = new MerkabaFlowerAnchorIdentity(0, 2,
                false, 4, 10);
            Assert.That(original, Is.EqualTo(compatible));
            Assert.That(original, Is.Not.EqualTo(changedSector));
            Assert.That(original, Is.Not.EqualTo(changedRoot));
            Assert.That(original, Is.Not.EqualTo(changedSide));
            Assert.That(original, Is.Not.EqualTo(changedSheet));
        }

        [Test]
        public void FlowerSymbolTag_RoundTripsNegativeJunctionAndAllStatuses()
        {
            foreach (MerkabaFlowerSymbolStatus status in Enum.GetValues(
                         typeof(MerkabaFlowerSymbolStatus)))
            {
                int line = 12;
                int sector = MerkabaSphereFlowerAuthority.Lines[line]
                    .SectorCount - 1;
                var tag = MerkabaFlowerSymbolTag.Create(5, line, true,
                    sector, true, 7u, status, 0xfffu);
                Assert.That(MerkabaFlowerSymbolTag.TryDecode(tag.Value,
                    out var decoded), Is.True);
                Assert.That(decoded, Is.EqualTo(tag));
                var symbol = new MerkabaFlowerSymbolKey(
                    new int3(-513, -1, 257), tag);
                Assert.That(symbol.Junction,
                    Is.EqualTo(new int3(-513, -1, 257)));
                Assert.That(symbol.IsCanonical, Is.True);
            }
        }

        [Test]
        public void PersistenceHeader_IsLittleEndianCrcBoundAndRejectsTails()
        {
            byte[] address = new byte[MerkabaSphereFlowerPersistenceAbi
                .OwnerAddressBytes];
            MerkabaSpatial.Address encoded = MerkabaSpatial.Encode(
                new int3(-257, -1, 256));
            var tile = new MerkabaTileAddress(encoded.BlockCoord,
                encoded.LocalAddress);
            MerkabaSphereFlowerPersistenceAbi.WriteOwnerAddress(address,
                tile, encoded.KernelLocal);
            byte[] payload = new byte[MerkabaFlowerDetailRecord.ByteSize];
            payload[0] = 0x10;
            payload[1] = 0x20;
            payload[2] = 0x30;
            payload[3] = 0x40;
            payload[4] = 0x50;
            MerkabaRecordHeader header = MerkabaRecordHeader.Create(
                MerkabaRecordKind.FlowerDetail,
                0x0102030405060708ul, address, payload);
            Assert.That(header.Crc32, Is.EqualTo(0xf970503eu));
            byte[] bytes = new byte[MerkabaRecordHeader.ByteSize];
            MerkabaSphereFlowerPersistenceAbi.WriteHeader(bytes, header);

            CollectionAssert.AreEqual(new byte[]
            {
                0x4d, 0x38, 0x53, 0x46, 0x05, 0x00, 0x07, 0x00,
                0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
                0x14, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00,
                0x3e, 0x50, 0x70, 0xf9
            }, bytes);
            Assert.That(MerkabaSphereFlowerPersistenceAbi.TryReadHeader(bytes,
                out var decoded), Is.True);
            Assert.That(MerkabaSphereFlowerPersistenceAbi.ValidateRecord(
                decoded, address, payload), Is.True);

            payload[0] ^= 1;
            Assert.That(MerkabaSphereFlowerPersistenceAbi.ValidateRecord(
                decoded, address, payload), Is.False);
            Assert.That(MerkabaSphereFlowerPersistenceAbi.TryReadHeader(
                new byte[27], out _), Is.False);
            Assert.Throws<ArgumentException>(() => MerkabaRecordHeader.Create(
                MerkabaRecordKind.FlowerDetail, 1ul, address,
                new byte[MerkabaFlowerDetailRecord.ByteSize - 1]));
        }

        [Test]
        public void PersistenceKinds_HaveOneExactAddressAndPayloadShape()
        {
            var expected = new[]
            {
                (MerkabaRecordKind.M8Tile, 16, 8192),
                (MerkabaRecordKind.DualBlock, 12, 8),
                (MerkabaRecordKind.DualBlockChildren, 12, 128),
                (MerkabaRecordKind.DualChunk, 16, 32),
                (MerkabaRecordKind.DualLeaf, 16, 64),
                (MerkabaRecordKind.FlowerOwnerEpoch, 16, 8),
                (MerkabaRecordKind.FlowerDetail, 20, 16),
                (MerkabaRecordKind.FlowerSkinMetricRun, 20, 24),
                (MerkabaRecordKind.FlowerVGroup, 24, 56),
                (MerkabaRecordKind.ThreadProgram, 4, 48),
                (MerkabaRecordKind.ThreadRun, 20, 24),
                (MerkabaRecordKind.ThreadColorGroup, 24, 112),
                (MerkabaRecordKind.Tombstone, 20, 8)
            };
            foreach (var item in expected)
            {
                Assert.DoesNotThrow(() =>
                    MerkabaSphereFlowerPersistenceAbi.ValidateRecordShape(
                        item.Item1, item.Item2, item.Item3), item.Item1.ToString());
                Assert.Throws<ArgumentException>(() =>
                    MerkabaSphereFlowerPersistenceAbi.ValidateRecordShape(
                        item.Item1, item.Item2, item.Item3 + 1));
            }

            var tombstone = MerkabaTombstoneRecord.Create(
                MerkabaRecordKind.FlowerDetail, 0x12345678u);
            Assert.That(tombstone.IsCanonical, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MerkabaTombstoneRecord.Create(MerkabaRecordKind.DualLeaf, 0u));
        }

        [Test]
        public void ExistingSignedHierarchy_IsTheOnlyPersistentAddressCodec()
        {
            foreach (int3 coord in new[]
            {
                new int3(-257, -256, -255), new int3(-1), new int3(0),
                new int3(255, 256, 257)
            })
            {
                MerkabaSpatial.Address encoded = MerkabaSpatial.Encode(coord);
                var tile = new MerkabaTileAddress(encoded.BlockCoord,
                    encoded.LocalAddress);
                byte[] bytes = new byte[MerkabaSphereFlowerPersistenceAbi
                    .OwnerAddressBytes];
                MerkabaSphereFlowerPersistenceAbi.WriteOwnerAddress(bytes,
                    tile, encoded.KernelLocal);
                MerkabaSphereFlowerPersistenceAbi.ReadOwnerAddress(bytes,
                    out MerkabaTileAddress decodedTile, out int kernelLocal);
                Assert.That(decodedTile, Is.EqualTo(tile));
                Assert.That(kernelLocal, Is.EqualTo(encoded.KernelLocal));
                Assert.That(MerkabaSpatial.Decode(decodedTile.BlockCoord,
                    decodedTile.LocalAddress, kernelLocal), Is.EqualTo(coord));

                byte[] group = new byte[MerkabaSphereFlowerPersistenceAbi
                    .GroupAddressBytes];
                MerkabaSphereFlowerPersistenceAbi.WriteGroupAddress(group,
                    tile, encoded.KernelLocal, 0xfedcba98u);
                MerkabaSphereFlowerPersistenceAbi.ReadGroupAddress(group,
                    out decodedTile, out kernelLocal, out uint groupIndex);
                Assert.That(decodedTile, Is.EqualTo(tile));
                Assert.That(kernelLocal, Is.EqualTo(encoded.KernelLocal));
                Assert.That(groupIndex, Is.EqualTo(0xfedcba98u));
            }
        }

        [Test]
        public void GeneratedManagedHlslAndNativeSchemas_AreExact()
        {
            string hlsl = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Shaders/" +
                "MerkabaSphereFlowerDataAbi.generated.hlsl"));
            StringAssert.Contains("struct M8FlowerDetailRecord", hlsl);
            StringAssert.Contains("struct M8FlowerSkinMetricRun", hlsl);
            StringAssert.Contains("struct M8FlowerVGroup", hlsl);
            StringAssert.Contains("struct M8ThreadColorGroup", hlsl);
            StringAssert.DoesNotContain("ThreadResidual", hlsl);
            StringAssert.DoesNotContain("FibonacciRouteOrigin", hlsl);
            StringAssert.DoesNotContain("RWStructuredBuffer", hlsl);
            StringAssert.DoesNotContain("StructuredBuffer", hlsl);
            string native = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Telemetry/Native/" +
                "MerkabaSphereFlowerDataAbi.h"));
            StringAssert.Contains("static_assert(sizeof(DualLeaf) == 64u)",
                native);
            StringAssert.Contains("static_assert(sizeof(RecordHeader) == 28u)",
                native);
        }

        [Test]
        public void NativeResourceAbi_HasOneIndexPerBackingResource()
        {
            Array resources = Enum.GetValues(typeof(MerkabaNativeVulkanExecutor.Resource));
            Assert.That(resources.Length, Is.EqualTo(MerkabaNativeVulkanExecutor.ResourceCount));
            for (int index = 0; index < resources.Length; ++index)
                Assert.That(Convert.ToInt32(resources.GetValue(index)), Is.EqualTo(index));
            foreach (string retired in new[] { "SurfaceWinnerRanks0", "SurfaceQueue", "CarveTiles", "DilationA", "DilationB" })
                Assert.That(Enum.IsDefined(typeof(MerkabaNativeVulkanExecutor.Resource), retired), Is.False);
        }

        [Test, Timeout(60000)]
        public void TestOnlyGpuConsumer_SeesIdenticalPackedFieldsAndStrides()
        {
            const string path =
                "Packages/com.genesis.roomscan/Tests/Editor/" +
                "MerkabaSphereFlowerDataAbi.compute";
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            Assert.That(shader, Is.Not.Null, path);
            int kernel = shader.FindKernel("VerifySphereFlowerDataAbi");

            var block = MerkabaDualBlockMeta.Create(
                MerkabaDualNodeState.Mixed, 17u, 23u);
            var children = new MerkabaDualBlockChildren();
            children.Word00 = 0x11223344u;
            children.Word31 = 0xaabbccddu;
            var chunk = MerkabaDualChunkPayload.Create(ulong.MaxValue,
                1ul, 31u, 19u);
            var leaf = new MerkabaDualLeaf();
            leaf.Word00 = 0x89abcdefu;
            leaf.Word15 = 0x76543210u;
            var epoch = new MerkabaFlowerOwnerEpoch(511u, 29u);
            var detailKey = MerkabaFlowerDetailKey.Create(2, 15, 47, 12,
                MerkabaFlowerDetailKind.KnotMetric, true, 1);
            var detail = MerkabaFlowerDetailRecord.Create(detailKey,
                -123, 456, 29u);
            var flowerKey = MerkabaFlowerL2Key.Create(15, 47, true, 31);
            var metric = new MerkabaFlowerSkinMetricRun
            {
                FlowerKey = flowerKey.Value,
                GroupBase = 37u,
                SplitBitsLo = 0x00000103u,
                SplitBitsHi = 0x00001000u,
                ParentEpoch = 29u,
                Reserved = 0u
            };
            var vGroup = new MerkabaFlowerVGroup
            {
                Child0 = new MerkabaFlowerVInterval
                    { Lower = -11, Upper = 13 },
                Child6 = new MerkabaFlowerVInterval
                    { Lower = -17, Upper = 19 }
            };
            var run = new MerkabaThreadRun
            {
                FlowerKey = flowerKey.Value,
                ProgramRef = 41u,
                GroupBase = 43u,
                SplitBitsLo = 0x76543211u,
                SplitBitsHi = 0x00123456u,
                ParentEpoch = 29u
            };
            var colorGroup = new MerkabaThreadColorGroup
            {
                Child0 = new MerkabaThreadColorInterval
                {
                    LowerLinearRgba = new half4((half)0.125f, (half)0.25f,
                        (half)0.5f, (half)1f),
                    UpperLinearRgba = new half4((half)1.5f, (half)2f,
                        (half)3f, (half)4f)
                },
                Child6 = new MerkabaThreadColorInterval
                {
                    LowerLinearRgba = new half4((half)(-1f), (half)(-2f),
                        (half)(-3f), (half)(-4f)),
                    UpperLinearRgba = new half4((half)5f, (half)6f,
                        (half)7f, (half)8f)
                }
            };
            var program = new MerkabaThreadProgramRecord
            {
                Flags = (uint)MerkabaThreadProgramFlags.OpticalValid,
                OpticalLower = new half4((half)0.0625f, (half)0.125f,
                    (half)0.25f, (half)0.5f),
                OpticalUpper = new half4((half)1f, (half)2f,
                    (half)4f, (half)8f),
                CaptureViewLower = new half4((half)(-1f), (half)(-2f),
                    (half)(-4f), (half)(-8f)),
                CaptureViewUpper = new half4((half)0.75f, (half)1.5f,
                    (half)3f, (half)6f)
            };
            var symbolTag = MerkabaFlowerSymbolTag.Create(5, 12, true, 1,
                true, 7u, MerkabaFlowerSymbolStatus.Refine, 0xabcu);
            var symbol = new MerkabaFlowerSymbolKey(new int3(-71, 73, -79),
                symbolTag);
            var observation = new MerkabaObservationRecord
            {
                TileAndKernel = 83u,
                SourcePixel = 89u,
                SymbolTag = symbolTag.Value,
                PrecisionKey = 97u
            };

            using var b0 = Buffer(block);
            using var b1 = Buffer(children);
            using var b2 = Buffer(chunk);
            using var b3 = Buffer(leaf);
            using var b4 = Buffer(epoch);
            using var b5 = Buffer(detail);
            using var b6 = Buffer(metric);
            using var b7 = Buffer(vGroup);
            using var b8 = Buffer(run);
            using var b9 = Buffer(colorGroup);
            using var b10 = Buffer(program);
            using var b11 = Buffer(symbol);
            using var b12 = Buffer(observation);
            using var output = new ComputeBuffer(58, sizeof(uint),
                ComputeBufferType.Structured);
            shader.SetBuffer(kernel, "_DualBlock", b0);
            shader.SetBuffer(kernel, "_DualChildren", b1);
            shader.SetBuffer(kernel, "_DualChunk", b2);
            shader.SetBuffer(kernel, "_DualLeaf", b3);
            shader.SetBuffer(kernel, "_OwnerEpoch", b4);
            shader.SetBuffer(kernel, "_FlowerDetail", b5);
            shader.SetBuffer(kernel, "_MetricRun", b6);
            shader.SetBuffer(kernel, "_VGroup", b7);
            shader.SetBuffer(kernel, "_ThreadRun", b8);
            shader.SetBuffer(kernel, "_ColorGroup", b9);
            shader.SetBuffer(kernel, "_ThreadProgram", b10);
            shader.SetBuffer(kernel, "_FlowerSymbol", b11);
            shader.SetBuffer(kernel, "_Observation", b12);
            shader.SetBuffer(kernel, "_DataAbiOutput", output);
            shader.Dispatch(kernel, 1, 1, 1);
            var actual = new uint[58];
            output.GetData(actual);

            uint[] expected =
            {
                block.StateAndGeneration, 23u,
                0x11223344u, 0xaabbccddu,
                uint.MaxValue, uint.MaxValue, 1u, 0u, 31u, 19u,
                0x89abcdefu, 0x76543210u,
                511u, 29u,
                detailKey.Value, unchecked((uint)-123), 456u, 29u,
                flowerKey.Value, 37u, 0x00000103u, 0x00001000u, 29u, 0u,
                unchecked((uint)-11), 13u, unchecked((uint)-17), 19u,
                flowerKey.Value, 41u, 43u, 0x76543211u, 0x00123456u, 29u,
                RawUInt32(colorGroup, 0), RawUInt32(colorGroup, 4),
                RawUInt32(colorGroup, 8), RawUInt32(colorGroup, 12),
                RawUInt32(colorGroup, 96), RawUInt32(colorGroup, 100),
                RawUInt32(colorGroup, 104), RawUInt32(colorGroup, 108),
                (uint)MerkabaThreadProgramFlags.OpticalValid, 0u, 0u, 0u,
                RawUInt32(program, 16), RawUInt32(program, 28),
                RawUInt32(program, 32), RawUInt32(program, 44),
                unchecked((uint)-71), unchecked((uint)-79), symbolTag.Value,
                97u, detailKey.Value, flowerKey.Value, symbolTag.Value,
                MerkabaFlowerSkinSplitBits.HighValidMask
            };
            CollectionAssert.AreEqual(expected, actual);
        }

        private static ComputeBuffer Buffer<T>(T value) where T : struct
        {
            var buffer = new ComputeBuffer(1, Marshal.SizeOf<T>(),
                ComputeBufferType.Structured);
            buffer.SetData(new[] { value });
            return buffer;
        }

        private static uint RawUInt32<T>(T value, int offset) where T : struct
        {
            IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
            try
            {
                Marshal.StructureToPtr(value, memory, false);
                return unchecked((uint)Marshal.ReadInt32(memory, offset));
            }
            finally
            {
                Marshal.FreeHGlobal(memory);
            }
        }

        private static void AssertSize<T>(int expected) where T : struct =>
            Assert.That(Marshal.SizeOf<T>(), Is.EqualTo(expected), typeof(T).Name);

        private static void AssertOffset<T>(string field, int expected)
            where T : struct => Assert.That(
                Marshal.OffsetOf<T>(field).ToInt32(), Is.EqualTo(expected),
                typeof(T).Name + "." + field);
    }
}
