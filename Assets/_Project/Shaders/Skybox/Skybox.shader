// Stylized procedural skybox (URP, non-physical, artist gradients).
// Based on Kelvin van Hoorn's "Unity skybox shader" tutorial:
//   https://kelvinvanhoorn.com/tutorials/unity_skybox_shader/
// Differences from the tutorial: the star cubemap is replaced by procedural 3D-cell stars
// and the moon texture is replaced by a ray-sphere moon with procedural maria (phases come from the sun direction).
// Stylized cartoon clouds (value-noise coverage mask + inverted animated Worley puffs on a projected cloud plane, crisp edges,
// banded toon shading and a sun-side rim) are layered over the sky and celestial bodies; they fade out at night
// and pick up the sunset tint from the gradients.
// Drive it with the globals _SunDir / _MoonDir (see SkyboxController).
Shader "BetterTogether/Skybox"
{
    Properties
    {
        [Header(Gradients)]
        [NoScaleOffset] _SunZenithGrad ("Sun-Zenith Gradient (base sky)", 2D) = "white" {}
        [NoScaleOffset] _ViewZenithGrad ("View-Zenith Gradient (horizon haze)", 2D) = "black" {}
        [NoScaleOffset] _SunViewGrad ("Sun-View Gradient (sun bloom)", 2D) = "black" {}
        _HorizonPower ("Horizon Power", Range(1, 16)) = 4
        _SunBloomPower ("Sun Bloom Power", Range(1, 16)) = 4

        [Header(Sun)]
        _SunRadius ("Sun Radius", Range(0, 0.3)) = 0.05
        _SunIntensity ("Sun Intensity", Range(0, 10)) = 3

        [Header(Moon)]
        _MoonRadius ("Moon Radius", Range(0, 0.3)) = 0.06
        _MoonColor ("Moon Color", Color) = (0.9, 0.92, 1, 1)
        _MoonExposure ("Moon Exposure", Range(-4, 4)) = 0.5
        _MoonNoiseScale ("Moon Noise Scale", Range(0.5, 12)) = 3
        _MoonNoiseStrength ("Moon Noise Strength", Range(0, 1)) = 0.45

        [Header(Stars)]
        _StarDensity ("Star Density", Range(10, 200)) = 60
        _StarCoverage ("Star Coverage", Range(0, 1)) = 0.25
        _StarSize ("Star Size", Range(0.01, 0.5)) = 0.12
        _StarPower ("Star Power", Range(0.5, 8)) = 2
        _StarExposure ("Star Exposure", Range(-4, 6)) = 1.5
        _StarSpeed ("Star Speed (turns/sec)", Range(-0.05, 0.05)) = 0.002
        _StarLatitude ("Star Latitude (deg)", Range(-90, 90)) = 40
        _TwinkleSpeed ("Twinkle Speed", Range(0, 10)) = 2
        _StarFadeSharpness ("Star Fade Sharpness", Range(0.5, 16)) = 4

        [Header(Clouds)]
        _CloudCoverage ("Cloud Coverage", Range(0, 1)) = 0.45
        _CloudEdgeSoftness ("Cloud Edge Softness", Range(0, 1)) = 0.15
        _CloudScale ("Cloud Scale", Range(0.1, 5)) = 0.6
        _CloudPuffScale ("Cloud Puff Scale", Range(1, 20)) = 6
        _CloudPuffAmount ("Cloud Puff Amount", Range(0, 1)) = 0.45
        _CloudBoilSpeed ("Cloud Boil Speed", Range(0, 2)) = 0.15
        _CloudWind ("Cloud Wind (xy)", Vector) = (0.01, 0.004, 0, 0)
        _CloudCurvature ("Cloud Curvature", Range(0, 1)) = 0.15
        _CloudColor ("Cloud Color", Color) = (1, 1, 1, 1)
        _CloudShadowColor ("Cloud Shadow Color", Color) = (0.72, 0.76, 0.9, 1)
        _CloudBands ("Cloud Shading Bands", Range(1, 4)) = 2
        _CloudRimColor ("Cloud Rim Color", Color) = (1, 0.98, 0.9, 1)
        _CloudRimWidth ("Cloud Rim Width", Range(0, 0.3)) = 0.08
        _CloudOpacity ("Cloud Opacity", Range(0, 1)) = 1
        _CloudHorizonFade ("Cloud Horizon Fade", Range(0.01, 0.5)) = 0.15
        _CloudDayFade ("Cloud Day Fade", Range(0.5, 10)) = 3

        [Header(Ground)]
        _GroundColor ("Ground Color", Color) = (0.35, 0.4, 0.45, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            Name "Skybox"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SunZenithGrad);  SAMPLER(sampler_SunZenithGrad);
            TEXTURE2D(_ViewZenithGrad); SAMPLER(sampler_ViewZenithGrad);
            TEXTURE2D(_SunViewGrad);    SAMPLER(sampler_SunViewGrad);

            CBUFFER_START(UnityPerMaterial)
                float _HorizonPower;
                float _SunBloomPower;
                float _SunRadius;
                float _SunIntensity;
                float _MoonRadius;
                float4 _MoonColor;
                float _MoonExposure;
                float _MoonNoiseScale;
                float _MoonNoiseStrength;
                float _StarDensity;
                float _StarCoverage;
                float _StarSize;
                float _StarPower;
                float _StarExposure;
                float _StarSpeed;
                float _StarLatitude;
                float _TwinkleSpeed;
                float _StarFadeSharpness;
                float _CloudCoverage;
                float _CloudEdgeSoftness;
                float _CloudScale;
                float _CloudPuffScale;
                float _CloudPuffAmount;
                float _CloudBoilSpeed;
                float4 _CloudWind;
                float _CloudCurvature;
                float4 _CloudColor;
                float4 _CloudShadowColor;
                float _CloudBands;
                float4 _CloudRimColor;
                float _CloudRimWidth;
                float _CloudOpacity;
                float _CloudHorizonFade;
                float _CloudDayFade;
                float4 _GroundColor;
            CBUFFER_END

            float4 _SunDir;
            float4 _MoonDir;

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 viewDirWS  : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.viewDirWS = TransformObjectToWorld(input.positionOS);
                return output;
            }

            // ---------- Helpers ----------

            // Samples a 256x1 gradient; u is clamped to texel centers.
            float3 SampleGradient(TEXTURE2D_PARAM(tex, samp), float u)
            {
                float uc = lerp(0.5 / 256.0, 1.0 - 0.5 / 256.0, saturate(u));
                return SAMPLE_TEXTURE2D_LOD(tex, samp, float2(uc, 0.5), 0).rgb;
            }

            float SkyHash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float3 SkyHash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            float ValueNoise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = SkyHash13(i);
                float n100 = SkyHash13(i + float3(1, 0, 0));
                float n010 = SkyHash13(i + float3(0, 1, 0));
                float n110 = SkyHash13(i + float3(1, 1, 0));
                float n001 = SkyHash13(i + float3(0, 0, 1));
                float n101 = SkyHash13(i + float3(1, 0, 1));
                float n011 = SkyHash13(i + float3(0, 1, 1));
                float n111 = SkyHash13(i + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
            }

            float CloudHash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ValueNoise2D(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = CloudHash21(i);
                float b = CloudHash21(i + float2(1, 0));
                float c = CloudHash21(i + float2(0, 1));
                float d = CloudHash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float2 CloudCellHash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            // Worley F1 distance (3x3 neighbourhood); feature points orbit inside their cell over time ("boiling").
            float CloudWorley(float2 p, float t)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float d = 8.0;
                [unroll] for (int y = -1; y <= 1; y++)
                {
                    [unroll] for (int x = -1; x <= 1; x++)
                    {
                        float2 g = float2(x, y);
                        float2 h = CloudCellHash22(i + g);
                        float2 o = 0.5 + 0.5 * sin(t + TWO_PI * h);
                        float2 r = g + o - f;
                        d = min(d, dot(r, r));
                    }
                }
                return sqrt(d);
            }

            // Cartoon cloud density field in ~[0,1]: low-frequency coverage mask blended with round Worley bubbles (2 octaves, 18 cells).
            float CloudShape(float2 uv, float t)
            {
                float mask = ValueNoise2D(uv) * 0.65 + ValueNoise2D(uv * 2.1 + 5.3) * 0.35;
                mask = saturate((mask - 0.5) * 1.6 + 0.5);

                float2 pp = uv * _CloudPuffScale;
                float puff = 0.7 * (1.0 - saturate(CloudWorley(pp, t)))
                           + 0.3 * (1.0 - saturate(CloudWorley(pp * 2.3 + 3.7, t * 1.3 + 1.9)));
                return lerp(mask, puff, _CloudPuffAmount);
            }

            // Rotation of 'angle' radians around a normalized axis (Rodrigues).
            float3x3 AngleAxis3x3(float angle, float3 axis)
            {
                float s, c;
                sincos(angle, s, c);
                float t = 1.0 - c;
                float x = axis.x, y = axis.y, z = axis.z;
                return float3x3(
                    t * x * x + c,     t * x * y - s * z, t * x * z + s * y,
                    t * x * y + s * z, t * y * y + c,     t * y * z - s * x,
                    t * x * z - s * y, t * y * z + s * x, t * z * z + c);
            }

            // Ray (origin 0, direction rd) vs sphere (center, radius). Returns hit distance, or -1 on miss.
            float RaySphere(float3 rd, float3 center, float radius)
            {
                float3 oc = -center;
                float b = dot(oc, rd);
                float h = b * b - (dot(oc, oc) - radius * radius);
                if (h < 0.0) return -1.0;
                return -b - sqrt(h);
            }

            // Returns lit moon color in rgb and coverage in a.
            float4 Moon(float3 view, float3 sunDir, float3 moonDir)
            {
                float t = RaySphere(view, moonDir, _MoonRadius);
                if (t <= 0.0) return float4(0, 0, 0, 0);

                float3 normal = normalize(view * t - moonDir);
                float lit = saturate(dot(normal, sunDir));

                float3 np = normal * _MoonNoiseScale;
                float maria = ValueNoise3D(np) * 0.65 + ValueNoise3D(np * 2.3 + 7.1) * 0.35;
                maria = smoothstep(0.4, 0.65, maria);
                float albedo = 1.0 - maria * _MoonNoiseStrength;

                float3 col = _MoonColor.rgb * albedo * lit * exp2(_MoonExposure);
                return float4(col, 1.0);
            }

            float3 Stars(float3 view, float time)
            {
                float3x3 tilt = AngleAxis3x3(radians(_StarLatitude), float3(1, 0, 0));
                float3x3 spin = AngleAxis3x3(time * _StarSpeed * TWO_PI, float3(0, 1, 0));
                float3 dir = mul(spin, mul(tilt, view));

                float3 p = dir * _StarDensity;
                float3 cell = floor(p);
                float3 local = frac(p);

                float3 rnd = SkyHash33(cell);
                float3 starPos = 0.2 + 0.6 * rnd;
                float present = step(SkyHash13(cell + 17.3), _StarCoverage);

                float d = length(local - starPos);
                float star = 1.0 - smoothstep(0.0, _StarSize, d);
                float twinkle = 0.75 + 0.25 * sin(time * _TwinkleSpeed + rnd.x * TWO_PI);

                float brightness = pow(saturate(star), _StarPower) * twinkle * exp2(_StarExposure);
                return present * brightness * lerp(float3(1.0, 0.9, 0.8), float3(0.8, 0.9, 1.0), rnd.y);
            }

            // ---------- Fragment ----------

            float4 Frag(Varyings input) : SV_Target
            {
                float3 view = normalize(input.viewDirWS);
                float3 sunDir = normalize(_SunDir.xyz);
                float3 moonDir = normalize(_MoonDir.xyz);

                float sunViewDot = dot(sunDir, view);
                float sunZenithDot = sunDir.y;
                float viewZenithDot = view.y;
                float sunZenithDot01 = (sunZenithDot + 1.0) * 0.5;
                float sunViewDot01 = (sunViewDot + 1.0) * 0.5;

                // Base sky, horizon haze, sun bloom
                float3 skyBase = SampleGradient(TEXTURE2D_ARGS(_SunZenithGrad, sampler_SunZenithGrad), sunZenithDot01);
                float3 haze = SampleGradient(TEXTURE2D_ARGS(_ViewZenithGrad, sampler_ViewZenithGrad), sunZenithDot01);
                float hazeMask = pow(saturate(1.0 - viewZenithDot), _HorizonPower);
                float3 bloom = SampleGradient(TEXTURE2D_ARGS(_SunViewGrad, sampler_SunViewGrad), sunZenithDot01);
                float bloomMask = pow(saturate(sunViewDot), _SunBloomPower);

                float3 sky = skyBase + haze * hazeMask + bloom * bloomMask;

                // Moon (occludes sun disc and stars)
                float4 moon = Moon(view, sunDir, moonDir);

                // Sun disc
                float sunEdge = 1.0 - _SunRadius * _SunRadius;
                float aa = max(fwidth(sunViewDot), 1e-5);
                float sunDisc = smoothstep(sunEdge - aa, sunEdge + aa, sunViewDot) * step(0.0, sunViewDot);
                float3 sun = sunDisc * _MainLightColor.rgb * _SunIntensity;

                // Stars
                float starStrength = (1.0 - sunViewDot01) * saturate(-sunZenithDot * _StarFadeSharpness);
                float3 stars = Stars(view, _Time.y) * starStrength;

                float3 celestial = (sun + stars) * (1.0 - moon.a) + moon.rgb;

                // Clouds: view direction projected onto a flat plane; cartoon shape field (mask + Worley puffs), crisp alpha edge,
                // toon shading from a second sample offset toward the sun, thin rim on the sun-facing edge.
                float2 cloudUV = view.xz / max(viewZenithDot + _CloudCurvature, 0.05);
                cloudUV = cloudUV * _CloudScale + _Time.y * _CloudWind.xy;
                float boilT = _Time.y * _CloudBoilSpeed;
                float2 sunOffset = sunDir.xz * (0.6 / max(_CloudPuffScale, 1.0));

                float cloudShape = CloudShape(cloudUV, boilT);
                float cloudShapeSun = CloudShape(cloudUV + sunOffset, boilT);

                float cloudThr = 1.0 - _CloudCoverage;
                float cloudAA = max(fwidth(cloudShape), 1e-4) * (1.0 + _CloudEdgeSoftness * 8.0) + _CloudEdgeSoftness * 0.04;
                float cloudDensity = smoothstep(cloudThr - cloudAA, cloudThr + cloudAA, cloudShape);

                // Light: positive when the sun-ward neighbour is thinner (sun-facing side). Quantized into flat bands with crisp steps.
                float cloudSunDelta = cloudShape - cloudShapeSun;
                float cloudLitAmt = saturate(0.55 + cloudSunDelta * 5.0);
                float cloudSteps = max(round(_CloudBands), 1.0);
                float cloudBandT = cloudLitAmt * cloudSteps + 0.5;
                float cloudBandW = clamp(fwidth(cloudBandT), 1e-4, 0.5);
                float cloudBandF = floor(cloudBandT);
                float cloudLitQ = saturate((cloudBandF + smoothstep(1.0 - cloudBandW, 1.0, cloudBandT - cloudBandF)) / cloudSteps);
                float cloudShade = 1.0 - cloudLitQ;

                // Rim: thin band just inside the alpha edge, only where the sun-ward side is outside the cloud
                float cloudRimBand = 1.0 - smoothstep(cloudThr + _CloudRimWidth - cloudAA, cloudThr + _CloudRimWidth + cloudAA, cloudShape);
                float cloudRim = cloudRimBand * smoothstep(0.0, 0.05, cloudSunDelta) * cloudDensity;

                // Warm sunset tint from the haze/bloom gradients (hue only, strongest when the sun is near the horizon)
                float3 warmSrc = haze + bloom;
                float warmMax = max(max(warmSrc.r, warmSrc.g), max(warmSrc.b, 1e-3));
                float warmAmt = saturate(1.0 - abs(sunZenithDot) * 3.0) * saturate(warmMax * 20.0);
                float3 warmTint = lerp(float3(1, 1, 1), warmSrc / warmMax, warmAmt);

                float sunGlow = pow(saturate(sunViewDot), 8.0);
                float3 cloudLit = _CloudColor.rgb * warmTint * (1.0 + 0.4 * sunGlow);
                float3 cloudShadowCol = _CloudShadowColor.rgb * lerp(float3(1, 1, 1), warmTint, 0.5) * (1.0 + 0.2 * sunGlow);
                float3 cloudRimCol = _CloudRimColor.rgb * warmTint * (1.0 + 0.4 * sunGlow);
                float3 cloudCol = lerp(cloudLit, cloudShadowCol, cloudShade);
                cloudCol = lerp(cloudCol, cloudRimCol, cloudRim);
                // Slightly darker toward the lower part of the sky
                cloudCol *= lerp(0.85, 1.0, smoothstep(0.0, 0.5, viewZenithDot));

                float dayFactor = saturate(sunZenithDot * _CloudDayFade + 0.2);
                float cloudAlpha = cloudDensity * _CloudOpacity * dayFactor * smoothstep(0.0, _CloudHorizonFade, viewZenithDot);

                float3 skyWithClouds = sky + celestial * (1.0 - cloudAlpha);
                skyWithClouds = lerp(skyWithClouds, cloudCol, cloudAlpha);

                // Ground
                float groundT = smoothstep(0.0, -0.05, viewZenithDot);
                float3 ground = _GroundColor.rgb * skyBase;

                float3 color = lerp(skyWithClouds, ground, groundT);

                // Dithering to reduce banding
                color += (InterleavedGradientNoise(input.positionCS.xy, 0) - 0.5) / 255.0;

                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
