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
                         "btn-new", "btn-export", "btn-readout", "btn-mesh",
                         "btn-occlusion", "btn-checker", "btn-artifact-view",
                         "btn-annotation-mode", "btn-annotation-save",
                         "btn-annotation-edit", "btn-annotation-delete",
                         "btn-tab-scan", "btn-tab-paint", "btn-tab-plan",
                         "btn-paint-view",
                         "btn-paint-load", "btn-paint-save", "btn-paint-line",
                         "btn-paint-surface", "btn-paint-spatial",
                         "btn-paint-erase", "btn-plan-view", "btn-plan-load",
                         "btn-plan-style", "btn-plan-annotation-mode",
                         "btn-plan-annotation-save", "btn-plan-annotation-edit",
                         "btn-plan-annotation-delete"
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
            Assert.That(root.Q<Label>("val-proximity"), Is.Not.Null);
            Assert.That(root.Q<TextField>("annotation-note"), Is.Not.Null);
            Assert.That(root.Q<Toggle>("artifact-world-lock"), Is.Not.Null);
            Assert.That(root.Q<Toggle>("artifact-room-align"), Is.Not.Null);
            Assert.That(root.Q<Label>("val-artifact"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("scan-panel"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("paint-panel"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("plan-panel"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("paint-color-swatch"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("paint-color-wheel"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("paint-color-cursor"), Is.Not.Null);
            foreach (string slider in new[]
                     {
                         "paint-value", "paint-alpha", "paint-width",
                         "plan-opacity"
                     })
                Assert.That(root.Q<Slider>(slider), Is.Not.Null, slider);
            Assert.That(root.Q<Slider>("paint-red"), Is.Null);
            Assert.That(root.Q<Slider>("paint-green"), Is.Null);
            Assert.That(root.Q<Slider>("paint-blue"), Is.Null);
            string source = File.ReadAllText(Path.GetFullPath(path));
            Assert.That(source, Does.Contain("Published triangles"));
            Assert.That(source, Does.Contain("Visible chunks"));
            string controller = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/UI/DebugMenuController.cs"));
            Assert.That(controller, Does.Contain(
                "_operationProgress.style.display = DisplayStyle.Flex"));
            Assert.That(controller, Does.Contain("Mathf.PingPong("));
            Assert.That(controller, Does.Not.Contain(
                "_operationProgress.style.display = indeterminate"));
            Assert.That(controller, Does.Contain(
                "scanner.DynamicOcclusionEnabled ="));
            Assert.That(controller, Does.Contain(
                "scanner.ReadoutDrawEnabled ="));
            Assert.That(controller, Does.Contain(
                "scanner.MeshReadoutEnabled ="));
            Assert.That(controller, Does.Contain(
                "scanner.CheckerReadoutEnabled ="));
            Assert.That(controller, Does.Contain(
                "TryGetStoredScanProximity"));
            Assert.That(controller, Does.Contain(
                "Outside · {proximityDistance:F1} m"));
            Assert.That(controller, Does.Contain(
                "Color.HSVToRGB(_paintHue, _paintSaturation"));
            Assert.That(controller, Does.Contain(
                "_paintColorWheel.CapturePointer(evt.pointerId)"));
            Assert.That(controller, Does.Not.Contain("_paintRed"));
            Assert.That(controller, Does.Not.Contain("_paintGreen"));
            Assert.That(controller, Does.Not.Contain("_paintBlue"));
            string scanner = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Core/RoomScanner.cs"));
            Assert.That(scanner, Does.Contain("integrationHz = 20f"));
            Assert.That(scanner, Does.Contain(
                "TryGetStoredScanProximity"));
            string renderer = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Merkaba/MerkabaGridRenderer.cs"));
            Assert.That(renderer, Does.Contain(
                "renderer.readoutDrawEnabled &&"));
            Assert.That(renderer, Does.Contain(
                "if (!readoutDrawEnabled || _gpuSubmissionSuspended"));

            string viewer = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/UI/" +
                "MerkabaArtifactViewer.cs"));
            Assert.That(viewer, Does.Contain("paint-surface"));
            Assert.That(viewer, Does.Contain("paint-spatial"));
            Assert.That(viewer, Does.Contain("SurfacePaintPoint"));
            Assert.That(viewer, Does.Contain("WorldToScanPoint"));
            Assert.That(viewer, Does.Contain("public bool PlanViewEnabled"));
            Assert.That(viewer, Does.Contain(
                "_modelMaterial.EnableKeyword(PlanKeyword)"));
            Assert.That(viewer, Does.Not.Contain("KernelState"));

            string follower = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/UI/" +
                "DebugMenuFollower.cs"));
            Assert.That(follower, Does.Contain("menuScale = 0.75f"));
            Assert.That(follower, Does.Contain(
                "controllerRotation * _controllerToPanelRotation"));
            Assert.That(follower, Does.Contain("Quaternion.Slerp"));
            string lateUpdate = follower.Substring(follower.IndexOf(
                "private void LateUpdate()",
                System.StringComparison.Ordinal));
            lateUpdate = lateUpdate.Substring(0, lateUpdate.IndexOf(
                "public void SnapToLeftController()",
                System.StringComparison.Ordinal));
            Assert.That(lateUpdate, Does.Not.Contain("FaceView()"));
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
        public void OpacityUsesOpaqueDepthWritingCoverageInsteadOfBlending()
        {
            const string assetPath =
                "Packages/com.genesis.roomscan/Runtime/Shaders/MerkabaGrid.shader";
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            Assert.That(material.HasProperty("_ScanOpacity"), Is.True);
            Object.DestroyImmediate(material);
            string source = File.ReadAllText(Path.GetFullPath(assetPath));
            Assert.That(source, Does.Contain(
                "Blend One Zero"));
            Assert.That(source, Does.Contain("ZWrite On"));
            Assert.That(source, Does.Contain(
                "#pragma multi_compile_local_fragment _ M8_ALPHA_COVERAGE"));
            Assert.That(source, Does.Contain(
                "clip(_ScanOpacity - coverageThreshold)"));
            Assert.That(source, Does.Contain(
                "return half4(color, 1.0h)"));
            Assert.That(source, Does.Contain(
                "#pragma multi_compile _ XR_HARD_OCCLUSION"));
            Assert.That(source, Does.Contain(
                "#pragma multi_compile_local_fragment _ M8_FINE_PREVIEW"));
            Assert.That(source, Does.Contain(
                "#pragma multi_compile_local_fragment _ M8_ENVIRONMENT_OCCLUSION"));
            Assert.That(source, Does.Contain(
                "if (any(uv < 0.0) || any(uv > 1.0))"));
            Assert.That(source, Does.Contain(
                "#pragma multi_compile_local_fragment _ M8_CHECKER_READOUT"));
            Assert.That(source, Does.Contain(
                "#if defined(M8_FINE_PREVIEW)"));
            Assert.That(source, Does.Contain(
                "#if defined(M8_ENVIRONMENT_OCCLUSION) && defined(XR_HARD_OCCLUSION)"));
            Assert.That(source, Does.Contain(
                "Packages/com.unity.xr.arfoundation/Assets/Shaders/Utils.hlsl"));
            Assert.That(source, Does.Contain(
                "UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);"));
            Assert.That(source, Does.Contain(
                "M8EnvironmentVisibility(input.worldPosition)"));
            Assert.That(source, Does.Contain(
                "clip(M8EnvironmentVisibility(input.worldPosition) - 0.5)"));
            Assert.That(source, Does.Contain(
                "#if defined(M8_CHECKER_READOUT)"));
            Assert.That(source, Does.Contain(
                ": half3(1.0h, 0.0h, 1.0h);"));
            Assert.That(source, Does.Contain(
                "? half3(1.0h, 1.0h, 0.0h)"));
            Assert.That(source, Does.Not.Contain("SampleSH"));
            Assert.That(source, Does.Not.Contain("GetMainLight"));
            Assert.That(source, Does.Not.Contain("normalWS"));
            Assert.That(source, Does.Not.Contain("if (_ScanOpacity"),
                "Opacity must select material state on CPU, not branch per fragment.");
            Assert.That(source, Does.Not.Contain(
                "if (input.colorConfidence == 0u) discard;"));
            Assert.That(source, Does.Not.Contain("barycentric"));
            Assert.That(source, Does.Not.Contain("fwidth"));
            Assert.That(source, Does.Not.Contain("pixelDistance"));
            Assert.That(source, Does.Contain(
                "? input.color : half3(0.55h, 0.16h, 0.42h)"));

            string renderer = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Merkaba/" +
                "MerkabaGridRenderer.cs"));
            Assert.That(renderer, Does.Contain(
                "material.EnableKeyword(\"M8_FINE_PREVIEW\")"));
            Assert.That(renderer, Does.Contain(
                "material.DisableKeyword(\"M8_FINE_PREVIEW\")"));
            Assert.That(renderer, Does.Contain(
                "material.EnableKeyword(\"M8_ENVIRONMENT_OCCLUSION\")"));
            Assert.That(renderer, Does.Contain(
                "material.EnableKeyword(\"M8_ALPHA_COVERAGE\")"));
            Assert.That(renderer, Does.Contain(
                "material.renderQueue = (int)RenderQueue.Geometry"));
            Assert.That(renderer, Does.Not.Contain("BlendMode."));
            Assert.That(renderer, Does.Contain(
                "material.EnableKeyword(\"M8_CHECKER_READOUT\")"));
            Assert.That(renderer, Does.Contain(
                "if (value && meshReadoutEnabled)"));
            Assert.That(renderer, Does.Contain(
                "!material.IsKeywordEnabled(\"M8_STEREO_MESH\")"));
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
                "float3 gridPosition0 : POSITION"));
            Assert.That(source, Does.Contain(
                "float3 gridPosition1 : TEXCOORD0"));
            Assert.That(source, Does.Contain(
                "unity_StereoEyeIndex == 0"));
            Assert.That(source, Does.Not.Contain("SV_VertexID"));
            Assert.That(source, Does.Not.Contain("StructuredBuffer"));
            Assert.That(source, Does.Not.Contain("logicalPrimitive"));
            Assert.That(source, Does.Not.Contain("primitiveId"));
            Assert.That(source, Does.Not.Contain(
                "_MerkabaPrimitiveCapacityPerChunk"));
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
