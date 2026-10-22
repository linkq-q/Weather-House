Shader "Hidden/Weather House/Oil Paint Post"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _SourceSize;
            float4 _BrushDirection;
            float _OilStrength, _Radius, _ColorSteps;
            float _PixelSize, _PixelStrength, _SplitScreen, _SplitPosition;
        CBUFFER_END

        float2 ViewUV(float2 textureUV)
        {
            return (textureUV - _BlitScaleBias.zw) / _BlitScaleBias.xy;
        }

        float4 ReadColor(float2 uv)
        {
            // Clamp to the current viewport, including when RTHandle's backing allocation
            // is larger than this camera or its native targetTexture screenshot.
            float2 edge = .5 * _SourceSize.zw;
            uv = clamp(uv, edge, 1.0 - edge);
            return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp,
                uv * _BlitScaleBias.xy + _BlitScaleBias.zw, 0);
        }

        bool OriginalSide(float2 uv)
        {
            return _SplitScreen > .5 && uv.x < _SplitPosition;
        }

        float3 GroupTones(float3 color)
        {
            if (_ColorSteps < 1.5) return color;
            // Quantize perceptual brightness gently, preserving RGB ratios and HDR values.
            // No gray, sepia, paper tint, or saturation reduction is applied to the image.
            float light = max(dot(color, float3(.2126, .7152, .0722)), 0);
            float level = floor(sqrt(light) * _ColorSteps + .5) / _ColorSteps;
            float3 grouped = color * ((level * level + .001) / (light + .001));
            return lerp(color, grouped, .25);
        }

        float4 OilFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = ViewUV(input.texcoord);
            float4 original = ReadColor(uv);
            if (OriginalSide(uv) || _OilStrength <= 0) return original;

            float3 sums[4], squares[4];
            [unroll] for (int q = 0; q < 4; ++q)
            {
                sums[q] = 0;
                squares[q] = 0;
            }

            float2 along = _BrushDirection.xy * _BrushDirection.z;
            float2 across = float2(-_BrushDirection.y, _BrushDirection.x) * _BrushDirection.w;
            // Four overlapping 3x3 regions share one fixed 5x5 neighborhood: 25 reads.
            // Elongating that neighborhood gives restrained directional brush shapes.
            [unroll] for (int y = -2; y <= 2; ++y)
            {
                [unroll] for (int x = -2; x <= 2; ++x)
                {
                    float2 offset = (along * x + across * y) * (_Radius * .5) * _SourceSize.zw;
                    float3 color = (x == 0 && y == 0) ? original.rgb : ReadColor(uv + offset).rgb;
                    float3 square = color * color;
                    if (x <= 0 && y <= 0) { sums[0] += color; squares[0] += square; }
                    if (x >= 0 && y <= 0) { sums[1] += color; squares[1] += square; }
                    if (x <= 0 && y >= 0) { sums[2] += color; squares[2] += square; }
                    if (x >= 0 && y >= 0) { sums[3] += color; squares[3] += square; }
                }
            }

            float3 painted = original.rgb;
            float bestScore = 1e20;
            [unroll] for (int region = 0; region < 4; ++region)
            {
                float3 mean = sums[region] / 9.0;
                float3 variance = max(squares[region] / 9.0 - mean * mean, 0);
                float3 difference = mean - original.rgb;
                // Center-color penalty keeps thin silhouettes from being replaced by a
                // flat background region, while low-variance colors merge into patches.
                float score = dot(variance, float3(.2126, .7152, .0722)) + .25 * dot(difference, difference);
                if (score < bestScore) { bestScore = score; painted = mean; }
            }
            return float4(lerp(original.rgb, GroupTones(painted), _OilStrength), original.a);
        }

        float4 PixelFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = ViewUV(input.texcoord);
            float4 original = ReadColor(uv);
            if (OriginalSide(uv) || _PixelStrength <= 0 || _PixelSize <= 1) return original;
            float2 cell = max(1.0, _PixelSize) * _SourceSize.zw;
            float2 center = (floor(uv / cell) + .5) * cell;
            float3 pixel = ReadColor(center).rgb;
            return float4(lerp(original.rgb, pixel, _PixelStrength), original.a);
        }
        ENDHLSL

        Pass
        {
            Name "Oil Color Regions"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment OilFragment
            ENDHLSL
        }
        Pass
        {
            Name "Micro Pixels"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment PixelFragment
            ENDHLSL
        }
    }
    FallBack Off
}
