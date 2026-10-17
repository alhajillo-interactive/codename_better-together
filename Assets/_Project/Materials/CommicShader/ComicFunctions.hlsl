#ifndef COMIC_FUNCTIONS_INCLUDED
#define COMIC_FUNCTIONS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// A separate print screen for each color keeps overlapping dots visible.
float ComicPrintDots(float2 screenUV, float coverage, float2 screenDirection)
{
    float2 rotatedUV = float2(
        dot(screenUV, screenDirection),
        dot(screenUV, float2(-screenDirection.y, screenDirection.x)));
    float dotDistance = length(frac(rotatedUV) - 0.5);
    float radius = 0.75 * saturate(coverage);
    float edge = max(fwidth(dotDistance), 0.006);
    return step(0.0001, coverage) *
        (1.0 - smoothstep(radius - edge, radius + edge, dotDistance));
}

void ComicShade_float(float3 NormalWS, float4 ScreenPosition, float4 BaseColor,
    float4 InkColor, float DotSize, float ShadowStrength, float Ambient,
    float3 PositionWS, float4 ShadowColor, float4 MidColor, float4 HighlightColor,
    float ShadowThreshold, float MidThreshold, float HighlightThreshold,
    float LightStrength, float LightColorInfluence, float ShadowBlend,
    out float3 Color)
{
#ifdef SHADERGRAPH_PREVIEW
    float3 lightDirection = normalize(float3(0.4, 0.7, 0.3));
    float3 lightColor = 1.0;
    float shadowAttenuation = 1.0;
#else
    Light mainLight = GetMainLight(TransformWorldToShadowCoord(PositionWS + normalize(NormalWS) * 0.01));
    float3 lightDirection = normalize(mainLight.direction);
    float3 lightColor = mainLight.color;
    float shadowAttenuation = mainLight.shadowAttenuation;
#endif

    float3 normal = normalize(NormalWS);
    float directLight = saturate(dot(normal, lightDirection)) * shadowAttenuation;
    float3 incomingLight = lightColor * directLight;

#if !defined(SHADERGRAPH_PREVIEW) && defined(_ADDITIONAL_LIGHTS)
    uint additionalLightCount = GetAdditionalLightsCount();
    for (uint lightIndex = 0u; lightIndex < additionalLightCount; ++lightIndex)
    {
        Light additionalLight = GetAdditionalLight(lightIndex, PositionWS, half4(1, 1, 1, 1));
        float facing = saturate(dot(normal, additionalLight.direction));
        incomingLight += additionalLight.color * facing *
            additionalLight.distanceAttenuation * additionalLight.shadowAttenuation;
    }
#endif

    float lightLuminance = dot(incomingLight, float3(0.2126, 0.7152, 0.0722));
    float illumination = saturate(saturate(Ambient) +
        max(LightStrength, 0.0) * lightLuminance);

    float shadowCut = saturate(ShadowThreshold);
    float midCut = max(shadowCut + 0.001, saturate(MidThreshold));
    float highlightCut = max(midCut + 0.001, saturate(HighlightThreshold));
    float transitionWidth = clamp(0.55 * max(ShadowStrength, 0.0), 0.08, 0.5);
    float2 screenUV = ScreenPosition.xy / max(DotSize, 1.0);

    // Each threshold grows its own colored dots as the surface gets darker.
    // The angled screens can occupy the same pixel, like layered print ink.
    float baseCoverage = saturate((highlightCut - illumination) / transitionWidth + 0.5);
    float midCoverage = saturate((midCut - illumination) / transitionWidth + 0.5);
    float shadowCoverage = min(
        saturate((shadowCut - illumination) / transitionWidth + 0.5),
        1.0 - 0.4 * saturate(ShadowBlend));
    float baseDots = ComicPrintDots(screenUV, baseCoverage, float2(0.965926, 0.258819));
    float midDots = ComicPrintDots(screenUV, midCoverage, float2(0.866025, -0.5));
    float shadowDots = ComicPrintDots(screenUV, shadowCoverage, float2(0.707107, 0.707107));

    float3 printedColor = HighlightColor.rgb;
    printedColor = lerp(printedColor, BaseColor.rgb, baseDots);
    printedColor = lerp(printedColor, MidColor.rgb, midDots);
    float3 shadowInk = lerp(ShadowColor.rgb, InkColor.rgb, 0.15);
    printedColor = lerp(printedColor, shadowInk, shadowDots);

    // A little of the colored print remains visible even inside deep shadow.
    float deepShadow = 1.0 - smoothstep(0.0, 0.08, illumination);
    float3 shadowPaper = lerp(BaseColor.rgb, MidColor.rgb, saturate(ShadowBlend));
    float coloredGap = deepShadow * saturate(ShadowBlend) * (1.0 - shadowDots) * 0.3;
    printedColor = lerp(printedColor, shadowPaper, coloredGap);

    float3 lightTint = incomingLight / max(lightLuminance, 0.001);
    Color = printedColor * lerp(float3(1.0, 1.0, 1.0),
        min(lightTint, float3(2.0, 2.0, 2.0)),
        saturate(LightColorInfluence) * (1.0 - shadowDots));
}

void ComicShade_half(half3 NormalWS, half4 ScreenPosition, half4 BaseColor,
    half4 InkColor, half DotSize, half ShadowStrength, half Ambient,
    half3 PositionWS, half4 ShadowColor, half4 MidColor, half4 HighlightColor,
    half ShadowThreshold, half MidThreshold, half HighlightThreshold,
    half LightStrength, half LightColorInfluence, half ShadowBlend,
    out half3 Color)
{
    float3 result;
    ComicShade_float(NormalWS, ScreenPosition, BaseColor, InkColor,
        DotSize, ShadowStrength, Ambient, PositionWS, ShadowColor, MidColor,
        HighlightColor, ShadowThreshold, MidThreshold, HighlightThreshold,
        LightStrength, LightColorInfluence, ShadowBlend, result);
    Color = result;
}

void ComicOutline_float(float3 PositionOS, float3 NormalOS, float Width,
    out float3 Out)
{
    Out = PositionOS + normalize(NormalOS) * max(Width, 0.0);
}

void ComicOutline_half(half3 PositionOS, half3 NormalOS, half Width,
    out half3 Out)
{
    float3 result;
    ComicOutline_float(PositionOS, NormalOS, Width, result);
    Out = result;
}

#endif
