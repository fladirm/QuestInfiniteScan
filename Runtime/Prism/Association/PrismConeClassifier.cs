using System;
using UnityEngine;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// Exhaustive finite-cone first-hit classifier. It emits deterministic per-pixel
    /// ConeEvents plus class-compacted GPU index segments and indirect dispatch args.
    /// BEHIND is contradiction evidence only; no kernel here carves behind first hit.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(0)]
    public sealed class PrismConeClassifier : MonoBehaviour
    {
        [SerializeField] private PrismPredictionRenderer predictionRenderer;
        [SerializeField] private PrismBoundaryGraph boundaryGraph;
        [SerializeField] private ComputeShader classifyCompute;
        [SerializeField, Range(3, 10)] private int eventRingSlots = 4;
        [SerializeField, Range(1f, 60f)] private float normalGateDegrees = 25f;
        [SerializeField, Range(0f, 1f)] private float boundaryGate = 0.35f;

        private static readonly int ResolutionId = Shader.PropertyToID("_Resolution");
        private static readonly int EventCapacityId = Shader.PropertyToID("_EventCapacity");
        private static readonly int NormalCosineGateId = Shader.PropertyToID("_NormalCosineGate");
        private static readonly int BoundaryGateId = Shader.PropertyToID("_BoundaryGate");
        private static readonly int MeasuredConsensusId = Shader.PropertyToID("_MeasuredConsensus");
        private static readonly int MeasuredFlagsId = Shader.PropertyToID("_MeasuredFlags");
        private static readonly int MeasuredNormalId = Shader.PropertyToID("_MeasuredNormal");
        private static readonly int BoundaryEvidenceId = Shader.PropertyToID("_BoundaryEvidence");
        private static readonly int PredictedDepthSigmaId = Shader.PropertyToID("_PredictedDepthSigma");
        private static readonly int PredictedNormalConfidenceId = Shader.PropertyToID("_PredictedNormalConfidence");
        private static readonly int PredictedFilmIdGenerationId = Shader.PropertyToID("_PredictedFilmIdGeneration");
        private static readonly int PredictedUvMetadataId = Shader.PropertyToID("_PredictedUvMetadata");
        private static readonly int Layer1DepthSigmaId = Shader.PropertyToID("_Layer1DepthSigma");
        private static readonly int Layer1NormalConfidenceId = Shader.PropertyToID("_Layer1NormalConfidence");
        private static readonly int Layer1FilmIdGenerationId = Shader.PropertyToID("_Layer1FilmIdGeneration");
        private static readonly int Layer1UvMetadataId = Shader.PropertyToID("_Layer1UvMetadata");
        private static readonly int RayLeftId = Shader.PropertyToID("_DepthRayCenterLeft");
        private static readonly int RayRightId = Shader.PropertyToID("_DepthRayCenterRight");
        private static readonly int RayDxLeftId = Shader.PropertyToID("_DepthRayDifferentialXLeft");
        private static readonly int RayDxRightId = Shader.PropertyToID("_DepthRayDifferentialXRight");
        private static readonly int RayDyLeftId = Shader.PropertyToID("_DepthRayDifferentialYLeft");
        private static readonly int RayDyRightId = Shader.PropertyToID("_DepthRayDifferentialYRight");
        private static readonly int EventsId = Shader.PropertyToID("_ConeEvents");
        private static readonly int ClassifiedIndicesId = Shader.PropertyToID("_ClassifiedIndices");
        private static readonly int ClassCountersId = Shader.PropertyToID("_ClassCounters");
        private static readonly int DispatchArgumentsId = Shader.PropertyToID("_ClassDispatchArguments");
        private static readonly int HasCanonicalBoundariesId = Shader.PropertyToID("_HasCanonicalBoundaries");
        private static readonly int BoundaryHashMaskId = Shader.PropertyToID("_CanonicalBoundaryHashMask");
        private static readonly int BoundaryCellsPerAxisId = Shader.PropertyToID("_CanonicalBoundaryCellsPerAxis");
        private static readonly int CanonicalBoundaryHeadersId = Shader.PropertyToID("_CanonicalBoundaryHeaders");
        private static readonly int CanonicalBoundaryHashId = Shader.PropertyToID("_CanonicalBoundaryHash");

        private ConeEventBufferRing _ring;
        private ConeEventFrameLease _latest;
        private int _clearKernel = -1;
        private int _classifyKernel = -1;
        private int _buildArgsKernel = -1;
        private bool _running;
        private long _classifiedFrames;
        private long _backpressureFrames;

        public event Action<ConeEventFrameLease> EventsReady;
        public long ClassifiedFrames => _classifiedFrames;
        public long BackpressureFrames => _backpressureFrames;

        public bool TryAcquireLatest(out ConeEventFrameLease frame)
        {
            if (_latest == null || _latest.IsDisposed)
            {
                frame = null;
                return false;
            }
            frame = _latest.Retain();
            return true;
        }

        public void StartClassifying(PrismPredictionRenderer source = null,
            PrismBoundaryGraph boundaries = null)
        {
            if (_running) return;
            predictionRenderer = source != null ? source : predictionRenderer;
            boundaryGraph = boundaries != null ? boundaries : boundaryGraph;
            predictionRenderer ??= GetComponent<PrismPredictionRenderer>();
            boundaryGraph ??= GetComponent<PrismBoundaryGraph>();
            classifyCompute ??= Resources.Load<ComputeShader>("Prism/ConeClassify");
            if (predictionRenderer == null || classifyCompute == null)
            {
                Logger.Error("Cone-PRISM classifier dependencies are missing.");
                return;
            }
            _clearKernel = classifyCompute.FindKernel("ClearClassCounters");
            _classifyKernel = classifyCompute.FindKernel("ClassifyConeEvents");
            _buildArgsKernel = classifyCompute.FindKernel("BuildClassDispatchArguments");
            _ring ??= new ConeEventBufferRing(eventRingSlots);
            predictionRenderer.PredictionReady += OnPrediction;
            _running = true;
        }

        public void StopClassifying()
        {
            if (_running && predictionRenderer != null)
                predictionRenderer.PredictionReady -= OnPrediction;
            _running = false;
            _latest?.Dispose();
            _latest = null;
            _ring?.Dispose();
            _ring = null;
        }

        private void OnDestroy() => StopClassifying();

        private void OnPrediction(PredictionFrameLease prediction)
        {
            if (!_running || prediction == null || prediction.IsDisposed) return;
            if (!_ring.TryBegin(prediction, out ConeEventFrameLease eventsFrame))
            {
                _backpressureFrames++;
                return;
            }

            try
            {
                NormalizedRigFrameLease measured = prediction.Source;
                ConeLutLease luts = measured.ConeLuts;
                Vector2Int resolution = measured.Source.DepthLeft.Resolution;
                classifyCompute.SetInts(ResolutionId, resolution.x, resolution.y);
                classifyCompute.SetInt(EventCapacityId, eventsFrame.EventCapacity);
                classifyCompute.SetFloat(NormalCosineGateId,
                    Mathf.Cos(normalGateDegrees * Mathf.Deg2Rad));
                classifyCompute.SetFloat(BoundaryGateId, boundaryGate);

                BindTextures(_classifyKernel, measured, prediction, luts);
                BindCanonicalBoundaries(_classifyKernel);
                classifyCompute.SetBuffer(_clearKernel, ClassCountersId,
                    eventsFrame.ClassCounters);
                classifyCompute.SetBuffer(_clearKernel, DispatchArgumentsId,
                    eventsFrame.ClassDispatchArguments);
                classifyCompute.SetBuffer(_classifyKernel, EventsId, eventsFrame.Events);
                classifyCompute.SetBuffer(_classifyKernel, ClassifiedIndicesId,
                    eventsFrame.ClassifiedIndices);
                classifyCompute.SetBuffer(_classifyKernel, ClassCountersId,
                    eventsFrame.ClassCounters);
                classifyCompute.SetBuffer(_buildArgsKernel, ClassCountersId,
                    eventsFrame.ClassCounters);
                classifyCompute.SetBuffer(_buildArgsKernel, DispatchArgumentsId,
                    eventsFrame.ClassDispatchArguments);
                classifyCompute.Dispatch(_clearKernel, 1, 1, 1);
                classifyCompute.Dispatch(_classifyKernel, CeilDiv(resolution.x, 8),
                    CeilDiv(resolution.y, 8), 2);
                classifyCompute.Dispatch(_buildArgsKernel, 1, 1, 1);
                eventsFrame.CommitGpuWrite();

                ConeEventFrameLease previous = _latest;
                _latest = eventsFrame;
                previous?.Dispose();
                _classifiedFrames++;
                EventsReady?.Invoke(eventsFrame);
            }
            catch (Exception exception)
            {
                eventsFrame.Dispose();
                Logger.Error($"Cone-PRISM classification failed: {exception.Message}");
            }
        }

        private void BindCanonicalBoundaries(int kernel)
        {
            ContactBoundaryPool pool = boundaryGraph?.BoundaryPool;
            bool available = pool != null && !pool.IsDisposed;
            classifyCompute.SetInt(HasCanonicalBoundariesId, available ? 1 : 0);
            if (!available) return;
            classifyCompute.SetInt(BoundaryHashMaskId, pool.HashCapacity - 1);
            classifyCompute.SetInt(BoundaryCellsPerAxisId, boundaryGraph.CellsPerAxis);
            classifyCompute.SetBuffer(kernel, CanonicalBoundaryHeadersId, pool.Headers);
            classifyCompute.SetBuffer(kernel, CanonicalBoundaryHashId, pool.HashEntries);
        }

        private void BindTextures(int kernel, NormalizedRigFrameLease measured,
            PredictionFrameLease prediction, ConeLutLease luts)
        {
            classifyCompute.SetTexture(kernel, MeasuredConsensusId, measured.ConsensusDepth);
            classifyCompute.SetTexture(kernel, MeasuredFlagsId, measured.Flags);
            classifyCompute.SetTexture(kernel, MeasuredNormalId, measured.LocalNormal);
            classifyCompute.SetTexture(kernel, BoundaryEvidenceId, measured.BoundaryEvidence);
            classifyCompute.SetTexture(kernel, PredictedDepthSigmaId, prediction.DepthSigma);
            classifyCompute.SetTexture(kernel, PredictedNormalConfidenceId,
                prediction.NormalConfidence);
            classifyCompute.SetTexture(kernel, PredictedFilmIdGenerationId,
                prediction.FilmIdGeneration);
            classifyCompute.SetTexture(kernel, PredictedUvMetadataId, prediction.UvMetadata);
            classifyCompute.SetTexture(kernel, Layer1DepthSigmaId,
                prediction.Layer1DepthSigma);
            classifyCompute.SetTexture(kernel, Layer1NormalConfidenceId,
                prediction.Layer1NormalConfidence);
            classifyCompute.SetTexture(kernel, Layer1FilmIdGenerationId,
                prediction.Layer1FilmIdGeneration);
            classifyCompute.SetTexture(kernel, Layer1UvMetadataId,
                prediction.Layer1UvMetadata);
            classifyCompute.SetTexture(kernel, RayLeftId, luts.DepthLeft.CenterRaySolidAngle);
            classifyCompute.SetTexture(kernel, RayRightId, luts.DepthRight.CenterRaySolidAngle);
            classifyCompute.SetTexture(kernel, RayDxLeftId,
                luts.DepthLeft.DifferentialXHalfAngle);
            classifyCompute.SetTexture(kernel, RayDxRightId,
                luts.DepthRight.DifferentialXHalfAngle);
            classifyCompute.SetTexture(kernel, RayDyLeftId,
                luts.DepthLeft.DifferentialYHalfAngle);
            classifyCompute.SetTexture(kernel, RayDyRightId,
                luts.DepthRight.DifferentialYHalfAngle);
        }

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);
    }
}
