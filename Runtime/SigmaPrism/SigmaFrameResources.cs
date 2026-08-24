using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Genesis.RoomScan.SigmaPrism
{
    internal enum SigmaFrameMemoryProfile
    {
        Minimum = 1024,
        HighThroughput = 2048,
        AuditedMaximum = 3072,
    }

    internal readonly struct SigmaFrameBufferSegment
    {
        internal SigmaFrameBufferSegment(long firstRecord, int recordCount,
            GraphicsBuffer buffer)
        {
            FirstRecord = firstRecord;
            RecordCount = recordCount;
            Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        }

        internal long FirstRecord { get; }
        internal int RecordCount { get; }
        internal GraphicsBuffer Buffer { get; }
    }

    /// <summary>
    /// One logical SoA buffer split only to respect the Vulkan storage-buffer
    /// range. Segment boundaries are execution metadata and never truncate the
    /// logical record stream.
    /// </summary>
    internal sealed class SigmaFrameSegmentedBuffer : IDisposable
    {
        private readonly List<SigmaFrameBufferSegment> _segments = new();
        private readonly int _recordsPerSegment;
        private readonly int _stride;
        private readonly string _name;
        private bool _disposed;

        internal SigmaFrameSegmentedBuffer(int stride, int recordsPerSegment,
            string name)
        {
            if (stride <= 0 || (stride & 3) != 0)
                throw new ArgumentOutOfRangeException(nameof(stride));
            if (recordsPerSegment <= 0)
                throw new ArgumentOutOfRangeException(nameof(recordsPerSegment));
            _stride = stride;
            _recordsPerSegment = recordsPerSegment;
            _name = string.IsNullOrWhiteSpace(name)
                ? throw new ArgumentException("A Sigma buffer name is required.",
                    nameof(name))
                : name;
        }

        internal int Stride => _stride;
        internal long RecordCapacity { get; private set; }
        internal long OwnedBytes => checked(RecordCapacity * _stride);
        internal IReadOnlyList<SigmaFrameBufferSegment> Segments => _segments;

        internal SigmaFrameBufferSegment Segment(int index)
        {
            RequireAlive();
            if ((uint)index >= (uint)_segments.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _segments[index];
        }

        internal long AdditionalBytesFor(long requiredRecords)
        {
            RequireAlive();
            if (requiredRecords < 0L)
                throw new ArgumentOutOfRangeException(nameof(requiredRecords));
            long missing = Math.Max(0L, requiredRecords - RecordCapacity);
            return checked(missing * _stride);
        }

        internal void GrowTo(long requiredRecords)
        {
            RequireAlive();
            if (requiredRecords < 0L)
                throw new ArgumentOutOfRangeException(nameof(requiredRecords));
            while (RecordCapacity < requiredRecords)
            {
                int count = checked((int)Math.Min(
                    requiredRecords - RecordCapacity, _recordsPerSegment));
                long bytes = checked((long)count * _stride);
                var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    count, _stride)
                {
                    name = $"{_name} [{_segments.Count}]"
                };
                if ((long)buffer.count * buffer.stride != bytes)
                {
                    buffer.Dispose();
                    throw new InvalidOperationException(
                        $"Sigma segment allocation mismatch for {_name}.");
                }
                _segments.Add(new SigmaFrameBufferSegment(RecordCapacity, count,
                    buffer));
                RecordCapacity = checked(RecordCapacity + count);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            for (int index = 0; index < _segments.Count; ++index)
                _segments[index].Buffer.Dispose();
            _segments.Clear();
            RecordCapacity = 0L;
        }

        private void RequireAlive()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SigmaFrameSegmentedBuffer));
        }
    }

    /// <summary>
    /// Complete exact four-source cell journal for one owned coherent frame.
    /// Execution is coordinate-major while storage keeps each sixteen-lane
    /// footprint contiguous: footprint * laneCount + coordinate. Segment cuts
    /// therefore never split one exact S16 source cell.
    /// </summary>
    internal sealed class SigmaFrameSourceStorage : IDisposable
    {
        private readonly SigmaFrameSegmentedBuffer[] _lo;
        private readonly SigmaFrameSegmentedBuffer[] _hi;
        private readonly SigmaFrameSegmentedBuffer[] _validity;
        private readonly SigmaFrameSegmentedBuffer[] _provenance;
        private bool _disposed;

        internal SigmaFrameSourceStorage(int slot, int footprintCount,
            int footprintsPerWindow)
        {
            if (slot < 0)
                throw new ArgumentOutOfRangeException(nameof(slot));
            if (footprintCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(footprintCount));
            FootprintCount = footprintCount;
            if (footprintsPerWindow <= 0)
                throw new ArgumentOutOfRangeException(nameof(footprintsPerWindow));
            long coordinateRecords = checked((long)footprintCount *
                SigmaGeneratedFrame.LaneCount);
            int coordinateSegmentRecords = checked(footprintsPerWindow *
                SigmaGeneratedFrame.LaneCount);
            int provenanceSegmentRecords = footprintsPerWindow;

            _lo = new SigmaFrameSegmentedBuffer[SigmaGeneratedFrame.SourceCount];
            _hi = new SigmaFrameSegmentedBuffer[SigmaGeneratedFrame.SourceCount];
            _validity = new SigmaFrameSegmentedBuffer[
                SigmaGeneratedFrame.SourceCount];
            _provenance = new SigmaFrameSegmentedBuffer[
                SigmaGeneratedFrame.SourceCount];
            try
            {
                for (int source = 0; source < SigmaGeneratedFrame.SourceCount;
                    ++source)
                {
                    string prefix = $"Sigma frame {slot} source {source}";
                    _lo[source] = new SigmaFrameSegmentedBuffer(
                        SigmaGeneratedFrame.PackedQ48Stride,
                        coordinateSegmentRecords, $"{prefix} lower Q48");
                    _hi[source] = new SigmaFrameSegmentedBuffer(
                        SigmaGeneratedFrame.PackedQ48Stride,
                        coordinateSegmentRecords, $"{prefix} upper Q48");
                    _validity[source] = new SigmaFrameSegmentedBuffer(
                        SigmaGeneratedFrame.ValidityStride,
                        coordinateSegmentRecords, $"{prefix} validity");
                    _provenance[source] = new SigmaFrameSegmentedBuffer(
                        SigmaGeneratedFrame.ProvenanceStride,
                        provenanceSegmentRecords, $"{prefix} provenance");
                    _lo[source].GrowTo(coordinateRecords);
                    _hi[source].GrowTo(coordinateRecords);
                    _validity[source].GrowTo(coordinateRecords);
                    _provenance[source].GrowTo(footprintCount);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal int FootprintCount { get; }
        internal long OwnedBytes
        {
            get
            {
                long bytes = 0L;
                for (int source = 0; source < SigmaGeneratedFrame.SourceCount;
                    ++source)
                {
                    bytes = checked(bytes + (_lo[source]?.OwnedBytes ?? 0L));
                    bytes = checked(bytes + (_hi[source]?.OwnedBytes ?? 0L));
                    bytes = checked(bytes +
                        (_validity[source]?.OwnedBytes ?? 0L));
                    bytes = checked(bytes +
                        (_provenance[source]?.OwnedBytes ?? 0L));
                }
                return bytes;
            }
        }

        internal SigmaFrameSegmentedBuffer Lower(SigmaFrameSource source) =>
            Get(_lo, source);
        internal SigmaFrameSegmentedBuffer Upper(SigmaFrameSource source) =>
            Get(_hi, source);
        internal SigmaFrameSegmentedBuffer Validity(SigmaFrameSource source) =>
            Get(_validity, source);
        internal SigmaFrameSegmentedBuffer Provenance(SigmaFrameSource source) =>
            Get(_provenance, source);

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Dispose(_lo);
            Dispose(_hi);
            Dispose(_validity);
            Dispose(_provenance);
        }

        private static SigmaFrameSegmentedBuffer Get(
            SigmaFrameSegmentedBuffer[] buffers, SigmaFrameSource source)
        {
            int index = checked((int)source);
            if ((uint)index >= SigmaGeneratedFrame.SourceCount)
                throw new ArgumentOutOfRangeException(nameof(source));
            return buffers[index] ?? throw new ObjectDisposedException(
                nameof(SigmaFrameSourceStorage));
        }

        private static void Dispose(SigmaFrameSegmentedBuffer[] buffers)
        {
            if (buffers == null)
                return;
            for (int index = 0; index < buffers.Length; ++index)
                buffers[index]?.Dispose();
        }
    }

    internal sealed class SigmaOwnedFrameLease : IDisposable
    {
        private SigmaFrameResources _owner;
        private readonly int _slot;
        private readonly uint _generation;

        internal SigmaOwnedFrameLease(SigmaFrameResources owner, int slot,
            uint generation)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _slot = slot;
            _generation = generation;
        }

        internal int Slot => _slot;
        internal uint Generation => _generation;
        internal SigmaFrameSourceStorage Sources => Owner.GetSources(_slot,
            _generation);

        internal SigmaOwnedFrameLease Retain()
        {
            SigmaFrameResources owner = Owner;
            owner.Retain(_slot, _generation);
            return new SigmaOwnedFrameLease(owner, _slot, _generation);
        }

        public void Dispose()
        {
            SigmaFrameResources owner = _owner;
            if (owner == null)
                return;
            _owner = null;
            owner.Release(_slot, _generation);
        }

        private SigmaFrameResources Owner => _owner ??
            throw new ObjectDisposedException(nameof(SigmaOwnedFrameLease));
    }

    /// <summary>
    /// Owned execution resources for the direct whole-frame inverse. Capacities
    /// bound residency only. Failed growth returns backpressure before admission;
    /// it never truncates an accepted source, candidate, proof or revision.
    /// </summary>
    internal sealed class SigmaFrameResources : IDisposable
    {
        private const long MiB = 1024L * 1024L;
        private const long PreferredSegmentBytes = 64L * MiB;
        private const int InitialProposalKinds = 4;
        private const int InitialRevisionRecords = 256;
        private const int ClosureCounterRecords = 4;

        private readonly long _bindingLimit;
        private readonly long _budgetBytes;
        private readonly FrameSlot[] _frameSlots;
        private readonly Stack<int> _freeAllocated = new();
        private readonly Stack<int> _unallocated = new();
        private long _allocatedBytes;
        private bool _disposed;

        internal SigmaFrameResources(Vector2Int resolution,
            SigmaFrameMemoryProfile profile = SigmaFrameMemoryProfile.HighThroughput)
            : this(resolution, profile, SystemInfo.maxGraphicsBufferSize)
        {
        }

        internal SigmaFrameResources(Vector2Int resolution,
            SigmaFrameMemoryProfile profile, long bindingLimit)
        {
            if (resolution.x <= 0 || resolution.y <= 0)
                throw new ArgumentOutOfRangeException(nameof(resolution));
            if (bindingLimit <= SigmaGeneratedFrame.ProvenanceStride)
                throw new ArgumentOutOfRangeException(nameof(bindingLimit));
            int profileMiB = checked((int)profile);
            if (profileMiB != (int)SigmaFrameMemoryProfile.Minimum &&
                profileMiB != (int)SigmaFrameMemoryProfile.HighThroughput &&
                profileMiB != (int)SigmaFrameMemoryProfile.AuditedMaximum)
                throw new ArgumentOutOfRangeException(nameof(profile));

            ValidateGeneratedAbi();
            Resolution = resolution;
            FootprintCount = checked(resolution.x * resolution.y);
            TargetSortCapacity = Math.Max(256,
                NextPowerOfTwo(FootprintCount));
            Profile = profile;
            _bindingLimit = bindingLimit;
            _budgetBytes = checked((long)profileMiB * MiB);
            FootprintsPerWindow = ComputeExecutionWindowFootprints(bindingLimit);

            long initialCandidates = checked((long)FootprintCount *
                InitialProposalKinds);
            long initialTargets = checked((long)TargetSortCapacity +
                FootprintCount);
            long initialEdges = checked((long)FootprintCount * 2L);
            Candidates = Buffer<SigmaFrameCandidateGpu>(
                SigmaGeneratedFrame.FrameCandidateStride,
                "Sigma direct frame candidates", InitialProposalKinds);
            Outcomes = Buffer<SigmaFrameOutcomeGpu>(
                SigmaGeneratedFrame.FrameOutcomeStride,
                "Sigma direct frame outcomes", InitialProposalKinds);
            CandidateStates = Buffer<SigmaFrameUInt2Gpu>(
                SigmaGeneratedFrame.PackedQ48Stride,
                "Sigma direct candidate S16 states",
                InitialProposalKinds * SigmaGeneratedFrame.LaneCount);
            CandidateLower = Buffer<SigmaFrameUInt2Gpu>(
                SigmaGeneratedFrame.PackedQ48Stride,
                "Sigma direct candidate lower cells",
                InitialProposalKinds * SigmaGeneratedFrame.LaneCount);
            CandidateUpper = Buffer<SigmaFrameUInt2Gpu>(
                SigmaGeneratedFrame.PackedQ48Stride,
                "Sigma direct candidate upper cells",
                InitialProposalKinds * SigmaGeneratedFrame.LaneCount);
            CandidateValidity = Buffer<uint>(sizeof(uint),
                "Sigma direct candidate cell validity",
                InitialProposalKinds * SigmaGeneratedFrame.LaneCount);
            PendingGauges = Buffer<SigmaPendingGaugeGpu>(
                SigmaGeneratedFrame.PendingGaugeStride,
                "Sigma persistent pending gauges", 1);
            PendingStates = Buffer<SigmaFrameUInt2Gpu>(
                SigmaGeneratedFrame.PackedQ48Stride,
                "Sigma persistent pending S16 states",
                SigmaGeneratedFrame.LaneCount);
            PendingLower = Buffer<SigmaFrameUInt2Gpu>(
                SigmaGeneratedFrame.PackedQ48Stride,
                "Sigma persistent pending lower cells",
                SigmaGeneratedFrame.LaneCount);
            PendingUpper = Buffer<SigmaFrameUInt2Gpu>(
                SigmaGeneratedFrame.PackedQ48Stride,
                "Sigma persistent pending upper cells",
                SigmaGeneratedFrame.LaneCount);
            PendingValidity = Buffer<uint>(sizeof(uint),
                "Sigma persistent pending validity",
                SigmaGeneratedFrame.LaneCount);
            PendingProjectionDepth = Buffer<uint>(sizeof(uint),
                "Sigma pending proposal depth", 2);
            PendingProjectionHandles = Buffer<SigmaFrameUInt2Gpu>(
                SigmaGeneratedFrame.PackedQ48Stride,
                "Sigma pending proposal handles", 2);
            PendingControl = Buffer<SigmaFrameUInt4Gpu>(
                SigmaGeneratedFrame.ProvenanceStride,
                "Sigma pending journal control");
            Deltas = Buffer<SigmaFrameDeltaGpu>(
                SigmaGeneratedFrame.FrameDeltaStride,
                "Sigma direct target stream");
            TargetScratch = Buffer<SigmaFrameDeltaGpu>(
                SigmaGeneratedFrame.FrameDeltaStride,
                "Sigma direct ordered target scratch");
            DirtyEdges = Buffer<SigmaDirtyEdgeGpu>(
                SigmaGeneratedFrame.DirtyEdgeStride,
                "Sigma direct exact intrinsic edges", 2);
            Revisions = Buffer<SigmaFrameRevisionGpu>(
                SigmaGeneratedFrame.FrameRevisionStride,
                "Sigma direct frame revisions");
            ResolvedBlockCounts = Buffer<SigmaFrameUInt4Gpu>(
                SigmaGeneratedFrame.ProvenanceStride,
                "Sigma direct resolved block counts");
            ResolvedIndices = Buffer<uint>(sizeof(uint),
                "Sigma direct resolved target indices");
            ReducedLower = Buffer<SigmaFrameUInt2Gpu>(
                SigmaGeneratedFrame.PackedQ48Stride,
                "Sigma reduced target lower cells", SigmaGeneratedFrame.LaneCount);
            ReducedUpper = Buffer<SigmaFrameUInt2Gpu>(
                SigmaGeneratedFrame.PackedQ48Stride,
                "Sigma reduced target upper cells", SigmaGeneratedFrame.LaneCount);
            ReducedGap = Buffer<SigmaFrameUInt2Gpu>(
                SigmaGeneratedFrame.PackedQ48Stride,
                "Sigma reduced target exact gaps", SigmaGeneratedFrame.LaneCount);
            ReducedValidity = Buffer<uint>(sizeof(uint),
                "Sigma reduced target cell validity",
                SigmaGeneratedFrame.LaneCount);
            ReducedStates = Buffer<SigmaFrameUInt2Gpu>(
                SigmaGeneratedFrame.PackedQ48Stride,
                "Sigma reduced final S16 states", SigmaGeneratedFrame.LaneCount);
            PendingLabels = Buffer<uint>(sizeof(uint),
                "Sigma direct pending component labels");
            PendingLinks = Buffer<uint>(sizeof(uint),
                "Sigma direct pending exact links");
            DeferredFlags = Buffer<uint>(sizeof(uint),
                "Sigma direct deferred mutation flags");
            RootLocalOffsets = Buffer<SigmaFrameUInt2Gpu>(
                SigmaGeneratedFrame.PackedQ48Stride,
                "Sigma direct pending root local offsets");
            RootBlockOffsets = Buffer<SigmaFrameUInt2Gpu>(
                SigmaGeneratedFrame.PackedQ48Stride,
                "Sigma direct pending root block offsets");
            RootSuperOffsets = Buffer<SigmaFrameUInt2Gpu>(
                SigmaGeneratedFrame.PackedQ48Stride,
                "Sigma direct pending root super offsets");
            PageMarks = Buffer<uint>(sizeof(uint),
                "Sigma direct changed page marks");
            ChangedPageSlots = Buffer<uint>(sizeof(uint),
                "Sigma direct changed page slots");
            ClosureCounters = Buffer<SigmaFrameUInt4Gpu>(
                SigmaGeneratedFrame.ProvenanceStride,
                "Sigma direct closure counters");
            ExtentAllocator = Buffer<SigmaFrameUInt4Gpu>(
                SigmaGeneratedFrame.ProvenanceStride,
                "Sigma direct carrier extent allocator");
            RevisionRoot = Buffer<uint>(sizeof(uint),
                "Sigma direct published revision root");

            if (!TryEnsureCandidateCapacity(initialCandidates) ||
                !TryEnsurePendingCapacity(FootprintCount) ||
                !TryEnsureTargetScratchCapacity(initialTargets) ||
                !TryEnsureDirtyEdgeCapacity(initialEdges) ||
                !TryEnsureRevisionCapacity(InitialRevisionRecords) ||
                !TryEnsureClosureCapacity(SigmaCarrier.MaximumPagesPerSegment))
                throw new InvalidOperationException(
                    "The selected Sigma frame memory profile cannot hold one " +
                    "complete direct-frame execution window.");

            long sourceBytes = EstimateSourceBytes(FootprintCount);
            long remaining = Math.Max(0L, _budgetBytes - _allocatedBytes);
            int frameCapacity = checked((int)Math.Min(int.MaxValue,
                remaining / Math.Max(1L, sourceBytes)));
            if (frameCapacity < 2)
                throw new InvalidOperationException(
                    "The selected Sigma frame memory profile cannot own two " +
                    "complete coherent source-cell journals.");

            _frameSlots = new FrameSlot[frameCapacity];
            OwnedFrames = CreateBuffer(frameCapacity,
                SigmaGeneratedFrame.OwnedFrameStride,
                "Sigma owned coherent frames");
            _allocatedBytes = checked(_allocatedBytes +
                BufferBytes(OwnedFrames));
            OwnedFrames.SetData(new SigmaOwnedFrameGpu[frameCapacity]);
            ClosureCounters.Segments[0].Buffer.SetData(
                new SigmaFrameUInt4Gpu[ClosureCounterRecords]);
            PendingControl.Segments[0].Buffer.SetData(new[]
            {
                UInt4(0u, unchecked((uint)FootprintCount), 1u, 0u),
            });
            ExtentAllocator.Segments[0].Buffer.SetData(
                new SigmaFrameUInt4Gpu[1]);
            RevisionRoot.Segments[0].Buffer.SetData(new uint[1]);
            for (int slot = frameCapacity - 1; slot >= 0; --slot)
                _unallocated.Push(slot);
        }

        internal Vector2Int Resolution { get; }
        internal int FootprintCount { get; }
        internal int TargetSortCapacity { get; }
        internal int FootprintsPerWindow { get; }
        internal int ExecutionWindowCount => checked((FootprintCount +
            FootprintsPerWindow - 1) / FootprintsPerWindow);
        internal SigmaFrameMemoryProfile Profile { get; }
        internal int FrameCapacity => _frameSlots.Length;
        internal long BudgetBytes => _budgetBytes;
        internal long OwnedBytes => _allocatedBytes;
        internal long SourceBytesPerFrame => EstimateSourceBytes(FootprintCount);

        internal GraphicsBuffer OwnedFrames { get; private set; }
        internal SigmaFrameSegmentedBuffer Candidates { get; }
        internal SigmaFrameSegmentedBuffer Outcomes { get; }
        internal SigmaFrameSegmentedBuffer CandidateStates { get; }
        internal SigmaFrameSegmentedBuffer CandidateLower { get; }
        internal SigmaFrameSegmentedBuffer CandidateUpper { get; }
        internal SigmaFrameSegmentedBuffer CandidateValidity { get; }
        internal SigmaFrameSegmentedBuffer PendingGauges { get; }
        internal SigmaFrameSegmentedBuffer PendingStates { get; }
        internal SigmaFrameSegmentedBuffer PendingLower { get; }
        internal SigmaFrameSegmentedBuffer PendingUpper { get; }
        internal SigmaFrameSegmentedBuffer PendingValidity { get; }
        internal SigmaFrameSegmentedBuffer PendingProjectionDepth { get; }
        internal SigmaFrameSegmentedBuffer PendingProjectionHandles { get; }
        internal SigmaFrameSegmentedBuffer PendingControl { get; }
        internal SigmaFrameSegmentedBuffer Deltas { get; }
        internal SigmaFrameSegmentedBuffer TargetScratch { get; }
        internal SigmaFrameSegmentedBuffer DirtyEdges { get; }
        internal SigmaFrameSegmentedBuffer Revisions { get; }
        internal SigmaFrameSegmentedBuffer ResolvedBlockCounts { get; }
        internal SigmaFrameSegmentedBuffer ResolvedIndices { get; }
        internal SigmaFrameSegmentedBuffer ReducedLower { get; }
        internal SigmaFrameSegmentedBuffer ReducedUpper { get; }
        internal SigmaFrameSegmentedBuffer ReducedGap { get; }
        internal SigmaFrameSegmentedBuffer ReducedValidity { get; }
        internal SigmaFrameSegmentedBuffer ReducedStates { get; }
        internal SigmaFrameSegmentedBuffer PendingLabels { get; }
        internal SigmaFrameSegmentedBuffer PendingLinks { get; }
        internal SigmaFrameSegmentedBuffer DeferredFlags { get; }
        internal SigmaFrameSegmentedBuffer RootLocalOffsets { get; }
        internal SigmaFrameSegmentedBuffer RootBlockOffsets { get; }
        internal SigmaFrameSegmentedBuffer RootSuperOffsets { get; }
        internal SigmaFrameSegmentedBuffer PageMarks { get; }
        internal SigmaFrameSegmentedBuffer ChangedPageSlots { get; }
        internal SigmaFrameSegmentedBuffer ClosureCounters { get; }
        internal SigmaFrameSegmentedBuffer ExtentAllocator { get; }
        internal SigmaFrameSegmentedBuffer RevisionRoot { get; }

        internal bool TryAcquireFrame(uint revision, uint calibrationEpoch,
            uint depthLeftKey, uint depthRightKey, uint rgbLeftKey,
            uint rgbRightKey, uint poseGeneration,
            uint correctedCalibrationGeneration,
            out SigmaOwnedFrameLease lease)
        {
            RequireAlive();
            if (revision == 0u || calibrationEpoch == 0u)
                throw new ArgumentOutOfRangeException(nameof(revision));

            int slot;
            if (_freeAllocated.Count != 0)
            {
                slot = _freeAllocated.Pop();
            }
            else
            {
                if (_unallocated.Count == 0 ||
                    _allocatedBytes + SourceBytesPerFrame > _budgetBytes)
                {
                    lease = null;
                    return false;
                }
                slot = _unallocated.Pop();
                try
                {
                    var sources = new SigmaFrameSourceStorage(slot,
                        FootprintCount, FootprintsPerWindow);
                    _frameSlots[slot].Sources = sources;
                    _allocatedBytes = checked(_allocatedBytes + sources.OwnedBytes);
                }
                catch
                {
                    _unallocated.Push(slot);
                    throw;
                }
            }

            ref FrameSlot frame = ref _frameSlots[slot];
            uint generation = frame.Generation == uint.MaxValue
                ? 1u
                : frame.Generation + 1u;
            frame.Generation = generation;
            frame.References = 1;
            var record = new SigmaOwnedFrameGpu
            {
                Identity = UInt4(revision, calibrationEpoch,
                    unchecked((uint)Resolution.x), unchecked((uint)Resolution.y)),
                Keys = UInt4(depthLeftKey, depthRightKey, rgbLeftKey,
                    rgbRightKey),
                PoseSource = UInt4(poseGeneration,
                    correctedCalibrationGeneration,
                    (uint)SigmaOwnedFrameState.Sealed, 0u),
            };
            OwnedFrames.SetData(new[] { record }, 0, slot, 1);
            lease = new SigmaOwnedFrameLease(this, slot, generation);
            return true;
        }

        internal bool TryEnsureCandidateCapacity(long candidateRecords)
        {
            RequireAlive();
            if (candidateRecords < 0L)
                throw new ArgumentOutOfRangeException(nameof(candidateRecords));
            long stateRecords = checked(candidateRecords *
                SigmaGeneratedFrame.LaneCount);
            long additional = checked(Candidates.AdditionalBytesFor(candidateRecords) +
                Outcomes.AdditionalBytesFor(candidateRecords));
            additional = checked(additional +
                CandidateStates.AdditionalBytesFor(stateRecords));
            additional = checked(additional +
                CandidateLower.AdditionalBytesFor(stateRecords));
            additional = checked(additional +
                CandidateUpper.AdditionalBytesFor(stateRecords));
            additional = checked(additional +
                CandidateValidity.AdditionalBytesFor(stateRecords));
            additional = checked(additional +
                Deltas.AdditionalBytesFor(TargetSortCapacity));
            long resolvedBlocks = checked((candidateRecords + 1023L) / 1024L);
            additional = checked(additional +
                ResolvedBlockCounts.AdditionalBytesFor(resolvedBlocks));
            additional = checked(additional +
                ResolvedIndices.AdditionalBytesFor(FootprintCount));
            long reducedCoordinates = checked((long)FootprintCount *
                SigmaGeneratedFrame.LaneCount);
            additional = checked(additional +
                ReducedLower.AdditionalBytesFor(reducedCoordinates));
            additional = checked(additional +
                ReducedUpper.AdditionalBytesFor(reducedCoordinates));
            additional = checked(additional +
                ReducedGap.AdditionalBytesFor(reducedCoordinates));
            additional = checked(additional +
                ReducedValidity.AdditionalBytesFor(reducedCoordinates));
            additional = checked(additional +
                ReducedStates.AdditionalBytesFor(reducedCoordinates));
            if (!TryReserve(additional))
                return false;
            Candidates.GrowTo(candidateRecords);
            Outcomes.GrowTo(candidateRecords);
            CandidateStates.GrowTo(stateRecords);
            CandidateLower.GrowTo(stateRecords);
            CandidateUpper.GrowTo(stateRecords);
            CandidateValidity.GrowTo(stateRecords);
            Deltas.GrowTo(TargetSortCapacity);
            ResolvedBlockCounts.GrowTo(resolvedBlocks);
            ResolvedIndices.GrowTo(FootprintCount);
            ReducedLower.GrowTo(reducedCoordinates);
            ReducedUpper.GrowTo(reducedCoordinates);
            ReducedGap.GrowTo(reducedCoordinates);
            ReducedValidity.GrowTo(reducedCoordinates);
            ReducedStates.GrowTo(reducedCoordinates);
            _allocatedBytes = checked(_allocatedBytes + additional);
            return true;
        }

        internal bool TryEnsurePendingCapacity(long records)
        {
            RequireAlive();
            if (records < 0L)
                throw new ArgumentOutOfRangeException(nameof(records));
            long coordinates = checked(records * SigmaGeneratedFrame.LaneCount);
            long projections = checked(records * 2L);
            long additional = PendingGauges.AdditionalBytesFor(records);
            additional = checked(additional +
                PendingStates.AdditionalBytesFor(coordinates));
            additional = checked(additional +
                PendingLower.AdditionalBytesFor(coordinates));
            additional = checked(additional +
                PendingUpper.AdditionalBytesFor(coordinates));
            additional = checked(additional +
                PendingValidity.AdditionalBytesFor(coordinates));
            additional = checked(additional +
                PendingProjectionDepth.AdditionalBytesFor(projections));
            additional = checked(additional +
                PendingProjectionHandles.AdditionalBytesFor(projections));
            additional = checked(additional +
                PendingControl.AdditionalBytesFor(1));
            if (!TryReserve(additional))
                return false;
            PendingGauges.GrowTo(records);
            PendingStates.GrowTo(coordinates);
            PendingLower.GrowTo(coordinates);
            PendingUpper.GrowTo(coordinates);
            PendingValidity.GrowTo(coordinates);
            PendingProjectionDepth.GrowTo(projections);
            PendingProjectionHandles.GrowTo(projections);
            PendingControl.GrowTo(1);
            _allocatedBytes = checked(_allocatedBytes + additional);
            return true;
        }

        internal bool TryEnsureTargetScratchCapacity(long records) =>
            TryGrow(TargetScratch, records);

        internal bool TryEnsureDirtyEdgeCapacity(long records) =>
            TryGrow(DirtyEdges, records);

        internal bool TryEnsureRevisionCapacity(long records) =>
            TryGrow(Revisions, records);

        internal bool TryEnsureClosureCapacity(int pageCapacity)
        {
            RequireAlive();
            if (pageCapacity <= 0 ||
                pageCapacity > SigmaCarrier.MaximumPagesPerSegment)
                throw new ArgumentOutOfRangeException(nameof(pageCapacity));
            long blockCount = (FootprintCount + 255L) / 256L;
            long superCount = (blockCount + 255L) / 256L;
            long additional = PendingLabels.AdditionalBytesFor(FootprintCount);
            additional = checked(additional +
                PendingLinks.AdditionalBytesFor(FootprintCount));
            additional = checked(additional +
                DeferredFlags.AdditionalBytesFor(FootprintCount));
            additional = checked(additional +
                RootLocalOffsets.AdditionalBytesFor(FootprintCount));
            additional = checked(additional +
                RootBlockOffsets.AdditionalBytesFor(blockCount));
            additional = checked(additional +
                RootSuperOffsets.AdditionalBytesFor(superCount));
            additional = checked(additional +
                PageMarks.AdditionalBytesFor(pageCapacity));
            additional = checked(additional +
                ChangedPageSlots.AdditionalBytesFor(pageCapacity));
            additional = checked(additional +
                ClosureCounters.AdditionalBytesFor(ClosureCounterRecords));
            additional = checked(additional +
                ExtentAllocator.AdditionalBytesFor(1));
            additional = checked(additional +
                RevisionRoot.AdditionalBytesFor(1));
            if (!TryReserve(additional))
                return false;
            PendingLabels.GrowTo(FootprintCount);
            PendingLinks.GrowTo(FootprintCount);
            DeferredFlags.GrowTo(FootprintCount);
            RootLocalOffsets.GrowTo(FootprintCount);
            RootBlockOffsets.GrowTo(blockCount);
            RootSuperOffsets.GrowTo(superCount);
            PageMarks.GrowTo(pageCapacity);
            ChangedPageSlots.GrowTo(pageCapacity);
            ClosureCounters.GrowTo(ClosureCounterRecords);
            ExtentAllocator.GrowTo(1);
            RevisionRoot.GrowTo(1);
            _allocatedBytes = checked(_allocatedBytes + additional);
            return true;
        }

        internal static long EstimateSourceBytes(int footprintCount)
        {
            if (footprintCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(footprintCount));
            long coordinates = checked((long)footprintCount *
                SigmaGeneratedFrame.LaneCount);
            long coordinateBytes = checked(coordinates *
                (SigmaGeneratedFrame.PackedQ48Stride * 2L +
                 SigmaGeneratedFrame.ValidityStride));
            long provenanceBytes = checked((long)footprintCount *
                SigmaGeneratedFrame.ProvenanceStride);
            return checked((coordinateBytes + provenanceBytes) *
                SigmaGeneratedFrame.SourceCount);
        }

        internal static int ComputeSegmentRecordCapacity(long bindingLimit,
            int stride)
        {
            if (bindingLimit <= stride || stride <= 0)
                throw new ArgumentOutOfRangeException(nameof(bindingLimit));
            long targetBytes = Math.Min(PreferredSegmentBytes,
                bindingLimit - stride);
            long records = targetBytes / stride;
            if (records >= 256L)
                records = records / 256L * 256L;
            return checked((int)Math.Max(1L, Math.Min(int.MaxValue, records)));
        }

        internal static int ComputeExecutionWindowFootprints(long bindingLimit)
        {
            int capacity = int.MaxValue;
            capacity = Math.Min(capacity, ComputeSegmentRecordCapacity(bindingLimit,
                SigmaGeneratedFrame.FrameCandidateStride) / InitialProposalKinds);
            capacity = Math.Min(capacity, ComputeSegmentRecordCapacity(bindingLimit,
                SigmaGeneratedFrame.FrameOutcomeStride) / InitialProposalKinds);
            capacity = Math.Min(capacity, ComputeSegmentRecordCapacity(bindingLimit,
                SigmaGeneratedFrame.PackedQ48Stride) /
                (InitialProposalKinds * SigmaGeneratedFrame.LaneCount));
            capacity = Math.Min(capacity, ComputeSegmentRecordCapacity(bindingLimit,
                SigmaGeneratedFrame.FrameDeltaStride));
            capacity = Math.Min(capacity, ComputeSegmentRecordCapacity(bindingLimit,
                SigmaGeneratedFrame.DirtyEdgeStride) / 2);
            capacity = Math.Min(capacity, ComputeSegmentRecordCapacity(bindingLimit,
                SigmaGeneratedFrame.ValidityStride) / SigmaGeneratedFrame.LaneCount);
            capacity = Math.Min(capacity, ComputeSegmentRecordCapacity(bindingLimit,
                SigmaGeneratedFrame.ProvenanceStride));
            capacity = capacity / 256 * 256;
            if (capacity < 256)
                throw new InvalidOperationException(
                    "Vulkan binding range cannot hold one 256-footprint Sigma window.");
            return capacity;
        }

        internal SigmaFrameExecutionWindow ExecutionWindow(int index)
        {
            RequireAlive();
            if ((uint)index >= (uint)ExecutionWindowCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            int first = checked(index * FootprintsPerWindow);
            return new SigmaFrameExecutionWindow(index, first,
                Math.Min(FootprintsPerWindow, FootprintCount - first));
        }

        internal static GraphicsBuffer WindowBuffer(SigmaFrameSegmentedBuffer buffer,
            SigmaFrameExecutionWindow window, int recordsPerFootprint)
        {
            if (buffer == null || recordsPerFootprint <= 0)
                throw new ArgumentOutOfRangeException(nameof(recordsPerFootprint));
            SigmaFrameBufferSegment segment = buffer.Segment(window.Index);
            long expectedFirst = checked((long)window.FirstFootprint *
                recordsPerFootprint);
            int required = checked(window.FootprintCount * recordsPerFootprint);
            if (segment.FirstRecord != expectedFirst || segment.RecordCount < required)
                throw new InvalidOperationException(
                    "Sigma execution-window buffers are not footprint aligned.");
            return segment.Buffer;
        }

        internal SigmaFrameSourceStorage GetSources(int slot, uint generation)
        {
            RequireSlot(slot, generation);
            return _frameSlots[slot].Sources;
        }

        internal void Retain(int slot, uint generation)
        {
            RequireSlot(slot, generation);
            _frameSlots[slot].References = checked(
                _frameSlots[slot].References + 1);
        }

        internal void Release(int slot, uint generation)
        {
            if (_disposed)
                return;
            RequireSlot(slot, generation);
            ref FrameSlot frame = ref _frameSlots[slot];
            frame.References--;
            if (frame.References != 0)
                return;
            var cleared = new SigmaOwnedFrameGpu
            {
                Identity = UInt4(0u, 0u, unchecked((uint)Resolution.x),
                    unchecked((uint)Resolution.y)),
                PoseSource = UInt4(0u, 0u,
                    (uint)SigmaOwnedFrameState.Free, 0u),
            };
            OwnedFrames.SetData(new[] { cleared }, 0, slot, 1);
            _freeAllocated.Push(slot);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            for (int slot = 0; slot < _frameSlots.Length; ++slot)
                _frameSlots[slot].Sources?.Dispose();
            Candidates.Dispose();
            Outcomes.Dispose();
            CandidateStates.Dispose();
            CandidateLower.Dispose();
            CandidateUpper.Dispose();
            CandidateValidity.Dispose();
            PendingGauges.Dispose();
            PendingStates.Dispose();
            PendingLower.Dispose();
            PendingUpper.Dispose();
            PendingValidity.Dispose();
            PendingProjectionDepth.Dispose();
            PendingProjectionHandles.Dispose();
            PendingControl.Dispose();
            Deltas.Dispose();
            TargetScratch.Dispose();
            DirtyEdges.Dispose();
            Revisions.Dispose();
            ResolvedBlockCounts.Dispose();
            ResolvedIndices.Dispose();
            ReducedLower.Dispose();
            ReducedUpper.Dispose();
            ReducedGap.Dispose();
            ReducedValidity.Dispose();
            ReducedStates.Dispose();
            PendingLabels.Dispose();
            PendingLinks.Dispose();
            DeferredFlags.Dispose();
            RootLocalOffsets.Dispose();
            RootBlockOffsets.Dispose();
            RootSuperOffsets.Dispose();
            PageMarks.Dispose();
            ChangedPageSlots.Dispose();
            ClosureCounters.Dispose();
            ExtentAllocator.Dispose();
            RevisionRoot.Dispose();
            OwnedFrames?.Dispose();
            OwnedFrames = null;
            _freeAllocated.Clear();
            _unallocated.Clear();
            _allocatedBytes = 0L;
        }

        private SigmaFrameSegmentedBuffer Buffer<T>(int stride, string name)
            where T : struct
        {
            if (Marshal.SizeOf<T>() != stride)
                throw new InvalidOperationException(
                    $"Sigma frame ABI stride mismatch for {typeof(T).Name}: " +
                    $"C#={Marshal.SizeOf<T>()}, generated={stride}.");
            return new SigmaFrameSegmentedBuffer(stride,
                ComputeSegmentRecordCapacity(_bindingLimit, stride), name);
        }

        private SigmaFrameSegmentedBuffer Buffer<T>(int stride, string name,
            int recordsPerFootprint) where T : struct
        {
            if (Marshal.SizeOf<T>() != stride || recordsPerFootprint <= 0)
                throw new InvalidOperationException(
                    $"Sigma frame window ABI mismatch for {typeof(T).Name}.");
            return new SigmaFrameSegmentedBuffer(stride,
                checked(FootprintsPerWindow * recordsPerFootprint), name);
        }

        private bool TryGrow(SigmaFrameSegmentedBuffer buffer, long records)
        {
            RequireAlive();
            long additional = buffer.AdditionalBytesFor(records);
            if (!TryReserve(additional))
                return false;
            buffer.GrowTo(records);
            _allocatedBytes = checked(_allocatedBytes + additional);
            return true;
        }

        private bool TryReserve(long additionalBytes) => additionalBytes >= 0L &&
            _allocatedBytes <= _budgetBytes - additionalBytes;

        private void RequireSlot(int slot, uint generation)
        {
            RequireAlive();
            if ((uint)slot >= (uint)_frameSlots.Length || generation == 0u ||
                _frameSlots[slot].Generation != generation ||
                _frameSlots[slot].References <= 0 ||
                _frameSlots[slot].Sources == null)
                throw new InvalidOperationException(
                    "Sigma owned-frame lease is stale or not resident.");
        }

        private void RequireAlive()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SigmaFrameResources));
        }

        private static GraphicsBuffer CreateBuffer(int count, int stride,
            string name)
        {
            if (count <= 0 || stride <= 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            return new GraphicsBuffer(GraphicsBuffer.Target.Structured, count,
                stride) { name = name };
        }

        private static long BufferBytes(GraphicsBuffer buffer) =>
            checked((long)buffer.count * buffer.stride);

        private static int NextPowerOfTwo(int value)
        {
            if (value <= 0 || value > 1 << 30)
                throw new ArgumentOutOfRangeException(nameof(value));
            int result = 1;
            while (result < value)
                result <<= 1;
            return result;
        }

        private static SigmaFrameUInt4Gpu UInt4(uint x, uint y, uint z, uint w) =>
            new() { X = x, Y = y, Z = z, W = w };

        private static void ValidateGeneratedAbi()
        {
            ValidateStride<SigmaOwnedFrameGpu>(
                SigmaGeneratedFrame.OwnedFrameStride);
            ValidateStride<SigmaFrameCandidateGpu>(
                SigmaGeneratedFrame.FrameCandidateStride);
            ValidateStride<SigmaFrameOutcomeGpu>(
                SigmaGeneratedFrame.FrameOutcomeStride);
            ValidateStride<SigmaPendingGaugeGpu>(
                SigmaGeneratedFrame.PendingGaugeStride);
            ValidateStride<SigmaFrameDeltaGpu>(
                SigmaGeneratedFrame.FrameDeltaStride);
            ValidateStride<SigmaDirtyEdgeGpu>(
                SigmaGeneratedFrame.DirtyEdgeStride);
            ValidateStride<SigmaFrameRevisionGpu>(
                SigmaGeneratedFrame.FrameRevisionStride);
            ValidateStride<SigmaFrameUInt2Gpu>(
                SigmaGeneratedFrame.PackedQ48Stride);
            ValidateStride<SigmaFrameUInt4Gpu>(
                SigmaGeneratedFrame.ProvenanceStride);
        }

        private static void ValidateStride<T>(int expected) where T : struct
        {
            int actual = Marshal.SizeOf<T>();
            if (actual != expected)
                throw new InvalidOperationException(
                    $"Sigma frame ABI stride mismatch for {typeof(T).Name}: " +
                    $"C#={actual}, generated={expected}.");
        }

        private struct FrameSlot
        {
            internal uint Generation;
            internal int References;
            internal SigmaFrameSourceStorage Sources;
        }
    }

    internal readonly struct SigmaFrameExecutionWindow
    {
        internal SigmaFrameExecutionWindow(int index, int firstFootprint,
            int footprintCount)
        {
            Index = index;
            FirstFootprint = firstFootprint;
            FootprintCount = footprintCount;
        }

        internal int Index { get; }
        internal int FirstFootprint { get; }
        internal int FootprintCount { get; }
    }
}
