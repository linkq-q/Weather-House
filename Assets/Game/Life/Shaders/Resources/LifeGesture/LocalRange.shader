Shader "Weather House/Life/Local Weather Range"
{
    Properties
    {
        _Color ("Color", Color) = (1,.82,.43,1)
        _Visibility ("Visibility", Range(0,1)) = 1
        _Flames ("Maintained weather", Range(0,1)) = 0
        _Reveal ("Opening sweep", Range(0,1)) = 1
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
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _Visibility, _Flames, _Reveal;
            CBUFFER_END
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; return o; }
            half4 frag(Varyings i) : SV_Target
            {
                float u = frac(i.uv.x), t = _Time.y;
                float sweep = 1 - smoothstep(_Reveal, _Reveal + .045, u);
                float visible = _Visibility * _Color.a * sweep;
                half3 ivory = half3(1, .97, .80);
                if (i.uv.y < -.5)
                {
                    float edge = abs((i.uv.y + 2) * 2 - 1);
                    float core = exp2(-edge * edge * 100);
                    float glow = exp2(-edge * edge * 8);
                    float alpha = saturate(core * .85 + glow * .18);
                    return half4((ivory * core * 2 + _Color.rgb * glow * .45) * visible, alpha * visible);
                }

                float h = i.uv.y;
                float cell = floor(u * 22);
                float seed = frac(sin(cell * 13.53 + 17.31) * 4375.85);
                float x = frac(u * 22) - .5;
                float reach = .37 + .46 * seed + .055 * sin(t * .8 + seed * 9);
                float bend = (.13 * sin(h * 4 - t * .95 + seed * 12) + (seed - .5) * .12) * h;
                float width = lerp(.085, .15, seed) * (1 - .78 * saturate(h / reach));
                float distance = x - bend;
                float core = exp2(-pow(distance / max(.008, width * .28), 2) * 1.45);
                float flame = exp2(-pow(distance / width, 2) * 1.4);
                float halo = exp2(-pow(distance / (width * 2), 2) * 1.3);
                float tip = 1 - smoothstep(reach - .15, reach + .025, h);
                float rising = .82 + .18 * sin(h * 13 - t * 2.4 + seed * 7);
                float envelope = tip * exp2(-h * 1.5) * rising * _Flames;
                float alpha = saturate((flame * .45 + core * .28) * envelope);
                half3 light = ivory * core * 1.75 + _Color.rgb * (flame * .65 + halo * .22);
                return half4(light * envelope * visible, alpha * visible);
            }
            ENDHLSL
        }
    }
}
