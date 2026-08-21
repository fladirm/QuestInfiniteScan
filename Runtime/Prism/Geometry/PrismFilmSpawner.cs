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
        private static readonly int TileResolutionId = Shader.PropertyToID("_TileResolution");
        private static readonly int FilmCapacityId = Shader.PropertyToID("_FilmCapacity");
        private static readonly int SpawnTileCapacityId = Shader.PropertyToID("_SpawnTileCapacity");
        private static readonly int ChunkIdId = Shader.PropertyToID("_ChunkId");
        private static readonly int ChunkFromDepthId = Shader.PropertyToID("_ChunkFromDepth");
        private static readonly int BehindLayerThresholdId = Shader.PropertyToID("_BehindLayerThreshold");
        private static readonly int EventsId = Shader.PropertyToID("_ConeEvents");
        private static readonly int RayLeftId = Shader.PropertyToID("_DepthRayCenterLeft");
        private static readonly int RayRightId = Shader.PropertyToID("_DepthRayCenterRight");
        private static readonly int FilmHeadersId = Shader.PropertyToID("_FilmHeaders");
        private static readonly int FilmInformationId = Shader.PropertyToID("_FilmInformation");
        private static readonly int FilmAllocatorId = Shader.PropertyToID("_FilmAllocator");
        private static readonly int SpawnTileIndicesId = Shader.PropertyToID("_SpawnTileIndices");
        private static readonly int SpawnTileStateId = Shader.PropertyToID("_SpawnTileState");
        private static readonly int SpawnTileDispatchArgumentsId =
            Shader.PropertyToID("_SpawnTileDispatchArguments");

        private readonly Matrix4x4[] _chunkFromDepth = new Matrix4x4[2];
        private ContactFilmPool _filmPool;
        private GraphicsBuffer _spawnTileIndices;
        private GraphicsBuffer _spawnTileState;
        private GraphicsBuffer _spawnTileDispatchArguments;
        private int _spawnTileCapacity;
        private int _clearTilesKernel = -1;
        private int _compactTilesKernel = -1;
        private int _buildTileArgsKernel = -1;
        private int _spawnKernel = -1;
        private bool _running;
        private bool _subscribedToSource;
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

        public void StartSpawning(PrismConeClassifier source = null,
            bool subscribeToSource = true)
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
            _clearTilesKernel = spawnCompute.FindKernel("ClearSpawnTileState");
            _compactTilesKernel = spawnCompute.FindKernel("CompactSpawnTiles");
            _buildTileArgsKernel =
                spawnCompute.FindKernel("BuildSpawnTileDispatchArguments");
            _spawnKernel = spawnCompute.FindKernel("SpawnContactFilms");
            _filmPool ??= new ContactFilmPool(filmCapacity);
            if (subscribeToSource)
            {
                coneClassifier.EventsReady += OnConeEvents;
                _subscribedToSource = true;
            }
            _running = true;
        }

        public void StopSpawning()
        {
            if (_subscribedToSource && coneClassifier != null)
                coneClassifier.EventsReady -= OnConeEvents;
            _subscribedToSource = false;
            _running = false;
        }

        private void OnDestroy()
        {
            StopSpawning();
            DisposeTileBuffers();
            _filmPool?.Dispose();
            _filmPool = null;
        }

        private void OnConeEvents(ConeEventFrameLease eventFrame) =>
            DispatchSpawn(eventFrame);

        internal bool DispatchSpawn(ConeEventFrameLease eventFrame)
        {
            if (!_running || eventFrame == null || eventFrame.IsDisposed ||
                _filmPool == null || _filmPool.IsDisposed) return false;
            try
            {
                NormalizedRigFrameLease measured = eventFrame.Source.Source;
                StereoRigFrameLease rig = measured.Source;
                ConeLutLease luts = measured.ConeLuts;
                Vector2Int resolution = rig.DepthResolution;
                Vector2Int tileResolution = new(CeilDiv(resolution.x, 8),
                    CeilDiv(resolution.y, 8));
                EnsureTileBuffers(checked(tileResolution.x * tileResolution.y * 2));
                _chunkFromDepth[0] = _chunkFromWorld * PoseMatrix(
                    rig.DepthLeft.WorldFromCamera);
                _chunkFromDepth[1] = _chunkFromWorld * PoseMatrix(
                    rig.DepthRight.WorldFromCamera);

                spawnCompute.SetInts(ResolutionId, resolution.x, resolution.y);
                spawnCompute.SetInts(TileResolutionId, tileResolution.x,
                    tileResolution.y);
                spawnCompute.SetInt(FilmCapacityId, _filmPool.Capacity);
                spawnCompute.SetInt(SpawnTileCapacityId, _spawnTileCapacity);
                spawnCompute.SetInt(ChunkIdId, unchecked((int)_chunkId));
                spawnCompute.SetFloat(BehindLayerThresholdId, behindLayerThreshold);
                spawnCompute.SetMatrixArray(ChunkFromDepthId, _chunkFromDepth);
                int[] eventKernels = { _compactTilesKernel, _spawnKernel };
                foreach (int kernel in eventKernels)
                {
                    spawnCompute.SetBuffer(kernel, EventsId, eventFrame.Events);
                    spawnCompute.SetBuffer(kernel, FilmHeadersId, _filmPool.Headers);
                    spawnCompute.SetBuffer(kernel, SpawnTileIndicesId,
                        _spawnTileIndices);
                    spawnCompute.SetBuffer(kernel, SpawnTileStateId,
                        _spawnTileState);
                    spawnCompute.SetBuffer(kernel, SpawnTileDispatchArgumentsId,
                        _spawnTileDispatchArguments);
                }
                int[] controlKernels =
                {
                    _clearTilesKernel, _buildTileArgsKernel
                };
                foreach (int kernel in controlKernels)
                {
                    spawnCompute.SetBuffer(kernel, SpawnTileIndicesId,
                        _spawnTileIndices);
                    spawnCompute.SetBuffer(kernel, SpawnTileStateId,
                        _spawnTileState);
                    spawnCompute.SetBuffer(kernel, SpawnTileDispatchArgumentsId,
                        _spawnTileDispatchArguments);
                }
                spawnCompute.SetTexture(_spawnKernel, RayLeftId,
                    luts.DepthLeft.CenterRaySolidAngle);
                spawnCompute.SetTexture(_spawnKernel, RayRightId,
                    luts.DepthRight.CenterRaySolidAngle);
                spawnCompute.SetBuffer(_spawnKernel, FilmInformationId,
                    _filmPool.Information);
                spawnCompute.SetBuffer(_spawnKernel, FilmAllocatorId,
                    _filmPool.Allocator);
                spawnCompute.Dispatch(_clearTilesKernel, 1, 1, 1);
                spawnCompute.Dispatch(_compactTilesKernel, tileResolution.x,
                    tileResolution.y, 2);
                spawnCompute.Dispatch(_buildTileArgsKernel, 1, 1, 1);
                spawnCompute.DispatchIndirect(_spawnKernel,
                    _spawnTileDispatchArguments, 0);
                _dispatchedFrames++;
                // The updater consumes this callback in-order and publishes one
                // combined spawn+refine mesh generation.  Standalone spawning still
                // publishes directly when no downstream information solver exists.
                if (SpawnCompleted != null) SpawnCompleted.Invoke(eventFrame);
                else NotifyFilmsMutated();
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error($"Cone-PRISM ContactFilm spawn failed: {exception.Message}");
                return false;
            }
        }

        private static Matrix4x4 PoseMatrix(Pose pose) =>
            Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one);

        private void EnsureTileBuffers(int capacity)
        {
            if (_spawnTileIndices != null && _spawnTileCapacity == capacity) return;
            DisposeTileBuffers();
            _spawnTileCapacity = Math.Max(1, capacity);
            _spawnTileIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                _spawnTileCapacity, sizeof(uint));
            _spawnTileState = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                4, sizeof(uint));
            _spawnTileDispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 1, sizeof(uint) * 3);
        }

        private void DisposeTileBuffers()
        {
            _spawnTileIndices?.Dispose();
            _spawnTileState?.Dispose();
            _spawnTileDispatchArguments?.Dispose();
            _spawnTileIndices = null;
            _spawnTileState = null;
            _spawnTileDispatchArguments = null;
            _spawnTileCapacity = 0;
        }

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);
    }
}
