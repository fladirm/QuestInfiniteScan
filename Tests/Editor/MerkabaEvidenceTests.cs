using System.Runtime.InteropServices;
using NUnit.Framework;
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
        public void ReplacementContinuity_HoldsOccupiedUntilEndpointExists()
        {
            KernelState foreground = default;
            foreground.SetOccupiedForFixture(true, Red);

            foreground.ApplyWeighted(MerkabaObservationKind.Free, 1f, 1f,
                default, replacementOccupied: false);
            Assert.That(foreground.IsOccupied, Is.True);
            Assert.That(foreground.OccupancyEvidence,
                Is.EqualTo(MerkabaConstants.OccupiedOnThreshold -
                    MerkabaConstants.FreeEvidenceScale));

            foreground.ApplyWeighted(MerkabaObservationKind.Free, 1f, 1f,
                default, replacementOccupied: false);
            Assert.That(foreground.IsOccupied, Is.True);
            Assert.That(foreground.OccupancyEvidence,
                Is.EqualTo(MerkabaConstants.OccupiedOffThreshold + 1));

            foreground.ApplyWeighted(MerkabaObservationKind.Free, 1f, 1f,
                default, replacementOccupied: true);
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
