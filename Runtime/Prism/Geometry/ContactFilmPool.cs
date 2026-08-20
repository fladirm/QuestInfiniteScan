using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    [Flags]
    public enum ContactFilmFlags : uint
    {
        None = 0,
        Active = 1u << 0,
        OneSided = 1u << 1,
        Uncertain = 1u << 2,
        DirtyGeometry = 1u << 3,
        DirtyMeshlet = 1u << 4
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ContactFilmHeaderGpu
    {
        public uint Id;
        public uint Generation;
        public uint ChunkId;
        public uint Flags;
        public Vector3 Origin;
        public float SupportTotal;
        public Vector3 Normal;
        public float SigmaNormal;
        public Vector3 TangentU;
        public float ExtentU;
        public Vector3 TangentV;
        public float ExtentV;
        public Vector4 Quadratic0123;
        public Vector2 Quadratic45;
        public float Confidence;
        public float Contradiction;
        public uint Revision;
        public uint DisplacementPage;
        public uint AppearancePage;
        public uint BoundaryStart;
        public uint BoundaryCount;
        public uint Reserved0;
        public uint Reserved1;
        public uint Reserved2;

        public const int Stride = 144;
    }

    /// <summary>
    /// Bounded canonical ContactFilm pool. Header and 6x6 information sufficient
    /// statistics remain GPU resident; allocator overflow fails closed.
    /// </summary>
    public sealed class ContactFilmPool : IDisposable
    {
        private static readonly uint[] InitialAllocator = { 0u, 0u, 0u, 1u };

        public ContactFilmPool(int capacity)
        {
            Capacity = Math.Max(1024, capacity);
            Headers = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Capacity,
                ContactFilmHeaderGpu.Stride);
            // Nine float4 records per film: 21 upper-triangle H values, 6 g values,
            // and quality/support state. Q3-08 updates the same resumable statistics.
            Information = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                checked(Capacity * 9), sizeof(float) * 4);
            Allocator = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4,
                sizeof(uint));
            Allocator.SetData(InitialAllocator);
        }

        public int Capacity { get; }
        public GraphicsBuffer Headers { get; private set; }
        public GraphicsBuffer Information { get; private set; }
        /// <summary>next slot, live count, overflow count, publication generation.</summary>
        public GraphicsBuffer Allocator { get; private set; }
        public bool IsDisposed => Headers == null;

        public void Dispose()
        {
            Headers?.Dispose();
            Information?.Dispose();
            Allocator?.Dispose();
            Headers = null;
            Information = null;
            Allocator = null;
        }
    }
}
