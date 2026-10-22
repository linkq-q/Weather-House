Shader "Weather House/Oil Paint Surface"
{
    Properties
    {
        _BaseColor("Base Pigment", Color) = (.46,.56,.34,1)
        _ShadowColor("Cool Shadow Pigment", Color) = (.19,.30,.29,1)
        _HighlightColor("Warm Light Pigment", Color) = (.89,.79,.48,1)
        _BrushScale("Brush Size Frequency (Object Space)", Range(.5,24)) = 8
        _StrokeColor("Independent Stroke Pigment", Color) = (.72,.46,.26,1)
        _StrokeDensity("Stroke Coverage Density", Range(0,2)) = 1.25
        _BrushStrength("Pigment and Dry Brush Strength", Range(0,1)) = .6
        _BrushAtlas("Four Oil Brush Stamps (2x2)", 2D) = "black" {}
        [ToggleUI] _UseBrushAtlas("Use Brush Stamp Atlas", Float) = 0
        _StrokeScatter("Stroke Direction Scatter (Degrees)", Range(0,90)) = 42
        _StrokeAspect("Stroke Length / Width", Range(.25,8)) = 1.35
        _StrokeAngle("Stroke Direction (Degrees)", Range(-180,180)) = 25
        _ColorSteps("Soft Lighting Color Steps", Range(2,12)) = 6
        _ShadowEdgeColor("Colored Shadow Transition", Color) = (.18,.42,.46,1)
        _ShadowEdgeWidth("Form Shadow Transition Width", Range(.02,.4)) = .14
        _ShadowEdgeStrength("Colored Transition Strength", Range(0,1)) = .55
        _ShadowFill("Minimum Shadow Fill", Range(0,1)) = .22
        _Impasto("Raised Paint Relief", Range(0,2)) = .45
        _Smoothness("Paint Sheen", Range(0,1)) = .38
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _ShadowColor, _HighlightColor;
            half4 _StrokeColor, _ShadowEdgeColor;
            float _StrokeDensity, _ShadowEdgeWidth, _ShadowEdgeStrength, _ShadowFill;
            float _BrushScale, _BrushStrength, _StrokeAspect, _StrokeAngle;
            float _ColorSteps, _Impasto, _Smoothness;
            float _UseBrushAtlas, _StrokeScatter;
            float4 _BrushAtlas_TexelSize;
        CBUFFER_END

        TEXTURE2D(_BrushAtlas);
        SAMPLER(sampler_BrushAtlas);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float3 BrushHash(float2 cell)
        {
            float3 p = frac(float3(cell.xyx) * float3(.1031,.1030,.0973));
            p += dot(p, p.yxz + 33.33);
            return frac((p.xxy + p.yzz) * p.zyx);
        }

        float PigmentPatch(float2 p)
        {
            float2 cell = floor(p), f = frac(p);
            f = f * f * (3 - 2 * f);
            return lerp(lerp(BrushHash(cell).x, BrushHash(cell + float2(1,0)).x, f.x),
                lerp(BrushHash(cell + float2(0,1)).x, BrushHash(cell + 1).x, f.x), f.y);
        }

        float StampInk(float2 uv, float2 dx, float2 dy, float tile)
        {
            float2 size = max(_BrushAtlas_TexelSize.zw, 4);
            float2 gx = dx * size * .5, gy = dy * size * .5;
            float lod = .5 * log2(max(max(dot(gx,gx),dot(gy,gy)),1e-6));
            // Keep at least 16 texels across each tile at the coarsest sampled mip.
            // A mip-aware inset prevents even bilinear/trilinear taps reaching another tile.
            lod = clamp(lod,0,max(0,log2(min(size.x,size.y) * .5) - 4));
            float2 inset = (exp2(ceil(lod)) + 1) * _BrushAtlas_TexelSize.xy;
            float2 tileOrigin = float2(fmod(tile,2),floor(tile * .5)) * .5;
            float2 atlasUV = tileOrigin + clamp(uv,inset,1 - inset) * .5;
            float ink = SAMPLE_TEXTURE2D_LOD(_BrushAtlas,sampler_BrushAtlas,atlasUV,lod).r;
            float inside = step(0,uv.x) * step(uv.x,1) * step(0,uv.y) * step(uv.y,1);
            // The black background is a mask, never a color applied to the material.
            return saturate((ink - .015) / .985) * inside;
        }

        float4 BrushPlane(float2 plane, float seed)
        {
            if (_StrokeDensity <= .0001) return 0;
            float density = max(_StrokeDensity,.2);
            float lowDensityOpacity = saturate(_StrokeDensity / .2);
            // Scale center spacing and cell-space stamp size together: density does not shrink each stroke.
            float2 p = plane * density + float2(seed,seed * .63);
            float2 cell = floor(p);
            float2 dpdx = ddx(p), dpdy = ddy(p);
            float pigment = 0, coverage = 0, height = 0, dry = 0;
            [unroll] for (int y = -2; y <= 2; y++)
            {
                [unroll] for (int x = -2; x <= 2; x++)
                {
                    float2 id = cell + float2(x,y);
                    float3 random = BrushHash(id + seed);
                    float3 detail = BrushHash(id + float2(seed * 2.71,19.23));
                    float sine, cosine;
                    sincos(radians(_StrokeAngle + (random.z * 2 - 1) * _StrokeScatter),sine,cosine);
                    float2 along = float2(cosine,sine), across = float2(-sine,cosine);
                    float2 q = p - (id + .5 + (random.xy - .5) * .56);
                    float aspect = sqrt(clamp(_StrokeAspect,.25,8));
                    float2 stampSize = float2(aspect,1 / aspect) * lerp(.94,1.30,detail.x);
                    // Density 2 has at most 2.8-cell support; a 5x5 neighborhood includes the rotated bounds.
                    stampSize *= min(1,1.40 / max(stampSize.x,stampSize.y));
                    stampSize *= density;
                    float2 local = float2(dot(q,along),dot(q,across)) / stampSize;
                    // Reject outside support before the atlas read or procedural ink calculation.
                    if (any(abs(local) > .5)) continue;
                    float2 duvdx = float2(dot(dpdx,along),dot(dpdx,across)) / stampSize;
                    float2 duvdy = float2(dot(dpdy,along),dot(dpdy,across)) / stampSize;
                    float ink;
                    if (_UseBrushAtlas > .5)
                    {
                        ink = StampInk(local + .5,duvdx,duvdy,min(3,floor(detail.z * 4)));
                    }
                    else
                    {
                        // Texture-free fallback: broad broken daubs, without periodic fibers.
                        float rough = PigmentPatch(local * float2(7,9) + id);
                        float boundary = 1 - length(local * 2) + (rough - .5) * .23;
                        float aa = max(.025,max(length(duvdx),length(duvdy)) * 2);
                        float mask = smoothstep(-aa,aa,boundary);
                        ink = mask * lerp(.50,1,PigmentPatch(local * float2(9,6) + id + seed));
                    }
                    float mask = smoothstep(.035,.62,ink);
                    float opacity = mask * lerp(.76,.97,detail.y) * lowDensityOpacity;
                    float tone = (random.z - .5) * .82 + (ink - .55) * .12;
                    // Ordered over-compositing keeps individual coats and broken edges;
                    // averaging overlapping marks would turn the surface into smooth stripes.
                    pigment = lerp(pigment,tone,opacity);
                    height = lerp(height,.18 + ink * .82,opacity) + coverage * opacity * .08;
                    dry = lerp(dry,(1 - ink) * mask,opacity);
                    coverage = opacity + coverage * (1 - opacity);
                }
            }
            pigment = pigment * 1.10 + (PigmentPatch(p * .31) - .5) * .04;
            return float4(pigment,coverage,height,dry);
        }

        float4 SurfaceBrush(float3 positionOS, float3 normalOS)
        {
            float3 weights = abs(normalize(normalOS));
            weights *= weights; weights *= weights; weights *= weights;
            weights /= max(dot(weights,float3(1,1,1)),.0001);
            float3 p = positionOS * max(_BrushScale,.5);
            return BrushPlane(p.zy,13.7) * weights.x + BrushPlane(p.xz,37.2) * weights.y + BrushPlane(p.xy,71.4) * weights.z;
        }

        float3 PaintNormal(float3 positionWS, float3 normalWS, float height)
        {
            float3 dx = ddx(positionWS), dy = ddy(positionWS);
            float3 rx = cross(dy,normalWS), ry = cross(normalWS,dx);
            float determinant = dot(dx,rx);
            float3 gradient = (ddx(height) * rx + ddy(height) * ry) * sign(determinant) / max(abs(determinant),1e-10);
            // Limit relief at silhouettes and overlapping stroke edges.
            gradient *= min(1, .85 / max(length(gradient),.0001));
            return normalize(normalWS - gradient);
        }

        float PaintBands(float lightValue)
        {
            float steps = max(1,round(_ColorSteps) - 1);
            float value = saturate(lightValue) * steps;
            float aa = min(.22,fwidth(value));
            float grouped = (floor(value) + smoothstep(.28 - aa,.72 + aa,frac(value))) / steps;
            // Retain continuous modeled form; color grouping is only a small paint accent.
            return lerp(saturate(lightValue),grouped,.18);
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ForwardVert
            #pragma fragment ForwardFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half3 normalOS : TEXCOORD3;
                half fog : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings ForwardVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.normalOS = input.normalOS;
                output.fog = ComputeFogFactor(position.positionCS.z);
                output.shadowCoord = GetShadowCoord(position);
                return output;
            }
            half4 ForwardFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                float4 brush = SurfaceBrush(input.positionOS,input.normalOS);
                float relief = brush.z * _Impasto * .014 / max(_BrushScale,.5);
                float3 paintedNormal = PaintNormal(input.positionWS,normalWS,relief);
                float4 shadowCoord = input.shadowCoord;
                #if defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light sun = GetMainLight(shadowCoord);
                float visibility = sun.shadowAttenuation * sun.distanceAttenuation;
                // Model the large light/shadow shape with geometry; paint relief only affects the sheen below.
                float geometricNdotL = dot(normalWS,sun.direction);
                float form = saturate(geometricNdotL * .72 + .28);
                float tone = PaintBands(form);
                half3 palette = lerp(_ShadowColor.rgb,_BaseColor.rgb,smoothstep(-.08,.82,tone));
                palette = lerp(palette,_HighlightColor.rgb,smoothstep(.35,1,tone) * .65);
                // Stroke hue is an independent pigment; only this stroke's paint load varies its value.
                half3 strokePigment = _StrokeColor.rgb * max(.2,1 + brush.x);
                float coat = saturate(brush.y * _BrushStrength);
                palette = lerp(palette,strokePigment,coat);
                palette = lerp(palette,_BaseColor.rgb,brush.w * _BrushStrength * .14);

                float edgeWidth = max(_ShadowEdgeWidth,.02);
                float edgeOffset = brush.x * edgeWidth * 1.1 * _BrushStrength;
                float edgeDistance = abs(geometricNdotL + edgeOffset);
                float edgeAA = min(edgeWidth * .35,fwidth(geometricNdotL));
                float formEdge = 1 - smoothstep(edgeWidth * .12,edgeWidth + edgeAA,edgeDistance);
                // Soft cast-shadow samples can also carry pigment, while their actual penumbra stays with URP.
                float castEdge = saturate(4 * sun.shadowAttenuation * (1 - sun.shadowAttenuation)) * form;
                float dryBreak = lerp(1,saturate(.55 + brush.y * .45 - brush.w * .70),_BrushStrength);
                float edgeBlend = saturate(max(formEdge,castEdge) * dryBreak * _ShadowEdgeStrength);
                palette = lerp(palette,_ShadowEdgeColor.rgb,edgeBlend);

                half3 ambient = max(SampleSH(normalWS),0);
                half3 fill = max(ambient * .8,half3(_ShadowFill,_ShadowFill,_ShadowFill));
                // Cast shadows attenuate direct light once. Colored ambient/fill keeps the chosen shadow pigment readable.
                half3 color = palette * (fill + sun.color * ((.15 + .85 * form) * visibility));
                float3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 halfDirection = SafeNormalize(sun.direction + viewDirection);
                float exponent = lerp(12,180,_Smoothness * _Smoothness);
                float sheen = pow(saturate(dot(paintedNormal,halfDirection)),exponent);
                sheen *= (.025 + _Impasto * .14) * lerp(.55,1,brush.z) * visibility * saturate(dot(normalWS,sun.direction));
                color += sun.color * lerp(half3(1,1,1),_HighlightColor.rgb,.35) * sheen;
                return half4(MixFog(color,input.fog),1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection, _LightPosition;
            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                float3 normal = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 direction = normalize(_LightPosition - world);
                #else
                    float3 direction = _LightDirection;
                #endif
                float4 position = TransformWorldToHClip(ApplyShadowBias(world,normal,direction));
                #if UNITY_REVERSED_Z
                    position.z = min(position.z,UNITY_NEAR_CLIP_VALUE);
                #else
                    position.z = max(position.z,UNITY_NEAR_CLIP_VALUE);
                #endif
                return position;
            }
            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            struct DepthVaryings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            DepthVaryings DepthVert(Attributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            half4 DepthFrag(DepthVaryings input) : SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input); return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            struct NormalVaryings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            NormalVaryings NormalsVert(Attributes input)
            {
                NormalVaryings output = (NormalVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 NormalsFrag(NormalVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // Keep scene edge detection on the modeled surface, not tiny paint ridges.
                float3 normal = NormalizeNormalPerPixel(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(normal);
                    return half4(PackFloat2To888(saturate(oct * .5 + .5)),0);
                #else
                    return half4(normal,0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
