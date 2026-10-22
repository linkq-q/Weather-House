using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    public sealed class WaterGrassLookWindow : EditorWindow
    {
        readonly Dictionary<Material, Material> previews = new Dictionary<Material, Material>();
        readonly Dictionary<Renderer, Material[]> renderers = new Dictionary<Renderer, Material[]>();
        readonly Dictionary<GrassMeadow, Material[]> meadows = new Dictionary<GrassMeadow, Material[]>();
        Vector2 scroll;
        int page;
        bool advanced;
        int sceneHandle;
        MaterialEditor materialEditor;
        Material selected;
        Material originalSky, previewSky;
        public bool Previewing { get { return previews.Count > 0; } }

        [MenuItem("Weather House/Vegetation Lab/水草画风 · 实时调节")]
        public static WaterGrassLookWindow Open()
        {
            var window = GetWindow<WaterGrassLookWindow>("水草画风");
            window.minSize = new Vector2(380, 420);
            return window;
        }
        void OnEnable()
        {
            EditorApplication.playModeStateChanged += PlayChanged;
            AssemblyReloadEvents.beforeAssemblyReload += CancelPreview;
        }
        void OnDisable()
        {
            CancelPreview();
            EditorApplication.playModeStateChanged -= PlayChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= CancelPreview;
        }
        void PlayChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode) CancelPreview();
        }
        void OnInspectorUpdate()
        {
            if (!Previewing) return;
            if (SceneManager.GetActiveScene().handle != sceneHandle) { CancelPreview(); return; }
            if (Application.isPlaying)
            {
                foreach (var pair in previews)
                {
                    if (!pair.Key || pair.Key.shader.name != "Weather House/Painterly Grass") continue;
                    foreach (string property in new[] { "_BaseColor", "_TipColor" }) pair.Value.SetColor(property, pair.Key.GetColor(property));
                    foreach (string property in new[] { "_WindStrength", "_WindSpeed", "_UpNormals" }) pair.Value.SetFloat(property, pair.Key.GetFloat(property));
                }
                foreach (var meadow in meadows.Keys.Where(m => m))
                foreach (var renderer in meadow.GetComponentsInChildren<Renderer>(true))
                {
                    var current = renderer.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < current.Length; i++)
                        if (current[i] && previews.TryGetValue(current[i], out var copy)) { current[i] = copy; changed = true; }
                    if (changed) renderer.sharedMaterials = current;
                }
            }
            Repaint();
            SceneView.RepaintAll();
            if (!Application.isPlaying) EditorApplication.QueuePlayerLoopUpdate();
        }
        static bool Supported(Material material)
        {
            if (!material || !material.shader) return false;
            string shader = material.shader.name;
            return shader == "Weather House/Painterly Water" || shader == "Weather House/Painterly Grass"
                || shader == "Weather House/Painterly Ground";
        }
        public void BeginPreview()
        {
            CancelPreview();
            var scene = SceneManager.GetActiveScene();
            sceneHandle = scene.handle;
            var view = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GrassLabView>(true)).FirstOrDefault();
            var meadowTarget = view ? view.meadow : null;
            if (!meadowTarget) return;
            var allowed = new HashSet<Material>(meadowTarget.materials.Where(m => m));
            var sceneRenderers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToArray();
            var ground = sceneRenderers.Where(r => r.sharedMaterial && r.sharedMaterial.shader.name == "Weather House/Painterly Ground")
                .OrderBy(r => (r.bounds.center - meadowTarget.transform.position).sqrMagnitude).FirstOrDefault();
            if (ground) allowed.Add(ground.sharedMaterial);
            var water = sceneRenderers.Where(r => r.GetComponent<PainterlyWaterReflection>())
                .OrderBy(r => (r.bounds.center - meadowTarget.transform.position).sqrMagnitude).FirstOrDefault();
            if (water && water.sharedMaterial) allowed.Add(water.sharedMaterial);
            foreach (var root in scene.GetRootGameObjects())
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] original = renderer.sharedMaterials;
                if (!original.Any(m => m && allowed.Contains(m))) continue;
                renderers.Add(renderer, original);
                renderer.sharedMaterials = original.Select(m => m && allowed.Contains(m) ? Clone(m) : m).ToArray();
            }
            foreach (var root in scene.GetRootGameObjects())
            foreach (var meadow in root.GetComponentsInChildren<GrassMeadow>(true))
            {
                if (meadow != meadowTarget || meadow.materials == null) continue;
                meadows.Add(meadow, meadow.materials);
                if (!Application.isPlaying)
                    meadow.materials = meadow.materials.Select(m => m && previews.TryGetValue(m, out var copy) ? copy : m).ToArray();
            }
            originalSky = RenderSettings.skybox;
            if (originalSky)
            {
                previewSky = new Material(originalSky) { name = originalSky.name + " · 试调", hideFlags = HideFlags.HideAndDontSave };
                previews.Add(originalSky, previewSky);
                RenderSettings.skybox = previewSky;
            }
        }
        Material Clone(Material source)
        {
            if (!Supported(source)) return source;
            if (!previews.TryGetValue(source, out var copy))
            {
                copy = new Material(source) { name = source.name + " · 试调", hideFlags = HideFlags.HideAndDontSave };
                previews.Add(source, copy);
            }
            return copy;
        }
        public void CancelPreview()
        {
            if (materialEditor) DestroyImmediate(materialEditor);
            materialEditor = null; selected = null;
            if (previewSky && RenderSettings.skybox == previewSky) RenderSettings.skybox = originalSky;
            // Include chunks rebuilt while the preview was running.
            foreach (var meadow in meadows.Keys.Where(m => m))
            foreach (var renderer in meadow.GetComponentsInChildren<Renderer>(true)) RestoreRenderer(renderer);
            foreach (var renderer in renderers.Keys.Where(r => r)) RestoreRenderer(renderer);
            foreach (var entry in meadows) if (entry.Key) entry.Key.materials = entry.Value;
            foreach (var copy in previews.Values) if (copy) DestroyImmediate(copy);
            previews.Clear(); renderers.Clear(); meadows.Clear();
            originalSky = null; previewSky = null;
        }
        void RestoreRenderer(Renderer renderer)
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
                foreach (var pair in previews) if (materials[i] == pair.Value) { materials[i] = pair.Key; break; }
            renderer.sharedMaterials = materials;
        }
        public Material[] PreviewMaterials { get { return previews.Values.ToArray(); } }
        public void SavePreview()
        {
            if (Application.isPlaying) throw new InvalidOperationException("请退出 Play 后保存；运行中草地使用临时资产。");
            foreach (var pair in previews)
                if (!EditorUtility.IsPersistent(pair.Key)) throw new InvalidOperationException("发现临时源材质，请在编辑模式重新开始试调。");
            foreach (var pair in previews)
            {
                Undo.RecordObject(pair.Key, "保存水草画风");
                pair.Key.CopyPropertiesFromMaterial(pair.Value);
                EditorUtility.SetDirty(pair.Key);
                AssetDatabase.SaveAssetIfDirty(pair.Key);
            }
        }
        public static void ApplyIllustratedStyle(IEnumerable<Material> materials)
        {
            foreach (var m in materials)
            {
                if (m.shader.name == "Weather House/Painterly Water")
                {
                    m.SetColor("_ShallowColor", new Color(.52f,.72f,.64f));
                    m.SetColor("_DeepColor", new Color(.22f,.52f,.46f));
                    m.SetColor("_ReflectionTint", new Color(.48f,.68f,.57f));
                    m.SetColor("_RippleColor", new Color(.83f,.91f,.77f));
                    m.SetColor("_FoamColor", new Color(.91f,.93f,.80f));
                    m.SetFloat("_Opacity", .97f); m.SetFloat("_WaterSaturation", 1.12f);
                    m.SetFloat("_ReflectionNear", .16f); m.SetFloat("_ReflectionFar", .95f);
                    m.SetFloat("_ReflectionFalloff", 1.6f); m.SetFloat("_ReflectionTintStrength", .22f);
                    m.SetFloat("_ReflectionStrength", .84f); m.SetFloat("_ReflectionDistortion", .018f);
                    m.SetFloat("_SurfaceStrokeStrength", .30f); m.SetFloat("_SmallStrokeWeight", .28f);
                    m.SetFloat("_RippleSpeed", .30f); m.SetFloat("_RippleStrength", .52f);
                    m.SetFloat("_ShoreWidth", .30f); m.SetFloat("_ShoreStrength", .90f);
                    m.SetFloat("_FoamSpeed", .65f); m.SetFloat("_FoamTravel", .8f);
                    m.SetFloat("_FoamSoftness", .05f); m.SetFloat("_WaveletStrength", .32f);
                    m.SetFloat("_WaveletScale", .7f); m.SetFloat("_WaveletSpeed", .45f);
                }
                if (m.shader.name == "Weather House/Painterly Grass")
                {
                    m.SetFloat("_MeadowBlend", .94f); m.SetFloat("_BladeContrast", .12f);
                    m.SetColor("_ShadowTint", new Color(.56f,.69f,.62f));
                }
                if (m.shader.name == "Skybox/Procedural")
                {
                    m.SetColor("_SkyTint", new Color(.66f,.73f,.69f));
                    m.SetFloat("_Exposure", 1.0f);
                }
            }
            SyncGround(materials);
        }
        public static void SyncGround(IEnumerable<Material> materials)
        {
            var ground = materials.FirstOrDefault(m => m.shader.name == "Weather House/Painterly Ground");
            if (!ground) return;
            foreach (var m in materials.Where(m => m.shader.name == "Weather House/Painterly Grass"))
            {
                m.SetColor("_MeadowBaseColor", ground.GetColor("_BaseColor"));
                m.SetColor("_MeadowPatchColor", ground.GetColor("_PatchColor"));
                m.SetFloat("_MeadowPatchScale", ground.GetFloat("_PatchScale"));
            }
        }
        void OnGUI()
        {
            EditorGUILayout.LabelField("水体与草地 · 实时画风试调", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("先打开 VegetationLab。开始试调会使用材质副本；取消、关闭窗口、切换 Play 或编译会恢复。保存只写回本轮预览的材质。", MessageType.Info);
            if (!Previewing)
            {
                if (GUILayout.Button("开始试调当前场景")) BeginPreview();
                return;
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("连片草地 / 灰绿池塘")) ApplyIllustratedStyle(previews.Values);
            if (GUILayout.Button("还原本次初始值")) foreach (var pair in previews) pair.Value.CopyPropertiesFromMaterial(pair.Key);
            EditorGUILayout.EndHorizontal();
            page = GUILayout.Toolbar(page, new[] { "水体", "草地", "地面", "天空" });
            string shader = page == 0 ? "Weather House/Painterly Water" : page == 1 ? "Weather House/Painterly Grass" : "Weather House/Painterly Ground";
            var choices = page == 3 ? (previewSky ? new[] { previewSky } : new Material[0]) : previews.Values.Where(m => m.shader.name == shader).ToArray();
            if (!choices.Contains(selected)) selected = choices.FirstOrDefault();
            if (choices.Length > 1)
                selected = choices[EditorGUILayout.Popup("材质", Math.Max(0, Array.IndexOf(choices, selected)), choices.Select(m => m.name).ToArray())];
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (selected)
            {
                if (page == 0)
                {
                    ColorControl("浅水颜色", "_ShallowColor"); ColorControl("深水颜色", "_DeepColor");
                    FloatControl("颜料饱和度", "_WaterSaturation", 0, 2); FloatControl("不透明度", "_Opacity", 0, 1);
                    FloatControl("近处 / 俯视倒影", "_ReflectionNear", 0, 1); FloatControl("远处 / 掠视倒影", "_ReflectionFar", 0, 1);
                    FloatControl("视角过渡", "_ReflectionFalloff", .2f, 5); FloatControl("整体倒影强度", "_ReflectionStrength", 0, 1);
                    ColorControl("倒影染色", "_ReflectionTint"); FloatControl("倒影染色量", "_ReflectionTintStrength", 0, 1);
                    FloatControl("倒影扰动", "_ReflectionDistortion", 0, .06f);
                    FloatControl("表面笔触", "_SurfaceStrokeStrength", 0, 1); FloatControl("小笔触权重", "_SmallStrokeWeight", 0, 1);
                    ColorControl("水波颜色", "_RippleColor"); FloatControl("短 S 水波强度", "_WaveletStrength", 0, 1);
                    FloatControl("短水波密度", "_WaveletScale", .1f, 3); FloatControl("短水波速度", "_WaveletSpeed", 0, 2);
                    FloatControl("短水波宽度", "_WaveletWidth", .01f, .3f); FloatControl("短水波长度", "_WaveletLength", .3f, 1.5f);
                    ColorControl("泡沫颜色", "_FoamColor"); FloatControl("岸边泡沫强度", "_ShoreStrength", 0, 1);
                    FloatControl("岸边泡沫宽度", "_ShoreWidth", .01f, 1); FloatControl("泡沫流速", "_FoamSpeed", 0, 2);
                    FloatControl("泡沫进退幅度", "_FoamTravel", 0, 1); FloatControl("泡沫软边", "_FoamSoftness", .01f, .5f);
                }
                else if (page == 1)
                {
                    FloatControl("连片色块混合", "_MeadowBlend", 0, 1); FloatControl("逐叶明暗对比", "_BladeContrast", 0, 1);
                    ColorControl("共用草地底色", "_MeadowBaseColor"); ColorControl("共用暖色斑块", "_MeadowPatchColor");
                    FloatControl("大色块频率", "_MeadowPatchScale", .02f, 2); ColorControl("阴影色", "_ShadowTint");
                    EditorGUILayout.HelpBox("高度、数量、草形、风和根尖色继续用原来的 Play 面板。这里调整整片草地的色块关系，不增加草量。", MessageType.None);
                }
                else if (page == 2)
                {
                    ColorControl("地面底色", "_BaseColor"); ColorControl("暖色斑块", "_PatchColor");
                    ColorControl("岸边土色", "_ShoreColor"); FloatControl("色块频率", "_PatchScale", .02f, 2);
                }
                else
                {
                    ColorControl("天空色调", "_SkyTint"); ColorControl("地平线下方颜色", "_GroundColor");
                    FloatControl("天空曝光", "_Exposure", 0, 3); FloatControl("大气厚度", "_AtmosphereThickness", 0, 5);
                    FloatControl("太阳大小", "_SunSize", 0, 1);
                    EditorGUILayout.HelpBox("天空盒影响水面平面倒影。环境光和雾仍沿用当前场景设置。", MessageType.None);
                }
                if (GUILayout.Button("将地面色块同步到所有草材质")) SyncGround(previews.Values);
                advanced = EditorGUILayout.Foldout(advanced, "全部 Shader 参数（高级）", true);
                if (advanced)
                {
                    if (!materialEditor || materialEditor.target != selected)
                    {
                        if (materialEditor) DestroyImmediate(materialEditor);
                        materialEditor = (MaterialEditor)UnityEditor.Editor.CreateEditor(selected);
                    }
                    materialEditor.PropertiesDefaultGUI(MaterialEditor.GetMaterialProperties(new Object[] { selected }));
                }
            }
            else EditorGUILayout.LabelField("当前场景没有这种材质。");
            EditorGUILayout.EndScrollView();
            using (new EditorGUI.DisabledScope(Application.isPlaying))
                if (GUILayout.Button("保存试调到原材质（支持 Undo）")) SavePreview();
            if (Application.isPlaying) EditorGUILayout.HelpBox("Play 试调是临时的。需要保留时，请在编辑模式用同一窗口调整并保存。", MessageType.None);
            if (GUILayout.Button("结束试调，恢复原材质引用")) CancelPreview();
        }
        void FloatControl(string label, string property, float min, float max)
        {
            if (!selected.HasProperty(property)) return;
            selected.SetFloat(property, EditorGUILayout.Slider(label, selected.GetFloat(property), min, max));
        }
        void ColorControl(string label, string property)
        {
            if (!selected.HasProperty(property)) return;
            selected.SetColor(property, EditorGUILayout.ColorField(label, selected.GetColor(property)));
        }
    }
}
