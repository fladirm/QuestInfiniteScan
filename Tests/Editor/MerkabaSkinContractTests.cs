using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    /// <summary>
    /// The readout skin is a cheap analytic readout of canonical M8 state: the
    /// support decides topology only, every published vertex lies on the
    /// measured sheet, and one physical point has one identity.
    /// </summary>
    public sealed class MerkabaSkinContractTests
    {
        private const float PlaneEpsilon = 1e-4f;

        [Test]
        public void EveryPublishedVertexLiesOnTheMeasuredSheet()
        {
            Dictionary<int3, KernelState> context = Wall(-3, 3, -3, 3);
            int corners = 0;
            foreach ((int3 coord, KernelState state) in Measured(context))
            {
                uint mask = MerkabaSkin.CompatibleNeighbourMask(coord, state,
                    context);
                float3 facing = MerkabaSkin.Facing(coord, state, context);
                foreach (MerkabaSkin.Facelet facelet in MerkabaSkin.Facelets
                             .ToArray())
                {
                    if (!MerkabaSkin.FaceletVisible(facelet, mask, facing))
                        continue;
                    foreach (int vertex in new[] { facelet.A, facelet.B,
                                 facelet.C })
                    {
                        float3 position = MerkabaSkin.VertexPosition(coord,
                            state, context, mask, vertex);
                        Assert.That(math.abs(position.z),
                            Is.LessThan(PlaneEpsilon),
                            $"kernel {coord} vertex {vertex} left the sheet");
                        corners++;
                    }
                }
            }
            Assert.That(corners, Is.GreaterThan(0));
        }

        [Test]
        public void PlanarSheetPublishesNoScaffoldFacets()
        {
            Dictionary<int3, KernelState> context = Wall(-3, 3, -3, 3);
            int facets = 0;
            foreach ((int3 coord, KernelState state) in Measured(context))
            {
                uint mask = MerkabaSkin.CompatibleNeighbourMask(coord, state,
                    context);
                float3 facing = MerkabaSkin.Facing(coord, state, context);
                foreach (MerkabaSkin.Facelet facelet in MerkabaSkin.Facelets
                             .ToArray())
                {
                    if (!MerkabaSkin.FaceletVisible(facelet, mask, facing))
                        continue;
                    float3 a = MerkabaSkin.VertexPosition(coord, state, context,
                        mask, facelet.A);
                    float3 b = MerkabaSkin.VertexPosition(coord, state, context,
                        mask, facelet.B);
                    float3 c = MerkabaSkin.VertexPosition(coord, state, context,
                        mask, facelet.C);
                    float3 normal = math.cross(b - a, c - a);
                    if (math.lengthsq(normal) < 1e-12f) continue;
                    float alignment = math.abs(math.normalize(normal).z);
                    Assert.That(alignment, Is.GreaterThan(0.999f),
                        $"kernel {coord} facelet is tilted {alignment}");
                    facets++;
                }
            }
            Assert.That(facets, Is.GreaterThan(0));
        }

        [Test]
        public void NeighbouringKernelsShareOnePhysicalVertex()
        {
            Dictionary<int3, KernelState> context = Wall(-3, 3, -3, 3);
            int3 first = new(0, 0, 0);
            int3 second = new(1, 0, 0);
            int neighbourBit = -1;
            for (int index = 0; index < 26; index++)
                if (math.all(MerkabaSkin.NeighbourOffset(index) ==
                             second - first))
                    neighbourBit = index;
            Assert.That(neighbourBit, Is.GreaterThanOrEqualTo(0));

            KernelState firstState = context[first];
            KernelState secondState = context[second];
            uint firstMask = MerkabaSkin.CompatibleNeighbourMask(first,
                firstState, context);
            uint secondMask = MerkabaSkin.CompatibleNeighbourMask(second,
                secondState, context);
            Assert.That(firstMask & (1u << neighbourBit), Is.Not.Zero);

            int shared = 0;
            for (int vertex = 0; vertex < MerkabaSkin.VertexCount; vertex++)
            {
                if ((MerkabaSkin.VertexContacts[vertex] &
                     (1u << neighbourBit)) == 0u) continue;
                int3 offset = MerkabaSkin.Vertices[vertex] -
                    (second - first) * MerkabaSkin.LatticeUnits;
                // A contact vertex inside the neighbour's support but not on
                // its boundary has no twin and is simply not shared.
                int twin = MerkabaSkin.TemplateIndexOfOffset(offset);
                if (twin < 0) continue;
                float3 fromFirst = MerkabaSkin.VertexPosition(first,
                    firstState, context, firstMask, vertex);
                float3 fromSecond = MerkabaSkin.VertexPosition(second,
                    secondState, context, secondMask, twin);
                Assert.That(fromFirst.Equals(fromSecond), Is.True,
                    $"vertex {vertex}/{twin} differs: {fromFirst} {fromSecond}");
                Assert.That(MerkabaSkin.VertexColor(first, firstState, context,
                        firstMask, vertex), Is.EqualTo(MerkabaSkin.VertexColor(
                        second, secondState, context, secondMask, twin)));
                shared++;
            }
            Assert.That(shared, Is.GreaterThan(0));
        }

        [Test]
        public void ExportSharesVerticesAcrossKernels()
        {
            Dictionary<int3, KernelState> context = Wall(-3, 3, -3, 3);
            MerkabaExportMembraneResult result = MerkabaExportMembrane.BuildSkin(
                MerkabaExportShell.Build(context));
            var positions = new HashSet<float3>();
            int corners = 0;
            foreach (MerkabaExportMembranePatch patch in result.Patches)
            {
                positions.Add(patch.Corner00);
                positions.Add(patch.Corner10);
                positions.Add(patch.Corner11);
                corners += 3;
            }
            Assert.That(corners, Is.GreaterThan(0));
            Assert.That(positions.Count * 2, Is.LessThan(corners),
                "corners are not shared across facelets");
        }

        [Test]
        public void ExportEdgesNeverBridgeSeparatedComponents()
        {
            var context = new Dictionary<int3, KernelState>();
            foreach (KeyValuePair<int3, KernelState> entry in Wall(-6, -4, -2, 2))
                context[entry.Key] = entry.Value;
            foreach (KeyValuePair<int3, KernelState> entry in Wall(4, 6, -2, 2))
                context[entry.Key] = entry.Value;
            MerkabaExportMembraneResult result = MerkabaExportMembrane.BuildSkin(
                MerkabaExportShell.Build(context));
            float longest = 0f;
            foreach (MerkabaExportMembranePatch patch in result.Patches)
            {
                longest = math.max(longest, math.distance(patch.Corner00,
                    patch.Corner10));
                longest = math.max(longest, math.distance(patch.Corner10,
                    patch.Corner11));
                longest = math.max(longest, math.distance(patch.Corner11,
                    patch.Corner00));
            }
            Assert.That(result.Patches.Count, Is.GreaterThan(0));
            // One support spans 50 mm; nothing may bridge the 175 mm gap.
            Assert.That(longest, Is.LessThan(0.051f));
        }

        private static IEnumerable<(int3, KernelState)> Measured(
            Dictionary<int3, KernelState> context)
        {
            foreach (KeyValuePair<int3, KernelState> entry in context)
                if (entry.Value.IsOccupied &&
                    entry.Value.HasMeasuredSurfacePlane)
                    yield return (entry.Key, entry.Value);
        }

        /// <summary>
        /// A measured sheet at z = 0 facing +z, with KNOWN-FREE evidence above it.
        /// </summary>
        private static Dictionary<int3, KernelState> Wall(int minX, int maxX,
            int minY, int maxY)
        {
            var context = new Dictionary<int3, KernelState>();
            for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                KernelState state = default;
                state.SetOccupiedForFixture(true, new Color32(40, 90, 200, 255));
                state.Flags = KernelState.SetSurfacePlane(state.Flags,
                    new float3(0f, 0f, 1f), 0f);
                context[new int3(x, y, 0)] = state;
                context[new int3(x, y, 1)] = new KernelState
                {
                    OccupancyEvidence = MerkabaConstants.ExportKnownFreeThreshold
                };
            }
            return context;
        }
    }
}
