Shader "Genesis/RoomScan/MerkabaGrid"
{
    Properties
    {
        _ScanOpacity("Scan Opacity", Range(0,1)) = 1
        [HideInInspector] _SrcBlend("Source Blend", Float) = 1
        [HideInInspector] _DstBlend("Destination Blend", Float) = 0
        [HideInInspector] _ZWrite("Depth Write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "MerkabaForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "MerkabaCanonicalGeometry.generated.hlsl"

            #define MERKABA_LATTICE_STEP 0.025
            #define MERKABA_KERNELS_PER_CHUNK 32768

            struct KernelState
            {
                int evidence;
                uint packedColor;
                uint colorConfidence;
                uint flags;
            };

            struct PrimitiveRecord
            {
                uint packedLocalPrimitive;
            };

            StructuredBuffer<KernelState> _MerkabaKernels;
            StructuredBuffer<PrimitiveRecord> _MerkabaPrimitiveRecordBanks;
            StructuredBuffer<uint> _MerkabaPublishedBanks;
            uint _MerkabaResidentSlot;
            uint _MerkabaResidentSlotCapacity;
            uint _MerkabaPrimitiveCapacityPerChunk;
            float3 _MerkabaChunkOrigin;
            float4x4 _MerkabaGridToWorld;

            CBUFFER_START(UnityPerMaterial)
                half _ScanOpacity;
            CBUFFER_END

            struct Attributes
            {
                uint vertexID : SV_VertexID;
#if UNITY_ANY_INSTANCING_ENABLED
                UNITY_VERTEX_INPUT_INSTANCE_ID
#else
                uint proceduralInstanceID : SV_InstanceID;
#endif
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 color : TEXCOORD0;
                nointerpolation uint colorConfidence : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
#if UNITY_ANY_INSTANCING_ENABLED
                uint primitiveInstanceID = unity_InstanceID;
#else
                uint primitiveInstanceID = input.proceduralInstanceID;
#endif
                uint bankStride = _MerkabaResidentSlotCapacity *
                    _MerkabaPrimitiveCapacityPerChunk;
                uint recordIndex =
                    (_MerkabaPublishedBanks[_MerkabaResidentSlot] & 1u) * bankStride +
                    _MerkabaResidentSlot *
                    _MerkabaPrimitiveCapacityPerChunk + primitiveInstanceID;
                PrimitiveRecord record = _MerkabaPrimitiveRecordBanks[recordIndex];
                uint localIndex = record.packedLocalPrimitive & 32767u;
                uint primitiveId = (record.packedLocalPrimitive >> 15u) & 31u;
                KernelState state = _MerkabaKernels[
                    _MerkabaResidentSlot * MERKABA_KERNELS_PER_CHUNK + localIndex];
                uint3 localCoord = uint3(localIndex & 31u,
                    (localIndex >> 5u) & 31u, (localIndex >> 10u) & 31u);

                float3 localPosition = MerkabaCanonicalPrimitivePosition(
                    primitiveId, input.vertexID);
                localPosition += (_MerkabaChunkOrigin + (float3)localCoord) *
                    MERKABA_LATTICE_STEP;
                float3 worldPosition = mul(_MerkabaGridToWorld,
                    float4(localPosition, 1)).xyz;
                output.positionCS = TransformWorldToHClip(worldPosition);
                output.color = half3(state.packedColor & 255u,
                    (state.packedColor >> 8) & 255u,
                    (state.packedColor >> 16) & 255u) / 255.0h;
                output.colorConfidence = state.colorConfidence;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (input.colorConfidence > 0u)
                    return half4(input.color, _ScanOpacity);

                return half4(half3(0.55h, 0.16h, 0.42h), _ScanOpacity);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
