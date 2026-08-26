using System;
using System.Collections.Generic;
using UnityEngine;

namespace Genesis.RoomScan.SigmaPrism
{
    public readonly struct SigmaCarrierPageHandle
    {
        internal SigmaCarrierPageHandle(SigmaCarrierPageCoordinate coordinate,
            uint generation, int segmentIndex, int pageSlot)
        {
            Coordinate = coordinate;
            Generation = generation;
            SegmentIndex = segmentIndex;
            PageSlot = pageSlot;
        }

        public SigmaCarrierPageCoordinate Coordinate { get; }
        public uint Generation { get; }
        public int SegmentIndex { get; }
        public int PageSlot { get; }
        public bool IsValid => Generation != 0u && SegmentIndex >= 0 && PageSlot >= 0;
    }

    public readonly struct SigmaCarrierReadBatch
    {
        internal SigmaCarrierReadBatch(int segmentIndex, int capacity,
            int pairFirst, GraphicsBuffer state, GraphicsBuffer representation,
            GraphicsBuffer metadata,
            GraphicsBuffer dirtyFlags, GraphicsBuffer readoutDirtyFlags,
            GraphicsBuffer publicationRoot)
        {
            SegmentIndex = segmentIndex;
            PageCapacity = capacity;
            PairFirst = pairFirst;
            State = state;
            Representation = representation;
            Metadata = metadata;
            DirtyFlags = dirtyFlags;
            ReadoutDirtyFlags = readoutDirtyFlags;
            PublicationRoot = publicationRoot;
        }

        public int SegmentIndex { get; }
        public int PageCapacity { get; }
        public int PairFirst { get; }
        public int PairCount => PageCapacity / 2;
        public ulong ReadoutRevision => 1UL;
        public GraphicsBuffer State { get; }
        internal GraphicsBuffer Representation { get; }
        public GraphicsBuffer Metadata { get; }
        public GraphicsBuffer DirtyFlags { get; }
        public GraphicsBuffer ReadoutDirtyFlags { get; }
        internal GraphicsBuffer PublicationRoot { get; }
        internal bool HasPublicationStorage => PublicationRoot != null;
    }

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
        public const int RepresentationWordsPerSample = 18;
        public const int RepresentationWordBytes = sizeof(uint) * 4;
        public const int RepresentationPageBytes = SamplesPerPage *
            RepresentationWordsPerSample * RepresentationWordBytes;
        public const int ResidentPageBytes = DecodedPageBytes +
            RepresentationPageBytes;
        public const int PageMetadataStride = 16 * sizeof(uint);
        public const int MaximumPagesPerSegment = 256;
        public const int DefaultDecodedBudgetMegabytes = 1024;
        // N3 owns only the base-density current/shadow pair.  The decoded
        // budget is a residency ceiling, never an eager allocation target.
        // N5's pager may grow the resident set in similarly bounded quanta.
        internal const int InitialResidentPageCapacity = 2;

        private const string CarrierResource = "SigmaPrism/SigmaCarrier";
        private const long MiB = 1024L * 1024L;

        [SerializeField, Min(64)] private int decodedBudgetMegabytes =
            DefaultDecodedBudgetMegabytes;

        private readonly List<CarrierSegment> _segments = new();
        private SigmaExactBackendGate _backendGate;
        private ComputeShader _carrierShader;
        private GraphicsBuffer _publicationRoot;
        private int _initializeGpuPoolKernel;
        private int _pagesPerSegment;
        private int _decodedBudgetPages;
        private bool _initialized;
        private bool _disposed;

        public string ModuleName => "Sigma exact carrier";
        public bool IsInitialized => _initialized && !_disposed;

        public void OnModuleInitialize(RoomScanner scanner)
        {
            if (scanner == null)
                throw new ArgumentNullException(nameof(scanner));
            if (_initialized)
                return;
            if (_disposed)
                throw new ObjectDisposedException(nameof(SigmaCarrier));

            _backendGate = scanner.ExactBackendGate ?? throw new InvalidOperationException(
                "Sigma carrier requires the GPU-resident exact backend gate.");
            _carrierShader = Resources.Load<ComputeShader>(CarrierResource);
            if (_carrierShader == null)
                throw new InvalidOperationException("Sigma carrier compute resource is missing.");
            _initializeGpuPoolKernel = _carrierShader.FindProfiledKernel(
                "InitializeGpuPool");

            long bindingLimit = SystemInfo.maxGraphicsBufferSize;
            long largestPageBinding = Math.Max(DecodedPageBytes,
                RepresentationPageBytes);
            if (bindingLimit < largestPageBinding)
                throw new InvalidOperationException(
                    $"Vulkan storage-buffer range {bindingLimit} cannot hold one " +
                    $"{largestPageBinding}-byte Sigma representation page.");
            _pagesPerSegment = ComputeSegmentPageCapacity(bindingLimit);
            _decodedBudgetPages = Math.Max(2,
                checked((int)Math.Min(int.MaxValue,
                    decodedBudgetMegabytes * MiB / ResidentPageBytes))) & ~1;
            _publicationRoot = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                1, sizeof(uint)) { name = "Sigma carrier publication root" };
            _publicationRoot.SetData(new uint[1]);
            _initialized = true;
        }

        public void OnScanStarted() { }
        public void OnScanStopped() { }

        internal SigmaCarrierReadBatch AcquireGpuManagedPool()
        {
            RequireInitialized();
            if (_segments.Count == 0)
            {
                int capacity = Math.Min(InitialResidentPageCapacity,
                    Math.Min(_pagesPerSegment, _decodedBudgetPages)) & ~1;
                if (capacity < InitialResidentPageCapacity)
                    throw new InvalidOperationException(
                        "Decoded residency cannot hold the initial Sigma " +
                        "current/shadow page pair.");
                var segment = new CarrierSegment(capacity, _segments.Count);
                _segments.Add(segment);
                Initialize(segment);
            }
            return CreateReadBatch(0);
        }

        public void CollectReadableSegments(List<SigmaCarrierReadBatch> destination)
        {
            RequireInitialized();
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            for (int index = 0; index < _segments.Count; ++index)
                destination.Add(CreateReadBatch(index));
        }

        public bool TryGetLatest(SigmaCarrierPageCoordinate coordinate,
            out SigmaCarrierPageHandle handle)
        {
            handle = default;
            return false;
        }

        public static int ComputeSegmentPageCapacity(long bindingLimit)
        {
            long largestPageBinding = Math.Max(DecodedPageBytes,
                RepresentationPageBytes);
            if (bindingLimit < largestPageBinding)
                throw new ArgumentOutOfRangeException(nameof(bindingLimit));
            long aligned = bindingLimit / largestPageBinding *
                largestPageBinding;
            if (aligned == bindingLimit &&
                aligned >= 2L * largestPageBinding)
                aligned -= largestPageBinding;
            long maximum = checked((long)MaximumPagesPerSegment *
                largestPageBinding);
            int pages = checked((int)(Math.Min(maximum, aligned) /
                largestPageBinding));
            if (pages < 2)
                throw new InvalidOperationException(
                    "A Sigma carrier segment requires one current/shadow pair.");
            return Math.Max(2, Math.Min(MaximumPagesPerSegment, pages) & ~1);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (CarrierSegment segment in _segments)
                segment.Dispose();
            _segments.Clear();
            _publicationRoot?.Dispose();
            _publicationRoot = null;
            _carrierShader = null;
            _backendGate = null;
            _initialized = false;
        }

        private void OnDestroy() => Dispose();

        private void Initialize(CarrierSegment segment)
        {
            _carrierShader.SetInt("_GpuPoolPageCount", segment.Capacity);
            _carrierShader.SetInt("_PageCapacity", segment.Capacity);
            _backendGate.Bind(_carrierShader, _initializeGpuPoolKernel);
            _carrierShader.SetBuffer(_initializeGpuPoolKernel,
                "_TargetCarrierState", segment.State);
            _carrierShader.SetBuffer(_initializeGpuPoolKernel,
                "_TargetCarrierRepresentation", segment.Representation);
            _carrierShader.SetBuffer(_initializeGpuPoolKernel,
                "_PageMetadata", segment.Metadata);
            _carrierShader.SetBuffer(_initializeGpuPoolKernel,
                "_DirtyFlags", segment.DirtyFlags);
            _carrierShader.SetBuffer(_initializeGpuPoolKernel,
                "_ReadoutDirtyFlags", segment.ReadoutDirtyFlags);
            _carrierShader.Dispatch(_initializeGpuPoolKernel,
                SamplesPerPage / 64, segment.Capacity, 1);
        }

        private SigmaCarrierReadBatch CreateReadBatch(int segmentIndex)
        {
            int pairFirst = 0;
            for (int index = 0; index < segmentIndex; ++index)
                pairFirst += _segments[index].Capacity / 2;
            CarrierSegment segment = _segments[segmentIndex];
            return new SigmaCarrierReadBatch(segmentIndex, segment.Capacity,
                pairFirst, segment.State, segment.Representation,
                segment.Metadata, segment.DirtyFlags,
                segment.ReadoutDirtyFlags, _publicationRoot);
        }

        private void RequireInitialized()
        {
            if (!IsInitialized)
                throw new InvalidOperationException("Sigma carrier is not initialized.");
        }

        private sealed class CarrierSegment : IDisposable
        {
            public CarrierSegment(int capacity, int index)
            {
                Capacity = capacity;
                State = Create(GraphicsBuffer.Target.Structured,
                    checked(capacity * PageLaneCount), PackedLaneBytes,
                    $"Sigma carrier state {index}");
                Representation = Create(GraphicsBuffer.Target.Structured,
                    checked(capacity * SamplesPerPage *
                        RepresentationWordsPerSample),
                    RepresentationWordBytes,
                    $"Sigma carrier representation {index}");
                Metadata = Create(GraphicsBuffer.Target.Structured, capacity,
                    PageMetadataStride, $"Sigma carrier metadata {index}");
                DirtyFlags = Create(GraphicsBuffer.Target.Structured, capacity,
                    sizeof(uint), $"Sigma carrier dirty flags {index}");
                ReadoutDirtyFlags = Create(GraphicsBuffer.Target.Structured,
                    capacity, sizeof(uint),
                    $"Sigma carrier readout dirty flags {index}");
                DirtyFlags.SetData(new uint[capacity]);
                ReadoutDirtyFlags.SetData(new uint[capacity]);
            }

            public int Capacity { get; }
            public GraphicsBuffer State { get; }
            public GraphicsBuffer Representation { get; }
            public GraphicsBuffer Metadata { get; }
            public GraphicsBuffer DirtyFlags { get; }
            public GraphicsBuffer ReadoutDirtyFlags { get; }

            public void Dispose()
            {
                State.Dispose();
                Representation.Dispose();
                Metadata.Dispose();
                DirtyFlags.Dispose();
                ReadoutDirtyFlags.Dispose();
            }

            private static GraphicsBuffer Create(GraphicsBuffer.Target target,
                int count, int stride, string name) =>
                new(target, count, stride) { name = name };
        }
    }
}
