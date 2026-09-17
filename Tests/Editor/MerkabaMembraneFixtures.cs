using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    /// <summary>
    /// Measured M8 fixtures shared by the CPU membrane oracle tests and the
    /// GPU readout parity test (plan section 8). Every measured cell stores
    /// the plane dot(p, N) = constant through its own offset, so a fixture is
    /// a set of physical sheets, never a set of patches.
    /// </summary>
    internal static class MerkabaMembraneFixtures
    {
        internal static readonly Color32 SheetColor = new(80, 120, 160, 255);

        /// <summary>The unit normal the 10-bit octahedral encoding stores.</summary>
        internal static float3 Stored(float3 normal)
        {
            KernelState probe = default;
            probe.Flags = KernelState.SetSurfacePlane(0u, normal, 0f);
            KernelState.DecodeSurfacePlane(probe.Flags, out float3 decoded,
                out _);
            return decoded;
        }

        /// <summary>
        /// Adds the measured cell whose stored plane is dot(p, N) = constant
        /// (metres). An occupied coordinate keeps its first sheet; cells the
        /// offset encoding cannot reach are skipped.
        /// </summary>
        internal static bool Put(IDictionary<int3, KernelState> context,
            int3 coord, float3 normal, float planeConstant,
            Color32? color = null)
        {
            if (context.ContainsKey(coord)) return false;
            float3 stored = Stored(normal);
            float offset = planeConstant - math.dot((float3)coord *
                MerkabaConstants.LatticeStep, stored);
            if (math.abs(offset) > MerkabaConstants.SurfacePlaneOffsetRange)
                return false;
            KernelState state = default;
            state.SetOccupiedForFixture(true, color ?? SheetColor);
            state.Flags = KernelState.SetSurfacePlane(state.Flags, stored,
                offset);
            context[coord] = state;
            return true;
        }

        internal static KernelState KnownFree() => new()
        {
            OccupancyEvidence = MerkabaConstants.ExportKnownFreeThreshold
        };

        internal static float3 XzNormal(float degreesFromX) => new(
            math.cos(math.radians(degreesFromX)), 0f,
            math.sin(math.radians(degreesFromX)));

        /// <summary>
        /// A sheet through <paramref name="anchor"/> with mean normal at
        /// <paramref name="baseDegrees"/> from +X in the XZ plane, sampled as
        /// the lattice staircase z = round(-x N.x / N.z), whose measured
        /// normals alternate +-amplitude in a checkerboard over (x, y). Each
        /// cell's plane passes through the sheet point nearest its centre.
        /// </summary>
        internal static void AddNoisyDiagonal(
            IDictionary<int3, KernelState> context, int3 anchor, int halfSize,
            float baseDegrees, float amplitudeDegrees)
        {
            float3 mean = XzNormal(baseDegrees);
            float3 anchorPoint = (float3)anchor * MerkabaConstants.LatticeStep;
            for (int x = -halfSize; x <= halfSize; x++)
            for (int y = -halfSize; y <= halfSize; y++)
            {
                int z = (int)math.floor(-x * mean.x / mean.z + 0.5f);
                int3 coord = anchor + new int3(x, y, z);
                float sign = ((x + y) & 1) != 0 ? 1f : -1f;
                float3 normal = XzNormal(baseDegrees + sign * amplitudeDegrees);
                float3 centre = (float3)coord * MerkabaConstants.LatticeStep;
                float3 foot = centre - math.dot(centre - anchorPoint, mean) *
                    mean;
                Put(context, coord, normal, math.dot(foot, Stored(normal)));
            }
        }

        /// <summary>
        /// Four uses of the line through the +X+Y corner of
        /// <paramref name="origin"/> (chart Z), one per column, in layers
        /// origin.z + sign * 0..3. Consecutive uses are 12 mm apart: the
        /// component spans three layers, so it has no node (C5.2).
        /// </summary>
        internal static void AddSpanChain(IDictionary<int3, KernelState> context,
            int3 origin, int sign, bool withoutLastUse = false)
        {
            int3[] columns = { new(0, 0, 0), new(0, 1, 0), new(1, 0, 0),
                new(1, 1, 0) };
            float baseHeight = origin.z * MerkabaConstants.LatticeStep +
                sign * 0.020f;
            for (int step = 0; step < (withoutLastUse ? 3 : 4); step++)
            {
                int3 coord = origin + columns[step] + new int3(0, 0, sign * step);
                Put(context, coord, new float3(0f, 0f, 1f),
                    baseHeight + sign * step * 0.012f);
            }
        }

        /// <summary>
        /// A sheet containing +Y whose measured normal turns from 30 degrees
        /// (chart X) to 60 degrees (chart Z) between y = y0 and y0 + 1, with
        /// the upper half stepped 10 mm inwards. Two rows on each side keep
        /// the canonical charts apart; MAIN (x, y0, z) owns one C5.4
        /// transition along +Y.
        /// </summary>
        internal static void AddFold(IDictionary<int3, KernelState> context,
            int x, int y0, int z)
        {
            float3 anchor = new float3(x, 0f, z) * MerkabaConstants.LatticeStep;
            for (int y = y0 - 1; y <= y0 + 2; y++)
            {
                bool lower = y <= y0;
                float3 normal = Stored(XzNormal(lower ? 30f : 60f));
                float constant = math.dot(anchor, normal) +
                    (lower ? 0f : -0.010f);
                Put(context, new int3(x, y, z), normal, constant);
            }
        }

        /// <summary>
        /// A curved leaf: the lattice cells within half a step (along the
        /// dominant axis) of a spherical cap, measured normals deviating up
        /// to 20 degrees from the true normal in a deterministic pattern.
        /// </summary>
        internal static void AddLeaf(IDictionary<int3, KernelState> context,
            float3 centre, float radius, float3 axis, float halfAngleDegrees,
            uint seed)
        {
            axis = math.normalize(axis);
            float step = MerkabaConstants.LatticeStep;
            int3 low = (int3)math.floor((centre - radius - step) / step);
            int3 high = (int3)math.ceil((centre + radius + step) / step);
            float cosine = math.cos(math.radians(halfAngleDegrees));
            for (int x = low.x; x <= high.x; x++)
            for (int y = low.y; y <= high.y; y++)
            for (int z = low.z; z <= high.z; z++)
            {
                int3 coord = new(x, y, z);
                float3 point = (float3)coord * step;
                float3 offset = point - centre;
                float distance = math.length(offset);
                if (distance <= 0f) continue;
                float3 direction = offset / distance;
                if (math.dot(direction, axis) < cosine) continue;
                if (math.abs(distance - radius) > step * 0.5f *
                    math.cmax(math.abs(direction)))
                    continue;
                uint hash = (uint)x * 73856093u ^ (uint)y * 19349663u ^
                    (uint)z * 83492791u ^ seed;
                hash ^= hash >> 13;
                hash *= 0x5bd1e995u;
                hash ^= hash >> 15;
                float tilt = math.radians(((hash & 1023u) / 1023f * 2f - 1f) *
                    20f);
                float azimuth = ((hash >> 10) & 1023u) / 1023f * 2f * math.PI;
                float3 tangent0 = math.normalize(math.cross(direction,
                    math.abs(direction.x) < 0.9f ? new float3(1f, 0f, 0f)
                        : new float3(0f, 1f, 0f)));
                float3 tangent1 = math.cross(direction, tangent0);
                float3 normal = direction * math.cos(tilt) +
                    (tangent0 * math.cos(azimuth) +
                     tangent1 * math.sin(azimuth)) * math.sin(tilt);
                float3 foot = centre + direction * radius;
                Put(context, coord, normal, math.dot(foot, Stored(normal)));
            }
        }

        /// <summary>Three curved leaves of one shrub around the origin.</summary>
        internal static Dictionary<int3, KernelState> Shrub()
        {
            var context = new Dictionary<int3, KernelState>();
            AddLeaf(context, new float3(0f, 0f, 0f), 0.12f,
                new float3(0f, 1f, 0.2f), 60f, 1u);
            AddLeaf(context, new float3(0.10f, 0.03f, 0.12f), 0.10f,
                new float3(1f, 0.5f, 0f), 60f, 2u);
            AddLeaf(context, new float3(-0.09f, 0.12f, 0.05f), 0.09f,
                new float3(-0.3f, 1f, -0.6f), 60f, 3u);
            return context;
        }

        /// <summary>
        /// The corpus around the tile [0, 8)^3: a 45 degree staircase and a
        /// noisy diagonal through tile edges, span chains leaving the tile
        /// through z = 0 and z = 8, a chart fold across y = 7/8, a curved leaf
        /// and a thin two-sided leaf with KNOWN FREE on both sides. Every
        /// element reaches far beyond the tile's 18^3 readout halo.
        /// </summary>
        internal static Dictionary<int3, KernelState> TileCorpus()
        {
            var context = new Dictionary<int3, KernelState>();
            for (int x = -2; x <= 10; x++)
            for (int y = -3; y <= 10; y++)
                Put(context, new int3(x, y, x), new float3(-1f, 0f, 1f), 0f);
            AddNoisyDiagonal(context, new int3(3, 3, 6), 11, 48.6f, 15f);
            AddSpanChain(context, new int3(2, 2, 0), -1);
            AddSpanChain(context, new int3(4, 5, 7), 1);
            AddSpanChain(context, new int3(6, 2, 0), -1);
            AddFold(context, 0, 7, 4);
            AddLeaf(context, new float3(0.19f, 0.19f, 0.02f), 0.10f,
                new float3(1f, 1f, 0.3f), 60f, 7u);
            for (int x = 5; x <= 10; x++)
            for (int z = 3; z <= 5; z++)
            {
                if (Put(context, new int3(x, 1, z), new float3(0f, 1f, 0f),
                        MerkabaConstants.LatticeStep))
                    context.TryAdd(new int3(x, 2, z), KnownFree());
                if (Put(context, new int3(x, 0, z), new float3(0f, -1f, 0f),
                        0f))
                    context.TryAdd(new int3(x, -1, z), KnownFree());
            }
            return context;
        }
    }
}
