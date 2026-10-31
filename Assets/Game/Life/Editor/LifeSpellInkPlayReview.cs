using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WeatherHousePrototype;
using WeatherHouse.WatersideV2;

namespace WeatherHouse.Life.Editor
{
    [InitializeOnLoad]
    public static class LifeSpellInkPlayReview
    {
        const string Review = "SpellInkReview/20261029-firefly";
        const string Key = "WeatherHouse.SpellInk.PlayReview";
        const string ScenePath = "Assets/Scenes/WeatherHouse_WatersideV2.unity";
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> checks = new List<string>();
        static double nextPoll;
        static bool started;
        static bool Owned => SessionState.GetBool(Key, false);
        static string Folder => SessionState.GetString(Key + ".folder", Review);

        static LifeSpellInkPlayReview()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Changed;
        }
        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + .5;
            string commandPath = Review + "/command.txt";
            if (File.Exists(commandPath))
            {
                string command = File.ReadAllText(commandPath).Trim(); File.Delete(commandPath);
                try
                {
                    if (command == "play") Begin();
                    else if (command == "refresh")
                    {
                        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Refresh only in Edit mode.");
                        foreach (string name in new[] { "LifeGestureInput", "LifeGesturePad", "LifeSpellInkGraphic", "LifeStrokeGraphic", "LifeGestureBook" })
                            AssetDatabase.ImportAsset("Assets/Game/Life/UI/" + name + ".cs", ImportAssetOptions.ForceUpdate);
                        UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
                    }
                    else if (command == "checks") LifeSpellInkChecks.Run();
                    else if (command != "status") throw new InvalidOperationException("Unknown spell review command");
                    Status("OK " + command);
                }
                catch (Exception e) { Status("REFUSED " + e); }
            }
            if (!Owned || !EditorApplication.isPlaying) return;
            if (!started)
            {
                var life = UnityEngine.Object.FindObjectOfType<LifeSession>();
                if (life && life.Gameplay && life.World && life.GetComponent<LifeGestureInput>())
                {
                    started = true; checks.Clear();
                    life.StartCoroutine(Guarded(Run(life)));
                }
            }
            if (DateTime.TryParse(SessionState.GetString(Key + ".began", ""), out DateTime began) &&
                DateTime.UtcNow - began.ToUniversalTime() > TimeSpan.FromMinutes(4)) Finish("FAIL timed out");
        }

        [MenuItem("Weather House/试玩/施法流萤检查（独立存档）")]
        public static void Begin()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling, "Editor is busy; leave the existing Play untouched.");
            Require(!SessionState.GetBool("WeatherHouse.DrawerPrototype.owned", false) && !File.Exists("DrawerPrototypeReview/command.txt"), "Drawer review still owns the editor.");
            Require(!SessionState.GetBool("WeatherHouse.Life.Review.Running", false) && !SessionState.GetBool("WeatherHouse.Onboarding.IsolatedReview", false), "Another life review owns the editor.");
            var scene = SceneManager.GetActiveScene(); Require(scene.path == ScenePath, "Keep WatersideV2 open; this review never switches scenes.");
            string folder = Review + "/play-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"); Directory.CreateDirectory(folder);
            if (scene.isDirty)
                Require(EditorSceneManager.SaveScene(scene, folder + "/unsaved-scene-copy.unity", true) && scene.isDirty, "Could not preserve the dirty scene as a copy.");
            SessionState.SetString(Key + ".folder", folder);
            SessionState.SetString(Key + ".save", Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE") ?? "");
            SessionState.SetString(Key + ".opt", Environment.GetEnvironmentVariable(LifeOnboarding.ReviewOptIn) ?? "");
            SessionState.SetString(Key + ".startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Key + ".dirty", scene.isDirty);
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            var paths = new[] { "language-notebook.json", "life-progress.json", "onboarding.json" }.Select(p => Path.Combine(Application.persistentDataPath, p)).Concat(new[] { ScenePath });
            SessionState.SetString(Key + ".hashes", string.Join("\n", paths.Select(p => p + "|" + Hash(p))));
            SessionState.SetString(Key + ".began", DateTime.UtcNow.ToString("O"));
            SessionState.SetBool(Key + ".finished", false); SessionState.SetBool(Key, true);
            Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE", Path.GetFullPath(folder + "/save"));
            Environment.SetEnvironmentVariable(LifeOnboarding.ReviewOptIn, null);
            EditorSceneManager.playModeStartScene = null;
            EditorApplication.isPaused = false; EditorApplication.isPlaying = true;
        }
        static void Changed(PlayModeStateChange state)
        {
            if (!Owned) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                started = false; Application.runInBackground = true;
                var gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                if (gameView != null) EditorWindow.GetWindow(gameView).Focus();
                Status("RUNNING isolated Play");
            }
            if (state != PlayModeStateChange.EnteredEditMode) return;
            string result = "STOPPED; original scene preserved";
            try
            {
                Require(SessionState.GetBool(Key + ".finished", false), "Review was interrupted before completion.");
                foreach (string line in SessionState.GetString(Key + ".hashes", "").Split('\n'))
                { int split = line.LastIndexOf('|'); Require(split > 0 && Hash(line.Substring(0, split)) == line.Substring(split + 1), "Original scene or save changed: " + line); }
                Require(SceneManager.GetActiveScene().path == ScenePath && SceneManager.GetActiveScene().isDirty == SessionState.GetBool(Key + ".dirty", false), "Scene path or dirty state changed.");
                File.WriteAllText(Folder + "/preservation.txt", "PASS original scene file, all three real saves, active scene path and dirty state preserved");
            }
            catch (Exception e) { result = "FAIL preservation: " + e; File.WriteAllText(Folder + "/preservation.txt", result); }
            finally
            {
                Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE", EmptyNull(SessionState.GetString(Key + ".save", "")));
                Environment.SetEnvironmentVariable(LifeOnboarding.ReviewOptIn, EmptyNull(SessionState.GetString(Key + ".opt", "")));
                string start = SessionState.GetString(Key + ".startScene", "");
                EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(start) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
                Application.runInBackground = SessionState.GetBool(Key + ".background", false);
                SessionState.SetBool(Key, false); Status(result);
            }
        }
        static IEnumerator Guarded(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(routine);
            while (stack.Count > 0)
            {
                object current = null; bool more;
                try { more = stack.Peek().MoveNext(); if (more) current = stack.Peek().Current; }
                catch (Exception e) { Finish("FAIL " + e); yield break; }
                if (!more) { stack.Pop(); continue; }
                if (current is IEnumerator nested) stack.Push(nested); else yield return current;
            }
            Finish("PASS automated production UI/recognition Play checks; paths are test input, not a human mouse acceptance test.");
        }
        static IEnumerator Run(LifeSession life)
        {
            yield return new WaitForSecondsRealtime(3f);
            Require(GameplaySession.PersistenceDirectory == Path.GetFullPath(Folder + "/save"), "Play uses the isolated save directory.");
            var input = life.GetComponent<LifeGestureInput>(); var hud = life.GetComponent<GameplayHud>();
            Require(input && hud && hud.OnboardingReady && !LifeOnboarding.Blocks(life.Gameplay), "Production casting UI is ready.");
            var mood = UnityEngine.Object.FindObjectOfType<WatersideMood>(); Require(mood, "Formal scene exposes its actual day/night mood.");
            foreach (bool night in new[] { false, true })
            {
                mood.Apply(night ? WatersideMood.Mode.Night : WatersideMood.Mode.Day);
                yield return null;
                var button = UnityEngine.Object.FindObjectsOfType<Button>().Single(b => b.name == "Spell shortcut button");
                button.onClick.Invoke(); Require(input.IsWorldDrawing && life.GestureInputActive, "G HUD button enters casting.");
                string id = night ? "clear" : "rain";
                var points = Get<List<Vector2>>(input, "points"); var ink = Get<LifeStrokeGraphic>(input, "stroke");
                var magic = ink.GetComponentInChildren<LifeSpellInkGraphic>();
                Require(magic && magic.Daylight == (night ? 0f : 1f), "Ink follows the actual scene mood.");
                points.Clear(); ink.SetPath(null); Set(input, "drawing", true);
                var path = StrokePath(id, 190f);
                foreach (Vector2 point in path) { points.Add(point); ink.SetPath(points); yield return null; }
                yield return new WaitForEndOfFrame(); Capture(night ? "night-writing" : "day-writing");
                Set(input, "drawing", false); Call(input, "SubmitStroke");
                Require(life.LocalRainActive == !night, "Original recognizer and casting logic apply " + id);
                Require(Get<float>(input, "feedbackUntil") == magic.ReleaseEndTime, "Casting UI waits for its actual last mote.");
                yield return new WaitForSecondsRealtime(.48f); yield return new WaitForEndOfFrame(); Capture(night ? "night-condense" : "day-condense");
                yield return new WaitForSecondsRealtime(.75f); yield return new WaitForEndOfFrame(); Capture(night ? "night-fireflies" : "day-fireflies");
                yield return new WaitForSecondsRealtime(Mathf.Max(.1f, magic.ReleaseEndTime - Time.unscaledTime + .15f));
                Require(!input.IsWorldDrawing && !life.GestureInputActive && magic.SampleCount == 0, "Finished world release clears and restores controls.");
                Require(!(magic.mainTexture is RenderTexture), "Closing the world overlay releases its render target.");
            }
            mood.Apply(WatersideMood.Mode.Day); hud.UseBookShortcut(KeyCode.M);
            float deadline = Time.realtimeSinceStartup + 8f;
            while ((!hud.JournalOpen || hud.IsAlbumOpening) && Time.realtimeSinceStartup < deadline) yield return null;
            Require(hud.JournalOpen && !hud.IsAlbumOpening, "Production magic book opens.");
            hud.OnboardingSelectGesture("rain"); yield return null;
            var pad = UnityEngine.Object.FindObjectsOfType<LifeGesturePad>().Single(p => p.isActiveAndEnabled);
            Require(pad.ink.GetComponentInChildren<LifeSpellInkGraphic>(), "Actual handwriting pad uses the shared effect.");
            var raycaster = pad.GetComponentInParent<GraphicRaycaster>(); Require(raycaster && EventSystem.current, "Handwriting uses its actual event camera.");
            var pointer = new PointerEventData(EventSystem.current) { pointerId = -1, button = PointerEventData.InputButton.Left,
                pointerPressRaycast = new RaycastResult { module = raycaster, gameObject = pad.gameObject } };
            var practice = StrokePath("rain", 115f);
            for (int i = 0; i < practice.Count; i++)
            {
                pointer.position = RectTransformUtility.WorldToScreenPoint(raycaster.eventCamera, pad.transform.TransformPoint(practice[i]));
                if (i == 0) pad.OnPointerDown(pointer); else pad.OnDrag(pointer);
                yield return null;
            }
            yield return new WaitForEndOfFrame(); Capture("practice-writing");
            pad.OnPointerUp(pointer);
            Require(life.IsGestureLearned("rain") && !life.GestureInputActive, "Real pointer handlers preserve successful learning and release the input lock.");
            var practiceMagic = pad.ink.GetComponentInChildren<LifeSpellInkGraphic>();
            Require(practiceMagic && practiceMagic.Released, "Successful handwriting starts the firefly release.");
            Require(input.IsStrokeActive, "Learning refresh waits for the visible tail while movement input is already released.");
            yield return new WaitForSecondsRealtime(1.1f); yield return new WaitForEndOfFrame(); Capture("practice-fireflies");
            pad.ResetStroke(); Require(practiceMagic.SampleCount == 0 && !practiceMagic.Released, "Practice retry clears the pending tail.");
            pointer.position = RectTransformUtility.WorldToScreenPoint(raycaster.eventCamera, pad.transform.position);
            pad.OnPointerDown(pointer);
            pointer.position = RectTransformUtility.WorldToScreenPoint(raycaster.eventCamera, pad.transform.TransformPoint(new Vector3(0, 240, 0)));
            pad.OnDrag(pointer); pad.OnPointerUp(pointer);
            Require(!life.GestureInputActive && practiceMagic.Released, "Out-of-bounds writing releases input and uses failure feedback.");
            hud.ToggleJournal(); yield return new WaitForSecondsRealtime(2f);
            Require(!hud.JournalOpen && !life.GestureInputActive, "Closing the book clears handwriting and restores input.");
            input.ToggleWorldCasting(); Require(input.IsWorldDrawing, "Casting can restart after handwriting.");
            input.Cancel(); Require(!input.IsWorldDrawing && !life.GestureInputActive, "Cancel exits immediately after restart.");
        }
        static List<Vector2> StrokePath(string id, float scale)
        {
            var source = GestureCatalog.Find(id).points; var result = new List<Vector2>();
            for (int i = 1; i < source.Length; i++) for (int j = 0; j < 8; j++) result.Add(Vector2.Lerp(source[i - 1], source[i], j / 8f) * scale);
            result.Add(source[source.Length - 1] * scale); return result;
        }
        static void Capture(string name)
        {
            var frame = ScreenCapture.CaptureScreenshotAsTexture();
            try { Require(frame && frame.width > 0, "GameView capture is available."); File.WriteAllBytes(Folder + "/" + name + ".png", frame.EncodeToPNG()); }
            finally { if (frame) UnityEngine.Object.Destroy(frame); }
        }
        static void Finish(string result)
        {
            File.WriteAllText(Folder + "/checks.txt", result + "\n" + string.Join("\n", checks));
            SessionState.SetBool(Key + ".finished", true); Status(result); EditorApplication.isPlaying = false;
        }
        static void Status(string message)
        {
            Directory.CreateDirectory(Review);
            File.WriteAllText(Review + "/status.txt", DateTime.UtcNow.ToString("O") + "\n" + message + "\nFolder=" + Folder +
                "\nPlay=" + EditorApplication.isPlaying + "\nCompiling=" + EditorApplication.isCompiling + "\nActiveScene=" + SceneManager.GetActiveScene().path + "\nDirty=" + SceneManager.GetActiveScene().isDirty);
        }
        static void Require(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); checks.Add("PASS " + label); }
        static T Get<T>(object value, string field) => (T)value.GetType().GetField(field, Hidden).GetValue(value);
        static void Set(object value, string field, object data) => value.GetType().GetField(field, Hidden).SetValue(value, data);
        static void Call(object value, string method) => value.GetType().GetMethod(method, Hidden).Invoke(value, null);
        static string EmptyNull(string value) => string.IsNullOrEmpty(value) ? null : value;
        static string Hash(string path) { if (!File.Exists(path)) return "MISSING"; using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))); }
    }
}
