using System;
using UnityEngine;

namespace Genesis.RoomScan.SigmaPrism
{
    public readonly struct SigmaGeometrySample
    {
        internal SigmaGeometrySample(Vector3 position, long informationMassRaw)
        {
            Position = position;
            InformationMassRaw = informationMassRaw;
        }

        public Vector3 Position { get; }
        public long InformationMassRaw { get; }
    }

    /// <summary>
    /// Exact projective readout point before disposable float presentation.
    /// N5 query-support summaries use these packed Q16.48 coordinates so a
    /// resident and an encoded/nonresident spelling produce identical bounds.
    /// </summary>
    internal readonly struct SigmaExactGeometrySample
    {
        internal SigmaExactGeometrySample(long xRaw, long yRaw, long zRaw,
            long informationMassRaw)
        {
            XRaw = xRaw;
            YRaw = yRaw;
            ZRaw = zRaw;
            InformationMassRaw = informationMassRaw;
        }

        internal long XRaw { get; }
        internal long YRaw { get; }
        internal long ZRaw { get; }
        internal long InformationMassRaw { get; }
    }

    /// <summary>
    /// Exact CPU semantic oracle for the projective geometry readout. Runtime bulk
    /// readout uses the matching generated GPU operator; this class is fixture and
    /// recovery authority, never a live CPU geometry path.
    /// </summary>
    public static class SigmaGeometryReadout
    {
        public static bool TryRead(SigmaS16 state, out SigmaGeometrySample sample)
        {
            if (!TryReadExact(state, out SigmaExactGeometrySample exact))
            {
                sample = default;
                return false;
            }
            sample = new SigmaGeometrySample(new Vector3(
                (float)SigmaNumericDomain.ToDouble(exact.XRaw),
                (float)SigmaNumericDomain.ToDouble(exact.YRaw),
                (float)SigmaNumericDomain.ToDouble(exact.ZRaw)),
                exact.InformationMassRaw);
            return true;
        }

        internal static bool TryReadExact(SigmaS16 state,
            out SigmaExactGeometrySample sample)
        {
            try
            {
                byte[] rows = SigmaGeneratedAlgebra.GeometryRows;
                long mass = SigmaS16Operators.HadamardRow(state, rows[0]);
                if (mass <= 0L)
                {
                    sample = default;
                    return false;
                }
                long x = SigmaNumericDomain.QDiv(
                    SigmaS16Operators.HadamardRow(state, rows[1]), mass);
                long y = SigmaNumericDomain.QDiv(
                    SigmaS16Operators.HadamardRow(state, rows[2]), mass);
                long z = SigmaNumericDomain.QDiv(
                    SigmaS16Operators.HadamardRow(state, rows[3]), mass);
                sample = new SigmaExactGeometrySample(x, y, z, mass);
                return true;
            }
            catch (OverflowException)
            {
                sample = default;
                return false;
            }
            catch (DivideByZeroException)
            {
                sample = default;
                return false;
            }
        }

        /// <summary>
        /// Deterministic fixture/bootstrap lift from projective Q16.48 world
        /// coordinates. Scanner mutation does not call this convenience method;
        /// S4-04 constructs complete admissible S16 cells before committing state.
        /// </summary>
        public static SigmaS16 LiftFixture(long informationMassRaw,
            long xRaw, long yRaw, long zRaw)
        {
            if (informationMassRaw <= 0L)
                throw new ArgumentOutOfRangeException(nameof(informationMassRaw));
            var operatorCoordinates = new long[SigmaS16.LaneCount];
            byte[] rows = SigmaGeneratedAlgebra.GeometryRows;
            operatorCoordinates[rows[0]] = informationMassRaw;
            operatorCoordinates[rows[1]] = SigmaNumericDomain.QMul(
                informationMassRaw, xRaw);
            operatorCoordinates[rows[2]] = SigmaNumericDomain.QMul(
                informationMassRaw, yRaw);
            operatorCoordinates[rows[3]] = SigmaNumericDomain.QMul(
                informationMassRaw, zRaw);
            SigmaS16 transformed = SigmaS16Operators.HadamardBT(
                SigmaS16.FromArray(operatorCoordinates));
            var lanes = new long[SigmaS16.LaneCount];
            for (int lane = 0; lane < lanes.Length; ++lane)
                lanes[lane] = SigmaNumericDomain.QShiftRight(transformed[lane], 4);
            SigmaS16 lifted = SigmaS16.FromArray(lanes);
            if (!TryRead(lifted, out _))
                throw new InvalidOperationException(
                    "Fixture geometry lift did not produce supported readout.");
            return lifted;
        }
    }
}
