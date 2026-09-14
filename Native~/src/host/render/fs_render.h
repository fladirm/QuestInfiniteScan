// Render module internal interface (native only): the per-frame block written by the CPU (twin of the
// GLSL FsCullFrame in fs_render_common.glsl), the render kernel table and the frame-begin hook. The public
// C ABI (FsRender_*) is implemented in fs_render.cpp. Contract §13.1, §13.4, §13.5.
#pragma once
#include <stdint.h>
#include "../world/fs_world_params.h"

namespace fs {
namespace render {

struct CullFrame {                       // std430 twin: every member is 4-byte scalar data, 16-byte aligned blocks
    float    planesL[6][4];
    float    planesR[6][4];
    float    centerViewProj[16];
    float    centerView[16];
    float    srcInvViewProj[2][16];
    float    envInvViewProj[2][16];
    float    headPos_focal[4];
    float    fwd_tanMargin[4];
    float    lod[4];                     // fovealPx, peripheralPx, headroom bias, screenWorkBudget
    uint32_t misc[4];                    // renderMode, frameIndex, reuseFlag, srcFlags (bit0 prev, bit1 env, bit2 flipY)
    float    srcDepth[4];                // prev width, height, layers, 0
    float    envDepth[4];                // env width, height, near, far
    float    anchors[FS_MAX_ANCHORS][16];
};
static_assert(sizeof(CullFrame) == 96 + 96 + 64 + 64 + 128 + 128 + 96 + FS_MAX_ANCHORS * 64, "CullFrame layout");
static_assert(sizeof(CullFrame) % 16 == 0, "CullFrame std430 array stride");

void EnsureInit();                       // idempotent; every FsRender_* export calls it

} // namespace render
} // namespace fs
