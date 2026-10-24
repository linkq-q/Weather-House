using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    // Applies only to existing birds and this tool's butterfly groups; never rebuilds the map.
    public static class BirdsButterfliesEnhancement
    {
        const string Folder = "Assets/Environment/WatersideBirdsButterflies";
        const string ButterflyFolder = "Assets/Environment/DDriveLibrary/Butterflies/LP Butterflies";
        const string RootName = "09 花缘蝴蝶 · 成群起落";
        const string BirdVisual = "V3 Bird Visual";
        const string Review = "BirdsButterfliesReview";

        public static string Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != WatersideSceneBuilder.ScenePath)
                throw new InvalidOperationException("Open the saved WatersideV2 scene in edit mode.");
            var population = Object.FindObjectsByType<WildlifePopulation>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.gameObject.scene == scene && p.terrain && p.agents.Any(a => a && a.speciesId == "robin"));
            if (!population) throw new InvalidOperationException("The existing robin population was not found.");
            PrepareAssets();
            GameObject birdPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/ShoreBird.prefab");
            var birdClips = BirdClips();
            var butterflyClips = ButterflyClips();
            string preparation = CheckAssets(); // Fail before modifying any live scene objects.
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("水岸鸟群与花缘蝴蝶");
            int group = Undo.GetCurrentGroup();
            int birds = 0;
            foreach (var agent in population.agents.Where(a => a && a.speciesId == "robin"))
            {
                Undo.RecordObject(agent, "Replace bird visual and animations");
                Transform previous = agent.visualRoot;
                if (previous && previous.name != BirdVisual)
                { Undo.RecordObject(previous.gameObject, "Keep original bird visual"); previous.gameObject.SetActive(false); }
                Transform owned = agent.transform.Find(BirdVisual);
                if (owned) Undo.DestroyObjectImmediate(owned.gameObject);
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(birdPrefab, agent.transform);
                Undo.RegisterCreatedObjectUndo(visual, "Add articulated bird");
                visual.name = BirdVisual;
                agent.visualRoot = visual.transform;
                agent.motionRoot = null;
                agent.animator = visual.GetComponent<Animator>();
                agent.clips = birdClips;
                agent.walkSpeed = 1.65f; agent.runSpeed = 3.1f;
                agent.flightHeight = 5.5f; agent.bodyRadius = .13f;
                agent.scaleRange = new Vector2(.88f, 1.12f);
                agent.timidity = .8f; agent.sociability = .35f;
                PrefabUtility.RecordPrefabInstancePropertyModifications(agent);
                birds++;
            }
            GameObject previousRoot = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (previousRoot) Undo.DestroyObjectImmediate(previousRoot);
            var butterflyRoot = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(butterflyRoot, "Add butterfly flower patches");
            Vector3[] centers = { WatersideLayout.Entry + new Vector3(6,0,4), WatersideLayout.OldTree,
                WatersideLayout.WesternGrove, WatersideLayout.LakeTerrace, WatersideLayout.MoonBay + new Vector3(0,0,5) };
            int[] variants = { 1, 7, 13, 20, 33 };
            int butterflies = 0;
            for (int g = 0; g < centers.Length; g++)
            {
                var patch = new GameObject("花缘蝶群 " + (g + 1)).AddComponent<WildlifePopulation>();
                patch.transform.SetParent(butterflyRoot.transform, false);
                patch.terrain = population.terrain; patch.player = population.player; patch.waterSurface = population.waterSurface;
                patch.worldSeed = population.worldSeed + 371 + g;
                patch.worldBounds = new Bounds(centers[g], new Vector3(22, 120, 22));
                patch.maxSlope = 32; patch.decisionInterval = .3f;
                var members = new List<WildlifeAgent>();
                var random = new WildlifeRandom(patch.worldSeed, g, "butterfly");
                for (int i = 0; i < 4; i++)
                {
                    var holder = new GameObject("蝴蝶 " + (g + 1) + "-" + (i + 1));
                    holder.transform.SetParent(patch.transform, false);
                    var agent = holder.AddComponent<WildlifeAgent>();
                    agent.speciesId = "butterfly"; agent.spawnId = 5000 + g * 10 + i; agent.groupId = g;
                    agent.locomotion = WildlifeLocomotion.Flying;
                    agent.flightHeight = 1.3f; agent.walkSpeed = .62f; agent.runSpeed = 1.35f; agent.bodyRadius = .07f;
                    agent.scaleRange = new Vector2(.8f, 1.16f); agent.timidity = .7f; agent.sociability = .12f;
                    agent.habitatCenter = centers[g]; agent.habitatRadius = 5;
                    bool placed = false;
                    for (int attempt = 0; attempt < 100; attempt++)
                    {
                        Vector2 offset = random.InCircle(6);
                        Vector3 p = centers[g] + new Vector3(offset.x, 0, offset.y);
                        if (!patch.TryLandingPosition(agent, p, out p)) continue;
                        if (WatersideLayout.PathEdgeDistance(p.x, p.z) < .35f) continue;
                        p.y += i % 2 == 0 ? agent.flightHeight : 0;
                        holder.transform.position = p;
                        if (!patch.TryMove(agent, p, out _)) continue;
                        placed = true; break;
                    }
                    if (!placed) { Object.DestroyImmediate(holder); continue; }
                    holder.transform.rotation = Quaternion.Euler(0, random.Range(0, 360), 0);
                    var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                        ButterflyFolder + "/Prefab/Butterfly_" + variants[g] + ".prefab"), holder.transform);
                    visual.name = "Butterfly Visual"; visual.transform.localPosition = Vector3.up * .34f;
                    visual.transform.localRotation = Quaternion.identity; visual.transform.localScale = Vector3.one * .095f;
                    agent.visualRoot = visual.transform; agent.animator = visual.GetComponent<Animator>();
                    agent.animator.applyRootMotion = false; agent.animator.runtimeAnimatorController = null;
                    agent.clips = butterflyClips;
                    foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
                    { skin.updateWhenOffscreen = true; skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
                    butterflyClips.idle.SampleAnimation(visual, 0);
                    members.Add(agent); butterflies++;
                }
                patch.agents = members.ToArray();
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            string report = preparation + "\nAPPLIED birds=" + birds + ", butterflies=" + butterflies +
                ". Scene marked dirty; caller owns saving and visual/Play verification.";
            Directory.CreateDirectory(Review); File.WriteAllText(Review + "/apply-checks.txt", report);
            return report;
        }

        public static void PrepareAssets()
        {
            Directory.CreateDirectory(Folder);
            foreach (string file in new[] { "Bird.fbx", "BirdAlbedo.png", "LICENSE.txt" })
                File.Copy(Review + "/Source/" + file, Folder + "/" + file, true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Folder + "/Bird.fbx");
            importer.isReadable = true; importer.importAnimation = false; importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
            var preview = EditorSceneManager.NewPreviewScene();
            GameObject source = null, root = null, butterfly = null;
            try
            {
                source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Bird.fbx"));
                SceneManager.MoveGameObjectToScene(source, preview);
                var filter = source.GetComponentInChildren<MeshFilter>();
                if (!filter) throw new InvalidOperationException("Official bird source has no mesh.");
                root = new GameObject("水岸小鸟 · 骨骼扇翅"); SceneManager.MoveGameObjectToScene(root, preview);
                var animator = root.AddComponent<Animator>(); animator.applyRootMotion = false;
                var body = Bone("Body", root.transform, Vector3.zero);
                var left = Bone("LeftWing", body, new Vector3(-.045f, .15f, 0));
                var right = Bone("RightWing", body, new Vector3(.045f, .15f, 0));
                Mesh mesh = Object.Instantiate(filter.sharedMesh); mesh.name = "ShoreBirdSkin";
                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = source.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]));
                Bounds bounds = new Bounds(vertices[0], Vector3.zero);
                foreach (Vector3 v in vertices) bounds.Encapsulate(v);
                float scale = .78f / bounds.size.x;
                var weights = new BoneWeight[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = (vertices[i] - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)) * scale;
                    float wing = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.047f, .105f, Mathf.Abs(vertices[i].x)));
                    weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 - wing, boneIndex1 = vertices[i].x < 0 ? 1 : 2, weight1 = wing };
                }
                mesh.vertices = vertices; mesh.boneWeights = weights;
                Transform[] bones = { body, left, right };
                mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * root.transform.localToWorldMatrix).ToArray();
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                mesh = SaveAsset(mesh, Folder + "/ShoreBirdSkin.asset");
                var skin = root.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh; skin.bones = bones; skin.rootBone = body;
                skin.updateWhenOffscreen = true; skin.localBounds = new Bounds(new Vector3(0,.15f,0), new Vector3(1.1f,.9f,.9f));
                var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/ShoreBird.mat");
                if (!material) material = SaveAsset(new Material(Shader.Find("Universal Render Pipeline/Lit")), Folder + "/ShoreBird.mat");
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/BirdAlbedo.png"));
                material.SetColor("_BaseColor", new Color(.8f,.86f,.94f)); material.SetFloat("_Smoothness", .08f);
                material.SetFloat("_Cull", 0); EditorUtility.SetDirty(material); skin.sharedMaterial = material;
                BuildClips(root, left, right, "Bird", false);
                PrefabUtility.SaveAsPrefabAsset(root, Folder + "/ShoreBird.prefab");
                butterfly = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ButterflyFolder + "/Prefab/Butterfly_1.prefab"));
                SceneManager.MoveGameObjectToScene(butterfly, preview);
                Transform[] transforms = butterfly.GetComponentsInChildren<Transform>();
                left = transforms.Single(t => t.name == "Butterfly_Left_Jnt");
                right = transforms.Single(t => t.name == "Butterfly_Right_Jnt");
                BuildClips(butterfly, left, right, "Butterfly", true);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (source) Object.DestroyImmediate(source); if (root) Object.DestroyImmediate(root); if (butterfly) Object.DestroyImmediate(butterfly);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        static Transform Bone(string name, Transform parent, Vector3 position)
        { var bone = new GameObject(name).transform; bone.SetParent(parent, false); bone.localPosition = position; return bone; }

        static T SaveAsset<T>(T value, string path) where T : Object
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing) { EditorUtility.CopySerialized(value, existing); Object.DestroyImmediate(value); EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(value, path); return value;
        }

        static void BuildClips(GameObject root, Transform left, Transform right, string prefix, bool butterfly)
        {
            foreach (string state in new[] { "Idle", "Fly", "Takeoff", "Land" })
            {
                bool idle = state == "Idle";
                float duration = idle ? 3 : state == "Fly" ? (butterfly ? .24f : .72f) : 1.44f;
                int frames = Mathf.Max(16, Mathf.RoundToInt(duration * 60));
                var clip = new AnimationClip { name = prefix + "_" + state, frameRate = 60 };
                foreach (Transform bone in new[] { left, right })
                {
                    float sign = bone == left ? -1 : 1;
                    // The supplied butterfly's Left bone is on +X, unlike this bird's conventional -X left.
                    if (butterfly) sign = -sign;
                    var curves = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                    for (int i = 0; i <= frames; i++)
                    {
                        float time = i * duration / frames;
                        float phase = time * Mathf.PI * 2 / (butterfly ? .24f : .72f);
                        float angle = idle ? (butterfly ? 48 : 18) + Mathf.Sin(time * Mathf.PI * 2 / duration) * 3 : Mathf.Sin(phase) * (butterfly ? 48 : 34);
                        Quaternion q = Quaternion.AngleAxis(sign * angle, Vector3.forward) * bone.localRotation;
                        if (idle && !butterfly)
                        {
                            float fold = 72 + Mathf.Sin(time * Mathf.PI * 2 / duration) * 1.5f;
                            q = Quaternion.AngleAxis(sign * fold, Vector3.up) *
                                Quaternion.AngleAxis(-sign * 12, Vector3.forward) * bone.localRotation;
                        }
                        curves[0].AddKey(time,q.x); curves[1].AddKey(time,q.y); curves[2].AddKey(time,q.z); curves[3].AddKey(time,q.w);
                    }
                    string path = AnimationUtility.CalculateTransformPath(bone, root.transform);
                    string[] components = { "x", "y", "z", "w" };
                    for (int i = 0; i < 4; i++) clip.SetCurve(path, typeof(Transform), "m_LocalRotation." + components[i], curves[i]);
                }
                clip.EnsureQuaternionContinuity();
                var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = idle || state == "Fly";
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                SaveAsset(clip, Folder + "/" + prefix + "_" + state + ".anim");
            }
        }

        static WildlifeClips BirdClips() => Clips("Bird");
        static WildlifeClips ButterflyClips() => Clips("Butterfly");
        static WildlifeClips Clips(string prefix)
        {
            AnimationClip Load(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/" + prefix + "_" + name + ".anim");
            return new WildlifeClips { idle = Load("Idle"), forage = Load("Idle"), greet = Load("Idle"), alert = Load("Idle"),
                walk = Load("Fly"), run = Load("Fly"), fly = Load("Fly"), takeoff = Load("Takeoff"), land = Load("Land") };
        }

        public static string CheckAssets()
        {
            var report = new StringBuilder();
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (string kind in new[] { "Bird", "Butterfly" })
                {
                    string path = kind == "Bird" ? Folder + "/ShoreBird.prefab" : ButterflyFolder + "/Prefab/Butterfly_1.prefab";
                    var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                    SceneManager.MoveGameObjectToScene(model, preview);
                    try
                    {
                        WildlifeClips clips = Clips(kind);
                        if (!clips.fly || !clips.idle || !clips.takeoff || !clips.land) throw new InvalidOperationException(kind + " has missing state clips.");
                        var skin = model.GetComponentsInChildren<SkinnedMeshRenderer>()
                            .First(s => kind == "Bird" || s.name.Contains("Wings"));
                        var a = new Mesh(); var b = new Mesh();
                        try
                        {
                            clips.fly.SampleAnimation(model, clips.fly.length * .25f); skin.BakeMesh(a);
                            clips.fly.SampleAnimation(model, clips.fly.length * .75f); skin.BakeMesh(b);
                            float delta = 0; Vector3[] av = a.vertices, bv = b.vertices;
                            for (int i = 0; i < av.Length; i++) delta = Mathf.Max(delta, Vector3.Distance(av[i], bv[i]));
                            if (delta < .03f) throw new InvalidOperationException(kind + " wing mesh does not deform across flight frames: " + delta);
                            report.AppendLine("PASS " + kind + ": skinned wing vertex delta=" + delta.ToString("F4") + ", all four state clips bound.");
                            if (kind == "Bird")
                            {
                                clips.fly.SampleAnimation(model, 0); skin.BakeMesh(a); a.RecalculateBounds();
                                float openWidth = a.bounds.size.x;
                                clips.idle.SampleAnimation(model, 0); skin.BakeMesh(b); b.RecalculateBounds();
                                if (b.bounds.size.x >= openWidth * .7f || b.bounds.min.y < -.005f)
                                    throw new InvalidOperationException("Bird idle did not fold its wings safely above the footing.");
                                report.AppendLine("PASS bird idle folded span=" + b.bounds.size.x.ToString("F4") + " / open span=" + openWidth.ToString("F4"));
                            }
                        }
                        finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
                    }
                    finally { Object.DestroyImmediate(model); }
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            CheckStateCycle(report);
            CheckRobinFlock(report);
            report.AppendLine("Source Take 001 is a static track; generated clips deform the supplied butterfly wing bones.");
            report.AppendLine("Uses existing WildlifeAgent seed, safe landing, takeoff, neighbour alarms and player startle; this check does not replace in-Play visual acceptance.");
            Directory.CreateDirectory(Review); File.WriteAllText(Review + "/asset-checks.txt", report.ToString());
            return report.ToString();
        }

        static void CheckRobinFlock(StringBuilder report)
        {
            var preview = EditorSceneManager.NewPreviewScene();
            var holder = new GameObject("Robin shared route check"); SceneManager.MoveGameObjectToScene(holder, preview);
            try
            {
                var pop = holder.AddComponent<WildlifePopulation>();
                pop.worldBounds = new Bounds(new Vector3(10000, 50, 10000), new Vector3(100, 100, 100));
                var members = new List<WildlifeAgent>();
                for (int i = 0; i < 3; i++)
                {
                    var child = new GameObject("Robin " + i); child.transform.SetParent(holder.transform);
                    var agent = child.AddComponent<WildlifeAgent>();
                    agent.speciesId = "robin"; agent.groupId = 9; agent.spawnId = 48 + i;
                    agent.locomotion = WildlifeLocomotion.Flying; agent.flightHeight = 5.5f;
                    agent.walkSpeed = 1.65f; agent.runSpeed = 3.1f; agent.bodyRadius = .13f;
                    agent.habitatCenter = new Vector3(10000, 0, 10000); agent.habitatRadius = 18;
                    child.transform.position = agent.habitatCenter + new Vector3(i - 1, 5.5f, 0); members.Add(agent);
                }
                pop.agents = members.ToArray(); pop.Initialize(); pop.Simulate(.1f);
                Vector3 shared = pop.RobinFlightWaypoint(9, members[0].habitatCenter);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 delta = members[i].Destination - shared; delta.y = 0;
                    if (delta.magnitude > 1.71f) throw new InvalidOperationException("Robin cruise did not use its stable small offset around the shared waypoint.");
                }
                for (int i = 0; i < 10; i++) pop.Simulate(.1f);
                if (Vector3.Distance(shared, pop.RobinFlightWaypoint(9, Vector3.zero)) > .001f)
                    throw new InvalidOperationException("Robin group route expired before its intended lifetime.");
                WildlifeState neighbourState = members[1].State;
                if (!members[0].ReceiveStartle(new WildlifeAlarm { id = 72, origin = members[0].transform.position,
                    intensity = 1, radius = 4, expiresAt = pop.SimulationTime + 1 }, pop.SimulationTime) || members[1].State != neighbourState)
                    throw new InvalidOperationException("An individual robin alarm incorrectly replaced its neighbour's state.");
                for (int i = 0; i < 115; i++) pop.Simulate(.1f);
                if (Vector3.Distance(shared, pop.RobinFlightWaypoint(9, Vector3.zero)) < 1)
                    throw new InvalidOperationException("Robin group route did not renew after expiry.");
                report.AppendLine("PASS robin cruise: shared expiring waypoint, stable individual offsets, independent alarm state.");
            }
            finally { Object.DestroyImmediate(holder); EditorSceneManager.ClosePreviewScene(preview); }
        }

        static void CheckStateCycle(StringBuilder report)
        {
            var preview = EditorSceneManager.NewPreviewScene();
            var holder = new GameObject("Bird butterfly state check");
            SceneManager.MoveGameObjectToScene(holder, preview);
            try
            {
                var pop = holder.AddComponent<WildlifePopulation>();
                pop.worldBounds = new Bounds(new Vector3(10000, 50, 10000), new Vector3(22, 100, 22));
                var child = new GameObject("Butterfly decision check"); child.transform.SetParent(holder.transform);
                var agent = child.AddComponent<WildlifeAgent>();
                agent.locomotion = WildlifeLocomotion.Flying; agent.speciesId = "butterfly"; agent.spawnId = 5001;
                agent.flightHeight = 1.3f; agent.walkSpeed = .62f; agent.runSpeed = 1.35f; agent.bodyRadius = .07f;
                agent.habitatCenter = new Vector3(10000, 0, 10000); agent.habitatRadius = 5; agent.clips = ButterflyClips();
                child.transform.position = agent.habitatCenter + Vector3.up * agent.flightHeight;
                pop.agents = new[] { agent }; pop.Initialize();
                var states = new HashSet<WildlifeState>();
                for (int i = 0; i < 1800; i++) { pop.Simulate(.1f); states.Add(agent.State); }
                foreach (WildlifeState state in new[] { WildlifeState.Fly, WildlifeState.Landing, WildlifeState.Takeoff })
                    if (!states.Contains(state)) throw new InvalidOperationException("Butterfly did not enter " + state);
                if (!states.Contains(WildlifeState.Idle) && !states.Contains(WildlifeState.Forage))
                    throw new InvalidOperationException("Butterfly never rested.");
                bool received = agent.ReceiveStartle(new WildlifeAlarm { id = 100, origin = agent.transform.position,
                    intensity = 1, radius = 5, expiresAt = pop.SimulationTime + 2 }, pop.SimulationTime);
                bool fled = false;
                for (int i = 0; i < 40; i++) { pop.Simulate(.1f); fled |= agent.State == WildlifeState.Flee; }
                if (!received || !fled) throw new InvalidOperationException("Butterfly alarm did not produce a takeoff/flight escape.");
                report.AppendLine("PASS deterministic butterfly simulation: flight, landing, resting, takeoff and alarm escape.");
            }
            finally { Object.DestroyImmediate(holder); EditorSceneManager.ClosePreviewScene(preview); }
        }

        public static string Check()
        {
            string assets = CheckAssets();
            GameObject root = GameObject.Find(RootName);
            if (!root) throw new InvalidOperationException("Butterfly groups have not been applied.");
            var populations = root.GetComponentsInChildren<WildlifePopulation>();
            int count = 0;
            foreach (var pop in populations)
            {
                if (pop.agents.Length < 3) throw new InvalidOperationException("Too few butterflies in " + pop.name);
                string errors = pop.ValidateConfiguration(); if (!string.IsNullOrEmpty(errors)) throw new InvalidOperationException(errors);
                if (pop.worldBounds.size.x > 24 || pop.worldBounds.size.z > 24) throw new InvalidOperationException("Butterfly patch can wander beyond its flower region.");
                count += pop.agents.Length;
            }
            string result = assets + "\nPASS " + populations.Length + " bounded flower patches, " + count + " butterflies with safe placements.";
            File.WriteAllText(Review + "/scene-checks.txt", result); return result;
        }
    }
}
