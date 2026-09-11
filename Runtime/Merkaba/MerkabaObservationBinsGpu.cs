using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Records the observation-bin storage stages on the caller's serialized
    /// queue. All ownership/count/interval decisions remain in the GPU kernels.
    /// The R1 consumer owns the frozen input lease and the completion fence.
    /// </summary>
    internal sealed class MerkabaObservationBinsGpu : IDisposable
    {
        internal const int DispatchArgumentWords = 16;
        internal const uint AllocationDispatchOffset = 16;
        internal const uint InstallDispatchOffset = 32;
        internal const uint RecountDispatchOffset = 48;
        private readonly ComputeShader _shader;
        private readonly MerkabaGrid _grid;
        private readonly ComputeBuffer _records;
        private readonly ComputeBuffer _tileBins;
        private readonly int _count, _reserve, _emit, _resolveNodes, _resolveTiles, _installTiles, _reset;
        private readonly int[] _size = new int[2];
        private readonly Matrix4x4[] _projectionInverse = new Matrix4x4[2];
        private readonly Matrix4x4[] _viewInverse = new Matrix4x4[2];
        private Texture _depth, _normal;
        private Matrix4x4 _worldToGrid;
        private float _maxDistance;
        private int _exclusionCount;
        private Vector4[] _exclusions;
        private FineBrushDescriptor _fineBrush;
        private uint _observation;
        private bool _countRecorded, _reservationRecorded, _disposed;

        internal ComputeBuffer TileBins => _tileBins;
        internal ComputeBuffer Records => _records;
        internal uint FrozenObservation => _observation;

        // This class borrows the single 32 MiB record allocation. It never
        // allocates a parallel candidate/record bank or owns canonical state.
        internal MerkabaObservationBinsGpu(ComputeShader shader, MerkabaGrid grid,
            ComputeBuffer observationRecords)
        {
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (observationRecords == null ||
                observationRecords.count != MerkabaObservationRecord.Capacity ||
                observationRecords.stride != MerkabaObservationRecord.ByteSize)
                throw new ArgumentException("ObservationRecords must be the single 32 MiB / 16-byte arena.",
                    nameof(observationRecords));
            _shader = shader;
            _grid = grid;
            _records = observationRecords;
            _count = shader.FindKernel("CountObservationBins");
            _reserve = shader.FindKernel("ReserveObservationBins");
            _emit = shader.FindKernel("EmitObservationBins");
            _resolveNodes = shader.FindKernel("ResolveMissingSpatialNodes");
            _resolveTiles = shader.FindKernel("ResolveObservationTileRequests");
            _installTiles = grid.WorldCompute.FindKernel("InitializeNewTiles");
            _reset = shader.FindKernel("ResetObservationBins");
            MerkabaGrid.ValidateGpuBufferAllocation(MerkabaSpatial.PhysicalTileCapacity, 16);
            _tileBins = new ComputeBuffer(MerkabaSpatial.PhysicalTileCapacity, 16);
            try
            {
                // One-time 512 KiB initialization; subsequent resets touch only
                // the previous attempt's recorded physical tiles.
                _tileBins.SetData(new Unity.Mathematics.uint4[MerkabaSpatial.PhysicalTileCapacity]);
            }
            catch
            {
                _tileBins.Dispose();
                throw;
            }
        }

        // Keep the sensor field immutable. The GPU reader admits strict
        // endpoints separately from bootstrap; this recorder never chooses.
        internal void Begin(uint observation, Texture acceptedDepth, Texture acceptedNormal,
            in Matrix4x4 referenceProjectionInverse, in Matrix4x4 referenceViewInverse,
            in Matrix4x4 worldToGrid, float maxDistance, int exclusionCount,
            Vector4[] exclusions, in FineBrushDescriptor fineBrush)
        {
            ThrowIfDisposed();
            if (_observation != 0u)
                throw new InvalidOperationException("The preceding immutable observation is still owned.");
            if (observation == 0u) throw new ArgumentOutOfRangeException(nameof(observation));
            if (!float.IsFinite(maxDistance) || maxDistance <= 0f ||
                exclusions == null || exclusions.Length != 64 ||
                (uint)exclusionCount > 64u)
                throw new ArgumentException("Invalid frozen observation scope.");
            if (acceptedDepth == null || acceptedNormal == null ||
                acceptedDepth.width != acceptedNormal.width ||
                acceptedDepth.height != acceptedNormal.height ||
                acceptedDepth.width < 1 || acceptedDepth.height < 1 ||
                acceptedDepth.width > 512 || acceptedDepth.height > 512)
                throw new ArgumentException("Accepted depth/normal dimensions must agree and be at most 512².");
            _depth = acceptedDepth;
            _normal = acceptedNormal;
            _size[0] = acceptedDepth.width;
            _size[1] = acceptedDepth.height;
            _projectionInverse[0] = referenceProjectionInverse;
            _projectionInverse[1] = referenceProjectionInverse;
            _viewInverse[0] = referenceViewInverse;
            _viewInverse[1] = referenceViewInverse;
            _worldToGrid = worldToGrid;
            _maxDistance = maxDistance;
            _exclusionCount = exclusionCount;
            _exclusions = exclusions;
            _fineBrush = fineBrush;
            _observation = observation;
        }

        // Part of the existing native observation job, not a separate queue
        // or camera lease. Only the two compact storage resources are added.
        internal void FillNativeResources(IntPtr[] resources)
        {
            resources[(int)MerkabaNativeVulkanExecutor.Resource.ObservationRecords] =
                _records.GetNativeBufferPtr();
            resources[(int)MerkabaNativeVulkanExecutor.Resource.ObservationTileBins] =
                _tileBins.GetNativeBufferPtr();
        }

        // The editor/graphics backend records the identical complete storage
        // sequence as the native job. Callers cannot accidentally reserve a
        // partial count or skip its allocation publication barrier.
        internal void Record(CommandBuffer command, bool reset)
        {
            RequireObservation(command);
            if (reset) RecordReset(command);
            // Existing HOT counts survive unchanged. Only actual sparse claims
            // dispatch publication/reset; only new addresses dispatch recount.
            RecordCount(command);
            // Three address dependencies: block, chunk, tile. The kernels are
            // reused; each absent request records zero indirect work.
            for (int level = 0; level < 3; ++level)
            {
                RecordTileRequestPublication(command);
                RecordReset(command, recount: true);
                RecordCount(command, recount: true);
            }
            // A contended final discovery may have queued storage-only work.
            // Publish before the claim backing is reused.
            RecordTileRequestPublication(command);
            RecordReserveAndEmit(command);
        }

        private void RecordCount(CommandBuffer command, bool recount = false)
        {
            RequireObservation(command);
            if (_countRecorded)
                throw new InvalidOperationException("Retire the preceding bin reservation before recounting.");
            BindCommon(command, _count);
            BindInput(command, _shader, _count);
            Bind(command, _count, "_M8TouchedTileQueue", _grid.M8TouchedTileQueue);
            Bind(command, _count, "_M8HashEntries", _grid.M8HashEntries);
            Bind(command, _count, "_M8ClaimQueue", _grid.M8ClaimQueue);
            Bind(command, _count, "_M8BlockChunkRefs", _grid.M8BlockChunkRefs);
            Bind(command, _count, "_M8ChunkTileRefs", _grid.M8ChunkTileRefs);
            Bind(command, _count, "_M8ObservationDispatchArgs", _grid.M8ObservationDispatchArgs);
            if (recount)
                command.DispatchCompute(_shader, _count, _grid.M8ObservationDispatchArgs, RecountDispatchOffset);
            else
                command.DispatchCompute(_shader, _count, (_size[0] + 7) / 8, (_size[1] + 7) / 8, 1);
            _countRecorded = true;
        }

        internal void RecordTileRequestPublication(CommandBuffer command)
        {
            RequireObservation(command);
            if (!_countRecorded)
                throw new InvalidOperationException("Tile requests must originate in this observation's count pass.");
            Bind(command, _resolveNodes, "_M8Counters", _grid.M8Counters);
            Bind(command, _resolveNodes, "_M8ClaimQueue", _grid.M8ClaimQueue);
            Bind(command, _resolveNodes, "_M8OwnerRecords", _grid.M8OwnerRecords);
            Bind(command, _resolveNodes, "_M8HashEntries", _grid.M8HashEntries);
            Bind(command, _resolveNodes, "_M8BlockChunkRefs", _grid.M8BlockChunkRefs);
            Bind(command, _resolveNodes, "_M8BlockPresenceL0", _grid.M8BlockPresenceL0);
            Bind(command, _resolveNodes, "_M8BlockPresenceL1", _grid.M8BlockPresenceL1);
            Bind(command, _resolveNodes, "_M8BlockPresenceL2", _grid.M8BlockPresenceL2);
            command.DispatchCompute(_shader, _resolveNodes,
                _grid.M8ObservationDispatchArgs, AllocationDispatchOffset);
            Bind(command, _resolveTiles, "_M8Counters", _grid.M8Counters);
            Bind(command, _resolveTiles, "_M8ClaimQueue", _grid.M8ClaimQueue);
            Bind(command, _resolveTiles, "_M8ChunkTileRefs", _grid.M8ChunkTileRefs);
            Bind(command, _resolveTiles, "_M8OwnerRecordsRead", _grid.M8OwnerRecords);
            Bind(command, _resolveTiles, "_M8ChunkPresence", _grid.M8ChunkPresence);
            Bind(command, _resolveTiles, "_M8PendingNewTileRefs", _grid.M8PendingNewTileRefs);
            Bind(command, _resolveTiles, "_M8LoadRequests", _grid.M8LoadRequests);
            Bind(command, _resolveTiles, "_M8LoadRequestReadCount", _grid.M8LoadRequestReadCount);
            Bind(command, _resolveTiles, "_M8ObservationDispatchArgs", _grid.M8ObservationDispatchArgs);
            command.DispatchCompute(_shader, _resolveTiles,
                _grid.M8ObservationDispatchArgs, AllocationDispatchOffset);
            ComputeShader world = _grid.WorldCompute;
            command.SetComputeBufferParam(world, _installTiles, "_M8ClaimQueueRead", _grid.M8ClaimQueue);
            command.SetComputeBufferParam(world, _installTiles, "_M8Counters", _grid.M8Counters);
            command.SetComputeBufferParam(world, _installTiles, "_M8FreeTileStackRead", _grid.M8FreeTileStack);
            command.SetComputeBufferParam(world, _installTiles, "_M8KernelStates0", _grid.M8KernelStates0);
            command.SetComputeBufferParam(world, _installTiles, "_M8KernelStates1", _grid.M8KernelStates1);
            command.SetComputeBufferParam(world, _installTiles, "_M8KernelStates2", _grid.M8KernelStates2);
            command.SetComputeBufferParam(world, _installTiles, "_M8KernelStates3", _grid.M8KernelStates3);
            command.SetComputeBufferParam(world, _installTiles, "_M8TileBits", _grid.M8TileBits);
            command.SetComputeBufferParam(world, _installTiles, "_M8TileRecords", _grid.M8TileRecords);
            command.SetComputeBufferParam(world, _installTiles, "_M8ChunkTileRefs", _grid.M8ChunkTileRefs);
            command.DispatchCompute(world, _installTiles,
                _grid.M8ObservationDispatchArgs, InstallDispatchOffset);
        }

        private void RecordReserveAndEmit(CommandBuffer command)
        {
            RequireObservation(command);
            if (!_countRecorded || _reservationRecorded)
                throw new InvalidOperationException("Each count pass has exactly one reservation.");
            BindCommon(command, _reserve);
            Bind(command, _reserve, "_M8TouchedTileQueue", _grid.M8TouchedTileQueue);
            Bind(command, _reserve, "_M8ObservationDispatchArgs", _grid.M8ObservationDispatchArgs);
            command.DispatchCompute(_shader, _reserve, 1, 1, 1);
            BindCommon(command, _emit);
            BindInput(command, _shader, _emit);
            Bind(command, _emit, "_M8HashEntriesRead", _grid.M8HashEntries);
            Bind(command, _emit, "_M8BlockChunkRefsRead", _grid.M8BlockChunkRefs);
            Bind(command, _emit, "_M8ChunkTileRefsRead", _grid.M8ChunkTileRefs);
            Bind(command, _emit, "_M8ObservationRecords", _records);
            Bind(command, _emit, "_M8TileBits", _grid.M8TileBits);
            command.DispatchCompute(_shader, _emit, (_size[0] + 7) / 8, (_size[1] + 7) / 8, 1);
            _reservationRecorded = true;
        }

        // The production FlowerCommit dispatch is
        // GPU-indirect: unresolved allocation/overflow produces zero groups.
        // No tile count or geometry data is read back to the CPU.
        internal void RecordCommit(CommandBuffer command, ComputeShader flowerCommit, int kernel)
        {
            RecordBindConsumer(command, flowerCommit, kernel);
            command.DispatchCompute(flowerCommit, kernel, _grid.M8ObservationDispatchArgs, 0);
        }

        // R1 and fine refinement consume the same frozen bins.
        // Binding is shared; it does not issue another workgroup or readback.
        internal void RecordBindConsumer(CommandBuffer command, ComputeShader flowerCommit, int kernel)
        {
            RequireObservation(command);
            if (!_reservationRecorded)
                throw new InvalidOperationException("FlowerCommit requires an emitted reservation.");
            command.SetComputeBufferParam(flowerCommit, kernel, MerkabaGrid.FlowerTablesId, _grid.M8FlowerTables);
            command.SetComputeBufferParam(flowerCommit, kernel, "_M8ObservationRecords", _records);
            command.SetComputeBufferParam(flowerCommit, kernel, "_M8ObservationRecordsRead", _records);
            command.SetComputeBufferParam(flowerCommit, kernel, "_M8ObservationTileBins", _tileBins);
            command.SetComputeBufferParam(flowerCommit, kernel, "_M8ObservationTileBinsRead", _tileBins);
            command.SetComputeBufferParam(flowerCommit, kernel, "_M8TouchedTileQueue", _grid.M8TouchedTileQueue);
            command.SetComputeBufferParam(flowerCommit, kernel, "_M8TouchedTileQueueRead", _grid.M8TouchedTileQueue);
            command.SetComputeBufferParam(flowerCommit, kernel, "_M8ObservationDispatchArgs", _grid.M8ObservationDispatchArgs);
            command.SetComputeBufferParam(flowerCommit, kernel, "_M8Counters", _grid.M8Counters);
            command.SetComputeBufferParam(flowerCommit, kernel, "_M8TileHalo", _grid.M8TileHalo);
            command.SetComputeBufferParam(flowerCommit, kernel, "_M8TileRecordsRead", _grid.M8TileRecords);
            command.SetComputeBufferParam(flowerCommit, kernel, "_M8HashEntriesRead", _grid.M8HashEntries);
            command.SetComputeBufferParam(flowerCommit, kernel, "_M8OwnerRecordsRead", _grid.M8OwnerRecords);
            command.SetComputeBufferParam(flowerCommit, kernel, "_M8BlockChunkRefsRead", _grid.M8BlockChunkRefs);
            command.SetComputeBufferParam(flowerCommit, kernel, "_M8ChunkTileRefsRead", _grid.M8ChunkTileRefs);
            command.SetComputeIntParam(flowerCommit, "_M8ObservationToken", unchecked((int)_observation));
            command.SetComputeIntParam(flowerCommit, "_M8ObservationRecordCapacity", _records.count);
            command.SetComputeIntParam(flowerCommit, "_M8ObservationHotSlotCount", MerkabaSpatial.PhysicalTileCapacity);
            BindInput(command, flowerCommit, kernel);
        }

        // Initial reset or a same-snapshot sparse-allocation recount boundary.
        internal void RecordReset(CommandBuffer command, bool recount = false)
        {
            RequireObservation(command);
            BindReset(command, _shader, _reset);
            if (recount)
                command.DispatchCompute(_shader, _reset, _grid.M8ObservationDispatchArgs, AllocationDispatchOffset);
            else
                command.DispatchCompute(_shader, _reset, 1, 1, 1);
            _countRecorded = false;
            _reservationRecorded = false;
        }

        internal void BindReset(CommandBuffer command, ComputeShader shader, int kernel)
        {
            RequireObservation(command);
            command.SetComputeIntParam(shader, "_M8ObservationToken", unchecked((int)_observation));
            command.SetComputeIntParam(shader, "_M8ObservationHotSlotCount", MerkabaSpatial.PhysicalTileCapacity);
            command.SetComputeBufferParam(shader, kernel, "_M8Counters", _grid.M8Counters);
            command.SetComputeBufferParam(shader, kernel, "_M8ObservationTileBins", _tileBins);
            command.SetComputeBufferParam(shader, kernel, "_M8TouchedTileQueue", _grid.M8TouchedTileQueue);
            command.SetComputeBufferParam(shader, kernel, "_M8TileBits", _grid.M8TileBits);
            command.SetComputeBufferParam(shader, kernel, "_M8TileRecordsRead", _grid.M8TileRecords);
            command.SetComputeBufferParam(shader, kernel, "_M8ObservationDispatchArgs", _grid.M8ObservationDispatchArgs);
        }

        // A fence proves only resource retirement, not refinement exhaustion.
        // FinalizeObservation remains responsible for the latter; this method
        // must be called from its completion path, never from a camera timer.
        internal void EndAfterFinalization()
        {
            ThrowIfDisposed();
            _countRecorded = false;
            _reservationRecorded = false;
            _depth = null;
            _normal = null;
            _exclusions = null;
            _fineBrush = default;
            _observation = 0u;
        }

        private void BindCommon(CommandBuffer command, int kernel)
        {
            Bind(command, kernel, "_M8FlowerTables", _grid.M8FlowerTables);
            command.SetComputeIntParam(_shader, "_M8ObservationToken", unchecked((int)_observation));
            command.SetComputeIntParam(_shader, "_M8ObservationHotSlotCount", MerkabaSpatial.PhysicalTileCapacity);
            command.SetComputeIntParam(_shader, "_M8ObservationRecordCapacity", _records.count);
            Bind(command, kernel, "_M8ObservationTileBins", _tileBins);
            Bind(command, kernel, "_M8Counters", _grid.M8Counters);
        }

        private void BindInput(CommandBuffer command, ComputeShader shader, int kernel)
        {
            command.SetComputeTextureParam(shader, kernel, "gsDepthTex", _depth);
            command.SetComputeTextureParam(shader, kernel, "gsDepthNormalTex", _normal);
            command.SetComputeIntParams(shader, "gsDepthTexSize", _size);
            command.SetComputeMatrixArrayParam(shader, "gsDepthProjInv", _projectionInverse);
            command.SetComputeMatrixArrayParam(shader, "gsDepthViewInv", _viewInverse);
            command.SetComputeMatrixParam(shader, "_MerkabaWorldToGrid", _worldToGrid);
            command.SetComputeFloatParam(shader, "_MerkabaMaxUpdateDistance", _maxDistance);
            command.SetComputeIntParam(shader, "_MerkabaExclusionCount", _exclusionCount);
            command.SetComputeVectorArrayParam(shader, "_MerkabaExclusionHeads", _exclusions);
            command.SetComputeIntParam(shader, "_M8FineRefineActive", _fineBrush.IsRefine ? 1 : 0);
            command.SetComputeVectorParam(shader, "_M8FineCursorPosition", _fineBrush.CursorPosition);
            command.SetComputeVectorParam(shader, "_M8FineBrushAxis", _fineBrush.Axis);
            command.SetComputeFloatParam(shader, "_M8FineRadiusSquared", _fineBrush.Radius*_fineBrush.Radius);
            command.SetComputeFloatParam(shader, "_M8FineLength", _fineBrush.Length);
        }

        private void Bind(CommandBuffer command, int kernel, string name, ComputeBuffer buffer) =>
            command.SetComputeBufferParam(_shader, kernel, name, buffer);

        private void RequireObservation(CommandBuffer command)
        {
            ThrowIfDisposed();
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (_observation == 0u) throw new InvalidOperationException("No immutable observation is held.");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MerkabaObservationBinsGpu));
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_observation != 0u)
                throw new InvalidOperationException("Finalize and retire the observation before releasing its buffers.");
            _tileBins.Dispose();
            _disposed = true;
        }
    }
}
