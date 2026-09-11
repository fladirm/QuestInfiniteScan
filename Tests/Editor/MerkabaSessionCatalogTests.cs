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

        // A canonical M8 tile is complete at every instant and a running scan
        // only refines it, so a storage or transfer operation takes the world
        // as it stands rather than waiting for work to finish. That requires
        // both producers to stop: the scan, and the dirty-page readout job,
        // which is resubmitted every rendered frame once the native scanner is
        // ready. On device, leaving it running made a new session fail with
        // "Cannot clear a leased dual GPU world" and made a save crawl on
        // "Counting dirty canonical tiles". Closure 4.7 requires the in-flight
        // job be waited out, not preempted by a CPU flag.
        [Test]
        public void EveryFrozenWorldOperation_StopsTheScanAndTheReadout()
        {
            string scanner = Source("Runtime/Core/RoomScanner.cs");
            int helper = scanner.IndexOf(
                "private async Task<bool> BeginFrozenWorldOperationAsync()",
                StringComparison.Ordinal);
            Assert.That(helper, Is.GreaterThanOrEqualTo(0),
                "the frozen-world operation helper must exist");

            int suspend = scanner.IndexOf("_renderer?.PausePagePublication()",
                helper, StringComparison.Ordinal);
            int quiesce = scanner.IndexOf("await QuiesceScanningAsync()",
                suspend, StringComparison.Ordinal);
            int readout = scanner.IndexOf(
                "await _renderer.FinishCurrentReadoutAsync()", quiesce,
                StringComparison.Ordinal);
            Assert.That(suspend, Is.GreaterThan(helper),
                "new readout jobs must stop before the wait");
            Assert.That(quiesce, Is.GreaterThan(suspend));
            Assert.That(readout, Is.GreaterThan(quiesce),
                "the in-flight readout job must be awaited, not preempted");
            Assert.That(scanner, Does.Not.Contain("FinishObservationDurableCutAsync"),
                "A frozen operation waits for the real scan and page fences, not a retired dual drain.");

            // Every action that writes, replaces, reads out or receives the
            // world takes it frozen. Missing one is how the readout kept
            // competing with the operation the user actually asked for.
            foreach (string entry in new[]
            {
                "public async Task<bool> SaveAsync()",
                "public async Task<bool> SaveAsAsync(string displayName)",
                "public async Task<bool> LoadAsync()",
                "public async Task<bool> OpenSessionAsync(Guid sessionId)",
                "public async Task<bool> DeleteSessionAsync(Guid sessionId)",
                "public async Task NewClearAsync(string displayName)",
                "private async Task<bool> RunExportAsync(",
                "private async Task<bool?> RunNativeImportAsync("
            })
            {
                int start = scanner.IndexOf(entry, StringComparison.Ordinal);
                Assert.That(start, Is.GreaterThanOrEqualTo(0), entry);
                int begins = scanner.IndexOf(
                    "await BeginFrozenWorldOperationAsync()", start,
                    StringComparison.Ordinal);
                int ends = scanner.IndexOf("EndFrozenWorldOperation()", begins,
                    StringComparison.Ordinal);
                Assert.That(begins, Is.GreaterThan(start),
                    entry + " must take the world frozen");
                Assert.That(ends, Is.GreaterThan(begins),
                    entry + " must release the readout again");
            }

            // ClearAllData is NEW, so it inherits the rule rather than
            // repeating it; asserting that keeps a second path from appearing.
            int clearAll = scanner.IndexOf(
                "public async void ClearAllDataAsync(", StringComparison.Ordinal);
            int delegated = scanner.IndexOf("await NewClearAsync()", clearAll,
                StringComparison.Ordinal);
            Assert.That(delegated, Is.GreaterThan(clearAll),
                "ClearAllData must delegate to NEW, not clear the world itself");

            // The operation needs the native queue, not the screen. Pausing via
            // SuspendGpuSubmission would clear _active, and TryGetActive gates
            // the flower draw on it, so a minutes-long export would blank the
            // scanned geometry. Closure 4.5: "Renderer nesmi vyhladovet".
            string renderer = Source("Runtime/Merkaba/MerkabaGridRenderer.cs");
            int active = renderer.IndexOf(
                "internal static bool TryGetActive(", StringComparison.Ordinal);
            int activeEnd = renderer.IndexOf("private bool Initialize()", active,
                StringComparison.Ordinal);
            Assert.That(active, Is.GreaterThanOrEqualTo(0));
            Assert.That(renderer.IndexOf("_pagePublicationPaused", active,
                    StringComparison.Ordinal), Is.Not.InRange(active, activeEnd),
                "the flower draw must survive a frozen-world operation");
            Assert.That(scanner.IndexOf("SuspendGpuSubmission", helper,
                    StringComparison.Ordinal),
                Is.Not.InRange(helper, scanner.IndexOf(
                    "private void EndFrozenWorldOperation", StringComparison.Ordinal)),
                "a frozen-world operation must not suspend the whole renderer");
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

            // Published pages have exactly one consumer, the flower vertex
            // draw. Opening the artifact viewer turns that draw off so an
            // exported model can be rotated and aligned against a clean room;
            // continuing to publish pages there is work nobody reads, and it
            // competes with paint and with an export started from the viewer.
            int drawGate = renderer.IndexOf("!readoutDrawEnabled", submit,
                StringComparison.Ordinal);
            Assert.That(drawGate, Is.GreaterThan(submit),
                "page publication must follow the readout draw");
            Assert.That(drawGate, Is.LessThan(quantum));
            string viewer = Source("Runtime/UI/MerkabaArtifactViewer.cs");
            Assert.That(viewer, Does.Contain("_scanner.ReadoutDrawEnabled = false;"),
                "opening the viewer must turn the scanner draw off");
            Assert.That(viewer, Does.Contain(
                    "_scanner.ReadoutDrawEnabled = _savedReadoutEnabled;"),
                "closing it must restore what the user had");
        }

        // A loaded 3D Tiles export is the one thing this viewer exists for, so
        // it must open, be viewed and be painted even with no anchor; only
        // ALIGN 1:1 is meaningless without one. Refusing the whole design
        // unless the package matched the ACTIVE session anchor left a foreign
        // export with nothing to paint into. BindAnnotations already had the
        // right rule for notes; the design now follows it instead of inventing
        // a second one. Paint is a MerkabaDesignDocument and never reaches M8,
        // so closure's derived-only rule holds in both cases.
        [Test]
        public void ForeignPackage_OpensAndPaintsWithoutAnAnchor()
        {
            string viewer = Source("Runtime/UI/MerkabaArtifactViewer.cs");
            int open = viewer.IndexOf("private void OpenSessionDesign()",
                StringComparison.Ordinal);
            int end = viewer.IndexOf("private void", open + 10,
                StringComparison.Ordinal);
            Assert.That(open, Is.GreaterThanOrEqualTo(0));
            string body = viewer.Substring(open, end - open);
            Assert.That(body, Does.Contain("anchoredToActiveSession"),
                "the design must branch on ownership, not refuse");
            Assert.That(body, Does.Contain("ArtifactDesignPath(_archivePath)"),
                "a foreign package must get its own design beside the archive");
            Assert.That(body, Does.Not.Contain(
                    "Session design requires the preview package's"),
                "the outright refusal must be gone");

            // The sidecar sits with the notes the same package already keeps.
            Assert.That(viewer, Does.Contain("\".design.json\""));
            Assert.That(viewer, Does.Contain("\".annotations.json\""));

            // Align is the only capability an anchorless package loses.
            Assert.That(viewer, Does.Contain("public bool CanAlignToRoom => IsOpen &&"));
            Assert.That(viewer, Does.Contain(
                    "\"ALIGN 1:1 unavailable: package has no spatial binding\""),
                "and the runtime must still refuse it, not just hide it");
            string menu = Source("Runtime/UI/DebugMenuController.cs");
            Assert.That(menu, Does.Contain("_artifactViewer?.CanAlignToRoom"),
                "the toggle must not promise an alignment the package cannot do");
            int paint = menu.IndexOf("_paintBrush?.SetEnabled", StringComparison.Ordinal);
            Assert.That(paint, Is.GreaterThanOrEqualTo(0));
            Assert.That(menu.Substring(paint, 80), Does.Not.Contain("CanAlignToRoom"),
                "painting must not depend on being alignable");
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
