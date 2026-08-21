using System;
using System.Runtime.InteropServices;
using Genesis.RoomScan.Prism;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class PrismTopologyContractTests
    {
        private const long MaxStorageBindingBytes = 128L * 1024L * 1024L;

        [Test]
        public void TopologyGpuAbiMatchesDeclaredStrides()
        {
            Assert.That(Marshal.SizeOf<ContactFilmHeaderGpu>(),
                Is.EqualTo(ContactFilmHeaderGpu.Stride));
            Assert.That(Marshal.SizeOf<DisplacementPageHeaderGpu>(),
                Is.EqualTo(DisplacementPageHeaderGpu.Stride));
            Assert.That(Marshal.SizeOf<DisplacementCellGpu>(),
                Is.EqualTo(DisplacementCellGpu.Stride));
            Assert.That(Marshal.SizeOf<ContactTopologyEvidenceGpu>(),
                Is.EqualTo(ContactTopologyEvidenceGpu.Stride));
            Assert.That(Marshal.SizeOf<TopologySplitRecordGpu>(),
                Is.EqualTo(TopologySplitRecordGpu.Stride));
            Assert.That(Marshal.SizeOf<TopologyMergeRecordGpu>(),
                Is.EqualTo(TopologyMergeRecordGpu.Stride));
            Assert.That(Marshal.SizeOf<FilmMergeHashEntryGpu>(),
                Is.EqualTo(FilmMergeHashEntryGpu.Stride));
        }

        [Test]
        public void DefaultSparseBuffersRemainBelowStorageBindingLimit()
        {
            const long films = 65_536;
            const long basePages = 8_192;
            const long microPages = 16_384;
            long baseCells = basePages * ContactDisplacementPool.BaseCellsPerPage;
            long microCells = microPages * ContactDisplacementPool.MicroCellsPerPage;

            long[] bindingBytes =
            {
                films * ContactFilmHeaderGpu.Stride,
                films * 9L * 16L,
                baseCells * DisplacementCellGpu.Stride,
                microCells * DisplacementCellGpu.Stride,
                (baseCells + microCells) * 8L * sizeof(int),
                films * ContactTopologyEvidenceGpu.Stride,
                films * FilmMergeHashEntryGpu.Stride * 2L
            };
            foreach (long bytes in bindingBytes)
                Assert.That(bytes, Is.LessThan(MaxStorageBindingBytes),
                    $"A single Vulkan storage binding would be {bytes} bytes.");
        }

        [Test]
        public void RequiredQ311ComputeKernelsImport()
        {
            ComputeShader displacement = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.genesis.roomscan/Runtime/Resources/Prism/" +
                "DisplacementTopology.compute");
            ComputeShader topology = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.genesis.roomscan/Runtime/Resources/Prism/" +
                "TopologyAdapt.compute");
            Assert.That(displacement, Is.Not.Null);
            Assert.That(topology, Is.Not.Null);

            string[] displacementKernels =
            {
                "InitializeDisplacementState", "AllocateBasePages",
                "AllocateBasePagesBehind", "AllocateBasePagesOccluder",
                "AccumulateDisplacement", "SolveDirtyDisplacement",
                "AccumulateFreeSpaceCoverage",
                "AccumulateOccluderFreeSpaceCoverage",
                "AllocateMicrotiles", "InitializeMicroPages",
                "SolveTopologyEvidence"
            };
            string[] topologyKernels =
            {
                "SplitContactFilms", "InitializeSplitDisplacement",
                "TransferSplitBoundaries", "BuildFilmMergeHash",
                "MergeCompatibleFilms", "InitializeMergedDisplacement"
            };
            foreach (string kernel in displacementKernels)
                Assert.DoesNotThrow(() => displacement.FindKernel(kernel), kernel);
            foreach (string kernel in topologyKernels)
                Assert.DoesNotThrow(() => topology.FindKernel(kernel), kernel);
        }

        [Test]
        public void FilmFlagsKeepOneSidedDetailAndRetiredParentsDistinct()
        {
            uint activeChild = (uint)(ContactFilmFlags.Active |
                ContactFilmFlags.OneSided | ContactFilmFlags.HasDisplacement);
            uint retiredParent = (uint)(ContactFilmFlags.SplitParent |
                ContactFilmFlags.Retired);

            Assert.That(activeChild & (uint)ContactFilmFlags.OneSided,
                Is.Not.Zero);
            Assert.That(activeChild & (uint)ContactFilmFlags.HasDisplacement,
                Is.Not.Zero);
            Assert.That(retiredParent & (uint)ContactFilmFlags.Active, Is.Zero);
            Assert.That(retiredParent & activeChild, Is.Zero);
        }
    }
}
