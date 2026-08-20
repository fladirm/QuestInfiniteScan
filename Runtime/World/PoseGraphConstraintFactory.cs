using System;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// Fail-closed construction boundary for newly observed pose-graph constraints.
    /// Schema-v1 can still read older edges with an empty covariance, but every edge
    /// produced by the infinite-world runtime carries explicit uncertainty and origin.
    /// </summary>
    public static class PoseGraphConstraintFactory
    {
        public static bool TryCreate(string edgeId, string sourceChunkId,
            string targetChunkId, PoseGraphConstraintKind kind,
            RigidPoseData sourceFromTarget, float confidence,
            float[] covarianceDiagonal, long observedUnixMilliseconds,
            string provenance, out PoseGraphEdgeRecord edge, out string error)
        {
            edge = null;
            error = null;
            if (!StoragePath.IsSafeIdentifier(edgeId, 96) ||
                !StoragePath.IsSafeIdentifier(sourceChunkId, 64) ||
                !StoragePath.IsSafeIdentifier(targetChunkId, 64) ||
                string.Equals(sourceChunkId, targetChunkId, StringComparison.Ordinal))
            {
                error = "Constraint identifiers are invalid or form a self edge.";
                return false;
            }
            if (!Enum.IsDefined(typeof(PoseGraphConstraintKind), kind))
            {
                error = "Constraint kind is invalid.";
                return false;
            }
            if (!IsFinite(confidence) || confidence <= 0f || confidence > 1f)
            {
                error = "Constraint confidence must be finite and in (0, 1].";
                return false;
            }
            if (covarianceDiagonal == null || covarianceDiagonal.Length != 6)
            {
                error = "A new constraint requires six diagonal covariance values.";
                return false;
            }
            var covarianceCopy = new float[6];
            for (int i = 0; i < covarianceCopy.Length; i++)
            {
                float value = covarianceDiagonal[i];
                if (!IsFinite(value) || value <= 0f)
                {
                    error = "Constraint covariance values must be finite and positive.";
                    return false;
                }
                covarianceCopy[i] = value;
            }
            if (observedUnixMilliseconds < 0)
            {
                error = "Constraint observation timestamp cannot be negative.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(provenance) || provenance.Length > 256)
            {
                error = "Constraint provenance is required and limited to 256 characters.";
                return false;
            }
            for (int i = 0; i < provenance.Length; i++)
            {
                if (char.IsControl(provenance[i]))
                {
                    error = "Constraint provenance cannot contain control characters.";
                    return false;
                }
            }

            var candidate = new PoseGraphEdgeRecord
            {
                edgeId = edgeId,
                sourceChunkId = sourceChunkId,
                targetChunkId = targetChunkId,
                kind = kind,
                sourceFromTarget = sourceFromTarget,
                confidence = confidence,
                covarianceDiagonal = covarianceCopy,
                observedUnixMilliseconds = observedUnixMilliseconds,
                provenance = provenance
            };
            if (!IsFinitePose(candidate.sourceFromTarget))
            {
                error = "Constraint transform must be a finite rigid pose.";
                return false;
            }

            edge = candidate;
            return true;
        }

        public static bool TryCreateFromEstimate(string edgeId,
            OverlapRegistrationRequest request, OverlapConstraintEstimate estimate,
            out PoseGraphEdgeRecord edge, out string error)
        {
            edge = null;
            error = null;
            if (request == null || estimate == null || !estimate.Succeeded)
            {
                error = estimate == null || string.IsNullOrEmpty(estimate.FailureReason)
                    ? "Overlap registration did not produce a constraint."
                    : estimate.FailureReason;
                return false;
            }
            return TryCreate(edgeId, request.SourceChunkId, request.TargetChunkId,
                PoseGraphConstraintKind.Icp, estimate.SourceFromTarget,
                estimate.Confidence, estimate.CovarianceDiagonal,
                request.ObservedUnixMilliseconds, estimate.Provenance,
                out edge, out error);
        }

        private static bool IsFinitePose(RigidPoseData pose)
        {
            if (!IsFinite(pose.position.x) || !IsFinite(pose.position.y) ||
                !IsFinite(pose.position.z))
                return false;
            float norm = pose.rotation.x * pose.rotation.x +
                         pose.rotation.y * pose.rotation.y +
                         pose.rotation.z * pose.rotation.z +
                         pose.rotation.w * pose.rotation.w;
            return IsFinite(norm) && Math.Abs(norm - 1f) <= 0.01f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
