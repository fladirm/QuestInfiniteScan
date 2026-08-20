using System;
using System.Collections.Generic;
using System.Globalization;
using Genesis.RoomScan.Exporting;
using Genesis.RoomScan.HeavyCompute;

namespace Genesis.RoomScan.World
{
    public sealed class InfiniteScanStatus
    {
        public string Mode { get; internal set; }
        public string World { get; internal set; }
        public string ActiveChunk { get; internal set; }
        public string Lifecycle { get; internal set; }
        public string Residency { get; internal set; }
        public string Graph { get; internal set; }
        public string Queue { get; internal set; }
        public string Network { get; internal set; }
        public string Artifacts { get; internal set; }
        public string Storage { get; internal set; }
        public string Export { get; internal set; }
    }

    public static class InfiniteScanDiagnostics
    {
        public static InfiniteScanStatus Capture(SubmapManager submaps,
            ChunkRefinementScheduler scheduler, GlbExportController exporter)
        {
            WorldManifest manifest = submaps?.Manifest;
            ChunkRecord active = submaps?.ActiveChunk;
            long artifactBytes = 0;
            int refined = 0, diffSoup = 0, glb = 0;
            if (manifest?.chunks != null)
            {
                for (int i = 0; i < manifest.chunks.Count; i++)
                {
                    List<ChunkArtifactRecord> artifacts = manifest.chunks[i]?.artifacts;
                    if (artifacts == null) continue;
                    for (int j = 0; j < artifacts.Count; j++)
                    {
                        ChunkArtifactRecord artifact = artifacts[j];
                        if (artifact == null) continue;
                        artifactBytes = SaturatingAdd(artifactBytes,
                            Math.Max(0, artifact.byteLength));
                        if (artifact.kind == ChunkArtifactKind.RefinedMesh) refined++;
                        else if (artifact.kind == ChunkArtifactKind.DiffSoup) diffSoup++;
                        else if (artifact.kind == ChunkArtifactKind.Glb) glb++;
                    }
                }
            }

            int pending = 0, ready = 0, failed = 0;
            IReadOnlyList<HeavyComputeQueueItem> jobs = scheduler?.Jobs;
            if (jobs != null)
            {
                for (int i = 0; i < jobs.Count; i++)
                {
                    HeavyComputeQueueItem job = jobs[i];
                    if (job == null) continue;
                    if (job.localState == HeavyComputeLocalState.Ready) ready++;
                    else if (job.localState == HeavyComputeLocalState.Failed) failed++;
                    else if (!job.IsTerminal) pending++;
                }
            }

            bool large = submaps != null && submaps.LargeWorldMode;
            return new InfiniteScanStatus
            {
                Mode = large ? "Large world (1 reusable TSDF)" : "Single room",
                World = manifest == null ? "None" :
                    $"{manifest.worldId} · r{manifest.revision} · {manifest.chunks.Count} chunks",
                ActiveChunk = active == null ? "None" :
                    $"{active.chunkId} · r{active.revision} · {active.state}",
                Lifecycle = submaps == null ? "Not attached" :
                    $"{submaps.FinalizationStatus} · background {submaps.BackgroundPublicationCount}",
                Residency = submaps == null ? "--" :
                    $"volume {submaps.ResidentVolumeCount}/{submaps.MaximumResidentVolumeCount}, " +
                    $"mesh {submaps.ResidentPersistedMeshCount}/" +
                    $"{Math.Max(0, submaps.MaximumResidentChunkMeshCount - 1)}, " +
                    $"DiffSoup {submaps.ResidentDiffSoupCount}",
                Graph = manifest == null ? "--" :
                    $"{manifest.edges.Count} edges · {submaps.PoseGraphStatus}",
                Queue = scheduler == null ? "Not attached" :
                    $"{pending} pending · {ready} ready · {failed} failed · " +
                    $"{jobs?.Count ?? 0} total",
                Network = scheduler == null ? "Not attached" :
                    scheduler.BackendMode == HeavyComputeBackendMode.None
                        ? $"Offline-safe · profile {scheduler.Profile}"
                        : $"LAN {scheduler.ServerUrl} · " +
                          $"{(scheduler.IsPumping ? "active" : "idle")}",
                Artifacts = $"refined {refined} · DiffSoup {diffSoup} · GLB {glb}",
                Storage = FormatBytes(artifactBytes) + " declared artifacts",
                Export = exporter == null ? "Not attached" : exporter.Status
            };
        }

        private static long SaturatingAdd(long left, long right) =>
            left > long.MaxValue - right ? long.MaxValue : left + right;

        internal static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024L * 1024L)
                return (bytes / 1024d).ToString("F1", CultureInfo.InvariantCulture) + " KiB";
            if (bytes < 1024L * 1024L * 1024L)
                return (bytes / (1024d * 1024d)).ToString("F1",
                    CultureInfo.InvariantCulture) + " MiB";
            return (bytes / (1024d * 1024d * 1024d)).ToString("F2",
                CultureInfo.InvariantCulture) + " GiB";
        }
    }
}
