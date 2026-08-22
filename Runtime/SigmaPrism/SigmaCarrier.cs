using System;
using System.Collections.Generic;
using UnityEngine;

namespace Genesis.RoomScan.SigmaPrism
{
    [Flags]
    public enum SigmaCarrierPageFlags : uint
    {
        None = 0,
        Allocated = 1u << 0,
        Published = 1u << 1,
        Dirty = 1u << 2,
    }

    public readonly struct SigmaCarrierPageHandle :
        IEquatable<SigmaCarrierPageHandle>
    {
        internal SigmaCarrierPageHandle(SigmaCarrierPageCoordinate coordinate,
            uint generation, uint revision, ulong certificateOffset,
            uint certificateCount, int segmentIndex, int pageSlot)
        {
            Coordinate = coordinate;
            Generation = generation;
            Revision = revision;
            CertificateOffset = certificateOffset;
            CertificateCount = certificateCount;
            SegmentIndex = segmentIndex;
            PageSlot = pageSlot;
        }

        public SigmaCarrierPageCoordinate Coordinate { get; }
        public uint Generation { get; }
        public uint Revision { get; }
        public ulong CertificateOffset { get; }
        public uint CertificateCount { get; }
        public int SegmentIndex { get; }
        public int PageSlot { get; }
        public bool IsValid => Generation != 0u && SegmentIndex >= 0 && PageSlot >= 0;

        public bool Equals(SigmaCarrierPageHandle other) =>
            Coordinate.Equals(other.Coordinate) && Generation == other.Generation &&
            SegmentIndex == other.SegmentIndex && PageSlot == other.PageSlot;
        public override bool Equals(object obj) =>
            obj is SigmaCarrierPageHandle other && Equals(other);
        public override int GetHashCode() =>
            HashCode.Combine(Coordinate, Generation, SegmentIndex, PageSlot);
        public override string ToString() =>
            $"{Coordinate}@{Generation} [{SegmentIndex}:{PageSlot}]";
    }

    /// <summary>
    /// An unpublished immutable-generation destination. Only this lease exposes a
    /// writable page binding. Publishing makes the generation read-only; disposing
    /// an unpublished lease aborts it without reusing its generation number.
    /// </summary>
    public sealed class SigmaCarrierWriteLease : IDisposable
    {
        private SigmaCarrier _owner;
        private bool _published;

        internal SigmaCarrierWriteLease(SigmaCarrier owner,
            SigmaCarrierPageHandle handle)
        {
            _owner = owner;
            Handle = handle;
        }

        public SigmaCarrierPageHandle Handle { get; }
        public bool IsPublished => _published;

        public void BindWritable(ComputeShader shader, int kernel,
            string stateBufferName, string pageSlotName, string pageCapacityName)
        {
            if (_owner == null || _published)
                throw new InvalidOperationException(
                    "Only an active unpublished Sigma generation is writable.");
            _owner.BindWritable(this, shader, kernel, stateBufferName,
                pageSlotName, pageCapacityName);
        }

        public SigmaCarrierPageHandle Publish()
        {
            if (_owner == null)
                throw new ObjectDisposedException(nameof(SigmaCarrierWriteLease));
            if (_published)
                return Handle;
            _owner.Publish(this);
            _published = true;
            return Handle;
        }

        public void Dispose()
        {
            SigmaCarrier owner = _owner;
            _owner = null;
            if (owner != null && !_published)
                owner.Abort(this);
        }
    }

    public readonly struct SigmaCarrierDirtyBatch
    {
        internal SigmaCarrierDirtyBatch(int segmentIndex, int capacity,
            GraphicsBuffer slots, GraphicsBuffer count, GraphicsBuffer dispatchArgs)
        {
            SegmentIndex = segmentIndex;
            PageCapacity = capacity;
            PageSlots = slots;
            Count = count;
            DispatchArguments = dispatchArgs;
        }

        public int SegmentIndex { get; }
        public int PageCapacity { get; }
        public GraphicsBuffer PageSlots { get; }
        public GraphicsBuffer Count { get; }
        public GraphicsBuffer DispatchArguments { get; }
    }

    /// <summary>
    /// Sparse GPU-resident Psi carrier. Signed logical coordinates and generation
    /// maps are scheduling metadata only; every allocated physical sample is one
    /// exact packed Q16.48 S16 state. Missing pages are the single implicit z_null.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class SigmaCarrier : MonoBehaviour, IRoomScanModule, IDisposable
    {
        public const int PageSize = SigmaDecodedPage.PageSize;
        public const int BlockSize = SigmaDecodedPage.BlockSize;
        public const int BlocksPerPage = SigmaDecodedPage.BlockCount;
        public const int SamplesPerPage = SigmaDecodedPage.SampleCount;
        public const int LanesPerSample = SigmaS16.LaneCount;
        public const int PackedLaneBytes = sizeof(uint) * 2;
        public const int PageLaneCount = SamplesPerPage * LanesPerSample;
        public const int DecodedPageBytes = PageLaneCount * PackedLaneBytes;
        public const int PageMetadataStride = 12 * sizeof(uint);
        public const int MaximumPagesPerSegment = 256;

        private const string CarrierResource = "SigmaPrism/SigmaCarrier";
        private const string CodecResource = "SigmaPrism/SigmaCarrierCodec";
        private const long MiB = 1024L * 1024L;

        [Header("Exact decoded carrier residency")]
        [SerializeField, Range(8, 64)] private int segmentMegabytes = 32;
        [SerializeField, Range(64, 240)] private int decodedBudgetMegabytes = 240;

        private readonly List<CarrierSegment> _segments = new();
        private readonly Dictionary<SigmaCarrierPageCoordinate,
            SigmaCarrierPageHandle> _latest = new();
        private readonly Dictionary<SigmaCarrierPageCoordinate, uint>
            _lastGeneration = new();
        private readonly Dictionary<PageGenerationKey, SigmaCarrierPageHandle>
            _allocated = new();
        private readonly HashSet<PageGenerationKey> _pending = new();

        private RoomScanner _scanner;
        private SigmaExactBackendGate _backendGate;
        private ComputeShader _carrierShader;
        private ComputeShader _codecShader;
        private int _initializeNullKernel;
        private int _cloneKernel;
        private int _publishKernel;
        private int _markDirtyKernel;
        private int _releaseKernel;
        private int _compactKernel;
        private int _acknowledgeKernel;
        private int _pagesPerSegment;
        private int _decodedBudgetPages;
        private long _runtimeBindingLimit;
        private bool _initialized;
        private bool _disposed;

        public string ModuleName => "Sigma exact carrier";
        public bool IsInitialized => _initialized && !_disposed;
        public int ResidentPageCount => _allocated.Count;
        public int SegmentCount => _segments.Count;
        public int PagesPerSegment => _pagesPerSegment;
        public int DecodedBudgetPages => _decodedBudgetPages;
        public long RuntimeStorageBufferLimitBytes => _runtimeBindingLimit;
        public long AllocatedDecodedBytes
        {
            get
            {
                long bytes = 0L;
                for (int index = 0; index < _segments.Count; ++index)
                    bytes += (long)_segments[index].Capacity * DecodedPageBytes;
                return bytes;
            }
        }

        /// <summary>Execution resource only; it never defines persistence bytes.</summary>
        public ComputeShader CodecShader
        {
            get
            {
                RequireInitialized();
                return _codecShader;
            }
        }

        public SigmaS16 ImplicitState => SigmaOperatorSet.Canonical.NullState;

        public void OnModuleInitialize(RoomScanner scanner)
        {
            if (scanner == null)
                throw new ArgumentNullException(nameof(scanner));
            if (_initialized)
                return;
            if (_disposed)
                throw new ObjectDisposedException(nameof(SigmaCarrier));

            _scanner = scanner;
            _backendGate = scanner.ExactBackendGate ?? throw new InvalidOperationException(
                "Sigma carrier requires the GPU-resident exact backend gate.");
            _carrierShader = Resources.Load<ComputeShader>(CarrierResource);
            _codecShader = Resources.Load<ComputeShader>(CodecResource);
            if (_carrierShader == null || _codecShader == null)
                throw new InvalidOperationException(
                    "Sigma carrier/codec compute resources are missing.");

            _initializeNullKernel = _carrierShader.FindKernel("InitializeNullPage");
            _cloneKernel = _carrierShader.FindKernel("ClonePageGeneration");
            _publishKernel = _carrierShader.FindKernel("PublishPageGeneration");
            _markDirtyKernel = _carrierShader.FindKernel("MarkPageDirty");
            _releaseKernel = _carrierShader.FindKernel("ReleasePageSlot");
            _compactKernel = _carrierShader.FindKernel("CompactDirtyPages");
            _acknowledgeKernel = _carrierShader.FindKernel("AcknowledgeDirtyPages");

            _runtimeBindingLimit = SystemInfo.maxGraphicsBufferSize;
            if (_runtimeBindingLimit < DecodedPageBytes)
                throw new InvalidOperationException(
                    $"Vulkan storage-buffer range {_runtimeBindingLimit} cannot hold one " +
                    $"{DecodedPageBytes}-byte Sigma page.");
            _pagesPerSegment = ComputeSegmentPageCapacity(_runtimeBindingLimit,
                segmentMegabytes);
            _decodedBudgetPages = Math.Max(1,
                checked((int)Math.Min(int.MaxValue,
                    decodedBudgetMegabytes * MiB / DecodedPageBytes)));
            _initialized = true;
            Logger.Info($"Sigma carrier ready: implicit z_null, page=64x64, " +
                        $"block=8x8, segmentPages={_pagesPerSegment}, " +
                        $"budgetPages={_decodedBudgetPages}, " +
                        $"bindingLimit={_runtimeBindingLimit} bytes.");
        }

        public void OnScanStarted() { }
        public void OnScanStopped() { }

        public static int ComputeSegmentPageCapacity(long runtimeBindingLimitBytes,
            int requestedMegabytes)
        {
            if (runtimeBindingLimitBytes < DecodedPageBytes)
                throw new ArgumentOutOfRangeException(nameof(runtimeBindingLimitBytes));
            if (requestedMegabytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(requestedMegabytes));
            long requested = checked((long)requestedMegabytes * MiB);
            long safeLimit = runtimeBindingLimitBytes;
            // Do not bind exactly at a driver-advertised edge when at least two
            // complete pages fit; retain one-page alignment headroom.
            long alignedLimit = safeLimit / DecodedPageBytes * DecodedPageBytes;
            if (alignedLimit == safeLimit && alignedLimit >= 2L * DecodedPageBytes)
                alignedLimit -= DecodedPageBytes;
            long segmentBytes = Math.Min(requested, alignedLimit);
            int pages = checked((int)(segmentBytes / DecodedPageBytes));
            return Math.Max(1, Math.Min(MaximumPagesPerSegment, pages));
        }

        public bool TryGetLatest(SigmaCarrierPageCoordinate coordinate,
            out SigmaCarrierPageHandle handle)
        {
            RequireInitialized();
            return _latest.TryGetValue(coordinate, out handle);
        }

        public SigmaCarrierWriteLease BeginNullGeneration(
            SigmaCarrierPageCoordinate coordinate, uint revision,
            ulong certificateOffset = 0UL, uint certificateCount = 0u)
        {
            RequireInitialized();
            EnsureNoPendingCoordinate(coordinate);
            SigmaCarrierPageHandle handle = ReserveGeneration(coordinate, revision,
                certificateOffset, certificateCount);
            CarrierSegment segment = _segments[handle.SegmentIndex];
            BindPageMutation(_initializeNullKernel, segment, handle);
            _carrierShader.SetBuffer(_initializeNullKernel, "_TargetCarrierState",
                segment.State);
            _carrierShader.SetBuffer(_initializeNullKernel, "_PageMetadata",
                segment.Metadata);
            _carrierShader.SetBuffer(_initializeNullKernel, "_DirtyFlags",
                segment.DirtyFlags);
            _carrierShader.Dispatch(_initializeNullKernel,
                SamplesPerPage / 64, 1, 1);
            return new SigmaCarrierWriteLease(this, handle);
        }

        public SigmaCarrierWriteLease BeginNextGeneration(
            SigmaCarrierPageCoordinate coordinate, uint revision,
            ulong certificateOffset, uint certificateCount)
        {
            RequireInitialized();
            EnsureNoPendingCoordinate(coordinate);
            if (!_latest.TryGetValue(coordinate, out SigmaCarrierPageHandle source))
                throw new InvalidOperationException(
                    "Cannot clone an implicit-null page; begin a null generation instead.");
            SigmaCarrierPageHandle handle = ReserveGeneration(coordinate, revision,
                certificateOffset, certificateCount);
            CarrierSegment sourceSegment = _segments[source.SegmentIndex];
            CarrierSegment targetSegment = _segments[handle.SegmentIndex];
            BindPageMutation(_cloneKernel, targetSegment, handle);
            _carrierShader.SetInt("_SourcePageSlot", source.PageSlot);
            _carrierShader.SetInt("_SourcePageCapacity", sourceSegment.Capacity);
            _carrierShader.SetBuffer(_cloneKernel, "_SourceCarrierState",
                sourceSegment.State);
            _carrierShader.SetBuffer(_cloneKernel, "_TargetCarrierState",
                targetSegment.State);
            _carrierShader.SetBuffer(_cloneKernel, "_PageMetadata",
                targetSegment.Metadata);
            _carrierShader.SetBuffer(_cloneKernel, "_DirtyFlags",
                targetSegment.DirtyFlags);
            _carrierShader.Dispatch(_cloneKernel, SamplesPerPage / 64, 1, 1);
            return new SigmaCarrierWriteLease(this, handle);
        }

        public void BindReadable(SigmaCarrierPageHandle handle, ComputeShader shader,
            int kernel, string stateBufferName, string pageSlotName,
            string pageCapacityName)
        {
            RequireAllocated(handle);
            if (shader == null)
                throw new ArgumentNullException(nameof(shader));
            CarrierSegment segment = _segments[handle.SegmentIndex];
            shader.SetBuffer(kernel, stateBufferName, segment.State);
            shader.SetInt(pageSlotName, handle.PageSlot);
            shader.SetInt(pageCapacityName, segment.Capacity);
        }

        public IReadOnlyList<SigmaCarrierDirtyBatch> CompactDirtyPages()
        {
            RequireInitialized();
            var batches = new SigmaCarrierDirtyBatch[_segments.Count];
            for (int index = 0; index < _segments.Count; ++index)
            {
                CarrierSegment segment = _segments[index];
                _carrierShader.SetInt("_PageCapacity", segment.Capacity);
                _carrierShader.SetBuffer(_compactKernel, "_DirtyFlags",
                    segment.DirtyFlags);
                _carrierShader.SetBuffer(_compactKernel, "_DirtyPageSlots",
                    segment.DirtySlots);
                _carrierShader.SetBuffer(_compactKernel, "_DirtyCount",
                    segment.DirtyCount);
                _carrierShader.SetBuffer(_compactKernel, "_DirtyDispatchArgs",
                    segment.DirtyDispatchArguments);
                _carrierShader.Dispatch(_compactKernel, 1, 1, 1);
                batches[index] = segment.CreateDirtyBatch(index);
            }
            return batches;
        }

        /// <summary>
        /// Called only after the compacted generations have durable ownership or a
        /// later stage's equivalent fence. It never mutates carrier coefficients.
        /// </summary>
        public void AcknowledgeDirtyBatch(SigmaCarrierDirtyBatch batch)
        {
            RequireInitialized();
            if ((uint)batch.SegmentIndex >= (uint)_segments.Count)
                throw new ArgumentOutOfRangeException(nameof(batch));
            CarrierSegment segment = _segments[batch.SegmentIndex];
            if (segment.DirtySlots != batch.PageSlots || segment.DirtyCount != batch.Count)
                throw new InvalidOperationException("Dirty batch does not belong to carrier.");
            _carrierShader.SetInt("_PageCapacity", segment.Capacity);
            _carrierShader.SetBuffer(_acknowledgeKernel, "_DirtyFlags",
                segment.DirtyFlags);
            _carrierShader.SetBuffer(_acknowledgeKernel, "_DirtyPageSlots",
                segment.DirtySlots);
            _carrierShader.SetBuffer(_acknowledgeKernel, "_DirtyCount",
                segment.DirtyCount);
            _carrierShader.SetBuffer(_acknowledgeKernel, "_PageMetadata",
                segment.Metadata);
            _carrierShader.Dispatch(_acknowledgeKernel,
                (segment.Capacity + 63) / 64, 1, 1);
        }

        public void MarkDirty(SigmaCarrierPageHandle handle)
        {
            RequireAllocated(handle);
            CarrierSegment segment = _segments[handle.SegmentIndex];
            _carrierShader.SetInt("_TargetPageSlot", handle.PageSlot);
            _carrierShader.SetInt("_PageCapacity", segment.Capacity);
            _backendGate.Bind(_carrierShader, _markDirtyKernel);
            _carrierShader.SetBuffer(_markDirtyKernel, "_DirtyFlags",
                segment.DirtyFlags);
            _carrierShader.SetBuffer(_markDirtyKernel, "_PageMetadata",
                segment.Metadata);
            _carrierShader.Dispatch(_markDirtyKernel, 1, 1, 1);
        }

        public bool TryReleaseRetiredGeneration(SigmaCarrierPageHandle handle)
        {
            RequireInitialized();
            var key = new PageGenerationKey(handle.Coordinate, handle.Generation);
            if (!_allocated.TryGetValue(key, out SigmaCarrierPageHandle existing) ||
                !existing.Equals(handle) || _pending.Contains(key))
                return false;
            if (_latest.TryGetValue(handle.Coordinate, out SigmaCarrierPageHandle latest) &&
                latest.Equals(handle))
                return false;

            CarrierSegment segment = _segments[handle.SegmentIndex];
            _carrierShader.SetInt("_TargetPageSlot", handle.PageSlot);
            _carrierShader.SetInt("_PageCapacity", segment.Capacity);
            _carrierShader.SetBuffer(_releaseKernel, "_PageMetadata", segment.Metadata);
            _carrierShader.SetBuffer(_releaseKernel, "_DirtyFlags", segment.DirtyFlags);
            _carrierShader.Dispatch(_releaseKernel, 1, 1, 1);
            _allocated.Remove(key);
            segment.ReleaseSlot(handle.PageSlot);
            return true;
        }

        internal void BindWritable(SigmaCarrierWriteLease lease, ComputeShader shader,
            int kernel, string stateBufferName, string pageSlotName,
            string pageCapacityName)
        {
            RequirePending(lease);
            if (shader == null)
                throw new ArgumentNullException(nameof(shader));
            SigmaCarrierPageHandle handle = lease.Handle;
            CarrierSegment segment = _segments[handle.SegmentIndex];
            _backendGate.Bind(shader, kernel);
            shader.SetBuffer(kernel, stateBufferName, segment.State);
            shader.SetInt(pageSlotName, handle.PageSlot);
            shader.SetInt(pageCapacityName, segment.Capacity);
        }

        internal void Publish(SigmaCarrierWriteLease lease)
        {
            RequirePending(lease);
            SigmaCarrierPageHandle handle = lease.Handle;
            CarrierSegment segment = _segments[handle.SegmentIndex];
            _carrierShader.SetInt("_TargetPageSlot", handle.PageSlot);
            _carrierShader.SetInt("_PageCapacity", segment.Capacity);
            _backendGate.Bind(_carrierShader, _publishKernel);
            _carrierShader.SetBuffer(_publishKernel, "_PageMetadata", segment.Metadata);
            _carrierShader.SetBuffer(_publishKernel, "_DirtyFlags", segment.DirtyFlags);
            _carrierShader.Dispatch(_publishKernel, 1, 1, 1);
            var key = new PageGenerationKey(handle.Coordinate, handle.Generation);
            _pending.Remove(key);
            _latest[handle.Coordinate] = handle;
        }

        internal void Abort(SigmaCarrierWriteLease lease)
        {
            if (_disposed || lease == null)
                return;
            SigmaCarrierPageHandle handle = lease.Handle;
            var key = new PageGenerationKey(handle.Coordinate, handle.Generation);
            if (!_pending.Remove(key))
                return;
            CarrierSegment segment = _segments[handle.SegmentIndex];
            _carrierShader.SetInt("_TargetPageSlot", handle.PageSlot);
            _carrierShader.SetInt("_PageCapacity", segment.Capacity);
            _carrierShader.SetBuffer(_releaseKernel, "_PageMetadata", segment.Metadata);
            _carrierShader.SetBuffer(_releaseKernel, "_DirtyFlags", segment.DirtyFlags);
            _carrierShader.Dispatch(_releaseKernel, 1, 1, 1);
            _allocated.Remove(key);
            segment.ReleaseSlot(handle.PageSlot);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            for (int index = 0; index < _segments.Count; ++index)
                _segments[index].Dispose();
            _segments.Clear();
            _latest.Clear();
            _allocated.Clear();
            _pending.Clear();
            _carrierShader = null;
            _codecShader = null;
            _backendGate = null;
            _scanner = null;
            _initialized = false;
        }

        private void OnDestroy() => Dispose();

        private SigmaCarrierPageHandle ReserveGeneration(
            SigmaCarrierPageCoordinate coordinate, uint revision,
            ulong certificateOffset, uint certificateCount)
        {
            uint previous = _lastGeneration.TryGetValue(coordinate, out uint generation)
                ? generation : 0u;
            uint next = checked(previous + 1u);
            if (next == 0u)
                throw new OverflowException("Sigma page generation exhausted.");
            (int segmentIndex, int pageSlot) = ReserveSlot();
            _lastGeneration[coordinate] = next;
            var handle = new SigmaCarrierPageHandle(coordinate, next, revision,
                certificateOffset, certificateCount, segmentIndex, pageSlot);
            var key = new PageGenerationKey(coordinate, next);
            _allocated.Add(key, handle);
            _pending.Add(key);
            return handle;
        }

        private (int segmentIndex, int pageSlot) ReserveSlot()
        {
            for (int index = 0; index < _segments.Count; ++index)
            {
                if (_segments[index].TryReserveSlot(out int slot))
                    return (index, slot);
            }
            int allocatedCapacity = 0;
            for (int index = 0; index < _segments.Count; ++index)
                allocatedCapacity += _segments[index].Capacity;
            int remaining = _decodedBudgetPages - allocatedCapacity;
            if (remaining <= 0)
                throw new InvalidOperationException(
                    "Decoded Sigma carrier residency budget is exhausted; stage/evict " +
                    "clean pages before allocating another generation.");
            int capacity = Math.Min(_pagesPerSegment, remaining);
            var segment = new CarrierSegment(capacity, _segments.Count);
            _segments.Add(segment);
            if (!segment.TryReserveSlot(out int createdSlot))
                throw new InvalidOperationException("New Sigma segment has no free page.");
            return (_segments.Count - 1, createdSlot);
        }

        private void BindPageMutation(int kernel, CarrierSegment segment,
            SigmaCarrierPageHandle handle)
        {
            _backendGate.Bind(_carrierShader, kernel);
            _carrierShader.SetInt("_TargetPageSlot", handle.PageSlot);
            _carrierShader.SetInt("_PageCapacity", segment.Capacity);
            SetUInt("_PageXLo", unchecked((uint)handle.Coordinate.X));
            SetUInt("_PageXHi", unchecked((uint)(handle.Coordinate.X >> 32)));
            SetUInt("_PageYLo", unchecked((uint)handle.Coordinate.Y));
            SetUInt("_PageYHi", unchecked((uint)(handle.Coordinate.Y >> 32)));
            SetUInt("_Generation", handle.Generation);
            SetUInt("_Revision", handle.Revision);
            SetUInt("_CertificateOffsetLo", unchecked((uint)handle.CertificateOffset));
            SetUInt("_CertificateOffsetHi", unchecked((uint)(handle.CertificateOffset >> 32)));
            SetUInt("_CertificateCount", handle.CertificateCount);
        }

        private void SetUInt(string name, uint value) =>
            _carrierShader.SetInt(name, unchecked((int)value));

        private void EnsureNoPendingCoordinate(SigmaCarrierPageCoordinate coordinate)
        {
            foreach (PageGenerationKey key in _pending)
            {
                if (key.Coordinate.Equals(coordinate))
                    throw new InvalidOperationException(
                        "Only one unpublished generation per Sigma page is permitted.");
            }
        }

        private void RequirePending(SigmaCarrierWriteLease lease)
        {
            RequireInitialized();
            if (lease == null)
                throw new ArgumentNullException(nameof(lease));
            SigmaCarrierPageHandle handle = lease.Handle;
            var key = new PageGenerationKey(handle.Coordinate, handle.Generation);
            if (!_pending.Contains(key) || !_allocated.TryGetValue(key,
                    out SigmaCarrierPageHandle allocated) || !allocated.Equals(handle))
                throw new InvalidOperationException(
                    "Sigma write lease is no longer the active unpublished generation.");
        }

        private void RequireAllocated(SigmaCarrierPageHandle handle)
        {
            RequireInitialized();
            var key = new PageGenerationKey(handle.Coordinate, handle.Generation);
            if (!handle.IsValid || !_allocated.TryGetValue(key,
                    out SigmaCarrierPageHandle allocated) || !allocated.Equals(handle))
                throw new InvalidOperationException("Unknown Sigma page generation.");
        }

        private void RequireInitialized()
        {
            if (!IsInitialized)
                throw new InvalidOperationException("Sigma carrier is not initialized.");
        }

        private readonly struct PageGenerationKey : IEquatable<PageGenerationKey>
        {
            public PageGenerationKey(SigmaCarrierPageCoordinate coordinate,
                uint generation)
            {
                Coordinate = coordinate;
                Generation = generation;
            }

            public SigmaCarrierPageCoordinate Coordinate { get; }
            public uint Generation { get; }
            public bool Equals(PageGenerationKey other) =>
                Coordinate.Equals(other.Coordinate) && Generation == other.Generation;
            public override bool Equals(object obj) =>
                obj is PageGenerationKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Coordinate, Generation);
        }

        private sealed class CarrierSegment : IDisposable
        {
            private readonly Stack<int> _freeSlots;

            public CarrierSegment(int capacity, int index)
            {
                if (capacity <= 0 || capacity > MaximumPagesPerSegment)
                    throw new ArgumentOutOfRangeException(nameof(capacity));
                Capacity = capacity;
                State = Create(GraphicsBuffer.Target.Structured,
                    checked(capacity * PageLaneCount), PackedLaneBytes,
                    $"Sigma carrier state {index}");
                Metadata = Create(GraphicsBuffer.Target.Structured, capacity,
                    PageMetadataStride, $"Sigma carrier metadata {index}");
                DirtyFlags = Create(GraphicsBuffer.Target.Structured, capacity,
                    sizeof(uint), $"Sigma carrier dirty flags {index}");
                DirtySlots = Create(GraphicsBuffer.Target.Structured, capacity,
                    sizeof(uint), $"Sigma carrier dirty slots {index}");
                DirtyCount = Create(GraphicsBuffer.Target.Structured, 1,
                    sizeof(uint), $"Sigma carrier dirty count {index}");
                DirtyDispatchArguments = Create(
                    GraphicsBuffer.Target.Structured |
                    GraphicsBuffer.Target.IndirectArguments,
                    3, sizeof(uint), $"Sigma carrier dirty dispatch {index}");
                DirtyFlags.SetData(new uint[capacity]);
                DirtyCount.SetData(new uint[1]);
                DirtyDispatchArguments.SetData(new uint[] { 0u, 1u, 1u });
                _freeSlots = new Stack<int>(capacity);
                for (int slot = capacity - 1; slot >= 0; --slot)
                    _freeSlots.Push(slot);
            }

            public int Capacity { get; }
            public GraphicsBuffer State { get; }
            public GraphicsBuffer Metadata { get; }
            public GraphicsBuffer DirtyFlags { get; }
            public GraphicsBuffer DirtySlots { get; }
            public GraphicsBuffer DirtyCount { get; }
            public GraphicsBuffer DirtyDispatchArguments { get; }

            public bool TryReserveSlot(out int slot)
            {
                if (_freeSlots.Count == 0)
                {
                    slot = -1;
                    return false;
                }
                slot = _freeSlots.Pop();
                return true;
            }

            public void ReleaseSlot(int slot)
            {
                if ((uint)slot >= Capacity)
                    throw new ArgumentOutOfRangeException(nameof(slot));
                _freeSlots.Push(slot);
            }

            public SigmaCarrierDirtyBatch CreateDirtyBatch(int segmentIndex) =>
                new(segmentIndex, Capacity, DirtySlots, DirtyCount,
                    DirtyDispatchArguments);

            public void Dispose()
            {
                State.Dispose();
                Metadata.Dispose();
                DirtyFlags.Dispose();
                DirtySlots.Dispose();
                DirtyCount.Dispose();
                DirtyDispatchArguments.Dispose();
            }

            private static GraphicsBuffer Create(GraphicsBuffer.Target target,
                int count, int stride, string name)
            {
                var buffer = new GraphicsBuffer(target, count, stride) { name = name };
                return buffer;
            }
        }
    }
}
