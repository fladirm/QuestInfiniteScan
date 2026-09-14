using System;
using System.Collections.Generic;

namespace FinalScan.Telemetry
{
    /// <summary>Bounded sample store with min/max/mean/percentiles and fixed-width histogram. Pure C#.</summary>
    public sealed class SampleStats
    {
        readonly List<double> _v;
        readonly int _cap;
        double _sum, _min = double.PositiveInfinity, _max = double.NegativeInfinity;
        int _total;

        public SampleStats(int capacity = 20000) { _cap = Math.Max(16, capacity); _v = new List<double>(Math.Min(_cap, 1024)); }

        public int Count => _total;
        public int Stored => _v.Count;
        public double Min => _total == 0 ? double.NaN : _min;
        public double Max => _total == 0 ? double.NaN : _max;
        public double Mean => _total == 0 ? double.NaN : _sum / _total;
        public double Last { get; private set; } = double.NaN;

        public void Add(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return;
            _total++; _sum += x; Last = x;
            if (x < _min) _min = x;
            if (x > _max) _max = x;
            if (_v.Count < _cap) _v.Add(x);
        }

        public void Reset() { _v.Clear(); _sum = 0; _min = double.PositiveInfinity; _max = double.NegativeInfinity; _total = 0; Last = double.NaN; }

        /// <summary>Nearest-rank percentile over stored samples (p in [0,100]).</summary>
        public double Percentile(double p)
        {
            if (_v.Count == 0) return double.NaN;
            var s = new List<double>(_v); s.Sort();
            double rank = Math.Ceiling(p / 100.0 * s.Count);
            int idx = (int)Math.Min(Math.Max(rank, 1), s.Count) - 1;
            return s[idx];
        }

        public double StdDev()
        {
            if (_v.Count < 2) return double.NaN;
            double m = 0; foreach (var x in _v) m += x; m /= _v.Count;
            double a = 0; foreach (var x in _v) a += (x - m) * (x - m);
            return Math.Sqrt(a / (_v.Count - 1));
        }

        /// <summary>Fixed-width histogram; last returned element is the overflow count.</summary>
        public int[] Histogram(double binWidth, int bins, double origin = 0.0)
        {
            var h = new int[bins + 1];
            if (binWidth <= 0 || bins <= 0) return h;
            foreach (var x in _v)
            {
                double r = (x - origin) / binWidth;
                if (r < 0) { h[0]++; continue; }
                int i = (int)r;
                if (i >= bins) h[bins]++; else h[i]++;
            }
            return h;
        }

        public IReadOnlyList<double> Samples => _v;

        public JsonWriter WriteTo(JsonWriter w, string key)
        {
            w.BeginObject(key)
             .Prop("n", _total)
             .Prop("min", Min).Prop("max", Max).Prop("mean", Mean)
             .Prop("p50", Percentile(50)).Prop("p90", Percentile(90)).Prop("p99", Percentile(99))
             .Prop("std", StdDev())
             .EndObject();
            return w;
        }

        public JsonWriter WriteHistogram(JsonWriter w, string key, double binWidth, int bins, double origin = 0.0)
        {
            var h = Histogram(binWidth, bins, origin);
            w.BeginObject(key).Prop("binWidth", binWidth).Prop("origin", origin).Prop("bins", bins)
             .BeginArray("counts");
            for (int i = 0; i < bins; i++) w.Value(h[i]);
            w.EndArray().Prop("overflow", h[bins]).EndObject();
            return w;
        }
    }
}
