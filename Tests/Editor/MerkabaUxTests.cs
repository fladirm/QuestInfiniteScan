using System.IO;
using Genesis.RoomScan;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaUxTests
    {
        [Test]
        public void MenuContainsAllActionsOpacityAndOperationFeedback()
        {
            const string path =
                "Packages/com.genesis.roomscan/Runtime/UI/DebugMenu.uxml";
            VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.That(asset, Is.Not.Null);
            TemplateContainer root = asset.CloneTree();

            foreach (string button in new[]
                     {
                         "btn-start", "btn-stop", "btn-save", "btn-load",
                         "btn-new", "btn-export"
                     })
                Assert.That(root.Q<Button>(button), Is.Not.Null, button);

            Slider opacity = root.Q<Slider>("scan-opacity");
            Assert.That(opacity, Is.Not.Null);
            Assert.That(opacity.lowValue, Is.EqualTo(0f));
            Assert.That(opacity.highValue, Is.EqualTo(1f));
            Assert.That(opacity.value, Is.EqualTo(1f));
            Assert.That(root.Q<Label>("operation-spinner"), Is.Not.Null);
            Assert.That(root.Q<Label>("operation-stage"), Is.Not.Null);
            Assert.That(root.Q<ProgressBar>("operation-progress"), Is.Not.Null);
            string source = File.ReadAllText(Path.GetFullPath(path));
            Assert.That(source, Does.Contain("Published triangles"));
            Assert.That(source, Does.Contain("Visible chunks"));
        }

        [Test]
        public void OperationStateDistinguishesSpinnerFromMeasuredProgress()
        {
            var spinner = new ScanOperationState(ScanOperationKind.ExportGlb,
                ScanOperationStage.SynchronizingScan, -1f, true, "Sync");
            var progress = new ScanOperationState(ScanOperationKind.Save,
                ScanOperationStage.WritingFile, 2f, true, "Write");

            Assert.That(spinner.IsIndeterminate, Is.True);
            Assert.That(progress.IsIndeterminate, Is.False);
            Assert.That(progress.Progress01, Is.EqualTo(1f));
        }

        [Test]
        public void OpacityShaderHasNoDynamicBranch()
        {
            const string assetPath =
                "Packages/com.genesis.roomscan/Runtime/Shaders/MerkabaGrid.shader";
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            Assert.That(material.HasProperty("_ScanOpacity"), Is.True);
            Object.DestroyImmediate(material);
            string source = File.ReadAllText(Path.GetFullPath(assetPath));
            Assert.That(source, Does.Contain("Blend [_SrcBlend] [_DstBlend]"));
            Assert.That(source, Does.Contain("_ScanOpacity)"));
            Assert.That(source, Does.Contain(
                "return half4(input.color, _ScanOpacity);"));
            Assert.That(source, Does.Not.Contain("SampleSH"));
            Assert.That(source, Does.Not.Contain("GetMainLight"));
            Assert.That(source, Does.Not.Contain("normalWS"));
            Assert.That(source, Does.Not.Contain("if (_ScanOpacity"),
                "Opacity must select material state on CPU, not branch per fragment.");
            Assert.That(source, Does.Contain(
                "if (input.colorConfidence == 0u) discard;"));
        }

        [Test]
        public void LiveShaderUsesUnitySinglePassInstancedContract()
        {
            const string assetPath =
                "Packages/com.genesis.roomscan/Runtime/Shaders/MerkabaGrid.shader";
            string source = File.ReadAllText(Path.GetFullPath(assetPath));

            Assert.That(source, Does.Contain("UNITY_VERTEX_INPUT_INSTANCE_ID"));
            Assert.That(source, Does.Contain("UNITY_SETUP_INSTANCE_ID(input)"));
            Assert.That(source, Does.Contain("UNITY_VERTEX_OUTPUT_STEREO"));
            Assert.That(source, Does.Contain(
                "UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output)"));
            Assert.That(source, Does.Contain(
                "uint primitiveInstanceID = unity_InstanceID"));
            Assert.That(source, Does.Contain(
                "_MerkabaPrimitiveCapacityPerChunk + primitiveInstanceID"));
            Assert.That(source, Does.Not.Contain(
                "uint instanceID : SV_InstanceID"));
        }

        [Test]
        public void SaveAndExportAreNotGatedByStaleCpuChunkCount()
        {
            string source = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/UI/DebugMenuController.cs"));

            Assert.That(source, Does.Contain("_save?.SetEnabled(!busy)"));
            Assert.That(source, Does.Contain("_export?.SetEnabled(!busy)"));
            Assert.That(source, Does.Not.Contain(
                "scanner.ActiveChunkCount > 0"));
        }
    }
}
