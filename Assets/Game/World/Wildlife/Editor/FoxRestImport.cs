using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WeatherHouse.Hillside.Editor
{
    public static class FoxRestImport
    {
        public const string Folder = "Assets/Game/World/Wildlife/Animation/Fox/";

        public static AnimationClip Load(string id, bool loop)
        {
            string path = Folder + "fox_" + id + ".fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!importer) throw new InvalidOperationException("Missing fox animation: " + path);
            var takes = importer.defaultClipAnimations;
            if (takes.Length != 1) throw new InvalidOperationException("Expected one preserved-skeleton take: " + path);
            if (importer.animationType != ModelImporterAnimationType.Generic || importer.clipAnimations.Length != 1 ||
                importer.clipAnimations[0].name != id || importer.clipAnimations[0].loopTime != loop)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.importCameras = importer.importLights = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                takes[0].name = id; takes[0].loopTime = loop; takes[0].loopPose = false;
                takes[0].keepOriginalPositionY = true;
                importer.clipAnimations = takes;
                importer.SaveAndReimport();
            }
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).ToArray();
            if (clips.Length != 1) throw new InvalidOperationException("Expected one imported fox clip: " + path);
            return clips[0];
        }

        public static void Bind(WildlifeAgent agent)
        {
            if (!agent || agent.speciesId != "fox") throw new ArgumentException("Only the current fox rig was validated.");
            agent.clips.forage = Load("eat", true);
            agent.clips.restDown = Load("restdown", false);
            agent.clips.restSleep = Load("restsleep", true);
            agent.clips.restUp = Load("restup", false);
        }

        [MenuItem("Weather House/Wildlife/Apply Reviewed Fox Rest Clips")]
        public static void ApplyToExistingFoxes()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Apply outside Play mode.");
            const string path = "Assets/Game/World/Hillside/Generated/fox.prefab";
            var prefab = PrefabUtility.LoadPrefabContents(path);
            try { Bind(prefab.GetComponent<WildlifeAgent>()); PrefabUtility.SaveAsPrefabAsset(prefab, path); }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            foreach (var agent in UnityEngine.Object.FindObjectsByType<WildlifeAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (agent.speciesId != "fox" || !agent.gameObject.scene.IsValid() || !agent.gameObject.scene.isLoaded) continue;
                Undo.RecordObject(agent, "Bind fox rest and eating clips"); Bind(agent);
                PrefabUtility.RecordPrefabInstancePropertyModifications(agent);
                EditorSceneManager.MarkSceneDirty(agent.gameObject.scene);
            }
        }
    }
}
