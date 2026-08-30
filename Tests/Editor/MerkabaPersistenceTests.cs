using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaPersistenceTests
    {
        [Test]
        public void V3SparseTiles_RoundTripDeterministicallyAcrossNegativeBlocks()
        {
            MerkabaSessionSnapshot source = Fixture();
            byte[] first = Write(source);
            MerkabaSessionSnapshot restored;
            using (var stream = new MemoryStream(first, false))
                restored = MerkabaPersistence.ReadSnapshot(stream);
            byte[] second = Write(restored);

            Assert.That(BitConverter.ToInt32(first, 4), Is.EqualTo(3));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(restored.AnchorUuid, Is.EqualTo(source.AnchorUuid));
            Assert.That(restored.IntegrationCount, Is.EqualTo(47));
            Assert.That(restored.Tiles, Has.Count.EqualTo(2));
            Assert.That(restored.Tiles[0].Address.BlockCoord,
                Is.EqualTo(new int3(-2, -1, 0)));
            Assert.That(restored.Tiles[1].Address.BlockCoord,
                Is.EqualTo(new int3(1, 0, 3)));
            Assert.That(restored.Tiles[0].States[17].IsOccupied, Is.True);
            Assert.That(restored.Tiles[0].States[17].NeedsCarve, Is.True);
            Assert.That(restored.Tiles[0].States[17].HasMeasuredSurfacePlane,
                Is.True);
            KernelState.DecodeSurfacePlane(restored.Tiles[0].States[17].Flags,
                out float3 restoredNormal, out float restoredOffset);
            Assert.That(math.dot(restoredNormal,
                math.normalize(new float3(1f, 0f, 1f))),
                Is.GreaterThan(0.99999f));
            Assert.That(restoredOffset, Is.EqualTo(0.006f).Within(0.00011f));
            KernelState pendingCorrection = restored.Tiles[0].States[23];
            Assert.That(pendingCorrection.OccupancyEvidence, Is.Zero);
            Assert.That(pendingCorrection.PackedColor, Is.Zero);
            Assert.That(pendingCorrection.ColorConfidence, Is.Zero);
            Assert.That(pendingCorrection.IsOccupied, Is.False);
            Assert.That(pendingCorrection.NeedsCarve, Is.True);
            Assert.That(restored.Tiles[0].States[24], Is.EqualTo(default(KernelState)));
            Assert.That(restored.Tiles[0].States[24].NeedsCarve, Is.False);
            Assert.That(restored.Tiles[1].States[500].OccupancyEvidence,
                Is.LessThan(0));
            Assert.That(restored.Tiles[1].States[500].NeedsCarve, Is.False);
        }

        [Test]
        public void CheckpointContainsOnlySparse8192ByteTilePayloads()
        {
            Assert.That(Marshal.SizeOf<MerkabaTileAddress>(), Is.EqualTo(16));
            MerkabaSessionSnapshot snapshot = Fixture();
            byte[] bytes = Write(snapshot);
            int expected = 108 + snapshot.Tiles.Count *
                (MerkabaSsdStore.TileRecordHeaderBytes +
                 MerkabaSsdStore.TilePayloadBytes);
            Assert.That(bytes.Length, Is.EqualTo(expected));
        }

        [Test]
        public void GreenfieldV3ReaderRejectsV2Checkpoint()
        {
            byte[] bytes = Write(Fixture());
            Buffer.BlockCopy(BitConverter.GetBytes(2), 0, bytes, 4, 4);
            using var stream = new MemoryStream(bytes, false);
            Assert.Throws<InvalidDataException>(() =>
                MerkabaPersistence.ReadSnapshot(stream));
        }

        [Test]
        public void PayloadCrcRejectsCanonicalCorruption()
        {
            byte[] bytes = Write(Fixture());
            bytes[108 + MerkabaSsdStore.TileRecordHeaderBytes + 7] ^= 0x40;
            using var stream = new MemoryStream(bytes, false);
            Assert.Throws<InvalidDataException>(() =>
                MerkabaPersistence.ReadSnapshot(stream));
        }

        [Test]
        public void ExplicitLoadCountsCanonicalOccupiedStatesExactly()
        {
            MerkabaSessionSnapshot snapshot = Fixture();
            Assert.That(MerkabaGrid.CountOccupiedStates(snapshot), Is.EqualTo(1u));
        }

        [Test]
        public void ExplicitLoadUsesBatchBoundedConvergenceAndRejectsGpuOverflow()
        {
            string gpu = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Merkaba/" +
                "MerkabaGrid.Gpu.cs"));
            string storage = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Merkaba/" +
                "MerkabaGrid.Storage.cs"));
            Assert.That(gpu, Does.Contain(
                "boundedConvergenceRounds = LoadRegistrationRoundLimit(count)"));
            Assert.That(gpu, Does.Not.Contain("hierarchyPublicationRounds"));
            Assert.That(gpu, Does.Not.Contain(
                "MerkabaSpatial.HashSlotsPerBucket * 2 +"));
            Assert.That(storage, Does.Contain("ReadWorldCountersAsync"));
            Assert.That(storage, Does.Contain("CounterHashFull"));
            Assert.That(storage, Does.Contain(
                "addressedTiles != (ulong)snapshot.Tiles.Count"));
        }

        [Test]
        public void LoadRegistrationBound_ConvergesThirtyTwoSerializedClaims()
        {
            int count = MerkabaGrid.StreamBatchCapacity;
            int blocks = 0;
            int chunks = 0;
            int tiles = 0;
            int rounds = MerkabaGrid.LoadRegistrationRoundLimit(count);
            Assert.That(rounds, Is.EqualTo(34));

            for (int round = 0; round < rounds; round++)
            {
                int blocksBefore = blocks;
                int chunksBefore = chunks;
                tiles = chunksBefore;
                chunks = blocksBefore;
                blocks = Math.Min(count, blocksBefore + 1);
            }

            Assert.That(blocks, Is.EqualTo(count));
            Assert.That(chunks, Is.EqualTo(count));
            Assert.That(tiles, Is.EqualTo(count));
        }

        [Test]
        public void AnchoredResumeFailsClosedBeforeRegisteringTheM8World()
        {
            string persistence = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Merkaba/" +
                "MerkabaPersistence.cs"));
            int anchored = persistence.IndexOf(
                "if (snapshot.AnchorUuid != Guid.Empty)",
                StringComparison.Ordinal);
            int loadWorld = persistence.IndexOf(
                "await _grid.LoadStoredSnapshotAsync(snapshot, progress)",
                StringComparison.Ordinal);
            Assert.That(anchored, Is.GreaterThanOrEqualTo(0));
            Assert.That(loadWorld, Is.GreaterThan(anchored));
            string gate = persistence.Substring(anchored, loadWorld - anchored);
            Assert.That(gate, Does.Contain("RoomAnchorManager is unavailable"));
            Assert.That(gate, Does.Contain("could not be localized"));
            Assert.That(gate, Does.Contain("RoomSpaceRoot.Instance == null"));
            Assert.That(gate, Does.Contain("is unavailable."));
            Assert.That(gate, Does.Contain("did not bind"));
            Assert.That(gate, Does.Not.Contain("using current world frame"));
        }

        [Test]
        public async Task OverlayIndexReturnsNewestExactTileGeneration()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "merkaba-m8-" + Guid.NewGuid().ToString("N"));
            var store = new MerkabaSsdStore(directory);
            var address = new MerkabaTileAddress(new int3(-3, 2, -1),
                (uint)(17 | (42 << 9)));
            try
            {
                KernelState[] firstStates = new KernelState[
                    MerkabaSpatial.KernelsPerTile];
                firstStates[5].Apply(MerkabaObservationKind.Surface, 1f,
                    new Color32(1, 2, 3, 255));
                var first = new MerkabaTileSnapshot
                {
                    Address = address,
                    States = firstStates
                };
                await store.AppendAsync(new[] { first });

                KernelState[] secondStates = (KernelState[])firstStates.Clone();
                secondStates[6].Apply(MerkabaObservationKind.Surface, 1f,
                    new Color32(4, 5, 6, 255));
                var second = new MerkabaTileSnapshot
                {
                    Address = address,
                    States = secondStates
                };
                await store.AppendAsync(new[] { second });
                await store.RebuildIndexAsync();
                MerkabaTileSnapshot[] restored = await store.ReadAsync(
                    new[] { address });

                Assert.That(store.IndexedTileCount, Is.EqualTo(1));
                Assert.That(first.Generation, Is.EqualTo(1u));
                Assert.That(second.Generation, Is.EqualTo(2u));
                Assert.That(restored[0].Generation, Is.EqualTo(2u));
                Assert.That(restored[0].States[6].IsOccupied, Is.True);
            }
            finally
            {
                store.Clear();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void DurableOverlayAndCheckpointPublishBeforeTheirCpuAuthority()
        {
            string store = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Merkaba/" +
                "MerkabaSsdStore.cs"));
            int durable = store.IndexOf("stream.Flush(true);",
                StringComparison.Ordinal);
            int indexPublish = store.IndexOf(
                "_index[update.Tile.Address] = update.Location;",
                StringComparison.Ordinal);
            Assert.That(durable, Is.GreaterThanOrEqualTo(0));
            Assert.That(indexPublish, Is.GreaterThan(durable));

            string persistence = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Merkaba/" +
                "MerkabaPersistence.cs"));
            Assert.That(persistence, Does.Contain("RenameAtomic"));
            Assert.That(persistence, Does.Not.Contain("File.Copy("));
        }

        private static MerkabaSessionSnapshot Fixture()
        {
            var snapshot = new MerkabaSessionSnapshot
            {
                AnchorUuid = Guid.Parse("91b649aa-bfcb-43c4-9818-79e5a1012c7b"),
                AnchorAtSave = Matrix4x4.TRS(new Vector3(1, 2, 3),
                    Quaternion.Euler(0, 30, 0), Vector3.one),
                IntegrationCount = 47
            };
            var negative = new KernelState[MerkabaSpatial.KernelsPerTile];
            negative[17].Apply(MerkabaObservationKind.Surface, 1f,
                new Color32(12, 34, 56, 255));
            negative[17].Flags = KernelState.SetSurfacePlane(
                negative[17].Flags,
                math.normalize(new float3(1f, 0f, 1f)), 0.006f);
            negative[23] = new KernelState
            {
                Flags = MerkabaConstants.NeedsCarveFlag
            };
            var positive = new KernelState[MerkabaSpatial.KernelsPerTile];
            for (int index = 0; index < 3; index++)
                positive[500].Apply(MerkabaObservationKind.Free, 1f, default);
            snapshot.Tiles.Add(new MerkabaTileSnapshot
            {
                Address = new MerkabaTileAddress(new int3(-2, -1, 0),
                    (uint)(511 | (63 << 9))),
                Generation = 2,
                States = negative
            });
            snapshot.Tiles.Add(new MerkabaTileSnapshot
            {
                Address = new MerkabaTileAddress(new int3(1, 0, 3), 0u),
                Generation = 7,
                States = positive
            });
            return snapshot;
        }

        private static byte[] Write(MerkabaSessionSnapshot snapshot)
        {
            using var stream = new MemoryStream();
            MerkabaPersistence.WriteSnapshot(stream, snapshot);
            return stream.ToArray();
        }
    }
}
