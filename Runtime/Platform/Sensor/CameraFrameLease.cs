using System;
using UnityEngine;

namespace FinalScan.Platform.Sensor
{
    public enum CameraEye { Left = 0, Right = 1 }

    public enum PairState { Pending = 0, Paired = 1, Expired = 2, Rejected = 3 }

    /// <summary>Pinhole intrinsics of one PCA stream (MRUK PassthroughCameraAccess.CameraIntrinsics flattened; §4.6).</summary>
    [Serializable]
    public struct CameraIntrinsicsData
    {
        public float fx, fy, cx, cy;
        /// <summary>Delivered image size the intrinsics refer to.</summary>
        public int width, height;
        /// <summary>Full sensor resolution reported by MRUK (1280x1280 measured).</summary>
        public int sensorWidth, sensorHeight;
        /// <summary>Camera pose relative to the head (MRUK LensOffset). Baseline = |offsetR - offsetL| = 63.4 mm measured.</summary>
        public Pose lensOffset;
        public bool valid;

        public static CameraIntrinsicsData Create(float fx, float fy, float cx, float cy, int w, int h, int sw, int sh, Pose lensOffset)
            => new CameraIntrinsicsData { fx = fx, fy = fy, cx = cx, cy = cy, width = w, height = h, sensorWidth = sw, sensorHeight = sh, lensOffset = lensOffset, valid = fx > 0 && fy > 0 && w > 0 && h > 0 };
    }

    /// <summary>
    /// The one internal frame type (contract §5): image handle + timestamp with uncertainty + pose + intrinsics + profile + lease
    /// lifetime. Reference counted: the per-eye ring holds one reference, every StereoObservation that uses the frame holds
    /// another; the texture slot returns to the pool when the last holder releases. The texture is an owned copy made in the
    /// producer callback (donor lesson RGBD-6: never keep the producer's texture, it is rewritten on the render thread).
    /// </summary>
    public sealed class CameraFrameLease
    {
        public long frameId;
        public CameraEye eye;
        /// <summary>Capture time in XrTime ns (§4.1) with its uncertainty and the gate path that produced it (§4.2).</summary>
        public long xrTimeNs;
        public long uncertaintyNs;
        public ClockGatePath gatePath;
        /// <summary>Head pose at capture time (world) and the camera pose derived through the lens offset.</summary>
        public Pose headPose;
        public Pose cameraPose;
        /// <summary>True when the runtime located the head at the capture time itself (xrLocateSpace on the callback), false when interpolated from the ring.</summary>
        public bool poseFromRuntime;
        public bool poseValid;
        public CameraIntrinsicsData intrinsics;
        public CaptureProfile profile;
        /// <summary>Owned texture copy (RenderTexture from the per-eye pool) or null in metadata-only replay.</summary>
        public Texture texture;
        public int poolSlot = -1;
        /// <summary>CLOCK_MONOTONIC ns when the frame became visible to managed code, and the same instant as XrTime.</summary>
        public long receiveMonotonicNs;
        public long receiveXrTimeNs;
        /// <summary>Raw producer timestamps kept for recording: DateTime ticks (CLOCK_REALTIME) and MRUK's monotonic field.</summary>
        public long sourceUtcTicks;
        public long sourceMonoNs;
        /// <summary>Pairing bookkeeping owned by <see cref="StereoPairer"/>.</summary>
        public PairState pairState;

        int _refs;
        Action<CameraFrameLease> _onFree;
        public int RefCount => _refs;
        public bool Alive => _refs > 0;
        public long LatencyNs => receiveXrTimeNs > 0 && xrTimeNs > 0 ? receiveXrTimeNs - xrTimeNs : 0;

        /// <summary>Starts the lifetime with one reference; <paramref name="onFree"/> runs when the last reference is released.</summary>
        public void Begin(Action<CameraFrameLease> onFree) { _refs = 1; _onFree = onFree; }
        public CameraFrameLease Acquire() { if (_refs <= 0) throw new InvalidOperationException("lease already freed"); _refs++; return this; }
        public void Release()
        {
            if (_refs <= 0) return;
            if (--_refs == 0) { var cb = _onFree; _onFree = null; cb?.Invoke(this); }
        }
    }

    /// <summary>
    /// Fixed pool of texture slots per eye. Slot textures are created by the host (RenderTexture) or left null (replay/tests);
    /// the pool only tracks ownership. Exhaustion is counted, never blocks.
    /// </summary>
    public sealed class FrameTexturePool
    {
        readonly Texture[] _textures;
        readonly bool[] _free;
        public int Capacity => _textures.Length;
        public int FreeCount { get; private set; }
        public long Exhausted { get; private set; }

        public FrameTexturePool(int capacity)
        {
            _textures = new Texture[Math.Max(1, capacity)];
            _free = new bool[_textures.Length];
            for (int i = 0; i < _free.Length; i++) _free[i] = true;
            FreeCount = _free.Length;
        }

        public void SetTexture(int slot, Texture texture) => _textures[slot] = texture;
        public Texture GetTexture(int slot) => slot >= 0 && slot < _textures.Length ? _textures[slot] : null;

        public bool TryAcquire(out int slot)
        {
            for (int i = 0; i < _free.Length; i++)
                if (_free[i]) { _free[i] = false; FreeCount--; slot = i; return true; }
            Exhausted++; slot = -1; return false;
        }

        public void ReleaseSlot(int slot)
        {
            if (slot < 0 || slot >= _free.Length || _free[slot]) return;
            _free[slot] = true; FreeCount++;
        }

        /// <summary>Returns every texture for the host to destroy; the pool is unusable afterwards.</summary>
        public Texture[] DrainTextures()
        {
            var t = (Texture[])_textures.Clone();
            for (int i = 0; i < _textures.Length; i++) _textures[i] = null;
            return t;
        }
    }
}
