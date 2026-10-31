Shader "Hidden/WeatherHouse/LifePhotoDepthCopy"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D_FLOAT(_MainTex); SAMPLER(sampler_MainTex);
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings Vert(uint vertexID:SV_VertexID)
            {
                Varyings output;output.positionCS=GetFullScreenTriangleVertexPosition(vertexID);output.uv=GetFullScreenTriangleTexCoord(vertexID);
                return output;
            }
            float Frag(Varyings input):SV_Target { return SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv).r; }
            ENDHLSL
        }
        Pass
        {
            Name "BoundSceneDepth"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings Vert(uint vertexID:SV_VertexID)
            {
                Varyings output;output.positionCS=GetFullScreenTriangleVertexPosition(vertexID);output.uv=GetFullScreenTriangleTexCoord(vertexID);
                return output;
            }
            float Frag(Varyings input):SV_Target { return SampleSceneDepth(input.uv); }
            ENDHLSL
        }
    }
}
