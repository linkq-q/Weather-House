using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using WeatherHouse.Vegetation;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    /// <summary>A short real Play observation; no clip sampling or scene saving.</summary>
    [InitializeOnLoad]
    public static class WatersideFlightPlayReview
    {
        const string Key = "WeatherHouse.WatersideFlightReview.";
        const string Folder = "WatersideV3Review/flight-play";
        const string ReportPath = Folder + "/checks.json";
        static Review report;
        static Probe bird, butterfly;
        static Camera camera;
        static int lastFrame = -1;
        static double lastTrace;

        [Serializable] sealed class Review
        {
            public string status = "RUNNING", error = "", startedUtc, finishedUtc;
            public float playSeconds;
            public int frames;
            public bool alarmAccepted, sawTakeoff, sawFlee, returnedToEdit, sceneDirtyAfter;
            public bool originalRunInBackground, originalPause, restoredRunInBackground, restoredPause;
            public Subject bird = new Subject(), butterfly = new Subject();
        }
        [Serializable] sealed class Subject
        {
            public string name;
            public int spawnId;
            public float rawDelta, referenceSpan, ratio, requiredRatio = .05f;
            public Vector3 initialBakedBoundsSize, skinLossyScale;
            public List<string> states = new List<string>();
            public List<string> screenshots = new List<string>();
        }
        sealed class Probe
        {
            public WildlifeAgent agent;
            public WildlifePopulation population;
            public SkinnedMeshRenderer skin;
            public Mesh sample;
            public Vector3[] baseline;
            public Subject result;
            public string stem;
        }
        static bool Active => SessionState.GetBool(Key + "active", false);
        static bool Ending => SessionState.GetBool(Key + "ending", false);

        static WatersideFlightPlayReview()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += PlayChanged;
            Application.logMessageReceived += OnLog;
        }

        public static void Begin()
        {
            if (Active) throw new InvalidOperationException("The flight Play review is already active.");
            WatersideSceneBuilder.RequireSaved();
            if (SceneManager.GetActiveScene().path != WatersideSceneBuilder.ScenePath)
                throw new InvalidOperationException("Open the saved WatersideV2 scene first.");
            Directory.CreateDirectory(Folder);
            report = new Review { startedUtc = DateTime.UtcNow.ToString("O"),
                originalRunInBackground = Application.runInBackground, originalPause = EditorApplication.isPaused };
            SessionState.SetBool(Key + "runInBackground", Application.runInBackground);
            SessionState.SetBool(Key + "pause", EditorApplication.isPaused);
            SessionState.SetString(Key + "startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetString(Key + "beginTicks", DateTime.UtcNow.Ticks.ToString());
            SessionState.SetString(Key + "runtimeError", "");
            SessionState.SetBool(Key + "active", true);
            SessionState.SetBool(Key + "ending", false);
            SessionState.SetBool(Key + "success", false);
            SessionState.SetBool(Key + "entered", false);
            try
            {
                Save();
                File.WriteAllText(Folder + "/trace.csv", "play_seconds,frame,species,state,raw_delta,reference_span,ratio,required_ratio\n");
                Application.runInBackground = true;
                EditorApplication.isPaused = false;
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(WatersideSceneBuilder.ScenePath);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(error); }
        }

        static void PlayChanged(PlayModeStateChange change)
        {
            if (!Active) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                report = Load(); lastFrame = -1; lastTrace = -1;
                SessionState.SetBool(Key + "entered", true);
                SessionState.SetString(Key + "enteredTicks", DateTime.UtcNow.Ticks.ToString());
            }
            else if (change == PlayModeStateChange.ExitingPlayMode) Cleanup();
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                report = Load();
                if (!Ending) { report.error = "Play was stopped before this review completed."; SessionState.SetBool(Key + "success", false); }
                Restore();
            }
        }

        static void Update()
        {
            if (!Active) return;
            try
            {
                if (Ending)
                { if (!EditorApplication.isPlayingOrWillChangePlaymode) { report = Load(); Restore(); } return; }
                if (report == null) report = Load();
                if (Age("beginTicks") > 45) throw new TimeoutException("Flight Play review exceeded 45 seconds including entry.");
                string logged = SessionState.GetString(Key + "runtimeError", "");
                if (!string.IsNullOrEmpty(logged)) throw new InvalidOperationException("Unity runtime error: " + logged);
                if (!EditorApplication.isPlaying || !SessionState.GetBool(Key + "entered", false)) return;
                double elapsed = Age("enteredTicks"); report.playSeconds = (float)elapsed;
                if (elapsed > 16) throw new TimeoutException("Required live flight evidence was not observed in 16 seconds.");
                EditorApplication.QueuePlayerLoopUpdate();
                if (Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount; report.frames++;
                if (bird == null || butterfly == null)
                {
                    if (!TrySetup()) return;
                    Sample(bird); Sample(butterfly);
                    float now = butterfly.population.SimulationTime;
                    report.alarmAccepted = butterfly.agent.ReceiveStartle(new WildlifeAlarm { id = int.MaxValue - 11,
                        origin = butterfly.agent.transform.position, radius = 3, intensity = 1, expiresAt = now + 2 }, now);
                    if (!report.alarmAccepted) throw new InvalidOperationException("The selected resting butterfly rejected its review alarm.");
                    Save(); return;
                }
                Sample(bird); Sample(butterfly);
                // Observe the butterfly continuously: its imported Animator culls transforms off camera.
                // Keeping an actual review camera on it exercises normal visible animation without changing culling settings.
                FrameSubject(butterfly);
                report.sawTakeoff |= butterfly.agent.State == WildlifeState.Takeoff;
                report.sawFlee |= butterfly.agent.State == WildlifeState.Flee;
                if (elapsed - lastTrace >= .25)
                {
                    lastTrace = elapsed;
                    foreach (Probe probe in new[] { bird, butterfly })
                        File.AppendAllText(Folder + "/trace.csv", FormattableString.Invariant($"{elapsed:F3},{Time.frameCount},{probe.stem},{probe.agent.State},{probe.result.rawDelta:F6},{probe.result.referenceSpan:F6},{probe.result.ratio:F6},{probe.result.requiredRatio:F3}\n"));
                    Save();
                }
                if (elapsed >= 10 && report.frames > 5 && bird.result.screenshots.Count == 2 && butterfly.result.screenshots.Count == 2 &&
                    report.sawTakeoff && report.sawFlee) Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        static bool TrySetup()
        {
            WildlifeAgent[] agents = Object.FindObjectsByType<WildlifeAgent>(FindObjectsSortMode.None);
            WildlifeAgent b = agents.Where(a => a.speciesId == "robin" && a.visualRoot && a.visualRoot.name == "V3 Bird Visual" &&
                a.State == WildlifeState.Fly).OrderBy(a => a.spawnId).FirstOrDefault();
            WildlifeAgent f = agents.Where(a => a.speciesId == "butterfly" && a.AirHeight < .01f &&
                (a.State == WildlifeState.Idle || a.State == WildlifeState.Forage)).OrderBy(a => a.spawnId).FirstOrDefault();
            if (!b || !f) return false;
            WildlifePopulation bp = b.GetComponentInParent<WildlifePopulation>(), fp = f.GetComponentInParent<WildlifePopulation>();
            if (!bp || !fp || !bp.Initialized || !fp.Initialized) return false;
            var go = new GameObject("Flight review camera · Play only") { hideFlags = HideFlags.DontSave };
            camera = go.AddComponent<Camera>();
            Camera main = Camera.main;
            if (main) camera.CopyFrom(main);
            camera.enabled = true; camera.depth = main ? main.depth + 1 : 1;
            camera.targetTexture = null; camera.nearClipPlane = .02f; camera.farClipPlane = 90;
            camera.fieldOfView = 43; camera.orthographic = false;
            camera.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
            bird = NewProbe(b, bp, report.bird, "bird");
            butterfly = NewProbe(f, fp, report.butterfly, "butterfly");
            return true;
        }

        static Probe NewProbe(WildlifeAgent agent, WildlifePopulation population, Subject result, string stem)
        {
            if (!agent.animator || !agent.animator.isActiveAndEnabled)
                throw new InvalidOperationException(stem + " Animator is not active in Play.");
            var skin = agent.visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>().First(s => stem == "bird" || s.name.Contains("Wings"));
            result.name = agent.name; result.spawnId = agent.spawnId;
            return new Probe { agent = agent, population = population, skin = skin, result = result, stem = stem,
                sample = new Mesh { name = "Flight review sample", hideFlags = HideFlags.DontSave } };
        }

        static void Sample(Probe probe)
        {
            string state = probe.agent.State.ToString();
            if (!probe.result.states.Contains(state)) probe.result.states.Add(state);
            probe.skin.BakeMesh(probe.sample);
            Vector3[] current = probe.sample.vertices;
            if (probe.baseline == null)
            {
                probe.sample.RecalculateBounds();
                Vector3 size = probe.sample.bounds.size;
                float span = Mathf.Max(size.x, size.y, size.z);
                if (current.Length == 0 || float.IsNaN(span) || float.IsInfinity(span) || span <= .000001f)
                    throw new InvalidOperationException("The live wing mesh has no measurable initial span.");
                probe.result.referenceSpan = span;
                probe.result.initialBakedBoundsSize = size;
                probe.result.skinLossyScale = probe.skin.transform.lossyScale;
                probe.baseline = current; Capture(probe, 1); return;
            }
            if (current.Length != probe.baseline.Length) throw new InvalidOperationException("Wing topology changed during Play observation.");
            float delta = 0;
            for (int i = 0; i < current.Length; i++)
                delta = Mathf.Max(delta, (current[i] - probe.baseline[i]).magnitude);
            // Compare deformation and span in the same baked vertex space, independent of individual size.
            probe.result.rawDelta = Mathf.Max(probe.result.rawDelta, delta);
            probe.result.ratio = probe.result.rawDelta / probe.result.referenceSpan;
            if (probe.result.screenshots.Count == 1 && delta / probe.result.referenceSpan >= probe.result.requiredRatio) Capture(probe, 2);
        }

        static void FrameSubject(Probe probe)
        {
            Vector3 center = probe.skin.bounds.center;
            Vector3 offset = probe.agent.transform.TransformDirection(new Vector3(1, .85f, -1.2f)).normalized;
            camera.transform.SetPositionAndRotation(center + offset * (probe.stem == "bird" ? 1.8f : .65f),
                Quaternion.LookRotation(-offset, Vector3.up));
        }

        static void Capture(Probe probe, int frame)
        {
            FrameSubject(probe);
            var target = new RenderTexture(1200, 900, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1200, 900, TextureFormat.RGB24, false);
            RenderTexture oldActive = RenderTexture.active;
            string file = probe.stem + "_0" + frame + ".png";
            try
            {
                target.Create(); camera.targetTexture = target; camera.aspect = 1200f / 900;
                foreach (var meadow in Object.FindObjectsByType<HillsideGrassRenderer>(FindObjectsSortMode.None)) meadow.DrawForCamera(camera, true);
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0); pixels.Apply();
                File.WriteAllBytes(Folder + "/" + file, pixels.EncodeToPNG()); probe.result.screenshots.Add(file);
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = oldActive;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }

        static void OnLog(string message, string trace, LogType kind)
        {
            if (Active && !Ending && EditorApplication.isPlaying && (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert))
                SessionState.SetString(Key + "runtimeError", message + "\n" + trace);
        }
        static double Age(string key)
        { return long.TryParse(SessionState.GetString(Key + key, "0"), out long ticks) ? (DateTime.UtcNow.Ticks - ticks) / (double)TimeSpan.TicksPerSecond : double.MaxValue; }
        static Review Load() => File.Exists(ReportPath) ? JsonUtility.FromJson<Review>(File.ReadAllText(ReportPath)) : new Review();
        static void Save() => File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));

        static void Finish(Exception error)
        {
            if (!Active || Ending) return;
            if (report == null) report = Load();
            SessionState.SetBool(Key + "ending", true); SessionState.SetBool(Key + "success", error == null);
            report.error = error == null ? "" : error.ToString(); report.status = "RETURNING_TO_EDIT";
            try { Cleanup(); Save(); }
            finally
            {
                EditorApplication.isPaused = false;
                if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
                else Restore();
            }
        }

        static void Cleanup()
        {
            if (camera) Object.DestroyImmediate(camera.gameObject); camera = null;
            if (bird != null && bird.sample) Object.DestroyImmediate(bird.sample);
            if (butterfly != null && butterfly.sample) Object.DestroyImmediate(butterfly.sample);
            bird = butterfly = null;
        }

        static void Restore()
        {
            try
            {
                Application.runInBackground = SessionState.GetBool(Key + "runInBackground", false);
                EditorApplication.isPaused = SessionState.GetBool(Key + "pause", false);
                string original = SessionState.GetString(Key + "startScene", "");
                EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(original) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(original);
                Cleanup();
                report.returnedToEdit = !EditorApplication.isPlaying;
                report.sceneDirtyAfter = SceneManager.GetActiveScene().isDirty;
                report.restoredRunInBackground = Application.runInBackground;
                report.restoredPause = EditorApplication.isPaused;
                report.status = SessionState.GetBool(Key + "success", false) && report.returnedToEdit && !report.sceneDirtyAfter ? "PASS" : "FAIL";
                report.finishedUtc = DateTime.UtcNow.ToString("O");
                Save();
            }
            finally { SessionState.SetBool(Key + "active", false); SessionState.SetBool(Key + "ending", false); }
        }
    }
}
