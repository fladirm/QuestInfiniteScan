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
        ManifoldLinks = 6,
        OuterFrontierSegments = 7,
        UnpairedActiveEdges = 8,
        StaleLinkEndpoints = 9,
        RejectedCanonicalLinks = 10,
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
        Measured = 1u << 1
    }

    public enum ManifoldLinkType : uint
    {
        Invalid = 0,
        Smooth = 1,
        Crease = 2,
        OcclusionFold = 3,
        Latent = 4
    }

    [Flags]
    public enum ManifoldLinkFlags : uint
    {
        None = 0,
        Active = 1u << 0,
        MultiViewConfirmed = 1u << 1,
        Dirty = 1u << 2,
        Retired = 1u << 3
    }

    [Flags]
    public enum LatentFrontierFlags : uint
    {
        None = 0,
        Active = 1u << 0,
        Outer = 1u << 1,
        Ordered = 1u << 2,
        Retired = 1u << 3
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PressureManifoldHeaderGpu
    {
        public uint Id;
        public uint Generation;
        public uint ChunkId;
        public uint Flags;
        public Vector3 OpticalSeed;
        public float SeedSigma;
        public uint MembershipStart;
        public uint MembershipCount;
        public uint LinkStart;
        public uint LinkCount;
        public uint FrontierStart;
        public uint FrontierCount;
        public uint Revision;
        public uint CalibrationEpochLow;
        public uint CalibrationEpochHigh;
        public uint Reserved0;
        public uint Reserved1;
        public uint Reserved2;

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
        public uint FirstLink;
        public uint LinkCount;
        public uint FirstFrontier;
        public uint FrontierCount;
        public uint Flags;
        public uint Revision;

        public const int Stride = 40;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ManifoldLinkGpu
    {
        public uint Id;
        public uint Generation;
        public uint ManifoldId;
        public uint ManifoldGeneration;
        public uint FilmA;
        public uint FilmAGeneration;
        public uint FilmB;
        public uint FilmBGeneration;
        public uint Type;
        public uint Flags;
        public uint BoundaryId;
        public uint Revision;
        public Vector4 UvA01;
        public Vector4 UvB01;
        public float Sigma;
        public float Support;
        public float Confidence;
        public float Reserved;

        public const int Stride = 96;
    }

    /// <summary>
    /// One generation-safe endpoint incidence. Every ManifoldLink owns exactly two
    /// fixed incidence records (A/B); memberships point to a linked list of these
    /// records rather than pretending globally allocated links are contiguous.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ManifoldLinkIncidenceGpu
    {
        public uint Id;
        public uint Generation;
        public uint LinkId;
        public uint LinkGeneration;
        public uint FilmId;
        public uint FilmGeneration;
        public uint NextId;
        public uint NextGeneration;
        public uint Endpoint;
        public uint Flags;

        public const int Stride = 40;
    }

    /// <summary>
    /// Generation-safe ownership of one latent frontier segment by one film.
    /// Frontier next/previous fields order the manifold's outer loops and may cross
    /// film boundaries after a splice; this separate incidence chain therefore owns
    /// per-film enumeration without confusing contour order with storage adjacency.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ManifoldFrontierIncidenceGpu
    {
        public uint Id;
        public uint Generation;
        public uint FrontierId;
        public uint FrontierGeneration;
        public uint FilmId;
        public uint FilmGeneration;
        public uint NextId;
        public uint NextGeneration;
        public uint Flags;
        public uint Reserved;

        public const int Stride = 40;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LatentFrontierSegmentGpu
    {
        public uint Id;
        public uint Generation;
        public uint ManifoldId;
        public uint ManifoldGeneration;
        public uint FilmId;
        public uint FilmGeneration;
        public uint NextId;
        public uint NextGeneration;
        public uint PreviousId;
        public uint PreviousGeneration;
        public uint Flags;
        public uint Revision;
        public Vector4 Uv01;
        public float Sigma;
        public float Support;
        public float Confidence;
        public float Reserved;

        public const int Stride = 80;
    }

    /// <summary>
    /// Canonical closed-manifold graph. All arrays are bounded GPU buffers; the live
    /// reconstruction never reads counts back to the CPU. Allocator words are:
    /// manifold next/live/overflow/generation, link next/live/overflow/generation,
    /// frontier next/live/overflow/generation, membership live/stale/unpaired/revision.
    /// </summary>
    public sealed class PressureManifoldPool : IDisposable
    {
        public const int AllocatorWords = 16;
        public const int DiagnosticWords = 16;

        private static readonly uint[] InitialAllocator =
        {
            0u, 0u, 0u, 1u,
            0u, 0u, 0u, 1u,
            0u, 0u, 0u, 1u,
            0u, 0u, 0u, 1u
        };

        public PressureManifoldPool(int filmCapacity, int manifoldCapacity = 1024)
        {
            FilmCapacity = Math.Max(1024, filmCapacity);
            ManifoldCapacity = Math.Max(64, manifoldCapacity);
            LinkCapacity = checked(FilmCapacity * 2);
            FrontierCapacity = checked(FilmCapacity * 4);
            Headers = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                ManifoldCapacity, PressureManifoldHeaderGpu.Stride);
            Memberships = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                FilmCapacity, FilmMembershipGpu.Stride);
            Links = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                LinkCapacity, ManifoldLinkGpu.Stride);
            LinkIncidences = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                checked(LinkCapacity * 2), ManifoldLinkIncidenceGpu.Stride);
            LinkHashCapacity = NextPowerOfTwo(checked(LinkCapacity * 2));
            LinkHash = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                LinkHashCapacity, sizeof(uint) * 2);
            Frontiers = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                FrontierCapacity, LatentFrontierSegmentGpu.Stride);
            FrontierIncidences = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                FrontierCapacity, ManifoldFrontierIncidenceGpu.Stride);
            Allocator = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                AllocatorWords, sizeof(uint));
            Current = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4,
                sizeof(uint));
            Diagnostics = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                DiagnosticWords, sizeof(uint));
            Allocator.SetData(InitialAllocator);
            Current.SetData(new uint[4]);
            Diagnostics.SetData(new uint[DiagnosticWords]);
        }

        public int FilmCapacity { get; }
        public int ManifoldCapacity { get; }
        public int LinkCapacity { get; }
        public int LinkHashCapacity { get; }
        public int FrontierCapacity { get; }
        public GraphicsBuffer Headers { get; private set; }
        public GraphicsBuffer Memberships { get; private set; }
        public GraphicsBuffer Links { get; private set; }
        public GraphicsBuffer LinkIncidences { get; private set; }
        public GraphicsBuffer LinkHash { get; private set; }
        public GraphicsBuffer Frontiers { get; private set; }
        public GraphicsBuffer FrontierIncidences { get; private set; }
        public GraphicsBuffer Allocator { get; private set; }
        public GraphicsBuffer Current { get; private set; }
        public GraphicsBuffer Diagnostics { get; private set; }
        public bool IsDisposed => Headers == null;

        public void Dispose()
        {
            Headers?.Dispose();
            Memberships?.Dispose();
            Links?.Dispose();
            LinkIncidences?.Dispose();
            LinkHash?.Dispose();
            Frontiers?.Dispose();
            FrontierIncidences?.Dispose();
            Allocator?.Dispose();
            Current?.Dispose();
            Diagnostics?.Dispose();
            Headers = null;
            Memberships = null;
            Links = null;
            LinkIncidences = null;
            LinkHash = null;
            Frontiers = null;
            FrontierIncidences = null;
            Allocator = null;
            Current = null;
            Diagnostics = null;
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value && result < 1 << 30) result <<= 1;
            return result;
        }
    }
}
