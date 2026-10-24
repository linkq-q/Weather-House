using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WeatherHouse.Hillside.Editor
{
    public static class WildlifeSolitaryIntegration
    {
        [MenuItem("Weather House/Wildlife/Enable Waterside Fox Routines")]
        public static void ApplyCurrentScene()
        { Debug.Log(ApplyCurrentFoxes(UnityEngine.Object.FindObjectOfType<WildlifePopulation>())); }

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
