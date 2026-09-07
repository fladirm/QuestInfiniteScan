using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaPersistenceTests
    {
        private static readonly Guid Session = Guid.Parse(
            "ab5ce946-1d3d-4ccf-97d4-e212e12b9ac4");
        private static readonly Guid Anchor = Guid.Parse(
            "91b649aa-bfcb-43c4-9818-79e5a1012c7b");
        private static readonly Matrix4x4 AnchorPose = Matrix4x4.TRS(
            new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 30f, 0f),
            Vector3.one);

        [Test]
        public void Manifest_IsExactDeterministicAndCrcBound()
        {
            MerkabaSessionManifest source = ManifestFixture();
            byte[] first = WriteManifest(source);
            MerkabaSessionManifest restored;
            using (var input = new MemoryStream(first, false))
                restored = MerkabaSessionManifest.Read(input);
            byte[] second = WriteManifest(restored);

            Assert.That(first, Has.Length.EqualTo(
                MerkabaSessionManifest.ByteSize));
            Assert.That(first, Is.EqualTo(second));
            Assert.That(restored.SessionUuid, Is.EqualTo(Session));
            Assert.That(restored.AnchorUuid, Is.EqualTo(Anchor));
            Assert.That(restored.CommitGeneration, Is.EqualTo(7ul));
            Assert.That(restored.ValidEnd(MerkabaStorageStream.ThreadAtlas),
                Is.EqualTo(106L));

            first[72] ^= 0x40;
            using var corrupt = new MemoryStream(first, false);
            Assert.Throws<InvalidDataException>(() =>
                MerkabaSessionManifest.Read(corrupt));
        }

        [Test]
        public async Task DirtyAppend_RoundTripsNewestNegativeCoordinateTile()
        {
            string directory = TemporaryRoot("m8-roundtrip");
            var address = new MerkabaTileAddress(new int3(-3, 2, -1),
                (uint)(17 | (42 << 9)));
            try
            {
                var store = new MerkabaSsdStore(directory);
                MerkabaTileSnapshot first = Tile(address, 5,
                    new Color32(1, 2, 3, 255));
                store.AppendM8Tiles(new[] { first });
                MerkabaStorageCommitResult commit1 = store.Commit(Session,
                    Anchor, AnchorPose, 11, 1);

                MerkabaTileSnapshot second = Tile(address, 6,
                    new Color32(4, 5, 6, 255));
                store.AppendM8Tiles(new[] { second });
                MerkabaStorageCommitResult commit2 = store.Commit(Session,
                    Anchor, AnchorPose, 12, 1);

                var reopened = new MerkabaSsdStore(directory);
                MerkabaSessionOpenState open = reopened.OpenCommitted();
                MerkabaTileSnapshot[] restored = await reopened.ReadAsync(
                    new[] { address });

                Assert.That(commit1.Generation, Is.EqualTo(1ul));
                Assert.That(commit2.Generation, Is.EqualTo(2ul));
                Assert.That(second.Generation, Is.EqualTo(2ul));
                Assert.That(open.Manifest.IntegrationCount, Is.EqualTo(12));
                Assert.That(open.IndexedTileCount, Is.EqualTo(1));
                Assert.That(restored[0].Generation, Is.EqualTo(2ul));
                Assert.That(restored[0].States[5].IsOccupied, Is.False);
                Assert.That(restored[0].States[6].IsOccupied, Is.True);
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public void CommitCost_IsExactlyNewDirtyRecordBytes()
        {
            string directory = TemporaryRoot("m8-dirty-cost");
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(new MerkabaTileAddress(new int3(-1, 0, 2), 9u),
                        7, new Color32(7, 8, 9, 255))
                });
                long oneRecord = MerkabaRecordHeader.ByteSize +
                    MerkabaSphereFlowerPersistenceAbi.TileAddressBytes +
                    MerkabaSsdStore.TilePayloadBytes;
                MerkabaStorageCommitResult first = store.Commit(Session,
                    Anchor, AnchorPose, 1, 1);
                MerkabaStorageCommitResult second = store.Commit(Session,
                    Anchor, AnchorPose, 1, 1);

                Assert.That(first.DirtyBytes, Is.EqualTo(oneRecord));
                Assert.That(second.DirtyBytes, Is.Zero);
                Assert.That(new FileInfo(store.M8LivePath).Length,
                    Is.EqualTo(oneRecord));
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public void CommittedRoot_CannotBeRelabeledWithAnotherIdentity()
        {
            string directory = TemporaryRoot("m8-identity-relabel");
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(new MerkabaTileAddress(new int3(0), 0u), 1,
                        new Color32(1, 2, 3, 255))
                });
                store.Commit(Session, Anchor, AnchorPose, 1, 1);

                Assert.Throws<InvalidOperationException>(() => store.Commit(
                    Guid.NewGuid(), Anchor, AnchorPose, 2, 1));
                Assert.Throws<InvalidOperationException>(() => store.Commit(
                    Session, Guid.NewGuid(), AnchorPose, 2, 1));

                MerkabaSessionManifest manifest = store
                    .CurrentCommittedState().Manifest;
                Assert.That(manifest.SessionUuid, Is.EqualTo(Session));
                Assert.That(manifest.AnchorUuid, Is.EqualTo(Anchor));
                Assert.That(manifest.CommitGeneration, Is.EqualTo(1ul));
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public async Task UncommittedTail_IsIgnoredAndTruncatedOnOpen()
        {
            string directory = TemporaryRoot("m8-tail");
            var committedAddress = new MerkabaTileAddress(
                new int3(-2, -1, 0), (uint)(511 | (63 << 9)));
            var tailAddress = new MerkabaTileAddress(new int3(7, 8, 9), 11u);
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(committedAddress, 17, new Color32(12, 34, 56, 255))
                });
                store.Commit(Session, Anchor, AnchorPose, 47, 1);
                long committedEnd = new FileInfo(store.M8LivePath).Length;
                store.AppendM8Tiles(new[]
                {
                    Tile(tailAddress, 31, new Color32(9, 8, 7, 255))
                });

                var reopened = new MerkabaSsdStore(directory);
                reopened.OpenCommitted();
                MerkabaTileSnapshot[] restored = await reopened.ReadAsync(
                    new[] { committedAddress });

                Assert.That(reopened.IndexedTileCount, Is.EqualTo(1));
                Assert.That(Array.IndexOf(reopened.SnapshotSortedAddresses(),
                    tailAddress), Is.EqualTo(-1));
                Assert.That(restored[0].States[17].IsOccupied, Is.True);
                Assert.That(new FileInfo(reopened.M8LivePath).Length,
                    Is.EqualTo(committedEnd));
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public void GarbageBeyondManifestRange_IsNeverParsed()
        {
            string directory = TemporaryRoot("m8-garbage-tail");
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(new MerkabaTileAddress(new int3(0), 0u), 1,
                        new Color32(1, 1, 1, 255))
                });
                store.Commit(Session, Anchor, AnchorPose, 1, 1);
                long committed = new FileInfo(store.M8LivePath).Length;
                using (var output = new FileStream(store.M8LivePath,
                           FileMode.Append, FileAccess.Write, FileShare.Read))
                    output.Write(new byte[] { 0xde, 0xad, 0xbe, 0xef }, 0, 4);

                var reopened = new MerkabaSsdStore(directory);
                Assert.DoesNotThrow(() => reopened.OpenCommitted());
                Assert.That(new FileInfo(reopened.M8LivePath).Length,
                    Is.EqualTo(committed));
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public void CorruptionInsideCommittedRange_FailsClosed()
        {
            string directory = TemporaryRoot("m8-corrupt");
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(new MerkabaTileAddress(new int3(0), 0u), 1,
                        new Color32(1, 1, 1, 255))
                });
                store.Commit(Session, Anchor, AnchorPose, 1, 1);
                using (var file = new FileStream(store.M8LivePath,
                           FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                {
                    file.Position = MerkabaRecordHeader.ByteSize +
                        MerkabaSphereFlowerPersistenceAbi.TileAddressBytes + 7;
                    int value = file.ReadByte();
                    file.Position--;
                    file.WriteByte((byte)(value ^ 0x40));
                    file.Flush(true);
                }

                var reopened = new MerkabaSsdStore(directory);
                Assert.Throws<InvalidDataException>(() =>
                    reopened.OpenCommitted());
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public void TruncatedCommittedStream_FailsBeforeReplay()
        {
            string directory = TemporaryRoot("m8-truncated-commit");
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(new MerkabaTileAddress(new int3(0), 0u), 4,
                        new Color32(1, 2, 3, 255))
                });
                store.Commit(Session, Anchor, AnchorPose, 1, 1);
                using (var file = new FileStream(store.M8LivePath,
                           FileMode.Open, FileAccess.Write, FileShare.Read))
                    file.SetLength(file.Length - 1L);

                var reopened = new MerkabaSsdStore(directory);
                Assert.Throws<InvalidDataException>(() =>
                    reopened.OpenCommitted());
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [TestCase((int)MerkabaCommitStage.BeforeDataFlush, false)]
        [TestCase((int)MerkabaCommitStage.AfterDataFlush, false)]
        [TestCase((int)MerkabaCommitStage.AfterManifestFlush, false)]
        [TestCase((int)MerkabaCommitStage.AfterManifestPublish, true)]
        public async Task CrashStage_ReopensExactlyOldOrNewGeneration(
            int stageValue, bool newGenerationPublished)
        {
            var stage = (MerkabaCommitStage)stageValue;
            string directory = TemporaryRoot("m8-crash");
            var address = new MerkabaTileAddress(new int3(-4, 3, -2), 17u);
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(address, 2, new Color32(2, 2, 2, 255))
                });
                store.Commit(Session, Anchor, AnchorPose, 1, 1);
                store.AppendM8Tiles(new[]
                {
                    Tile(address, 3, new Color32(3, 3, 3, 255))
                });

                Assert.Throws<IOException>(() => store.Commit(Session, Anchor,
                    AnchorPose, 2, 1, crashProbe: reached =>
                    {
                        if (reached == stage)
                            throw new IOException("simulated process crash");
                    }));

                var reopened = new MerkabaSsdStore(directory);
                MerkabaSessionOpenState open = reopened.OpenCommitted();
                MerkabaTileSnapshot[] tile = await reopened.ReadAsync(
                    new[] { address });
                Assert.That(open.Manifest.CommitGeneration,
                    Is.EqualTo(newGenerationPublished ? 2ul : 1ul));
                Assert.That(tile[0].States[newGenerationPublished ? 3 : 2]
                    .IsOccupied, Is.True);
                Assert.That(tile[0].States[newGenerationPublished ? 2 : 3]
                    .IsOccupied, Is.False);
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public void M8DualDetailAndThread_CommitUnderOneManifest()
        {
            string directory = TemporaryRoot("m8-all-streams");
            var tile = new MerkabaTileAddress(new int3(-2, 1, -1), 0u);
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 5, new Color32(10, 20, 30, 255))
                });
                store.AppendSphereFlowerRecords(AllAuthorityRecords(tile, 5,
                    1u).ToArray());
                store.Commit(Session, Anchor, AnchorPose, 9, 1);

                var reopened = new MerkabaSsdStore(directory);
                MerkabaSessionOpenState open = reopened.OpenCommitted();
                MerkabaSphereFlowerReplayIndex replay = reopened.Subordinate;

                foreach (MerkabaStorageStream stream in new[]
                         {
                             MerkabaStorageStream.M8Live,
                             MerkabaStorageStream.ThroughLive,
                             MerkabaStorageStream.FlowerDetail,
                             MerkabaStorageStream.ThreadAtlas
                         })
                    Assert.That(open.Manifest.ValidEnd(stream),
                        Is.GreaterThan(0L), stream.ToString());
                Assert.That(replay.ReadDual(tile, 5),
                    Is.EqualTo(MerkabaDualReadResult.CertainThrough));
                Assert.That(replay.ValidFlowerDetailCount, Is.EqualTo(1));
                Assert.That(replay.ValidFlowerSkinMetricRunCount,
                    Is.EqualTo(1));
                Assert.That(replay.ValidFlowerVGroupCount, Is.EqualTo(1));
                Assert.That(replay.ValidThreadRunCount, Is.EqualTo(1));
                Assert.That(replay.ValidThreadColorGroupCount, Is.EqualTo(1));
                Assert.That(replay.ThreadProgramCount, Is.EqualTo(1));
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public async Task IdleCompaction_PublishesBaseGenerationWithoutChangingTruth()
        {
            string directory = TemporaryRoot("m8-base-compaction");
            var tile = new MerkabaTileAddress(new int3(-5, 2, -3),
                (uint)(7 | (19 << 9)));
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 11, new Color32(10, 40, 90, 255))
                });
                store.AppendSphereFlowerRecords(AllAuthorityRecords(tile, 11,
                    1u).ToArray());
                MerkabaStorageCommitResult committed = store.Commit(Session,
                    Anchor, AnchorPose, 5, 1);
                long fineEnd = store.CurrentCommittedState().Manifest.ValidEnd(
                    MerkabaStorageStream.FlowerDetail);
                long threadEnd = store.CurrentCommittedState().Manifest.ValidEnd(
                    MerkabaStorageStream.ThreadAtlas);

                Assert.That(store.CompactCommittedBases(), Is.True);
                MerkabaSessionOpenState compacted = store.CurrentCommittedState();
                Assert.That(compacted.Manifest.CommitGeneration,
                    Is.EqualTo(committed.Generation));
                Assert.That(compacted.Manifest.M8BaseGeneration,
                    Is.EqualTo(committed.Generation));
                Assert.That(compacted.Manifest.ThroughBaseGeneration,
                    Is.EqualTo(committed.Generation));
                Assert.That(compacted.Manifest.ValidEnd(
                    MerkabaStorageStream.M8Base), Is.GreaterThan(0L));
                Assert.That(compacted.Manifest.ValidEnd(
                    MerkabaStorageStream.ThroughBase), Is.GreaterThan(0L));
                Assert.That(compacted.Manifest.ValidEnd(
                    MerkabaStorageStream.M8Live), Is.Zero);
                Assert.That(compacted.Manifest.ValidEnd(
                    MerkabaStorageStream.ThroughLive), Is.Zero);
                Assert.That(compacted.Manifest.ValidEnd(
                    MerkabaStorageStream.FlowerDetail), Is.EqualTo(fineEnd));
                Assert.That(compacted.Manifest.ValidEnd(
                    MerkabaStorageStream.ThreadAtlas), Is.EqualTo(threadEnd));
                Assert.That(new FileInfo(store.M8LivePath).Length, Is.Zero);
                Assert.That(new FileInfo(store.ThroughLivePath).Length, Is.Zero);
                Assert.That(store.CompactCommittedBases(), Is.False,
                    "A clean base-only generation has no compaction work.");

                var reopened = new MerkabaSsdStore(directory);
                MerkabaSessionOpenState open = reopened.OpenCommitted();
                MerkabaTileSnapshot[] restored = await reopened.ReadAsync(
                    new[] { tile });
                Assert.That(open.Manifest.CommitGeneration,
                    Is.EqualTo(committed.Generation));
                Assert.That(restored[0].States[11].IsOccupied, Is.True);
                Assert.That(reopened.Subordinate.ReadDual(tile, 11),
                    Is.EqualTo(MerkabaDualReadResult.CertainThrough));
                Assert.That(reopened.Subordinate.ValidFlowerDetailCount,
                    Is.EqualTo(1));
                Assert.That(reopened.Subordinate.ValidFlowerSkinMetricRunCount,
                    Is.EqualTo(1));
                Assert.That(reopened.Subordinate.ValidFlowerVGroupCount,
                    Is.EqualTo(1));
                Assert.That(reopened.Subordinate.ValidThreadRunCount,
                    Is.EqualTo(1));
                Assert.That(reopened.Subordinate.ValidThreadColorGroupCount,
                    Is.EqualTo(1));
                Assert.That(reopened.Subordinate.ThreadProgramCount,
                    Is.EqualTo(1));
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [TestCase((int)MerkabaCompactionStage.BeforeRecoveryDataFlush, 0)]
        [TestCase((int)MerkabaCompactionStage.AfterRecoveryDataFlush, 0)]
        [TestCase((int)MerkabaCompactionStage.AfterRecoveryManifestFlush, 0)]
        [TestCase((int)MerkabaCompactionStage.AfterRecoveryManifestPublish, 1)]
        [TestCase((int)MerkabaCompactionStage.AfterM8BasePublish, 1)]
        [TestCase((int)MerkabaCompactionStage.AfterThroughBasePublish, 1)]
        [TestCase((int)MerkabaCompactionStage.AfterFinalManifestFlush, 1)]
        [TestCase((int)MerkabaCompactionStage.AfterFinalManifestPublish, 2)]
        public async Task IdleCompactionCrash_ReopensOldRecoveryOrFinalLayout(
            int stageValue, int expectedLayout)
        {
            string directory = TemporaryRoot("m8-compaction-crash");
            var tile = new MerkabaTileAddress(new int3(-1, -2, -3), 3u);
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 9, new Color32(3, 4, 5, 255))
                });
                store.AppendSphereFlowerRecords(new[]
                {
                    DualBlock(tile.BlockCoord,
                        MerkabaDualNodeState.AllThrough, 1u)
                });
                store.Commit(Session, Anchor, AnchorPose, 2, 1);
                long originalM8Live = store.CurrentCommittedState().Manifest
                    .ValidEnd(MerkabaStorageStream.M8Live);
                long originalDualLive = store.CurrentCommittedState().Manifest
                    .ValidEnd(MerkabaStorageStream.ThroughLive);

                var stage = (MerkabaCompactionStage)stageValue;
                Assert.Throws<IOException>(() => store.CompactCommittedBases(
                    reached =>
                    {
                        if (reached == stage)
                            throw new IOException("simulated compaction crash");
                    }));

                var reopened = new MerkabaSsdStore(directory);
                MerkabaSessionOpenState open = reopened.OpenCommitted();
                Assert.That(open.Manifest.CommitGeneration, Is.EqualTo(1ul));
                if (expectedLayout == 2)
                {
                    Assert.That(open.Manifest.ValidEnd(
                        MerkabaStorageStream.M8Base), Is.GreaterThan(0L));
                    Assert.That(open.Manifest.ValidEnd(
                        MerkabaStorageStream.ThroughBase), Is.GreaterThan(0L));
                    Assert.That(open.Manifest.ValidEnd(
                        MerkabaStorageStream.M8Live), Is.Zero);
                    Assert.That(open.Manifest.ValidEnd(
                        MerkabaStorageStream.ThroughLive), Is.Zero);
                }
                else
                {
                    Assert.That(open.Manifest.ValidEnd(
                        MerkabaStorageStream.M8Base), Is.Zero);
                    Assert.That(open.Manifest.ValidEnd(
                        MerkabaStorageStream.ThroughBase), Is.Zero);
                    Assert.That(open.Manifest.ValidEnd(
                            MerkabaStorageStream.M8Live), expectedLayout == 0
                        ? Is.EqualTo(originalM8Live)
                        : Is.GreaterThan(originalM8Live));
                    Assert.That(open.Manifest.ValidEnd(
                            MerkabaStorageStream.ThroughLive), expectedLayout == 0
                        ? Is.EqualTo(originalDualLive)
                        : Is.GreaterThan(originalDualLive));
                }
                MerkabaTileSnapshot[] restored = await reopened.ReadAsync(
                    new[] { tile });
                Assert.That(restored[0].States[9].IsOccupied, Is.True);
                Assert.That(reopened.Subordinate.ReadDual(tile, 9),
                    Is.EqualTo(MerkabaDualReadResult.CertainThrough));
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public async Task RecoveryManifest_PreservesDualTruthPreviouslyOnlyInBase()
        {
            string directory = TemporaryRoot("m8-compaction-base-dual-recovery");
            var tile = new MerkabaTileAddress(new int3(-4, 3, -2), 6u);
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 5, new Color32(1, 2, 3, 255))
                });
                store.AppendSphereFlowerRecords(new[]
                {
                    DualBlock(tile.BlockCoord,
                        MerkabaDualNodeState.AllThrough, 1u)
                });
                store.Commit(Session, Anchor, AnchorPose, 1, 1);
                Assert.That(store.CompactCommittedBases(), Is.True);
                Assert.That(store.CurrentCommittedState().Manifest.ValidEnd(
                    MerkabaStorageStream.ThroughLive), Is.Zero);

                // Two later M8 records make only the M8 live history worth
                // compacting. The dual truth still exists exclusively in the
                // previously published dual base at compaction entry.
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 6, new Color32(4, 5, 6, 255))
                });
                store.Commit(Session, Anchor, AnchorPose, 2, 1);
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 7, new Color32(7, 8, 9, 255))
                });
                store.Commit(Session, Anchor, AnchorPose, 3, 1);

                Assert.Throws<IOException>(() => store.CompactCommittedBases(
                    stage =>
                    {
                        if (stage == MerkabaCompactionStage
                                .AfterRecoveryManifestPublish)
                            throw new IOException("simulated recovery crash");
                    }));

                var reopened = new MerkabaSsdStore(directory);
                MerkabaSessionOpenState open = reopened.OpenCommitted();
                Assert.That(open.Manifest.CommitGeneration, Is.EqualTo(3ul));
                Assert.That(open.Manifest.ValidEnd(
                    MerkabaStorageStream.ThroughBase), Is.Zero);
                Assert.That(open.Manifest.ValidEnd(
                    MerkabaStorageStream.ThroughLive), Is.GreaterThan(0L));
                MerkabaTileSnapshot[] restored = await reopened.ReadAsync(
                    new[] { tile });
                Assert.That(restored[0].States[7].IsOccupied, Is.True);
                Assert.That(reopened.Subordinate.ReadDual(tile, 7),
                    Is.EqualTo(MerkabaDualReadResult.CertainThrough));
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public void IdleCompaction_ReplacesBaseInsteadOfAccumulatingSnapshots()
        {
            string directory = TemporaryRoot("m8-compaction-replaces");
            var tile = new MerkabaTileAddress(new int3(-2, 4, -6), 5u);
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 1, new Color32(1, 2, 3, 255))
                });
                store.Commit(Session, Anchor, AnchorPose, 1, 1);
                Assert.That(store.CompactCommittedBases(), Is.True);
                Assert.That(new FileInfo(store.M8BasePath).Length,
                    Is.EqualTo(MerkabaSsdStore.EncodedM8RecordBytes));

                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 2, new Color32(2, 3, 4, 255))
                });
                store.Commit(Session, Anchor, AnchorPose, 2, 1);
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 3, new Color32(3, 4, 5, 255))
                });
                store.Commit(Session, Anchor, AnchorPose, 3, 1);
                Assert.That(store.CompactCommittedBases(), Is.True);
                Assert.That(new FileInfo(store.M8BasePath).Length,
                    Is.EqualTo(MerkabaSsdStore.EncodedM8RecordBytes));
                Assert.That(new FileInfo(store.M8LivePath).Length, Is.Zero);
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public void IdleCompaction_IsByteDeterministicAcrossAppendOrder()
        {
            string firstDirectory = TemporaryRoot("m8-compact-order-a");
            string secondDirectory = TemporaryRoot("m8-compact-order-b");
            var negative = new MerkabaTileAddress(new int3(-3, 2, -1), 7u);
            var positive = new MerkabaTileAddress(new int3(4, -2, 5), 9u);
            try
            {
                var first = new MerkabaSsdStore(firstDirectory);
                first.AppendM8Tiles(new[]
                {
                    Tile(positive, 2, new Color32(2, 3, 4, 255)),
                    Tile(negative, 1, new Color32(1, 2, 3, 255))
                });
                first.AppendSphereFlowerRecords(new[]
                {
                    DualBlock(positive.BlockCoord,
                        MerkabaDualNodeState.AllThrough, 1u),
                    DualBlock(negative.BlockCoord,
                        MerkabaDualNodeState.AllThrough, 1u)
                });
                first.Commit(Session, Anchor, AnchorPose, 1, 2);
                Assert.That(first.CompactCommittedBases(), Is.True);

                var second = new MerkabaSsdStore(secondDirectory);
                second.AppendM8Tiles(new[]
                {
                    Tile(negative, 1, new Color32(1, 2, 3, 255)),
                    Tile(positive, 2, new Color32(2, 3, 4, 255))
                });
                second.AppendSphereFlowerRecords(new[]
                {
                    DualBlock(negative.BlockCoord,
                        MerkabaDualNodeState.AllThrough, 1u),
                    DualBlock(positive.BlockCoord,
                        MerkabaDualNodeState.AllThrough, 1u)
                });
                second.Commit(Session, Anchor, AnchorPose, 1, 2);
                Assert.That(second.CompactCommittedBases(), Is.True);

                Assert.That(File.ReadAllBytes(second.M8BasePath),
                    Is.EqualTo(File.ReadAllBytes(first.M8BasePath)));
                Assert.That(File.ReadAllBytes(second.ThroughBasePath),
                    Is.EqualTo(File.ReadAllBytes(first.ThroughBasePath)));
            }
            finally
            {
                DeleteRoot(firstDirectory);
                DeleteRoot(secondDirectory);
            }
        }

        [Test]
        public async Task AppendAfterCompaction_DiscardsManifestIgnoredTail()
        {
            string directory = TemporaryRoot("m8-compaction-tail");
            var tile = new MerkabaTileAddress(new int3(-2, 1, 3), 4u);
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 3, new Color32(1, 2, 3, 255))
                });
                store.AppendSphereFlowerRecords(new[]
                {
                    DualBlock(tile.BlockCoord,
                        MerkabaDualNodeState.AllThrough, 1u)
                });
                store.Commit(Session, Anchor, AnchorPose, 1, 1);
                Assert.That(store.CompactCommittedBases(), Is.True);

                File.WriteAllBytes(store.M8LivePath,
                    new byte[] { 0xde, 0xad, 0xbe, 0xef });
                File.WriteAllBytes(store.ThroughLivePath,
                    new byte[] { 0xfa, 0x11, 0xed });
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 3, new Color32(9, 8, 7, 255))
                });
                store.AppendSphereFlowerRecords(new[]
                {
                    DualBlock(tile.BlockCoord,
                        MerkabaDualNodeState.AllFull, 2u)
                });
                store.Commit(Session, Anchor, AnchorPose, 2, 1);

                var reopened = new MerkabaSsdStore(directory);
                reopened.OpenCommitted();
                MerkabaTileSnapshot[] restored = await reopened.ReadAsync(
                    new[] { tile });
                Assert.That(restored[0].States[3].IsOccupied, Is.True);
                Assert.That(reopened.Subordinate.ReadDual(tile, 3),
                    Is.EqualTo(MerkabaDualReadResult.CertainFull));
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public void Compaction_OmitsImplicitAllFullDualBlocks()
        {
            string directory = TemporaryRoot("m8-compact-implicit-full");
            var tile = new MerkabaTileAddress(new int3(-7, 2, 4), 0u);
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendSphereFlowerRecords(new[]
                {
                    DualBlock(tile.BlockCoord,
                        MerkabaDualNodeState.AllFull, 1u)
                });
                store.Commit(Session, Anchor, AnchorPose, 1, 0);
                Assert.That(store.CompactCommittedBases(), Is.True);
                Assert.That(new FileInfo(store.ThroughBasePath).Length,
                    Is.Zero);
                Assert.That(new FileInfo(store.ThroughLivePath).Length,
                    Is.Zero);

                var reopened = new MerkabaSsdStore(directory);
                reopened.OpenCommitted();
                Assert.That(reopened.Subordinate.ReadDual(tile, 17),
                    Is.EqualTo(MerkabaDualReadResult.CertainFull));
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public void IdleCompaction_RejectsAnUncommittedDirtyGeneration()
        {
            string directory = TemporaryRoot("m8-compaction-dirty");
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(new MerkabaTileAddress(new int3(1, -1, 1), 0u),
                        2, new Color32(1, 2, 3, 255))
                });
                Assert.Throws<InvalidOperationException>(() =>
                    store.CompactCommittedBases());
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public async Task IdleCompactionCancellationBeforePublish_RestoresOldTail()
        {
            string directory = TemporaryRoot("m8-compaction-cancel");
            var tile = new MerkabaTileAddress(new int3(-1, 2, -3), 0u);
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 8, new Color32(1, 2, 3, 255))
                });
                store.Commit(Session, Anchor, AnchorPose, 1, 1);
                long committedLive = new FileInfo(store.M8LivePath).Length;
                using var cancellation = new CancellationTokenSource();

                Assert.Throws<OperationCanceledException>(() =>
                    store.CompactCommittedBases(stage =>
                    {
                        if (stage == MerkabaCompactionStage
                                .BeforeRecoveryDataFlush)
                            cancellation.Cancel();
                    }, cancellation.Token));

                Assert.That(new FileInfo(store.M8LivePath).Length,
                    Is.EqualTo(committedLive));
                var reopened = new MerkabaSsdStore(directory);
                reopened.OpenCommitted();
                MerkabaTileSnapshot[] restored = await reopened.ReadAsync(
                    new[] { tile });
                Assert.That(restored[0].States[8].IsOccupied, Is.True);
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public void CloneCommitted_CopiesEveryAuthorityPrefixByteExactly()
        {
            string sourceDirectory = TemporaryRoot("m8-clone-source");
            string destinationDirectory = TemporaryRoot("m8-clone-destination");
            Guid destinationSession = Guid.NewGuid();
            var tile = new MerkabaTileAddress(new int3(-2, 1, -1), 0u);
            try
            {
                var source = new MerkabaSsdStore(sourceDirectory);
                source.AppendM8Tiles(new[]
                {
                    Tile(tile, 5, new Color32(10, 20, 30, 255))
                });
                source.AppendSphereFlowerRecords(AllAuthorityRecords(tile, 5,
                    1u).ToArray());
                source.Commit(Session, Anchor, AnchorPose, 9, 1);
                source.CloneCommittedTo(destinationDirectory,
                    destinationSession);

                var destination = new MerkabaSsdStore(destinationDirectory);
                MerkabaSessionOpenState opened = destination.OpenCommitted();
                Assert.That(opened.Manifest.SessionUuid,
                    Is.EqualTo(destinationSession));
                Assert.That(opened.Manifest.AnchorUuid, Is.EqualTo(Anchor));

                (string source, string destination)[] streams =
                {
                    (source.M8BasePath, destination.M8BasePath),
                    (source.M8LivePath, destination.M8LivePath),
                    (source.ThroughBasePath, destination.ThroughBasePath),
                    (source.ThroughLivePath, destination.ThroughLivePath),
                    (source.FlowerDetailPath, destination.FlowerDetailPath),
                    (source.ThreadAtlasPath, destination.ThreadAtlasPath)
                };
                foreach ((string sourcePath, string destinationPath) in streams)
                {
                    if (!File.Exists(sourcePath))
                    {
                        Assert.That(File.Exists(destinationPath), Is.False);
                        continue;
                    }
                    Assert.That(File.ReadAllBytes(destinationPath),
                        Is.EqualTo(File.ReadAllBytes(sourcePath)));
                }
            }
            finally
            {
                DeleteRoot(sourceDirectory);
                DeleteRoot(destinationDirectory);
            }
        }

        [Test]
        public void DualMixedMutation_PreservesUnchangedDescendantsAndUniformBarrier()
        {
            string directory = TemporaryRoot("m8-dual-generations");
            int3 block = new(-3, 2, -1);
            var tileA = new MerkabaTileAddress(block, (uint)(0 | (2 << 9)));
            var tileB = new MerkabaTileAddress(block, (uint)(1 | (3 << 9)));
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendSphereFlowerRecords(new[]
                {
                    DualBlock(block, MerkabaDualNodeState.Mixed, 1u),
                    DualChildren(block, 0),
                    DualChunk(tileA, 1u),
                    DualLeaf(tileA, 5)
                });
                store.Commit(Session, Anchor, AnchorPose, 1, 0);

                // The parent and child-state records advance, while the
                // untouched chunk/leaf from generation one remain authoritative.
                store.AppendSphereFlowerRecords(new[]
                {
                    DualBlock(block, MerkabaDualNodeState.Mixed, 2u),
                    DualChildren(block, 0, 1),
                    DualChunk(tileB, 2u),
                    DualLeaf(tileB, 9)
                });
                store.Commit(Session, Anchor, AnchorPose, 2, 0);

                var generationTwo = new MerkabaSsdStore(directory);
                generationTwo.OpenCommitted();
                Assert.That(generationTwo.Subordinate.ReadDual(tileA, 5),
                    Is.EqualTo(MerkabaDualReadResult.CertainThrough));
                Assert.That(generationTwo.Subordinate.ReadDual(tileB, 9),
                    Is.EqualTo(MerkabaDualReadResult.CertainThrough));

                // A uniform ancestor is an exact descendant tombstone. A later
                // expansion must start clean and cannot resurrect tile A.
                generationTwo.AppendSphereFlowerRecords(new[]
                {
                    DualBlock(block, MerkabaDualNodeState.AllThrough, 3u)
                });
                generationTwo.Commit(Session, Anchor, AnchorPose, 3, 0);
                generationTwo.AppendSphereFlowerRecords(new[]
                {
                    DualBlock(block, MerkabaDualNodeState.Mixed, 4u),
                    DualChildren(block, 1),
                    DualChunk(tileB, 4u),
                    DualLeaf(tileB, 10)
                });
                generationTwo.Commit(Session, Anchor, AnchorPose, 4, 0);

                var generationFour = new MerkabaSsdStore(directory);
                generationFour.OpenCommitted();
                Assert.That(generationFour.Subordinate.ReadDual(tileA, 5),
                    Is.EqualTo(MerkabaDualReadResult.CertainFull));
                Assert.That(generationFour.Subordinate.ReadDual(tileB, 9),
                    Is.EqualTo(MerkabaDualReadResult.CertainFull));
                Assert.That(generationFour.Subordinate.ReadDual(tileB, 10),
                    Is.EqualTo(MerkabaDualReadResult.CertainThrough));
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public void DualExpansionFloor_RejectsOutOfOrderStaleDescendants()
        {
            int3 block = new(-4, 3, -2);
            var staleTile = new MerkabaTileAddress(block,
                (uint)(0 | (1 << 9)));
            var liveTile = new MerkabaTileAddress(block,
                (uint)(0 | (2 << 9)));
            var replay = new MerkabaSphereFlowerReplayIndex();
            var generationSix = new MerkabaRecordVersion(6ul, 0ul);

            replay.Apply(DualBlock(block, MerkabaDualNodeState.Mixed, 6u),
                generationSix);
            replay.Apply(DualChildren(block, 0),
                new MerkabaRecordVersion(6ul, 1ul));
            replay.Apply(DualChunk(liveTile, 6u),
                new MerkabaRecordVersion(6ul, 2ul));
            replay.Apply(DualLeaf(liveTile, 9),
                new MerkabaRecordVersion(6ul, 3ul));

            replay.Apply(DualLeaf(staleTile, 5),
                new MerkabaRecordVersion(5ul, 99ul));
            replay.ValidateClosedHierarchy(_ => false);

            Assert.That(replay.ReadDual(staleTile, 5),
                Is.EqualTo(MerkabaDualReadResult.CertainFull));
            Assert.That(replay.ReadDual(liveTile, 9),
                Is.EqualTo(MerkabaDualReadResult.CertainThrough));
        }

        [Test]
        public void IncompleteMixedDualHierarchy_CannotPublishManifest()
        {
            string directory = TemporaryRoot("m8-dual-incomplete");
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendSphereFlowerRecords(new[]
                {
                    DualBlock(new int3(1, -2, 3),
                        MerkabaDualNodeState.Mixed, 1u)
                });
                Assert.Throws<InvalidDataException>(() => store.Commit(Session,
                    Anchor, AnchorPose, 1, 0));
                Assert.That(File.Exists(store.ManifestPath), Is.False);
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public void NonCollapsedUniformDualNodes_AreRejected()
        {
            int3 block = new(2, -3, 4);
            var tile = new MerkabaTileAddress(block, 0u);

            var uniformBlock = new MerkabaSphereFlowerReplayIndex();
            uniformBlock.Apply(DualBlock(block,
                    MerkabaDualNodeState.Mixed, 1u),
                new MerkabaRecordVersion(1ul, 0ul));
            uniformBlock.Apply(DualChildren(block),
                new MerkabaRecordVersion(1ul, 1ul));
            Assert.Throws<InvalidDataException>(() =>
                uniformBlock.ValidateClosedHierarchy(_ => false));

            var uniformChunk = new MerkabaSphereFlowerReplayIndex();
            uniformChunk.Apply(DualBlock(block,
                    MerkabaDualNodeState.Mixed, 1u),
                new MerkabaRecordVersion(1ul, 0ul));
            uniformChunk.Apply(DualChildren(block, 0),
                new MerkabaRecordVersion(1ul, 1ul));
            uniformChunk.Apply(DualUniformChunk(tile,
                    MerkabaDualNodeState.AllFull, 1u),
                new MerkabaRecordVersion(1ul, 2ul));
            Assert.Throws<InvalidDataException>(() =>
                uniformChunk.ValidateClosedHierarchy(_ => false));
        }

        [Test]
        public void ParentEpochChange_RejectsStaleFineTruthWithoutTraversal()
        {
            string directory = TemporaryRoot("m8-parent-epoch");
            var tile = new MerkabaTileAddress(new int3(0), 0u);
            MerkabaFlowerDetailKey key = DetailKey();
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 7, new Color32(1, 2, 3, 255))
                });
                store.AppendSphereFlowerRecords(new[]
                {
                    OwnerEpoch(tile, 7, 1u),
                    Detail(tile, 7, key, 1u)
                });
                store.Commit(Session, Anchor, AnchorPose, 1, 1);
                Assert.That(store.Subordinate.TryGetFine(tile, 7,
                    MerkabaRecordKind.FlowerDetail, key.Value, out _), Is.True);

                store.AppendSphereFlowerRecords(new[]
                {
                    OwnerEpoch(tile, 7, 2u)
                });
                store.Commit(Session, Anchor, AnchorPose, 2, 1);
                Assert.That(store.Subordinate.TryGetFine(tile, 7,
                    MerkabaRecordKind.FlowerDetail, key.Value, out _), Is.False);

                var reopened = new MerkabaSsdStore(directory);
                reopened.OpenCommitted();
                Assert.That(reopened.Subordinate.TryGetFine(tile, 7,
                    MerkabaRecordKind.FlowerDetail, key.Value, out _), Is.False);
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public void ParentEpoch_CannotDecreaseOrSkip()
        {
            var tile = new MerkabaTileAddress(new int3(-1, 0, 1), 0u);
            var replay = new MerkabaSphereFlowerReplayIndex();
            replay.Apply(OwnerEpoch(tile, 7, 1u),
                new MerkabaRecordVersion(1ul, 0ul));
            replay.Apply(OwnerEpoch(tile, 7, 2u),
                new MerkabaRecordVersion(2ul, 0ul));

            Assert.Throws<InvalidDataException>(() => replay.Apply(
                OwnerEpoch(tile, 7, 1u),
                new MerkabaRecordVersion(3ul, 0ul)));
            Assert.Throws<InvalidDataException>(() => replay.Apply(
                OwnerEpoch(tile, 7, 4u),
                new MerkabaRecordVersion(3ul, 1ul)));
            Assert.That(replay.TryGetOwnerEpoch(tile, 7, out uint epoch),
                Is.True);
            Assert.That(epoch, Is.EqualTo(2u));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ParentEpoch_CompleteHistorySnapshotCoalescesAndSurvivesOpen(
            bool historyAlreadyAppended)
        {
            string directory = TemporaryRoot("m8-epoch-coalesced-snapshot");
            var tile = new MerkabaTileAddress(new int3(-1, 0, 1), 0u);
            MerkabaFlowerDetailKey key = DetailKey();
            try
            {
                var store = new MerkabaSsdStore(directory);
                if (historyAlreadyAppended)
                {
                    store.AppendM8Tiles(new[] { Tile(tile, 7, new Color32(1, 2, 3, 255)) });
                    store.AppendSphereFlowerRecords(new[]
                    {
                        OwnerEpoch(tile, 7, 1u), Detail(tile, 7, key, 1u)
                    });
                    store.Commit(Session, Anchor, AnchorPose, 1, 1);
                }

                // The source really contained fine state, then three structural
                // invalidations before storage captured its complete image.
                // Capture emits its history epoch but no stale descendants.
                MerkabaAppendRecord[] captured = CapturedRetiredOwner(tile, 7, 4u);
                Assert.That(captured, Has.Length.EqualTo(1));
                Assert.That(captured[0].OwnerEpochSnapshot, Is.True);
                var snapshot = EmptyTile(tile);
                snapshot.Sidecars = captured;
                store.AppendObservationBatch(new[] { snapshot }, captured,
                    completeFineImages: true);
                store.Commit(Session, Anchor, AnchorPose, 2, 0);

                var reopened = new MerkabaSsdStore(directory);
                reopened.OpenCommitted();
                Assert.That(reopened.Subordinate.TryGetOwnerEpoch(tile, 7, out uint epoch), Is.True);
                Assert.That(epoch, Is.EqualTo(4u));
                Assert.That(reopened.Subordinate.TryGetFine(tile, 7,
                    MerkabaRecordKind.FlowerDetail, key.Value, out _), Is.False);
                Assert.That(reopened.Subordinate.CaptureTile(tile), Has.Length.EqualTo(1));
                Assert.Throws<InvalidDataException>(() => reopened.AppendSphereFlowerRecords(
                    new[] { OwnerEpoch(tile, 7, 3u) }));
                Assert.Throws<InvalidDataException>(() => reopened.AppendSphereFlowerRecords(
                    new[] { OwnerEpoch(tile, 7, 6u) }));
                reopened.OpenCommitted();
                Assert.That(reopened.Subordinate.TryGetOwnerEpoch(tile, 7, out epoch), Is.True);
                Assert.That(epoch, Is.EqualTo(4u));
                Assert.That(reopened.Subordinate.ValidFlowerDetailCount, Is.Zero);
            }
            finally { DeleteRoot(directory); }
        }

        [Test]
        public void ParentEpoch_CaptureReceiptRequiresItsCompleteTileImage()
        {
            string directory = TemporaryRoot("m8-epoch-snapshot-scope");
            var tile = new MerkabaTileAddress(new int3(-1, 0, 1), 0u);
            try
            {
                var store = new MerkabaSsdStore(directory);
                MerkabaAppendRecord[] captured = CapturedRetiredOwner(tile, 7, 4u);
                Assert.Throws<InvalidDataException>(() => store.AppendSphereFlowerRecords(captured));
                Assert.Throws<InvalidDataException>(() => store.AppendObservationBatch(
                    new[] { EmptyTile(tile) }, captured, completeFineImages: true));
                var snapshot = EmptyTile(tile);
                snapshot.Sidecars = captured;
                Assert.Throws<InvalidDataException>(() => store.AppendObservationBatch(
                    new[] { snapshot }, Array.Empty<MerkabaAppendRecord>(), completeFineImages: true));
                Assert.That(store.Subordinate.TryGetOwnerEpoch(tile, 7, out _), Is.False);
                Assert.That(File.Exists(store.ManifestPath), Is.False);
            }
            finally { DeleteRoot(directory); }
        }

        [Test]
        public void ThreadRunWithoutCommittedProgram_CannotPublishManifest()
        {
            string directory = TemporaryRoot("m8-thread-program-reference");
            var tile = new MerkabaTileAddress(new int3(0), 0u);
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 7, new Color32(1, 2, 3, 255))
                });
                store.AppendSphereFlowerRecords(new[]
                {
                    OwnerEpoch(tile, 7, 1u),
                    ThreadRun(tile, 7, FlowerKey(), 91u, 1u)
                });

                Assert.Throws<InvalidDataException>(() => store.Commit(Session,
                    Anchor, AnchorPose, 1, 1));
                Assert.That(File.Exists(store.ManifestPath), Is.False);
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public void SkinSplitWithoutAtomicSevenChildGroup_CannotPublishManifest()
        {
            string directory = TemporaryRoot("m8-skin-atomic-group");
            var tile = new MerkabaTileAddress(new int3(0), 0u);
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 7, new Color32(1, 2, 3, 255))
                });
                byte[] run = new byte[MerkabaFlowerSkinMetricRun.ByteSize];
                Write32(run, 0, FlowerKey().Value);
                Write32(run, 4, 9u);
                Write32(run, 8, 1u);
                Write32(run, 16, 1u);
                store.AppendSphereFlowerRecords(new[]
                {
                    OwnerEpoch(tile, 7, 1u),
                    new MerkabaAppendRecord(
                        MerkabaRecordKind.FlowerSkinMetricRun,
                        OwnerAddress(tile, 7), run)
                });

                Assert.Throws<InvalidDataException>(() => store.Commit(Session,
                    Anchor, AnchorPose, 1, 1));
                Assert.That(File.Exists(store.ManifestPath), Is.False);
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SkinRun_CannotBorrowAnotherRunsNewerGroups(bool metric)
        {
            string directory = TemporaryRoot("m8-skin-generation-alias");
            var tile = new MerkabaTileAddress(new int3(-1, 0, 1),
                511u | (63u << 9));
            MerkabaRecordKind runKind = metric
                ? MerkabaRecordKind.FlowerSkinMetricRun
                : MerkabaRecordKind.ThreadRun;
            MerkabaRecordKind groupKind = metric
                ? MerkabaRecordKind.FlowerVGroup
                : MerkabaRecordKind.ThreadColorGroup;
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 511, new Color32(1, 2, 3, 255))
                });
                MerkabaAppendRecord[] records = AllAuthorityRecords(tile,
                    511, 1u).ToArray();
                store.AppendSphereFlowerRecords(records);
                store.Commit(Session, Anchor, AnchorPose, 1, 1);

                MerkabaAppendRecord original = records.Single(record =>
                    record.Kind == runKind);
                byte[] secondRun = (byte[])original.Payload.Clone();
                uint secondKey = MerkabaFlowerL2Key.Create(1, 0, false,
                    0).Value;
                Write32(secondRun, 0, secondKey);
                // An independent L2 tries to reuse the first L2's group
                // address in a later generation. The old run must not be
                // validated through the new run's group reference.
                store.AppendSphereFlowerRecords(new[]
                {
                    new MerkabaAppendRecord(runKind, original.Address,
                        secondRun),
                    records.Single(record => record.Kind == groupKind)
                });
                Assert.Throws<InvalidDataException>(() => store.Commit(
                    Session, Anchor, AnchorPose, 2, 1));

                var reopened = new MerkabaSsdStore(directory);
                Assert.That(reopened.OpenCommitted().Manifest.CommitGeneration,
                    Is.EqualTo(1ul));
                Assert.That(reopened.Subordinate.TryGetFine(tile, 511,
                    runKind, FlowerKey().Value, out byte[] restored), Is.True);
                Assert.That(restored, Is.EqualTo(original.Payload));
                Assert.That(reopened.Subordinate.TryGetFine(tile, 511,
                    runKind, secondKey, out _), Is.False);
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SkinRun_WrappedGroupRangeIsRejectedBeforeReplay(bool metric)
        {
            var tile = new MerkabaTileAddress(new int3(0), 0u);
            MerkabaRecordKind kind = metric
                ? MerkabaRecordKind.FlowerSkinMetricRun
                : MerkabaRecordKind.ThreadRun;
            MerkabaAppendRecord original = AllAuthorityRecords(tile, 0,
                1u).Single(record => record.Kind == kind);
            byte[] payload = (byte[])original.Payload.Clone();
            Write32(payload, metric ? 4 : 8, uint.MaxValue - 1u);
            Write32(payload, metric ? 8 : 12, 7u); // three child groups
            var record = new MerkabaAppendRecord(kind, original.Address,
                payload);
            var replay = new MerkabaSphereFlowerReplayIndex();
            Assert.Throws<InvalidDataException>(() => replay.Apply(record,
                new MerkabaRecordVersion(1ul, 1ul)));
        }

        [Test]
        public void FineTombstone_RemainsAuthoritativeAfterReplay()
        {
            string directory = TemporaryRoot("m8-fine-tombstone");
            var tile = new MerkabaTileAddress(new int3(0), 0u);
            MerkabaFlowerDetailKey key = DetailKey();
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 7, new Color32(1, 2, 3, 255))
                });
                store.AppendSphereFlowerRecords(new[]
                {
                    OwnerEpoch(tile, 7, 1u),
                    Detail(tile, 7, key, 1u)
                });
                store.Commit(Session, Anchor, AnchorPose, 1, 1);
                store.AppendSphereFlowerRecords(new[]
                {
                    Tombstone(tile, 7, MerkabaRecordKind.FlowerDetail,
                        key.Value)
                });
                store.Commit(Session, Anchor, AnchorPose, 2, 1);

                var reopened = new MerkabaSsdStore(directory);
                reopened.OpenCommitted();
                Assert.That(reopened.Subordinate.TryGetFine(tile, 7,
                    MerkabaRecordKind.FlowerDetail, key.Value, out _), Is.False);
                Assert.That(reopened.Subordinate.ValidFlowerDetailCount, Is.Zero);
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public void FineTruthWithoutCanonicalR1Owner_CannotPublishManifest()
        {
            string directory = TemporaryRoot("m8-orphan-fine");
            var tile = new MerkabaTileAddress(new int3(0), 0u);
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 5, new Color32(1, 2, 3, 255))
                });
                store.AppendSphereFlowerRecords(new[]
                {
                    OwnerEpoch(tile, 6, 1u),
                    Detail(tile, 6, DetailKey(), 1u)
                });
                Assert.Throws<InvalidDataException>(() => store.Commit(Session,
                    Anchor, AnchorPose, 1, 1));
                Assert.That(File.Exists(store.ManifestPath), Is.False);
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public void FineTruthWithoutSparseOwnerEpoch_CannotPublishManifest()
        {
            string directory = TemporaryRoot("m8-fine-without-epoch");
            var tile = new MerkabaTileAddress(new int3(0), 0u);
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 5, new Color32(1, 2, 3, 255))
                });
                store.AppendSphereFlowerRecords(new[]
                {
                    Detail(tile, 5, DetailKey(), 1u)
                });

                Assert.Throws<InvalidDataException>(() => store.Commit(Session,
                    Anchor, AnchorPose, 1, 1));
                Assert.That(File.Exists(store.ManifestPath), Is.False);
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SparseOwnerEpochWithoutFineHistory_CannotPublishManifest(
            bool completeFineImages)
        {
            string directory = TemporaryRoot("m8-epoch-without-fine");
            var tile = new MerkabaTileAddress(new int3(0), 0u);
            try
            {
                var store = new MerkabaSsdStore(directory);
                var snapshot = Tile(tile, 5, new Color32(1, 2, 3, 255));
                snapshot.Sidecars = new[] { OwnerEpoch(tile, 5, 1u) };
                store.AppendObservationBatch(new[] { snapshot }, snapshot.Sidecars,
                    completeFineImages);

                Assert.Throws<InvalidDataException>(() => store.Commit(Session,
                    Anchor, AnchorPose, 1, 1));
                Assert.Throws<InvalidDataException>(() => store.Subordinate.CaptureTile(tile));
                Assert.That(File.Exists(store.ManifestPath), Is.False);
                var reopened = new MerkabaSsdStore(directory);
                Assert.Throws<FileNotFoundException>(() => reopened.OpenCommitted());
                Assert.That(reopened.Subordinate.TryGetOwnerEpoch(tile, 5, out _), Is.False);
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public void ParentDelete_WithEpochAdvanceLeavesNoLiveFineTruth()
        {
            string directory = TemporaryRoot("m8-parent-delete");
            var tile = new MerkabaTileAddress(new int3(0), 0u);
            MerkabaFlowerDetailKey key = DetailKey();
            try
            {
                var store = new MerkabaSsdStore(directory);
                store.AppendM8Tiles(new[]
                {
                    Tile(tile, 7, new Color32(1, 2, 3, 255))
                });
                store.AppendSphereFlowerRecords(new[]
                {
                    OwnerEpoch(tile, 7, 1u),
                    Detail(tile, 7, key, 1u)
                });
                store.Commit(Session, Anchor, AnchorPose, 1, 1);

                store.AppendM8Tiles(new[] { EmptyTile(tile) });
                store.AppendSphereFlowerRecords(new[]
                {
                    OwnerEpoch(tile, 7, 2u)
                });
                store.Commit(Session, Anchor, AnchorPose, 2, 0);

                var reopened = new MerkabaSsdStore(directory);
                reopened.OpenCommitted();
                Assert.That(reopened.Subordinate.ValidFlowerDetailCount, Is.Zero);
                Assert.That(reopened.Subordinate.TryGetFine(tile, 7,
                    MerkabaRecordKind.FlowerDetail, key.Value, out _), Is.False);
            }
            finally
            {
                DeleteRoot(directory);
            }
        }

        [Test]
        public async Task StoredScanProximity_UsesNearestTileNotWholeBlock()
        {
            string directory = TemporaryRoot("m8-proximity");
            var store = new MerkabaSsdStore(directory);
            MerkabaSpatial.Address firstAddress = MerkabaSpatial.Encode(
                new int3(0, 0, 0));
            MerkabaSpatial.Address secondAddress = MerkabaSpatial.Encode(
                new int3(MerkabaSpatial.TileSize, 0, 0));
            try
            {
                await store.AppendM8TilesAsync(new[]
                {
                    EmptyTile(firstAddress), EmptyTile(secondAddress)
                });

                Assert.That(store.TryFindNearestStoredTile(
                    new float3(0.5f, 0.1f, 0.1f), out float3 closest,
                    out float distance), Is.True);
                Assert.That(distance, Is.EqualTo(0.1f).Within(1.0e-5f));
                Assert.That(closest.x, Is.EqualTo(0.4f).Within(1.0e-5f));
            }
            finally
            {
                store.Clear();
                DeleteRoot(directory);
            }
        }

        [Test]
        public void ExplicitLoad_RegistersOnlyManifestIndexedAddresses()
        {
            string storage = Source("Runtime/Merkaba/MerkabaGrid.Storage.cs");
            Assert.That(storage, Does.Contain("LoadCommittedStorageAsync"));
            Assert.That(storage, Does.Contain("SnapshotSortedAddresses()"));
            Assert.That(storage, Does.Contain("RegisterLoadedTileAddresses"));
            Assert.That(storage, Does.Contain("CounterHashFull"));
            Assert.That(storage, Does.Contain(
                "addressedTiles != (ulong)registeredTiles.Count"));
            Assert.That(storage, Does.Not.Contain("LoadStoredSnapshotAsync"));
            Assert.That(storage, Does.Not.Contain("ReadCanonicalSnapshot"));
        }

        [Test]
        public void AnchoredResume_ValidatesManifestBeforeWorldRegistration()
        {
            string persistence = Source("Runtime/Merkaba/MerkabaPersistence.cs");
            int readManifest = persistence.IndexOf(
                "ReadManifestFromDirectory(directory)", StringComparison.Ordinal);
            int switchRoot = persistence.IndexOf(
                "SwitchStorageRootAsync(directory, false, true, progress",
                StringComparison.Ordinal);
            int replayValidation = persistence.IndexOf(
                "candidate.Manifest.CommitGeneration", switchRoot,
                StringComparison.Ordinal);
            int localize = persistence.IndexOf(
                "EnsureSessionAnchorAsync(\n                                    session.AnchorId, false)",
                replayValidation, StringComparison.Ordinal);
            int loadWorld = persistence.IndexOf(
                "LoadCommittedStorageAsync(opened, progress)",
                StringComparison.Ordinal);
            Assert.That(readManifest, Is.GreaterThanOrEqualTo(0));
            Assert.That(switchRoot, Is.GreaterThan(readManifest));
            Assert.That(replayValidation, Is.GreaterThan(switchRoot));
            Assert.That(localize, Is.GreaterThan(replayValidation));
            Assert.That(loadWorld, Is.GreaterThan(localize));
            Assert.That(persistence, Does.Contain("opened.Manifest.AnchorAtSave"));
            Assert.That(persistence, Does.Not.Contain("merkaba-grid.bin"));
        }

        [Test]
        public void AnchoredResume_RelocatesGridFromSavedToLocalizedPose()
        {
            var owner = new GameObject("anchored-grid-fixture");
            owner.transform.SetPositionAndRotation(new Vector3(1f, 2f, -3f),
                Quaternion.Euler(0f, 17f, 0f));
            Matrix4x4 sceneGrid = owner.transform.localToWorldMatrix;
            MerkabaGrid grid = owner.AddComponent<MerkabaGrid>();
            typeof(MerkabaGrid).GetMethod("Awake",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(grid, null);
            Matrix4x4 saved = Matrix4x4.TRS(new Vector3(4f, 0.5f, -2f),
                Quaternion.Euler(0f, 31f, 0f), Vector3.one);
            Matrix4x4 localized = Matrix4x4.TRS(
                new Vector3(-6f, 1.25f, 8f),
                Quaternion.Euler(0f, -23f, 0f), Vector3.one);
            try
            {
                grid.RelocateForLoadedAnchor(localized, saved);
                Matrix4x4 expected = localized * saved.inverse * sceneGrid;
                Assert.That(Vector3.Distance(owner.transform.position,
                    expected.GetColumn(3)), Is.LessThan(1e-5f));
                Assert.That(Quaternion.Angle(owner.transform.rotation,
                    expected.rotation), Is.LessThan(1e-4f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void LegacyCheckpointAuthority_IsAbsent()
        {
            string store = Source("Runtime/Merkaba/MerkabaSsdStore.cs");
            string persistence = Source("Runtime/Merkaba/MerkabaPersistence.cs");
            string catalog = Source("Runtime/Merkaba/MerkabaSessionCatalog.cs");
            foreach (string source in new[] { store, persistence, catalog })
            {
                Assert.That(source, Does.Not.Contain("MerkabaSessionSnapshot"));
                Assert.That(source, Does.Not.Contain("merkaba-grid.bin"));
                Assert.That(source, Does.Not.Contain("PublishCheckpoint"));
                Assert.That(source, Does.Not.Contain("MigrateLegacy"));
            }
        }

        [Test]
        public void DurablePublication_IsFileFsyncRenameDirectoryFsync()
        {
            string persistence = Source("Runtime/Merkaba/MerkabaPersistence.cs");
            string publish = persistence.Substring(persistence.IndexOf(
                "internal static class MerkabaFilePublishing",
                StringComparison.Ordinal));
            Assert.That(publish, Does.Contain("RenameAtomic"));
            Assert.That(publish, Does.Contain("FlushParentDirectory(destination)"));
            Assert.That(publish, Does.Contain("SyncFileDescriptor(descriptor)"));
        }

        private static IEnumerable<MerkabaAppendRecord> AllAuthorityRecords(
            MerkabaTileAddress tile, int kernel, uint epoch)
        {
            int3 block = tile.BlockCoord;
            byte[] blockAddress = new byte[
                MerkabaSphereFlowerPersistenceAbi.BlockAddressBytes];
            MerkabaSphereFlowerPersistenceAbi.WriteBlockAddress(blockAddress,
                block);
            byte[] blockPayload = new byte[MerkabaDualBlockMeta.ByteSize];
            Write32(blockPayload, 0, (1u << 2) |
                (uint)MerkabaDualNodeState.Mixed);
            Write32(blockPayload, 4, 0u);
            yield return new MerkabaAppendRecord(MerkabaRecordKind.DualBlock,
                blockAddress, blockPayload);

            byte[] children = new byte[MerkabaDualBlockChildren.ByteSize];
            int childShift = (tile.ChunkLocal & 15) * 2;
            int childWord = tile.ChunkLocal >> 4;
            Write32(children, childWord * 4,
                (uint)MerkabaDualNodeState.Mixed << childShift);
            yield return new MerkabaAppendRecord(
                MerkabaRecordKind.DualBlockChildren, blockAddress, children);

            byte[] chunkAddress = new byte[
                MerkabaSphereFlowerPersistenceAbi.ChunkAddressBytes];
            MerkabaSphereFlowerPersistenceAbi.WriteChunkAddress(chunkAddress,
                block, tile.ChunkLocal);
            byte[] chunk = new byte[MerkabaDualChunkPayload.ByteSize];
            ulong bit = 1ul << tile.TileLocal;
            Write64(chunk, 0, bit);
            Write64(chunk, 8, bit);
            Write32(chunk, 16, 0u);
            Write32(chunk, 20, 1u);
            yield return new MerkabaAppendRecord(MerkabaRecordKind.DualChunk,
                chunkAddress, chunk);

            byte[] tileAddress = TileAddress(tile);
            byte[] leaf = new byte[MerkabaDualLeaf.ByteSize];
            Write32(leaf, (kernel >> 5) * 4, 1u << (kernel & 31));
            yield return new MerkabaAppendRecord(MerkabaRecordKind.DualLeaf,
                tileAddress, leaf);

            yield return OwnerEpoch(tile, kernel, epoch);
            MerkabaFlowerDetailKey detailKey = DetailKey();
            yield return Detail(tile, kernel, detailKey, epoch);
            MerkabaFlowerL2Key flowerKey = FlowerKey();

            byte[] ownerAddress = OwnerAddress(tile, kernel);
            byte[] metricRun = new byte[MerkabaFlowerSkinMetricRun.ByteSize];
            Write32(metricRun, 0, flowerKey.Value);
            Write32(metricRun, 4, 17u);
            Write32(metricRun, 8, 1u);
            Write32(metricRun, 16, epoch);
            yield return new MerkabaAppendRecord(
                MerkabaRecordKind.FlowerSkinMetricRun, ownerAddress,
                metricRun);

            byte[] vGroup = new byte[MerkabaFlowerVGroup.ByteSize];
            for (int child = 0; child < 7; child++)
            {
                Write32(vGroup, child * 8, 1u);
                Write32(vGroup, child * 8 + 4, 2u);
            }
            yield return new MerkabaAppendRecord(MerkabaRecordKind.FlowerVGroup,
                GroupAddress(tile, kernel, 17u), vGroup);

            byte[] programAddress = new byte[
                MerkabaSphereFlowerPersistenceAbi.ProgramAddressBytes];
            MerkabaSphereFlowerPersistenceAbi.WriteProgramAddress(
                programAddress, 7u);
            yield return new MerkabaAppendRecord(
                MerkabaRecordKind.ThreadProgram, programAddress,
                new byte[MerkabaThreadProgramRecord.ByteSize]);

            byte[] run = new byte[MerkabaThreadRun.ByteSize];
            Write32(run, 0, flowerKey.Value);
            Write32(run, 4, 7u);
            Write32(run, 8, 23u);
            Write32(run, 12, 1u);
            Write32(run, 20, epoch);
            yield return new MerkabaAppendRecord(MerkabaRecordKind.ThreadRun,
                ownerAddress, run);

            byte[] colors = new byte[MerkabaThreadColorGroup.ByteSize];
            yield return new MerkabaAppendRecord(
                MerkabaRecordKind.ThreadColorGroup,
                GroupAddress(tile, kernel, 23u), colors);
        }

        private static MerkabaAppendRecord OwnerEpoch(MerkabaTileAddress tile,
            int kernel, uint epoch)
        {
            byte[] payload = new byte[MerkabaFlowerOwnerEpoch.ByteSize];
            Write32(payload, 0, (uint)kernel);
            Write32(payload, 4, epoch);
            return new MerkabaAppendRecord(
                MerkabaRecordKind.FlowerOwnerEpoch, TileAddress(tile), payload);
        }

        private static MerkabaAppendRecord[] CapturedRetiredOwner(
            MerkabaTileAddress tile, int kernel, uint finalEpoch)
        {
            var source = new MerkabaSphereFlowerReplayIndex();
            source.Apply(OwnerEpoch(tile, kernel, 1u), new MerkabaRecordVersion(1ul, 0ul));
            source.Apply(Detail(tile, kernel, DetailKey(), 1u), new MerkabaRecordVersion(1ul, 1ul));
            for (uint epoch = 2u; epoch <= finalEpoch; epoch++)
                source.Apply(OwnerEpoch(tile, kernel, epoch), new MerkabaRecordVersion(epoch, 0ul));
            MerkabaAppendRecord[] image = source.CaptureTile(tile);
            return MerkabaFlowerPageStorage.DecodeCaptureRecords(tile,
                MerkabaFlowerPageStorage.EncodeLoadRecords(tile, image));
        }

        private static MerkabaAppendRecord DualBlock(int3 block,
            MerkabaDualNodeState state, uint nodeGeneration)
        {
            byte[] address = new byte[
                MerkabaSphereFlowerPersistenceAbi.BlockAddressBytes];
            MerkabaSphereFlowerPersistenceAbi.WriteBlockAddress(address, block);
            byte[] payload = new byte[MerkabaDualBlockMeta.ByteSize];
            Write32(payload, 0, (nodeGeneration << 2) | (uint)state);
            Write32(payload, 4, state == MerkabaDualNodeState.Mixed
                ? 0u : MerkabaDualBlockMeta.NoPayload);
            return new MerkabaAppendRecord(MerkabaRecordKind.DualBlock,
                address, payload);
        }

        private static MerkabaAppendRecord DualChildren(int3 block,
            params int[] mixedChunks)
        {
            byte[] address = new byte[
                MerkabaSphereFlowerPersistenceAbi.BlockAddressBytes];
            MerkabaSphereFlowerPersistenceAbi.WriteBlockAddress(address, block);
            byte[] payload = new byte[MerkabaDualBlockChildren.ByteSize];
            foreach (int chunk in mixedChunks)
            {
                int shift = (chunk & 15) * 2;
                int offset = (chunk >> 4) * 4;
                uint word = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(
                    payload, offset);
                Write32(payload, offset, word |
                    ((uint)MerkabaDualNodeState.Mixed << shift));
            }
            return new MerkabaAppendRecord(
                MerkabaRecordKind.DualBlockChildren, address, payload);
        }

        private static MerkabaAppendRecord DualChunk(MerkabaTileAddress tile,
            uint nodeGeneration)
        {
            byte[] address = new byte[
                MerkabaSphereFlowerPersistenceAbi.ChunkAddressBytes];
            MerkabaSphereFlowerPersistenceAbi.WriteChunkAddress(address,
                tile.BlockCoord, tile.ChunkLocal);
            byte[] payload = new byte[MerkabaDualChunkPayload.ByteSize];
            ulong bit = 1ul << tile.TileLocal;
            Write64(payload, 0, bit);
            Write64(payload, 8, bit);
            Write32(payload, 16, 0u);
            Write32(payload, 20, nodeGeneration);
            return new MerkabaAppendRecord(MerkabaRecordKind.DualChunk,
                address, payload);
        }

        private static MerkabaAppendRecord DualUniformChunk(
            MerkabaTileAddress tile, MerkabaDualNodeState state,
            uint nodeGeneration)
        {
            byte[] address = new byte[
                MerkabaSphereFlowerPersistenceAbi.ChunkAddressBytes];
            MerkabaSphereFlowerPersistenceAbi.WriteChunkAddress(address,
                tile.BlockCoord, tile.ChunkLocal);
            byte[] payload = new byte[MerkabaDualChunkPayload.ByteSize];
            if (state == MerkabaDualNodeState.AllThrough)
            {
                Write64(payload, 0, ulong.MaxValue);
                Write64(payload, 8, 0ul);
            }
            else if (state != MerkabaDualNodeState.AllFull)
                throw new ArgumentOutOfRangeException(nameof(state));
            Write32(payload, 16, MerkabaDualBlockMeta.NoPayload);
            Write32(payload, 20, nodeGeneration);
            return new MerkabaAppendRecord(MerkabaRecordKind.DualChunk,
                address, payload);
        }

        private static MerkabaAppendRecord DualLeaf(MerkabaTileAddress tile,
            params int[] throughKernels)
        {
            byte[] payload = new byte[MerkabaDualLeaf.ByteSize];
            foreach (int kernel in throughKernels)
            {
                int offset = (kernel >> 5) * 4;
                uint word = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(
                    payload, offset);
                Write32(payload, offset, word | (1u << (kernel & 31)));
            }
            return new MerkabaAppendRecord(MerkabaRecordKind.DualLeaf,
                TileAddress(tile), payload);
        }

        private static MerkabaAppendRecord Tombstone(MerkabaTileAddress tile,
            int kernel, MerkabaRecordKind target, uint localKey)
        {
            byte[] payload = new byte[MerkabaTombstoneRecord.ByteSize];
            Write32(payload, 0, (uint)target);
            Write32(payload, 4, localKey);
            return new MerkabaAppendRecord(MerkabaRecordKind.Tombstone,
                OwnerAddress(tile, kernel), payload);
        }

        private static MerkabaAppendRecord Detail(MerkabaTileAddress tile,
            int kernel, MerkabaFlowerDetailKey key, uint epoch)
        {
            byte[] payload = new byte[MerkabaFlowerDetailRecord.ByteSize];
            Write32(payload, 0, key.Value);
            Write32(payload, 4, 1u);
            Write32(payload, 8, 2u);
            Write32(payload, 12, epoch);
            return new MerkabaAppendRecord(MerkabaRecordKind.FlowerDetail,
                OwnerAddress(tile, kernel), payload);
        }

        private static MerkabaAppendRecord ThreadRun(MerkabaTileAddress tile,
            int kernel, MerkabaFlowerL2Key key, uint programRef,
            uint epoch)
        {
            byte[] payload = new byte[MerkabaThreadRun.ByteSize];
            Write32(payload, 0, key.Value);
            Write32(payload, 4, programRef);
            Write32(payload, 8, MerkabaThreadRun.InvalidRef);
            Write32(payload, 20, epoch);
            return new MerkabaAppendRecord(MerkabaRecordKind.ThreadRun,
                OwnerAddress(tile, kernel), payload);
        }

        private static MerkabaFlowerDetailKey DetailKey() =>
            MerkabaFlowerDetailKey.Create(1, 0, 0, 3,
                MerkabaFlowerDetailKind.R2Phase, false, 0);

        private static MerkabaFlowerL2Key FlowerKey() =>
            MerkabaFlowerL2Key.Create(0, 0, false, 0);

        private static byte[] TileAddress(MerkabaTileAddress tile)
        {
            var result = new byte[
                MerkabaSphereFlowerPersistenceAbi.TileAddressBytes];
            MerkabaSphereFlowerPersistenceAbi.WriteTileAddress(result, tile);
            return result;
        }

        private static byte[] OwnerAddress(MerkabaTileAddress tile, int kernel)
        {
            var result = new byte[
                MerkabaSphereFlowerPersistenceAbi.OwnerAddressBytes];
            MerkabaSphereFlowerPersistenceAbi.WriteOwnerAddress(result, tile,
                kernel);
            return result;
        }

        private static byte[] GroupAddress(MerkabaTileAddress tile, int kernel,
            uint group)
        {
            var result = new byte[
                MerkabaSphereFlowerPersistenceAbi.GroupAddressBytes];
            MerkabaSphereFlowerPersistenceAbi.WriteGroupAddress(result, tile,
                kernel, group);
            return result;
        }

        private static void Write32(byte[] target, int offset, uint value) =>
            MerkabaSphereFlowerPersistenceAbi.WriteUInt32(target, offset, value);

        private static void Write64(byte[] target, int offset, ulong value) =>
            MerkabaSphereFlowerPersistenceAbi.WriteUInt64(target, offset, value);

        private static MerkabaSessionManifest ManifestFixture()
        {
            var result = new MerkabaSessionManifest
            {
                SessionUuid = Session,
                AnchorUuid = Anchor,
                AnchorAtSave = AnchorPose,
                CommitGeneration = 7ul,
                IntegrationCount = 47,
                OccupiedKernelCount = 12u,
                CanonicalTileCount = 2u,
                M8BaseGeneration = 2ul,
                ThroughBaseGeneration = 3ul
            };
            for (int i = 0; i < result.ValidEnds.Length; i++)
                result.ValidEnds[i] = 101L + i;
            return result;
        }

        private static byte[] WriteManifest(MerkabaSessionManifest manifest)
        {
            using var stream = new MemoryStream();
            MerkabaSessionManifest.Write(stream, manifest);
            return stream.ToArray();
        }

        private static MerkabaTileSnapshot Tile(MerkabaTileAddress address,
            int occupiedKernel, Color32 color)
        {
            var states = new KernelState[MerkabaSpatial.KernelsPerTile];
            states[occupiedKernel].SetOccupiedForFixture(true, color);
            states[occupiedKernel].Flags = KernelState.SetSurfacePlane(
                states[occupiedKernel].Flags,
                math.normalize(new float3(1f, 0f, 1f)), 0.006f);
            return new MerkabaTileSnapshot
            {
                Address = address,
                States = states
            };
        }

        private static MerkabaTileSnapshot EmptyTile(
            MerkabaSpatial.Address address) => new()
        {
            Address = new MerkabaTileAddress(address.BlockCoord,
                address.LocalAddress),
            States = new KernelState[MerkabaSpatial.KernelsPerTile]
        };

        private static MerkabaTileSnapshot EmptyTile(
            MerkabaTileAddress address) => new()
        {
            Address = address,
            States = new KernelState[MerkabaSpatial.KernelsPerTile]
        };

        private static string TemporaryRoot(string prefix) => Path.Combine(
            Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));

        private static void DeleteRoot(string directory)
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private static string Source(string relative) => File.ReadAllText(
            Path.GetFullPath("Packages/com.genesis.roomscan/" + relative));
    }
}
