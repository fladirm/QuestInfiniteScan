using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    internal enum GpuTextureCopyMode : byte
    {
        Color = 0,
        ProjectionDepthArray = 1
    }

    /// <summary>
    /// A ref-counted view of one immutable GPU copy. Holding the lease prevents its
    /// preallocated ring slot from being overwritten.
    /// </summary>
    internal sealed class GpuTextureLease : IDisposable
    {
        private GpuTextureRing _owner;
        private readonly int _slot;
        private readonly uint _generation;

        internal GpuTextureLease(GpuTextureRing owner, int slot, uint generation)
        {
            _owner = owner;
            _slot = slot;
            _generation = generation;
        }

        internal Texture Texture
        {
            get
            {
                if (_owner == null)
                    throw new ObjectDisposedException(nameof(GpuTextureLease));
                return _owner.GetTexture(_slot, _generation);
            }
        }

        internal GraphicsFormat GraphicsFormat => Texture.graphicsFormat;

        internal GpuTextureLease Retain()
        {
            if (_owner == null)
                throw new ObjectDisposedException(nameof(GpuTextureLease));
            _owner.Retain(_slot, _generation);
            return new GpuTextureLease(_owner, _slot, _generation);
        }

        public void Dispose()
        {
            GpuTextureRing owner = _owner;
            if (owner == null)
                return;
            _owner = null;
            owner.Release(_slot, _generation);
        }
    }

    /// <summary>
    /// Preallocated GPU-to-GPU copy ring. It never reads pixels on CPU and never waits on
    /// a fence; unavailable slots cause fail-closed frame rejection/backpressure.
    /// </summary>
    internal sealed class GpuTextureRing : IDisposable
    {
        private sealed class Slot
        {
            internal RenderTexture Texture;
            internal uint Generation;
            internal int References;
            internal SigmaGpuCompletionTicket Completion;
            internal bool HasCompletion;
            internal bool CompletionFaulted;
            internal bool RetireWhenReleased;
        }

        private readonly Slot[] _slots;
        private readonly string _name;
        private readonly GraphicsFormat _fallbackFormat;
        private readonly GpuTextureCopyMode _copyMode;
        private readonly ComputeShader _imageCopyCompute;
        private readonly int _copyDepthArrayKernel;
        private int _cursor;
        private bool _disposed;
        private string _completionFault;

        internal GpuTextureRing(string name, int capacity,
            GraphicsFormat fallbackFormat = GraphicsFormat.R8G8B8A8_UNorm,
            GpuTextureCopyMode copyMode = GpuTextureCopyMode.Color)
        {
            if (capacity < 3)
                throw new ArgumentOutOfRangeException(nameof(capacity),
                    "A capture ring needs at least three slots.");
            SigmaGpuCompletion.RequireSupported();
            _name = string.IsNullOrWhiteSpace(name) ? "RigCapture" : name;
            _fallbackFormat = fallbackFormat;
            _copyMode = copyMode;
            if (_copyMode == GpuTextureCopyMode.ProjectionDepthArray)
            {
                _imageCopyCompute = Resources.Load<ComputeShader>(
                    "SigmaPrism/RigImageCopy");
                if (_imageCopyCompute == null)
                    throw new InvalidOperationException(
                        "Sigma-PRISM-16 depth-copy compute resource is missing.");
                _copyDepthArrayKernel = _imageCopyCompute.FindKernel(
                    "CopyProjectionDepthArray");
            }
            _slots = new Slot[capacity];
            for (int i = 0; i < capacity; i++)
                _slots[i] = new Slot();
        }

        internal int Capacity => _slots.Length;

        internal bool TryCopy(Texture source, out GpuTextureLease lease,
            out RigFrameRejectionReason rejection)
        {
            lease = null;
            rejection = RigFrameRejectionReason.None;
            if (_disposed || source == null)
            {
                rejection = RigFrameRejectionReason.MissingTexture;
                return false;
            }
            if (_completionFault != null)
            {
                rejection = RigFrameRejectionReason.GpuCopyFailed;
                return false;
            }

            if (!IsSupportedDimension(source) || source.width <= 0 ||
                source.height <= 0)
            {
                rejection = RigFrameRejectionReason.UnsupportedTexture;
                return false;
            }

            int selected = -1;
            for (int offset = 0; offset < _slots.Length; offset++)
            {
                int index = (_cursor + offset) % _slots.Length;
                Slot slot = _slots[index];
                if (slot.References != 0)
                    continue;
                SigmaGpuCompletionStatus status = PollCompletion(slot,
                    out string completionError);
                if (status == SigmaGpuCompletionStatus.Faulted)
                {
                    LatchCompletionFault(slot, completionError);
                    rejection = RigFrameRejectionReason.GpuCopyFailed;
                    return false;
                }
                if (status != SigmaGpuCompletionStatus.Complete)
                    continue;
                selected = index;
                break;
            }

            if (selected < 0)
            {
                rejection = RigFrameRejectionReason.RingExhausted;
                return false;
            }

            Slot target = _slots[selected];
            try
            {
                EnsureCompatibleTarget(target, source, selected);
                CopyOnGpu(source, target.Texture);
                target.Completion = SigmaGpuCompletion.InsertAfterGraphicsWork();
                target.HasCompletion = true;
                target.CompletionFaulted = false;

                target.Generation = NextGeneration(target.Generation);
                target.References = 1;
                _cursor = (selected + 1) % _slots.Length;
                lease = new GpuTextureLease(this, selected, target.Generation);
                return true;
            }
            catch (Exception exception)
            {
                target.CompletionFaulted = true;
                LatchCompletionFault(target,
                    $"GPU copy submission could not be fenced: {exception.Message}");
                Logger.Warning($"{_name}: GPU frame copy failed: {exception.Message}");
                rejection = RigFrameRejectionReason.GpuCopyFailed;
                return false;
            }
        }

        internal Texture GetTexture(int slotIndex, uint generation)
        {
            Slot slot = ValidateLease(slotIndex, generation);
            return slot.Texture;
        }

        internal void Retain(int slotIndex, uint generation)
        {
            Slot slot = ValidateLease(slotIndex, generation);
            checked { slot.References++; }
        }

        internal void Release(int slotIndex, uint generation)
        {
            if ((uint)slotIndex >= (uint)_slots.Length)
                return;
            Slot slot = _slots[slotIndex];
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

        private Slot ValidateLease(int slotIndex, uint generation)
        {
            if ((uint)slotIndex >= (uint)_slots.Length)
                throw new ObjectDisposedException(nameof(GpuTextureLease));
            Slot slot = _slots[slotIndex];
            if (slot.Generation != generation || slot.References <= 0 || slot.Texture == null)
                throw new ObjectDisposedException(nameof(GpuTextureLease));
            return slot;
        }

        private static SigmaGpuCompletionStatus PollCompletion(Slot slot,
            out string error)
        {
            if (slot.CompletionFaulted)
            {
                error = "The capture slot has an unprovable GPU completion.";
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
                ? "Unknown GPU completion failure."
                : error;
            Logger.Error($"{_name}: {_completionFault}");
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

        private void RetireSlot(Slot slot, bool faulted, string error)
        {
            RenderTexture texture = slot.Texture;
            SigmaGpuCompletionTicket completion = slot.Completion;
            slot.Texture = null;
            slot.HasCompletion = false;
            slot.CompletionFaulted = false;
            slot.RetireWhenReleased = false;
            Action release = () => DestroyTexture(texture);
            if (faulted)
                SigmaGpuRetirement.Quarantine(release,
                    $"{_name} retired capture slot", error);
            else
                SigmaGpuRetirement.Retire(completion, release,
                    $"{_name} retired capture slot");
        }

        private void EnsureCompatibleTarget(Slot slot, Texture source, int slotIndex)
        {
            int volumeDepth = _copyMode == GpuTextureCopyMode.ProjectionDepthArray
                ? Math.Max(2, GetVolumeDepth(source))
                : GetVolumeDepth(source);
            TextureDimension targetDimension =
                _copyMode == GpuTextureCopyMode.ProjectionDepthArray
                    ? TextureDimension.Tex2DArray
                    : TargetDimension(source);
            GraphicsFormat targetFormat =
                _copyMode == GpuTextureCopyMode.ProjectionDepthArray
                    ? _fallbackFormat
                    : TargetFormat(source);
            if (slot.Texture != null && slot.Texture.width == source.width &&
                slot.Texture.height == source.height &&
                slot.Texture.dimension == targetDimension &&
                slot.Texture.volumeDepth == volumeDepth &&
                slot.Texture.graphicsFormat == targetFormat)
            {
                return;
            }

            DestroySlot(slot);
            var descriptor = new RenderTextureDescriptor(source.width, source.height)
            {
                graphicsFormat = targetFormat,
                depthBufferBits = 0,
                msaaSamples = 1,
                dimension = targetDimension,
                volumeDepth = volumeDepth,
                useMipMap = false,
                autoGenerateMips = false,
                enableRandomWrite =
                    _copyMode == GpuTextureCopyMode.ProjectionDepthArray
            };
            slot.Texture = new RenderTexture(descriptor)
            {
                name = $"[{_name}] Slot {slotIndex}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!slot.Texture.Create())
                throw new InvalidOperationException("RenderTexture.Create returned false.");
        }

        private static int GetVolumeDepth(Texture texture)
        {
            if (texture is RenderTexture renderTexture)
                return Math.Max(1, renderTexture.volumeDepth);
            if (texture is Texture2DArray textureArray)
                return Math.Max(1, textureArray.depth);
            return 1;
        }

        private static bool IsSupportedDimension(Texture texture)
        {
            TextureDimension dimension = texture.dimension;
            return dimension == TextureDimension.Tex2D ||
                   dimension == TextureDimension.Tex2DArray ||
                   dimension == TextureDimension.Unknown &&
                   texture is not Cubemap && texture is not Texture3D;
        }

        private static TextureDimension TargetDimension(Texture source) =>
            source.dimension == TextureDimension.Tex2DArray
                ? TextureDimension.Tex2DArray
                : TextureDimension.Tex2D;

        private GraphicsFormat TargetFormat(Texture source) =>
            source.graphicsFormat != GraphicsFormat.None
                ? source.graphicsFormat
                : _fallbackFormat;

        private void CopyOnGpu(Texture source, RenderTexture target)
        {
            if (_copyMode == GpuTextureCopyMode.ProjectionDepthArray)
            {
                int slices = Math.Min(GetVolumeDepth(source), target.volumeDepth);
                if (slices < 2)
                    throw new InvalidOperationException(
                        "Quest environment depth did not expose both array layers.");
                _imageCopyCompute.SetInts("_Resolution", source.width,
                    source.height);
                _imageCopyCompute.SetInt("_SliceCount", slices);
                _imageCopyCompute.SetTexture(_copyDepthArrayKernel,
                    "_SourceProjectionDepth", source);
                _imageCopyCompute.SetTexture(_copyDepthArrayKernel,
                    "_TargetProjectionDepth", target);
                _imageCopyCompute.Dispatch(_copyDepthArrayKernel,
                    Math.Max(1, (source.width + 7) / 8),
                    Math.Max(1, (source.height + 7) / 8), slices);
                return;
            }

            bool exactCopy = source.dimension != TextureDimension.Unknown &&
                             source.graphicsFormat != GraphicsFormat.None &&
                             source.graphicsFormat == target.graphicsFormat;
            if (exactCopy)
            {
                Graphics.CopyTexture(source, target);
                return;
            }
            // Passthrough camera textures can be external/format-less on Horizon OS.
            // Blit remains GPU-only and performs external-image format conversion.
            // Array slices must be selected explicitly; a plain array Blit does not
            // preserve the two eye layers on Vulkan.
            if (target.dimension == TextureDimension.Tex2DArray)
            {
                int slices = Math.Min(GetVolumeDepth(source), target.volumeDepth);
                for (int slice = 0; slice < slices; slice++)
                    Graphics.Blit(source, target, slice, slice);
                return;
            }
            Graphics.Blit(source, target);
        }

        private static uint NextGeneration(uint current) => current == uint.MaxValue ? 1u : current + 1u;

        private static void DestroySlot(Slot slot)
        {
            DestroyTexture(slot.Texture);
            slot.Texture = null;
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
    }
}
