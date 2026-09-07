using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaGlbWriterTests
    {
        [Test]
        public void GlbContainsIndexedCapturedRadianceFlowerAndIsDeterministic()
        {
            MerkabaFlowerPresentation fixture = Fixture();
            byte[] first = Write(fixture, out MerkabaGlbResult firstResult);
            byte[] second = Write(fixture, out MerkabaGlbResult secondResult);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(secondResult.ByteLength, Is.EqualTo(firstResult.ByteLength));

            Assert.That(ReadUInt32(first, 0), Is.EqualTo(0x46546C67u));
            Assert.That(ReadUInt32(first, 4), Is.EqualTo(2u));
            Assert.That(ReadUInt32(first, 8), Is.EqualTo((uint)first.Length));
            int jsonLength = checked((int)ReadUInt32(first, 12));
            Assert.That(jsonLength % 4, Is.Zero);
            Assert.That(ReadUInt32(first, 16), Is.EqualTo(0x4E4F534Au));
            string json = Encoding.UTF8.GetString(first, 20, jsonLength)
                .TrimEnd(' ');
            Assert.That(json, Does.Contain("\"POSITION\":0"));
            Assert.That(json, Does.Contain("\"NORMAL\":1"));
            Assert.That(json, Does.Contain("\"COLOR_0\":2"));
            Assert.That(json, Does.Contain("\"indices\":3"));
            Assert.That(json, Does.Contain(
                "\"componentType\":5121,\"normalized\":true"));
            Assert.That(json, Does.Contain("\"doubleSided\":true"));
            Assert.That(json, Does.Not.Contain("TEXCOORD_0"));
            Assert.That(json, Does.Not.Contain("image"));

            int binaryHeader = 20 + jsonLength;
            int binaryLength = checked((int)ReadUInt32(first, binaryHeader));
            Assert.That(ReadUInt32(first, binaryHeader + 4),
                Is.EqualTo(0x004E4942u));
            Assert.That(binaryHeader + 8 + binaryLength, Is.EqualTo(first.Length));
            Assert.That(firstResult.IndexCount,
                Is.EqualTo(firstResult.PrimitiveCount * 3));
            Assert.That(firstResult.VertexCount,
                Is.LessThanOrEqualTo(firstResult.IndexCount));

            int binaryStart = binaryHeader + 8;
            int indicesOffset = firstResult.VertexCount * (12 + 12 + 4);
            Assert.That(ReadUInt32(first, binaryStart + indicesOffset),
                Is.EqualTo(0u));
            Assert.That(ReadUInt32(first, binaryStart + indicesOffset + 4),
                Is.EqualTo(2u));
            Assert.That(ReadUInt32(first, binaryStart + indicesOffset + 8),
                Is.EqualTo(1u));
        }

        [Test]
        public void OneExplicitL2CarrierMaterializesOnlyItsSixWedges()
        {
            MerkabaFlowerPresentation flower = MerkabaFlowerWriterFixture.Create(new int3(0));
            byte[] bytes = Write(flower, out MerkabaGlbResult result);
            Assert.That(bytes, Is.Not.Empty);
            Assert.That(flower.Positions.Count, Is.EqualTo(7));
            Assert.That(result.PrimitiveCount, Is.EqualTo(6));
            Assert.That(result.IndexCount, Is.EqualTo(18));
            int jsonLength = checked((int)ReadUInt32(bytes, 12));
            int binaryStart = 20 + jsonLength + 8;
            int normalsOffset = result.VertexCount * 12;
            for (int vertex = 0; vertex < result.VertexCount; vertex++)
            {
                int offset = binaryStart + normalsOffset + vertex * 12;
                Assert.That(Math.Abs(BitConverter.ToSingle(bytes, offset)), Is.EqualTo(1f));
                Assert.That(BitConverter.ToSingle(bytes, offset + 4), Is.Zero);
                Assert.That(BitConverter.ToSingle(bytes, offset + 8), Is.Zero);
            }
        }

        [Test]
        public void ParallelSymbolicCarriersRemainDistinctGeometry()
        {
            MerkabaFlowerPresentation flower = MerkabaFlowerWriterFixture.Create(new int3(0), new int3(1, 0, 0));
            Write(flower, out MerkabaGlbResult result);
            Assert.That(flower.Positions.Count, Is.EqualTo(14));
            Assert.That(result.PrimitiveCount, Is.EqualTo(12));
            Assert.That(result.IndexCount, Is.EqualTo(36));
            Assert.That(flower.Carriers[0].PositionIndices, Is.Not.EqualTo(flower.Carriers[1].PositionIndices));
        }

        [Test]
        public void SharedPositionIndicesRemainIndependentOfCarrierNormals()
        {
            MerkabaFlowerPresentation flower = MerkabaFlowerWriterFixture.Create();
            var first = flower.Carriers[0];
            var reverse = new MerkabaFlowerPresentation.Carrier
            {
                Owner = first.Owner,
                Symbol = MerkabaFlowerSymbolRecord.CreateCarrier(0, 0, 1, true, true, 63u, 0u, 0, 0u, 63u),
                SkinHeader = first.SkinHeader, SkinSamples = first.SkinSamples
            };
            Array.Copy(first.PositionIndices, reverse.PositionIndices, 7);
            flower.Carriers.Add(reverse); flower.TriangleCount += 6;
            byte[] bytes = Write(flower, out MerkabaGlbResult result);
            Assert.That(flower.Knots.Count, Is.EqualTo(7));
            Assert.That(flower.Positions.Count, Is.EqualTo(7));
            Assert.That(reverse.PositionIndices, Is.EqualTo(first.PositionIndices));
            Assert.That(result.PrimitiveCount, Is.EqualTo(12));
            int binaryStart = 28 + checked((int)ReadUInt32(bytes, 12));
            int normals = binaryStart + result.VertexCount * 12;
            // Both carriers emit the same shared-site packet, so the reverse
            // carrier begins exactly half way through the vertex stream. The
            // stride follows the emitted packet; it is not a fixed 18.
            Assert.That(result.VertexCount % 2, Is.Zero);
            int perCarrier = result.VertexCount / 2;
            Assert.That(BitConverter.ToSingle(bytes, normals),
                Is.EqualTo(-BitConverter.ToSingle(bytes, normals + perCarrier * 12)));
        }

        [Test]
        public void WriterHasNoLegacy24MVertexLimitAndStillEnforcesGlb4GiB()
        {
            Assert.That(MerkabaGlbWriter.CheckedIndexCountForPrimitiveCount(
                8_000_001), Is.EqualTo(24_000_003));
            Assert.Throws<InvalidDataException>(() =>
                MerkabaGlbWriter.CheckedIndexCountForPrimitiveCount(50_000_000));
            string source = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Merkaba/" +
                "MerkabaGlbWriter.cs"));
            Assert.That(source, Does.Not.Contain("MerkabaCanonicalGeometry"));
            Assert.That(source, Does.Not.Contain("LegacyKernels"));
            Assert.That(source, Does.Not.Contain("MaximumVertices"));
            Assert.That(source, Does.Not.Contain("List<ExportPrimitive>"));
        }

        [Test]
        public void BoundedWriterSpoolsSpatialBatchesAndPublishesOneValidGlb()
        {
            string spool = Path.Combine(Path.GetTempPath(),
                "merkaba-glb-spool-" + Guid.NewGuid().ToString("N"));
            MerkabaGlbResult result;
            using var output = new MemoryStream();
            using (var session = new MerkabaGlbWriter.StreamingSession(spool))
            {
                session.Append(MerkabaFlowerWriterFixture.Create(new int3(0)));
                session.Append(MerkabaFlowerWriterFixture.Create(new int3(64, 0, 0)));
                // The spool holds one bounded file per emitted stream. Its
                // arity follows the writer's accessor set - asserting the
                // invariant, not a frozen count that grows with UV/atlas
                // streams. No batch may materialize the model in memory.
                string[] spooled = Directory.GetFiles(spool);
                Assert.That(spooled, Is.Not.Empty);
                foreach (string name in new[]
                         {
                             "positions.bin", "normals.bin", "colors.bin",
                             "indices.bin", "ranges.bin"
                         })
                    Assert.That(spooled.Any(value =>
                        Path.GetFileName(value) == name), Is.True, name);
                Assert.That(spooled.Any(value =>
                    Path.GetFileName(value) == "document.json"), Is.False,
                    "The document is only written by Complete.");
                result = session.Complete(output);
            }

            byte[] bytes = output.ToArray();
            Assert.That(Directory.Exists(spool), Is.False);
            Assert.That(ReadUInt32(bytes, 0), Is.EqualTo(0x46546C67u));
            Assert.That(ReadUInt32(bytes, 8), Is.EqualTo((uint)bytes.Length));
            Assert.That(result.PrimitiveCount, Is.EqualTo(12));
            // Two appended carriers emit the shared seven-site packet each,
            // so the vertex stream is 14 while the index stream stays 36.
            // Sharing is the point: vertices must stay below indices.
            Assert.That(result.VertexCount, Is.EqualTo(14));
            Assert.That(result.IndexCount, Is.EqualTo(36));
            Assert.That(result.VertexCount, Is.LessThan(result.IndexCount));
        }

        private static MerkabaFlowerPresentation Fixture() => MerkabaFlowerWriterFixture.Create(
            new int3(-1, 0, 0), new int3(0), new int3(31, 1, 0), new int3(32, 1, 0));

        private static byte[] Write(MerkabaFlowerPresentation fixture,
            out MerkabaGlbResult result)
        {
            using var stream = new MemoryStream();
            result = MerkabaGlbWriter.Write(stream, fixture, float3.zero);
            return stream.ToArray();
        }

        private static uint ReadUInt32(byte[] bytes, int offset) =>
            BitConverter.ToUInt32(bytes, offset);
    }
}
