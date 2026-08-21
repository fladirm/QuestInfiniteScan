using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// Persistent GPU information-filter update for MATCH cone events.  Metric cone
    /// precision is converted into a film-relative quality envelope: strong close,
    /// frontal observations move and harden the manifold, while substantially weaker
    /// distant/grazing observations may add support but cannot blur trusted detail.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(15)]
    public sealed class PrismFilmUpdater : MonoBehaviour
    {
        private const int AccumulatorWordsPerFilm = 32;
        private const int MatchClassDispatchOffset =
            (int)ConeEventClass.Match * sizeof(uint) * 3;
        private const int BehindClassDispatchOffset =
            (int)ConeEventClass.Behind * sizeof(uint) * 3;

        [SerializeField] private PrismConeClassifier coneClassifier;
        [SerializeField] private PrismFilmSpawner filmSpawner;
        [SerializeField] private ComputeShader updateCompute;
        [SerializeField, Range(0.05f, 0.9f)]
        private float geometryQualityFloor = 0.25f;
        [SerializeField, Min(0.00025f)] private float minimumSurfaceSigma = 0.0005f;
        [SerializeField, Min(0.00025f)] private float minimumHuberDelta = 0.0015f;

        private static readonly int FilmCapacityId = Shader.PropertyToID("_FilmCapacity");
        private static readonly int EventCapacityId = Shader.PropertyToID("_EventCapacity");
        private static readonly int PhotometricCapacityId =
            Shader.PropertyToID("_PhotometricCapacity");
        private static readonly int QualityFloorId = Shader.PropertyToID("_QualityFloor");
        private static readonly int MinimumSigmaId = Shader.PropertyToID("_MinimumSigma");
        private static readonly int MinimumHuberId = Shader.PropertyToID("_MinimumHuber");
        private static readonly int ChunkFromDepthId = Shader.PropertyToID("_ChunkFromDepth");
        private static readonly int EventsId = Shader.PropertyToID("_ConeEvents");
        private static readonly int ClassifiedIndicesId = Shader.PropertyToID("_ClassifiedIndices");
        private static readonly int ClassCountersId = Shader.PropertyToID("_ClassCounters");
        private static readonly int RayLeftId = Shader.PropertyToID("_DepthRayCenterLeft");
        private static readonly int RayRightId = Shader.PropertyToID("_DepthRayCenterRight");
        private static readonly int FilmHeadersId = Shader.PropertyToID("_FilmHeaders");
        private static readonly int FilmInformationId = Shader.PropertyToID("_FilmInformation");
        private static readonly int FrameAccumulatorId = Shader.PropertyToID("_FrameAccumulator");
        private static readonly int FilmDirtyFlagsId = Shader.PropertyToID("_FilmDirtyFlags");
        private static readonly int DirtyFilmIndicesId = Shader.PropertyToID("_DirtyFilmIndices");
        private static readonly int DirtyStateId = Shader.PropertyToID("_DirtyState");
        private static readonly int SolveDispatchArgumentsId = Shader.PropertyToID("_SolveDispatchArguments");
        private static readonly int PhotometricPressuresId =
            Shader.PropertyToID("_PhotometricPressures");
        private static readonly int PhotometricStateId =
            Shader.PropertyToID("_PhotometricState");

        private readonly Matrix4x4[] _chunkFromDepth = new Matrix4x4[2];
        private GraphicsBuffer _frameAccumulator;
        private GraphicsBuffer _filmDirtyFlags;
        private GraphicsBuffer _dirtyFilmIndices;
        private GraphicsBuffer _dirtyState;
        private GraphicsBuffer _solveDispatchArguments;
        private int _initializeKernel = -1;
        private int _clearFrameKernel = -1;
        private int _accumulateKernel = -1;
        private int _contradictionKernel = -1;
        private int _photometricKernel = -1;
        private int _buildSolveArgsKernel = -1;
        private int _solveKernel = -1;
        private bool _running;
        private bool _subscribedToSource;
        private bool _initialized;
        private long _updatedFrames;
        private Matrix4x4 _chunkFromWorld = Matrix4x4.identity;

        public event Action<ConeEventFrameLease> UpdateCompleted;
        public long UpdatedFrames => _updatedFrames;

        public void SetChunkFrame(Matrix4x4 worldFromChunk) =>
            _chunkFromWorld = worldFromChunk.inverse;

        public void StartUpdating(PrismConeClassifier events = null,
            PrismFilmSpawner films = null, bool subscribeToSource = true)
        {
            if (_running) return;
            coneClassifier = events != null ? events : coneClassifier;
            filmSpawner = films != null ? films : filmSpawner;
            coneClassifier ??= GetComponent<PrismConeClassifier>();
            filmSpawner ??= GetComponent<PrismFilmSpawner>();
            updateCompute ??= Resources.Load<ComputeShader>("Prism/ContactFilmUpdate");
            ContactFilmPool pool = filmSpawner?.FilmPool;
            if (coneClassifier == null || pool == null || pool.IsDisposed ||
                updateCompute == null)
            {
                Logger.Error("Cone-PRISM film update dependencies are missing.");
                return;
            }

            _initializeKernel = updateCompute.FindKernel("InitializeFilmUpdateState");
            _clearFrameKernel = updateCompute.FindKernel("ClearFilmUpdateFrame");
            _accumulateKernel = updateCompute.FindKernel("AccumulateMatchedFilms");
            _contradictionKernel = updateCompute.FindKernel("AccumulateBehindEvidence");
            _photometricKernel = updateCompute.FindKernel(
                "AccumulatePhotometricPressure");
            _buildSolveArgsKernel = updateCompute.FindKernel("BuildFilmSolveArguments");
            _solveKernel = updateCompute.FindKernel("SolveDirtyFilms");
            Allocate(pool.Capacity);
            BindPersistent(pool);
            if (!_initialized)
            {
                updateCompute.Dispatch(_initializeKernel, CeilDiv(pool.Capacity, 64), 1, 1);
                _initialized = true;
            }
            if (subscribeToSource)
            {
                filmSpawner.SpawnCompleted += OnConeEvents;
                _subscribedToSource = true;
            }
            _running = true;
        }

        public void StopUpdating()
        {
            if (_subscribedToSource && filmSpawner != null)
                filmSpawner.SpawnCompleted -= OnConeEvents;
            _subscribedToSource = false;
            _running = false;
        }

        private void OnDestroy()
        {
            StopUpdating();
            DisposeBuffers();
        }

        private void OnConeEvents(ConeEventFrameLease eventFrame) =>
            DispatchUpdate(eventFrame);

        internal bool DispatchUpdate(ConeEventFrameLease eventFrame,
            PrismPhotometricRefiner photometric = null)
        {
            ContactFilmPool pool = filmSpawner?.FilmPool;
            if (!_running || eventFrame == null || eventFrame.IsDisposed ||
                pool == null || pool.IsDisposed) return false;
            try
            {
                NormalizedRigFrameLease measured = eventFrame.Source.Source;
                StereoRigFrameLease rig = measured.Source;
                ConeLutLease luts = measured.ConeLuts;
                _chunkFromDepth[0] = _chunkFromWorld * PoseMatrix(
                    rig.DepthLeft.WorldFromCamera);
                _chunkFromDepth[1] = _chunkFromWorld * PoseMatrix(
                    rig.DepthRight.WorldFromCamera);

                BindPersistent(pool);
                updateCompute.SetInt(EventCapacityId, eventFrame.EventCapacity);
                updateCompute.SetFloat(QualityFloorId, geometryQualityFloor);
                updateCompute.SetFloat(MinimumSigmaId, minimumSurfaceSigma);
                updateCompute.SetFloat(MinimumHuberId, minimumHuberDelta);
                updateCompute.SetMatrixArray(ChunkFromDepthId, _chunkFromDepth);
                updateCompute.SetBuffer(_accumulateKernel, EventsId, eventFrame.Events);
                updateCompute.SetBuffer(_accumulateKernel, ClassifiedIndicesId,
                    eventFrame.ClassifiedIndices);
                updateCompute.SetBuffer(_accumulateKernel, ClassCountersId,
                    eventFrame.ClassCounters);
                updateCompute.SetBuffer(_contradictionKernel, EventsId,
                    eventFrame.Events);
                updateCompute.SetBuffer(_contradictionKernel, ClassifiedIndicesId,
                    eventFrame.ClassifiedIndices);
                updateCompute.SetBuffer(_contradictionKernel, ClassCountersId,
                    eventFrame.ClassCounters);
                updateCompute.SetTexture(_accumulateKernel, RayLeftId,
                    luts.DepthLeft.CenterRaySolidAngle);
                updateCompute.SetTexture(_accumulateKernel, RayRightId,
                    luts.DepthRight.CenterRaySolidAngle);

                updateCompute.Dispatch(_clearFrameKernel, 1, 1, 1);
                updateCompute.DispatchIndirect(_accumulateKernel,
                    eventFrame.ClassDispatchArguments, MatchClassDispatchOffset);
                updateCompute.DispatchIndirect(_contradictionKernel,
                    eventFrame.ClassDispatchArguments, BehindClassDispatchOffset);
                if (photometric?.Pressures != null &&
                    photometric.PressureState != null &&
                    photometric.PressureArguments != null)
                {
                    updateCompute.SetInt(PhotometricCapacityId,
                        photometric.PressureCapacity);
                    updateCompute.SetBuffer(_photometricKernel,
                        PhotometricPressuresId, photometric.Pressures);
                    updateCompute.SetBuffer(_photometricKernel,
                        PhotometricStateId, photometric.PressureState);
                    updateCompute.DispatchIndirect(_photometricKernel,
                        photometric.PressureArguments, 0);
                }
                updateCompute.Dispatch(_buildSolveArgsKernel, 1, 1, 1);
                updateCompute.DispatchIndirect(_solveKernel,
                    _solveDispatchArguments, 0);
                _updatedFrames++;
                if (UpdateCompleted != null) UpdateCompleted.Invoke(eventFrame);
                else filmSpawner.NotifyFilmsMutated();
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error($"Cone-PRISM film information update failed: {exception.Message}");
                return false;
            }
        }

        private void Allocate(int capacity)
        {
            if (_frameAccumulator != null &&
                _filmDirtyFlags != null && _filmDirtyFlags.count == capacity) return;
            DisposeBuffers();
            _frameAccumulator = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                checked(capacity * AccumulatorWordsPerFilm), sizeof(int));
            _filmDirtyFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                capacity, sizeof(uint));
            _dirtyFilmIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                capacity, sizeof(uint));
            _dirtyState = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                4, sizeof(uint));
            _solveDispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                1, sizeof(uint) * 3);
            _initialized = false;
        }

        private void BindPersistent(ContactFilmPool pool)
        {
            updateCompute.SetInt(FilmCapacityId, pool.Capacity);
            int[] kernels =
            {
                _initializeKernel, _clearFrameKernel, _accumulateKernel,
                _contradictionKernel, _photometricKernel,
                _buildSolveArgsKernel, _solveKernel
            };
            foreach (int kernel in kernels)
            {
                if (kernel < 0) continue;
                updateCompute.SetBuffer(kernel, FrameAccumulatorId, _frameAccumulator);
                updateCompute.SetBuffer(kernel, FilmDirtyFlagsId, _filmDirtyFlags);
                updateCompute.SetBuffer(kernel, DirtyFilmIndicesId, _dirtyFilmIndices);
                updateCompute.SetBuffer(kernel, DirtyStateId, _dirtyState);
                updateCompute.SetBuffer(kernel, SolveDispatchArgumentsId,
                    _solveDispatchArguments);
            }
            updateCompute.SetBuffer(_accumulateKernel, FilmHeadersId, pool.Headers);
            updateCompute.SetBuffer(_accumulateKernel, FilmInformationId,
                pool.Information);
            updateCompute.SetBuffer(_contradictionKernel, FilmHeadersId, pool.Headers);
            updateCompute.SetBuffer(_contradictionKernel, FilmInformationId,
                pool.Information);
            updateCompute.SetBuffer(_photometricKernel, FilmHeadersId, pool.Headers);
            updateCompute.SetBuffer(_photometricKernel, FilmInformationId,
                pool.Information);
            updateCompute.SetBuffer(_solveKernel, FilmHeadersId, pool.Headers);
            updateCompute.SetBuffer(_solveKernel, FilmInformationId,
                pool.Information);
        }

        private void DisposeBuffers()
        {
            _frameAccumulator?.Dispose();
            _filmDirtyFlags?.Dispose();
            _dirtyFilmIndices?.Dispose();
            _dirtyState?.Dispose();
            _solveDispatchArguments?.Dispose();
            _frameAccumulator = null;
            _filmDirtyFlags = null;
            _dirtyFilmIndices = null;
            _dirtyState = null;
            _solveDispatchArguments = null;
            _initialized = false;
        }

        private static Matrix4x4 PoseMatrix(Pose pose) =>
            Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one);

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);
    }
}
