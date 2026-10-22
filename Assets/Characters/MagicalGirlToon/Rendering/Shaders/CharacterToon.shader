Shader "ZZZ/Character Toon"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _ShadeColor("Ambient Shadow Tint", Color) = (0.62, 0.68, 0.82, 1)
        _ShadeThreshold("Shade Threshold", Range(0, 1)) = 0.48
        _ShadeSoftness("Shade Softness", Range(0.001, 0.5)) = 0.08
        _FormStrength("Continuous Form", Range(0, 1)) = 0.75
        _DirectStrength("Direct Light Strength", Range(0, 2)) = 0.85
        _FillStrength("Environment Fill Strength", Range(0, 2)) = 0.55
        _ShadowStrength("Cast Shadow Strength", Range(0, 1)) = 0.75
        _EnvironmentShadowStrength("Environment Shadow Strength", Range(0, 1)) = 0.65
        _AdditionalLightStrength("Additional Light Strength", Range(0, 2)) = 0.35
        _AdditionalLightLimit("Additional Light Brightness Limit", Range(0, 2)) = 0.5
        _OcclusionStrength("Ambient Occlusion Strength", Range(0, 1)) = 0.55
        _SpecColor("Specular Color", Color) = (1, 0.96, 0.9, 1)
        _SpecThreshold("Specular Threshold", Range(0, 1)) = 0.84
        _SpecSoftness("Specular Softness", Range(0.001, 0.5)) = 0.08
        _SpecStrength("Specular Strength", Range(0, 2)) = 0.12
        _SpecPower("Specular Power", Range(8, 256)) = 96
        _RimColor("Rim Color", Color) = (0.9, 0.94, 1, 1)
        _RimPower("Rim Power", Range(1, 12)) = 4
        _RimStrength("Rim Strength", Range(0, 2)) = 0.12
        _OutlineColor("Outline Color", Color) = (0.18, 0.18, 0.20, 1)
        _OutlineWidth("Outline Width (Pixels)", Range(0, 10)) = 1.35
        _OutlineDistance("Outline Full Width Distance (1080p, 39 deg)", Range(0.1, 10)) = 1.25
        _OutlineIntersectionFade("Outline Contact Fade (Metres)", Range(0, 0.01)) = 0
        [HideInInspector] _NativeFaceOutlineMask("Native Face Interior Outline Mask", Float) = 0
        [ToggleUI] _FaceMode("Face Lighting", Float) = 0
        [ToggleUI] _HairMode("Hair Lighting", Float) = 0
        _HairControlMap("Hair Fold and Highlight Controls", 2D) = "black" {}
        _BodyMode("Body Lighting (0 Off, 1 Body, 2 Gauze)", Float) = 0
        [ToggleUI] _UseSkinOverlay("Merge Skin Overlay", Float) = 0
        _SkinOverlayMap("Skin Overlay", 2D) = "black" {}
        [ToggleUI] _CastShadows("Cast Dynamic Shadows", Float) = 1
        _FaceHairShadowMap("Front Hair Face Shadow", 2D) = "black" {}
        _FaceHairShadowStrength("Front Hair Shadow Strength", Range(0, 1)) = 1
        _FaceHairShadowSpread("Front Hair Shadow Spread", Range(0, 1)) = 0.11
        _FaceNoseShift("Nose UV Alignment", Range(-0.02, 0.02)) = 0.00714
        _FaceShadowColor("Cheek Shadow Color", Color) = (0.86, 0.68, 0.64, 1)
        _FaceShadowStrength("Cheek Shadow Strength", Range(0, 1)) = 0.55
        [Header(Optional Authored Lighting Inputs)]
        _FaceSDFMap("Face Threshold R / Region A (Linear)", 2D) = "black" {}
        _FaceSDFStrength("Authored Face Threshold Strength", Range(0, 1)) = 0
        _FaceSDFSoftness("Face Threshold Feather", Range(0.001, 0.15)) = 0.025
        [ToggleUI] _FaceSDFSwapSides("Face Threshold Swap Light Sides", Float) = 0
        _DiffuseRampMap("Diffuse Tint Ramp (Dark Left / Lit Right)", 2D) = "white" {}
        _DiffuseRampStrength("Diffuse Tint Ramp Strength", Range(0, 1)) = 0
        _StylizedMaskMap("Authored Mask R Ambient / G Spec / B Ramp", 2D) = "white" {}
        _StylizedMaskStrength("Authored Mask Strength (Not Source M Layout)", Range(0, 1)) = 0
        [Enum(CharacterLightingInputDebug)] _StylizedInputDebug("Lighting Input Channel Preview", Float) = 0
        [Header(Native Packed Surface Inputs)]
        _PackedNormalMap("Source N (RG Normal / B Diffuse Bias)", 2D) = "gray" {}
        _PackedNormalStrength("Source Normal Strength", Range(0, 1)) = 0
        _SourceMaterialMap("Source M (R ID / G Metallic / B Specular)", 2D) = "white" {}
        _SourceAttributeMap("Source A (R Hair Stencil-Fade / G Smoothness / B Emission)", 2D) = "white" {}
        _SourceInputsStrength("Source Material Controls", Range(0, 1)) = 0
        _SourceSlot1("Source Slot 1 (M.R >= 0.8) Tint RGB / Spec A", Color) = (1, 1, 1, 1)
        _SourceSlot2("Source Slot 2 (0.6 <= M.R < 0.8) Tint RGB / Spec A", Color) = (1, 1, 1, 1)
        _SourceSlot3("Source Slot 3 (0.4 <= M.R < 0.6) Tint RGB / Spec A", Color) = (1, 1, 1, 1)
        _SourceSlot4("Source Slot 4 (0.2 <= M.R < 0.4) Tint RGB / Spec A", Color) = (1, 1, 1, 1)
        _SourceSlot5("Source Slot 5 (M.R < 0.2) Tint RGB / Spec A", Color) = (1, 1, 1, 1)
        [Header(Local Self Emission Approximation)]
        [HDR] _SourceEmissionColor("Source A.B Emission Color", Color) = (1, 1, 1, 1)
        _SourceEmissionStrength("Source A.B Emission Strength", Range(0, 8)) = 0
        [Header(Skin Scatter Approximation)]
        _SkinScatterStrength("Skin Scatter Strength", Range(0, 1)) = 0
        _SkinScatterRadius("Skin Scatter Angular Spread", Range(0.05, 1)) = 0.35
        _SkinScatterColor("Skin Scatter Tint", Color) = (1, 0.65, 0.5, 1)
        _HairCreaseStrength("Hair Fold Strength", Range(0, 1)) = 0.65
        _HairCreaseColor("Hair Fold Color", Color) = (0.47, 0.41, 0.50, 1)
        _HairSpecColor("Hair Band Color", Color) = (0.83, 0.76, 0.63, 1)
        _HairSpecWidth("Hair Band Width", Range(0.05, 0.8)) = 0.32
        _HairSpecShift("Hair Band Shift", Range(-1, 1)) = 0.1
        _HairSpecStrength("Hair Band Strength", Range(0, 2)) = 0.18
        _SkinShadowStrength("Skin Form Shadows", Range(0, 1)) = 0.18
        _SkinContactStrength("Skin Contact Shadows", Range(0, 1)) = 0
        _SkinShadeColor("Skin Soft Shadow Color", Color) = (0.78, 0.74, 0.75, 1)
        _SkinDeepShadeColor("Skin Deep Shadow Color", Color) = (0.71, 0.655, 0.67, 1)
        _SkinContactColor("Skin Occlusion Color", Color) = (0.61, 0.54, 0.56, 1)
        _SkinShadeThreshold("Skin Soft Shadow Threshold", Range(0, 1)) = 0.70
        _SkinDeepThreshold("Skin Deep Shadow Threshold", Range(0, 1)) = 0.46
        _SkinNormalStrength("Skin Normal Smoothing", Range(0, 1)) = 1
        _BodyDetailStrength("Garment Fold Strength", Range(0, 1)) = 0.35
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [ToggleUI] _AlphaClip("Alpha Clip", Float) = 1
        [ToggleUI] _AlphaToMask("Alpha To Coverage (Opaque Cutout)", Float) = 0
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _SrcBlend("Source Blend", Float) = 1
        [HideInInspector] _DstBlend("Destination Blend", Float) = 0
        [HideInInspector] _ZWrite("Z Write", Float) = 1
        [HideInInspector] _Surface("Surface", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_SkinOverlayMap);
        SAMPLER(sampler_SkinOverlayMap);
        TEXTURE2D(_HairControlMap);
        SAMPLER(sampler_HairControlMap);
        TEXTURE2D(_FaceHairShadowMap);
        SAMPLER(sampler_FaceHairShadowMap);
        TEXTURE2D(_FaceSDFMap);
        SAMPLER(sampler_FaceSDFMap);
        TEXTURE2D(_DiffuseRampMap);
        SAMPLER(sampler_DiffuseRampMap);
        TEXTURE2D(_StylizedMaskMap);
        SAMPLER(sampler_StylizedMaskMap);
        TEXTURE2D(_PackedNormalMap);
        SAMPLER(sampler_PackedNormalMap);
        TEXTURE2D(_SourceMaterialMap);
        SAMPLER(sampler_SourceMaterialMap);
        TEXTURE2D(_SourceAttributeMap);
        SAMPLER(sampler_SourceAttributeMap);
        float4x4 _HeadWorldToLocal;
        float4 _CharacterLightProbeWS;
        #include "CharacterFaceLighting.hlsl"
        #include "CharacterStylizedInputs.hlsl"
        #include "CharacterOutline.hlsl"
        #include "CharacterSurfaceInputs.hlsl"

        // Every pass uses the same layout; borrowing Lit passes breaks SRP batching.
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _ShadeColor;
            half4 _SpecColor;
            half4 _RimColor;
            half4 _OutlineColor;
            half _ShadeThreshold;
            half _ShadeSoftness;
            half _FormStrength;
            half _DirectStrength;
            half _FillStrength;
            half _ShadowStrength;
            half _EnvironmentShadowStrength, _AdditionalLightStrength, _AdditionalLightLimit;
            half _OcclusionStrength;
            half _SpecThreshold;
            half _SpecSoftness;
            half _SpecStrength;
            half _SpecPower;
            half _RimPower;
            half _RimStrength;
            half _OutlineWidth;
            half _OutlineDistance;
            half _OutlineIntersectionFade;
            half _NativeFaceOutlineMask;
            half _Cutoff;
            half _AlphaClip;
            half _FaceMode, _HairMode, _BodyMode, _UseSkinOverlay, _CastShadows, _Surface;
            half _FaceNoseShift, _FaceShadowStrength;
            half _FaceSDFStrength, _FaceSDFSoftness, _FaceSDFSwapSides;
            half _DiffuseRampStrength, _StylizedMaskStrength, _StylizedInputDebug;
            half _PackedNormalStrength, _SourceInputsStrength;
            half4 _SourceSlot1, _SourceSlot2, _SourceSlot3, _SourceSlot4, _SourceSlot5;
            half4 _SourceEmissionColor;
            half _SourceEmissionStrength;
            half _SkinScatterStrength, _SkinScatterRadius;
            half4 _SkinScatterColor;
            half _FaceHairShadowStrength, _FaceHairShadowSpread;
            half4 _FaceShadowColor, _HairCreaseColor, _HairSpecColor;
            half _HairCreaseStrength, _HairSpecWidth, _HairSpecShift, _HairSpecStrength;
            half _SkinShadowStrength, _SkinContactStrength, _BodyDetailStrength;
            half4 _SkinShadeColor, _SkinDeepShadeColor, _SkinContactColor;
            half _SkinShadeThreshold, _SkinDeepThreshold, _SkinNormalStrength;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float4 tangentOS : TANGENT;
            float4 outlineControl : TEXCOORD7;
            float4 bodyControl : TEXCOORD4;
            float4 bodyBind : TEXCOORD5;
            float4 bodySmoothNormal : TEXCOORD6;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            half3 skinNormalWS : TEXCOORD3;
            half outlineCoverage : TEXCOORD4;
            float4 bodyControl : TEXCOORD5;
            float4 bodyBind : TEXCOORD6;
            half fogFactor : TEXCOORD7;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = position.positionCS;
            output.positionWS = position.positionWS;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.bodyControl = input.bodyControl;
            output.bodyBind = input.bodyBind;
            output.skinNormalWS = CharacterOutlineNormalWS(input.normalOS, input.tangentOS, input.bodySmoothNormal);
            output.fogFactor = ComputeFogFactor(output.positionCS.z);
            return output;
        }

        half4 SampleCharacter(float2 uv)
        {
            if (_FaceMode > 0.5h) uv = CharacterFaceUV(uv, _FaceNoseShift);
            half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
            if (_UseSkinOverlay > 0.5h)
            {
                half4 skin = SAMPLE_TEXTURE2D(_SkinOverlayMap, sampler_SkinOverlayMap, uv);
                albedo.rgb = lerp(albedo.rgb, skin.rgb * _BaseColor.rgb, skin.a);
            }
            if (_AlphaClip > 0.5h)
                clip(albedo.a - _Cutoff);
            else if (_Surface < 0.5h)
                albedo.a = 1.0h;
            return albedo;
        }
        ENDHLSL

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            AlphaToMask [_AlphaToMask]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CharacterHairLighting.hlsl"
            #include "CharacterBodyLighting.hlsl"

            half CharacterEnvironmentShadow(float3 positionWS, Light key)
            {
                if (_CharacterLightProbeWS.w <= 0.0) return 1.0h;
                // Receive occlusion locally: a head-bone sample would darken the entire character at once.
                // Retain the authored near-surface offset to suppress fine self shadows. This is not caster filtering.
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    // Screen shadows describe the visible surface, not an arbitrary offset world-space point.
                    return key.shadowAttenuation;
                #endif
                float3 probe = positionWS + key.direction * (0.22 * _CharacterLightProbeWS.w);
                return GetMainLight(TransformWorldToShadowCoord(probe), probe, half4(1, 1, 1, 1)).shadowAttenuation;
            }

            half3 CharacterFaceLight(float3 positionHead, float2 uv, half3 lightHead)
            {
                half faceShadow = CharacterFaceShadow(positionHead, lightHead);
                if (_FaceSDFStrength > 0.0h)
                    faceShadow = CharacterFaceThresholdShadow(uv, lightHead, faceShadow,
                        TEXTURE2D_ARGS(_FaceSDFMap, sampler_FaceSDFMap),
                        _FaceSDFStrength, _FaceSDFSoftness, _FaceSDFSwapSides);
                half noseShadow = CharacterFaceNoseShadow(positionHead, lightHead);
                half hairShadow = CharacterFaceHairShadow(positionHead, lightHead,
                    TEXTURE2D_ARGS(_FaceHairShadowMap, sampler_FaceHairShadowMap), _FaceHairShadowSpread)
                    * _FaceHairShadowStrength;
                half form = min(lerp(0.96h, 0.68h, faceShadow), lerp(0.96h, 0.52h, noseShadow));
                return half3(form, max(faceShadow, noseShadow), hairShadow);
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 albedo = SampleCharacter(input.uv);
                if (_StylizedInputDebug > 0.5h)
                {
                    if (_StylizedInputDebug > 6.5h)
                    {
                        half id = SAMPLE_TEXTURE2D(_SourceMaterialMap, sampler_SourceMaterialMap, input.uv).r;
                        half4 slotColour = CharacterSourceSlotParameters(id, half4(1, 0, 0, 1), half4(0, 1, 0, 1),
                            half4(0, 0, 1, 1), half4(1, 1, 1, 1), half4(0, 0, 0, 1));
                        return half4(slotColour.rgb, albedo.a);
                    }
                    half channel;
                    if (_StylizedInputDebug < 2.5h)
                    {
                        half4 data = SAMPLE_TEXTURE2D(_FaceSDFMap, sampler_FaceSDFMap, input.uv);
                        channel = _StylizedInputDebug < 1.5h ? data.r : data.a;
                    }
                    else
                    {
                        half4 data = SAMPLE_TEXTURE2D(_StylizedMaskMap, sampler_StylizedMaskMap, input.uv);
                        channel = _StylizedInputDebug < 3.5h ? data.r :
                            (_StylizedInputDebug < 4.5h ? data.g : (_StylizedInputDebug < 5.5h ? data.b : data.a));
                    }
                    return half4(channel, channel, channel, albedo.a);
                }
                half4 stylizedMask = half4(1, 1, 1, 1);
                if (_StylizedMaskStrength > 0.0h)
                    stylizedMask = CharacterStylizedMask(input.uv,
                        TEXTURE2D_ARGS(_StylizedMaskMap, sampler_StylizedMaskMap), _StylizedMaskStrength);
                half3 normal = normalize(input.normalWS) * IS_FRONT_VFACE(frontFace, 1.0h, -1.0h);
                half3 sourceNormal = half3(0.5h, 0.5h, 0.5h);
                if (_PackedNormalStrength > 0.0h || _SourceInputsStrength > 0.0h)
                    sourceNormal = SAMPLE_TEXTURE2D(_PackedNormalMap, sampler_PackedNormalMap, input.uv).rgb;
                normal = CharacterPackedNormalWS(input.positionWS, input.uv, normal, sourceNormal.rg, _PackedNormalStrength);
                half sourceBias = (sourceNormal.b - 0.5h) * _SourceInputsStrength;
                half3 sourceMaterial = half3(1, 0, 1);
                half3 sourceAttributes = half3(1, 0.5h, 0);
                half smoothness = 0.5h;
                if (_SourceInputsStrength > 0.0h)
                {
                    sourceMaterial = SAMPLE_TEXTURE2D(_SourceMaterialMap, sampler_SourceMaterialMap, input.uv).rgb;
                    sourceAttributes = SAMPLE_TEXTURE2D(_SourceAttributeMap, sampler_SourceAttributeMap, input.uv).rgb;
                    smoothness = sourceAttributes.g;
                }
                half4 sourceSlot = lerp(half4(1, 1, 1, 1), CharacterSourceSlotParameters(sourceMaterial.r,
                    _SourceSlot1, _SourceSlot2, _SourceSlot3, _SourceSlot4, _SourceSlot5), _SourceInputsStrength);
                albedo.rgb *= sourceSlot.rgb;
                half3 view = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));

                // Evaluate per pixel so triangles crossing cascade boundaries sample the correct map.
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light key = GetMainLight(shadowCoord, input.positionWS, half4(1, 1, 1, 1));
                half environmentShadow = CharacterEnvironmentShadow(input.positionWS, key);
                half ndotl = dot(normal, key.direction);
                half wrappedLight = saturate(ndotl * 0.5h + 0.5h + sourceBias);
                half band = smoothstep(_ShadeThreshold - _ShadeSoftness,
                                       _ShadeThreshold + _ShadeSoftness, wrappedLight);
                half form = lerp(band, wrappedLight, _FormStrength);
                half shadow = lerp(1.0h, key.shadowAttenuation, _ShadowStrength);
                shadow = min(shadow, lerp(1.0h, environmentShadow, _EnvironmentShadowStrength));
                half3 direct = key.color * (key.distanceAttenuation * shadow * _DirectStrength);

                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(input.positionCS));
                half ambientOcclusion = lerp(1.0h, ao.indirectAmbientOcclusion, _OcclusionStrength);
                ambientOcclusion *= stylizedMask.r;
                half3 fill = SampleSH(normal) * _ShadeColor.rgb * (_FillStrength * ambientOcclusion);
                float3 positionHead = mul(_HeadWorldToLocal, float4(input.positionWS, 1)).xyz;
                half3 lightHead = SafeNormalize(mul((float3x3)_HeadWorldToLocal, key.direction));
                half faceShadow = 0;
                half faceHairShadow = 0;
                CharacterHairLighting hair = (CharacterHairLighting)0;
                CharacterHairSettings hairSettings = (CharacterHairSettings)0;
                half4 hairControl = 0;
                float3 strand = 0;
                if (_FaceMode > 0.5h)
                {
                    half3 faceLight = CharacterFaceLight(positionHead, input.uv, lightHead);
                    form = faceLight.x;
                    faceShadow = faceLight.y;
                    faceHairShadow = faceLight.z;
                }
                if (_HairMode > 0.5h)
                {
                    hairSettings.shadeThreshold = _ShadeThreshold;
                    hairSettings.shadeSoftness = _ShadeSoftness;
                    hairSettings.formStrength = _FormStrength;
                    hairSettings.creaseStrength = _HairCreaseStrength;
                    hairSettings.specWidth = _HairSpecWidth;
                    hairSettings.specShift = _HairSpecShift;
                    hairSettings.specStrength = _HairSpecStrength;
                    hairSettings.diffuseBias = sourceBias;
                    hairControl = SAMPLE_TEXTURE2D(_HairControlMap, sampler_HairControlMap, input.uv);
                    strand = CharacterHairDirectionV(input.positionWS, input.uv, normal);
                    hair = EvaluateCharacterHair(normal, strand, view, key.direction, hairControl, hairSettings);
                    form = hair.form;
                }
                half3 diffuse = albedo.rgb * (form * direct + fill);
                half3 additionalNormal = normal;
                half3 additionalAlbedo = albedo.rgb;
                half scatterMask = _FaceMode > 0.5h ? 1.0h : 0.0h;
                if (_FaceMode > 0.5h)
                {
                    diffuse *= lerp(half3(1,1,1), _FaceShadowColor.rgb, faceShadow * _FaceShadowStrength);
                    diffuse *= lerp(half3(1,1,1), _SkinShadeColor.rgb, faceHairShadow);
                }
                if (_HairMode > 0.5h)
                    diffuse *= lerp(half3(1,1,1), _HairCreaseColor.rgb, hair.contact);
                if (_BodyMode > 0.5h)
                {
                    half skinMask = _UseSkinOverlay > 0.5h
                        ? SAMPLE_TEXTURE2D(_SkinOverlayMap, sampler_SkinOverlayMap, input.uv).a
                        : input.bodyControl.x;
                    half3 skinNormal = normalize(input.skinNormalWS) * IS_FRONT_VFACE(frontFace, 1.0h, -1.0h);
                    skinNormal = CharacterPackedNormalWS(input.positionWS, input.uv, skinNormal, sourceNormal.rg, _PackedNormalStrength);
                    diffuse = CharacterBodyDiffuse(diffuse, albedo.rgb, normal, skinNormal, key, ambientOcclusion,
                        skinMask, input.bodyControl, input.bodyBind, input.uv, view, albedo.a, environmentShadow, sourceBias, sourceSlot.rgb);
                    additionalNormal = normalize(lerp(normal, skinNormal, saturate(skinMask) * _SkinNormalStrength));
                    scatterMask = _BodyMode < 1.5h ? saturate(skinMask) : 0.0h;
                    if (_BodyMode > 1.5h)
                        additionalAlbedo = lerp(albedo.rgb, half3(0.94h, 0.97h, 1.0h) * sourceSlot.rgb,
                            smoothstep(1.148, 1.164, input.bodyBind.y));
                }

                if (_DiffuseRampStrength > 0.0h)
                {
                    half rampLight = _FaceMode > 0.5h ? 1.0h - faceShadow :
                        (_HairMode > 0.5h ? form : saturate(dot(additionalNormal, key.direction) * 0.5h + 0.5h));
                    diffuse *= CharacterDiffuseRamp(rampLight,
                        TEXTURE2D_ARGS(_DiffuseRampMap, sampler_DiffuseRampMap), _DiffuseRampStrength * stylizedMask.b);
                }

                half3 halfVector = SafeNormalize(key.direction + view);
                half ndoth = saturate(dot(normal, halfVector));
                half specPower = lerp(_SpecPower, exp2(3.0h + 5.0h * smoothness), _SourceInputsStrength);
                half spec = pow(ndoth, specPower) * smoothstep(_SpecThreshold - _SpecSoftness,
                                                               _SpecThreshold + _SpecSoftness, ndoth);
                spec *= saturate(ndotl) * _SpecStrength;
                half rim = pow(1.0h - saturate(dot(normal, view)), _RimPower);
                rim *= saturate(ndotl) * _RimStrength;
                half metallic = saturate(sourceMaterial.g) * _SourceInputsStrength;
                half3 specColor = lerp(_SpecColor.rgb, albedo.rgb, metallic);
                half sourceSpecMask = lerp(1.0h, sourceMaterial.b, _SourceInputsStrength);
                // Keep the accepted painted hair band; source M adds its separate surface highlights.
                half3 specular = _HairMode > 0.5h
                    ? hair.spec * _HairSpecColor.rgb + spec * specColor * sourceMaterial.b * _SourceInputsStrength
                    : spec * specColor * sourceSpecMask;
                specular *= stylizedMask.g * sourceSlot.a;
                diffuse *= 1.0h - metallic * 0.5h;
                half3 color = diffuse + direct * (specular + rim * _RimColor.rgb * albedo.rgb);
                if (_SourceInputsStrength > 0.0h)
                {
                    half3 reflection = GlossyEnvironmentReflection(reflect(-view, normal), input.positionWS,
                        max(0.08h, 1.0h - smoothness), ambientOcclusion);
                    half3 reflectance = lerp(0.04h * _SpecColor.rgb, albedo.rgb, metallic);
                    color += reflection * reflectance * sourceSpecMask * _SpecStrength * _SourceInputsStrength * sourceSlot.a;
                }
                if (_SkinScatterStrength > 0.0h)
                {
                    half visibility = min(key.shadowAttenuation, environmentShadow);
                    visibility *= (1.0h - faceHairShadow) * (1.0h - faceShadow);
                    color += albedo.rgb * key.color * key.distanceAttenuation * visibility * scatterMask
                        * _SkinScatterColor.rgb * _SkinScatterStrength
                        * CharacterSkinScatter(dot(additionalNormal, key.direction), _SkinScatterRadius);
                }
                float3 additionalColor = 0;
                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                    // Shade both Forward light modes per pixel; each light owns its shadows and face projection.
                    if (_AdditionalLightStrength > 0.0h && _AdditionalLightLimit > 0.0h)
                    {
                        uint count = GetAdditionalLightsCount();
                        for (uint i = 0; i < count; ++i)
                        {
                            Light light = GetAdditionalLight(i, input.positionWS, half4(1, 1, 1, 1));
                            half attenuation = light.distanceAttenuation * light.shadowAttenuation;
                            half diffuseResponse = saturate((dot(additionalNormal, light.direction) + 0.35h) / 1.35h + sourceBias);
                            half3 localDiffuse = additionalAlbedo * diffuseResponse;
                            half faceVisibility = 1;
                            if (_FaceMode > 0.5h)
                            {
                                half3 localLightHead = SafeNormalize(mul((float3x3)_HeadWorldToLocal, light.direction));
                                half3 faceLight = CharacterFaceLight(positionHead, input.uv, localLightHead);
                                localDiffuse = additionalAlbedo * faceLight.x
                                    * lerp(half3(1, 1, 1), _FaceShadowColor.rgb, faceLight.y * _FaceShadowStrength)
                                    * lerp(half3(1, 1, 1), _SkinShadeColor.rgb, faceLight.z);
                                faceVisibility = (1.0h - faceLight.y) * (1.0h - faceLight.z);
                            }

                            half3 localHalfVector = SafeNormalize(light.direction + view);
                            half localNdotH = saturate(dot(normal, localHalfVector));
                            half localSpec = pow(localNdotH, specPower) * smoothstep(_SpecThreshold - _SpecSoftness,
                                _SpecThreshold + _SpecSoftness, localNdotH);
                            localSpec *= saturate(dot(normal, light.direction)) * _SpecStrength;
                            half3 localSpecular = localSpec * specColor * sourceSpecMask;
                            if (_HairMode > 0.5h)
                            {
                                CharacterHairLighting localHair = EvaluateCharacterHair(normal, strand, view,
                                    light.direction, hairControl, hairSettings);
                                localDiffuse *= lerp(half3(1, 1, 1), _HairCreaseColor.rgb, localHair.contact);
                                localSpecular = localHair.spec * _HairSpecColor.rgb
                                    + localSpec * specColor * sourceMaterial.b * _SourceInputsStrength;
                            }
                            localDiffuse *= 1.0h - metallic * 0.5h;
                            localSpecular *= stylizedMask.g * sourceSlot.a * faceVisibility;
                            half3 localScatter = additionalAlbedo * _SkinScatterColor.rgb * _SkinScatterStrength
                                * scatterMask * faceVisibility
                                * CharacterSkinScatter(dot(additionalNormal, light.direction), _SkinScatterRadius);
                            additionalColor += light.color * attenuation * (localDiffuse + localSpecular + localScatter);
                        }
                    }
                #endif
                additionalColor *= _AdditionalLightStrength;
                // Cap all local diffuse, highlights and scatter together while preserving their hue.
                float peak = max(additionalColor.r, max(additionalColor.g, additionalColor.b));
                color += additionalColor * min(1.0, _AdditionalLightLimit / max(peak, 0.0001));
                // Local self-emission approximation; the source shader boosts lit colour instead.
                color += albedo.rgb * sourceAttributes.b * _SourceEmissionColor.rgb
                    * _SourceEmissionStrength * _SourceInputsStrength;
                color = MixFog(color, input.fogFactor);
                return half4(color, albedo.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            Varyings ShadowVert(Attributes input)
            {
                Varyings output = Vert(input);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection = normalize(_LightPosition - output.positionWS);
                #else
                    float3 lightDirection = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS, output.normalWS, lightDirection));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(_CastShadows - 0.5h);
                if (_BodyMode > 0.5h) clip(input.bodyControl.w - 0.5h);
                SampleCharacter(input.uv);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]
            AlphaToMask [_AlphaToMask]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            half4 DepthFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(0.5h - _Surface);
                half4 albedo = SampleCharacter(input.uv);
                return half4(input.positionCS.z, 0, 0, albedo.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]
            AlphaToMask [_AlphaToMask]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            half4 DepthNormalsFrag(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(0.5h - _Surface);
                half4 albedo = SampleCharacter(input.uv);
                float3 normal = normalize(input.normalWS) * IS_FRONT_VFACE(frontFace, 1.0, -1.0);
                if (_PackedNormalStrength > 0.0h)
                {
                    half2 packedRG = SAMPLE_TEXTURE2D(_PackedNormalMap, sampler_PackedNormalMap, input.uv).rg;
                    normal = CharacterPackedNormalWS(input.positionWS, input.uv, normal, packedRG, _PackedNormalStrength);
                }
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormal = PackNormalOctQuadEncode(normal);
                    return half4(PackFloat2To888(saturate(octNormal * 0.5 + 0.5)), albedo.a);
                #else
                    return half4(normal, albedo.a);
                #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "CharacterOutline"
            Tags { "LightMode" = "CharacterOutline" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            Varyings OutlineVert(Attributes input)
            {
                Varyings output = Vert(input);
                float3 outlineNormal = CharacterOutlineNormalWS(input.normalOS, input.tangentOS, input.outlineControl);
                half outlineWeight = input.outlineControl.w >= 2.0 ? saturate(input.outlineControl.w - 2.0) : 1.0;
                if (_FaceMode > 0.5h)
                {
                    float3 positionHead = mul(_HeadWorldToLocal, float4(output.positionWS, 1)).xyz;
                    float3 normalHead = SafeNormalize(mul((float3x3)_HeadWorldToLocal, output.normalWS));
                    float3 viewHead = SafeNormalize(mul((float3x3)_HeadWorldToLocal, GetWorldSpaceViewDir(output.positionWS)));
                    outlineWeight *= CharacterFaceOutlineWeight(positionHead, normalHead, viewHead);
                    outlineWeight *= lerp(1.0, CharacterFaceInteriorOutlineWeight(input.bodyBind.xyz, viewHead), _NativeFaceOutlineMask);
                }
                if (_UseSkinOverlay > 0.5h)
                {
                    float3 p = input.bodyBind.xyz;
                    half chestInterior = saturate(input.bodyControl.x) * smoothstep(0.058, 0.076, p.z)
                        * (1.0 - smoothstep(0.043, 0.055, abs(p.x))) * smoothstep(1.130, 1.146, p.y)
                        * (1.0 - smoothstep(1.248, 1.266, p.y));
                    outlineWeight *= 1.0h - chestInterior;
                }
                output.positionCS = CharacterOutlinePositionCS(output.positionCS, output.positionWS,
                    outlineNormal, _OutlineWidth, _OutlineDistance, outlineWeight, output.outlineCoverage);
                output.normalWS = outlineNormal;
                return output;
            }

            half4 OutlineFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(0.5h - _Surface);
                SampleCharacter(input.uv);
                float3 view = GetWorldSpaceViewDir(input.positionWS);
                half opacity = CharacterOutlineOpacity(input.normalWS, view, _HairMode > 0.5h ? 0.90h : 0.60h);
                // Also mask fragments: triangles adjoining eye/lip openings can have unmasked outer vertices.
                if (_NativeFaceOutlineMask > 0.5)
                {
                    float3 viewHead = SafeNormalize(mul((float3x3)_HeadWorldToLocal, view));
                    opacity *= CharacterFaceInteriorOutlineWeight(input.bodyBind.xyz, viewHead);
                }
                if (_OutlineIntersectionFade > 0.0)
                {
                    float rawDepth = SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS));
                    float sceneDepth = unity_OrthoParams.w > 0.5 ? LinearDepthToEyeDepth(rawDepth) : LinearEyeDepth(rawDepth, _ZBufferParams);
                    float hullDepth = unity_OrthoParams.w > 0.5 ? LinearDepthToEyeDepth(input.positionCS.z) : LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                    // Fade near-coplanar hair intersections instead of exposing alternating hull fragments.
                    opacity *= smoothstep(0.0001, _OutlineIntersectionFade, sceneDepth - hullDepth);
                }
                half3 color = MixFog(_OutlineColor.rgb, input.fogFactor);
                return half4(color, _OutlineColor.a * input.outlineCoverage * opacity);
            }
            ENDHLSL
        }
    }
}
