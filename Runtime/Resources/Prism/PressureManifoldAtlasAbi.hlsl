#ifndef PRESSURE_MANIFOLD_ATLAS_ABI_INCLUDED
#define PRESSURE_MANIFOLD_ATLAS_ABI_INCLUDED

struct AtlasContactFilmHeader
{
    uint id; uint generation; uint chunkId; uint flags;
    float3 origin; float supportTotal;
    float3 normal; float sigmaNormal;
    float3 tangentU; float extentU;
    float3 tangentV; float extentV;
    float4 quadratic0123; float2 quadratic45;
    float confidence; float contradiction;
    uint revision; uint displacementPage; uint appearancePage;
    uint boundaryStart; uint boundaryCount;
    uint supportMaskLow; uint supportMaskHigh;
    uint reserved0; uint reserved1; uint reserved2;
};

struct AtlasPressureManifoldHeader
{
    uint id; uint generation; uint rootFrameId; uint flags;
    float3 opticalSeed; float seedSigma;
    uint membershipStart; uint membershipCount;
    uint halfEdgeStart; uint halfEdgeCount;
    uint frontierLoopStart; uint frontierLoopCount;
    uint revision; uint calibrationEpochLow; uint calibrationEpochHigh;
    uint topologyGeneration; uint elasticRevision; uint reserved;
};

struct AtlasFilmMembership
{
    uint filmId; uint filmGeneration; uint manifoldId; uint manifoldGeneration;
    uint firstHalfEdge; uint halfEdgeCount;
    uint firstFrontierLoop; uint frontierLoopCount;
    uint flags; uint revision;
};

struct SupportContourSegment
{
    uint id; uint generation; uint filmId; uint filmGeneration;
    uint manifoldId; uint manifoldGeneration; uint cellKey; uint flags;
    float4 uv01;
    float sigma; float support; float confidence; float bandwidth;
};

struct SupportContourPage
{
    uint id; uint generation; uint filmId; uint filmGeneration;
    uint nextPageId; uint nextPageGeneration; uint segmentCount; uint flags;
};

struct SurfaceHalfEdge
{
    uint id; uint generation; uint manifoldId; uint manifoldGeneration;
    uint filmId; uint filmGeneration;
    uint contourSegmentId; uint contourSegmentGeneration;
    uint twinId; uint twinGeneration; uint nextId; uint nextGeneration;
    uint previousId; uint previousGeneration;
    uint boundaryId; uint boundaryGeneration;
    uint evidenceId; uint evidenceGeneration;
    uint frontierLoopId; uint frontierLoopGeneration;
    uint relation; uint flags; uint revision; uint reserved;
};

struct FrontierLoop
{
    uint id; uint generation; uint manifoldId; uint manifoldGeneration;
    uint firstHalfEdgeId; uint firstHalfEdgeGeneration;
    uint halfEdgeCount; uint flags;
    float3 latentAnchor; float signedArea;
    float sigma; float support; float confidence; uint revision;
};

struct ContinuationEvidence
{
    uint id; uint generation; uint filmA; uint filmAGeneration;
    uint filmB; uint filmBGeneration; uint boundaryId; uint boundaryGeneration;
    float positionResidual; float positionSigma;
    float normalCosine; float sidednessScore;
    float firstHitScore; float visibilityScore;
    float poseCalibrationQuality; float support;
    uint independentViewMask; uint flags; uint revision; uint reserved0;
};

// Derived elastic correction of one measured leaf chart.  The correction is
// generation tagged because ContactFilm slots are reusable.  It never replaces
// the measured posterior: the posterior stiffness controls how far this local
// compatibility solve may move the chart at a proven continuation.
struct ElasticChartState
{
    uint filmId; uint filmGeneration; uint revision; uint flags;
    float normalOffset; float normalizedTiltU; float normalizedTiltV;
    float confidence;
};

struct EvidenceAlignedSplitPlan
{
    uint parentFilmIndex; uint parentFilmGeneration;
    uint childFilmIndex0; uint childFilmIndex1;
    uint childGeneration0; uint childGeneration1;
    uint childBasePage0; uint childBasePage1;
    uint parentActiveOrdinal; uint newActiveOrdinal; uint firstDirtyOrdinal;
    uint boundaryId;
    uint boundaryGeneration; uint splitKind; uint transactionState; uint reserved;
    float4 separatorUv;
    float2 childFractions; float separation; float confidence;
};

struct CrossChunkTopologyPortal
{
    uint id; uint generation; uint halfEdgeId; uint halfEdgeGeneration;
    uint remoteHalfEdgeId; uint remoteHalfEdgeGeneration;
    uint ownerChunkId; uint remoteChunkId;
    uint manifoldId; uint manifoldGeneration; uint flags; uint revision;
    // All geometric values live in ownerChunkId coordinates.  A mirrored portal
    // stores the same physical hand-off transformed into the other chunk frame.
    float4 localEndpointAndSigma;
    float4 localNormalAndBandwidth;
    float4 localTangentAndSupport;
    uint independentViewMask; uint evidenceFlags;
    float firstHitScore; float poseCalibrationQuality;
};

static const uint ATLAS_INVALID = 0xffffffffu;
static const uint HALF_EDGE_ACTIVE = 1u << 0u;
static const uint HALF_EDGE_MEASURED = 1u << 1u;
static const uint HALF_EDGE_TWIN_CONFIRMED = 1u << 2u;
static const uint HALF_EDGE_OUTER = 1u << 3u;
static const uint HALF_EDGE_DIRTY = 1u << 4u;
static const uint HALF_EDGE_PORTAL = 1u << 5u;
static const uint HALF_EDGE_RETIRED = 1u << 6u;
static const uint HALF_EDGE_SMOOTH = 1u;
static const uint HALF_EDGE_CREASE = 2u;
static const uint HALF_EDGE_OCCLUSION = 3u;
static const uint HALF_EDGE_OUTER_FRONTIER = 4u;
static const uint PORTAL_ACTIVE = 1u << 0u;
static const uint PORTAL_OWNER_RESIDENT = 1u << 1u;
static const uint PORTAL_REMOTE_RESIDENT = 1u << 2u;
static const uint PORTAL_MATCHED = 1u << 3u;
static const uint PORTAL_GHOST = 1u << 4u;
static const uint PORTAL_DIRTY = 1u << 5u;
static const uint FRONTIER_LOOP_ACTIVE = 1u << 0u;
static const uint FRONTIER_LOOP_ORDERED = 1u << 1u;
static const uint FRONTIER_LOOP_OUTER = 1u << 2u;
static const uint FRONTIER_LOOP_LATENT_TOPOLOGY_ONLY = 1u << 3u;
static const uint FRONTIER_LOOP_INNER = 1u << 5u;

#endif
