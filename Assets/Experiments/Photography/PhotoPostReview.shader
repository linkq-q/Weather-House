Shader "Hidden/WeatherHouse/PhotoPostReview"
{
    Properties { _MainTex("Photograph",2D)="white"{} _Sharpness("Edge clarity",Float)=0 _Contrast("Contrast",Float)=1
        _Saturation("Saturation",Float)=1 _LocalContrast("Local contrast",Float)=0 _ShadowLift("Shadow lift",Float)=0 _Warmth("Warmth",Float)=0 }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;float4 _MainTex_TexelSize;
            float _Sharpness,_Contrast,_Saturation,_LocalContrast,_ShadowLift,_Warmth;
            float luminance(float3 c){return dot(c,float3(.2126,.7152,.0722));}
            float3 blur4(float2 uv,float radius)
            {
                float2 d=_MainTex_TexelSize.xy*radius;
                return (tex2D(_MainTex,uv+float2(d.x,0)).rgb+tex2D(_MainTex,uv-float2(d.x,0)).rgb+
                    tex2D(_MainTex,uv+float2(0,d.y)).rgb+tex2D(_MainTex,uv-float2(0,d.y)).rgb)*.25;
            }
            float4 frag(v2f_img i):SV_Target
            {
                float3 source=tex2D(_MainTex,i.uv).rgb;float l=luminance(source);
                float detail=(l-luminance(blur4(i.uv,1.5)))*_Sharpness+(l-luminance(blur4(i.uv,12)))*_LocalContrast;
                float3 color=source+clamp(detail,-.08,.08);
                color=(color-.5)*_Contrast+.5;
                color+=_ShadowLift*pow(saturate(1-l),2);
                color=lerp(luminance(color).xxx,color,_Saturation);
                color+=float3(1,.35,-.7)*_Warmth;
                return float4(saturate(color),1);
            }
            ENDCG
        }
    }
}
