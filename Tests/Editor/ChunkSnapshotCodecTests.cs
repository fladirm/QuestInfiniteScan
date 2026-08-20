using System;
using System.IO;
using Genesis.RoomScan.World;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class ChunkSnapshotCodecTests
    {
        [Test]
        public void VolumeRoundTripPreservesFrameAndExactPayload()
        {
            var source = new ChunkVolumeSnapshot
            {
                VoxelCount = new Vector3Int(2, 3, 4),
                VoxelSize = 0.05f,
                IntegrationCount = 37,
                WorldFromVolume = new RigidPoseData(new Vector3(3f, 1f, -2f),
                    Quaternion.Euler(0f, 45f, 0f)),
                TsdfBytes = Pattern(2 * 3 * 4 * 2, 17),
                ColorBytes = Pattern(2 * 3 * 4 * 4, 29)
            };

            using var stream = new MemoryStream();
            Assert.That(ChunkSnapshotCodec.TryWriteVolume(stream, source,
                out string writeError), Is.True, writeError);
            stream.Position = 0;
            Assert.That(ChunkSnapshotCodec.TryReadVolume(stream,
                out ChunkVolumeSnapshot restored, out string readError), Is.True, readError);

            Assert.That(restored.VoxelCount, Is.EqualTo(source.VoxelCount));
            Assert.That(restored.VoxelSize, Is.EqualTo(source.VoxelSize));
            Assert.That(restored.IntegrationCount, Is.EqualTo(source.IntegrationCount));
            Assert.That(Vector3.Distance(restored.WorldFromVolume.position,
                source.WorldFromVolume.position), Is.LessThan(0.00001f));
            Assert.That(Quaternion.Angle(restored.WorldFromVolume.rotation,
                source.WorldFromVolume.rotation), Is.LessThan(0.001f));
            Assert.That(restored.TsdfBytes, Is.EqualTo(source.TsdfBytes));
            Assert.That(restored.ColorBytes, Is.EqualTo(source.ColorBytes));
        }

        [Test]
        public void LiveMeshRoundTripRejectsOutOfRangeIndexAndTrailingBytes()
        {
            var source = new ChunkLiveMeshSnapshot
            {
                VertexCount = 3,
                IndexCount = 3,
                LocalBounds = new BoundsData(Vector3.zero, Vector3.one),
                VertexBytes = Pattern(3 * ChunkLiveMeshSnapshot.VertexStride, 41),
                IndexBytes = Indices(0, 1, 2)
            };

            byte[] encoded;
            using (var stream = new MemoryStream())
            {
                Assert.That(ChunkSnapshotCodec.TryWriteLiveMesh(stream, source,
                    out string error), Is.True, error);
                encoded = stream.ToArray();
                stream.Position = 0;
                Assert.That(ChunkSnapshotCodec.TryReadLiveMesh(stream,
                    out ChunkLiveMeshSnapshot restored, out string readError), Is.True,
                    readError);
                Assert.That(restored.VertexBytes, Is.EqualTo(source.VertexBytes));
                Assert.That(restored.IndexBytes, Is.EqualTo(source.IndexBytes));
            }

            source.IndexBytes = Indices(0, 1, 3);
            using (var invalidIndex = new MemoryStream())
            {
                Assert.That(ChunkSnapshotCodec.TryWriteLiveMesh(invalidIndex, source,
                    out string indexError), Is.False);
                Assert.That(indexError, Does.Contain("outside"));
            }

            Array.Resize(ref encoded, encoded.Length + 1);
            using var trailing = new MemoryStream(encoded, false);
            Assert.That(ChunkSnapshotCodec.TryReadLiveMesh(trailing, out _,
                out string trailingError), Is.False);
            Assert.That(trailingError, Does.Contain("length"));
        }

        [Test]
        public void VolumeReaderRejectsDeclaredDimensionsBeforeLargeAllocation()
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(0x56534951u);
                writer.Write(ChunkSnapshotCodec.VolumeFormatVersion);
                writer.Write(int.MaxValue);
                writer.Write(256);
                writer.Write(256);
            }
            stream.Position = 0;
            Assert.That(ChunkSnapshotCodec.TryReadVolume(stream, out _, out string error),
                Is.False);
            Assert.That(error, Does.Contain("rejected"));
        }

        private static byte[] Pattern(int length, int seed)
        {
            var bytes = new byte[length];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)((i * 31 + seed) & 0xFF);
            return bytes;
        }

        private static byte[] Indices(params int[] indices)
        {
            var bytes = new byte[indices.Length * sizeof(int)];
            Buffer.BlockCopy(indices, 0, bytes, 0, bytes.Length);
            return bytes;
        }
    }
}
