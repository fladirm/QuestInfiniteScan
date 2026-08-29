using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>Greenfield V2 sparse-M8 persistence. No dense legacy format exists.</summary>
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
            if (_scanner != null && !_scanner.TryBeginOperation(
                    ScanOperationKind.Save, ScanOperationStage.SynchronizingScan,
                    "Synchronizing scan"))
                return false;
            IsBusy = true;
            bool succeeded = false;
            SetStatus("Saving…");
            try
            {
                Report(ScanOperationKind.Save,
                    ScanOperationStage.SynchronizingScan, -1f,
                    "Finishing current observation");
                if (_integrator != null)
                    await _integrator.FinishCurrentObservationAsync();
                Report(ScanOperationKind.Save,
                    ScanOperationStage.CapturingState, 0.2f,
                    "Flushing dirty M8 tiles");
                await _grid.FlushAllDirtyTilesAsync();

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
                        _integrator != null ? _integrator.IntegrationCount : 0);
                Report(ScanOperationKind.Save,
                    ScanOperationStage.WritingFile, 0.55f,
                    "Writing sparse M8 checkpoint");
                await _grid.PublishCheckpointAsync(snapshot);
                Report(ScanOperationKind.Save,
                    ScanOperationStage.PublishingFile, 0.95f,
                    "Publishing checkpoint");
                SetStatus($"Saved {snapshot.Tiles.Count} M8 tiles");
                succeeded = true;
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("Merkaba V2 save failed: " + exception);
                SetStatus("Save failed: " + exception.Message);
                return false;
            }
            finally
            {
                IsBusy = false;
                _scanner?.FinishOperation(ScanOperationKind.Save, succeeded,
                    LastStatus);
                StatusChanged?.Invoke();
            }
        }

        public async Task<bool> LoadAsync()
        {
            if (IsBusy || _grid == null || !SavedSessionExists) return false;
            if (_scanner != null && !_scanner.TryBeginOperation(
                    ScanOperationKind.Load, ScanOperationStage.ReadingFile,
                    "Reading sparse M8 checkpoint"))
                return false;
            IsBusy = true;
            bool succeeded = false;
            SetStatus("Loading…");
            try
            {
                MerkabaSessionSnapshot snapshot = await _grid
                    .ReadCheckpointSnapshotAsync();
                if (snapshot.AnchorUuid != Guid.Empty)
                {
                    Report(ScanOperationKind.Load,
                        ScanOperationStage.LocalizingAnchor, -1f,
                        "Localizing saved anchor");
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
                }
                Report(ScanOperationKind.Load,
                    ScanOperationStage.ApplyingState, 0.75f,
                    "Registering sparse M8 world");
                await _grid.LoadStoredSnapshotAsync(snapshot);
                _integrator?.RestoreIntegrationCount(snapshot.IntegrationCount);
                SetStatus($"Loaded {snapshot.Tiles.Count} M8 tiles");
                succeeded = true;
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("Merkaba V2 load failed: " + exception);
                SetStatus("Load failed: " + exception.Message);
                return false;
            }
            finally
            {
                IsBusy = false;
                _scanner?.FinishOperation(ScanOperationKind.Load, succeeded,
                    LastStatus);
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

        private void Report(ScanOperationKind kind, ScanOperationStage stage,
            float progress, string text) => _scanner?.ReportOperation(kind,
            stage, progress, text);
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
