using System;
using System.IO;
using Genesis.RoomScan.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class DebugMenuUxTests
    {
        [Test]
        public void MenuUsesLeftAnchorLeftToggleAndRightPointer()
        {
            Vector3 position = DebugMenuFollower.ControllerPanelPosition(
                Vector3.zero, Vector3.up, Vector3.forward, 0.18f, 0.04f);
            Assert.That(position, Is.EqualTo(new Vector3(0f, 0.18f, 0.04f)));

            string input = SourceOf("RoomScanInputHandler");
            Assert.That(input, Does.Contain(
                "ScanAction.ToggleDebugMenu,     button = " +
                "OVRInput.Button.PrimaryThumbstick"));
            string follower = SourceOf("DebugMenuFollower");
            Assert.That(follower, Does.Contain(
                "OVRInput.Handedness.LeftHanded"));
            string ray = SourceOf("ControllerRayDriver");
            Assert.That(ray, Does.Contain("ChooseRightController"));
            Assert.That(ray, Does.Contain(
                "OVRInput.Handedness.RightHanded"));
            Assert.That(ray, Does.Contain(
                "OVRInput.Button.SecondaryIndexTrigger"));
            Assert.That(ray, Does.Not.Contain("ChooseBestController"));
        }

        [Test]
        public void HidingMenuCannotStopScanOrHideReadout()
        {
            string source = SourceOf("DebugMenuController");
            int begin = source.IndexOf("public void Hide()",
                StringComparison.Ordinal);
            int end = source.IndexOf("private void Query()",
                begin, StringComparison.Ordinal);
            Assert.That(begin, Is.GreaterThanOrEqualTo(0));
            Assert.That(end, Is.GreaterThan(begin));
            string hide = source.Substring(begin, end - begin);
            Assert.That(hide, Does.Not.Contain("RoomScanner"));
            Assert.That(hide, Does.Not.Contain("StopScanning"));
            Assert.That(hide, Does.Not.Contain("SetRenderMode"));
            Assert.That(hide, Does.Not.Contain("ToggleScanning"));
        }

        private static string SourceOf(string name)
        {
            string[] guids = AssetDatabase.FindAssets($"{name} t:MonoScript");
            Assert.That(guids, Has.Length.EqualTo(1), name);
            return File.ReadAllText(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
