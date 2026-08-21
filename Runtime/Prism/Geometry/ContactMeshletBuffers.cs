using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ContactMeshletVertexGpu
    {
        public Vector3 Position;
        public uint FilmId;
        public Vector3 Normal;
        public uint Generation;
        public Vector2 Uv;
        public float Sigma;
        public float Confidence;
        public uint Sidedness;
        public uint Flags;
        public uint AppearancePage;
        // Build-local continuous support sample.  It is persisted with the derived
        // cache but is not part of canonical ContactFilm state.
        public uint CoverageBits;

        public const int Stride = 64;
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
    /// One immutable, generation-tagged meshlet publication. The builder only writes
    /// the inactive generation. A published generation is never mutated while a
    /// prediction or preview draw can still reference it.
    /// </summary>
    public sealed class ContactMeshletGenerationBuffers : IDisposable
    {
        private static readonly uint[] EmptyIndirectArgs = { 0u, 1u, 0u, 0u };
        private static readonly uint[] EmptyDispatchArgs = { 0u, 1u, 1u };
        private static readonly uint[] EmptyCounters = new uint[8];

        internal ContactMeshletGenerationBuffers(int vertexCapacity,
            int indexCapacity, int descriptorCapacity)
        {
            VertexCapacity = Math.Max(1, vertexCapacity);
            IndexCapacity = Math.Max(1, indexCapacity);
            DescriptorCapacity = Math.Max(1, descriptorCapacity);
            Vertices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                VertexCapacity, ContactMeshletVertexGpu.Stride);
            Indices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                IndexCapacity, sizeof(uint));
            Descriptors = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                DescriptorCapacity, ContactMeshletDescriptorGpu.Stride);
            DrawArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured,
                1, sizeof(uint) * 4);
            BuildCounters = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                EmptyCounters.Length, sizeof(uint));
            BuildDispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured,
                1, sizeof(uint) * 3);
            CullDispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured,
                1, sizeof(uint) * 3);
            DrawArguments.SetData(EmptyIndirectArgs);
            BuildCounters.SetData(EmptyCounters);
            BuildDispatchArguments.SetData(EmptyDispatchArgs);
            CullDispatchArguments.SetData(EmptyDispatchArgs);
        }

        public GraphicsBuffer Vertices { get; private set; }
        public GraphicsBuffer Indices { get; private set; }
        public GraphicsBuffer Descriptors { get; private set; }
        public GraphicsBuffer DrawArguments { get; private set; }
        public GraphicsBuffer BuildCounters { get; private set; }
        public GraphicsBuffer BuildDispatchArguments { get; private set; }
        public GraphicsBuffer CullDispatchArguments { get; private set; }
        public int VertexCapacity { get; }
        public int IndexCapacity { get; }
        public int DescriptorCapacity { get; }
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
            DrawArguments?.Dispose();
            BuildCounters?.Dispose();
            BuildDispatchArguments?.Dispose();
            CullDispatchArguments?.Dispose();
            Vertices = null;
            Indices = null;
            Descriptors = null;
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
            int descriptorCapacity = 1)
        {
            Allocate(vertexCapacity, indexCapacity, descriptorCapacity);
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
        public uint PublicationGeneration => Published.Generation;
        public Matrix4x4 WorldFromChunk { get; private set; }
        public bool IsDisposed => _generations == null;

        internal void EnsureCapacity(int vertexCapacity, int indexCapacity,
            int descriptorCapacity)
        {
            vertexCapacity = Math.Max(1, vertexCapacity);
            indexCapacity = Math.Max(1, indexCapacity);
            descriptorCapacity = Math.Max(1, descriptorCapacity);
            if (vertexCapacity <= VertexCapacity && indexCapacity <= IndexCapacity &&
                descriptorCapacity <= DescriptorCapacity) return;
            int vertices = Math.Max(vertexCapacity, VertexCapacity);
            int indices = Math.Max(indexCapacity, IndexCapacity);
            int descriptors = Math.Max(descriptorCapacity, DescriptorCapacity);
            DisposeGenerations();
            Allocate(vertices, indices, descriptors);
        }

        internal bool TryBeginBuild(out ContactMeshletGenerationBuffers generation)
        {
            generation = Inactive;
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

        internal void MarkPublishedRead(GraphicsFence fence) =>
            Published.MarkRead(fence);

        public void Dispose()
        {
            DisposeGenerations();
            _generations = null;
        }

        private void Allocate(int vertexCapacity, int indexCapacity,
            int descriptorCapacity)
        {
            _generations = new[]
            {
                new ContactMeshletGenerationBuffers(vertexCapacity, indexCapacity,
                    descriptorCapacity),
                new ContactMeshletGenerationBuffers(vertexCapacity, indexCapacity,
                    descriptorCapacity)
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
