using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// One bounded scratch set for a terminally-owned native observation. None of
    /// these buffers owns physical identity; only a published carrier delta can
    /// mutate Psi. Cardinality changes workgroups, never buffer-specific dispatch
    /// sequences.
    /// </summary>
    internal sealed class SigmaNativeFrameSlotResources : IDisposable
    {
        internal const int FreshBranchCapacity = 4;
        internal const int LiveFreshBranchCount = 1;
        internal const int RelationCapacity = LiveFreshBranchCount * 2;
        internal const int StatesPerSlot = LiveFreshBranchCount + 3;
        internal const int RepresentationDeltaCapacity = 4;
        internal const int CertificateWordCount = 16;
        internal const int FootprintEvidenceWordCount = 52;
        internal const int BoundaryReceiptWordCount = 6;
        internal const int TileSize = 16;
        internal const int TileFootprintCapacity = TileSize * TileSize;
        internal const int TileHeaderWordCount = 2;
        internal const int TileFootprintReceiptWordCount = 8;
        internal const int TileSupportSummaryWordCount = 2;
        internal const int TileComponentSummaryWordCount = 2;

        internal SigmaNativeFrameSlotResources(int index)
            : this(index, Vector2Int.one)
        {
        }

        internal SigmaNativeFrameSlotResources(int index,
            Vector2Int resolution)
        {
            if (resolution.x <= 0 || resolution.y <= 0)
                throw new ArgumentOutOfRangeException(nameof(resolution));
            FootprintCapacity = checked(resolution.x * resolution.y);
            BoundaryCapacity = checked((resolution.x - 1) * resolution.y +
                resolution.x * (resolution.y - 1));
            FootprintStateOffset = StatesPerSlot * SigmaS16.LaneCount;
            FootprintCertificateOffset =
                (RepresentationDeltaCapacity + 1) * CertificateWordCount;
            BoundaryScratchOffset = checked(FootprintCapacity *
                FootprintEvidenceWordCount);
            TileCountX = checked((resolution.x + TileSize - 1) / TileSize);
            TileCountY = checked((resolution.y + TileSize - 1) / TileSize);
            TileCapacity = checked(TileCountX * TileCountY);
            TileHeaderScratchOffset = checked(BoundaryScratchOffset +
                BoundaryCapacity * BoundaryReceiptWordCount);
            TileFootprintScratchOffset = checked(TileHeaderScratchOffset +
                TileCapacity * TileHeaderWordCount);
            TileSupportSummaryScratchOffset = checked(
                TileFootprintScratchOffset + FootprintCapacity *
                    TileFootprintReceiptWordCount);
            TileComponentSummaryScratchOffset = checked(
                TileSupportSummaryScratchOffset + TileCapacity *
                    TileFootprintCapacity * TileSupportSummaryWordCount);
            int closeScratchCount = checked(TileComponentSummaryScratchOffset +
                TileCapacity * TileFootprintCapacity *
                    TileComponentSummaryWordCount);
            NativeFrame = Buffer<SigmaNativeFrameGpu>(1,
                SigmaGeneratedFrame.NativeFrameStride, $"native frame {index}");
            // Index zero remains the accepted N3 terminal consumer during CUT A;
            // the complete disposable footprint domain starts at index one.
            Observation = Buffer<SigmaNativeObservationGpu>(
                checked(FootprintCapacity + 1),
                SigmaGeneratedFrame.NativeObservationStride,
                $"native observation {index}");
            CloseScratch = UInt2(closeScratchCount,
                $"native close scratch {index}");
            States = UInt2(checked(FootprintStateOffset + FootprintCapacity *
                SigmaS16.LaneCount), $"native states {index}");

            RelationInputs = CreateUInt4Buffer(RelationCapacity,
                $"native relation inputs {index}");
            RelationPlans = CreateUInt4Buffer(RelationCapacity,
                $"native relation plans {index}");
            RelationNearIntervals = CreateUInt4Buffer(RelationCapacity,
                $"native relation near intervals {index}");
            RelationResults = CreateUInt4Buffer(RelationCapacity,
                $"native relation results {index}");
            RelationFactors = CreateUInt4Buffer(RelationCapacity,
                $"native relation factors {index}");
            RelationHashes = CreateUInt4Buffer(RelationCapacity,
                $"native relation hashes {index}");
            RelationNorms = CreateUInt4Buffer(RelationCapacity * 4,
                $"native relation norms {index}");

            BranchHeaders = CreateUInt4Buffer(FreshBranchCapacity + 1,
                $"native branch headers {index}");
            BranchSupports = UInt2(FreshBranchCapacity + 1,
                $"native branch supports {index}");
            BranchPredictions = CreateUInt4Buffer(FreshBranchCapacity * 4,
                $"native branch predictions {index}");

            StateDelta = Buffer<SigmaNativeStateDeltaGpu>(
                RepresentationDeltaCapacity,
                SigmaGeneratedFrame.NativeStateDeltaStride,
                $"native state delta {index}");
            GaugeDelta = Buffer<SigmaNativeGaugeDeltaGpu>(
                RepresentationDeltaCapacity,
                SigmaGeneratedFrame.NativeGaugeDeltaStride,
                $"native gauge delta {index}");
            LocalityCertificateWords = CreateUInt4Buffer(
                checked(FootprintCertificateOffset + FootprintCapacity *
                    CertificateWordCount),
                $"native locality certificates {index}");
            Unresolved = Buffer<SigmaUnresolvedConstraintGpu>(1,
                SigmaGeneratedFrame.UnresolvedConstraintStride,
                $"native unresolved constraint {index}");
            Revisions = Buffer<SigmaNativeFieldRevisionGpu>(2,
                SigmaGeneratedFrame.NativeFieldRevisionStride,
                $"native revisions {index}");
            Counters = CreateUInt4Buffer(4, $"native counters {index}");

            InitializeRelationDescriptors();
        }

        internal GraphicsBuffer NativeFrame { get; }
        internal GraphicsBuffer Observation { get; }
        internal GraphicsBuffer CloseScratch { get; }
        internal int FootprintCapacity { get; }
        internal int BoundaryCapacity { get; }
        internal int FootprintStateOffset { get; }
        internal int FootprintCertificateOffset { get; }
        internal int BoundaryScratchOffset { get; }
        internal int TileCountX { get; }
        internal int TileCountY { get; }
        internal int TileCapacity { get; }
        internal int TileHeaderScratchOffset { get; }
        internal int TileFootprintScratchOffset { get; }
        internal int TileSupportSummaryScratchOffset { get; }
        internal int TileComponentSummaryScratchOffset { get; }
        internal GraphicsBuffer States { get; }
        internal GraphicsBuffer RelationInputs { get; }
        internal GraphicsBuffer RelationPlans { get; }
        internal GraphicsBuffer RelationNearIntervals { get; }
        internal GraphicsBuffer RelationResults { get; }
        internal GraphicsBuffer RelationFactors { get; }
        internal GraphicsBuffer RelationHashes { get; }
        internal GraphicsBuffer RelationNorms { get; }
        internal GraphicsBuffer BranchHeaders { get; }
        internal GraphicsBuffer BranchSupports { get; }
        internal GraphicsBuffer BranchPredictions { get; }
        internal GraphicsBuffer StateDelta { get; }
        internal GraphicsBuffer GaugeDelta { get; }
        internal GraphicsBuffer LocalityCertificateWords { get; }
        internal GraphicsBuffer Unresolved { get; }
        internal GraphicsBuffer Revisions { get; }
        internal GraphicsBuffer Counters { get; }
        internal bool Leased { get; set; }

        internal long OwnedBytes =>
            Bytes(NativeFrame) + Bytes(Observation) + Bytes(CloseScratch) +
            Bytes(States) +
            Bytes(RelationInputs) + Bytes(RelationPlans) +
            Bytes(RelationNearIntervals) + Bytes(RelationResults) +
            Bytes(RelationFactors) + Bytes(RelationHashes) +
            Bytes(RelationNorms) + Bytes(BranchHeaders) +
            Bytes(BranchSupports) + Bytes(BranchPredictions) + Bytes(StateDelta) +
            Bytes(GaugeDelta) + Bytes(LocalityCertificateWords) +
            Bytes(Unresolved) + Bytes(Revisions) +
            Bytes(Counters);

        public void Dispose()
        {
            NativeFrame.Dispose();
            Observation.Dispose();
            CloseScratch.Dispose();
            States.Dispose();
            RelationInputs.Dispose();
            RelationPlans.Dispose();
            RelationNearIntervals.Dispose();
            RelationResults.Dispose();
            RelationFactors.Dispose();
            RelationHashes.Dispose();
            RelationNorms.Dispose();
            BranchHeaders.Dispose();
            BranchSupports.Dispose();
            BranchPredictions.Dispose();
            StateDelta.Dispose();
            GaugeDelta.Dispose();
            LocalityCertificateWords.Dispose();
            Unresolved.Dispose();
            Revisions.Dispose();
            Counters.Dispose();
        }

        private void InitializeRelationDescriptors()
        {
            int admissionOffset = LiveFreshBranchCount * SigmaS16.LaneCount;
            int zeroOffset = (LiveFreshBranchCount + 1) * SigmaS16.LaneCount;
            int priorOffset = (LiveFreshBranchCount + 2) * SigmaS16.LaneCount;
            var inputs = new UInt4[RelationCapacity];
            var plans = new UInt4[RelationCapacity];
            var near = new UInt4[RelationCapacity];
            for (int branch = 0; branch < LiveFreshBranchCount; ++branch)
            {
                int boundary = branch;
                int transport = LiveFreshBranchCount + branch;
                inputs[boundary].X = checked((uint)(branch *
                    SigmaS16.LaneCount));
                plans[boundary].X = checked((uint)zeroOffset);
                plans[boundary].Y = checked((uint)zeroOffset);
                inputs[transport].X = checked((uint)admissionOffset);
                plans[transport].X = checked((uint)priorOffset);
                plans[transport].Y = checked((uint)zeroOffset);
                // Empty calibrated-near interval. Exact ZD and exact-zero
                // relation classes remain distinct from near-singular.
                near[boundary] = new UInt4 { X = 1u };
                near[transport] = new UInt4 { X = 1u };
            }
            RelationInputs.SetData(inputs);
            RelationPlans.SetData(plans);
            RelationNearIntervals.SetData(near);
        }

        private static GraphicsBuffer CreateUInt4Buffer(int count, string name) =>
            new(GraphicsBuffer.Target.Structured, Math.Max(1, count),
                sizeof(uint) * 4) { name = name };

        private static GraphicsBuffer UInt2(int count, string name) =>
            new(GraphicsBuffer.Target.Structured, Math.Max(1, count),
                sizeof(uint) * 2) { name = name };

        private static GraphicsBuffer Buffer<T>(int count, int stride,
            string name) where T : struct
        {
            if (Marshal.SizeOf<T>() != stride)
                throw new InvalidOperationException($"Generated ABI stride " +
                    $"mismatch for {typeof(T).Name}.");
            return new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                Math.Max(1, count), stride) { name = name };
        }

        private static long Bytes(GraphicsBuffer value) =>
            checked((long)value.count * value.stride);

        [StructLayout(LayoutKind.Sequential)]
        private struct UInt4
        {
            internal uint X;
            internal uint Y;
            internal uint Z;
            internal uint W;
        }
    }

    internal sealed class SigmaNativeFrameResources : IDisposable
    {
        private readonly SigmaNativeFrameSlotResources[] _slots;

        internal SigmaNativeFrameResources(Vector2Int resolution, int capacity)
        {
            if (resolution.x <= 0 || resolution.y <= 0)
                throw new ArgumentOutOfRangeException(nameof(resolution));
            Resolution = resolution;
            FrameCapacity = Mathf.Clamp(capacity, 3, 8);
            _slots = new SigmaNativeFrameSlotResources[FrameCapacity];
            for (int index = 0; index < _slots.Length; ++index)
                _slots[index] = new SigmaNativeFrameSlotResources(index,
                    resolution);
        }

        internal Vector2Int Resolution { get; }
        internal int FrameCapacity { get; }
        internal long OwnedBytes
        {
            get
            {
                long result = 0L;
                foreach (SigmaNativeFrameSlotResources slot in _slots)
                    result = checked(result + slot.OwnedBytes);
                return result;
            }
        }

        internal bool TryLease(out int index,
            out SigmaNativeFrameSlotResources resources)
        {
            for (index = 0; index < _slots.Length; ++index)
            {
                if (_slots[index].Leased)
                    continue;
                resources = _slots[index];
                resources.Leased = true;
                return true;
            }
            index = -1;
            resources = null;
            return false;
        }

        internal void Release(int index)
        {
            if ((uint)index >= (uint)_slots.Length || !_slots[index].Leased)
                throw new InvalidOperationException(
                    "Native frame scratch release is not owned.");
            _slots[index].Leased = false;
        }

        public void Dispose()
        {
            foreach (SigmaNativeFrameSlotResources slot in _slots)
                slot.Dispose();
        }
    }
}
