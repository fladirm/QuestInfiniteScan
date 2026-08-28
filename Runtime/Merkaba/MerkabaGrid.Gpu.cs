using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    internal readonly struct MerkabaResidencyFrame
    {
        public readonly int IntegrationChunkCount;
        public readonly int VisibleChunkCount;

        public MerkabaResidencyFrame(int integrationChunkCount, int visibleChunkCount)
        {
            IntegrationChunkCount = integrationChunkCount;
            VisibleChunkCount = visibleChunkCount;
        }
    }

    public sealed partial class MerkabaGrid
    {
        [Header("GPU Residency")]
        [SerializeField, Range(16, 192)] private int maxResidentChunks = 96;
        [SerializeField, Range(8, 96)] private int maxIntegrationChunks = 48;
        [SerializeField, Range(8, 128)] private int maxVisibleChunks = 64;

        private sealed class ResidentPage
        {
            public readonly int3 Coord;
            public MerkabaChunk Chunk;
            public readonly int Slot;
            public int LastTouchedFrame;
            public bool PendingEviction;

            public ResidentPage(int3 coord, MerkabaChunk chunk, int slot, int frame)
            {
                Coord = coord;
                Chunk = chunk;
                Slot = slot;
                LastTouchedFrame = frame;
            }
        }

        private readonly struct ChunkCandidate
        {
            public readonly int3 Coord;
            public readonly float DistanceSquared;

            public ChunkCandidate(int3 coord, float distanceSquared)
            {
                Coord = coord;
                DistanceSquared = distanceSquared;
            }
        }

        private readonly Dictionary<int3, ResidentPage> _resident = new();
        private readonly SortedSet<int> _freeSlots = new();
        private readonly HashSet<int3> _desiredCoords = new();
        private ResidentPage[] _slots;
        private int _gpuGeneration;
        private bool _gpuReady;

        private ComputeBuffer _kernelBuffer;
        private ComputeBuffer _pageCoordsBuffer;
        private ComputeBuffer _pageNeighboursBuffer;
        private ComputeBuffer _kernelDirtyBuffer;
        private ComputeBuffer _topologyMaskBuffer;
        private ComputeBuffer _integrationSlotsBuffer;
        private ComputeBuffer _integrationEnabledBuffer;
        private ComputeBuffer _visibleSlotsBuffer;
        private ComputeBuffer _pageHashBuffer;
        private ComputeBuffer _surfaceCandidateBitsBuffer;
        private ComputeBuffer _surfaceQueueBuffer;
        private ComputeBuffer _surfaceCountBuffer;
        private ComputeBuffer _carveListedBitsBuffer;
        private ComputeBuffer _carveLocalIndicesBuffer;
        private ComputeBuffer _carveCountsBuffer;
        private ComputeBuffer _carveQueueBuffer;
        private ComputeBuffer _carveCountBuffer;
        private ComputeBuffer _surfaceDispatchArgsBuffer;
        private ComputeBuffer _carveDispatchArgsBuffer;

        private int4[] _pageCoordsCpu;
        private int[] _pageNeighboursCpu;
        private int[] _integrationSlotsCpu;
        private uint[] _integrationEnabledCpu;
        private int[] _visibleSlotsCpu;
        private uint[] _dirtyOnes;
        private uint[] _zeroMasks;
        private KernelState[] _zeroStates;
        private uint[] _zeroPageBits;
        private uint[] _pageCarveBits;
        private uint[] _pageCarveIndices;
        private readonly uint[] _singleZero = { 0u };
        private int4[] _pageHashCpu;

        private const int PageHashCapacity = 256;
        private const int WordsPerPage = MerkabaConstants.KernelsPerChunk / 32;

        internal ComputeBuffer KernelBuffer => _kernelBuffer;
        internal ComputeBuffer PageCoordsBuffer => _pageCoordsBuffer;
        internal ComputeBuffer PageNeighboursBuffer => _pageNeighboursBuffer;
        internal ComputeBuffer KernelDirtyBuffer => _kernelDirtyBuffer;
        internal ComputeBuffer TopologyMaskBuffer => _topologyMaskBuffer;
        internal ComputeBuffer IntegrationSlotsBuffer => _integrationSlotsBuffer;
        internal ComputeBuffer IntegrationEnabledBuffer => _integrationEnabledBuffer;
        internal ComputeBuffer VisibleSlotsBuffer => _visibleSlotsBuffer;
        internal ComputeBuffer PageHashBuffer => _pageHashBuffer;
        internal ComputeBuffer SurfaceCandidateBitsBuffer => _surfaceCandidateBitsBuffer;
        internal ComputeBuffer SurfaceQueueBuffer => _surfaceQueueBuffer;
        internal ComputeBuffer SurfaceCountBuffer => _surfaceCountBuffer;
        internal ComputeBuffer CarveListedBitsBuffer => _carveListedBitsBuffer;
        internal ComputeBuffer CarveLocalIndicesBuffer => _carveLocalIndicesBuffer;
        internal ComputeBuffer CarveCountsBuffer => _carveCountsBuffer;
        internal ComputeBuffer CarveQueueBuffer => _carveQueueBuffer;
        internal ComputeBuffer CarveCountBuffer => _carveCountBuffer;
        internal ComputeBuffer SurfaceDispatchArgsBuffer => _surfaceDispatchArgsBuffer;
        internal ComputeBuffer CarveDispatchArgsBuffer => _carveDispatchArgsBuffer;
        internal int IntegrationWorkCapacity => maxIntegrationChunks *
                                                MerkabaConstants.KernelsPerChunk;
        internal int PageHashEntryCount => PageHashCapacity;
        internal int ResidentPageCount => _resident.Count;
        internal int TransientResidentPageCount
        {
            get
            {
                int count = 0;
                foreach (ResidentPage page in _resident.Values)
                    if (page.Chunk == null) count++;
                return count;
            }
        }
        internal int IntegrationChunkCount { get; private set; }
        internal int VisibleChunkCount { get; private set; }
        internal int MaxVisibleChunks => maxVisibleChunks;
        internal Matrix4x4 GridToWorldMatrix => transform.localToWorldMatrix;
        internal bool GpuReady => _gpuReady;

        internal void EnsureGpuResources()
        {
            if (_gpuReady) return;
            maxIntegrationChunks = Mathf.Min(maxIntegrationChunks, maxResidentChunks);
            maxVisibleChunks = Mathf.Min(maxVisibleChunks, maxResidentChunks);
            int totalKernels = checked(maxResidentChunks * MerkabaConstants.KernelsPerChunk);
            int stateStride = Marshal.SizeOf<KernelState>();
            if (stateStride != 16)
                throw new InvalidOperationException(
                    $"KernelState GPU ABI must be 16 bytes, got {stateStride}.");

            _kernelBuffer = new ComputeBuffer(totalKernels, stateStride,
                ComputeBufferType.Structured);
            _pageCoordsBuffer = new ComputeBuffer(maxResidentChunks, sizeof(int) * 4,
                ComputeBufferType.Structured);
            _pageNeighboursBuffer = new ComputeBuffer(maxResidentChunks * 27, sizeof(int),
                ComputeBufferType.Structured);
            _kernelDirtyBuffer = new ComputeBuffer(totalKernels, sizeof(uint),
                ComputeBufferType.Structured);
            _topologyMaskBuffer = new ComputeBuffer(totalKernels, sizeof(uint),
                ComputeBufferType.Structured);
            _integrationSlotsBuffer = new ComputeBuffer(maxIntegrationChunks, sizeof(int),
                ComputeBufferType.Structured);
            _integrationEnabledBuffer = new ComputeBuffer(maxResidentChunks, sizeof(uint),
                ComputeBufferType.Structured);
            _visibleSlotsBuffer = new ComputeBuffer(maxVisibleChunks, sizeof(int),
                ComputeBufferType.Structured);
            _pageHashBuffer = new ComputeBuffer(PageHashCapacity, sizeof(int) * 4,
                ComputeBufferType.Structured);
            int totalWords = totalKernels / 32;
            int workCapacity = IntegrationWorkCapacity;
            _surfaceCandidateBitsBuffer = new ComputeBuffer(totalWords, sizeof(uint),
                ComputeBufferType.Structured);
            _surfaceQueueBuffer = new ComputeBuffer(workCapacity, sizeof(uint),
                ComputeBufferType.Structured);
            _surfaceCountBuffer = new ComputeBuffer(1, sizeof(uint),
                ComputeBufferType.Structured);
            _carveListedBitsBuffer = new ComputeBuffer(totalWords, sizeof(uint),
                ComputeBufferType.Structured);
            _carveLocalIndicesBuffer = new ComputeBuffer(totalKernels, sizeof(uint),
                ComputeBufferType.Structured);
            _carveCountsBuffer = new ComputeBuffer(maxResidentChunks, sizeof(uint),
                ComputeBufferType.Structured);
            _carveQueueBuffer = new ComputeBuffer(workCapacity, sizeof(uint),
                ComputeBufferType.Structured);
            _carveCountBuffer = new ComputeBuffer(1, sizeof(uint),
                ComputeBufferType.Structured);
            _surfaceDispatchArgsBuffer = new ComputeBuffer(3, sizeof(uint),
                ComputeBufferType.IndirectArguments);
            _carveDispatchArgsBuffer = new ComputeBuffer(3, sizeof(uint),
                ComputeBufferType.IndirectArguments);

            _slots = new ResidentPage[maxResidentChunks];
            _pageCoordsCpu = new int4[maxResidentChunks];
            _pageNeighboursCpu = new int[maxResidentChunks * 27];
            _integrationSlotsCpu = new int[maxIntegrationChunks];
            _integrationEnabledCpu = new uint[maxResidentChunks];
            _visibleSlotsCpu = new int[maxVisibleChunks];
            _dirtyOnes = new uint[MerkabaConstants.KernelsPerChunk];
            _zeroMasks = new uint[MerkabaConstants.KernelsPerChunk];
            _zeroStates = new KernelState[MerkabaConstants.KernelsPerChunk];
            _zeroPageBits = new uint[WordsPerPage];
            _pageCarveBits = new uint[WordsPerPage];
            _pageCarveIndices = new uint[MerkabaConstants.KernelsPerChunk];
            _pageHashCpu = new int4[PageHashCapacity];
            Array.Fill(_dirtyOnes, 1u);
            Array.Fill(_pageNeighboursCpu, -1);
            Array.Fill(_pageHashCpu, new int4(0, 0, 0, -1));
            for (int slot = 0; slot < maxResidentChunks; slot++)
            {
                _freeSlots.Add(slot);
                _pageCoordsCpu[slot] = new int4(0, 0, 0, -1);
            }
            _pageCoordsBuffer.SetData(_pageCoordsCpu);
            _pageNeighboursBuffer.SetData(_pageNeighboursCpu);
            _pageHashBuffer.SetData(_pageHashCpu);
            _integrationEnabledBuffer.SetData(_integrationEnabledCpu);
            _surfaceCandidateBitsBuffer.SetData(new uint[totalWords]);
            _surfaceCountBuffer.SetData(_singleZero);
            _carveListedBitsBuffer.SetData(new uint[totalWords]);
            _carveCountsBuffer.SetData(new uint[maxResidentChunks]);
            _carveCountBuffer.SetData(_singleZero);
            _surfaceDispatchArgsBuffer.SetData(new uint[] { 0, 1, 1 });
            _carveDispatchArgsBuffer.SetData(new uint[] { 0, 1, 1 });
            _gpuReady = true;
        }

        internal void BeginIntegrationWorkFrame()
        {
            _surfaceCountBuffer.SetData(_singleZero);
            _carveCountBuffer.SetData(_singleZero);
        }

        /// <summary>
        /// Builds a bounded current-frustum working set. Candidate work depends only on
        /// the present camera/frustum and resident cap, never on all historical chunks.
        /// </summary>
        internal MerkabaResidencyFrame RefreshResidency(Camera camera, float maxDistance,
            bool allocateForIntegration)
        {
            EnsureGpuResources();
            if (camera == null || maxDistance <= 0f)
            {
                SetFrameSlots(Array.Empty<int>(), Array.Empty<int>());
                return default;
            }

            List<ChunkCandidate> candidates = CollectFrustumCandidates(camera, maxDistance);
            _desiredCoords.Clear();
            int desiredLimit = Mathf.Min(candidates.Count,
                Mathf.Max(maxIntegrationChunks, maxVisibleChunks));
            for (int i = 0; i < desiredLimit; i++)
                _desiredCoords.Add(candidates[i].Coord);

            var changed = new List<int3>();
            var integration = new List<int>(maxIntegrationChunks);
            var visible = new List<int>(maxVisibleChunks);

            for (int i = 0; i < candidates.Count &&
                 (integration.Count < maxIntegrationChunks ||
                  visible.Count < maxVisibleChunks); i++)
            {
                int3 coord = candidates[i].Coord;
                bool chunkExists = _chunks.ContainsKey(coord);
                bool mayAllocate = allocateForIntegration &&
                                   integration.Count < maxIntegrationChunks;
                if (!chunkExists && !mayAllocate) continue;

                ResidentPage page = EnsureResident(coord, mayAllocate, changed);
                if (page == null || page.PendingEviction) continue;
                page.LastTouchedFrame = Time.frameCount;
                if (visible.Count < maxVisibleChunks) visible.Add(page.Slot);
                if (allocateForIntegration && integration.Count < maxIntegrationChunks)
                    integration.Add(page.Slot);
            }

            if (changed.Count > 0) RebuildPageTablesAndDirtyLocal(changed);
            SetFrameSlots(integration, visible);

            int missing = allocateForIntegration
                ? Mathf.Min(candidates.Count, maxIntegrationChunks) - integration.Count
                : 0;
            while (missing-- > 0 && ScheduleOneEviction()) { }

            return new MerkabaResidencyFrame(IntegrationChunkCount, VisibleChunkCount);
        }

        internal void MarkIntegrationPagesGpuCurrent()
        {
            for (int i = 0; i < IntegrationChunkCount; i++)
            {
                ResidentPage page = _slots[_integrationSlotsCpu[i]];
                if (page?.Chunk != null && !page.PendingEviction)
                {
                    page.Chunk.CpuStateCurrent = false;
                    page.Chunk.Persisted = false;
                }
            }
        }

        internal Task SynchronizeResidentStateAsync()
        {
            if (!_gpuReady || _resident.Count == 0) return Task.CompletedTask;
            var completion = new TaskCompletionSource<bool>();
            int generation = _gpuGeneration;
            AsyncGPUReadback.Request(_kernelBuffer, request =>
            {
                if (request.hasError)
                {
                    completion.TrySetException(new IOException(
                        "GPU readback failed while saving the Merkaba grid."));
                    return;
                }
                if (generation != _gpuGeneration)
                {
                    completion.TrySetCanceled();
                    return;
                }

                NativeArray<KernelState> data = request.GetData<KernelState>();
                foreach (ResidentPage page in _resident.Values)
                    CopyPageSnapshot(page, data, page.Slot * MerkabaConstants.KernelsPerChunk);
                completion.TrySetResult(true);
            });
            return completion.Task;
        }

        private ResidentPage EnsureResident(int3 coord, bool createIfMissing,
            List<int3> changed)
        {
            if (_resident.TryGetValue(coord, out ResidentPage existing)) return existing;
            _chunks.TryGetValue(coord, out MerkabaChunk chunk);
            if (chunk == null && !createIfMissing) return null;
            if (_freeSlots.Count == 0) return null;

            int slot = _freeSlots.Min;
            _freeSlots.Remove(slot);
            // A missing canonical chunk receives only an empty transient GPU page.
            // It is materialised in _chunks only after a SURFACE write survives a
            // synchronization/eviction readback. FREE can therefore never allocate air.
            var page = new ResidentPage(coord, chunk, slot, Time.frameCount);
            _resident.Add(coord, page);
            _slots[slot] = page;

            int offset = slot * MerkabaConstants.KernelsPerChunk;
            KernelState[] source = chunk != null ? chunk.States : _zeroStates;
            _kernelBuffer.SetData(source, 0, offset, source.Length);
            _kernelDirtyBuffer.SetData(_dirtyOnes, 0, offset, _dirtyOnes.Length);
            _topologyMaskBuffer.SetData(_zeroMasks, 0, offset, _zeroMasks.Length);
            ResetIntegrationPage(page);
            changed.Add(coord);
            return page;
        }

        private void ResetIntegrationPage(ResidentPage page)
        {
            int wordOffset = page.Slot * WordsPerPage;
            _surfaceCandidateBitsBuffer.SetData(_zeroPageBits, 0,
                wordOffset, WordsPerPage);
            _carveListedBitsBuffer.SetData(_zeroPageBits, 0,
                wordOffset, WordsPerPage);
            _carveCountsBuffer.SetData(_singleZero, 0, page.Slot, 1);

            if (page.Chunk == null) return;
            Array.Clear(_pageCarveBits, 0, _pageCarveBits.Length);
            int count = 0;
            KernelState[] states = page.Chunk.States;
            for (int index = 0; index < states.Length; index++)
            {
                KernelState state = states[index];
                if (state.OccupancyEvidence <= MerkabaConstants.ExportKnownFreeThreshold &&
                    !state.IsOccupied)
                    continue;
                if (state.OccupancyEvidence <= 0 && !state.IsOccupied) continue;
                _pageCarveBits[index >> 5] |= 1u << (index & 31);
                _pageCarveIndices[count++] = (uint)index;
            }
            if (count == 0) return;
            _carveListedBitsBuffer.SetData(_pageCarveBits, 0,
                wordOffset, WordsPerPage);
            _carveLocalIndicesBuffer.SetData(_pageCarveIndices, 0,
                page.Slot * MerkabaConstants.KernelsPerChunk, count);
            var countValue = new[] { (uint)count };
            _carveCountsBuffer.SetData(countValue, 0, page.Slot, 1);
        }

        private bool ScheduleOneEviction()
        {
            ResidentPage victim = null;
            foreach (ResidentPage candidate in _resident.Values)
            {
                if (candidate.PendingEviction || _desiredCoords.Contains(candidate.Coord))
                    continue;
                if (victim == null || candidate.LastTouchedFrame < victim.LastTouchedFrame ||
                    (candidate.LastTouchedFrame == victim.LastTouchedFrame &&
                     candidate.Slot < victim.Slot))
                    victim = candidate;
            }
            if (victim == null) return false;

            victim.PendingEviction = true;
            RebuildPageTablesAndDirtyLocal(new List<int3> { victim.Coord });
            int generation = _gpuGeneration;
            int byteSize = MerkabaConstants.KernelsPerChunk * Marshal.SizeOf<KernelState>();
            int byteOffset = victim.Slot * byteSize;
            AsyncGPUReadback.Request(_kernelBuffer, byteSize, byteOffset, request =>
            {
                if (generation != _gpuGeneration) return;
                if (request.hasError)
                {
                    victim.PendingEviction = false;
                    Logger.Error($"MerkabaGrid: eviction readback failed for {victim.Coord}");
                    return;
                }
                bool hasCanonicalState = CopyPageSnapshot(victim,
                    request.GetData<KernelState>(), 0);
                _resident.Remove(victim.Coord);
                _slots[victim.Slot] = null;
                _freeSlots.Add(victim.Slot);
                if (!hasCanonicalState && victim.Chunk != null &&
                    _chunks.TryGetValue(victim.Coord, out MerkabaChunk current) &&
                    ReferenceEquals(current, victim.Chunk))
                    _chunks.Remove(victim.Coord);
                RebuildPageTablesAndDirtyLocal(new List<int3> { victim.Coord });
            });
            return true;
        }

        private bool CopyPageSnapshot(ResidentPage page, NativeArray<KernelState> data,
            int sourceOffset)
        {
            int occupied = 0;
            bool hasCanonicalState = false;
            for (int i = 0; i < MerkabaConstants.KernelsPerChunk; i++)
            {
                KernelState state = data[sourceOffset + i];
                if (state.IsOccupied) occupied++;
                if (state.OccupancyEvidence != 0 || state.PackedColor != 0 ||
                    state.ColorConfidence != 0 || state.Flags != 0)
                    hasCanonicalState = true;
            }
            if (!hasCanonicalState)
            {
                if (page.Chunk != null)
                {
                    OccupiedKernelCount -= page.Chunk.OccupiedCount;
                    page.Chunk.OccupiedCount = 0;
                    Array.Clear(page.Chunk.States, 0, page.Chunk.States.Length);
                    page.Chunk.CpuStateCurrent = true;
                    page.Chunk.Persisted = false;
                }
                return false;
            }

            if (page.Chunk == null)
            {
                page.Chunk = GetOrCreateChunk(page.Coord);
            }
            KernelState[] destination = page.Chunk.States;
            for (int i = 0; i < destination.Length; i++)
                destination[i] = data[sourceOffset + i];
            OccupiedKernelCount += occupied - page.Chunk.OccupiedCount;
            page.Chunk.OccupiedCount = occupied;
            page.Chunk.CpuStateCurrent = true;
            page.Chunk.Persisted = false;
            return hasCanonicalState;
        }

        private List<ChunkCandidate> CollectFrustumCandidates(Camera camera, float maxDistance)
        {
            Vector3 localCamera = transform.InverseTransformPoint(camera.transform.position);
            float chunkSpan = MerkabaConstants.ChunkSize * MerkabaConstants.LatticeStep;
            int3 cameraChunk = new(
                Mathf.FloorToInt(localCamera.x / chunkSpan),
                Mathf.FloorToInt(localCamera.y / chunkSpan),
                Mathf.FloorToInt(localCamera.z / chunkSpan));
            int radius = Mathf.CeilToInt(maxDistance / chunkSpan) + 1;
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var result = new List<ChunkCandidate>(512);
            float maxDistanceSq = maxDistance * maxDistance;

            for (int z = -radius; z <= radius; z++)
            for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                int3 coord = cameraChunk + new int3(x, y, z);
                Bounds worldBounds = ChunkWorldBounds(coord);
                float distanceSq = worldBounds.SqrDistance(camera.transform.position);
                if (distanceSq > maxDistanceSq ||
                    !GeometryUtility.TestPlanesAABB(planes, worldBounds))
                    continue;
                result.Add(new ChunkCandidate(coord, distanceSq));
            }

            result.Sort((left, right) =>
            {
                int distance = left.DistanceSquared.CompareTo(right.DistanceSquared);
                if (distance != 0) return distance;
                if (left.Coord.x != right.Coord.x)
                    return left.Coord.x.CompareTo(right.Coord.x);
                if (left.Coord.y != right.Coord.y)
                    return left.Coord.y.CompareTo(right.Coord.y);
                return left.Coord.z.CompareTo(right.Coord.z);
            });
            return result;
        }

        private Bounds ChunkWorldBounds(int3 coord)
        {
            int3 origin = MerkabaConstants.ChunkOrigin(coord);
            float3 localCenter = ((float3)origin +
                (MerkabaConstants.ChunkSize - 1) * 0.5f) * MerkabaConstants.LatticeStep;
            float localSize = MerkabaConstants.ChunkSize * MerkabaConstants.LatticeStep +
                              MerkabaConstants.HalfSupport;
            Matrix4x4 matrix = transform.localToWorldMatrix;
            Vector3 worldCenter = matrix.MultiplyPoint3x4((Vector3)localCenter);
            Vector3 localExtents = Vector3.one * (localSize * 0.5f);
            Vector3 worldExtents = new(
                Mathf.Abs(matrix.m00) * localExtents.x + Mathf.Abs(matrix.m01) * localExtents.y +
                Mathf.Abs(matrix.m02) * localExtents.z,
                Mathf.Abs(matrix.m10) * localExtents.x + Mathf.Abs(matrix.m11) * localExtents.y +
                Mathf.Abs(matrix.m12) * localExtents.z,
                Mathf.Abs(matrix.m20) * localExtents.x + Mathf.Abs(matrix.m21) * localExtents.y +
                Mathf.Abs(matrix.m22) * localExtents.z);
            return new Bounds(worldCenter, worldExtents * 2f);
        }

        private void SetFrameSlots(IReadOnlyList<int> integration, IReadOnlyList<int> visible)
        {
            IntegrationChunkCount = Mathf.Min(integration.Count, maxIntegrationChunks);
            VisibleChunkCount = Mathf.Min(visible.Count, maxVisibleChunks);
            Array.Fill(_integrationSlotsCpu, 0);
            Array.Clear(_integrationEnabledCpu, 0, _integrationEnabledCpu.Length);
            Array.Fill(_visibleSlotsCpu, 0);
            for (int i = 0; i < IntegrationChunkCount; i++)
            {
                _integrationSlotsCpu[i] = integration[i];
                _integrationEnabledCpu[integration[i]] = 1u;
            }
            for (int i = 0; i < VisibleChunkCount; i++)
                _visibleSlotsCpu[i] = visible[i];
            _integrationSlotsBuffer.SetData(_integrationSlotsCpu);
            _integrationEnabledBuffer.SetData(_integrationEnabledCpu);
            _visibleSlotsBuffer.SetData(_visibleSlotsCpu);
        }

        private void RebuildPageTablesAndDirtyLocal(IReadOnlyList<int3> changedCoords)
        {
            if (!_gpuReady) return;
            Array.Fill(_pageNeighboursCpu, -1);
            Array.Fill(_pageHashCpu, new int4(0, 0, 0, -1));
            for (int slot = 0; slot < maxResidentChunks; slot++)
                _pageCoordsCpu[slot] = new int4(0, 0, 0, -1);

            foreach (ResidentPage page in _resident.Values)
            {
                if (page.PendingEviction) continue;
                int3 coord = page.Coord;
                _pageCoordsCpu[page.Slot] = new int4(coord, page.Slot);
                InsertPageHash(coord, page.Slot);
                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int neighbourIndex = (dx + 1) + 3 * (dy + 1) + 9 * (dz + 1);
                    int3 neighbourCoord = coord + new int3(dx, dy, dz);
                    if (_resident.TryGetValue(neighbourCoord, out ResidentPage neighbour) &&
                        !neighbour.PendingEviction)
                        _pageNeighboursCpu[page.Slot * 27 + neighbourIndex] = neighbour.Slot;
                }
            }
            _pageCoordsBuffer.SetData(_pageCoordsCpu);
            _pageNeighboursBuffer.SetData(_pageNeighboursCpu);
            _pageHashBuffer.SetData(_pageHashCpu);

            var dirtySlots = new HashSet<int>();
            foreach (int3 changed in changedCoords)
            {
                if (_resident.TryGetValue(changed, out ResidentPage self) && !self.PendingEviction)
                    dirtySlots.Add(self.Slot);
                foreach (int3 offset in MerkabaConstants.Neighbours)
                    if (_resident.TryGetValue(changed + offset, out ResidentPage neighbour) &&
                        !neighbour.PendingEviction)
                        dirtySlots.Add(neighbour.Slot);
            }
            foreach (int slot in dirtySlots)
                _kernelDirtyBuffer.SetData(_dirtyOnes, 0,
                    slot * MerkabaConstants.KernelsPerChunk, _dirtyOnes.Length);
        }

        private void InsertPageHash(int3 coord, int slot)
        {
            int index = (int)(HashPageCoord(coord) & (PageHashCapacity - 1));
            for (int probe = 0; probe < PageHashCapacity; probe++)
            {
                if (_pageHashCpu[index].w < 0)
                {
                    _pageHashCpu[index] = new int4(coord, slot);
                    return;
                }
                index = (index + 1) & (PageHashCapacity - 1);
            }
            throw new InvalidOperationException("Merkaba resident page hash is full.");
        }

        internal static uint HashPageCoord(int3 coord)
        {
            unchecked
            {
                return (uint)coord.x * 73856093u ^
                       (uint)coord.y * 19349663u ^
                       (uint)coord.z * 83492791u;
            }
        }

        private void ClearGpuResidencyWithoutReadback()
        {
            if (!_gpuReady) return;
            _gpuGeneration++;
            _resident.Clear();
            _freeSlots.Clear();
            Array.Clear(_slots, 0, _slots.Length);
            Array.Fill(_pageNeighboursCpu, -1);
            Array.Fill(_pageHashCpu, new int4(0, 0, 0, -1));
            for (int slot = 0; slot < maxResidentChunks; slot++)
            {
                _freeSlots.Add(slot);
                _pageCoordsCpu[slot] = new int4(0, 0, 0, -1);
            }
            _pageCoordsBuffer.SetData(_pageCoordsCpu);
            _pageNeighboursBuffer.SetData(_pageNeighboursCpu);
            _pageHashBuffer.SetData(_pageHashCpu);
            _surfaceCountBuffer.SetData(_singleZero);
            _carveCountBuffer.SetData(_singleZero);
            SetFrameSlots(Array.Empty<int>(), Array.Empty<int>());
        }

        private void ReleaseGpuResources()
        {
            _gpuGeneration++;
            _kernelBuffer?.Release();
            _pageCoordsBuffer?.Release();
            _pageNeighboursBuffer?.Release();
            _kernelDirtyBuffer?.Release();
            _topologyMaskBuffer?.Release();
            _integrationSlotsBuffer?.Release();
            _integrationEnabledBuffer?.Release();
            _visibleSlotsBuffer?.Release();
            _pageHashBuffer?.Release();
            _surfaceCandidateBitsBuffer?.Release();
            _surfaceQueueBuffer?.Release();
            _surfaceCountBuffer?.Release();
            _carveListedBitsBuffer?.Release();
            _carveLocalIndicesBuffer?.Release();
            _carveCountsBuffer?.Release();
            _carveQueueBuffer?.Release();
            _carveCountBuffer?.Release();
            _surfaceDispatchArgsBuffer?.Release();
            _carveDispatchArgsBuffer?.Release();
            _kernelBuffer = null;
            _pageCoordsBuffer = null;
            _pageNeighboursBuffer = null;
            _kernelDirtyBuffer = null;
            _topologyMaskBuffer = null;
            _integrationSlotsBuffer = null;
            _integrationEnabledBuffer = null;
            _visibleSlotsBuffer = null;
            _pageHashBuffer = null;
            _surfaceCandidateBitsBuffer = null;
            _surfaceQueueBuffer = null;
            _surfaceCountBuffer = null;
            _carveListedBitsBuffer = null;
            _carveLocalIndicesBuffer = null;
            _carveCountsBuffer = null;
            _carveQueueBuffer = null;
            _carveCountBuffer = null;
            _surfaceDispatchArgsBuffer = null;
            _carveDispatchArgsBuffer = null;
            _gpuReady = false;
        }
    }
}
