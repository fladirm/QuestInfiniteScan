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
        public uint Reserved0;
        public uint Reserved1;

        public const int Stride = 64;
    }

    /// <summary>
    /// Generation-tagged GPU meshlet publication consumed by prediction and preview.
    /// Q3-12 fills its back generation through compute; an empty generation is a valid
    /// initial world and produces UNSEEN ConeEvents without CPU geometry.
    /// </summary>
    public sealed class ContactMeshletBuffers : IDisposable
    {
        private static readonly uint[] EmptyIndirectArgs = { 0u, 1u, 0u, 0u };

        public ContactMeshletBuffers(int vertexCapacity = 1, int indexCapacity = 1)
        {
            Allocate(Math.Max(1, vertexCapacity), Math.Max(1, indexCapacity));
            WorldFromChunk = Matrix4x4.identity;
        }

        public GraphicsBuffer Vertices { get; private set; }
        public GraphicsBuffer Indices { get; private set; }
        public GraphicsBuffer DrawArguments { get; private set; }
        public int VertexCapacity { get; private set; }
        public int IndexCapacity { get; private set; }
        public uint PublicationGeneration { get; private set; }
        public Matrix4x4 WorldFromChunk { get; private set; }
        public bool IsDisposed => Vertices == null;

        internal void EnsureCapacity(int vertexCapacity, int indexCapacity)
        {
            vertexCapacity = Math.Max(1, vertexCapacity);
            indexCapacity = Math.Max(1, indexCapacity);
            if (vertexCapacity <= VertexCapacity && indexCapacity <= IndexCapacity)
                return;
            Vertices?.Dispose();
            Indices?.Dispose();
            DrawArguments?.Dispose();
            Allocate(Math.Max(vertexCapacity, VertexCapacity),
                Math.Max(indexCapacity, IndexCapacity));
        }

        private void Allocate(int vertexCapacity, int indexCapacity)
        {
            VertexCapacity = vertexCapacity;
            IndexCapacity = indexCapacity;
            Vertices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                VertexCapacity, ContactMeshletVertexGpu.Stride);
            Indices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                IndexCapacity, sizeof(uint));
            DrawArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured,
                1, sizeof(uint) * 4);
            DrawArguments.SetData(EmptyIndirectArgs);
        }

        public void SetChunkTransform(Matrix4x4 worldFromChunk) =>
            WorldFromChunk = worldFromChunk;

        internal void MarkPublished(uint generation)
        {
            if (generation == 0u)
                throw new ArgumentOutOfRangeException(nameof(generation));
            PublicationGeneration = generation;
        }

        public void Dispose()
        {
            Vertices?.Dispose();
            Indices?.Dispose();
            DrawArguments?.Dispose();
            Vertices = null;
            Indices = null;
            DrawArguments = null;
        }
    }
}
