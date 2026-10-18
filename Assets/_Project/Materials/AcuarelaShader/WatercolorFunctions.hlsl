#ifndef WATERCOLOR_FUNCTIONS_INCLUDED
#define WATERCOLOR_FUNCTIONS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

float WatercolorHash(float3 p)
{
    return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453);
}

float WatercolorNoise(float3 p)
{
    float3 cell = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = lerp(WatercolorHash(cell), WatercolorHash(cell + float3(1, 0, 0)), f.x);
    float b = lerp(WatercolorHash(cell + float3(0, 1, 0)),
        WatercolorHash(cell + float3(1, 1, 0)), f.x);
    float c = lerp(WatercolorHash(cell + float3(0, 0, 1)),
        WatercolorHash(cell + float3(1, 0, 1)), f.x);
    float d = lerp(WatercolorHash(cell + float3(0, 1, 1)),
        WatercolorHash(cell + float3(1, 1, 1)), f.x);
    return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
}

float WatercolorCells(float3 p)
{
    float3 cell = floor(p);
    float3 f = frac(p);
    float nearest = 3.0;
    [unroll] for (int z = -1; z <= 1; z++)
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float3 offset = float3(x, y, z);
        float3 seed = cell + offset;
        float3 jitter = float3(WatercolorHash(seed),
            WatercolorHash(seed + 17.0), WatercolorHash(seed + 43.0));
        nearest = min(nearest, length(offset + 0.25 + 0.5 * jitter - f));
    }
    return saturate(nearest);
}

void WatercolorShade_float(float3 NormalWS, float4 ScreenPosition, float4 BaseColor,
    float WashScale, float EdgeBleed, float Ambient,
    float3 PositionWS, float ShadowMultiplier, float HighlightMultiplier,
    float ShadowThreshold, float HighlightThreshold,
    float LightStrength, float LightColorInfluence, float PaperStrength,
    UnityTexture2D MainTexture, float2 UV, float TextureStrength,
    out float3 Color)
{
#ifdef SHADERGRAPH_PREVIEW
    float3 lightDirection = normalize(float3(0.4, 0.7, 0.3));
    float3 lightColor = 1.0;
    float shadowAttenuation = 1.0;
    float3 localPosition = PositionWS;
#else
    Light mainLight = GetMainLight(TransformWorldToShadowCoord(
        PositionWS + normalize(NormalWS) * 0.01));
    float3 lightDirection = normalize(mainLight.direction);
    float3 lightColor = mainLight.color;
    float shadowAttenuation = mainLight.shadowAttenuation;
    float3 localPosition = TransformWorldToObject(PositionWS);
#endif

    float3 normal = normalize(NormalWS);
    float facing = saturate(dot(normal, lightDirection));
    float3 incomingLight = lightColor * facing * shadowAttenuation;
#if !defined(SHADERGRAPH_PREVIEW) && defined(_ADDITIONAL_LIGHTS)
    uint count = GetAdditionalLightsCount();
    for (uint i = 0u; i < count; ++i)
    {
        Light additional = GetAdditionalLight(i, PositionWS, half4(1, 1, 1, 1));
        incomingLight += additional.color * saturate(dot(normal, additional.direction)) *
            additional.distanceAttenuation * additional.shadowAttenuation;
    }
#endif
    float luminance = dot(incomingLight, float3(0.2126, 0.7152, 0.0722));
    float light = saturate(saturate(Ambient) + max(0.0, LightStrength) * luminance);

    // Object-space washes stay attached to animated or moving meshes.
    float3 washUV = localPosition * max(WashScale, 0.01);
    float broad = WatercolorNoise(washUV * 0.55);
    float medium = WatercolorNoise(washUV * 1.7 + 13.0);
    float fine = WatercolorNoise(washUV * 5.2 + 29.0);
    float cells = WatercolorCells(washUV * 0.75 + 7.0);
    float bleed = saturate(EdgeBleed);
    float variation = (broad - 0.5) * 0.29 + (medium - 0.5) * 0.12 +
        (cells - 0.5) * 0.1;
    float washLight = saturate(light + variation * bleed);

    float shadowCut = saturate(ShadowThreshold);
    float highlightCut = max(shadowCut + 0.001, saturate(HighlightThreshold));
    float softness = lerp(0.025, 0.12, bleed);
    float3 shadowPigment = BaseColor.rgb * saturate(ShadowMultiplier);
    float3 highlightPigment = BaseColor.rgb * max(1.0, HighlightMultiplier);
    float3 pigment = lerp(shadowPigment, BaseColor.rgb,
        smoothstep(shadowCut - softness, shadowCut + softness, washLight));
    pigment = lerp(pigment, highlightPigment,
        smoothstep(highlightCut - softness, highlightCut + softness, washLight));

    // Pigment settles at wash boundaries and into the grain of the paper.
    float borderDistance = min(abs(washLight - shadowCut),
        abs(washLight - highlightCut));
    float pooledEdge = (1.0 - smoothstep(0.007, 0.055, borderDistance)) * bleed;
    pigment *= 1.0 - pooledEdge * 0.2;
    pigment *= 1.0 + (medium - 0.5) * bleed * 0.2;

    float2 paperPixel = floor(ScreenPosition.xy * 0.5);
    float paperGrain = WatercolorHash(float3(paperPixel, 19.0));
    float granulation = saturate(PaperStrength) *
        (0.08 * (0.5 - fine) + 0.075 * (0.5 - paperGrain));
    pigment *= 1.0 + granulation;
    float dryPaper = saturate(PaperStrength) *
        saturate((fine - 0.62) * 2.0) * 0.18;
    pigment = lerp(pigment, highlightPigment, dryPaper);
    float3 textureColor = SAMPLE_TEXTURE2D(MainTexture.tex,
        MainTexture.samplerstate, UV).rgb;
    pigment *= lerp(float3(1.0, 1.0, 1.0), textureColor,
        saturate(TextureStrength));

#ifdef SHADERGRAPH_PREVIEW
    float3 sceneAmbient = float3(0.55, 0.6, 0.7);
#else
    float3 sceneAmbient = SampleSH(normal);
#endif
    float3 sceneLighting = saturate(sceneAmbient + incomingLight * max(0.0, LightStrength));
    float neutralLighting = dot(sceneLighting, float3(0.2126, 0.7152, 0.0722));
    Color = saturate(pigment * lerp(neutralLighting.xxx, sceneLighting,
        saturate(LightColorInfluence)));
}

void WatercolorShade_half(half3 NormalWS, half4 ScreenPosition, half4 BaseColor,
    half WashScale, half EdgeBleed, half Ambient,
    half3 PositionWS, half ShadowMultiplier, half HighlightMultiplier,
    half ShadowThreshold, half HighlightThreshold,
    half LightStrength, half LightColorInfluence, half PaperStrength,
    UnityTexture2D MainTexture, half2 UV, half TextureStrength,
    out half3 Color)
{
    float3 result;
    WatercolorShade_float(NormalWS, ScreenPosition, BaseColor,
        WashScale, EdgeBleed, Ambient, PositionWS, ShadowMultiplier,
        HighlightMultiplier, ShadowThreshold, HighlightThreshold,
        LightStrength, LightColorInfluence, PaperStrength,
        MainTexture, UV, TextureStrength, result);
    Color = result;
}

void WatercolorOutline_float(float3 PositionOS, float3 NormalOS, float Width,
    out float3 Out)
{
    float wobble = WatercolorNoise(PositionOS * 33.0);
    Out = PositionOS + normalize(NormalOS) * max(Width, 0.0) *
        lerp(0.78, 1.22, wobble);
}

void WatercolorOutline_half(half3 PositionOS, half3 NormalOS, half Width,
    out half3 Out)
{
    float3 result;
    WatercolorOutline_float(PositionOS, NormalOS, Width, result);
    Out = result;
}

#endif
