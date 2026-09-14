using System;
using System.Collections.Generic;
using FinalScan.Telemetry;

namespace FinalScan.Residency
{
    /// <summary>
    /// Pure evaluator of the C07 spin test (contract §21.2 readout, §21.8 turn vs move). Fed one sample per frame for
    /// the test duration; <see cref="Evaluate"/> yields the FS-ACCEPT verdict:
    ///   PASS requires orientationRequests == 0 on every frame, no frame with zero visible surfels while facing
    ///   geometry, no frame longer than <see cref="MaxFrameMs"/>, and at least <see cref="MinTotalYawDeg"/> of yaw
    ///   actually covered (otherwise the run did not exercise rotation and is reported FAIL with that reason).
    /// </summary>
    public sealed class SpinAcceptance
    {
        public struct Sample
        {
            public double TimeSec;
            public float YawRateDegPerSec;
            public long RequestsThisFrame;
            public long OrientationRequests;
            public long VisibleSurfels;
            public double FrameMs;
            public bool FacingGeometry;
        }

        public const double MaxFrameMs = 20.0;
        public const double MinTotalYawDeg = 180.0;

        readonly List<Sample> _samples = new List<Sample>(4096);
        double _totalYawDeg;
        double _lastTime = double.NaN;

        public int Count => _samples.Count;
        public double TotalYawDeg => _totalYawDeg;
        public IReadOnlyList<Sample> Samples => _samples;

        public void Add(Sample s)
        {
            if (!double.IsNaN(_lastTime))
            {
                double dt = s.TimeSec - _lastTime;
                if (dt > 0 && dt < 1.0) _totalYawDeg += Math.Abs(s.YawRateDegPerSec) * dt;
            }
            _lastTime = s.TimeSec;
            _samples.Add(s);
        }

        public void Reset() { _samples.Clear(); _totalYawDeg = 0; _lastTime = double.NaN; }

        public sealed class Result
        {
            public bool Pass;
            public int Frames;
            public double DurationSec;
            public double TotalYawDeg;
            public float MaxYawRateDegPerSec;
            public long MaxOrientationRequests;
            public long TotalRequests;
            public int ZeroVisibleWhileFacing;
            public int FramesOver20Ms;
            public double MaxFrameMs;
            public double P99FrameMs;
            public long MinVisibleWhileFacing = long.MaxValue;
            public readonly List<string> Reasons = new List<string>();

            public string ToJson()
            {
                var w = new JsonWriter(512);
                w.BeginObject().Prop("test", "spin").Prop("frames", Frames).Prop("durationSec", DurationSec).Prop("totalYawDeg", TotalYawDeg)
                 .Prop("maxYawRateDegPerSec", MaxYawRateDegPerSec).Prop("maxOrientationRequests", MaxOrientationRequests).Prop("totalRequests", TotalRequests)
                 .Prop("zeroVisibleWhileFacing", ZeroVisibleWhileFacing).Prop("minVisibleWhileFacing", MinVisibleWhileFacing == long.MaxValue ? -1 : MinVisibleWhileFacing)
                 .Prop("framesOver20Ms", FramesOver20Ms).Prop("maxFrameMs", MaxFrameMs).Prop("p99FrameMs", P99FrameMs)
                 .BeginArray("reasons").Values(Reasons).EndArray()
                 .Prop("verdict", Pass ? "PASS" : "FAIL").EndObject();
                return w.ToString();
            }
        }

        public Result Evaluate()
        {
            var r = new Result { Frames = _samples.Count, TotalYawDeg = _totalYawDeg };
            if (_samples.Count == 0) { r.Reasons.Add("no samples"); return r; }
            r.DurationSec = _samples[_samples.Count - 1].TimeSec - _samples[0].TimeSec;
            var frameMs = new List<double>(_samples.Count);
            foreach (Sample s in _samples)
            {
                r.MaxYawRateDegPerSec = Math.Max(r.MaxYawRateDegPerSec, Math.Abs(s.YawRateDegPerSec));
                r.MaxOrientationRequests = Math.Max(r.MaxOrientationRequests, s.OrientationRequests);
                r.TotalRequests += Math.Max(0, s.RequestsThisFrame);
                if (s.FacingGeometry)
                {
                    if (s.VisibleSurfels <= 0) r.ZeroVisibleWhileFacing++;
                    r.MinVisibleWhileFacing = Math.Min(r.MinVisibleWhileFacing, s.VisibleSurfels);
                }
                if (s.FrameMs > MaxFrameMs) r.FramesOver20Ms++;
                r.MaxFrameMs = Math.Max(r.MaxFrameMs, s.FrameMs);
                frameMs.Add(s.FrameMs);
            }
            frameMs.Sort();
            r.P99FrameMs = frameMs[Math.Min(frameMs.Count - 1, (int)Math.Floor(0.99 * (frameMs.Count - 1)))];
            if (r.MaxOrientationRequests > 0) r.Reasons.Add("orientationResidencyRequests > 0 (§21.8)");
            if (r.ZeroVisibleWhileFacing > 0) r.Reasons.Add("zero visible surfels while facing geometry (§21.2 black tiles)");
            if (r.FramesOver20Ms > 0) r.Reasons.Add("frame > 20 ms (§21.2 72 Hz)");
            if (_totalYawDeg < MinTotalYawDeg) r.Reasons.Add("insufficient rotation (< " + MinTotalYawDeg + " deg)");
            r.Pass = r.Reasons.Count == 0;
            return r;
        }
    }
}
