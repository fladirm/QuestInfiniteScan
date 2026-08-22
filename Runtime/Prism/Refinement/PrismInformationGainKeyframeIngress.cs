using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// GPU-only temporal RGB ingress. Visible first-hit ContactFilms vote with the
    /// expected information they add; the accepted stereo slot, generation, texture
    /// copy and metadata publication never require a CPU readback.
    /// </summary>
    internal sealed class PrismInformationGainKeyframeIngress : IDisposable
    {
        private const int MatchClassDispatchOffset =
            (int)ConeEventClass.Match * sizeof(uint) * 3;

        private static readonly int EventCapacityId = Shader.PropertyToID("_EventCapacity");
        private static readonly int FilmCapacityId = Shader.PropertyToID("_FilmCapacity");
        private static readonly int TemporalViewCapacityId =
            Shader.PropertyToID("_TemporalViewCapacity");
        private static readonly int CurrentSequenceId = Shader.PropertyToID("_CurrentSequence");
        private static readonly int MaximumIntervalId =
            Shader.PropertyToID("_MaximumKeyframeInterval");
        private static readonly int MinimumSpacingId =
            Shader.PropertyToID("_MinimumKeyframeSpacing");
        private static readonly int MinimumFilmGainId =
            Shader.PropertyToID("_MinimumFilmInformationGain");
        private static readonly int MinimumFrameGainId =
            Shader.PropertyToID("_MinimumFrameInformationGain");
        private static readonly int AggregateFrameGainId =
            Shader.PropertyToID("_AggregateFrameInformationGain");
        private static readonly int MinimumSigmaId = Shader.PropertyToID("_MinimumRefineSigma");
        private static readonly int RgbResolutionId = Shader.PropertyToID("_RgbResolution");
        private static readonly int RgbIntrinsicsId = Shader.PropertyToID("_RgbIntrinsics");
        private static readonly int RgbFromChunkId = Shader.PropertyToID("_RgbFromChunk");
        private static readonly int ChunkFromDepthId = Shader.PropertyToID("_ChunkFromDepth");
        private static readonly int CameraOriginId = Shader.PropertyToID("_CameraOriginChunk");
        private static readonly int EventsId = Shader.PropertyToID("_ConeEvents");
        private static readonly int ClassifiedIndicesId =
            Shader.PropertyToID("_ClassifiedIndices");
        private static readonly int ClassCountersId = Shader.PropertyToID("_ClassCounters");
        private static readonly int FilmHeadersId = Shader.PropertyToID("_FilmHeaders");
        private static readonly int FilmInformationId = Shader.PropertyToID("_FilmInformation");
        private static readonly int FilmAllocatorId = Shader.PropertyToID("_FilmAllocator");
        private static readonly int ActiveFilmsId =
            Shader.PropertyToID("_CanonicalActiveFilmIndices");
        private static readonly int RayLeftId = Shader.PropertyToID("_DepthRayCenterLeft");
        private static readonly int RayRightId = Shader.PropertyToID("_DepthRayCenterRight");
        private static readonly int RgbLeftId = Shader.PropertyToID("_RgbLeft");
        private static readonly int RgbRightId = Shader.PropertyToID("_RgbRight");
        private static readonly int FilmGainId = Shader.PropertyToID("_KeyframeFilmGain");
        private static readonly int StateId = Shader.PropertyToID("_KeyframeState");
        private static readonly int SlotGenerationsId =
            Shader.PropertyToID("_KeyframeSlotGenerations");
        private static readonly int DispatchArgumentsId =
            Shader.PropertyToID("_KeyframeDispatchArguments");
        private static readonly int ViewSelectArgumentsId =
            Shader.PropertyToID("_ViewSelectDispatchArguments");
        private static readonly int TemporalViewsId = Shader.PropertyToID("_TemporalViews");
        private static readonly int TemporalRgbWriteId =
            Shader.PropertyToID("_TemporalRgbWrite");

        private ComputeShader _compute;
        private GraphicsBuffer _filmGain;
        private GraphicsBuffer _state;
        private GraphicsBuffer _slotGenerations;
        private GraphicsBuffer _dispatchArguments;
        private int _filmCapacity;
        private int _temporalViewCapacity;
        private int _initialize = -1;
        private int _begin = -1;
        private int _clear = -1;
        private int _evaluate = -1;
        private int _reduce = -1;
        private int _finalize = -1;
        private int _commitPixels = -1;
        private int _commitMetadata = -1;
        private readonly GpuResourceRetirementQueue _retirement;

        internal PrismInformationGainKeyframeIngress(
            GpuResourceRetirementQueue retirement)
        {
            _retirement = retirement ?? throw new ArgumentNullException(
                nameof(retirement));
        }

        internal GraphicsBuffer FilmGain => _filmGain;
        internal GraphicsBuffer State => _state;
        internal bool IsReady => _compute != null && _state != null;

        internal bool Ensure(int filmCapacity, int temporalViewCapacity)
        {
            _compute ??= Resources.Load<ComputeShader>(
                "Prism/InformationGainKeyframes");
            if (_compute == null) return false;
            FindKernels();
            if (_filmGain != null && _filmCapacity == filmCapacity &&
                _temporalViewCapacity == temporalViewCapacity) return true;

            DisposeBuffers();
            _filmCapacity = Math.Max(1, filmCapacity);
            _temporalViewCapacity = Math.Max(2, temporalViewCapacity);
            _filmGain = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                _filmCapacity, sizeof(uint));
            _state = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 16,
                sizeof(uint));
            _slotGenerations = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                Math.Max(1, _temporalViewCapacity / 2), sizeof(uint));
            _dispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 2, sizeof(uint) * 3);
            return true;
        }

        internal void Reset(ContactFilmPool pool, GraphicsBuffer temporalViews,
            GraphicsBuffer viewSelectArguments)
        {
            if (!IsReady) return;
            SetCapacities();
            _compute.SetBuffer(_initialize, FilmGainId, _filmGain);
            _compute.SetBuffer(_initialize, StateId, _state);
            _compute.SetBuffer(_initialize, SlotGenerationsId, _slotGenerations);
            _compute.SetBuffer(_initialize, DispatchArgumentsId, _dispatchArguments);
            _compute.SetBuffer(_initialize, ViewSelectArgumentsId,
                viewSelectArguments);
            _compute.Dispatch(_initialize,
                CeilDiv(Math.Max(_filmCapacity, _temporalViewCapacity / 2), 64),
                1, 1);
        }

        internal void Dispatch(ConeEventFrameLease eventFrame, ContactFilmPool pool,
            StereoRigFrameLease rig, NormalizedRigFrameLease normalized,
            Matrix4x4[] rgbFromChunk, Matrix4x4[] chunkFromDepth,
            Vector4[] rgbIntrinsics, Vector4[] cameraOriginChunk,
            GraphicsBuffer temporalViews, RenderTexture temporalRgb,
            GraphicsBuffer viewSelectArguments, float minimumSigma,
            int maximumInterval, int minimumSpacing, float minimumFilmGain,
            float minimumFrameGain, float aggregateFrameGain)
        {
            if (!IsReady) throw new InvalidOperationException(
                "Information-gain keyframe ingress is not initialized.");
            SetCapacities();
            _compute.SetInt(EventCapacityId, eventFrame.EventCapacity);
            _compute.SetInt(CurrentSequenceId, unchecked((int)rig.Sequence));
            _compute.SetInt(MaximumIntervalId, Math.Max(1, maximumInterval));
            _compute.SetInt(MinimumSpacingId, Math.Max(1, minimumSpacing));
            _compute.SetFloat(MinimumFilmGainId, minimumFilmGain);
            _compute.SetFloat(MinimumFrameGainId, minimumFrameGain);
            _compute.SetFloat(AggregateFrameGainId, aggregateFrameGain);
            _compute.SetFloat(MinimumSigmaId, minimumSigma);
            _compute.SetInts(RgbResolutionId, rig.RgbLeft.Resolution.x,
                rig.RgbLeft.Resolution.y);
            _compute.SetVectorArray(RgbIntrinsicsId, rgbIntrinsics);
            _compute.SetMatrixArray(RgbFromChunkId, rgbFromChunk);
            _compute.SetMatrixArray(ChunkFromDepthId, chunkFromDepth);
            _compute.SetVectorArray(CameraOriginId, cameraOriginChunk);

            BindCommon(pool, viewSelectArguments);
            _compute.SetBuffer(_evaluate, EventsId, eventFrame.Events);
            _compute.SetBuffer(_evaluate, ClassifiedIndicesId,
                eventFrame.ClassifiedIndices);
            _compute.SetBuffer(_evaluate, ClassCountersId,
                eventFrame.ClassCounters);
            _compute.SetTexture(_evaluate, RayLeftId,
                normalized.ConeLuts.DepthLeft.CenterRaySolidAngle);
            _compute.SetTexture(_evaluate, RayRightId,
                normalized.ConeLuts.DepthRight.CenterRaySolidAngle);
            _compute.SetTexture(_evaluate, RgbLeftId, rig.RgbLeft.Texture);
            _compute.SetTexture(_evaluate, RgbRightId, rig.RgbRight.Texture);
            _compute.SetBuffer(_finalize, ClassCountersId,
                eventFrame.ClassCounters);
            _compute.SetTexture(_commitPixels, RgbLeftId, rig.RgbLeft.Texture);
            _compute.SetTexture(_commitPixels, RgbRightId, rig.RgbRight.Texture);
            _compute.SetTexture(_commitPixels, TemporalRgbWriteId, temporalRgb);
            _compute.SetBuffer(_commitMetadata, TemporalViewsId, temporalViews);

            _compute.Dispatch(_begin, 1, 1, 1);
            _compute.DispatchIndirect(_clear, _dispatchArguments, 0);
            _compute.DispatchIndirect(_evaluate,
                eventFrame.ClassDispatchArguments, MatchClassDispatchOffset);
            _compute.DispatchIndirect(_reduce, _dispatchArguments, 0);
            _compute.Dispatch(_finalize, 1, 1, 1);
            _compute.DispatchIndirect(_commitPixels, _dispatchArguments,
                sizeof(uint) * 3);
            _compute.Dispatch(_commitMetadata, 1, 1, 1);
        }

        private void BindCommon(ContactFilmPool pool,
            GraphicsBuffer viewSelectArguments)
        {
            int[] stateKernels =
            {
                _begin, _clear, _evaluate, _reduce, _finalize,
                _commitPixels, _commitMetadata
            };
            foreach (int kernel in stateKernels)
                _compute.SetBuffer(kernel, StateId, _state);

            _compute.SetBuffer(_begin, FilmAllocatorId, pool.Allocator);
            _compute.SetBuffer(_begin, DispatchArgumentsId, _dispatchArguments);
            _compute.SetBuffer(_begin, ViewSelectArgumentsId, viewSelectArguments);

            _compute.SetBuffer(_clear, FilmAllocatorId, pool.Allocator);
            _compute.SetBuffer(_clear, ActiveFilmsId, pool.ActiveIndices);
            _compute.SetBuffer(_clear, FilmGainId, _filmGain);

            _compute.SetBuffer(_evaluate, FilmHeadersId, pool.Headers);
            _compute.SetBuffer(_evaluate, FilmInformationId, pool.Information);
            _compute.SetBuffer(_evaluate, FilmGainId, _filmGain);

            _compute.SetBuffer(_reduce, FilmAllocatorId, pool.Allocator);
            _compute.SetBuffer(_reduce, ActiveFilmsId, pool.ActiveIndices);
            _compute.SetBuffer(_reduce, FilmGainId, _filmGain);

            _compute.SetBuffer(_finalize, FilmAllocatorId, pool.Allocator);
            _compute.SetBuffer(_finalize, SlotGenerationsId, _slotGenerations);
            _compute.SetBuffer(_finalize, DispatchArgumentsId, _dispatchArguments);
            _compute.SetBuffer(_finalize, ViewSelectArgumentsId,
                viewSelectArguments);
        }

        private void SetCapacities()
        {
            _compute.SetInt(FilmCapacityId, _filmCapacity);
            _compute.SetInt(TemporalViewCapacityId, _temporalViewCapacity);
        }

        private void FindKernels()
        {
            if (_initialize >= 0) return;
            _initialize = _compute.FindKernel("InitializeKeyframeIngress");
            _begin = _compute.FindKernel("BeginKeyframeEvaluation");
            _clear = _compute.FindKernel("ClearActiveFilmGain");
            _evaluate = _compute.FindKernel("EvaluateVisibleFilmGain");
            _reduce = _compute.FindKernel("ReduceFrameInformationGain");
            _finalize = _compute.FindKernel("FinalizeKeyframeDecision");
            _commitPixels = _compute.FindKernel("CommitKeyframePixels");
            _commitMetadata = _compute.FindKernel("CommitKeyframeMetadata");
        }

        public void Dispose()
        {
            DisposeBuffers();
            _compute = null;
            _initialize = _begin = _clear = _evaluate = _reduce = _finalize =
                _commitPixels = _commitMetadata = -1;
        }

        private void DisposeBuffers()
        {
            _retirement.RetireAfterCurrentGpuWork(_filmGain);
            _retirement.RetireAfterCurrentGpuWork(_state);
            _retirement.RetireAfterCurrentGpuWork(_slotGenerations);
            _retirement.RetireAfterCurrentGpuWork(_dispatchArguments);
            _filmGain = null;
            _state = null;
            _slotGenerations = null;
            _dispatchArguments = null;
            _filmCapacity = 0;
            _temporalViewCapacity = 0;
        }

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);
    }
}
