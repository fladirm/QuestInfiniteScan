using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class FineBrushTests
    {
        private const string Package =
            "Packages/com.genesis.roomscan/";

        [Test]
        public void ExactControllerCylinderUsesCursorRadiusAndLength()
        {
            Assert.That(FineBrushDescriptor.TryCreate(
                new Vector3(1f, 2f, 3f), Vector3.back, Vector3.forward,
                0.1f, 0.5f, FineBrushOperation.Refine,
                out FineBrushDescriptor brush), Is.True);

            Assert.That(brush.Contains(new Vector3(1.099f, 2f, 3.25f)),
                Is.True);
            Assert.That(brush.Contains(new Vector3(1.1001f, 2f, 3.25f)),
                Is.False);
            Assert.That(brush.Contains(new Vector3(1f, 2f, 2.999f)),
                Is.False);
            Assert.That(brush.Contains(new Vector3(1f, 2f, 3.501f)),
                Is.False);
        }

        [Test]
        public void BrushAxisIsControllerRayAndBoundsEncloseCylinder()
        {
            Vector3 cursor = new(-1f, 4f, 7f);
            Vector3 controllerRay = new(1f, 2f, 3f);
            Assert.That(FineBrushDescriptor.TryCreate(cursor,
                Vector3.forward, controllerRay, 0.2f, 0.8f,
                FineBrushOperation.Preview, out FineBrushDescriptor brush),
                Is.True);

            Assert.That(Vector3.Dot(brush.Axis, controllerRay.normalized),
                Is.EqualTo(1f).Within(1e-6f));
            Assert.That(brush.CursorPosition, Is.EqualTo(cursor));
            Assert.That(Vector3.Dot(brush.SurfaceNormal, -brush.Axis),
                Is.GreaterThanOrEqualTo(0f));
            Assert.That(brush.BoundsCenter,
                Is.EqualTo(cursor + brush.Axis * 0.4f));
            Assert.That(brush.BoundsRadius,
                Is.EqualTo(Mathf.Sqrt(0.2f * 0.2f + 0.4f * 0.4f))
                    .Within(1e-6f));
        }

        [Test]
        public void PreviewAndMutationUseTheSameExactCylinder()
        {
            string scanner = Source("Runtime/Core/RoomScanner.cs");
            string renderer = Source(
                "Runtime/Merkaba/MerkabaGridRenderer.cs");
            string shader = Source("Runtime/Shaders/MerkabaGrid.shader");
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string scope = Source("Runtime/Shaders/MerkabaObservationScope.hlsl");
            string refine = Source("Runtime/Shaders/StereoRgbdRefine.compute");
            string controller = Source(
                "Runtime/UI/ControllerRayDriver.cs");

            Assert.That(scanner, Does.Contain("SetFineSurfacePreview("));
            Assert.That(scanner, Does.Contain(
                "surfaceNormal, rayDirection, fineBrushRadius, fineToolLength"));
            Assert.That(scanner, Does.Not.Contain("Physics.Raycast("));
            Assert.That(renderer, Does.Contain(
                "_finePreviewDescriptor.Radius *"));
            Assert.That(renderer, Does.Contain(
                "_finePreviewDescriptor.Length"));
            AssertCylinderPredicate(shader, "_FineCursorPosition.xyz",
                "_FineBrushAxis.xyz", "_FineBrushParams.z",
                "_FineBrushParams.y");
            AssertCylinderPredicate(scope, "_M8FineCursorPosition",
                "_M8FineBrushAxis", "_M8FineLength",
                "_M8FineRadiusSquared");
            Assert.That(integration, Does.Contain("M8FineContains(worldPosition)"));
            Assert.That(integration, Does.Contain("axial.lo < 0.0 || axial.hi > _M8FineLength"),
                "Destructive whole-support mutations must keep the entire interval inside the same cylinder.");
            Assert.That(integration, Does.Contain("radial.hi <= _M8FineRadiusSquared"));
            AssertCylinderPredicate(refine, "_M8FineCursorPosition",
                "_M8FineBrushAxis", "_M8FineLength",
                "_M8FineRadiusSquared");
            Assert.That(controller, Does.Contain(
                "descriptor.CursorPosition + axis * (descriptor.Length * 0.5f)"));
            Assert.That(controller, Does.Contain(
                "descriptor.Radius * 2f, descriptor.Length * 0.5f"));
        }

        [Test]
        public void FineSurfaceTargetIsOneParallelWorkgroup()
        {
            string target = Source("Runtime/Shaders/DepthNormals.compute");
            string depth = Source("Runtime/Core/DepthCapture.cs");
            string body = Slice(target, "[numthreads(128, 1, 1)]\n" +
                "void FineSurfaceTarget", "// Editor simulation");

            Assert.That(body, Does.Contain("uint lane : SV_GroupIndex"));
            Assert.That(body, Does.Contain("gFineDepthDelta[lane + 1u]"));
            Assert.That(body, Does.Contain(
                "lowerDelta < 0.0 &&\n        upperDelta >= 0.0"));
            Assert.That(body, Does.Contain("InterlockedMin(gFineWinnerRank"));
            Assert.That(body, Does.Contain("GroupMemoryBarrierWithGroupSync"));
            Assert.That(body, Does.Not.Contain("for ("));
            Assert.That(body, Does.Not.Contain("while ("));
            Assert.That(target, Does.Not.Contain("binary"));
            Assert.That(depth, Does.Contain(
                "command.DispatchCompute(depthNormalCompute, kernel, 1, 1, 1)"));
            Assert.That(depth, Does.Not.Contain("_nextFineSurfaceTarget"));
            Assert.That(depth, Does.Contain(
                "if (!allowSubmit || _fineSurfaceTargetReadbackPending)"));
            Assert.That(depth, Does.Contain("new ComputeBuffer(2, 16,"));
        }

        [Test]
        public void FineRefineMasksJointSolveAndUsesTheSharedR1Admission()
        {
            string refine = Source("Runtime/Shaders/StereoRgbdRefine.compute");
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string commit = Source("Runtime/Shaders/MerkabaFlowerCommit.hlsl");
            string bins = Source("Runtime/Shaders/MerkabaObservationBins.compute");
            string scanner = Source("Runtime/Core/RoomScanner.cs");

            int selectedWorld = refine.IndexOf("float3 selectedWorld",
                StringComparison.Ordinal);
            int mask = refine.IndexOf("if (_M8FineRefineActive != 0u)",
                StringComparison.Ordinal);
            int publish = refine.IndexOf("_DstDepth[id] = depth;",
                StringComparison.Ordinal);
            Assert.That(selectedWorld, Is.GreaterThanOrEqualTo(0));
            Assert.That(mask, Is.GreaterThan(selectedWorld));
            Assert.That(publish, Is.GreaterThan(mask));
            Assert.That(integration, Does.Contain(
                "!M8FineContains(worldPosition)"));
            Assert.That(commit, Does.Contain("M8ObservationContains(worldPosition,gsDepthEyePos())"));
            Assert.That(bins, Does.Contain("M8ObservationContains(world,gsDepthEyePos())"));
            Assert.That(commit, Does.Contain("M8FlowerAdmitR1("));
            Assert.That(commit, Does.Not.Contain("MERKABA_OCCUPIED_ON - max(state.evidence, 0)"),
                "FINE selects the measurement support; it cannot bypass first-hit R1 seed/closure rules.");
            Assert.That(integration, Does.Not.Contain("M8FineWeight"));
            Assert.That(integration, Does.Not.Contain("fineWeight"));
            Assert.That(scanner, Does.Contain("RequestFreshDepthFrame()"));
            Assert.That(refine, Does.Not.Contain("AppendSurfaceCandidate"));
        }

        [Test]
        public void FineStrictFirstHitRemainsASeedUntilR1ClosureOrNewObservation()
        {
            uint plane = KernelState.SetSurfacePlane(0u, new float3(0f, 0f, 1f), 0f);
            uint4 seed = MerkabaSphereFlowerAuthority.M8FlowerAdmitR1(
                uint4.zero, plane, 0xff556677u, false, true, false);
            Assert.That(seed.w & MerkabaConstants.OccupiedFlag, Is.Zero);
            Assert.That(seed.w & MerkabaConstants.R1SeedFlag, Is.Not.Zero);
            Assert.That(math.asint(seed.x), Is.LessThan(MerkabaConstants.OccupiedOnThreshold));

            uint4 sameObservation = MerkabaSphereFlowerAuthority.M8FlowerAdmitR1(
                seed, plane, 0xff556677u, true, false, false);
            Assert.That(sameObservation.w & MerkabaConstants.OccupiedFlag, Is.Zero);
            uint4 repeated = MerkabaSphereFlowerAuthority.M8FlowerAdmitR1(
                seed, plane, 0xff556677u, true, true, false);
            uint4 localClosure = MerkabaSphereFlowerAuthority.M8FlowerAdmitR1(
                uint4.zero, plane, 0xff556677u, false, false, true);
            Assert.That(repeated.w & MerkabaConstants.OccupiedFlag, Is.Not.Zero);
            Assert.That(localClosure.w & MerkabaConstants.OccupiedFlag, Is.Not.Zero);
            Assert.That(repeated.w & MerkabaConstants.R1SeedFlag, Is.Zero);
            Assert.That(localClosure.w & MerkabaConstants.R1SeedFlag, Is.Zero);
        }

        [Test]
        public void HeldFineActionsFreezeOnlyOneOperationDescriptor()
        {
            string scanner = Source("Runtime/Core/RoomScanner.cs");
            string target = Source("Runtime/Core/DepthCapture.cs");
            string preview = Slice(scanner, "private void UpdateFinePreview()",
                "private bool TryGetPendingFineDescriptor");
            string erase = Slice(scanner, "private void UpdateFineErase()",
                "private void RestartFineCycleAfterExpiredDepth");

            Assert.That(preview.IndexOf("TryGetPendingFineDescriptor",
                    StringComparison.Ordinal),
                Is.LessThan(preview.IndexOf("TryCreateFineDescriptor(",
                    StringComparison.Ordinal)));
            Assert.That(scanner, Does.Contain(
                "out _fineObservationDescriptor"));
            Assert.That(scanner, Does.Contain(
                "TryCreateFineDescriptor(FineBrushOperation.Erase,"));
            Assert.That(scanner, Does.Contain(
                "FineSurfaceTargetCompletedSequence <="));
            Assert.That(target, Does.Contain(
                "_fineSurfaceTargetReadbackPending = true;"));
            Assert.That(target, Does.Contain(
                "_fineSurfaceTargetCompletedSequence = querySequence;"));
            Assert.That(target, Does.Contain("RequestNextDepthFrame();"));
            Assert.That(erase, Does.Not.Contain("IntegrationInterval"));
            Assert.That(erase, Does.Not.Contain("_lastIntegrationTime"));
        }

        [Test]
        public void FineOffKeepsOriginalAutomaticObservationPath()
        {
            string scanner = Source("Runtime/Core/RoomScanner.cs");
            string integrator = Source("Runtime/Merkaba/MerkabaIntegrator.cs");
            string input = Source("Runtime/RoomScanInputHandler.cs");

            Assert.That(scanner, Does.Contain("if (_fineAuthorityActive)"));
            Assert.That(scanner, Does.Contain("UpdateFineRefine();"));
            Assert.That(integrator, Does.Contain(
                "return SetStereoCameraData(frame, default);"));
            Assert.That(integrator, Does.Contain(
                "internal bool TrySwitchObservationAuthority()"));
            Assert.That(scanner, Does.Contain(
                "_integrator.TrySwitchObservationAuthority()"));
            Assert.That(input, Does.Contain("OVRInput.RawButton.RIndexTrigger"));
            Assert.That(input, Does.Contain("OVRInput.RawButton.RHandTrigger"));
        }

        [Test]
        public void FineAuthorityAndControllerPoseFailClosed()
        {
            string scanner = Source("Runtime/Core/RoomScanner.cs");
            string integrator = Source(
                "Runtime/Merkaba/MerkabaIntegrator.cs");
            string controller = Source(
                "Runtime/UI/ControllerRayDriver.cs");

            int retireErase = scanner.IndexOf(
                "_integrator.TryRetireFineEraseAttempt()",
                StringComparison.Ordinal);
            int retireObservation = scanner.IndexOf(
                "_integrator.TryRetireObservationAttempt()",
                StringComparison.Ordinal);
            int switchAuthority = scanner.IndexOf(
                "UpdateFineAuthorityBoundary();", StringComparison.Ordinal);
            Assert.That(switchAuthority, Is.GreaterThan(retireErase));
            Assert.That(switchAuthority, Is.GreaterThan(retireObservation));
            Assert.That(integrator, Does.Contain(
                "_observationPrepared || _attemptInFlight ||"));
            Assert.That(controller, Does.Contain(
                "!OVRInput.GetControllerPositionTracked(controller) ||"));
            Assert.That(controller, Does.Contain(
                "!OVRInput.GetControllerOrientationTracked(controller)"));
        }

        [Test]
        public void FineEraseIsTransactionalImmediateCanonicalDeletion()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string integrator = Source(
                "Runtime/Merkaba/MerkabaIntegrator.cs");
            string scanner = Source("Runtime/Core/RoomScanner.cs");
            string erase = Slice(integration, "void M8EraseFineKernel",
                "void EraseFineTiles");

            Assert.That(integration, Does.Contain(
                "#pragma kernel QueryFineEraseTiles"));
            Assert.That(integration, Does.Contain(
                "M8_COUNTER_UNRESOLVED_OBSERVATION_TILES] == 0u"));
            Assert.That(erase, Does.Contain(
                "M8StoreKernelState(physicalSlot, kernelLocal, (KernelState)0)"));
            Assert.That(erase, Does.Contain(
                "InterlockedAnd(_M8TileBits[wordIndex].x, ~bit"));
            Assert.That(erase, Does.Contain(
                "InterlockedAnd(_M8TileBits[wordIndex].y, ~bit"));
            Assert.That(erase, Does.Contain(
                "InterlockedAnd(_M8TileBits[wordIndex].z, ~bit"));
            Assert.That(erase, Does.Contain(
                "gridPosition + normalGrid * signedOffset"));
            Assert.That(erase, Does.Not.Contain("UpdateOccupancy"));
            Assert.That(erase, Does.Not.Contain("MERKABA_FREE_SCALE"));
            Assert.That(integrator, Does.Contain(
                "_grid.ResidencyEpoch == _fineEraseResidencyEpoch"));
            Assert.That(scanner, Does.Contain("UpdateFineErase();"));
            Assert.That(scanner, Does.Contain(
                "FinishCurrentFineEraseAsync()"));
        }

        [Test]
        public void ObsoleteEyeConeAuthorityIsAbsent()
        {
            string all = Source("Runtime/Core/FineBrushDescriptor.cs") +
                Source("Runtime/Core/RoomScanner.cs") +
                Source("Runtime/Core/DepthCapture.cs") +
                Source("Runtime/Merkaba/MerkabaIntegrator.cs") +
                Source("Runtime/Shaders/MerkabaIntegration.compute") +
                Source("Runtime/Shaders/StereoRgbdRefine.compute");
            Assert.That(all, Does.Not.Contain("FineEyeOrigin"));
            Assert.That(all, Does.Not.Contain("FineCosHalfAngleSquared"));
            Assert.That(all, Does.Not.Contain("TryGetCyclopeanEyeOrigin"));
            Assert.That(all, Does.Not.Contain("Brush angle"));
        }

        private static void AssertCylinderPredicate(string source,
            string cursor, string axis, string length, string radiusSquared)
        {
            source = Regex.Replace(source, @"\s+", string.Empty);
            Assert.That(source, Does.Contain(cursor));
            Assert.That(source, Does.Contain(axis));
            Assert.That(source, Does.Contain("axial>=0.0"));
            Assert.That(source, Does.Contain("axial<=" + length));
            Assert.That(source, Does.Contain(
                "dot(radial,radial)<=" + radiusSquared));
        }

        private static string Slice(string source, string start, string end)
        {
            int first = source.IndexOf(start, StringComparison.Ordinal);
            int last = source.IndexOf(end, first + start.Length,
                StringComparison.Ordinal);
            Assert.That(first, Is.GreaterThanOrEqualTo(0), start);
            Assert.That(last, Is.GreaterThan(first), end);
            return source.Substring(first, last - first);
        }

        private static string Source(string relative) =>
            File.ReadAllText(Package + relative);
    }
}
