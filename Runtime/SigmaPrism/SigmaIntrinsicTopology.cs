using System;
using System.Numerics;

namespace Genesis.RoomScan.SigmaPrism
{
    public enum SigmaTopologyClass : byte
    {
        Regular = 0,
        Singular = 1,
        Unresolved = 2,
        Unsupported = 3
    }

    public readonly struct SigmaIntrinsicTopologySignature
    {
        internal SigmaIntrinsicTopologySignature(SigmaTopologyClass classification,
            SigmaS16 transition, int annihilatorId, BigInteger annihilatorError,
            BigInteger transitionScale, BigInteger associatorError,
            BigInteger associatorScale, bool nearAnnihilator,
            bool exactAnnihilator, bool associatorStrong, bool contactNull,
            uint firstKey, uint secondKey)
        {
            Classification = classification;
            Transition = transition;
            AnnihilatorId = annihilatorId;
            AnnihilatorError = annihilatorError;
            TransitionScale = transitionScale;
            AssociatorError = associatorError;
            AssociatorScale = associatorScale;
            NearAnnihilator = nearAnnihilator;
            ExactAnnihilator = exactAnnihilator;
            AssociatorStrong = associatorStrong;
            ContactNull = contactNull;
            FirstIndependenceKey = firstKey;
            SecondIndependenceKey = secondKey;
        }

        public SigmaTopologyClass Classification { get; }
        public SigmaS16 Transition { get; }
        public int AnnihilatorId { get; }
        public BigInteger AnnihilatorError { get; }
        public BigInteger TransitionScale { get; }
        public BigInteger AssociatorError { get; }
        public BigInteger AssociatorScale { get; }
        public bool NearAnnihilator { get; }
        public bool ExactAnnihilator { get; }
        public bool AssociatorStrong { get; }
        public bool ContactNull { get; }
        public uint FirstIndependenceKey { get; }
        public uint SecondIndependenceKey { get; }
    }

    /// <summary>
    /// Exact semantic oracle for the intrinsic topology readout of Psi. Production
    /// uses the equivalent GPU plan; this type owns no topology or geometry state.
    /// </summary>
    public static class SigmaIntrinsicTopology
    {
        public const int DefaultSingularShift = 6;
        public const int DefaultAssociatorShift = 5;

        public static SigmaIntrinsicTopologySignature EvaluateCell(
            SigmaS16 center, SigmaS16 right, SigmaS16 down,
            uint firstIndependenceKey, uint secondIndependenceKey,
            bool discontinuityEvidence,
            int singularShift = DefaultSingularShift,
            int associatorShift = DefaultAssociatorShift)
        {
            if ((uint)singularShift > 31u)
                throw new ArgumentOutOfRangeException(nameof(singularShift));
            if ((uint)associatorShift > 31u)
                throw new ArgumentOutOfRangeException(nameof(associatorShift));

            bool centerContact = SigmaGeometryReadout.TryRead(center, out _);
            bool rightContact = SigmaGeometryReadout.TryRead(right, out _);
            bool downContact = SigmaGeometryReadout.TryRead(down, out _);
            bool bothNull = !centerContact && !rightContact;
            bool contactNull = centerContact != rightContact;
            if (bothNull)
            {
                return new SigmaIntrinsicTopologySignature(
                    SigmaTopologyClass.Unsupported, default, -1,
                    BigInteger.Zero, BigInteger.One, BigInteger.Zero,
                    BigInteger.One, false, false, false, false, 0u, 0u);
            }

            SigmaS16 transition = SigmaOperatorEvaluator.EvaluateS16(
                SigmaOperatorPlans.Transition, center, right);
            BigInteger transitionScale = BigInteger.Max(BigInteger.One,
                L1(transition));
            int annihilatorId = 0;
            BigInteger annihilatorError = BigInteger.Zero;
            bool hasAnnihilator = false;
            for (int action = 0;
                action < SigmaGeneratedAlgebra.AnnihilatorActionCount; ++action)
            {
                SigmaS16 residual = SigmaS16Operators.RightSignedDyadAction(
                    transition, SigmaS16Operators.GetAnnihilatorAction(action));
                BigInteger error = L1(residual);
                if (!hasAnnihilator || error < annihilatorError)
                {
                    hasAnnihilator = true;
                    annihilatorError = error;
                    annihilatorId = action;
                }
            }
            bool near = (annihilatorError << singularShift) <= transitionScale;
            bool exact = annihilatorError.IsZero;

            BigInteger associatorError = BigInteger.Zero;
            BigInteger associatorScale = BigInteger.One;
            bool associatorStrong = false;
            if (centerContact && rightContact && downContact)
            {
                SigmaS16 deltaU = SigmaS16Operators.Subtract(right, center);
                SigmaS16 deltaV = SigmaS16Operators.Subtract(down, center);
                SigmaS16 associator = SigmaOperatorEvaluator.EvaluateS16(
                    SigmaOperatorPlans.Associator, center, deltaU, deltaV);
                associatorError = L1(associator);
                associatorScale = BigInteger.Max(BigInteger.One,
                    L1(center) + L1(deltaU) + L1(deltaV));
                associatorStrong = (associatorError << associatorShift) >=
                    associatorScale;
            }

            CanonicalizeKeys(firstIndependenceKey, secondIndependenceKey,
                out uint key0, out uint key1);
            bool independent = key0 != 0u && key1 != 0u && key0 != key1;
            SigmaTopologyClass classification;
            if (near && independent && (contactNull || associatorStrong ||
                    discontinuityEvidence))
                classification = SigmaTopologyClass.Singular;
            else if (near || contactNull || associatorStrong ||
                     discontinuityEvidence)
                classification = SigmaTopologyClass.Unresolved;
            else
                classification = SigmaTopologyClass.Regular;

            return new SigmaIntrinsicTopologySignature(classification,
                transition, annihilatorId, annihilatorError, transitionScale,
                associatorError, associatorScale, near, exact,
                associatorStrong, contactNull, key0, key1);
        }

        public static BigInteger L1(SigmaS16 value)
        {
            BigInteger sum = BigInteger.Zero;
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                sum += BigInteger.Abs(new BigInteger(value[lane]));
            return sum;
        }

        private static void CanonicalizeKeys(uint first, uint second,
            out uint key0, out uint key1)
        {
            if (first == 0u || first == second)
            {
                key0 = second;
                key1 = 0u;
                return;
            }
            if (second == 0u)
            {
                key0 = first;
                key1 = 0u;
                return;
            }
            key0 = Math.Min(first, second);
            key1 = Math.Max(first, second);
        }
    }
}
