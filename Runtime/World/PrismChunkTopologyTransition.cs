using System;
using UnityEngine;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// Immutable control-plane description of one storage-frame transition.  It
    /// does not define a physical surface boundary: it only tells the GPU where
    /// measured outer half-edges may be mirrored as topology ghosts while their
    /// neighbouring chunk is non-resident.
    /// </summary>
    internal readonly struct PrismChunkTopologyTransition
    {
        public PrismChunkTopologyTransition(uint sourceChunkId, uint targetChunkId,
            Vector4 ownershipPlaneInSource, float overlapBandMeters,
            Matrix4x4 targetFromSource, bool isRevisit)
        {
            SourceChunkId = sourceChunkId;
            TargetChunkId = targetChunkId;
            OwnershipPlaneInSource = ownershipPlaneInSource;
            OverlapBandMeters = overlapBandMeters;
            TargetFromSource = targetFromSource;
            IsRevisit = isRevisit;
        }

        public uint SourceChunkId { get; }
        public uint TargetChunkId { get; }
        public Vector4 OwnershipPlaneInSource { get; }
        public float OverlapBandMeters { get; }
        public Matrix4x4 TargetFromSource { get; }
        public bool IsRevisit { get; }
        public bool IsValid => SourceChunkId != 0u && TargetChunkId != 0u &&
            SourceChunkId != TargetChunkId && OverlapBandMeters > 0f;

        public static PrismChunkTopologyTransition FromRequest(
            SubmapRolloverRequest request, float overlapMeters)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.BoundaryAxis < 0 || request.BoundaryAxis > 2 ||
                request.BoundaryDirection == 0)
                throw new ArgumentException(
                    "Rollover request has no valid ownership boundary.",
                    nameof(request));

            Matrix4x4 sourceFromTarget = request.SourceFromTarget.ToMatrix();
            Matrix4x4 targetFromSource = sourceFromTarget.inverse;
            Vector3 normal = Vector3.zero;
            normal[request.BoundaryAxis] = request.BoundaryDirection > 0 ? 1f : -1f;
            Vector3 targetOriginInSource = sourceFromTarget.GetColumn(3);
            float planeOffset = Vector3.Dot(normal, targetOriginInSource * 0.5f);
            return new PrismChunkTopologyTransition(
                PrismChunkIdentity.ToNumericId(request.SourceChunkId),
                PrismChunkIdentity.ToNumericId(request.TargetChunkId),
                new Vector4(normal.x, normal.y, normal.z, planeOffset),
                Mathf.Max(0.05f, overlapMeters * 0.55f), targetFromSource,
                request.IsRevisit);
        }
    }
}
