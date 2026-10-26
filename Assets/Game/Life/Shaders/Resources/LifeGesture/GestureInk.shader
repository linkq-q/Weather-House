Shader "Weather House/Life/Gesture Ink"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float2 localPosition : TEXCOORD1; };
            float4 _ClipRect;
            Varyings vert(Attributes v)
            {
                Varyings o; o.positionCS = UnityObjectToClipPos(v.positionOS); o.color = v.color; o.uv = v.uv; o.localPosition = v.positionOS.xy; return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float edge = abs(i.uv.y * 2 - 1);
                float core = 1 - smoothstep(.18, .65, edge);
                float glow = (1 - edge) * (1 - edge);
                half4 color = half4(lerp(i.color.rgb, half3(1, .99, .85), core * .38), i.color.a * (core * .64 + glow * .36));
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.localPosition, _ClipRect);
                #endif
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
