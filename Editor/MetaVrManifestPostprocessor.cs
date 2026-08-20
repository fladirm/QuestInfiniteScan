#if UNITY_ANDROID
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace Genesis.RoomScan.Editor
{
    /// <summary>
    /// Meta XR Core 205 still emits the legacy uses-horizonos-sdk element even on
    /// Horizon OS 2.7 / Meta VR API 207. Run strictly after Meta's callback and adapt
    /// only that generated declaration to the device parser's modern vocabulary.
    /// Remove this shim once the upstream package emits uses-metavr-sdk itself.
    /// </summary>
    public sealed class MetaVrManifestPostprocessor : IPostGenerateGradleAndroidProject
    {
        private const string HorizonNamespace = "http://schemas.horizonos/sdk";
        public int callbackOrder => 200_000; // Meta XR 205 uses 99,999.

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = Path.Combine(path, "src", "main",
                "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
                throw new BuildFailedException(
                    "Generated Unity library AndroidManifest.xml is missing: " +
                    manifestPath);
            if (UpgradeGeneratedManifest(manifestPath))
                Debug.Log("[QuestInfiniteScan] Upgraded generated Horizon OS SDK tag " +
                          "to uses-metavr-sdk/minApiVersion/targetApiVersion.");
        }

        internal static bool UpgradeGeneratedManifest(string manifestPath)
        {
            var document = XDocument.Load(manifestPath,
                LoadOptions.PreserveWhitespace);
            XElement root = document.Root ?? throw new BuildFailedException(
                "Generated Android manifest has no root element.");
            string[] appTagNames =
            {
                "uses-vr-sdk", "uses-hzos-sdk", "uses-horizonos-sdk",
                "uses-metavr-sdk"
            };
            XElement[] tags = root.Elements()
                .Where(element => appTagNames.Contains(element.Name.LocalName,
                    StringComparer.Ordinal))
                .ToArray();
            if (tags.Length == 0)
                throw new BuildFailedException(
                    "Meta XR did not generate a Meta VR OS SDK declaration.");

            string minimum = FindVersion(tags, "minApiVersion", "minSdkVersion");
            string target = FindVersion(tags, "targetApiVersion", "targetSdkVersion");
            if (!int.TryParse(minimum, out int minimumValue) || minimumValue < 0 ||
                !int.TryParse(target, out int targetValue) || targetValue < minimumValue)
                throw new BuildFailedException(
                    "Generated Meta VR OS API versions are invalid.");

            XNamespace horizon = HorizonNamespace;
            var modern = new XElement(horizon + "uses-metavr-sdk",
                new XAttribute(horizon + "minApiVersion", minimumValue),
                new XAttribute(horizon + "targetApiVersion", targetValue));
            tags[0].AddBeforeSelf(modern);
            foreach (XElement tag in tags) tag.Remove();
            document.Save(manifestPath, SaveOptions.DisableFormatting);
            return tags.Length != 1 ||
                   tags[0].Name.LocalName != "uses-metavr-sdk" ||
                   tags[0].Attributes().Any(attribute =>
                       attribute.Name.LocalName == "minSdkVersion" ||
                       attribute.Name.LocalName == "targetSdkVersion");
        }

        private static string FindVersion(XElement[] tags, params string[] names)
        {
            foreach (string name in names)
            foreach (XElement tag in tags)
            {
                XAttribute attribute = tag.Attributes().FirstOrDefault(candidate =>
                    candidate.Name.LocalName == name);
                if (attribute != null) return attribute.Value;
            }
            return null;
        }
    }
}
#endif
