using System;
using System.IO;
using System.Threading.Tasks;

namespace Genesis.RoomScan
{
    public sealed partial class MerkabaGrid
    {
        private Task _observationDrainTask;
        private Task<MerkabaStorageCommitResult> _observationDurableTask;
        private MerkabaSsdStore _observationCommitStore;
        private MerkabaCommitMetadata _observationCommitMetadata;
        private MerkabaIntegrator _observationCommitIntegrator;
        private MerkabaPersistence _observationCommitPersistence;
        private int _observationCommitWorld;
        private uint _observationCommitSource;
        private uint _observationCommitToken;
        private uint _observationCommitChange;
        private bool _observationCommitRetry;
        private int _observationCommitWaiters;
        private Exception _observationCommitFailure;

        internal bool HasObservationDurableCut =>
            _observationDrainTask != null || _observationDurableTask != null;

        // Only semantic writers stop during the source drain. Storage work
        // keeps running, and fsync/manifest publication does not hold the GPU.
        internal bool ObservationMutationSubmissionAllowed
        {
            get
            {
                PumpObservationDurableCut();
                return !_storageReplacementPending && !_flushAllDirty &&
                    _observationDrainTask == null && DualMutationSubmissionAllowed;
            }
        }

        private void PumpObservationDurableCut()
        {
            if (_observationDurableTask != null)
            {
                if (!_observationDurableTask.IsCompleted) return;
                Task<MerkabaStorageCommitResult> commit = _observationDurableTask;
                _observationDurableTask = null;
                try
                {
                    MerkabaStorageCommitResult result = commit.GetAwaiter().GetResult();
                    if (_observationCommitWorld == _gpuGeneration &&
                        ReferenceEquals(_observationCommitStore, _ssdStore))
                    {
                        if (result.Committed)
                        {
                            AcknowledgeDualDurableGeneration(_observationCommitSource);
                            _baseCompactionRequested = true;
                        }
                        else
                            _observationCommitRetry = true;
                    }
                }
                catch (Exception failure)
                {
                    _observationCommitFailure = failure;
                    Logger.Error("Observation storage commit failed; no durable " +
                        "generation was acknowledged: " + failure.GetBaseException().Message);
                }
            }

            if (_observationDrainTask != null)
            {
                if (!_observationDrainTask.IsCompleted) return;
                Task drain = _observationDrainTask;
                try
                {
                    drain.GetAwaiter().GetResult();
                    if (_observationCommitWorld != _gpuGeneration ||
                        !ReferenceEquals(_observationCommitStore, _ssdStore) ||
                        _drainedDualGeneration != _observationCommitSource)
                        throw new InvalidDataException("Observation drain source changed.");

                    // The receipt proves zero dirty M8 tiles AND zero dirty
                    // dual nodes after their SSD append acknowledgements. The
                    // append position additionally rejects an overtaking CPU
                    // writer; neither a fence nor a sampled queue count does.
                    MerkabaStorageAppendPosition position =
                        _observationCommitStore.CaptureAppendPosition();
                    MerkabaCommitMetadata metadata = _observationCommitMetadata;
                    _observationDurableTask = _observationCommitStore.CommitAsync(
                        position, metadata.SessionUuid, metadata.AnchorUuid,
                        metadata.AnchorAtSave, metadata.IntegrationCount,
                        _drainedOccupiedKernelCount);
                }
                catch (Exception failure)
                {
                    _observationCommitFailure = failure;
                    if (_flushCompletion != null &&
                        ReferenceEquals(_flushCompletion.Task, drain))
                    {
                        _flushCompletion = null;
                        _flushAllDirty = false;
                        _flushProgress = null;
                    }
                    Logger.Error("Observation source drain failed; no durable " +
                        "generation was acknowledged: " + failure.GetBaseException().Message);
                }
                finally
                {
                    // Commit owns its immutable CPU inputs now. Later GPU
                    // observations may proceed while these streams are flushed.
                    _observationDrainTask = null;
                }
                return;
            }

            // Automatic cuts are pressure driven, not a full flush per frame.
            // A failed finite reclaim sweep cannot wake itself merely because
            // a read-only GPU submission incremented the publishing sequence.
            if (_observationDurableTask != null || _observationCommitWaiters != 0 ||
                _storageReplacementPending || _flushCompletion != null ||
                _baseCompactionTask != null || !_dualCapacityBackpressure ||
                _dualCapacitySampleGpuGeneration != _gpuGeneration ||
                _dualCapacitySampleObservation != _issuedObservationToken ||
                _dualCapacitySampleObservation != _retiredObservationToken ||
                !DualMutationSubmissionAllowed || _dualPublishedGeneration == 0u)
                return;
            if (!_observationCommitRetry &&
                _observationCommitWorld == _gpuGeneration &&
                _observationCommitToken == _dualCapacitySampleObservation &&
                _observationCommitChange == _retiredObservationToken)
                return;

            if (_observationCommitIntegrator == null)
                _observationCommitIntegrator = GetComponent<MerkabaIntegrator>();
            if (_observationCommitPersistence == null)
                _observationCommitPersistence = GetComponent<MerkabaPersistence>();
            if (_observationCommitIntegrator == null ||
                _observationCommitIntegrator.HasAttemptInFlight ||
                _observationCommitIntegrator.HasFineEraseAttemptInFlight ||
                _observationCommitPersistence == null ||
                _observationCommitPersistence.IsBusy ||
                !_observationCommitPersistence.TryCaptureCommitMetadata(
                    out MerkabaCommitMetadata captured))
                return;

            EnsureStorage();
            _observationCommitStore = _ssdStore;
            _observationCommitMetadata = captured;
            _observationCommitWorld = _gpuGeneration;
            _observationCommitSource = _dualPublishedGeneration;
            _observationCommitToken = _dualCapacitySampleObservation;
            _observationCommitChange = _retiredObservationToken;
            _observationCommitRetry = false;
            _observationDrainTask = FlushAllDirtyTilesAsync();
        }

        // Lifecycle callers use the same pump, before closing GPU submission.
        // Do not start another automatic cut while SAVE/OPEN/quiesce waits.
        internal async Task FinishObservationDurableCutAsync()
        {
            Exception priorFailure = _observationCommitFailure;
            _observationCommitWaiters++;
            try
            {
                while (HasObservationDurableCut)
                {
                    if (_observationDrainTask != null && !GpuSubmissionAllowed)
                        _flushCompletion?.TrySetException(new InvalidOperationException(
                            "GPU submission stopped before the observation drain retired."));
                    PumpStorage();
                    if (HasObservationDurableCut) await Task.Yield();
                }
                if (!ReferenceEquals(priorFailure, _observationCommitFailure))
                    throw new IOException("Observation storage cut failed during retirement.",
                        _observationCommitFailure);
            }
            finally { _observationCommitWaiters--; }
        }
    }
}
