#ifndef GENESIS_TSDF_FUSION_POLICY_INCLUDED
#define GENESIS_TSDF_FUSION_POLICY_INCLUDED

// Must stay numerically aligned with Runtime/Core/TsdfFusionPolicy.cs. These are
// scalar uniforms rather than a new voxel resource; B06 therefore leaves the
// persistent RG8_SNORM TSDF + RGBA8 color layout unchanged.
int   gsFusionProtectionEnabled;
float gsFusionStableConfidence;
float gsFusionExistingQualityFloor;
float gsFusionQualityHysteresis;
float gsFusionOppositeOrientationDot;
float gsFusionSurfaceBand;
float gsFusionResidualTolerance;
float gsFusionResidualConfidenceSlack;
float gsFusionImprovementResidualScale;
float gsFusionStableBlendFloor;
float gsFusionNormalMinWeight;
float gsFusionBackBandVoxels;

#define GS_FUSION_INVALID_INPUT              0
#define GS_FUSION_OCCLUDED                   1
#define GS_FUSION_FROZEN                     2
#define GS_FUSION_INSUFFICIENT_SEED_QUALITY  3
#define GS_FUSION_OPPOSITE_SURFACE           4
#define GS_FUSION_LOWER_QUALITY              5
#define GS_FUSION_INCONSISTENT_OUTLIER       6
#define GS_FUSION_INSUFFICIENT_INFLUENCE     7
#define GS_FUSION_SEEDED                     8
#define GS_FUSION_INTEGRATED                 9
#define GS_FUSION_INTEGRATED_CLAMPED        10

#define GS_FUSION_MIN_SEED_QUALITY 0.25
#define GS_FUSION_MIN_PROVISIONAL_SEED_QUALITY 0.18
#define GS_FUSION_MIN_PROVISIONAL_SEED_WEIGHT 0.02
#define GS_FUSION_SEED_WEIGHT 0.10
#define GS_FUSION_MIN_INFLUENCE 0.005
#define GS_FUSION_MIN_ORIENTATION_RESIDUAL 0.04

struct GsTsdfFusionResult
{
    float accepted;
    float tsdf;
    float weight;
    float quality;
    float blend;
    float existingQuality;
    int decision;
};

float gsFusionObservationQuality(float surfaceDistanceMeters,
    float maximumUpdateDistanceMeters, float incidence)
{
    float distanceFactor = saturate(1.0 - surfaceDistanceMeters /
        max(maximumUpdateDistanceMeters, 0.0001));
    return distanceFactor * saturate(incidence);
}

GsTsdfFusionResult gsFuseTsdf(float oldTsdf, float oldWeight,
    float existingBestQuality, float incomingTsdf,
    float surfaceDistanceMeters, float maximumUpdateDistanceMeters,
    float incidence, bool visible, bool hasExistingOrientation,
    float orientationDot, float maxWeight, float blendRate,
    float stability, float weightGrowth)
{
    // Initialize the single return object up front. Unity's Vulkan cross-compiler
    // otherwise emits a false-positive uninitialized hidden-return warning for
    // struct values returned by helper functions on early branches.
    GsTsdfFusionResult result = (GsTsdfFusionResult)0;
    result.accepted = 0.0;
    result.tsdf = oldTsdf;
    result.weight = oldWeight;
    result.decision = GS_FUSION_INVALID_INPUT;

    if (!visible)
    {
        result.decision = GS_FUSION_OCCLUDED;
    }
    else if (oldWeight < 0.0)
    {
        result.decision = GS_FUSION_FROZEN;
    }
    else
    {
        incomingTsdf = clamp(incomingTsdf, -1.0, 1.0);
        float quality = gsFusionObservationQuality(surfaceDistanceMeters,
            maximumUpdateDistanceMeters, incidence);
        float weight = max(oldWeight, 0.0);
        result.weight = weight;
        result.quality = quality;

        if (weight < 0.001)
        {
            if (quality < GS_FUSION_MIN_PROVISIONAL_SEED_QUALITY)
            {
                result.decision = GS_FUSION_INSUFFICIENT_SEED_QUALITY;
            }
            else
            {
                float seedProgress = saturate(
                    (quality - GS_FUSION_MIN_PROVISIONAL_SEED_QUALITY) /
                    (GS_FUSION_MIN_SEED_QUALITY -
                     GS_FUSION_MIN_PROVISIONAL_SEED_QUALITY));
                result.accepted = 1.0;
                result.tsdf = incomingTsdf;
                result.weight = lerp(GS_FUSION_MIN_PROVISIONAL_SEED_WEIGHT,
                    GS_FUSION_SEED_WEIGHT, seedProgress);
                result.blend = 1.0;
                result.decision = GS_FUSION_SEEDED;
            }
        }
        else
        {
            float confidence = saturate(weight / max(maxWeight, 0.0001));
            float existingQuality = max(saturate(existingBestQuality),
                confidence * gsFusionExistingQualityFloor);
            float residual = incomingTsdf - oldTsdf;
            float residualMagnitude = abs(residual);
            bool stableSurface = gsFusionProtectionEnabled != 0 &&
                abs(oldTsdf) < gsFusionSurfaceBand &&
                confidence >= gsFusionStableConfidence;
            result.existingQuality = existingQuality;

            bool oppositeSurface = stableSurface && hasExistingOrientation &&
                orientationDot < gsFusionOppositeOrientationDot &&
                residualMagnitude > GS_FUSION_MIN_ORIENTATION_RESIDUAL;
            bool lowerQuality = stableSurface &&
                quality + gsFusionQualityHysteresis < existingQuality &&
                residualMagnitude > gsFusionResidualTolerance;

            if (oppositeSurface)
            {
                result.decision = GS_FUSION_OPPOSITE_SURFACE;
            }
            else if (lowerQuality)
            {
                result.decision = GS_FUSION_LOWER_QUALITY;
            }
            else
            {
                float effectiveTsdf = incomingTsdf;
                bool clamped = false;
                bool inconsistentOutlier = false;
                float improvement = max(0.0,
                    quality - existingQuality - gsFusionQualityHysteresis);
                if (stableSurface)
                {
                    float allowedResidual = gsFusionResidualTolerance +
                        (1.0 - confidence) * gsFusionResidualConfidenceSlack +
                        improvement * gsFusionImprovementResidualScale;
                    if (residualMagnitude > allowedResidual)
                    {
                        if (improvement <= 0.0)
                        {
                            inconsistentOutlier = true;
                        }
                        else
                        {
                            effectiveTsdf = clamp(oldTsdf +
                                sign(residual) * allowedResidual, -1.0, 1.0);
                            clamped = true;
                        }
                    }
                }

                if (inconsistentOutlier)
                {
                    result.decision = GS_FUSION_INCONSISTENT_OUTLIER;
                }
                else
                {
                    float q2 = quality * quality;
                    float blend = q2 * blendRate /
                        (1.0 + weight * stability);
                    if (stableSurface)
                    {
                        float blendScale = improvement > 0.0
                            ? 0.5 + 0.5 * saturate(improvement)
                            : max(gsFusionStableBlendFloor, 1.0 - confidence);
                        blend *= blendScale;
                    }
                    blend = clamp(blend, 0.0, 0.7);

                    if (blend < GS_FUSION_MIN_INFLUENCE)
                    {
                        result.decision = GS_FUSION_INSUFFICIENT_INFLUENCE;
                    }
                    else
                    {
                        result.accepted = 1.0;
                        result.tsdf = lerp(oldTsdf, effectiveTsdf, blend);
                        result.weight = min(weight + q2 * weightGrowth,
                            maxWeight);
                        result.blend = blend;
                        result.decision = clamped
                            ? GS_FUSION_INTEGRATED_CLAMPED
                            : GS_FUSION_INTEGRATED;
                    }
                }
            }
        }
    }
    return result;
}

#endif
