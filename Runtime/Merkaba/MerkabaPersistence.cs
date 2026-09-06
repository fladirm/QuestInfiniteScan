using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>
    /// REV-B session transaction coordinator. M8 and every subordinate stream
    /// become durable only through one atomically published generation manifest.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MerkabaPersistence : MonoBehaviour
    {
        private MerkabaGrid _grid;
        private MerkabaIntegrator _integrator;
        private RoomScanner _scanner;
        private MerkabaSessionCatalog _catalog;
        private MerkabaSessionInfo _activeSession;

        public bool IsBusy { get; private set; }
        public Guid ActiveSessionId => _activeSession?.Id ?? Guid.Empty;
        public string ActiveSessionName => _activeSession?.displayName ??
            "No session";
        public Guid ActiveAnchorUuid => _activeSession?.AnchorId ?? Guid.Empty;
        public bool HasActiveSession => _activeSession != null;
        public bool IsDirty { get; private set; }
        public bool SavedSessionExists => _activeSession != null &&
            File.Exists(Path.Combine(ActiveSessionDirectory,
                MerkabaSsdStore.ManifestFileName));
        public bool AnySessionExists => Sessions.Count > 0;
        public IReadOnlyList<MerkabaSessionInfo> Sessions =>
            _catalog?.List() ?? Array.Empty<MerkabaSessionInfo>();
        public string LastStatus { get; private set; } = "Not saved";
        public string SessionPath => SavedSessionExists
            ? Path.Combine(ActiveSessionDirectory,
                MerkabaSsdStore.ManifestFileName)
            : string.Empty;
        internal string ActiveDesignPath => _activeSession != null
            ? Path.Combine(ActiveSessionDirectory,
                MerkabaSessionCatalog.DesignFileName)
            : string.Empty;
        internal string DesignLibraryPath => _catalog?.LibraryRoot ??
            string.Empty;
        public event Action StatusChanged;

        private string ActiveSessionDirectory => _activeSession != null
            ? _catalog.SessionDirectory(_activeSession.Id) : string.Empty;

        private void Awake()
        {
            _grid = GetComponent<MerkabaGrid>();
            _integrator = GetComponent<MerkabaIntegrator>();
            _scanner = GetComponent<RoomScanner>();
            _catalog = new MerkabaSessionCatalog(Path.Combine(
                Application.persistentDataPath, "MerkabaScan"));
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
                MerkabaStorageCommitResult result = await SaveActiveCoreAsync(
                    progress);
                _catalog.MarkSaved(_activeSession);
                IsDirty = false;
                SetStatus($"Saved {result.CanonicalTileCount} M8 tiles · " +
                    $"{result.DirtyBytes} dirty bytes");
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("Merkaba REV-B save failed: " + exception);
                SetStatus("Save failed: " + exception.Message);
                return false;
            }
            finally
            {
                IsBusy = false;
                StatusChanged?.Invoke();
            }
        }

        public Task<bool> LoadAsync()
        {
            if (IsBusy || _grid == null) return Task.FromResult(false);
            MerkabaSessionInfo selected = null;
            if (_activeSession != null && SavedSessionExists)
                selected = _activeSession;
            else
                foreach (MerkabaSessionInfo session in Sessions)
                    if (File.Exists(Path.Combine(
                            _catalog.SessionDirectory(session.Id),
                            MerkabaSsdStore.ManifestFileName)))
                    {
                        selected = session;
                        break;
                    }
            if (selected != null) return OpenSessionAsync(selected.Id);
            SetStatus("No saved session");
            return Task.FromResult(false);
        }

        public async Task<bool> OpenSessionAsync(Guid sessionId)
        {
            if (IsBusy || _grid == null || sessionId == Guid.Empty)
                return false;
            MerkabaSessionInfo previousSession = _activeSession;
            bool previousDirty = IsDirty;
            bool worldCleared = false;
            RoomAnchorManager anchorManager = null;
            IsBusy = true;
            SetStatus("Loading…");
            try
            {
                IProgress<OperationWorkProgress> progress = ProgressFor(
                    ScanOperationKind.Load);
                MerkabaSessionInfo session = _catalog.Read(sessionId);
                string directory = _catalog.SessionDirectory(session.Id);
                string manifestPath = Path.Combine(directory,
                    MerkabaSsdStore.ManifestFileName);
                if (!File.Exists(manifestPath))
                    throw new FileNotFoundException(
                        "Scan session has not been saved yet.");
                // Manifest identity is validated before localizing or clearing
                // any current world. Its committed byte ranges are the only
                // crash-safe session authority.
                MerkabaSessionManifest manifest =
                    MerkabaSsdStore.ReadManifestFromDirectory(directory);
                if (manifest.SessionUuid != session.Id ||
                    manifest.AnchorUuid != session.AnchorId)
                    throw new InvalidDataException(
                        "Session metadata and manifest identity differ.");
                anchorManager = RoomAnchorManager.Instance;
                if (anchorManager == null || !anchorManager.enabled)
                    throw new InvalidOperationException(
                        "Saved M8 world requires its spatial anchor, but " +
                        "RoomAnchorManager is unavailable.");
                MerkabaSessionOpenState opened = await _grid
                    .SwitchStorageRootAsync(directory, false, true, progress,
                        async candidate =>
                        {
                            if (candidate == null ||
                                candidate.Manifest.CommitGeneration !=
                                manifest.CommitGeneration ||
                                candidate.Manifest.SessionUuid != session.Id ||
                                candidate.Manifest.AnchorUuid != session.AnchorId)
                                throw new InvalidDataException(
                                    "Session manifest changed during OPEN replay.");
                            progress.Report(
                                OperationWorkProgress.Indeterminate(
                                    ScanOperationStage.LocalizingAnchor,
                                    "Localizing saved anchor"));
                            if (!await anchorManager.EnsureSessionAnchorAsync(
                                    session.AnchorId, false) ||
                                anchorManager.SpatialAnchorUuid !=
                                session.AnchorId)
                                throw new InvalidOperationException(
                                    "Saved M8 spatial anchor could not be " +
                                    "localized.");
                            if (RoomSpaceRoot.Instance == null)
                                throw new InvalidOperationException(
                                    "Saved M8 spatial anchor localized, but " +
                                    "RoomSpaceRoot is unavailable.");
                            if (!await RoomSpaceRoot.WaitForAnchorBindAsync(
                                    anchorManager.SpatialAnchorTransform))
                                throw new InvalidOperationException(
                                    "Saved M8 spatial anchor localized, but " +
                                    "RoomSpaceRoot did not bind.");
                        });
                worldCleared = true;
                _integrator?.Clear();
                _grid.RelocateForLoadedAnchor(
                    anchorManager.SpatialAnchorMatrix,
                    opened.Manifest.AnchorAtSave);
                await _grid.LoadCommittedStorageAsync(opened, progress);
                _integrator?.RestoreIntegrationCount(
                    opened.Manifest.IntegrationCount);
                _activeSession = session;
                IsDirty = false;
                SetStatus($"Loaded {opened.IndexedTileCount} M8 tiles");
                return true;
            }
            catch (Exception exception)
            {
                Guid previousAnchor = previousSession?.AnchorId ?? Guid.Empty;
                bool anchorChanged = anchorManager != null &&
                    anchorManager.SpatialAnchorUuid != previousAnchor;
                bool failClosed = worldCleared || anchorChanged;
                if (failClosed) ClearCanonicalWorldFailClosed();
                _activeSession = failClosed ? null : previousSession;
                IsDirty = failClosed ? false : previousDirty;
                Logger.Error("Merkaba REV-B load failed: " + exception);
                SetStatus("Load failed: " + exception.Message);
                return false;
            }
            finally
            {
                IsBusy = false;
                StatusChanged?.Invoke();
            }
        }

        internal async Task BeginNewSessionAsync(Guid anchorUuid,
            string displayName = null)
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
            MerkabaSessionInfo session = _catalog.Create(anchorUuid,
                displayName);
            try
            {
                await _grid.SwitchStorageRootAsync(
                    _catalog.SessionDirectory(session.Id), true, true);
            }
            catch
            {
                _catalog.Delete(session.Id);
                // NEW has already selected a new spatial coordinate authority.
                // If its empty store cannot become authoritative, retaining the
                // prior session label would pair it with the wrong anchor.
                _activeSession = null;
                IsDirty = false;
                ClearCanonicalWorldFailClosed();
                throw;
            }
            _activeSession = session;
            IsDirty = true;
            SetStatus("New session — not saved");
            StatusChanged?.Invoke();
        }

        public async Task<bool> SaveAsAsync(string displayName)
        {
            if (IsBusy || _grid == null || _activeSession == null)
                return false;
            IsBusy = true;
            SetStatus("Saving as…");
            MerkabaSessionInfo created = null;
            bool switched = false;
            try
            {
                IProgress<OperationWorkProgress> progress = ProgressFor(
                    ScanOperationKind.Save);
                MerkabaStorageCommitResult result = await SaveActiveCoreAsync(
                    progress);
                _catalog.MarkSaved(_activeSession);
                created = _catalog.Create(ActiveAnchorUuid, displayName);
                string destinationDirectory = _catalog.SessionDirectory(
                    created.Id);
                await _grid.CloneCommittedStorageAsync(destinationDirectory,
                    created.Id);
                string sourceDesign = Path.Combine(ActiveSessionDirectory,
                    MerkabaSessionCatalog.DesignFileName);
                if (File.Exists(sourceDesign))
                    await Task.Run(() => CopyFileDurable(sourceDesign,
                        Path.Combine(destinationDirectory,
                            MerkabaSessionCatalog.DesignFileName)));
                await _grid.SwitchStorageRootAsync(destinationDirectory,
                    false, false);
                _activeSession = created;
                switched = true;
                _catalog.MarkSaved(created);
                IsDirty = false;
                SetStatus($"Saved as {created.displayName} · " +
                    $"{result.CanonicalTileCount} M8 tiles");
                return true;
            }
            catch (Exception exception)
            {
                if (created != null && !switched)
                    _catalog.Delete(created.Id);
                Logger.Error("Merkaba Save As failed: " + exception);
                SetStatus("Save As failed: " + exception.Message);
                return false;
            }
            finally
            {
                IsBusy = false;
                StatusChanged?.Invoke();
            }
        }

        public bool RenameActiveSession(string displayName)
        {
            if (IsBusy || _activeSession == null) return false;
            try
            {
                _catalog.Rename(_activeSession, displayName);
                SetStatus("Renamed to " + _activeSession.displayName);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("Session rename failed: " + exception);
                SetStatus("Rename failed: " + exception.Message);
                return false;
            }
        }

        public async Task<bool> DeleteSessionAsync(Guid sessionId)
        {
            if (IsBusy || sessionId == Guid.Empty) return false;
            bool deletingActive = sessionId == ActiveSessionId;
            IsBusy = true;
            try
            {
                if (deletingActive)
                {
                    string inactive = Path.Combine(
                        Application.temporaryCachePath,
                        "MerkabaScanInactive");
                    // Fail closed from this point: the active session is being
                    // destroyed and must never remain paired with an empty or
                    // replacement storage authority.
                    _activeSession = null;
                    IsDirty = false;
                    await _grid.SwitchStorageRootAsync(inactive, true, true);
                    _integrator?.Clear();
                }
                _catalog.Delete(sessionId);
                SetStatus("Session deleted");
                return true;
            }
            catch (Exception exception)
            {
                if (deletingActive) ClearCanonicalWorldFailClosed();
                Logger.Error("Session delete failed: " + exception);
                SetStatus("Delete failed: " + exception.Message);
                return false;
            }
            finally
            {
                IsBusy = false;
                StatusChanged?.Invoke();
            }
        }

        internal void MarkDirty()
        {
            if (_activeSession == null || IsDirty) return;
            IsDirty = true;
            SetStatus("Unsaved changes");
        }

        private void ClearCanonicalWorldFailClosed()
        {
            try
            {
                _integrator?.Clear();
            }
            catch (Exception exception)
            {
                Logger.Error("Could not clear M8 after session authority " +
                    "failure: " + exception.Message);
            }
        }

        private async Task<MerkabaStorageCommitResult> SaveActiveCoreAsync(
            IProgress<OperationWorkProgress> progress)
        {
            if (_integrator != null &&
                (_integrator.HasPendingObservation ||
                 _integrator.HasAttemptInFlight ||
                 _integrator.HasPendingFineErase ||
                 _integrator.HasFineEraseAttemptInFlight))
                throw new InvalidOperationException(
                    "Save requires RoomScanner quiesce before persistence.");
            if (_activeSession == null || ActiveAnchorUuid == Guid.Empty)
                throw new InvalidOperationException(
                    "Active session has no persisted room anchor.");
            await _grid.FlushAllDirtyTilesAsync(progress);
            RoomAnchorManager anchor = RoomAnchorManager.Instance;
            if (anchor == null || !anchor.enabled ||
                !await anchor.EnsureSessionAnchorAsync(ActiveAnchorUuid,
                    false) || anchor.SpatialAnchorUuid != ActiveAnchorUuid)
                throw new InvalidOperationException(
                    "Active session room anchor could not be localized.");
            int occupied = Math.Max(0, _grid.M8OccupiedKernelCount);
            return await _grid.CommitStorageAsync(ActiveSessionId,
                ActiveAnchorUuid, anchor.SpatialAnchorMatrix,
                _integrator != null ? _integrator.IntegrationCount : 0,
                checked((uint)occupied), progress);
        }

        internal static void CopyFileDurable(string source, string destination)
        {
            string temporary = destination + ".tmp";
            using (var input = new FileStream(source, FileMode.Open,
                       FileAccess.Read, FileShare.Read, 1024 * 1024,
                       FileOptions.SequentialScan))
            using (var output = new FileStream(temporary, FileMode.Create,
                       FileAccess.Write, FileShare.None, 1024 * 1024,
                       FileOptions.SequentialScan))
            {
                input.CopyTo(output, 1024 * 1024);
                output.Flush(true);
            }
            MerkabaFilePublishing.Publish(temporary, destination);
        }

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
#if (UNITY_ANDROID && !UNITY_EDITOR) || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern int OpenDirectory(string path, int flags);

        [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
        private static extern int SyncFileDescriptor(int descriptor);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        private static extern int CloseFileDescriptor(int descriptor);
#endif

        internal static void Publish(string temporary, string destination)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (RenameAtomic(temporary, destination) != 0)
                throw new IOException("Atomic file rename failed with " +
                                      $"errno {Marshal.GetLastWin32Error()}.");
#else
            if (!File.Exists(destination))
            {
                File.Move(temporary, destination);
            }
            else
            {
                string backup = destination + ".bak";
                if (File.Exists(backup)) File.Delete(backup);
                File.Replace(temporary, destination, backup, true);
                if (File.Exists(backup)) File.Delete(backup);
            }
#endif
            FlushParentDirectory(destination);
        }

        /// <summary>
        /// Flushes the directory entry that makes a durable temporary file
        /// authoritative. Quest/Android and the Linux proof host use the same
        /// POSIX ordering: file fsync, atomic rename, parent-directory fsync.
        /// </summary>
        private static void FlushParentDirectory(string destination)
        {
#if (UNITY_ANDROID && !UNITY_EDITOR) || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            string directory = Path.GetDirectoryName(Path.GetFullPath(
                destination));
            int descriptor = OpenDirectory(directory, 0);
            if (descriptor < 0)
                throw new IOException("Could not open publish directory with " +
                    $"errno {Marshal.GetLastWin32Error()}.");
            int syncError = 0;
            try
            {
                if (SyncFileDescriptor(descriptor) != 0)
                    syncError = Marshal.GetLastWin32Error();
            }
            finally
            {
                if (CloseFileDescriptor(descriptor) != 0 && syncError == 0)
                    syncError = Marshal.GetLastWin32Error();
            }
            if (syncError != 0)
                throw new IOException("Could not fsync publish directory with " +
                    $"errno {syncError}.");
#endif
        }
    }
}
