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
        public void ProductionMenuSeparatesWorkflowsDiagnosticsAndInputAuthority()
        {
            const string path =
                "Packages/com.genesis.roomscan/Runtime/UI/DebugMenu.uxml";
            VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.That(asset, Is.Not.Null);
            TemplateContainer root = asset.CloneTree();

            foreach (string button in new[]
                     {
                         "btn-start", "btn-save", "btn-save-as", "btn-load",
                         "btn-new", "btn-rename", "btn-delete-session",
                         "btn-export", "btn-export-tiles", "btn-readout",
                         "btn-mesh", "btn-occlusion", "btn-checker",
                         "btn-artifact-view", "btn-artifact-load",
                         "btn-annotation-mode", "btn-annotation-save",
                         "btn-annotation-edit", "btn-annotation-delete",
                         "btn-tab-scan", "btn-tab-refine", "btn-tab-design",
                         "btn-tab-view", "btn-fine-refine", "btn-fine-erase",
                         "btn-paint-view",
                         "btn-paint-load", "btn-paint-save", "btn-paint-brush",
                         "btn-paint-line", "btn-paint-surface",
                         "btn-paint-spatial", "btn-paint-spray",
                         "btn-paint-erase", "btn-paint-eyedropper",
                         "btn-paint-round", "btn-paint-square",
                         "btn-design-paint", "btn-design-objects",
                         "btn-object-import", "btn-object-place",
                         "btn-object-select", "btn-object-duplicate",
                         "btn-object-visible", "btn-object-lock",
                         "btn-object-delete", "btn-design-undo",
                         "btn-design-redo",
                         "btn-save-swatch", "btn-plan-model", "btn-plan-style"
                     })
                Assert.That(root.Q<Button>(button), Is.Not.Null, button);

            Slider opacity = root.Q<Slider>("scan-opacity");
            Assert.That(opacity, Is.Not.Null);
            Assert.That(opacity.lowValue, Is.EqualTo(0f));
            Assert.That(opacity.highValue, Is.EqualTo(1f));
            Assert.That(opacity.value, Is.EqualTo(1f));
            Assert.That(root.Q<Label>("operation-spinner"), Is.Null);
            Assert.That(root.Q<Label>("operation-stage"), Is.Not.Null);
            Assert.That(root.Q<ProgressBar>("operation-progress"), Is.Not.Null);
            Assert.That(root.Q<Label>("val-proximity"), Is.Not.Null);
            Assert.That(root.Q<TextField>("export-name"), Is.Not.Null);
            Assert.That(root.Q<TextField>("annotation-note"), Is.Not.Null);
            Assert.That(root.Q<Toggle>("artifact-world-lock"), Is.Not.Null);
            Assert.That(root.Q<Toggle>("artifact-room-align"), Is.Not.Null);
            Assert.That(root.Q<Label>("val-artifact"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("scan-panel"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("refine-panel"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("design-panel"), Is.Not.Null);
            Assert.That(root.Q<ScrollView>("design-panel"), Is.Null,
                "The compact DESIGN workspace must not hide controls in a " +
                "scroll viewport.");
            Assert.That(root.Q<VisualElement>("view-panel"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("paint-color-swatch"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("paint-color-wheel"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("paint-color-cursor"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("paint-workspace"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("objects-workspace"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("design-history-actions"),
                Is.Not.Null);
            Assert.That(root.Q<DropdownField>("object-asset-picker"),
                Is.Not.Null);
            Assert.That(root.Q<DropdownField>("object-instance-picker"),
                Is.Not.Null);
            Assert.That(root.Q<Toggle>("object-surface-snap"), Is.Not.Null);
            Assert.That(root.Q<Toggle>("object-upright-snap"), Is.Not.Null);
            Assert.That(root.Q<Toggle>("object-grid-snap"), Is.Not.Null);
            foreach (string slider in new[]
                     {
                         "paint-value", "paint-alpha", "paint-width",
                         "paint-flow", "paint-hardness", "paint-saturation",
                         "paint-density", "paint-scatter",
                         "fine-radius", "fine-length"
                     })
                Assert.That(root.Q<Slider>(slider), Is.Not.Null, slider);
            Assert.That(root.Q<Slider>("paint-red"), Is.Null);
            Assert.That(root.Q<Slider>("paint-green"), Is.Null);
            Assert.That(root.Q<Slider>("paint-blue"), Is.Null);
            string source = File.ReadAllText(Path.GetFullPath(path));
            Assert.That(source, Does.Contain("Published triangles"));
            Assert.That(source, Does.Contain("Visible chunks"));
            Foldout diagnostics = root.Q<Foldout>("diagnostics-foldout");
            Assert.That(diagnostics, Is.Not.Null);
            Assert.That(diagnostics.value, Is.False);
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
            Assert.That(controller, Does.Contain(
                "_paintColorWheel.ReleasePointer(evt.pointerId)"));
            Assert.That(controller, Does.Contain("RefreshSessionChoices()"));
            Assert.That(controller, Does.Contain("MenuTab.Refine"));
            Assert.That(controller, Does.Contain("MenuTab.Design"));
            Assert.That(controller, Does.Contain("MenuTab.View"));
            Assert.That(controller, Does.Not.Contain("SpinnerFrames"));
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
            Assert.That(viewer, Does.Contain("MerkabaPaintEngine"));
            Assert.That(viewer, Does.Contain(
                "MerkabaPaintEngine.SpatialBrushPoint(ray)"));
            Assert.That(viewer, Does.Contain("AppendProjectedSurfaceSamples"));
            Assert.That(viewer, Does.Contain("EraseSphere(center"));
            Assert.That(viewer, Does.Contain("SurfacePaintPoint"));
            Assert.That(viewer, Does.Contain("WorldToScanPoint"));
            Assert.That(viewer, Does.Contain("public bool PlanViewEnabled"));
            Assert.That(viewer, Does.Contain(
                "_modelMaterial.EnableKeyword(PlanKeyword)"));
            Assert.That(viewer, Does.Contain(
                "MerkabaArtifactPaintTool.Eyedropper"));
            Assert.That(viewer, Does.Contain(
                "MerkabaArtifactPaintTool.Spray"));
            Assert.That(viewer, Does.Contain("TryInterpolateVertexColor"));
            Assert.That(viewer, Does.Contain("RequestDesignAssetFromDisk"));
            Assert.That(viewer, Does.Contain("ContinueTwoHandGrab"));
            Assert.That(viewer, Does.Contain("ObjectInputEnabled"));
            Assert.That(viewer, Does.Not.Contain("_spatialPaintDistance"));
            Assert.That(viewer, Does.Not.Contain("_paintDraftLine"));
            Assert.That(viewer, Does.Not.Contain("KernelState"));
            int viewerUiGate = viewer.IndexOf(
                "if (_rayDriver != null && _rayDriver.IsPointingAtUi)",
                System.StringComparison.Ordinal);
            int viewerGrip = viewer.IndexOf("bool rightGrip = OVRInput.Get(",
                System.StringComparison.Ordinal);
            Assert.That(viewerUiGate, Is.GreaterThanOrEqualTo(0));
            Assert.That(viewerGrip, Is.GreaterThan(viewerUiGate));

            string rayDriver = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/UI/" +
                "ControllerRayDriver.cs"));
            Assert.That(rayDriver, Does.Contain("LayerMask.GetMask(\"UI\")"));
            Assert.That(rayDriver, Does.Not.Contain(
                "LayerMask.GetMask(\"Default\", \"UI\")"));
            Assert.That(rayDriver, Does.Contain("_uiTriggerCaptured"));
            Assert.That(rayDriver, Does.Contain(
                "[DefaultExecutionOrder(-1000)]"));
            Assert.That(rayDriver, Does.Contain("RefreshUiAuthority();"));
            string scanInput = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/" +
                "RoomScanInputHandler.cs"));
            Assert.That(scanInput, Does.Contain("uiOwnsTrigger"));
            Assert.That(scanInput, Does.Contain("scanner.FineEraseSelected"));
            string documentRaycaster = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/UI/" +
                "VRDocumentRaycaster.cs"));
            Assert.That(documentRaycaster, Does.Contain(
                "LayerMask.NameToLayer(\"UI\")"));
            Assert.That(documentRaycaster, Does.Not.Contain(
                "private LayerMask interactionLayers = ~0"));

            string stylesheet = File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/UI/DebugMenu.uss"));
            Assert.That(stylesheet, Does.Contain("width: 196px"));
            Assert.That(stylesheet, Does.Contain("min-height: 54px"));
            Assert.That(stylesheet, Does.Contain("width: 580px"));
            Assert.That(stylesheet, Does.Contain(".paint-tool-strip"));
            Assert.That(stylesheet, Does.Not.Contain(".design-scroll"));

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
                "float3 gridPosition : POSITION"));
            Assert.That(source, Does.Contain(
                "half4 packedColor : COLOR"));
            Assert.That(source, Does.Contain(
                "input.vertexID + unity_StereoEyeIndex *"));
            Assert.That(source, Does.Contain("uint vertexID : SV_VertexID"));
            Assert.That(source, Does.Contain(
                "StructuredBuffer<MerkabaReadoutVertex> _M8ReadoutVertices"));
            Assert.That(source, Does.Contain(
                "#if defined(M8_STEREO_MESH)"));
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
