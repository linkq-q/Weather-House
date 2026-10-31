Shader "Hidden/Weather House/Animal Paper Screen"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        float4 _AnimalTexel;
        float4 _AnimalOutlineLod;
        float2 _AnimalFilterAxis, _AnimalEdgeDirection;
        float _AnimalMergeRadius, _AnimalEdgeEnergy;
        half4 _AnimalEdgeColor;
        TEXTURE2D_X(_AnimalMerged);
        float EyeDepth(float raw)
        {
            if (unity_OrthoParams.w < .5) return LinearEyeDepth(raw, _ZBufferParams);
            #if UNITY_REVERSED_Z
                raw = 1 - raw;
            #endif
            return lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
        }
        float4 Data(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0); }
        ENDHLSL
        Pass
        {
            Name "Merge lighting before bands"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Merge
            float4 Merge(Varyings input) : SV_Target
            {
                float4 center = Data(input.texcoord);
                if (center.a < .5 || _AnimalMergeRadius < .01) return center;
                float2 sum = 0; float total = 0;
                // Remove narrow islands before thresholding, respecting depth and membership.
                [unroll] for (int i = -8; i <= 8; i++)
                {
                    float2 uv = input.texcoord + _AnimalFilterAxis * _AnimalTexel.xy * (_AnimalMergeRadius * i / 8.0);
                    float4 sample = Data(uv);
                    float weight = (9 - abs(i)) * saturate(1 - abs(sample.b - center.b) / .12);
                    weight *= sample.a > .5 && abs(sample.a - center.a) < .25 ? 1 : 0;
                    sum += sample.rg * weight; total += weight;
                }
                return float4(sum / max(total, .001), center.ba);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Connected directional silhouette"
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil { Ref 1 ReadMask 1 WriteMask 0 Comp NotEqual Pass Keep }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Edge
            half4 Edge(Varyings input) : SV_Target
            {
                float targetDepth = EyeDepth(SampleSceneDepth(input.texcoord));
                float alpha = 0;
                // Sweep the silhouette, not a displaced copy: thin legs stay joined to the edge.
                [loop] for (int i = 1; i <= 32; i++)
                {
                    float distance = i * .5 * _AnimalTexel.z;
                    float2 uv = input.texcoord - _AnimalEdgeDirection * _AnimalTexel.xy * distance;
                    if (any(uv < 0) || any(uv > 1)) continue;
                    float4 sample = Data(uv);
                    float width = max(0, sample.a - 1) * _AnimalTexel.z;
                    float lod = smoothstep(_AnimalOutlineLod.x, _AnimalOutlineLod.y, sample.b);
                    width *= lerp(1, _AnimalOutlineLod.z, lod);
                    if (width < distance || sample.b > targetDepth + .015) continue;
                    // Directional dilation already selects the lit silhouette. Use the merged
                    // shadow field so subpixel self-shadow noise cannot stripe a wide edge.
                    float lit = SAMPLE_TEXTURE2D_X_LOD(_AnimalMerged, sampler_PointClamp, uv, 0).g;
                    alpha = max(alpha, lit * saturate(width - distance + .5));
                }
                return half4(_AnimalEdgeColor.rgb, alpha * _AnimalEdgeColor.a * _AnimalEdgeEnergy);
            }
            ENDHLSL
        }
    }
}
