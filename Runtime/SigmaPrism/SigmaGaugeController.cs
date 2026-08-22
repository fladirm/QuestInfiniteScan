using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct SigmaGaugeRequestGpu
    {
        internal SigmaGaugeRequestGpu(NativeArray<uint> words)
        {
            if (words.Length < 16)
                throw new ArgumentException("Gauge request requires 16 words.",
                    nameof(words));
            Valid = words[0];
            SourceBlock = words[1];
            Axis = words[2];
            Direction = words[3];
            SpanBlocks = words[4];
            ProofSlot = words[5];
            SourceGeneration = words[6];
            Revision = words[7];
            ErrorLo = words[8];
            ErrorHi = words[9];
            WidthLo = words[10];
            WidthHi = words[11];
            IndependenceKey0 = words[12];
            IndependenceKey1 = words[13];
            SourceMask = words[14];
            ProofRevision = words[15];
        }

        internal readonly uint Valid;
        internal readonly uint SourceBlock;
        internal readonly uint Axis;
        internal readonly uint Direction;
        internal readonly uint SpanBlocks;
        internal readonly uint ProofSlot;
        internal readonly uint SourceGeneration;
        internal readonly uint Revision;
        internal readonly uint ErrorLo;
        internal readonly uint ErrorHi;
        internal readonly uint WidthLo;
        internal readonly uint WidthHi;
        internal readonly uint IndependenceKey0;
        internal readonly uint IndependenceKey1;
        internal readonly uint SourceMask;
        internal readonly uint ProofRevision;

        internal bool IsValid => Valid != 0u && SourceBlock < 64u &&
            Axis <= (uint)SigmaGaugeAxis.Y &&
            Direction <= (uint)SigmaGaugeDirection.Negative &&
            SpanBlocks >= SigmaGaugeRefinement.RequiredNullBands + 1u &&
            SpanBlocks <= SigmaDecodedPage.BlocksPerAxis;
        internal long ErrorRaw => unchecked((long)((ulong)ErrorLo |
            ((ulong)ErrorHi << 32)));

        internal SigmaGaugeMap ToMap() => new(
            unchecked((int)(SourceBlock & 7u)),
            unchecked((int)(SourceBlock >> 3)),
            (SigmaGaugeAxis)Axis, (SigmaGaugeDirection)Direction,
            unchecked((int)SpanBlocks));
    }

    internal readonly struct SigmaGaugeSelection
    {
        internal SigmaGaugeSelection(SigmaCarrierPageHandle source,
            SigmaGaugeRequestGpu request, int requestIndex)
        {
            Source = source;
            Request = request;
            RequestIndex = requestIndex;
        }

        internal SigmaCarrierPageHandle Source { get; }
        internal SigmaGaugeRequestGpu Request { get; }
        internal int RequestIndex { get; }
        internal bool IsValid => Source.IsValid && Request.IsValid &&
            RequestIndex >= 0;
    }

    internal readonly struct SigmaGaugeTransactionStatus
    {
        internal SigmaGaugeTransactionStatus(NativeArray<uint> words,
            SigmaGaugeMap map, int rawCloneCount)
        {
            TransformedSamples = Word(words, 0);
            ProofBlocks = Word(words, 1);
            RawClones = Word(words, 2);
            Failed = Word(words, 3) != 0u;
            UnresolvedTransitions = Word(words, 4);
            ArithmeticFailures = Word(words, 5);
            NonNullTailSamples = Word(words, 6);
            RetainedMismatches = Word(words, 7);
            RetainedSamples = Word(words, 8);
            TopologyTransitions = Word(words, 9);
            TopologyFailures = Word(words, 12);
            TargetSingularTransitions = Word(words, 13);
            SourceSingularTransitions = Word(words, 14);
            TopologyTransportFailures = Word(words, 15);
            uint expectedTransformed = checked((uint)(map.RegionLength *
                SigmaDecodedPage.PageSize));
            uint expectedRetained = checked((uint)(SigmaDecodedPage.SampleCount -
                SigmaGaugeRefinement.RequiredNullBands *
                SigmaDecodedPage.BlockSize * SigmaDecodedPage.PageSize));
            IsValid = !Failed && TransformedSamples == expectedTransformed &&
                ProofBlocks == SigmaConstraintLedger.BlocksPerPage &&
                RawClones <= (uint)rawCloneCount &&
                RetainedSamples == expectedRetained &&
                TopologyTransitions == SigmaTopologyController.TransitionsPerPage &&
                TopologyFailures == 0u &&
                TargetSingularTransitions == SourceSingularTransitions &&
                TopologyTransportFailures == 0u &&
                UnresolvedTransitions == 0u && ArithmeticFailures == 0u &&
                NonNullTailSamples == 0u && RetainedMismatches == 0u;
        }

        internal uint TransformedSamples { get; }
        internal uint ProofBlocks { get; }
        internal uint RawClones { get; }
        internal bool Failed { get; }
        internal uint UnresolvedTransitions { get; }
        internal uint ArithmeticFailures { get; }
        internal uint NonNullTailSamples { get; }
        internal uint RetainedMismatches { get; }
        internal uint RetainedSamples { get; }
        internal uint TopologyTransitions { get; }
        internal uint TopologyFailures { get; }
        internal uint TargetSingularTransitions { get; }
        internal uint SourceSingularTransitions { get; }
        internal uint TopologyTransportFailures { get; }
        internal bool IsValid { get; }

        private static uint Word(NativeArray<uint> words, int index) =>
            (uint)index < (uint)words.Length ? words[index] : uint.MaxValue;
    }

    /// <summary>
    /// S4-07 GPU work graph. It changes only the local coordinate gauge of one
    /// immutable carrier generation. Request selection is proof-driven; the CPU
    /// reads only one 64-byte scheduling record and never carrier coefficients.
    /// </summary>
    internal sealed class SigmaGaugeController : IDisposable
    {
        private const string ResourceName =
            "SigmaPrism/SigmaGaugeRefinement";
        internal const int RequestCapacity = 8;
        private const int StatusWord4Count = 4;

        private readonly SigmaCarrier _carrier;
        private readonly SigmaTopologyController _topology;
        private readonly SigmaConstraintLedger _ledger;
        private readonly SigmaExactBackendGate _backendGate;
        private readonly ComputeShader _shader;
        private GraphicsBuffer _blockFacts;
        private GraphicsBuffer _requests;
        private GraphicsBuffer _rawClonePlan;
        private GraphicsBuffer _rawCloneStatus;
        private GraphicsBuffer _targetRawHeads;
        private GraphicsBuffer _status;
        private int _rawCapacity;
        private bool _disposed;

        private readonly int _factsKernel;
        private readonly int _selectKernel;
        private readonly int _clearKernel;
        private readonly int _stateKernel;
        private readonly int _cloneRawKernel;
        private readonly int _finalizeRawKernel;
        private readonly int _proofKernel;
        private readonly int _validateKernel;
        private readonly int _clearTopologyKernel;
        private readonly int _transportTopologyKernel;
        private readonly int _validateTopologyKernel;

        internal SigmaGaugeController(SigmaCarrier carrier,
            SigmaTopologyController topology, SigmaConstraintLedger ledger,
            SigmaExactBackendGate backendGate)
        {
            _carrier = carrier ?? throw new ArgumentNullException(nameof(carrier));
            _topology = topology ?? throw new ArgumentNullException(nameof(topology));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _backendGate = backendGate ?? throw new ArgumentNullException(
                nameof(backendGate));
            _shader = Resources.Load<ComputeShader>(ResourceName);
            if (_shader == null)
                throw new InvalidOperationException(
                    "Sigma gauge-refinement compute resource is missing.");
            _factsKernel = _shader.FindKernel("BuildGaugeBlockFacts");
            _selectKernel = _shader.FindKernel("SelectGaugeRequest");
            _clearKernel = _shader.FindKernel("ClearGaugeTransaction");
            _stateKernel = _shader.FindKernel("TransformGaugeState");
            _cloneRawKernel = _shader.FindKernel("CloneGaugeRawTiles");
            _finalizeRawKernel = _shader.FindKernel("FinalizeGaugeRawChains");
            _proofKernel = _shader.FindKernel("TransformGaugeProof");
            _validateKernel = _shader.FindKernel("ValidateGaugeTransform");
            _clearTopologyKernel = _shader.FindKernel(
                "ClearGaugeTopologyPrior");
            _transportTopologyKernel = _shader.FindKernel(
                "TransportGaugeTopologyPrior");
            _validateTopologyKernel = _shader.FindKernel(
                "ValidateGaugeTopology");
            _blockFacts = Buffer(64, sizeof(uint) * 4,
                "Sigma gauge null/proof block facts");
            _requests = Buffer(RequestCapacity,
                Marshal.SizeOf<SigmaGaugeRequestGpu>(),
                "Sigma exact gauge requests");
            _targetRawHeads = Buffer(SigmaConstraintLedger.BlocksPerPage,
                sizeof(uint), "Sigma gauge target raw heads");
            _status = Buffer(StatusWord4Count, sizeof(uint) * 4,
                "Sigma gauge transaction status");
            EnsureRawCapacity(1);
        }

        internal GraphicsBuffer RequestBuffer => _requests;
        internal GraphicsBuffer StatusBuffer => _status;
        internal GraphicsBuffer RawCloneStatusBuffer => _rawCloneStatus;

        internal int BuildRequests(System.Collections.Generic.IReadOnlyList<
            SigmaCarrierPageHandle> sources)
        {
            RequireAlive();
            if (sources == null)
                throw new ArgumentNullException(nameof(sources));
            int proofSlot;
            _requests.SetData(new SigmaGaugeRequestGpu[RequestCapacity]);
            int count = Math.Min(RequestCapacity, sources.Count);
            for (int index = 0; index < count; ++index)
            {
                SigmaCarrierPageHandle source = sources[index];
                proofSlot = SigmaConstraintLedger.DecodeCertificateSlot(
                    source.CertificateOffset, source.CertificateCount,
                    _ledger.ProofPageCapacity);
                if (proofSlot < 0)
                    continue;
                BindRequestIndex(index);
                _carrier.BindReadable(source, _shader, _factsKernel,
                    "_SourceCarrierState", "_SourceCarrierPageSlot",
                    "_SourceCarrierPageCapacity");
                BindTopology(source, _factsKernel);
                _ledger.BindGaugeSourceReadOnly(_shader, _factsKernel, proofSlot);
                _shader.SetBuffer(_factsKernel, "_GaugeBlockFacts", _blockFacts);
                _backendGate.Bind(_shader, _factsKernel);
                _shader.Dispatch(_factsKernel,
                    SigmaConstraintLedger.BlocksPerPage, 1, 1);

                _ledger.BindGaugeSourceReadOnly(_shader, _selectKernel,
                    proofSlot);
                _shader.SetBuffer(_selectKernel, "_GaugeBlockFacts", _blockFacts);
                _shader.SetBuffer(_selectKernel, "_GaugeRequests", _requests);
                SetUInt("_GaugeSourceGeneration", source.Generation);
                SetUInt("_GaugeTargetRevision", source.Revision);
                _shader.SetInt("_GaugeRequestCapacity", RequestCapacity);
                _backendGate.Bind(_shader, _selectKernel);
                _shader.Dispatch(_selectKernel, 1, 1, 1);
            }
            return count;
        }

        internal bool TryReadBestRequest(NativeArray<uint> words,
            System.Collections.Generic.IReadOnlyList<SigmaCarrierPageHandle>
                sources, out SigmaGaugeSelection selection)
        {
            RequireAlive();
            int count = sources == null ? 0 : Math.Min(RequestCapacity,
                sources.Count);
            if (words.Length < count * 16)
            {
                selection = default;
                return false;
            }
            bool found = false;
            SigmaGaugeRequestGpu best = default;
            SigmaCarrierPageHandle bestSource = default;
            int bestIndex = -1;
            for (int index = 0; index < count; ++index)
            {
                NativeArray<uint> slice = words.GetSubArray(index * 16, 16);
                var request = new SigmaGaugeRequestGpu(slice);
                SigmaCarrierPageHandle source = sources[index];
                int proofSlot = SigmaConstraintLedger.DecodeCertificateSlot(
                    source.CertificateOffset, source.CertificateCount,
                    _ledger.ProofPageCapacity);
                bool matches = request.IsValid && proofSlot >= 0 &&
                    request.ProofSlot == (uint)proofSlot &&
                    request.SourceGeneration == source.Generation &&
                    request.Revision == source.Revision &&
                    request.ProofRevision == source.Revision;
                if (!matches)
                    continue;
                long error = request.ErrorRaw;
                if (!found || error > best.ErrorRaw ||
                    (error == best.ErrorRaw && source.Coordinate.CompareTo(
                        bestSource.Coordinate) < 0))
                {
                    found = true;
                    best = request;
                    bestSource = source;
                    bestIndex = index;
                }
            }
            selection = found ? new SigmaGaugeSelection(bestSource, best,
                bestIndex) : default;
            return found;
        }

        internal SigmaGaugeTransaction BeginTransform(
            SigmaGaugeSelection selection)
        {
            RequireAlive();
            SigmaCarrierPageHandle source = selection.Source;
            SigmaGaugeRequestGpu request = selection.Request;
            if (!request.IsValid)
                throw new ArgumentException("Gauge request is invalid.",
                    nameof(request));
            SigmaGaugeMap map = request.ToMap();
            SigmaGaugeProofLease proof = _ledger.BeginGaugePage(source, map);
            SigmaCarrierWriteLease carrier = null;
            try
            {
                carrier = _carrier.BeginNextGeneration(source.Coordinate,
                    source.Revision, proof.CertificateOffset,
                    proof.CertificateCount);
                EnsureRawCapacity(Math.Max(1, proof.ClonePlan.Length));
                if (proof.ClonePlan.Length != 0)
                    _rawClonePlan.SetData(proof.ClonePlan);
                BindTransactionCommon(proof, request, selection.RequestIndex);
                BindProofTarget(_clearKernel, proof);
                _shader.Dispatch(_clearKernel,
                    SigmaConstraintLedger.BoundsPerPage / 64, 1, 1);

                BindState(source, carrier, _stateKernel);
                BindTopology(source, _stateKernel);
                _shader.SetBuffer(_stateKernel, "_GaugeStatus", _status);
                _shader.Dispatch(_stateKernel,
                    SigmaCarrier.SamplesPerPage / 64, 1, 1);

                if (proof.ClonePlan.Length != 0)
                {
                    BindProofSource(_cloneRawKernel, proof);
                    BindProofTarget(_cloneRawKernel, proof);
                    _shader.SetBuffer(_cloneRawKernel, "_GaugeRawClonePlan",
                        _rawClonePlan);
                    _shader.SetBuffer(_cloneRawKernel, "_GaugeRawCloneStatus",
                        _rawCloneStatus);
                    // One 64-lane workgroup owns one raw-tile clone plan.
                    _shader.Dispatch(_cloneRawKernel,
                        proof.ClonePlan.Length, 1, 1);
                }
                BindProofTarget(_finalizeRawKernel, proof);
                _shader.SetBuffer(_finalizeRawKernel, "_GaugeRawCloneStatus",
                    _rawCloneStatus);
                _shader.Dispatch(_finalizeRawKernel, 1, 1, 1);

                BindProofSource(_proofKernel, proof);
                BindProofTarget(_proofKernel, proof);
                _shader.Dispatch(_proofKernel,
                    SigmaConstraintLedger.BlocksPerPage, 1, 1);

                BindState(source, carrier, _validateKernel);
                _shader.SetBuffer(_validateKernel, "_GaugeStatus", _status);
                _shader.Dispatch(_validateKernel,
                    SigmaCarrier.SamplesPerPage / 64, 1, 1);
                var transaction = new SigmaGaugeTransaction(source, carrier,
                    proof, request, selection.RequestIndex);
                carrier = null;
                proof = null;
                return transaction;
            }
            finally
            {
                carrier?.Dispose();
                proof?.Dispose();
            }
        }

        internal SigmaGaugeTransactionStatus ReadStatus(NativeArray<uint> words,
            SigmaGaugeTransaction transaction) => new(words, transaction.Map,
                transaction.Proof.ClonePlan.Length);

        internal void ValidateTopology(SigmaGaugeTransaction transaction)
        {
            RequireAlive();
            SigmaTopologyGaugeBinding topology = transaction.TopologyBinding;
            if (topology.TargetTransitions == null ||
                topology.SourceTransitions == null)
                throw new InvalidOperationException(
                    "Gauge topology transport was not prepared.");
            BindRequestIndex(transaction.RequestIndex);
            BindGaugeTopology(topology, _validateTopologyKernel);
            _shader.SetBuffer(_validateTopologyKernel,
                "_GaugeStatus", _status);
            _shader.SetBuffer(_validateTopologyKernel, "_GaugeRequests",
                _requests);
            _backendGate.Bind(_shader, _validateTopologyKernel);
            _shader.Dispatch(_validateTopologyKernel,
                SigmaTopologyController.TransitionsPerPage / 64, 1, 1);
        }

        internal void TransportTopologyPrior(SigmaGaugeTransaction transaction)
        {
            RequireAlive();
            SigmaTopologyGaugeBinding topology = _topology.
                PrepareGaugeGeneration(transaction.Carrier.Handle,
                    transaction.Source);
            transaction.TopologyBinding = topology;
            BindRequestIndex(transaction.RequestIndex);
            BindGaugeTopology(topology, _clearTopologyKernel);
            _shader.SetBuffer(_clearTopologyKernel, "_GaugeStatus", _status);
            _backendGate.Bind(_shader, _clearTopologyKernel);
            _shader.Dispatch(_clearTopologyKernel,
                SigmaTopologyController.TransitionsPerPage / 64, 1, 1);
            BindGaugeTopology(topology, _transportTopologyKernel);
            _shader.SetBuffer(_transportTopologyKernel, "_GaugeStatus", _status);
            _backendGate.Bind(_shader, _transportTopologyKernel);
            _shader.Dispatch(_transportTopologyKernel,
                SigmaTopologyController.TransitionsPerPage / 64, 1, 1);
        }

        internal void PublishProof(SigmaGaugeTransaction transaction,
            NativeArray<uint> cloneStatus) =>
            _ledger.PublishGauge(transaction.Proof, cloneStatus);

        internal void ValidateProofForPublication(
            SigmaGaugeTransaction transaction, NativeArray<uint> cloneStatus) =>
            _ledger.ValidateGaugeForPublication(transaction.Proof, cloneStatus);

        private void BindTransactionCommon(SigmaGaugeProofLease proof,
            SigmaGaugeRequestGpu request, int requestIndex)
        {
            BindRequestIndex(requestIndex);
            SetUInt("_GaugeTargetRevision", request.Revision);
            _shader.SetInt("_GaugeRawCloneCount", proof.ClonePlan.Length);
            _shader.SetBuffer(_clearKernel, "_GaugeRequests", _requests);
        }

        private void BindRequestIndex(int requestIndex)
        {
            if ((uint)requestIndex >= (uint)RequestCapacity)
                throw new ArgumentOutOfRangeException(nameof(requestIndex));
            _shader.SetInt("_GaugeRequestIndex", requestIndex);
            _shader.SetInt("_GaugeRequestCapacity", RequestCapacity);
            _shader.SetBuffer(_stateKernel, "_GaugeRequests", _requests);
            _shader.SetBuffer(_cloneRawKernel, "_GaugeRequests", _requests);
            _shader.SetBuffer(_proofKernel, "_GaugeRequests", _requests);
            _shader.SetBuffer(_validateKernel, "_GaugeRequests", _requests);
            _shader.SetBuffer(_validateTopologyKernel, "_GaugeRequests",
                _requests);
            _shader.SetBuffer(_clearTopologyKernel, "_GaugeRequests", _requests);
            _shader.SetBuffer(_transportTopologyKernel, "_GaugeRequests",
                _requests);
        }


        private void BindGaugeTopology(SigmaTopologyGaugeBinding topology,
            int kernel)
        {
            _shader.SetInt("_SourceTopologyPageSlot", topology.SourceSlot);
            _shader.SetInt("_SourceTopologyPageCapacity",
                topology.SourceCapacity);
            _shader.SetInt("_TargetTopologyPageSlot", topology.TargetSlot);
            _shader.SetInt("_TargetTopologyPageCapacity",
                topology.TargetCapacity);
            _shader.SetBuffer(kernel, "_SourceTopologyTransitions",
                topology.SourceTransitions);
            _shader.SetBuffer(kernel, "_TargetTopologyTransitions",
                topology.TargetTransitions);
        }

        private void BindState(SigmaCarrierPageHandle source,
            SigmaCarrierWriteLease target, int kernel)
        {
            _carrier.BindReadable(source, _shader, kernel,
                "_SourceCarrierState", "_SourceCarrierPageSlot",
                "_SourceCarrierPageCapacity");
            target.BindWritable(_shader, kernel, "_TargetCarrierState",
                "_TargetCarrierPageSlot", "_TargetCarrierPageCapacity");
            _shader.SetBuffer(kernel, "_GaugeStatus", _status);
            _backendGate.Bind(_shader, kernel);
        }

        private void BindTopology(SigmaCarrierPageHandle source, int kernel)
        {
            if (!_topology.TryGetSegmentView(source.SegmentIndex,
                    out SigmaTopologySegmentView topology))
                throw new InvalidOperationException(
                    "Gauge source topology cache is unavailable.");
            _shader.SetInt("_SourceTopologyPageSlot", source.PageSlot);
            _shader.SetInt("_SourceTopologyPageCapacity", topology.PageCapacity);
            _shader.SetBuffer(kernel, "_SourceTopologyTransitions",
                topology.TransitionRecords);
        }

        private void BindProofSource(int kernel, SigmaGaugeProofLease proof)
        {
            _ledger.BindGaugeSource(_shader, kernel, proof);
            _shader.SetBuffer(kernel, "_GaugeRawClonePlan", _rawClonePlan);
            _shader.SetBuffer(kernel, "_GaugeRawCloneStatus", _rawCloneStatus);
            _shader.SetBuffer(kernel, "_GaugeTargetRawHeads", _targetRawHeads);
            _shader.SetBuffer(kernel, "_GaugeStatus", _status);
            _backendGate.Bind(_shader, kernel);
        }

        private void BindProofTarget(int kernel, SigmaGaugeProofLease proof)
        {
            _ledger.BindGaugeTarget(_shader, kernel, proof);
            _shader.SetBuffer(kernel, "_GaugeRawClonePlan", _rawClonePlan);
            _shader.SetBuffer(kernel, "_GaugeRawCloneStatus", _rawCloneStatus);
            _shader.SetBuffer(kernel, "_GaugeTargetRawHeads", _targetRawHeads);
            _shader.SetBuffer(kernel, "_GaugeStatus", _status);
            _backendGate.Bind(_shader, kernel);
        }

        private void EnsureRawCapacity(int count)
        {
            if (_rawCapacity >= count)
                return;
            int capacity = 1;
            while (capacity < count)
                capacity = checked(capacity << 1);
            _rawClonePlan?.Dispose();
            _rawCloneStatus?.Dispose();
            _rawClonePlan = Buffer(capacity,
                Marshal.SizeOf<SigmaGaugeRawClonePlan>(),
                "Sigma gauge immutable raw clone plan");
            _rawCloneStatus = Buffer(capacity, sizeof(uint) * 4,
                "Sigma gauge raw clone status");
            _rawCapacity = capacity;
        }

        private void SetUInt(string name, uint value) =>
            _shader.SetInt(name, unchecked((int)value));

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);

        private static GraphicsBuffer Buffer(int count, int stride,
            string name) => new(GraphicsBuffer.Target.Structured,
                Math.Max(1, count), stride) { name = name };

        private void RequireAlive()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SigmaGaugeController));
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _blockFacts?.Dispose();
            _requests?.Dispose();
            _rawClonePlan?.Dispose();
            _rawCloneStatus?.Dispose();
            _targetRawHeads?.Dispose();
            _status?.Dispose();
            _blockFacts = null;
            _requests = null;
            _rawClonePlan = null;
            _rawCloneStatus = null;
            _targetRawHeads = null;
            _status = null;
        }
    }

    internal sealed class SigmaGaugeTransaction : IDisposable
    {
        internal SigmaGaugeTransaction(SigmaCarrierPageHandle source,
            SigmaCarrierWriteLease carrier, SigmaGaugeProofLease proof,
            SigmaGaugeRequestGpu request, int requestIndex)
        {
            Source = source;
            Carrier = carrier;
            Proof = proof;
            Request = request;
            RequestIndex = requestIndex;
            Map = request.ToMap();
        }

        internal SigmaCarrierPageHandle Source { get; }
        internal SigmaCarrierWriteLease Carrier { get; private set; }
        internal SigmaGaugeProofLease Proof { get; private set; }
        internal SigmaGaugeRequestGpu Request { get; }
        internal int RequestIndex { get; }
        internal SigmaGaugeMap Map { get; }
        internal SigmaTopologyBuildToken Topology { get; set; }
        internal SigmaTopologyGaugeBinding TopologyBinding { get; set; }

        internal void MarkPublished()
        {
            Carrier = null;
            Proof = null;
        }

        public void Dispose()
        {
            Carrier?.Dispose();
            Proof?.Dispose();
            Carrier = null;
            Proof = null;
        }
    }
}
