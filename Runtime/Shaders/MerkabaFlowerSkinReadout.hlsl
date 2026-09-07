#ifndef GENESIS_MERKABA_FLOWER_SKIN_READOUT_INCLUDED
#define GENESIS_MERKABA_FLOWER_SKIN_READOUT_INCLUDED

#include "MerkabaSphereFlower.generated.hlsl"
#include "MerkabaSphereFlowerDataAbi.generated.hlsl"
#include "MerkabaFlowerSidecar.hlsl"

// Derived 64-byte aligned sample. RGB, three Q5.26 innovations and certified
// optical midpoints are gathered once during page compaction; the fragment
// never follows persistent RGB/V groups or fetches a second optical program.
struct M8FlowerSkinDrawSample
{
    float3 CapturedRgb;
    uint Flags;
    int Amplitude3;
    int Amplitude4;
    int Amplitude5;
    uint Reserved;
    float4 Optical;
    float4 CaptureView;
};

struct M8FlowerSkinDrawHeader
{
    uint SplitBitsLo;
    uint SplitBitsHi;
    uint FirstSample;
    uint ParentEpoch;
};

struct M8FlowerSkinLocality
{
    uint3 Child;
    float3 Barycentric3;
    float3 Barycentric4;
    float3 Barycentric5;
    float3 DerivativeU3;
    float3 DerivativeU4;
    float3 DerivativeU5;
    float3 DerivativeV3;
    float3 DerivativeV4;
    float3 DerivativeV5;
};

// Inputs are the already evaluated, canonically ordered L2 wedge vertices.
// This derives the material-coordinate Jacobian; it does not construct a
// carrier, fit a plane, or permit a non-certain carrier to be emitted.
bool M8FlowerSkinRuntimeFrame(float3 p0, float3 p1, float3 p2,
    out float3 tangentU, out float3 tangentV, out float3 normal,
    out float3 barycentricU, out float3 barycentricV)
{
    tangentU = 0.0;
    tangentV = 0.0;
    normal = 0.0;
    barycentricU = 0.0;
    barycentricV = 0.0;
    float3 edgeU = p1 - p0;
    float3 edgeV = p2 - p0;
    float3 area = cross(edgeU, edgeV);
    float edgeSquared = dot(edgeU, edgeU);
    float areaSquared = dot(area, area);
    if (!(edgeSquared > 0.0) || !(areaSquared > 0.0) ||
        !M8FlowerIsFinite(edgeSquared) || !M8FlowerIsFinite(areaSquared)) return false;
    float edgeLength = sqrt(edgeSquared);
    tangentU = edgeU / edgeLength;
    normal = area / sqrt(areaSquared);
    tangentV = cross(normal, tangentU);
    float x = dot(edgeV, tangentU);
    float y = dot(edgeV, tangentV);
    if (!(y > 0.0) || !M8FlowerIsFinite(x) || !M8FlowerIsFinite(y)) return false;
    float inverseU = 1.0 / edgeLength;
    float inverseV = 1.0 / y;
    float skew = x * inverseU;
    barycentricU = float3(-inverseU, inverseU, 0.0);
    barycentricV = float3((skew - 1.0) * inverseV,
        -skew * inverseV, inverseV);
    return all(M8FlowerIsFinite(barycentricU)) && all(M8FlowerIsFinite(barycentricV));
}

float4 M8FlowerSkinUnpackHalf4(uint2 packed)
{
    return float4(f16tof32(packed.x & 0xffffu),
        f16tof32(packed.x >> 16u), f16tof32(packed.y & 0xffffu),
        f16tof32(packed.y >> 16u));
}

bool M8FlowerSkinColorMidpoint(M8ThreadColorInterval value,
    out float3 color)
{
    float4 lo = M8FlowerSkinUnpackHalf4(value.LowerLinearRgba);
    float4 hi = M8FlowerSkinUnpackHalf4(value.UpperLinearRgba);
    precise float4 midpoint = lo + (hi - lo) * 0.5;
    color = midpoint.rgb;
    return all(M8FlowerIsFinite(lo)) && all(M8FlowerIsFinite(hi)) && all(lo <= hi);
}

int M8FlowerSkinMetricMidpoint(M8FlowerVInterval value)
{
    // Unsigned subtraction retains the full signed endpoint span, including
    // [-2^31,2^31-1]. The final addition is deliberately modulo 2^32.
    return asint(asuint(value.Lower) +
        ((asuint(value.Upper) - asuint(value.Lower)) >> 1u));
}

bool M8FlowerSkinMaskValid(uint lo, uint hi)
{
    if ((hi & ~M8_FLOWER_SKIN_SPLIT_HIGH_MASK) != 0u) return false;
    uint root, l3;
    uint2 l4;
    M8FlowerUnpackSkinSplitBits(lo, hi, root, l3, l4);
    return M8FlowerSkinSplitClosure(root, l3, l4);
}

bool M8FlowerSkinGroupRangeValid(uint groupBase, uint lo, uint hi)
{
    uint count = countbits(lo) + countbits(hi);
    return groupBase != 0xffffffffu && count != 0u &&
        groupBase <= 0xffffffffu - count;
}

bool M8FlowerSkinRgbRunValid(M8ThreadRun run)
{
    if (!M8FlowerL2KeyValid(run.FlowerKey) ||
        !M8FlowerSkinMaskValid(run.SplitBitsLo, run.SplitBitsHi)) return false;
    if ((run.SplitBitsLo & 1u) == 0u)
        return run.GroupBase == 0xffffffffu && run.ProgramRef != 0xffffffffu;
    return M8FlowerSkinGroupRangeValid(run.GroupBase,
        run.SplitBitsLo, run.SplitBitsHi);
}

bool M8FlowerSkinMetricRunValid(M8FlowerSkinMetricRun run)
{
    return run.Reserved == 0u && M8FlowerL2KeyValid(run.FlowerKey) &&
        (run.SplitBitsLo & 1u) != 0u &&
        M8FlowerSkinMaskValid(run.SplitBitsLo, run.SplitBitsHi) &&
        M8FlowerSkinGroupRangeValid(run.GroupBase,
            run.SplitBitsLo, run.SplitBitsHi);
}

bool M8FlowerSkinUnion(M8ThreadRun rgb, bool rgbPresent,
    M8FlowerSkinMetricRun metric, bool metricPresent, uint parentEpoch,
    out uint2 split, out uint sampleCount)
{
    split = uint2(0u, 0u);
    sampleCount = 1u;
    // A stale record is absent. A current malformed record rejects publication;
    // it must never be made to look like an observed uniform signal.
    if (rgbPresent && rgb.ParentEpoch == parentEpoch && parentEpoch != 0u)
    {
        if (!M8FlowerSkinRgbRunValid(rgb)) return false;
        split |= uint2(rgb.SplitBitsLo, rgb.SplitBitsHi);
    }
    if (metricPresent && metric.ParentEpoch == parentEpoch && parentEpoch != 0u)
    {
        if (!M8FlowerSkinMetricRunValid(metric)) return false;
        if (rgbPresent && rgb.ParentEpoch == parentEpoch &&
            rgb.FlowerKey != metric.FlowerKey) return false;
        split |= uint2(metric.SplitBitsLo, metric.SplitBitsHi);
    }
    uint root, l3;
    uint2 l4;
    M8FlowerUnpackSkinSplitBits(split.x, split.y, root, l3, l4);
    sampleCount = M8FlowerCompactSkinValueCount(root, l3, l4);
    return sampleCount != 0xffffffffu;
}

uint M8FlowerSkinDrawIndex(M8FlowerSkinDrawHeader header, uint3 child)
{
    uint root, l3;
    uint2 l4;
    M8FlowerUnpackSkinSplitBits(header.SplitBitsLo, header.SplitBitsHi,
        root, l3, l4);
    if (root == 0u) return header.FirstSample;
    uint j3 = M8FlowerSkinL3ChildRank[child.x];
    if ((l3 & (1u << j3)) == 0u) return header.FirstSample + 1u + j3;
    uint canonicalParent = 7u * child.x + child.y;
    uint r4 = M8FlowerSkinL4ChildRank[canonicalParent];
    uint j4 = M8FlowerSkinL4ParentThread[canonicalParent];
    uint word = j4 < 32u ? l4.x : l4.y;
    if ((word & (1u << (j4 & 31u))) == 0u)
        return header.FirstSample + 8u + 7u * M8FlowerRank7(l3, j3) + r4;
    uint terminal = 49u * child.x + 7u * child.y + child.z;
    return header.FirstSample + 8u + 7u * countbits(l3) +
        7u * M8FlowerRank49(l4, j4) + M8FlowerSkinL5ChildRank[terminal];
}

// Used during dirty-page compaction only. regionLevel is the scan-authored
// group being emitted, never a camera/raster LOD choice. The root is level 2;
// each set union split adds all seven child samples as one publication.
bool M8FlowerCompileSkinRegion(float3 rootRgb, uint parentEpoch,
    uint flowerKey, uint regionLevel, uint3 child, M8ThreadRun rgb,
    bool rgbPresent, M8FlowerSkinMetricRun metric, bool metricPresent,
    out M8FlowerSkinDrawSample sample)
{
    sample.CapturedRgb = rootRgb;
    sample.Flags = 0u;
    sample.Amplitude3 = 0;
    sample.Amplitude4 = 0;
    sample.Amplitude5 = 0;
    sample.Reserved = 0u;
    sample.Optical = 0.0;
    sample.CaptureView = 0.0;
    if (!all(M8FlowerIsFinite(rootRgb)) || regionLevel < 2u || regionLevel > 5u ||
        any(child > 6u)) return false;

    bool useRgb = rgbPresent && parentEpoch != 0u &&
        rgb.ParentEpoch == parentEpoch;
    bool useMetric = metricPresent && parentEpoch != 0u &&
        metric.ParentEpoch == parentEpoch;
    if (useRgb && (rgb.FlowerKey != flowerKey ||
        !M8FlowerSkinRgbRunValid(rgb))) return false;
    if (useMetric && (metric.FlowerKey != flowerKey ||
        !M8FlowerSkinMetricRunValid(metric))) return false;

    [unroll]
    for (uint level = 3u; level <= 5u; level++)
    {
        if (level > regionLevel) break;
        uint group, rank;
        if (useRgb && M8FlowerSkinCompactChildAddress(rgb.GroupBase,
            rgb.SplitBitsLo, rgb.SplitBitsHi, level, child.x, child.y,
            child.z, group, rank))
        {
            if (group < rgb.GroupBase || group > 0xffffffffu / 112u ||
                !M8FlowerThreadRange(group * 112u, 112u)) return false;
            uint4 packed = M8_FLOWER_THREAD_SOURCE.Load4(group * 112u + rank * 16u);
            M8ThreadColorInterval value;
            value.LowerLinearRgba = packed.xy;
            value.UpperLinearRgba = packed.zw;
            float3 color;
            if (!M8FlowerSkinColorMidpoint(value, color))
                return false;
            sample.CapturedRgb = color;
        }
        if (useMetric && M8FlowerSkinCompactChildAddress(metric.GroupBase,
            metric.SplitBitsLo, metric.SplitBitsHi, level, child.x, child.y,
            child.z, group, rank))
        {
            if (group < metric.GroupBase || group > 0xffffffffu / 56u ||
                !M8FlowerDetailRange(group * 56u, 56u))
                return false;
            uint2 packed = M8_FLOWER_DETAIL_SOURCE.Load2(group * 56u + rank * 8u);
            M8FlowerVInterval value;
            value.Lower = asint(packed.x);
            value.Upper = asint(packed.y);
            if (value.Lower > value.Upper) return false;
            int amplitude = M8FlowerSkinMetricMidpoint(value);
            if (level == 3u) sample.Amplitude3 = amplitude;
            else if (level == 4u) sample.Amplitude4 = amplitude;
            else sample.Amplitude5 = amplitude;
        }
    }

    if (useRgb && rgb.ProgramRef != 0xffffffffu)
    {
        if (rgb.ProgramRef > 0xffffffffu / 48u ||
            !M8FlowerThreadRange(rgb.ProgramRef * 48u, 48u)) return false;
        uint address = rgb.ProgramRef * 48u;
        uint4 a = M8_FLOWER_THREAD_SOURCE.Load4(address);
        uint4 b = M8_FLOWER_THREAD_SOURCE.Load4(address + 16u);
        uint4 c = M8_FLOWER_THREAD_SOURCE.Load4(address + 32u);
        M8ThreadProgramRecord program;
        program.Flags = a.x; program.Reserved0 = a.y;
        program.Reserved1 = a.z; program.Reserved2 = a.w;
        program.OpticalLower = b.xy; program.OpticalUpper = b.zw;
        program.CaptureViewLower = c.xy; program.CaptureViewUpper = c.zw;
        if (program.Reserved0 != 0u || program.Reserved1 != 0u ||
            program.Reserved2 != 0u || (program.Flags & ~1u) != 0u)
            return false;
        float4 lo = M8FlowerSkinUnpackHalf4(program.OpticalLower);
        float4 hi = M8FlowerSkinUnpackHalf4(program.OpticalUpper);
        float4 viewLo = M8FlowerSkinUnpackHalf4(program.CaptureViewLower);
        float4 viewHi = M8FlowerSkinUnpackHalf4(program.CaptureViewUpper);
        if (!all(M8FlowerIsFinite(lo)) || !all(M8FlowerIsFinite(hi)) ||
            !all(M8FlowerIsFinite(viewLo)) || !all(M8FlowerIsFinite(viewHi)) ||
            !all(lo <= hi) || !all(viewLo <= viewHi)) return false;
        if ((program.Flags & 1u) != 0u)
        {
            sample.Optical = lo + (hi - lo) * 0.5;
            sample.CaptureView = viewLo + (viewHi - viewLo) * 0.5;
            sample.Flags = 1u;
        }
        else if (any(lo != 0.0) || any(hi != 0.0) ||
            any(viewLo != 0.0) || any(viewHi != 0.0)) return false;
    }
    return true;
}

// Inverse locality used by compaction to emit only actual seven-value groups.
// The generated inverse thread map is global; no per-carrier child map exists.
uint3 M8FlowerSkinGroupChild(uint regionLevel, uint parentThread,
    uint childThreadRank)
{
    if (regionLevel == 3u)
    {
        uint c3 = M8FlowerSkinThreadToCanonical[57u * childThreadRank];
        return uint3(c3, 0u, 0u);
    }
    if (regionLevel == 4u)
    {
        uint canonical = M8FlowerSkinThreadToCanonical[
            57u * parentThread + 1u + 8u * childThreadRank] - 7u;
        return uint3(canonical / 7u, canonical % 7u, 0u);
    }
    uint j3 = parentThread / 7u;
    uint r4 = parentThread % 7u;
    uint canonical = M8FlowerSkinThreadToCanonical[
        57u * j3 + 2u + 8u * r4 + childThreadRank] - 56u;
    return uint3(canonical / 49u, (canonical / 7u) % 7u, canonical % 7u);
}

M8FlowerSkinDrawHeader M8FlowerLoadSkinDrawHeader(uint address)
{
    uint4 value=M8_FLOWER_THREAD_SOURCE.Load4(address);
    M8FlowerSkinDrawHeader header;
    header.SplitBitsLo=value.x;header.SplitBitsHi=value.y;
    header.FirstSample=value.z;header.ParentEpoch=value.w;
    return header;
}

M8FlowerSkinDrawSample M8FlowerLoadSkinDrawSample(uint index)
{
    // All reconstructed channels and certified optical fields share this
    // aligned packet; no subsequent persistent atlas lookup occurs per pixel.
    uint address=index*64u;
    uint4 a=M8_FLOWER_THREAD_SOURCE.Load4(address);
    uint4 b=M8_FLOWER_THREAD_SOURCE.Load4(address+16u);
    M8FlowerSkinDrawSample sample;
    sample.CapturedRgb=asfloat(a.xyz);sample.Flags=a.w;
    sample.Amplitude3=asint(b.x);sample.Amplitude4=asint(b.y);
    sample.Amplitude5=asint(b.z);sample.Reserved=b.w;
    sample.Optical=asfloat(M8_FLOWER_THREAD_SOURCE.Load4(address+32u));
    sample.CaptureView=asfloat(M8_FLOWER_THREAD_SOURCE.Load4(address+48u));
    return sample;
}

bool M8FlowerSkinResidentLayout(uint ownerRef,uint flowerKey,
    out M8ThreadRun rgb,out bool rgbPresent,out M8FlowerSkinMetricRun metric,
    out bool metricPresent,out uint epoch,out uint2 split,out uint sampleCount)
{
    epoch=M8FlowerGetOwnerEpoch(ownerRef);
    uint resident;
    rgbPresent=M8FlowerFindThreadRun(ownerRef,flowerKey,epoch,rgb,resident);
    metricPresent=M8FlowerFindMetricRun(ownerRef,flowerKey,epoch,metric,resident);
    bool canonical=M8FlowerL2KeyValid(flowerKey);
    bool valid=M8FlowerSkinUnion(rgb,rgbPresent,metric,metricPresent,epoch,split,sampleCount);
    return canonical && valid;
}

#if defined(M8_FLOWER_SIDECAR_WRITE) || defined(M8_FLOWER_THREAD_DRAW_WRITE)
void M8FlowerStoreSkinDrawSample(uint address,M8FlowerSkinDrawSample sample)
{
    _M8ThreadAtlasPages.Store4(address,uint4(asuint(sample.CapturedRgb),sample.Flags));
    _M8ThreadAtlasPages.Store4(address+16u,uint4(asuint(sample.Amplitude3),
        asuint(sample.Amplitude4),asuint(sample.Amplitude5),0u));
    _M8ThreadAtlasPages.Store4(address+32u,asuint(sample.Optical));
    _M8ThreadAtlasPages.Store4(address+48u,asuint(sample.CaptureView));
}

// Count/allocation happens at page scope; this emits only the exact union's
// explicit groups into a caller-reserved unpublished range of the draw pool.
bool M8FlowerCompileSkinDrawProgram(uint ownerRef,uint flowerKey,float3 rootRgb,
    uint headerAddress,uint firstSampleAddress,uint sampleCapacity,
    out uint sampleCount)
{
    sampleCount=0u;
    M8ThreadRun rgb;M8FlowerSkinMetricRun metric;
    bool rgbPresent,metricPresent;uint epoch;uint2 split;
    if(!M8FlowerSkinResidentLayout(ownerRef,flowerKey,rgb,rgbPresent,metric,
        metricPresent,epoch,split,sampleCount) || sampleCount>sampleCapacity ||
        (firstSampleAddress&63u)!=0u || headerAddress<M8_FLOWER_DRAW_DATA ||
        firstSampleAddress<M8_FLOWER_DRAW_DATA ||
        headerAddress-M8_FLOWER_DRAW_DATA>67108864u-16u ||
        sampleCount>67108864u/64u ||
        firstSampleAddress-M8_FLOWER_DRAW_DATA>67108864u-sampleCount*64u)return false;
    M8FlowerSkinDrawSample sample;
    if(!M8FlowerCompileSkinRegion(rootRgb,epoch,flowerKey,2u,uint3(0u,0u,0u),
        rgb,rgbPresent,metric,metricPresent,sample))return false;
    M8FlowerStoreSkinDrawSample(firstSampleAddress,sample);
    [loop]for(uint word=0u;word<2u;word++)
    {
        uint bits=split[word];
        [loop]while(bits!=0u)
        {
            uint bit=(uint)firstbitlow(bits);
            uint ordinal=word*32u+bit;
            uint level=ordinal==0u?3u:(ordinal<8u?4u:5u);
            uint parent=ordinal==0u?0u:(ordinal<8u?ordinal-1u:ordinal-8u);
            uint group=M8FlowerSplitRank(split,ordinal);
            [loop]for(uint rank=0u;rank<7u;rank++)
            {
                uint3 child=M8FlowerSkinGroupChild(level,parent,rank);
                if(!M8FlowerCompileSkinRegion(rootRgb,epoch,flowerKey,level,child,
                    rgb,rgbPresent,metric,metricPresent,sample))return false;
                M8FlowerStoreSkinDrawSample(firstSampleAddress+(1u+7u*group+rank)*64u,sample);
            }
            bits&=bits-1u;
        }
    }
    DeviceMemoryBarrier();
    _M8ThreadAtlasPages.Store4(headerAddress,
        uint4(split,firstSampleAddress/64u,epoch));
    return true;
}

// All six source wedges address the same generated seven-site carrier and
// therefore the same fixed 399-position thread. Preserve the existing key ABI
// using the first generated source wedge as its single canonical spelling.
uint M8FlowerCarrierSkinKey(uint carrier,uint rootSigns,uint hubSector)
{
    uint source=M8FlowerL2Wedge[6u*carrier].y;
    return source | ((rootSigns&1u)<<M8_FLOWER_L2_KEY_ROOT_SHIFT) |
        (hubSector<<M8_FLOWER_L2_KEY_SECTOR_SHIFT);
}

bool M8FlowerCarrierSkinBytes(uint ownerRef,uint carrier,uint activeWedges,
    uint rootSigns,uint hubSector,out uint bytes)
{
    bytes=0u;
    if(carrier>=128u || activeWedges==0u || activeWedges>=64u ||
        hubSector>=32u)return false;
    if(ownerRef==0u)return true;
    M8ThreadRun rgb;M8FlowerSkinMetricRun metric;
    bool rgbPresent,metricPresent;uint epoch;uint2 split;uint count;
    uint key=M8FlowerCarrierSkinKey(carrier,rootSigns,hubSector);
    if(!M8FlowerSkinResidentLayout(ownerRef,key,rgb,rgbPresent,metric,
        metricPresent,epoch,split,count))return false;
    // One 16B header in a 64B-aligned prefix, followed by one compact union
    // signal. Uniform M8-only carriers allocate no derived skin data.
    if(epoch!=0u && (rgbPresent || metricPresent))bytes=64u+64u*count;
    return true;
}

bool M8FlowerCompileCarrierSkin(uint ownerRef,uint carrier,uint activeWedges,
    uint rootSigns,uint hubSector,float3 capturedRgb,uint address,uint byteCount,
    out uint threadRef)
{
    threadRef=0xffffffffu;
    uint required;
    if(!M8FlowerCarrierSkinBytes(ownerRef,carrier,activeWedges,rootSigns,
        hubSector,required) || required!=byteCount)return false;
    if(required==0u)return true;
    if((address&63u)!=0u || address<M8_FLOWER_DRAW_DATA ||
        byteCount>67108864u || address-M8_FLOWER_DRAW_DATA>67108864u-byteCount)return false;
    uint count=(byteCount-64u)/64u,written;
    uint key=M8FlowerCarrierSkinKey(carrier,rootSigns,hubSector);
    if(!M8FlowerCompileSkinDrawProgram(ownerRef,key,capturedRgb,address,
        address+64u,count,written) || written!=count)return false;
    threadRef=address;
    return true;
}
#endif

M8FlowerSkinLocality M8FlowerResolveSkinLocality(float3 barycentric,
    uint wedge, float3 derivativeU, float3 derivativeV)
{
    M8FlowerSkinLocality result;
    result.Child.x = M8FlowerSkinDescendWithJacobian(barycentric, wedge,
        derivativeU, derivativeV);
    result.Barycentric3 = barycentric;
    result.DerivativeU3 = derivativeU;
    result.DerivativeV3 = derivativeV;
    result.Child.y = M8FlowerSkinDescendWithJacobian(barycentric, wedge,
        derivativeU, derivativeV);
    result.Barycentric4 = barycentric;
    result.DerivativeU4 = derivativeU;
    result.DerivativeV4 = derivativeV;
    result.Child.z = M8FlowerSkinDescendWithJacobian(barycentric, wedge,
        derivativeU, derivativeV);
    result.Barycentric5 = barycentric;
    result.DerivativeU5 = derivativeU;
    result.DerivativeV5 = derivativeV;
    return result;
}

// The derivatives here are with respect to the runtime L2 orthonormal frame,
// not screen derivatives and not a generated/static world plane.
float3 M8FlowerEvaluateSkinNormal(M8FlowerSkinLocality locality,
    M8FlowerSkinDrawSample sample, float3 tangentU, float3 tangentV,
    float3 normal, out float metricV)
{
    const float q26 = 1.0 / 67108864.0;
    float3 amplitude = float3(sample.Amplitude3, sample.Amplitude4,
        sample.Amplitude5) * q26;
    float3 g3 = M8FlowerSkinBubbleGradient(locality.Barycentric3);
    float3 g4 = M8FlowerSkinBubbleGradient(locality.Barycentric4);
    float3 g5 = M8FlowerSkinBubbleGradient(locality.Barycentric5);
    precise float du = amplitude.x * dot(g3, locality.DerivativeU3) +
        amplitude.y * dot(g4, locality.DerivativeU4) +
        amplitude.z * dot(g5, locality.DerivativeU5);
    precise float dv = amplitude.x * dot(g3, locality.DerivativeV3) +
        amplitude.y * dot(g4, locality.DerivativeV4) +
        amplitude.z * dot(g5, locality.DerivativeV5);
    metricV = M8FlowerNestedV(locality.Barycentric3, locality.Barycentric4,
        locality.Barycentric5, amplitude);
    precise float denominatorSquared = 1.0 + du * du + dv * dv;
    return (normal - du * tangentU - dv * tangentV) /
        sqrt(denominatorSquared);
}

#endif
