#ifndef CONE_PRISM_MATH_INCLUDED
#define CONE_PRISM_MATH_INCLUDED

float ConeProjectionDepthToViewZ(float rawDepth, float2 nearFar)
{
    if (!isfinite(nearFar.y) || nearFar.y > 1e20)
        return nearFar.x / max(1.0 - rawDepth, 1e-8);
    float denominator = nearFar.y - rawDepth * (nearFar.y - nearFar.x);
    return denominator > 1e-8
        ? (nearFar.x * nearFar.y) / denominator
        : 0.0;
}

// Metric tangent-plane footprint vectors per one source-image pixel.
bool ConeSurfaceFootprint(float3 ray, float3 rayDx, float3 rayDy,
    float rangeMeters, float3 surfaceNormal, out float3 axisX, out float3 axisY,
    out float areaSquareMeters, out float incidence)
{
    ray = normalize(ray);
    surfaceNormal = normalize(surfaceNormal);
    float signedIncidence = dot(surfaceNormal, ray);
    incidence = abs(signedIncidence);
    if (incidence <= 1e-4 || rangeMeters <= 0.0)
    {
        axisX = 0.0;
        axisY = 0.0;
        areaSquareMeters = 0.0;
        return false;
    }

    axisX = rangeMeters * (rayDx - ray *
        (dot(surfaceNormal, rayDx) / signedIncidence));
    axisY = rangeMeters * (rayDy - ray *
        (dot(surfaceNormal, rayDy) / signedIncidence));
    areaSquareMeters = length(cross(axisX, axisY));
    return isfinite(areaSquareMeters) && areaSquareMeters > 0.0;
}

#endif
