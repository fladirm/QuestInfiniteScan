using System;
using System.Collections.Generic;
using UnityEngine;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// Stable limits and version numbers shared by the world persistence and LAN contracts.
    /// Raising a limit or changing a serialized meaning requires a schema review.
    /// </summary>
    public static class WorldSchema
    {
        public const int CurrentVersion = 1;
        public const int MaximumJsonCharacters = 8 * 1024 * 1024;
        public const int MaximumChunks = 100_000;
        public const int MaximumEdges = 400_000;
        public const int MaximumArtifactsPerChunk = 32;
        public const long MaximumArtifactBytes = 16L * 1024 * 1024 * 1024;
    }

    /// <summary>
    /// Versioned root document for one unbounded scan world. All poses use the
    /// convention <c>destinationFromSource</c> and algebraic composition order.
    /// Unity vectors/quaternions retain Unity's coordinate basis at this boundary.
    /// </summary>
    [Serializable]
    public sealed class WorldManifest
    {
        public int schemaVersion = WorldSchema.CurrentVersion;
        public string worldId = string.Empty;
        public string displayName = string.Empty;
        public long createdUnixMilliseconds;
        public long updatedUnixMilliseconds;
        public int revision;
        public string worldAnchorId = string.Empty;
        public List<ChunkRecord> chunks = new();
        public List<PoseGraphEdgeRecord> edges = new();
    }

    public enum ChunkLifecycleState
    {
        New = 0,
        Active = 1,
        Finalizing = 2,
        Persisted = 3,
        Cached = 4,
        Failed = 5
    }

    [Serializable]
    public sealed class ChunkRecord
    {
        public string chunkId = string.Empty;
        public int revision;
        public ChunkLifecycleState state = ChunkLifecycleState.New;
        public RigidPoseData worldFromChunk = RigidPoseData.Identity;
        public BoundsData localBounds = BoundsData.Empty;
        public string anchorId = string.Empty;
        public long createdUnixMilliseconds;
        public long updatedUnixMilliseconds;
        public float quality;
        public List<ChunkArtifactRecord> artifacts = new();
    }

    public enum ChunkArtifactKind
    {
        Unknown = 0,
        Volume = 1,
        Keyframes = 2,
        LiveMesh = 3,
        RefinedMesh = 4,
        RefinedAtlas = 5,
        RefinedNormal = 6,
        DiffSoup = 7,
        Glb = 8
    }

    [Serializable]
    public sealed class ChunkArtifactRecord
    {
        public ChunkArtifactKind kind;
        public int formatVersion = 1;
        public int chunkRevision;
        public string relativePath = string.Empty;
        public string sha256 = string.Empty;
        public long byteLength;
    }

    public enum PoseGraphConstraintKind
    {
        Tracking = 0,
        Overlap = 1,
        Icp = 2,
        LoopClosure = 3,
        Anchor = 4
    }

    /// <summary>
    /// Relative constraint whose pose maps target-local coordinates into
    /// source-local coordinates. Therefore:
    /// <c>worldFromTarget = worldFromSource * sourceFromTarget</c>.
    /// </summary>
    [Serializable]
    public sealed class PoseGraphEdgeRecord
    {
        public string edgeId = string.Empty;
        public string sourceChunkId = string.Empty;
        public string targetChunkId = string.Empty;
        public PoseGraphConstraintKind kind = PoseGraphConstraintKind.Tracking;
        public RigidPoseData sourceFromTarget = RigidPoseData.Identity;
        public float confidence = 1f;
        public float[] covarianceDiagonal = Array.Empty<float>();
        public long observedUnixMilliseconds;
        public string provenance = string.Empty;
    }

    [Serializable]
    public struct BoundsData : IEquatable<BoundsData>
    {
        public Vector3 center;
        public Vector3 extents;

        public BoundsData(Vector3 center, Vector3 extents)
        {
            this.center = center;
            this.extents = extents;
        }

        public static BoundsData Empty => new(Vector3.zero, Vector3.zero);

        public Bounds ToUnityBounds()
        {
            return new Bounds(center, extents * 2f);
        }

        public bool Equals(BoundsData other)
        {
            return center == other.center && extents == other.extents;
        }

        public override bool Equals(object obj)
        {
            return obj is BoundsData other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked { return center.GetHashCode() * 397 ^ extents.GetHashCode(); }
        }
    }

    /// <summary>
    /// Serializable rigid transform without scale. The name of the containing
    /// field defines direction (for example, <c>worldFromChunk</c>).
    /// </summary>
    [Serializable]
    public struct RigidPoseData : IEquatable<RigidPoseData>
    {
        public Vector3 position;
        public Quaternion rotation;

        public RigidPoseData(Vector3 position, Quaternion rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }

        public static RigidPoseData Identity => new(Vector3.zero, Quaternion.identity);

        public Matrix4x4 ToMatrix()
        {
            return Matrix4x4.TRS(position, rotation, Vector3.one);
        }

        public Vector3 TransformPoint(Vector3 point)
        {
            return position + rotation * point;
        }

        public RigidPoseData Inverse()
        {
            Quaternion inverseRotation = Quaternion.Inverse(rotation);
            return new RigidPoseData(inverseRotation * -position, inverseRotation);
        }

        public static RigidPoseData operator *(RigidPoseData destinationFromMiddle,
            RigidPoseData middleFromSource)
        {
            return new RigidPoseData(
                destinationFromMiddle.TransformPoint(middleFromSource.position),
                destinationFromMiddle.rotation * middleFromSource.rotation);
        }

        public bool Equals(RigidPoseData other)
        {
            return position == other.position && rotation == other.rotation;
        }

        public override bool Equals(object obj)
        {
            return obj is RigidPoseData other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked { return position.GetHashCode() * 397 ^ rotation.GetHashCode(); }
        }
    }
}
