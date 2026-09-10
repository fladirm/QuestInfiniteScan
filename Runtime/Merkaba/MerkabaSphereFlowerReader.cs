using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    public static partial class MerkabaSphereFlowerAuthority
    {
        // Metric evidence only: resolved axes do not select a generated
        // junction class. A zero q slot without its resolved bit is absent.
        internal readonly struct R3MetricEvidence
        {
            internal readonly FloatInterval Scalar;
            internal readonly Interval3 Vector;
            internal readonly uint ResolvedAxes, ExactZeroAxes, ProvisionalAxes;
            internal readonly int BranchChirality;

            internal R3MetricEvidence(FloatInterval scalar, Interval3 vector,
                uint resolved, uint exactZero, uint provisional, int chirality)
            {
                Scalar = scalar; Vector = vector;
                ResolvedAxes = resolved; ExactZeroAxes = exactZero;
                ProvisionalAxes = provisional; BranchChirality = chirality;
            }
        }

        // Transient proof receipt tied to one frozen parent snapshot. The
        // token is an address/sign selection, never new metric geometry.
        internal readonly struct ParentCompletionSelection
        {
            internal readonly FlowerDecode Source;
            internal readonly ProofClassification Classification;
            internal readonly uint Token, JunctionClass;
            internal readonly ulong Candidates;
            internal int CompletedPetal => (int)(Token & 63u);
            internal uint AnchorSigns => (Token >> 6) & 7u;

            internal ParentCompletionSelection(FlowerDecode source, ProofClassification classification,
                uint token, uint junctionClass, ulong candidates)
            {
                Source = source; Classification = classification; Token = token;
                JunctionClass = junctionClass; Candidates = candidates;
            }
        }

        // A failed REQUIRED presentation query, not absent geometry and not
        // an I/O status. The exact symbolic region is enough to report which
        // support needs evidence; this receipt stores no metric world state.
        internal readonly struct RequiredSupportReceipt
        {
            internal readonly int3 Owner;
            internal readonly int Carrier;
            internal readonly uint WedgeMask;
            internal readonly int JunctionPetal;
            internal readonly uint RootAlternatives;

            internal RequiredSupportReceipt(int3 owner, int carrier, uint wedgeMask,
                int junctionPetal = -1, uint rootAlternatives = 0u)
            {
                Owner = owner; Carrier = carrier; WedgeMask = wedgeMask;
                JunctionPetal = junctionPetal; RootAlternatives = rootAlternatives;
            }
        }

        // One owner's disposable decode over a frozen world. Generated source
        // incidence reaches the branches; metric reads are memoized only when
        // consumed. Neither a full root universe nor a program cursor is built.
        internal sealed class FlowerDecode
        {
            private const ulong PetalMask = (1UL << PetalClassCount) - 1UL;
            private readonly SnapshotReader _reader;
            private readonly int3 _owner;
            private readonly float2 _errors;
            private readonly Dictionary<int, (PhaseRootEvidence Root, bool Provisional)> _original = new();
            private readonly Dictionary<int, (PhaseRootEvidence Root, bool Read)> _knots = new();
            private readonly Dictionary<int, DecodedCarrier> _directCarriers = new();
            private readonly PhaseRootEvidence[] _directAnchors = new PhaseRootEvidence[3 * PetalClassCount];
            private readonly ulong[] _directAnchorSeen = new ulong[3];
            private ulong _directAnchorConflict;
            private readonly PhaseRootEvidence[] _candidate = new PhaseRootEvidence[3 * PetalClassCount];
            private readonly ProofClassification[] _candidateStatus = new ProofClassification[3 * PetalClassCount];
            private uint4 _visited;
            private ulong _failedDirect, _failedOrientation, _failedDualClear, _dualVeto;
            private bool _completionEvaluated;
            private ParentCompletionSelection _completion;

            internal uint4 ReachedCarriers { get; private set; }

            internal bool IsComplete { get; private set; }
            internal ulong ConfirmedDirect { get; private set; }
            internal ulong UniqueRoots { get; private set; }
            // Three original flag/root identities only. The separate finite
            // R3 selector must still prove the candidate's junction class.
            internal ulong UniqueSymbols { get; private set; }
            internal ulong CertainOrientation { get; private set; }
            internal ulong DualClear { get; private set; }
            internal ulong DualVeto { get; private set; }
            internal ulong DualAmbiguous { get; private set; }

            private sealed class DecodedCarrier
            {
                internal readonly ProofClassification Status;
                internal readonly MerkabaFlowerSymbolRecord Symbol;
                internal readonly uint Unresolved, Direct;
                internal readonly PhaseRootEvidence[] Roots;
                internal readonly float3[] Positions;

                internal DecodedCarrier(ProofClassification status, MerkabaFlowerSymbolRecord symbol,
                    uint unresolved, uint direct, ReadOnlySpan<PhaseRootEvidence> roots,
                    ReadOnlySpan<float3> positions)
                {
                    Status = status; Symbol = symbol; Unresolved = unresolved; Direct = direct;
                    Roots = roots.ToArray(); Positions = positions.ToArray();
                }
            }

            internal FlowerDecode(SnapshotReader reader, int3 owner, float2 errors)
            {
                _reader = reader; _owner = owner; _errors = errors;
                ulong possible = PetalMask;
                uint absent = 0u;
                // R1, then only higher-shell anchors of surviving sources.
                // One possible sign is sufficient for reaching a construction;
                // joint branch closure later requests its exact alternatives.
                for (int node = 0; node < NodeClassCount; node++)
                {
                    ulong incident = NodeIncidentPetalsValue[node];
                    if ((possible & incident) == 0u) continue;
                    if (ReadOriginal(node, false, out _, out _) != ProofClassification.Impossible ||
                        ReadOriginal(node, true, out _, out _) != ProofClassification.Impossible) continue;
                    absent |= 1u << node;
                    possible &= ~incident;
                }
                ReachedCarriers = M8FlowerReachedCarriers(absent);
            }

            internal void RequireSource(SnapshotReader reader, int3 owner, float2 errors)
            {
                if (!ReferenceEquals(reader, _reader) || math.any(owner != _owner) ||
                    math.any(math.asuint(errors) != math.asuint(_errors)))
                    throw new InvalidOperationException("Flower decode requires one unchanged owner and frozen reader.");
            }

            internal ProofClassification ReadOriginal(int node, bool plus,
                out PhaseRootEvidence root, out bool provisional)
            {
                int index = 2 * node + (plus ? 1 : 0);
                if (!_original.TryGetValue(index, out var value))
                {
                    ProofClassification status = _reader.ReadOriginalShared(_owner, node, plus,
                        _errors.x, _errors.y, out root, out provisional);
                    value = (new PhaseRootEvidence(root.Symbol, root.Root, status), provisional);
                    _original.Add(index, value);
                }
                root = value.Root;
                provisional = value.Provisional;
                return root.Classification;
            }

            internal bool ReadKnot(int knot, bool plus, out PhaseRootEvidence root)
            {
                int key = 2 * knot + (plus ? 1 : 0);
                if (!_knots.TryGetValue(key, out var value))
                {
                    bool read = _reader.ReadL2Knot(_owner, knot, plus, _errors.x, _errors.y, out root, this);
                    value = (root, read);
                    _knots.Add(key, value);
                }
                root = value.Root;
                return value.Read;
            }

            internal void StoreDirectCarrier(int carrier, ProofClassification status,
                MerkabaFlowerSymbolRecord symbol, uint unresolved, uint direct,
                ReadOnlySpan<PhaseRootEvidence> roots, ReadOnlySpan<float3> positions) =>
                _directCarriers.Add(carrier, new DecodedCarrier(status, symbol, unresolved, direct, roots, positions));

            internal bool TryReadDirectCarrier(int carrier, out ProofClassification status,
                out MerkabaFlowerSymbolRecord symbol, out uint unresolved, out uint direct,
                Span<PhaseRootEvidence> roots, Span<float3> positions)
            {
                status = default; symbol = default; unresolved = direct = 0u;
                if (!_directCarriers.TryGetValue(carrier, out DecodedCarrier value)) return false;
                status = value.Status; symbol = value.Symbol; unresolved = value.Unresolved; direct = value.Direct;
                value.Roots.AsSpan().CopyTo(roots); value.Positions.AsSpan().CopyTo(positions);
                return true;
            }

            internal void RecordCarrier(int carrier, uint oriented, uint direct, uint dualClear, uint dualVeto,
                ReadOnlySpan<PhaseRootEvidence> roots = default)
            {
                if (IsComplete || (uint)carrier >= L2HubCount ||
                    ((oriented | direct | dualClear | dualVeto) & ~63u) != 0u ||
                    ((direct | dualClear | dualVeto) & ~oriented) != 0u ||
                    (direct & ~dualClear) != 0u || (dualClear & dualVeto) != 0u ||
                    (direct != 0u && roots.Length != L2CarrierSiteCount))
                    throw new InvalidOperationException("Invalid actual carrier proof in Flower decode.");
                uint bit = 1u << (carrier & 31);
                int word = carrier >> 5;
                if ((_visited[word] & bit) != 0u)
                    throw new InvalidOperationException("Flower source carrier was counted twice.");
                _visited[word] |= bit;
                for (int wedge = 0; wedge < 6; wedge++)
                {
                    int petal = CarrierData.Wedges[6 * carrier + wedge].Petal;
                    ulong parent = 1UL << petal;
                    uint child = 1u << wedge;
                    if ((direct & child) == 0u) _failedDirect |= parent;
                    if ((oriented & child) == 0u) _failedOrientation |= parent;
                    if ((dualClear & child) == 0u) _failedDualClear |= parent;
                    if ((dualVeto & child) != 0u) _dualVeto |= parent;
                    if ((direct & child) == 0u) continue;
                    int3 sites = L2CarrierTriangleIndices(wedge);
                    for (int vertex = 0; vertex < 3; vertex++)
                    {
                        int site = sites[vertex], knot = L2CarrierKnotIndex(carrier, site);
                        if (!TryGetL2KnotLoop(knot, out int level, out int3 offset, out int line) || level != 0)
                            continue;
                        for (int anchor = 0; anchor < 3; anchor++)
                        {
                            int node = PetalsValue[petal].Node(anchor);
                            if (NodesValue[node].LineClass != line || math.any(NodesValue[node].Direction != offset))
                                continue;
                            PhaseRootEvidence selected = roots[site];
                            bool plus = ((selected.Symbol.Tag >> 7) & 1u) != 0u;
                            if (ReadOriginal(node, plus, out PhaseRootEvidence original, out bool provisional) !=
                                    ProofClassification.Certain || provisional ||
                                CloseSharedPhaseRoot(original, selected, out selected) != ProofClassification.Certain)
                            { _directAnchorConflict |= parent; continue; }
                            int index = 3 * petal + anchor;
                            if ((_directAnchorSeen[anchor] & parent) != 0u &&
                                CloseSharedPhaseRoot(_directAnchors[index], selected, out selected) !=
                                    ProofClassification.Certain)
                            { _directAnchorConflict |= parent; continue; }
                            _directAnchors[index] = selected;
                            _directAnchorSeen[anchor] |= parent;
                        }
                    }
                }
            }

            internal void Complete()
            {
                if (IsComplete) return;
                if (math.any((_visited & ReachedCarriers) != ReachedCarriers))
                    throw new InvalidOperationException("Parent completion requires every reached carrier.");
                // Unreached carriers have a proved absent source anchor, not
                // an untested metric candidate. Account for their source
                // incidence without evaluating any child loop or dual cover.
                uint4 excluded = ~_visited;
                while (TakeReachedCarrier(ref excluded, out int carrier))
                {
                    uint4 mask = DecodeCarrierPetals[carrier];
                    ulong petals = mask.x | ((ulong)mask.y << 32);
                    _failedDirect |= petals;
                    _failedOrientation |= petals;
                    _failedDualClear |= petals;
                }
                ConfirmedDirect = PetalMask & ~_failedDirect & ~_directAnchorConflict &
                    _directAnchorSeen[0] & _directAnchorSeen[1] & _directAnchorSeen[2];
                CertainOrientation = PetalMask & ~_failedOrientation;
                DualClear = PetalMask & ~_failedDualClear;
                DualVeto = PetalMask & _dualVeto;
                DualAmbiguous = PetalMask & ~(DualClear | DualVeto);
                // Only D supplies these anchors. Candidate roots are never
                // fed back into this pass, even if a later completion is unique.
                uint2 boundary = M8FlowerCompletionBoundaryCandidates(Mask(ConfirmedDirect));
                ulong pending = boundary.x | ((ulong)boundary.y << 32);
                while (pending != 0u)
                {
                    int petal = (uint)pending != 0u ? math.tzcnt((uint)pending) :
                        32 + math.tzcnt((uint)(pending >> 32));
                    pending &= pending - 1UL;
                    bool rootsUnique = true, symbolsUnique = true;
                    for (int anchor = 0; anchor < 3; anchor++)
                    {
                        ProofClassification status = ReadIncidentAnchor(petal, anchor,
                            out PhaseRootEvidence root, out bool symbolUnique);
                        _candidateStatus[3 * petal + anchor] = status;
                        bool rootUnique = status == ProofClassification.Certain;
                        rootsUnique &= rootUnique;
                        symbolsUnique &= symbolUnique;
                        if (rootUnique) _candidate[3 * petal + anchor] = root;
                    }
                    if (rootsUnique) UniqueRoots |= 1UL << petal;
                    if (symbolsUnique) UniqueSymbols |= 1UL << petal;
                }
                IsComplete = true;
            }

            internal bool TryGetCandidateAnchor(int petal, int anchor, out PhaseRootEvidence root)
            {
                root = default;
                if (!IsComplete || (uint)petal >= PetalClassCount || (uint)anchor >= 3u ||
                    (UniqueRoots & (1UL << petal)) == 0u) return false;
                root = _candidate[3 * petal + anchor];
                return true;
            }

            internal bool TryGetCompletionToken(int petal, out uint token)
            {
                token = uint.MaxValue;
                if (!IsComplete || (uint)petal >= PetalClassCount ||
                    ((UniqueRoots & UniqueSymbols) & (1UL << petal)) == 0u) return false;
                uint signs = 0u;
                for (int anchor = 0; anchor < 3; anchor++)
                    signs |= ((_candidate[3 * petal + anchor].Symbol.Tag >> 7) & 1u) << anchor;
                token = (uint)petal | (signs << 6);
                return true;
            }

            internal bool HasCompletionAnchors(uint token) => token < 512u &&
                TryGetCompletionToken((int)(token & 63u), out uint actual) && actual == token;

            internal ProofClassification EvaluateCompletion(out ParentCompletionSelection selection)
            {
                if (!IsComplete) throw new InvalidOperationException("Completion requires the immutable full parent snapshot.");
                if (_completionEvaluated) { selection = _completion; return selection.Classification; }
                uint2 direct = Mask(ConfirmedDirect);
                uint2 boundaryMask = M8FlowerCompletionBoundaryCandidates(direct);
                ulong boundary = boundaryMask.x | ((ulong)boundaryMask.y << 32);
                var classification = ProofClassification.Impossible;
                uint selectedToken = uint.MaxValue, junctionClass = uint.MaxValue;
                ulong candidates = 0u;
                if (boundary != 0u)
                {
                    if (!_reader.TryReadOwner(_owner, out KernelState state, out _))
                        classification = ProofClassification.Ambiguous;
                    else
                    {
                        int parity = (_owner.x & 1) | ((_owner.y & 1) << 1) | ((_owner.z & 1) << 2);
                        TetraFrameRule frame = TetraFramesValue[parity];
                        M8FlowerUnpackPlane(state.Flags, out float3 normal, out _);
                        Span<Interval3> bounds = stackalloc Interval3[3];
                        ulong rootsCertain = 0u, symbolsCertain = 0u, shellsCertain = 0u, orientationCertain = 0u;
                        bool ambiguous = false;
                        for (int petal = 0; petal < PetalClassCount; petal++)
                        {
                            ulong bit = 1UL << petal;
                            if ((boundary & bit) == 0u) continue;
                            bool impossible = false, uncertain = false;
                            uint token = (uint)petal;
                            for (int anchor = 0; anchor < 3; anchor++)
                            {
                                ProofClassification status = _candidateStatus[3 * petal + anchor];
                                if (status == ProofClassification.Impossible) { impossible = true; break; }
                                if (status != ProofClassification.Certain) { uncertain = true; continue; }
                                PhaseRootEvidence root = _candidate[3 * petal + anchor];
                                uint sign = (root.Symbol.Tag >> 7) & 1u;
                                token |= sign << (6 + anchor);
                                NodeRule node = NodesValue[PetalsValue[petal].Node(anchor)];
                                if (!RootRelativeBounds(0, node.Direction, node.LineClass, root, out bounds[anchor]))
                                    uncertain = true;
                            }
                            if (impossible) continue;
                            if (uncertain) { ambiguous = true; continue; }
                            rootsCertain |= bit; symbolsCertain |= bit;
                            // The candidate flag participates in class
                            // filtering BEFORE uniqueness. Unrelated classes
                            // neither certify nor veto this donor-backed flag.
                            JunctionSelection junction = _reader.ReadR3Junction(_owner, _errors, this, petal);
                            if (junction.Classification != JunctionClassification.CertainClosure)
                            {
                                _reader.RecordRequiredJunctionSupport(_owner, petal, junction.RequiredDualMask);
                                if (junction.Classification != JunctionClassification.Impossible) ambiguous = true;
                                continue;
                            }
                            JunctionRule rule = JunctionRules[(int)junction.ClassIndex];
                            int junctionAxis = -1;
                            for (int axis = 0; axis < 4; axis++)
                                if (rule.Flag(frame.LineClasses[axis]) == petal &&
                                    rule.Corner(frame.LineClasses[axis]) == PetalsValue[petal].CornerNode)
                                    junctionAxis = axis;
                            if (junctionAxis < 0 || ((token >> 8) & 1u) !=
                                ((junction.RootSigns >> junctionAxis) & 1u)) continue;
                            if (PetalsValue[petal].Orientation < 0)
                            { Interval3 edge = bounds[1]; bounds[1] = bounds[2]; bounds[2] = edge; }
                            ProofClassification orientation = CarrierWedgeOrientation(bounds[0], bounds[1], bounds[2],
                                normal, _errors.x);
                            if (orientation == ProofClassification.Impossible) continue;
                            if (orientation != ProofClassification.Certain) { ambiguous = true; continue; }
                            ProofClassification child = _reader.CompletionPetalProof(_owner, petal, token, _errors, this);
                            if (child == ProofClassification.Impossible) continue;
                            if (child != ProofClassification.Certain) { ambiguous = true; continue; }
                            // These bits require all sixteen actual children
                            // AND their complete dual supports, not one anchor
                            // triangle or the pre-existing own-D admission.
                            orientationCertain |= bit; shellsCertain |= bit;
                            selectedToken = token;
                            junctionClass = junction.ClassIndex;
                        }
                        uint2 mask = M8FlowerCompletionCandidates(direct, Mask(rootsCertain), Mask(symbolsCertain),
                            Mask(shellsCertain), Mask(orientationCertain), default);
                        candidates = mask.x | ((ulong)mask.y << 32);
                        uint decision = M8FlowerUniqueCompletion(mask, out uint selected);
                        classification = ambiguous || decision == 2u ? ProofClassification.Ambiguous :
                            decision == 1u && selected == (selectedToken & 63u) ? ProofClassification.Certain :
                            ProofClassification.Impossible;
                    }
                }
                if (classification != ProofClassification.Certain) selectedToken = uint.MaxValue;
                _completion = new ParentCompletionSelection(this, classification, selectedToken, junctionClass, candidates);
                _completionEvaluated = true;
                selection = _completion;
                return classification;
            }

            internal ProofClassification ReadCarrier(int carrier, ParentCompletionSelection selection,
                out MerkabaFlowerSymbolRecord symbol, out uint unresolved,
                Span<PhaseRootEvidence> roots, Span<float3> positions)
            {
                if (!ReferenceEquals(selection.Source, this) || !_completionEvaluated ||
                    selection.Token != _completion.Token || selection.Classification != _completion.Classification)
                    throw new InvalidOperationException("Completion receipt does not belong to this frozen parent decision.");
                uint completed = selection.Token & 63u;
                bool changed = false;
                if ((uint)carrier < L2HubCount && selection.Classification == ProofClassification.Certain)
                    changed = (DecodeCarrierPetals[carrier][(int)(completed >> 5)] &
                        (1u << (int)(completed & 31u))) != 0u;
                if (!changed && TryReadDirectCarrier(carrier, out ProofClassification cached,
                        out symbol, out unresolved, out _, roots, positions)) return cached;
                return _reader.ClassifyPageCarrier(_owner, carrier, _errors, out symbol, out unresolved,
                    out _, roots, positions, this,
                    selection.Classification == ProofClassification.Certain ? selection.Token : uint.MaxValue);
            }

            private static uint2 Mask(ulong value) => new((uint)value, (uint)(value >> 32));

            private bool TryDirectAnchor(int petal, int node, out PhaseRootEvidence selected)
            {
                selected = default;
                ulong bit = 1UL << petal;
                if ((ConfirmedDirect & bit) == 0u) return false;
                for (int anchor = 0; anchor < 3; anchor++)
                {
                    if (PetalsValue[petal].Node(anchor) != node) continue;
                    if ((_directAnchorSeen[anchor] & bit) == 0u || (_directAnchorConflict & bit) != 0u)
                        return false;
                    selected = _directAnchors[3 * petal + anchor];
                    return ClassifyPhaseSector(selected, out _) == ProofClassification.Certain &&
                        TryGetAnchorRootReferences(node, selected.Symbol.Tag, out ulong references) &&
                        (references & bit) != 0u;
                }
                return false;
            }

            private ProofClassification ReadIncidentAnchor(int petal, int anchor, out PhaseRootEvidence root,
                out bool uniqueSymbol)
            {
                root = default; uniqueSymbol = false;
                int node = PetalsValue[petal].Node(anchor);
                uint2 neighbourMask = M8FlowerCompletionNeighbours[petal];
                ulong neighbours = neighbourMask.x | ((ulong)neighbourMask.y << 32);
                // Bx cancels this candidate's actual shared edges. Their
                // confirmed neighbours, not any petal merely touching this
                // node, must agree on the SAME endpoint relation.
                ulong donors = neighbours & NodeIncidentPetalsValue[node] & ConfirmedDirect;
                bool found = false;
                while (donors != 0u)
                {
                    int donor = (uint)donors != 0u ? math.tzcnt((uint)donors) :
                        32 + math.tzcnt((uint)(donors >> 32));
                    donors &= donors - 1UL;
                    if (!TryDirectAnchor(donor, node, out PhaseRootEvidence candidate))
                        return ProofClassification.Ambiguous;
                    if (!TryGetAnchorRootReferences(node, candidate.Symbol.Tag, out ulong allowed))
                        return ProofClassification.Ambiguous;
                    if ((allowed & (1UL << petal)) == 0u)
                    { uniqueSymbol = false; return ProofClassification.Impossible; }
                    if (!found) { root = candidate; found = true; uniqueSymbol = true; continue; }
                    if (math.any(root.Symbol.Junction != candidate.Symbol.Junction) ||
                        (root.Symbol.Tag & 0x1fffu) != (candidate.Symbol.Tag & 0x1fffu))
                    { uniqueSymbol = false; return ProofClassification.Impossible; }
                    ProofClassification status = CloseSharedPhaseRoot(root, candidate, out PhaseRootEvidence closed);
                    if (status != ProofClassification.Certain) return status;
                    root = closed;
                }
                return found ? ProofClassification.Certain : ProofClassification.Impossible;
            }
        }

        /// <summary>Read-only CPU view of the same frozen M8/sidecar tile
        /// packets used by residency. Missing context remains unresolved;
        /// only absence in the captured storage index means absent M8 data.
        /// No surface, fitted plane, adjacency graph or welded XYZ is stored.</summary>
        internal sealed class SnapshotReader
        {
            private sealed class Tile
            {
                internal readonly KernelState[] States;
                internal readonly Dictionary<int, uint> Epochs = new();
                internal readonly Dictionary<(int Owner, uint Key), MerkabaFlowerDetailRecord> Phases = new();
                internal readonly Dictionary<(int Owner, uint Key), MerkabaThreadRun> Threads = new();
                internal readonly Dictionary<(int Owner, uint Key), MerkabaFlowerSkinMetricRun> Metrics = new();
                internal readonly Dictionary<(int Owner, uint Group), MerkabaThreadColorGroup> Colors = new();
                internal readonly Dictionary<(int Owner, uint Group), MerkabaFlowerVGroup> Amplitudes = new();
                internal readonly Dictionary<uint, MerkabaThreadProgramRecord> Programs = new();

                internal Tile(MerkabaTileSnapshot snapshot)
                {
                    if (snapshot.States == null || snapshot.States.Length != MerkabaSpatial.KernelsPerTile)
                        throw new InvalidDataException("Incomplete frozen M8 tile for Flower evaluation.");
                    States = (KernelState[])snapshot.States.Clone();
                    foreach (MerkabaAppendRecord record in snapshot.Sidecars)
                    {
                        if (record.Kind != MerkabaRecordKind.FlowerOwnerEpoch &&
                            record.Kind != MerkabaRecordKind.FlowerDetail &&
                            record.Kind != MerkabaRecordKind.ThreadRun &&
                            record.Kind != MerkabaRecordKind.FlowerSkinMetricRun &&
                            record.Kind != MerkabaRecordKind.ThreadColorGroup &&
                            record.Kind != MerkabaRecordKind.FlowerVGroup &&
                            record.Kind != MerkabaRecordKind.ThreadProgram) continue;
                        MerkabaSphereFlowerReplayIndex.ValidateRecord(record);
                        if (record.Kind == MerkabaRecordKind.ThreadProgram)
                        {
                            uint key = MerkabaSphereFlowerPersistenceAbi.ReadProgramAddress(record.Address);
                            if (!Programs.TryAdd(key, MemoryMarshal.Read<MerkabaThreadProgramRecord>(record.Payload)))
                                throw new InvalidDataException("Duplicate Thread program in a frozen tile image.");
                            continue;
                        }
                        if (record.Kind == MerkabaRecordKind.ThreadColorGroup ||
                            record.Kind == MerkabaRecordKind.FlowerVGroup)
                        {
                            MerkabaSphereFlowerPersistenceAbi.ReadGroupAddress(record.Address,
                                out MerkabaTileAddress groupTile, out int groupOwner, out uint group);
                            if (!groupTile.Equals(snapshot.Address))
                                throw new InvalidDataException("Flower signal group belongs to a different snapshot tile.");
                            bool added = record.Kind == MerkabaRecordKind.ThreadColorGroup
                                ? Colors.TryAdd((groupOwner, group), MemoryMarshal.Read<MerkabaThreadColorGroup>(record.Payload))
                                : Amplitudes.TryAdd((groupOwner, group), MemoryMarshal.Read<MerkabaFlowerVGroup>(record.Payload));
                            if (!added) throw new InvalidDataException("Duplicate Flower signal group in a frozen tile image.");
                            continue;
                        }
                        if (record.Kind == MerkabaRecordKind.FlowerOwnerEpoch)
                        {
                            if (!MerkabaSphereFlowerPersistenceAbi.ReadTileAddress(record.Address).Equals(snapshot.Address))
                                throw new InvalidDataException("Flower owner epoch belongs to a different snapshot tile.");
                            int owner = checked((int)MerkabaSphereFlowerPersistenceAbi.ReadUInt32(record.Payload, 0));
                            uint epoch = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(record.Payload, 4);
                            if (!Epochs.TryAdd(owner, epoch))
                                throw new InvalidDataException("Duplicate owner epoch in a complete Flower tile image.");
                        }
                        else
                        {
                            MerkabaSphereFlowerPersistenceAbi.ReadOwnerAddress(record.Address,
                                out MerkabaTileAddress tile, out int owner);
                            if (!tile.Equals(snapshot.Address))
                                throw new InvalidDataException("Flower phase belongs to a different snapshot tile.");
                            if (record.Kind == MerkabaRecordKind.ThreadRun)
                            {
                                var run = MemoryMarshal.Read<MerkabaThreadRun>(record.Payload);
                                if (!Threads.TryAdd((owner, run.FlowerKey), run))
                                    throw new InvalidDataException("Duplicate ThreadRun in a frozen tile image.");
                                continue;
                            }
                            if (record.Kind == MerkabaRecordKind.FlowerSkinMetricRun)
                            {
                                var run = MemoryMarshal.Read<MerkabaFlowerSkinMetricRun>(record.Payload);
                                if (!Metrics.TryAdd((owner, run.FlowerKey), run))
                                    throw new InvalidDataException("Duplicate metric-skin run in a frozen tile image.");
                                continue;
                            }
                            var phase = new MerkabaFlowerDetailRecord
                            {
                                Key = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(record.Payload, 0),
                                Lower = MerkabaSphereFlowerPersistenceAbi.ReadInt32(record.Payload, 4),
                                Upper = MerkabaSphereFlowerPersistenceAbi.ReadInt32(record.Payload, 8),
                                ParentEpoch = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(record.Payload, 12)
                            };
                            if (!Phases.TryAdd((owner, phase.Key), phase))
                                throw new InvalidDataException("Duplicate phase key in a complete Flower tile image.");
                        }
                    }
                    foreach (var key in Phases.Keys)
                        if (!Epochs.ContainsKey(key.Owner))
                            throw new InvalidDataException("Frozen Flower detail has no parent owner epoch.");
                    foreach (var key in Threads.Keys)
                        if (!Epochs.ContainsKey(key.Owner))
                            throw new InvalidDataException("Frozen ThreadRun has no parent owner epoch.");
                    foreach (var key in Metrics.Keys)
                        if (!Epochs.ContainsKey(key.Owner))
                            throw new InvalidDataException("Frozen metric-skin run has no parent owner epoch.");
                }
            }

            private readonly MerkabaTileAddress[] _stored;
            private readonly Dictionary<MerkabaTileAddress, Tile> _tiles = new();
            private readonly Func<int3, ExcavationCellState> _readCell;
            private ulong _requiredSupportVersion;
            private RequiredSupportReceipt _requiredSupportReceipt;

            internal ulong RequiredSupportVersion => _requiredSupportVersion;

            internal bool TryRequiredSupportSince(ulong version, out RequiredSupportReceipt receipt)
            {
                receipt = _requiredSupportReceipt;
                return _requiredSupportVersion != version;
            }

            private void RecordRequiredSupport(int3 owner, int carrier, uint wedgeMask)
            {
                if (wedgeMask == 0u) return;
                _requiredSupportReceipt = new RequiredSupportReceipt(owner, carrier, wedgeMask);
                _requiredSupportVersion = checked(_requiredSupportVersion + 1UL);
            }

            internal void RecordRequiredJunctionSupport(int3 owner, int petal, uint rootAlternatives)
            {
                if (rootAlternatives == 0u) return;
                _requiredSupportReceipt = new RequiredSupportReceipt(owner, -1, 0u, petal, rootAlternatives);
                _requiredSupportVersion = checked(_requiredSupportVersion + 1UL);
            }

            internal SnapshotReader(IEnumerable<MerkabaTileSnapshot> snapshots,
                MerkabaTileAddress[] storedIndex, Func<int3, ExcavationCellState> readCell)
            {
                if (snapshots == null) throw new ArgumentNullException(nameof(snapshots));
                if (storedIndex == null) throw new ArgumentNullException(nameof(storedIndex));
                if (readCell == null) throw new ArgumentNullException(nameof(readCell));
                // CaptureStoredTileIndex already returns the immutable sorted
                // M8 index. Reuse it; do not build a second world address map.
                _stored = storedIndex;
                _readCell = readCell;
                foreach (MerkabaTileSnapshot snapshot in snapshots)
                {
                    if (snapshot == null || Array.BinarySearch(_stored, snapshot.Address) < 0 ||
                        !_tiles.TryAdd(snapshot.Address, new Tile(snapshot)))
                        throw new InvalidDataException("Invalid or duplicate frozen Flower snapshot tile.");
                }
            }

            internal bool TryReadOwner(int3 owner, out KernelState state, out uint epoch)
            {
                state = default;
                epoch = 0u;
                ResolveOwner(owner, out MerkabaTileAddress address, out int local);
                if (!_tiles.TryGetValue(address, out Tile tile))
                    return Array.BinarySearch(_stored, address) < 0;
                state = tile.States[local];
                tile.Epochs.TryGetValue(local, out epoch);
                return true;
            }

            internal FlowerDecode BeginFlowerDecode(int3 owner, float2 errors) => new(this, owner, errors);

            internal MerkabaFlowerSkinDrawSample[] ReadSkinSignal(int3 owner,
                in MerkabaFlowerSymbolRecord symbol, out MerkabaFlowerSkinDrawHeader header)
                => ReadSkinSignal(owner, symbol, out header, out _);

            internal MerkabaFlowerSkinDrawSample[] ReadSkinSignal(int3 owner,
                in MerkabaFlowerSymbolRecord symbol, out MerkabaFlowerSkinDrawHeader header,
                out uint2 rgbSplitBits)
            {
                rgbSplitBits = default;
                if (!TryReadOwner(owner, out KernelState state, out uint epoch) || !StableR1(state.Flags))
                    throw new InvalidDataException("A direct Flower signal needs its frozen canonical owner.");
                ResolveOwner(owner, out MerkabaTileAddress address, out int local);
                if (!_tiles.TryGetValue(address, out Tile tile))
                    throw new InvalidDataException("Flower signal owner context is unresolved.");
                L2WedgeRule representative = L2Wedges[6 * symbol.CarrierId];
                uint key = MerkabaFlowerL2Key.Create(representative.ChildPath, representative.Petal,
                    (symbol.RootSigns & 1u) != 0u, symbol.HubSector).Value;
                MerkabaThreadRun? rgb = null;
                MerkabaFlowerSkinMetricRun? metric = null;
                MerkabaThreadProgramRecord? optical = null;
                MerkabaThreadColorGroup[] colors = Array.Empty<MerkabaThreadColorGroup>();
                MerkabaFlowerVGroup[] amplitudes = Array.Empty<MerkabaFlowerVGroup>();
                if (tile.Threads.TryGetValue((local, key), out MerkabaThreadRun thread) && thread.ParentEpoch == epoch)
                {
                    if (!thread.IsValidFor(epoch)) throw new InvalidDataException("Invalid frozen ThreadRun.");
                    rgb = thread;
                    rgbSplitBits = new uint2(thread.SplitBitsLo, thread.SplitBitsHi);
                    colors = new MerkabaThreadColorGroup[thread.GroupCount];
                    for (int group = 0; group < colors.Length; group++)
                        if (!tile.Colors.TryGetValue((local, checked(thread.GroupBase + (uint)group)), out colors[group]))
                            throw new InvalidDataException("Frozen ThreadRun is missing an explicit color group.");
                    if (thread.ProgramRef != MerkabaThreadRun.InvalidRef)
                    {
                        if (!tile.Programs.TryGetValue(thread.ProgramRef, out MerkabaThreadProgramRecord program))
                            throw new InvalidDataException("Frozen ThreadRun optical program is missing.");
                        optical = program;
                    }
                }
                if (tile.Metrics.TryGetValue((local, key), out MerkabaFlowerSkinMetricRun run) && run.ParentEpoch == epoch)
                {
                    if (!run.IsValidFor(epoch)) throw new InvalidDataException("Invalid frozen metric-skin run.");
                    metric = run;
                    amplitudes = new MerkabaFlowerVGroup[run.GroupCount];
                    for (int group = 0; group < amplitudes.Length; group++)
                        if (!tile.Amplitudes.TryGetValue((local, checked(run.GroupBase + (uint)group)), out amplitudes[group]))
                            throw new InvalidDataException("Frozen metric-skin run is missing an explicit V group.");
                }
                float3 capture = new float3(state.PackedColor & 255u, (state.PackedColor >> 8) & 255u,
                    (state.PackedColor >> 16) & 255u) * (1f / 255f);
                return CompileSkinDrawSamples(capture, epoch, rgb, colors, metric, amplitudes, optical, out header);
            }

            internal ProofClassification ClassifyPageCarrier(int3 owner, int carrier, float2 errors,
                out MerkabaFlowerSymbolRecord symbol, out uint unresolved,
                Span<PhaseRootEvidence> roots, Span<float3> positions, bool requiredSupport = true)
                => ClassifyPageCarrier(owner, carrier, errors, out symbol, out unresolved,
                    out _, roots, positions, requiredSupport: requiredSupport);

            internal ProofClassification ClassifyPageCarrier(int3 owner, int carrier, float2 errors,
                out MerkabaFlowerSymbolRecord symbol, out uint unresolved, out uint directWedges,
                Span<PhaseRootEvidence> roots, Span<float3> positions,
                FlowerDecode parentSnapshot = null, uint completionToken = uint.MaxValue,
                bool requiredSupport = true, bool recordParentDirect = true)
            {
                if (roots.Length != L2CarrierSiteCount || positions.Length != L2CarrierSiteCount)
                    throw new ArgumentException("A Flower carrier has seven shared knot sites.");
                parentSnapshot?.RequireSource(this, owner, errors);
                bool recordParent = parentSnapshot != null && !parentSnapshot.IsComplete && recordParentDirect;
                if (recordParent && parentSnapshot.TryReadDirectCarrier(carrier,
                        out ProofClassification cached, out symbol, out unresolved, out directWedges,
                        roots, positions)) return cached;
                ProofClassification status = ClassifyL2Carrier(owner, carrier, errors,
                    out symbol, out unresolved, out directWedges, roots, positions, parentSnapshot, completionToken);
                uint owned = 0u;
                for (int wedge = 0; wedge < 6; wedge++)
                    if (L2OwnsWedge(carrier, wedge)) owned |= 1u << wedge;
                unresolved &= owned;
                if (status != ProofClassification.Certain)
                {
                    if (recordParent) parentSnapshot.RecordCarrier(carrier, 0u, 0u, 0u, 0u);
                    ProofClassification result = status == ProofClassification.Ambiguous && unresolved != 0u
                        ? ProofClassification.Ambiguous : ProofClassification.Impossible;
                    if (recordParent) parentSnapshot.StoreDirectCarrier(carrier, result, symbol, unresolved,
                        directWedges, roots, positions);
                    return result;
                }
                uint rawActive = symbol.ActiveWedgeMask;
                uint active = rawActive & owned;
                uint dualClear = 0u, dualVeto = 0u, requiredAmbiguous = 0u;
                uint supportNeeded = recordParent ? rawActive : active | directWedges;
                for (int wedge = 0; wedge < 6; wedge++)
                {
                    uint bit = 1u << wedge;
                    if ((supportNeeded & bit) == 0u) continue;
                    ProofClassification dual = SupportWedgeDual(owner, carrier, wedge, roots, _readCell);
                    if (dual == ProofClassification.Impossible) dualClear |= bit;
                    else
                    {
                        active &= ~bit;
                        directWedges &= ~bit;
                        if (dual == ProofClassification.Certain) dualVeto |= bit;
                    }
                    if (dual == ProofClassification.Ambiguous && (owned & bit) != 0u)
                    {
                        unresolved |= bit;
                        requiredAmbiguous |= bit;
                    }
                }
                // The finite classifier still reports its unchanged masks.
                // A page transaction must ALSO observe this required-support
                // receipt; returning a remaining active wedge is not closure.
                // Optional pairing probes do not make an unused halo required.
                if (requiredSupport) RecordRequiredSupport(owner, carrier, requiredAmbiguous);
                if (recordParent) parentSnapshot.RecordCarrier(carrier, rawActive, directWedges, dualClear, dualVeto, roots);
                symbol.OwnerAndCarrier = (symbol.OwnerAndCarrier &
                    ~(MerkabaFlowerSymbolRecord.WedgeMask << MerkabaFlowerSymbolRecord.ActiveWedgeShift)) |
                    (active << MerkabaFlowerSymbolRecord.ActiveWedgeShift);
                uint used = 0u;
                for (int wedge = 0; wedge < 6; wedge++)
                    if ((active & (1u << wedge)) != 0u)
                        used |= 1u | (1u << (1 + wedge)) | (1u << (1 + (wedge + 1) % 6));
                symbol.RootsAndWedges = ((uint)symbol.HubSector << MerkabaFlowerSymbolRecord.HubSectorShift) |
                    (symbol.RootSigns & used) |
                    ((symbol.CompletedWedgeMask & active) << MerkabaFlowerSymbolRecord.CompletedWedgeShift) |
                    ((symbol.ReverseWedgeMask & active) << MerkabaFlowerSymbolRecord.ReverseWedgeShift);
                for (int site = 0; site < 7; site++)
                    if ((used & (1u << site)) == 0u) { roots[site] = default; positions[site] = default; }
                status = active != 0u ? ProofClassification.Certain : unresolved != 0u ?
                    ProofClassification.Ambiguous : ProofClassification.Impossible;
                if (recordParent) parentSnapshot.StoreDirectCarrier(carrier, status, symbol, unresolved,
                    directWedges, roots, positions);
                return status;
            }

            internal ProofClassification CompletionPetalProof(int3 owner, int petal, uint token,
                float2 errors, FlowerDecode snapshot)
            {
                Span<PhaseRootEvidence> roots = stackalloc PhaseRootEvidence[7];
                Span<float3> positions = stackalloc float3[7];
                bool ambiguous = false;
                int requiredCarrier = -1;
                uint requiredWedge = 0u;
                // The inverse generated source permutation covers this
                // WHOLE parent: all sixteen children, before owner emission
                // ownership. There is no new/extrapolated candidate geometry.
                for (int path = 0; path < 16; path++)
                {
                    int index = L2WedgeIndex(petal, path), carrier = index / 6, wedge = index % 6;
                    ProofClassification status = ClassifyL2Carrier(owner, carrier, errors,
                        out MerkabaFlowerSymbolRecord symbol, out uint unresolved, out _,
                        roots, positions, snapshot, token);
                    uint bit = 1u << wedge;
                    if (status != ProofClassification.Certain || (symbol.ActiveWedgeMask & bit) == 0u)
                    {
                        if ((unresolved & bit) != 0u) { ambiguous = true; continue; }
                        return ProofClassification.Impossible;
                    }
                    ProofClassification dual = SupportWedgeDual(owner, carrier, wedge, roots, _readCell);
                    if (dual == ProofClassification.Certain) return ProofClassification.Impossible;
                    if (dual != ProofClassification.Impossible)
                    {
                        ambiguous = true;
                        requiredCarrier = carrier;
                        requiredWedge = bit;
                    }
                }
                // A later impossible child makes this entire candidate
                // irrelevant. Only a still-possible completion can require
                // resolving an earlier mixed/COLD child support.
                if (requiredCarrier >= 0) RecordRequiredSupport(owner, requiredCarrier, requiredWedge);
                return ambiguous ? ProofClassification.Ambiguous : ProofClassification.Certain;
            }

            private static void ResolveOwner(int3 owner, out MerkabaTileAddress tile, out int local)
            {
                MerkabaSpatial.Address address = MerkabaSpatial.Encode(owner);
                tile = new MerkabaTileAddress(address.BlockCoord,
                    (uint)(address.ChunkLocal | (address.TileLocal << 9)));
                local = address.KernelLocal;
            }

            private bool TryReadPhase(int3 owner, uint epoch, MerkabaFlowerDetailKey key,
                out MerkabaFlowerDetailRecord record)
            {
                record = default;
                if (epoch == 0u) return false;
                ResolveOwner(owner, out MerkabaTileAddress address, out int local);
                return _tiles.TryGetValue(address, out Tile tile) &&
                    tile.Epochs.TryGetValue(local, out uint actualEpoch) && actualEpoch == epoch &&
                    tile.Phases.TryGetValue((local, key.Value), out record) && record.IsValidFor(epoch);
            }

            private bool HasPhaseFamily(int3 owner, uint epoch, int level, int path,
                int petal, int line, bool plus)
            {
                if (epoch == 0u) return false;
                MerkabaFlowerDetailKind kind = LinesValue[line].Shell == Shell.R3Closure
                    ? MerkabaFlowerDetailKind.R3Phase : MerkabaFlowerDetailKind.R2Phase;
                for (int sector = 0; sector < LinesValue[line].SectorCount; sector++)
                    if (TryReadPhase(owner, epoch, MerkabaFlowerDetailKey.Create(level,
                        path, petal, line, kind, plus, sector), out _)) return true;
                return false;
            }

            private static int FirstPetal(ulong mask) => (uint)mask != 0u
                ? math.tzcnt((uint)mask) : 32 + math.tzcnt((uint)(mask >> 32));

            private static bool StableR1(uint flags) =>
                (flags & (M8_FLOWER_OCCUPIED_FLAG | M8_FLOWER_PLANE_VALID | M8_FLOWER_SEED_FLAG)) ==
                (M8_FLOWER_OCCUPIED_FLAG | M8_FLOWER_PLANE_VALID);

            private static bool TryGeometryTetraParity(int3 owner, GeometryNode node, out int parity)
            {
                parity = 0;
                if ((uint)node.Level >= GeometryLevelCount || (uint)node.Line >= LineClassCount) return false;
                sbyte endpoint;
                if (node.Level == 0)
                {
                    if ((uint)node.RootNode >= NodeClassCount || NodesValue[node.RootNode].LineClass != node.Line ||
                        math.any(NodesValue[node.RootNode].Direction != node.Offset)) return false;
                    endpoint = NodesValue[node.RootNode].Orientation;
                }
                else if (!TryGetChildPhaseLoop(node.Petal, node.ParentContext, node.KnotSite,
                    out int level, out int3 offset, out int line, out _, out endpoint, out _, out int inherited) ||
                    inherited >= 0 || level != node.Level || line != node.Line || math.any(offset != node.Offset))
                    return false;
                if ((endpoint != 1 && endpoint != -1) ||
                    !TryOwnerJunction(owner, node.Level, node.Offset, out int3 junction)) return false;
                int3 direction = endpoint * LinesValue[node.Line].Direction;
                if (math.any(((junction ^ direction) & 1) != 0)) return false;
                // Same overflow-safe exact (J-directedEndpoint)/2 as HLSL.
                int3 cell = (junction >> 1) + (((junction & 1) - direction) / 2);
                parity = (cell.x & 1) | ((cell.y & 1) << 1) | ((cell.z & 1) << 2);
                return true;
            }

            private ProofClassification ApplyOwnPhase(int3 owner, uint epoch, MerkabaFlowerDetailKey key,
                GeometryNode node, PhaseRootEvidence prediction, out PhaseRootEvidence root)
            {
                root = prediction;
                if (!TryReadPhase(owner, epoch, key, out MerkabaFlowerDetailRecord record))
                {
                    if (!HasPhaseFamily(owner, epoch, key.GeometryLevel, key.GeometryChildPath,
                        key.PetalClass, key.Channel, key.RootSign)) return ProofClassification.Certain;
                    root = WithStatus(prediction, ProofClassification.Ambiguous);
                    return ProofClassification.Ambiguous;
                }
                if (key.Kind == MerkabaFlowerDetailKind.R2Phase)
                {
                    ProofClassification status = SynthesizePhaseRecord(prediction, key, record, epoch, out root);
                    root = WithStatus(root, status);
                    return status;
                }
                root = WithStatus(prediction, ProofClassification.Ambiguous);
                if (key.Kind != MerkabaFlowerDetailKind.R3Phase) return ProofClassification.Ambiguous;
                if (!TryGeometryTetraParity(owner, node, out int parity)) return ProofClassification.Ambiguous;
                int eta = 0;
                for (int axis = 0; axis < 4; axis++)
                    if (TetraFramesValue[parity].LineClasses[axis] == key.Channel)
                        eta = TetraFramesValue[parity].Eta[axis];
                if (eta == 0) return ProofClassification.Ambiguous;
                FloatInterval turn = DecodePhaseInterval(record.Lower, record.Upper);
                if (eta < 0) turn = new FloatInterval(-turn.Upper, -turn.Lower);
                if (RotatePhaseEvidence(prediction, turn, out PhaseRootEvidence rotated) !=
                    ProofClassification.Certain)
                    return ProofClassification.Ambiguous;
                root = rotated;
                return ProofClassification.Certain;
            }

            internal ProofClassification ReadOriginalLocal(int3 owner, int nodeIndex, bool plus,
                float normalError, float offsetError, out PhaseRootEvidence root)
            {
                root = default;
                if ((uint)nodeIndex >= NodeClassCount) return ProofClassification.Impossible;
                if (!TryReadOwner(owner, out KernelState state, out uint epoch))
                { root = Unresolved; return ProofClassification.Ambiguous; }
                if (!StableR1(state.Flags)) return ProofClassification.Impossible;
                NodeRule node = NodesValue[nodeIndex];
                ProofClassification status = CarrierRootProof(owner, state.Flags, 0, node.Direction,
                    node.LineClass, plus, normalError, offsetError, out root);
                if (status != ProofClassification.Certain) return status;
                if (node.Shell == Shell.R1Core) return ProofClassification.Certain;
                var key = MerkabaFlowerDetailKey.Create(0, 0, FirstPetal(NodeIncidentPetalsValue[nodeIndex]),
                    node.LineClass, node.Shell == Shell.R3Closure ? MerkabaFlowerDetailKind.R3Phase :
                        MerkabaFlowerDetailKind.R2Phase, plus, (int)((root.Symbol.Tag >> 8) & 31u));
                var geometryNode = new GeometryNode
                {
                    Line = node.LineClass, Offset = node.Direction, RootNode = nodeIndex,
                    Petal = key.PetalClass, Plus = plus, Kind = key.Kind
                };
                return ApplyOwnPhase(owner, epoch, key, geometryNode, root, out root);
            }

            internal ProofClassification ReadOriginalShared(int3 owner, int nodeIndex, bool plus,
                float normalError, float offsetError, out PhaseRootEvidence root)
                => ReadOriginalShared(owner, nodeIndex, plus, normalError, offsetError, out root, out _);

            internal ProofClassification ReadOriginalShared(int3 owner, int nodeIndex, bool plus,
                float normalError, float offsetError, out PhaseRootEvidence root, out bool provisional)
            {
                provisional = false;
                ProofClassification status = ReadOriginalLocal(owner, nodeIndex, plus,
                    normalError, offsetError, out PhaseRootEvidence local);
                root = local;
                if (status != ProofClassification.Certain) return status;
                NodeRule node = NodesValue[nodeIndex];
                long x = (long)owner.x + node.Direction.x;
                long y = (long)owner.y + node.Direction.y;
                long z = (long)owner.z + node.Direction.z;
                if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue ||
                    z < int.MinValue || z > int.MaxValue)
                { root = WithStatus(local, ProofClassification.Ambiguous); return ProofClassification.Ambiguous; }
                int3 otherOwner = new((int)x, (int)y, (int)z);
                if (!TryReadOwner(otherOwner, out KernelState peerState, out _))
                { root = WithStatus(local, ProofClassification.Ambiguous); return ProofClassification.Ambiguous; }
                if (!StableR1(peerState.Flags) && node.Shell != Shell.R1Core)
                {
                    // Same provisional higher-shell anchor as live readout;
                    // resolved absence never substitutes for COLD evidence.
                    provisional = true;
                    root = local;
                    return ProofClassification.Certain;
                }
                status = ReadOriginalLocal(otherOwner, nodeIndex ^ 1, plus,
                    normalError, offsetError, out PhaseRootEvidence other);
                if (status != ProofClassification.Certain)
                { root = WithStatus(local, status); return status; }
                // Both original endpoint innovations are synthesized BEFORE
                // canonical SEAL. Persisted local residuals are never rebased.
                status = SealPhaseRelation(node.Orientation > 0 ? local : other,
                    node.Orientation > 0 ? other : local, out root, out _);
                root = WithStatus(root, status);
                return status;
            }

            private static ProofClassification R3MetricResidual(PhaseRootEvidence predicted,
                PhaseRootEvidence observed, int parity, int axis, out FloatInterval q)
            {
                q = FloatInterval.Singleton(0f);
                if (predicted.Classification == ProofClassification.Impossible ||
                    observed.Classification == ProofClassification.Impossible) return ProofClassification.Impossible;
                if ((uint)parity >= TetraFrameCount || (uint)axis >= 4u ||
                    predicted.Classification != ProofClassification.Certain || observed.Classification != ProofClassification.Certain ||
                    !TryPhaseIdentity(predicted, out var p) || !TryPhaseIdentity(observed, out var o))
                    return ProofClassification.Ambiguous;
                if (ClassifyPhaseSector(predicted, out int pSector) != ProofClassification.Certain ||
                    ClassifyPhaseSector(observed, out int oSector) != ProofClassification.Certain)
                    return ProofClassification.Ambiguous;
                if (math.any(predicted.Symbol.Junction != observed.Symbol.Junction) ||
                    (predicted.Symbol.Tag & 0x1fffu) != (observed.Symbol.Tag & 0x1fffu) ||
                    pSector != p.Sector || oSector != o.Sector || TetraFramesValue[parity].LineClasses[axis] != p.LineClass)
                    return ProofClassification.Impossible;
                bool exactIdentity = predicted.Root.X.IsSingleton && predicted.Root.Y.IsSingleton &&
                    observed.Root.X.IsSingleton && observed.Root.Y.IsSingleton &&
                    predicted.Root.X.Lower == observed.Root.X.Lower && predicted.Root.Y.Lower == observed.Root.Y.Lower;
                if (!exactIdentity && (TangentHalfAngle(predicted.Root, observed.Root, out q) != ProofClassification.Certain ||
                    !float.IsFinite(q.Lower) || !float.IsFinite(q.Upper))) return ProofClassification.Ambiguous;
                if (TetraFramesValue[parity].Eta[axis] < 0) q = new FloatInterval(-q.Upper, -q.Lower);
                return ProofClassification.Certain;
            }

            // The actual original R3 root interval, not its owner's box or a
            // midpoint sample. Uses the same ordered bounds and dyadic cell
            // cover as the full-wedge predicate above.
            internal ProofClassification RootSupportDual(int3 owner, int nodeIndex, PhaseRootEvidence root)
            {
                if ((uint)nodeIndex >= NodeClassCount)
                    return ProofClassification.Ambiguous;
                NodeRule node = NodesValue[nodeIndex];
                if (!TryOwnerJunction(owner, 0, node.Direction, out int3 junction) ||
                    math.any(root.Symbol.Junction != junction) ||
                    !RootRelativeBounds(0, node.Direction, node.LineClass, root, out Interval3 relative))
                    return ProofClassification.Ambiguous;
                int3 origin = (owner >> 3) << 3;
                int3 relativeOwner = owner - origin;
                FloatInterval step = FloatInterval.Singleton(LevelStep(0));
                int3 first = default, last = default;
                for (int axis = 0; axis < 3; axis++)
                {
                    FloatInterval translation = FloatInterval.Multiply(FloatInterval.Singleton(relativeOwner[axis]), step);
                    FloatInterval cells = FloatInterval.Divide(FloatInterval.Add(relative[axis], translation), step);
                    if (!float.IsFinite(cells.Lower) || !float.IsFinite(cells.Upper) ||
                        cells.Lower < -8f || cells.Upper >= 15f)
                        return ProofClassification.Ambiguous;
                    first[axis] = (int)Math.Floor(cells.Lower);
                    last[axis] = (int)Math.Floor(cells.Upper);
                }
                return ClassifySupportCellCover(origin, first, last, _readCell);
            }

            internal JunctionSelection ReadR3Junction(int3 owner, float2 errors,
                FlowerDecode parentSnapshot = null, int requiredPetal = -1)
            {
                var unresolved = new JunctionSelection { Classification = JunctionClassification.Ambiguous,
                    ClassIndex = uint.MaxValue, RootSigns = uint.MaxValue };
                if (!TryReadOwner(owner, out KernelState state, out _)) return unresolved;
                if (!StableR1(state.Flags))
                { unresolved.Classification = JunctionClassification.Impossible; return unresolved; }
                parentSnapshot?.RequireSource(this, owner, errors);
                int parity = (owner.x & 1) | ((owner.y & 1) << 1) | ((owner.z & 1) << 2);
                TetraFrameRule frame = TetraFramesValue[parity];
                Span<FloatInterval> q = stackalloc FloatInterval[8];
                Span<uint> tags = stackalloc uint[8];
                q.Clear(); tags.Clear();
                uint known = 0u, ambiguous = 0u, allowed = 0u, veto = 0u;
                for (int axis = 0; axis < 4; axis++)
                for (int sign = 0; sign < 2; sign++)
                {
                    int index = 2 * axis + sign;
                    uint bit = 1u << index;
                    int line = frame.LineClasses[axis];
                    int node = 2 * line + (frame.Eta[axis] < 0 ? 1 : 0);
                    ProofClassification status = CarrierRootProof(owner, state.Flags, 0,
                        NodesValue[node].Direction, line, sign != 0, errors.x, errors.y,
                        out PhaseRootEvidence predicted);
                    if (status != ProofClassification.Certain)
                    { if (status != ProofClassification.Impossible) ambiguous |= bit; continue; }
                    PhaseRootEvidence observed;
                    bool provisional;
                    status = parentSnapshot == null
                        ? ReadOriginalShared(owner, node, sign != 0, errors.x, errors.y, out observed, out provisional)
                        : parentSnapshot.ReadOriginal(node, sign != 0, out observed, out provisional);
                    if (status != ProofClassification.Certain || provisional)
                    { if (status != ProofClassification.Impossible) ambiguous |= bit; continue; }
                    status = R3MetricResidual(predicted, observed, parity, axis, out q[index]);
                    if (status != ProofClassification.Certain)
                    { if (status != ProofClassification.Impossible) ambiguous |= bit; continue; }
                    known |= bit;
                    tags[index] = observed.Symbol.Tag;
                    ProofClassification support = RootSupportDual(owner, node, observed);
                    if (support == ProofClassification.Impossible) allowed |= bit;
                    else if (support == ProofClassification.Certain) veto |= bit;
                    // Mixed/COLD leaves a known metric with unresolved dual
                    // permission. It never becomes an all-true shell mask.
                }
                return SelectR3Junction(parity, q, tags, known, ambiguous, allowed, veto, requiredPetal);
            }

            internal ProofClassification ReadR3MetricBundle(int3 owner, uint rootSigns, float2 errors,
                Span<FloatInterval> q, out R3MetricEvidence evidence)
            {
                if (q.Length != 4) throw new ArgumentException("Four canonical R3 axes are required.", nameof(q));
                q.Clear(); evidence = default;
                if (rootSigns >= 16u) return ProofClassification.Impossible;
                if (!TryReadOwner(owner, out KernelState state, out _)) return ProofClassification.Ambiguous;
                if (!StableR1(state.Flags)) return ProofClassification.Impossible;
                int parity = (owner.x & 1) | ((owner.y & 1) << 1) | ((owner.z & 1) << 2);
                TetraFrameRule frame = TetraFramesValue[parity];
                int chirality = frame.Chirality;
                uint resolved = 0u, exactZero = 0u, provisionalAxes = 0u, impossible = 0u;
                for (int axis = 0; axis < 4; axis++)
                {
                    bool plus = (rootSigns & (1u << axis)) != 0u;
                    chirality *= plus ? 1 : -1;
                    int line = frame.LineClasses[axis];
                    int node = 2 * line + (frame.Eta[axis] < 0 ? 1 : 0);
                    // R2 substitutions keep every original R3 anchor exact;
                    // no child phase is added to this inherited prediction.
                    ProofClassification status = CarrierRootProof(owner, state.Flags, 0,
                        NodesValue[node].Direction, line, plus, errors.x, errors.y, out PhaseRootEvidence prediction);
                    if (status != ProofClassification.Certain)
                    { if (status == ProofClassification.Impossible) impossible |= 1u << axis; continue; }
                    status = ReadOriginalShared(owner, node, plus, errors.x, errors.y,
                        out PhaseRootEvidence observed, out bool provisional);
                    if (provisional) provisionalAxes |= 1u << axis;
                    if (status != ProofClassification.Certain || provisional)
                    { if (status == ProofClassification.Impossible) impossible |= 1u << axis; continue; }
                    status = R3MetricResidual(prediction, observed, parity, axis, out q[axis]);
                    if (status != ProofClassification.Certain)
                    { if (status == ProofClassification.Impossible) impossible |= 1u << axis; continue; }
                    resolved |= 1u << axis;
                    if (q[axis].IsSingleton && q[axis].Lower == 0f) exactZero |= 1u << axis;
                }
                FloatInterval scalar = default; Interval3 vector = default;
                if (impossible == 0u && resolved == 15u) TetraForwardIntervals(q, out scalar, out vector);
                evidence = new R3MetricEvidence(scalar, vector, resolved, exactZero, provisionalAxes, chirality);
                if (impossible != 0u) return ProofClassification.Impossible;
                if (resolved != 15u ||
                    !math.all(math.isfinite(new float4(scalar.Lower, scalar.Upper, vector.X.Lower, vector.X.Upper))) ||
                    !math.all(math.isfinite(new float4(vector.Y.Lower, vector.Y.Upper, vector.Z.Lower, vector.Z.Upper))))
                    return ProofClassification.Ambiguous;
                return ProofClassification.Certain;
            }

            private static PhaseRootEvidence WithStatus(PhaseRootEvidence root, ProofClassification status) =>
                new(root.Symbol, root.Root, status);

            private struct GeometryNode
            {
                internal int Level, Line, Petal, Path, Strand, ParentContext, KnotSite, RootNode;
                internal int3 Offset;
                internal bool Plus;
                internal MerkabaFlowerDetailKind Kind;
            }

            private static bool ResolveSource(uint source, bool plus, out GeometryNode node)
            {
                node = new GeometryNode
                {
                    Petal = (int)(source & 63u), ParentContext = (int)((source >> 6) & 7u),
                    KnotSite = (int)((source >> 9) & 7u), Plus = plus
                };
                if ((uint)node.Petal >= 48u || (uint)node.ParentContext >= 5u ||
                    (uint)node.KnotSite >= 6u) return false;
                int index = (node.Petal * 5 + node.ParentContext) * 6 + node.KnotSite;
                uint key = ChildLoopCreations[index];
                if ((key & 0x80000000u) == 0u) return false;
                uint4 address = ChildLoopAddresses[index];
                node.Offset = math.asint(address.xyz); node.Level = (int)(address.w & 3u);
                node.Line = (int)((address.w >> 2) & 15u); node.Strand = (int)((address.w >> 6) & 127u);
                node.Petal = (int)(key & 63u); node.Path = (int)((key >> 6) & 15u);
                node.RootNode = (int)((key >> 10) & 31u);
                node.Kind = (key & (1u << 15)) != 0u ? MerkabaFlowerDetailKind.R3Phase :
                    MerkabaFlowerDetailKind.R2Phase;
                return true;
            }

            private bool PredictGeometryNode(int3 owner, uint flags, uint epoch,
                GeometryNode task, float normalError, float offsetError, out PhaseRootEvidence prediction,
                FlowerDecode decode = null)
            {
                if (CarrierRootProof(owner, flags, task.Level, task.Offset, task.Line, task.Plus,
                    normalError, offsetError, out prediction) != ProofClassification.Certain) return false;
                if (task.Level == 0) return true;
                Span<PhaseRootEvidence> parents = stackalloc PhaseRootEvidence[3];
                Span<PhaseRootEvidence> ancestors = stackalloc PhaseRootEvidence[2];
                Span<MerkabaFlowerDetailRecord> records = stackalloc MerkabaFlowerDetailRecord[2];
                Span<MerkabaFlowerDetailKey> keys = stackalloc MerkabaFlowerDetailKey[2];
                parents.Clear(); ancestors.Clear(); records.Clear(); keys.Clear();
                int count = 0;
                PhaseFamilyRule family = PhaseFamiliesValue[task.Strand];
                int sourcePetal = FirstPetal(family.RootIncidentPetals);
                if (HasPhaseFamily(owner, epoch, 0, 0, sourcePetal, task.Line, task.Plus))
                {
                    if (CarrierRootProof(owner, flags, 0, NodesValue[family.RootNode].Direction,
                        task.Line, task.Plus, normalError, offsetError, out PhaseRootEvidence source) !=
                        ProofClassification.Certain) return false;
                    var key = MerkabaFlowerDetailKey.Create(0, 0, sourcePetal, task.Line,
                        MerkabaFlowerDetailKind.R2Phase, task.Plus, (int)((source.Symbol.Tag >> 8) & 31u));
                    if (TryReadPhase(owner, epoch, key, out MerkabaFlowerDetailRecord record))
                    {
                        PhaseRootEvidence shared;
                        ProofClassification status = decode == null
                            ? ReadOriginalShared(owner, family.RootNode, task.Plus, normalError, offsetError, out shared)
                            : decode.ReadOriginal(family.RootNode, task.Plus, out shared, out _);
                        if (status != ProofClassification.Certain) return false;
                        // Shared source validates identity and closure, while
                        // rotations use the UNCHANGED endpoint-local integers.
                        ancestors[count] = shared; records[count] = record; keys[count] = key; count++;
                    }
                }
                if (task.Level == 2)
                {
                    StrandRule strand = StrandsValue[task.Strand];
                    if (HasPhaseFamily(owner, epoch, 1, family.FinePath0, strand.Petal0,
                        task.Line, task.Plus))
                    {
                        int3 sourceOffset = NodesValue[strand.Node0].Direction + NodesValue[strand.Node1].Direction;
                        if (CarrierRootProof(owner, flags, 1, sourceOffset, task.Line, task.Plus,
                            normalError, offsetError, out PhaseRootEvidence source) !=
                            ProofClassification.Certain) return false;
                        var key = MerkabaFlowerDetailKey.Create(1, family.FinePath0, strand.Petal0,
                            task.Line, MerkabaFlowerDetailKind.R2Phase, task.Plus,
                            (int)((source.Symbol.Tag >> 8) & 31u));
                        if (TryReadPhase(owner, epoch, key, out MerkabaFlowerDetailRecord record))
                        {
                            if (count != 0)
                            {
                                FloatInterval turn = DecodePhaseInterval(records[0].Lower, records[0].Upper);
                                if (family.PhaseOrientation < 0) turn = new FloatInterval(-turn.Upper, -turn.Lower);
                                if (RotatePhaseEvidence(source, turn, out PhaseRootEvidence transported) !=
                                    ProofClassification.Certain) return false;
                                source = transported;
                            }
                            if (SynthesizePhaseRecord(source, key, record, epoch,
                                out PhaseRootEvidence synthesized) != ProofClassification.Certain) return false;
                            ancestors[count] = synthesized; records[count] = record; keys[count] = key; count++;
                        }
                    }
                }
                M8FlowerUnpackPlane(flags, out float3 normal, out float delta);
                return PredictChildFromFamily(owner, task.Petal, task.ParentContext, task.KnotSite,
                    normal, delta, normalError, offsetError, (int)((prediction.Symbol.Tag >> 8) & 31u),
                    task.Plus, parents, ancestors[..count], records[..count], keys[..count], epoch,
                    out prediction) == ProofClassification.Certain;
            }

            private bool ReadL2Incidence(int3 owner, uint flags, uint epoch, uint source, bool plus,
                float normalError, float offsetError, out PhaseRootEvidence root, FlowerDecode decode)
            {
                root = Unresolved;
                if (!StableR1(flags) || !ResolveSource(source, plus, out GeometryNode node)) return false;
                if (node.Level == 0)
                    return (decode == null
                        ? ReadOriginalShared(owner, node.RootNode, plus, normalError, offsetError, out root)
                        : decode.ReadOriginal(node.RootNode, plus, out root, out _)) == ProofClassification.Certain;
                if (!PredictGeometryNode(owner, flags, epoch, node, normalError, offsetError, out root, decode))
                {
                    if (root.Classification == ProofClassification.Certain)
                        root = WithStatus(root, ProofClassification.Ambiguous);
                    return false;
                }
                if (LinesValue[node.Line].Shell == Shell.R1Core) return true;
                var key = MerkabaFlowerDetailKey.Create(node.Level, node.Path, node.Petal,
                    node.Line, node.Kind, plus, (int)((root.Symbol.Tag >> 8) & 31u));
                return ApplyOwnPhase(owner, epoch, key, node, root, out root) == ProofClassification.Certain;
            }

            internal bool ReadL2Knot(int3 owner, int knot, bool plus, float normalError,
                float offsetError, out PhaseRootEvidence root, FlowerDecode decode = null)
            {
                decode?.RequireSource(this, owner, new float2(normalError, offsetError));
                root = Unresolved;
                if ((uint)knot >= L2KnotCount ||
                    !TryReadOwner(owner, out KernelState state, out uint epoch)) return false;
                int first = CarrierData.IncidenceOffsets[knot], end = CarrierData.IncidenceOffsets[knot + 1];
                for (int incidence = first; incidence < end; incidence++)
                {
                    if (!ReadL2Incidence(owner, state.Flags, epoch, CarrierData.IncidenceSources[incidence],
                        plus, normalError, offsetError, out PhaseRootEvidence candidate, decode))
                    { root = candidate; return false; }
                    if (incidence == first) root = candidate;
                    else
                    {
                        ProofClassification status = CloseSharedPhaseRoot(root, candidate, out PhaseRootEvidence closed);
                        if (status != ProofClassification.Certain)
                        { root = WithStatus(closed, status); return false; }
                        root = closed;
                    }
                }
                return root.Classification == ProofClassification.Certain;
            }

            // Explicit alternatives supplied by the finite admission predicate;
            // reading seven roots does not itself admit a carrier or its flags.
            internal bool ReadL2CarrierRoots(int3 owner, int carrier, uint rootSigns,
                float normalError, float offsetError, Span<PhaseRootEvidence> roots)
            {
                if (roots.Length != L2CarrierSiteCount) throw new ArgumentException("Seven carrier roots are required.");
                roots.Clear();
                if ((uint)carrier >= L2HubCount || rootSigns >= 128u) return false;
                for (int site = 0; site < L2CarrierSiteCount; site++)
                    if (!ReadL2Knot(owner, L2CarrierKnotIndex(carrier, site), ((rootSigns >> site) & 1u) != 0u,
                        normalError, offsetError, out roots[site])) return false;
                return true;
            }

            private void SourceAnchorAlternatives(int3 owner, int petal, int anchor,
                float2 errors, out uint available, out uint uncertain, out uint nonprovisional,
                FlowerDecode parentSnapshot, uint completionToken)
            {
                available = uncertain = nonprovisional = 0u;
                int node = PetalsValue[petal].Node(anchor);
                bool completed = completionToken != uint.MaxValue && (completionToken & 63u) == petal;
                uint permitted = 3u;
                PhaseRootEvidence donor = default;
                if (completed)
                {
                    permitted = 1u << (int)((completionToken >> (6 + anchor)) & 1u);
                    if (parentSnapshot == null || !parentSnapshot.HasCompletionAnchors(completionToken) ||
                        !parentSnapshot.TryGetCandidateAnchor(petal, anchor, out donor))
                    { uncertain = permitted; return; }
                }
                for (int sign = 0; sign < 2; sign++)
                {
                    uint bit = 1u << sign;
                    if ((permitted & bit) == 0u) continue;
                    PhaseRootEvidence root;
                    bool provisional;
                    ProofClassification status = parentSnapshot == null
                        ? ReadOriginalShared(owner, node, sign != 0, errors.x, errors.y, out root, out provisional)
                        : parentSnapshot.ReadOriginal(node, sign != 0, out root, out provisional);
                    if (status == ProofClassification.Impossible) continue;
                    if (status != ProofClassification.Certain ||
                        ClassifyPhaseSector(root, out _) != ProofClassification.Certain ||
                        !TryGetAnchorRootReferences(node, root.Symbol.Tag, out ulong allowed))
                    { uncertain |= bit; continue; }
                    if ((allowed & (1UL << petal)) == 0u) continue;
                    if (completed && (provisional ||
                        CloseSharedPhaseRoot(root, donor, out _) != ProofClassification.Certain))
                    {
                        uncertain |= bit;
                        continue;
                    }
                    available |= bit;
                    if (!provisional && !completed) nonprovisional |= bit;
                }
            }

            internal ProofClassification ClassifyL2Carrier(int3 owner, int carrier, float2 errors,
                out MerkabaFlowerSymbolRecord symbol, out uint unresolvedWedges,
                Span<PhaseRootEvidence> roots, Span<float3> positions)
                => ClassifyL2Carrier(owner, carrier, errors, out symbol, out unresolvedWedges,
                    out _, roots, positions);

            internal ProofClassification ClassifyL2Carrier(int3 owner, int carrier, float2 errors,
                out MerkabaFlowerSymbolRecord symbol, out uint unresolvedWedges, out uint directWedges,
                Span<PhaseRootEvidence> roots, Span<float3> positions,
                FlowerDecode parentSnapshot = null, uint completionToken = uint.MaxValue)
            {
                if (roots.Length != 7 || positions.Length != 7)
                    throw new ArgumentException("A Flower carrier has seven shared knot sites.");
                symbol = default; symbol.ThreadRef = MerkabaFlowerSymbolRecord.InvalidRef;
                unresolvedWedges = 0u;
                directWedges = 0u;
                parentSnapshot?.RequireSource(this, owner, errors);
                roots.Clear(); positions.Clear();
                if ((uint)carrier >= L2HubCount) return ProofClassification.Impossible;
                if (completionToken != uint.MaxValue &&
                    (parentSnapshot == null || !parentSnapshot.HasCompletionAnchors(completionToken)))
                { unresolvedWedges = 63u; return ProofClassification.Ambiguous; }
                if (!TryReadOwner(owner, out KernelState state, out _))
                { unresolvedWedges = 63u; return ProofClassification.Ambiguous; }
                if (!StableR1(state.Flags)) return ProofClassification.Impossible;
                M8FlowerUnpackPlane(state.Flags, out float3 normal, out _);
                Span<Interval3> support = stackalloc Interval3[14];
                Span<uint> proofTags = stackalloc uint[14];
                support.Clear();
                proofTags.Clear();
                uint known = 0u, unknown = 0u;
                for (int site = 0; site < 7; site++)
                for (int sign = 0; sign < 2; sign++)
                {
                    int index = 2 * site + sign, knot = L2CarrierKnotIndex(carrier, site);
                    uint bit = 1u << index;
                    PhaseRootEvidence root;
                    bool read = parentSnapshot == null
                        ? ReadL2Knot(owner, knot, sign != 0, errors.x, errors.y, out root)
                        : parentSnapshot.ReadKnot(knot, sign != 0, out root);
                    if (read && RootRelativeBounds(knot, root, out support[index]))
                    {
                        known |= bit;
                        proofTags[index] = root.Symbol.Tag & ~BoundaryWitnessMask;
                        if ((root.Symbol.Tag & BoundaryWitnessMask) != 0u &&
                            ClassifyPhaseSector(root, out _) == ProofClassification.Certain)
                            proofTags[index] = root.Symbol.Tag;
                    }
                    else if (root.Classification != ProofClassification.Impossible) unknown |= bit;
                }
                Span<uint> certain = stackalloc uint[6], uncertain = stackalloc uint[6];
                Span<uint> directTriples = stackalloc uint[6];
                certain.Clear(); uncertain.Clear(); directTriples.Clear();
                for (int wedge = 0; wedge < 6; wedge++)
                {
                    L2WedgeRule source = CarrierData.Wedges[6 * carrier + wedge];
                    int petal = source.Petal;
                    ProofClassification parent = ProofClassification.Certain;
                    bool parentDirect = true;
                    uint3 anchorAvailable = default, anchorUncertain = default, anchorDirect = default;
                    for (int anchor = 0; anchor < 3; anchor++)
                    {
                        SourceAnchorAlternatives(owner, petal, anchor, errors,
                            out uint available, out uint pending, out uint direct, parentSnapshot, completionToken);
                        anchorAvailable[anchor] = available;
                        anchorUncertain[anchor] = pending;
                        anchorDirect[anchor] = direct;
                        parentDirect &= direct != 0u;
                        if ((available | pending) == 0u) { parent = ProofClassification.Impossible; break; }
                        if (available == 0u) parent = ProofClassification.Ambiguous;
                    }
                    if (parent == ProofClassification.Impossible) continue;
                    int3 sites = L2CarrierTriangleIndices(wedge);
                    for (int triple = 0; triple < 8; triple++)
                    {
                        int3 index = 2 * sites + new int3(triple & 1, (triple >> 1) & 1, (triple >> 2) & 1);
                        uint needed = (1u << index.x) | (1u << index.y) | (1u << index.z);
                        if ((needed & ~(known | unknown)) != 0u) continue;
                        bool resolved = parent == ProofClassification.Certain && (needed & ~known) == 0u;
                        bool impossible = false;
                        bool tripleDirect = parentDirect;
                        for (int vertex = 0; vertex < 3; vertex++)
                        {
                            int knot = L2CarrierKnotIndex(carrier, sites[vertex]);
                            if (!TryGetL2KnotLoop(knot, out int level, out int3 offset, out int line))
                            { impossible = true; break; }
                            if (level == 0)
                            {
                                int anchor = -1;
                                for (int original = 0; original < 3; original++)
                                {
                                    NodeRule node = NodesValue[PetalsValue[petal].Node(original)];
                                    if (node.LineClass == line && math.all(node.Direction == offset)) anchor = original;
                                }
                                if (anchor < 0) { impossible = true; break; }
                                uint sign = 1u << (index[vertex] & 1);
                                if (((anchorAvailable[anchor] | anchorUncertain[anchor]) & sign) == 0u)
                                { impossible = true; break; }
                                resolved &= (anchorAvailable[anchor] & sign) != 0u;
                                tripleDirect &= (anchorDirect[anchor] & sign) != 0u;
                            }
                            if ((known & (1u << index[vertex])) == 0u) continue;
                            ProofClassification status = SourceFlagContainment(petal, source.ChildPath,
                                knot, proofTags[index[vertex]], support[index[vertex]]);
                            if (status == ProofClassification.Impossible) { impossible = true; break; }
                            if (status != ProofClassification.Certain) resolved = false;
                        }
                        if (impossible) continue;
                        if ((needed & ~known) == 0u)
                        {
                            ProofClassification status = CarrierWedgeOrientation(support[index.x], support[index.y],
                                support[index.z], normal, errors.x);
                            if (status == ProofClassification.Impossible) continue;
                            if (status != ProofClassification.Certain) resolved = false;
                        }
                        if (resolved)
                        {
                            certain[wedge] |= 1u << triple;
                            if (tripleDirect) directTriples[wedge] |= 1u << triple;
                        }
                        else uncertain[wedge] |= 1u << triple;
                    }
                }
                ProofClassification result = CombineCarrierCandidates(certain, uncertain, L2CarrierBranchMasks[carrier],
                    out uint signs, out uint active, out unresolvedWedges);
                if (result != ProofClassification.Certain) return result;
                uint used = 0u;
                for (int wedge = 0; wedge < 6; wedge++)
                    if ((active & (1u << wedge)) != 0u)
                        used |= 1u | (1u << (1 + wedge)) | (1u << (1 + (wedge + 1) % 6));
                for (int site = 0; site < 7; site++)
                {
                    if ((used & (1u << site)) == 0u) continue;
                    int knot = L2CarrierKnotIndex(carrier, site);
                    bool plus = (signs & (1u << site)) != 0u;
                    bool read = parentSnapshot == null
                        ? ReadL2Knot(owner, knot, plus, errors.x, errors.y, out roots[site])
                        : parentSnapshot.ReadKnot(knot, plus, out roots[site]);
                    if (!read || !TryRootGridPosition(roots[site], out positions[site]))
                    { unresolvedWedges |= active; return ProofClassification.Ambiguous; }
                }
                ResolveOwner(owner, out _, out int local);
                bool reverse = M8FlowerPlaneFreeSide(state.Flags) < 0;
                uint completed = 0u;
                if (completionToken != uint.MaxValue)
                    for (int wedge = 0; wedge < 6; wedge++)
                        if (CarrierData.Wedges[6 * carrier + wedge].Petal == (completionToken & 63u))
                            completed |= active & (1u << wedge);
                // CPU packet references are absent; all lookups remain the
                // frozen owner/key/epoch above, never a fabricated GPU offset.
                symbol = MerkabaFlowerSymbolRecord.CreateCarrier(local, carrier, 1, reverse, false,
                    active, signs, (int)((roots[0].Symbol.Tag >> 8) & 31u), completed, reverse ? active : 0u);
                for (int wedge = 0; wedge < 6; wedge++)
                    if ((active & (1u << wedge)) != 0u &&
                        (directTriples[wedge] & (1u << (int)CarrierTriple(signs, wedge))) != 0u)
                        directWedges |= 1u << wedge;
                directWedges &= ~completed;
                return ProofClassification.Certain;
            }

            private static PhaseRootEvidence Unresolved =>
                new(default, default, ProofClassification.Ambiguous);
        }
    }
}
