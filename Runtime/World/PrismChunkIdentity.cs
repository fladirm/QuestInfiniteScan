using System.Globalization;

namespace Genesis.RoomScan.World
{
    /// <summary>Stable non-zero 32-bit identity stored in GPU ContactFilm headers.</summary>
    public static class PrismChunkIdentity
    {
        public static uint ToNumericId(string chunkId)
        {
            if (string.IsNullOrEmpty(chunkId)) return 1u;
            int separator = chunkId.LastIndexOf('-');
            if (separator >= 0 && separator + 1 < chunkId.Length &&
                uint.TryParse(chunkId.Substring(separator + 1),
                    NumberStyles.None, CultureInfo.InvariantCulture,
                    out uint ordinal) && ordinal < uint.MaxValue)
                return ordinal + 1u;

            uint hash = 2166136261u;
            foreach (char character in chunkId)
            {
                hash ^= character;
                hash *= 16777619u;
            }
            return hash == 0u ? 1u : hash;
        }
    }
}
