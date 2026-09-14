using System;
using System.Globalization;
using FinalScan.Telemetry;
using UnityEngine;

namespace FinalScan.Platform.Sensor
{
    /// <summary>Head pose located by the runtime at an XrTime (xrLocateSpace on the camera callback, §4.3). Null = runtime could not.</summary>
    public delegate bool RuntimeLocate(long xrTimeNs, out Pose headWorld);

    /// <summary>
    /// The sensor authority without Unity/OVR calls: clock gate, pose ring, per-eye leases/rings, pairing, motion gate,
    /// governor, tick pacing and telemetry. <see cref="SensorAuthority"/> drives it on device, <see cref="SensorReplayer"/>
    /// drives it from a recording with identical inputs, so both produce the same observation stream (§4.7).
    ///
    /// Frame path: <see cref="TryBeginFrame"/> (time conversion, pose, pool slot) → caller copies the image into the slot
    /// texture → <see cref="CommitFrame"/> (ring push with monotonic/stale validation, cadence, pairing, tick request).
    /// </summary>
    public sealed class SensorPipeline
    {
        public const long MinTickIntervalNs = 50_000_000;   // §3.3: ≤ 20 integration ticks per second
        public const int PoolPerEye = 8;

        public readonly XrClock Clock = new XrClock();
        public readonly PoseRing Poses = new PoseRing();
        public readonly PcaFrameRing RingL = new PcaFrameRing(CameraEye.Left), RingR = new PcaFrameRing(CameraEye.Right);
        public readonly FrameTexturePool PoolL = new FrameTexturePool(PoolPerEye), PoolR = new FrameTexturePool(PoolPerEye);
        public readonly CadenceMeter CadenceL, CadenceR;
        public readonly MotionGate Gate = new MotionGate();
        public readonly StereoPairer Pairer;
        public readonly ProfileGovernor Governor;
        public ISensorSink Sink;

        public CaptureProfile Profile => Governor.Current;
        public long FramesSeen { get; private set; }
        public long FramesBegun { get; private set; }
        public long FramesCommitted { get; private set; }
        public long FramesNoTime { get; private set; }
        public long FramesNoPose { get; private set; }
        public long FramesPoolExhausted { get; private set; }
        public long PoseFromRuntime { get; private set; }
        public long PoseFromRing { get; private set; }
        public long TicksRequested { get; private set; }
        public long TicksCoalesced { get; private set; }
        public long TickResultErrors { get; private set; }
        public long LastTickXrTimeNs { get; private set; }
        public uint PendingTickObservation { get; private set; }
        public long LastLatencyNs { get; private set; }
        /// <summary>Runtime-located vs ring-interpolated head pose residual on the last frame (mm, deg); NaN when only one is available.</summary>
        public float LastPoseResidualMm { get; private set; } = float.NaN;
        public float LastPoseResidualDeg { get; private set; } = float.NaN;
        long _nextFrameId = 1;

        public event Action<CameraFrameLease> FrameCommitted;
        public event Action<StereoObservation> ObservationCreated;
        public event Action<string> Log;
        /// <summary>Raw inputs, raised for the recorder so replay can feed identical values (§4.7).</summary>
        public event Action<double, long, double, long> ClockReference;
        public event Action<PoseSample> PoseSampled;

        /// <summary>One clock reference per host frame (see <see cref="XrClock.AddReference"/>).</summary>
        public void AddClockReference(double ovrBeforeSeconds, long utcTicks, double ovrAfterSeconds, long monoNowNs)
        {
            Clock.AddReference(ovrBeforeSeconds, utcTicks, ovrAfterSeconds, monoNowNs);
            ClockReference?.Invoke(ovrBeforeSeconds, utcTicks, ovrAfterSeconds, monoNowNs);
        }

        /// <summary>One tracking sample per XR frame into the pose ring.</summary>
        public bool PushPose(in PoseSample sample)
        {
            if (!Poses.Push(sample)) return false;
            PoseSampled?.Invoke(sample);
            return true;
        }

        public SensorPipeline(ISensorSink sink = null, CaptureProfile initial = CaptureProfile.Normal30)
        {
            Sink = sink ?? new NullSensorSink();
            Governor = new ProfileGovernor(initial);
            double period = CaptureProfiles.Spec(initial).NominalPeriodNs;
            CadenceL = new CadenceMeter(period); CadenceR = new CadenceMeter(period);
            Pairer = new StereoPairer(RingL, RingR, CadenceL, CadenceR, Poses, Gate, period);
            Pairer.ObservationCreated += OnObservation;
            Clock.GateChanged += line => Log?.Invoke(line);
        }

        public PcaFrameRing Ring(CameraEye eye) => eye == CameraEye.Left ? RingL : RingR;
        public FrameTexturePool Pool(CameraEye eye) => eye == CameraEye.Left ? PoolL : PoolR;
        public CadenceMeter Cadence(CameraEye eye) => eye == CameraEye.Left ? CadenceL : CadenceR;

        /// <summary>
        /// Converts one producer frame into a lease. <paramref name="monoFieldNs"/> = MRUK's private monotonic field (≤ 0 when absent),
        /// <paramref name="utcTicks"/> = PassthroughCameraAccess.Timestamp.Ticks, <paramref name="ovrNowSeconds"/>/<paramref name="monoNowNs"/> = clocks read
        /// in the same callback. <paramref name="locate"/> asks the runtime for the head pose at the capture time; the ring is the fallback.
        /// The returned lease owns a pool slot (texture in <c>Pool(eye).GetTexture(lease.poolSlot)</c>); the caller must copy the image
        /// and then <see cref="CommitFrame"/> or <see cref="AbortFrame"/>.
        /// </summary>
        public bool TryBeginFrame(CameraEye eye, long monoFieldNs, long utcTicks, double ovrNowSeconds, long monoNowNs,
                                  in CameraIntrinsicsData intrinsics, RuntimeLocate locate, out CameraFrameLease lease)
        {
            lease = null; FramesSeen++;
            if (!Clock.TryConvert(monoFieldNs, utcTicks, ovrNowSeconds, out long xrNs, out long uncNs, out var path)) { FramesNoTime++; return false; }
            var ring = Ring(eye); var pool = Pool(eye);
            int slot;
            while (!pool.TryAcquire(out slot)) { if (!ring.EvictOldest()) break; }
            if (slot < 0) { FramesPoolExhausted++; return false; }

            var l = new CameraFrameLease
            {
                frameId = _nextFrameId++, eye = eye, xrTimeNs = xrNs, uncertaintyNs = uncNs, gatePath = path,
                intrinsics = intrinsics, profile = Profile, poolSlot = slot, texture = pool.GetTexture(slot),
                receiveMonotonicNs = monoNowNs, receiveXrTimeNs = ovrNowSeconds > 0 ? (long)(ovrNowSeconds * 1e9) : 0,
                sourceUtcTicks = utcTicks, sourceMonoNs = monoFieldNs
            };
            l.Begin(FreeLease);

            bool ringOk = Poses.TryLocate(xrNs, PoseNode.Head, out Pose ringHead, out _);
            Pose rtHead = Pose.identity;
            bool rtOk = locate != null && locate(xrNs, out rtHead) && SensorMath.IsFinite(rtHead);
            if (rtOk && ringOk) { LastPoseResidualMm = (rtHead.position - ringHead.position).magnitude * 1000f; LastPoseResidualDeg = SensorMath.AngleDeg(rtHead.rotation, ringHead.rotation); }
            else { LastPoseResidualMm = float.NaN; LastPoseResidualDeg = float.NaN; }
            if (rtOk) { l.headPose = rtHead; l.poseFromRuntime = true; l.poseValid = true; PoseFromRuntime++; }
            else if (ringOk) { l.headPose = ringHead; l.poseValid = true; PoseFromRing++; }
            else { l.headPose = Pose.identity; l.poseValid = false; FramesNoPose++; }
            l.cameraPose = l.poseValid ? SensorMath.Mul(l.headPose, intrinsics.lensOffset) : Pose.identity;
            LastLatencyNs = l.LatencyNs;
            FramesBegun++;
            lease = l;
            return true;
        }

        void FreeLease(CameraFrameLease l) { Pool(l.eye).ReleaseSlot(l.poolSlot); l.poolSlot = -1; l.texture = null; }

        /// <summary>Pushes the lease into its ring (validation may drop it), updates cadence and pairing, paces scan ticks.</summary>
        public bool CommitFrame(CameraFrameLease lease)
        {
            if (lease == null) return false;
            var ring = Ring(lease.eye);
            long now = lease.receiveXrTimeNs;
            if (!ring.Push(lease)) return false;
            Cadence(lease.eye).Add(lease.xrTimeNs, now > 0 ? now : lease.xrTimeNs);
            FramesCommitted++;
            FrameCommitted?.Invoke(lease);
            Pairer.Update(now, Profile);
            PumpTicks(now);
            return true;
        }

        public void AbortFrame(CameraFrameLease lease) => lease?.Release();

        void OnObservation(StereoObservation o)
        {
            if (PendingTickObservation != 0) TicksCoalesced++;
            PendingTickObservation = o.observationId;
            ObservationCreated?.Invoke(o);
        }

        /// <summary>Requests at most one scan tick per <see cref="MinTickIntervalNs"/>, always for the newest coherent observation (§3.3).</summary>
        public void PumpTicks(long nowXrTimeNs)
        {
            if (PendingTickObservation == 0) return;
            if (LastTickXrTimeNs > 0 && nowXrTimeNs - LastTickXrTimeNs < MinTickIntervalNs) return;
            int rc = Sink.ScanRequestTick(PendingTickObservation);
            if (rc != 0 && Sink.Available) TickResultErrors++;
            TicksRequested++; LastTickXrTimeNs = nowXrTimeNs; PendingTickObservation = 0;
        }

        /// <summary>Governor step; returns true when the caller must reconfigure the cameras to <see cref="Profile"/>.</summary>
        public bool UpdateGovernor(double nowSeconds, long nowXrTimeNs, ScanMode mode, bool thermalWarning)
        {
            double fl = CadenceL.DeliveredFps(nowXrTimeNs), fr = CadenceR.DeliveredFps(nowXrTimeNs);
            return Governor.Update(nowSeconds, mode, fl, fr, thermalWarning);
        }

        /// <summary>Call after the cameras were reconfigured: cadence restarts, rings and pending pairs are dropped.</summary>
        public void OnProfileApplied()
        {
            CadenceL.Reset(); CadenceR.Reset();
            RingL.Clear(); RingR.Clear();
        }

        public void Reset()
        {
            Pairer.Clear(); RingL.Clear(); RingR.Clear(); Poses.Clear(); CadenceL.Reset(); CadenceR.Reset(); PendingTickObservation = 0;
        }

        /// <summary>Writes the "FS-SENSOR" telemetry object (§20 counters included).</summary>
        public JsonWriter WriteTelemetry(JsonWriter w, long nowXrTimeNs, double intervalSeconds, long pairsInInterval, long framesInIntervalL, long framesInIntervalR)
        {
            var o = Pairer.Latest;
            w.Prop("profile", Profile.ToString()).Prop("captureProfile", (int)Profile).Prop("scanMode", Governor.Mode.ToString()).Prop("profileTransitions", Governor.Transitions).Prop("profileReason", Governor.LastReason)
             .Prop("fpsL", CadenceL.DeliveredFps(nowXrTimeNs)).Prop("fpsR", CadenceR.DeliveredFps(nowXrTimeNs))
             .Prop("periodMsL", CadenceL.MedianPeriodNs / 1e6).Prop("periodMsR", CadenceR.MedianPeriodNs / 1e6)
             .Prop("framesL", CadenceL.Frames).Prop("framesR", CadenceR.Frames)
             .Prop("intervalFpsL", intervalSeconds > 0 ? framesInIntervalL / intervalSeconds : 0).Prop("intervalFpsR", intervalSeconds > 0 ? framesInIntervalR / intervalSeconds : 0)
             .Prop("pairsPerSec", intervalSeconds > 0 ? pairsInInterval / intervalSeconds : 0).Prop("pairs", Pairer.Pairs)
             .Prop("direct", Pairer.Direct).Prop("motionCompensated", Pairer.MotionCompensated).Prop("lowConfidence", Pairer.LowConfidence)
             .Prop("rejectedSkew", Pairer.RejectedBySkew).Prop("rejectedUncertainty", Pairer.RejectedByUncertainty).Prop("expiredUnpaired", Pairer.ExpiredUnpaired)
             .Prop("pairWindowMs", Pairer.LastWindowNs / 1e6).Prop("lastSkewMs", Pairer.LastSkewNs / 1e6)
             .Prop("timestampNonMonotonic", RingL.DroppedNonMonotonic + RingR.DroppedNonMonotonic).Prop("nonMonotonicL", RingL.DroppedNonMonotonic).Prop("nonMonotonicR", RingR.DroppedNonMonotonic)
             .Prop("staleDropped", RingL.DroppedStale + RingR.DroppedStale).Prop("poolExhausted", FramesPoolExhausted).Prop("noTime", FramesNoTime).Prop("noPose", FramesNoPose)
             .Prop("clockGatePath", Clock.GatePath.ToString()).Prop("clockUncertaintyNs", Clock.UncertaintyNs == long.MaxValue ? -1 : Clock.UncertaintyNs).Prop("clockClass", Clock.UncertaintyClass.ToString())
             .Prop("pathAVerified", Clock.PathAVerified).Prop("pathAJitterNs", Clock.PathAJitterNs).Prop("pathCResidualNs", Clock.PathCFit.valid ? Clock.PathCFit.residualRms * 1e9 : double.NaN)
             .Prop("motionGated", Pairer.MotionGated).Prop("motionUnknown", Gate.Unknown)
             .Prop("poseRing", Poses.Count).Prop("poseRingCoverageMs", Poses.Count > 1 ? (Poses.NewestTimeNs - Poses.OldestTimeNs) / 1e6 : 0)
             .Prop("poseFromRuntime", PoseFromRuntime).Prop("poseFromRing", PoseFromRing).Prop("poseResidualMm", LastPoseResidualMm).Prop("poseResidualDeg", LastPoseResidualDeg)
             .Prop("latencyMs", LastLatencyNs / 1e6)
             .Prop("ticks", TicksRequested).Prop("ticksCoalesced", TicksCoalesced).Prop("tickErrors", TickResultErrors).Prop("sinkAvailable", Sink.Available);
            if (o != null)
            {
                w.BeginObject("latest").Prop("id", (long)o.observationId).Prop("skewMs", o.skewNs / 1e6).Prop("skew", o.skew.ToString()).Prop("confidence", o.confidence)
                 .Prop("angularDegPerSec", o.headAngularDegPerSec).Prop("geometry", o.geometryEvidence).Prop("baselineMm", o.baselineMeters * 1000f)
                 .Prop("ageMs", (nowXrTimeNs - o.createdXrTimeNs) / 1e6).Prop("textures", o.HasTextures).EndObject();
            }
            return w;
        }
    }
}
