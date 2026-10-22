Shader "Weather House/Author Foliage Static"
{
    Properties
    {
        _LeavesAlpha("Author LeavesAlpha (RGB mask)", 2D) = "black" {}
        _ColorVariationNoise("Original TilingNoise05 (required)", 2D) = "black" {}
        _TopColor("Author TopColor (linear RGBA)", Vector) = (.321569,.137255,.019608,1)
        _NoiseColor("Author NoiseColor (linear RGBA)", Vector) = (.133333,.011765,.023529,1)
        _SubsurfaceColor("Author SubsurfaceColor (linear RGBA)", Vector) = (.337255,.082353,0,1)
        _LeavesSize("Author LeavesSize (centimetres)", Float) = 25
        _IsFluffy("Author isFluffy (0 or 1)", Range(0,1)) = 1
        _TopColorGradientHeight("Author TopColorGradientHeight", Float) = 1
        _TopColorGradientWidth("Author TopColorGradientWidth", Float) = .5
        _ColorVariationNoiseTiling("Author noise world period (centimetres)", Float) = 2000
        _Cutoff("Author opacity mask clip", Range(0,1)) = .3333
        _Opacity("Author subsurface Opacity", Range(0,1)) = .05
        [ToggleUI] _ApproximateSubsurface("URP scattering approximation (not UE Subsurface)", Float) = 1
        [HideInInspector] _UEWorldX("UE world X in Unity coordinates", Vector) = (0,0,1,0)
        [HideInInspector] _UEWorldY("UE world Y in Unity coordinates", Vector) = (1,0,0,0)
        [HideInInspector] _UEWorldZ("UE world Z in Unity coordinates", Vector) = (0,1,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        ZWrite On

        HLSLINCLUDE
        #define _SPECULAR_SETUP 1
        #define _SPECULARHIGHLIGHTS_OFF 1
        #define _ENVIRONMENTREFLECTIONS_OFF 1
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_LeavesAlpha); SAMPLER(sampler_LeavesAlpha);
        TEXTURE2D(_ColorVariationNoise); SAMPLER(sampler_ColorVariationNoise);
        CBUFFER_START(UnityPerMaterial)
            float4 _TopColor, _NoiseColor, _SubsurfaceColor;
            float4 _UEWorldX, _UEWorldY, _UEWorldZ;
            float _LeavesSize, _IsFluffy, _TopColorGradientHeight, _TopColorGradientWidth;
            float _ColorVariationNoiseTiling, _Cutoff, _Opacity, _ApproximateSubsurface;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            half fog : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        float3 ToAuthorWorld(float3 unityVector)
        {
            return float3(dot(unityVector,_UEWorldX.xyz), dot(unityVector,_UEWorldY.xyz), dot(unityVector,_UEWorldZ.xyz));
        }
        float3 FromAuthorWorld(float3 authorVector)
        {
            return _UEWorldX.xyz * authorVector.x + _UEWorldY.xyz * authorVector.y + _UEWorldZ.xyz * authorVector.z;
        }
        float3 AuthorPositionWS(Attributes input, VertexNormalInputs frame)
        {
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            if (_IsFluffy > .5)
            {
                // UE FBX export writes (U,1-V); only procedural WPO needs the original UE UV.
                // Texture sampling intentionally retains Unity's imported UV below.
                float2 authorUV = float2(input.uv.x,1 - input.uv.y);
                float2 face = authorUV * 2 - 1;
                // UE's FBX exporter also negates the binormal for that V flip.
                // Preserve imported tangents and undo it only in this procedural UE frame.
                float3 authorBitangentWS = -frame.bitangentWS;
                float3 firstWS = frame.tangentWS * face.x + authorBitangentWS * face.y;
                // The second TransformVector deliberately reinterprets UE world XYZ as tangent XYZ.
                float3 firstAuthor = ToAuthorWorld(firstWS);
                float3 secondWS = frame.tangentWS * firstAuthor.x + authorBitangentWS * firstAuthor.y + frame.normalWS * firstAuthor.z;
                float3 scale = float3(length(mul((float3x3)unity_ObjectToWorld,_UEWorldX.xyz)),
                                     length(mul((float3x3)unity_ObjectToWorld,_UEWorldY.xyz)),
                                     length(mul((float3x3)unity_ObjectToWorld,_UEWorldZ.xyz)));
                float3 offsetAuthor = ToAuthorWorld(secondWS) * (_LeavesSize * .01) * scale;
                positionWS += FromAuthorWorld(offsetAuthor);
            }
            // Static study: F_BendWind and SimpleGrassWind are both zero; hasWeightPaint is false in all three instances.
            return positionWS;
        }
        void AuthorClip(float2 uv)
        {
            // UE connects texture RGB to a scalar mask. These exports have no alpha channel.
            clip(SAMPLE_TEXTURE2D(_LeavesAlpha,sampler_LeavesAlpha,uv).r - _Cutoff);
        }
        half3 AuthorBaseColor(Varyings input)
        {
            float top = (dot(input.normalWS,_UEWorldZ.xyz) + _TopColorGradientHeight) * .5;
            // UE 5.7 Power compiles to PositiveClampedPow, not abs(base) followed by pow.
            float gradient = top <= 2.980233e-8 ? 0 : pow(max(top,2.980233e-8),_TopColorGradientWidth);
            float2 noiseUV = ToAuthorWorld(input.positionWS).xy * (100 / max(_ColorVariationNoiseTiling,1.e-4));
            noiseUV.y = 1 - noiseUV.y;
            half3 noise = SAMPLE_TEXTURE2D(_ColorVariationNoise,sampler_ColorVariationNoise,noiseUV).rgb;
            return lerp(_TopColor.rgb * gradient,_NoiseColor.rgb,noise);
        }
        Varyings AuthorVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input,output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexNormalInputs frame = GetVertexNormalInputs(input.normalOS,input.tangentOS);
            output.positionWS = AuthorPositionWS(input,frame);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = frame.normalWS;
            output.uv = input.uv;
            output.fog = ComputeFogFactor(output.positionCS.z);
            return output;
        }
        half4 AuthorDepth(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            AuthorClip(input.uv);
            return 0;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex AuthorVertex
            #pragma fragment AuthorForward
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            half4 AuthorForward(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                AuthorClip(input.uv);
                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                // UE's (0,0,TwoSidedSign) cancels its automatic back-face normal flip.
                // This URP path keeps the imported normal on both sides and applies no additional face flip.
                lighting.normalWS = NormalizeNormalPerPixel(input.normalWS);
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                lighting.bakedGI = SampleSH(lighting.normalWS);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1,1,1,1);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = AuthorBaseColor(input);
                surface.specular = 0;
                surface.smoothness = .5;
                surface.normalTS = half3(0,0,1);
                surface.occlusion = 1;
                surface.alpha = 1;
                half3 color = UniversalFragmentPBR(lighting,surface).rgb;

                // shortcut: approximate UE 5.7 SubsurfaceBxDF's wrap/view terms for the main light only;
                // URP provides neither its transmission thickness nor its extinction/hue-shift evaluation here.
                Light sun = GetMainLight(lighting.shadowCoord,input.positionWS,lighting.shadowMask);
                half wrap = pow(saturate((dot(lighting.normalWS,sun.direction) + .5) / 1.5),1.5) * (2.5 / 1.5);
                half back = lerp(1,wrap,_Opacity) / (2 * PI);
                half through = pow(saturate(dot(sun.direction,-lighting.viewDirectionWS)),12) * lerp(3,.1,_Opacity);
                // The scene converts UE directional intensity to URP with intensity / PI.
                // This term already contains UE's 1/(2*PI), so restore PI here to avoid converting it twice.
                // Thin-leaf approximation: URP has no independent transmission-thickness/shadow buffer.
                // The author's low Opacity lets scattered light cross a leaf even under an opaque surface shadow;
                // this opacity blend is a comparison candidate, not UE Subsurface parity.
                half transmissionShadow = lerp(1,sun.shadowAttenuation,_Opacity);
                color += _ApproximateSubsurface * _SubsurfaceColor.rgb * lerp(back,1,through)
                         * sun.color * PI * sun.distanceAttenuation * transmissionShadow;
                return half4(MixFog(color,input.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex AuthorShadowVertex
            #pragma fragment AuthorDepth
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection, _LightPosition;
            Varyings AuthorShadowVertex(Attributes input)
            {
                Varyings output = AuthorVertex(input);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 direction = normalize(_LightPosition - output.positionWS);
                #else
                    float3 direction = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS,output.normalWS,direction));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z,UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
                #else
                    output.positionCS.z = max(output.positionCS.z,UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
                #endif
                return output;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex AuthorVertex
            #pragma fragment AuthorDepth
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex AuthorVertex
            #pragma fragment AuthorNormals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            half4 AuthorNormals(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                AuthorClip(input.uv);
                float3 normal = NormalizeNormalPerPixel(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(normal);
                    return half4(PackFloat2To888(saturate(oct * .5 + .5)),0);
                #else
                    return half4(normal,0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
