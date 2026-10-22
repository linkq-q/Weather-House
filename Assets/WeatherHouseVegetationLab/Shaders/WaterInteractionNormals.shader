Shader "Hidden/WeatherHouse/WaterInteractionNormals"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        // Rect stores world X/Z minimum and inverse size. Fixed-size RTs use full [0,1] UVs.
        float4 _FieldRect;
        float4 _Cell; // texel UV, world cell size
        float4 _Step; // integration dt, decay multiplier, wave speed squared, 1/substeps
        float4 _Wake; // world center X/Z, radius, height injection
        float4 _WakeDirection;
        float4 _Ring; // world center X/Z, radius, height injection

        float2 State(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).rg;
        }

        float Impulse(float2 world)
        {
            float result = 0;
            if (_Wake.w > 0)
            {
                float2 delta = (world - _Wake.xy) / max(_Wake.z, .05);
                float axial = dot(delta, _WakeDirection.xy);
                float lateral = dot(delta, float2(-_WakeDirection.y, _WakeDirection.x));
                float behind = max(-axial, 0);
                float spread = .48 + behind * .28;
                float ridges = exp(-pow((abs(lateral) - spread) * 3.3, 2));
                float trough = exp(-lateral * lateral * 5);
                float envelope = exp(-pow((axial + .45) * .8, 2));
                envelope *= 1 - smoothstep(.35, 1.2, axial);
                envelope *= 1 - smoothstep(2, 3.5, length(delta));
                result += (ridges * .7 - trough * .45) * envelope * _Wake.w;
            }
            if (_Ring.w > 0)
            {
                float radial = length(world - _Ring.xy) / max(_Ring.z, .05);
                float crest = exp(-pow((radial - .85) * 3.5, 2));
                float trough = .4 * exp(-radial * radial * 4);
                result += (crest - trough) * _Ring.w;
            }
            return result * _Step.w;
        }

        float4 Integrate(Varyings input) : SV_Target
        {
            float2 uv = input.texcoord;
            float2 current = State(uv);
            float left = State(uv - float2(_Cell.x, 0)).r;
            float right = State(uv + float2(_Cell.x, 0)).r;
            float down = State(uv - float2(0, _Cell.y)).r;
            float up = State(uv + float2(0, _Cell.y)).r;
            float laplacian = (left + right - 2 * current.x) / (_Cell.z * _Cell.z)
                + (down + up - 2 * current.x) / (_Cell.w * _Cell.w);
            float velocity = (current.y + laplacian * _Step.z * _Step.x) * _Step.y;
            float height = (current.x + velocity * _Step.x) * _Step.y;
            float2 world = _FieldRect.xy + uv / _FieldRect.zw;
            height += Impulse(world);
            float2 edge = min(uv, 1 - uv) / _Cell.xy;
            float absorption = smoothstep(.5, 3.5, min(edge.x, edge.y));
            return float4(clamp(height, -.15, .15), clamp(velocity, -.7, .7), 0, 1) * float4(absorption, absorption, 1, 1);
        }

        float4 EncodeNormals(Varyings input) : SV_Target
        {
            float2 uv = input.texcoord;
            float left = State(uv - float2(_Cell.x, 0)).r;
            float right = State(uv + float2(_Cell.x, 0)).r;
            float down = State(uv - float2(0, _Cell.y)).r;
            float up = State(uv + float2(0, _Cell.y)).r;
            float2 slope = float2((right - left) / (2 * _Cell.z), (up - down) / (2 * _Cell.w));
            // Texture channels are world X, world Z, world up Y (a conventional tangent normal).
            float3 normal = normalize(float3(-slope, 1));
            // D3D11 float16 render targets truncate toward zero. Without this centred
            // quantization, an arbitrarily small negative deviation from 0.5 becomes
            // 0.49975586, leaving a one-sided normal floor over the spreading field.
            // 1/1024 in normal space is 1/2048 in encoded space: exactly representable
            // on either side of 0.5. Sub-half-step components become exactly neutral.
            normal.xy = sign(normal.xy) * floor(abs(normal.xy) * 1024.0 + 0.5) / 1024.0;
            normal.z = sqrt(saturate(1.0 - dot(normal.xy, normal.xy)));
            return float4(normal * .5 + .5, 1);
        }
        ENDHLSL

        Pass
        {
            Name "Integrate height and velocity"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Integrate
            ENDHLSL
        }
        Pass
        {
            Name "Encode world-aligned surface normals"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment EncodeNormals
            ENDHLSL
        }
    }
    Fallback Off
}
