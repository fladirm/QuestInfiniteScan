using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaExportShellTests
    {
        private static readonly Color32 WallColor = new(48, 132, 218, 255);

        [Test]
        public void StrongKnownFree_UsesTheOneCentralThreshold()
        {
            KernelState weak = Free(MerkabaConstants.ExportKnownFreeThreshold + 1);
            KernelState strong = Free(MerkabaConstants.ExportKnownFreeThreshold);

            Assert.That(MerkabaConstants.ExportKnownFreeThreshold,
                Is.EqualTo(-MerkabaConstants.OccupiedOnThreshold));
            Assert.That(MerkabaExportShell.IsStrongKnownFree(weak), Is.False);
            Assert.That(MerkabaExportShell.IsStrongKnownFree(strong), Is.True);
        }

        [Test]
        public void RearOccupancy_IsNotDiscardedBySparseKnownFree()
        {
            var evidence = new Dictionary<int3, KernelState>
            {
                [new int3(-1, 0, 0)] = StrongFree(),
                [new int3(0, 0, 0)] = Occupied(),
                [new int3(1, 0, 0)] = Occupied(),
                [new int3(2, 0, 0)] = Occupied()
            };

            MerkabaExportShellResult result = MerkabaExportShell.Build(evidence);

            Assert.That(result.ShellCoordinates, Is.EqualTo(new[]
            {
                new int3(0, 0, 0), new int3(1, 0, 0), new int3(2, 0, 0)
            }));
        }

        [Test]
        public void BothObservedSidesAndRealInteriorRemainForMembraneSolver()
        {
            var evidence = new Dictionary<int3, KernelState>
            {
                [new int3(-1, 0, 0)] = StrongFree(),
                [new int3(0, 0, 0)] = Occupied(),
                [new int3(1, 0, 0)] = Occupied(),
                [new int3(2, 0, 0)] = Occupied(),
                [new int3(3, 0, 0)] = Occupied(),
                [new int3(4, 0, 0)] = StrongFree()
            };

            MerkabaExportShellResult result = MerkabaExportShell.Build(evidence);

            Assert.That(result.ShellCoordinates, Is.EqualTo(new[]
            {
                new int3(0, 0, 0), new int3(1, 0, 0),
                new int3(2, 0, 0), new int3(3, 0, 0)
            }));
        }

        [Test]
        public void OneUnknownWallHole_IsHealedOnlyInExportReadout()
        {
            int3 hole = new(0, 0, 0);
            Dictionary<int3, KernelState> evidence = AxisWall(hole, 2, hole);

            MerkabaExportShellResult result = MerkabaExportShell.Build(evidence);

            Assert.That(result.HealedCoordinates, Does.Contain(hole));
            Assert.That(result.ShellCoordinates, Does.Contain(hole));
            Assert.That(result.SyntheticKernelCount, Is.EqualTo(1));
            MerkabaKernelSnapshot synthetic = result.Kernels.Single(value =>
                value.Coord.Equals(hole));
            Assert.That(synthetic.State.Color, Is.EqualTo(WallColor));
            Assert.That(evidence.ContainsKey(hole), Is.False,
                "Export-local healing mutated the canonical evidence map.");
        }

        [Test]
        public void StrongKnownFreeOpening_IsNeverFilledByClosing()
        {
            int3 opening = new(0, 0, 0);
            Dictionary<int3, KernelState> evidence = AxisWall(opening, 2, opening);
            evidence[opening] = StrongFree();

            MerkabaExportShellResult result = MerkabaExportShell.Build(evidence);

            Assert.That(result.HealedCoordinates.Contains(opening), Is.False);
            Assert.That(result.ShellCoordinates.Contains(opening), Is.False);
            Assert.That(result.SyntheticKernelCount, Is.Zero);
        }

        [Test]
        public void LargeUnknownOpening_SurvivesOneFixedClosingIteration()
        {
            var evidence = new Dictionary<int3, KernelState>();
            for (int y = -5; y <= 5; y++)
            for (int z = -5; z <= 5; z++)
            {
                if (Math.Abs(y) <= 2 && Math.Abs(z) <= 2) continue;
                evidence[new int3(0, y, z)] = Occupied();
            }

            MerkabaExportShellResult result = MerkabaExportShell.Build(evidence);

            Assert.That(result.HealedCoordinates.Contains(new int3(0)), Is.False);
        }

        [Test]
        public void DiagonalSteppedWall_HealsOneUnknownDropout()
        {
            int3 hole = new(0, 0, 0);
            var evidence = new Dictionary<int3, KernelState>();
            for (int diagonal = -3; diagonal <= 3; diagonal++)
            for (int z = -3; z <= 3; z++)
            {
                int3 coord = new(diagonal, diagonal, z);
                if (!coord.Equals(hole)) evidence[coord] = Occupied();
            }

            MerkabaExportShellResult result = MerkabaExportShell.Build(evidence);

            Assert.That(result.HealedCoordinates, Does.Contain(hole));
        }

        [Test]
        public void ChunkBorderTranslation_ProducesIdenticalHealAndShell()
        {
            int3 interiorCenter = new(8, 8, 8);
            int3 borderCenter = new(31, 31, 31);
            MerkabaExportShellResult interior = MerkabaExportShell.Build(
                SnapshotFromEvidence(ObservedWallFixture(interiorCenter)));
            MerkabaExportShellResult border = MerkabaExportShell.Build(
                SnapshotFromEvidence(ObservedWallFixture(borderCenter)));

            Assert.That(RelativeKey(interior.HealedCoordinates, interiorCenter),
                Is.EqualTo(RelativeKey(border.HealedCoordinates, borderCenter)));
            Assert.That(RelativeKey(interior.ShellCoordinates, interiorCenter),
                Is.EqualTo(RelativeKey(border.ShellCoordinates, borderCenter)));
        }

        [Test]
        public void SameSnapshot_ProducesDeterministicCoordinatesAndKernelOrder()
        {
            MerkabaSessionSnapshot snapshot = SnapshotFromEvidence(
                ObservedWallFixture(new int3(-32, 7, 31)));

            MerkabaExportShellResult first = MerkabaExportShell.Build(snapshot);
            MerkabaExportShellResult second = MerkabaExportShell.Build(snapshot);

            Assert.That(second.HealedCoordinates, Is.EqualTo(first.HealedCoordinates));
            Assert.That(second.ShellCoordinates, Is.EqualTo(first.ShellCoordinates));
            Assert.That(second.Kernels.Select(value => value.Coord),
                Is.EqualTo(first.Kernels.Select(value => value.Coord)));
            Assert.That(second.Kernels.Select(value => value.State.PackedColor),
                Is.EqualTo(first.Kernels.Select(value => value.State.PackedColor)));
        }

        [Test]
        public void SeparableClosing_IsExactlyEquivalentToCubicReference()
        {
            var occupied = new HashSet<int3>();
            for (int z = -4; z <= 4; z++)
            for (int y = -5; y <= 3; y++)
            for (int x = -6; x <= 2; x++)
            {
                if ((x * 17 + y * 11 + z * 7) % 5 != 0 &&
                    !(x == -2 && y == -1 && z == 1))
                    occupied.Add(new int3(x, y, z));
            }

            var strongFree = new HashSet<int3>
            {
                new(-2, -1, 1),
                new(-4, 0, 0)
            };
            occupied.ExceptWith(strongFree);
            var evidence = occupied.ToDictionary(coord => coord,
                _ => Occupied());
            foreach (int3 coord in strongFree)
                evidence[coord] = StrongFree();

            int3[] expected = CubicClosingReference(occupied, strongFree)
                .OrderBy(value => value.x).ThenBy(value => value.y)
                .ThenBy(value => value.z).ToArray();
            MerkabaExportShellResult actual = MerkabaExportShell.Build(evidence);

            Assert.That(actual.HealedCoordinates, Is.EqualTo(expected));
        }

        [Test]
        public void ExportCleanup_DoesNotMutateCanonicalSnapshot()
        {
            Dictionary<int3, KernelState> evidence =
                ObservedWallFixture(new int3(-1, -31, 32));
            foreach (int3 coord in evidence.Keys.ToArray())
            {
                KernelState state = evidence[coord];
                if (!state.IsOccupied) continue;
                state.Flags = KernelState.SetSurfacePlane(state.Flags,
                    new float3(1, 0, 0), 0f);
                evidence[coord] = state;
            }
            MerkabaSessionSnapshot snapshot = SnapshotFromEvidence(evidence);
            byte[] before = Serialize(snapshot);

            MerkabaExportShellResult shell = MerkabaExportShell.Build(snapshot);
            MerkabaExportMembraneResult membrane =
                MerkabaExportMembrane.Build(shell);
            using var export = new MemoryStream();
            _ = MerkabaGlbWriter.Write(export, membrane);

            Assert.That(Serialize(snapshot), Is.EqualTo(before));
        }

        [Test]
        public void ComponentWithoutFreeEvidence_PreservesEveryRealOwner()
        {
            var evidence = new Dictionary<int3, KernelState>();
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                evidence[new int3(x, y, z)] = Occupied();

            MerkabaExportShellResult result = MerkabaExportShell.Build(evidence);

            Assert.That(result.HealedCoordinates, Has.Length.EqualTo(27));
            Assert.That(result.ShellCoordinates, Has.Length.EqualTo(27));
            Assert.That(result.ShellCoordinates.Contains(new int3(0)), Is.True);
        }

        [Test]
        public void SparseStrongFree_DoesNotDeleteA197kObservedSurfaceFixture()
        {
            const int expectedOccupied = 197000;
            var evidence = new Dictionary<int3, KernelState>(
                expectedOccupied + 1);
            for (int x = 0; x < 197; x++)
            for (int y = 0; y < 20; y++)
            for (int z = 0; z < 50; z++)
                evidence.Add(new int3(x, y, z), Occupied());
            evidence.Add(new int3(-1, 0, 0), StrongFree());

            MerkabaExportShellResult result = MerkabaExportShell.Build(evidence);

            Assert.That(result.OriginalOccupiedCount,
                Is.EqualTo(expectedOccupied));
            Assert.That(result.StrongKnownFreeCount, Is.EqualTo(1));
            Assert.That(result.SyntheticKernelCount, Is.Zero);
            Assert.That(result.ShellCoordinates, Has.Length.EqualTo(
                expectedOccupied));
        }

        [Test]
        public void ExportShell_ReachesMeasuredPlaneMembraneGlbGeometry()
        {
            Dictionary<int3, KernelState> evidence =
                ObservedWallFixture(new int3(0));
            foreach (int3 coord in evidence.Keys.ToArray())
            {
                KernelState state = evidence[coord];
                if (state.IsOccupied)
                {
                    state.Flags = KernelState.SetSurfacePlane(state.Flags,
                        new float3(1, 0, 0), 0f);
                    evidence[coord] = state;
                }
            }
            MerkabaExportShellResult shell = MerkabaExportShell.Build(evidence);
            MerkabaExportMembraneResult membrane =
                MerkabaExportMembrane.Build(shell);
            using var stream = new MemoryStream();
            MerkabaGlbResult result = MerkabaGlbWriter.Write(stream, membrane);
            byte[] bytes = stream.ToArray();
            int jsonLength = checked((int)BitConverter.ToUInt32(bytes, 12));
            int binaryStart = 20 + jsonLength + 8;

            Assert.That(result.PrimitiveCount, Is.GreaterThan(0));
            for (int vertex = 0; vertex < result.VertexCount; vertex++)
            {
                int offset = binaryStart + vertex * 28 + 12;
                float x = Math.Abs(BitConverter.ToSingle(bytes, offset));
                float y = Math.Abs(BitConverter.ToSingle(bytes, offset + 4));
                float z = Math.Abs(BitConverter.ToSingle(bytes, offset + 8));
                Assert.That(x, Is.GreaterThan(0.99f));
                Assert.That(y, Is.LessThan(0.01f));
                Assert.That(z, Is.LessThan(0.01f),
                    "The old octa/tip geometry leaked into measured export.");
            }
        }

        private static Dictionary<int3, KernelState> AxisWall(int3 center,
            int radius, int3 omitted)
        {
            var evidence = new Dictionary<int3, KernelState>();
            for (int y = -radius; y <= radius; y++)
            for (int z = -radius; z <= radius; z++)
            {
                int3 coord = center + new int3(0, y, z);
                if (!coord.Equals(omitted)) evidence[coord] = Occupied();
            }
            return evidence;
        }

        private static Dictionary<int3, KernelState> ObservedWallFixture(int3 center)
        {
            var evidence = AxisWall(center, 2, center);
            for (int y = -2; y <= 2; y++)
            for (int z = -2; z <= 2; z++)
                evidence[center + new int3(-1, y, z)] = StrongFree();
            return evidence;
        }

        private static MerkabaSessionSnapshot SnapshotFromEvidence(
            IReadOnlyDictionary<int3, KernelState> evidence)
        {
            var tiles = new Dictionary<MerkabaTileAddress, KernelState[]>();
            foreach (KeyValuePair<int3, KernelState> pair in evidence)
            {
                MerkabaSpatial.Address address = MerkabaSpatial.Encode(pair.Key);
                var tileAddress = new MerkabaTileAddress(address.BlockCoord,
                    address.LocalAddress);
                if (!tiles.TryGetValue(tileAddress, out KernelState[] states))
                {
                    states = new KernelState[MerkabaSpatial.KernelsPerTile];
                    tiles.Add(tileAddress, states);
                }
                states[address.KernelLocal] = pair.Value;
            }

            var snapshot = new MerkabaSessionSnapshot();
            foreach (MerkabaTileAddress address in tiles.Keys.OrderBy(value => value))
            {
                snapshot.Tiles.Add(new MerkabaTileSnapshot
                {
                    Address = address,
                    States = tiles[address]
                });
            }
            return snapshot;
        }

        private static KernelState Occupied()
        {
            KernelState state = default;
            state.SetOccupiedForFixture(true, WallColor);
            return state;
        }

        private static KernelState StrongFree() =>
            Free(MerkabaConstants.ExportKnownFreeThreshold);

        private static KernelState Free(int evidence) => new()
        {
            OccupancyEvidence = evidence
        };

        private static string RelativeKey(IEnumerable<int3> coords, int3 origin) =>
            string.Join(";", coords.Select(value => value - origin)
                .OrderBy(value => value.x).ThenBy(value => value.y)
                .ThenBy(value => value.z)
                .Select(value => $"{value.x},{value.y},{value.z}"));

        private static byte[] Serialize(MerkabaSessionSnapshot snapshot)
        {
            using var stream = new MemoryStream();
            MerkabaPersistence.WriteSnapshot(stream, snapshot);
            return stream.ToArray();
        }

        private static HashSet<int3> CubicClosingReference(
            HashSet<int3> occupied, HashSet<int3> strongFree)
        {
            var dilated = new HashSet<int3>();
            foreach (int3 coord in occupied)
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                dilated.Add(coord + new int3(x, y, z));

            var healed = new HashSet<int3>(occupied);
            foreach (int3 candidate in dilated)
            {
                bool retained = true;
                for (int z = -1; z <= 1 && retained; z++)
                for (int y = -1; y <= 1 && retained; y++)
                for (int x = -1; x <= 1; x++)
                {
                    if (dilated.Contains(candidate + new int3(x, y, z)))
                        continue;
                    retained = false;
                    break;
                }
                if (retained && !strongFree.Contains(candidate))
                    healed.Add(candidate);
            }
            return healed;
        }
    }
}
