Shader "Hidden/Genesis/SigmaPrism/StreamRevalidation"
{
    HLSLINCLUDE
    #pragma target 4.5

    #include "SigmaCarrierAbi.hlsl"
    #include "SigmaTopologyAbi.hlsl"
    #include "SigmaInverseAbi.hlsl"
    #include "SigmaInverseWorkAbi.hlsl"
    #include "SigmaGeometryReadout.hlsl"

    StructuredBuffer<float4> _ReadoutVertices;
    StructuredBuffer<SigmaCarrierPageMetaGpu> _PageMetadata;
    StructuredBuffer<uint> _TopologyCellFlags;
    StructuredBuffer<uint4> _TopologyPageKeys;
    StructuredBuffer<uint4> _RevalidationContext;
    StructuredBuffer<uint4> _RevalidationPageSnapshot;
    StructuredBuffer<SigmaPageVisibilityGpu> _StreamPageVisibility;
    StructuredBuffer<uint2> _StreamBundleCalibration;
    StructuredBuffer<uint> _StreamBundleRayEpoch;
    uint _SegmentIndex;

    #define SIGMA_READOUT_EXTENT 65u
    #define SIGMA_READOUT_SAMPLES 4225u
    #define SIGMA_READOUT_VERTICES_PER_PAGE 24576u

    struct SigmaHistoricalClearVaryings
    {
        float4 positionCS : SV_POSITION;
        nointerpolation uint targetLayer : SV_RenderTargetArrayIndex;
    };

    struct SigmaHistoricalOutput
    {
        float2 depthSupport : SV_Target0;
        uint4 carrierPage : SV_Target1;
        float4 carrierUvNormal : SV_Target2;
        uint4 stateKey : SV_Target3;
    };

    struct SigmaHistoricalClearOutput
    {
        float2 depthSupport : SV_Target0;
        uint4 carrierPage : SV_Target1;
        float4 carrierUvNormal : SV_Target2;
        uint4 stateKey : SV_Target3;
        float hardwareDepth : SV_Depth;
    };

    struct SigmaHistoricalVaryings
    {
        float4 positionCS : SV_POSITION;
        float3 positionOptical : TEXCOORD0;
        float3 normalOptical : TEXCOORD1;
        float2 carrierLocal : TEXCOORD2;
        nointerpolation float support : TEXCOORD3;
        nointerpolation uint4 pageCoordinate : TEXCOORD4;
        nointerpolation uint4 stateKey : TEXCOORD5;
        nointerpolation uint targetLayer : SV_RenderTargetArrayIndex;
    };

    SigmaHistoricalClearVaryings SigmaHistoricalClearVertex(
        uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
    {
        SigmaHistoricalClearVaryings output;
        float2 position = vertexId == 0u ? float2(-1.0, -1.0) :
            vertexId == 1u ? float2(-1.0, 3.0) : float2(3.0, -1.0);
        output.positionCS = float4(position, 0.0, 1.0);
        output.targetLayer = instanceId & 1u;
        return output;
    }

    SigmaHistoricalClearOutput SigmaHistoricalClearFragment(
        SigmaHistoricalClearVaryings input)
    {
        SigmaHistoricalClearOutput output;
        output.depthSupport = 0.0;
        output.carrierPage = 0u;
        output.carrierUvNormal = 0.0;
        output.stateKey = 0u;
        #if UNITY_REVERSED_Z
            output.hardwareDepth = 0.0;
        #else
            output.hardwareDepth = 1.0;
        #endif
        return output;
    }

    uint SigmaReadoutIndex(uint pageSlot, uint x, uint y)
    {
        return pageSlot * SIGMA_READOUT_SAMPLES +
            y * SIGMA_READOUT_EXTENT + x;
    }

    uint2 SigmaTriangleCorner(uint triangleIndex, uint corner)
    {
        if (triangleIndex == 0u)
        {
            if (corner == 0u)
                return uint2(0u, 0u);
            if (corner == 1u)
                return uint2(1u, 0u);
            return uint2(0u, 1u);
        }
        if (corner == 0u)
            return uint2(1u, 0u);
        if (corner == 1u)
            return uint2(1u, 1u);
        return uint2(0u, 1u);
    }

    float2 SigmaEncodeOctahedral(float3 normal)
    {
        normal /= max(1e-20,
            abs(normal.x) + abs(normal.y) + abs(normal.z));
        float2 encoded = normal.xy;
        if (normal.z < 0.0)
            encoded = (1.0 - abs(encoded.yx)) *
                float2(encoded.x >= 0.0 ? 1.0 : -1.0,
                    encoded.y >= 0.0 ? 1.0 : -1.0);
        return encoded;
    }

    float SigmaHistoricalCalibration(uint calibrationBase, uint eye,
        uint field)
    {
        return SigmaQ48ToReadoutFloat(_StreamBundleCalibration[
            calibrationBase + eye * SIGMA_DEPTH_VIEW_STRIDE + field]);
    }

    float3x3 SigmaHistoricalWorldFromOptical(uint calibrationBase, uint eye)
    {
        return float3x3(
            SigmaHistoricalCalibration(calibrationBase, eye,
                SIGMA_CAL_R00 + 0u),
            SigmaHistoricalCalibration(calibrationBase, eye,
                SIGMA_CAL_R00 + 1u),
            SigmaHistoricalCalibration(calibrationBase, eye,
                SIGMA_CAL_R00 + 2u),
            SigmaHistoricalCalibration(calibrationBase, eye,
                SIGMA_CAL_R00 + 3u),
            SigmaHistoricalCalibration(calibrationBase, eye,
                SIGMA_CAL_R00 + 4u),
            SigmaHistoricalCalibration(calibrationBase, eye,
                SIGMA_CAL_R00 + 5u),
            SigmaHistoricalCalibration(calibrationBase, eye,
                SIGMA_CAL_R00 + 6u),
            SigmaHistoricalCalibration(calibrationBase, eye,
                SIGMA_CAL_R00 + 7u),
            SigmaHistoricalCalibration(calibrationBase, eye,
                SIGMA_CAL_R00 + 8u));
    }

    float3 SigmaHistoricalTranslation(uint calibrationBase, uint eye)
    {
        return float3(
            SigmaHistoricalCalibration(calibrationBase, eye,
                SIGMA_CAL_TX + 0u),
            SigmaHistoricalCalibration(calibrationBase, eye,
                SIGMA_CAL_TX + 1u),
            SigmaHistoricalCalibration(calibrationBase, eye,
                SIGMA_CAL_TX + 2u));
    }

    float4 SigmaHistoricalClip(float3 optical, float fx, float fy, float cx,
        float cy, float nearPlane, float farPlane, float2 resolution)
    {
        float z = max(optical.z, 1e-20);
        float2 pixel = float2(fx * optical.x / z + cx,
            fy * optical.y / z + cy);
        float2 ndc = float2(pixel.x * (2.0 / resolution.x) - 1.0,
            1.0 - pixel.y * (2.0 / resolution.y));
        float rasterFar = max(farPlane, nearPlane + 1e-3);
        #if UNITY_REVERSED_Z
            float clipZ = nearPlane * (rasterFar - z) /
                (rasterFar - nearPlane);
        #else
            float clipZ = rasterFar * (z - nearPlane) /
                (rasterFar - nearPlane);
        #endif
        return float4(ndc * z, clipZ, z);
    }

    SigmaHistoricalVaryings SigmaHistoricalVertex(uint vertexId : SV_VertexID,
        uint instanceId : SV_InstanceID)
    {
        SigmaHistoricalVaryings output;
        uint eye = instanceId & 1u;
        uint4 context = _RevalidationContext[0u];
        uint4 owner = _RevalidationContext[1u];
        uint bundleSlot = context.x;
        uint calibrationBase = context.z;
        uint activePage = vertexId / SIGMA_READOUT_VERTICES_PER_PAGE;
        uint pageVertex = vertexId -
            activePage * SIGMA_READOUT_VERTICES_PER_PAGE;
        uint4 snapshot = _RevalidationPageSnapshot[activePage];
        uint pageSlot = snapshot.x;
        uint primitive = pageVertex / 3u;
        uint corner = pageVertex - primitive * 3u;
        uint triangleIndex = primitive & 1u;
        uint cell = primitive >> 1u;
        uint cellX = cell & 63u;
        uint cellY = cell >> 6u;
        SigmaCarrierPageMetaGpu metadata = _PageMetadata[pageSlot];
        SigmaPageVisibilityGpu visibility =
            _StreamPageVisibility[pageSlot];
        uint4 topologyKey = _TopologyPageKeys[pageSlot];
        uint cellTopology = _TopologyCellFlags[
            pageSlot * SIGMA_PAGE_SAMPLE_COUNT + cell];
        uint triangleCut = triangleIndex == 0u
            ? SIGMA_TOPOLOGY_TRIANGLE_0_CUT
            : SIGMA_TOPOLOGY_TRIANGLE_1_CUT;
        bool snapshotCurrent = metadata.generation == snapshot.y &&
            metadata.revision == snapshot.z &&
            visibility.bornRetired.y == snapshot.w &&
            visibility.pins.w == owner.x + 1u;
        bool topologyCurrent = snapshotCurrent &&
            topologyKey.x == metadata.generation &&
            topologyKey.y == metadata.revision && topologyKey.z == 1u;

        uint2 c0 = SigmaTriangleCorner(triangleIndex, 0u);
        uint2 c1 = SigmaTriangleCorner(triangleIndex, 1u);
        uint2 c2 = SigmaTriangleCorner(triangleIndex, 2u);
        float4 r0 = _ReadoutVertices[SigmaReadoutIndex(pageSlot,
            cellX + c0.x, cellY + c0.y)];
        float4 r1 = _ReadoutVertices[SigmaReadoutIndex(pageSlot,
            cellX + c1.x, cellY + c1.y)];
        float4 r2 = _ReadoutVertices[SigmaReadoutIndex(pageSlot,
            cellX + c2.x, cellY + c2.y)];
        float support = min(r0.w, min(r1.w, r2.w));
        float3 areaVector = cross(r1.xyz - r0.xyz, r2.xyz - r0.xyz);
        float areaSquared = dot(areaVector, areaVector);
        bool valid = bundleSlot < SIGMA_STREAM_BUNDLE_CAPACITY &&
            topologyCurrent && (cellTopology & triangleCut) == 0u &&
            support > 0.0 && areaSquared > 1e-20;

        uint2 selectedCorner = SigmaTriangleCorner(triangleIndex, corner);
        float3 position = corner == 0u ? r0.xyz :
            corner == 1u ? r1.xyz : r2.xyz;
        float3 normal = valid ? areaVector * rsqrt(areaSquared) :
            float3(0.0, 0.0, 1.0);
        float3x3 worldFromOptical =
            SigmaHistoricalWorldFromOptical(calibrationBase, eye);
        float3 translation = SigmaHistoricalTranslation(calibrationBase, eye);
        float3x3 opticalFromWorld = transpose(worldFromOptical);
        float3 positionOptical = mul(opticalFromWorld, position - translation);
        float3 normalOptical = normalize(mul(opticalFromWorld, normal));
        float nearPlane = SigmaHistoricalCalibration(calibrationBase, eye,
            SIGMA_CAL_NEAR);
        float farPlane = SigmaHistoricalCalibration(calibrationBase, eye,
            SIGMA_CAL_FAR);
        float2 resolution = float2(
            _StreamBundleRayEpoch[bundleSlot * 4u + 2u],
            _StreamBundleRayEpoch[bundleSlot * 4u + 3u]);
        float fx = SigmaHistoricalCalibration(calibrationBase, eye,
            SIGMA_CAL_FX);
        float fy = SigmaHistoricalCalibration(calibrationBase, eye,
            SIGMA_CAL_FY);
        float cx = SigmaHistoricalCalibration(calibrationBase, eye,
            SIGMA_CAL_CX);
        float cy = SigmaHistoricalCalibration(calibrationBase, eye,
            SIGMA_CAL_CY);
        valid = valid && all(resolution > 0.0) && nearPlane > 0.0 &&
            farPlane > nearPlane && positionOptical.z >= nearPlane &&
            positionOptical.z <= farPlane;
        output.positionCS = valid
            ? SigmaHistoricalClip(positionOptical, fx, fy, cx, cy,
                nearPlane, farPlane, resolution)
            : float4(0.0, 0.0, 2.0, 1.0);
        output.positionOptical = positionOptical;
        output.normalOptical = normalOptical;
        output.carrierLocal = float2(cellX + selectedCorner.x,
            cellY + selectedCorner.y);
        output.support = valid ? support : 0.0;
        output.pageCoordinate = uint4(metadata.pageXLo, metadata.pageXHi,
            metadata.pageYLo, metadata.pageYHi);
        output.stateKey = uint4(metadata.generation, metadata.revision,
            _SegmentIndex, pageSlot);
        output.targetLayer = eye;
        return output;
    }

    SigmaHistoricalOutput SigmaHistoricalFragment(
        SigmaHistoricalVaryings input)
    {
        if (input.support <= 0.0)
            discard;
        SigmaHistoricalOutput output;
        output.depthSupport = float2(length(input.positionOptical),
            input.support);
        output.carrierPage = input.pageCoordinate;
        output.carrierUvNormal = float4(input.carrierLocal,
            SigmaEncodeOctahedral(normalize(input.normalOptical)));
        output.stateKey = input.stateKey;
        return output;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "SigmaHistoricalClear"
            Cull Off
            ZWrite On
            ZTest Always
            Blend Off

            HLSLPROGRAM
            #pragma vertex SigmaHistoricalClearVertex
            #pragma fragment SigmaHistoricalClearFragment
            ENDHLSL
        }

        Pass
        {
            Name "SigmaHistoricalRevalidation"
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex SigmaHistoricalVertex
            #pragma fragment SigmaHistoricalFragment
            ENDHLSL
        }
    }
}
