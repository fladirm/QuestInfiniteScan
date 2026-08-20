using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    [Flags]
    public enum NormalizedDepthFlags : uint
    {
        None = 0,
        Valid = 1u << 0,
        RawInvalid = 1u << 1,
        RangeInvalid = 1u << 2,
        RayInvalid = 1u << 3
    }

    /// <summary>
    /// Immutable output of GPU depth normalization. MetricDepth stores Euclidean ray
    /// range in X and camera view-Z in Y for both array slices.
    /// </summary>
    public sealed class NormalizedRigFrameLease : IDisposable
    {
        private NormalizedDepthRing _owner;
        private readonly int _slot;
        private readonly uint _generation;
        private StereoRigFrameLease _source;
        private ConeLutLease _coneLuts;

        internal NormalizedRigFrameLease(NormalizedDepthRing owner, int slot,
            uint generation, StereoRigFrameLease source, ConeLutLease coneLuts)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _slot = slot;
            _generation = generation;
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _coneLuts = coneLuts ?? throw new ArgumentNullException(nameof(coneLuts));
        }

        public StereoRigFrameLease Source => _source ??
            throw new ObjectDisposedException(nameof(NormalizedRigFrameLease));
        public ConeLutLease ConeLuts => _coneLuts ??
            throw new ObjectDisposedException(nameof(NormalizedRigFrameLease));
        public RenderTexture MetricDepth => Owner.GetMetricDepth(_slot, _generation);
        public RenderTexture Flags => Owner.GetFlags(_slot, _generation);
        /// <summary>Range, view-Z, learned sigma, and L/R consensus confidence.</summary>
        public RenderTexture ConsensusDepth => Owner.GetConsensusDepth(_slot, _generation);
        /// <summary>Camera-local oriented normal XYZ and plane-fit confidence.</summary>
        public RenderTexture LocalNormal => Owner.GetLocalNormal(_slot, _generation);
        /// <summary>Depth, normal, RGB, and combined persistent-boundary evidence.</summary>
        public RenderTexture BoundaryEvidence => Owner.GetBoundaryEvidence(_slot, _generation);
        public bool IsDisposed => _owner == null;
        public bool IsValid => !IsDisposed && Source.IsValid &&
                               ConeLuts.Calibration.IsCompatible(Source);

        public NormalizedRigFrameLease Retain()
        {
            NormalizedDepthRing owner = Owner;
            owner.Retain(_slot, _generation);
            return new NormalizedRigFrameLease(owner, _slot, _generation,
                Source.Retain(), ConeLuts.Retain());
        }

        internal void CommitGpuWrite() => Owner.CommitGpuWrite(_slot, _generation);

        public void Dispose()
        {
            NormalizedDepthRing owner = _owner;
            if (owner == null)
                return;
            _owner = null;
            _source.Dispose();
            _coneLuts.Dispose();
            _source = null;
            _coneLuts = null;
            owner.Release(_slot, _generation);
        }

        private NormalizedDepthRing Owner => _owner ??
            throw new ObjectDisposedException(nameof(NormalizedRigFrameLease));
    }

    internal sealed class NormalizedDepthRing : IDisposable
    {
        private sealed class Slot
        {
            internal RenderTexture MetricDepth;
            internal RenderTexture Flags;
            internal RenderTexture ConsensusDepth;
            internal RenderTexture LocalNormal;
            internal RenderTexture BoundaryEvidence;
            internal uint Generation;
            internal int References;
            internal GraphicsFence Fence;
            internal bool HasFence;
            internal bool RetireWhenReleased;
        }

        private readonly Slot[] _slots;
        private int _cursor;
        private bool _disposed;

        internal NormalizedDepthRing(int capacity)
        {
            if (capacity < 3)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _slots = new Slot[capacity];
            for (int i = 0; i < capacity; i++)
                _slots[i] = new Slot();
        }

        internal bool TryBegin(StereoRigFrameLease source, ConeLutLease coneLuts,
            out NormalizedRigFrameLease frame)
        {
            frame = null;
            if (_disposed || source == null || !source.IsValid || coneLuts == null ||
                coneLuts.IsDisposed)
                return false;

            int selected = -1;
            for (int offset = 0; offset < _slots.Length; offset++)
            {
                int index = (_cursor + offset) % _slots.Length;
                Slot candidate = _slots[index];
                if (candidate.References == 0 && FencePassed(candidate))
                {
                    selected = index;
                    break;
                }
            }
            if (selected < 0)
                return false;

            Slot slot = _slots[selected];
            Vector2Int resolution = source.DepthLeft.Resolution;
            EnsureTextures(slot, resolution, selected);
            slot.Generation = NextGeneration(slot.Generation);
            slot.References = 1;
            slot.HasFence = false;
            _cursor = (selected + 1) % _slots.Length;
            frame = new NormalizedRigFrameLease(this, selected, slot.Generation,
                source.Retain(), coneLuts.Retain());
            return true;
        }

        internal RenderTexture GetMetricDepth(int index, uint generation) =>
            Validate(index, generation).MetricDepth;

        internal RenderTexture GetFlags(int index, uint generation) =>
            Validate(index, generation).Flags;

        internal RenderTexture GetConsensusDepth(int index, uint generation) =>
            Validate(index, generation).ConsensusDepth;

        internal RenderTexture GetLocalNormal(int index, uint generation) =>
            Validate(index, generation).LocalNormal;

        internal RenderTexture GetBoundaryEvidence(int index, uint generation) =>
            Validate(index, generation).BoundaryEvidence;

        internal void CommitGpuWrite(int index, uint generation)
        {
            Slot slot = Validate(index, generation);
            try
            {
                slot.Fence = Graphics.CreateGraphicsFence(
                    GraphicsFenceType.AsyncQueueSynchronisation,
                    SynchronisationStageFlags.AllGPUOperations);
                slot.HasFence = true;
            }
            catch (Exception)
            {
                // Null/headless editor graphics can lack fences. Device Vulkan does not.
                slot.HasFence = false;
            }
        }

        internal void Retain(int index, uint generation)
        {
            Slot slot = Validate(index, generation);
            checked { slot.References++; }
        }

        internal void Release(int index, uint generation)
        {
            if ((uint)index >= (uint)_slots.Length)
                return;
            Slot slot = _slots[index];
            if (slot.Generation != generation || slot.References <= 0)
                return;
            slot.References--;
            if (slot.References == 0 && slot.RetireWhenReleased)
                DestroySlot(slot);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (Slot slot in _slots)
            {
                if (slot.References == 0)
                    DestroySlot(slot);
                else
                    slot.RetireWhenReleased = true;
            }
        }

        private static void EnsureTextures(Slot slot, Vector2Int resolution, int index)
        {
            if (Compatible(slot.MetricDepth, resolution,
                    GraphicsFormat.R32G32_SFloat) &&
                Compatible(slot.Flags, resolution, GraphicsFormat.R32_UInt) &&
                Compatible(slot.ConsensusDepth, resolution,
                    GraphicsFormat.R32G32B32A32_SFloat) &&
                Compatible(slot.LocalNormal, resolution,
                    GraphicsFormat.R16G16B16A16_SFloat) &&
                Compatible(slot.BoundaryEvidence, resolution,
                    GraphicsFormat.R16G16B16A16_SFloat))
                return;

            DestroySlot(slot);
            slot.MetricDepth = CreateArrayTexture(
                $"[Cone-PRISM] Metric Depth {index}", resolution,
                GraphicsFormat.R32G32_SFloat);
            slot.Flags = CreateArrayTexture(
                $"[Cone-PRISM] Depth Flags {index}", resolution,
                GraphicsFormat.R32_UInt);
            slot.ConsensusDepth = CreateArrayTexture(
                $"[Cone-PRISM] Consensus Depth {index}", resolution,
                GraphicsFormat.R32G32B32A32_SFloat);
            slot.LocalNormal = CreateArrayTexture(
                $"[Cone-PRISM] Local Normal {index}", resolution,
                GraphicsFormat.R16G16B16A16_SFloat);
            slot.BoundaryEvidence = CreateArrayTexture(
                $"[Cone-PRISM] Boundary Evidence {index}", resolution,
                GraphicsFormat.R16G16B16A16_SFloat);
        }

        private static RenderTexture CreateArrayTexture(string name,
            Vector2Int resolution, GraphicsFormat format)
        {
            var descriptor = new RenderTextureDescriptor(resolution.x, resolution.y)
            {
                graphicsFormat = format,
                depthBufferBits = 0,
                msaaSamples = 1,
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 2,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false
            };
            var texture = new RenderTexture(descriptor)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!texture.Create())
            {
                DestroyTexture(texture);
                throw new InvalidOperationException($"Unable to allocate {name}.");
            }
            return texture;
        }

        private Slot Validate(int index, uint generation)
        {
            if ((uint)index >= (uint)_slots.Length)
                throw new ObjectDisposedException(nameof(NormalizedRigFrameLease));
            Slot slot = _slots[index];
            if (slot.Generation != generation || slot.References <= 0 ||
                slot.MetricDepth == null || slot.Flags == null ||
                slot.ConsensusDepth == null || slot.LocalNormal == null ||
                slot.BoundaryEvidence == null)
                throw new ObjectDisposedException(nameof(NormalizedRigFrameLease));
            return slot;
        }

        private static bool FencePassed(Slot slot)
        {
            if (!slot.HasFence)
                return true;
            try { return slot.Fence.passed; }
            catch (Exception) { return true; }
        }

        private static bool Compatible(RenderTexture texture, Vector2Int resolution,
            GraphicsFormat format) => texture != null &&
                                      texture.width == resolution.x &&
                                      texture.height == resolution.y &&
                                      texture.volumeDepth == 2 &&
                                      texture.dimension == TextureDimension.Tex2DArray &&
                                      texture.graphicsFormat == format;

        private static void DestroySlot(Slot slot)
        {
            DestroyTexture(slot.MetricDepth);
            DestroyTexture(slot.Flags);
            DestroyTexture(slot.ConsensusDepth);
            DestroyTexture(slot.LocalNormal);
            DestroyTexture(slot.BoundaryEvidence);
            slot.MetricDepth = null;
            slot.Flags = null;
            slot.ConsensusDepth = null;
            slot.LocalNormal = null;
            slot.BoundaryEvidence = null;
            slot.HasFence = false;
            slot.RetireWhenReleased = false;
        }

        private static void DestroyTexture(RenderTexture texture)
        {
            if (texture == null)
                return;
            texture.Release();
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(texture);
            else
                UnityEngine.Object.DestroyImmediate(texture);
        }

        private static uint NextGeneration(uint current) =>
            current == uint.MaxValue ? 1u : current + 1u;
    }
}
