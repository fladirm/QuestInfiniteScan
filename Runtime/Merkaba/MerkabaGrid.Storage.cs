using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    public sealed partial class MerkabaGrid
    {
        [StructLayout(LayoutKind.Sequential)]
        private readonly struct Raw16
        {
            internal readonly uint X;
            internal readonly uint Y;
            internal readonly uint Z;
            internal readonly uint W;
        }

        private MerkabaSsdStore _ssdStore;
        private string _storageDirectory;
        private bool _streamCounterPending;
        private bool _attemptCompletionReadbackPending;
        private uint _attemptCompletionExpectedToken;
        private bool _evictionSelectionPendingSample;
        private float _nextStreamPoll;
        private uint _loadRequestCursor;
        private uint _observedLoadRequestCount;
        private bool _loadAddressReadbackPending;
        private MerkabaTileAddress[] _loadAddresses;
        private Task<MerkabaTileSnapshot[]> _loadStorageTask;
        private MerkabaTileSnapshot[] _loadStorageResult;
        private Exception _loadStorageFailure;
        private bool _loadInstallStatusPending;
        private bool _loadAcknowledgePending;
        private bool _writebackReadbackPending;
        private Task _writebackStorageTask;
        private int _writebackBatchCount;
        private bool _writebackCompletionPending;
        private Exception _writebackStorageFailure;
        private int _completedWritebackBatchCount;
        private int _deferredWritebackFailureCount;
        private bool _flushAllDirty;
        private TaskCompletionSource<bool> _flushCompletion;
        private IProgress<OperationWorkProgress> _flushProgress;
        private int _flushCompletedTiles;
        private int _flushTotalTiles = -1;
        private uint _issuedObservationToken;
        private uint _completedObservationToken;
        private uint _completedObservationFailure;
        private bool _completedObservationChangedReadout;
        private uint _completedAttemptToken;
        private uint _residencyEpoch;
        private readonly uint[] _streamControlWord = new uint[1];
        private readonly double[] _loadLatencies = new double[64];
        private readonly double[] _writeLatencies = new double[64];
        private int _loadLatencyCount;
        private int _loadLatencyCursor;
        private int _writeLatencyCount;
        private int _writeLatencyCursor;
        private double _loadIoStartedAt;
        private double _writeIoStartedAt;
        private ulong _loadBytesTotal;
        private ulong _writeBytesTotal;
        private ulong _loadBytesAtRateSample;
        private ulong _writeBytesAtRateSample;
        private double _storageRateSampleAt;
        private float _loadBytesPerSecond;
        private float _writeBytesPerSecond;
        private bool _storageReplacementPending;
        private bool _baseCompactionRequested;
        private Task<bool> _baseCompactionTask;
        private CancellationTokenSource _baseCompactionCancellation;

        internal bool HasUnresolvedStorageRequests =>
            _loadRequestCursor != _observedLoadRequestCount ||
            _loadAddressReadbackPending || _loadStorageTask != null ||
            _loadStorageResult != null || _loadStorageFailure != null ||
            _loadInstallStatusPending;

        internal uint CompletedObservationToken => _completedObservationToken;
        internal uint CompletedObservationFailure =>
            _completedObservationFailure;
        internal bool CompletedObservationChangedReadout =>
            _completedObservationChangedReadout;
        internal uint CompletedAttemptToken => _completedAttemptToken;
        internal uint ResidencyEpoch => _residencyEpoch;

        private void EnsureStorage()
        {
            _storageDirectory ??= Path.Combine(
                Application.persistentDataPath, "MerkabaScan");
            _ssdStore ??= new MerkabaSsdStore(_storageDirectory);
        }

        internal async Task<MerkabaSessionOpenState> SwitchStorageRootAsync(
            string directory,
            bool clearCanonicalFiles, bool clearGpuWorld,
            IProgress<OperationWorkProgress> openProgress = null,
            Func<MerkabaSessionOpenState, Task> beforeAdoption = null)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Session storage root is required.",
                    nameof(directory));
            string fullDirectory = Path.GetFullPath(directory);
            if (_storageReplacementPending)
                throw new InvalidOperationException(
                    "M8 storage authority is already being replaced.");

            _storageReplacementPending = true;
            try
            {
                await CancelAndRetireBaseCompactionAsync(false);
                while (_streamCounterPending ||
                       _attemptCompletionReadbackPending ||
                       _loadAddressReadbackPending ||
                       _loadInstallStatusPending || _writebackReadbackPending)
                    await Task.Yield();

                Task loadTask = _loadStorageTask;
                Task writeTask = _writebackStorageTask;
                if (loadTask != null)
                {
                    try { await loadTask; }
                    catch (Exception exception)
                    {
                        Logger.Warning("Discarding retired session read: " +
                            exception.GetBaseException().Message);
                    }
                }
                if (writeTask != null)
                {
                    try { await writeTask; }
                    catch (Exception exception)
                    {
                        Logger.Warning("Retired session write failed before " +
                            "storage switch: " +
                            exception.GetBaseException().Message);
                    }
                }

                if (_flushCompletion != null)
                {
                    _flushCompletion.TrySetException(
                        new InvalidOperationException(
                            "M8 storage root changed during a flush."));
                    _flushCompletion = null;
                    _flushAllDirty = false;
                    _flushProgress = null;
                }

                var replacement = new MerkabaSsdStore(fullDirectory);
                MerkabaSessionOpenState opened = null;
                if (clearCanonicalFiles)
                    replacement.Clear();
                else
                    opened = await replacement.OpenCommittedAsync(openProgress);

                // OPEN's candidate authority is completely replayed and
                // validated before anchor localization. The callback is a
                // pre-adoption barrier: failure leaves both the current store
                // and current GPU world untouched.
                if (beforeAdoption != null)
                {
                    if (opened == null)
                        throw new InvalidOperationException(
                            "A new empty root has no committed state to adopt.");
                    await beforeAdoption(opened);
                }

                if (clearGpuWorld)
                {
                    EnsureGpuResources();
                    if (!GpuSubmissionAllowed)
                        throw new InvalidOperationException(
                            "Cannot replace the M8 world while GPU submission " +
                            "is suspended.");
                    // The replacement has already passed manifest/range replay.
                    // Keep the gate closed while the old GPU coordinate authority
                    // is retired and the new store becomes authoritative.
                    ClearGpuWorldForNewScan();
                }
                _ssdStore = replacement;
                _storageDirectory = fullDirectory;
                // When the GPU world survives (SAVE AS), its ring cursors and
                // any completed load task survive too. Resetting them would
                // replay historical load requests into the copied session.
                return opened;
            }
            finally
            {
                _storageReplacementPending = false;
                _nextStreamPoll = 0f;
            }
        }

        private void ResetStorageRuntimeState()
        {
            _loadRequestCursor = 0u;
            _observedLoadRequestCount = 0u;
            _loadAddresses = null;
            _loadStorageTask = null;
            _loadStorageResult = null;
            _loadStorageFailure = null;
            _loadAddressReadbackPending = false;
            _evictionSelectionPendingSample = false;
            _loadInstallStatusPending = false;
            _loadAcknowledgePending = false;
            _writebackReadbackPending = false;
            _writebackStorageTask = null;
            _writebackBatchCount = 0;
            _writebackCompletionPending = false;
            _writebackStorageFailure = null;
            _completedWritebackBatchCount = 0;
            _deferredWritebackFailureCount = 0;
            _flushAllDirty = false;
            _flushCompletion = null;
            _flushProgress = null;
            _flushCompletedTiles = 0;
            _flushTotalTiles = -1;
            _issuedObservationToken = 0u;
            _completedObservationToken = 0u;
            _completedObservationFailure = 0u;
            _completedObservationChangedReadout = false;
            _completedAttemptToken = 0u;
            _residencyEpoch = 0u;
            _attemptCompletionReadbackPending = false;
            _attemptCompletionExpectedToken = 0u;
            _loadLatencyCount = _loadLatencyCursor = 0;
            _writeLatencyCount = _writeLatencyCursor = 0;
            _loadIoStartedAt = _writeIoStartedAt = 0.0;
            _loadBytesTotal = _writeBytesTotal = 0uL;
            _loadBytesAtRateSample = _writeBytesAtRateSample = 0uL;
            _storageRateSampleAt = Time.realtimeSinceStartupAsDouble;
            _loadBytesPerSecond = _writeBytesPerSecond = 0f;
        }

        private void PumpStorage()
        {
            if (_storageReplacementPending) return;
            CompleteStorageCpuTasks();
            UpdateStorageRates();
            PumpIdleBaseCompaction();
            if (!GpuSubmissionAllowed ||
                MerkabaNativeVulkanExecutor.HasJobInFlight) return;
            SubmitDeferredStorageControl();
            CompleteStorageGpuWork();
            if (_streamCounterPending || Time.unscaledTime < _nextStreamPoll)
                return;
            _nextStreamPoll = Time.unscaledTime + 0.05f;
            _streamCounterPending = true;
            int generation = _gpuGeneration;
            AsyncGPUReadback.Request(_m8Counters, request =>
            {
                _streamCounterPending = false;
                if (generation != _gpuGeneration || request.hasError) return;
                var values = request.GetData<uint>();
                ApplySampledCounters(values);
                _observedLoadRequestCount = values[19];
                // Accounting above is CPU-only. A callback issued before
                // quiesce must never enqueue readback/compute/upload work after
                // the retirement marker.
                if (_storageReplacementPending || !GpuSubmissionAllowed ||
                    MerkabaNativeVulkanExecutor.HasJobInFlight) return;
                if (!_loadAddressReadbackPending && _loadStorageTask == null &&
                    _loadStorageResult == null && _loadStorageFailure == null &&
                    !_loadInstallStatusPending &&
                    _loadRequestCursor != _observedLoadRequestCount)
                    BeginLoadAddressReadback();
                uint rawWritebackCount = values[20];
                uint writebackCount = Math.Min(rawWritebackCount,
                    (uint)StreamBatchCapacity);
                if (writebackCount > 0u && !_writebackReadbackPending &&
                    _writebackStorageTask == null &&
                    !_writebackCompletionPending)
                {
                    if (_flushAllDirty && _flushTotalTiles < 0)
                    {
                        _flushTotalTiles = checked(_flushCompletedTiles +
                            (int)rawWritebackCount);
                        ReportFlushProgress();
                    }
                    _evictionSelectionPendingSample = false;
                    BeginWritebackReadback((int)writebackCount);
                }
                else if (_evictionSelectionPendingSample &&
                         writebackCount == 0u)
                {
                    _evictionSelectionPendingSample = false;
                    if (_flushAllDirty && !_writebackReadbackPending &&
                        _writebackStorageTask == null &&
                        !_writebackCompletionPending)
                    {
                        if (_flushTotalTiles < 0)
                            _flushTotalTiles = _flushCompletedTiles;
                        ReportFlushProgress(true);
                        _flushAllDirty = false;
                        _flushCompletion?.TrySetResult(true);
                        _flushCompletion = null;
                        _flushProgress = null;
                    }
                }
                else if (!_writebackReadbackPending &&
                         _writebackStorageTask == null &&
                         !_writebackCompletionPending &&
                         (_flushAllDirty ||
                          values[CounterEvictionNeeded] != 0u))
                {
                    SelectEvictionVictims(_flushAllDirty);
                    _evictionSelectionPendingSample = true;
                    _nextStreamPoll = 0f;
                }
            });
        }

        internal void PumpStorageForLifecycleRetirement()
        {
            PumpStorage();
        }

        private void ApplySampledCounters(Unity.Collections.NativeArray<uint> values)
        {
            uint hotTiles = values[CounterHotTileCount];
            uint coldTiles = values[CounterColdTileCount];
            PublishResidencyEpoch(values[CounterResidencyEpoch]);
            M8BlockCount = ToInt(values[CounterBlockCount]);
            M8ChunkCount = ToInt(values[CounterChunkCount]);
            M8HotTileCount = ToInt(hotTiles);
            M8ColdTileCount = ToInt(coldTiles);
            M8OccupiedKernelCount = ToInt(values[CounterOccupiedKernelCount]);
        }

        internal void RequestAttemptCompletion(uint expectedAttemptToken)
        {
            if (!GpuSubmissionAllowed || _m8AttemptCompletion == null)
                throw new InvalidOperationException(
                    "Attempt completion cannot be requested while GPU submission is suspended.");
            if (expectedAttemptToken == 0u)
                throw new ArgumentOutOfRangeException(
                    nameof(expectedAttemptToken));
            if (_attemptCompletionReadbackPending)
                throw new InvalidOperationException(
                    "Only one observation attempt may await completion.");

            _attemptCompletionReadbackPending = true;
            _attemptCompletionExpectedToken = expectedAttemptToken;
            int generation = _gpuGeneration;
            AsyncGPUReadback.Request(_m8AttemptCompletion, request =>
            {
                // A stale callback must not clear or publish a newer request.
                if (generation != _gpuGeneration ||
                    expectedAttemptToken != _attemptCompletionExpectedToken)
                    return;

                _attemptCompletionReadbackPending = false;
                _attemptCompletionExpectedToken = 0u;
                if (request.hasError)
                {
                    Logger.Error("M8 exact attempt-completion readback failed; " +
                                 $"attempt={expectedAttemptToken}.");
                    return;
                }

                Unity.Collections.NativeArray<Raw16> values =
                    request.GetData<Raw16>();
                if (values.Length != 1 || values[0].X != expectedAttemptToken)
                {
                    uint actual = values.Length == 1 ? values[0].X : 0u;
                    Logger.Error("Ignored stale M8 attempt-completion record; " +
                                 $"expected={expectedAttemptToken} actual={actual}.");
                    return;
                }

                Raw16 completion = values[0];
                _completedAttemptToken = completion.X;
                _completedObservationToken = completion.Y;
                _completedObservationChangedReadout =
                    (completion.Z & 0x80000000u) != 0u;
                _completedObservationFailure = completion.Z & 0x7fffffffu;
                PublishResidencyEpoch(completion.W);
                // CPU accounting only. This callback must never enqueue GPU
                // work after a quiesce retirement marker.
            });
        }

        private void PublishResidencyEpoch(uint candidate)
        {
            if (unchecked((int)(candidate - _residencyEpoch)) >= 0)
                _residencyEpoch = candidate;
        }

        private void BeginLoadAddressReadback()
        {
            if (_storageReplacementPending || !GpuSubmissionAllowed) return;
            uint available = _observedLoadRequestCount - _loadRequestCursor;
            uint queueIndex = _loadRequestCursor & LoadRequestMask;
            uint contiguous = (uint)LoadRequestCapacity - queueIndex;
            int count = (int)Math.Min(Math.Min(available,
                (uint)StreamBatchCapacity), contiguous);
            if (count <= 0) return;
            _loadAddressReadbackPending = true;
            int generation = _gpuGeneration;
            AsyncGPUReadback.Request(_m8LoadRequests, count * 16,
                checked((int)queueIndex * 16), request =>
                {
                    _loadAddressReadbackPending = false;
                    if (generation != _gpuGeneration) return;
                    if (request.hasError)
                    {
                        Logger.Error("M8 SSD load-request readback failed.");
                        return;
                    }
                    if (_storageReplacementPending) return;
                    var raw = request.GetData<Raw16>();
                    var addresses = new MerkabaTileAddress[raw.Length];
                    for (int index = 0; index < raw.Length; index++)
                        addresses[index] = new MerkabaTileAddress(new int3(
                            unchecked((int)raw[index].X),
                            unchecked((int)raw[index].Y),
                            unchecked((int)raw[index].Z)), raw[index].W);
                    _loadAddresses = addresses;
                    EnsureStorage();
                    _loadIoStartedAt = Time.realtimeSinceStartupAsDouble;
                    _loadStorageTask = _ssdStore.ReadAsync(addresses);
                });
        }

        private void CompleteStorageCpuTasks()
        {
            if (_loadStorageTask != null && _loadStorageTask.IsCompleted)
            {
                Task<MerkabaTileSnapshot[]> task = _loadStorageTask;
                _loadStorageTask = null;
                bool completedStorageRead = _loadIoStartedAt > 0.0;
                RecordStorageLatency(_loadLatencies, ref _loadLatencyCount,
                    ref _loadLatencyCursor, ref _loadIoStartedAt);
                if (task.IsCanceled)
                    _loadStorageFailure = new TaskCanceledException(
                        "M8 SSD tile load was canceled.");
                else if (task.IsFaulted)
                    _loadStorageFailure = task.Exception?.GetBaseException() ??
                        new IOException("M8 SSD tile load failed.");
                else
                {
                    if (completedStorageRead)
                        _loadBytesTotal += (ulong)task.Result.Length *
                            MerkabaSsdStore.TilePayloadBytes;
                    _loadStorageResult = task.Result;
                }
            }
            if (_writebackStorageTask != null &&
                _writebackStorageTask.IsCompleted)
            {
                Task task = _writebackStorageTask;
                _writebackStorageTask = null;
                RecordStorageLatency(_writeLatencies, ref _writeLatencyCount,
                    ref _writeLatencyCursor, ref _writeIoStartedAt);
                if (task.IsCanceled)
                    _writebackStorageFailure = new TaskCanceledException(
                        "M8 SSD writeback was canceled.");
                else if (task.IsFaulted)
                    _writebackStorageFailure =
                        task.Exception?.GetBaseException() ??
                        new IOException("M8 SSD writeback failed.");
                else
                {
                    _writeBytesTotal += (ulong)_writebackBatchCount *
                        MerkabaSsdStore.TilePayloadBytes;
                    if (_flushAllDirty)
                    {
                        _flushCompletedTiles = checked(_flushCompletedTiles +
                            _writebackBatchCount);
                        ReportFlushProgress();
                    }
                }
                _completedWritebackBatchCount = _writebackBatchCount;
                _writebackCompletionPending = true;
                _writebackBatchCount = 0;
            }
            if (_baseCompactionTask != null &&
                _baseCompactionTask.IsCompleted)
            {
                Task<bool> task = _baseCompactionTask;
                CancellationTokenSource cancellation =
                    _baseCompactionCancellation;
                _baseCompactionTask = null;
                _baseCompactionCancellation = null;
                if (task.IsCanceled)
                    _baseCompactionRequested = true;
                else if (task.IsFaulted)
                    Logger.Error("Idle M8 base compaction failed without " +
                        "changing canonical truth: " +
                        (task.Exception?.GetBaseException().Message ??
                         "unknown storage failure"));
                cancellation?.Dispose();
            }
        }

        private void PumpIdleBaseCompaction()
        {
            bool idle = IsIdleForBaseCompaction();
            if (_baseCompactionTask != null)
            {
                if (!idle) _baseCompactionCancellation?.Cancel();
                return;
            }
            if (!_baseCompactionRequested || !idle ||
                MerkabaNativeVulkanExecutor.HasJobInFlight)
                return;
            EnsureStorage();
            _baseCompactionRequested = false;
            _baseCompactionCancellation = new CancellationTokenSource();
            _baseCompactionTask = _ssdStore.CompactCommittedBasesAsync(
                _baseCompactionCancellation.Token);
        }

        private bool IsIdleForBaseCompaction()
        {
            RoomScanner scanner = RoomScanner.Instance;
            return !_storageReplacementPending && scanner != null &&
                ReferenceEquals(scanner.Grid, this) && !scanner.IsScanning &&
                scanner.ScanLifecycle == ScanLifecycleState.Stopped &&
                !scanner.IsBusy && !_streamCounterPending &&
                !_loadAddressReadbackPending && _loadStorageTask == null &&
                _loadStorageResult == null && _loadStorageFailure == null &&
                !_loadInstallStatusPending && !_writebackReadbackPending &&
                _writebackStorageTask == null && !_writebackCompletionPending &&
                !_flushAllDirty && _flushCompletion == null;
        }

        private async Task CancelAndRetireBaseCompactionAsync(bool retryWhenIdle)
        {
            Task<bool> task = _baseCompactionTask;
            if (task == null)
            {
                if (!retryWhenIdle) _baseCompactionRequested = false;
                return;
            }
            _baseCompactionCancellation?.Cancel();
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // Cancellation is allowed only before the recovery manifest.
            }
            catch (Exception exception)
            {
                Logger.Error("Retiring idle M8 base compaction failed: " +
                    exception.GetBaseException().Message);
            }
            if (ReferenceEquals(_baseCompactionTask, task))
            {
                _baseCompactionTask = null;
                _baseCompactionCancellation?.Dispose();
                _baseCompactionCancellation = null;
            }
            _baseCompactionRequested = retryWhenIdle;
        }

        private void CompleteStorageGpuWork()
        {
            if (!GpuSubmissionAllowed) return;
            if (_loadStorageFailure != null)
            {
                Logger.Error("M8 SSD tile load failed: " +
                    _loadStorageFailure.Message);
                if (_loadAddresses == null)
                    throw new InvalidOperationException(
                        "A completed M8 load has no request addresses.");
                UploadLoadAddresses(_loadAddresses);
                FailLoadedTiles(_loadAddresses.Length);
                _loadRequestCursor += (uint)_loadAddresses.Length;
                AcknowledgeLoadRequests();
                _loadAddresses = null;
                _loadStorageFailure = null;
            }
            else if (_loadStorageResult != null)
            {
                MerkabaTileSnapshot[] tiles = _loadStorageResult;
                _loadStorageResult = null;
                SubmitLoadedTiles(tiles);
            }

            if (!_writebackCompletionPending) return;
            int count = _completedWritebackBatchCount;
            Exception failure = _writebackStorageFailure;
            _writebackCompletionPending = false;
            _completedWritebackBatchCount = 0;
            _writebackStorageFailure = null;
            if (failure != null)
            {
                Logger.Error("M8 SSD writeback failed; canonical tiles " +
                    "returned HOT and remain dirty: " + failure.Message);
                _flushCompletion?.TrySetException(failure);
                _flushCompletion = null;
                _flushAllDirty = false;
                _flushProgress = null;
                FailWritebackBatch(count);
                _nextStreamPoll = 0f;
            }
            else
                AcknowledgeWritebackBatch(count);
        }

        private void SubmitLoadedTiles(MerkabaTileSnapshot[] tiles)
        {
            if (!GpuSubmissionAllowed)
            {
                _loadStorageTask = Task.FromResult(tiles);
                return;
            }
            var addresses = new MerkabaTileAddress[tiles.Length];
            var states = new KernelState[tiles.Length *
                MerkabaSpatial.KernelsPerTile];
            for (int item = 0; item < tiles.Length; item++)
            {
                addresses[item] = tiles[item].Address;
                Array.Copy(tiles[item].States, 0, states,
                    item * MerkabaSpatial.KernelsPerTile,
                    MerkabaSpatial.KernelsPerTile);
            }
            _m8LoadStagingAddresses.SetData(addresses, 0, 0, addresses.Length);
            _m8LoadStagingStates.SetData(states, 0, 0, states.Length);
            InstallLoadedTiles(tiles.Length);
            _loadInstallStatusPending = true;
            int generation = _gpuGeneration;
            AsyncGPUReadback.Request(_m8LoadStagingAddresses,
                tiles.Length * 16,
                0, request =>
                {
                    _loadInstallStatusPending = false;
                    if (generation != _gpuGeneration) return;
                    if (_storageReplacementPending) return;
                    bool complete = !request.hasError;
                    if (complete)
                    {
                        var statuses = request.GetData<Raw16>();
                        for (int index = 0; index < statuses.Length; index++)
                            complete &= (statuses[index].W & 0x80000000u) != 0u;
                    }
                    if (complete)
                    {
                        _loadRequestCursor += (uint)tiles.Length;
                        AcknowledgeLoadRequests();
                        _loadAddresses = null;
                    }
                    else
                    {
                        _loadStorageTask = Task.FromResult(tiles);
                        _nextStreamPoll = Time.unscaledTime + 0.05f;
                    }
                });
        }

        private void UploadLoadAddresses(MerkabaTileAddress[] addresses)
        {
            if (!GpuSubmissionAllowed) return;
            _m8LoadStagingAddresses.SetData(addresses, 0, 0,
                addresses.Length);
        }

        private void AcknowledgeLoadRequests()
        {
            if (!GpuSubmissionAllowed)
            {
                _loadAcknowledgePending = true;
                return;
            }
            _streamControlWord[0] = _loadRequestCursor;
            _m8LoadRequestReadCount.SetData(_streamControlWord);
            _loadAcknowledgePending = false;
        }

        private void BeginWritebackReadback(int count)
        {
            if (_storageReplacementPending || !GpuSubmissionAllowed) return;
            _writebackReadbackPending = true;
            int rawCount = count * (MerkabaSpatial.KernelsPerTile + 1);
            int generation = _gpuGeneration;
            AsyncGPUReadback.Request(_m8WritebackStaging, rawCount * 16, 0,
                request =>
                {
                    _writebackReadbackPending = false;
                    if (generation != _gpuGeneration ||
                        _storageReplacementPending)
                        return;
                    if (request.hasError)
                    {
                        Logger.Error("M8 writeback staging readback failed; " +
                                     "canonical tiles return HOT and dirty.");
                        if (generation == _gpuGeneration)
                        {
                            _flushCompletion?.TrySetException(new IOException(
                                "M8 writeback staging readback failed."));
                            _flushCompletion = null;
                            _flushAllDirty = false;
                            _flushProgress = null;
                            if (GpuSubmissionAllowed)
                                FailWritebackBatch(count);
                            else
                                _deferredWritebackFailureCount = Math.Max(
                                    _deferredWritebackFailureCount, count);
                            _nextStreamPoll = 0f;
                        }
                        return;
                    }
                    var raw = request.GetData<Raw16>();
                    var tiles = new List<MerkabaTileSnapshot>(count);
                    for (int item = 0; item < count; item++)
                    {
                        int baseIndex = item *
                            (MerkabaSpatial.KernelsPerTile + 1);
                        Raw16 header = raw[baseIndex];
                        var states = new KernelState[MerkabaSpatial.KernelsPerTile];
                        for (int kernel = 0; kernel < states.Length; kernel++)
                        {
                            Raw16 value = raw[baseIndex + 1 + kernel];
                            states[kernel].OccupancyEvidence =
                                unchecked((int)value.X);
                            states[kernel].PackedColor = value.Y;
                            states[kernel].ColorConfidence = value.Z;
                            states[kernel].Flags = value.W;
                        }
                        tiles.Add(new MerkabaTileSnapshot
                        {
                            Address = new MerkabaTileAddress(new int3(
                                unchecked((int)header.X),
                                unchecked((int)header.Y),
                                unchecked((int)header.Z)), header.W),
                            States = states
                        });
                    }
                    EnsureStorage();
                    _writebackBatchCount = count;
                    _writeIoStartedAt = Time.realtimeSinceStartupAsDouble;
                    _writebackStorageTask = _ssdStore.AppendM8TilesAsync(tiles);
                });
        }

        private void SubmitDeferredStorageControl()
        {
            if (!GpuSubmissionAllowed) return;
            if (_loadAcknowledgePending) AcknowledgeLoadRequests();
            if (_deferredWritebackFailureCount <= 0) return;
            int count = _deferredWritebackFailureCount;
            _deferredWritebackFailureCount = 0;
            FailWritebackBatch(count);
        }

        internal void CaptureStorageMetrics(out float loadBytesPerSecond,
            out float writeBytesPerSecond, out float loadLatencyP50Ms,
            out float loadLatencyP95Ms, out float writeLatencyP50Ms,
            out float writeLatencyP95Ms)
        {
            loadBytesPerSecond = _loadBytesPerSecond;
            writeBytesPerSecond = _writeBytesPerSecond;
            loadLatencyP50Ms = StorageLatencyPercentile(
                _loadLatencies, _loadLatencyCount, 0.50f);
            loadLatencyP95Ms = StorageLatencyPercentile(
                _loadLatencies, _loadLatencyCount, 0.95f);
            writeLatencyP50Ms = StorageLatencyPercentile(
                _writeLatencies, _writeLatencyCount, 0.50f);
            writeLatencyP95Ms = StorageLatencyPercentile(
                _writeLatencies, _writeLatencyCount, 0.95f);
        }

        private void UpdateStorageRates()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            double elapsed = now - _storageRateSampleAt;
            if (elapsed < 1.0) return;
            _loadBytesPerSecond = (float)((_loadBytesTotal -
                _loadBytesAtRateSample) / elapsed);
            _writeBytesPerSecond = (float)((_writeBytesTotal -
                _writeBytesAtRateSample) / elapsed);
            _loadBytesAtRateSample = _loadBytesTotal;
            _writeBytesAtRateSample = _writeBytesTotal;
            _storageRateSampleAt = now;
        }

        private static void RecordStorageLatency(double[] samples,
            ref int count, ref int cursor, ref double startedAt)
        {
            if (startedAt <= 0.0) return;
            samples[cursor] = Math.Max(0.0,
                Time.realtimeSinceStartupAsDouble - startedAt);
            cursor = (cursor + 1) % samples.Length;
            count = Math.Min(count + 1, samples.Length);
            startedAt = 0.0;
        }

        private static float StorageLatencyPercentile(double[] samples,
            int count, float percentile)
        {
            if (count <= 0) return 0f;
            var sorted = new double[count];
            Array.Copy(samples, sorted, count);
            Array.Sort(sorted);
            int index = Mathf.Clamp(Mathf.CeilToInt((count - 1) * percentile),
                0, count - 1);
            return (float)(sorted[index] * 1000.0);
        }

        internal Task FlushAllDirtyTilesAsync(
            IProgress<OperationWorkProgress> progress = null)
        {
            EnsureGpuResources();
            if (!GpuSubmissionAllowed)
                return Task.FromException(new InvalidOperationException(
                    "Cannot flush M8 tiles while GPU submission is quiesced."));
            if (_flushCompletion != null) return _flushCompletion.Task;
            _flushAllDirty = true;
            _flushProgress = progress;
            _flushCompletedTiles = 0;
            _flushTotalTiles = -1;
            _flushCompletion = new TaskCompletionSource<bool>();
            _nextStreamPoll = 0f;
            _flushProgress?.Report(OperationWorkProgress.Indeterminate(
                ScanOperationStage.FlushingTiles,
                "Counting dirty canonical tiles"));
            return _flushCompletion.Task;
        }

        private void ReportFlushProgress(bool complete = false)
        {
            if (_flushProgress == null) return;
            int total = Math.Max(0, _flushTotalTiles);
            int completed = complete ? total : Math.Min(_flushCompletedTiles,
                total);
            _flushProgress.Report(new OperationWorkProgress(
                ScanOperationStage.FlushingTiles, completed, total,
                total == 0 ? "No dirty canonical tiles" :
                $"Flushed {completed}/{total} canonical tiles"));
        }

        internal MerkabaTileAddress[] CaptureStoredTileIndex()
        {
            EnsureStorage();
            return _ssdStore.SnapshotSortedAddresses();
        }

        internal bool TryGetStoredScanProximity(Vector3 worldPosition,
            out Vector3 worldDirection, out float distance)
        {
            EnsureStorage();
            Vector3 gridPosition = GridToWorldMatrix.inverse.MultiplyPoint3x4(
                worldPosition);
            if (!_ssdStore.TryFindNearestStoredTile(gridPosition,
                    out float3 closestGrid, out _))
            {
                worldDirection = Vector3.zero;
                distance = 0f;
                return false;
            }
            Vector3 closestWorld = GridToWorldMatrix.MultiplyPoint3x4(
                closestGrid);
            Vector3 delta = closestWorld - worldPosition;
            distance = delta.magnitude;
            worldDirection = distance > 1.0e-5f
                ? delta / distance : Vector3.zero;
            return true;
        }

        internal Task<MerkabaTileSnapshot[]> ReadStoredTilesAsync(
            IReadOnlyList<MerkabaTileAddress> addresses)
        {
            EnsureStorage();
            return _ssdStore.ReadAsync(addresses);
        }

        internal async Task<MerkabaStorageCommitResult> CommitStorageAsync(
            Guid sessionUuid, Guid anchorUuid, Matrix4x4 anchorAtSave,
            int integrationCount, uint occupiedKernelCount,
            IProgress<OperationWorkProgress> progress = null)
        {
            EnsureStorage();
            if (_storageReplacementPending)
                throw new InvalidOperationException(
                    "Cannot commit while storage authority is changing.");
            MerkabaStorageCommitResult result = await _ssdStore.CommitAsync(
                sessionUuid, anchorUuid, anchorAtSave, integrationCount,
                occupiedKernelCount, progress);
            _baseCompactionRequested = true;
            return result;
        }

        internal Task CloneCommittedStorageAsync(string destinationDirectory,
            Guid destinationSessionUuid)
        {
            EnsureStorage();
            if (_storageReplacementPending)
                return Task.FromException(new InvalidOperationException(
                    "Cannot clone while storage authority is changing."));
            return _ssdStore.CloneCommittedToAsync(destinationDirectory,
                destinationSessionUuid);
        }

        internal MerkabaSessionOpenState CurrentCommittedStorageState()
        {
            EnsureStorage();
            return _ssdStore.CurrentCommittedState();
        }

        internal async Task LoadCommittedStorageAsync(
            MerkabaSessionOpenState openState,
            IProgress<OperationWorkProgress> progress = null)
        {
            if (openState == null) throw new ArgumentNullException(
                nameof(openState));
            EnsureGpuResources();
            if (!GpuSubmissionAllowed)
                throw new InvalidOperationException(
                    "Cannot register committed M8 storage while GPU submission " +
                    "is suspended.");
            EnsureStorage();
            MerkabaSessionOpenState current = _ssdStore.CurrentCommittedState();
            if (current.Manifest.SessionUuid != openState.Manifest.SessionUuid ||
                current.Manifest.CommitGeneration !=
                openState.Manifest.CommitGeneration ||
                current.IndexedTileCount != openState.IndexedTileCount)
                throw new InvalidDataException(
                    "Committed storage authority changed before GPU registration.");

            _storageReplacementPending = true;
            try
            {
                MerkabaTileAddress[] addresses =
                    _ssdStore.SnapshotSortedAddresses();
                if (addresses.Length != openState.IndexedTileCount)
                    throw new InvalidDataException(
                        "Committed M8 index count changed during load.");
                int batches = DivideRoundUp(addresses.Length,
                    StreamBatchCapacity);
                int completedBatches = 0;
                for (int offset = 0; offset < addresses.Length;
                     offset += StreamBatchCapacity)
                {
                    if (!GpuSubmissionAllowed)
                        throw new InvalidOperationException(
                            "M8 Load was interrupted by GPU quiesce.");
                    int count = Math.Min(StreamBatchCapacity,
                        addresses.Length - offset);
                    var batch = new MerkabaTileAddress[count];
                    Array.Copy(addresses, offset, batch, 0, count);
                    UploadLoadAddresses(batch);
                    RegisterLoadedTileAddresses(count);
                    await Task.Yield();
                    completedBatches++;
                    progress?.Report(new OperationWorkProgress(
                        ScanOperationStage.ApplyingState, completedBatches,
                        batches,
                        $"Registered {completedBatches}/{batches} M8 batches"));
                }
                if (batches == 0)
                    progress?.Report(new OperationWorkProgress(
                        ScanOperationStage.ApplyingState, 0, 0,
                        "Registered empty M8 world"));
                uint[] counters = await ReadWorldCountersAsync();
                ulong addressedTiles = (ulong)counters[CounterHotTileCount] +
                    counters[CounterColdTileCount];
                if (counters[CounterBlockOverflow] != 0u ||
                    counters[CounterChunkOverflow] != 0u ||
                    counters[CounterHashFull] != 0u ||
                    addressedTiles != (ulong)addresses.Length)
                {
                    ClearGpuWorldForNewScan();
                    throw new InvalidDataException(
                        "Committed M8 index exceeds block/chunk/hash capacity " +
                        "or did not register every logical tile.");
                }
                _streamControlWord[0] =
                    openState.Manifest.OccupiedKernelCount;
                if (!GpuSubmissionAllowed)
                    throw new InvalidOperationException(
                        "M8 Load was interrupted by GPU quiesce.");
                _m8Counters.SetData(_streamControlWord, 0,
                    CounterOccupiedKernelCount, 1);
                M8OccupiedKernelCount = ToInt(
                    openState.Manifest.OccupiedKernelCount);
            }
            finally
            {
                _storageReplacementPending = false;
                _nextStreamPoll = 0f;
            }
        }

        private Task<uint[]> ReadWorldCountersAsync()
        {
            if (!GpuSubmissionAllowed)
                return Task.FromException<uint[]>(new InvalidOperationException(
                    "Cannot read M8 operation state while GPU submission is quiesced."));
            int generation = _gpuGeneration;
            var completion = new TaskCompletionSource<uint[]>();
            AsyncGPUReadback.Request(_m8Counters, request =>
            {
                if (generation != _gpuGeneration)
                    completion.TrySetException(new InvalidOperationException(
                        "M8 world changed while reading explicit operation state."));
                else if (request.hasError)
                    completion.TrySetException(new IOException(
                        "M8 counter readback failed during explicit Load."));
                else
                    completion.TrySetResult(request.GetData<uint>().ToArray());
            });
            return completion.Task;
        }

    }
}
