using System;
using System.Collections.Generic;
using System.IO;
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
        public void GlbContainsIndexedMeasuredColorPbrMembraneAndIsDeterministic()
        {
            MerkabaExportMembraneResult fixture = Fixture();
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
        public void SingleMeasuredKernelExportsOneSupportPatchNotMerkabaSoup()
        {
            var evidence = new Dictionary<int3, KernelState>
            {
                [new int3(0)] = Measured(new float3(1, 0, 0), 0.007f,
                    new Color32(180, 100, 20, 255))
            };
            MerkabaExportMembraneResult membrane = Membrane(evidence);
            byte[] bytes = Write(membrane, out MerkabaGlbResult result);

            Assert.That(bytes, Is.Not.Empty);
            Assert.That(result.PrimitiveCount, Is.EqualTo(2));
            Assert.That(result.VertexCount, Is.EqualTo(4));
            Assert.That(result.IndexCount, Is.EqualTo(6));

            int jsonLength = checked((int)ReadUInt32(bytes, 12));
            int binaryStart = 20 + jsonLength + 8;
            int normalsOffset = result.VertexCount * 12;
            for (int vertex = 0; vertex < result.VertexCount; vertex++)
            {
                int offset = binaryStart + normalsOffset + vertex * 12;
                Assert.That(Math.Abs(BitConverter.ToSingle(bytes, offset)),
                    Is.GreaterThan(0.99f));
                Assert.That(Math.Abs(BitConverter.ToSingle(bytes, offset + 4)),
                    Is.LessThan(0.01f));
                Assert.That(Math.Abs(BitConverter.ToSingle(bytes, offset + 8)),
                    Is.LessThan(0.01f));
            }
        }

        [Test]
        public void ParallelMeasuredLayersRemainDistinctIndexedGeometry()
        {
            KernelState state = Measured(new float3(1, 0, 0), 0f,
                new Color32(90, 150, 210, 255));
            var evidence = new Dictionary<int3, KernelState>
            {
                [new int3(0, 0, 0)] = state,
                [new int3(1, 0, 0)] = state
            };
            MerkabaExportMembraneResult membrane = Membrane(evidence);

            byte[] bytes = Write(membrane, out MerkabaGlbResult result);
            Assert.That(bytes, Is.Not.Empty);
            Assert.That(membrane.MeasuredPatchCount, Is.EqualTo(2));
            Assert.That(result.PrimitiveCount, Is.EqualTo(4));
            Assert.That(result.VertexCount, Is.EqualTo(8));
            Assert.That(result.IndexCount, Is.EqualTo(12));
        }

        [Test]
        public void ExactSharedMeasuredSeamReusesIndexedVertices()
        {
            KernelState state = Measured(new float3(1, 0, 0), 0f,
                new Color32(90, 150, 210, 255));
            int3 firstCoord = new(0, 0, 0);
            int3 secondCoord = new(0, 2, 0);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(firstCoord, state,
                out MerkabaOverlapShell.Patch first), Is.True);
            Assert.That(MerkabaOverlapShell.TryBuildPatch(secondCoord, state,
                out MerkabaOverlapShell.Patch second), Is.True);
            var patches = new List<MerkabaExportMembranePatch>
            {
                ExportPatch(first), ExportPatch(second)
            };
            var membrane = new MerkabaExportMembraneResult(patches,
                new List<MerkabaKernelSnapshot>(),
                new[] { firstCoord, secondCoord }, 2, 2, 2, 0, 0, 0, 0, 0);

            Write(membrane, out MerkabaGlbResult result);
            Assert.That(result.PrimitiveCount, Is.EqualTo(4));
            Assert.That(result.VertexCount, Is.EqualTo(6));
            Assert.That(result.VertexCount,
                Is.LessThan(membrane.Patches.Count * 4));
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
            Assert.That(source, Does.Contain("membrane.LegacyKernels"));
            Assert.That(source, Does.Not.Contain("MaximumVertices"));
            Assert.That(source, Does.Not.Contain("List<ExportPrimitive>"));
        }

        private static MerkabaExportMembraneResult Fixture()
        {
            Color32 color = new(25, 100, 220, 255);
            var evidence = new Dictionary<int3, KernelState>
            {
                [new int3(-1, 0, 0)] = Measured(new float3(1, 0, 0), 0f, color),
                [new int3(0, 0, 0)] = Measured(new float3(1, 0, 0), 0f, color),
                [new int3(31, 1, 0)] = Measured(new float3(0, 1, 0), 0f, color),
                [new int3(32, 1, 0)] = Measured(new float3(0, 1, 0), 0f, color)
            };
            return Membrane(evidence);
        }

        private static MerkabaExportMembraneResult Membrane(
            IReadOnlyDictionary<int3, KernelState> evidence) =>
            MerkabaExportMembrane.Build(MerkabaExportShell.Build(evidence));

        private static KernelState Measured(float3 normal, float offset,
            Color32 color)
        {
            KernelState state = default;
            state.SetOccupiedForFixture(true, color);
            state.Flags = KernelState.SetSurfacePlane(state.Flags, normal, offset);
            return state;
        }

        private static byte[] Write(MerkabaExportMembraneResult fixture,
            out MerkabaGlbResult result)
        {
            using var stream = new MemoryStream();
            result = MerkabaGlbWriter.Write(stream, fixture);
            return stream.ToArray();
        }

        private static MerkabaExportMembranePatch ExportPatch(
            MerkabaOverlapShell.Patch patch) => new(patch.Main, patch.Normal,
            patch.Corner00.GridPosition, patch.Corner10.GridPosition,
            patch.Corner11.GridPosition, patch.Corner01.GridPosition,
            patch.Corner00.PackedColor, false);

        private static uint ReadUInt32(byte[] bytes, int offset) =>
            BitConverter.ToUInt32(bytes, offset);
    }
}
