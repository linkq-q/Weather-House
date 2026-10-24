using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ForestSpriteCharacter.Editor
{
    [InitializeOnLoad]
    public static class ForestSpriteGaitReview
    {
        const string Folder = "VegetationLabReview/GaitRevision20261022";
        const string RequestPath = Folder + "/request.json";
        const string LastIdKey = "ForestSpriteGaitReview.LastId";
        const int CaptureFrameRate = 120;
        const float FloorY = 100, WarmupSeconds = 2, MeasureSeconds = 3;
        static readonly float[][] Phases = {
            new[] { 0f, .125f, .25f, .375f, .5f, .625f, .75f, .875f },
            new[] { 0f, .09f, .21f, .36f, .5f, .59f, .71f, .86f }
        };
        [Serializable] sealed class Request { public string id = "", command = "", label = ""; }
        [Serializable] sealed class Response { public string id, status, message, output, utc; }
        [Serializable] sealed class Frame
        {
            public string action, state;
            public int referenceFrame, unityFrame;
            public float targetPhase, actualPhase, normalizedTime, phaseError, deltaTime, time, stageTime;
            public bool grounded, finite;
            public Vector3 root, hips, leftFoot, rightFoot, leftHand, rightHand;
            public float leftSole, rightSole, wavePhase, flowAmount, wingSpread;
            public float[] guideTangent;
            public float stretchP95, stretchMaximum, clothStretchP95, clothStretchMaximum;
            public Vector3 clothBoundsCenter, clothBoundsSize;
            public string[] images;
        }
        [Serializable] sealed class SpeedSample
        {
            public string preset, action;
            public int fps, frames;
            public float configuredSpeed, seconds, horizontalDistance, displacement, measuredSpeed, speedRatio, newToOldRatio;
            public float dtElapsed, realtimeElapsed, measuredRealtimeSpeed;
            public bool comparisonAvailable;
            public float minDeltaTime = float.MaxValue, maxDeltaTime;
            public Vector3 start, end;
        }
        [Serializable] sealed class Report
        {
            public string id, command, label, result = "RUNNING", error, utc, scene, animatorController;
            public string evidence = "Existing Play session; native Animator and Cloth; sampled after PhysicsSkinnedClothFinishUpdate. Camera images are one completed physical frame. No scene save or scene replacement.";
            public string capeSettings;
            public float walkSpeed, runSpeed, warmupSeconds = WarmupSeconds, floorHeight = FloorY;
            public int captureFrameRate;
            public bool restorationCompleted;
            public List<Frame> frames = new List<Frame>();
            public List<SpeedSample> speeds = new List<SpeedSample>();
        }
        sealed class ReviewFrameEnd { }
        sealed class CameraState { public Camera camera; public bool enabled; }
        sealed class BehaviourState { public Behaviour component; public bool enabled; }
        sealed class LayerState { public GameObject gameObject; public int layer; }
        sealed class ParameterState { public AnimatorControllerParameter parameter; public float number; public int integer; public bool flag; }
        static Request request;
        static Report report;
        static string output;
        static bool active, installed, pending, snapshotReady, speedMode;
        static double deadline, nextPoll;
        static int stage, lastFrame, targetIndex;
        static float stageTime, cycleStart, previousNormalized, settleTime, capturedDeltaTime;
        static double measureRealtimeStarted;
        static bool startedMovement, targetsArmed, measuring;
        static ForestSpriteDemoController player;
        static CharacterController motor;
        static Animator animator;
        static ForestSpriteCapeCloth cape;
        static Camera inputCamera, captureCamera;
        static GameObject temporaryRoot;
        static Material groundMaterial;
        static SkinnedMeshRenderer boots;
        static Transform hips, leftFoot, rightFoot, leftHand, rightHand;
        static Vector3[] bootVertices;
        static BoneWeight[] bootWeights;
        static Matrix4x4[] bootBindposes;
        static bool[] leftBones, rightBones;
        static int[] edgeParticles;
        static float[] edgeLengths;
        static SpeedSample speedSample;
        static Vector3 previousMeasuredPosition, savedPosition;
        static Quaternion savedRotation;
        static Dictionary<FieldInfo, object> playerFields;
        static readonly List<CameraState> cameras = new List<CameraState>();
        static readonly List<BehaviourState> drivers = new List<BehaviourState>();
        static readonly List<LayerState> layers = new List<LayerState>();
        static readonly List<ParameterState> parameters = new List<ParameterState>();
        static bool savedPlayerEnabled, savedMotorEnabled, savedPaused, savedBackground, savedClothEnabled, savedWindPreview;
        static float savedTimeScale, savedFixedDelta, savedAnimatorSpeed, savedWavePhase;
        static int savedCaptureRate, savedVSync, savedTargetRate, savedStateHash;
        static float savedNormalizedTime;
        static AnimatorCullingMode savedCulling;
        static readonly FieldInfo WaveField = typeof(ForestSpriteCapeCloth).GetField("wavePhase", BindingFlags.Instance | BindingFlags.NonPublic);

        static ForestSpriteGaitReview()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.ExitingPlayMode && active) Finish("STOPPED", "Play mode exited.");
            };
            AssemblyReloadEvents.beforeAssemblyReload += () => { if (active) Finish("STOPPED", "Assembly reload."); };
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + .15;
            if (active && EditorApplication.timeSinceStartup > deadline) { Finish("FAIL", "Review exceeded ten real minutes."); return; }
            if (!File.Exists(RequestPath)) return;
            Request incoming;
            try { incoming = JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath)); }
            catch { return; } // A writer may still be replacing the request file.
            if (incoming == null || string.IsNullOrWhiteSpace(incoming.id) || incoming.id == SessionState.GetString(LastIdKey, "")) return;
            SessionState.SetString(LastIdKey, incoming.id);
            try
            {
                if (incoming.command == "ImportWalk")
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Import animation in Edit mode.");
                    AssetDatabase.ImportAsset(ForestSpriteDemoBuilder.ModelPath, ImportAssetOptions.ForceSynchronousImport);
                    var clip = ForestSpriteDemoBuilder.LoadClips()["Walk"];
                    var destination = AssetDatabase.LoadAssetAtPath<AnimationClip>(ForestSpriteDemoBuilder.Root + "/Generated/Walk_LongStride.anim");
                    if (!destination) throw new InvalidOperationException("Existing Walk animation missing.");
                    EditorUtility.CopySerialized(clip, destination); destination.name = "Walk"; destination.hideFlags = HideFlags.None;
                    EditorUtility.SetDirty(destination); AssetDatabase.SaveAssetIfDirty(destination);
                    Respond(incoming, "COMPLETE", "Copied authored Walk keys into the existing animation asset; scene and bindings retained.");
                }
                else if (incoming.command == "Stop")
                {
                    if (active) Finish("STOPPED", "Stopped by request.");
                    Respond(incoming, "STOPPED", "Review resources restored; existing Play session retained.");
                }
                else
                {
                    if (active) throw new InvalidOperationException("A review is active; send Stop before another request.");
                    if (incoming.command != "Capture" && incoming.command != "Speed") throw new InvalidOperationException("Commands: Capture, Speed, Stop.");
                    Begin(incoming);
                }
            }
            catch (Exception e)
            {
                if (active && request == incoming) Finish("FAIL", e.ToString());
                else Respond(incoming, "FAIL", e.ToString());
            }
        }

        static void Begin(Request incoming)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Run in the existing VegetationLab Play session.");
            if (string.IsNullOrEmpty(incoming.label) || incoming.label != Path.GetFileName(incoming.label)
                || incoming.label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || incoming.label == "." || incoming.label == "..")
                throw new InvalidOperationException("Use a plain evidence label such as before or after.");
            player = Object.FindObjectsOfType<ForestSpriteDemoController>().Single(p => p.isActiveAndEnabled);
            if (player.gameObject.scene.name.IndexOf("Vegetation", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("The live player must belong to the VegetationLab scene.");
            motor = player.GetComponent<CharacterController>(); animator = player.animator; cape = player.capeCloth;
            if (!motor || !animator || !cape || !cape.cloth) throw new InvalidOperationException("Player Animator/CharacterController/Cloth is incomplete.");
            if (WaveField == null) throw new InvalidOperationException("Cape wave phase reset entry is missing.");
            output = Path.GetFullPath(Path.Combine(Folder, incoming.label + (incoming.command == "Speed" ? "-speed" : "")));
            if (File.Exists(Path.Combine(output, "result.json"))) throw new InvalidOperationException("Evidence already exists; use a fresh label.");
            Directory.CreateDirectory(output);
            request = incoming; speedMode = incoming.command == "Speed";
            report = new Report { id = incoming.id, command = incoming.command, label = incoming.label,
                scene = player.gameObject.scene.path, animatorController = AssetDatabase.GetAssetPath(animator.runtimeAnimatorController),
                capeSettings = JsonUtility.ToJson(cape.settings), walkSpeed = player.walkSpeed, runSpeed = player.runSpeed,
                captureFrameRate = speedMode ? 0 : CaptureFrameRate };
            SaveSnapshot();
            savedCapeDiagnostics = cape.DiagnosticsEnabled;
            cape.DiagnosticsEnabled = true;
            active = true; deadline = EditorApplication.timeSinceStartup + 600;
            Time.timeScale = 1; Time.captureFramerate = speedMode ? 60 : CaptureFrameRate; Time.fixedDeltaTime = 1f / 60;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; Application.runInBackground = true;
            EditorApplication.isPaused = false;
            foreach (var state in cameras) state.camera.enabled = false;
            foreach (var state in drivers) state.component.enabled = false;
            player.externalCameraControl = true; player.showControls = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.speed = 1;
            BuildTemporaryStage();
            player.demoCamera = inputCamera;
            cape.SetWindPreview(false);
            PrepareMetrics();
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (!InsertMarker(ref loop)) throw new InvalidOperationException("PhysicsSkinnedClothFinishUpdate is missing.");
            PlayerLoop.SetPlayerLoop(loop); installed = true;
            BeginStage(0);
            SaveReport(); Respond(incoming, "RUNNING", "Native Play review started.");
        }

        static void SaveSnapshot()
        {
            savedPosition = player.transform.position; savedRotation = player.transform.rotation;
            savedPlayerEnabled = player.enabled; savedMotorEnabled = motor.enabled;
            playerFields = typeof(ForestSpriteDemoController).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => !f.IsInitOnly).ToDictionary(f => f, f => f.GetValue(player));
            savedCaptureRate = Time.captureFramerate; savedTimeScale = Time.timeScale; savedFixedDelta = Time.fixedDeltaTime;
            savedVSync = QualitySettings.vSyncCount; savedTargetRate = Application.targetFrameRate;
            savedBackground = Application.runInBackground; savedPaused = EditorApplication.isPaused;
            savedAnimatorSpeed = animator.speed; savedCulling = animator.cullingMode;
            var info = animator.GetCurrentAnimatorStateInfo(0); savedStateHash = info.fullPathHash; savedNormalizedTime = info.normalizedTime;
            parameters.Clear();
            foreach (var p in animator.parameters)
            {
                var state = new ParameterState { parameter = p };
                if (p.type == AnimatorControllerParameterType.Float) state.number = animator.GetFloat(p.nameHash);
                if (p.type == AnimatorControllerParameterType.Int) state.integer = animator.GetInteger(p.nameHash);
                if (p.type == AnimatorControllerParameterType.Bool) state.flag = animator.GetBool(p.nameHash);
                parameters.Add(state);
            }
            savedWavePhase = WaveField == null ? 0 : (float)WaveField.GetValue(cape);
            savedClothEnabled = cape.cloth.enabled; savedWindPreview = cape.WindPreview;
            cameras.Clear(); drivers.Clear(); layers.Clear();
            foreach (var c in Object.FindObjectsOfType<Camera>(true).Where(c => c.gameObject.scene.IsValid()))
                cameras.Add(new CameraState { camera = c, enabled = c.enabled });
            foreach (var b in Object.FindObjectsOfType<MonoBehaviour>(true))
                if (b.GetType().Name == "VegetationTopDownCamera"
                    || b.GetType().Name == "GrassLabCamera" || b is ForestSpriteCapePanel)
                    drivers.Add(new BehaviourState { component = b, enabled = b.enabled });
            foreach (var go in player.GetComponentsInChildren<Renderer>(true).Select(r => r.gameObject).Distinct())
                layers.Add(new LayerState { gameObject = go, layer = go.layer });
            snapshotReady = true;
        }

        static void BuildTemporaryStage()
        {
            temporaryRoot = new GameObject("Gait review temporary stage") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(temporaryRoot, player.gameObject.scene);
            GameObject Make(string name)
            {
                var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(temporaryRoot.transform, false); return go;
            }
            var solid = Make("Review floor collider"); solid.transform.position = new Vector3(0, FloorY - .5f, 0);
            solid.AddComponent<BoxCollider>().size = new Vector3(200, 1, 200);
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane); plane.name = "Review visible floor";
            plane.hideFlags = HideFlags.DontSave; plane.transform.SetParent(temporaryRoot.transform, false);
            plane.transform.position = Vector3.up * FloorY; plane.transform.localScale = Vector3.one * 20;
            Object.DestroyImmediate(plane.GetComponent<Collider>()); plane.layer = 31;
            groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.DontSave };
            groundMaterial.SetColor("_BaseColor", new Color(.74f, .76f, .74f)); groundMaterial.SetFloat("_Smoothness", 0);
            plane.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            inputCamera = Make("Review input direction +Z").AddComponent<Camera>(); inputCamera.enabled = false;
            inputCamera.transform.rotation = Quaternion.identity;
            captureCamera = Make("Review isolated camera").AddComponent<Camera>(); captureCamera.enabled = false;
            captureCamera.scene = player.gameObject.scene; captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = new Color(.82f, .84f, .82f); captureCamera.cullingMask = 1 << 31;
            captureCamera.orthographic = true; captureCamera.orthographicSize = 1.08f;
            captureCamera.nearClipPlane = .05f; captureCamera.farClipPlane = 25; captureCamera.allowHDR = false;
            foreach (var state in layers) state.gameObject.layer = 31;
            Physics.SyncTransforms();
        }

        static void PrepareMetrics()
        {
            var bones = player.visual.GetComponentsInChildren<Transform>().GroupBy(b => b.name).ToDictionary(g => g.Key, g => g.First());
            hips = bones["Hips"]; leftFoot = bones["Foot.L"]; rightFoot = bones["Foot.R"];
            leftHand = bones["Hand.L"]; rightHand = bones["Hand.R"];
            boots = player.visual.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "Body_Boots");
            bootVertices = boots.sharedMesh.vertices; bootWeights = boots.sharedMesh.boneWeights; bootBindposes = boots.sharedMesh.bindposes;
            leftBones = boots.bones.Select(b => b.name == "Foot.L" || b.name == "Toe.L").ToArray();
            rightBones = boots.bones.Select(b => b.name == "Foot.R" || b.name == "Toe.R").ToArray();
            var mesh = cape.cloth.GetComponent<SkinnedMeshRenderer>().sharedMesh;
            var vertices = mesh.vertices; var edges = cape.guideEdgeVertices;
            edgeLengths = cape.guideEdgeRestLengths;
            edgeParticles = new int[edges.Length];
            var matched = new Dictionary<int, int>();
            for (int i = 0; i < edges.Length; i++)
            {
                if (!matched.TryGetValue(edges[i], out int nearest))
                {
                    float best = float.PositiveInfinity; nearest = -1;
                    for (int p = 0; p < cape.restParticles.Length; p++)
                    {
                        float distance = (vertices[edges[i]] - cape.restParticles[p]).sqrMagnitude;
                        if (distance < best) { best = distance; nearest = p; }
                    }
                    if (nearest < 0 || best > 1e-6f) throw new InvalidOperationException("Cannot map cloth edge to welded particles.");
                    matched.Add(edges[i], nearest);
                }
                edgeParticles[i] = nearest;
            }
        }

        static void BeginStage(int index)
        {
            stage = index; settleTime = stageTime = 0; targetIndex = 0; startedMovement = targetsArmed = measuring = false;
            lastFrame = -1; pending = false; previousNormalized = -1;
            int fps = !speedMode ? CaptureFrameRate : stage >= 8 ? 0 : stage >= 4 ? 10 : 60;
            Time.captureFramerate = fps;
            if (speedMode)
            {
                bool newer = stage >= 8 || stage % 4 >= 2;
                player.walkSpeed = newer ? 2.25f : 1.5f; player.runSpeed = newer ? 4.5f : 3;
                speedSample = new SpeedSample { fps = fps, preset = newer ? "new" : "old", action = stage % 2 == 0 ? "Walk" : "Run",
                    configuredSpeed = stage % 2 == 0 ? player.walkSpeed : player.runSpeed };
            }
            player.ResetPosition(); motor.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(0, FloorY + .025f, 0), Quaternion.identity);
            motor.enabled = true; Physics.SyncTransforms();
            player.SetScriptedInput(Vector2.zero, false, false);
            animator.Play("Idle", 0, 0); animator.Update(0);
            cape.ResetSimulation(); WaveField?.SetValue(cape, 0f);
            player.secondaryMotion?.ResetSimulation();
        }

        static void Sample()
        {
            if (!active || pending || !EditorApplication.isPlaying || EditorApplication.isPaused || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try
            {
                settleTime += Time.deltaTime;
                if (settleTime < .5f) return;
                bool running = speedMode ? stage % 2 == 1 : stage == 1;
                string action = running ? "Run" : "Walk";
                if (!startedMovement)
                {
                    player.SetScriptedInput(Vector2.up, running, false); startedMovement = true; stageTime = 0; return;
                }
                stageTime += Time.deltaTime;
                if (stageTime < WarmupSeconds) return;
                if (speedMode) { SampleSpeed(); return; }
                var info = animator.GetCurrentAnimatorStateInfo(0);
                if (!info.IsName(action) || animator.IsInTransition(0)) return;
                float normalized = info.normalizedTime;
                if (!targetsArmed)
                {
                    cycleStart = Mathf.Floor(normalized) + 1; previousNormalized = normalized; targetsArmed = true; return;
                }
                float target = cycleStart + Phases[stage][targetIndex];
                if (previousNormalized < target && normalized >= target)
                {
                    capturedDeltaTime = Time.deltaTime;
                    pending = true; EditorApplication.isPaused = true;
                    EditorApplication.delayCall += CapturePending;
                }
                previousNormalized = normalized;
                if (stageTime > 20) throw new TimeoutException("Target gait phase did not arrive.");
            }
            catch (Exception e) { Finish("FAIL", e.ToString()); }
        }

        static void SampleSpeed()
        {
            if (!measuring)
            {
                speedSample.start = previousMeasuredPosition = player.transform.position;
                measureRealtimeStarted = Time.realtimeSinceStartupAsDouble; measuring = true; return;
            }
            Vector3 now = player.transform.position;
            speedSample.horizontalDistance += Vector3.ProjectOnPlane(now - previousMeasuredPosition, Vector3.up).magnitude;
            previousMeasuredPosition = now; speedSample.seconds += Time.deltaTime; speedSample.frames++;
            speedSample.minDeltaTime = Mathf.Min(speedSample.minDeltaTime, Time.deltaTime);
            speedSample.maxDeltaTime = Mathf.Max(speedSample.maxDeltaTime, Time.deltaTime);
            if (speedSample.seconds + 1e-5f < MeasureSeconds) return;
            speedSample.end = now; speedSample.displacement = Vector3.ProjectOnPlane(now - speedSample.start, Vector3.up).magnitude;
            speedSample.dtElapsed = speedSample.seconds;
            speedSample.realtimeElapsed = (float)(Time.realtimeSinceStartupAsDouble - measureRealtimeStarted);
            speedSample.measuredSpeed = speedSample.displacement / speedSample.seconds;
            speedSample.measuredRealtimeSpeed = speedSample.displacement / Mathf.Max(.00001f, speedSample.realtimeElapsed);
            speedSample.speedRatio = speedSample.measuredSpeed / speedSample.configuredSpeed;
            if (speedSample.preset == "new")
            {
                var old = report.speeds.SingleOrDefault(s => s.fps == speedSample.fps && s.preset == "old" && s.action == speedSample.action);
                speedSample.comparisonAvailable = old != null;
                if (old != null) speedSample.newToOldRatio = speedSample.measuredSpeed / old.measuredSpeed;
            }
            report.speeds.Add(speedSample); SaveReport();
            if (stage == 9) Finish("COMPLETE", null); else BeginStage(stage + 1);
        }

        static void CapturePending()
        {
            if (!active || !pending) return;
            try
            {
                var info = animator.GetCurrentAnimatorStateInfo(0);
                var particles = cape.cloth.vertices;
                var world = particles.Select(cape.cloth.transform.TransformPoint).ToArray();
                if (world.Length == 0 || world.Any(v => !Finite(v))) throw new InvalidOperationException("Non-finite or empty completed cloth.");
                Bounds bounds = new Bounds(world[0], Vector3.zero); foreach (var v in world) bounds.Encapsulate(v);
                var ratios = new float[edgeLengths.Length];
                for (int i = 0; i < ratios.Length; i++) ratios[i] = Vector3.Distance(world[edgeParticles[2 * i]], world[edgeParticles[2 * i + 1]]) / edgeLengths[i];
                Array.Sort(ratios);
                var matrices = boots.bones.Select((b, i) => b.localToWorldMatrix * bootBindposes[i]).ToArray();
                float leftSole = float.PositiveInfinity, rightSole = float.PositiveInfinity;
                for (int i = 0; i < bootVertices.Length; i++)
                {
                    var w = bootWeights[i]; var v = bootVertices[i];
                    Vector3 p = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                        + matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
                    if (Weight(w, leftBones) > .25f) leftSole = Mathf.Min(leftSole, p.y - FloorY);
                    if (Weight(w, rightBones) > .25f) rightSole = Mathf.Min(rightSole, p.y - FloorY);
                }
                Vector3 Local(Transform bone) => player.transform.InverseTransformPoint(bone.position);
                string action = stage == 0 ? "Walk" : "Run", prefix = action + "-" + (targetIndex + 1).ToString("D2");
                var frame = new Frame { action = action, state = player.CurrentState, referenceFrame = targetIndex + 1,
                    unityFrame = Time.frameCount, targetPhase = Phases[stage][targetIndex], actualPhase = Mathf.Repeat(info.normalizedTime, 1),
                    normalizedTime = info.normalizedTime, phaseError = info.normalizedTime - cycleStart - Phases[stage][targetIndex],
                    time = Time.time, stageTime = stageTime, deltaTime = capturedDeltaTime, grounded = player.IsGrounded, finite = true,
                    root = player.transform.position, hips = Local(hips), leftFoot = Local(leftFoot), rightFoot = Local(rightFoot),
                    leftHand = Local(leftHand), rightHand = Local(rightHand), leftSole = leftSole, rightSole = rightSole,
                    wavePhase = WaveField == null ? -1 : (float)WaveField.GetValue(cape), flowAmount = cape.FlowAmount,
                    wingSpread = cape.WingSpreadDegrees, guideTangent = (float[])cape.GuideTangentDegrees.Clone(),
                    stretchP95 = cape.GuideTargetStretchP95, stretchMaximum = cape.GuideTargetStretchMaximum,
                    clothStretchP95 = ratios[(int)((ratios.Length - 1) * .95f)], clothStretchMaximum = ratios.Last(),
                    clothBoundsCenter = bounds.center, clothBoundsSize = bounds.size,
                    images = new[] { prefix + "-side.png", prefix + "-rear3q.png", prefix + "-front3q.png" } };
                Render(frame.images[0], 90); Render(frame.images[1], 45); Render(frame.images[2], 135);
                report.frames.Add(frame); SaveReport();
                targetIndex++; pending = false;
                if (targetIndex == Phases[stage].Length)
                {
                    if (stage == 1) { Finish("COMPLETE", null); return; }
                    BeginStage(stage + 1);
                }
                EditorApplication.isPaused = false;
            }
            catch (Exception e) { Finish("FAIL", e.ToString()); }
        }

        static float Weight(BoneWeight w, bool[] selected) => (selected[w.boneIndex0] ? w.weight0 : 0) +
            (selected[w.boneIndex1] ? w.weight1 : 0) + (selected[w.boneIndex2] ? w.weight2 : 0) + (selected[w.boneIndex3] ? w.weight3 : 0);
        static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        static void Render(string filename, float yaw)
        {
            Vector3 target = player.transform.position + Vector3.up * .84f;
            captureCamera.transform.position = target + Quaternion.Euler(5, yaw, 0) * Vector3.back * 4;
            captureCamera.transform.LookAt(target);
            var rt = new RenderTexture(900, 900, 24); var pixels = new Texture2D(900, 900, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            try
            {
                captureCamera.targetTexture = rt; captureCamera.aspect = 1; captureCamera.Render(); RenderTexture.active = rt;
                pixels.ReadPixels(new Rect(0, 0, 900, 900), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(output, filename), pixels.EncodeToPNG());
            }
            finally { captureCamera.targetTexture = null; RenderTexture.active = old; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(pixels); }
        }

        static bool InsertMarker(ref PlayerLoopSystem loop)
        {
            if (loop.subSystemList == null) return false;
            var children = loop.subSystemList.ToList();
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i].type == typeof(UnityEngine.PlayerLoop.PostLateUpdate.PhysicsSkinnedClothFinishUpdate))
                {
                    children.Insert(i + 1, new PlayerLoopSystem { type = typeof(ReviewFrameEnd), updateDelegate = Sample });
                    loop.subSystemList = children.ToArray(); return true;
                }
                var child = children[i]; if (InsertMarker(ref child)) { children[i] = child; loop.subSystemList = children.ToArray(); return true; }
            }
            return false;
        }
        static void RemoveMarker(ref PlayerLoopSystem loop)
        {
            if (loop.subSystemList == null) return;
            var children = loop.subSystemList.Where(s => s.type != typeof(ReviewFrameEnd)).ToArray();
            for (int i = 0; i < children.Length; i++) RemoveMarker(ref children[i]);
            loop.subSystemList = children;
        }

        static void Finish(string result, string error)
        {
            if (!active) return;
            active = false; pending = false; EditorApplication.delayCall -= CapturePending;
            report.result = result; report.error = error;
            try { Restore(); report.restorationCompleted = true; }
            catch (Exception e) { report.result = "FAIL"; report.error = (error ?? "") + "\nRESTORE: " + e; }
            SaveReport(); Respond(request, report.result, report.error ?? "Review complete; original runtime scene and controls restored.");
        }

        static bool savedCapeDiagnostics;

        static void Restore()
        {
            if (installed)
            {
                var loop = PlayerLoop.GetCurrentPlayerLoop(); RemoveMarker(ref loop); PlayerLoop.SetPlayerLoop(loop); installed = false;
            }
            if (!snapshotReady) return;
            if (cape) cape.DiagnosticsEnabled = savedCapeDiagnostics;
            try
            {
                foreach (var state in layers) if (state.gameObject) state.gameObject.layer = state.layer;
                if (player && motor)
                {
                    player.ReleaseScriptedInput(); motor.enabled = false;
                    player.transform.SetPositionAndRotation(savedPosition, savedRotation);
                    foreach (var field in playerFields) field.Key.SetValue(player, field.Value);
                    motor.enabled = savedMotorEnabled; player.enabled = savedPlayerEnabled;
                    foreach (var p in parameters)
                    {
                        if (p.parameter.type == AnimatorControllerParameterType.Float) animator.SetFloat(p.parameter.nameHash, p.number);
                        if (p.parameter.type == AnimatorControllerParameterType.Int) animator.SetInteger(p.parameter.nameHash, p.integer);
                        if (p.parameter.type == AnimatorControllerParameterType.Bool) animator.SetBool(p.parameter.nameHash, p.flag);
                    }
                    animator.Play(savedStateHash, 0, savedNormalizedTime); animator.Update(0);
                    animator.speed = savedAnimatorSpeed; animator.cullingMode = savedCulling;
                    player.secondaryMotion?.ResetSimulation();
                    cape.ResetSimulation(); cape.SetWindPreview(savedWindPreview); cape.cloth.enabled = savedClothEnabled;
                    WaveField?.SetValue(cape, savedWavePhase); Physics.SyncTransforms();
                }
            }
            finally
            {
                foreach (var state in drivers) if (state.component) state.component.enabled = state.enabled;
                foreach (var state in cameras) if (state.camera) state.camera.enabled = state.enabled;
                if (temporaryRoot) Object.DestroyImmediate(temporaryRoot);
                if (groundMaterial) Object.DestroyImmediate(groundMaterial);
                Time.captureFramerate = savedCaptureRate; Time.fixedDeltaTime = savedFixedDelta; Time.timeScale = savedTimeScale;
                QualitySettings.vSyncCount = savedVSync; Application.targetFrameRate = savedTargetRate; Application.runInBackground = savedBackground;
                EditorApplication.isPaused = savedPaused; snapshotReady = false;
            }
        }
        static void SaveReport()
        {
            report.utc = DateTime.UtcNow.ToString("O"); File.WriteAllText(Path.Combine(output, "result.json"), JsonUtility.ToJson(report, true));
        }
        static void Respond(Request value, string status, string message)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, "response.json"), JsonUtility.ToJson(new Response {
                id = value.id, status = status, message = message, output = output, utc = DateTime.UtcNow.ToString("O") }, true));
        }
    }
}
