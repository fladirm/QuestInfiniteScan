#ifndef GENESIS_MERKABA_OBSERVATION_SCOPE_INCLUDED
#define GENESIS_MERKABA_OBSERVATION_SCOPE_INCLUDED

#include "MerkabaSphereFlowerDataAbi.generated.hlsl"

// One frozen acquisition scope is shared by count, emit and the R1 writer.
// Allocation publication contains no scope, ownership, or surface decisions.
float _MerkabaMaxUpdateDistance;
int _MerkabaExclusionCount;
float3 _MerkabaExclusionHeads[64];
uint _M8FineRefineActive;
float3 _M8FineCursorPosition;
float3 _M8FineBrushAxis;
float _M8FineRadiusSquared;
float _M8FineLength;

#define EXCLUSION_TOP 0.25
#define EXCLUSION_BOTTOM 1.7
#define EXCLUSION_RADIUS 0.6

bool M8FineContains(float3 worldPosition)
{
    float3 relative = worldPosition - _M8FineCursorPosition;
    float axial = dot(relative, _M8FineBrushAxis);
    float3 radial = relative - _M8FineBrushAxis * axial;
    return axial >= 0.0 && axial <= _M8FineLength &&
        dot(radial, radial) <= _M8FineRadiusSquared;
}

bool IsExcluded(float3 worldPosition)
{
    [loop]
    for (int exclusion = 0; exclusion < _MerkabaExclusionCount; exclusion++)
    {
        float3 head = _MerkabaExclusionHeads[exclusion];
        float2 difference = worldPosition.xz - head.xz;
        if (dot(difference, difference) < EXCLUSION_RADIUS * EXCLUSION_RADIUS &&
            worldPosition.y < head.y + EXCLUSION_TOP &&
            worldPosition.y > head.y - EXCLUSION_BOTTOM)
            return true;
    }
    return false;
}

bool M8ObservationContains(float3 worldPosition, float3 eyePosition)
{
    if (!all(M8FlowerIsFinite(worldPosition)) || !M8FlowerIsFinite(_MerkabaMaxUpdateDistance) ||
        _MerkabaMaxUpdateDistance <= 0.0 || IsExcluded(worldPosition)) return false;
    float3 delta = worldPosition-eyePosition;
    return dot(delta,delta) <= _MerkabaMaxUpdateDistance*_MerkabaMaxUpdateDistance &&
        (_M8FineRefineActive == 0u || M8FineContains(worldPosition));
}

#endif
