using System;
using System.Collections.Generic;
using System.Globalization;
using Genesis.RoomScan.Exporting;

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
            GlbExportController exporter)
        {
            WorldManifest manifest = submaps?.Manifest;
            ChunkRecord active = submaps?.ActiveChunk;
            long artifactBytes = 0;
            int refined = 0, glb = 0;
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
                        else if (artifact.kind == ChunkArtifactKind.Glb) glb++;
                    }
                }
            }

            bool large = submaps != null && submaps.LargeWorldMode;
            return new InfiniteScanStatus
            {
                Mode = large ? "Cone-PRISM infinite world" : "Not attached",
                World = manifest == null ? "None" :
                    $"{manifest.worldId} · r{manifest.revision} · {manifest.chunks.Count} chunks",
                ActiveChunk = active == null ? "None" :
                    $"{active.chunkId} · r{active.revision} · {active.state}",
                Lifecycle = submaps == null ? "Not attached" :
                    $"{submaps.FinalizationStatus} · background {submaps.BackgroundPublicationCount}",
                Residency = submaps == null ? "--" :
                    $"canonical chunks {submaps.ResidentChunkCount}",
                Graph = manifest == null ? "--" :
                    $"{manifest.edges.Count} edges",
                Queue = "GPU work graph",
                Network = "Pure Quest / offline",
                Artifacts = $"refined {refined} · GLB {glb}",
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
