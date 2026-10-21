Shader "Weather House/Painterly Water"
{
    Properties
    {
        [Header(Pond Palette)]
        _ShallowColor ("Shallow bank color", Color) = (0.714, 0.780, 0.678, 1)
        _DeepColor ("Deep turquoise color", Color) = (0.561, 0.710, 0.706, 1)
        _DepthDistance ("Depth color distance (m)", Range(0.1, 8)) = 1.8
        [Header(Reflections)]
        _ReflectionTint ("Reflection tint", Color) = (0.757, 0.824, 0.796, 1)
        _ReflectionStrength ("Reflection strength", Range(0, 1)) = 0.94
        _ReflectionDistortion ("Reflection breakup", Range(0, 0.06)) = 0.022
        [HideInInspector] _PlanarReflectionTex ("Planar reflection", 2D) = "black" {}
        [HideInInspector] _HasPlanarReflection ("Has planar reflection", Float) = 0
        [Header(Painted Ripples)]
        _RippleColor ("Brush stroke color", Color) = (0.85, 0.91, 0.81, 1)
        _RippleStrength ("Ripple strength", Range(0, 1)) = 0.65
        _RippleSpeed ("Ripple speed", Range(0, 1)) = 0.16
        _StrokeScale ("Brush stroke frequency", Range(0.1, 4)) = 0.8
        _BrushDirection ("Brush direction XZ", Vector) = (0.819, 0.574, 0, 0)
        _ShoreWidth ("Soft shoreline width (m)", Range(0.01, 1)) = 0.16
        [Enum(Off,0,WaterDepth,1)] _DebugDepth ("Debug water depth", Float) = 0
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

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _ReflectionTint;
                half4 _RippleColor;
                float _DepthDistance;
                float _ReflectionStrength;
                float _ReflectionDistortion;
                float _RippleStrength;
                float _RippleSpeed;
                float _StrokeScale;
                float _ShoreWidth;
                float _HasPlanarReflection;
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

            // Two differently moving scales break the reflection into short horizontal patches.
            float2 PaintedRipples(float2 worldXZ, float time)
            {
                float2 direction = normalize(_BrushDirection.xy + float2(0.0001, 0));
                float2 q = float2(dot(worldXZ, direction), dot(worldXZ, float2(-direction.y, direction.x)));
                q *= _StrokeScale;
                float warp = ValueNoise(q * 0.85 + float2(time * 0.12, -time * 0.18)) - 0.5;
                float broad = ValueNoise(q * float2(0.62, 6.5) + float2(time * 0.22, -time * 0.45) + warp * 0.35);
                float detail = ValueNoise(q * float2(1.8, 14.0) + float2(-time * 0.31, time * 0.32) + warp * 0.6);
                broad = lerp(broad, smoothstep(0.28, 0.72, broad), 0.7);
                detail = lerp(detail, smoothstep(0.3, 0.7, detail), 0.45);
                return float2(broad * 0.75 + detail * 0.25, detail) * 2.0 - 1.0;
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
                float deviceDepth = SampleSceneDepth(screenUV);
                #if !UNITY_REVERSED_Z
                    deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, deviceDepth);
                #endif
                // Reconstructing the bed gives vertical depth for perspective and orthographic views.
                float3 bedPosition = ComputeWorldSpacePosition(screenUV, deviceDepth, UNITY_MATRIX_I_VP);
                float waterDepth = max(0.0, input.positionWS.y - bedPosition.y);
                if (_DebugDepth > 0.5)
                {
                    float depthPreview = saturate(waterDepth / max(_DepthDistance, 0.01));
                    return half4(depthPreview, depthPreview, depthPreview, 1);
                }
                float depthBlend = 1.0 - exp(-waterDepth / max(_DepthDistance, 0.01));
                float time = _Time.y * _RippleSpeed;
                float2 p = input.positionWS.xz;

                float broadNoise = ValueNoise(p * 0.21);
                half3 color = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthBlend);
                color *= lerp(0.97, 1.03, broadNoise);

                float2 ripples = PaintedRipples(p, time);
                half3 normalWS = normalize(half3(ripples.x * 0.11 * _RippleStrength,
                                                1.0, ripples.y * 0.075 * _RippleStrength));
                half3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 reflectionDirection = reflect(-viewDirection, normalWS);
                half3 reflectedColor = GlossyEnvironmentReflection(reflectionDirection, input.positionWS,
                                                                   0.22h, 1.0h, screenUV);
                float4 projectedReflection = mul(_PlanarReflectionVP, float4(input.positionWS, 1));
                float2 reflectionUV = projectedReflection.xy / max(projectedReflection.w, 0.0001);
                reflectionUV += ripples * float2(1.0, 0.22) * _ReflectionDistortion * _RippleStrength;
                half3 planar = SAMPLE_TEXTURE2D_LOD(_PlanarReflectionTex, sampler_PlanarReflectionTex,
                                                   saturate(reflectionUV), 0).rgb;
                reflectedColor = lerp(reflectedColor, planar, _HasPlanarReflection);
                reflectedColor = lerp(reflectedColor, _ReflectionTint.rgb, 0.06);
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirection)), 2.0);
                float reflectionWeight = _ReflectionStrength * lerp(0.65, 1.0, fresnel);
                reflectionWeight *= smoothstep(0.015, 0.24, waterDepth);
                color = lerp(color, reflectedColor, reflectionWeight);

                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                color *= lerp(0.90, 1.0, sun.shadowAttenuation);

                // Reconstructed vertical depth follows both the bank and intersecting rocks.
                float shoreNoise = ValueNoise(p * 2.8 + ripples * 0.15);
                float shoreDepth = waterDepth + (shoreNoise - 0.5) * _ShoreWidth * 0.28;
                float shoreline = 1.0 - smoothstep(_ShoreWidth * 0.13, _ShoreWidth * 0.42, abs(shoreDepth - _ShoreWidth * 0.28));
                float ring = pow(saturate(0.5 + 0.5 * sin(shoreDepth * 32.0 - time * 1.7 + shoreNoise)), 12.0);
                ring *= (1.0 - smoothstep(_ShoreWidth, _ShoreWidth * 4.0, waterDepth));
                ring *= smoothstep(_ShoreWidth * 0.4, _ShoreWidth, waterDepth);
                float brokenEdge = smoothstep(0.25, 0.62, shoreNoise);
                float edgeWeight = shoreline * (0.34 + brokenEdge * 0.38) + ring * brokenEdge * 0.3;
                color = lerp(color, _RippleColor.rgb, saturate(edgeWeight));
                color = MixFog(color, input.fogFactor);
                float shorelineFade = smoothstep(0.0, 0.025, waterDepth);
                float opacity = lerp(0.68, 0.99, depthBlend) * shorelineFade;
                return half4(color, opacity);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
