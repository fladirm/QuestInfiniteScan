using System;
using System.IO;
using System.Runtime.InteropServices;
using Genesis.RoomScan.Prism;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class PrismMeshletContractTests
    {
        private const long MaxStorageBindingBytes = 128L * 1024L * 1024L;

        [Test]
        public void MeshletGpuAbiMatchesDeclaredStrides()
        {
            Assert.That(Marshal.SizeOf<ContactMeshletVertexGpu>(),
                Is.EqualTo(ContactMeshletVertexGpu.Stride));
            Assert.That(Marshal.SizeOf<ContactMeshletDescriptorGpu>(),
                Is.EqualTo(ContactMeshletDescriptorGpu.Stride));
            Assert.That(Marshal.SizeOf<ContactMeshletViewLodGpu>(),
                Is.EqualTo(ContactMeshletViewLodGpu.Stride));
        }

        [Test]
        public void DefaultMeshletBindingsRemainBelowStorageBindingLimit()
        {
            long[] bindingBytes =
            {
                1_500_000L * ContactMeshletVertexGpu.Stride,
                6_000_000L * sizeof(uint),
                131_072L * ContactMeshletDescriptorGpu.Stride,
                131_072L * ContactMeshletViewLodGpu.Stride
            };
            foreach (long bytes in bindingBytes)
                Assert.That(bytes, Is.LessThan(MaxStorageBindingBytes),
                    $"A single Vulkan storage binding would be {bytes} bytes.");
        }

        [Test]
        public void RequiredQ312ComputeKernelsImport()
        {
            ComputeShader build = Load("MeshletBuild.compute");
            ComputeShader cull = Load("MeshletViewCull.compute");
            ComputeShader hiZ = Load("HiZRangePyramid.compute");
            Assert.That(build, Is.Not.Null);
            Assert.That(cull, Is.Not.Null);
            Assert.That(hiZ, Is.Not.Null);
            foreach (string kernel in new[]
                     {
                         "ClearMeshletBuild", "BuildMeshDispatchArguments",
                         "BuildAdaptiveFilmMeshlets",
                         "BuildElasticBoundaryMeshlets",
                         "FinalizeMeshletDrawArguments"
                     })
                Assert.DoesNotThrow(() => build.FindKernel(kernel), kernel);
            foreach (string kernel in new[]
                     { "ClearMeshletView", "CullMeshletView", "FinalizeMeshletView" })
                Assert.DoesNotThrow(() => cull.FindKernel(kernel), kernel);
            foreach (string kernel in new[]
                     { "CopyHiZLevelZero", "ReduceHiZLevel" })
                Assert.DoesNotThrow(() => hiZ.FindKernel(kernel), kernel);
        }

        [Test]
        public void PublicationUsesDistinctFrontAndBackBuffers()
        {
            using var meshlets = new ContactMeshletBuffers(8, 24, 4);
            ContactMeshletGenerationBuffers original = meshlets.Published;
            Assert.That(meshlets.TryBeginBuild(out ContactMeshletGenerationBuffers inactive),
                Is.True);
            Assert.That(inactive, Is.Not.SameAs(original));
            Assert.That(inactive.Vertices, Is.Not.SameAs(original.Vertices));
            Assert.That(inactive.Indices, Is.Not.SameAs(original.Indices));
            Assert.That(inactive.Descriptors, Is.Not.SameAs(original.Descriptors));
            meshlets.Publish(7u);
            Assert.That(meshlets.Published, Is.SameAs(inactive));
            Assert.That(meshlets.PublicationGeneration, Is.EqualTo(7u));
            Assert.That(meshlets.TryBeginBuild(out ContactMeshletGenerationBuffers next),
                Is.True);
            Assert.That(next, Is.SameAs(original));
        }

        [Test]
        public void ProductionMeshletPathContainsNoCpuReadback()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../Packages/com.genesis.roomscan/Runtime/Prism/Geometry"));
            foreach (string name in new[]
                     {
                         "PrismMeshletBuilder.cs", "PrismPredictionRenderer.cs",
                         "ContactMeshletBuffers.cs"
                     })
            {
                string source = File.ReadAllText(Path.Combine(root, name));
                StringAssert.DoesNotContain("AsyncGPUReadback", source, name);
                StringAssert.DoesNotContain(".GetData(", source, name);
                StringAssert.DoesNotContain("new Mesh(", source, name);
            }
        }

        private static ComputeShader Load(string name) =>
            AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.genesis.roomscan/Runtime/Resources/Prism/" + name);
    }
}
