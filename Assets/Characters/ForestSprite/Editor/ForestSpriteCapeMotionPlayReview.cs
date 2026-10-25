using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using WeatherHouse.WatersideV2;
using Object = UnityEngine.Object;

namespace ForestSpriteCharacter.Editor
{
    // Disposable Play review: no profile, prefab, scene or mood persistence.
    [InitializeOnLoad]
    public static class ForestSpriteCapeMotionPlayReview
    {
        const string Key = "ForestSprite.CapeMotionPlay.", Folder = "WatersideV3Review/cape-motion-play";
        const float SettleSeconds = .75f, MoveSeconds = 2.4f;
        static readonly FieldInfo Smooth = typeof(ForestSpriteCapeCloth).GetField("smooth", BindingFlags.Instance | BindingFlags.NonPublic);
        struct CompletedClothFrame { }
        static Review report;
        static ForestSpriteDemoController player;
        static ForestSpriteCapeCloth cape;
        static CharacterController body;
        static Camera camera;
        static Vector3 routeStart, routeDirection;
        static string originalSettings;
        static int stage, lastFrame = -1, captureIndex;
        static float elapsed, playStart;
        static bool moving, setup, installed;
        static Moments guides, cloth, guideFrames, relativeGuides;
        static Matrix4x4 referenceGuideToPlayerMetres;
        static readonly List<Vector3>[] paths = new List<Vector3>[4];
        static bool Active => SessionState.GetBool(Key + "active", false);
        static bool Ending => SessionState.GetBool(Key + "ending", false);

        [Serializable] sealed class Review
        {
            public string status = "RUNNING", error = "", scene, sceneHash, profilePath, profileHash, startedUtc, finishedUtc, editSettings;
            public string evidence = "Actual guide tips and native Cloth.vertices after PhysicsSkinnedClothFinishUpdate; coordinates in the moving player's local frame. Short matched trajectories include physical lag; these are not offline guide equations.";
            public string cameraView = "Rear top-down view along the matched movement route, preserving the scene camera's pitch and identical player-relative 2.3m orthographic framing in every capture. Mood and lighting are untouched.";
            public string boundsRepair = "";
            public string relativeGuideEvidence = "Added metrics use metres and the same sampled frames as overall RMS. guideFrameYRms tracks the guide-frame origin relative to the player on the player up axis. guideRelativeXRms/YRms remove the current guide-frame translation and rotation by converting each tip to frame-local coordinates, then project through the frame-to-player linear transform captured after the trial Idle reset. This retains guide deformation and contact response, while excluding subsequent rigid Chest/frame motion; it does not isolate native Cloth.";
            public Vector3 routeStart, routeEnd, editPlayerPosition;
            public Quaternion editPlayerRotation;
            public bool returnedToEdit, sceneDirtyAfter, sceneFileUnchanged, profileUnchanged, restoredStartScene, restoredPlayerAndSettings, restoredEditorOptions;
            public float simulatedSeconds, walkPathMaxDifference, runPathMaxDifference;
            public List<Trial> trials = new List<Trial>();
        }
        [Serializable] sealed class Trial
        {
            public string preset, gait;
            public float waveAmplitude, wingSpread, guideXRms, guideYRms, clothXRms, clothYRms, actualDistance, groundedFraction;
            public float guideFrameYRms, guideRelativeXRms, guideRelativeYRms;
            public int frames, guideTips, clothParticles, groundedFrames;
            public List<string> images = new List<string>();
        }
        sealed class Moments
        {
            readonly double[] x, y, xx, yy;
            int count;
            public Moments(int n) { x = new double[n]; y = new double[n]; xx = new double[n]; yy = new double[n]; }
            public void Add(Vector3[] points)
            {
                if (points.Length != x.Length) throw new InvalidOperationException("Particle count changed during a trial.");
                for (int i = 0; i < points.Length; i++)
                {
                    if (!ForestSpriteCapeMotionReview.Finite(points[i])) throw new InvalidOperationException("Non-finite simulated cape point.");
                    x[i] += points[i].x; y[i] += points[i].y; xx[i] += points[i].x * points[i].x; yy[i] += points[i].y * points[i].y;
                }
                count++;
            }
            public float Rms(bool vertical)
            {
                if (count < 20 || x.Length == 0) throw new InvalidOperationException("Too few completed cloth frames.");
                var sum = vertical ? y : x; var square = vertical ? yy : xx; double variance = 0;
                for (int i = 0; i < x.Length; i++) variance += Math.Max(0, square[i] / count - Math.Pow(sum[i] / count, 2));
                return (float)Math.Sqrt(variance / x.Length);
            }
        }

        static ForestSpriteCapeMotionPlayReview()
        {
            EditorApplication.playModeStateChanged += Changed;
            EditorApplication.update += Watchdog;
            Application.logMessageReceived += OnLog;
        }
        public static void Begin()
        {
            if (Active || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Begin from Edit mode with no active cape review.");
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path) || scene.isDirty || Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Save user edits before the disposable Play review.");
            if (EditorSettings.enterPlayModeOptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0)
                throw new InvalidOperationException("This disposable review requires normal scene reload; it does not change the project setting.");
            var selected = ForestSpriteCapeMotionReview.CurrentCapes().Where(c => c.isActiveAndEnabled).ToArray();
            if (selected.Length != 1) throw new InvalidOperationException("Review requires one active cape/player.");
            Directory.CreateDirectory(Folder);
            report = new Review { scene = scene.path, sceneHash = Hash(scene.path), profilePath = selected[0].SettingsPath,
                profileHash = Hash(selected[0].SettingsPath), startedUtc = DateTime.UtcNow.ToString("O"), editSettings = JsonUtility.ToJson(selected[0].settings) };
            var editPlayer = selected[0].GetComponentInParent<ForestSpriteDemoController>();
            if (!editPlayer) throw new InvalidOperationException("Selected cape has no controller.");
            report.editPlayerPosition = editPlayer.transform.position; report.editPlayerRotation = editPlayer.transform.rotation;
            SessionState.SetString(Key + "startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetString(Key + "ticks", DateTime.UtcNow.Ticks.ToString());
            SessionState.SetBool(Key + "background", Application.runInBackground); SessionState.SetBool(Key + "pause", EditorApplication.isPaused);
            SessionState.SetInt(Key + "captureRate", Time.captureFramerate); SessionState.SetString(Key + "runtimeError", "");
            SessionState.SetBool(Key + "active", true); SessionState.SetBool(Key + "ending", false);
            SessionState.SetBool(Key + "entered", false); SessionState.SetBool(Key + "success", false);
            Save();
            try
            {
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path);
                Application.runInBackground = true; EditorApplication.isPaused = false; EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(error); }
        }
        static void Changed(PlayModeStateChange state)
        {
            if (!Active) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                report = Load(); setup = false; lastFrame = -1; playStart = Time.time;
                SessionState.SetBool(Key + "entered", true);
                var loop = PlayerLoop.GetCurrentPlayerLoop(); Remove(ref loop);
                if (!Insert(ref loop)) { Finish(new InvalidOperationException("Native Cloth completion stage was not found.")); return; }
                PlayerLoop.SetPlayerLoop(loop); installed = true;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode) Cleanup();
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                report = Load();
                if (!Ending) { report.error = "Play was stopped before sampling completed."; SessionState.SetBool(Key + "success", false); }
                RestoreEditor();
            }
        }
        static void Watchdog()
        {
            if (!Active) return;
            if (Ending) { if (!EditorApplication.isPlayingOrWillChangePlaymode) RestoreEditor(); return; }
            if (!long.TryParse(SessionState.GetString(Key + "ticks", ""), out long ticks) || (DateTime.UtcNow.Ticks - ticks) / (double)TimeSpan.TicksPerSecond > 150)
            { Finish(new TimeoutException("Cape review exceeded 150 wall-clock seconds.")); return; }
            string error = SessionState.GetString(Key + "runtimeError", "");
            if (!string.IsNullOrEmpty(error)) { Finish(new InvalidOperationException(error)); return; }
            if (EditorApplication.isPlaying) EditorApplication.QueuePlayerLoopUpdate();
        }
        static void Sample()
        {
            if (!Active || Ending || !EditorApplication.isPlaying || EditorApplication.isPaused || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            try
            {
                // Start() loads the user's saved profile before we snapshot or apply either trial.
                if (!setup) { if (Time.time - playStart >= .3f) Setup(); return; }
                report.simulatedSeconds = Time.time - playStart;
                elapsed += Time.deltaTime;
                if (!moving)
                {
                    if (elapsed < SettleSeconds) return;
                    if (!player.IsGrounded) throw new InvalidOperationException("Player did not settle on the dry route.");
                    elapsed = 0; moving = true; player.SetScriptedInput(InputDirection(), stage % 2 == 1, false); return;
                }
                var trial = report.trials[stage]; trial.frames++; if (player.IsGrounded) trial.groundedFrames++;
                paths[stage].Add(player.transform.position);
                player.SetScriptedInput(InputDirection(), stage % 2 == 1, false);
                if (elapsed >= .6f)
                {
                    var tips = Enumerable.Range(0, cape.GuideDirectionCount).Select(d => player.transform.InverseTransformPoint(cape.guideNodes[d * 7 + 6].position)).ToArray();
                    var particles = cape.cloth.vertices.Select(v => player.transform.InverseTransformPoint(cape.cloth.transform.TransformPoint(v))).ToArray();
                    guides.Add(tips); cloth.Add(particles);
                    Vector3 frameOffsetMetres = Quaternion.Inverse(player.transform.rotation) * (cape.guideFrame.position - player.transform.position);
                    guideFrames.Add(new[] { frameOffsetMetres });
                    var relativeTips = Enumerable.Range(0, cape.GuideDirectionCount).Select(d =>
                        referenceGuideToPlayerMetres.MultiplyVector(cape.guideFrame.InverseTransformPoint(cape.guideNodes[d * 7 + 6].position))).ToArray();
                    relativeGuides.Add(relativeTips);
                }
                if (captureIndex < 3 && elapsed >= new[] { .8f, 1.6f, 2.3f }[captureIndex])
                { Capture(trial, captureIndex++); }
                if (elapsed + .0001f < MoveSeconds) return;
                trial.guideXRms = guides.Rms(false); trial.guideYRms = guides.Rms(true);
                trial.clothXRms = cloth.Rms(false); trial.clothYRms = cloth.Rms(true);
                trial.guideFrameYRms = guideFrames.Rms(true);
                trial.guideRelativeXRms = relativeGuides.Rms(false); trial.guideRelativeYRms = relativeGuides.Rms(true);
                trial.actualDistance = Vector3.ProjectOnPlane(player.transform.position - routeStart, Vector3.up).magnitude;
                trial.groundedFraction = trial.groundedFrames / (float)trial.frames;
                float expected = (stage % 2 == 1 ? player.runSpeed : player.walkSpeed) * elapsed;
                if (trial.actualDistance < expected * .9f || trial.groundedFraction < .9f)
                    throw new InvalidOperationException("Route was obstructed or ungrounded; do not interpret this as cape A/B evidence.");
                Save();
                if (stage < 3) BeginTrial(stage + 1);
                else
                {
                    report.walkPathMaxDifference = PathDifference(paths[0], paths[2]); report.runPathMaxDifference = PathDifference(paths[1], paths[3]);
                    if (Mathf.Max(report.walkPathMaxDifference, report.runPathMaxDifference) > .08f)
                        throw new InvalidOperationException("A/B player trajectories differ by more than 8 cm.");
                    Finish(null);
                }
            }
            catch (Exception error) { Finish(error); }
        }
        static void Setup()
        {
            cape = ForestSpriteCapeMotionReview.CurrentCapes().Single(c => c.isActiveAndEnabled);
            player = cape.GetComponentInParent<ForestSpriteDemoController>(); body = player ? player.GetComponent<CharacterController>() : null;
            if (!player || !body || !cape.UsesGuides || !cape.cloth || !cape.cloth.enabled || !cape.MotionEnabled || cape.WindPreview || player.ScriptedInput)
                throw new InvalidOperationException("Need an idle, enabled real controller, guide rig and native Cloth without wind preview or another scripted driver.");
            if (Smooth == null) throw new MissingFieldException("Guide smoothing field changed.");
            originalSettings = JsonUtility.ToJson(cape.settings);
            var validSettings = JsonUtility.FromJson<ForestSpriteCapeCloth.Settings>(originalSettings); validSettings.Clamp();
            if (JsonUtility.ToJson(validSettings) != originalSettings) throw new InvalidOperationException("Original settings would be clamped; inspect them before sampling.");
            var renderer = cape.GetComponent<SkinnedMeshRenderer>();
            if (renderer && !ForestSpriteCapeMotionReview.Finite(renderer.localBounds))
            {
                ForestSpriteCapeMotionReview.RestoreKnownBounds(renderer);
                report.boundsRepair = "Runtime only: restored last tracked finite cape localBounds (center .015848666,.44542745,-.16785541; extents 1.1278195,.967198,.91327393). No prefab change.";
            }
            Physics.SyncTransforms(); FindRoute();
            report.routeStart = routeStart; report.routeEnd = routeStart + routeDirection * (player.runSpeed * MoveSeconds + .5f);
            var main = Camera.main; if (!main) throw new InvalidOperationException("No main camera for the matching top-down view.");
            camera = new GameObject("Cape A-B review camera", typeof(Camera)).GetComponent<Camera>(); camera.CopyFrom(main);
            // Keep the gameplay pitch, but look along the travel direction so the hat does not hide the cape.
            camera.transform.rotation = Quaternion.Euler(main.transform.eulerAngles.x, Quaternion.LookRotation(routeDirection).eulerAngles.y, 0);
            camera.enabled = false; camera.targetTexture = null;
            camera.orthographic = true; camera.orthographicSize = 2.3f; camera.nearClipPlane = .02f;
            var src = main.GetUniversalAdditionalCameraData(); var dst = camera.GetUniversalAdditionalCameraData();
            dst.renderPostProcessing = src.renderPostProcessing; dst.renderShadows = src.renderShadows; dst.volumeLayerMask = src.volumeLayerMask;
            dst.volumeTrigger = src.volumeTrigger; dst.requiresDepthTexture = true;
            Time.captureFramerate = 60; setup = true; BeginTrial(0);
        }
        static void FindRoute()
        {
            var boundary = player.GetComponent<WatersideWaterBoundary>();
            var terrain = boundary ? boundary.terrain : Terrain.activeTerrain;
            if (!terrain) throw new InvalidOperationException("No terrain for a collision-checked dry route.");
            float distance = Mathf.Max(player.walkSpeed, player.runSpeed) * MoveSeconds + .5f;
            Vector3 origin = player.transform.position;
            for (int ring = 0; ring <= 3; ring++)
            for (int a = 0; a < (ring == 0 ? 1 : 12); a++)
            for (int turn = 0; turn < 16; turn++)
            {
                var start = origin + Quaternion.Euler(0, a * 30, 0) * Vector3.forward * (ring * 3);
                var direction = Quaternion.Euler(0, turn * 22.5f, 0) * Vector3.forward;
                bool valid = true; float previous = 0;
                for (int i = 0; i <= Mathf.CeilToInt(distance / .35f); i++)
                {
                    var p = start + direction * Mathf.Min(distance, i * .35f); var local = p - terrain.transform.position;
                    if (local.x < 0 || local.z < 0 || local.x > terrain.terrainData.size.x || local.z > terrain.terrainData.size.z) { valid = false; break; }
                    p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                    if (i > 0 && Mathf.Abs(p.y - previous) > .09f || boundary && boundary.SampleSupportedDepth(p, p.y + .02f) > .02f) { valid = false; break; }
                    float radius = body.radius * Mathf.Abs(player.transform.lossyScale.x) + .03f;
                    float height = body.height * Mathf.Abs(player.transform.lossyScale.y);
                    var hits = Physics.OverlapCapsule(p + Vector3.up * (radius + .06f), p + Vector3.up * Mathf.Max(radius + .06f, height - radius), radius, ~0, QueryTriggerInteraction.Ignore);
                    if (hits.Any(h => h && !(h is TerrainCollider) && h != body && !h.transform.IsChildOf(player.transform))) { valid = false; break; }
                    previous = p.y; if (i == 0) start.y = p.y;
                }
                if (!valid) continue;
                routeStart = start + Vector3.up * .03f; routeDirection = direction; return;
            }
            throw new InvalidOperationException("No unobstructed dry 11m route near the player; move the saved player to an open meadow and retry.");
        }
        static void BeginTrial(int index)
        {
            stage = index; elapsed = 0; captureIndex = 0; moving = false;
            cape.settings = JsonUtility.FromJson<ForestSpriteCapeCloth.Settings>(originalSettings);
            if (stage >= 2) { cape.settings.waveAmplitude = ForestSpriteCapeMotionReview.WaveAmplitude; cape.settings.wingSpread = ForestSpriteCapeMotionReview.WingSpread; }
            Smooth.SetValue(cape, null); cape.ApplySettings();
            player.ResetPosition(); body.enabled = false;
            player.transform.SetPositionAndRotation(routeStart, Quaternion.LookRotation(routeDirection)); body.enabled = true;
            player.SetScriptedInput(Vector2.zero, false, false); player.animator.Play("Idle", 0, 0); player.animator.Update(0);
            cape.ResetSimulation(); Physics.SyncTransforms();
            guides = new Moments(cape.GuideDirectionCount); cloth = new Moments(cape.cloth.vertices.Length); paths[stage] = new List<Vector3>();
            guideFrames = new Moments(1); relativeGuides = new Moments(cape.GuideDirectionCount);
            // Freeze the Idle frame orientation and world scale; MultiplyVector excludes its translation.
            // Do not use player.worldToLocalMatrix here, which would convert metres to scaled player units.
            referenceGuideToPlayerMetres = Matrix4x4.Rotate(Quaternion.Inverse(player.transform.rotation)) * cape.guideFrame.localToWorldMatrix;
            report.trials.Add(new Trial { preset = stage < 2 ? "A-current" : "B-gentle", gait = stage % 2 == 0 ? "Walk" : "Run",
                waveAmplitude = cape.settings.waveAmplitude, wingSpread = cape.settings.wingSpread,
                guideTips = cape.GuideDirectionCount, clothParticles = cape.cloth.vertices.Length });
        }
        static Vector2 InputDirection()
        {
            Vector3 forward = player.demoCamera ? Vector3.ProjectOnPlane(player.demoCamera.transform.forward, Vector3.up).normalized : Vector3.forward;
            return new Vector2(Vector3.Dot(routeDirection, Vector3.Cross(Vector3.up, forward)), Vector3.Dot(routeDirection, forward));
        }
        static void Capture(Trial trial, int index)
        {
            string file = trial.preset + "-" + trial.gait + "-" + (index + 1) + ".png";
            camera.transform.position = player.transform.position + Vector3.up * .65f - camera.transform.forward * 8;
            var target = RenderTexture.GetTemporary(1200, 900, 24); var active = RenderTexture.active;
            var pixels = new Texture2D(1200, 900, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.aspect = 4f / 3; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0); pixels.Apply(); File.WriteAllBytes(Path.Combine(Folder, file), pixels.EncodeToPNG()); trial.images.Add(file);
            }
            finally { camera.targetTexture = null; RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(pixels); }
        }
        static float PathDifference(List<Vector3> a, List<Vector3> b)
        {
            if (a.Count != b.Count) throw new InvalidOperationException("A/B sampled frame counts differ.");
            float result = 0; for (int i = 0; i < a.Count; i++) result = Mathf.Max(result, Vector3.Distance(a[i], b[i])); return result;
        }
        static bool Insert(ref PlayerLoopSystem loop)
        {
            if (loop.subSystemList == null) return false;
            var children = loop.subSystemList.ToList();
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i].type == typeof(UnityEngine.PlayerLoop.PostLateUpdate.PhysicsSkinnedClothFinishUpdate))
                { children.Insert(i + 1, new PlayerLoopSystem { type = typeof(CompletedClothFrame), updateDelegate = Sample }); loop.subSystemList = children.ToArray(); return true; }
                var child = children[i]; if (Insert(ref child)) { children[i] = child; loop.subSystemList = children.ToArray(); return true; }
            }
            return false;
        }
        static void Remove(ref PlayerLoopSystem loop)
        {
            if (loop.subSystemList == null) return;
            var children = loop.subSystemList.Where(s => s.type != typeof(CompletedClothFrame)).ToArray();
            for (int i = 0; i < children.Length; i++) Remove(ref children[i]); loop.subSystemList = children;
        }
        static void OnLog(string message, string stack, LogType type)
        {
            if (Active && !Ending && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                SessionState.SetString(Key + "runtimeError", message + "\n" + stack);
        }
        static void Finish(Exception error)
        {
            if (!Active || Ending) return;
            if (report == null) report = Load();
            report.error = error == null ? "" : error.ToString(); report.status = "RETURNING_TO_EDIT";
            SessionState.SetBool(Key + "ending", true); SessionState.SetBool(Key + "success", error == null);
            try { Cleanup(); Save(); }
            finally { EditorApplication.isPaused = false; if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false; else RestoreEditor(); }
        }
        static void Cleanup()
        {
            if (installed) { var loop = PlayerLoop.GetCurrentPlayerLoop(); Remove(ref loop); PlayerLoop.SetPlayerLoop(loop); installed = false; }
            if (player) player.ReleaseScriptedInput();
            if (cape && originalSettings != null) { cape.settings = JsonUtility.FromJson<ForestSpriteCapeCloth.Settings>(originalSettings); cape.ApplySettings(); }
            if (camera) Object.DestroyImmediate(camera.gameObject);
            Time.captureFramerate = SessionState.GetInt(Key + "captureRate", 0);
            camera = null; cape = null; player = null; body = null; setup = false; originalSettings = null;
        }
        static void RestoreEditor()
        {
            if (!Active) return;
            report = Load();
            try
            {
                Cleanup(); Application.runInBackground = SessionState.GetBool(Key + "background", false); EditorApplication.isPaused = SessionState.GetBool(Key + "pause", false);
                string original = SessionState.GetString(Key + "startScene", "");
                EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(original) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(original);
                report.returnedToEdit = !EditorApplication.isPlaying; report.sceneDirtyAfter = Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty);
                report.sceneFileUnchanged = Hash(report.scene) == report.sceneHash; report.profileUnchanged = Hash(report.profilePath) == report.profileHash;
                report.restoredStartScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == original;
                var restored = ForestSpriteCapeMotionReview.CurrentCapes().Single(c => c.isActiveAndEnabled);
                var restoredPlayer = restored.GetComponentInParent<ForestSpriteDemoController>();
                report.restoredPlayerAndSettings = restoredPlayer && Vector3.Distance(restoredPlayer.transform.position, report.editPlayerPosition) < .00001f &&
                    Quaternion.Angle(restoredPlayer.transform.rotation, report.editPlayerRotation) < .001f && JsonUtility.ToJson(restored.settings) == report.editSettings;
                report.restoredEditorOptions = Time.captureFramerate == SessionState.GetInt(Key + "captureRate", 0) &&
                    Application.runInBackground == SessionState.GetBool(Key + "background", false) && EditorApplication.isPaused == SessionState.GetBool(Key + "pause", false);
                report.status = SessionState.GetBool(Key + "success", false) && report.returnedToEdit && !report.sceneDirtyAfter && report.sceneFileUnchanged && report.profileUnchanged &&
                    report.restoredStartScene && report.restoredPlayerAndSettings && report.restoredEditorOptions ? "PASS" : "FAIL";
                report.finishedUtc = DateTime.UtcNow.ToString("O"); Save();
            }
            finally { SessionState.SetBool(Key + "active", false); SessionState.SetBool(Key + "ending", false); }
        }
        static string Hash(string path) { if (!File.Exists(path)) return "<missing>"; using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))); }
        static Review Load() => JsonUtility.FromJson<Review>(File.ReadAllText(Folder + "/checks.json"));
        static void Save() => File.WriteAllText(Folder + "/checks.json", JsonUtility.ToJson(report, true));
    }
}
