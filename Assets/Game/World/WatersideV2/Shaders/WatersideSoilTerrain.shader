Shader "Weather House/Waterside Soil Terrain"
{
    Properties
    {
        [HideInInspector] [ToggleUI] _EnableHeightBlend("EnableHeightBlend", Float) = 0
        _HeightTransition("Height Transition", Range(0, 1)) = 0
        [HideInInspector] [PerRendererData] _NumLayersCount("Total Layer Count", Float) = 4
        [HideInInspector] _Control("Control", 2D) = "red" {}
        [HideInInspector] _Splat0("Layer 0", 2D) = "grey" {}
        [HideInInspector] _Splat1("Layer 1", 2D) = "grey" {}
        [HideInInspector] _Splat2("Layer 2", 2D) = "grey" {}
        [HideInInspector] _Splat3("Layer 3", 2D) = "grey" {}
        [HideInInspector] _Normal0("Normal 0", 2D) = "bump" {}
        [HideInInspector] _Normal1("Normal 1", 2D) = "bump" {}
        [HideInInspector] _Normal2("Normal 2", 2D) = "bump" {}
        [HideInInspector] _Normal3("Normal 3", 2D) = "bump" {}
        [HideInInspector] _Mask0("Mask 0", 2D) = "grey" {}
        [HideInInspector] _Mask1("Mask 1", 2D) = "grey" {}
        [HideInInspector] _Mask2("Mask 2", 2D) = "grey" {}
        [HideInInspector] _Mask3("Mask 3", 2D) = "grey" {}
        [HideInInspector][Gamma] _Metallic0("Metallic 0", Range(0, 1)) = 0
        [HideInInspector][Gamma] _Metallic1("Metallic 1", Range(0, 1)) = 0
        [HideInInspector][Gamma] _Metallic2("Metallic 2", Range(0, 1)) = 0
        [HideInInspector][Gamma] _Metallic3("Metallic 3", Range(0, 1)) = 0
        [HideInInspector] _Smoothness0("Smoothness 0", Range(0, 1)) = .05
        [HideInInspector] _Smoothness1("Smoothness 1", Range(0, 1)) = .05
        [HideInInspector] _Smoothness2("Smoothness 2", Range(0, 1)) = .05
        [HideInInspector] _Smoothness3("Smoothness 3", Range(0, 1)) = .05
        [HideInInspector] _MainTex("BaseMap", 2D) = "grey" {}
        [HideInInspector] _BaseColor("Main Color", Color) = (1,1,1,1)
        [HideInInspector] _TerrainHolesTexture("Holes Map", 2D) = "white" {}
        [ToggleUI] _EnableInstancedPerPixelNormal("Instanced Per Pixel Normal", Float) = 1
        [HideInInspector] _SoilFootprints("Runtime soil impressions", 2D) = "black" {}
        [HideInInspector] _SoilFieldRect("Footprint world rectangle", Vector) = (0,0,0,0)
        [HideInInspector] _SoilTerrainRect("Terrain world rectangle", Vector) = (0,0,0,0)
        [HideInInspector] _SoilLayerWeights("Soil terrain layer", Vector) = (0,1,0,0)
        [HideInInspector] _SoilInteractionStrength("Runtime enabled", Float) = 0
        _SoilImpressionDepth("Soil impression depth", Range(0, .025)) = .008
        _SoilDarkening("Soil compression shade", Range(0, .3)) = .16
    }
    SubShader
    {
        Tags { "Queue"="Geometry-100" "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "UniversalMaterialType"="Lit" "TerrainCompatible"="True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex SplatmapVert
            #pragma fragment SplatmapFragment
            #pragma multi_compile_fragment __ _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #pragma shader_feature_local_fragment _TERRAIN_BLEND_HEIGHT
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _MASKMAP
            #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
            #define _METALLICSPECGLOSSMAP 1
            #define _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A 1
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_SoilFootprints); SAMPLER(sampler_SoilFootprints);
            float4 _SoilFootprints_TexelSize, _SoilFieldRect, _SoilTerrainRect, _SoilLayerWeights;
            float _SoilInteractionStrength, _SoilImpressionDepth, _SoilDarkening;

            // Keep the package's terrain splat/normal/lightmap/shadow implementation; modify only its PBR inputs.
            half4 WatersideSoilPBR(InputData data, half3 albedo, half metallic, half3 specular,
                half smoothness, half occlusion, half3 emission, half alpha)
            {
                if (_SoilInteractionStrength > .001)
                {
                    float2 uv = (data.positionWS.xz - _SoilFieldRect.xy) * _SoilFieldRect.zw;
                    float inside = step(0, uv.x) * step(0, uv.y) * step(uv.x, 1) * step(uv.y, 1);
                    float2 groundUV = (data.positionWS.xz - _SoilTerrainRect.xy) * _SoilTerrainRect.zw;
                    groundUV = (groundUV * (_Control_TexelSize.zw - 1) + .5) * _Control_TexelSize.xy;
                    half soil = dot(SAMPLE_TEXTURE2D(_Control, sampler_Control, groundUV), _SoilLayerWeights);
                    half strength = inside * saturate((soil - .2) / .6) * _SoilInteractionStrength;
                    half mark = SAMPLE_TEXTURE2D(_SoilFootprints, sampler_SoilFootprints, uv).r;
                    float2 texel = _SoilFootprints_TexelSize.xy;
                    half dx = SAMPLE_TEXTURE2D(_SoilFootprints, sampler_SoilFootprints, uv + float2(texel.x, 0)).r
                        - SAMPLE_TEXTURE2D(_SoilFootprints, sampler_SoilFootprints, uv - float2(texel.x, 0)).r;
                    half dz = SAMPLE_TEXTURE2D(_SoilFootprints, sampler_SoilFootprints, uv + float2(0, texel.y)).r
                        - SAMPLE_TEXTURE2D(_SoilFootprints, sampler_SoilFootprints, uv - float2(0, texel.y)).r;
                    float2 slope = float2(dx, dz) * .5 * _SoilFootprints_TexelSize.zw * _SoilFieldRect.zw * _SoilImpressionDepth * strength;
                    data.normalWS = NormalizeNormalPerPixel(data.normalWS + half3(slope.x, 0, slope.y));
                    albedo *= 1 - mark * strength * _SoilDarkening;
                    smoothness = lerp(smoothness, min(smoothness, .025), mark * strength * .5);
                }
                return UniversalFragmentPBR(data, albedo, metallic, specular, smoothness, occlusion, emission, alpha);
            }
            // Lighting.hlsl is already included: this redirects the terrain fragment's call, not Unity's functions.
            #define UniversalFragmentPBR WatersideSoilPBR
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitPasses.hlsl"
            #undef UniversalFragmentPBR
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Terrain/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Terrain/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Terrain/Lit/DepthNormals"
        UsePass "Universal Render Pipeline/Terrain/Lit/SceneSelectionPass"
        UsePass "Universal Render Pipeline/Terrain/Lit/Meta"
        UsePass "Hidden/Nature/Terrain/Utilities/PICKING"
    }
    Dependency "AddPassShader" = "Hidden/Universal Render Pipeline/Terrain/Lit (Add Pass)"
    Dependency "BaseMapShader" = "Hidden/Universal Render Pipeline/Terrain/Lit (Base Pass)"
    Dependency "BaseMapGenShader" = "Hidden/Universal Render Pipeline/Terrain/Lit (Basemap Gen)"
    CustomEditor "UnityEditor.Rendering.Universal.TerrainLitShaderGUI"
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
