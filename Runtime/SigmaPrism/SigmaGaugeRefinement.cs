using System;

namespace Genesis.RoomScan.SigmaPrism
{
    public enum SigmaGaugeAxis : byte
    {
        X = 0,
        Y = 1,
    }

    public enum SigmaGaugeDirection : byte
    {
        Positive = 0,
        Negative = 1,
    }

    /// <summary>
    /// One separable local carrier reparameterization.  It doubles one requested
    /// 8-sample interval and translates every retained sample between that interval
    /// and an implicit-null tail.  The transverse coordinate is unchanged, so this
    /// is a genuine 2D map chi(u,v)=(chiX(u),v) or (u,chiY(v)), not a detail field.
    /// </summary>
    public readonly struct SigmaGaugeMap : IEquatable<SigmaGaugeMap>
    {
        public SigmaGaugeMap(int sourceBlockX, int sourceBlockY,
            SigmaGaugeAxis axis, SigmaGaugeDirection direction, int spanBlocks)
        {
            if ((uint)sourceBlockX >= SigmaDecodedPage.BlocksPerAxis)
                throw new ArgumentOutOfRangeException(nameof(sourceBlockX));
            if ((uint)sourceBlockY >= SigmaDecodedPage.BlocksPerAxis)
                throw new ArgumentOutOfRangeException(nameof(sourceBlockY));
            if (spanBlocks < SigmaGaugeRefinement.RequiredNullBands + 1 ||
                spanBlocks > SigmaDecodedPage.BlocksPerAxis)
                throw new ArgumentOutOfRangeException(nameof(spanBlocks));
            int sourceAxisBlock = axis == SigmaGaugeAxis.X
                ? sourceBlockX : sourceBlockY;
            int firstBlock = direction == SigmaGaugeDirection.Negative
                ? sourceAxisBlock - spanBlocks + 1 : sourceAxisBlock;
            if (firstBlock < 0 || firstBlock + spanBlocks >
                SigmaDecodedPage.BlocksPerAxis)
                throw new ArgumentOutOfRangeException(nameof(spanBlocks),
                    "Gauge insertion must be resident in one execution page.");

            SourceBlockX = sourceBlockX;
            SourceBlockY = sourceBlockY;
            Axis = axis;
            Direction = direction;
            SpanBlocks = spanBlocks;
        }

        public int SourceBlockX { get; }
        public int SourceBlockY { get; }
        public SigmaGaugeAxis Axis { get; }
        public SigmaGaugeDirection Direction { get; }
        public int SpanBlocks { get; }
        public bool Negative => Direction == SigmaGaugeDirection.Negative;
        public int SourceAxisBlock => Axis == SigmaGaugeAxis.X
            ? SourceBlockX : SourceBlockY;
        public int RegionBlock => Negative
            ? SourceAxisBlock - SpanBlocks + 1 : SourceAxisBlock;
        public int RegionSample => RegionBlock * SigmaDecodedPage.BlockSize;
        public int RegionLength => SpanBlocks * SigmaDecodedPage.BlockSize;
        public int RetainedLength =>
            (SpanBlocks - SigmaGaugeRefinement.RequiredNullBands) *
            SigmaDecodedPage.BlockSize;

        public bool Equals(SigmaGaugeMap other) =>
            SourceBlockX == other.SourceBlockX &&
            SourceBlockY == other.SourceBlockY && Axis == other.Axis &&
            Direction == other.Direction && SpanBlocks == other.SpanBlocks;
        public override bool Equals(object obj) =>
            obj is SigmaGaugeMap other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(SourceBlockX,
            SourceBlockY, Axis, Direction, SpanBlocks);
    }

    /// <summary>
    /// Exact CPU semantic oracle for S4-07.  The live implementation is GPU-only.
    /// Direct retained samples are transported bit-for-bit; newly exposed samples
    /// are exact nearest-even pullbacks of the same piecewise-linear Psi.  The
    /// outer null tail is implicit and supplies the otherwise unstored part of the
    /// global bijection.
    /// </summary>
    public static class SigmaGaugeRefinement
    {
        public const int InsertedScale = 2;
        public const int RequiredNullBands = 2;

        public static SigmaS16[] Apply(SigmaS16[] page, SigmaGaugeMap map,
            Func<SigmaGaugeAxis, int, int, SigmaTopologyClass>
                transitionClass = null)
        {
            ValidatePage(page);
            if (!TerminalBandsAreNull(page, map))
                throw new InvalidOperationException(
                    "Gauge stretching requires two exact implicit-null tail bands.");
            var output = (SigmaS16[])page.Clone();
            for (int transverse = 0; transverse < SigmaDecodedPage.PageSize;
                ++transverse)
            {
                for (int local = 0; local < map.RegionLength; ++local)
                {
                    int targetAxis = map.RegionSample + local;
                    AxisPreimage(targetAxis, map, out int oldAxisTimesTwo);
                    int xTimesTwo = map.Axis == SigmaGaugeAxis.X
                        ? oldAxisTimesTwo : transverse * 2;
                    int yTimesTwo = map.Axis == SigmaGaugeAxis.Y
                        ? oldAxisTimesTwo : transverse * 2;
                    int x = map.Axis == SigmaGaugeAxis.X
                        ? targetAxis : transverse;
                    int y = map.Axis == SigmaGaugeAxis.Y
                        ? targetAxis : transverse;
                    output[y * SigmaDecodedPage.PageSize + x] =
                        SampleHalfGrid(page, xTimesTwo, yTimesTwo,
                            transitionClass);
                }
            }
            return output;
        }

        public static SigmaS16[] ApplyInverse(SigmaS16[] transformed,
            SigmaGaugeMap map)
        {
            ValidatePage(transformed);
            var output = (SigmaS16[])transformed.Clone();
            SigmaS16 latent = SigmaS16Operators.NullState;
            for (int transverse = 0; transverse < SigmaDecodedPage.PageSize;
                ++transverse)
            {
                for (int local = 0; local < map.RegionLength; ++local)
                    SetAxis(output, map, map.RegionSample + local, transverse,
                        latent);
                for (int oriented = 0; oriented < map.RetainedLength; ++oriented)
                {
                    int oldAxis = OrientedToPage(oriented, map);
                    int targetOriented = MapRetainedSampleAxis(oriented);
                    int targetAxis = OrientedToPage(targetOriented, map);
                    SetAxis(output, map, oldAxis, transverse,
                        GetAxis(transformed, map, targetAxis, transverse));
                }
            }
            return output;
        }

        public static bool TerminalBandsAreNull(SigmaS16[] page,
            SigmaGaugeMap map)
        {
            ValidatePage(page);
            SigmaS16 latent = SigmaS16Operators.NullState;
            int terminalStart = map.RetainedLength;
            for (int transverse = 0; transverse < SigmaDecodedPage.PageSize;
                ++transverse)
            {
                for (int oriented = terminalStart; oriented < map.RegionLength;
                    ++oriented)
                {
                    int axis = OrientedToPage(oriented, map);
                    if (!GetAxis(page, map, axis, transverse).Equals(latent))
                        return false;
                }
            }
            return true;
        }

        public static bool HasProjectiveReproductionDemand(
            SigmaS16[] block, int operatorCoordinate, long admissibleWidthRaw,
            bool independentlyAccepted, out long maximumErrorRaw)
        {
            if (block == null || block.Length !=
                SigmaDecodedPage.SamplesPerBlock)
                throw new ArgumentException("Gauge demand requires one 8x8 block.",
                    nameof(block));
            if ((uint)operatorCoordinate >= SigmaS16.LaneCount)
                throw new ArgumentOutOfRangeException(nameof(operatorCoordinate));
            if (admissibleWidthRaw < 0L)
                throw new ArgumentOutOfRangeException(nameof(admissibleWidthRaw));
            maximumErrorRaw = 0L;
            if (!independentlyAccepted)
                return false;

            var projected = new long[block.Length];
            for (int sample = 0; sample < block.Length; ++sample)
                projected[sample] = ProjectiveCoordinate(block[sample],
                    operatorCoordinate);
            for (int y = 0; y < SigmaDecodedPage.BlockSize; ++y)
            {
                for (int x = 1; x + 1 < SigmaDecodedPage.BlockSize; ++x)
                    maximumErrorRaw = Math.Max(maximumErrorRaw,
                        MidpointError(projected[y * 8 + x - 1],
                            projected[y * 8 + x], projected[y * 8 + x + 1]));
            }
            for (int x = 0; x < SigmaDecodedPage.BlockSize; ++x)
            {
                for (int y = 1; y + 1 < SigmaDecodedPage.BlockSize; ++y)
                    maximumErrorRaw = Math.Max(maximumErrorRaw,
                        MidpointError(projected[(y - 1) * 8 + x],
                            projected[y * 8 + x], projected[(y + 1) * 8 + x]));
            }
            return maximumErrorRaw > admissibleWidthRaw;
        }

        internal static int MapRetainedSampleAxis(int orientedSource)
        {
            if (orientedSource < 0)
                throw new ArgumentOutOfRangeException(nameof(orientedSource));
            return orientedSource < SigmaDecodedPage.BlockSize
                ? checked(orientedSource * InsertedScale)
                : checked(orientedSource + SigmaDecodedPage.BlockSize);
        }

        internal static bool TryMapRetainedSample(int sourceSample,
            SigmaGaugeMap map, out int targetSample)
        {
            if ((uint)sourceSample >= SigmaDecodedPage.SampleCount)
                throw new ArgumentOutOfRangeException(nameof(sourceSample));
            int x = sourceSample & (SigmaDecodedPage.PageSize - 1);
            int y = sourceSample >> 6;
            int sourceAxis = map.Axis == SigmaGaugeAxis.X ? x : y;
            int sourceStart = map.SourceAxisBlock * SigmaDecodedPage.BlockSize;
            int oriented = map.Negative
                ? sourceStart + SigmaDecodedPage.BlockSize - 1 - sourceAxis
                : sourceAxis - sourceStart;
            if (oriented < 0 || oriented >= map.RegionLength)
            {
                targetSample = sourceSample;
                return true;
            }
            if (oriented >= map.RetainedLength)
            {
                targetSample = -1;
                return false;
            }
            int targetAxis = OrientedToPage(
                MapRetainedSampleAxis(oriented), map);
            int targetX = map.Axis == SigmaGaugeAxis.X ? targetAxis : x;
            int targetY = map.Axis == SigmaGaugeAxis.Y ? targetAxis : y;
            targetSample = targetY * SigmaDecodedPage.PageSize + targetX;
            return true;
        }

        internal static int[] TargetBlocksForSourceBlock(int sourceBlock,
            SigmaGaugeMap map)
        {
            if ((uint)sourceBlock >= SigmaDecodedPage.BlockCount)
                throw new ArgumentOutOfRangeException(nameof(sourceBlock));
            Span<bool> used = stackalloc bool[SigmaDecodedPage.BlockCount];
            int blockX = sourceBlock & 7;
            int blockY = sourceBlock >> 3;
            for (int localY = 0; localY < SigmaDecodedPage.BlockSize; ++localY)
            for (int localX = 0; localX < SigmaDecodedPage.BlockSize; ++localX)
            {
                int sourceSample = (blockY * SigmaDecodedPage.BlockSize +
                    localY) * SigmaDecodedPage.PageSize +
                    blockX * SigmaDecodedPage.BlockSize + localX;
                if (!TryMapRetainedSample(sourceSample, map,
                        out int targetSample))
                    continue;
                int targetX = targetSample & 63;
                int targetY = targetSample >> 6;
                used[(targetY >> 3) * 8 + (targetX >> 3)] = true;
            }
            int count = 0;
            for (int block = 0; block < used.Length; ++block)
                count += used[block] ? 1 : 0;
            var result = new int[count];
            int cursor = 0;
            for (int block = 0; block < used.Length; ++block)
                if (used[block])
                    result[cursor++] = block;
            return result;
        }

        private static long ProjectiveCoordinate(SigmaS16 state, int lane)
        {
            SigmaS16 transformed = SigmaS16Operators.HadamardB(state);
            long mass = transformed[SigmaGeneratedAlgebra.GeometryRows[0]];
            if (mass <= 0L)
                throw new InvalidOperationException(
                    "Gauge detail demand is defined only on supported Psi.");
            return SigmaNumericDomain.QDiv(transformed[lane], mass);
        }

        private static long MidpointError(long left, long center, long right)
        {
            long midpoint = SigmaNumericDomain.QMidpoint(left, right);
            return SigmaNumericDomain.QAbs(
                SigmaNumericDomain.QSub(center, midpoint));
        }

        private static SigmaS16 SampleHalfGrid(SigmaS16[] page,
            int xTimesTwo, int yTimesTwo,
            Func<SigmaGaugeAxis, int, int, SigmaTopologyClass>
                transitionClass)
        {
            int x = xTimesTwo >> 1;
            int y = yTimesTwo >> 1;
            bool halfX = (xTimesTwo & 1) != 0;
            bool halfY = (yTimesTwo & 1) != 0;
            SigmaS16 p00 = At(page, x, y);
            if (!halfX && !halfY)
                return p00;
            SigmaS16 p10 = halfX ? At(page, x + 1, y) : p00;
            SigmaS16 p01 = halfY ? At(page, x, y + 1) : p00;
            if (halfX && halfY)
                throw new InvalidOperationException(
                    "One gauge event refines exactly one carrier axis.");
            if (transitionClass != null)
            {
                SigmaGaugeAxis axis = halfX ? SigmaGaugeAxis.X :
                    SigmaGaugeAxis.Y;
                SigmaTopologyClass classification = transitionClass(axis,
                    x, y);
                if (classification == SigmaTopologyClass.Unresolved)
                    throw new InvalidOperationException(
                        "Gauge interpolation cannot cross unresolved topology.");
                if (classification == SigmaTopologyClass.Singular)
                    return p00;
            }
            return halfX ? Midpoint(p00, p10) : Midpoint(p00, p01);
        }

        private static SigmaS16 Midpoint(SigmaS16 left, SigmaS16 right)
        {
            var lanes = new long[SigmaS16.LaneCount];
            for (int lane = 0; lane < lanes.Length; ++lane)
                lanes[lane] = SigmaNumericDomain.QMidpoint(left[lane],
                    right[lane]);
            return SigmaS16.FromArray(lanes);
        }

        private static SigmaS16 At(SigmaS16[] page, int x, int y)
        {
            if ((uint)x >= SigmaDecodedPage.PageSize ||
                (uint)y >= SigmaDecodedPage.PageSize)
                throw new InvalidOperationException(
                    "Gauge map requested a non-resident carrier halo.");
            return page[y * SigmaDecodedPage.PageSize + x];
        }

        private static SigmaS16 GetAxis(SigmaS16[] page, SigmaGaugeMap map,
            int axis, int transverse) => map.Axis == SigmaGaugeAxis.X
            ? At(page, axis, transverse) : At(page, transverse, axis);

        private static void SetAxis(SigmaS16[] page, SigmaGaugeMap map,
            int axis, int transverse, SigmaS16 value)
        {
            int x = map.Axis == SigmaGaugeAxis.X ? axis : transverse;
            int y = map.Axis == SigmaGaugeAxis.Y ? axis : transverse;
            page[y * SigmaDecodedPage.PageSize + x] = value;
        }

        private static void AxisPreimage(int targetAxis, SigmaGaugeMap map,
            out int oldAxisTimesTwo)
        {
            int sourceStart = map.SourceAxisBlock * SigmaDecodedPage.BlockSize;
            int sourceEnd = sourceStart + SigmaDecodedPage.BlockSize;
            int orientedTarget = map.Negative
                ? sourceEnd - 1 - targetAxis : targetAxis - sourceStart;
            if (orientedTarget < 0)
                throw new InvalidOperationException("Target is outside gauge region.");
            int compressionStart = map.RetainedLength +
                SigmaDecodedPage.BlockSize;
            int orientedOldTimesTwo;
            if (orientedTarget < 16)
                orientedOldTimesTwo = orientedTarget;
            else if (orientedTarget < compressionStart)
                orientedOldTimesTwo = checked((orientedTarget -
                    SigmaDecodedPage.BlockSize) * 2);
            else
                orientedOldTimesTwo = checked(map.RetainedLength * 2 +
                    (orientedTarget - compressionStart) * 4);
            oldAxisTimesTwo = map.Negative
                ? checked((sourceEnd - 1) * 2 - orientedOldTimesTwo)
                : checked(sourceStart * 2 + orientedOldTimesTwo);
        }

        private static int OrientedToPage(int oriented, SigmaGaugeMap map)
        {
            int sourceStart = map.SourceAxisBlock * SigmaDecodedPage.BlockSize;
            return map.Negative
                ? sourceStart + SigmaDecodedPage.BlockSize - 1 - oriented
                : sourceStart + oriented;
        }

        private static void ValidatePage(SigmaS16[] page)
        {
            if (page == null || page.Length != SigmaDecodedPage.SampleCount)
                throw new ArgumentException("Gauge transform requires one 64x64 page.",
                    nameof(page));
        }
    }
}
