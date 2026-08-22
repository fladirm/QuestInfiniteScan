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
    /// Exact CPU semantic oracle for the projective geometry readout. Runtime bulk
    /// readout uses the matching generated GPU operator; this class is fixture and
    /// recovery authority, never a live CPU geometry path.
    /// </summary>
    public static class SigmaGeometryReadout
    {
        public static bool TryRead(SigmaS16 state, out SigmaGeometrySample sample)
        {
            try
            {
                long[] geometry = SigmaS16Operators.GeometryReadout(state);
                if (geometry[0] <= 0L)
                {
                    sample = default;
                    return false;
                }
                long x = SigmaNumericDomain.QDiv(geometry[1], geometry[0]);
                long y = SigmaNumericDomain.QDiv(geometry[2], geometry[0]);
                long z = SigmaNumericDomain.QDiv(geometry[3], geometry[0]);
                sample = new SigmaGeometrySample(new Vector3(
                    (float)SigmaNumericDomain.ToDouble(x),
                    (float)SigmaNumericDomain.ToDouble(y),
                    (float)SigmaNumericDomain.ToDouble(z)), geometry[0]);
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
