using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// Stable GPU-resident diagnostic ABI. These are reconstruction counters/gauges,
    /// never CPU-side control inputs. A detached snapshot may read them asynchronously.
    /// </summary>
    public enum PressureManifoldDiagnostic : uint
    {
        SpawnCandidates = 0,
        CandidateUnions = 1,
        CanonicalFilmsCreated = 2,
        MeasuredPredictionPixels = 3,
        LatentPredictionPixels = 4,
        ActiveManifolds = 5,
        ConfirmedContinuations = 6,
        OuterFrontierHalfEdges = 7,
        UnpairedActiveEdges = 8,
        StaleTopologyEndpoints = 9,
        RejectedContinuations = 10,
        SplitCount = 11,
        MergeCount = 12,
        RejectedScreenAdjacencyLinks = 13,
        UnsupportedMeasuredTriangles = 14,
        MeshletAllocationOverflow = 15
    }

    [Flags]
    public enum PressureManifoldFlags : uint
    {
        None = 0,
        Active = 1u << 0,
        Closed = 1u << 1,
        DirtyTopology = 1u << 2,
        RestoredUnlinked = 1u << 3
    }

    [Flags]
    public enum FilmMembershipFlags : uint
    {
        None = 0,
        Active = 1u << 0,
        Measured = 1u << 1,
        TopologyValid = 1u << 2,
        TopologyOpen = 1u << 3,
        HasTopology = 1u << 4
    }

    public enum SurfaceHalfEdgeRelation : uint
    {
        Invalid = 0,
        Smooth = 1,
        Crease = 2,
        Occlusion = 3,
        OuterFrontier = 4
    }

    [Flags]
    public enum CrossChunkTopologyPortalFlags : uint
    {
        None = 0,
        Active = 1u << 0,
        OwnerResident = 1u << 1,
        RemoteResident = 1u << 2,
        Matched = 1u << 3,
        Ghost = 1u << 4,
        Dirty = 1u << 5
    }

    [Flags]
    public enum SurfaceHalfEdgeFlags : uint
    {
        None = 0,
        Active = 1u << 0,
        Measured = 1u << 1,
        TwinConfirmed = 1u << 2,
        Outer = 1u << 3,
        Dirty = 1u << 4,
        CrossChunkPortal = 1u << 5,
        Retired = 1u << 6
    }

    [Flags]
    public enum FrontierLoopFlags : uint
    {
        None = 0,
        Active = 1u << 0,
        Ordered = 1u << 1,
        Outer = 1u << 2,
        LatentTopologyOnly = 1u << 3,
        Retired = 1u << 4,
        Inner = 1u << 5
    }

    [Flags]
    public enum ContinuationEvidenceFlags : uint
    {
        None = 0,
        GeometricCoincidence = 1u << 0,
        SidednessCompatible = 1u << 1,
        FirstHitConsistent = 1u << 2,
        VisibilityConsistent = 1u << 3,
        IndependentViews = 1u << 4,
        PoseCalibrationAccepted = 1u << 5,
        BoundaryShared = 1u << 6,
        BoundaryExcluded = 1u << 7,
        Committable = 1u << 8
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PressureManifoldHeaderGpu
    {
        public uint Id;
        public uint Generation;
        public uint RootFrameId;
        public uint Flags;
        public Vector3 OpticalSeed;
        public float SeedSigma;
        public uint MembershipStart;
        public uint MembershipCount;
        public uint HalfEdgeStart;
        public uint HalfEdgeCount;
        public uint FrontierLoopStart;
        public uint FrontierLoopCount;
        public uint Revision;
        public uint CalibrationEpochLow;
        public uint CalibrationEpochHigh;
        public uint TopologyGeneration;
        public uint ElasticRevision;
        public uint Reserved;

        public const int Stride = 80;
    }

    /// <summary>
    /// Generation-safe film-to-manifold ownership. The live buffer is indexed by the
    /// ContactFilm slot, so a reused ID can never inherit a stale membership merely
    /// because allocator order changed.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FilmMembershipGpu
    {
        public uint FilmId;
        public uint FilmGeneration;
        public uint ManifoldId;
        public uint ManifoldGeneration;
        public uint FirstHalfEdge;
        public uint HalfEdgeCount;
        public uint FirstFrontierLoop;
        public uint FrontierLoopCount;
        public uint Flags;
        public uint Revision;

        public const int Stride = 40;
    }

    /// <summary>
    /// One oriented segment extracted from the measured support field. UV endpoints
    /// are arbitrary marching-squares intersections; chart rectangle edges carry no
    /// special meaning. Segments are provisional until welded into half-edges.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SupportContourSegmentGpu
    {
        public uint Id;
        public uint Generation;
        public uint FilmId;
        public uint FilmGeneration;
        public uint ManifoldId;
        public uint ManifoldGeneration;
        public uint CellKey;
        public uint Flags;
        public Vector4 Uv01;
        public float Sigma;
        public float Support;
        public float Confidence;
        public float Bandwidth;

        public const int Stride = 64;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SupportContourPageGpu
    {
        public uint Id;
        public uint Generation;
        public uint FilmId;
        public uint FilmGeneration;
        public uint NextPageId;
        public uint NextPageGeneration;
        public uint SegmentCount;
        public uint Flags;

        public const int Stride = 32;
    }

    /// <summary>
    /// Canonical oriented topology edge. Connectivity is independent from chart
    /// parameter bounds and from storage chunk ownership.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SurfaceHalfEdgeGpu
    {
        public uint Id;
        public uint Generation;
        public uint ManifoldId;
        public uint ManifoldGeneration;
        public uint FilmId;
        public uint FilmGeneration;
        public uint ContourSegmentId;
        public uint ContourSegmentGeneration;
        public uint TwinId;
        public uint TwinGeneration;
        public uint NextId;
        public uint NextGeneration;
        public uint PreviousId;
        public uint PreviousGeneration;
        public uint BoundaryId;
        public uint BoundaryGeneration;
        public uint EvidenceId;
        public uint EvidenceGeneration;
        public uint FrontierLoopId;
        public uint FrontierLoopGeneration;
        public uint Relation;
        public uint Flags;
        public uint Revision;
        public uint Reserved;

        public const int Stride = 96;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FrontierLoopGpu
    {
        public uint Id;
        public uint Generation;
        public uint ManifoldId;
        public uint ManifoldGeneration;
        public uint FirstHalfEdgeId;
        public uint FirstHalfEdgeGeneration;
        public uint HalfEdgeCount;
        public uint Flags;
        public Vector3 LatentAnchor;
        public float SignedArea;
        public float Sigma;
        public float Support;
        public float Confidence;
        public uint Revision;

        public const int Stride = 64;
    }

    /// <summary>
    /// Evidence is canonical input to a topology decision. Multi-view is a proven
    /// property encoded by IndependentViewMask, never a label inferred from distance.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ContinuationEvidenceGpu
    {
        public uint Id;
        public uint Generation;
        public uint FilmA;
        public uint FilmAGeneration;
        public uint FilmB;
        public uint FilmBGeneration;
        public uint BoundaryId;
        public uint BoundaryGeneration;
        public float PositionResidual;
        public float PositionSigma;
        public float NormalCosine;
        public float SidednessScore;
        public float FirstHitScore;
        public float VisibilityScore;
        public float PoseCalibrationQuality;
        public float Support;
        public uint IndependentViewMask;
        public uint Flags;
        public uint Revision;
        public uint Reserved0;

        public const int Stride = 80;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ElasticChartStateGpu
    {
        public uint FilmId;
        public uint FilmGeneration;
        public uint Revision;
        public uint Flags;
        public float NormalOffset;
        public float NormalizedTiltU;
        public float NormalizedTiltV;
        public float Confidence;

        public const int Stride = 32;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EvidenceAlignedSplitPlanGpu
    {
        public uint ParentFilmIndex;
        public uint ParentFilmGeneration;
        public uint ChildFilmIndex0;
        public uint ChildFilmIndex1;
        public uint ChildGeneration0;
        public uint ChildGeneration1;
        public uint ChildBasePage0;
        public uint ChildBasePage1;
        public uint ParentActiveOrdinal;
        public uint NewActiveOrdinal;
        public uint FirstDirtyOrdinal;
        public uint BoundaryId;
        public uint BoundaryGeneration;
        public uint SplitKind;
        public uint TransactionState;
        public uint Reserved;
        public Vector4 SeparatorUv;
        public Vector2 ChildFractions;
        public float Separation;
        public float Confidence;

        public const int Stride = 96;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CrossChunkTopologyPortalGpu
    {
        public uint Id;
        public uint Generation;
        public uint HalfEdgeId;
        public uint HalfEdgeGeneration;
        public uint RemoteHalfEdgeId;
        public uint RemoteHalfEdgeGeneration;
        public uint OwnerChunkId;
        public uint RemoteChunkId;
        public uint ManifoldId;
        public uint ManifoldGeneration;
        public uint Flags;
        public uint Revision;
        public Vector4 LocalEndpointAndSigma;
        public Vector4 LocalNormalAndBandwidth;
        public Vector4 LocalTangentAndSupport;
        public uint IndependentViewMask;
        public uint EvidenceFlags;
        public float FirstHitScore;
        public float PoseCalibrationQuality;

        public const int Stride = 112;
    }

    /// <summary>
    /// Canonical PressureManifold atlas. The chart rectangle is only a numerical
    /// parameter domain; topology is carried exclusively by measured support
    /// contours, oriented half-edges and ordered frontier loops.
    /// </summary>
    public sealed class PressureManifoldPool : IDisposable
    {
        // next manifold, live manifolds, overflow, generation,
        // live memberships, stale memberships, reserved, revision.
        public const int AllocatorWords = 8;
        public const int DiagnosticWords = 16;
        public const int ContourSegmentsPerPage = 64;

        private static readonly uint[] InitialAllocator =
        {
            0u, 0u, 0u, 1u,
            0u, 0u, 0u, 1u
        };

        public PressureManifoldPool(int filmCapacity, int manifoldCapacity = 1024)
        {
            FilmCapacity = Math.Max(1024, filmCapacity);
            ManifoldCapacity = Math.Max(64, manifoldCapacity);
            ContourSegmentCapacity = NextPowerOfTwo(checked(FilmCapacity * 3));
            ContourPageCapacity = Math.Max(1,
                ContourSegmentCapacity / ContourSegmentsPerPage);
            HalfEdgeCapacity = ContourSegmentCapacity;
            HalfEdgeHashCapacity = NextPowerOfTwo(checked(HalfEdgeCapacity * 2));
            FrontierLoopCapacity = HalfEdgeCapacity;
            PortalCapacity = NextPowerOfTwo(Math.Max(1024, FilmCapacity / 8));
            Headers = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                ManifoldCapacity, PressureManifoldHeaderGpu.Stride);
            Memberships = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                FilmCapacity, FilmMembershipGpu.Stride);
            Allocator = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                AllocatorWords, sizeof(uint));
            Current = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4,
                sizeof(uint));
            Diagnostics = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                DiagnosticWords, sizeof(uint));
            SupportContours = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                ContourSegmentCapacity, SupportContourSegmentGpu.Stride);
            SupportContourPages = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, ContourPageCapacity,
                SupportContourPageGpu.Stride);
            HalfEdges = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                HalfEdgeCapacity, SurfaceHalfEdgeGpu.Stride);
            HalfEdgeHashHeads = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                HalfEdgeHashCapacity, sizeof(uint));
            HalfEdgeHashNext = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                HalfEdgeCapacity, sizeof(uint));
            HalfEdgeHashKeys = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                HalfEdgeCapacity, sizeof(uint));
            EndpointHashHeads = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                HalfEdgeHashCapacity, sizeof(uint));
            EndpointHashEntries = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                checked(HalfEdgeCapacity * 2), sizeof(uint) * 4);
            HalfEdgeLoopParents = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                HalfEdgeCapacity, sizeof(uint));
            HalfEdgeLoopIds = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                HalfEdgeCapacity, sizeof(uint));
            FrontierLoops = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                FrontierLoopCapacity, FrontierLoopGpu.Stride);
            FrontierLoopMoments = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                FrontierLoopCapacity, sizeof(int) * 4);
            ContinuationEvidenceCapacity = HalfEdgeCapacity;
            ContinuationEvidence = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, ContinuationEvidenceCapacity,
                ContinuationEvidenceGpu.Stride);
            ElasticStates = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                FilmCapacity, ElasticChartStateGpu.Stride);
            ElasticGradients = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                FilmCapacity, sizeof(int) * 4);
            ElasticDiagonals = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                FilmCapacity, sizeof(int) * 4);
            SplitProposalKeys = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                FilmCapacity, sizeof(uint));
            SplitPlans = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                FilmCapacity, EvidenceAlignedSplitPlanGpu.Stride);
            SplitRecordIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                FilmCapacity, sizeof(uint));
            SplitState = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8,
                sizeof(uint));
            SplitDispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 4, sizeof(uint) * 3);
            HalfEdgeBoundaryClaims = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, HalfEdgeCapacity, sizeof(uint));
            CrossChunkPortals = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                PortalCapacity, CrossChunkTopologyPortalGpu.Stride);
            PortalPlans = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                HalfEdgeCapacity, sizeof(uint));
            PortalState = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8,
                sizeof(uint));
            PortalDispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 4, sizeof(uint) * 3);
            FilmTopologyRanges = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                FilmCapacity, sizeof(uint) * 4);
            ContourPlans = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                FilmCapacity, sizeof(uint) * 4);
            AtlasAllocator = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                16, sizeof(uint));
            DirtyTopologyFilms = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                FilmCapacity, sizeof(uint));
            TopologyDirtyFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                FilmCapacity, sizeof(uint));
            AtlasDispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 8, sizeof(uint) * 3);
            Allocator.SetData(InitialAllocator);
            Current.SetData(new uint[4]);
            Diagnostics.SetData(new uint[DiagnosticWords]);
            AtlasAllocator.SetData(new uint[16]);
            TopologyDirtyFlags.SetData(new uint[FilmCapacity]);
        }

        public int FilmCapacity { get; }
        public int ManifoldCapacity { get; }
        public int ContourSegmentCapacity { get; }
        public int ContourPageCapacity { get; }
        public int HalfEdgeCapacity { get; }
        public int HalfEdgeHashCapacity { get; }
        public int FrontierLoopCapacity { get; }
        public int ContinuationEvidenceCapacity { get; }
        public int PortalCapacity { get; }
        public GraphicsBuffer Headers { get; private set; }
        public GraphicsBuffer Memberships { get; private set; }
        public GraphicsBuffer Allocator { get; private set; }
        public GraphicsBuffer Current { get; private set; }
        public GraphicsBuffer Diagnostics { get; private set; }
        public GraphicsBuffer SupportContours { get; private set; }
        public GraphicsBuffer SupportContourPages { get; private set; }
        public GraphicsBuffer HalfEdges { get; private set; }
        public GraphicsBuffer HalfEdgeHashHeads { get; private set; }
        public GraphicsBuffer HalfEdgeHashNext { get; private set; }
        public GraphicsBuffer HalfEdgeHashKeys { get; private set; }
        public GraphicsBuffer EndpointHashHeads { get; private set; }
        public GraphicsBuffer EndpointHashEntries { get; private set; }
        public GraphicsBuffer HalfEdgeLoopParents { get; private set; }
        public GraphicsBuffer HalfEdgeLoopIds { get; private set; }
        public GraphicsBuffer FrontierLoops { get; private set; }
        public GraphicsBuffer FrontierLoopMoments { get; private set; }
        public GraphicsBuffer ContinuationEvidence { get; private set; }
        public GraphicsBuffer ElasticStates { get; private set; }
        public GraphicsBuffer ElasticGradients { get; private set; }
        public GraphicsBuffer ElasticDiagonals { get; private set; }
        public GraphicsBuffer SplitProposalKeys { get; private set; }
        public GraphicsBuffer SplitPlans { get; private set; }
        public GraphicsBuffer SplitRecordIndices { get; private set; }
        public GraphicsBuffer SplitState { get; private set; }
        public GraphicsBuffer SplitDispatchArguments { get; private set; }
        public GraphicsBuffer HalfEdgeBoundaryClaims { get; private set; }
        public GraphicsBuffer CrossChunkPortals { get; private set; }
        public GraphicsBuffer PortalPlans { get; private set; }
        public GraphicsBuffer PortalState { get; private set; }
        public GraphicsBuffer PortalDispatchArguments { get; private set; }
        public GraphicsBuffer FilmTopologyRanges { get; private set; }
        public GraphicsBuffer ContourPlans { get; private set; }
        public GraphicsBuffer AtlasAllocator { get; private set; }
        public GraphicsBuffer DirtyTopologyFilms { get; private set; }
        public GraphicsBuffer TopologyDirtyFlags { get; private set; }
        public GraphicsBuffer AtlasDispatchArguments { get; private set; }
        public bool IsDisposed => Headers == null;

        public void Dispose()
        {
            Headers?.Dispose();
            Memberships?.Dispose();
            Allocator?.Dispose();
            Current?.Dispose();
            Diagnostics?.Dispose();
            SupportContours?.Dispose();
            SupportContourPages?.Dispose();
            HalfEdges?.Dispose();
            HalfEdgeHashHeads?.Dispose();
            HalfEdgeHashNext?.Dispose();
            HalfEdgeHashKeys?.Dispose();
            EndpointHashHeads?.Dispose();
            EndpointHashEntries?.Dispose();
            HalfEdgeLoopParents?.Dispose();
            HalfEdgeLoopIds?.Dispose();
            FrontierLoops?.Dispose();
            FrontierLoopMoments?.Dispose();
            ContinuationEvidence?.Dispose();
            ElasticStates?.Dispose();
            ElasticGradients?.Dispose();
            ElasticDiagonals?.Dispose();
            SplitProposalKeys?.Dispose();
            SplitPlans?.Dispose();
            SplitRecordIndices?.Dispose();
            SplitState?.Dispose();
            SplitDispatchArguments?.Dispose();
            HalfEdgeBoundaryClaims?.Dispose();
            CrossChunkPortals?.Dispose();
            PortalPlans?.Dispose();
            PortalState?.Dispose();
            PortalDispatchArguments?.Dispose();
            FilmTopologyRanges?.Dispose();
            ContourPlans?.Dispose();
            AtlasAllocator?.Dispose();
            DirtyTopologyFilms?.Dispose();
            TopologyDirtyFlags?.Dispose();
            AtlasDispatchArguments?.Dispose();
            Headers = null;
            Memberships = null;
            Allocator = null;
            Current = null;
            Diagnostics = null;
            SupportContours = null;
            SupportContourPages = null;
            HalfEdges = null;
            HalfEdgeHashHeads = null;
            HalfEdgeHashNext = null;
            HalfEdgeHashKeys = null;
            EndpointHashHeads = null;
            EndpointHashEntries = null;
            HalfEdgeLoopParents = null;
            HalfEdgeLoopIds = null;
            FrontierLoops = null;
            FrontierLoopMoments = null;
            ContinuationEvidence = null;
            ElasticStates = null;
            ElasticGradients = null;
            ElasticDiagonals = null;
            SplitProposalKeys = null;
            SplitPlans = null;
            SplitRecordIndices = null;
            SplitState = null;
            SplitDispatchArguments = null;
            HalfEdgeBoundaryClaims = null;
            CrossChunkPortals = null;
            PortalPlans = null;
            PortalState = null;
            PortalDispatchArguments = null;
            FilmTopologyRanges = null;
            ContourPlans = null;
            AtlasAllocator = null;
            DirtyTopologyFilms = null;
            TopologyDirtyFlags = null;
            AtlasDispatchArguments = null;
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value && result < 1 << 30) result <<= 1;
            return result;
        }
    }
}
