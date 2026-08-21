using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    [Flags]
    public enum DisplacementPageFlags : uint
    {
        None = 0,
        Active = 1u << 0,
        BaseGrid16 = 1u << 1,
        MicroGrid8 = 1u << 2,
        Dirty = 1u << 3
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DisplacementPageHeaderGpu
    {
        public uint Id;
        public uint Generation;
        public uint FilmId;
        public uint FilmGeneration;
        public uint Level;
        public uint ParentPage;
        public uint ParentCell;
        public uint Flags;
        public Vector4 UvBounds;
        public uint BestFootprintBits;
        public uint MaximumVarianceBits;
        public uint SupportFixed;
        public uint Revision;

        public const int Stride = 64;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DisplacementCellGpu
    {
        public float Displacement;
        public float Sigma;
        public float Support;
        public float Coverage;
        public float BestPrecision;
        public float BestFootprint;
        public float ResidualVariance;
        // Persistent opposing pre-hit pressure (legacy serialized field name).
        // Unlike a frame accumulator it survives revisits/persistence and must do
        // work against the baked contact posterior before it can displace the
        // conserved film. It never erodes coverage or topology.
        public float PreHitPressure;
        public uint PreHitPressureViewMask;
        public uint Revision;

        public const int Stride = 40;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ContactTopologyEvidenceGpu
    {
        public float PositiveMoment;
        public float PositiveSupport;
        public float NegativeMoment;
        public float NegativeSupport;
        public float ResidualVariance;
        public float BoundarySupport;
        public float TotalSupport;
        public uint Revision;

        public const int Stride = 32;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TopologySplitRecordGpu
    {
        public uint ParentFilmIndex;
        public uint ParentFilmGeneration;
        public uint ChildFilmIndex0;
        public uint ChildFilmIndex1;
        public uint ChildFilmIndex2;
        public uint ChildFilmIndex3;
        public uint FirstChildBasePage;
        public uint ChildCount;
        public uint ParentActiveOrdinal;
        public uint FirstNewActiveOrdinal;
        public uint FirstDirtyOrdinal;
        public uint ReservedLinkStart;
        public uint ReservedExternalLinkCount;
        public uint ReservedLinkCount;
        public uint ReservedFrontierStart;
        public uint ReservedFrontierCount;
        public uint ReservedBoundaryStart;
        public uint ReservedBoundaryCount;
        public uint TransactionState;

        public const int Stride = 76;
    }

    /// <summary>
    /// Exact per-boundary write plan produced by the single split transaction
    /// planner. TransferSplitBoundaries never allocates; it only consumes this
    /// generation-tagged plan after the owning split record commits.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TopologyBoundarySplitPlanGpu
    {
        public uint BoundaryIndex;
        public uint BoundaryGeneration;
        public uint SplitRecordIndex;
        public uint ParentFilmIndex;
        public uint ParentFilmGeneration;
        public uint ParentEndpoint;
        public uint ReservedStart;
        public uint SegmentCount;

        public const int Stride = 32;
    }

    /// <summary>
    /// Segmented sparse detail hierarchy. Base Grid16 and recursive Grid8 microtiles
    /// deliberately live in separate buffers, keeping every Vulkan storage binding
    /// below 128 MiB while allowing detail allocation to follow measured footprint.
    /// </summary>
    public sealed class ContactDisplacementPool : IDisposable
    {
        public const int BaseCellsPerPage = 16 * 16;
        public const int MicroCellsPerPage = 8 * 8;
        // Per-frame contact W/sum/sum2, coverage, pressure view mask,
        // best precision/footprint, support, and opposing-pressure W/sum/sum2.
        // The base and micro arenas are separate Vulkan storage bindings so the
        // default high-detail pool never crosses Quest's 128 MiB binding limit.
        public const int TransientAccumulatorWordsPerCell = 11;

        private static readonly uint[] InitialAllocator =
            { 0u, 0u, 0u, 0u, 1u, 0u, 0u, 0u };

        public ContactDisplacementPool(int filmCapacity, int basePageCapacity,
            int microPageCapacity)
        {
            FilmCapacity = Math.Max(1024, filmCapacity);
            BasePageCapacity = Math.Max(256, basePageCapacity);
            MicroPageCapacity = Math.Max(1024, microPageCapacity);
            BaseCellCapacity = checked(BasePageCapacity * BaseCellsPerPage);
            MicroCellCapacity = checked(MicroPageCapacity * MicroCellsPerPage);
            PageHeaders = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                checked(BasePageCapacity + MicroPageCapacity),
                DisplacementPageHeaderGpu.Stride);
            BaseCells = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                BaseCellCapacity, DisplacementCellGpu.Stride);
            MicroCells = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                MicroCellCapacity, DisplacementCellGpu.Stride);
            BaseChildPages = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                BaseCellCapacity, sizeof(uint));
            MicroChildPages = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                MicroCellCapacity, sizeof(uint));
            TopologyEvidence = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                FilmCapacity, ContactTopologyEvidenceGpu.Stride);
            Allocator = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                InitialAllocator.Length, sizeof(uint));
            Allocator.SetData(InitialAllocator);
        }

        public int FilmCapacity { get; }
        public int BasePageCapacity { get; }
        public int MicroPageCapacity { get; }
        public int BaseCellCapacity { get; }
        public int MicroCellCapacity { get; }
        public int TotalCellCapacity => checked(BaseCellCapacity + MicroCellCapacity);
        public GraphicsBuffer PageHeaders { get; private set; }
        public GraphicsBuffer BaseCells { get; private set; }
        public GraphicsBuffer MicroCells { get; private set; }
        public GraphicsBuffer BaseChildPages { get; private set; }
        public GraphicsBuffer MicroChildPages { get; private set; }
        public GraphicsBuffer TopologyEvidence { get; private set; }
        /// <summary>base next, micro next, overflows, generation, diagnostics.</summary>
        public GraphicsBuffer Allocator { get; private set; }
        public bool IsDisposed => PageHeaders == null;

        public void Dispose()
        {
            PageHeaders?.Dispose();
            BaseCells?.Dispose();
            MicroCells?.Dispose();
            BaseChildPages?.Dispose();
            MicroChildPages?.Dispose();
            TopologyEvidence?.Dispose();
            Allocator?.Dispose();
            PageHeaders = null;
            BaseCells = null;
            MicroCells = null;
            BaseChildPages = null;
            MicroChildPages = null;
            TopologyEvidence = null;
            Allocator = null;
        }
    }
}
