using UnityEngine;

namespace FinalScan
{
    /// <summary>
    /// Scene root of FinalScan. The editor host setup attaches this to the "[FinalScan]" GameObject.
    /// Subsystems register themselves as sibling components; this component only owns version/identity logging.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FinalScanBootstrap : MonoBehaviour
    {
        public const string Version = "0.1.0-c01";
        public const string LogTag = "FinalScan";

        void Awake()
        {
            Debug.Log($"[{LogTag}] bootstrap version={Version} unity={Application.unityVersion} device={SystemInfo.deviceModel} gpu={SystemInfo.graphicsDeviceName} api={SystemInfo.graphicsDeviceType}");
        }
    }
}
