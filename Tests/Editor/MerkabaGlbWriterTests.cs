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
        public void GlbContainsIndexedVertexColorPbrBoundaryAndIsDeterministic()
        {
            List<MerkabaKernelSnapshot> fixture = Fixture();
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
            string json = Encoding.UTF8.GetString(first, 20, jsonLength).TrimEnd(' ');
            Assert.That(json, Does.Contain("\"POSITION\":0"));
            Assert.That(json, Does.Contain("\"NORMAL\":1"));
            Assert.That(json, Does.Contain("\"COLOR_0\":2"));
            Assert.That(json, Does.Contain("\"indices\":3"));
            Assert.That(json, Does.Contain("\"componentType\":5121,\"normalized\":true"));
            Assert.That(json, Does.Contain("\"baseColorFactor\":[1,1,1,1]"));
            Assert.That(json, Does.Contain("\"metallicFactor\":0"));
            Assert.That(json, Does.Contain("\"roughnessFactor\":0.85"));
            Assert.That(json, Does.Not.Contain("TEXCOORD_0"));
            Assert.That(json, Does.Not.Contain("image"));

            int binaryHeader = 20 + jsonLength;
            int binaryLength = checked((int)ReadUInt32(first, binaryHeader));
            Assert.That(ReadUInt32(first, binaryHeader + 4), Is.EqualTo(0x004E4942u));
            Assert.That(binaryHeader + 8 + binaryLength, Is.EqualTo(first.Length));
            Assert.That(firstResult.VertexCount, Is.EqualTo(firstResult.IndexCount));
            Assert.That(firstResult.VertexCount % 3, Is.Zero);

            int binaryStart = binaryHeader + 8;
            int indicesOffset = firstResult.VertexCount * (12 + 12 + 4);
            Assert.That(ReadUInt32(first, binaryStart + indicesOffset), Is.EqualTo(0u));
            Assert.That(ReadUInt32(first, binaryStart + indicesOffset + 4), Is.EqualTo(2u));
            Assert.That(ReadUInt32(first, binaryStart + indicesOffset + 8), Is.EqualTo(1u));
        }

        [Test]
        public void SingleKernelExportsAllFixedBoundaryPatches()
        {
            KernelState state = Occupied(new Color32(180, 100, 20, 255));
            var fixture = new List<MerkabaKernelSnapshot>
            {
                new(new int3(0), state)
            };
            byte[] bytes = Write(fixture, out MerkabaGlbResult result);
            Assert.That(bytes, Is.Not.Empty);
            Assert.That(result.PrimitivePatchCount,
                Is.EqualTo(MerkabaConstants.BoundaryPatchCount));
            Assert.That(result.VertexCount, Is.EqualTo(
                MerkabaConstants.BoundaryPatchCount * MerkabaConstants.VerticesPerPatch));
        }

        private static List<MerkabaKernelSnapshot> Fixture()
        {
            Color32 color = new(25, 100, 220, 255);
            return new List<MerkabaKernelSnapshot>
            {
                new(new int3(-1, 0, 0), Occupied(color)),
                new(new int3(0, 0, 0), Occupied(color)),
                new(new int3(31, 1, 0), Occupied(color)),
                new(new int3(32, 1, 0), Occupied(color))
            };
        }

        private static KernelState Occupied(Color32 color)
        {
            KernelState state = default;
            MerkabaIntegrator.IntegrateClassified(ref state,
                MerkabaObservationKind.Surface, 1f, color);
            return state;
        }

        private static byte[] Write(IReadOnlyList<MerkabaKernelSnapshot> fixture,
            out MerkabaGlbResult result)
        {
            using var stream = new MemoryStream();
            result = MerkabaGlbWriter.Write(stream, fixture);
            return stream.ToArray();
        }

        private static uint ReadUInt32(byte[] bytes, int offset) =>
            BitConverter.ToUInt32(bytes, offset);
    }
}
