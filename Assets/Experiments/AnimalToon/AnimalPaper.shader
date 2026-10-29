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
        _OutlineColor("Light Edge Color", Color) = (1,0.94,0.78,1)
        _OutlineWidth("Light Edge Width at 1080p", Range(0,5)) = 1.8
        _EdgeThreshold("Light Edge Threshold", Range(0,0.8)) = 0.12
        _EdgeSoftness("Light Edge Softness", Range(0.01,0.5)) = 0.2
        [Toggle] _SmoothOutline("Baked Smooth Outline Normals", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _ShadeColor, _OutlineColor;
            half _ShadeThreshold, _BandSoftness, _ToonStrength, _AmbientStrength, _LightStrength;
            half _OutlineWidth, _EdgeThreshold, _EdgeSoftness, _SmoothOutline;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            float3 smoothNormalTS : TEXCOORD3;
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
        half4 DepthFrag(Varyings input) : SV_Target { return 0; }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
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
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 n = normalize(input.normalWS);
                Light key = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half form = saturate(dot(n,key.direction)*0.5h+0.5h);
                half width = max(_BandSoftness, fwidth(form));
                half band = 0.65h * smoothstep(_ShadeThreshold-width,_ShadeThreshold+width,form)
                          + 0.35h * smoothstep(0.82h-width,0.82h+width,form);
                half tone = lerp(form,band,_ToonStrength);
                half3 ambient = max(0,SampleSH(n)) * _AmbientStrength;
                half3 direct = key.color * key.distanceAttenuation * key.shadowAttenuation
                             * lerp(_ShadeColor.rgb,half3(1,1,1),tone) * _LightStrength;
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb * _BaseColor.rgb;
                return half4(MixFog(albedo*(ambient+direct),input.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "AnimalLightOutline"
            Tags { "LightMode"="AnimalLightOutline" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            // Keep this as an outer silhouette, without bright shell seams across the face.
            Stencil { Ref 1 ReadMask 1 WriteMask 0 Comp NotEqual Pass Keep }
            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            Varyings OutlineVert(Attributes input)
            {
                Varyings o=Vert(input);
                float3 normal = normalize(o.normalWS);
                if (_SmoothOutline > 0.5h)
                {
                    float3 tangent = TransformObjectToWorldDir(input.tangentOS.xyz);
                    tangent = SafeNormalize(tangent-normal*dot(tangent,normal));
                    float3 bitangent = cross(normal,tangent)*input.tangentOS.w*GetOddNegativeScale();
                    normal=SafeNormalize(tangent*input.smoothNormalTS.x+bitangent*input.smoothNormalTS.y+normal*input.smoothNormalTS.z);
                }
                float depth = max(0.01,abs(TransformWorldToView(o.positionWS).z));
                float pixelsPerMetre = 0.5*abs(UNITY_MATRIX_P._m11)*_ScaledScreenParams.y;
                pixelsPerMetre /= unity_OrthoParams.w > 0.5 ? 1.0 : depth;
                float width = _OutlineWidth * _ScaledScreenParams.y / 1080.0;
                // Preserve the source position for shadow lookup; only the shell moves.
                o.positionCS=TransformWorldToHClip(o.positionWS+normal*width/max(pixelsPerMetre,0.001));
                o.normalWS=normal;
                return o;
            }
            half4 OutlineFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(_OutlineWidth-0.001h);
                Light key=GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half facing=dot(normalize(input.normalWS),key.direction);
                half softness=max(_EdgeSoftness,fwidth(facing));
                half edge=smoothstep(_EdgeThreshold-softness,_EdgeThreshold+softness,facing);
                half energy=saturate(max(key.color.r,max(key.color.g,key.color.b)));
                half alpha=edge*key.shadowAttenuation*key.distanceAttenuation*energy*_OutlineColor.a;
                return half4(MixFog(_OutlineColor.rgb,input.fog),alpha);
            }
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
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
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
