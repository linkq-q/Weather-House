using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouse.Hillside.Editor
{
    public static class WildlifeSolitaryIntegration
    {
        [MenuItem("Weather House/Wildlife/Enable Waterside Fox Routines")]
        public static void ApplyCurrentScene()
        { Debug.Log(ApplyCurrentFoxes(FindFoxPopulation(SceneManager.GetActiveScene()))); }

        public static WildlifePopulation FindFoxPopulation(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Expected a loaded scene containing fox 20 and 21.");
            WildlifePopulation found = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (WildlifePopulation population in root.GetComponentsInChildren<WildlifePopulation>(true))
                {
                    bool first = false, second = false;
                    if (population.agents == null) continue;
                    foreach (WildlifeAgent agent in population.agents)
                    {
                        if (!agent || agent.gameObject.scene != scene || agent.speciesId != "fox") continue;
                        first |= agent.spawnId == 20; second |= agent.spawnId == 21;
                    }
                    if (!first || !second) continue;
                    if (found) throw new InvalidOperationException("More than one population contains fox 20 and 21 in this scene.");
                    found = population;
                }
            if (!found) throw new InvalidOperationException("No population contains both fox 20 and 21 in this scene.");
            return found;
        }

        public static string ApplyCurrentFoxes(WildlifePopulation population)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Enable the scene instances outside Play mode.");
            if (!population || !population.gameObject.scene.IsValid() ||
                population.gameObject.scene.name != "WeatherHouse_WatersideV2")
                throw new InvalidOperationException("Expected the existing Waterside V2 population.");
            WildlifeAgent first = null, second = null;
            foreach (WildlifeAgent agent in population.agents)
            {
                if (!agent || (agent.spawnId != 20 && agent.spawnId != 21)) continue;
                if (agent.speciesId != "fox" || agent.locomotion != WildlifeLocomotion.Ground || agent.gameObject.scene != population.gameObject.scene ||
                    (agent.spawnId == 20 ? first != null : second != null))
                    throw new InvalidOperationException("The expected two ground fox instances could not be identified uniquely.");
                if (agent.spawnId == 20) first = agent; else second = agent;
            }
            if (!first || !second) throw new InvalidOperationException("Expected fox spawn 20 and 21 in this population.");
            Undo.RecordObjects(new UnityEngine.Object[] { first, second }, "启用当前地图狐狸独居日常");
            first.routineMode = second.routineMode = WildlifeRoutineMode.Solitary;
            if (PrefabUtility.IsPartOfPrefabInstance(first)) PrefabUtility.RecordPrefabInstancePropertyModifications(first);
            if (PrefabUtility.IsPartOfPrefabInstance(second)) PrefabUtility.RecordPrefabInstancePropertyModifications(second);
            EditorUtility.SetDirty(first); EditorUtility.SetDirty(second);
            EditorSceneManager.MarkSceneDirty(population.gameObject.scene);
            return "Enabled solitary routines on fox 20 and 21; scene save remains with its owner.";
        }
    }
}
