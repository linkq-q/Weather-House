using System;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public class TODSystemManager : Singleton<TODSystemManager>
{
    private readonly static string weatherSettingsName = "Assets/TODSystem/Data/WeatherSettings.asset";
    private readonly static string timelineLabel = "TODTimelineAssets";
    private readonly static string skyboxLabel = "SkyboxMaterials";

    private static bool todDataLoaded = false;
    public static bool TODDataLoaded => todDataLoaded;

    private static WeatherSettingsSO weatherSettings;
    private static PlayableDirector playableDirector;

    [SerializeField]
    private DirectionalLightController directionalLightController;

    [HideInInspector]
    public static DirectionalLightTrackBehaviour directionalLightTB;
    private static TimelineAsset currentTODTimeline;
    private static int currentWeatherID;
    private static int currentFrame = 0;
    public static int CurrentFrame => currentFrame;

    [Tooltip("现实世界的一秒对应游戏世界的多少分钟")]
    public float timeElapseSpeed = 60;

    private readonly static double startTime = 12;

    [SerializeField]
    [Range(0, 24)]
    private double currentTime;
    // public double CurrentTime
    // {
    //     get { return currentTime; }
    //     set
    //     {
    //         currentTime = Math.Clamp(value, 0, 24);
    //         playableDirector.time = currentTime;
    //         currentFrame = (int)(currentTODTimeline.editorSettings.frameRate * currentTime);
    //     }
    // }

    [SerializeField]
    private bool timeFlow = false;
    public bool TimeFlow => timeFlow;
    //public bool enableTimeFlow = false;
    //private static bool onSceneLoading = false;

    public enum SceneLoadSettings
    {
        Reset,
        Continue
    };

    protected override void Awake()
    {
        base.Awake();
        DontDestroyOnLoad(this);

        Addressables.LoadAssetAsync<WeatherSettingsSO>(weatherSettingsName).Completed += (handle) =>
        {
            Debug.Log("WeatherSettings loaded.");
            weatherSettings = handle.Result;

            LoadTODTimelineAssets();
        };
    }

    void OnValidate()
    {
        if (playableDirector != null)
        {
            timeElapseSpeed = Mathf.Clamp(timeElapseSpeed, 1, 1440);

            playableDirector.time = currentTime;
            currentFrame = (int)(currentTODTimeline.editorSettings.frameRate * currentTime);

            if (timeFlow)
            {
                if (playableDirector.timeUpdateMode == DirectorUpdateMode.Manual)
                {
                    playableDirector.timeUpdateMode = DirectorUpdateMode.GameTime;
                    playableDirector.Play();
                }

                playableDirector.playableGraph.GetRootPlayable(0).SetSpeed(timeElapseSpeed / 60);
            }
            else
            {
                if (playableDirector.timeUpdateMode == DirectorUpdateMode.GameTime)
                {
                    playableDirector.timeUpdateMode = DirectorUpdateMode.Manual;
                }

                playableDirector.Evaluate();
            }
        }
    }

    void FixedUpdate()
    {
        if (playableDirector != null)
        {
            currentTime = playableDirector.time;
            currentFrame = (int)(currentTODTimeline.editorSettings.frameRate * currentTime);
        }
    }

    void LoadTODTimelineAssets()
    {
        Addressables.LoadAssetsAsync<TimelineAsset>(timelineLabel, (timelineAsset) =>
        {
            Debug.Log($"{timelineAsset.name} loaded.");
        }).Completed += (handle) =>
        {
            Debug.Log("All TOD timelines loaded.");
            LoadSkyboxMaterials();
        };
    }

    void LoadSkyboxMaterials()
    {
        Addressables.LoadAssetsAsync<Material>(skyboxLabel, (skyboxMaterial) =>
        {
            Debug.Log($"{skyboxMaterial.name} loaded.");
        }).Completed += (handle) =>
        {
            Debug.Log("All skybox materials loaded.");
            todDataLoaded = true;
            InitializeDirector();
            SetWeatherID(0);

            currentTime = playableDirector.time;
            currentFrame = (int)(currentTODTimeline.editorSettings.frameRate * currentTime);
            timeFlow = true;
        };
    }

    void InitializeDirector()
    {
        playableDirector = transform.AddComponent<PlayableDirector>();
        playableDirector.timeUpdateMode = DirectorUpdateMode.GameTime;
        playableDirector.playOnAwake = true;
        playableDirector.extrapolationMode = DirectorWrapMode.Loop;
        playableDirector.initialTime = startTime;
        Debug.Log("PlayableDirector Initialized.");
    }

    public void SetWeatherID(int weatherID)
    {
        if (todDataLoaded)
        {
            WeatherSettingsSO.WeatherData weatherData = weatherSettings.FindWeatherDataByID(weatherID);
            playableDirector.playableAsset = weatherData.timeline;
            currentTODTimeline = weatherData.timeline;
            currentWeatherID = weatherID;

            SetPlayableDirectorBindings();
            directionalLightController.Skybox = weatherData.skybox;

            playableDirector.Play();
            Debug.Log($"Set weatherID: {currentWeatherID}.");
        }
        else
        {
            Debug.Log("Error: Weather data has not been loaded completely.");
        }
    }

    void SetPlayableDirectorBindings()
    {
        foreach (var track in currentTODTimeline.GetOutputTracks())
        {
            Debug.Log($"Track Name: {track.name}.");
            if (track is MarkerTrack)
            {
                Debug.Log("Track is MarkerTrack. No bindings.");
            }
            else if (track is DirectionalLightTrack)
            {
                Debug.Log("Track is DirectionalLightTrack. Bound to DirectionalLightController.");
                // directionalLightController = GameObject.FindGameObjectWithTag("DirectionalLight").GetComponent<DirectionalLightController>();
                playableDirector.SetGenericBinding(track, directionalLightController);
            }
            else
            {
                Debug.Log("Error: Unknown TOD track type.");
            }
        }
    }

    public void OnSceneLoaded(SceneLoadSettings settings)
    {
        if (playableDirector != null)
        {
            SetPlayableDirectorBindings();
            //onSceneLoading = false;
            switch (settings)
            {
                case SceneLoadSettings.Reset:
                    currentTime = playableDirector.time = startTime;
                    currentFrame = (int)(currentTODTimeline.editorSettings.frameRate * currentTime);
                    //playableDirector.Stop();
                    //playableDirector.Evaluate();
                    break;

                case SceneLoadSettings.Continue:

                    break;
            }

            if (timeFlow)
            {
                playableDirector.Play();
            }
            else
            {
                playableDirector.Evaluate();
            }
            // playableDirector.Evaluate();
            // playableDirector.Resume();
        }
    }

    public void OnSceneUnloaded()
    {
        //onSceneLoading = true;
        if (playableDirector != null && playableDirector.timeUpdateMode == DirectorUpdateMode.GameTime)
        {
            playableDirector.Pause();
        }
    }
}
