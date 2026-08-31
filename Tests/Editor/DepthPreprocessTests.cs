using System;
using System.IO;
using System.Reflection;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Tests
{
    public sealed class DepthPreprocessTests
    {
        [Test]
        public void DilationSequence_IncludesFinalUnitStepExactlyOnce()
        {
            Assert.That(DepthCapture.BuildDilationStepSequence(8), Is.EqualTo(new[]
            {
                256, 128, 64, 32, 16, 8, 4, 2, 1
            }));
            Assert.That(DepthCapture.BuildDilationStepSequence(0),
                Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void PreprocessCadence_ConsumesOnlyNewRawFrameVersions()
        {
            Assert.That(DepthCapture.ShouldPreprocessFrame(0, 0), Is.False);
            Assert.That(DepthCapture.ShouldPreprocessFrame(1, 0), Is.True);
            Assert.That(DepthCapture.ShouldPreprocessFrame(1, 1), Is.False);
            Assert.That(DepthCapture.ShouldPreprocessFrame(7, 1), Is.True,
                "Dropped/intermediate sensor versions must collapse into one latest-frame consume.");
            Assert.That(DepthCapture.ShouldPreprocessFrame(7, 7), Is.False);
        }

        [Test]
        public void RequestedDepthSnapshot_LatchesExactlyOneOwnedFrame()
        {
            var host = new GameObject("Depth snapshot test");
            DepthCapture capture = host.AddComponent<DepthCapture>();
            Texture2DArray first = MakeDepth(4, 4, 0.2f, 0.3f);
            Texture2DArray second = MakeDepth(4, 4, 0.7f, 0.8f);
            RenderTexture owned = null;
            try
            {
                typeof(DepthCapture).GetField("_projectionDepthCopyKernel",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(capture, new ComputeKernelHelper(
                        LoadCompute("DepthNormals.compute"),
                        "CopyProjectionDepthArray"));
                capture.StartDepthCapture();
                Assert.That(capture.TryLatchDepthSnapshot(first), Is.False,
                    "An unrequested producer frame must be discarded.");
                Assert.That(capture.OwnedRawDepthSnapshot, Is.Null);

                Assert.That(capture.RequestNextDepthFrame(), Is.True);
                Assert.That(capture.DepthFrameRequested, Is.True);
                Assert.That(capture.TryLatchDepthSnapshot(first), Is.True);
                owned = capture.OwnedRawDepthSnapshot;
                int version = capture.LatestRawFrameVersion;
                Assert.That(owned, Is.Not.Null);
                Assert.That(owned, Is.Not.SameAs(first));
                Assert.That(owned.graphicsFormat,
                    Is.EqualTo(GraphicsFormat.R32_SFloat));
                Assert.That(capture.DepthFrameRequested, Is.False);
                Assert.That(capture.OwnedDepthSnapshotReady, Is.True);
                Assert.That(capture.HasUnprocessedFrame, Is.True);

                Assert.That(capture.TryLatchDepthSnapshot(second), Is.False,
                    "An unconsumed owned snapshot must not be overwritten.");
                Assert.That(capture.RequestNextDepthFrame(), Is.False,
                    "Without a held A observation, a ready snapshot is the only owned slot.");
                Assert.That(capture.OwnedRawDepthSnapshot, Is.SameAs(owned));
                Assert.That(capture.LatestRawFrameVersion, Is.EqualTo(version));
            }
            finally
            {
                capture.BeginQuiesceDepthCapture();
                capture.CompleteDepthCaptureStop();
                if (owned != null) UnityEngine.Object.DestroyImmediate(owned);
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void RequestedDepthSnapshot_AlwaysSamplesExternalTexture()
        {
            string source = RuntimeSource("Runtime/Core/DepthCapture.cs");
            string latch = Slice(source,
                "internal bool TryLatchDepthSnapshot(Texture transientDepth)",
                "private void EnsureOwnedRawDepth");

            Assert.That(latch, Does.Not.Contain("Graphics.CopyTexture"));
            Assert.That(latch, Does.Not.Contain("transientDepth.graphicsFormat"));
            Assert.That(latch, Does.Contain(
                "_projectionDepthCopyKernel.Set(command,"));
            Assert.That(latch, Does.Contain(
                "InputProjectionDepthID, transientDepth);"));
            Assert.That(latch, Does.Contain(
                "_projectionDepthCopyKernel.DispatchFit(command,"));
            Assert.That(latch, Does.Contain(
                "CaptureOwner.DepthSnapshotCopy"));
            Assert.That(latch, Does.Contain(
                "MerkabaGpuTimestamps.End(CaptureOwner.DepthSnapshotCopy"));
        }

        [Test]
        public void OwnedDepthSnapshot_IsPreprocessedOnlyOnConsume()
        {
            string source = RuntimeSource("Runtime/Core/DepthCapture.cs");
            string callback = Slice(source, "private void OnDepthFrame(",
                "internal bool ConsumeLatestDepthFrame(CommandBuffer command,");
            int requestGuard = callback.IndexOf(
                "if (!_depthFrameRequested || _requestedDepthSlot < 0) return;",
                StringComparison.Ordinal);
            Assert.That(requestGuard, Is.GreaterThanOrEqualTo(0));
            Assert.That(callback, Does.Not.Contain("ApplyBilateralFilter()"));
            Assert.That(callback, Does.Not.Contain("ComputeNormals()"));
            Assert.That(callback, Does.Not.Contain("ComputeDilation()"));

            string consume = Slice(source,
                "internal bool ConsumeLatestDepthFrame(CommandBuffer command,",
                "public void ReleaseConsumedObservation()");
            Assert.That(consume, Does.Contain(
                "_depthTex = _ownedRawDepth[_heldDepthSlot];"));
            Assert.That(consume, Does.Contain(
                "ApplyStereoRgbdRefinement(command, cameraFrame, fineBrush);"));
            Assert.That(consume, Does.Not.Contain("ComputeNormals(command);"));
            Assert.That(consume, Does.Contain("ComputeDilation(command);"));
            Assert.That(consume, Does.Contain("_heldDepthSlot = _readyDepthSlot;"));
            Assert.That(consume, Does.Not.Contain("_heldDepthSlot = -1;"),
                "A must remain owned until the integration token completes.");
            Assert.That(source, Does.Not.Contain("private Texture _rawDepthTex"));
            Assert.That(source, Does.Contain("CopyProjectionDepthArray"));
            Assert.That(source, Does.Contain("GraphicsFormat.R32_SFloat"));
            Assert.That(source, Does.Not.Contain("ApplyBilateralFilter"));
            Assert.That(source, Does.Not.Contain("RGBGuide"));
        }

        [Test]
        public void ObservationCadence_LatchesTrueStereoPcaAndMetadataOnce()
        {
            var host = new GameObject("PCA snapshot test");
            DepthCapture depth = host.AddComponent<DepthCapture>();
            MerkabaIntegrator integrator = host.AddComponent<MerkabaIntegrator>();
            var left = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var right = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            left.Apply(false, false);
            right.Apply(false, false);
            Vector3 leftPosition = new(1f, 2f, 3f);
            Vector3 rightPosition = new(4f, 5f, 6f);
            try
            {
                typeof(MerkabaIntegrator).GetField("_depthCapture",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(integrator, depth);
                var leftFrame = new CameraFrameDescriptor(left,
                    new Pose(leftPosition, Quaternion.Euler(1f, 2f, 3f)),
                    new Vector2(7f, 8f), new Vector2(9f, 10f),
                    new Vector2(11f, 12f), new Vector2(13f, 14f),
                    2.0, 1u, StereoEye.Left);
                var rightFrame = new CameraFrameDescriptor(right,
                    new Pose(rightPosition, Quaternion.Euler(4f, 5f, 6f)),
                    new Vector2(17f, 18f), new Vector2(19f, 20f),
                    new Vector2(21f, 22f), new Vector2(23f, 24f),
                    2.001, 1u, StereoEye.Right);
                Assert.That(integrator.SetStereoCameraData(
                    new StereoCameraFrame(leftFrame, rightFrame, 0.001)),
                    Is.True);
                RenderTexture[] owned = PrivateField<RenderTexture[]>(integrator,
                    "_cameraFrameCopies");
                Assert.That(owned[0], Is.Not.Null);
                Assert.That(owned[1], Is.Not.Null);
                Assert.That(owned[0], Is.Not.SameAs(left));
                Assert.That(owned[1], Is.Not.SameAs(right));
                Assert.That(integrator.CameraFrameAvailable, Is.True);
                Vector3[] positions = PrivateField<Vector3[]>(integrator,
                    "_cameraPosition");
                Assert.That(positions[0], Is.EqualTo(leftPosition));
                Assert.That(positions[1], Is.EqualTo(rightPosition));
                Assert.That(typeof(MerkabaIntegrator).GetField("_pendingCameraFrame",
                    BindingFlags.Instance | BindingFlags.NonPublic), Is.Null,
                    "No producer-owned PCA texture may be retained as hidden state.");

                string scanner = RuntimeSource("Runtime/Core/RoomScanner.cs");
                string update = Slice(scanner, "private void Update()",
                    "private void OnDisable()");
                Assert.That(update, Does.Contain("TryGetSynchronizedFrame("));
                Assert.That(update, Does.Contain("SetStereoCameraData(cameraFrame)"));
                Assert.That(update, Does.Contain("DepthExpired"));
                string arm = Slice(scanner, "private void ArmNextObservation()",
                    "private void OnIntegrated()");
                Assert.That(arm, Does.Contain("RequestNextDepthFrame()"));
                Assert.That(arm, Does.Not.Contain("ProvideColorFrame"));
            }
            finally
            {
                RenderTexture[] owned = PrivateField<RenderTexture[]>(integrator,
                    "_cameraFrameCopies");
                foreach (RenderTexture texture in owned)
                    if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(left);
                UnityEngine.Object.DestroyImmediate(right);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TrueStereoRgbdContract_IsFailClosedAndWorldReprojected()
        {
            string scanner = RuntimeSource("Runtime/Core/RoomScanner.cs");
            string update = Slice(scanner, "private void Update()",
                "private void OnDisable()");
            Assert.That(update, Does.Contain("TryGetReadyFrameUnixTime("));
            Assert.That(update, Does.Contain("TryGetSynchronizedFrame("));
            Assert.That(update, Does.Contain("DepthExpired"));
            Assert.That(update.IndexOf("SetStereoCameraData(cameraFrame)",
                    StringComparison.Ordinal),
                Is.LessThan(update.IndexOf("TrySubmitObservationAttempt()",
                    update.IndexOf("SetStereoCameraData(cameraFrame)",
                        StringComparison.Ordinal), StringComparison.Ordinal)));

            string refine = RuntimeSource(
                "Runtime/Shaders/StereoRgbdRefine.compute");
            Assert.That(refine, Does.Contain("WorldPatchCensus(0u"));
            Assert.That(refine, Does.Contain("WorldPatchCensus(1u"));
            Assert.That(refine, Does.Contain(
                "MerkabaProjectCameraUv(eye, center)"));
            Assert.That(refine, Does.Contain("TryProjectedDepthPlane("));
            Assert.That(refine, Does.Contain(
                "countbits(leftCensus ^ rightCensus)"));
            Assert.That(refine, Does.Contain("_DstDepth[id] = 0.0;"));
            Assert.That(refine, Does.Contain("_DstNormal[id] = 0.0;"));
            Assert.That(refine, Does.Contain("RGBD_HYPOTHESIS_RADIUS 2"));
            Assert.That(refine, Does.Contain(
                "RGBD_MAX_METRIC_CORRECTION 0.0125"));
            Assert.That(refine, Does.Contain("RGBD_CENSUS_SAMPLES 8u"));
            Assert.That(refine, Does.Contain(
                "RGBD_MAX_CENSUS_MISMATCH 2u"));
            Assert.That(refine, Does.Contain("CameraCoverage("));
            Assert.That(refine, Does.Contain("WorldPatchCensus("));
            Assert.That(refine, Does.Contain(
                "dot(worldPosition - rightPlanePoint"));
            Assert.That(refine, Does.Not.Contain(
                "distance(worldPosition, observedWorld)"));
            Assert.That(refine, Does.Not.Contain("stereoMargin"));
            Assert.That(refine, Does.Not.Contain("_DepthSearchRadius"));
            Assert.That(refine, Does.Not.Contain("same UV"));
            Assert.That(refine, Does.Contain("DepthNdcToWorld"));
            Assert.That(refine, Does.Contain("_DepthProjInv[eye]"));
            Assert.That(refine, Does.Contain("_DepthViewInv[eye]"));
            Assert.That(refine, Does.Contain("WorldToReferenceDepthNdc"));
            Assert.That(refine, Does.Contain(
                "void StereoRgbdRefine(uint2 id"));
            Assert.That(refine, Does.Not.Contain("_DstDepth[id.z]"));
            Assert.That(refine, Does.Not.Contain("ProjectionToLinear"),
                "Environment Depth must use its exact per-eye projection, " +
                "not a generic near/far conversion.");

            string integration = RuntimeSource(
                "Runtime/Shaders/MerkabaIntegration.compute");
            Assert.That(integration, Does.Contain(
                "MerkabaProjectCameraUv(0u, worldSurface)"));
            Assert.That(integration, Does.Contain(
                "MerkabaProjectCameraUv(1u, worldSurface)"));
            Assert.That(integration, Does.Contain(
                "MerkabaSampleCameraRgb(0u, leftUv)"));
            Assert.That(integration, Does.Contain(
                "MerkabaSampleCameraRgb(1u, rightUv)"));
            Assert.That(integration, Does.Not.Contain(
                "eyeSurfaceQuality[cameraEye]"));
            Assert.That(integration, Does.Not.Contain(
                "Texture2D<float4> _MerkabaCameraRgb;"));
            Assert.That(integration, Does.Not.Contain("CameraExposure"),
                "Canonical RGB must store measured PCA color, not presentation exposure.");

            string depth = RuntimeSource("Runtime/Core/DepthCapture.cs");
            string devicePose = Slice(depth, "private void HandleDeviceDepth(",
                "internal bool TryLatchDepthSnapshot(");
            Assert.That(devicePose, Does.Contain(
                "Matrix4x4.TRS(pose.position, pose.rotation, ScaleFlipZ)"));
            Assert.That(devicePose, Does.Not.Contain("worldToTracking"));
            Assert.That(devicePose, Does.Not.Contain("XROrigin"));
            string provider = RuntimeSource(
                "Runtime/Camera/PassthroughCameraProvider.cs");
            Assert.That(provider, Does.Contain("camera.GetCameraPose()"));
            Assert.That(provider, Does.Not.Contain("TrackingToWorld"));
            Assert.That(provider, Does.Contain("HistoryCapacity = 2"));
            Assert.That(provider, Does.Contain("MatchFrameHistory("));
            Assert.That(provider, Does.Contain(
                "command.BlitPcaHistoryProfiled(sample.Texture, owned,"));
            string renderCopy = Slice(provider,
                "private void OnEndContextRendering(",
                "private bool TryCaptureMetadata");
            Assert.That(renderCopy, Does.Contain("TryCaptureMetadata(0"));
            Assert.That(renderCopy, Does.Contain("TryCaptureMetadata(1"));
            Assert.That(renderCopy.IndexOf("TryCaptureMetadata(0",
                    StringComparison.Ordinal),
                Is.LessThan(renderCopy.IndexOf("StageOwnedSample(command, 0",
                    StringComparison.Ordinal)));
            Assert.That(provider, Does.Not.Contain("private void Update()"),
                "Live PCA image metadata must be latched in the same render " +
                "callback that submits its owned copy.");
            Assert.That(provider, Does.Contain(
                "RenderPipelineManager.endContextRendering += " +
                "OnEndContextRendering"));
            Assert.That(depth, Does.Contain(
                "_refinedDepthTex.graphicsFormat != GraphicsFormat.R32_SFloat"));
            Assert.That(depth, Does.Contain(
                "_stereoRgbdRefineKernel.Set(command, RefineDstNormalId"));
            Assert.That(depth, Does.Not.Contain("ComputeNormals(command)"));
        }

        [Test]
        public void SensorClockMapper_BracketsXrTimeIntoPcaUnixDomain()
        {
            double[] xr = { 100.000, 100.002 };
            int index = 0;
            DateTime utc = DateTime.UnixEpoch.AddSeconds(200.0);
            var mapper = new SensorClockMapper(() => xr[index++], () => utc);

            Assert.That(mapper.TryCaptureAnchor(), Is.True);
            Assert.That(mapper.UncertaintySeconds,
                Is.EqualTo(0.0010001).Within(1e-9));
            Assert.That(mapper.TryMapXrNanoseconds(100_001_000_000L,
                out double mapped), Is.True);
            Assert.That(mapped, Is.EqualTo(200.0).Within(1e-6));
        }

        [Test]
        public void SensorClockMapper_RejectsUncertainAnchor()
        {
            double[] xr = { 100.0, 100.02 };
            int index = 0;
            var mapper = new SensorClockMapper(() => xr[index++],
                () => DateTime.UnixEpoch.AddSeconds(200.0));

            Assert.That(mapper.TryCaptureAnchor(), Is.False);
            Assert.That(mapper.IsReady, Is.False);
            Assert.That(mapper.TryMapXrNanoseconds(100_001_000_000L,
                out _), Is.False);
        }

        [Test]
        public void ReadyDepth_RetriesClockAnchorBeforeFailClosedPairing()
        {
            string depth = RuntimeSource("Runtime/Core/DepthCapture.cs");
            string readyTime = Slice(depth,
                "internal bool TryGetReadyFrameUnixTime(",
                "internal bool DiscardReadyDepthFrame()");
            int retry = readyTime.IndexOf("_depthClock.TryCaptureAnchor();",
                StringComparison.Ordinal);
            int map = readyTime.IndexOf("_depthClock.TryMapXrNanoseconds(",
                StringComparison.Ordinal);

            Assert.That(retry, Is.GreaterThanOrEqualTo(0));
            Assert.That(map, Is.GreaterThan(retry));
        }

        [Test]
        public void ScanStart_PreparesGpuBeforeStartingQuestCapture()
        {
            string scanner = RuntimeSource("Runtime/Core/RoomScanner.cs");
            string start = Slice(scanner, "public async Task StartScanningAsync()",
                "public void StopScanning()");
            int prepare = start.IndexOf("_grid.EnsureGpuResources();",
                StringComparison.Ordinal);
            int firstYield = start.IndexOf("await Task.Yield();", prepare,
                StringComparison.Ordinal);
            int secondYield = start.IndexOf("await Task.Yield();",
                firstYield + 1, StringComparison.Ordinal);
            int pca = start.IndexOf("_cameraProvider?.StartCapture();",
                StringComparison.Ordinal);
            int depth = start.IndexOf("_depthCapture.StartDepthCapture();",
                StringComparison.Ordinal);

            Assert.That(prepare, Is.GreaterThanOrEqualTo(0));
            Assert.That(firstYield, Is.GreaterThan(prepare));
            Assert.That(secondYield, Is.GreaterThan(firstYield));
            Assert.That(pca, Is.GreaterThan(secondYield));
            Assert.That(depth, Is.GreaterThan(secondYield));
            Assert.That(start.IndexOf("ArmNextObservation();",
                StringComparison.Ordinal), Is.GreaterThan(depth));

            string grid = RuntimeSource("Runtime/Merkaba/MerkabaGrid.Gpu.cs");
            string ensure = Slice(grid, "internal void EnsureGpuResources()",
                "private void ReleaseGpuResources()");
            Assert.That(ensure, Does.Contain("if (_gpuReady) return;"));
        }

        [Test]
        public void Quiesce_RetiresObservationAndCopiesBeforeProviderTeardown()
        {
            string scanner = RuntimeSource("Runtime/Core/RoomScanner.cs");
            string quiesce = Slice(scanner, "private async Task<bool> QuiesceCoreAsync()",
                "private uint NextLifecycleGeneration()");
            int stopAdmission = quiesce.IndexOf("IsScanning = false;",
                StringComparison.Ordinal);
            int detach = quiesce.IndexOf("BeginQuiesceDepthCapture();",
                StringComparison.Ordinal);
            int observation = quiesce.IndexOf("FinishCurrentObservationAsync();",
                StringComparison.Ordinal);
            int copies = quiesce.IndexOf("await Task.WhenAll(depthRetirement, cameraRetirement,",
                StringComparison.Ordinal);
            int depthStop = quiesce.IndexOf("CompleteDepthCaptureStop();",
                StringComparison.Ordinal);
            int pcaStop = quiesce.IndexOf("_cameraProvider?.StopCapture();",
                StringComparison.Ordinal);

            Assert.That(stopAdmission, Is.GreaterThanOrEqualTo(0));
            Assert.That(detach, Is.GreaterThan(stopAdmission));
            Assert.That(quiesce.IndexOf("BeginSnapshotQuiesce();",
                StringComparison.Ordinal), Is.GreaterThan(stopAdmission));
            Assert.That(observation, Is.GreaterThan(detach));
            Assert.That(copies, Is.GreaterThan(observation));
            Assert.That(depthStop, Is.GreaterThan(copies));
            Assert.That(pcaStop, Is.GreaterThan(depthStop));

            string depth = RuntimeSource("Runtime/Core/DepthCapture.cs");
            string begin = Slice(depth, "internal void BeginQuiesceDepthCapture()",
                "internal Task RetireSubmittedDepthCopiesAsync()");
            string complete = Slice(depth, "internal void CompleteDepthCaptureStop()",
                "private void OnDestroy()");
            Assert.That(begin, Does.Not.Contain("_arOcclusionManager.enabled = false"));
            Assert.That(complete, Does.Not.Contain("_arOcclusionManager.enabled = false"));
            Assert.That(depth, Does.Contain("EnsureDynamicOcclusion();"));
            Assert.That(depth, Does.Contain(
                "AROcclusionShaderMode.HardOcclusion"));
            Assert.That(depth, Does.Contain(
                "_arOcclusionManager.gameObject.AddComponent<ARShaderOcclusion>()"));
            Assert.That(depth, Does.Contain(
                "bool depthRequired = _captureActive || dynamicOcclusionEnabled;"));
            Assert.That(depth, Does.Contain(
                "_shaderOcclusion.enabled = dynamicOcclusionEnabled;"));
            Assert.That(depth, Does.Not.Contain("WaitForCompletion"));
            Assert.That(depth, Does.Not.Contain("Thread.Sleep"));
            Assert.That(depth, Does.Not.Contain("Task.Delay"));
            Assert.That(depth, Does.Not.Contain("OnApplicationPause"),
                "RoomScanner is the sole pause lifecycle authority.");

            string provider = RuntimeSource(
                "Runtime/Camera/PassthroughCameraProvider.cs");
            Assert.That(provider, Does.Contain(
                "RetireSubmittedSnapshotCopiesAsync()"));
            Assert.That(provider, Does.Contain(
                "CaptureOwnedGpuResourceRelease()"));
            Assert.That(provider, Does.Not.Contain("WaitForCompletion"));
        }

        [Test]
        public void ObservationAttempt_IsTokenRetiredWithoutBlindRedispatch()
        {
            string scanner = RuntimeSource("Runtime/Core/RoomScanner.cs");
            string update = Slice(scanner, "private void Update()",
                "private void OnDisable()");
            Assert.That(update, Does.Contain("TryRetireObservationAttempt();"));
            Assert.That(update, Does.Contain("!_integrator.HasAttemptInFlight"));
            Assert.That(update, Does.Contain("TrySubmitObservationAttempt()"));
            Assert.That(update, Does.Not.Contain("_integrator.Integrate(camera)"));

            string integrator = RuntimeSource(
                "Runtime/Merkaba/MerkabaIntegrator.cs");
            string retry = Slice(integrator,
                "private bool CanRetryPreparedObservation()",
                "private bool ObservationTimedOut()");
            Assert.That(retry, Does.Contain("ResidencyEpoch"));
            Assert.That(retry, Does.Contain("_attemptResidencyEpoch"));

            string shader = RuntimeSource(
                "Runtime/Shaders/MerkabaIntegration.compute");
            Assert.That(shader, Does.Contain(
                "_M8AttemptCompletion[0] = uint4(_M8AttemptToken"));
        }

        [Test]
        public void SaveAndExport_AwaitSharedQuiesceBeforeExplicitOperation()
        {
            string scanner = RuntimeSource("Runtime/Core/RoomScanner.cs");
            string save = Slice(scanner, "public async Task<bool> SaveAsync()",
                "public async Task<bool> LoadAsync()");
            string export = Slice(scanner,
                "public async Task<bool> ExportGlbAsync()",
                "public async void ClearAllDataAsync");
            Assert.That(save.IndexOf("await QuiesceScanningAsync()",
                    StringComparison.Ordinal),
                Is.LessThan(save.IndexOf("_persistence.SaveAsync()",
                    StringComparison.Ordinal)));
            Assert.That(export.IndexOf("await QuiesceScanningAsync()",
                    StringComparison.Ordinal),
                Is.LessThan(export.IndexOf("_exporter.ExportGlbAsync()",
                    StringComparison.Ordinal)));
            Assert.That(RuntimeSource("Runtime/Merkaba/MerkabaPersistence.cs"),
                Does.Not.Contain("await _integrator.FinishCurrentObservationAsync()"));
            Assert.That(RuntimeSource("Runtime/Merkaba/MerkabaExporter.cs"),
                Does.Not.Contain("await _integrator.FinishCurrentObservationAsync()"));
        }

        [Test, Timeout(30000)]
        public void JointSolve_IsTheOnlyDerivedNormalAuthority()
        {
            string copy = RuntimeSource("Runtime/Shaders/DepthNormals.compute");
            string joint = RuntimeSource(
                "Runtime/Shaders/StereoRgbdRefine.compute");
            Assert.That(copy, Does.Not.Contain("#pragma kernel DepthNorm"));
            Assert.That(copy, Does.Not.Contain("gsDepthNormalTexRW"));
            Assert.That(joint, Does.Contain("RWTexture2D<float4> _DstNormal"));
            Assert.That(joint, Does.Contain(
                "_DstNormal[id] = float4(selectedNormal, 1.0);"));
            Assert.That(joint, Does.Contain("TryDepthPlane("));
            Assert.That(joint, Does.Contain("TryProjectedDepthPlane("));
        }

        [Test, Timeout(30000)]
        public void JointSolve_TexturelessFourStreamPlane_KeepsMetricPrior()
        {
            const int width = 17;
            const int height = 15;
            ComputeShader compute = LoadCompute("StereoRgbdRefine.compute");
            int kernel = compute.FindKernel("StereoRgbdRefine");
            Matrix4x4 projection = Matrix4x4.Perspective(90f,
                width / (float)height, 0.1f, 10f);
            float sourceDepth = DepthNdc(projection, 1f);
            Texture2DArray depth = MakeDepth(width, height, sourceDepth,
                sourceDepth);
            Texture2D pcaLeft = MakeSolidRgb(width, height,
                new Color(0.4f, 0.4f, 0.4f, 1f));
            Texture2D pcaRight = MakeSolidRgb(width, height,
                new Color(0.4f, 0.4f, 0.4f, 1f));
            RenderTexture outputDepth = Make2DTarget(width, height,
                GraphicsFormat.R32_SFloat);
            RenderTexture outputNormal = Make2DTarget(width, height);
            int metricGroupsX = Mathf.CeilToInt(width / 8f);
            int metricGroupsY = Mathf.CeilToInt(height / 8f);
            using var refineMetrics = new ComputeBuffer(metricGroupsX *
                metricGroupsY * MerkabaGpuTimestamps.RefineMetricValueCount,
                sizeof(uint));

            try
            {
                Matrix4x4[] projections = { projection, projection };
                Matrix4x4[] inverseProjections =
                    { projection.inverse, projection.inverse };
                Matrix4x4[] views = { Matrix4x4.identity, Matrix4x4.identity };
                compute.SetTexture(kernel, "_SrcDepth", depth);
                compute.SetTexture(kernel, "_DstDepth", outputDepth);
                compute.SetTexture(kernel, "_DstNormal", outputNormal);
                compute.SetInt("_DepthW", width);
                compute.SetInt("_DepthH", height);
                compute.SetMatrixArray("_DepthProj", projections);
                compute.SetMatrixArray("_DepthProjInv", inverseProjections);
                compute.SetMatrixArray("_DepthView", views);
                compute.SetMatrixArray("_DepthViewInv", views);
                compute.SetBuffer(kernel, "_RefineMetrics", refineMetrics);
                compute.SetInt("_RefineMetricsEnabled", 1);
                compute.SetInt("_RefineMetricGroupsX", metricGroupsX);
                BindPca(compute, kernel, 0, pcaLeft, width, height);
                BindPca(compute, kernel, 1, pcaRight, width, height);
                compute.Dispatch(kernel, Mathf.CeilToInt(width / 8f),
                    Mathf.CeilToInt(height / 8f), 1);

                int center = width / 2 + width * (height / 2);
                float refined = Read2D(outputDepth, width, height)
                    .GetData<float>()[center];
                float4 normal = Read2D(outputNormal, width, height)
                    .GetData<float4>()[center];
                Assert.That(refined, Is.EqualTo(sourceDepth).Within(2e-4f),
                    "Ambiguous PCA texture must preserve the joint metric prior.");
                AssertFinite(normal, "joint normal");
                Assert.That(normal.w, Is.GreaterThan(0.5f));
                Assert.That(math.lengthsq(normal.xyz),
                    Is.EqualTo(1f).Within(2e-3f));

                var metricValues = new uint[refineMetrics.count];
                refineMetrics.GetData(metricValues);
                var radial = new uint[
                    MerkabaGpuTimestamps.RefineMetricValueCount];
                for (int index = 0; index < metricValues.Length; index++)
                    radial[index % radial.Length] += metricValues[index];
                for (int bin = 0; bin <
                     MerkabaGpuTimestamps.RefineRadialBinCount; bin++)
                {
                    int offset = bin * MerkabaGpuTimestamps.RefineMetricCount;
                    uint rejected = radial[offset + 1] + radial[offset + 2] +
                                    radial[offset + 3] + radial[offset + 4];
                    Assert.That(radial[offset], Is.EqualTo(
                        radial[offset + 7] + rejected));
                    Assert.That(radial[offset + 7], Is.EqualTo(
                        radial[offset + 5] + radial[offset + 6]));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(depth);
                UnityEngine.Object.DestroyImmediate(pcaLeft);
                UnityEngine.Object.DestroyImmediate(pcaRight);
                UnityEngine.Object.DestroyImmediate(outputDepth);
                UnityEngine.Object.DestroyImmediate(outputNormal);
            }
        }

        [Test, Timeout(30000)]
        public void DepthDilation_SignedBorderTapsOnJointField_AreSafe()
        {
            const int width = 13;
            const int height = 11;
            const float jointDepth = 0.42f;
            ComputeShader compute = LoadCompute("DepthDilation.compute");
            int init = compute.FindKernel("InitDepthDilation");
            int step = compute.FindKernel("DilateDepthStep");
            Matrix4x4 projection = Matrix4x4.Perspective(90f,
                width / (float)height, 0.1f, 10f);
            Texture2D depth = MakeDepth2D(width, height, jointDepth);
            RenderTexture a = Make2DTarget(width, height);
            RenderTexture b = Make2DTarget(width, height);

            try
            {
                BindDepthMatrices(compute, projection, width, height);
                compute.SetFloat(DepthCapture.VoxDistID, 0.075f);
                compute.SetFloat(DepthCapture.VoxSizeShaderID, 0.025f);
                compute.SetTexture(init, DepthCapture.DepthTexID, depth);
                compute.SetTexture(init, DepthCapture.DilateSrcID, a);
                compute.SetTexture(init, DepthCapture.DilateDestID, b);
                int groupsX = Mathf.CeilToInt(width / 8f);
                int groupsY = Mathf.CeilToInt(height / 8f);
                compute.Dispatch(init, groupsX, groupsY, 1);

                foreach (int stepSize in DepthCapture.BuildDilationStepSequence(8))
                {
                    compute.SetTexture(step, DepthCapture.DilateSrcID, a);
                    compute.SetTexture(step, DepthCapture.DilateDestID, b);
                    compute.SetInt(DepthCapture.DilateStepSizeID, stepSize);
                    compute.Dispatch(step, groupsX, groupsY, 1);
                    (a, b) = (b, a);
                }

                var values = Read2D(a, width, height).GetData<float4>();
                Assert.That(values.Length, Is.EqualTo(width * height));
                for (int index = 0; index < values.Length; index++)
                    AssertFinite(values[index], $"dilation[{index}]");
                Assert.That(values[0].z,
                    Is.EqualTo(jointDepth).Within(2e-4f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(depth);
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
            }
        }

        private static ComputeShader LoadCompute(string file)
        {
            string path = "Packages/com.genesis.roomscan/Runtime/Shaders/" + file;
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            Assert.That(shader, Is.Not.Null, path);
            return shader;
        }

        private static string RuntimeSource(string relativePath) =>
            File.ReadAllText(Path.GetFullPath(
                "Packages/com.genesis.roomscan/" + relativePath));

        private static string Slice(string source, string start, string end)
        {
            int first = source.IndexOf(start, StringComparison.Ordinal);
            int last = source.IndexOf(end, first + start.Length,
                StringComparison.Ordinal);
            Assert.That(first, Is.GreaterThanOrEqualTo(0), start);
            Assert.That(last, Is.GreaterThan(first), end);
            return source.Substring(first, last - first);
        }

        private static T PrivateField<T>(object target, string name) =>
            (T)target.GetType().GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(target);

        private static void BindDepthMatrices(ComputeShader compute,
            Matrix4x4 projection, int width, int height)
        {
            Matrix4x4[] projections = { projection, projection };
            Matrix4x4[] inverseProjections =
                { projection.inverse, projection.inverse };
            Matrix4x4[] views = { Matrix4x4.identity, Matrix4x4.identity };
            compute.SetMatrixArray(DepthCapture.ProjID, projections);
            compute.SetMatrixArray(DepthCapture.ProjInvID, inverseProjections);
            compute.SetMatrixArray(DepthCapture.ViewID, views);
            compute.SetMatrixArray(DepthCapture.ViewInvID, views);
            compute.SetVector(DepthCapture.ZParamsID,
                new Vector4(0.1f, 10f, 0f, 0f));
            compute.SetVector(DepthCapture.TexSizeID,
                new Vector4(width, height, 0f, 0f));
        }

        private static Texture2DArray MakeDepth(int width, int height,
            float leftDepth, float rightDepth)
        {
            var texture = new Texture2DArray(width, height, 2,
                TextureFormat.RFloat, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[width * height];
            Array.Fill(pixels, new Color(leftDepth, 0f, 0f, 0f));
            texture.SetPixels(pixels, 0, 0);
            Array.Fill(pixels, new Color(rightDepth, 0f, 0f, 0f));
            texture.SetPixels(pixels, 1, 0);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D MakeDepth2D(int width, int height,
            float depth)
        {
            var texture = new Texture2D(width, height, TextureFormat.RFloat,
                false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[width * height];
            Array.Fill(pixels, new Color(depth, 0f, 0f, 0f));
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static RenderTexture MakeArrayTarget(int width, int height)
        {
            var descriptor = new RenderTextureDescriptor(width, height)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 2,
                graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat,
                enableRandomWrite = true,
                msaaSamples = 1
            };
            var target = new RenderTexture(descriptor);
            target.Create();
            return target;
        }

        private static RenderTexture Make2DTarget(int width, int height,
            GraphicsFormat format = GraphicsFormat.R32G32B32A32_SFloat)
        {
            var descriptor = new RenderTextureDescriptor(width, height)
            {
                dimension = TextureDimension.Tex2D,
                volumeDepth = 1,
                graphicsFormat = format,
                enableRandomWrite = true,
                msaaSamples = 1
            };
            var target = new RenderTexture(descriptor);
            target.Create();
            return target;
        }

        private static Texture2D MakeSolidRgb(int width, int height,
            Color color)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32,
                false, true);
            var pixels = new Color[width * height];
            Array.Fill(pixels, color);
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static void BindPca(ComputeShader compute, int kernel,
            int eye, Texture texture, int width, int height)
        {
            string suffix = eye == 0 ? "Left" : "Right";
            compute.SetTexture(kernel, "_MerkabaCameraRgb" + suffix, texture);
            compute.SetVector("_MerkabaCameraPosition" + suffix, Vector3.zero);
            compute.SetMatrix("_MerkabaCameraInverseRotation" + suffix,
                Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f)));
            compute.SetVector("_MerkabaCameraFocalLength" + suffix,
                new Vector2(width * 0.5f, height * 0.5f));
            compute.SetVector("_MerkabaCameraPrincipalPoint" + suffix,
                new Vector2(width * 0.5f, height * 0.5f));
            compute.SetVector("_MerkabaCameraSensorResolution" + suffix,
                new Vector2(width, height));
            compute.SetVector("_MerkabaCameraCurrentResolution" + suffix,
                new Vector2(width, height));
        }

        private static AsyncGPUReadbackRequest ReadArrayLayer(
            RenderTexture texture, int width, int height, int layer)
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(
                texture, 0, 0, width, 0, height, layer, 1);
            request.WaitForCompletion();
            Assert.That(request.hasError, Is.False,
                $"GPU readback failed for texture-array layer {layer}.");
            return request;
        }

        private static AsyncGPUReadbackRequest Read2D(
            RenderTexture texture, int width, int height)
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(
                texture, 0, 0, width, 0, height, 0, 1);
            request.WaitForCompletion();
            Assert.That(request.hasError, Is.False,
                "GPU readback failed for joint 2D texture.");
            return request;
        }

        private static float DepthNdc(Matrix4x4 projection, float distance)
        {
            Vector4 clip = projection * new Vector4(0, 0, -distance, 1);
            return clip.z / clip.w * 0.5f + 0.5f;
        }

        private static void AssertFinite(float4 value, string label)
        {
            Assert.That(float.IsNaN(value.x) || float.IsInfinity(value.x), Is.False, label);
            Assert.That(float.IsNaN(value.y) || float.IsInfinity(value.y), Is.False, label);
            Assert.That(float.IsNaN(value.z) || float.IsInfinity(value.z), Is.False, label);
            Assert.That(float.IsNaN(value.w) || float.IsInfinity(value.w), Is.False, label);
        }
    }
}
