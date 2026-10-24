using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using ForestSpriteCharacter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WeatherHouse.Vegetation;
using WeatherHouse.Hillside;
using WeatherHouse.WildlifeJourney;
using WeatherHouse.WatersideV2;
using Object = UnityEngine.Object;

namespace WeatherHousePrototype.Editor
{
    [InitializeOnLoad]
    public static class WatersideGameplayPlayReview
    {
        const string Key = "Waterside.GameplayUiReview.", DefaultFolder = "WatersideV3Review/gameplay-ui/play";
        static string Folder => SessionState.GetString(Key + "folder", DefaultFolder);
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static GameplaySession session;
        static GameplayHud hud;
        static ForestSpriteDemoController player;
        static VegetationTopDownCamera orbit;
        static Report report;
        static int stage, routeAttempt;
        static float next, enteredAt;
        static Vector3 blockedPosition;
        static Dictionary<Renderer, bool> renderers, forcedOff;
        static string orbitBefore;
        static Dictionary<HillsideGrassRenderer, Camera> grassCameras;
        static Camera movementCameraBefore;
        static Dictionary<JourneySocial, Camera> socialCameras;
        static Vector3 photoForward;
        static AnimalActor dialogueAnimal;
        static bool Active => SessionState.GetBool(Key + "active", false);
        static bool Ending => SessionState.GetBool(Key + "ending", false);

        [Serializable] sealed class Report
        {
            public string status = "RUNNING", error = "", scene, sceneHash, profilePath, profileHash, profileTempHash, startedUtc, finishedUtc;
            public string inputEvidence = "Calls the same ToggleDialogue action used by GameplayHud's E branch, plus production C/J/Esc actions. No claim of hardware-key injection.";
            public bool returnedToEdit, sceneDirtyAfter, sceneFileUnchanged, notebookRestored, editorOptionsRestored, screenshotsWritten;
            public float blockedHorizontalMotion, restoredHorizontalMotion, photoHorizontalMotion, photoDirectionAgreement;
            public string observed;
            public List<string> checks = new List<string>();
        }

        static WatersideGameplayPlayReview()
        {
            EditorApplication.playModeStateChanged += Changed;
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
        }

        public static void Begin()
        {
            if (Active || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start UI review from Edit mode.");
            WatersideGameplayInstaller.CheckBindings();
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path) || Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Save scene edits before the disposable UI review.");
            if (EditorSettings.enterPlayModeOptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0)
                throw new InvalidOperationException("UI review requires ordinary scene reload; project setting was not changed.");
            SessionState.SetString(Key + "folder", DefaultFolder + "/runs/" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(Folder);
            string profile = Object.FindObjectOfType<GameplaySession>().SavePath;
            report = new Report { scene = scene.path, sceneHash = Hash(scene.path), profilePath = profile,
                profileHash = Hash(profile), profileTempHash = Hash(profile + ".tmp"), startedUtc = DateTime.UtcNow.ToString("O") };
            Backup(profile, "notebook"); Backup(profile + ".tmp", "notebook-temp");
            SessionState.SetString(Key + "startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Key + "background", Application.runInBackground); SessionState.SetBool(Key + "paused", EditorApplication.isPaused);
            SessionState.SetString(Key + "ticks", DateTime.UtcNow.Ticks.ToString()); SessionState.SetString(Key + "runtimeError", "");
            SessionState.SetBool(Key + "ending", false); SessionState.SetBool(Key + "success", false);
            SessionState.SetBool(Key + "editConfirmed", true); SessionState.SetBool(Key + "restoreBlocked", false);
            SessionState.SetString(Key + "checkpoint", "");
            Save(); WriteReportOnce("running.json"); SessionState.SetBool(Key + "active", true);
            try
            {
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path);
            Application.runInBackground = true; EditorApplication.isPaused = false;
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            SessionState.SetBool(Key + "editConfirmed", false); EditorApplication.isPlaying = true;
            } catch (Exception error)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling)
                    SessionState.SetBool(Key + "editConfirmed", true);
                Finish(error);
            }
        }

        static void Changed(PlayModeStateChange change)
        {
            if (!Active) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                SessionState.SetBool(Key + "editConfirmed", false);
                if (Ending) { EditorApplication.isPlaying = false; return; }
                try { report = Load(); stage = 0; routeAttempt = 0; enteredAt = Time.unscaledTime; next = enteredAt + .6f; }
                catch (Exception error) { Finish(error); }
            }
            else if (change == PlayModeStateChange.ExitingPlayMode) Cleanup();
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(Key + "editConfirmed", true);
                if (!Ending) Finish(new OperationCanceledException("Play exited before the UI sequence completed."));
                else Restore();
            }
        }

        static void Tick()
        {
            if (!Active) return;
            if (Ending)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || !SessionState.GetBool(Key + "editConfirmed", false)) EditorApplication.isPlaying = false;
                else if (!SessionState.GetBool(Key + "restoreBlocked", false)) Restore();
                return;
            }
            if (!long.TryParse(SessionState.GetString(Key + "ticks", ""), out long ticks) || (DateTime.UtcNow.Ticks - ticks) / (double)TimeSpan.TicksPerSecond > 120)
            { Finish(new TimeoutException("UI review exceeded 120 seconds.")); return; }
            string logged = SessionState.GetString(Key + "runtimeError", "");
            if (!string.IsNullOrEmpty(logged)) { Finish(new InvalidOperationException(logged)); return; }
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            EditorApplication.QueuePlayerLoopUpdate();
            if (Time.unscaledTime < next) return;
            try
            {
                switch (stage)
                {
                    case 0:
                        session = Object.FindObjectOfType<GameplaySession>(); hud = session.GetComponent<GameplayHud>(); player = session.forestSprite;
                        orbit = session.PlayerViewCamera.GetComponent<VegetationTopDownCamera>(); orbitBefore = JsonUtility.ToJson(orbit);
                        Require(session && hud && hud.isActiveAndEnabled && player && orbit, "Original gameplay UI and the existing ForestSprite are active.");
                        Require(session.GetComponentsInChildren<Canvas>().Any(c => c.name == "Weather House HUD" && c.enabled), "Original screen-space HUD has started.");
                        Require(session.GetComponentsInChildren<Text>().Any(t => t.text == "C 拍照") && session.GetComponentsInChildren<Text>().Any(t => t.text == "J 手账"), "Original C/J controls are visible.");
                        if (!session.Observed) PlaceNearAnimal();
                        stage = 1; next = Time.unscaledTime + .5f; break;
                    case 1:
                        if (!session.Observed)
                        {
                            if (routeAttempt >= 12) throw new InvalidOperationException("No actual visible nearby animal could be observed; UI observation has not passed.");
                            PlaceNearAnimal(); next = Time.unscaledTime + .5f; break;
                        }
                        report.observed = session.Observed.animalId; Capture("01_hud_observed");
                        stage = 10; next = Time.unscaledTime + .3f; break;
                    case 10:
                        dialogueAnimal = session.Observed;
                        hud.ToggleDialogue(); session.RefreshInputBlocking();
                        Require(session.GetComponentsInChildren<InputField>(true).Any(i => i.gameObject.activeInHierarchy), "E action opened the original reply InputField.");
                        Require(hud.BlocksMovement && player.InputBlocked && orbit.InputBlocked, "Dialogue locks movement and orbit input.");
                        blockedPosition = player.transform.position; player.SetScriptedInput(Vector2.down, false, false);
                        stage = 2; next = Time.unscaledTime + .4f; break;
                    case 2:
                        report.blockedHorizontalMotion = Flat(player.transform.position - blockedPosition);
                        Require(report.blockedHorizontalMotion < .025f, "A real requested movement is blocked while the reply bubble is open.");
                        Require(session.Observed == dialogueAnimal && hud.DialogueTarget == dialogueAnimal, "The E conversation retains its original animal while typing.");
                        Capture("02_original_reply_bubble");
                        stage = 11; next = Time.unscaledTime + .3f; break;
                    case 11:
                        CloseTop(); session.RefreshInputBlocking();
                        Require(!hud.BlocksMovement && !player.InputBlocked && !orbit.InputBlocked, "Esc action releases movement and orbit immediately.");
                        blockedPosition = player.transform.position;
                        stage = 3; next = Time.unscaledTime + .35f; break;
                    case 3:
                        report.restoredHorizontalMotion = Flat(player.transform.position - blockedPosition);
                        Require(report.restoredHorizontalMotion > .12f, "Real movement resumes after Esc.");
                        player.ReleaseScriptedInput(); hud.ToggleJournal();
                        stage = 4; next = Time.unscaledTime + 1.5f; break;
                    case 4:
                        Require(hud.JournalOpen && player.InputBlocked && orbit.InputBlocked, "J opens the original album and blocks game movement.");
                        Capture("03_original_album");
                        stage = 12; next = Time.unscaledTime + .3f; break;
                    case 12:
                        CloseTop();
                        stage = 5; next = Time.unscaledTime + 1.5f; break;
                    case 5:
                        if (hud.JournalOpen) { if (Time.unscaledTime - enteredAt > 30) throw new TimeoutException("Album close animation did not finish."); next = Time.unscaledTime + .2f; break; }
                        Require(!hud.BlocksMovement && !player.InputBlocked, "Album close restores movement.");
                        renderers = player.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r, r => r.enabled);
                        forcedOff = renderers.Keys.ToDictionary(r => r, r => r.forceRenderingOff);
                        Require(renderers.Keys.Any(r => r.GetComponent<ForestSpriteCapeCloth>()), "Photo visibility check independently includes the actual Cape Cloth sibling under the player root.");
                        grassCameras = Object.FindObjectsOfType<HillsideGrassRenderer>(true).Where(g => g.gameObject.scene == session.gameObject.scene).ToDictionary(g => g, g => g.viewCamera);
                        socialCameras = Object.FindObjectsOfType<JourneySocial>(true).Where(g => g.gameObject.scene == session.gameObject.scene).ToDictionary(g => g, g => g.viewCamera);
                        movementCameraBefore = player.MovementCameraOverride;
                        hud.SetCameraMode(true);
                        Require(grassCameras.Count > 0 && grassCameras.Keys.All(g => g.viewCamera == session.PhotoRig.CaptureCamera), "Photo camera receives every same-scene grass renderer including local overlays.");
                        Require(socialCameras.Count > 0 && socialCameras.Keys.All(g => g.CurrentViewCamera == session.PhotoRig.CaptureCamera), "Existing social bubbles resolve the active photo camera.");
                        Require(session.PhotoRig.Active && session.CurrentView == session.PhotoRig.CaptureCamera && !session.PlayerViewCamera.enabled,
                            "C switches to the existing photo rig using ForestSprite, without adding a legacy walker.");
                        Require(player.JumpBlocked && orbit.InputBlocked && renderers.Keys.All(r => r.forceRenderingOff), "Photo mode hides player visuals and prevents Space from also jumping.");
                        stage = 6; next = Time.unscaledTime + .3f; break;
                    case 6:
                        Require(renderers.All(r => r.Key.forceRenderingOff && r.Key.enabled == r.Value), "All player renderers including Cape Cloth remain suppressed after multiple rendered frames; their enabled state and cloth simulation are untouched.");
                        Require(Mathf.Abs(Mathf.DeltaAngle(0, session.CurrentView.transform.eulerAngles.x)) <= 15.01f,
                            "Photo entry uses the existing character's forward viewing pitch, not the top-down gameplay pitch.");
                        Capture("04_original_photo_mode");
                        stage = 13; next = Time.unscaledTime + .3f; break;
                    case 13:
                        var yaw = typeof(PhotoCameraRig).GetField("photoYaw", Private);
                        yaw.SetValue(session.PhotoRig, (float)yaw.GetValue(session.PhotoRig) + 90f);
                        typeof(PhotoCameraRig).GetMethod("UpdatePose", Private).Invoke(session.PhotoRig, null);
                        photoForward = Vector3.ProjectOnPlane(session.CurrentView.transform.forward, Vector3.up).normalized;
                        Require(player.MovementCameraOverride == session.CurrentView, "Movement uses the actual rotated photo camera.");
                        blockedPosition = player.transform.position; player.SetScriptedInput(Vector2.up, false, false);
                        stage = 14; next = Time.unscaledTime + .25f; break;
                    case 14:
                        player.ReleaseScriptedInput();
                        Vector3 photoDelta = Vector3.ProjectOnPlane(player.transform.position - blockedPosition, Vector3.up);
                        report.photoHorizontalMotion = photoDelta.magnitude; report.photoDirectionAgreement = Vector3.Dot(photoDelta.normalized, photoForward);
                        Require(report.photoHorizontalMotion > .08f && report.photoDirectionAgreement > .8f, "Real forward movement follows the photo camera after a 90 degree turn.");
                        hud.SetCameraMode(false);
                        Require(!session.PhotoRig.Active && session.PlayerViewCamera.enabled && !player.JumpBlocked && !orbit.InputBlocked, "Photo exit restores gameplay camera and input.");
                        Require(player.MovementCameraOverride == movementCameraBefore && grassCameras.All(g => g.Key && g.Key.viewCamera == g.Value), "Photo exit restores the prior movement reference and every grass camera.");
                        Require(socialCameras.All(g => g.Key && g.Key.viewCamera == g.Value), "Photo exit restores every social bubble camera.");
                        Require(renderers.All(p => p.Key && p.Key.enabled == p.Value) && forcedOff.All(p => p.Key && p.Key.forceRenderingOff == p.Value), "Every player renderer enabled and forceRenderingOff state is restored.");
                        Require(JsonUtility.ToJson(orbit) == orbitBefore, "Orbit authoring settings are unchanged.");
                        Require(Object.FindObjectsOfType<ForestSpriteDemoController>().Length == 1 && Object.FindObjectsOfType<PrototypeWalker>().Length == 0,
                            "One existing player remains; no second movement controller was added.");
                        stage = 15; next = Time.unscaledTime + .3f; break;
                    case 15:
                        if (!session.Observed) { PlaceNearAnimal(); next = Time.unscaledTime + .4f; break; }
                        hud.ToggleDialogue(); Require(hud.IsDialogueOpen, "Reply is open before disabling HUD.");
                        hud.enabled = false;
                        Require(!player.InputBlocked && !orbit.InputBlocked && !hud.BlocksMovement && !session.PhotoRig.Active, "Disabling HUD closes dialogue and releases gameplay input.");
                        Require(!session.GetComponentsInChildren<Canvas>().Single(c => c.name == "Weather House HUD").enabled, "Disabled HUD is not left visible or interactive.");
                        hud.enabled = true;
                        hud.ToggleJournal(); hud.enabled = false;
                        Require(!hud.JournalOpen && !player.InputBlocked && !orbit.InputBlocked, "Disabling HUD during album opening also releases input.");
                        hud.enabled = true; hud.SetCameraMode(true); hud.enabled = false;
                        Require(!session.PhotoRig.Active && session.PlayerViewCamera.enabled && !player.JumpBlocked && grassCameras.All(g => g.Key.viewCamera == g.Value), "Disabling HUD in photo mode restores camera, grass and jump.");
                        hud.enabled = true;
                        Finish(null); break;
                }
                if (!Ending) Save();
            }
            catch (Exception error) { Finish(error); }
        }

        static void PlaceNearAnimal()
        {
            var terrain = Terrain.activeTerrain; var body = player.GetComponent<CharacterController>();
            var boundary = player.GetComponent<WatersideWaterBoundary>();
            var animals = session.animals.Where(a => a && a.IsAvailableForObservation).OrderBy(a => (a.transform.position - player.transform.position).sqrMagnitude).ToArray();
            Require(terrain && body && animals.Length > 0, "Actual terrain, player body and observable ecology animals exist.");
            for (; routeAttempt < 12; routeAttempt++)
            {
                var animal = animals[(routeAttempt / 4) % animals.Length];
                Vector3 forward = Quaternion.Euler(0, orbit.Yaw + (routeAttempt % 4 - 1) * 25f, 0) * Vector3.forward;
                Vector3 p = animal.transform.position - forward * 3.2f;
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y + .04f;
                if (boundary && boundary.SampleSupportedDepth(p, p.y) > .1f) continue;
                float radius = body.radius * Mathf.Abs(player.transform.lossyScale.x) + .03f;
                float height = body.height * Mathf.Abs(player.transform.lossyScale.y);
                if (Physics.OverlapCapsule(p + Vector3.up * (radius + .06f), p + Vector3.up * Mathf.Max(radius + .06f, height - radius), radius,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).Any(c => !(c is TerrainCollider) && c != body && !c.transform.IsChildOf(player.transform))) continue;
                player.ResetPosition(); body.enabled = false; player.transform.position = p; body.enabled = true;
                Physics.SyncTransforms(); orbit.SnapToPlayer(); routeAttempt++; return;
            }
            throw new InvalidOperationException("No clear dry player position beside an existing animal; observation test cannot proceed safely.");
        }

        static void CloseTop() => typeof(GameplayHud).GetMethod("CloseTop", Private).Invoke(hud, null);
        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
        static void Capture(string name) { ScreenCapture.CaptureScreenshot(Path.Combine(Folder, name + ".png")); }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); report.checks.Add(message); }
        static void OnLog(string message, string stack, LogType type)
        { if (Active && !Ending && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) SessionState.SetString(Key + "runtimeError", message + "\n" + stack); }
        static void Cleanup()
        { if (player) player.ReleaseScriptedInput(); if (hud && hud.CameraMode) hud.SetCameraMode(false); }
        static void Finish(Exception error)
        {
            if (!Active || Ending) return;
            SessionState.SetBool(Key + "ending", true); SessionState.SetBool(Key + "success", error == null);
            SessionState.SetString(Key + "runtimeError", error?.ToString() ?? "");
            try
            {
                if (report == null) report = Load(); report.error = error?.ToString() ?? "";
                report.status = "RETURNING_TO_EDIT";
                Cleanup(); Save();
            }
            catch (Exception finishError)
            {
                SessionState.SetBool(Key + "success", false);
                SessionState.SetString(Key + "runtimeError", SessionState.GetString(Key + "runtimeError", "") + "\n" + finishError);
                Debug.LogError("UI review cleanup/report failed; Play exit is still requested. " + finishError);
            }
            finally
            {
                EditorApplication.isPaused = false;
                if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false; else Restore();
            }
        }
        static void Restore()
        {
            if (!Active || SessionState.GetBool(Key + "restoreBlocked", false)) return;
            SessionState.SetBool(Key + "ending", true);
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !SessionState.GetBool(Key + "editConfirmed", false)) return;
            Exception failure = null;
            try
            {
                if (report == null) report = Load();
                string retainedError = SessionState.GetString(Key + "runtimeError", "");
                if (!string.IsNullOrEmpty(retainedError)) report.error = retainedError;
                ValidateBackup("notebook", report.profileHash); ValidateBackup("notebook-temp", report.profileTempHash);
                RestoreFile(report.profilePath, "notebook"); RestoreFile(report.profilePath + ".tmp", "notebook-temp");
                report.notebookRestored = Hash(report.profilePath) == report.profileHash && Hash(report.profilePath + ".tmp") == report.profileTempHash;
                if (!report.notebookRestored) throw new IOException("Restored notebook bytes do not match the original backup hashes.");
            }
            catch (Exception error) { failure = error; }
            finally
            {
                try
                {
                    Application.runInBackground = SessionState.GetBool(Key + "background", false); EditorApplication.isPaused = SessionState.GetBool(Key + "paused", false);
                    string original = SessionState.GetString(Key + "startScene", "");
                    EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(original) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(original);
                    bool restored = Application.runInBackground == SessionState.GetBool(Key + "background", false) &&
                        EditorApplication.isPaused == SessionState.GetBool(Key + "paused", false) && AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == original;
                    if (report != null) report.editorOptionsRestored = restored;
                    if (!restored) throw new InvalidOperationException("UI review editor options did not restore.");
                }
                catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            }
            try
            {
                if (failure != null) throw failure;
                report.returnedToEdit = !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating;
                if (!report.returnedToEdit) throw new InvalidOperationException("UI review must remain in confirmed Edit mode before cleanup can complete.");
                report.sceneDirtyAfter = Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty);
                report.sceneFileUnchanged = Hash(report.scene) == report.sceneHash;
                report.screenshotsWritten = new[] { "01_hud_observed", "02_original_reply_bubble", "03_original_album", "04_original_photo_mode" }.All(n => File.Exists(Path.Combine(Folder, n + ".png")) && new FileInfo(Path.Combine(Folder, n + ".png")).Length > 1024 && File.GetLastWriteTimeUtc(Path.Combine(Folder, n + ".png")) >= DateTime.Parse(report.startedUtc).ToUniversalTime());
                report.status = SessionState.GetBool(Key + "success", false) && report.returnedToEdit && !report.sceneDirtyAfter && report.sceneFileUnchanged && report.notebookRestored && report.editorOptionsRestored && report.screenshotsWritten ? "PASS" : "FAIL";
                report.finishedUtc = DateTime.UtcNow.ToString("O"); Save(); WriteReportOnce("checks.json");
                SessionState.SetBool(Key + "active", false); SessionState.SetBool(Key + "ending", false);
            }
            catch (Exception error)
            {
                SessionState.SetBool(Key + "restoreBlocked", true);
                if (report != null)
                {
                    report.status = "RESTORE_FAILED"; report.error += "\n" + error;
                    try { Save(); WriteReportOnce("restore-error-" + Guid.NewGuid().ToString("N") + ".json"); }
                    catch (Exception writeError) { Debug.LogWarning("Could not save UI restoration failure report: " + writeError.Message); }
                }
                Debug.LogError("UI review restoration is pending; original backups are retained in " + Folder +
                    ". Resolve the file error, then call WatersideGameplayPlayReview.RetryRestore from Edit mode. " + error);
            }
        }
        [MenuItem("Weather House/Waterside V2/Retry UI Review Restoration")]
        public static void RetryRestore()
        {
            if (!Active || !Ending || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Retry restoration only for a pending UI review in stable Edit mode.");
            SessionState.SetBool(Key + "editConfirmed", true); SessionState.SetBool(Key + "restoreBlocked", false);
            Restore();
        }
        static void ValidateBackup(string name, string expectedHash)
        {
            bool existed = SessionState.GetBool(Key + name + ".existed", false);
            if (existed ? expectedHash == "<missing>" || Hash(Path.Combine(Folder, name + ".before")) != expectedHash : expectedHash != "<missing>")
                throw new IOException("Original " + name + " backup is missing or does not match its captured hash; live files were not restored.");
        }
        static void Backup(string path, string name)
        { SessionState.SetBool(Key + name + ".existed", File.Exists(path)); if (File.Exists(path)) File.Copy(path, Path.Combine(Folder, name + ".before"), true); }
        static void RestoreFile(string path, string name)
        { if (SessionState.GetBool(Key + name + ".existed", false)) File.Copy(Path.Combine(Folder, name + ".before"), path, true); else if (File.Exists(path)) File.Delete(path); }
        static string Hash(string path) { if (!File.Exists(path)) return "<missing>"; using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))); }
        static Report Load()
        {
            string checkpoint = SessionState.GetString(Key + "checkpoint", "");
            if (string.IsNullOrEmpty(checkpoint))
                checkpoint = File.ReadAllText(Path.Combine(Folder, File.Exists(Path.Combine(Folder, "checks.json")) ? "checks.json" : "running.json"));
            return JsonUtility.FromJson<Report>(checkpoint);
        }
        static void Save() => SessionState.SetString(Key + "checkpoint", JsonUtility.ToJson(report, true));
        static void WriteReportOnce(string name)
        {
            string path = Path.Combine(Folder, name), pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(pending, JsonUtility.ToJson(report, true));
            File.Move(pending, path);
        }
    }
}
