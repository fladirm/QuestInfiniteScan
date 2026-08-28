using System;
using System.IO;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaPersistenceTests
    {
        [Test]
        public void CanonicalSnapshot_RoundTripsDeterministicallyAcrossNegativeChunks()
        {
            MerkabaSessionSnapshot source = Fixture();
            byte[] first = Write(source);
            MerkabaSessionSnapshot restored;
            using (var stream = new MemoryStream(first, false))
                restored = MerkabaPersistence.ReadSnapshot(stream);
            byte[] second = Write(restored);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(restored.AnchorUuid, Is.EqualTo(source.AnchorUuid));
            Assert.That(restored.IntegrationCount, Is.EqualTo(47));
            Assert.That(restored.Chunks, Has.Count.EqualTo(2));
            Assert.That(restored.Chunks[0].Coord, Is.EqualTo(new int3(-2, -1, 0)));
            Assert.That(restored.Chunks[1].Coord, Is.EqualTo(new int3(1, 0, 3)));
            KernelState occupied = restored.Chunks[0].States[17];
            KernelState carved = restored.Chunks[1].States[900];
            Assert.That(occupied.IsOccupied, Is.True);
            Assert.That(occupied.Color, Is.EqualTo(new Color32(12, 34, 56, 255)));
            Assert.That(carved.IsOccupied, Is.False);
            Assert.That(carved.OccupancyEvidence, Is.LessThan(0));
        }

        [Test]
        public void LoaderRejectsStateThatViolatesHysteresisContract()
        {
            byte[] bytes = Write(Fixture());
            // Header 108 bytes + first chunk header 16 bytes = first KernelState.
            Buffer.BlockCopy(BitConverter.GetBytes(
                MerkabaConstants.OccupiedOnThreshold + 10), 0, bytes, 124, 4);
            // The first state is empty, so evidence above ON is corrupt.
            using var stream = new MemoryStream(bytes, false);
            Assert.Throws<InvalidDataException>(() => MerkabaPersistence.ReadSnapshot(stream));
        }

        [Test]
        public void SnapshotContainsOnlyMinimalSixteenByteKernelRecords()
        {
            MerkabaSessionSnapshot snapshot = Fixture();
            byte[] bytes = Write(snapshot);
            int expected = 108 + snapshot.Chunks.Count * 16 +
                snapshot.Chunks.Count * MerkabaConstants.KernelsPerChunk * 16;
            Assert.That(bytes.Length, Is.EqualTo(expected));
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
            var negative = new KernelState[MerkabaConstants.KernelsPerChunk];
            MerkabaIntegrator.IntegrateClassified(ref negative[17],
                MerkabaObservationKind.Surface, 1f, new Color32(12, 34, 56, 255));
            var positive = new KernelState[MerkabaConstants.KernelsPerChunk];
            for (int i = 0; i < 3; i++)
                MerkabaIntegrator.IntegrateClassified(ref positive[900],
                    MerkabaObservationKind.Free, 1f, default);
            snapshot.Chunks.Add(new MerkabaChunkSnapshot
            {
                Coord = new int3(-2, -1, 0), States = negative
            });
            snapshot.Chunks.Add(new MerkabaChunkSnapshot
            {
                Coord = new int3(1, 0, 3), States = positive
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
