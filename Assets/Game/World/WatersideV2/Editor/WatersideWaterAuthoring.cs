using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WatersideWaterAuthoring
    {
        const string Path = "Assets/Game/World/WatersideV2/Generated/Nature/Water.mat";
        static readonly Dictionary<string,float> Requested = new Dictionary<string,float> {
            {"_ShoreWidth",.23f},{"_ShoreStrength",1},{"_FoamSpeed",.7f},{"_FoamTravel",.183f},{"_FoamSoftness",.101f},
            {"_WaveletStrength",.2f},{"_WaveletScale",.58f},{"_WaveletWidth",.075f},{"_WaveletLength",1.25f},{"_WaveletSpeed",.15f},
            {"_CausticStrength",.23f},{"_CausticScale",1.25f},{"_CausticSpeed",.28f},{"_CausticDepth",1.1f},
            {"_ReflectionNear",.845f},{"_ReflectionFar",.345f},{"_ReflectionFalloff",3.05f},{"_ReflectionTintStrength",0},
            {"_WaterSaturation",1.35f},{"_ReflectionStrength",.9f},{"_ReflectionDistortion",.0045f},
            {"_ReflectionBrushStrength",.78f},{"_ReflectionBrushSize",1.35f},{"_ReflectionColorSteps",8},
            {"_ReflectionPosterize",.48f},{"_UseWaterBrushAtlas",1}
        };

        public static Material GetOrCreateMaterial(string path,string source)
        {
            var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(existing)return existing;
            var material=new Material(AssetDatabase.LoadAssetAtPath<Material>(source));
            foreach(var pair in Requested)material.SetFloat(pair.Key,pair.Value);
            AssetDatabase.CreateAsset(material,path);return material;
        }

        public static void ConfigureNewInteraction(HillsideWaterInteraction value)
        {
            value.strength=1.45f;value.radius=.52f;value.decay=1.05f;value.waveSpeed=1.15f;
            // The ocean field spans the whole terrain; half-metre cells retain the small character's wake.
            value.resolution=value.surface is WatersideWaterSurface s && s.section==-1 ? 512 : 256;
        }

        public static void ApplyRequested()
        {
            WatersideSceneBuilder.RequireSaved();
            if(SceneManager.GetActiveScene().path!=WatersideSceneBuilder.ScenePath)throw new InvalidOperationException("Open V2.");
            var material=AssetDatabase.LoadAssetAtPath<Material>(Path);
            foreach(var pair in Requested)material.SetFloat(pair.Key,pair.Value);
            material.SetTextureScale("_WaterBrushAtlas",Vector2.one);material.SetTextureOffset("_WaterBrushAtlas",Vector2.zero);
            EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);
            var lines=new List<string>{"UTC="+DateTime.UtcNow.ToString("O"),"Screenshot floats applied; existing colors and texture references preserved."};
            var waters=Object.FindObjectsOfType<HillsideWaterInteraction>().Where(w=>w.gameObject.scene==SceneManager.GetActiveScene()).ToArray();
            foreach(var water in waters)
            {
                lines.Add(water.name+" before: strength="+water.strength+" radius="+water.radius+" decay="+water.decay+" speed="+water.waveSpeed+" resolution="+water.resolution);
                ConfigureNewInteraction(water);EditorUtility.SetDirty(water);
            }
            // Existing photo property blocks must pick up the user's Inspector colors as well.
            var mood=Object.FindObjectOfType<WatersideMood>();
            if(mood.Current!=WatersideMood.Mode.Day)throw new InvalidOperationException("Apply the authored daylight palette in Day mode.");
            var block=new MaterialPropertyBlock();
            foreach(var water in waters)
            {
                water.waterRenderer.GetPropertyBlock(block);
                foreach(string color in new[]{"_ShallowColor","_DeepColor","_FoamColor","_RippleColor"})block.SetColor(color,material.GetColor(color));
                water.waterRenderer.SetPropertyBlock(block);
            }
            typeof(WatersideMood).GetField("daylight",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(mood,null);mood.CaptureState();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Directory.CreateDirectory("WatersideV3Review/water-authoring");File.WriteAllLines("WatersideV3Review/water-authoring/applied.txt",lines);
            Check();
        }

        public static void Check()
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(Path);string before=EditorJsonUtility.ToJson(material);
            if(GetOrCreateMaterial(Path,"")!=material||before!=EditorJsonUtility.ToJson(material))throw new InvalidOperationException("Rebuild changed authored material.");
            foreach(var pair in Requested)if(Mathf.Abs(material.GetFloat(pair.Key)-pair.Value)>.00001f)throw new InvalidOperationException("Saved screenshot value mismatch: "+pair.Key);
            if(material.GetTextureScale("_WaterBrushAtlas")!=Vector2.one||material.GetTextureOffset("_WaterBrushAtlas")!=Vector2.zero)throw new InvalidOperationException("Atlas UV mismatch.");
            var waters=Object.FindObjectsOfType<HillsideWaterInteraction>();
            foreach(var w in waters)if(w.strength!=1.45f||w.radius!=.52f||w.decay!=1.05f||w.waveSpeed!=1.15f)throw new InvalidOperationException("Wake settings mismatch: "+w.name);
            File.WriteAllText("WatersideV3Review/water-authoring/checks.txt","PASS\nScreenshotFloats="+Requested.Count+"\nWaterComponents="+waters.Length+"\nRebuildPreservesMaterial=True\nColors="+material.GetColor("_ShallowColor")+" / "+material.GetColor("_DeepColor")+"\nWake=1.45 strength, .52m radius, 1.05 decay, 1.15 speed.\nRuntime wake visibility requires separate Play verification.\n");
            WatersideWaterContactReview.Check();
        }
    }
}
