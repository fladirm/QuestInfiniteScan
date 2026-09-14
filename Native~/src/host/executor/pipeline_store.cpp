// Pipeline cache / binary pack file format; see pipeline_store.h.
#include "pipeline_store.h"
#include <cstdio>
#include <cstring>
#include <sys/stat.h>
#include <unistd.h>

namespace fs {
namespace store {
namespace {

constexpr uint32_t kCacheMagic = 0x43505346u;   // "FSPC" little endian
constexpr uint32_t kPackMagic  = 0x42505346u;   // "FSPB"
constexpr uint32_t kFormatVersion = 1;

struct FileHeader {                      // 48 bytes, identical for both files
    uint32_t magic;
    uint32_t version;
    uint32_t driverVersion;
    uint32_t vendorID;
    uint32_t deviceID;
    uint32_t count;                      // cache: 1, pack: binary count
    uint8_t  uuid[kUuidSize];
    uint64_t payloadBytes;               // bytes following the header
};
static_assert(sizeof(FileHeader) == 48, "FileHeader layout");

bool HeaderMatches(const FileHeader& h, uint32_t magic, const CacheIdentity& id, size_t fileSize) {
    if (h.magic != magic || h.version != kFormatVersion) return false;
    if (h.driverVersion != id.driverVersion || h.vendorID != id.vendorID || h.deviceID != id.deviceID) return false;
    if (std::memcmp(h.uuid, id.pipelineCacheUUID, kUuidSize) != 0) return false;
    if (h.payloadBytes != fileSize - sizeof(FileHeader)) return false;
    return true;
}

FileHeader MakeHeader(uint32_t magic, const CacheIdentity& id, uint32_t count, uint64_t payload) {
    FileHeader h = {};
    h.magic = magic; h.version = kFormatVersion; h.driverVersion = id.driverVersion; h.vendorID = id.vendorID; h.deviceID = id.deviceID;
    h.count = count; std::memcpy(h.uuid, id.pipelineCacheUUID, kUuidSize); h.payloadBytes = payload;
    return h;
}

template <typename T> void Append(std::vector<uint8_t>& v, const T& x) { const uint8_t* p = reinterpret_cast<const uint8_t*>(&x); v.insert(v.end(), p, p + sizeof(T)); }
template <typename T> bool ReadAt(const std::vector<uint8_t>& v, size_t& pos, T& out) {
    if (pos + sizeof(T) > v.size()) return false;
    std::memcpy(&out, v.data() + pos, sizeof(T)); pos += sizeof(T); return true;
}

} // namespace

std::string CacheFilePath(const std::string& storageDir) { return storageDir + "/pipeline-cache.bin"; }
std::string BinaryFilePath(const std::string& storageDir, const std::string& globalKeyHex, const std::string& pipelineKeyHex) {
    return storageDir + "/binaries/" + globalKeyHex + "/" + pipelineKeyHex + ".bin";
}

std::string HexString(const uint8_t* bytes, size_t n) {
    static const char* hex = "0123456789abcdef";
    std::string s; s.reserve(n * 2);
    for (size_t i = 0; i < n; ++i) { s += hex[bytes[i] >> 4]; s += hex[bytes[i] & 15]; }
    return s;
}

bool MakeDirs(const std::string& path) {
    if (path.empty()) return false;
    std::string cur;
    for (size_t i = 0; i <= path.size(); ++i) {
        if (i == path.size() || path[i] == '/') {
            cur = path.substr(0, i);
            if (!cur.empty()) {
                struct stat st = {};
                if (stat(cur.c_str(), &st) != 0) { if (mkdir(cur.c_str(), 0700) != 0 && errno != EEXIST) return false; }
                else if (!S_ISDIR(st.st_mode)) return false;
            }
        }
    }
    return true;
}

bool ReadFile(const std::string& path, std::vector<uint8_t>& out) {
    out.clear();
    FILE* f = std::fopen(path.c_str(), "rb");
    if (!f) return false;
    uint8_t buf[65536];
    size_t n;
    while ((n = std::fread(buf, 1, sizeof(buf), f)) > 0) out.insert(out.end(), buf, buf + n);
    const bool ok = std::ferror(f) == 0;
    std::fclose(f);
    return ok;
}

bool WriteFileAtomic(const std::string& path, const void* data, size_t size) {
    const size_t slash = path.rfind('/');
    if (slash != std::string::npos && !MakeDirs(path.substr(0, slash))) return false;
    const std::string tmp = path + ".tmp";
    FILE* f = std::fopen(tmp.c_str(), "wb");
    if (!f) return false;
    const bool ok = size == 0 || std::fwrite(data, 1, size, f) == size;
    const bool closed = std::fclose(f) == 0;
    if (!ok || !closed) { std::remove(tmp.c_str()); return false; }
    if (std::rename(tmp.c_str(), path.c_str()) != 0) { std::remove(tmp.c_str()); return false; }
    return true;
}

std::vector<uint8_t> EncodeCacheFile(const CacheIdentity& id, const void* data, size_t size) {
    std::vector<uint8_t> v; v.reserve(sizeof(FileHeader) + size);
    Append(v, MakeHeader(kCacheMagic, id, 1, size));
    const uint8_t* p = static_cast<const uint8_t*>(data);
    if (size) v.insert(v.end(), p, p + size);
    return v;
}

bool DecodeCacheFile(const std::vector<uint8_t>& file, const CacheIdentity& id, std::vector<uint8_t>& outData) {
    outData.clear();
    if (file.size() < sizeof(FileHeader)) return false;
    FileHeader h; std::memcpy(&h, file.data(), sizeof(h));
    if (!HeaderMatches(h, kCacheMagic, id, file.size()) || h.count != 1 || h.payloadBytes == 0) return false;
    // Vulkan pipeline cache header (spec 1.3 "Pipeline Cache Header"): headerSize u32, headerVersion u32 (=1),
    // vendorID, deviceID, pipelineCacheUUID[16]; cross-check so a foreign blob is never handed to the driver.
    if (h.payloadBytes < 32) return false;
    const uint8_t* p = file.data() + sizeof(FileHeader);
    uint32_t headerSize, headerVersion, vendor, device;
    std::memcpy(&headerSize, p, 4); std::memcpy(&headerVersion, p + 4, 4); std::memcpy(&vendor, p + 8, 4); std::memcpy(&device, p + 12, 4);
    if (headerSize < 32 || headerVersion != 1 || vendor != id.vendorID || device != id.deviceID) return false;
    if (std::memcmp(p + 16, id.pipelineCacheUUID, kUuidSize) != 0) return false;
    outData.assign(p, p + h.payloadBytes);
    return true;
}

std::vector<uint8_t> EncodeBinaryPack(const CacheIdentity& id, const std::vector<BinaryBlob>& blobs) {
    std::vector<uint8_t> payload;
    for (const BinaryBlob& b : blobs) {
        Append(payload, b.keySize);
        payload.insert(payload.end(), b.key, b.key + kMaxBinaryKeySize);
        Append(payload, static_cast<uint64_t>(b.data.size()));
        payload.insert(payload.end(), b.data.begin(), b.data.end());
    }
    std::vector<uint8_t> v; v.reserve(sizeof(FileHeader) + payload.size());
    Append(v, MakeHeader(kPackMagic, id, static_cast<uint32_t>(blobs.size()), payload.size()));
    v.insert(v.end(), payload.begin(), payload.end());
    return v;
}

bool DecodeBinaryPack(const std::vector<uint8_t>& file, const CacheIdentity& id, std::vector<BinaryBlob>& outBlobs) {
    outBlobs.clear();
    if (file.size() < sizeof(FileHeader)) return false;
    FileHeader h; std::memcpy(&h, file.data(), sizeof(h));
    if (!HeaderMatches(h, kPackMagic, id, file.size()) || h.count == 0 || h.count > 64) return false;
    size_t pos = sizeof(FileHeader);
    for (uint32_t i = 0; i < h.count; ++i) {
        BinaryBlob b;
        if (!ReadAt(file, pos, b.keySize) || b.keySize == 0 || b.keySize > kMaxBinaryKeySize) { outBlobs.clear(); return false; }
        if (pos + kMaxBinaryKeySize > file.size()) { outBlobs.clear(); return false; }
        std::memcpy(b.key, file.data() + pos, kMaxBinaryKeySize); pos += kMaxBinaryKeySize;
        uint64_t size = 0;
        if (!ReadAt(file, pos, size) || size == 0 || pos + size > file.size()) { outBlobs.clear(); return false; }
        b.data.assign(file.data() + pos, file.data() + pos + size); pos += static_cast<size_t>(size);
        outBlobs.push_back(std::move(b));
    }
    if (pos != file.size()) { outBlobs.clear(); return false; }
    return true;
}

} // namespace store
} // namespace fs
