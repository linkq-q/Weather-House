using System;
using System.Collections;
using System.Reflection;
using ForestSpriteCharacter;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.WildlifeJourney;
using Object = UnityEngine.Object;

namespace WeatherHouse.Hillside.Editor
{
    // Uses reflection only for newly added seams, so this file also runs against the pre-fix runtime.
    public static class WildlifePlayerCollisionChecks
    {
        [MenuItem("Weather House/Wildlife/Check Player Collision")]
        public static void RunMenu() { Debug.Log(Application.isPlaying ? RunPlayChecks() : RunLogicChecks()); }

        public static string RunLogicChecks()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run logic checks in Edit mode.");
            CheckAnimalSweepAndHeight(); CheckLanding(); CheckLegacySimulation(); CheckJourney();
            return "PASS animal swept contact, outward-only escape and actual capsule centre/height\n" +
                "PASS legacy Population.Simulate reacts to the player\nPASS journey sheep walks around the stationary player without overlap";
        }

        public static string RunPlayChecks()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Native CharacterController movement and lifecycle require actual Play mode.");
            CheckPlayerMovement(); CheckControllerPipeline(); CheckVerticalPlayer(); CheckPostMoveCeiling(); CheckMultipleBodies(); CheckFalseSupport(); CheckPlayLifecycle();
            return "PASS actual CharacterController.Move cannot pass through a legacy animal, including a long sweep\n" +
                "PASS production AdvanceMovement calls water then animal constraint and preserves vertical displacement\n" +
                "PASS diagonal existing-contact escape cannot bypass the other axis\n" +
                "PASS actual post-Move animal ceiling contact retains Above and stops upward speed\n" +
                "PASS multiple-body and population order changes preserve a clear swept segment\n" +
                "PASS final Below support is rebuilt after axis clipping; invalidated contact cannot grant grounded\n" +
                "PASS multiple populations and actual enable/disable/destroy registration";
        }

        public static string RunControllerChecks()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Controller integration requires actual Play mode.");
            CheckControllerPipeline();
            return "PASS production AdvanceMovement calls water then animal constraint before native CharacterController.Move";
        }

        public static string RunLandingChecks()
        { CheckLanding(); return "PASS real bird Landing rejects a new vertical player overlap and resumes Takeoff"; }

        public static void RunJourneyChecks()
        { CheckJourney(); Debug.Log("PASS journey sheep walks around the stationary player without overlap"); }

        public static string RunVerticalChecks()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Vertical controller integration requires actual Play mode.");
            CheckVerticalPlayer(); return "PASS actual controller lands on an animal, reports grounded and can jump away";
        }

        public static string RunNativeWallChecks()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Native wall/support integration requires actual Play mode.");
            for (int wallCase = 0; wallCase < 2; wallCase++)
            using (var f = new Fixture(true))
            {
                if (wallCase == 1)
                {
                    var wall = new GameObject("Native wall clips the predicted animal landing");
                    wall.transform.SetParent(f.root.transform);
                    wall.transform.position = f.center + new Vector3(-.42f, 2f, 0);
                    wall.AddComponent<BoxCollider>().size = new Vector3(.1f, 4f, 4f);
                }
                PutPlayer(f, f.center + new Vector3(-.75f, .68f, 0));
                // One ordinary 50 ms walk step descends 10 cm. The virtual landing is safe,
                // but the native wall may prevent the capsule from reaching that animal.
                f.movement.walkSpeed = 2.25f;
                Set(f.movement, "verticalSpeed", -2f - f.movement.gravity * .05f);
                Advance(f, Vector3.right, false, .05f);
                Vector3 actual = f.player.transform.position - f.center;
                if (wallCase == 0)
                    Require(actual.x > -.67f && actual.y >= .599f && actual.y <= .62f && f.movement.IsGrounded,
                        "The unobstructed control did not land on the animal: " + actual.ToString("F4"));
                else
                {
                    Require(actual.x < -.68f && !f.capsule.isGrounded && actual.y > .5f,
                        "The real wall did not invalidate the predicted landing: " + actual.ToString("F4"));
                    Require(!f.movement.IsGrounded,
                        "Native CharacterController.Move stopped beside the animal, but predicted Below still grants grounded: " +
                        actual.ToString("F4") + "; flags=" + f.movement.AnimalCollisionFlags);
                }
            }
            return "PASS native wall clipping cannot retain predicted animal support; unobstructed landing still reports grounded";
        }

        public static string RunShapeChecks()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Measured body/player integration requires actual Play mode.");
            using (var f = new Fixture(true))
            {
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube); visual.name = "Long tall animal mesh";
                Object.DestroyImmediate(visual.GetComponent<Collider>()); visual.transform.SetParent(f.old.transform, false);
                visual.transform.localPosition = new Vector3(0, .6f, .4f); visual.transform.localScale = new Vector3(.4f, 1.2f, 2.2f);
                f.population.Initialize();
                PutPlayer(f, f.center + Vector3.forward * 2); StepPlayer(f, Vector3.back * 4);
                Require(f.player.transform.position.z > f.center.z + 1.85f,
                    "The player entered the measured animal's front half while its old small Radius was still clear.");
                PutPlayer(f, f.center); f.old.transform.position = f.center + Vector3.forward * 2;
                Require(!f.population.TryMove(f.old, f.center + Vector3.forward * 1.4f, out _),
                    "The animal's measured front half moved into the stationary player.");
                f.old.transform.position = f.center;
                PutPlayer(f, f.center + new Vector3(-2, 1, 0)); StepPlayer(f, Vector3.right * 4);
                Require(f.player.transform.position.x < f.center.x - 1.85f,
                    "A player's capsule crossed the animal above Radius*2 but below the measured mesh top.");
                PutPlayer(f, f.center + new Vector3(-2, 2, 0)); StepPlayer(f, Vector3.right * 4);
                Require(f.player.transform.position.x > f.center.x + 1.99f,
                    "The measured horizontal radius was incorrectly reused as the animal's vertical height.");
                PutPlayer(f, f.center + Vector3.up * 2);
                for (int i = 0; i < 25; i++) Advance(f, Vector3.zero, false, .05f);
                Require(f.player.transform.position.y >= 1.199f && f.player.transform.position.y <= 1.22f && f.movement.IsGrounded,
                    "Player support did not use the measured 1.2m animal top.");
            }
            return "PASS actual measured animal length/height are used in both player directions and final support; overhead passage remains clear";
        }

        private static void CheckMultipleBodies()
        {
            for (int order = 0; order < 2; order++)
            using (var f = new Fixture(true))
            {
                var other = new GameObject("Second animal"); other.transform.SetParent(f.root.transform);
                WildlifeAgent second = other.AddComponent<WildlifeAgent>(); second.scaleRange = Vector2.one; second.bodyRadius = .3f;
                second.timidity = second.sociability = 0; second.habitatCenter = f.center; second.habitatRadius = 10;
                f.old.transform.position = f.center + Vector3.right * (order == 0 ? 0f : 1.5f);
                second.transform.position = f.center + Vector3.right * (order == 0 ? 1.5f : 0f);
                WildlifePopulation extra = f.root.AddComponent<WildlifePopulation>(); extra.player = f.player.transform;
                extra.worldBounds = f.population.worldBounds; extra.obstacleMask = 0; extra.agents = new[] { second }; extra.Initialize();
                PutPlayer(f, f.center + new Vector3(-2, 2, 0));
                Vector3 start = f.player.transform.position, allowed = Constrained(f, new Vector3(4, -2, 0));
                foreach (WildlifeAgent animal in new[] { f.old, second })
                for (int i = 0; i <= 200; i++)
                {
                    Vector3 point = start + allowed * (i / 200f);
                    Require(!(Flat(point, animal.transform.position) < animal.Radius + .25f + .119f &&
                        point.y < animal.transform.position.y + animal.Radius * 2f - .001f && point.y + 1.4f > animal.transform.position.y + .001f),
                        "A later population's axis clipping made the final segment cross a previously checked animal.");
                }
                f.capsule.Move(allowed);
                Require((f.player.transform.position - start - allowed).magnitude < .03f,
                    "The actual controller did not execute the validated multi-body segment.");
            }
        }

        private static void CheckFalseSupport()
        {
            using (var f = new Fixture(true))
            {
                // This later side collision shortens X after the earlier body shortened Y.
                f.old.transform.position = f.center + Vector3.up;
                var other = new GameObject("Earlier ground contact"); other.transform.SetParent(f.root.transform);
                other.transform.position = f.center + Vector3.right;
                WildlifeAgent second = other.AddComponent<WildlifeAgent>(); second.scaleRange = Vector2.one; second.bodyRadius = .3f;
                second.timidity = second.sociability = 0; second.habitatCenter = f.center; second.habitatRadius = 10;
                WildlifePopulation extra = f.root.AddComponent<WildlifePopulation>(); extra.player = f.player.transform;
                extra.worldBounds = f.population.worldBounds; extra.obstacleMask = 0; extra.agents = new[] { second }; extra.Initialize();
                PutPlayer(f, f.center + new Vector3(-2, 2, 0));
                Vector3 allowed = Constrained(f, new Vector3(4, -2, 0));
                f.capsule.Move(allowed);
                f.movement.RefreshAnimalContacts?.Invoke(-2f);
                Require(!f.movement.IsGrounded && f.player.transform.position.y > .5f,
                    "An invalidated intermediate Below contact made the player grounded while its feet were unsupported.");
            }
        }

        private static void CheckVerticalPlayer()
        {
            using (var f = new Fixture(true))
            {
                PutPlayer(f, f.center + Vector3.up * 2);
                for (int i = 0; i < 25; i++) Advance(f, Vector3.zero, false, .05f);
                Require(f.player.transform.position.y >= .599f && f.player.transform.position.y <= .62f && f.movement.IsGrounded,
                    "A player descending above the animal passed through it or never acquired virtual grounded support.");
                Advance(f, Vector3.zero, true, .05f);
                for (int i = 0; i < 4; i++) Advance(f, Vector3.zero, false, .05f);
                Require(f.player.transform.position.y > .7f && f.movement.VerticalSpeed > 0f,
                    "The player could not jump upward away from the animal's virtual support.");
                PutPlayer(f, f.center + Vector3.up * .4f);
                Vector3 before = f.player.transform.position;
                StepPlayer(f, Vector3.up * .5f);
                Require(f.player.transform.position.y > before.y + .49f, "An existing vertical contact could not leave upward.");
            }
        }

        private static void CheckLanding()
        {
            using (var f = new Fixture(Application.isPlaying))
            {
                PutPlayer(f, f.center);
                f.old.locomotion = WildlifeLocomotion.Flying; f.old.flightHeight = 2f;
                f.old.transform.position = f.center + Vector3.up * 2f; f.old.Initialize(f.population);
                Set(f.old, "altitude", 2f); Set(f.old, "stateSince", 0f); Set(f.old, "stateUntil", 1.6f);
                typeof(WildlifeAgent).GetProperty("State").SetValue(f.old, WildlifeState.Landing);
                bool tookOff = false;
                for (int i = 0; i < 40 && !tookOff; i++)
                {
                    f.population.Simulate(.05f);
                    Require(f.old.transform.position.y >= 1.399f, "A descending bird entered the player's capsule before abandoning Landing.");
                    tookOff = f.old.State == WildlifeState.Takeoff;
                }
                Require(tookOff, "The blocked landing did not resume Takeoff.");
                f.old.transform.position = f.center + Vector3.up * .9f; Set(f.old, "altitude", 2f);
                Require(f.population.TryMove(f.old, f.old.transform.position, out _), "An existing bird contact could not separate upward.");
                Set(f.old, "altitude", .2f);
                Require(!f.population.TryMove(f.old, f.old.transform.position, out _), "An existing bird contact moved deeper through the player.");
            }
        }

        private static void CheckControllerPipeline()
        {
            using (var f = new Fixture(true))
            {
                bool waterCalled = false, animalCalled = false;
                Vector3 waterResult = default, animalInput = default, animalResult = default;
                f.movement.ConstrainDisplacement = d => { waterCalled = true; waterResult = new Vector3(d.x * .5f, d.y, d.z * .5f); return waterResult; };
                PropertyInfo hook = typeof(ForestSpriteDemoController).GetProperty("ConstrainAnimalDisplacement");
                if (hook?.GetValue(f.movement) is Func<Vector3, Vector3> original)
                    hook.SetValue(f.movement, (Func<Vector3, Vector3>)(d => { animalCalled = true; animalInput = d; animalResult = original(d); return animalResult; }));
                f.movement.walkSpeed = 80f;
                Advance(f, Vector3.right, false, .05f);
                Require(waterCalled && animalCalled && (waterResult - animalInput).sqrMagnitude < .00000001f,
                    "Production AdvanceMovement did not call the water hook followed by the animal hook.");
                Require(Mathf.Abs(animalInput.y - animalResult.y) < .000001f && animalResult.x <= animalInput.x &&
                    f.player.transform.position.x <= f.center.x - .669f && f.player.transform.position.x > f.center.x - 1.5f &&
                    Mathf.Abs(f.player.transform.position.y) < .04f,
                    "Production controller crossed the animal, expanded the water-safe displacement, or altered vertical movement.");
            }
        }

        private static void CheckPlayerMovement()
        {
            using (var f = new Fixture(true))
            {
                // Before the fix, the real controller crosses the collider-free animal and ends at +2m.
                StepPlayer(f, Vector3.right * 4);
                Require(f.player.transform.position.x <= f.center.x - .669f && f.player.transform.position.x > f.center.x - 1.5f,
                    "Actual CharacterController.Move crossed the legacy animal between two safe endpoints.");
                PutPlayer(f, f.center + Vector3.left * .5f);
                Vector3 before = f.player.transform.position;
                StepPlayer(f, Vector3.right * 2);
                Require(Vector3.Distance(before, f.player.transform.position) < .001f,
                    "Existing contact crossed the animal centre and escaped from the opposite side.");
                StepPlayer(f, Vector3.left * .3f);
                Require(f.player.transform.position.x < before.x - .29f, "Existing player contact could not separate outward.");
                PutPlayer(f, f.center + Vector3.left * .5f); before = f.player.transform.position;
                StepPlayer(f, new Vector3(2f, .001f, 0));
                Require(f.player.transform.position.x < before.x + .001f,
                    "A tiny upward escape component allowed the overlapping player to cross the animal horizontally.");
                PutPlayer(f, f.center + Vector3.up * .4f); before = f.player.transform.position;
                StepPlayer(f, new Vector3(.001f, -2f, 0));
                Require(f.player.transform.position.y > before.y - .001f,
                    "A tiny horizontal escape component allowed the overlapping player to cross the animal vertically.");
                PutPlayer(f, f.center + new Vector3(-2, 3, 0));
                StepPlayer(f, Vector3.right * 4);
                Require(f.player.transform.position.x > f.center.x + 1.99f, "An animal below the player's actual capsule blocked a bridge-height crossing.");

                PutPlayer(f, f.center + Vector3.left * 2);
                bool waterCalled = false;
                Func<Vector3, Vector3> water = d => { waterCalled = true; return new Vector3(d.x * .1f, d.y, d.z * .1f); };
                f.movement.ConstrainDisplacement = water;
                Vector3 allowed = Constrained(f, new Vector3(4, .13f, 0));
                Require(waterCalled && allowed.x <= .40001f && Mathf.Abs(allowed.y - .13f) < .00001f &&
                    f.movement.ConstrainDisplacement == water, "Animal constraint replaced the water hook, expanded its safe segment or changed vertical motion.");
            }
        }

        private static void CheckAnimalSweepAndHeight()
        {
            using (var f = new Fixture())
            {
                PutPlayer(f, f.center);
                f.old.transform.position = f.center + Vector3.left * 2;
                Require(!f.population.TryMove(f.old, f.center + Vector3.right * 2, out _),
                    "An animal crossed the player's capsule between two safe endpoints.");
                f.old.transform.position = f.center + Vector3.left * .5f;
                Require(f.population.TryMove(f.old, f.center + Vector3.left * .8f, out _) &&
                    !f.population.TryMove(f.old, f.center + Vector3.right * 2, out _),
                    "An existing animal contact could not leave, or crossed through the player centre.");
                f.capsule.center = new Vector3(1.5f, .7f, 0);
                f.old.transform.position = f.center + new Vector3(1.5f, 0, -2);
                Require(!f.population.TryMove(f.old, f.center + new Vector3(1.5f, 0, 2), out _),
                    "The collision body used player root position instead of the actual offset capsule centre.");
                PutPlayer(f, f.center + Vector3.up * 3);
                Require(f.population.TryMove(f.old, f.center + new Vector3(1.5f, 0, 2), out _),
                    "The player on an upper floor blocked an animal underneath.");
            }
        }

        private static void CheckLegacySimulation()
        {
            using (var f = new Fixture())
            {
                PutPlayer(f, f.center);
                f.old.transform.position = f.center + new Vector3(-.7f, 0, .3f);
                Set(f.old, "destination", f.center + new Vector3(3, 0, .3f)); Set(f.old, "nextDestination", 1000f);
                Set(f.old, "stateUntil", 1000f);
                typeof(WildlifeAgent).GetProperty("State").SetValue(f.old, WildlifeState.Wander);
                Vector3 start = f.old.transform.position;
                f.population.Simulate(.05f);
                Vector3 first = f.old.transform.position, separation = (Vector3)Get(f.old, "separation");
                Require(separation.z > .01f && first.z > start.z + .003f && Vector3.Distance(first, start) <= .0561f,
                    "The actual first legacy Think/Move did not steer away from the player before contact: separation=" + separation + ", move=" + (first - start));
                for (int tick = 0; tick < 100; tick++)
                {
                    Vector3 before = f.old.transform.position; f.population.Simulate(.05f);
                    Require(Flat(f.old.transform.position, f.player.transform.position) >= .669f &&
                        Vector3.Distance(before, f.old.transform.position) <= .0561f, "Real legacy movement entered the player or exceeded walking speed.");
                }
                Debug.Log("Player separation integration: first move=" + (first - start) + ", separation=" + separation +
                    ", final=" + (f.old.transform.position - f.center) + ", state=" + f.old.State);
            }
        }

        private static void CheckJourney()
        {
            using (var f = new Fixture())
            {
                f.old.gameObject.SetActive(false); PutPlayer(f, f.center);
                JourneyNavigation nav = f.root.AddComponent<JourneyNavigation>();
                nav.surfaceSampler = p => new JourneySurface { valid = true, groundY = 0 };
                nav.nodes = new[] { new JourneyNode { position = f.center + Vector3.left * 4 }, new JourneyNode { position = f.center + Vector3.right * 4 } };
                nav.links = new[] { new JourneyLink { from = 0, to = 1, bidirectional = true } }; nav.Rebuild();
                JourneyHerd herd = f.root.AddComponent<JourneyHerd>(); herd.navigation = nav; herd.surroundingPopulation = f.population;
                herd.autoAdvance = false; herd.travelSpread = 0;
                var body = new GameObject("Journey sheep").transform; body.SetParent(f.root.transform); body.position = nav.nodes[0].position;
                var member = new JourneyMember { id = 1, root = body, bodyLength = 1.2f, sociability = 0,
                    profile = new JourneyProfile { bodyRadius = .3f, walkSpeed = 1f, canSwim = false } };
                herd.members = new[] { member };
                herd.sites = new[] { new JourneyActivitySite { center = nav.nodes[0].position, radius = 2 }, new JourneyActivitySite { center = nav.nodes[1].position, radius = 2 } };
                herd.Initialize(); herd.BeginJourney(1);
                int crowdFrames = 0;
                float crossTrack = 0;
                for (int tick = 0; tick < 900 && !member.Arrived; tick++)
                {
                    Vector3 before = body.position; herd.Simulate(.05f);
                    if (Get(member, "crowdPath") != null) crowdFrames++;
                    crossTrack = Mathf.Max(crossTrack, Mathf.Abs(body.position.z - f.center.z));
                    Require(Flat(body.position, f.player.transform.position) >= .969f && Vector3.Distance(before, body.position) <= .0561f,
                        "Journey movement entered the player's body or exceeded walking speed.");
                }
                Debug.Log("Journey player contact: position=" + (body.position - f.center) + ", state=" + member.State +
                    ", arrived=" + member.Arrived + ", crowdFrames=" + crowdFrames + ", crossTrack=" + crossTrack +
                    ", segment=" + Get(member, "segment") + ", destination=" + ((Vector3)Get(member, "destination") - f.center) +
                    ", decision=" + member.LastDecision);
                Require(member.Arrived && body.position.x > f.center.x + 2 && crowdFrames > 0,
                    "The journey sheep did not use its local crowd path to walk around the player.");
            }
        }

        private static void CheckPlayLifecycle()
        {
            using (var f = new Fixture(true))
            {
                WildlifePopulation extra = f.root.AddComponent<WildlifePopulation>();
                extra.player = f.player.transform; extra.agents = new[] { f.old }; extra.worldBounds = f.population.worldBounds; extra.Initialize();
                f.population.Initialize();
                Component contact = Contact(f);
                PropertyInfo postMove = typeof(ForestSpriteDemoController).GetProperty("RefreshAnimalContacts");
                Require(contact != null && f.player.GetComponents(contact.GetType()).Length == 1 && ((ICollection)Get(contact, "populations")).Count == 2,
                    "Repeated initialization or multiple populations duplicated the player constraint.");
                Require(postMove?.GetValue(f.movement) != null, "The actual-position contact callback was not registered.");
                f.population.enabled = false;
                Require(((ICollection)Get(contact, "populations")).Count == 1 && Constrained(f, Vector3.right * 4).x < 2 && postMove.GetValue(f.movement) != null,
                    "Disabling one population removed the other population's body constraint or left a registration behind.");
                f.movement.AnimalCollisionFlags = CollisionFlags.Below;
                Object.DestroyImmediate(extra);
                Require(((ICollection)Get(contact, "populations")).Count == 0 && Constrained(f, Vector3.right * 4).x > 3.99f &&
                    postMove.GetValue(f.movement) == null && f.movement.AnimalCollisionFlags == CollisionFlags.None,
                    "Destroying the last population retained its constraint, post-Move callback or contact flags.");
                f.population.enabled = true;
                Require(((ICollection)Get(contact, "populations")).Count == 1 && Constrained(f, Vector3.right * 4).x < 2 && postMove.GetValue(f.movement) != null,
                    "Re-enabling the population did not restore exactly one constraint.");
                PropertyInfo hook = typeof(ForestSpriteDemoController).GetProperty("ConstrainAnimalDisplacement");
                f.movement.AnimalCollisionFlags = CollisionFlags.Below;
                ((Behaviour)contact).enabled = false;
                Require(hook.GetValue(f.movement) == null && postMove.GetValue(f.movement) == null &&
                    f.movement.AnimalCollisionFlags == CollisionFlags.None,
                    "Disabling the contact component retained one of its callbacks or grounded flags.");
                ((Behaviour)contact).enabled = true;
                Func<Vector3, Vector3> anotherOwner = d => d;
                Action<float> anotherPostMove = y => { };
                hook.SetValue(f.movement, anotherOwner); postMove.SetValue(f.movement, anotherPostMove);
                f.movement.AnimalCollisionFlags = CollisionFlags.Above;
                ((Behaviour)contact).enabled = false;
                Require(ReferenceEquals(hook.GetValue(f.movement), anotherOwner) &&
                    ReferenceEquals(postMove.GetValue(f.movement), anotherPostMove) && f.movement.AnimalCollisionFlags == CollisionFlags.Above,
                    "Disabling the constraint cleared another callback owner's delegate or contact flags.");
            }
        }

        private static void CheckPostMoveCeiling()
        {
            using (var f = new Fixture(true))
            {
                f.old.transform.position = f.center + Vector3.up * 2f;
                PutPlayer(f, f.center + Vector3.up * .4f);
                Set(f.movement, "verticalSpeed", 5f - f.movement.gravity * .05f);
                Advance(f, Vector3.zero, false, .05f);
                Require(f.player.transform.position.y >= .59f && f.player.transform.position.y <= .6f &&
                    (f.movement.AnimalCollisionFlags & CollisionFlags.Above) != 0 &&
                    Mathf.Abs(f.movement.VerticalSpeed) < .00001f && !f.movement.IsGrounded,
                    "Actual post-Move animal ceiling contact lost Above, upward-speed cancellation or its airborne state.");
            }
        }

        private sealed class Fixture : IDisposable
        {
            public readonly Vector3 center = new Vector3(2000, 0, 2000);
            public readonly GameObject root, player;
            public readonly CharacterController capsule;
            public readonly ForestSpriteDemoController movement;
            public readonly WildlifePopulation population;
            public readonly WildlifeAgent old;
            private readonly Scene preview;
            private readonly AnimatorController animation;
            public Fixture(bool play = false)
            {
                root = new GameObject("Temporary animal-player collision checks");
                if (!play) { preview = EditorSceneManager.NewPreviewScene(); SceneManager.MoveGameObjectToScene(root, preview); }
                else
                {
                    Require(root.scene.GetPhysicsScene() == Physics.defaultPhysicsScene, "Native physics fixture must use the default physics scene.");
                    var floor = new GameObject("Physics floor"); floor.transform.SetParent(root.transform); floor.transform.position = center + Vector3.down * .5f;
                    floor.AddComponent<BoxCollider>().size = new Vector3(20, 1, 20);
                }
                player = new GameObject("Actual capsule player"); player.transform.SetParent(root.transform); player.SetActive(false);
                player.transform.position = center + Vector3.left * 2;
                capsule = player.AddComponent<CharacterController>(); capsule.radius = .25f; capsule.height = 1.4f;
                capsule.center = Vector3.up * .7f; capsule.skinWidth = .01f; capsule.minMoveDistance = 0;
                if (play) Require(player.scene.GetPhysicsScene() == Physics.defaultPhysicsScene, "Native controller must share the default physics scene with the fixture floor.");
                var visual = new GameObject("Visual"); visual.transform.SetParent(player.transform, false);
                var animator = visual.AddComponent<Animator>(); animation = new AnimatorController(); animation.AddLayer("Base Layer");
                animation.layers[0].stateMachine.AddState("Idle"); animation.layers[0].stateMachine.AddState("Jump"); animator.runtimeAnimatorController = animation;
                movement = player.AddComponent<ForestSpriteDemoController>(); movement.animator = animator; movement.visual = visual.transform;
                movement.enabled = false; movement.externalCameraControl = true; movement.showControls = false;
                player.SetActive(true);
                population = root.AddComponent<WildlifePopulation>(); population.worldBounds = new Bounds(center + Vector3.up * 10, new Vector3(40, 20, 40));
                population.obstacleMask = 0; population.player = player.transform;
                var animal = new GameObject("Collider-free legacy animal"); animal.transform.SetParent(root.transform); animal.transform.position = center;
                old = animal.AddComponent<WildlifeAgent>(); old.scaleRange = Vector2.one; old.bodyRadius = .3f;
                old.walkSpeed = 1; old.timidity = old.sociability = 0; old.habitatCenter = center; old.habitatRadius = 10;
                population.agents = new[] { old }; population.Initialize(); Physics.SyncTransforms();
            }
            public void Dispose()
            {
                Object.DestroyImmediate(root); Object.DestroyImmediate(animation);
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static Component Contact(Fixture f) => f.player.GetComponent("WildlifePlayerCollision");
        private static Vector3 Constrained(Fixture f, Vector3 displacement)
        {
            if (f.movement.ConstrainDisplacement != null) displacement = f.movement.ConstrainDisplacement(displacement);
            var hook = typeof(ForestSpriteDemoController).GetProperty("ConstrainAnimalDisplacement");
            return hook?.GetValue(f.movement) is Func<Vector3, Vector3> constraint ? constraint(displacement) : displacement;
        }
        private static void StepPlayer(Fixture f, Vector3 displacement)
        {
            f.capsule.Move(Constrained(f, displacement));
            f.movement.RefreshAnimalContacts?.Invoke(displacement.y);
        }
        private static void Advance(Fixture f, Vector3 direction, bool jump, float dt)
        { typeof(ForestSpriteDemoController).GetMethod("AdvanceMovement", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(f.movement, new object[] { direction, false, jump, dt }); }
        private static void PutPlayer(Fixture f, Vector3 position)
        { f.capsule.enabled = false; f.player.transform.position = position; f.capsule.enabled = true; Physics.SyncTransforms(); }
        private static object Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
