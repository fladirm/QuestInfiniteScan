// FinalScan surfel raster pass 1 (contract §13.2 / §13.4): perspective-correct oriented ellipse per FsDrawRecord,
// opaque, depth-writing. Consumed by Runtime/Render/SurfelRenderer.cs through TWO DrawProceduralIndirect calls per
// frame that read back-to-back FsIndirectDrawArgs from one args buffer:
//   args offset 0   opaque leaf-surfel bucket  (records [0, N_opaque))            → pass 0 (SCAN/PLAN) or pass 2 (XRAY)
//   args offset 16  aggregate bucket (records [N_opaque, N_opaque + N_agg))       → pass 1 (SCAN/PLAN) or pass 2 (XRAY)
// The buckets are contiguous, so the aggregate record base is opaqueArgs.instanceCount / 2 (both buckets are written
// with instanceCount = visible * 2). Both args must carry startInstance = 0: SV_InstanceID base-instance semantics
// differ between graphics APIs, so the record base is derived in the shader from the args buffer instead
// (_DrawArgs, the same GraphicsBuffer bound as StructuredBuffer<uint>). The LOD cut may update only every second
// frame; nothing here assumes the args change per frame.
//
// Geometry. vertexCountPerInstance is read from the args: 6 = screen quad in ellipse space (needs the analytic rim
// discard); 3K >= 9 = K-triangle fan circumscribing the ellipse (Adreno LRZ friendly: pass 0 with FS_POLYGON has no
// discard and no alpha-to-coverage, coverage is geometric). Native writes 24 (8-gon) for the opaque bucket when
// SurfelRenderer.opaqueGeometry == PolygonFan.
//
// Single-pass instanced stereo. URP's UNITY_SETUP_INSTANCE_ID (UnityInstancing.hlsl, SHADEROPTIONS_XR_MAX_VIEWS <= 2)
// derives  unity_StereoEyeIndex = SV_InstanceID & 1  and  unity_InstanceID = unity_BaseInstanceID + (SV_InstanceID >> 1).
// Consecutive instance ids alternate eyes and every record is drawn twice, once per eye; the native cull writes
// instanceCount = visibleSurfels * 2 (donor lesson f4970df; Unity's instance multiplier cannot touch GPU-written
// args). unity_BaseInstanceID is 0 for procedural draws. Without stereo instancing the same doubled args are consumed
// by taking SV_InstanceID >> 1 and collapsing odd ids, so a mono camera never draws a record twice.
//
// Record encodings mirror Runtime/Render/SurfelDrawAbi.cs exactly (tests pin the managed side):
//   normalOct32       oct u16|u16 → [-1,1]; tangentAndRadii angle u16 | rMajor u8 log | rMinor u8 log (r = 0.5 mm * 2^(v/16));
//   colorOrHandle     RGBA8 (R low byte); alpha byte = coverage when FS_DRAW_FLAG_AGGREGATE (bit 3);
//   flags             bit0 detail, bit1 selected, bit2 erased-preview, bit3 aggregate, bit4 transient, bit5 no measured appearance.
Shader "FinalScan/Surfel"
{
    Properties
    {
        _Mode ("Render mode (0 scan, 1 xray, 2 plan)", Int) = 0
        _Opacity ("XRay opacity", Range(0.05, 1)) = 0.5
        _LightDir ("Light direction (world)", Vector) = (0.3, 0.8, 0.5, 0)
        _XRayZTest ("XRay ZTest", Int) = 4
        _OpaqueA2C ("Opaque alpha-to-coverage (quad geometry)", Int) = 0
        _ArgsSlot ("Args slot (per draw, property block)", Int) = 0
        _AggregateRecordBase ("Aggregate record base (-1 = derive from args)", Int) = -1
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    // FsDrawRecord, 32 B (Native~/include/finalscan_world_abi.h).
    struct FsDrawRecord
    {
        float3 center;          // world metres
        uint   normalOct32;
        uint   tangentAndRadii;
        uint   colorOrHandle;
        uint   surfaceId;
        uint   flags;
    };
    StructuredBuffer<FsDrawRecord> _DrawRecords;
    StructuredBuffer<uint> _DrawArgs;      // 2 x FsIndirectDrawArgs = 8 uints

    CBUFFER_START(FsSurfelParams)
        int    _Mode;
        float  _Opacity;
        float4 _LightDir;
        int    _ArgsSlot;
        int    _AggregateRecordBase;
    CBUFFER_END

    #define FS_FLAG_DETAIL     1u
    #define FS_FLAG_SELECTED   2u
    #define FS_FLAG_ERASED     4u
    #define FS_FLAG_AGGREGATE  8u
    #define FS_FLAG_TRANSIENT  16u
    #define FS_FLAG_NO_APPEARANCE 32u   // E4.2: geometry without measured appearance (XRAY / PLAN only): neutral grey, never normal pseudo-colour

    #define FS_RADIUS_BASE_M   0.0005
    #define FS_TWO_PI          6.28318530718

    float3 FsDecodeNormal(uint packed)
    {
        float fx = (packed & 0xFFFFu) / 65535.0 * 2.0 - 1.0;
        float fy = ((packed >> 16) & 0xFFFFu) / 65535.0 * 2.0 - 1.0;
        float fz = 1.0 - abs(fx) - abs(fy);
        if (fz < 0.0)
        {
            float ox = (1.0 - abs(fy)) * (fx >= 0.0 ? 1.0 : -1.0);
            float oy = (1.0 - abs(fx)) * (fy >= 0.0 ? 1.0 : -1.0);
            fx = ox; fy = oy;
        }
        return normalize(float3(fx, fy, fz));
    }

    float FsDecodeRadiusU8(uint v) { return FS_RADIUS_BASE_M * exp2(v / 16.0); }

    // Tangent frame convention shared with SurfelDrawAbi.TangentFrame.
    void FsTangentFrame(float3 n, float angle, out float3 tMajor, out float3 tMinor)
    {
        float3 reference = abs(n.y) > 0.99 ? float3(1, 0, 0) : float3(0, 1, 0);
        float3 t0 = normalize(cross(n, reference));
        float3 b0 = cross(n, t0);
        tMajor = cos(angle) * t0 + sin(angle) * b0;
        tMinor = cross(n, tMajor);
    }

    float4 FsDecodeColor(uint rgba8)
    {
        return float4(rgba8 & 0xFFu, (rgba8 >> 8) & 0xFFu, (rgba8 >> 16) & 0xFFu, (rgba8 >> 24) & 0xFFu) / 255.0;
    }

    // Screen-space hash for hashed alpha (aggregates keep their gaps under alpha-to-coverage, §13.4).
    float FsHash(uint2 pix, uint seed)
    {
        uint h = pix.x * 0x8da6b343u ^ pix.y * 0xd8163841u ^ seed * 0xcb1ab31fu;
        h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
        return (h & 0x00FFFFFFu) / 16777216.0;
    }

    // Ellipse-space vertex (unit circle = ellipse rim) for vertex v of an instance drawn with vertexCount vertices.
    //   6        : two-triangle quad, corners (±1, ±1)   → rim needs the fragment discard (r² > 1)
    //   3K (K>=3): K-triangle fan; triangle t = v / 3, corner v % 3 (0 = centre); circumscribed so the polygon covers the ellipse
    float2 FsEllipseVertex(uint v, uint vertexCount)
    {
        if (vertexCount < 9u)
        {
            // triangles (0,1,2) (3,4,5) over corners (-1,-1) (1,-1) (-1,1) / (-1,1) (1,-1) (1,1)
            uint q = v % 6u;
            float x = (q == 1u || q == 4u || q == 5u) ? 1.0 : -1.0;
            float y = (q == 2u || q == 3u || q == 5u) ? 1.0 : -1.0;
            return float2(x, y);
        }
        uint k = vertexCount / 3u;
        uint tri = v / 3u;
        uint corner = v % 3u;
        if (corner == 0u) return float2(0, 0);
        float step = FS_TWO_PI / k;
        float a = (tri + (corner == 2u ? 1u : 0u)) * step;
        float scale = 1.0 / cos(0.5 * step);        // circumscribe: edge midpoints touch the rim
        return float2(cos(a), sin(a)) * scale;
    }

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        #if UNITY_ANY_INSTANCING_ENABLED
            UNITY_VERTEX_INPUT_INSTANCE_ID          // uint instanceID : SV_InstanceID (URP macro)
        #else
            uint instanceID : SV_InstanceID;        // procedural draw without instancing keywords
        #endif
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
        float2 ellipse    : TEXCOORD1;      // ellipse-local coords: inside when dot(e,e) <= 1
        float3 normalWS   : TEXCOORD2;
        float4 color      : TEXCOORD3;      // rgb, a = coverage (aggregate) or 1
        nointerpolation uint2 idFlags : TEXCOORD4;   // surfaceId, flags
        nointerpolation float4 plane  : TEXCOORD5;   // world plane (n, -n.c) for FS_ANALYTIC_DEPTH
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings FsVert(Attributes input)
    {
        Varyings o = (Varyings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

        // Per-draw instance → record. See header: startInstance is 0 in both args, so the bucket base comes from the args buffer.
        #if defined(UNITY_STEREO_INSTANCING_ENABLED)
            uint local = unity_InstanceID;
            bool collapse = false;
        #else
            uint local = input.instanceID >> 1;
            bool collapse = (input.instanceID & 1u) != 0u;
        #endif
        uint slot = (uint)max(_ArgsSlot, 0) * 4u;
        uint vertexCount = _DrawArgs[slot + 0];
        uint base = 0u;
        if (_ArgsSlot != 0) base = _AggregateRecordBase >= 0 ? (uint)_AggregateRecordBase : (_DrawArgs[1] >> 1);
        uint record = base + local;

        FsDrawRecord r = _DrawRecords[record];
        float3 n = FsDecodeNormal(r.normalOct32);
        float angle = (r.tangentAndRadii & 0xFFFFu) * (FS_TWO_PI / 65536.0);
        float rMajor = FsDecodeRadiusU8((r.tangentAndRadii >> 16) & 0xFFu);
        float rMinor = FsDecodeRadiusU8((r.tangentAndRadii >> 24) & 0xFFu);
        float3 tMajor, tMinor;
        FsTangentFrame(n, angle, tMajor, tMinor);

        float2 q = FsEllipseVertex(input.vertexID, vertexCount);
        float3 posWS = r.center + tMajor * (rMajor * q.x) + tMinor * (rMinor * q.y);
        if (collapse || ((r.flags & FS_FLAG_ERASED) != 0u && _Mode == 0)) posWS = r.center;   // degenerate: rasterizes nothing

        o.positionWS = posWS;
        o.positionCS = TransformWorldToHClip(posWS);
        o.ellipse = q;
        o.normalWS = n;
        float4 c = FsDecodeColor(r.colorOrHandle);
        o.color = float4(c.rgb, (r.flags & FS_FLAG_AGGREGATE) != 0u ? c.a : 1.0);
        o.idFlags = uint2(r.surfaceId, r.flags);
        o.plane = float4(n, -dot(n, r.center));
        return o;
    }

    struct FragOut
    {
        float4 color : SV_Target;
        #if defined(FS_ANALYTIC_DEPTH)
        float depth : SV_Depth;
        #endif
    };

    // FS_NO_CLIP: geometric coverage only (polygon fan, no discard, early-Z/LRZ stays fully enabled).
    FragOut FsFragImpl(Varyings i, bool analyticRim, bool hashedAlpha)
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        FragOut o;
        uint flags = i.idFlags.y;

        float alpha = 1.0;
        if (analyticRim)
        {
            // Analytic ellipse coverage; alpha-to-coverage antialiases the rim with the MSAA samples.
            float r2 = dot(i.ellipse, i.ellipse);
            float w = max(fwidth(r2), 1e-4);
            float rim = saturate((1.0 - r2) / w);
            clip(rim - 0.001);
            alpha = rim;
        }
        if (hashedAlpha && (flags & FS_FLAG_AGGREGATE) != 0u)
        {
            // Hashed alpha: keep the fraction of pixels given by the coverage byte (gaps between leaves survive).
            float h = FsHash(uint2(i.positionCS.xy), i.idFlags.x);
            clip(i.color.a - h);
        }

        // Two-sided lambert with a fixed ambient (appearance authority arrives in C16+).
        float3 n = normalize(i.normalWS);
        float3 v = normalize(GetCameraPositionWS() - i.positionWS);
        if (dot(n, v) < 0.0) n = -n;
        float ndl = saturate(dot(n, normalize(_LightDir.xyz)));
        float3 col = ((flags & FS_FLAG_NO_APPEARANCE) != 0u ? float3(0.55, 0.55, 0.55) : i.color.rgb) * (0.35 + 0.65 * ndl);

        if ((flags & FS_FLAG_TRANSIENT) != 0u)
        {
            float lum = dot(col, float3(0.299, 0.587, 0.114));
            col = lerp(col, lum.xxx, 0.7) * float3(0.85, 0.9, 1.0);
        }
        if ((flags & FS_FLAG_SELECTED) != 0u) col = lerp(col, float3(1.0, 0.85, 0.2), 0.5);
        if ((flags & FS_FLAG_ERASED) != 0u) col = lerp(col, float3(1.0, 0.2, 0.2), 0.6);
        if (_Mode == 2) col = lerp(col, float3(0.9, 0.9, 0.9), 0.3);   // plan: flat tint until the plan camera lands (C24)

        o.color = float4(col, _Mode == 1 ? _Opacity : alpha);

        #if defined(FS_ANALYTIC_DEPTH)
            // Exact plane intersection along the view ray. The polygon/quad is coplanar with the surfel, so this equals
            // the interpolated depth up to interpolation precision; kept as a measured option (§13.2 early-Z on TBDR).
            float3 camPos = GetCameraPositionWS();
            float3 dir = i.positionWS - camPos;
            float denom = dot(i.plane.xyz, dir);
            float t = abs(denom) > 1e-6 ? -(dot(i.plane.xyz, camPos) + i.plane.w) / denom : 1.0;
            float3 hit = camPos + dir * t;
            float4 clip4 = TransformWorldToHClip(hit);
            o.depth = clip4.z / clip4.w;
        #endif
        return o;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        // Pass 0: opaque leaf bucket, SCAN / PLAN. Depth write. FS_POLYGON (material keyword, set with
        // opaqueGeometry == PolygonFan): no discard, no alpha-to-coverage → LRZ stays enabled. Quad geometry keeps the
        // analytic rim + AlphaToMask [_OpaqueA2C].
        Pass
        {
            Name "SurfelOpaque"
            Tags { "LightMode" = "FinalScanSurfel" }
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off
            AlphaToMask [_OpaqueA2C]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex FsVert
            #pragma fragment FsFragOpaque
            #pragma multi_compile_instancing
            #pragma shader_feature_local FS_POLYGON
            #pragma shader_feature_local FS_ANALYTIC_DEPTH
            #pragma exclude_renderers gles
            FragOut FsFragOpaque(Varyings i)
            {
                #if defined(FS_POLYGON)
                    return FsFragImpl(i, false, false);
                #else
                    return FsFragImpl(i, true, false);
                #endif
            }
            ENDHLSL
        }

        // Pass 1: aggregate bucket (§13.4 coverage LOD), SCAN / PLAN. Depth write, hashed alpha under alpha-to-coverage.
        Pass
        {
            Name "SurfelAggregate"
            Tags { "LightMode" = "FinalScanSurfelAggregate" }
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off
            AlphaToMask On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex FsVert
            #pragma fragment FsFragAggregate
            #pragma multi_compile_instancing
            #pragma shader_feature_local FS_ANALYTIC_DEPTH
            #pragma exclude_renderers gles
            FragOut FsFragAggregate(Varyings i) { return FsFragImpl(i, true, true); }
            ENDHLSL
        }

        // Pass 2: XRAY / RADAR (§13.5) for both buckets: no depth-prior cull happens natively; opacity blend and a
        // selectable depth test between surfels (_XRayZTest = LessEqual or Always). No depth write so passthrough stays readable.
        Pass
        {
            Name "SurfelXRay"
            Tags { "LightMode" = "FinalScanSurfelXRay" }
            Cull Off
            ZWrite Off
            ZTest [_XRayZTest]
            Blend SrcAlpha OneMinusSrcAlpha
            AlphaToMask Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex FsVert
            #pragma fragment FsFragXRay
            #pragma multi_compile_instancing
            #pragma exclude_renderers gles
            FragOut FsFragXRay(Varyings i) { return FsFragImpl(i, true, true); }
            ENDHLSL
        }
    }
    Fallback Off
}
