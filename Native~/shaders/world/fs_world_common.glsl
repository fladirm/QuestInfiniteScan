// FinalScan world GLSL common: byte-identical twins of Native~/src/host/world/fs_world_types.h
// (encodings, page/cell/Morton math, page hash lookup, cluster layout). Included by every world/ and
// render/ kernel. Requires GL_GOOGLE_include_directive (glslangValidator -I).
#ifndef FS_WORLD_COMMON_GLSL
#define FS_WORLD_COMMON_GLSL
#include "fs_abi.glsl"
#include "../../src/host/world/fs_world_params.h"

#define FS_PI 3.14159265358979
#define FS_RADIUS_BASE_M 0.0005
#define FS_SIGMA_BASE_M  0.0001
#define FS_PAGE_HASH_MAX_PROBE 32u

// ---- surfel handles (twin: fs::world::HandleOf / SlabOf) ----------------------------------------------
uint fsHandleSlab(uint h) { return h / uint(FS_SLAB_SURFELS); }
uint fsHandleIndex(uint h) { return h % uint(FS_SLAB_SURFELS); }
// index directory entries
bool fsEntryIsLeaf(uint e) { return (e & FS_INDEX_ENTRY_LEAF) != 0u; }
bool fsEntryIsNode(uint e) { return (e & FS_INDEX_ENTRY_NODE) != 0u && (e & FS_INDEX_ENTRY_LEAF) == 0u; }
uint fsEntryId(uint e) { return e & FS_INDEX_ENTRY_ID; }
uint fsLeafEntry(uint id) { return FS_INDEX_ENTRY_LEAF | id; }
uint fsNodeEntry(uint id) { return FS_INDEX_ENTRY_NODE | id; }

// ---- fixed point / oct / log encodings ------------------------------------------------------------------
int fsEncodePos(float metres) { return int(clamp(round(metres / FS_SURFEL_POS_UNIT_M), -32768.0, 32767.0)); }
float fsDecodePos(int q) { return float(q) * FS_SURFEL_POS_UNIT_M; }
vec3 fsSurfelLocalPos(FsSurfel s) { return vec3(fsDecodePos(fsGet_FsSurfel_px(s)), fsDecodePos(fsGet_FsSurfel_py(s)), fsDecodePos(fsGet_FsSurfel_pz(s))); }
uint fsEncodeOct32(vec3 n) {
    float l1 = abs(n.x) + abs(n.y) + abs(n.z); l1 = max(l1, 1e-12);
    float x = n.x / l1, y = n.y / l1;
    if (n.z < 0.0) { float ox = (1.0 - abs(y)) * (x >= 0.0 ? 1.0 : -1.0); float oy = (1.0 - abs(x)) * (y >= 0.0 ? 1.0 : -1.0); x = ox; y = oy; }
    uint ux = uint(clamp(round((x * 0.5 + 0.5) * 65535.0), 0.0, 65535.0));
    uint uy = uint(clamp(round((y * 0.5 + 0.5) * 65535.0), 0.0, 65535.0));
    return ux | (uy << 16);
}
vec3 fsDecodeOct32(uint packed) {
    float x = float(packed & 0xFFFFu) / 65535.0 * 2.0 - 1.0;
    float y = float(packed >> 16) / 65535.0 * 2.0 - 1.0;
    float z = 1.0 - abs(x) - abs(y);
    if (z < 0.0) { float ox = (1.0 - abs(y)) * (x >= 0.0 ? 1.0 : -1.0); float oy = (1.0 - abs(x)) * (y >= 0.0 ? 1.0 : -1.0); x = ox; y = oy; }
    vec3 n = vec3(x, y, z); float len = length(n); return len < 1e-12 ? vec3(0.0, 0.0, 1.0) : n / len;
}
uint fsPackOct32(uint oct, uint octHi) { uint ux = ((oct & 0xFFu) << 8) | (octHi & 0xFFu); uint uy = (oct & 0xFF00u) | (octHi >> 8); return ux | (uy << 16); }
void fsSplitOct32(uint p, out uint oct, out uint octHi) { uint ux = p & 0xFFFFu, uy = p >> 16; oct = (ux >> 8) | ((uy >> 8) << 8); octHi = (ux & 0xFFu) | ((uy & 0xFFu) << 8); }
vec3 fsSurfelNormal(FsSurfel s) { return fsDecodeOct32(fsPackOct32(fsGet_FsSurfel_normalOct(s), fsGet_FsSurfel_normalOctHi(s))); }
uint fsEncodeLog(float metres, float base) { if (metres <= base) return 0u; return uint(clamp(round(log2(metres / base) * 4096.0), 0.0, 65535.0)); }
float fsDecodeLog(uint v, float base) { return base * exp2(float(v) / 4096.0); }
uint fsEncodeLogRadius(float m) { return fsEncodeLog(m, FS_RADIUS_BASE_M); }
float fsDecodeLogRadius(uint v) { return fsDecodeLog(v, FS_RADIUS_BASE_M); }
uint fsEncodeLogSigma(float m) { return fsEncodeLog(m, FS_SIGMA_BASE_M); }
float fsDecodeLogSigma(uint v) { return fsDecodeLog(v, FS_SIGMA_BASE_M); }
uint fsRadius8FromLog16(uint v) { return min((v + 128u) >> 8, 255u); }
uint fsPreviewColorFromNormal(vec3 n) {
    uvec3 c = uvec3(clamp(n * 0.5 + 0.5, 0.0, 1.0) * 255.0);
    return c.x | (c.y << 8) | (c.z << 16);
}
#define FS_EVIDENCE_COUNT_MASK 0x03FFu
#define FS_FLAG_SIDED_A   0x0400u
#define FS_FLAG_SIDED_B   0x0800u
#define FS_FLAG_TRANSIENT 0x1000u
#define FS_FLAG_PROMOTED  0x2000u
#define FS_FLAG_REMOVED   0x4000u
#define FS_FLAG_DETAIL    0x8000u

// ---- page / cell / Morton ---------------------------------------------------------------------------
int fsPageCoord(float metres) { return int(floor(metres / FS_PAGE_EXTENT_M)); }
float fsPageOrigin(int coord) { return (float(coord) + 0.5) * FS_PAGE_EXTENT_M; }
vec3 fsPageOrigin3(FsPageKey k) { return vec3(fsPageOrigin(k.x), fsPageOrigin(k.y), fsPageOrigin(k.z)); }
uint fsHashPageKey(FsPageKey k) {
    uint h = uint(k.anchorId) * 0x9E3779B1u;
    h ^= uint(k.x) * 0x85EBCA77u; h = (h << 13) | (h >> 19);
    h ^= uint(k.y) * 0xC2B2AE3Du; h = (h << 13) | (h >> 19);
    h ^= uint(k.z) * 0x27D4EB2Fu;
    h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
    return h;
}
bool fsKeyEq(FsPageKey a, FsPageKey b) { return a.anchorId == b.anchorId && a.x == b.x && a.y == b.y && a.z == b.z; }
int fsClampCell(int c) { return c < 0 ? 0 : (c >= FS_CELLS_PER_AXIS ? FS_CELLS_PER_AXIS - 1 : c); }
uint fsCellOf(vec3 l) {
    float halfE = FS_PAGE_EXTENT_M * 0.5;
    int cx = fsClampCell(int(floor((l.x + halfE) / FS_CELL_EXTENT_M)));
    int cy = fsClampCell(int(floor((l.y + halfE) / FS_CELL_EXTENT_M)));
    int cz = fsClampCell(int(floor((l.z + halfE) / FS_CELL_EXTENT_M)));
    return uint(cx) + uint(cy) * uint(FS_CELLS_PER_AXIS) + uint(cz) * uint(FS_CELLS_PER_AXIS * FS_CELLS_PER_AXIS);
}
uint fsOctant(vec3 l) {
    float halfE = FS_PAGE_EXTENT_M * 0.5;
    vec3 f = (l + vec3(halfE)) / FS_CELL_EXTENT_M; vec3 fr = f - floor(f);
    return (fr.x >= 0.5 ? 1u : 0u) | (fr.y >= 0.5 ? 2u : 0u) | (fr.z >= 0.5 ? 4u : 0u);
}
uint fsPart1By2(uint v) { v &= 0x1Fu; v = (v | (v << 8)) & 0x100F00Fu; v = (v | (v << 4)) & 0x10C30C3u; v = (v | (v << 2)) & 0x1249249u; return v; }
uint fsMortonOfCell(uint cell) { return fsPart1By2(cell & 31u) | (fsPart1By2((cell >> 5) & 31u) << 1) | (fsPart1By2((cell >> 10) & 31u) << 2); }
uint fsCellOfMorton(uint m) {
    uint cx = 0u, cy = 0u, cz = 0u;
    for (uint b = 0u; b < 5u; ++b) { cx |= ((m >> (3u * b)) & 1u) << b; cy |= ((m >> (3u * b + 1u)) & 1u) << b; cz |= ((m >> (3u * b + 2u)) & 1u) << b; }
    return cx | (cy << 5) | (cz << 10);
}
// Cell AABB (page-local metres) of a cell index.
void fsCellBounds(uint cell, out vec3 bmin, out vec3 bmax) {
    float halfE = FS_PAGE_EXTENT_M * 0.5;
    vec3 c = vec3(float(cell & 31u), float((cell >> 5) & 31u), float((cell >> 10) & 31u));
    bmin = c * FS_CELL_EXTENT_M - vec3(halfE); bmax = bmin + vec3(FS_CELL_EXTENT_M);
}

// ---- canonical tangent frame + footprint ellipse (twins: TangentFrame / FootprintOfAabb / PackFootprint) --
void fsTangentFrame(vec3 n, out vec3 t1, out vec3 t2) {
    vec3 up = abs(n.y) > 0.99 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
    t1 = cross(n, up); float l = length(t1); t1 = l < 1e-9 ? vec3(1.0, 0.0, 0.0) : t1 / l;
    t2 = cross(n, t1);
}
void fsFootprintOfAabb(vec3 bmin, vec3 bmax, vec3 t1, vec3 t2, out float a, out float b, out float cx, out float cy) {
    float x0 = 1e30, x1 = -1e30, y0 = 1e30, y1 = -1e30;
    for (int i = 0; i < 8; ++i) {
        vec3 c = vec3((i & 1) != 0 ? bmax.x : bmin.x, (i & 2) != 0 ? bmax.y : bmin.y, (i & 4) != 0 ? bmax.z : bmin.z);
        float x = dot(c, t1), y = dot(c, t2);
        x0 = min(x0, x); x1 = max(x1, x); y0 = min(y0, y); y1 = max(y1, y);
    }
    a = 0.5 * (x1 - x0); b = 0.5 * (y1 - y0); cx = 0.5 * (x0 + x1); cy = 0.5 * (y0 + y1);
}
uint fsPackFootprint(float a, float b) { return fsEncodeLogRadius(a) | (fsEncodeLogRadius(b) << 16); }
float fsFootprintA(uint packed) { return fsDecodeLogRadius(packed & 0xFFFFu); }
float fsFootprintB(uint packed) { return fsDecodeLogRadius(packed >> 16); }
bool fsCoverageCellInside(uint cx, uint cy) {
    float u = (float(cx) + 0.5) / float(FS_COVERAGE_GRID) * 2.0 - 1.0, v = (float(cy) + 0.5) / float(FS_COVERAGE_GRID) * 2.0 - 1.0;
    return u * u + v * v <= 1.0;
}

// Micro-cell refinement (index depth d): octant of `l` inside the box [bmin, bmax) and the child box.
uint fsOctantOf(vec3 l, vec3 bmin, vec3 bmax, out vec3 cmin, out vec3 cmax) {
    vec3 mid = 0.5 * (bmin + bmax);
    uint o = (l.x >= mid.x ? 1u : 0u) | (l.y >= mid.y ? 2u : 0u) | (l.z >= mid.z ? 4u : 0u);
    cmin = vec3((o & 1u) != 0u ? mid.x : bmin.x, (o & 2u) != 0u ? mid.y : bmin.y, (o & 4u) != 0u ? mid.z : bmin.z);
    cmax = vec3((o & 1u) != 0u ? bmax.x : mid.x, (o & 2u) != 0u ? bmax.y : mid.y, (o & 4u) != 0u ? bmax.z : mid.z);
    return o;
}
#endif // FS_WORLD_COMMON_GLSL
