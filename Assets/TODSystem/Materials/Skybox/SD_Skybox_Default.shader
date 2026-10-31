Shader "TODSystem/Skybox_Default"
{
    Properties
    {
        //_MainTex ("Texture", 2D) = "white" {}
        //_BaseRotation ("Base Rotation (Euler)", Vector) = (0,0,0,0)
        //_SunMaskParam0 ("Sun Mask Param 0", Range(0,1)) = 1

        [Header(Sun)] [Space]
        _SunColor ("Sun Color", Color) = (1,1,1,1)
        _SunlightIntensity ("Sunlight Intensity", Range(0,50)) = 2
        _SunRadius ("Sun Radius", Range(1,100)) = 1.0
        _SunMaskParam1 ("Sun Mask Param 1", Range(0,1)) = 1
        //_SunOuterRadius ("Sun Outer Radius", Range(1,100)) = 1.0
        [Space]
        _SunHaloColor ("Sun Halo Color", Color) = (1,1,1,1)
        _SunScatteringColor ("Sun Scattering Color", Color) = (1,1,1,1)
        _VenusColor ("Venus Color", Color) = (1,1,1,1)
        _SunHaloExponent ("Sun Halo Exponent", Range(1,10000)) = 1
        _SunScatteringExponent ("Sun Scattering Exponent", Range(1,10000)) = 1
        _VenusExponent ("Venus Exponent", Range(1,10000)) = 1
        _SunHaloIntensity ("Sun Halo Intensity", Range(0,50)) = 1
        _SunScatteringIntensity ("Sun Scattering Intensity", Range(0,50)) = 1
        _VenusIntensity ("Venus Intensity", Range(0,50)) = 1
        _VenusExponentHorizontal ("Venus Horizontal Exponent", Range(0,1)) = 1
        _HorizonSoftenExponent ("Horizon Soften Exponent", Range(1,10000)) = 50

        [Header(Moon)] [Space]
        _MoonCubeMap ("Moon Cube Map", Cube) = "white" {}
        _MoonlightIntensity ("Moonlight Intensity", Range(0,1)) = 0.06
        _MoonRadius ("Moon Radius", Range(1,50)) = 1.0
        _MoonBaseRotation ("Moon Base Rotation (Euler)", Vector) = (0,0,0,0)
        _MoonMarginFade ("Moon Margin Fade", Range(0,2)) = 1.0
        [Space]
        _MoonBloomColor ("Moon Bloom Color", Color) = (1,1,1,1)
        _MoonBloomExponent ("Moon Bloom Exponent", Range(0,1000)) = 100

        [Header(Star)] [Space]
        //_StarRotation

        [Header(Sky)] [Space]
        _HorizonYBoundary ("Horizon Y Boundary", Range(0.01, 1)) = 0.3
        _GradientExponent ("Gradient Exponent", Range(0.01,2)) = 1
        _StarCubeMap ("Star Texture", Cube) = "white" {}
        _StarIntensity ("Star Intensity", Range(0,10)) = 1


        //_SunRotation ("Sun Rotation", Vector) = (0,0,0,0)
        //_MoonRotation ("Moon Rotation", Vector) = (0,0,0,0)
        _SunRotation ("Sun Rotation", Vector) = (0,0,0,0)
        _MoonRotation ("Moon Rotation", Vector) = (0,0,0,0)
        [HideInInspector] _StarRotation ("Star Rotation", Vector) = (0,0,0,0)
        [HideInInspector] _SkyColorTop ("SkyColor Day Top", Color) = (1, 1, 1, 1)
        [HideInInspector] _SkyColorHorizon ("SkyColor Day Horizon", Color) = (1, 1, 1, 1)
        [HideInInspector] _SkyColorBottom ("SkyColor Day Bottom", Color) = (1, 1, 1, 1)
        //[HideInInspector] _DayOrNight ("Day Or Night", float) = 0
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
            "Queue" = "Background"
        }
        LOD 100
        ZWrite Off

        Pass
        {
            Name "SkyboxPass"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/TODSystem/Scripts/HLSL/Utilities.hlsl"

            #pragma vertex vert
            #pragma fragment frag

            struct a2v
            {
                float4 vertex : POSITION;
                float3 uv : TEXCOORD0;
                float3 normal: NORMAL;
                float4 tangent: TANGENT;
            };

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float3 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 positionVS : TEXCOORD2;
            };

            //TEXTURE2D(_MainTex);
            //SAMPLER(sampler_MainTex);
            //float4 _MainTex_ST;
            TEXTURECUBE(_MoonCubeMap);
            SAMPLER(sampler_MoonCubeMap);

            CBUFFER_START(UnityPerMaterial)

            //float3 _BaseRotation;
            //float _SunMaskParam0;

            //float _Red;
            float3 _SunColor;
            float _SunlightIntensity;
            float _SunRadius;
            float _SunMaskParam1;
            //float _SunOuterRadius;

            float3 _SunHaloColor;
            float3 _SunScatteringColor;
            float3 _VenusColor;
            float _SunHaloExponent;
            float _SunScatteringExponent;
            float _VenusExponent;
            float _SunHaloIntensity;
            float _SunScatteringIntensity;
            float _VenusIntensity;
            float _VenusExponentHorizontal;
            float _HorizonSoftenExponent;

            float _MoonlightIntensity;
            float _MoonRadius;
            float3 _MoonBaseRotation;
            float _MoonMarginFade;

            float3 _MoonBloomColor;
            float _MoonBloomExponent;

            float3 _SkyColorTop;
            float3 _SkyColorHorizon;
            float3 _SkyColorBottom;
            // float3 _SkyColorNightTop;
            // float3 _SkyColorNightHorizon;
            // float3 _SkyColorNightBottom;
            float _HorizonYBoundary;
            float _GradientExponent;

            //float _DayOrNight;
            float3 _SunRotation;
            float3 _MoonRotation;

            CBUFFER_END

            v2f vert(a2v v)
            {
                v2f o;
                VertexPositionInputs vertexInput=GetVertexPositionInputs(v.vertex.xyz);
                o.uv=v.uv;
                o.positionWS=vertexInput.positionWS;
                o.positionVS=vertexInput.positionVS;
                o.positionCS=vertexInput.positionCS;

                return o;
            }

            // 返回值的x代表是否有交点，0=无交点，1=有交点
            // 返回值的yzw代表最近交点的月面cubemap坐标
            // rayDir与moonPos需要归一化
            float4 rayMoonIntersection(float3 rayDir, float3 moonPos, float moonRadius)
            {
                rayDir=normalize(rayDir);
                moonPos=normalize(moonPos);

                float b=dot(rayDir,moonPos);
                float delta=b*b-1.0+moonRadius*moonRadius*0.0001;
                float modSqDelta=sqrt(max(delta,0));
                float t=b-modSqDelta;
                float hasIntersection=step(0,delta)*step(0,t);

                float3 texCoord=normalize(t*rayDir-moonPos);
                float cosTheta=-dot(texCoord,moonPos);
                float intersectionAngle=hasIntersection*cosTheta; // 1为相交于球心，0为无交点

                texCoord=rotateByEulerAngles(texCoord,_MoonRotation,1);
                texCoord=rotateByEulerAngles(texCoord,_MoonBaseRotation,0);

                return float4(intersectionAngle,texCoord);
            }

            float4 frag(v2f i) : SV_Target
            {
                // Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                //return float4(light.direction,1);
                float3 sunDir=calculateCelestialUV(_SunRotation);
                float3 moonDir=calculateCelestialUV(_MoonRotation);

                float sunDist=distance(sunDir,normalize(i.uv.xyz));
                float sunMask=step(100*sunDist,_SunRadius);
                float3 sunColor=sunMask*max(1.5*_SunlightIntensity,2)*_SunColor;

                float4 moonIntersection=rayMoonIntersection(i.uv.xyz,moonDir,_MoonRadius);
                float moonMask=moonIntersection.x;

                float3 moonColor=lerp(float3(0,0,0),SAMPLE_TEXTURECUBE(_MoonCubeMap,sampler_MoonCubeMap,moonIntersection.yzw).xyz,moonMask);
                moonColor*=max(22*_MoonlightIntensity,1)*pow(max(0.01,moonIntersection.x),_MoonMarginFade);

                float sunHeightMask=smoothstep(0,_HorizonYBoundary,abs(i.uv.y));

                float sunHeightMaskMod=pow(sunHeightMask,_GradientExponent);
                float3 skyColor=lerp(lerp(_SkyColorHorizon,_SkyColorBottom,sunHeightMaskMod),
                lerp(_SkyColorHorizon,_SkyColorTop,sunHeightMaskMod),step(0,i.uv.y));

                // Sun halo (bloom) and scattering
                float sdoti=dot(sunDir,normalize(i.uv.xyz));

                float sunHaloMask=pow(saturate(1+_SunRadius*0.01-sunDist),_SunHaloExponent);
                float sunMask1=max(saturate(0.5*sdoti+0.5),0.001);
                float sunMask2=max(saturate(_SunMaskParam1*sdoti+1-_SunMaskParam1),0.001);

                float bellCurve1=pow(sunMask1, _SunScatteringExponent*abs(i.uv.y)+1);
                float bellCurve2=pow(sunMask2, _VenusExponent*abs(i.uv.y));
                float horizonSoften=1-pow(1-saturate(i.uv.y),_HorizonSoftenExponent);

                float sunScatteringMask=saturate(bellCurve1*horizonSoften);
                float venusMask=saturate(bellCurve2*horizonSoften);
                float venusMaskHorizontal=pow(saturate(0.5*dot(normalizeSafe2(sunDir.xz),normalizeSafe2(i.uv.xz))+0.53),_VenusExponentHorizontal);
                venusMask*=venusMaskHorizontal;

                float3 sunBloom=sunHaloMask*_SunHaloColor*_SunHaloIntensity+sunScatteringMask*_SunScatteringColor*_SunScatteringIntensity+venusMask*_VenusColor*_VenusIntensity;

                float3 bloom=sunBloom;
                skyColor+=bloom;

                float3 finalColor=sunColor+moonColor+lerp(skyColor,float3(0,0,0),sunMask+0.8*moonMask);
                return float4(finalColor,1);
            }
            ENDHLSL
        }
    }
}
