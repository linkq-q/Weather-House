Shader "Weather House/Original Trees/Study Sky"
{
    Properties
    {
        _Horizon("Horizon", Color) = (0.31,0.45,0.62,1)
        _Zenith("Zenith", Color) = (0.55,0.70,0.80,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _Horizon, _Zenith;
            struct Varyings { float4 positionCS:SV_POSITION; float3 direction:TEXCOORD0; };
            Varyings Vert(float3 positionOS:POSITION)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(positionOS); o.direction=positionOS; return o;
            }
            half4 Frag(Varyings i):SV_Target
            { return half4(lerp(_Horizon.rgb,_Zenith.rgb,saturate(normalize(i.direction).y*2)),1); }
            ENDHLSL
        }
    }
}
