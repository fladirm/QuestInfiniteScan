#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    public static partial class MerkabaSphereFlowerAuthority
    {
        /// <summary>
        /// Frozen, already reduced endpoint observations on an actual generated
        /// child loop. Ancestors index earlier cases, not scan history. This is
        /// oracle input, never persistent topology or a production CPU scanner.
        /// </summary>
        internal sealed class PhaseDrainEvidence
        {
            internal readonly int Petal, ParentContext, KnotSite;
            internal readonly MerkabaFlowerDetailKey Key;
            internal readonly PhaseRootEvidence Observed;
            private readonly int[] _ancestors;
            internal ReadOnlySpan<int> Ancestors => _ancestors;

            internal PhaseDrainEvidence(int petal, int parentContext, int knotSite,
                MerkabaFlowerDetailKey key, PhaseRootEvidence firstEndpoint,
                PhaseRootEvidence secondEndpoint,
                ReadOnlySpan<int> ancestors = default)
            {
                if (!TryGetChildPhaseLoop(petal, parentContext, knotSite,
                        out int level, out _, out int line, out int strandClass, out _, out _,
                        out int inherited) || inherited >= 0 ||
                    key.PetalClass != petal || key.GeometryLevel != level ||
                    key.Channel != line || LinesValue[line].Shell != Shell.R2Shape ||
                    key.Kind != MerkabaFlowerDetailKind.R2Phase)
                    throw new ArgumentException("Expected a generated new R2 child loop.");
                PhaseFamilyRule family = PhaseFamiliesValue[strandClass];
                StrandRule strand = StrandsValue[strandClass];
                int path = level == 1 ? (petal == strand.Petal0 ? family.FinePath0 :
                    petal == strand.Petal1 ? family.FinePath1 : -1) :
                    4 * (parentContext - 1) + (knotSite == 4 ? 1 : 0);
                if (key.GeometryChildPath != path)
                    throw new ArgumentException("Record path must come from the generated incidence.");
                Petal = petal;
                ParentContext = parentContext;
                KnotSite = knotSite;
                Key = key;
                ProofClassification classification = SealPhaseRelation(firstEndpoint,
                    secondEndpoint, out PhaseRootEvidence observed, out _);
                Observed = classification == ProofClassification.Certain ? observed :
                    new PhaseRootEvidence(firstEndpoint.Symbol, default, classification);
                _ancestors = ancestors.ToArray();
            }
        }

        /// <summary>Complete seven-child, two-view footprint enclosures. Pixel
        /// projection and seven-root carrier admission have already happened.
        /// These bounds are copied: retry cannot acquire a newer camera frame.</summary>
        internal sealed class RgbDrainEvidence
        {
            internal readonly int ParentOrdinal;
            internal readonly uint LeftCertain, RightCertain;
            private readonly FloatInterval[] _left, _right;

            internal RgbDrainEvidence(int parentOrdinal,
                ReadOnlySpan<FloatInterval> left, ReadOnlySpan<FloatInterval> right,
                uint leftCertain, uint rightCertain)
            {
                if ((uint)parentOrdinal >= SkinSplitBitCount ||
                    left.Length != 21 || right.Length != 21)
                    throw new ArgumentException("One fixed seven-child RGB footprint required.");
                ParentOrdinal = parentOrdinal;
                _left = left.ToArray();
                _right = right.ToArray();
                LeftCertain = leftCertain;
                RightCertain = rightCertain;
            }

            internal SplitClassification Reduce(out MerkabaThreadColorInterval[] colors)
            {
                colors = new MerkabaThreadColorInterval[7];
                if ((LeftCertain & RightCertain & 0x7fu) != 0x7fu)
                    return SplitClassification.Ambiguous;
                Span<FloatInterval> stored = stackalloc FloatInterval[21];
                for (int child = 0; child < 7; child++)
                {
                    float4 lower = new(0f, 0f, 0f, 1f), upper = lower;
                    for (int channel = 0; channel < 3; channel++)
                    {
                        int index = 3 * child + channel;
                        if (!FloatInterval.TryIntersect(_left[index], _right[index],
                                out FloatInterval interval))
                            return SplitClassification.Ambiguous;
                        lower[channel] = interval.Lower;
                        upper[channel] = interval.Upper;
                    }
                    if (!MerkabaThreadColorInterval.TryEncode(lower, upper, out colors[child]))
                        return SplitClassification.Ambiguous;
                    // Match the live reducer: disjointness is tested AFTER
                    // outward binary16 encoding, not on a narrower float box.
                    lower = colors[child].LowerLinearRgba;
                    upper = colors[child].UpperLinearRgba;
                    for (int channel = 0; channel < 3; channel++)
                        stored[3 * child + channel] = new FloatInterval(lower[channel], upper[channel]);
                }
                return ClassifyRgbSkinSplit(stored, 0x7fu);
            }
        }

        /// <summary>
        /// CPU oracle for reduced immutable evidence -> initially empty R2 records
        /// and canonical RGB groups. It calls the same endpoint SEAL, family prediction,
        /// interval analysis, Q2.29 synthesis and final-root closure as the GPU.
        /// This does NOT prove stereo acquisition, GPU dispatch/allocator
        /// liveness, R3 junction admission, V, or fragment footprint coverage.
        /// Canonical RGB group output is not a claim of thread-layout parity.
        /// </summary>
        internal sealed class ObservationDrainOracle
        {
            private readonly int3 _owner;
            private readonly float3 _normal;
            private readonly float _offset, _normalError, _offsetError;
            private readonly uint _epoch;
            private readonly PhaseDrainEvidence[] _phases;
            private readonly RgbDrainEvidence[] _rgb;
            private readonly PhaseRootEvidence[] _closed;
            private readonly SortedDictionary<uint, MerkabaFlowerDetailRecord> _records = new();
            private readonly SortedDictionary<int, MerkabaThreadColorInterval[]> _groups = new();
            private int _cursor;

            internal int PendingCount => _phases.Length + _rgb.Length - _cursor;
            internal bool Complete => PendingCount == 0;
            internal int Evaluations { get; private set; }
            internal int Unresolved { get; private set; }
            internal int PrunedRgbRegions { get; private set; }
            internal ulong RgbSplitBits { get; private set; }
            internal uint ParentEpoch => _epoch;

            internal ObservationDrainOracle(int3 owner, float3 normal, float offset,
                float normalError, float offsetError, uint parentEpoch,
                ReadOnlySpan<PhaseDrainEvidence> phases,
                ReadOnlySpan<RgbDrainEvidence> rgb)
            {
                if (parentEpoch == 0 || !math.all(math.isfinite(normal)) ||
                    !float.IsFinite(offset) || !float.IsFinite(normalError) ||
                    !float.IsFinite(offsetError) || normalError < 0 || offsetError < 0)
                    throw new ArgumentException("A finite frozen R1 carrier and owner epoch are required.");
                _owner = owner; _normal = normal; _offset = offset;
                _normalError = normalError; _offsetError = offsetError; _epoch = parentEpoch;
                _phases = phases.ToArray(); _rgb = rgb.ToArray();
                _closed = new PhaseRootEvidence[_phases.Length];
                var keys = new HashSet<uint>();
                for (int index = 0; index < _phases.Length; index++)
                {
                    PhaseDrainEvidence phase = _phases[index] ??
                        throw new ArgumentException("Missing frozen phase case.");
                    if (!keys.Add(phase.Key.Value)) throw new ArgumentException("Duplicate parent-local record key.");
                    int previous = -1;
                    foreach (int ancestor in phase.Ancestors)
                    {
                        if (ancestor <= previous || ancestor >= index)
                            throw new ArgumentException("Ancestry must be a strict coarse-to-fine prefix.");
                        previous = ancestor;
                    }
                }
                int previousParent = -1;
                foreach (RgbDrainEvidence group in _rgb)
                {
                    if (group == null || group.ParentOrdinal <= previousParent)
                        throw new ArgumentException("RGB groups must use fixed parent-thread order.");
                    previousParent = group.ParentOrdinal;
                }
            }

            internal MerkabaFlowerDetailRecord[] CommittedDetail()
            {
                var result = new MerkabaFlowerDetailRecord[_records.Count];
                _records.Values.CopyTo(result, 0);
                return result;
            }

            internal MerkabaThreadColorInterval[] CommittedCanonicalRgb()
            {
                var result = new MerkabaThreadColorInterval[7 * _groups.Count];
                int offset = 0;
                foreach (var group in _groups.Values)
                { Array.Copy(group, 0, result, offset, 7); offset += 7; }
                return result;
            }

            /// <summary>Unavailable publication models allocator/generation
            /// backpressure. No cursor, ancestry or metric value advances on
            /// that attempt; the next quantum uses the identical input object.</summary>
            internal int Drain(int quantum, bool publicationAvailable = true)
            {
                if (quantum <= 0) throw new ArgumentOutOfRangeException(nameof(quantum));
                int consumed = 0;
                while (!Complete && consumed < quantum)
                {
                    if (_cursor < _phases.Length)
                    {
                        if (!DrainPhase(_cursor, publicationAvailable)) break;
                    }
                    else if (!DrainRgb(_rgb[_cursor - _phases.Length], publicationAvailable)) break;
                    _cursor++; consumed++;
                }
                return consumed;
            }

            private bool DrainPhase(int index, bool publicationAvailable)
            {
                PhaseDrainEvidence phase = _phases[index];
                Span<PhaseRootEvidence> roots = stackalloc PhaseRootEvidence[2];
                Span<MerkabaFlowerDetailRecord> records = stackalloc MerkabaFlowerDetailRecord[2];
                Span<MerkabaFlowerDetailKey> keys = stackalloc MerkabaFlowerDetailKey[2];
                int count = 0;
                foreach (int ancestor in phase.Ancestors)
                {
                    var key = _phases[ancestor].Key;
                    if (!_records.TryGetValue(key.Value, out var record)) continue;
                    if (count == 2) throw new InvalidOperationException("Geometry ancestry exceeds L0-L2.");
                    roots[count] = _closed[ancestor]; records[count] = record; keys[count++] = key;
                }
                Evaluations++;
                if (PredictChildFromFamily(_owner, phase.Petal, phase.ParentContext,
                        phase.KnotSite, _normal, _offset, _normalError, _offsetError,
                        phase.Key.Sector, phase.Key.RootSign, ReadOnlySpan<PhaseRootEvidence>.Empty,
                        roots[..count], records[..count], keys[..count], _epoch,
                        out PhaseRootEvidence predicted) != ProofClassification.Certain)
                { Unresolved++; return true; }
                PhaseResidualResult analysis = AnalyzePhaseResidual(predicted, phase.Observed);
                if (analysis.Classification != PhaseResidualClassification.CertainNonzero)
                {
                    if (analysis.Classification == PhaseResidualClassification.ExactZero)
                        _closed[index] = predicted;
                    else Unresolved++;
                    return true;
                }
                var candidate = MerkabaFlowerDetailRecord.Create(phase.Key,
                    analysis.Lower, analysis.Upper, _epoch);
                if (SynthesizePhaseRecord(predicted, phase.Key, candidate, _epoch,
                        out var synthesized) != ProofClassification.Certain ||
                    CloseSharedPhaseRoot(synthesized, phase.Observed, out var closed) !=
                        ProofClassification.Certain)
                { Unresolved++; return true; }
                if (!publicationAvailable) return false;
                _records[candidate.Key] = candidate;
                _closed[index] = closed;
                return true;
            }

            private bool DrainRgb(RgbDrainEvidence evidence, bool publicationAvailable)
            {
                int parent = evidence.ParentOrdinal;
                int predecessor = parent < 8 ? 0 : 1 + (parent - 8) / 7;
                if (parent != 0 && (RgbSplitBits & (1UL << predecessor)) == 0)
                { PrunedRgbRegions++; return true; }
                Evaluations++;
                SplitClassification classification = evidence.Reduce(out var group);
                if (classification == SplitClassification.Ambiguous)
                { Unresolved++; return true; }
                if (classification == SplitClassification.Uniform)
                { PrunedRgbRegions++; return true; }
                if (!publicationAvailable) return false;
                _groups[parent] = group;
                RgbSplitBits |= 1UL << parent;
                return true;
            }
        }
    }
}
#endif
