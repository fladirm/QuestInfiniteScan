using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    internal interface ISigmaResidencyCompletion
    {
        SigmaGpuCompletionStatus Poll(out string error);
    }

    internal interface ISigmaColdBatchOperation :
        ISigmaResidencyCompletion, IDisposable { }

    internal interface ISigmaColdUploadBackend
    {
        ISigmaColdBatchOperation Begin(SigmaCarrierReadBatch target,
            SigmaColdDecodedBatch batch);
    }

    internal interface ISigmaResidencyUpdateBackend
    {
        ISigmaColdBatchOperation Begin(SigmaCarrierReadBatch target,
            IReadOnlyList<SigmaResidencyUpdate> updates);
    }

    internal readonly struct SigmaResidentPairAddress :
        IEquatable<SigmaResidentPairAddress>
    {
        internal SigmaResidentPairAddress(int segmentIndex, int pairIndex,
            SigmaResidencyKey key)
        {
            if (segmentIndex < 0 || pairIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(pairIndex));
            SegmentIndex = segmentIndex;
            PairIndex = pairIndex;
            Key = key;
        }

        internal int SegmentIndex { get; }
        internal int PairIndex { get; }
        internal SigmaResidencyKey Key { get; }
        public bool Equals(SigmaResidentPairAddress other) =>
            SegmentIndex == other.SegmentIndex && PairIndex == other.PairIndex &&
            Key.Equals(other.Key);
        public override bool Equals(object obj) =>
            obj is SigmaResidentPairAddress other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(SegmentIndex,
            PairIndex, Key);
    }

    internal enum SigmaResidencyLeaseKind
    {
        Publication,
        Readout,
        Persistence,
    }

    internal enum SigmaPagerActivity
    {
        Idle,
        IndexingCold,
        Reading,
        Uploading,
        PublishingLoad,
        RetiringPhysical,
        PublishingAdoption,
        PublishingAbsent,
        PublishingEviction,
        Faulted,
    }

    internal sealed class SigmaCompletedResidencyCompletion :
        ISigmaResidencyCompletion
    {
        internal static readonly SigmaCompletedResidencyCompletion Instance =
            new();
        private SigmaCompletedResidencyCompletion() { }
        public SigmaGpuCompletionStatus Poll(out string error)
        {
            error = null;
            return SigmaGpuCompletionStatus.Complete;
        }
    }

    internal sealed class SigmaNativeColdUploadBackend : ISigmaColdUploadBackend
    {
        public ISigmaColdBatchOperation Begin(SigmaCarrierReadBatch target,
            SigmaColdDecodedBatch batch)
        {
            SigmaNativeVulkanColdUpload.ColdUploadJob job =
                SigmaNativeVulkanColdUpload.Create(target, batch);
            CommandBuffer command = CommandBufferPool.Get(
                "Sigma N5 COLD_REHYDRATE upload");
            bool recorded = false;
            try
            {
                job.Record(command);
                recorded = true;
                Graphics.ExecuteCommandBuffer(command);
                return new NativeOperation(job);
            }
            catch
            {
                if (!recorded)
                    job.Dispose();
                // Once ExecuteCommandBuffer was attempted, completion is
                // uncertain. The caller quarantines the reserved pair and the
                // job deliberately remains alive instead of recycling memory.
                throw;
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }

        private sealed class NativeOperation : ISigmaColdBatchOperation
        {
            private SigmaNativeVulkanColdUpload.ColdUploadJob _job;
            internal NativeOperation(
                SigmaNativeVulkanColdUpload.ColdUploadJob job) =>
                _job = job;
            public SigmaGpuCompletionStatus Poll(out string error) =>
                _job.Poll(out error);
            public void Dispose()
            {
                _job?.Dispose();
                _job = null;
            }
        }
    }

    internal sealed class SigmaGraphicsResidencyUpdateBackend :
        ISigmaResidencyUpdateBackend
    {
        private readonly SigmaCarrierResidencyResources _resources;

        internal SigmaGraphicsResidencyUpdateBackend(
            SigmaCarrierResidencyResources resources) =>
            _resources = resources ?? throw new ArgumentNullException(
                nameof(resources));

        public ISigmaColdBatchOperation Begin(SigmaCarrierReadBatch target,
            IReadOnlyList<SigmaResidencyUpdate> updates)
        {
            CommandBuffer command = CommandBufferPool.Get(
                "Sigma N5 residency publish-last");
            try
            {
                SigmaGpuCompletionTicket ticket = _resources.RecordApply(
                    command, target, updates);
                Graphics.ExecuteCommandBuffer(command);
                return new Operation(_resources.Results, updates.Count, ticket);
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }

        private sealed class Operation : ISigmaColdBatchOperation
        {
            [System.Runtime.InteropServices.StructLayout(
                System.Runtime.InteropServices.LayoutKind.Sequential)]
            private struct ResultGpu
            {
                internal uint Code;
                internal uint X;
                internal uint Y;
                internal uint Z;
            }

            private readonly GraphicsBuffer _results;
            private readonly int _count;
            private readonly SigmaGpuCompletionTicket _ticket;
            private bool _requested;
            private bool _finished;
            private string _error;

            internal Operation(GraphicsBuffer results, int count,
                SigmaGpuCompletionTicket ticket)
            {
                _results = results;
                _count = count;
                _ticket = ticket;
            }

            public SigmaGpuCompletionStatus Poll(out string error)
            {
                if (_finished)
                {
                    error = _error;
                    return _error == null ? SigmaGpuCompletionStatus.Complete :
                        SigmaGpuCompletionStatus.Faulted;
                }
                SigmaGpuCompletionStatus status = _ticket.Poll(
                    out string fenceError);
                if (status == SigmaGpuCompletionStatus.Pending)
                {
                    error = null;
                    return status;
                }
                if (status == SigmaGpuCompletionStatus.Faulted)
                {
                    _finished = true;
                    _error = fenceError ??
                        "Residency locator publication fence faulted.";
                    error = _error;
                    return status;
                }
                if (!_requested)
                {
                    _requested = true;
                    try
                    {
                        AsyncGPUReadback.Request(_results,
                            checked(_count *
                                SigmaCarrierResidencyAbi.ResultStride), 0,
                            CompleteReadback);
                    }
                    catch (Exception exception)
                    {
                        _finished = true;
                        _error = "Residency result readback could not start: " +
                            exception.Message;
                    }
                }
                error = _error;
                return _finished
                    ? (_error == null ? SigmaGpuCompletionStatus.Complete :
                        SigmaGpuCompletionStatus.Faulted)
                    : SigmaGpuCompletionStatus.Pending;
            }

            private void CompleteReadback(AsyncGPUReadbackRequest request)
            {
                if (request.hasError)
                {
                    _error = "Residency result readback failed.";
                    _finished = true;
                    return;
                }
                NativeArray<ResultGpu> data = request.GetData<ResultGpu>();
                if (data.Length < _count)
                    _error = "Residency result readback was truncated.";
                else
                {
                    for (int index = 0; index < _count; ++index)
                    {
                        if (data[index].Code ==
                            (uint)SigmaResidencyResult.Applied)
                            continue;
                        _error = "Residency update failed at batch index " +
                            index + " with result " + data[index].Code + ".";
                        break;
                    }
                }
                _finished = true;
            }

            public void Dispose() { }
        }
    }

    /// <summary>
    /// Bounded N5 cold pager. The array below is lifecycle state for the finite
    /// physical cache only; durable HEAD/PageRecord and the one GPU locator remain
    /// the logical authorities.
    /// </summary>
    internal sealed class SigmaCarrierPager : IDisposable
    {
        private sealed class ResidentPair
        {
            internal bool Occupied;
            internal SigmaCarrierReadBatch Target;
            internal int PairIndex;
            internal int CurrentSlot;
            internal int ShadowSlot;
            internal int CurrentGlobalSlot;
            internal int ShadowGlobalSlot;
            internal uint ResidentGeneration;
            internal SigmaResidencyState State;
            internal SigmaResidencyKey Key;
            internal SigmaDurableHash RootHash;
            internal SigmaDurableHash PageBlobHash;
            internal int PublicationLeases;
            internal int ReadoutLeases;
            internal int PersistenceLeases;
            internal ISigmaResidencyCompletion LastReader;
            internal ISigmaResidencyCompletion LastWriter;
        }

        private sealed class PendingPage
        {
            internal SigmaDurablePageRecord Record;
            internal SigmaResidencyKey Key;
            internal ResidentPair Pair;
            internal SigmaDecodedPage Decoded;
        }

        private sealed class RetirementBatch
        {
            internal SigmaCarrierReadBatch Target;
            internal IReadOnlyList<SigmaResidencyUpdate> Updates;
        }

        private readonly SigmaDurableStore _store;
        private readonly SigmaCarrierReadBatch[] _targets;
        private readonly ISigmaColdUploadBackend _upload;
        private readonly ISigmaResidencyUpdateBackend _updates;
        private readonly ResidentPair[] _pairs;
        private readonly List<PendingPage> _pending = new();
        private readonly Queue<SigmaStagedDurablePage> _completedLoads = new();
        private readonly Queue<RetirementBatch> _retirementBatches = new();
        private List<SigmaResidencyUpdate> _adoptionUpdates;
        private SigmaDurableRootLease _rootLease;
        private Task<SigmaColdDecodedBatch> _decodeTask;
        private ISigmaColdBatchOperation _operation;
        private SigmaPagerActivity _activity;
        private string _fault;
        private SigmaResidencyKey _completedEvictionKey;
        private int _completedEvictionSegment = -1;
        private int _completedEvictionPair = -1;
        private bool _disposed;

        internal SigmaCarrierPager(SigmaDurableStore store,
            SigmaCarrierReadBatch target, ISigmaColdUploadBackend upload,
            ISigmaResidencyUpdateBackend updates) : this(store,
                new[] { target }, upload, updates) { }

        internal SigmaCarrierPager(SigmaDurableStore store,
            IReadOnlyList<SigmaCarrierReadBatch> targets,
            ISigmaColdUploadBackend upload,
            ISigmaResidencyUpdateBackend updates)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _upload = upload ?? throw new ArgumentNullException(nameof(upload));
            _updates = updates ?? throw new ArgumentNullException(nameof(updates));
            if (targets == null || targets.Count == 0)
                throw new ArgumentOutOfRangeException(nameof(targets));
            _targets = new SigmaCarrierReadBatch[targets.Count];
            int pageCapacity = targets[0].PageCapacity;
            int firstPair = targets[0].PairFirst;
            int pairCapacity = 0;
            for (int segment = 0; segment < targets.Count; ++segment)
            {
                SigmaCarrierReadBatch target = targets[segment];
                if (target.SegmentIndex != segment ||
                    target.PageCapacity != pageCapacity ||
                    target.PairFirst != checked(firstPair + pairCapacity) ||
                    target.PageCapacity < 2 ||
                    (target.PageCapacity & 1) != 0 ||
                    target.PublicationRoot != targets[0].PublicationRoot ||
                    target.ResidentLocator != targets[0].ResidentLocator ||
                    target.ResidentSlotTable !=
                        targets[0].ResidentSlotTable ||
                    target.ResidentLocatorCapacity !=
                        targets[0].ResidentLocatorCapacity ||
                    target.ResidentSlotCapacity !=
                        targets[0].ResidentSlotCapacity ||
                    !ReferenceEquals(target.RuntimeState,
                        targets[0].RuntimeState))
                    throw new ArgumentException(
                        "Pager binding banks must be equal, ordered, and own " +
                        "one shared locator/root authority with complete " +
                        "current/shadow pairs.", nameof(targets));
                _targets[segment] = target;
                pairCapacity = checked(pairCapacity + target.PairCount);
            }
            if (checked((firstPair + pairCapacity) * 2) >
                targets[0].ResidentSlotCapacity)
                throw new ArgumentException(
                    "Pager binding banks exceed the shared dense slot table.",
                    nameof(targets));
            _pairs = new ResidentPair[pairCapacity];
            int output = 0;
            foreach (SigmaCarrierReadBatch target in _targets)
            {
                for (int pair = 0; pair < target.PairCount; ++pair)
                {
                    int current = pair * 2;
                    int global = target.PairFirst * 2 + current;
                    _pairs[output++] = new ResidentPair
                    {
                        Target = target,
                        PairIndex = pair,
                        CurrentSlot = current,
                        ShadowSlot = current + 1,
                        CurrentGlobalSlot = global,
                        ShadowGlobalSlot = global + 1,
                        State = SigmaResidencyState.AbsentInRoot,
                    };
                }
            }
        }

        internal SigmaPagerActivity Activity => _activity;
        internal string Fault => _fault;
        internal int PairCapacity => _pairs.Length;
        internal IReadOnlyList<SigmaCarrierReadBatch> Targets => _targets;
        internal int ResidentPairCount
        {
            get
            {
                int count = 0;
                foreach (ResidentPair pair in _pairs)
                    if (pair.State == SigmaResidencyState.HotClean ||
                        pair.State == SigmaResidencyState.HotDirty)
                        ++count;
                return count;
            }
        }
        internal int FreePairCount
        {
            get
            {
                int count = 0;
                foreach (ResidentPair pair in _pairs)
                    if (!pair.Occupied)
                        ++count;
                return count;
            }
        }

        internal int FreePairCountInSegment(int segmentIndex)
        {
            if ((uint)segmentIndex >= (uint)_targets.Length)
                throw new ArgumentOutOfRangeException(nameof(segmentIndex));
            int count = 0;
            foreach (ResidentPair pair in _pairs)
                if (pair.Target.SegmentIndex == segmentIndex &&
                    !pair.Occupied)
                    ++count;
            return count;
        }

        internal int MaximumFreePairCountInBank
        {
            get
            {
                int maximum = 0;
                for (int index = 0; index < _targets.Length; ++index)
                    maximum = Math.Max(maximum,
                        FreePairCountInSegment(index));
                return maximum;
            }
        }

        internal int NativeTargetFreePairCount
        {
            get
            {
                SigmaCarrierReadBatch target = SelectNativeTarget();
                return FreePairCountInSegment(target.SegmentIndex);
            }
        }

        internal SigmaCarrierReadBatch SelectNativeTarget()
        {
            ThrowIfDisposed();
            if (TryGetResidentAppendTail(out ResidentPair appendTail))
                return appendTail.Target;

            SigmaCarrierReadBatch selected = _targets[0];
            int selectedFree = FreePairCountInSegment(selected.SegmentIndex);
            for (int index = 1; index < _targets.Length; ++index)
            {
                int free = FreePairCountInSegment(index);
                if (free > selectedFree)
                {
                    selected = _targets[index];
                    selectedFree = free;
                }
            }
            return selected;
        }

        internal bool BeginRehydrate(
            IReadOnlyList<SigmaCarrierPageCoordinate> coordinates)
        {
            ThrowIfDisposed();
            if (_activity != SigmaPagerActivity.Idle)
                return false;
            if (coordinates == null || coordinates.Count == 0 ||
                coordinates.Count > SigmaNativeVulkanColdUpload.MaximumPages)
                throw new ArgumentOutOfRangeException(nameof(coordinates));
            SigmaCarrierReadBatch loadTarget = SelectLoadTarget(
                coordinates.Count);
            _rootLease = _store.PinHead();
            var unique = new HashSet<SigmaCarrierPageCoordinate>();
            try
            {
                for (int index = 0; index < coordinates.Count; ++index)
                {
                    SigmaCarrierPageCoordinate coordinate = coordinates[index];
                    if (!unique.Add(coordinate))
                        throw new ArgumentException(
                            "A rehydrate batch contains a duplicate page key.");
                    if (!_store.TryGetPageRecord(_rootLease, coordinate,
                            out SigmaDurablePageRecord record))
                        throw new InvalidOperationException(
                            "ABSENT_IN_ROOT is not a rehydrate request: " +
                            coordinate + ".");
                    ResidentPair pair = ReservePair(loadTarget);
                    var key = new SigmaResidencyKey(coordinate,
                        record.Revision, record.PageGeneration);
                    pair.Key = key;
                    pair.RootHash = _rootLease.Hash;
                    pair.PageBlobHash = record.PageBlobHash;
                    pair.ResidentGeneration = NextResidentGeneration(pair);
                    pair.State = SigmaResidencyState.Loading;
                    pair.PersistenceLeases = 1;
                    _pending.Add(new PendingPage
                    {
                        Record = record,
                        Key = key,
                        Pair = pair,
                    });
                }
                try
                {
                    _operation = _updates.Begin(loadTarget,
                        BuildColdIndexUpdates());
                    _activity = SigmaPagerActivity.IndexingCold;
                    return true;
                }
                catch (Exception exception)
                {
                    // ExecuteCommandBuffer may throw after Unity accepted part
                    // of a submission.  The reserved pairs therefore remain
                    // quarantined; they are never returned to the allocator on
                    // a CPU exception whose GPU completion is unknown.
                    Quarantine("Cold locator indexing submission became " +
                        "uncertain: " + exception.Message);
                    throw;
                }
            }
            catch
            {
                if (_activity != SigmaPagerActivity.Faulted)
                    ReleasePendingBeforeGpu();
                throw;
            }
        }

        internal bool BeginRehydrate(
            IReadOnlyList<SigmaResidencyKey> keys)
        {
            ThrowIfDisposed();
            if (_activity != SigmaPagerActivity.Idle)
                return false;
            if (keys == null || keys.Count == 0 ||
                keys.Count > SigmaNativeVulkanColdUpload.MaximumPages)
                throw new ArgumentOutOfRangeException(nameof(keys));
            SigmaCarrierReadBatch loadTarget = SelectLoadTarget(keys.Count);
            _rootLease = _store.PinHead();
            var unique = new HashSet<SigmaResidencyKey>();
            try
            {
                for (int index = 0; index < keys.Count; ++index)
                {
                    SigmaResidencyKey key = keys[index];
                    if (!unique.Add(key))
                        throw new ArgumentException(
                            "A rehydrate batch contains a duplicate exact key.");
                    if (!_store.TryGetPageRecord(_rootLease, key.Coordinate,
                            out SigmaDurablePageRecord record) ||
                        record.Revision != key.RootContext ||
                        record.PageGeneration != key.PageGeneration)
                        throw new InvalidOperationException(
                            "The exact cold generation is not reachable from " +
                            "the selected durable HEAD: " + key.Coordinate +
                            ".");
                    ResidentPair pair = ReservePair(loadTarget);
                    pair.Key = key;
                    pair.RootHash = _rootLease.Hash;
                    pair.PageBlobHash = record.PageBlobHash;
                    pair.ResidentGeneration = NextResidentGeneration(pair);
                    pair.State = SigmaResidencyState.Loading;
                    pair.PersistenceLeases = 1;
                    _pending.Add(new PendingPage
                    {
                        Record = record,
                        Key = key,
                        Pair = pair,
                    });
                }
                try
                {
                    _operation = _updates.Begin(loadTarget,
                        BuildColdIndexUpdates());
                    _activity = SigmaPagerActivity.IndexingCold;
                    return true;
                }
                catch (Exception exception)
                {
                    Quarantine("Exact cold locator indexing submission became " +
                        "uncertain: " + exception.Message);
                    throw;
                }
            }
            catch
            {
                if (_activity != SigmaPagerActivity.Faulted)
                    ReleasePendingBeforeGpu();
                throw;
            }
        }

        internal bool BeginPublishAbsent(
            IReadOnlyList<SigmaResidencyKey> keys)
        {
            ThrowIfDisposed();
            if (_activity != SigmaPagerActivity.Idle)
                return false;
            if (keys == null || keys.Count == 0 ||
                keys.Count > SigmaCarrierResidencyAbi.MaximumColdBatch)
                throw new ArgumentOutOfRangeException(nameof(keys));
            var unique = new HashSet<SigmaResidencyKey>();
            var updates = new List<SigmaResidencyUpdate>(keys.Count);
            for (int index = 0; index < keys.Count; ++index)
                if (unique.Add(keys[index]))
                    updates.Add(new SigmaResidencyUpdate(keys[index], 0u,
                        -1, -1, -1,
                        SigmaResidencyState.AbsentInRoot));
            _operation = _updates.Begin(_targets[0], updates);
            _activity = SigmaPagerActivity.PublishingAbsent;
            return true;
        }

        internal bool BeginAdoptDurableSnapshot(
            IReadOnlyCollection<SigmaStagedDurablePage> snapshot) =>
            BeginAdoptDurableSnapshot(snapshot,
                Array.Empty<SigmaResidentPairAddress>());

        internal bool BeginAdoptDurableSnapshot(
            IReadOnlyCollection<SigmaStagedDurablePage> snapshot,
            IReadOnlyList<SigmaResidentPairAddress> retiredPairs)
        {
            ThrowIfDisposed();
            if (_activity != SigmaPagerActivity.Idle)
                return false;
            if (snapshot == null || snapshot.Count > _pairs.Length ||
                retiredPairs == null)
                throw new ArgumentOutOfRangeException(nameof(snapshot));
            if (snapshot.Count == 0 && retiredPairs.Count == 0)
                return true;
            _rootLease = _store.PinHead();
            var updates = new List<SigmaResidencyUpdate>(snapshot.Count);
            var claimedPairs = new HashSet<long>();
            try
            {
                foreach (SigmaStagedDurablePage staged in snapshot)
                {
                    int pairIndex = staged.VisibleSlot >> 1;
                    if ((uint)staged.SegmentIndex >=
                            (uint)_targets.Length ||
                        (uint)pairIndex >=
                            (uint)_targets[staged.SegmentIndex].PairCount ||
                        !claimedPairs.Add(PhysicalPairKey(
                            staged.SegmentIndex, pairIndex)))
                        throw new InvalidDataException(
                            "Durable resident snapshot aliases a physical pair.");
                    SigmaDurablePageUpdate page = staged.Update;
                    if (!_store.TryGetPageRecord(_rootLease, page.Coordinate,
                            out SigmaDurablePageRecord record) ||
                        record.PageGeneration != staged.Update.PageGeneration)
                        throw new InvalidDataException(
                            "Resident page is not reachable from durable HEAD.");
                    ResidentPair pair = FindPhysicalPair(
                        staged.SegmentIndex, pairIndex);
                    var key = new SigmaResidencyKey(page.Coordinate,
                        record.Revision, record.PageGeneration);
                    if (pair.Occupied)
                    {
                        if (!pair.Key.Equals(key) ||
                            pair.CurrentSlot != staged.VisibleSlot)
                            throw new InvalidOperationException(
                                "A changed resident generation must retire its " +
                                "old exact locator before adoption.");
                        continue;
                    }
                    pair.Occupied = true;
                    pair.CurrentSlot = staged.VisibleSlot;
                    pair.ShadowSlot = staged.VisibleSlot ^ 1;
                    pair.CurrentGlobalSlot = pair.Target.PairFirst * 2 +
                        staged.VisibleSlot;
                    pair.ShadowGlobalSlot = pair.Target.PairFirst * 2 +
                        pair.ShadowSlot;
                    pair.ResidentGeneration = NextResidentGeneration(pair);
                    pair.State = SigmaResidencyState.HotClean;
                    pair.Key = key;
                    pair.RootHash = _rootLease.Hash;
                    pair.PageBlobHash = record.PageBlobHash;
                    pair.PersistenceLeases = 1;
                    _pending.Add(new PendingPage
                    {
                        Record = record,
                        Key = key,
                        Pair = pair,
                    });
                    updates.Add(new SigmaResidencyUpdate(key,
                        pair.ResidentGeneration, pair.Target.SegmentIndex,
                        pair.CurrentSlot, pair.CurrentGlobalSlot,
                        SigmaResidencyState.HotClean, null, 0u,
                        SigmaResidencyOperation.Upsert,
                        pair.ShadowGlobalSlot));
                }
                BuildRetirementBatches(retiredPairs, claimedPairs);
                _adoptionUpdates = updates;
                if (_retirementBatches.Count != 0)
                {
                    StartNextRetirementBatch();
                    return true;
                }
                if (updates.Count == 0)
                {
                    CompleteActivity();
                    return true;
                }
                StartAdoption();
                return true;
            }
            catch
            {
                if (_activity != SigmaPagerActivity.Faulted)
                    ReleasePendingBeforeGpu();
                throw;
            }
        }

        internal void Poll()
        {
            ThrowIfDisposed();
            if (_activity == SigmaPagerActivity.Idle ||
                _activity == SigmaPagerActivity.Faulted)
                return;
            if (_activity == SigmaPagerActivity.Reading)
            {
                PollDecode();
                return;
            }
            SigmaGpuCompletionStatus status = _operation.Poll(out string error);
            if (status == SigmaGpuCompletionStatus.Pending)
                return;
            if (status == SigmaGpuCompletionStatus.Faulted)
            {
                Quarantine(error ?? "Cold pager completion faulted.");
                return;
            }
            _operation.Dispose();
            _operation = null;
            switch (_activity)
            {
                case SigmaPagerActivity.IndexingCold:
                    _decodeTask = Task.Run(DecodePending);
                    _activity = SigmaPagerActivity.Reading;
                    break;
                case SigmaPagerActivity.Uploading:
                    _operation = _updates.Begin(PendingTarget(),
                        BuildLoadedUpdates());
                    _activity = SigmaPagerActivity.PublishingLoad;
                    break;
                case SigmaPagerActivity.PublishingLoad:
                    FinishLoad();
                    break;
                case SigmaPagerActivity.RetiringPhysical:
                    if (_retirementBatches.Count != 0)
                        StartNextRetirementBatch();
                    else if (_adoptionUpdates != null &&
                        _adoptionUpdates.Count != 0)
                        StartAdoption();
                    else
                        CompleteActivity();
                    break;
                case SigmaPagerActivity.PublishingAdoption:
                    FinishLoad();
                    break;
                case SigmaPagerActivity.PublishingAbsent:
                    CompleteActivity();
                    break;
                case SigmaPagerActivity.PublishingEviction:
                    FinishEviction();
                    break;
                default:
                    Quarantine("Unexpected cold pager completion state.");
                    break;
            }
        }

        internal bool TryBeginEviction(SigmaResidencyKey key)
        {
            ThrowIfDisposed();
            if (_activity != SigmaPagerActivity.Idle)
                return false;
            ResidentPair pair = FindPair(key);
            if (pair == null || pair.State != SigmaResidencyState.HotClean)
                return false;
            SigmaDurableRootLease root = _store.PinHead();
            try
            {
                bool reachable = _store.TryGetPageRecord(root,
                    key.Coordinate, out SigmaDurablePageRecord record) &&
                    record.PageGeneration == key.PageGeneration &&
                    record.PageBlobHash == pair.PageBlobHash;
                bool supportVerified = reachable &&
                    _store.IsSupportSummaryVerified(root, record);
                if (!reachable || !supportVerified || !LifetimeGate(pair,
                        out _))
                    return false;
                _rootLease = root;
                root = null;
                pair.State = SigmaResidencyState.Evicting;
                pair.PersistenceLeases = 1;
                _pending.Add(new PendingPage
                {
                    Record = record,
                    Key = key,
                    Pair = pair,
                });
                try
                {
                    _operation = _updates.Begin(pair.Target, new[]
                    {
                        BuildEvictionUpdate(pair)
                    });
                    _activity = SigmaPagerActivity.PublishingEviction;
                    return true;
                }
                catch (Exception exception)
                {
                    Quarantine("Cold eviction publication submission became " +
                        "uncertain: " + exception.Message);
                    throw;
                }
            }
            finally
            {
                root?.Dispose();
            }
        }

        internal bool TryBeginAnyEviction(out SigmaResidencyKey key)
            => TryBeginAnyEviction(null, out key);

        internal bool TryBeginAnyEviction(
            ISet<SigmaResidencyKey> protectedKeys,
            out SigmaResidencyKey key)
        {
            ThrowIfDisposed();
            key = default;
            if (_activity != SigmaPagerActivity.Idle)
                return false;
            foreach (ResidentPair pair in _pairs)
            {
                if (!pair.Occupied || pair.State !=
                    SigmaResidencyState.HotClean ||
                    (protectedKeys != null &&
                        protectedKeys.Contains(pair.Key)) ||
                    !LifetimeGate(pair, out _))
                    continue;
                key = pair.Key;
                return TryBeginEviction(key);
            }
            return false;
        }

        internal bool TryBeginNativeTargetEviction(int requiredFreePairs,
            ISet<SigmaResidencyKey> protectedKeys,
            out SigmaResidencyKey key)
        {
            ThrowIfDisposed();
            key = default;
            if (_activity != SigmaPagerActivity.Idle ||
                requiredFreePairs <= 0 ||
                requiredFreePairs > _targets[0].PairCount)
                return false;

            int selectedSegment = SelectNativeTarget().SegmentIndex;
            int free = 0;
            int evictable = 0;
            foreach (ResidentPair pair in _pairs)
            {
                if (pair.Target.SegmentIndex != selectedSegment)
                    continue;
                if (!pair.Occupied)
                {
                    ++free;
                    continue;
                }
                if (pair.State == SigmaResidencyState.HotClean &&
                    (protectedKeys == null ||
                        !protectedKeys.Contains(pair.Key)) &&
                    LifetimeGate(pair, out _))
                    ++evictable;
            }
            if (free >= requiredFreePairs ||
                free + evictable < requiredFreePairs)
                return false;
            foreach (ResidentPair pair in _pairs)
            {
                if (pair.Target.SegmentIndex != selectedSegment ||
                    !pair.Occupied ||
                    pair.State != SigmaResidencyState.HotClean ||
                    (protectedKeys != null &&
                        protectedKeys.Contains(pair.Key)) ||
                    !LifetimeGate(pair, out _))
                    continue;
                key = pair.Key;
                return TryBeginEviction(key);
            }
            return false;
        }

        internal void CollectResidentKeys(List<SigmaResidencyKey> destination)
        {
            ThrowIfDisposed();
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            foreach (ResidentPair pair in _pairs)
                if (pair.Occupied &&
                    (pair.State == SigmaResidencyState.HotClean ||
                     pair.State == SigmaResidencyState.HotDirty))
                    destination.Add(pair.Key);
        }

        internal bool TryTakeCompletedEviction(out SigmaResidencyKey key,
            out int pairIndex)
        {
            bool result = TryTakeCompletedEviction(out key,
                out _, out pairIndex);
            return result;
        }

        internal bool TryTakeCompletedEviction(out SigmaResidencyKey key,
            out int segmentIndex, out int pairIndex)
        {
            key = _completedEvictionKey;
            segmentIndex = _completedEvictionSegment;
            pairIndex = _completedEvictionPair;
            if (pairIndex < 0)
                return false;
            _completedEvictionKey = default;
            _completedEvictionSegment = -1;
            _completedEvictionPair = -1;
            return true;
        }

        internal bool TryTakeCompletedLoad(out SigmaStagedDurablePage page)
        {
            ThrowIfDisposed();
            if (_completedLoads.Count == 0)
            {
                page = default;
                return false;
            }
            page = _completedLoads.Dequeue();
            return true;
        }

        internal IDisposable AcquireLease(SigmaResidencyKey key,
            SigmaResidencyLeaseKind kind)
        {
            ThrowIfDisposed();
            ResidentPair pair = FindPair(key) ?? throw new InvalidOperationException(
                "Cannot lease a nonresident exact page generation.");
            IncrementLease(pair, kind);
            return new PageLease(this, pair, kind);
        }

        internal void SetLastReader(SigmaResidencyKey key,
            ISigmaResidencyCompletion completion) =>
            (FindPair(key) ?? throw new InvalidOperationException(
                "Unknown resident generation.")).LastReader = completion ??
                throw new ArgumentNullException(nameof(completion));

        internal void SetLastWriter(SigmaResidencyKey key,
            ISigmaResidencyCompletion completion) =>
            (FindPair(key) ?? throw new InvalidOperationException(
                "Unknown resident generation.")).LastWriter = completion ??
                throw new ArgumentNullException(nameof(completion));

        internal SigmaResidencyState StateOf(SigmaResidencyKey key)
        {
            ResidentPair pair = FindPair(key);
            if (pair != null)
                return pair.State;
            if (_store.Head != null && _store.TryGetPageRecord(key.Coordinate,
                    out SigmaDurablePageRecord record) &&
                record.Revision == key.RootContext &&
                record.PageGeneration == key.PageGeneration)
                return SigmaResidencyState.ColdDurable;
            return SigmaResidencyState.AbsentInRoot;
        }

        private void BuildRetirementBatches(
            IReadOnlyList<SigmaResidentPairAddress> retiredPairs,
            ISet<long> adoptedPairs)
        {
            _retirementBatches.Clear();
            for (int segment = 0; segment < _targets.Length; ++segment)
            {
                SigmaCarrierReadBatch target = _targets[segment];
                var updates = new List<SigmaResidencyUpdate>();
                for (int index = 0; index < retiredPairs.Count; ++index)
                {
                    SigmaResidentPairAddress retired = retiredPairs[index];
                    if (retired.SegmentIndex != segment)
                        continue;
                    if ((uint)retired.PairIndex >= (uint)target.PairCount ||
                        adoptedPairs.Contains(PhysicalPairKey(segment,
                            retired.PairIndex)))
                        throw new InvalidDataException(
                            "Post-HEAD retirement aliases an adopted pair.");
                    int slot = retired.PairIndex * 2;
                    int global = target.PairFirst * 2 + slot;
                    updates.Add(new SigmaResidencyUpdate(retired.Key, 1u,
                        segment, slot, global,
                        SigmaResidencyState.AbsentInRoot, null, 0u,
                        SigmaResidencyOperation.RetirePhysical, global + 1));
                }
                if (updates.Count != 0)
                    _retirementBatches.Enqueue(new RetirementBatch
                    {
                        Target = target,
                        Updates = updates,
                    });
            }
        }

        private void StartNextRetirementBatch()
        {
            RetirementBatch batch = _retirementBatches.Dequeue();
            _operation = _updates.Begin(batch.Target, batch.Updates);
            _activity = SigmaPagerActivity.RetiringPhysical;
        }

        private void StartAdoption()
        {
            _operation = _updates.Begin(_targets[0], _adoptionUpdates);
            _activity = SigmaPagerActivity.PublishingAdoption;
        }

        private IReadOnlyList<SigmaResidencyUpdate> BuildColdIndexUpdates()
        {
            var updates = new SigmaResidencyUpdate[_pending.Count];
            for (int index = 0; index < updates.Length; ++index)
                updates[index] = new SigmaResidencyUpdate(_pending[index].Key,
                    0u, -1, -1, -1, SigmaResidencyState.ColdDurable);
            return updates;
        }

        private IReadOnlyList<SigmaResidencyUpdate> BuildLoadedUpdates()
        {
            var updates = new SigmaResidencyUpdate[_pending.Count];
            for (int index = 0; index < updates.Length; ++index)
            {
                ResidentPair pair = _pending[index].Pair;
                updates[index] = new SigmaResidencyUpdate(pair.Key,
                    pair.ResidentGeneration, pair.Target.SegmentIndex,
                    pair.CurrentSlot, pair.CurrentGlobalSlot,
                    SigmaResidencyState.HotClean,
                    SigmaResidencyState.ColdDurable, 0u,
                    SigmaResidencyOperation.PublishLoaded,
                    pair.ShadowGlobalSlot);
            }
            return updates;
        }

        private SigmaResidencyUpdate BuildEvictionUpdate(ResidentPair pair) =>
            new(pair.Key, pair.ResidentGeneration, pair.Target.SegmentIndex,
                pair.CurrentSlot, pair.CurrentGlobalSlot,
                SigmaResidencyState.ColdDurable,
                SigmaResidencyState.HotClean, 0u,
                SigmaResidencyOperation.Evict, pair.ShadowGlobalSlot);

        private SigmaColdDecodedBatch DecodePending()
        {
            var pages = new SigmaDecodedPage[_pending.Count];
            for (int index = 0; index < pages.Length; ++index)
            {
                PendingPage pending = _pending[index];
                if (!_store.TryReadCanonicalPage(_rootLease, pending.Record,
                        out SigmaDecodedPage page))
                    throw new InvalidOperationException(
                        "Durable PageBlob is missing for " +
                        pending.Key.Coordinate + ".");
                // Missing/stale/corrupt support summaries remain conservative;
                // this load proceeds instead of omitting the page.
                _store.IsSupportSummaryVerified(_rootLease, pending.Record);
                pending.Decoded = page;
                pages[index] = page;
            }
            return PackDecodedPages(pages);
        }

        private SigmaColdDecodedBatch PackDecodedPages(
            IReadOnlyList<SigmaDecodedPage> pages)
        {
            var state = new long[checked(pages.Count *
                SigmaCarrier.PageLaneCount)];
            var representation = new uint[checked(pages.Count *
                SigmaCarrier.SamplesPerPage *
                SigmaCarrier.RepresentationWordsPerSample * 4)];
            var controls = new SigmaColdUploadPageGpu[pages.Count];
            for (int pageIndex = 0; pageIndex < pages.Count; ++pageIndex)
            {
                SigmaDecodedPage page = pages[pageIndex];
                int stateBase = pageIndex * SigmaCarrier.PageLaneCount;
                for (int sample = 0; sample < SigmaCarrier.SamplesPerPage;
                    ++sample)
                {
                    SigmaS16 value = page.SampleAt(sample);
                    for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                        state[stateBase + sample * SigmaS16.LaneCount + lane] =
                            value[lane];
                }
                int representationBase = pageIndex *
                    SigmaCarrier.SamplesPerPage *
                    SigmaCarrier.RepresentationWordsPerSample * 4;
                for (int recordIndex = 0;
                    recordIndex < page.RepresentationCount; ++recordIndex)
                {
                    SigmaCarrierRepresentationRecord record =
                        page.RepresentationAt(recordIndex);
                    Array.Copy(record.Words, 0, representation,
                        representationBase + record.SampleIndex *
                            SigmaCarrier.RepresentationWordsPerSample * 4,
                        record.Words.Length);
                }
                PendingPage pending = _pending[pageIndex];
                ResidentPair pair = pending.Pair;
                controls[pageIndex] = SigmaColdUploadPageGpu.Create(page,
                    pending.Key, pair.ResidentGeneration,
                    pair.Target.SegmentIndex, pair.CurrentSlot,
                    pair.CurrentGlobalSlot, pair.ShadowGlobalSlot);
            }
            return new SigmaColdDecodedBatch(state, representation, controls);
        }

        private void PollDecode()
        {
            if (!_decodeTask.IsCompleted)
                return;
            if (_decodeTask.IsFaulted || _decodeTask.IsCanceled)
            {
                string error = _decodeTask.Exception?.GetBaseException().Message ??
                    "Cold decode task was cancelled.";
                Quarantine(error);
                return;
            }
            try
            {
                _operation = _upload.Begin(PendingTarget(),
                    _decodeTask.Result);
                _decodeTask = null;
                _activity = SigmaPagerActivity.Uploading;
            }
            catch (Exception exception)
            {
                Quarantine("Cold upload submission failed: " + exception.Message);
            }
        }

        private void FinishLoad()
        {
            bool completedRehydrate = _activity ==
                SigmaPagerActivity.PublishingLoad;
            foreach (PendingPage pending in _pending)
            {
                ResidentPair pair = pending.Pair;
                pair.State = SigmaResidencyState.HotClean;
                pair.PersistenceLeases = 0;
                pair.LastReader = SigmaCompletedResidencyCompletion.Instance;
                pair.LastWriter = SigmaCompletedResidencyCompletion.Instance;
                if (completedRehydrate)
                {
                    if (pending.Decoded == null)
                        throw new InvalidDataException(
                            "A completed cold load lost its decoded page receipt.");
                    _completedLoads.Enqueue(new SigmaStagedDurablePage(
                        pair.Target.SegmentIndex, pair.CurrentSlot,
                        new SigmaDurablePageUpdate(
                            pending.Decoded, pending.Key.PageGeneration,
                            pending.Record.QuerySupport)));
                }
            }
            CompleteActivity();
        }

        private void FinishEviction()
        {
            ResidentPair pair = _pending[0].Pair;
            _completedEvictionKey = pair.Key;
            _completedEvictionSegment = pair.Target.SegmentIndex;
            _completedEvictionPair = pair.PairIndex;
            pair.Occupied = false;
            pair.State = SigmaResidencyState.AbsentInRoot;
            pair.Key = default;
            pair.RootHash = default;
            pair.PageBlobHash = default;
            pair.PublicationLeases = 0;
            pair.ReadoutLeases = 0;
            pair.PersistenceLeases = 0;
            pair.LastReader = null;
            pair.LastWriter = null;
            CompleteActivity();
        }

        private void CompleteActivity()
        {
            _pending.Clear();
            _retirementBatches.Clear();
            _adoptionUpdates = null;
            _rootLease?.Dispose();
            _rootLease = null;
            _decodeTask = null;
            _activity = SigmaPagerActivity.Idle;
        }

        private void Quarantine(string error)
        {
            _operation?.Dispose();
            _operation = null;
            foreach (PendingPage pending in _pending)
            {
                pending.Pair.State = SigmaResidencyState.Quarantined;
                pending.Pair.PersistenceLeases = Math.Max(1,
                    pending.Pair.PersistenceLeases);
            }
            _rootLease?.Dispose();
            _rootLease = null;
            _decodeTask = null;
            _retirementBatches.Clear();
            _adoptionUpdates = null;
            _fault = error;
            _activity = SigmaPagerActivity.Faulted;
        }

        private void ReleasePendingBeforeGpu()
        {
            foreach (PendingPage pending in _pending)
            {
                pending.Pair.Occupied = false;
                pending.Pair.State = SigmaResidencyState.AbsentInRoot;
                pending.Pair.Key = default;
                pending.Pair.PersistenceLeases = 0;
            }
            _pending.Clear();
            _retirementBatches.Clear();
            _adoptionUpdates = null;
            _rootLease?.Dispose();
            _rootLease = null;
            _activity = SigmaPagerActivity.Idle;
        }

        private SigmaCarrierReadBatch SelectLoadTarget(int count)
        {
            // Prefer the highest numbered disposable bank. This keeps the
            // primary bank available for NativeClose targets without making
            // segment order part of page identity.
            for (int index = _targets.Length - 1; index >= 0; --index)
                if (FreePairCountInSegment(index) >= count)
                    return _targets[index];
            throw new InvalidOperationException(
                "No single resident binding bank can accept the bounded cold " +
                "batch; admission must backpressure.");
        }

        private ResidentPair ReservePair(SigmaCarrierReadBatch target)
        {
            foreach (ResidentPair pair in _pairs)
            {
                if (pair.Occupied ||
                    pair.Target.SegmentIndex != target.SegmentIndex)
                    continue;
                pair.Occupied = true;
                pair.State = SigmaResidencyState.Loading;
                return pair;
            }
            throw new InvalidOperationException(
                "No legal resident pair is available; scan admission must " +
                "backpressure until dirty/leased/completion work advances.");
        }

        private SigmaCarrierReadBatch PendingTarget()
        {
            if (_pending.Count == 0)
                throw new InvalidOperationException(
                    "Cold pager operation has no target bank.");
            SigmaCarrierReadBatch target = _pending[0].Pair.Target;
            for (int index = 1; index < _pending.Count; ++index)
                if (_pending[index].Pair.Target.SegmentIndex !=
                    target.SegmentIndex)
                    throw new InvalidOperationException(
                        "One cold GPU batch crossed resident binding banks.");
            return target;
        }

        private ResidentPair FindPhysicalPair(int segmentIndex, int pairIndex)
        {
            foreach (ResidentPair pair in _pairs)
                if (pair.Target.SegmentIndex == segmentIndex &&
                    pair.PairIndex == pairIndex)
                    return pair;
            throw new ArgumentOutOfRangeException(nameof(pairIndex));
        }

        private static long PhysicalPairKey(int segmentIndex, int pairIndex) =>
            ((long)segmentIndex << 32) | unchecked((uint)pairIndex);

        private ResidentPair FindPair(SigmaResidencyKey key)
        {
            foreach (ResidentPair pair in _pairs)
                if (pair.Occupied && pair.Key.Equals(key))
                    return pair;
            return null;
        }

        private bool TryGetResidentAppendTail(out ResidentPair pair)
        {
            pair = null;
            uint extent = _targets[0].DurableLogicalExtent;
            if (extent == 0u ||
                extent % SigmaCarrier.SamplesPerPage == 0u)
                return false;

            var coordinate = new SigmaCarrierPageCoordinate(
                extent / SigmaCarrier.SamplesPerPage, 0L);
            if (!_store.TryGetPageRecord(coordinate,
                    out SigmaDurablePageRecord record))
                throw new InvalidDataException(
                    "The durable append tail is absent from selected HEAD.");

            var key = new SigmaResidencyKey(coordinate,
                record.Revision, record.PageGeneration);
            pair = FindPair(key);
            if (pair == null ||
                (pair.State != SigmaResidencyState.HotClean &&
                 pair.State != SigmaResidencyState.HotDirty))
                throw new InvalidOperationException(
                    "The exact durable append tail is not resident; native " +
                    "admission must backpressure until rehydrate completes.");
            return true;
        }

        private static uint NextResidentGeneration(ResidentPair pair) =>
            pair.ResidentGeneration == uint.MaxValue
                ? 1u : pair.ResidentGeneration + 1u;

        private static bool LifetimeGate(ResidentPair pair, out string error)
        {
            error = null;
            if (pair.PublicationLeases != 0 || pair.ReadoutLeases != 0 ||
                pair.PersistenceLeases != 0)
                return false;
            if (!CompletionPassed(pair.LastReader, "reader", out error) ||
                !CompletionPassed(pair.LastWriter, "writer", out error))
            {
                if (error != null)
                    pair.State = SigmaResidencyState.Quarantined;
                return false;
            }
            return true;
        }

        private static bool CompletionPassed(ISigmaResidencyCompletion probe,
            string label, out string error)
        {
            error = null;
            if (probe == null)
                return false;
            SigmaGpuCompletionStatus status = probe.Poll(out string probeError);
            if (status == SigmaGpuCompletionStatus.Complete)
                return true;
            if (status == SigmaGpuCompletionStatus.Faulted)
                error = "Last GPU " + label + " completion faulted: " +
                    probeError;
            return false;
        }

        private static void IncrementLease(ResidentPair pair,
            SigmaResidencyLeaseKind kind)
        {
            switch (kind)
            {
                case SigmaResidencyLeaseKind.Publication:
                    pair.PublicationLeases++;
                    break;
                case SigmaResidencyLeaseKind.Readout:
                    pair.ReadoutLeases++;
                    break;
                case SigmaResidencyLeaseKind.Persistence:
                    pair.PersistenceLeases++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static void DecrementLease(ResidentPair pair,
            SigmaResidencyLeaseKind kind)
        {
            int value;
            switch (kind)
            {
                case SigmaResidencyLeaseKind.Publication:
                    value = --pair.PublicationLeases;
                    break;
                case SigmaResidencyLeaseKind.Readout:
                    value = --pair.ReadoutLeases;
                    break;
                case SigmaResidencyLeaseKind.Persistence:
                    value = --pair.PersistenceLeases;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (value < 0)
                throw new InvalidOperationException(
                    "Resident generation lease accounting underflow.");
        }

        private sealed class PageLease : IDisposable
        {
            private SigmaCarrierPager _owner;
            private ResidentPair _pair;
            private readonly SigmaResidencyLeaseKind _kind;
            internal PageLease(SigmaCarrierPager owner, ResidentPair pair,
                SigmaResidencyLeaseKind kind)
            {
                _owner = owner;
                _pair = pair;
                _kind = kind;
            }
            public void Dispose()
            {
                if (_owner == null) return;
                DecrementLease(_pair, _kind);
                _owner = null;
                _pair = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_activity != SigmaPagerActivity.Idle &&
                _activity != SigmaPagerActivity.Faulted)
                throw new InvalidOperationException(
                    "Pager teardown is blocked by pending cold completion.");
            _disposed = true;
            _operation?.Dispose();
            _operation = null;
            _rootLease?.Dispose();
            _rootLease = null;
            _completedLoads.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SigmaCarrierPager));
        }
    }
}
