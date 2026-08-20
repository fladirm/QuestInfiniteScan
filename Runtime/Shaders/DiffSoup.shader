Shader "Genesis/RoomScan/DiffSoup"
{
    Properties
    {
        [NoScaleOffset] _Lut0 ("DiffSoup feature LUT 0", 2D) = "black" {}
        [NoScaleOffset] _Lut1 ("DiffSoup feature LUT 1", 2D) = "black" {}
        [HideInInspector] _LutSize ("LUT size", Vector) = (1, 1, 0, 0)
        [HideInInspector] _Level ("Subdivision level", Float) = 0
        [HideInInspector] _DepthOnly ("Depth only", Float) = 0
        [HideInInspector] _ColorMask ("Color mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "DisableBatching" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        TEXTURE2D(_Lut0);
        TEXTURE2D(_Lut1);

        CBUFFER_START(UnityPerMaterial)
            float4 _LutSize;
            float _Level;
            float _DepthOnly;
        CBUFFER_END

        // Packed exactly like upstream viewer.cpp::tile_weights. These remain explicit
        // arrays because each DiffSoup chunk owns different immutable MLP constants.
        float4x4 _W1[16];
        float4 _B1[4];
        float4x4 _W2[16];
        float4 _B2[4];
        float4x4 _W3[4];
        float4 _B3;

        float _RSWireframe;
        float _RSWireThickness;

        struct Attributes
        {
            float3 positionOS : POSITION;
            float4 feature : TEXCOORD0; // xyz barycentric, w face ID
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 barycentric : TEXCOORD0;
            nointerpolation float triangleId : TEXCOORD1;
            float3 positionOS : TEXCOORD2;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings DiffSoupVert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS = TransformObjectToHClip(input.positionOS);
            output.barycentric = input.feature.xyz;
            output.triangleId = input.feature.w;
            output.positionOS = input.positionOS;
            return output;
        }

        int LevelSize(int level)
        {
            if (level == 0) return 3;
            int a = (1 << (level - 1)) + 1;
            int b = (1 << level) + 1;
            return a * b;
        }

        int TriangularIndex(int x, int y)
        {
            return (x + y) * (x + y + 1) / 2 + y;
        }

        int2 IndexToCoord(int index, int width)
        {
            return int2(index % width, index / width);
        }

        void SampleDiffSoupFeatures(float triangleId, float3 barycentric,
            out float4 featureA, out float4 featureB)
        {
            int textureWidth = (int)_LutSize.x;
            int level = (int)_Level;
            int samples = LevelSize(level);
            int baseIndex = (int)(triangleId + 0.5) * samples;
            int resolution = 1 << level;
            float b0 = barycentric.x * (float)resolution;
            float b1 = barycentric.y * (float)resolution;
            int x = clamp((int)floor(b0), 0, resolution - 1);
            int y = clamp((int)floor(b1), 0, resolution - 1 - x);
            b0 -= (float)x;
            b1 -= (float)y;
            bool flip = b0 + b1 > 1.0;
            int flipInteger = flip ? 1 : 0;
            float flipFloat = flip ? 1.0 : 0.0;
            int x0 = x + 1;
            int y0 = y;
            int x1 = x;
            int y1 = y + 1;
            int x2 = x + flipInteger;
            int y2 = min(y + flipInteger, resolution - x2);
            int index0 = baseIndex + TriangularIndex(x0, y0);
            int index1 = baseIndex + TriangularIndex(x1, y1);
            int index2 = baseIndex + TriangularIndex(x2, y2);
            float weight0 = lerp(b0, 1.0 - b1, flipFloat);
            float weight1 = lerp(b1, 1.0 - b0, flipFloat);
            float weight2 = 1.0 - weight0 - weight1;
            float4 a0 = LOAD_TEXTURE2D(_Lut0, IndexToCoord(index0, textureWidth));
            float4 a1 = LOAD_TEXTURE2D(_Lut0, IndexToCoord(index1, textureWidth));
            float4 a2 = LOAD_TEXTURE2D(_Lut0, IndexToCoord(index2, textureWidth));
            float4 b0v = LOAD_TEXTURE2D(_Lut1, IndexToCoord(index0, textureWidth));
            float4 b1v = LOAD_TEXTURE2D(_Lut1, IndexToCoord(index1, textureWidth));
            float4 b2v = LOAD_TEXTURE2D(_Lut1, IndexToCoord(index2, textureWidth));
            featureA = a0 * weight0 + a1 * weight1 + a2 * weight2;
            featureB = b0v * weight0 + b1v * weight1 + b2v * weight2;
        }

        void EvaluateSh2(float3 direction, out float sh[9])
        {
            const float c0 = 0.28209479177387814;
            const float c1 = 0.4886025119029199;
            const float c20 = 1.0925484305920792;
            const float c21 = -1.0925484305920792;
            const float c22 = 0.31539156525252005;
            const float c23 = -1.0925484305920792;
            const float c24 = 0.5462742152960396;
            sh[0] = c0;
            sh[1] = -c1 * direction.y;
            sh[2] = c1 * direction.z;
            sh[3] = -c1 * direction.x;
            float xx = direction.x * direction.x;
            float yy = direction.y * direction.y;
            float zz = direction.z * direction.z;
            sh[4] = c20 * direction.x * direction.y;
            sh[5] = c21 * direction.y * direction.z;
            sh[6] = c22 * (2.0 * zz - xx - yy);
            sh[7] = c23 * direction.x * direction.z;
            sh[8] = c24 * (xx - yy);
        }

        float4 Relu4(float4 value) { return max(value, 0.0); }
        float Sigmoid(float value)
        {
            value = clamp(value, -30.0, 30.0);
            return 1.0 / (1.0 + exp(-value));
        }

        float3 EvaluateMlp(float4 featureA, float3 featureB, float3 direction)
        {
            float sh[9];
            EvaluateSh2(direction, sh);
            float4 x0 = featureA;
            float4 x1 = float4(featureB, sh[0]);
            float4 x2 = float4(sh[1], sh[2], sh[3], sh[4]);
            float4 x3 = float4(sh[5], sh[6], sh[7], sh[8]);
            float4 y0 = Relu4(mul(_W1[0], x0) + mul(_W1[1], x1) + mul(_W1[2], x2) + mul(_W1[3], x3) + _B1[0]);
            float4 y1 = Relu4(mul(_W1[4], x0) + mul(_W1[5], x1) + mul(_W1[6], x2) + mul(_W1[7], x3) + _B1[1]);
            float4 y2 = Relu4(mul(_W1[8], x0) + mul(_W1[9], x1) + mul(_W1[10], x2) + mul(_W1[11], x3) + _B1[2]);
            float4 y3 = Relu4(mul(_W1[12], x0) + mul(_W1[13], x1) + mul(_W1[14], x2) + mul(_W1[15], x3) + _B1[3]);
            float4 z0 = Relu4(mul(_W2[0], y0) + mul(_W2[1], y1) + mul(_W2[2], y2) + mul(_W2[3], y3) + _B2[0]);
            float4 z1 = Relu4(mul(_W2[4], y0) + mul(_W2[5], y1) + mul(_W2[6], y2) + mul(_W2[7], y3) + _B2[1]);
            float4 z2 = Relu4(mul(_W2[8], y0) + mul(_W2[9], y1) + mul(_W2[10], y2) + mul(_W2[11], y3) + _B2[2]);
            float4 z3 = Relu4(mul(_W2[12], y0) + mul(_W2[13], y1) + mul(_W2[14], y2) + mul(_W2[15], y3) + _B2[3]);
            float4 output = mul(_W3[0], z0) + mul(_W3[1], z1) +
                            mul(_W3[2], z2) + mul(_W3[3], z3) + _B3;
            return float3(Sigmoid(output.x), Sigmoid(output.y), Sigmoid(output.z));
        }

        half4 DiffSoupFrag(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float4 featureA, featureB;
            SampleDiffSoupFeatures(input.triangleId, input.barycentric,
                featureA, featureB);
            clip(featureB.a - 0.5);
            if (_DepthOnly > 0.5) return 0;

            float3 positionWS = TransformObjectToWorld(input.positionOS);
            float3 directionWS = GetCameraPositionWS() - positionWS;
            float3 directionChunk = TransformWorldToObjectDir(directionWS, true);
            float3 mlp = EvaluateMlp(featureA, featureB.rgb, directionChunk);
            float3 encodedColor = lerp(featureA.rgb, mlp, featureA.a);

            if (_RSWireframe > 0.5)
            {
                float thickness = max(_RSWireThickness, 0.2);
                float3 derivative = max(abs(ddx(input.barycentric)),
                    abs(ddy(input.barycentric)));
                float3 edge = smoothstep(0.0, derivative * thickness,
                    input.barycentric);
                float minimumEdge = min(edge.x, min(edge.y, edge.z));
                clip(saturate(1.0 - thickness * 0.15) - minimumEdge);
                encodedColor = lerp(float3(0.9, 0.9, 0.92), encodedColor,
                    smoothstep(0.35, 0.85, max(input.barycentric.x,
                        max(input.barycentric.y, input.barycentric.z))));
            }

            #if !defined(UNITY_COLORSPACE_GAMMA)
                encodedColor = SRGBToLinear(saturate(encodedColor));
            #endif
            return half4(encodedColor, 1.0);
        }
        ENDHLSL

        Pass
        {
            Name "DiffSoupForward"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask [_ColorMask]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DiffSoupVert
            #pragma fragment DiffSoupFrag
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DiffSoupVert
            #pragma fragment DiffSoupDepthFrag
            #pragma multi_compile_instancing

            half4 DiffSoupDepthFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 featureA, featureB;
                SampleDiffSoupFeatures(input.triangleId, input.barycentric,
                    featureA, featureB);
                clip(featureB.a - 0.5);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
