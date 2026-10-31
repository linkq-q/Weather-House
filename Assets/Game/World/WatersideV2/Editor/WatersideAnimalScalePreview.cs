using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WatersideAnimalScalePreview
    {
        [MenuItem("Weather House/Animal AI/Sync Squirrel and Turtle Edit Sizes")]
        public static void RunMenu() { Debug.Log("Updated animal edit sizes: " + ApplyToActiveScene()); }

        public static int ApplyToActiveScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Animal size preview requires Edit mode.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/WeatherHouse_WatersideV2.unity") throw new InvalidOperationException("Open WatersideV2 first.");
            int changed=0;
            foreach (WildlifeAgent animal in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<WildlifeAgent>(true)))
                if ((animal.speciesId=="squirrel" || animal.speciesId=="turtle") && Apply(animal)) changed++;
            if(changed>0)EditorSceneManager.MarkSceneDirty(scene);
            return changed;
        }

        public static bool Apply(WildlifeAgent animal)
        {
            if(!animal || !animal.visualRoot || Application.isPlaying) return false;
            WildlifePopulation population=animal.GetComponentInParent<WildlifePopulation>();
            if(!population) return false;
            Undo.RecordObjects(new UnityEngine.Object[]{animal,animal.visualRoot},"同步动物编辑与运行尺寸");
            if(!animal.BakeVisualScale(population.worldSeed))return false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(animal);
            PrefabUtility.RecordPrefabInstancePropertyModifications(animal.visualRoot);
            EditorUtility.SetDirty(animal);EditorUtility.SetDirty(animal.visualRoot);
            return true;
        }
    }

    [CustomEditor(typeof(WildlifeAgent)),CanEditMultipleObjects]
    sealed class WildlifeAgentScaleInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if(!DrawDefaultInspector() || Application.isPlaying)return;
            foreach(WildlifeAgent animal in targets)
                if(animal.speciesId=="squirrel" || animal.speciesId=="turtle")WatersideAnimalScalePreview.Apply(animal);
        }
    }
}
