using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Authority = Genesis.RoomScan.MerkabaSphereFlowerAuthority;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaDepthCertificateTests
    {
        private const string ProbePath =
            "Packages/com.genesis.roomscan/Tests/Editor/MerkabaDepthCertificateProbe.compute";

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct Source
        {
            internal float Lower, Upper;
            internal uint Valid, Reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct Query
        {
            internal int4 Bounds;
            internal int4 Parameters;
            internal float4 Depth;

            internal Query(int eye, int2 image, int2 lo, int2 hi, float upper)
            {
                Bounds = new int4(lo, hi);
                Parameters = new int4(eye, image.x, image.y, 0);
                Depth = new float4(upper, 0f, 0f, 0f);
            }
        }

        private sealed class Fixture
        {
            internal readonly string Name;
            internal readonly int2 Image;
            internal readonly Source[] Sources;

            internal Fixture(string name, int width, int height)
            {
                Name = name;
                Image = new int2(width, height);
                Sources = new Source[2 * width * height];
                for (int i = 0; i < Sources.Length; i++)
                    Sources[i] = new Source { Lower = 8f, Upper = 9f, Valid = 1u };
            }

            internal int Index(int eye, int x, int y) =>
                eye * Image.x * Image.y + y * Image.x + x;

            internal void Set(int eye, int x, int y, float lower,
                float upper = 9f, uint valid = 1u) =>
                Sources[Index(eye, x, y)] = new Source
                { Lower = lower, Upper = upper, Valid = valid };
        }

        [Test, Timeout(30000)]
        public void Cpu_CommonPrefixAndExactCover_MatchEveryPixelForEverySmallRectangle()
        {
            Fixture fixture = CreateFixture("exhaustive-small");
            AssertQueries(fixture, BuildCpu(fixture), SmallQueries(fixture), null);
        }

        [TestCase("uniform")]
        [TestCase("interior-foreground")]
        [TestCase("invalid-boundaries")]
        [TestCase("non-power-fov")]
        [TestCase("full-512")]
        [Timeout(30000)]
        public void Cpu_FrozenFootprints_HaveZeroFalseThrough(string name)
        {
            Fixture fixture = CreateFixture(name);
            AssertQueries(fixture, BuildCpu(fixture), Queries(fixture), null);
        }

        [TestCase("exhaustive-small")]
        [TestCase("uniform")]
        [TestCase("interior-foreground")]
        [TestCase("invalid-boundaries")]
        [TestCase("non-power-fov")]
        [TestCase("full-512")]
        [Timeout(60000)]
        public void Hlsl_BuildAndBothQueryPaths_MatchCpuAndIndependentPixelOracle(string name)
        {
            Assert.That(SystemInfo.supportsComputeShaders, Is.True,
                "Behavioral certificate parity requires a compute-capable test device.");
            Fixture fixture = CreateFixture(name);
            List<Query> queries = name == "exhaustive-small"
                ? SmallQueries(fixture) : Queries(fixture);
            Authority.DepthCertificateNode[] cpu = BuildCpu(fixture);
            uint4[] results = Dispatch(fixture, queries, cpu);
            AssertQueries(fixture, cpu, queries, results);
        }

        [Test]
        public void InteriorForegroundOutsideAllFourCorners_BlocksThroughInBothEyes()
        {
            Fixture fixture = CreateFixture("interior-foreground");
            Authority.DepthCertificateNode[] cpu = BuildCpu(fixture);
            int2 lo = new(16, 16), hi = new(47, 47);
            for (int eye = 0; eye < 2; eye++)
            {
                foreach (int2 corner in new[] { lo, new int2(lo.x, hi.y),
                             new int2(hi.x, lo.y), hi })
                    Assert.That(fixture.Sources[fixture.Index(eye, corner.x, corner.y)].Lower,
                        Is.GreaterThan(4f), "All corner samples alone would incorrectly pass.");
                Query query = new(eye, fixture.Image, lo, hi, 4f);
                Assert.That(EveryPixel(fixture, query, lo, hi), Is.False);
                Assert.That(Classify(cpu, query, false),
                    Is.EqualTo(Authority.ProofClassification.Ambiguous));
                Assert.That(Classify(cpu, query, true),
                    Is.EqualTo(Authority.ProofClassification.Ambiguous));
            }
        }

        [TestCase("clear", 1u)]
        [TestCase("interior-occluder", 2u)]
        [TestCase("new-direct-object", 0u)]
        [TestCase("right-eye-only", 1u)]
        [TestCase("left-fov-invalid", 1u)]
        [TestCase("both-fov-invalid", 2u)]
        [TestCase("behind-eye", 2u)]
        [TestCase("near-clip", 2u)]
        [TestCase("expanded-fov", 2u)]
        [TestCase("negative-translation", 1u)]
        [Timeout(120000)]
        public void FullSupportAndDirectPrecedence_AgreeOnCpuAndGpu(string scenario, uint expected)
        {
            Fixture fixture = CreateFixture("uniform");
            float3 minimum = new(-0.125f, -0.125f, -1.25f);
            float3 maximum = new(0.125f, 0.125f, -1f);
            var views = new[] { Matrix4x4.identity, Matrix4x4.identity };
            Matrix4x4 projection = Matrix4x4.Perspective(90f,
                fixture.Image.x / (float)fixture.Image.y, 0.1f, 10f);
            var projections = new[] { projection, projection };
            var errors = new[] { new Vector4(0f, 0f, 0f, 1f), new Vector4(0f, 0f, 0f, 1f) };
            if (scenario == "interior-occluder")
                for (int eye = 0; eye < 2; eye++)
                    fixture.Set(eye, fixture.Image.x / 2, fixture.Image.y / 2, 0.5f);
            if (scenario == "right-eye-only")
                for (int i = 0; i < fixture.Image.x * fixture.Image.y; i++)
                    fixture.Sources[i].Valid = 0u;
            if (scenario == "left-fov-invalid")
                views[0] = Matrix4x4.Translate(new Vector3(-4f, 0f, 0f));
            if (scenario == "both-fov-invalid")
            {
                minimum.x = 0.75f;
                maximum.x = 1.25f;
            }
            if (scenario == "behind-eye") { minimum.z = 0.125f; maximum.z = 0.25f; }
            if (scenario == "near-clip") { minimum.z = -0.25f; maximum.z = -0.0625f; }
            if (scenario == "expanded-fov")
            {
                minimum.x = 0.75f;
                maximum.x = 0.875f;
                errors[0].y = errors[1].y = 0.25f;
            }
            if (scenario == "negative-translation")
            {
                float3 translation = new(-8f, -4f, -2f);
                minimum += translation;
                maximum += translation;
                views[0] = views[1] = Matrix4x4.Translate(-(Vector3)translation);
            }
            bool endpoint = scenario == "new-direct-object";
            Authority.DepthCertificateNode[] nodes = BuildCpu(fixture);
            uint cpu = (uint)Authority.ClassifyStereoDepthSupport(nodes, fixture.Image,
                minimum, maximum, new[] { (float4x4)views[0], (float4x4)views[1] },
                new[] { (float4x4)projections[0], (float4x4)projections[1] },
                new[] { (float4)errors[0], (float4)errors[1] }, endpoint);
            Assert.That(cpu, Is.EqualTo(expected), scenario + " CPU support classification");
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ProbePath);
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel("QuerySupportProbe");
            using var certificate = new ComputeBuffer(nodes.Length, 8);
            using var output = new ComputeBuffer(1, 16);
            certificate.SetData(nodes);
            shader.SetBuffer(kernel, "_M8DepthCertificate", certificate);
            shader.SetBuffer(kernel, "_ProbeDepthResults", output);
            shader.SetInts("_ProbeImageSize", fixture.Image.x, fixture.Image.y);
            shader.SetVector("_ProbeSupportMin", new Vector4(minimum.x, minimum.y, minimum.z, 0f));
            shader.SetVector("_ProbeSupportMax", new Vector4(maximum.x, maximum.y, maximum.z, 0f));
            shader.SetMatrixArray("_ProbeViews", views);
            shader.SetMatrixArray("_ProbeProjections", projections);
            shader.SetVectorArray("_M8DepthErrorBounds", errors);
            shader.SetInt("_ProbeEndpointIntersects", endpoint ? 1 : 0);
            shader.Dispatch(kernel, 1, 1, 1);
            var result = new uint4[1];
            output.GetData(result);
            Assert.That(result[0].x, Is.EqualTo(expected), scenario + " GPU support classification");
        }

        [Test]
        public void ExactCover_ExcludesUnrelatedForegroundAndInvalidPadding()
        {
            foreach (string name in new[] { "full-512", "non-power-fov" })
            {
                Fixture fixture = CreateFixture(name);
                Authority.DepthCertificateNode[] cpu = BuildCpu(fixture);
                int2 lo = name == "full-512" ? new int2(255) : new int2(61, 30);
                int2 hi = name == "full-512" ? new int2(257) : new int2(66, 34);
                Query query = new(0, fixture.Image, lo, hi, 4f);
                Assert.That(EveryPixel(fixture, query, lo, hi), Is.True, name);
                Assert.That(Classify(cpu, query, true),
                    Is.EqualTo(Authority.ProofClassification.Ambiguous), name);
                Assert.That(Classify(cpu, query, false),
                    Is.EqualTo(Authority.ProofClassification.Certain), name);
            }
        }

        [Test]
        public void StrictEquality_IsNotThrough_AndAdjacentBinary32ValuesAreNotAnEpsilon()
        {
            Fixture fixture = CreateFixture("uniform");
            Authority.DepthCertificateNode[] cpu = BuildCpu(fixture);
            int2 pixel = new(23, 29);
            float before = math.asfloat(math.asuint(8f) - 1u);
            float after = math.asfloat(math.asuint(8f) + 1u);
            foreach (bool envelopeOnly in new[] { false, true })
            foreach (int eye in new[] { 0, 1 })
            {
                Assert.That(Classify(cpu, new Query(eye, fixture.Image, pixel, pixel, before),
                    envelopeOnly), Is.EqualTo(Authority.ProofClassification.Certain));
                Assert.That(Classify(cpu, new Query(eye, fixture.Image, pixel, pixel, 8f),
                    envelopeOnly), Is.EqualTo(Authority.ProofClassification.Ambiguous));
                Assert.That(Classify(cpu, new Query(eye, fixture.Image, pixel, pixel, after),
                    envelopeOnly), Is.EqualTo(Authority.ProofClassification.Ambiguous));
            }
        }

        [Test]
        public void Valid512Quadtree_CannotExhaust64EntryDepthFirstStack()
        {
            // A depth-first pop can leave at most three pending siblings per
            // ancestor. The 512² domain has nine splits: 1 + 3*9 =28. Fully
            // expand every node here, even those the real cover prunes/accepts;
            // that realizes the structural upper bound, not a query copy.
            int side = Authority.DepthCertificateSide, depth = 0;
            while (side > 1)
            {
                Assert.That(side & 1, Is.Zero);
                side >>= 1;
                depth++;
            }
            var pending = new Stack<int>();
            pending.Push(depth);
            int peak = 1, visited = 0;
            while (pending.Count != 0)
            {
                int remaining = pending.Pop();
                visited++;
                if (remaining == 0) continue;
                for (int child = 0; child < 4; child++) pending.Push(remaining - 1);
                peak = Math.Max(peak, pending.Count);
            }
            Assert.That(depth, Is.EqualTo(9));
            Assert.That(visited, Is.EqualTo(Authority.DepthCertificateEyeStride));
            Assert.That(peak, Is.EqualTo(1 + 3 * depth));
            Assert.That(peak, Is.EqualTo(28));
            Assert.That(peak, Is.LessThan(Authority.DepthCertificateGpuStackCapacity));
            Assert.That(Authority.DepthCertificateGpuStackCapacity, Is.EqualTo(64));
            // No fabricated exhaustion fixture: that production branch is
            // unreachable for valid 512² inputs. Invalid larger domains are
            // exercised by real CPU/HLSL queries and must remain AMBIGUOUS.
        }

        private static Fixture CreateFixture(string name)
        {
            Fixture result = name switch
            {
                "exhaustive-small" => new Fixture(name, 9, 7),
                "non-power-fov" => new Fixture(name, 67, 35),
                "full-512" => new Fixture(name, 512, 512),
                _ => new Fixture(name, 64, 64)
            };
            if (name == "exhaustive-small")
            {
                for (int eye = 0; eye < 2; eye++)
                for (int y = 0; y < result.Image.y; y++)
                for (int x = 0; x < result.Image.x; x++)
                    result.Set(eye, x, y, 1f + ((3 * x + y + eye) & 7), 10f);
                result.Set(0, 4, 3, 8f, valid: 0u);
                result.Set(1, 2, 5, 8f, valid: 2u);
            }
            else if (name == "interior-foreground")
            {
                result.Set(0, 23, 29, 0.5f);
                result.Set(1, 37, 22, 0.5f);
            }
            else if (name == "invalid-boundaries")
            {
                for (int y = 8; y < 56; y++) result.Set(0, 31, y, 8f, valid: 0u);
                result.Set(1, 23, 29, 8f, valid: 2u);
                result.Set(1, 0, 0, 0f);
                result.Set(1, 63, 0, -1f);
                result.Set(1, 0, 63, 1f, float.PositiveInfinity);
                result.Set(1, 63, 63, float.PositiveInfinity, float.PositiveInfinity);
            }
            else if (name == "full-512")
            {
                result.Set(0, 0, 0, 0.5f);
                result.Set(1, 511, 511, 8f, valid: 0u);
            }
            return result;
        }

        private static List<Query> SmallQueries(Fixture fixture)
        {
            var result = new List<Query>();
            for (int eye = 0; eye < 2; eye++)
            for (int y0 = 0; y0 < fixture.Image.y; y0++)
            for (int y1 = y0; y1 < fixture.Image.y; y1++)
            for (int x0 = 0; x0 < fixture.Image.x; x0++)
            for (int x1 = x0; x1 < fixture.Image.x; x1++)
            foreach (float upper in new[] { 0f, 1f, 4f, 8f })
                result.Add(new Query(eye, fixture.Image, new int2(x0, y0),
                    new int2(x1, y1), upper));
            return result;
        }

        private static List<Query> Queries(Fixture fixture)
        {
            var result = new List<Query>();
            var rectangles = new List<int4>
            {
                new(0, 0, fixture.Image.x - 1, fixture.Image.y - 1),
                new(0, 0, 0, 0),
                new(fixture.Image.x - 1, fixture.Image.y - 1,
                    fixture.Image.x - 1, fixture.Image.y - 1),
                new(16, 16, 47, Math.Min(47, fixture.Image.y - 1)),
                new(23, 29, 23, 29), new(22, 28, 24, 30),
                new(30, 8, 32, Math.Min(55, fixture.Image.y - 1)),
                new(31, 0, 32, fixture.Image.y - 1),
                new(1, 1, fixture.Image.x - 2, fixture.Image.y - 2)
            };
            if (fixture.Name == "full-512") rectangles.Add(new int4(255, 255, 257, 257));
            if (fixture.Name == "non-power-fov") rectangles.Add(new int4(61, 30, 66, 34));
            var random = new System.Random(0x4d38);
            for (int i = 0; i < 96; i++)
            {
                int x = random.Next(fixture.Image.x), y = random.Next(fixture.Image.y);
                rectangles.Add(new int4(x, y,
                    Math.Min(fixture.Image.x - 1, x + random.Next(32)),
                    Math.Min(fixture.Image.y - 1, y + random.Next(32))));
            }
            float[] uppers = { 0f, 0.5f, 1f, 4f,
                math.asfloat(math.asuint(8f) - 1u), 8f,
                math.asfloat(math.asuint(8f) + 1u) };
            for (int eye = 0; eye < 2; eye++)
            foreach (int4 rectangle in rectangles)
            foreach (float upper in uppers)
                result.Add(new Query(eye, fixture.Image, rectangle.xy, rectangle.zw, upper));
            foreach (int4 bounds in new[]
                     {
                         new int4(-1, 0, 1, 1), new int4(0, -1, 1, 1),
                         new int4(0, 0, fixture.Image.x, fixture.Image.y - 1),
                         new int4(0, 0, fixture.Image.x - 1, fixture.Image.y),
                         new int4(2, 2, 1, 3), new int4(2, 2, 3, 1)
                     })
                result.Add(new Query(0, fixture.Image, bounds.xy, bounds.zw, 1f));
            foreach (float invalid in new[]
                     { -1f, float.NaN, float.NegativeInfinity, float.PositiveInfinity })
                result.Add(new Query(0, fixture.Image, new int2(1), new int2(2), invalid));
            foreach (int eye in new[] { -1, 2 })
                result.Add(new Query(eye, fixture.Image, new int2(1), new int2(2), 1f));
            foreach (int2 image in new[]
                     { new int2(0, 64), new int2(64, 0), new int2(513, 512) })
                result.Add(new Query(0, image, new int2(1), new int2(2), 1f));
            return result;
        }

        // Independent reference: no production address, prefix, reduction or
        // dyadic-cover function is used. Every pixel of the requested region
        // must own a finite valid interval with strict depth separation.
        private static bool EveryPixel(Fixture fixture, Query query, int2 lo, int2 hi)
        {
            int eye = query.Parameters.x;
            int2 image = query.Parameters.yz;
            float upper = query.Depth.x;
            if (eye < 0 || eye >= 2 || image.x <= 0 || image.y <= 0 ||
                image.x > 512 || image.y > 512 || !math.all(image == fixture.Image) ||
                lo.x < 0 || lo.y < 0 || hi.x < lo.x || hi.y < lo.y ||
                hi.x >= image.x || hi.y >= image.y ||
                !math.isfinite(upper) || upper < 0f) return false;
            for (int y = lo.y; y <= hi.y; y++)
            for (int x = lo.x; x <= hi.x; x++)
            {
                Source source = fixture.Sources[fixture.Index(eye, x, y)];
                if (source.Valid != 1u || !math.isfinite(source.Lower) ||
                    !math.isfinite(source.Upper) || source.Lower <= 0f ||
                    source.Upper < source.Lower || !(upper < source.Lower)) return false;
            }
            return true;
        }

        private static bool EnvelopePixels(Fixture fixture, Query query, out int level)
        {
            level = -1;
            int2 lo = query.Bounds.xy, hi = query.Bounds.zw;
            if (lo.x < 0 || lo.y < 0 || hi.x < lo.x || hi.y < lo.y ||
                hi.x >= fixture.Image.x || hi.y >= fixture.Image.y) return false;
            // Integer division, not the production xor/leading-bit formula.
            int side = 1;
            level = 0;
            while (lo.x / side != hi.x / side || lo.y / side != hi.y / side)
            {
                side *= 2;
                level++;
            }
            int2 origin = new(lo.x / side * side, lo.y / side * side);
            return EveryPixel(fixture, query, origin, origin + side - 1);
        }

        private static Authority.ProofClassification Classify(
            Authority.DepthCertificateNode[] certificate, Query query, bool envelopeOnly) =>
            Authority.ClassifyDepthCertificate(certificate, query.Parameters.x,
                query.Parameters.yz, query.Bounds.xy, query.Bounds.zw,
                query.Depth.x, envelopeOnly);

        private static Authority.DepthCertificateNode[] BuildCpu(Fixture fixture)
        {
            var result = new Authority.DepthCertificateNode[Authority.DepthCertificateNodeCount];
            int pixels = fixture.Image.x * fixture.Image.y;
            var intervals = new Authority.FloatInterval[pixels];
            var validity = new byte[pixels];
            for (int eye = 0; eye < 2; eye++)
            {
                for (int i = 0; i < pixels; i++)
                {
                    Source source = fixture.Sources[eye * pixels + i];
                    intervals[i] = new Authority.FloatInterval(source.Lower, source.Upper);
                    validity[i] = checked((byte)source.Valid);
                }
                Authority.BuildDepthCertificate(intervals, validity, fixture.Image, eye, result);
            }
            return result;
        }

        private static void AssertQueries(Fixture fixture,
            Authority.DepthCertificateNode[] certificate, List<Query> queries, uint4[] gpu)
        {
            int certain = 0, ambiguous = 0, slowPathProofs = 0;
            for (int i = 0; i < queries.Count; i++)
            {
                Query query = queries[i];
                bool exact = EveryPixel(fixture, query, query.Bounds.xy, query.Bounds.zw);
                bool prefix = EnvelopePixels(fixture, query, out _);
                uint expected = exact ? 1u : 2u, fast = prefix ? 1u : 2u;
                uint cpu = (uint)Classify(certificate, query, false);
                uint cpuFast = (uint)Classify(certificate, query, true);
                if (cpu != expected || cpuFast != fast ||
                    (gpu != null && (gpu[i].x != expected || gpu[i].y != fast)))
                    Assert.Fail($"{fixture.Name} query#{i} eye={query.Parameters.x} " +
                        $"bounds={query.Bounds} upper={query.Depth.x:R}: " +
                        $"pixel oracle exact/prefix={expected}/{fast}, CPU={cpu}/{cpuFast}, " +
                        $"GPU={(gpu == null ? "not requested" : gpu[i].ToString())}");
                if (prefix && !exact) Assert.Fail("A prefix proved a non-free enclosed footprint.");
                if (gpu != null && gpu[i].w != Authority.DepthCertificateGpuStackCapacity)
                    Assert.Fail("Generated GPU stack capacity differs from the CPU contract.");
                if (exact) certain++; else ambiguous++;
                if (exact && !prefix) slowPathProofs++;
            }
            Assert.That(certain, Is.GreaterThan(0), fixture.Name + " must exercise real THROUGH");
            Assert.That(ambiguous, Is.GreaterThan(0), fixture.Name + " must exercise blocked THROUGH");
            if (fixture.Name == "exhaustive-small" || fixture.Name == "non-power-fov" ||
                fixture.Name == "full-512")
                Assert.That(slowPathProofs, Is.GreaterThan(0),
                    fixture.Name + " must exercise a successful exact cover after envelope failure");
            TestContext.WriteLine($"{fixture.Name}: {queries.Count} complete-pixel comparisons, " +
                $"{certain} THROUGH, {ambiguous} AMBIGUOUS, " +
                $"{slowPathProofs} successful exact covers after failed envelopes; " +
                $"CPU{(gpu == null ? string.Empty : "/HLSL")} zero false THROUGH.");
        }

        private static uint4[] Dispatch(Fixture fixture, List<Query> queries,
            Authority.DepthCertificateNode[] cpu)
        {
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ProbePath);
            Assert.That(shader, Is.Not.Null, ProbePath);
            Assert.That(Marshal.SizeOf<Source>(), Is.EqualTo(16));
            Assert.That(Marshal.SizeOf<Query>(), Is.EqualTo(48));
            using var source = new ComputeBuffer(fixture.Sources.Length, 16);
            using var certificate = new ComputeBuffer(Authority.DepthCertificateNodeCount, 8);
            using var cases = new ComputeBuffer(queries.Count, 48);
            using var results = new ComputeBuffer(queries.Count, 16);
            source.SetData(fixture.Sources);
            cases.SetData(queries);
            shader.SetInts("_ProbeImageSize", fixture.Image.x, fixture.Image.y);
            int build = shader.FindKernel("BuildLeavesProbe");
            shader.SetBuffer(build, "_ProbeDepthSources", source);
            shader.SetBuffer(build, "_ProbeCertificateWrite", certificate);
            shader.Dispatch(build, 2 * 512 * 512 / 64, 1, 1);
            int reduce = shader.FindKernel("ReduceLevelProbe");
            shader.SetBuffer(reduce, "_ProbeCertificateWrite", certificate);
            for (int level = 1; level < Authority.DepthCertificateLevelCount; level++)
            {
                int side = 512 >> level;
                shader.SetInt("_ProbeLevel", level);
                shader.Dispatch(reduce, (2 * side * side + 63) / 64, 1, 1);
            }
            var nodes = new uint2[cpu.Length];
            certificate.GetData(nodes);
            for (int i = 0; i < nodes.Length; i++)
                if (nodes[i].x != math.asuint(cpu[i].MinLowerDepth) ||
                    nodes[i].y != cpu[i].AllValid)
                    Assert.Fail($"{fixture.Name} CPU/HLSL certificate node#{i}: " +
                        $"GPU={nodes[i]}, CPU={cpu[i].MinLowerDepth:R}/{cpu[i].AllValid}");
            int queryKernel = shader.FindKernel("QueryCertificateProbe");
            shader.SetInt("_ProbeQueryCount", queries.Count);
            shader.SetBuffer(queryKernel, "_M8DepthCertificate", certificate);
            shader.SetBuffer(queryKernel, "_ProbeDepthQueries", cases);
            shader.SetBuffer(queryKernel, "_ProbeDepthResults", results);
            shader.Dispatch(queryKernel, (queries.Count + 63) / 64, 1, 1);
            var output = new uint4[queries.Count];
            results.GetData(output);
            return output;
        }
    }
}
