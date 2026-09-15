// C09R-E4.1 surface sheet / manifold closure rules (twin: fs::world::SheetEdge / SheetUpdate in fs_fusion_ref.h).
// Canonical authority stays the adaptive metric surfel (contract §8); the SurfaceGraph is disposable: per epoch, per
// dirty cell, over PROMOTED surfels of the 27-cell neighbourhood. An edge joins two surfels of the same physical sheet
// (compatible normal, signed plane distance inside the bounded gate = no depth discontinuity, supports no farther apart
// than FS_SHEET_GAP_MAX_M). Over the 1-ring: robust local plane fit (flat -> centre pulled along its normal onto the fit,
// normal blended; curved -> untouched), coverage closure (support grows toward the neighbours' half distance, never
// beyond FS_SHEET_RADIUS_MAX_M, never where no edge exists) and redundant collapse (a surfel sitting inside a stronger
// same-sheet neighbour is removed).
#ifndef FS_SHEET_GLSL
#define FS_SHEET_GLSL
// Symmetric edge test of a <-> b; tangential distance in `td`, signed plane distance in `d` (frame of the mean normal).
bool fsSheetEdge(FsPatch a, FsPatch b, out float td, out float d) {
    td = 0.0; d = 0.0;
    if (dot(a.n, b.n) < FS_SHEET_MIN_DOT) return false;
    vec3 nm = normalize(a.n + b.n);                                          // symmetric: the mean normal (a surfel's own tilt does not break the edge)
    vec3 delta = b.p - a.p; d = dot(nm, delta);
    float planeTol = min(FS_ASSOC_SIGMA_GATE * sqrt(a.sigmaN * a.sigmaN + b.sigmaN * b.sigmaN), FS_SHEET_PLANE_MAX_M);
    if (abs(d) > planeTol) return false;
    td = length(delta - d * nm);
    if (td > FS_SHEET_REACH_MAX_M) return false;
    return td <= a.rM + b.rM + FS_SHEET_GAP_MAX_M;
}
#endif
