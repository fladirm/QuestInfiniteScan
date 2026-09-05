using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaSessionCatalogTests
    {
        [Test]
        public void CatalogWritesNamedSessionMetadataUnderUuidRoot()
        {
            string root = TemporaryRoot();
            Guid anchor = Guid.NewGuid();
            try
            {
                var catalog = new MerkabaSessionCatalog(root);
                MerkabaSessionInfo session = catalog.Create(anchor,
                    "North Wing");

                Assert.That(session.Id, Is.Not.EqualTo(Guid.Empty));
                Assert.That(session.AnchorId, Is.EqualTo(anchor));
                Assert.That(session.displayName, Is.EqualTo("North Wing"));
                string directory = catalog.SessionDirectory(session.Id);
                Assert.That(Path.GetFileName(directory),
                    Is.EqualTo(session.Id.ToString("N")));
                Assert.That(File.Exists(Path.Combine(directory,
                    MerkabaSessionCatalog.MetadataFileName)), Is.True);
                Assert.That(catalog.Read(session.Id).AnchorId,
                    Is.EqualTo(anchor));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public async Task IndependentSessionStoresNeverExposeOtherSessionTiles()
        {
            string root = TemporaryRoot();
            try
            {
                var catalog = new MerkabaSessionCatalog(root);
                MerkabaSessionInfo a = catalog.Create(Guid.NewGuid(), "A");
                MerkabaSessionInfo b = catalog.Create(Guid.NewGuid(), "B");
                var storeA = new MerkabaSsdStore(
                    catalog.SessionDirectory(a.Id));
                var storeB = new MerkabaSsdStore(
                    catalog.SessionDirectory(b.Id));
                MerkabaTileSnapshot tileA = Tile(new int3(-1, 0, 2), 9);
                MerkabaTileSnapshot tileB = Tile(new int3(4, -3, 1), 17);
                await storeA.AppendAsync(new[] { tileA });
                await storeB.AppendAsync(new[] { tileB });
                await storeA.RebuildIndexAsync();
                await storeB.RebuildIndexAsync();

                Assert.That(storeA.SnapshotSortedAddresses(),
                    Is.EqualTo(new[] { tileA.Address }));
                Assert.That(storeB.SnapshotSortedAddresses(),
                    Is.EqualTo(new[] { tileB.Address }));
                Assert.That(storeA.SnapshotSortedAddresses()
                    .Contains(tileB.Address), Is.False);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public async Task LegacyCheckpointMovesIntoOneRecoverableNamedSession()
        {
            string root = TemporaryRoot();
            Guid anchor = Guid.NewGuid();
            try
            {
                Directory.CreateDirectory(root);
                var legacy = new MerkabaSsdStore(root);
                var snapshot = new MerkabaSessionSnapshot
                {
                    AnchorUuid = anchor,
                    AnchorAtSave = Matrix4x4.identity,
                    IntegrationCount = 3
                };
                snapshot.Tiles.Add(Tile(new int3(0, 0, 0), 1));
                await legacy.PublishCheckpointAsync(snapshot);

                var catalog = new MerkabaSessionCatalog(root);
                MerkabaSessionInfo[] sessions = catalog.List().ToArray();

                Assert.That(sessions, Has.Length.EqualTo(1));
                Assert.That(sessions[0].AnchorId, Is.EqualTo(anchor));
                Assert.That(File.Exists(legacy.CheckpointPath), Is.False);
                Assert.That(File.Exists(Path.Combine(
                    catalog.SessionDirectory(sessions[0].Id),
                    "merkaba-grid.bin")), Is.True);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public async Task NamedStoresAndSaveAsRemainIndependentAndKeepAnchor()
        {
            string root = TemporaryRoot();
            Guid anchor = Guid.NewGuid();
            try
            {
                var catalog = new MerkabaSessionCatalog(root);
                MerkabaSessionInfo a = catalog.Create(anchor, "A");
                MerkabaSessionInfo b = catalog.Create(Guid.NewGuid(), "B");
                var snapshotA = new MerkabaSessionSnapshot
                {
                    AnchorUuid = anchor,
                    AnchorAtSave = Matrix4x4.identity,
                    IntegrationCount = 7
                };
                snapshotA.Tiles.Add(Tile(new int3(-2, 1, 3), 11));
                var snapshotB = new MerkabaSessionSnapshot
                {
                    AnchorUuid = b.AnchorId,
                    AnchorAtSave = Matrix4x4.identity,
                    IntegrationCount = 9
                };
                snapshotB.Tiles.Add(Tile(new int3(8, -1, 0), 23));
                var storeA = new MerkabaSsdStore(
                    catalog.SessionDirectory(a.Id));
                var storeB = new MerkabaSsdStore(
                    catalog.SessionDirectory(b.Id));
                await storeA.PublishCheckpointAsync(snapshotA);
                await storeB.PublishCheckpointAsync(snapshotB);
                catalog.MarkSaved(a);
                catalog.MarkSaved(b);

                var reopenedStoreA = new MerkabaSsdStore(
                    catalog.SessionDirectory(a.Id));
                await reopenedStoreA.RebuildIndexAsync();
                MerkabaSessionSnapshot reopenedA = await reopenedStoreA
                    .ReadCanonicalSnapshotAsync(anchor, Matrix4x4.identity, 7);
                Assert.That(reopenedA.Tiles.Select(tile => tile.Address),
                    Is.EqualTo(new[] { snapshotA.Tiles[0].Address }));
                Assert.That(reopenedA.Tiles.Select(tile => tile.Address)
                    .Contains(snapshotB.Tiles[0].Address), Is.False);

                MerkabaSessionInfo copy = catalog.Create(anchor, "A copy");
                string copiedCheckpoint = Path.Combine(
                    catalog.SessionDirectory(copy.Id), "merkaba-grid.bin");
                await Task.Run(() => MerkabaPersistence.CopyFileDurable(
                    storeA.CheckpointPath, copiedCheckpoint));
                string sourceDesign = Path.Combine(
                    catalog.SessionDirectory(a.Id),
                    MerkabaSessionCatalog.DesignFileName);
                string copiedDesign = Path.Combine(
                    catalog.SessionDirectory(copy.Id),
                    MerkabaSessionCatalog.DesignFileName);
                File.WriteAllText(sourceDesign, "{\"formatVersion\":1}");
                await Task.Run(() => MerkabaPersistence.CopyFileDurable(
                    sourceDesign, copiedDesign));
                catalog.MarkSaved(copy);
                MerkabaSessionSnapshot reopenedCopy = await Task.Run(() =>
                {
                    using var input = new FileStream(copiedCheckpoint,
                        FileMode.Open, FileAccess.Read, FileShare.Read);
                    return MerkabaSsdStore.ReadCheckpoint(input);
                });
                Assert.That(copy.AnchorId, Is.EqualTo(anchor));
                Assert.That(reopenedCopy.AnchorUuid, Is.EqualTo(anchor));
                Assert.That(reopenedCopy.Tiles.Select(tile => tile.Address),
                    Is.EqualTo(new[] { snapshotA.Tiles[0].Address }));
                Assert.That(File.ReadAllText(copiedDesign),
                    Is.EqualTo("{\"formatVersion\":1}"));

                catalog.Rename(copy, "A archive");
                Assert.That(catalog.Read(copy.Id).displayName,
                    Is.EqualTo("A archive"));
                catalog.Delete(b.Id);
                Assert.That(catalog.List().Any(session => session.Id == b.Id),
                    Is.False);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void SessionFlowUsesMetadataFirstAndBoundedStoreSwitching()
        {
            string persistence = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Merkaba/" +
                "MerkabaPersistence.cs"));
            int metadata = persistence.IndexOf(
                "MerkabaSessionInfo session = _catalog.Read(sessionId)",
                StringComparison.Ordinal);
            int anchor = persistence.IndexOf(
                "EnsureSessionAnchorAsync(\n" +
                "                        session.AnchorId, false)",
                metadata, StringComparison.Ordinal);
            int switchRoot = persistence.IndexOf(
                "SwitchStorageRootAsync(directory, false, true)", anchor,
                StringComparison.Ordinal);
            int checkpoint = persistence.IndexOf(
                "ReadCheckpointSnapshotAsync(progress)", switchRoot,
                StringComparison.Ordinal);
            Assert.That(metadata, Is.GreaterThanOrEqualTo(0));
            Assert.That(anchor, Is.GreaterThan(metadata));
            Assert.That(switchRoot, Is.GreaterThan(anchor));
            Assert.That(checkpoint, Is.GreaterThan(switchRoot));
            Assert.That(persistence, Does.Contain(
                "public async Task<bool> SaveAsAsync(string displayName)"));
            Assert.That(persistence, Does.Contain(
                "CopyFileDurable(sourceCheckpoint"));
            Assert.That(persistence, Does.Not.Contain("File.Copy("));

            string storage = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Merkaba/" +
                "MerkabaGrid.Storage.cs"));
            string change = storage.Substring(storage.IndexOf(
                "internal async Task SwitchStorageRootAsync",
                StringComparison.Ordinal));
            Assert.That(change, Does.Contain("_storageReplacementPending = true"));
            Assert.That(change, Does.Contain("await loadTask"));
            Assert.That(change, Does.Contain("await writeTask"));
            Assert.That(change, Does.Contain("ClearGpuWorldForNewScan()"));
            Assert.That(change, Does.Contain("_ssdStore = replacement"));
            Assert.That(change, Does.Contain(
                "When the GPU world survives (SAVE AS)"));
        }

        private static MerkabaTileSnapshot Tile(int3 blockCoord,
            int kernel)
        {
            var states = new KernelState[MerkabaSpatial.KernelsPerTile];
            states[kernel].Apply(MerkabaObservationKind.Surface, 1f,
                new Color32(12, 34, 56, 255));
            return new MerkabaTileSnapshot
            {
                Address = new MerkabaTileAddress(blockCoord, (uint)kernel),
                States = states
            };
        }

        private static string TemporaryRoot() => Path.Combine(
            Path.GetTempPath(), "merkaba-sessions-" +
            Guid.NewGuid().ToString("N"));
    }
}
