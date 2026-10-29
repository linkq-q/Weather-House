Shader "Weather House/Life/Journal Surface"
{
    Properties
    {
        _BaseMap ("Page or cover", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Paper or cloth normal", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0,1)) = 0.35
        _OcclusionMap ("Paper or cloth AO", 2D) = "white" {}
        _OcclusionStrength ("AO strength", Range(0,1)) = 0.28
        _ShadeStrength ("Soft directional shading", Range(0,1)) = 0.30
        _MinimumShade ("Readability floor", Range(0,1)) = 0.80
        _FoldOcclusion ("Binding fold occlusion", Range(0,1)) = 0
        _PhotoSheen ("Satin photo sheen", Range(0,1)) = 0.24
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Back ZWrite On ZTest LEqual
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half3 normalWS : TEXCOORD1; half4 tangentWS : TEXCOORD2; float4 detailUv : TEXCOORD3; float2 surfaceUv : TEXCOORD4; float3 positionWS : TEXCOORD5; };
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _BumpMap_ST, _OcclusionMap_ST;
            half4 _BaseColor;
            half _BumpScale, _OcclusionStrength, _ShadeStrength, _MinimumShade, _FoldOcclusion;
            half _PhotoSheen;
            float4 _PhotoRects[8];
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = half4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w * GetOddNegativeScale());
                // The composed page changes only _BaseMap_ST. Surface relief stays in its own
                // authored UV0, so fibers do not shrink into the full-book snapshot crop.
                output.detailUv = float4(TRANSFORM_TEX(input.uv, _BumpMap), TRANSFORM_TEX(input.uv, _OcclusionMap));
                output.surfaceUv = input.uv;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                half3 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                half3 normal = normalize(input.normalWS), tangent = normalize(input.tangentWS.xyz);
                half photo = 0, rim = 0, contact = 0;
                float2 photoUv = 0;
                float2 pixel = float2(1.0 / 1672.0, 1.0 / 941.0);
                [unroll] for (int index = 0; index < 8; index++)
                {
                    float4 rect = _PhotoRects[index];
                    if (rect.z <= rect.x) continue;
                    float2 edge = min(input.uv - rect.xy, rect.zw - input.uv) / pixel;
                    float inside = smoothstep(-.5, .5, min(edge.x, edge.y));
                    if (inside > photo) photoUv = (input.uv - rect.xy) / (rect.zw - rect.xy);
                    photo = max(photo, inside);
                    // A narrow upper lip and a soft lower/right contact edge imply paper thickness.
                    float top = (rect.w - input.uv.y) / pixel.y;
                    float left = (input.uv.x - rect.x) / pixel.x;
                    rim = max(rim, inside * exp2(-max(0, min(top, left)) * 1.4));
                    float2 shifted = input.uv - float2(1.2, -1.8) * pixel;
                    float2 shadowEdge = min(shifted - rect.xy, rect.zw - shifted) / pixel;
                    contact = max(contact, (1 - inside) * smoothstep(-2, 1, min(shadowEdge.x, shadowEdge.y)));
                }
                half3 bitangent = cross(normal, tangent) * input.tangentWS.w;
                half3 relief = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.detailUv.xy), _BumpScale);
                relief = lerp(relief, half3(0, 0, 1), photo * .92h);
                normal = normalize(tangent * relief.x + bitangent * relief.y + normal * relief.z);
                // Fixed grazing studio light reveals the curved gutter independently of the
                // world exposure. A level page stays bright; only surfaces facing away darken.
                half3 light = normalize(half3(-.78h, .52h, .35h));
                half shade = saturate(1.h + _ShadeStrength * (dot(normal, light) - light.y));
                half ao = lerp(1.h, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, input.detailUv.zw).g, _OcclusionStrength);
                ao = lerp(ao, 1.h, photo);
                half u = saturate(input.surfaceUv.x), v = saturate(input.surfaceUv.y);
                half trough = 4.h * u * (1.h - u);
                half ends = smoothstep(0.h, .05h, v) * smoothstep(0.h, .05h, 1.h - v);
                half fold = 1.h - _FoldOcclusion * trough * trough * ends;
                color *= max(_MinimumShade, shade * ao) * fold;
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 reflected = reflect(-view, normal);
                // A broad fixed studio window; only the page normal/view moves the reflection.
                float window = photoUv.x + photoUv.y * .45 - .82 + reflected.x * .9 + reflected.z * .35;
                half broad = exp2(-window * window * 32);
                half facing = pow(saturate(dot(normal, normalize(view + light))), 8);
                half sheen = photo * _PhotoSheen * broad * (.28h + .72h * facing);
                color *= 1.h - contact * .10h;
                color = lerp(color, half3(1.h, .97h, .88h), saturate(sheen + rim * .055h));
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
