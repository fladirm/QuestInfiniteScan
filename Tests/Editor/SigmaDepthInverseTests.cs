using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaDepthInverseTests
    {
        [Test]
        public void FirstHitSectorsHaveExactCausalPartition()
        {
            SigmaQ48Interval prediction = Interval(0.99, 1.01);

            Assert.That(SigmaDepthInverse.ClassifyFirstHit(
                Interval(0.49, 0.51), prediction),
                Is.EqualTo(SigmaFirstHitSector.NoConstraint),
                "a nearer first hit belongs to another carrier preimage");
            Assert.That(SigmaDepthInverse.ClassifyFirstHit(
                Interval(0.995, 1.005), prediction),
                Is.EqualTo(SigmaFirstHitSector.Hit));
            Assert.That(SigmaDepthInverse.ClassifyFirstHit(
                Interval(1.49, 1.51), prediction),
                Is.EqualTo(SigmaFirstHitSector.PreHitExclusion),
                "predicted contact lies in measured pre-hit free path");

            SigmaS16 state = SupportedState(8, 0.0, 0.0, 1.0);
            SigmaDepthCommitResult postHit = SigmaDepthInverse.MeetAndCommitSupported(
                state, new[]
                {
                    Cell(0.0, 0.0, 1.5, 0.001,
                        SigmaDepthSourceClass.DepthLeft,
                        SigmaFirstHitSector.PreHitExclusion)
                });
            Assert.That(postHit.Accepted, Is.False);
            Assert.That(postHit.Changed, Is.False);
            Assert.That(postHit.State, Is.EqualTo(state),
                "an exclusion is retained as evidence and never directly mutates state");
        }

        [Test]
        public void ExactMeetIsLeftRightOrderInvariantAndWeakEvidenceCannotPull()
        {
            SigmaS16 state = SupportedState(8, 0.0, 0.0, 1.0);
            SigmaDepthAdmissibleCell left = Cell(0.0010, 0.0, 1.0, 0.0005,
                SigmaDepthSourceClass.DepthLeft);
            SigmaDepthAdmissibleCell right = Cell(0.0009, 0.0, 1.0, 0.0006,
                SigmaDepthSourceClass.DepthRight);

            SigmaDepthCommitResult lr = SigmaDepthInverse.MeetAndCommitSupported(
                state, new[] { left, right });
            SigmaDepthCommitResult rl = SigmaDepthInverse.MeetAndCommitSupported(
                state, new[] { right, left });
            Assert.That(lr.Accepted, Is.True);
            Assert.That(lr.Changed, Is.True);
            Assert.That(rl.State, Is.EqualTo(lr.State));
            Assert.That(rl.Conflict.AxisMask, Is.EqualTo(lr.Conflict.AxisMask));
            Assert.That(rl.Conflict.InclusiveSourceMask,
                Is.EqualTo(lr.Conflict.InclusiveSourceMask));

            SigmaDepthAdmissibleCell broadFarObservation = Cell(0.0008, 0.0, 1.0,
                0.050, SigmaDepthSourceClass.DepthLeft);
            SigmaDepthCommitResult confirmed = SigmaDepthInverse.MeetAndCommitSupported(
                lr.State, new[] { broadFarObservation });
            Assert.That(confirmed.Accepted, Is.True);
            Assert.That(confirmed.Changed, Is.False,
                "a broad compatible cell confirms but cannot pull a compressed film");
            Assert.That(confirmed.State, Is.EqualTo(lr.State));
        }

        [Test]
        public void EmptyMeetPreservesExactGapProvenanceAndCanonicalBytes()
        {
            SigmaS16 state = SupportedState(8, 0.0, 0.0, 1.0);
            SigmaDepthAdmissibleCell left = Cell(-0.00075, 0.0, 1.0, 0.0002,
                SigmaDepthSourceClass.DepthLeft);
            SigmaDepthAdmissibleCell right = Cell(0.00075, 0.0, 1.0, 0.0002,
                SigmaDepthSourceClass.DepthRight);

            SigmaDepthCommitResult result = SigmaDepthInverse.MeetAndCommitSupported(
                state, new[] { right, left });
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Changed, Is.False);
            Assert.That(result.State, Is.EqualTo(state));
            Assert.That(result.Conflict.IsEmptyMeet, Is.True);
            Assert.That(result.Conflict.AxisMask & 1u, Is.Not.Zero);
            Assert.That(result.Conflict.Gaps[0], Is.GreaterThan(0L));
            Assert.That(result.Conflict.LowerSources[0],
                Is.EqualTo(SigmaDepthSourceClass.DepthRight));
            Assert.That(result.Conflict.UpperSources[0],
                Is.EqualTo(SigmaDepthSourceClass.DepthLeft));
        }

        [Test]
        public void IndependentNarrowSupportRaisesResistanceWithoutMovingReadout()
        {
            long initialMass = SigmaDepthInverse.DefaultContactMassMinRaw;
            SigmaS16 state = SigmaGeometryReadout.LiftFixture(initialMass,
                Q(0.25), Q(-0.125), Q(1.5));
            SigmaDepthAdmissibleCell left = Cell(0.25, -0.125, 1.5, 0.0005,
                SigmaDepthSourceClass.DepthLeft);
            SigmaDepthAdmissibleCell right = Cell(0.25, -0.125, 1.5, 0.0005,
                SigmaDepthSourceClass.DepthRight);

            SigmaDepthCommitResult strengthened =
                SigmaDepthInverse.MeetAndCommitSupported(state,
                    new[] { left, right });
            Assert.That(strengthened.Accepted, Is.True);
            Assert.That(strengthened.Changed, Is.True);
            Assert.That(SigmaGeometryReadout.TryRead(strengthened.State,
                out SigmaGeometrySample readout), Is.True);
            Assert.That(readout.InformationMassRaw, Is.GreaterThan(initialMass));
            long[] geometry = SigmaS16Operators.GeometryReadout(
                strengthened.State);
            Assert.That(SigmaNumericDomain.QDiv(geometry[1], geometry[0]),
                Is.EqualTo(Q(0.25)).Within(1L));
            Assert.That(SigmaNumericDomain.QDiv(geometry[2], geometry[0]),
                Is.EqualTo(Q(-0.125)).Within(1L));
            Assert.That(SigmaNumericDomain.QDiv(geometry[3], geometry[0]),
                Is.EqualTo(Q(1.5)).Within(1L));

            SigmaDepthCommitResult replay =
                SigmaDepthInverse.MeetAndCommitSupported(strengthened.State,
                    new[] { left });
            Assert.That(replay.Accepted, Is.True);
            Assert.That(replay.Changed, Is.False,
                "one repeated independence class cannot harden or move the state");
            Assert.That(replay.State, Is.EqualTo(strengthened.State));

            SigmaDepthCommitResult repeatedPair =
                SigmaDepthInverse.MeetAndCommitSupported(strengthened.State,
                    new[] { right, left });
            Assert.That(repeatedPair.Accepted, Is.True);
            Assert.That(repeatedPair.Changed, Is.False,
                "replaying the same independent stereo cells cannot add count-based hardness");
            Assert.That(repeatedPair.State, Is.EqualTo(strengthened.State));
        }
        private static SigmaS16 SupportedState(long mass, double x, double y,
            double z) => SigmaGeometryReadout.LiftFixture(
                SigmaNumericDomain.FromInteger(mass), Q(x), Q(y), Q(z));

        private static SigmaDepthAdmissibleCell Cell(double x, double y,
            double z, double radius, SigmaDepthSourceClass source,
            SigmaFirstHitSector sector = SigmaFirstHitSector.Hit) => new(
                Interval(x - radius, x + radius),
                Interval(y - radius, y + radius),
                Interval(z - radius, z + radius), source,
                (uint)source + 100u, sector);

        private static SigmaQ48Interval Interval(double lower, double upper) =>
            new(Q(lower), Q(upper));

        private static long Q(double value) => SigmaNumericDomain.Quantize(value);
    }
}
