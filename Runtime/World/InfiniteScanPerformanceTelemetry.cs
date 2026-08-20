using System.Globalization;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// Stable, allocation-light key/value telemetry consumed by the ADB profiling harness.
    /// Values deliberately contain no localized formatting or free-form identifiers so a
    /// device capture remains machine-readable across locales and app revisions.
    /// </summary>
    internal static class InfiniteScanPerformanceTelemetry
    {
        internal const string Prefix = "QIS_WORLD_PROFILE";

        internal static string Format(long unixMilliseconds, string reason,
            int chunks, int activeRevision, ChunkLifecycleState activeState, int edges,
            int residentVolumes, int maximumResidentVolumes, int residentMeshes,
            int residentDiffSoup, int backgroundPublications, long allocatedBytes,
            long reservedBytes)
        {
            reason = reason == "start" || reason == "rollover" ||
                     reason == "periodic" || reason == "attach"
                ? reason
                : "unknown";
            return string.Format(CultureInfo.InvariantCulture,
                Prefix + " unixMs={0} reason={1} chunks={2} activeRevision={3} " +
                "activeState={4} edges={5} residentVolumes={6} " +
                "maxResidentVolumes={7} residentMeshes={8} residentDiffSoup={9} " +
                "backgroundPublications={10} allocatedBytes={11} reservedBytes={12}",
                unixMilliseconds, reason, chunks, activeRevision, (int)activeState,
                edges, residentVolumes, maximumResidentVolumes, residentMeshes,
                residentDiffSoup, backgroundPublications, allocatedBytes, reservedBytes);
        }
    }
}
