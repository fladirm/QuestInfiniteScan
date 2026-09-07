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

            internal SnapshotReader(IEnumerable<MerkabaTileSnapshot> snapshots,
                MerkabaTileAddress[] storedIndex, Func<int3, ExcavationCellState> readCell = null)
            {
                if (snapshots == null) throw new ArgumentNullException(nameof(snapshots));
                if (storedIndex == null) throw new ArgumentNullException(nameof(storedIndex));
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

            internal MerkabaFlowerSkinDrawSample[] ReadSkinSignal(int3 owner,
                in MerkabaFlowerSymbolRecord symbol, out MerkabaFlowerSkinDrawHeader header)
            {
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
                Span<PhaseRootEvidence> roots, Span<float3> positions)
            {
                ProofClassification status = ClassifyL2Carrier(owner, carrier, errors,
                    out symbol, out unresolved, roots, positions);
                uint owned = 0u;
                for (int wedge = 0; wedge < 6; wedge++)
                    if (L2OwnsWedge(carrier, wedge)) owned |= 1u << wedge;
                unresolved &= owned;
                if (status != ProofClassification.Certain)
                    return status == ProofClassification.Ambiguous && unresolved != 0u
                        ? ProofClassification.Ambiguous : ProofClassification.Impossible;
                uint active = symbol.ActiveWedgeMask & owned;
                for (int wedge = 0; wedge < 6; wedge++)
                {
                    uint bit = 1u << wedge;
                    if ((active & bit) == 0u) continue;
                    ProofClassification dual = _readCell == null ? ProofClassification.Ambiguous :
                        SupportWedgeDual(owner, carrier, wedge, roots, _readCell);
                    if (dual != ProofClassification.Impossible) active &= ~bit;
                    if (dual == ProofClassification.Ambiguous) unresolved |= bit;
                }
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
                return active != 0u ? ProofClassification.Certain : unresolved != 0u ?
                    ProofClassification.Ambiguous : ProofClassification.Impossible;
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
                if (RotateTangentHalfAngle(prediction.Root, turn, key.Channel,
                        key.Sector, out Interval2 rotated) != ProofClassification.Certain)
                    return ProofClassification.Ambiguous;
                root = new PhaseRootEvidence(prediction.Symbol, rotated, ProofClassification.Certain);
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
                if (ClassifySector(p.LineClass, predicted.Root, out int pSector) != ProofClassification.Certain ||
                    ClassifySector(o.LineClass, observed.Root, out int oSector) != ProofClassification.Certain)
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
                if (!TryGetChildPhaseLoop(node.Petal, node.ParentContext, node.KnotSite,
                    out node.Level, out node.Offset, out node.Line, out node.Strand,
                    out _, out _, out int inherited)) return false;
                if (node.Level == 0)
                {
                    node.RootNode = PetalsValue[node.Petal].Node(node.KnotSite);
                    node.Petal = FirstPetal(NodeIncidentPetalsValue[node.RootNode]);
                }
                else
                {
                    if (inherited >= 0) return false;
                    PhaseFamilyRule family = PhaseFamiliesValue[node.Strand];
                    node.RootNode = family.RootNode;
                    if (node.Level == 1)
                    {
                        node.Petal = StrandsValue[node.Strand].Petal0;
                        node.Path = family.FinePath0;
                    }
                    else node.Path = 4 * (node.ParentContext - 1) + (node.KnotSite == 4 ? 1 : 0);
                }
                node.Kind = LinesValue[node.Line].Shell == Shell.R3Closure
                    ? MerkabaFlowerDetailKind.R3Phase : MerkabaFlowerDetailKind.R2Phase;
                return true;
            }

            private bool PredictGeometryNode(int3 owner, uint flags, uint epoch,
                GeometryNode task, float normalError, float offsetError, out PhaseRootEvidence prediction)
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
                if (CarrierRootProof(owner, flags, 0, NodesValue[family.RootNode].Direction,
                    task.Line, task.Plus, normalError, offsetError, out PhaseRootEvidence source) ==
                    ProofClassification.Certain)
                {
                    var key = MerkabaFlowerDetailKey.Create(0, 0, sourcePetal, task.Line,
                        MerkabaFlowerDetailKind.R2Phase, task.Plus, (int)((source.Symbol.Tag >> 8) & 31u));
                    if (TryReadPhase(owner, epoch, key, out MerkabaFlowerDetailRecord record))
                    {
                        if (ReadOriginalShared(owner, family.RootNode, task.Plus, normalError,
                            offsetError, out PhaseRootEvidence shared) != ProofClassification.Certain) return false;
                        // Shared source validates identity and closure, while
                        // rotations use the UNCHANGED endpoint-local integers.
                        ancestors[count] = shared; records[count] = record; keys[count] = key; count++;
                    }
                }
                else if (HasPhaseFamily(owner, epoch, 0, 0, sourcePetal, task.Line, task.Plus)) return false;
                if (task.Level == 2)
                {
                    StrandRule strand = StrandsValue[task.Strand];
                    int3 sourceOffset = NodesValue[strand.Node0].Direction + NodesValue[strand.Node1].Direction;
                    if (CarrierRootProof(owner, flags, 1, sourceOffset, task.Line, task.Plus,
                        normalError, offsetError, out source) == ProofClassification.Certain)
                    {
                        var key = MerkabaFlowerDetailKey.Create(1, family.FinePath0, strand.Petal0,
                            task.Line, MerkabaFlowerDetailKind.R2Phase, task.Plus,
                            (int)((source.Symbol.Tag >> 8) & 31u));
                        if (TryReadPhase(owner, epoch, key, out MerkabaFlowerDetailRecord record))
                        {
                            if (count != 0)
                            {
                                FloatInterval turn = DecodePhaseInterval(records[0].Lower, records[0].Upper);
                                if (family.PhaseOrientation < 0) turn = new FloatInterval(-turn.Upper, -turn.Lower);
                                if (RotateTangentHalfAngle(source.Root, turn, task.Line,
                                    (int)((source.Symbol.Tag >> 8) & 31u), out Interval2 transported) !=
                                    ProofClassification.Certain) return false;
                                source = new PhaseRootEvidence(source.Symbol, transported, source.Classification);
                            }
                            if (SynthesizePhaseRecord(source, key, record, epoch,
                                out PhaseRootEvidence synthesized) != ProofClassification.Certain) return false;
                            ancestors[count] = synthesized; records[count] = record; keys[count] = key; count++;
                        }
                    }
                    else if (HasPhaseFamily(owner, epoch, 1, family.FinePath0,
                        strand.Petal0, task.Line, task.Plus)) return false;
                }
                M8FlowerUnpackPlane(flags, out float3 normal, out float delta);
                return PredictChildFromFamily(owner, task.Petal, task.ParentContext, task.KnotSite,
                    normal, delta, normalError, offsetError, (int)((prediction.Symbol.Tag >> 8) & 31u),
                    task.Plus, parents, ancestors[..count], records[..count], keys[..count], epoch,
                    out prediction) == ProofClassification.Certain;
            }

            private bool ReadL2Incidence(int3 owner, uint flags, uint epoch, uint source, bool plus,
                float normalError, float offsetError, out PhaseRootEvidence root)
            {
                root = Unresolved;
                if (!StableR1(flags) || !ResolveSource(source, plus, out GeometryNode node)) return false;
                if (node.Level == 0)
                    return ReadOriginalShared(owner, node.RootNode, plus, normalError, offsetError, out root) ==
                        ProofClassification.Certain;
                if (!PredictGeometryNode(owner, flags, epoch, node, normalError, offsetError, out root))
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
                float offsetError, out PhaseRootEvidence root)
            {
                root = Unresolved;
                if ((uint)knot >= L2KnotCount ||
                    !TryReadOwner(owner, out KernelState state, out uint epoch)) return false;
                int first = CarrierData.IncidenceOffsets[knot], end = CarrierData.IncidenceOffsets[knot + 1];
                for (int incidence = first; incidence < end; incidence++)
                {
                    if (!ReadL2Incidence(owner, state.Flags, epoch, CarrierData.IncidenceSources[incidence],
                        plus, normalError, offsetError, out PhaseRootEvidence candidate))
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

            private ProofClassification SourceAnchorAdmission(int3 owner, int petal, int anchor,
                float2 errors)
            {
                int node = PetalsValue[petal].Node(anchor);
                uint admitted = 0u, unresolved = 0u;
                for (int sign = 0; sign < 2; sign++)
                {
                    ProofClassification status = ReadOriginalShared(owner, node, sign != 0,
                        errors.x, errors.y, out PhaseRootEvidence root);
                    if (status == ProofClassification.Impossible) continue;
                    if (status != ProofClassification.Certain ||
                        !TryGetAnchorSectorFlags(node, (int)((root.Symbol.Tag >> 8) & 31u), out ulong allowed))
                    { unresolved |= 1u << sign; continue; }
                    if ((allowed & (1UL << petal)) != 0u) admitted |= 1u << sign;
                }
                if (unresolved != 0u || math.countbits(admitted) > 1) return ProofClassification.Ambiguous;
                return admitted != 0u ? ProofClassification.Certain : ProofClassification.Impossible;
            }

            internal ProofClassification ClassifyL2Carrier(int3 owner, int carrier, float2 errors,
                out MerkabaFlowerSymbolRecord symbol, out uint unresolvedWedges,
                Span<PhaseRootEvidence> roots, Span<float3> positions)
            {
                if (roots.Length != 7 || positions.Length != 7)
                    throw new ArgumentException("A Flower carrier has seven shared knot sites.");
                symbol = default; symbol.ThreadRef = MerkabaFlowerSymbolRecord.InvalidRef;
                unresolvedWedges = 0u;
                roots.Clear(); positions.Clear();
                if ((uint)carrier >= L2HubCount) return ProofClassification.Impossible;
                if (!TryReadOwner(owner, out KernelState state, out _))
                { unresolvedWedges = 63u; return ProofClassification.Ambiguous; }
                if (!StableR1(state.Flags)) return ProofClassification.Impossible;
                M8FlowerUnpackPlane(state.Flags, out float3 normal, out _);
                Span<Interval3> support = stackalloc Interval3[14];
                support.Clear();
                uint known = 0u, unknown = 0u;
                for (int site = 0; site < 7; site++)
                for (int sign = 0; sign < 2; sign++)
                {
                    int index = 2 * site + sign, knot = L2CarrierKnotIndex(carrier, site);
                    uint bit = 1u << index;
                    if (ReadL2Knot(owner, knot, sign != 0, errors.x, errors.y, out PhaseRootEvidence root) &&
                        RootRelativeBounds(knot, root, out support[index])) known |= bit;
                    else if (root.Classification != ProofClassification.Impossible) unknown |= bit;
                }
                Span<uint> certain = stackalloc uint[6], uncertain = stackalloc uint[6];
                certain.Clear(); uncertain.Clear();
                for (int wedge = 0; wedge < 6; wedge++)
                {
                    int petal = CarrierData.Wedges[6 * carrier + wedge].Petal;
                    ProofClassification parent = ProofClassification.Certain;
                    for (int anchor = 0; anchor < 3; anchor++)
                    {
                        ProofClassification status = SourceAnchorAdmission(owner, petal, anchor, errors);
                        if (status == ProofClassification.Impossible) { parent = status; break; }
                        if (status != ProofClassification.Certain) parent = ProofClassification.Ambiguous;
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
                        for (int vertex = 0; vertex < 3; vertex++)
                        {
                            if ((known & (1u << index[vertex])) == 0u) continue;
                            ProofClassification status = SourceFlagContainment(petal, support[index[vertex]]);
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
                        if (resolved) certain[wedge] |= 1u << triple;
                        else uncertain[wedge] |= 1u << triple;
                    }
                }
                ProofClassification result = CombineCarrierCandidates(certain, uncertain,
                    out uint signs, out uint active, out unresolvedWedges);
                if (result != ProofClassification.Certain) return result;
                uint used = 0u;
                for (int wedge = 0; wedge < 6; wedge++)
                    if ((active & (1u << wedge)) != 0u)
                        used |= 1u | (1u << (1 + wedge)) | (1u << (1 + (wedge + 1) % 6));
                for (int site = 0; site < 7; site++)
                {
                    if ((used & (1u << site)) == 0u) continue;
                    if (!ReadL2Knot(owner, L2CarrierKnotIndex(carrier, site), (signs & (1u << site)) != 0u,
                        errors.x, errors.y, out roots[site]) || !TryRootGridPosition(roots[site], out positions[site]))
                    { unresolvedWedges |= active; return ProofClassification.Ambiguous; }
                }
                ResolveOwner(owner, out _, out int local);
                bool reverse = M8FlowerPlaneFreeSide(state.Flags) < 0;
                // CPU packet references are absent; all lookups remain the
                // frozen owner/key/epoch above, never a fabricated GPU offset.
                symbol = MerkabaFlowerSymbolRecord.CreateCarrier(local, carrier, 1, reverse, false,
                    active, signs, (int)((roots[0].Symbol.Tag >> 8) & 31u), 0u, reverse ? active : 0u);
                return ProofClassification.Certain;
            }

            private static PhaseRootEvidence Unresolved =>
                new(default, default, ProofClassification.Ambiguous);
        }
    }
}
