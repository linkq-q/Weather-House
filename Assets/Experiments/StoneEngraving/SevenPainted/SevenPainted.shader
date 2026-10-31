Shader "Weather House/Stone Engraving/Seven Mineral Paint"
{
    Properties
    {
        [MainTexture] _BaseMap("Stone texture", 2D) = "white" {}
        [MainColor] _BaseColor("Stone color", Color) = (.5566,.5566,.5566,1)
        [Normal] _BumpMap("Stone normal", 2D) = "bump" {}
        _BumpScale("Stone normal strength", Range(0,2)) = 1
        [Normal] _EngravingNormal("Carving normal", 2D) = "bump" {}
        _EngravingMask("Carving depth / AO / unused / coverage", 2D) = "black" {}
        _EngravingStrength("Carving strength", Range(0,2)) = 1
        _CavityTint("Groove darkening", Range(0,1)) = .28
        _GrooveDepth("Groove depth in metres", Range(0,.1)) = .08
        _GrooveShadow("Groove self shadow", Range(0,1)) = .65
        _DiscoveryContrast("Gameplay groove contrast", Range(0,1)) = .72
        _DiscoveryWidth("Gameplay groove expansion (pixels)", Range(0,1.5)) = .4
        [HideInInspector] _GameplayReadability("Gameplay view only", Float) = 0
        _Smoothness("Stone smoothness", Range(0,1)) = .12
        [Toggle] _Faceted("Use flat stone facets", Float) = 0
        _ProjectionCenter("Carving center (local)", Vector) = (0,0,0,0)
        _ProjectionRight("Carving right (local)", Vector) = (1,0,0,0)
        _ProjectionUp("Carving up (local)", Vector) = (0,1,0,0)
        _ProjectionSize("Width / height / depth extent (local)", Vector) = (3.6,2.4,2,0)
        _PaintGrain("Dry brush pigment grain", 2D) = "white" {}
        _PaintColor("Mineral paint", Color) = (.93,.88,.73,1)
        _PaintStrength("Paint amount", Range(0,1)) = 0
        _PaintFill("Interior mineral pigment", 2D) = "black" {}
        _FillStrength("Interior pigment strength", Range(0,1)) = 0
        _FillUV("Pigment UV scale and offset", Vector) = (1,1,0,0)
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _Cutoff("Cutoff", Float) = .5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "UniversalMaterialType"="Lit" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_EngravingNormal); SAMPLER(sampler_EngravingNormal);
            TEXTURE2D(_EngravingMask); SAMPLER(sampler_EngravingMask);
            TEXTURE2D(_PaintGrain); SAMPLER(sampler_PaintGrain);
            TEXTURE2D(_PaintFill); SAMPLER(sampler_PaintFill);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _BaseColor;
                float4 _ProjectionCenter, _ProjectionRight, _ProjectionUp, _ProjectionSize;
                float _BumpScale, _EngravingStrength, _CavityTint, _Smoothness, _Cull, _Cutoff;
                float _GrooveDepth, _GrooveShadow, _Faceted;
                float _DiscoveryContrast, _DiscoveryWidth, _GameplayReadability;
                half4 _PaintColor; half _PaintStrength, _FillStrength; float4 _FillUV;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS:POSITION; float3 normalOS:NORMAL;
                float4 tangentOS:TANGENT; float2 uv:TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float3 positionWS:TEXCOORD0; float3 positionOS:TEXCOORD1;
                half3 normalWS:TEXCOORD2; half4 tangentWS:TEXCOORD3;
                float2 uv:TEXCOORD4; half fog:TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes v)
            {
                Varyings o=(Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n=GetVertexNormalInputs(v.normalOS,v.tangentOS);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS; o.positionOS=v.positionOS.xyz;
                o.normalWS=n.normalWS; o.tangentWS=half4(n.tangentWS,v.tangentOS.w*GetOddNegativeScale());
                o.uv=TRANSFORM_TEX(v.uv,_BaseMap); o.fog=ComputeFogFactor(p.positionCS.z);
                return o;
            }
            float GrooveAt(float2 uv,float2 dx,float2 dy)
            {
                return SAMPLE_TEXTURE2D_GRAD(_EngravingMask,sampler_EngravingMask,uv,dx,dy).r;
            }
            float2 TraceGroove(float2 uv,float2 ray,float2 dx,float2 dy)
            {
                float depth=0, sampled=GrooveAt(uv,dx,dy);
                if(sampled<.001)return uv;
                float previousSample=sampled;
                float2 stepUV=ray/16;
                [loop] for(int n=0;n<16;n++)
                {
                    if(depth>=sampled)break;
                    previousSample=sampled;
                    uv-=stepUV;depth+=1.0/16;
                    sampled=GrooveAt(uv,dx,dy);
                }
                float after=sampled-depth;
                float before=previousSample-depth+1.0/16;
                float weight=saturate(after/min(after-before,-.0001));
                return uv+stepUV*weight;
            }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 geometricNormal=normalize(i.normalWS);
                if (_Faceted > .5)
                {
                    half3 facet=normalize(cross(ddy(i.positionWS),ddx(i.positionWS)));
                    geometricNormal=dot(facet,geometricNormal)<0 ? -facet : facet;
                }
                half3 tangent=normalize(i.tangentWS.xyz);
                half3 bitangent=cross(geometricNormal,tangent)*i.tangentWS.w;
                half3 stoneNormal=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,i.uv),_BumpScale);
                half3 normal=normalize(tangent*stoneNormal.x+bitangent*stoneNormal.y+geometricNormal*stoneNormal.z);
                float3 local=i.positionOS-_ProjectionCenter.xyz;
                float3 rightOS=normalize(_ProjectionRight.xyz), upOS=normalize(_ProjectionUp.xyz);
                float3 frontOS=normalize(cross(upOS,rightOS));
                float2 uv=float2(dot(local,rightOS),dot(local,upOS))/_ProjectionSize.xy+.5;
                half3 rightWS=normalize(TransformObjectToWorldDir(rightOS));
                half3 upWS=normalize(TransformObjectToWorldDir(upOS));
                half3 frontWS=normalize(TransformObjectToWorldNormal(frontOS));
                half valid=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);
                valid*=step(abs(dot(local,frontOS)),_ProjectionSize.z);
                valid*=smoothstep(.15,.65,dot(geometricNormal,frontWS));
                float2 dx=ddx(uv),dy=ddy(uv);
                float2 sizeWS=_ProjectionSize.xy*float2(
                    length(mul((float3x3)GetObjectToWorldMatrix(),rightOS)),
                    length(mul((float3x3)GetObjectToWorldMatrix(),upOS)));
                half3 view=GetWorldSpaceNormalizeViewDir(i.positionWS);
                if(valid>.001 && _EngravingStrength>0 && _GrooveDepth>0)
                {
                    float2 ray=float2(dot(view,rightWS),dot(view,upWS))/max(dot(view,frontWS),.2);
                    uv=TraceGroove(uv,ray*_GrooveDepth*valid/sizeWS,dx,dy);
                }
                half4 carving=SAMPLE_TEXTURE2D_GRAD(_EngravingMask,sampler_EngravingMask,uv,dx,dy);
                half3 detail=UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_EngravingNormal,sampler_EngravingNormal,uv,dx,dy),_EngravingStrength);
                half cavityShadow=0;
                Light key=GetMainLight();
                if(valid>.001 && carving.r>.001 && _GrooveDepth>0 && dot(key.direction,frontWS)>.1)
                {
                    float2 lightRay=float2(dot(key.direction,rightWS),dot(key.direction,upWS))
                        /max(dot(key.direction,frontWS),.15)*_GrooveDepth/sizeWS;
                    [unroll] for(int j=1;j<=8;j++)
                    {
                        float rise=carving.r*j/8;
                        float surface=GrooveAt(uv+lightRay*rise,dx,dy);
                        cavityShadow=max(cavityShadow,smoothstep(.025,.12,carving.r-rise-surface));
                    }
                }
                // Project the carving gradient onto the receiving stone facet.
                half3 du=rightWS-normal*dot(normal,rightWS), dv=upWS-normal*dot(normal,upWS);
                normal=normalize(normal+valid*(du*detail.x+dv*detail.y)/max(detail.z,.2));
                SurfaceData s=(SurfaceData)0;
                s.albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;
                s.albedo*=1-valid*carving.r*_CavityTint*saturate(_EngravingStrength);
                s.albedo*=1-valid*cavityShadow*_GrooveShadow*saturate(_EngravingStrength);
                // A small screen-space expansion keeps subpixel strokes visible in the gameplay view.
                // Photo/album/other cameras keep this branch off, including during the camera fade.
                if (_GameplayReadability > 0 && valid > .001)
                {
                    float2 offsetX=dx*_DiscoveryWidth, offsetY=dy*_DiscoveryWidth;
                    half coverage=carving.a;
                    coverage=max(coverage,SAMPLE_TEXTURE2D_GRAD(_EngravingMask,sampler_EngravingMask,uv+offsetX,dx,dy).a);
                    coverage=max(coverage,SAMPLE_TEXTURE2D_GRAD(_EngravingMask,sampler_EngravingMask,uv-offsetX,dx,dy).a);
                    coverage=max(coverage,SAMPLE_TEXTURE2D_GRAD(_EngravingMask,sampler_EngravingMask,uv+offsetY,dx,dy).a);
                    coverage=max(coverage,SAMPLE_TEXTURE2D_GRAD(_EngravingMask,sampler_EngravingMask,uv-offsetY,dx,dy).a);
                    half readableGroove=pow(saturate(coverage),.6);
                    s.albedo*=1-valid*readableGroove*_DiscoveryContrast*saturate(_GameplayReadability)*saturate(_EngravingStrength);
                }
                // Preserve the source-art registration within the canonical glyph layout.
                float2 fillUV=uv*_FillUV.xy+_FillUV.zw;
                half4 fill=SAMPLE_TEXTURE2D(_PaintFill,sampler_PaintFill,fillUV);
                half fillValid=valid*step(0,fillUV.x)*step(fillUV.x,1)*step(0,fillUV.y)*step(fillUV.y,1);
                half grain=SAMPLE_TEXTURE2D(_PaintGrain,sampler_PaintGrain,uv*2).r;
                s.albedo=lerp(s.albedo,fill.rgb,fillValid*fill.a*_FillStrength*(1-carving.a));
                half paint=valid*carving.a*_PaintStrength*lerp(.9,1,grain);
                // Pigment sits inside the existing groove and still receives its wall shadow, normal and scene lighting.
                half3 pigment=_PaintColor.rgb*(1-carving.r*.12)*(1-cavityShadow*.24);
                s.albedo=lerp(s.albedo,pigment,paint);
                s.alpha=1; s.metallic=0; s.specular=half3(.2,.2,.2);
                s.smoothness=_Smoothness; s.normalTS=half3(0,0,1);
                s.occlusion=lerp(1,carving.g,valid*saturate(_EngravingStrength));
                InputData d=(InputData)0;
                d.positionWS=i.positionWS; d.normalWS=normal;
                d.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord=TransformWorldToShadowCoord(i.positionWS);
                d.bakedGI=SampleSH(normal); d.vertexLighting=VertexLighting(i.positionWS,normal);
                d.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS); d.shadowMask=half4(1,1,1,1);
                half4 color=UniversalFragmentPBR(d,s);
                color.rgb=MixFog(color.rgb,i.fog);
                return color;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
