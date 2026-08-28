using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Applies QRS-derived projected observations to reversible kernel evidence. GPU
    /// dispatch/residency is added by the runtime path; this reference path is the exact
    /// semantic oracle used by integration tests and offline replay.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MerkabaIntegrator : MonoBehaviour
    {
        public static MerkabaObservationResult IntegrateObservation(ref KernelState state,
            in MerkabaObservationInput input, Color32 color)
        {
            MerkabaObservationResult result = MerkabaObservation.Classify(input);
            IntegrateClassified(ref state, result.Kind, result.Quality, color);
            return result;
        }

        public static bool IntegrateClassified(ref KernelState state,
            MerkabaObservationKind kind, float quality, Color32 color) =>
            state.Apply(kind, quality, color);
    }
}
