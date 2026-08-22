#ifndef PRISM_SURFACE_CHART_GEOMETRY_INCLUDED
#define PRISM_SURFACE_CHART_GEOMETRY_INCLUDED

// Shared evaluation of one measured ContactFilm posterior.  The rectangle is
// only a numerical UV domain; geometry is the quadratic manifold plus the
// deepest measured displacement page available at the queried footprint.
struct AtlasDisplacementPageHeader
{
    uint id; uint generation; uint filmId; uint filmGeneration;
    uint level; uint parentPage; uint parentCell; uint flags;
    float4 uvBounds;
    uint bestFootprintBits; uint maximumVarianceBits; uint supportFixed; uint revision;
};

struct AtlasDisplacementCell
{
    float displacement; float sigma; float support; float coverage;
    float bestPrecision; float bestFootprint; float residualVariance;
    float preHitPressure; uint preHitPressureViewMask; uint revision;
};

StructuredBuffer<AtlasDisplacementPageHeader> _DisplacementPages;
StructuredBuffer<AtlasDisplacementCell> _BaseDisplacementCells;
StructuredBuffer<AtlasDisplacementCell> _MicroDisplacementCells;
StructuredBuffer<uint> _BaseChildPages;
StructuredBuffer<uint> _MicroChildPages;
#ifndef PRISM_ELASTIC_STATE_DECLARED
StructuredBuffer<ElasticChartState> _ElasticChartStates;
#endif

uint _HasDisplacement;
uint _BasePageCapacity;
uint _MicroPageCapacity;
uint _BaseCellCapacity;
uint _MaximumMicroLevels;

static const uint ATLAS_FILM_HAS_DISPLACEMENT = 1u << 5u;
static const uint ATLAS_DISPLACEMENT_PAGE_ACTIVE = 1u;
static const uint ATLAS_DISPLACEMENT_BASE_GRID = 16u;
static const uint ATLAS_DISPLACEMENT_MICRO_GRID = 8u;
static const uint ATLAS_DISPLACEMENT_BASE_CELLS = 256u;
static const uint ATLAS_DISPLACEMENT_MICRO_CELLS = 64u;
static const uint ATLAS_DISPLACEMENT_ALLOCATION_CLAIM = 0xffffffffu;

AtlasDisplacementCell AtlasLoadDisplacementCell(bool micro, uint pageIndex,
    uint localCell)
{
    if (micro)
        return _MicroDisplacementCells[
            pageIndex * ATLAS_DISPLACEMENT_MICRO_CELLS + localCell];
    return _BaseDisplacementCells[
        pageIndex * ATLAS_DISPLACEMENT_BASE_CELLS + localCell];
}

uint AtlasLoadDisplacementChild(bool micro, uint pageIndex, uint localCell)
{
    return micro
        ? _MicroChildPages[pageIndex * ATLAS_DISPLACEMENT_MICRO_CELLS + localCell]
        : _BaseChildPages[pageIndex * ATLAS_DISPLACEMENT_BASE_CELLS + localCell];
}

float AtlasBilinearDisplacement(bool micro, uint pageIndex,
    AtlasDisplacementPageHeader page, float2 uv)
{
    uint grid = micro ? ATLAS_DISPLACEMENT_MICRO_GRID :
        ATLAS_DISPLACEMENT_BASE_GRID;
    float2 span = max(page.uvBounds.zw - page.uvBounds.xy, 1e-8);
    float2 localUv = saturate((saturate(uv) - page.uvBounds.xy) / span);
    float2 samplePosition = localUv * (float)grid - 0.5;
    int2 lower = (int2)floor(samplePosition);
    float2 fraction = frac(samplePosition);
    uint2 p00 = (uint2)clamp(lower, 0, (int)grid - 1);
    uint2 p10 = uint2(min(p00.x + 1u, grid - 1u), p00.y);
    uint2 p01 = uint2(p00.x, min(p00.y + 1u, grid - 1u));
    uint2 p11 = min(p00 + 1u, grid - 1u);
    float d00 = AtlasLoadDisplacementCell(micro, pageIndex,
        p00.y * grid + p00.x).displacement;
    float d10 = AtlasLoadDisplacementCell(micro, pageIndex,
        p10.y * grid + p10.x).displacement;
    float d01 = AtlasLoadDisplacementCell(micro, pageIndex,
        p01.y * grid + p01.x).displacement;
    float d11 = AtlasLoadDisplacementCell(micro, pageIndex,
        p11.y * grid + p11.x).displacement;
    return lerp(lerp(d00, d10, fraction.x),
        lerp(d01, d11, fraction.x), fraction.y);
}

float AtlasSampleLineageHierarchy(uint rootHandle, float2 uv)
{
    uint lineage = rootHandle;
    [loop]
    for (uint lineageDepth = 0u; lineageDepth < 8u && lineage != 0u;
        ++lineageDepth)
    {
        uint rootIndex = lineage - 1u;
        if (rootIndex >= _BasePageCapacity) break;
        AtlasDisplacementPageHeader root = _DisplacementPages[rootIndex];
        if ((root.flags & ATLAS_DISPLACEMENT_PAGE_ACTIVE) == 0u) break;
        bool micro = false;
        uint pageIndex = rootIndex;
        AtlasDisplacementPageHeader page = root;
        [loop]
        for (uint level = 0u; level < _MaximumMicroLevels; ++level)
        {
            uint grid = micro ? ATLAS_DISPLACEMENT_MICRO_GRID :
                ATLAS_DISPLACEMENT_BASE_GRID;
            float2 span = max(page.uvBounds.zw - page.uvBounds.xy, 1e-8);
            float2 localUv = saturate((uv - page.uvBounds.xy) / span);
            uint2 cell = min((uint2)(localUv * (float)grid), grid - 1u);
            uint childHandle = AtlasLoadDisplacementChild(micro, pageIndex,
                cell.y * grid + cell.x);
            if (childHandle == 0u || childHandle ==
                ATLAS_DISPLACEMENT_ALLOCATION_CLAIM) break;
            uint childIndex = childHandle - 1u;
            if (childIndex >= _MicroPageCapacity) break;
            AtlasDisplacementPageHeader child =
                _DisplacementPages[_BasePageCapacity + childIndex];
            if ((child.flags & ATLAS_DISPLACEMENT_PAGE_ACTIVE) == 0u) break;
            micro = true; pageIndex = childIndex; page = child;
        }
        uint grid = micro ? ATLAS_DISPLACEMENT_MICRO_GRID :
            ATLAS_DISPLACEMENT_BASE_GRID;
        float2 span = max(page.uvBounds.zw - page.uvBounds.xy, 1e-8);
        float2 localUv = saturate((uv - page.uvBounds.xy) / span);
        uint2 cell = min((uint2)(localUv * (float)grid), grid - 1u);
        AtlasDisplacementCell selected = AtlasLoadDisplacementCell(micro,
            pageIndex, cell.y * grid + cell.x);
        if (selected.revision != 0u || root.parentPage == 0u)
            return AtlasBilinearDisplacement(micro, pageIndex, page, uv);
        lineage = root.parentPage;
    }
    return 0.0;
}

bool AtlasResolveDisplacementPage(AtlasContactFilmHeader film, float2 uv,
    out bool micro, out uint pageIndex, out AtlasDisplacementPageHeader page)
{
    micro = false;
    pageIndex = 0u;
    page = (AtlasDisplacementPageHeader)0;
    if (_HasDisplacement == 0u ||
        (film.flags & ATLAS_FILM_HAS_DISPLACEMENT) == 0u ||
        film.displacementPage == 0u ||
        film.displacementPage == ATLAS_DISPLACEMENT_ALLOCATION_CLAIM)
        return false;

    pageIndex = film.displacementPage - 1u;
    if (pageIndex >= _BasePageCapacity) return false;
    page = _DisplacementPages[pageIndex];
    if ((page.flags & ATLAS_DISPLACEMENT_PAGE_ACTIVE) == 0u ||
        page.filmId != film.id || page.filmGeneration != film.generation)
        return false;

    float2 normalized = saturate(uv);
    [loop]
    for (uint level = 0u; level < _MaximumMicroLevels; ++level)
    {
        float2 span = max(page.uvBounds.zw - page.uvBounds.xy, 1e-8);
        float2 localUv = saturate((normalized - page.uvBounds.xy) / span);
        uint grid = micro ? ATLAS_DISPLACEMENT_MICRO_GRID :
            ATLAS_DISPLACEMENT_BASE_GRID;
        uint2 cell = min((uint2)(localUv * (float)grid), grid - 1u);
        uint childHandle = AtlasLoadDisplacementChild(micro, pageIndex,
            cell.y * grid + cell.x);
        if (childHandle == 0u ||
            childHandle == ATLAS_DISPLACEMENT_ALLOCATION_CLAIM)
            break;
        uint childIndex = childHandle - 1u;
        if (childIndex >= _MicroPageCapacity) break;
        AtlasDisplacementPageHeader child =
            _DisplacementPages[_BasePageCapacity + childIndex];
        if ((child.flags & ATLAS_DISPLACEMENT_PAGE_ACTIVE) == 0u ||
            child.filmId != film.id ||
            child.filmGeneration != film.generation)
            break;
        micro = true;
        pageIndex = childIndex;
        page = child;
    }
    return true;
}

float AtlasSampleDisplacement(AtlasContactFilmHeader film, float2 uv)
{
    bool micro;
    uint pageIndex;
    AtlasDisplacementPageHeader page;
    if (!AtlasResolveDisplacementPage(film, uv, micro, pageIndex, page))
        return 0.0;

    uint rootIndex = film.displacementPage - 1u;
    AtlasDisplacementPageHeader root = _DisplacementPages[rootIndex];
    uint grid = micro ? ATLAS_DISPLACEMENT_MICRO_GRID :
        ATLAS_DISPLACEMENT_BASE_GRID;
    float2 span = max(page.uvBounds.zw - page.uvBounds.xy, 1e-8);
    float2 localUv = saturate((uv - page.uvBounds.xy) / span);
    uint2 selectedCell = min((uint2)(localUv * (float)grid), grid - 1u);
    AtlasDisplacementCell selected = AtlasLoadDisplacementCell(micro,
        pageIndex, selectedCell.y * grid + selectedCell.x);
    if (selected.revision == 0u && root.parentPage != 0u)
        return AtlasSampleLineageHierarchy(root.parentPage, uv);
    return AtlasBilinearDisplacement(micro, pageIndex, page, uv);
}

float AtlasQuadraticHeight(AtlasContactFilmHeader film, float u, float v)
{
    return film.quadratic0123.x + film.quadratic0123.y * u +
        film.quadratic0123.z * v + film.quadratic0123.w * u * u +
        film.quadratic45.x * u * v + film.quadratic45.y * v * v;
}

float AtlasElasticHeight(AtlasContactFilmHeader film, float2 uv)
{
    if (film.id == 0u) return 0.0;
    ElasticChartState state = _ElasticChartStates[film.id - 1u];
    if (state.filmId != film.id || state.filmGeneration != film.generation)
        return 0.0;
    float2 normalized = uv * 2.0 - 1.0;
    return state.normalOffset + state.normalizedTiltU * normalized.x +
        state.normalizedTiltV * normalized.y;
}

float3 AtlasSurfacePoint(AtlasContactFilmHeader film, float2 uv)
{
    float u = (uv.x * 2.0 - 1.0) * film.extentU;
    float v = (uv.y * 2.0 - 1.0) * film.extentV;
    float h = AtlasQuadraticHeight(film, u, v) +
        AtlasSampleDisplacement(film, uv) + AtlasElasticHeight(film, uv);
    return film.origin + u * film.tangentU + v * film.tangentV + h * film.normal;
}

// Derivatives include measured displacement.  This is used by topology evidence
// and the eventual mesh materializer, so both reason about the same surface.
float3 AtlasSurfaceNormal(AtlasContactFilmHeader film, float2 uv)
{
    float2 stepUv = float2(
        0.5 / max(16.0, 2.0 * film.extentU / max(film.sigmaNormal, 1e-4)),
        0.5 / max(16.0, 2.0 * film.extentV / max(film.sigmaNormal, 1e-4)));
    float2 loU = float2(max(0.0, uv.x - stepUv.x), uv.y);
    float2 hiU = float2(min(1.0, uv.x + stepUv.x), uv.y);
    float2 loV = float2(uv.x, max(0.0, uv.y - stepUv.y));
    float2 hiV = float2(uv.x, min(1.0, uv.y + stepUv.y));
    float3 du = AtlasSurfacePoint(film, hiU) - AtlasSurfacePoint(film, loU);
    float3 dv = AtlasSurfacePoint(film, hiV) - AtlasSurfacePoint(film, loV);
    float3 normal = normalize(cross(du, dv));
    return dot(normal, film.normal) >= 0.0 ? normal : -normal;
}

#endif
