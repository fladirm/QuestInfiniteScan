using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaPaintEngineTests
    {
        private string _directory;
        private GameObject _roomObject;
        private GameObject _engineObject;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(),
                "merkaba-paint-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (_engineObject != null)
                UnityEngine.Object.DestroyImmediate(_engineObject);
            if (_roomObject != null)
                UnityEngine.Object.DestroyImmediate(_roomObject);
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }

        [Test]
        public void SpatialBrushAndSurfaceSpacingHaveFixedPhysicalMeaning()
        {
            var ray = new Ray(new Vector3(1f, 2f, 3f), Vector3.forward);
            Assert.That(Vector3.Distance(
                    MerkabaPaintEngine.SpatialBrushPoint(ray),
                    ray.origin + Vector3.forward * 0.20f),
                Is.LessThan(1e-6f));
            Assert.That(MerkabaPaintEngine.SurfaceSampleSpacing(0.05f),
                Is.EqualTo(0.01f).Within(1e-6f));
        }

        [Test]
        public void SprayIsDeterministicAndRendersDisconnectedDabs()
        {
            Vector3 first = MerkabaPaintEngine.DeterministicSprayOffset(
                17u, 3u, Vector3.right, Vector3.up, Vector3.forward, 0.1f,
                MerkabaBrushShape.Round);
            Vector3 second = MerkabaPaintEngine.DeterministicSprayOffset(
                17u, 3u, Vector3.right, Vector3.up, Vector3.forward, 0.1f,
                MerkabaBrushShape.Round);
            Assert.That(Vector3.Distance(first, second), Is.LessThan(1e-7f));

            MerkabaPaintEngine engine = OpenEngine();
            engine.BeginStroke(MerkabaDesignTool.Spray, Settings());
            Assert.That(engine.AddSpray(Vector3.zero, Vector3.forward, 1f,
                10f, 0.05f), Is.EqualTo(10));
            Assert.That(engine.CommitStroke(), Is.True);

            Mesh mesh = _roomObject.GetComponentInChildren<MeshFilter>().
                sharedMesh;
            Assert.That(mesh.vertexCount, Is.EqualTo(10 * 6));
            Assert.That(mesh.GetIndexCount(0), Is.EqualTo(10u * 24u));
        }

        [Test]
        public void LocalEraserSplitsStrokeAndPreservesBothOuterRuns()
        {
            MerkabaPaintEngine engine = OpenEngine();
            engine.BeginStroke(MerkabaDesignTool.SpatialBrush, Settings());
            for (int x = -2; x <= 2; x++)
                engine.AddSample(new Vector3(x * 0.1f, 0f, 0f),
                    Vector3.zero, false);
            Assert.That(engine.CommitStroke(), Is.True);

            Assert.That(engine.EraseSphere(Vector3.zero, 0.02f),
                Is.EqualTo(1));
            Assert.That(engine.StrokeCount, Is.EqualTo(2));
            Assert.That(engine.Save(), Is.True);

            MerkabaDesignDocument stored = MerkabaDesignDocument.Load(
                DesignPath());
            Assert.That(stored.strokes.Count, Is.EqualTo(2));
            Assert.That(stored.strokes[0].samples.Count,
                Is.EqualTo(2));
            Assert.That(stored.strokes[1].samples.Count,
                Is.EqualTo(2));
            Assert.That(stored.strokes[0].samples[^1].position.x,
                Is.LessThan(0f));
            Assert.That(stored.strokes[1].samples[0].position.x,
                Is.GreaterThan(0f));
        }

        [Test]
        public void DesignDocumentRoundTripsWithoutChangingStoredGeometry()
        {
            var document = new MerkabaDesignDocument();
            document.strokes.Add(new MerkabaDesignStroke
            {
                id = document.AllocateStrokeId(),
                tool = MerkabaDesignTool.SurfaceBrush,
                color = new Color(0.2f, 0.4f, 0.8f, 0.6f),
                opacity = 0.6f,
                flow = 0.7f,
                hardness = 0.9f,
                saturation = 0.8f,
                radius = 0.03f,
                shape = MerkabaBrushShape.Square,
                samples = new List<MerkabaDesignSample>
                {
                    new()
                    {
                        position = new Vector3(-1f, 2f, 3f),
                        normal = Vector3.up,
                        hasNormal = true,
                        radius = 0.03f
                    }
                }
            });
            string first = DesignPath();
            string second = Path.Combine(_directory, "copy.json");
            document.Save(first);
            MerkabaDesignDocument loaded = MerkabaDesignDocument.Load(first);
            loaded.Save(second);

            Assert.That(File.ReadAllText(second), Is.EqualTo(
                File.ReadAllText(first)));
        }

        private MerkabaPaintEngine OpenEngine()
        {
            _roomObject = new GameObject("Room");
            _engineObject = new GameObject("Paint Engine");
            MerkabaPaintEngine engine =
                _engineObject.AddComponent<MerkabaPaintEngine>();
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Packages/com.genesis.roomscan/Runtime/Shaders/" +
                "MerkabaArtifactPreview.shader");
            Assert.That(shader, Is.Not.Null);
            engine.Open(_roomObject.transform, shader, DesignPath());
            return engine;
        }

        private string DesignPath() =>
            Path.Combine(_directory, MerkabaSessionCatalog.DesignFileName);

        private static MerkabaPaintSettings Settings() => new(
            new Color(0.1f, 0.8f, 1f, 0.8f), 0.8f, 1f, 0.75f, 1f,
            0.01f, MerkabaBrushShape.Round);
    }
}
