using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Genesis.RoomScan.World
{
    public sealed class PoseGraphRefinementResult
    {
        internal PoseGraphRefinementResult(bool succeeded, string error,
            PoseGraphEdgeRecord edge, PoseGraphSolution solution,
            OverlapConstraintEstimate estimate)
        {
            Succeeded = succeeded;
            Error = error ?? string.Empty;
            Edge = edge;
            Solution = solution;
            Estimate = estimate;
        }

        public bool Succeeded { get; }
        public string Error { get; }
        public PoseGraphEdgeRecord Edge { get; }
        public PoseGraphSolution Solution { get; }
        public OverlapConstraintEstimate Estimate { get; }
    }

    /// <summary>
    /// Serializes background overlap estimates, durable constraint insertion, and
    /// detached graph optimization. The caller remains responsible for refreshing
    /// render/chunk transforms after a successful metadata commit.
    /// </summary>
    public sealed class PoseGraphRefinementCoordinator : IDisposable
    {
        private readonly IOverlapConstraintEstimator _estimator;
        private readonly PoseGraphOptimizationSettings _optimizationSettings;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private bool _disposed;

        public PoseGraphRefinementCoordinator(IOverlapConstraintEstimator estimator,
            PoseGraphOptimizationSettings optimizationSettings = null)
        {
            _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));
            _optimizationSettings = optimizationSettings ??
                                    new PoseGraphOptimizationSettings();
            if (!_optimizationSettings.TryValidate(out string error))
                throw new ArgumentException(error, nameof(optimizationSettings));
        }

        public async Task<PoseGraphRefinementResult> RefineAsync(WorldManifest manifest,
            WorldStore store, OverlapRegistrationRequest request,
            long commitUnixMilliseconds, CancellationToken cancellationToken)
        {
            if (_disposed)
                return Failure("Pose-graph refinement coordinator is disposed.");
            if (manifest == null || request == null || commitUnixMilliseconds < 0)
                return Failure("Pose-graph refinement arguments are invalid.");

            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_disposed)
                    return Failure("Pose-graph refinement coordinator is disposed.");
                OverlapConstraintEstimate estimate = await _estimator.EstimateAsync(
                    request, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!estimate.Succeeded)
                    return new PoseGraphRefinementResult(false,
                        estimate.FailureReason, null, null, estimate);
                if (!ContainsChunk(manifest, request.SourceChunkId) ||
                    !ContainsChunk(manifest, request.TargetChunkId))
                {
                    return new PoseGraphRefinementResult(false,
                        "Overlap chunks no longer exist in the world.", null, null,
                        estimate);
                }

                string edgeId = NextIcpEdgeId(manifest);
                if (!PoseGraphConstraintFactory.TryCreateFromEstimate(edgeId,
                        request, estimate, out PoseGraphEdgeRecord candidate,
                        out string edgeError))
                {
                    return new PoseGraphRefinementResult(false, edgeError, null,
                        null, estimate);
                }
                long commitTime = Math.Max(commitUnixMilliseconds,
                    Math.Max(manifest.updatedUnixMilliseconds,
                        candidate.observedUnixMilliseconds));
                if (!PoseGraphConstraintCommitter.TryAppend(manifest, candidate,
                        store, commitTime, out PoseGraphEdgeRecord committedEdge,
                        out string commitError))
                {
                    return new PoseGraphRefinementResult(false, commitError, null,
                        null, estimate);
                }

                // A publication may advance the manifest between two queued overlap
                // jobs. Re-solve a bounded number of times against current metadata;
                // TryApplySolution itself performs the authoritative stale-pose check.
                const int maximumAttempts = 3;
                string lastError = null;
                for (int attempt = 0; attempt < maximumAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!PoseGraphOptimizer.TryOptimize(manifest,
                            _optimizationSettings, out PoseGraphSolution solution,
                            out lastError))
                    {
                        break;
                    }
                    long applyTime = Math.Max(commitTime,
                        manifest.updatedUnixMilliseconds);
                    if (PoseGraphOptimizer.TryApplySolution(manifest, solution,
                            store, applyTime, out lastError))
                    {
                        return new PoseGraphRefinementResult(true, string.Empty,
                            committedEdge, solution, estimate);
                    }
                }
                return new PoseGraphRefinementResult(false,
                    "Constraint was committed, but graph correction failed: " +
                    lastError, committedEdge, null, estimate);
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose()
        {
            _disposed = true;
        }

        private static PoseGraphRefinementResult Failure(string error)
        {
            return new PoseGraphRefinementResult(false, error, null, null, null);
        }

        private static bool ContainsChunk(WorldManifest manifest, string chunkId)
        {
            if (manifest?.chunks == null || chunkId == null)
                return false;
            for (int i = 0; i < manifest.chunks.Count; i++)
            {
                if (manifest.chunks[i] != null && string.Equals(
                        manifest.chunks[i].chunkId, chunkId,
                        StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static string NextIcpEdgeId(WorldManifest manifest)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < manifest.edges.Count; i++)
            {
                if (manifest.edges[i] != null)
                    used.Add(manifest.edges[i].edgeId);
            }
            int sequence = manifest.edges.Count;
            string candidate;
            do { candidate = $"icp-{sequence++:D8}"; }
            while (used.Contains(candidate));
            return candidate;
        }
    }

    /// <summary>
    /// Atomic metadata transaction for one validated graph constraint. Failure restores
    /// the exact list/revision/timestamp visible to the caller.
    /// </summary>
    public static class PoseGraphConstraintCommitter
    {
        public static bool TryAppend(WorldManifest manifest,
            PoseGraphEdgeRecord edge, WorldStore store, long unixMilliseconds,
            out PoseGraphEdgeRecord committedEdge, out string error)
        {
            committedEdge = null;
            error = null;
            if (manifest == null || edge == null || manifest.edges == null ||
                manifest.revision == int.MaxValue || unixMilliseconds < 0)
            {
                error = "Constraint commit arguments are invalid.";
                return false;
            }
            for (int i = 0; i < manifest.edges.Count; i++)
            {
                if (manifest.edges[i] != null && string.Equals(
                        manifest.edges[i].edgeId, edge.edgeId,
                        StringComparison.Ordinal))
                {
                    error = "Constraint edge identifier already exists.";
                    return false;
                }
            }
            if (!PoseGraphConstraintFactory.TryCreate(edge.edgeId,
                    edge.sourceChunkId, edge.targetChunkId, edge.kind,
                    edge.sourceFromTarget, edge.confidence,
                    edge.covarianceDiagonal, edge.observedUnixMilliseconds,
                    edge.provenance, out PoseGraphEdgeRecord detached,
                    out error))
                return false;

            int oldRevision = manifest.revision;
            long oldUpdated = manifest.updatedUnixMilliseconds;
            manifest.edges.Add(detached);
            manifest.revision++;
            manifest.updatedUnixMilliseconds = Math.Max(unixMilliseconds,
                Math.Max(oldUpdated, detached.observedUnixMilliseconds));
            WorldValidationResult validation = WorldManifestValidator.Validate(manifest);
            bool persisted = validation.IsValid &&
                             (store == null || store.TryCommitManifest(manifest,
                                 out error));
            if (persisted)
            {
                committedEdge = detached;
                return true;
            }

            manifest.edges.RemoveAt(manifest.edges.Count - 1);
            manifest.revision = oldRevision;
            manifest.updatedUnixMilliseconds = oldUpdated;
            if (!validation.IsValid)
                error = validation.ToString();
            return false;
        }
    }
}
