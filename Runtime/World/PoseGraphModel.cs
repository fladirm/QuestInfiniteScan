using System;
using System.Collections.Generic;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// Lightweight mutable view over manifest poses and constraints. It deliberately
    /// has no access to mesh, volume, keyframe, or texture payloads.
    /// </summary>
    public sealed class PoseGraphModel
    {
        private readonly WorldManifest _manifest;
        private readonly Dictionary<string, ChunkRecord> _chunks;

        private PoseGraphModel(WorldManifest manifest, Dictionary<string, ChunkRecord> chunks)
        {
            _manifest = manifest;
            _chunks = chunks;
        }

        public static bool TryCreate(WorldManifest manifest, out PoseGraphModel model,
            out WorldValidationResult validation)
        {
            model = null;
            validation = WorldManifestValidator.Validate(manifest);
            if (!validation.IsValid)
                return false;

            var chunks = new Dictionary<string, ChunkRecord>(manifest.chunks.Count,
                StringComparer.Ordinal);
            for (int i = 0; i < manifest.chunks.Count; i++)
                chunks.Add(manifest.chunks[i].chunkId, manifest.chunks[i]);
            model = new PoseGraphModel(manifest, chunks);
            return true;
        }

        public bool TryGetWorldFromChunk(string chunkId, out RigidPoseData worldFromChunk)
        {
            if (chunkId != null && _chunks.TryGetValue(chunkId, out ChunkRecord chunk))
            {
                worldFromChunk = chunk.worldFromChunk;
                return true;
            }
            worldFromChunk = RigidPoseData.Identity;
            return false;
        }

        /// <summary>
        /// Changes exactly one graph vertex pose and world revision. Chunk-local
        /// bounds and all artifact references remain untouched.
        /// </summary>
        public bool TrySetWorldFromChunk(string chunkId, RigidPoseData worldFromChunk,
            long updatedUnixMilliseconds)
        {
            if (chunkId == null || !_chunks.TryGetValue(chunkId, out ChunkRecord chunk))
                return false;
            if (_manifest.revision == int.MaxValue)
                return false;
            if (updatedUnixMilliseconds < 0)
                return false;

            var probe = new WorldManifest
            {
                worldId = _manifest.worldId,
                displayName = _manifest.displayName,
                createdUnixMilliseconds = _manifest.createdUnixMilliseconds,
                updatedUnixMilliseconds = Math.Max(_manifest.updatedUnixMilliseconds,
                    updatedUnixMilliseconds),
                revision = _manifest.revision,
                worldAnchorId = _manifest.worldAnchorId,
                chunks = new List<ChunkRecord>
                {
                    new()
                    {
                        chunkId = chunk.chunkId,
                        revision = chunk.revision,
                        state = chunk.state,
                        worldFromChunk = worldFromChunk,
                        localBounds = chunk.localBounds,
                        anchorId = chunk.anchorId,
                        createdUnixMilliseconds = chunk.createdUnixMilliseconds,
                        updatedUnixMilliseconds = Math.Max(chunk.updatedUnixMilliseconds,
                            updatedUnixMilliseconds),
                        quality = chunk.quality,
                        artifacts = chunk.artifacts
                    }
                },
                edges = new List<PoseGraphEdgeRecord>()
            };
            WorldValidationResult validation = WorldManifestValidator.Validate(probe);
            if (!validation.IsValid)
                return false;

            chunk.worldFromChunk = worldFromChunk;
            chunk.updatedUnixMilliseconds = probe.chunks[0].updatedUnixMilliseconds;
            _manifest.updatedUnixMilliseconds = probe.updatedUnixMilliseconds;
            _manifest.revision++;
            return true;
        }

        public bool TryPredictTargetWorldPose(PoseGraphEdgeRecord edge,
            out RigidPoseData worldFromTarget)
        {
            worldFromTarget = RigidPoseData.Identity;
            if (edge == null || !_chunks.TryGetValue(edge.sourceChunkId,
                    out ChunkRecord source) || !_chunks.ContainsKey(edge.targetChunkId))
                return false;

            worldFromTarget = source.worldFromChunk * edge.sourceFromTarget;
            return true;
        }
    }
}
