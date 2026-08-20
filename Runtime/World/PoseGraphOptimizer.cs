using System;
using System.Collections.Generic;
using UnityEngine;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// Bounded robust pose-graph relaxation for chunk transforms. It operates only on
    /// SE(3) graph vertices and never reads or mutates chunk-local geometry/artifacts.
    /// A solution is detached from the manifest until <see cref="TryApplySolution"/>
    /// performs one validated atomic metadata commit.
    /// </summary>
    public static class PoseGraphOptimizer
    {
        public static bool TryOptimize(WorldManifest manifest,
            PoseGraphOptimizationSettings settings, out PoseGraphSolution solution,
            out string error)
        {
            solution = null;
            error = null;
            WorldValidationResult validation = WorldManifestValidator.Validate(manifest);
            if (!validation.IsValid)
            {
                error = validation.ToString();
                return false;
            }
            if (settings == null || !settings.TryValidate(out error))
                return false;

            var chunks = new List<ChunkRecord>(manifest.chunks);
            chunks.Sort(CompareChunks);
            int count = chunks.Count;
            var indexById = new Dictionary<string, int>(count, StringComparer.Ordinal);
            var poses = new RigidPoseData[count];
            var original = new RigidPoseData[count];
            for (int i = 0; i < count; i++)
            {
                indexById.Add(chunks[i].chunkId, i);
                poses[i] = chunks[i].worldFromChunk;
                original[i] = poses[i];
            }
            if (!string.IsNullOrEmpty(settings.FixedChunkId) &&
                !indexById.ContainsKey(settings.FixedChunkId))
            {
                error = "The requested fixed chunk does not exist in the world.";
                return false;
            }

            List<EdgeWork> edges = BuildEdges(manifest.edges, indexById);
            bool[] fixedVertices = SelectComponentRoots(chunks, edges,
                settings.FixedChunkId);
            PoseGraphErrorMetrics initial = Evaluate(poses, edges, settings,
                out _);

            var translationDelta = new Vector3[count];
            var rotationDelta = new Vector3[count];
            var translationWeight = new double[count];
            var rotationWeight = new double[count];
            int iterations = 0;
            bool converged = edges.Count == 0;

            for (int iteration = 0; iteration < settings.MaximumIterations &&
                 !converged; iteration++)
            {
                Array.Clear(translationDelta, 0, count);
                Array.Clear(rotationDelta, 0, count);
                Array.Clear(translationWeight, 0, count);
                Array.Clear(rotationWeight, 0, count);

                for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
                {
                    EdgeWork edge = edges[edgeIndex];
                    RigidPoseData source = poses[edge.SourceIndex];
                    RigidPoseData target = poses[edge.TargetIndex];
                    EdgeResidual residual = CalculateResidual(source, target,
                        edge.SourceFromTarget);
                    float robust = RobustWeight(residual, settings);
                    if (robust <= 0f)
                        continue;

                    double translationPrecision = edge.TranslationPrecision * robust;
                    double rotationPrecision = edge.RotationPrecision * robust;
                    if (!fixedVertices[edge.TargetIndex])
                    {
                        RigidPoseData proposal = source * edge.SourceFromTarget;
                        translationDelta[edge.TargetIndex] +=
                            (proposal.position - target.position) *
                            (float)translationPrecision;
                        rotationDelta[edge.TargetIndex] += RotationVector(
                            proposal.rotation * Quaternion.Inverse(target.rotation)) *
                            (float)rotationPrecision;
                        translationWeight[edge.TargetIndex] += translationPrecision;
                        rotationWeight[edge.TargetIndex] += rotationPrecision;
                    }

                    if (!fixedVertices[edge.SourceIndex])
                    {
                        RigidPoseData proposal = target * edge.TargetFromSource;
                        translationDelta[edge.SourceIndex] +=
                            (proposal.position - source.position) *
                            (float)translationPrecision;
                        rotationDelta[edge.SourceIndex] += RotationVector(
                            proposal.rotation * Quaternion.Inverse(source.rotation)) *
                            (float)rotationPrecision;
                        translationWeight[edge.SourceIndex] += translationPrecision;
                        rotationWeight[edge.SourceIndex] += rotationPrecision;
                    }
                }

                float maximumTranslationStep = 0f;
                float maximumRotationStep = 0f;
                for (int i = 0; i < count; i++)
                {
                    if (fixedVertices[i])
                        continue;

                    Vector3 positionStep = translationWeight[i] > 0.0
                        ? translationDelta[i] / (float)translationWeight[i]
                        : Vector3.zero;
                    Vector3 rotationStep = rotationWeight[i] > 0.0
                        ? rotationDelta[i] / (float)rotationWeight[i]
                        : Vector3.zero;
                    positionStep *= settings.Relaxation;
                    rotationStep *= settings.Relaxation;
                    positionStep = ClampMagnitude(positionStep,
                        settings.MaximumTranslationStepMeters);
                    rotationStep = ClampMagnitude(rotationStep,
                        settings.MaximumRotationStepDegrees * Mathf.Deg2Rad);

                    poses[i].position += positionStep;
                    poses[i].rotation = Normalize(QuaternionFromRotationVector(
                        rotationStep) * poses[i].rotation);
                    maximumTranslationStep = Mathf.Max(maximumTranslationStep,
                        positionStep.magnitude);
                    maximumRotationStep = Mathf.Max(maximumRotationStep,
                        rotationStep.magnitude);
                }

                iterations = iteration + 1;
                converged = maximumTranslationStep <=
                            settings.TranslationConvergenceMeters &&
                            maximumRotationStep <=
                            settings.RotationConvergenceDegrees * Mathf.Deg2Rad;
            }

            PoseGraphErrorMetrics final = Evaluate(poses, edges, settings,
                out List<PoseGraphRejectedEdge> rejected);
            var updates = new List<PoseGraphPoseUpdate>(count);
            var roots = new List<string>();
            for (int i = 0; i < count; i++)
            {
                if (fixedVertices[i])
                    roots.Add(chunks[i].chunkId);
                updates.Add(new PoseGraphPoseUpdate(chunks[i].chunkId,
                    original[i], poses[i]));
            }

            solution = new PoseGraphSolution(manifest.worldId, manifest.revision,
                updates, roots, rejected, initial, final, iterations, converged);
            return true;
        }

        /// <summary>
        /// Applies a detached solution and persists it as one world revision. Any
        /// validation, stale-revision, or storage failure restores every in-memory pose
        /// and timestamp before returning false.
        /// </summary>
        public static bool TryApplySolution(WorldManifest manifest,
            PoseGraphSolution solution, WorldStore store, long unixMilliseconds,
            out string error)
        {
            error = null;
            if (manifest == null || solution == null ||
                !string.Equals(manifest.worldId, solution.WorldId,
                    StringComparison.Ordinal) ||
                manifest.revision != solution.BaseWorldRevision ||
                manifest.revision == int.MaxValue ||
                unixMilliseconds < manifest.updatedUnixMilliseconds)
            {
                error = "Pose-graph solution is stale or does not match the world.";
                return false;
            }

            var byId = new Dictionary<string, PoseGraphPoseUpdate>(
                solution.Updates.Count, StringComparer.Ordinal);
            for (int i = 0; i < solution.Updates.Count; i++)
            {
                PoseGraphPoseUpdate update = solution.Updates[i];
                if (update == null || string.IsNullOrEmpty(update.ChunkId) ||
                    byId.ContainsKey(update.ChunkId))
                {
                    error = "Pose-graph solution contains duplicate or invalid chunks.";
                    return false;
                }
                byId.Add(update.ChunkId, update);
            }
            if (byId.Count != manifest.chunks.Count)
            {
                error = "Pose-graph solution does not cover every world chunk.";
                return false;
            }

            var oldPoses = new RigidPoseData[manifest.chunks.Count];
            var oldUpdated = new long[manifest.chunks.Count];
            long oldWorldUpdated = manifest.updatedUnixMilliseconds;
            int oldRevision = manifest.revision;

            // Validate every optimistic-concurrency precondition before changing even
            // one pose. A stale entry late in the list must leave the entire in-memory
            // manifest byte-for-byte untouched.
            for (int i = 0; i < manifest.chunks.Count; i++)
            {
                ChunkRecord chunk = manifest.chunks[i];
                if (chunk == null || !byId.TryGetValue(chunk.chunkId,
                        out PoseGraphPoseUpdate update) ||
                    !PoseApproximately(chunk.worldFromChunk, update.OriginalPose))
                {
                    error = "Pose-graph solution no longer matches current chunk poses.";
                    return false;
                }
                oldPoses[i] = chunk.worldFromChunk;
                oldUpdated[i] = chunk.updatedUnixMilliseconds;
            }

            bool changed = false;
            for (int i = 0; i < manifest.chunks.Count; i++)
            {
                ChunkRecord chunk = manifest.chunks[i];
                PoseGraphPoseUpdate update = byId[chunk.chunkId];
                if (!PoseApproximately(update.OriginalPose, update.OptimizedPose))
                {
                    chunk.worldFromChunk = update.OptimizedPose;
                    chunk.updatedUnixMilliseconds = Math.Max(chunk.updatedUnixMilliseconds,
                        unixMilliseconds);
                    changed = true;
                }
            }

            if (!changed)
                return true;

            manifest.revision++;
            manifest.updatedUnixMilliseconds = unixMilliseconds;
            WorldValidationResult validation = WorldManifestValidator.Validate(manifest);
            bool committed = validation.IsValid &&
                             (store == null || store.TryCommitManifest(manifest, out error));
            if (committed)
                return true;

            for (int i = 0; i < manifest.chunks.Count; i++)
            {
                manifest.chunks[i].worldFromChunk = oldPoses[i];
                manifest.chunks[i].updatedUnixMilliseconds = oldUpdated[i];
            }
            manifest.revision = oldRevision;
            manifest.updatedUnixMilliseconds = oldWorldUpdated;
            if (!validation.IsValid)
                error = validation.ToString();
            return false;
        }

        private static List<EdgeWork> BuildEdges(List<PoseGraphEdgeRecord> records,
            Dictionary<string, int> indexById)
        {
            var sorted = new List<PoseGraphEdgeRecord>(records);
            sorted.Sort((left, right) => string.CompareOrdinal(left.edgeId, right.edgeId));
            var edges = new List<EdgeWork>(sorted.Count);
            for (int i = 0; i < sorted.Count; i++)
            {
                PoseGraphEdgeRecord edge = sorted[i];
                if (edge.confidence <= 0f)
                    continue;
                GetVariances(edge, out float translationVariance,
                    out float rotationVariance);
                edges.Add(new EdgeWork(edge,
                    indexById[edge.sourceChunkId], indexById[edge.targetChunkId],
                    edge.confidence / translationVariance,
                    edge.confidence / rotationVariance));
            }
            return edges;
        }

        private static bool[] SelectComponentRoots(List<ChunkRecord> chunks,
            List<EdgeWork> edges, string requestedFixedChunkId)
        {
            int count = chunks.Count;
            var union = new DisjointSet(count);
            for (int i = 0; i < edges.Count; i++)
                union.Union(edges[i].SourceIndex, edges[i].TargetIndex);

            var best = new Dictionary<int, int>();
            for (int i = 0; i < count; i++)
            {
                int component = union.Find(i);
                if (!best.TryGetValue(component, out int current) ||
                    CompareChunks(chunks[i], chunks[current]) < 0)
                    best[component] = i;
            }
            if (!string.IsNullOrEmpty(requestedFixedChunkId))
            {
                int requested = chunks.FindIndex(chunk => string.Equals(chunk.chunkId,
                    requestedFixedChunkId, StringComparison.Ordinal));
                if (requested >= 0)
                    best[union.Find(requested)] = requested;
            }

            var fixedVertices = new bool[count];
            foreach (int root in best.Values)
                fixedVertices[root] = true;
            return fixedVertices;
        }

        private static PoseGraphErrorMetrics Evaluate(RigidPoseData[] poses,
            List<EdgeWork> edges, PoseGraphOptimizationSettings settings,
            out List<PoseGraphRejectedEdge> rejected)
        {
            rejected = new List<PoseGraphRejectedEdge>();
            if (edges.Count == 0)
                return new PoseGraphErrorMetrics(0f, 0f, 0f, 0f, 0);

            double translationSquared = 0.0;
            double rotationSquared = 0.0;
            float maxTranslation = 0f;
            float maxRotation = 0f;
            int accepted = 0;
            for (int i = 0; i < edges.Count; i++)
            {
                EdgeWork edge = edges[i];
                EdgeResidual residual = CalculateResidual(poses[edge.SourceIndex],
                    poses[edge.TargetIndex], edge.SourceFromTarget);
                float normalized = NormalizedResidual(residual, settings);
                if (normalized > settings.OutlierCutoff)
                {
                    rejected.Add(new PoseGraphRejectedEdge(edge.EdgeId,
                        edge.Provenance, normalized));
                    continue;
                }
                accepted++;
                translationSquared += residual.TranslationMeters *
                                      residual.TranslationMeters;
                rotationSquared += residual.RotationRadians *
                                   residual.RotationRadians;
                maxTranslation = Mathf.Max(maxTranslation,
                    residual.TranslationMeters);
                maxRotation = Mathf.Max(maxRotation, residual.RotationRadians);
            }
            if (accepted == 0)
                return new PoseGraphErrorMetrics(0f, 0f, 0f, 0f, 0);
            return new PoseGraphErrorMetrics(
                (float)Math.Sqrt(translationSquared / accepted), maxTranslation,
                (float)Math.Sqrt(rotationSquared / accepted) * Mathf.Rad2Deg,
                maxRotation * Mathf.Rad2Deg, accepted);
        }

        private static EdgeResidual CalculateResidual(RigidPoseData worldFromSource,
            RigidPoseData worldFromTarget, RigidPoseData measuredSourceFromTarget)
        {
            RigidPoseData currentSourceFromTarget = worldFromSource.Inverse() *
                                                    worldFromTarget;
            RigidPoseData error = measuredSourceFromTarget.Inverse() *
                                  currentSourceFromTarget;
            return new EdgeResidual(error.position.magnitude,
                RotationVector(error.rotation).magnitude);
        }

        private static float RobustWeight(EdgeResidual residual,
            PoseGraphOptimizationSettings settings)
        {
            float normalized = NormalizedResidual(residual, settings);
            if (normalized > settings.OutlierCutoff)
                return 0f;
            return normalized <= 1f ? 1f : 1f / normalized;
        }

        private static float NormalizedResidual(EdgeResidual residual,
            PoseGraphOptimizationSettings settings)
        {
            float translation = residual.TranslationMeters /
                                settings.HuberTranslationMeters;
            float rotation = residual.RotationRadians /
                             (settings.HuberRotationDegrees * Mathf.Deg2Rad);
            return Mathf.Sqrt(translation * translation + rotation * rotation);
        }

        private static void GetVariances(PoseGraphEdgeRecord edge,
            out float translationVariance, out float rotationVariance)
        {
            float[] covariance = edge.covarianceDiagonal;
            if (covariance != null && covariance.Length == 6)
            {
                translationVariance = Mathf.Max(1e-6f,
                    (covariance[0] + covariance[1] + covariance[2]) / 3f);
                rotationVariance = Mathf.Max(1e-6f,
                    (covariance[3] + covariance[4] + covariance[5]) / 3f);
                return;
            }

            switch (edge.kind)
            {
                case PoseGraphConstraintKind.Icp:
                    translationVariance = 0.0025f;
                    rotationVariance = 0.0076f;
                    break;
                case PoseGraphConstraintKind.LoopClosure:
                    translationVariance = 0.01f;
                    rotationVariance = 0.02f;
                    break;
                case PoseGraphConstraintKind.Anchor:
                    translationVariance = 0.0004f;
                    rotationVariance = 0.001f;
                    break;
                default:
                    translationVariance = 0.02f;
                    rotationVariance = 0.04f;
                    break;
            }
        }

        private static Vector3 RotationVector(Quaternion quaternion)
        {
            Quaternion q = Normalize(quaternion);
            if (q.w < 0f)
                q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            float sinHalf = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z);
            if (sinHalf < 1e-7f)
                return new Vector3(q.x, q.y, q.z) * 2f;
            float angle = 2f * Mathf.Atan2(sinHalf, Mathf.Clamp(q.w, -1f, 1f));
            return new Vector3(q.x, q.y, q.z) * (angle / sinHalf);
        }

        private static Quaternion QuaternionFromRotationVector(Vector3 vector)
        {
            float angle = vector.magnitude;
            if (angle < 1e-7f)
                return Normalize(new Quaternion(vector.x * 0.5f, vector.y * 0.5f,
                    vector.z * 0.5f, 1f));
            float half = angle * 0.5f;
            float scale = Mathf.Sin(half) / angle;
            return new Quaternion(vector.x * scale, vector.y * scale,
                vector.z * scale, Mathf.Cos(half));
        }

        private static Quaternion Normalize(Quaternion quaternion)
        {
            float norm = Mathf.Sqrt(quaternion.x * quaternion.x +
                quaternion.y * quaternion.y + quaternion.z * quaternion.z +
                quaternion.w * quaternion.w);
            if (norm < 1e-8f || float.IsNaN(norm) || float.IsInfinity(norm))
                return Quaternion.identity;
            float inverse = 1f / norm;
            return new Quaternion(quaternion.x * inverse, quaternion.y * inverse,
                quaternion.z * inverse, quaternion.w * inverse);
        }

        private static Vector3 ClampMagnitude(Vector3 value, float maximum)
        {
            float magnitude = value.magnitude;
            return magnitude > maximum && magnitude > 0f
                ? value * (maximum / magnitude) : value;
        }

        private static bool PoseApproximately(RigidPoseData left, RigidPoseData right)
        {
            return Vector3.SqrMagnitude(left.position - right.position) <= 1e-10f &&
                   Quaternion.Angle(left.rotation, right.rotation) <= 0.001f;
        }

        private static int CompareChunks(ChunkRecord left, ChunkRecord right)
        {
            int created = left.createdUnixMilliseconds.CompareTo(
                right.createdUnixMilliseconds);
            return created != 0 ? created : string.CompareOrdinal(left.chunkId, right.chunkId);
        }

        private readonly struct EdgeResidual
        {
            internal readonly float TranslationMeters;
            internal readonly float RotationRadians;

            internal EdgeResidual(float translationMeters, float rotationRadians)
            {
                TranslationMeters = translationMeters;
                RotationRadians = rotationRadians;
            }
        }

        private sealed class EdgeWork
        {
            internal readonly string EdgeId;
            internal readonly string Provenance;
            internal readonly int SourceIndex;
            internal readonly int TargetIndex;
            internal readonly RigidPoseData SourceFromTarget;
            internal readonly RigidPoseData TargetFromSource;
            internal readonly double TranslationPrecision;
            internal readonly double RotationPrecision;

            internal EdgeWork(PoseGraphEdgeRecord edge, int sourceIndex,
                int targetIndex, double translationPrecision,
                double rotationPrecision)
            {
                EdgeId = edge.edgeId;
                Provenance = edge.provenance;
                SourceIndex = sourceIndex;
                TargetIndex = targetIndex;
                SourceFromTarget = edge.sourceFromTarget;
                TargetFromSource = edge.sourceFromTarget.Inverse();
                TranslationPrecision = translationPrecision;
                RotationPrecision = rotationPrecision;
            }
        }

        private sealed class DisjointSet
        {
            private readonly int[] _parent;
            private readonly byte[] _rank;

            internal DisjointSet(int count)
            {
                _parent = new int[count];
                _rank = new byte[count];
                for (int i = 0; i < count; i++)
                    _parent[i] = i;
            }

            internal int Find(int value)
            {
                while (_parent[value] != value)
                {
                    _parent[value] = _parent[_parent[value]];
                    value = _parent[value];
                }
                return value;
            }

            internal void Union(int left, int right)
            {
                int leftRoot = Find(left);
                int rightRoot = Find(right);
                if (leftRoot == rightRoot) return;
                if (_rank[leftRoot] < _rank[rightRoot])
                    _parent[leftRoot] = rightRoot;
                else if (_rank[leftRoot] > _rank[rightRoot])
                    _parent[rightRoot] = leftRoot;
                else
                {
                    _parent[rightRoot] = leftRoot;
                    _rank[leftRoot]++;
                }
            }
        }
    }

    public sealed class PoseGraphOptimizationSettings
    {
        public int MaximumIterations { get; set; } = 40;
        public float Relaxation { get; set; } = 0.55f;
        public float HuberTranslationMeters { get; set; } = 0.35f;
        public float HuberRotationDegrees { get; set; } = 10f;
        public float OutlierCutoff { get; set; } = 12f;
        public float MaximumTranslationStepMeters { get; set; } = 0.25f;
        public float MaximumRotationStepDegrees { get; set; } = 8f;
        public float TranslationConvergenceMeters { get; set; } = 0.0005f;
        public float RotationConvergenceDegrees { get; set; } = 0.02f;
        public string FixedChunkId { get; set; } = string.Empty;

        internal bool TryValidate(out string error)
        {
            error = null;
            if (MaximumIterations < 1 || MaximumIterations > 1000 ||
                !FiniteRange(Relaxation, 0.01f, 1f) ||
                !FiniteRange(HuberTranslationMeters, 0.001f, 100f) ||
                !FiniteRange(HuberRotationDegrees, 0.01f, 180f) ||
                !FiniteRange(OutlierCutoff, 1f, 1000f) ||
                !FiniteRange(MaximumTranslationStepMeters, 0.001f, 100f) ||
                !FiniteRange(MaximumRotationStepDegrees, 0.01f, 180f) ||
                !FiniteRange(TranslationConvergenceMeters, 1e-7f, 1f) ||
                !FiniteRange(RotationConvergenceDegrees, 1e-6f, 10f) ||
                FixedChunkId == null)
            {
                error = "Pose-graph optimization settings are invalid.";
                return false;
            }
            return true;
        }

        private static bool FiniteRange(float value, float minimum, float maximum)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                   value >= minimum && value <= maximum;
        }
    }

    public sealed class PoseGraphSolution
    {
        internal PoseGraphSolution(string worldId, int baseWorldRevision,
            IReadOnlyList<PoseGraphPoseUpdate> updates,
            IReadOnlyList<string> fixedChunkIds,
            IReadOnlyList<PoseGraphRejectedEdge> rejectedEdges,
            PoseGraphErrorMetrics initialError, PoseGraphErrorMetrics finalError,
            int iterations, bool converged)
        {
            WorldId = worldId;
            BaseWorldRevision = baseWorldRevision;
            Updates = updates;
            FixedChunkIds = fixedChunkIds;
            RejectedEdges = rejectedEdges;
            InitialError = initialError;
            FinalError = finalError;
            Iterations = iterations;
            Converged = converged;
        }

        public string WorldId { get; }
        public int BaseWorldRevision { get; }
        public IReadOnlyList<PoseGraphPoseUpdate> Updates { get; }
        public IReadOnlyList<string> FixedChunkIds { get; }
        public IReadOnlyList<PoseGraphRejectedEdge> RejectedEdges { get; }
        public PoseGraphErrorMetrics InitialError { get; }
        public PoseGraphErrorMetrics FinalError { get; }
        public int Iterations { get; }
        public bool Converged { get; }
    }

    public sealed class PoseGraphPoseUpdate
    {
        internal PoseGraphPoseUpdate(string chunkId, RigidPoseData originalPose,
            RigidPoseData optimizedPose)
        {
            ChunkId = chunkId;
            OriginalPose = originalPose;
            OptimizedPose = optimizedPose;
        }

        public string ChunkId { get; }
        public RigidPoseData OriginalPose { get; }
        public RigidPoseData OptimizedPose { get; }
    }

    public readonly struct PoseGraphErrorMetrics
    {
        internal PoseGraphErrorMetrics(float translationRmsMeters,
            float translationMaxMeters, float rotationRmsDegrees,
            float rotationMaxDegrees, int acceptedEdges)
        {
            TranslationRmsMeters = translationRmsMeters;
            TranslationMaxMeters = translationMaxMeters;
            RotationRmsDegrees = rotationRmsDegrees;
            RotationMaxDegrees = rotationMaxDegrees;
            AcceptedEdges = acceptedEdges;
        }

        public float TranslationRmsMeters { get; }
        public float TranslationMaxMeters { get; }
        public float RotationRmsDegrees { get; }
        public float RotationMaxDegrees { get; }
        public int AcceptedEdges { get; }
    }

    public sealed class PoseGraphRejectedEdge
    {
        internal PoseGraphRejectedEdge(string edgeId, string provenance,
            float normalizedResidual)
        {
            EdgeId = edgeId;
            Provenance = provenance;
            NormalizedResidual = normalizedResidual;
        }

        public string EdgeId { get; }
        public string Provenance { get; }
        public float NormalizedResidual { get; }
    }
}
