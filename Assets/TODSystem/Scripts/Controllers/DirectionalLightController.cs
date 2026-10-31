using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.VisualScripting;
using Unity.VisualScripting.Dependencies.NCalc;
using UnityEngine;
using UnityEngine.Rendering;

[System.Serializable]
[ExecuteAlways]
public class DirectionalLightController : MonoBehaviour
{
    private static bool isDay;
    public static bool IsDay => isDay;
    private static int nowTime;
    private readonly static int dayBeginTime = 300, dayEndTime = 1140;
    private readonly static float exponentMin = 1, exponentMax = 10000;
    private readonly static float sunIntensityMin = 0, sunIntensityMax = 50;
    private readonly static float moonIntensityMin = 0, moonIntensityMax = 1;

    [Range(0, 90)]
    public float sunDeclination = 0, moonDeclination = 0;

    [Range(0, 1)]
    public double beta = 0.2, baseEnvS = 0.5;
    public double envAttenuation = 22;
    public double sunI0 = 2.5, sunEnvDir = 2.1, sunEnvIndir = 0.1;
    public double moonI0 = 0.1; //, moonEnvDir = 2.1, moonEnvIndir = 0.1;
    //public double 

    [SerializeField]
    private Light sun, moon;

    [SerializeField]
    private Material m_Skybox;
    public Material Skybox { set; get; }

    public Color SunColor
    {
        set { m_Skybox?.SetColor("_SunColor", value); }
    }

    public Color SunHaloColor
    {
        set { m_Skybox?.SetColor("_SunHaloColor", value); }
    }

    public Color SunScatteringColor
    {
        set { m_Skybox?.SetColor("_SunScatteringColor", value); }
    }

    public Color VenusColor
    {
        set { m_Skybox?.SetColor("_VenusColor", value); }
    }

    public float SunHaloExponent
    {
        set { m_Skybox?.SetFloat("_SunHaloExponent", Mathf.Clamp(value, exponentMin, exponentMax)); }
    }

    public float SunScatteringExponent
    {
        set { m_Skybox?.SetFloat("_SunScatteringExponent", Mathf.Clamp(value, exponentMin, exponentMax)); }
    }

    public float VenusExponent
    {
        set { m_Skybox?.SetFloat("_VenusExponent", Mathf.Clamp(value, exponentMin, exponentMax)); }
    }

    float SunlightIntensity
    {
        set
        {
            float intensity = Mathf.Clamp(value, sunIntensityMin, sunIntensityMax);
            m_Skybox?.SetFloat("_SunlightIntensity", intensity);
            sun.intensity = intensity;
        }
    }

    public float SunHaloIntensity
    {
        set
        {
            m_Skybox?.SetFloat("_SunHaloIntensity", Mathf.Clamp(value, sunIntensityMin, sunIntensityMax));
        }
    }

    public float SunScatteringIntensity
    {
        set
        {
            m_Skybox?.SetFloat("_SunScatteringIntensity", Mathf.Clamp(value, sunIntensityMin, sunIntensityMax));
        }
    }

    public float VenusIntensity
    {
        set
        {
            m_Skybox?.SetFloat("_VenusIntensity", Mathf.Clamp(value, sunIntensityMin, sunIntensityMax));
        }
    }

    float MoonlightIntensity
    {
        set
        {
            float intensity = Mathf.Clamp(value, moonIntensityMin, moonIntensityMax);
            m_Skybox?.SetFloat("_MoonlightIntensity", intensity);
            moon.intensity = intensity;
        }
    }

    float EnvIntensityMultiplier
    {
        set { RenderSettings.ambientIntensity = value; }
    }

    public (Color top, Color horizon, Color bottom) SkyColor
    {
        set
        {
            m_Skybox?.SetColor("_SkyColorTop", value.top);
            m_Skybox?.SetColor("_SkyColorHorizon", value.horizon);
            m_Skybox?.SetColor("_SkyColorBottom", value.bottom);
        }
    }

    public Color FogColor
    {
        set { RenderSettings.fogColor = value; }
    }

    public float FogRange
    {
        set
        {
            RenderSettings.fogStartDistance = value;
            RenderSettings.fogEndDistance = value * 52 / 19;
        }
    }

    void Awake()
    {
        // sun = transform.Find("Sun").GetComponent<Light>();
        // moon = transform.Find("Moon").GetComponent<Light>();
        RenderSettings.sun = sun;
    }

    // void OnValidate()
    // {
    //     SetIntensity();
    // }

    void Update()
    {
        //m_Skybox?.SetVector("_BaseRotation", transform.rotation.eulerAngles);
        m_Skybox?.SetVector("_SunRotation", sun.transform.rotation.eulerAngles);
        m_Skybox?.SetVector("_MoonRotation", moon.transform.rotation.eulerAngles);

        if (TODSystemManager.TODDataLoaded)
        {
            nowTime = TODSystemManager.CurrentFrame;

            bool isDayNew = nowTime >= dayBeginTime && nowTime <= dayEndTime;
            if (isDay != isDayNew)
            {
                isDay = isDayNew;
                SwitchDayNight();
            }
        }
    }

    void SwitchDayNight()
    {
        if (isDay)
        {
            print("Enter day.");
            RenderSettings.sun = sun;
            m_Skybox.SetFloat("_DayOrNight", 0);
        }
        else
        {
            print("Enter night.");
            RenderSettings.sun = moon;
            m_Skybox.SetFloat("_DayOrNight", 1);
        }
    }

    Quaternion CalculateCelestialRotation(double dec, float time)
    {
        double l = dec * Math.PI / 180, t = time * Math.PI / 12;
        double x = -Math.Asin(Math.Cos(l) * Math.Cos(t));
        double y = -Math.Atan2(Math.Sin(l) * Math.Cos(t), Math.Sin(t));
        double z = l == 0 ? y - Math.PI * 0.5 : (Math.Acos(Math.Clamp(Math.Sin(l) / Math.Sqrt(Math.Sin(l) * Math.Sin(l) * Math.Cos(t) * Math.Cos(t) + Math.Sin(t) * Math.Sin(t)), -1, 1)) * (t <= Math.PI ? -1 : 1));
        //}
        //Debug.Log($"hhh {Mathf.Abs(time - 12)}");

        // if (dec >= 80)
        // {
        //     Debug.Log($"!!! {180 / Mathf.PI * new Vector3((float)x, (float)y, (float)z)}");
        // }

        //Debug.Log(new Vector3((float)x, (float)y, (float)z));
        return Quaternion.Euler(180 / Mathf.PI * new Vector3((float)x, (float)y, (float)z));
    }

    float CalculateLightIntensity(double i0, double beta, double h)
    {
        double m = 1.0 / Math.Max(0.00001, Math.Sin(h * Math.PI / 180) + 0.50572 * Math.Pow(Math.Max(1, h + 6.07995), -1.6364));
        //Debug.Log($"{Math.Sin(h * Math.PI / 180) + 0.50572 * Math.Pow(Math.Max(1, h + 6.07995), -1.6364)} {Math.Sin(h * Math.PI / 180)} {0.50572 * Math.Pow(Math.Max(1, h + 6.07995), -1.6364)}");
        return (float)(i0 * Math.Exp(-beta * m));
    }

    float CalculateEnvMultiplier(double envCoeffDir, double envCoeffIndir, double h)
    {
        double sinh = Math.Max(0.00001, Math.Sin(h * Math.PI / 180));
        double directS = envCoeffDir * (1 + sinh * sinh) * sinh * (Math.Exp(-beta / sinh) - Math.Exp(-beta)) / (sinh - 1);
        double indirectS = envCoeffIndir * Math.Exp(envAttenuation * Math.Min(0, 1 - Math.Abs(90 - h) / 90));

        return (float)(directS + indirectS);
    }

    public void SetRotation(float time)
    {
        sun.transform.rotation = CalculateCelestialRotation(sunDeclination, time);
        moon.transform.rotation = CalculateCelestialRotation(moonDeclination, (time + 12) % 24);

        m_Skybox.SetVector("_SunRotation", sun.transform.rotation.eulerAngles);
        m_Skybox.SetVector("_MoonRotation", moon.transform.rotation.eulerAngles);
        //Debug.Log(time);
        m_Skybox.SetVector("_StarRotation", CalculateCelestialRotation(0, time).eulerAngles);

        // Debug.Log($"{Math.Cos(moonDeclination)}, {Math.Cos((time + 12) % 24)}, {-Math.Asin(Math.Cos(moonDeclination) * Math.Cos((time + 12) % 24))}, {moon.transform.rotation.eulerAngles}");
    }

    public void SetIntensity()
    {
        double sunAngle = sun.transform.rotation.eulerAngles.x, moonAngle = moon.transform.rotation.eulerAngles.x;
        sunAngle = sunAngle <= 180 ? sunAngle : sunAngle - 360;
        moonAngle = moonAngle <= 180 ? moonAngle : moonAngle - 360;

        float sunlightIntensity = sunAngle <= -1.75 ? 0 : CalculateLightIntensity(sunI0, beta, sunAngle);
        float moonlightIntensity = moonAngle <= -1.75 ? 0 : CalculateLightIntensity(moonI0, beta, moonAngle);
        SunlightIntensity = sunlightIntensity;
        MoonlightIntensity = moonlightIntensity;

        float sunEnvS = CalculateEnvMultiplier(sunEnvDir, sunEnvIndir, sunAngle);
        //float moonEnvS = CalculateEnvMultiplier(moonEnvDir, moonEnvIndir, moonAngle);

        EnvIntensityMultiplier = (float)baseEnvS + sunEnvS;// + moonEnvS;
        //Debug.Log($"{(float)sunAngle}: {sunlightIntensity}, {moonlightIntensity}, {RenderSettings.sun.intensity}, {RenderSettings.ambientIntensity}");
    }
}