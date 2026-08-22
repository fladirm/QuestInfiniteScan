using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// S4-04 dual-depth inverse readout. Pixel classification, finite-cone cell
    /// construction, exact meet and projective commit witnesses remain on GPU.
    /// The CPU sees only bounded asynchronous page/block scheduling flags required
    /// by the immutable carrier allocator; it never reads pixels or geometry.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SigmaCarrier))]
    [RequireComponent(typeof(SigmaTopologyController))]
    [RequireComponent(typeof(SigmaRenderer))]
    [RequireComponent(typeof(SigmaRigBridge))]
    [DefaultExecutionOrder(0)]
    public sealed class SigmaInverseController : MonoBehaviour, IRoomScanModule
    {
        private const string NormalizeResource = "SigmaPrism/DepthNormalize";
        private const string InverseResource = "SigmaPrism/SigmaInverse";
        private const string ConeLutResource = "SigmaPrism/ConeLut";
        private const int SegmentFlagStride = SigmaCarrier.MaximumPagesPerSegment;
        private const int CalibrationStride = 36;
        private const int ConflictStride = 72;
        private const int MaximumGaugeCommitSlots = 8;
        private static readonly Vector2Int[] GaugeNeighbourOffsets =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1)
        };

        [Header("Bounded asynchronous scheduling")]
        [SerializeField, Range(4096, 131072)] private int conflictCapacity = 32768;
        [SerializeField, Range(1, 8)] private int maxGaugePagesPerCommit = 6;

        private readonly List<SigmaCarrierReadBatch> _readBatches = new();
        private readonly List<SigmaCarrierPageHandle> _currentPages = new();
        private readonly List<SegmentInverseScratch> _segmentScratch = new();
        private readonly Dictionary<int, SigmaCarrierPageHandle>
            _pageSnapshot = new();
        private readonly Dictionary<GaugeImagePage, uint> _gaugePages = new();
        private readonly List<GaugeImagePage> _gaugePageOrder = new();
        private readonly SigmaPackedQ48[] _calibrationUpload =
            new SigmaPackedQ48[CalibrationStride * 2];

        private RoomScanner _scanner;
        private SigmaCarrier _carrier;
        private SigmaTopologyController _topology;
        private SigmaRenderer _renderer;
        private SigmaRigBridge _rigBridge;
        private SigmaExactBackendGate _backendGate;
        private ComputeShader _normalize;
        private ComputeShader _inverse;
        private ComputeShader _coneLutShader;
        private RigCalibration _calibration;
        private RigConeLutSet _coneLuts;
        private SigmaPredictionFrameLease _pendingPrediction;
        private InFlightFrame _inFlight;
        private RenderTexture _metricDepth;
        private RenderTexture _depthFlags;
        private GraphicsBuffer _calibrationQ48;
        private GraphicsBuffer _activePageFlags;
        private GraphicsBuffer _commitPageFlags;
        private GraphicsBuffer _unmatchedBlockFlags;
        private GraphicsBuffer _unmatchedBlockAnchors;
        private GraphicsBuffer _conflictRecords;
        private GraphicsBuffer _conflictCount;
        private GraphicsBuffer _frameCounters;
        private GraphicsBuffer _gaugePromotionCounts;
        private int _activeFlagCapacity;
        private int _blockFlagCapacity;
        private Vector2Int _scratchResolution;
        private uint _nextRevision = 1u;
        private ulong _nextGaugeOriginOrdinal;
        private bool _running;
        private bool _initialized;
        private bool _disposed;

        private int _normalizeKernel;
        private int _clearKernel;
        private int _classifyKernel;
        private int _compactKernel;
        private int _proposalKernel;
        private int _commitKernel;
        private int _promoteKernel;

        public string ModuleName => "Sigma joint inverse depth";
        public bool IsInitialized => _initialized && !_disposed;
        public long SubmittedFrames { get; private set; }
        public long CommittedFrames { get; private set; }
        public long DroppedFrames { get; private set; }
        public long FailedFrames { get; private set; }
        public long CommittedPageGenerations { get; private set; }
        public long AllocatedGaugePages { get; private set; }
        public SigmaInverseDiagnosticSnapshot LastDiagnostics { get; private set; }

        public void OnModuleInitialize(RoomScanner scanner)
        {
            if (_initialized)
                return;
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            _carrier = scanner.Carrier ?? GetComponent<SigmaCarrier>();
            _topology = scanner.SigmaTopology ??
                GetComponent<SigmaTopologyController>();
            _renderer = scanner.SigmaRenderer ?? GetComponent<SigmaRenderer>();
            _rigBridge = scanner.RigBridge ?? GetComponent<SigmaRigBridge>();
            _backendGate = scanner.ExactBackendGate ?? throw new InvalidOperationException(
                "Sigma inverse requires the GPU-resident exact backend gate.");
            _normalize = Resources.Load<ComputeShader>(NormalizeResource);
            _inverse = Resources.Load<ComputeShader>(InverseResource);
            _coneLutShader = Resources.Load<ComputeShader>(ConeLutResource);
            if (_carrier == null || _topology == null || _renderer == null ||
                _rigBridge == null ||
                _normalize == null || _inverse == null || _coneLutShader == null)
                throw new InvalidOperationException(
                    "Sigma joint inverse resources are incomplete.");
            if (!SystemInfo.supportsAsyncGPUReadback)
                throw new InvalidOperationException(
                    "Sigma immutable-page scheduling requires non-blocking GPU readback support.");

            _normalizeKernel = _normalize.FindKernel("NormalizeStereoDepth");
            _clearKernel = _inverse.FindKernel("ClearInverseFrame");
            _classifyKernel = _inverse.FindKernel("ClassifyDepthFrame");
            _compactKernel = _inverse.FindKernel("CompactActivePages");
            _proposalKernel = _inverse.FindKernel("BuildDepthProposals");
            _commitKernel = _inverse.FindKernel("CommitDepthProposals");
            _promoteKernel = _inverse.FindKernel("PromoteGaugePage");
            _calibrationQ48 = CreateBuffer(CalibrationStride * 2,
                Marshal.SizeOf<SigmaPackedQ48>(), "Sigma depth calibration Q48");
            _conflictRecords = CreateBuffer(conflictCapacity, ConflictStride,
                "Sigma inverse conflict records");
            _conflictCount = CreateBuffer(1, sizeof(uint),
                "Sigma inverse conflict count");
            _frameCounters = CreateBuffer(8, sizeof(uint),
                "Sigma inverse frame counters");
            _gaugePromotionCounts = CreateBuffer(MaximumGaugeCommitSlots,
                sizeof(uint), "Sigma inverse gauge promotion counts");
            _conflictCount.SetData(new uint[1]);
            _frameCounters.SetData(new uint[8]);
            _gaugePromotionCounts.SetData(new uint[MaximumGaugeCommitSlots]);
            _renderer.PredictionReady += OnPredictionReady;
            _initialized = true;
        }

        public void OnScanStarted()
        {
            _running = true;
            _nextRevision = DetermineNextRevision();
        }

        public void OnScanStopped()
        {
            _running = false;
            _pendingPrediction?.Dispose();
            _pendingPrediction = null;
            if (_inFlight != null)
                _inFlight.Discard = true;
        }

        private void OnPredictionReady(SigmaPredictionFrameLease prediction)
        {
            if (!_running || !_initialized || prediction == null ||
                prediction.IsDisposed)
                return;
            if (_pendingPrediction != null || _inFlight != null)
            {
                DroppedFrames++;
                return;
            }
            _pendingPrediction = prediction.Retain();
        }

        private void LateUpdate()
        {
            if (!_initialized || _disposed)
                return;
            if (_inFlight != null && _inFlight.AllRequestsDone)
                CompleteInFlight();
            if (!_running || _inFlight != null || _pendingPrediction == null)
                return;

            SigmaPredictionFrameLease prediction = _pendingPrediction;
            _pendingPrediction = null;
            try
            {
                BeginInverse(prediction);
            }
            catch (Exception exception)
            {
                prediction.Dispose();
                FailedFrames++;
                Logger.Error("Sigma inverse submission failed: " + exception.Message);
            }
        }

        private void BeginInverse(SigmaPredictionFrameLease prediction)
        {
            StereoRigFrameLease source = prediction.Source;
            if (!source.IsValid)
                throw new InvalidOperationException("Inverse source lease is invalid.");
            EnsureCalibration(source);
            EnsureFrameResources(source.DepthResolution);
            UploadExactCalibration(source);

            _carrier.CollectReadableSegments(_readBatches);
            EnsureSegmentScratch();
            _carrier.CollectCurrentPages(_currentPages);
            _pageSnapshot.Clear();
            for (int index = 0; index < _currentPages.Count; ++index)
            {
                SigmaCarrierPageHandle page = _currentPages[index];
                _pageSnapshot.Add(GlobalPageIndex(page.SegmentIndex, page.PageSlot),
                    page);
            }

            int segmentCount = _readBatches.Count;
            int activeFlagCount = Math.Max(1, segmentCount * SegmentFlagStride);
            Vector2Int blockResolution = new(
                CeilDiv(source.DepthResolution.x, 32),
                CeilDiv(source.DepthResolution.y, 32));
            int blockCount = checked(blockResolution.x * blockResolution.y);
            EnsureFlagBuffers(activeFlagCount, blockCount);

            uint revision = _nextRevision++;
            if (_nextRevision == 0u)
                throw new OverflowException("Sigma world revision exhausted.");
            uint leftKey = IndependenceKey(source.DepthLeft, source.CalibrationEpoch);
            uint rightKey = IndependenceKey(source.DepthRight, source.CalibrationEpoch);

            CommandBuffer command = CommandBufferPool.Get(
                "Sigma-PRISM-16 Joint Inverse Depth");
            try
            {
                RecordNormalize(command, source);
                RecordClear(command, activeFlagCount, blockCount);
                BindFrameInputs(command, source, prediction, blockResolution,
                    segmentCount, leftKey, rightKey);
                command.DispatchCompute(_inverse, _classifyKernel,
                    CeilDiv(source.DepthResolution.x, 8),
                    CeilDiv(source.DepthResolution.y, 8), 2);

                for (int segment = 0; segment < segmentCount; ++segment)
                {
                    SigmaCarrierReadBatch batch = _readBatches[segment];
                    SegmentInverseScratch scratch = _segmentScratch[segment];
                    RecordCompactActive(command, batch, scratch, segment);
                    RecordBuildProposals(command, batch, scratch, segment,
                        prediction, revision);
                }
                Graphics.ExecuteCommandBuffer(command);
            }
            finally
            {
                CommandBufferPool.Release(command);
            }

            var pageSnapshot = new Dictionary<int, SigmaCarrierPageHandle>(
                _pageSnapshot);
            Dictionary<SigmaCarrierPageCoordinate, SigmaTopologyEvidenceView>
                topologyEvidence = BuildTopologyEvidenceMap(pageSnapshot,
                    revision, leftKey, rightKey);
            var readbackLatch = new ReadbackRetirementLatch();
            var inFlight = new InFlightFrame(prediction, revision,
                source.DepthResolution, blockResolution, segmentCount,
                leftKey, rightKey, pageSnapshot,
                topologyEvidence,
                readbackLatch,
                readbackLatch.Request(_activePageFlags),
                readbackLatch.Request(_commitPageFlags),
                readbackLatch.Request(_unmatchedBlockFlags),
                readbackLatch.Request(_unmatchedBlockAnchors),
                readbackLatch.Request(_frameCounters),
                readbackLatch.Request(_conflictCount));
            _inFlight = inFlight;
            SubmittedFrames++;
        }

        private Dictionary<SigmaCarrierPageCoordinate,
            SigmaTopologyEvidenceView> BuildTopologyEvidenceMap(
            Dictionary<int, SigmaCarrierPageHandle> pageSnapshot,
            uint frameSerial, uint leftKey, uint rightKey)
        {
            var evidence = new Dictionary<SigmaCarrierPageCoordinate,
                SigmaTopologyEvidenceView>(pageSnapshot.Count);
            foreach (SigmaCarrierPageHandle page in pageSnapshot.Values)
            {
                SegmentInverseScratch scratch = _segmentScratch[page.SegmentIndex];
                evidence[page.Coordinate] = SigmaTopologyEvidenceView.Proposals(
                    page.Coordinate, scratch.ProposalStatus,
                    scratch.ProposalEpoch, page.PageSlot, scratch.Capacity,
                    frameSerial, leftKey, rightKey);
            }
            return evidence;
        }

        private void CompleteInFlight()
        {
            InFlightFrame frame = _inFlight;
            try
            {
                if (frame.HasGaugeReadback)
                {
                    CompleteGaugePromotions(frame);
                    return;
                }
                if (frame.HasError || frame.Discard)
                {
                    FailedFrames += frame.HasError ? 1L : 0L;
                    FinishFrame(frame, 0u);
                    return;
                }
                NativeArray<uint> conflictCountData = frame.ConflictCount.GetData<uint>();
                uint producedConflicts = conflictCountData.Length > 0
                    ? conflictCountData[0] : uint.MaxValue;
                frame.ProducedConflicts = producedConflicts;
                if (producedConflicts > (uint)conflictCapacity)
                {
                    FailedFrames++;
                    Logger.Warning("Sigma inverse evidence capacity exceeded; frame failed closed.");
                    FinishFrame(frame, 0u);
                    return;
                }
                NativeArray<uint> commits = frame.CommitFlags.GetData<uint>();
                var publishedMatched = new List<SigmaCarrierPageHandle>();
                CommitMatchedPages(frame, commits, publishedMatched);
                NativeArray<uint> activePages =
                    frame.ActivePageFlags.GetData<uint>();
                UpdateObservedTopologyPages(frame, activePages,
                    publishedMatched);
                NativeArray<uint> unmatched = frame.UnmatchedBlocks.GetData<uint>();
                NativeArray<uint> anchors = frame.UnmatchedAnchors.GetData<uint>();
                int scheduledGaugePages = BeginGaugePageCommits(frame, unmatched,
                    anchors);
                if (scheduledGaugePages != 0)
                {
                    frame.BeginGaugeReadback(_gaugePromotionCounts);
                    return;
                }
                FinishFrame(frame, 0u);
            }
            catch (Exception exception)
            {
                frame.AbortGaugeWrites();
                FailedFrames++;
                Logger.Error("Sigma inverse completion failed: " + exception.Message);
                FinishFrame(frame, 0u);
            }
        }

        private void CompleteGaugePromotions(InFlightFrame frame)
        {
            if (frame.HasError || frame.Discard)
            {
                frame.AbortGaugeWrites();
                FailedFrames += frame.HasError ? 1L : 0L;
                FinishFrame(frame, 0u);
                return;
            }

            NativeArray<uint> counts = frame.GaugePromotions.GetData<uint>();
            uint promotedSamples = 0u;
            int publishedPages = 0;
            var publishedHandles = new List<SigmaCarrierPageHandle>(
                frame.GaugeWrites.Count);
            var pendingGauge = new List<PendingGaugePublication>(
                frame.GaugeWrites.Count);
            for (int index = 0; index < frame.GaugeWrites.Count; ++index)
            {
                SigmaCarrierWriteLease write = frame.GaugeWrites[index];
                uint count = (uint)index < (uint)counts.Length ? counts[index] : 0u;
                if (count == 0u)
                {
                    write.Dispose();
                    continue;
                }
                pendingGauge.Add(new PendingGaugePublication(write, count));
            }
            pendingGauge.Sort(static (left, right) =>
                right.Write.Handle.Coordinate.CompareTo(
                    left.Write.Handle.Coordinate));
            for (int index = 0; index < pendingGauge.Count; ++index)
            {
                SigmaCarrierWriteLease write = pendingGauge[index].Write;
                uint count = pendingGauge[index].PromotedSamples;
                frame.TopologyEvidence[write.Handle.Coordinate] =
                    SigmaTopologyEvidenceView.FullStereo(
                        write.Handle.Coordinate, frame.Revision,
                        frame.LeftIndependenceKey, frame.RightIndependenceKey);
                ResolveTopologyEvidence(frame, write.Handle.Coordinate,
                    out SigmaTopologyEvidenceView targetEvidence,
                    out SigmaTopologyEvidenceView rightEvidence,
                    out SigmaTopologyEvidenceView downEvidence);
                SigmaTopologyBuildToken topology = _topology.BuildGeneration(
                    write.Handle, default, targetEvidence, rightEvidence,
                    downEvidence);
                SigmaCarrierPageHandle published = write.Publish();
                topology.Publish();
                write.Dispose();
                publishedHandles.Add(published);
                promotedSamples = checked(promotedSamples + count);
                publishedPages++;
            }
            frame.GaugeWrites.Clear();
            RebuildAffectedTopology(frame, publishedHandles);
            frame.PublishedGaugePages = publishedPages;
            FinishFrame(frame, promotedSamples);
        }

        private void FinishFrame(InFlightFrame frame, uint promotedSamples)
        {
            if (!ReferenceEquals(_inFlight, frame))
                return;
            _inFlight = null;
            if (!frame.Discard && !frame.InitialReadbackHasError)
            {
                NativeArray<uint> counters = frame.Counters.GetData<uint>();
                LastDiagnostics = SigmaInverseDiagnosticSnapshot.From(counters,
                    frame.ProducedConflicts, promotedSamples);
            }
            int committedPages = frame.MatchedPageCommits +
                frame.PublishedGaugePages;
            if (committedPages != 0)
                CommittedFrames++;
            CommittedPageGenerations += committedPages;
            AllocatedGaugePages += frame.PublishedGaugePages;
            frame.Dispose();
        }

        private int CommitMatchedPages(InFlightFrame frame,
            NativeArray<uint> commitFlags,
            List<SigmaCarrierPageHandle> publishedHandles)
        {
            int committed = 0;
            var changedSources = new List<SigmaCarrierPageHandle>();
            foreach (KeyValuePair<int, SigmaCarrierPageHandle> pair in
                frame.PageSnapshot)
            {
                int global = pair.Key;
                if ((uint)global >= (uint)commitFlags.Length ||
                    commitFlags[global] == 0u)
                    continue;
                SigmaCarrierPageHandle source = pair.Value;
                if (!_carrier.TryGetLatest(source.Coordinate,
                        out SigmaCarrierPageHandle latest) || !latest.Equals(source))
                    continue;
                changedSources.Add(source);
            }
            // Right/down neighbours sort before their left/up owners. Each page can
            // therefore publish its topology once against final neighbours while
            // the retired source slot is still safe to copy and can be reclaimed
            // immediately afterwards.
            changedSources.Sort(static (left, right) =>
                right.Coordinate.CompareTo(left.Coordinate));
            for (int index = 0; index < changedSources.Count; ++index)
            {
                SigmaCarrierPageHandle source = changedSources[index];
                SegmentInverseScratch scratch = _segmentScratch[source.SegmentIndex];
                using SigmaCarrierWriteLease target = _carrier.BeginNextGeneration(
                    source.Coordinate, frame.Revision, source.CertificateOffset,
                    source.CertificateCount);
                target.BindWritable(_inverse, _commitKernel, "_TargetCarrierState",
                    "_TargetPageSlot", "_TargetPageCapacity");
                _inverse.SetInt("_SourcePageSlot", source.PageSlot);
                _inverse.SetBuffer(_commitKernel, "_ProposalGeometryRead",
                    scratch.ProposalGeometry);
                _inverse.SetBuffer(_commitKernel, "_ProposalMassRead",
                    scratch.ProposalMass);
                _inverse.SetBuffer(_commitKernel, "_ProposalStatusRead",
                    scratch.ProposalStatus);
                _inverse.Dispatch(_commitKernel,
                    SigmaCarrier.SamplesPerPage / 64, 1, 1);
                ResolveTopologyEvidence(frame, source.Coordinate,
                    out SigmaTopologyEvidenceView targetEvidence,
                    out SigmaTopologyEvidenceView rightEvidence,
                    out SigmaTopologyEvidenceView downEvidence);
                SigmaTopologyBuildToken topology = _topology.BuildGeneration(
                    target.Handle, source, targetEvidence, rightEvidence,
                    downEvidence);
                SigmaCarrierPageHandle published = target.Publish();
                topology.Publish();
                publishedHandles.Add(published);
                _carrier.TryReleaseRetiredGeneration(source);
                committed++;
                frame.MatchedPageCommits++;
            }
            return committed;
        }

        private void UpdateObservedTopologyPages(InFlightFrame frame,
            NativeArray<uint> activeFlags,
            IReadOnlyList<SigmaCarrierPageHandle> changedPages)
        {
            var rebuild = new Dictionary<SigmaCarrierPageCoordinate, bool>();
            var changed = new HashSet<SigmaCarrierPageCoordinate>();
            for (int index = 0; index < changedPages.Count; ++index)
                changed.Add(changedPages[index].Coordinate);
            foreach (KeyValuePair<int, SigmaCarrierPageHandle> pair in
                frame.PageSnapshot)
            {
                if ((uint)pair.Key < (uint)activeFlags.Length &&
                    activeFlags[pair.Key] != 0u &&
                    !changed.Contains(pair.Value.Coordinate))
                    AddTopologyRebuild(rebuild, pair.Value.Coordinate, false);
            }
            for (int index = 0; index < changedPages.Count; ++index)
            {
                SigmaCarrierPageCoordinate coordinate =
                    changedPages[index].Coordinate;
                if (TryOffset(coordinate, -1L, 0L,
                        out SigmaCarrierPageCoordinate left) &&
                    !changed.Contains(left))
                    AddTopologyRebuild(rebuild, left, true);
                if (TryOffset(coordinate, 0L, -1L,
                        out SigmaCarrierPageCoordinate up) &&
                    !changed.Contains(up))
                    AddTopologyRebuild(rebuild, up, true);
            }
            RebuildTopologyCoordinates(frame, rebuild);
        }

        private void RebuildAffectedTopology(InFlightFrame frame,
            IReadOnlyList<SigmaCarrierPageHandle> changedPages)
        {
            if (changedPages == null || changedPages.Count == 0)
                return;
            var changed = new HashSet<SigmaCarrierPageCoordinate>();
            for (int index = 0; index < changedPages.Count; ++index)
                changed.Add(changedPages[index].Coordinate);
            var rebuild = new Dictionary<SigmaCarrierPageCoordinate, bool>();
            for (int index = 0; index < changedPages.Count; ++index)
            {
                SigmaCarrierPageCoordinate coordinate =
                    changedPages[index].Coordinate;
                if (TryOffset(coordinate, -1L, 0L,
                        out SigmaCarrierPageCoordinate left) &&
                    !changed.Contains(left))
                    AddTopologyRebuild(rebuild, left, true);
                if (TryOffset(coordinate, 0L, -1L,
                        out SigmaCarrierPageCoordinate up) &&
                    !changed.Contains(up))
                    AddTopologyRebuild(rebuild, up, true);
            }
            RebuildTopologyCoordinates(frame, rebuild);
        }

        private void RebuildTopologyCoordinates(InFlightFrame frame,
            Dictionary<SigmaCarrierPageCoordinate, bool> coordinates)
        {
            var ordered = new List<SigmaCarrierPageCoordinate>(coordinates.Keys);
            ordered.Sort(static (left, right) =>
                right.CompareTo(left));
            for (int index = 0; index < ordered.Count; ++index)
            {
                SigmaCarrierPageCoordinate coordinate = ordered[index];
                if (!_carrier.TryGetLatest(coordinate,
                        out SigmaCarrierPageHandle page))
                    continue;
                ResolveTopologyEvidence(frame, coordinate,
                    out SigmaTopologyEvidenceView targetEvidence,
                    out SigmaTopologyEvidenceView rightEvidence,
                    out SigmaTopologyEvidenceView downEvidence);
                _topology.RebuildCurrent(page, targetEvidence, rightEvidence,
                    downEvidence, coordinates[coordinate]);
            }
        }

        private static void AddTopologyRebuild(
            Dictionary<SigmaCarrierPageCoordinate, bool> rebuild,
            SigmaCarrierPageCoordinate coordinate, bool forceBoundaryTransitions)
        {
            if (rebuild.TryGetValue(coordinate, out bool existing))
                rebuild[coordinate] = existing || forceBoundaryTransitions;
            else
                rebuild.Add(coordinate, forceBoundaryTransitions);
        }

        private static void ResolveTopologyEvidence(InFlightFrame frame,
            SigmaCarrierPageCoordinate coordinate,
            out SigmaTopologyEvidenceView target,
            out SigmaTopologyEvidenceView right,
            out SigmaTopologyEvidenceView down)
        {
            frame.TopologyEvidence.TryGetValue(coordinate, out target);
            right = default;
            down = default;
            if (TryOffset(coordinate, 1L, 0L,
                    out SigmaCarrierPageCoordinate rightCoordinate))
                frame.TopologyEvidence.TryGetValue(rightCoordinate, out right);
            if (TryOffset(coordinate, 0L, 1L,
                    out SigmaCarrierPageCoordinate downCoordinate))
                frame.TopologyEvidence.TryGetValue(downCoordinate, out down);
        }

        private static bool TryOffset(SigmaCarrierPageCoordinate source,
            long deltaX, long deltaY, out SigmaCarrierPageCoordinate result)
        {
            try
            {
                result = new SigmaCarrierPageCoordinate(
                    checked(source.X + deltaX), checked(source.Y + deltaY));
                return true;
            }
            catch (OverflowException)
            {
                result = default;
                return false;
            }
        }

        private int BeginGaugePageCommits(InFlightFrame frame,
            NativeArray<uint> blockFlags, NativeArray<uint> blockAnchors)
        {
            _gaugePages.Clear();
            for (int blockY = 0; blockY < frame.BlockResolution.y; ++blockY)
            {
                for (int blockX = 0; blockX < frame.BlockResolution.x; ++blockX)
                {
                    int blockIndex = blockY * frame.BlockResolution.x + blockX;
                    if ((uint)blockIndex >= (uint)blockFlags.Length ||
                        blockFlags[blockIndex] == 0u)
                        continue;
                    var page = new GaugeImagePage(blockX >> 1, blockY >> 1);
                    uint quadrant = 1u << (((blockY & 1) << 1) | (blockX & 1));
                    _gaugePages.TryGetValue(page, out uint mask);
                    _gaugePages[page] = mask | quadrant;
                }
            }
            if (_gaugePages.Count == 0)
                return 0;

            _gaugePageOrder.Clear();
            foreach (GaugeImagePage page in _gaugePages.Keys)
                _gaugePageOrder.Add(page);
            _gaugePageOrder.Sort();
            if (_gaugePageOrder.Count > maxGaugePagesPerCommit)
                _gaugePageOrder.RemoveRange(maxGaugePagesPerCommit,
                    _gaugePageOrder.Count - maxGaugePagesPerCommit);

            int minX = int.MaxValue;
            int minY = int.MaxValue;
            for (int index = 0; index < _gaugePageOrder.Count; ++index)
            {
                minX = Math.Min(minX, _gaugePageOrder[index].X);
                minY = Math.Min(minY, _gaugePageOrder[index].Y);
            }
            SigmaCarrierPageCoordinate origin = FindFreeGaugeOrigin(frame,
                blockAnchors, _gaugePageOrder, minX, minY);
            int scheduled = 0;
            for (int index = 0; index < _gaugePageOrder.Count; ++index)
            {
                GaugeImagePage imagePage = _gaugePageOrder[index];
                var coordinate = new SigmaCarrierPageCoordinate(
                    checked(origin.X + imagePage.X - minX),
                    checked(origin.Y + imagePage.Y - minY));
                SigmaCarrierWriteLease target = _carrier.BeginNullGeneration(
                    coordinate, frame.Revision);
                frame.GaugeWrites.Add(target);
                BindPromotion(frame, target, imagePage,
                    _gaugePages[imagePage], index);
                _inverse.Dispatch(_promoteKernel, 8, 8, 1);
                scheduled++;
            }
            return scheduled;
        }

        private void BindPromotion(InFlightFrame frame,
            SigmaCarrierWriteLease target, GaugeImagePage imagePage,
            uint blockMask, int commitIndex)
        {
            StereoRigFrameLease source = frame.Prediction.Source;
            target.BindWritable(_inverse, _promoteKernel, "_TargetCarrierState",
                "_TargetPageSlot", "_TargetPageCapacity");
            _backendGate.Bind(_inverse, _promoteKernel);
            BindCommonInverseInputs(_inverse, _promoteKernel, source,
                frame.Prediction, frame.LeftIndependenceKey,
                frame.RightIndependenceKey);
            _inverse.SetInts("_Resolution", frame.Resolution.x, frame.Resolution.y);
            _inverse.SetInts("_GaugePixelOrigin", imagePage.X * 64,
                imagePage.Y * 64);
            _inverse.SetInt("_GaugeBlockMask", unchecked((int)blockMask));
            _inverse.SetInt("_GaugeCommitCapacity", MaximumGaugeCommitSlots);
            _inverse.SetInt("_GaugeCommitIndex", commitIndex);
            _inverse.SetBuffer(_promoteKernel, "_GaugePromotionCounts",
                _gaugePromotionCounts);
        }

        private void RecordNormalize(CommandBuffer command,
            StereoRigFrameLease source)
        {
            command.SetComputeIntParams(_normalize, Id("_Resolution"),
                source.DepthResolution.x, source.DepthResolution.y);
            command.SetComputeVectorParam(_normalize, Id("_NearFar"),
                new Vector4(source.DepthNearFar.x, source.DepthNearFar.y, 0f, 0f));
            command.SetComputeTextureParam(_normalize, _normalizeKernel,
                Id("_RawDepth"), source.DepthLeft.Texture);
            command.SetComputeTextureParam(_normalize, _normalizeKernel,
                Id("_DepthRayCenterLeft"), _coneLuts.DepthLeft.CenterRaySolidAngle);
            command.SetComputeTextureParam(_normalize, _normalizeKernel,
                Id("_DepthRayCenterRight"), _coneLuts.DepthRight.CenterRaySolidAngle);
            command.SetComputeTextureParam(_normalize, _normalizeKernel,
                Id("_MetricDepth"), _metricDepth);
            command.SetComputeTextureParam(_normalize, _normalizeKernel,
                Id("_DepthFlags"), _depthFlags);
            command.DispatchCompute(_normalize, _normalizeKernel,
                CeilDiv(source.DepthResolution.x, 8),
                CeilDiv(source.DepthResolution.y, 8), 2);
        }

        private void RecordClear(CommandBuffer command, int activeFlagCount,
            int blockCount)
        {
            int clearCount = Math.Max(Math.Max(activeFlagCount, blockCount), 8);
            command.SetComputeIntParam(_inverse, "_ActiveFlagCount",
                activeFlagCount);
            command.SetComputeIntParam(_inverse, "_BlockFlagCount", blockCount);
            command.SetComputeIntParam(_inverse, "_GaugeCommitCapacity",
                MaximumGaugeCommitSlots);
            command.SetComputeIntParam(_inverse, "_ClearCount", clearCount);
            command.SetComputeBufferParam(_inverse, _clearKernel,
                "_ActivePageFlags", _activePageFlags);
            command.SetComputeBufferParam(_inverse, _clearKernel,
                "_CommitPageFlags", _commitPageFlags);
            command.SetComputeBufferParam(_inverse, _clearKernel,
                "_UnmatchedBlockFlags", _unmatchedBlockFlags);
            command.SetComputeBufferParam(_inverse, _clearKernel,
                "_UnmatchedBlockAnchors", _unmatchedBlockAnchors);
            command.SetComputeBufferParam(_inverse, _clearKernel,
                "_ConflictCount", _conflictCount);
            command.SetComputeBufferParam(_inverse, _clearKernel,
                "_FrameCounters", _frameCounters);
            command.SetComputeBufferParam(_inverse, _clearKernel,
                "_GaugePromotionCounts", _gaugePromotionCounts);
            command.DispatchCompute(_inverse, _clearKernel,
                CeilDiv(clearCount, 64), 1, 1);
        }

        private void BindFrameInputs(CommandBuffer command,
            StereoRigFrameLease source, SigmaPredictionFrameLease prediction,
            Vector2Int blockResolution, int segmentCount, uint leftKey,
            uint rightKey)
        {
            command.SetComputeIntParams(_inverse, Id("_Resolution"),
                source.DepthResolution.x, source.DepthResolution.y);
            command.SetComputeIntParams(_inverse, Id("_GaugeBlockResolution"),
                blockResolution.x, blockResolution.y);
            command.SetComputeIntParam(_inverse, "_SegmentCount", segmentCount);
            command.SetComputeIntParam(_inverse, "_LeftIndependenceKey",
                unchecked((int)leftKey));
            command.SetComputeIntParam(_inverse, "_RightIndependenceKey",
                unchecked((int)rightKey));
            SetFrameMatrices(command, source);
            command.SetComputeBufferParam(_inverse, _classifyKernel,
                "_DepthCalibrationQ48", _calibrationQ48);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                Id("_MetricDepth"), _metricDepth);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                Id("_DepthFlags"), _depthFlags);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                Id("_PredDepthSupport"), prediction.DepthSupport);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                Id("_PredStateKey"), prediction.StateKey);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                Id("_DepthRayCenterLeft"), _coneLuts.DepthLeft.CenterRaySolidAngle);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                Id("_DepthRayCenterRight"), _coneLuts.DepthRight.CenterRaySolidAngle);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                Id("_DepthSlopeBoundsLeft"), _coneLuts.DepthLeft.SlopeBounds);
            command.SetComputeTextureParam(_inverse, _classifyKernel,
                Id("_DepthSlopeBoundsRight"), _coneLuts.DepthRight.SlopeBounds);
            command.SetComputeBufferParam(_inverse, _classifyKernel,
                "_ActivePageFlags", _activePageFlags);
            command.SetComputeBufferParam(_inverse, _classifyKernel,
                "_UnmatchedBlockFlags", _unmatchedBlockFlags);
            command.SetComputeBufferParam(_inverse, _classifyKernel,
                "_UnmatchedBlockAnchors", _unmatchedBlockAnchors);
            command.SetComputeBufferParam(_inverse, _classifyKernel,
                "_FrameCounters", _frameCounters);
        }

        private void RecordCompactActive(CommandBuffer command,
            SigmaCarrierReadBatch batch, SegmentInverseScratch scratch, int segment)
        {
            command.SetComputeIntParam(_inverse, "_PageCapacity", batch.PageCapacity);
            command.SetComputeIntParam(_inverse, "_SegmentFlagOffset",
                segment * SegmentFlagStride);
            command.SetComputeBufferParam(_inverse, _compactKernel,
                "_ActivePageFlagsRead", _activePageFlags);
            command.SetComputeBufferParam(_inverse, _compactKernel,
                "_ActivePageSlots", scratch.ActivePageSlots);
            command.SetComputeBufferParam(_inverse, _compactKernel,
                "_ActiveDispatchArgs", scratch.ActiveDispatchArguments);
            command.SetComputeBufferParam(_inverse, _compactKernel,
                "_FrameCounters", _frameCounters);
            command.DispatchCompute(_inverse, _compactKernel, 1, 1, 1);
        }

        private void RecordBuildProposals(CommandBuffer command,
            SigmaCarrierReadBatch batch, SegmentInverseScratch scratch, int segment,
            SigmaPredictionFrameLease prediction, uint frameSerial)
        {
            command.SetComputeIntParam(_inverse, "_PageCapacity", batch.PageCapacity);
            command.SetComputeIntParam(_inverse, "_SegmentIndex", segment);
            command.SetComputeIntParam(_inverse, "_SegmentFlagOffset",
                segment * SegmentFlagStride);
            command.SetComputeIntParam(_inverse, "_ConflictCapacity",
                conflictCapacity);
            command.SetComputeIntParam(_inverse, "_ProposalFrameSerial",
                unchecked((int)frameSerial));
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_SigmaExactBackendGate", _backendGate.Buffer);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_DepthCalibrationQ48", _calibrationQ48);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_CarrierState", batch.State);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_PageMetadata", batch.Metadata);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_CurrentFlags", batch.CurrentFlags);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_ActivePageSlotsRead", scratch.ActivePageSlots);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_ProposalGeometry", scratch.ProposalGeometry);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_ProposalMass", scratch.ProposalMass);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_ProposalStatus", scratch.ProposalStatus);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_ProposalEpoch", scratch.ProposalEpoch);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_CommitPageFlags", _commitPageFlags);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_ConflictRecords", _conflictRecords);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_ConflictCount", _conflictCount);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_FrameCounters", _frameCounters);
            BindFrameTextures(command, _proposalKernel, prediction);
            command.DispatchCompute(_inverse, _proposalKernel,
                scratch.ActiveDispatchArguments, 0);
        }

        private void BindFrameTextures(CommandBuffer command, int kernel,
            SigmaPredictionFrameLease prediction)
        {
            command.SetComputeTextureParam(_inverse, kernel, Id("_MetricDepth"),
                _metricDepth);
            command.SetComputeTextureParam(_inverse, kernel, Id("_DepthFlags"),
                _depthFlags);
            command.SetComputeTextureParam(_inverse, kernel, Id("_PredDepthSupport"),
                prediction.DepthSupport);
            command.SetComputeTextureParam(_inverse, kernel, Id("_PredCarrierPage"),
                prediction.CarrierPage);
            command.SetComputeTextureParam(_inverse, kernel,
                Id("_PredCarrierUvNormal"), prediction.CarrierUvNormal);
            command.SetComputeTextureParam(_inverse, kernel, Id("_PredStateKey"),
                prediction.StateKey);
            command.SetComputeTextureParam(_inverse, kernel,
                Id("_DepthSlopeBoundsLeft"), _coneLuts.DepthLeft.SlopeBounds);
            command.SetComputeTextureParam(_inverse, kernel,
                Id("_DepthSlopeBoundsRight"), _coneLuts.DepthRight.SlopeBounds);
        }

        private void BindCommonInverseInputs(ComputeShader shader, int kernel,
            StereoRigFrameLease source, SigmaPredictionFrameLease prediction,
            uint leftKey, uint rightKey)
        {
            shader.SetBuffer(kernel, "_DepthCalibrationQ48", _calibrationQ48);
            shader.SetTexture(kernel, "_MetricDepth", _metricDepth);
            shader.SetTexture(kernel, "_DepthFlags", _depthFlags);
            shader.SetTexture(kernel, "_PredDepthSupport", prediction.DepthSupport);
            shader.SetTexture(kernel, "_PredCarrierPage", prediction.CarrierPage);
            shader.SetTexture(kernel, "_PredCarrierUvNormal",
                prediction.CarrierUvNormal);
            shader.SetTexture(kernel, "_PredStateKey", prediction.StateKey);
            shader.SetTexture(kernel, "_DepthRayCenterLeft",
                _coneLuts.DepthLeft.CenterRaySolidAngle);
            shader.SetTexture(kernel, "_DepthRayCenterRight",
                _coneLuts.DepthRight.CenterRaySolidAngle);
            shader.SetTexture(kernel, "_DepthSlopeBoundsLeft",
                _coneLuts.DepthLeft.SlopeBounds);
            shader.SetTexture(kernel, "_DepthSlopeBoundsRight",
                _coneLuts.DepthRight.SlopeBounds);
            shader.SetInt("_LeftIndependenceKey", unchecked((int)leftKey));
            shader.SetInt("_RightIndependenceKey", unchecked((int)rightKey));
            SetFrameMatrices(shader, source);
        }

        private void EnsureCalibration(StereoRigFrameLease source)
        {
            if (_calibration != null && _calibration.IsCompatible(source))
                return;
            if (!RigCalibration.TryCreate(source, out RigCalibration calibration))
                throw new InvalidOperationException("Unable to freeze inverse rig calibration.");
            _coneLuts?.Retire();
            _calibration = calibration;
            _coneLuts = RigConeLutSet.Create(_coneLutShader, calibration);
        }

        private void EnsureFrameResources(Vector2Int resolution)
        {
            if (_scratchResolution == resolution && _metricDepth != null &&
                _depthFlags != null)
                return;
            DestroyTexture(_metricDepth);
            DestroyTexture(_depthFlags);
            _metricDepth = CreateArrayTexture("Sigma metric depth", resolution,
                GraphicsFormat.R32G32_SFloat);
            _depthFlags = CreateArrayTexture("Sigma depth flags", resolution,
                GraphicsFormat.R32_UInt);
            _scratchResolution = resolution;
        }

        private void EnsureFlagBuffers(int activeFlagCount, int blockCount)
        {
            if (_activeFlagCapacity < activeFlagCount)
            {
                _activePageFlags?.Dispose();
                _commitPageFlags?.Dispose();
                _activeFlagCapacity = NextPowerOfTwo(activeFlagCount);
                _activePageFlags = CreateBuffer(_activeFlagCapacity, sizeof(uint),
                    "Sigma active page flags");
                _commitPageFlags = CreateBuffer(_activeFlagCapacity, sizeof(uint),
                    "Sigma commit page flags");
            }
            if (_blockFlagCapacity < blockCount)
            {
                _unmatchedBlockFlags?.Dispose();
                _unmatchedBlockAnchors?.Dispose();
                _blockFlagCapacity = NextPowerOfTwo(blockCount);
                _unmatchedBlockFlags = CreateBuffer(_blockFlagCapacity,
                    sizeof(uint), "Sigma unmatched gauge blocks");
                _unmatchedBlockAnchors = CreateBuffer(_blockFlagCapacity,
                    sizeof(uint), "Sigma unmatched gauge anchors");
            }
        }

        private void EnsureSegmentScratch()
        {
            for (int index = 0; index < _readBatches.Count; ++index)
            {
                SigmaCarrierReadBatch batch = _readBatches[index];
                if (index < _segmentScratch.Count &&
                    _segmentScratch[index].Matches(batch))
                    continue;
                if (index < _segmentScratch.Count)
                {
                    _segmentScratch[index].Dispose();
                    _segmentScratch[index] = new SegmentInverseScratch(batch);
                }
                else
                    _segmentScratch.Add(new SegmentInverseScratch(batch));
            }
            for (int index = _segmentScratch.Count - 1;
                index >= _readBatches.Count; --index)
            {
                _segmentScratch[index].Dispose();
                _segmentScratch.RemoveAt(index);
            }
        }

        private void UploadExactCalibration(StereoRigFrameLease source)
        {
            FillCalibration(0, source.DepthLeft, source.Health);
            FillCalibration(1, source.DepthRight, source.Health);
            _calibrationQ48.SetData(_calibrationUpload);
        }

        private void FillCalibration(int eye, GpuImageView view,
            RigPairingHealth health)
        {
            int offset = eye * CalibrationStride;
            SetQ(offset + 0, view.Intrinsics.FocalLength.x);
            SetQ(offset + 1, view.Intrinsics.FocalLength.y);
            SetQ(offset + 2, view.Intrinsics.PrincipalPoint.x);
            SetQ(offset + 3, view.Intrinsics.PrincipalPoint.y);
            Matrix4x4 world = Matrix4x4.TRS(view.WorldFromCamera.position,
                view.WorldFromCamera.rotation, Vector3.one);
            int cursor = offset + 4;
            for (int row = 0; row < 3; ++row)
            {
                for (int column = 0; column < 3; ++column)
                    SetQ(cursor++, world[row, column]);
            }
            SetQ(offset + 13, world[0, 3]);
            SetQ(offset + 14, world[1, 3]);
            SetQ(offset + 15, world[2, 3]);
            SetQ(offset + 16, view.DepthNearFar.x);
            SetQ(offset + 17, RigDepthContract.FiniteRasterFar(view.DepthNearFar));
            double clockWidth = Math.Min(0.01,
                health.ClockUncertaintyNanoseconds * 1e-9 * 0.5);
            SetQ(offset + 18, Math.Max(0.001, clockWidth));
            double[] thresholds = { 0.5, 1.0, 2.0, 3.0, 5.0, 32767.0 };
            double[] widths = { 0.003, 0.0045, 0.007, 0.012, 0.025, 0.05 };
            for (int bin = 0; bin < 6; ++bin)
            {
                SetQ(offset + 19 + bin, thresholds[bin]);
                SetQ(offset + 25 + bin, widths[bin]);
            }
            SetQ(offset + 31, 0.001);
            SetQ(offset + 32, 0.05);
            SetQRaw(offset + 33, SigmaNumericDomain.FromRatio(1, 64));
            SetQRaw(offset + 34, 0L);
            SetQRaw(offset + 35, 0L);
        }

        private void SetQ(int index, double value) =>
            SetQRaw(index, SigmaNumericDomain.Quantize(value));

        private void SetQRaw(int index, long raw) =>
            _calibrationUpload[index] = SigmaPackedQ48.FromRaw(raw);

        private void SetFrameMatrices(CommandBuffer command,
            StereoRigFrameLease source)
        {
            Matrix4x4 leftWorld = Matrix4x4.TRS(source.DepthLeft.WorldFromCamera.position,
                source.DepthLeft.WorldFromCamera.rotation, Vector3.one);
            Matrix4x4 rightWorld = Matrix4x4.TRS(source.DepthRight.WorldFromCamera.position,
                source.DepthRight.WorldFromCamera.rotation, Vector3.one);
            command.SetComputeMatrixParam(_inverse, Id("_WorldFromOpticalLeft"), leftWorld);
            command.SetComputeMatrixParam(_inverse, Id("_WorldFromOpticalRight"), rightWorld);
            command.SetComputeMatrixParam(_inverse, Id("_OpticalFromWorldLeft"),
                leftWorld.inverse);
            command.SetComputeMatrixParam(_inverse, Id("_OpticalFromWorldRight"),
                rightWorld.inverse);
            command.SetComputeVectorParam(_inverse, Id("_DepthIntrinsicsLeft"),
                IntrinsicsVector(source.DepthLeft.Intrinsics));
            command.SetComputeVectorParam(_inverse, Id("_DepthIntrinsicsRight"),
                IntrinsicsVector(source.DepthRight.Intrinsics));
        }

        private void SetFrameMatrices(ComputeShader shader,
            StereoRigFrameLease source)
        {
            Matrix4x4 leftWorld = Matrix4x4.TRS(source.DepthLeft.WorldFromCamera.position,
                source.DepthLeft.WorldFromCamera.rotation, Vector3.one);
            Matrix4x4 rightWorld = Matrix4x4.TRS(source.DepthRight.WorldFromCamera.position,
                source.DepthRight.WorldFromCamera.rotation, Vector3.one);
            shader.SetMatrix("_WorldFromOpticalLeft", leftWorld);
            shader.SetMatrix("_WorldFromOpticalRight", rightWorld);
            shader.SetMatrix("_OpticalFromWorldLeft", leftWorld.inverse);
            shader.SetMatrix("_OpticalFromWorldRight", rightWorld.inverse);
            shader.SetVector("_DepthIntrinsicsLeft",
                IntrinsicsVector(source.DepthLeft.Intrinsics));
            shader.SetVector("_DepthIntrinsicsRight",
                IntrinsicsVector(source.DepthRight.Intrinsics));
        }

        private static Vector4 IntrinsicsVector(RigIntrinsics intrinsics) => new(
            intrinsics.FocalLength.x, intrinsics.FocalLength.y,
            intrinsics.PrincipalPoint.x, intrinsics.PrincipalPoint.y);

        private SigmaCarrierPageCoordinate FindFreeGaugeOrigin(
            InFlightFrame frame, NativeArray<uint> blockAnchors,
            IReadOnlyList<GaugeImagePage> pages, int minX, int minY)
        {
            if (TryFindAdjacentGaugeOrigin(frame, blockAnchors, pages, minX,
                    minY, out SigmaCarrierPageCoordinate adjacent))
                return adjacent;

            for (int attempt = 0; attempt < 1_000_000; ++attempt)
            {
                SigmaCarrierPageCoordinate candidate = SignedMortonCoordinate(
                    _nextGaugeOriginOrdinal++);
                if (GaugeRegionIsFree(candidate, pages, minX, minY))
                    return candidate;
            }
            throw new InvalidOperationException(
                "Unable to place a deterministic latent gauge page group.");
        }

        private bool TryFindAdjacentGaugeOrigin(InFlightFrame frame,
            NativeArray<uint> blockAnchors, IReadOnlyList<GaugeImagePage> pages,
            int minX, int minY, out SigmaCarrierPageCoordinate origin)
        {
            for (int pageIndex = 0; pageIndex < pages.Count; ++pageIndex)
            {
                GaugeImagePage imagePage = pages[pageIndex];
                uint mask = _gaugePages[imagePage];
                for (int quadrant = 0; quadrant < 4; ++quadrant)
                {
                    if ((mask & (1u << quadrant)) == 0u)
                        continue;
                    int blockX = imagePage.X * 2 + (quadrant & 1);
                    int blockY = imagePage.Y * 2 + (quadrant >> 1);
                    if ((uint)blockX >= (uint)frame.BlockResolution.x ||
                        (uint)blockY >= (uint)frame.BlockResolution.y)
                        continue;
                    int blockIndex = blockY * frame.BlockResolution.x + blockX;
                    if ((uint)blockIndex >= (uint)blockAnchors.Length)
                        continue;
                    uint globalAnchor = blockAnchors[blockIndex];
                    if (globalAnchor == uint.MaxValue ||
                        !frame.PageSnapshot.TryGetValue(
                            unchecked((int)globalAnchor), out SigmaCarrierPageHandle anchor))
                        continue;

                    for (int neighbourIndex = 0;
                        neighbourIndex < GaugeNeighbourOffsets.Length;
                        ++neighbourIndex)
                    {
                        Vector2Int offset = GaugeNeighbourOffsets[neighbourIndex];
                        var candidate = new SigmaCarrierPageCoordinate(
                            checked(anchor.Coordinate.X + offset.x -
                                (imagePage.X - minX)),
                            checked(anchor.Coordinate.Y + offset.y -
                                (imagePage.Y - minY)));
                        if (!GaugeRegionIsFree(candidate, pages, minX, minY))
                            continue;
                        origin = candidate;
                        return true;
                    }
                }
            }
            origin = default;
            return false;
        }

        private bool GaugeRegionIsFree(SigmaCarrierPageCoordinate origin,
            IReadOnlyList<GaugeImagePage> pages, int minX, int minY)
        {
            for (int index = 0; index < pages.Count; ++index)
            {
                var coordinate = new SigmaCarrierPageCoordinate(
                    checked(origin.X + pages[index].X - minX),
                    checked(origin.Y + pages[index].Y - minY));
                if (_carrier.TryGetLatest(coordinate, out _))
                    return false;
            }
            return true;
        }

        internal static SigmaCarrierPageCoordinate SignedMortonCoordinate(
            ulong ordinal)
        {
            uint x = CompactMorton(ordinal);
            uint y = CompactMorton(ordinal >> 1);
            return new SigmaCarrierPageCoordinate(ZigZagDecode(x), ZigZagDecode(y));
        }

        private static uint CompactMorton(ulong value)
        {
            value &= 0x5555_5555_5555_5555UL;
            value = (value | value >> 1) & 0x3333_3333_3333_3333UL;
            value = (value | value >> 2) & 0x0f0f_0f0f_0f0f_0f0fUL;
            value = (value | value >> 4) & 0x00ff_00ff_00ff_00ffUL;
            value = (value | value >> 8) & 0x0000_ffff_0000_ffffUL;
            value = (value | value >> 16) & 0x0000_0000_ffff_ffffUL;
            return (uint)value;
        }

        private static long ZigZagDecode(uint value) =>
            (long)(value >> 1) ^ -((long)value & 1L);

        private uint DetermineNextRevision()
        {
            _carrier.CollectCurrentPages(_currentPages);
            uint maximum = 0u;
            for (int index = 0; index < _currentPages.Count; ++index)
                maximum = Math.Max(maximum, _currentPages[index].Revision);
            return checked(maximum + 1u);
        }

        private static uint IndependenceKey(GpuImageView view, uint epoch)
        {
            unchecked
            {
                uint hash = 2166136261u;
                Mix(ref hash, epoch);
                Mix(ref hash, (uint)view.Eye + 1u);
                Vector3 position = view.WorldFromCamera.position;
                Quaternion rotation = view.WorldFromCamera.rotation;
                Mix(ref hash, (uint)Mathf.RoundToInt(position.x * 25f));
                Mix(ref hash, (uint)Mathf.RoundToInt(position.y * 25f));
                Mix(ref hash, (uint)Mathf.RoundToInt(position.z * 25f));
                Mix(ref hash, (uint)Mathf.RoundToInt(rotation.x * 64f));
                Mix(ref hash, (uint)Mathf.RoundToInt(rotation.y * 64f));
                Mix(ref hash, (uint)Mathf.RoundToInt(rotation.z * 64f));
                Mix(ref hash, (uint)Mathf.RoundToInt(rotation.w * 64f));
                return hash == 0u ? 1u : hash;
            }
        }

        private static void Mix(ref uint hash, uint value)
        {
            unchecked
            {
                hash ^= value;
                hash *= 16777619u;
            }
        }

        private static int GlobalPageIndex(int segment, int slot) =>
            checked(segment * SegmentFlagStride + slot);

        private static int Id(string name) => Shader.PropertyToID(name);

        private static RenderTexture CreateArrayTexture(string name,
            Vector2Int resolution, GraphicsFormat format)
        {
            if (!SystemInfo.IsFormatSupported(format,
                    GraphicsFormatUsage.LoadStore))
                throw new InvalidOperationException(
                    $"Required inverse texture format unsupported: {format}.");
            var descriptor = new RenderTextureDescriptor(resolution.x, resolution.y)
            {
                graphicsFormat = format,
                depthBufferBits = 0,
                msaaSamples = 1,
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 2,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false
            };
            var texture = new RenderTexture(descriptor)
            {
                name = $"[Sigma-PRISM-16] {name}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!texture.Create())
                throw new InvalidOperationException($"Unable to create {name}.");
            return texture;
        }

        private static GraphicsBuffer CreateBuffer(int count, int stride,
            string name) => new(GraphicsBuffer.Target.Structured,
                Math.Max(1, count), stride) { name = name };

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
                result = checked(result << 1);
            return result;
        }

        private static void DestroyTexture(RenderTexture texture)
        {
            if (texture == null)
                return;
            texture.Release();
            if (Application.isPlaying)
                Destroy(texture);
            else
                DestroyImmediate(texture);
        }

        private void OnDestroy()
        {
            if (_disposed)
                return;
            _disposed = true;
            _running = false;
            if (_renderer != null)
                _renderer.PredictionReady -= OnPredictionReady;
            _pendingPrediction?.Dispose();
            _pendingPrediction = null;
            InverseOwnedResources resources = DetachOwnedResources();
            InFlightFrame frame = _inFlight;
            _inFlight = null;
            if (frame == null)
                resources.Dispose();
            else
            {
                frame.Discard = true;
                frame.RetireWhenReadbacksComplete(() =>
                {
                    try { frame.Dispose(); }
                    finally { resources.Dispose(); }
                });
            }
            _initialized = false;
        }

        private InverseOwnedResources DetachOwnedResources()
        {
            var scratch = _segmentScratch.ToArray();
            _segmentScratch.Clear();
            var resources = new InverseOwnedResources(_coneLuts, _metricDepth,
                _depthFlags, _calibrationQ48, _activePageFlags,
                _commitPageFlags, _unmatchedBlockFlags, _unmatchedBlockAnchors,
                _conflictRecords,
                _conflictCount, _frameCounters, _gaugePromotionCounts, scratch);
            _coneLuts = null;
            _calibration = null;
            _metricDepth = null;
            _depthFlags = null;
            _calibrationQ48 = null;
            _activePageFlags = null;
            _commitPageFlags = null;
            _unmatchedBlockFlags = null;
            _unmatchedBlockAnchors = null;
            _conflictRecords = null;
            _conflictCount = null;
            _frameCounters = null;
            _gaugePromotionCounts = null;
            _activeFlagCapacity = 0;
            _blockFlagCapacity = 0;
            _scratchResolution = default;
            return resources;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct SigmaPackedQ48
        {
            private SigmaPackedQ48(uint low, uint high)
            {
                Low = low;
                High = high;
            }

            public readonly uint Low;
            public readonly uint High;

            public static SigmaPackedQ48 FromRaw(long raw) => new(
                unchecked((uint)raw), unchecked((uint)(raw >> 32)));
        }

        private readonly struct PendingGaugePublication
        {
            public PendingGaugePublication(SigmaCarrierWriteLease write,
                uint promotedSamples)
            {
                Write = write;
                PromotedSamples = promotedSamples;
            }

            public SigmaCarrierWriteLease Write { get; }
            public uint PromotedSamples { get; }
        }

        private readonly struct GaugeImagePage : IEquatable<GaugeImagePage>,
            IComparable<GaugeImagePage>
        {
            public GaugeImagePage(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X { get; }
            public int Y { get; }
            public int CompareTo(GaugeImagePage other)
            {
                int y = Y.CompareTo(other.Y);
                return y != 0 ? y : X.CompareTo(other.X);
            }
            public bool Equals(GaugeImagePage other) => X == other.X && Y == other.Y;
            public override bool Equals(object obj) =>
                obj is GaugeImagePage other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(X, Y);
        }

        private sealed class SegmentInverseScratch : IDisposable
        {
            private readonly GraphicsBuffer _stateIdentity;
            private readonly GraphicsBuffer _metadataIdentity;

            public SegmentInverseScratch(SigmaCarrierReadBatch batch)
            {
                SegmentIndex = batch.SegmentIndex;
                Capacity = batch.PageCapacity;
                _stateIdentity = batch.State;
                _metadataIdentity = batch.Metadata;
                ActivePageSlots = CreateBuffer(Capacity, sizeof(uint),
                    $"Sigma inverse active slots {SegmentIndex}");
                ActiveDispatchArguments = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured |
                    GraphicsBuffer.Target.IndirectArguments, 3, sizeof(uint))
                {
                    name = $"Sigma inverse dispatch {SegmentIndex}"
                };
                ActiveDispatchArguments.SetData(new uint[] { 64u, 0u, 1u });
                ProposalGeometry = CreateBuffer(checked(Capacity *
                    SigmaCarrier.SamplesPerPage * 3), sizeof(uint) * 2,
                    $"Sigma inverse proposal geometry {SegmentIndex}");
                ProposalStatus = CreateBuffer(checked(Capacity *
                    SigmaCarrier.SamplesPerPage), sizeof(uint),
                    $"Sigma inverse proposal status {SegmentIndex}");
                ProposalEpoch = CreateBuffer(Capacity, sizeof(uint),
                    $"Sigma inverse proposal epoch {SegmentIndex}");
                ProposalEpoch.SetData(new uint[Capacity]);
                ProposalMass = CreateBuffer(checked(Capacity *
                    SigmaCarrier.SamplesPerPage), sizeof(uint) * 2,
                    $"Sigma inverse proposal mass {SegmentIndex}");
            }

            public int SegmentIndex { get; }
            public int Capacity { get; }
            public GraphicsBuffer ActivePageSlots { get; }
            public GraphicsBuffer ActiveDispatchArguments { get; }
            public GraphicsBuffer ProposalGeometry { get; }
            public GraphicsBuffer ProposalMass { get; }
            public GraphicsBuffer ProposalStatus { get; }
            public GraphicsBuffer ProposalEpoch { get; }

            public bool Matches(SigmaCarrierReadBatch batch) =>
                SegmentIndex == batch.SegmentIndex && Capacity == batch.PageCapacity &&
                ReferenceEquals(_stateIdentity, batch.State) &&
                ReferenceEquals(_metadataIdentity, batch.Metadata);

            public void Dispose()
            {
                ActivePageSlots.Dispose();
                ActiveDispatchArguments.Dispose();
                ProposalGeometry.Dispose();
                ProposalMass.Dispose();
                ProposalStatus.Dispose();
                ProposalEpoch.Dispose();
            }
        }

        private sealed class InverseOwnedResources : IDisposable
        {
            private RigConeLutSet _coneLuts;
            private RenderTexture _metricDepth;
            private RenderTexture _depthFlags;
            private GraphicsBuffer[] _buffers;
            private SegmentInverseScratch[] _scratch;

            public InverseOwnedResources(RigConeLutSet coneLuts,
                RenderTexture metricDepth, RenderTexture depthFlags,
                GraphicsBuffer calibration, GraphicsBuffer activeFlags,
                GraphicsBuffer commitFlags, GraphicsBuffer unmatchedFlags,
                GraphicsBuffer unmatchedAnchors,
                GraphicsBuffer conflicts, GraphicsBuffer conflictCount,
                GraphicsBuffer counters, GraphicsBuffer gaugePromotionCounts,
                SegmentInverseScratch[] scratch)
            {
                _coneLuts = coneLuts;
                _metricDepth = metricDepth;
                _depthFlags = depthFlags;
                _buffers = new[] { calibration, activeFlags, commitFlags,
                    unmatchedFlags, unmatchedAnchors, conflicts, conflictCount, counters,
                    gaugePromotionCounts };
                _scratch = scratch ?? Array.Empty<SegmentInverseScratch>();
            }

            public void Dispose()
            {
                _coneLuts?.Retire();
                _coneLuts = null;
                DestroyTexture(_metricDepth);
                DestroyTexture(_depthFlags);
                _metricDepth = null;
                _depthFlags = null;
                if (_buffers != null)
                {
                    for (int index = 0; index < _buffers.Length; ++index)
                        _buffers[index]?.Dispose();
                    _buffers = null;
                }
                if (_scratch != null)
                {
                    for (int index = 0; index < _scratch.Length; ++index)
                        _scratch[index]?.Dispose();
                    _scratch = null;
                }
            }
        }

        /// <summary>
        /// Keeps teardown resources alive until every small asynchronous scheduler
        /// readback issued for the frame has completed. It never exposes pixel or
        /// carrier data to the CPU and never blocks the render thread.
        /// </summary>
        private sealed class ReadbackRetirementLatch
        {
            private readonly object _gate = new();
            private int _pending;
            private Action _retirement;

            public AsyncGPUReadbackRequest Request(GraphicsBuffer buffer)
            {
                if (buffer == null)
                    throw new ArgumentNullException(nameof(buffer));
                lock (_gate)
                    checked { _pending++; }
                try
                {
                    return AsyncGPUReadback.Request(buffer, OnCompleted);
                }
                catch
                {
                    OnCompleted(default);
                    throw;
                }
            }

            public void RetireWhenComplete(Action retirement)
            {
                if (retirement == null)
                    throw new ArgumentNullException(nameof(retirement));
                Action invoke = null;
                lock (_gate)
                {
                    if (_retirement != null)
                        throw new InvalidOperationException(
                            "Readback retirement was already registered.");
                    if (_pending == 0)
                        invoke = retirement;
                    else
                        _retirement = retirement;
                }
                invoke?.Invoke();
            }

            private void OnCompleted(AsyncGPUReadbackRequest request)
            {
                Action invoke = null;
                lock (_gate)
                {
                    if (_pending <= 0)
                        return;
                    _pending--;
                    if (_pending == 0 && _retirement != null)
                    {
                        invoke = _retirement;
                        _retirement = null;
                    }
                }
                invoke?.Invoke();
            }
        }

        private sealed class InFlightFrame : IDisposable
        {
            public InFlightFrame(SigmaPredictionFrameLease prediction,
                uint revision, Vector2Int resolution, Vector2Int blockResolution,
                int segmentCount, uint leftIndependenceKey,
                uint rightIndependenceKey,
                Dictionary<int, SigmaCarrierPageHandle> pageSnapshot,
                Dictionary<SigmaCarrierPageCoordinate,
                    SigmaTopologyEvidenceView> topologyEvidence,
                ReadbackRetirementLatch readbackLatch,
                AsyncGPUReadbackRequest activePageFlags,
                AsyncGPUReadbackRequest commitFlags,
                AsyncGPUReadbackRequest unmatchedBlocks,
                AsyncGPUReadbackRequest unmatchedAnchors,
                AsyncGPUReadbackRequest counters,
                AsyncGPUReadbackRequest conflictCount)
            {
                Prediction = prediction;
                Revision = revision;
                Resolution = resolution;
                BlockResolution = blockResolution;
                SegmentCount = segmentCount;
                LeftIndependenceKey = leftIndependenceKey;
                RightIndependenceKey = rightIndependenceKey;
                PageSnapshot = pageSnapshot;
                TopologyEvidence = topologyEvidence ?? throw new
                    ArgumentNullException(nameof(topologyEvidence));
                _readbackLatch = readbackLatch ?? throw new ArgumentNullException(
                    nameof(readbackLatch));
                ActivePageFlags = activePageFlags;
                CommitFlags = commitFlags;
                UnmatchedBlocks = unmatchedBlocks;
                UnmatchedAnchors = unmatchedAnchors;
                Counters = counters;
                ConflictCount = conflictCount;
            }

            public SigmaPredictionFrameLease Prediction { get; }
            public uint Revision { get; }
            public Vector2Int Resolution { get; }
            public Vector2Int BlockResolution { get; }
            public int SegmentCount { get; }
            public uint LeftIndependenceKey { get; }
            public uint RightIndependenceKey { get; }
            public Dictionary<int, SigmaCarrierPageHandle> PageSnapshot { get; }
            public Dictionary<SigmaCarrierPageCoordinate,
                SigmaTopologyEvidenceView> TopologyEvidence { get; }
            public AsyncGPUReadbackRequest ActivePageFlags { get; }
            public AsyncGPUReadbackRequest CommitFlags { get; }
            public AsyncGPUReadbackRequest UnmatchedBlocks { get; }
            public AsyncGPUReadbackRequest UnmatchedAnchors { get; }
            public AsyncGPUReadbackRequest Counters { get; }
            public AsyncGPUReadbackRequest ConflictCount { get; }
            public AsyncGPUReadbackRequest GaugePromotions { get; private set; }
            public List<SigmaCarrierWriteLease> GaugeWrites { get; } = new();
            public bool Discard { get; set; }
            public bool HasGaugeReadback { get; private set; }
            public int MatchedPageCommits { get; set; }
            public int PublishedGaugePages { get; set; }
            public uint ProducedConflicts { get; set; }
            private readonly ReadbackRetirementLatch _readbackLatch;
            public bool InitialReadbackHasError => ActivePageFlags.hasError ||
                CommitFlags.hasError ||
                UnmatchedBlocks.hasError || UnmatchedAnchors.hasError ||
                Counters.hasError || ConflictCount.hasError;
            public bool AllRequestsDone => HasGaugeReadback
                ? GaugePromotions.done
                : ActivePageFlags.done && CommitFlags.done &&
                    UnmatchedBlocks.done && Counters.done &&
                    UnmatchedAnchors.done && ConflictCount.done;
            public bool HasError => HasGaugeReadback
                ? GaugePromotions.hasError
                : InitialReadbackHasError;

            public void BeginGaugeReadback(GraphicsBuffer promotionCounts)
            {
                if (HasGaugeReadback)
                    throw new InvalidOperationException(
                        "Gauge promotion readback has already been requested.");
                GaugePromotions = _readbackLatch.Request(promotionCounts);
                HasGaugeReadback = true;
            }

            public void RetireWhenReadbacksComplete(Action retirement) =>
                _readbackLatch.RetireWhenComplete(retirement);

            public void AbortGaugeWrites()
            {
                for (int index = 0; index < GaugeWrites.Count; ++index)
                    GaugeWrites[index].Dispose();
                GaugeWrites.Clear();
            }

            public void Dispose()
            {
                AbortGaugeWrites();
                Prediction.Dispose();
            }
        }
    }

    public readonly struct SigmaInverseDiagnosticSnapshot
    {
        private SigmaInverseDiagnosticSnapshot(uint activePages, uint hitSamples,
            uint changedSamples, uint emptyMeets, uint exclusions,
            uint unmatchedBlocks, uint promotedSamples, uint failedChecks,
            uint evidenceRecords)
        {
            ActivePages = activePages;
            HitSamples = hitSamples;
            ChangedSamples = changedSamples;
            EmptyMeets = emptyMeets;
            Exclusions = exclusions;
            UnmatchedBlocks = unmatchedBlocks;
            PromotedSamples = promotedSamples;
            FailedChecks = failedChecks;
            EvidenceRecords = evidenceRecords;
        }

        public uint ActivePages { get; }
        public uint HitSamples { get; }
        public uint ChangedSamples { get; }
        public uint EmptyMeets { get; }
        public uint Exclusions { get; }
        public uint UnmatchedBlocks { get; }
        public uint PromotedSamples { get; }
        public uint FailedChecks { get; }
        public uint EvidenceRecords { get; }

        internal static SigmaInverseDiagnosticSnapshot From(
            NativeArray<uint> counters, uint evidenceRecords,
            uint promotedSamples) => new(
                Value(counters, 0), Value(counters, 1), Value(counters, 2),
                Value(counters, 3), Value(counters, 4), Value(counters, 5),
                promotedSamples, Value(counters, 7), evidenceRecords);

        private static uint Value(NativeArray<uint> values, int index) =>
            (uint)index < (uint)values.Length ? values[index] : 0u;
    }
}
