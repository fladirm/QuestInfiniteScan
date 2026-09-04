using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>Greenfield V3 sparse-M8 persistence. No legacy reader exists.</summary>
    [DisallowMultipleComponent]
    public sealed class MerkabaPersistence : MonoBehaviour
    {
        private MerkabaGrid _grid;
        private MerkabaIntegrator _integrator;
        private RoomScanner _scanner;

        public bool IsBusy { get; private set; }
        public Guid ActiveAnchorUuid { get; private set; }
        public bool HasActiveSession => ActiveAnchorUuid != Guid.Empty;
        public bool SavedSessionExists => _grid != null
            ? File.Exists(_grid.CheckpointPath)
            : File.Exists(DefaultCheckpointPath);
        public string LastStatus { get; private set; } = "Not saved";
        public string SessionPath => _grid != null
            ? _grid.CheckpointPath : DefaultCheckpointPath;
        public event Action StatusChanged;

        private static string DefaultCheckpointPath => Path.Combine(
            Application.persistentDataPath, "MerkabaScan", "merkaba-grid.bin");

        private void Awake()
        {
            _grid = GetComponent<MerkabaGrid>();
            _integrator = GetComponent<MerkabaIntegrator>();
            _scanner = GetComponent<RoomScanner>();
        }

        public async Task<bool> SaveAsync()
        {
            if (IsBusy || _grid == null) return false;
            IsBusy = true;
            SetStatus("Saving…");
            try
            {
                if (_integrator != null && _integrator.HasPendingObservation)
                    throw new InvalidOperationException(
                        "Save requires RoomScanner quiesce before persistence.");
                IProgress<OperationWorkProgress> progress = ProgressFor(
                    ScanOperationKind.Save);
                await _grid.FlushAllDirtyTilesAsync(progress);

                RoomAnchorManager anchor = RoomAnchorManager.Instance;
                if (ActiveAnchorUuid == Guid.Empty)
                    throw new InvalidOperationException(
                        "Active session has no persisted room anchor.");
                if (anchor == null || !anchor.enabled ||
                    !await anchor.EnsureSessionAnchorAsync(ActiveAnchorUuid,
                        false) || anchor.SpatialAnchorUuid != ActiveAnchorUuid)
                    throw new InvalidOperationException(
                        "Active session room anchor could not be localized.");
                MerkabaSessionSnapshot snapshot = await _grid
                    .CaptureStoredSnapshotAsync(ActiveAnchorUuid,
                        anchor.SpatialAnchorMatrix,
                        _integrator != null ? _integrator.IntegrationCount : 0,
                        progress);
                await _grid.PublishCheckpointAsync(snapshot, progress);
                SetStatus($"Saved {snapshot.Tiles.Count} M8 tiles");
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("Merkaba V3 save failed: " + exception);
                SetStatus("Save failed: " + exception.Message);
                return false;
            }
            finally
            {
                IsBusy = false;
                StatusChanged?.Invoke();
            }
        }

        public async Task<bool> LoadAsync()
        {
            if (IsBusy || _grid == null || !SavedSessionExists) return false;
            IsBusy = true;
            SetStatus("Loading…");
            try
            {
                IProgress<OperationWorkProgress> progress = ProgressFor(
                    ScanOperationKind.Load);
                MerkabaSessionSnapshot snapshot = await _grid
                    .ReadCheckpointSnapshotAsync(progress);
                if (snapshot.AnchorUuid == Guid.Empty)
                    throw new InvalidDataException(
                        "Saved M8 session has no persisted room anchor UUID.");
                progress.Report(OperationWorkProgress.Indeterminate(
                    ScanOperationStage.LocalizingAnchor,
                    "Localizing saved anchor"));
                RoomAnchorManager anchorManager = RoomAnchorManager.Instance;
                if (anchorManager == null || !anchorManager.enabled)
                    throw new InvalidOperationException(
                        "Saved M8 world requires its spatial anchor, but " +
                        "RoomAnchorManager is unavailable.");
                if (!await anchorManager.EnsureSessionAnchorAsync(
                        snapshot.AnchorUuid, false) ||
                    anchorManager.SpatialAnchorUuid != snapshot.AnchorUuid)
                    throw new InvalidOperationException(
                        "Saved M8 spatial anchor could not be localized.");
                if (RoomSpaceRoot.Instance == null)
                    throw new InvalidOperationException(
                        "Saved M8 spatial anchor localized, but RoomSpaceRoot " +
                        "is unavailable.");
                if (!await RoomSpaceRoot.WaitForAnchorBindAsync(
                        anchorManager.SpatialAnchorTransform))
                    throw new InvalidOperationException(
                        "Saved M8 spatial anchor localized, but RoomSpaceRoot " +
                        "did not bind.");
                _grid.RelocateForLoadedAnchor(
                    anchorManager.SpatialAnchorMatrix, snapshot.AnchorAtSave);
                await _grid.LoadStoredSnapshotAsync(snapshot, progress);
                _integrator?.RestoreIntegrationCount(snapshot.IntegrationCount);
                ActiveAnchorUuid = snapshot.AnchorUuid;
                SetStatus($"Loaded {snapshot.Tiles.Count} M8 tiles");
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("Merkaba V3 load failed: " + exception);
                SetStatus("Load failed: " + exception.Message);
                return false;
            }
            finally
            {
                IsBusy = false;
                StatusChanged?.Invoke();
            }
        }

        public void ClearSavedSession()
        {
            if (IsBusy) return;
            try
            {
                _grid?.ClearStorage();
                ActiveAnchorUuid = Guid.Empty;
                SetStatus("No saved session");
            }
            catch (Exception exception)
            {
                Logger.Error("Could not clear M8 storage: " + exception.Message);
                SetStatus("Clear failed: " + exception.Message);
            }
        }

        internal void BeginNewSession(Guid anchorUuid)
        {
            if (anchorUuid == Guid.Empty)
                throw new ArgumentException(
                    "A new session requires its persisted room anchor UUID.",
                    nameof(anchorUuid));
            RoomAnchorManager anchor = RoomAnchorManager.Instance;
            if (anchor == null || anchor.SpatialAnchorUuid != anchorUuid ||
                !anchor.HasSpatialAnchor || RoomSpaceRoot.Instance == null ||
                RoomSpaceRoot.Instance.CurrentAnchor !=
                    anchor.SpatialAnchorTransform)
                throw new InvalidOperationException(
                    "A new session may begin only after its room anchor is " +
                    "localized and bound.");
            _grid.ClearStorage();
            ActiveAnchorUuid = anchorUuid;
            SetStatus("New session — not saved");
        }

        internal static void WriteSnapshot(Stream destination,
            MerkabaSessionSnapshot snapshot) =>
            MerkabaSsdStore.WriteCheckpoint(destination, snapshot);

        internal static MerkabaSessionSnapshot ReadSnapshot(Stream source) =>
            MerkabaSsdStore.ReadCheckpoint(source);

        private void SetStatus(string status)
        {
            LastStatus = status;
            StatusChanged?.Invoke();
        }

        private IProgress<OperationWorkProgress> ProgressFor(
            ScanOperationKind kind) => new Progress<OperationWorkProgress>(value =>
            _scanner?.ReportOperation(kind, value));
    }

    internal static class MerkabaFilePublishing
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        [DllImport("libc", EntryPoint = "rename", SetLastError = true)]
        private static extern int RenameAtomic(string source, string destination);
#endif

        internal static void Publish(string temporary, string destination)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (RenameAtomic(temporary, destination) != 0)
                throw new IOException("Atomic checkpoint rename failed with " +
                                      $"errno {Marshal.GetLastWin32Error()}.");
#else
            if (!File.Exists(destination))
            {
                File.Move(temporary, destination);
                return;
            }
            string backup = destination + ".bak";
            if (File.Exists(backup)) File.Delete(backup);
            File.Replace(temporary, destination, backup, true);
            if (File.Exists(backup)) File.Delete(backup);
#endif
        }
    }
}
