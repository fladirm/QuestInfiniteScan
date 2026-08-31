using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaExportMembraneTests
    {
        private static readonly Color32 MeasuredColor = new(32, 96, 224, 255);

        [Test]
        public void ClosedMeasuredRoomProducesOnlyMeasuredPatches()
        {
            var evidence = new Dictionary<int3, KernelState>();
            AddMeasured(evidence, new int3(-2, 0, 0), new float3(1, 0, 0));
            AddMeasured(evidence, new int3(2, 0, 0), new float3(1, 0, 0));
            AddMeasured(evidence, new int3(0, -2, 0), new float3(0, 1, 0));
            AddMeasured(evidence, new int3(0, 2, 0), new float3(0, 1, 0));
            AddMeasured(evidence, new int3(0, 0, -2), new float3(0, 0, 1));
            AddMeasured(evidence, new int3(0, 0, 2), new float3(0, 0, 1));

            MerkabaExportMembraneResult result = Build(evidence);

            Assert.That(result.MeasuredPatchCount, Is.EqualTo(6));
            Assert.That(result.InferredPatchCount, Is.Zero);
            Assert.That(result.Patches.All(value => !value.IsInferred), Is.True);
        }

        [Test]
        public void StrongFreeDoorwayIsNotClosed()
        {
            int3 opening = new(0, 0, 0);
            Dictionary<int3, KernelState> evidence = WallWithHole(opening);
            evidence[opening] = StrongFree();

            MerkabaExportMembraneResult result = Build(evidence);

            Assert.That(result.Patches.Any(value => value.Coord.Equals(opening)),
                Is.False);
        }

        [Test]
        public void OneMissingWallSupportBecomesNeutralGrayInferredMembrane()
        {
            int3 hole = new(0, 0, 0);
            Dictionary<int3, KernelState> evidence = WallWithHole(hole);

            MerkabaExportMembraneResult result = Build(evidence);
            MerkabaExportMembranePatch patch = result.Patches.Single(value =>
                value.Coord.Equals(hole));

            Assert.That(patch.IsInferred, Is.True);
            Assert.That(patch.PackedColor,
                Is.EqualTo(MerkabaConstants.NeutralPackedColor));
            Assert.That(result.InferredPatchCount, Is.EqualTo(1));
        }

        [Test]
        public void RealOccupiedBehindLayerIsNeverDestructivelyFiltered()
        {
            var evidence = new Dictionary<int3, KernelState>
            {
                [new int3(-1, 0, 0)] = StrongFree(),
                [new int3(0, 0, 0)] = Measured(new float3(1, 0, 0)),
                [new int3(1, 0, 0)] = Measured(new float3(1, 0, 0))
            };

            MerkabaExportMembraneResult result = Build(evidence);

            Assert.That(result.RemovedBehindMembraneCount, Is.Zero);
            Assert.That(result.Patches.Any(value =>
                value.Coord.Equals(new int3(0, 0, 0))), Is.True);
            Assert.That(result.Patches.Any(value =>
                value.Coord.Equals(new int3(1, 0, 0))), Is.True);
        }

        [Test]
        public void ThinPartitionWithKnownFreeOnBothSidesKeepsBothLayers()
        {
            var evidence = new Dictionary<int3, KernelState>
            {
                [new int3(-1, 0, 0)] = StrongFree(),
                [new int3(0, 0, 0)] = Measured(new float3(1, 0, 0)),
                [new int3(1, 0, 0)] = Measured(new float3(1, 0, 0)),
                [new int3(2, 0, 0)] = StrongFree()
            };

            MerkabaExportMembraneResult result = Build(evidence);

            Assert.That(result.RemovedBehindMembraneCount, Is.Zero);
            Assert.That(result.MeasuredPatchCount, Is.EqualTo(2));
        }

        [Test]
        public void CloseParallelLayersWithoutFreeConstraintNeverCollapse()
        {
            var evidence = new Dictionary<int3, KernelState>
            {
                [new int3(0, 0, 0)] = Measured(new float3(1, 0, 0)),
                [new int3(1, 0, 0)] = Measured(new float3(1, 0, 0))
            };

            MerkabaExportMembraneResult result = Build(evidence);

            Assert.That(result.MeasuredPatchCount, Is.EqualTo(2));
            float first = math.dot(result.Patches[0].Corner00,
                result.Patches[0].Normal);
            float second = math.dot(result.Patches[1].Corner00,
                result.Patches[1].Normal);
            Assert.That(math.abs(first - second),
                Is.EqualTo(MerkabaConstants.LatticeStep).Within(1e-5f));
        }

        [Test]
        public void LegacyOwnerIsCountedAndPreservedForCanonicalFallback()
        {
            var evidence = new Dictionary<int3, KernelState>
            {
                [new int3(0, 0, 0)] = Measured(new float3(0, 0, 1)),
                [new int3(0, 1, 0)] = LegacyOccupied()
            };

            MerkabaExportMembraneResult result = Build(evidence);

            Assert.That(result.LegacyMeasuredUnknownPlaneCount, Is.EqualTo(1));
            Assert.That(result.UnresolvedLegacyCount, Is.Zero);
            Assert.That(result.LegacyKernels.Single().Coord,
                Is.EqualTo(new int3(0, 1, 0)));
            Assert.That(result.Patches.Any(value =>
                value.Coord.Equals(new int3(0, 1, 0))), Is.False);
        }

        [Test]
        public void PermutedInputProducesByteIdenticalPatchSequence()
        {
            var ordered = new Dictionary<int3, KernelState>();
            var reversed = new Dictionary<int3, KernelState>();
            var entries = new List<KeyValuePair<int3, KernelState>>();
            for (int y = -2; y <= 2; y++)
            for (int z = -2; z <= 2; z++)
                entries.Add(new KeyValuePair<int3, KernelState>(
                    new int3(0, y, z), Measured(new float3(1, 0, 0))));
            foreach (KeyValuePair<int3, KernelState> entry in entries)
                ordered.Add(entry.Key, entry.Value);
            for (int index = entries.Count - 1; index >= 0; index--)
                reversed.Add(entries[index].Key, entries[index].Value);

            MerkabaExportMembraneResult first = Build(ordered);
            MerkabaExportMembraneResult second = Build(reversed);

            Assert.That(second.Patches.Select(Key),
                Is.EqualTo(first.Patches.Select(Key)));
        }

        [Test]
        public void SparsePartitionScalesToA197kMeasuredFixture()
        {
            const int expectedOccupied = 197000;
            var evidence = new Dictionary<int3, KernelState>(
                expectedOccupied + 1);
            KernelState measured = Measured(new float3(0, 0, 1));
            for (int x = 0; x < 197; x++)
            for (int y = 0; y < 20; y++)
            for (int z = 0; z < 50; z++)
                evidence.Add(new int3(x, y, z), measured);
            evidence.Add(new int3(-1, 0, 0), StrongFree());

            Stopwatch watch = Stopwatch.StartNew();
            MerkabaExportMembraneResult result = Build(evidence);
            using var stream = new MemoryStream();
            MerkabaGlbResult glb = MerkabaGlbWriter.Write(stream, result);
            watch.Stop();

            Assert.That(result.CanonicalOccupiedCount,
                Is.EqualTo(expectedOccupied));
            Assert.That(result.MeasuredPatchCount,
                Is.GreaterThan(expectedOccupied - 10));
            Assert.That(glb.PrimitiveCount,
                Is.EqualTo(result.Patches.Count * 2));
            Assert.That(glb.ByteLength, Is.EqualTo(stream.Length));
            Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(15),
                $"Sparse membrane took {watch.Elapsed.TotalSeconds:F3}s.");
        }

        private static string Key(MerkabaExportMembranePatch patch) =>
            $"{patch.Coord.x},{patch.Coord.y},{patch.Coord.z}:" +
            $"{math.asint(patch.Corner00.x)},{math.asint(patch.Corner00.y)}," +
            $"{math.asint(patch.Corner00.z)}:{patch.PackedColor}:{patch.IsInferred}";

        private static MerkabaExportMembraneResult Build(
            IReadOnlyDictionary<int3, KernelState> evidence) =>
            MerkabaExportMembrane.Build(MerkabaExportShell.Build(evidence));

        private static Dictionary<int3, KernelState> WallWithHole(int3 hole)
        {
            var evidence = new Dictionary<int3, KernelState>();
            for (int y = -2; y <= 2; y++)
            for (int z = -2; z <= 2; z++)
            {
                int3 coord = new(0, y, z);
                if (!coord.Equals(hole))
                    evidence.Add(coord, Measured(new float3(1, 0, 0)));
            }
            return evidence;
        }

        private static void AddMeasured(IDictionary<int3, KernelState> evidence,
            int3 coord, float3 normal) => evidence.Add(coord, Measured(normal));

        private static KernelState Measured(float3 normal)
        {
            KernelState state = LegacyOccupied();
            state.Flags = KernelState.SetSurfacePlane(state.Flags, normal, 0f);
            return state;
        }

        private static KernelState LegacyOccupied()
        {
            KernelState state = default;
            state.SetOccupiedForFixture(true, MeasuredColor);
            return state;
        }

        private static KernelState StrongFree() => new()
        {
            OccupancyEvidence = MerkabaConstants.ExportKnownFreeThreshold
        };
    }
}
