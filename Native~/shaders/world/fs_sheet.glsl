// C09R-E4.1R surface complex rules (twin: fs::world::SheetEdgeR / SheetFitR in fs_fusion_ref.h). Promoted surfels are
// control sites of one surface; the SurfaceGraph is derived: per node the true metric top-K compatible neighbours of a
// ball query (FS_SHEET_LINK_R_M, across cells and pages). An edge is valid only when both ends propose each other.
#ifndef FS_SHEET_GLSL
#define FS_SHEET_GLSL
// Symmetric edge gate in world space (positions of different pages are comparable): normals within FS_SHEET_MIN_DOT,
// signed plane distance in the mean-normal frame inside the sigma gate bounded by FS_SHEET_PLANE_MAX_M, distance <= R.
bool fsSheetEdge(FsPatch a, vec3 pa, FsPatch b, vec3 pb, out float dist) {
    dist = 1e30;
    if (dot(a.n, b.n) < FS_SHEET_MIN_DOT) return false;
    vec3 nm = normalize(a.n + b.n);
    vec3 delta = pb - pa;
    float d = dot(nm, delta);
    if (abs(d) > min(FS_ASSOC_SIGMA_GATE * sqrt(a.sigmaN * a.sigmaN + b.sigmaN * b.sigmaN), FS_SHEET_PLANE_MAX_M)) return false;
    dist = length(delta);
    return dist <= FS_SHEET_LINK_R_M;
}
// World grid helpers: global cell index along one axis -> page coordinate and page-local cell coordinate.
#ifndef FS_FLOOR_DIV32_DEFINED
#define FS_FLOOR_DIV32_DEFINED
int fsFloorDiv32(int g) { return g >= 0 ? g / 32 : -((-g + 31) / 32); }
#endif
#endif
