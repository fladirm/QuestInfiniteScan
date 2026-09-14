namespace FinalScan.Render
{
    /// <summary>
    /// Environment Depth arrives through AROcclusionManager.frameReceived at the Unity frame rate (72 Hz callback)
    /// while the sensor produces 25 Hz (XrTime delta 40 ms, C01 evidence); repeats carry the same timestamp.
    /// This keeps only frames with a strictly newer timestamp and counts what was dropped, so the native prior is fed
    /// once per real depth frame.
    /// </summary>
    public sealed class DepthFrameDedupe
    {
        public long LastTimestampNs { get; private set; } = long.MinValue;
        public int Accepted { get; private set; }
        public int Repeats { get; private set; }
        public int NonMonotonic { get; private set; }
        public int NoTimestamp { get; private set; }

        /// <summary>Returns true when the frame with this timestamp should be forwarded.</summary>
        public bool Accept(bool hasTimestamp, long timestampNs)
        {
            if (!hasTimestamp) { NoTimestamp++; return false; }
            if (timestampNs == LastTimestampNs) { Repeats++; return false; }
            if (timestampNs < LastTimestampNs) { NonMonotonic++; return false; }
            LastTimestampNs = timestampNs;
            Accepted++;
            return true;
        }

        public void Reset() { LastTimestampNs = long.MinValue; Accepted = Repeats = NonMonotonic = NoTimestamp = 0; }
    }
}
