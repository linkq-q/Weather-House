using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Hillside.Editor
{
    public static class WildlifeAlarmChecks
    {
        [MenuItem("Weather House/Wildlife/Check alarm source isolation")]
        public static void RunMenu() { Debug.Log(RunLogicChecks()); }

        public static string RunLogicChecks()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run isolated alarm checks outside Play mode.");
            CheckSameSpeciesWalking(); CheckMixedSource(); CheckOriginalSource(); CheckUnknownSpecies(); CheckExplicitAlarm(); CheckPlayer();
            return "PASS actual Population.Simulate: neighbouring foxes walk without alarming each other\n" +
                "PASS actual mixed population: prey detects a walking fox; original fox and its species remain calm\n" +
                "PASS alarm keeps original source identity and species after source mutation\n" +
                "PASS unknown species do not acquire same-species immunity\n" +
                "PASS source-less explicit alarms retain radius, duplicate and expiry rules\n" +
                "PASS actual player proximity still alarms foxes";
        }

        private static void CheckSameSpeciesWalking()
        {
            using (var f = new Fixture())
            {
                WildlifeAgent a = f.Agent(20, "fox", Vector3.zero, true);
                WildlifeAgent b = f.Agent(21, "fox", new Vector3(1.7f, 0, 0), true);
                f.Initialize(a, b);
                bool nearbyWalking = false;
                for (int i = 0; i < 160; i++)
                {
                    f.population.Simulate(.05f);
                    nearbyWalking |= Vector3.Distance(a.transform.position, b.transform.position) < 2.8f &&
                        (a.ActualSpeed > .45f || b.ActualSpeed > .45f);
                    Require(f.population.AlarmCount == 0 && a.AcceptedAlarmCount == 0 && b.AcceptedAlarmCount == 0,
                        "Same-species ordinary walking produced an ambient alarm or frightened a fox.");
                }
                Require(nearbyWalking, "The same-species fixture never exercised nearby real walking.");
            }
        }

        private static void CheckMixedSource()
        {
            using (var f = new Fixture())
            {
                WildlifeAgent fox = f.Agent(20, "fox", Vector3.zero, true);
                WildlifeAgent otherFox = f.Agent(21, "fox", new Vector3(-1.7f, 0, 0), true);
                WildlifeAgent rabbit = f.Agent(30, "rabbit", new Vector3(1.7f, 0, 0), false);
                WildlifeAgent squirrel = f.Agent(31, "squirrel", new Vector3(0, 0, 1.7f), false);
                f.Initialize(fox, otherFox, rabbit, squirrel);
                bool sawWalking = false;
                for (int i = 0; i < 80 && f.population.AlarmCount == 0; i++)
                {
                    f.population.Simulate(.05f);
                    sawWalking |= fox.ActualSpeed > .45f || otherFox.ActualSpeed > .45f;
                }
                Require(sawWalking && f.population.AlarmCount == 1, "Nearby prey did not detect a genuinely walking threatening species.");
                Require(fox.AcceptedAlarmCount == 0 && otherFox.AcceptedAlarmCount == 0,
                    "A mixed-population alarm frightened its original source or another fox.");
                Require(rabbit.AcceptedAlarmCount == 1 && squirrel.AcceptedAlarmCount == 1,
                    "The source filter accidentally suppressed nearby different-species reactions.");
                Require(rabbit.State == WildlifeState.Alert && squirrel.State == WildlifeState.Alert,
                    "Mixed-species alarm receipt did not enter the real Alert state.");
            }
        }

        private static void CheckOriginalSource()
        {
            using (var f = new Fixture())
            {
                WildlifeAgent source = f.Agent(20, "fox", Vector3.zero, true);
                WildlifeAgent lateFox = f.Agent(21, "fox", new Vector3(15, 0, 0), false);
                WildlifeAgent lateRabbit = f.Agent(30, "rabbit", new Vector3(16, 0, 0), false);
                f.Initialize(source, lateFox, lateRabbit);
                WildlifeAlarm alarm = EmitWithSource(f.population, source, 5f);
                source.speciesId = "rabbit";
                lateFox.transform.position = Vector3.right;
                lateRabbit.transform.position = Vector3.left;
                Require(!source.ReceiveStartle(alarm, 0), "Changing species let the original threat frighten itself.");
                Require(!lateFox.ReceiveStartle(alarm, 0), "The alarm forgot its source species snapshot.");
                Require(lateRabbit.ReceiveStartle(alarm, 0), "Changing the source species changed an already emitted alarm's recipients.");
            }
        }

        private static void CheckUnknownSpecies()
        {
            using (var f = new Fixture())
            {
                WildlifeAgent source = f.Agent(20, "", Vector3.zero, true);
                WildlifeAgent unknown = f.Agent(21, "", Vector3.right, false);
                WildlifeAgent missing = f.Agent(22, null, Vector3.left, false);
                f.Initialize(source, unknown, missing);
                EmitWithSource(f.population, source, 5f);
                Require(source.AcceptedAlarmCount == 0, "An unnamed source frightened itself.");
                Require(unknown.AcceptedAlarmCount == 1 && missing.AcceptedAlarmCount == 1,
                    "Missing species identifiers were treated as a known same species.");
            }
        }

        private static void CheckExplicitAlarm()
        {
            using (var f = new Fixture())
            {
                WildlifeAgent a = f.Agent(20, "fox", Vector3.zero, true);
                WildlifeAgent b = f.Agent(21, "fox", Vector3.right, true);
                WildlifeAgent remote = f.Agent(30, "rabbit", new Vector3(15, 0, 0), false);
                f.Initialize(a, b, remote);
                WildlifeAlarm alarm = f.population.EmitStartle(Vector3.zero, 4, 1);
                Require(a.AcceptedAlarmCount == 1 && b.AcceptedAlarmCount == 1 && remote.AcceptedAlarmCount == 0,
                    "A source-less explicit alarm changed its original radius or same-species reaction.");
                Require(!a.ReceiveStartle(alarm, 0), "The same alarm was accepted twice.");
                Require(!remote.ReceiveStartle(new WildlifeAlarm { id = 99, origin = remote.transform.position,
                    radius = 5, intensity = 1, expiresAt = -1 }, 0), "An expired alarm was accepted.");
            }
        }

        private static void CheckPlayer()
        {
            using (var f = new Fixture())
            {
                WildlifeAgent fox = f.Agent(20, "fox", Vector3.zero, true);
                WildlifeAgent peer = f.Agent(21, "fox", Vector3.right, true);
                var player = new GameObject("Nearby player"); player.transform.SetParent(f.root.transform, false);
                player.transform.position = new Vector3(.4f, 0, .4f); f.population.player = player.transform;
                f.Initialize(fox, peer); f.population.Simulate(.05f);
                Require(f.population.AlarmCount == 1 && fox.AcceptedAlarmCount == 1 && peer.AcceptedAlarmCount == 1,
                    "A real nearby player lost the ability to alarm foxes.");
            }
        }

        // Reflection only keeps this regression compilable against the unmodified three-argument baseline.
        private static WildlifeAlarm EmitWithSource(WildlifePopulation population, WildlifeAgent source, float radius)
        {
            MethodInfo method = typeof(WildlifePopulation).GetMethod("EmitStartle",
                new[] { typeof(Vector3), typeof(float), typeof(float), typeof(WildlifeAgent) });
            return method == null ? population.EmitStartle(source.transform.position, radius, 1) :
                (WildlifeAlarm)method.Invoke(population, new object[] { source.transform.position, radius, 1f, source });
        }

        private sealed class Fixture : IDisposable
        {
            public readonly GameObject root;
            public readonly WildlifePopulation population;
            private readonly Scene scene;
            public Fixture()
            {
                scene = EditorSceneManager.NewPreviewScene();
                root = new GameObject("Isolated alarm checks") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, scene);
                population = root.AddComponent<WildlifePopulation>(); population.worldSeed = 42;
                population.obstacleMask = 0; population.decisionInterval = .1f;
                population.worldBounds = new Bounds(new Vector3(0, 10, 0), new Vector3(100, 20, 100));
            }
            public WildlifeAgent Agent(int id, string species, Vector3 position, bool threatening)
            {
                var body = new GameObject("Alarm subject " + id); body.transform.SetParent(root.transform, false); body.transform.position = position;
                var agent = body.AddComponent<WildlifeAgent>(); agent.spawnId = id; agent.groupId = id; agent.speciesId = species;
                agent.scaleRange = Vector2.one; agent.habitatCenter = position; agent.habitatRadius = 6;
                agent.walkSpeed = .65f; agent.runSpeed = 1.7f; agent.bodyRadius = .2f;
                agent.sociability = 0; agent.timidity = .7f; agent.startlesOthers = threatening;
                agent.routineMode = WildlifeRoutineMode.Solitary;
                return agent;
            }
            public void Initialize(params WildlifeAgent[] agents)
            {
                population.agents = agents; population.Initialize();
                foreach (WildlifeAgent agent in agents)
                { agent.SolitaryRoutine.Hunger = .9f; agent.SolitaryRoutine.Thirst = agent.SolitaryRoutine.Fatigue = .05f; }
            }
            public void Dispose() { Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
