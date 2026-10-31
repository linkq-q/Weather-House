using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Scriptable Objects/Scene Weather Config", order = 2)]
public class SceneWeatherConfigSO : ScriptableObject
{
    [System.Serializable]
    public class SceneWeatherConfig
    {
        public string name;
        public List<int> availableWeatherID;

    }

    public List<SceneWeatherConfig> scenes;
}
