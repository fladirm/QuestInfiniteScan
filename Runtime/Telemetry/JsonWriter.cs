using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FinalScan.Telemetry
{
    /// <summary>
    /// Minimal forward-only JSON builder (StringBuilder based, no Newtonsoft dependency).
    /// Usage: <c>new JsonWriter().BeginObject().Prop("a", 1).BeginArray("xs").Value(1.5).EndArray().EndObject().ToString()</c>.
    /// Non-finite doubles are written as <c>null</c>. Strings are escaped per RFC 8259.
    /// </summary>
    public sealed class JsonWriter
    {
        readonly StringBuilder _sb;
        // One entry per open container; true while the container has no element yet.
        readonly Stack<bool> _empty = new Stack<bool>();

        public JsonWriter(int capacity = 256) { _sb = new StringBuilder(capacity); }

        public int Depth => _empty.Count;
        public int Length => _sb.Length;

        void Sep()
        {
            if (_empty.Count == 0) return;
            if (_empty.Peek()) { _empty.Pop(); _empty.Push(false); }
            else _sb.Append(',');
        }

        void Key(string key)
        {
            Sep();
            WriteString(_sb, key ?? "null");
            _sb.Append(':');
        }

        public JsonWriter BeginObject() { Sep(); _sb.Append('{'); _empty.Push(true); return this; }
        public JsonWriter BeginObject(string key) { Key(key); _sb.Append('{'); _empty.Push(true); return this; }
        public JsonWriter EndObject() { if (_empty.Count > 0) _empty.Pop(); _sb.Append('}'); return this; }
        public JsonWriter BeginArray() { Sep(); _sb.Append('['); _empty.Push(true); return this; }
        public JsonWriter BeginArray(string key) { Key(key); _sb.Append('['); _empty.Push(true); return this; }
        public JsonWriter EndArray() { if (_empty.Count > 0) _empty.Pop(); _sb.Append(']'); return this; }

        public JsonWriter Prop(string key, string value) { Key(key); WriteStringOrNull(value); return this; }
        public JsonWriter Prop(string key, double value) { Key(key); WriteNumber(value); return this; }
        public JsonWriter Prop(string key, float value) { Key(key); WriteNumber(value); return this; }
        public JsonWriter Prop(string key, long value) { Key(key); _sb.Append(value.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Prop(string key, int value) { Key(key); _sb.Append(value.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Prop(string key, bool value) { Key(key); _sb.Append(value ? "true" : "false"); return this; }
        public JsonWriter PropNull(string key) { Key(key); _sb.Append("null"); return this; }
        /// <summary>Writes pre-serialized JSON verbatim (null/empty becomes <c>null</c>).</summary>
        public JsonWriter PropRaw(string key, string json) { Key(key); _sb.Append(string.IsNullOrWhiteSpace(json) ? "null" : json.Trim()); return this; }

        public JsonWriter Value(string value) { Sep(); WriteStringOrNull(value); return this; }
        public JsonWriter Value(double value) { Sep(); WriteNumber(value); return this; }
        public JsonWriter Value(long value) { Sep(); _sb.Append(value.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Value(int value) { Sep(); _sb.Append(value.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Value(bool value) { Sb(value ? "true" : "false"); return this; }
        public JsonWriter ValueNull() { Sb("null"); return this; }
        public JsonWriter ValueRaw(string json) { Sb(string.IsNullOrWhiteSpace(json) ? "null" : json.Trim()); return this; }

        public JsonWriter Values(IEnumerable<double> values) { if (values != null) foreach (var v in values) Value(v); return this; }
        public JsonWriter Values(IEnumerable<long> values) { if (values != null) foreach (var v in values) Value(v); return this; }
        public JsonWriter Values(IEnumerable<int> values) { if (values != null) foreach (var v in values) Value(v); return this; }
        public JsonWriter Values(IEnumerable<string> values) { if (values != null) foreach (var v in values) Value(v); return this; }

        void Sb(string s) { Sep(); _sb.Append(s); }

        void WriteStringOrNull(string value)
        {
            if (value == null) _sb.Append("null"); else WriteString(_sb, value);
        }

        void WriteNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) { _sb.Append("null"); return; }
            _sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>Closes every open container and returns the document.</summary>
        public override string ToString()
        {
            var sb = new StringBuilder(_sb.Length + _empty.Count);
            sb.Append(_sb);
            // We do not know container kinds on the stack; track them via a parallel scan of the text is
            // overkill. Callers are expected to balance; auto-close as objects for safety.
            for (int i = 0; i < _empty.Count; i++) sb.Append('}');
            return sb.ToString();
        }

        public static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            WriteString(sb, s);
            return sb.ToString();
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
