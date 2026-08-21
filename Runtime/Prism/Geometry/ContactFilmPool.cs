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
        DirtyMeshlet = 1u << 4,
        HasDisplacement = 1u << 5,
        SplitParent = 1u << 6,
        Retired = 1u << 7,
        TopologyLocked = 1u << 8,
        // This chart belongs to a conserved connected pressure manifold. The bit
        // never means that this film/tile is independently capped or closed.
        PressureManifoldMember = 1u << 9
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
        // Conservative 8x8 observed-contact support captured at spawn.  It controls
        // posterior confidence/detail deposition only; it must never punch holes in
        // the closed pressure manifold.
        public uint SupportMaskLow;
        public uint SupportMaskHigh;
        // TopologyAdapt owns these words for split-parent/child bookkeeping. Closed
        // manifold/eye-seed state lives in its own generation-tagged GPU pool.
        public uint Reserved0;
        public uint Reserved1;
        public uint Reserved2;

        public const int Stride = 152;
    }

    [Flags]
    public enum ContactFilmSlotFlags : uint
    {
        None = 0,
        Allocated = 1u << 0,
        Active = 1u << 1,
        Dirty = 1u << 2,
        Free = 1u << 3
    }

    /// <summary>
    /// Generation-safe storage ownership for one canonical film slot. ActiveOrdinal
    /// points into the compact live list; NextFree is a one-based handle so zero is
    /// the lock-free free-list terminator.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ContactFilmSlotStateGpu
    {
        public uint Generation;
        public uint ActiveOrdinal;
        public uint NextFree;
        public uint Flags;

        public const int Stride = 16;
    }

    /// <summary>
    /// Bounded canonical ContactFilm pool. Header and 6x6 information sufficient
    /// statistics remain GPU resident; allocator overflow fails closed.
    /// </summary>
    public sealed class ContactFilmPool : IDisposable
    {
        // 0..5 packed analytic-surface H and pre-hit state, 6..7 g/support,
        // 8 quality envelope, 9 independent-contact view state/posterior variance.
        public const int InformationRecords = 10;
        // high-water, live, overflow, publication generation, free head, free
        // count, compact active count, compact dirty count.
        private static readonly uint[] InitialAllocator =
            { 0u, 0u, 0u, 1u, 0u, 0u, 0u, 0u };

        public ContactFilmPool(int capacity)
        {
            Capacity = Math.Max(1024, capacity);
            Headers = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Capacity,
                ContactFilmHeaderGpu.Stride);
            // Resumable information posterior.  Its physical precision and
            // independent-view state are canonical reconstruction data.
            Information = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                checked(Capacity * InformationRecords), sizeof(float) * 4);
            Allocator = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8,
                sizeof(uint));
            SlotStates = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                Capacity, ContactFilmSlotStateGpu.Stride);
            ActiveIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                Capacity, sizeof(uint));
            DirtyIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                Capacity, sizeof(uint));
            Allocator.SetData(InitialAllocator);
            SlotStates.SetData(new ContactFilmSlotStateGpu[Capacity]);
            ActiveIndices.SetData(new uint[Capacity]);
            DirtyIndices.SetData(new uint[Capacity]);
            Manifolds = new PressureManifoldPool(Capacity);
        }

        public int Capacity { get; }
        public GraphicsBuffer Headers { get; private set; }
        public GraphicsBuffer Information { get; private set; }
        /// <summary>
        /// High-water/live/overflow/generation followed by free-head/free-count and
        /// compact active/dirty counts. No production dispatch uses high-water.
        /// </summary>
        public GraphicsBuffer Allocator { get; private set; }
        public GraphicsBuffer SlotStates { get; private set; }
        public GraphicsBuffer ActiveIndices { get; private set; }
        public GraphicsBuffer DirtyIndices { get; private set; }
        public PressureManifoldPool Manifolds { get; private set; }
        public bool IsDisposed => Headers == null;

        public void Dispose()
        {
            Headers?.Dispose();
            Information?.Dispose();
            Allocator?.Dispose();
            SlotStates?.Dispose();
            ActiveIndices?.Dispose();
            DirtyIndices?.Dispose();
            Manifolds?.Dispose();
            Headers = null;
            Information = null;
            Allocator = null;
            SlotStates = null;
            ActiveIndices = null;
            DirtyIndices = null;
            Manifolds = null;
        }
    }
}
