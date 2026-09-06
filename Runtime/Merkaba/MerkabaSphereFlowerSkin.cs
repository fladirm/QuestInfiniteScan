using System;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Exact REV-C planar skin oracle. Geometry has already terminated at L2;
    /// this partial contains only immutable address/stitch tables and pure
    /// signal math shared with generated HLSL.
    /// </summary>
    public static partial class MerkabaSphereFlowerAuthority
    {
        public const int SkinSiteCount = 7;
        public const int SkinWedgeCount = 6;
        public const int SkinOrderCount = 6;
        public const int SkinChamberCount = SkinWedgeCount * SkinOrderCount;
        public const int SkinL3Count = 7;
        public const int SkinL4Count = 49;
        public const int SkinL5Count = 343;
        public const int SkinThreadPositionCount = 399;
        public const int SkinSplitBitCount = 57;
        public const int SkinStitchStateCount = 12;

#if UNITY_EDITOR
        private static readonly int[] FibonacciWordLengths =
        {
            1, 2, 3, 5, 8, 13, 21, 34, 55, 89,
            144, 233, 377, 610, 987, 1597, 2584
        };
#endif

        public enum SplitClassification : byte
        {
            Uniform = 0,
            Split = 1,
            Ambiguous = 2
        }

        public readonly struct SkinChamberRule
        {
            public readonly byte High;
            public readonly byte Middle;
            public readonly byte Low;
            public readonly byte ChildSite;
            public readonly byte ChildWedge;
            public readonly byte NextStitchState;
            public readonly sbyte Orientation;

            internal SkinChamberRule(byte high, byte middle, byte low,
                byte childSite, byte childWedge, byte nextStitchState,
                sbyte orientation)
            {
                High = high;
                Middle = middle;
                Low = low;
                ChildSite = childSite;
                ChildWedge = childWedge;
                NextStitchState = nextStitchState;
                Orientation = orientation;
            }
        }

        private static class SkinData
        {
            internal static readonly SkinChamberRule[] Chambers;
            internal static readonly ushort[] CanonicalToThread;
            internal static readonly ushort[] ThreadToCanonical;
            internal static readonly ushort[] CanonicalL5ToThread;
            internal static readonly byte[] L3ChildRank;
            internal static readonly byte[] L3State;
            internal static readonly byte[] L4ParentThread;
            internal static readonly byte[] L4ChildRank;
            internal static readonly byte[] L4State;
            internal static readonly byte[] L5ChildRank;

            static SkinData()
            {
#if UNITY_EDITOR
                Chambers = BuildSkinChambers();
                BuildSkinThread(out CanonicalToThread, out ThreadToCanonical,
                    out CanonicalL5ToThread, out L3ChildRank, out L3State,
                    out L4ParentThread, out L4ChildRank, out L4State,
                    out L5ChildRank);
#else
                LoadGeneratedSkinTables(out Chambers, out CanonicalToThread,
                    out ThreadToCanonical, out CanonicalL5ToThread,
                    out L3ChildRank, out L3State, out L4ParentThread,
                    out L4ChildRank, out L4State, out L5ChildRank);
#endif
                ValidateSkinTables();
            }
        }

        public static ReadOnlySpan<SkinChamberRule> SkinChambers =>
            SkinData.Chambers;
        public static ReadOnlySpan<ushort> SkinCanonicalToThread =>
            SkinData.CanonicalToThread;
        public static ReadOnlySpan<ushort> SkinThreadToCanonical =>
            SkinData.ThreadToCanonical;
        public static ReadOnlySpan<ushort> SkinCanonicalL5ToThread =>
            SkinData.CanonicalL5ToThread;
        public static ReadOnlySpan<byte> SkinL3ChildRank =>
            SkinData.L3ChildRank;
        public static ReadOnlySpan<byte> SkinL3State => SkinData.L3State;
        public static ReadOnlySpan<byte> SkinL4ParentThread =>
            SkinData.L4ParentThread;
        public static ReadOnlySpan<byte> SkinL4ChildRank =>
            SkinData.L4ChildRank;
        public static ReadOnlySpan<byte> SkinL4State => SkinData.L4State;
        public static ReadOnlySpan<byte> SkinL5ChildRank =>
            SkinData.L5ChildRank;

        public static int SkinCanonicalIndex(int level, int c3,
            int c4 = 0, int c5 = 0)
        {
            ValidateSkinChild(c3, nameof(c3));
            if (level == 3) return c3;
            ValidateSkinChild(c4, nameof(c4));
            if (level == 4) return 7 + 7 * c3 + c4;
            ValidateSkinChild(c5, nameof(c5));
            if (level == 5) return 56 + 49 * c3 + 7 * c4 + c5;
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        public static int SkinTerminalCanonicalIndex(int c3, int c4,
            int c5)
        {
            ValidateSkinChild(c3, nameof(c3));
            ValidateSkinChild(c4, nameof(c4));
            ValidateSkinChild(c5, nameof(c5));
            return 49 * c3 + 7 * c4 + c5;
        }

        public static int SkinThreadPosition(int level, int c3,
            int c4 = 0, int c5 = 0) =>
            SkinData.CanonicalToThread[SkinCanonicalIndex(level, c3, c4, c5)];

        public static int SkinL3ParentThreadIndex(int c3)
        {
            ValidateSkinChild(c3, nameof(c3));
            return SkinData.L3ChildRank[c3];
        }

        public static int SkinL4ParentThreadIndex(int c3, int c4)
        {
            ValidateSkinChild(c3, nameof(c3));
            ValidateSkinChild(c4, nameof(c4));
            return SkinData.L4ParentThread[7 * c3 + c4];
        }

        public static int SkinL4CompactChildRank(int c3, int c4)
        {
            ValidateSkinChild(c3, nameof(c3));
            ValidateSkinChild(c4, nameof(c4));
            return SkinData.L4ChildRank[7 * c3 + c4];
        }

        public static int SkinL5CompactChildRank(int c3, int c4, int c5)
        {
            int canonical = SkinTerminalCanonicalIndex(c3, c4, c5);
            return SkinData.L5ChildRank[canonical];
        }

        public static byte SkinStableOrder(float3 barycentric, int wedge)
        {
            ValidateSkinWedge(wedge);
            if (!math.all(math.isfinite(barycentric)))
                throw new ArgumentOutOfRangeException(nameof(barycentric));
            int3 sites = SkinWedgeSites(wedge);
            int high = 0;
            int middle = 1;
            int low = 2;
            SortBefore(ref high, ref middle, barycentric, sites);
            SortBefore(ref middle, ref low, barycentric, sites);
            SortBefore(ref high, ref middle, barycentric, sites);
            return EncodeOrder(high, middle, low);
        }

        public static int ClassifySkinChild(float3 barycentric, int wedge)
        {
            byte order = SkinStableOrder(barycentric, wedge);
            return SkinData.Chambers[wedge * SkinOrderCount + order].ChildSite;
        }

        public static SkinChamberRule DescendSkin(float3 barycentric,
            int wedge, out float3 childBarycentric)
        {
            byte order = SkinStableOrder(barycentric, wedge);
            SkinChamberRule rule =
                SkinData.Chambers[wedge * SkinOrderCount + order];
            float x0 = barycentric[rule.High];
            float x1 = barycentric[rule.Middle];
            float x2 = barycentric[rule.Low];
            childBarycentric = new float3(x0 - x1,
                2f * (x1 - x2), 3f * x2);
            return rule;
        }

        public static bool IntervalsDisjoint(FloatInterval left,
            FloatInterval right) => left.Upper < right.Lower ||
            right.Upper < left.Lower;

        public static SplitClassification ClassifyScalarSkinSplit(
            ReadOnlySpan<FloatInterval> childIntervals, uint certainMask)
        {
            if (childIntervals.Length != SkinSiteCount)
                throw new ArgumentException("Exactly seven intervals required.",
                    nameof(childIntervals));
            if ((certainMask & 0x7fu) != 0x7fu)
                return SplitClassification.Ambiguous;
            for (int i = 0; i < SkinSiteCount; i++)
            for (int j = i + 1; j < SkinSiteCount; j++)
                if (IntervalsDisjoint(childIntervals[i], childIntervals[j]))
                    return SplitClassification.Split;
            return SplitClassification.Uniform;
        }

        public static SplitClassification ClassifyRgbSkinSplit(
            ReadOnlySpan<FloatInterval> childRgbIntervals, uint certainMask)
        {
            if (childRgbIntervals.Length != SkinSiteCount * 3)
                throw new ArgumentException("Exactly seven RGB intervals required.",
                    nameof(childRgbIntervals));
            if ((certainMask & 0x7fu) != 0x7fu)
                return SplitClassification.Ambiguous;
            for (int i = 0; i < SkinSiteCount; i++)
            for (int j = i + 1; j < SkinSiteCount; j++)
            for (int channel = 0; channel < 3; channel++)
                if (IntervalsDisjoint(childRgbIntervals[3 * i + channel],
                        childRgbIntervals[3 * j + channel]))
                    return SplitClassification.Split;
            return SplitClassification.Uniform;
        }

        public static int Rank7(uint mask, int bit)
        {
            if ((uint)bit > 7u) throw new ArgumentOutOfRangeException(nameof(bit));
            uint below = bit == 0 ? 0u : (1u << bit) - 1u;
            return math.countbits(mask & below & 0x7fu);
        }

        public static int Rank49(uint low, uint high, int bit)
        {
            if ((uint)bit > 49u)
                throw new ArgumentOutOfRangeException(nameof(bit));
            high &= 0x1ffffu;
            if (bit <= 32)
            {
                uint below = bit == 0 ? 0u : bit == 32
                    ? uint.MaxValue : (1u << bit) - 1u;
                return math.countbits(low & below);
            }
            int highBit = bit - 32;
            uint highBelow = highBit == 0 ? 0u : (1u << highBit) - 1u;
            return math.countbits(low) + math.countbits(high & highBelow);
        }

        public static bool ValidateSkinSplitClosure(bool splitL2,
            uint splitL3Thread, uint splitL4Low, uint splitL4High)
        {
            if ((splitL3Thread & ~0x7fu) != 0u ||
                (splitL4High & ~0x1ffffu) != 0u)
                return false;
            splitL3Thread &= 0x7fu;
            splitL4High &= 0x1ffffu;
            if (!splitL2)
                return splitL3Thread == 0u && splitL4Low == 0u &&
                    splitL4High == 0u;
            for (int parent = 0; parent < SkinL3Count; parent++)
            {
                if ((splitL3Thread & (1u << parent)) != 0u) continue;
                for (int child = 0; child < SkinSiteCount; child++)
                {
                    int bit = 7 * parent + child;
                    uint value = bit < 32 ? splitL4Low : splitL4High;
                    int shift = bit < 32 ? bit : bit - 32;
                    if ((value & (1u << shift)) != 0u) return false;
                }
            }
            return true;
        }

        public static int CompactSkinValueCount(bool splitL2,
            uint splitL3Thread, uint splitL4Low, uint splitL4High)
        {
            if (!ValidateSkinSplitClosure(splitL2, splitL3Thread,
                    splitL4Low, splitL4High))
                throw new ArgumentException("Orphan or out-of-range skin split.");
            if (!splitL2) return 1;
            return 8 + 7 * math.countbits(splitL3Thread & 0x7fu) +
                7 * (math.countbits(splitL4Low) +
                    math.countbits(splitL4High & 0x1ffffu));
        }

        public static float SkinBubble(float3 barycentric)
        {
            float p = barycentric.x * barycentric.y * barycentric.z;
            return 729f * p * p;
        }

        public static float3 SkinBubbleGradient(float3 barycentric) => new(
            1458f * barycentric.x * barycentric.y * barycentric.y *
                barycentric.z * barycentric.z,
            1458f * barycentric.y * barycentric.x * barycentric.x *
                barycentric.z * barycentric.z,
            1458f * barycentric.z * barycentric.x * barycentric.x *
                barycentric.y * barycentric.y);

        public static float EvaluateNestedSkinV(float3 child3,
            float3 child4, float3 child5, float3 amplitudes) =>
            amplitudes.x * SkinBubble(child3) +
            amplitudes.y * SkinBubble(child4) +
            amplitudes.z * SkinBubble(child5);

#if UNITY_EDITOR
        public static int FibonacciBitForCodegen(int ordinal)
        {
            if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
            int k = 0;
            while (k < FibonacciWordLengths.Length &&
                   FibonacciWordLengths[k] <= ordinal) k++;
            if (k == FibonacciWordLengths.Length)
                throw new ArgumentOutOfRangeException(nameof(ordinal));
            int n = ordinal;
            while (k > 1)
            {
                if (n < FibonacciWordLengths[k - 1]) k--;
                else
                {
                    n -= FibonacciWordLengths[k - 1];
                    k -= 2;
                }
            }
            return k == 0 || n == 0 ? 0 : 1;
        }
#endif

        public static bool SkinSitesAdjacent(int left, int right)
        {
            ValidateSkinChild(left, nameof(left));
            ValidateSkinChild(right, nameof(right));
            if (left == right) return false;
            if (left == 0 || right == 0) return true;
            int a = left - 1;
            int b = right - 1;
            int delta = Mod(a - b, SkinWedgeCount);
            return delta == 1 || delta == SkinWedgeCount - 1;
        }

#if UNITY_EDITOR
        private static SkinChamberRule[] BuildSkinChambers()
        {
            int3[] orders =
            {
                new(0, 1, 2), new(0, 2, 1), new(1, 0, 2),
                new(1, 2, 0), new(2, 0, 1), new(2, 1, 0)
            };
            var output = new SkinChamberRule[SkinChamberCount];
            for (int wedge = 0; wedge < SkinWedgeCount; wedge++)
            {
                int3 sites = SkinWedgeSites(wedge);
                for (int order = 0; order < SkinOrderCount; order++)
                {
                    int high = orders[order].x;
                    int middle = orders[order].y;
                    int low = orders[order].z;
                    int child = sites[high];
                    int childWedge;
                    if (child == 0) childWedge = wedge;
                    else
                    {
                        int ring = child - 1;
                        int nextRingSite = Mod(ring + 1, SkinWedgeCount) + 1;
                        bool forward = sites[middle] == nextRingSite ||
                            (sites[middle] == 0 && sites[low] == nextRingSite);
                        childWedge = forward ? ring :
                            Mod(ring - 1, SkinWedgeCount);
                    }
                    sbyte orientation = (sbyte)(PermutationParity(
                        high, middle, low) > 0 ? 1 : -1);
                    int state = 2 * childWedge + (orientation < 0 ? 1 : 0);
                    output[wedge * SkinOrderCount + order] =
                        new SkinChamberRule((byte)high, (byte)middle,
                            (byte)low, (byte)child, (byte)childWedge,
                            (byte)state, orientation);
                }
            }
            return output;
        }

        private static void BuildSkinThread(out ushort[] canonicalToThread,
            out ushort[] threadToCanonical,
            out ushort[] canonicalL5ToThread, out byte[] l3ChildRank,
            out byte[] l3State, out byte[] l4ParentThread,
            out byte[] l4ChildRank, out byte[] l4State,
            out byte[] l5ChildRank)
        {
            canonicalToThread = new ushort[SkinThreadPositionCount];
            threadToCanonical = new ushort[SkinThreadPositionCount];
            canonicalL5ToThread = new ushort[SkinL5Count];
            l3ChildRank = new byte[SkinL3Count];
            l3State = new byte[SkinL3Count];
            l4ParentThread = new byte[SkinL4Count];
            l4ChildRank = new byte[SkinL4Count];
            l4State = new byte[SkinL4Count];
            l5ChildRank = new byte[SkinL5Count];
            for (int i = 0; i < threadToCanonical.Length; i++)
                threadToCanonical[i] = ushort.MaxValue;

            byte rootState = 0;
            byte[] order3 = StitchOrder(rootState,
                FibonacciBitForCodegen(0));
            for (int c3 = 0; c3 < SkinSiteCount; c3++)
            {
                int r3 = FindRank(order3, c3);
                int t3 = 57 * r3;
                int i3 = SkinCanonicalIndex(3, c3);
                AssignThread(canonicalToThread, threadToCanonical, i3, t3);
                l3ChildRank[c3] = (byte)r3;
                byte state3 = ChildStitchState(rootState,
                    FibonacciBitForCodegen(0), r3, c3);
                l3State[c3] = state3;
                int variant4 = FibonacciBitForCodegen(t3);
                byte[] order4 = StitchOrder(state3, variant4);
                for (int c4 = 0; c4 < SkinSiteCount; c4++)
                {
                    int r4 = FindRank(order4, c4);
                    int p4 = 7 * c3 + c4;
                    int j4 = 7 * r3 + r4;
                    int t4 = t3 + 1 + 8 * r4;
                    int i4 = SkinCanonicalIndex(4, c3, c4);
                    AssignThread(canonicalToThread, threadToCanonical, i4, t4);
                    l4ParentThread[p4] = (byte)j4;
                    l4ChildRank[p4] = (byte)r4;
                    byte state4 = ChildStitchState(state3, variant4, r4, c4);
                    l4State[p4] = state4;
                    int variant5 = FibonacciBitForCodegen(t4);
                    byte[] order5 = StitchOrder(state4, variant5);
                    for (int c5 = 0; c5 < SkinSiteCount; c5++)
                    {
                        int r5 = FindRank(order5, c5);
                        int terminal = SkinTerminalCanonicalIndex(c3, c4, c5);
                        int t5 = t4 + 1 + r5;
                        int i5 = SkinCanonicalIndex(5, c3, c4, c5);
                        AssignThread(canonicalToThread, threadToCanonical,
                            i5, t5);
                        canonicalL5ToThread[terminal] = (ushort)t5;
                        l5ChildRank[terminal] = (byte)r5;
                    }
                }
            }
        }

        private static byte[] StitchOrder(int state, int variant)
        {
            if ((uint)state >= SkinStitchStateCount)
                throw new ArgumentOutOfRangeException(nameof(state));
            if ((uint)variant > 1u)
                throw new ArgumentOutOfRangeException(nameof(variant));
            int[] source = variant == 0
                ? new[] { 1, 0, 2, 3, 4, 5, 6 }
                : new[] { 1, 2, 3, 4, 5, 0, 6 };
            var order = new byte[SkinSiteCount];
            for (int i = 0; i < order.Length; i++)
                order[i] = (byte)MapStitchSite(state, source[i]);
            for (int i = 1; i < order.Length; i++)
                if (!SkinSitesAdjacent(order[i - 1], order[i]))
                    throw new InvalidOperationException(
                        "Generated stitch contains a non-adjacent transition.");
            return order;
        }

        private static int MapStitchSite(int state, int site)
        {
            if (site == 0) return 0;
            int rotation = state >> 1;
            bool reflected = (state & 1) != 0;
            int ring = site - 1;
            return Mod(rotation + (reflected ? -ring : ring),
                SkinWedgeCount) + 1;
        }

        private static byte ChildStitchState(int parentState, int variant,
            int rank, int childSite)
        {
            int parentRotation = parentState >> 1;
            bool reflected = (parentState & 1) != 0;
            int rotation = childSite == 0
                ? Mod(parentRotation + rank, SkinWedgeCount)
                : childSite - 1;
            bool childReflected = reflected ^ (variant != 0) ^
                ((rank & 1) != 0);
            return (byte)(2 * rotation + (childReflected ? 1 : 0));
        }
#endif

        private static void ValidateSkinTables()
        {
            if (SkinData.Chambers.Length != SkinChamberCount ||
                SkinData.CanonicalToThread.Length != SkinThreadPositionCount ||
                SkinData.ThreadToCanonical.Length != SkinThreadPositionCount ||
                SkinData.CanonicalL5ToThread.Length != SkinL5Count ||
                SkinData.L3ChildRank.Length != SkinL3Count ||
                SkinData.L3State.Length != SkinL3Count ||
                SkinData.L4ParentThread.Length != SkinL4Count ||
                SkinData.L4ChildRank.Length != SkinL4Count ||
                SkinData.L4State.Length != SkinL4Count ||
                SkinData.L5ChildRank.Length != SkinL5Count)
                throw new InvalidOperationException(
                    "Generated fixed-thread cardinality is invalid.");

            int[] footprintCount = new int[SkinSiteCount];
            for (int wedge = 0; wedge < SkinWedgeCount; wedge++)
            {
                bool[] orders = new bool[27];
                int3 sites = SkinWedgeSites(wedge);
                for (int order = 0; order < SkinOrderCount; order++)
                {
                    SkinChamberRule rule = SkinData.Chambers[
                        wedge * SkinOrderCount + order];
                    int encoded = 9 * rule.High + 3 * rule.Middle + rule.Low;
                    if ((uint)rule.High > 2u || (uint)rule.Middle > 2u ||
                        (uint)rule.Low > 2u || orders[encoded] ||
                        rule.ChildSite != sites[rule.High] ||
                        rule.ChildSite >= SkinSiteCount ||
                        rule.ChildWedge >= SkinWedgeCount ||
                        rule.NextStitchState >= SkinStitchStateCount ||
                        (rule.Orientation != -1 && rule.Orientation != 1))
                        throw new InvalidOperationException(
                            "Generated skin chamber is not canonical.");
                    orders[encoded] = true;
                    footprintCount[rule.ChildSite]++;
                }
            }
            if (footprintCount[0] != 12)
                throw new InvalidOperationException(
                    "Generated hub footprint is incomplete.");
            for (int site = 1; site < SkinSiteCount; site++)
                if (footprintCount[site] != 4)
                    throw new InvalidOperationException(
                        "Generated ring footprint is incomplete.");

            for (int thread = 0; thread < SkinThreadPositionCount; thread++)
            {
                int canonical = SkinData.ThreadToCanonical[thread];
                if ((uint)canonical >= SkinThreadPositionCount ||
                    SkinData.CanonicalToThread[canonical] != thread)
                    throw new InvalidOperationException(
                        "Generated fixed thread is not bijective.");
            }
            var seenParents = new bool[SkinL4Count];
            for (int i = 0; i < SkinData.L4ParentThread.Length; i++)
            {
                int parent = SkinData.L4ParentThread[i];
                if ((uint)parent >= SkinL4Count || seenParents[parent])
                    throw new InvalidOperationException(
                        "Generated L4 parent thread order is not bijective.");
                seenParents[parent] = true;
            }

            ValidateLocalStitchRanks(SkinData.L3ChildRank, 0);
            for (int c3 = 0; c3 < SkinL3Count; c3++)
            {
                int r3 = SkinData.L3ChildRank[c3];
                int t3 = 57 * r3;
                if (SkinData.CanonicalToThread[SkinCanonicalIndex(3, c3)] != t3 ||
                    SkinData.L3State[c3] >= SkinStitchStateCount)
                    throw new InvalidOperationException(
                        "Generated L3 subtree is not contiguous.");
                ValidateLocalStitchRanks(SkinData.L4ChildRank, 7 * c3);
                for (int c4 = 0; c4 < SkinSiteCount; c4++)
                {
                    int p4 = 7 * c3 + c4;
                    int r4 = SkinData.L4ChildRank[p4];
                    int t4 = t3 + 1 + 8 * r4;
                    if (SkinData.L4ParentThread[p4] != 7 * r3 + r4 ||
                        SkinData.CanonicalToThread[
                            SkinCanonicalIndex(4, c3, c4)] != t4 ||
                        SkinData.L4State[p4] >= SkinStitchStateCount)
                        throw new InvalidOperationException(
                            "Generated L4 subtree is not contiguous.");
                    ValidateLocalStitchRanks(SkinData.L5ChildRank, 7 * p4);
                    for (int c5 = 0; c5 < SkinSiteCount; c5++)
                    {
                        int terminal = 7 * p4 + c5;
                        int t5 = t4 + 1 + SkinData.L5ChildRank[terminal];
                        if (SkinData.CanonicalL5ToThread[terminal] != t5 ||
                            SkinData.CanonicalToThread[
                                SkinCanonicalIndex(5, c3, c4, c5)] != t5)
                            throw new InvalidOperationException(
                                "Generated L5 thread address is not contiguous.");
                    }
                }
            }
        }

        private static void ValidateLocalStitchRanks(byte[] ranks, int offset)
        {
            int previous = -1;
            for (int rank = 0; rank < SkinSiteCount; rank++)
            {
                int site = -1;
                for (int candidate = 0; candidate < SkinSiteCount; candidate++)
                    if (ranks[offset + candidate] == rank)
                    {
                        if (site >= 0)
                            throw new InvalidOperationException(
                                "Generated stitch rank is duplicated.");
                        site = candidate;
                    }
                if (site < 0 || previous >= 0 &&
                    !SkinSitesAdjacent(previous, site))
                    throw new InvalidOperationException(
                        "Generated stitch is not an adjacent bijection.");
                previous = site;
            }
        }

#if UNITY_EDITOR
        private static void AssignThread(ushort[] canonicalToThread,
            ushort[] threadToCanonical, int canonical, int thread)
        {
            if ((uint)canonical >= canonicalToThread.Length ||
                (uint)thread >= threadToCanonical.Length ||
                threadToCanonical[thread] != ushort.MaxValue)
                throw new InvalidOperationException(
                    "Generated thread position is duplicated.");
            canonicalToThread[canonical] = (ushort)thread;
            threadToCanonical[thread] = (ushort)canonical;
        }
#endif

        private static int3 SkinWedgeSites(int wedge) => new(0, wedge + 1,
            wedge == SkinWedgeCount - 1 ? 1 : wedge + 2);

        private static void SortBefore(ref int left, ref int right,
            float3 values, int3 sites)
        {
            float lv = values[left];
            float rv = values[right];
            if (lv > rv || (lv == rv && sites[left] < sites[right])) return;
            int swap = left;
            left = right;
            right = swap;
        }

        private static byte EncodeOrder(int high, int middle, int low)
        {
            if (high == 0) return (byte)(middle == 1 ? 0 : 1);
            if (high == 1) return (byte)(middle == 0 ? 2 : 3);
            return (byte)(middle == 0 ? 4 : 5);
        }

#if UNITY_EDITOR
        private static int PermutationParity(int a, int b, int c)
        {
            int inversions = (a > b ? 1 : 0) + (a > c ? 1 : 0) +
                (b > c ? 1 : 0);
            return (inversions & 1) == 0 ? 1 : -1;
        }

        private static int FindRank(byte[] order, int site)
        {
            for (int i = 0; i < order.Length; i++)
                if (order[i] == site) return i;
            throw new InvalidOperationException("Stitch site is missing.");
        }
#endif

        private static int Mod(int value, int modulus)
        {
            int remainder = value % modulus;
            return remainder < 0 ? remainder + modulus : remainder;
        }

        private static void ValidateSkinChild(int child, string parameter)
        {
            if ((uint)child >= SkinSiteCount)
                throw new ArgumentOutOfRangeException(parameter);
        }

        private static void ValidateSkinWedge(int wedge)
        {
            if ((uint)wedge >= SkinWedgeCount)
                throw new ArgumentOutOfRangeException(nameof(wedge));
        }
    }
}
