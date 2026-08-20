using System;
using UnityEngine;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// GPU-only tile component clustering and robust quadratic ContactFilm spawn.
    /// Canonical allocation is bounded; overflow records evidence and never corrupts
    /// already-published films.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10)]
    public sealed class PrismFilmSpawner : MonoBehaviour
    {
        [SerializeField] private PrismConeClassifier coneClassifier;
        [SerializeField] private ComputeShader spawnCompute;
        [SerializeField, Min(1024)] private int filmCapacity = 65536;
        [SerializeField, Range(0.1f, 1f)] private float behindLayerThreshold = 0.6f;

        private static readonly int ResolutionId = Shader.PropertyToID("_Resolution");
        private static readonly int FilmCapacityId = Shader.PropertyToID("_FilmCapacity");
        private static readonly int ChunkIdId = Shader.PropertyToID("_ChunkId");
        private static readonly int ChunkFromDepthId = Shader.PropertyToID("_ChunkFromDepth");
        private static readonly int BehindLayerThresholdId = Shader.PropertyToID("_BehindLayerThreshold");
        private static readonly int EventsId = Shader.PropertyToID("_ConeEvents");
        private static readonly int RayLeftId = Shader.PropertyToID("_DepthRayCenterLeft");
        private static readonly int RayRightId = Shader.PropertyToID("_DepthRayCenterRight");
        private static readonly int FilmHeadersId = Shader.PropertyToID("_FilmHeaders");
        private static readonly int FilmInformationId = Shader.PropertyToID("_FilmInformation");
        private static readonly int FilmAllocatorId = Shader.PropertyToID("_FilmAllocator");

        private readonly Matrix4x4[] _chunkFromDepth = new Matrix4x4[2];
        private ContactFilmPool _filmPool;
        private int _spawnKernel = -1;
        private bool _running;
        private long _dispatchedFrames;
        private Matrix4x4 _chunkFromWorld = Matrix4x4.identity;
        private uint _chunkId;

        public event Action<ContactFilmPool> FilmsMutated;
        public event Action<ConeEventFrameLease> SpawnCompleted;
        public ContactFilmPool FilmPool => _filmPool;
        public long DispatchedFrames => _dispatchedFrames;

        internal void NotifyFilmsMutated() => FilmsMutated?.Invoke(_filmPool);

        public void SetChunkFrame(uint chunkId, Matrix4x4 worldFromChunk)
        {
            _chunkId = chunkId;
            _chunkFromWorld = worldFromChunk.inverse;
        }

        public void StartSpawning(PrismConeClassifier source = null)
        {
            if (_running) return;
            coneClassifier = source != null ? source : coneClassifier;
            coneClassifier ??= GetComponent<PrismConeClassifier>();
            spawnCompute ??= Resources.Load<ComputeShader>("Prism/ContactFilmSpawn");
            if (coneClassifier == null || spawnCompute == null)
            {
                Logger.Error("Cone-PRISM ContactFilm spawn dependencies are missing.");
                return;
            }
            _spawnKernel = spawnCompute.FindKernel("SpawnContactFilms");
            _filmPool ??= new ContactFilmPool(filmCapacity);
            coneClassifier.EventsReady += OnConeEvents;
            _running = true;
        }

        public void StopSpawning()
        {
            if (_running && coneClassifier != null)
                coneClassifier.EventsReady -= OnConeEvents;
            _running = false;
        }

        private void OnDestroy()
        {
            StopSpawning();
            _filmPool?.Dispose();
            _filmPool = null;
        }

        private void OnConeEvents(ConeEventFrameLease eventFrame)
        {
            if (!_running || eventFrame == null || eventFrame.IsDisposed ||
                _filmPool == null || _filmPool.IsDisposed) return;
            try
            {
                NormalizedRigFrameLease measured = eventFrame.Source.Source;
                StereoRigFrameLease rig = measured.Source;
                ConeLutLease luts = measured.ConeLuts;
                Vector2Int resolution = rig.DepthLeft.Resolution;
                _chunkFromDepth[0] = _chunkFromWorld * PoseMatrix(
                    rig.DepthLeft.WorldFromCamera);
                _chunkFromDepth[1] = _chunkFromWorld * PoseMatrix(
                    rig.DepthRight.WorldFromCamera);

                spawnCompute.SetInts(ResolutionId, resolution.x, resolution.y);
                spawnCompute.SetInt(FilmCapacityId, _filmPool.Capacity);
                spawnCompute.SetInt(ChunkIdId, unchecked((int)_chunkId));
                spawnCompute.SetFloat(BehindLayerThresholdId, behindLayerThreshold);
                spawnCompute.SetMatrixArray(ChunkFromDepthId, _chunkFromDepth);
                spawnCompute.SetBuffer(_spawnKernel, EventsId, eventFrame.Events);
                spawnCompute.SetTexture(_spawnKernel, RayLeftId,
                    luts.DepthLeft.CenterRaySolidAngle);
                spawnCompute.SetTexture(_spawnKernel, RayRightId,
                    luts.DepthRight.CenterRaySolidAngle);
                spawnCompute.SetBuffer(_spawnKernel, FilmHeadersId, _filmPool.Headers);
                spawnCompute.SetBuffer(_spawnKernel, FilmInformationId,
                    _filmPool.Information);
                spawnCompute.SetBuffer(_spawnKernel, FilmAllocatorId,
                    _filmPool.Allocator);
                spawnCompute.Dispatch(_spawnKernel, CeilDiv(resolution.x, 8),
                    CeilDiv(resolution.y, 8), 2);
                _dispatchedFrames++;
                // The updater consumes this callback in-order and publishes one
                // combined spawn+refine mesh generation.  Standalone spawning still
                // publishes directly when no downstream information solver exists.
                if (SpawnCompleted != null) SpawnCompleted.Invoke(eventFrame);
                else NotifyFilmsMutated();
            }
            catch (Exception exception)
            {
                Logger.Error($"Cone-PRISM ContactFilm spawn failed: {exception.Message}");
            }
        }

        private static Matrix4x4 PoseMatrix(Pose pose) =>
            Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one);

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);
    }
}
