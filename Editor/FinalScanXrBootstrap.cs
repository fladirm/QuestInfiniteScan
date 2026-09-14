// FinalScan host: project-level XR configuration for Quest 3 / Quest 3S.
// Port (renamed, minimized) of the donor VR project bootstrap; MIGRATION_LEDGER §2.
// Idempotent: every Fix is safe to rerun. Batch and window callers share FixAllAsync.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;

namespace FinalScan.Editor
{
    internal sealed class XrCheck
    {
        public string Id;
        public Func<string> Current;
        public Func<bool> IsOk;
        public Action Fix;
    }

    internal static class FinalScanXrBootstrap
    {
        const string Tag = "[FinalScan]";

        // OpenXR feature ids (string constants; a missing feature is reported, not thrown).
        const string FeatureMetaXr = "com.meta.openxr.feature.metaxr";
        const string FeatureMetaQuest = "com.unity.openxr.feature.metaquest";
        const string FeatureFoveation = "com.meta.openxr.feature.foveation";
        const string FeatureOculusTouch = "com.unity.openxr.feature.input.oculustouch";
        const string FeatureQuestTouchPlus = "com.unity.openxr.feature.input.metaquestplus";
        const string FeatureQuestTouchPro = "com.unity.openxr.feature.input.metaquestpro";
        const string FeatureArCamera = "com.unity.openxr.feature.arfoundation-meta-camera";
        const string FeatureArOcclusion = "com.unity.openxr.feature.arfoundation-meta-occlusion";
        const string FeatureArSession = "com.unity.openxr.feature.arfoundation-meta-session";
        const string OpenXrLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";

        public static IReadOnlyList<XrCheck> Checks { get; } = BuildChecks();

        public static void Audit()
        {
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
        }

        /// <summary>Applies every failing check, then Meta's own OVRProjectSetup sweep.</summary>
        public static async Task FixAllAsync()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Cannot configure XR while Play mode is active.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException(
                    "Active build target must be Android (run Unity with -buildTarget Android).");

            Audit();
            int fixedCount = 0;
            foreach (XrCheck check in Checks)
            {
                if (check.IsOk()) continue;
                try
                {
                    check.Fix();
                    fixedCount++;
                    Debug.Log($"{Tag} XR fix: {check.Id} -> {check.Current()}");
                }
                catch (Exception exception)
                {
                    Debug.LogError($"{Tag} XR fix failed for {check.Id}: {exception}");
                }
            }

            try
            {
                await OVRProjectSetup.FixAllAsync(BuildTargetGroup.Android);
                Debug.Log($"{Tag} OVRProjectSetup.FixAllAsync(Android) done.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{Tag} OVRProjectSetup.FixAllAsync raised {exception.GetType().Name}: {exception.Message}");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} XR bootstrap done, fixed={fixedCount}, checks={Checks.Count}.");
        }

        static List<XrCheck> BuildChecks() => new List<XrCheck>
        {
            new XrCheck
            {
                Id = "player.il2cpp",
                Current = () => PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android).ToString(),
                IsOk = () => PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) == ScriptingImplementation.IL2CPP,
                Fix = () => PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP),
            },
            new XrCheck
            {
                Id = "player.arm64",
                Current = () => PlayerSettings.Android.targetArchitectures.ToString(),
                IsOk = () => PlayerSettings.Android.targetArchitectures == AndroidArchitecture.ARM64,
                Fix = () => PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64,
            },
            new XrCheck
            {
                Id = "player.minsdk32",
                Current = () => ((int)PlayerSettings.Android.minSdkVersion).ToString(),
                IsOk = () => (int)PlayerSettings.Android.minSdkVersion >= 32,
                Fix = () => PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32,
            },
            new XrCheck
            {
                Id = "player.vulkanOnly",
                Current = () => string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android)),
                IsOk = () =>
                {
                    if (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android)) return false;
                    GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
                    return apis.Length == 1 && apis[0] == GraphicsDeviceType.Vulkan;
                },
                Fix = () =>
                {
                    PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                    PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
                },
            },
            new XrCheck
            {
                Id = "player.stereoInstancing",
                Current = () => PlayerSettings.stereoRenderingPath.ToString(),
                IsOk = () => PlayerSettings.stereoRenderingPath == StereoRenderingPath.Instancing,
                Fix = () => PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing,
            },
            new XrCheck
            {
                // Adreno decodes ASTC in hardware; ETC2 is software-expanded.
                Id = "player.astc",
                Current = () => string.Join(",", PlayerSettings.Android.textureCompressionFormats ?? Array.Empty<TextureCompressionFormat>()),
                IsOk = () =>
                {
                    TextureCompressionFormat[] formats = PlayerSettings.Android.textureCompressionFormats;
                    return formats != null && formats.Length == 1 && formats[0] == TextureCompressionFormat.ASTC;
                },
                Fix = () => PlayerSettings.Android.textureCompressionFormats = new[] { TextureCompressionFormat.ASTC },
            },
            new XrCheck
            {
                Id = "xr.loader.openxr",
                Current = () => DescribeLoaders(BuildTargetGroup.Android),
                IsOk = () => GetXrManager(BuildTargetGroup.Android)?.activeLoaders?
                    .Any(loader => loader != null && loader.GetType().FullName == OpenXrLoaderType) == true,
                Fix = () => EnsureOpenXrLoader(BuildTargetGroup.Android),
            },
            FeatureCheck("openxr.metaxr", FeatureMetaXr),
            FeatureCheck("openxr.metaquest", FeatureMetaQuest),
            FeatureCheck("openxr.foveation", FeatureFoveation),
            FeatureCheck("openxr.touch.oculus", FeatureOculusTouch),
            FeatureCheck("openxr.touch.plus", FeatureQuestTouchPlus),
            FeatureCheck("openxr.touch.pro", FeatureQuestTouchPro),
            FeatureCheck("openxr.ar.camera", FeatureArCamera),
            FeatureCheck("openxr.ar.session", FeatureArSession),
            FeatureCheck("openxr.ar.occlusion", FeatureArOcclusion),
            OvrConfigCheck("ovr.devices.quest3",
                config => string.Join(",", config.targetDeviceTypes),
                config => config.targetDeviceTypes.Contains(OVRProjectConfig.DeviceType.Quest3)
                          && config.targetDeviceTypes.Contains(OVRProjectConfig.DeviceType.Quest3S),
                config =>
                {
                    if (!config.targetDeviceTypes.Contains(OVRProjectConfig.DeviceType.Quest3))
                        config.targetDeviceTypes.Add(OVRProjectConfig.DeviceType.Quest3);
                    if (!config.targetDeviceTypes.Contains(OVRProjectConfig.DeviceType.Quest3S))
                        config.targetDeviceTypes.Add(OVRProjectConfig.DeviceType.Quest3S);
                }),
            OvrConfigCheck("ovr.scene",
                config => config.sceneSupport.ToString(),
                config => config.sceneSupport >= OVRProjectConfig.FeatureSupport.Supported,
                config => config.sceneSupport = OVRProjectConfig.FeatureSupport.Supported),
            OvrConfigCheck("ovr.passthrough",
                config => config.insightPassthroughSupport.ToString(),
                config => config.insightPassthroughSupport >= OVRProjectConfig.FeatureSupport.Supported,
                config => config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported),
            OvrConfigCheck("ovr.anchors",
                config => config.anchorSupport.ToString(),
                config => config.anchorSupport != OVRProjectConfig.AnchorSupport.Disabled,
                config => config.anchorSupport = OVRProjectConfig.AnchorSupport.Enabled),
            OvrConfigCheck("ovr.passthroughCameraAccess",
                config => config.isPassthroughCameraAccessEnabled.ToString(),
                config => config.isPassthroughCameraAccessEnabled,
                config => config.isPassthroughCameraAccessEnabled = true),
            new XrCheck
            {
                Id = "ovr.runtimeSettings",
                Current = () => OVRRuntimeSettings.GetRuntimeSettings() != null ? "exists" : "missing",
                IsOk = () => OVRRuntimeSettings.GetRuntimeSettings() != null,
                Fix = () =>
                {
                    OVRRuntimeSettings settings = OVRRuntimeSettings.GetRuntimeSettings();
                    if (settings != null) EditorUtility.SetDirty(settings);
                },
            },
        };

        // -- OpenXR features ------------------------------------------------

        static XrCheck FeatureCheck(string id, string featureId) => new XrCheck
        {
            Id = id,
            Current = () => DescribeFeature(featureId),
            IsOk = () => IsFeatureEnabled(featureId),
            Fix = () => SetFeatureEnabled(featureId, true),
        };

        static string DescribeFeature(string featureId)
        {
            var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, featureId);
            return feature == null ? "(not found)" : feature.enabled ? "enabled" : "disabled";
        }

        static bool IsFeatureEnabled(string featureId)
        {
            var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, featureId);
            return feature != null && feature.enabled;
        }

        static void SetFeatureEnabled(string featureId, bool enabled)
        {
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, featureId);
            if (feature == null)
            {
                Debug.LogWarning($"{Tag} OpenXR feature '{featureId}' is not registered for Android.");
                return;
            }
            if (feature.enabled == enabled) return;
            feature.enabled = enabled;
            EditorUtility.SetDirty(feature);
        }

        // -- XR Plug-in Management loader -----------------------------------

        static string DescribeLoaders(BuildTargetGroup group)
        {
            XRManagerSettings manager = GetXrManager(group);
            if (manager == null) return "(no XR settings)";
            var loaders = manager.activeLoaders;
            if (loaders == null || loaders.Count == 0) return "(none)";
            return string.Join(",", loaders.Where(l => l != null).Select(l => l.GetType().Name));
        }

        static XRManagerSettings GetXrManager(BuildTargetGroup group)
        {
            XRGeneralSettingsPerBuildTarget perTarget = GetOrCreatePerBuildTarget();
            XRGeneralSettings settings = perTarget == null ? null : perTarget.SettingsForBuildTarget(group);
            return settings == null ? null : settings.AssignedSettings;
        }

        static XRGeneralSettingsPerBuildTarget _perTarget;

        // XRGeneralSettingsPerBuildTarget.GetOrCreate is internal; reflect rather than vendor its asset logic.
        static XRGeneralSettingsPerBuildTarget GetOrCreatePerBuildTarget()
        {
            if (EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget existing) && existing != null)
                return _perTarget = existing;
            if (_perTarget != null) return _perTarget;

            MethodInfo getOrCreate = typeof(XRGeneralSettingsPerBuildTarget).GetMethod(
                "GetOrCreate", BindingFlags.Static | BindingFlags.NonPublic);
            if (getOrCreate == null)
            {
                Debug.LogError($"{Tag} XRGeneralSettingsPerBuildTarget.GetOrCreate not found; XR Management API changed.");
                return null;
            }
            return _perTarget = (XRGeneralSettingsPerBuildTarget)getOrCreate.Invoke(null, null);
        }

        static void EnsureOpenXrLoader(BuildTargetGroup group)
        {
            XRGeneralSettingsPerBuildTarget perTarget = GetOrCreatePerBuildTarget();
            if (perTarget == null) return;
            if (!perTarget.HasManagerSettingsForBuildTarget(group))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            XRManagerSettings manager = perTarget.SettingsForBuildTarget(group)?.AssignedSettings;
            if (manager == null)
            {
                Debug.LogError($"{Tag} Could not create XRManagerSettings for {group}.");
                return;
            }
            if (!XRPackageMetadataStore.AssignLoader(manager, OpenXrLoaderType, group))
                Debug.LogWarning($"{Tag} AssignLoader(OpenXR, {group}) returned false.");
        }

        // -- OVRProjectConfig -----------------------------------------------

        static XrCheck OvrConfigCheck(string id, Func<OVRProjectConfig, string> read,
            Func<OVRProjectConfig, bool> isOk, Action<OVRProjectConfig> fix) => new XrCheck
        {
            Id = id,
            Current = () => OVRProjectConfig.CachedProjectConfig == null ? "(no config)" : read(OVRProjectConfig.CachedProjectConfig),
            IsOk = () => OVRProjectConfig.CachedProjectConfig != null && isOk(OVRProjectConfig.CachedProjectConfig),
            Fix = () =>
            {
                OVRProjectConfig config = OVRProjectConfig.CachedProjectConfig;
                if (config == null) throw new InvalidOperationException("Meta XR project config is unavailable.");
                fix(config);
                OVRProjectConfig.CommitProjectConfig(config);
            },
        };
    }
}
