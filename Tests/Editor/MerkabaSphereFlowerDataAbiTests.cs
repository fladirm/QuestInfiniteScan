using System;
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
            AssertSize<MerkabaThreadRun>(16);
            AssertSize<MerkabaThreadResidual>(32);
            AssertSize<MerkabaThreadProgramRecord>(48);
            AssertSize<MerkabaFlowerSymbolKey>(16);
            AssertSize<MerkabaObservationRecord>(16);
            AssertSize<MerkabaRecordHeader>(28);
            AssertSize<MerkabaTombstoneRecord>(8);

            AssertOffset<MerkabaThreadResidual>(
                nameof(MerkabaThreadResidual.LowerLinearRgba), 8);
            AssertOffset<MerkabaThreadResidual>(
                nameof(MerkabaThreadResidual.UpperLinearRgba), 16);
            AssertOffset<MerkabaThreadResidual>(
                nameof(MerkabaThreadResidual.Flags), 24);
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
            for (int level = 0; level < 6; level++)
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
                    Assert.That(decoded.Level, Is.EqualTo(level));
                    Assert.That(decoded.ChildPath, Is.EqualTo(path));
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

            int metricLo = MerkabaFlowerDetailRecord.EncodeMetricLower(-value);
            int metricHi = MerkabaFlowerDetailRecord.EncodeMetricUpper(-value);
            Assert.That(MerkabaFlowerDetailRecord.DecodeMetric(metricLo),
                Is.LessThanOrEqualTo(-value));
            Assert.That(MerkabaFlowerDetailRecord.DecodeMetric(metricHi),
                Is.GreaterThanOrEqualTo(-value));

            var key = MerkabaFlowerDetailKey.Create(5, 1023, 47, 12,
                MerkabaFlowerDetailKind.VAmplitude, true,
                MerkabaSphereFlowerAuthority.Lines[12].SectorCount - 1);
            var record = MerkabaFlowerDetailRecord.Create(key, int.MinValue,
                int.MaxValue, 9u);
            Assert.That(record.DrawMidpoint, Is.EqualTo(-1));
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
                FlowerKey = key.Value,
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
            Assert.That(header.Crc32, Is.EqualTo(0xe4e543e9u));
            byte[] bytes = new byte[MerkabaRecordHeader.ByteSize];
            MerkabaSphereFlowerPersistenceAbi.WriteHeader(bytes, header);

            CollectionAssert.AreEqual(new byte[]
            {
                0x4d, 0x38, 0x53, 0x46, 0x04, 0x00, 0x07, 0x00,
                0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
                0x14, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00,
                0xe9, 0x43, 0xe5, 0xe4
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
                (MerkabaRecordKind.ThreadProgram, 4, 48),
                (MerkabaRecordKind.ThreadRun, 20, 16),
                (MerkabaRecordKind.ThreadResidual, 20, 32),
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
            }
        }

        [Test]
        public void GeneratedManagedHlslAndNativeSchemas_AreExact()
        {
            string hlsl = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Shaders/" +
                "MerkabaSphereFlowerDataAbi.generated.hlsl"));
            StringAssert.Contains("struct M8FlowerDetailRecord", hlsl);
            StringAssert.Contains("struct M8ThreadResidual", hlsl);
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
        public void Cut02Schema_HasNoProductionBufferOrExecutorBinding()
        {
            string grid = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Merkaba/" +
                "MerkabaGrid.Gpu.cs"));
            string executor = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Telemetry/" +
                "MerkabaNativeVulkanExecutor.cs"));
            foreach (string field in new[]
            {
                "_m8DualBlock", "_m8DualChunk", "_m8DualLeaf",
                "_m8FlowerDetail", "_m8ThreadAtlas", "_m8FlowerSymbol"
            })
                StringAssert.DoesNotContain(field, grid);
            foreach (string resource in new[]
            {
                "DualBlockState", "DualChunkState", "DualLeaves",
                "FlowerDetailPages", "ThreadAtlasPages", "FlowerSymbolArena"
            })
                StringAssert.DoesNotContain(resource, executor);
            Assert.That(MerkabaNativeVulkanExecutor.ResourceCount,
                Is.EqualTo(45));
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
            var detailKey = MerkabaFlowerDetailKey.Create(5, 1023, 47, 12,
                MerkabaFlowerDetailKind.VAmplitude, true, 1);
            var detail = MerkabaFlowerDetailRecord.Create(detailKey,
                -123, 456, 29u);
            var run = new MerkabaThreadRun
            {
                FlowerKey = detailKey.Value,
                ProgramRef = 41u,
                ResidualBase = 43u,
                ParentEpoch = 29u
            };
            var residual = new MerkabaThreadResidual
            {
                SegmentKey = 47u,
                ParentEpoch = 29u,
                LowerLinearRgba = new half4((half)0.125f, (half)0.25f,
                    (half)0.5f, (half)1f),
                UpperLinearRgba = new half4((half)1.5f, (half)2f,
                    (half)3f, (half)4f),
                Flags = 0x13579bdfu,
                Reserved = 0x2468ace0u
            };
            var program = new MerkabaThreadProgramRecord
            {
                EndpointColorRule = 53u,
                RgbResidualBasis = 59u,
                FibonacciRouteOrigin = 61u,
                Flags = 67u,
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
            using var b6 = Buffer(run);
            using var b7 = Buffer(residual);
            using var b8 = Buffer(program);
            using var b9 = Buffer(symbol);
            using var b10 = Buffer(observation);
            using var output = new ComputeBuffer(42, sizeof(uint),
                ComputeBufferType.Structured);
            shader.SetBuffer(kernel, "_DualBlock", b0);
            shader.SetBuffer(kernel, "_DualChildren", b1);
            shader.SetBuffer(kernel, "_DualChunk", b2);
            shader.SetBuffer(kernel, "_DualLeaf", b3);
            shader.SetBuffer(kernel, "_OwnerEpoch", b4);
            shader.SetBuffer(kernel, "_FlowerDetail", b5);
            shader.SetBuffer(kernel, "_ThreadRun", b6);
            shader.SetBuffer(kernel, "_ThreadResidual", b7);
            shader.SetBuffer(kernel, "_ThreadProgram", b8);
            shader.SetBuffer(kernel, "_FlowerSymbol", b9);
            shader.SetBuffer(kernel, "_Observation", b10);
            shader.SetBuffer(kernel, "_DataAbiOutput", output);
            shader.Dispatch(kernel, 1, 1, 1);
            var actual = new uint[42];
            output.GetData(actual);

            uint[] expected =
            {
                block.StateAndGeneration, 23u,
                0x11223344u, 0xaabbccddu,
                uint.MaxValue, uint.MaxValue, 1u, 0u, 31u, 19u,
                0x89abcdefu, 0x76543210u,
                511u, 29u,
                detailKey.Value, unchecked((uint)-123), 456u, 29u,
                detailKey.Value, 41u, 43u, 29u,
                47u, 29u, 0x13579bdfu, 0x2468ace0u,
                53u, 67u,
                unchecked((uint)-71), unchecked((uint)-79), symbolTag.Value,
                97u, detailKey.Value, symbolTag.Value,
                RawUInt32(residual, 8), RawUInt32(residual, 12),
                RawUInt32(residual, 16), RawUInt32(residual, 20),
                RawUInt32(program, 16), RawUInt32(program, 28),
                RawUInt32(program, 32), RawUInt32(program, 44)
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
