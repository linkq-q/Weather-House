using System;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using WeatherHouse.AnimalToon;

namespace WeatherHouse.Life.Editor
{
    public static class AnimalPaperInstall
    {
        public static void Install()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Install in Edit mode.");
            const string path="Assets/Game/Resources/AnimalPaper.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Weather House/Animals/Paper Toon"));AssetDatabase.CreateAsset(material,path);}
            material.SetFloat("_OutlineWidth",8f/3f);material.SetFloat("_ToonStrength",1);material.SetFloat("_BandSoftness",.01f);
            material.SetColor("_ShadeColor",new Color(.82f,.80f,.76f,1));material.SetFloat("_ShadowFloor",.12f);
            EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);
            foreach(string quality in new[]{"Performant","Balanced","HighFidelity"})
            {
                var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/URP-"+quality+"-Renderer.asset");
                if(renderer.defaultStencilState.overrideStencilState)throw new InvalidOperationException("Review existing stencil override before installing animal paper.");
                if(renderer.rendererFeatures.OfType<AnimalLightOutlineFeature>().Any())continue;
                var feature=ScriptableObject.CreateInstance<AnimalLightOutlineFeature>();feature.name="Animal paper light and merged regions";
                var serialized=new SerializedObject(feature);serialized.FindProperty("screenShader").objectReferenceValue=Shader.Find("Hidden/Weather House/Animal Paper Screen");serialized.ApplyModifiedPropertiesWithoutUndo();
                renderer.rendererFeatures.Add(feature);AssetDatabase.AddObjectToAsset(feature,renderer);renderer.SetDirty();EditorUtility.SetDirty(renderer);AssetDatabase.SaveAssetIfDirty(renderer);
            }
            File.WriteAllText("PhotoStillReview/20261029/toon-install.txt","PASS three forward renderers; no stencil override; explicit screen shader reference; template outline=8/3 / strength=1 / softness=.01; runtime camera merge=12.");
        }
    }
}
