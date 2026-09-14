// On-disk pipeline delivery (contract §15.7): VkPipelineCache blob and VK_KHR_pipeline_binary packs under
// <storageRoot>/finalscan/. Pure file I/O + header validation (no Vulkan calls) so the host tests cover it.
// Files are keyed by pipelineCacheUUID + driverVersion (+ vendor/device id); a mismatching or malformed
// file is ignored, never an error (§15.7 "never fail on missing/invalid binaries").
#pragma once
#include <stdint.h>
#include <string>
#include <vector>

namespace fs {
namespace store {

constexpr uint32_t kUuidSize = 16;
constexpr uint32_t kMaxBinaryKeySize = 32;

struct CacheIdentity {
    uint32_t driverVersion = 0;
    uint32_t vendorID = 0;
    uint32_t deviceID = 0;
    uint8_t  pipelineCacheUUID[kUuidSize] = {};
};

struct BinaryBlob {
    uint32_t keySize = 0;
    uint8_t  key[kMaxBinaryKeySize] = {};
    std::vector<uint8_t> data;
};

// Paths
std::string CacheFilePath(const std::string& storageDir);                                             // <dir>/pipeline-cache.bin
std::string BinaryFilePath(const std::string& storageDir, const std::string& globalKeyHex, const std::string& pipelineKeyHex);
std::string HexString(const uint8_t* bytes, size_t n);
bool MakeDirs(const std::string& path);                                                               // mkdir -p
bool ReadFile(const std::string& path, std::vector<uint8_t>& out);
bool WriteFileAtomic(const std::string& path, const void* data, size_t size);                         // tmp + rename

// VkPipelineCache blob ("FSPC" header + raw vkGetPipelineCacheData bytes). Load returns false when the
// file is missing, malformed, or belongs to another driver/device.
std::vector<uint8_t> EncodeCacheFile(const CacheIdentity& id, const void* data, size_t size);
bool DecodeCacheFile(const std::vector<uint8_t>& file, const CacheIdentity& id, std::vector<uint8_t>& outData);
// Pipeline binary pack ("FSPB" header + N {keySize,key,dataSize,data}).
std::vector<uint8_t> EncodeBinaryPack(const CacheIdentity& id, const std::vector<BinaryBlob>& blobs);
bool DecodeBinaryPack(const std::vector<uint8_t>& file, const CacheIdentity& id, std::vector<BinaryBlob>& outBlobs);

} // namespace store
} // namespace fs
