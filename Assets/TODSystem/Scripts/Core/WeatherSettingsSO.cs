using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Timeline;

[CreateAssetMenu(menuName = "Scriptable Objects/Weather Settings", order = 1)]
public class WeatherSettingsSO : ScriptableObject
{
    [System.Serializable]
    public class WeatherData
    {
        public TimelineAsset timeline;
        public Material skybox;
    }

    [System.Serializable]
    public class Weather
    {
        public string name;
        public int weatherID;
        public WeatherData weatherData;
    }

    public List<Weather> weathers;

    public WeatherData FindWeatherDataByID(int weatherID)
    {
        foreach (Weather weather in weathers)
        {
            if (weather.weatherID == weatherID)
            {
                return weather.weatherData;
            }
        }

        Debug.Log($"Error: There is no weather whose ID is {weatherID}.");
        return weathers[0].weatherData;
    }
}
