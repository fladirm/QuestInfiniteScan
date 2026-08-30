using System;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Frozen 16-byte candidate metadata and deterministic attempt-local winner
    /// rank shared by the CPU contract tests and MerkabaIntegration.compute.
    /// This is not persistent M8 state.
    /// </summary>
    internal static class MerkabaSurfaceMeasurement
    {
        internal const int PixelBits = 12;
        internal const uint PixelMask = (1u << PixelBits) - 1u;
        internal const uint PackedPixelMask = (1u << (PixelBits * 2)) - 1u;
        internal const int RouteShift = 24;
        internal const int AuthorityShift = 26;
        internal const uint OffAxisBlockedFlag = 1u << 28;
        internal const uint ReplacementFlag = 1u << 29;
        internal const uint EmptyWinner = uint.MaxValue;

        internal const int AuthorityNone = 0;
        internal const int AuthorityDiscovery = 1;
        internal const int AuthoritySupport = 2;
        internal const int AuthorityRevision = 3;

        internal static uint PackPixel(int x, int y)
        {
            if ((uint)x > PixelMask || (uint)y > PixelMask)
                throw new ArgumentOutOfRangeException(
                    $"Surface source pixel ({x},{y}) exceeds 12-bit ABI.");
            return (uint)x | ((uint)y << PixelBits);
        }

        internal static int PackMetadata(int x, int y, int route,
            int authority, bool offAxisBlocked, bool replacement)
        {
            if ((uint)route > 3u) throw new ArgumentOutOfRangeException(
                nameof(route));
            if ((uint)authority > 3u) throw new ArgumentOutOfRangeException(
                nameof(authority));
            uint packed = PackPixel(x, y) |
                ((uint)route << RouteShift) |
                ((uint)authority << AuthorityShift) |
                (offAxisBlocked ? OffAxisBlockedFlag : 0u) |
                (replacement ? ReplacementFlag : 0u);
            return unchecked((int)packed);
        }

        internal static int PixelX(int metadata) =>
            (int)(unchecked((uint)metadata) & PixelMask);

        internal static int PixelY(int metadata) =>
            (int)((unchecked((uint)metadata) >> PixelBits) & PixelMask);

        internal static uint WinnerRank(int authority, float residual,
            float incidence, int pixelX, int pixelY)
        {
            uint authorityRank = authority switch
            {
                AuthorityRevision => 0u,
                AuthoritySupport => 1u,
                AuthorityDiscovery => 2u,
                _ => 3u
            };
            if (authorityRank == 3u)
                throw new ArgumentOutOfRangeException(nameof(authority));
            uint residualRank = (uint)Math.Min(15, Mathf.FloorToInt(
                Mathf.Clamp01(residual / MerkabaConstants.HalfSupport) * 16f));
            uint incidenceRank = (uint)Math.Min(3, Mathf.FloorToInt(
                (1f - Mathf.Clamp01(incidence)) * 4f));
            return (authorityRank << 30) | (residualRank << 26) |
                (incidenceRank << 24) | PackPixel(pixelX, pixelY);
        }
    }
}
