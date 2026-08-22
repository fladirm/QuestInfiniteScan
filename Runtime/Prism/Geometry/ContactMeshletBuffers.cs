using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// Derived vertex semantics. These bits are deliberately unrelated to
    /// <see cref="ContactFilmFlags"/>; copying canonical flags into a mesh vertex is
    /// an ABI violation because derived material and canonical lifecycle are
    /// different state machines.
    /// </summary>
    [Flags]
    public enum ContactMeshletVertexFlags : uint
    {
        None = 0,
        MeasuredContact = 1u << 0,
        Boundary = 1u << 1,
        LatentConnector = 1u << 2,
        LatentFrontier = 1u << 3
    }

    /// <summary>Typed flags for one derived meshlet descriptor.</summary>
    [Flags]
    public enum ContactMeshletDescriptorFlags : uint
    {
        None = 0,
        MeasuredSurface = 1u << 0,
        HasBoundary = 1u << 1,
        ExactTopology = 1u << 2,
        ElasticConnector = 1u << 3,
        LatentFrontier = 1u << 4
    }

    /// <summary>Per-view culling result; never persisted as canonical state.</summary>
    [Flags]
    public enum ContactMeshletViewFlags : uint
    {
        None = 0,
        Visible = 1u << 0,
        Occluded = 1u << 1,
        FrustumRejected = 1u << 2,
        Overflow = 1u << 3
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ContactMeshletVertexGpu
    {
        public Vector3 Position;
        // Film id, flags, sidedness and coverage share one 32-bit identity word.
        // The live film pool has at most 65,536 slots, so 17 id bits preserve the
        // full generation-tagged id domain without truncation.
        public uint PackedFilmMaterial;
        // Derived display data is packed; canonical ContactFilm precision is
        // unchanged. Keeping the three-million-vertex arena below Android's
        // 128 MiB single-GraphicsBuffer limit also improves vertex bandwidth.
        public uint PackedNormal;
        public uint Generation;
        public uint PackedUv;
        public uint PackedSigmaConfidence;

        // A 32-byte / 16-byte-aligned stride is invariant across D3D structured
        // layout and Vulkan std430. A 44-byte stride can be rounded to 48 bytes by
        // SPIR-V drivers, causing every vertex after the first to be misaddressed.
        public const int Stride = 32;

        public static uint PackNormal(Vector3 value)
        {
            if (value.sqrMagnitude < 1e-20f) value = Vector3.forward;
            value.Normalize();
            float inverseL1 = 1f / Mathf.Max(1e-20f,
                Mathf.Abs(value.x) + Mathf.Abs(value.y) + Mathf.Abs(value.z));
            float x = value.x * inverseL1;
            float y = value.y * inverseL1;
            if (value.z < 0f)
            {
                float oldX = x;
                x = (1f - Mathf.Abs(y)) * (oldX >= 0f ? 1f : -1f);
                y = (1f - Mathf.Abs(oldX)) * (y >= 0f ? 1f : -1f);
            }
            int encodedX = Mathf.Clamp(Mathf.RoundToInt(x * 32767f),
                -32767, 32767);
            int encodedY = Mathf.Clamp(Mathf.RoundToInt(y * 32767f),
                -32767, 32767);
            return (uint)(encodedX & 0xffff) |
                   ((uint)(encodedY & 0xffff) << 16);
        }

        public static Vector3 UnpackNormal(uint packed)
        {
            float x = (short)(packed & 0xffffu) / 32767f;
            float y = (short)(packed >> 16) / 32767f;
            var result = new Vector3(x, y,
                1f - Mathf.Abs(x) - Mathf.Abs(y));
            if (result.z < 0f)
            {
                float oldX = result.x;
                result.x = (1f - Mathf.Abs(result.y)) *
                           (oldX >= 0f ? 1f : -1f);
                result.y = (1f - Mathf.Abs(oldX)) *
                           (result.y >= 0f ? 1f : -1f);
            }
            return result.sqrMagnitude > 1e-20f
                ? result.normalized
                : Vector3.forward;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ContactMeshletDescriptorGpu
    {
        public uint FilmId;
        public uint Generation;
        public uint VertexBase;
        public uint MaximumSegments;
        public Vector3 Center;
        public float Radius;
        public Vector4 UvBounds;
        public float GeometricError;
        public float DetailFootprint;
        public float Confidence;
        public uint AppearancePage;
        public uint Flags;
        public uint SourceIndexBase;
        public uint SourceIndexCount;
        public uint Reserved;

        public const int Stride = 80;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ContactMeshletViewLodGpu
    {
        public uint GeometrySegments;
        public float AppearanceMip;
        public float ProjectedPixels;
        public uint Flags;

        public const int Stride = 16;
    }

    /// <summary>
    /// Stable derived arena ownership for one generation-tagged ContactFilm. Counts
    /// may change inside the reserved ranges without repacking unrelated films.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ContactFilmMeshletRangeGpu
    {
        public uint FilmId;
        public uint FilmGeneration;
        public uint FilmRevision;
        public uint Flags;
        public uint VertexOffset;
        public uint VertexCapacity;
        public uint IndexOffset;
        public uint IndexCapacity;
        public uint DescriptorOffset;
        public uint DescriptorCapacity;
        public uint MaximumSegments;
        public uint Reserved;

        public const int Stride = 48;
    }

    /// <summary>
    /// One immutable, generation-tagged meshlet publication. The builder only writes
    /// the inactive generation. A published generation is never mutated while a
    /// prediction or preview draw can still reference it.
    /// </summary>
    public sealed class ContactMeshletGenerationBuffers : IDisposable
    {
        private static readonly uint[] EmptyIndirectArgs = { 0u, 1u, 0u, 0u };
        private static readonly uint[] EmptyBuildDispatchArgs =
        {
            0u, 1u, 1u,
            0u, 1u, 1u,
            0u, 1u, 1u,
            0u, 1u, 1u,
            0u, 1u, 1u
        };
        private static readonly uint[] EmptyCullDispatchArgs = { 0u, 1u, 1u };
        private static readonly uint[] EmptyCounters = new uint[8];

        internal ContactMeshletGenerationBuffers(int vertexCapacity,
            int indexCapacity, int descriptorCapacity, int filmCapacity = 1)
        {
            VertexCapacity = Math.Max(1, vertexCapacity);
            IndexCapacity = Math.Max(1, indexCapacity);
            DescriptorCapacity = Math.Max(1, descriptorCapacity);
            FilmCapacity = Math.Max(1, filmCapacity);
            Vertices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                VertexCapacity, ContactMeshletVertexGpu.Stride);
            Indices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                IndexCapacity, sizeof(uint));
            Descriptors = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                DescriptorCapacity, ContactMeshletDescriptorGpu.Stride);
            FilmRanges = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                FilmCapacity, ContactFilmMeshletRangeGpu.Stride);
            DrawArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured,
                1, sizeof(uint) * 4);
            BuildCounters = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                EmptyCounters.Length, sizeof(uint));
            BuildDispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured,
                5, sizeof(uint) * 3);
            CullDispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured,
                1, sizeof(uint) * 3);
            DrawArguments.SetData(EmptyIndirectArgs);
            BuildCounters.SetData(EmptyCounters);
            BuildDispatchArguments.SetData(EmptyBuildDispatchArgs);
            CullDispatchArguments.SetData(EmptyCullDispatchArgs);
        }

        public GraphicsBuffer Vertices { get; private set; }
        public GraphicsBuffer Indices { get; private set; }
        public GraphicsBuffer Descriptors { get; private set; }
        public GraphicsBuffer FilmRanges { get; private set; }
        public GraphicsBuffer DrawArguments { get; private set; }
        public GraphicsBuffer BuildCounters { get; private set; }
        public GraphicsBuffer BuildDispatchArguments { get; private set; }
        public GraphicsBuffer CullDispatchArguments { get; private set; }
        public int VertexCapacity { get; }
        public int IndexCapacity { get; }
        public int DescriptorCapacity { get; }
        public int FilmCapacity { get; }
        public uint Generation { get; internal set; }
        public bool IsDisposed => Vertices == null;
        private GraphicsFence _lastReadFence;
        private bool _hasReadFence;
        internal bool CanWrite
        {
            get
            {
                if (!_hasReadFence) return true;
                try
                {
                    if (!_lastReadFence.passed) return false;
                }
                catch (Exception) { }
                _hasReadFence = false;
                return true;
            }
        }

        internal void MarkRead(GraphicsFence fence)
        {
            _lastReadFence = fence;
            _hasReadFence = true;
        }

        public void Dispose()
        {
            Vertices?.Dispose();
            Indices?.Dispose();
            Descriptors?.Dispose();
            FilmRanges?.Dispose();
            DrawArguments?.Dispose();
            BuildCounters?.Dispose();
            BuildDispatchArguments?.Dispose();
            CullDispatchArguments?.Dispose();
            Vertices = null;
            Indices = null;
            Descriptors = null;
            FilmRanges = null;
            DrawArguments = null;
            BuildCounters = null;
            BuildDispatchArguments = null;
            CullDispatchArguments = null;
            _hasReadFence = false;
        }
    }

    /// <summary>
    /// Per-view GPU scratch. Culling emits a compact indirect index list and a
    /// descriptor-indexed geometry/appearance LOD table without a CPU-visible count.
    /// </summary>
    public sealed class ContactMeshletViewBuffers : IDisposable
    {
        private static readonly uint[] EmptyIndirectArgs = { 0u, 1u, 0u, 0u };
        private static readonly uint[] EmptyCounters = new uint[4];

        internal ContactMeshletViewBuffers(int indexCapacity, int descriptorCapacity)
        {
            IndexCapacity = Math.Max(1, indexCapacity);
            DescriptorCapacity = Math.Max(1, descriptorCapacity);
            VisibleIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                IndexCapacity, sizeof(uint));
            ViewLod = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                DescriptorCapacity, ContactMeshletViewLodGpu.Stride);
            DrawArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured,
                1, sizeof(uint) * 4);
            Counters = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                EmptyCounters.Length, sizeof(uint));
            DrawArguments.SetData(EmptyIndirectArgs);
            Counters.SetData(EmptyCounters);
        }

        public GraphicsBuffer VisibleIndices { get; private set; }
        public GraphicsBuffer ViewLod { get; private set; }
        public GraphicsBuffer DrawArguments { get; private set; }
        public GraphicsBuffer Counters { get; private set; }
        public int IndexCapacity { get; }
        public int DescriptorCapacity { get; }

        public void Dispose()
        {
            VisibleIndices?.Dispose();
            ViewLod?.Dispose();
            DrawArguments?.Dispose();
            Counters?.Dispose();
            VisibleIndices = null;
            ViewLod = null;
            DrawArguments = null;
            Counters = null;
        }
    }

    /// <summary>
    /// Double-buffered GPU meshlet publication consumed by prediction and preview.
    /// Buffer identity, not a mutable flag, separates published and rebuilding data.
    /// </summary>
    public sealed class ContactMeshletBuffers : IDisposable
    {
        private ContactMeshletGenerationBuffers[] _generations;
        private int _publishedSlot;
        private int _buildSlot = 1;

        public ContactMeshletBuffers(int vertexCapacity = 1, int indexCapacity = 1,
            int descriptorCapacity = 1, int filmCapacity = 1)
        {
            Allocate(vertexCapacity, indexCapacity, descriptorCapacity,
                filmCapacity);
            WorldFromChunk = Matrix4x4.identity;
        }

        public ContactMeshletGenerationBuffers Published => _generations[_publishedSlot];
        internal ContactMeshletGenerationBuffers Inactive => _generations[_buildSlot];
        public GraphicsBuffer Vertices => Published.Vertices;
        public GraphicsBuffer Indices => Published.Indices;
        public GraphicsBuffer Descriptors => Published.Descriptors;
        public GraphicsBuffer DrawArguments => Published.DrawArguments;
        public GraphicsBuffer CullDispatchArguments => Published.CullDispatchArguments;
        public int VertexCapacity => Published.VertexCapacity;
        public int IndexCapacity => Published.IndexCapacity;
        public int DescriptorCapacity => Published.DescriptorCapacity;
        public int FilmCapacity => Published.FilmCapacity;
        public uint PublicationGeneration => Published.Generation;
        public Matrix4x4 WorldFromChunk { get; private set; }
        public bool IsDisposed => _generations == null;

        internal void EnsureCapacity(int vertexCapacity, int indexCapacity,
            int descriptorCapacity, int filmCapacity = 1)
        {
            vertexCapacity = Math.Max(1, vertexCapacity);
            indexCapacity = Math.Max(1, indexCapacity);
            descriptorCapacity = Math.Max(1, descriptorCapacity);
            filmCapacity = Math.Max(1, filmCapacity);
            if (vertexCapacity <= VertexCapacity && indexCapacity <= IndexCapacity &&
                descriptorCapacity <= DescriptorCapacity &&
                filmCapacity <= FilmCapacity) return;
            int vertices = Math.Max(vertexCapacity, VertexCapacity);
            int indices = Math.Max(indexCapacity, IndexCapacity);
            int descriptors = Math.Max(descriptorCapacity, DescriptorCapacity);
            int films = Math.Max(filmCapacity, FilmCapacity);
            DisposeGenerations();
            Allocate(vertices, indices, descriptors, films);
        }

        internal bool TryBeginBuild(out ContactMeshletGenerationBuffers generation)
        {
            generation = Inactive;
            return generation.CanWrite;
        }

        internal bool TryBeginPublishedWrite(
            out ContactMeshletGenerationBuffers generation)
        {
            generation = Published;
            return generation.CanWrite;
        }

        public ContactMeshletViewBuffers CreateViewBuffers() =>
            new(IndexCapacity, DescriptorCapacity);

        public void SetChunkTransform(Matrix4x4 worldFromChunk) =>
            WorldFromChunk = worldFromChunk;

        internal void Publish(uint generation)
        {
            if (generation == 0u)
                throw new ArgumentOutOfRangeException(nameof(generation));
            Inactive.Generation = generation;
            int previousPublished = _publishedSlot;
            _publishedSlot = _buildSlot;
            _buildSlot = previousPublished;
        }

        internal void MarkPublishedMutation(uint generation)
        {
            if (generation == 0u)
                throw new ArgumentOutOfRangeException(nameof(generation));
            Published.Generation = generation;
        }

        internal void MarkPublishedRead(GraphicsFence fence) =>
            Published.MarkRead(fence);

        public void Dispose()
        {
            DisposeGenerations();
            _generations = null;
        }

        private void Allocate(int vertexCapacity, int indexCapacity,
            int descriptorCapacity, int filmCapacity)
        {
            _generations = new[]
            {
                new ContactMeshletGenerationBuffers(vertexCapacity, indexCapacity,
                    descriptorCapacity, filmCapacity),
                new ContactMeshletGenerationBuffers(vertexCapacity, indexCapacity,
                    descriptorCapacity, filmCapacity)
            };
            _publishedSlot = 0;
            _buildSlot = 1;
        }

        private void DisposeGenerations()
        {
            if (_generations == null) return;
            foreach (ContactMeshletGenerationBuffers generation in _generations)
                generation?.Dispose();
        }
    }
}
