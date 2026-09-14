// FinalScan host: installs Meta XR Building Blocks (OVRCameraRig, Passthrough, PassthroughCameraAccess)
// through Meta.XR.BuildingBlocks.Editor via reflection (its install API is internal).
// Port (renamed, minimized) of the donor building-block installer; MIGRATION_LEDGER §2. Idempotent.

using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Meta.XR.BuildingBlocks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FinalScan.Editor
{
    internal static class FinalScanBuildingBlocks
    {
        const string Tag = "[FinalScan]";

        // Mirrors com.meta.xr.sdk.core/Editor/BuildingBlocks/BlockDataIds.cs (SDK 205).
        public const string CameraRigBlock = "e47682b9-c270-40b1-b16d-90b627a5ce1b";
        public const string PassthroughBlock = "f0540b20-dfd6-420e-b20d-c270f88dc77e";
        public const string PassthroughCameraAccessBlock = "0792d3af-c7d9-4f9c-a6f0-fd580a051e48";

        static readonly (string Id, string Label)[] RequiredBlocks =
        {
            (CameraRigBlock, "OVRCameraRig"),
            (PassthroughBlock, "Passthrough"),
            (PassthroughCameraAccessBlock, "PassthroughCameraAccess"),
        };

        static MethodInfo _getBlockData;
        static MethodInfo _installWithDependencies;
        static PropertyInfo _isSingletonAndPresent;

        public static bool IsPresent(string blockId) =>
            Object.FindObjectsByType<BuildingBlock>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Any(block => block != null && block.BlockId == blockId);

        /// <summary>Installs each missing required block. Returns false when the Meta API is unavailable.</summary>
        public static async Task<bool> EnsureAsync()
        {
            if (!ResolveApi()) return false;

            int installed = 0;
            foreach ((string id, string label) in RequiredBlocks)
            {
                if (IsPresent(id)) continue;
                object data = _getBlockData.Invoke(null, new object[] { id });
                if (data == null)
                {
                    Debug.LogWarning($"{Tag} Building Block '{label}' ({id}) missing from registry; skipped.");
                    continue;
                }
                if (_isSingletonAndPresent != null && (bool)_isSingletonAndPresent.GetValue(data)) continue;
                try
                {
                    if (_installWithDependencies.Invoke(data, new object[] { null }) is Task task) await task;
                    installed++;
                    Debug.Log($"{Tag} Installed Building Block: {label}");
                }
                catch (TargetInvocationException exception)
                    when (exception.InnerException?.GetType().Name == "InstallationCancelledException")
                {
                    // Singleton present at install time; benign.
                }
                catch (Exception exception)
                {
                    Debug.LogError($"{Tag} Building Block '{label}' install failed: {exception.Message}");
                }
            }
            if (installed > 0) AssetDatabase.SaveAssets();
            return true;
        }

        static bool ResolveApi()
        {
            if (_getBlockData != null && _installWithDependencies != null) return true;

            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => candidate.GetName().Name == "Meta.XR.BuildingBlocks.Editor");
            Type utils = assembly?.GetType("Meta.XR.BuildingBlocks.Editor.Utils");
            Type blockData = assembly?.GetType("Meta.XR.BuildingBlocks.Editor.BlockData");
            if (utils == null || blockData == null)
            {
                Debug.LogWarning($"{Tag} Meta.XR.BuildingBlocks.Editor API not found; falling back to manual rig creation.");
                return false;
            }

            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic;
            _getBlockData = utils.GetMethod("GetBlockData", any | BindingFlags.Static, null, new[] { typeof(string) }, null);
            _installWithDependencies = blockData.GetMethods(any | BindingFlags.Instance).FirstOrDefault(method =>
                method.Name == "InstallWithDependencies" && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType == typeof(GameObject));
            _isSingletonAndPresent = blockData.GetProperty("IsSingletonAndAlreadyPresent", any | BindingFlags.Instance);
            return _getBlockData != null && _installWithDependencies != null;
        }
    }
}
