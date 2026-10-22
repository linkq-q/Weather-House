Shader "Weather House/UI/Album Magic Aura"
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
        _Shape ("Circular Element", Float) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "AlbumMagicField.cginc"
            float4 _ClipRect;
            float _Shape;
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 page : TEXCOORD1; };
            struct v2f { float4 vertex : SV_POSITION; half4 color : COLOR; float2 page : TEXCOORD0; float4 local : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                o.local = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.page = v.page;
                o.color = v.color;
                return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                float2 center = _NoiseOrigin.xy + _NoiseOrigin.zw*.5;
                float2 halfSize = _NoiseOrigin.zw*.5;
                float2 delta = abs(i.page-center)-(halfSize-5);
                float box = length(max(delta,0)) + min(max(delta.x,delta.y),0)-5;
                float circle = (length((i.page-center)/halfSize)-1)*min(halfSize.x,halfSize.y);
                float shape = lerp(box,circle,_Shape);
                float distance = AlbumFrontDistance(i.page);
                float width = max(.001,_EdgeWidth);
                float active = AlbumLightEnvelope();
                float inside = 1-smoothstep(-1,2,shape);
                float spread = exp2(-max(shape,0)/max(1,_GlowSpread*.32));
                float core = exp2(-abs(distance)/max(.001,width*.18))*inside;
                float skirt = exp2(-abs(distance)/width)*inside;
                float halo = exp2(-abs(distance)/(width*2.7))*spread;
                float edgeHalo = exp2(-abs(shape)/max(1,_GlowSpread*.34)) * exp2(-abs(distance)/(width*3));
                // Overlay UI is rendered after camera bloom. Draw its soft exterior light explicitly.
                // Colored coverage keeps the gold visible on cream paper; additive light carries the white core.
                float opacity = saturate((skirt*.5 + core*.4) * active * _GlowIntensity);
                half3 tint = AlbumGoldRamp(saturate(core*.85+skirt*.55));
                half3 light = _GoldColor.rgb*(halo*.22+edgeHalo*.24)
                    + _IvoryColor.rgb*core*.85;
                half4 c = half4(tint*opacity+light*active*_GlowIntensity,opacity)*i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                c *= UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
