using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    internal enum SigmaResidencyState : uint
    {
        AbsentInRoot = 0u,
        ColdDurable = 1u,
        Loading = 2u,
        HotClean = 3u,
        HotDirty = 4u,
        Evicting = 5u,
        Quarantined = 6u,
    }

    internal enum SigmaResidencyResult : uint
    {
        Applied = 1u,
        Found = 2u,
        NotFound = 3u,
        TableFull = 4u,
        Invalid = 5u,
        StateConflict = 6u,
        SlotConflict = 7u,
    }

    internal enum SigmaResidencyOperation : uint
    {
        Upsert = 1u,
        PublishLoaded = 2u,
        Evict = 3u,
        RetirePhysical = 4u,
    }

    internal readonly struct SigmaResidencyKey : IEquatable<SigmaResidencyKey>
    {
        internal SigmaResidencyKey(SigmaCarrierPageCoordinate coordinate,
            ulong rootContext, uint pageGeneration)
        {
            if (rootContext == 0UL)
                throw new ArgumentOutOfRangeException(nameof(rootContext));
            if (pageGeneration == 0u)
                throw new ArgumentOutOfRangeException(nameof(pageGeneration));
            Coordinate = coordinate;
            RootContext = rootContext;
            PageGeneration = pageGeneration;
        }

        internal SigmaCarrierPageCoordinate Coordinate { get; }
        internal ulong RootContext { get; }
        internal uint PageGeneration { get; }

        public bool Equals(SigmaResidencyKey other) =>
            Coordinate.Equals(other.Coordinate) &&
            RootContext == other.RootContext &&
            PageGeneration == other.PageGeneration;
        public override bool Equals(object obj) =>
            obj is SigmaResidencyKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Coordinate,
            RootContext, PageGeneration);
    }

    internal readonly struct SigmaResidencyUpdate
    {
        internal SigmaResidencyUpdate(SigmaResidencyKey key,
            uint residentGeneration, int segment, int slot, int globalSlot,
            SigmaResidencyState state,
            SigmaResidencyState? expectedState = null, uint flags = 0u,
            SigmaResidencyOperation operation =
                SigmaResidencyOperation.Upsert,
            int pairedGlobalSlot = -1)
        {
            bool hasSlot = SigmaCarrierResidencyAbi.StateHasSlot(state);
            bool eviction = operation == SigmaResidencyOperation.Evict;
            bool retirement = operation ==
                SigmaResidencyOperation.RetirePhysical;
            if ((hasSlot || eviction || retirement) &&
                (residentGeneration == 0u || segment < 0 ||
                slot < 0 || globalSlot < 0))
                throw new ArgumentOutOfRangeException(nameof(globalSlot));
            if (!hasSlot && !eviction && !retirement &&
                (segment >= 0 || slot >= 0 || globalSlot >= 0))
                throw new ArgumentException(
                    "A nonresident locator entry cannot own a physical slot.");
            if (pairedGlobalSlot >= 0 &&
                (!hasSlot && !eviction && !retirement ||
                pairedGlobalSlot == globalSlot))
                throw new ArgumentOutOfRangeException(
                    nameof(pairedGlobalSlot));
            if (operation == SigmaResidencyOperation.PublishLoaded &&
                (state != SigmaResidencyState.HotClean ||
                    expectedState != SigmaResidencyState.ColdDurable ||
                    pairedGlobalSlot < 0))
                throw new ArgumentException(
                    "A loaded publication is exactly COLD_DURABLE -> " +
                    "HOT_CLEAN for one reserved current/shadow pair.");
            if (eviction && (state != SigmaResidencyState.ColdDurable ||
                expectedState != SigmaResidencyState.HotClean ||
                pairedGlobalSlot < 0))
                throw new ArgumentException(
                    "Eviction is exactly HOT_CLEAN -> COLD_DURABLE for one pair.");
            if (retirement && (state != SigmaResidencyState.AbsentInRoot ||
                expectedState.HasValue || pairedGlobalSlot < 0))
                throw new ArgumentException(
                    "Physical retirement is a post-HEAD disposable pair " +
                    "cleanup, not a residency-state transition.");
            Key = key;
            ResidentGeneration = residentGeneration;
            Segment = segment;
            Slot = slot;
            GlobalSlot = globalSlot;
            State = state;
            ExpectedState = expectedState;
            Flags = flags;
            Operation = operation;
            PairedGlobalSlot = pairedGlobalSlot;
        }

        internal SigmaResidencyKey Key { get; }
        internal uint ResidentGeneration { get; }
        internal int Segment { get; }
        internal int Slot { get; }
        internal int GlobalSlot { get; }
        internal SigmaResidencyState State { get; }
        internal SigmaResidencyState? ExpectedState { get; }
        internal uint Flags { get; }
        internal SigmaResidencyOperation Operation { get; }
        internal int PairedGlobalSlot { get; }

        internal SigmaResidencyUpdateGpu ToGpu() =>
            SigmaResidencyUpdateGpu.Create(this);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SigmaResidencyUpdateGpu
    {
        internal uint PageXLo;
        internal uint PageXHi;
        internal uint PageYLo;
        internal uint PageYHi;
        internal uint RootContextLo;
        internal uint RootContextHi;
        internal uint PageGeneration;
        internal uint ResidentGeneration;
        internal uint Segment;
        internal uint Slot;
        internal uint GlobalSlot;
        internal uint State;
        internal uint Operation;
        internal uint ExpectedState;
        internal uint Flags;
        internal uint Reserved;

        internal static SigmaResidencyUpdateGpu Create(
            SigmaResidencyUpdate update)
        {
            ulong root = update.Key.RootContext;
            return new SigmaResidencyUpdateGpu
            {
                PageXLo = unchecked((uint)update.Key.Coordinate.X),
                PageXHi = unchecked((uint)(update.Key.Coordinate.X >> 32)),
                PageYLo = unchecked((uint)update.Key.Coordinate.Y),
                PageYHi = unchecked((uint)(update.Key.Coordinate.Y >> 32)),
                RootContextLo = unchecked((uint)root),
                RootContextHi = unchecked((uint)(root >> 32)),
                PageGeneration = update.Key.PageGeneration,
                ResidentGeneration = update.ResidentGeneration,
                Segment = update.Segment < 0 ? uint.MaxValue :
                    checked((uint)update.Segment),
                Slot = update.Slot < 0 ? uint.MaxValue :
                    checked((uint)update.Slot),
                GlobalSlot = update.GlobalSlot < 0 ? uint.MaxValue :
                    checked((uint)update.GlobalSlot),
                State = (uint)update.State,
                Operation = (uint)update.Operation,
                ExpectedState = update.ExpectedState.HasValue
                    ? (uint)update.ExpectedState.Value : uint.MaxValue,
                Flags = update.Flags,
                Reserved = update.PairedGlobalSlot < 0 ? uint.MaxValue :
                    checked((uint)update.PairedGlobalSlot),
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SigmaResidentSlotGpu
    {
        internal uint PageXLo;
        internal uint PageXHi;
        internal uint PageYLo;
        internal uint PageYHi;
        internal uint RootContextLo;
        internal uint RootContextHi;
        internal uint PageGeneration;
        internal uint ResidentGeneration;
        internal uint Segment;
        internal uint Slot;
        internal uint State;
        internal uint LocatorBucket;
        internal uint LastReaderTicket;
        internal uint LastWriterTicket;
        internal uint LeaseCount;
        internal uint Flags;
    }

    internal static class SigmaCarrierResidencyAbi
    {
        internal const int LocatorWords = 16;
        internal const int LocatorStride = LocatorWords * sizeof(uint);
        internal const int SlotStride = 16 * sizeof(uint);
        internal const int UpdateStride = 16 * sizeof(uint);
        internal const int ResultStride = 4 * sizeof(uint);
        internal const int ThreadsPerGroup = 64;
        internal const uint UpdateUpsert = 1u;
        internal const uint UpdatePublishLoaded = 2u;
        internal const uint UpdateEvict = 3u;
        internal const uint UpdateRetirePhysical = 4u;
        internal const int MinimumLocatorCapacity = 1024;
        internal const int LocatorEntriesPerPhysicalSlot = 8;
        internal const int MaximumColdBatch = 4096;
        private const long ProjectBufferLimit = 128L * 1024L * 1024L;

        internal static bool StateHasSlot(SigmaResidencyState state) =>
            state == SigmaResidencyState.Loading ||
            state == SigmaResidencyState.HotClean ||
            state == SigmaResidencyState.HotDirty ||
            state == SigmaResidencyState.Evicting ||
            state == SigmaResidencyState.Quarantined;

        internal static bool TransitionAllowed(SigmaResidencyState prior,
            SigmaResidencyState next)
        {
            if (prior == next || next == SigmaResidencyState.Quarantined)
                return true;
            return prior switch
            {
                SigmaResidencyState.ColdDurable =>
                    next == SigmaResidencyState.Loading,
                SigmaResidencyState.Loading =>
                    next == SigmaResidencyState.HotClean,
                SigmaResidencyState.HotClean =>
                    next == SigmaResidencyState.HotDirty ||
                    next == SigmaResidencyState.Evicting,
                SigmaResidencyState.HotDirty =>
                    next == SigmaResidencyState.HotClean,
                SigmaResidencyState.Evicting =>
                    next == SigmaResidencyState.ColdDurable ||
                    next == SigmaResidencyState.HotClean,
                _ => false,
            };
        }

        internal static int ComputeLocatorCapacity(int physicalSlotCapacity)
        {
            if (physicalSlotCapacity <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(physicalSlotCapacity));
            int required = checked(Math.Max(MinimumLocatorCapacity,
                physicalSlotCapacity * LocatorEntriesPerPhysicalSlot));
            int capacity = 1;
            while (capacity < required)
                capacity = checked(capacity << 1);
            if ((long)capacity * LocatorStride > ProjectBufferLimit)
                throw new InvalidOperationException(
                    "The sparse residency locator exceeds the 128 MiB " +
                    "Quest storage-buffer contract.");
            return capacity;
        }

        internal static uint Hash(SigmaResidencyKey key)
        {
            uint hash = 2166136261u;
            hash = HashWord(hash, unchecked((uint)key.Coordinate.X));
            hash = HashWord(hash, unchecked((uint)(key.Coordinate.X >> 32)));
            hash = HashWord(hash, unchecked((uint)key.Coordinate.Y));
            hash = HashWord(hash, unchecked((uint)(key.Coordinate.Y >> 32)));
            hash = HashWord(hash, unchecked((uint)key.RootContext));
            hash = HashWord(hash, unchecked((uint)(key.RootContext >> 32)));
            return HashWord(hash, key.PageGeneration);
        }

        private static uint HashWord(uint hash, uint word)
        {
            unchecked
            {
                hash ^= word;
                hash *= 16777619u;
                hash ^= hash >> 13;
                return hash;
            }
        }
    }

    /// <summary>
    /// Owns the only sparse GPU forward locator and the only dense physical-slot
    /// reverse table.  Update/result buffers are bounded cold staging, not page
    /// directories and not canonical authority.
    /// </summary>
    internal sealed class SigmaCarrierResidencyResources : IDisposable
    {
        private readonly ComputeShader _shader;
        private readonly SigmaExactBackendGate _backendGate;
        private readonly int _initializeKernel;
        private readonly int _applyKernel;
        private readonly int _resolveKernel;
        private GraphicsBuffer _pageMetadata;
        private GraphicsBuffer _dirtyFlags;
        private GraphicsBuffer _readoutDirtyFlags;
        private int _pageCapacity;
        private bool _disposed;

        internal SigmaCarrierResidencyResources(ComputeShader shader,
            SigmaExactBackendGate backendGate, int physicalSlotCapacity)
        {
            _shader = shader != null ? shader : throw new ArgumentNullException(
                nameof(shader));
            _backendGate = backendGate ?? throw new ArgumentNullException(
                nameof(backendGate));
            if (physicalSlotCapacity <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(physicalSlotCapacity));
            SlotCapacity = physicalSlotCapacity;
            LocatorCapacity = SigmaCarrierResidencyAbi.ComputeLocatorCapacity(
                physicalSlotCapacity);
            BatchCapacity = Math.Min(SigmaCarrierResidencyAbi.MaximumColdBatch,
                Math.Max(256, physicalSlotCapacity * 2));
            _initializeKernel = shader.FindProfiledKernel(
                "InitializeResidencyIndex");
            _applyKernel = shader.FindProfiledKernel("ApplyResidencyUpdates");
            _resolveKernel = shader.FindProfiledKernel(
                "ResolveResidencyQueries");
            Locator = new GraphicsBuffer(GraphicsBuffer.Target.Raw,
                checked(LocatorCapacity * SigmaCarrierResidencyAbi.LocatorWords),
                sizeof(uint)) { name = "Sigma sparse resident page locator" };
            SlotTable = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                SlotCapacity, SigmaCarrierResidencyAbi.SlotStride)
            { name = "Sigma dense resident slot table" };
            Updates = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                BatchCapacity, SigmaCarrierResidencyAbi.UpdateStride)
            { name = "Sigma cold residency update staging" };
            Results = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                BatchCapacity, SigmaCarrierResidencyAbi.ResultStride)
            { name = "Sigma cold residency result staging" };
            InitializeImmediate();
        }

        internal int LocatorCapacity { get; }
        internal int SlotCapacity { get; }
        internal int BatchCapacity { get; }
        internal GraphicsBuffer Locator { get; }
        internal GraphicsBuffer SlotTable { get; }
        internal GraphicsBuffer Updates { get; }
        internal GraphicsBuffer Results { get; }

        internal void ResetDisposableIndex()
        {
            ThrowIfDisposed();
            InitializeImmediate();
        }

        internal void AttachCarrierBank(SigmaCarrierReadBatch batch)
        {
            ThrowIfDisposed();
            if (batch.Metadata == null || batch.DirtyFlags == null ||
                batch.ReadoutDirtyFlags == null || batch.PageCapacity <= 0)
                throw new ArgumentException(
                    "Residency retirement requires one complete carrier bank.",
                    nameof(batch));
            _pageMetadata = batch.Metadata;
            _dirtyFlags = batch.DirtyFlags;
            _readoutDirtyFlags = batch.ReadoutDirtyFlags;
            _pageCapacity = batch.PageCapacity;
        }

        internal void UploadBatch(IReadOnlyList<SigmaResidencyUpdate> updates)
        {
            ThrowIfDisposed();
            Updates.SetData(PackBatch(updates));
        }

        internal SigmaGpuCompletionTicket RecordApply(CommandBuffer command,
            SigmaCarrierReadBatch target,
            IReadOnlyList<SigmaResidencyUpdate> updates)
        {
            ThrowIfDisposed();
            if (command == null) throw new ArgumentNullException(nameof(command));
            ValidateAttachedBank(target, updates);
            AttachCarrierBank(target);
            SigmaResidencyUpdateGpu[] packed = PackBatch(updates);
            command.SetBufferData(Updates, packed);
            command.SetComputeIntParam(_shader, "_ResidentLocatorCapacity",
                LocatorCapacity);
            command.SetComputeIntParam(_shader, "_ResidentSlotCapacity",
                SlotCapacity);
            command.SetComputeIntParam(_shader, "_ResidencyUpdateCount",
                packed.Length);
            command.SetComputeIntParam(_shader, "_PageCapacity",
                _pageCapacity);
            command.SetComputeBufferParam(_shader, _applyKernel,
                "_SigmaExactBackendGate", _backendGate.Buffer);
            command.SetComputeBufferParam(_shader, _applyKernel,
                "_ResidentPageLocator", Locator);
            command.SetComputeBufferParam(_shader, _applyKernel,
                "_ResidentSlotTable", SlotTable);
            command.SetComputeBufferParam(_shader, _applyKernel,
                "_ResidencyUpdates", Updates);
            command.SetComputeBufferParam(_shader, _applyKernel,
                "_ResidencyResults", Results);
            RequireCarrierBank();
            command.SetComputeBufferParam(_shader, _applyKernel,
                "_PageMetadata", _pageMetadata);
            command.SetComputeBufferParam(_shader, _applyKernel,
                "_DirtyFlags", _dirtyFlags);
            command.SetComputeBufferParam(_shader, _applyKernel,
                "_ReadoutDirtyFlags", _readoutDirtyFlags);
            command.DispatchCompute(_shader, _applyKernel,
                Groups(packed.Length), 1, 1);
            return SigmaGpuCompletion.RecordAfterAllWork(command);
        }

        private static void ValidateAttachedBank(
            SigmaCarrierReadBatch target,
            IReadOnlyList<SigmaResidencyUpdate> updates)
        {
            if (updates == null)
                throw new ArgumentNullException(nameof(updates));
            for (int index = 0; index < updates.Count; ++index)
            {
                SigmaResidencyUpdate update = updates[index];
                bool bankSpecific = update.Operation ==
                        SigmaResidencyOperation.PublishLoaded ||
                    update.Operation == SigmaResidencyOperation.Evict ||
                    update.Operation ==
                        SigmaResidencyOperation.RetirePhysical;
                if (!bankSpecific)
                    continue;
                if (update.Segment != target.SegmentIndex ||
                    (uint)update.Slot >= (uint)target.PageCapacity)
                    throw new InvalidOperationException(
                        "A bank-specific residency update is attached to the " +
                        "wrong decoded binding bank.");
                int global = checked(target.PairFirst * 2 + update.Slot);
                int paired = checked(target.PairFirst * 2 +
                    (update.Slot ^ 1));
                if (update.GlobalSlot != global ||
                    update.PairedGlobalSlot != paired)
                    throw new InvalidOperationException(
                        "A bank-specific residency update has an invalid " +
                        "local/global slot mapping.");
            }
        }

        private SigmaResidencyUpdateGpu[] PackBatch(
            IReadOnlyList<SigmaResidencyUpdate> updates)
        {
            ThrowIfDisposed();
            if (updates == null) throw new ArgumentNullException(nameof(updates));
            if (updates.Count == 0 || updates.Count > BatchCapacity)
                throw new ArgumentOutOfRangeException(nameof(updates));
            var keys = new HashSet<SigmaResidencyKey>();
            var physicalSlots = new HashSet<int>();
            var packed = new SigmaResidencyUpdateGpu[updates.Count];
            for (int index = 0; index < updates.Count; ++index)
            {
                SigmaResidencyUpdate update = updates[index];
                if (!keys.Add(update.Key))
                    throw new InvalidOperationException(
                        "One cold residency batch contains a duplicate full key.");
                bool ownsPair = SigmaCarrierResidencyAbi.StateHasSlot(
                        update.State) ||
                    update.Operation == SigmaResidencyOperation.Evict ||
                    update.Operation ==
                        SigmaResidencyOperation.RetirePhysical;
                if (ownsPair)
                {
                    if (update.GlobalSlot < 0 ||
                        update.PairedGlobalSlot < 0 ||
                        update.GlobalSlot >= SlotCapacity ||
                        update.PairedGlobalSlot >= SlotCapacity ||
                        !physicalSlots.Add(update.GlobalSlot) ||
                        !physicalSlots.Add(update.PairedGlobalSlot))
                        throw new InvalidOperationException(
                            "One cold residency batch aliases or exceeds a " +
                            "physical current/shadow slot pair.");
                }
                packed[index] = update.ToGpu();
            }
            return packed;
        }

        internal void DispatchApplyImmediate(int count)
        {
            Bind(_applyKernel, count);
            _shader.Dispatch(_applyKernel, Groups(count), 1, 1);
        }

        internal void DispatchResolveImmediate(int count)
        {
            Bind(_resolveKernel, count);
            _shader.Dispatch(_resolveKernel, Groups(count), 1, 1);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Locator.Dispose();
            SlotTable.Dispose();
            Updates.Dispose();
            Results.Dispose();
        }

        private void InitializeImmediate()
        {
            Bind(_initializeKernel, 0);
            _shader.Dispatch(_initializeKernel,
                Groups(Math.Max(LocatorCapacity, SlotCapacity)), 1, 1);
        }

        private void Bind(int kernel, int count)
        {
            ThrowIfDisposed();
            if (count < 0 || count > BatchCapacity)
                throw new ArgumentOutOfRangeException(nameof(count));
            _backendGate.Bind(_shader, kernel);
            _shader.SetInt("_ResidentLocatorCapacity", LocatorCapacity);
            _shader.SetInt("_ResidentSlotCapacity", SlotCapacity);
            _shader.SetInt("_ResidencyUpdateCount", count);
            _shader.SetInt("_PageCapacity", _pageCapacity);
            _shader.SetBuffer(kernel, "_ResidentPageLocator", Locator);
            _shader.SetBuffer(kernel, "_ResidentSlotTable", SlotTable);
            if (kernel != _initializeKernel)
            {
                RequireCarrierBank();
                _shader.SetBuffer(kernel, "_ResidencyUpdates", Updates);
                _shader.SetBuffer(kernel, "_ResidencyResults", Results);
                _shader.SetBuffer(kernel, "_PageMetadata", _pageMetadata);
                _shader.SetBuffer(kernel, "_DirtyFlags", _dirtyFlags);
                _shader.SetBuffer(kernel, "_ReadoutDirtyFlags",
                    _readoutDirtyFlags);
            }
        }

        private void RequireCarrierBank()
        {
            if (_pageCapacity <= 0 || _pageMetadata == null ||
                _dirtyFlags == null || _readoutDirtyFlags == null)
                throw new InvalidOperationException(
                    "The sparse locator is not attached to its decoded bank.");
        }

        private static int Groups(int count) => Math.Max(1,
            (count + SigmaCarrierResidencyAbi.ThreadsPerGroup - 1) /
            SigmaCarrierResidencyAbi.ThreadsPerGroup);

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(
                    nameof(SigmaCarrierResidencyResources));
        }
    }
}
