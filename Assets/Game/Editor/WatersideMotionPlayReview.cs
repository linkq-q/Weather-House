using System;
using System.IO;
using System.Collections.Generic;
using ForestSpriteCharacter;
using WeatherHouse.Vegetation;
using WeatherHouse.WatersideV2;
using System.Linq;
using System.Reflection;
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
    public static class WatersideMotionPlayReview
    {
        const string Key = "WeatherHouse.MotionPlay.", Folder = "JourneyReview/Revision_20261024/MotionReview";
        static bool Active => SessionState.GetBool(Key + "active", false);
        static bool Ending => SessionState.GetBool(Key + "ending", false);
        static string DirectoryPath => SessionState.GetString(Key + "folder", "");
        static ForestSpriteDemoController player;
        static Camera view;
        static VegetationTopDownCamera orbit;
        static LODGroup[] fences;
        static Vector3 routeDirection, walkStart;
        static int phase, shot, lastFrame;
        static float phaseStarted, nextShot;
        static readonly List<string> captures = new List<string>();
        static readonly List<float> speeds = new List<float>();
        static readonly Vector3[] fencePositions = { new Vector3(-29.005417f,2.168939f,-30.521046f), new Vector3(-28.81251f,2.28505f,-31.75228f), new Vector3(-32.34170150756836f,2.5340611934661865f,-33.877655029296875f) };
        [Serializable] public sealed class Report
        {
            public string status, label, scene, sceneHash, notebook, notebookHash, tempHash, startScene, startedUtc, endedUtc, error, samplePath;
            public bool previousBackground, previousPaused, returnedToEdit, sceneUnchanged, sceneClean, notebookRestored, optionsRestored;
            public float walkSpeed, cycleDistance, measuredMeanSpeed, travelled;
            public int images;
            public bool enterOptionsEnabled;
            public int enterOptions;
            public string evidence = "Owned actual main-scene Play: scripted production controller walk, continuous camera movement over water and near/far fence LOD visual capture. No performance timing claim; scene/Notebook restored on exit.";
        }
        static WatersideMotionPlayReview()
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
        static void Save(Report value)
        {
            if (Active)
            {
                var latest = Read();
                if (latest != null && !string.IsNullOrEmpty(latest.error) && (value.error == null || !value.error.Contains(latest.error)))
                    value.error = latest.error + "\n" + value.error;
            }
            SessionState.SetString(Key + "report", JsonUtility.ToJson(value));
        }
        public static void BeginBaseline() => Begin("baseline");
        public static void BeginCandidate() => Begin("candidate");
        public static void BeginWaterBaseline() => Begin("water-baseline");
        public static void BeginWaterCandidate() => Begin("water-candidate");
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
            string directory = Path.GetFullPath(Folder + "/Runs/" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_lifecycle_" + label);
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
                var report = Read(); report.status = "WARMUP"; Save(report); phase = 0; shot = 0; lastFrame = -1; captures.Clear(); speeds.Clear();
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
                || (DateTime.UtcNow.Ticks - started) / (double)TimeSpan.TicksPerSecond > 120) { Finish("Review exceeded 120 seconds."); return; }
            var report = Read();
            if (!string.IsNullOrEmpty(report.error)) { Finish(null); return; }
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                if (Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                if (report.status == "WARMUP")
                {
                    if (Time.realtimeSinceStartup - SessionState.GetFloat(Key + "startTime", 0) < 2) return;
                    Setup(report); report.status = "MEASURING"; Save(report);
                    phaseStarted = Time.unscaledTime; nextShot = 0;
                }
                Step(report);
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
            if (player) player.ReleaseScriptedInput();
            if (fences != null) foreach (var f in fences) if (f) f.ForceLOD(-1);
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
            bool sampled = report.images >= (report.label.StartsWith("water-") ? 20 : 40) && Directory.GetFiles(DirectoryPath, "*.png").Length == report.images;
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

        static void Setup(Report report)
        {
            var session = Object.FindObjectOfType<GameplaySession>();
            player = session.forestSprite; view = session.PlayerViewCamera;
            orbit = view.GetComponent<VegetationTopDownCamera>(); orbit.enabled = false;
            player.SetScriptedInput(Vector2.zero, false, false);
            report.walkSpeed = player.walkSpeed; report.cycleDistance = player.walkCycleDistance;
            if (report.label.StartsWith("water-")) { phase=1; return; }
            if (Mathf.Abs(player.walkSpeed - 2.925f) > .001f || Mathf.Abs(player.walkCycleDistance - .95f) > .001f)
                throw new InvalidOperationException("Main player's persisted speed/cycle did not migrate.");
            FindRoute();
            view.transform.rotation = Quaternion.LookRotation(routeDirection, Vector3.up);
            player.SetScriptedInput(Vector2.up, false, false);
            walkStart = player.transform.position;
            fences = fencePositions.Select(p => Object.FindObjectsOfType<LODGroup>().OrderBy(g => Vector3.Distance(g.transform.position,p)).First()).ToArray();
            for (int i = 0; i < fences.Length; i++)
                if (Vector3.Distance(fences[i].transform.position, fencePositions[i]) > .02f) throw new Exception("Reviewed fence root missing.");
        }
        static void FindRoute()
        {
            var body = player.GetComponent<CharacterController>(); var terrain = Terrain.activeTerrain;
            var water = player.GetComponent<WatersideWaterBoundary>();
            Vector3 origin = player.transform.position;
            float radius = body.radius * Mathf.Abs(player.transform.lossyScale.x) + .04f;
            float height = body.height * Mathf.Abs(player.transform.lossyScale.y);
            for (int ring = 0; ring < 6; ring++) for (int side = 0; side < 8; side++) for (int heading = 0; heading < 8; heading++)
            {
                Vector3 start = origin + Quaternion.Euler(0,side*45,0) * Vector3.forward * ring * 1.5f;
                Vector3 dir = Quaternion.Euler(0,heading*45,0) * Vector3.forward;
                bool clear = true;
                for (int step = 0; step <= 20; step++)
                {
                    Vector3 at = start + dir * (step * .35f); at.y = terrain.SampleHeight(at) + terrain.transform.position.y;
                    if (water && water.SampleSupportedDepth(at, at.y) > .03f) { clear = false; break; }
                    if (Physics.OverlapCapsule(at + Vector3.up*(radius+.08f), at + Vector3.up*Mathf.Max(radius+.08f,height-radius),radius,
                        Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).Any(c => !(c is TerrainCollider) && c != body && !c.transform.IsChildOf(player.transform))) { clear = false; break; }
                }
                if (!clear) continue;
                start.y = terrain.SampleHeight(start) + terrain.transform.position.y + .02f;
                body.enabled = false; player.transform.position = start; body.enabled = true; Physics.SyncTransforms(); routeDirection = dir; return;
            }
            throw new Exception("No dry clear seven metre route near player; walk review not performed.");
        }
        static void Aim(Vector3 target, float yaw, float pitch, float distance)
        {
            Quaternion rot = Quaternion.Euler(pitch,yaw,0);
            view.transform.SetPositionAndRotation(target + rot*Vector3.back*distance,rot);
        }
        static void Shot(string name, Report report)
        {
            string path = Path.Combine(DirectoryPath,name + ".png");
            ScreenCapture.CaptureScreenshot(path); captures.Add(path); report.images = captures.Count;
            Save(report);
        }
        static void Step(Report report)
        {
            float age = Time.unscaledTime - phaseStarted;
            if (phase == 0)
            {
                // Keep camera yaw aligned to input so the production controller follows the tested corridor.
                float yaw = Quaternion.LookRotation(routeDirection).eulerAngles.y;
                Aim(player.transform.position+Vector3.up*.6f,yaw,30,4.8f);
                if (age > .4f) speeds.Add(new Vector2(player.ActualVelocity.x,player.ActualVelocity.z).magnitude);
                if (age >= nextShot) { Shot("walk_"+shot++.ToString("D3"),report); nextShot=age+.12f; }
                if (age < 2.1f) return;
                report.measuredMeanSpeed=speeds.Average(); report.travelled=Vector3.Distance(walkStart,player.transform.position);
                if (report.measuredMeanSpeed < report.walkSpeed*.90f || report.measuredMeanSpeed > report.walkSpeed*1.04f) throw new Exception("Actual main-scene walk speed outside expected range: "+report.measuredMeanSpeed);
                player.SetScriptedInput(Vector2.zero,false,false); phase=1; phaseStarted=Time.unscaledTime; nextShot=0; shot=0; Save(report);
            }
            else if (phase == 1)
            {
                float t=Mathf.Clamp01(age/5.4f);
                Vector3 lake = WatersideLayout.WaterChannels[0].Nodes[2].Position;
                Aim(lake + Vector3.right*Mathf.Lerp(-1.5f,1.5f,t),55+12*Mathf.Sin(t*Mathf.PI*2),40,Mathf.Lerp(20,13,t));
                if(age>=nextShot) { Shot("water_"+shot++.ToString("D3"),report); nextShot=age+.15f; }
                if(age<5.4f)return;
                phase=report.label.StartsWith("water-") ? 5 : 2; phaseStarted=Time.unscaledTime; nextShot=0; shot=0;
            }
            else if (phase>=2 && phase<=4)
            {
                int index=phase-2; var fence=fences[index];
                var lods=fence.GetLODs(); Vector3 target=lods[0].renderers[0].bounds.center;
                if(age<2.4f)
                {
                    fence.ForceLOD(-1); Aim(target,70,23,Mathf.Lerp(3,32,Mathf.PingPong(age/1.2f,1)));
                    if(age>=nextShot) { Shot("fence"+index+"_zoom_"+shot++.ToString("D3"),report); nextShot=age+.24f; }
                }
                else if(shot<103)
                {
                    int level=shot<100 ? 0 : shot-100;
                    fence.ForceLOD(level); Aim(target,70,23,4);
                    if(age>=nextShot) { Shot("fence"+index+"_lod"+level,report); shot=101+level; nextShot=age+.4f; }
                }
                else { fence.ForceLOD(-1); phase++; phaseStarted=Time.unscaledTime; nextShot=0; shot=0; }
            }
            else if(age>.5f)
            {
                if (!captures.All(p => File.Exists(p) && new FileInfo(p).Length > 1024))
                { if(age>8) throw new IOException("Captured images did not finish writing."); return; }
                if (!report.label.StartsWith("water-")) for (int i=0;i<3;i++) for(int lod=0;lod<3;lod++)
                    if (!File.Exists(Path.Combine(DirectoryPath,"fence"+i+"_lod"+lod+".png")))
                        throw new Exception("A requested fence LOD capture was skipped; visual evidence incomplete.");
                foreach(string prefix in report.label.StartsWith("water-") ? new[]{"water_"} : new[] {"walk_", "water_", "fence0_zoom_", "fence1_zoom_", "fence2_zoom_"})
                    if(!Directory.GetFiles(DirectoryPath,prefix+"*.png").Any()) throw new Exception("Missing visual sequence: "+prefix);
                Finish(null);
            }
        }

        public static void RetryRestore()
        {
            if (!Active || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Retry only in confirmed idle Edit mode.");
            SessionState.SetBool(Key + "editConfirmed", true); SessionState.SetBool(Key + "restoreBlocked", false); Restore();
        }
    }
}
