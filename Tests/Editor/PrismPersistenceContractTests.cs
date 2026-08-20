using System.IO;
using Genesis.RoomScan.Prism;
using Genesis.RoomScan.World;
using NUnit.Framework;

namespace Genesis.RoomScan.Tests
{
    public sealed class PrismPersistenceContractTests
    {
        [Test]
        public void CanonicalSnapshotRoundTripsExactPosteriorBytes()
        {
            var source = new PrismCanonicalChunkSnapshot
            {
                FilmCount = 2,
                BoundaryCount = 1,
                FilmGeneration = 7,
                BoundaryGeneration = 9,
                CalibrationEpoch = 123456789,
                FilmHeaders = Pattern(2 * ContactFilmHeaderGpu.Stride, 3),
                FilmInformation = Pattern(2 * 9 * 16, 5),
                BoundaryHeaders = Pattern(ContactBoundaryHeaderGpu.Stride, 7),
                BoundaryInformation = Pattern(
                    ContactBoundaryPool.InformationRecordsPerBoundary * 16, 11)
            };
            using var stream = new MemoryStream();

            Assert.That(PrismCanonicalChunkCodec.TryWrite(stream, source,
                out string writeError), Is.True, writeError);
            stream.Position = 0;
            Assert.That(PrismCanonicalChunkCodec.TryRead(stream, out var restored,
                out string readError), Is.True, readError);
            Assert.That(restored.FilmCount, Is.EqualTo(source.FilmCount));
            Assert.That(restored.BoundaryCount, Is.EqualTo(source.BoundaryCount));
            Assert.That(restored.CalibrationEpoch, Is.EqualTo(source.CalibrationEpoch));
            Assert.That(restored.FilmHeaders, Is.EqualTo(source.FilmHeaders));
            Assert.That(restored.FilmInformation, Is.EqualTo(source.FilmInformation));
            Assert.That(restored.BoundaryHeaders, Is.EqualTo(source.BoundaryHeaders));
            Assert.That(restored.BoundaryInformation,
                Is.EqualTo(source.BoundaryInformation));
        }

        [Test]
        public void CanonicalSnapshotRejectsTrailingOrMismatchedPayload()
        {
            var source = new PrismCanonicalChunkSnapshot
            {
                FilmCount = 1,
                BoundaryCount = 0,
                FilmGeneration = 1,
                BoundaryGeneration = 1,
                FilmHeaders = new byte[ContactFilmHeaderGpu.Stride],
                FilmInformation = new byte[9 * 16]
            };
            using var stream = new MemoryStream();
            Assert.That(PrismCanonicalChunkCodec.TryWrite(stream, source, out _), Is.True);
            stream.WriteByte(0x5a);
            stream.Position = 0;

            Assert.That(PrismCanonicalChunkCodec.TryRead(stream, out _,
                out string error), Is.False);
            Assert.That(error, Does.Contain("trailing"));
        }

        private static byte[] Pattern(int count, int multiplier)
        {
            var bytes = new byte[count];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)((i * multiplier + 17) & 0xff);
            return bytes;
        }
    }
}
