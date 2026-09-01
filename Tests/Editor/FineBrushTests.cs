using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class FineBrushTests
    {
        private const string Package =
            "Packages/com.genesis.roomscan/";

        [Test]
        public void ExactConeUsesFullApexAngleAndRadialDepth()
        {
            Assert.That(FineBrushDescriptor.TryCreate(Vector3.zero,
                Vector3.forward, Vector3.back, 60f, 2f,
                FineBrushOperation.Refine,
                out FineBrushDescriptor brush), Is.True);

            float half = 30f * Mathf.Deg2Rad;
            Vector3 boundary = new(Mathf.Sin(half) * 1.99f, 0f,
                Mathf.Cos(half) * 1.99f + 0.001f);
            Assert.That(brush.Contains(boundary), Is.True);
            Assert.That(brush.Contains(boundary * 1.01f), Is.False);
            Assert.That(brush.Contains(new Vector3(1.01f, 0f, 1.7f)),
                Is.False);
            Assert.That(brush.Contains(Vector3.back), Is.False);
        }

        [Test]
        public void BrushAxisIsAlwaysEyeToCursor()
        {
            Vector3 eye = new(2f, -1f, 3f);
            Vector3 cursor = new(-1f, 4f, 7f);
            Assert.That(FineBrushDescriptor.TryCreate(eye, cursor,
                Vector3.back, 20f, 4f, FineBrushOperation.Preview,
                out FineBrushDescriptor brush),
                Is.True);
            Assert.That(Vector3.Dot(brush.Axis,
                (cursor - eye).normalized), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(brush.CursorPosition, Is.EqualTo(cursor));
            Assert.That(Vector3.Dot(brush.SurfaceNormal, eye - cursor),
                Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void PreviewProjectsTheExactBrushOntoTheReadoutSurface()
        {
            string scanner = Source("Runtime/Core/RoomScanner.cs");
            string renderer = Source(
                "Runtime/Merkaba/MerkabaGridRenderer.cs");
            string shader = Source("Runtime/Shaders/MerkabaGrid.shader");
            string controller = Source(
                "Runtime/UI/ControllerRayDriver.cs");

            Assert.That(scanner, Does.Contain("SetFineSurfacePreview("));
            Assert.That(renderer, Does.Contain(
                "_finePreviewDescriptor.CosHalfAngleSquared"));
            Assert.That(renderer, Does.Contain(
                "_finePreviewDescriptor.ToolDepthSquared"));
            Assert.That(shader, Does.Contain(
                "input.worldPosition -\n                        _FineEyeOrigin.xyz"));
            Assert.That(shader, Does.Contain(
                "axial * axial >=\n                        distanceSquared * _FineBrushParams.y"));
            Assert.That(shader, Does.Contain(
                "distanceSquared <= _FineBrushParams.z"));
            Assert.That(controller, Does.Not.Contain("FineBrushCone"));
            Assert.That(controller, Does.Not.Contain("BuildFineConeMesh"));
            Assert.That(controller, Does.Contain(
                "float radius = targetDistance * Mathf.Tan(halfAngle)"));
            Assert.That(controller, Does.Contain(
                "Vector3.Distance(descriptor.EyeOrigin,\n" +
                "                descriptor.CursorPosition)"));
            Assert.That(controller, Does.Contain(
                "Quaternion.FromToRotation(Vector3.up, axis)"));
            Assert.That(controller, Does.Contain(
                "descriptor.CursorPosition - axis * 0.001f"));
            Assert.That(controller, Does.Contain(
                "cursorTint.a = Mathf.Clamp01(color.a * 0.5f)"));
            Assert.That(scanner, Does.Contain(
                "TryUpdateFineSurfaceTarget(rayOrigin,"));
            Assert.That(scanner, Does.Contain(
                "rayDirection, fineToolDepth, true,"));
            Assert.That(scanner, Does.Not.Contain("Physics.Raycast("));
            Assert.That(scanner, Does.Not.Contain(
                "FineSurfaceTargetReadbackPending) return"));
            Assert.That(scanner, Does.Not.Contain(
                "eyeOrigin + rayDirection * fineToolDepth"));
            string depthTarget = Source("Runtime/Shaders/DepthNormals.compute");
            Assert.That(depthTarget, Does.Contain(
                "void FineSurfaceTarget(uint3 id : SV_DispatchThreadID)"));
            Assert.That(depthTarget, Does.Contain(
                "gsFineRayOrigin + gsFineRayDirection * hitDistance"));
            Assert.That(depthTarget, Does.Contain(
                "gsFineTarget[1] = valid ? float4(bestNormal, 0.0)"));
        }

        [Test]
        public void FineRefineIsAnEndOfJointSolveMaskAndNotAnotherScanner()
        {
            string refine = Source("Runtime/Shaders/StereoRgbdRefine.compute");
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string scanner = Source("Runtime/Core/RoomScanner.cs");
            string depth = Source("Runtime/Core/DepthCapture.cs");

            int selectedWorld = refine.IndexOf("float3 selectedWorld",
                System.StringComparison.Ordinal);
            int mask = refine.IndexOf("!M8FineContains(selectedWorld)",
                System.StringComparison.Ordinal);
            int publish = refine.IndexOf("_DstDepth[id] = selectedDepth",
                System.StringComparison.Ordinal);
            Assert.That(selectedWorld, Is.GreaterThanOrEqualTo(0));
            Assert.That(mask, Is.GreaterThan(selectedWorld));
            Assert.That(publish, Is.GreaterThan(mask));
            Assert.That(integration, Does.Contain(
                "!M8FineContains(targetWorld)"));
            Assert.That(integration, Does.Contain(
                "!M8FineContains(worldPosition)"));
            Assert.That(integration, Does.Contain(
                "float M8FineWeight(float3 worldPosition)"));
            Assert.That(integration, Does.Contain(
                "quality * quality * fineWeight * MERKABA_SURFACE_SCALE"));
            Assert.That(integration, Does.Contain(
                "evidenceWeight *= M8FineWeight(worldPosition)"));
            Assert.That(scanner, Does.Contain("RequestFreshDepthFrame()"));
            Assert.That(scanner, Does.Contain("_fineMinimumLeftSequence"));
            Assert.That(scanner, Does.Contain("_fineMinimumRightSequence"));
            Assert.That(depth, Does.Contain(
                "fineBrush.IsRefine ? 1 : 0"));
            Assert.That(refine, Does.Not.Contain("AppendSurfaceCandidate"));
        }

        [Test]
        public void HeldFineActionsTrackLiveTargetButFreezeOneCycleDescriptor()
        {
            string scanner = Source("Runtime/Core/RoomScanner.cs");
            string target = Source("Runtime/Core/DepthCapture.cs");
            Assert.That(scanner, Does.Contain(
                "TryCreateFineDescriptor(FineBrushOperation.Refine,\n" +
                "                        out _fineObservationDescriptor)"));
            Assert.That(scanner, Does.Contain(
                "TryCreateFineDescriptor(FineBrushOperation.Erase,"));
            Assert.That(scanner, Does.Contain(
                "TryGetPendingFineDescriptor(action,"));
            Assert.That(scanner, Does.Contain(
                "_finePreviewDescriptor = pendingDescriptor;"));
            Assert.That(scanner, Does.Contain(
                "Vector3.Distance(eyeOrigin, cursorPosition)"));
            Assert.That(scanner, Does.Contain("_fineCycleArmed = false;"));
            Assert.That(target, Does.Contain(
                "if (!allowSubmit || _fineSurfaceTargetReadbackPending"));
            Assert.That(target, Does.Contain(
                "worldTarget = _fineSurfaceTargetWorld;"));
            Assert.That(target, Does.Contain(
                "_fineSurfaceTargetReadbackPending = true;"));
            Assert.That(target, Does.Contain(
                "new ComputeBuffer(2, 16,"));
        }

        [Test]
        public void FineOffKeepsTheOriginalAutomaticObservationPath()
        {
            string scanner = Source("Runtime/Core/RoomScanner.cs");
            string integrator = Source("Runtime/Merkaba/MerkabaIntegrator.cs");
            string input = Source("Runtime/RoomScanInputHandler.cs");
            string refine = Source("Runtime/Shaders/StereoRgbdRefine.compute");

            Assert.That(scanner, Does.Contain("if (fineMode)"));
            Assert.That(scanner, Does.Contain("UpdateFineRefine();"));
            Assert.That(scanner, Does.Contain(
                "_cameraProvider.TryGetSynchronizedFrame(\n                    depthUnixSeconds, availableSkew,\n                    out StereoCameraFrame cameraFrame)"));
            Assert.That(integrator, Does.Contain(
                "return SetStereoCameraData(frame, default);"));
            Assert.That(scanner, Does.Contain(
                "_integrator?.RestoreReadyAutomaticObservationAuthority();"));
            Assert.That(integrator, Does.Contain(
                "_cameraFineBrush[_readyCameraSlot] = default;"));
            Assert.That(refine, Does.Contain(
                "_M8FineRefineActive != 0u &&"));
            Assert.That(input, Does.Contain("OVRInput.RawButton.RIndexTrigger"));
            Assert.That(input, Does.Contain("OVRInput.RawButton.RHandTrigger"));
            Assert.That(input, Does.Not.Contain("GetDown(OVRInput.RawButton"));
        }

        [Test]
        public void FineEraseIsTransactionalCanonicalDeletionNotFreeEvidence()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string integrator = Source(
                "Runtime/Merkaba/MerkabaIntegrator.cs");
            string scanner = Source("Runtime/Core/RoomScanner.cs");

            Assert.That(integration, Does.Contain(
                "#pragma kernel QueryFineEraseTiles"));
            Assert.That(integration, Does.Contain(
                "M8_COUNTER_UNRESOLVED_CARVE_TILES] == 0u"));
            Assert.That(integration, Does.Contain(
                "M8StoreKernelState(physicalSlot, kernelLocal, " +
                "(KernelState)0)"));
            Assert.That(integration, Does.Contain(
                "InterlockedAnd(_M8TileBits[wordIndex].x, ~bit"));
            Assert.That(integration, Does.Contain(
                "InterlockedAnd(_M8TileBits[wordIndex].y, ~bit"));
            Assert.That(integration, Does.Contain(
                "InterlockedAnd(_M8TileBits[wordIndex].z, ~bit"));
            Assert.That(integrator, Does.Contain(
                "TrySubmitFineEraseAttempt()"));
            Assert.That(integrator, Does.Contain(
                "_grid.ResidencyEpoch == _fineEraseResidencyEpoch"));
            Assert.That(scanner, Does.Contain("UpdateFineErase();"));
            Assert.That(scanner, Does.Contain(
                "FinishCurrentFineEraseAsync()"));

            int eraseStart = integration.IndexOf("void M8EraseFineKernel",
                System.StringComparison.Ordinal);
            int eraseEnd = integration.IndexOf(
                "void EraseFineTiles", eraseStart,
                System.StringComparison.Ordinal);
            string erase = integration.Substring(eraseStart,
                eraseEnd - eraseStart);
            Assert.That(erase, Does.Contain(
                "gridPosition + normalGrid * signedOffset"));
            Assert.That(erase, Does.Contain(
                "M8HasSurfacePlane(state.flags)"));
            Assert.That(erase, Does.Not.Contain("UpdateOccupancy"));
            Assert.That(erase, Does.Not.Contain("MERKABA_FREE_SCALE"));
            Assert.That(erase, Does.Not.Contain("gsDepth"));
        }

        private static string Source(string relative) =>
            File.ReadAllText(Package + relative);
    }
}
