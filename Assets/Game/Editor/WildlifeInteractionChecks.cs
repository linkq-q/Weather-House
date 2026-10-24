using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using WeatherHouse.WildlifeJourney;
using Object = UnityEngine.Object;

namespace WeatherHousePrototype.Editor
{
    // Reflection on the new Actor properties lets the same fixture expose the old Awake/BuildVisual regression.
    [InitializeOnLoad]
    public static class WildlifeInteractionChecks
    {
        private const string Key = "WeatherHouse.WildlifeInteractionChecks.";
        private const string Report = "WildlifeInteractionReview/results.txt";
        private static Fixture live;
        private static Geometry saved;
        private static int frame, phase;
        private static double started;
        private static byte[] notebookBefore;
        private static string NotebookPath => Path.Combine(Application.persistentDataPath, "language-notebook.json");
        static WildlifeInteractionChecks() { EditorApplication.playModeStateChanged += Changed; }

        public static void RunEditChecks()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Edit checks require Edit mode.");
            using (var fixture = new Fixture(false)) CheckBinding(fixture);
            Debug.Log("PASS ecology binding, legacy/Herd sources, geometry protection, stable IDs, glyphs and availability in Edit mode");
        }
        public static void BeginPlayChecks()
        {
            Require(File.Exists(".wildlife-journey-validation") && !EditorApplication.isPlayingOrWillChangePlaymode,
                "Actual Play checks require the isolated .wildlife-journey-validation project.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Require(!SceneManager.GetSceneAt(i).isDirty, "Do not discard a dirty scene.");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Require(EditorSceneManager.SaveScene(scene, "Assets/InteractionValidation.unity"), "Could not save the isolated fixture scene.");
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            File.WriteAllText(Report, "RUNNING actual Play: active AddComponent/Awake, native Update and original ecology simulation.\n");
            SessionState.SetBool(Key + "running", true); SessionState.SetBool(Key + "passed", false);
            EditorApplication.isPlaying = true;
        }
        private static void Changed(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key + "running", false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += StartPlay;
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                File.AppendAllText(Report, "HANDOFF Play=False dirty=" + SceneManager.GetActiveScene().isDirty + "\n");
                SessionState.SetBool(Key + "running", false);
                EditorApplication.Exit(SessionState.GetBool(Key + "passed", false) ? 0 : 1);
            }
        }
        private static void StartPlay()
        {
            try
            {
                started = EditorApplication.timeSinceStartup;
                notebookBefore = File.Exists(NotebookPath) ? File.ReadAllBytes(NotebookPath) : null;
                live = new Fixture(true);
                CheckBinding(live);
                Log("PASS active Agent and Agent-free Herd attachment does not create primitives, props, replace materials or change bones/Animator");
                CheckEcologyMovement(live);
                Log("PASS original Population.Simulate and Herd.Simulate still move both bodies after interaction binding");
                saved = new Geometry(live.root.transform); frame = Time.frameCount; phase = 0;
                EditorApplication.update += ObserveFrames;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void ObserveFrames()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup - started < 30, "Native Update observation timed out.");
                if (Time.frameCount < frame + 5) return;
                saved.AssertUnchanged("Actor's actual Update took over the ecology body");
                if (phase++ == 0)
                {
                    Log("PASS five real Unity frames preserve manually advanced ecology poses while only Actor remains enabled");
                    WildlifeInteractionTarget target = live.sheep.GetComponent<WildlifeInteractionTarget>();
                    target.enabled = false;
                    Require(!Available(live.sheepActor), "Disabled target remained observable.");
                    live.sheepActor.BuildVisual(); saved.AssertUnchanged("Disabled target fell back to the prototype renderer");
                    Object.DestroyImmediate(target);
                    Object.DestroyImmediate(live.agent);
                    Require(!Available(live.sheepActor) && !Available(live.agentActor), "Destroyed target/driver remained observable.");
                    live.sheepActor.BuildVisual(); live.agentActor.BuildVisual();
                    saved = new Geometry(live.root.transform); frame = Time.frameCount;
                    return;
                }
                Log("PASS disabled/destroyed target and destroyed driver cannot re-enable prototype geometry or Update");
                Require(notebookBefore == null ? !File.Exists(NotebookPath) :
                    File.Exists(NotebookPath) && notebookBefore.SequenceEqual(File.ReadAllBytes(NotebookPath)), "Validation changed the user's notebook.");
                Log("PASS no Session/Notebook/UI objects were created and the user's notebook bytes remain unchanged");
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= ObserveFrames;
            try { live?.Dispose(); live = null; }
            catch (Exception cleanup) { if (error == null) error = cleanup; }
            Log(error == null ? "PASS complete; real E keyboard/HUD visual integration remains a separate main-map check." : "FAIL " + error);
            if (error != null) Debug.LogException(error);
            SessionState.SetBool(Key + "passed", error == null);
            EditorApplication.isPlaying = false;
        }

        private static void CheckBinding(Fixture f)
        {
            var initial = new Geometry(f.root.transform);
            AnimalActor[] actors = WildlifeInteractionTarget.BindScene(f.scene);
            initial.AssertUnchanged("Immediate active AddComponent/Awake modified the existing animal geometry");
            Require(actors.Length == 2 && actors.Select(a => a.animalId).Distinct().Count() == 2, "Agent and transferred sheep were not uniquely registered.");
            f.agentActor = f.agent.GetComponent<AnimalActor>(); f.sheepActor = f.sheep.GetComponent<AnimalActor>();
            Require(f.agentActor && f.sheepActor && !f.sheep.GetComponent<WildlifeAgent>(), "The sheep was re-created as a legacy agent or missed by binding.");
            string[] ids = actors.Select(a => a.animalId).OrderBy(x => x).ToArray();
            AnimalActor[] rebound = WildlifeInteractionTarget.BindScene(f.scene);
            Require(rebound.Length == 2 && rebound.All(a => actors.Contains(a)) &&
                rebound.Select(a => a.animalId).OrderBy(x => x).SequenceEqual(ids) &&
                f.root.GetComponentsInChildren<WildlifeInteractionTarget>(true).Length == 2, "Repeated binding changed identities or duplicated components.");
            foreach (AnimalActor actor in actors) { actor.BuildVisual(); actor.BuildVisual(); }
            initial.AssertUnchanged("External BuildVisual changed the model or material");
            Require((Transform)Property(f.agentActor, "VisualRoot") == f.agent.visualRoot &&
                (Transform)Property(f.sheepActor, "VisualRoot") == f.member.visualRoot, "Photo focus root did not use the actual imported visual root.");

            f.population.enabled = f.herd.enabled = true;
            Require(Available(f.agentActor) && Available(f.sheepActor), "Enabled drivers were not observable.");
            CheckButterflyExclusion(f);
            foreach (AnimalActor actor in actors)
            {
                Require(actor.background && actor.region == null && !actor.RequestSatisfied, "Ecology binding fabricated a weather request.");
                string utterance = actor.UtteranceId; string[] glyphs = actor.CurrentGlyphIds;
                Require(glyphs.SequenceEqual(new[] { "hello" }) && actor.CurrentGlyphIds.SequenceEqual(glyphs) && actor.UtteranceId == utterance,
                    "Repeated reads changed the greeting snapshot.");
                glyphs[0] = "not-a-word";
                Require(actor.CurrentGlyphIds.SequenceEqual(new[] { "hello" }), "A caller mutated the shared greeting buffer.");
            }
            Vector3 anchor = f.sheepActor.BubbleAnchor;
            Vector3 headMovement = f.head.parent.TransformVector(Vector3.up * .3f);
            f.head.localPosition += Vector3.up * .3f;
            Require((f.sheepActor.BubbleAnchor - anchor - headMovement).magnitude < .001f, "The sheep bubble did not follow its real head bone.");
            f.head.localPosition -= Vector3.up * .3f;
            Vector3 oldAgentAnchor = f.agentActor.BubbleAnchor;
            f.agent.visualRoot.localScale *= 1.2f;
            Require(f.agentActor.BubbleAnchor.y > oldAgentAnchor.y + .02f, "Bounds fallback did not reflect the model's current scale.");
            f.agent.visualRoot.localScale /= 1.2f;
            Require(!(bool)Property(f.agentActor, "HasAmbientSpeech") && !(bool)Property(f.sheepActor, "HasAmbientSpeech"), "A silent animal claimed ambient speech.");
            CheckAmbientOwnership(f);

            // Resolving by current member id/root must survive replacing the serializable member object.
            JourneyMember old = f.member;
            f.member = new JourneyMember { id = old.id, root = old.root, visualRoot = old.visualRoot, headBone = old.headBone,
                profile = old.profile, bodyLength = old.bodyLength, sociability = 0 };
            f.herd.members = new[] { f.member };
            Require(Available(f.sheepActor), "The adapter retained a stale member object.");
            f.herd.members = Array.Empty<JourneyMember>();
            Require(!Available(f.sheepActor), "A removed herd member remained observable.");
            f.herd.members = new[] { f.member };
            f.agent.enabled = false; Require(!Available(f.agentActor), "A disabled agent remained observable."); f.agent.enabled = true;
            f.population.enabled = f.herd.enabled = false;
            Require(!Available(f.agentActor) && !Available(f.sheepActor), "A disabled driver remained observable.");
            f.agentActor.BuildVisual(); f.sheepActor.BuildVisual();
            initial.AssertUnchanged("Driver lifecycle changed ecology visuals");
            CheckPrototype(f);
            CheckInactiveFirstAwake(f);
        }
        private static void CheckButterflyExclusion(Fixture f)
        {
            Transform body = f.Body("Non-conversational butterfly", new Vector3(2020, 0, 2020));
            var butterfly = body.gameObject.AddComponent<WildlifeAgent>();
            butterfly.speciesId = "butterfly"; butterfly.spawnId = 5001; butterfly.visualRoot = body.GetChild(0);
            WildlifeAgent[] original = f.population.agents;
            string species = f.agent.speciesId;
            var geometry = new Geometry(body);
            try
            {
                f.population.agents = new[] { f.agent, butterfly };
                AnimalActor[] actors = WildlifeInteractionTarget.BindScene(f.scene);
                Require(actors.Length == 2 && !body.GetComponent<AnimalActor>() && !body.GetComponent<WildlifeInteractionTarget>(),
                    "Butterflies must not be installed as conversation targets.");
                geometry.AssertUnchanged("Excluding a butterfly changed its existing visual or Animator");
                // Persisted scenes already have the old marker/Actor; their live availability must also reject butterflies.
                f.agent.speciesId = "butterfly";
                Require(!Available(f.agentActor) && f.agentActor.CurrentGlyphIds.Length == 0 && f.agentActor.UtteranceId.EndsWith(".unavailable"),
                    "An already-bound butterfly still exposes an observation or greeting.");
                Require(WildlifeInteractionTarget.BindScene(f.scene).SequenceEqual(new[] { f.sheepActor }),
                    "Rebinding retained an existing butterfly in the conversation list.");
                f.agent.speciesId = species;
                Require(Available(f.agentActor) && f.agentActor.CurrentGlyphIds.SequenceEqual(new[] { "hello" }),
                    "The butterfly filter disabled an ordinary animal's conversation.");
            }
            finally
            {
                f.agent.speciesId = species; f.population.agents = original;
                Object.DestroyImmediate(body.gameObject);
            }
        }
        private static void CheckAmbientOwnership(Fixture f)
        {
            var host = new GameObject("Ambient speech contract"); host.transform.SetParent(f.root.transform);
            var bubble = new GameObject("Visible bubble state", typeof(RectTransform)); bubble.transform.SetParent(host.transform);
            JourneySocial social = host.AddComponent<JourneySocial>(); f.herd.social = social;
            try
            {
                var turn = new JourneyConversation { speakerId = f.member.id, listenerId = -1, sequenceId = 2, turnIndex = 1, glyphIds = new[] { "you" } };
                typeof(JourneySocial).GetProperty("ActiveConversation").SetValue(social, turn);
                Set(social, "bubble", bubble.GetComponent<RectTransform>());
                Require((bool)Property(f.sheepActor, "HasAmbientSpeech") && !(bool)Property(f.agentActor, "HasAmbientSpeech"),
                    "The visible social turn was not attributed exclusively to its speaker.");
                string utterance = f.sheepActor.UtteranceId;
                string[] visibleGlyphs = f.sheepActor.CurrentGlyphIds;
                Require(visibleGlyphs.SequenceEqual(new[] { "you" }) && f.sheepActor.UtteranceId == utterance &&
                    f.sheepActor.CurrentGlyphIds.SequenceEqual(visibleGlyphs), "Observation did not capture the visible social word with a stable context.");
                visibleGlyphs[0] = "not-a-word";
                Require(turn.glyphIds[0] == "you" && f.sheepActor.CurrentGlyphIds[0] == "you", "Observation mutated the social turn's glyphs.");
                turn.glyphIds = new[] { "hello" };
                Require(f.sheepActor.UtteranceId != utterance, "A restarted social sequence reused the identity of a different word.");
                turn.glyphIds = new[] { "you" };
                turn.turnIndex++;
                Require(f.sheepActor.UtteranceId != utterance, "Two social turns reused one utterance identity.");
                turn.speakerId = f.member.id + 1;
                Require(!(bool)Property(f.sheepActor, "HasAmbientSpeech"), "A listener claimed the speaker's bubble.");
                Require(f.sheepActor.CurrentGlyphIds.SequenceEqual(new[] { "hello" }), "A listener recorded a different animal's visible words.");
                turn.speakerId = f.member.id; bubble.SetActive(false);
                Require(!(bool)Property(f.sheepActor, "HasAmbientSpeech"), "An active but unpresented turn claimed a visible bubble.");
                bubble.SetActive(true); social.enabled = false;
                Require(!(bool)Property(f.sheepActor, "HasAmbientSpeech"), "Disabled social presentation claimed a bubble.");
            }
            finally { f.herd.social = null; Object.DestroyImmediate(host); }
        }
        private static void CheckInactiveFirstAwake(Fixture f)
        {
            Transform body = f.Body("Inactive before first Awake", new Vector3(2020, 0, 2020));
            body.gameObject.SetActive(false);
            WildlifeAgent agent = body.gameObject.AddComponent<WildlifeAgent>();
            agent.speciesId = "fox"; agent.spawnId = 99; agent.visualRoot = body.GetChild(0); agent.enabled = false;
            f.population.agents = new[] { f.agent, agent };
            var before = new Geometry(body);
            try
            {
                WildlifeInteractionTarget.BindScene(f.scene);
                AnimalActor actor = body.GetComponent<AnimalActor>();
                // Do not read Actor getters before destruction: that would hide a missing explicit latch.
                Object.DestroyImmediate(body.GetComponent<WildlifeInteractionTarget>());
                body.gameObject.SetActive(true);
                before.AssertUnchanged("First Awake after removing an inactive body's marker modified its model");
                actor.BuildVisual();
                before.AssertUnchanged("An inactive binding did not retain external presentation ownership");
                Require(!Available(actor), "An inactive binding whose target was removed remained observable.");
            }
            finally { f.population.agents = new[] { f.agent }; Object.DestroyImmediate(body.gameObject); }
        }
        private static void CheckPrototype(Fixture f)
        {
            GameObject root = new GameObject("Ordinary prototype control"); SceneManager.MoveGameObjectToScene(root, f.scene);
            try
            {
                root.transform.position = new Vector3(2500, 0, 2500);
                AnimalActor actor = root.AddComponent<AnimalActor>(); actor.BuildVisual();
                int objects = root.GetComponentsInChildren<Transform>(true).Length;
                actor.BuildVisual();
                Require(root.transform.Find("Visual") && root.transform.Find("WeatherProps") && objects > 4 &&
                    objects == root.GetComponentsInChildren<Transform>(true).Length, "Ordinary Actor prototype generation regressed.");
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static void CheckEcologyMovement(Fixture f)
        {
            f.population.Initialize(); f.herd.Initialize();
            Vector3 oldAgent = f.agent.transform.position, oldSheep = f.sheep.position;
            Set(f.agent, "destination", oldAgent + Vector3.right * 4); Set(f.agent, "nextDestination", 1000f); Set(f.agent, "stateUntil", 1000f);
            typeof(WildlifeAgent).GetProperty("State").SetValue(f.agent, WildlifeState.Wander);
            Require(f.herd.BeginJourney(1), "The herd fixture had no actual route.");
            for (int i = 0; i < 240; i++) { f.population.Simulate(.05f); f.herd.Simulate(.05f); }
            Require(Vector3.Distance(oldAgent, f.agent.transform.position) > .5f &&
                Vector3.Distance(oldSheep, f.sheep.position) > 2f, "Ecology did not advance after interaction binding.");
        }

        private sealed class Fixture : IDisposable
        {
            public readonly Scene scene;
            public readonly GameObject root;
            public readonly WildlifePopulation population;
            public readonly WildlifeAgent agent;
            public readonly JourneyHerd herd;
            public readonly Transform sheep, head;
            public JourneyMember member;
            public AnimalActor agentActor, sheepActor;
            private readonly bool play;
            private readonly Material material;
            public Fixture(bool play)
            {
                this.play = play;
                scene = play ? SceneManager.CreateScene("Interaction fixture") : EditorSceneManager.NewPreviewScene();
                root = new GameObject("Ecology interaction fixture"); SceneManager.MoveGameObjectToScene(root, scene);
                material = new Material(Shader.Find("Hidden/InternalErrorShader"));
                population = root.AddComponent<WildlifePopulation>(); population.enabled = false;
                population.worldBounds = new Bounds(new Vector3(2000, 10, 2000), new Vector3(60, 20, 60)); population.obstacleMask = 0;
                Transform body = Body("Fox existing body", new Vector3(1990, 0, 1990));
                agent = body.gameObject.AddComponent<WildlifeAgent>(); agent.speciesId = "fox"; agent.spawnId = 20;
                agent.visualRoot = body.GetChild(0); agent.scaleRange = new Vector2(.91f, .91f); agent.sociability = agent.timidity = 0;
                agent.habitatCenter = body.position; agent.habitatRadius = 10;
                population.agents = new[] { agent };
                GameObject driver = new GameObject("Separate Journey driver"); driver.transform.SetParent(root.transform);
                JourneyNavigation nav = driver.AddComponent<JourneyNavigation>(); nav.surfaceSampler = p => new JourneySurface { valid = true, groundY = 0 };
                nav.nodes = new[] { new JourneyNode { position = new Vector3(2000, 0, 2000) }, new JourneyNode { position = new Vector3(2005, 0, 2000) } };
                nav.links = new[] { new JourneyLink { from = 0, to = 1, bidirectional = true } }; nav.Rebuild();
                herd = driver.AddComponent<JourneyHerd>(); herd.enabled = false; herd.navigation = nav; herd.autoAdvance = false; herd.travelSpread = 0;
                sheep = Body("Transferred sheep without Agent", nav.nodes[0].position);
                head = new GameObject("Head").transform; head.SetParent(sheep.GetChild(0), false); head.localPosition = new Vector3(0, .8f, .4f);
                member = new JourneyMember { id = 23, root = sheep, visualRoot = sheep.GetChild(0), headBone = head, sociability = 0 };
                herd.members = new[] { member };
                herd.sites = new[] { new JourneyActivitySite { center = nav.nodes[0].position, radius = 2 }, new JourneyActivitySite { center = nav.nodes[1].position, radius = 2 } };
            }
            public Transform Body(string name, Vector3 position)
            {
                var body = new GameObject(name).transform; body.SetParent(root.transform); body.position = position;
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube); visual.name = "ImportedRig";
                Object.DestroyImmediate(visual.GetComponent<Collider>()); visual.transform.SetParent(body, false);
                visual.transform.localPosition = new Vector3(0, .45f, 0); visual.transform.localScale = new Vector3(.6f, .9f, 1.2f);
                Renderer renderer = visual.GetComponent<Renderer>(); renderer.sharedMaterial = material;
                var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", Color.magenta); renderer.SetPropertyBlock(block);
                visual.AddComponent<Animator>();
                return body;
            }
            public void Dispose()
            {
                Object.DestroyImmediate(root); Object.DestroyImmediate(material);
                if (play) SceneManager.UnloadSceneAsync(scene); else EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        private sealed class Geometry
        {
            private readonly Transform root;
            private readonly Transform[] nodes;
            private readonly Vector3[] positions, scales;
            private readonly Quaternion[] rotations;
            private readonly Renderer[] renderers;
            private readonly Material[][] materials;
            private readonly Color[] colors;
            private readonly Animator[] animators;
            private readonly RuntimeAnimatorController[] controllers;
            private readonly bool[] rootMotion;
            public Geometry(Transform root)
            {
                this.root = root; nodes = root.GetComponentsInChildren<Transform>(true);
                positions = nodes.Select(t => t.localPosition).ToArray(); rotations = nodes.Select(t => t.localRotation).ToArray(); scales = nodes.Select(t => t.localScale).ToArray();
                renderers = root.GetComponentsInChildren<Renderer>(true); materials = renderers.Select(r => r.sharedMaterials).ToArray();
                colors = renderers.Select(ColorOf).ToArray();
                animators = root.GetComponentsInChildren<Animator>(true);
                controllers = animators.Select(a => a.runtimeAnimatorController).ToArray(); rootMotion = animators.Select(a => a.applyRootMotion).ToArray();
            }
            public void AssertUnchanged(string message)
            {
                Require(nodes.SequenceEqual(root.GetComponentsInChildren<Transform>(true)) && renderers.SequenceEqual(root.GetComponentsInChildren<Renderer>(true)), message + ": hierarchy/renderers changed.");
                for (int i = 0; i < nodes.Length; i++)
                    Require((nodes[i].localPosition - positions[i]).sqrMagnitude < .00000001f && Quaternion.Angle(nodes[i].localRotation, rotations[i]) < .001f &&
                        (nodes[i].localScale - scales[i]).sqrMagnitude < .00000001f, message + ": pose changed at " + nodes[i].name);
                for (int i = 0; i < renderers.Length; i++)
                    Require(materials[i].SequenceEqual(renderers[i].sharedMaterials) && ColorOf(renderers[i]) == colors[i], message + ": material changed at " + renderers[i].name);
                Require(animators.SequenceEqual(root.GetComponentsInChildren<Animator>(true)), message + ": Animator components changed.");
                for (int i = 0; i < animators.Length; i++)
                    Require(controllers[i] == animators[i].runtimeAnimatorController && rootMotion[i] == animators[i].applyRootMotion,
                        message + ": Animator controller/root motion changed.");
            }
            private static Color ColorOf(Renderer renderer) { var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); return block.GetColor("_BaseColor"); }
        }
        private static bool Available(AnimalActor actor) => (bool)Property(actor, "IsAvailableForObservation");
        private static object Property(object value, string name)
        { PropertyInfo property = value.GetType().GetProperty(name); Require(property != null, "Missing interface " + name); return property.GetValue(value); }
        private static void Set(object value, string name, object field) => value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(value, field);
        private static void Log(string message) { File.AppendAllText(Report, message + "\n"); Debug.Log(message); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
