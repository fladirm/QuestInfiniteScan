using System;

namespace FinalScan.Platform.Sensor
{
    /// <summary>
    /// Measures delivered cadence of one stream from its capture timestamps: frames per second over a sliding wall window
    /// (for the profile governor) and the median inter-frame period (for the pairing window). Pure C#.
    /// </summary>
    public sealed class CadenceMeter
    {
        public const int PeriodWindow = 32;
        readonly long[] _deltas = new long[PeriodWindow]; int _dHead, _dCount;
        readonly long[] _arrivals; int _aHead, _aCount;
        readonly long _fpsWindowNs;
        long _lastTs, _firstArrival;
        readonly double _nominalPeriodNs;
        readonly long[] _tmp = new long[PeriodWindow];

        public long Frames { get; private set; }

        public CadenceMeter(double nominalPeriodNs, double fpsWindowSeconds = 2.0, int maxRate = 90)
        {
            _nominalPeriodNs = nominalPeriodNs > 0 ? nominalPeriodNs : 1e9 / 30.0;
            _fpsWindowNs = (long)(fpsWindowSeconds * 1e9);
            _arrivals = new long[Math.Max(8, (int)(fpsWindowSeconds * maxRate) + 4)];
        }

        /// <summary>Records one delivered frame: <paramref name="captureNs"/> for the period estimate, <paramref name="arrivalNs"/> for fps.</summary>
        public void Add(long captureNs, long arrivalNs)
        {
            Frames++;
            if (_lastTs > 0 && captureNs > _lastTs)
            {
                _deltas[_dHead] = captureNs - _lastTs; _dHead = (_dHead + 1) % PeriodWindow; if (_dCount < PeriodWindow) _dCount++;
            }
            if (captureNs > _lastTs) _lastTs = captureNs;
            if (_firstArrival <= 0) _firstArrival = arrivalNs;
            _arrivals[_aHead] = arrivalNs; _aHead = (_aHead + 1) % _arrivals.Length; if (_aCount < _arrivals.Length) _aCount++;
        }

        public void Reset() { _dHead = _dCount = 0; _aHead = _aCount = 0; _lastTs = 0; _firstArrival = 0; Frames = 0; }

        /// <summary>Median capture-to-capture period in ns; nominal until 4 deltas exist.</summary>
        public double MedianPeriodNs
        {
            get
            {
                if (_dCount < 4) return _nominalPeriodNs;
                Array.Copy(_deltas, _tmp, _dCount); Array.Sort(_tmp, 0, _dCount);
                return (_dCount & 1) == 1 ? _tmp[_dCount / 2] : 0.5 * (_tmp[_dCount / 2 - 1] + _tmp[_dCount / 2]);
            }
        }

        public bool PeriodMeasured => _dCount >= 4;

        /// <summary>
        /// Delivered frames per second = frames that arrived inside the wall window ending at <paramref name="nowNs"/>
        /// divided by the window length. Returns 0 ("not measured") until a full window has elapsed since the first frame,
        /// so warm-up never reads as under-delivery; a stalled stream decays to 0 within one window.
        /// </summary>
        public double DeliveredFps(long nowNs)
        {
            if (_aCount == 0 || _firstArrival <= 0 || nowNs - _firstArrival < _fpsWindowNs) return 0;
            long from = nowNs - _fpsWindowNs;
            int n = 0;
            for (int i = 0; i < _aCount; i++)
            {
                long a = _arrivals[(_aHead - _aCount + i + _arrivals.Length) % _arrivals.Length];
                if (a >= from && a <= nowNs) n++;
            }
            return n / (_fpsWindowNs * 1e-9);
        }
    }
}
