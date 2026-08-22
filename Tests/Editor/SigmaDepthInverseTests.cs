using System;
using System.Runtime.InteropServices;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaDepthInverseTests
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct UInt2
        {
            public uint X;
            public uint Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UInt4
        {
            public uint X;
            public uint Y;
            public uint Z;
            public uint W;
        }

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

        [Test]
        public void GpuDepthMeetMatchesCpuProjectiveCommitBitForBit()
        {
            ComputeShader shader = Resources.Load<ComputeShader>(
                "SigmaPrism/SigmaInverse");
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel("EvaluateDepthMeetFixture");

            SigmaS16 state = SupportedState(8, 0.0, 0.0, 1.0);
            SigmaDepthAdmissibleCell left = Cell(0.0010, 0.0, 1.0, 0.0005,
                SigmaDepthSourceClass.DepthLeft);
            SigmaDepthAdmissibleCell right = Cell(0.0009, 0.0, 1.0, 0.0006,
                SigmaDepthSourceClass.DepthRight);
            SigmaDepthCommitResult expected = SigmaDepthInverse.MeetAndCommitSupported(
                state, new[] { left, right });
            Assert.That(expected.Accepted && expected.Changed, Is.True);

            UInt2[] packedState = Pack(state);
            UInt2[] bounds = PackCells(left, right);
            UInt4[] metadata =
            {
                PackCellMetadata(left),
                PackCellMetadata(right)
            };
            UInt2[] calibration = BuildFixtureCalibration();
            var output = new UInt2[SigmaS16.LaneCount];
            var result = new UInt4[3];

            using var stateBuffer = Buffer(packedState.Length,
                Marshal.SizeOf<UInt2>(), packedState);
            using var boundsBuffer = Buffer(bounds.Length,
                Marshal.SizeOf<UInt2>(), bounds);
            using var metadataBuffer = Buffer(metadata.Length,
                Marshal.SizeOf<UInt4>(), metadata);
            using var calibrationBuffer = Buffer(calibration.Length,
                Marshal.SizeOf<UInt2>(), calibration);
            using var outputBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, output.Length,
                Marshal.SizeOf<UInt2>());
            using var resultBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, result.Length,
                Marshal.SizeOf<UInt4>());

            shader.SetBuffer(kernel, "_DepthCalibrationQ48", calibrationBuffer);
            shader.SetBuffer(kernel, "_FixtureState", stateBuffer);
            shader.SetBuffer(kernel, "_FixtureCellBounds", boundsBuffer);
            shader.SetBuffer(kernel, "_FixtureCellMeta", metadataBuffer);
            shader.SetBuffer(kernel, "_FixtureStateOut", outputBuffer);
            shader.SetBuffer(kernel, "_FixtureResult", resultBuffer);
            shader.Dispatch(kernel, 1, 1, 1);
            outputBuffer.GetData(output);
            resultBuffer.GetData(result);

            Assert.That(result[0].X & 3u, Is.EqualTo(3u),
                $"accepted + changed proposal bits; status=0x{result[0].X:x8}, " +
                $"conflict=0x{result[0].Y:x8}, valid={result[2].Z}");
            Assert.That(result[0].Y, Is.Zero);
            Assert.That(result[2].Z, Is.EqualTo(1u),
                "GPU revalidation must pass before the candidate is writable");
            CollectionAssert.AreEqual(expected.State.ToArray(), Unpack(output));
        }

        [Test]
        public void NullPromotionRequiresIndependentNonEmptyStereoMeet()
        {
            ComputeShader shader = Resources.Load<ComputeShader>(
                "SigmaPrism/SigmaInverse");
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel("EvaluateNullDepthMeetFixture");
            SigmaDepthAdmissibleCell left = Cell(0.2, -0.1, 1.25, 0.002,
                SigmaDepthSourceClass.DepthLeft);
            SigmaDepthAdmissibleCell right = Cell(0.201, -0.099, 1.249, 0.002,
                SigmaDepthSourceClass.DepthRight);
            UInt2[] bounds = PackCells(left, right);
            UInt4[] metadata = { PackCellMetadata(left), PackCellMetadata(right) };
            UInt2[] calibration = BuildFixtureCalibration();
            var output = new UInt2[SigmaS16.LaneCount];
            var result = new UInt4[3];

            using var boundsBuffer = Buffer(bounds.Length,
                Marshal.SizeOf<UInt2>(), bounds);
            using var metadataBuffer = Buffer(metadata.Length,
                Marshal.SizeOf<UInt4>(), metadata);
            using var calibrationBuffer = Buffer(calibration.Length,
                Marshal.SizeOf<UInt2>(), calibration);
            using var outputBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, output.Length,
                Marshal.SizeOf<UInt2>());
            using var resultBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, result.Length,
                Marshal.SizeOf<UInt4>());
            shader.SetBuffer(kernel, "_DepthCalibrationQ48", calibrationBuffer);
            shader.SetBuffer(kernel, "_FixtureCellBounds", boundsBuffer);
            shader.SetBuffer(kernel, "_FixtureCellMeta", metadataBuffer);
            shader.SetBuffer(kernel, "_FixtureStateOut", outputBuffer);
            shader.SetBuffer(kernel, "_FixtureResult", resultBuffer);

            shader.Dispatch(kernel, 1, 1, 1);
            resultBuffer.GetData(result);
            Assert.That(result[0].X, Is.EqualTo(1u));
            Assert.That(((ulong)result[0].W << 32) | result[0].Z,
                Is.GreaterThan(0UL));

            metadata[1].Y = metadata[0].Y;
            metadataBuffer.SetData(metadata);
            shader.Dispatch(kernel, 1, 1, 1);
            resultBuffer.GetData(result);
            Assert.That(result[0].X, Is.Zero,
                "reusing one independence key cannot promote latent carrier");

            metadata[1] = PackCellMetadata(right);
            metadataBuffer.SetData(metadata);
            SigmaDepthAdmissibleCell disjoint = Cell(0.3, -0.1, 1.25, 0.002,
                SigmaDepthSourceClass.DepthRight);
            bounds = PackCells(left, disjoint);
            boundsBuffer.SetData(bounds);
            shader.Dispatch(kernel, 1, 1, 1);
            resultBuffer.GetData(result);
            Assert.That(result[0].X, Is.Zero,
                "an empty stereo meet remains unresolved and cannot promote");
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

        private static UInt2[] Pack(SigmaS16 value)
        {
            var result = new UInt2[SigmaS16.LaneCount];
            for (int lane = 0; lane < result.Length; ++lane)
                result[lane] = Pack(value[lane]);
            return result;
        }

        private static UInt2[] PackCells(params SigmaDepthAdmissibleCell[] cells)
        {
            var result = new UInt2[cells.Length * 6];
            for (int source = 0; source < cells.Length; ++source)
            {
                for (int axis = 0; axis < 3; ++axis)
                {
                    result[source * 6 + axis * 2] = Pack(cells[source][axis].Lower);
                    result[source * 6 + axis * 2 + 1] =
                        Pack(cells[source][axis].Upper);
                }
            }
            return result;
        }

        private static UInt4 PackCellMetadata(SigmaDepthAdmissibleCell cell) => new()
        {
            X = (uint)cell.Source,
            Y = cell.IndependenceKey,
            Z = (uint)cell.Sector,
            W = 1u
        };

        private static UInt2[] BuildFixtureCalibration()
        {
            const int stride = 36;
            var result = new UInt2[stride * 2];
            for (int eye = 0; eye < 2; ++eye)
            {
                int offset = eye * stride;
                result[offset + 31] = Pack(Q(0.001));
                result[offset + 32] = Pack(Q(0.050));
                result[offset + 33] = Pack(SigmaNumericDomain.FromRatio(1, 64));
            }
            return result;
        }

        private static GraphicsBuffer Buffer<T>(int count, int stride, T[] data)
            where T : struct
        {
            var result = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                count, stride);
            result.SetData(data);
            return result;
        }

        private static UInt2 Pack(long raw) => new()
        {
            X = unchecked((uint)raw),
            Y = unchecked((uint)(raw >> 32))
        };

        private static long[] Unpack(UInt2[] values)
        {
            var result = new long[values.Length];
            for (int index = 0; index < values.Length; ++index)
                result[index] = unchecked((long)(((ulong)values[index].Y << 32) |
                    values[index].X));
            return result;
        }
    }
}
