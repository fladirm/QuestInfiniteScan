using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    internal enum SigmaFrameCompletionDisposition : byte
    {
        Faulted = 0,
        NoChange = 1,
        Unresolved = 2,
        Published = 3,
    }

    /// <summary>
    /// Fixed host recorder for the direct whole-frame inverse. The CPU owns only
    /// immutable calibration uploads, complete-frame resource leases and fences.
    /// Every accepted coherent frame executes one fixed GPU dataflow and publishes
    /// one atomic Psi revision; execution partition never acquires identity.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SigmaCarrier))]
    [RequireComponent(typeof(SigmaRenderer))]
    [RequireComponent(typeof(SigmaRigBridge))]
    [DefaultExecutionOrder(0)]
    public sealed class SigmaInverseController : MonoBehaviour, IRoomScanModule
    {
        private const string DepthNormalizeResource =
            "SigmaPrism/DepthNormalize";
        private const string ConeLutResource = "SigmaPrism/ConeLut";
        private const string PoseGaugeResource = "SigmaPrism/SigmaPoseGauge";
        private const int CalibrationStride = 36;
        private const int RgbCalibrationStride = 8;
        private const int PosePriorValueCount = 15;
        private const uint N5CapacityFaultReceipt = 0x00000120u;
        private const uint N5ResidencyRequiredReceipt = 0x00008000u;
        private const uint N5ResidencyInvalidReceipt = 0x00010000u;

        private enum ColdContinuationKind : byte
        {
            None,
            Publication,
            Frontier,
            Capacity,
            Residency,
            CapacityAndResidency,
        }

        private enum ColdContinuationPhase : byte
        {
            None,
            Persisting,
            AdoptingResidentSnapshot,
            ResolvingPredictionRequests,
            PublishingAbsent,
            AwaitingLegalEviction,
            Evicting,
            Rehydrating,
            ReadyToReplay,
        }

        private enum SupportPrefetchPhase : byte
        {
            None,
            AwaitingLegalEviction,
            Evicting,
            Rehydrating,
        }

        [Header("Direct coherent-frame graph")]
        [SerializeField, Range(3, 8)] private int ingressSlotCount = 4;
        [SerializeField, Range(0.005f, 0.1f)]
        private float poseTranslationPriorMetres = 0.03f;
        [SerializeField, Range(0.25f, 5f)]
        private float poseRotationPriorDegrees = 2f;
        [SerializeField, Range(4, 32)] private int poseSampleStride = 16;
        [SerializeField] private bool profileNextCanonicalSubmission = true;

        private SigmaExactConstraintJournal _constraintJournal = new();
        private SigmaConstraintDurabilitySnapshot _pendingFrontierSnapshot;
        private readonly SigmaNativeCompletionTransfer _completionTransfer =
            new();
        private readonly SigmaPackedQ48[] _calibrationUpload =
            new SigmaPackedQ48[CalibrationStride * 2];
        private readonly SigmaPackedQ48[] _rgbCalibrationUpload =
            new SigmaPackedQ48[RgbCalibrationStride * 2];
        private readonly SigmaPackedQ48[] _posePriorUpload =
            new SigmaPackedQ48[PosePriorValueCount];
        private readonly LatencyTracker _frameLatency = new();
        private readonly FrameTiming[] _frameTimings = new FrameTiming[1];
        private SigmaRuntimeTelemetrySnapshot _runtimeTelemetry =
            SigmaRuntimeTelemetrySnapshot.Awaiting;

        private SigmaCarrier _carrier;
        private SigmaRenderer _renderer;
        private SigmaRigBridge _rigBridge;
        private SigmaExactBackendGate _backendGate;
        private SigmaCarrierReadBatch _pool;
        private SigmaCarrierReadBatch _secondaryPool;
        private SigmaNativeFrameGraph _graph;
        private RigCalibration _calibration;
        private RigConeLutSet _coneLuts;

        private ComputeShader _normalizeShader;
        private ComputeShader _coneLutShader;
        private ComputeShader _poseGaugeShader;
        private int _normalizeKernel;
        private int _poseBuildKernel;
        private int _poseReduceKernel;
        private int _poseCalibrationKernel;

        private IngressSlot[] _ingressSlots;

        private SigmaGpuCompletionTicket _lastCompletion;
        private bool _hasLastCompletion;
        private bool _running;
        private bool _initialized;
        private bool _disposed;
        private bool _completionFaulted;
        private uint _nextRevision = 1u;
        private Pose _previousTrackingPose;
        private long _previousTrackingTimestampNs;
        private long _lastSourceSequence;
        private bool _hasPreviousTrackingPose;
        private IngressSlot _durabilitySlot;
        private ColdContinuationKind _coldKind;
        private ColdContinuationPhase _coldPhase;
        private readonly List<SigmaResidencyKey> _coldLoadKeys = new();
        private readonly List<SigmaResidencyKey> _coldAbsentKeys = new();
        private readonly HashSet<SigmaResidencyKey> _coldProtectedKeys = new();
        private int _coldEvictionsRemaining;
        private int _coldFreshPairsRequired;
        private bool _coldTargetEvictionOnly;
        private uint _coldTargetLogicalExtent;
        private int _coldLoadOffset;
        private int _coldLoadBatchCount;
        private readonly List<SigmaResidencyKey> _startupRestoreKeys = new();
        private int _startupRestoreOffset;
        private int _startupRestoreBatchCount;
        private bool _startupRestorePending;
        private SupportPrefetchPhase _supportPrefetchPhase;
        private readonly List<SigmaResidencyKey> _supportPrefetchLoads = new();
        private readonly HashSet<SigmaResidencyKey> _supportPrefetchProtected =
            new();
        private int _supportPrefetchEvictions;
        private int _supportPrefetchLoadOffset;
        private int _supportPrefetchLoadBatchCount;
        private ulong _supportBackpressureRevision;
        private int _supportBackpressureCandidates = -1;
        private double _nextSupportBackpressureLogTime;

        public string ModuleName => "Sigma direct whole-frame RGB-D inverse";
        public bool IsInitialized => _initialized && !_disposed;
        public long SubmittedFrames { get; private set; }
        public long CommittedFrames { get; private set; }
        public long FailedFrames { get; private set; }
        public long PeakCompletionAgeFrames { get; private set; }
        public int CompletionTickets => CountCompletionTickets() +
            SigmaGpuRetirement.PendingCount;
        public SigmaRuntimeTelemetrySnapshot RuntimeTelemetry =>
            _runtimeTelemetry;

        // Donor-shaped observation ownership: the fixed 5 Hz scheduler may
        // transfer one new coherent frame only after the prior native close has
        // reached its terminal GPU fence. No historical scan queue is admitted.
        internal bool CanAcceptScheduledObservation =>
            ScheduledObservationReady(_initialized, _disposed, _running,
                _completionFaulted, 0,
                HasInFlightIngress()) &&
            !_startupRestorePending &&
            _supportPrefetchPhase == SupportPrefetchPhase.None &&
            !SigmaNativeVulkanExecutor.HasJobInFlight &&
            (_carrier == null ||
                (!_carrier.Persistence.IsBusy &&
                 _carrier.Persistence.Activity !=
                    SigmaDurableActivity.Complete &&
                 _carrier.Pager.Activity == SigmaPagerActivity.Idle));

        internal static bool ScheduledObservationReady(bool initialized,
            bool disposed, bool running, bool completionFaulted,
            int pendingCount, bool hasInFlight) =>
            initialized && !disposed && running && !completionFaulted &&
            pendingCount == 0 && !hasInFlight;

        public void OnModuleInitialize(RoomScanner scanner)
        {
            if (_initialized)
                return;
            if (scanner == null) throw new ArgumentNullException(nameof(scanner));
            _carrier = scanner.Carrier ?? GetComponent<SigmaCarrier>();
            _renderer = scanner.SigmaRenderer ?? GetComponent<SigmaRenderer>();
            _rigBridge = scanner.RigBridge ?? GetComponent<SigmaRigBridge>();
            _backendGate = scanner.ExactBackendGate ??
                throw new InvalidOperationException(
                    "Sigma inverse requires the exact backend gate.");
            SigmaNativeVulkanExecutor.RequireAvailable();
            Logger.Info("Sigma N4.2R capabilities: supportsAsyncCompute=" +
                        $"{SystemInfo.supportsAsyncCompute}, " +
                        $"supportsGraphicsFence={SystemInfo.supportsGraphicsFence}, " +
                        $"supportsAsyncGPUReadback=" +
                        $"{SystemInfo.supportsAsyncGPUReadback}, " +
                        "selected native queue=plugin-owned Vulkan " +
                        "same-family background.");

            _normalizeShader = Resources.Load<ComputeShader>(
                DepthNormalizeResource);
            _coneLutShader = Resources.Load<ComputeShader>(ConeLutResource);
            _poseGaugeShader = Resources.Load<ComputeShader>(PoseGaugeResource);
            if (_carrier == null || _renderer == null ||
                _rigBridge == null || _normalizeShader == null ||
                _coneLutShader == null || _poseGaugeShader == null)
                throw new InvalidOperationException(
                    "Sigma direct inverse resources are incomplete.");

            FindKernels();
            _pool = _carrier.AcquireGpuManagedPool();
            _secondaryPool = _carrier.GetResidentBank(1);
            _constraintJournal = LoadDurableConstraintJournal();
            BeginStartupRestore();
            CreateDirectGraph();
            _initialized = true;
            Logger.Info("Sigma direct frame host ready; the first coherent " +
                        "prediction fixes the owned-frame resolution.");
        }

        public void OnScanStarted()
        {
            if (_completionFaulted)
            {
                Logger.Error("Sigma inverse cannot resume after an unproven GPU " +
                             "completion fault; restart the application.");
                return;
            }
            _running = true;
            _hasPreviousTrackingPose = false;
            _previousTrackingTimestampNs = 0L;
            _lastSourceSequence = 0L;
            _frameLatency.Reset();
            if (profileNextCanonicalSubmission)
                Logger.Info("Sigma native-queue timing is armed for every " +
                    "submitted 16-dispatch close.");
        }

        public void OnScanStopped()
        {
            _running = false;
            // Every already-submitted complete frame owns its source leases until
            // its fence closes; stopping cannot expose a partial revision.
        }

        internal async Task ClearDurableWorldAsync()
        {
            if (!_initialized || _disposed)
                throw new InvalidOperationException(
                    "Sigma durable clear requires an initialized controller.");
            if (_running)
                throw new InvalidOperationException(
                    "Sensor admission must stop before durable clear.");
            if (_completionFaulted)
                throw new InvalidOperationException(
                    "Durable clear cannot recycle resources after an unproven " +
                    "GPU completion fault.");

            while (HasInFlightIngress() || _durabilitySlot != null ||
                _supportPrefetchPhase != SupportPrefetchPhase.None ||
                _startupRestorePending || _carrier.Persistence.IsBusy ||
                _carrier.Persistence.Activity == SigmaDurableActivity.Complete ||
                _carrier.Pager.Activity != SigmaPagerActivity.Idle ||
                SigmaNativeVulkanExecutor.HasJobInFlight ||
                SigmaNativeVulkanColdEncode.HasJobInFlight ||
                SigmaNativeVulkanColdUpload.HasJobInFlight)
                await Task.Yield();

            ulong revision = Math.Max(_pool.DurableRevision + 1UL,
                (ulong)_nextRevision);
            if (revision >= uint.MaxValue)
                throw new OverflowException(
                    "The empty durable root exceeds its uint GPU ABI.");
            SigmaDurableCommitResult result = await
                _carrier.SelectEmptyDurableAsync(revision);
            _nextRevision = checked((uint)revision + 1u);
            ResetColdContinuation();
            _constraintJournal.Clear();
            _pendingFrontierSnapshot = default;
            _renderer.ResetDisposableReadoutAfterClear();
            Logger.Info("Sigma N5 explicit clear selected empty durable HEAD: " +
                "revision=" + result.RootObject.Revision + ".");
        }

        private void LateUpdate()
        {
            if (!_initialized || _disposed)
                return;

            PollIngress();
            DrainCompletionTransfers();
            PollSupportPrefetch();
            PollStartupRestore();
            PollDurabilityAndContinuation();
            if (!_running && !HasInFlightIngress())
                _completionTransfer.FlushOpenAfterGpuIdle();
            FrameTimingManager.CaptureFrameTimings();
            SigmaGpuKernelTelemetry.CaptureAndLogFrame();
        }

        /// <summary>
        /// Transfers one newest coherent capture directly into the single
        /// queue-1 scanner transaction. Capture itself is not scanner work and
        /// missed 5 Hz admissions never queue or catch up.
        /// </summary>
        internal bool TryScheduleLatestObservation()
        {
            if (!CanAcceptScheduledObservation ||
                !_rigBridge.TryAcquireLatest(out StereoRigFrameLease source))
                return false;
            try
            {
                if (source.Sequence == _lastSourceSequence)
                    return false;
                if (!TryPreparePredictionSupport(source,
                        RoomSpaceRoot.WorldToRoom))
                    return false;
                EnsureDirectGraph(source);
                if (!TryGetFreeIngressSlot(out IngressSlot slot) ||
                    !SubmitIngress(slot, source))
                    return false;
                _lastSourceSequence = source.Sequence;
                _rigBridge.AcknowledgeConsumed(source.Sequence);
                return true;
            }
            catch (Exception exception)
            {
                LatchCompletionFault("Sigma direct-frame submission failed: " +
                    exception.Message);
                return false;
            }
            finally
            {
                source.Dispose();
            }
        }

        private bool TryPreparePredictionSupport(StereoRigFrameLease source,
            Matrix4x4 worldToRoom)
        {
            double selectionBegin = Time.realtimeSinceStartupAsDouble;
            SigmaDurableStore store = _carrier.Persistence.Store;
            if (!store.HasHead)
                return true;

            SigmaQ48Bounds3 queryBounds = default;
            bool bounded = true;
            IReadOnlyList<SigmaResidencyKey> selected =
                Array.Empty<SigmaResidencyKey>();
            if (store.QuerySupportPageCount != 0)
            {
                bounded = SigmaQuerySupportPlan.TryBuildSensorQueryBounds(
                    source, worldToRoom, poseTranslationPriorMetres,
                    poseRotationPriorDegrees, out queryBounds);
                if (!bounded)
                {
                    // Invalid calibration/bound construction never omits a
                    // durable page. The finite durable directory becomes the
                    // candidate set and normal working-set backpressure applies.
                    queryBounds = new SigmaQ48Bounds3(long.MinValue,
                        long.MinValue, long.MinValue, long.MaxValue,
                        long.MaxValue, long.MaxValue);
                }
                selected = store.SelectPredictionSupport(queryBounds);
            }

            var candidates = new HashSet<SigmaResidencyKey>(selected);
            using SigmaDurableRootLease root = store.PinHead();
            for (int index = 0; index < selected.Count; ++index)
            {
                SigmaResidencyKey key = selected[index];
                if (!store.TryGetPageRecord(root, key.Coordinate,
                        out SigmaDurablePageRecord record) ||
                    record.Revision != key.RootContext ||
                    record.PageGeneration != key.PageGeneration)
                    throw new InvalidDataException(
                        "Query-support index selected a stale durable key.");
                // The readout normal consumes +X/+Y/+XY halo samples. These
                // neighbours are presentation dependencies only; their physical
                // slots and the index traversal never enter canonical identity.
                AddDurableNeighbour(store, root, key.Coordinate, 1L, 0L,
                    candidates);
                AddDurableNeighbour(store, root, key.Coordinate, 0L, 1L,
                    candidates);
                AddDurableNeighbour(store, root, key.Coordinate, 1L, 1L,
                    candidates);
            }
            int readoutCandidateCount = candidates.Count;
            bool hasAppendTail = AddRequiredAppendTail(
                _pool.DurableLogicalExtent, store, root, candidates);

            SigmaCarrierPager pager = _carrier.Pager;
            _supportPrefetchLoads.Clear();
            _supportPrefetchProtected.Clear();
            foreach (SigmaResidencyKey key in candidates)
            {
                SigmaResidencyState state = pager.StateOf(key);
                if (state == SigmaResidencyState.HotClean ||
                    state == SigmaResidencyState.HotDirty)
                    _supportPrefetchProtected.Add(key);
                else if (state == SigmaResidencyState.ColdDurable)
                    _supportPrefetchLoads.Add(key);
                else
                    throw new InvalidOperationException(
                        "A selected durable support key has illegal idle " +
                        "residency state " + state + ".");
            }
            if (_supportPrefetchLoads.Count == 0)
            {
                ResetSupportBackpressureLog();
                return true;
            }

            _supportPrefetchLoads.Sort(CompareResidencyKeys);
            try
            {
                _supportPrefetchEvictions = CapacityEvictionsRequired(
                    pager.PairCapacity, pager.FreePairCount,
                    _supportPrefetchProtected.Count,
                    _supportPrefetchLoads.Count, 0);
            }
            catch (InvalidOperationException)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                ulong revision = root.Root.Revision;
                if (_supportBackpressureRevision != revision ||
                    _supportBackpressureCandidates != candidates.Count ||
                    now >= _nextSupportBackpressureLogTime)
                {
                    Logger.Warning("Sigma N5 support working set " +
                        "backpressured: candidates=" + candidates.Count +
                        " selected=" + selected.Count + " halo=" +
                        (readoutCandidateCount - selected.Count) +
                        " appendTail=" + (hasAppendTail ? 1 : 0) + " hot=" +
                        _supportPrefetchProtected.Count + " cold=" +
                        _supportPrefetchLoads.Count + " unbounded=" +
                        store.QuerySupportUnboundedCount + " visitedNodes=" +
                        store.LastQuerySupportVisitedNodes +
                        " bounded=" + bounded + " residentPairs=" +
                        pager.PairCapacity + " targetPairs=" +
                        _pool.PairCount + " selectMs=" +
                        ((now - selectionBegin) * 1000.0).ToString("F3") +
                        " headIndexLookups=" +
                        store.HeadRecordIndexLookups + ".");
                    _supportBackpressureRevision = revision;
                    _supportBackpressureCandidates = candidates.Count;
                    _nextSupportBackpressureLogTime = now + 5.0;
                }
                _supportPrefetchLoads.Clear();
                _supportPrefetchProtected.Clear();
                return false;
            }
            ResetSupportBackpressureLog();
            _supportPrefetchLoadOffset = 0;
            _supportPrefetchLoadBatchCount = 0;
            Logger.Info("Sigma N5 query-support prefetch: candidates=" +
                candidates.Count + " cold=" + _supportPrefetchLoads.Count +
                " appendTail=" + (hasAppendTail ? 1 : 0) +
                " unbounded=" + store.QuerySupportUnboundedCount +
                " visitedNodes=" + store.LastQuerySupportVisitedNodes +
                " evictions=" + _supportPrefetchEvictions + ".");
            if (_supportPrefetchEvictions != 0)
                _supportPrefetchPhase =
                    SupportPrefetchPhase.AwaitingLegalEviction;
            else
                BeginNextSupportPrefetchLoad();
            return false;
        }

        internal static bool AddRequiredAppendTail(uint durableExtent,
            SigmaDurableStore store, SigmaDurableRootLease root,
            ISet<SigmaResidencyKey> destination)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (durableExtent == 0u ||
                durableExtent % SigmaCarrier.SamplesPerPage == 0u)
                return false;

            var coordinate = new SigmaCarrierPageCoordinate(
                durableExtent / SigmaCarrier.SamplesPerPage, 0L);
            if (!store.TryGetPageRecord(root, coordinate,
                    out SigmaDurablePageRecord record))
                throw new InvalidDataException(
                    "The durable append tail is absent from selected HEAD.");
            destination.Add(new SigmaResidencyKey(coordinate,
                record.Revision, record.PageGeneration));
            return true;
        }

        private void ResetSupportBackpressureLog()
        {
            _supportBackpressureRevision = 0UL;
            _supportBackpressureCandidates = -1;
            _nextSupportBackpressureLogTime = 0.0;
        }

        private static void AddDurableNeighbour(SigmaDurableStore store,
            SigmaDurableRootLease root, SigmaCarrierPageCoordinate coordinate,
            long offsetX, long offsetY, ISet<SigmaResidencyKey> destination)
        {
            long x, y;
            try
            {
                x = checked(coordinate.X + offsetX);
                y = checked(coordinate.Y + offsetY);
            }
            catch (OverflowException)
            {
                return;
            }
            var neighbour = new SigmaCarrierPageCoordinate(x, y);
            if (store.TryGetPageRecord(root, neighbour,
                    out SigmaDurablePageRecord record))
                destination.Add(new SigmaResidencyKey(neighbour,
                    record.Revision, record.PageGeneration));
        }

        private void PollSupportPrefetch()
        {
            if (_supportPrefetchPhase == SupportPrefetchPhase.None)
                return;
            SigmaCarrierPager pager = _carrier.Pager;
            pager.Poll();
            if (pager.Activity == SigmaPagerActivity.Faulted)
            {
                LatchCompletionFault("Sigma query-support prefetch failed " +
                    "closed: " + pager.Fault);
                return;
            }
            if (pager.Activity != SigmaPagerActivity.Idle)
                return;

            if (_supportPrefetchPhase ==
                SupportPrefetchPhase.AwaitingLegalEviction)
            {
                if (_supportPrefetchEvictions == 0)
                {
                    BeginNextSupportPrefetchLoad();
                    return;
                }
                if (pager.TryBeginAnyEviction(_supportPrefetchProtected,
                        out SigmaResidencyKey evicted))
                {
                    _supportPrefetchPhase = SupportPrefetchPhase.Evicting;
                    Logger.Info("Sigma N5 COLD_EVICT support-prefetch: logical=" +
                        evicted.Coordinate + " generation=" +
                        evicted.PageGeneration + ".");
                }
                return;
            }

            if (_supportPrefetchPhase == SupportPrefetchPhase.Evicting)
            {
                if (!pager.TryTakeCompletedEviction(out _,
                        out int segmentIndex, out int pairIndex))
                {
                    LatchCompletionFault(
                        "Support-prefetch eviction lost its retirement receipt.");
                    return;
                }
                _carrier.Persistence.ForgetResidentPair(segmentIndex,
                    pairIndex);
                _supportPrefetchEvictions--;
                _supportPrefetchPhase =
                    SupportPrefetchPhase.AwaitingLegalEviction;
                return;
            }

            if (_supportPrefetchPhase == SupportPrefetchPhase.Rehydrating)
            {
                int completed = 0;
                while (pager.TryTakeCompletedLoad(
                    out SigmaStagedDurablePage loaded))
                {
                    _carrier.Persistence.RememberResidentPage(loaded);
                    ++completed;
                }
                if (completed != _supportPrefetchLoadBatchCount)
                {
                    LatchCompletionFault(
                        "Support-prefetch rehydrate lost a resident receipt.");
                    return;
                }
                _supportPrefetchLoadOffset += completed;
                _supportPrefetchLoadBatchCount = 0;
                BeginNextSupportPrefetchLoad();
            }
        }

        private void BeginNextSupportPrefetchLoad()
        {
            if (_supportPrefetchLoadOffset >= _supportPrefetchLoads.Count)
            {
                Logger.Info("Sigma N5 query-support prefetch complete: pages=" +
                    _supportPrefetchLoads.Count + ".");
                _supportPrefetchPhase = SupportPrefetchPhase.None;
                _supportPrefetchLoads.Clear();
                _supportPrefetchProtected.Clear();
                _supportPrefetchEvictions = 0;
                _supportPrefetchLoadOffset = 0;
                _supportPrefetchLoadBatchCount = 0;
                return;
            }
            int bankFree = _carrier.Pager.MaximumFreePairCountInBank;
            if (bankFree == 0)
                throw new InvalidOperationException(
                    "No resident binding bank can accept support pages.");
            _supportPrefetchLoadBatchCount = Math.Min(bankFree, Math.Min(
                SigmaNativeVulkanColdUpload.MaximumPages,
                _supportPrefetchLoads.Count - _supportPrefetchLoadOffset));
            var batch = _supportPrefetchLoads.GetRange(
                _supportPrefetchLoadOffset,
                _supportPrefetchLoadBatchCount);
            if (!_carrier.Pager.BeginRehydrate(batch))
                throw new InvalidOperationException(
                    "Query-support rehydrate batch did not start.");
            _supportPrefetchPhase = SupportPrefetchPhase.Rehydrating;
            Logger.Info("Sigma N5 COLD_REHYDRATE query-support: pages=" +
                _supportPrefetchLoadBatchCount + ".");
        }

        private SigmaRuntimeTimingTelemetry CaptureTimingTelemetry()
        {
            uint timingCount = FrameTimingManager.GetLatestTimings(1,
                _frameTimings);
            bool hasFrameTiming = timingCount != 0u &&
                _frameTimings[0].cpuFrameTime > 0.0 &&
                _frameTimings[0].gpuFrameTime > 0.0;
            return new SigmaRuntimeTimingTelemetry(
                _frameLatency.Snapshot,
                hasFrameTiming ? _frameTimings[0].cpuFrameTime : 0.0,
                hasFrameTiming ? _frameTimings[0].gpuFrameTime : 0.0,
                hasFrameTiming);
        }

        private void EnsureDirectGraph(StereoRigFrameLease source)
        {
            SigmaNativeQuestAperture.RequirePhysicalDepth(
                source.DepthResolution);
            if (_graph != null)
            {
                if (_graph.Resolution !=
                        SigmaNativeQuestAperture.LogicalResolution ||
                    _graph.SensorOffset !=
                        SigmaNativeQuestAperture.SensorOffset)
                    throw new InvalidOperationException(
                        "The fixed native aperture changed inside one " +
                        "direct-frame session.");
                return;
            }
            CreateDirectGraph();
        }

        private void CreateDirectGraph()
        {
            if (_graph != null)
                return;
            _graph = new SigmaNativeFrameGraph(
                SigmaNativeQuestAperture.LogicalResolution,
                SigmaNativeQuestAperture.SensorOffset, _backendGate,
                ingressSlotCount);
            CreatePersistentResources(_graph.FrameCapacity);
            Logger.Info($"Sigma direct graph ready: physicalDepth=" +
                        $"{SigmaNativeQuestAperture.PhysicalDepthResolution.x}x" +
                        $"{SigmaNativeQuestAperture.PhysicalDepthResolution.y}, " +
                        $"logicalAperture={_graph.Resolution.x}x" +
                        $"{_graph.Resolution.y}, sensorOffset=" +
                        $"{_graph.SensorOffset.x},{_graph.SensorOffset.y}, " +
                        $"ownedFrames={_graph.FrameCapacity}, " +
                        $"hotDispatches={SigmaNativeFrameGraph.HotDispatchCount}, " +
                        $"memory={_graph.OwnedBytes / (1024L * 1024L)}MiB.");
        }

        private bool SubmitIngress(IngressSlot slot, StereoRigFrameLease source)
        {
            if (source == null || !source.IsValid)
                throw new InvalidOperationException(
                    "Inverse source lease is invalid.");
            Matrix4x4 worldToRoom = RoomSpaceRoot.WorldToRoom;
            SigmaPredictionAcquireResult predictionAcquisition =
                _renderer.TryAcquireScannerPrediction(source, worldToRoom,
                    out SigmaPredictionFrameLease ownedPrediction);
            if (predictionAcquisition == SigmaPredictionAcquireResult.Busy)
                return false;
            if (predictionAcquisition == SigmaPredictionAcquireResult.Faulted)
                throw new InvalidOperationException(
                    "Scanner prediction ring faulted.");
            SigmaPredictionAcquireResult correctedAcquisition =
                _renderer.TryAcquirePoseGaugePrediction(source, worldToRoom,
                    out SigmaPredictionFrameLease correctedPrediction);
            if (correctedAcquisition == SigmaPredictionAcquireResult.Busy)
            {
                ownedPrediction.Dispose();
                return false;
            }
            if (correctedAcquisition == SigmaPredictionAcquireResult.Faulted)
            {
                ownedPrediction.Dispose();
                throw new InvalidOperationException(
                    "Same-frame corrected prediction ring faulted.");
            }

            ConeLutLease luts = null;
            CommandBuffer scannerCommand = null;
            SigmaNativeVulkanExecutor.SigmaNativeVulkanJob nativeJob = null;
            SigmaNativeFrameLease ownedFrame = null;
            uint revision = 0u;
            uint readoutRevision = 0u;
            bool scannerSubmissionAttempted = false;
            bool scannerSubmitted = false;
            bool slotOwnsResources = false;
            SigmaGpuCompletionTicket nativeDone = default;
            List<IDisposable> residencyLeases = null;
            SigmaNativeCompletionTransfer.Reservation completion = default;
            try
            {
                slot.EnsureFrameResources(source.DepthResolution);
                revision = NextRevision();
                uint leftKey = IndependenceKey(source.DepthLeft,
                    source.CalibrationEpoch, worldToRoom);
                uint rightKey = IndependenceKey(source.DepthRight,
                    source.CalibrationEpoch, worldToRoom);
                uint rgbLeftKey = IndependenceKey(source.RgbLeft,
                    source.CalibrationEpoch, worldToRoom);
                uint rgbRightKey = IndependenceKey(source.RgbRight,
                    source.CalibrationEpoch, worldToRoom);
                if (!_graph.TryAcquire(out ownedFrame))
                    return false;
                completion = _completionTransfer.Reserve(revision);

                scannerCommand = CommandBufferPool.Get(
                    "Sigma-PRISM-16 Scanner Prepass");
                EnsureCalibration(source, scannerCommand);
                luts = _coneLuts.Acquire();
                UploadExactCalibration(scannerCommand, slot, source,
                    worldToRoom);
                UploadPosePrior(scannerCommand, slot, source, worldToRoom);
                if (!_renderer.RecordScannerPrediction(scannerCommand, source,
                        ownedPrediction, out readoutRevision))
                    return false;
                RecordNormalize(scannerCommand, slot, source, luts);
                RecordPoseGauge(scannerCommand, slot, source, ownedPrediction,
                    revision, luts);
                RecordCorrectedCalibration(scannerCommand, slot, source,
                    worldToRoom);
                _renderer.RecordPoseGaugePrediction(scannerCommand, source,
                    slot.PoseResult, worldToRoom, correctedPrediction);

                SigmaCarrierReadBatch target =
                    _carrier.SelectNativeTargetBank();
                Logger.Info("Sigma N5 native target: revision=" + revision +
                    " segment=" + target.SegmentIndex + " freePairs=" +
                    _carrier.Pager.FreePairCountInSegment(
                        target.SegmentIndex) + " durableExtent=" +
                    target.DurableLogicalExtent + " partialTail=" +
                    (target.DurableLogicalExtent %
                        SigmaCarrier.SamplesPerPage != 0u) + ".");
                var input = new SigmaNativeFrameInput(correctedPrediction,
                    slot.MetricDepth, slot.DepthFlags,
                    slot.CorrectedDepthCalibration,
                    slot.CorrectedRgbCalibration, slot.PoseResult, luts,
                    leftKey, rightKey, rgbLeftKey, rgbRightKey,
                    _pool, _secondaryPool, target);
                nativeJob = _graph.CreateNativeCloseJob(ownedFrame, revision,
                    source.CalibrationEpoch, input, completion.Buffer,
                    completion.RecordIndex);
                nativeJob.RecordPrepare(scannerCommand);
                nativeJob.RecordSubmit(scannerCommand);
                nativeDone = new SigmaGpuCompletionTicket(nativeJob);
                residencyLeases = PinResidentBank(nativeDone);

                scannerSubmissionAttempted = true;
                Graphics.ExecuteCommandBuffer(scannerCommand);
                scannerSubmitted = true;
                _renderer.CommitScannerTransaction(ownedPrediction,
                    correctedPrediction, nativeDone, readoutRevision);
                TrackLast(nativeDone);
                slot.Begin(ownedPrediction, correctedPrediction, luts,
                    ownedFrame, completion, nativeJob, nativeDone, revision,
                    source.CalibrationEpoch, leftKey, rightKey, rgbLeftKey,
                    rgbRightKey, residencyLeases,
                    Time.realtimeSinceStartupAsDouble);
                slotOwnsResources = true;
                ownedPrediction = null;
                correctedPrediction = null;
                luts = null;
                ownedFrame = null;
                nativeJob = null;
                residencyLeases = null;
                SubmittedFrames++;
                return true;
            }
            finally
            {
                if (completion.IsValid && !scannerSubmissionAttempted)
                    _completionTransfer.Cancel(completion);

                if (scannerSubmitted && !slotOwnsResources)
                {
                    SigmaPredictionFrameLease retirePrediction =
                        ownedPrediction;
                    SigmaPredictionFrameLease retireCorrected =
                        correctedPrediction;
                    ConeLutLease retireLuts = luts;
                    SigmaNativeFrameLease retireFrame = ownedFrame;
                    SigmaNativeVulkanExecutor.SigmaNativeVulkanJob retireJob =
                        nativeJob;
                    SigmaNativeCompletionTransfer.Reservation retireCompletion =
                        completion;
                    List<IDisposable> retireResidencyLeases =
                        residencyLeases;
                    SigmaGpuRetirement.Retire(nativeDone, () =>
                    {
                        try
                        {
                            if (retireJob != null &&
                                retireJob.TryReadCompletion(
                                    out SigmaFrameUInt2Gpu[] words))
                                _completionTransfer.CompleteNative(
                                    retireCompletion, words);
                            else
                                _completionTransfer.CompleteNativeFailure(
                                    retireCompletion,
                                    "Orphaned native completion bytes were " +
                                    "unavailable after its terminal fence.");
                        }
                        finally
                        {
                            retirePrediction?.Dispose();
                            retireCorrected?.Dispose();
                            retireLuts?.Dispose();
                            retireFrame?.Dispose();
                            retireJob?.Dispose();
                            ReleaseResidencyLeases(retireResidencyLeases);
                        }
                    }, "Sigma orphaned background native submission");
                    ownedPrediction = null;
                    correctedPrediction = null;
                    luts = null;
                    ownedFrame = null;
                    nativeJob = null;
                    residencyLeases = null;
                }
                else if (scannerSubmissionAttempted && !scannerSubmitted)
                {
                    SigmaPredictionFrameLease quarantinePrediction =
                        ownedPrediction;
                    SigmaPredictionFrameLease quarantineCorrected =
                        correctedPrediction;
                    ConeLutLease quarantineLuts = luts;
                    SigmaNativeFrameLease quarantineFrame = ownedFrame;
                    SigmaNativeVulkanExecutor.SigmaNativeVulkanJob
                        quarantineJob = nativeJob;
                    List<IDisposable> quarantineResidencyLeases =
                        residencyLeases;
                    SigmaGpuRetirement.Quarantine(() =>
                    {
                        quarantinePrediction?.Dispose();
                        quarantineCorrected?.Dispose();
                        quarantineLuts?.Dispose();
                        quarantineFrame?.Dispose();
                        quarantineJob?.Dispose();
                        ReleaseResidencyLeases(quarantineResidencyLeases);
                    }, "Sigma uncertain native executor submission",
                    "Graphics.ExecuteCommandBuffer threw after submission " +
                    "was attempted; GPU ownership is unproven.");
                    ownedPrediction = null;
                    correctedPrediction = null;
                    luts = null;
                    ownedFrame = null;
                    nativeJob = null;
                    residencyLeases = null;
                }
                else if (!scannerSubmissionAttempted)
                {
                    nativeJob?.CancelBeforeExecution();
                    nativeJob = null;
                }

                ownedFrame?.Dispose();
                ownedPrediction?.Dispose();
                correctedPrediction?.Dispose();
                luts?.Dispose();
                nativeJob?.Dispose();
                ReleaseResidencyLeases(residencyLeases);
                if (scannerCommand != null)
                    CommandBufferPool.Release(scannerCommand);
            }
        }

        private List<IDisposable> PinResidentBank(
            SigmaGpuCompletionTicket terminal)
        {
            var keys = new List<SigmaResidencyKey>();
            _carrier.Pager.CollectResidentKeys(keys);
            var leases = new List<IDisposable>(keys.Count);
            try
            {
                foreach (SigmaResidencyKey key in keys)
                {
                    leases.Add(_carrier.Pager.AcquireLease(key,
                        SigmaResidencyLeaseKind.Publication));
                    _carrier.Pager.SetLastReader(key, terminal);
                    _carrier.Pager.SetLastWriter(key, terminal);
                }
                return leases;
            }
            catch
            {
                ReleaseResidencyLeases(leases);
                throw;
            }
        }

        private static void ReleaseResidencyLeases(
            List<IDisposable> leases)
        {
            if (leases == null)
                return;
            for (int index = leases.Count - 1; index >= 0; --index)
                leases[index]?.Dispose();
            leases.Clear();
        }

        private void PollIngress()
        {
            if (_ingressSlots == null)
                return;
            for (int index = 0; index < _ingressSlots.Length; ++index)
            {
                IngressSlot slot = _ingressSlots[index];
                if (!slot.InFlight || slot.AwaitingDisposition)
                    continue;
                SigmaGpuCompletionStatus status = slot.Poll(out string error);
                if (status == SigmaGpuCompletionStatus.Pending)
                {
                    slot.AdvanceAge();
                    PeakCompletionAgeFrames = Math.Max(
                        PeakCompletionAgeFrames, slot.AgeFrames);
                    continue;
                }
                SigmaGpuKernelTelemetry.CompleteProfiledSubmission(
                    slot.Revision);
                if (status == SigmaGpuCompletionStatus.Faulted)
                {
                    LatchCompletionFault($"Sigma ingress slot {index} failed " +
                        $"closed: {error}");
                    continue;
                }
                try
                {
                    if (!slot.TryReadCompletion(
                            out SigmaFrameUInt2Gpu[] completionWords))
                        throw new InvalidOperationException(
                            "Native completion staging was unavailable after " +
                            "its terminal fence.");
                    _completionTransfer.CompleteNative(
                        slot.CompletionReservation, completionWords);
                }
                catch (Exception exception)
                {
                    slot.Complete();
                    LatchCompletionFault("Sigma post-native completion transfer " +
                        $"failed: {exception.Message}");
                    continue;
                }
                _frameLatency.Add(slot.ElapsedMilliseconds(
                    Time.realtimeSinceStartupAsDouble));
                slot.MarkNativeComplete();
            }
        }

        private void DrainCompletionTransfers()
        {
            while (_completionTransfer.TryDequeue(
                out SigmaNativeCompletionRecord completion,
                out string transferError))
            {
                if (transferError != null)
                {
                    LatchCompletionFault(transferError);
                    return;
                }
                IngressSlot slot = FindAwaitingDispositionSlot(
                    completion.ExpectedRevision);
                if (slot == null)
                {
                    LatchCompletionFault("Terminal native receipt has no " +
                        "retained ingress owner for revision " +
                        completion.ExpectedRevision + ".");
                    return;
                }
                if (slot.PredictionPageRequestOverflow ||
                    (completion.Frame.Disposition.Y &
                        N5ResidencyInvalidReceipt) != 0u)
                {
                    LatchCompletionFault("Sigma N5 prediction residency " +
                        "receipt was invalid or overflowed its bounded exact " +
                        "request table.");
                    return;
                }
                if (TryClassifyColdContinuationReceipt(completion,
                        out ColdContinuationKind coldKind))
                {
                    if (_durabilitySlot != null)
                    {
                        LatchCompletionFault("The bounded durable mailbox " +
                            "already owns an admitted transaction.");
                        return;
                    }
                    _durabilitySlot = slot;
                    _coldKind = coldKind;
                    try
                    {
                        bool needsCapacity = coldKind ==
                                ColdContinuationKind.Capacity ||
                            coldKind == ColdContinuationKind
                                .CapacityAndResidency;
                        _coldTargetLogicalExtent = needsCapacity
                            ? completion.Frame.Publication.Z
                            : _pool.DurableLogicalExtent;
                        _coldFreshPairsRequired = needsCapacity
                            ? CapacityFreshPairCount(
                                _pool.DurableLogicalExtent,
                                _coldTargetLogicalExtent)
                            : 0;
                        if (needsCapacity)
                        {
                            // The failed observation has not entered the
                            // canonical root. Persist only the exact preceding
                            // published root; the retained slot continues to own
                            // this observation.
                            byte[] frontier = DurableFrontierBytes();
                            if (TryUseAlreadyDurableCapacityRoot(
                                    completion.PublishedRoot, frontier))
                            {
                                _coldPhase = ColdContinuationPhase
                                    .ResolvingPredictionRequests;
                                Logger.Info("Sigma N5 capacity root/frontier " +
                                    "already durable; skipped empty encode and " +
                                    "HEAD transaction for root=" +
                                    completion.PublishedRoot + ".");
                            }
                            else
                            {
                                _carrier.Persistence.Begin(
                                    completion.PublishedRoot, null, frontier);
                                _coldPhase = ColdContinuationPhase.Persisting;
                            }
                        }
                        else
                        {
                            if (_pool.DurableRevision !=
                                completion.PublishedRoot)
                                throw new InvalidOperationException(
                                    "A cold prediction request does not match " +
                                    "the selected durable HEAD revision.");
                            _coldPhase = ColdContinuationPhase
                                .ResolvingPredictionRequests;
                        }
                        Logger.Info("Sigma N5 COLD_CONTINUATION: kind=" +
                            coldKind + " receipt=0x" +
                            completion.Frame.Disposition.Y.ToString("x") +
                            " retainedRevision=" + completion.ExpectedRevision +
                            " root=" + completion.PublishedRoot + ".");
                    }
                    catch (Exception exception)
                    {
                        LatchCompletionFault("Cold continuation could not " +
                            "start: " + exception.Message);
                    }
                    continue;
                }
                SigmaFrameCompletionDisposition disposition =
                    ClassifyFrameCompletion(completion.Frame,
                        completion.PublishedRoot, completion.ExpectedRevision,
                        out string classificationError);
                if (disposition == SigmaFrameCompletionDisposition.Faulted)
                {
                    LatchCompletionFault(classificationError);
                    return;
                }
                bool retainExactEvidence = disposition ==
                    SigmaFrameCompletionDisposition.Unresolved ||
                    (disposition == SigmaFrameCompletionDisposition.Published &&
                    (completion.Frame.Identity.W == (uint)
                        SigmaNativeColdReason.RepresentationRefinement ||
                    completion.Frame.Identity.W == (uint)
                        SigmaNativeColdReason.StaticExclusion));
                bool completionJournalChanged = false;
                if (retainExactEvidence)
                {
                    SigmaConstraintAdmission journalAdmission =
                        _constraintJournal.Add(completion.Evidence);
                    completionJournalChanged = journalAdmission !=
                        SigmaConstraintAdmission.DuplicateOrWeaker;
                    Logger.Info(completion.Evidence.FormatLogLine(
                        completion.Revision) + $" journal={journalAdmission} " +
                        $"retained={_constraintJournal.Count}");
                }
                _runtimeTelemetry = SigmaRuntimeTelemetrySnapshot.From(
                    completion.Revision, completion.PublishedRoot, disposition,
                    completion.Frame, CaptureTimingTelemetry());
                Logger.Info(_runtimeTelemetry.FormatLogLine());
                if (disposition == SigmaFrameCompletionDisposition.Published)
                {
                    if (_durabilitySlot != null)
                    {
                        LatchCompletionFault("A second publication reached the " +
                            "single durable mailbox.");
                        return;
                    }
                    try
                    {
                        _durabilitySlot = slot;
                        _coldKind = ColdContinuationKind.Publication;
                        _coldPhase = ColdContinuationPhase.Persisting;
                        _carrier.Persistence.Begin(completion.PublishedRoot,
                            completion.Evidence, DurableFrontierBytes());
                    }
                    catch (Exception exception)
                    {
                        LatchCompletionFault("Durable publication could not " +
                            "start: " + exception.Message);
                        return;
                    }
                }
                else if (disposition ==
                        SigmaFrameCompletionDisposition.Unresolved &&
                    completionJournalChanged)
                {
                    if (_durabilitySlot != null)
                    {
                        LatchCompletionFault("The bounded durable mailbox " +
                            "already owns an unresolved frontier update.");
                        return;
                    }
                    try
                    {
                        _durabilitySlot = slot;
                        _coldKind = ColdContinuationKind.Frontier;
                        _coldPhase = ColdContinuationPhase.Persisting;
                        _carrier.Persistence.Begin(completion.PublishedRoot,
                            null, DurableFrontierBytes());
                    }
                    catch (Exception exception)
                    {
                        LatchCompletionFault("Durable unresolved frontier " +
                            "could not start: " + exception.Message);
                        return;
                    }
                }
                else
                    slot.Complete();
            }
        }

        private void PollDurabilityAndContinuation()
        {
            if (_durabilitySlot == null)
                return;

            if (_coldPhase == ColdContinuationPhase.Persisting)
            {
                SigmaCarrierPersistence persistence = _carrier.Persistence;
                persistence.Poll();
                if (persistence.Activity == SigmaDurableActivity.Faulted)
                {
                    LatchCompletionFault("Sigma durable HEAD failed closed: " +
                        persistence.Fault);
                    return;
                }
                if (persistence.TryTakePublication(
                        out SigmaDurablePublication publication))
                {
                    try
                    {
                        _constraintJournal.AcknowledgeDurable(
                            _pendingFrontierSnapshot);
                        _pendingFrontierSnapshot = default;
                        Logger.Info("Sigma N5 durable publication accepted: " +
                            "revision=" + publication.Commit.RootObject.Revision +
                            ".");
                        if (_coldKind == ColdContinuationKind.Frontier)
                        {
                            CompleteDurableFrontier();
                            return;
                        }
                        _carrier.RebuildPagerFromDurableResidentSnapshot(
                            publication.RetiredPairs);
                        _coldPhase = ColdContinuationPhase
                            .AdoptingResidentSnapshot;
                    }
                    catch (Exception exception)
                    {
                        LatchCompletionFault("Durable resident adoption " +
                            "failed: " + exception.Message);
                        return;
                    }
                }
            }

            if (_coldPhase == ColdContinuationPhase.None ||
                _coldPhase == ColdContinuationPhase.Persisting)
                return;

            SigmaCarrierPager pager = _carrier.Pager;
            pager.Poll();
            if (pager.Activity == SigmaPagerActivity.Faulted)
            {
                LatchCompletionFault("Sigma cold pager failed closed: " +
                    pager.Fault);
                return;
            }
            if (pager.Activity != SigmaPagerActivity.Idle)
                return;

            if (_coldPhase ==
                ColdContinuationPhase.AdoptingResidentSnapshot)
            {
                if (_coldKind == ColdContinuationKind.Publication)
                {
                    CompleteDurablePublication();
                    return;
                }
                _coldPhase = ColdContinuationPhase
                    .ResolvingPredictionRequests;
            }

            if (_coldPhase ==
                ColdContinuationPhase.ResolvingPredictionRequests)
            {
                try
                {
                    ResolvePredictionRequests(_durabilitySlot,
                        _coldFreshPairsRequired);
                }
                catch (Exception exception)
                {
                    LatchCompletionFault("Cold request resolution failed: " +
                        exception.Message);
                }
                return;
            }

            if (_coldPhase == ColdContinuationPhase.PublishingAbsent)
            {
                BeginEvictionOrRehydrate();
                return;
            }

            if (_coldPhase ==
                ColdContinuationPhase.AwaitingLegalEviction)
            {
                if (_coldEvictionsRemaining <= 0)
                {
                    BeginNextRehydrateOrReplay();
                    return;
                }
                SigmaResidencyKey evicted;
                bool started;
                if (_coldTargetEvictionOnly)
                    started = pager.TryBeginNativeTargetEviction(
                        _coldFreshPairsRequired, _coldProtectedKeys,
                        out evicted);
                else
                    started = pager.TryBeginAnyEviction(_coldProtectedKeys,
                        out evicted);
                if (started)
                {
                    _coldPhase = ColdContinuationPhase.Evicting;
                    Logger.Info("Sigma N5 COLD_EVICT: logical=" +
                        evicted.Coordinate + " generation=" +
                        evicted.PageGeneration + ".");
                }
                return;
            }

            if (_coldPhase == ColdContinuationPhase.Evicting)
            {
                try
                {
                    if (!pager.TryTakeCompletedEviction(out _,
                            out int segmentIndex, out int pairIndex))
                        throw new InvalidOperationException(
                            "A completed eviction lost its physical-pair " +
                            "retirement receipt.");
                    _carrier.Persistence.ForgetResidentPair(segmentIndex,
                        pairIndex);
                    _coldEvictionsRemaining--;
                    _coldPhase = ColdContinuationPhase
                        .AwaitingLegalEviction;
                }
                catch (Exception exception)
                {
                    LatchCompletionFault("Cold eviction retirement failed: " +
                        exception.Message);
                }
                return;
            }

            if (_coldPhase == ColdContinuationPhase.Rehydrating)
            {
                try
                {
                    int completed = 0;
                    while (pager.TryTakeCompletedLoad(
                        out SigmaStagedDurablePage loaded))
                    {
                        _carrier.Persistence.RememberResidentPage(loaded);
                        _coldProtectedKeys.Add(new SigmaResidencyKey(
                            loaded.Update.Coordinate,
                            loaded.Update.Revision,
                            loaded.Update.PageGeneration));
                        completed++;
                    }
                    if (completed != _coldLoadBatchCount)
                        throw new InvalidOperationException(
                            "A cold rehydrate batch lost a resident receipt.");
                    _coldLoadOffset += completed;
                    _coldLoadBatchCount = 0;
                    BeginNextRehydrateOrReplay();
                }
                catch (Exception exception)
                {
                    LatchCompletionFault("Cold rehydrate completion failed: " +
                        exception.Message);
                }
                return;
            }

            if (_coldPhase == ColdContinuationPhase.ReadyToReplay)
            {
                try
                {
                    TryReplayRetainedObservation(_durabilitySlot);
                }
                catch (Exception exception)
                {
                    LatchCompletionFault("Cold continuation replay failed: " +
                        exception.Message);
                }
            }
        }

        private void BeginStartupRestore()
        {
            _startupRestoreKeys.Clear();
            _startupRestoreOffset = 0;
            _startupRestoreBatchCount = 0;
            ulong durableRevision = _pool.DurableRevision;
            if (durableRevision == 0UL)
                return;
            if (durableRevision >= uint.MaxValue)
                throw new OverflowException(
                    "The durable native revision exceeds its uint GPU ABI.");
            _nextRevision = checked((uint)durableRevision + 1u);
            SigmaDurableStore store = _carrier.Persistence.Store;
            using SigmaDurableRootLease root = store.PinHead();
            IReadOnlyList<SigmaDurablePageRecord> records =
                store.EnumeratePageRecords(root);
            int first = Math.Max(0,
                records.Count - _carrier.Pager.PairCapacity);
            for (int index = first; index < records.Count; ++index)
            {
                SigmaDurablePageRecord record = records[index];
                _startupRestoreKeys.Add(new SigmaResidencyKey(
                    record.Coordinate, record.Revision,
                    record.PageGeneration));
            }
            _startupRestorePending = true;
            if (_startupRestoreKeys.Count == 0)
            {
                PublishRestoredRoot();
                return;
            }
            BeginNextStartupRestoreBatch();
            Logger.Info("Sigma N5 startup restore: durableRevision=" +
                durableRevision + " logicalPages=" + records.Count +
                " residentWorkingSet=" + _startupRestoreKeys.Count + ".");
        }

        private SigmaExactConstraintJournal LoadDurableConstraintJournal()
        {
            SigmaDurableStore store = _carrier.Persistence.Store;
            if (!store.HasHead)
                return new SigmaExactConstraintJournal();
            using SigmaDurableRootLease root = store.PinHead();
            return store.TryReadUnresolvedFrontier(root, out byte[] bytes)
                ? SigmaExactConstraintJournal.DecodeCanonical(bytes)
                : new SigmaExactConstraintJournal();
        }

        private byte[] DurableFrontierBytes()
        {
            if (_constraintJournal.Count == 0)
            {
                _pendingFrontierSnapshot = default;
                return null;
            }
            _pendingFrontierSnapshot =
                _constraintJournal.EncodeDurableCanonical();
            return _pendingFrontierSnapshot.Bytes;
        }

        private bool TryUseAlreadyDurableCapacityRoot(uint publishedRoot,
            byte[] requestedFrontier)
        {
            if (_pool.DurableRevision != publishedRoot)
                return false;
            SigmaDurableStore store = _carrier.Persistence.Store;
            if (!store.HasHead || store.Head.Revision != publishedRoot)
                return false;
            using SigmaDurableRootLease root = store.PinHead();
            bool hasDurableFrontier = store.TryReadUnresolvedFrontier(root,
                out byte[] durableFrontier);
            if (!CapacityRootAndFrontierAreAlreadyDurable(
                    _pool.DurableRevision, publishedRoot, requestedFrontier,
                    hasDurableFrontier, durableFrontier))
                return false;
            _constraintJournal.AcknowledgeDurable(_pendingFrontierSnapshot);
            _pendingFrontierSnapshot = default;
            return true;
        }

        internal static bool CapacityRootAndFrontierAreAlreadyDurable(
            ulong durableRevision, uint publishedRoot, byte[] requestedFrontier,
            bool hasDurableFrontier, byte[] durableFrontier)
        {
            if (durableRevision != publishedRoot)
                return false;
            if (requestedFrontier == null)
                return !hasDurableFrontier;
            if (!hasDurableFrontier || durableFrontier == null ||
                requestedFrontier.Length != durableFrontier.Length)
                return false;
            for (int index = 0; index < requestedFrontier.Length; ++index)
                if (requestedFrontier[index] != durableFrontier[index])
                    return false;
            return true;
        }

        private void PollStartupRestore()
        {
            if (!_startupRestorePending)
                return;
            SigmaCarrierPager pager = _carrier.Pager;
            pager.Poll();
            if (pager.Activity == SigmaPagerActivity.Faulted)
            {
                LatchCompletionFault("Sigma startup rehydrate failed closed: " +
                    pager.Fault);
                return;
            }
            if (pager.Activity != SigmaPagerActivity.Idle)
                return;
            if (_startupRestoreBatchCount != 0)
            {
                int completed = 0;
                while (pager.TryTakeCompletedLoad(
                    out SigmaStagedDurablePage loaded))
                {
                    _carrier.Persistence.RememberResidentPage(loaded);
                    completed++;
                }
                if (completed != _startupRestoreBatchCount)
                {
                    LatchCompletionFault(
                        "Sigma startup restore lost a resident receipt.");
                    return;
                }
                _startupRestoreOffset += completed;
                _startupRestoreBatchCount = 0;
            }
            if (_startupRestoreOffset < _startupRestoreKeys.Count)
            {
                BeginNextStartupRestoreBatch();
                return;
            }
            PublishRestoredRoot();
        }

        private void BeginNextStartupRestoreBatch()
        {
            int bankFree = _carrier.Pager.MaximumFreePairCountInBank;
            if (bankFree == 0)
                throw new InvalidOperationException(
                    "No resident binding bank can accept restored pages.");
            _startupRestoreBatchCount = Math.Min(bankFree, Math.Min(
                SigmaNativeVulkanColdUpload.MaximumPages,
                _startupRestoreKeys.Count - _startupRestoreOffset));
            var batch = _startupRestoreKeys.GetRange(_startupRestoreOffset,
                _startupRestoreBatchCount);
            if (!_carrier.Pager.BeginRehydrate(batch))
                throw new InvalidOperationException(
                    "The bounded startup rehydrate batch did not start.");
        }

        private void PublishRestoredRoot()
        {
            uint revision = checked((uint)_pool.DurableRevision);
            _pool.PublicationRoot.SetData(new[] { revision });
            _startupRestorePending = false;
            _startupRestoreKeys.Clear();
            _startupRestoreOffset = 0;
            _startupRestoreBatchCount = 0;
            Logger.Info("Sigma N5 restored durable root-last selector: root=" +
                revision + " residentPairs=" +
                _carrier.Pager.ResidentPairCount + ".");
        }

        private void CompleteDurablePublication()
        {
            Logger.Info("Sigma N5 resident locator adopted durable revision=" +
                _pool.DurableRevision + ".");
            _durabilitySlot.Complete();
            _durabilitySlot = null;
            CommittedFrames++;
            ResetColdContinuation();
        }

        private void CompleteDurableFrontier()
        {
            Logger.Info("Sigma N5 unresolved frontier reached durable HEAD " +
                "without resident-page or canonical-root mutation.");
            _durabilitySlot.Complete();
            _durabilitySlot = null;
            ResetColdContinuation();
        }

        private void ResolvePredictionRequests(IngressSlot slot,
            int freshPairsRequired)
        {
            if (freshPairsRequired < 0 ||
                freshPairsRequired > _pool.PairCount)
                throw new ArgumentOutOfRangeException(
                    nameof(freshPairsRequired));
            _coldLoadKeys.Clear();
            _coldAbsentKeys.Clear();
            _coldProtectedKeys.Clear();
            _coldLoadOffset = 0;
            _coldLoadBatchCount = 0;
            var loads = new HashSet<SigmaResidencyKey>();
            var absent = new HashSet<SigmaResidencyKey>();
            SigmaCarrierPager pager = _carrier.Pager;
            SigmaDurableStore store = _carrier.Persistence.Store;
            using SigmaDurableRootLease root = store.PinHead();
            foreach (SigmaPredictionPageRequest request in
                slot.PredictionPageRequests)
            {
                SigmaPredictionPageRequestFlags flags = request.Flags;
                if ((flags & SigmaPredictionPageRequestFlags.Coherent) == 0 ||
                    (flags & SigmaPredictionPageRequestFlags.Invalid) != 0 ||
                    request.PageGeneration == 0u ||
                    request.PageRevision == 0u)
                    throw new InvalidDataException(
                        "A prediction request has an invalid exact key/flag.");
                var key = new SigmaResidencyKey(request.Coordinate,
                    request.PageRevision, request.PageGeneration);
                bool exists = store.TryGetPageRecord(root,
                    request.Coordinate, out SigmaDurablePageRecord record);
                bool exact = exists &&
                    record.Revision == request.PageRevision &&
                    record.PageGeneration == request.PageGeneration;

                if ((flags & SigmaPredictionPageRequestFlags.Hot) != 0)
                {
                    if (!exact || (pager.StateOf(key) !=
                            SigmaResidencyState.HotClean &&
                        pager.StateOf(key) != SigmaResidencyState.HotDirty))
                        throw new InvalidDataException(
                            "A HOT prediction key is not the exact selected " +
                            "durable resident generation.");
                    _coldProtectedKeys.Add(key);
                }
                if ((flags & SigmaPredictionPageRequestFlags.Directory) != 0)
                {
                    if (!exists)
                        absent.Add(key);
                    else if (!exact)
                        throw new InvalidDataException(
                            "A cold prediction key is stale relative to the " +
                            "selected durable PageRecord.");
                    else
                    {
                        SigmaResidencyState state = pager.StateOf(key);
                        if (state == SigmaResidencyState.HotClean ||
                            state == SigmaResidencyState.HotDirty)
                            _coldProtectedKeys.Add(key);
                        else if (state == SigmaResidencyState.ColdDurable)
                            loads.Add(key);
                        else
                            throw new InvalidOperationException(
                                "A cold prediction key has an illegal idle " +
                                "residency state " + state + ".");
                    }
                }
                if ((flags & SigmaPredictionPageRequestFlags.Absent) != 0)
                {
                    if (exists)
                        throw new InvalidDataException(
                            "ABSENT_IN_ROOT locator contradicts durable HEAD.");
                    absent.Add(key);
                }
            }
            if (freshPairsRequired != 0 &&
                _pool.DurableLogicalExtent % SigmaCarrier.SamplesPerPage != 0u)
            {
                var tailCoordinate = new SigmaCarrierPageCoordinate(
                    _pool.DurableLogicalExtent / SigmaCarrier.SamplesPerPage,
                    0L);
                if (!store.TryGetPageRecord(root, tailCoordinate,
                        out SigmaDurablePageRecord tailRecord))
                    throw new InvalidDataException(
                        "The durable append tail is absent from selected HEAD.");
                var tailKey = new SigmaResidencyKey(tailCoordinate,
                    tailRecord.Revision, tailRecord.PageGeneration);
                SigmaResidencyState tailState = pager.StateOf(tailKey);
                if (tailState == SigmaResidencyState.HotClean ||
                    tailState == SigmaResidencyState.HotDirty)
                    _coldProtectedKeys.Add(tailKey);
                else if (tailState == SigmaResidencyState.ColdDurable)
                    loads.Add(tailKey);
                else
                    throw new InvalidOperationException(
                        "The exact durable append tail has no legal residency " +
                        "state: " + tailState + ".");
            }
            _coldLoadKeys.AddRange(loads);
            _coldAbsentKeys.AddRange(absent);
            _coldLoadKeys.Sort(CompareResidencyKeys);
            _coldAbsentKeys.Sort(CompareResidencyKeys);
            _coldEvictionsRemaining = CapacityEvictionsRequired(
                pager.PairCapacity, pager.FreePairCount,
                _coldProtectedKeys.Count, _coldLoadKeys.Count,
                freshPairsRequired);
            Logger.Info("Sigma N5 cold request plan: loads=" +
                _coldLoadKeys.Count + " absent=" + _coldAbsentKeys.Count +
                " protected=" + _coldProtectedKeys.Count + " evictions=" +
                _coldEvictionsRemaining + " freshPairs=" +
                freshPairsRequired + " targetExtent=" +
                _coldTargetLogicalExtent + ".");
            if (_coldAbsentKeys.Count != 0)
            {
                if (!pager.BeginPublishAbsent(_coldAbsentKeys))
                    throw new InvalidOperationException(
                        "Exact ABSENT_IN_ROOT publication did not start.");
                _coldPhase = ColdContinuationPhase.PublishingAbsent;
            }
            else
                BeginEvictionOrRehydrate();
        }

        private void BeginEvictionOrRehydrate()
        {
            _coldTargetEvictionOnly = false;
            if (_coldEvictionsRemaining != 0)
                _coldPhase = ColdContinuationPhase.AwaitingLegalEviction;
            else
                BeginNextRehydrateOrReplay();
        }

        private void BeginNextRehydrateOrReplay()
        {
            if (_coldLoadOffset >= _coldLoadKeys.Count)
            {
                int targetDeficit = Math.Max(0, _coldFreshPairsRequired -
                    _carrier.Pager.NativeTargetFreePairCount);
                if (targetDeficit != 0)
                {
                    _coldEvictionsRemaining = targetDeficit;
                    _coldTargetEvictionOnly = true;
                    _coldPhase = ColdContinuationPhase.AwaitingLegalEviction;
                    return;
                }
                _coldTargetEvictionOnly = false;
                _coldPhase = ColdContinuationPhase.ReadyToReplay;
                return;
            }
            int bankFree = _carrier.Pager.MaximumFreePairCountInBank;
            if (bankFree == 0)
                throw new InvalidOperationException(
                    "No resident binding bank can accept cold pages.");
            _coldLoadBatchCount = Math.Min(bankFree, Math.Min(
                SigmaNativeVulkanColdUpload.MaximumPages,
                _coldLoadKeys.Count - _coldLoadOffset));
            var batch = _coldLoadKeys.GetRange(_coldLoadOffset,
                _coldLoadBatchCount);
            if (!_carrier.Pager.BeginRehydrate(batch))
                throw new InvalidOperationException(
                    "Exact cold rehydrate batch did not start.");
            _coldPhase = ColdContinuationPhase.Rehydrating;
            Logger.Info("Sigma N5 COLD_REHYDRATE: pages=" +
                _coldLoadBatchCount + ".");
        }

        private static int CompareResidencyKeys(SigmaResidencyKey left,
            SigmaResidencyKey right)
        {
            int coordinate = left.Coordinate.CompareTo(right.Coordinate);
            if (coordinate != 0) return coordinate;
            int root = left.RootContext.CompareTo(right.RootContext);
            return root != 0 ? root : left.PageGeneration.CompareTo(
                right.PageGeneration);
        }

        private void ResetColdContinuation()
        {
            _coldKind = ColdContinuationKind.None;
            _coldPhase = ColdContinuationPhase.None;
            _coldLoadKeys.Clear();
            _coldAbsentKeys.Clear();
            _coldProtectedKeys.Clear();
            _coldEvictionsRemaining = 0;
            _coldFreshPairsRequired = 0;
            _coldTargetEvictionOnly = false;
            _coldTargetLogicalExtent = 0u;
            _coldLoadOffset = 0;
            _coldLoadBatchCount = 0;
        }

        private bool TryReplayRetainedObservation(IngressSlot slot)
        {
            if (slot == null || !slot.InFlight ||
                !slot.AwaitingDisposition)
                throw new InvalidOperationException(
                    "Capacity replay lost its retained observation owner.");
            if (!_graph.TryAcquire(out SigmaNativeFrameLease ownedFrame))
                return false;

            SigmaNativeCompletionTransfer.Reservation completion = default;
            SigmaNativeVulkanExecutor.SigmaNativeVulkanJob nativeJob = null;
            CommandBuffer command = null;
            SigmaGpuCompletionTicket nativeDone = default;
            bool submissionAttempted = false;
            bool submitted = false;
            bool slotOwnsSubmission = false;
            List<IDisposable> residencyLeases = null;
            uint revision = slot.Revision;
            try
            {
                completion = _completionTransfer.Reserve(revision);
                SigmaCarrierReadBatch target =
                    _carrier.SelectNativeTargetBank();
                Logger.Info("Sigma N5 replay target: revision=" + revision +
                    " segment=" + target.SegmentIndex + " freePairs=" +
                    _carrier.Pager.FreePairCountInSegment(
                        target.SegmentIndex) + " durableExtent=" +
                    target.DurableLogicalExtent + " partialTail=" +
                    (target.DurableLogicalExtent %
                        SigmaCarrier.SamplesPerPage != 0u) + ".");
                SigmaNativeFrameInput input = slot.ReplayInput(_pool,
                    _secondaryPool, target);
                nativeJob = _graph.CreateNativeCloseJob(ownedFrame, revision,
                    slot.CalibrationEpoch, input, completion.Buffer,
                    completion.RecordIndex);
                command = CommandBufferPool.Get(
                    "Sigma N5 COLD_CAPACITY_CONTINUATION replay");
                nativeJob.RecordPrepare(command);
                nativeJob.RecordSubmit(command);
                nativeDone = new SigmaGpuCompletionTicket(nativeJob);
                residencyLeases = PinResidentBank(nativeDone);
                submissionAttempted = true;
                Graphics.ExecuteCommandBuffer(command);
                submitted = true;
                TrackLast(nativeDone);
                slot.BeginReplay(ownedFrame, completion, nativeJob, nativeDone,
                    revision, residencyLeases,
                    Time.realtimeSinceStartupAsDouble);
                slotOwnsSubmission = true;
                ownedFrame = null;
                nativeJob = null;
                residencyLeases = null;
                _durabilitySlot = null;
                ResetColdContinuation();
                SubmittedFrames++;
                Logger.Info("Sigma N5 exact retained replay submitted: revision=" +
                    revision + ".");
                return true;
            }
            finally
            {
                if (completion.IsValid && !submissionAttempted)
                    _completionTransfer.Cancel(completion);
                if (submissionAttempted && !submitted)
                {
                    SigmaNativeFrameLease uncertainFrame = ownedFrame;
                    SigmaNativeVulkanExecutor.SigmaNativeVulkanJob
                        uncertainJob = nativeJob;
                    List<IDisposable> uncertainResidencyLeases =
                        residencyLeases;
                    SigmaGpuRetirement.Quarantine(() =>
                    {
                        uncertainFrame?.Dispose();
                        uncertainJob?.Dispose();
                        ReleaseResidencyLeases(uncertainResidencyLeases);
                    }, "Sigma N5 uncertain capacity replay",
                    "Unity command submission returned without terminal " +
                    "native ownership proof.");
                    _completionTransfer.CompleteNativeFailure(completion,
                        "Capacity replay submission ownership is uncertain.");
                    ownedFrame = null;
                    nativeJob = null;
                    residencyLeases = null;
                }
                else if (submitted && !slotOwnsSubmission)
                {
                    SigmaNativeFrameLease retireFrame = ownedFrame;
                    SigmaNativeVulkanExecutor.SigmaNativeVulkanJob retireJob =
                        nativeJob;
                    SigmaNativeCompletionTransfer.Reservation retireCompletion =
                        completion;
                    List<IDisposable> retireResidencyLeases =
                        residencyLeases;
                    SigmaGpuRetirement.Retire(nativeDone, () =>
                    {
                        try
                        {
                            if (retireJob != null &&
                                retireJob.TryReadCompletion(
                                    out SigmaFrameUInt2Gpu[] words))
                                _completionTransfer.CompleteNative(
                                    retireCompletion, words);
                            else
                                _completionTransfer.CompleteNativeFailure(
                                    retireCompletion,
                                    "Orphaned capacity replay completion bytes " +
                                    "were unavailable.");
                        }
                        finally
                        {
                            retireFrame?.Dispose();
                            retireJob?.Dispose();
                            ReleaseResidencyLeases(retireResidencyLeases);
                        }
                    }, "Sigma orphaned N5 capacity replay");
                    ownedFrame = null;
                    nativeJob = null;
                    residencyLeases = null;
                }
                else if (!submissionAttempted)
                {
                    nativeJob?.CancelBeforeExecution();
                    nativeJob = null;
                }
                ownedFrame?.Dispose();
                nativeJob?.Dispose();
                ReleaseResidencyLeases(residencyLeases);
                if (command != null)
                    CommandBufferPool.Release(command);
            }
        }

        private IngressSlot FindAwaitingDispositionSlot(uint revision)
        {
            if (_ingressSlots == null)
                return null;
            for (int index = 0; index < _ingressSlots.Length; ++index)
            {
                IngressSlot slot = _ingressSlots[index];
                if (slot.InFlight && slot.AwaitingDisposition &&
                    slot.Revision == revision)
                    return slot;
            }
            return null;
        }

        internal static bool IsCapacityContinuationReceipt(
            SigmaNativeCompletionRecord completion) =>
            TryClassifyColdContinuationReceipt(completion,
                out ColdContinuationKind kind) &&
            (kind == ColdContinuationKind.Capacity ||
             kind == ColdContinuationKind.CapacityAndResidency);

        internal static int CapacityFreshPairCount(uint durableExtent,
            uint targetExtent)
        {
            if (targetExtent < durableExtent)
                throw new InvalidDataException(
                    "A capacity receipt regressed the durable logical extent.");
            ulong pageSize = SigmaCarrier.SamplesPerPage;
            ulong durablePages = ((ulong)durableExtent + pageSize - 1UL) /
                pageSize;
            ulong targetPages = ((ulong)targetExtent + pageSize - 1UL) /
                pageSize;
            ulong fresh = targetPages - durablePages;
            // A capacity receipt can also arise from relocation into one
            // nonresident logical page without extending the append frontier.
            // Preserve the exact transaction by making at least one legal plan
            // slot available; append growth reserves every newly named page.
            fresh = Math.Max(1UL, fresh);
            if (fresh > int.MaxValue)
                throw new InvalidDataException(
                    "A capacity receipt exceeds the bounded resident ABI.");
            return checked((int)fresh);
        }

        internal static int CapacityEvictionsRequired(int pairCapacity,
            int freePairs, int protectedPairs, int coldLoads, int freshPairs)
        {
            if (pairCapacity <= 0 || freePairs < 0 || protectedPairs < 0 ||
                coldLoads < 0 || freshPairs < 0 || freePairs > pairCapacity ||
                protectedPairs > pairCapacity - freePairs)
                throw new ArgumentOutOfRangeException(nameof(pairCapacity));
            int incoming = checked(coldLoads + freshPairs);
            int exactWorkingSet = checked(protectedPairs + incoming);
            if (exactWorkingSet > pairCapacity)
                throw new InvalidOperationException(
                    "The exact transaction working set exceeds the bounded " +
                    "resident bank; admission remains backpressured without " +
                    "overwriting a protected generation.");
            return Math.Max(0, incoming - freePairs);
        }

        private static bool TryClassifyColdContinuationReceipt(
            SigmaNativeCompletionRecord completion,
            out ColdContinuationKind kind)
        {
            kind = ColdContinuationKind.None;
            if (completion == null || completion.ExpectedRevision == 0u ||
                completion.Frame.Identity.X != completion.ExpectedRevision ||
                completion.PublishedRoot == completion.ExpectedRevision ||
                completion.Frame.Publication.X != completion.PublishedRoot ||
                completion.Frame.Publication.Y != 0u ||
                completion.Frame.Disposition.X !=
                    (uint)SigmaNativeFrameDisposition.Faulted ||
                completion.Frame.Disposition.W !=
                    (uint)SigmaNativeColdReason.PageFault)
                return false;
            uint receipt = completion.Frame.Disposition.Y;
            if ((receipt & N5ResidencyInvalidReceipt) != 0u ||
                (receipt & ~(N5CapacityFaultReceipt |
                    N5ResidencyRequiredReceipt)) != 0u)
                return false;
            bool capacity = (receipt & N5CapacityFaultReceipt) ==
                N5CapacityFaultReceipt;
            bool residency = (receipt & N5ResidencyRequiredReceipt) != 0u;
            if (!capacity && !residency)
                return false;
            kind = capacity && residency
                ? ColdContinuationKind.CapacityAndResidency
                : capacity ? ColdContinuationKind.Capacity
                : ColdContinuationKind.Residency;
            return true;
        }

        private bool HasInFlightIngress()
        {
            if (_ingressSlots == null)
                return false;
            for (int index = 0; index < _ingressSlots.Length; ++index)
                if (_ingressSlots[index].InFlight)
                    return true;
            return false;
        }

        private void LatchCompletionFault(string message)
        {
            if (_completionFaulted)
                return;
            _completionFaulted = true;
            _running = false;
            FailedFrames++;
            Logger.Error(message);
        }

        private void RecordNormalize(CommandBuffer command, IngressSlot slot,
            StereoRigFrameLease source, ConeLutLease luts)
        {
            command.SetComputeIntParams(_normalizeShader, "_Resolution",
                source.DepthResolution.x, source.DepthResolution.y);
            command.SetComputeVectorParam(_normalizeShader, "_NearFar",
                new Vector4(source.DepthNearFar.x, source.DepthNearFar.y,
                    0f, 0f));
            command.SetComputeTextureParam(_normalizeShader, _normalizeKernel,
                "_RawDepth", source.DepthLeft.Texture);
            command.SetComputeTextureParam(_normalizeShader, _normalizeKernel,
                "_DepthRayCenterLeft",
                luts.DepthLeft.CenterRaySolidAngle);
            command.SetComputeTextureParam(_normalizeShader, _normalizeKernel,
                "_DepthRayCenterRight",
                luts.DepthRight.CenterRaySolidAngle);
            command.SetComputeTextureParam(_normalizeShader, _normalizeKernel,
                "_MetricDepth", slot.MetricDepth);
            command.SetComputeTextureParam(_normalizeShader, _normalizeKernel,
                "_DepthFlags", slot.DepthFlags);
            command.DispatchComputeProfiled(_normalizeShader, _normalizeKernel,
                CeilDiv(source.DepthResolution.x, 8),
                CeilDiv(source.DepthResolution.y, 8), 2);
        }

        private void RecordPoseGauge(CommandBuffer command, IngressSlot slot,
            StereoRigFrameLease source, SigmaPredictionFrameLease prediction,
            uint revision, ConeLutLease luts)
        {
            int sampleWidth = CeilDiv(source.DepthResolution.x,
                poseSampleStride);
            int sampleHeight = CeilDiv(source.DepthResolution.y,
                poseSampleStride);
            int partialCount = CeilDiv(checked(sampleWidth * sampleHeight * 2),
                64);
            slot.EnsurePosePartials(partialCount);
            command.SetComputeBufferParam(_poseGaugeShader, _poseBuildKernel,
                "_SigmaExactBackendGate", _backendGate.Buffer);
            command.SetComputeBufferParam(_poseGaugeShader, _poseBuildKernel,
                "_DepthCalibrationQ48", slot.RawDepthCalibration);
            command.SetComputeBufferParam(_poseGaugeShader, _poseBuildKernel,
                "_PosePrior", slot.PosePrior);
            command.SetComputeBufferParam(_poseGaugeShader, _poseBuildKernel,
                "_PosePartials", slot.PosePartials);
            command.SetComputeTextureParam(_poseGaugeShader, _poseBuildKernel,
                "_PoseMetricDepth", slot.MetricDepth);
            command.SetComputeTextureParam(_poseGaugeShader, _poseBuildKernel,
                "_PoseDepthFlags", slot.DepthFlags);
            command.SetComputeTextureParam(_poseGaugeShader, _poseBuildKernel,
                "_PosePredDepthSupport", prediction.DepthSupport);
            command.SetComputeTextureParam(_poseGaugeShader, _poseBuildKernel,
                "_PosePredCarrierUvNormal", prediction.CarrierUvNormal);
            command.SetComputeTextureParam(_poseGaugeShader, _poseBuildKernel,
                "_PoseRayLeft", luts.DepthLeft.CenterRaySolidAngle);
            command.SetComputeTextureParam(_poseGaugeShader, _poseBuildKernel,
                "_PoseRayRight", luts.DepthRight.CenterRaySolidAngle);
            command.SetComputeIntParams(_poseGaugeShader, "_PoseResolution",
                source.DepthResolution.x, source.DepthResolution.y);
            command.SetComputeIntParam(_poseGaugeShader, "_PoseSampleStride",
                poseSampleStride);
            command.SetComputeIntParam(_poseGaugeShader, "_PoseRevision",
                unchecked((int)revision));
            command.SetComputeIntParam(_poseGaugeShader, "_PosePartialCount",
                partialCount);
            command.DispatchComputeProfiled(_poseGaugeShader, _poseBuildKernel,
                partialCount, 1, 1);

            command.SetComputeBufferParam(_poseGaugeShader, _poseReduceKernel,
                "_SigmaExactBackendGate", _backendGate.Buffer);
            command.SetComputeBufferParam(_poseGaugeShader, _poseReduceKernel,
                "_PosePrior", slot.PosePrior);
            command.SetComputeBufferParam(_poseGaugeShader, _poseReduceKernel,
                "_PosePartials", slot.PosePartials);
            command.SetComputeBufferParam(_poseGaugeShader, _poseReduceKernel,
                "_PoseResult", slot.PoseResult);
            command.DispatchComputeProfiled(_poseGaugeShader, _poseReduceKernel,
                1, 1, 1);
        }

        private void RecordCorrectedCalibration(CommandBuffer command,
            IngressSlot slot, StereoRigFrameLease source,
            Matrix4x4 worldToRoom)
        {
            Matrix4x4 referenceWorld = SigmaRoomFrame.FromCamera(worldToRoom,
                source.DepthLeft.WorldFromCamera);
            command.SetComputeBufferParam(_poseGaugeShader,
                _poseCalibrationKernel, "_DepthCalibrationQ48",
                slot.RawDepthCalibration);
            command.SetComputeBufferParam(_poseGaugeShader,
                _poseCalibrationKernel, "_PoseRgbCalibrationQ48",
                slot.RawRgbCalibration);
            command.SetComputeBufferParam(_poseGaugeShader,
                _poseCalibrationKernel, "_CorrectedDepthCalibrationQ48",
                slot.CorrectedDepthCalibration);
            command.SetComputeBufferParam(_poseGaugeShader,
                _poseCalibrationKernel, "_CorrectedRgbCalibrationQ48",
                slot.CorrectedRgbCalibration);
            command.SetComputeBufferParam(_poseGaugeShader,
                _poseCalibrationKernel, "_PoseResult", slot.PoseResult);
            command.SetComputeMatrixParam(_poseGaugeShader,
                "_PoseConsumeReferenceFromWorld", referenceWorld.inverse);
            command.SetComputeMatrixParam(_poseGaugeShader,
                "_PoseConsumeWorldFromReference", referenceWorld);
            command.DispatchComputeProfiled(_poseGaugeShader,
                _poseCalibrationKernel, 2, 1, 1);
        }

        private void UploadExactCalibration(CommandBuffer command,
            IngressSlot slot,
            StereoRigFrameLease source, Matrix4x4 worldToRoom)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            FillCalibration(0, source.DepthLeft, source.Health, worldToRoom);
            FillCalibration(1, source.DepthRight, source.Health, worldToRoom);
            command.SetBufferData(slot.RawDepthCalibration, _calibrationUpload);
            FillRgbCalibration(0, source.RgbLeft.WorldFromCamera,
                source.Health, worldToRoom);
            FillRgbCalibration(1, source.RgbRight.WorldFromCamera,
                source.Health, worldToRoom);
            command.SetBufferData(slot.RawRgbCalibration, _rgbCalibrationUpload);
        }

        private void UploadPosePrior(CommandBuffer command, IngressSlot slot,
            StereoRigFrameLease source, Matrix4x4 worldToRoom)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            for (int component = 0; component < 6; ++component)
                _posePriorUpload[component] = SigmaPackedQ48.FromRaw(0L);
            Vector2 envelope = BuildTrackingPriorEnvelope(source, worldToRoom);
            long translationWidth = SigmaNumericDomain.Quantize(envelope.x);
            long rotationWidth = SigmaNumericDomain.Quantize(envelope.y);
            for (int component = 0; component < 6; ++component)
                _posePriorUpload[6 + component] = SigmaPackedQ48.FromRaw(
                    component < 3 ? translationWidth : rotationWidth);
            _posePriorUpload[12] = SigmaPackedQ48.FromRaw(
                SigmaNumericDomain.Quantize(0.00025));
            _posePriorUpload[13] = SigmaPackedQ48.FromRaw(
                SigmaNumericDomain.Quantize(0.15));
            _posePriorUpload[14] = SigmaPackedQ48.FromRaw(
                SigmaNumericDomain.Quantize(0.03));
            command.SetBufferData(slot.PosePrior, _posePriorUpload);
        }

        private Vector2 BuildTrackingPriorEnvelope(StereoRigFrameLease source,
            Matrix4x4 worldToRoom)
        {
            Pose current = SigmaRoomFrame.CameraPose(worldToRoom,
                source.DepthLeft.WorldFromCamera);
            long timestamp = source.DepthLeft.Timestamp.UnixNanoseconds;
            float translation = poseTranslationPriorMetres;
            float rotation = poseRotationPriorDegrees * Mathf.Deg2Rad;
            if (_hasPreviousTrackingPose &&
                timestamp > _previousTrackingTimestampNs)
            {
                double deltaSeconds =
                    (timestamp - _previousTrackingTimestampNs) * 1e-9;
                long timingNanoseconds = SaturatingAdd(
                    Math.Max(0L,
                        source.Health.ClockUncertaintyNanoseconds),
                    SaturatingAdd(AbsNanoseconds(
                            source.Health.RgbDepthDeltaNanoseconds),
                        AbsNanoseconds(
                            source.Health.RgbDeltaNanoseconds) / 2L));
                double uncertaintySeconds = timingNanoseconds * 1e-9;
                float distance = Vector3.Distance(
                    _previousTrackingPose.position, current.position);
                float angle = Quaternion.Angle(
                    _previousTrackingPose.rotation, current.rotation) *
                    Mathf.Deg2Rad;
                float linearRate = distance /
                    (float)Math.Max(deltaSeconds, 1e-6);
                float angularRate = angle /
                    (float)Math.Max(deltaSeconds, 1e-6);
                translation = Mathf.Clamp(0.003f + linearRate *
                        (float)uncertaintySeconds * 2f +
                        MaxRigTranslationResidual(source),
                    0.003f, poseTranslationPriorMetres);
                rotation = Mathf.Clamp(0.25f * Mathf.Deg2Rad +
                        angularRate * (float)uncertaintySeconds * 2f +
                        MaxRigRotationResidual(source),
                    0.25f * Mathf.Deg2Rad,
                    poseRotationPriorDegrees * Mathf.Deg2Rad);
            }
            _previousTrackingPose = current;
            _previousTrackingTimestampNs = timestamp;
            _hasPreviousTrackingPose = true;
            return new Vector2(translation, rotation);
        }

        private float MaxRigTranslationResidual(StereoRigFrameLease source)
        {
            RigExtrinsicsSnapshot reference = _calibration.ReferenceExtrinsics;
            RigExtrinsicsSnapshot current = source.Extrinsics;
            return Mathf.Max(
                Vector3.Distance(reference.LeftDepthFromRightDepth.position,
                    current.LeftDepthFromRightDepth.position),
                Vector3.Distance(reference.LeftRgbFromLeftDepth.position,
                    current.LeftRgbFromLeftDepth.position),
                Vector3.Distance(reference.RightRgbFromRightDepth.position,
                    current.RightRgbFromRightDepth.position));
        }

        private float MaxRigRotationResidual(StereoRigFrameLease source)
        {
            RigExtrinsicsSnapshot reference = _calibration.ReferenceExtrinsics;
            RigExtrinsicsSnapshot current = source.Extrinsics;
            return Mathf.Deg2Rad * Mathf.Max(
                Quaternion.Angle(reference.LeftDepthFromRightDepth.rotation,
                    current.LeftDepthFromRightDepth.rotation),
                Quaternion.Angle(reference.LeftRgbFromLeftDepth.rotation,
                    current.LeftRgbFromLeftDepth.rotation),
                Quaternion.Angle(reference.RightRgbFromRightDepth.rotation,
                    current.RightRgbFromRightDepth.rotation));
        }

        private void EnsureCalibration(StereoRigFrameLease source,
            CommandBuffer command)
        {
            if (_calibration != null && _calibration.IsCompatible(source))
                return;
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (!RigCalibration.TryCreate(source,
                    out RigCalibration calibration))
                throw new InvalidOperationException(
                    "Unable to freeze inverse rig calibration.");
            _coneLuts?.Retire();
            _calibration = calibration;
            _coneLuts = RigConeLutSet.Create(_coneLutShader, calibration,
                command);
        }

        private void FindKernels()
        {
            _normalizeKernel = _normalizeShader.FindProfiledKernel(
                "NormalizeStereoDepth");
            _poseBuildKernel = _poseGaugeShader.FindProfiledKernel(
                "BuildPoseGaugePartials");
            _poseReduceKernel = _poseGaugeShader.FindProfiledKernel(
                "ReducePoseGauge");
            _poseCalibrationKernel = _poseGaugeShader.FindProfiledKernel(
                "BuildCorrectedCalibration");
        }

        private void CreatePersistentResources(int frameCapacity)
        {
            int packedStride = Marshal.SizeOf<SigmaPackedQ48>();
            ingressSlotCount = Mathf.Clamp(ingressSlotCount, 3,
                Math.Min(8, frameCapacity));
            _ingressSlots = new IngressSlot[ingressSlotCount];
            Vector2Int physical =
                SigmaNativeQuestAperture.PhysicalDepthResolution;
            int sampleWidth = CeilDiv(physical.x, poseSampleStride);
            int sampleHeight = CeilDiv(physical.y, poseSampleStride);
            int posePartialCount = CeilDiv(
                checked(sampleWidth * sampleHeight * 2), 64);
            for (int index = 0; index < _ingressSlots.Length; ++index)
            {
                _ingressSlots[index] = new IngressSlot(
                    CreateBuffer(CalibrationStride * 2, packedStride,
                        $"Sigma ingress {index} raw depth calibration"),
                    CreateBuffer(RgbCalibrationStride * 2, packedStride,
                        $"Sigma ingress {index} raw RGB calibration"),
                    CreateBuffer(CalibrationStride * 2, packedStride,
                        $"Sigma ingress {index} corrected depth calibration"),
                    CreateBuffer(RgbCalibrationStride * 2, packedStride,
                        $"Sigma ingress {index} corrected RGB calibration"),
                    CreateBuffer(PosePriorValueCount, packedStride,
                        $"Sigma ingress {index} pose prior"),
                    CreateBuffer(4, sizeof(uint) * 4,
                        $"Sigma ingress {index} pose result"), index);
                _ingressSlots[index].EnsureFrameResources(physical);
                _ingressSlots[index].EnsurePosePartials(posePartialCount);
            }
        }

        private void FillCalibration(int eye, GpuImageView view,
            RigPairingHealth health, Matrix4x4 worldToRoom)
        {
            int offset = eye * CalibrationStride;
            SetQ(offset + 0, view.Intrinsics.FocalLength.x);
            SetQ(offset + 1, view.Intrinsics.FocalLength.y);
            SetQ(offset + 2, view.Intrinsics.PrincipalPoint.x);
            SetQ(offset + 3, view.Intrinsics.PrincipalPoint.y);
            Matrix4x4 world = SigmaRoomFrame.FromCamera(worldToRoom,
                view.WorldFromCamera);
            int cursor = offset + 4;
            for (int row = 0; row < 3; ++row)
            for (int column = 0; column < 3; ++column)
                SetQ(cursor++, world[row, column]);
            SetQ(offset + 13, world[0, 3]);
            SetQ(offset + 14, world[1, 3]);
            SetQ(offset + 15, world[2, 3]);
            SetQ(offset + 16, view.DepthNearFar.x);
            SetQ(offset + 17, RigDepthContract.FiniteRasterFar(
                view.DepthNearFar));
            double clockWidth = Math.Min(0.01,
                health.ClockUncertaintyNanoseconds * 1e-9 * 0.5);
            SetQ(offset + 18, Math.Max(0.001, clockWidth));
            double[] thresholds = { 0.5, 1.0, 2.0, 3.0, 5.0, 32767.0 };
            double[] widths = { 0.003, 0.0045, 0.007, 0.012, 0.025, 0.05 };
            for (int bin = 0; bin < 6; ++bin)
            {
                SetQ(offset + 19 + bin, thresholds[bin]);
                SetQ(offset + 25 + bin, widths[bin]);
            }
            SetQ(offset + 31, 0.001);
            SetQ(offset + 32, 0.05);
            SetQRaw(offset + 33, SigmaNumericDomain.FromRatio(1, 64));
            SetQRaw(offset + 34, 0L);
            SetQRaw(offset + 35, 0L);
        }

        private void FillRgbCalibration(int eye, Pose pose,
            RigPairingHealth health, Matrix4x4 worldToRoom)
        {
            int offset = eye * RgbCalibrationStride;
            Matrix4x4 roomFromCamera = SigmaRoomFrame.FromCamera(worldToRoom,
                pose);
            SetRgbQ(offset + 0, roomFromCamera.m03);
            SetRgbQ(offset + 1, roomFromCamera.m13);
            SetRgbQ(offset + 2, roomFromCamera.m23);
            SetRgbQRaw(offset + 3, SigmaNumericDomain.FromRatio(2, 255));
            SetRgbQRaw(offset + 4, SigmaNumericDomain.FromRatio(1, 64));
            double clockWidth = Math.Min(0.01,
                health.ClockUncertaintyNanoseconds * 1e-9 * 0.5);
            SetRgbQ(offset + 5, Math.Max(0.0005, clockWidth));
            SetRgbQRaw(offset + 6, SigmaNumericDomain.FromRatio(1, 255));
            SetRgbQRaw(offset + 7, 0L);
        }

        private bool TryGetFreeIngressSlot(out IngressSlot result)
        {
            if (_ingressSlots == null)
            {
                result = null;
                return false;
            }
            for (int index = 0; index < _ingressSlots.Length; ++index)
            {
                if (_ingressSlots[index].InFlight)
                    continue;
                result = _ingressSlots[index];
                return true;
            }
            result = null;
            return false;
        }

        private int CountCompletionTickets()
        {
            int count = 0;
            if (_ingressSlots != null)
            {
                for (int index = 0; index < _ingressSlots.Length; ++index)
                    if (_ingressSlots[index].InFlight)
                        count++;
            }
            return count;
        }

        private void TrackLast(SigmaGpuCompletionTicket ticket)
        {
            _lastCompletion = ticket;
            _hasLastCompletion = true;
        }

        private uint NextRevision()
        {
            uint revision = _nextRevision++;
            if (revision == 0u || _nextRevision == 0u)
                throw new OverflowException("Sigma world revision exhausted.");
            return revision;
        }

        private void SetQ(int index, double value) =>
            SetQRaw(index, SigmaNumericDomain.Quantize(value));
        private void SetQRaw(int index, long raw) =>
            _calibrationUpload[index] = SigmaPackedQ48.FromRaw(raw);
        private void SetRgbQ(int index, double value) =>
            SetRgbQRaw(index, SigmaNumericDomain.Quantize(value));
        private void SetRgbQRaw(int index, long raw) =>
            _rgbCalibrationUpload[index] = SigmaPackedQ48.FromRaw(raw);

        private static long AbsNanoseconds(long value) => value == long.MinValue
            ? long.MaxValue : Math.Abs(value);
        private static long SaturatingAdd(long left, long right) =>
            left > long.MaxValue - right ? long.MaxValue : left + right;
        private static uint IndependenceKey(GpuImageView view, uint epoch,
            Matrix4x4 worldToRoom)
        {
            unchecked
            {
                uint hash = 2166136261u;
                Mix(ref hash, epoch);
                Mix(ref hash, (uint)view.Eye + 1u);
                Pose roomPose = SigmaRoomFrame.CameraPose(worldToRoom,
                    view.WorldFromCamera);
                Vector3 p = roomPose.position;
                Quaternion q = roomPose.rotation;
                Mix(ref hash, (uint)Mathf.RoundToInt(p.x * 25f));
                Mix(ref hash, (uint)Mathf.RoundToInt(p.y * 25f));
                Mix(ref hash, (uint)Mathf.RoundToInt(p.z * 25f));
                Mix(ref hash, (uint)Mathf.RoundToInt(q.x * 64f));
                Mix(ref hash, (uint)Mathf.RoundToInt(q.y * 64f));
                Mix(ref hash, (uint)Mathf.RoundToInt(q.z * 64f));
                Mix(ref hash, (uint)Mathf.RoundToInt(q.w * 64f));
                return hash == 0u ? 1u : hash;
            }
        }

        private static void Mix(ref uint hash, uint value)
        {
            unchecked { hash = (hash ^ value) * 16777619u; }
        }

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);
        private static GraphicsBuffer CreateBuffer(int count, int stride,
            string name) => new(GraphicsBuffer.Target.Structured,
                Math.Max(1, count), stride) { name = name };

        private static RenderTexture CreateArrayTexture(string name,
            Vector2Int resolution, GraphicsFormat format)
        {
            if (!SystemInfo.IsFormatSupported(format,
                    GraphicsFormatUsage.LoadStore))
                throw new InvalidOperationException(
                    $"Required inverse texture format unsupported: {format}.");
            var descriptor = new RenderTextureDescriptor(resolution.x,
                resolution.y)
            {
                graphicsFormat = format,
                depthBufferBits = 0,
                msaaSamples = 1,
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 2,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false
            };
            var texture = new RenderTexture(descriptor)
            {
                name = $"[Sigma-PRISM-16] {name}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!texture.Create())
                throw new InvalidOperationException($"Unable to create {name}.");
            return texture;
        }

        private static void DestroyTexture(RenderTexture texture)
        {
            if (texture == null)
                return;
            texture.Release();
            if (Application.isPlaying)
                Destroy(texture);
            else
                DestroyImmediate(texture);
        }

        private void OnDestroy()
        {
            if (_disposed)
                return;
            _disposed = true;
            _running = false;
            SigmaGpuKernelTelemetry.CancelSingleSubmission();
            // The unresolved journal is part of durable HEAD. Pending N5
            // transactions remain owned by the carrier retirement gate.
            _constraintJournal.Clear();

            IngressSlot[] slots = _ingressSlots;
            _ingressSlots = null;
            RigConeLutSet coneLuts = _coneLuts;
            _coneLuts = null;
            SigmaNativeFrameGraph graph = _graph;
            _graph = null;
            _initialized = false;

            void ReleaseOwnedResources()
            {
                if (slots != null)
                    for (int index = 0; index < slots.Length; ++index)
                        slots[index]?.Dispose();
                coneLuts?.Retire();
                graph?.Dispose();
                _completionTransfer.Dispose();
            }

            if (_completionFaulted)
            {
                SigmaGpuRetirement.Quarantine(ReleaseOwnedResources,
                    "Sigma direct-frame controller resources",
                    "A completion fault left GPU ownership unproven.");
            }
            else if (_hasLastCompletion)
            {
                SigmaGpuRetirement.Retire(_lastCompletion,
                    ReleaseOwnedResources,
                    "Sigma direct-frame controller teardown");
            }
            else
                ReleaseOwnedResources();
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct SigmaPackedQ48
        {
            private SigmaPackedQ48(uint low, uint high)
            {
                Low = low;
                High = high;
            }
            public readonly uint Low;
            public readonly uint High;
            public static SigmaPackedQ48 FromRaw(long raw) => new(
                unchecked((uint)raw), unchecked((uint)(raw >> 32)));
        }

        private sealed class LatencyTracker
        {
            private long _sampleCount;
            private double _lastMs;
            private double _totalMs;
            private double _maximumMs;

            internal SigmaStageLatencyTelemetry Snapshot =>
                new(_sampleCount, _lastMs,
                    _sampleCount == 0 ? 0.0 : _totalMs / _sampleCount,
                    _maximumMs);

            internal void Add(double milliseconds)
            {
                if (double.IsNaN(milliseconds) ||
                    double.IsInfinity(milliseconds))
                    return;
                milliseconds = Math.Max(0.0, milliseconds);
                _lastMs = milliseconds;
                _totalMs += milliseconds;
                _maximumMs = Math.Max(_maximumMs, milliseconds);
                _sampleCount++;
            }

            internal void Reset()
            {
                _sampleCount = 0L;
                _lastMs = 0.0;
                _totalMs = 0.0;
                _maximumMs = 0.0;
            }
        }

        private sealed class IngressSlot : IDisposable
        {
            private SigmaPredictionFrameLease _prediction;
            private SigmaPredictionFrameLease _correctedPrediction;
            private ConeLutLease _coneLuts;
            private SigmaNativeFrameLease _ownedFrame;
            private SigmaNativeVulkanExecutor.SigmaNativeVulkanJob _nativeJob;
            private SigmaNativeCompletionTransfer.Reservation
                _completionReservation;
            private SigmaGpuCompletionTicket _ticket;
            private double _submittedAt;
            private readonly int _index;
            private Vector2Int _resolution;
            private int _posePartialCapacity;
            private uint _calibrationEpoch;
            private uint _leftKey;
            private uint _rightKey;
            private uint _rgbLeftKey;
            private uint _rgbRightKey;
            private List<IDisposable> _residencyLeases;

            internal IngressSlot(GraphicsBuffer rawDepthCalibration,
                GraphicsBuffer rawRgbCalibration,
                GraphicsBuffer correctedDepthCalibration,
                GraphicsBuffer correctedRgbCalibration,
                GraphicsBuffer posePrior, GraphicsBuffer poseResult, int index)
            {
                RawDepthCalibration = rawDepthCalibration;
                RawRgbCalibration = rawRgbCalibration;
                CorrectedDepthCalibration = correctedDepthCalibration;
                CorrectedRgbCalibration = correctedRgbCalibration;
                PosePrior = posePrior;
                PoseResult = poseResult;
                _index = index;
            }

            internal GraphicsBuffer RawDepthCalibration { get; }
            internal GraphicsBuffer RawRgbCalibration { get; }
            internal GraphicsBuffer CorrectedDepthCalibration { get; }
            internal GraphicsBuffer CorrectedRgbCalibration { get; }
            internal GraphicsBuffer PosePrior { get; }
            internal GraphicsBuffer PoseResult { get; }
            internal GraphicsBuffer PosePartials { get; private set; }
            internal RenderTexture MetricDepth { get; private set; }
            internal RenderTexture DepthFlags { get; private set; }
            internal bool InFlight { get; private set; }
            internal bool AwaitingDisposition { get; private set; }
            internal long AgeFrames { get; private set; }
            internal uint Revision { get; private set; }
            internal SigmaPredictionPageRequest[] PredictionPageRequests
            {
                get;
                private set;
            } = Array.Empty<SigmaPredictionPageRequest>();
            internal bool PredictionPageRequestOverflow { get; private set; }
            internal SigmaNativeCompletionTransfer.Reservation
                CompletionReservation => _completionReservation;

            internal void Begin(SigmaPredictionFrameLease prediction,
                SigmaPredictionFrameLease correctedPrediction,
                ConeLutLease coneLuts, SigmaNativeFrameLease ownedFrame,
                SigmaNativeCompletionTransfer.Reservation completionReservation,
                SigmaNativeVulkanExecutor.SigmaNativeVulkanJob nativeJob,
                SigmaGpuCompletionTicket ticket,
                uint revision, uint calibrationEpoch, uint leftKey,
                uint rightKey, uint rgbLeftKey, uint rgbRightKey,
                List<IDisposable> residencyLeases,
                double submittedAt)
            {
                if (InFlight)
                    throw new InvalidOperationException(
                        "Sigma ingress slot is already in flight.");
                _prediction = prediction;
                _correctedPrediction = correctedPrediction;
                _coneLuts = coneLuts;
                _ownedFrame = ownedFrame ?? throw new ArgumentNullException(
                    nameof(ownedFrame));
                _nativeJob = nativeJob ?? throw new ArgumentNullException(
                    nameof(nativeJob));
                if (!completionReservation.IsValid)
                    throw new ArgumentException(
                        "A native completion reservation is required.",
                        nameof(completionReservation));
                _completionReservation = completionReservation;
                _ticket = ticket;
                _calibrationEpoch = calibrationEpoch;
                _leftKey = leftKey;
                _rightKey = rightKey;
                _rgbLeftKey = rgbLeftKey;
                _rgbRightKey = rgbRightKey;
                _residencyLeases = residencyLeases ??
                    throw new ArgumentNullException(nameof(residencyLeases));
                _submittedAt = submittedAt;
                Revision = revision;
                AgeFrames = 0L;
                InFlight = true;
                AwaitingDisposition = false;
                PredictionPageRequests =
                    Array.Empty<SigmaPredictionPageRequest>();
                PredictionPageRequestOverflow = false;
            }

            internal SigmaNativeFrameInput ReplayInput(
                SigmaCarrierReadBatch source0,
                SigmaCarrierReadBatch source1,
                SigmaCarrierReadBatch target) => new(
                    _correctedPrediction, MetricDepth, DepthFlags,
                    CorrectedDepthCalibration, CorrectedRgbCalibration,
                    PoseResult, _coneLuts, _leftKey, _rightKey,
                    _rgbLeftKey, _rgbRightKey, source0, source1, target);

            internal uint CalibrationEpoch => _calibrationEpoch;

            internal void BeginReplay(SigmaNativeFrameLease ownedFrame,
                SigmaNativeCompletionTransfer.Reservation reservation,
                SigmaNativeVulkanExecutor.SigmaNativeVulkanJob nativeJob,
                SigmaGpuCompletionTicket ticket, uint revision,
                List<IDisposable> residencyLeases,
                double submittedAt)
            {
                if (!InFlight || !AwaitingDisposition ||
                    _ownedFrame != null || _nativeJob != null ||
                    !reservation.IsValid)
                    throw new InvalidOperationException(
                        "Capacity replay slot ownership is invalid.");
                _ownedFrame = ownedFrame ?? throw new ArgumentNullException(
                    nameof(ownedFrame));
                _completionReservation = reservation;
                _nativeJob = nativeJob ?? throw new ArgumentNullException(
                    nameof(nativeJob));
                _ticket = ticket;
                _residencyLeases = residencyLeases ??
                    throw new ArgumentNullException(nameof(residencyLeases));
                Revision = revision;
                _submittedAt = submittedAt;
                AgeFrames = 0L;
                AwaitingDisposition = false;
            }

            internal SigmaGpuCompletionStatus Poll(out string error)
                => _ticket.Poll(out error);
            internal bool TryReadCompletion(
                out SigmaFrameUInt2Gpu[] completion)
            {
                if (_nativeJob != null &&
                    _nativeJob.TryReadCompletion(out completion) &&
                    _nativeJob.TryReadPredictionPageRequests(
                        out SigmaPredictionPageRequest[] requests,
                        out bool overflow))
                {
                    PredictionPageRequests = requests;
                    PredictionPageRequestOverflow = overflow;
                    return true;
                }
                completion = null;
                return false;
            }
            internal void AdvanceAge() => AgeFrames++;
            internal double ElapsedMilliseconds(double completedAt) =>
                Math.Max(0.0, completedAt - _submittedAt) * 1000.0;

            internal void MarkNativeComplete()
            {
                if (!InFlight || AwaitingDisposition)
                    throw new InvalidOperationException(
                        "Native completion ownership is invalid.");
                _ownedFrame?.Dispose();
                _ownedFrame = null;
                _nativeJob?.Dispose();
                _nativeJob = null;
                _ticket = default;
                ReleaseResidencyLeases(_residencyLeases);
                _residencyLeases = null;
                AwaitingDisposition = true;
            }

            internal void EnsureFrameResources(Vector2Int resolution)
            {
                if (_resolution != resolution || MetricDepth == null ||
                    DepthFlags == null)
                {
                    DestroyTexture(MetricDepth);
                    DestroyTexture(DepthFlags);
                    MetricDepth = CreateArrayTexture(
                        $"Sigma ingress {_index} metric depth", resolution,
                        GraphicsFormat.R32G32_SFloat);
                    DepthFlags = CreateArrayTexture(
                        $"Sigma ingress {_index} depth flags", resolution,
                        GraphicsFormat.R32_UInt);
                    _resolution = resolution;
                }
            }

            internal void EnsurePosePartials(int partialCount)
            {
                if (PosePartials != null &&
                    _posePartialCapacity >= partialCount)
                    return;
                PosePartials?.Dispose();
                _posePartialCapacity = Math.Max(1, partialCount);
                PosePartials = CreateBuffer(
                    checked(_posePartialCapacity * 7), sizeof(uint) * 4,
                    $"Sigma ingress {_index} pose partial meets");
            }

            internal void Complete()
            {
                ReleaseTransientInputs();
                _ownedFrame?.Dispose();
                _ownedFrame = null;
                _nativeJob?.Dispose();
                _nativeJob = null;
                ReleaseResidencyLeases(_residencyLeases);
                _residencyLeases = null;
                _completionReservation = default;
                InFlight = false;
                AwaitingDisposition = false;
                PredictionPageRequests =
                    Array.Empty<SigmaPredictionPageRequest>();
                PredictionPageRequestOverflow = false;
                AgeFrames = 0L;
                _submittedAt = 0.0;
            }

            public void Dispose()
            {
                ReleaseTransientInputs();
                _ownedFrame?.Dispose();
                _ownedFrame = null;
                _nativeJob?.Dispose();
                _nativeJob = null;
                ReleaseResidencyLeases(_residencyLeases);
                _residencyLeases = null;
                _completionReservation = default;
                RawDepthCalibration?.Dispose();
                RawRgbCalibration?.Dispose();
                CorrectedDepthCalibration?.Dispose();
                CorrectedRgbCalibration?.Dispose();
                PosePrior?.Dispose();
                PoseResult?.Dispose();
                PosePartials?.Dispose();
                DestroyTexture(MetricDepth);
                DestroyTexture(DepthFlags);
                MetricDepth = null;
                DepthFlags = null;
                InFlight = false;
                AwaitingDisposition = false;
                PredictionPageRequests =
                    Array.Empty<SigmaPredictionPageRequest>();
                PredictionPageRequestOverflow = false;
                _submittedAt = 0.0;
            }

            private void ReleaseTransientInputs()
            {
                _prediction?.Dispose();
                _prediction = null;
                _correctedPrediction?.Dispose();
                _correctedPrediction = null;
                _coneLuts?.Dispose();
                _coneLuts = null;
            }
        }

        internal static SigmaFrameCompletionDisposition ClassifyFrameCompletion(
            SigmaNativeFrameGpu frame, uint publishedRoot,
            uint expectedRevision,
            out string error)
        {
            if (expectedRevision == 0u ||
                frame.Identity.X != expectedRevision)
            {
                error = $"Sigma frame disposition revision mismatch: expected " +
                    $"{expectedRevision}, received {frame.Identity.X}.";
                return SigmaFrameCompletionDisposition.Faulted;
            }
            SigmaNativeFrameDisposition state =
                (SigmaNativeFrameDisposition)frame.Disposition.X;
            // Disposition is state-tagged: W is a fault receipt only for the
            // Faulted state. A resolved/published frame carries its exact
            // certificate-delta count in the same word.
            if (state == SigmaNativeFrameDisposition.Faulted)
            {
                error = $"Sigma frame revision {expectedRevision} reported " +
                    $"publication fault mask=0x{frame.Disposition.Y:x8}, " +
                    $"reason={(SigmaNativeColdReason)frame.Disposition.W} " +
                    $"({frame.Disposition.W}).";
                return SigmaFrameCompletionDisposition.Faulted;
            }
            if (state == SigmaNativeFrameDisposition.Published)
            {
                if (publishedRoot != expectedRevision ||
                    frame.Publication.Y != expectedRevision)
                {
                    error = $"Sigma frame revision {expectedRevision} reported " +
                        "publication evidence before publication root " +
                        $"{publishedRoot}.";
                    return SigmaFrameCompletionDisposition.Faulted;
                }
                error = null;
                return SigmaFrameCompletionDisposition.Published;
            }
            if (state == SigmaNativeFrameDisposition.NoChange)
            {
                error = null;
                return SigmaFrameCompletionDisposition.NoChange;
            }
            if (state == SigmaNativeFrameDisposition.Unresolved)
            {
                error = null;
                return SigmaFrameCompletionDisposition.Unresolved;
            }

            error = $"Sigma frame revision {expectedRevision} ended at illegal " +
                $"post-fence state {state}.";
            return SigmaFrameCompletionDisposition.Faulted;
        }
    }

}
