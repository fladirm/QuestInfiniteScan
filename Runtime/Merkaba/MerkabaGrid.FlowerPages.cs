using System;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>Resident budgets and byte offsets, not limits on stored world size.</summary>
    internal static class MerkabaFlowerGpuLayout
    {
        internal const int TileCapacity = 32768;
        internal const int TileDirectoryBase = 64;
        internal const int TileDirectoryStride = 16;
        internal const int OwnerIndexBytes = 512 * sizeof(uint);
        internal const int OwnerHeaderBytes = 64;
        internal const int PersistentOrder = 17;
        internal const int DrawOrder = 18;
        internal const int PersistentBytes = 32 * 1024 * 1024;
        internal const int SymbolBytes = 64 * 1024 * 1024;
        internal const int DrawSampleBytes = 64 * 1024 * 1024;
        internal const int ArenaHeaderBytes = 32;
        internal static uint[] CreateArenaInitialWords(int order)
        {
            if (order < 5 || order > DrawOrder) throw new ArgumentOutOfRangeException(nameof(order));
            int leaves = (1 << (order + 1)) >> 5;
            int length = ArenaHeaderBytes / 4 + 2 * leaves;
            int count = leaves;
            for (int level = 1; level < 4; ++level)
            {
                count = (count + 31) >> 5;
                length += count;
            }
            var words = new uint[length];
            words[ArenaHeaderBytes / 4] = 2u; // free heap node 1 = whole arena
            int first = ArenaHeaderBytes / 4 + 2 * leaves;
            count = leaves;
            for (int level = 1; level < 4; ++level)
            {
                words[first] = 1u;
                count = (count + 31) >> 5;
                first += count;
            }
            return words;
        }
        internal const int DetailArenaControl = TileDirectoryBase +
            TileCapacity * TileDirectoryStride;
        internal const int DetailDataBase = (DetailArenaControl +
            ArenaHeaderBytes + ((1 << (PersistentOrder + 1)) - 1) * 4 + 255) & ~255;
        internal const int DetailBufferBytes = DetailDataBase + PersistentBytes;
        // GPU-only handoff inside one observation; not persistent Flower data.
        internal const int SignalBufferBytes = 32 * 1024 * 1024;
        internal const int SignalHeaderBytes = 64;
        internal const int SignalItemBytes = 224;
        internal const int SignalDispatchOffset = 16;
        internal const int ChangeDispatchOffset = 32;
        internal const int ChangeTileDispatchOffset = 48;
        internal const int MeasuredOwnerDispatchOffset = ChangeDispatchOffset;
        internal static uint[] CreateSignalInitialHeader()
        {
            var words = new uint[(SignalHeaderBytes + 4096) / 4];
            words[5] = words[6] = words[9] = words[10] = words[13] = words[14] = 1u;
            return words;
        }
        internal const int ThreadArenaControl = 0;
        internal const int ThreadDataBase = (ArenaHeaderBytes +
            ((1 << (PersistentOrder + 1)) - 1) * 4 + 255) & ~255;
        internal const int DrawArenaControl = ThreadDataBase + PersistentBytes;
        internal const int ThreadPersistentBufferBytes = DrawArenaControl;
        internal const int DrawDataBase = (DrawArenaControl + ArenaHeaderBytes +
            ((1 << (DrawOrder + 1)) - 1) * 4 + 255) & ~255;
        internal const int ThreadBufferBytes = DrawDataBase + DrawSampleBytes;

        internal const int PageDirectoryBase = 64;
        internal const int PageHeaderBase = PageDirectoryBase + TileCapacity * 16;
        // Two tiny headers per page are publication metadata, never two world
        // symbol/vertex arenas. Symbols use one shared generational allocator.
        internal const int PageAuxBase = PageHeaderBase + TileCapacity * 2 * 32;
        internal const int PageDirtyBitsBase = PageAuxBase + TileCapacity * 32;
        internal const int PageDirtySummaryBase = PageDirtyBitsBase + 1024 * 4;
        internal const int SymbolArenaControl = PageDirtySummaryBase + 32 * 4;
        internal const int DirtyBatchCapacity = 32;
        internal const int DirtyBatchBase = (SymbolArenaControl + ArenaHeaderBytes +
            ((1 << (DrawOrder + 1)) - 1) * 4 + 15) & ~15;
        internal const int DirtyBatchRecordBytes = 64;
        internal const int DirtyBatchEmitIndices = DirtyBatchBase + DirtyBatchCapacity * DirtyBatchRecordBytes;
        internal const int DirtyBatchScratchBase = DirtyBatchEmitIndices + DirtyBatchCapacity * sizeof(uint);
        // Per-page execution scratch: 512 uint2 exclusive prefixes. No world vertices.
        internal const int DirtyBatchScratchBytes = 512 * 8;
        internal const int PageDirectoryBytes = DirtyBatchScratchBase + DirtyBatchCapacity * DirtyBatchScratchBytes;
        internal const int IndirectCommandBytes = 5 * sizeof(uint);
        internal const int IndirectCountOffset = TileCapacity * IndirectCommandBytes;
        internal const int DirtyBatchDispatchOffset = (IndirectCountOffset + sizeof(uint) + 15) & ~15;
        internal const int IndirectBytes = DirtyBatchDispatchOffset + 4 * sizeof(uint);
        internal const int MaximumPageSymbols = 512 * 128;
        internal const int VerticesPerSymbol = 7;
        internal const int IndicesPerSymbol = 18;
        internal const int StaticIndexCount = MaximumPageSymbols * IndicesPerSymbol;
    }

    public sealed partial class MerkabaGrid
    {
        private ComputeBuffer _m8FlowerDetailPages;
        private ComputeBuffer _m8ThreadAtlasPages;
        private ComputeBuffer _m8FlowerSymbolArena;
        private ComputeBuffer _m8FlowerPageDirectory;
        private ComputeBuffer _m8FlowerIndirectCommands;
        private GraphicsBuffer _m8FlowerIndices;
        private bool _flowerReadoutConsumerRegistered;

        internal ComputeBuffer M8FlowerDetailPages => _m8FlowerDetailPages;
        internal ComputeBuffer M8ThreadAtlasPages => _m8ThreadAtlasPages;
        internal ComputeBuffer M8FlowerSymbolArena => _m8FlowerSymbolArena;
        internal ComputeBuffer M8FlowerPageDirectory => _m8FlowerPageDirectory;
        internal ComputeBuffer M8FlowerIndirectCommands => _m8FlowerIndirectCommands;
        internal GraphicsBuffer M8FlowerIndices => _m8FlowerIndices;

        // The sole procedural renderer registers in Awake, before any scan,
        // OPEN or storage consumer calls EnsureGpuResources. This is resource
        // ownership, not an alternate geometry path or runtime feature switch.
        internal void RegisterFlowerReadoutConsumer()
        {
            if (_flowerReadoutConsumerRegistered) return;
            if (_m8ThreadAtlasPages != null)
                throw new InvalidOperationException(
                    "The Flower readout consumer must register before canonical GPU allocation.");
            _flowerReadoutConsumerRegistered = true;
        }

        // Called once by EnsureGpuResources, before native resources are captured.
        // The existing owned-buffer list retires these with the same GPU fence.
        private void AllocateFlowerPages()
        {
            if (_m8FlowerDetailPages != null) return;
            _m8FlowerDetailPages = Allocate(MerkabaFlowerGpuLayout.DetailBufferBytes / 4,
                4, ComputeBufferType.Raw);
            int threadBytes = _flowerReadoutConsumerRegistered
                ? MerkabaFlowerGpuLayout.ThreadBufferBytes
                : MerkabaFlowerGpuLayout.ThreadPersistentBufferBytes;
            _m8ThreadAtlasPages = Allocate(threadBytes / 4,
                4, ComputeBufferType.Raw);
            _m8FlowerDetailPages.name = "M8 FlowerDetail resident pages";
            _m8ThreadAtlasPages.name = _flowerReadoutConsumerRegistered
                ? "M8 ThreadAtlas persistent prefix + derived draw pool"
                : "M8 ThreadAtlas persistent pages";
            // Canonical mutation/storage always publishes source dirtiness,
            // even when no presentation consumer has been registered.
            _m8FlowerPageDirectory = Allocate((MerkabaFlowerGpuLayout.PageDirectoryBytes + 3) / 4,
                4, ComputeBufferType.Raw);
            _m8FlowerPageDirectory.name = "M8 Flower page directory";
            if (_flowerReadoutConsumerRegistered)
            {
                _m8FlowerSymbolArena = Allocate(MerkabaFlowerGpuLayout.SymbolBytes / 4,
                    4, ComputeBufferType.Raw);
                _m8FlowerIndirectCommands = Allocate(MerkabaFlowerGpuLayout.IndirectBytes / 4,
                    4, ComputeBufferType.Raw | ComputeBufferType.IndirectArguments);
                _m8FlowerSymbolArena.name = "M8 Flower symbols 64 MiB";
                _m8FlowerIndirectCommands.name = "M8 Flower indirect commands";
                AllocateFlowerIndices();
            }
            ResetFlowerPagesAfterRetirement();
        }

        private void AllocateFlowerIndices()
        {
            ValidateGpuBufferAllocation(MerkabaFlowerGpuLayout.StaticIndexCount,
                sizeof(uint));
            var indices = new uint[MerkabaFlowerGpuLayout.StaticIndexCount];
            int write = 0;
            for (uint symbol = 0; symbol < MerkabaFlowerGpuLayout.MaximumPageSymbols; symbol++)
            {
                uint first = symbol * MerkabaFlowerGpuLayout.VerticesPerSymbol;
                for (uint wedge = 0; wedge < 6; wedge++)
                {
                    indices[write++] = first;
                    indices[write++] = first + wedge + 1u;
                    indices[write++] = first + (wedge + 1u) % 6u + 1u;
                }
            }
            _m8FlowerIndices = new GraphicsBuffer(GraphicsBuffer.Target.Index,
                indices.Length, sizeof(uint)) { name = "M8 immutable Flower fan indices" };
            _m8FlowerIndices.SetData(indices);
        }

        internal void BindFlowerRenderResources(Material material)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            material.SetBuffer(FlowerTablesId, _m8FlowerTables);
            material.SetBuffer("_M8KernelStates0Read", _m8KernelStates0);
            material.SetBuffer("_M8KernelStates1Read", _m8KernelStates1);
            material.SetBuffer("_M8KernelStates2Read", _m8KernelStates2);
            material.SetBuffer("_M8KernelStates3Read", _m8KernelStates3);
            material.SetBuffer("_M8TileRecordsRead", _m8TileRecords);
            material.SetBuffer("_M8TileHaloRead", _m8TileHalo);
            material.SetBuffer("_M8OwnerRecordsRead", _m8OwnerRecords);
            material.SetBuffer("_M8ChunkTileRefsRead", _m8ChunkTileRefs);
            material.SetBuffer("_M8FlowerDetailPagesRead", _m8FlowerDetailPages);
            material.SetBuffer("_M8ThreadAtlasPagesRead", _m8ThreadAtlasPages);
            material.SetBuffer("_M8FlowerSymbolArenaRead", _m8FlowerSymbolArena);
            material.SetBuffer("_M8FlowerPageDirectoryRead", _m8FlowerPageDirectory);
        }

        // World clear/OPEN may call this only after retiring prior GPU users.
        // Payloads need no clearing: allocation is unpublished until completely
        // initialized. Reset only the compact free/allocated bitmaps and hints.
        private void ResetFlowerPagesAfterRetirement()
        {
            if (_m8FlowerDetailPages == null) return;
            _m8FlowerDetailPages.SetData(new uint[
                MerkabaFlowerGpuLayout.DetailArenaControl / 4]);
            InitializeFlowerArena(_m8FlowerDetailPages,
                MerkabaFlowerGpuLayout.DetailArenaControl, MerkabaFlowerGpuLayout.PersistentOrder);
            InitializeFlowerArena(_m8ThreadAtlasPages,
                MerkabaFlowerGpuLayout.ThreadArenaControl, MerkabaFlowerGpuLayout.PersistentOrder);
            _m8FlowerPageDirectory.SetData(new uint[
                MerkabaFlowerGpuLayout.SymbolArenaControl / 4]);
            InitializeFlowerArena(_m8FlowerPageDirectory,
                MerkabaFlowerGpuLayout.SymbolArenaControl, MerkabaFlowerGpuLayout.DrawOrder);
            if (_m8FlowerSymbolArena != null)
            {
                InitializeFlowerArena(_m8ThreadAtlasPages,
                    MerkabaFlowerGpuLayout.DrawArenaControl, MerkabaFlowerGpuLayout.DrawOrder);
                _m8FlowerIndirectCommands.SetData(new uint[] { 0u }, 0,
                    MerkabaFlowerGpuLayout.IndirectCountOffset / 4, 1);
                _m8FlowerIndirectCommands.SetData(new uint[] { 0u, 1u, 1u, 0u }, 0,
                    MerkabaFlowerGpuLayout.DirtyBatchDispatchOffset / 4, 4);
            }
        }

        private static void InitializeFlowerArena(ComputeBuffer buffer, int control, int order)
        {
            uint[] initial = MerkabaFlowerGpuLayout.CreateArenaInitialWords(order);
            buffer.SetData(initial, 0, control / 4, initial.Length);
        }

        internal void BindFlowerPages(ComputeShader shader, int kernel)
        {
            if (_m8FlowerDetailPages == null)
                throw new InvalidOperationException("Flower resources have not been allocated.");
            shader.SetBuffer(kernel, "_M8FlowerDetailPages", _m8FlowerDetailPages);
            shader.SetBuffer(kernel, "_M8FlowerDetailPagesRead", _m8FlowerDetailPages);
            shader.SetBuffer(kernel, "_M8ThreadAtlasPages", _m8ThreadAtlasPages);
            shader.SetBuffer(kernel, "_M8ThreadAtlasPagesRead", _m8ThreadAtlasPages);
            shader.SetBuffer(kernel, "_M8FlowerPageDirectory", _m8FlowerPageDirectory);
            shader.SetBuffer(kernel, "_M8FlowerPageDirectoryRead", _m8FlowerPageDirectory);
            if (_m8FlowerSymbolArena != null)
            {
                shader.SetBuffer(kernel, "_M8FlowerSymbolArena", _m8FlowerSymbolArena);
                shader.SetBuffer(kernel, "_M8FlowerSymbolArenaRead", _m8FlowerSymbolArena);
                shader.SetBuffer(kernel, "_M8FlowerIndirectCommands", _m8FlowerIndirectCommands);
            }
        }

        private void ForgetFlowerPagesAfterRelease()
        {
            _m8FlowerDetailPages = null;
            _m8ThreadAtlasPages = null;
            _m8FlowerSymbolArena = null;
            _m8FlowerPageDirectory = null;
            _m8FlowerIndirectCommands = null;
        }
    }
}
