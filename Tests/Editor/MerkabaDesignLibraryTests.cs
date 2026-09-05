using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaDesignLibraryTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(),
                "merkaba-library-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }

        [Test]
        public void DuplicateImportStoresOneContentAddressedAsset()
        {
            byte[] glb = CreateGlb();
            string firstPath = Path.Combine(_directory, "chair.glb");
            string secondPath = Path.Combine(_directory, "same-chair.glb");
            File.WriteAllBytes(firstPath, glb);
            File.WriteAllBytes(secondPath, glb);
            string libraryPath = Path.Combine(_directory, "library");
            var library = new MerkabaDesignLibrary(libraryPath);

            MerkabaDesignAsset first = library.Import(firstPath);
            MerkabaDesignAsset second = library.Import(secondPath);

            Assert.That(second.id, Is.EqualTo(first.id));
            Assert.That(library.Assets.Count, Is.EqualTo(1));
            Assert.That(Directory.GetFiles(libraryPath, "*.glb"),
                Has.Length.EqualTo(1));
            Assert.That(Directory.GetFiles(libraryPath, "*.json"),
                Has.Length.EqualTo(1));
            Assert.That(File.ReadAllBytes(library.AssetPath(first.id)),
                Is.EqualTo(glb));
            Assert.That(library.Decode(first.id).Indices.Length,
                Is.GreaterThan(0));
        }

        [Test]
        public void InstanceTransformAndStateRoundTripInDesignDocument()
        {
            string path = Path.Combine(_directory, "design.json");
            var document = new MerkabaDesignDocument();
            document.instances.Add(new MerkabaDesignInstance
            {
                instanceId = document.AllocateInstanceId(),
                assetId = new string('a', 64),
                position = new Vector3(1f, 2f, -3f),
                rotation = Quaternion.Euler(10f, 20f, 30f),
                scale = new Vector3(0.5f, 1.5f, 2f),
                visible = false,
                locked = true
            });
            document.Save(path);

            MerkabaDesignDocument loaded = MerkabaDesignDocument.Load(path);
            Assert.That(loaded.instances.Count, Is.EqualTo(1));
            MerkabaDesignInstance instance = loaded.instances[0];
            Assert.That(instance.instanceId, Is.EqualTo(1));
            Assert.That(instance.assetId, Is.EqualTo(new string('a', 64)));
            Assert.That(Vector3.Distance(instance.position,
                new Vector3(1f, 2f, -3f)), Is.LessThan(1e-6f));
            Assert.That(Quaternion.Angle(instance.rotation,
                Quaternion.Euler(10f, 20f, 30f)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(instance.scale,
                new Vector3(0.5f, 1.5f, 2f)), Is.LessThan(1e-6f));
            Assert.That(instance.visible, Is.False);
            Assert.That(instance.locked, Is.True);
            Assert.That(loaded.AllocateInstanceId(), Is.EqualTo(2));
        }

        private static byte[] CreateGlb()
        {
            KernelState state = default;
            state.SetOccupiedForFixture(true, new Color32(80, 140, 220, 255));
            state.Flags = KernelState.SetSurfacePlane(state.Flags,
                new float3(0f, 1f, 0f), 0f);
            var evidence = new Dictionary<int3, KernelState>
            {
                [new int3(0, 0, 0)] = state
            };
            MerkabaExportMembraneResult membrane =
                MerkabaExportMembrane.Build(MerkabaExportShell.Build(evidence));
            using var stream = new MemoryStream();
            _ = MerkabaGlbWriter.Write(stream, membrane);
            return stream.ToArray();
        }
    }
}
