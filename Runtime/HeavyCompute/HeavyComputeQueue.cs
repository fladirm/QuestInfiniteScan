using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.World;
using UnityEngine;

namespace Genesis.RoomScan.HeavyCompute
{
    public enum HeavyComputeLocalState
    {
        PendingCreate = 0,
        PendingUpload = 1,
        PendingEnqueue = 2,
        Polling = 3,
        PendingDownload = 4,
        Ready = 5,
        CancelPending = 6,
        Canceled = 7,
        Failed = 8,
        Superseded = 9
    }

    [Serializable]
    public sealed class HeavyComputeQueueItem
    {
        public HeavyComputeSubmission submission;
        public HeavyComputeLocalState localState;
        public string inputRelativePath;
        public string artifactRelativePath;
        public HeavyComputeBlobDescriptor artifactBundle;
        public string remoteState;
        public float progress;
        public int retryCount;
        public long nextAttemptUnixMs;
        public long createdUnixMs;
        public long updatedUnixMs;
        public string errorCode;
        public string message;

        public string JobId => submission?.jobId;
        public bool IsTerminal => localState == HeavyComputeLocalState.Ready ||
                                  localState == HeavyComputeLocalState.Canceled ||
                                  localState == HeavyComputeLocalState.Failed ||
                                  localState == HeavyComputeLocalState.Superseded;
    }

    [Serializable]
    internal sealed class HeavyComputeQueueDocument
    {
        public int schemaVersion = 1;
        public List<HeavyComputeQueueItem> jobs = new();
    }

    /// <summary>
    /// Atomic local source of truth. Network state is never required to resume a job:
    /// each next operation is idempotent and persisted only after it succeeds.
    /// </summary>
    public sealed class HeavyComputeQueueStore
    {
        public const int SchemaVersion = 1;
        public const int MaximumJobs = 100_000;
        private const string QueueFileName = "queue.json";
        private const string QueueBackupFileName = "queue.json.bak";

        private readonly object _gate = new();
        private readonly string _root;
        private HeavyComputeQueueDocument _document;

        public HeavyComputeQueueStore(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("Queue root cannot be empty.", nameof(root));
            _root = Path.GetFullPath(root);
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(Path.Combine(_root, "inputs"));
            Directory.CreateDirectory(Path.Combine(_root, "artifacts"));
            if (!TryLoad(out string error))
                throw new InvalidDataException(error);
        }

        public string RootDirectory => _root;
        public string GetInputPath(string jobId) => GetOwnedPath("inputs", jobId, ".zip");
        public string GetArtifactPath(string jobId) =>
            GetOwnedPath("artifacts", jobId, ".diffsoup.zip");

        public IReadOnlyList<HeavyComputeQueueItem> Snapshot()
        {
            lock (_gate)
                return CloneDocument(_document).jobs.AsReadOnly();
        }

        public bool TryEnqueue(HeavyComputeSubmission submission, string inputPath,
            long unixMilliseconds, out HeavyComputeQueueItem item, out string error)
        {
            item = null;
            error = null;
            if (!HeavyComputeContract.TryValidateSubmission(submission, true, out error) ||
                unixMilliseconds < 0)
                return false;
            string expectedPath;
            try { expectedPath = GetInputPath(submission.jobId); }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            if (!PathsEqual(expectedPath, inputPath) || !File.Exists(expectedPath) ||
                new FileInfo(expectedPath).Length != submission.inputBundle.byteLength)
            {
                error = "Input bundle must be the complete queue-owned job file.";
                return false;
            }

            lock (_gate)
            {
                HeavyComputeQueueItem existing = Find(submission.jobId);
                if (existing != null)
                {
                    if (!string.Equals(existing.submission.requestFingerprint,
                            submission.requestFingerprint, StringComparison.Ordinal))
                    {
                        error = "A conflicting immutable submission already uses this job ID.";
                        return false;
                    }
                    item = CloneItem(existing);
                    return true;
                }
                if (_document.jobs.Count >= MaximumJobs)
                {
                    error = "Heavy-compute queue reached its job limit.";
                    return false;
                }
                var created = new HeavyComputeQueueItem
                {
                    submission = submission,
                    localState = HeavyComputeLocalState.PendingCreate,
                    inputRelativePath = RelativeOwnedPath(expectedPath),
                    artifactRelativePath = string.Empty,
                    remoteState = string.Empty,
                    progress = 0f,
                    nextAttemptUnixMs = unixMilliseconds,
                    createdUnixMs = unixMilliseconds,
                    updatedUnixMs = unixMilliseconds,
                    errorCode = string.Empty,
                    message = "Queued locally"
                };
                _document.jobs.Add(created);
                if (!TryPersist(out error))
                {
                    _document.jobs.Remove(created);
                    return false;
                }
                item = CloneItem(created);
                return true;
            }
        }

        public bool TryGetNextDue(long unixMilliseconds, out HeavyComputeQueueItem item)
        {
            item = null;
            lock (_gate)
            {
                HeavyComputeQueueItem selected = null;
                for (int i = 0; i < _document.jobs.Count; i++)
                {
                    HeavyComputeQueueItem candidate = _document.jobs[i];
                    if (candidate.IsTerminal || candidate.nextAttemptUnixMs > unixMilliseconds)
                        continue;
                    if (selected == null || candidate.nextAttemptUnixMs < selected.nextAttemptUnixMs ||
                        candidate.nextAttemptUnixMs == selected.nextAttemptUnixMs &&
                        candidate.createdUnixMs < selected.createdUnixMs)
                        selected = candidate;
                }
                if (selected == null)
                    return false;
                item = CloneItem(selected);
                return true;
            }
        }

        public bool TryApply(string jobId, Func<HeavyComputeQueueItem, bool> mutation,
            out HeavyComputeQueueItem updated, out string error)
        {
            updated = null;
            error = null;
            if (!HeavyComputeContract.IsLowerHexDigest(jobId) || mutation == null)
            {
                error = "Queue mutation identity is invalid.";
                return false;
            }
            lock (_gate)
            {
                HeavyComputeQueueItem current = Find(jobId);
                if (current == null)
                {
                    error = "Queued job does not exist.";
                    return false;
                }
                HeavyComputeQueueItem before = CloneItem(current);
                if (!mutation(current))
                {
                    error = "Queue mutation was rejected.";
                    return false;
                }
                if (!TryValidateItem(current, out error) || !TryPersist(out error))
                {
                    int index = _document.jobs.IndexOf(current);
                    _document.jobs[index] = before;
                    return false;
                }
                updated = CloneItem(current);
                return true;
            }
        }

        public bool TryCancel(string jobId, long unixMilliseconds, out string error)
        {
            return TryApply(jobId, item =>
            {
                if (item.IsTerminal)
                    return item.localState == HeavyComputeLocalState.Canceled;
                item.localState = HeavyComputeLocalState.CancelPending;
                item.nextAttemptUnixMs = unixMilliseconds;
                item.updatedUnixMs = Math.Max(item.updatedUnixMs, unixMilliseconds);
                item.message = "Cancellation queued";
                return true;
            }, out _, out error);
        }

        public bool TryRetry(string jobId, long unixMilliseconds, out string error)
        {
            return TryApply(jobId, item =>
            {
                if (item.localState != HeavyComputeLocalState.Failed)
                    return false;
                item.localState = HeavyComputeLocalState.PendingCreate;
                item.nextAttemptUnixMs = unixMilliseconds;
                item.updatedUnixMs = Math.Max(item.updatedUnixMs, unixMilliseconds);
                item.errorCode = string.Empty;
                item.message = "Retry queued";
                return true;
            }, out _, out error);
        }

        internal string ResolveOwnedRelativePath(string relativePath)
        {
            if (!StoragePath.IsSafeRelativePath(relativePath))
                throw new InvalidDataException("Queue path is unsafe.");
            return StoragePath.CombineContained(_root, relativePath.Split('/'));
        }

        private bool TryLoad(out string error)
        {
            lock (_gate)
            {
                string primary = Path.Combine(_root, QueueFileName);
                string backup = Path.Combine(_root, QueueBackupFileName);
                if (!File.Exists(primary) && !File.Exists(backup))
                {
                    _document = new HeavyComputeQueueDocument();
                    return TryPersist(out error);
                }
                if (TryReadDocument(primary, out _document, out error))
                    return true;
                string primaryError = error;
                if (TryReadDocument(backup, out _document, out error))
                    return true;
                error = $"No valid heavy-compute queue. Primary: {primaryError}; backup: {error}";
                return false;
            }
        }

        private bool TryReadDocument(string path, out HeavyComputeQueueDocument document,
            out string error)
        {
            document = null;
            error = null;
            try
            {
                if (!File.Exists(path))
                {
                    error = "missing";
                    return false;
                }
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > WorldSchema.MaximumJsonCharacters)
                    throw new InvalidDataException("Queue JSON size is invalid.");
                string json = File.ReadAllText(path);
                document = JsonUtility.FromJson<HeavyComputeQueueDocument>(json);
                if (!TryValidateDocument(document, out error))
                {
                    document = null;
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                document = null;
                return false;
            }
        }

        private bool TryPersist(out string error)
        {
            if (!TryValidateDocument(_document, out error))
                return false;
            string json = JsonUtility.ToJson(_document, false);
            string destination = Path.Combine(_root, QueueFileName);
            string backup = Path.Combine(_root, QueueBackupFileName);
            return AtomicUtf8File.TryWrite(destination, backup, json,
                File.Exists(destination), out error);
        }

        private bool TryValidateDocument(HeavyComputeQueueDocument document, out string error)
        {
            error = null;
            if (document == null || document.schemaVersion != SchemaVersion ||
                document.jobs == null || document.jobs.Count > MaximumJobs)
            {
                error = "Heavy-compute queue schema or size is invalid.";
                return false;
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < document.jobs.Count; i++)
            {
                if (!TryValidateItem(document.jobs[i], out error))
                    return false;
                if (!ids.Add(document.jobs[i].JobId))
                {
                    error = "Heavy-compute queue contains a duplicate job.";
                    return false;
                }
            }
            return true;
        }

        private bool TryValidateItem(HeavyComputeQueueItem item, out string error)
        {
            error = null;
            if (item == null ||
                !HeavyComputeContract.TryValidateSubmission(item.submission, true, out error) ||
                !StoragePath.IsSafeRelativePath(item.inputRelativePath) ||
                item.inputRelativePath != "inputs/" + item.JobId + ".zip" ||
                !Enum.IsDefined(typeof(HeavyComputeLocalState), item.localState) ||
                !IsFinite(item.progress) || item.progress < 0f || item.progress > 1f ||
                item.retryCount < 0 || item.nextAttemptUnixMs < 0 ||
                item.createdUnixMs < 0 || item.updatedUnixMs < item.createdUnixMs ||
                (item.message?.Length ?? 0) > 1024 || (item.errorCode?.Length ?? 0) > 64)
            {
                error ??= "Heavy-compute queue item is invalid.";
                return false;
            }
            bool ready = item.localState == HeavyComputeLocalState.Ready;
            if (ready)
            {
                if (!StoragePath.IsSafeRelativePath(item.artifactRelativePath) ||
                    item.artifactRelativePath !=
                    "artifacts/" + item.JobId + ".diffsoup.zip" ||
                    !HeavyComputeContract.TryValidateBlob(item.artifactBundle,
                        HeavyComputeProtocol.DiffSoupArtifactMediaType,
                        HeavyComputeProtocol.DiffSoupArtifactVersion,
                        HeavyComputeProtocol.MaximumArtifactBytes, out error))
                    return false;
            }
            else if (!string.IsNullOrEmpty(item.artifactRelativePath))
            {
                error = "Only a ready job may reference a local artifact.";
                return false;
            }
            return true;
        }

        private HeavyComputeQueueItem Find(string jobId)
        {
            return _document.jobs.Find(candidate =>
                string.Equals(candidate.JobId, jobId, StringComparison.Ordinal));
        }

        private string GetOwnedPath(string directory, string jobId, string suffix)
        {
            if (!HeavyComputeContract.IsLowerHexDigest(jobId))
                throw new ArgumentException("Job ID is invalid.", nameof(jobId));
            return StoragePath.CombineContained(_root, directory, jobId + suffix);
        }

        private string RelativeOwnedPath(string path)
        {
            string full = Path.GetFullPath(path);
            if (!StoragePath.IsContained(_root, full))
                throw new InvalidOperationException("Queue file escapes its root.");
            return full.Substring(_root.TrimEnd(Path.DirectorySeparatorChar).Length + 1)
                .Replace(Path.DirectorySeparatorChar, '/');
        }

        private static HeavyComputeQueueDocument CloneDocument(
            HeavyComputeQueueDocument document)
        {
            return JsonUtility.FromJson<HeavyComputeQueueDocument>(JsonUtility.ToJson(document));
        }

        private static HeavyComputeQueueItem CloneItem(HeavyComputeQueueItem item)
        {
            return JsonUtility.FromJson<HeavyComputeQueueItem>(JsonUtility.ToJson(item));
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrEmpty(right))
                return false;
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// Executes one idempotent network/disk step at a time. The caller starts PumpOnceAsync
    /// from a background module; enqueue itself never awaits or contacts the network.
    /// </summary>
    public sealed class HeavyComputeJobScheduler
    {
        private readonly HeavyComputeQueueStore _queue;
        private readonly IHeavyComputeBackend _backend;
        private readonly Func<string, string, int, bool> _isCurrentRevision;
        private readonly SemaphoreSlim _pumpGate = new(1, 1);

        public HeavyComputeJobScheduler(HeavyComputeQueueStore queue,
            IHeavyComputeBackend backend,
            Func<string, string, int, bool> isCurrentRevision)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _isCurrentRevision = isCurrentRevision ??
                throw new ArgumentNullException(nameof(isCurrentRevision));
        }

        public async Task<bool> PumpOnceAsync(long unixMilliseconds,
            CancellationToken cancellationToken = default)
        {
            if (!_backend.IsEnabled || !_queue.TryGetNextDue(unixMilliseconds, out var item))
                return false;
            if (!await _pumpGate.WaitAsync(0, cancellationToken))
                return false;
            try
            {
                if (!_isCurrentRevision(item.submission.key.worldId,
                        item.submission.key.chunkId, item.submission.key.chunkRevision))
                {
                    ApplySuperseded(item.JobId, unixMilliseconds);
                    return true;
                }
                await ExecuteStepAsync(item, unixMilliseconds, cancellationToken);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HeavyComputeBackendException exception)
            {
                ApplyFailure(item.JobId, unixMilliseconds, exception.Code,
                    exception.Message, exception.IsTransient);
                return true;
            }
            catch (Exception exception)
            {
                ApplyFailure(item.JobId, unixMilliseconds, "client_error",
                    exception.Message, false);
                return true;
            }
            finally
            {
                _pumpGate.Release();
            }
        }

        private async Task ExecuteStepAsync(HeavyComputeQueueItem item, long now,
            CancellationToken cancellationToken)
        {
            HeavyComputeJobStatus status;
            switch (item.localState)
            {
                case HeavyComputeLocalState.PendingCreate:
                    status = await _backend.CreateOrReplayAsync(item.submission,
                        cancellationToken);
                    ApplyRemoteStatus(item.JobId, status, now,
                        HeavyComputeLocalState.PendingUpload);
                    break;
                case HeavyComputeLocalState.PendingUpload:
                    string input = _queue.ResolveOwnedRelativePath(item.inputRelativePath);
                    status = await _backend.UploadInputAsync(item.submission, input,
                        cancellationToken);
                    ApplyRemoteStatus(item.JobId, status, now,
                        HeavyComputeLocalState.PendingEnqueue);
                    break;
                case HeavyComputeLocalState.PendingEnqueue:
                    status = await _backend.EnqueueAsync(item.submission, cancellationToken);
                    ApplyRemoteStatus(item.JobId, status, now,
                        HeavyComputeLocalState.Polling);
                    break;
                case HeavyComputeLocalState.Polling:
                    status = await _backend.GetStatusAsync(item.submission, cancellationToken);
                    ApplyRemoteStatus(item.JobId, status, now,
                        HeavyComputeLocalState.Polling);
                    break;
                case HeavyComputeLocalState.PendingDownload:
                    await DownloadAsync(item, now, cancellationToken);
                    break;
                case HeavyComputeLocalState.CancelPending:
                    status = await _backend.CancelAsync(item.submission, cancellationToken);
                    ApplyRemoteStatus(item.JobId, status, now,
                        HeavyComputeLocalState.CancelPending);
                    break;
            }
        }

        private async Task DownloadAsync(HeavyComputeQueueItem item, long now,
            CancellationToken cancellationToken)
        {
            if (item.artifactBundle == null)
                throw new HeavyComputeBackendException("artifact_descriptor_missing",
                    "Succeeded job has no artifact descriptor.", false);
            string finalPath = _queue.GetArtifactPath(item.JobId);
            string pendingPath = finalPath + ".pending-" + Guid.NewGuid().ToString("N");
            await _backend.DownloadArtifactAsync(item.submission, item.artifactBundle,
                pendingPath, cancellationToken);
            if (!_isCurrentRevision(item.submission.key.worldId,
                    item.submission.key.chunkId, item.submission.key.chunkRevision))
            {
                TryDeleteExact(pendingPath);
                ApplySuperseded(item.JobId, now);
                return;
            }
            if (File.Exists(finalPath))
            {
                if (new FileInfo(finalPath).Length != item.artifactBundle.byteLength ||
                    !string.Equals(Hashing.ComputeSha256(finalPath),
                        item.artifactBundle.sha256, StringComparison.Ordinal))
                    throw new HeavyComputeBackendException("artifact_conflict",
                        "A different local artifact already uses this job ID.", false);
                TryDeleteExact(pendingPath);
            }
            else
            {
                File.Move(pendingPath, finalPath);
            }
            _queue.TryApply(item.JobId, current =>
            {
                current.localState = HeavyComputeLocalState.Ready;
                current.artifactRelativePath = "artifacts/" + item.JobId +
                                               ".diffsoup.zip";
                current.progress = 1f;
                current.nextAttemptUnixMs = long.MaxValue;
                current.updatedUnixMs = Math.Max(current.updatedUnixMs, now);
                current.errorCode = string.Empty;
                current.message = "Artifact downloaded and hash-verified";
                return true;
            }, out _, out string error);
            if (!string.IsNullOrEmpty(error))
                throw new IOException(error);
        }

        private void ApplyRemoteStatus(string jobId, HeavyComputeJobStatus status,
            long now, HeavyComputeLocalState successfulDefault)
        {
            if (!_queue.TryApply(jobId, item =>
            {
                item.remoteState = status.state;
                item.progress = status.progress;
                item.updatedUnixMs = Math.Max(item.updatedUnixMs, now);
                item.message = status.message ?? string.Empty;
                item.errorCode = status.errorCode ?? string.Empty;
                item.artifactBundle = status.artifactBundle?.Clone();
                item.retryCount = 0;
                item.localState = status.RemoteState switch
                {
                    HeavyComputeRemoteState.AwaitingUpload =>
                        successfulDefault == HeavyComputeLocalState.PendingUpload
                            ? HeavyComputeLocalState.PendingUpload : successfulDefault,
                    HeavyComputeRemoteState.Queued => HeavyComputeLocalState.Polling,
                    HeavyComputeRemoteState.Running => HeavyComputeLocalState.Polling,
                    HeavyComputeRemoteState.Succeeded =>
                        HeavyComputeLocalState.PendingDownload,
                    HeavyComputeRemoteState.Failed => HeavyComputeLocalState.Failed,
                    HeavyComputeRemoteState.Canceled => HeavyComputeLocalState.Canceled,
                    _ => successfulDefault
                };
                item.nextAttemptUnixMs = item.localState switch
                {
                    HeavyComputeLocalState.Polling => checked(now + 1_000),
                    HeavyComputeLocalState.Failed => long.MaxValue,
                    HeavyComputeLocalState.Canceled => long.MaxValue,
                    _ => now
                };
                return true;
            }, out _, out string error))
                throw new IOException(error);
        }

        private void ApplyFailure(string jobId, long now, string code, string message,
            bool transient)
        {
            _queue.TryApply(jobId, item =>
            {
                item.retryCount++;
                item.updatedUnixMs = Math.Max(item.updatedUnixMs, now);
                item.errorCode = code ?? "backend_error";
                item.message = message ?? string.Empty;
                if (!transient)
                {
                    item.localState = HeavyComputeLocalState.Failed;
                    item.nextAttemptUnixMs = long.MaxValue;
                }
                else
                {
                    int exponent = Math.Min(item.retryCount - 1, 5);
                    item.nextAttemptUnixMs = checked(now + Math.Min(30_000, 1_000 << exponent));
                }
                return true;
            }, out _, out _);
        }

        private void ApplySuperseded(string jobId, long now)
        {
            _queue.TryApply(jobId, item =>
            {
                item.localState = HeavyComputeLocalState.Superseded;
                item.artifactBundle = null;
                item.artifactRelativePath = string.Empty;
                item.updatedUnixMs = Math.Max(item.updatedUnixMs, now);
                item.nextAttemptUnixMs = long.MaxValue;
                item.errorCode = "superseded_revision";
                item.message = "A newer local chunk revision exists; result cannot be promoted.";
                return true;
            }, out _, out _);
        }

        private static void TryDeleteExact(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
