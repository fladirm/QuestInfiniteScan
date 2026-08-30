using System;
using System.IO;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaSpatialTests
    {
        private static readonly int[] Boundaries =
        {
            -257, -256, -255, -33, -32, -31, -9, -8, -7, -1, 0, 1,
            7, 8, 9, 31, 32, 33, 255, 256, 257
        };

        [Test]
        public void SignedAddress_RoundTripsEveryFrozenBoundaryCombination()
        {
            foreach (int z in Boundaries)
            foreach (int y in Boundaries)
            foreach (int x in Boundaries)
            {
                int3 global = new(x, y, z);
                MerkabaSpatial.Address address = MerkabaSpatial.Encode(global);
                Assert.That(math.all(address.Local >= 0) &&
                            math.all(address.Local <= 255), Is.True, global.ToString());
                Assert.That(address.D4, Is.InRange(0, 7));
                Assert.That(address.D3, Is.InRange(0, 7));
                Assert.That(address.D2, Is.InRange(0, 7));
                Assert.That(address.D1, Is.InRange(0, 7));
                Assert.That(address.D0, Is.InRange(0, 7));
                Assert.That(address.ChunkLocal, Is.InRange(0, 511));
                Assert.That(address.TileLocal, Is.InRange(0, 63));
                Assert.That(address.KernelLocal, Is.InRange(0, 511));
                Assert.That(address.GlobalCoord, Is.EqualTo(global));
                Assert.That(MerkabaSpatial.Decode(address.BlockCoord,
                    address.ChunkLocal, address.TileLocal, address.KernelLocal),
                    Is.EqualTo(global));
                Assert.That(MerkabaSpatial.Decode(address.BlockCoord,
                    address.LocalAddress, address.KernelLocal), Is.EqualTo(global));
            }
        }

        [TestCase(-1, -1, 255)]
        [TestCase(-256, -1, 0)]
        [TestCase(-257, -2, 255)]
        [TestCase(int.MinValue, -8388608, 0)]
        [TestCase(255, 0, 255)]
        [TestCase(256, 1, 0)]
        public void SignedFloorDivision_IsMathematical(int value, int block,
            int local)
        {
            Assert.That(MerkabaSpatial.FloorDiv(value, 256), Is.EqualTo(block));
            Assert.That(MerkabaSpatial.FloorMod(value, 256), Is.EqualTo(local));
        }

        [Test]
        public void Neighbours_CrossEveryAddressBoundaryExactly()
        {
            foreach (int boundary in new[] { -257, -256, -1, 0, 7, 8, 31, 32, 255, 256 })
            foreach (int axis in new[] { 0, 1, 2 })
            {
                int3 source = new(11, -19, 37);
                source[axis] = boundary;
                foreach (int delta in new[] { -1, 1 })
                {
                    int3 neighbour = source;
                    neighbour[axis] += delta;
                    MerkabaSpatial.Address encoded = MerkabaSpatial.Encode(neighbour);
                    Assert.That(MerkabaSpatial.Decode(encoded.BlockCoord,
                        encoded.ChunkLocal, encoded.TileLocal, encoded.KernelLocal),
                        Is.EqualTo(neighbour));
                }
            }
        }

        [TestCase(-1.5001f, -2)]
        [TestCase(-1.5f, -2)]
        [TestCase(-1.4999f, -1)]
        [TestCase(-0.5f, -1)]
        [TestCase(-0.4999f, 0)]
        [TestCase(0.4999f, 0)]
        [TestCase(0.5f, 1)]
        [TestCase(1.4999f, 1)]
        [TestCase(1.5f, 2)]
        public void NearestKernel_IsDeterministicAcrossSignedHalfSteps(
            float coordinate, int expected)
        {
            int3 result = MerkabaSpatial.NearestKernel(
                new float3(coordinate, coordinate, coordinate));
            Assert.That(result, Is.EqualTo(new int3(expected)));
        }

        [TestCase(0, 0, 0, 0x9bafd7c6u, 0xa8e88a6bu, 0x3f15482cu, 6086u, 2667u)]
        [TestCase(1, 2, 3, 0xfa9f79a6u, 0x48f2f44cu, 0x596f5ab1u, 6566u, 5196u)]
        [TestCase(-1, -1, -1, 0xa5f48f40u, 0xa4533e83u, 0x515b8a62u, 3904u, 7811u)]
        [TestCase(-257, -256, -255, 0x02c1739cu, 0x3e8e37c4u, 0x0113b35eu, 5020u, 6084u)]
        public void FrozenPcg3dAndBucketVectors_MatchBitExactly(int x, int y, int z,
            uint hx, uint hy, uint hz, uint b0, uint b1)
        {
            int3 coord = new(x, y, z);
            Assert.That(MerkabaSpatial.Pcg3d(coord), Is.EqualTo(new uint3(hx, hy, hz)));
            Assert.That(MerkabaSpatial.BucketPair(coord), Is.EqualTo(new uint2(b0, b1)));
        }

        [Test]
        public void SharedHlsl_ContainsFrozenLoopFreeUint32Mixer()
        {
            string path = Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Shaders/MerkabaSpatial.hlsl");
            string hlsl = File.ReadAllText(path);
            StringAssert.Contains("1664525u * v + 1013904223u", hlsl);
            StringAssert.Contains("magnitude >> 8u", hlsl);
            StringAssert.DoesNotContain("magnitude + 255u", hlsl);
            StringAssert.DoesNotContain("value /", hlsl);
            StringAssert.DoesNotContain("uint64", hlsl);
            StringAssert.DoesNotContain("% MERKABA_M8_HASH_BUCKET_COUNT", hlsl);
            string mixer = hlsl.Substring(hlsl.IndexOf("uint3 MerkabaPcg3d",
                StringComparison.Ordinal));
            mixer = mixer.Substring(0, mixer.IndexOf("uint2 MerkabaHashBucketPair",
                StringComparison.Ordinal));
            StringAssert.DoesNotContain("for (", mixer);
            StringAssert.DoesNotContain("while (", mixer);
        }

        [Test]
        public void PhysicalTileBanksContainWholeTilesWithoutCrossing()
        {
            Assert.That(MerkabaSpatial.PhysicalTileBankCount, Is.EqualTo(4));
            for (int bank = 0; bank < MerkabaSpatial.PhysicalTileBankCount; bank++)
            {
                int first = bank * MerkabaSpatial.PhysicalTileBankCapacity;
                int last = first + MerkabaSpatial.PhysicalTileBankCapacity - 1;
                Assert.That(MerkabaSpatial.PhysicalTileBank(first), Is.EqualTo(bank));
                Assert.That(MerkabaSpatial.PhysicalTileBank(last), Is.EqualTo(bank));
                Assert.That(MerkabaSpatial.BankStateIndex(first, 0), Is.Zero);
                Assert.That(MerkabaSpatial.BankStateIndex(last,
                    MerkabaSpatial.KernelsPerTile - 1),
                    Is.EqualTo(MerkabaSpatial.PhysicalTileBankCapacity *
                               MerkabaSpatial.KernelsPerTile - 1));
            }
        }

        [Test]
        public void HashClaimContract_DefersSameKeyAndNeverAliasesCollisions()
        {
            var table = new ClaimTable();
            int3 key = new(-257, -256, -255);
            Assert.That(table.TryClaim(key, out int claimed),
                Is.EqualTo(ClaimResult.Claimed));
            Assert.That(table.TryClaim(key, out _),
                Is.EqualTo(ClaimResult.Deferred));
            table.Publish(claimed, key, 17u);
            Assert.That(table.TryClaim(key, out int ready),
                Is.EqualTo(ClaimResult.Ready));
            Assert.That(ready, Is.EqualTo(17));

            int3 first = default;
            int3 second = default;
            var byBucket = new System.Collections.Generic.Dictionary<uint, int3>();
            for (int value = 0; value < 20000; value++)
            {
                int3 candidate = new(value, value * -3, value * 7);
                uint bucket = MerkabaSpatial.BucketPair(candidate).x;
                if (byBucket.TryGetValue(bucket, out first) &&
                    !math.all(first == candidate))
                {
                    second = candidate;
                    break;
                }
                byBucket[bucket] = candidate;
            }
            Assert.That(math.any(first != second), Is.True);
            Assert.That(table.TryClaim(first, out int firstSlot),
                Is.EqualTo(ClaimResult.Claimed));
            table.Publish(firstSlot, first, 23u);
            Assert.That(table.TryClaim(second, out int secondSlot),
                Is.EqualTo(ClaimResult.Claimed));
            table.Publish(secondSlot, second, 29u);
            Assert.That(table.TryClaim(first, out ready),
                Is.EqualTo(ClaimResult.Ready));
            Assert.That(ready, Is.EqualTo(23));
            Assert.That(table.TryClaim(second, out ready),
                Is.EqualTo(ClaimResult.Ready));
            Assert.That(ready, Is.EqualTo(29));

            var full = new ClaimTable();
            full.FillCandidateSlots(key);
            Assert.That(full.TryClaim(key, out _), Is.EqualTo(ClaimResult.Full));
        }

        [Test]
        public void EightLaneStereoQuery_HasNoFalseNegativeUnderRotatedGrid()
        {
            var random = new System.Random(0x4d38);
            int intersectingChildren = 0;
            for (int iteration = 0; iteration < 256; iteration++)
            {
                int span = 1 << random.Next(4, 9);
                int3 parentMin = new(random.Next(-512, 513),
                    random.Next(-512, 513), random.Next(-512, 513));
                Matrix4x4 gridToWorld = Matrix4x4.TRS(
                    RandomVector(random, -4f, 4f),
                    Quaternion.Euler(RandomVector(random, -180f, 180f)),
                    RandomVector(random, 0.65f, 1.6f));
                Vector3 centerLocal = ((Vector3)(float3)parentMin +
                    Vector3.one * ((span - 1) * 0.5f)) *
                    MerkabaConstants.LatticeStep;
                Vector3 centerWorld = gridToWorld.MultiplyPoint3x4(centerLocal);
                Quaternion viewRotation = Quaternion.Euler(
                    RandomVector(random, -180f, 180f));
                Vector3 eyeAxis = viewRotation * Vector3.right * 0.032f;
                Plane[] left = Frustum(centerWorld - eyeAxis, viewRotation);
                Plane[] right = Frustum(centerWorld + eyeAxis, viewRotation);

                uint computed = PlaneChildMask(parentMin, span, left,
                    gridToWorld) | PlaneChildMask(parentMin, span, right,
                    gridToWorld);
                uint brute = 0u;
                for (uint child = 0u; child < 8u; child++)
                {
                    int childSpan = span / 2;
                    int3 childMin = parentMin + ChildOffset(child, childSpan);
                    if (BruteIntersects(childMin, childSpan, left, gridToWorld) ||
                        BruteIntersects(childMin, childSpan, right, gridToWorld))
                        brute |= 1u << (int)child;
                }
                intersectingChildren += math.countbits(brute);
                Assert.That(computed & brute, Is.EqualTo(brute),
                    $"iteration={iteration} span={span} min={parentMin}");
            }
            Assert.That(intersectingChildren, Is.GreaterThan(0));
        }

        private static Plane[] Frustum(Vector3 position, Quaternion rotation)
        {
            Matrix4x4 view = Matrix4x4.TRS(position, rotation,
                Vector3.one).inverse;
            Matrix4x4 projection = Matrix4x4.Perspective(86f, 1.1f,
                0.05f, 20f);
            return GeometryUtility.CalculateFrustumPlanes(projection * view);
        }

        private static uint PlaneChildMask(int3 parentMin, int parentSpan,
            Plane[] planes, Matrix4x4 gridToWorld)
        {
            uint mask = 255u;
            foreach (Plane plane in planes)
            {
                int childSpan = parentSpan / 2;
                Vector3 centerLocal = ((Vector3)(float3)parentMin +
                    Vector3.one * ((parentSpan - 1) * 0.5f)) *
                    MerkabaConstants.LatticeStep;
                float offset = parentSpan * 0.25f *
                    MerkabaConstants.LatticeStep;
                float extent = (childSpan * MerkabaConstants.LatticeStep +
                    MerkabaConstants.HalfSupport) * 0.5f;
                Vector3 center = gridToWorld.MultiplyPoint3x4(centerLocal);
                Vector3 x = gridToWorld.MultiplyVector(
                    new Vector3(offset, 0f, 0f));
                Vector3 y = gridToWorld.MultiplyVector(
                    new Vector3(0f, offset, 0f));
                Vector3 z = gridToWorld.MultiplyVector(
                    new Vector3(0f, 0f, offset));
                float radius = Mathf.Abs(Vector3.Dot(plane.normal,
                                   gridToWorld.MultiplyVector(
                                       new Vector3(extent, 0f, 0f)))) +
                               Mathf.Abs(Vector3.Dot(plane.normal,
                                   gridToWorld.MultiplyVector(
                                       new Vector3(0f, extent, 0f)))) +
                               Mathf.Abs(Vector3.Dot(plane.normal,
                                   gridToWorld.MultiplyVector(
                                       new Vector3(0f, 0f, extent))));
                uint planeMask = 0u;
                for (uint child = 0u; child < 8u; child++)
                {
                    float score = plane.GetDistanceToPoint(center) +
                        Vector3.Dot(plane.normal, (child & 1u) != 0u ? x : -x) +
                        Vector3.Dot(plane.normal, (child & 2u) != 0u ? y : -y) +
                        Vector3.Dot(plane.normal, (child & 4u) != 0u ? z : -z);
                    if (score + radius >= 0f) planeMask |= 1u << (int)child;
                }
                mask &= planeMask;
            }
            return mask;
        }

        private static bool BruteIntersects(int3 globalMin, int span,
            Plane[] planes, Matrix4x4 gridToWorld)
        {
            Vector3 localMin = (Vector3)(float3)globalMin *
                MerkabaConstants.LatticeStep -
                Vector3.one * MerkabaConstants.HalfSupport;
            Vector3 localMax = (Vector3)(float3)(globalMin + span - 1) *
                MerkabaConstants.LatticeStep +
                Vector3.one * MerkabaConstants.HalfSupport;
            foreach (Plane plane in planes)
            {
                float maximum = float.NegativeInfinity;
                for (uint corner = 0u; corner < 8u; corner++)
                {
                    Vector3 local = new(
                        (corner & 1u) != 0u ? localMax.x : localMin.x,
                        (corner & 2u) != 0u ? localMax.y : localMin.y,
                        (corner & 4u) != 0u ? localMax.z : localMin.z);
                    maximum = Mathf.Max(maximum, plane.GetDistanceToPoint(
                        gridToWorld.MultiplyPoint3x4(local)));
                }
                if (maximum < -1e-5f) return false;
            }
            return true;
        }

        private static int3 ChildOffset(uint child, int span) => new(
            (child & 1u) != 0u ? span : 0,
            (child & 2u) != 0u ? span : 0,
            (child & 4u) != 0u ? span : 0);

        private static Vector3 RandomVector(System.Random random,
            float minimum, float maximum) => new(
            Mathf.Lerp(minimum, maximum, (float)random.NextDouble()),
            Mathf.Lerp(minimum, maximum, (float)random.NextDouble()),
            Mathf.Lerp(minimum, maximum, (float)random.NextDouble()));

        private enum ClaimResult : byte
        {
            Ready,
            Claimed,
            Deferred,
            Full
        }

        private sealed class ClaimTable
        {
            private struct Entry
            {
                internal int3 Key;
                internal uint Reference;
            }

            private readonly Entry[] _entries = new Entry[
                MerkabaSpatial.HashEntryCount];

            internal ClaimResult TryClaim(int3 key, out int value)
            {
                uint2 buckets = MerkabaSpatial.BucketSearchOrder(key);
                int firstEmpty = -1;
                bool claimed = false;
                for (int order = 0; order < 2; order++)
                {
                    uint bucket = order == 0 ? buckets.x : buckets.y;
                    for (int slot = 0;
                         slot < MerkabaSpatial.HashSlotsPerBucket; slot++)
                    {
                        int index = (int)bucket *
                            MerkabaSpatial.HashSlotsPerBucket + slot;
                        Entry entry = _entries[index];
                        if (entry.Reference != MerkabaSpatial.EmptyRef &&
                            entry.Reference != MerkabaSpatial.ClaimedNewRef &&
                            math.all(entry.Key == key))
                        {
                            value = (int)entry.Reference - 1;
                            return ClaimResult.Ready;
                        }
                        if (entry.Reference == MerkabaSpatial.ClaimedNewRef)
                            claimed = true;
                        else if (entry.Reference == MerkabaSpatial.EmptyRef &&
                                 firstEmpty < 0)
                            firstEmpty = index;
                    }
                }
                if (claimed)
                {
                    value = -1;
                    return ClaimResult.Deferred;
                }
                if (firstEmpty < 0)
                {
                    value = -1;
                    return ClaimResult.Full;
                }
                _entries[firstEmpty].Reference = MerkabaSpatial.ClaimedNewRef;
                value = firstEmpty;
                return ClaimResult.Claimed;
            }

            internal void Publish(int slot, int3 key, uint blockIndex)
            {
                _entries[slot].Key = key;
                _entries[slot].Reference = blockIndex + 1u;
            }

            internal void FillCandidateSlots(int3 key)
            {
                uint2 buckets = MerkabaSpatial.BucketSearchOrder(key);
                for (int order = 0; order < 2; order++)
                for (int slot = 0;
                     slot < MerkabaSpatial.HashSlotsPerBucket; slot++)
                {
                    int index = (int)(order == 0 ? buckets.x : buckets.y) *
                        MerkabaSpatial.HashSlotsPerBucket + slot;
                    _entries[index].Key = key + new int3(slot + 1,
                        order + 1, 17);
                    _entries[index].Reference = (uint)index + 1u;
                }
            }
        }
    }
}
