using System.Collections;
using FinalScan.Host;
using FinalScan.Platform.Native;
using FinalScan.Render;
using FinalScan.Residency;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FinalScan.Tests
{
    /// <summary>
    /// PlayMode skeleton of the C07 spin acceptance (contract §21.2/§21.8). Device only: it needs the native plugin,
    /// the synthetic world and a person (or a rig script) rotating the headset; everywhere else it is ignored.
    /// Run with the Unity test runner on the Quest (PlayMode → Run on device) or drive it through
    /// <c>setprop debug.finalscan.spintest 1</c> on the prepared scene and read FS-ACCEPT from logcat.
    /// </summary>
    public class SpinAcceptanceTest
    {
        const float DurationSec = 30f;

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Spin_ZeroOrientationRequests_NoBlackFrames_72Hz()
        {
            if (Application.platform != RuntimePlatform.Android) Assert.Ignore("device only (Quest 3 / 3S)");
            if (!FinalScanHostNative.Available) Assert.Ignore("libFinalScanNative.so ABI 2 not available");

            FinalScanHost host = Object.FindAnyObjectByType<FinalScanHost>();
            if (host == null)
            {
                var root = new GameObject("[FinalScan]");
                host = root.AddComponent<FinalScanHost>();
                root.AddComponent<SurfelRenderer>();
                root.AddComponent<ResidencyDriver>();
            }
            ResidencyDriver driver = Object.FindAnyObjectByType<ResidencyDriver>();
            Assert.IsNotNull(driver);

            // Wait for the executor and the synthetic world.
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!host.Ready && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(host.Ready, "host never reached READY: status " + host.Status + " init rc " + host.InitResult);
            while (host.VisibleSurfels <= 0 && Time.realtimeSinceStartup < deadline) yield return null;

            driver.RequestSpinTest(DurationSec);
            deadline = Time.realtimeSinceStartup + DurationSec + 30f;
            while (driver.LastResultDetail == null && Time.realtimeSinceStartup < deadline) yield return null;

            SpinAcceptance.Result r = driver.LastResultDetail;
            Assert.IsNotNull(r, "spin test did not finish");
            Debug.Log("FS-ACCEPT spin " + r.ToJson());
            Assert.AreEqual(0, r.MaxOrientationRequests, "orientationResidencyRequests must stay 0 (§21.8)");
            Assert.AreEqual(0, r.ZeroVisibleWhileFacing, "no zero-visible frame while facing geometry (§21.2)");
            Assert.AreEqual(0, r.FramesOver20Ms, "no frame over 20 ms (§21.2)");
            Assert.IsTrue(r.Pass, string.Join("; ", r.Reasons));
        }
    }
}
