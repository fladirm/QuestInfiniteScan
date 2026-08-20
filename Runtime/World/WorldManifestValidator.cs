using System;
using System.Collections.Generic;

namespace Genesis.RoomScan.World
{
    public sealed class WorldValidationResult
    {
        private const int MaximumReportedErrors = 128;
        private readonly List<string> _errors = new();
        private bool _truncated;

        public bool IsValid => _errors.Count == 0 && !_truncated;
        public IReadOnlyList<string> Errors => _errors;

        internal void Add(string path, string message)
        {
            if (_errors.Count < MaximumReportedErrors)
                _errors.Add($"{path}: {message}");
            else
                _truncated = true;
        }

        public override string ToString()
        {
            if (IsValid)
                return "valid";
            string joined = string.Join("; ", _errors);
            return _truncated ? joined + "; additional errors omitted" : joined;
        }
    }

    /// <summary>
    /// Fail-closed validator for untrusted manifests. Validation occurs before a
    /// document is allowed to drive allocations, paths, graph updates, or rendering.
    /// </summary>
    public static class WorldManifestValidator
    {
        private const float MaximumCoordinateMagnitude = 1_000_000f;
        private const float MaximumBoundsExtent = 10_000f;
        private const float QuaternionNormTolerance = 0.01f;

        public static WorldValidationResult Validate(WorldManifest manifest)
        {
            var result = new WorldValidationResult();
            if (manifest == null)
            {
                result.Add("$", "manifest is null");
                return result;
            }

            if (manifest.schemaVersion != WorldSchema.CurrentVersion)
                result.Add("schemaVersion", $"unsupported version {manifest.schemaVersion}");

            ValidateIdentifier(result, "worldId", manifest.worldId, 96, false);
            ValidateText(result, "displayName", manifest.displayName, 256);
            ValidateIdentifier(result, "worldAnchorId", manifest.worldAnchorId, 128, true);
            ValidateRevision(result, "revision", manifest.revision);
            ValidateTimestampPair(result, "$", manifest.createdUnixMilliseconds,
                manifest.updatedUnixMilliseconds);

            var chunkIds = new HashSet<string>(StringComparer.Ordinal);
            if (manifest.chunks == null)
            {
                result.Add("chunks", "array is required");
            }
            else
            {
                if (manifest.chunks.Count > WorldSchema.MaximumChunks)
                    result.Add("chunks", $"count exceeds {WorldSchema.MaximumChunks}");

                int count = Math.Min(manifest.chunks.Count, WorldSchema.MaximumChunks);
                for (int i = 0; i < count; i++)
                {
                    ChunkRecord chunk = manifest.chunks[i];
                    string path = $"chunks[{i}]";
                    if (chunk == null)
                    {
                        result.Add(path, "entry is null");
                        continue;
                    }

                    ValidateIdentifier(result, path + ".chunkId", chunk.chunkId, 64, false);
                    if (!string.IsNullOrEmpty(chunk.chunkId) && !chunkIds.Add(chunk.chunkId))
                        result.Add(path + ".chunkId", "duplicate identifier");
                    ValidateRevision(result, path + ".revision", chunk.revision);
                    if (!Enum.IsDefined(typeof(ChunkLifecycleState), chunk.state))
                        result.Add(path + ".state", "unknown lifecycle state");
                    ValidatePose(result, path + ".worldFromChunk", chunk.worldFromChunk);
                    ValidateBounds(result, path + ".localBounds", chunk.localBounds);
                    ValidateIdentifier(result, path + ".anchorId", chunk.anchorId, 128, true);
                    ValidateTimestampPair(result, path, chunk.createdUnixMilliseconds,
                        chunk.updatedUnixMilliseconds);
                    if (manifest.createdUnixMilliseconds > 0 &&
                        chunk.createdUnixMilliseconds > 0 &&
                        chunk.createdUnixMilliseconds < manifest.createdUnixMilliseconds)
                        result.Add(path + ".createdUnixMilliseconds",
                            "cannot precede world creation");
                    if (manifest.updatedUnixMilliseconds > 0 &&
                        chunk.updatedUnixMilliseconds > manifest.updatedUnixMilliseconds)
                        result.Add(path + ".updatedUnixMilliseconds",
                            "cannot exceed world update timestamp");
                    if (!IsFinite(chunk.quality) || chunk.quality < 0f || chunk.quality > 1f)
                        result.Add(path + ".quality", "must be finite and in [0, 1]");
                    ValidateArtifacts(result, path, chunk);
                }
            }

            ValidateEdges(result, manifest.edges, chunkIds, manifest.updatedUnixMilliseconds);
            return result;
        }

        private static void ValidateArtifacts(WorldValidationResult result, string chunkPath,
            ChunkRecord chunk)
        {
            string path = chunkPath + ".artifacts";
            if (chunk.artifacts == null)
            {
                result.Add(path, "array is required");
                return;
            }
            if (chunk.artifacts.Count > WorldSchema.MaximumArtifactsPerChunk)
                result.Add(path, $"count exceeds {WorldSchema.MaximumArtifactsPerChunk}");

            var kinds = new HashSet<ChunkArtifactKind>();
            int count = Math.Min(chunk.artifacts.Count, WorldSchema.MaximumArtifactsPerChunk);
            for (int i = 0; i < count; i++)
            {
                ChunkArtifactRecord artifact = chunk.artifacts[i];
                string itemPath = $"{path}[{i}]";
                if (artifact == null)
                {
                    result.Add(itemPath, "entry is null");
                    continue;
                }
                if (!Enum.IsDefined(typeof(ChunkArtifactKind), artifact.kind) ||
                    artifact.kind == ChunkArtifactKind.Unknown)
                    result.Add(itemPath + ".kind", "unknown artifact kind");
                else if (!kinds.Add(artifact.kind))
                    result.Add(itemPath + ".kind", "duplicate artifact kind");
                if (artifact.formatVersion <= 0)
                    result.Add(itemPath + ".formatVersion", "must be positive");
                ValidateRevision(result, itemPath + ".chunkRevision", artifact.chunkRevision);
                if (artifact.chunkRevision > chunk.revision)
                    result.Add(itemPath + ".chunkRevision", "cannot exceed chunk revision");
                ValidateRelativePath(result, itemPath + ".relativePath", artifact.relativePath);
                ValidateSha256(result, itemPath + ".sha256", artifact.sha256);
                if (artifact.byteLength < 0 || artifact.byteLength > WorldSchema.MaximumArtifactBytes)
                    result.Add(itemPath + ".byteLength",
                        $"must be in [0, {WorldSchema.MaximumArtifactBytes}]");
            }
        }

        private static void ValidateEdges(WorldValidationResult result,
            List<PoseGraphEdgeRecord> edges, HashSet<string> chunkIds,
            long worldUpdatedUnixMilliseconds)
        {
            if (edges == null)
            {
                result.Add("edges", "array is required");
                return;
            }
            if (edges.Count > WorldSchema.MaximumEdges)
                result.Add("edges", $"count exceeds {WorldSchema.MaximumEdges}");

            var edgeIds = new HashSet<string>(StringComparer.Ordinal);
            int count = Math.Min(edges.Count, WorldSchema.MaximumEdges);
            for (int i = 0; i < count; i++)
            {
                PoseGraphEdgeRecord edge = edges[i];
                string path = $"edges[{i}]";
                if (edge == null)
                {
                    result.Add(path, "entry is null");
                    continue;
                }

                ValidateIdentifier(result, path + ".edgeId", edge.edgeId, 96, false);
                if (!string.IsNullOrEmpty(edge.edgeId) && !edgeIds.Add(edge.edgeId))
                    result.Add(path + ".edgeId", "duplicate identifier");
                ValidateIdentifier(result, path + ".sourceChunkId", edge.sourceChunkId, 64, false);
                ValidateIdentifier(result, path + ".targetChunkId", edge.targetChunkId, 64, false);
                if (edge.sourceChunkId == edge.targetChunkId)
                    result.Add(path, "self edge is not allowed");
                if (!chunkIds.Contains(edge.sourceChunkId))
                    result.Add(path + ".sourceChunkId", "does not reference a chunk");
                if (!chunkIds.Contains(edge.targetChunkId))
                    result.Add(path + ".targetChunkId", "does not reference a chunk");
                if (!Enum.IsDefined(typeof(PoseGraphConstraintKind), edge.kind))
                    result.Add(path + ".kind", "unknown constraint kind");
                ValidatePose(result, path + ".sourceFromTarget", edge.sourceFromTarget);
                if (!IsFinite(edge.confidence) || edge.confidence < 0f || edge.confidence > 1f)
                    result.Add(path + ".confidence", "must be finite and in [0, 1]");
                ValidateCovariance(result, path + ".covarianceDiagonal", edge.covarianceDiagonal);
                if (edge.observedUnixMilliseconds < 0)
                    result.Add(path + ".observedUnixMilliseconds", "cannot be negative");
                else if (edge.observedUnixMilliseconds > 0 &&
                    worldUpdatedUnixMilliseconds > 0 &&
                    edge.observedUnixMilliseconds > worldUpdatedUnixMilliseconds)
                    result.Add(path + ".observedUnixMilliseconds",
                        "cannot exceed world update timestamp");
                ValidateText(result, path + ".provenance", edge.provenance, 256);
            }
        }

        private static void ValidateCovariance(WorldValidationResult result, string path,
            float[] covariance)
        {
            if (covariance == null)
            {
                result.Add(path, "array is required (empty or six diagonal values)");
                return;
            }
            if (covariance.Length != 0 && covariance.Length != 6)
            {
                result.Add(path, "must be empty or contain six diagonal values");
                return;
            }
            for (int i = 0; i < covariance.Length; i++)
            {
                if (!IsFinite(covariance[i]) || covariance[i] < 0f)
                    result.Add($"{path}[{i}]", "must be finite and non-negative");
            }
        }

        private static void ValidatePose(WorldValidationResult result, string path,
            RigidPoseData pose)
        {
            if (!IsFinite(pose.position.x) || !IsFinite(pose.position.y) ||
                !IsFinite(pose.position.z))
            {
                result.Add(path + ".position", "components must be finite");
            }
            else if (Math.Abs(pose.position.x) > MaximumCoordinateMagnitude ||
                Math.Abs(pose.position.y) > MaximumCoordinateMagnitude ||
                Math.Abs(pose.position.z) > MaximumCoordinateMagnitude)
            {
                result.Add(path + ".position", "component magnitude exceeds world limit");
            }

            float norm = pose.rotation.x * pose.rotation.x + pose.rotation.y * pose.rotation.y +
                pose.rotation.z * pose.rotation.z + pose.rotation.w * pose.rotation.w;
            if (!IsFinite(norm) || Math.Abs(norm - 1f) > QuaternionNormTolerance)
                result.Add(path + ".rotation", "must be a finite unit quaternion");
        }

        private static void ValidateBounds(WorldValidationResult result, string path,
            BoundsData bounds)
        {
            if (!IsFinite(bounds.center.x) || !IsFinite(bounds.center.y) ||
                !IsFinite(bounds.center.z))
                result.Add(path + ".center", "components must be finite");
            if (!IsFinite(bounds.extents.x) || !IsFinite(bounds.extents.y) ||
                !IsFinite(bounds.extents.z) || bounds.extents.x < 0f ||
                bounds.extents.y < 0f || bounds.extents.z < 0f)
                result.Add(path + ".extents", "components must be finite and non-negative");
            else if (bounds.extents.x > MaximumBoundsExtent ||
                bounds.extents.y > MaximumBoundsExtent || bounds.extents.z > MaximumBoundsExtent)
                result.Add(path + ".extents", "component exceeds chunk bounds limit");
        }

        private static void ValidateTimestampPair(WorldValidationResult result, string path,
            long created, long updated)
        {
            if (created < 0)
                result.Add(path + ".createdUnixMilliseconds", "cannot be negative");
            if (updated < 0)
                result.Add(path + ".updatedUnixMilliseconds", "cannot be negative");
            if (created > 0 && updated > 0 && updated < created)
                result.Add(path + ".updatedUnixMilliseconds", "cannot precede creation");
        }

        private static void ValidateRevision(WorldValidationResult result, string path, int revision)
        {
            if (revision < 0)
                result.Add(path, "cannot be negative");
        }

        private static void ValidateIdentifier(WorldValidationResult result, string path,
            string value, int maximumLength, bool allowEmpty)
        {
            if (value == null)
            {
                result.Add(path, "cannot be null");
                return;
            }
            if (value.Length == 0)
            {
                if (!allowEmpty)
                    result.Add(path, "cannot be empty");
                return;
            }
            if (value.Length > maximumLength)
            {
                result.Add(path, $"length exceeds {maximumLength}");
                return;
            }
            if (!IsAsciiAlphaNumeric(value[0]) || !IsAsciiAlphaNumeric(value[value.Length - 1]) ||
                value.Contains(".."))
            {
                result.Add(path,
                    "must start/end with an alphanumeric character and cannot contain '..'");
                return;
            }
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool valid = c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' ||
                    c >= '0' && c <= '9' || c == '-' || c == '_' || c == '.';
                if (!valid)
                {
                    result.Add(path, "contains a character outside [A-Za-z0-9._-]");
                    return;
                }
            }
        }

        private static void ValidateText(WorldValidationResult result, string path,
            string value, int maximumLength)
        {
            if (value == null)
            {
                result.Add(path, "cannot be null");
                return;
            }
            if (value.Length > maximumLength)
            {
                result.Add(path, $"length exceeds {maximumLength}");
                return;
            }
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i]))
                {
                    result.Add(path, "contains a control character");
                    return;
                }
            }
        }

        private static void ValidateRelativePath(WorldValidationResult result, string path,
            string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                result.Add(path, "cannot be empty");
                return;
            }
            if (value.Length > 512 || value[0] == '/' || value.Contains('\\') ||
                value.Contains(':') || value.Contains("//"))
            {
                result.Add(path, "must be a normalized forward-slash relative path");
                return;
            }
            string[] segments = value.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i].Length == 0 || segments[i] == "." || segments[i] == "..")
                {
                    result.Add(path, "contains an unsafe path segment");
                    return;
                }
                for (int j = 0; j < segments[i].Length; j++)
                {
                    if (char.IsControl(segments[i][j]))
                    {
                        result.Add(path, "contains a control character");
                        return;
                    }
                }
            }
        }

        private static void ValidateSha256(WorldValidationResult result, string path, string value)
        {
            if (value == null || value.Length != 64)
            {
                result.Add(path, "must contain 64 hexadecimal characters");
                return;
            }
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') &&
                    !(c >= 'A' && c <= 'F'))
                {
                    result.Add(path, "must contain only hexadecimal characters");
                    return;
                }
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsAsciiAlphaNumeric(char value)
        {
            return value >= 'a' && value <= 'z' || value >= 'A' && value <= 'Z' ||
                value >= '0' && value <= '9';
        }
    }
}
