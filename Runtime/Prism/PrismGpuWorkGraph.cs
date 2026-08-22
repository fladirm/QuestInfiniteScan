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
        private PrismPressureManifoldAtlas _atlas;
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
            PrismPressureManifoldAtlas atlas, PrismMeshletBuilder meshlets)
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
            _atlas = atlas;
            _meshlets = meshlets;
            if (_preprocessor == null || _prediction == null || _classifier == null ||
                _spawner == null || _updater == null || _boundaries == null ||
                _photometric == null || _displacement == null || _atlas == null ||
                _meshlets == null)
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
                    !_displacement.DispatchDisplacement(events) ||
                    !_atlas.DispatchAtlas())
                {
                    _rejectedFrames++;
                    return;
                }

                // Geometry evidence is integrated every tick. The atlas coalesces
                // topology into transactional batches; materialize exactly those
                // batches instead of rebuilding an equivalent derived mesh between
                // them. The previous immutable generation remains visible and valid.
                if (_atlas.DispatchedThisTick) _meshlets.RequestBuild();
                _completedFrames++;
            }
            finally
            {
                _dispatching = false;
            }
        }
    }
}
