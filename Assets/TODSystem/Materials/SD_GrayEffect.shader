Shader "PostProcessTest/GrayEffect"
{
    Properties
    {
        _Red ("Red", Range(0,1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }
        LOD 100
        ZWrite Off Cull Off

        Pass
        {
            Name "GrayEffectPass"

            // 只绘制模板值等于 1 的像素
            Stencil
            {
                Ref 1
                Comp Equal
            }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment frag

            CBUFFER_START(UnityPerMaterial)

            float _Red;

            CBUFFER_END

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearRepeat, input.texcoord);
                float gray = dot(col.rgb, float3(0.2126, 0.7152, 0.0722));
                //return float4(_Red,col.gb,1);
                return float4(gray, gray, gray, col.a);
            }
            ENDHLSL
        }
    }
}