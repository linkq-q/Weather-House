using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

namespace WeatherHouse.WildlifeJourney.Editor
{
    // A camera-only review of the saved map. Animal decisions, destinations and clocks stay untouched.
    [InitializeOnLoad]
    public static class JourneyEcologyPlayReview
    {
        const string Key = "WeatherHouse.EcologyReview.";
        const string Request = "JourneyReview/Revision_20261024/ecology-play-request.txt";
        const string MissingFile = "<missing>";
        static JourneyHerd herd;
        static WildlifePopulation population;
        static WildlifeAgent[] foxes;
        static Camera camera;
        static RenderTexture target;
        static double started, nextPoll;
        static float nextTrace, nextFrame, previousTime, fixedVisibleSeconds, fixedVisibleRun, fixedLongestVisibleRun;
        static string folder, cameraPhase;
        static int frames, chatShots, formationFrames, foxFrames, fixedShots, fixedChatShots, lastFrame = -1, focusedFoxId = -1;
        static Vector3 fixedCenter;
        static readonly HashSet<string> foxActivities = new HashSet<string>();
        static readonly HashSet<int> speakers = new HashSet<int>();
        static readonly HashSet<int> fixedSpeakers = new HashSet<int>();
        static readonly HashSet<string> capturedTurns = new HashSet<string>();
        static bool Running => SessionState.GetBool(Key + "running", false);
        static bool notebookRecoveryAttempted;

        [Serializable] sealed class NotebookBackup
        {
            public string path, notebookHash, temporaryHash;
        }

        static JourneyEcologyPlayReview()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += PlayChanged;
        }

        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + .08;
            try
            {
                if (SessionState.GetBool(Key + "cleanup", false) && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    if (!notebookRecoveryAttempted) CompleteCleanup();
                    return;
                }
                if (Running)
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    { Finish("FAIL", "Play ended or could not start; recovering the notebook from Edit mode.", false); CompleteCleanup(); return; }
                    if (EditorApplication.isPlaying)
                    {
                        if (string.IsNullOrEmpty(folder))
                        {
                            if (!SessionState.GetBool(Key + "entered", false)) return;
                            folder = SessionState.GetString(Key + "folder", "");
                            Finish("FAIL", "Scripts reloaded during the review; the partial capture is preserved and the run was not restarted.");
                            return;
                        }
                        if (EditorApplication.timeSinceStartup - started > 720)
                        { Finish("FAIL", "Real-time review exceeded 12 minutes, including initialization."); return; }
                        Observe();
                    }
                    return;
                }
                if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
                if (File.ReadAllText(Request).Trim() != "playcheck") throw new InvalidOperationException("Unknown ecology review request.");
                File.Delete(Request); Begin();
            }
            catch (Exception error)
            {
                if (Running) Finish("FAIL", error.ToString());
                else { Directory.CreateDirectory(Path.GetDirectoryName(Request)); File.WriteAllText(Request + ".error", error.ToString()); }
                Debug.LogException(error);
            }
        }

        public static void Begin()
        {
            var scene = SceneManager.GetActiveScene();
            if (Running || SessionState.GetBool(Key + "cleanup", false) || !string.IsNullOrEmpty(SessionState.GetString(Key + "notebookManifest", "")))
                throw new InvalidOperationException("Finish or recover the previous ecology review before starting another one.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty || scene.path != "Assets/Scenes/WeatherHouse_WatersideV2.unity")
                throw new InvalidOperationException("Review the saved Waterside scene from clean Edit mode.");
            folder = "JourneyReview/Revision_20261024/MapPlay_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            if (Directory.Exists(folder)) throw new IOException("The review directory already exists; preserve its backups and retry later.");
            Directory.CreateDirectory(folder);
            File.WriteAllText(folder + "/summary.txt", "RUNNING\nReal Update; unchanged animal parameters and clocks; temporary observation camera.\n");
            File.WriteAllText("JourneyReview/Revision_20261024/latest-map-play.txt", folder);
            string manifest;
            try { manifest = BackupNotebook(Path.Combine(Application.persistentDataPath, "language-notebook.json"), folder); }
            catch (Exception error)
            { File.AppendAllText(folder + "/summary.txt", "FAIL notebook backup; Play was not started.\n" + error + "\n"); throw; }
            SessionState.SetString(Key + "folder", folder);
            SessionState.SetString(Key + "notebookManifest", manifest);
            SessionState.SetBool(Key + "background", Application.runInBackground);
            SessionState.SetBool(Key + "paused", EditorApplication.isPaused);
            SessionState.SetBool(Key + "running", true);
            SessionState.SetBool(Key + "cleanup", false);
            SessionState.SetBool(Key + "entered", false);
            notebookRecoveryAttempted = false;
            try { EditorApplication.isPlaying = true; }
            catch (Exception error) { Finish("FAIL", error.ToString(), false); CompleteCleanup(); throw; }
        }

        static void PlayChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && Running)
            {
                folder = SessionState.GetString(Key + "folder", "");
                SessionState.SetBool(Key + "entered", true);
                started = EditorApplication.timeSinceStartup;
                nextTrace = nextFrame = previousTime = fixedVisibleSeconds = fixedVisibleRun = fixedLongestVisibleRun = 0;
                frames = chatShots = formationFrames = foxFrames = fixedShots = fixedChatShots = 0; lastFrame = -1;
                herd = null; population = null; camera = null; target = null; focusedFoxId = -1;
                cameraPhase = "fixed_sheep";
                foxActivities.Clear(); speakers.Clear(); fixedSpeakers.Clear(); capturedTurns.Clear();
                Application.runInBackground = true; EditorApplication.isPaused = false;
                File.WriteAllText(folder + "/trajectory.csv", "time,type,id,state,x,y,z,speed,detail\n");
                File.WriteAllText(folder + "/visibility.csv", "time,phase,speaker,listener,visible,observed,fixedVisibleSeconds,cameraX,cameraY,cameraZ,orthoSize\n");
            }
            if (state == PlayModeStateChange.ExitingPlayMode && Running)
                Finish("FAIL", "Play mode was stopped before the 210-second observation completed.", false);
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + "cleanup", false))
                CompleteCleanup();
        }

        static string BackupNotebook(string path, string reviewFolder)
        {
            path = Path.GetFullPath(path); reviewFolder = Path.GetFullPath(reviewFolder);
            var backup = new NotebookBackup { path = path, notebookHash = FileHash(path), temporaryHash = FileHash(path + ".tmp") };
            string[] paths = { path, path + ".tmp" }, hashes = { backup.notebookHash, backup.temporaryHash };
            for (int i = 0; i < paths.Length; i++)
            {
                if (hashes[i] == MissingFile) continue;
                string copy = Path.Combine(reviewFolder, i == 0 ? "notebook.before" : "notebook-temp.before");
                File.Copy(paths[i], copy, false);
                if (FileHash(copy) != hashes[i] || FileHash(paths[i]) != hashes[i]) throw new IOException("Notebook backup verification failed: " + paths[i]);
            }
            string manifest = Path.Combine(reviewFolder, "notebook-backup.json");
            using (var writer = new StreamWriter(new FileStream(manifest, FileMode.CreateNew, FileAccess.Write)))
                writer.Write(JsonUtility.ToJson(backup, true));
            File.AppendAllText(Path.Combine(reviewFolder, "summary.txt"), "NOTEBOOK_BACKUP path=" + path + "\nnotebook=" + backup.notebookHash + "\ntemporary=" + backup.temporaryHash + "\n");
            return manifest;
        }

        static string RestoreNotebook(string manifest)
        {
            if (string.IsNullOrEmpty(manifest)) throw new IOException("Notebook recovery manifest is missing; no profile files were changed.");
            var backup = JsonUtility.FromJson<NotebookBackup>(File.ReadAllText(manifest));
            if (backup == null || string.IsNullOrEmpty(backup.path) || Path.GetFileName(backup.path) != "language-notebook.json" ||
                !ValidHash(backup.notebookHash) || !ValidHash(backup.temporaryHash)) throw new IOException("Invalid notebook recovery manifest; no profile files were changed.");
            string reviewFolder = Path.GetDirectoryName(manifest);
            string[] paths = { backup.path, backup.path + ".tmp" }, hashes = { backup.notebookHash, backup.temporaryHash };
            string[] copies = { Path.Combine(reviewFolder, "notebook.before"), Path.Combine(reviewFolder, "notebook-temp.before") };
            // Validate both originals before changing either live file, including restoring an originally absent file.
            for (int i = 0; i < paths.Length; i++)
                if (hashes[i] != MissingFile && FileHash(copies[i]) != hashes[i])
                    throw new IOException("Notebook backup is missing or damaged; live files were not changed: " + copies[i]);
            var evidence = new List<string>();
            for (int i = 0; i < paths.Length; i++)
            {
                string current = FileHash(paths[i]);
                if (current != hashes[i])
                {
                    string retained = Path.Combine(reviewFolder, (i == 0 ? "notebook" : "notebook-temp") + ".during-review-" + Guid.NewGuid().ToString("N"));
                    if (hashes[i] == MissingFile)
                        File.Move(paths[i], retained); // Preserve review-created data while restoring the original absence.
                    else
                    {
                        if (current != MissingFile)
                        {
                            File.Copy(paths[i], retained, false);
                            if (FileHash(retained) != current) throw new IOException("Could not preserve the review's notebook output: " + paths[i]);
                        }
                        string staging = paths[i] + ".ecology-restore-" + Guid.NewGuid().ToString("N");
                        File.Copy(copies[i], staging, false);
                        if (FileHash(staging) != hashes[i]) throw new IOException("Notebook restore staging verification failed: " + staging);
                        if (current == MissingFile) File.Move(staging, paths[i]);
                        else File.Replace(staging, paths[i], null);
                    }
                }
                string restored = FileHash(paths[i]);
                if (restored != hashes[i]) throw new IOException("Notebook restore verification failed: " + paths[i]);
                evidence.Add("NOTEBOOK_RESTORE path=" + paths[i] + " original=" + hashes[i] + " review=" + current + " restored=" + restored);
            }
            return string.Join("\n", evidence) + "\n";
        }

        static bool ValidHash(string value) => value == MissingFile || value != null && value.Length == 64 && value.All(Uri.IsHexDigit);
        static string FileHash(string path)
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) throw new IOException("A directory occupies the notebook file path: " + path);
                using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            }
            catch (FileNotFoundException) { return MissingFile; }
            catch (DirectoryNotFoundException) { return MissingFile; }
        }

        static void CompleteCleanup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            notebookRecoveryAttempted = true;
            string reviewFolder = SessionState.GetString(Key + "folder", "");
            try
            {
                string evidence = RestoreNotebook(SessionState.GetString(Key + "notebookManifest", ""));
                File.AppendAllText(Path.Combine(reviewFolder, "summary.txt"), evidence + "HANDOFF Play=False dirty=" + SceneManager.GetActiveScene().isDirty + "\n");
                SessionState.SetBool(Key + "cleanup", false);
                SessionState.EraseString(Key + "notebookManifest");
            }
            catch (Exception error)
            {
                string message = "FAIL notebook restore; backups and recovery manifest retained for retry.\n" + error + "\n";
                try { File.AppendAllText(Path.Combine(reviewFolder, "summary.txt"), message); }
                catch (Exception reportError) { Debug.LogError("Could not write notebook recovery failure report: " + reportError); }
                Debug.LogError(message);
            }
            finally
            {
                Application.runInBackground = SessionState.GetBool(Key + "background", false);
                EditorApplication.isPaused = SessionState.GetBool(Key + "paused", false);
            }
        }

        static void Observe()
        {
            if (!camera)
            {
                Scene scene = SceneManager.GetActiveScene();
                herd = Object.FindObjectsOfType<JourneyHerd>().SingleOrDefault(h => h.gameObject.scene == scene);
                population = WeatherHouse.Hillside.Editor.WildlifeSolitaryIntegration.FindFoxPopulation(scene);
                if (!herd || !population || !population.Initialized || !Camera.main) return;
                if (!herd.social) throw new InvalidOperationException("Saved herd has no animal speech controller.");
                foxes = population.agents.Where(a => a && a.speciesId == "fox").OrderBy(a => a.spawnId).ToArray();
                if (foxes.Length != 2 || foxes[0].spawnId != 20 || foxes[1].spawnId != 21 || foxes.Any(a => a.SolitaryRoutine == null))
                    throw new InvalidOperationException("The saved map must explicitly enable solitary routines on fox 20 and 21 before this review.");
                camera = new GameObject("Temporary ecology observation camera").AddComponent<Camera>();
                camera.CopyFrom(Camera.main); camera.gameObject.AddComponent<UniversalAdditionalCameraData>().requiresDepthTexture = true;
                target = new RenderTexture(1600, 1000, 24); target.Create(); camera.targetTexture = target;
                camera.enabled = true; camera.aspect = 1.6f; camera.nearClipPlane = .1f; camera.farClipPlane = 240;
                herd.social.viewCamera = camera;
                Vector3[] positions = herd.members.Where(m => m != null && m.root && m.root.gameObject.activeInHierarchy).Select(m => m.root.position).ToArray();
                if (positions.Length == 0) throw new InvalidOperationException("Saved herd has no active members.");
                fixedCenter = positions.Aggregate(Vector3.zero, (sum, point) => sum + point) / positions.Length + Vector3.up;
                Quaternion rotation = Quaternion.Euler(30f, 35f, 0f);
                camera.orthographic = true; camera.orthographicSize = 6f;
                camera.transform.SetPositionAndRotation(fixedCenter + rotation * Vector3.back * 27f, rotation);
                File.AppendAllText(folder + "/summary.txt", string.Format(CultureInfo.InvariantCulture,
                    "PLAYER_INITIAL camera={0} position={1} rotation={2} orthographic={3} size={4:F2} fov={5:F2}\nFIXED_VIEW time=0..45 center={6} position={7} pitch=30 yaw=35 size=6 distance=27; no pair-following\n",
                    Camera.main.name, Camera.main.transform.position.ToString("F3"), Camera.main.transform.eulerAngles.ToString("F3"),
                    Camera.main.orthographic, Camera.main.orthographicSize, Camera.main.fieldOfView, fixedCenter.ToString("F3"), camera.transform.position.ToString("F3")));
                previousTime = herd.SimulationTime;
                return; // Let real LateUpdate project the words into this camera before any visibility measurement or screenshot.
            }
            if (!herd || !population) throw new InvalidOperationException("The observed animal controllers were destroyed during the review.");
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            frames++;
            float time = herd.SimulationTime;
            string phase = cameraPhase;
            // Read the display from the previous real LateUpdate before moving the observation camera for the next frame.
            JourneyConversation turn = herd.social.ActiveConversation;
            bool visible = turn != null && herd.social.VisibleBubbleCount == 1 && herd.social.CanPresentFrom(camera);
            float fixedDelta = phase == "fixed_sheep" ? Mathf.Max(0f, Mathf.Min(time, 45f) - previousTime) : 0f;
            if (visible)
            {
                var graphic = herd.social.GetComponentsInChildren<ProceduralUiGraphic>().FirstOrDefault(g => g.mark == UiMark.AnimalBubble);
                if (!graphic) throw new InvalidOperationException("Visible speech did not use the game's AnimalBubble component.");
                speakers.Add(turn.speakerId);
                if (fixedDelta > 0f)
                {
                    float displayed = Mathf.Min(fixedDelta, Mathf.Max(0f, time - turn.startedAt));
                    fixedVisibleSeconds += displayed; fixedVisibleRun += displayed;
                    fixedLongestVisibleRun = Mathf.Max(fixedLongestVisibleRun, fixedVisibleRun); fixedSpeakers.Add(turn.speakerId);
                }
                string key = turn.sequenceId + "-" + turn.turnIndex;
                if (chatShots < 12 && capturedTurns.Add(key))
                {
                    if (time < 45f) fixedChatShots++;
                    Capture(phase + "_conversation_" + (++chatShots).ToString("00"));
                    File.AppendAllText(folder + "/conversations.txt", string.Format(CultureInfo.InvariantCulture,
                        "time={0:F2} phase={1} speaker={2} listener={3} glyph={4} observed={5} ui={6}/{7}\n",
                        time, phase, turn.speakerId, turn.listenerId, string.Join("+", turn.glyphIds), turn.observed, graphic.GetType().Name, graphic.mark));
                }
            }
            else fixedVisibleRun = 0f;
            previousTime = time;
            Vector3 view = camera.transform.position;
            File.AppendAllText(folder + "/visibility.csv", string.Format(CultureInfo.InvariantCulture,
                "{0:F3},{1},{2},{3},{4},{5},{6:F3},{7:F3},{8:F3},{9:F3},{10:F3}\n", time, phase,
                turn == null ? -1 : turn.speakerId, turn == null ? -1 : turn.listenerId, visible,
                turn != null && turn.observed, fixedVisibleSeconds, view.x, view.y, view.z, camera.orthographicSize));

            if (time < 45f)
            {
                if (time >= nextFrame && fixedShots < 9)
                { Capture("fixed_sheep_" + (++fixedShots).ToString("00")); nextFrame = time + 5f; }
            }
            else if (time < 105f)
            {
                var walking = herd.members.Where(m => m != null && m.root && !m.Arrived && m.Mode == JourneyTravelMode.Walk).ToArray();
                if (cameraPhase == "moving_herd" && time >= nextFrame && formationFrames < 30 && walking.Length > 2)
                { Capture("formation_" + (++formationFrames).ToString("00")); nextFrame = time + 1.5f; }
                // Follow the moving group, never the current conversational pair.
                Focus(walking.Length > 2 ? walking.Select(m => m.root.position) : herd.members.Where(m => m != null && m.root).Select(m => m.root.position), 6f);
                cameraPhase = "moving_herd";
            }
            else
            {
                int index = time < 157.5f ? 0 : Mathf.Min(1, foxes.Length - 1);
                if (!foxes[index]) throw new InvalidOperationException("A selected fox disappeared during observation.");
                if (cameraPhase == "fox" && focusedFoxId == foxes[index].spawnId && time >= nextFrame && foxFrames < 42)
                { Capture("fox_" + foxes[index].spawnId + "_" + (++foxFrames).ToString("00")); nextFrame = time + 2.5f; }
                Focus(new[] { foxes[index].transform.position }, 3.5f);
                cameraPhase = "fox"; focusedFoxId = foxes[index].spawnId;
            }
            if (time >= nextTrace)
            {
                nextTrace = time + .5f;
                foreach (var member in herd.members) Trace(time, "sheep", member.id, member.State.ToString(), member.root.position, member.ActualSpeed, member.Mode + " " + member.LastDecision);
                foreach (var fox in foxes)
                {
                    string detail = DescribeRoutine(fox);
                    foxActivities.Add(fox.spawnId + ":" + detail.Split(' ')[0]);
                    Trace(time, "fox", fox.spawnId, fox.State.ToString(), fox.transform.position, fox.ActualSpeed, detail);
                }
            }
            if (time >= 210f) Finish(fixedVisibleSeconds >= 2f && fixedSpeakers.Count >= 2 && fixedChatShots >= 2 && formationFrames > 0 && foxFrames > 0 ? "CAPTURED" : "FAIL",
                "Fixed player-scale view must show at least two speakers/turns and two seconds of the existing animal UI; inspect motion and screenshots before visual acceptance.");
        }

        static string DescribeRoutine(WildlifeAgent fox)
        {
            WildlifeSolitaryRoutine routine = fox.SolitaryRoutine;
            if (routine == null) return "unavailable";
            return string.Format(CultureInfo.InvariantCulture,
                "{0} working={1} hunger={2:F3} thirst={3:F3} fatigue={4:F3} target={5} home={6} failures={7} returning={8} decisions={9} alarms={10} playerDistance={11:F3} playerSpeed={12:F3}",
                routine.Activity, routine.IsWorking, routine.Hunger, routine.Thirst, routine.Fatigue,
                routine.Target.ToString("F3"), routine.Home.ToString("F3"), routine.FailureCount,
                routine.ReturningHome, routine.DecisionCount, fox.AcceptedAlarmCount,
                population.player ? Vector3.Distance(fox.transform.position, population.player.position) : -1f, population.PlayerSpeed);
        }

        static void Focus(IEnumerable<Vector3> positions, float minimumSize)
        {
            var values = positions.ToArray(); if (values.Length == 0) return;
            var bounds = new Bounds(values[0] + Vector3.up, Vector3.one * 2);
            foreach (var value in values) bounds.Encapsulate(value + Vector3.up);
            Quaternion rotation = Quaternion.Euler(40, 35, 0);
            camera.orthographic = true; camera.orthographicSize = Mathf.Max(minimumSize, bounds.extents.magnitude * .68f);
            camera.transform.SetPositionAndRotation(bounds.center + rotation * Vector3.back * 27f, rotation);
        }

        static void Capture(string name)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var behaviour in Object.FindObjectsOfType<MonoBehaviour>())
                if (behaviour.GetType().Name == "HillsideGrassRenderer")
                    behaviour.GetType().GetMethod("DrawForCamera")?.Invoke(behaviour, new object[] { camera, true });
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                File.WriteAllBytes(folder + "/" + name + ".png", pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
        }
        static void Trace(float time, string kind, int id, string state, Vector3 p, float speed, string detail)
        {
            File.AppendAllText(folder + "/trajectory.csv", string.Format(CultureInfo.InvariantCulture,
                "{0:F3},{1},{2},{3},{4:F3},{5:F3},{6:F3},{7:F3},\"{8}\"\n", time, kind, id, state, p.x, p.y, p.z, speed, detail.Replace("\"", "\"\"")));
        }
        static void Finish(string status, string reason, bool stopPlay = true)
        {
            SessionState.SetBool(Key + "running", false); SessionState.SetBool(Key + "cleanup", true);
            SessionState.SetBool(Key + "entered", false);
            if (string.IsNullOrEmpty(folder)) folder = SessionState.GetString(Key + "folder", "");
            try
            {
                File.AppendAllText(folder + "/summary.txt", string.Format(CultureInfo.InvariantCulture,
                    "{0}\n{1}\ntime={2:F2} sampledFrames={3} conversations={4} speakers={5} formationFrames={6} foxFrames={7}\nfixedViewVisibleSeconds={8:F3} fixedViewLongestVisibleSeconds={9:F3} fixedViewSpeakers={10} fixedViewConversationShots={11}\nactivities={12}\n",
                    status, reason, herd ? herd.SimulationTime : 0f, frames, chatShots, string.Join(",", speakers), formationFrames, foxFrames,
                    fixedVisibleSeconds, fixedLongestVisibleRun, string.Join(",", fixedSpeakers), fixedChatShots, string.Join(",", foxActivities)));
            }
            finally
            {
                // Also recover a temporary camera after a domain reload so its render texture is not leaked.
                if (!camera) camera = Object.FindObjectsOfType<Camera>().FirstOrDefault(c => c.name == "Temporary ecology observation camera");
                if (!target && camera) target = camera.targetTexture;
                if (camera) camera.targetTexture = null;
                if (target) { target.Release(); Object.DestroyImmediate(target); target = null; }
                if (camera) { Object.DestroyImmediate(camera.gameObject); camera = null; }
                if (stopPlay && EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
            }
        }
    }
}
