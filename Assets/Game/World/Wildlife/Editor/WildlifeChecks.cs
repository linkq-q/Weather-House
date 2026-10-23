using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Hillside.Editor
{
    public static class WildlifeChecks
    {
        [MenuItem("Weather House/Hillside/Check Wildlife Logic")]
        public static void RunMenu() { Debug.Log(RunLogicChecks()); }

        public static string RunLogicChecks()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run these isolated logic checks outside Play mode.");
            var results = new List<string>();
            GameObject root = null;
            TerrainData data = null;
            AnimationClip idle = null, walk = null;
            Scene preview = EditorSceneManager.NewPreviewScene();
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            try
            {
                float expectedRandom = UnityEngine.Random.value;
                UnityEngine.Random.state = randomState;
                var first = new WildlifeRandom(42, 3, "deer");
                var repeat = new WildlifeRandom(42, 3, "deer");
                var different = new WildlifeRandom(42, 4, "deer");
                bool varied = false;
                for (int i = 0; i < 64; i++)
                {
                    float a = first.Value(), b = repeat.Value(), c = different.Value();
                    Require(a == b && a >= 0f && a < 1f, "Seeded random replay/range failed.");
                    varied |= a != c;
                }
                Require(varied && UnityEngine.Random.value == expectedRandom, "Animal RNG changed global Random or ignored spawnId.");
                results.Add("PASS seeded replay, distinct animals and global Random isolation");

                root = new GameObject("Wildlife isolated checks") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, preview);
                data = new TerrainData { heightmapResolution = 33, size = new Vector3(80f, 10f, 80f) };
                GameObject ground = Terrain.CreateTerrainGameObject(data);
                ground.transform.SetParent(root.transform);
                ground.transform.position = new Vector3(-40f, 0f, -40f);
                var population = root.AddComponent<WildlifePopulation>();
                population.terrain = ground.GetComponent<Terrain>();
                population.obstacleMask = 0;
                population.worldBounds = new Bounds(new Vector3(0f, 10f, 0f), new Vector3(80f, 30f, 80f));
                population.waterAreas = new[] { new HabitatVolume { id = "pond", center = new Vector3(0f, 1.5f, 0f), radii = new Vector2(4f, 6f) } };
                var a1 = Agent(root.transform, "deer", 1, new Vector3(-10f, 0f, 0f));
                var a2 = Agent(root.transform, "deer", 2, new Vector3(-8.5f, 0f, 0f));
                var remote = Agent(root.transform, "deer", 3, new Vector3(20f, 0f, 0f));
                var fish = Agent(root.transform, "fish", 4, Vector3.zero);
                fish.locomotion = WildlifeLocomotion.Swimming; fish.waterAreaIndex = 0; fish.bodyRadius = .15f;
                fish.timidity = 0f;
                var bird = Agent(root.transform, "robin", 5, new Vector3(10f, 4f, 10f));
                bird.locomotion = WildlifeLocomotion.Flying; bird.timidity = 0f; bird.flightHeight = 4f;
                population.agents = new[] { a1, a2, remote, fish, bird };
                population.Initialize();
                float originalScale = a1.ScaleFactor;
                population.Initialize();
                Require(a1.ScaleFactor == originalScale && a1.visualRoot.localScale == Vector3.one * originalScale, "Repeated initialization compounded animal size.");
                Require(a1.ScaleFactor >= a1.scaleRange.x && a1.ScaleFactor <= a1.scaleRange.y && a1.ScaleFactor != a2.ScaleFactor,
                    "Stable animal size variation failed.");
                results.Add("PASS stable individual sizes and idempotent initialization");

                Require(population.TryProjectPosition(a1, new Vector3(10f, 0f, 0f), out _), "Ground endpoint should be valid.");
                Require(!population.TryMove(a1, new Vector3(10f, 0f, 0f), out _), "Ground animal crossed a pond between safe endpoints.");
                Require(!population.TryProjectPosition(a1, Vector3.zero, out _), "Ground animal entered water.");
                Require(!population.TryProjectPosition(a1, new Vector3(45f, 0f, 0f), out _), "Animal escaped world bounds.");
                Require(population.TryProjectPosition(fish, Vector3.zero, out Vector3 wet) && wet.y < 1.5f && wet.y > 0f, "Fish did not remain below water and above bottom.");
                Require(!population.TryProjectPosition(fish, new Vector3(8f, 0f, 0f), out _), "Fish entered dry ground.");
                HabitatVolume originalWater = population.waterAreas[0];
                population.waterAreas[0] = new HabitatVolume { center = new Vector3(0f, .1f, 0f), radii = new Vector2(4f, 6f) };
                Require(!population.TryProjectPosition(fish, Vector3.zero, out _), "Fish accepted water shallower than its body.");
                population.waterAreas[0] = originalWater;
                Require(population.TryProjectPosition(bird, Vector3.zero, out Vector3 air) && air.y > 1.5f, "Bird could not cross a pond above its surface.");
                results.Add("PASS land/water/air constraints, shallow water, world edge and whole-segment pond rejection");

                WildlifeAlarm alarm = population.EmitStartle(a1.transform.position, 4f, 1f);
                Require(a1.State == WildlifeState.Alert && a2.State == WildlifeState.Alert && remote.AcceptedAlarmCount == 0,
                    "Startle radius isolation failed.");
                Require(!a1.ReceiveStartle(alarm, population.SimulationTime), "The same startle event was consumed twice.");
                Require(!remote.ReceiveStartle(new WildlifeAlarm { id = 999, origin = remote.transform.position, radius = 5f,
                    intensity = 1f, expiresAt = -1f }, population.SimulationTime), "Expired startle was accepted.");
                Advance(population, 6f);
                Require(population.AlarmCount == 1 && a1.AcceptedAlarmCount == 1 && a2.AcceptedAlarmCount == 1,
                    "Startle contagion created new alarms.");
                Require(a1.State != WildlifeState.Alert && a1.State != WildlifeState.Flee, "Animal failed to recover after fear timeout.");
                results.Add("PASS finite local startle, deduplication, expiry and calm recovery");

                a1.transform.position = a1.habitatCenter; a2.transform.position = a2.habitatCenter; remote.transform.position = remote.habitatCenter;
                population.Initialize();
                a1.timidity = a2.timidity = remote.timidity = 0f;
                Advance(population, 6f);
                a1.transform.position = new Vector3(-10f, 0f, 0f);
                a2.transform.position = new Vector3(-8.5f, 0f, 0f);
                a1.sociability = a2.sociability = remote.sociability = 1f;
                Require(population.TryGreet(a1, a2), "A compatible nearby pair could not greet.");
                Require(a1.GreetingPartner == a2 && a2.GreetingPartner == a1 && !population.TryGreet(a1, remote), "Greeting pair was not reserved mutually.");
                Advance(population, 2.5f);
                Require(a1.GreetingPartner == null && a2.GreetingPartner == null && !population.TryGreet(a1, a2), "Greeting did not release or respect cooldown.");
                results.Add("PASS mutual greeting reservation, completion and cooldown");

                idle = new AnimationClip { name = "Idle test" }; walk = new AnimationClip { name = "Walk test" };
                var clips = new WildlifeClips { idle = idle, walk = walk };
                Require(clips.ForState(WildlifeState.Greet, WildlifeLocomotion.Ground) == idle, "Greeting fallback was not calm idle.");
                Require(clips.ForState(WildlifeState.Flee, WildlifeLocomotion.Ground) == walk, "Missing run did not fall back to locomotion.");
                foreach (WildlifeState state in Enum.GetValues(typeof(WildlifeState)))
                    Require(clips.ForState(state, WildlifeLocomotion.Ground) != null, "An FSM state lost its animation.");
                results.Add("PASS semantic clip fallback for every state");
                var missingClip = new AnimationClip();
                clips.alert = missingClip;
                Object.DestroyImmediate(missingClip);
                Require(CollectClips(clips).Count == 2, "A destroyed Unity clip survived optional-slot filtering.");
                results.Add("PASS Unity missing-object clip filtering");

                bird.transform.position = new Vector3(-20f, 4f, -20f);
                bird.habitatCenter = bird.transform.position; bird.habitatRadius = 10f; bird.walkSpeed = 5f;
                population.agents = new[] { bird }; population.Initialize();
                var birdStates = new HashSet<WildlifeState>();
                for (int i = 0; i < 900; i++)
                {
                    population.Simulate(.1f); birdStates.Add(bird.State);
                    if (bird.AirHeight < .01f)
                        Require(population.TryLandingPosition(bird, bird.transform.position, out _), "Bird perched on water or invalid terrain.");
                }
                Require(birdStates.Contains(WildlifeState.Fly) && birdStates.Contains(WildlifeState.Landing) &&
                    birdStates.Contains(WildlifeState.Takeoff) && (birdStates.Contains(WildlifeState.Forage) || birdStates.Contains(WildlifeState.Idle)),
                    "Bird did not complete flight, safe landing, perch/sing and takeoff.");
                results.Add("PASS bird flight / dry landing / perch or sing / takeoff cycle");

                var crowd = new WildlifeAgent[80];
                for (int i = 0; i < crowd.Length; i++)
                {
                    crowd[i] = Agent(root.transform, "sheep", 100 + i, new Vector3(-20f, 0f, -20f));
                    crowd[i].timidity = crowd[i].sociability = 0f;
                }
                population.agents = crowd;
                population.Initialize(); population.Simulate(.1f);
                Require(population.LastNeighbourVisits <= crowd.Length * 49, "Dense population neighbour work exceeded its per-animal cap.");
                results.Add("PASS 80-animal dense neighbour work cap (not a frame-rate benchmark)");
                return string.Join("\n", results);
            }
            finally
            {
                UnityEngine.Random.state = randomState;
                if (root != null) Object.DestroyImmediate(root);
                if (data != null) Object.DestroyImmediate(data);
                if (idle != null) Object.DestroyImmediate(idle);
                if (walk != null) Object.DestroyImmediate(walk);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        public static string RunRigBindingChecks(WildlifePopulation population)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run isolated rig binding checks outside Play mode.");
            if (population == null) throw new ArgumentNullException(nameof(population));
            Scene preview = EditorSceneManager.NewPreviewScene();
            var checkedSpecies = new HashSet<string>();
            var report = new List<string>();
            try
            {
                foreach (WildlifeAgent source in population.agents)
                {
                    if (source == null || !checkedSpecies.Add(source.speciesId)) continue;
                    Require(source.animator != null && source.clips != null, source.name + ": animator/clips missing.");
                    Require(source.clips.idle != null, source.speciesId + ": required idle clip is missing.");
                    if (source.locomotion == WildlifeLocomotion.Flying)
                        Require(source.clips.takeoff != null && source.clips.fly != null && source.clips.land != null,
                            source.speciesId + ": required takeoff, fly or land clip is missing; rebuild or repair the animal prefab.");
                    else Require(source.clips.walk != null, source.speciesId + ": required walk/swim clip is missing.");
                    GameObject clone = Object.Instantiate(source.animator.gameObject);
                    clone.name = "Binding check " + source.speciesId;
                    clone.hideFlags = HideFlags.HideAndDontSave;
                    SceneManager.MoveGameObjectToScene(clone, preview);
                    var graph = PlayableGraph.Create(clone.name);
                    try
                    {
                        var animator = clone.GetComponent<Animator>();
                        animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
                        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                        var output = AnimationPlayableOutput.Create(graph, "Rig check", animator);
                        Transform[] bones = clone.GetComponentsInChildren<Transform>();
                        HashSet<AnimationClip> clips = CollectClips(source.clips);
                        int movingClips = 0;
                        foreach (AnimationClip clip in clips)
                        {
                            Require(!clip.legacy, source.speciesId + ": legacy clip cannot feed this Animator graph: " + clip.name);
                            var playable = AnimationClipPlayable.Create(graph, clip);
                            output.SetSourcePlayable(playable); graph.Play();
                            playable.SetTime(clip.length * .12f); graph.Evaluate(0f);
                            var positions = new Vector3[bones.Length];
                            var rotations = new Quaternion[bones.Length];
                            for (int i = 0; i < bones.Length; i++) { positions[i] = bones[i].localPosition; rotations[i] = bones[i].localRotation; }
                            bool changed = false;
                            foreach (float phase in new[] { .37f, .71f })
                            {
                                playable.SetTime(clip.length * phase); graph.Evaluate(0f);
                                for (int i = 0; i < bones.Length; i++)
                                    changed |= (bones[i].localPosition - positions[i]).sqrMagnitude > .00000001f ||
                                        Quaternion.Angle(bones[i].localRotation, rotations[i]) > .01f;
                            }
                            if (changed) movingClips++;
                            if (clip == source.clips.walk || clip == source.clips.run || clip == source.clips.fly)
                                Require(changed, source.speciesId + ": locomotion clip did not move any bound transforms: " + clip.name);
                            output.SetSourcePlayable(Playable.Null); graph.DestroyPlayable(playable);
                        }
                        Require(movingClips > 0, source.speciesId + ": no clip animated the cloned rig.");
                        report.Add("PASS " + source.speciesId + ": " + clips.Count + " clips evaluated; " + movingClips +
                            " changed bone transforms; Avatar=" + (animator.avatar == null ? "internal generic" : animator.avatar.name));
                    }
                    finally { if (graph.IsValid()) graph.Destroy(); Object.DestroyImmediate(clone); }
                }
                return string.Join("\n", report);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static HashSet<AnimationClip> CollectClips(WildlifeClips source)
        {
            var result = new HashSet<AnimationClip>();
            foreach (AnimationClip clip in new[] { source.idle, source.forage, source.walk, source.run,
                source.greet, source.alert, source.takeoff, source.fly, source.land })
                if (clip != null) result.Add(clip);
            return result;
        }

        private static WildlifeAgent Agent(Transform parent, string species, int id, Vector3 point)
        {
            var go = new GameObject(species + " " + id);
            go.transform.SetParent(parent); go.transform.position = point;
            var agent = go.AddComponent<WildlifeAgent>();
            agent.speciesId = species; agent.spawnId = id; agent.groupId = id < 4 ? 1 : id;
            agent.habitatCenter = point; agent.habitatRadius = 8f; agent.sociability = 0f;
            var visual = new GameObject("Visual"); visual.transform.SetParent(go.transform, false); agent.visualRoot = visual.transform;
            return agent;
        }

        private static void Advance(WildlifePopulation population, float seconds)
        { for (int i = 0; i < Mathf.CeilToInt(seconds * 10f); i++) population.Simulate(.1f); }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
