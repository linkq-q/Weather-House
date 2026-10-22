Shader "Weather House/Painterly Water"
{
    Properties
    {
        [Header(Pond Palette)]
        _ShallowColor ("Shallow bank color", Color) = (0.66, 0.79, 0.75, 1)
        _DeepColor ("Deep turquoise color", Color) = (0.55, 0.75, 0.74, 1)
        _DepthDistance ("Depth color distance (m)", Range(0.1, 8)) = 1.2
        _Opacity ("Water opacity", Range(0, 1)) = 0.70
        _RefractionStrength ("Refraction strength", Range(0, 0.05)) = 0.012
        [Header(Reflections)]
        _ReflectionTint ("Reflection tint", Color) = (0.75, 0.86, 0.82, 1)
        _ReflectionNear ("Near / looking-down reflection", Range(0, 1)) = 0.65
        _ReflectionFar ("Far / grazing reflection", Range(0, 1)) = 1
        _ReflectionFalloff ("Reflection view transition", Range(0.2, 5)) = 2
        _ReflectionTintStrength ("Reflection pigment tint", Range(0, 1)) = 0.06
        _WaterSaturation ("Water pigment saturation", Range(0, 2)) = 1
        _ReflectionStrength ("Reflection strength", Range(0, 1)) = 0.86
        _ReflectionDistortion ("Reflection breakup", Range(0, 0.06)) = .005
        _ReflectionBrushStrength ("Reflection brush strength", Range(0, 1)) = 0.78
        _ReflectionBrushSize ("Reflection brush length (m)", Range(0.1, 3)) = 1.35
        _ReflectionColorSteps ("Reflection lightness steps", Range(3, 24)) = 8
        _ReflectionPosterize ("Reflection color simplification", Range(0, 1)) = 0.48
        [HideInInspector] _PlanarReflectionTex ("Planar reflection", 2D) = "black" {}
        [HideInInspector] _HasPlanarReflection ("Has planar reflection", Float) = 0
        [HideInInspector] _HasSceneColor ("Has current camera opaque color", Float) = 0
        [Header(Painted Ripples)]
        _WaterBrushAtlas ("Four water paint stamps (2x2)", 2D) = "black" {}
        [ToggleUI] _UseWaterBrushAtlas ("Use water brush stamp atlas", Float) = 0
        _RippleColor ("Cream brush stroke color", Color) = (0.96, 0.94, 0.82, 1)
        _RippleStrength ("Ripple strength", Range(0, 1)) = .36
        _RippleSpeed ("Ripple speed", Range(0, 1)) = 0.12
        _StrokeScale ("Brush stroke frequency", Range(0.1, 4)) = 0.8
        _BrushDirection ("Brush direction XZ", Vector) = (0.819, 0.574, 0, 0)
        [ToggleUI] _ViewAlignedStrokes ("Keep strokes horizontal in view", Float) = 0
        _SurfaceStrokeStrength ("Surface stroke strength", Range(0, 1)) = .84
        _SurfaceStrokeDensity ("Surface stroke density", Range(0, 1)) = .54
        _BrushViewResponse ("Brush view response", Range(0, 1)) = 0.85
        _SmallStrokeScale ("Small brush frequency multiplier", Range(1.5, 5)) = 3.8
        _SmallStrokeWeight ("Small brush layer weight", Range(0, 1)) = 0.75
        _BrushGradientStrength ("Along-brush pigment gradient", Range(0, 1)) = 0.65
        _ShadowPigmentColor ("Saturated shadow pigment", Color) = (0.035, 0.40, 0.43, 1)
        _ShadowPigmentBlend ("Shadow pigment blend", Range(0, 1)) = 0.75
        [HideInInspector] _InteractionNormalTex ("Character water normal field", 2D) = "bump" {}
        [HideInInspector] _InteractionWorldRect ("Interaction world rectangle", Vector) = (0, 0, 0, 0)
        [HideInInspector] _InteractionStrength ("Character water normal strength", Float) = 0
        [Header(Shoreline)]
        _ShoreWidth ("Shoreline depth width (m)", Range(0.01, 1)) = 0.20
        _ShoreStrength ("Shoreline stroke strength", Range(0, 1)) = 0.68
        _FoamColor ("Foam pigment", Color) = (0.93, 0.94, 0.82, 1)
        _FoamSpeed ("Foam flow speed", Range(0, 2)) = 0
        _FoamTravel ("Foam advance and retreat", Range(0, 1)) = 0
        _FoamSoftness ("Foam painted edge softness", Range(0.01, 0.5)) = 0.16
        _WaveletStrength ("Short S wave paint", Range(0, 1)) = 0
        _WaveletScale ("Short wave frequency", Range(0.1, 3)) = 0.65
        _WaveletWidth ("Short wave paint width", Range(0.01, 0.3)) = 0.075
        _WaveletLength ("Short wave paint length", Range(0.3, 1.5)) = 1.25
        _WaveletSpeed ("Short wave speed", Range(0, 2)) = 0.35
        [Header(Underwater Caustics)]
        _CausticStrength ("Caustic strength", Range(0, 2)) = 0.22
        _CausticScale ("Caustic cells per meter", Range(0.2, 5)) = 1.25
        _CausticSpeed ("Caustic speed", Range(0, 2)) = 0.28
        _CausticDepth ("Caustic maximum depth (m)", Range(0.1, 8)) = 1.1
        [Enum(Off,0,WaterDepth,1,Caustics,2,RefractionRejection,3)] _DebugDepth ("Debug view", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "PainterlyWater"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _ReflectionNear, _ReflectionFar, _ReflectionFalloff, _ReflectionTintStrength, _WaterSaturation;
                half4 _FoamColor;
                float _FoamSpeed, _FoamTravel, _FoamSoftness, _WaveletStrength, _WaveletScale, _WaveletSpeed;
                float _WaveletWidth, _WaveletLength;
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _ReflectionTint;
                half4 _RippleColor;
                float _DepthDistance;
                float _Opacity;
                float _RefractionStrength;
                float _ReflectionStrength;
                float _ReflectionDistortion;
                float _ReflectionBrushStrength;
                float _ReflectionBrushSize;
                float _ReflectionColorSteps;
                float _ReflectionPosterize;
                float _RippleStrength;
                float _RippleSpeed;
                float _StrokeScale;
                float _ShoreWidth;
                float _ShoreStrength;
                float _SurfaceStrokeStrength;
                float _SurfaceStrokeDensity;
                float _ViewAlignedStrokes;
                float _CausticStrength;
                float _CausticScale;
                float _CausticSpeed;
                float _CausticDepth;
                float _HasPlanarReflection;
                float _HasSceneColor;
                float4 _BrushDirection;
                float _DebugDepth;
                float _UseWaterBrushAtlas;
                float _BrushViewResponse;
                float _SmallStrokeScale;
                float _SmallStrokeWeight;
                float _BrushGradientStrength;
                half4 _ShadowPigmentColor;
                float _ShadowPigmentBlend;
                float _InteractionStrength;
                float4 _InteractionWorldRect;
                float4 _WaterBrushAtlas_TexelSize;
            CBUFFER_END

            TEXTURE2D(_InteractionNormalTex);
            SAMPLER(sampler_InteractionNormalTex);
            TEXTURE2D(_WaterBrushAtlas);
            SAMPLER(sampler_WaterBrushAtlas);
            TEXTURE2D(_PlanarReflectionTex);
            SAMPLER(sampler_PlanarReflectionTex);
            float4x4 _PlanarReflectionVP;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(0.1031, 0.11369));
                p += dot(p, p.yx + 19.19);
                return frac(p.x * p.y * (p.x + p.y));
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(cell), Hash21(cell + float2(1, 0)), f.x),
                            lerp(Hash21(cell + float2(0, 1)), Hash21(cell + 1), f.x), f.y);
            }

            float2 BrushDirection()
            {
                float2 direction = normalize(_BrushDirection.xy + float2(0.0001, 0));
                float2 viewRight = mul((float3x3)UNITY_MATRIX_I_V, float3(1, 0, 0)).xz;
                if (_ViewAlignedStrokes > 0.5 && dot(viewRight, viewRight) > 0.0001)
                    direction = normalize(viewRight);
                return direction;
            }

            // Two gentle moving scales deform reflected forms without covering them in surface noise.
            float2 PaintedRipples(float2 q, float time)
            {
                float warp = ValueNoise(q * 0.38 + float2(time * 0.12, -time * 0.18)) - 0.5;
                float broad = ValueNoise(q * float2(0.62, 1.35) + float2(time * 0.22, -time * 0.45) + warp * 0.35);
                float2 detailQ = float2(q.x * 0.86 + q.y * 0.30, q.y * 3.1 - q.x * 0.25);
                float detail = ValueNoise(detailQ + float2(-time * 0.31, time * 0.32) + warp * 0.35);
                // Slow, broad displacement preserves the reflected silhouette instead of combing it into lines.
                broad = lerp(broad, smoothstep(0.22, 0.78, broad), 0.20);
                return float2(broad * 0.94 + detail * 0.06, lerp(broad, detail, 0.18)) * 2.0 - 1.0;
            }

            float3 ScenePosition(float2 uv)
            {
                float deviceDepth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, deviceDepth);
                #endif
                return ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
            }

            float2 ReflectionUV(float3 positionWS)
            {
                float4 projected = mul(_PlanarReflectionVP, float4(positionWS, 1));
                return projected.xy / max(projected.w, 0.0001);
            }

            // A finite horizontal daub. Variation changes its contour, never the whole water color.
            float BrushFootprint(float2 local, float seed)
            {
                float halfLength = lerp(0.36, 0.49, Hash21(float2(seed, 3.7)));
                float centerX = 0.5 + (seed - 0.5) * 0.04;
                float x = abs(local.x - centerX) / halfLength;
                float edgeShape = ValueNoise(float2(local.x * 5.0, seed * 23.0));
                float centerY = 0.49 + (seed - 0.5) * 0.12 + (edgeShape - 0.5) * 0.09;
                float width = lerp(0.20, 0.34, Hash21(float2(seed, 17.9)));
                width *= (1.0 - 0.34 * saturate(x * x)) * lerp(0.76, 1.12, edgeShape);
                float2 aa = max(fwidth(local), float2(0.002, 0.002));
                float ends = 1.0 - smoothstep(0.85, 1.0 + aa.x, x);
                float sides = 1.0 - smoothstep(width, width + aa.y, abs(local.y - centerY));
                // A few broad bristle gaps occur only near the edge of the paint impression.
                float dryEdge = ValueNoise(float2(local.x * 7.0, local.y * 11.0) + seed * 19.0);
                float edgeOnly = smoothstep(width * 0.55, width, abs(local.y - centerY));
                return ends * sides * (1.0 - edgeOnly * smoothstep(0.58, 0.78, dryEdge) * 0.65);
            }

            float WaterStampInk(float2 uv, float tile, float lod)
            {
                // Mip-aware padding keeps every filtered tap inside its own atlas quadrant.
                float2 inset = (exp2(ceil(lod)) + 1.0) * _WaterBrushAtlas_TexelSize.xy;
                float2 origin = float2(fmod(tile, 2.0), floor(tile * 0.5)) * 0.5;
                float2 atlasUV = origin + clamp(uv, inset, 1.0 - inset) * 0.5;
                float ink = SAMPLE_TEXTURE2D_LOD(_WaterBrushAtlas, sampler_WaterBrushAtlas, atlasUV, lod).r;
                float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                return saturate((ink - 0.02) / 0.98) * inside;
            }

            float ReflectionImpression(float2 worldBrush, float2 patchSize, float2 offset,
                                       out float2 anchorDelta, out float pigmentSeed, out float2 pigmentCoordinate)
            {
                float2 q = worldBrush / patchSize + offset;
                float2 cell = floor(q), local = frac(q);
                float2 dpdx = ddx(q), dpdy = ddy(q);
                float nearest = 100.0;
                float2 chosenUV = 0.5, chosenAlong = float2(1, 0), chosenDelta = 0;
                float chosenSize = 1.0;
                pigmentSeed = 0.5;
                // Jittered centers and individual angles avoid shared horizontal rows or cut tile edges.
                [unroll] for (int y = -1; y <= 1; y++)
                {
                    [unroll] for (int x = -1; x <= 1; x++)
                    {
                        float2 neighbor = float2(x, y), id = cell + neighbor;
                        float seed = Hash21(id + offset + 13.3);
                        float2 center = neighbor + 0.5 + (float2(Hash21(id + 5.2), Hash21(id + 31.7)) - 0.5) * 0.52;
                        float sn, cs;
                        sincos((Hash21(id + 71.9) - 0.5) * 1.25, sn, cs);
                        float2 along = float2(cs, sn), across = float2(-sn, cs);
                        float size = lerp(0.60, 1.40, Hash21(id + 36.2));
                        float2 delta = local - center;
                        float2 uv = float2(dot(delta, along), dot(delta, across)) / size + 0.5;
                        float2 centered = abs(uv - 0.5);
                        float metric = max(centered.x, centered.y);
                        if (metric < nearest)
                        {
                            nearest = metric;
                            chosenUV = uv;
                            chosenAlong = along;
                            chosenDelta = -delta;
                            chosenSize = size;
                            pigmentSeed = seed;
                        }
                    }
                }
                anchorDelta = chosenDelta * patchSize;
                float ink;
                if (_UseWaterBrushAtlas > 0.5)
                {
                    float2 across = float2(-chosenAlong.y, chosenAlong.x);
                    float2 texSize = max(_WaterBrushAtlas_TexelSize.zw, 4.0);
                    float2 gx = float2(dot(dpdx, chosenAlong), dot(dpdx, across)) / chosenSize * texSize * 0.5;
                    float2 gy = float2(dot(dpdy, chosenAlong), dot(dpdy, across)) / chosenSize * texSize * 0.5;
                    float maxLod = max(0.0, log2(min(texSize.x, texSize.y) * 0.5) - 4.0);
                    float lod = clamp(0.5 * log2(max(max(dot(gx, gx), dot(gy, gy)), 1e-6)), 0.0, maxLod);
                    ink = WaterStampInk(chosenUV, min(3.0, floor(pigmentSeed * 4.0)), lod);
                }
                else ink = BrushFootprint(chosenUV, pigmentSeed);
                pigmentCoordinate = float2(chosenUV.x, ink);
                // Keep the stamp's broken edge and uneven paint load when dividing reflected color.
                return smoothstep(0.12, 0.56, ink) * lerp(0.45, 1.0, ink) * (1.0 - step(0.5, nearest));
            }

            half3 GradeBrushPigment(half3 color, float alongBrush, float paintLoad, float seed, float reflectionLuminance)
            {
                // A continuous gradient follows this impression's own rotated long axis.
                float along = lerp(saturate(alongBrush), 1.0 - saturate(alongBrush), step(0.5, seed));
                float gradient = (smoothstep(0.05, 0.95, along) * 2.0 - 1.0) * _BrushGradientStrength;
                float loaded = lerp(0.65, 1.0, saturate(paintLoad));
                half3 graded = max(0, color * (1.0 + gradient * 0.20 * loaded)
                                     + gradient * half3(0.055, 0.012, -0.040) * loaded);
                // Only darker reflected forms receive saturated pigment; bright sky and cream clouds remain light.
                float shadow = 1.0 - smoothstep(0.10, 0.40, max(reflectionLuminance, 0.0));
                float luminance = max(dot(graded, half3(0.2126, 0.7152, 0.0722)), 0.0001);
                float pigmentLuminance = max(dot(_ShadowPigmentColor.rgb, half3(0.2126, 0.7152, 0.0722)), 0.001);
                half3 shadowPigment = _ShadowPigmentColor.rgb * (luminance / pigmentLuminance);
                float shadowBlend = _ShadowPigmentBlend * shadow * loaded * (1.0 - gradient * 0.30);
                // Match value before blending hue, so the new blue-green does not darken the entire pond.
                return lerp(graded, shadowPigment, saturate(shadowBlend));
            }

            half3 PaintReflectionPigment(half3 source, float seed, float2 pigmentCoordinate, float footprint)
            {
                float luminance = max(dot(source, float3(0.2126, 0.7152, 0.0722)), 0.0001);
                float levels = max(_ReflectionColorSteps, 3.0);
                float lightness = floor(sqrt(luminance) * levels + 0.5) / levels;
                float ratio = clamp(lightness * lightness / luminance, 0.6, 1.4);
                half3 color = source * lerp(1.0, ratio, _ReflectionPosterize);
                float pigment = (seed * 2.0 - 1.0) * _ReflectionBrushStrength * footprint;
                half3 painted = max(0, color * (1.0 + pigment * 0.09) + pigment * half3(0.016, 0.003, -0.013));
                half3 graded = GradeBrushPigment(painted, pigmentCoordinate.x, pigmentCoordinate.y, seed, luminance);
                half3 basePigment = GradeBrushPigment(painted, 0.5, 1.0, 0.0, luminance);
                return lerp(basePigment, graded, saturate(_ReflectionBrushStrength * footprint));
            }

            half3 BrushReflection(float3 positionWS, float2 direction, float2 ripples, float2 interactionOffset,
                                  out float reflectionLuminance, out float detailNeed)
            {
                float2 perpendicular = float2(-direction.y, direction.x);
                float2 worldBrush = float2(dot(positionWS.xz, direction), dot(positionWS.xz, perpendicular));
                float2 patchSize = max(_ReflectionBrushSize, 0.01) * float2(1.40, 0.78);
                float2 coarseDelta, coarsePigment;
                float coarseSeed;
                float coarseMask = ReflectionImpression(worldBrush, patchSize, float2(0, 0),
                                                        coarseDelta, coarseSeed, coarsePigment);
                float3 coarseCenter = positionWS + float3(direction.x * coarseDelta.x + perpendicular.x * coarseDelta.y,
                                                          0, direction.y * coarseDelta.x + perpendicular.y * coarseDelta.y);
                float2 distortion = ripples * float2(1.0, 0.22) * _ReflectionDistortion * _RippleStrength + interactionOffset;
                half3 original = SAMPLE_TEXTURE2D_LOD(_PlanarReflectionTex, sampler_PlanarReflectionTex,
                                                       saturate(ReflectionUV(positionWS) + distortion), 0).rgb;
                // One center color holds the broad impression together; the controlled pigment gradient remains.
                half3 coarseSample = SAMPLE_TEXTURE2D_LOD(_PlanarReflectionTex, sampler_PlanarReflectionTex,
                                                           saturate(ReflectionUV(coarseCenter) + distortion), 0).rgb;
                float coarseWeight = saturate(_ReflectionBrushStrength * coarseMask);
                half3 coarseSource = lerp(original, coarseSample, coarseWeight);
                half3 color = PaintReflectionPigment(coarseSource, coarseSeed, coarsePigment, coarseMask);
                reflectionLuminance = dot(coarseSource, half3(0.2126, 0.7152, 0.0722));
                detailNeed = 0.0;
                if (_SmallStrokeWeight > 0.001)
                {
                    float2 fineDelta, finePigment;
                    float fineSeed;
                    float fineMask = ReflectionImpression(worldBrush, patchSize / max(_SmallStrokeScale, 1.5),
                                                          float2(19.37, 8.61), fineDelta, fineSeed, finePigment);
                    float3 fineCenter = positionWS + float3(direction.x * fineDelta.x + perpendicular.x * fineDelta.y,
                                                            0, direction.y * fineDelta.x + perpendicular.y * fineDelta.y);
                    half3 fineSample = SAMPLE_TEXTURE2D_LOD(_PlanarReflectionTex, sampler_PlanarReflectionTex,
                                                             saturate(ReflectionUV(fineCenter) + distortion), 0).rgb;
                    // Coarse-to-fine approximation: add small paint only where the two center colors disagree.
                    // This is a local single-pass proxy for painting error, not a full multi-pass canvas solver.
                    float colorError = length(fineSample - coarseSample);
                    detailNeed = smoothstep(0.045, 0.18, colorError);
                    float fineWeight = saturate(fineMask * detailNeed * _SmallStrokeWeight * _ReflectionBrushStrength);
                    half3 fineColor = PaintReflectionPigment(fineSample, fineSeed, finePigment, 1.0);
                    color = lerp(color, fineColor, fineWeight);
                    reflectionLuminance = lerp(reflectionLuminance,
                                              dot(fineSample, half3(0.2126, 0.7152, 0.0722)), fineWeight);
                }
                return color;
            }

            struct WaterBrush
            {
                float mask;
                float seed;
                float paintLoad;
                float alongBrush;
                half3 normalWS;
                float2 alongWS;
            };

            WaterBrush SurfaceStrokes(float2 q, float2 direction, float density)
            {
                WaterBrush result;
                result.mask = 0;
                result.seed = 0.5;
                result.paintLoad = 0;
                result.alongBrush = 0.5;
                result.normalWS = half3(0, 1, 0);
                result.alongWS = direction;
                // World-anchored, short wide impressions. Only the reflected image moves with the view.
                q *= 0.52;
                float2 cell = floor(q), local = frac(q);
                float2 dpdx = ddx(q), dpdy = ddy(q);
                float nearest = 100.0;
                float2 chosenUV = float2(0.5, 0.5), chosenAlong = float2(1, 0), chosenSize = float2(1, 1);
                float chosenSeed = 0.5;
                // Find the nearest eligible impression, including neighboring cells, before sampling once.
                [unroll] for (int y = -1; y <= 1; y++)
                {
                    [unroll] for (int x = -1; x <= 1; x++)
                    {
                        float2 offset = float2(x, y), id = cell + offset;
                        float seed = Hash21(id + 13.3);
                        float enabled = step(seed, density) * step(0.001, density);
                        float2 center = offset + 0.5 + (float2(Hash21(id + 4.7), Hash21(id + 27.1)) - 0.5) * 0.34;
                        float angle = (Hash21(id + 71.9) - 0.5) * 1.12;
                        float sn, cs;
                        sincos(angle, sn, cs);
                        float2 along = float2(cs, sn), across = float2(-sn, cs);
                        float length = lerp(0.60, 1.40, Hash21(id + 36.2));
                        float aspect = lerp(1.35, 2.25, Hash21(id + 52.4));
                        float2 stampSize = float2(length, length / aspect);
                        float2 delta = local - center;
                        float2 uv = float2(dot(delta, along), dot(delta, across)) / stampSize + 0.5;
                        float2 centered = abs(uv - 0.5);
                        float metric = max(centered.x, centered.y);
                        if (enabled > 0.5 && metric < nearest)
                        {
                            nearest = metric;
                            chosenUV = uv;
                            chosenAlong = along;
                            chosenSize = stampSize;
                            chosenSeed = Hash21(id + 93.7);
                        }
                    }
                }
                float2 chosenAcross = float2(-chosenAlong.y, chosenAlong.x);
                float2 uvDx = float2(dot(dpdx, chosenAlong), dot(dpdx, chosenAcross)) / chosenSize;
                float2 uvDy = float2(dot(dpdy, chosenAlong), dot(dpdy, chosenAcross)) / chosenSize;
                float2 perpendicular = float2(-direction.y, direction.x);
                float2 alongWS = direction * chosenAlong.x + perpendicular * chosenAlong.y;
                float2 acrossWS = direction * chosenAcross.x + perpendicular * chosenAcross.y;
                float ink;
                float2 gradient = 0;
                if (_UseWaterBrushAtlas > 0.5)
                {
                    float2 size = max(_WaterBrushAtlas_TexelSize.zw, 4.0);
                    float2 gx = uvDx * size * 0.5, gy = uvDy * size * 0.5;
                    float maxLod = max(0.0, log2(min(size.x, size.y) * 0.5) - 4.0);
                    float lod = clamp(0.5 * log2(max(max(dot(gx, gx), dot(gy, gy)), 1e-6)), 0.0, maxLod);
                    float tile = min(3.0, floor(chosenSeed * 4.0));
                    ink = WaterStampInk(chosenUV, tile, lod);
                    // Coarse, bounded relief avoids glitter from individual high-frequency bristles.
                    float2 stepUV = max(float2(0.012, 0.012), (exp2(lod) * 2.0) / size);
                    float inkU = WaterStampInk(chosenUV + float2(stepUV.x, 0), tile, lod);
                    float inkV = WaterStampInk(chosenUV + float2(0, stepUV.y), tile, lod);
                    gradient = clamp(float2(inkU - ink, inkV - ink) * 1.3, -0.28, 0.28);
                    gradient *= 1.0 - smoothstep(maxLod - 1.0, maxLod + 0.1, lod);
                }
                else ink = BrushFootprint(chosenUV, chosenSeed);
                float eligible = 1.0 - step(0.5, nearest);
                // The atlas supplies broken shape and paint relief, never white diffuse color.
                result.mask = smoothstep(0.14, 0.48, ink) * lerp(0.25, 1.0, ink) * eligible;
                result.seed = chosenSeed;
                result.paintLoad = ink;
                result.alongBrush = chosenUV.x;
                float2 tilt = alongWS * gradient.x + acrossWS * gradient.y;
                result.normalWS = normalize(half3(-tilt.x, 1.0, -tilt.y));
                result.alongWS = alongWS;
                return result;
            }

            half3 GlazeWaterBrush(half3 localColor, WaterBrush brush, half3 viewDirection, Light sun, float waterDepth,
                                  float layerWeight, float reflectionLuminance)
            {
                float seedValue = brush.seed * 2.0 - 1.0;
                float signedPigment = (seedValue < 0.0 ? -1.0 : 1.0) * lerp(0.35, 1.0, abs(seedValue));
                // Hue and value belong to one paint impression; atlas gray levels retain its interior brushwork.
                half3 pigment = localColor * (1.0 + signedPigment * 0.22 + (brush.paintLoad - 0.65) * 0.10);
                pigment += signedPigment * half3(0.055, 0.012, -0.040);
                // A restrained warm tint keeps the actual water and reflected colors visible.
                pigment = lerp(pigment, _RippleColor.rgb, saturate(signedPigment) * 0.045);
                half3 halfDirection = SafeNormalize(viewDirection + sun.direction);
                float raised = pow(saturate(dot(brush.normalWS, halfDirection)), 18.0);
                float flat = pow(saturate(halfDirection.y), 18.0);
                float relief = clamp((raised - flat) * 0.20, -0.065, 0.065) * _BrushViewResponse;
                pigment += relief * min(sun.color, half3(1.25, 1.25, 1.25)) * lerp(localColor, _RippleColor.rgb, 0.35);
                pigment = GradeBrushPigment(pigment, brush.alongBrush, brush.paintLoad, brush.seed, reflectionLuminance);
                float2 viewXZ = normalize(viewDirection.xz + float2(0.0001, 0));
                float acrossView = 1.0 - abs(dot(viewXZ, brush.alongWS));
                float directionalWeight = lerp(0.55, 1.0, smoothstep(0.0, 0.9, acrossView));
                float viewWeight = lerp(0.8, directionalWeight, _BrushViewResponse);
                float weight = brush.mask * _SurfaceStrokeStrength * viewWeight * layerWeight;
                weight *= smoothstep(_ShoreWidth * 0.6, _ShoreWidth * 2.0, waterDepth);
                return lerp(localColor, max(0, pigment), saturate(weight));
            }

            float ShorePaint(float2 p, float waterDepth, float time)
            {
                // Depth ties every mark to the actual bank and intersecting geometry.
                float broad = ValueNoise(p * 0.85 + float2(7.4, 19.2));
                float detail = ValueNoise(p * 2.1 + float2(16.2, 3.8));
                float localWidth = max(0.001, _ShoreWidth * lerp(0.55, 1.65, broad));
                float aa = clamp(fwidth(waterDepth) * 0.5, 0.001, localWidth * 0.12);
                float foamTime = _Time.y * _FoamSpeed;
                float surge = sin(foamTime * 2.1 + broad * 5.0) * 0.5 + 0.5;
                float center = localWidth * (lerp(0.25, 0.44, detail) + _FoamTravel * surge * 0.65);
                aa = max(aa, localWidth * _FoamSoftness * _FoamTravel);
                float halfWidth = localWidth * lerp(0.075, 0.19, detail);
                float contact = 1.0 - smoothstep(max(0.0, halfWidth - aa), halfWidth + aa,
                                                  abs(waterDepth - center));
                float flowing = ValueNoise(p * 1.8 + float2(foamTime * .33, -foamTime * .18));
                float contactBreaks = smoothstep(0.27, 0.57, lerp(broad, flowing, _FoamTravel));
                // A second, thinner impression overlaps in only some segments of the shore.
                float outerNoise = ValueNoise(p * 1.25 + float2(32.8, 11.7));
                float outerCenter = localWidth * (0.87 + (outerNoise - 0.5) * 0.22);
                outerCenter += localWidth * sin(time * 0.6 + broad * 4.0) * 0.035;
                float outerWidth = localWidth * lerp(0.028, 0.075, outerNoise);
                float outer = 1.0 - smoothstep(max(0.0, outerWidth - aa), outerWidth + aa,
                                                abs(waterDepth - outerCenter));
                float outerBreaks = smoothstep(0.53, 0.72, outerNoise);
                return saturate(contact * contactBreaks * lerp(1.0, .65 + .35 * surge, _FoamTravel)
                    + outer * outerBreaks * 0.42);
            }

            float ShortWavePaint(float2 p)
            {
                float2 q = p * _WaveletScale;
                float2 cell = floor(q / float2(3.5, 1.8));
                float seed = Hash21(cell + 21.7);
                float phase = frac(_Time.y * _WaveletSpeed * .25 + seed);
                float2 local = (frac(q / float2(3.5, 1.8)) - .5) * float2(3.5, 1.8);
                local.y -= (phase - .5) * .65;
                float curve = sin(local.x * 2.7 + seed * 6.28) * .17;
                float edge = abs(local.y - curve);
                float aa = max(fwidth(edge), .008);
                float mark = 1 - smoothstep(_WaveletWidth * .3, _WaveletWidth + aa, edge);
                float length = _WaveletLength * lerp(.7, 1, seed);
                float ends = 1 - smoothstep(length * .55, length, abs(local.x));
                if (_UseWaterBrushAtlas > .5)
                {
                    float2 inkUV = float2(local.x / max(length * 2, .01) + .5, (local.y - curve) / max(_WaveletWidth * 3, .01) + .5);
                    float2 dx = ddx(inkUV) * _WaterBrushAtlas_TexelSize.zw * .5;
                    float2 dy = ddy(inkUV) * _WaterBrushAtlas_TexelSize.zw * .5;
                    float lod = max(0, .5 * log2(max(dot(dx, dx), dot(dy, dy)) + .001));
                    mark *= lerp(.5, 1, WaterStampInk(saturate(inkUV), min(3, floor(seed * 4)), min(lod, 5)));
                }
                float life = sin(phase * 3.14159265);
                return mark * ends * life * life * smoothstep(.25, .65, seed);
            }

            float Caustics(float2 bedXZ, float time)
            {
                // Animated Voronoi edges projected on the reconstructed bed, not on the water plane.
                float2 q = bedXZ * _CausticScale + float2(time * 0.06, -time * 0.04);
                float2 cell = floor(q), local = frac(q);
                float nearest = 8.0, second = 8.0;
                [unroll] for (int y = -1; y <= 1; y++)
                {
                    [unroll] for (int x = -1; x <= 1; x++)
                    {
                        float2 offset = float2(x, y);
                        float2 seed = float2(Hash21(cell + offset), Hash21(cell + offset + 43.1));
                        float2 site = 0.5 + 0.28 * sin(seed * 6.2831853 + time * float2(0.8, 1.1));
                        float2 delta = offset + site - local;
                        float distanceSquared = dot(delta, delta);
                        second = max(nearest, min(second, distanceSquared));
                        nearest = min(nearest, distanceSquared);
                    }
                }
                float edge = sqrt(second) - sqrt(nearest);
                float aa = max(fwidth(edge), 0.002);
                return 1.0 - smoothstep(0.025, 0.070 + aa, edge);
            }

            float2 InteractionSlope(float2 worldXZ, float waterDepth)
            {
                // Zero strength and an unbound rectangle are strict neutral paths.
                if (_InteractionStrength <= 0.0 || _InteractionWorldRect.z <= 0.0 || _InteractionWorldRect.w <= 0.0)
                    return 0;
                float2 uv = (worldXZ - _InteractionWorldRect.xy) * _InteractionWorldRect.zw;
                float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                // Field RGB encodes world X / world Z / up Y. Neutral is (0.5, 0.5, 1).
                float3 tangentNormal = SAMPLE_TEXTURE2D_LOD(_InteractionNormalTex, sampler_InteractionNormalTex, saturate(uv), 0).rgb * 2.0 - 1.0;
                float2 slope = tangentNormal.xy / max(tangentNormal.z, 0.35);
                slope = clamp(slope * clamp(_InteractionStrength, 0.0, 3.0), -0.65, 0.65);
                return slope * inside * smoothstep(0.015, 0.14, waterDepth);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                // Reconstructing the bed gives vertical depth for perspective and orthographic views.
                float3 bedPosition = ScenePosition(screenUV);
                float waterDepth = max(0.0, input.positionWS.y - bedPosition.y);
                if (_DebugDepth > 0.5 && _DebugDepth < 1.5)
                {
                    float depthPreview = saturate(waterDepth / max(_DepthDistance, 0.01));
                    return half4(depthPreview, depthPreview, depthPreview, 1);
                }
                float depthBlend = 1.0 - exp(-waterDepth / max(_DepthDistance, 0.01));
                float time = _Time.y * _RippleSpeed;
                float2 p = input.positionWS.xz;
                float2 direction = BrushDirection();
                float2 perpendicular = float2(-direction.y, direction.x);
                float2 q = float2(dot(p, direction), dot(p, perpendicular)) * _StrokeScale;
                float2 ripples = PaintedRipples(q, time);
                float2 slope = (direction * ripples.x * 0.11 + perpendicular * ripples.y * 0.075) * _RippleStrength;
                half3 calmNormalWS = normalize(half3(slope.x, 1.0, slope.y));
                float2 interactionSlope = InteractionSlope(p, waterDepth);
                half3 normalWS = normalize(half3(slope.x + interactionSlope.x, 1.0, slope.y + interactionSlope.y));
                float2 interactionView = mul((float3x3)UNITY_MATRIX_V, float3(interactionSlope.x, 0, interactionSlope.y)).xy;

                float2 refractedUV = screenUV + ripples * float2(1.0, 0.35) * _RefractionStrength * _RippleStrength
                                               * smoothstep(0.02, 0.5, waterDepth) + interactionView * 0.018;
                float2 halfPixel = 0.5 / _ScaledScreenParams.xy;
                refractedUV = clamp(refractedUV, halfPixel, 1.0 - halfPixel);
                float3 refractedBed = ScenePosition(refractedUV);
                float surfaceEyeDepth = -TransformWorldToView(input.positionWS).z;
                float bedEyeDepth = -TransformWorldToView(refractedBed).z;
                // Reject samples of foreground grass, banks and exposed rocks before sampling their color.
                float accepted = step(surfaceEyeDepth + 0.001, bedEyeDepth) * step(refractedBed.y, input.positionWS.y - 0.001);
                float2 safeUV = lerp(screenUV, refractedUV, accepted);
                float3 safeBed = lerp(bedPosition, refractedBed, accepted);
                float safeDepth = max(0.0, input.positionWS.y - safeBed.y);
                half3 bedColor = 0;
                if (_HasSceneColor > 0.5) bedColor = SampleSceneColor(safeUV);
                Light bedSun = GetMainLight(TransformWorldToShadowCoord(safeBed));
                float caustic = 0.0;
                if ((_CausticStrength > 0.001 && _HasSceneColor > 0.5) || (_DebugDepth > 1.5 && _DebugDepth < 2.5))
                {
                    float3 bedNormal = normalize(cross(ddy(safeBed), ddx(safeBed)) + float3(0, 0.00001, 0));
                    float receiver = saturate(abs(bedNormal.y) * 1.5);
                    // Keep the bed pattern in clear shallows so deep reflections remain quiet.
                    float depthFade = (1.0 - smoothstep(_CausticDepth * 0.2, _CausticDepth, safeDepth))
                                      * smoothstep(0.03, 0.16, safeDepth);
                    caustic = Caustics(safeBed.xz, _Time.y * _CausticSpeed) * depthFade * receiver * bedSun.shadowAttenuation;
                    bedColor += caustic * _CausticStrength * half3(0.85, 0.90, 0.67) * min(bedSun.color, half3(1.5, 1.5, 1.5));
                }
                if (_DebugDepth > 1.5 && _DebugDepth < 2.5) return half4(caustic, caustic, caustic, 1);
                if (_DebugDepth > 2.5) return half4(1.0 - accepted, accepted, 0, 1);

                half3 waterColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthBlend);
                half3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 reflectionDirection = reflect(-viewDirection, normalWS);
                half3 reflectedColor = GlossyEnvironmentReflection(reflectionDirection, input.positionWS,
                                                                   0.22h, 1.0h, screenUV);
                float reflectionLuminance = dot(reflectedColor, half3(0.2126, 0.7152, 0.0722));
                // Probe fallback has no stroke-center samples; screen contrast is only a detail-placement fallback.
                float reflectionDetail = smoothstep(0.015, 0.08, length(fwidth(reflectedColor)));
                if (_HasPlanarReflection > 0.5)
                    reflectedColor = BrushReflection(input.positionWS, direction, ripples, interactionView * 0.025,
                                                       reflectionLuminance, reflectionDetail);
                reflectedColor = lerp(reflectedColor, _ReflectionTint.rgb, _ReflectionTintStrength);
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirection)), max(.2, _ReflectionFalloff));
                float reflectionWeight = _ReflectionStrength * lerp(_ReflectionNear, _ReflectionFar, fresnel);
                reflectionWeight *= smoothstep(0.015, 0.24, waterDepth);
                waterColor = lerp(waterColor, reflectedColor, reflectionWeight);
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                waterColor *= lerp(0.90, 1.0, sun.shadowAttenuation);
                // A small local highlight follows the same dynamic normal as refraction and reflection.
                half3 waterHalfDirection = SafeNormalize(viewDirection + sun.direction);
                float displacedLight = pow(saturate(dot(normalWS, waterHalfDirection)), 16.0);
                float calmLight = pow(saturate(dot(calmNormalWS, waterHalfDirection)), 16.0);
                float interactionLight = clamp((displacedLight - calmLight) * 0.16, -0.06, 0.06);
                waterColor += interactionLight * min(sun.color, half3(1.25, 1.25, 1.25)) * lerp(waterColor, _RippleColor.rgb, 0.35);
                if (_SurfaceStrokeStrength > 0.001)
                {
                    WaterBrush broadBrush = SurfaceStrokes(q, direction, _SurfaceStrokeDensity);
                    waterColor = GlazeWaterBrush(waterColor, broadBrush, viewDirection, sun, waterDepth, 1.0, reflectionLuminance);
                    if (_SmallStrokeWeight > 0.001)
                    {
                        // Sparse small daubs repair reflection detail; quiet water keeps the broad underpainting.
                        WaterBrush smallBrush = SurfaceStrokes(q * _SmallStrokeScale + float2(17.3, 53.9),
                                                               direction, _SurfaceStrokeDensity * 0.45);
                        waterColor = GlazeWaterBrush(waterColor, smallBrush, viewDirection, sun, waterDepth,
                                                     _SmallStrokeWeight * reflectionDetail, reflectionLuminance);
                    }
                }
                float wavePaint = 0;
                if (_WaveletStrength > .001)
                    wavePaint = ShortWavePaint(float2(dot(p, direction), dot(p, perpendicular)))
                        * _WaveletStrength * smoothstep(.08, .4, waterDepth);
                waterColor = lerp(waterColor, _RippleColor.rgb, wavePaint);
                half waterLuma = dot(waterColor, half3(.2126, .7152, .0722));
                waterColor = max(0, lerp(waterLuma.xxx, waterColor, _WaterSaturation));
                waterColor = MixFog(waterColor, input.fogFactor);
                // Very shallow pigment gradually enters the visible bed color, avoiding a pale solid rim.
                // The final water-layer opacity remains the same 70/30 composition, exactly once.
                float shallowPigment = smoothstep(0.015, 0.32, waterDepth);
                if (_HasSceneColor > 0.5) waterColor = lerp(bedColor, waterColor, shallowPigment);
                half3 color = _HasSceneColor > 0.5 ? lerp(bedColor, waterColor, _Opacity) : waterColor;

                float edgeWeight = _ShoreStrength * ShorePaint(p, waterDepth, time);
                half3 strokeColor = MixFog(_FoamColor.rgb, input.fogFactor);
                color = lerp(color, strokeColor, saturate(edgeWeight));
                float shorelineFade = smoothstep(0.0, 0.025, waterDepth);
                // SceneView can omit OpaqueTexture in URP 14; fall back without reading stale camera data.
                float alpha = _HasSceneColor > 0.5 ? shorelineFade : shorelineFade * max(_Opacity, saturate(edgeWeight));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
