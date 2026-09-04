using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaEvidenceTests
    {
        private static readonly Color32 Red = new(255, 16, 8, 255);
        private static readonly Color32 Blue = new(8, 32, 255, 255);

        [Test]
        public void KernelStateAbi_RemainsExactlySixteenBytes()
        {
            Assert.That(Marshal.SizeOf<KernelState>(), Is.EqualTo(16));
        }

        [Test]
        public void SurfacePlane_OctahedralNormalRoundTripIsBounded()
        {
            float3[] normals =
            {
                new(1f, 0f, 0f), new(0f, 1f, 0f), new(0f, 0f, 1f),
                math.normalize(new float3(0.17f, -0.63f, 0.76f)),
                math.normalize(new float3(-0.42f, 0.88f, -0.21f))
            };
            foreach (float3 expected in normals)
            {
                uint flags = KernelState.SetSurfacePlane(0u, expected, 0f);
                KernelState.DecodeSurfacePlane(flags, out float3 decoded,
                    out _);
                Assert.That(math.abs(math.dot(math.normalize(expected),
                    decoded)), Is.GreaterThan(0.99999f));
            }
        }

        [Test]
        public void SurfacePlane_OppositeNormalAndOffsetEncodeIdentically()
        {
            float3 normal = math.normalize(new float3(-0.3f, 0.7f, 0.2f));
            uint forward = KernelState.SetSurfacePlane(0u, normal, 0.011f);
            uint reverse = KernelState.SetSurfacePlane(0u, -normal, -0.011f);
            Assert.That(reverse, Is.EqualTo(forward));
        }

        [Test]
        public void SurfacePlane_OffsetEndpointsAndSubMillimetreValueRoundTrip()
        {
            foreach (float expected in new[] { -0.025f, -0.0073f, 0f,
                         0.0091f, 0.025f })
            {
                uint flags = KernelState.SetSurfacePlane(0u,
                    new float3(0f, 0f, 1f), expected);
                KernelState.DecodeSurfacePlane(flags, out _, out float actual);
                Assert.That(actual, Is.EqualTo(expected).Within(
                    MerkabaConstants.SurfacePlaneOffsetRange / 127f * 0.51f));
            }
        }

        [Test]
        public void SurfacePlane_ClearPreservesUnrelatedFlagsAndLegacyIsInvalid()
        {
            uint preserved = MerkabaConstants.OccupiedFlag |
                MerkabaConstants.NeedsCarveFlag;
            uint flags = KernelState.SetSurfacePlane(preserved,
                math.normalize(new float3(1f, 2f, 3f)), 0.004f);
            Assert.That(KernelState.HasSurfacePlane(flags), Is.True);
            Assert.That(KernelState.ClearSurfacePlane(flags), Is.EqualTo(preserved));
            Assert.That(KernelState.HasSurfacePlane(7u << 2), Is.False);
        }

        [Test]
        public void NonOccupiedState_ClearsStaleSurfacePlane()
        {
            KernelState state = default;
            state.SetOccupiedForFixture(true, Red);
            state.Flags = KernelState.SetSurfacePlane(state.Flags,
                new float3(0f, 1f, 0f), 0.003f);
            while (state.IsOccupied)
                state.ApplyWeighted(MerkabaObservationKind.Free, 1f, 1f,
                    default, allowOccupiedClear: true);
            Assert.That(state.HasMeasuredSurfacePlane, Is.False);

            state.SetOccupiedForFixture(true, Red);
            state.Flags = KernelState.SetSurfacePlane(state.Flags,
                new float3(0f, 1f, 0f), 0.003f);
            state.SetOccupiedForFixture(false, default);
            Assert.That(state.IsOccupied, Is.False);
            Assert.That(state.HasMeasuredSurfacePlane, Is.False);
        }

        [Test]
        public void RepeatedSurface_RefinesRgbWithoutBecomingIrreversible()
        {
            KernelState state = default;
            for (int index = 0; index < 16; index++)
                state.Apply(MerkabaObservationKind.Surface, 0.55f, Red);
            int weakEvidence = state.OccupancyEvidence;
            uint weakConfidence = state.ColorConfidence;
            for (int index = 0; index < 24; index++)
                state.Apply(MerkabaObservationKind.Surface, 1f, Blue);
            Assert.That(state.IsOccupied, Is.True);
            Assert.That(state.NeedsCarve, Is.True);
            Assert.That(weakEvidence,
                Is.LessThanOrEqualTo(MerkabaConstants.EvidenceConfidenceLimit));
            Assert.That(state.OccupancyEvidence,
                Is.EqualTo(MerkabaConstants.EvidenceConfidenceLimit));
            Assert.That(state.ColorConfidence, Is.GreaterThan(weakConfidence));
            Assert.That(state.Color.b, Is.GreaterThan(state.Color.r));
        }

        [Test]
        public void GeometryConfidence_AccumulatesAcrossFourConfirmations()
        {
            KernelState state = default;
            for (int confirmation = 1;
                 confirmation <= MerkabaConstants.EvidenceConfirmationCount;
                 confirmation++)
            {
                state.Apply(MerkabaObservationKind.Surface, 1f, Red);
                Assert.That(state.OccupancyEvidence, Is.EqualTo(
                    confirmation * MerkabaConstants.SurfaceEvidenceScale));
            }

            state.Apply(MerkabaObservationKind.Surface, 1f, Blue);
            Assert.That(state.OccupancyEvidence,
                Is.EqualTo(MerkabaConstants.EvidenceConfidenceLimit));
            Assert.That(state.Color.b, Is.GreaterThan(0),
                "geometry confidence saturates without freezing RGB refinement");
        }

        [Test]
        public void ReversibleFreeEvidence_RemovesFalseForeground()
        {
            KernelState state = default;
            state.Apply(MerkabaObservationKind.Surface, 1f, Red);
            Assert.That(state.IsOccupied, Is.True);
            for (int index = 0; index < 16 && state.IsOccupied; index++)
                state.Apply(MerkabaObservationKind.Free, 1f, default);
            Assert.That(state.IsOccupied, Is.False);
            Assert.That(state.OccupancyEvidence,
                Is.LessThanOrEqualTo(MerkabaConstants.OccupiedOffThreshold));
            Assert.That(state.NeedsCarve, Is.True,
                "ordinary FREE keeps the corrective membership active");

            while (state.OccupancyEvidence >
                   MerkabaConstants.ExportKnownFreeThreshold)
                state.Apply(MerkabaObservationKind.Free, 1f, default);
            Assert.That(state.NeedsCarve, Is.False,
                "strong known FREE retires corrective membership");
        }

        [Test]
        public void LaterDepthOnlyEvidence_NeverErasesValidRgb()
        {
            KernelState state = default;
            for (int index = 0; index < 8; index++)
                state.Apply(MerkabaObservationKind.Surface, 1f, Blue);
            uint packed = state.PackedColor;
            uint confidence = state.ColorConfidence;
            for (int index = 0; index < 32; index++)
                state.Apply(MerkabaObservationKind.Free, 1f, default);
            Assert.That(state.PackedColor, Is.EqualTo(packed));
            Assert.That(state.ColorConfidence, Is.EqualTo(confidence));
        }

        [Test]
        public void UnknownObservation_IsNonDestructive()
        {
            KernelState state = default;
            state.Apply(MerkabaObservationKind.Surface, 1f, Red);
            KernelState before = state;
            Assert.That(state.Apply(MerkabaObservationKind.Unknown, 1f, Blue),
                Is.False);
            Assert.That(state.OccupancyEvidence,
                Is.EqualTo(before.OccupancyEvidence));
            Assert.That(state.PackedColor, Is.EqualTo(before.PackedColor));
            Assert.That(state.ColorConfidence, Is.EqualTo(before.ColorConfidence));
            Assert.That(state.Flags, Is.EqualTo(before.Flags));
        }

        [Test]
        public void FreeOnNeverObservedState_DoesNotCreateCarveMembership()
        {
            KernelState state = default;
            state.Apply(MerkabaObservationKind.Free, 1f, default);
            Assert.That(state.IsOccupied, Is.False);
            Assert.That(state.NeedsCarve, Is.False);
            Assert.That(state.PackedColor, Is.Zero);
            Assert.That(state.ColorConfidence, Is.Zero);
        }

        [Test]
        public void DistanceWeightedReplacement_CorrectsEveryForegroundLayerBoundedly()
        {
            KernelState firstBlob = default;
            KernelState secondBlob = default;
            KernelState replacement = default;
            for (int pass = 0;
                 pass < MerkabaConstants.EvidenceConfirmationCount; pass++)
            {
                firstBlob.Apply(MerkabaObservationKind.Surface, 1f, Red);
                secondBlob.Apply(MerkabaObservationKind.Surface, 1f, Red);
            }

            replacement.Apply(MerkabaObservationKind.Surface, 1f, Blue);
            firstBlob.ApplyWeighted(MerkabaObservationKind.Free, 1f, 1f,
                default, replacement.IsOccupied);
            secondBlob.ApplyWeighted(MerkabaObservationKind.Free, 1f, 0.4f,
                default, replacement.IsOccupied);

            Assert.That(replacement.IsOccupied, Is.True);
            Assert.That(replacement.Color.b, Is.GreaterThan(
                replacement.Color.r));
            Assert.That(firstBlob.IsOccupied, Is.True,
                "one FREE observation must not bypass hysteresis");
            Assert.That(secondBlob.IsOccupied, Is.True);
            Assert.That(firstBlob.OccupancyEvidence, Is.EqualTo(
                MerkabaConstants.EvidenceConfidenceLimit -
                MerkabaConstants.FreeEvidenceScale));
            Assert.That(secondBlob.OccupancyEvidence, Is.EqualTo(
                MerkabaConstants.EvidenceConfidenceLimit -
                Mathf.RoundToInt(0.4f * MerkabaConstants.FreeEvidenceScale)));

            int firstIterations = 1;
            while (firstBlob.IsOccupied && firstIterations++ < 32)
                firstBlob.ApplyWeighted(MerkabaObservationKind.Free, 1f, 1f,
                    default, true);
            int secondIterations = 1;
            while (secondBlob.IsOccupied && secondIterations++ < 64)
                secondBlob.ApplyWeighted(MerkabaObservationKind.Free, 1f, 0.4f,
                    default, true);

            Assert.That(firstIterations, Is.EqualTo(10));
            Assert.That(secondIterations, Is.EqualTo(24));
            Assert.That(firstBlob.IsOccupied, Is.False);
            Assert.That(secondBlob.IsOccupied, Is.False);
        }

        [Test]
        public void CarveClearAuthority_HoldsOccupiedUntilExactGateAllowsClear()
        {
            KernelState foreground = default;
            foreground.SetOccupiedForFixture(true, Red);

            foreground.ApplyWeighted(MerkabaObservationKind.Free, 1f, 1f,
                default, allowOccupiedClear: false);
            Assert.That(foreground.IsOccupied, Is.True);
            Assert.That(foreground.OccupancyEvidence,
                Is.EqualTo(MerkabaConstants.OccupiedOnThreshold -
                    MerkabaConstants.FreeEvidenceScale));

            foreground.ApplyWeighted(MerkabaObservationKind.Free, 1f, 1f,
                default, allowOccupiedClear: false);
            Assert.That(foreground.IsOccupied, Is.True);
            Assert.That(foreground.OccupancyEvidence,
                Is.EqualTo(MerkabaConstants.OccupiedOffThreshold + 1));

            foreground.ApplyWeighted(MerkabaObservationKind.Free, 1f, 1f,
                default, allowOccupiedClear: true);
            Assert.That(foreground.IsOccupied, Is.False);
            Assert.That(foreground.OccupancyEvidence,
                Is.LessThanOrEqualTo(MerkabaConstants.OccupiedOffThreshold));
        }

        [Test]
        public void FreeDistanceWeight_IsContinuousMonotonicAndQrsTruncated()
        {
            float[] clearances =
            {
                -0.01f, 0f, MerkabaConstants.HalfSupport,
                0.050f, 0.075f, 0.100f, 0.125f,
                MerkabaConstants.FreeFullClearance, 1f
            };
            float[] expected = { 0f, 0f, 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f, 1f };
            float previous = -1f;
            for (int index = 0; index < clearances.Length; index++)
            {
                float actual = MerkabaObservation.FreeDistanceWeight(
                    clearances[index]);
                Assert.That(actual, Is.EqualTo(expected[index]).Within(1e-5f),
                    $"clearance {clearances[index]:F3} m");
                Assert.That(actual, Is.GreaterThanOrEqualTo(previous));
                previous = actual;
            }
        }

        [Test]
        public void FrozenJointRay_FreeExistsOnlyBeforeEndpointInsideSupportTube()
        {
            const float endpoint = 2f;
            Assert.That(MerkabaObservation.InsideFrozenFreeRayTube(
                1.5f, endpoint, 0f), Is.True);
            Assert.That(MerkabaObservation.InsideFrozenFreeRayTube(
                1.5f, endpoint, MerkabaConstants.HalfSupport), Is.True);
            Assert.That(MerkabaObservation.InsideFrozenFreeRayTube(
                endpoint - MerkabaConstants.HalfSupport, endpoint, 0f),
                Is.False, "The support band at H is not FREE.");
            Assert.That(MerkabaObservation.InsideFrozenFreeRayTube(
                endpoint + 0.1f, endpoint, 0f), Is.False,
                "Nothing behind H is FREE.");
            Assert.That(MerkabaObservation.InsideFrozenFreeRayTube(
                1.5f, endpoint, MerkabaConstants.HalfSupport + 0.0001f),
                Is.False, "A neighbouring ray cannot carve this owner.");
            Assert.That(MerkabaObservation.InsideFrozenFreeRayTube(
                -0.1f, endpoint, 0f), Is.False);
        }

        [Test]
        public void CheapCarveGate_ContainsEveryExactSurfaceOrFreeCase()
        {
            var random = new System.Random(0x5134);
            for (int iteration = 0; iteration < 10000; iteration++)
            {
                bool surface = (iteration & 7) == 0;
                float endpoint = 0.051f + (float)random.NextDouble() * 4.9f;
                float along = surface
                    ? endpoint
                    : 0.001f + (float)random.NextDouble() *
                      Mathf.Max(0.001f, endpoint -
                          MerkabaConstants.HalfSupport - 0.001f);
                float perpendicular = (float)random.NextDouble() *
                    MerkabaConstants.HalfSupport;
                MerkabaObservationResult exact = MerkabaObservation.Classify(
                    Input(replacementSurfaceValid: true,
                        isReplacementKernel: surface,
                        kernelDistance: along,
                        measuredDistance: endpoint));
                if (exact.Kind == MerkabaObservationKind.Unknown) continue;
                MerkabaCheapCarveGateResult gate =
                    MerkabaObservation.CheapFrozenRayGate(
                        projectionDepthValid: true,
                        outsideExclusions: true,
                        kernelInFront: true,
                        isSurfaceEndpoint: surface,
                        kernelAlong: along,
                        endpointDistance: endpoint,
                        perpendicularDistance: perpendicular,
                        insideOuterAttention: true);
                Assert.That(gate,
                    Is.EqualTo(MerkabaCheapCarveGateResult.Candidate),
                    $"iteration={iteration} kind={exact.Kind}");
            }
        }

        [Test]
        public void CheapCarveGate_BoundariesAreConservativeAndExact()
        {
            const float endpoint = 2f;
            float half = MerkabaConstants.HalfSupport;
            Assert.That(MerkabaObservation.CheapFrozenRayGate(true, true,
                true, false, 1f, endpoint, half, true),
                Is.EqualTo(MerkabaCheapCarveGateResult.Candidate));
            Assert.That(MerkabaObservation.CheapFrozenRayGate(true, true,
                true, false, endpoint - half, endpoint, 0f, true),
                Is.EqualTo(MerkabaCheapCarveGateResult.OutsideRayTube));
            Assert.That(MerkabaObservation.CheapFrozenRayGate(true, true,
                true, true, endpoint, endpoint, 0f, true),
                Is.EqualTo(MerkabaCheapCarveGateResult.Candidate));
            Assert.That(MerkabaObservation.CheapFrozenRayGate(true, true,
                true, false, 1f, endpoint, half + 1e-5f, true),
                Is.EqualTo(MerkabaCheapCarveGateResult.OutsideRayTube));
            Assert.That(MerkabaObservation.CheapFrozenRayGate(true, true,
                true, false, 1f, endpoint, 0f, false),
                Is.EqualTo(
                    MerkabaCheapCarveGateResult.OutsideOuterAttention));
            Assert.That(MerkabaObservation.CheapFrozenRayGate(true, true,
                false, false, -0.01f, endpoint, 0f, true),
                Is.EqualTo(MerkabaCheapCarveGateResult.NotInFront));
        }

        [Test]
        public void ReplacementEndpoint_OwnsSurfaceAndOnlyForegroundIsFree()
        {
            MerkabaObservationResult endpoint = MerkabaObservation.Classify(
                Input(replacementSurfaceValid: true,
                    isReplacementKernel: true, kernelDistance: 2f,
                    measuredDistance: 2f));
            MerkabaObservationResult foreground = MerkabaObservation.Classify(
                Input(replacementSurfaceValid: true,
                    isReplacementKernel: false, kernelDistance: 1.5f,
                    measuredDistance: 2f));
            MerkabaObservationResult nearSurface = MerkabaObservation.Classify(
                Input(replacementSurfaceValid: true,
                    isReplacementKernel: false, kernelDistance: 1.98f,
                    measuredDistance: 2f));
            MerkabaObservationResult behind = MerkabaObservation.Classify(
                Input(replacementSurfaceValid: true,
                    isReplacementKernel: false, kernelDistance: 2.5f,
                    measuredDistance: 2f));
            MerkabaObservationResult noReplacement = MerkabaObservation.Classify(
                Input(replacementSurfaceValid: false,
                    isReplacementKernel: false, kernelDistance: 1.5f,
                    measuredDistance: 2f));

            Assert.That(endpoint.Kind, Is.EqualTo(MerkabaObservationKind.Surface));
            Assert.That(foreground.Kind, Is.EqualTo(MerkabaObservationKind.Free));
            Assert.That(foreground.EvidenceWeight, Is.GreaterThan(0f));
            Assert.That(nearSurface.Kind,
                Is.EqualTo(MerkabaObservationKind.Unknown));
            Assert.That(nearSurface.EvidenceWeight, Is.Zero);
            Assert.That(behind.Kind, Is.EqualTo(MerkabaObservationKind.Unknown));
            Assert.That(noReplacement.Kind,
                Is.EqualTo(MerkabaObservationKind.Unknown));
        }

        private static MerkabaObservationInput Input(
            bool replacementSurfaceValid, bool isReplacementKernel,
            float kernelDistance, float measuredDistance) => new(
            depthValid: true, insideFrustum: true, outsideExclusions: true,
            replacementSurfaceValid: replacementSurfaceValid,
            isReplacementKernel: isReplacementKernel,
            kernelEyeDistance: kernelDistance,
            measuredEyeDistance: measuredDistance,
            kernelViewDepthLinear: kernelDistance,
            measuredDepthLinear: measuredDistance,
            dilatedDepthLinear: measuredDistance,
            normalFacing: 1f, maxUpdateDistance: 5f);
    }
}
