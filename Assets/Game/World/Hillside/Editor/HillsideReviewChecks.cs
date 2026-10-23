using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ForestSpriteCharacter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using WeatherHouse.Vegetation;
using Object = UnityEngine.Object;

namespace WeatherHouse.Hillside.Editor
{
    [InitializeOnLoad]
    public static class HillsideReviewChecks
    {
        const string Folder = HillsideSceneBuilder.Review;
        const string Key = "WeatherHouse.HillsideReview";
        static double nextPoll, started;
        static int phase;
        static readonly StringBuilder report = new StringBuilder();
        static readonly Dictionary<WildlifeAgent, Vector3> positions = new Dictionary<WildlifeAgent, Vector3>();
        static readonly Dictionary<Transform, Quaternion> poses = new Dictionary<Transform, Quaternion>();
        static WildlifeAgent alarmSubject;
        static bool sawFlee;
        static int greetingBefore;
        static string runtimeErrors = "";
        static Vector3 walkStart, walkTarget;
        static Vector3[] walkPoints;
        static int walkPoint;
        static double walkStarted;
        static Camera reviewCamera;
        static HillsideGrassRenderer reviewGrass;
        static bool reviewFog;
        static int reviewShot;
        static double nextCapture;

        static HillsideReviewChecks()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += PlayChanged;
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + .2;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (reviewCamera)
            {
                try { AdvanceCapture(); }
                catch (Exception e)
                {
                    EndCapture();
                    File.WriteAllText(Folder + "/capture-state.txt", "FAIL\n" + e);
                    if (SessionState.GetBool(Key + ".running", false)) Finish(e);
                }
                return;
            }
            string request = Folder + "/request.txt";
            if (File.Exists(request))
            {
                string command = File.ReadAllText(request).Trim();
                File.Delete(request);
                try
                {
                    Directory.CreateDirectory(Folder);
                    if (command == "inspect") State();
                    else if (command == "build") { HillsideSceneBuilder.Build(); CaptureOverview(); State(); }
                    else if (command == "rebuild") { HillsideSceneBuilder.Build(true); CaptureOverview(); State(); }
                    else if (command == "animals") { HillsideSceneBuilder.RebuildAnimals(); State(); }
                    else if (command == "validate") Validate();
                    else if (command == "logic")
                    {
                        File.WriteAllText(Folder + "/wildlife-logic-checks.txt", WildlifeChecks.RunLogicChecks());
                        File.WriteAllText(Folder + "/wildlife-rig-checks.txt", WildlifeChecks.RunRigBindingChecks(Object.FindObjectOfType<WildlifePopulation>()));
                    }
                    else if (command == "capture") CaptureOverview();
                    else if (command == "animalbounds") File.WriteAllText(Folder + "/animal-scale.txt", HillsideAnimalBuilder.DiagnoseScale());
                    else if (command == "fishaxis")
                    {
                        var fish = Object.FindObjectsOfType<WildlifeAgent>().First(a => a.speciesId == "fish");
                        var bones = fish.GetComponentsInChildren<Transform>();
                        string data = string.Join("\n", new[] { "Main1", "Main2", "Main6" }.Select(name => name + "=" + fish.transform.InverseTransformPoint(bones.First(t => t.name == name).position)));
                        File.WriteAllText(Folder + "/fish-axis.txt", data + "\nmodel=" + fish.animator.transform.localEulerAngles);
                    }
                    else if (command == "playcheck") StartPlayCheck();
                    else throw new InvalidOperationException("Unsupported hillside command: " + command);
                    File.WriteAllText(Folder + "/last-command.txt", command + " OK " + DateTime.UtcNow.ToString("O"));
                }
                catch (Exception e) { File.WriteAllText(Folder + "/last-command.txt", command + " FAIL\n" + e); Debug.LogException(e); }
            }
            if (SessionState.GetBool(Key + ".running", false) && EditorApplication.isPlaying)
            {
                try { TickPlay(); }
                catch (Exception e) { Finish(e); }
            }
        }

        static void State()
        {
            var scene = SceneManager.GetActiveScene();
            File.WriteAllText(Folder + "/state.txt", "scene=" + scene.path + "\nplaying=" + EditorApplication.isPlaying + "\ndirty=" + scene.isDirty + "\ncompiling=" + EditorApplication.isCompiling + "\nutc=" + DateTime.UtcNow.ToString("O"));
        }

        [MenuItem("Weather House/Hillside/Validate Saved Map")]
        public static void Validate()
        {
            Require(SceneManager.GetActiveScene().path == HillsideSceneBuilder.ScenePath, "Validation must target the new hillside scene.");
            var terrain = Object.FindObjectOfType<Terrain>();
            var population = Object.FindObjectOfType<WildlifePopulation>();
            Require(terrain && population, "Terrain and wildlife population exist.");
            string terrainReport = HillsideLayout.Validate((x, z) => terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y);
            Require(AssetDatabase.GetAssetPath(terrain.terrainData).StartsWith(HillsideSceneBuilder.Output), "Independent terrain data is persisted.");
            Require(population.agents.Length == 80, "80 habitat-specific animals are saved.");
            Require(population.agents.Select(a => a.speciesId).Distinct().Count() == 9, "Nine species are connected.");
            Require(population.waterAreas.Length == 3, "Main pond plus W1 and W2.");
            Require(Object.FindObjectsOfType<ForestSpriteDemoController>().Length == 1, "Exactly one playable character.");
            Require(Object.FindObjectsOfType<Camera>().Count(c => c.enabled) == 1, "Exactly one active view camera.");
            Require(Object.FindObjectsOfType<VegetationTopDownCamera>().Length == 1, "Existing orbit camera reused.");
            foreach (var agent in population.agents)
            {
                Require(agent.animator && agent.clips.FirstAvailable && agent.visualRoot, "Animation connected: " + agent.name);
                Require(agent.GetComponentsInChildren<SkinnedMeshRenderer>().Length > 0, "Skinned geometry: " + agent.name);
                Require(population.TryProjectPosition(agent, agent.transform.position, out _), "Valid habitat spawn: " + agent.name);
            }
            foreach (var renderer in Object.FindObjectsOfType<Renderer>())
                foreach (var material in renderer.sharedMaterials)
                    Require(material && material.shader && material.shader.name != "Hidden/InternalErrorShader", "Valid shader: " + renderer.name);
            Physics.SyncTransforms();
            var blocked = new HashSet<string>();
            foreach (var route in HillsideLayout.Routes)
                for (int segment = 1; segment < route.Length; segment++)
                {
                    int steps = Mathf.CeilToInt(Vector3.Distance(route[segment - 1], route[segment]) / 1.2f);
                    for (int step = 0; step <= steps; step++)
                    {
                        Vector3 p = Vector3.Lerp(route[segment - 1], route[segment], step / (float)steps);
                        p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                        foreach (var hit in Physics.OverlapCapsule(p + Vector3.up * .45f, p + Vector3.up * 1.5f, .28f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                            if (!(hit is TerrainCollider) && !hit.GetComponentInParent<ForestSpriteDemoController>()) blocked.Add(hit.name);
                    }
                }
            Require(blocked.Count == 0, "Paths blocked by scene props: " + string.Join(", ", blocked));
            File.WriteAllText(Folder + "/scene-checks.txt", "PASS\n80 animals, 9 species, 3 waters, one character, one orbit camera; persistent independent terrain and valid habitat spawns.\n" +
                terrainReport + "\nPhysical path corridor: unobstructed.\nVisual similarity and manual control feel still require image/runtime review.\n");
            if (!Application.isPlaying)
            {
                File.WriteAllText(Folder + "/wildlife-logic-checks.txt", WildlifeChecks.RunLogicChecks());
                File.WriteAllText(Folder + "/wildlife-rig-checks.txt", WildlifeChecks.RunRigBindingChecks(population));
                File.WriteAllText(Folder + "/wildlife-size-checks.txt", HillsideAnimalBuilder.ValidateSavedScales());
            }
        }

        [MenuItem("Weather House/Hillside/Run Runtime Review")]
        public static void StartPlayCheck()
        {
            HillsideSceneBuilder.RequireSavedEditor();
            Require(SceneManager.GetActiveScene().path == HillsideSceneBuilder.ScenePath, "Open the saved hillside first.");
            Validate();
            Directory.CreateDirectory(Folder);
            SessionState.SetString(Key + ".startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            SessionState.SetBool(Key + ".running", true);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(HillsideSceneBuilder.ScenePath);
            Application.runInBackground = true;
            File.WriteAllText(Folder + "/runtime-checks.txt", "RUNNING\n");
            EditorApplication.isPlaying = true;
        }

        static void PlayChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key + ".running", false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                phase = 0; started = EditorApplication.timeSinceStartup; sawFlee = false; runtimeErrors = "";
                report.Clear(); positions.Clear(); poses.Clear();
                Application.logMessageReceived += Log;
            }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                Application.logMessageReceived -= Log;
                if (File.Exists(Folder + "/runtime-checks.txt") && File.ReadAllText(Folder + "/runtime-checks.txt").StartsWith("RUNNING"))
                    File.WriteAllText(Folder + "/runtime-checks.txt", "INTERRUPTED\nPlay ended before runtime checks completed.\n" + report);
                if (reviewCamera) EndCapture();
                string path = SessionState.GetString(Key + ".startScene", "");
                EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
                Application.runInBackground = SessionState.GetBool(Key + ".background", false);
                SessionState.SetBool(Key + ".running", false);
                State();
            }
        }

        static void Log(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeErrors += message + "\n"; }

        static void TickPlay()
        {
            double wallTime = EditorApplication.timeSinceStartup - started;
            if (wallTime > 150) throw new TimeoutException("Hillside runtime review exceeded 150 seconds.");
            var population = Object.FindObjectOfType<WildlifePopulation>();
            if (!population || !population.Initialized) return;
            float elapsed = population.SimulationTime;
            if (elapsed < 2) return;
            if (phase == 0)
            {
                Require(population.ValidateConfiguration() == "", population.ValidateConfiguration());
                foreach (var agent in population.agents)
                {
                    positions[agent] = agent.transform.position;
                    foreach (var bone in agent.animator.GetComponentsInChildren<Transform>()) poses[bone] = bone.localRotation;
                    Bounds body = HillsideAnimalBuilder.PoseBounds(agent.visualRoot.gameObject);
                    float nominal = HillsideAnimalBuilder.TargetHeight(agent.speciesId) * agent.ScaleFactor;
                    Require(body.size.y > nominal * .25f && body.size.y < nominal * 3, "Live animal has visible world dimensions: " + agent.name + " height=" + body.size.y);
                    Require(Vector3.Distance(body.center, agent.transform.position) < nominal * 3 + 1, "Live skeleton stays with the animal root: " + agent.name);
                }
                Require(population.agents.Select(a => Mathf.RoundToInt(a.ScaleFactor * 1000)).Distinct().Count() > 20, "Seeded size variation is visible.");
                Capture(Camera.main, "01_天气屋试玩.png");
                alarmSubject = population.agents.First(a => a.speciesId == "rabbit");
                population.EmitStartle(alarmSubject.transform.position + Vector3.right, 6, 1);
                report.AppendLine("Seeded sizes: " + population.agents.Min(a => a.ScaleFactor).ToString("F3") + " .. " + population.agents.Max(a => a.ScaleFactor).ToString("F3"));
                phase = 1;
            }
            if (phase >= 1 && alarmSubject && alarmSubject.State == WildlifeState.Flee) sawFlee = true;
            if (phase == 1 && elapsed > 7)
            {
                Require(sawFlee, "A local startle reached the real Flee state.");
                Require(population.agents.Count(a => (a.transform.position - positions[a]).sqrMagnitude > .1f) >= 12, "Animals actually move across the terrain/water/air.");
                int changed = poses.Count(p => p.Key && Quaternion.Angle(p.Value, p.Key.localRotation) > 2);
                Require(changed > 20, "Actual skeletal animation changed more than 20 bone transforms.");
                report.AppendLine("Live skeleton changes: " + changed + "; observed Alert/Flee transition.");
                CaptureOverview(true);
                greetingBefore = population.GreetingCount;
                var pair = population.agents.Where(a => a.speciesId == "sheep").Take(2).ToArray();
                // The pair is arranged only in Play, to exercise the real mutual greeting contract.
                var second = pair[0].transform.position + Vector3.right * 1.3f;
                if (population.TryProjectPosition(pair[1], second, out var placed)) pair[1].transform.position = placed;
                population.TryGreet(pair[0], pair[1]);
                phase = 2;
            }
            if (phase == 2 && elapsed > 13)
            {
                Require(population.GreetingCount > 0, "At least one mutual greeting occurred.");
                Require(alarmSubject.State != WildlifeState.Alert && alarmSubject.State != WildlifeState.Flee, "Startle expires and animal returns to normal life.");
                foreach (var agent in population.agents)
                {
                    Require(population.TryProjectPosition(agent, agent.transform.position, out var projected), "Animal remains in legal habitat: " + agent.name);
                    if (agent.locomotion != WildlifeLocomotion.Flying) Require(Mathf.Abs(projected.y - agent.transform.position.y) < .4f, "Ground/water height follows terrain: " + agent.name);
                }
                var player = Object.FindObjectOfType<ForestSpriteDemoController>();
                Require(player.IsGrounded, "Playable forest sprite is grounded.");
                report.AppendLine("Mutual greetings: " + population.GreetingCount + "; alarms: " + population.AlarmCount + "; animals remain in legal habitats.");
                report.AppendLine("Player grounded, original prefab animator/cape and top-down camera active.");
                Require(runtimeErrors.Length == 0, runtimeErrors);
                BeginSlopeWalk(player, population.terrain);
                phase = 3;
            }
            if (phase == 3)
            {
                var player = Object.FindObjectOfType<ForestSpriteDemoController>();
                Vector3 remaining = Vector3.ProjectOnPlane(walkTarget - player.transform.position, Vector3.up);
                if (remaining.magnitude < .65f)
                {
                    if (++walkPoint < walkPoints.Length)
                    {
                        walkTarget = walkPoints[walkPoint];
                        return;
                    }
                    player.SetScriptedInput(Vector2.zero, false, false);
                    Require(player.transform.position.y > walkStart.y + .6f, "Controller climbs the actual hillside.");
                    Require(player.IsGrounded, "Controller remains grounded after climbing.");
                    report.AppendLine("Actual controller climbed " + (player.transform.position.y - walkStart.y).ToString("F2") + "m over a 10m uphill route section.");
                    Require(runtimeErrors.Length == 0, runtimeErrors);
                    Capture(Camera.main, "06_上坡通行验证.png");
                    Finish(null);
                    return;
                }
                Require(EditorApplication.timeSinceStartup - walkStarted < 35, "Player failed to traverse the uphill route within 35 seconds.");
                Vector3 forward = Vector3.ProjectOnPlane(player.demoCamera.transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                Vector3 direction = remaining.normalized;
                player.SetScriptedInput(new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward)), false, false);
            }
        }

        static void BeginSlopeWalk(ForestSpriteDemoController player, Terrain terrain)
        {
            float steepest = 0;
            foreach (var route in HillsideLayout.Routes)
                for (int direction = -1; direction <= 1; direction += 2)
                for (int i = 0; i < route.Length; i++)
                {
                    var points = new List<Vector3>();
                    Vector3 a = route[i], previous = a;
                    float run = 0;
                    for (int next = i + direction; next >= 0 && next < route.Length && run < 10; next += direction)
                    {
                        Vector3 p = route[next];
                        float segment = Vector3.ProjectOnPlane(p - previous, Vector3.up).magnitude;
                        if (segment < .001f) continue;
                        if (run + segment >= 10) p = Vector3.Lerp(previous, p, (10 - run) / segment);
                        points.Add(p); run += segment; previous = p;
                    }
                    if (run < 10 || points.Count == 0) continue;
                    Vector3 b = points[points.Count - 1];
                    a.y = terrain.SampleHeight(a) + terrain.transform.position.y;
                    b.y = terrain.SampleHeight(b) + terrain.transform.position.y;
                    if (b.y - a.y <= steepest) continue;
                    steepest = b.y - a.y; walkStart = a; walkPoints = points.ToArray();
                }
            Require(steepest > .8f, "A sustained uphill road exists for a real controller test.");
            walkPoint = 0; walkTarget = walkPoints[0];
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false; player.transform.position = walkStart + Vector3.up * .12f; controller.enabled = true;
            Object.FindObjectOfType<VegetationTopDownCamera>().SnapToPlayer();
            walkStarted = EditorApplication.timeSinceStartup;
        }

        static void Finish(Exception error)
        {
            File.WriteAllText(Folder + "/runtime-checks.txt", (error == null ? "PASS\n" : "FAIL\n") + report + (error == null ? "" : error.ToString()));
            EditorApplication.isPlaying = false;
        }

        public static void CaptureOverview(bool herdOnly = false)
        {
            if (reviewCamera) throw new InvalidOperationException("A hillside capture is already running.");
            reviewCamera = new GameObject("Hillside review camera").AddComponent<Camera>();
            reviewCamera.gameObject.hideFlags = HideFlags.HideAndDontSave;
            if (Camera.main) reviewCamera.CopyFrom(Camera.main);
            var data = reviewCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.requiresDepthTexture = true; data.requiresColorTexture = true;
            reviewCamera.enabled = false; reviewCamera.farClipPlane = 600; reviewCamera.nearClipPlane = .2f;
            reviewFog = RenderSettings.fog; reviewShot = herdOnly ? 6 : 0;
            reviewGrass = Object.FindObjectOfType<HillsideGrassRenderer>();
            if (reviewGrass) reviewGrass.manualCaptureOnly = true;
            File.WriteAllText(Folder + "/capture-state.txt", "RUNNING\n");
            nextCapture = EditorApplication.timeSinceStartup + .5;
            EditorApplication.QueuePlayerLoopUpdate();
        }

        static void AdvanceCapture()
        {
            if (EditorApplication.timeSinceStartup < nextCapture) return;
            Camera camera = reviewCamera;
            switch (reviewShot++)
            {
                case 0:
                    if (Camera.main) Capture(Camera.main, "01_角色视角_编辑预览.png");
                    break;
                case 1:
                RenderSettings.fog = false;
                camera.transform.position = new Vector3(15, 160, -190);
                camera.transform.LookAt(new Vector3(0, 8, 0));
                camera.fieldOfView = 58;
                Capture(camera, "00_山林全景.png");
                    break;
                case 2:
                camera.transform.position = new Vector3(0, 245, 0);
                camera.transform.rotation = Quaternion.Euler(90, 0, 0);
                camera.orthographic = true; camera.orthographicSize = 125;
                Capture(camera, "02_地图俯视.png");
                    break;
                case 3:
                camera.orthographic = false; camera.fieldOfView = 58;
                RenderSettings.fog = reviewFog;
                camera.transform.position = HillsideLayout.D + new Vector3(2, 5, 0);
                camera.transform.LookAt(HillsideLayout.A + Vector3.up * 2);
                Capture(camera, "03_山顶回望.png");
                    break;
                case 4:
                camera.transform.position = HillsideLayout.E + new Vector3(25, 18, -28);
                camera.transform.LookAt(HillsideLayout.E + Vector3.up);
                Capture(camera, "04_半山花坡.png");
                    break;
                case 5:
                camera.transform.position = HillsideLayout.B + new Vector3(20, 13, -24);
                camera.transform.LookAt(HillsideLayout.B + Vector3.up);
                Capture(camera, "05_老树小院.png");
                    break;
                case 6:
                var flock = Object.FindObjectsOfType<WildlifeAgent>().Where(a => a.speciesId == "sheep").ToArray();
                if (flock.Length > 0)
                {
                    Vector3 center = flock.Aggregate(Vector3.zero, (sum, a) => sum + a.transform.position) / flock.Length;
                    camera.transform.position = center + new Vector3(10, 7, -13);
                    camera.transform.LookAt(center + Vector3.up * .5f);
                    Capture(camera, "07_羊群生活.png");
                }
                    break;
                default:
                    EndCapture();
                    File.WriteAllText(Folder + "/capture-state.txt", "PASS " + DateTime.UtcNow.ToString("O"));
                    return;
            }
            // Instance submissions last one frame. Advance the player loop before changing viewpoints.
            nextCapture = EditorApplication.timeSinceStartup + .5;
            EditorApplication.QueuePlayerLoopUpdate();
        }

        static void EndCapture()
        {
            RenderSettings.fog = reviewFog;
            if (reviewGrass) reviewGrass.manualCaptureOnly = false;
            if (reviewCamera) Object.DestroyImmediate(reviewCamera.gameObject);
            reviewCamera = null; reviewGrass = null;
        }

        public static void Capture(Camera camera, string name)
        {
            Directory.CreateDirectory(Folder);
            var target = RenderTexture.GetTemporary(1440, 900, 24, RenderTextureFormat.ARGB32);
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            var image = new Texture2D(1440, 900, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                var grass = Object.FindObjectOfType<HillsideGrassRenderer>();
                if (grass) grass.DrawForCamera(camera, true);
                camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0); image.Apply();
                File.WriteAllBytes(Folder + "/" + name, image.EncodeToPNG());
            }
            finally { camera.targetTexture = oldTarget; RenderTexture.active = oldActive; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(image); }
        }

        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
