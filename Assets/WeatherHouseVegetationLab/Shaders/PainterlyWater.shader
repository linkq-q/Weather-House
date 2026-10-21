Shader "Weather House/Painterly Water"
{
    Properties
    {
        [Header(Pond Palette)]
        _ShallowColor ("Shallow bank color", Color) = (0.710, 0.812, 0.753, 1)
        _DeepColor ("Deep turquoise color", Color) = (0.604, 0.780, 0.753, 1)
        _DepthDistance ("Depth color distance (m)", Range(0.1, 8)) = 1.2
        _Opacity ("Water opacity", Range(0, 1)) = 0.70
        _RefractionStrength ("Refraction strength", Range(0, 0.05)) = 0.012
        [Header(Reflections)]
        _ReflectionTint ("Reflection tint", Color) = (0.722, 0.847, 0.796, 1)
        _ReflectionStrength ("Reflection strength", Range(0, 1)) = 0.86
        _ReflectionDistortion ("Reflection breakup", Range(0, 0.06)) = 0.012
        _ReflectionBrushStrength ("Reflection brush strength", Range(0, 1)) = 0.50
        _ReflectionBrushSize ("Reflection brush length (m)", Range(0.1, 3)) = 0.75
        _ReflectionColorSteps ("Reflection lightness steps", Range(3, 24)) = 9
        _ReflectionPosterize ("Reflection color simplification", Range(0, 1)) = 0.35
        [HideInInspector] _PlanarReflectionTex ("Planar reflection", 2D) = "black" {}
        [HideInInspector] _HasPlanarReflection ("Has planar reflection", Float) = 0
        [HideInInspector] _HasSceneColor ("Has current camera opaque color", Float) = 0
        [Header(Painted Ripples)]
        _RippleColor ("Cream brush stroke color", Color) = (0.933, 0.937, 0.824, 1)
        _RippleStrength ("Ripple strength", Range(0, 1)) = 0.55
        _RippleSpeed ("Ripple speed", Range(0, 1)) = 0.16
        _StrokeScale ("Brush stroke frequency", Range(0.1, 4)) = 0.8
        _BrushDirection ("Brush direction XZ", Vector) = (0.819, 0.574, 0, 0)
        [Toggle] _ViewAlignedStrokes ("Keep strokes horizontal in view", Float) = 1
        _SurfaceStrokeStrength ("Surface stroke strength", Range(0, 1)) = 0.58
        _SurfaceStrokeDensity ("Surface stroke density", Range(0, 1)) = 0.24
        [Header(Shoreline)]
        _ShoreWidth ("Shoreline depth width (m)", Range(0.01, 1)) = 0.16
        _ShoreStrength ("Shoreline stroke strength", Range(0, 1)) = 0.72
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
            CBUFFER_END

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
                float warp = ValueNoise(q * 0.85 + float2(time * 0.12, -time * 0.18)) - 0.5;
                float broad = ValueNoise(q * float2(0.62, 6.5) + float2(time * 0.22, -time * 0.45) + warp * 0.35);
                float detail = ValueNoise(q * float2(1.8, 14.0) + float2(-time * 0.31, time * 0.32) + warp * 0.6);
                broad = lerp(broad, smoothstep(0.28, 0.72, broad), 0.7);
                detail = lerp(detail, smoothstep(0.3, 0.7, detail), 0.45);
                return float2(broad * 0.75 + detail * 0.25, detail) * 2.0 - 1.0;
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

            half3 BrushReflection(float3 positionWS, float2 direction, float2 ripples)
            {
                float2 perpendicular = float2(-direction.y, direction.x);
                float2 patchSize = max(_ReflectionBrushSize, 0.01) * float2(1.0, 0.16);
                float2 q = float2(dot(positionWS.xz, direction), dot(positionWS.xz, perpendicular)) / patchSize;
                // Stagger and slightly bend the rows so patch boundaries never make a rectangular grid.
                q.y += ValueNoise(float2(q.x * 0.27, q.y * 0.08)) * 0.22;
                float rowOffset = Hash21(float2(floor(q.y), 7.4));
                q.x += rowOffset;
                float2 cell = floor(q);
                float2 local = frac(q);
                float2 center = 0.5 + float2(Hash21(cell), Hash21(cell + 31.7)) * 0.16 - 0.08;
                float2 toCenter = (center - local) * patchSize;
                float3 anchor = positionWS + float3(direction.x * toCenter.x + perpendicular.x * toCenter.y,
                                                    0, direction.y * toCenter.x + perpendicular.y * toCenter.y);
                float2 distortion = ripples * float2(1.0, 0.22) * _ReflectionDistortion * _RippleStrength;
                float2 uv = ReflectionUV(positionWS) + distortion;
                float2 anchorUV = ReflectionUV(anchor) + distortion;
                half3 original = SAMPLE_TEXTURE2D_LOD(_PlanarReflectionTex, sampler_PlanarReflectionTex, saturate(uv), 0).rgb;
                half3 patch = SAMPLE_TEXTURE2D_LOD(_PlanarReflectionTex, sampler_PlanarReflectionTex, saturate(anchorUV), 0).rgb;
                float2 margin = min(local, 1.0 - local);
                float feather = smoothstep(0.0, 0.10, margin.x) * smoothstep(0.0, 0.12, margin.y);
                half3 color = lerp(original, patch, _ReflectionBrushStrength * feather);
                // Quantize perceptual lightness, retaining the actual warm/cool reflected hue.
                float luminance = max(dot(color, float3(0.2126, 0.7152, 0.0722)), 0.0001);
                float levels = max(_ReflectionColorSteps, 3.0);
                float lightness = floor(sqrt(luminance) * levels + 0.5) / levels;
                float ratio = clamp(lightness * lightness / luminance, 0.6, 1.4);
                return color * lerp(1.0, ratio, _ReflectionPosterize);
            }

            float SurfaceStrokes(float2 q, float time)
            {
                // One finite, tapered line per sparse staggered cell; no repeating full-width stripes.
                q *= float2(0.30, 1.45);
                q.y += time * 0.025;
                q.x += Hash21(float2(floor(q.y), 18.1));
                float2 cell = floor(q);
                float2 f = frac(q);
                float seed = Hash21(cell + 13.3);
                float length = lerp(0.16, 0.40, Hash21(cell + 6.2));
                float x = abs(f.x - 0.5) / length;
                float taper = saturate(1.0 - x * x);
                float center = 0.32 + Hash21(cell + 81.4) * 0.36;
                center += sin(f.x * 5.0 + seed * 6.28 + time * 0.3) * 0.004;
                float width = lerp(0.018, 0.030, seed) * taper;
                float aa = max(fwidth(q.y), 0.0001);
                float lineMask = 1.0 - smoothstep(width, width + aa, abs(f.y - center));
                float ends = 1.0 - smoothstep(0.8, 1.0, x);
                float presence = step(seed, _SurfaceStrokeDensity) * step(0.001, _SurfaceStrokeDensity);
                return lineMask * ends * presence;
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
                half3 normalWS = normalize(half3(slope.x, 1.0, slope.y));

                float2 refractedUV = screenUV + ripples * float2(1.0, 0.35) * _RefractionStrength * _RippleStrength
                                               * smoothstep(0.02, 0.5, waterDepth);
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
                if (_HasPlanarReflection > 0.5) reflectedColor = BrushReflection(input.positionWS, direction, ripples);
                reflectedColor = lerp(reflectedColor, _ReflectionTint.rgb, 0.06);
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirection)), 2.0);
                float reflectionWeight = _ReflectionStrength * lerp(0.65, 1.0, fresnel);
                reflectionWeight *= smoothstep(0.015, 0.24, waterDepth);
                waterColor = lerp(waterColor, reflectedColor, reflectionWeight);
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                waterColor *= lerp(0.90, 1.0, sun.shadowAttenuation);
                waterColor = MixFog(waterColor, input.fogFactor);
                // SceneColor already contains the bed (and its fog): composite exactly once.
                half3 color = _HasSceneColor > 0.5 ? lerp(bedColor, waterColor, _Opacity) : waterColor;

                // Reconstructed vertical depth follows both the bank and intersecting rocks.
                float shoreNoise = ValueNoise(p * 2.8 + ripples * 0.15);
                float shoreDepth = waterDepth + (shoreNoise - 0.5) * _ShoreWidth * 0.16;
                float shoreAA = clamp(fwidth(waterDepth) * 0.5, 0.001, _ShoreWidth * 0.10);
                float shoreline = 1.0 - smoothstep(max(0.0, _ShoreWidth * 0.055 - shoreAA), _ShoreWidth * 0.055 + shoreAA,
                                                   abs(shoreDepth - _ShoreWidth * 0.32));
                float ringCenter = _ShoreWidth * (0.95 + sin(time * 0.65 + shoreNoise) * 0.09);
                float ring = 1.0 - smoothstep(max(0.0, _ShoreWidth * 0.025 - shoreAA), _ShoreWidth * 0.025 + shoreAA, abs(shoreDepth - ringCenter));
                float arcNoise = ValueNoise(p * 0.95 + float2(7.4, 19.2));
                float brokenEdge = smoothstep(0.40, 0.62, arcNoise);
                float edgeWeight = _ShoreStrength * (shoreline * brokenEdge + ring * smoothstep(0.56, 0.72, arcNoise) * 0.20);
                float stroke = SurfaceStrokes(q, time) * _SurfaceStrokeStrength * smoothstep(_ShoreWidth, _ShoreWidth * 2.0, waterDepth);
                half3 strokeColor = MixFog(_RippleColor.rgb, input.fogFactor);
                color = lerp(color, strokeColor, saturate(max(edgeWeight, stroke)));
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
