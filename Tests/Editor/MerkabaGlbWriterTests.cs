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
            Assert.That(json, Does.Not.Contain("\"NORMAL\""));
            Assert.That(json, Does.Not.Contain("\"TANGENT\""));
            Assert.That(json, Does.Contain("\"COLOR_0\":1"));
            Assert.That(json, Does.Contain("\"indices\":2"));
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
            int indicesOffset = firstResult.VertexCount * (12 + 4);
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
            Assert.That(result.VertexCount, Is.EqualTo(7));
            Assert.That(result.PrimitiveCount, Is.EqualTo(6));
            Assert.That(result.IndexCount, Is.EqualTo(18));
            int jsonLength = checked((int)ReadUInt32(bytes, 12));
            int binaryStart = 20 + jsonLength + 8;
            for (int vertex = 0; vertex < result.VertexCount; vertex++)
            {
                int offset = binaryStart + vertex * 12;
                float3 expected = flower.Positions[vertex];
                Assert.That(BitConverter.ToSingle(bytes, offset), Is.EqualTo(-expected.x));
                Assert.That(BitConverter.ToSingle(bytes, offset + 4), Is.EqualTo(expected.y));
                Assert.That(BitConverter.ToSingle(bytes, offset + 8), Is.EqualTo(expected.z));
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
            Assert.That(result.VertexCount, Is.EqualTo(14));
            // The same symbolic knots retain bit-identical positions under
            // opposite winding. Normals belong to incident L2 frames, not knots.
            CollectionAssert.AreEqual(bytes.Skip(binaryStart).Take(7 * 12),
                bytes.Skip(binaryStart + 7 * 12).Take(7 * 12));
            int indices = binaryStart + result.VertexCount * 16;
            for (int wedge = 0; wedge < 6; wedge++)
            {
                int forward = indices + wedge * 12;
                int backward = indices + (6 + wedge) * 12;
                Assert.That(ReadUInt32(bytes, backward) - 7u, Is.EqualTo(ReadUInt32(bytes, forward)));
                Assert.That(ReadUInt32(bytes, backward + 4) - 7u, Is.EqualTo(ReadUInt32(bytes, forward + 8)));
                Assert.That(ReadUInt32(bytes, backward + 8) - 7u, Is.EqualTo(ReadUInt32(bytes, forward + 4)));
            }
            string json = Encoding.UTF8.GetString(bytes, 20, binaryStart - 28);
            Assert.That(json, Does.Not.Contain("\"NORMAL\""));
            Assert.That(json, Does.Not.Contain("\"TANGENT\""));
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
                             "positions.bin", "colors.bin",
                             "indices.bin", "ranges.bin"
                         })
                    Assert.That(spooled.Any(value =>
                        Path.GetFileName(value) == name), Is.True, name);
                Assert.That(spooled.Any(value => Path.GetFileName(value) == "normals.bin"), Is.False);
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

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void VOnlyAtlasPreservesNestedMicroNormalsWithMirroredChartAndWinding(bool mirror, bool reverse)
        {
            MerkabaFlowerPresentation flower = MetricFixture(5, reverse);
            var carrier = flower.Carriers[0];
            for (int site = 0; site < 7; site++)
            {
                float2 c = MerkabaSphereFlowerAuthority.L2CarrierChartSite(site);
                flower.Positions[site] = new float3(1f, -2f, 3f) + 0.0125f *
                    (c.x * new float3(0.6f, 1f, 0.3f) + c.y * (mirror ? -1f : 1f) * new float3(0.1f, -0.2f, 0.9f));
            }
            float3[] positions = flower.Positions.ToArray();
            Assert.That(carrier.RgbSplitBits, Is.EqualTo(uint2.zero));
            Assert.That(MerkabaFlowerMaterialBake.CellSize(carrier), Is.EqualTo(64),
                "V-only detail must allocate the union's L5 cell even with uniform captured RGB.");
            string directory = Path.Combine(Path.GetTempPath(), "m8-v-atlas-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using var atlas = new MerkabaFlowerMaterialBake.Atlas(directory);
                var cell = atlas.Append(flower, carrier, default);
                using var pixels = File.OpenRead(Path.Combine(directory, "atlas-v-00000000.rgba"));
                using var reader = new BinaryReader(pixels);
                int observed = 0, relief = 0;
                for (int y = 0; y < cell.Size; y++)
                for (int x = 0; x < cell.Size; x++)
                {
                    float2 chart = new float2(
                        (float)(2.0 * (x + 0.5 - 2) / (cell.Size - 4) - 1),
                        (float)(2.0 * (y + 0.5 - 2) / (cell.Size - 4) - 1));
                    if (!MerkabaSphereFlowerAuthority.TryL2CarrierChartWedge(chart, out int wedge,
                            out float3 bc, out _, out _)) continue;
                    flower.WedgeFrame(carrier, wedge, out float3 t1, out float3 t2, out float3 n,
                        out float3 du, out float3 dv);
                    if (reverse) { n = -n; t2 = -t2; dv = -dv; }
                    var sample = MerkabaSphereFlowerAuthority.EvaluateSkinDrawSignal(carrier.SkinHeader,
                        carrier.SkinSamples, wedge, bc, du, dv, out _, out _, out float2 gradient);
                    Assert.That(sample.Amplitude3, Is.Not.Zero);
                    Assert.That(sample.Amplitude4, Is.Not.Zero);
                    Assert.That(sample.Amplitude5, Is.Not.Zero);
                    float3 expected = MerkabaSphereFlowerAuthority.SkinMicroNormal(t1, t2, n, gradient);
                    float2 a = MerkabaSphereFlowerAuthority.L2CarrierChartSite(1 + wedge);
                    float2 b = MerkabaSphereFlowerAuthority.L2CarrierChartSite(1 + (wedge + 1) % 6);
                    float3 e1 = flower.Position(carrier, 1 + wedge) - flower.Position(carrier, 0);
                    float3 e2 = flower.Position(carrier, 1 + (wedge + 1) % 6) - flower.Position(carrier, 0);
                    float3 u = math.normalize(e1 * b.y - e2 * a.y), v = math.cross(n, u);
                    if (math.dot(v, e2 * a.x - e1 * b.x) < 0f) v = -v;
                    pixels.Position = 4L * ((cell.Y + y) * MerkabaFlowerMaterialBake.Resolution + cell.X + x);
                    float3 encoded = new float3(reader.ReadByte(), reader.ReadByte(), reader.ReadByte()) * (2f / 255f) - 1f;
                    Assert.That(reader.ReadByte(), Is.EqualTo(255));
                    float3 decoded = encoded.x * u + encoded.y * v + encoded.z * n;
                    // Three signed UNORM8 components: each error <= 1/255.
                    // A second such bound conservatively encloses FP32 frame arithmetic.
                    Assert.That(math.distance(decoded, expected), Is.LessThanOrEqualTo(2f * math.sqrt(3f) / 255f));
                    observed++;
                    if (math.lengthsq(gradient) > 0f) relief++;
                }
                Assert.That(observed, Is.GreaterThan(0));
                Assert.That(relief, Is.GreaterThan(0));
                CollectionAssert.AreEqual(positions, flower.Positions, "Atlas generation cannot displace L2.");
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AtlasResumeValidatesBothRgbAndVReceipts(bool corruptV)
        {
            string directory = Path.Combine(Path.GetTempPath(), "m8-atlas-receipt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var flower = MetricFixture(3, false);
                MerkabaFlowerMaterialBake.ResumeState receipt;
                using (var atlas = new MerkabaFlowerMaterialBake.Atlas(directory))
                {
                    Assert.That(atlas.Append(flower, flower.Carriers[0], default).Size, Is.EqualTo(16));
                    receipt = atlas.Checkpoint(default);
                }
                using (var resumed = new MerkabaFlowerMaterialBake.Atlas(directory, receipt))
                    Assert.That(resumed.PageCount, Is.EqualTo(1));
                string path = Path.Combine(directory, corruptV ? "atlas-v-00000000.rgba" : "atlas-rgb-00000000.rgba");
                using (var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
                {
                    int first = file.ReadByte(); file.Position = 0; file.WriteByte((byte)(first ^ 1));
                }
                Assert.Throws<InvalidDataException>(() =>
                {
                    using var rejected = new MerkabaFlowerMaterialBake.Atlas(directory, receipt);
                });
            }
            finally { Directory.Delete(directory, true); }
        }

        private static MerkabaFlowerPresentation MetricFixture(int depth, bool reverse)
        {
            var flower = MerkabaFlowerWriterFixture.Create();
            var carrier = flower.Carriers[0];
            carrier.Symbol = MerkabaFlowerSymbolRecord.CreateCarrier(0, 0, 1, reverse, false, 63u,
                0u, 0, 0u, reverse ? 63u : 0u);
            uint low = depth == 3 ? 1u : depth == 4 ? 255u : uint.MaxValue;
            uint high = depth == 5 ? 0x01ffffffu : 0u;
            var run = new MerkabaFlowerSkinMetricRun { FlowerKey = MerkabaFlowerL2Key.Create(0, 0, false, 0).Value,
                GroupBase = 0u, ParentEpoch = 1u, SplitBitsLo = low, SplitBitsHi = high };
            var value = new MerkabaFlowerVInterval { Lower = 512, Upper = 512 };
            var group = new MerkabaFlowerVGroup { Child0 = value, Child1 = value, Child2 = value,
                Child3 = value, Child4 = value, Child5 = value, Child6 = value };
            carrier.SkinSamples = MerkabaSphereFlowerAuthority.CompileSkinDrawSamples(
                MerkabaFlowerWriterFixture.Captured, 1u, null, null, run,
                Enumerable.Repeat(group, run.GroupCount).ToArray(), null, out carrier.SkinHeader);
            return flower;
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
