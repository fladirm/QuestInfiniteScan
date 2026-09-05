using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
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

        [Test]
        public void PlacementUsesRoomCoordinatesAndDuplicateSharesDecodedMesh()
        {
            string source = Path.Combine(_directory, "fixture.glb");
            File.WriteAllBytes(source, CreateGlb());
            var library = new MerkabaDesignLibrary(Path.Combine(_directory,
                "library"));
            MerkabaDesignAsset asset = library.Import(source);
            var document = new MerkabaDesignDocument();
            var room = new GameObject("Room");
            room.transform.SetPositionAndRotation(new Vector3(4f, 1f, -2f),
                Quaternion.Euler(0f, 35f, 0f));
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Packages/com.genesis.roomscan/Runtime/Shaders/" +
                "MerkabaArtifactPreview.shader");
            Assert.That(shader, Is.Not.Null);
            try
            {
                library.Open(document, room.transform, shader, () => { });
                Assert.That(library.SelectAsset(asset.id), Is.True);
                library.SetPlacementEnabled(true);
                var ray = new Ray(new Vector3(1f, 2f, 3f),
                    Vector3.forward);
                library.UpdatePlacementPreview(ray, false, default, default,
                    false, true, false);
                Assert.That(library.PlaceSelected(), Is.True);
                Assert.That(document.instances.Count, Is.EqualTo(1));
                Assert.That(Vector3.Distance(document.instances[0].position,
                    room.transform.InverseTransformPoint(ray.GetPoint(0.50f))),
                    Is.LessThan(1e-5f));

                Assert.That(library.DuplicateSelected(), Is.True);
                MeshFilter[] filters = room.GetComponentsInChildren<
                    MeshFilter>();
                Assert.That(filters.Length, Is.EqualTo(2));
                Assert.That(filters[0].sharedMesh,
                    Is.SameAs(filters[1].sharedMesh));

                Transform original = room.transform.Find(
                    "Merkaba Design Objects/Design Object 1");
                Assert.That(original, Is.Not.Null);
                Vector3 before = original.position;
                Vector3 delta = new(0.3f, -0.1f, 0.2f);
                room.transform.position += delta;
                Assert.That(Vector3.Distance(original.position, before + delta),
                    Is.LessThan(1e-5f));

                Assert.That(library.SelectInstance(1), Is.True);
                Assert.That(library.ToggleSelectedLocked(), Is.True);
                Assert.That(library.ContinueOneHandGrab(Vector3.zero,
                    Quaternion.identity), Is.False);
                Assert.That(library.ToggleSelectedLocked(), Is.True);
                Assert.That(library.ContinueOneHandGrab(Vector3.zero,
                    Quaternion.identity), Is.True);
                library.EndGrab(true);
            }
            finally
            {
                library.CloseRuntime();
                UnityEngine.Object.DestroyImmediate(room);
            }
        }

        [Test]
        public void ObjectChangesUseSharedUndoAndCanceledGrabRestoresSnapshot()
        {
            string source = Path.Combine(_directory, "fixture.glb");
            File.WriteAllBytes(source, CreateGlb());
            var library = new MerkabaDesignLibrary(Path.Combine(_directory,
                "library"));
            MerkabaDesignAsset asset = library.Import(source);
            var room = new GameObject("Room");
            var engineObject = new GameObject("Paint Engine");
            MerkabaPaintEngine engine =
                engineObject.AddComponent<MerkabaPaintEngine>();
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Packages/com.genesis.roomscan/Runtime/Shaders/" +
                "MerkabaArtifactPreview.shader");
            Assert.That(shader, Is.Not.Null);
            try
            {
                engine.Open(room.transform, shader, Path.Combine(_directory,
                    "design.json"));
                library.Open(engine.Document, room.transform, shader,
                    engine.MarkDocumentChanged, engine.BeginDocumentChange,
                    engine.CommitDocumentChange,
                    engine.RollbackDocumentChange);
                library.SelectAsset(asset.id);
                library.SetPlacementEnabled(true);
                var ray = new Ray(Vector3.zero, Vector3.forward);
                library.UpdatePlacementPreview(ray, false, default, default,
                    false, true, false);
                Assert.That(library.PlaceSelected(), Is.True);
                Assert.That(engine.Document.instances.Count, Is.EqualTo(1));
                Assert.That(engine.Undo(), Is.True);
                library.RefreshInstances();
                Assert.That(engine.Document.instances, Is.Empty);
                Assert.That(engine.Redo(), Is.True);
                library.RefreshInstances();
                Assert.That(engine.Document.instances.Count, Is.EqualTo(1));

                Assert.That(library.SelectInstance(1), Is.True);
                Vector3 original = engine.Document.instances[0].position;
                Assert.That(library.ContinueOneHandGrab(Vector3.zero,
                    Quaternion.identity), Is.True);
                Assert.That(library.ContinueOneHandGrab(Vector3.right * 0.3f,
                    Quaternion.identity), Is.True);
                Assert.That(Vector3.Distance(
                    engine.Document.instances[0].position, original),
                    Is.GreaterThan(0.1f));
                library.EndGrab(false);
                Assert.That(Vector3.Distance(
                    engine.Document.instances[0].position, original),
                    Is.LessThan(1e-6f));
            }
            finally
            {
                library.CloseRuntime();
                UnityEngine.Object.DestroyImmediate(engineObject);
                UnityEngine.Object.DestroyImmediate(room);
            }
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
