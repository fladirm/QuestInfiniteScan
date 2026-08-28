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
            Assert.That(source, Does.Not.Contain("if ("),
                "Opacity must select material state on CPU, not branch per fragment.");
        }
    }
}
