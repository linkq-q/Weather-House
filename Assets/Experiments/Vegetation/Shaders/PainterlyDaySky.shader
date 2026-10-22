Shader "Weather House/Painterly Day Sky"
{
    Properties
    {
        _SkyTop("Clear Cyan Sky", Color) = (0.61, 0.82, 0.90, 1)
        _Horizon("Pale Horizon", Color) = (0.79, 0.91, 0.93, 1)
        _CloudColor("Sunlit Cream Clouds", Color) = (0.99, 0.95, 0.86, 1)
        _CloudShade("Cool Cloud Undersides", Color) = (0.84, 0.88, 0.85, 1)
        _CloudCoverage("Cloud Coverage", Range(0, 1)) = 0.51
        _SunDirection("Sun Direction", Vector) = (0.5, 0.65, -0.5, 0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _SkyTop, _Horizon, _CloudColor, _CloudShade;
                float4 _SunDirection;
                float _CloudCoverage;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float3 direction:TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.direction=input.positionOS.xyz;
                return output;
            }
            float Hash(float3 p)
            {
                p=frac(p*.1031);
                p+=dot(p,p.yzx+33.33);
                return frac((p.x+p.y)*p.z);
            }
            float CloudNoise(float3 p)
            {
                float3 cell=floor(p), f=frac(p);
                f=f*f*(3-2*f);
                float nearFace=lerp(lerp(Hash(cell),Hash(cell+float3(1,0,0)),f.x),
                    lerp(Hash(cell+float3(0,1,0)),Hash(cell+float3(1,1,0)),f.x),f.y);
                float farFace=lerp(lerp(Hash(cell+float3(0,0,1)),Hash(cell+float3(1,0,1)),f.x),
                    lerp(Hash(cell+float3(0,1,1)),Hash(cell+float3(1,1,1)),f.x),f.y);
                return lerp(nearFace,farFace,f.z);
            }
            half4 Frag(Varyings input):SV_Target
            {
                float3 direction=normalize(input.direction);
                half3 sky=lerp(_Horizon.rgb,_SkyTop.rgb,sqrt(saturate(direction.y)));
                // Three broad scales give joined cloud masses without small speckled detail.
                float3 p=direction*float3(3.1,3.8,3.1)+float3(4.2,1.7,8.3);
                float shape=CloudNoise(p)*.68+CloudNoise(p*2.07+11.3)*.25+CloudNoise(p*4.03+23.7)*.07;
                float threshold=lerp(.90,.10,_CloudCoverage);
                float mask=smoothstep(threshold-.025,threshold+.025,shape);
                mask*=smoothstep(-.015,.075,direction.y)*smoothstep(0,.05,_CloudCoverage);
                float3 sun=normalize(_SunDirection.xyz+float3(0,.0001,0));
                float warmFace=saturate(.62+smoothstep(threshold,threshold+.16,shape)*.3+dot(direction,sun)*.08);
                half3 cloud=lerp(_CloudShade.rgb,_CloudColor.rgb,warmFace);
                return half4(lerp(sky,cloud,mask),1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
