using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    internal enum SigmaDurableActivity
    {
        Idle,
        Encoding,
        Validating,
        Committing,
        Complete,
        Faulted,
    }

    internal readonly struct SigmaDurablePublication
    {
        internal SigmaDurablePublication(SigmaDurableCommitResult commit,
            IReadOnlyList<SigmaStagedDurablePage> pages,
            IReadOnlyList<SigmaResidentPairAddress> retiredPairs)
        {
            Commit = commit;
            Pages = pages;
            RetiredPairs = retiredPairs;
        }

        internal SigmaDurableCommitResult Commit { get; }
        internal IReadOnlyList<SigmaStagedDurablePage> Pages { get; }
        internal IReadOnlyList<SigmaResidentPairAddress> RetiredPairs { get; }
    }

    /// <summary>
    /// One bounded durable mailbox. GPU dirty compaction/codec work is batched on
    /// queue 1; CPU work validates exact staged bytes and installs one HEAD-last
    /// COW transaction. Scanner admission remains closed until that transaction
    /// either reaches HEAD or faults while XR continues to present FRONT.
    /// </summary>
    internal sealed class SigmaCarrierPersistence : IDisposable
    {
        private readonly SigmaDurableStore _store;
        private readonly SigmaExactBackendGate _exactGate;
        private readonly SigmaCarrierReadBatch[] _sources;
        private readonly Dictionary<long, SigmaStagedDurablePage>
            _residentByPair = new();
        private readonly List<SigmaStagedDurablePage> _staged = new();
        private readonly List<SigmaResidentPairAddress> _retiredPairs = new();
        private SigmaNativeVulkanColdEncode.ColdEncodeJob _encode;
        private Task<ValidationBatch> _validation;
        private Task<SigmaDurableCommitResult> _commit;
        private SigmaColdEncodedBatch _encodedBatch;
        private SigmaDurablePublication _publication;
        private byte[] _certificateManifest;
        private byte[] _unresolvedFrontier;
        private ulong _revision;
        private int _sourceIndex;
        private uint _dirtySkip;
        private long _startedTimestamp;
        private long _encodeStartedTimestamp;
        private long _commitStartedTimestamp;
        private double _encodeWallMilliseconds;
        private double _validationCpuMilliseconds;
        private string _fault;
        private bool _disposed;

        internal SigmaCarrierPersistence(SigmaDurableStore store,
            SigmaExactBackendGate exactGate, SigmaCarrierReadBatch source) :
            this(store, exactGate, new[] { source }) { }

        internal SigmaCarrierPersistence(SigmaDurableStore store,
            SigmaExactBackendGate exactGate,
            IReadOnlyList<SigmaCarrierReadBatch> sources)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _exactGate = exactGate ?? throw new ArgumentNullException(
                nameof(exactGate));
            if (sources == null || sources.Count == 0)
                throw new ArgumentOutOfRangeException(nameof(sources));
            _sources = new SigmaCarrierReadBatch[sources.Count];
            int pageCapacity = sources[0].PageCapacity;
            int firstPair = sources[0].PairFirst;
            int pairCount = 0;
            for (int index = 0; index < sources.Count; ++index)
            {
                SigmaCarrierReadBatch source = sources[index];
                if (source.SegmentIndex != index ||
                    source.PageCapacity != pageCapacity ||
                    source.PairFirst != checked(firstPair + pairCount) ||
                    source.State == null ||
                    source.Representation == null ||
                    source.Metadata == null ||
                    source.DirtyFlags == null ||
                    source.PublicationRoot != sources[0].PublicationRoot ||
                    source.ResidentLocator != sources[0].ResidentLocator ||
                    source.ResidentSlotTable !=
                        sources[0].ResidentSlotTable ||
                    !ReferenceEquals(source.RuntimeState,
                        sources[0].RuntimeState))
                    throw new ArgumentException(
                        "Durable resident banks must be equal, ordered, and " +
                        "share one root/locator authority.", nameof(sources));
                _sources[index] = source;
                pairCount = checked(pairCount + source.PairCount);
            }
        }

        internal SigmaDurableActivity Activity { get; private set; }
        internal bool IsBusy => Activity == SigmaDurableActivity.Encoding ||
            Activity == SigmaDurableActivity.Validating ||
            Activity == SigmaDurableActivity.Committing;
        internal string Fault => _fault;
        internal SigmaDurableStore Store => _store;
        internal IReadOnlyCollection<SigmaStagedDurablePage> ResidentSnapshot =>
            _residentByPair.Values;

        internal void ForgetResidentPair(int pairIndex) =>
            ForgetResidentPair(0, pairIndex);

        internal void ForgetResidentPair(int segmentIndex, int pairIndex)
        {
            ThrowIfDisposed();
            if (IsBusy)
                throw new InvalidOperationException(
                    "A resident pair cannot retire during durable staging.");
            if ((uint)segmentIndex >= (uint)_sources.Length ||
                pairIndex < 0 || pairIndex >=
                    _sources[segmentIndex].PairCount)
                throw new ArgumentOutOfRangeException(nameof(pairIndex));
            _residentByPair.Remove(PhysicalPairKey(segmentIndex, pairIndex));
        }

        internal void RememberResidentPage(SigmaStagedDurablePage page)
        {
            ThrowIfDisposed();
            if (IsBusy)
                throw new InvalidOperationException(
                    "A resident page cannot enter the snapshot during durable " +
                    "staging.");
            int pairIndex = page.VisibleSlot >> 1;
            if ((uint)page.SegmentIndex >= (uint)_sources.Length ||
                pairIndex < 0 || pairIndex >=
                    _sources[page.SegmentIndex].PairCount)
                throw new ArgumentOutOfRangeException(nameof(page));
            SigmaDurablePageUpdate update = page.Update;
            if (!_store.TryGetPageRecord(update.Coordinate,
                    out SigmaDurablePageRecord record) ||
                record.Revision != update.Revision ||
                record.PageGeneration != update.PageGeneration)
                throw new InvalidDataException(
                    "A rehydrated resident generation is not reachable from " +
                    "durable HEAD.");
            RemoveSupersededCoordinate(page, retirePhysical: false);
            _residentByPair[PhysicalPairKey(page.SegmentIndex, pairIndex)] =
                page;
        }

        internal void Begin(ulong publishedRevision,
            SigmaExactConstraintRecord certificateReceipt,
            byte[] unresolvedFrontier)
        {
            ThrowIfDisposed();
            if (publishedRevision == 0UL)
                throw new ArgumentOutOfRangeException(
                    nameof(publishedRevision));
            if (Activity != SigmaDurableActivity.Idle)
                throw new InvalidOperationException(
                    "The bounded durable mailbox already owns a revision.");
            _revision = publishedRevision;
            _certificateManifest = certificateReceipt?.CanonicalBytes();
            _unresolvedFrontier = unresolvedFrontier == null ? null :
                (byte[])unresolvedFrontier.Clone();
            _dirtySkip = 0u;
            _sourceIndex = 0;
            _staged.Clear();
            _retiredPairs.Clear();
            _publication = default;
            _fault = null;
            _startedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            _encodeStartedTimestamp = 0L;
            _commitStartedTimestamp = 0L;
            _encodeWallMilliseconds = 0.0;
            _validationCpuMilliseconds = 0.0;
            StartEncodeBatch();
        }

        internal void Poll()
        {
            ThrowIfDisposed();
            try
            {
                switch (Activity)
                {
                    case SigmaDurableActivity.Encoding:
                        PollEncode();
                        break;
                    case SigmaDurableActivity.Validating:
                        PollValidation();
                        break;
                    case SigmaDurableActivity.Committing:
                        PollCommit();
                        break;
                }
            }
            catch (Exception exception)
            {
                _encode?.Dispose();
                _encode = null;
                _fault = exception.Message;
                Activity = SigmaDurableActivity.Faulted;
            }
        }

        internal bool TryTakePublication(out SigmaDurablePublication result)
        {
            ThrowIfDisposed();
            if (Activity != SigmaDurableActivity.Complete)
            {
                result = default;
                return false;
            }
            result = _publication;
            _publication = default;
            _certificateManifest = null;
            _unresolvedFrontier = null;
            _encodedBatch = null;
            _validation = null;
            _commit = null;
            _revision = 0UL;
            _dirtySkip = 0u;
            _sourceIndex = 0;
            _staged.Clear();
            _retiredPairs.Clear();
            Activity = SigmaDurableActivity.Idle;
            return true;
        }

        private void StartEncodeBatch()
        {
            _encodeStartedTimestamp =
                System.Diagnostics.Stopwatch.GetTimestamp();
            SigmaCarrierReadBatch source = _sources[_sourceIndex];
            _encode = SigmaNativeVulkanColdEncode.Create(_exactGate, source,
                _dirtySkip);
            CommandBuffer command = CommandBufferPool.Get(
                "Sigma N5 COLD_DURABLE_ENCODE");
            bool recorded = false;
            try
            {
                _encode.Record(command);
                recorded = true;
                Graphics.ExecuteCommandBuffer(command);
                Activity = SigmaDurableActivity.Encoding;
            }
            catch
            {
                if (!recorded)
                    _encode.Dispose();
                throw;
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }

        private void PollEncode()
        {
            SigmaGpuCompletionStatus status = _encode.Poll(out string error);
            if (status == SigmaGpuCompletionStatus.Pending)
                return;
            if (status == SigmaGpuCompletionStatus.Faulted)
                throw new InvalidOperationException(error ??
                    "Cold durable encoder faulted.");
            long encodeCompletedTimestamp =
                System.Diagnostics.Stopwatch.GetTimestamp();
            _encodeWallMilliseconds += ElapsedMilliseconds(
                _encodeStartedTimestamp, encodeCompletedTimestamp);
            _encodeStartedTimestamp = 0L;
            _encodedBatch = _encode.TakeResult();
            _encode.Dispose();
            _encode = null;
            if (_encodedBatch.DirtySkip != _dirtySkip ||
                _encodedBatch.PageCapacity !=
                    _sources[_sourceIndex].PageCapacity)
                throw new InvalidDataException(
                    "Cold durable receipt does not match its source batch.");
            if (_encodedBatch.Count == 0)
            {
                if (_encodedBatch.TotalDirty != _dirtySkip)
                    throw new InvalidDataException(
                        "Dirty compaction made no bounded progress.");
                AdvanceSourceOrCommit();
                return;
            }
            SigmaColdEncodedBatch batch = _encodedBatch;
            _validation = Task.Run(() =>
            {
                long begin = System.Diagnostics.Stopwatch.GetTimestamp();
                IReadOnlyList<SigmaStagedDurablePage> pages =
                    batch.BuildPageUpdates(
                        _sources[_sourceIndex].SegmentIndex);
                long end = System.Diagnostics.Stopwatch.GetTimestamp();
                return new ValidationBatch(pages,
                    ElapsedMilliseconds(begin, end));
            });
            Activity = SigmaDurableActivity.Validating;
        }

        private void PollValidation()
        {
            if (!_validation.IsCompleted)
                return;
            if (_validation.IsCanceled || _validation.IsFaulted)
                throw _validation.Exception?.GetBaseException() ??
                    new InvalidOperationException(
                        "Durable staged-byte validation was cancelled.");
            ValidationBatch validated = _validation.Result;
            IReadOnlyList<SigmaStagedDurablePage> pages = validated.Pages;
            _validationCpuMilliseconds += validated.CpuMilliseconds;
            if (pages.Count != _encodedBatch.Count)
                throw new InvalidDataException(
                    "Durable validation dropped a staged dirty page.");
            for (int index = 0; index < pages.Count; ++index)
            {
                SigmaStagedDurablePage page = pages[index];
                int pair = page.VisibleSlot >> 1;
                RemoveSupersededCoordinate(page, retirePhysical: true);
                _staged.Add(page);
                _residentByPair[PhysicalPairKey(page.SegmentIndex, pair)] =
                    page;
            }
            _dirtySkip = checked(_dirtySkip + (uint)pages.Count);
            uint total = _encodedBatch.TotalDirty;
            _validation = null;
            _encodedBatch = null;
            if (_dirtySkip < total)
                StartEncodeBatch();
            else if (_dirtySkip == total)
                AdvanceSourceOrCommit();
            else
                throw new InvalidDataException(
                    "Dirty durable batches exceeded the compacted count.");
        }

        private void BeginCommit()
        {
            var updates = new SigmaDurablePageUpdate[_staged.Count];
            for (int index = 0; index < updates.Length; ++index)
                updates[index] = _staged[index].Update;
            var request = new SigmaDurableCommitRequest(_revision, updates,
                _certificateManifest, _unresolvedFrontier);
            _commitStartedTimestamp =
                System.Diagnostics.Stopwatch.GetTimestamp();
            _commit = _store.CommitAsync(request);
            Activity = SigmaDurableActivity.Committing;
        }

        private void PollCommit()
        {
            if (!_commit.IsCompleted)
                return;
            if (_commit.IsCanceled || _commit.IsFaulted)
                throw _commit.Exception?.GetBaseException() ??
                    new IOException("Durable HEAD transaction was cancelled.");
            // No NativeClose can overlap this mailbox. Clearing the complete
            // tiny dirty bitmap after HEAD therefore cannot erase newer work.
            foreach (SigmaCarrierReadBatch source in _sources)
                source.DirtyFlags.SetData(new uint[source.PageCapacity]);
            _publication = new SigmaDurablePublication(_commit.Result,
                _staged.ToArray(), _retiredPairs.ToArray());
            uint extent = _sources[0].RuntimeState.DurableLogicalExtent;
            for (int index = 0; index < _staged.Count; ++index)
            {
                SigmaDurablePageUpdate page = _staged[index].Update;
                if (page.Coordinate.Y != 0L || page.Coordinate.X < 0L ||
                    (ulong)page.Coordinate.X > uint.MaxValue /
                        (uint)SigmaCarrier.SamplesPerPage)
                    throw new InvalidDataException(
                        "A durable native page has an invalid logical key.");
                ulong candidate = (ulong)page.Coordinate.X *
                    (uint)SigmaCarrier.SamplesPerPage +
                    page.ActiveSampleCount;
                if (candidate > uint.MaxValue)
                    throw new InvalidDataException(
                        "The durable logical extent exceeds its uint ABI.");
                extent = Math.Max(extent, (uint)candidate);
            }
            _sources[0].RuntimeState.DurableLogicalExtent = extent;
            _sources[0].RuntimeState.DurableRevision =
                _commit.Result.RootObject.Revision;
            long completedTimestamp =
                System.Diagnostics.Stopwatch.GetTimestamp();
            double stageMilliseconds = ElapsedMilliseconds(
                _startedTimestamp, _commitStartedTimestamp);
            double stageOtherMilliseconds = Math.Max(0.0,
                stageMilliseconds - _encodeWallMilliseconds -
                _validationCpuMilliseconds);
            double commitMilliseconds = ElapsedMilliseconds(
                _commitStartedTimestamp, completedTimestamp);
            double totalMilliseconds = ElapsedMilliseconds(
                _startedTimestamp, completedTimestamp);
            Logger.Info("Sigma N5 durable HEAD: revision=" +
                _commit.Result.RootObject.Revision + " pages=" +
                _staged.Count + " blobs=" +
                _commit.Result.Statistics.PageBlobs + " cow=" +
                _commit.Result.Statistics.PageMapNodes + "/" +
                _commit.Result.Statistics.SupportMapNodes + " bytes=" +
                _commit.Result.Statistics.BytesWritten + " stageMs=" +
                stageMilliseconds.ToString("F3") + " encodeWallMs=" +
                _encodeWallMilliseconds.ToString("F3") + " validateCpuMs=" +
                _validationCpuMilliseconds.ToString("F3") + " stageOtherMs=" +
                stageOtherMilliseconds.ToString("F3") + " commitMs=" +
                commitMilliseconds.ToString("F3") + " pageObjectMs=" +
                _commit.Result.Statistics.PageObjectMilliseconds.ToString("F3") +
                " cowMs=" +
                _commit.Result.Statistics.CowMilliseconds.ToString("F3") +
                " otherObjectMs=" +
                _commit.Result.Statistics.OtherObjectMilliseconds.ToString("F3") +
                " objectFlushMs=" +
                _commit.Result.Statistics.ObjectFlushMilliseconds.ToString("F3") +
                " headPublishMs=" +
                _commit.Result.Statistics.HeadPublishMilliseconds.ToString("F3") +
                " totalMs=" +
                totalMilliseconds.ToString("F3") + ".");
            Activity = SigmaDurableActivity.Complete;
        }

        private void AdvanceSourceOrCommit()
        {
            _dirtySkip = 0u;
            _sourceIndex++;
            if (_sourceIndex < _sources.Length)
                StartEncodeBatch();
            else
                BeginCommit();
        }

        private void RemoveSupersededCoordinate(SigmaStagedDurablePage page,
            bool retirePhysical)
        {
            long currentKey = PhysicalPairKey(page.SegmentIndex,
                page.VisibleSlot >> 1);
            var remove = new List<long>();
            foreach (KeyValuePair<long, SigmaStagedDurablePage> entry in
                _residentByPair)
            {
                if (entry.Key == currentKey ||
                    !entry.Value.Update.Coordinate.Equals(
                        page.Update.Coordinate))
                    continue;
                if (!retirePhysical)
                    throw new InvalidOperationException(
                        "A cold rehydrate attempted to duplicate one logical " +
                        "page across physical binding banks.");
                remove.Add(entry.Key);
                SigmaStagedDurablePage stale = entry.Value;
                var address = new SigmaResidentPairAddress(
                    stale.SegmentIndex, stale.VisibleSlot >> 1,
                    new SigmaResidencyKey(stale.Update.Coordinate,
                        stale.Update.Revision,
                        stale.Update.PageGeneration));
                if (!_retiredPairs.Contains(address))
                    _retiredPairs.Add(address);
            }
            foreach (long key in remove)
                _residentByPair.Remove(key);
        }

        private static long PhysicalPairKey(int segmentIndex, int pairIndex) =>
            ((long)segmentIndex << 32) | unchecked((uint)pairIndex);

        private static double ElapsedMilliseconds(long begin, long end)
        {
            if (begin == 0L || end < begin)
                return 0.0;
            return (end - begin) * 1000.0 /
                System.Diagnostics.Stopwatch.Frequency;
        }

        private readonly struct ValidationBatch
        {
            internal ValidationBatch(
                IReadOnlyList<SigmaStagedDurablePage> pages,
                double cpuMilliseconds)
            {
                Pages = pages ?? throw new ArgumentNullException(nameof(pages));
                CpuMilliseconds = cpuMilliseconds;
            }

            internal IReadOnlyList<SigmaStagedDurablePage> Pages { get; }
            internal double CpuMilliseconds { get; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            // An interrupted transaction never advances HEAD before all objects
            // are installed. Its prior durable root therefore remains valid.
            _disposed = true;
            _encode?.Dispose();
            _encode = null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(
                    nameof(SigmaCarrierPersistence));
        }
    }
}
