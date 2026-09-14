using System;
using UnityEngine;

namespace FinalScan.Platform.Sensor
{
    /// <summary>One tracking sample: head pose plus per-eye poses at an XrTime (all in the same space, world on device).</summary>
    public struct PoseSample
    {
        public long xrTimeNs;
        public Pose head;
        public Pose eyeLeft;
        public Pose eyeRight;
        /// <summary>Angular velocity reported by the runtime (rad/s, world), zero when unknown.</summary>
        public Vector3 angularVelocity;
        public Vector3 linearVelocity;
        public bool valid;
    }

    /// <summary>
    /// FinalScan's own pose history (contract §4.3, invariant): OpenXR only guarantees ≥ 50 ms of xrLocateSpace history and
    /// the PCA latency (30 ms p50, 37 ms p99 measured) plus pairing delay eats most of it. The host pushes one sample per
    /// XR frame (72 Hz); the ring covers <see cref="CoverageSeconds"/> (2 s → 160 slots, 144 needed).
    /// <see cref="TryLocate"/> interpolates between bracketing samples (lerp + slerp) and extrapolates at most
    /// <see cref="MaxExtrapolationNs"/> past the newest sample using the last velocity. Pure C#; deterministic in replay.
    /// </summary>
    public sealed class PoseRing
    {
        public const double CoverageSeconds = 2.0;
        public const int NominalRateHz = 72;
        public const int DefaultCapacity = 160;
        public const long MaxExtrapolationNs = 20_000_000; // 20 ms

        readonly PoseSample[] _s;
        int _head, _count;
        public int Capacity => _s.Length;
        public int Count => _count;
        public long Pushed { get; private set; }
        public long RejectedNonMonotonic { get; private set; }
        public long Locates { get; private set; }
        public long LocateInterpolated { get; private set; }
        public long LocateExtrapolated { get; private set; }
        public long LocateFailed { get; private set; }

        public PoseRing(int capacity = DefaultCapacity) { _s = new PoseSample[Math.Max(4, capacity)]; }

        public bool IsEmpty => _count == 0;
        public PoseSample Newest => _count == 0 ? default : _s[(_head - 1 + _s.Length) % _s.Length];
        public PoseSample Oldest => _count == 0 ? default : _s[(_head - _count + _s.Length) % _s.Length];
        public long NewestTimeNs => _count == 0 ? 0 : Newest.xrTimeNs;
        public long OldestTimeNs => _count == 0 ? 0 : Oldest.xrTimeNs;
        /// <summary>Sample i counted from the oldest (0) to the newest (Count-1).</summary>
        public PoseSample At(int i) => _s[(_head - _count + i + _s.Length) % _s.Length];

        /// <summary>Appends a sample. Samples must arrive in increasing XrTime; equal or older times are rejected and counted.</summary>
        public bool Push(in PoseSample sample)
        {
            if (!sample.valid || sample.xrTimeNs <= 0) return false;
            if (_count > 0 && sample.xrTimeNs <= NewestTimeNs) { RejectedNonMonotonic++; return false; }
            _s[_head] = sample;
            _head = (_head + 1) % _s.Length;
            if (_count < _s.Length) _count++;
            Pushed++;
            return true;
        }

        public bool Push(long xrTimeNs, in Pose head, in Pose eyeLeft, in Pose eyeRight, Vector3 angularVelocity = default, Vector3 linearVelocity = default)
            => Push(new PoseSample { xrTimeNs = xrTimeNs, head = head, eyeLeft = eyeLeft, eyeRight = eyeRight, angularVelocity = angularVelocity, linearVelocity = linearVelocity, valid = true });

        public void Clear() { _head = 0; _count = 0; }

        /// <summary>Head pose at <paramref name="xrTimeNs"/>. Fails outside [oldest, newest + 20 ms].</summary>
        public bool TryLocate(long xrTimeNs, out Pose pose) => TryLocate(xrTimeNs, PoseNode.Head, out pose, out _);

        public bool TryLocate(long xrTimeNs, PoseNode node, out Pose pose) => TryLocate(xrTimeNs, node, out pose, out _);

        /// <summary>Locates a node pose; <paramref name="extrapolatedNs"/> is &gt; 0 when the answer lies past the newest sample.</summary>
        public bool TryLocate(long xrTimeNs, PoseNode node, out Pose pose, out long extrapolatedNs)
        {
            Locates++;
            pose = Pose.identity; extrapolatedNs = 0;
            if (_count == 0 || xrTimeNs <= 0) { LocateFailed++; return false; }
            long newestT = NewestTimeNs;
            if (xrTimeNs >= newestT)
            {
                extrapolatedNs = xrTimeNs - newestT;
                if (extrapolatedNs > MaxExtrapolationNs) { LocateFailed++; return false; }
                var n = Newest;
                if (extrapolatedNs == 0 || _count < 2) { pose = Get(n, node); LocateInterpolated++; return true; }
                var p = At(_count - 2);
                double dt = (n.xrTimeNs - p.xrTimeNs) * 1e-9;
                if (dt <= 0) { pose = Get(n, node); LocateInterpolated++; return true; }
                float t = (float)((xrTimeNs - p.xrTimeNs) * 1e-9 / dt); // > 1
                pose = SensorMath.Lerp(Get(p, node), Get(n, node), t);
                LocateExtrapolated++;
                return true;
            }
            if (xrTimeNs < OldestTimeNs) { LocateFailed++; return false; }
            // binary search over logical indices for the first sample with time > xrTimeNs
            int lo = 0, hi = _count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (At(mid).xrTimeNs < xrTimeNs) lo = mid + 1; else hi = mid;
            }
            var b = At(lo);
            if (b.xrTimeNs == xrTimeNs || lo == 0) { pose = Get(b, node); LocateInterpolated++; return true; }
            var a = At(lo - 1);
            double span = (b.xrTimeNs - a.xrTimeNs) * 1e-9;
            float u = span <= 0 ? 1f : (float)((xrTimeNs - a.xrTimeNs) * 1e-9 / span);
            pose = SensorMath.Lerp(Get(a, node), Get(b, node), u);
            LocateInterpolated++;
            return true;
        }

        /// <summary>
        /// Peak head angular speed (deg/s) and linear speed (m/s) between consecutive samples inside [fromNs, toNs]
        /// (window widened to at least one sample pair). Returns false when the ring does not cover the window.
        /// </summary>
        public bool TryMotion(long fromNs, long toNs, out float angularDegPerSec, out float linearMetersPerSec)
        {
            angularDegPerSec = 0; linearMetersPerSec = 0;
            if (_count < 2) return false;
            if (toNs < fromNs) { long t = toNs; toNs = fromNs; fromNs = t; }
            if (fromNs < OldestTimeNs - 1 || toNs > NewestTimeNs + MaxExtrapolationNs) return false;
            int first = 0;
            for (int i = 0; i < _count; i++) { if (At(i).xrTimeNs >= fromNs) { first = Math.Max(0, i - 1); break; } first = i; }
            bool any = false;
            for (int i = first + 1; i < _count; i++)
            {
                var a = At(i - 1); var b = At(i);
                if (a.xrTimeNs > toNs && any) break;
                double dt = (b.xrTimeNs - a.xrTimeNs) * 1e-9;
                if (dt <= 0) continue;
                float ang = SensorMath.AngleDeg(a.head.rotation, b.head.rotation) / (float)dt;
                float lin = (b.head.position - a.head.position).magnitude / (float)dt;
                if (ang > angularDegPerSec) angularDegPerSec = ang;
                if (lin > linearMetersPerSec) linearMetersPerSec = lin;
                any = true;
                if (b.xrTimeNs >= toNs) break;
            }
            return any;
        }

        static Pose Get(in PoseSample s, PoseNode node)
        {
            switch (node)
            {
                case PoseNode.EyeLeft: return s.eyeLeft;
                case PoseNode.EyeRight: return s.eyeRight;
                default: return s.head;
            }
        }
    }

    public enum PoseNode { Head = 0, EyeLeft = 1, EyeRight = 2 }
}
