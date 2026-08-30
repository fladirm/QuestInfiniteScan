using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Deterministic CPU authority for the disposable M8 overlap-shell readout.
    /// Coordinates are exact quarter-lattice units; no result is persistent state.
    /// </summary>
    internal static class MerkabaOverlapShell
    {
        internal const int QuarterUnitsPerLatticeStep = 4;
        internal const int SupportHalfQuarterUnits = 4;
        internal const int CornersPerPatch = 4;
        internal const int TrianglesPerPatch = 2;
        internal const int VerticesPerPatch = 6;

        internal readonly struct Signature : IEquatable<Signature>
        {
            internal readonly byte NormalIndex;
            internal readonly byte ChartAxis;
            internal readonly sbyte FreeSign;
            internal readonly int3 Normal;

            internal Signature(int normalIndex, int chartAxis, int freeSign,
                int3 normal)
            {
                NormalIndex = (byte)normalIndex;
                ChartAxis = (byte)chartAxis;
                FreeSign = (sbyte)freeSign;
                Normal = normal;
            }

            public bool Equals(Signature other) =>
                NormalIndex == other.NormalIndex &&
                ChartAxis == other.ChartAxis &&
                FreeSign == other.FreeSign &&
                math.all(Normal == other.Normal);

            public override bool Equals(object obj) =>
                obj is Signature other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(
                NormalIndex, ChartAxis, FreeSign,
                Normal.x, Normal.y, Normal.z);
        }

        internal readonly struct Corner : IEquatable<Corner>
        {
            internal readonly int3 QuarterCoordinate;
            internal readonly uint PackedColor;
            internal readonly int ContributorCount;

            internal Corner(int3 quarterCoordinate, uint packedColor,
                int contributorCount)
            {
                QuarterCoordinate = quarterCoordinate;
                PackedColor = packedColor;
                ContributorCount = contributorCount;
            }

            public bool Equals(Corner other) =>
                math.all(QuarterCoordinate == other.QuarterCoordinate) &&
                PackedColor == other.PackedColor &&
                ContributorCount == other.ContributorCount;

            public override bool Equals(object obj) =>
                obj is Corner other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(
                QuarterCoordinate.x, QuarterCoordinate.y,
                QuarterCoordinate.z, PackedColor, ContributorCount);
        }

        internal readonly struct Patch
        {
            internal readonly int3 Main;
            internal readonly Signature SurfaceSignature;
            internal readonly Corner Corner00;
            internal readonly Corner Corner10;
            internal readonly Corner Corner11;
            internal readonly Corner Corner01;

            internal Patch(int3 main, Signature signature, Corner corner00,
                Corner corner10, Corner corner11, Corner corner01)
            {
                Main = main;
                SurfaceSignature = signature;
                Corner00 = corner00;
                Corner10 = corner10;
                Corner11 = corner11;
                Corner01 = corner01;
            }

            internal Corner GetCorner(int index) => index switch
            {
                0 => Corner00,
                1 => Corner10,
                2 => Corner11,
                3 => Corner01,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };

            internal Corner GetTriangleVertex(int vertex)
            {
                if ((uint)vertex >= VerticesPerPatch)
                    throw new ArgumentOutOfRangeException(nameof(vertex));
                ReadOnlySpan<byte> order = SurfaceSignature.FreeSign > 0
                    ? ForwardOrder : ReverseOrder;
                return GetCorner(order[vertex]);
            }
        }

        private readonly struct Contributor
        {
            internal readonly int3 Coord;
            internal readonly KernelState State;
            internal readonly int NormalCoordinate;

            internal Contributor(int3 coord, KernelState state,
                int normalCoordinate)
            {
                Coord = coord;
                State = state;
                NormalCoordinate = normalCoordinate;
            }
        }

        private readonly struct Branch
        {
            internal readonly int NormalIndex;
            internal readonly int3 Normal;
            internal readonly int ChartAxis;
            internal readonly int FreeSign;
            internal readonly int TangentSupport;
            internal readonly int NormalResidual;
            internal readonly int FreeCoherence;

            internal Branch(int normalIndex, int3 normal, int chartAxis,
                int freeSign, int tangentSupport, int normalResidual,
                int freeCoherence)
            {
                NormalIndex = normalIndex;
                Normal = normal;
                ChartAxis = chartAxis;
                FreeSign = freeSign;
                TangentSupport = tangentSupport;
                NormalResidual = normalResidual;
                FreeCoherence = freeCoherence;
            }

            internal Signature Signature => new(NormalIndex, ChartAxis,
                FreeSign, Normal);
        }

        private sealed class LocalWindow
        {
            private readonly int3 _main;
            private readonly KernelState[] _states = new KernelState[27];

            internal LocalWindow(int3 main, Func<int3, KernelState> sample)
            {
                _main = main;
                for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                    _states[Index(new int3(x, y, z))] =
                        sample(main + new int3(x, y, z));
            }

            internal KernelState At(int3 offset)
            {
                if (math.any(offset < -1) || math.any(offset > 1))
                    throw new InvalidOperationException(
                        $"Overlap-shell queried non-immediate offset {offset}.");
                return _states[Index(offset)];
            }

            internal int3 Global(int3 offset) => _main + offset;

            private static int Index(int3 offset) =>
                offset.x + 1 + 3 * (offset.y + 1) +
                9 * (offset.z + 1);
        }

        private static readonly byte[] ForwardOrder = { 0, 1, 2, 0, 2, 3 };
        private static readonly byte[] ReverseOrder = { 0, 2, 1, 0, 3, 2 };
        private static readonly int3[] NormalDictionary =
        {
            new(1, 0, 0), new(0, 1, 0), new(0, 0, 1),
            new(1, 1, 0), new(1, -1, 0),
            new(1, 0, 1), new(1, 0, -1),
            new(0, 1, 1), new(0, 1, -1),
            new(1, 1, 1), new(1, 1, -1),
            new(1, -1, 1), new(1, -1, -1)
        };

        internal static bool TryBuildPatch(int3 main,
            Func<int3, KernelState> sample, out Patch patch)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            var window = new LocalWindow(main, sample);
            if (!window.At(default).IsOccupied)
            {
                patch = default;
                return false;
            }

            if (!TryDeriveSignature(window, out Signature signature))
            {
                patch = default;
                return false;
            }
            Axes(signature.ChartAxis, out int3 chartNormal,
                out int3 tangent0, out int3 tangent1);
            Corner corner00 = BuildCorner(window, signature, chartNormal,
                tangent0, tangent1, -1, -1);
            Corner corner10 = BuildCorner(window, signature, chartNormal,
                tangent0, tangent1, 1, -1);
            Corner corner11 = BuildCorner(window, signature, chartNormal,
                tangent0, tangent1, 1, 1);
            Corner corner01 = BuildCorner(window, signature, chartNormal,
                tangent0, tangent1, -1, 1);
            patch = new Patch(main, signature, corner00, corner10,
                corner11, corner01);
            return true;
        }

        internal static bool TryDeriveSignature(int3 main,
            Func<int3, KernelState> sample, out Signature signature)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            return TryDeriveSignature(new LocalWindow(main, sample),
                out signature);
        }

        private static bool TryDeriveSignature(LocalWindow window,
            out Signature signature)
        {
            bool found = false;
            bool tied = false;
            Branch best = default;
            for (int index = 0; index < NormalDictionary.Length; index++)
            {
                if (!TryEvaluateBranch(window, index, out Branch candidate))
                    continue;
                int comparison = found ? CompareBranch(candidate, best) : 1;
                if (comparison > 0)
                {
                    best = candidate;
                    found = true;
                    tied = false;
                }
                else if (comparison == 0 &&
                         !math.all(candidate.Normal == best.Normal))
                    tied = true;
            }
            if (!found || tied)
            {
                signature = default;
                return false;
            }
            signature = best.Signature;
            return true;
        }

        private static bool TryEvaluateBranch(LocalWindow window,
            int normalIndex, out Branch branch)
        {
            int3 normal = NormalDictionary[normalIndex];
            int chartAxis = FirstNonZeroAxis(normal);
            Axes(chartAxis, out int3 chartNormal,
                out int3 tangent0, out int3 tangent1);
            FreeSide(window, normal, out int positiveFree,
                out int negativeFree);
            if (positiveFree == negativeFree)
            {
                branch = default;
                return false;
            }
            int freeSign = positiveFree > negativeFree ? 1 : -1;
            int freeCoherence = Math.Abs(positiveFree - negativeFree);
            int support = 0;
            int residual = 0;
            Span<int2> tangentDirections = stackalloc int2[8];
            for (int v = -1; v <= 1; v++)
            for (int u = -1; u <= 1; u++)
            {
                if (u == 0 && v == 0) continue;
                int3 tangentOffset = tangent0 * u + tangent1 * v;
                if (!TrySelectColumnContributor(window, normal, chartNormal,
                        freeSign, tangentOffset, out Contributor contributor,
                        out int contributorResidual))
                    continue;
                tangentDirections[support] = new int2(u, v);
                support++;
                int normalSquared = math.dot(normal, normal);
                residual += contributorResidual * contributorResidual *
                    (6 / normalSquared);
            }
            if (support < 2 || !HasNonCollinearSupport(
                    tangentDirections[..support]))
            {
                branch = default;
                return false;
            }
            branch = new Branch(normalIndex, normal, chartAxis, freeSign,
                support, residual, freeCoherence);
            return true;
        }

        private static int CompareBranch(Branch left, Branch right)
        {
            int support = left.TangentSupport.CompareTo(right.TangentSupport);
            if (support != 0) return support;
            int residual = right.NormalResidual.CompareTo(left.NormalResidual);
            if (residual != 0) return residual;
            return left.FreeCoherence.CompareTo(right.FreeCoherence);
        }

        private static bool HasNonCollinearSupport(ReadOnlySpan<int2> support)
        {
            for (int first = 0; first < support.Length; first++)
            for (int second = first + 1; second < support.Length; second++)
                if (support[first].x * support[second].y -
                    support[first].y * support[second].x != 0)
                    return true;
            return false;
        }

        private static void FreeSide(LocalWindow window, int3 normal,
            out int positive, out int negative)
        {
            positive = 0;
            negative = 0;
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                int3 offset = new(x, y, z);
                int signedDistance = math.dot(offset, normal);
                int evidence = window.At(offset).OccupancyEvidence;
                if (evidence >= 0 || signedDistance == 0) continue;
                int weight = -evidence * Math.Abs(signedDistance);
                if (signedDistance > 0) positive += weight;
                else negative += weight;
            }
        }

        private static Corner BuildCorner(LocalWindow window,
            Signature signature, int3 chartNormal, int3 tangent0, int3 tangent1,
            int tangentSign0, int tangentSign1)
        {
            var contributors = new List<Contributor>(4);
            for (int v = 0; v <= 1; v++)
            for (int u = 0; u <= 1; u++)
            {
                int3 tangentOffset =
                    (u == 0 ? default : tangent0 * tangentSign0) +
                    (v == 0 ? default : tangent1 * tangentSign1);
                if (TrySelectColumnContributor(window, signature.Normal,
                        chartNormal, signature.FreeSign, tangentOffset,
                        out Contributor contributor, out _))
                    contributors.Add(contributor);
            }

            if (contributors.Count == 0)
                throw new InvalidOperationException(
                    "An occupied MAIN must contribute to every local corner.");
            contributors.Sort(CompareContributor);
            int lower = contributors[(contributors.Count - 1) / 2]
                .NormalCoordinate;
            int upper = contributors[contributors.Count / 2]
                .NormalCoordinate;
            int quarterHeight = MedianQuarterHeight(contributors);

            int3 quarter = window.Global(default) *
                QuarterUnitsPerLatticeStep +
                tangent0 * (tangentSign0 * SupportHalfQuarterUnits) +
                tangent1 * (tangentSign1 * SupportHalfQuarterUnits);
            quarter[signature.ChartAxis] = quarterHeight;

            uint packedColor = ReduceColor(contributors, lower, upper,
                out int colorContributorCount);
            return new Corner(quarter, packedColor, colorContributorCount);
        }

        internal static int MedianQuarterHeight(ReadOnlySpan<int> heights)
        {
            if (heights.Length == 0 || heights.Length > 4)
                throw new ArgumentOutOfRangeException(nameof(heights));
            Span<int> ordered = stackalloc int[4];
            heights.CopyTo(ordered);
            for (int i = 1; i < heights.Length; i++)
            {
                int value = ordered[i];
                int insert = i;
                while (insert > 0 && value < ordered[insert - 1])
                {
                    ordered[insert] = ordered[insert - 1];
                    insert--;
                }
                ordered[insert] = value;
            }
            int lower = ordered[(heights.Length - 1) / 2];
            int upper = ordered[heights.Length / 2];
            return 2 * (lower + upper);
        }

        private static int MedianQuarterHeight(List<Contributor> contributors)
        {
            Span<int> heights = stackalloc int[4];
            for (int index = 0; index < contributors.Count; index++)
                heights[index] = contributors[index].NormalCoordinate;
            return MedianQuarterHeight(heights[..contributors.Count]);
        }

        private static bool TrySelectColumnContributor(LocalWindow window,
            int3 normal, int3 chartNormal, int freeSign, int3 tangentOffset,
            out Contributor selected, out int selectedResidual)
        {
            selected = default;
            selectedResidual = 0;
            bool found = false;
            ColumnFreeSide(window, normal, chartNormal, tangentOffset,
                out int positiveFree, out int negativeFree);
            if (positiveFree != negativeFree &&
                (positiveFree > negativeFree ? 1 : -1) != freeSign)
                return false;

            for (int normalOffset = -1; normalOffset <= 1; normalOffset++)
            {
                int3 offset = tangentOffset + chartNormal * normalOffset;
                KernelState state = window.At(offset);
                if (!state.IsOccupied) continue;
                if (HasKnownFreeSeparator(window, tangentOffset, chartNormal,
                        normalOffset))
                    continue;
                int residual = Math.Abs(math.dot(offset, normal));
                if (residual > math.dot(normal, normal)) continue;
                int chartAxis = FirstNonZeroAxis(normal);
                var candidate = new Contributor(window.Global(offset), state,
                    window.Global(offset)[chartAxis]);
                int selectedOffset = selected.NormalCoordinate -
                    window.Global(default)[chartAxis];
                if (!found ||
                    Math.Abs(normalOffset) < Math.Abs(selectedOffset) ||
                    (Math.Abs(normalOffset) == Math.Abs(selectedOffset) &&
                     (residual < selectedResidual ||
                      (residual == selectedResidual &&
                       MerkabaConstants.LexicographicallyLess(candidate.Coord,
                           selected.Coord)))))
                {
                    selected = candidate;
                    selectedResidual = residual;
                    found = true;
                }
            }
            return found;
        }

        private static void ColumnFreeSide(LocalWindow window, int3 normal,
            int3 chartNormal, int3 tangentOffset, out int positive,
            out int negative)
        {
            positive = 0;
            negative = 0;
            for (int normalOffset = -1; normalOffset <= 1; normalOffset++)
            {
                int3 offset = tangentOffset + chartNormal * normalOffset;
                int evidence = window.At(offset).OccupancyEvidence;
                int signedDistance = math.dot(offset, normal);
                if (evidence >= 0 || signedDistance == 0) continue;
                int weight = -evidence * Math.Abs(signedDistance);
                if (signedDistance > 0) positive += weight;
                else negative += weight;
            }
        }

        private static bool HasKnownFreeSeparator(LocalWindow window,
            int3 tangentOffset, int3 chartNormal, int normalOffset)
        {
            if (normalOffset > 0)
                for (int step = 0; step < normalOffset; step++)
                    if (window.At(tangentOffset + chartNormal * step)
                            .OccupancyEvidence < 0)
                        return true;
            if (normalOffset < 0)
                for (int step = 0; step > normalOffset; step--)
                    if (window.At(tangentOffset + chartNormal * step)
                            .OccupancyEvidence < 0)
                        return true;
            return false;
        }

        private static uint ReduceColor(List<Contributor> contributors,
            int lowerHeight, int upperHeight, out int contributorCount)
        {
            ulong red = 0;
            ulong green = 0;
            ulong blue = 0;
            ulong alpha = 0;
            ulong total = 0;
            contributorCount = 0;
            foreach (Contributor contributor in contributors)
            {
                if (contributor.NormalCoordinate < lowerHeight ||
                    contributor.NormalCoordinate > upperHeight ||
                    contributor.State.ColorConfidence == 0)
                    continue;
                uint weight = Math.Min(contributor.State.ColorConfidence,
                    (uint)MerkabaConstants.MaximumColorConfidence);
                uint color = contributor.State.PackedColor;
                red += (color & 0xffu) * (ulong)weight;
                green += ((color >> 8) & 0xffu) * (ulong)weight;
                blue += ((color >> 16) & 0xffu) * (ulong)weight;
                alpha += ((color >> 24) & 0xffu) * (ulong)weight;
                total += weight;
                contributorCount++;
            }
            if (total == 0)
                return MerkabaConstants.NeutralPackedColor & 0x00ffffffu;
            uint r = (uint)((red + total / 2) / total);
            uint g = (uint)((green + total / 2) / total);
            uint b = (uint)((blue + total / 2) / total);
            uint a = (uint)((alpha + total / 2) / total);
            return r | (g << 8) | (b << 16) | (a << 24);
        }

        private static int CompareContributor(Contributor left,
            Contributor right)
        {
            int height = left.NormalCoordinate.CompareTo(
                right.NormalCoordinate);
            if (height != 0) return height;
            if (MerkabaConstants.LexicographicallyLess(left.Coord,
                    right.Coord))
                return -1;
            if (MerkabaConstants.LexicographicallyLess(right.Coord,
                    left.Coord))
                return 1;
            return 0;
        }

        private static int FirstNonZeroAxis(int3 value)
        {
            if (value.x != 0) return 0;
            return value.y != 0 ? 1 : 2;
        }

        internal static ReadOnlySpan<int3> CanonicalNormals =>
            NormalDictionary;

        internal static void Axes(int normalAxis, out int3 normal,
            out int3 tangent0, out int3 tangent1)
        {
            switch (normalAxis)
            {
                case 0:
                    normal = new int3(1, 0, 0);
                    tangent0 = new int3(0, 1, 0);
                    tangent1 = new int3(0, 0, 1);
                    break;
                case 1:
                    normal = new int3(0, 1, 0);
                    tangent0 = new int3(0, 0, 1);
                    tangent1 = new int3(1, 0, 0);
                    break;
                case 2:
                    normal = new int3(0, 0, 1);
                    tangent0 = new int3(1, 0, 0);
                    tangent1 = new int3(0, 1, 0);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(normalAxis));
            }
        }

#if UNITY_EDITOR
        internal static string BuildGeneratedHlsl()
        {
            const string template = @"// GENERATED from MerkabaOverlapShell.cs. DO NOT EDIT.
@HASH@ifndef GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED
@HASH@define GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED

@HASH@define M8_OVERLAP_QUARTERS_PER_STEP @QUARTERS@
@HASH@define M8_OVERLAP_SUPPORT_HALF_QUARTERS @SUPPORT_HALF@
@HASH@define M8_OVERLAP_TRIANGLES_PER_PATCH @TRIANGLES@u
@HASH@define M8_OVERLAP_NORMAL_COUNT 13u

struct M8OverlapSignature
{
    uint valid;
    uint normalIndex;
    uint chartAxis;
    int freeSign;
    int3 normal;
};

struct M8OverlapBranch
{
    uint valid;
    uint normalIndex;
    uint chartAxis;
    int freeSign;
    int3 normal;
    uint tangentSupport;
    uint normalResidual;
    uint freeCoherence;
};

struct M8OverlapContributor
{
    uint valid;
    int normalOffset;
    int height;
    uint stateToken;
    uint residual;
};

struct M8OverlapBranchState
{
    uint active;
    uint normalIndex;
    uint chartAxis;
    int freeSign;
    int3 normal;
    int3 chartNormal;
    int3 tangent0;
    int3 tangent1;
    uint normalSquared;
    uint supportMask;
    uint support;
    uint residual;
    uint freeCoherence;
};

struct M8OverlapCorner
{
    int3 quarterCoordinate;
    uint packedColor;
};

struct M8OverlapPatch
{
    M8OverlapSignature signature;
    M8OverlapCorner corner00;
    M8OverlapCorner corner10;
    M8OverlapCorner corner11;
    M8OverlapCorner corner01;
};

int3 M8OverlapNormal(uint index)
{
    int3 value = int3(1, -1, -1);
    if (index == 0u) value = int3(1, 0, 0);
    else if (index == 1u) value = int3(0, 1, 0);
    else if (index == 2u) value = int3(0, 0, 1);
    else if (index == 3u) value = int3(1, 1, 0);
    else if (index == 4u) value = int3(1, -1, 0);
    else if (index == 5u) value = int3(1, 0, 1);
    else if (index == 6u) value = int3(1, 0, -1);
    else if (index == 7u) value = int3(0, 1, 1);
    else if (index == 8u) value = int3(0, 1, -1);
    else if (index == 9u) value = int3(1, 1, 1);
    else if (index == 10u) value = int3(1, 1, -1);
    else if (index == 11u) value = int3(1, -1, 1);
    return value;
}

uint M8OverlapFirstNonZeroAxis(int3 value)
{
    uint axis = 2u;
    if (value.x != 0) axis = 0u;
    else if (value.y != 0) axis = 1u;
    return axis;
}

int M8OverlapAxisValue(int3 value, uint axis)
{
    int component = value.z;
    if (axis == 0u) component = value.x;
    else if (axis == 1u) component = value.y;
    return component;
}

void M8SetOverlapAxisValue(inout int3 value, uint axis, int component)
{
    if (axis == 0u) value.x = component;
    else if (axis == 1u) value.y = component;
    else value.z = component;
}

void M8OverlapAxes(uint chartAxis, out int3 chartNormal,
    out int3 tangent0, out int3 tangent1)
{
    if (chartAxis == 0u)
    {
        chartNormal = int3(1, 0, 0);
        tangent0 = int3(0, 1, 0);
        tangent1 = int3(0, 0, 1);
    }
    else if (chartAxis == 1u)
    {
        chartNormal = int3(0, 1, 0);
        tangent0 = int3(0, 0, 1);
        tangent1 = int3(1, 0, 0);
    }
    else
    {
        chartNormal = int3(0, 0, 1);
        tangent0 = int3(1, 0, 0);
        tangent1 = int3(0, 1, 0);
    }
}

int2 M8OverlapTangentDirection(uint index)
{
    int2 direction = int2(1, 1);
    if (index == 0u) direction = int2(-1, -1);
    else if (index == 1u) direction = int2(0, -1);
    else if (index == 2u) direction = int2(1, -1);
    else if (index == 3u) direction = int2(-1, 0);
    else if (index == 4u) direction = int2(1, 0);
    else if (index == 5u) direction = int2(-1, 1);
    else if (index == 6u) direction = int2(0, 1);
    return direction;
}

uint M8OverlapResidualScale(uint normalSquared)
{
    uint scale = 2u;
    if (normalSquared == 1u) scale = 6u;
    else if (normalSquared == 2u) scale = 3u;
    return scale;
}

void M8AccumulateOverlapFreeSample(int3 mainHaloCoord, int3 normal,
    int3 offset, inout uint positive, inout uint negative)
{
    int signedDistance = dot(offset, normal);
    int evidence = gM8ShellEvidence[M8ShellIndex(mainHaloCoord, offset)];
    if (evidence >= 0 || signedDistance == 0) return;
    uint weight = (uint)(-evidence * abs(signedDistance));
    if (signedDistance > 0) positive += weight;
    else negative += weight;
}

void M8OverlapFreeSide(int3 mainHaloCoord, int3 normal,
    out uint positive, out uint negative)
{
    positive = 0u;
    negative = 0u;
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3(-1,-1,-1), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3( 0,-1,-1), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3( 1,-1,-1), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3(-1, 0,-1), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3( 0, 0,-1), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3( 1, 0,-1), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3(-1, 1,-1), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3( 0, 1,-1), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3( 1, 1,-1), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3(-1,-1, 0), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3( 0,-1, 0), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3( 1,-1, 0), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3(-1, 0, 0), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3( 1, 0, 0), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3(-1, 1, 0), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3( 0, 1, 0), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3( 1, 1, 0), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3(-1,-1, 1), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3( 0,-1, 1), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3( 1,-1, 1), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3(-1, 0, 1), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3( 0, 0, 1), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3( 1, 0, 1), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3(-1, 1, 1), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3( 0, 1, 1), positive, negative);
    M8AccumulateOverlapFreeSample(mainHaloCoord, normal, int3( 1, 1, 1), positive, negative);
}

void M8AccumulateOverlapColumnFreeSample(int3 mainHaloCoord, int3 normal,
    int3 chartNormal, int3 tangentOffset, int normalOffset,
    inout uint positive, inout uint negative)
{
    int3 offset = tangentOffset + chartNormal * normalOffset;
    int evidence = gM8ShellEvidence[M8ShellIndex(mainHaloCoord, offset)];
    int signedDistance = dot(offset, normal);
    if (evidence >= 0 || signedDistance == 0) return;
    uint weight = (uint)(-evidence * abs(signedDistance));
    if (signedDistance > 0) positive += weight;
    else negative += weight;
}

void M8OverlapColumnFreeSide(int3 mainHaloCoord, int3 normal,
    int3 chartNormal, int3 tangentOffset, out uint positive, out uint negative)
{
    positive = 0u;
    negative = 0u;
    M8AccumulateOverlapColumnFreeSample(mainHaloCoord, normal, chartNormal,
        tangentOffset, -1, positive, negative);
    M8AccumulateOverlapColumnFreeSample(mainHaloCoord, normal, chartNormal,
        tangentOffset, 0, positive, negative);
    M8AccumulateOverlapColumnFreeSample(mainHaloCoord, normal, chartNormal,
        tangentOffset, 1, positive, negative);
}

bool M8OverlapHasKnownFreeSeparator(int3 mainHaloCoord,
    int3 chartNormal, int3 tangentOffset, int normalOffset)
{
    return normalOffset != 0 &&
        gM8ShellEvidence[M8ShellIndex(mainHaloCoord, tangentOffset)] < 0;
}

void M8ConsiderOverlapContributorSample(int3 globalCoord,
    int3 mainHaloCoord, int3 normal, int3 chartNormal, int3 tangentOffset,
    int normalOffset, uint chartAxis, uint normalSquared,
    inout M8OverlapContributor selected)
{
    int3 offset = tangentOffset + chartNormal * normalOffset;
    uint sampleIndex = M8ShellIndex(mainHaloCoord, offset);
    if (gM8ShellOccupied[sampleIndex] == 0u ||
        M8OverlapHasKnownFreeSeparator(mainHaloCoord, chartNormal,
            tangentOffset, normalOffset))
        return;
    uint residual = (uint)abs(dot(offset, normal));
    if (residual > normalSquared) return;
    if (selected.valid == 0u ||
        abs(normalOffset) < abs(selected.normalOffset) ||
        (abs(normalOffset) == abs(selected.normalOffset) &&
         residual < selected.residual))
    {
        selected.valid = 1u;
        selected.normalOffset = normalOffset;
        selected.residual = residual;
        selected.height = M8OverlapAxisValue(globalCoord, chartAxis) +
            normalOffset;
        selected.stateToken = gM8ShellStateTokens[sampleIndex];
    }
}

M8OverlapContributor M8GetOverlapContributor(int3 globalCoord,
    int3 mainHaloCoord, int3 normal, int3 chartNormal, int freeSign,
    int3 tangentOffset)
{
    M8OverlapContributor selected = (M8OverlapContributor)0;
    uint positiveFree = 0u;
    uint negativeFree = 0u;
    M8OverlapColumnFreeSide(mainHaloCoord, normal, chartNormal,
        tangentOffset, positiveFree, negativeFree);
    if (positiveFree != negativeFree &&
        (positiveFree > negativeFree ? 1 : -1) != freeSign)
        return selected;

    uint chartAxis = M8OverlapFirstNonZeroAxis(normal);
    uint normalSquared = (uint)dot(normal, normal);
    M8ConsiderOverlapContributorSample(globalCoord, mainHaloCoord, normal,
        chartNormal, tangentOffset, -1, chartAxis, normalSquared, selected);
    M8ConsiderOverlapContributorSample(globalCoord, mainHaloCoord, normal,
        chartNormal, tangentOffset, 0, chartAxis, normalSquared, selected);
    M8ConsiderOverlapContributorSample(globalCoord, mainHaloCoord, normal,
        chartNormal, tangentOffset, 1, chartAxis, normalSquared, selected);
    return selected;
}

bool M8OverlapHasNonCollinearSupport(uint supportMask)
{
    uint directionGroups = 0u;
    directionGroups += (supportMask & 0x81u) != 0u ? 1u : 0u;
    directionGroups += (supportMask & 0x42u) != 0u ? 1u : 0u;
    directionGroups += (supportMask & 0x24u) != 0u ? 1u : 0u;
    directionGroups += (supportMask & 0x18u) != 0u ? 1u : 0u;
    return directionGroups >= 2u;
}

void M8AccumulateOverlapDirection(int3 globalCoord, int3 mainHaloCoord,
    int3 normal, int3 chartNormal, int3 tangent0, int3 tangent1,
    int freeSign, uint normalSquared, int2 direction, uint directionIndex,
    inout uint supportMask, inout uint support, inout uint residual)
{
    int3 tangentOffset = tangent0 * direction.x + tangent1 * direction.y;
    M8OverlapContributor contributor = M8GetOverlapContributor(globalCoord,
        mainHaloCoord, normal, chartNormal, freeSign, tangentOffset);
    if (contributor.valid == 0u)
        return;
    supportMask |= 1u << directionIndex;
    support++;
    residual += contributor.residual * contributor.residual *
        M8OverlapResidualScale(normalSquared);
}

void M8BeginOverlapBranch(int3 mainHaloCoord, uint normalIndex,
    out M8OverlapBranchState state)
{
    state = (M8OverlapBranchState)0;
    state.normalIndex = normalIndex;
    state.normal = M8OverlapNormal(normalIndex);
    state.chartAxis = M8OverlapFirstNonZeroAxis(state.normal);
    M8OverlapAxes(state.chartAxis, state.chartNormal,
        state.tangent0, state.tangent1);
    uint positiveFree;
    uint negativeFree;
    M8OverlapFreeSide(mainHaloCoord, state.normal,
        positiveFree, negativeFree);
    if (positiveFree == negativeFree) return;

    state.active = 1u;
    state.freeSign = positiveFree > negativeFree ? 1 : -1;
    state.normalSquared = (uint)dot(state.normal, state.normal);
    state.freeCoherence = positiveFree > negativeFree
        ? positiveFree - negativeFree : negativeFree - positiveFree;
}

void M8AccumulateOverlapBranchDirection(int3 globalCoord,
    int3 mainHaloCoord, uint directionIndex,
    inout M8OverlapBranchState state)
{
    if (state.active == 0u) return;
    M8AccumulateOverlapDirection(globalCoord, mainHaloCoord, state.normal,
        state.chartNormal, state.tangent0, state.tangent1, state.freeSign,
        state.normalSquared, M8OverlapTangentDirection(directionIndex),
        directionIndex, state.supportMask, state.support, state.residual);
}

M8OverlapBranch M8FinishOverlapBranch(M8OverlapBranchState state)
{
    M8OverlapBranch branch = (M8OverlapBranch)0;
    if (state.active == 0u || state.support < 2u ||
        !M8OverlapHasNonCollinearSupport(state.supportMask))
        return branch;

    branch.valid = 1u;
    branch.normalIndex = state.normalIndex;
    branch.chartAxis = state.chartAxis;
    branch.freeSign = state.freeSign;
    branch.normal = state.normal;
    branch.tangentSupport = state.support;
    branch.normalResidual = state.residual;
    branch.freeCoherence = state.freeCoherence;
    return branch;
}

int M8CompareOverlapBranch(M8OverlapBranch left, M8OverlapBranch right)
{
    int result = 0;
    if (left.tangentSupport != right.tangentSupport)
        result = left.tangentSupport > right.tangentSupport ? 1 : -1;
    else if (left.normalResidual != right.normalResidual)
        result = left.normalResidual < right.normalResidual ? 1 : -1;
    else if (left.freeCoherence != right.freeCoherence)
        result = left.freeCoherence > right.freeCoherence ? 1 : -1;
    return result;
}

void M8ResetOverlapBranchSearch(out bool found, out bool tied,
    out M8OverlapBranch best)
{
    found = false;
    tied = false;
    best = (M8OverlapBranch)0;
}

void M8ConsiderOverlapBranch(M8OverlapBranch candidate,
    inout bool found, inout bool tied,
    inout M8OverlapBranch best)
{
    int comparison = found ? M8CompareOverlapBranch(candidate, best) : 1;
    if (comparison > 0)
    {
        best = candidate;
        found = true;
        tied = false;
    }
    else if (comparison == 0 && candidate.normalIndex != best.normalIndex)
        tied = true;
}

M8OverlapSignature M8FinishOverlapBranchSearch(bool found, bool tied,
    M8OverlapBranch best)
{
    M8OverlapSignature signature = (M8OverlapSignature)0;
    if (!found || tied)
        return signature;
    signature.valid = 1u;
    signature.normalIndex = best.normalIndex;
    signature.chartAxis = best.chartAxis;
    signature.freeSign = best.freeSign;
    signature.normal = best.normal;
    return signature;
}

uint M8PackOverlapSignature(M8OverlapSignature signature)
{
    return signature.normalIndex | (signature.freeSign > 0 ? 16u : 0u);
}

M8OverlapSignature M8UnpackOverlapSignature(uint packed)
{
    M8OverlapSignature signature = (M8OverlapSignature)0;
    signature.valid = 1u;
    signature.normalIndex = packed & 15u;
    signature.normal = M8OverlapNormal(signature.normalIndex);
    signature.chartAxis = M8OverlapFirstNonZeroAxis(signature.normal);
    signature.freeSign = (packed & 16u) != 0u ? 1 : -1;
    return signature;
}

int2 M8OverlapMedianBand(int4 values, uint count)
{
    int2 band = int2(0, 0);
    if (count == 1u)
    {
        band = int2(values.x, values.x);
        return band;
    }
    if (count == 2u)
    {
        band = int2(min(values.x, values.y), max(values.x, values.y));
        return band;
    }
    if (count == 3u)
    {
        int minimum = min(values.x, min(values.y, values.z));
        int maximum = max(values.x, max(values.y, values.z));
        int median = values.x + values.y + values.z - minimum - maximum;
        return int2(median, median);
    }
    int x = values.x;
    int y = values.y;
    int z = values.z;
    int w = values.w;
    int swap;
    if (x > y) { swap = x; x = y; y = swap; }
    if (z > w) { swap = z; z = w; w = swap; }
    if (x > z) { swap = x; x = z; z = swap; }
    if (y > w) { swap = y; y = w; w = swap; }
    if (y > z) { swap = y; y = z; z = swap; }
    return int2(y, z);
}

void M8AppendOverlapContributor(int height, uint stateToken,
    inout int4 heights, inout uint4 stateTokens, inout uint count)
{
    if (count == 0u) { heights.x = height; stateTokens.x = stateToken; }
    else if (count == 1u) { heights.y = height; stateTokens.y = stateToken; }
    else if (count == 2u) { heights.z = height; stateTokens.z = stateToken; }
    else { heights.w = height; stateTokens.w = stateToken; }
    count++;
}

void M8CollectOverlapContributor(int3 globalCoord, int3 mainHaloCoord,
    M8OverlapSignature signature, int3 chartNormal, int3 tangentOffset,
    inout int4 heights, inout uint4 stateTokens, inout uint count)
{
    M8OverlapContributor contributor = M8GetOverlapContributor(globalCoord,
        mainHaloCoord, signature.normal, chartNormal, signature.freeSign,
        tangentOffset);
    if (contributor.valid != 0u)
        M8AppendOverlapContributor(contributor.height, contributor.stateToken,
            heights, stateTokens, count);
}

void M8AccumulateOverlapColor(int height, uint stateToken,
    int lowerHeight, int upperHeight, inout uint4 colorTotal,
    inout uint total)
{
    if (height < lowerHeight || height > upperHeight || stateToken == 0u)
        return;
    uint stateIndex = stateToken - 1u;
    KernelState state = M8LoadKernelStateRead(stateIndex >> 9u,
        stateIndex & 511u);
    uint weight = min(state.colorConfidence, 65535u);
    if (weight == 0u) return;
    uint color = state.packedColor;
    colorTotal += uint4(color & 255u, (color >> 8u) & 255u,
        (color >> 16u) & 255u, (color >> 24u) & 255u) * weight;
    total += weight;
}

uint M8ReduceOverlapColor(int4 heights, uint4 stateTokens,
    uint count, int lowerHeight, int upperHeight)
{
    uint4 colorTotal = uint4(0u, 0u, 0u, 0u);
    uint total = 0u;
    if (count > 0u) M8AccumulateOverlapColor(heights.x, stateTokens.x,
        lowerHeight, upperHeight, colorTotal, total);
    if (count > 1u) M8AccumulateOverlapColor(heights.y, stateTokens.y,
        lowerHeight, upperHeight, colorTotal, total);
    if (count > 2u) M8AccumulateOverlapColor(heights.z, stateTokens.z,
        lowerHeight, upperHeight, colorTotal, total);
    if (count > 3u) M8AccumulateOverlapColor(heights.w, stateTokens.w,
        lowerHeight, upperHeight, colorTotal, total);
    uint packed = 0x00a0a0a0u;
    if (total != 0u)
    {
        float inverseTotal = rcp((float)total);
        uint4 reduced = (uint4)floor((float4)colorTotal * inverseTotal + 0.5);
        packed = reduced.x | (reduced.y << 8u) | (reduced.z << 16u) |
            (reduced.w << 24u);
    }
    return packed;
}

M8OverlapCorner M8BuildOverlapCorner(int3 globalCoord, int3 mainHaloCoord,
    M8OverlapSignature signature, int3 chartNormal, int3 tangent0,
    int3 tangent1, int tangentSign0, int tangentSign1)
{
    int4 heights = int4(0, 0, 0, 0);
    uint4 stateTokens = uint4(0u, 0u, 0u, 0u);
    uint count = 0u;
    M8CollectOverlapContributor(globalCoord, mainHaloCoord, signature,
        chartNormal, int3(0, 0, 0), heights, stateTokens, count);
    M8CollectOverlapContributor(globalCoord, mainHaloCoord, signature,
        chartNormal, tangent0 * tangentSign0, heights, stateTokens, count);
    M8CollectOverlapContributor(globalCoord, mainHaloCoord, signature,
        chartNormal, tangent1 * tangentSign1, heights, stateTokens, count);
    M8CollectOverlapContributor(globalCoord, mainHaloCoord, signature,
        chartNormal, tangent0 * tangentSign0 + tangent1 * tangentSign1,
        heights, stateTokens, count);

    int2 heightBand = M8OverlapMedianBand(heights, count);
    int lowerHeight = heightBand.x;
    int upperHeight = heightBand.y;
    M8OverlapCorner corner = (M8OverlapCorner)0;
    corner.quarterCoordinate = globalCoord * M8_OVERLAP_QUARTERS_PER_STEP +
        tangent0 * (tangentSign0 * M8_OVERLAP_SUPPORT_HALF_QUARTERS) +
        tangent1 * (tangentSign1 * M8_OVERLAP_SUPPORT_HALF_QUARTERS);
    M8SetOverlapAxisValue(corner.quarterCoordinate, signature.chartAxis,
        2 * (lowerHeight + upperHeight));
    corner.packedColor = M8ReduceOverlapColor(heights, stateTokens, count,
        lowerHeight, upperHeight);
    return corner;
}

M8OverlapPatch M8BuildOverlapPatchFromSignature(int3 globalCoord,
    int3 mainHaloCoord, M8OverlapSignature signature)
{
    int3 chartNormal;
    int3 tangent0;
    int3 tangent1;
    M8OverlapAxes(signature.chartAxis, chartNormal, tangent0, tangent1);
    M8OverlapPatch patch = (M8OverlapPatch)0;
    patch.signature = signature;
    patch.corner00 = M8BuildOverlapCorner(globalCoord, mainHaloCoord,
        signature, chartNormal, tangent0, tangent1, -1, -1);
    patch.corner10 = M8BuildOverlapCorner(globalCoord, mainHaloCoord,
        signature, chartNormal, tangent0, tangent1, 1, -1);
    patch.corner11 = M8BuildOverlapCorner(globalCoord, mainHaloCoord,
        signature, chartNormal, tangent0, tangent1, 1, 1);
    patch.corner01 = M8BuildOverlapCorner(globalCoord, mainHaloCoord,
        signature, chartNormal, tangent0, tangent1, -1, 1);
    return patch;
}

M8OverlapCorner M8OverlapPatchCorner(M8OverlapPatch patch, uint index)
{
    M8OverlapCorner corner = patch.corner01;
    if (index == 0u) corner = patch.corner00;
    else if (index == 1u) corner = patch.corner10;
    else if (index == 2u) corner = patch.corner11;
    return corner;
}

uint M8OverlapTriangleCorner(int freeSign, uint vertex)
{
    bool forward = freeSign > 0;
    if (vertex == 0u) return 0u;
    if (vertex == 1u) return forward ? 1u : 2u;
    if (vertex == 2u) return forward ? 2u : 1u;
    if (vertex == 3u) return 0u;
    if (vertex == 4u) return forward ? 2u : 3u;
    return forward ? 3u : 2u;
}

@HASH@endif
";
            return template
                .Replace("@HASH@", "#")
                .Replace("@QUARTERS@", QuarterUnitsPerLatticeStep.ToString())
                .Replace("@SUPPORT_HALF@", SupportHalfQuarterUnits.ToString())
                .Replace("@TRIANGLES@", TrianglesPerPatch.ToString());
        }
#endif
    }
}
