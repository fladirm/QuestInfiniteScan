using System;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Immutable authority record consumed by later scanner stages. It binds the
    /// NumericDomain, generated algebra and exact plan bundle without introducing
    /// another arithmetic implementation.
    /// </summary>
    public sealed class SigmaOperatorSet
    {
        private SigmaOperatorSet()
        {
            if (!string.Equals(SigmaGeneratedAlgebra.NumericDomainId,
                    SigmaNumericDomain.Id, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Generated algebra NumericDomain does not match semantic truth.");
            if (SigmaGeneratedAlgebra.LaneCount != SigmaS16.LaneCount)
                throw new InvalidOperationException("Generated algebra is not S16.");
            if (SigmaGeneratedAlgebra.GeometryRows.Length != 4 ||
                SigmaGeneratedAlgebra.HiddenRows.Length != 12)
                throw new InvalidOperationException("Generated readout basis is incomplete.");
        }

        public static SigmaOperatorSet Canonical { get; } = new();

        public string NumericDomainId => SigmaNumericDomain.Id;
        public string GeneratedBundleFingerprint =>
            SigmaGeneratedAlgebra.BundleFingerprint;
        public string ExactPlanBundleFingerprint =>
            SigmaOperatorPlans.PlanBundleFingerprint;
        public SigmaS16 NullState => SigmaS16Operators.NullState;

        public void RequireCanonicalBackend(SigmaBackendCapabilityProfile backend,
            SigmaOperatorPlan plan)
        {
            if (backend == null)
                throw new ArgumentNullException(nameof(backend));
            backend.RequireCanonical(plan);
        }
    }
}
