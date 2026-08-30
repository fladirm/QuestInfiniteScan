using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>One already-projected depth relation used by production and test integration.</summary>
    public readonly struct MerkabaObservationInput
    {
        public readonly bool DepthValid;
        public readonly bool InsideFrustum;
        public readonly bool OutsideExclusions;
        public readonly bool ReplacementSurfaceValid;
        public readonly bool IsReplacementKernel;
        public readonly float KernelEyeDistance;
        public readonly float MeasuredEyeDistance;
        public readonly float KernelViewDepthLinear;
        public readonly float MeasuredDepthLinear;
        public readonly float DilatedDepthLinear;
        public readonly float NormalFacing;
        public readonly float MaxUpdateDistance;

        public MerkabaObservationInput(bool depthValid, bool insideFrustum,
            bool outsideExclusions, bool replacementSurfaceValid,
            bool isReplacementKernel, float kernelEyeDistance,
            float measuredEyeDistance,
            float kernelViewDepthLinear, float measuredDepthLinear,
            float dilatedDepthLinear, float normalFacing, float maxUpdateDistance)
        {
            DepthValid = depthValid;
            InsideFrustum = insideFrustum;
            OutsideExclusions = outsideExclusions;
            ReplacementSurfaceValid = replacementSurfaceValid;
            IsReplacementKernel = isReplacementKernel;
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
        public readonly float EvidenceWeight;
        public readonly float Clearance;
        public readonly bool AcceptableDepthDisparity;
        public readonly bool UnoccludedByDilation;
        public readonly bool ValidSurfaceNormal;

        public MerkabaObservationResult(MerkabaObservationKind kind, float quality,
            float evidenceWeight, float clearance,
            bool acceptableDepthDisparity, bool unoccludedByDilation,
            bool validSurfaceNormal)
        {
            Kind = kind;
            Quality = quality;
            EvidenceWeight = evidenceWeight;
            Clearance = clearance;
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
            if (!input.DepthValid || !input.InsideFrustum ||
                !input.OutsideExclusions || !input.ReplacementSurfaceValid ||
                !IsFinitePositive(input.KernelEyeDistance) ||
                !IsFinitePositive(input.MeasuredEyeDistance) ||
                !IsFinitePositive(input.MeasuredDepthLinear) ||
                !IsFinitePositive(input.DilatedDepthLinear) ||
                input.MaxUpdateDistance <= 0f ||
                input.MeasuredEyeDistance > input.MaxUpdateDistance)
                return Unknown();

            float clearance = input.MeasuredEyeDistance -
                input.KernelEyeDistance;
            MerkabaObservationKind kind;
            if (input.IsReplacementKernel)
                kind = MerkabaObservationKind.Surface;
            else if (clearance > MerkabaConstants.HalfSupport)
                kind = MerkabaObservationKind.Free;
            else
                return Unknown(); // behind or inside the support uncertainty band

            bool disparity = input.MeasuredDepthLinear <
                input.DilatedDepthLinear + DepthDisparityThreshold;
            bool unoccluded = input.KernelViewDepthLinear < input.DilatedDepthLinear || disparity;
            bool normalValid = input.NormalFacing > MinimumNormalDot;
            if (!unoccluded || !normalValid)
                return new MerkabaObservationResult(MerkabaObservationKind.Unknown,
                    0f, 0f, clearance,
                    disparity, unoccluded, normalValid);

            float distanceFactor = Mathf.Clamp01(1f -
                input.MeasuredEyeDistance / input.MaxUpdateDistance);
            float angleFactor = Mathf.Clamp01(input.NormalFacing);
            float quality = distanceFactor * angleFactor;
            float evidenceWeight = quality * quality;
            if (kind == MerkabaObservationKind.Free)
                evidenceWeight *= FreeDistanceWeight(clearance);
            return new MerkabaObservationResult(kind, quality, evidenceWeight,
                clearance, disparity, unoccluded, normalValid);
        }

        public static float FreeDistanceWeight(float clearance) =>
            Mathf.Clamp01((clearance - MerkabaConstants.HalfSupport) /
                (MerkabaConstants.FreeFullClearance -
                 MerkabaConstants.HalfSupport));

        private static bool IsFinitePositive(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        private static MerkabaObservationResult Unknown() => new(
            MerkabaObservationKind.Unknown, 0f, 0f, 0f, false, false, false);
    }
}
