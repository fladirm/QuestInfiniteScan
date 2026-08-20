using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    public enum ConeEventClass : uint
    {
        Invalid = 0,
        Match = 1,
        NewFront = 2,
        Behind = 3,
        NewLayer = 4,
        Unseen = 5,
        Boundary = 6,
        Count = 7
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ConeEventGpu
    {
        public uint PixelLinear;
        public uint PackedPixel;
        public uint Classification;
        public uint Flags;
        public float MeasuredRange;
        public float PredictedRange;
        public float CombinedSigma;
        public float Confidence;
        public Vector3 MeasuredNormal;
        public float BoundaryEvidence;
        public uint FilmId;
        public uint FilmGeneration;
        public Vector2 PredictedUv;
        public float FootprintArea;
        public float Incidence;
        public float PressurePrecision;
        public uint OccluderFilmId;
        public uint OccluderGeneration;

        public const int Stride = 84;
    }

    public sealed class ConeEventFrameLease : IDisposable
    {
        private ConeEventBufferRing _owner;
        private readonly int _slot;
        private readonly uint _generation;
        private PredictionFrameLease _source;

        internal ConeEventFrameLease(ConeEventBufferRing owner, int slot,
            uint generation, PredictionFrameLease source)
        {
            _owner = owner;
            _slot = slot;
            _generation = generation;
            _source = source;
        }

        public PredictionFrameLease Source => _source ??
            throw new ObjectDisposedException(nameof(ConeEventFrameLease));
        public GraphicsBuffer Events => Owner.Get(_slot, _generation).Events;
        public GraphicsBuffer ClassifiedIndices => Owner.Get(_slot, _generation).ClassifiedIndices;
        public GraphicsBuffer ClassCounters => Owner.Get(_slot, _generation).ClassCounters;
        public GraphicsBuffer ClassDispatchArguments => Owner.Get(_slot, _generation).DispatchArguments;
        public int EventCapacity => Owner.Get(_slot, _generation).EventCapacity;
        public uint BufferGeneration => _generation;
        public bool IsDisposed => _owner == null;

        public ConeEventFrameLease Retain()
        {
            ConeEventBufferRing owner = Owner;
            owner.Retain(_slot, _generation);
            return new ConeEventFrameLease(owner, _slot, _generation, Source.Retain());
        }

        internal void CommitGpuWrite() => Owner.CommitGpuWrite(_slot, _generation);

        public void Dispose()
        {
            ConeEventBufferRing owner = _owner;
            if (owner == null) return;
            _owner = null;
            _source.Dispose();
            _source = null;
            owner.Release(_slot, _generation);
        }

        private ConeEventBufferRing Owner => _owner ??
            throw new ObjectDisposedException(nameof(ConeEventFrameLease));
    }

    internal sealed class ConeEventBufferRing : IDisposable
    {
        internal sealed class Slot
        {
            internal GraphicsBuffer Events;
            internal GraphicsBuffer ClassifiedIndices;
            internal GraphicsBuffer ClassCounters;
            internal GraphicsBuffer DispatchArguments;
            internal int EventCapacity;
            internal uint Generation;
            internal int References;
            internal GraphicsFence Fence;
            internal bool HasFence;
            internal bool RetireWhenReleased;
        }

        private readonly Slot[] _slots;
        private int _cursor;
        private bool _disposed;

        internal ConeEventBufferRing(int capacity)
        {
            _slots = new Slot[Math.Max(3, capacity)];
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new Slot();
        }

        internal bool TryBegin(PredictionFrameLease source, out ConeEventFrameLease frame)
        {
            frame = null;
            if (_disposed || source == null || source.IsDisposed) return false;
            int selected = -1;
            for (int offset = 0; offset < _slots.Length; offset++)
            {
                int index = (_cursor + offset) % _slots.Length;
                if (_slots[index].References == 0 && FencePassed(_slots[index]))
                {
                    selected = index;
                    break;
                }
            }
            if (selected < 0) return false;
            Slot slot = _slots[selected];
            Vector2Int resolution = source.Source.Source.DepthLeft.Resolution;
            int eventCapacity = checked(resolution.x * resolution.y * 2);
            EnsureBuffers(slot, eventCapacity);
            slot.Generation = NextGeneration(slot.Generation);
            slot.References = 1;
            slot.HasFence = false;
            _cursor = (selected + 1) % _slots.Length;
            frame = new ConeEventFrameLease(this, selected, slot.Generation,
                source.Retain());
            return true;
        }

        internal Slot Get(int index, uint generation)
        {
            if ((uint)index >= (uint)_slots.Length)
                throw new ObjectDisposedException(nameof(ConeEventFrameLease));
            Slot slot = _slots[index];
            if (slot.Generation != generation || slot.References <= 0 ||
                slot.Events == null)
                throw new ObjectDisposedException(nameof(ConeEventFrameLease));
            return slot;
        }

        internal void CommitGpuWrite(int index, uint generation)
        {
            Slot slot = Get(index, generation);
            try
            {
                slot.Fence = Graphics.CreateGraphicsFence(
                    GraphicsFenceType.AsyncQueueSynchronisation,
                    SynchronisationStageFlags.AllGPUOperations);
                slot.HasFence = true;
            }
            catch (Exception) { slot.HasFence = false; }
        }

        internal void Retain(int index, uint generation)
        {
            Slot slot = Get(index, generation);
            checked { slot.References++; }
        }

        internal void Release(int index, uint generation)
        {
            if ((uint)index >= (uint)_slots.Length) return;
            Slot slot = _slots[index];
            if (slot.Generation != generation || slot.References <= 0) return;
            slot.References--;
            if (slot.References == 0 && slot.RetireWhenReleased) Destroy(slot);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (Slot slot in _slots)
            {
                if (slot.References == 0) Destroy(slot);
                else slot.RetireWhenReleased = true;
            }
        }

        private static void EnsureBuffers(Slot slot, int eventCapacity)
        {
            if (slot.Events != null && slot.EventCapacity == eventCapacity) return;
            Destroy(slot);
            int classCount = (int)ConeEventClass.Count;
            slot.Events = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                eventCapacity, ConeEventGpu.Stride);
            slot.ClassifiedIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                checked(eventCapacity * classCount), sizeof(uint));
            slot.ClassCounters = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                classCount, sizeof(uint));
            slot.DispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                classCount, sizeof(uint) * 3);
            slot.EventCapacity = eventCapacity;
        }

        private static void Destroy(Slot slot)
        {
            slot.Events?.Dispose();
            slot.ClassifiedIndices?.Dispose();
            slot.ClassCounters?.Dispose();
            slot.DispatchArguments?.Dispose();
            slot.Events = null;
            slot.ClassifiedIndices = null;
            slot.ClassCounters = null;
            slot.DispatchArguments = null;
            slot.EventCapacity = 0;
            slot.HasFence = false;
            slot.RetireWhenReleased = false;
        }

        private static bool FencePassed(Slot slot)
        {
            if (!slot.HasFence) return true;
            try { return slot.Fence.passed; }
            catch (Exception) { return true; }
        }

        private static uint NextGeneration(uint current) =>
            current == uint.MaxValue ? 1u : current + 1u;
    }
}
