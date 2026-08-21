using UnityEngine;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// Sole owner of one canonical reconstruction tick. Capture publishes an immutable
    /// GPU mailbox; this graph consumes at most one coherent frame and enqueues every
    /// dependent pass in strict order before the preprocessor places the terminal fence.
    /// No native camera callback performs reconstruction and no stage can silently fork
    /// a second geometry path.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-5)]
    public sealed class PrismGpuWorkGraph : MonoBehaviour
    {
        private PrismDepthPreprocessor _preprocessor;
        private PrismPredictionRenderer _prediction;
        private PrismConeClassifier _classifier;
        private PrismFilmSpawner _spawner;
        private PrismPhotometricRefiner _photometric;
        private PrismFilmUpdater _updater;
        private PrismBoundaryGraph _boundaries;
        private PrismDisplacementTopology _displacement;
        private PrismMeshletBuilder _meshlets;
        private bool _running;
        private bool _dispatching;
        private long _completedFrames;
        private long _rejectedFrames;

        public bool IsRunning => _running;
        public long CompletedFrames => _completedFrames;
        public long RejectedFrames => _rejectedFrames;

        internal void StartGraph(PrismDepthPreprocessor preprocessor,
            PrismPredictionRenderer prediction, PrismConeClassifier classifier,
            PrismFilmSpawner spawner, PrismPhotometricRefiner photometric,
            PrismFilmUpdater updater,
            PrismBoundaryGraph boundaries, PrismDisplacementTopology displacement,
            PrismMeshletBuilder meshlets)
        {
            if (_running) return;
            _preprocessor = preprocessor;
            _prediction = prediction;
            _classifier = classifier;
            _spawner = spawner;
            _photometric = photometric;
            _updater = updater;
            _boundaries = boundaries;
            _displacement = displacement;
            _meshlets = meshlets;
            if (_preprocessor == null || _prediction == null || _classifier == null ||
                _spawner == null || _updater == null || _boundaries == null ||
                _photometric == null || _displacement == null || _meshlets == null)
            {
                Logger.Error("Cone-PRISM GPU work graph is incomplete.");
                return;
            }
            _preprocessor.FrameReady += ExecuteFrame;
            _running = true;
        }

        internal void StopGraph()
        {
            if (_running && _preprocessor != null)
                _preprocessor.FrameReady -= ExecuteFrame;
            _running = false;
            _dispatching = false;
        }

        private void OnDestroy() => StopGraph();

        private void ExecuteFrame(NormalizedRigFrameLease normalized)
        {
            if (!_running || _dispatching || normalized == null ||
                !normalized.IsValid)
            {
                _rejectedFrames++;
                return;
            }

            _dispatching = true;
            try
            {
                if (!_prediction.TryRenderFrame(normalized,
                        out PredictionFrameLease prediction) ||
                    !_classifier.TryClassifyFrame(prediction,
                        out ConeEventFrameLease events) ||
                    !_spawner.DispatchSpawn(events) ||
                    !_photometric.DispatchPhotometricPressure(events) ||
                    !_updater.DispatchUpdate(events, _photometric) ||
                    !_boundaries.DispatchBoundaries(events) ||
                    !_displacement.DispatchDisplacement(events))
                {
                    _rejectedFrames++;
                    return;
                }

                // The inactive publication can still be fenced by the preceding
                // render. BuildCurrent keeps its dirty request and retries without
                // blocking; no CPU wait or readback is introduced.
                _meshlets.BuildCurrent();
                _completedFrames++;
            }
            finally
            {
                _dispatching = false;
            }
        }
    }
}
