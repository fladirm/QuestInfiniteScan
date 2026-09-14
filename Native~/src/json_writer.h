// FinalScan native: minimal streaming JSON writer (header-only, host-testable).
// Produces compact, valid JSON. Non-finite doubles are emitted as null.
#pragma once
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

namespace fs {

class JsonWriter {
public:
    JsonWriter() { out_.reserve(4096); }

    void BeginObject() { Prefix(); out_ += '{'; stack_.push_back(Frame{true, true}); }
    void EndObject() { out_ += '}'; stack_.pop_back(); }
    void BeginArray() { Prefix(); out_ += '['; stack_.push_back(Frame{false, true}); }
    void EndArray() { out_ += ']'; stack_.pop_back(); }

    void Key(const char* key) {
        Comma();
        WriteString(key ? key : "");
        out_ += ':';
        pendingKey_ = true;
    }

    void String(const char* value) { Prefix(); WriteString(value ? value : ""); }
    void String(const std::string& value) { Prefix(); WriteString(value.c_str()); }
    void Bool(bool value) { Prefix(); out_ += value ? "true" : "false"; }
    void Null() { Prefix(); out_ += "null"; }
    void Int(int64_t value) { Prefix(); char b[32]; std::snprintf(b, sizeof(b), "%lld", static_cast<long long>(value)); out_ += b; }
    void UInt(uint64_t value) { Prefix(); char b[32]; std::snprintf(b, sizeof(b), "%llu", static_cast<unsigned long long>(value)); out_ += b; }
    void Double(double value, int precision = 6) {
        Prefix();
        if (!std::isfinite(value)) { out_ += "null"; return; }
        char b[64];
        std::snprintf(b, sizeof(b), "%.*g", precision, value);
        out_ += b;
    }
    void Hex(uint64_t value) { Prefix(); char b[32]; std::snprintf(b, sizeof(b), "\"0x%llx\"", static_cast<unsigned long long>(value)); out_ += b; }

    // Convenience: key + value.
    void KV(const char* key, const char* value) { Key(key); String(value); }
    void KV(const char* key, const std::string& value) { Key(key); String(value); }
    void KV(const char* key, bool value) { Key(key); Bool(value); }
    void KV(const char* key, int32_t value) { Key(key); Int(value); }
    void KV(const char* key, int64_t value) { Key(key); Int(value); }
    void KV(const char* key, uint32_t value) { Key(key); UInt(value); }
    void KV(const char* key, uint64_t value) { Key(key); UInt(value); }
    void KV(const char* key, double value, int precision = 6) { Key(key); Double(value, precision); }
    void KVHex(const char* key, uint64_t value) { Key(key); Hex(value); }
    void KVNull(const char* key) { Key(key); Null(); }
    void RawValue(const char* key, const std::string& json) { Key(key); Prefix(); out_ += json.empty() ? "null" : json; }

    const std::string& Str() const { return out_; }
    std::string Take() { std::string s; s.swap(out_); stack_.clear(); pendingKey_ = false; return s; }
    bool Balanced() const { return stack_.empty(); }

private:
    struct Frame { bool object; bool first; };

    void Comma() {
        if (stack_.empty()) return;
        Frame& f = stack_.back();
        if (!f.first) out_ += ',';
        f.first = false;
    }
    void Prefix() {
        if (pendingKey_) { pendingKey_ = false; return; }
        Comma();
    }
    void WriteString(const char* s) {
        out_ += '"';
        for (const unsigned char* p = reinterpret_cast<const unsigned char*>(s); *p; ++p) {
            unsigned char c = *p;
            switch (c) {
                case '"': out_ += "\\\""; break;
                case '\\': out_ += "\\\\"; break;
                case '\n': out_ += "\\n"; break;
                case '\r': out_ += "\\r"; break;
                case '\t': out_ += "\\t"; break;
                case '\b': out_ += "\\b"; break;
                case '\f': out_ += "\\f"; break;
                default:
                    if (c < 0x20) { char b[8]; std::snprintf(b, sizeof(b), "\\u%04x", c); out_ += b; }
                    else out_ += static_cast<char>(c);
            }
        }
        out_ += '"';
    }

    std::string out_;
    std::vector<Frame> stack_;
    bool pendingKey_ = false;
};

// Copies json into a caller buffer per the C ABI contract: writes up to cap bytes
// (NUL-terminated) and returns the full required length (excluding the NUL).
inline int32_t CopyJsonOut(const std::string& json, char* buf, int32_t cap) {
    const int32_t need = static_cast<int32_t>(json.size());
    if (buf != nullptr && cap > 0) {
        const int32_t n = need < cap - 1 ? need : cap - 1;
        std::memcpy(buf, json.data(), static_cast<size_t>(n));
        buf[n] = '\0';
    }
    return need;
}

} // namespace fs
