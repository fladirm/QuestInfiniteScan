using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Fixed host-side recorder for the S4-08.3 GPU state machine.  It binds
    /// immutable resources and records bounded indirect work only; every work
    /// choice, cursor and canonical decision remains GPU-resident.
    /// </summary>
    internal sealed class SigmaStreamingGraph
    {
        private const string WorkResource =
            "SigmaPrism/SigmaInverseWorkGraph";
        private const string SourceResource = "SigmaPrism/SigmaSourceBundle";
        private const string InverseResource = "SigmaPrism/SigmaStreamInverse";
        private const string ProofResource = "SigmaPrism/SigmaStreamProof";
        private const string TransitionResource =
            "SigmaPrism/SigmaStreamTransition";
        private const string PublicationResource =
            "SigmaPrism/SigmaStreamPublication";
        private const string DerivedResource =
            "SigmaPrism/SigmaStreamDerived";
        private const string DormantResource =
            "SigmaPrism/SigmaStreamDormant";

        private readonly SigmaCarrierReadBatch _pool;
        private readonly SigmaConstraintLedger _ledger;
        private readonly SigmaStreamingResources _stream;
        private readonly SigmaExactBackendGate _backendGate;
        private readonly SigmaRenderer _renderer;
        private readonly GraphicsBuffer _rgbViewOperators;
        private readonly GraphicsBuffer _rgbViewSupportScale;
        private readonly SigmaTopologySegmentView _topologyView;

        private readonly ComputeShader _work;
        private readonly ComputeShader _source;
        private readonly ComputeShader _inverse;
        private readonly ComputeShader _proof;
        private readonly ComputeShader _transition;
        private readonly ComputeShader _publication;
        private readonly ComputeShader _derived;
        private readonly ComputeShader _dormant;

        private readonly int _initializeGraph;
        private readonly int _initializeOwnership;
        private readonly int _clearIngress;
        private readonly int _compactIngress;
        private readonly int _finalizeBundles;
        private readonly int _schedule;
        private readonly int _scheduleDiagnostics;
        private readonly int _releaseProbation;
        private readonly int _preparePages;

        private readonly int _copyBundleMetadata;
        private readonly int _extractBundleSamples;
        private readonly int _evaluateMicrotile;

        private readonly int _reduceSourceBlock;
        private readonly int _prepareProofOrder;
        private readonly int _mergeProofRuns;
        private readonly int _coalesceProof;
        private readonly int _prepareRedundancy;
        private readonly int _evaluateRedundancy;
        private readonly int _emitCertificates;
        private readonly int _retainRaw;
        private readonly int _completeProofBlock;

        private readonly int _validateTransition;
        private readonly int _validateAssociator;

        private readonly int _initializeVisibility;
        private readonly int _prepareManifest;
        private readonly int _publishManifest;
        private readonly int _resolvePageCaches;
        private readonly int _retireTransaction;
        private readonly int _materializeTopology;
        private readonly int _prepareDormant;
        private readonly int _parkDormantSources;
        private readonly int _parkDormantSegments;
        private readonly int _releaseDormantPage;
        private readonly int _finalizeDormant;
        private readonly int _recheckDormant;

        internal SigmaStreamingGraph(SigmaCarrierReadBatch pool,
            SigmaConstraintLedger ledger, SigmaStreamingResources stream,
            SigmaExactBackendGate backendGate,
            SigmaRenderer renderer,
            GraphicsBuffer rgbViewOperators,
            GraphicsBuffer rgbViewSupportScale,
            SigmaTopologySegmentView topologyView)
        {
            _pool = pool;
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _backendGate = backendGate ?? throw new ArgumentNullException(
                nameof(backendGate));
            _renderer = renderer ?? throw new ArgumentNullException(
                nameof(renderer));
            _rgbViewOperators = rgbViewOperators ?? throw new ArgumentNullException(
                nameof(rgbViewOperators));
            _rgbViewSupportScale = rgbViewSupportScale ??
                throw new ArgumentNullException(nameof(rgbViewSupportScale));
            if (topologyView.PageCapacity != pool.PageCapacity ||
                topologyView.SegmentIndex != pool.SegmentIndex)
                throw new ArgumentException(
                    "Streaming topology view does not match the carrier pool.",
                    nameof(topologyView));
            _topologyView = topologyView;

            _work = RequireShader(WorkResource);
            _source = RequireShader(SourceResource);
            _inverse = RequireShader(InverseResource);
            _proof = RequireShader(ProofResource);
            _transition = RequireShader(TransitionResource);
            _publication = RequireShader(PublicationResource);
            _derived = RequireShader(DerivedResource);
            _dormant = RequireShader(DormantResource);

            _initializeGraph = _work.FindKernel("InitializeStreamingGraph");
            _initializeOwnership = _work.FindKernel(
                "InitializeStreamingOwnership");
            _clearIngress = _work.FindKernel("ClearIngressWork");
            _compactIngress = _work.FindKernel("CompactIngressBundles");
            _finalizeBundles = _work.FindKernel("FinalizeExtractedBundles");
            _schedule = _work.FindKernel("ScheduleSigmaTransactions");
            _scheduleDiagnostics = _work.FindKernel(
                "FinalizeStreamingScheduleDiagnostics");
            _releaseProbation = _work.FindKernel(
                "ReleaseProbationAssociations");
            _preparePages = _work.FindKernel("PrepareTransactionPages");

            _copyBundleMetadata = _source.FindKernel(
                "CopySealedBundleMetadata");
            _extractBundleSamples = _source.FindKernel(
                "ExtractSealedBundleSamples");
            _evaluateMicrotile = _inverse.FindKernel(
                "EvaluateTransactionMicrotile");

            _reduceSourceBlock = _proof.FindKernel(
                "ReduceTransactionSourceBlock");
            _prepareProofOrder = _proof.FindKernel("PrepareProofOrder");
            _mergeProofRuns = _proof.FindKernel("MergeProofRuns");
            _coalesceProof = _proof.FindKernel("CoalesceProofWindow");
            _prepareRedundancy = _proof.FindKernel(
                "PrepareProofRedundancy");
            _evaluateRedundancy = _proof.FindKernel(
                "EvaluateProofRedundancyWindow");
            _emitCertificates = _proof.FindKernel("EmitProofCertificates");
            _retainRaw = _proof.FindKernel("RetainProofRawWindow");
            _completeProofBlock = _proof.FindKernel("CompleteProofBlock");

            _validateTransition = _transition.FindKernel(
                "ValidateCandidateTransitionChunk");
            _validateAssociator = _transition.FindKernel(
                "ValidateCandidateAssociatorChunk");

            _initializeVisibility = _publication.FindKernel(
                "InitializeManifestVisibility");
            _prepareManifest = _publication.FindKernel(
                "PreparePublicationManifest");
            _publishManifest = _publication.FindKernel(
                "PublishPublicationManifest");
            _resolvePageCaches = _publication.FindKernel(
                "ResolvePublishedPageCaches");
            _retireTransaction = _publication.FindKernel(
                "RetirePublishedTransaction");
            _materializeTopology = _derived.FindKernel(
                "MaterializePublishedTopology");
            _prepareDormant = _dormant.FindKernel(
                "PrepareDormantParking");
            _parkDormantSources = _dormant.FindKernel(
                "ParkDormantSources");
            _parkDormantSegments = _dormant.FindKernel(
                "ParkDormantSegments");
            _releaseDormantPage = _dormant.FindKernel(
                "ReleaseDormantPage");
            _finalizeDormant = _dormant.FindKernel(
                "FinalizeDormantParking");
            _recheckDormant = _dormant.FindKernel(
                "RecheckDormantProbations");
        }

        internal SigmaStreamingResources Resources => _stream;

        internal void RecordInitialize(CommandBuffer command)
        {
            RequireCommand(command);
            BindInitializeGraph(command, _initializeGraph);
            command.DispatchCompute(_work, _initializeGraph, 1, 1, 1);
            BindInitializeOwnership(command, _initializeOwnership);
            command.DispatchCompute(_work, _initializeOwnership, 1, 1, 1);
            BindInitializeVisibility(command, _initializeVisibility);
            command.DispatchCompute(_publication, _initializeVisibility,
                CeilDiv(_pool.PageCapacity, 64), 1, 1);
        }

        internal void RecordIngress(CommandBuffer command,
            StereoRigFrameLease frame, SigmaPredictionFrameLease prediction,
            RenderTexture metricDepth, RenderTexture depthFlags,
            GraphicsBuffer correctedDepthCalibration,
            GraphicsBuffer correctedRgbCalibration,
            GraphicsBuffer poseResult, GraphicsBuffer frameStaging,
            GraphicsBuffer activePageFlags,
            GraphicsBuffer unmatchedBlockFlags,
            Vector2Int blockResolution, int blockCount, uint revision,
            uint leftKey, uint rightKey, uint rgbLeftKey, uint rgbRightKey,
            uint rayEpoch, Texture depthRayCenterLeft)
        {
            RequireCommand(command);
            if (frame == null || !frame.IsValid || prediction == null ||
                prediction.IsDisposed)
                throw new ArgumentException(
                    "Streaming ingress requires a coherent retained frame.");
            _renderer.EnsureHistoricalRevalidationTargets(
                frame.DepthResolution);

            BindClearIngress(command, _clearIngress);
            command.DispatchCompute(_work, _clearIngress, 1, 1, 1);

            BindCompactIngress(command, _compactIngress, activePageFlags,
                unmatchedBlockFlags, blockResolution, blockCount, revision,
                frame.CalibrationEpoch, leftKey, rightKey, rgbLeftKey,
                rgbRightKey, rayEpoch);
            command.DispatchCompute(_work, _compactIngress, 1, 1, 1);

            BindBundleSource(command, _copyBundleMetadata, frame, prediction,
                metricDepth, depthFlags, correctedDepthCalibration,
                correctedRgbCalibration, poseResult, frameStaging,
                depthRayCenterLeft);
            // The extraction count is GPU-owned.  Sixty-four bounded Y groups
            // cover every possible header and self-reject above the live count.
            command.DispatchCompute(_source, _copyBundleMetadata, 1,
                SigmaGeneratedStreaming.BundleCapacity, 1);

            BindBundleSource(command, _extractBundleSamples, frame, prediction,
                metricDepth, depthFlags, correctedDepthCalibration,
                correctedRgbCalibration, poseResult, frameStaging,
                depthRayCenterLeft);
            DispatchIndirect(command, _source, _extractBundleSamples,
                SigmaStreamOpcode.EXTRACT_BUNDLE);

            BindFinalizeBundles(command, _finalizeBundles);
            command.DispatchCompute(_work, _finalizeBundles, 1, 1, 1);
        }

        internal void RecordCanonicalQuantum(CommandBuffer command,
            int singularShift, int associatorShift)
        {
            RequireCommand(command);

            BindInitializeVisibility(command, _initializeVisibility);
            command.DispatchCompute(_publication, _initializeVisibility,
                CeilDiv(_pool.PageCapacity, 64), 1, 1);

            BindSchedule(command, _schedule);
            command.DispatchCompute(_work, _schedule, 1, 1, 1);

            BindPreparePages(command, _preparePages);
            DispatchIndirect(command, _work, _preparePages,
                SigmaStreamOpcode.ADMIT);

            BindReleaseProbation(command, _releaseProbation);
            command.DispatchCompute(_work, _releaseProbation, 1, 1, 1);

            int[] dormantParking = { _prepareDormant, _parkDormantSources,
                _parkDormantSegments, _releaseDormantPage,
                _finalizeDormant };
            for (int index = 0; index < dormantParking.Length; ++index)
            {
                int kernel = dormantParking[index];
                BindDormant(command, kernel);
                DispatchIndirect(command, _dormant, kernel,
                    SigmaStreamOpcode.DORMANT_RECHECK);
            }
            BindDormant(command, _recheckDormant);
            command.DispatchCompute(_dormant, _recheckDormant, 1, 1, 1);

            _renderer.RecordHistoricalRevalidation(command, _stream, _ledger);

            BindInverse(command, _evaluateMicrotile);
            DispatchIndirect(command, _inverse, _evaluateMicrotile,
                SigmaStreamOpcode.EVALUATE_MICROTILE);

            BindProof(command, _reduceSourceBlock);
            DispatchIndirect(command, _proof, _reduceSourceBlock,
                SigmaStreamOpcode.REDUCE_SOURCE_BLOCK);

            int[] proofClosure = {
                _prepareProofOrder, _mergeProofRuns, _coalesceProof,
                _prepareRedundancy, _evaluateRedundancy, _emitCertificates,
                _retainRaw, _completeProofBlock
            };
            for (int index = 0; index < proofClosure.Length; ++index)
            {
                int kernel = proofClosure[index];
                BindProof(command, kernel);
                DispatchIndirect(command, _proof, kernel,
                    SigmaStreamOpcode.FINALIZE_PROOF_BLOCK);
            }

            BindTransition(command, _validateTransition, singularShift,
                associatorShift);
            DispatchIndirect(command, _transition, _validateTransition,
                SigmaStreamOpcode.TRANSITION_ANNIHILATOR);
            BindTransition(command, _validateAssociator, singularShift,
                associatorShift);
            DispatchIndirect(command, _transition, _validateAssociator,
                SigmaStreamOpcode.TRANSITION_ASSOCIATOR);
            int[] publication = { _prepareManifest, _publishManifest,
                _resolvePageCaches };
            for (int index = 0; index < publication.Length; ++index)
            {
                int kernel = publication[index];
                BindPublication(command, kernel);
                DispatchIndirect(command, _publication, kernel,
                    SigmaStreamOpcode.PUBLISH_MANIFEST);
            }

            BindScheduleDiagnostics(command, _scheduleDiagnostics);
            command.DispatchCompute(_work, _scheduleDiagnostics, 1, 1, 1);
        }

        internal void RecordDerivedQuantum(CommandBuffer command)
        {
            RequireCommand(command);
            BindDerived(command, _materializeTopology);
            DispatchIndirect(command, _derived, _materializeTopology,
                SigmaStreamOpcode.PUBLISH_MANIFEST);
            BindPublication(command, _retireTransaction);
            DispatchIndirect(command, _publication, _retireTransaction,
                SigmaStreamOpcode.PUBLISH_MANIFEST);
        }

        private void BindInitializeGraph(CommandBuffer command, int kernel)
        {
            Set(command, _work, kernel, "_StreamTransactions",
                _stream.Transactions);
            Set(command, _work, kernel, "_StreamBundles", _stream.Bundles);
            Set(command, _work, kernel, "_StreamProbation", _stream.Probation);
            Set(command, _work, kernel, "_StreamWorkCounts", _stream.WorkCounts);
            Set(command, _work, kernel, "_StreamDispatchArgs",
                _stream.DispatchArguments);
            Set(command, _work, kernel, "_StreamSchedulerControl",
                _stream.SchedulerControl);
            Set(command, _work, kernel, "_StreamDiagnostics",
                _stream.Diagnostics);
        }

        private void BindInitializeOwnership(CommandBuffer command, int kernel)
        {
            Set(command, _work, kernel, "_StreamAssociationOwners",
                _stream.AssociationOwners);
            Set(command, _work, kernel, "_StreamSourceSegments",
                _stream.SourceHandleSegments);
            SetInt(command, _work, "_StreamSourceSegmentCapacity",
                SigmaStreamingResources.SourceHandleSegmentCapacity);
        }

        private void BindClearIngress(CommandBuffer command, int kernel)
        {
            Set(command, _work, kernel, "_StreamSchedulerControl",
                _stream.SchedulerControl);
            Set(command, _work, kernel, "_StreamWorkCounts", _stream.WorkCounts);
            Set(command, _work, kernel, "_StreamDispatchArgs",
                _stream.DispatchArguments);
        }

        private void BindCompactIngress(CommandBuffer command, int kernel,
            GraphicsBuffer activePageFlags, GraphicsBuffer unmatchedBlockFlags,
            Vector2Int blockResolution, int blockCount, uint revision,
            uint calibrationEpoch, uint leftKey, uint rightKey,
            uint rgbLeftKey, uint rgbRightKey, uint rayEpoch)
        {
            Set(command, _work, kernel, "_PageMetadata", _pool.Metadata);
            Set(command, _work, kernel, "_CurrentFlags", _pool.CurrentFlags);
            Set(command, _work, kernel, "_ActivePageFlags", activePageFlags);
            Set(command, _work, kernel, "_UnmatchedBlockFlags",
                unmatchedBlockFlags);
            Set(command, _work, kernel, "_StreamTransactions",
                _stream.Transactions);
            Set(command, _work, kernel, "_StreamBundles", _stream.Bundles);
            Set(command, _work, kernel, "_StreamAssociationOwners",
                _stream.AssociationOwners);
            Set(command, _work, kernel, "_StreamWorkItems", _stream.WorkItems);
            Set(command, _work, kernel, "_StreamWorkCounts", _stream.WorkCounts);
            Set(command, _work, kernel, "_StreamDispatchArgs",
                _stream.DispatchArguments);
            Set(command, _work, kernel, "_StreamSchedulerControl",
                _stream.SchedulerControl);
            Set(command, _work, kernel, "_RawLiveWords",
                _ledger.RawLiveBitmapBuffer);
            SetInt(command, _work, "_PageCapacity", _pool.PageCapacity);
            SetInt(command, _work, "_SegmentIndex", _pool.SegmentIndex);
            SetInt(command, _work, "_ActiveFlagCount", activePageFlags.count);
            SetInt(command, _work, "_BlockFlagCount", blockCount);
            command.SetComputeIntParams(_work, "_GaugeBlockResolution",
                blockResolution.x, blockResolution.y);
            SetUInt(command, _work, "_FrameRevision", revision);
            SetUInt(command, _work, "_CalibrationEpoch", calibrationEpoch);
            SetUInt(command, _work, "_LeftIndependenceKey", leftKey);
            SetUInt(command, _work, "_RightIndependenceKey", rightKey);
            SetUInt(command, _work, "_RgbLeftIndependenceKey", rgbLeftKey);
            SetUInt(command, _work, "_RgbRightIndependenceKey", rgbRightKey);
            SetUInt(command, _work, "_RayEpochGeneration", rayEpoch);
            SetInt(command, _work, "_RawTileCapacity",
                _ledger.RawTileCapacity);
        }

        private void BindFinalizeBundles(CommandBuffer command, int kernel)
        {
            Set(command, _work, kernel, "_StreamWorkItems", _stream.WorkItems);
            Set(command, _work, kernel, "_StreamWorkCounts", _stream.WorkCounts);
            Set(command, _work, kernel, "_StreamBundles", _stream.Bundles);
            Set(command, _work, kernel, "_StreamDiagnostics",
                _stream.Diagnostics);
        }

        private void BindSchedule(CommandBuffer command, int kernel)
        {
            Set(command, _work, kernel, "_SigmaExactBackendGate",
                _backendGate.Buffer);
            Set(command, _work, kernel, "_StreamTransactions",
                _stream.Transactions);
            Set(command, _work, kernel, "_StreamBundles", _stream.Bundles);
            Set(command, _work, kernel, "_StreamProbation", _stream.Probation);
            Set(command, _work, kernel, "_StreamSourceSegments",
                _stream.SourceHandleSegments);
            Set(command, _work, kernel, "_StreamWorkItems", _stream.WorkItems);
            Set(command, _work, kernel, "_StreamWorkCounts", _stream.WorkCounts);
            Set(command, _work, kernel, "_StreamDispatchArgs",
                _stream.DispatchArguments);
            Set(command, _work, kernel, "_StreamSchedulerControl",
                _stream.SchedulerControl);
            Set(command, _work, kernel, "_StreamKernelTokenCosts",
                _stream.KernelTokenCosts);
            Set(command, _work, kernel, "_StreamKernelBudgetClasses",
                _stream.KernelBudgetClasses);
            SetInt(command, _work, "_StreamSourceSegmentCapacity",
                SigmaStreamingResources.SourceHandleSegmentCapacity);
        }

        private void BindScheduleDiagnostics(CommandBuffer command, int kernel)
        {
            Set(command, _work, kernel, "_StreamTransactions",
                _stream.Transactions);
            Set(command, _work, kernel, "_StreamBundles", _stream.Bundles);
            Set(command, _work, kernel, "_StreamSchedulerControl",
                _stream.SchedulerControl);
            Set(command, _work, kernel, "_StreamDiagnostics",
                _stream.Diagnostics);
        }

        private void BindReleaseProbation(CommandBuffer command, int kernel)
        {
            Set(command, _work, kernel, "_StreamBundles", _stream.Bundles);
            Set(command, _work, kernel, "_StreamAssociationOwners",
                _stream.AssociationOwners);
        }

        private void BindPreparePages(CommandBuffer command, int kernel)
        {
            Set(command, _work, kernel, "_SigmaExactBackendGate",
                _backendGate.Buffer);
            Set(command, _work, kernel, "_CarrierState", _pool.State);
            Set(command, _work, kernel, "_PageMetadata", _pool.Metadata);
            Set(command, _work, kernel, "_DirtyFlags", _pool.DirtyFlags);
            Set(command, _work, kernel, "_CurrentFlags", _pool.CurrentFlags);
            Set(command, _work, kernel, "_ReadoutDirtyFlags",
                _pool.ReadoutDirtyFlags);
            Set(command, _work, kernel, "_StreamPageVisibility",
                _stream.PageVisibility);
            Set(command, _work, kernel, "_StreamTransactions",
                _stream.Transactions);
            Set(command, _work, kernel, "_StreamWorkItems", _stream.WorkItems);
            Set(command, _work, kernel, "_StreamWorkCounts", _stream.WorkCounts);
            Set(command, _work, kernel, "_StreamSchedulerControl",
                _stream.SchedulerControl);
            SetInt(command, _work, "_PageCapacity", _pool.PageCapacity);
        }

        private void BindBundleSource(CommandBuffer command, int kernel,
            StereoRigFrameLease frame, SigmaPredictionFrameLease prediction,
            RenderTexture metricDepth, RenderTexture depthFlags,
            GraphicsBuffer depthCalibration, GraphicsBuffer rgbCalibration,
            GraphicsBuffer poseResult, GraphicsBuffer frameStaging,
            Texture depthRayCenterLeft)
        {
            Set(command, _source, kernel, "_CarrierState", _pool.State);
            Set(command, _source, kernel, "_PageMetadata", _pool.Metadata);
            Set(command, _source, kernel, "_DepthCalibrationQ48",
                depthCalibration);
            Set(command, _source, kernel, "_RgbCalibrationQ48", rgbCalibration);
            Set(command, _source, kernel, "_RawFrameStaging", frameStaging);
            Set(command, _source, kernel, "_StreamWorkItems", _stream.WorkItems);
            Set(command, _source, kernel, "_StreamWorkCounts", _stream.WorkCounts);
            Set(command, _source, kernel, "_StreamBundles", _stream.Bundles);
            Set(command, _source, kernel, "_StreamBundleCalibration",
                _stream.BundleCalibration);
            Set(command, _source, kernel, "_StreamBundleRayEpoch",
                _stream.BundleRayEpoch);
            Set(command, _source, kernel, "_StreamAssociation",
                _stream.Association);
            Set(command, _source, kernel, "_RawWords", _ledger.RawWordsBuffer);
            Set(command, _source, kernel, "_RawFrameRecords",
                _ledger.FrameRecordBuffer);
            Set(command, _source, kernel, "_PoseResult", poseResult);
            SetInt(command, _source, "_PageCapacity", _pool.PageCapacity);
            command.SetComputeIntParams(_source, "_Resolution",
                frame.DepthResolution.x, frame.DepthResolution.y);
            command.SetComputeIntParams(_source, "_RgbResolutionLeft",
                frame.RgbLeft.Resolution.x, frame.RgbLeft.Resolution.y);
            command.SetComputeIntParams(_source, "_RgbResolutionRight",
                frame.RgbRight.Resolution.x, frame.RgbRight.Resolution.y);
            SetTexture(command, _source, kernel, "_MetricDepth", metricDepth);
            SetTexture(command, _source, kernel, "_DepthFlags", depthFlags);
            SetTexture(command, _source, kernel, "_PredDepthSupport",
                prediction.DepthSupport);
            SetTexture(command, _source, kernel, "_PredCarrierPage",
                prediction.CarrierPage);
            SetTexture(command, _source, kernel, "_PredCarrierUvNormal",
                prediction.CarrierUvNormal);
            SetTexture(command, _source, kernel, "_PredStateKey",
                prediction.StateKey);
            SetTexture(command, _source, kernel, "_DepthRayCenterLeft",
                depthRayCenterLeft);
            SetTexture(command, _source, kernel, "_RgbLeft",
                frame.RgbLeft.Texture);
            SetTexture(command, _source, kernel, "_RgbRight",
                frame.RgbRight.Texture);
            SetFrameMatrices(command, _source, frame);
        }

        private void BindInverse(CommandBuffer command, int kernel)
        {
            Set(command, _inverse, kernel, "_SigmaExactBackendGate",
                _backendGate.Buffer);
            Set(command, _inverse, kernel, "_CarrierState", _pool.State);
            Set(command, _inverse, kernel, "_TargetCarrierState", _pool.State);
            Set(command, _inverse, kernel, "_PageMetadata", _pool.Metadata);
            Set(command, _inverse, kernel, "_StreamBundles", _stream.Bundles);
            Set(command, _inverse, kernel, "_StreamSourceSegments",
                _stream.SourceHandleSegments);
            Set(command, _inverse, kernel, "_StreamAssociation",
                _stream.Association);
            Set(command, _inverse, kernel, "_RawWords", _ledger.RawWordsBuffer);
            Set(command, _inverse, kernel, "_StreamWorkItems", _stream.WorkItems);
            Set(command, _inverse, kernel, "_StreamWorkCounts", _stream.WorkCounts);
            Set(command, _inverse, kernel, "_StreamSchedulerControlRead",
                _stream.SchedulerControl);
            Set(command, _inverse, kernel, "_StreamTransactions",
                _stream.Transactions);
            Set(command, _inverse, kernel, "_ProofSamples",
                _ledger.ProofSampleBuffer);
            Set(command, _inverse, kernel, "_StreamJointBounds",
                _stream.JointBounds);
            Set(command, _inverse, kernel, "_StreamJointProvenance",
                _stream.JointProvenance);
            Set(command, _inverse, kernel, "_StreamSampleMeta",
                _stream.SampleMetadata);
            Set(command, _inverse, kernel, "_StreamSampleOutcomes",
                _stream.SampleOutcomes);
            Set(command, _inverse, kernel, "_StreamDiagnostics",
                _stream.Diagnostics);
            Set(command, _inverse, kernel, "_StreamBundleCalibration",
                _stream.BundleCalibration);
            Set(command, _inverse, kernel, "_RgbViewOperators",
                _rgbViewOperators);
            Set(command, _inverse, kernel, "_RgbViewSupportScale",
                _rgbViewSupportScale);
            SetInt(command, _inverse, "_PageCapacity", _pool.PageCapacity);
            SetInt(command, _inverse, "_TargetPageCapacity", _pool.PageCapacity);
            SetInt(command, _inverse, "_StreamSourceSegmentCapacity",
                SigmaStreamingResources.SourceHandleSegmentCapacity);
        }

        private void BindProof(CommandBuffer command, int kernel)
        {
            Set(command, _proof, kernel, "_SigmaExactBackendGate",
                _backendGate.Buffer);
            Set(command, _proof, kernel, "_ProofSamples",
                _ledger.ProofSampleBuffer);
            Set(command, _proof, kernel, "_StreamWorkItems", _stream.WorkItems);
            Set(command, _proof, kernel, "_StreamWorkCounts", _stream.WorkCounts);
            Set(command, _proof, kernel, "_StreamBundles", _stream.Bundles);
            Set(command, _proof, kernel, "_StreamSourceSegments",
                _stream.SourceHandleSegments);
            Set(command, _proof, kernel, "_PageMetadata", _pool.Metadata);
            Set(command, _proof, kernel, "_StreamTransactionsRead",
                _stream.Transactions);
            Set(command, _proof, kernel, "_StreamProofCandidatesRead",
                _stream.ProofCandidates);
            Set(command, _proof, kernel, "_StreamProofCandidateBoundsRead",
                _stream.ProofCandidateBounds);
            Set(command, _proof, kernel, "_StreamProofSortIndicesARead",
                _stream.ProofSortIndicesA);
            Set(command, _proof, kernel, "_StreamProofSortIndicesBRead",
                _stream.ProofSortIndicesB);
            Set(command, _proof, kernel, "_StreamProofKeepWordsRead",
                _stream.ProofKeepWords);
            Set(command, _proof, kernel, "_StreamSchedulerControlRead",
                _stream.SchedulerControl);
            Set(command, _proof, kernel, "_PriorCertificates",
                _ledger.CertificateBuffer);
            Set(command, _proof, kernel, "_PriorCertificateBounds",
                _ledger.CertificateBoundsBuffer);
            Set(command, _proof, kernel, "_PriorConstraintBlocks",
                _ledger.ConstraintBlockBuffer);
            Set(command, _proof, kernel, "_StreamTransactions",
                _stream.Transactions);
            Set(command, _proof, kernel, "_StreamProofClosures",
                _stream.ProofClosures);
            Set(command, _proof, kernel, "_StreamProofCandidates",
                _stream.ProofCandidates);
            Set(command, _proof, kernel, "_StreamProofCandidateBounds",
                _stream.ProofCandidateBounds);
            Set(command, _proof, kernel, "_StreamProofSortIndicesA",
                _stream.ProofSortIndicesA);
            Set(command, _proof, kernel, "_StreamProofSortIndicesB",
                _stream.ProofSortIndicesB);
            Set(command, _proof, kernel, "_StreamProofPrefix",
                _stream.ProofPrefix);
            Set(command, _proof, kernel, "_StreamProofPrefixBounds",
                _stream.ProofPrefixBounds);
            Set(command, _proof, kernel, "_StreamProofKeepWords",
                _stream.ProofKeepWords);
            Set(command, _proof, kernel, "_Certificates",
                _ledger.CertificateBuffer);
            Set(command, _proof, kernel, "_CertificateBounds",
                _ledger.CertificateBoundsBuffer);
            Set(command, _proof, kernel, "_ConstraintBlocks",
                _ledger.ConstraintBlockBuffer);
            Set(command, _proof, kernel, "_RawTiles",
                _ledger.RawHeaderBuffer);
            Set(command, _proof, kernel, "_StreamSchedulerControl",
                _stream.SchedulerControl);
            Set(command, _proof, kernel, "_StreamDiagnostics",
                _stream.Diagnostics);
            SetInt(command, _proof, "_PageCapacity", _pool.PageCapacity);
            SetInt(command, _proof, "_RawTileCapacity",
                _ledger.RawTileCapacity);
            SetInt(command, _proof, "_StreamSourceSegmentCapacity",
                SigmaStreamingResources.SourceHandleSegmentCapacity);
            SetInt(command, _proof, "_StreamProofCandidateCapacity",
                _stream.ProofCandidates.count);
        }

        private void BindTransition(CommandBuffer command, int kernel,
            int singularShift, int associatorShift)
        {
            Set(command, _transition, kernel, "_SigmaExactBackendGate",
                _backendGate.Buffer);
            Set(command, _transition, kernel, "_StreamWorkItems",
                _stream.WorkItems);
            Set(command, _transition, kernel, "_StreamWorkCounts",
                _stream.WorkCounts);
            Set(command, _transition, kernel, "_CarrierState", _pool.State);
            Set(command, _transition, kernel, "_TargetCarrierState",
                _pool.State);
            Set(command, _transition, kernel, "_PageMetadata", _pool.Metadata);
            Set(command, _transition, kernel, "_StreamManifests",
                _stream.PublicationManifests);
            Set(command, _transition, kernel, "_StreamPageVisibility",
                _stream.PageVisibility);
            Set(command, _transition, kernel, "_StreamSampleMeta",
                _stream.SampleMetadata);
            Set(command, _transition, kernel, "_StreamSampleOutcomes",
                _stream.SampleOutcomes);
            Set(command, _transition, kernel, "_StreamBundles",
                _stream.Bundles);
            Set(command, _transition, kernel, "_StreamSourceSegments",
                _stream.SourceHandleSegments);
            Set(command, _transition, kernel, "_StreamTransactions",
                _stream.Transactions);
            Set(command, _transition, kernel, "_StreamCandidateTransitions",
                _stream.CandidateTransitions);
            Set(command, _transition, kernel, "_StreamCandidateNeighbours",
                _stream.CandidateNeighbours);
            Set(command, _transition, kernel, "_StreamDiagnostics",
                _stream.Diagnostics);
            SetInt(command, _transition, "_PageCapacity", _pool.PageCapacity);
            SetInt(command, _transition, "_TargetPageCapacity",
                _pool.PageCapacity);
            SetInt(command, _transition, "_StreamManifestCapacity",
                _pool.PageCapacity);
            SetInt(command, _transition, "_StreamSourceSegmentCapacity",
                SigmaStreamingResources.SourceHandleSegmentCapacity);
            SetInt(command, _transition, "_SingularShift", singularShift);
            SetInt(command, _transition, "_AssociatorShift", associatorShift);
        }

        private void BindInitializeVisibility(CommandBuffer command, int kernel)
        {
            Set(command, _publication, kernel, "_StreamManifests",
                _stream.PublicationManifests);
            Set(command, _publication, kernel, "_StreamPageVisibility",
                _stream.PageVisibility);
            Set(command, _publication, kernel, "_PageMetadata", _pool.Metadata);
            Set(command, _publication, kernel, "_CurrentFlags",
                _pool.CurrentFlags);
            SetInt(command, _publication, "_PageCapacity", _pool.PageCapacity);
            SetInt(command, _publication, "_StreamManifestCapacity",
                _pool.PageCapacity);
        }

        private void BindDerived(CommandBuffer command, int kernel)
        {
            Set(command, _derived, kernel, "_StreamWorkItems",
                _stream.WorkItems);
            Set(command, _derived, kernel, "_StreamWorkCounts",
                _stream.WorkCounts);
            Set(command, _derived, kernel, "_StreamTransactions",
                _stream.Transactions);
            Set(command, _derived, kernel, "_StreamCandidateTransitions",
                _stream.CandidateTransitions);
            Set(command, _derived, kernel, "_StreamCandidateNeighbours",
                _stream.CandidateNeighbours);
            Set(command, _derived, kernel, "_PageMetadata", _pool.Metadata);
            Set(command, _derived, kernel, "_StreamManifests",
                _stream.PublicationManifests);
            Set(command, _derived, kernel, "_StreamPageVisibility",
                _stream.PageVisibility);
            Set(command, _derived, kernel, "_TopologyTransitionRecords",
                _topologyView.TransitionRecords);
            Set(command, _derived, kernel, "_TopologyCellFlags",
                _topologyView.CellFlags);
            Set(command, _derived, kernel, "_TopologyPageKeys",
                _topologyView.PageKeys);
            Set(command, _derived, kernel, "_ReadoutDirtyFlags",
                _pool.ReadoutDirtyFlags);
            SetInt(command, _derived, "_PageCapacity", _pool.PageCapacity);
            SetInt(command, _derived, "_TopologyPageCapacity",
                _topologyView.PageCapacity);
            SetInt(command, _derived, "_StreamManifestCapacity",
                _pool.PageCapacity);
        }

        private void BindDormant(CommandBuffer command, int kernel)
        {
            Set(command, _dormant, kernel, "_StreamWorkCounts",
                _stream.WorkCounts);
            Set(command, _dormant, kernel, "_StreamWorkItems",
                _stream.WorkItems);
            Set(command, _dormant, kernel, "_StreamTransactions",
                _stream.Transactions);
            Set(command, _dormant, kernel, "_StreamTransactionsRead",
                _stream.Transactions);
            Set(command, _dormant, kernel, "_StreamBundles",
                _stream.Bundles);
            Set(command, _dormant, kernel, "_StreamBundlesRead",
                _stream.Bundles);
            Set(command, _dormant, kernel, "_StreamProbation",
                _stream.Probation);
            Set(command, _dormant, kernel, "_StreamProbationRead",
                _stream.Probation);
            Set(command, _dormant, kernel, "_StreamSourceSegments",
                _stream.SourceHandleSegments);
            Set(command, _dormant, kernel, "_StreamSourceSegmentsRead",
                _stream.SourceHandleSegments);
            Set(command, _dormant, kernel, "_StreamAssociationOwners",
                _stream.AssociationOwners);
            Set(command, _dormant, kernel, "_StreamProofClosures",
                _stream.ProofClosures);
            Set(command, _dormant, kernel, "_StreamProofClosuresRead",
                _stream.ProofClosures);
            Set(command, _dormant, kernel, "_PageMetadata", _pool.Metadata);
            Set(command, _dormant, kernel, "_PageMetadataRead",
                _pool.Metadata);
            Set(command, _dormant, kernel, "_CurrentFlags",
                _pool.CurrentFlags);
            Set(command, _dormant, kernel, "_CurrentFlagsRead",
                _pool.CurrentFlags);
            Set(command, _dormant, kernel, "_DirtyFlags", _pool.DirtyFlags);
            Set(command, _dormant, kernel, "_DirtyFlagsRead",
                _pool.DirtyFlags);
            Set(command, _dormant, kernel, "_ReadoutDirtyFlags",
                _pool.ReadoutDirtyFlags);
            Set(command, _dormant, kernel, "_StreamSchedulerControl",
                _stream.SchedulerControl);
            Set(command, _dormant, kernel, "_StreamDiagnostics",
                _stream.Diagnostics);
            SetInt(command, _dormant, "_PageCapacity", _pool.PageCapacity);
            SetInt(command, _dormant, "_StreamSourceSegmentCapacity",
                SigmaStreamingResources.SourceHandleSegmentCapacity);
        }

        private void BindPublication(CommandBuffer command, int kernel)
        {
            Set(command, _publication, kernel, "_SigmaExactBackendGate",
                _backendGate.Buffer);
            Set(command, _publication, kernel, "_StreamWorkItems",
                _stream.WorkItems);
            Set(command, _publication, kernel, "_StreamWorkCounts",
                _stream.WorkCounts);
            Set(command, _publication, kernel, "_ConstraintBlocks",
                _ledger.ConstraintBlockBuffer);
            Set(command, _publication, kernel, "_StreamTransactions",
                _stream.Transactions);
            Set(command, _publication, kernel, "_StreamBundles",
                _stream.Bundles);
            Set(command, _publication, kernel, "_StreamSourceSegments",
                _stream.SourceHandleSegments);
            Set(command, _publication, kernel, "_StreamAssociationOwners",
                _stream.AssociationOwners);
            Set(command, _publication, kernel, "_StreamManifests",
                _stream.PublicationManifests);
            Set(command, _publication, kernel, "_StreamPageVisibility",
                _stream.PageVisibility);
            Set(command, _publication, kernel, "_PageMetadata", _pool.Metadata);
            Set(command, _publication, kernel, "_CurrentFlags",
                _pool.CurrentFlags);
            Set(command, _publication, kernel, "_DirtyFlags", _pool.DirtyFlags);
            Set(command, _publication, kernel, "_ReadoutDirtyFlags",
                _pool.ReadoutDirtyFlags);
            Set(command, _publication, kernel, "_RawLiveWords",
                _ledger.RawLiveBitmapBuffer);
            Set(command, _publication, kernel, "_StreamSchedulerControl",
                _stream.SchedulerControl);
            Set(command, _publication, kernel, "_StreamDiagnostics",
                _stream.Diagnostics);
            SetInt(command, _publication, "_PageCapacity", _pool.PageCapacity);
            SetInt(command, _publication, "_StreamManifestCapacity",
                _pool.PageCapacity);
            SetInt(command, _publication, "_StreamSourceSegmentCapacity",
                SigmaStreamingResources.SourceHandleSegmentCapacity);
            SetInt(command, _publication, "_RawTileCapacity",
                _ledger.RawTileCapacity);
        }

        private void DispatchIndirect(CommandBuffer command,
            ComputeShader shader, int kernel, SigmaStreamOpcode opcode)
        {
            command.DispatchCompute(shader, kernel, _stream.DispatchArguments,
                checked((uint)opcode * 3u * sizeof(uint)));
        }

        private static void SetFrameMatrices(CommandBuffer command,
            ComputeShader shader, StereoRigFrameLease source)
        {
            Matrix4x4 leftWorld = PoseMatrix(source.DepthLeft.WorldFromCamera);
            Matrix4x4 rightWorld = PoseMatrix(source.DepthRight.WorldFromCamera);
            command.SetComputeMatrixParam(shader, "_WorldFromOpticalLeft",
                leftWorld);
            command.SetComputeMatrixParam(shader, "_OpticalFromWorldLeft",
                leftWorld.inverse);
            command.SetComputeMatrixParam(shader, "_OpticalFromWorldRight",
                rightWorld.inverse);
            command.SetComputeVectorParam(shader, "_DepthIntrinsicsLeft",
                Intrinsics(source.DepthLeft.Intrinsics));
            command.SetComputeVectorParam(shader, "_DepthIntrinsicsRight",
                Intrinsics(source.DepthRight.Intrinsics));
            command.SetComputeMatrixParam(shader, "_RgbOpticalFromWorldLeft",
                PoseMatrix(source.RgbLeft.WorldFromCamera).inverse);
            command.SetComputeMatrixParam(shader, "_RgbOpticalFromWorldRight",
                PoseMatrix(source.RgbRight.WorldFromCamera).inverse);
            command.SetComputeVectorParam(shader, "_RgbIntrinsicsLeft",
                Intrinsics(source.RgbLeft.Intrinsics));
            command.SetComputeVectorParam(shader, "_RgbIntrinsicsRight",
                Intrinsics(source.RgbRight.Intrinsics));
        }

        private static ComputeShader RequireShader(string resource)
        {
            ComputeShader shader = UnityEngine.Resources.Load<ComputeShader>(
                resource);
            return shader != null ? shader : throw new InvalidOperationException(
                $"Required Sigma streaming compute is missing: {resource}.");
        }

        private static void RequireCommand(CommandBuffer command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
        }

        private static void Set(CommandBuffer command, ComputeShader shader,
            int kernel, string name, GraphicsBuffer buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(name);
            command.SetComputeBufferParam(shader, kernel, name, buffer);
        }

        private static void SetTexture(CommandBuffer command,
            ComputeShader shader, int kernel, string name, Texture texture)
        {
            if (texture == null)
                throw new ArgumentNullException(name);
            command.SetComputeTextureParam(shader, kernel, name, texture);
        }

        private static void SetInt(CommandBuffer command, ComputeShader shader,
            string name, int value) => command.SetComputeIntParam(shader, name,
                value);

        private static void SetUInt(CommandBuffer command, ComputeShader shader,
            string name, uint value) => command.SetComputeIntParam(shader, name,
                unchecked((int)value));

        private static Matrix4x4 PoseMatrix(Pose pose) => Matrix4x4.TRS(
            pose.position, pose.rotation, Vector3.one);

        private static Vector4 Intrinsics(RigIntrinsics intrinsics) => new(
            intrinsics.FocalLength.x, intrinsics.FocalLength.y,
            intrinsics.PrincipalPoint.x, intrinsics.PrincipalPoint.y);

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);
    }
}
