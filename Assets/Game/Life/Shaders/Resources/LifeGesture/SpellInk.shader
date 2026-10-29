Shader "Weather House/Life/Spell Ink"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        struct appdata { float4 vertex:POSITION; float4 local:TEXCOORD0; float4 path:TEXCOORD1; float2 widths:TEXCOORD2; };
        struct v2f { float4 vertex:SV_POSITION; float2 local:TEXCOORD0; float4 path:TEXCOORD1; float2 widths:TEXCOORD2; };
        float _InkTime, _Length, _Release, _Daylight, _Failure, _StrokeSeed;
        float4 _Style;
        v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.local=v.local.xy; o.path=v.path; o.widths=v.widths; return o; }
        float wave(float v) { return sin(v)*.5+.5; }
        float moteHash(float v) { return frac(sin(v*17.13+_StrokeSeed)*13.137); }
        float3 palette(float3 c) {
            #ifndef UNITY_COLORSPACE_GAMMA
            c=GammaToLinearSpace(c);
            #endif
            return c;
        }
        ENDCG
        Pass
        {
            // Unite round capsules before compositing; overlaps never accumulate brightness.
            Blend One One
            BlendOp Max
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            float4 frag(v2f i):SV_Target
            {
                float along=clamp(i.local.x,0,i.path.y), f=along/i.path.y, s=i.path.x+along;
                float age=max(0,_InkTime-lerp(i.path.z,i.path.w,f));
                float w=lerp(i.widths.x,i.widths.y,f)*_Style.x;
                float d=length(float2(i.local.x-along,i.local.y));
                float fade=1;
                if(_Release>=0){
                    float cell=floor(s/30),nearest=1000;
                    [unroll] for(int n=-1;n<=1;n++){
                        float node=cell+n,center=(node+.2+.6*moteHash(node))*30;
                        nearest=min(nearest,abs(s-center));
                    }
                    float field=s/30-.5,left=floor(field);
                    float delay=.12+.55*lerp(moteHash(left+4.7),moteHash(left+5.7),smoothstep(0,1,frac(field)));
                    float gather=smoothstep(delay-.08,delay+.3,_Release);
                    d=length(float2(d,nearest*gather));
                    fade=1-smoothstep(delay+.25,delay+.63,_Release);w*=1-gather*.55;
                }
                w*=lerp(.4,1,smoothstep(0,10,s))*lerp(.7,1,smoothstep(0,7,_Length-s));
                float flow=.78+.22*wave(s*.035-_InkTime*3.1);
                float glint=pow(wave(s*.065-_InkTime*4.3),20)*pow(wave(s*.021+1.4),3);
                float core=exp(-pow(d/(w*.8),2)), body=exp(-pow(d/(w*2.25),2)), halo=exp(-pow(d/_Style.y,2));
                float strand=(sin(s*.024+sin(s*.041)-_InkTime*1.6)*4.3+6.3)*3;
                float threads=(exp(-pow((i.local.y-strand)/.7,2))+.7*exp(-pow((i.local.y+strand*.72)/.55,2)))
                    *pow(wave(s*.019-_InkTime*.85),2)*.48*step(0,i.local.x)*step(i.local.x,i.path.y);
                float ha=min(.8,(halo*_Style.z+threads*.4)*(.9+glint*.2)*flow);
                float ba=min(.88,body*_Style.w+threads*.35), ca=core*lerp(.9,.99,_Daylight)*(.92+glint*.08);
                float3 glow=palette(lerp(float3(.39,.79,.72),float3(1,.93,.66),_Daylight));
                float3 bodyColor=palette(lerp(float3(.43,.82,.74),float3(.94,.75,.34),_Daylight));
                float3 coreColor=palette(lerp(float3(.94,.99,.85),float3(1,1,.96),_Daylight));
                bodyColor=lerp(bodyColor,palette(float3(1,.54,.36)),_Failure*.7);
                float3 rgb=glow*ha; rgb=bodyColor*ba+rgb*(1-ba);
                rgb=coreColor*ca+rgb*(1-ca);
                float alpha=ca+(ba+ha*(1-ba))*(1-ca);
                return float4(rgb,alpha)*fade*smoothstep(0,.065,age);
            }
            ENDCG
        }
        Pass
        {
            Blend One OneMinusSrcAlpha
            BlendOp Add
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i):SV_Target
            {
                float r=length(i.local), round=exp(-r*r*9);
                float star=exp(-abs(i.local.x)*22-abs(i.local.y)*3)+exp(-abs(i.local.y)*22-abs(i.local.x)*3);
                float shape=lerp(round,star*.6+round*.4,step(.5,i.path.y));
                if(i.path.y>1.5)shape=exp(-r*r*24)*.85+exp(-r*r*4)*.18;
                if(i.path.y>3.5)shape=exp(-r*r*52)*.95+exp(-r*r*6.5)*.23;
                if(i.path.y>4.5)shape=exp(-r*r*34)*.68+exp(-r*r*6)*.12;
                float alpha=saturate(shape*i.path.x*(1-smoothstep(.72,1,r))*lerp(1,1.3,_Daylight));
                float3 c=lerp(float3(.58,.88,.78),float3(1,.86,.59),i.path.z);
                c=lerp(c,lerp(float3(1,.9,.5),float3(1,.98,.86),exp(-r*r*12)),_Daylight);
                if(i.path.y>3.5)c=lerp(c,float3(.96,1,.9),exp(-r*r*32)*.8);
                return float4(palette(c)*alpha,alpha);
            }
            ENDCG
        }
    }
}
