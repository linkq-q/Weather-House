using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    /// <summary>Material-only preview of the existing v05 house; never saves a scene or source material.</summary>
    public sealed class WeatherHouseOilPaintWindow : EditorWindow
    {
        const string HouseFolder = "Assets/Environment/WeatherHouseV05";
        const string PrefabPath = HouseFolder + "/WeatherHouse_v05.prefab";
        const string CandidateFolder = HouseFolder + "/OilPaintPreview";
        const string ShaderName = "Weather House/Oil Paint Surface";
        const string AtlasPath = "Assets/Experiments/Materials/Textures/OilBrushStamps.png";
        static readonly string[] Parts = { "Architecture", "RoofTiles", "Moss", "Foliage", "WeatherWindow" };
        static readonly string[] Labels = { "墙体与木构", "屋顶瓦片", "苔藓", "屋顶叶片", "天气窗" };
        [SerializeField] int selected;
        [SerializeField] bool colorsOpen, lightingOpen;
        Vector2 scroll;
        string status;

        [MenuItem("Weather House/Material Lab/天气屋油画实时调参")]
        public static void OpenWindow()
        {
            var window = GetWindow<WeatherHouseOilPaintWindow>("天气屋油画");
            window.minSize = new Vector2(350, 430);
            window.Show();
        }

        static void RequireEdit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在编辑模式调参；无需切换场景或进入 Play。");
        }
        static string SourcePath(int i) => HouseFolder + "/Materials/WH05_" + Parts[i] + ".mat";
        static string CandidatePath(int i) => CandidateFolder + "/WH05_" + Parts[i] + "_Oil.mat";
        static Material Source(int i) => AssetDatabase.LoadAssetAtPath<Material>(SourcePath(i));
        static Material Candidate(int i) => AssetDatabase.LoadAssetAtPath<Material>(CandidatePath(i));
        static Shader PaintShader()
        {
            var shader = Shader.Find(ShaderName);
            if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader) || shader.FindPropertyIndex("_TextureInfluence") < 0)
                throw new InvalidOperationException("油画 Shader 尚未成功导入。");
            return shader;
        }
        static void ValidateCandidate(Material material)
        {
            if (!material || material.shader.name != ShaderName || !material.HasProperty("_TextureInfluence"))
                throw new InvalidOperationException("候选材质或贴图兼容 Shader 不匹配；保留当前内容，不覆盖。");
        }

        // New assets only. Existing candidates may contain the user's tuning and must not be reset here.
        public static string CreateCandidates()
        {
            RequireEdit(); var shader = PaintShader();
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            if (!atlas || Parts.Where((_, i) => !Source(i)).Any())
                throw new InvalidOperationException("天气屋原材质或原笔触图集缺失。");
            if (Parts.Where((_, i) => (Source(i).HasProperty("_Surface") && Source(i).GetFloat("_Surface") != 0) ||
                (Source(i).HasProperty("_AlphaClip") && Source(i).GetFloat("_AlphaClip") != 0)).Any())
                throw new InvalidOperationException("原材质已增加透明或裁切设置，需先核对，不应用不透明油画预览。");
            for (int i = 0; i < Parts.Length; i++)
                if (File.Exists(CandidatePath(i))) ValidateCandidate(Candidate(i));
            if (!AssetDatabase.IsValidFolder(CandidateFolder))
                AssetDatabase.CreateFolder(HouseFolder, "OilPaintPreview");
            string backup = null; int created = 0;
            for (int i = 0; i < Parts.Length; i++)
            {
                if (Candidate(i)) continue;
                if (backup == null)
                {
                    backup = "JourneyReview/WeatherHouseOilPaintV1/OriginalMaterials-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
                    Directory.CreateDirectory(backup);
                    for (int n = 0; n < Parts.Length; n++)
                    {
                        File.Copy(SourcePath(n), backup + "/" + Path.GetFileName(SourcePath(n)));
                        File.WriteAllText(backup + "/" + Parts[n] + "-live.json", EditorJsonUtility.ToJson(Source(n), true));
                    }
                }
                var material = new Material(Source(i)) { name = "WH05_" + Parts[i] + "_Oil" };
                SetPreset(material, Source(i), i, shader, atlas);
                AssetDatabase.CreateAsset(material, CandidatePath(i)); created++;
            }
            return "新建 " + created + " 份候选；原材质未改动。" + (backup == null ? "" : "备份：" + backup);
        }

        static void SetPreset(Material material, Material source, int i, Shader shader, Texture atlas)
        {
            Color pigment = source.GetColor("_BaseColor");
            float cull = source.HasProperty("_Cull") ? source.GetFloat("_Cull") : 2;
            Texture baseMap = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") :
                AssetDatabase.LoadAssetAtPath<Texture2D>(HouseFolder + "/Textures/" + Parts[i] + "_BaseColor.png");
            Vector2 scale = source.HasProperty("_BaseMap") ? source.GetTextureScale("_BaseMap") : Vector2.one;
            Vector2 offset = source.HasProperty("_BaseMap") ? source.GetTextureOffset("_BaseMap") : Vector2.zero;
            if (!baseMap) throw new InvalidOperationException("缺少原颜色贴图：" + Parts[i]);
            material.shader = shader; material.shaderKeywords = Array.Empty<string>();
            material.renderQueue = -1; material.SetOverrideTag("RenderType", "Opaque");
            material.SetTexture("_BaseMap", baseMap); material.SetTextureScale("_BaseMap", scale); material.SetTextureOffset("_BaseMap", offset);
            // The current moss is procedural: leave its baked map opt-in, while preserving its green palette.
            material.SetFloat("_TextureInfluence", i == 2 ? 0 : 1); material.SetFloat("_Cull", cull);
            material.SetColor("_BaseColor", pigment);
            material.SetColor("_ShadowColor", pigment * new Color(.66f, .74f, .70f, 1));
            material.SetColor("_HighlightColor", pigment * new Color(1.05f, 1.02f, .94f, 1));
            material.SetColor("_StrokeColor", pigment * new Color(1.02f, .98f, .88f, 1));
            material.SetColor("_ShadowEdgeColor", pigment * new Color(.72f, .82f, .77f, 1));
            material.SetTexture("_BrushAtlas", atlas); material.SetFloat("_UseBrushAtlas", 1);
            material.SetFloat("_BrushScale", 1 / (i == 2 ? .7f : .38f));
            material.SetFloat("_StrokeDensity", 1.15f); material.SetFloat("_BrushStrength", i == 4 ? .28f : .48f);
            material.SetFloat("_StrokeAspect", .7f); material.SetFloat("_StrokeAngle", 25); material.SetFloat("_StrokeScatter", 42);
            material.SetFloat("_ColorSteps", 6); material.SetFloat("_ShadowFill", .22f);
            material.SetFloat("_ShadowEdgeWidth", .14f); material.SetFloat("_ShadowEdgeStrength", .18f);
            material.SetFloat("_Impasto", .3f); material.SetFloat("_Smoothness", .24f);
        }

        static GameObject CurrentHouse()
        {
            var scene = SceneManager.GetActiveScene();
            var matches = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name == "天气屋 · v05 苔顶木构").ToArray();
            if (matches.Length != 1 || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(matches[0].gameObject) != PrefabPath)
                throw new InvalidOperationException("当前场景需要唯一的 v05 天气屋实例；未修改其他对象。");
            return matches[0].gameObject;
        }
        static int PartIndex(Material material)
        {
            for (int i = 0; i < Parts.Length; i++)
                if (material && (material == Source(i) || material == Candidate(i))) return i;
            return -1;
        }
        static int ApplyMaterials(GameObject house, bool painted, bool recordUndo)
        {
            var renderers = house.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length != 15) throw new InvalidOperationException("天气屋渲染器数量已变化，需先核对所有 LOD。");
            var indices = renderers.Select(r => r.sharedMaterials.Length == 1 ? PartIndex(r.sharedMaterial) : -1).ToArray();
            if (indices.Any(i => i < 0) || Enumerable.Range(0, Parts.Length).Any(i => indices.Count(n => n == i) != 3))
                throw new InvalidOperationException("存在新的材质覆盖或 LOD 分组变化；保留用户当前修改。");
            var targets = indices.Select(i => painted ? Candidate(i) : Source(i)).ToArray();
            if (targets.Any(m => !m)) throw new InvalidOperationException("请先创建五份候选材质。");
            if (painted) foreach (var material in targets) ValidateCandidate(material);
            if (recordUndo) Undo.RecordObjects(renderers, painted ? "预览天气屋油画" : "恢复天气屋原材质");
            int changes = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].sharedMaterial == targets[i]) continue;
                renderers[i].sharedMaterial = targets[i]; changes++;
                if (recordUndo) PrefabUtility.RecordPrefabInstancePropertyModifications(renderers[i]);
            }
            SceneView.RepaintAll(); EditorApplication.QueuePlayerLoopUpdate();
            return changes;
        }
        public static string PreviewCurrent()
        {
            RequireEdit(); var house = CurrentHouse(); CreateCandidates();
            return "已替换 " + ApplyMaterials(house, true, true) + " 个 LOD 材质槽；场景尚未保存。";
        }
        public static string RestoreCurrent()
        {
            RequireEdit(); return "已恢复 " + ApplyMaterials(CurrentHouse(), false, true) + " 个材质槽；其他场景编辑保留。";
        }
        public static string InspectCurrent()
        {
            var house = CurrentHouse();
            return string.Join("\n", house.GetComponentsInChildren<Renderer>(true).Select(r =>
                r.name + " = " + string.Join(", ", r.sharedMaterials.Select(AssetDatabase.GetAssetPath))));
        }

        // Root may run this in Edit mode: only an isolated prefab contents scene is rebound, never the live house.
        public static string VerifyIsolatedBindings()
        {
            RequireEdit(); CreateCandidates();
            var house = PrefabUtility.LoadPrefabContents(PrefabPath);
            Material foreign = null;
            try
            {
                var renderers = house.GetComponentsInChildren<Renderer>(true);
                var original = renderers.Select(r => r.sharedMaterial).ToArray();
                if (ApplyMaterials(house, true, false) != 15 || ApplyMaterials(house, true, false) != 0)
                    throw new Exception("Preview/idempotence failed.");
                if (ApplyMaterials(house, false, false) != 15 || renderers.Where((r, i) => r.sharedMaterial != original[i]).Any())
                    throw new Exception("Original material restore failed.");
                foreign = new Material(original[0]); renderers[0].sharedMaterial = foreign;
                bool refused = false;
                try { ApplyMaterials(house, true, false); } catch (InvalidOperationException) { refused = true; }
                if (!refused || renderers[0].sharedMaterial != foreign || renderers.Skip(1).Where((r, i) => r.sharedMaterial != original[i + 1]).Any())
                    throw new Exception("Foreign override preservation failed.");
                for (int i = 0; i < Parts.Length; i++)
                {
                    var source = Source(i); var candidate = Candidate(i);
                    Texture expected = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") :
                        AssetDatabase.LoadAssetAtPath<Texture2D>(HouseFolder + "/Textures/" + Parts[i] + "_BaseColor.png");
                    if (candidate.GetTexture("_BaseMap") != expected || candidate.GetFloat("_Cull") != source.GetFloat("_Cull"))
                        throw new Exception("Original color map or culling changed: " + Parts[i]);
                }
                return "PASS 15 slots / 3 LODs: preview, repeat, restore, foreign override refusal, original maps and face culling. GPU appearance requires visual review.";
            }
            finally { PrefabUtility.UnloadPrefabContents(house); if (foreign) DestroyImmediate(foreign); }
        }

        void Action(Func<string> action)
        {
            try { status = action(); } catch (Exception e) { status = e.Message; Debug.LogException(e); }
        }
        void OnGUI()
        {
            EditorGUILayout.LabelField("天气屋 · 原油画笔触", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("实时调节当前天气屋的五类材质，覆盖全部三档 LOD。保留原贴图、UV 和模型；不切相机、不加全屏滤镜。", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("预览油画")) Action(PreviewCurrent);
                    if (GUILayout.Button("恢复原材质")) Action(RestoreCurrent);
                }
                EditorGUILayout.LabelField("替换可 Ctrl+Z 撤销；关闭窗口保留当前预览，不自动保存场景。", EditorStyles.wordWrappedMiniLabel);
                selected = EditorGUILayout.Popup("调节部位", selected, Labels);
                var material = Candidate(selected);
                if (material && material.shader.name == ShaderName)
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    Float(material, "原贴图保留", "_TextureInfluence", 0, 1, "0 使用油画色板；1 保留原UV贴图的色彩和细节。");
                    float size = 1 / Mathf.Max(.5f, material.GetFloat("_BrushScale"));
                    EditorGUI.BeginChangeCheck();
                    float value = EditorGUILayout.Slider(new GUIContent("笔触尺寸", "物体空间尺寸；越大越宽，不改变模型。"), size, 1f / 24, 2);
                    if (EditorGUI.EndChangeCheck()) Change(material, () => material.SetFloat("_BrushScale", 1 / value));
                    Float(material, "笔触密度", "_StrokeDensity", 0, 2, "与尺寸独立；0 隐藏笔触。");
                    Float(material, "笔触强度", "_BrushStrength", 0, 1);
                    Float(material, "笔触长宽比", "_StrokeAspect", .25f, 8);
                    Float(material, "笔触方向", "_StrokeAngle", -180, 180);
                    Float(material, "方向散差", "_StrokeScatter", 0, 90);
                    colorsOpen = EditorGUILayout.Foldout(colorsOpen, "底色、笔触与明暗色", true);
                    if (colorsOpen)
                    {
                        ColorField(material, "底色染色", "_BaseColor"); ColorField(material, "笔触颜色", "_StrokeColor");
                        ColorField(material, "阴影颜色", "_ShadowColor"); ColorField(material, "亮部颜色", "_HighlightColor");
                        ColorField(material, "明暗交界色", "_ShadowEdgeColor");
                    }
                    lightingOpen = EditorGUILayout.Foldout(lightingOpen, "明暗与厚涂", true);
                    if (lightingOpen)
                    {
                        Float(material, "暗部提亮", "_ShadowFill", 0, 1);
                        Float(material, "交界宽度", "_ShadowEdgeWidth", .02f, .4f);
                        Float(material, "交界着色", "_ShadowEdgeStrength", 0, 1);
                        Float(material, "明暗色阶", "_ColorSteps", 2, 12);
                        Float(material, "厚涂起伏", "_Impasto", 0, 2);
                        Float(material, "表面光泽", "_Smoothness", 0, 1);
                    }
                    if (GUILayout.Button("恢复此部位的初始候选参数")) Action(() =>
                    {
                        Change(material, () => SetPreset(material, Source(selected), selected, PaintShader(), AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath)));
                        return "已恢复候选默认参数；原材质未改。";
                    });
                    EditorGUILayout.EndScrollView();
                    if (GUILayout.Button("保存五份候选参数（仅材质）")) Action(() =>
                    {
                        for (int i = 0; i < Parts.Length; i++) { ValidateCandidate(Candidate(i)); AssetDatabase.SaveAssetIfDirty(Candidate(i)); }
                        return "已保存五份独立候选材质；未保存场景、原材质或其他资产。";
                    });
                    EditorGUILayout.HelpBox("此预览使用油画光照与厚涂，原法线和金属高光不参与该效果；恢复原材质可回到原来的完整表现。苔藓默认保留现有绿色色板。", MessageType.None);
                }
                else EditorGUILayout.HelpBox("点击“预览油画”创建独立材质并应用到当前天气屋。", MessageType.None);
            }
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Info);
        }
        static void Change(Material material, Action change)
        {
            Undo.RecordObject(material, "调节天气屋油画"); change(); EditorUtility.SetDirty(material);
            SceneView.RepaintAll(); EditorApplication.QueuePlayerLoopUpdate();
        }
        static void Float(Material material, string label, string property, float min, float max, string tip = "")
        {
            EditorGUI.BeginChangeCheck(); float value = EditorGUILayout.Slider(new GUIContent(label, tip), material.GetFloat(property), min, max);
            if (EditorGUI.EndChangeCheck()) Change(material, () => material.SetFloat(property, value));
        }
        static void ColorField(Material material, string label, string property)
        {
            EditorGUI.BeginChangeCheck(); Color value = EditorGUILayout.ColorField(label, material.GetColor(property));
            if (EditorGUI.EndChangeCheck()) Change(material, () => material.SetColor(property, value));
        }
    }
}
