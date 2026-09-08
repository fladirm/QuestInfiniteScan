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
        public void Catalog_WritesMetadataUnderUuidRoot()
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
                DeleteRoot(root);
            }
        }

        [Test]
        public async Task IndependentStores_NeverExposeOtherSessionTiles()
        {
            string root = TemporaryRoot();
            try
            {
                var catalog = new MerkabaSessionCatalog(root);
                MerkabaSessionInfo a = catalog.Create(Guid.NewGuid(), "A");
                MerkabaSessionInfo b = catalog.Create(Guid.NewGuid(), "B");
                MerkabaTileSnapshot tileA = Tile(new int3(-1, 0, 2), 9);
                MerkabaTileSnapshot tileB = Tile(new int3(4, -3, 1), 17);
                var storeA = new MerkabaSsdStore(
                    catalog.SessionDirectory(a.Id));
                var storeB = new MerkabaSsdStore(
                    catalog.SessionDirectory(b.Id));
                await storeA.AppendM8TilesAsync(new[] { tileA });
                await storeB.AppendM8TilesAsync(new[] { tileB });
                await storeA.CommitAsync(a.Id, a.AnchorId,
                    Matrix4x4.identity, 1, 1);
                await storeB.CommitAsync(b.Id, b.AnchorId,
                    Matrix4x4.identity, 1, 1);

                var reopenedA = new MerkabaSsdStore(
                    catalog.SessionDirectory(a.Id));
                var reopenedB = new MerkabaSsdStore(
                    catalog.SessionDirectory(b.Id));
                reopenedA.OpenCommitted();
                reopenedB.OpenCommitted();
                Assert.That(reopenedA.SnapshotSortedAddresses(),
                    Is.EqualTo(new[] { tileA.Address }));
                Assert.That(reopenedB.SnapshotSortedAddresses(),
                    Is.EqualTo(new[] { tileB.Address }));
                Assert.That(reopenedA.SnapshotSortedAddresses()
                    .Contains(tileB.Address), Is.False);
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        [Test]
        public void LegacyRootFiles_AreNotMigratedOrInterpreted()
        {
            string root = TemporaryRoot();
            try
            {
                Directory.CreateDirectory(root);
                string legacy = Path.Combine(root, "merkaba-grid.bin");
                File.WriteAllBytes(legacy, new byte[] { 1, 2, 3, 4 });
                var catalog = new MerkabaSessionCatalog(root);

                Assert.That(catalog.List(), Is.Empty);
                Assert.That(File.Exists(legacy), Is.True);
                Assert.That(Directory.Exists(catalog.SessionsRoot), Is.True);
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        [Test]
        public async Task CloneCommitted_ChangesOnlySessionIdentityAndKeepsAnchor()
        {
            string root = TemporaryRoot();
            Guid anchor = Guid.NewGuid();
            try
            {
                var catalog = new MerkabaSessionCatalog(root);
                MerkabaSessionInfo source = catalog.Create(anchor, "A");
                var sourceStore = new MerkabaSsdStore(
                    catalog.SessionDirectory(source.Id));
                MerkabaTileSnapshot tile = Tile(new int3(-2, 1, 3), 11);
                sourceStore.AppendM8Tiles(new[] { tile });
                sourceStore.Commit(source.Id, anchor, Matrix4x4.identity,
                    7, 1);
                catalog.MarkSaved(source);

                MerkabaSessionInfo copy = catalog.Create(anchor, "A copy");
                string copyDirectory = catalog.SessionDirectory(copy.Id);
                await sourceStore.CloneCommittedToAsync(copyDirectory, copy.Id);
                string sourceDesign = Path.Combine(
                    catalog.SessionDirectory(source.Id),
                    MerkabaSessionCatalog.DesignFileName);
                string copiedDesign = Path.Combine(copyDirectory,
                    MerkabaSessionCatalog.DesignFileName);
                File.WriteAllText(sourceDesign, "{\"formatVersion\":1}");
                await Task.Run(() => MerkabaPersistence.CopyFileDurable(
                    sourceDesign, copiedDesign));
                catalog.MarkSaved(copy);

                var copyStore = new MerkabaSsdStore(copyDirectory);
                MerkabaSessionOpenState opened = copyStore.OpenCommitted();
                Assert.That(opened.Manifest.SessionUuid, Is.EqualTo(copy.Id));
                Assert.That(opened.Manifest.AnchorUuid, Is.EqualTo(anchor));
                Assert.That(opened.Manifest.IntegrationCount, Is.EqualTo(7));
                Assert.That(copyStore.SnapshotSortedAddresses(),
                    Is.EqualTo(new[] { tile.Address }));
                Assert.That(File.ReadAllText(copiedDesign),
                    Is.EqualTo("{\"formatVersion\":1}"));

                catalog.Rename(copy, "A archive");
                Assert.That(catalog.Read(copy.Id).displayName,
                    Is.EqualTo("A archive"));
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        [Test]
        public void Catalog_RejectsManifestMetadataIdentityMismatch()
        {
            string root = TemporaryRoot();
            try
            {
                var catalog = new MerkabaSessionCatalog(root);
                MerkabaSessionInfo session = catalog.Create(Guid.NewGuid(),
                    "Mismatch");
                var store = new MerkabaSsdStore(
                    catalog.SessionDirectory(session.Id));
                store.Commit(Guid.NewGuid(), session.AnchorId,
                    Matrix4x4.identity, 0, 0);

                Assert.Throws<InvalidDataException>(() =>
                    catalog.Read(session.Id));
                Assert.That(catalog.List().Any(value =>
                    value.Id == session.Id), Is.False);
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        [Test]
        public void SessionFlow_IsManifestFirstAndSnapshotFree()
        {
            string persistence = Source("Runtime/Merkaba/MerkabaPersistence.cs");
            int metadata = persistence.IndexOf(
                "MerkabaSessionInfo session = _catalog.Read(sessionId)",
                StringComparison.Ordinal);
            int manifest = persistence.IndexOf(
                "ReadManifestFromDirectory(directory)", metadata,
                StringComparison.Ordinal);
            int switchRoot = persistence.IndexOf(
                "SwitchStorageRootAsync(directory, false, true, progress",
                manifest,
                StringComparison.Ordinal);
            int replayValidation = persistence.IndexOf(
                "candidate.Manifest.CommitGeneration",
                switchRoot, StringComparison.Ordinal);
            int anchor = persistence.IndexOf(
                "EnsureSessionAnchorAsync(\n                                    session.AnchorId, false)",
                replayValidation, StringComparison.Ordinal);
            int load = persistence.IndexOf(
                "LoadCommittedStorageAsync(opened, progress)", anchor,
                StringComparison.Ordinal);
            Assert.That(metadata, Is.GreaterThanOrEqualTo(0));
            Assert.That(manifest, Is.GreaterThan(metadata));
            Assert.That(switchRoot, Is.GreaterThan(manifest));
            Assert.That(replayValidation, Is.GreaterThan(switchRoot));
            Assert.That(anchor, Is.GreaterThan(replayValidation));
            Assert.That(load, Is.GreaterThan(anchor));
            Assert.That(persistence, Does.Contain(
                "CloneCommittedStorageAsync(destinationDirectory"));
            Assert.That(persistence, Does.Not.Contain("MerkabaSessionSnapshot"));
            Assert.That(persistence, Does.Not.Contain("merkaba-grid.bin"));

            string storage = Source(
                "Runtime/Merkaba/MerkabaGrid.Storage.cs");
            Assert.That(storage, Does.Contain(
                "internal async Task<MerkabaSessionOpenState> " +
                "SwitchStorageRootAsync"));
            Assert.That(storage, Does.Contain("await loadTask"));
            Assert.That(storage, Does.Contain("await writeTask"));
            Assert.That(storage, Does.Contain("OpenCommittedAsync"));
            int openCandidate = storage.IndexOf(
                "opened = await replacement.OpenCommittedAsync(openProgress)",
                StringComparison.Ordinal);
            int adoptionBarrier = storage.IndexOf(
                "await beforeAdoption(opened)", openCandidate,
                StringComparison.Ordinal);
            int adoptStore = storage.IndexOf("_ssdStore = replacement",
                adoptionBarrier, StringComparison.Ordinal);
            Assert.That(openCandidate, Is.GreaterThanOrEqualTo(0));
            Assert.That(adoptionBarrier, Is.GreaterThan(openCandidate));
            Assert.That(adoptStore, Is.GreaterThan(adoptionBarrier));
            Assert.That(storage, Does.Contain("ClearGpuWorldForNewScan()"));
        }

        // ClearGpuWorldForNewScan refuses a leased dual GPU world, and the
        // native readout job is submitted every rendered frame once the
        // scanner is ready. Quiescing scanning alone therefore never reaches a
        // clearable state: on device this surfaced as "Cannot clear a leased
        // dual GPU world" the first time a new session was requested after all
        // 26 pipelines created. Every path that replaces the world must wait
        // the in-flight job out, which closure 4.7 requires over a CPU flag.
        [Test]
        public void WorldReplacingSessionPaths_WaitOutTheNativeLeaseBeforeClearing()
        {
            string scanner = Source("Runtime/Core/RoomScanner.cs");
            int helper = scanner.IndexOf(
                "private async Task<bool> QuiesceForWorldClearAsync()",
                StringComparison.Ordinal);
            Assert.That(helper, Is.GreaterThanOrEqualTo(0),
                "the world-clear quiesce helper must exist");

            int suspend = scanner.IndexOf("_renderer?.SuspendGpuSubmission()",
                helper, StringComparison.Ordinal);
            int quiesce = scanner.IndexOf("await QuiesceScanningAsync()",
                suspend, StringComparison.Ordinal);
            int readout = scanner.IndexOf(
                "await _renderer.FinishCurrentReadoutAsync()", quiesce,
                StringComparison.Ordinal);
            int durable = scanner.IndexOf(
                "await _grid.FinishObservationDurableCutAsync()", readout,
                StringComparison.Ordinal);
            Assert.That(suspend, Is.GreaterThan(helper),
                "new native readout jobs must stop before the wait");
            Assert.That(quiesce, Is.GreaterThan(suspend));
            Assert.That(readout, Is.GreaterThan(quiesce),
                "the in-flight readout job must be awaited, not preempted");
            Assert.That(durable, Is.GreaterThan(readout));

            foreach (string entry in new[]
            {
                "public async Task<bool> OpenSessionAsync(Guid sessionId)",
                "public async Task<bool> DeleteSessionAsync(Guid sessionId)",
                "public async Task NewClearAsync(string displayName)"
            })
            {
                int start = scanner.IndexOf(entry, StringComparison.Ordinal);
                Assert.That(start, Is.GreaterThanOrEqualTo(0), entry);
                int guard = scanner.IndexOf("await QuiesceForWorldClearAsync()",
                    start, StringComparison.Ordinal);
                int resume = scanner.IndexOf("ResumeSubmissionAfterWorldClear()",
                    start, StringComparison.Ordinal);
                Assert.That(guard, Is.GreaterThan(start),
                    entry + " must quiesce the native lease before clearing");
                Assert.That(resume, Is.GreaterThan(guard),
                    entry + " must resume renderer submission afterwards");
            }

            // SAVE AS keeps the GPU world, so it must not stall the renderer.
            int saveAs = scanner.IndexOf(
                "public async Task<bool> SaveAsAsync(string displayName)",
                StringComparison.Ordinal);
            int saveAsEnd = scanner.IndexOf("public bool RenameActiveSession",
                saveAs, StringComparison.Ordinal);
            Assert.That(saveAs, Is.GreaterThanOrEqualTo(0));
            Assert.That(scanner.IndexOf("QuiesceForWorldClearAsync", saveAs,
                    StringComparison.Ordinal), Is.Not.InRange(saveAs, saveAsEnd),
                "SAVE AS retains the GPU world and must not quiesce for a clear");
        }

        // PumpStorage gates its GPU section on HasJobInFlight, and the
        // dirty-page readout job lives about 42 ms and is resubmitted every
        // rendered frame, so once the native scanner is ready it holds the
        // queue continuously. On device a save then never left "Counting dirty
        // canonical tiles". Closure 4.7 orders bounded work FINE/ERASE ->
        // observation -> dirty page -> WARM, so the readout must yield to a
        // pending canonical flush rather than starve it.
        [Test]
        public void DirtyPageReadout_YieldsToAPendingCanonicalFlush()
        {
            string storage = Source("Runtime/Merkaba/MerkabaGrid.Storage.cs");
            Assert.That(storage, Does.Contain(
                "internal bool HasPendingCanonicalFlush => _flushCompletion != null;"),
                "the pending canonical flush must be observable by the renderer");
            int pump = storage.IndexOf("private void PumpStorage()",
                StringComparison.Ordinal);
            int gate = storage.IndexOf(
                "MerkabaNativeVulkanExecutor.HasJobInFlight) return;", pump,
                StringComparison.Ordinal);
            Assert.That(gate, Is.GreaterThan(pump),
                "PumpStorage still defers its GPU section to the native queue");

            string renderer = Source("Runtime/Merkaba/MerkabaGridRenderer.cs");
            int submit = renderer.IndexOf(
                "private void OnContextRendered(", StringComparison.Ordinal);
            int yields = renderer.IndexOf("_grid.HasPendingCanonicalFlush",
                submit, StringComparison.Ordinal);
            int quantum = renderer.IndexOf("SubmitPageQuantum(camera)", submit,
                StringComparison.Ordinal);
            Assert.That(submit, Is.GreaterThanOrEqualTo(0));
            Assert.That(yields, Is.GreaterThan(submit),
                "page publication must yield to a pending canonical flush");
            Assert.That(quantum, Is.GreaterThan(yields),
                "the yield must be decided before a new page quantum is submitted");
        }

        private static MerkabaTileSnapshot Tile(int3 blockCoord, int kernel)
        {
            var states = new KernelState[MerkabaSpatial.KernelsPerTile];
            states[kernel].SetOccupiedForFixture(true,
                new Color32(12, 34, 56, 255));
            states[kernel].Flags = KernelState.SetSurfacePlane(states[kernel].Flags,
                new float3(0, 0, 1), 0f);
            return new MerkabaTileSnapshot
            {
                Address = new MerkabaTileAddress(blockCoord, (uint)kernel),
                States = states
            };
        }

        private static string Source(string relative) => File.ReadAllText(
            Path.GetFullPath("Packages/com.genesis.roomscan/" + relative));

        private static string TemporaryRoot() => Path.Combine(
            Path.GetTempPath(), "merkaba-sessions-" +
            Guid.NewGuid().ToString("N"));

        private static void DeleteRoot(string root)
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
