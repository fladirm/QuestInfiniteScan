using System;
using System.IO;
using System.Text;
using Genesis.RoomScan.World;
using NUnit.Framework;

namespace Genesis.RoomScan.Tests
{
    public sealed class ChunkKeyframeArchiveTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "QuestInfiniteScanTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Test]
        public void ArchiveRoundTripIsDeterministicAndExact()
        {
            string source = Path.Combine(_root, "source");
            Directory.CreateDirectory(Path.Combine(source, "images"));
            File.WriteAllText(Path.Combine(source, "frames.jsonl"),
                "{\"id\":0}\n{\"id\":1}\n", Encoding.UTF8);
            File.WriteAllBytes(Path.Combine(source, "images", "000000.jpg"),
                new byte[] { 0xFF, 0xD8, 1, 2, 0xFF, 0xD9 });
            File.WriteAllBytes(Path.Combine(source, "images", "000001.jpg"),
                new byte[] { 0xFF, 0xD8, 3, 4, 0xFF, 0xD9 });

            byte[] first;
            using (var stream = new MemoryStream())
            {
                Assert.That(ChunkKeyframeArchive.TryWriteDirectory(stream, source,
                    out string writeError), Is.True, writeError);
                first = stream.ToArray();
            }
            using (var stream = new MemoryStream())
            {
                Assert.That(ChunkKeyframeArchive.TryWriteDirectory(stream, source,
                    out string secondError), Is.True, secondError);
                Assert.That(stream.ToArray(), Is.EqualTo(first));
            }

            string restored = Path.Combine(_root, "restored");
            using (var stream = new MemoryStream(first, false))
                Assert.That(ChunkKeyframeArchive.TryExtract(stream, restored,
                    out string extractError), Is.True, extractError);
            Assert.That(File.ReadAllText(Path.Combine(restored, "frames.jsonl")),
                Is.EqualTo(File.ReadAllText(Path.Combine(source, "frames.jsonl"))));
            Assert.That(File.ReadAllBytes(Path.Combine(restored, "images", "000001.jpg")),
                Is.EqualTo(File.ReadAllBytes(Path.Combine(source, "images", "000001.jpg"))));
        }

        [Test]
        public void ExtractRejectsTraversalWithoutWritingOutsideStaging()
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(0x4B534951u);
                writer.Write(ChunkKeyframeArchive.FormatVersion);
                writer.Write(1);
                byte[] name = Encoding.UTF8.GetBytes("../escape.jpg");
                writer.Write(name.Length);
                writer.Write(name);
                writer.Write(1L);
                writer.Write((byte)7);
            }
            stream.Position = 0;
            Assert.That(ChunkKeyframeArchive.TryExtract(stream,
                Path.Combine(_root, "target"), out string error), Is.False);
            Assert.That(error, Does.Contain("unsafe"));
            Assert.That(File.Exists(Path.Combine(_root, "escape.jpg")), Is.False);
        }
    }
}
