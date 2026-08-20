using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// Accumulates classified contact discontinuities into sparse persistent cubic
    /// BoundaryCurves.  Work is compacted and solved on GPU; no edge pixels or curve
    /// counts return to the CPU hot path.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(18)]
    public sealed class PrismBoundaryGraph : MonoBehaviour
    {
        private const int AccumulatorWords = 16;
        private const int BoundaryDispatchOffset =
            (int)ConeEventClass.Boundary * sizeof(uint) * 3;

        [SerializeField] private PrismFilmUpdater filmUpdater;
        [SerializeField] private PrismFilmSpawner filmSpawner;
        [SerializeField] private ComputeShader boundaryCompute;
        [SerializeField, Min(1024)] private int boundaryCapacity = 131072;
        [SerializeField, Min(2048)] private int hashCapacity = 262144;
        [SerializeField, Range(4, 32)] private int cellsPerAxis = 16;
        [SerializeField, Range(2, 32)] private int persistentViewSupport = 8;

        private static readonly int BoundaryCapacityId = Shader.PropertyToID("_BoundaryCapacity");
        private static readonly int HashMaskId = Shader.PropertyToID("_HashMask");
        private static readonly int EventCapacityId = Shader.PropertyToID("_EventCapacity");
        private static readonly int CellsPerAxisId = Shader.PropertyToID("_CellsPerAxis");
        private static readonly int PersistentSupportId = Shader.PropertyToID("_PersistentSupport");
        private static readonly int EventsId = Shader.PropertyToID("_ConeEvents");
        private static readonly int FilmHeadersId = Shader.PropertyToID("_FilmHeaders");
        private static readonly int ClassifiedIndicesId = Shader.PropertyToID("_ClassifiedIndices");
        private static readonly int ClassCountersId = Shader.PropertyToID("_ClassCounters");
        private static readonly int BoundaryHeadersId = Shader.PropertyToID("_BoundaryHeaders");
        private static readonly int BoundaryInformationId = Shader.PropertyToID("_BoundaryInformation");
        private static readonly int BoundaryHashId = Shader.PropertyToID("_BoundaryHash");
        private static readonly int BoundaryAllocatorId = Shader.PropertyToID("_BoundaryAllocator");
        private static readonly int FrameAccumulatorId = Shader.PropertyToID("_BoundaryFrameAccumulator");
        private static readonly int DirtyFlagsId = Shader.PropertyToID("_BoundaryDirtyFlags");
        private static readonly int DirtyIndicesId = Shader.PropertyToID("_DirtyBoundaryIndices");
        private static readonly int DirtyStateId = Shader.PropertyToID("_BoundaryDirtyState");
        private static readonly int SolveArgumentsId = Shader.PropertyToID("_BoundarySolveArguments");

        private ContactBoundaryPool _pool;
        private GraphicsBuffer _frameAccumulator;
        private GraphicsBuffer _dirtyFlags;
        private GraphicsBuffer _dirtyIndices;
        private GraphicsBuffer _dirtyState;
        private GraphicsBuffer _solveArguments;
        private int _initializeKernel = -1;
        private int _clearKernel = -1;
        private int _accumulateKernel = -1;
        private int _buildArgsKernel = -1;
        private int _solveKernel = -1;
        private bool _running;
        private long _processedFrames;

        public event Action<ConeEventFrameLease> BoundariesUpdated;
        public ContactBoundaryPool BoundaryPool => _pool;
        public long ProcessedFrames => _processedFrames;

        public void StartTracking(PrismFilmUpdater updater = null,
            PrismFilmSpawner films = null)
        {
            if (_running) return;
            filmUpdater = updater != null ? updater : filmUpdater;
            filmSpawner = films != null ? films : filmSpawner;
            filmUpdater ??= GetComponent<PrismFilmUpdater>();
            filmSpawner ??= GetComponent<PrismFilmSpawner>();
            boundaryCompute ??= Resources.Load<ComputeShader>("Prism/ContactBoundaryUpdate");
            if (filmUpdater == null || filmSpawner?.FilmPool == null ||
                boundaryCompute == null)
            {
                Logger.Error("Cone-PRISM boundary graph dependencies are missing.");
                return;
            }

            _pool ??= new ContactBoundaryPool(boundaryCapacity, hashCapacity);
            AllocateFrameBuffers(_pool.Capacity);
            _initializeKernel = boundaryCompute.FindKernel("InitializeBoundaryState");
            _clearKernel = boundaryCompute.FindKernel("ClearBoundaryFrame");
            _accumulateKernel = boundaryCompute.FindKernel("AccumulateBoundaryEvents");
            _buildArgsKernel = boundaryCompute.FindKernel("BuildBoundarySolveArguments");
            _solveKernel = boundaryCompute.FindKernel("SolveDirtyBoundaries");
            BindAll();
            boundaryCompute.Dispatch(_initializeKernel,
                CeilDiv(Math.Max(_pool.Capacity, _pool.HashCapacity), 64), 1, 1);
            filmUpdater.UpdateCompleted += OnFilmUpdateCompleted;
            _running = true;
        }

        public void StopTracking()
        {
            if (_running && filmUpdater != null)
                filmUpdater.UpdateCompleted -= OnFilmUpdateCompleted;
            _running = false;
        }

        private void OnDestroy()
        {
            StopTracking();
            DisposeFrameBuffers();
            _pool?.Dispose();
            _pool = null;
        }

        private void OnFilmUpdateCompleted(ConeEventFrameLease frame)
        {
            if (!_running || frame == null || frame.IsDisposed || _pool == null) return;
            try
            {
                BindAll();
                boundaryCompute.SetInt(EventCapacityId, frame.EventCapacity);
                boundaryCompute.SetInt(CellsPerAxisId, cellsPerAxis);
                boundaryCompute.SetInt(PersistentSupportId, persistentViewSupport);
                boundaryCompute.SetBuffer(_accumulateKernel, EventsId, frame.Events);
                boundaryCompute.SetBuffer(_accumulateKernel, ClassifiedIndicesId,
                    frame.ClassifiedIndices);
                boundaryCompute.SetBuffer(_accumulateKernel, ClassCountersId,
                    frame.ClassCounters);
                boundaryCompute.Dispatch(_clearKernel, 1, 1, 1);
                boundaryCompute.DispatchIndirect(_accumulateKernel,
                    frame.ClassDispatchArguments, BoundaryDispatchOffset);
                boundaryCompute.Dispatch(_buildArgsKernel, 1, 1, 1);
                boundaryCompute.DispatchIndirect(_solveKernel, _solveArguments, 0);
                _processedFrames++;
                if (BoundariesUpdated != null) BoundariesUpdated.Invoke(frame);
                else filmSpawner.NotifyFilmsMutated();
            }
            catch (Exception exception)
            {
                Logger.Error($"Cone-PRISM boundary update failed: {exception.Message}");
                filmSpawner.NotifyFilmsMutated();
            }
        }

        private void AllocateFrameBuffers(int capacity)
        {
            if (_frameAccumulator != null) return;
            _frameAccumulator = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                checked(capacity * AccumulatorWords), sizeof(int));
            _dirtyFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                capacity, sizeof(uint));
            _dirtyIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                capacity, sizeof(uint));
            _dirtyState = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                4, sizeof(uint));
            _solveArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                1, sizeof(uint) * 3);
        }

        private void BindAll()
        {
            boundaryCompute.SetInt(BoundaryCapacityId, _pool.Capacity);
            boundaryCompute.SetInt(HashMaskId, _pool.HashCapacity - 1);
            int[] kernels =
            {
                _initializeKernel, _clearKernel, _accumulateKernel,
                _buildArgsKernel, _solveKernel
            };
            foreach (int kernel in kernels)
            {
                boundaryCompute.SetBuffer(kernel, BoundaryHeadersId, _pool.Headers);
                boundaryCompute.SetBuffer(kernel, BoundaryInformationId,
                    _pool.Information);
                boundaryCompute.SetBuffer(kernel, BoundaryHashId, _pool.HashEntries);
                boundaryCompute.SetBuffer(kernel, BoundaryAllocatorId, _pool.Allocator);
                boundaryCompute.SetBuffer(kernel, FrameAccumulatorId, _frameAccumulator);
                boundaryCompute.SetBuffer(kernel, DirtyFlagsId, _dirtyFlags);
                boundaryCompute.SetBuffer(kernel, DirtyIndicesId, _dirtyIndices);
                boundaryCompute.SetBuffer(kernel, DirtyStateId, _dirtyState);
                boundaryCompute.SetBuffer(kernel, SolveArgumentsId, _solveArguments);
            }
            boundaryCompute.SetBuffer(_accumulateKernel, FilmHeadersId,
                filmSpawner.FilmPool.Headers);
        }

        private void DisposeFrameBuffers()
        {
            _frameAccumulator?.Dispose();
            _dirtyFlags?.Dispose();
            _dirtyIndices?.Dispose();
            _dirtyState?.Dispose();
            _solveArguments?.Dispose();
            _frameAccumulator = null;
            _dirtyFlags = null;
            _dirtyIndices = null;
            _dirtyState = null;
            _solveArguments = null;
        }

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);
    }
}
