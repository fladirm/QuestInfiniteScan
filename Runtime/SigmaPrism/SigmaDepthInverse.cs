using System;
using System.Collections.Generic;

namespace Genesis.RoomScan.SigmaPrism
{
    public enum SigmaDepthSourceClass : byte
    {
        Prior = 0,
        DepthLeft = 1,
        DepthRight = 2,
    }

    public enum SigmaFirstHitSector : byte
    {
        NoConstraint = 0,
        Hit = 1,
        PreHitExclusion = 2,
    }

    /// <summary>
    /// Sparse depth-only view of a complete projective S16 source cell. Depth fixes
    /// y[g0]=ONE and bounds only the three generated geometry rows; every hidden
    /// operator coordinate is implicitly unconstrained. This is an exact compact
    /// encoding of section 12.2, not a second geometry state.
    /// </summary>
    public readonly struct SigmaDepthAdmissibleCell
    {
        public SigmaDepthAdmissibleCell(SigmaQ48Interval worldX,
            SigmaQ48Interval worldY, SigmaQ48Interval worldZ,
            SigmaDepthSourceClass source, uint independenceKey,
            SigmaFirstHitSector sector)
        {
            WorldX = worldX;
            WorldY = worldY;
            WorldZ = worldZ;
            Source = source;
            IndependenceKey = independenceKey;
            Sector = sector;
        }

        public SigmaQ48Interval WorldX { get; }
        public SigmaQ48Interval WorldY { get; }
        public SigmaQ48Interval WorldZ { get; }
        public SigmaDepthSourceClass Source { get; }
        public uint IndependenceKey { get; }
        public SigmaFirstHitSector Sector { get; }

        public SigmaQ48Interval this[int axis] => axis switch
        {
            0 => WorldX,
            1 => WorldY,
            2 => WorldZ,
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };
    }

    public readonly struct SigmaDepthConflict
    {
        internal SigmaDepthConflict(uint axisMask, long[] gaps,
            SigmaDepthSourceClass[] lowerSources,
            SigmaDepthSourceClass[] upperSources, uint inclusiveSourceMask,
            uint sectorMask)
        {
            AxisMask = axisMask;
            Gaps = gaps;
            LowerSources = lowerSources;
            UpperSources = upperSources;
            InclusiveSourceMask = inclusiveSourceMask;
            SectorMask = sectorMask;
        }

        public uint AxisMask { get; }
        public IReadOnlyList<long> Gaps { get; }
        public IReadOnlyList<SigmaDepthSourceClass> LowerSources { get; }
        public IReadOnlyList<SigmaDepthSourceClass> UpperSources { get; }
        public uint InclusiveSourceMask { get; }
        public uint SectorMask { get; }
        public bool IsEmptyMeet => AxisMask != 0u;
    }

    public readonly struct SigmaDepthCommitResult
    {
        internal SigmaDepthCommitResult(SigmaS16 state, bool accepted,
            bool changed, SigmaDepthConflict conflict)
        {
            State = state;
            Accepted = accepted;
            Changed = changed;
            Conflict = conflict;
        }

        public SigmaS16 State { get; }
        public bool Accepted { get; }
        public bool Changed { get; }
        public SigmaDepthConflict Conflict { get; }
    }

    /// <summary>
    /// Scalar semantic oracle for S4-04. Live pixels execute the equivalent HLSL
    /// plan; this reference exists for exact parity/recovery fixtures only.
    /// </summary>
    public static class SigmaDepthInverse
    {
        public static readonly long DefaultPriorFloorRaw =
            SigmaNumericDomain.FromRatio(1, 1000); // one millimetre
        public static readonly long DefaultPriorCeilingRaw =
            SigmaNumericDomain.FromRatio(1, 20); // fifty millimetres
        public static readonly long DefaultContactMassMinRaw =
            SigmaNumericDomain.FromRatio(1, 64);

        public static SigmaFirstHitSector ClassifyFirstHit(
            SigmaQ48Interval measuredRange, SigmaQ48Interval predictedRange)
        {
            if (measuredRange.IsEmpty)
                return SigmaFirstHitSector.NoConstraint;
            if (predictedRange.IsEmpty)
                return SigmaFirstHitSector.Hit;
            if (measuredRange.Upper < predictedRange.Lower)
                return SigmaFirstHitSector.NoConstraint;
            if (predictedRange.Upper < measuredRange.Lower)
                return SigmaFirstHitSector.PreHitExclusion;
            return SigmaFirstHitSector.Hit;
        }

        public static long PriorHalfWidth(long informationMassRaw,
            long floorRaw = 0L, long ceilingRaw = 0L)
        {
            if (informationMassRaw <= 0L)
                throw new ArgumentOutOfRangeException(nameof(informationMassRaw));
            floorRaw = floorRaw > 0L ? floorRaw : DefaultPriorFloorRaw;
            ceilingRaw = ceilingRaw > 0L ? ceilingRaw : DefaultPriorCeilingRaw;
            // A dyadic monotone resistance ladder is exact in Q16.48 and avoids
            // manufacturing precision from a costly approximate reciprocal.
            // Every halving of supported information mass doubles the admissible
            // width until the calibrated ceiling is reached.
            long width = floorRaw;
            long threshold = SigmaNumericDomain.One;
            for (int step = 0; step < 16 && informationMassRaw < threshold &&
                width < ceilingRaw; ++step)
            {
                threshold = SigmaNumericDomain.QShiftRight(threshold, 1);
                width = Math.Min(ceilingRaw,
                    SigmaNumericDomain.QShiftLeft(width, 1));
            }
            return width;
        }

        public static long InformationMassForWidth(long maximumWidthRaw,
            long floorRaw = 0L, long massFloorRaw = 0L)
        {
            if (maximumWidthRaw < 0L)
                throw new ArgumentOutOfRangeException(nameof(maximumWidthRaw));
            floorRaw = floorRaw > 0L ? floorRaw : DefaultPriorFloorRaw;
            massFloorRaw = massFloorRaw > 0L ? massFloorRaw :
                DefaultContactMassMinRaw;
            long mass = SigmaNumericDomain.One;
            long threshold = floorRaw;
            for (int step = 0; step < 16 && maximumWidthRaw > threshold &&
                mass > massFloorRaw; ++step)
            {
                threshold = SigmaNumericDomain.QShiftLeft(threshold, 1);
                mass = SigmaNumericDomain.QShiftRight(mass, 1);
            }
            return Math.Max(massFloorRaw, Math.Min(mass,
                SigmaNumericDomain.One));
        }

        public static SigmaDepthCommitResult MeetAndCommitSupported(
            SigmaS16 current, IReadOnlyList<SigmaDepthAdmissibleCell> cells,
            long priorFloorRaw = 0L, long priorCeilingRaw = 0L)
        {
            if (cells == null)
                throw new ArgumentNullException(nameof(cells));

            long[] transformed = SigmaS16Operators.HadamardB(current).ToArray();
            byte[] geometryRows = SigmaGeneratedAlgebra.GeometryRows;
            long mass = transformed[geometryRows[0]];
            if (mass <= 0L)
                throw new InvalidOperationException(
                    "Supported inverse commit requires positive geometry mass.");

            var currentCoordinates = new long[3];
            var lower = new long[3];
            var upper = new long[3];
            var lowerSource = new SigmaDepthSourceClass[3];
            var upperSource = new SigmaDepthSourceClass[3];
            long priorHalfWidth = PriorHalfWidth(mass, priorFloorRaw,
                priorCeilingRaw);
            for (int axis = 0; axis < 3; ++axis)
            {
                long coordinate = SigmaNumericDomain.QDiv(
                    transformed[geometryRows[axis + 1]], mass);
                currentCoordinates[axis] = coordinate;
                lower[axis] = SigmaNumericDomain.QSub(coordinate, priorHalfWidth);
                upper[axis] = SigmaNumericDomain.QAdd(coordinate, priorHalfWidth);
                lowerSource[axis] = SigmaDepthSourceClass.Prior;
                upperSource[axis] = SigmaDepthSourceClass.Prior;
            }

            uint inclusiveMask = 0u;
            uint sectorMask = 0u;
            uint firstIndependenceKey = 0u;
            uint secondIndependenceKey = 0u;
            for (int index = 0; index < cells.Count; ++index)
            {
                SigmaDepthAdmissibleCell cell = cells[index];
                int source = (int)cell.Source;
                sectorMask |= (uint)cell.Sector << (source * 2);
                if (cell.Sector != SigmaFirstHitSector.Hit)
                    continue;
                inclusiveMask |= 1u << source;
                if (cell.IndependenceKey != 0u)
                {
                    if (firstIndependenceKey == 0u)
                        firstIndependenceKey = cell.IndependenceKey;
                    else if (cell.IndependenceKey != firstIndependenceKey)
                        secondIndependenceKey = cell.IndependenceKey;
                }
                for (int axis = 0; axis < 3; ++axis)
                    MeetAxis(cell[axis], cell.Source, ref lower[axis],
                        ref upper[axis], ref lowerSource[axis],
                        ref upperSource[axis]);
            }

            if (inclusiveMask == 0u)
                return new SigmaDepthCommitResult(current, false, false,
                    new SigmaDepthConflict(0u, new long[3], lowerSource,
                        upperSource, 0u, sectorMask));

            uint conflictMask = 0u;
            var gaps = new long[3];
            for (int axis = 0; axis < 3; ++axis)
            {
                if (lower[axis] <= upper[axis])
                    continue;
                conflictMask |= 1u << axis;
                gaps[axis] = SigmaNumericDomain.QSub(lower[axis], upper[axis]);
            }
            if (conflictMask != 0u)
            {
                return new SigmaDepthCommitResult(current, false, false,
                    new SigmaDepthConflict(conflictMask, gaps, lowerSource,
                        upperSource, inclusiveMask, sectorMask));
            }

            long maximumWidth = 0L;
            for (int axis = 0; axis < 3; ++axis)
                maximumWidth = Math.Max(maximumWidth,
                    SigmaNumericDomain.QSub(upper[axis], lower[axis]));
            long targetMass = mass;
            if (secondIndependenceKey != 0u)
                targetMass = Math.Max(mass,
                    InformationMassForWidth(maximumWidth, priorFloorRaw));

            bool changed = targetMass > mass;
            for (int axis = 0; axis < 3; ++axis)
            {
                long accepted = SigmaNumericDomain.QClamp(currentCoordinates[axis],
                    lower[axis], upper[axis]);
                changed |= accepted != currentCoordinates[axis];
                transformed[geometryRows[axis + 1]] = SigmaNumericDomain.QMul(
                    mass, accepted);
            }
            if (!changed)
            {
                return new SigmaDepthCommitResult(current, true, false,
                    new SigmaDepthConflict(0u, gaps, lowerSource, upperSource,
                        inclusiveMask, sectorMask));
            }

            SigmaS16 inverse = SigmaS16Operators.HadamardBT(
                SigmaS16.FromArray(transformed));
            var candidateLanes = inverse.ToArray();
            for (int lane = 0; lane < candidateLanes.Length; ++lane)
                candidateLanes[lane] = SigmaNumericDomain.QShiftRight(
                    candidateLanes[lane], 4);
            SigmaS16 candidate = SigmaS16.FromArray(candidateLanes);
            if (targetMass > mass)
            {
                long ratio = SigmaNumericDomain.QDiv(targetMass, mass);
                for (int lane = 0; lane < candidateLanes.Length; ++lane)
                    candidateLanes[lane] = SigmaNumericDomain.QMul(
                        candidateLanes[lane], ratio);
                candidate = SigmaS16.FromArray(candidateLanes);
            }

            // Re-evaluate the quantized candidate before it can become authority.
            long[] check = SigmaS16Operators.HadamardB(candidate).ToArray();
            long checkMass = check[geometryRows[0]];
            if (checkMass <= 0L || Math.Abs(checkMass - targetMass) > 1L)
                return FailedCandidate(current, gaps, lowerSource, upperSource,
                    inclusiveMask, sectorMask);
            for (int axis = 0; axis < 3; ++axis)
            {
                long coordinate = SigmaNumericDomain.QDiv(
                    check[geometryRows[axis + 1]], checkMass);
                // One output LSB is the persisted projective normalization allowance.
                if (coordinate < SaturatingSubtract(lower[axis], 1L) ||
                    coordinate > SaturatingAdd(upper[axis], 1L))
                {
                    return FailedCandidate(current, gaps, lowerSource, upperSource,
                        inclusiveMask, sectorMask);
                }
            }

            return new SigmaDepthCommitResult(candidate, true, true,
                new SigmaDepthConflict(0u, gaps, lowerSource, upperSource,
                    inclusiveMask, sectorMask));
        }

        private static void MeetAxis(SigmaQ48Interval incoming,
            SigmaDepthSourceClass source, ref long lower, ref long upper,
            ref SigmaDepthSourceClass lowerSource,
            ref SigmaDepthSourceClass upperSource)
        {
            if (incoming.Lower > lower || incoming.Lower == lower &&
                source < lowerSource)
            {
                lower = incoming.Lower;
                lowerSource = source;
            }
            if (incoming.Upper < upper || incoming.Upper == upper &&
                source < upperSource)
            {
                upper = incoming.Upper;
                upperSource = source;
            }
        }

        private static SigmaDepthCommitResult FailedCandidate(SigmaS16 current,
            long[] gaps, SigmaDepthSourceClass[] lowerSource,
            SigmaDepthSourceClass[] upperSource, uint inclusiveMask,
            uint sectorMask) => new(current, false, false,
                new SigmaDepthConflict(0u, gaps, lowerSource, upperSource,
                    inclusiveMask, sectorMask));

        private static long SaturatingSubtract(long value, long amount) =>
            value < long.MinValue + amount ? long.MinValue : value - amount;

        private static long SaturatingAdd(long value, long amount) =>
            value > long.MaxValue - amount ? long.MaxValue : value + amount;
    }
}
