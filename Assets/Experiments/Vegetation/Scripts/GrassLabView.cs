using UnityEngine;

namespace WeatherHouse.Vegetation
{
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(-10)]
    public partial class GrassLabView : MonoBehaviour
    {
        public GrassMeadow meadow;
        [Tooltip("可选的地形区块列表。留空时保持原场景单一区域操作。")]
        public GrassMeadow[] regions;
        // Keep saved orbit settings when adding navigation to an existing scene.
        [HideInInspector] public Vector3 target = new Vector3(0, 1, 1);
        [HideInInspector] public float distance = 31, yaw = -30, pitch = 27;
        public bool showPanel = true;
        Font font;
        GUIStyle label, title, button, tabs;
        GrassLabCamera navigation;
        Vector2 scroll;
        int page, selectedSpecies;
        string parameterSaveMessage;
        Rect PanelRect { get { return new Rect(14, 14, Mathf.Min(370, Screen.width - 28), Mathf.Max(180, Screen.height - 28)); } }

        public GrassLabCamera Navigation
        {
            get
            {
                if (!navigation) navigation = GetComponent<GrassLabCamera>();
                if (!navigation)
                {
                    navigation = gameObject.AddComponent<GrassLabCamera>();
                    navigation.target = target; navigation.distance = distance;
                    navigation.yaw = yaw; navigation.pitch = pitch;
                }
                navigation.meadow = meadow;
                return navigation;
            }
        }
        void Awake() { Navigation.meadow = meadow; }
        void Start()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 16);
            Navigation.PlaceCamera();
        }
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab)) showPanel = !showPanel;
            Navigation.pointerOverPanel = showPanel && PanelRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
        }
        public void PlaceCamera() { Navigation.PlaceCamera(); }
        public void Overview() { Navigation.Overview(); }
        public void CloseGrass() { Navigation.CloseGrass(); }
        public void Shore() { Navigation.Shore(); }
        public void SelectRegion(int index)
        {
            if (regions == null || index < 0 || index >= regions.Length || !regions[index]) return;
            meadow = regions[index];
            selectedSpecies = Mathf.Clamp(selectedSpecies, 0, Mathf.Max(0, meadow.SpeciesCount - 1));
            scroll = Vector2.zero;
            Navigation.FocusMeadow(meadow);
        }

        void OnGUI()
        {
            if (!showPanel || !meadow) return;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 14, wordWrap = true };
                title = new GUIStyle(label) { fontSize = 22, fontStyle = FontStyle.Bold };
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 14, wordWrap = true };
                tabs = new GUIStyle(button) { fontSize = 12, wordWrap = false };
            }
            Rect rect = PanelRect;
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 14, rect.y + 12, rect.width - 28, rect.height - 24));
            GUILayout.Label(Navigation.libraryLayout ? "植物资源库 · 试验场" : "草地与水体 · 试验场", title);
            if (regions != null && regions.Length > 0)
            {
                var labels = new string[regions.Length];
                int selected = -1;
                for (int i = 0; i < regions.Length; i++)
                {
                    labels[i] = i == 0 ? "原始草地" : regions[i] ? regions[i].name : "未配置区域";
                    if (regions[i] == meadow) selected = i;
                }
                int chosen = GUILayout.SelectionGrid(selected, labels, 2, button, GUILayout.Height(Mathf.CeilToInt(labels.Length / 2f) * 34));
                if (chosen != selected) SelectRegion(chosen);
            }
            page = GUILayout.Toolbar(page, new[] { "比例", "形状", "颜色/风", "相机", "水草画风" }, tabs, GUILayout.Height(30));
#if UNITY_EDITOR
            if (GUILayout.Button("保存草地与画风为默认值", button, GUILayout.Height(30)))
            {
                try { SaveRegionDefaults(); parameterSaveMessage = "已保存。退出 Play、重新打开场景后仍保留。"; }
                catch (System.Exception error) { parameterSaveMessage = error.Message; }
            }
            if (!string.IsNullOrEmpty(parameterSaveMessage)) GUILayout.Label(parameterSaveMessage, label);
#endif
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Space(8);
            if (meadow.ConfigurationError != null && page != 3) GUILayout.Label(meadow.ConfigurationError, label);
            else if (page == 0) DrawMeadow();
            else if (page == 1) DrawShape();
            else if (page == 2) DrawAppearance();
            else if (page == 3) DrawCamera();
            else DrawWaterGrassLook();
            GUILayout.EndScrollView();
            GUILayout.Space(6);
            if (!string.IsNullOrEmpty(meadow.LiveError)) GUILayout.Label("更新失败：" + meadow.LiveError, label);
            else GUILayout.Label(meadow.IsLiveUpdating ? "正在更新草地 · " + (meadow.LiveProgress * 100).ToString("F0") + "%" : "草地已同步", label);
            if (page == 4) GUILayout.Label("拖动立即改变画风；点击保存可在重开场景后保留。\nTab 隐藏面板 · 4 行走 · 5 飞行", label);
            else GUILayout.Label("拖动即更新，松开后自动完成整片草地。\nTab 隐藏面板 · 4 行走 · 5 飞行\n点击上方保存按钮，可保留比例、形状、颜色和风。", label);
            if (Navigation.libraryLayout) GUILayout.Label("参数作用于右侧混合草地；左侧展示台保留已保存的资源样本。", label);
            GUILayout.EndArea();
        }
        public void SaveRegionDefaults()
        {
            if (!meadow.defaults) throw new System.InvalidOperationException("当前区域尚未配置默认值资产。");
            SaveWaterGrassDefaults();
            meadow.SaveParameterDefaults();
        }
        void DrawMeadow()
        {
            GUILayout.Label(meadow.SpeciesCount + " 种植物 · 比例按草丛数量归一化。", label);
            meadow.totalClumps = IntSlider("总草丛数", meadow.totalClumps, 0, 40000, 100);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("自然混合", button)) meadow.SetMixPreset(0);
            if (GUILayout.Button(meadow.SpeciesCount > 3 ? "草叶为主" : "低矮草坪", button)) meadow.SetMixPreset(1);
            if (GUILayout.Button("花草地", button)) meadow.SetMixPreset(2);
            GUILayout.EndHorizontal();
            GUILayout.Label("分布种子 " + meadow.seed, label);
            if (GUILayout.Button("换一组草地位置", button)) meadow.seed = unchecked(meadow.seed + 1);
            float sum = 0;
            foreach (float weight in meadow.weights) sum += Mathf.Max(0, weight);
            sum = Mathf.Max(.001f, sum);
            string[] names = meadow.SpeciesLabels();
            int[] counts = meadow.Counts;
            for (int i = 0; i < meadow.SpeciesCount; i++)
                meadow.weights[i] = Slider(names[i] + " " + (meadow.weights[i] / sum * 100).ToString("F1") + "% · 当前 " + counts[i] + " 丛", meadow.weights[i], 0, 100, "F1");
        }
        GrassSpecies ChooseSpecies()
        {
            string[] names = meadow.SpeciesLabels();
            selectedSpecies = Mathf.Clamp(selectedSpecies, 0, names.Length - 1);
            selectedSpecies = GUILayout.SelectionGrid(selectedSpecies, names, 3, button, GUILayout.Height(Mathf.CeilToInt(names.Length / 3f) * 34));
            GUILayout.Label(names[selectedSpecies], label);
            return meadow.species[selectedSpecies];
        }
        void DrawShape()
        {
            GrassSpecies s = ChooseSpecies();
            bool grass = GrassMeadow.IsGrass(s.kind);
            s.height = Slider("高度（米）", s.height, .08f, 2.5f, "F2");
            s.width = Slider(grass ? "叶片宽度（米）" : "茎/侧叶尺度（米）", s.width, .005f, .12f, "F3");
            if (PlantControlRules.Label(s.kind, "tipWidthRatio") != null) s.tipWidthRatio = Slider("叶尖宽度占比", s.tipWidthRatio, 0, .4f, "F2");
            s.spread = Slider(grass ? "叶尖展开半径（米）" : "植株展开半径（米）", s.spread, .03f, .8f, "F2");
            s.bend = Slider("上部弯曲", s.bend, 0, 1.5f, "F2");
            if (PlantControlRules.Label(s.kind, "bladeCount") != null)
            {
                PlantControlRules.DensityRange(s.kind, out int min, out int max);
                int density = PlantControlRules.DensityValue(s.kind, s.bladeCount);
                int next = IntSlider(PlantControlRules.Label(s.kind, "bladeCount"), density, min, max);
                if (next != density) s.bladeCount = PlantControlRules.DensityRaw(s.kind, next);
            }
            if (PlantControlRules.Label(s.kind, "rootSpread") != null) s.rootSpread = Slider("根部展开比例", s.rootSpread, .025f, .4f, "F3");
            if (PlantControlRules.Label(s.kind, "heightVariation") != null) s.heightVariation = Slider("高度随机变化", s.heightVariation, 0, .7f, "F2");
            if (grass)
            {
                s.outerBladeFraction = Slider("外围展开叶片占比", s.outerBladeFraction, 0, 1, "F2");
                s.clumpLayering = Slider("草簇高低分层", s.clumpLayering, 0, 1, "F2");
                GUILayout.Label("叶宽让整片更厚；叶尖占比只调末端。根束与叶尖展开越小，草簇越聚拢。", label);
            }
            if (PlantControlRules.Label(s.kind, "flowerCount") != null)
            {
                int max = s.kind == GrassKind.Plume || s.kind == GrassKind.LeafyHerb ? 5 : 7;
                int count = Mathf.Clamp(s.flowerCount, 1, max);
                int next = IntSlider(PlantControlRules.Label(s.kind, "flowerCount"), count, 1, max);
                if (next != count) s.flowerCount = next;
            }
            if (PlantControlRules.Label(s.kind, "flowerRadius") != null)
                s.flowerRadius = Slider(PlantControlRules.Label(s.kind, "flowerRadius"), s.flowerRadius, .025f, .22f, "F3");
            if (PlantControlRules.Label(s.kind, "petalCount") != null)
            {
                int max = s.kind == GrassKind.FlowerSpike || s.kind == GrassKind.FlowerSpray ? 5 : 12;
                int count = Mathf.Clamp(s.petalCount, 3, max);
                int next = IntSlider("单花花瓣数", count, 3, max);
                if (next != count) s.petalCount = next;
            }
            if (s.kind == GrassKind.Fern)
            {
                int pairs = PlantControlRules.FernPairs(s.headLength);
                int next = IntSlider("每支羽叶的小叶对数", pairs, 12, 18);
                if (next != pairs) s.headLength = Mathf.Max(.05f, (next - 12) * .1f);
            }
            else if (PlantControlRules.Label(s.kind, "headLength") != null)
                s.headLength = Slider(PlantControlRules.Label(s.kind, "headLength"), s.headLength, .05f, .8f, "F2");
            if (PlantControlRules.Label(s.kind, "leafAspect") != null) s.leafAspect = Slider("叶片长宽比例", s.leafAspect, .1f, 2, "F2");
            if (PlantControlRules.Label(s.kind, "curl") != null) s.curl = Slider(PlantControlRules.Label(s.kind, "curl"), s.curl, 0, 1, "F2");
            GUILayout.Label("形态种子 " + s.seed, label);
            if (GUILayout.Button("换一组植株形态", button)) s.seed = unchecked(s.seed + 1);
        }
        void DrawAppearance()
        {
            GrassSpecies s = ChooseSpecies();
            Color root = ColorSliders("根部颜色", s.baseColor), tip = ColorSliders("尖部颜色", s.tipColor);
            if (root != s.baseColor || tip != s.tipColor) { s.baseColor = root; s.tipColor = tip; meadow.ApplyMaterials(); }
            if (PlantControlRules.Label(s.kind, "flowerColor") != null) s.flowerColor = ColorSliders(PlantControlRules.Label(s.kind, "flowerColor"), s.flowerColor);
            if (PlantControlRules.Label(s.kind, "centerColor") != null) s.centerColor = ColorSliders("花心颜色", s.centerColor);
            float wind = Slider("风力", meadow.windStrength, 0, .6f, "F2");
            float speed = Slider("风速", meadow.windSpeed, 0, 3, "F2");
            float up = Slider("朝上法线 · 0 真实 / 1 柔和", meadow.upNormals, 0, 1, "F2");
            if (wind != meadow.windStrength || speed != meadow.windSpeed || up != meadow.upNormals)
            { meadow.windStrength = wind; meadow.windSpeed = speed; meadow.upNormals = up; meadow.ApplyMaterials(); }
        }
        Color ColorSliders(string caption, Color color)
        {
            GUILayout.Label(caption, label);
            color.r = Slider("R", color.r, 0, 1, "F2");
            color.g = Slider("G", color.g, 0, 1, "F2");
            color.b = Slider("B", color.b, 0, 1, "F2");
            return color;
        }
        void DrawCamera()
        {
            var nav = Navigation;
            int mode = GUILayout.Toolbar((int)nav.mode, new[] { "环绕", "4 行走", "5 飞行" }, button, GUILayout.Height(30));
            if (mode != (int)nav.mode) nav.SetMode((GrassCameraMode)mode);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1 全景", button)) Overview();
            if (GUILayout.Button(nav.libraryLayout ? "2 四档草" : "2 草近景", button)) CloseGrass();
            if (GUILayout.Button(nav.libraryLayout ? "3 点缀近景" : "3 水岸", button)) Shore();
            GUILayout.EndHorizontal();
            nav.moveSpeed = Slider("行走速度（米/秒）", nav.moveSpeed, .5f, 12, "F1");
            nav.flySpeed = Slider("飞行速度（米/秒）", nav.flySpeed, 1, 25, "F1");
            nav.sprintMultiplier = Slider("Shift 加速倍率", nav.sprintMultiplier, 1, 5, "F1");
            nav.lookSensitivity = Slider("鼠标灵敏度", nav.lookSensitivity, .2f, 5, "F1");
            nav.eyeHeight = Slider("行走视点高度（米）", nav.eyeHeight, .35f, 2.2f, "F2");
            Camera camera = GetComponent<Camera>();
            camera.fieldOfView = Slider("视野角度 FOV", camera.fieldOfView, 35, 85, "F0");
            GUILayout.Label("WASD 移动 · 按住右键看向\nShift 加速 · 松开右键或 Esc 释放鼠标\n飞行：E / Space 上升，Q / Ctrl 下降\n" +
                (nav.libraryLayout ? "行走跟随地面；这是浏览相机，展示台与植物不阻挡。" : "行走跟随地面并停在岸边；这是浏览相机，树木和石头不阻挡。"), label);
        }
        float Slider(string caption, float value, float min, float max, string format)
        {
            GUILayout.Label(caption + "  " + value.ToString(format), label);
            float changed = GUILayout.HorizontalSlider(value, min, max);
            return changed;
        }
        int IntSlider(string caption, int value, int min, int max, int step = 1)
        {
            GUILayout.Label(caption + "  " + value, label);
            int changed = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, min, max) / step) * step;
            return changed;
        }
        void OnDestroy() { RestoreLookMaterials(); if (font) Destroy(font); }
    }

    // The runtime panel and Inspector share the fields that each mesh actually uses.
    public static class PlantControlRules
    {
        public static string Label(GrassKind kind, string field)
        {
            bool grass = GrassMeadow.IsGrass(kind), daisy = kind == GrassKind.Daisy;
            bool greenOnly = kind == GrassKind.Fern || kind == GrassKind.LeafyHerb || kind == GrassKind.RoundGroundcover;
            switch (field)
            {
                case "displayName": return "显示名称";
                case "height": return "高度（米）";
                case "width": return grass ? "叶片宽度（米）" : "茎/侧叶尺度（米）";
                case "spread": return "植株展开半径（米）";
                case "bend": return "上部弯曲";
                case "seed": return "形态种子";
                case "baseColor": return "根部颜色";
                case "tipColor": return "尖部颜色";
                case "tipWidthRatio": return grass || daisy ? "叶尖宽度占比" : null;
                case "rootSpread": return daisy || kind == GrassKind.RoundGroundcover ? null : "根部展开比例";
                case "heightVariation": return daisy ? null : "高度随机变化";
                case "outerBladeFraction": return grass ? "外围展开叶片占比" : null;
                case "clumpLayering": return grass ? "草簇高低分层" : null;
                case "bladeCount":
                    if (daisy) return null;
                    if (grass) return "每丛叶片数";
                    if (kind == GrassKind.Fern) return "主羽叶数";
                    if (kind == GrassKind.LeafyHerb) return "每茎侧叶层数";
                    if (kind == GrassKind.RoundGroundcover) return "三圆叶组数";
                    if (kind == GrassKind.Plume) return "羽穗侧枝层数";
                    if (kind == GrassKind.FlowerSpike) return "花序层数";
                    if (kind == GrassKind.FlowerSpray) return "每枝小花数";
                    if (kind == GrassKind.Foxtail) return "穗表面细叶数";
                    return "每茎侧叶数";
                case "flowerCount":
                    if (grass || kind == GrassKind.Fern || kind == GrassKind.RoundGroundcover) return null;
                    return kind == GrassKind.LeafyHerb ? "每丛茎数" : "每丛花茎/穗数";
                case "flowerRadius":
                    if (grass || kind == GrassKind.Fern || kind == GrassKind.LeafyHerb) return null;
                    return kind == GrassKind.RoundGroundcover ? "圆叶尺寸（米）" : "花朵/穗头尺寸（米）";
                case "flowerColor": return grass || greenOnly ? null : "花瓣/穗头颜色";
                case "centerColor": return daisy || kind == GrassKind.FlowerSpike || kind == GrassKind.FlowerSpray || kind == GrassKind.Wildflower || kind == GrassKind.Bellflower ? "花心颜色" : null;
                case "petalCount": return kind == GrassKind.FlowerSpike || kind == GrassKind.FlowerSpray || kind == GrassKind.Wildflower || kind == GrassKind.Bellflower ? "单花花瓣数" : null;
                case "headLength":
                    if (kind == GrassKind.Fern) return "每支羽叶的小叶对数";
                    if (kind == GrassKind.Bellflower) return "花冠深度（米）";
                    return kind == GrassKind.Plume || kind == GrassKind.SeedHead || kind == GrassKind.Foxtail || kind == GrassKind.FlowerSpike ? "花序长度（米）" : null;
                case "leafAspect": return grass || daisy ? null : "叶片长宽比例";
                case "curl": return grass || daisy ? null : kind == GrassKind.Bellflower ? "花冠朝下程度" : "叶片/花序弯卷";
                default: return null;
            }
        }

        public static int FernPairs(float length) { return Mathf.Clamp(12 + Mathf.RoundToInt(length * 10), 12, 18); }
        public static void DensityRange(GrassKind kind, out int min, out int max)
        {
            min = 1; max = 40;
            switch (kind)
            {
                case GrassKind.Plume: min = 6; max = 14; break;
                case GrassKind.SeedHead: min = 1; max = 4; break;
                case GrassKind.FlowerSpike: min = 3; max = 7; break;
                case GrassKind.FlowerSpray: min = 2; max = 4; break;
                case GrassKind.Fern: min = 4; max = 12; break;
                case GrassKind.Foxtail: min = 10; max = 28; break;
                case GrassKind.Wildflower: case GrassKind.Bellflower: min = 2; max = 5; break;
                case GrassKind.LeafyHerb: min = 3; max = 9; break;
                case GrassKind.RoundGroundcover: min = 1; max = 10; break;
            }
        }
        public static int DensityValue(GrassKind kind, int raw)
        {
            int value = raw;
            switch (kind)
            {
                case GrassKind.SeedHead: value = raw / 6; break;
                case GrassKind.FlowerSpike: case GrassKind.LeafyHerb: value = raw / 2; break;
                case GrassKind.FlowerSpray: case GrassKind.Wildflower: case GrassKind.Bellflower: value = raw / 5; break;
                case GrassKind.Fern: value = Mathf.RoundToInt(Mathf.Sqrt(raw) * 2.05f); break;
                case GrassKind.Foxtail: value = raw + 6; break;
                case GrassKind.RoundGroundcover: value = (raw + 2) / 3; break;
            }
            DensityRange(kind, out int min, out int max);
            return Mathf.Clamp(value, min, max);
        }
        public static int DensityRaw(GrassKind kind, int value)
        {
            switch (kind)
            {
                case GrassKind.SeedHead: return value * 6;
                case GrassKind.FlowerSpike: case GrassKind.LeafyHerb: return value * 2;
                case GrassKind.FlowerSpray: case GrassKind.Wildflower: case GrassKind.Bellflower: return value * 5;
                case GrassKind.Fern: return Mathf.RoundToInt(Mathf.Pow(value / 2.05f, 2));
                case GrassKind.Foxtail: return value - 6;
                case GrassKind.RoundGroundcover: return value * 3;
                default: return value;
            }
        }
    }
}
