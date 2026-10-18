Shader "Project/Watercolor/InkOutline"
{
    Properties
    {
        _WatercolorOutlineColor ("Ink Color", Color) = (0.2, 0.075, 0.08, 1)
        _WatercolorOutlineWidth ("Outline Width", Float) = 0.015
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry+10" }
        Pass
        {
            Name "Ink Silhouette"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            Stencil
            {
                Ref 1
                Comp NotEqual
                Pass Keep
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _WatercolorOutlineColor;
                float _WatercolorOutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 p = input.positionOS.xyz;
                float jitter = sin(dot(p, float3(37.2, 53.7, 29.1))) *
                    sin(dot(p, float3(83.1, 23.4, 61.8)));
                p += normalize(input.normalOS) * max(_WatercolorOutlineWidth, 0.0) *
                    (1.0 + 0.17 * jitter);
                output.positionCS = TransformObjectToHClip(p);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _WatercolorOutlineColor;
            }
            ENDHLSL
        }
    }
}
