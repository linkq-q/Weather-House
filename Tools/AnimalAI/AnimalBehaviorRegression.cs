// Stage into an Editor folder only for the batch run. Never saves the scene or real progress.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.Life;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class AnimalBehaviorRegression
{
    const string Key = "AnimalBehaviorRegression";
    const string ScenePath = "Assets/Scenes/WeatherHouse_WatersideV2.unity";
    static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly List<string> lines = new List<string>();
    static AnimalAutonomy director;
    static LifeSession life;
    static LifeLocalWeather weather;
    static Transform player;
    static object subject;
    static float started, stageAt, sampled, arrivedAt = -1;
    static int stage, failures;
    static Vector3 rainCenter, approachStart, playerGoal;
    static float approachDistance;
    static bool sawApproach;
    static string Folder => SessionState.GetString(Key + ".folder", "");
    static float Age => Time.time - stageAt;

    static AnimalBehaviorRegression()
    {
        EditorApplication.playModeStateChanged += change => {
            if (!SessionState.GetBool(Key, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            { started = Time.realtimeSinceStartup; stage = 0; Application.runInBackground = true; }
            if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".finishing", false))
                VerifyAndExit();
        };
        EditorApplication.update += Tick;
    }
    public static void StartBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Batch-only evidence run.");
        string path = Environment.GetEnvironmentVariable("WEATHERHOUSE_BEHAVIOR_REPORT");
        if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Set WEATHERHOUSE_BEHAVIOR_REPORT to a new output directory.");
        path = Path.GetFullPath(path); Directory.CreateDirectory(path);
        SessionState.SetString(Key + ".folder", path);
        SessionState.SetString(Key + ".sceneHash", Hash(ScenePath));
        var guards = new List<string>();
        foreach (string name in new[] { "language-notebook.json", "life-progress.json", "onboarding.json" })
        { string save = Path.Combine(Application.persistentDataPath, name); guards.Add(save + "|" + Hash(save)); }
        SessionState.SetString(Key + ".saveHashes", string.Join("\n", guards));
        Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE", Path.Combine(path, "isolated-save"));
        EditorSceneManager.OpenScene(ScenePath);
        File.WriteAllLines(Path.Combine(path, "edit-animal-scales.txt"), ScaleSnapshot());
        SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".finishing", false);
        EditorApplication.isPlaying = true;
    }
    static object Field(object value, string name) => value?.GetType().GetField(name, Flags)?.GetValue(value);
    static void Set(object value, string name, object data) => value.GetType().GetField(name, Flags)?.SetValue(value, data);
    static Transform Root(object value) => (Transform)Field(value, "root");
    static string Action => (string)Field(subject, "action");
    static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
    static string State(object a)
    {
        var agent = (WildlifeAgent)Field(a, "agent");
        object shelterQuery = Field(a, "shelterQuery");
        return "id=" + Field(a, "id") + "; action=" + Field(a, "action") + "; position=" + Root(a).position.ToString("F3") +
            "; goal=" + ((Vector3)Field(a, "goal")).ToString("F3") + "; available=" + a.GetType().GetProperty("Available").GetValue(a) +
            "; sleeping=" + a.GetType().GetProperty("Sleeping").GetValue(a) + "; nextRoute=" + Field(a, "nextRoute") +
            "; routeStep=" + Field(a, "routeStep") + "; route=" + ((IList)Field(a, "route"))?.Count +
            "; searchQueries=" + ((AnimalLocalRoute)Field(a, "search"))?.Queries + "; wildlife=" + (agent ? agent.State.ToString() : "herd") +
            "; lifeTarget=" + (agent && agent.HasLifeTarget) + "; autonomous=" + (agent && agent.AutonomousTarget) +
            "; wet=" + typeof(AnimalAutonomy).GetMethod("Wet", Flags).Invoke(director, new[] { a }) +
            "; rainRadius=" + Field(director, "rainRadius") + "; weatherRadius=" + Field(weather, "radius") +
            "; shelterProbe=" + Field(shelterQuery, "probe") + "; shelterCandidate=" + Field(shelterQuery, "candidate") +
            "; shelterCandidates=" + ((IList)Field(shelterQuery, "candidates"))?.Count +
            "; shelterRouteQueries=" + ((AnimalLocalRoute)Field(shelterQuery, "route"))?.Queries;
    }
    static object Choose(string suffix, bool outgoing)
    {
        var animals = (IList)Field(director, "animals"); object fallback = null;
        foreach (object a in animals)
        {
            var agent = (WildlifeAgent)Field(a, "agent");
            if (!agent || !agent.isActiveAndEnabled) continue;
            if (((string)Field(a, "id")).EndsWith(suffix, StringComparison.Ordinal)) return a;
            if (fallback == null && (bool)Field(a, "outgoing") == outgoing && (bool)a.GetType().GetProperty("Available").GetValue(a)) fallback = a;
        }
        return fallback;
    }
    static void Record(string text) { lines.Add("t=" + Time.time.ToString("F2") + " " + text); Flush(); }
    static void Flush() => File.WriteAllLines(Path.Combine(Folder, "observations.txt"), lines);
    static void Check(bool success, string text) { if (!success) failures++; Record((success ? "PASS " : "FAIL ") + text); }
    static void Next(int value) { stage = value; stageAt = Time.time; arrivedAt = -1; }
    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || SessionState.GetBool(Key + ".finishing", false) || !EditorApplication.isPlaying) return;
        try
        {
            if (Time.realtimeSinceStartup - started > 720) { Check(false, "Overall 720 second wall-clock deadline"); Finish(); return; }
            if (!director) director = Object.FindObjectOfType<AnimalAutonomy>();
            if (director)
            {
                // Local behavior regression is deterministic and must not depend on paid network replies.
                Set(director, "nextRequest", float.PositiveInfinity);
                var client = director.GetComponent<AnimalPlannerClient>();
                if (client) { Set(client, "config", null); Set(client, "nextConfigurationRead", float.PositiveInfinity); }
            }
            if (stage == 0)
            {
                if (Time.time < 5 || !director || director.AnimalCount == 0) return;
                life = Object.FindObjectOfType<LifeSession>(); player = life.Gameplay.PlayerTransform;
                weather = (LifeLocalWeather)Field(life, "weather");
                Set(weather, "radius", 80f);
                File.WriteAllLines(Path.Combine(Folder, "play-animal-scales.txt"), ScaleSnapshot());
                Record("REGISTER count=" + director.AnimalCount + " outgoing=" + director.OutgoingCount + "; saved formal scene; no animal teleport; network planning suppressed in memory");
                subject = Choose("/sheep/6101", false);
                if (subject == null) throw new InvalidOperationException("No ordinary land animal registered.");
                player.position = Root(subject).position + new Vector3(0, 0, -6);
                rainCenter = Root(subject).position;
                Capture("01_before_rain", 0);
                Record("ARRANGE player 6m away; test-only LifeLocalWeather.radius=80 so the regular LifeSession.Apply preserves sustained rain coverage every frame. Visual sky radius remains 8m; no gesture unlock test. " + State(subject));
                AnimalBehaviorDiagnostics.CaptureSelected(director, player, Path.Combine(Folder, "diagnostics-before-rain.txt"));
                weather.Cast("rain", out _); weather.Cast("strong", out _); Next(1);
            }
            if (subject != null && Time.time - sampled > 1)
            { sampled = Time.time; Record("SAMPLE stage=" + stage + " " + State(subject)); }
            if (stage == 1)
            {
                weather.Apply(life.World, Time.deltaTime);
                bool arrived = Action == "shelter" && Flat(Root(subject).position, (Vector3)Field(subject, "goal")) < .6f;
                if (arrived && arrivedAt < 0) arrivedAt = Time.time;
                if (!arrived) arrivedAt = -1;
                if (arrivedAt >= 0 && Time.time - arrivedAt > 1 || Age > 65)
                {
                    Check(arrived, "Shelter: physically reached selected shelter and held for one second; " + State(subject));
                    AnimalBehaviorDiagnostics.CaptureSelected(director, player, Path.Combine(Folder, "diagnostics-after-rain.txt"));
                    Capture("02_shelter", 0); weather.Cast("clear", out _); weather.Apply(life.World, Time.deltaTime); Next(2);
                }
            }
            else if (stage == 2 && Age > 3)
            {
                Check(Action != "shelter", "Rain stopped: released shelter action; " + State(subject)); Capture("03_rain_stopped", 0);
                player.position = Root(subject).position + Vector3.right * 14;
                Set(subject, "fatigue", .95f); Set(subject, "until", 0f); Record("ARRANGE fatigue .95 and player 14m away; no direct sleep action assignment"); Next(3);
            }
            else if (stage == 3)
            {
                bool sleeping = (bool)subject.GetType().GetProperty("Sleeping").GetValue(subject);
                if (sleeping && Age > 3 || Age > 25)
                {
                    Check(sleeping, "Sleep selected and executed; " + State(subject)); Capture("04_sleep_camera_a", 0); Capture("05_sleep_camera_b", 100);
                    subject = Choose("/rabbit/12", true);
                    if (subject == null) throw new InvalidOperationException("No outgoing animal registered.");
                    approachStart = Root(subject).position; playerGoal = approachStart + new Vector3(0, 0, -4); player.position = playerGoal;
                    approachDistance = Flat(approachStart, playerGoal); sawApproach = false;
                    AnimalBehaviorDiagnostics.CaptureSelected(director, player, Path.Combine(Folder, "diagnostics-before-approach.txt"));
                    Record("ARRANGE player 4m away from original outgoing rabbit; animal unmodified. " + State(subject)); Capture("06_approach_before", 0); Next(4);
                }
            }
            else if (stage == 4)
            {
                player.position = playerGoal;
                sawApproach |= Action == "approach" || Action == "follow";
                float moved = Flat(approachStart, Root(subject).position), now = Flat(Root(subject).position, playerGoal);
                if (Age > 20)
                {
                    Check(sawApproach && moved > .35f && now < approachDistance - .3f,
                        "Outgoing approach: selected=" + sawApproach + "; moved=" + moved.ToString("F3") + "; playerDistance=" + now.ToString("F3") + "; " + State(subject));
                    AnimalBehaviorDiagnostics.CaptureSelected(director, player, Path.Combine(Folder, "diagnostics-after-approach.txt"));
                    Capture("07_approach_after", 0); Finish();
                }
            }
        }
        catch (Exception error) { Check(false, error.ToString()); Finish(); }
    }
    static void Capture(string name, float turn)
    {
        Transform root = Root(subject); Camera main = Camera.main;
        if (!main) throw new InvalidOperationException("No main camera");
        var go = new GameObject("Temporary behavior evidence camera"); var camera = go.AddComponent<Camera>(); camera.CopyFrom(main); camera.enabled = false;
        camera.orthographic = false; camera.fieldOfView = 45; camera.nearClipPlane = .05f;
        Vector3 focus = root.position + Vector3.up * .9f;
        Vector3 offset = Quaternion.Euler(0, turn, 0) * new Vector3(2.5f, 2, -4.3f);
        for (int i = 0; i < 8; i++)
        {
            Vector3 candidate = Quaternion.Euler(0, i * 45, 0) * offset;
            bool blocked = false;
            foreach (RaycastHit hit in Physics.RaycastAll(focus, candidate.normalized, candidate.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(root)) { blocked = true; break; }
            if (!blocked) { offset = candidate; break; }
        }
        camera.transform.position = focus + offset; camera.transform.LookAt(focus);
        var target = new RenderTexture(1280, 720, 24); camera.targetTexture = target;
        RenderTexture previous = RenderTexture.active; var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.Render(); RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG());
            Record("CAPTURE " + name + "; separate real rendering camera=" + camera.transform.position.ToString("F3") + "; " + State(subject));
        }
        finally { RenderTexture.active = previous; Object.Destroy(image); camera.targetTexture = null; target.Release(); Object.Destroy(target); Object.Destroy(go); }
    }
    static IEnumerable<string> ScaleSnapshot()
    {
        foreach (Transform value in Object.FindObjectsOfType<Transform>(true))
        {
            string name = value.name.ToLowerInvariant();
            if (!(name.Contains("squirrel") || name.Contains("turtle") || name.Contains("松鼠") || name.Contains("乌龟"))) continue;
            string path = value.name; for (Transform parent = value.parent; parent; parent = parent.parent) path = parent.name + "/" + path;
            var renderers = value.GetComponentsInChildren<Renderer>(); Bounds bounds = new Bounds(value.position, Vector3.zero);
            if (renderers.Length > 0) { bounds = renderers[0].bounds; foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds); }
            yield return path + "; local=" + value.localScale.ToString("F5") + "; lossy=" + value.lossyScale.ToString("F5") + "; bounds=" + bounds.size.ToString("F5");
        }
    }
    static string Hash(string path)
    { if (!File.Exists(path)) return "MISSING"; using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    static void Finish()
    {
        Record("RESULT behavior failures=" + failures + "; waiting for edit-mode save protection check");
        SessionState.SetInt(Key + ".exitCode", failures > 0 ? 1 : 0); SessionState.SetBool(Key + ".finishing", true); EditorApplication.isPlaying = false;
    }
    static void VerifyAndExit()
    {
        int exit = SessionState.GetInt(Key + ".exitCode", 1); var checks = new List<string>();
        foreach (string line in SessionState.GetString(Key + ".saveHashes", "").Split('\n'))
        {
            int split = line.LastIndexOf('|'); if (split < 0) continue;
            bool same = Hash(line.Substring(0, split)) == line.Substring(split + 1);
            checks.Add((same ? "PASS " : "FAIL ") + "Real save unchanged: " + Path.GetFileName(line.Substring(0, split))); if (!same) exit = 1;
        }
        bool sceneSame = Hash(ScenePath) == SessionState.GetString(Key + ".sceneHash", "");
        checks.Add((sceneSame ? "PASS " : "FAIL ") + "Saved scene file unchanged; Play=False"); if (!sceneSame) exit = 1;
        File.WriteAllLines(Path.Combine(Folder, "save-protection.txt"), checks);
        SessionState.SetBool(Key, false); EditorApplication.Exit(exit);
    }
}
