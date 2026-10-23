using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ForestSpriteCharacter;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Vegetation;
using Object = UnityEngine.Object;

namespace WeatherHouse.Hillside.Editor
{
    // Editor-only, temporary Play measurements. Never saves or rebuilds the user's scene.
    [InitializeOnLoad]
    public static class HillsidePerformanceDiagnostics
    {
        const string Folder = "HillsidePerformanceReview", Key = "WeatherHouse.HillsidePerformance.";
        static readonly string[] Stages = { "cold_start", "baseline", "grass_off", "wildlife_off", "reflections_off", "all_off", "baseline_repeat", "gait_run", "gait_run_no_grass" };
        static readonly List<Frame> frames = new List<Frame>(40000);
        static readonly List<StageSummary> summaries = new List<StageSummary>();
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        static HillsideGrassRenderer[] grasses;
        static PainterlyWaterReflection[] reflections;
        static ForestSpriteDemoController player;
        static VegetationTopDownCamera cameraDriver;
        static Transform leftFoot, rightFoot, leftLeg, rightLeg;
        static RuntimeSnapshot snapshot;
        static ProfilerRecorder mainThread, renderThread, gc;
        static double nextPoll, stageStarted, playRequested;
        static double recoveryAfter, nextRecovery;
        static int recoveryState = -1, stableRecoveryTicks;
        static int stage = -1, lastFrame = -1;
        static string runFolder, status = "idle", error = "";
        static bool measuring, stopping, globalsOnlyRestore;

        [Serializable] sealed class Toggle { public int id; public bool value; }
        [Serializable] sealed class RuntimeSnapshot { public Toggle[] behaviours, objects, grassCapture; }
        [Serializable] sealed class GlobalSnapshot { public bool background, paused; public int vSync, target; public string playStart; }
        [Serializable] sealed class SceneState { public string path; public bool dirty, loaded, active; }
        [Serializable] sealed class CellBenchmark { public string utc; public Vector2Int key; public float density, cellSize; public Vector3 cameraPosition; public double[] milliseconds; }
        [Serializable] sealed class SpeciesInventory { public string species; public int animals, skinnedRenderers; public long vertices, triangles; }
        [Serializable] sealed class Inventory
        {
            public string utc, activeScene, quality, graphicsDevice, graphicsApi, screenResolution, phase, runPath;
            public bool playing, paused, dirty, compiling, running, runInBackground;
            public int gameWidth, gameHeight, qualityLevel, vSync, targetFrameRate, renderers, skinnedRenderers, meshFilters, instancedMaterials;
            public long sceneVertices, sceneTriangles;
            public SceneState[] scenes;
            public SpeciesInventory[] wildlife;
        }
        struct Frame
        {
            public int stage, frame, frameGap, drawCalls, batches, grassCells, grassClumps, stateHash;
            public double elapsed, ms, mainMs, renderMs, gcBytes, triangles, normalizedTime, jumpPhase, runCadence, velocityY, verticalSpeed;
            public string state;
            public bool grounded, animatorRun, animatorJump, transition, applicationFocused;
            public Vector3 position, leftFootPosition, rightFootPosition;
            public Quaternion leftLegRotation, rightLegRotation;
        }
        [Serializable] sealed class Distribution { public double median, p95, max; public int validSamples; }
        [Serializable] sealed class StageSummary
        {
            public string stage;
            public int samples, firstFrame, lastFrame, observedFrameSpan, focusedSamples;
            public double sampleCoverage, focusedFraction;
            public Distribution frameMs, mainThreadMs, renderThreadMs, gcBytes, drawCalls, batches, triangles, grassCells, grassClumps;
        }
        [Serializable] sealed class Report
        {
            public string utc, status, error, runPath, currentStage, leftFootPath, rightFootPath, leftLegPath, rightLegPath;
            public string evidence = "Unity Editor Play, foreground Game view required; CPU/GC recorder unavailable values are -1. Frame time and Main Thread include Editor overhead/waits; no player build or GPU timing. Editor update observes distinct Time.frameCount values and may miss frames; frameGap/sampleCoverage expose this. Cold start begins at EnteredPlayMode and excludes domain reload/asset import. Grass uses manualCaptureOnly to preserve caches: grassCells counts cached cells; grassClumps is zero while suppressed. Gait run moves the player and is separate from stationary comparisons. AI progresses between stages; baseline_repeat checks drift.";
            public StageSummary[] stages;
        }

        static HillsidePerformanceDiagnostics()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += PlayChanged;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            EditorApplication.delayCall += Recover;
        }

        static bool Running => SessionState.GetBool(Key + "active", false);
        static bool RestorePending => SessionState.GetBool(Key + "restorePending", false);
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static T[] SceneObjects<T>() where T : Component => Object.FindObjectsOfType<T>(true).Where(o => o.gameObject.scene.IsValid() && o.gameObject.scene.isLoaded).ToArray();
        static void Json(string path, object value) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(value, true)); }

        static void Update()
        {
            try
            {
                if (EditorApplication.timeSinceStartup >= nextPoll && !EditorApplication.isCompiling)
                {
                    nextPoll = EditorApplication.timeSinceStartup + .2;
                    string path = Folder + "/request.txt";
                    if (File.Exists(path))
                    {
                        string command = File.ReadAllText(path).Trim(); File.Delete(path);
                        try
                        {
                            if (command == "inspect") Inspect();
                            else if (command == "start") Start();
                            else if (command == "start_current") StartComparison(false, true);
                            else if (command == "start_gait") StartGait();
                            else if (command == "stop") Stop("stopped");
                            else if (command == "restore") RequestRestore();
                            else if (command == "benchmark_cell") BenchmarkCell();
                            else if (command == "selfcheck") SelfCheck();
                            else if (command == "grass_checks") RunRepairCheck("WeatherHouse.Hillside.Editor.HillsideGrassPerformanceChecks", "grass-repair-checks.txt");
                            else if (command == "cape_checks") RunRepairCheck("ForestSpriteCharacter.Editor.ForestSpriteCapePerformanceChecks", "cape-repair-checks.txt");
                            else if (command == "runtime_checks") RunRepairCheck("WeatherHouse.Hillside.Editor.HillsideSafeRuntimeChecks", "runtime-repair-checks.txt");
                            else throw new InvalidOperationException("Unknown performance command: " + command);
                            File.WriteAllText(Folder + "/last-command.txt", command + " OK " + DateTime.UtcNow.ToString("O"));
                        }
                        catch (Exception e) { File.WriteAllText(Folder + "/last-command.txt", command + " FAIL\n" + e); }
                    }
                }
                if (measuring && EditorApplication.isPlaying && !EditorApplication.isPaused) Tick();
                else if (Running && !EditorApplication.isPlayingOrWillChangePlaymode && playRequested > 0 && EditorApplication.timeSinceStartup - playRequested > 30) Stop("play_entry_failed");
                if (RestorePending && !measuring && (globalsOnlyRestore || !string.IsNullOrEmpty(SessionState.GetString(Key + "runtime", "")))) RecoveryTick();
            }
            catch (Exception e) { error = e.ToString(); Stop("failed"); Debug.LogException(e); }
        }

        [MenuItem("Weather House/Hillside/Performance/Inspect")]
        public static void Inspect()
        {
            var scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).ToArray();
            var renderers = SceneObjects<Renderer>();
            long vertices = 0, triangles = 0;
            foreach (var r in renderers) { Mesh mesh = MeshOf(r); if (mesh) { vertices += mesh.vertexCount; triangles += Triangles(mesh); } }
            var inventory = new Inventory {
                utc = DateTime.UtcNow.ToString("O"), activeScene = SceneManager.GetActiveScene().path,
                playing = EditorApplication.isPlaying, paused = EditorApplication.isPaused, dirty = scenes.Any(s => s.isDirty),
                compiling = EditorApplication.isCompiling, running = Running, phase = stage < 0 ? status : Stages[stage], runPath = runFolder,
                qualityLevel = QualitySettings.GetQualityLevel(), quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
                vSync = QualitySettings.vSyncCount, targetFrameRate = Application.targetFrameRate, runInBackground = Application.runInBackground,
                graphicsDevice = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                screenResolution = UnityStats.screenRes, gameWidth = Screen.width, gameHeight = Screen.height,
                renderers = renderers.Length, skinnedRenderers = SceneObjects<SkinnedMeshRenderer>().Length, meshFilters = SceneObjects<MeshFilter>().Length,
                instancedMaterials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m && m.enableInstancing).Distinct().Count(),
                sceneVertices = vertices, sceneTriangles = triangles,
                scenes = scenes.Select(s => new SceneState { path = s.path, dirty = s.isDirty, loaded = s.isLoaded, active = s == SceneManager.GetActiveScene() }).ToArray(),
                wildlife = SceneObjects<WildlifeAgent>().GroupBy(a => a.speciesId).Select(g => {
                    var skins = g.SelectMany(a => a.GetComponentsInChildren<SkinnedMeshRenderer>(true)).ToArray();
                    return new SpeciesInventory { species = g.Key, animals = g.Count(), skinnedRenderers = skins.Length,
                        vertices = skins.Sum(r => r.sharedMesh ? (long)r.sharedMesh.vertexCount : 0), triangles = skins.Sum(r => r.sharedMesh ? Triangles(r.sharedMesh) : 0) };
                }).ToArray()
            };
            Json(Folder + "/state.json", inventory);
        }

        static Mesh MeshOf(Renderer renderer) => renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
        static long Triangles(Mesh mesh) { long total = 0; for (int i = 0; i < mesh.subMeshCount; i++) if (mesh.GetTopology(i) == MeshTopology.Triangles) total += (long)mesh.GetIndexCount(i) / 3; return total; }
        static void RunRepairCheck(string typeName, string reportName)
        {
            Require(!Running && !EditorApplication.isPlayingOrWillChangePlaymode, "Repair checks need Edit mode.");
            try
            {
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).FirstOrDefault(t => t != null);
                Require(type != null, "Repair check not imported: " + typeName);
                var run = type.GetMethod("Run", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                Require(run != null, "Repair check has no public static Run method: " + typeName);
                string result = run.Invoke(null, null)?.ToString() ?? "PASS";
                File.WriteAllText(Folder + "/" + reportName, result);
            }
            catch (Exception e) { File.WriteAllText(Folder + "/" + reportName, "FAIL\n" + e); throw; }
        }
        static void FocusGameView() { var type = Type.GetType("UnityEditor.GameView,UnityEditor"); if (type != null) EditorWindow.GetWindow(type)?.Focus(); }

        [MenuItem("Weather House/Hillside/Performance/Start Comparison")]
        public static void Start() { StartComparison(false); }
        [MenuItem("Weather House/Hillside/Performance/Start Gait Comparison")]
        public static void StartGait() { StartComparison(true); }
        static void StartComparison(bool gaitOnly, bool currentUnsavedScene = false)
        {
            Require(!Running && !EditorApplication.isPlayingOrWillChangePlaymode, "Stop the current Play/session first.");
            Require(SceneManager.GetActiveScene().path == HillsideSceneBuilder.ScenePath, "Open the saved WeatherHouse_Hillside scene first.");
            if (currentUnsavedScene)
                Require(SceneManager.sceneCount == 1, "Current-scene measurements need only the hillside scene open; diagnostics never closes other scenes.");
            else
                for (int i = 0; i < SceneManager.sceneCount; i++) { var scene = SceneManager.GetSceneAt(i); Require(!scene.isDirty && !string.IsNullOrEmpty(scene.path), "All open scenes must already be saved; diagnostics never saves them."); }
            Inspect();
            bool inheritedRestore = RestorePending && !string.IsNullOrEmpty(SessionState.GetString(Key + "globals", ""));
            if (inheritedRestore) RestoreGlobals();
            var globals = new GlobalSnapshot { background = Application.runInBackground, paused = EditorApplication.isPaused,
                vSync = QualitySettings.vSyncCount, target = Application.targetFrameRate, playStart = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) };
            if (!inheritedRestore) SessionState.SetString(Key + "globals", JsonUtility.ToJson(globals));
            SessionState.SetString(Key + "runtime", ""); SessionState.SetBool(Key + "gaitOnly", gaitOnly);
            runFolder = Folder + "/" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"); Directory.CreateDirectory(runFolder);
            File.Copy(Folder + "/state.json", runFolder + "/entry-state.json");
            SessionState.SetString(Key + "runFolder", runFolder);
            File.WriteAllText(Folder + "/latest-run.txt", runFolder); frames.Clear(); summaries.Clear(); error = ""; status = "entering_play";
            stage = -1; snapshot = null; stopping = measuring = globalsOnlyRestore = false; leftFoot = rightFoot = null;
            SessionState.SetBool(Key + "active", true); SessionState.SetBool(Key + "restorePending", true);
            SessionState.SetBool(Key + "explicitRestore", false);
            try
            {
                WriteReport();
                Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                EditorApplication.isPaused = false; EditorSceneManager.playModeStartScene = null;
                FocusGameView(); playRequested = EditorApplication.timeSinceStartup; EditorApplication.isPlaying = true;
            }
            catch (Exception e) { error = e.ToString(); Stop("start_failed"); throw; }
        }

        static void PlayChanged(PlayModeStateChange change)
        {
            if (!Running && !RestorePending) return;
            try
            {
                if (change == PlayModeStateChange.EnteredPlayMode && Running && string.IsNullOrEmpty(SessionState.GetString(Key + "runtime", ""))) BeginRuntime();
                else if (change == PlayModeStateChange.ExitingPlayMode)
                {
                    try { if (!stopping) { status = "interrupted"; FinishStage(); WriteReport(); } }
                    finally { measuring = false; if (!globalsOnlyRestore) Safely(RestoreRuntime); Safely(DisposeRecorders); Safely(RestoreGlobals); }
                }
                else if (change == PlayModeStateChange.EnteredEditMode)
                {
                    FinishRestoration();
                }
            }
            catch (Exception e) { error = e.ToString(); Stop("failed"); }
        }

        static void BeginRuntime()
        {
            Require(SceneManager.GetActiveScene().path == HillsideSceneBuilder.ScenePath, "Unexpected Play scene; aborting.");
            grasses = SceneObjects<HillsideGrassRenderer>(); reflections = SceneObjects<PainterlyWaterReflection>();
            player = SceneObjects<ForestSpriteDemoController>().Single(); cameraDriver = SceneObjects<VegetationTopDownCamera>().FirstOrDefault();
            var bones = player.animator.GetComponentsInChildren<Transform>(true);
            leftFoot = bones.FirstOrDefault(t => t.name == "Foot.L"); rightFoot = bones.FirstOrDefault(t => t.name == "Foot.R");
            leftLeg = bones.FirstOrDefault(t => t.name == "UpperLeg.L"); rightLeg = bones.FirstOrDefault(t => t.name == "UpperLeg.R");
            var behaviours = new List<Behaviour>(); behaviours.AddRange(grasses); behaviours.AddRange(reflections);
            var objects = new HashSet<GameObject>();
            foreach (var population in SceneObjects<WildlifePopulation>())
            {
                behaviours.Add(population);
                // Disable individual agent roots: never deactivate a population ancestor containing terrain/player.
                foreach (var agent in population.agents.Where(a => a))
                {
                    Require(!player.transform.IsChildOf(agent.transform) && !agent.GetComponentInChildren<Terrain>(true), "An animal root contains unrelated scene objects.");
                    objects.Add(agent.gameObject);
                }
            }
            if (cameraDriver) behaviours.Add(cameraDriver);
            snapshot = new RuntimeSnapshot { behaviours = behaviours.Distinct().Select(b => new Toggle { id = b.GetInstanceID(), value = b.enabled }).ToArray(),
                objects = objects.Select(g => new Toggle { id = g.GetInstanceID(), value = g.activeSelf }).ToArray(),
                grassCapture = grasses.Select(g => new Toggle { id = g.GetInstanceID(), value = g.manualCaptureOnly }).ToArray() };
            SessionState.SetString(Key + "runtime", JsonUtility.ToJson(snapshot));
            player.SetScriptedInput(Vector2.zero, false, false);
            if (cameraDriver) { cameraDriver.SnapToPlayer(); cameraDriver.enabled = false; }
            Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            mainThread = Recorder(ProfilerCategory.Internal, "Main Thread"); renderThread = Recorder(ProfilerCategory.Internal, "Render Thread");
            gc = Recorder(ProfilerCategory.Memory, "GC Allocated In Frame");
            measuring = true; status = "running"; lastFrame = -1; FocusGameView(); BeginStage(SessionState.GetBool(Key + "gaitOnly", false) ? 7 : 0); Inspect();
        }

        static ProfilerRecorder Recorder(ProfilerCategory category, string name) { try { return ProfilerRecorder.StartNew(category, name, 1); } catch { return default; } }
        static double Reading(ProfilerRecorder recorder, double scale = 1) => recorder.Valid && recorder.Count > 0 ? recorder.LastValue * scale : -1;
        static void DisposeRecorders() { mainThread.Dispose(); renderThread.Dispose(); gc.Dispose(); mainThread = renderThread = gc = default; }

        static void RestoreRuntime()
        {
            if (snapshot == null) { string json = SessionState.GetString(Key + "runtime", ""); if (!string.IsNullOrEmpty(json)) snapshot = JsonUtility.FromJson<RuntimeSnapshot>(json); }
            if (snapshot != null)
            {
                foreach (var t in snapshot.objects) if (EditorUtility.InstanceIDToObject(t.id) is GameObject go && go.activeSelf != t.value) go.SetActive(t.value);
                foreach (var t in snapshot.behaviours) if (EditorUtility.InstanceIDToObject(t.id) is Behaviour b && b.enabled != t.value) b.enabled = t.value;
                foreach (var t in snapshot.grassCapture) if (EditorUtility.InstanceIDToObject(t.id) is HillsideGrassRenderer g) g.manualCaptureOnly = t.value;
            }
            if (player) player.ReleaseScriptedInput();
        }

        static void BeginStage(int index)
        {
            RestoreRuntime(); stage = index; stageStarted = EditorApplication.timeSinceStartup;
            bool all = Stages[stage] == "all_off";
            if (all || Stages[stage] == "grass_off") foreach (var grass in grasses) grass.manualCaptureOnly = true;
            if (all || Stages[stage] == "reflections_off") foreach (var reflection in reflections) reflection.enabled = false;
            if (all || Stages[stage] == "wildlife_off")
            {
                foreach (var population in SceneObjects<WildlifePopulation>()) population.enabled = false;
                foreach (var t in snapshot.objects) if (EditorUtility.InstanceIDToObject(t.id) is GameObject go) go.SetActive(false);
            }
            bool gait = Stages[stage].StartsWith("gait_run", StringComparison.Ordinal);
            if (gait) { player.ResetPosition(); if (cameraDriver) cameraDriver.SnapToPlayer(); }
            if (Stages[stage] == "gait_run_no_grass") foreach (var grass in grasses) grass.manualCaptureOnly = true;
            player.SetScriptedInput(gait ? Vector2.up : Vector2.zero, gait, false);
            if (cameraDriver && !gait) cameraDriver.enabled = false;
            WriteReport();
        }

        static void Tick()
        {
            if (Time.frameCount == lastFrame) return;
            int gap = lastFrame < 0 ? 0 : Time.frameCount - lastFrame; lastFrame = Time.frameCount;
            double elapsed = EditorApplication.timeSinceStartup - stageStarted;
            bool cold = stage == 0, gait = Stages[stage].StartsWith("gait_run", StringComparison.Ordinal);
            double warmup = cold || gait ? 0 : 2, duration = cold ? 10 : gait ? 6 : 4;
            if (elapsed >= warmup && Time.unscaledDeltaTime > 0)
            {
                AnimatorStateInfo info = player.animator ? player.animator.GetCurrentAnimatorStateInfo(0) : default;
                int cellCount = 0, clumpCount = 0;
                foreach (var grass in grasses) if (grass && grass.enabled) { cellCount += grass.CachedCellCount; if (!grass.manualCaptureOnly) clumpCount += grass.VisibleClumpCount; }
                frames.Add(new Frame { stage = stage, frame = Time.frameCount, frameGap = gap, elapsed = elapsed, ms = Time.unscaledDeltaTime * 1000,
                    mainMs = Reading(mainThread, .000001), renderMs = Reading(renderThread, .000001), gcBytes = Reading(gc),
                    drawCalls = UnityStats.drawCalls, batches = UnityStats.batches, triangles = UnityStats.triangles,
                    grassCells = cellCount, grassClumps = clumpCount,
                    state = player.CurrentState, grounded = player.IsGrounded, applicationFocused = Application.isFocused, jumpPhase = player.JumpPhase, normalizedTime = info.normalizedTime,
                    stateHash = info.shortNameHash, position = player.transform.position, animatorRun = info.IsName("Run"), animatorJump = info.IsName("Jump"),
                    transition = player.animator && player.animator.IsInTransition(0), runCadence = player.animator ? player.animator.GetFloat("RunCadence") : -1,
                    velocityY = player.ActualVelocity.y, verticalSpeed = player.VerticalSpeed,
                    leftFootPosition = leftFoot ? leftFoot.localPosition : Vector3.zero, rightFootPosition = rightFoot ? rightFoot.localPosition : Vector3.zero,
                    leftLegRotation = leftLeg ? leftLeg.localRotation : Quaternion.identity, rightLegRotation = rightLeg ? rightLeg.localRotation : Quaternion.identity });
            }
            if (elapsed < warmup + duration) return;
            FinishStage();
            if (stage + 1 < Stages.Length) BeginStage(stage + 1); else Stop("complete");
        }

        static Distribution Stats(IEnumerable<double> input)
        {
            double[] values = input.Where(v => v >= 0 && !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v => v).ToArray();
            return new Distribution { validSamples = values.Length, median = Percentile(values, .5), p95 = Percentile(values, .95), max = values.Length == 0 ? -1 : values[values.Length - 1] };
        }
        // Linear interpolation on sorted samples, including one-sample and unavailable-recorder cases.
        static double Percentile(double[] sorted, double fraction) { if (sorted.Length == 0) return -1; double rank = (sorted.Length - 1) * fraction; int low = (int)rank; return sorted[low] + (sorted[Math.Min(low + 1, sorted.Length - 1)] - sorted[low]) * (rank - low); }

        static void FinishStage()
        {
            if (stage < 0 || summaries.Any(s => s.stage == Stages[stage])) return;
            var sample = frames.Where(f => f.stage == stage).ToArray();
            if (sample.Length == 0) return;
            summaries.Add(new StageSummary { stage = Stages[stage], samples = sample.Length, firstFrame = sample[0].frame, lastFrame = sample[sample.Length - 1].frame,
                observedFrameSpan = sample[sample.Length - 1].frame - sample[0].frame + 1, sampleCoverage = sample.Length / (double)(sample[sample.Length - 1].frame - sample[0].frame + 1),
                focusedSamples = sample.Count(f => f.applicationFocused), focusedFraction = sample.Count(f => f.applicationFocused) / (double)sample.Length,
                frameMs = Stats(sample.Select(f => f.ms)), mainThreadMs = Stats(sample.Select(f => f.mainMs)), renderThreadMs = Stats(sample.Select(f => f.renderMs)),
                gcBytes = Stats(sample.Select(f => f.gcBytes)), drawCalls = Stats(sample.Select(f => (double)f.drawCalls)), batches = Stats(sample.Select(f => (double)f.batches)),
                triangles = Stats(sample.Select(f => f.triangles)), grassCells = Stats(sample.Select(f => (double)f.grassCells)), grassClumps = Stats(sample.Select(f => (double)f.grassClumps)) });
            WriteFrames(); WriteReport();
        }

        static void WriteFrames()
        {
            var csv = new StringBuilder("stage,frame,stageSeconds,frameMs,mainThreadMs,renderThreadMs,gcBytes,drawCalls,batches,triangles,grassCells,grassClumps,currentState,grounded,jumpPhase,normalizedTime,stateHash,x,y,z,animatorRun,animatorJump,inTransition,runCadence,velocityY,verticalSpeed,leftFootX,leftFootY,leftFootZ,rightFootX,rightFootY,rightFootZ,leftLegQx,leftLegQy,leftLegQz,leftLegQw,rightLegQx,rightLegQy,rightLegQz,rightLegQw,frameGap,applicationFocused\n");
            foreach (var f in frames) csv.AppendFormat(Invariant, "{0},{1},{2:F6},{3:F6},{4:F6},{5:F6},{6:F0},{7},{8},{9:F0},{10},{11},{12},{13},{14:F6},{15:F6},{16},{17:F6},{18:F6},{19:F6},{20},{21},{22},{23:F6},{24:F6},{25:F6},{26:F6},{27:F6},{28:F6},{29:F6},{30:F6},{31:F6},{32:F6},{33:F6},{34:F6},{35:F6},{36:F6},{37:F6},{38:F6},{39:F6},{40},{41}\n",
                Stages[f.stage], f.frame, f.elapsed, f.ms, f.mainMs, f.renderMs, f.gcBytes, f.drawCalls, f.batches, f.triangles, f.grassCells, f.grassClumps,
                f.state, f.grounded ? 1 : 0, f.jumpPhase, f.normalizedTime, f.stateHash, f.position.x, f.position.y, f.position.z,
                f.animatorRun ? 1 : 0, f.animatorJump ? 1 : 0, f.transition ? 1 : 0, f.runCadence, f.velocityY, f.verticalSpeed,
                f.leftFootPosition.x, f.leftFootPosition.y, f.leftFootPosition.z, f.rightFootPosition.x, f.rightFootPosition.y, f.rightFootPosition.z,
                f.leftLegRotation.x, f.leftLegRotation.y, f.leftLegRotation.z, f.leftLegRotation.w, f.rightLegRotation.x, f.rightLegRotation.y, f.rightLegRotation.z, f.rightLegRotation.w, f.frameGap, f.applicationFocused ? 1 : 0);
            File.WriteAllText(runFolder + "/frames.csv", csv.ToString());
        }

        static void WriteReport()
        {
            if (string.IsNullOrEmpty(runFolder)) runFolder = SessionState.GetString(Key + "runFolder", "");
            if (string.IsNullOrEmpty(runFolder)) return;
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), status = status, error = error, runPath = runFolder,
                currentStage = stage < 0 ? "" : Stages[stage], stages = summaries.ToArray(),
                leftFootPath = leftFoot && player ? AnimationUtility.CalculateTransformPath(leftFoot, player.transform) : "unavailable",
                rightFootPath = rightFoot && player ? AnimationUtility.CalculateTransformPath(rightFoot, player.transform) : "unavailable",
                leftLegPath = leftLeg && player ? AnimationUtility.CalculateTransformPath(leftLeg, player.transform) : "unavailable",
                rightLegPath = rightLeg && player ? AnimationUtility.CalculateTransformPath(rightLeg, player.transform) : "unavailable" };
            Json(runFolder + "/summary.json", report); Json(Folder + "/summary.json", report);
        }

        public static void Stop(string reason = "stopped")
        {
            if (!Running) { if (RestorePending) RequestRestore(); return; }
            stopping = true; measuring = false; status = reason;
            SessionState.SetBool(Key + "restorePending", true);
            try { FinishStage(); WriteReport(); }
            finally
            {
                if (EditorApplication.isPlaying) Safely(RestoreRuntime);
                Safely(DisposeRecorders); Safely(RestoreGlobals);
                if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
            }
        }
        [MenuItem("Weather House/Hillside/Performance/Stop")]
        static void StopMenu() { Stop(); }
        static void Safely(Action action) { try { action(); } catch (Exception e) { Debug.LogException(e); } }

        static void RestoreGlobals()
        {
            string json = SessionState.GetString(Key + "globals", ""); if (string.IsNullOrEmpty(json)) return;
            var saved = JsonUtility.FromJson<GlobalSnapshot>(json);
            Application.runInBackground = saved.background; QualitySettings.vSyncCount = saved.vSync; Application.targetFrameRate = saved.target;
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(saved.playStart) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(saved.playStart);
            EditorApplication.isPaused = saved.paused;
        }

        static void BeforeReload()
        {
            if (!measuring) return;
            SessionState.SetBool(Key + "restorePending", true); status = "aborted_domain_reload";
            error = "Script domain reloaded during measurement; partial run retained. Restoration continues after reload.";
            try { FinishStage(); WriteReport(); }
            catch (Exception e) { Debug.LogWarning("Performance interruption report: " + e.Message); }
            finally { measuring = false; Safely(DisposeRecorders); }
        }
        static void Recover()
        {
            if (!RestorePending && !Running) return;
            if (Running && !string.IsNullOrEmpty(SessionState.GetString(Key + "globals", ""))) SessionState.SetBool(Key + "restorePending", true);
            runFolder = SessionState.GetString(Key + "runFolder", "");
            if (!string.IsNullOrEmpty(SessionState.GetString(Key + "runtime", ""))) stopping = globalsOnlyRestore = true;
            if (SessionState.GetBool(Key + "explicitRestore", false) || (RestorePending && !Running)) stopping = globalsOnlyRestore = true;
            recoveryAfter = EditorApplication.timeSinceStartup + 1.5; recoveryState = -1; stableRecoveryTicks = 0;
            try
            {
                string path = runFolder + "/summary.json";
                if (File.Exists(path)) { var report = JsonUtility.FromJson<Report>(File.ReadAllText(path)); if (report != null) { status = report.status; error = report.error; if (report.stages != null) summaries.AddRange(report.stages); } }
            }
            catch (Exception e) { Debug.LogWarning("Performance report recovery: " + e.Message); }
            // Never clear pending/active from transient false Play flags during a domain reload.
        }

        [MenuItem("Weather House/Hillside/Performance/Restore Pending Session")]
        public static void RequestRestore()
        {
            Require(!string.IsNullOrEmpty(SessionState.GetString(Key + "globals", "")), "No saved performance settings remain to restore.");
            SessionState.SetBool(Key + "restorePending", true); measuring = false; stopping = globalsOnlyRestore = true;
            SessionState.SetBool(Key + "explicitRestore", true);
            Safely(DisposeRecorders); RestoreGlobals();
            recoveryAfter = EditorApplication.timeSinceStartup + 1.5; recoveryState = -1; stableRecoveryTicks = 0;
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
            Inspect();
        }

        static void RecoveryTick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < recoveryAfter || now < nextRecovery || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextRecovery = now + .2;
            int current = EditorApplication.isPlaying ? 2 : EditorApplication.isPlayingOrWillChangePlaymode ? 1 : 0;
            stableRecoveryTicks = current == recoveryState ? stableRecoveryTicks + 1 : 0; recoveryState = current;
            if (stableRecoveryTicks < 3) return;
            Safely(DisposeRecorders); Safely(RestoreGlobals);
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
            else if (globalsOnlyRestore) FinishRestoration();
        }

        static void FinishRestoration()
        {
            RestoreGlobals(); SessionState.SetBool(Key + "active", false); SessionState.SetBool(Key + "restorePending", false);
            SessionState.SetBool(Key + "explicitRestore", false); SessionState.SetString(Key + "runtime", ""); SessionState.SetString(Key + "globals", "");
            snapshot = null; stopping = globalsOnlyRestore = false; Inspect();
        }

        [MenuItem("Weather House/Hillside/Performance/Benchmark Cell CPU")]
        public static void BenchmarkCell()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !Running, "Cell benchmark requires Edit mode with no active measurement.");
            Require(SceneManager.GetActiveScene().path == HillsideSceneBuilder.ScenePath, "Open the saved Hillside scene first.");
            var source = SceneObjects<HillsideGrassRenderer>().Single(); var character = SceneObjects<ForestSpriteDemoController>().Single();
            var camera = Camera.main; Require(camera, "Main Game camera is missing.");
            var key = new Vector2Int(Mathf.FloorToInt(character.transform.position.x / source.cellSize), Mathf.FloorToInt(character.transform.position.z / source.cellSize));
            var method = typeof(HillsideGrassRenderer).GetMethod("BuildCell", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Require(method != null, "BuildCell entrypoint is missing."); var elapsed = new double[3];
            var preview = EditorSceneManager.NewPreviewScene(); GameObject holder = null;
            try
            {
                holder = new GameObject("Isolated grass benchmark") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(holder, preview);
                var grass = holder.AddComponent<HillsideGrassRenderer>(); grass.manualCaptureOnly = true;
                grass.terrain = source.terrain; grass.variants = source.variants; grass.materials = source.materials;
                grass.speciesWeights = (float[])source.speciesWeights.Clone(); grass.seed = source.seed;
                grass.clumpsPerSquareMeter = source.clumpsPerSquareMeter; grass.cellSize = source.cellSize;
                grass.visibleRadius = source.visibleRadius; grass.maximumSlope = source.maximumSlope;
                for (int i = 0; i < elapsed.Length; i++) { var watch = System.Diagnostics.Stopwatch.StartNew(); method.Invoke(grass, new object[] { key, grass.cellSize, camera.transform.position }); watch.Stop(); elapsed[i] = watch.Elapsed.TotalMilliseconds; }
                Json(Folder + "/cell-benchmark.json", new CellBenchmark { utc = DateTime.UtcNow.ToString("O"), key = key, density = grass.clumpsPerSquareMeter, cellSize = grass.cellSize, cameraPosition = camera.transform.position, milliseconds = elapsed });
            }
            finally { if (holder) Object.DestroyImmediate(holder); EditorSceneManager.ClosePreviewScene(preview); }
        }

        [MenuItem("Weather House/Hillside/Performance/Self Check")]
        public static void SelfCheck()
        {
            Require(Percentile(new double[0], .5) == -1, "Empty percentile failed.");
            Require(Percentile(new[] { 8.0 }, .95) == 8, "Single percentile failed.");
            Require(Percentile(new[] { 1.0, 2, 3, 4 }, .5) == 2.5, "Even median failed.");
            Require(Math.Abs(Percentile(new[] { 1.0, 2, 3, 4 }, .95) - 3.85) < .000001, "Interpolated p95 failed.");
            var result = Stats(new[] { -1.0, double.NaN, 5, 1, 3 });
            Require(result.validSamples == 3 && result.median == 3 && result.max == 5, "Unavailable metric filtering failed.");
            Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/selfcheck.txt", "PASS: empty, single, even median, interpolated p95, unavailable metrics.\n");
        }
    }
}
