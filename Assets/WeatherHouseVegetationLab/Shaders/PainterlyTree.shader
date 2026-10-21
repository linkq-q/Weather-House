Shader "Weather House/Painterly Tree"
{
    Properties
    {
        _BaseColor("Middle Color", Color) = (.48,.57,.29,1)
        _LitColor("Sunlit Color", Color) = (.69,.72,.40,1)
        _ShadowColor("Cool Shade Color", Color) = (.29,.42,.33,1)
        _Foliage("Foliage (0 Bark / 1 Leaves)", Range(0,1)) = 1
        _FoliageShadowStrength("Leaf Shadow Strength", Range(0,1)) = .38
        _PatchScale("Brush Patch Scale", Range(.2,6)) = 1.8
        _PatchStrength("Brush Color Variation", Range(0,.4)) = .16
        _WindStrength("Wind Strength (Metres)", Range(0,.2)) = .055
        _WindSpeed("Wind Speed", Range(0,3)) = .8
        _WindDirection("Wind Direction XZ", Vector) = (1,0,.45,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _LitColor, _ShadowColor;
            float4 _WindDirection;
            float _Foliage, _FoliageShadowStrength, _PatchScale, _PatchStrength, _WindStrength, _WindSpeed;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        float3 TreePositionWS(Attributes input)
        {
            float3 p = TransformObjectToWorld(input.positionOS.xyz);
            float2 direction = normalize(_WindDirection.xz + float2(.0001,0));
            float phase = dot(p.xz, direction) * .31 - _Time.y * _WindSpeed;
            p.xz += direction * (sin(phase) * .7 + sin(phase * .53 + 1.8) * .3) * _WindStrength * input.color.a;
            return p;
        }
        float Patch(float3 p)
        {
            // Object-space brush variation follows the tree rather than the camera.
            float2 projected = float2(p.x + p.y * .37,p.z + p.y * .19);
            float2 cell = floor(projected);
            float2 f = frac(projected); f = f * f * (3 - 2 * f);
            float4 a = frac(sin(float4(dot(cell,float2(127.1,311.7)),
                dot(cell+float2(1,0),float2(127.1,311.7)),
                dot(cell+float2(0,1),float2(127.1,311.7)),
                dot(cell+float2(1,1),float2(127.1,311.7)))) * 43758.5453);
            return lerp(lerp(a.x,a.y,f.x),lerp(a.z,a.w,f.x),f.y);
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
                float3 positionOS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half tint : TEXCOORD4;
                half fog : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TreePositionWS(input);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.positionOS = input.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv; o.tint = input.color.r;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half facing = dot(SafeNormalize(input.normalWS), sun.direction);
                half form = smoothstep(-.32,.88,facing);
                float3 brushPosition = input.positionOS * _PatchScale;
                brushPosition.y *= lerp(.28,.72,_Foliage);
                float patch = Patch(brushPosition + input.positionOS.y * float3(.12,0,.17));
                // Keep canopy volume readable without turning each overlapping leaf into
                // a hard black cutout. Bark still receives the full scene shadow.
                half receivedShadow = lerp(1,sun.shadowAttenuation,lerp(1,_FoliageShadowStrength,_Foliage));
                half lit = saturate(form * receivedShadow);
                half3 color = lerp(_ShadowColor.rgb, _BaseColor.rgb, smoothstep(.06,.55,lit));
                color = lerp(color, _LitColor.rgb, smoothstep(.48,.95,lit) * .85);
                color *= input.tint * (1 + (patch - .5) * _PatchStrength * 2);
                // Long, quiet bark stripes follow the branch UVs, with no high-frequency bump.
                float bark = smoothstep(.35,.7,sin(input.uv.x * 45 + input.uv.y * 2 + patch) * .5 + .5);
                color *= lerp(1 - bark * .10,1,_Foliage);
                half energy = dot(sun.color, half3(.2126,.7152,.0722));
                half ambient = dot(max(SampleSH(half3(0,1,0)),0),half3(.2126,.7152,.0722));
                color *= clamp(ambient * .4 + energy * .65,.35,1.25);
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
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection, _LightPosition;
            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 world = TreePositionWS(input);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 direction = normalize(_LightPosition - world);
                #else
                    float3 direction = _LightDirection;
                #endif
                float4 clip = TransformWorldToHClip(ApplyShadowBias(world,TransformObjectToWorldNormal(input.normalOS),direction));
                #if UNITY_REVERSED_Z
                    clip.z = min(clip.z,UNITY_NEAR_CLIP_VALUE);
                #else
                    clip.z = max(clip.z,UNITY_NEAR_CLIP_VALUE);
                #endif
                return clip;
            }
            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            float4 DepthVert(Attributes input) : SV_POSITION
            { UNITY_SETUP_INSTANCE_ID(input); return TransformWorldToHClip(TreePositionWS(input)); }
            half4 DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex NormalVert
            #pragma fragment NormalFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            struct NormalVaryings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
            NormalVaryings NormalVert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                NormalVaryings o;
                o.positionCS = TransformWorldToHClip(TreePositionWS(input));
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }
            half4 NormalFrag(NormalVaryings input) : SV_Target
            {
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
}
