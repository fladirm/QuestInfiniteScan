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

                Guid anchorUuid = Guid.Empty;
                Matrix4x4 anchorAtSave = Matrix4x4.identity;
                RoomAnchorManager anchor = RoomAnchorManager.Instance;
                if (anchor != null && anchor.HasSpatialAnchor)
                {
                    anchorUuid = anchor.SpatialAnchorUuid;
                    anchorAtSave = anchor.SpatialAnchorMatrix;
                }
                MerkabaSessionSnapshot snapshot = await _grid
                    .CaptureStoredSnapshotAsync(anchorUuid, anchorAtSave,
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
                if (snapshot.AnchorUuid != Guid.Empty)
                {
                    progress.Report(OperationWorkProgress.Indeterminate(
                        ScanOperationStage.LocalizingAnchor,
                        "Localizing saved anchor"));
                    RoomAnchorManager anchorManager = RoomAnchorManager.Instance;
                    if (anchorManager == null)
                        throw new InvalidOperationException(
                            "Saved M8 world requires its spatial anchor, but " +
                            "RoomAnchorManager is unavailable.");
                    Matrix4x4? localized = await anchorManager
                        .LoadSpatialAnchorAsync(snapshot.AnchorUuid);
                    if (!localized.HasValue)
                        throw new InvalidOperationException(
                            "Saved M8 spatial anchor could not be localized.");
                    if (RoomSpaceRoot.Instance == null)
                        throw new InvalidOperationException(
                            "Saved M8 spatial anchor localized, but RoomSpaceRoot " +
                            "is unavailable.");
                    if (!await RoomSpaceRoot.WaitForBindAsync())
                        throw new InvalidOperationException(
                            "Saved M8 spatial anchor localized, but RoomSpaceRoot " +
                            "did not bind.");
                    _grid.RelocateForLoadedAnchor(localized.Value,
                        snapshot.AnchorAtSave);
                }
                await _grid.LoadStoredSnapshotAsync(snapshot, progress);
                _integrator?.RestoreIntegrationCount(snapshot.IntegrationCount);
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
                SetStatus("No saved session");
            }
            catch (Exception exception)
            {
                Logger.Error("Could not clear M8 storage: " + exception.Message);
                SetStatus("Clear failed: " + exception.Message);
            }
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
