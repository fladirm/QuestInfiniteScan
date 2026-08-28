using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>One already-projected depth relation used by production and test integration.</summary>
    public readonly struct MerkabaObservationInput
    {
        public readonly bool DepthValid;
        public readonly bool InsideFrustum;
        public readonly bool OutsideExclusions;
        public readonly float KernelEyeDistance;
        public readonly float MeasuredEyeDistance;
        public readonly float KernelViewDepthLinear;
        public readonly float MeasuredDepthLinear;
        public readonly float DilatedDepthLinear;
        public readonly float NormalFacing;
        public readonly float MaxUpdateDistance;

        public MerkabaObservationInput(bool depthValid, bool insideFrustum,
            bool outsideExclusions, float kernelEyeDistance, float measuredEyeDistance,
            float kernelViewDepthLinear, float measuredDepthLinear,
            float dilatedDepthLinear, float normalFacing, float maxUpdateDistance)
        {
            DepthValid = depthValid;
            InsideFrustum = insideFrustum;
            OutsideExclusions = outsideExclusions;
            KernelEyeDistance = kernelEyeDistance;
            MeasuredEyeDistance = measuredEyeDistance;
            KernelViewDepthLinear = kernelViewDepthLinear;
            MeasuredDepthLinear = measuredDepthLinear;
            DilatedDepthLinear = dilatedDepthLinear;
            NormalFacing = normalFacing;
            MaxUpdateDistance = maxUpdateDistance;
        }
    }

    public readonly struct MerkabaObservationResult
    {
        public readonly MerkabaObservationKind Kind;
        public readonly float Quality;
        public readonly bool AcceptableDepthDisparity;
        public readonly bool UnoccludedByDilation;
        public readonly bool ValidSurfaceNormal;

        public MerkabaObservationResult(MerkabaObservationKind kind, float quality,
            bool acceptableDepthDisparity, bool unoccludedByDilation,
            bool validSurfaceNormal)
        {
            Kind = kind;
            Quality = quality;
            AcceptableDepthDisparity = acceptableDepthDisparity;
            UnoccludedByDilation = unoccludedByDilation;
            ValidSurfaceNormal = validSurfaceNormal;
        }
    }

    /// <summary>
    /// QRS-derived surface/free/unknown classification. Persistent signed distance is
    /// deliberately absent: the signed depth relation exists only for this decision.
    /// </summary>
    public static class MerkabaObservation
    {
        public const float DepthDisparityThreshold = 0.5f;
        public const float MinimumNormalDot = 0.3f;

        public static MerkabaObservationResult Classify(in MerkabaObservationInput input)
        {
            if (!input.DepthValid || !input.InsideFrustum || !input.OutsideExclusions ||
                !IsFinitePositive(input.KernelEyeDistance) ||
                !IsFinitePositive(input.MeasuredEyeDistance) ||
                !IsFinitePositive(input.MeasuredDepthLinear) ||
                !IsFinitePositive(input.DilatedDepthLinear) ||
                input.MaxUpdateDistance <= 0f)
                return Unknown();

            float relation = input.MeasuredEyeDistance - input.KernelEyeDistance;
            MerkabaObservationKind kind;
            if (relation > MerkabaConstants.HalfSupport)
                kind = MerkabaObservationKind.Free;
            else if (Mathf.Abs(relation) <= MerkabaConstants.HalfSupport)
                kind = MerkabaObservationKind.Surface;
            else
                return Unknown(); // behind the measured surface

            bool disparity = input.MeasuredDepthLinear <
                input.DilatedDepthLinear + DepthDisparityThreshold;
            bool unoccluded = input.KernelViewDepthLinear < input.DilatedDepthLinear || disparity;
            bool normalValid = kind == MerkabaObservationKind.Free ||
                               input.NormalFacing > MinimumNormalDot;
            if (!unoccluded || !normalValid)
                return new MerkabaObservationResult(MerkabaObservationKind.Unknown, 0f,
                    disparity, unoccluded, normalValid);

            float distanceFactor = Mathf.Clamp01(1f -
                input.KernelEyeDistance / input.MaxUpdateDistance);
            float angleFactor = Mathf.Clamp01(input.NormalFacing);
            // Free-space proof is a ray relation. Retain angle quality where available,
            // while avoiding a destructively useless zero from a noisy surface normal.
            if (kind == MerkabaObservationKind.Free)
                angleFactor = Mathf.Max(angleFactor, 0.5f);
            float quality = distanceFactor * angleFactor;
            return new MerkabaObservationResult(kind, quality, disparity, unoccluded,
                normalValid);
        }

        private static bool IsFinitePositive(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        private static MerkabaObservationResult Unknown() => new(
            MerkabaObservationKind.Unknown, 0f, false, false, false);
    }
}
