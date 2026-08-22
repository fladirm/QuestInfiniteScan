#ifndef BOUNDARY_CURVE_ATLAS_ABI_INCLUDED
#define BOUNDARY_CURVE_ATLAS_ABI_INCLUDED

struct AtlasContactBoundaryHeader
{
    uint id; uint generation; uint chunkId; uint flags;
    uint filmA; uint filmAGeneration; uint filmB; uint filmBGeneration;
    float4 controlUv01; float4 controlUv23;
    float sigma; float support; float confidence; float contradiction;
    uint revision; uint cellKey; uint viewBinMask; uint lastSeenSequence;
};

struct BoundaryCurveTopology
{
    uint boundaryId; uint boundaryGeneration;
    uint manifoldId; uint manifoldGeneration;
    uint leftHalfEdgeId; uint leftHalfEdgeGeneration;
    uint rightHalfEdgeId; uint rightHalfEdgeGeneration;
    uint leftFilmId; uint leftFilmGeneration;
    uint rightFilmId; uint rightFilmGeneration;
    uint cellKeyA; uint cellKeyB; uint independentViewMask; uint flags;
    uint revision; uint cacheGeneration;
    float positionResidual; float positionSigma;
    float firstHitScore; float visibilityScore;
    float poseCalibrationQuality; float sidednessScore;
};

struct BoundaryCurveCache
{
    uint boundaryId; uint boundaryGeneration;
    uint filmA; uint filmAGeneration;
    uint filmB; uint filmBGeneration; uint flags; uint revision;
    float4 segmentA0; float4 segmentA1; float4 segmentA2; float4 segmentA3;
    float4 segmentB0; float4 segmentB1; float4 segmentB2; float4 segmentB3;
};

static const uint BOUNDARY_ACTIVE = 1u << 0u;
static const uint BOUNDARY_UNCERTAIN = 1u << 1u;
static const uint BOUNDARY_DIRTY = 1u << 2u;
static const uint BOUNDARY_PERSISTENT = 1u << 3u;
static const uint BOUNDARY_RETIRED = 1u << 4u;
static const uint BOUNDARY_MULTIVIEW = 1u << 5u;

static const uint BOUNDARY_TOPOLOGY_ACTIVE = 1u << 0u;
static const uint BOUNDARY_TOPOLOGY_LEFT = 1u << 1u;
static const uint BOUNDARY_TOPOLOGY_RIGHT = 1u << 2u;
static const uint BOUNDARY_TOPOLOGY_SHARED = 1u << 3u;
static const uint BOUNDARY_TOPOLOGY_MULTIVIEW = 1u << 4u;
static const uint BOUNDARY_TOPOLOGY_CREASE = 1u << 5u;
static const uint BOUNDARY_TOPOLOGY_OCCLUSION = 1u << 6u;
static const uint BOUNDARY_TOPOLOGY_DIRTY_CACHE = 1u << 7u;

#endif
