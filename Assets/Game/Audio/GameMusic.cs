using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouse
{
    // One continuous music source for the drawer opening and the playable world.
    public sealed class GameMusic : MonoBehaviour
    {
        static GameMusic instance;
        AudioSource source;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneLoaded += SceneLoaded;
            SceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }
        static void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "DrawerPrototype" && scene.name != "WeatherHouse_WatersideV2") return;
            if (!instance) new GameObject("Weather House BGM").AddComponent<GameMusic>();
        }
        void Awake()
        {
            if (instance && instance != this) { Destroy(gameObject); return; }
            instance = this; DontDestroyOnLoad(gameObject);
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = true; source.spatialBlend = 0;
            source.priority = 220; source.volume = 0;
            source.clip = Resources.Load<AudioClip>("Music/Afternoon Shade");
            if (source.clip) source.Play(); else Debug.LogError("Afternoon Shade BGM is missing.", this);
        }
        void Update() { if (source) source.volume = Mathf.MoveTowards(source.volume, .12f, Time.unscaledDeltaTime * .1f); }
        void OnDestroy() { if (instance == this) instance = null; }
    }
}
