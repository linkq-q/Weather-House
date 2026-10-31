using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHousePrototype;
using WeatherHouse.Hillside;
using WeatherHouse.Hillside.Editor;
using Object = UnityEngine.Object;

namespace WeatherHouse.Review
{
    // Disposable review only: normal scene reload, no gameplay/quality changes, protected notebook.
    [InitializeOnLoad]
    public static class WatersideFoxRestPlayReview
    {
        const string Key = "WeatherHouse.FoxRestPlay.", Folder = "JourneyReview/Revision_20261024/AnimationInstall/MainReview";
        static bool Active => SessionState.GetBool(Key + "active", false);
        static bool Ending => SessionState.GetBool(Key + "ending", false);
        static string DirectoryPath => SessionState.GetString(Key + "folder", "");
        static Type probe;
        [Serializable] public sealed class Report
        {
            public string status, label, scene, sceneHash, notebook, notebookHash, tempHash, startScene, startedUtc, endedUtc, error, samplePath;
            public bool previousBackground, previousPaused, returnedToEdit, sceneUnchanged, sceneClean, notebookRestored, optionsRestored;
            public bool enterOptionsEnabled;
            public int enterOptions;
            public string evidence = "Owned Unity Editor Play, original scene, 4 s warm-up; 8 s natural fox observation, then explicitly triggered fatigue/hunger and one startle for two original foxes. Existing state machine chooses paths. Ordinary Game screenshots on the real terrain; temporary inspection framing is restored, no actor teleport, quality reduction, time acceleration, bake or scene save. Notebook and its .tmp are restored only after confirmed Edit mode.";
        }
        static WatersideFoxRestPlayReview()
        {
            EditorApplication.playModeStateChanged += Changed;
            EditorApplication.update += Tick;
            Application.logMessageReceived += Log;
            AssemblyReloadEvents.beforeAssemblyReload += Reload;
        }
        static string Hash(string path)
        {
            if (!File.Exists(path)) return "missing";
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        }
        static Report Read() => JsonUtility.FromJson<Report>(SessionState.GetString(Key + "report", ""));
        static void Save(Report value) => SessionState.SetString(Key + "report", JsonUtility.ToJson(value));
        static Type Probe
        {
            get
            {
                bool dog = Read()?.label == "dog-eating";
                string file = dog ? "JourneyReview/Revision_20261024/PopulationContact/Probe/DogEatingMainProbeV3.dll" : Folder + "/FoxRestMainProbeV4.dll";
                string type = dog ? "WeatherHouse.Review.DogEatingMainProbe" : "WeatherHouse.Review.FoxRestMainProbe";
                return probe ?? (probe = Assembly.LoadFile(Path.GetFullPath(file)).GetType(type, true));
            }
        }
        public static void BeginReviewedDogEating() => Begin("dog-eating");
        public static void BeginReviewed() => Begin("fox-rest");
        [Serializable] public sealed class BindingReport
        {
            public string status, error, backup, sceneBefore, sceneAfter, prefabBefore, prefabAfter;
            public int animals;
            public bool rootsUnchanged, sourceSceneUnchangedBeforeSave;
            public string[] foxClips;
        }
        public static void BindReviewedClips()
        {
            if (Active || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Bind only in idle Edit mode.");
            var scene = SceneManager.GetActiveScene();
            if (SceneManager.sceneCount != 1 || !scene.isLoaded || scene.isDirty || scene.path != "Assets/Scenes/WeatherHouse_WatersideV2.unity")
                throw new InvalidOperationException("Bind requires only the clean saved Waterside V2 scene.");
            const string prefab = "Assets/Game/World/Hillside/Generated/fox.prefab";
            var animals = Object.FindObjectsOfType<WildlifeAgent>(true).Where(a => a.gameObject.scene == scene).ToArray();
            var foxes = animals.Where(a => a.speciesId == "fox").OrderBy(a => a.spawnId).ToArray();
            if (foxes.Length != 2 || !File.Exists(prefab)) throw new InvalidOperationException("Expected two existing foxes and their saved original prefab.");
            var roots = animals.ToDictionary(a => a.transform, a => (position: a.transform.position, rotation: a.transform.rotation, scale: a.transform.localScale));
            bool RootsUnchanged() => roots.All(p => p.Key && p.Key.position.Equals(p.Value.position) && p.Key.rotation.Equals(p.Value.rotation) && p.Key.localScale.Equals(p.Value.scale));
            string backup = Path.GetFullPath(Folder + "/Bindings/" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(backup);
            var report = new BindingReport { status = "STARTED", error = "", backup = backup, animals = animals.Length,
                sceneBefore = Hash(scene.path), prefabBefore = Hash(prefab) };
            try
            {
                File.Copy(scene.path, Path.Combine(backup, "scene-disk-before.unity"));
                File.Copy(prefab, Path.Combine(backup, "fox-before.prefab"));
                File.Copy(prefab + ".meta", Path.Combine(backup, "fox-before.prefab.meta"));
                if (!EditorSceneManager.SaveScene(scene, Path.Combine(backup, "scene-memory-before.unity"), true))
                    throw new IOException("Could not preserve the loaded scene as a separate copy.");
                if (scene.isDirty || Hash(scene.path) != report.sceneBefore || !RootsUnchanged())
                    throw new InvalidOperationException("Scene changed while preparing the binding backup.");
                FoxRestImport.ApplyToExistingFoxes();
                report.rootsUnchanged = RootsUnchanged();
                if (!report.rootsUnchanged) throw new InvalidOperationException("Binding unexpectedly changed an animal root transform; backups retained.");
                report.foxClips = foxes.SelectMany(fox => new[] { "forage", "restDown", "restSleep", "restUp" }.Select(name =>
                {
                    var clip = typeof(WildlifeClips).GetField(name)?.GetValue(fox.clips) as AnimationClip;
                    string path = AssetDatabase.GetAssetPath(clip);
                    if (!clip || string.IsNullOrEmpty(path)) throw new InvalidOperationException("Fox clip not bound: " + name);
                    return fox.spawnId + " | " + name + " | " + clip.name + " | " + AssetDatabase.AssetPathToGUID(path) + " | " + path;
                })).ToArray();
                report.sourceSceneUnchangedBeforeSave = Hash(scene.path) == report.sceneBefore;
                if (!report.sourceSceneUnchangedBeforeSave) throw new IOException("Scene file was changed externally before saving; binding remains unsaved and backups retained.");
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the owned fox binding.");
                report.rootsUnchanged &= RootsUnchanged();
                if (!report.rootsUnchanged || scene.isDirty) throw new InvalidOperationException("Post-save transform/clean-state check failed.");
                report.status = "PASS";
            }
            catch (Exception error) { report.error = error.ToString(); report.status = "FAIL"; throw; }
            finally
            {
                report.sceneAfter = Hash(scene.path); report.prefabAfter = Hash(prefab);
                File.WriteAllText(Path.Combine(backup, "binding-result.json"), JsonUtility.ToJson(report, true));
            }
        }
        public static void Begin(string label)
        {
            if (string.IsNullOrWhiteSpace(label) || label.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
                throw new ArgumentException("Use a short alphanumeric review label.", nameof(label));
            if (Active || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Begin from idle Edit mode; another review may be active.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/WeatherHouse_WatersideV2.unity" || Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Review requires the saved Waterside scene and no dirty loaded scene.");
            if (EditorSettings.enterPlayModeOptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0)
                throw new InvalidOperationException("Ordinary scene reload is required; editor setting was not changed.");
            var gameplay = Object.FindObjectOfType<GameplaySession>();
            if (!gameplay) throw new InvalidOperationException("GameplaySession missing; notebook protection cannot be established.");
            string notebook = gameplay.SavePath;
            var report = new Report { label = label, status = "ENTERING", scene = scene.path, sceneHash = Hash(scene.path),
                notebook = notebook, notebookHash = Hash(notebook), tempHash = Hash(notebook + ".tmp"),
                previousBackground = Application.runInBackground, previousPaused = EditorApplication.isPaused,
                startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
                enterOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled, enterOptions = (int)EditorSettings.enterPlayModeOptions,
                startedUtc = DateTime.UtcNow.ToString("O"), error = "" };
            if (label == "dog-eating") report.evidence = "Protected original-scene Play; 4 s warm-up followed by at most 60 s entirely natural dog Idle/Forage transitions. No animal teleport or forced state. Temporary player input hold/camera framing restored, notebook protected only after confirmed Edit. No scene save.";
            string directory = Path.GetFullPath(Folder + "/Runs/" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_lifecycle_" + label);
            Directory.CreateDirectory(directory); SessionState.SetString(Key + "folder", directory);
            if (File.Exists(notebook)) File.Copy(notebook, Path.Combine(directory, "notebook.before"));
            if (File.Exists(notebook + ".tmp")) File.Copy(notebook + ".tmp", Path.Combine(directory, "notebook-temp.before"));
            probe = null;
            Save(report); SessionState.SetBool(Key + "active", true); SessionState.SetBool(Key + "ending", false);
            SessionState.SetBool(Key + "restoreBlocked", false); SessionState.SetBool(Key + "editConfirmed", true);
            SessionState.SetString(Key + "ticks", DateTime.UtcNow.Ticks.ToString());
            File.WriteAllText(Path.Combine(directory, "running.json"), JsonUtility.ToJson(report, true));
            try
            {
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path);
                Application.runInBackground = true; EditorApplication.isPaused = false;
                EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
                SessionState.SetBool(Key + "editConfirmed", false); EditorApplication.isPlaying = true;
            }
            catch (Exception e)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) SessionState.SetBool(Key + "editConfirmed", true);
                Finish(e.ToString());
            }
        }
        static void Changed(PlayModeStateChange state)
        {
            if (!Active) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                SessionState.SetBool(Key + "editConfirmed", false);
                if (Ending) { EditorApplication.isPlaying = false; return; }
                var report = Read(); report.status = "WARMUP"; Save(report);
                SessionState.SetFloat(Key + "startTime", Time.realtimeSinceStartup);
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                if (!Ending) { var report = Read(); report.error += "Play exited before sampling completed."; Save(report); SessionState.SetBool(Key + "ending", true); }
                StopProbe();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(Key + "editConfirmed", true); Restore();
            }
        }
        static void Tick()
        {
            if (!Active) return;
            if (Ending)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
                else if (SessionState.GetBool(Key + "editConfirmed", false) && !SessionState.GetBool(Key + "restoreBlocked", false)) Restore();
                return;
            }
            if (!long.TryParse(SessionState.GetString(Key + "ticks", ""), out long started)
                || (DateTime.UtcNow.Ticks - started) / (double)TimeSpan.TicksPerSecond > 160) { Finish("Review exceeded 160 seconds."); return; }
            var report = Read();
            if (!string.IsNullOrEmpty(report.error)) { Finish(null); return; }
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                if (report.status == "WARMUP" && Time.realtimeSinceStartup - SessionState.GetFloat(Key + "startTime", 0) >= 4)
                {
                    report.samplePath = (string)Probe.GetMethod("Begin").Invoke(null, new object[] { report.label, 120.0 });
                    report.error = Read().error; // Begin can synchronously log an error through the registered listener.
                    report.status = "MEASURING"; Save(report);
                    SessionState.SetFloat(Key + "startTime", Time.realtimeSinceStartup);
                }
                else if (report.status == "MEASURING" && (bool)Probe.GetProperty("IsComplete").GetValue(null))
                    Finish((string)Probe.GetProperty("Failure").GetValue(null));
            }
            catch (Exception e) { Finish(e.ToString()); }
        }
        static void Log(string message, string stack, LogType type)
        {
            if (!Active || Ending || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
            var report = Read(); report.error += message + "\n" + stack + "\n"; Save(report);
        }
        static void Reload()
        {
            if (!Active || Ending) return;
            var report = Read();
            if (report.status != "WARMUP" && report.status != "MEASURING") return;
            report.error += "Unexpected script reload during sampling."; Save(report); StopProbe();
            SessionState.SetBool(Key + "ending", true);
        }
        static void StopProbe()
        {
            if (probe == null) return;
            try { probe.GetMethod("Finish").Invoke(null, new object[] { string.IsNullOrEmpty(Read().error) ? "COMPLETE" : "ABORTED" }); }
            catch (Exception e) { var report = Read(); report.error += "Probe stop: " + e; Save(report); }
        }
        public static void Stop() => Finish("Stopped by request.");
        static void Finish(string error)
        {
            if (!Active) return;
            var report = Read(); if (!string.IsNullOrEmpty(error)) report.error += error;
            report.status = "ENDING"; Save(report); SessionState.SetBool(Key + "ending", true); StopProbe();
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false; else Restore();
        }
        static void ValidateBackup(string name, string expected)
        {
            if (Hash(Path.Combine(DirectoryPath, name + ".before")) != expected)
                throw new IOException("Backup validation failed: " + name + ". Keep review files and retry after repair.");
        }
        static void RestoreFile(string destination, string name, string expected)
        {
            if (expected != "missing") File.Copy(Path.Combine(DirectoryPath, name + ".before"), destination, true);
            else if (File.Exists(destination)) File.Delete(destination);
        }
        static void Restore()
        {
            if (!Active || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
                || !SessionState.GetBool(Key + "editConfirmed", false) || SessionState.GetBool(Key + "restoreBlocked", false)) return;
            var report = Read();
            try
            {
                // Validate both originals before writing either profile file.
                ValidateBackup("notebook", report.notebookHash); ValidateBackup("notebook-temp", report.tempHash);
                RestoreFile(report.notebook, "notebook", report.notebookHash); RestoreFile(report.notebook + ".tmp", "notebook-temp", report.tempHash);
                report.notebookRestored = Hash(report.notebook) == report.notebookHash && Hash(report.notebook + ".tmp") == report.tempHash;
                if (!report.notebookRestored) throw new IOException("Notebook restore did not match the original hashes.");
            }
            catch (Exception e)
            {
                report.error += "Restore failed; backups retained: " + e; SessionState.SetBool(Key + "restoreBlocked", true);
            }
            finally
            {
                try
                {
                    Application.runInBackground = report.previousBackground; EditorApplication.isPaused = report.previousPaused;
                    EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(report.startScene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(report.startScene);
                    report.optionsRestored = Application.runInBackground == report.previousBackground && EditorApplication.isPaused == report.previousPaused
                        && AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == report.startScene
                        && EditorSettings.enterPlayModeOptionsEnabled == report.enterOptionsEnabled && (int)EditorSettings.enterPlayModeOptions == report.enterOptions;
                }
                catch (Exception e) { report.error += "Editor option restore failed: " + e; SessionState.SetBool(Key + "restoreBlocked", true); }
            }
            report.returnedToEdit = !EditorApplication.isPlayingOrWillChangePlaymode;
            report.sceneUnchanged = Hash(report.scene) == report.sceneHash;
            report.sceneClean = SceneManager.GetActiveScene().path == report.scene && !Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty);
            bool sampled = !string.IsNullOrEmpty(report.samplePath) && File.Exists(Path.Combine(report.samplePath, "sample.json"));
            report.status = string.IsNullOrEmpty(report.error) && sampled && report.returnedToEdit && report.sceneUnchanged && report.sceneClean && report.notebookRestored && report.optionsRestored ? "PASS" : "FAIL";
            report.endedUtc = DateTime.UtcNow.ToString("O"); Save(report);
            string path = Path.Combine(DirectoryPath, "result-" + DateTime.UtcNow.ToString("HHmmssfff") + ".json");
            try { File.WriteAllText(path, JsonUtility.ToJson(report, true)); }
            catch (Exception e)
            {
                report.error += "Result write failed: " + e; Save(report); SessionState.SetBool(Key + "restoreBlocked", true);
            }
            if (!SessionState.GetBool(Key + "restoreBlocked", false)) { SessionState.SetBool(Key + "active", false); SessionState.SetBool(Key + "ending", false); }
        }
        public static void RetryRestore()
        {
            if (!Active || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Retry only in confirmed idle Edit mode.");
            SessionState.SetBool(Key + "editConfirmed", true); SessionState.SetBool(Key + "restoreBlocked", false); Restore();
        }
    }
}
