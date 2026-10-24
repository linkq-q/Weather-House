using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Hillside.Editor
{
    public static class WildlifeSolitaryChecks
    {
        [MenuItem("Weather House/Hillside/Check Solitary Wildlife")]
        public static void RunMenu() { Debug.Log(RunLogicChecks()); }

        public static string RunLogicChecks()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run isolated solitary checks outside Play mode.");
            string strategies = WildlifeSolitaryStrategyChecks.RunLogicChecks();
            CheckIndependentAgents(); CheckAlarmRecovery(); CheckModesAndHome(); CheckPoseRestoration(); CheckBlockedHome();
            return strategies + "\nPASS real Population.Simulate ignores distant group centers for solitary animals\n" +
                "PASS real alarm/flee interrupts work and returns to personal home\nPASS explicit opt-in, ambient default and original habitat bounds\n" +
                "PASS actual head Transform overlay is bounded, non-accumulating and restored by the disable callback\n" +
                "PASS real Population.Simulate returns around a forbidden pond without restoring fatigue along its detour";
        }

        private static void CheckIndependentAgents()
        {
            using (var east = new Fixture(20)) using (var west = new Fixture(-20))
            {
                for (int tick = 0; tick < 600; tick++)
                {
                    Vector3 before = east.fox.transform.position;
                    east.population.Simulate(.05f); west.population.Simulate(.05f);
                    Require(Vector3.Distance(east.fox.transform.position, west.fox.transform.position) < .0001f &&
                        Vector3.Distance(east.fox.Destination, west.fox.Destination) < .0001f,
                        "Moving a distant same-group fox changed the personal animal's target or movement.");
                    Require(Vector3.Distance(before, east.fox.transform.position) <= .0561f && east.fox.SolitaryRoutine.Contains(east.fox.transform.position),
                        "The solitary fox exceeded walking speed or escaped its existing habitat region.");
                }
                Require(east.fox.SolitaryRoutine.DecisionCount >= 2, "The independence fixture never completed multiple decisions.");
            }
        }

        private static void CheckAlarmRecovery()
        {
            using (var f = new Fixture(20))
            {
                var routine = f.fox.SolitaryRoutine; routine.Hunger = .9f; routine.Thirst = routine.Fatigue = .05f;
                for (int i = 0; i < 500 && !routine.IsWorking; i++) f.population.Simulate(.05f);
                Require(routine.IsWorking && routine.Activity == SolitaryActivity.Forage, "The alarm fixture did not reach real foraging work.");
                f.fox.timidity = .7f; float hunger = routine.Hunger;
                f.population.EmitStartle(f.fox.transform.position, 4, 1);
                Require(f.fox.State == WildlifeState.Alert && !routine.IsWorking && f.fox.AcceptedAlarmCount == 1,
                    "The actual alarm did not interrupt solitary work.");
                for (int i = 0; i < 20; i++) f.population.Simulate(.05f);
                Require(routine.Hunger >= hunger, "The alarmed fox continued restoring hunger.");
                bool homeTarget = false, resumed = false;
                for (int i = 0; i < 1200 && !resumed; i++)
                {
                    f.population.Simulate(.05f);
                    homeTarget |= routine.HasTarget && routine.Activity == SolitaryActivity.Rest && Vector3.Distance(routine.Target, routine.Home) < .001f;
                    resumed |= homeTarget && routine.IsWorking && routine.Activity == SolitaryActivity.Forage;
                }
                Require(homeTarget && resumed, "Actual fleeing never returned home and resumed the interrupted personal need.");
            }
        }

        private static void CheckModesAndHome()
        {
            using (var f = new Fixture(20))
            {
                f.fox.transform.position = new Vector3(2, 0, 0);
                // First initialization already captured the individual home; reinitialization must not move it to the current foot position.
                f.fox.Initialize(f.population);
                Require(f.fox.SolitaryRoutine.Home == Vector3.zero && f.fox.habitatCenter == Vector3.zero,
                    "Reinitialization moved the personal home or rewrote the original region.");
                f.fox.routineMode = WildlifeRoutineMode.Ambient; f.fox.Initialize(f.population);
                Require(f.fox.SolitaryRoutine == null, "The explicit ambient/off mode did not restore original decisions.");
                f.fox.speciesId = "rabbit"; f.fox.routineMode = default; f.fox.Initialize(f.population);
                Require(f.fox.SolitaryRoutine == null, "The default mode changed a non-fox's original controller.");
                f.fox.routineMode = WildlifeRoutineMode.Solitary; f.fox.Initialize(f.population);
                Require(f.fox.SolitaryRoutine != null && f.fox.SolitaryRoutine.Home == f.fox.transform.position,
                    "Explicit solitary mode could not reuse the strategy for another ground species.");
            }
        }

        private static void CheckPoseRestoration()
        {
            using (var f = new Fixture(20))
            {
                var head = new GameObject("Head").transform; head.SetParent(f.fox.transform, false);
                Quaternion original = Quaternion.Euler(4, 12, -3); head.localRotation = original;
                f.fox.Initialize(f.population);
                f.fox.SolitaryRoutine.Hunger = .9f; f.fox.SolitaryRoutine.Thirst = f.fox.SolitaryRoutine.Fatigue = .05f;
                for (int i = 0; i < 500 && !f.fox.SolitaryRoutine.IsWorking; i++) f.population.Simulate(.05f);
                Require(f.fox.SolitaryRoutine.IsWorking, "The head-pose fixture never reached its activity.");
                MethodInfo late = typeof(WildlifeAgent).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo update = typeof(WildlifeAgent).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                late.Invoke(f.fox, null); Quaternion first = head.localRotation;
                Require(Quaternion.Angle(original, first) > 2f && Quaternion.Angle(original, first) < 15f,
                    "The low-amplitude forage pose was absent or excessive.");
                update.Invoke(f.fox, null);
                Require(Quaternion.Angle(original, head.localRotation) < .001f, "The next frame did not restore the pre-overlay pose.");
                late.Invoke(f.fox, null);
                Require(Quaternion.Angle(first, head.localRotation) < .001f, "Repeated frames accumulated the additive pose.");
                // Edit mode does not dispatch non-ExecuteAlways OnDisable; exercise the actual callback explicitly.
                typeof(WildlifeAgent).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f.fox, null);
                Require(Quaternion.Angle(original, head.localRotation) < .001f, "Disabling the agent left its additive head pose behind.");
            }
        }

        private static void CheckBlockedHome()
        {
            using (var f = new Fixture(20))
            {
                f.fox.transform.position = new Vector3(4, 0, 0);
                var pond = new HabitatVolume { id = "Forbidden return-home pond", center = new Vector3(2, 0, 0), radii = new Vector2(.3f, .9f) };
                f.population.waterAreas = new[] { pond };
                var routine = f.fox.SolitaryRoutine;
                routine.Hunger = routine.Thirst = routine.Fatigue = .05f; routine.Interrupt(true);
                Require(!f.population.TryMove(f.fox, routine.Home, out _), "The real movement fixture did not block the direct route home.");
                bool detour = false, arrived = false;
                for (int i = 0; i < 900 && !arrived; i++)
                {
                    Vector3 before = f.fox.transform.position; float fatigue = routine.Fatigue;
                    f.population.Simulate(.05f);
                    Vector3 position = f.fox.transform.position;
                    Require(Vector3.Distance(before, position) <= .0561f && !pond.Contains(position, -f.fox.Radius),
                        "The real return detour exceeded walking speed or entered forbidden water.");
                    if (Vector3.Distance(position, routine.Home) > .4f)
                        Require(routine.Fatigue >= fatigue, "A real intermediate stop restored fatigue before reaching home.");
                    detour |= routine.ReturningHome && routine.HasTarget && routine.Target != routine.Home;
                    arrived = routine.Activity == SolitaryActivity.Rest && routine.IsWorking && Vector3.Distance(position, routine.Home) <= .18f;
                }
                Require(detour && arrived, "The real controller remained on its blocked straight line instead of walking around the pond to home.");
            }
        }

        private sealed class Fixture : IDisposable
        {
            public readonly WildlifePopulation population;
            public readonly WildlifeAgent fox;
            private readonly Scene scene;
            private readonly GameObject root;
            public Fixture(float companionX)
            {
                scene = EditorSceneManager.NewPreviewScene();
                root = new GameObject("Isolated solitary wildlife checks") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, scene);
                population = root.AddComponent<WildlifePopulation>(); population.worldSeed = 42; population.obstacleMask = 0;
                population.worldBounds = new Bounds(new Vector3(0, 10, 0), new Vector3(120, 20, 120));
                fox = Agent("Subject", 20, Vector3.zero, 5);
                WildlifeAgent companion = Agent("Distant same-group fox", 21, new Vector3(companionX, 0, 0), 1);
                population.agents = new[] { fox, companion }; population.Initialize();
            }
            private WildlifeAgent Agent(string name, int id, Vector3 home, float range)
            {
                var body = new GameObject(name); body.transform.SetParent(root.transform, false); body.transform.position = home;
                var agent = body.AddComponent<WildlifeAgent>(); agent.speciesId = "fox"; agent.spawnId = id; agent.groupId = 5;
                Require(agent.routineMode == WildlifeRoutineMode.Ambient, "A newly loaded ambient fox opted into solitary behaviour without an instance setting.");
                agent.routineMode = WildlifeRoutineMode.Solitary;
                agent.habitatCenter = home; agent.habitatRadius = range; agent.scaleRange = Vector2.one;
                agent.walkSpeed = 1; agent.bodyRadius = .2f; agent.sociability = agent.timidity = 0;
                return agent;
            }
            public void Dispose() { Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
