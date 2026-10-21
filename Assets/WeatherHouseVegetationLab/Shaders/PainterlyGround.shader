Shader "Weather House/Painterly Ground"
{
    Properties
    {
        _BaseColor("Meadow Bed", Color) = (0.40, 0.49, 0.22, 1)
        _PatchColor("Warm Pigment", Color) = (0.57, 0.62, 0.31, 1)
        _ShoreColor("Shore Ochre", Color) = (0.69, 0.66, 0.45, 1)
        _ShadowTint("Cool Shadow", Color) = (0.56, 0.69, 0.62, 1)
        _WaterLevel("Water Height", Float) = 0
        _PatchScale("Pigment Patch Scale", Range(0.02, 2)) = 0.23
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _PatchColor, _ShoreColor, _ShadowTint;
            float _WaterLevel, _PatchScale;
        CBUFFER_END
        struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
        float Noise(float2 p)
        {
            float2 cell = floor(p), f = frac(p);
            f = f * f * (3 - 2 * f);
            float4 d = float4(dot(cell,float2(127.1,311.7)),dot(cell+float2(1,0),float2(127.1,311.7)),
                dot(cell+float2(0,1),float2(127.1,311.7)),dot(cell+1,float2(127.1,311.7)));
            float4 h = frac(sin(d)*43758.5453);
            return lerp(lerp(h.x,h.y,f.x),lerp(h.z,h.w,f.x),f.y);
        }
        ENDHLSL
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; half3 normal:TEXCOORD1; half fog:TEXCOORD2; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.world=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.world);
                o.normal=TransformObjectToWorldNormal(v.normalOS);
                o.fog=ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float patch=Noise(i.world.xz*_PatchScale);
                float brush=Noise(i.world.xz*float2(2.1,4.8));
                half3 meadow=lerp(_BaseColor.rgb,_PatchColor.rgb,smoothstep(0.30,0.75,patch)*0.65);
                meadow*=0.97+0.06*smoothstep(0.35,0.65,brush);
                float bank=smoothstep(_WaterLevel+0.15,_WaterLevel+0.95,i.world.y+(patch-.5)*.25);
                half3 surface=lerp(_ShoreColor.rgb,meadow,bank);
                Light light=GetMainLight(TransformWorldToShadowCoord(i.world));
                half diffuse=smoothstep(-0.2,0.8,dot(normalize(i.normal),light.direction));
                half3 lit=max(SampleSH(half3(0,1,0)),half3(.16,.16,.16))*.65+light.color*(.28+diffuse*.52);
                half3 color=surface*lit*lerp(half3(1,1,1),_ShadowTint.rgb,(1-light.shadowAttenuation)*.55);
                return half4(MixFog(color,i.fog),1);
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
            #pragma vertex VertDepth
            #pragma fragment FragDepth
            float4 VertDepth(Attributes v):SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half4 FragDepth():SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertDepthNormals
            #pragma fragment FragDepthNormals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            struct DepthNormalAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct DepthNormalVaryings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            DepthNormalVaryings VertDepthNormals(DepthNormalAttributes input)
            {
                DepthNormalVaryings output = (DepthNormalVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 FragDepthNormals(DepthNormalVaryings input) : SV_Target
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
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex VertShadow
            #pragma fragment FragShadow
            float3 _LightDirection;
            float4 VertShadow(Attributes v):SV_POSITION
            {
                float3 world=TransformObjectToWorld(v.positionOS.xyz);
                float4 position=TransformWorldToHClip(ApplyShadowBias(world,TransformObjectToWorldNormal(v.normalOS),_LightDirection));
                #if UNITY_REVERSED_Z
                    position.z=min(position.z,UNITY_NEAR_CLIP_VALUE);
                #else
                    position.z=max(position.z,UNITY_NEAR_CLIP_VALUE);
                #endif
                return position;
            }
            half4 FragShadow():SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
