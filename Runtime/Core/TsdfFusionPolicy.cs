using System;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>
    /// CPU reference for the surface-protection policy implemented by
    /// <c>TsdfFusionPolicy.hlsl</c>. Keeping the arbitration independent from camera and
    /// GPU types makes observation sequences deterministic in EditMode tests.
    ///
    /// The policy intentionally does not add a per-voxel resource. Existing TSDF weight
    /// supplies confidence, color-volume alpha supplies the best observation quality when
    /// RGB was available, and the shader derives the previous surface orientation from the
    /// local TSDF gradient.
    /// </summary>
    internal static class TsdfFusionPolicy
    {
        internal const float MinimumSeedQuality = 0.25f;
        internal const float MinimumProvisionalSeedQuality = 0.18f;
        internal const float MinimumProvisionalSeedWeight = 0.02f;
        internal const float SeedWeight = 0.10f;
        internal const float MinimumInfluence = 0.005f;
        internal const float MinimumOrientationResidual = 0.04f;

        internal static TsdfFusionResult Fuse(in TsdfFusionInput input,
            in TsdfFusionParameters parameters)
        {
            if (!parameters.IsValid || !input.IsFinite)
                return TsdfFusionResult.Rejected(input.ExistingTsdf,
                    input.ExistingWeight, TsdfFusionDecision.InvalidInput);

            if (!input.Visible)
                return TsdfFusionResult.Rejected(input.ExistingTsdf,
                    input.ExistingWeight, TsdfFusionDecision.Occluded);

            if (input.ExistingWeight < 0f)
                return TsdfFusionResult.Rejected(input.ExistingTsdf,
                    input.ExistingWeight, TsdfFusionDecision.Frozen);

            float incomingTsdf = Mathf.Clamp(input.IncomingTsdf, -1f, 1f);
            float quality = ObservationQuality(input.SurfaceDistanceMeters,
                input.MaximumUpdateDistanceMeters, input.Incidence);
            float weight = Mathf.Max(input.ExistingWeight, 0f);

            if (weight < 0.001f)
            {
                if (quality < MinimumProvisionalSeedQuality)
                    return TsdfFusionResult.Rejected(input.ExistingTsdf, weight,
                        TsdfFusionDecision.InsufficientSeedQuality, quality);

                // Oblique observations below the historic 0.25 seed cutoff start below
                // MinMeshWeight. Repeated consistent frames can grow confidence and fill
                // the surface, while one weak frame remains invisible.
                float seedProgress = Mathf.InverseLerp(MinimumProvisionalSeedQuality,
                    MinimumSeedQuality, quality);
                float seedWeight = Mathf.Lerp(MinimumProvisionalSeedWeight, SeedWeight,
                    seedProgress);
                return new TsdfFusionResult(true, incomingTsdf, seedWeight, quality,
                    1f, TsdfFusionDecision.Seeded);
            }

            float confidence = Mathf.Clamp01(weight / parameters.MaxWeight);
            float existingQuality = Mathf.Max(Mathf.Clamp01(input.ExistingBestQuality),
                confidence * parameters.ExistingQualityFloor);
            float residual = incomingTsdf - input.ExistingTsdf;
            float residualMagnitude = Mathf.Abs(residual);
            bool stableSurface = parameters.Enabled &&
                                 Mathf.Abs(input.ExistingTsdf) < parameters.SurfaceBand &&
                                 confidence >= parameters.StableConfidence;

            if (stableSurface && input.HasExistingOrientation &&
                input.OrientationDot < parameters.OppositeOrientationDot &&
                residualMagnitude > MinimumOrientationResidual)
            {
                return TsdfFusionResult.Rejected(input.ExistingTsdf, weight,
                    TsdfFusionDecision.OppositeSurface, quality, existingQuality);
            }

            // A stable surface is monotonic with respect to observation quality: a
            // meaningfully worse view may add neither bias nor confidence. This is what
            // prevents a 4 m revisit from pulling a surface established at 1-2 m.
            if (stableSurface &&
                quality + parameters.QualityHysteresis < existingQuality &&
                residualMagnitude > parameters.ResidualTolerance)
            {
                return TsdfFusionResult.Rejected(input.ExistingTsdf, weight,
                    TsdfFusionDecision.LowerQuality, quality, existingQuality);
            }

            float effectiveTsdf = incomingTsdf;
            bool clamped = false;
            float improvement = Mathf.Max(0f,
                quality - existingQuality - parameters.QualityHysteresis);
            if (stableSurface)
            {
                float allowedResidual = parameters.ResidualTolerance +
                    (1f - confidence) * parameters.ResidualConfidenceSlack +
                    improvement * parameters.ImprovementResidualScale;
                if (residualMagnitude > allowedResidual)
                {
                    if (improvement <= 0f)
                    {
                        return TsdfFusionResult.Rejected(input.ExistingTsdf, weight,
                            TsdfFusionDecision.InconsistentOutlier, quality,
                            existingQuality);
                    }

                    effectiveTsdf = input.ExistingTsdf + Mathf.Sign(residual) * allowedResidual;
                    effectiveTsdf = Mathf.Clamp(effectiveTsdf, -1f, 1f);
                    clamped = true;
                }
            }

            float q2 = quality * quality;
            float blend = q2 * parameters.BlendRate /
                          (1f + weight * parameters.Stability);
            if (stableSurface)
            {
                float blendScale = improvement > 0f
                    ? 0.5f + 0.5f * Mathf.Clamp01(improvement)
                    : Mathf.Max(parameters.StableBlendFloor, 1f - confidence);
                blend *= blendScale;
            }
            blend = Mathf.Clamp(blend, 0f, 0.7f);

            if (blend < MinimumInfluence)
            {
                return TsdfFusionResult.Rejected(input.ExistingTsdf, weight,
                    TsdfFusionDecision.InsufficientInfluence, quality, existingQuality);
            }

            float newTsdf = Mathf.Lerp(input.ExistingTsdf, effectiveTsdf, blend);
            float newWeight = Mathf.Min(weight + q2 * parameters.WeightGrowth,
                parameters.MaxWeight);
            return new TsdfFusionResult(true, newTsdf, newWeight, quality, blend,
                clamped ? TsdfFusionDecision.IntegratedClamped :
                          TsdfFusionDecision.Integrated,
                existingQuality);
        }

        internal static float ObservationQuality(float surfaceDistanceMeters,
            float maximumUpdateDistanceMeters, float incidence)
        {
            if (!IsFinite(surfaceDistanceMeters) ||
                !IsFinite(maximumUpdateDistanceMeters) ||
                !IsFinite(incidence) || maximumUpdateDistanceMeters <= 0f)
                return 0f;

            float distanceFactor = Mathf.Clamp01(
                1f - surfaceDistanceMeters / maximumUpdateDistanceMeters);
            return distanceFactor * Mathf.Clamp01(incidence);
        }

        internal static float BehindSurfaceBandMeters(float voxelMinimumMeters,
            float voxelSizeMeters, float bandVoxels)
        {
            if (!IsFinite(voxelMinimumMeters) || !IsFinite(voxelSizeMeters) ||
                !IsFinite(bandVoxels) || voxelMinimumMeters <= 0f ||
                voxelSizeMeters <= 0f || bandVoxels <= 0f)
                return 0f;

            return Mathf.Min(voxelMinimumMeters, voxelSizeMeters * bandVoxels);
        }

        internal static bool IsBehindSurfaceVisible(float raySignedDistanceMeters,
            float voxelMinimumMeters, float voxelSizeMeters, float bandVoxels)
        {
            return IsFinite(raySignedDistanceMeters) &&
                   raySignedDistanceMeters >= -BehindSurfaceBandMeters(
                       voxelMinimumMeters, voxelSizeMeters, bandVoxels);
        }

        internal static bool IsProjectedBehindSurfaceVisible(float raySignedDistanceMeters,
            float incidence, float voxelMinimumMeters, float voxelSizeMeters,
            float bandVoxels)
        {
            if (!IsFinite(raySignedDistanceMeters) || !IsFinite(incidence))
                return false;
            float projectedSignedDistance = raySignedDistanceMeters *
                                            Mathf.Clamp01(incidence);
            return projectedSignedDistance >= -BehindSurfaceBandMeters(
                voxelMinimumMeters, voxelSizeMeters, bandVoxels);
        }

        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    internal enum TsdfFusionDecision
    {
        InvalidInput,
        Occluded,
        Frozen,
        InsufficientSeedQuality,
        OppositeSurface,
        LowerQuality,
        InconsistentOutlier,
        InsufficientInfluence,
        Seeded,
        Integrated,
        IntegratedClamped
    }

    internal readonly struct TsdfFusionParameters
    {
        internal readonly bool Enabled;
        internal readonly float MaxWeight;
        internal readonly float BlendRate;
        internal readonly float Stability;
        internal readonly float WeightGrowth;
        internal readonly float StableConfidence;
        internal readonly float ExistingQualityFloor;
        internal readonly float QualityHysteresis;
        internal readonly float OppositeOrientationDot;
        internal readonly float SurfaceBand;
        internal readonly float ResidualTolerance;
        internal readonly float ResidualConfidenceSlack;
        internal readonly float ImprovementResidualScale;
        internal readonly float StableBlendFloor;

        internal TsdfFusionParameters(bool enabled, float maxWeight, float blendRate,
            float stability, float weightGrowth, float stableConfidence,
            float existingQualityFloor, float qualityHysteresis,
            float oppositeOrientationDot, float surfaceBand, float residualTolerance,
            float residualConfidenceSlack, float improvementResidualScale,
            float stableBlendFloor)
        {
            Enabled = enabled;
            MaxWeight = maxWeight;
            BlendRate = blendRate;
            Stability = stability;
            WeightGrowth = weightGrowth;
            StableConfidence = stableConfidence;
            ExistingQualityFloor = existingQualityFloor;
            QualityHysteresis = qualityHysteresis;
            OppositeOrientationDot = oppositeOrientationDot;
            SurfaceBand = surfaceBand;
            ResidualTolerance = residualTolerance;
            ResidualConfidenceSlack = residualConfidenceSlack;
            ImprovementResidualScale = improvementResidualScale;
            StableBlendFloor = stableBlendFloor;
        }

        internal static TsdfFusionParameters Default => new(
            true, 0.5f, 0.8f, 2.5f, 0.025f,
            0.35f, 0.55f, 0.04f, -0.15f, 0.85f,
            0.12f, 0.18f, 1.5f, 0.15f);

        internal bool IsValid =>
            TsdfFusionPolicy.IsFinite(MaxWeight) && MaxWeight > 0f &&
            TsdfFusionPolicy.IsFinite(BlendRate) && BlendRate >= 0f &&
            TsdfFusionPolicy.IsFinite(Stability) && Stability >= 0f &&
            TsdfFusionPolicy.IsFinite(WeightGrowth) && WeightGrowth >= 0f &&
            TsdfFusionPolicy.IsFinite(StableConfidence) &&
            StableConfidence >= 0f && StableConfidence <= 1f &&
            TsdfFusionPolicy.IsFinite(ExistingQualityFloor) &&
            ExistingQualityFloor >= 0f && ExistingQualityFloor <= 1f &&
            TsdfFusionPolicy.IsFinite(QualityHysteresis) && QualityHysteresis >= 0f &&
            TsdfFusionPolicy.IsFinite(OppositeOrientationDot) &&
            OppositeOrientationDot >= -1f && OppositeOrientationDot <= 1f &&
            TsdfFusionPolicy.IsFinite(SurfaceBand) && SurfaceBand > 0f &&
            SurfaceBand <= 1f &&
            TsdfFusionPolicy.IsFinite(ResidualTolerance) && ResidualTolerance >= 0f &&
            TsdfFusionPolicy.IsFinite(ResidualConfidenceSlack) &&
            ResidualConfidenceSlack >= 0f &&
            TsdfFusionPolicy.IsFinite(ImprovementResidualScale) &&
            ImprovementResidualScale >= 0f &&
            TsdfFusionPolicy.IsFinite(StableBlendFloor) &&
            StableBlendFloor >= 0f && StableBlendFloor <= 1f;
    }

    internal readonly struct TsdfFusionInput
    {
        internal readonly float ExistingTsdf;
        internal readonly float ExistingWeight;
        internal readonly float ExistingBestQuality;
        internal readonly float IncomingTsdf;
        internal readonly float SurfaceDistanceMeters;
        internal readonly float MaximumUpdateDistanceMeters;
        internal readonly float Incidence;
        internal readonly bool Visible;
        internal readonly bool HasExistingOrientation;
        internal readonly float OrientationDot;

        internal TsdfFusionInput(float existingTsdf, float existingWeight,
            float existingBestQuality, float incomingTsdf,
            float surfaceDistanceMeters, float maximumUpdateDistanceMeters,
            float incidence, bool visible, bool hasExistingOrientation,
            float orientationDot)
        {
            ExistingTsdf = existingTsdf;
            ExistingWeight = existingWeight;
            ExistingBestQuality = existingBestQuality;
            IncomingTsdf = incomingTsdf;
            SurfaceDistanceMeters = surfaceDistanceMeters;
            MaximumUpdateDistanceMeters = maximumUpdateDistanceMeters;
            Incidence = incidence;
            Visible = visible;
            HasExistingOrientation = hasExistingOrientation;
            OrientationDot = orientationDot;
        }

        internal bool IsFinite =>
            TsdfFusionPolicy.IsFinite(ExistingTsdf) &&
            TsdfFusionPolicy.IsFinite(ExistingWeight) &&
            TsdfFusionPolicy.IsFinite(ExistingBestQuality) &&
            TsdfFusionPolicy.IsFinite(IncomingTsdf) &&
            TsdfFusionPolicy.IsFinite(SurfaceDistanceMeters) &&
            TsdfFusionPolicy.IsFinite(MaximumUpdateDistanceMeters) &&
            TsdfFusionPolicy.IsFinite(Incidence) &&
            (!HasExistingOrientation || TsdfFusionPolicy.IsFinite(OrientationDot));
    }

    internal readonly struct TsdfFusionResult
    {
        internal readonly bool Accepted;
        internal readonly float Tsdf;
        internal readonly float Weight;
        internal readonly float ObservationQuality;
        internal readonly float Blend;
        internal readonly TsdfFusionDecision Decision;
        internal readonly float ExistingQuality;

        internal TsdfFusionResult(bool accepted, float tsdf, float weight,
            float observationQuality, float blend, TsdfFusionDecision decision,
            float existingQuality = 0f)
        {
            Accepted = accepted;
            Tsdf = tsdf;
            Weight = weight;
            ObservationQuality = observationQuality;
            Blend = blend;
            Decision = decision;
            ExistingQuality = existingQuality;
        }

        internal static TsdfFusionResult Rejected(float tsdf, float weight,
            TsdfFusionDecision decision, float observationQuality = 0f,
            float existingQuality = 0f)
        {
            return new TsdfFusionResult(false, tsdf, weight, observationQuality,
                0f, decision, existingQuality);
        }
    }
}
