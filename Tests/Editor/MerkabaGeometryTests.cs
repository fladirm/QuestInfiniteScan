using Genesis.RoomScan;
using NUnit.Framework;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaGeometryTests
    {
        [Test]
        public void FloorAddressing_IsCorrectAtNegativeChunkBoundaries()
        {
            var cases = new[]
            {
                (global: -65, chunk: -3, local: 31),
                (global: -64, chunk: -2, local: 0),
                (global: -33, chunk: -2, local: 31),
                (global: -32, chunk: -1, local: 0),
                (global: -1, chunk: -1, local: 31),
                (global: 0, chunk: 0, local: 0),
                (global: 31, chunk: 0, local: 31),
                (global: 32, chunk: 1, local: 0)
            };
            foreach (var item in cases)
            {
                Assert.That(MerkabaConstants.FloorDiv(item.global, 32), Is.EqualTo(item.chunk));
                Assert.That(MerkabaConstants.FloorMod(item.global, 32), Is.EqualTo(item.local));
            }
        }
    }
}
