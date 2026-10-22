Shader "Weather House/Painterly Grass"
{
    Properties
    {
        _BaseColor("Root Color", Color) = (0.34, 0.44, 0.11, 1)
        _TipColor("Tip Color", Color) = (0.83, 0.86, 0.31, 1)
        _ShadowTint("Cool Shadow Color", Color) = (0.28, 0.43, 0.40, 1)
        _ShadowStrength("Shadow Strength", Range(0, 1)) = 0.85
        _UpNormals("Upward Normals (0 Geometry / 1 Smooth Meadow)", Range(0, 1)) = 1
        _PatchScale("Broad Color Patch Scale", Range(0.02, 3)) = 0.23
        _PatchStrength("Broad Color Patch Strength", Range(0, 0.6)) = 0.17
        _MeadowBlend("Continuous meadow blend", Range(0, 1)) = 0
        _MeadowBaseColor("Meadow shared base", Color) = (0.49, 0.55, 0.24, 1)
        _MeadowPatchColor("Meadow shared warm patch", Color) = (0.81, 0.75, 0.38, 1)
        _MeadowPatchScale("Meadow shared patch scale", Range(0.02, 2)) = 0.23
        _BladeContrast("Individual blade contrast", Range(0, 1)) = 1
        _WindStrength("Wind Strength (Metres)", Range(0, 0.8)) = 0.11
        _WindSpeed("Wind Speed", Range(0, 5)) = 1.0
        _WindScale("Wind Wave Scale", Range(0.02, 3)) = 0.32
        _WindDirection("Wind Direction XZ", Vector) = (1, 0, 0.45, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _TipColor, _ShadowTint;
            half4 _MeadowBaseColor, _MeadowPatchColor;
            float _MeadowBlend, _MeadowPatchScale, _BladeContrast;
            float4 _WindDirection;
            float _ShadowStrength, _PatchScale, _PatchStrength, _UpNormals;
            float _WindStrength, _WindSpeed, _WindScale;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float2 wind : TEXCOORD1;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        float3 GrassPositionWS(Attributes input)
        {
            float3 world = TransformObjectToWorld(input.positionOS.xyz);
            float2 direction = normalize(_WindDirection.xz + float2(0.0001, 0));
            float phase = dot(world.xz, direction) * _WindScale - _Time.y * _WindSpeed;
            float gust = sin(phase) * 0.65 + sin(phase * 0.47 + 1.4) * 0.35;
            world.xz += direction * (gust * _WindStrength * input.wind.x);
            world.y -= abs(gust) * _WindStrength * input.wind.x * 0.09;
            return world;
        }
        float PatchNoise(float2 p)
        {
            float2 cell = floor(p);
            float2 f = frac(p);
            f = f * f * (3 - 2 * f);
            float4 dots = float4(dot(cell, float2(127.1, 311.7)), dot(cell + float2(1, 0), float2(127.1, 311.7)),
                dot(cell + float2(0, 1), float2(127.1, 311.7)), dot(cell + 1, float2(127.1, 311.7)));
            float4 h = frac(sin(dots) * 43758.5453);
            return lerp(lerp(h.x, h.y, f.x), lerp(h.z, h.w, f.x), f.y);
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half4 color : COLOR;
                half fog : TEXCOORD2;
                half3 normalWS : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = GrassPositionWS(input);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                output.color = input.color;
                // Apply upward shading after per-clump transforms, so merged grass stays consistent.
                output.normalWS = SafeNormalize(lerp(TransformObjectToWorldNormal(input.normalOS),
                    half3(0, 1, 0), saturate(_UpNormals)));
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float patch = PatchNoise(input.positionWS.xz * _PatchScale);
                half3 grass = lerp(_BaseColor.rgb, _TipColor.rgb, pow(saturate(input.uv.y), 0.68));
                grass *= 1 + (patch - 0.5) * _PatchStrength * 2;
                grass *= 0.97 + 0.03 * smoothstep(0.2, 0.55, input.uv.x);
                // Use the ground's world-space pigment field; geometry remains visible at silhouettes.
                float meadowPatch = PatchNoise(input.positionWS.xz * _MeadowPatchScale);
                half3 meadow = lerp(_MeadowBaseColor.rgb, _MeadowPatchColor.rgb,
                    smoothstep(0.30, 0.75, meadowPatch) * 0.65);
                meadow *= 1 + (pow(saturate(input.uv.y), 0.68) - 0.5) * _BladeContrast * 0.45;
                grass = lerp(grass, meadow, _MeadowBlend);
                half3 surface = lerp(grass, input.color.rgb, input.color.a);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half diffuse = 0.15 + smoothstep(-0.15, 0.85, dot(SafeNormalize(input.normalWS), light.direction)) * 0.75;
                half ambient = dot(max(SampleSH(half3(0, 1, 0)), 0), half3(0.2126, 0.7152, 0.0722));
                half sunlight = dot(light.color, half3(0.2126, 0.7152, 0.0722));
                half illumination = max(0.12, ambient * 0.35 + sunlight * diffuse);
                half shadow = (1 - light.shadowAttenuation) * _ShadowStrength;
                half luminance = dot(surface, half3(0.2126, 0.7152, 0.0722));
                half3 shade = lerp(surface * 0.38, _ShadowTint.rgb * (0.5 + luminance * 0.9), 0.75);
                half3 color = lerp(surface * illumination, shade, shadow);
                half groundDiffuse = smoothstep(-0.2, 0.8, light.direction.y);
                half3 groundLight = max(SampleSH(half3(0, 1, 0)), half3(.16, .16, .16)) * .65
                    + light.color * (.28 + groundDiffuse * .52);
                half3 unified = surface * groundLight * lerp(half3(1, 1, 1), _ShadowTint.rgb,
                    (1 - light.shadowAttenuation) * .55);
                color = lerp(color, unified, _MeadowBlend * (1 - input.color.a));
                return half4(MixFog(color, input.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection, _LightPosition;
            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 world = GrassPositionWS(input);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 direction = normalize(_LightPosition - world);
                #else
                    float3 direction = _LightDirection;
                #endif
                float4 clip = TransformWorldToHClip(ApplyShadowBias(world, float3(0, 1, 0), direction));
                #if UNITY_REVERSED_Z
                    clip.z = min(clip.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    clip.z = max(clip.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return clip;
            }
            half4 DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            struct DepthNormalVaryings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            DepthNormalVaryings DepthNormalsVert(Attributes input)
            {
                DepthNormalVaryings output = (DepthNormalVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                // Match the forward pass so the depth silhouette moves with every blade.
                output.positionCS = TransformWorldToHClip(GrassPositionWS(input));
                output.normalWS = SafeNormalize(lerp(TransformObjectToWorldNormal(input.normalOS),
                    half3(0, 1, 0), saturate(_UpNormals)));
                return output;
            }
            half4 DepthNormalsFrag(DepthNormalVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(normalWS);
                    return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0);
                #else
                    return half4(normalWS, 0);
                #endif
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            float4 DepthVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformWorldToHClip(GrassPositionWS(input));
            }
            half4 DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
