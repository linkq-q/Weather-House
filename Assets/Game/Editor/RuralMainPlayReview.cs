using System;
using System.IO;
using System.Linq;

using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHousePrototype;


using Object = UnityEngine.Object;

namespace WeatherHouse.Review
{
    // Disposable review only: normal scene reload, no gameplay/quality changes, protected notebook.
    [InitializeOnLoad]
    public static class RuralMainPlayReview
    {
        const string Key = "WeatherHouse.RuralMainPlay.", Folder = "JourneyReview/Revision_20261024/MapIntegrationPlan/MainPlayReview";
        static bool Active => SessionState.GetBool(Key + "active", false);
        static bool Ending => SessionState.GetBool(Key + "ending", false);
        static string DirectoryPath => SessionState.GetString(Key + "folder", "");

        [Serializable] public sealed class Report
        {
            public string status, label, scene, sceneHash, notebook, notebookHash, tempHash, startScene, startedUtc, endedUtc, error, samplePath;
            public bool previousBackground, previousPaused, returnedToEdit, sceneUnchanged, sceneClean, notebookRestored, optionsRestored;
            public bool enterOptionsEnabled;
            public int enterOptions;
            public string evidence = "Saved main-map Play: original player input and capsule; independent Game captures with restored temporary camera framing. No teleport, controller disable, model movement, scene save or performance tuning. Notebook restored after confirmed Edit.";
        }
        static RuralMainPlayReview()
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
        public static void Begin(string expectedSavedSceneHash, string suite = "all")
        {
            if (!string.Equals(Hash("Assets/Scenes/WeatherHouse_WatersideV2.unity"), expectedSavedSceneHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Saved scene hash changed; review the applied map before Play.");
            if (SceneManager.sceneCount != 1 || WeatherHouse.WatersideV2.WatersideLayout.TidePools.Length != 6)
                throw new InvalidOperationException("Need one saved scene and the applied six-pool layout.");
            if (!new[] { "all", "views", "details", "steps", "roofs", "coast", "entrances", "mage", "social", "herd" }.Contains(suite)) throw new ArgumentException("Unknown suite.");
            if (SessionState.GetBool("WeatherHouse.FoxRestPlay.active", false)) throw new InvalidOperationException("Another protected review owns Play.");
            if (suite == "mage") MageMainPlayProbe.Preflight();
            Start(suite);
        }
        static void Start(string label)
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
            string directory = Path.GetFullPath((label == "mage" ? "JourneyReview/Revision_20261024/MageCandidate/Review/MainPlay" : Folder) + "/Runs/" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_lifecycle_" + label);
            Directory.CreateDirectory(directory); SessionState.SetString(Key + "folder", directory);
            if (File.Exists(notebook)) File.Copy(notebook, Path.Combine(directory, "notebook.before"));
            if (File.Exists(notebook + ".tmp")) File.Copy(notebook + ".tmp", Path.Combine(directory, "notebook-temp.before"));

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
                if (report.label == "mage") SessionState.SetFloat(Key + "magePlayStart", Time.realtimeSinceStartup);
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
                || (DateTime.UtcNow.Ticks - started) / (double)TimeSpan.TicksPerSecond > 1200) { Finish("Review exceeded 1200 seconds."); return; }
            var report = Read();
            if (!string.IsNullOrEmpty(report.error)) { Finish(null); return; }
            if (Read().label == "mage" && EditorApplication.isPlaying && Time.realtimeSinceStartup - SessionState.GetFloat(Key + "magePlayStart", 0) >= 29)
            { Finish("Mage Play exceeded 29 seconds, including warmup or pause."); return; }
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                if (report.status == "WARMUP" && Time.realtimeSinceStartup - SessionState.GetFloat(Key + "startTime", 0) >= 4)
                {
                    report.samplePath = report.label == "mage" ? MageMainPlayProbe.Begin(Path.Combine(DirectoryPath, "sample"))
                        : report.label == "social" ? JourneySocialMainProbe.Begin(Path.Combine(DirectoryPath, "sample"))
                        : report.label == "herd" ? JourneyHerdMainProbe.Begin(Path.Combine(DirectoryPath, "sample"))
                        : RuralMainPlayProbe.Begin(Path.Combine(DirectoryPath, "sample"), report.label);
                    report.error = Read().error; // Begin can synchronously log an error through the registered listener.
                    report.status = "MEASURING"; Save(report);
                    SessionState.SetFloat(Key + "startTime", Time.realtimeSinceStartup);
                }
                else if (report.status == "MEASURING" && (report.label == "mage" ? MageMainPlayProbe.IsComplete : report.label == "social" ? JourneySocialMainProbe.IsComplete : report.label == "herd" ? JourneyHerdMainProbe.IsComplete : RuralMainPlayProbe.IsComplete))
                    Finish(report.label == "mage" ? MageMainPlayProbe.Failure : report.label == "social" ? JourneySocialMainProbe.Failure : report.label == "herd" ? JourneyHerdMainProbe.Failure : RuralMainPlayProbe.Failure);
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

            try
            {
                var report = Read(); string status = string.IsNullOrEmpty(report.error) ? "COMPLETE" : "ABORTED";
                if (report.label == "mage") MageMainPlayProbe.Finish(status); else if (report.label == "social") JourneySocialMainProbe.Finish(status); else if (report.label == "herd") JourneyHerdMainProbe.Finish(status); else RuralMainPlayProbe.Finish(status);
            }
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
            bool sampled = false;
            if (!string.IsNullOrEmpty(report.samplePath) && File.Exists(Path.Combine(report.samplePath, "sample.json")))
            {
                try
                {
                    string json = File.ReadAllText(Path.Combine(report.samplePath, "sample.json"));
                    if (report.label == "mage")
                    {
                        var sample = JsonUtility.FromJson<MageMainPlayProbe.Report>(json);
                        sampled = sample != null && sample.result == "PASS" && sample.restored && sample.configurationUnchanged;
                    }
                    else if (report.label == "social")
                    {
                        var sample = JsonUtility.FromJson<JourneySocialMainProbe.Report>(json);
                        sampled = sample != null && sample.result == "COMPLETE" && sample.restored && sample.configurationUnchanged;
                    }
                    else if (report.label == "herd")
                    {
                        var sample = JsonUtility.FromJson<JourneyHerdMainProbe.Report>(json);
                        sampled = sample != null && sample.result == "COMPLETE" && sample.restored && sample.configurationUnchanged && sample.completedOriginalJourney && sample.physicsFailures == 0;
                    }
                    else
                    {
                        var sample = JsonUtility.FromJson<RuralMainPlayProbe.Report>(json);
                        sampled = sample != null && sample.result == "COMPLETE" && sample.restored && sample.assetTransformsUnchanged;
                    }
                }
                catch (Exception e) { report.error += "Sample read failed: " + e; }
            }
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
