// Synthetic world generator (C04): CPU-side planar scenes sampled into surfels (grouped by logical page)
// or into FsSurfaceMeasurement streams (kind | 0x100) so the measurement → integrate → publish → cull
// path can be exercised without sensors. Pure: no Vulkan, deterministic for a given seed.
#pragma once
#include <stdint.h>
#include <vector>
#include "fs_world_types.h"

namespace fs {
namespace world {

enum SyntheticKind : int32_t { SYN_ROOM_BOX = 0, SYN_CORRIDOR = 1, SYN_STAIRCASE = 2, SYN_THIN_WALL = 3, SYN_DENSE_FOLIAGE = 4 };
constexpr int32_t kSyntheticViaRingFlag = 0x100;
constexpr float   kSyntheticRoomHeightM = 2.6f;
constexpr float   kSyntheticThinWallGapM = 0.03f;
// Dense foliage benchmark (contract §21.5b): shelves of plants, every leaf a random ellipsoid sampled at
// <= 1 cm with spatially coherent gaps (2 cm blocks + a hole per leaf) so far-LOD coverage stays well below 1.
constexpr float   kFoliageLeafSpacingM = 0.01f;
constexpr float   kFoliageGapFraction = 0.5f;
constexpr float   kFoliageGapBlockM = 0.02f;
constexpr uint64_t kFoliageTargetSurfels = 3000000;   // generator adds plants until this is reached (bounded by extent)

struct SyntheticPage { FsPageKey key; std::vector<FsSurfel> surfels; };
struct SyntheticScene {
    std::vector<SyntheticPage> pages;
    std::vector<SurfelSample> samples;     // flat (also used for the measurement stream)
    uint32_t planeCount = 0;
    uint64_t sampleCount = 0;
};

// Generates the scene. surfelSpacingM is clamped to [0.005, 1] (foliage: <= 1 cm); radius = spacing * 0.75; sigmaN 1 mm
// (+ tiny jitter from seed), sigmaT 2 mm; colour by normal; surfaceId per plane (1-based).
void GenerateSynthetic(int32_t kind, float extentM, float surfelSpacingM, uint32_t seed, int32_t anchorId, SyntheticScene& out);

// Converts samples to measurements (anchor-local), stereo source flag, given observation id.
void SamplesToMeasurements(const std::vector<SurfelSample>& samples, size_t begin, size_t end, uint32_t observationId,
                           std::vector<FsSurfaceMeasurement>& out);

} // namespace world
} // namespace fs
