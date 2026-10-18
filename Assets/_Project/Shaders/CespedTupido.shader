Shader "Project/Cesped Tupido"
{
    Properties
    {
        _MainTex ("Silueta blanca", 2D) = "white" {}
        _GroundTex ("Color del terreno", 2D) = "white" {}
        _BottomColor ("Color base", Color) = (0.16, 0.35, 0.12, 1)
        _TopColor ("Color puntas", Color) = (0.58, 0.78, 0.28, 1)
        _FarColor ("Color lejano", Color) = (0.36, 0.57, 0.20, 1)
        _Cutoff ("Recorte alfa", Range(0, 1)) = 0.5
        _GroundBlend ("Mezcla con terreno", Range(0, 1)) = 0
        _NearDistance ("Inicio color lejano", Float) = 10
        _FarDistance ("Fin color lejano", Float) = 25
        _WindStrength ("Fuerza viento", Range(0, 0.25)) = 0.07
        _WindSpeed ("Velocidad viento", Range(0, 8)) = 2
        _WindScale ("Variacion viento", Range(0, 3)) = 0.7
        _WaveScale ("Escala ondas", Range(0.01, 1)) = 0.32
        _WaveSpeed ("Velocidad ondas", Range(0, 4)) = 0.7
        _WaveStrength ("Fuerza ondas", Range(0, 0.25)) = 0.06
        _WaveColor ("Color ondas", Range(0, 0.5)) = 0.16
        _ShadowStrength ("Fuerza sombras", Range(0, 1)) = 0.75
        _LightInfluence ("Influencia luz directa", Range(0, 1)) = 0.55
        _Saturation ("Saturacion", Range(0, 2)) = 1.12
        [HideInInspector] _TerrainOriginSize ("Terreno origen y escala", Vector) = (0, 0, 1, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        Cull Off
        ZWrite On

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_GroundTex);
            SAMPLER(sampler_GroundTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _BottomColor;
                half4 _TopColor;
                half4 _FarColor;
                half _Cutoff;
                half _GroundBlend;
                float _NearDistance;
                float _FarDistance;
                half _WindStrength;
                half _WindSpeed;
                half _WindScale;
                half _WaveScale;
                half _WaveSpeed;
                half _WaveStrength;
                half _WaveColor;
                half _ShadowStrength;
                half _LightInfluence;
                half _Saturation;
                float4 _TerrainOriginSize;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                float3 worldPosition : TEXCOORD2;
                half wave : TEXCOORD3;
                half distanceFade : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;

                float3 worldPosition = TransformObjectToWorld(input.positionOS.xyz);
                float3 rootPosition = TransformObjectToWorld(float3(0, 0, 0));
                float height = saturate(input.uv.y);
                float phase = _Time.y * _WindSpeed + dot(rootPosition.xz, float2(0.73, 0.57)) * _WindScale;
                float2 wavePosition = rootPosition.xz * _WaveScale + _Time.y * _WaveSpeed * float2(1, 0.45);
                float wave = sin(wavePosition.x + sin(wavePosition.y) * 0.9) * 0.5 + 0.5;
                wave = smoothstep(0.4, 0.8, wave);
                float sway = ((sin(phase) + sin(phase * 1.71 + 0.8) * 0.35) * _WindStrength
                    + wave * _WaveStrength) * height * height;
                worldPosition.xz += float2(sway, sway * 0.35);

                output.positionCS = TransformWorldToHClip(worldPosition);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.worldPosition = worldPosition;
                output.wave = wave;
                output.distanceFade = smoothstep(_NearDistance, max(_FarDistance, _NearDistance + 0.01),
                    distance(GetCameraPositionWS(), rootPosition));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 textureColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(textureColor.a - _Cutoff);

                half height = smoothstep(0.05, 0.95, input.uv.y);
                half3 topColor = lerp(_TopColor.rgb, _FarColor.rgb, input.distanceFade);
                half3 gradient = lerp(_BottomColor.rgb, topColor, height);
                float2 groundUV = (input.worldPosition.xz - _TerrainOriginSize.xy) * _TerrainOriginSize.zw;
                half3 groundColor = SAMPLE_TEXTURE2D(_GroundTex, sampler_GroundTex, saturate(groundUV)).rgb;
                gradient = lerp(gradient, groundColor, _GroundBlend * (1 - height) * (1 - height));
                gradient *= 1 + input.wave * _WaveColor * height;

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.worldPosition));
                half3 ambient = SampleSH(half3(0, 1, 0));
                half shadow = lerp(1, mainLight.shadowAttenuation, _ShadowStrength);
                half3 direct = mainLight.color * saturate(mainLight.direction.y) * 0.6 * shadow;
                half3 lighting = saturate(ambient + direct * _LightInfluence);
                half3 color = gradient * textureColor.rgb * lighting;
                half luminance = dot(color, half3(0.2126, 0.7152, 0.0722));
                color = saturate(lerp(half3(luminance, luminance, luminance), color, _Saturation));
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
