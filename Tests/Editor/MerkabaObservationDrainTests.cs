using System;
using NUnit.Framework;
using Unity.Mathematics;
using A = Genesis.RoomScan.MerkabaSphereFlowerAuthority;

namespace Genesis.RoomScan.Tests
{
    /// <summary>Reduced-evidence behavioral parity. Not a GPU dispatch,
    /// camera acquisition, carrier-admission or whole-observation proof.</summary>
    public sealed class MerkabaObservationDrainTests
    {
        private sealed class Fixture
        {
            internal int3 Owner = new(-9, 0, 31);
            internal float3 Normal;
            internal float Offset;
            internal float NormalError = A.FloatInterval.Enclose(2.0 * Math.Sqrt(18.0) / 1023.0).Upper;
            internal float OffsetError = A.FloatInterval.Enclose((double)A.LatticeStep / (2 * 127)).Upper;
            internal A.PhaseDrainEvidence[] Phases;
            internal A.RgbDrainEvidence[] Rgb = BuildRgb(false);

            internal A.ObservationDrainOracle Create(uint epoch = 7u) =>
                new(Owner, Normal, Offset, NormalError, OffsetError, epoch, Phases, Rgb);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(7)]
        [TestCase(64)]
        public void ReducedImmutableEvidence_QuantumAndRetryPreserveExactPayload(int quantum)
        {
            Fixture fixture = BuildPhaseFixture();
            var eager = fixture.Create();
            eager.Drain(int.MaxValue);
            Assert.That(eager.Complete, Is.True);
            Assert.That(eager.CommittedDetail(), Has.Length.EqualTo(2),
                "The fixture must commit real, dependent L1 and L2 R2 innovations.");

            var split = fixture.Create();
            int attempt = 0;
            while (!split.Complete && attempt < 1024)
            {
                // Scheduling varies; the same frozen object supplies every
                // interval. No observation sequence number enters a key.
                bool available = attempt++ % 3 != 0;
                split.Drain(quantum, available);
            }
            Assert.That(split.Complete, Is.True, "One immutable observation must drain without camera input.");
            Assert.That(DetailBits(split), Is.EqualTo(DetailBits(eager)));
            Assert.That(ColorBits(split), Is.EqualTo(ColorBits(eager)));
            Assert.That(split.RgbSplitBits, Is.EqualTo(eager.RgbSplitBits));
            Assert.That(split.Unresolved, Is.EqualTo(eager.Unresolved));
            Assert.That(split.Drain(quantum), Is.Zero);
            Assert.That(DetailBits(split), Is.EqualTo(DetailBits(eager)),
                "Retry after completion must not promote evidence a second time.");
        }

        [Test]
        public void Backpressure_HoldsCurrentMetricCandidateUntilPublication()
        {
            var drain = BuildPhaseFixture().Create();
            int pending = drain.PendingCount;
            for (int retry = 0; retry < 5; retry++)
            {
                Assert.That(drain.Drain(1, false), Is.Zero);
                Assert.That(drain.PendingCount, Is.EqualTo(pending));
                Assert.That(drain.CommittedDetail(), Is.Empty);
            }
            Assert.That(drain.Drain(1), Is.EqualTo(1));
            Assert.That(drain.CommittedDetail(), Has.Length.EqualTo(1));
            Assert.That(drain.PendingCount, Is.EqualTo(pending - 1));
        }

        [Test]
        public void OwnerEpoch_NotSchedulingHistoryIsPersistedWithTheMetric()
        {
            Fixture fixture = BuildPhaseFixture();
            var first = fixture.Create(17);
            var second = fixture.Create(18);
            first.Drain(int.MaxValue);
            while (!second.Complete) second.Drain(1);
            var a = first.CommittedDetail();
            var b = second.CommittedDetail();
            Assert.That(a, Has.Length.EqualTo(2));
            Assert.That(b, Has.Length.EqualTo(a.Length));
            for (int index = 0; index < a.Length; index++)
            {
                Assert.That(b[index].Key, Is.EqualTo(a[index].Key));
                Assert.That(b[index].Lower, Is.EqualTo(a[index].Lower));
                Assert.That(b[index].Upper, Is.EqualTo(a[index].Upper));
                Assert.That(a[index].ParentEpoch, Is.EqualTo(17u));
                Assert.That(b[index].ParentEpoch, Is.EqualTo(18u));
                Assert.That(a[index].IsValidFor(18), Is.False);
            }
        }

        [Test]
        public void UniformRgb_PrunesTheFixedDescendantProgramWithoutAllocatingGroups()
        {
            var drain = new A.ObservationDrainOracle(int3.zero, new float3(0, 0, 1),
                0, 0, 0, 1, Array.Empty<A.PhaseDrainEvidence>(), BuildRgb(true));
            while (!drain.Complete) drain.Drain(2);
            Assert.That(drain.Evaluations, Is.EqualTo(1), "Only the root seven-footprint reduction is needed.");
            Assert.That(drain.PrunedRgbRegions, Is.EqualTo(57));
            Assert.That(drain.RgbSplitBits, Is.Zero);
            Assert.That(drain.CommittedCanonicalRgb(), Is.Empty);
            Assert.That(drain.CommittedDetail(), Is.Empty);
        }

        [Test]
        public void FrozenRgbFootprints_AreCopiedAndIncompleteCoverageCannotSplit()
        {
            var original = ColorFootprints(true);
            var group = new A.RgbDrainEvidence(0, original, original, 0x7fu, 0x7fu);
            Array.Fill(original, A.FloatInterval.Singleton(0.5f));
            var drain = new A.ObservationDrainOracle(int3.zero, new float3(0, 0, 1),
                0, 0, 0, 1, Array.Empty<A.PhaseDrainEvidence>(), new[] { group });
            drain.Drain(1);
            Assert.That(drain.RgbSplitBits, Is.EqualTo(1UL));
            Assert.That(drain.CommittedCanonicalRgb(), Has.Length.EqualTo(7));

            var partial = new A.RgbDrainEvidence(0, ColorFootprints(true),
                ColorFootprints(true), 0x7fu, 0x3fu);
            var incomplete = new A.ObservationDrainOracle(int3.zero, new float3(0, 0, 1),
                0, 0, 0, 1, Array.Empty<A.PhaseDrainEvidence>(), new[] { partial });
            incomplete.Drain(1);
            Assert.That(incomplete.Unresolved, Is.EqualTo(1));
            Assert.That(incomplete.RgbSplitBits, Is.Zero);
            Assert.That(incomplete.CommittedCanonicalRgb(), Is.Empty);
        }

        private static A.RgbDrainEvidence[] BuildRgb(bool uniform)
        {
            var groups = new A.RgbDrainEvidence[57];
            for (int parent = 0; parent < groups.Length; parent++)
            {
                bool split = !uniform && (parent == 0 || parent == 1 || parent == 8);
                var color = ColorFootprints(split);
                groups[parent] = new A.RgbDrainEvidence(parent, color, color, 0x7fu, 0x7fu);
            }
            return groups;
        }

        private static A.FloatInterval[] ColorFootprints(bool different)
        {
            var rgb = new A.FloatInterval[21];
            for (int child = 0; child < 7; child++)
            for (int channel = 0; channel < 3; channel++)
            {
                float center = different ? (child + 1) / 16f : 0.5f;
                rgb[3 * child + channel] = new A.FloatInterval(center - 1f / 1024f,
                    center + 1f / 1024f);
            }
            return rgb;
        }

        private static uint[] DetailBits(A.ObservationDrainOracle drain)
        {
            var records = drain.CommittedDetail();
            var result = new uint[4 * records.Length];
            for (int i = 0; i < records.Length; i++)
            {
                result[4 * i] = records[i].Key;
                result[4 * i + 1] = unchecked((uint)records[i].Lower);
                result[4 * i + 2] = unchecked((uint)records[i].Upper);
                result[4 * i + 3] = records[i].ParentEpoch;
            }
            return result;
        }

        private static ushort[] ColorBits(A.ObservationDrainOracle drain)
        {
            var groups = drain.CommittedCanonicalRgb();
            var result = new ushort[8 * groups.Length];
            for (int i = 0; i < groups.Length; i++)
            for (int channel = 0; channel < 4; channel++)
            {
                result[8 * i + channel] = groups[i].LowerLinearRgba[channel].value;
                result[8 * i + 4 + channel] = groups[i].UpperLinearRgba[channel].value;
            }
            return result;
        }

        private static Fixture BuildPhaseFixture()
        {
            // Fixed decoded R1 carrier with the mandatory quantization bounds.
            // Direct fine evidence is independent of its prediction: the
            // certified measured arc below does not inherit R1/ancestor error.
            const int petal = 0, site = 5;
            Assert.That(A.TryGetChildPhaseLoop(petal, 0, site, out int level,
                out int3 offset, out int line, out _, out _, out _, out _), Is.True);
            Assert.That(A.Lines[line].Shell, Is.EqualTo(A.Shell.R2Shape));
            var loop = A.EvaluateLoop(level, new A.Long3(offset.x, offset.y, offset.z), line);
            var fixture = new Fixture { Normal = loop.E1,
                Offset = math.dot(loop.E1, loop.Center) - loop.Radius / 32f };
            // These two independent, immutable direct observations lie
            // strictly inside the generated R2 sector. The former arc around
            // q=0 crossed an authorized order cut and is correctly AMBIGUOUS.
            // L1: 31/1024 <= q <= 33/1024; L2: 95/1024 <= q <= 97/1024.
            // Both use u(q)=(-2q/(1+q²),(1-q²)/(1+q²)), with nonzero
            // outward-rounded widths. Neither evidence interval is computed
            // from prediction. Mandatory R1 uncertainty remains unchanged.
            var firstMeasured = MeasuredArc(31, 33, 1024);
            var secondMeasured = MeasuredArc(95, 97, 1024);
            Assert.That(TryCase(fixture, petal, 0, site, true, firstMeasured,
                Array.Empty<A.PhaseRootEvidence>(), Array.Empty<MerkabaFlowerDetailRecord>(),
                Array.Empty<MerkabaFlowerDetailKey>(), out var first, out var firstRecord,
                out var firstRoot), Is.True, "The fixed L1 innovation must be CERTAIN and nonzero.");
            Assert.That(TryCase(fixture, petal, 1, site, true, secondMeasured,
                new[] { firstRoot }, new[] { firstRecord }, new[] { first.Key },
                out var second, out _, out _), Is.True,
                "The fixed L2 innovation must remain CERTAIN after committed L1 synthesis.");
            fixture.Phases = new[] { first, second };
            return fixture;
        }

        private static A.Interval2 MeasuredArc(int first, int last, int denominator)
        {
            // On 0<q<1 both coordinates decrease, so exact rational endpoint
            // values bound the entire observed arc, not just two samples.
            Assert.That(first, Is.GreaterThan(0));
            Assert.That(last, Is.GreaterThan(first).And.LessThan(denominator));
            double d2 = denominator * denominator;
            var xFirst = A.FloatInterval.Enclose(-2.0 * first * denominator / (d2 + first * first));
            var xLast = A.FloatInterval.Enclose(-2.0 * last * denominator / (d2 + last * last));
            var yFirst = A.FloatInterval.Enclose((d2 - first * first) / (d2 + first * first));
            var yLast = A.FloatInterval.Enclose((d2 - last * last) / (d2 + last * last));
            return new A.Interval2(new A.FloatInterval(xLast.Lower, xFirst.Upper),
                new A.FloatInterval(yLast.Lower, yFirst.Upper));
        }

        private static bool TryCase(Fixture fixture, int petal, int context, int site,
            bool plus, A.Interval2 measured, A.PhaseRootEvidence[] ancestors,
            MerkabaFlowerDetailRecord[] records, MerkabaFlowerDetailKey[] keys,
            out A.PhaseDrainEvidence evidence, out MerkabaFlowerDetailRecord record,
            out A.PhaseRootEvidence closed)
        {
            evidence = null; record = default; closed = default;
            if (!A.TryGetChildPhaseLoop(petal, context, site, out int level,
                    out int3 offset, out int line, out int strand, out _, out _, out _)) return false;
            var loop = A.EvaluateLoop(level, new A.Long3(offset.x, offset.y, offset.z), line);
            var roots = A.EvaluateRoots(A.RestrictPlaneToLoop(float3.zero, fixture.Normal,
                fixture.Offset, fixture.NormalError, fixture.OffsetError, loop));
            if (roots.Classification != A.RootClassification.CertainSecant ||
                A.ClassifySector(line, plus ? roots.Plus : roots.Minus, out int sector) !=
                    A.ProofClassification.Certain) return false;
            if (A.PredictChildFromFamily(fixture.Owner, petal, context, site,
                    fixture.Normal, fixture.Offset, fixture.NormalError, fixture.OffsetError,
                    sector, plus, Array.Empty<A.PhaseRootEvidence>(), ancestors, records, keys,
                    7u, out var predicted) != A.ProofClassification.Certain ||
                A.ClassifySector(line, measured, out int measuredSector) !=
                    A.ProofClassification.Certain || measuredSector != sector) return false;
            var endpoint = new A.PhaseRootEvidence(predicted.Symbol, measured, A.ProofClassification.Certain);
            if (A.SealPhaseRelation(endpoint, endpoint, out var observed, out _) !=
                A.ProofClassification.Certain) return false;
            var analysis = A.AnalyzePhaseResidual(predicted, observed);
            if (analysis.Classification != A.PhaseResidualClassification.CertainNonzero) return false;
            var family = A.PhaseFamilies[strand];
            var incidence = A.Strands[strand];
            int path = level == 1 ? (petal == incidence.Petal0 ? family.FinePath0 : family.FinePath1)
                : 4 * (context - 1) + (site == 4 ? 1 : 0);
            var key = MerkabaFlowerDetailKey.Create(level, path, petal, line,
                MerkabaFlowerDetailKind.R2Phase, plus, sector);
            record = MerkabaFlowerDetailRecord.Create(key, analysis.Lower, analysis.Upper, 7u);
            if (A.SynthesizePhaseRecord(predicted, key, record, 7u, out var synthesized) !=
                    A.ProofClassification.Certain ||
                A.CloseSharedPhaseRoot(synthesized, observed, out closed) != A.ProofClassification.Certain)
                return false;
            evidence = new A.PhaseDrainEvidence(petal, context, site, key, endpoint, endpoint,
                context == 0 ? Array.Empty<int>() : new[] { 0 });
            return true;
        }
    }
}
