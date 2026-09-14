// FinalScan native logging. Tag "FinalScanNative". JSON payloads are logged as one
// logcat line prefixed "FS-NATIVE <name> " (plus chunked copies when logcat would truncate).
#pragma once
#include <cstdarg>
#include <cstdio>
#include <string>

#if defined(__ANDROID__)
#include <android/log.h>
#endif

namespace fs {

inline void LogRaw(int level, const char* text) {
#if defined(__ANDROID__)
    __android_log_write(level, "FinalScanNative", text);
#else
    (void)level;
    std::fprintf(stderr, "FinalScanNative: %s\n", text);
#endif
}

inline void Log(const char* fmt, ...) {
    char buf[2048];
    va_list ap;
    va_start(ap, fmt);
    std::vsnprintf(buf, sizeof(buf), fmt, ap);
    va_end(ap);
#if defined(__ANDROID__)
    LogRaw(ANDROID_LOG_INFO, buf);
#else
    LogRaw(4, buf);
#endif
}

inline void LogError(const char* fmt, ...) {
    char buf[2048];
    va_list ap;
    va_start(ap, fmt);
    std::vsnprintf(buf, sizeof(buf), fmt, ap);
    va_end(ap);
#if defined(__ANDROID__)
    LogRaw(ANDROID_LOG_ERROR, buf);
#else
    LogRaw(6, buf);
#endif
}

// Single-line JSON log. logcat truncates lines around 4 KiB, so when the payload is
// longer the full text is additionally emitted as numbered chunks (FS-NATIVE-CHUNK).
inline void LogJson(const char* name, const std::string& json) {
    const size_t kMaxChunked = 65536;   // beyond this only the (truncated) single line is logged
    std::string line = "FS-NATIVE ";
    line += name;
    line += ' ';
    if (json.size() > kMaxChunked) {
        char head[96];
        std::snprintf(head, sizeof(head), "[%zu bytes, truncated; full payload via getter] ", json.size());
        line += head;
        line += json.substr(0, 3500);
    } else line += json;
#if defined(__ANDROID__)
    LogRaw(ANDROID_LOG_INFO, line.c_str());
#else
    LogRaw(4, line.c_str());
#endif
    const size_t kChunk = 3500;
    if (json.size() > kChunk && json.size() <= kMaxChunked) {
        const size_t chunks = (json.size() + kChunk - 1) / kChunk;
        for (size_t i = 0; i < chunks; ++i) {
            char head[96];
            std::snprintf(head, sizeof(head), "FS-NATIVE-CHUNK %s %zu/%zu ", name, i + 1, chunks);
            std::string part = head;
            part += json.substr(i * kChunk, kChunk);
#if defined(__ANDROID__)
            LogRaw(ANDROID_LOG_INFO, part.c_str());
#else
            LogRaw(4, part.c_str());
#endif
        }
    }
}

} // namespace fs
