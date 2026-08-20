using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    public sealed class PredictionFrameLease : IDisposable
    {
        private PredictionTargetRing _owner;
        private readonly int _slot;
        private readonly uint _generation;
        private NormalizedRigFrameLease _source;

        internal PredictionFrameLease(PredictionTargetRing owner, int slot,
            uint generation, NormalizedRigFrameLease source)
        {
            _owner = owner;
            _slot = slot;
            _generation = generation;
            _source = source;
        }

        public NormalizedRigFrameLease Source => _source ??
            throw new ObjectDisposedException(nameof(PredictionFrameLease));
        public RenderTexture DepthSigma => Owner.Get(_slot, _generation).DepthSigma;
        public RenderTexture NormalConfidence => Owner.Get(_slot, _generation).NormalConfidence;
        public RenderTexture FilmIdGeneration => Owner.Get(_slot, _generation).FilmIdGeneration;
        public RenderTexture UvMetadata => Owner.Get(_slot, _generation).UvMetadata;
        public RenderTexture HardwareDepth => Owner.Get(_slot, _generation).HardwareDepth;
        public RenderTexture Layer1DepthSigma => Owner.Get(_slot, _generation).Layer1DepthSigma;
        public RenderTexture Layer1NormalConfidence => Owner.Get(_slot, _generation).Layer1NormalConfidence;
        public RenderTexture Layer1FilmIdGeneration => Owner.Get(_slot, _generation).Layer1FilmIdGeneration;
        public RenderTexture Layer1UvMetadata => Owner.Get(_slot, _generation).Layer1UvMetadata;
        public RenderTexture Layer1HardwareDepth => Owner.Get(_slot, _generation).Layer1HardwareDepth;
        public uint TargetGeneration => _generation;
        public bool IsDisposed => _owner == null;

        public PredictionFrameLease Retain()
        {
            PredictionTargetRing owner = Owner;
            owner.Retain(_slot, _generation);
            return new PredictionFrameLease(owner, _slot, _generation,
                Source.Retain());
        }

        internal void CommitGpuWrite() => Owner.CommitGpuWrite(_slot, _generation);

        public void Dispose()
        {
            PredictionTargetRing owner = _owner;
            if (owner == null) return;
            _owner = null;
            _source.Dispose();
            _source = null;
            owner.Release(_slot, _generation);
        }

        private PredictionTargetRing Owner => _owner ??
            throw new ObjectDisposedException(nameof(PredictionFrameLease));
    }

    internal sealed class PredictionTargetRing : IDisposable
    {
        internal sealed class Slot
        {
            internal RenderTexture DepthSigma;
            internal RenderTexture NormalConfidence;
            internal RenderTexture FilmIdGeneration;
            internal RenderTexture UvMetadata;
            internal RenderTexture HardwareDepth;
            internal RenderTexture Layer1DepthSigma;
            internal RenderTexture Layer1NormalConfidence;
            internal RenderTexture Layer1FilmIdGeneration;
            internal RenderTexture Layer1UvMetadata;
            internal RenderTexture Layer1HardwareDepth;
            internal uint Generation;
            internal int References;
            internal GraphicsFence Fence;
            internal bool HasFence;
            internal bool RetireWhenReleased;
        }

        private readonly Slot[] _slots;
        private int _cursor;
        private bool _disposed;

        internal PredictionTargetRing(int capacity)
        {
            _slots = new Slot[Math.Max(3, capacity)];
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new Slot();
        }

        internal bool TryBegin(NormalizedRigFrameLease source,
            out PredictionFrameLease frame)
        {
            frame = null;
            if (_disposed || source == null || !source.IsValid) return false;
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
            EnsureTextures(slot, source.Source.DepthLeft.Resolution, selected);
            slot.Generation = NextGeneration(slot.Generation);
            slot.References = 1;
            slot.HasFence = false;
            _cursor = (selected + 1) % _slots.Length;
            frame = new PredictionFrameLease(this, selected, slot.Generation,
                source.Retain());
            return true;
        }

        internal Slot Get(int index, uint generation)
        {
            if ((uint)index >= (uint)_slots.Length)
                throw new ObjectDisposedException(nameof(PredictionFrameLease));
            Slot slot = _slots[index];
            if (slot.Generation != generation || slot.References <= 0 ||
                slot.DepthSigma == null)
                throw new ObjectDisposedException(nameof(PredictionFrameLease));
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
            if (slot.References == 0 && slot.RetireWhenReleased) DestroySlot(slot);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (Slot slot in _slots)
            {
                if (slot.References == 0) DestroySlot(slot);
                else slot.RetireWhenReleased = true;
            }
        }

        private static void EnsureTextures(Slot slot, Vector2Int resolution, int index)
        {
            if (Compatible(slot.DepthSigma, resolution, GraphicsFormat.R32G32_SFloat) &&
                Compatible(slot.NormalConfidence, resolution,
                    GraphicsFormat.R16G16B16A16_SFloat) &&
                Compatible(slot.FilmIdGeneration, resolution,
                    GraphicsFormat.R32G32_UInt) &&
                Compatible(slot.UvMetadata, resolution,
                    GraphicsFormat.R16G16B16A16_SFloat) &&
                CompatibleDepth(slot.HardwareDepth, resolution) &&
                Compatible(slot.Layer1DepthSigma, resolution,
                    GraphicsFormat.R32G32_SFloat) &&
                Compatible(slot.Layer1NormalConfidence, resolution,
                    GraphicsFormat.R16G16B16A16_SFloat) &&
                Compatible(slot.Layer1FilmIdGeneration, resolution,
                    GraphicsFormat.R32G32_UInt) &&
                Compatible(slot.Layer1UvMetadata, resolution,
                    GraphicsFormat.R16G16B16A16_SFloat) &&
                CompatibleDepth(slot.Layer1HardwareDepth, resolution)) return;
            DestroySlot(slot);
            slot.DepthSigma = CreateColor("Depth Sigma", index, resolution,
                GraphicsFormat.R32G32_SFloat);
            slot.NormalConfidence = CreateColor("Normal Confidence", index, resolution,
                GraphicsFormat.R16G16B16A16_SFloat);
            slot.FilmIdGeneration = CreateColor("Film ID Generation", index, resolution,
                GraphicsFormat.R32G32_UInt);
            slot.UvMetadata = CreateColor("UV Metadata", index, resolution,
                GraphicsFormat.R16G16B16A16_SFloat);
            slot.HardwareDepth = CreateDepth(index, resolution);
            slot.Layer1DepthSigma = CreateColor("Layer 1 Depth Sigma", index,
                resolution, GraphicsFormat.R32G32_SFloat);
            slot.Layer1NormalConfidence = CreateColor("Layer 1 Normal Confidence",
                index, resolution, GraphicsFormat.R16G16B16A16_SFloat);
            slot.Layer1FilmIdGeneration = CreateColor("Layer 1 Film ID Generation",
                index, resolution, GraphicsFormat.R32G32_UInt);
            slot.Layer1UvMetadata = CreateColor("Layer 1 UV Metadata", index,
                resolution, GraphicsFormat.R16G16B16A16_SFloat);
            slot.Layer1HardwareDepth = CreateDepth(index + 1000, resolution);
        }

        private static RenderTexture CreateColor(string label, int index,
            Vector2Int resolution, GraphicsFormat format)
        {
            var descriptor = ArrayDescriptor(resolution);
            descriptor.graphicsFormat = format;
            var texture = new RenderTexture(descriptor)
            {
                name = $"[Cone-PRISM] Prediction {label} {index}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!texture.Create()) throw new InvalidOperationException(texture.name);
            return texture;
        }

        private static RenderTexture CreateDepth(int index, Vector2Int resolution)
        {
            var descriptor = ArrayDescriptor(resolution);
            descriptor.graphicsFormat = GraphicsFormat.None;
            descriptor.depthStencilFormat = GraphicsFormat.D32_SFloat;
            var texture = new RenderTexture(descriptor)
            {
                name = $"[Cone-PRISM] Prediction Hardware Depth {index}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!texture.Create()) throw new InvalidOperationException(texture.name);
            return texture;
        }

        private static RenderTextureDescriptor ArrayDescriptor(Vector2Int resolution) =>
            new(resolution.x, resolution.y)
            {
                depthBufferBits = 0,
                msaaSamples = 1,
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 2,
                enableRandomWrite = false,
                useMipMap = false,
                autoGenerateMips = false
            };

        private static bool Compatible(RenderTexture texture, Vector2Int resolution,
            GraphicsFormat format) => texture != null && texture.width == resolution.x &&
            texture.height == resolution.y && texture.volumeDepth == 2 &&
            texture.graphicsFormat == format;

        private static bool CompatibleDepth(RenderTexture texture, Vector2Int resolution) =>
            texture != null && texture.width == resolution.x &&
            texture.height == resolution.y && texture.volumeDepth == 2 &&
            texture.depthStencilFormat == GraphicsFormat.D32_SFloat;

        private static bool FencePassed(Slot slot)
        {
            if (!slot.HasFence) return true;
            try { return slot.Fence.passed; }
            catch (Exception) { return true; }
        }

        private static void DestroySlot(Slot slot)
        {
            DestroyTexture(slot.DepthSigma);
            DestroyTexture(slot.NormalConfidence);
            DestroyTexture(slot.FilmIdGeneration);
            DestroyTexture(slot.UvMetadata);
            DestroyTexture(slot.HardwareDepth);
            DestroyTexture(slot.Layer1DepthSigma);
            DestroyTexture(slot.Layer1NormalConfidence);
            DestroyTexture(slot.Layer1FilmIdGeneration);
            DestroyTexture(slot.Layer1UvMetadata);
            DestroyTexture(slot.Layer1HardwareDepth);
            slot.DepthSigma = null;
            slot.NormalConfidence = null;
            slot.FilmIdGeneration = null;
            slot.UvMetadata = null;
            slot.HardwareDepth = null;
            slot.Layer1DepthSigma = null;
            slot.Layer1NormalConfidence = null;
            slot.Layer1FilmIdGeneration = null;
            slot.Layer1UvMetadata = null;
            slot.Layer1HardwareDepth = null;
            slot.HasFence = false;
            slot.RetireWhenReleased = false;
        }

        private static void DestroyTexture(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }

        private static uint NextGeneration(uint current) =>
            current == uint.MaxValue ? 1u : current + 1u;
    }
}
