Shader "Weather House/Drawer Letter Ink"
{
    Properties
    {
        _MainTex("Font Atlas", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Depth Test", Float) = 4
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest [_ZTest]
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.uv=input.uv; output.color=input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                return half4(input.color.rgb,input.color.a*SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv).a);
            }
            ENDHLSL
        }
    }
}
