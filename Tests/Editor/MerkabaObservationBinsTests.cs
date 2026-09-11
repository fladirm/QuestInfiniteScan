using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaObservationBinsTests
    {
        private const int Slots = MerkabaSpatial.PhysicalTileCapacity;
        private const int Generation = 7;
        private const string Root = "Packages/com.genesis.roomscan/";

        private static ComputeShader Shader(string path) =>
            AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + path);

        private static void Bind(ComputeShader shader, int kernel,
            ComputeBuffer bins, ComputeBuffer counters, int capacity, int token = Generation)
        {
            shader.SetInt("_M8ObservationToken", token);
            shader.SetInt("_M8ObservationHotSlotCount", Slots);
            shader.SetInt("_M8ObservationRecordCapacity", capacity);
            shader.SetBuffer(kernel, "_M8ObservationTileBins", bins);
            shader.SetBuffer(kernel, "_M8Counters", counters);
        }

        [Test, Timeout(30000)]
        public void CountReserveEmit_UsesOnlyStampedSpansAndPreservesEveryRecord()
        {
            const int count = 2048;
            var input = Enumerable.Range(0, count).Select(i => new uint4(
                (uint)(((i % 5) * 8191 << 9) | (i & 511)), (uint)i,
                0x150u, 0u)).ToArray();
            var initial = new uint4[Slots];
            initial[1] = new uint4(Generation - 1, 123, 456, 789);
            using var bins = new ComputeBuffer(Slots, 16);
            using var counters = new ComputeBuffer(MerkabaGrid.CounterCount, 4);
            using var touched = new ComputeBuffer(Slots, 4);
            using var args = new ComputeBuffer(MerkabaObservationBinsGpu.DispatchArgumentWords, 4);
            using var sources = new ComputeBuffer(count, 16);
            using var records = new ComputeBuffer(count, 16);
            bins.SetData(initial);
            counters.SetData(new uint[MerkabaGrid.CounterCount]);
            sources.SetData(input);
            ComputeShader probe = Shader("Tests/Editor/MerkabaObservationBinsProbe.compute");
            int countKernel = probe.FindKernel("CountBinsProbe");
            Bind(probe, countKernel, bins, counters, count);
            probe.SetBuffer(countKernel, "_M8TouchedTileQueue", touched);
            probe.SetInt("_ProbeRecordCount", count);
            probe.SetBuffer(countKernel, "_ProbeRecords", sources);
            probe.Dispatch(countKernel, count / 64, 1, 1);
            Reserve(bins, counters, touched, args, count);
            int emitKernel = probe.FindKernel("EmitBinsProbe");
            Bind(probe, emitKernel, bins, counters, count);
            probe.SetBuffer(emitKernel, "_ProbeRecords", sources);
            probe.SetBuffer(emitKernel, "_M8ObservationRecords", records);
            probe.Dispatch(emitKernel, count / 64, 1, 1);
            var actual = new uint4[count];
            records.GetData(actual);
            CollectionAssert.AreEqual(input, actual.OrderBy(r => r.y).ToArray());
            var directory = new uint4[Slots];
            bins.GetData(directory);
            Assert.That(directory[1], Is.EqualTo(initial[1]));
            uint offset = 0;
            for (int tile = 0; tile < 5; tile++)
            {
                uint4 bin = directory[tile * 8191];
                Assert.That(bin.x, Is.EqualTo(Generation));
                Assert.That(bin.z, Is.EqualTo(offset));
                Assert.That(bin.w, Is.EqualTo(bin.y));
                offset += bin.y;
            }
            Assert.That(offset, Is.EqualTo(count));
            var commands = new uint[3];
            args.GetData(commands);
            CollectionAssert.AreEqual(new uint[] { 5, 1, 1 }, commands);
            var status = new uint[MerkabaGrid.CounterCount];
            counters.GetData(status);
            Assert.That(status[MerkabaGrid.CounterObservationFailure], Is.Zero);
        }

        [Test, Timeout(30000)]
        public void Reservation_IsBoundedAndCannotWrapOrPartiallyPublish()
        {
            using var bins = new ComputeBuffer(Slots, 16);
            using var counters = new ComputeBuffer(MerkabaGrid.CounterCount, 4);
            using var touched = new ComputeBuffer(Slots, 4);
            using var args = new ComputeBuffer(MerkabaObservationBinsGpu.DispatchArgumentWords, 4);
            var initial = new uint4[Slots];
            foreach (uint perSlot in new[] { 0u, 1u, 64u, 65u, uint.MaxValue })
            {
                Array.Fill(initial, new uint4(Generation, perSlot, 123, 456));
                bins.SetData(initial);
                counters.SetData(new uint[MerkabaGrid.CounterCount]);
                Reserve(bins, counters, touched, args, MerkabaObservationRecord.Capacity);
                bool valid = (ulong)perSlot * Slots <= MerkabaObservationRecord.Capacity;
                var commands = new uint[3];
                args.GetData(commands);
                Assert.That(commands[0], Is.EqualTo(valid && perSlot != 0u ? Slots : 0));
                var status = new uint[MerkabaGrid.CounterCount];
                counters.GetData(status);
                Assert.That(status[MerkabaGrid.CounterObservationFailure] == 0u,
                    Is.EqualTo(valid));
                bins.GetData(initial);
                for (int slot = 0; slot < Slots; slot++)
                {
                    Assert.That(initial[slot].z,
                        Is.EqualTo(valid && perSlot != 0u ? perSlot * (uint)slot : 123u));
                    Assert.That(initial[slot].w,
                        Is.EqualTo(valid && perSlot != 0u ? 0u : 456u));
                }
            }
        }

        private static void Reserve(ComputeBuffer bins, ComputeBuffer counters,
            ComputeBuffer touched, ComputeBuffer args, int capacity, int token = Generation)
        {
            ComputeShader shader = Shader("Runtime/Shaders/MerkabaObservationBins.compute");
            int kernel = shader.FindKernel("ReserveObservationBins");
            Bind(shader, kernel, bins, counters, capacity, token);
            shader.SetBuffer(kernel, "_M8TouchedTileQueue", touched);
            shader.SetBuffer(kernel, "_M8ObservationDispatchArgs", args);
            shader.Dispatch(kernel, 1, 1, 1);
        }

        [Test, Timeout(30000)]
        public void OwnerRouting_UsesExactEightOwnersAcrossSignedHierarchyBoundaries()
        {
            int[] boundaries = { -257, -256, -33, -32, -9, -8, -1, 0,
                7, 8, 31, 32, 255, 256, 257 };
            int3[] points = boundaries.SelectMany(x => new[] {
                new int3(x), new int3(x, 2, 3), new int3(2, x, 3),
                new int3(2, 3, x) }).ToArray();
            using var world = new OwnerWorld(points);
            world.DispatchOwners("CountOwnersProbe");
            Reserve(world.Bins, world.Counters, world.Touched, world.Args, points.Length * 8, world.Token);
            world.DispatchOwners("EmitOwnersProbe");
            CollectionAssert.AreEqual(world.Expected.OrderBy(x => x.y).ThenBy(x => x.x),
                Read<uint4>(world.Records).OrderBy(x => x.y).ThenBy(x => x.x));
            world.AssertEndpointSupport(true);
            Assert.That(Read<uint>(world.Counters)[MerkabaGrid.CounterObservationFailure], Is.Zero);
            world.ResetBins();
            Assert.That(Read<uint4>(world.Bins).All(x => math.all(x == 0u)), Is.True);
        }

        [TestCase(MerkabaSpatial.EmptyRef)]
        [TestCase(MerkabaSpatial.ColdOnSsdRef)]
        [TestCase(MerkabaSpatial.LoadingRef)]
        [TestCase(MerkabaSpatial.EvictingRef)]
        [Timeout(30000)]
        public void MissingOwner_IsLocalAndInstalledOwnersJoinOnlyTheNextSnapshot(uint missing)
        {
            // The two x tiles share one chunk. Every lane requests the same
            // missing tile; its HOT neighbour completes in snapshot N.
            int3[] points = Enumerable.Repeat(new int3(7, 0, 0), 64).ToArray();
            using var world = new OwnerWorld(points);
            uint[] refs = Read<uint>(world.TileRefs);
            int missingIndex = Array.FindIndex(refs, x => x == 2u);
            Assert.That(missingIndex, Is.GreaterThanOrEqualTo(0));
            refs[missingIndex] = missing;
            world.TileRefs.SetData(refs);
            world.DispatchOwners("CountOwnersProbe");
            Reserve(world.Bins, world.Counters, world.Touched, world.Args, points.Length * 8);
            world.DispatchOwners("EmitOwnersProbe");
            Assert.That(Read<uint>(world.Args)[0], Is.EqualTo(1u));
            uint4[] partial = Read<uint4>(world.Records).Take(256).ToArray();
            CollectionAssert.AreEqual(world.Expected.Where(r => (r.x >> 9) == 0u)
                    .OrderBy(r => r.y).ThenBy(r => r.x),
                partial.OrderBy(r => r.y).ThenBy(r => r.x));
            Assert.That(Read<uint4>(world.Bins)[0].w, Is.EqualTo(256u));
            Assert.That(Read<uint>(world.Counters)[MerkabaGrid.CounterObservationFailure], Is.Zero);
            uint[] counters = Read<uint>(world.Counters);
            Assert.That(counters[MerkabaGrid.CounterUnresolvedSurfaceTiles], Is.EqualTo(64));
            Assert.That(counters[MerkabaGrid.CounterNewTileQueueCount], Is.EqualTo(missing == MerkabaSpatial.EmptyRef ||
                missing == MerkabaSpatial.ColdOnSsdRef ? 1u : 0u));
            Assert.That(Read<uint4>(world.Bins)[0].y, Is.EqualTo(256));
            world.ServiceTileRequests();
            counters = Read<uint>(world.Counters);
            Assert.That(counters[MerkabaGrid.CounterNewTileQueueCount], Is.Zero,
                "claim arena released before HOT install");
            Assert.That(counters[MerkabaGrid.CounterPendingNewTileCount],
                Is.EqualTo(missing == MerkabaSpatial.EmptyRef ? 1u : 0u));
            Assert.That(counters[MerkabaGrid.CounterLoadRequests],
                Is.EqualTo(missing == MerkabaSpatial.ColdOnSsdRef ? 1u : 0u));
            world.ResetBins();
            world.BeginNextSnapshot();
            refs[missingIndex] = 2u; // independent storage job, visible in N+1
            world.TileRefs.SetData(refs);
            world.DispatchOwners("CountOwnersProbe");
            Reserve(world.Bins, world.Counters, world.Touched, world.Args, points.Length * 8);
            world.DispatchOwners("EmitOwnersProbe");
            CollectionAssert.AreEqual(world.Expected.OrderBy(x => x.y).ThenBy(x => x.x),
                Read<uint4>(world.Records).OrderBy(x => x.y).ThenBy(x => x.x));
            world.AssertEndpointSupport(true);
            Assert.That(Read<uint>(world.Counters)[MerkabaGrid.CounterObservationFailure], Is.Zero);
        }

        private static T[] Read<T>(ComputeBuffer buffer) where T : struct
        {
            var values = new T[buffer.count];
            buffer.GetData(values); // Oracle only, never a production readback.
            return values;
        }

        [Test, Timeout(30000)]
        public void SnapshotReset_PreservesAddressClaimsWithoutRecountPacket()
        {
            using var world = new OwnerWorld(new[] { new int3(7, 0, 0) });
            uint[] counts = Read<uint>(world.Counters);
            counts[MerkabaGrid.CounterNewBlockQueueCount] = 2u;
            counts[MerkabaGrid.CounterNewChunkQueueCount] = 3u;
            counts[MerkabaGrid.CounterNewTileQueueCount] = 4u;
            counts[MerkabaGrid.CounterPendingNewTileCount] = 5u;
            world.Counters.SetData(counts);
            world.Args.SetData(new uint[] { 1u, 1u, 1u }, 0,
                (int)MerkabaObservationBinsGpu.AllocationDispatchOffset / 4, 3);
            world.ResetBins();
            uint[] after = Read<uint>(world.Counters);
            Assert.That(after[MerkabaGrid.CounterNewBlockQueueCount], Is.EqualTo(2u));
            Assert.That(after[MerkabaGrid.CounterNewChunkQueueCount], Is.EqualTo(3u));
            Assert.That(after[MerkabaGrid.CounterNewTileQueueCount], Is.EqualTo(4u));
            Assert.That(after[MerkabaGrid.CounterPendingNewTileCount], Is.EqualTo(5u));
            CollectionAssert.AreEqual(new uint[] { 1u, 1u, 1u },
                Read<uint>(world.Args).Skip((int)MerkabaObservationBinsGpu.AllocationDispatchOffset / 4).Take(3));
            Assert.That(MerkabaObservationBinsGpu.DispatchArgumentWords, Is.EqualTo(12),
                "Only tile work, address publication and installs remain; no recount packet.");
        }

        [Test, Timeout(30000)]
        public void OwnerInstalledAfterCount_CannotWriteAnUnreservedSpan()
        {
            using var world = new OwnerWorld(new[] { new int3(7, 0, 0) });
            uint[] refs = Read<uint>(world.TileRefs);
            int missing = Array.FindIndex(refs, x => x == 2u);
            refs[missing] = MerkabaSpatial.LoadingRef;
            world.TileRefs.SetData(refs);
            world.DispatchOwners("CountOwnersProbe");
            refs[missing] = 2u;
            world.TileRefs.SetData(refs);
            Reserve(world.Bins, world.Counters, world.Touched, world.Args, 8);
            world.DispatchOwners("EmitOwnersProbe");
            Assert.That(Read<uint>(world.Args)[0], Is.EqualTo(1u));
            CollectionAssert.AreEqual(world.Expected.Where(x => (x.x >> 9) == 0u).OrderBy(x => x.x),
                Read<uint4>(world.Records).Take(4).OrderBy(x => x.x));
            Assert.That(Read<uint4>(world.Bins)[1], Is.EqualTo(new uint4(0u)));
            Assert.That(Read<uint>(world.Counters)[MerkabaGrid.CounterObservationFailure], Is.Zero);
        }

        [Test, Timeout(30000)]
        public void NativeSqrtWithIntegerCorrection_MatchesCorrectRoundingAcrossBinary32()
        {
            var bits = new List<uint> { 0u, 0x80000000u };
            for (uint exponent = 0u; exponent < 255u; ++exponent)
            foreach (uint mantissa in new[] { 0u, 1u, 2u, 0x3fffffu, 0x7ffffeu, 0x7fffffu })
                bits.Add((exponent << 23) | mantissa);
            uint random = 0x391893a7u;
            for (int i = 0; i < 8192; ++i)
            {
                random = random * 1664525u + 1013904223u;
                bits.Add(random % 0x7f800000u);
            }
            using var input = new ComputeBuffer(bits.Count, 16);
            using var output = new ComputeBuffer(bits.Count, 16);
            input.SetData(bits.Select(x => new uint4(x)).ToArray());
            ComputeShader shader = Shader("Tests/Editor/MerkabaObservationBinsProbe.compute");
            int kernel = shader.FindKernel("CanonicalSqrtProbe");
            shader.SetInt("_ProbeRecordCount", bits.Count);
            shader.SetBuffer(kernel, "_ProbeRecords", input);
            shader.SetBuffer(kernel, "_ProbeReduced", output);
            shader.Dispatch(kernel, (bits.Count + 63) / 64, 1, 1);
            uint4[] actual = Read<uint4>(output);
            for (int i = 0; i < bits.Count; ++i)
            {
                uint expected = (bits[i] & 0x7fffffffu) == 0u ? bits[i] :
                    math.asuint((float)Math.Sqrt(ExactFloat(bits[i])));
                Assert.That(actual[i].x, Is.EqualTo(expected), $"sqrt bits=0x{bits[i]:x8}");
            }
        }

        [Test, Timeout(30000)]
        public void DivisionBounds_EncloseExactDyadicProductsAcrossBinary32Domain()
        {
            const uint one = 0x3f800000u, half = 0x3f000000u, two = 0x40000000u;
            const uint three = 0x40400000u, negative = 0x80000000u, maximum = 0x7f7fffffu;
            var cases = new List<uint4>();
            void Add(uint n, uint d) => cases.Add(new uint4(n, n, d, d));
            Add(0u, one);
            Add(negative, one);
            cases.Add(new uint4(1u, 1u, half, two));
            cases.Add(new uint4(negative | 1u, negative | 1u, half, two));
            cases.Add(new uint4(negative | 1u, 1u, half, two));
            Add(0x007fffffu, 0x00800000u);
            Add(0x00800000u, 0x007fffffu);
            Add(math.asuint(0.9238796f), one);
            Add(one, three);
            Add(negative | one, three);
            Add(maximum, one);
            Add(maximum, 1u);
            Add(negative | maximum, 1u);
            Add(1u, maximum);
            Add(negative | 1u, maximum);
            cases.Add(new uint4(one, two, half, three));
            // Every finite exponent field is exercised in numerator and divisor,
            // with two deterministic mantissa/exponent pairings and both signs.
            for (uint exponent = 0; exponent < 255u; exponent++)
            for (uint pairing = 0; pairing < 2u; pairing++)
            {
                uint denominatorExponent = pairing == 0u ? 254u - exponent :
                    (73u * exponent + 19u) % 255u;
                uint n = (exponent << 23) | (((exponent * 0x1f123u + pairing * 0x391u) & 0x7fffffu) | 1u);
                uint d = (denominatorExponent << 23) | (((exponent * 0x35a17u + pairing * 0x917u) & 0x7fffffu) | 1u);
                Add(n, d);
                Add(n | negative, d);
            }
            using var input = new ComputeBuffer(cases.Count, 16);
            using var output = new ComputeBuffer(2 * cases.Count, 16);
            input.SetData(cases);
            ComputeShader shader = Shader("Tests/Editor/MerkabaObservationBinsProbe.compute");
            int kernel = shader.FindKernel("DivisionBoundsProbe");
            shader.SetInt("_ProbeRecordCount", cases.Count);
            shader.SetBuffer(kernel, "_ProbeRecords", input);
            shader.SetBuffer(kernel, "_ProbeReduced", output);
            shader.Dispatch(kernel, (cases.Count + 63) / 64, 1, 1);
            uint4[] actual = Read<uint4>(output);
            for (int i = 0; i < cases.Count; i++)
            {
                uint4 c = cases[i], status = actual[2 * i], bounds = actual[2 * i + 1];
                uint lowerDivisor = ExactFloat(c.x) < 0.0 ? c.z : c.w;
                uint upperDivisor = ExactFloat(c.y) < 0.0 ? c.w : c.z;
                bool Fits(uint n, uint d) => Math.Abs(ExactFloat(n)) <= ExactFloat(maximum) * ExactFloat(d);
                Assert.That(status.x != 0u, Is.EqualTo(Fits(c.x, c.z)), $"scalar {i}");
                Assert.That(status.y != 0u,
                    Is.EqualTo(Fits(c.x, lowerDivisor) && Fits(c.y, upperDivisor)), $"interval {i}");
                if (status.x != 0u)
                {
                    AssertDirectedBound(c.x, c.z, bounds.x, true, $"scalar lower {i}");
                    AssertDirectedBound(c.x, c.z, bounds.y, false, $"scalar upper {i}");
                    if (ExactFloat(bounds.x) != ExactFloat(bounds.y))
                        Assert.That(ExactFloat(AdjacentFloat(bounds.x, true)), Is.EqualTo(ExactFloat(bounds.y)), $"adjacent {i}");
                }
                if (status.y != 0u)
                {
                    AssertDirectedBound(c.x, lowerDivisor, bounds.z, true, $"interval lower {i}");
                    AssertDirectedBound(c.y, upperDivisor, bounds.w, false, $"interval upper {i}");
                    Assert.That(ExactFloat(bounds.z), Is.LessThanOrEqualTo(ExactFloat(bounds.w)), $"ordered {i}");
                }
            }
        }

        // Decode via integer bits, so the independent CPU oracle never subjects
        // subnormal inputs to a binary32 arithmetic/DAZ operation. Binary64 holds
        // every product of two finite binary32 values exactly (at most 48 bits).
        private static double ExactFloat(uint bits)
        {
            uint exponent = (bits >> 23) & 255u;
            Assert.That(exponent, Is.LessThan(255u), $"finite bound 0x{bits:x8}");
            uint mantissa = (bits & 0x7fffffu) | (exponent == 0u ? 0u : 0x800000u);
            double power = BitConverter.Int64BitsToDouble((long)(exponent == 0u ? 874u : exponent + 873u) << 52);
            double value = mantissa * power;
            return (bits & 0x80000000u) == 0u ? value : -value;
        }

        private static uint AdjacentFloat(uint bits, bool upward)
        {
            if ((bits & 0x7fffffffu) == 0u) return upward ? 1u : 0x80000001u;
            return ((bits & 0x80000000u) == 0u) == upward ? bits + 1u : bits - 1u;
        }

        private static void AssertDirectedBound(uint numerator, uint denominator,
            uint bound, bool lower, string label)
        {
            double n = ExactFloat(numerator), d = ExactFloat(denominator);
            double product = ExactFloat(bound) * d;
            Assert.That(lower ? product <= n : product >= n, Is.True, label);
            if (product == n) return;
            double adjacent = ExactFloat(AdjacentFloat(bound, lower)) * d;
            Assert.That(lower ? adjacent > n : adjacent < n, Is.True, label + " tight");
        }

        [Test, Timeout(30000)]
        public void RootIntervals_EncloseAnalyticRootsAndUseGeneratedSectorsOnly()
        {
            var cases = new List<float4>();
            void Add(float alo, float ahi, float blo, float bhi,
                float clo, float chi, bool plus, int line)
            {
                cases.Add(new float4(alo, ahi, blo, bhi));
                cases.Add(new float4(clo, chi, plus ? 1 : 0, line));
            }
            Add(0, 0, 0, 0, 0, 0, false, 0); // coplanar, no isolated knot
            Add(1, 1, 0, 0, 0, 0, false, 0); // no root
            Add(5, 5, 3, 3, 4, 4, false, 0); // exact dyadic tangent
            Add(5, 5, 3, 3, 4, 4, true, 0);
            Add(0.9f, 1.1f, 1, 1, 0, 0, false, 0); // crosses tangency
            Add(0, 0, 1, 1, 0, 0, false, 0);
            for (int line = 0; line < MerkabaSphereFlowerAuthority.LineClassCount; line++)
            {
                var rule = MerkabaSphereFlowerAuthority.Lines[line];
                for (int s = 0; s < rule.SectorCount; s++)
                {
                    var a = MerkabaSphereFlowerAuthority.SectorBoundaries[rule.SectorOffset + s];
                    var b = MerkabaSphereFlowerAuthority.SectorBoundaries[
                        rule.SectorOffset + (s + 1) % rule.SectorCount];
                    double start = Math.Atan2(a.Unit.y, a.Unit.x);
                    double end = Math.Atan2(b.Unit.y, b.Unit.x);
                    if (end <= start) end += 2.0 * Math.PI;
                    double angle = (start + end) * 0.5;
                    var u = new float2((float)Math.Cos(angle), (float)Math.Sin(angle));
                    // Exercise independently perturbed ABC boxes at the center
                    // of every generated sector, not a sampled adjacency table.
                    foreach (float width in new[] { 0f, 0.00001f })
                    foreach (float scale in new[] { 0.0001f, 1f, 10000f })
                        Add((-u.x - width) * scale, (-u.x + width) * scale,
                            (1 - width) * scale, (1 + width) * scale,
                            -width * scale, width * scale, u.y >= 0f, line);
                }
            }
            int count = cases.Count / 2;
            using var input = new ComputeBuffer(cases.Count, 16);
            using var results = new ComputeBuffer(count, 16);
            using var roots = new ComputeBuffer(count, 16);
            input.SetData(cases);
            ComputeShader shader = Shader("Tests/Editor/MerkabaObservationBinsProbe.compute");
            int kernel = shader.FindKernel("RootIntervalsProbe");
            using var flowerTables = MerkabaGrid.CreateFlowerTableBuffer();
            shader.SetBuffer(kernel, MerkabaGrid.FlowerTablesId, flowerTables);
            shader.SetInt("_ProbeRecordCount", count);
            shader.SetBuffer(kernel, "_ProbeCoefficients", input);
            shader.SetBuffer(kernel, "_ProbeReduced", results);
            shader.SetBuffer(kernel, "_ProbeRootIntervals", roots);
            shader.Dispatch(kernel, (count + 63) / 64, 1, 1);
            uint4[] actual = Read<uint4>(results);
            float4[] bounds = Read<float4>(roots);
            var certainSectors = new HashSet<(int, uint)>();
            for (int i = 0; i < count; i++)
            {
                float4 a = cases[2 * i], b = cases[2 * i + 1];
                var abc = new MerkabaSphereFlowerAuthority.Interval3(
                    new(a.x, a.y), new(a.z, a.w), new(b.x, b.y));
                var classification = MerkabaSphereFlowerAuthority.ClassifyRoots(abc);
                if (actual[i].x != (uint)classification)
                    DescribeRootFailure(shader, a, b);
                Assert.That(actual[i].x, Is.EqualTo((uint)classification), $"root case {i}");
                if (actual[i].x != 1u && actual[i].x != 2u)
                {
                    Assert.That(actual[i].z, Is.Zero);
                    continue;
                }
                float4 r = bounds[i];
                for (int corner = 0; corner < 8; corner++)
                {
                    double ca = (corner & 1) == 0 ? a.x : a.y;
                    double cb = (corner & 2) == 0 ? a.z : a.w;
                    double cc = (corner & 4) == 0 ? b.x : b.y;
                    double q = cb * cb + cc * cc;
                    double delta = q - ca * ca;
                    Assert.That(delta, Is.GreaterThanOrEqualTo(0));
                    double turn = (b.z != 0f ? 1 : -1) * Math.Sqrt(delta);
                    double x = (-ca * cb - turn * cc) / q;
                    double y = (-ca * cc + turn * cb) / q;
                    Assert.That(x, Is.InRange((double)r.x, (double)r.y), $"x {i}/{corner}");
                    Assert.That(y, Is.InRange((double)r.z, (double)r.w), $"y {i}/{corner}");
                }
                var root = new MerkabaSphereFlowerAuthority.Interval2(
                    new(r.x, r.y), new(r.z, r.w));
                var sectorProof = MerkabaSphereFlowerAuthority.ClassifySector((int)b.w,
                    root, out int sector);
                Assert.That(actual[i].z != 0u,
                    Is.EqualTo(sectorProof == MerkabaSphereFlowerAuthority.ProofClassification.Certain));
                if (actual[i].z != 0u)
                {
                    Assert.That(actual[i].y, Is.EqualTo((uint)sector));
                    certainSectors.Add(((int)b.w, actual[i].y));
                }
            }
            Assert.That(certainSectors.Count,
                Is.EqualTo(MerkabaSphereFlowerAuthority.SectorBoundaries.Length));
            Assert.That(bounds[2], Is.EqualTo(bounds[3]),
                "tangent signs evaluate the same single root");
        }

        private static void DescribeRootFailure(ComputeShader shader, float4 a, float4 b)
        {
            int kernel = shader.FindKernel("RootIntervalStagesProbe");
            using var input = new ComputeBuffer(2, 16);
            using var flags = new ComputeBuffer(1, 16);
            using var stages = new ComputeBuffer(4, 16);
            input.SetData(new[] { a, b });
            shader.SetBuffer(kernel, "_ProbeCoefficients", input);
            shader.SetBuffer(kernel, "_ProbeReduced", flags);
            shader.SetBuffer(kernel, "_ProbeRootIntervals", stages);
            shader.Dispatch(kernel, 1, 1, 1);
            uint4 result = Read<uint4>(flags)[0];
            float4[] values = Read<float4>(stages);
            TestContext.WriteLine($"Root ABC={a}/{b}: " +
                $"class/sqrt/xDivision/yDivision={result}; " +
                $"Q,delta={math.asuint(values[0])}; turn,x={math.asuint(values[1])}; " +
                $"y,rootX={math.asuint(values[2])}; rootY={math.asuint(values[3])}");
        }

        [Test, Timeout(30000)]
        public void PlaneIntervals_EncloseCpuQuantizationAndOrderedOperationBounds()
        {
            float normalBound = (float)(2.0 * Math.Sqrt(18.0) / 1023.0);
            float offsetBound = MerkabaSphereFlowerAuthority.LatticeStep / 254f;
            float offset = MerkabaSphereFlowerAuthority.LatticeStep / 5f;
            var inputs = new List<float4>();
            var expected = new List<MerkabaSphereFlowerAuthority.Interval3>();
            for (int level = 0; level < 3; level++)
            for (int line = 0; line < MerkabaSphereFlowerAuthority.LineClassCount; line++)
            foreach (int sign in new[] { -1, 1 })
            foreach (float3 normal in new[] { new float3(1, 0, 0),
                new float3(0, 1, 0), math.normalize(new float3(1, 2, -3)) })
            {
                int3 d = sign * MerkabaSphereFlowerAuthority.Lines[line].Direction;
                var loop = MerkabaSphereFlowerAuthority.EvaluateLoop(level,
                    new(d.x, d.y, d.z), line);
                expected.Add(MerkabaSphereFlowerAuthority.RestrictPlaneToLoop(
                    new float3(0), normal, offset, normalBound, offsetBound, loop));
                inputs.Add(new float4(normal, offset));
                inputs.Add(new float4(loop.Center, loop.Radius));
                inputs.Add(new float4(normalBound, offsetBound, line, 0));
            }
            using var input = new ComputeBuffer(inputs.Count, 16);
            using var results = new ComputeBuffer(expected.Count, 16);
            using var intervals = new ComputeBuffer(2 * expected.Count, 16);
            input.SetData(inputs);
            ComputeShader shader = Shader("Tests/Editor/MerkabaObservationBinsProbe.compute");
            int kernel = shader.FindKernel("PlaneIntervalsProbe");
            // The generated alphabet now lives in the shared read-only table
            // buffer, so a standalone proof must upload it exactly as
            // production does. Without it the probe reads unbound memory.
            using var tables = MerkabaGrid.CreateFlowerTableBuffer();
            shader.SetBuffer(kernel, "_M8FlowerTables", tables);
            shader.SetInt("_ProbeRecordCount", expected.Count);
            shader.SetBuffer(kernel, "_ProbeCoefficients", input);
            shader.SetBuffer(kernel, "_ProbeReduced", results);
            shader.SetBuffer(kernel, "_ProbeRootIntervals", intervals);
            shader.Dispatch(kernel, (expected.Count + 63) / 64, 1, 1);
            uint4[] status = Read<uint4>(results);
            float4[] actual = Read<float4>(intervals);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.That(status[i].x, Is.EqualTo(1u), $"plane {i}");
                var e = expected[i];
                float4 ab = actual[2 * i], c = actual[2 * i + 1];
                Assert.That(ab.x, Is.LessThanOrEqualTo(e.X.Lower));
                Assert.That(ab.y, Is.GreaterThanOrEqualTo(e.X.Upper));
                Assert.That(ab.z, Is.LessThanOrEqualTo(e.Y.Lower));
                Assert.That(ab.w, Is.GreaterThanOrEqualTo(e.Y.Upper));
                Assert.That(c.x, Is.LessThanOrEqualTo(e.Z.Lower));
                Assert.That(c.y, Is.GreaterThanOrEqualTo(e.Z.Upper),
                    $"C upper case {i}: {inputs[3*i]} {inputs[3*i+1]} {inputs[3*i+2]}; GPU={c.xy}, CPU={e.Z}");
            }
        }

        private sealed class OwnerWorld : IDisposable
        {
            private readonly List<ComputeBuffer> _buffers = new();
            private readonly ComputeBuffer _sources, _hash, _owners, _claims,
                _blockRefs, _presence, _pending, _loads, _loadCursor;
            public readonly ComputeBuffer Bins, Counters, Touched, Args, Records, TileRefs;
            private readonly ComputeBuffer _tileBits, _tileRecords;
            public readonly uint4[] Expected;
            private readonly int _pointCount;
            public int Token { get; private set; } = Generation;

            public void BeginNextSnapshot()
            {
                Token++;
                Counters.SetData(new uint[] { (uint)Token }, 0, MerkabaGrid.CounterObservationToken, 1);
            }

            public OwnerWorld(int3[] points)
            {
                _pointCount = points.Length;
                var blocks = new Dictionary<int3, int>();
                var chunks = new Dictionary<(int, int), int>();
                var tiles = new Dictionary<(int, int), int>();
                Expected = new uint4[points.Length * 8];
                for (int i = 0; i < points.Length; i++)
                for (int ordinal = 0; ordinal < 8; ordinal++)
                {
                    MerkabaSpatial.Address a = MerkabaSpatial.Encode(
                        MerkabaSphereFlowerAuthority.OverlapOwner(points[i], ordinal));
                    if (!blocks.TryGetValue(a.BlockCoord, out int block))
                        blocks.Add(a.BlockCoord, block = blocks.Count);
                    if (!chunks.TryGetValue((block, a.ChunkLocal), out int chunk))
                        chunks.Add((block, a.ChunkLocal), chunk = chunks.Count);
                    if (!tiles.TryGetValue((chunk, a.TileLocal), out int slot))
                        tiles.Add((chunk, a.TileLocal), slot = tiles.Count);
                    Expected[8 * i + ordinal] = new uint4(
                        (uint)(slot * 512 + a.KernelLocal), (uint)i, 0x150,
                        0x80000000u | (uint)i);
                }
                var hash = new uint4[MerkabaSpatial.HashEntryCount];
                var owners = new uint4[MerkabaSpatial.OwnerChunkOffset + chunks.Count];
                foreach (var block in blocks)
                {
                    uint2 pair = MerkabaSpatial.BucketSearchOrder(block.Key);
                    int entry = -1;
                    for (int probe = 0; probe < 8; probe++)
                    {
                        int candidate = (int)pair[probe / 4] * 4 + probe % 4;
                        if (hash[candidate].w != 0u) continue;
                        entry = candidate;
                        break;
                    }
                    Assert.That(entry, Is.GreaterThanOrEqualTo(0));
                    hash[entry] = new uint4(math.asuint(block.Key), (uint)block.Value + 1u);
                    owners[block.Value] = new uint4(math.asuint(block.Key), 0);
                }
                var blockRefs = new uint[blocks.Count * 512];
                foreach (var chunk in chunks)
                {
                    blockRefs[chunk.Key.Item1 * 512 + chunk.Key.Item2] = (uint)chunk.Value + 1u;
                    owners[MerkabaSpatial.OwnerChunkOffset + chunk.Value] =
                        new uint4((uint)chunk.Key.Item1, (uint)chunk.Key.Item2, 0, 0);
                }
                var tileRefs = new uint[chunks.Count * 64];
                foreach (var tile in tiles)
                    tileRefs[tile.Key.Item1 * 64 + tile.Key.Item2] = (uint)tile.Value + 1u;
                _hash = Upload(hash, 16);
                _owners = Upload(owners, 16);
                _blockRefs = Upload(blockRefs, 4);
                TileRefs = Upload(tileRefs, 4);
                _tileBits = Upload(new uint4[tiles.Count * 16], 16);
                var tileRecords = new uint4[2 * tiles.Count];
                foreach (var tile in tiles)
                {
                    tileRecords[2 * tile.Value] = new uint4((uint)tile.Key.Item1,
                        (uint)tile.Key.Item2, 0u, 0u);
                    tileRecords[2 * tile.Value + 1] = new uint4(0u, 0u, 0u, 1u);
                }
                _tileRecords = Upload(tileRecords, 16);
                _claims = Upload(new uint2[MerkabaSpatial.ClaimRecordCount], 8);
                _presence = Upload(new uint[chunks.Count * 9], 4);
                _pending = Upload(new uint[Slots], 4);
                _loads = Upload(new uint4[MerkabaGrid.LoadRequestCapacity], 16);
                _loadCursor = Upload(new uint[1], 4);
                _sources = Upload(points.Select((p, i) => new uint4(math.asuint(p), (uint)i)).ToArray(), 16);
                Bins = Upload(new uint4[Slots], 16);
                var counters = new uint[MerkabaGrid.CounterCount];
                counters[MerkabaGrid.CounterObservationToken] = Generation;
                Counters = Upload(counters, 4);
                Touched = Upload(new uint[Slots], 4);
                Args = Upload(new uint[MerkabaObservationBinsGpu.DispatchArgumentWords], 4);
                Records = Upload(new uint4[Expected.Length], 16);
            }

            private ComputeBuffer Upload<T>(T[] data, int stride) where T : struct
            {
                var buffer = new ComputeBuffer(data.Length, stride);
                _buffers.Add(buffer);
                buffer.SetData(data);
                return buffer;
            }

            public void DispatchOwners(string name)
            {
                ComputeShader shader = Shader("Tests/Editor/MerkabaObservationBinsProbe.compute");
                int kernel = shader.FindKernel(name);
                Bind(shader, kernel, Bins, Counters, Expected.Length, Token);
                shader.SetInt("_ProbeRecordCount", _pointCount);
                shader.SetBuffer(kernel, "_ProbeRecords", _sources);
                shader.SetBuffer(kernel, "_M8ObservationDispatchArgs", Args);
                if (name == "CountOwnersProbe")
                {
                    shader.SetBuffer(kernel, "_M8TouchedTileQueue", Touched);
                    shader.SetBuffer(kernel, "_M8HashEntries", _hash);
                    shader.SetBuffer(kernel, "_M8ClaimQueue", _claims);
                    shader.SetBuffer(kernel, "_M8BlockChunkRefs", _blockRefs);
                    shader.SetBuffer(kernel, "_M8ChunkTileRefs", TileRefs);
                }
                else
                {
                    shader.SetBuffer(kernel, "_M8HashEntriesRead", _hash);
                    shader.SetBuffer(kernel, "_M8BlockChunkRefsRead", _blockRefs);
                    shader.SetBuffer(kernel, "_M8ChunkTileRefsRead", TileRefs);
                    shader.SetBuffer(kernel, "_M8ObservationRecords", Records);
                    shader.SetBuffer(kernel, "_M8TileBits", _tileBits);
                }
                shader.Dispatch(kernel, (_pointCount + 63) / 64, 1, 1);
            }

            public void AssertEndpointSupport(bool emitted)
            {
                var expected = new uint[_tileBits.count];
                if (emitted)
                    foreach (uint4 record in Expected)
                        expected[record.x >> 5] |= 1u << (int)(record.x & 31u);
                uint4[] actual = Read<uint4>(_tileBits);
                CollectionAssert.AreEqual(expected, actual.Select(word => word.z),
                    "Only emitted frozen endpoints may mark this snapshot's touched support.");
            }

            public void ServiceTileRequests()
            {
                ComputeShader shader = Shader("Runtime/Shaders/MerkabaObservationBins.compute");
                int kernel = shader.FindKernel("ResolveObservationTileRequests");
                shader.SetBuffer(kernel, "_M8Counters", Counters);
                shader.SetBuffer(kernel, "_M8ClaimQueue", _claims);
                shader.SetBuffer(kernel, "_M8ChunkTileRefs", TileRefs);
                shader.SetBuffer(kernel, "_M8OwnerRecordsRead", _owners);
                shader.SetBuffer(kernel, "_M8ChunkPresence", _presence);
                shader.SetBuffer(kernel, "_M8PendingNewTileRefs", _pending);
                shader.SetBuffer(kernel, "_M8LoadRequests", _loads);
                shader.SetBuffer(kernel, "_M8LoadRequestReadCount", _loadCursor);
                shader.SetBuffer(kernel, "_M8ObservationDispatchArgs", Args);
                shader.Dispatch(kernel, 1, 1, 1);
            }

            public void ResetBins()
            {
                ComputeShader shader = Shader("Tests/Editor/MerkabaObservationBinsProbe.compute");
                int kernel = shader.FindKernel("ResetObservationBins");
                Bind(shader, kernel, Bins, Counters, Expected.Length, Token);
                shader.SetBuffer(kernel, "_M8TouchedTileQueue", Touched);
                shader.SetBuffer(kernel, "_M8ObservationDispatchArgs", Args);
                shader.SetBuffer(kernel, "_M8TileBits", _tileBits);
                shader.SetBuffer(kernel, "_M8TileRecordsRead", _tileRecords);
                shader.Dispatch(kernel, 1, 1, 1);
            }

            public void Dispose()
            {
                foreach (ComputeBuffer buffer in _buffers) buffer.Dispose();
            }
        }

        [Test, Timeout(30000)]
        public void Reduction_IntersectsBeforeSelectionAndRejectsConflicts()
        {
            var records = new[]
            {
                new uint4(0, 3, 0, 0x80000003), new uint4(0, 9, 0, 0x80000009),
                new uint4(1, 0, 0, 0), new uint4(1, 1, 0x100, 1),
                new uint4(2, 2, 0, 2), new uint4(2, 3, 0, 3),
                new uint4(511, 7, 0x150, 0x80000007)
            };
            var intervals = new[]
            {
                new int2(-10, 20), new int2(-3, 15), new int2(0, 1),
                new int2(0, 1), new int2(-3, 0), new int2(10, 11), new int2(-5, 5)
            };
            using var sources = new ComputeBuffer(records.Length, 16);
            using var metric = new ComputeBuffer(records.Length, 8);
            using var output = new ComputeBuffer(512, 16);
            ComputeShader shader = Shader("Tests/Editor/MerkabaObservationBinsProbe.compute");
            int kernel = shader.FindKernel("ReduceBinsProbe");
            shader.SetInt("_ProbeRecordCount", records.Length);
            shader.SetBuffer(kernel, "_ProbeRecords", sources);
            shader.SetBuffer(kernel, "_ProbeIntervals", metric);
            shader.SetBuffer(kernel, "_ProbeReduced", output);
            var expected = Enumerable.Repeat(new uint4(uint.MaxValue), 512).ToArray();
            expected[0] = new uint4(0, unchecked((uint)-3), 15, 3);
            expected[511] = new uint4(0x150, unchecked((uint)-5), 5, 7);
            for (int order = 0; order < 2; order++)
            {
                sources.SetData(records);
                metric.SetData(intervals);
                shader.Dispatch(kernel, 1, 1, 1);
                var actual = new uint4[512];
                output.GetData(actual);
                CollectionAssert.AreEqual(expected, actual);
                Array.Reverse(records);
                Array.Reverse(intervals);
            }
        }
    }
}
