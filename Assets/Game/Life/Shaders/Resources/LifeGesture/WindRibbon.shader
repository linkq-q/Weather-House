Shader "Weather House/Life/Wind Ribbon"
{
    Properties
    {
        _Color ("颜色 Tint", Color) = (1,1,1,1)
        _Opacity ("不透明度 Opacity", Range(0,1)) = 1
        _Brightness ("亮度 Brightness", Range(0,4)) = 1
        _CoreWidth ("亮线核心宽度 Core Width", Range(0,.95)) = .15
        _EdgeSoftness ("边缘柔软度 Edge Softness", Range(.05,2)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _Opacity, _Brightness, _CoreWidth, _EdgeSoftness;
            CBUFFER_END
            V vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.color=i.color*_Color; o.uv=i.uv; return o; }
            half4 frag(V i):SV_Target
            {
                float edge = abs(i.uv.y * 2 - 1);
                i.color.rgb *= _Brightness;
                i.color.a *= pow(saturate(1 - smoothstep(_CoreWidth, 1, edge)), 1 / max(.05, _EdgeSoftness)) * _Opacity;
                return i.color;
            }
            ENDHLSL
        }
    }
}
