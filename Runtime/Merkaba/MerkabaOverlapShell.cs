using System;
using System.Collections.Generic;
using System.Text;
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
        internal const int HalfStepQuarterUnits = 2;
        internal const int CornersPerPatch = 4;
        internal const int TrianglesPerPatch = 2;
        internal const int VerticesPerPatch = 6;

        internal readonly struct Signature : IEquatable<Signature>
        {
            internal readonly byte NormalAxis;
            internal readonly sbyte FreeSign;
            internal readonly bool HasKnownFreeSide;

            internal Signature(int normalAxis, int freeSign,
                bool hasKnownFreeSide)
            {
                NormalAxis = (byte)normalAxis;
                FreeSign = (sbyte)freeSign;
                HasKnownFreeSide = hasKnownFreeSide;
            }

            public bool Equals(Signature other) =>
                NormalAxis == other.NormalAxis &&
                FreeSign == other.FreeSign &&
                HasKnownFreeSide == other.HasKnownFreeSide;

            public override bool Equals(object obj) =>
                obj is Signature other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(
                NormalAxis, FreeSign, HasKnownFreeSide);
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

            Signature signature = DeriveSignature(window);
            Axes(signature.NormalAxis, out int3 normal,
                out int3 tangent0, out int3 tangent1);
            Corner corner00 = BuildCorner(window, signature, normal,
                tangent0, tangent1, -1, -1);
            Corner corner10 = BuildCorner(window, signature, normal,
                tangent0, tangent1, 1, -1);
            Corner corner11 = BuildCorner(window, signature, normal,
                tangent0, tangent1, 1, 1);
            Corner corner01 = BuildCorner(window, signature, normal,
                tangent0, tangent1, -1, 1);
            patch = new Patch(main, signature, corner00, corner10,
                corner11, corner01);
            return true;
        }

        internal static Signature DeriveSignature(int3 main,
            Func<int3, KernelState> sample)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            return DeriveSignature(new LocalWindow(main, sample));
        }

        private static Signature DeriveSignature(LocalWindow window)
        {
            int3 freeMoment = default;
            int3 occupiedCrossings = default;
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                int3 offset = new(x, y, z);
                if (math.all(offset == 0)) continue;
                KernelState state = window.At(offset);
                if (state.OccupancyEvidence < 0)
                    freeMoment += offset;
                if (state.IsOccupied)
                    occupiedCrossings += math.abs(offset);
            }

            // Occupied neighbours choose only the height-field chart. FREE
            // evidence chooses its side; an unevenly observed FREE layer must
            // never rotate the tangent lattice at a sheet edge.
            int chartAxis = MinimumAxis(occupiedCrossings);
            int signedFree = freeMoment[chartAxis];
            bool knownFree = signedFree != 0;
            return new Signature(chartAxis, knownFree && signedFree < 0
                ? -1 : 1, knownFree);
        }

        private static Corner BuildCorner(LocalWindow window,
            Signature signature, int3 normal, int3 tangent0, int3 tangent1,
            int tangentSign0, int tangentSign1)
        {
            var contributors = new List<Contributor>(4);
            for (int v = 0; v <= 1; v++)
            for (int u = 0; u <= 1; u++)
            {
                int3 tangentOffset =
                    (u == 0 ? default : tangent0 * tangentSign0) +
                    (v == 0 ? default : tangent1 * tangentSign1);
                if (TrySelectColumnContributor(window, signature, normal,
                        tangentOffset, out Contributor contributor))
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
                tangent0 * (tangentSign0 * HalfStepQuarterUnits) +
                tangent1 * (tangentSign1 * HalfStepQuarterUnits);
            quarter[signature.NormalAxis] = quarterHeight;

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
            Signature signature, int3 normal, int3 tangentOffset,
            out Contributor selected)
        {
            selected = default;
            bool found = false;
            int columnFreeMoment = 0;
            for (int normalOffset = -1; normalOffset <= 1; normalOffset++)
            {
                KernelState state = window.At(tangentOffset +
                    normal * normalOffset);
                if (state.OccupancyEvidence < 0)
                    columnFreeMoment += normalOffset;
            }
            bool mainColumn = math.all(tangentOffset == 0);
            if (!mainColumn && signature.HasKnownFreeSide &&
                columnFreeMoment != 0 &&
                Math.Sign(columnFreeMoment) != signature.FreeSign)
                return false;

            for (int normalOffset = -1; normalOffset <= 1; normalOffset++)
            {
                int3 offset = tangentOffset + normal * normalOffset;
                KernelState state = window.At(offset);
                // Immediate +/-1 columns have no intervening lattice site;
                // a FREE separator therefore cannot be hidden between MAIN
                // and this candidate.
                if (!state.IsOccupied) continue;
                var candidate = new Contributor(window.Global(offset), state,
                    window.Global(offset)[signature.NormalAxis]);
                if (!found || Math.Abs(normalOffset) < Math.Abs(
                        selected.NormalCoordinate -
                        window.Global(default)[signature.NormalAxis]) ||
                    (Math.Abs(normalOffset) == Math.Abs(
                        selected.NormalCoordinate -
                        window.Global(default)[signature.NormalAxis]) &&
                     MerkabaConstants.LexicographicallyLess(candidate.Coord,
                         selected.Coord)))
                {
                    selected = candidate;
                    found = true;
                }
            }
            return found;
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

        private static int MinimumAxis(int3 value)
        {
            if (value.x <= value.y && value.x <= value.z) return 0;
            return value.y <= value.z ? 1 : 2;
        }

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
            var text = new StringBuilder(5000);
            text.Append("// GENERATED from MerkabaOverlapShell.cs. DO NOT EDIT.\n")
                .Append("#ifndef GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED\n")
                .Append("#define GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED\n\n")
                .Append("#define M8_OVERLAP_QUARTERS_PER_STEP ")
                .Append(QuarterUnitsPerLatticeStep).Append("\n")
                .Append("#define M8_OVERLAP_HALF_STEP_QUARTERS ")
                .Append(HalfStepQuarterUnits).Append("\n")
                .Append("#define M8_OVERLAP_TRIANGLES_PER_PATCH ")
                .Append(TrianglesPerPatch).Append("u\n\n")
                .Append("struct M8OverlapSignature\n{\n")
                .Append("    uint normalAxis;\n    int freeSign;\n")
                .Append("    uint hasKnownFreeSide;\n};\n\n")
                .Append("struct M8OverlapCorner\n{\n")
                .Append("    int3 quarterCoordinate;\n")
                .Append("    uint packedColor;\n};\n\n")
                .Append("void M8OverlapAxes(uint normalAxis, out int3 normal, ")
                .Append("out int3 tangent0, out int3 tangent1)\n{\n")
                .Append("    if (normalAxis == 0u)\n    {\n")
                .Append("        normal = int3(1, 0, 0);\n")
                .Append("        tangent0 = int3(0, 1, 0);\n")
                .Append("        tangent1 = int3(0, 0, 1);\n    }\n")
                .Append("    else if (normalAxis == 1u)\n    {\n")
                .Append("        normal = int3(0, 1, 0);\n")
                .Append("        tangent0 = int3(0, 0, 1);\n")
                .Append("        tangent1 = int3(1, 0, 0);\n    }\n")
                .Append("    else\n    {\n")
                .Append("        normal = int3(0, 0, 1);\n")
                .Append("        tangent0 = int3(1, 0, 0);\n")
                .Append("        tangent1 = int3(0, 1, 0);\n    }\n}\n\n")
                .Append("uint M8OverlapMinimumAxis(int3 value)\n{\n")
                .Append("    if (value.x <= value.y && value.x <= value.z) ")
                .Append("return 0u;\n")
                .Append("    return value.y <= value.z ? 1u : 2u;\n}\n\n")
                .Append("void M8OverlapMedianBand(int4 values, uint count, ")
                .Append("out int lower, out int upper)\n{\n")
                .Append("    if (count == 1u)\n    {\n")
                .Append("        lower = upper = values.x;\n        return;\n    }\n")
                .Append("    if (count == 2u)\n    {\n")
                .Append("        lower = min(values.x, values.y);\n")
                .Append("        upper = max(values.x, values.y);\n        return;\n    }\n")
                .Append("    if (count == 3u)\n    {\n")
                .Append("        int median = values.x + values.y + values.z - ")
                .Append("min(values.x, min(values.y, values.z)) - ")
                .Append("max(values.x, max(values.y, values.z));\n")
                .Append("        lower = upper = median;\n        return;\n    }\n")
                .Append("    int x = values.x; int y = values.y;\n")
                .Append("    int z = values.z; int w = values.w; int swap;\n")
                .Append("    if (x > y) { swap = x; x = y; y = swap; }\n")
                .Append("    if (z > w) { swap = z; z = w; w = swap; }\n")
                .Append("    if (x > z) { swap = x; x = z; z = swap; }\n")
                .Append("    if (y > w) { swap = y; y = w; w = swap; }\n")
                .Append("    if (y > z) { swap = y; y = z; z = swap; }\n")
                .Append("    lower = y; upper = z;\n}\n\n")
                .Append("int M8OverlapMedianQuarterHeight(int4 values, ")
                .Append("uint count)\n{\n")
                .Append("    int lower; int upper;\n")
                .Append("    M8OverlapMedianBand(values, count, lower, upper);\n")
                .Append("    return 2 * (lower + upper);\n}\n\n")
                .Append("uint M8OverlapTriangleCorner(int freeSign, ")
                .Append("uint vertex)\n{\n")
                .Append("    bool forward = freeSign > 0;\n")
                .Append("    switch (vertex)\n    {\n");
            for (int vertex = 0; vertex < VerticesPerPatch; vertex++)
                text.Append("        case ").Append(vertex).Append("u: return forward ? ")
                    .Append(ForwardOrder[vertex]).Append("u : ")
                    .Append(ReverseOrder[vertex]).Append("u;\n");
            return text.Append("        default: return 0u;\n    }\n}\n\n")
                .Append("#endif\n").ToString();
        }
#endif
    }
}
