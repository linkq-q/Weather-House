using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.WildlifeJourney.Editor
{
    internal static class JourneySwimImport
    {
        public const string Path = "Assets/Game/World/Wildlife/Journey/Animation/sheep_swim.fbx";

        public static AnimationClip Load()
        {
            var importer = AssetImporter.GetAtPath(Path) as ModelImporter;
            if (!importer) throw new InvalidOperationException("Missing sheep swimming asset: " + Path);
            if (importer.animationType != ModelImporterAnimationType.Generic || importer.clipAnimations.Length != 1 ||
                !importer.clipAnimations[0].loopTime || importer.clipAnimations[0].name != "Swim")
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.importCameras = importer.importLights = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                var clips = importer.defaultClipAnimations;
                if (clips.Length != 1) throw new InvalidOperationException("Expected exactly one complete swim take.");
                clips[0].name = "Swim"; clips[0].loopTime = true; clips[0].loopPose = false;
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(Path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__"));
            if (!clip || Mathf.Abs(clip.length - 1.2f) > .04f) throw new InvalidOperationException("The imported swim cycle must retain the complete 1.2-second take.");
            return clip;
        }
    }
}
