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
        public uint FirstChildFilmIndex;
        public uint FirstChildBasePage;
        public uint ChildCount;

        public const int Stride = 16;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TopologyMergeRecordGpu
    {
        public uint FilmAIndex;
        public uint FilmBIndex;
        public uint MergedFilmIndex;
        public uint MergedBasePage;
        public uint Reserved0;
        public uint Reserved1;
        public uint Reserved2;
        public uint Reserved3;

        public const int Stride = 32;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FilmMergeHashEntryGpu
    {
        public int CellX;
        public int CellY;
        public int CellZ;
        public uint FilmId;
        public uint Generation;
        public uint Hash;

        public const int Stride = 24;
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
