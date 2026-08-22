using System.IO;
using Genesis.RoomScan.Exporting;
using Genesis.RoomScan.World;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class OperatorExperienceTests
    {
        [Test]
        public void LargeWorldDefaultsAreExplicitIdempotentAndBounded()
        {
            var gameObject = new GameObject("large-world-defaults");
            try
            {
                SubmapManager submaps = gameObject.AddComponent<SubmapManager>();
                submaps.ApplyLargeWorldDefaults();
                submaps.ApplyLargeWorldDefaults();
                Assert.That(submaps.LargeWorldMode, Is.True);
                Assert.That(submaps.UsesLargeWorldDefaults, Is.True);
                Assert.That(submaps.ResidentChunkCount, Is.EqualTo(0));
                Assert.That(submaps.BoundaryMarginMeters, Is.EqualTo(1f));
                Assert.That(submaps.OverlapMeters, Is.EqualTo(2f));
                Assert.That(submaps.RearmHysteresisMeters, Is.EqualTo(0.75f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void DiagnosticsAndOperatorUiExposeRequiredWorldSurfaces()
        {
            InfiniteScanStatus empty = InfiniteScanDiagnostics.Capture(null, null);
            Assert.That(empty.Mode, Is.EqualTo("Not attached"));
            Assert.That(empty.Network, Is.EqualTo("Pure Quest / offline"));
            Assert.That(InfiniteScanDiagnostics.FormatBytes(3L * 1024L * 1024L),
                Is.EqualTo("3.0 MiB"));

            string uxmlPath = Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/UI/DebugMenu.uxml");
            string uxml = File.ReadAllText(uxmlPath);
            string[] requiredNames =
            {
                "nav-world", "view-world", "val-world-id", "val-active-chunk",
                "val-chunk-lifecycle", "val-residency", "val-graph",
                "val-world-storage", "val-glb-export", "btn-export-chunk-glb",
                "btn-export-world-glb"
            };
            foreach (string required in requiredNames)
                Assert.That(uxml, Does.Contain($"name=\"{required}\""), required);
            Assert.That(uxml, Does.Contain("canonical PressureManifold"));
            Assert.That(uxml, Does.Not.Contain("DiffSoup"));
            Assert.That(uxml, Does.Not.Contain("Gaussian"));
        }

        [Test]
        public void WorldPerformanceTelemetryIsStableAndMachineReadable()
        {
            string line = InfiniteScanPerformanceTelemetry.Format(1234, "rollover",
                7, 3, ChunkLifecycleState.Active, 8, 1, 1, 2, 1, 0,
                123456789, 234567890);
            Assert.That(line, Is.EqualTo(
                "QIS_WORLD_PROFILE unixMs=1234 reason=rollover chunks=7 " +
                "activeRevision=3 activeState=1 edges=8 residentCanonical=1 " +
                "maxResidentCanonical=1 residentMeshlets=2 residentAppearance=1 " +
                "backgroundPublications=0 allocatedBytes=123456789 " +
                "reservedBytes=234567890"));
            Assert.That(InfiniteScanPerformanceTelemetry.Format(0, "free form", 0,
                0, ChunkLifecycleState.New, 0, 0, 1, 0, 0, 0, 0, 0),
                Does.Contain("reason=unknown"));
        }

        [Test]
        public void ScanStartAndUiBindingContractsPreventReentrantSnapshotStaging()
        {
            string scannerPath = Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Core/RoomScanner.cs");
            string menuPath = Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/UI/DebugMenuController.cs");
            string residencyPath = Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/World/" +
                "PrismChunkResidencyManager.cs");

            string scanner = File.ReadAllText(scannerPath);
            string menu = File.ReadAllText(menuPath);
            string residency = File.ReadAllText(residencyPath);

            Assert.That(scanner, Does.Contain("ScanLifecycleState.Starting"));
            Assert.That(scanner, Does.Contain(
                "ToggleScanning ignored while start is already in progress"));
            Assert.That(scanner, Does.Contain(
                "No ScanStopped notification here"));
            Assert.That(menu, Does.Contain("if (_boundRoot != _root)"));
            Assert.That(residency, Does.Not.Contain(
                "_scanner.ScanStarted += OnScanStarted"));
        }
    }
}
