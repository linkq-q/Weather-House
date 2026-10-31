Shader "Hidden/WeatherHouse/StoneApproval"
{
    Properties { _Coverage("Coverage",2D)="black"{} _BaseColor("Stone",Color)=(.48,.49,.46,1)
        _Center("Center",Vector)=(0,0,0,0) _Right("Right",Vector)=(1,0,0,0)
        _Up("Up",Vector)=(0,0,1,0) _Size("Size",Vector)=(1,1,1,0) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_Coverage); SAMPLER(sampler_Coverage);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor,_Center,_Right,_Up,_Size;
            CBUFFER_END
            struct V {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
            struct F {float4 positionCS:SV_POSITION;float3 p:TEXCOORD0;float3 n:TEXCOORD1;float3 ws:TEXCOORD2;};
            F Vert(V v){F o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.p=v.positionOS.xyz;
                o.n=TransformObjectToWorldNormal(v.normalOS);o.ws=TransformObjectToWorld(v.positionOS.xyz);return o;}
            half4 Frag(F i):SV_Target
            {
                float3 local=i.p-_Center.xyz;
                float2 uv=float2(dot(local,_Right.xyz),dot(local,_Up.xyz))/_Size.xy+.5;
                float3 front=TransformObjectToWorldNormal(cross(_Up.xyz,_Right.xyz));
                float3 normal=normalize(cross(ddy(i.ws),ddx(i.ws)));
                normal*=dot(normal,i.n)<0?-1:1;
                float valid=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1)*step(.75,dot(normal,front));
                half coverage=SAMPLE_TEXTURE2D(_Coverage,sampler_Coverage,uv).r*valid;
                half3 color=_BaseColor.rgb*(1-.88*coverage);
                Light light=GetMainLight();
                half3 illumination=SampleSH(normal)+light.color*saturate(dot(normal,light.direction));
                return half4(color*illumination,1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
