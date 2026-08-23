using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Execution-only storage for bounded S4-08.3 streaming transactions.  These
    /// buffers contain scheduling cursors, copied observation payload references
    /// and disposable association state; none of them is a physical world beside
    /// the carrier.  Every allocation is fixed at initialization and validated
    /// against the Vulkan binding limit before canonical mutation is enabled.
    /// </summary>
    internal sealed class SigmaStreamingResources : IDisposable
    {
        internal const int AssociationSlots =
            SigmaGeneratedStreaming.TransactionCapacity;
        internal const int SamplesPerAssociation = SigmaCarrier.SamplesPerPage;
        internal const int WorkItemsPerOpcode = 64;
        internal const int SchedulerControlWords = 32;
        internal const int RayEpochWordsPerBundle = 4;
        internal const int OutcomeWordsPerSample = 1;
        internal const int CoordinatesPerSample = SigmaS16.LaneCount;
        internal const int SourceHandleSegmentCapacity =
            (SigmaGeneratedStreaming.BundleCapacity +
                SigmaGeneratedStreaming.SourceHandleWindowCapacity - 1) /
                SigmaGeneratedStreaming.SourceHandleWindowCapacity +
            SigmaGeneratedStreaming.TransactionCapacity;
        internal const int ProofResidentCandidateWindow =
            SigmaGeneratedStreaming.BundleCapacity *
                SigmaGeneratedStreaming.ProofSourceClassCount +
            SigmaConstraintLedger.CertificatesPerBlock + 1;

        private bool _disposed;

        internal SigmaStreamingResources(int pageCapacity)
        {
            ValidateGeneratedAbi();
            if (pageCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(pageCapacity));
            long bindingLimit = SystemInfo.maxGraphicsBufferSize;
            if (bindingLimit <= 0L)
                throw new InvalidOperationException(
                    "The runtime did not expose a valid graphics-buffer limit.");

            Transactions = CreateStructured(
                SigmaGeneratedStreaming.TransactionCapacity,
                SigmaGeneratedStreaming.TransactionStride,
                "Sigma streaming transactions", bindingLimit);
            Bundles = CreateStructured(SigmaGeneratedStreaming.BundleCapacity,
                SigmaGeneratedStreaming.BundleStride,
                "Sigma sealed source bundles", bindingLimit);
            Probation = CreateStructured(SigmaGeneratedStreaming.BundleCapacity,
                SigmaGeneratedStreaming.ProbationStride,
                "Sigma null-contact probation", bindingLimit);
            SourceHandleSegments = CreateStructured(
                SourceHandleSegmentCapacity,
                SigmaGeneratedStreaming.SourceHandleSegmentStride,
                "Sigma generation-safe source handle segments", bindingLimit);
            SourceHandleFreeWords = CreateStructured(
                (SourceHandleSegmentCapacity + 31) / 32, sizeof(uint),
                "Sigma source handle segment free bitmap", bindingLimit);
            BundleCalibration = CreateStructured(checked(
                    SigmaGeneratedStreaming.BundleCapacity *
                    SigmaGeneratedStreaming.CalibrationQ48ValuesPerBundle),
                sizeof(uint) * 2, "Sigma owned bundle calibration Q48",
                bindingLimit);
            BundleRayEpoch = CreateStructured(checked(
                    SigmaGeneratedStreaming.BundleCapacity *
                    RayEpochWordsPerBundle), sizeof(uint),
                "Sigma immutable ray epoch pins", bindingLimit);
            Association = CreateStructured(checked(AssociationSlots *
                    SamplesPerAssociation),
                SigmaGeneratedStreaming.AssociationSampleStride,
                "Sigma active prediction association", bindingLimit);
            AssociationOwners = CreateStructured(AssociationSlots,
                sizeof(uint) * 4, "Sigma association slot owners", bindingLimit);
            SampleOutcomes = CreateStructured(checked(AssociationSlots *
                    SamplesPerAssociation), sizeof(uint) * OutcomeWordsPerSample,
                "Sigma exact sample outcomes", bindingLimit);
            JointBounds = CreateStructured(checked(AssociationSlots *
                    SamplesPerAssociation * CoordinatesPerSample),
                sizeof(uint) * 4, "Sigma streaming joint Q48 bounds",
                bindingLimit);
            JointProvenance = CreateStructured(checked(AssociationSlots *
                    SamplesPerAssociation * CoordinatesPerSample),
                sizeof(uint) * 4, "Sigma streaming joint provenance",
                bindingLimit);
            SampleMetadata = CreateStructured(checked(AssociationSlots *
                    SamplesPerAssociation), sizeof(uint) * 4,
                "Sigma streaming sample metadata", bindingLimit);
            ProofClosures = CreateStructured(
                SigmaGeneratedStreaming.TransactionCapacity,
                SigmaGeneratedStreaming.ProofClosureStride,
                "Sigma persistent proof closure state", bindingLimit);
            ProofCandidates = CreateStructured(ProofResidentCandidateWindow,
                SigmaGeneratedStreaming.ProofCandidateStride,
                "Sigma lossless resident proof candidate journal", bindingLimit);
            ProofCandidateBounds = CreateStructured(checked(
                    ProofResidentCandidateWindow * CoordinatesPerSample),
                sizeof(uint) * 4,
                "Sigma proof candidate Q48 bounds", bindingLimit);
            ProofSortIndicesA = CreateStructured(ProofResidentCandidateWindow,
                sizeof(uint), "Sigma proof stable sort indices A", bindingLimit);
            ProofSortIndicesB = CreateStructured(ProofResidentCandidateWindow,
                sizeof(uint), "Sigma proof stable sort indices B", bindingLimit);
            ProofPrefix = CreateStructured(ProofResidentCandidateWindow,
                SigmaGeneratedStreaming.ProofPrefixStride,
                "Sigma proof prefix gate state", bindingLimit);
            ProofPrefixBounds = CreateStructured(checked(
                    ProofResidentCandidateWindow * CoordinatesPerSample),
                sizeof(uint) * 4, "Sigma proof prefix Q48 meets", bindingLimit);
            ProofKeepWords = CreateStructured(
                (ProofResidentCandidateWindow + 31) / 32, sizeof(uint),
                "Sigma proof fixed-point keep bitmap", bindingLimit);
            PublicationManifests = CreateStructured(pageCapacity,
                SigmaGeneratedStreaming.PublicationManifestStride,
                "Sigma publication manifests", bindingLimit);
            PageVisibility = CreateStructured(pageCapacity,
                SigmaGeneratedStreaming.PageVisibilityStride,
                "Sigma page manifest visibility", bindingLimit);
            CandidateTransitions = CreateStructured(checked(
                    SigmaGeneratedStreaming.TransactionCapacity *
                    SigmaCarrier.SamplesPerPage * 2), sizeof(uint) * 4,
                "Sigma canonical candidate transition closure", bindingLimit);
            CandidateNeighbours = CreateStructured(
                SigmaGeneratedStreaming.TransactionCapacity,
                sizeof(uint) * 4,
                "Sigma candidate logical neighbour slots", bindingLimit);

            WorkItems = CreateStructured(checked(
                    SigmaGeneratedStreaming.OpcodeCount * WorkItemsPerOpcode),
                SigmaGeneratedStreaming.WorkItemStride,
                "Sigma streaming opcode work lists", bindingLimit);
            WorkCounts = CreateStructured(SigmaGeneratedStreaming.OpcodeCount,
                sizeof(uint), "Sigma streaming opcode counts", bindingLimit);
            const string dispatchArgumentsName =
                "Sigma streaming indirect dispatch arguments";
            DispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments,
                checked(SigmaGeneratedStreaming.OpcodeCount * 3), sizeof(uint))
            {
                name = dispatchArgumentsName
            };
            ValidateBinding(DispatchArguments.count, DispatchArguments.stride,
                dispatchArgumentsName, bindingLimit);
            SchedulerControl = CreateStructured(SchedulerControlWords,
                sizeof(uint), "Sigma streaming scheduler control", bindingLimit);
            KernelTokenCosts = CreateStructured(
                SigmaGeneratedStreaming.OpcodeCount, sizeof(uint),
                "Sigma generated kernel token costs", bindingLimit);
            KernelBudgetClasses = CreateStructured(
                SigmaGeneratedStreaming.OpcodeCount, sizeof(uint),
                "Sigma generated kernel budget classes", bindingLimit);
            Diagnostics = CreateStructured(1,
                SigmaGeneratedStreaming.DiagnosticStride,
                "Sigma streaming diagnostics", bindingLimit);

            Transactions.SetData(new SigmaTransactionGpu[
                SigmaGeneratedStreaming.TransactionCapacity]);
            Bundles.SetData(new SigmaSealedSourceBundleGpu[
                SigmaGeneratedStreaming.BundleCapacity]);
            Probation.SetData(new SigmaProbationGpu[
                SigmaGeneratedStreaming.BundleCapacity]);
            SourceHandleSegments.SetData(new SigmaSourceHandleSegmentGpu[
                SourceHandleSegmentCapacity]);
            SourceHandleFreeWords.SetData(new uint[
                (SourceHandleSegmentCapacity + 31) / 32]);
            AssociationOwners.SetData(new SigmaStreamUInt4Gpu[
                AssociationSlots]);
            WorkCounts.SetData(new uint[SigmaGeneratedStreaming.OpcodeCount]);
            SchedulerControl.SetData(new uint[SchedulerControlWords]);
            KernelTokenCosts.SetData(SigmaGeneratedStreaming.KernelTokenCost);
            KernelBudgetClasses.SetData(
                SigmaGeneratedStreaming.KernelBudgetClass);
            Diagnostics.SetData(new SigmaStreamDiagnosticGpu[1]);
            ProofClosures.SetData(new SigmaProofClosureGpu[
                SigmaGeneratedStreaming.TransactionCapacity]);
            PublicationManifests.SetData(new SigmaPublicationManifestGpu[
                pageCapacity]);
            PageVisibility.SetData(new SigmaPageVisibilityGpu[pageCapacity]);

            var dispatch = new uint[checked(
                SigmaGeneratedStreaming.OpcodeCount * 3)];
            for (int opcode = 0; opcode <
                SigmaGeneratedStreaming.OpcodeCount; ++opcode)
            {
                dispatch[opcode * 3 + 1] = 1u;
                dispatch[opcode * 3 + 2] = 1u;
            }
            DispatchArguments.SetData(dispatch);

            OwnedBytes = SumBytes(Transactions, Bundles, Probation,
                SourceHandleSegments, SourceHandleFreeWords,
                BundleCalibration, BundleRayEpoch, Association,
                AssociationOwners, SampleOutcomes, JointBounds,
                JointProvenance, SampleMetadata, ProofClosures,
                ProofCandidates, ProofCandidateBounds, ProofSortIndicesA,
                ProofSortIndicesB, ProofPrefix, ProofPrefixBounds,
                ProofKeepWords, PublicationManifests, PageVisibility,
                CandidateTransitions, CandidateNeighbours,
                WorkItems, WorkCounts,
                DispatchArguments, SchedulerControl, KernelTokenCosts,
                KernelBudgetClasses, Diagnostics);
        }

        internal GraphicsBuffer Transactions { get; private set; }
        internal GraphicsBuffer Bundles { get; private set; }
        internal GraphicsBuffer Probation { get; private set; }
        internal GraphicsBuffer SourceHandleSegments { get; private set; }
        internal GraphicsBuffer SourceHandleFreeWords { get; private set; }
        internal GraphicsBuffer BundleCalibration { get; private set; }
        internal GraphicsBuffer BundleRayEpoch { get; private set; }
        internal GraphicsBuffer Association { get; private set; }
        internal GraphicsBuffer AssociationOwners { get; private set; }
        internal GraphicsBuffer SampleOutcomes { get; private set; }
        internal GraphicsBuffer JointBounds { get; private set; }
        internal GraphicsBuffer JointProvenance { get; private set; }
        internal GraphicsBuffer SampleMetadata { get; private set; }
        internal GraphicsBuffer ProofClosures { get; private set; }
        internal GraphicsBuffer ProofCandidates { get; private set; }
        internal GraphicsBuffer ProofCandidateBounds { get; private set; }
        internal GraphicsBuffer ProofSortIndicesA { get; private set; }
        internal GraphicsBuffer ProofSortIndicesB { get; private set; }
        internal GraphicsBuffer ProofPrefix { get; private set; }
        internal GraphicsBuffer ProofPrefixBounds { get; private set; }
        internal GraphicsBuffer ProofKeepWords { get; private set; }
        internal GraphicsBuffer PublicationManifests { get; private set; }
        internal GraphicsBuffer PageVisibility { get; private set; }
        internal GraphicsBuffer CandidateTransitions { get; private set; }
        internal GraphicsBuffer CandidateNeighbours { get; private set; }
        internal GraphicsBuffer WorkItems { get; private set; }
        internal GraphicsBuffer WorkCounts { get; private set; }
        internal GraphicsBuffer DispatchArguments { get; private set; }
        internal GraphicsBuffer SchedulerControl { get; private set; }
        internal GraphicsBuffer KernelTokenCosts { get; private set; }
        internal GraphicsBuffer KernelBudgetClasses { get; private set; }
        internal GraphicsBuffer Diagnostics { get; private set; }
        internal long OwnedBytes { get; }

        internal long ActiveAssociationBytes => BufferBytes(Association);

        internal static void ValidateTransientBudget(long proofBytes,
            long rawBytes, long additionalBytes)
        {
            const long limit = 112L * 1024L * 1024L;
            long total = checked(checked(proofBytes + rawBytes) +
                additionalBytes);
            if (total > limit)
                throw new InvalidOperationException(
                    $"Sigma inverse transient allocation {total} bytes exceeds " +
                    $"the {limit}-byte section-34 budget.");
        }

        private static void ValidateGeneratedAbi()
        {
            ValidateStride<SigmaTransactionGpu>(
                SigmaGeneratedStreaming.TransactionStride);
            ValidateStride<SigmaSealedSourceBundleGpu>(
                SigmaGeneratedStreaming.BundleStride);
            ValidateStride<SigmaProbationGpu>(
                SigmaGeneratedStreaming.ProbationStride);
            ValidateStride<SigmaSourceHandleSegmentGpu>(
                SigmaGeneratedStreaming.SourceHandleSegmentStride);
            ValidateStride<SigmaProofClosureGpu>(
                SigmaGeneratedStreaming.ProofClosureStride);
            ValidateStride<SigmaProofCandidateGpu>(
                SigmaGeneratedStreaming.ProofCandidateStride);
            ValidateStride<SigmaProofPrefixGpu>(
                SigmaGeneratedStreaming.ProofPrefixStride);
            ValidateStride<SigmaAssociationSampleGpu>(
                SigmaGeneratedStreaming.AssociationSampleStride);
            ValidateStride<SigmaPublicationManifestGpu>(
                SigmaGeneratedStreaming.PublicationManifestStride);
            ValidateStride<SigmaPageVisibilityGpu>(
                SigmaGeneratedStreaming.PageVisibilityStride);
            ValidateStride<SigmaStreamWorkItemGpu>(
                SigmaGeneratedStreaming.WorkItemStride);
            ValidateStride<SigmaStreamDiagnosticGpu>(
                SigmaGeneratedStreaming.DiagnosticStride);

            int opcodeCount = SigmaGeneratedStreaming.OpcodeCount;
            if (SigmaGeneratedStreaming.KernelTokenCost.Length != opcodeCount ||
                SigmaGeneratedStreaming.KernelBudgetClass.Length != opcodeCount ||
                SigmaGeneratedStreaming.KernelThreadCount.Length != opcodeCount ||
                SigmaGeneratedStreaming.KernelBytesRead.Length != opcodeCount ||
                SigmaGeneratedStreaming.KernelBytesWritten.Length != opcodeCount ||
                SigmaGeneratedStreaming.KernelScratchBytes.Length != opcodeCount ||
                SigmaGeneratedStreaming.KernelBarrierCount.Length != opcodeCount ||
                SigmaGeneratedStreaming.KernelWitnessCount.Length != opcodeCount ||
                SigmaGeneratedStreaming.KernelMaximumRecords.Length != opcodeCount)
                throw new InvalidOperationException(
                    "Generated Sigma streaming cost tables have inconsistent " +
                    "opcode lengths.");
        }

        private static void ValidateStride<T>(int expected) where T : struct
        {
            int actual = Marshal.SizeOf<T>();
            if (actual != expected)
                throw new InvalidOperationException(
                    $"Sigma streaming ABI stride mismatch for {typeof(T).Name}: " +
                    $"C#={actual}, generated={expected}.");
        }

        private static GraphicsBuffer CreateStructured(int count, int stride,
            string name, long bindingLimit)
        {
            ValidateBinding(count, stride, name, bindingLimit);
            return new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                Math.Max(1, count), stride) { name = name };
        }

        private static void ValidateBinding(int count, int stride, string name,
            long bindingLimit)
        {
            if (count <= 0 || stride <= 0)
                throw new ArgumentOutOfRangeException(name,
                    "Sigma buffers require positive count and stride.");
            long bytes = checked((long)count * stride);
            if (bytes > bindingLimit)
                throw new InvalidOperationException(
                    $"{name} requires {bytes} bytes, above the runtime Vulkan " +
                    $"binding range {bindingLimit}.");
        }

        private static long BufferBytes(GraphicsBuffer buffer) =>
            buffer == null ? 0L : checked((long)buffer.count * buffer.stride);

        private static long SumBytes(params GraphicsBuffer[] buffers)
        {
            long total = 0L;
            for (int index = 0; index < buffers.Length; ++index)
                total = checked(total + BufferBytes(buffers[index]));
            return total;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            DisposeBuffer(Transactions);
            DisposeBuffer(Bundles);
            DisposeBuffer(Probation);
            DisposeBuffer(SourceHandleSegments);
            DisposeBuffer(SourceHandleFreeWords);
            DisposeBuffer(BundleCalibration);
            DisposeBuffer(BundleRayEpoch);
            DisposeBuffer(Association);
            DisposeBuffer(AssociationOwners);
            DisposeBuffer(SampleOutcomes);
            DisposeBuffer(JointBounds);
            DisposeBuffer(JointProvenance);
            DisposeBuffer(SampleMetadata);
            DisposeBuffer(ProofClosures);
            DisposeBuffer(ProofCandidates);
            DisposeBuffer(ProofCandidateBounds);
            DisposeBuffer(ProofSortIndicesA);
            DisposeBuffer(ProofSortIndicesB);
            DisposeBuffer(ProofPrefix);
            DisposeBuffer(ProofPrefixBounds);
            DisposeBuffer(ProofKeepWords);
            DisposeBuffer(PublicationManifests);
            DisposeBuffer(PageVisibility);
            DisposeBuffer(CandidateTransitions);
            DisposeBuffer(CandidateNeighbours);
            DisposeBuffer(WorkItems);
            DisposeBuffer(WorkCounts);
            DisposeBuffer(DispatchArguments);
            DisposeBuffer(SchedulerControl);
            DisposeBuffer(KernelTokenCosts);
            DisposeBuffer(KernelBudgetClasses);
            DisposeBuffer(Diagnostics);
            Transactions = null;
            Bundles = null;
            Probation = null;
            SourceHandleSegments = null;
            SourceHandleFreeWords = null;
            BundleCalibration = null;
            BundleRayEpoch = null;
            Association = null;
            AssociationOwners = null;
            SampleOutcomes = null;
            JointBounds = null;
            JointProvenance = null;
            SampleMetadata = null;
            ProofClosures = null;
            ProofCandidates = null;
            ProofCandidateBounds = null;
            ProofSortIndicesA = null;
            ProofSortIndicesB = null;
            ProofPrefix = null;
            ProofPrefixBounds = null;
            ProofKeepWords = null;
            PublicationManifests = null;
            PageVisibility = null;
            CandidateTransitions = null;
            CandidateNeighbours = null;
            WorkItems = null;
            WorkCounts = null;
            DispatchArguments = null;
            SchedulerControl = null;
            KernelTokenCosts = null;
            KernelBudgetClasses = null;
            Diagnostics = null;
        }

        private static void DisposeBuffer(GraphicsBuffer buffer) =>
            buffer?.Dispose();
    }
}
