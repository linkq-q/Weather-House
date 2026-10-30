using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using WeatherHouse.Life;
using WeatherHousePrototype;
using ForestSpriteCharacter;

[InitializeOnLoad]
public static class MagicAudioChecks
{
    public const string Output = "../../../InteractionReview/20261030/MagicAudio/checks.txt";
    static MagicAudioChecks() { EditorApplication.playModeStateChanged += state => {
        if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("MagicAudioChecks", false)) {
            SessionState.SetBool("MagicAudioChecks", false); new GameObject("Magic audio checks").AddComponent<MagicAudioProbe>();
        }
    }; }
    public static void Run()
    {
        var script = AssetDatabase.LoadAllAssetsAtPath("Assets/WeatherHousePhotoCandidate.dll").OfType<MonoScript>()
            .Single(s => s.GetClass() == typeof(LifeAudioCatalog));
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out string guid, out long id);
        const string asset = "Assets/Game/Life/Audio/Resources/LifeAudio/Catalog.asset";
        File.WriteAllText(asset, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(asset), @"m_Script: \{[^}]+\}",
            "m_Script: {fileID: " + id + ", guid: " + guid + ", type: 3}"));
        AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport);
        var catalog = Resources.Load<LifeAudioCatalog>("LifeAudio/Catalog");
        var lines = new List<string>();
        void Check(bool ok, string label) { lines.Add((ok ? "PASS " : "FAIL ") + label); }
        Check(catalog.Find(LifeAudioCue.Cast).clips.Single().length > 3.8f, "07 is the success clip");
        bool drawing = Enum.TryParse("Casting", out LifeAudioCue casting);
        bool failure = Enum.TryParse("CastFailed", out LifeAudioCue failed);
        Check(drawing && catalog.Find(casting)?.clips.Length == 2, "03 and 04 are two casting variants");
        Check(failure && catalog.Find(failed)?.clips.Length == 1, "06 is a separate failure cue");
        File.WriteAllLines(Output, lines);
        if (lines.Any(l => l.StartsWith("FAIL"))) { EditorApplication.Exit(1); return; }
        WeatherHouse.Life.Editor.LifeAudioChecks.Run();
        File.Copy("LifeGameplayReview/20261026/audio-checks.txt", "../../../InteractionReview/20261030/MagicAudio/unity-import-checks.txt", true);
        foreach (var clip in catalog.sounds.SelectMany(s => s.clips).Concat(catalog.voices.Select(v => v.clip)).Distinct())
            if (!clip.LoadAudioData() || clip.loadState != AudioDataLoadState.Loaded || clip.samples <= 0)
                throw new Exception("Audio failed to decode: " + clip.name);
        Add("PASS All 26 catalog clips decode in Unity");
        Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE", Path.GetFullPath("../../../InteractionReview/20261030/MagicAudio/Save"));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool("MagicAudioChecks", true); EditorApplication.isPlaying = true;
    }
    public static void Add(string line) => File.AppendAllText(Output, line + "\n");
}
public class MagicAudioProbe : MonoBehaviour
{
    readonly List<string> errors = new List<string>();
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Hidden).SetValue(obj, value);
    static T Get<T>(object obj, string field) => (T)obj.GetType().GetField(field, Hidden).GetValue(obj);
    static void Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Hidden).Invoke(obj, args);
    static void Property(object obj, string name, object value) => obj.GetType().GetProperty(name).SetValue(obj, value);
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); MagicAudioChecks.Add("PASS " + label); }
    IEnumerator Start()
    {
        Application.logMessageReceived += Log;
        var steps = Steps();
        while (true) {
            bool more; object next = null;
            try { more = steps.MoveNext(); if (more) next = steps.Current; }
            catch (Exception e) { MagicAudioChecks.Add("FAIL " + e); EditorApplication.Exit(1); yield break; }
            if (!more) break; yield return next;
        }
        foreach (var error in errors) MagicAudioChecks.Add("FAIL runtime log: " + error);
        if (errors.Count == 0) MagicAudioChecks.Add("PASS No runtime errors or exceptions");
        Application.logMessageReceived -= Log;
        EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
    }
    void Log(string condition, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(condition); }
    IEnumerator Steps()
    {
        var root = new GameObject("Disabled game fixture"); root.SetActive(false);
        var game = root.AddComponent<GameplaySession>(); var life = root.AddComponent<LifeSession>();
        foreach (var component in root.GetComponents<MonoBehaviour>()) component.enabled = false;
        // A stationary player transform suffices for audio; keep the unrigged controller inactive.
        var playerObject = new GameObject("Stationary player fixture"); playerObject.SetActive(false);
        var player = playerObject.AddComponent<ForestSpriteDemoController>(); player.enabled = false;
        player.demoCamera = new GameObject("Listener", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        game.forestSprite = player; Set(life, "saveBlocked", true);
        var world = root.AddComponent<LifeWorld>(); world.enabled = false; Property(life, "World", world);
        var weather = new GameObject("Local weather").AddComponent<LifeLocalWeather>(); weather.Initialize(player.transform, 8); Set(life, "weather", weather);
        var input = root.AddComponent<LifeGestureInput>(); input.enabled = false; Set(input, "life", life);
        var ink = new GameObject("Ink", typeof(RectTransform)).AddComponent<LifeStrokeGraphic>(); Set(input, "stroke", ink);
        Set(input, "drawHint", new GameObject("Hint", typeof(RectTransform)).AddComponent<Text>());
        var audio = root.AddComponent<LifeAudio>(); root.SetActive(true); yield return null; yield return null;
        Call(audio, "OnApplicationFocus", true);
        var sources = Get<AudioSource[]>(audio, "sources");
        var casting = (LifeAudioCue)Enum.Parse(typeof(LifeAudioCue), "Casting");
        var failed = (LifeAudioCue)Enum.Parse(typeof(LifeAudioCue), "CastFailed");
        var variants = audio.Catalog.Find(casting).clips;
        Check(sources.Length == 12, "12 existing sources retained");
        var points = Get<List<Vector2>>(input, "points");
        void Move() { points.Clear(); points.Add(new Vector2(-10000, -10000)); Call(input, "AddPoint"); }
        Set(input, "worldMode", true); life.SetGestureInput(true); yield return null;
        Check(!sources[2].isPlaying, "Armed casting is silent until drawing moves");
        Move(); yield return null; yield return null;
        Check(sources[2].isPlaying && sources[2].clip == variants[0], "First world drawing plays 03");
        yield return new WaitForSecondsRealtime(.65f);
        Check(!sources[2].isPlaying, "Stationary stroke fades to silence");
        Move(); yield return null; yield return null;
        Check(sources[2].isPlaying && sources[2].clip == variants[0], "Resuming same stroke keeps 03");
        int failures = audio.Count(failed); input.Cancel();
        Check(!sources[2].isPlaying && audio.Count(failed) == failures, "Cancel stops immediately without failure sound");
        Set(input, "worldMode", true); life.SetGestureInput(true); Move(); yield return null; yield return null;
        Check(sources[2].isPlaying && sources[2].clip == variants[1], "Next stroke plays 04 without layering");
        points.Clear(); Call(input, "SubmitStroke");
        Check(!sources[2].isPlaying && audio.Count(failed) == failures + 1, "Invalid submitted stroke stops drawing and plays 06 once");
        Check(sources.Any(s => s.isPlaying && s.clip == audio.Catalog.Find(failed).clips[0]), "Failure output uses selected 06 asset");
        yield return new WaitForSecondsRealtime(.35f);
        Check(audio.Count(failed) == failures + 1 && !sources[2].isPlaying, "Result does not repeat on later frames");
        int successes = audio.Count(LifeAudioCue.Cast); points.Clear();
        Vector2[] path = GestureCatalog.Find("rain").points;
        for (int i = 1; i < path.Length; i++) for (int j = 0; j < 12; j++) points.Add(Vector2.Lerp(path[i-1], path[i], j/12f)*150);
        points.Add(path.Last()*150); Call(input, "SubmitStroke");
        Check(life.LocalRainActive && audio.Count(LifeAudioCue.Cast) == successes + 1 && audio.Count(failed) == failures + 1,
            "Actual recognition and weather cast play 07 exactly once, without 06");
        Check(sources.Any(s => s.isPlaying && s.clip == audio.Catalog.Find(LifeAudioCue.Cast).clips[0]), "Successful output uses selected 07 asset");
        input.Cancel(); input.SetPracticeDrawing(true); LifeAudio.StrokeMoved(); yield return null; yield return null;
        Check(sources[2].isPlaying && sources[2].clip == audio.Catalog.Find(LifeAudioCue.Stroke).clips[0], "Book practice retains its paper stroke sound");
        Call(audio, "OnApplicationFocus", false); int before = audio.PlayedCount;
        LifeAudio.StrokeMoved(); LifeAudio.Play(failed); weather.Cast("rain", out _); yield return null;
        Check(sources.All(s => !s.isPlaying) && audio.PlayedCount == before, "Focus loss stops sounds and rejects background casting");
        input.Cancel(); Call(audio, "OnApplicationFocus", true); yield return null; yield return null;
        Check(!sources[2].isPlaying && audio.PlayedCount <= before + 2, "Focus return does not replay stale drawing or results");
        audio.enabled = false; Check(sources.All(s => !s.isPlaying), "Disable stops all audio");
    }
}
