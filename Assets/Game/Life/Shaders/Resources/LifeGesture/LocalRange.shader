Shader "Weather House/Life/Local Weather Range"
{
    Properties
    {
        _Color ("主体颜色 / Base Color", Color) = (1,.82,.43,1)
        _CoreColor ("焰心颜色 / Core Color", Color) = (1,.97,.80,1)
        _TipColor ("尖端颜色 / Tip Color", Color) = (1,.97,.80,1)
        _TipTint ("尖端染色 / Tip Tint", Range(0,1)) = 0
        _Brightness ("整体亮度 / Brightness", Range(0,4)) = 1
        _Opacity ("整体不透明度 / Opacity", Range(0,1)) = 1

        _FlameCount ("火焰数量 / Flame Count", Range(1,64)) = 22
        _FlameWidth ("火焰宽度 / Width", Range(.1,3)) = 1
        _FlameHeight ("火焰高度比例 / Height", Range(.1,1.2)) = 1
        _Variation ("高低宽窄随机程度 / Variation", Range(0,1)) = 1
        _PositionJitter ("排列错落 / Position Jitter", Range(0,.6)) = .20
        _Taper ("向上收尖 / Taper", Range(0,.98)) = .70
        _TipSoftness ("尖端柔边 / Tip Softness", Range(.005,.4)) = .15
        _HeightFade ("向上衰减 / Height Fade", Range(0,8)) = 1.5
        _EdgeSoftness ("侧边柔软度 / Edge Softness", Range(.2,3)) = 1

        _CoreWidth ("焰心宽度 / Core Width", Range(.05,1)) = .44
        _CoreBrightness ("焰心亮度 / Core Brightness", Range(0,4)) = 1.35
        _BodyBrightness ("主体亮度 / Body Brightness", Range(0,3)) = .65
        _HaloBrightness ("光晕亮度 / Halo Brightness", Range(0,2)) = .22
        _HaloWidth ("光晕扩散 / Halo Width", Range(1,5)) = 2
        _FlameOpacity ("单簇火焰不透明度 / Flame Opacity", Range(0,1)) = 1

        _FlowSpeed ("整体运动速度 / Flow Speed", Range(0,4)) = 1
        _SwayAmount ("摆动幅度 / Sway Amount", Range(0,.5)) = .13
        _SwaySpeed ("摆动速度 / Sway Speed", Range(0,4)) = .95
        _SwayFrequency ("弯曲次数 / Sway Frequency", Range(0,15)) = 4
        _Lean ("整体倾斜 / Lean", Range(-.5,.5)) = 0
        _HeightFlutter ("高度起伏 / Height Flutter", Range(0,.2)) = .055
        _PulseAmount ("明暗波动幅度 / Pulse Amount", Range(0,.5)) = .18
        _PulseSpeed ("明暗波动速度 / Pulse Speed", Range(0,8)) = 2.4

        _Distortion ("边缘扰动 / Distortion", Range(0,.5)) = 0
        _Erosion ("破碎溶解 / Erosion", Range(0,1)) = 0
        _NoiseScale ("扰动细密程度 / Noise Scale", Range(1,32)) = 8
        _NoiseSpeed ("扰动流动速度 / Noise Speed", Range(0,4)) = 1
        _NoiseTex ("自定义灰度扰动纹理 / Noise Texture", 2D) = "gray" {}
        _NoiseTextureBlend ("使用纹理的比例 / Texture Blend", Range(0,1)) = 0

        _VeilOpacity ("底部光幕强度 / Veil Strength", Range(0,3)) = 1
        _VeilHeight ("光幕高度 / Veil Height", Range(0,1)) = .50
        _VeilWaves ("光幕波浪幅度 / Veil Waves", Range(0,3)) = 1
        _VeilSpeed ("光幕运动速度 / Veil Speed", Range(0,4)) = 1

        _RimBrightness ("贴地圈亮度 / Rim Brightness", Range(0,4)) = 1
        _RimOpacity ("贴地圈不透明度 / Rim Opacity", Range(0,1)) = 1
        _RimCoreWidth ("贴地亮线宽度 / Rim Core Width", Range(.1,4)) = 1
        _RimGlowWidth ("贴地柔光宽度 / Rim Glow Width", Range(.1,4)) = 1

        [HideInInspector] _Visibility ("Visibility", Range(0,1)) = 1
        [HideInInspector] _Flames ("Maintained weather", Range(0,1)) = 0
        [HideInInspector] _Reveal ("Opening sweep", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend One OneMinusSrcAlpha
        Cull Off ZWrite Off ZTest LEqual
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
            half4 _Color, _CoreColor, _TipColor;
            float4 _NoiseTex_ST;
            float _Visibility, _Flames, _Reveal, _FlameCount, _FlowSpeed;
            float _TipTint, _Brightness, _Opacity;
            float _FlameWidth, _FlameHeight, _Variation, _PositionJitter, _Taper, _TipSoftness, _HeightFade, _EdgeSoftness;
            float _CoreWidth, _CoreBrightness, _BodyBrightness, _HaloBrightness, _HaloWidth, _FlameOpacity;
            float _SwayAmount, _SwaySpeed, _SwayFrequency, _Lean, _HeightFlutter, _PulseAmount, _PulseSpeed;
            float _Distortion, _Erosion, _NoiseScale, _NoiseSpeed, _NoiseTextureBlend;
            float _VeilOpacity, _VeilHeight, _VeilWaves, _VeilSpeed;
            float _RimBrightness, _RimOpacity, _RimCoreWidth, _RimGlowWidth;
            CBUFFER_END
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; return o; }
            half4 frag(Varyings i) : SV_Target
            {
                float u = frac(i.uv.x), t = _Time.y * _FlowSpeed;
                float sweep = 1 - smoothstep(_Reveal, _Reveal + .045, u);
                float visible = _Visibility * _Color.a * sweep * _Opacity;
                half3 ivory = _CoreColor.rgb;
                if (i.uv.y < -.5)
                {
                    float edge = abs((i.uv.y + 2) * 2 - 1);
                    float core = exp2(-pow(edge / max(.01, _RimCoreWidth), 2) * 100);
                    float glow = exp2(-pow(edge / max(.01, _RimGlowWidth), 2) * 8);
                    float alpha = saturate(core * .85 + glow * .18);
                    float rimVisible = visible * _RimOpacity;
                    return half4((ivory * core * 2 + _Color.rgb * glow * .45) * rimVisible * _RimBrightness * _Brightness, alpha * rimVisible);
                }

                float h = i.uv.y;
                float angle = u * 6.283185307;
                float vt = t * _VeilSpeed;
                float crest = _VeilHeight + _VeilWaves * (.12 * sin(angle * 5 - vt * .79) + .08 * sin(angle * 3 + vt * .47 + 1.2));
                float veilTop = 1 - smoothstep(crest - .20, crest + .18, h);
                float veilRise = .88 + .12 * sin(h * 8 - vt * 1.4 + sin(angle * 3));
                float veil = (.24 * exp2(-h * 2.9) + .025) * veilTop * veilRise * _Flames * _VeilOpacity;
                float count = max(1, round(_FlameCount));
                float cell = floor(u * count);
                float seed = frac(sin(cell * 13.53 + 17.31) * 4375.85);
                float x = frac(u * count) - .5 - (seed - .5) * _PositionJitter;
                float reach = max(.02, _FlameHeight * (lerp(.6, .37 + .46 * seed, _Variation) + _HeightFlutter * sin(t * .8 + seed * 9)));
                float bend = (_SwayAmount * sin(h * _SwayFrequency - t * _SwaySpeed + seed * 12) + (seed - .5) * .12 * _Variation + _Lean) * h;
                float erosion = 1;
                // Optional detail uses periodic procedural noise, with an artist texture override.
                if (_Distortion > 0 || _Erosion > 0)
                {
                    float nt = t * _NoiseSpeed;
                    float noise = .5 + sin(angle * round(_NoiseScale) + h * 5 - nt * 1.7) / 3
                        + sin(angle * round(_NoiseScale * 2) + h * 11 - nt * .9) / 6;
                    if (_NoiseTextureBlend > 0)
                    {
                        float2 uv = float2(u, h) * _NoiseTex_ST.xy + _NoiseTex_ST.zw + float2(0, -nt * .3);
                        noise = lerp(noise, SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv).r, _NoiseTextureBlend);
                    }
                    bend += (noise - .5) * _Distortion * h;
                    erosion = lerp(1, smoothstep(.25, .7, noise), _Erosion);
                }
                float width = max(.004, lerp(.255, lerp(.19, .32, seed), _Variation) * _FlameWidth * (1 - _Taper * saturate(h / reach)));
                float distance = x - bend;
                float softness = max(.05, _EdgeSoftness);
                float core = exp2(-pow(distance / max(.008, width * _CoreWidth), 2) * 1.45 / softness);
                float flame = exp2(-pow(distance / width, 2) * 1.4 / softness);
                float halo = exp2(-pow(distance / (width * max(1, _HaloWidth)), 2) * 1.3 / softness);
                float tipSoftness = max(.001, _TipSoftness);
                float tip = 1 - smoothstep(reach - tipSoftness, reach + tipSoftness / 6, h);
                float rising = 1 - _PulseAmount + _PulseAmount * sin(h * 13 - t * _PulseSpeed + seed * 7);
                float envelope = tip * exp2(-h * _HeightFade) * rising * _Flames * _FlameOpacity * erosion;
                float alpha = saturate((flame * .45 + core * .28) * envelope);
                float tintAmount = smoothstep(0, 1, saturate(h / reach)) * _TipTint;
                half3 bodyColor = lerp(_Color.rgb, _TipColor.rgb, tintAmount);
                half3 coreColor = lerp(ivory, _TipColor.rgb, tintAmount);
                half3 light = (coreColor * core * _CoreBrightness + bodyColor * (flame * _BodyBrightness + halo * _HaloBrightness)) * envelope;
                light += lerp(_Color.rgb, ivory, .35) * veil * (1 - alpha);
                alpha += veil * (1 - alpha);
                return half4(light * visible * _Brightness, saturate(alpha) * visible);
            }
            ENDHLSL
        }
    }
    CustomEditor "WeatherHouse.Life.Editor.LifeCastRangeShaderGUI"
}
