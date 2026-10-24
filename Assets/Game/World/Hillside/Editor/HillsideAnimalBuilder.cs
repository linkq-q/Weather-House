using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using WeatherHouseSquirrelLab;
using Object = UnityEngine.Object;

namespace WeatherHouse.Hillside.Editor
{
    public static class HillsideAnimalBuilder
    {
        const string Root = "Assets/Experiments/QuadrupedMotion/EightAnimals";
        [Serializable] sealed class Entry { public string animal, state, model, texture, headBone, rootBone; public bool loop; }
        [Serializable] sealed class Manifest { public Entry[] clips; }
        static Entry[] Entries => JsonUtility.FromJson<Manifest>(File.ReadAllText(Root + "/manifest.json")).clips;

        public static void PrepareAssets()
        {
            foreach (var entry in Entries)
            {
                var importer = AssetImporter.GetAtPath(entry.model) as ModelImporter;
                if (!importer) throw new FileNotFoundException(entry.model);
                bool dirty = importer.animationType != ModelImporterAnimationType.Generic || importer.animationCompression != ModelImporterAnimationCompression.Off || importer.importCameras || importer.importLights || importer.optimizeGameObjects;
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.importCameras = false; importer.importLights = false; importer.optimizeGameObjects = false;
                var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
                foreach (var clip in clips)
                {
                    dirty |= clip.loopTime != entry.loop;
                    clip.loopTime = entry.loop;
                }
                if (importer.clipAnimations.Length == 0) dirty = true;
                if (dirty) { importer.clipAnimations = clips; importer.SaveAndReimport(); }
            }
        }

        public static WildlifePopulation Build(Terrain terrain, Transform player, string output)
        {
            var population = new GameObject("07 动物种群").AddComponent<WildlifePopulation>();
            population.terrain = terrain; population.player = player; population.worldSeed = 20261023;
            population.worldBounds = new Bounds(new Vector3(0, 22, 0), new Vector3(230, 100, 230));
            population.waterAreas = HillsideLayout.Waters.Select(w => new HabitatVolume {
                id = w.Name, center = new Vector3(w.Center.x, w.SurfaceY, w.Center.y), radii = w.Radii }).ToArray();
            var templates = new Dictionary<string, GameObject>();
            foreach (string species in new[] { "sheep", "deer", "dog", "fox", "rabbit", "fish", "robin", "turtle", "squirrel" })
                templates[species] = Template(species, output);
            var animals = new List<WildlifeAgent>();
            int group = 0;
            Spawn("dog", 3, 18, 84, 7, -1, templates, population, animals, ++group);
            Spawn("rabbit", 5, 14, 71, 8, -1, templates, population, animals, ++group);
            Spawn("squirrel", 6, 18, 47, 9, -1, templates, population, animals, ++group);
            Spawn("fox", 2, 10, 39, 6, -1, templates, population, animals, ++group);
            Spawn("rabbit", 6, 63, 44, 8, -1, templates, population, animals, ++group);
            Spawn("sheep", 10, 72, 40, 10, -1, templates, population, animals, ++group);
            Spawn("deer", 7, 79, 33, 13, -1, templates, population, animals, ++group);
            Spawn("deer", 4, 54, 23, 10, -1, templates, population, animals, ++group);
            Spawn("turtle", 3, 27, 63, 5, -1, templates, population, animals, ++group);
            Spawn("turtle", 2, 74, 44, 4, -1, templates, population, animals, ++group);
            Spawn("fish", 12, 33, 75, 15, 0, templates, population, animals, ++group);
            Spawn("fish", 4, 24, 53, 2, 1, templates, population, animals, ++group);
            Spawn("fish", 4, 75, 47, 2.5f, 2, templates, population, animals, ++group);
            for (int i = 0; i < 4; i++) Spawn("robin", 3, 22 + i * 16, 70 - i * 15, 14, -1, templates, population, animals, ++group);
            population.agents = animals.ToArray();
            return population;
        }

        static void Spawn(string species, int count, float x, float y, float radius, int water, Dictionary<string, GameObject> templates,
            WildlifePopulation population, List<WildlifeAgent> animals, int group)
        {
            var groupRoot = HillsideSceneBuilder.Group(species + " · 群 " + group, population.transform);
            Vector3 center = HillsideLayout.Design(x, y); center.y = HillsideLayout.Height(center.x, center.z);
            for (int i = 0; i < count; i++)
            {
                var random = new WildlifeRandom(population.worldSeed, animals.Count, species);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(templates[species], groupRoot);
                go.name = species + "_" + animals.Count.ToString("D2");
                var agent = go.GetComponent<WildlifeAgent>();
                agent.spawnId = animals.Count; agent.groupId = group; agent.habitatCenter = center; agent.habitatRadius = radius;
                agent.waterAreaIndex = water;
                Vector3 position = center;
                bool valid = false;
                for (int attempt = 0; attempt < 300; attempt++)
                {
                    Vector2 offset = random.InCircle(radius * .7f);
                    position = center + new Vector3(offset.x, 0, offset.y);
                    position.y = HillsideLayout.Height(position.x, position.z);
                    if (species == "robin") { position.y += 5; valid = true; break; }
                    if (water >= 0)
                    {
                        var volume = population.waterAreas[water];
                        if (!volume.Contains(position, .6f) || position.y > volume.center.y - .5f) continue;
                        position.y = volume.center.y - .35f; valid = true; break;
                    }
                    if (HillsideLayout.IsWater(position.x, position.z, .7f) || HillsideLayout.PathDistance(position.x, position.z) < 1.7f) continue;
                    if (!population.TryProjectPosition(agent, position, out position)) continue;
                    if (Physics.OverlapSphere(position + Vector3.up * .65f, agent.bodyRadius * 1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        .Any(hit => !(hit is TerrainCollider) && !hit.GetComponentInParent<WildlifeAgent>())) continue;
                    if (animals.Any(a => (a.transform.position - position).sqrMagnitude < 1.1f)) continue;
                    valid = true; break;
                }
                if (!valid) throw new InvalidOperationException("No valid habitat spawn for " + species + " group " + group);
                go.transform.position = position;
                go.transform.rotation = Quaternion.Euler(0, random.Range(0, 360), 0);
                animals.Add(agent);
            }
        }

        static GameObject Template(string species, string output)
        {
            bool squirrel = species == "squirrel";
            Entry[] entries = squirrel ? Array.Empty<Entry>() : Entries.Where(e => e.animal == species).ToArray();
            Entry first = entries.FirstOrDefault(e => e.state == "idle") ?? entries.FirstOrDefault();
            string path = squirrel ? "Assets/Experiments/Squirrel/Models/turn.fbx" : first.model;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab) throw new FileNotFoundException(path);
            var root = new GameObject(species);
            try
            {
                var agent = root.AddComponent<WildlifeAgent>();
                agent.speciesId = species;
                var visual = new GameObject("Visual").transform; visual.SetParent(root.transform, false);
                var heading = new GameObject("ModelAlignment").transform; heading.SetParent(visual, false);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, heading);
                model.name = "Model";
                var animator = model.GetComponent<Animator>();
                if (!animator) animator = model.AddComponent<Animator>();
                animator.applyRootMotion = false; animator.runtimeAnimatorController = null;
                foreach (var renderer in model.GetComponentsInChildren<Renderer>()) renderer.enabled = renderer is SkinnedMeshRenderer;
                Clip(path).SampleAnimation(model, 0);
                var bones = model.GetComponentsInChildren<Transform>();
                var head = bones.FirstOrDefault(t => t.name == (squirrel ? "Head" : first.headBone));
                var hips = bones.FirstOrDefault(t => t.name == (squirrel ? "Pelvis" : first.rootBone));
                if (head && hips)
                {
                    var forward = Vector3.ProjectOnPlane(head.position - hips.position, Vector3.up);
                    if (forward.sqrMagnitude > .0001f) heading.rotation = Quaternion.FromToRotation(forward.normalized, Vector3.forward);
                }
                Bounds bounds = PoseBounds(model);
                float height = TargetHeight(species);
                visual.localScale = Vector3.one * (height / Mathf.Max(.01f, bounds.size.y));
                bounds = PoseBounds(model);
                visual.position += new Vector3(-bounds.center.x, -.0f - bounds.min.y, -bounds.center.z);
                foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    Texture texture = squirrel ? skin.sharedMaterial.mainTexture : AssetDatabase.LoadAssetAtPath<Texture2D>(first.texture);
                    var material = HillsideSceneBuilder.Material("Animal_" + species, Color.white);
                    material.SetTexture("_BaseMap", texture);
                    skin.sharedMaterials = Enumerable.Repeat(material, skin.sharedMaterials.Length).ToArray();
                    skin.updateWhenOffscreen = true;
                }
                agent.animator = animator; agent.visualRoot = visual;
                AnimationClip Find(string state) { var e = entries.FirstOrDefault(item => item.state == state); return e == null ? null : Clip(e.model); }
                if (squirrel)
                {
                    AnimationClip Sq(string state) => Clip("Assets/Experiments/Squirrel/Models/" + state + ".fbx");
                    agent.clips = new WildlifeClips { idle = Sq("turn"), forage = Sq("turn"), walk = Sq("walk_biped"), run = Sq("run_biped"), greet = Sq("wave") };
                    var tail = model.AddComponent<SquirrelTailWave>(); tail.modelRoot = model.transform;
                }
                else agent.clips = new WildlifeClips {
                    idle = Find("idle") ?? Find("swim"), forage = Find("eat") ?? Find("look") ?? Find("sing") ?? Find("idle") ?? Find("swim"),
                    walk = Find("walk") ?? Find("swim"), run = Find("run") ?? Find("swim_fast") ?? Find("walk"),
                    greet = Find("sing") ?? Find("look") ?? Find("idle"), alert = Find("react") ?? Find("idle"), takeoff = Find("takeoff"), fly = Find("fly"), land = Find("land") };
                if (species == "fox") FoxRestImport.Bind(agent);
                if (species == "robin") agent.motionRoot = hips;
                agent.locomotion = species == "robin" ? WildlifeLocomotion.Flying : species == "fish" ? WildlifeLocomotion.Swimming : WildlifeLocomotion.Ground;
                agent.walkSpeed = species == "turtle" ? .22f : species == "fish" ? .65f : species == "robin" ? 3.2f : species == "deer" ? 1.1f : .65f;
                agent.runSpeed = species == "robin" ? 5 : species == "turtle" ? .4f : species == "fox" || species == "rabbit" ? 2.2f : 1.4f;
                agent.startlesOthers = species == "fox" || species == "dog";
                agent.friendlyAcrossSpecies = species == "dog" || species == "squirrel";
                agent.sociability = species == "fox" ? .15f : .7f;
                agent.timidity = species == "dog" ? .1f : species == "fox" ? .3f : .7f;
                agent.bodyRadius = Mathf.Max(.12f, height * .27f);
                agent.scaleRange = new Vector2(.82f, 1.16f);
                string savedPath = output + "/" + species + ".prefab";
                var saved = PrefabUtility.SaveAsPrefabAsset(root, savedPath);
                ValidateSavedScale(savedPath);
                return saved;
            }
            finally { Object.DestroyImmediate(root); }
        }

        public static AnimationClip Clip(string path)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            if (clips.Length != 1) throw new InvalidOperationException("Expected one skeletal animation: " + path + ", got " + clips.Length);
            return clips[0];
        }

        public static float TargetHeight(string species) => species == "deer" ? 1.4f : species == "sheep" ? .95f : species == "dog" ? .8f :
            species == "fox" ? .7f : species == "rabbit" ? .55f : species == "robin" ? .32f : species == "turtle" ? .30f : species == "fish" ? .30f : .72f;

        public static string ValidateSavedScales() => string.Join("\n", new[] { "sheep", "deer", "dog", "fox", "rabbit", "fish", "robin", "turtle", "squirrel" }
            .Select(species => ValidateSavedScale("Assets/Game/World/Hillside/Generated/" + species + ".prefab")));

        static string ValidateSavedScale(string assetPath)
        {
            // Reload the persisted hierarchy in Unity's isolated prefab preview scene, then evaluate the actual runtime binding.
            GameObject loaded = PrefabUtility.LoadPrefabContents(assetPath);
            PlayableGraph graph = default;
            try
            {
                var agent = loaded.GetComponent<WildlifeAgent>();
                if (!agent || !agent.animator || !agent.clips.idle) throw new InvalidOperationException(assetPath + ": missing agent, Animator or idle clip");
                var animator = agent.animator;
                animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                graph = PlayableGraph.Create("Saved animal scale check");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Scale", animator);
                var clip = AnimationClipPlayable.Create(graph, agent.clips.idle);
                output.SetSourcePlayable(clip); graph.Play(); clip.SetTime(0); graph.Evaluate(0);
                Bounds bounds = PoseBounds(animator.gameObject);
                float target = TargetHeight(agent.speciesId);
                if (float.IsNaN(bounds.size.y) || Mathf.Abs(bounds.size.y - target) > target * .02f ||
                    Mathf.Abs(bounds.min.y) > .02f || Mathf.Abs(bounds.center.x) > .02f || Mathf.Abs(bounds.center.z) > .02f)
                    throw new InvalidOperationException(assetPath + ": persisted animated geometry has incorrect size or grounding. target=" + target.ToString("F6") + " " + FormatBounds(bounds));
                return "PASS " + agent.speciesId + " saved prefab + Playables(0): height=" + bounds.size.y.ToString("F6") +
                    " target=" + target.ToString("F6") + " minY=" + bounds.min.y.ToString("F6");
            }
            finally { if (graph.IsValid()) graph.Destroy(); PrefabUtility.UnloadPrefabContents(loaded); }
        }

        // Every sample owns a fresh instance in an isolated preview scene. No saved scene or asset is changed.
        public static string DiagnoseScale()
        {
            var report = new StringBuilder();
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (string species in new[] { "sheep", "deer", "dog", "fox", "rabbit", "fish", "robin", "turtle", "squirrel" })
                {
                    Entry first = Entries.FirstOrDefault(e => e.animal == species && e.state == "idle") ?? Entries.FirstOrDefault(e => e.animal == species);
                    string path = species == "squirrel" ? "Assets/Experiments/Squirrel/Models/turn.fbx" : first.model;
                    var clip = Clip(path);
                    report.AppendLine("SPECIES " + species + " targetHeight=" + TargetHeight(species).ToString("F6") + " clip=" + clip.name);
                    foreach (string assetPath in new[] { path, "Assets/Game/World/Hillside/Generated/" + species + ".prefab" })
                    {
                        var source = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                        if (!source) { report.AppendLine("MISSING " + assetPath); continue; }
                        for (int mode = 0; mode < 3; mode++)
                        {
                            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
                            PlayableGraph graph = default;
                            try
                            {
                                var animator = instance.GetComponentInChildren<Animator>();
                                if (!animator) animator = instance.AddComponent<Animator>();
                                animator.applyRootMotion = false; animator.runtimeAnimatorController = null;
                                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                                if (mode == 1) clip.SampleAnimation(animator.gameObject, 0);
                                if (mode == 2)
                                {
                                    graph = PlayableGraph.Create("Animal scale diagnostic");
                                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                                    var output = AnimationPlayableOutput.Create(graph, "Scale", animator);
                                    var playable = AnimationClipPlayable.Create(graph, clip);
                                    output.SetSourcePlayable(playable); graph.Play();
                                    playable.SetTime(0); graph.Evaluate(0);
                                }
                                report.AppendLine(assetPath + " MODE=" + new[] { "Imported rest", "SampleAnimation(0)", "Playables(0)" }[mode]);
                                foreach (Transform t in instance.GetComponentsInChildren<Transform>())
                                    if (t == instance.transform || t == animator.transform || t.name == "Visual" || t.name == "ModelAlignment" ||
                                        t.name.EndsWith("_Rig") || t.name == (first?.headBone ?? "Head") || t.name == (first?.rootBone ?? "Pelvis") ||
                                        t.name == "Main1" || t.name == "Main2" || t.name == "Main6")
                                        report.AppendLine(" T " + t.name + " localP=" + t.localPosition.ToString("F6") + " localS=" + t.localScale.ToString("F6") +
                                            " worldS=" + t.lossyScale.ToString("F6") + " rootP=" + instance.transform.InverseTransformPoint(t.position).ToString("F6") + " euler=" + t.localEulerAngles.ToString("F6"));
                                foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                                {
                                    report.AppendLine(" R " + skin.name + " scale=" + skin.transform.lossyScale.ToString("F6") + " renderer=" + FormatBounds(skin.bounds) +
                                        " local=" + FormatBounds(skin.localBounds) + " shared=" + FormatBounds(skin.sharedMesh.bounds));
                                    var mesh = new Mesh();
                                    try
                                    {
                                        foreach (bool useScale in new[] { false, true })
                                        {
                                            skin.BakeMesh(mesh, useScale);
                                            Bounds world = VertexBounds(mesh.vertices.Select(v => skin.transform.TransformPoint(v)));
                                            report.AppendLine("  BakeMesh useScale=" + useScale + " local=" + FormatBounds(mesh.bounds) + " transformed=" + FormatBounds(world));
                                            mesh.Clear();
                                        }
                                    }
                                    finally { Object.DestroyImmediate(mesh); }
                                }
                            }
                            finally { if (graph.IsValid()) graph.Destroy(); Object.DestroyImmediate(instance); }
                        }
                    }
                }
                return report.ToString();
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        static string FormatBounds(Bounds bounds) => "center=" + bounds.center.ToString("F6") + " size=" + bounds.size.ToString("F6") + " min=" + bounds.min.ToString("F6");

        static Bounds VertexBounds(IEnumerable<Vector3> vertices)
        {
            bool initialized = false; Bounds result = default;
            foreach (Vector3 point in vertices)
            {
                if (!initialized) { result = new Bounds(point, Vector3.zero); initialized = true; }
                else result.Encapsulate(point);
            }
            if (!initialized) throw new InvalidOperationException("Empty baked mesh");
            return result;
        }

        public static Bounds PoseBounds(GameObject model)
        {
            bool initialized = false; Bounds result = default;
            var mesh = new Mesh();
            try
            {
                foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    // These FBX renderers have a scale of 100. The default path already includes that scale;
                    // useScale=true returns renderer-local vertices so TransformPoint applies it exactly once.
                    skin.BakeMesh(mesh, true);
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        Vector3 point = skin.transform.TransformPoint(vertex);
                        if (!initialized) { result = new Bounds(point, Vector3.zero); initialized = true; }
                        else result.Encapsulate(point);
                    }
                    mesh.Clear();
                }
            }
            finally { Object.DestroyImmediate(mesh); }
            if (!initialized) throw new InvalidOperationException("No skinned mesh: " + model.name);
            return result;
        }
    }
}
