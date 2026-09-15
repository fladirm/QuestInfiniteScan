// C10R / C11R tile maps (binding FS_MEAS_B_TILES, one job at a time): PCA texture confidence (luma std per 40x30 tile of cameras
// 0 = left, 1 = right, 2 = keyframe) followed by the Env Depth planar tiles of both layers (6 words each). Twin: fs::meas::TileLayout.
#ifndef FS_MEAS_TILES_GLSL
#define FS_MEAS_TILES_GLSL
uint fsTexTileWord(uint cam, uint tile) { return cam * uint(FS_TEX_TILES) + tile; }
uint fsPlanarTileWord(uint layer, uint tile, uint w) { return 3u * uint(FS_TEX_TILES) + (layer * uint(FS_PLANAR_TILES) + tile) * uint(FS_PLANAR_TILE_WORDS) + w; }
// pixel (bottom-left origin) -> texture tile of camera cam (width W, height H)
uint fsTexTileOf(vec2 px, float W, float H) {
    uint tx = uint(clamp(px.x / W * float(FS_TEX_TILES_X), 0.0, float(FS_TEX_TILES_X) - 1.0));
    uint ty = uint(clamp(px.y / H * float(FS_TEX_TILES_Y), 0.0, float(FS_TEX_TILES_Y) - 1.0));
    return ty * uint(FS_TEX_TILES_X) + tx;
}
#endif
