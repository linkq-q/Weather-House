Shader "Weather House/UI/Album Magic Reveal"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Reveal ("Reveal", Range(0,1)) = 0
        _NoiseTex ("Shared Noise", 2D) = "gray" {}
        _NoiseSize ("Noise Tile Size", Float) = 120
        _NoiseOrigin ("Element Bounds", Vector) = (0,0,100,100)
        _NoiseOffset ("Shared Noise Offset", Vector) = (0,0,0,0)
        _EdgeWidth ("Gold Edge Width", Float) = .06
        _Softness ("Reveal Softness", Float) = .025
        _AmberColor ("Amber", Color) = (1,.22,.015,1)
        _GoldColor ("Gold", Color) = (1,.66,.075,1)
        _IvoryColor ("Ivory", Color) = (1,.97,.73,1)
        _GlowIntensity ("Glow Intensity", Float) = 1
        _GlowSpread ("Exterior Glow Spread", Float) = 24
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "AlbumMagicField.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float2 page : TEXCOORD1; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float2 page : TEXCOORD1; float4 local : TEXCOORD2; };
            sampler2D _MainTex;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            v2f vert(appdata v)
            {
                v2f o;
                o.local = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.page = v.page;
                return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                half4 source = (tex2D(_MainTex, i.uv) + _TextureSampleAdd) * i.color;
                float distance = AlbumFrontDistance(i.page);
                float feather = max(_Softness, fwidth(distance)*.7);
                float visible = smoothstep(-feather, feather, distance);
                if (_Reveal <= 0) visible = 0;
                if (_Reveal >= 1) visible = 1;
                float active = AlbumLightEnvelope();
                float width = max(.001,_EdgeWidth);
                float heat = exp2(-max(distance,0)/width);
                // Amber -> gold -> ivory at the arriving edge, settling to the original image.
                // The separate expanded aura provides the light outside the original mesh.
                half3 color = lerp(source.rgb,AlbumGoldRamp(heat),saturate(heat*.82*active*_GlowIntensity));
                half alpha = source.a * visible;
                half4 c = half4(color*alpha,alpha);
                #ifdef UNITY_UI_CLIP_RECT
                c *= UnityGet2DClipping(i.local.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a - 0.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
