using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouse.Hillside.Editor
{
    public static class WildlifeSolitaryIntegrationChecks
    {
        [MenuItem("Weather House/Wildlife/Check Solitary Scene Integration")]
        public static void RunMenu() { Debug.Log(RunLogicChecks()); }

        public static string RunLogicChecks()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run isolated scene integration checks outside Play mode.");
            using (var selected = new Fixture()) using (var unrelatedScene = new Fixture())
            {
                selected.Population("Butterflies", false);
                WildlifePopulation foxes = selected.Population("Existing fox population", true);
                unrelatedScene.Population("Foxes in another scene", true);
                Require(WildlifeSolitaryIntegration.FindFoxPopulation(selected.scene) == foxes,
                    "The scene lookup selected an unrelated population or a population in another scene.");
                foreach (WildlifeAgent fox in foxes.agents)
                    Require(fox.routineMode == WildlifeRoutineMode.Ambient, "Population discovery changed an animal's routine mode.");
                selected.Population("Ambiguous second fox population", true);
                RequireThrows(() => WildlifeSolitaryIntegration.FindFoxPopulation(selected.scene),
                    "Two matching populations silently selected one instead of rejecting ambiguity.");
            }
            using (var missing = new Fixture())
            {
                WildlifePopulation partial = missing.Population("Only fox 20", true);
                partial.agents = new[] { partial.agents[0] };
                RequireThrows(() => WildlifeSolitaryIntegration.FindFoxPopulation(missing.scene),
                    "A partial pair was accepted as the current two-fox population.");
            }
            return "PASS actual scene lookup finds the unique fox 20/21 population among unrelated populations without mutating fields\n" +
                "PASS another scene cannot supply the pair; duplicate matching populations and missing foxes are rejected";
        }

        private sealed class Fixture : IDisposable
        {
            public readonly Scene scene = EditorSceneManager.NewPreviewScene();
            public WildlifePopulation Population(string name, bool pair)
            {
                var root = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, scene);
                var population = root.AddComponent<WildlifePopulation>();
                var agents = new WildlifeAgent[2];
                for (int i = 0; i < agents.Length; i++)
                {
                    var body = new GameObject(pair ? "Fox " + (20 + i) : "Unrelated animal " + i);
                    body.transform.SetParent(root.transform, false);
                    var animal = body.AddComponent<WildlifeAgent>();
                    animal.speciesId = pair ? "fox" : "butterfly"; animal.spawnId = 20 + i;
                    agents[i] = animal;
                }
                population.agents = agents;
                return population;
            }
            public void Dispose() { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void RequireThrows(Action action, string message)
        {
            try { action(); } catch (InvalidOperationException) { return; }
            throw new InvalidOperationException(message);
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
