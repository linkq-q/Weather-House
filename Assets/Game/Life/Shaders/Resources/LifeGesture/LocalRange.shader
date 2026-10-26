Shader "Weather House/Life/Local Weather Range"
{
    Properties { _Color ("Color", Color) = (.6,.9,.8,1) _Visibility ("Visibility", Range(0,1)) = 1 }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off ZWrite Off ZTest LEqual
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _Visibility;
            CBUFFER_END
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; return o; }
            half4 frag(Varyings i) : SV_Target
            {
                float edge = abs(i.uv.y * 2 - 1);
                float ringOpacity = 1 - smoothstep(.1, .9, edge);
                float breath = .86 + .14 * sin(_Time.y * 2.2 + i.uv.x * 6.283185);
                return half4(_Color.rgb, _Color.a * _Visibility * ringOpacity * breath);
            }
            ENDHLSL
        }
    }
}
