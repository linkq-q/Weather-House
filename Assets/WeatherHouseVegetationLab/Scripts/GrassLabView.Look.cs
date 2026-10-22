using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WeatherHouse.Vegetation
{
    public partial class GrassLabView
    {
        readonly Dictionary<Material, Material> lookCopies = new Dictionary<Material, Material>();
        readonly HashSet<string> lookColors = new HashSet<string>();
        GrassMeadow lookMeadow;
        Material[] lookMaterials;
        Material lookOriginalSky;
        int lookPage, lookGrass;
        string lookMessage;

        public void ShowWaterGrassLook() { showPanel = true; page = 4; }
        public Material[] WaterGrassMaterials
        {
            get
            {
                if (lookMeadow == meadow && lookMaterials != null) return lookMaterials;
                lookMeadow = meadow;
                var materials = new List<Material>(meadow.materials.Where(m => m));
                var renderers = gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToArray();
                foreach (string shader in new[] { "Weather House/Painterly Water", "Weather House/Painterly Ground" })
                {
                    var nearest = renderers.Where(r => r.sharedMaterial && r.sharedMaterial.shader.name == shader)
                        .OrderBy(r => (r.bounds.center - meadow.transform.position).sqrMagnitude).FirstOrDefault();
                    if (!nearest) continue;
                    var source = nearest.sharedMaterial;
                    if (lookCopies.ContainsValue(source)) { materials.Add(source); continue; }
                    var copy = new Material(source) { name = source.name + " · 运行时画风", hideFlags = HideFlags.DontSave };
                    lookCopies.Add(source, copy); materials.Add(copy);
                    foreach (var renderer in renderers)
                    {
                        var slots = renderer.sharedMaterials;
                        if (!slots.Contains(source)) continue;
                        for (int i = 0; i < slots.Length; i++) if (slots[i] == source) slots[i] = copy;
                        renderer.sharedMaterials = slots;
                    }
                }
                if (RenderSettings.skybox)
                {
                    if (!lookOriginalSky)
                    {
                        lookOriginalSky = RenderSettings.skybox;
                        var sky = new Material(lookOriginalSky) { name = lookOriginalSky.name + " · 运行时画风", hideFlags = HideFlags.DontSave };
                        lookCopies.Add(lookOriginalSky, sky); RenderSettings.skybox = sky;
                    }
                    materials.Add(RenderSettings.skybox);
                }
                return lookMaterials = materials.Distinct().ToArray();
            }
        }
        Material LookSource(Material material)
        {
            foreach (var pair in lookCopies) if (pair.Value == material) return pair.Key;
            return meadow.SourceMaterial(material);
        }
        // Grass root/tip and wind are owned by the existing species/shape panel.
        public static void CopyLook(Material from, Material to)
        {
            if (to.shader.name != "Weather House/Painterly Grass") { to.CopyPropertiesFromMaterial(from); return; }
            Color root = to.GetColor("_BaseColor"), tip = to.GetColor("_TipColor");
            float wind = to.GetFloat("_WindStrength"), speed = to.GetFloat("_WindSpeed"), normals = to.GetFloat("_UpNormals");
            to.CopyPropertiesFromMaterial(from);
            to.SetColor("_BaseColor", root); to.SetColor("_TipColor", tip);
            to.SetFloat("_WindStrength", wind); to.SetFloat("_WindSpeed", speed); to.SetFloat("_UpNormals", normals);
        }
        public void SaveWaterGrassDefaults()
        {
#if UNITY_EDITOR
            var materials = WaterGrassMaterials;
            var sources = materials.Select(LookSource).ToArray();
            if (sources.Any(m => !m || !UnityEditor.EditorUtility.IsPersistent(m)))
                throw new InvalidOperationException("存在编辑器试调副本，请先结束编辑器窗口试调，再使用 Game 面板保存。");
            for (int i = 0; i < materials.Length; i++)
            {
                UnityEditor.Undo.RecordObject(sources[i], "保存运行时水草画风");
                CopyLook(materials[i], sources[i]);
                UnityEditor.EditorUtility.SetDirty(sources[i]);
                UnityEditor.AssetDatabase.SaveAssetIfDirty(sources[i]);
            }
            lookMessage = "已保存到材质。退出 Play 或重新打开场景后仍保留；支持 Undo。";
#else
            lookMessage = "默认值保存需要 Unity 编辑器；当前运行仍可实时试调。";
#endif
        }
        void DrawWaterGrassLook()
        {
            var all = WaterGrassMaterials;
            lookPage = GUILayout.Toolbar(lookPage, new[] { "水体", "草地", "地面", "天空" }, button, GUILayout.Height(30));
            string shader = lookPage == 0 ? "Weather House/Painterly Water" : lookPage == 1 ? "Weather House/Painterly Grass" : "Weather House/Painterly Ground";
            var choices = all.Where(m => lookPage == 3 ? m == RenderSettings.skybox : m.shader.name == shader).ToArray();
            if (choices.Length == 0) { GUILayout.Label("当前区域没有这类材质。", label); return; }
            int index = 0;
            if (lookPage == 1)
            {
                lookGrass = Mathf.Clamp(lookGrass, 0, choices.Length - 1);
                lookGrass = GUILayout.SelectionGrid(lookGrass, choices.Select(m => m.name.Replace("(Clone)", "")).ToArray(), 2, button);
                index = lookGrass;
            }
            var m = choices[index];
#if UNITY_EDITOR
            if (GUILayout.Button("保存当前区域画风为默认值", button))
            {
                try { SaveWaterGrassDefaults(); } catch (Exception error) { lookMessage = error.Message; }
            }
#endif
            if (GUILayout.Button("恢复已保存的画风", button))
                foreach (var material in all) CopyLook(LookSource(material), material);
            if (!string.IsNullOrEmpty(lookMessage)) GUILayout.Label(lookMessage, label);
            if (lookPage == 0)
            {
                LookColor(m, "浅水颜色", "_ShallowColor"); LookColor(m, "深水颜色", "_DeepColor");
                LookFloat(m, "颜料饱和度", "_WaterSaturation", 0, 2); LookFloat(m, "不透明度", "_Opacity", 0, 1);
                LookFloat(m, "近处 / 俯视倒影", "_ReflectionNear", 0, 1); LookFloat(m, "远处 / 掠视倒影", "_ReflectionFar", 0, 1);
                LookFloat(m, "视角过渡", "_ReflectionFalloff", .2f, 5); LookFloat(m, "整体倒影强度", "_ReflectionStrength", 0, 1);
                LookColor(m, "倒影染色", "_ReflectionTint"); LookFloat(m, "倒影染色量", "_ReflectionTintStrength", 0, 1);
                LookFloat(m, "倒影扰动", "_ReflectionDistortion", 0, .06f);
                LookFloat(m, "表面笔触", "_SurfaceStrokeStrength", 0, 1); LookFloat(m, "小笔触权重", "_SmallStrokeWeight", 0, 1);
                LookColor(m, "水波颜色", "_RippleColor"); LookFloat(m, "短 S 水波强度", "_WaveletStrength", 0, 1);
                LookFloat(m, "短水波密度", "_WaveletScale", .1f, 3); LookFloat(m, "短水波速度", "_WaveletSpeed", 0, 2);
                LookFloat(m, "短水波宽度", "_WaveletWidth", .01f, .3f); LookFloat(m, "短水波长度", "_WaveletLength", .3f, 1.5f);
                LookColor(m, "泡沫颜色", "_FoamColor"); LookFloat(m, "岸边泡沫强度", "_ShoreStrength", 0, 1);
                LookFloat(m, "岸边泡沫宽度", "_ShoreWidth", .01f, 1); LookFloat(m, "泡沫流速", "_FoamSpeed", 0, 2);
                LookFloat(m, "泡沫进退幅度", "_FoamTravel", 0, 1); LookFloat(m, "泡沫软边", "_FoamSoftness", .01f, .5f);
            }
            else if (lookPage == 1)
            {
                LookFloat(m, "连片色块混合", "_MeadowBlend", 0, 1); LookFloat(m, "逐叶明暗对比", "_BladeContrast", 0, 1);
                LookColor(m, "共用草地底色", "_MeadowBaseColor"); LookColor(m, "共用暖色斑块", "_MeadowPatchColor");
                LookFloat(m, "大色块频率", "_MeadowPatchScale", .02f, 2); LookColor(m, "阴影色", "_ShadowTint");
            }
            else if (lookPage == 2)
            {
                LookColor(m, "地面底色", "_BaseColor"); LookColor(m, "暖色斑块", "_PatchColor"); LookColor(m, "岸边土色", "_ShoreColor");
                LookFloat(m, "色块频率", "_PatchScale", .02f, 2);
            }
            else
            {
                LookColor(m, "天空色调", "_SkyTint"); LookColor(m, "地平线下方颜色", "_GroundColor");
                LookFloat(m, "天空曝光", "_Exposure", 0, 3); LookFloat(m, "大气厚度", "_AtmosphereThickness", 0, 5);
                LookFloat(m, "太阳大小", "_SunSize", 0, 1);
            }
            if (lookPage == 1 || lookPage == 2)
            {
                GUILayout.Label("明暗交界（当前区域草与地面同步）", label);
                LookFloat(m, "边界硬度", "_BoundaryHardness", 0, 1, true);
                LookFloat(m, "边缘起伏强度", "_BoundaryBreakup", 0, .25f, true);
                LookFloat(m, "起伏分组尺度（米）", "_BoundarySize", .15f, 3, true);
            }
            GUILayout.Label("拖动立即生效；点颜色展开 RGB。未保存的试调在退出 Play 后恢复。", label);
        }
        void LookFloat(Material material, string caption, string property, float min, float max, bool synchronize = false)
        {
            if (!material.HasProperty(property)) return;
            float value = material.GetFloat(property), next = Slider(caption, value, min, max, "F4");
            if (next == value) return;
            material.SetFloat(property, next);
            if (synchronize) foreach (var m in WaterGrassMaterials) if (m.HasProperty(property)) m.SetFloat(property, next);
        }
        void LookColor(Material material, string caption, string property)
        {
            if (!material.HasProperty(property)) return;
            string key = material.GetInstanceID() + property;
            Color color = material.GetColor(property), old = GUI.backgroundColor;
            GUI.backgroundColor = color;
            bool toggle = GUILayout.Button(caption + "  #" + ColorUtility.ToHtmlStringRGB(color), button);
            GUI.backgroundColor = old;
            if (toggle) { if (!lookColors.Add(key)) lookColors.Remove(key); }
            if (lookColors.Contains(key)) material.SetColor(property, ColorSliders(caption, color));
        }
        void RestoreLookMaterials()
        {
            if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
            foreach (var renderer in gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)))
            {
                var slots = renderer.sharedMaterials; bool changed = false;
                for (int i = 0; i < slots.Length; i++)
                    foreach (var pair in lookCopies) if (slots[i] == pair.Value) { slots[i] = pair.Key; changed = true; break; }
                if (changed) renderer.sharedMaterials = slots;
            }
            if (lookOriginalSky && lookCopies.TryGetValue(lookOriginalSky, out var sky) && RenderSettings.skybox == sky) RenderSettings.skybox = lookOriginalSky;
            foreach (var copy in lookCopies.Values) if (copy) { if (Application.isPlaying) Destroy(copy); else DestroyImmediate(copy); }
            lookCopies.Clear(); lookMaterials = null; lookMeadow = null; lookOriginalSky = null;
        }
    }
}
