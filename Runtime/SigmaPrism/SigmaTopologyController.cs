using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    internal readonly struct SigmaTopologyEvidenceView
    {
        internal const int ProposalMode = 1;

        private SigmaTopologyEvidenceView(SigmaCarrierPageCoordinate coordinate,
            GraphicsBuffer proposalStatus, GraphicsBuffer proposalEpoch,
            int pageSlot, int pageCapacity, uint frameSerial,
            uint leftIndependenceKey, uint rightIndependenceKey, int mode)
        {
            Coordinate = coordinate;
            ProposalStatus = proposalStatus;
            ProposalEpoch = proposalEpoch;
            PageSlot = pageSlot;
            PageCapacity = pageCapacity;
            FrameSerial = frameSerial;
            LeftIndependenceKey = leftIndependenceKey;
            RightIndependenceKey = rightIndependenceKey;
            Mode = mode;
        }

        internal SigmaCarrierPageCoordinate Coordinate { get; }
        internal GraphicsBuffer ProposalStatus { get; }
        internal GraphicsBuffer ProposalEpoch { get; }
        internal int PageSlot { get; }
        internal int PageCapacity { get; }
        internal uint FrameSerial { get; }
        internal uint LeftIndependenceKey { get; }
        internal uint RightIndependenceKey { get; }
        internal int Mode { get; }
        internal bool IsValid => Mode != 0;

        internal static SigmaTopologyEvidenceView Proposals(
            SigmaCarrierPageCoordinate coordinate, GraphicsBuffer proposalStatus,
            GraphicsBuffer proposalEpoch, int pageSlot, int pageCapacity,
            uint frameSerial, uint leftIndependenceKey,
            uint rightIndependenceKey)
        {
            if (proposalStatus == null)
                throw new ArgumentNullException(nameof(proposalStatus));
            if (proposalEpoch == null)
                throw new ArgumentNullException(nameof(proposalEpoch));
            if (pageSlot < 0 || pageSlot >= pageCapacity)
                throw new ArgumentOutOfRangeException(nameof(pageSlot));
            return new SigmaTopologyEvidenceView(coordinate, proposalStatus,
                proposalEpoch, pageSlot, pageCapacity, frameSerial,
                leftIndependenceKey, rightIndependenceKey, 1);
        }

        internal static SigmaTopologyEvidenceView FullStereo(
            SigmaCarrierPageCoordinate coordinate, uint frameSerial,
            uint leftIndependenceKey, uint rightIndependenceKey) => new(
                coordinate, null, null, 0, 1, frameSerial,
                leftIndependenceKey, rightIndependenceKey, 2);

        internal static SigmaTopologyEvidenceView GaugeRebuild(
            SigmaCarrierPageCoordinate coordinate, uint frameSerial) => new(
                coordinate, null, null, 0, 1, frameSerial, 0u, 0u, 3);
    }

    public readonly struct SigmaTopologySegmentView
    {
        internal SigmaTopologySegmentView(int segmentIndex, int pageCapacity,
            GraphicsBuffer transitionRecords, GraphicsBuffer cellFlags,
            GraphicsBuffer pageKeys)
        {
            SegmentIndex = segmentIndex;
            PageCapacity = pageCapacity;
            TransitionRecords = transitionRecords;
            CellFlags = cellFlags;
            PageKeys = pageKeys;
        }

        public int SegmentIndex { get; }
        public int PageCapacity { get; }
        public GraphicsBuffer TransitionRecords { get; }
        public GraphicsBuffer CellFlags { get; }
        public GraphicsBuffer PageKeys { get; }
    }

    public readonly struct SigmaTopologyBuildToken
    {
        internal SigmaTopologyBuildToken(SigmaTopologyController owner,
            SigmaCarrierPageHandle handle)
        {
            _owner = owner;
            Handle = handle;
        }

        private readonly SigmaTopologyController _owner;
        public SigmaCarrierPageHandle Handle { get; }
        public bool IsValid => _owner != null && Handle.IsValid;
        public void Publish()
        {
            if (!IsValid)
                throw new InvalidOperationException(
                    "Invalid intrinsic-topology publication token.");
            _owner.PublishBuiltGeneration(Handle);
        }
    }

    internal readonly struct SigmaTopologyGaugeBinding
    {
        internal SigmaTopologyGaugeBinding(GraphicsBuffer sourceTransitions,
            int sourceSlot, int sourceCapacity, GraphicsBuffer targetTransitions,
            int targetSlot, int targetCapacity)
        {
            SourceTransitions = sourceTransitions;
            SourceSlot = sourceSlot;
            SourceCapacity = sourceCapacity;
            TargetTransitions = targetTransitions;
            TargetSlot = targetSlot;
            TargetCapacity = targetCapacity;
        }

        internal GraphicsBuffer SourceTransitions { get; }
        internal int SourceSlot { get; }
        internal int SourceCapacity { get; }
        internal GraphicsBuffer TargetTransitions { get; }
        internal int TargetSlot { get; }
        internal int TargetCapacity { get; }
    }

    /// <summary>
    /// Disposable intrinsic-topology readout. It caches exact neighbour transitions,
    /// full annihilator scans and associator gates by immutable carrier generation;
    /// it owns no boundary, chart, mesh or canonical topology state.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SigmaCarrier))]
    [DefaultExecutionOrder(-5)]
    public sealed class SigmaTopologyController : MonoBehaviour, IRoomScanModule
    {
        public const int TransitionsPerPage = SigmaCarrier.SamplesPerPage * 2;
        public const int TransitionRecordStride = sizeof(uint) * 4;
        public const int TopologyCandidateCapacity = 1024;
        private const uint CandidateDispatchOffset = 0u;
        private const uint WitnessDispatchOffset = 3u * sizeof(uint);

        private const string TopologyResource =
            "SigmaPrism/SigmaIntrinsicTopology";

        [SerializeField, Range(0, 12)] private int singularShift =
            SigmaIntrinsicTopology.DefaultSingularShift;
        [SerializeField, Range(0, 12)] private int associatorShift =
            SigmaIntrinsicTopology.DefaultAssociatorShift;

        private readonly List<SigmaCarrierReadBatch> _readBatches = new();
        private readonly List<TopologySegmentCache> _segmentCaches = new();
        private readonly Dictionary<SigmaCarrierPageCoordinate,
            SigmaCarrierPageHandle> _latestTopology = new();

        private SigmaCarrier _carrier;
        private SigmaExactBackendGate _backendGate;
        private ComputeShader _shader;
        private GraphicsBuffer _transitionTau;
        private GraphicsBuffer _transitionScale;
        private GraphicsBuffer _transitionMeta;
        private GraphicsBuffer _sampleSupport;
        private GraphicsBuffer _topologyCandidates;
        private GraphicsBuffer _topologyCandidateCount;
        private GraphicsBuffer _topologyDispatchArguments;
        private GraphicsBuffer _counters;
        private GraphicsBuffer _dummyEvidence;
        private GraphicsBuffer _liveTopologyNeighbours;
        private bool _initialized;

        private int _clearCacheKernel;
        private int _clearPageKernel;
        private int _copyPriorKernel;
        private int _clearWorkKernel;
        private int _sampleSupportKernel;
        private int _buildMetaKernel;
        private int _buildTauKernel;
        private int _markCandidatesKernel;
        private int _buildDispatchKernel;
        private int _scanKernel;
        private int _accumulateEvidenceKernel;
        private int _associatorKernel;
        private int _finalizeKernel;
        private int _cellCutsKernel;
        private int _publishKernel;
        private int _resolveLiveNeighboursKernel;

        public string ModuleName => "Sigma intrinsic singular topology";
        public bool IsInitialized => _initialized;
        public int PublishedPageCount => _latestTopology.Count;
        public GraphicsBuffer DiagnosticCounters => _counters;
        internal int SingularShift => singularShift;
        internal int AssociatorShift => associatorShift;

        public void OnModuleInitialize(RoomScanner scanner)
        {
            if (_initialized)
                return;
            if (scanner == null)
                throw new ArgumentNullException(nameof(scanner));
            _carrier = scanner.Carrier ?? GetComponent<SigmaCarrier>();
            _backendGate = scanner.ExactBackendGate ??
                throw new InvalidOperationException(
                    "Intrinsic topology requires the exact backend gate.");
            _shader = Resources.Load<ComputeShader>(TopologyResource);
            if (_carrier == null || _shader == null)
                throw new InvalidOperationException(
                    "Sigma intrinsic-topology resources are incomplete.");

            _clearCacheKernel = _shader.FindKernel("ClearTopologyCache");
            _clearPageKernel = _shader.FindKernel("ClearTopologyPage");
            _copyPriorKernel = _shader.FindKernel("CopyPriorTopologyPage");
            _clearWorkKernel = _shader.FindKernel("ClearTopologyWork");
            _sampleSupportKernel = _shader.FindKernel("BuildSampleSupport");
            _buildMetaKernel = _shader.FindKernel("BuildTransitionMeta");
            _buildTauKernel = _shader.FindKernel("BuildTransitionTau");
            _markCandidatesKernel = _shader.FindKernel(
                "MarkTopologyCandidates");
            _buildDispatchKernel = _shader.FindKernel(
                "BuildTopologyDispatchArgs");
            _scanKernel = _shader.FindKernel("ScanAnnihilatorCatalog");
            _accumulateEvidenceKernel = _shader.FindKernel(
                "AccumulateTopologyEvidence");
            _associatorKernel = _shader.FindKernel("EvaluateAssociatorCells");
            _finalizeKernel = _shader.FindKernel("FinalizeTransitionClasses");
            _cellCutsKernel = _shader.FindKernel("BuildCellCutFlags");
            _publishKernel = _shader.FindKernel("PublishTopologyPage");
            _resolveLiveNeighboursKernel = _shader.FindKernel(
                "ResolveLiveTopologyNeighbours");

            _transitionTau = CreateBuffer(
                checked(TransitionsPerPage * SigmaCarrier.LanesPerSample),
                sizeof(uint) * 2, "Sigma transition tau scratch");
            _transitionScale = CreateBuffer(TransitionsPerPage,
                sizeof(uint) * 4, "Sigma transition scale scratch");
            _transitionMeta = CreateBuffer(TransitionsPerPage,
                sizeof(uint) * 4, "Sigma transition evidence scratch");
            _sampleSupport = CreateBuffer(SigmaCarrier.SamplesPerPage,
                sizeof(uint), "Sigma exact sample support scratch");
            _topologyCandidates = CreateBuffer(TopologyCandidateCapacity,
                sizeof(uint),
                "Sigma active topology transitions");
            _topologyCandidateCount = CreateBuffer(1, sizeof(uint),
                "Sigma active topology count");
            _topologyDispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 6, sizeof(uint))
            {
                name = "Sigma topology indirect dispatch"
            };
            _counters = CreateBuffer(8, sizeof(uint),
                "Sigma topology diagnostic counters");
            _dummyEvidence = CreateBuffer(1, sizeof(uint),
                "Sigma topology empty evidence");
            _liveTopologyNeighbours = CreateBuffer(
                SigmaCarrier.MaximumPagesPerSegment, sizeof(uint) * 4,
                "Sigma intrinsic carrier neighbour work");
            _counters.SetData(new uint[8]);
            _dummyEvidence.SetData(new uint[1]);
            _topologyCandidateCount.SetData(new uint[1]);
            _topologyDispatchArguments.SetData(new uint[]
                { 0u, 1u, 1u, 0u, 1u, 1u });
            _initialized = true;
        }

        public void OnScanStarted() { }
        public void OnScanStopped() { }

        public void EnsureSegmentViews()
        {
            RequireInitialized();
            _carrier.CollectReadableSegments(_readBatches);
            for (int index = 0; index < _readBatches.Count; ++index)
            {
                SigmaCarrierReadBatch batch = _readBatches[index];
                if (index < _segmentCaches.Count &&
                    _segmentCaches[index].Matches(batch))
                    continue;
                if (index < _segmentCaches.Count)
                {
                    _segmentCaches[index].Dispose();
                    _segmentCaches[index] = CreateSegmentCache(batch);
                }
                else
                    _segmentCaches.Add(CreateSegmentCache(batch));
            }
            for (int index = _segmentCaches.Count - 1;
                index >= _readBatches.Count; --index)
            {
                _segmentCaches[index].Dispose();
                _segmentCaches.RemoveAt(index);
            }
        }

        public bool TryGetSegmentView(int segmentIndex,
            out SigmaTopologySegmentView view)
        {
            EnsureSegmentViews();
            if ((uint)segmentIndex >= (uint)_segmentCaches.Count)
            {
                view = default;
                return false;
            }
            view = _segmentCaches[segmentIndex].View;
            return true;
        }

        /// <summary>
        /// Records exact intrinsic topology for the compact proof-committed
        /// inverse work list. The bounded CPU loop records commands only; target
        /// identity, validity and generation are consumed from GPU work/carrier
        /// buffers. No page decision or topology value crosses to the CPU.
        /// </summary>
        internal void RecordGpuInverseTopology(CommandBuffer command,
            SigmaCarrierReadBatch batch, GraphicsBuffer inverseWork,
            GraphicsBuffer inverseWorkControl, GraphicsBuffer proposalStatus,
            GraphicsBuffer proposalEpoch, int workCapacity, uint frameSerial,
            uint leftIndependenceKey, uint rightIndependenceKey)
        {
            RequireInitialized();
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (inverseWork == null || inverseWorkControl == null ||
                proposalStatus == null || proposalEpoch == null)
                throw new ArgumentNullException(nameof(inverseWork));
            EnsureSegmentViews();
            if ((uint)batch.SegmentIndex >= (uint)_segmentCaches.Count)
                throw new InvalidOperationException(
                    "GPU topology target segment cache is unavailable.");
            TopologySegmentCache cache = _segmentCaches[batch.SegmentIndex];

            command.SetComputeIntParam(_shader, "_TargetPageCapacity",
                batch.PageCapacity);
            command.SetComputeBufferParam(_shader,
                _resolveLiveNeighboursKernel, "_LiveInverseWork", inverseWork);
            command.SetComputeBufferParam(_shader,
                _resolveLiveNeighboursKernel, "_LiveInverseWorkControl",
                inverseWorkControl);
            command.SetComputeBufferParam(_shader,
                _resolveLiveNeighboursKernel, "_LivePageMetadata",
                batch.Metadata);
            command.SetComputeBufferParam(_shader,
                _resolveLiveNeighboursKernel, "_LiveCurrentFlags",
                batch.CurrentFlags);
            command.SetComputeBufferParam(_shader,
                _resolveLiveNeighboursKernel, "_LiveTopologyNeighbours",
                _liveTopologyNeighbours);
            command.DispatchCompute(_shader, _resolveLiveNeighboursKernel,
                workCapacity, 1, 1);

            for (int workIndex = 0; workIndex < workCapacity; ++workIndex)
            {
                BindGpuLiveCommon(command, batch, cache, inverseWork,
                    inverseWorkControl, proposalStatus, proposalEpoch,
                    workIndex, frameSerial, leftIndependenceKey,
                    rightIndependenceKey);

                command.SetComputeBufferParam(_shader, _clearPageKernel,
                    "_TransitionCache", cache.TransitionRecords);
                command.SetComputeBufferParam(_shader, _clearPageKernel,
                    "_AssociatorFlags", cache.AssociatorFlags);
                command.SetComputeBufferParam(_shader, _clearPageKernel,
                    "_CellTopologyFlags", cache.CellFlags);
                command.SetComputeBufferParam(_shader, _clearPageKernel,
                    "_TopologyPageKeys", cache.PageKeys);
                command.DispatchCompute(_shader, _clearPageKernel,
                    TransitionsPerPage / 64, 1, 1);

                command.SetComputeBufferParam(_shader, _clearWorkKernel,
                    "_TopologyCandidateCount", _topologyCandidateCount);
                command.SetComputeBufferParam(_shader, _clearWorkKernel,
                    "_TopologyDispatchArgs", _topologyDispatchArguments);
                command.DispatchCompute(_shader, _clearWorkKernel, 1, 1, 1);

                command.SetComputeBufferParam(_shader, _sampleSupportKernel,
                    "_TargetState", batch.State);
                command.SetComputeBufferParam(_shader, _sampleSupportKernel,
                    "_SampleSupport", _sampleSupport);
                command.DispatchCompute(_shader, _sampleSupportKernel,
                    SigmaCarrier.SamplesPerPage / 64, 1, 1);

                command.SetComputeBufferParam(_shader, _buildMetaKernel,
                    "_TargetState", batch.State);
                command.SetComputeBufferParam(_shader, _buildMetaKernel,
                    "_RightState", batch.State);
                command.SetComputeBufferParam(_shader, _buildMetaKernel,
                    "_DownState", batch.State);
                command.SetComputeBufferParam(_shader, _buildMetaKernel,
                    "_TransitionMeta", _transitionMeta);
                command.SetComputeBufferParam(_shader, _buildMetaKernel,
                    "_SampleSupport", _sampleSupport);
                command.SetComputeBufferParam(_shader, _buildMetaKernel,
                    "_TransitionCache", cache.TransitionRecords);
                BindGpuEvidence(command, _buildMetaKernel, proposalStatus,
                    proposalEpoch);
                command.DispatchCompute(_shader, _buildMetaKernel,
                    TransitionsPerPage / 64, 1, 1);

                command.SetComputeBufferParam(_shader, _markCandidatesKernel,
                    "_TransitionMeta", _transitionMeta);
                command.SetComputeBufferParam(_shader, _markCandidatesKernel,
                    "_TopologyCandidates", _topologyCandidates);
                command.SetComputeBufferParam(_shader, _markCandidatesKernel,
                    "_TopologyCandidateCount", _topologyCandidateCount);
                command.SetComputeBufferParam(_shader, _markCandidatesKernel,
                    "_TopologyCounters", _counters);
                command.DispatchCompute(_shader, _markCandidatesKernel,
                    1, 1, 1);

                command.SetComputeBufferParam(_shader, _buildDispatchKernel,
                    "_TopologyCandidateCount", _topologyCandidateCount);
                command.SetComputeBufferParam(_shader, _buildDispatchKernel,
                    "_TopologyDispatchArgs", _topologyDispatchArguments);
                command.DispatchCompute(_shader, _buildDispatchKernel, 1, 1, 1);

                command.SetComputeBufferParam(_shader, _buildTauKernel,
                    "_TargetState", batch.State);
                command.SetComputeBufferParam(_shader, _buildTauKernel,
                    "_RightState", batch.State);
                command.SetComputeBufferParam(_shader, _buildTauKernel,
                    "_DownState", batch.State);
                command.SetComputeBufferParam(_shader, _buildTauKernel,
                    "_TransitionTau", _transitionTau);
                command.SetComputeBufferParam(_shader, _buildTauKernel,
                    "_TransitionScale", _transitionScale);
                command.SetComputeBufferParam(_shader, _buildTauKernel,
                    "_TransitionMeta", _transitionMeta);
                command.SetComputeBufferParam(_shader, _buildTauKernel,
                    "_TopologyCandidates", _topologyCandidates);
                command.SetComputeBufferParam(_shader, _buildTauKernel,
                    "_TopologyCandidateCount", _topologyCandidateCount);
                command.DispatchCompute(_shader, _buildTauKernel,
                    _topologyDispatchArguments, CandidateDispatchOffset);

                command.SetComputeBufferParam(_shader, _scanKernel,
                    "_TransitionTau", _transitionTau);
                command.SetComputeBufferParam(_shader, _scanKernel,
                    "_TransitionScale", _transitionScale);
                command.SetComputeBufferParam(_shader, _scanKernel,
                    "_TransitionMeta", _transitionMeta);
                command.SetComputeBufferParam(_shader, _scanKernel,
                    "_TransitionCache", cache.TransitionRecords);
                command.SetComputeBufferParam(_shader, _scanKernel,
                    "_TopologyCounters", _counters);
                command.SetComputeBufferParam(_shader, _scanKernel,
                    "_TopologyCandidates", _topologyCandidates);
                command.SetComputeBufferParam(_shader, _scanKernel,
                    "_TopologyCandidateCount", _topologyCandidateCount);
                command.DispatchCompute(_shader, _scanKernel,
                    _topologyDispatchArguments, WitnessDispatchOffset);

                BindLiveAssociator(command, batch, cache);
                command.DispatchCompute(_shader, _associatorKernel,
                    _topologyDispatchArguments, CandidateDispatchOffset);

                command.SetComputeBufferParam(_shader, _finalizeKernel,
                    "_TransitionCache", cache.TransitionRecords);
                command.SetComputeBufferParam(_shader, _finalizeKernel,
                    "_TransitionMeta", _transitionMeta);
                command.SetComputeBufferParam(_shader, _finalizeKernel,
                    "_AssociatorFlags", cache.AssociatorFlags);
                command.SetComputeBufferParam(_shader, _finalizeKernel,
                    "_TopologyCounters", _counters);
                command.DispatchCompute(_shader, _finalizeKernel,
                    TransitionsPerPage / 64, 1, 1);

                command.SetComputeBufferParam(_shader, _cellCutsKernel,
                    "_TransitionCache", cache.TransitionRecords);
                command.SetComputeBufferParam(_shader, _cellCutsKernel,
                    "_RightTransitionCache", cache.TransitionRecords);
                command.SetComputeBufferParam(_shader, _cellCutsKernel,
                    "_DownTransitionCache", cache.TransitionRecords);
                command.SetComputeBufferParam(_shader, _cellCutsKernel,
                    "_CellTopologyFlags", cache.CellFlags);
                command.DispatchCompute(_shader, _cellCutsKernel,
                    SigmaCarrier.SamplesPerPage / 64, 1, 1);

                command.SetComputeBufferParam(_shader, _publishKernel,
                    "_TopologyPageKeys", cache.PageKeys);
                command.DispatchCompute(_shader, _publishKernel, 1, 1, 1);
            }
        }

        private void BindGpuLiveCommon(CommandBuffer command,
            SigmaCarrierReadBatch batch, TopologySegmentCache cache,
            GraphicsBuffer inverseWork, GraphicsBuffer inverseWorkControl,
            GraphicsBuffer proposalStatus, GraphicsBuffer proposalEpoch,
            int workIndex, uint frameSerial, uint leftIndependenceKey,
            uint rightIndependenceKey)
        {
            command.SetComputeIntParam(_shader, "_UseInverseWorkList", 1);
            command.SetComputeIntParam(_shader, "_LiveWorkIndex", workIndex);
            command.SetComputeIntParam(_shader, "_TargetPageCapacity",
                batch.PageCapacity);
            command.SetComputeIntParam(_shader, "_RightPageCapacity",
                batch.PageCapacity);
            command.SetComputeIntParam(_shader, "_DownPageCapacity",
                batch.PageCapacity);
            command.SetComputeIntParam(_shader, "_RightPageValid", 0);
            command.SetComputeIntParam(_shader, "_DownPageValid", 0);
            command.SetComputeIntParam(_shader, "_TargetEvidenceMode",
                SigmaTopologyEvidenceView.ProposalMode);
            command.SetComputeIntParam(_shader, "_RightEvidenceMode", 0);
            command.SetComputeIntParam(_shader, "_DownEvidenceMode", 0);
            command.SetComputeIntParam(_shader, "_TargetEvidencePageCapacity",
                batch.PageCapacity);
            command.SetComputeIntParam(_shader, "_RightEvidencePageCapacity", 1);
            command.SetComputeIntParam(_shader, "_DownEvidencePageCapacity", 1);
            command.SetComputeIntParam(_shader, "_EvidenceFrameSerial",
                unchecked((int)frameSerial));
            command.SetComputeIntParam(_shader, "_LeftIndependenceKey",
                unchecked((int)leftIndependenceKey));
            command.SetComputeIntParam(_shader, "_RightIndependenceKey",
                unchecked((int)rightIndependenceKey));
            command.SetComputeIntParam(_shader, "_ForceBoundaryTransitions", 0);
            command.SetComputeIntParam(_shader, "_SingularShift", singularShift);
            command.SetComputeIntParam(_shader, "_AssociatorShift",
                associatorShift);
            command.SetComputeIntParam(_shader, "_TopologyCandidateCapacity",
                TopologyCandidateCapacity);
            int[] kernels = { _clearPageKernel, _sampleSupportKernel,
                _buildMetaKernel, _associatorKernel, _markCandidatesKernel,
                _buildTauKernel, _scanKernel, _finalizeKernel,
                _cellCutsKernel, _publishKernel };
            for (int index = 0; index < kernels.Length; ++index)
            {
                int kernel = kernels[index];
                command.SetComputeBufferParam(_shader, kernel,
                    "_SigmaExactBackendGate", _backendGate.Buffer);
                command.SetComputeBufferParam(_shader, kernel,
                    "_LiveInverseWork", inverseWork);
                command.SetComputeBufferParam(_shader, kernel,
                    "_LiveInverseWorkControl", inverseWorkControl);
                command.SetComputeBufferParam(_shader, kernel,
                    "_LivePageMetadata", batch.Metadata);
                command.SetComputeBufferParam(_shader, kernel,
                    "_LiveCurrentFlags", batch.CurrentFlags);
                command.SetComputeBufferParam(_shader, kernel,
                    "_LiveTopologyNeighbours", _liveTopologyNeighbours);
            }
        }

        private void BindGpuEvidence(CommandBuffer command, int kernel,
            GraphicsBuffer proposalStatus, GraphicsBuffer proposalEpoch)
        {
            command.SetComputeBufferParam(_shader, kernel,
                "_TargetEvidenceProposalStatus", proposalStatus);
            command.SetComputeBufferParam(_shader, kernel,
                "_TargetEvidenceEpoch", proposalEpoch);
            command.SetComputeBufferParam(_shader, kernel,
                "_RightEvidenceProposalStatus", _dummyEvidence);
            command.SetComputeBufferParam(_shader, kernel,
                "_RightEvidenceEpoch", _dummyEvidence);
            command.SetComputeBufferParam(_shader, kernel,
                "_DownEvidenceProposalStatus", _dummyEvidence);
            command.SetComputeBufferParam(_shader, kernel,
                "_DownEvidenceEpoch", _dummyEvidence);
        }

        private void BindLiveAssociator(CommandBuffer command,
            SigmaCarrierReadBatch batch, TopologySegmentCache cache)
        {
            command.SetComputeBufferParam(_shader, _associatorKernel,
                "_TargetState", batch.State);
            command.SetComputeBufferParam(_shader, _associatorKernel,
                "_RightState", batch.State);
            command.SetComputeBufferParam(_shader, _associatorKernel,
                "_DownState", batch.State);
            command.SetComputeBufferParam(_shader, _associatorKernel,
                "_TransitionMeta", _transitionMeta);
            command.SetComputeBufferParam(_shader, _associatorKernel,
                "_TransitionCache", cache.TransitionRecords);
            command.SetComputeBufferParam(_shader, _associatorKernel,
                "_AssociatorFlags", cache.AssociatorFlags);
            command.SetComputeBufferParam(_shader, _associatorKernel,
                "_TopologyCounters", _counters);
            command.SetComputeBufferParam(_shader, _associatorKernel,
                "_TopologyCandidates", _topologyCandidates);
            command.SetComputeBufferParam(_shader, _associatorKernel,
                "_TopologyCandidateCount", _topologyCandidateCount);
        }

        internal SigmaTopologyBuildToken BuildGeneration(
            SigmaCarrierPageHandle target, SigmaCarrierPageHandle prior,
            SigmaTopologyEvidenceView targetEvidence,
            SigmaTopologyEvidenceView rightEvidence,
            SigmaTopologyEvidenceView downEvidence)
        {
            RequireInitialized();
            if (!target.IsValid)
                throw new ArgumentException("Target generation is invalid.",
                    nameof(target));
            EnsureSegmentViews();
            TopologySegmentCache targetCache =
                _segmentCaches[target.SegmentIndex];
            if (prior.IsValid && TryGetCurrentTopology(prior,
                    out TopologySegmentCache priorCache))
                CopyPrior(priorCache, prior.PageSlot, targetCache,
                    target.PageSlot);
            else
                ClearPage(targetCache, target.PageSlot);

            BuildPage(target, targetCache, targetEvidence, rightEvidence,
                downEvidence, false);
            return new SigmaTopologyBuildToken(this, target);
        }

        internal SigmaTopologyGaugeBinding PrepareGaugeGeneration(
            SigmaCarrierPageHandle target, SigmaCarrierPageHandle prior)
        {
            RequireInitialized();
            if (!target.IsValid || !prior.IsValid ||
                !target.Coordinate.Equals(prior.Coordinate))
                throw new ArgumentException(
                    "Gauge topology requires two generations of one carrier page.");
            EnsureSegmentViews();
            if (!TryGetCurrentTopology(prior,
                    out TopologySegmentCache sourceCache))
                throw new InvalidOperationException(
                    "Gauge source topology generation is unavailable.");
            TopologySegmentCache targetCache =
                _segmentCaches[target.SegmentIndex];
            CopyPrior(sourceCache, prior.PageSlot, targetCache, target.PageSlot);
            return new SigmaTopologyGaugeBinding(sourceCache.TransitionRecords,
                prior.PageSlot, sourceCache.Capacity,
                targetCache.TransitionRecords, target.PageSlot,
                targetCache.Capacity);
        }

        internal SigmaTopologyBuildToken FinishGaugeGeneration(
            SigmaCarrierPageHandle target, SigmaTopologyEvidenceView evidence)
        {
            RequireInitialized();
            EnsureSegmentViews();
            BuildPage(target, _segmentCaches[target.SegmentIndex], evidence,
                default, default, false);
            return new SigmaTopologyBuildToken(this, target);
        }

        internal void RebuildCurrent(SigmaCarrierPageHandle page,
            SigmaTopologyEvidenceView targetEvidence,
            SigmaTopologyEvidenceView rightEvidence,
            SigmaTopologyEvidenceView downEvidence,
            bool forceBoundaryTransitions)
        {
            RequireInitialized();
            EnsureSegmentViews();
            TopologySegmentCache cache = _segmentCaches[page.SegmentIndex];
            bool current = TryGetCurrentTopology(page, out _);
            if (!current)
                ClearPage(cache, page.PageSlot);
            if (current && !forceBoundaryTransitions)
                AccumulateEvidencePage(page, cache, targetEvidence,
                    rightEvidence, downEvidence);
            else
                BuildPage(page, cache, targetEvidence, rightEvidence, downEvidence,
                    forceBoundaryTransitions);
            PublishBuiltGeneration(page);
        }

        private void AccumulateEvidencePage(SigmaCarrierPageHandle target,
            TopologySegmentCache targetCache,
            SigmaTopologyEvidenceView targetEvidence,
            SigmaTopologyEvidenceView rightEvidence,
            SigmaTopologyEvidenceView downEvidence)
        {
            BindDirectExecutionMode();
            SigmaCarrierReadBatch targetBatch = _readBatches[target.SegmentIndex];
            ResolveNeighbour(target.Coordinate, 1L, 0L, targetBatch,
                out SigmaCarrierReadBatch rightBatch,
                out SigmaCarrierPageHandle right, out bool rightValid);
            ResolveNeighbour(target.Coordinate, 0L, 1L, targetBatch,
                out SigmaCarrierReadBatch downBatch,
                out SigmaCarrierPageHandle down, out bool downValid);
            BindPageState(targetBatch, target, rightBatch, right, rightValid,
                downBatch, down, downValid);
            ValidateEvidenceCoordinate(targetEvidence, target.Coordinate,
                nameof(targetEvidence));
            if (rightValid)
                ValidateEvidenceCoordinate(rightEvidence, right.Coordinate,
                    nameof(rightEvidence));
            else
                rightEvidence = default;
            if (downValid)
                ValidateEvidenceCoordinate(downEvidence, down.Coordinate,
                    nameof(downEvidence));
            else
                downEvidence = default;
            ResolveEvidenceIdentity(targetEvidence, rightEvidence, downEvidence,
                out uint frameSerial, out uint leftIndependenceKey,
                out uint rightIndependenceKey);
            BindEvidence(_accumulateEvidenceKernel, targetEvidence,
                rightEvidence, downEvidence, frameSerial);
            SetUInt("_LeftIndependenceKey", leftIndependenceKey);
            SetUInt("_RightIndependenceKey", rightIndependenceKey);
            _shader.SetBuffer(_accumulateEvidenceKernel, "_TransitionCache",
                targetCache.TransitionRecords);
            _shader.SetBuffer(_accumulateEvidenceKernel, "_TopologyCounters",
                _counters);
            _backendGate.Bind(_shader, _accumulateEvidenceKernel);
            _shader.Dispatch(_accumulateEvidenceKernel,
                TransitionsPerPage / 64, 1, 1);

            BindTopologyNeighbours(right, rightValid, down, downValid,
                targetCache);
            _shader.SetBuffer(_cellCutsKernel, "_TransitionCache",
                targetCache.TransitionRecords);
            _shader.SetBuffer(_cellCutsKernel, "_CellTopologyFlags",
                targetCache.CellFlags);
            _backendGate.Bind(_shader, _cellCutsKernel);
            _shader.Dispatch(_cellCutsKernel,
                SigmaCarrier.SamplesPerPage / 64, 1, 1);
            SetUInt("_TargetGeneration", target.Generation);
            SetUInt("_TargetRevision", target.Revision);
            _shader.SetBuffer(_publishKernel, "_TopologyPageKeys",
                targetCache.PageKeys);
            _backendGate.Bind(_shader, _publishKernel);
            _shader.Dispatch(_publishKernel, 1, 1, 1);
        }

        internal void PublishBuiltGeneration(SigmaCarrierPageHandle handle)
        {
            if (!handle.IsValid)
                throw new ArgumentException("Topology handle is invalid.",
                    nameof(handle));
            _latestTopology[handle.Coordinate] = handle;
        }

        private void BuildPage(SigmaCarrierPageHandle target,
            TopologySegmentCache targetCache,
            SigmaTopologyEvidenceView targetEvidence,
            SigmaTopologyEvidenceView rightEvidence,
            SigmaTopologyEvidenceView downEvidence,
            bool forceBoundaryTransitions)
        {
            BindDirectExecutionMode();
            SigmaCarrierReadBatch targetBatch = _readBatches[target.SegmentIndex];
            ResolveNeighbour(target.Coordinate, 1L, 0L, targetBatch,
                out SigmaCarrierReadBatch rightBatch,
                out SigmaCarrierPageHandle right, out bool rightValid);
            ResolveNeighbour(target.Coordinate, 0L, 1L, targetBatch,
                out SigmaCarrierReadBatch downBatch,
                out SigmaCarrierPageHandle down, out bool downValid);

            BindPageState(targetBatch, target, rightBatch, right, rightValid,
                downBatch, down, downValid);
            ValidateEvidenceCoordinate(targetEvidence, target.Coordinate,
                nameof(targetEvidence));
            if (rightValid)
                ValidateEvidenceCoordinate(rightEvidence, right.Coordinate,
                    nameof(rightEvidence));
            else
                rightEvidence = default;
            if (downValid)
                ValidateEvidenceCoordinate(downEvidence, down.Coordinate,
                    nameof(downEvidence));
            else
                downEvidence = default;
            ResolveEvidenceIdentity(targetEvidence, rightEvidence, downEvidence,
                out uint frameSerial, out uint leftIndependenceKey,
                out uint rightIndependenceKey);
            BindEvidence(_buildMetaKernel, targetEvidence, rightEvidence,
                downEvidence, frameSerial);
            SetUInt("_LeftIndependenceKey", leftIndependenceKey);
            SetUInt("_RightIndependenceKey", rightIndependenceKey);
            _shader.SetInt("_ForceBoundaryTransitions",
                forceBoundaryTransitions ? 1 : 0);
            _shader.SetBuffer(_clearWorkKernel, "_TopologyCandidateCount",
                _topologyCandidateCount);
            _shader.SetBuffer(_clearWorkKernel, "_TopologyDispatchArgs",
                _topologyDispatchArguments);
            _shader.Dispatch(_clearWorkKernel, 1, 1, 1);
            _shader.SetBuffer(_sampleSupportKernel, "_TargetState",
                targetBatch.State);
            _shader.SetBuffer(_sampleSupportKernel, "_SampleSupport",
                _sampleSupport);
            _backendGate.Bind(_shader, _sampleSupportKernel);
            _shader.Dispatch(_sampleSupportKernel,
                SigmaCarrier.SamplesPerPage / 64, 1, 1);
            _shader.SetBuffer(_buildMetaKernel, "_TransitionMeta",
                _transitionMeta);
            _shader.SetBuffer(_buildMetaKernel, "_SampleSupport",
                _sampleSupport);
            _shader.SetBuffer(_buildMetaKernel, "_TransitionCache",
                targetCache.TransitionRecords);
            _backendGate.Bind(_shader, _buildMetaKernel);
            _shader.Dispatch(_buildMetaKernel, TransitionsPerPage / 64, 1, 1);

            _shader.SetInt("_SingularShift", singularShift);
            _shader.SetInt("_AssociatorShift", associatorShift);
            _shader.SetInt("_TopologyCandidateCapacity",
                TopologyCandidateCapacity);
            _shader.SetBuffer(_markCandidatesKernel, "_TransitionMeta",
                _transitionMeta);
            _shader.SetBuffer(_markCandidatesKernel, "_TopologyCandidates",
                _topologyCandidates);
            _shader.SetBuffer(_markCandidatesKernel, "_TopologyCandidateCount",
                _topologyCandidateCount);
            _shader.SetBuffer(_markCandidatesKernel, "_TopologyCounters",
                _counters);
            _backendGate.Bind(_shader, _markCandidatesKernel);
            _shader.Dispatch(_markCandidatesKernel,
                1, 1, 1);
            _shader.SetBuffer(_buildDispatchKernel, "_TopologyCandidateCount",
                _topologyCandidateCount);
            _shader.SetBuffer(_buildDispatchKernel, "_TopologyDispatchArgs",
                _topologyDispatchArguments);
            _shader.Dispatch(_buildDispatchKernel, 1, 1, 1);

            // Every transition runs the cheap activity gate, but only compacted
            // constraint-active lanes enter the expensive exact dense transition
            // product and complete 168-witness scan.
            _shader.SetBuffer(_buildTauKernel, "_TransitionTau", _transitionTau);
            _shader.SetBuffer(_buildTauKernel, "_TransitionScale",
                _transitionScale);
            _shader.SetBuffer(_buildTauKernel, "_TransitionMeta",
                _transitionMeta);
            _shader.SetBuffer(_buildTauKernel, "_TopologyCandidates",
                _topologyCandidates);
            _shader.SetBuffer(_buildTauKernel, "_TopologyCandidateCount",
                _topologyCandidateCount);
            _backendGate.Bind(_shader, _buildTauKernel);
            _shader.DispatchIndirect(_buildTauKernel,
                _topologyDispatchArguments, CandidateDispatchOffset);

            _shader.SetBuffer(_scanKernel, "_TransitionTau", _transitionTau);
            _shader.SetBuffer(_scanKernel, "_TransitionScale", _transitionScale);
            _shader.SetBuffer(_scanKernel, "_TransitionMeta", _transitionMeta);
            _shader.SetBuffer(_scanKernel, "_TransitionCache",
                targetCache.TransitionRecords);
            _shader.SetBuffer(_scanKernel, "_TopologyCounters", _counters);
            _shader.SetBuffer(_scanKernel, "_TopologyCandidates",
                _topologyCandidates);
            _shader.SetBuffer(_scanKernel, "_TopologyCandidateCount",
                _topologyCandidateCount);
            _shader.DispatchIndirect(_scanKernel, _topologyDispatchArguments,
                WitnessDispatchOffset);

            BindDirectAssociator(targetCache);
            _shader.DispatchIndirect(_associatorKernel,
                _topologyDispatchArguments, CandidateDispatchOffset);

            _shader.SetBuffer(_finalizeKernel, "_TransitionCache",
                targetCache.TransitionRecords);
            _shader.SetBuffer(_finalizeKernel, "_TransitionMeta",
                _transitionMeta);
            _shader.SetBuffer(_finalizeKernel, "_AssociatorFlags",
                targetCache.AssociatorFlags);
            _shader.SetBuffer(_finalizeKernel, "_TopologyCounters", _counters);
            _backendGate.Bind(_shader, _finalizeKernel);
            _shader.Dispatch(_finalizeKernel, TransitionsPerPage / 64, 1, 1);

            BindTopologyNeighbours(right, rightValid, down, downValid,
                targetCache);
            _shader.SetBuffer(_cellCutsKernel, "_TransitionCache",
                targetCache.TransitionRecords);
            _shader.SetBuffer(_cellCutsKernel, "_CellTopologyFlags",
                targetCache.CellFlags);
            _backendGate.Bind(_shader, _cellCutsKernel);
            _shader.Dispatch(_cellCutsKernel,
                SigmaCarrier.SamplesPerPage / 64, 1, 1);

            SetUInt("_TargetGeneration", target.Generation);
            SetUInt("_TargetRevision", target.Revision);
            _shader.SetBuffer(_publishKernel, "_TopologyPageKeys",
                targetCache.PageKeys);
            _backendGate.Bind(_shader, _publishKernel);
            _shader.Dispatch(_publishKernel, 1, 1, 1);
        }

        private void BindDirectAssociator(TopologySegmentCache targetCache)
        {
            _shader.SetBuffer(_associatorKernel, "_AssociatorFlags",
                targetCache.AssociatorFlags);
            _shader.SetBuffer(_associatorKernel, "_TransitionMeta",
                _transitionMeta);
            _shader.SetBuffer(_associatorKernel, "_TransitionCache",
                targetCache.TransitionRecords);
            _shader.SetBuffer(_associatorKernel, "_TopologyCounters", _counters);
            _shader.SetBuffer(_associatorKernel, "_TopologyCandidates",
                _topologyCandidates);
            _shader.SetBuffer(_associatorKernel, "_TopologyCandidateCount",
                _topologyCandidateCount);
            _backendGate.Bind(_shader, _associatorKernel);
        }

        private static void ValidateEvidenceCoordinate(
            SigmaTopologyEvidenceView evidence,
            SigmaCarrierPageCoordinate coordinate, string parameterName)
        {
            if (evidence.IsValid && !evidence.Coordinate.Equals(coordinate))
                throw new ArgumentException(
                    "Topology evidence belongs to another logical carrier page.",
                    parameterName);
        }

        private static void ResolveEvidenceIdentity(
            SigmaTopologyEvidenceView target,
            SigmaTopologyEvidenceView right,
            SigmaTopologyEvidenceView down,
            out uint frameSerial, out uint leftIndependenceKey,
            out uint rightIndependenceKey)
        {
            frameSerial = 0u;
            leftIndependenceKey = 0u;
            rightIndependenceKey = 0u;
            MergeEvidenceIdentity(target, ref frameSerial,
                ref leftIndependenceKey, ref rightIndependenceKey);
            MergeEvidenceIdentity(right, ref frameSerial,
                ref leftIndependenceKey, ref rightIndependenceKey);
            MergeEvidenceIdentity(down, ref frameSerial,
                ref leftIndependenceKey, ref rightIndependenceKey);
        }

        private static void MergeEvidenceIdentity(
            SigmaTopologyEvidenceView evidence, ref uint frameSerial,
            ref uint leftIndependenceKey, ref uint rightIndependenceKey)
        {
            if (!evidence.IsValid)
                return;
            if (frameSerial == 0u)
                frameSerial = evidence.FrameSerial;
            else if (evidence.FrameSerial != frameSerial)
                throw new InvalidOperationException(
                    "Topology evidence from different frame epochs cannot be fused.");
            if (leftIndependenceKey == 0u)
                leftIndependenceKey = evidence.LeftIndependenceKey;
            else if (evidence.LeftIndependenceKey != 0u &&
                     evidence.LeftIndependenceKey != leftIndependenceKey)
                throw new InvalidOperationException(
                    "Topology left-eye evidence key mismatch.");
            if (rightIndependenceKey == 0u)
                rightIndependenceKey = evidence.RightIndependenceKey;
            else if (evidence.RightIndependenceKey != 0u &&
                     evidence.RightIndependenceKey != rightIndependenceKey)
                throw new InvalidOperationException(
                    "Topology right-eye evidence key mismatch.");
        }

        private void BindEvidence(int kernel, SigmaTopologyEvidenceView target,
            SigmaTopologyEvidenceView right, SigmaTopologyEvidenceView down,
            uint frameSerial)
        {
            SetUInt("_EvidenceFrameSerial", frameSerial);
            BindEvidenceView(kernel, "Target", target);
            BindEvidenceView(kernel, "Right", right);
            BindEvidenceView(kernel, "Down", down);
        }

        private void BindEvidenceView(int kernel, string prefix,
            SigmaTopologyEvidenceView evidence)
        {
            _shader.SetInt($"_{prefix}EvidenceMode", evidence.Mode);
            _shader.SetInt($"_{prefix}EvidencePageSlot",
                evidence.IsValid ? evidence.PageSlot : 0);
            _shader.SetInt($"_{prefix}EvidencePageCapacity",
                evidence.IsValid ? Math.Max(1, evidence.PageCapacity) : 1);
            _shader.SetBuffer(kernel,
                $"_{prefix}EvidenceProposalStatus",
                evidence.ProposalStatus ?? _dummyEvidence);
            _shader.SetBuffer(kernel, $"_{prefix}EvidenceEpoch",
                evidence.ProposalEpoch ?? _dummyEvidence);
        }

        private void BindPageState(SigmaCarrierReadBatch targetBatch,
            SigmaCarrierPageHandle target, SigmaCarrierReadBatch rightBatch,
            SigmaCarrierPageHandle right, bool rightValid,
            SigmaCarrierReadBatch downBatch, SigmaCarrierPageHandle down,
            bool downValid)
        {
            _shader.SetInt("_TargetPageSlot", target.PageSlot);
            _shader.SetInt("_TargetPageCapacity", targetBatch.PageCapacity);
            _shader.SetInt("_RightPageSlot", rightValid ? right.PageSlot : 0);
            _shader.SetInt("_RightPageCapacity", rightBatch.PageCapacity);
            _shader.SetInt("_RightPageValid", rightValid ? 1 : 0);
            _shader.SetInt("_DownPageSlot", downValid ? down.PageSlot : 0);
            _shader.SetInt("_DownPageCapacity", downBatch.PageCapacity);
            _shader.SetInt("_DownPageValid", downValid ? 1 : 0);
            _shader.SetBuffer(_buildMetaKernel, "_TargetState", targetBatch.State);
            _shader.SetBuffer(_buildMetaKernel, "_RightState", rightBatch.State);
            _shader.SetBuffer(_buildMetaKernel, "_DownState", downBatch.State);
            _shader.SetBuffer(_buildTauKernel, "_TargetState", targetBatch.State);
            _shader.SetBuffer(_buildTauKernel, "_RightState", rightBatch.State);
            _shader.SetBuffer(_buildTauKernel, "_DownState", downBatch.State);
            _shader.SetBuffer(_associatorKernel, "_TargetState", targetBatch.State);
            _shader.SetBuffer(_associatorKernel, "_RightState", rightBatch.State);
            _shader.SetBuffer(_associatorKernel, "_DownState", downBatch.State);
        }

        private void BindTopologyNeighbours(SigmaCarrierPageHandle right,
            bool rightValid, SigmaCarrierPageHandle down, bool downValid,
            TopologySegmentCache fallback)
        {
            TopologySegmentCache rightCache = null;
            TopologySegmentCache downCache = null;
            bool rightTopology = rightValid && TryGetCurrentTopology(right,
                out rightCache);
            bool downTopology = downValid && TryGetCurrentTopology(down,
                out downCache);
            _shader.SetInt("_RightPageValid", rightTopology ? 1 : 0);
            _shader.SetInt("_DownPageValid", downTopology ? 1 : 0);
            _shader.SetInt("_RightPageSlot", rightTopology ? right.PageSlot : 0);
            _shader.SetInt("_DownPageSlot", downTopology ? down.PageSlot : 0);
            _shader.SetInt("_RightPageCapacity", rightTopology
                ? rightCache.Capacity : fallback.Capacity);
            _shader.SetInt("_DownPageCapacity", downTopology
                ? downCache.Capacity : fallback.Capacity);
            _shader.SetBuffer(_cellCutsKernel, "_RightTransitionCache",
                rightTopology ? rightCache.TransitionRecords :
                    fallback.TransitionRecords);
            _shader.SetBuffer(_cellCutsKernel, "_DownTransitionCache",
                downTopology ? downCache.TransitionRecords :
                    fallback.TransitionRecords);
        }

        private void ResolveNeighbour(SigmaCarrierPageCoordinate coordinate,
            long deltaX, long deltaY, SigmaCarrierReadBatch fallback,
            out SigmaCarrierReadBatch batch, out SigmaCarrierPageHandle handle,
            out bool valid)
        {
            handle = default;
            valid = TryOffset(coordinate, deltaX, deltaY,
                    out SigmaCarrierPageCoordinate neighbour) &&
                _carrier.TryGetLatest(neighbour, out handle);
            if (valid)
                batch = _readBatches[handle.SegmentIndex];
            else
            {
                batch = fallback;
            }
        }

        private bool TryGetCurrentTopology(SigmaCarrierPageHandle handle,
            out TopologySegmentCache cache)
        {
            if (handle.IsValid && _latestTopology.TryGetValue(handle.Coordinate,
                    out SigmaCarrierPageHandle current) && current.Equals(handle) &&
                (uint)handle.SegmentIndex < (uint)_segmentCaches.Count)
            {
                cache = _segmentCaches[handle.SegmentIndex];
                return true;
            }
            cache = null;
            return false;
        }

        private void CopyPrior(TopologySegmentCache source, int sourceSlot,
            TopologySegmentCache target, int targetSlot)
        {
            BindDirectExecutionMode();
            _shader.SetInt("_PriorPageSlot", sourceSlot);
            _shader.SetInt("_PriorPageCapacity", source.Capacity);
            _shader.SetInt("_TargetPageSlot", targetSlot);
            _shader.SetInt("_TargetPageCapacity", target.Capacity);
            _shader.SetBuffer(_copyPriorKernel, "_PriorTransitionCache",
                source.TransitionRecords);
            _shader.SetBuffer(_copyPriorKernel, "_TransitionCache",
                target.TransitionRecords);
            _shader.Dispatch(_copyPriorKernel, TransitionsPerPage / 64, 1, 1);
        }

        private void ClearPage(TopologySegmentCache cache, int pageSlot)
        {
            BindDirectExecutionMode();
            _shader.SetInt("_TargetPageSlot", pageSlot);
            _shader.SetInt("_TargetPageCapacity", cache.Capacity);
            _shader.SetBuffer(_clearPageKernel, "_TransitionCache",
                cache.TransitionRecords);
            _shader.SetBuffer(_clearPageKernel, "_AssociatorFlags",
                cache.AssociatorFlags);
            _shader.SetBuffer(_clearPageKernel, "_CellTopologyFlags",
                cache.CellFlags);
            _shader.SetBuffer(_clearPageKernel, "_TopologyPageKeys",
                cache.PageKeys);
            _shader.Dispatch(_clearPageKernel, TransitionsPerPage / 64, 1, 1);
        }

        private void BindDirectExecutionMode()
        {
            // Scalar compute parameters are shared by the shader asset. Direct
            // work can follow a live inverse dispatch, so never inherit its
            // compact work-list addressing mode.
            _shader.SetInt("_UseInverseWorkList", 0);
        }

        private TopologySegmentCache CreateSegmentCache(
            SigmaCarrierReadBatch batch)
        {
            var cache = new TopologySegmentCache(batch);
            int transitions = checked(batch.PageCapacity * TransitionsPerPage);
            int cells = checked(batch.PageCapacity * SigmaCarrier.SamplesPerPage);
            _shader.SetInt("_ClearTransitionCount", transitions);
            _shader.SetInt("_ClearCellCount", cells);
            _shader.SetInt("_ClearPageCount", batch.PageCapacity);
            _shader.SetBuffer(_clearCacheKernel, "_TransitionCache",
                cache.TransitionRecords);
            _shader.SetBuffer(_clearCacheKernel, "_AssociatorFlags",
                cache.AssociatorFlags);
            _shader.SetBuffer(_clearCacheKernel, "_CellTopologyFlags",
                cache.CellFlags);
            _shader.SetBuffer(_clearCacheKernel, "_TopologyPageKeys",
                cache.PageKeys);
            _shader.SetBuffer(_clearCacheKernel, "_TopologyCounters", _counters);
            int count = Math.Max(transitions, Math.Max(cells, batch.PageCapacity));
            _shader.Dispatch(_clearCacheKernel, CeilDiv(count, 64), 1, 1);
            return cache;
        }

        private void SetUInt(string name, uint value) =>
            _shader.SetInt(name, unchecked((int)value));

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

        private static GraphicsBuffer CreateBuffer(int count, int stride,
            string name) => new(GraphicsBuffer.Target.Structured,
                Math.Max(1, count), stride) { name = name };

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);

        private void RequireInitialized()
        {
            if (!_initialized)
                throw new InvalidOperationException(
                    "Sigma intrinsic topology is not initialized.");
        }

        private void OnDestroy()
        {
            for (int index = 0; index < _segmentCaches.Count; ++index)
                _segmentCaches[index].Dispose();
            _segmentCaches.Clear();
            _transitionTau?.Dispose();
            _transitionScale?.Dispose();
            _transitionMeta?.Dispose();
            _sampleSupport?.Dispose();
            _topologyCandidates?.Dispose();
            _topologyCandidateCount?.Dispose();
            _topologyDispatchArguments?.Dispose();
            _counters?.Dispose();
            _dummyEvidence?.Dispose();
            _liveTopologyNeighbours?.Dispose();
            _transitionTau = null;
            _transitionScale = null;
            _transitionMeta = null;
            _sampleSupport = null;
            _topologyCandidates = null;
            _topologyCandidateCount = null;
            _topologyDispatchArguments = null;
            _counters = null;
            _dummyEvidence = null;
            _liveTopologyNeighbours = null;
            _latestTopology.Clear();
            _shader = null;
            _backendGate = null;
            _carrier = null;
            _initialized = false;
        }

        private sealed class TopologySegmentCache : IDisposable
        {
            private readonly GraphicsBuffer _stateIdentity;
            private readonly GraphicsBuffer _metadataIdentity;

            public TopologySegmentCache(SigmaCarrierReadBatch batch)
            {
                SegmentIndex = batch.SegmentIndex;
                Capacity = batch.PageCapacity;
                _stateIdentity = batch.State;
                _metadataIdentity = batch.Metadata;
                TransitionRecords = CreateBuffer(checked(Capacity *
                    TransitionsPerPage), TransitionRecordStride,
                    $"Sigma transition signatures {SegmentIndex}");
                AssociatorFlags = CreateBuffer(checked(Capacity *
                    SigmaCarrier.SamplesPerPage), sizeof(uint),
                    $"Sigma associator flags {SegmentIndex}");
                CellFlags = CreateBuffer(checked(Capacity *
                    SigmaCarrier.SamplesPerPage), sizeof(uint),
                    $"Sigma topology cell cuts {SegmentIndex}");
                PageKeys = CreateBuffer(Capacity, sizeof(uint) * 4,
                    $"Sigma topology page keys {SegmentIndex}");
            }

            public int SegmentIndex { get; }
            public int Capacity { get; }
            public GraphicsBuffer TransitionRecords { get; }
            public GraphicsBuffer AssociatorFlags { get; }
            public GraphicsBuffer CellFlags { get; }
            public GraphicsBuffer PageKeys { get; }
            public SigmaTopologySegmentView View => new(SegmentIndex, Capacity,
                TransitionRecords, CellFlags, PageKeys);

            public bool Matches(SigmaCarrierReadBatch batch) =>
                SegmentIndex == batch.SegmentIndex && Capacity == batch.PageCapacity &&
                ReferenceEquals(_stateIdentity, batch.State) &&
                ReferenceEquals(_metadataIdentity, batch.Metadata);

            public void Dispose()
            {
                TransitionRecords.Dispose();
                AssociatorFlags.Dispose();
                CellFlags.Dispose();
                PageKeys.Dispose();
            }
        }
    }
}
