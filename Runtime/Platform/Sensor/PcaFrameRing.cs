using System;

namespace FinalScan.Platform.Sensor
{
    /// <summary>
    /// Per-eye history ring of <see cref="CameraFrameLease"/> (contract §4.4: history per eye, never latest-only).
    /// Depth 8 covers ~270 ms at 30 Hz / 160 ms at 50 Hz, more than PCA latency + pairing delay.
    ///
    /// Validation on push: capture timestamps must increase strictly (the HAL was seen to violate monotonicity on other
    /// devices; C01 measured zero here, the counter stays because the contract requires it); a frame older than
    /// <see cref="StaleNs"/> at receive time is dropped as stale. Both drops release the lease immediately.
    /// The ring holds one reference per entry; eviction releases it.
    /// </summary>
    public sealed class PcaFrameRing
    {
        public const int DefaultDepth = 8;
        public const long StaleNs = 500_000_000; // 0.5 s: older than any useful observation (§3.3 stale drop)

        readonly CameraFrameLease[] _e;
        int _head, _count;
        public CameraEye Eye { get; }
        public int Depth => _e.Length;
        public int Count => _count;
        public long Accepted { get; private set; }
        public long DroppedNonMonotonic { get; private set; }
        public long DroppedStale { get; private set; }
        public long DroppedInvalidTime { get; private set; }
        public long Evicted { get; private set; }
        public long NewestTimeNs { get; private set; }
        public long LastFrameId { get; private set; }

        public PcaFrameRing(CameraEye eye, int depth = DefaultDepth) { Eye = eye; _e = new CameraFrameLease[Math.Max(2, depth)]; }

        public CameraFrameLease At(int i) => _e[(_head - _count + i + _e.Length) % _e.Length];
        public CameraFrameLease Newest => _count == 0 ? null : _e[(_head - 1 + _e.Length) % _e.Length];

        /// <summary>Takes ownership of the caller's reference. Returns false (and releases) when the frame is rejected.</summary>
        public bool Push(CameraFrameLease lease)
        {
            if (lease == null) return false;
            if (lease.xrTimeNs <= 0) { DroppedInvalidTime++; lease.Release(); return false; }
            if (_count > 0 && lease.xrTimeNs <= NewestTimeNs) { DroppedNonMonotonic++; lease.Release(); return false; }
            if (lease.receiveXrTimeNs > 0 && lease.receiveXrTimeNs - lease.xrTimeNs > StaleNs) { DroppedStale++; lease.Release(); return false; }
            if (_count == _e.Length) EvictOldest();
            _e[_head] = lease; _head = (_head + 1) % _e.Length; _count++;
            NewestTimeNs = lease.xrTimeNs; LastFrameId = lease.frameId; Accepted++;
            return true;
        }

        /// <summary>Releases the ring's reference on the oldest entry (a StereoObservation may still hold it).</summary>
        public bool EvictOldest()
        {
            if (_count == 0) return false;
            int idx = (_head - _count + _e.Length) % _e.Length;
            var l = _e[idx]; _e[idx] = null; _count--; Evicted++;
            l?.Release();
            return true;
        }

        public void Clear() { while (_count > 0) EvictOldest(); NewestTimeNs = 0; }

        public CameraFrameLease Find(long frameId)
        {
            for (int i = 0; i < _count; i++) { var l = At(i); if (l != null && l.frameId == frameId) return l; }
            return null;
        }
    }
}
