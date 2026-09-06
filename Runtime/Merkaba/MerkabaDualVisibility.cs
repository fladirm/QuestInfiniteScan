using System;
using System.Runtime.InteropServices;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Persistent node values of the sparse negative-volume hierarchy. Missing
    /// nodes are implicitly AllFull. Invalid is reserved for corrupt input and
    /// is never a stored world value.
    /// </summary>
    internal enum MerkabaDualNodeState : byte
    {
        AllFull = 0,
        AllThrough = 1,
        Mixed = 2,
        Invalid = 3
    }

    internal enum MerkabaDualReadResult : byte
    {
        CertainFull = 0,
        CertainThrough = 1,
        MixedResident = 2,
        AmbiguousCold = 3
    }

    internal readonly struct MerkabaDualGenerationAdvance
    {
        internal readonly uint Generation;
        internal readonly bool RequiresTransactionalRebase;

        internal MerkabaDualGenerationAdvance(uint generation,
            bool requiresTransactionalRebase)
        {
            Generation = generation;
            RequiresTransactionalRebase = requiresTransactionalRebase;
        }
    }

    internal static class MerkabaDualGeneration
    {
        internal static MerkabaDualGenerationAdvance AdvanceBlock(
            uint current)
        {
            if (current > MerkabaDualBlockMeta.MaximumGeneration)
                throw new ArgumentOutOfRangeException(nameof(current));
            return current == MerkabaDualBlockMeta.MaximumGeneration
                ? new MerkabaDualGenerationAdvance(1u, true)
                : new MerkabaDualGenerationAdvance(current + 1u, false);
        }

        internal static MerkabaDualGenerationAdvance AdvanceChunk(
            uint current) => current == uint.MaxValue
            ? new MerkabaDualGenerationAdvance(1u, true)
            : new MerkabaDualGenerationAdvance(current + 1u, false);
    }

    /// <summary>Eight-byte sparse block header; it reuses the M8 block key.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaDualBlockMeta
    {
        internal const int ByteSize = 8;
        internal const uint NoPayload = uint.MaxValue;
        internal const uint MaximumGeneration = 0x3fffffffu;

        internal uint StateAndGeneration;
        internal uint PayloadIndex;

        internal readonly MerkabaDualNodeState State =>
            (MerkabaDualNodeState)(StateAndGeneration & 3u);

        internal readonly uint Generation => StateAndGeneration >> 2;

        internal static MerkabaDualBlockMeta Create(
            MerkabaDualNodeState state, uint generation, uint payloadIndex)
        {
            ValidateState(state);
            if (generation == 0u || generation > MaximumGeneration)
                throw new ArgumentOutOfRangeException(nameof(generation));
            if ((state == MerkabaDualNodeState.Mixed) !=
                (payloadIndex != NoPayload))
                throw new ArgumentException(
                    "Only a MIXED dual block owns a child payload.",
                    nameof(payloadIndex));
            return new MerkabaDualBlockMeta
            {
                StateAndGeneration = (generation << 2) | (uint)state,
                PayloadIndex = payloadIndex
            };
        }

        internal readonly bool IsCanonical =>
            State != MerkabaDualNodeState.Invalid && Generation != 0u &&
            ((State == MerkabaDualNodeState.Mixed) ==
             (PayloadIndex != NoPayload));

        internal static void ValidateState(MerkabaDualNodeState state)
        {
            if (state == MerkabaDualNodeState.Invalid ||
                (uint)state > (uint)MerkabaDualNodeState.Mixed)
                throw new ArgumentOutOfRangeException(nameof(state));
        }
    }

    /// <summary>
    /// The 512 two-bit chunk states of one MIXED block. Child payload identity
    /// remains the existing M8 block/chunk address; this is not an index.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaDualBlockChildren
    {
        internal const int ChildCount = MerkabaSpatial.BlockChunkCount;
        internal const int WordCount = 32;
        internal const int ByteSize = 128;
        private const uint AllThroughWord = 0x55555555u;

        internal uint Word00; internal uint Word01;
        internal uint Word02; internal uint Word03;
        internal uint Word04; internal uint Word05;
        internal uint Word06; internal uint Word07;
        internal uint Word08; internal uint Word09;
        internal uint Word10; internal uint Word11;
        internal uint Word12; internal uint Word13;
        internal uint Word14; internal uint Word15;
        internal uint Word16; internal uint Word17;
        internal uint Word18; internal uint Word19;
        internal uint Word20; internal uint Word21;
        internal uint Word22; internal uint Word23;
        internal uint Word24; internal uint Word25;
        internal uint Word26; internal uint Word27;
        internal uint Word28; internal uint Word29;
        internal uint Word30; internal uint Word31;

        internal static MerkabaDualBlockChildren CreateUniform(
            MerkabaDualNodeState state)
        {
            if (state != MerkabaDualNodeState.AllFull &&
                state != MerkabaDualNodeState.AllThrough)
                throw new ArgumentOutOfRangeException(nameof(state));
            var result = new MerkabaDualBlockChildren();
            uint word = state == MerkabaDualNodeState.AllThrough
                ? AllThroughWord : 0u;
            for (int i = 0; i < WordCount; i++) result.SetWord(i, word);
            return result;
        }

        internal readonly bool IsCanonical
        {
            get
            {
                for (int child = 0; child < ChildCount; child++)
                    if (Get(child) == MerkabaDualNodeState.Invalid)
                        return false;
                return true;
            }
        }

        internal readonly MerkabaDualNodeState Get(int child)
        {
            ValidateChild(child);
            uint word = GetWord(child >> 4);
            return (MerkabaDualNodeState)((word >> ((child & 15) * 2)) & 3u);
        }

        internal void Set(int child, MerkabaDualNodeState state)
        {
            ValidateChild(child);
            MerkabaDualBlockMeta.ValidateState(state);
            int wordIndex = child >> 4;
            int shift = (child & 15) * 2;
            uint word = GetWord(wordIndex);
            word = (word & ~(3u << shift)) | ((uint)state << shift);
            SetWord(wordIndex, word);
        }

        internal readonly MerkabaDualNodeState CollapseState()
        {
            bool allFull = true;
            bool allThrough = true;
            for (int i = 0; i < WordCount; i++)
            {
                uint word = GetWord(i);
                allFull &= word == 0u;
                allThrough &= word == AllThroughWord;
            }
            return allFull ? MerkabaDualNodeState.AllFull :
                allThrough ? MerkabaDualNodeState.AllThrough :
                MerkabaDualNodeState.Mixed;
        }

        private static void ValidateChild(int child)
        {
            if ((uint)child >= ChildCount)
                throw new ArgumentOutOfRangeException(nameof(child));
        }

        internal readonly uint GetWord(int index) => index switch
        {
            0 => Word00, 1 => Word01, 2 => Word02, 3 => Word03,
            4 => Word04, 5 => Word05, 6 => Word06, 7 => Word07,
            8 => Word08, 9 => Word09, 10 => Word10, 11 => Word11,
            12 => Word12, 13 => Word13, 14 => Word14, 15 => Word15,
            16 => Word16, 17 => Word17, 18 => Word18, 19 => Word19,
            20 => Word20, 21 => Word21, 22 => Word22, 23 => Word23,
            24 => Word24, 25 => Word25, 26 => Word26, 27 => Word27,
            28 => Word28, 29 => Word29, 30 => Word30, 31 => Word31,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        private void SetWord(int index, uint value)
        {
            switch (index)
            {
                case 0: Word00 = value; break; case 1: Word01 = value; break;
                case 2: Word02 = value; break; case 3: Word03 = value; break;
                case 4: Word04 = value; break; case 5: Word05 = value; break;
                case 6: Word06 = value; break; case 7: Word07 = value; break;
                case 8: Word08 = value; break; case 9: Word09 = value; break;
                case 10: Word10 = value; break; case 11: Word11 = value; break;
                case 12: Word12 = value; break; case 13: Word13 = value; break;
                case 14: Word14 = value; break; case 15: Word15 = value; break;
                case 16: Word16 = value; break; case 17: Word17 = value; break;
                case 18: Word18 = value; break; case 19: Word19 = value; break;
                case 20: Word20 = value; break; case 21: Word21 = value; break;
                case 22: Word22 = value; break; case 23: Word23 = value; break;
                case 24: Word24 = value; break; case 25: Word25 = value; break;
                case 26: Word26 = value; break; case 27: Word27 = value; break;
                case 28: Word28 = value; break; case 29: Word29 = value; break;
                case 30: Word30 = value; break; case 31: Word31 = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    /// <summary>Thirty-two-byte summary of the 64 tiles in one M8 chunk.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaDualChunkPayload
    {
        internal const int ByteSize = 32;

        internal uint NonFullMaskLo;
        internal uint NonFullMaskHi;
        internal uint MixedMaskLo;
        internal uint MixedMaskHi;
        internal uint LeafRefBase;
        internal uint Generation;
        internal uint Reserved0;
        internal uint Reserved1;

        internal static MerkabaDualChunkPayload Create(ulong nonFullMask,
            ulong mixedMask, uint leafRefBase, uint generation)
        {
            if ((mixedMask & ~nonFullMask) != 0ul)
                throw new ArgumentException(
                    "Every MIXED tile must also be marked non-full.",
                    nameof(mixedMask));
            if (generation == 0u)
                throw new ArgumentOutOfRangeException(nameof(generation));
            bool hasLeaves = mixedMask != 0ul;
            if (hasLeaves !=
                (leafRefBase != MerkabaDualBlockMeta.NoPayload))
                throw new ArgumentException(
                    "LeafRefBase exists exactly when MIXED leaves exist.",
                    nameof(leafRefBase));
            return new MerkabaDualChunkPayload
            {
                NonFullMaskLo = (uint)nonFullMask,
                NonFullMaskHi = (uint)(nonFullMask >> 32),
                MixedMaskLo = (uint)mixedMask,
                MixedMaskHi = (uint)(mixedMask >> 32),
                LeafRefBase = leafRefBase,
                Generation = generation
            };
        }

        internal static MerkabaDualChunkPayload CreateUniform(
            MerkabaDualNodeState state, uint generation)
        {
            if (state != MerkabaDualNodeState.AllFull &&
                state != MerkabaDualNodeState.AllThrough)
                throw new ArgumentOutOfRangeException(nameof(state));
            return Create(state == MerkabaDualNodeState.AllThrough
                    ? ulong.MaxValue : 0ul,
                0ul, MerkabaDualBlockMeta.NoPayload, generation);
        }

        internal readonly ulong NonFullMask =>
            NonFullMaskLo | ((ulong)NonFullMaskHi << 32);

        internal readonly ulong MixedMask =>
            MixedMaskLo | ((ulong)MixedMaskHi << 32);

        internal readonly bool IsCanonical => Generation != 0u &&
            Reserved0 == 0u && Reserved1 == 0u &&
            (MixedMask & ~NonFullMask) == 0ul &&
            ((MixedMask != 0ul) ==
             (LeafRefBase != MerkabaDualBlockMeta.NoPayload));

        internal readonly MerkabaDualNodeState GetTileState(int tileLocal)
        {
            ValidateTile(tileLocal);
            ulong bit = 1ul << tileLocal;
            if ((NonFullMask & bit) == 0ul)
                return MerkabaDualNodeState.AllFull;
            return (MixedMask & bit) == 0ul
                ? MerkabaDualNodeState.AllThrough
                : MerkabaDualNodeState.Mixed;
        }

        internal readonly bool TryGetLeafRef(int tileLocal,
            out uint leafRef)
        {
            ValidateTile(tileLocal);
            ulong bit = 1ul << tileLocal;
            ulong mixed = MixedMask;
            if ((mixed & bit) == 0ul)
            {
                leafRef = MerkabaDualBlockMeta.NoPayload;
                return false;
            }
            ulong preceding = tileLocal == 0
                ? 0ul : mixed & (bit - 1ul);
            leafRef = checked(LeafRefBase + (uint)PopCount(preceding));
            return true;
        }

        internal readonly MerkabaDualNodeState CollapseState()
        {
            if (MixedMask != 0ul) return MerkabaDualNodeState.Mixed;
            if (NonFullMask == 0ul) return MerkabaDualNodeState.AllFull;
            return NonFullMask == ulong.MaxValue
                ? MerkabaDualNodeState.AllThrough
                : MerkabaDualNodeState.Mixed;
        }

        internal static int PopCount(ulong value)
        {
            value -= (value >> 1) & 0x5555555555555555ul;
            value = (value & 0x3333333333333333ul) +
                ((value >> 2) & 0x3333333333333333ul);
            value = (value + (value >> 4)) & 0x0f0f0f0f0f0f0f0ful;
            return (int)((value * 0x0101010101010101ul) >> 56);
        }

        private static void ValidateTile(int tileLocal)
        {
            if ((uint)tileLocal >= MerkabaSpatial.TilesPerChunk)
                throw new ArgumentOutOfRangeException(nameof(tileLocal));
        }
    }

    /// <summary>One exact 512-bit mixed SEE_THROUGH tile leaf.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaDualLeaf
    {
        internal const int WordCount = MerkabaSpatial.TileWordCount;
        internal const int ByteSize = 64;

        internal uint Word00; internal uint Word01;
        internal uint Word02; internal uint Word03;
        internal uint Word04; internal uint Word05;
        internal uint Word06; internal uint Word07;
        internal uint Word08; internal uint Word09;
        internal uint Word10; internal uint Word11;
        internal uint Word12; internal uint Word13;
        internal uint Word14; internal uint Word15;

        internal static MerkabaDualLeaf CreateUniform(
            MerkabaDualNodeState state)
        {
            if (state != MerkabaDualNodeState.AllFull &&
                state != MerkabaDualNodeState.AllThrough)
                throw new ArgumentOutOfRangeException(nameof(state));
            var result = new MerkabaDualLeaf();
            uint word = state == MerkabaDualNodeState.AllThrough
                ? uint.MaxValue : 0u;
            for (int i = 0; i < WordCount; i++) result.SetWord(i, word);
            return result;
        }

        internal readonly bool IsThrough(int kernelLocal)
        {
            ValidateKernel(kernelLocal);
            return (GetWord(kernelLocal >> 5) &
                (1u << (kernelLocal & 31))) != 0u;
        }

        internal void SetThrough(int kernelLocal, bool through)
        {
            ValidateKernel(kernelLocal);
            int wordIndex = kernelLocal >> 5;
            uint bit = 1u << (kernelLocal & 31);
            uint word = GetWord(wordIndex);
            SetWord(wordIndex, through ? word | bit : word & ~bit);
        }

        internal readonly int ThroughCount()
        {
            int count = 0;
            for (int i = 0; i < WordCount; i++)
                count += MerkabaDualChunkPayload.PopCount(GetWord(i));
            return count;
        }

        internal readonly MerkabaDualNodeState CollapseState()
        {
            bool allFull = true;
            bool allThrough = true;
            for (int i = 0; i < WordCount; i++)
            {
                uint word = GetWord(i);
                allFull &= word == 0u;
                allThrough &= word == uint.MaxValue;
            }
            return allFull ? MerkabaDualNodeState.AllFull :
                allThrough ? MerkabaDualNodeState.AllThrough :
                MerkabaDualNodeState.Mixed;
        }

        internal readonly uint GetWord(int index) => index switch
        {
            0 => Word00, 1 => Word01, 2 => Word02, 3 => Word03,
            4 => Word04, 5 => Word05, 6 => Word06, 7 => Word07,
            8 => Word08, 9 => Word09, 10 => Word10, 11 => Word11,
            12 => Word12, 13 => Word13, 14 => Word14, 15 => Word15,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        private void SetWord(int index, uint value)
        {
            switch (index)
            {
                case 0: Word00 = value; break; case 1: Word01 = value; break;
                case 2: Word02 = value; break; case 3: Word03 = value; break;
                case 4: Word04 = value; break; case 5: Word05 = value; break;
                case 6: Word06 = value; break; case 7: Word07 = value; break;
                case 8: Word08 = value; break; case 9: Word09 = value; break;
                case 10: Word10 = value; break; case 11: Word11 = value; break;
                case 12: Word12 = value; break; case 13: Word13 = value; break;
                case 14: Word14 = value; break; case 15: Word15 = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        private static void ValidateKernel(int kernelLocal)
        {
            if ((uint)kernelLocal >= MerkabaSpatial.KernelsPerTile)
                throw new ArgumentOutOfRangeException(nameof(kernelLocal));
        }
    }
}
