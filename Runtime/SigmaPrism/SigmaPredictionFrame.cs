using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    internal enum SigmaPredictionAcquireResult
    {
        Acquired = 0,
        Busy = 1,
        Faulted = 2
    }

    /// <summary>
    /// Immutable lease over one dual-eye disposable forward readout. Carrier page
    /// coordinates are exact signed-64 limbs; local UV and normal are derived FP.
    /// </summary>
    public sealed class SigmaPredictionFrameLease : IDisposable
    {
        private SigmaPredictionTargetRing _owner;
        private readonly int _slot;
        private readonly uint _generation;
        private readonly SigmaPoseGaugeState _poseGauge;
        private readonly Matrix4x4 _worldToRoom;
        private StereoRigFrameLease _source;

        internal SigmaPredictionFrameLease(SigmaPredictionTargetRing owner,
            int slot, uint generation, StereoRigFrameLease source,
            SigmaPoseGaugeState poseGauge, Matrix4x4 worldToRoom)
        {
            _owner = owner;
            _slot = slot;
            _generation = generation;
            _source = source;
            _poseGauge = poseGauge;
            _worldToRoom = worldToRoom;
        }

        public StereoRigFrameLease Source => _source ??
            throw new ObjectDisposedException(nameof(SigmaPredictionFrameLease));
        public RenderTexture DepthSupport => Slot.DepthSupport;
        public RenderTexture CarrierPage => Slot.CarrierPage;
        public RenderTexture CarrierUvNormal => Slot.CarrierUvNormal;
        public RenderTexture StateKey => Slot.StateKey;
        public RenderTexture HardwareDepth => Slot.HardwareDepth;
        public uint TargetGeneration => _generation;
        public SigmaPoseGaugeState PoseGauge => _poseGauge;
        internal Matrix4x4 WorldToRoom => _worldToRoom;
        public bool IsDisposed => _owner == null;

        public SigmaPredictionFrameLease Retain()
        {
            SigmaPredictionTargetRing owner = Owner;
            owner.Retain(_slot, _generation);
            return new SigmaPredictionFrameLease(owner, _slot, _generation,
                Source.Retain(), _poseGauge, _worldToRoom);
        }

        internal void CommitGpuWrite() => Owner.CommitGpuWrite(_slot, _generation);

        public void Dispose()
        {
            SigmaPredictionTargetRing owner = _owner;
            if (owner == null)
                return;
            _owner = null;
            _source.Dispose();
            _source = null;
            owner.Release(_slot, _generation);
        }

        private SigmaPredictionTargetRing.Slot Slot => Owner.Get(_slot, _generation);
        private SigmaPredictionTargetRing Owner => _owner ??
            throw new ObjectDisposedException(nameof(SigmaPredictionFrameLease));
    }

    /// <summary>
    /// Non-blocking ref-counted target ring. A busy GPU slot causes backpressure;
    /// the scanner never waits or overwrites a prediction consumed by inverse work.
    /// </summary>
    internal sealed class SigmaPredictionTargetRing : IDisposable
    {
        internal sealed class Slot
        {
            internal RenderTexture DepthSupport;
            internal RenderTexture CarrierPage;
            internal RenderTexture CarrierUvNormal;
            internal RenderTexture StateKey;
            internal RenderTexture HardwareDepth;
            internal uint Generation;
            internal int References;
            internal SigmaGpuCompletionTicket Completion;
            internal bool HasCompletion;
            internal bool CompletionFaulted;
            internal bool RetireWhenReleased;
        }

        private readonly Slot[] _slots;
        private int _cursor;
        private bool _disposed;
        private string _completionFault;

        internal SigmaPredictionTargetRing(int capacity)
        {
            SigmaGpuCompletion.RequireSupported();
            _slots = new Slot[Math.Max(3, capacity)];
            for (int index = 0; index < _slots.Length; ++index)
                _slots[index] = new Slot();
        }

        internal SigmaPredictionAcquireResult TryBegin(
            StereoRigFrameLease source,
            SigmaPoseGaugeState poseGauge, Matrix4x4 worldToRoom,
            out SigmaPredictionFrameLease frame)
        {
            frame = null;
            if (_disposed || source == null || !source.IsValid)
                return SigmaPredictionAcquireResult.Faulted;
            if (_completionFault != null)
                return SigmaPredictionAcquireResult.Faulted;
            int selected = -1;
            for (int offset = 0; offset < _slots.Length; ++offset)
            {
                int index = (_cursor + offset) % _slots.Length;
                Slot candidate = _slots[index];
                if (candidate.References != 0)
                    continue;
                SigmaGpuCompletionStatus status = PollCompletion(candidate,
                    out string completionError);
                if (status == SigmaGpuCompletionStatus.Faulted)
                {
                    LatchCompletionFault(candidate, completionError);
                    return SigmaPredictionAcquireResult.Faulted;
                }
                if (status != SigmaGpuCompletionStatus.Complete)
                    continue;
                selected = index;
                break;
            }
            if (selected < 0)
                return SigmaPredictionAcquireResult.Busy;

            Slot slot = _slots[selected];
            EnsureTextures(slot, source.DepthResolution, selected);
            slot.Generation = NextGeneration(slot.Generation);
            slot.References = 1;
            slot.HasCompletion = false;
            slot.CompletionFaulted = false;
            _cursor = (selected + 1) % _slots.Length;
            frame = new SigmaPredictionFrameLease(this, selected,
                slot.Generation, source.Retain(), poseGauge, worldToRoom);
            return SigmaPredictionAcquireResult.Acquired;
        }

        internal string CompletionFault => _completionFault;

        internal Slot Get(int index, uint generation)
        {
            if ((uint)index >= (uint)_slots.Length)
                throw new ObjectDisposedException(nameof(SigmaPredictionFrameLease));
            Slot slot = _slots[index];
            if (slot.Generation != generation || slot.References <= 0 ||
                slot.DepthSupport == null)
                throw new ObjectDisposedException(nameof(SigmaPredictionFrameLease));
            return slot;
        }

        internal void CommitGpuWrite(int index, uint generation)
        {
            Slot slot = Get(index, generation);
            try
            {
                slot.Completion = SigmaGpuCompletion.InsertAfterGraphicsWork();
                slot.HasCompletion = true;
                slot.CompletionFaulted = false;
            }
            catch (Exception exception)
            {
                LatchCompletionFault(slot,
                    $"Prediction write could not be fenced: {exception.Message}");
                throw;
            }
        }

        internal void Retain(int index, uint generation)
        {
            Slot slot = Get(index, generation);
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
                TryDestroyRetiredSlot(slot);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (Slot slot in _slots)
            {
                slot.RetireWhenReleased = true;
                if (slot.References == 0)
                    TryDestroyRetiredSlot(slot);
            }
        }

        private static void EnsureTextures(Slot slot, Vector2Int resolution,
            int index)
        {
            if (Compatible(slot.DepthSupport, resolution,
                    GraphicsFormat.R32G32_SFloat) &&
                Compatible(slot.CarrierPage, resolution,
                    GraphicsFormat.R32G32B32A32_UInt) &&
                Compatible(slot.CarrierUvNormal, resolution,
                    GraphicsFormat.R32G32B32A32_SFloat) &&
                Compatible(slot.StateKey, resolution,
                    GraphicsFormat.R32G32B32A32_UInt) &&
                CompatibleDepth(slot.HardwareDepth, resolution))
                return;

            DestroySlot(slot);
            slot.DepthSupport = CreateColor("Depth Support", index, resolution,
                GraphicsFormat.R32G32_SFloat);
            slot.CarrierPage = CreateColor("Carrier Page", index, resolution,
                GraphicsFormat.R32G32B32A32_UInt);
            slot.CarrierUvNormal = CreateColor("Carrier UV Normal", index,
                resolution, GraphicsFormat.R32G32B32A32_SFloat);
            slot.StateKey = CreateColor("State Key", index, resolution,
                GraphicsFormat.R32G32B32A32_UInt);
            slot.HardwareDepth = CreateDepth(index, resolution);
        }

        private static RenderTexture CreateColor(string label, int index,
            Vector2Int resolution, GraphicsFormat format)
        {
            if (!SystemInfo.IsFormatSupported(format,
                    GraphicsFormatUsage.Render))
                throw new InvalidOperationException(
                    $"Required Sigma prediction MRT format is unsupported: {format}.");
            RenderTextureDescriptor descriptor = ArrayDescriptor(resolution);
            descriptor.graphicsFormat = format;
            var texture = new RenderTexture(descriptor)
            {
                name = $"[Sigma-PRISM-16] Prediction {label} {index}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!texture.Create())
                throw new InvalidOperationException(texture.name);
            return texture;
        }

        private static RenderTexture CreateDepth(int index,
            Vector2Int resolution)
        {
            var descriptor = ArrayDescriptor(resolution);
            descriptor.graphicsFormat = GraphicsFormat.None;
            descriptor.depthStencilFormat = GraphicsFormat.D32_SFloat;
            var texture = new RenderTexture(descriptor)
            {
                name = $"[Sigma-PRISM-16] Prediction Hardware Depth {index}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!texture.Create())
                throw new InvalidOperationException(texture.name);
            return texture;
        }

        private static RenderTextureDescriptor ArrayDescriptor(
            Vector2Int resolution) => new(resolution.x, resolution.y)
        {
            depthBufferBits = 0,
            msaaSamples = 1,
            dimension = TextureDimension.Tex2DArray,
            volumeDepth = 2,
            enableRandomWrite = false,
            useMipMap = false,
            autoGenerateMips = false
        };

        private static bool Compatible(RenderTexture texture,
            Vector2Int resolution, GraphicsFormat format) =>
            texture != null && texture.width == resolution.x &&
            texture.height == resolution.y && texture.volumeDepth == 2 &&
            texture.dimension == TextureDimension.Tex2DArray &&
            texture.graphicsFormat == format;

        private static bool CompatibleDepth(RenderTexture texture,
            Vector2Int resolution) => texture != null &&
            texture.width == resolution.x && texture.height == resolution.y &&
            texture.volumeDepth == 2 &&
            texture.dimension == TextureDimension.Tex2DArray &&
            texture.depthStencilFormat == GraphicsFormat.D32_SFloat;

        private static SigmaGpuCompletionStatus PollCompletion(Slot slot,
            out string error)
        {
            if (slot.CompletionFaulted)
            {
                error = "The prediction slot has an unprovable GPU completion.";
                return SigmaGpuCompletionStatus.Faulted;
            }
            if (!slot.HasCompletion)
            {
                error = null;
                return SigmaGpuCompletionStatus.Complete;
            }
            return slot.Completion.Poll(out error);
        }

        private void LatchCompletionFault(Slot slot, string error)
        {
            slot.CompletionFaulted = true;
            if (_completionFault != null)
                return;
            _completionFault = string.IsNullOrWhiteSpace(error)
                ? "Unknown prediction completion failure."
                : error;
            Logger.Error("Sigma prediction ring: " + _completionFault);
        }

        private void TryDestroyRetiredSlot(Slot slot)
        {
            SigmaGpuCompletionStatus status = PollCompletion(slot,
                out string error);
            if (status == SigmaGpuCompletionStatus.Complete)
            {
                DestroySlot(slot);
                return;
            }
            if (status == SigmaGpuCompletionStatus.Faulted)
            {
                LatchCompletionFault(slot, error);
                RetireSlot(slot, true, error);
                return;
            }
            RetireSlot(slot, false, null);
        }

        private static void RetireSlot(Slot slot, bool faulted, string error)
        {
            RenderTexture depthSupport = slot.DepthSupport;
            RenderTexture carrierPage = slot.CarrierPage;
            RenderTexture carrierUvNormal = slot.CarrierUvNormal;
            RenderTexture stateKey = slot.StateKey;
            RenderTexture hardwareDepth = slot.HardwareDepth;
            SigmaGpuCompletionTicket completion = slot.Completion;
            slot.DepthSupport = null;
            slot.CarrierPage = null;
            slot.CarrierUvNormal = null;
            slot.StateKey = null;
            slot.HardwareDepth = null;
            slot.HasCompletion = false;
            slot.CompletionFaulted = false;
            slot.RetireWhenReleased = false;
            Action release = () =>
            {
                DestroyTexture(depthSupport);
                DestroyTexture(carrierPage);
                DestroyTexture(carrierUvNormal);
                DestroyTexture(stateKey);
                DestroyTexture(hardwareDepth);
            };
            if (faulted)
                SigmaGpuRetirement.Quarantine(release,
                    "Sigma retired prediction slot", error);
            else
                SigmaGpuRetirement.Retire(completion, release,
                    "Sigma retired prediction slot");
        }

        private static void DestroySlot(Slot slot)
        {
            DestroyTexture(slot.DepthSupport);
            DestroyTexture(slot.CarrierPage);
            DestroyTexture(slot.CarrierUvNormal);
            DestroyTexture(slot.StateKey);
            DestroyTexture(slot.HardwareDepth);
            slot.DepthSupport = null;
            slot.CarrierPage = null;
            slot.CarrierUvNormal = null;
            slot.StateKey = null;
            slot.HardwareDepth = null;
            slot.HasCompletion = false;
            slot.CompletionFaulted = false;
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
