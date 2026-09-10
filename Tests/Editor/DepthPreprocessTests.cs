using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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
                typeof(DepthCapture).GetField("_captureActive",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(capture, true);
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
            Assert.That(callback, Does.Not.Contain("RecordDepthCertificate("));

            string consume = Slice(source,
                "internal bool ConsumeLatestDepthFrame(CommandBuffer command,",
                "public void ReleaseConsumedObservation()");
            Assert.That(consume, Does.Contain(
                "_depthTex = _ownedRawDepth[_heldDepthSlot];"));
            Assert.That(consume, Does.Contain(
                "ApplyStereoRgbdRefinement(command, cameraFrame, fineBrush, gridToWorld);"));
            Assert.That(consume, Does.Not.Contain("ComputeNormals(command);"));
            // Certificate recording moved to the integrator, which owns the
            // bins the certificate is reduced against. The invariant is
            // unchanged: it happens on the observation path, never in the
            // producer callback, and DepthCapture still exposes only that one
            // recording entry point.
            Assert.That(source, Does.Contain(
                "internal void RecordDepthCertificate(CommandBuffer command, MerkabaObservationBinsGpu bins)"));
            Assert.That(RuntimeSource("Runtime/Merkaba/MerkabaIntegrator.cs"),
                Does.Contain("_depthCapture.RecordDepthCertificate(command, _bins);"));
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
            Assert.That(refine, Does.Contain("MerkabaTrySampleCameraRgb(eye,StereoMidpoint(world),captured)"));
            Assert.That(refine, Does.Contain("StereoOppositePlane(support,opposite)"));
            Assert.That(refine, Does.Contain("M8FlowerObservedLoop("));
            Assert.That(refine, Does.Contain("M8FlowerSealBend("));
            Assert.That(refine, Does.Contain("countbits(axes) < 2u"));
            Assert.That(refine, Does.Contain("countbits(candidates) == 1u"));
            Assert.That(refine, Does.Contain("certainCandidates == candidates"));
            Assert.That(refine, Does.Contain("_DstDepth[id] = 0.0;"));
            Assert.That(refine, Does.Contain("_DstNormal[id] = 0.0.xxxx;"));
            Assert.That(refine, Does.Contain("RGBD_HYPOTHESES 5u"));
            Assert.That(refine, Does.Contain("StereoObservationValid()"));
            Assert.That(refine, Does.Contain("!uniqueCorrection && metricPriorAccepted"));
            Assert.That(refine, Does.Contain("StereoIntersects(residual,"));
            Assert.That(refine, Does.Not.Contain("WorldPatchCensus"));
            Assert.That(refine, Does.Not.Contain("JointTangentBasis"));
            Assert.That(refine, Does.Not.Contain("RGBD_MAX_CENSUS_MISMATCH"));
            Assert.That(refine, Does.Not.Contain("RGBD_MAX_METRIC_CORRECTION"));
            Assert.That(refine, Does.Not.Contain(
                "distance(worldPosition, observedWorld)"));
            Assert.That(refine, Does.Not.Contain("stereoMargin"));
            Assert.That(refine, Does.Not.Contain("_DepthSearchRadius"));
            Assert.That(refine, Does.Not.Contain("same UV"));
            Assert.That(refine, Does.Contain("_DepthProjInv[eye]"));
            Assert.That(refine, Does.Contain("_DepthViewInv[eye]"));
            Assert.That(refine, Does.Contain("_DepthProj[0]"));
            Assert.That(refine, Does.Contain(
                "void StereoFlowerRefine(uint2 id"));
            Assert.That(refine, Does.Not.Contain("_DstDepth[id.z]"));
            Assert.That(refine, Does.Not.Contain("ProjectionToLinear"),
                "Environment Depth must use its exact per-eye projection, " +
                "not a generic near/far conversion.");

            string integration = RuntimeSource(
                "Runtime/Shaders/MerkabaFlowerCommit.hlsl");
            Assert.That(integration, Does.Contain(
                "MerkabaTrySampleCameraRgb(0u,world,leftRgb)"));
            Assert.That(integration, Does.Contain(
                "MerkabaTrySampleCameraRgb(1u,world,rightRgb)"));
            Assert.That(integration, Does.Contain("measured.w != 1.0"),
                "The writer may consume only CERTAIN joint measurements.");
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
            int depth = start.IndexOf("_depthCapture.StartDepthCaptureAsync();",
                StringComparison.Ordinal);
            int ready = start.IndexOf("await Task.WhenAll(depthReady,",
                StringComparison.Ordinal);

            Assert.That(prepare, Is.GreaterThanOrEqualTo(0));
            Assert.That(firstYield, Is.GreaterThan(prepare));
            Assert.That(secondYield, Is.GreaterThan(firstYield));
            Assert.That(start, Does.Not.Contain("loadedCoverageReady"));
            Assert.That(start, Does.Not.Contain(
                "WaitForLoadedCoverageReadyAsync"));
            Assert.That(pca, Is.GreaterThan(secondYield));
            Assert.That(depth, Is.GreaterThan(secondYield));
            Assert.That(ready, Is.GreaterThan(depth));
            Assert.That(start.IndexOf("ArmNextObservation();",
                StringComparison.Ordinal), Is.GreaterThan(ready));

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
                "internal void SuspendEnvironmentDepthForApplicationPause()");
            string pause = Slice(depth,
                "internal void SuspendEnvironmentDepthForApplicationPause()",
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
                "(_captureActive || dynamicOcclusionEnabled);"));
            Assert.That(pause, Does.Contain(
                "_arOcclusionManager.enabled = false;"));
            Assert.That(pause, Does.Contain(
                "RestoreEnvironmentDepthAfterApplicationResumeAsync()"));
            Assert.That(pause, Does.Contain(
                "EnsureEnvironmentDepthRunningAsync(true, true)"));
            Assert.That(depth, Does.Contain(
                "_shaderOcclusion.enabled = dynamicOcclusionEnabled &&"));
            Assert.That(depth, Does.Contain(
                "_arOcclusionManager.subsystem.running"));
            string startDepth = Slice(depth,
                "internal async Task<bool> StartDepthCaptureAsync(",
                "private Task<bool> EnsureEnvironmentDepthRunningAsync(");
            Assert.That(startDepth, Does.Contain(
                "await EnsureEnvironmentDepthRunningAsync(false, true)"));
            Assert.That(startDepth, Does.Contain(
                "_latestRawFrameVersion != baselineVersion"));
            Assert.That(startDepth, Does.Contain("HasUnprocessedFrame"));
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
            string submit = Slice(integrator,
                "internal bool TrySubmitObservationAttempt()",
                "private bool TrySubmitNativeObservationAttempt()");
            string retire = Slice(integrator,
                "internal bool TryRetireObservationAttempt()",
                "internal bool TryPrepareFineErase(");
            // A prepared snapshot is never resubmitted: the only thing that may
            // happen to it is retirement, and retirement always releases it.
            Assert.That(submit, Does.Contain("if (_observationPrepared) return false;"));
            Assert.That(retire, Does.Contain("return FinishObservation("));
            Assert.That(retire, Does.Not.Contain("return false;\n            }\n\n            _waiting"));
            Assert.That(integrator, Does.Not.Contain("ObservationTimedOut"),
                "Already-supported work must not expire while awaiting a storage dependency.");

            string shader = RuntimeSource(
                "Runtime/Shaders/MerkabaIntegration.compute");
            Assert.That(shader, Does.Contain(
                "_M8AttemptCompletion[0] = uint4(_M8AttemptToken"));
        }

        [Test]
        public void ObservationMutation_FailsClosedWhileQuestHeadPoseIsInvalid()
        {
            string scanner = RuntimeSource("Runtime/Core/RoomScanner.cs");
            string update = Slice(scanner, "private void Update()",
                "private bool HasTrackedHeadPose()");
            string tracking = Slice(scanner,
                "private bool HasTrackedHeadPose()", "private void OnEnable()");
            int gate = update.IndexOf("if (!HasTrackedHeadPose())",
                StringComparison.Ordinal);
            int submit = update.LastIndexOf("TrySubmitObservationAttempt()",
                StringComparison.Ordinal);

            Assert.That(gate, Is.GreaterThanOrEqualTo(0));
            Assert.That(submit, Is.GreaterThan(gate));
            Assert.That(update, Does.Contain("DiscardReadyDepthFrame()"));
            Assert.That(update, Does.Contain("ArmNextObservation()"));
            Assert.That(tracking, Does.Contain(
                "OVRPlugin.GetNodePositionTracked("));
            Assert.That(tracking, Does.Contain(
                "OVRPlugin.GetNodeOrientationTracked("));
            Assert.That(tracking, Does.Contain("OVRPlugin.Node.EyeCenter"));
        }

        // MerkabaNativeUniformTable rejects a repeated name, and it rejects the
        // whole observation with it. _DepthProjInv was written both by the
        // observation builder and by the certificate writer it calls, which had
        // been latent since the native queue checkpoint because depth never
        // reached this path. When it finally did, every frame stayed held and
        // never became ready: the scan reported active and produced nothing.
        [Test]
        public void NativeObservationUniforms_NameEveryValueExactlyOnce()
        {
            string integrator = Slice(
                RuntimeSource("Runtime/Merkaba/MerkabaIntegrator.cs"),
                "private MerkabaNativeUniformTable BuildNativeObservationUniforms(",
                "private uint NextAttemptToken()");
            string certificate = Slice(
                RuntimeSource("Runtime/Core/DepthCapture.cs"),
                "internal void WriteDepthCertificateUniforms(",
                "internal void BindDepthCertificate(");
            Assert.That(integrator, Does.Contain(
                    "_depthCapture.WriteDepthCertificateUniforms(values)"),
                "the certificate set is written into the same table");

            var names = new List<string>();
            foreach (string source in new[] { integrator, certificate })
                foreach (Match match in Regex.Matches(source,
                             "values\\.\\w+\\(\"([^\"]+)\""))
                    names.Add(match.Groups[1].Value);
            Assert.That(names, Is.Not.Empty);
            var repeated = names.GroupBy(name => name)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key).ToArray();
            Assert.That(repeated, Is.Empty,
                "one writer per native uniform: " + string.Join(", ", repeated));
        }

        [Test]
        public void SaveAndExport_AwaitSharedQuiesceBeforeExplicitOperation()
        {
            string scanner = RuntimeSource("Runtime/Core/RoomScanner.cs");
            string save = Slice(scanner, "public async Task<bool> SaveAsync()",
                "public async Task<bool> LoadAsync()");
            // Both export overloads delegate to one entry point; the
            // quiesce-before-read invariant lives there and still precedes
            // every exporter core call.
            string export = Slice(scanner,
                "private Task<bool> BeginExportAsync(",
                "public async void ClearAllDataAsync");
            // The quiesce now travels inside BeginFrozenWorldOperationAsync,
            // which also stops the dirty-page readout, so the invariant is
            // stated against that entry point. Anchor it at >= 0 first: a bare
            // IndexOf that returns -1 satisfies "less than" vacuously and would
            // let the quiesce disappear unnoticed.
            int saveQuiesce = save.IndexOf(
                "await BeginFrozenWorldOperationAsync()", StringComparison.Ordinal);
            Assert.That(saveQuiesce, Is.GreaterThanOrEqualTo(0));
            Assert.That(saveQuiesce,
                Is.LessThan(save.IndexOf("_persistence.SaveAsync()",
                    StringComparison.Ordinal)));
            string helper = Slice(scanner,
                "private async Task<bool> BeginFrozenWorldOperationAsync()",
                "private void EndFrozenWorldOperation()");
            Assert.That(helper, Does.Contain("await QuiesceScanningAsync()"),
                "the frozen-world entry point must still retire the observation");
            Assert.That(helper, Does.Contain(
                    "await _renderer.FinishCurrentReadoutAsync()"),
                "and must wait the in-flight dirty-page readout out");
            int quiesce = export.IndexOf(
                "await BeginFrozenWorldOperationAsync()", StringComparison.Ordinal);
            Assert.That(quiesce, Is.GreaterThanOrEqualTo(0));
            foreach (string core in new[]
                     {
                         "_exporter.ExportGlbCoreAsync(",
                         "_exporter.ExportViewerPackageCoreAsync("
                     })
                Assert.That(quiesce,
                    Is.LessThan(export.IndexOf(core, StringComparison.Ordinal)), core);
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
                "_DstNormal[id] = float4(selectedNormal,"));
            Assert.That(joint, Does.Contain("float4(selectedNormal,1.0)"));
            Assert.That(joint, Does.Not.Contain("selectedConfidence"));
            Assert.That(joint, Does.Contain("StereoDepthPlane("));
            Assert.That(joint, Does.Contain("StereoOppositePlane("));
        }

        // Includes cold Vulkan driver/pipeline compilation, not just the
        // tiny fixture dispatch. Device frame-time acceptance is separate.
        [TestCase(true), TestCase(false), Timeout(120000)]
        public void JointSolve_TexturelessPlane_KeepsMeasuredDepthOnlyWithOppositeSupport(
            bool oppositeDepthValid)
        {
            const int width = 17;
            const int height = 15;
            ComputeShader compute = LoadCompute("StereoRgbdRefine.compute");
            int kernel = compute.FindKernel("StereoFlowerRefine");
            using var flowerTables = MerkabaGrid.CreateFlowerTableBuffer();
            compute.SetBuffer(kernel, MerkabaGrid.FlowerTablesId, flowerTables);
            Matrix4x4 projection = Matrix4x4.Perspective(90f,
                width / (float)height, 0.1f, 10f);
            float sourceDepth = DepthNdc(projection, 1f);
            Texture2DArray depth = MakeDepth(width, height, sourceDepth,
                oppositeDepthValid ? sourceDepth : 0f);
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
                BindSyntheticStereoBounds(compute);
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
                Assert.That(refined, Is.EqualTo(oppositeDepthValid ? sourceDepth : 0f),
                    "Ambiguous RGB correction must retain stereo-supported measured depth, never invent support when the other depth eye is missing.");
                AssertFinite(normal, "joint normal");
                if (oppositeDepthValid)
                {
                    Assert.That(normal.w, Is.EqualTo(1f));
                    Assert.That(math.dot(normal.xyz, new float3(0f,0f,1f)), Is.GreaterThan(0.999f));
                }
                else Assert.That(normal, Is.EqualTo(float4.zero));

                var metricValues = new uint[refineMetrics.count];
                refineMetrics.GetData(metricValues);
                var radial = new uint[
                    MerkabaGpuTimestamps.RefineMetricValueCount];
                for (int index = 0; index < metricValues.Length; index++)
                    radial[index % radial.Length] += metricValues[index];
                uint measured = 0u, sourceValid = 0u, invalidObservation = 0u;
                for (int bin = 0; bin <
                     MerkabaGpuTimestamps.RefineRadialBinCount; bin++)
                {
                    int offset = bin * MerkabaGpuTimestamps.RefineMetricCount;
                    measured += radial[offset];
                    sourceValid += radial[offset + 8];
                    invalidObservation += radial[offset + 9];
                    uint rejected = radial[offset + 1] + radial[offset + 2] +
                                    radial[offset + 3] + radial[offset + 4];
                    Assert.That(radial[offset], Is.EqualTo(
                        radial[offset + 7] + rejected));
                    Assert.That(radial[offset + 7], Is.EqualTo(
                        radial[offset + 5] + radial[offset + 6]));
                }
                Assert.That(sourceValid, Is.EqualTo(width * height));
                Assert.That(invalidObservation, Is.Zero);
                Assert.That(measured, Is.GreaterThan(0u),
                    "Both cases must reach measured reference depth; only the opposite-eye support differs.");
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

        [Test, Timeout(120000)]
        public void JointSolve_DisjointStereoChromaticIntervalsRejectTheEndpoint()
        {
            const int width = 17;
            const int height = 15;
            ComputeShader compute = LoadCompute("StereoRgbdRefine.compute");
            int kernel = compute.FindKernel("StereoFlowerRefine");
            using var flowerTables = MerkabaGrid.CreateFlowerTableBuffer();
            compute.SetBuffer(kernel, MerkabaGrid.FlowerTablesId, flowerTables);
            Matrix4x4 projection = Matrix4x4.Perspective(90f,
                width / (float)height, 0.1f, 10f);
            float sourceDepth = DepthNdc(projection, 1f);
            Texture2DArray depth = MakeDepth(width, height, sourceDepth,
                sourceDepth);
            Texture2D pcaLeft = MakeSolidRgb(width, height,
                new Color(0.8f, 0.05f, 0.05f, 1f));
            Texture2D pcaRight = MakeSolidRgb(width, height,
                new Color(0.05f, 0.8f, 0.05f, 1f));
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
                Matrix4x4[] views =
                    { Matrix4x4.identity, Matrix4x4.identity };
                compute.SetTexture(kernel, "_SrcDepth", depth);
                compute.SetTexture(kernel, "_DstDepth", outputDepth);
                compute.SetTexture(kernel, "_DstNormal", outputNormal);
                compute.SetInt("_DepthW", width);
                compute.SetInt("_DepthH", height);
                compute.SetMatrixArray("_DepthProj", projections);
                compute.SetMatrixArray("_DepthProjInv", inverseProjections);
                compute.SetMatrixArray("_DepthView", views);
                compute.SetMatrixArray("_DepthViewInv", views);
                BindSyntheticStereoBounds(compute);
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
                Assert.That(refined, Is.Zero,
                    "Disjoint captured radiance intervals cannot become a lower-confidence R1 endpoint.");
                AssertFinite(normal, "rejected joint normal");
                Assert.That(normal, Is.EqualTo(float4.zero));
                var metricValues = new uint[refineMetrics.count];
                refineMetrics.GetData(metricValues);
                uint chromaticRejections = 0u;
                for (int index = 3; index < metricValues.Length;
                     index += MerkabaGpuTimestamps.RefineMetricCount)
                    chromaticRejections += metricValues[index];
                Assert.That(chromaticRejections, Is.GreaterThan(0u),
                    "The synthetic plane must reach both PCA projections and reject their disjoint color, not fail an earlier input gate.");
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

        private static void BindSyntheticStereoBounds(ComputeShader compute)
        {
            // Exercise the exact shipping observation policy, not a synthetic
            // laboratory profile that the real device never supplies.
            var depth = DepthCapture.ObservationDepthBounds;
            var rgb = DepthCapture.ObservationRgbBounds;
            compute.SetVectorArray("_M8DepthErrorBounds", new[] { depth, depth });
            compute.SetVectorArray("_M8RgbErrorBounds", new[] { rgb, rgb });
            compute.SetVector("_M8PlaneErrorBounds", DepthCapture.ObservationPlaneBounds);
            // Avoid making all analytic roots exact sector-boundary cases.
            Matrix4x4 grid = Matrix4x4.Translate(new Vector3(0.00575f, 0.00675f, 0.00775f));
            compute.SetMatrix("_MerkabaGridToWorld", grid);
            compute.SetMatrix("_MerkabaWorldToGrid", grid.inverse);
            compute.SetInt("_M8FineRefineActive", 0);
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
