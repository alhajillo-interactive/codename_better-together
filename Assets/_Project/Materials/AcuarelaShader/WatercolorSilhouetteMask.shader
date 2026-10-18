Shader "Project/Watercolor/SilhouetteMask"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+5" }
        Pass
        {
            Name "Silhouette Stencil"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            ColorMask 0
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
