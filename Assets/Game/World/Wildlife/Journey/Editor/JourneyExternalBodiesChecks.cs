using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using Object = UnityEngine.Object;

namespace WeatherHouse.WildlifeJourney.Editor
{
    public static class JourneyExternalBodiesChecks
    {
        [MenuItem("Weather House/Wildlife Journey/Check Mixed Population Avoidance")]
        public static void RunMenu() { Debug.Log(RunLogicChecks()); }

        public static string RunLogicChecks()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run isolated mixed-population checks outside Play mode.");
            return string.Join("\n", CheckLegacyMovement(), CheckJourney(false), CheckJourney(true), CheckLifecycle(), CheckContactExit(), CheckMedia(),
                CheckOccupiedDestination(false), CheckOccupiedDestination(true), CheckOccupiedLanding());
        }

        public static string RunPlayLifecycleCheck()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Lifecycle callbacks require actual Play mode.");
            var root = new GameObject("Temporary mixed-body Play lifecycle check") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Vector3 center = new Vector3(2000, 0, 2000);
                WildlifePopulation population = root.AddComponent<WildlifePopulation>();
                population.worldBounds = new Bounds(center + Vector3.up * 10, new Vector3(20, 20, 20)); population.obstacleMask = 0;
                WildlifeAgent old = Child(root.transform, "Temporary legacy animal", center + Vector3.left * 2).AddComponent<WildlifeAgent>();
                old.scaleRange = Vector2.one; old.bodyRadius = .3f; old.habitatCenter = center;
                population.agents = new[] { old }; population.Initialize();
                JourneyNavigation navigation = root.AddComponent<JourneyNavigation>();
                navigation.surfaceSampler = p => new JourneySurface { valid = true };
                JourneyHerd herd = root.AddComponent<JourneyHerd>(); herd.navigation = navigation; herd.autoAdvance = false;
                herd.members = new[] { new JourneyMember { id = 1, root = Child(root.transform, "Temporary journey sheep", center).transform,
                    bodyLength = 1.2f, profile = new JourneyProfile { bodyRadius = .3f, canSwim = false } } };
                herd.sites = new[] { new JourneyActivitySite { name = "temporary", center = center, radius = 4 } };
                herd.Initialize();
                Vector3 across = center + Vector3.right * 2;
                Require(((ICollection)Get(population, "externalBodies")).Count == 1 && !population.TryMove(old, across, out _),
                    "The real Play fixture did not register its herd body.");
                herd.enabled = false;
                Require(((ICollection)Get(population, "externalBodies")).Count == 0 && population.TryMove(old, across, out _),
                    "Actual Play OnDisable did not immediately release the herd registration.");
                herd.enabled = true;
                Require(((ICollection)Get(population, "externalBodies")).Count == 1 && !population.TryMove(old, across, out _),
                    "Actual Play OnEnable did not immediately restore the herd registration.");
                // Immediate destruction keeps this synchronous check inside its own temporary object lifetime.
                Object.DestroyImmediate(herd);
                Require(((ICollection)Get(population, "externalBodies")).Count == 0 && population.TryMove(old, across, out _),
                    "Actual Play destruction did not release the herd before subsequent movement queries.");
                return "PASS real Play lifecycle: disable unregisters immediately, enable restores once, destruction releases the herd body";
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static string CheckLegacyMovement()
        {
            using (var f = new Fixture(new Vector3(-1.2f, 0, .15f), Vector3.zero))
            {
                Vector3 start = f.old.transform.position;
                Vector3 away = start - f.member.root.position;
                Require(Vector3.Dot(f.population.ExternalSeparation(f.old), away) > .01f,
                    "The old animal received no soft separation from a journey sheep.");
                SetOldState(f.old, WildlifeState.Wander);
                Set(f.old, "destination", new Vector3(4, 0, .15f));
                Set(f.old, "nextDestination", 1000f);
                f.population.Simulate(.05f); f.herd.Simulate(.05f);
                Require(f.old.transform.position.z > start.z + .0001f,
                    "Real WildlifePopulation.Simulate ignored the external body's sideways soft steering.");
                float travelled = Vector3.Distance(start, f.old.transform.position);
                for (int tick = 0; tick < 60; tick++)
                {
                    Vector3 before = f.old.transform.position;
                    f.population.Simulate(.05f); f.herd.Simulate(.05f);
                    float step = Vector3.Distance(before, f.old.transform.position);
                    Require(step <= .0561f && Mathf.Abs(f.old.transform.position.y) < .0001f,
                        "The legacy animal exceeded its walk speed or left the ground.");
                    Require(FlatDistance(f.old.transform.position, f.member.root.position) >= Clearance(f) - .0001f,
                        "The legacy animal entered the journey sheep's hard body clearance.");
                    travelled += step;
                }
                Require(travelled > .2f, "The soft-steering fixture never exercised legacy movement.");
                f.old.transform.position = new Vector3(-2, 0, 0);
                Require(!f.population.TryMove(f.old, new Vector3(2, 0, 0), out _),
                    "Legacy movement crossed an external body between two safe endpoints.");
                f.old.transform.position = new Vector3(-.85f, 0, 0);
                Require(f.population.TryMove(f.old, new Vector3(-1.2f, 0, 0), out _) &&
                    !f.population.TryMove(f.old, new Vector3(-.75f, 0, 0), out _),
                    "An existing cross-controller contact could not separate or was allowed to deepen.");
                return "PASS legacy Simulate applies soft avoidance; full-segment hard spacing and outward-only contact escape hold";
            }
        }

        private static string CheckJourney(bool flying)
        {
            using (var f = new Fixture(new Vector3(0, flying ? 3 : 0, 0), new Vector3(-4, 0, 0), flying))
            {
                Vector3 goal = new Vector3(4, 0, 0), oldStart = f.old.transform.position;
                Require(f.herd.BeginJourney(1), "The mixed-population fixture could not begin a journey.");
                JourneyRoute route = f.navigation.FindRoute(f.member.root.position, goal, f.member.profile);
                Require(route != null, "The isolated flat route was unreachable.");
                Set(f.member, "route", route); Set(f.member, "segment", 0);
                Set(f.member, "destination", goal); Set(f.member, "nextChoice", 0f);
                float lateral = 0;
                for (int tick = 0; tick < 600 && !f.member.Arrived; tick++)
                {
                    Vector3 before = f.member.root.position;
                    f.population.Simulate(.05f); f.herd.Simulate(.05f);
                    Require(Vector3.Distance(before, f.member.root.position) <= .0561f && f.member.Mode == JourneyTravelMode.Walk &&
                        Mathf.Abs(f.member.root.position.y) < .0001f, "Journey avoidance teleported, exceeded walking speed or left the ground.");
                    Require(Vector3.Distance(oldStart, f.old.transform.position) < .0001f, "The static legacy neighbour moved during its fixture.");
                    if (!flying) Require(FlatDistance(f.member.root.position, oldStart) >= Clearance(f) - .0001f,
                        "A journey sheep walked through a stationary legacy animal.");
                    lateral = Mathf.Max(lateral, Mathf.Abs(f.member.root.position.z));
                    Require(f.member.DistanceTravelled < 16f, "The journey sheep circled instead of completing its eight-metre route.");
                }
                Require(f.member.Arrived && Vector3.Distance(f.member.root.position, goal) < .2f,
                    "The mixed-population journey did not arrive within 30s: " + f.member.root.position + "; " + f.member.LastDecision);
                Require(flying ? lateral < .01f : lateral > .5f,
                    flying ? "A high flying animal unnecessarily diverted a ground journey." : "The journey did not physically detour around the legacy body.");
                if (flying)
                {
                    f.member.root.position = Vector3.zero; f.old.transform.position = new Vector3(-2, 3, 0);
                    Require(f.population.ExternalSeparation(f.old).sqrMagnitude < .000001f &&
                        f.population.TryMove(f.old, new Vector3(2, 3, 0), out Vector3 air) && Mathf.Abs(air.y - 3) < .0001f,
                        "A ground sheep blocked a legacy animal travelling on a separate altitude.");
                }
                return string.Format(CultureInfo.InvariantCulture, "PASS {0}: arrives in {1:F2}s, walks {2:F2}m, lateral deviation {3:F2}m",
                    flying ? "both controllers ignore another altitude" : "journey routes around a stationary legacy animal",
                    f.herd.SimulationTime, f.member.DistanceTravelled, lateral);
            }
        }

        private static string CheckLifecycle()
        {
            using (var f = new Fixture(new Vector3(-2, 0, 0), Vector3.zero))
            {
                Require(Blocked(f), "Herd.Initialize did not discover and register with its parent population.");
                f.population.RegisterExternalBody(f.herd, f.member.root, .6f);
                f.population.RegisterExternalBody(f.herd, f.member.root, .6f);
                Require(((ICollection)Get(f.population, "externalBodies")).Count == 1 && f.population.agents.Length == 1 &&
                    f.population.agents[0] == f.old, "External registration duplicated the body or added it to legacy agents.");
                f.herd.enabled = false;
                // Non-ExecuteAlways behaviours do not dispatch Edit-mode callbacks; exercise the disabled-owner query guard here.
                Require(!Blocked(f), "A disabled herd owner still blocked legacy movement.");
                f.herd.enabled = true;
                f.herd.Simulate(.05f); // Edit fixtures have no automatic OnEnable/Update registration tick.
                Require(Blocked(f), "The first simulation tick after enabling a herd did not restore its external bodies.");
                f.member.root.gameObject.SetActive(false);
                Require(!Blocked(f), "An inactive journey member still blocked legacy movement.");
                f.member.root.gameObject.SetActive(true);
                Require(Blocked(f), "Reactivating a journey member did not restore its physical presence.");
                f.population.UnregisterExternalBodies(f.herd);
                Require(!Blocked(f), "Explicit unregister retained the owner's bodies.");
                f.population.RegisterExternalBody(f.herd, f.member.root, .6f);
                Require(Blocked(f), "Explicit registration did not restore the owner after unregister.");
                Object.DestroyImmediate(f.herd);
                Require(!Blocked(f) && ((ICollection)Get(f.population, "externalBodies")).Count == 0,
                    "Destroying the herd left an active-looking external body behind.");
                return "PASS parent discovery, idempotent registration, disabled-owner filtering, resumed simulation, member activity and stale-owner/unregister cleanup";
            }
        }

        private static string CheckContactExit()
        {
            using (var f = new Fixture(Vector3.zero, new Vector3(-.85f, 0, 0)))
            {
                MethodInfo clear = typeof(JourneyHerd).GetMethod("ClearOfOthers", BindingFlags.Instance | BindingFlags.NonPublic);
                Require(clear != null && !(bool)clear.Invoke(f.herd, new object[] { f.member, new Vector3(-.75f, 0, 0), null, null }),
                    "Journey movement allowed an existing legacy-body contact to deepen.");
                Set(f.member, "localTarget", new Vector3(-2, 0, 0));
                float previous = FlatDistance(f.member.root.position, f.old.transform.position);
                for (int tick = 0; tick < 100 && previous < 1.8f; tick++)
                {
                    Vector3 before = f.member.root.position;
                    f.population.Simulate(.05f); f.herd.Simulate(.05f);
                    float distance = FlatDistance(f.member.root.position, f.old.transform.position);
                    Require(distance >= previous - .0001f && Vector3.Distance(before, f.member.root.position) <= .0561f,
                        "A real journey contact escape deepened contact or exceeded walking speed.");
                    previous = distance;
                }
                Require(previous >= 1.8f, "A journey sheep stayed trapped in an existing legacy-body contact.");
                f.old.enabled = false;
                Require((bool)clear.Invoke(f.herd, new object[] { f.member, Vector3.zero, null, null }), "An inactive legacy agent still blocked the journey herd.");
                f.old.enabled = true;
                Require(!(bool)clear.Invoke(f.herd, new object[] { f.member, Vector3.zero, null, null }), "An enabled legacy agent lost its body clearance.");
                Object.DestroyImmediate(f.old);
                Require((bool)clear.Invoke(f.herd, new object[] { f.member, Vector3.zero, null, null }), "A destroyed legacy agent still blocked the journey herd.");
                return "PASS journey contact escapes by real simulation; legacy inactive/reactivated/destroyed bodies update immediately";
            }
        }

        private static string CheckMedia()
        {
            using (var f = new Fixture(new Vector3(-.85f, 0, 0), Vector3.zero))
            {
                MethodInfo clear = typeof(JourneyHerd).GetMethod("ClearOfOthers", BindingFlags.Instance | BindingFlags.NonPublic);
                Vector3 inward = new Vector3(-.75f, 0, 0);
                f.population.RegisterExternalBody(f.herd, f.member.root, .6f, false);
                Require(f.population.ExternalSeparation(f.old).sqrMagnitude < .000001f && f.population.TryMove(f.old, inward, out _),
                    "A non-grounded external body still participated in ground avoidance.");
                f.population.RegisterExternalBody(f.herd, f.member.root, .6f, true);
                Require(f.population.ExternalSeparation(f.old).sqrMagnitude > .001f && !f.population.TryMove(f.old, inward, out _) &&
                    ((ICollection)Get(f.population, "externalBodies")).Count == 1,
                    "Changing an existing body's grounded flag duplicated it or failed to restore ground avoidance.");

                f.old.locomotion = WildlifeLocomotion.Flying; Set(f.old, "altitude", .3f);
                f.old.transform.position = new Vector3(-.85f, .3f, 0);
                Require(f.population.ExternalSeparation(f.old).sqrMagnitude < .000001f && f.population.TryMove(f.old, inward, out _) &&
                    (bool)clear.Invoke(f.herd, new object[] { f.member, inward, null, null }),
                    "An airborne legacy animal within the height band still participated in ground avoidance.");
                Set(f.old, "altitude", 0f); f.old.transform.position = new Vector3(-.85f, 0, 0);
                Require(f.population.ExternalSeparation(f.old).sqrMagnitude > .001f && !f.population.TryMove(f.old, inward, out _) &&
                    !(bool)clear.Invoke(f.herd, new object[] { f.member, inward, null, null }),
                    "A landed legacy bird did not recover ground avoidance in both controllers.");

                f.old.locomotion = WildlifeLocomotion.Swimming; f.old.waterAreaIndex = 0; f.old.swimDepth = 1.2f;
                f.population.waterAreas = new[] { new HabitatVolume { center = new Vector3(0, 1.5f, 0), radii = new Vector2(10, 10) } };
                f.old.transform.position = new Vector3(-.85f, .3f, 0);
                Require(f.population.ExternalSeparation(f.old).sqrMagnitude < .000001f && f.population.TryMove(f.old, inward, out _) &&
                    (bool)clear.Invoke(f.herd, new object[] { f.member, inward, null, null }),
                    "A swimming legacy animal within the height band still participated in ground avoidance.");

                f.population.waterAreas = new HabitatVolume[0]; f.old.locomotion = WildlifeLocomotion.Ground;
                f.old.transform.position = new Vector3(-.85f, 0, 0); f.member.Mode = JourneyTravelMode.Swim;
                typeof(JourneyHerd).GetMethod("RegisterSurroundingBodies", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f.herd, null);
                Require(f.population.ExternalSeparation(f.old).sqrMagnitude < .000001f && f.population.TryMove(f.old, inward, out _) &&
                    (bool)clear.Invoke(f.herd, new object[] { f.member, inward, null, null }),
                    "A journey sheep's Swim mode did not disable ground avoidance in both controllers.");
                f.member.Mode = JourneyTravelMode.Walk;
                typeof(JourneyHerd).GetMethod("RegisterSurroundingBodies", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f.herd, null);
                Require(!f.population.TryMove(f.old, inward, out _), "A journey sheep returning to Walk did not restore its external body.");
                return "PASS grounded updates, airborne/landed birds and both swimming controllers respect movement medium";
            }
        }

        private static string CheckOccupiedDestination(bool afterDeparture)
        {
            using (var f = new Fixture(new Vector3(0, 0, 4), new Vector3(-4, 0, 0)))
            {
                f.herd.sites[1].radius = 2.5f;
                if (!afterDeparture)
                {
                    int placementSeed = unchecked(f.herd.seed + 73856093);
                    Require(JourneyScatter.TryCreate(f.herd.sites[1].center, f.herd.sites[1].radius, f.member.bodyLength + .18f,
                        1, placementSeed, out Vector3[] originalGoals), "The occupied-goal fixture could not predict its first scatter point.");
                    // Occupy the deterministic first candidate before choosing a journey; leave the rest of the site open.
                    f.old.transform.position = originalGoals[0];
                }
                Require(f.herd.BeginJourney(1), "A single resting legacy animal incorrectly made the entire roomy site unreachable.");
                Vector3 goal = (Vector3)Get(f.member, "destination"), oldStart = f.old.transform.position;
                Require(FlatDistance(goal, oldStart) >= Clearance(f),
                    "BeginJourney selected a destination already occupied by a resting legacy animal: goal=" + goal + "; old=" + oldStart);
                if (afterDeparture)
                {
                    for (int tick = 0; tick < 100 && f.member.DistanceTravelled < .25f; tick++)
                    {
                        Vector3 before = f.member.root.position;
                        f.population.Simulate(.05f); f.herd.Simulate(.05f);
                        Require(Vector3.Distance(before, f.member.root.position) <= .0561f &&
                            FlatDistance(f.member.root.position, oldStart) >= Clearance(f) - .0001f,
                            "The departure phase exceeded walking speed or crossed the legacy body.");
                    }
                    Require(f.member.DistanceTravelled >= .25f && !f.member.Arrived && FlatDistance(f.member.root.position, goal) > 2f,
                        "The dynamic fixture did not depart before its reserved destination became occupied.");
                    f.old.transform.position = oldStart = goal;
                }
                while (f.herd.SimulationTime < 45f && !f.member.Arrived)
                {
                    Vector3 before = f.member.root.position;
                    f.population.Simulate(.05f); f.herd.Simulate(.05f);
                    Require(Vector3.Distance(before, f.member.root.position) <= .0561f && f.member.Mode == JourneyTravelMode.Walk &&
                        FlatDistance(f.member.root.position, oldStart) >= Clearance(f) - .0001f,
                        "The occupied-destination journey exceeded walking speed, changed medium or crossed the legacy body.");
                    Require(Vector3.Distance(f.old.transform.position, oldStart) < .0001f,
                        "The resting legacy animal moved, invalidating the occupied-destination regression.");
                    Require(f.member.DistanceTravelled < 20f, "The occupied-destination journey circled without completing.");
                }
                Vector3 actualGoal = (Vector3)Get(f.member, "destination");
                Require(FlatDistance(actualGoal, oldStart) >= Clearance(f) && FlatDistance(actualGoal, f.herd.sites[1].center) <= 2.5f &&
                    (!afterDeparture || FlatDistance(actualGoal, goal) >= Clearance(f)),
                    "The occupied destination was not replaced with a safe free goal within the same activity site.");
                Require(f.member.Arrived && f.herd.TargetSite == 1 && Vector3.Distance(f.member.root.position, actualGoal) < .2f,
                    "The herd did not complete its alternate free destination within 45 seconds.");
                return string.Format(CultureInfo.InvariantCulture,
                    "PASS {0} is replaced with a free destination in the same site; arrives in {1:F2}s after {2:F2}m",
                    afterDeparture ? "destination occupied after departure" : "occupied arrival candidate", f.herd.SimulationTime, f.member.DistanceTravelled);
            }
        }

        private static string CheckOccupiedLanding()
        {
            using (var f = new Fixture(new Vector3(.8f, 0, 0), new Vector3(-.12f, -.14f, 0)))
            {
                f.member.profile.canSwim = true; f.member.profile.waterlineHeight = .14f;
                f.member.Mode = JourneyTravelMode.Swim;
                f.navigation.surfaceSampler = p => p.x < 0
                    ? new JourneySurface { valid = true, groundY = -2, hasWater = true, waterY = 0 }
                    : new JourneySurface { valid = true };
                Vector3 goal = new Vector3(3, 0, 0);
                Require(f.herd.BeginJourney(1), "The shallow landing fixture could not begin a journey.");
                JourneyRoute route = f.navigation.TraceDirectRoute(f.member.root.position, goal, f.member.profile);
                Require(route != null, "The otherwise reachable landing fixture has an invalid water-to-land route.");
                Set(f.member, "route", route); Set(f.member, "segment", 0); Set(f.member, "destination", goal); Set(f.member, "nextChoice", 0f);
                bool walked = false;
                for (int tick = 0; tick < 400 && !f.member.Arrived; tick++)
                {
                    if (tick == 20) f.old.transform.position = new Vector3(0, 0, 4);
                    Vector3 before = f.member.root.position;
                    f.population.Simulate(.05f); f.herd.Simulate(.05f);
                    Require(FlatDistance(before, f.member.root.position) <= .0561f && Mathf.Abs(before.y - f.member.root.position.y) <= .1401f,
                        "The landing fixture exceeded horizontal speed or its explicit .14m support-height transition.");
                    if (f.member.Mode == JourneyTravelMode.Walk)
                    {
                        walked = true;
                        Require(FlatDistance(f.member.root.position, f.old.transform.position) >= Clearance(f) - .0001f,
                            "The first walking step after swimming entered the legacy body's ground clearance.");
                    }
                }
                Require(walked && f.member.Arrived && Vector3.Distance(f.member.root.position, goal) < .2f,
                    "The sheep did not finish landing after the resting legacy animal vacated the bank.");

                // Candidate height controls contact: the previous root lies below the height band, while the next step enters it.
                f.member.Arrived = false; f.member.Mode = JourneyTravelMode.Walk;
                f.member.root.position = new Vector3(-.5f, -.7f, 0); f.old.transform.position = Vector3.zero;
                MethodInfo clear = typeof(JourneyHerd).GetMethod("ClearOfOthers", BindingFlags.Instance | BindingFlags.NonPublic);
                Require(!(bool)clear.Invoke(f.herd, new object[] { f.member, new Vector3(-.3f, -.3f, 0), null, JourneyTravelMode.Walk }),
                    "Ground-body height filtering used the old root height instead of the prospective movement position.");
                return "PASS real Swim-to-Walk landing respects occupied ground, resumes after clearance and tests the prospective support height";
            }
        }

        private sealed class Fixture : IDisposable
        {
            public readonly WildlifePopulation population;
            public readonly WildlifeAgent old;
            public readonly JourneyHerd herd;
            public readonly JourneyMember member;
            public readonly JourneyNavigation navigation;
            private readonly Scene scene;
            private readonly GameObject root;

            public Fixture(Vector3 oldPosition, Vector3 sheepPosition, bool flying = false)
            {
                scene = EditorSceneManager.NewPreviewScene();
                root = new GameObject("Isolated mixed-population regression") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, scene);
                population = root.AddComponent<WildlifePopulation>();
                population.worldBounds = new Bounds(new Vector3(0, 10, 0), new Vector3(60, 20, 60));
                population.obstacleMask = 0;
                var oldBody = Child(root.transform, "Legacy animal", oldPosition);
                old = oldBody.AddComponent<WildlifeAgent>(); old.speciesId = "squirrel"; old.spawnId = 100;
                old.scaleRange = Vector2.one; old.bodyRadius = .3f; old.walkSpeed = 1; old.timidity = old.sociability = 0;
                old.habitatRadius = 25; old.locomotion = flying ? WildlifeLocomotion.Flying : WildlifeLocomotion.Ground;
                old.flightHeight = 3;
                population.agents = new[] { old }; population.Initialize(); SetOldState(old, WildlifeState.Idle);
                var controller = Child(root.transform, "Journey controller", Vector3.zero);
                navigation = controller.AddComponent<JourneyNavigation>();
                navigation.surfaceSampler = p => new JourneySurface { valid = true };
                navigation.nodes = new[] { new JourneyNode { id = "west", position = new Vector3(-4, 0, 0) },
                    new JourneyNode { id = "east", position = new Vector3(4, 0, 0) } };
                navigation.links = new[] { new JourneyLink { from = 0, to = 1 } }; navigation.Rebuild();
                member = new JourneyMember { id = 1, root = Child(controller.transform, "Journey sheep", sheepPosition).transform,
                    bodyLength = 1.2f, sociability = 0, profile = new JourneyProfile { bodyRadius = .3f, canSwim = false, walkSpeed = 1 } };
                herd = controller.AddComponent<JourneyHerd>(); herd.navigation = navigation; herd.autoAdvance = false;
                herd.members = new[] { member };
                herd.sites = new[] { new JourneyActivitySite { name = "origin", center = Vector3.zero, radius = 8 },
                    new JourneyActivitySite { name = "destination", center = new Vector3(4, 0, 0), radius = 1 } };
                herd.Initialize(); Set(member, "nextChoice", 1000f);
            }

            public void Dispose() { Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static GameObject Child(Transform parent, string name, Vector3 position)
        {
            var child = new GameObject(name); child.transform.SetParent(parent, false); child.transform.position = position; return child;
        }
        private static bool Blocked(Fixture f) { return !f.population.TryMove(f.old, new Vector3(2, 0, 0), out _); }
        private static float Clearance(Fixture f) { return f.old.Radius + Mathf.Max(f.member.profile.bodyRadius, f.member.bodyLength * .5f) + .12f; }
        private static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        private static void SetOldState(WildlifeAgent agent, WildlifeState state)
        {
            typeof(WildlifeAgent).GetMethod("SetState", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(agent, new object[] { state, 0f, 1000f });
        }
        private static object Get(object target, string name) { return Field(target, name).GetValue(target); }
        private static void Set(object target, string name, object value) { Field(target, name).SetValue(target, value); }
        private static FieldInfo Field(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Missing fixture field: " + name); return field;
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
