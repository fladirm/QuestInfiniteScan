using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Genesis.RoomScan.SigmaPrism
{
    internal sealed class SigmaCarrierRuntimeState
    {
        internal uint DurableLogicalExtent;
        internal ulong DurableRevision;
    }

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
            GraphicsBuffer publicationRoot, GraphicsBuffer residentLocator,
            GraphicsBuffer residentSlotTable, int residentLocatorCapacity,
            int residentSlotCapacity, SigmaCarrierRuntimeState runtimeState)
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
            ResidentLocator = residentLocator;
            ResidentSlotTable = residentSlotTable;
            ResidentLocatorCapacity = residentLocatorCapacity;
            ResidentSlotCapacity = residentSlotCapacity;
            RuntimeState = runtimeState ?? throw new ArgumentNullException(
                nameof(runtimeState));
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
        internal GraphicsBuffer ResidentLocator { get; }
        internal GraphicsBuffer ResidentSlotTable { get; }
        internal int ResidentLocatorCapacity { get; }
        internal int ResidentSlotCapacity { get; }
        internal SigmaCarrierRuntimeState RuntimeState { get; }
        internal uint DurableLogicalExtent => RuntimeState.DurableLogicalExtent;
        internal ulong DurableRevision => RuntimeState.DurableRevision;
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
        internal const int ResidentBindingBankCount = 2;
        public const int DefaultDecodedBudgetMegabytes = 1024;
        // N3 owns only the base-density current/shadow pair.  The decoded
        // budget is a residency ceiling, never an eager allocation target.
        // N5's pager may grow the resident set in similarly bounded quanta.
        internal const int MinimumResidentPageCapacity = 2;

        private const string CarrierResource = "SigmaPrism/SigmaCarrier";
        private const long MiB = 1024L * 1024L;
        private const long ProjectGraphicsBufferLimit = 128L * MiB;

        [SerializeField, Min(64)] private int decodedBudgetMegabytes =
            DefaultDecodedBudgetMegabytes;

        private readonly List<CarrierSegment> _segments = new();
        private SigmaExactBackendGate _backendGate;
        private ComputeShader _carrierShader;
        private GraphicsBuffer _publicationRoot;
        private SigmaCarrierResidencyResources _residency;
        private SigmaDurableStore _durableStore;
        private SigmaCarrierPersistence _persistence;
        private SigmaCarrierPager _pager;
        private Task<SigmaDurableStore> _durableOpenTask;
        private readonly SigmaCarrierRuntimeState _runtimeState = new();
        private int _initializeGpuPoolKernel;
        private int _pagesPerSegment;
        private int _decodedBudgetPages;
        private int _residentBankPageCapacity;
        private bool _initialized;
        private bool _disposed;

        public string ModuleName => "Sigma exact carrier";
        public bool IsInitialized => _initialized && !_disposed;
        internal bool HasDurableWorldContent
        {
            get
            {
                if (_durableStore == null || !_durableStore.HasHead)
                    return false;
                using SigmaDurableRootLease root = _durableStore.PinHead();
                SigmaDurableRootObject selected = root.Root;
                return !selected.SparsePageMapRootHash.IsZero ||
                    !selected.QuerySupportRootHash.IsZero ||
                    !selected.CertificateManifestRootHash.IsZero ||
                    !selected.UnresolvedFrontierRootHash.IsZero;
            }
        }
        internal SigmaCarrierResidencyResources Residency
        {
            get
            {
                RequireInitialized();
                return _residency;
            }
        }
        internal SigmaCarrierPersistence Persistence
        {
            get
            {
                RequireInitialized();
                return _persistence ?? throw new InvalidOperationException(
                    "The N5 durable owner has not acquired its resident bank.");
            }
        }
        internal SigmaCarrierPager Pager
        {
            get
            {
                RequireInitialized();
                return _pager ?? throw new InvalidOperationException(
                    "The N5 pager has not acquired its resident bank.");
            }
        }

        /// <summary>
        /// Verifies the complete HEAD-reachable immutable graph off the Unity/XR
        /// thread. Module publication still waits for the exact result, so no
        /// scanner consumer can observe a partially verified durable root.
        /// </summary>
        internal async Task PrepareDurableStoreAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SigmaCarrier));
            if (_durableStore != null)
                return;
            if (_durableOpenTask == null)
            {
                string path = Path.Combine(Application.persistentDataPath,
                    "SigmaPrism", "n5");
                _durableOpenTask = Task.Run(() => new SigmaDurableStore(path));
            }

            long started = Stopwatch.GetTimestamp();
            SigmaDurableStore opened = await _durableOpenTask;
            if (_disposed)
                throw new ObjectDisposedException(nameof(SigmaCarrier));
            _durableStore = opened;
            if (!_durableStore.HasHead)
                return;

            using SigmaDurableRootLease root = _durableStore.PinHead();
            _runtimeState.DurableRevision = root.Root.Revision;
            _runtimeState.DurableLogicalExtent =
                _durableStore.ComputeLogicalExtent(root);
            Logger.Info("Sigma N5 durable HEAD restored: revision=" +
                _runtimeState.DurableRevision + " extent=" +
                _runtimeState.DurableLogicalExtent + " pages=" +
                _durableStore.HeadInventoryPageCount + " pageBytes=" +
                _durableStore.HeadInventoryPageBytes + " openMs=" +
                ((Stopwatch.GetTimestamp() - started) * 1000.0 /
                    Stopwatch.Frequency).ToString("F3") +
                " unityThreadBlocked=0.");
        }

        internal void RebuildPagerFromDurableResidentSnapshot(
            IReadOnlyList<SigmaResidentPairAddress> retiredPairs = null)
        {
            RequireInitialized();
            if (_persistence == null)
                throw new InvalidOperationException(
                    "Durable resident snapshot is unavailable.");
            _pager?.Dispose();
            _residency.ResetDisposableIndex();
            SigmaCarrierReadBatch[] banks = CreateResidentBanks();
            _pager = new SigmaCarrierPager(_durableStore, banks,
                new SigmaNativeColdUploadBackend(),
                new SigmaGraphicsResidencyUpdateBackend(_residency));
            if (!_pager.BeginAdoptDurableSnapshot(
                    _persistence.ResidentSnapshot,
                    retiredPairs ?? Array.Empty<SigmaResidentPairAddress>()))
                throw new InvalidOperationException(
                    "Durable resident snapshot adoption did not start.");
        }

        internal async Task<SigmaDurableCommitResult> SelectEmptyDurableAsync(
            ulong revision)
        {
            RequireInitialized();
            if (_persistence == null || _pager == null)
                throw new InvalidOperationException(
                    "The durable carrier bank is not available for clear.");
            if (_persistence.Activity != SigmaDurableActivity.Idle ||
                _pager.Activity != SigmaPagerActivity.Idle ||
                SigmaNativeVulkanExecutor.HasJobInFlight ||
                SigmaNativeVulkanColdEncode.HasJobInFlight ||
                SigmaNativeVulkanColdUpload.HasJobInFlight ||
                SigmaNativeVulkanReadout.HasJobInFlight)
                throw new InvalidOperationException(
                    "Explicit clear is blocked by an unfinished GPU/durable " +
                    "owner.");

            // HEAD changes first. Until this atomic selector advances, the old
            // complete durable root remains the sole authority.
            SigmaDurableCommitResult selected = await
                _durableStore.SelectEmptyAsync(revision);

            _pager.Dispose();
            _pager = null;
            _persistence.Dispose();
            _persistence = null;
            _residency.ResetDisposableIndex();
            foreach (CarrierSegment segment in _segments)
                Initialize(segment);
            _runtimeState.DurableLogicalExtent = 0u;
            _runtimeState.DurableRevision = revision;
            _publicationRoot.SetData(new[] { checked((uint)revision) });

            SigmaCarrierReadBatch[] banks = CreateResidentBanks();
            _persistence = new SigmaCarrierPersistence(_durableStore,
                _backendGate, banks);
            _pager = new SigmaCarrierPager(_durableStore, banks,
                new SigmaNativeColdUploadBackend(),
                new SigmaGraphicsResidencyUpdateBackend(_residency));
            return selected;
        }

        public void OnModuleInitialize(RoomScanner scanner)
        {
            if (scanner == null)
                throw new ArgumentNullException(nameof(scanner));
            if (_initialized)
                return;
            if (_disposed)
                throw new ObjectDisposedException(nameof(SigmaCarrier));
            if (_durableStore == null)
                throw new InvalidOperationException(
                    "Durable HEAD verification must finish before carrier " +
                    "module publication.");

            _backendGate = scanner.ExactBackendGate ?? throw new InvalidOperationException(
                "Sigma carrier requires the GPU-resident exact backend gate.");
            _carrierShader = Resources.Load<ComputeShader>(CarrierResource);
            if (_carrierShader == null)
                throw new InvalidOperationException("Sigma carrier compute resource is missing.");
            _initializeGpuPoolKernel = _carrierShader.FindProfiledKernel(
                "InitializeGpuPool");

            long bindingLimit = Math.Min(SystemInfo.maxGraphicsBufferSize,
                ProjectGraphicsBufferLimit);
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
            _residentBankPageCapacity = Math.Min(_pagesPerSegment,
                _decodedBudgetPages / ResidentBindingBankCount) & ~1;
            if (_residentBankPageCapacity < MinimumResidentPageCapacity)
                throw new InvalidOperationException(
                    "Decoded residency budget cannot hold two complete Quest " +
                    "current/shadow binding banks.");
            _publicationRoot = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                1, sizeof(uint)) { name = "Sigma carrier publication root" };
            _publicationRoot.SetData(new uint[1]);
            _residency = new SigmaCarrierResidencyResources(_carrierShader,
                _backendGate, checked(_residentBankPageCapacity *
                    ResidentBindingBankCount));
            Logger.Info("Sigma N5 resident binding banks: banks=" +
                ResidentBindingBankCount + " pagesPerBank=" +
                _residentBankPageCapacity + " pairsTotal=" +
                (_residentBankPageCapacity * ResidentBindingBankCount / 2) +
                " stateBytesPerBank=" +
                ((long)_residentBankPageCapacity * DecodedPageBytes) +
                " representationBytesPerBank=" +
                ((long)_residentBankPageCapacity * RepresentationPageBytes) +
                ".");
            _initialized = true;
        }

        public void OnScanStarted() { }
        public void OnScanStopped() { }

        internal SigmaCarrierReadBatch AcquireGpuManagedPool()
        {
            RequireInitialized();
            if (_segments.Count == 0)
            {
                // Two equal Vulkan binding banks are disposable decoded
                // residency only. The carrier root, durable directory and
                // sparse locator remain singular; segment never becomes
                // logical or canonical identity.
                for (int index = 0; index < ResidentBindingBankCount; ++index)
                {
                    var segment = new CarrierSegment(
                        _residentBankPageCapacity, index);
                    _segments.Add(segment);
                    Initialize(segment);
                }
            }
            SigmaCarrierReadBatch[] banks = CreateResidentBanks();
            if (_persistence == null)
            {
                _persistence = new SigmaCarrierPersistence(_durableStore,
                    _backendGate, banks);
                _pager = new SigmaCarrierPager(_durableStore, banks,
                    new SigmaNativeColdUploadBackend(),
                    new SigmaGraphicsResidencyUpdateBackend(_residency));
            }
            return banks[0];
        }

        internal SigmaCarrierReadBatch GetResidentBank(int segmentIndex)
        {
            RequireInitialized();
            if ((uint)segmentIndex >= (uint)_segments.Count)
                throw new ArgumentOutOfRangeException(nameof(segmentIndex));
            return CreateReadBatch(segmentIndex);
        }

        internal SigmaCarrierReadBatch SelectNativeTargetBank()
        {
            RequireInitialized();
            return Pager.SelectNativeTarget();
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
            SigmaCarrierPersistence persistence = _persistence;
            _persistence = null;
            SigmaCarrierPager pager = _pager;
            _pager = null;
            CarrierSegment[] segments = _segments.ToArray();
            _segments.Clear();
            GraphicsBuffer publicationRoot = _publicationRoot;
            _publicationRoot = null;
            SigmaCarrierResidencyResources residency = _residency;
            _residency = null;
            _durableStore = null;
            _durableOpenTask = null;
            _carrierShader = null;
            _backendGate = null;
            _initialized = false;

            void ReleaseOwnedResources()
            {
                persistence?.Dispose();
                pager?.Dispose();
                foreach (CarrierSegment segment in segments)
                    segment.Dispose();
                publicationRoot?.Dispose();
                residency?.Dispose();
            }

            var completion = new CarrierRetirementCompletion(persistence,
                pager);
            SigmaGpuCompletionStatus status = completion.Poll(
                out string error);
            if (status == SigmaGpuCompletionStatus.Complete)
                ReleaseOwnedResources();
            else if (status == SigmaGpuCompletionStatus.Faulted)
                SigmaGpuRetirement.Quarantine(ReleaseOwnedResources,
                    "Sigma N5 carrier bank",
                    error ?? "Cold carrier completion faulted.");
            else
                SigmaGpuRetirement.Retire(completion,
                    ReleaseOwnedResources, "Sigma N5 carrier bank teardown");
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
                segment.ReadoutDirtyFlags, _publicationRoot,
                _residency.Locator, _residency.SlotTable,
                _residency.LocatorCapacity, _residency.SlotCapacity,
                _runtimeState);
        }

        private SigmaCarrierReadBatch[] CreateResidentBanks()
        {
            if (_segments.Count != ResidentBindingBankCount)
                throw new InvalidOperationException(
                    "N5 resident binding-bank ownership is incomplete.");
            var banks = new SigmaCarrierReadBatch[_segments.Count];
            for (int index = 0; index < banks.Length; ++index)
                banks[index] = CreateReadBatch(index);
            return banks;
        }

        private void RequireInitialized()
        {
            if (!IsInitialized)
                throw new InvalidOperationException("Sigma carrier is not initialized.");
        }

        private sealed class CarrierRetirementCompletion :
            ISigmaResidencyCompletion
        {
            private readonly SigmaCarrierPersistence _persistence;
            private readonly SigmaCarrierPager _pager;

            internal CarrierRetirementCompletion(
                SigmaCarrierPersistence persistence, SigmaCarrierPager pager)
            {
                _persistence = persistence;
                _pager = pager;
            }

            public SigmaGpuCompletionStatus Poll(out string error)
            {
                try
                {
                    _persistence?.Poll();
                    _pager?.Poll();
                    if (_persistence?.Activity ==
                            SigmaDurableActivity.Faulted)
                    {
                        error = _persistence.Fault ??
                            "Durable carrier teardown faulted.";
                        return SigmaGpuCompletionStatus.Faulted;
                    }
                    if (_pager?.Activity == SigmaPagerActivity.Faulted)
                    {
                        error = _pager.Fault ??
                            "Pager teardown completion faulted.";
                        return SigmaGpuCompletionStatus.Faulted;
                    }
                    if ((_persistence != null && _persistence.IsBusy) ||
                        (_pager != null && _pager.Activity !=
                            SigmaPagerActivity.Idle) ||
                        SigmaNativeVulkanExecutor.HasJobInFlight ||
                        SigmaNativeVulkanColdEncode.HasJobInFlight ||
                        SigmaNativeVulkanColdUpload.HasJobInFlight ||
                        SigmaNativeVulkanReadout.HasJobInFlight)
                    {
                        error = null;
                        return SigmaGpuCompletionStatus.Pending;
                    }
                    error = null;
                    return SigmaGpuCompletionStatus.Complete;
                }
                catch (Exception exception)
                {
                    error = "Carrier retirement polling failed: " +
                        exception.Message;
                    return SigmaGpuCompletionStatus.Faulted;
                }
            }
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
