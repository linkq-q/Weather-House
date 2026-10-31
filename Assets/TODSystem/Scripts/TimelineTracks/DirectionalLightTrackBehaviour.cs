using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[System.Serializable]
public class DirectionalLightTrackBehaviour : PlayableBehaviour
{
    //public Vector3 directionalLightRotation;
    public Color skyColorTop, skyColorHorizon, skyColorBottom;
    public Color sunColor;
    public Color sunHaloColor, sunScatteringColor, venusColor;
    public float sunHaloExponent, sunScatteringExponent, venusExponent;
    public float sunHaloIntensity, sunScatteringIntensity, venusIntensity;

    public Color fogColor;
    public float fogRange;
    //public float sunlightIntensity;
    //public float moonlightIntensity;
    //public float envIntensityMultiplier;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData) // 此处的playable是轨道资产在运行时被实例化为的track mixer playable（由track生成）。playerData就是轨道绑定的对象
    {
        DirectionalLightController directionalLight = playerData as DirectionalLightController;

        directionalLight.SetRotation((float)playable.GetTime());
        directionalLight.SetIntensity();
        directionalLight.SkyColor = (skyColorTop, skyColorHorizon, skyColorBottom);
        //Debug.Log(skyColorTop);
        directionalLight.SunColor = sunColor;
        directionalLight.SunHaloColor = sunHaloColor;
        directionalLight.SunScatteringColor = sunScatteringColor;
        directionalLight.VenusColor = venusColor;
        directionalLight.SunHaloExponent = sunHaloExponent;
        directionalLight.SunScatteringExponent = sunScatteringExponent;
        directionalLight.VenusExponent = venusExponent;
        //directionalLight.SunlightIntensity = sunlightIntensity;
        directionalLight.SunHaloIntensity = sunHaloIntensity;
        directionalLight.SunScatteringIntensity = sunScatteringIntensity;
        directionalLight.VenusIntensity = venusIntensity;
        //directionalLight.MoonlightIntensity = moonlightIntensity;
        //directionalLight.EnvIntensityMultiplier = envIntensityMultiplier;
        directionalLight.FogColor = fogColor;
        directionalLight.FogRange = fogRange;

        if (Math.Abs(playable.GetTime() - 12) <= 0.001)
        {
            Debug.Log("DynamicGI.UpdateEnvironment()");
            DynamicGI.UpdateEnvironment();
        }

        // if (Math.Abs(playable.GetTime() - 18) <= 0.01)
        // {
        //     Debug.Log("日落");
        // }
        // else if (Math.Abs(playable.GetTime() - 18.6) <= 0.01)
        // {
        //     Debug.Log("民用黄昏");
        // }
        // else if (Math.Abs(playable.GetTime() - 19.05) <= 0.01)
        // {
        //     Debug.Log("航海黄昏");
        // }
        // else if (Math.Abs(playable.GetTime() - 19.6) <= 0.01)
        // {
        //     Debug.Log("天文黄昏");
        // }
    }
}