// FinalScan host: URP pipeline + renderer assets for Quest (4x MSAA, no HDR, no render feature).
// Port (renamed, minimized) of the donor URP setup; MIGRATION_LEDGER §2. Idempotent.

using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using FinalScan.Render;

namespace FinalScan.Editor
{
    internal static class FinalScanUrpSetup
    {
        const string Tag = "[FinalScan]";
        const string SettingsDir = "Assets/Settings";
        const string PipelinePath = SettingsDir + "/URP-Pipeline.asset";
        const string RendererPath = SettingsDir + "/URP-Renderer.asset";

        /// <summary>
        /// Ensures URP-Pipeline.asset + URP-Renderer.asset exist, carry Quest defaults, and are
        /// assigned to GraphicsSettings and every quality level.
        /// </summary>
        public static UniversalRenderPipelineAsset Ensure()
        {
            if (!Directory.Exists(SettingsDir)) Directory.CreateDirectory(SettingsDir);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = CreatePipelineAsset();
                Debug.Log($"{Tag} Created {PipelinePath} (+ renderer).");
            }

            ApplyQuestDefaults(pipeline);

            if (GraphicsSettings.defaultRenderPipeline != pipeline)
            {
                GraphicsSettings.defaultRenderPipeline = pipeline;
                Debug.Log($"{Tag} GraphicsSettings.defaultRenderPipeline = {PipelinePath}");
            }
            AssignToAllQualityLevels(pipeline);
            EnsureRenderFeature();
            AssetDatabase.SaveAssets();
            return pipeline;
        }

        /// <summary>
        /// Adds FinalScanRenderFeature to URP-Renderer.asset as a sub-asset (the same SerializedObject path the URP
        /// renderer inspector uses: m_RendererFeatures + m_RendererFeatureMap local ids) and assigns its depth-copy shader.
        /// </summary>
        public static FinalScanRenderFeature EnsureRenderFeature()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null) throw new InvalidOperationException(RendererPath + " is not a UniversalRendererData");
            FinalScanRenderFeature feature = null;
            foreach (ScriptableRendererFeature f in rendererData.rendererFeatures)
                if (f is FinalScanRenderFeature existing) { feature = existing; break; }
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<FinalScanRenderFeature>();
                feature.name = nameof(FinalScanRenderFeature);
                AssetDatabase.AddObjectToAsset(feature, rendererData);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                var serialized = new SerializedObject(rendererData);
                SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
                SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(rendererData);
                Debug.Log($"{Tag} Added FinalScanRenderFeature to {RendererPath}.");
            }
            FinalScanHostSetup.AssignShader(feature, "depthCopyShader", FinalScanHostSetup.DepthCopyShaderPath);
            EditorUtility.SetDirty(feature);
            return feature;
        }

        static UniversalRenderPipelineAsset CreatePipelineAsset()
        {
            // Mirrors Assets > Create > Rendering > URP Asset (with Universal Renderer).
            // CreateRendererAsset is internal, hence reflection.
            MethodInfo createRenderer = typeof(UniversalRenderPipelineAsset).GetMethod(
                "CreateRendererAsset", BindingFlags.NonPublic | BindingFlags.Static);
            if (createRenderer == null)
                throw new MissingMethodException("UniversalRenderPipelineAsset.CreateRendererAsset not found (URP API moved).");

            ParameterInfo[] parameters = createRenderer.GetParameters();
            ScriptableRendererData rendererData = parameters.Length switch
            {
                4 => (ScriptableRendererData)createRenderer.Invoke(null,
                    new object[] { RendererPath, RendererType.UniversalRenderer, false, "Renderer" }),
                2 => (ScriptableRendererData)createRenderer.Invoke(null,
                    new object[] { PipelinePath, RendererType.UniversalRenderer }),
                _ => throw new MissingMethodException($"Unexpected CreateRendererAsset arity {parameters.Length}."),
            };
            if (rendererData == null) throw new InvalidOperationException("URP renderer asset creation returned null.");

            var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(pipeline, PipelinePath);
            return pipeline;
        }

        // 4x MSAA is nearly free on Adreno; HDR off saves bandwidth. SerializedObject avoids internal setters.
        static void ApplyQuestDefaults(UniversalRenderPipelineAsset asset)
        {
            var serialized = new SerializedObject(asset);
            bool changed = false;
            SetInt(serialized, "m_MSAA", 4, ref changed);
            SetBool(serialized, "m_SupportsHDR", false, ref changed);
            SetInt(serialized, "m_ShadowCascadeCount", 1, ref changed);
            SetBool(serialized, "m_SoftShadowsSupported", false, ref changed);
            SetBool(serialized, "m_UseSRPBatcher", true, ref changed);
            if (!changed) return;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            Debug.Log($"{Tag} Applied Quest URP defaults (4x MSAA, no HDR, 1 cascade, no soft shadows).");
        }

        static void SetInt(SerializedObject serialized, string name, int value, ref bool changed)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null && property.intValue != value) { property.intValue = value; changed = true; }
        }

        static void SetBool(SerializedObject serialized, string name, bool value, ref bool changed)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null && property.boolValue != value) { property.boolValue = value; changed = true; }
        }

        static void AssignToAllQualityLevels(UniversalRenderPipelineAsset pipeline)
        {
            int previous = QualitySettings.GetQualityLevel();
            try
            {
                int touched = 0;
                for (int i = 0; i < QualitySettings.names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, applyExpensiveChanges: false);
                    if (QualitySettings.renderPipeline == pipeline) continue;
                    QualitySettings.renderPipeline = pipeline;
                    touched++;
                }
                if (touched > 0) Debug.Log($"{Tag} Assigned URP pipeline to {touched} quality level(s).");
            }
            finally
            {
                QualitySettings.SetQualityLevel(previous, applyExpensiveChanges: false);
            }
        }
    }
}
