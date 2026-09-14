using System;
using System.Collections.Generic;
using UnityEngine;

namespace FinalScan.Platform.Sensor
{
    /// <summary>Skew class of an L/R pair (contract §4.4): use directly / motion-compensate / lower confidence / reject.</summary>
    public enum SkewClass { Direct = 0, MotionCompensate = 1, LowConfidence = 2, Reject = 3 }

    /// <summary>
    /// One coherent stereo observation: L/R frame ids, capture times, poses, skew, motion during the skew, intrinsics L/R,
    /// extrinsics from the two camera poses. Immutable after creation except lease release (textures may go away when the
    /// observation ages out of the texture-retaining part of the history; metadata stays).
    /// </summary>
    public sealed class StereoObservation
    {
        public uint observationId;
        public long frameIdL, frameIdR;
        public long xrTimeNsL, xrTimeNsR;
        public long uncertaintyNsL, uncertaintyNsR;
        public ClockGatePath gatePath;
        /// <summary>Camera poses (world) at each capture time and the head poses they derive from.</summary>
        public Pose cameraPoseL, cameraPoseR, headPoseL, headPoseR;
        public CameraIntrinsicsData intrinsicsL, intrinsicsR;
        /// <summary>Right camera expressed in the left camera frame, from the two located camera poses (includes head motion during the skew).</summary>
        public Pose extrinsicsRightInLeft;
        /// <summary>Static rig extrinsics from the lens offsets (for rectification maps, §4.6).</summary>
        public Pose rigRightInLeft;
        public float baselineMeters;
        public long skewNs;                 // tR - tL (signed)
        public SkewClass skew;
        public float confidence;            // 1 direct, 0.7 motion-compensated, 0.4 low; halved when timestamp uncertainty is degraded
        public bool uncertaintyDegraded;
        public float headAngularDegPerSec, headLinearMetersPerSec;
        public MotionVerdict motion;
        public bool geometryEvidence;
        public float blurPenalty;
        public CaptureProfile profile;
        /// <summary>Time of the later frame at creation (used for stale checks and tick pacing).</summary>
        public long createdXrTimeNs;
        public long receiveXrTimeNs;

        public CameraFrameLease leaseL, leaseR;
        public bool HasTextures => leaseL != null && leaseR != null && leaseL.Alive && leaseR.Alive;
        public Texture textureL => leaseL?.texture;
        public Texture textureR => leaseR?.texture;
        public long MidTimeNs => (xrTimeNsL + xrTimeNsR) / 2;

        internal void ReleaseLeases()
        {
            leaseL?.Release(); leaseR?.Release(); leaseL = null; leaseR = null;
        }
    }

    /// <summary>
    /// Pairs the two per-eye rings (contract §4.4). Evidence: L/R capture deltas quantised to 0/±20/40 ms at 30 Hz and
    /// 0/20 ms at 50 Hz (sensor base clock 50 Hz), 46 % acceptance with latest-only pairing on the donor, so:
    ///  * candidates = all pending frames of both rings, pair with minimal |tL−tR| inside the window
    ///    (<see cref="WindowPeriods"/> × measured period, never a fixed 1/30 s);
    ///  * a pair is committed only when it is settled: no frame that can still arrive on the earlier eye could beat it
    ///    (|dt| ≤ period/2, or a newer frame already exists on that eye, or that eye is stale);
    ///  * skew classes: &lt; <see cref="DirectNs"/> direct, &lt; period/2 motion-compensate (head velocity attached),
    ///    &lt; period low confidence, otherwise reject;
    ///  * a pending frame the other eye has already passed by more than the window expires (counted).
    /// Publishes <see cref="Latest"/> plus an 8-deep history for temporal multiview (C11); the newest
    /// <see cref="TextureHistoryDepth"/> observations keep their texture leases, older ones keep metadata only.
    /// </summary>
    public sealed class StereoPairer
    {
        public const double WindowPeriods = 1.5;
        public const long DirectNs = 4_000_000;
        public const int HistoryDepth = 8;
        public const int TextureHistoryDepth = 2;

        readonly PcaFrameRing _l, _r;
        readonly CadenceMeter _cl, _cr;
        readonly PoseRing _poses;
        readonly MotionGate _gate;
        readonly double _nominalPeriodNs;
        readonly List<StereoObservation> _history = new List<StereoObservation>(HistoryDepth + 1);
        uint _nextId = 1;

        public StereoObservation Latest { get; private set; }
        public IReadOnlyList<StereoObservation> History => _history;
        public long Pairs { get; private set; }
        public long RejectedBySkew { get; private set; }
        public long RejectedByUncertainty { get; private set; }
        public long RejectedByWindow => ExpiredUnpaired;
        public long ExpiredUnpaired { get; private set; }
        public long Direct { get; private set; }
        public long MotionCompensated { get; private set; }
        public long LowConfidence { get; private set; }
        public long MotionGated { get; private set; }
        public double LastWindowNs { get; private set; }
        public double LastPeriodNs { get; private set; }
        public long LastSkewNs { get; private set; }
        public long UncertaintyRejectNs = XrClock.DegradedLimitNs;
        public long UncertaintyDegradeNs = XrClock.GoodLimitNs;
        /// <summary>Raised for every committed observation (also the rejected/gated ones are not raised).</summary>
        public event Action<StereoObservation> ObservationCreated;

        public StereoPairer(PcaFrameRing left, PcaFrameRing right, CadenceMeter cadenceLeft, CadenceMeter cadenceRight, PoseRing poses, MotionGate gate, double nominalPeriodNs)
        {
            _l = left; _r = right; _cl = cadenceLeft; _cr = cadenceRight; _poses = poses; _gate = gate ?? new MotionGate();
            _nominalPeriodNs = nominalPeriodNs > 0 ? nominalPeriodNs : 1e9 / 30.0;
        }

        /// <summary>Period used for the window: the slower measured eye, nominal until measured.</summary>
        public double PeriodNs
        {
            get
            {
                double a = _cl != null && _cl.PeriodMeasured ? _cl.MedianPeriodNs : 0;
                double b = _cr != null && _cr.PeriodMeasured ? _cr.MedianPeriodNs : 0;
                double p = Math.Max(a, b);
                return p > 0 ? p : _nominalPeriodNs;
            }
        }

        /// <summary>Runs the matching over the current rings; call after every frame push. Returns the number of observations committed.</summary>
        public int Update(long nowXrTimeNs, CaptureProfile profile)
        {
            double period = PeriodNs; LastPeriodNs = period;
            long window = (long)(WindowPeriods * period); LastWindowNs = window;
            int committed = 0;
            for (int guard = 0; guard < 16; guard++)
            {
                if (!FindBest(window, out var bl, out var br, out long absDt)) break;
                if (!Settled(bl, br, period, window)) break;
                Commit(bl, br, period, profile, nowXrTimeNs);
                committed++;
            }
            Expire(window);
            return committed;
        }

        bool FindBest(long window, out CameraFrameLease bestL, out CameraFrameLease bestR, out long bestAbs)
        {
            bestL = null; bestR = null; bestAbs = long.MaxValue;
            for (int i = 0; i < _l.Count; i++)
            {
                var l = _l.At(i); if (l == null || l.pairState != PairState.Pending) continue;
                for (int j = 0; j < _r.Count; j++)
                {
                    var r = _r.At(j); if (r == null || r.pairState != PairState.Pending) continue;
                    long d = Math.Abs(r.xrTimeNs - l.xrTimeNs);
                    if (d > window) continue;
                    // ties (quantised 0/20/40 ms deltas are common): take the oldest pair so pairing stays sequential
                    if (d < bestAbs || (d == bestAbs && l.xrTimeNs + r.xrTimeNs < bestL.xrTimeNs + bestR.xrTimeNs)) { bestAbs = d; bestL = l; bestR = r; }
                }
            }
            return bestL != null;
        }

        /// <summary>No frame that can still arrive on the earlier eye can be closer to the later frame than this candidate.</summary>
        bool Settled(CameraFrameLease l, CameraFrameLease r, double period, long window)
        {
            long dt = r.xrTimeNs - l.xrTimeNs;
            if (Math.Abs(dt) <= period * 0.5 + 1) return true;
            // earlier frame's eye: a newer frame on it would arrive at >= newest + period
            var earlierRing = dt > 0 ? _l : _r;
            long laterT = dt > 0 ? r.xrTimeNs : l.xrTimeNs;
            long earlierNewest = earlierRing.NewestTimeNs;
            if (earlierNewest > (dt > 0 ? l.xrTimeNs : r.xrTimeNs)) return true;          // a newer frame exists and was not closer
            long expectedNext = earlierNewest + (long)period;
            if (Math.Abs(expectedNext - laterT) >= Math.Abs(dt)) return true;             // the next frame cannot beat this one
            long otherNewest = (dt > 0 ? _r : _l).NewestTimeNs;
            if (otherNewest - earlierNewest > window) return true;                         // earlier eye is stale, stop waiting
            return false;
        }

        void Commit(CameraFrameLease l, CameraFrameLease r, double period, CaptureProfile profile, long nowNs)
        {
            long dt = r.xrTimeNs - l.xrTimeNs; long adt = Math.Abs(dt);
            LastSkewNs = dt;
            SkewClass cls = adt < DirectNs ? SkewClass.Direct : adt < period * 0.5 ? SkewClass.MotionCompensate : adt < period ? SkewClass.LowConfidence : SkewClass.Reject;
            long unc = Math.Max(l.uncertaintyNs, r.uncertaintyNs);
            if (cls == SkewClass.Reject) { RejectedBySkew++; l.pairState = PairState.Rejected; r.pairState = PairState.Rejected; return; }
            if (unc >= UncertaintyRejectNs) { RejectedByUncertainty++; l.pairState = PairState.Rejected; r.pairState = PairState.Rejected; return; }
            l.pairState = PairState.Paired; r.pairState = PairState.Paired;

            var o = new StereoObservation
            {
                observationId = _nextId++, frameIdL = l.frameId, frameIdR = r.frameId,
                xrTimeNsL = l.xrTimeNs, xrTimeNsR = r.xrTimeNs, uncertaintyNsL = l.uncertaintyNs, uncertaintyNsR = r.uncertaintyNs,
                gatePath = l.gatePath == r.gatePath ? l.gatePath : (l.gatePath > r.gatePath ? l.gatePath : r.gatePath),
                cameraPoseL = l.cameraPose, cameraPoseR = r.cameraPose, headPoseL = l.headPose, headPoseR = r.headPose,
                intrinsicsL = l.intrinsics, intrinsicsR = r.intrinsics,
                skewNs = dt, skew = cls, profile = profile, createdXrTimeNs = Math.Max(l.xrTimeNs, r.xrTimeNs), receiveXrTimeNs = nowNs,
                uncertaintyDegraded = unc >= UncertaintyDegradeNs,
                leaseL = l.Acquire(), leaseR = r.Acquire()
            };
            o.extrinsicsRightInLeft = SensorMath.Relative(l.cameraPose, r.cameraPose);
            o.rigRightInLeft = SensorMath.Relative(l.intrinsics.lensOffset, r.intrinsics.lensOffset);
            o.baselineMeters = o.rigRightInLeft.position.magnitude;
            o.confidence = cls == SkewClass.Direct ? 1f : cls == SkewClass.MotionCompensate ? 0.7f : 0.4f;
            if (o.uncertaintyDegraded) o.confidence *= 0.5f;
            o.motion = _gate.Evaluate(_poses, l.xrTimeNs, r.xrTimeNs, out o.headAngularDegPerSec, out o.headLinearMetersPerSec);
            o.geometryEvidence = o.motion == MotionVerdict.GeometryEvidence && l.poseValid && r.poseValid;
            o.blurPenalty = _gate.BlurPenalty(o.headAngularDegPerSec);
            if (o.motion == MotionVerdict.NotGeometryEvidence) MotionGated++;
            switch (cls) { case SkewClass.Direct: Direct++; break; case SkewClass.MotionCompensate: MotionCompensated++; break; default: LowConfidence++; break; }
            Pairs++;

            _history.Add(o);
            while (_history.Count > HistoryDepth) { _history[0].ReleaseLeases(); _history.RemoveAt(0); }
            for (int i = 0; i < _history.Count - TextureHistoryDepth; i++) _history[i].ReleaseLeases();
            Latest = o;
            ObservationCreated?.Invoke(o);
        }

        void Expire(long window)
        {
            ExpireRing(_l, _r.NewestTimeNs, window);
            ExpireRing(_r, _l.NewestTimeNs, window);
        }

        void ExpireRing(PcaFrameRing ring, long otherNewest, long window)
        {
            if (otherNewest <= 0) return;
            for (int i = 0; i < ring.Count; i++)
            {
                var f = ring.At(i);
                if (f == null || f.pairState != PairState.Pending) continue;
                if (otherNewest - f.xrTimeNs > window) { f.pairState = PairState.Expired; ExpiredUnpaired++; }
            }
        }

        public void Clear()
        {
            foreach (var o in _history) o.ReleaseLeases();
            _history.Clear(); Latest = null;
        }
    }
}
