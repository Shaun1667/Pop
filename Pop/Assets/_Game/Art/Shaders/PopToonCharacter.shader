// Pop 캐릭터용 툰 셰이더 (URP 17, Forward / Forward+ 모두. 렌더러 피처 필요 없음).
// - 부드럽게 만든 2단 램프(밝은 면 / 그늘 면, 경계만 살짝 흐림) + 환경광(SH)
// - 림 라이트(크림색): 어두운 바위 배경에서 실루엣이 떨어져 보이게
// - 화면 고정 두께 아웃라인(SRPDefaultUnlit 패스, 뒷면을 법선 방향으로 키움).
//   얇은 곳(비막 · 모자 챙 · 스카프)에서 아웃라인이 앞으로 뚫고 나오지 않도록 정점을 카메라 반대쪽으로 민다.
// - 그림자는 드리우기만 하고 받지 않는다(얼굴 · 몸에 얼룩지는 것을 피함).
// _BaseColor는 PlayerStunVisual이 MaterialPropertyBlock으로 바꾸는 색 배율이다(기절 0.6 → 1.0, 풀릴 때 흰색 번쩍임).
// 값은 머티리얼에 직접 두지 않고 ToonCharacterSettings 에셋 → Pop/Dev/Squirrel/Apply Toon Material로 넣는다.
Shader "Pop/Toon Character"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Color (stun tint)", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 2)) = 0.4
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2

        [Header(Ramp)]
        _ShadeEdge ("Shade Edge (half-lambert)", Range(0, 1)) = 0.5
        _ShadeSoftness ("Edge Softness", Range(0.005, 0.5)) = 0.08
        _ShadeColor ("Shade Color (multiplier)", Color) = (0.7, 0.64, 0.72, 1)
        _LightStrength ("Main Light Strength", Range(0, 2)) = 1
        _AmbientStrength ("Ambient (SH) Strength", Range(0, 2)) = 0.6

        [Header(Rim)]
        _RimColor ("Rim Color", Color) = (1, 0.92, 0.76, 1)
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.55
        _RimEdge ("Rim Edge (1 - NdotV)", Range(0, 1)) = 0.62
        _RimSoftness ("Rim Softness", Range(0.005, 0.5)) = 0.12
        _RimLitSide ("Rim Light-Side Bias", Range(0, 1)) = 0.35

        [Header(Outline)]
        _OutlineColor ("Outline Color", Color) = (0.16, 0.11, 0.09, 1)
        _OutlineAlbedoMix ("Outline Albedo Mix", Range(0, 1)) = 0.45
        _OutlineAlbedoDarken ("Outline Albedo Darken", Range(0, 1)) = 0.35
        _OutlineWidth ("Outline Width (px at 1080p)", Range(0, 8)) = 1.6
        _OutlineDepthPush ("Outline Push Away From Camera (m)", Range(0, 0.3)) = 0.04
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "UniversalMaterialType" = "Unlit" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            half _Cull;
            half _ShadeEdge;
            half _ShadeSoftness;
            half4 _ShadeColor;
            half _LightStrength;
            half _AmbientStrength;
            half4 _RimColor;
            half _RimStrength;
            half _RimEdge;
            half _RimSoftness;
            half _RimLitSide;
            half4 _OutlineColor;
            half _OutlineAlbedoMix;
            half _OutlineAlbedoDarken;
            float _OutlineWidth;
            float _OutlineDepthPush;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        ENDHLSL

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            // 그림자 키워드는 일부러 받지 않는다(그림자를 받지 않음).

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = normal.normalWS;
                output.tangentWS = half4(normal.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 albedo = tex.rgb * _BaseColor.rgb;

                half3 geomN = isFrontFace ? input.normalWS : -input.normalWS;   // Cull Off일 때 뒷면
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                half3 bitangent = input.tangentWS.w * cross(geomN, input.tangentWS.xyz);
                half3 N = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, geomN)));
                half3 V = GetWorldSpaceNormalizeViewDir(input.positionWS);

                Light mainLight = GetMainLight();   // 그림자 없이
                half halfLambert = dot(N, mainLight.direction) * 0.5h + 0.5h;
                half ramp = smoothstep(_ShadeEdge - _ShadeSoftness, _ShadeEdge + _ShadeSoftness, halfLambert);
                half3 band = lerp(_ShadeColor.rgb, half3(1, 1, 1), ramp);

                half3 ambient = SampleSH(N) * _AmbientStrength;
                half3 color = albedo * (band * mainLight.color * _LightStrength + ambient);

                // 림: 실루엣 가장자리 띠. 빛 쪽에서 조금 더 세게. 기절 때 같이 어두워지도록 _BaseColor를 곱한다.
                half rim = smoothstep(_RimEdge - _RimSoftness, _RimEdge + _RimSoftness, 1.0h - saturate(dot(N, V)));
                rim *= lerp(1.0h, 0.35h + 0.65h * ramp, _RimLitSide);
                color += rim * _RimColor.rgb * _RimStrength * _BaseColor.rgb;

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            // URP Forward · Forward+ 렌더러가 불투명 단계에서 그리는 기본 태그. 렌더러 피처가 필요 없다.
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings OutlineVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                // 카메라 반대쪽으로 민다: 얇은 판(비막 · 챙)의 뒷면 껍데기가 앞면을 뚫고 나오지 않게.
                float3 away = IsPerspectiveProjection()
                    ? normalize(positionWS - GetCameraPositionWS())
                    : -UNITY_MATRIX_V[2].xyz;
                positionWS += away * _OutlineDepthPush;

                float4 positionCS = TransformWorldToHClip(positionWS);

                // 법선을 화면(픽셀) 공간에서 펴서 거리와 상관없이 같은 두께. 1080p 기준 픽셀 수를 해상도에 맞춰 키움.
                float2 normalPx = mul((float3x3)GetWorldToHClipMatrix(), normalWS).xy * _ScreenParams.xy;
                float len = length(normalPx);
                float2 dir = len > 1e-5 ? normalPx / len : float2(0, 0);
                float widthPx = _OutlineWidth * (_ScreenParams.y / 1080.0);
                positionCS.xy += dir * widthPx * (2.0 / _ScreenParams.xy) * positionCS.w;

                output.positionCS = positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positionCS.z);
                return output;
            }

            half4 OutlineFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half3 color = lerp(_OutlineColor.rgb, albedo * _OutlineAlbedoDarken, _OutlineAlbedoMix) * _BaseColor.rgb;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "PopToonDepthPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #include "PopToonDepthPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "PopToonDepthPasses.hlsl"
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
