using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    [Flags]
    public enum ContactBoundaryFlags : uint
    {
        None = 0,
        Active = 1u << 0,
        Uncertain = 1u << 1,
        Dirty = 1u << 2,
        Persistent = 1u << 3,
        Retired = 1u << 4,
        MultiView = 1u << 5,
        // Boundary observations never own physical adjacency. Canonical adjacency
        // and outer latent closure live exclusively in PressureManifoldPool.
        ReservedObservationClass0 = 1u << 6,
        ReservedObservationClass1 = 1u << 7
    }

    [Flags]
    public enum BoundaryCurveTopologyFlags : uint
    {
        None = 0,
        Active = 1u << 0,
        LeftIncident = 1u << 1,
        RightIncident = 1u << 2,
        Shared = 1u << 3,
        MultiView = 1u << 4,
        Crease = 1u << 5,
        Occlusion = 1u << 6,
        DirtyCache = 1u << 7
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ContactBoundaryHeaderGpu
    {
        public uint Id;
        public uint Generation;
        public uint ChunkId;
        public uint Flags;
        public uint FilmA;
        public uint FilmAGeneration;
        public uint FilmB;
        public uint FilmBGeneration;
        public Vector4 ControlUv01;
        public Vector4 ControlUv23;
        public float Sigma;
        public float Support;
        public float Confidence;
        public float Contradiction;
        public uint Revision;
        public uint CellKey;
        public uint ViewBinMask;
        public uint LastSeenSequence;

        public const int Stride = 96;
    }

    /// <summary>
    /// Shared atlas incidence for one canonical world-space BoundaryCurve.  The
    /// boundary header owns the spline posterior; this record owns both oriented
    /// incident half-edges and their evidence.  It is indexed by boundary slot.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BoundaryCurveTopologyGpu
    {
        public uint BoundaryId;
        public uint BoundaryGeneration;
        public uint ManifoldId;
        public uint ManifoldGeneration;
        public uint LeftHalfEdgeId;
        public uint LeftHalfEdgeGeneration;
        public uint RightHalfEdgeId;
        public uint RightHalfEdgeGeneration;
        public uint LeftFilmId;
        public uint LeftFilmGeneration;
        public uint RightFilmId;
        public uint RightFilmGeneration;
        public uint CellKeyA;
        public uint CellKeyB;
        public uint IndependentViewMask;
        public uint Flags;
        public uint Revision;
        public uint CacheGeneration;
        public float PositionResidual;
        public float PositionSigma;
        public float FirstHitScore;
        public float VisibilityScore;
        public float PoseCalibrationQuality;
        public float SidednessScore;

        public const int Stride = 96;
    }

    /// <summary>
    /// Derived boundary/cell intersection cache. Four adaptively flattened UV
    /// segments are stored for each incident chart; world geometry remains the one
    /// canonical spline in ContactBoundary.Information records 3..6.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BoundaryCurveCacheGpu
    {
        public uint BoundaryId;
        public uint BoundaryGeneration;
        public uint FilmA;
        public uint FilmAGeneration;
        public uint FilmB;
        public uint FilmBGeneration;
        public uint Flags;
        public uint Revision;
        public Vector4 SegmentA0;
        public Vector4 SegmentA1;
        public Vector4 SegmentA2;
        public Vector4 SegmentA3;
        public Vector4 SegmentB0;
        public Vector4 SegmentB1;
        public Vector4 SegmentB2;
        public Vector4 SegmentB3;

        public const int Stride = 160;
    }

    /// <summary>
    /// Sparse GPU-resident ContactBoundary graph keyed by film generation and local
    /// UV cell.  Curves remain canonical in film surface coordinates, so film
    /// refinement moves their derived 3D geometry without detaching the edge.
    /// </summary>
    public sealed class ContactBoundaryPool : IDisposable
    {
        private static readonly uint[] InitialAllocator = { 0u, 0u, 0u, 1u };
        public const int InformationRecordsPerBoundary = 9;
        public const int HashEntryStride = sizeof(uint) * 5;

        public ContactBoundaryPool(int capacity, int hashCapacity)
        {
            Capacity = Math.Max(1024, capacity);
            HashCapacity = NextPowerOfTwo(Math.Max(Capacity * 2, hashCapacity));
            Headers = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                Capacity, ContactBoundaryHeaderGpu.Stride);
            // UV posterior, evidence/quality, four canonical 3D cubic controls and
            // multi-view/retirement state. These are resumable canonical data, not a
            // transient render cache.
            Information = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                checked(Capacity * InformationRecordsPerBoundary), sizeof(float) * 4);
            HashEntries = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                HashCapacity, HashEntryStride);
            Allocator = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                4, sizeof(uint));
            Topology = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                Capacity, BoundaryCurveTopologyGpu.Stride);
            CurveCache = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                Capacity, BoundaryCurveCacheGpu.Stride);
            Allocator.SetData(InitialAllocator);
        }

        public int Capacity { get; }
        public int HashCapacity { get; }
        public GraphicsBuffer Headers { get; private set; }
        public GraphicsBuffer Information { get; private set; }
        public GraphicsBuffer HashEntries { get; private set; }
        public GraphicsBuffer Allocator { get; private set; }
        public GraphicsBuffer Topology { get; private set; }
        public GraphicsBuffer CurveCache { get; private set; }
        public bool IsDisposed => Headers == null;

        public void Dispose()
        {
            Headers?.Dispose();
            Information?.Dispose();
            HashEntries?.Dispose();
            Allocator?.Dispose();
            Topology?.Dispose();
            CurveCache?.Dispose();
            Headers = null;
            Information = null;
            HashEntries = null;
            Allocator = null;
            Topology = null;
            CurveCache = null;
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value && result < 1 << 30) result <<= 1;
            return result;
        }
    }
}
