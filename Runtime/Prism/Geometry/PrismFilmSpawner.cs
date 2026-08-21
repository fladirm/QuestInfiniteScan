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
        [SerializeField, Min(1f)] private float physicalPrecisionUnit = 4096f;

        private static readonly int ResolutionId = Shader.PropertyToID("_Resolution");
        private static readonly int TileResolutionId = Shader.PropertyToID("_TileResolution");
        private static readonly int FilmCapacityId = Shader.PropertyToID("_FilmCapacity");
        private static readonly int SpawnTileCapacityId = Shader.PropertyToID("_SpawnTileCapacity");
        private static readonly int ChunkIdId = Shader.PropertyToID("_ChunkId");
        private static readonly int ChunkFromDepthId = Shader.PropertyToID("_ChunkFromDepth");
        private static readonly int BehindLayerThresholdId = Shader.PropertyToID("_BehindLayerThreshold");
        private static readonly int PrecisionUnitId = Shader.PropertyToID("_PrecisionUnit");
        private static readonly int EventsId = Shader.PropertyToID("_ConeEvents");
        private static readonly int RayLeftId = Shader.PropertyToID("_DepthRayCenterLeft");
        private static readonly int RayRightId = Shader.PropertyToID("_DepthRayCenterRight");
        private static readonly int FilmHeadersId = Shader.PropertyToID("_FilmHeaders");
        private static readonly int FilmInformationId = Shader.PropertyToID("_FilmInformation");
        private static readonly int FilmAllocatorId = Shader.PropertyToID("_FilmAllocator");
        private static readonly int FilmSlotStatesId =
            Shader.PropertyToID("_FilmSlotStates");
        private static readonly int ActiveFilmIndicesId =
            Shader.PropertyToID("_ActiveFilmIndices");
        private static readonly int DirtyFilmIndicesId =
            Shader.PropertyToID("_DirtyFilmIndices");
        private static readonly int SpawnTileIndicesId = Shader.PropertyToID("_SpawnTileIndices");
        private static readonly int SpawnTileStateId = Shader.PropertyToID("_SpawnTileState");
        private static readonly int SpawnTileDispatchArgumentsId =
            Shader.PropertyToID("_SpawnTileDispatchArguments");
        private static readonly int CandidateCapacityId = Shader.PropertyToID("_CandidateCapacity");
        private static readonly int CandidateHashCapacityId = Shader.PropertyToID("_CandidateHashCapacity");
        private static readonly int CandidateHeadersId = Shader.PropertyToID("_CandidateHeaders");
        private static readonly int CandidateInformationId = Shader.PropertyToID("_CandidateInformation");
        private static readonly int CandidateParentsId = Shader.PropertyToID("_CandidateParents");
        private static readonly int CandidateHashHeadsId = Shader.PropertyToID("_CandidateHashHeads");
        private static readonly int CandidateHashNextId = Shader.PropertyToID("_CandidateHashNext");
        private static readonly int CandidateHashKeysId = Shader.PropertyToID("_CandidateHashKeys");
        private static readonly int CandidateRepresentativesId = Shader.PropertyToID("_CandidateRepresentatives");
        private static readonly int CandidateAggregatesId = Shader.PropertyToID("_CandidateAggregates");
        private static readonly int CandidateStateId = Shader.PropertyToID("_CandidateState");
        private static readonly int CandidateDispatchArgumentsId =
            Shader.PropertyToID("_CandidateDispatchArguments");
        private static readonly int ManifoldCapacityId = Shader.PropertyToID("_ManifoldCapacity");
        private static readonly int FrontierCapacityId = Shader.PropertyToID("_FrontierCapacity");
        private static readonly int CalibrationEpochId = Shader.PropertyToID("_CalibrationEpoch");
        private static readonly int OpticalSeedId = Shader.PropertyToID("_OpticalSeed");
        private static readonly int ManifoldHeadersId = Shader.PropertyToID("_ManifoldHeaders");
        private static readonly int FilmMembershipsId = Shader.PropertyToID("_FilmMemberships");
        private static readonly int LatentFrontiersId = Shader.PropertyToID("_LatentFrontiers");
        private static readonly int ManifoldFrontierIncidencesId =
            Shader.PropertyToID("_ManifoldFrontierIncidences");
        private static readonly int ManifoldAllocatorId = Shader.PropertyToID("_ManifoldAllocator");
        private static readonly int CurrentManifoldId = Shader.PropertyToID("_CurrentManifold");
        private static readonly int ManifoldDiagnosticsId = Shader.PropertyToID("_ManifoldDiagnostics");
        private static readonly int CandidatePublicationsId =
            Shader.PropertyToID("_CandidatePublications");
        private static readonly int CandidateHeadersReadId =
            Shader.PropertyToID("_CandidateHeadersRead");
        private static readonly int CandidateInformationReadId =
            Shader.PropertyToID("_CandidateInformationRead");
        private static readonly int CandidateRepresentativesReadId =
            Shader.PropertyToID("_CandidateRepresentativesRead");
        private static readonly int CandidateAggregatesReadId =
            Shader.PropertyToID("_CandidateAggregatesRead");
        private static readonly int CandidatePublicationsReadId =
            Shader.PropertyToID("_CandidatePublicationsRead");
        private static readonly int FilmHeadersReadId =
            Shader.PropertyToID("_FilmHeadersRead");
        private static readonly int ManifoldHeadersReadId =
            Shader.PropertyToID("_ManifoldHeadersRead");
        private static readonly int CurrentManifoldReadId =
            Shader.PropertyToID("_CurrentManifoldRead");

        private readonly Matrix4x4[] _chunkFromDepth = new Matrix4x4[2];
        private ContactFilmPool _filmPool;
        private GraphicsBuffer _spawnTileIndices;
        private GraphicsBuffer _spawnTileState;
        private GraphicsBuffer _spawnTileDispatchArguments;
        private GraphicsBuffer _candidateHeaders;
        private GraphicsBuffer _candidateInformation;
        private GraphicsBuffer _candidateParents;
        private GraphicsBuffer _candidateHashHeads;
        private GraphicsBuffer _candidateHashNext;
        private GraphicsBuffer _candidateHashKeys;
        private GraphicsBuffer _candidateRepresentatives;
        private GraphicsBuffer _candidateAggregates;
        private GraphicsBuffer _candidateState;
        private GraphicsBuffer _candidateDispatchArguments;
        private GraphicsBuffer _candidatePublications;
        private int _candidateHashCapacity;
        private int _spawnTileCapacity;
        private int _clearTilesKernel = -1;
        private int _compactTilesKernel = -1;
        private int _buildTileArgsKernel = -1;
        private int _spawnKernel = -1;
        private int _clearCandidatesKernel = -1;
        private int _buildCandidateArgsKernel = -1;
        private int _buildCandidateHashKernel = -1;
        private int _unionCandidatesKernel = -1;
        private int _compressCandidatesKernel = -1;
        private int _compactRepresentativesKernel = -1;
        private int _buildRepresentativeArgsKernel = -1;
        private int _accumulateRepresentativesKernel = -1;
        private int _stampCandidateSupportKernel = -1;
        private int _ensureManifoldKernel = -1;
        private int _reservePublicationKernel = -1;
        private int _writeCanonicalFilmsKernel = -1;
        private int _writeCanonicalTopologyKernel = -1;
        private int _finalizeManifoldKernel = -1;
        private bool _running;
        private bool _subscribedToSource;
        private long _dispatchedFrames;
        private Matrix4x4 _chunkFromWorld = Matrix4x4.identity;
        private uint _chunkId;

        public event Action<ContactFilmPool> FilmsMutated;
        public event Action<ConeEventFrameLease> SpawnCompleted;
        public ContactFilmPool FilmPool => _filmPool;
        public PressureManifoldPool PressureManifolds => _filmPool?.Manifolds;
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
            _clearCandidatesKernel = spawnCompute.FindKernel("ClearSpawnCandidates");
            _spawnKernel = spawnCompute.FindKernel("EmitSpawnCandidates");
            _buildCandidateArgsKernel =
                spawnCompute.FindKernel("BuildCandidateDispatchArguments");
            _buildCandidateHashKernel = spawnCompute.FindKernel("BuildCandidateHash");
            _unionCandidatesKernel =
                spawnCompute.FindKernel("UnionCompatibleCandidates");
            _compressCandidatesKernel =
                spawnCompute.FindKernel("CompressCandidateParents");
            _compactRepresentativesKernel =
                spawnCompute.FindKernel("CompactCandidateRepresentatives");
            _buildRepresentativeArgsKernel =
                spawnCompute.FindKernel("BuildRepresentativeDispatchArguments");
            _accumulateRepresentativesKernel =
                spawnCompute.FindKernel("AccumulateCandidateRepresentatives");
            _stampCandidateSupportKernel =
                spawnCompute.FindKernel("StampCandidateSupport");
            _ensureManifoldKernel = spawnCompute.FindKernel("EnsurePressureManifold");
            _reservePublicationKernel =
                spawnCompute.FindKernel("ReserveCandidatePublication");
            _writeCanonicalFilmsKernel =
                spawnCompute.FindKernel("WriteCanonicalFilms");
            _writeCanonicalTopologyKernel =
                spawnCompute.FindKernel("WriteCanonicalMembershipAndFrontier");
            _finalizeManifoldKernel =
                spawnCompute.FindKernel("FinalizePressureManifoldCounts");
            _filmPool ??= new ContactFilmPool(filmCapacity);
            EnsureCandidateBuffers(_filmPool.Capacity);
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
            DisposeCandidateBuffers();
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
                spawnCompute.SetInt(CandidateCapacityId, _filmPool.Capacity);
                spawnCompute.SetInt(CandidateHashCapacityId, _candidateHashCapacity);
                spawnCompute.SetInt(ChunkIdId, unchecked((int)_chunkId));
                spawnCompute.SetInt(CalibrationEpochId,
                    unchecked((int)rig.CalibrationEpoch));
                spawnCompute.SetFloat(BehindLayerThresholdId, behindLayerThreshold);
                spawnCompute.SetFloat(PrecisionUnitId, physicalPrecisionUnit);
                spawnCompute.SetMatrixArray(ChunkFromDepthId, _chunkFromDepth);
                Vector4 opticalSeed = _chunkFromDepth[0].GetColumn(3);
                spawnCompute.SetVector(OpticalSeedId, opticalSeed);
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

                int[] candidateKernels =
                {
                    _clearCandidatesKernel, _spawnKernel,
                    _buildCandidateArgsKernel, _buildCandidateHashKernel,
                    _unionCandidatesKernel, _compressCandidatesKernel,
                    _compactRepresentativesKernel, _buildRepresentativeArgsKernel,
                    _accumulateRepresentativesKernel, _stampCandidateSupportKernel,
                    _ensureManifoldKernel, _reservePublicationKernel,
                    _writeCanonicalFilmsKernel,
                    _writeCanonicalTopologyKernel,
                    _finalizeManifoldKernel
                };
                PressureManifoldPool manifolds = _filmPool.Manifolds;
                foreach (int kernel in candidateKernels)
                {
                    spawnCompute.SetBuffer(kernel, CandidateHeadersId,
                        _candidateHeaders);
                    spawnCompute.SetBuffer(kernel, CandidateInformationId,
                        _candidateInformation);
                    spawnCompute.SetBuffer(kernel, CandidateParentsId,
                        _candidateParents);
                    spawnCompute.SetBuffer(kernel, CandidateHashHeadsId,
                        _candidateHashHeads);
                    spawnCompute.SetBuffer(kernel, CandidateHashNextId,
                        _candidateHashNext);
                    spawnCompute.SetBuffer(kernel, CandidateHashKeysId,
                        _candidateHashKeys);
                    spawnCompute.SetBuffer(kernel, CandidateRepresentativesId,
                        _candidateRepresentatives);
                    spawnCompute.SetBuffer(kernel, CandidateAggregatesId,
                        _candidateAggregates);
                    spawnCompute.SetBuffer(kernel, CandidateStateId,
                        _candidateState);
                    spawnCompute.SetBuffer(kernel, CandidateDispatchArgumentsId,
                        _candidateDispatchArguments);
                    spawnCompute.SetBuffer(kernel, FilmHeadersId, _filmPool.Headers);
                    spawnCompute.SetBuffer(kernel, FilmInformationId,
                        _filmPool.Information);
                    spawnCompute.SetBuffer(kernel, FilmAllocatorId,
                        _filmPool.Allocator);
                    spawnCompute.SetBuffer(kernel, FilmSlotStatesId,
                        _filmPool.SlotStates);
                    spawnCompute.SetBuffer(kernel, ActiveFilmIndicesId,
                        _filmPool.ActiveIndices);
                    spawnCompute.SetBuffer(kernel, DirtyFilmIndicesId,
                        _filmPool.DirtyIndices);
                    spawnCompute.SetBuffer(kernel, ManifoldHeadersId,
                        manifolds.Headers);
                    spawnCompute.SetBuffer(kernel, FilmMembershipsId,
                        manifolds.Memberships);
                    spawnCompute.SetBuffer(kernel, LatentFrontiersId,
                        manifolds.Frontiers);
                    spawnCompute.SetBuffer(kernel, ManifoldFrontierIncidencesId,
                        manifolds.FrontierIncidences);
                    spawnCompute.SetBuffer(kernel, ManifoldAllocatorId,
                        manifolds.Allocator);
                    spawnCompute.SetBuffer(kernel, CurrentManifoldId,
                        manifolds.Current);
                    spawnCompute.SetBuffer(kernel, ManifoldDiagnosticsId,
                        manifolds.Diagnostics);
                    spawnCompute.SetBuffer(kernel, CandidatePublicationsId,
                        _candidatePublications);
                }
                int[] publicationReaders =
                {
                    _reservePublicationKernel,
                    _writeCanonicalFilmsKernel, _writeCanonicalTopologyKernel
                };
                foreach (int kernel in publicationReaders)
                {
                    spawnCompute.SetBuffer(kernel, CandidateHeadersReadId,
                        _candidateHeaders);
                    spawnCompute.SetBuffer(kernel, CandidateInformationReadId,
                        _candidateInformation);
                    spawnCompute.SetBuffer(kernel, CandidateRepresentativesReadId,
                        _candidateRepresentatives);
                    spawnCompute.SetBuffer(kernel, CandidateAggregatesReadId,
                        _candidateAggregates);
                    spawnCompute.SetBuffer(kernel, CandidatePublicationsReadId,
                        _candidatePublications);
                    spawnCompute.SetBuffer(kernel, FilmHeadersReadId,
                        _filmPool.Headers);
                    spawnCompute.SetBuffer(kernel, ManifoldHeadersReadId,
                        manifolds.Headers);
                    spawnCompute.SetBuffer(kernel, CurrentManifoldReadId,
                        manifolds.Current);
                }
                spawnCompute.SetInt(ManifoldCapacityId, manifolds.ManifoldCapacity);
                spawnCompute.SetInt(FrontierCapacityId, manifolds.FrontierCapacity);
                spawnCompute.Dispatch(_clearTilesKernel, 1, 1, 1);
                spawnCompute.Dispatch(_clearCandidatesKernel,
                    CeilDiv(Math.Max(_filmPool.Capacity, _candidateHashCapacity), 64),
                    1, 1);
                spawnCompute.Dispatch(_compactTilesKernel, tileResolution.x,
                    tileResolution.y, 2);
                spawnCompute.Dispatch(_buildTileArgsKernel, 1, 1, 1);
                spawnCompute.DispatchIndirect(_spawnKernel,
                    _spawnTileDispatchArguments, 0);
                spawnCompute.Dispatch(_buildCandidateArgsKernel, 1, 1, 1);
                spawnCompute.DispatchIndirect(_buildCandidateHashKernel,
                    _candidateDispatchArguments, 0);
                // DSU unions are order-independent; two relaxation waves plus path
                // compression converge chains crossing tile and eye boundaries.
                for (int wave = 0; wave < 2; wave++)
                {
                    spawnCompute.DispatchIndirect(_unionCandidatesKernel,
                        _candidateDispatchArguments, 0);
                    spawnCompute.DispatchIndirect(_compressCandidatesKernel,
                        _candidateDispatchArguments, 0);
                }
                spawnCompute.DispatchIndirect(_compactRepresentativesKernel,
                    _candidateDispatchArguments, 0);
                spawnCompute.Dispatch(_buildRepresentativeArgsKernel, 1, 1, 1);
                spawnCompute.DispatchIndirect(_accumulateRepresentativesKernel,
                    _candidateDispatchArguments, sizeof(uint) * 6);
                spawnCompute.DispatchIndirect(_stampCandidateSupportKernel,
                    _candidateDispatchArguments, sizeof(uint) * 6);
                spawnCompute.Dispatch(_ensureManifoldKernel, 1, 1, 1);
                spawnCompute.Dispatch(_reservePublicationKernel, 1, 1, 1);
                spawnCompute.DispatchIndirect(_writeCanonicalFilmsKernel,
                    _candidateDispatchArguments, sizeof(uint) * 3);
                spawnCompute.DispatchIndirect(_writeCanonicalTopologyKernel,
                    _candidateDispatchArguments, sizeof(uint) * 3);
                spawnCompute.Dispatch(_finalizeManifoldKernel, 1, 1, 1);
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

        private void EnsureCandidateBuffers(int capacity)
        {
            if (_candidateHeaders != null && _candidateHeaders.count == capacity)
                return;
            DisposeCandidateBuffers();
            _candidateHashCapacity = NextPowerOfTwo(checked(capacity * 2));
            _candidateHeaders = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                capacity, ContactFilmHeaderGpu.Stride);
            _candidateInformation = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                checked(capacity * ContactFilmPool.InformationRecords),
                sizeof(float) * 4);
            _candidateParents = UIntBuffer(capacity);
            _candidateHashHeads = UIntBuffer(_candidateHashCapacity);
            _candidateHashNext = UIntBuffer(capacity);
            _candidateHashKeys = UIntBuffer(capacity);
            _candidateRepresentatives = UIntBuffer(capacity);
            _candidateAggregates = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                checked(capacity * 4), sizeof(int) * 4);
            _candidateState = UIntBuffer(8);
            _candidateDispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 4, sizeof(uint) * 3);
            _candidatePublications = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, capacity, sizeof(uint) * 5);
        }

        private static GraphicsBuffer UIntBuffer(int count) =>
            new(GraphicsBuffer.Target.Structured, Math.Max(1, count), sizeof(uint));

        private void DisposeCandidateBuffers()
        {
            _candidateHeaders?.Dispose();
            _candidateInformation?.Dispose();
            _candidateParents?.Dispose();
            _candidateHashHeads?.Dispose();
            _candidateHashNext?.Dispose();
            _candidateHashKeys?.Dispose();
            _candidateRepresentatives?.Dispose();
            _candidateAggregates?.Dispose();
            _candidateState?.Dispose();
            _candidateDispatchArguments?.Dispose();
            _candidatePublications?.Dispose();
            _candidateHeaders = null;
            _candidateInformation = null;
            _candidateParents = null;
            _candidateHashHeads = null;
            _candidateHashNext = null;
            _candidateHashKeys = null;
            _candidateRepresentatives = null;
            _candidateAggregates = null;
            _candidateState = null;
            _candidateDispatchArguments = null;
            _candidatePublications = null;
            _candidateHashCapacity = 0;
        }

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value && result < 1 << 30) result <<= 1;
            return result;
        }
    }
}
