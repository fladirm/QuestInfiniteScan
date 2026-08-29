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
        public void RepeatedSurface_StrengthensAndRefinesRgb()
        {
            KernelState state = default;
            for (int index = 0; index < 16; index++)
                state.Apply(MerkabaObservationKind.Surface, 0.55f, Red);
            int weakEvidence = state.OccupancyEvidence;
            uint weakConfidence = state.ColorConfidence;
            for (int index = 0; index < 24; index++)
                state.Apply(MerkabaObservationKind.Surface, 1f, Blue);
            Assert.That(state.IsOccupied, Is.True);
            Assert.That(state.OccupancyEvidence, Is.GreaterThan(weakEvidence));
            Assert.That(state.ColorConfidence, Is.GreaterThan(weakConfidence));
            Assert.That(state.Color.b, Is.GreaterThan(state.Color.r));
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
    }
}
