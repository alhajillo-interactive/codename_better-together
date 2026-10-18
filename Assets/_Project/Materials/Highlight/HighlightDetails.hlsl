#ifndef HIGHLIGHT_DETAILS_INCLUDED
#define HIGHLIGHT_DETAILS_INCLUDED

void HighlightDetails_float(float3 NormalWS, float3 PositionWS, float4 Tint,
    float Opacity, float EdgeStrength, float LineScale, float PulseSpeed,
    out float3 Color, out float Alpha)
{
#ifdef SHADERGRAPH_PREVIEW
    float3 viewDirection = normalize(float3(0.4, 0.5, 1.0));
    float3 localPosition = PositionWS;
#else
    float3 viewDirection = normalize(_WorldSpaceCameraPos - PositionWS);
    float3 localPosition = TransformWorldToObject(PositionWS);
#endif

    float facing = saturate(dot(normalize(NormalWS), viewDirection));
    float rim = pow(1.0 - facing, 2.5);
    float rimBand = smoothstep(0.28, 0.48, rim);
    float celBand = step(0.58, facing);

    // Object-space diagonal lines stay fixed on moving objects.
    float scale = max(LineScale, 0.01);
    float stripePhase = frac((localPosition.y + localPosition.x * 0.32 +
        localPosition.z * 0.18) * scale - _Time.y * max(PulseSpeed, 0.0) * 0.15);
    float stripe = 1.0 - smoothstep(0.04, 0.10, stripePhase);
    float pulse = 0.87 + 0.13 * sin(_Time.y * max(PulseSpeed, 0.0) * 2.0);

    float detail = 0.14 * celBand + 0.22 * stripe;
    Color = Tint.rgb * (0.55 + detail + max(EdgeStrength, 0.0) * rimBand) * pulse;
    Alpha = saturate(Opacity * Tint.a * (0.62 + 0.15 * stripe + 0.45 * rimBand));
}

void HighlightDetails_half(half3 NormalWS, half3 PositionWS, half4 Tint,
    half Opacity, half EdgeStrength, half LineScale, half PulseSpeed,
    out half3 Color, out half Alpha)
{
    float3 resultColor;
    float resultAlpha;
    HighlightDetails_float(NormalWS, PositionWS, Tint, Opacity, EdgeStrength,
        LineScale, PulseSpeed, resultColor, resultAlpha);
    Color = resultColor;
    Alpha = resultAlpha;
}

#endif
