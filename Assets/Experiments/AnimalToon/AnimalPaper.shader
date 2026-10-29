Shader "Weather House/Animals/Paper Toon"
{
    Properties
    {
        [MainTexture] _BaseMap("Animal Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        _ShadeColor("Shade Tint", Color) = (0.62,0.66,0.72,1)
        _ShadeThreshold("Shade Threshold", Range(0,1)) = 0.48
        _BandSoftness("Band Softness", Range(0.005,0.3)) = 0.08
        _ToonStrength("Band Strength", Range(0,1)) = 0.8
        _AmbientStrength("Ambient Fill", Range(0,2)) = 0.7
        _LightStrength("Direct Light", Range(0,2)) = 0.7
        _ShadowFloor("Deep Shadow Light Floor", Range(0,0.3)) = 0
        [Toggle] _EnvironmentShadowsOnly("Environment Shadows Only", Float) = 0
        _OutlineWidth("Light Edge Width at 1080p", Range(0,16)) = 1.8
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _ShadeColor;
            half _ShadeThreshold, _BandSoftness, _ToonStrength, _AmbientStrength, _LightStrength;
            half _OutlineWidth;
            half _ShadowFloor, _EnvironmentShadowsOnly;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            half fog : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings Vert(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input,o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(input.normalOS);
            o.uv = TRANSFORM_TEX(input.uv,_BaseMap);
            o.fog = ComputeFogFactor(o.positionCS.z);
            return o;
        }
        TEXTURE2D_X(_AnimalMerged);
        float _AnimalMergeRadius;
        TEXTURE2D_SHADOW(_AnimalEnvironmentShadowmap);
        float _AnimalEnvironmentShadowReady, _AnimalEnvironmentShadowPass;
        Light PaperMainLight(float3 positionWS)
        {
            Light key = GetMainLight(TransformWorldToShadowCoord(positionWS));
            #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
            if (_EnvironmentShadowsOnly > .5 && _AnimalEnvironmentShadowReady > .5)
            {
                #if defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    half cascade = ComputeCascadeIndex(positionWS);
                #else
                    half cascade = 0;
                #endif
                float4 coord = float4(mul(_MainLightWorldToShadow[cascade],float4(positionWS,1)).xyz,0);
                key.shadowAttenuation = SampleShadowmap(TEXTURE2D_ARGS(_AnimalEnvironmentShadowmap,sampler_LinearClampCompare),
                    coord,GetMainLightShadowSamplingData(),GetMainLightShadowParams(),false);
            }
            #endif
            return key;
        }
        half4 PaperShade(Varyings input, bool merged) : SV_Target
        {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 n = normalize(input.normalWS);
                Light key = PaperMainLight(input.positionWS);
                half form = saturate(dot(n,key.direction)*0.5h+0.5h);
                if (merged)
                {
                    float4 data = SAMPLE_TEXTURE2D_X_LOD(_AnimalMerged, sampler_PointClamp, GetNormalizedScreenSpaceUV(input.positionCS), 0);
                    if (data.a > .5)
                    {
                        form = data.r;
                        key.shadowAttenuation = _AnimalMergeRadius > .01
                            ? smoothstep(.5 - _BandSoftness, .5 + _BandSoftness, data.g) : data.g;
                    }
                }
                half width = max(_BandSoftness, fwidth(form));
                half band = 0.65h * smoothstep(_ShadeThreshold-width,_ShadeThreshold+width,form)
                          + 0.35h * smoothstep(0.82h-width,0.82h+width,form);
                half tone = lerp(form,band,_ToonStrength);
                half3 ambient = max(0,SampleSH(n)) * _AmbientStrength;
                half3 direct = key.color * key.distanceAttenuation * lerp(_ShadowFloor,1,key.shadowAttenuation)
                             * lerp(_ShadeColor.rgb,half3(1,1,1),tone) * _LightStrength;
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb * _BaseColor.rgb;
                return half4(MixFog(albedo*(ambient+direct),input.fog),1);
        }
        half4 DepthFrag(Varyings input) : SV_Target { return 0; }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            // Per-sample coverage prevents a dark one-pixel seam between an MSAA model and its edge.
            Stencil { Ref 1 ReadMask 1 WriteMask 1 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            half4 Frag(Varyings input) : SV_Target
            {
                return PaperShade(input, false);
            }
            ENDHLSL
        }
        Pass
        {
            Name "AnimalPaperData"
            Tags { "LightMode"="AnimalPaperData" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DataFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            float4 DataFrag(Varyings input) : SV_Target
            {
                float eye = -TransformWorldToView(input.positionWS).z;
                float raw = SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS));
                float sceneEye = LinearEyeDepth(raw, _ZBufferParams);
                if (unity_OrthoParams.w > .5)
                {
                    #if UNITY_REVERSED_Z
                        raw = 1 - raw;
                    #endif
                    sceneEye = lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
                }
                clip(sceneEye + .015 - eye);
                Light key = PaperMainLight(input.positionWS);
                float form = saturate(dot(normalize(input.normalWS), key.direction) * .5 + .5);
                return float4(form, key.shadowAttenuation, eye, 1 + _OutlineWidth);
            }
            ENDHLSL
        }
        Pass
        {
            Name "AnimalPaperComposite"
            Tags { "LightMode"="AnimalPaperComposite" }
            ZWrite Off ZTest Equal
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CompositeFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            half4 CompositeFrag(Varyings input) : SV_Target { return PaperShade(input, true); }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            half4 ShadowFrag(Varyings input) : SV_Target
            {
                // The ordinary atlas still contains animals for ground and other receivers.
                clip(.5 - _AnimalEnvironmentShadowPass * _EnvironmentShadowsOnly);
                return 0;
            }
            float3 _LightDirection, _LightPosition;
            Varyings ShadowVert(Attributes input)
            {
                Varyings o=Vert(input);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection=normalize(_LightPosition-o.positionWS);
                #else
                    float3 lightDirection=_LightDirection;
                #endif
                o.positionCS=TransformWorldToHClip(ApplyShadowBias(o.positionWS,o.normalWS,lightDirection));
                #if UNITY_REVERSED_Z
                    o.positionCS.z=min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z=max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
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
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment NormalFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalFrag(Varyings input) : SV_Target
            {
                float3 normal=normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(normal)*0.5+0.5)),0);
                #else
                    return half4(normal,0);
                #endif
            }
            ENDHLSL
        }
    }
}
