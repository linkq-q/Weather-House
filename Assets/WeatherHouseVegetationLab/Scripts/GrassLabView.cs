using UnityEngine;

namespace WeatherHouse.Vegetation
{
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(-10)]
    public class GrassLabView : MonoBehaviour
    {
        public GrassMeadow meadow;
        // Keep saved orbit settings when adding navigation to an existing scene.
        [HideInInspector] public Vector3 target = new Vector3(0, 1, 1);
        [HideInInspector] public float distance = 31, yaw = -30, pitch = 27;
        public bool showPanel = true;
        Font font;
        GUIStyle label, title, button;
        GrassLabCamera navigation;
        Vector2 scroll;
        int page, selectedSpecies;
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

        void OnGUI()
        {
            if (!showPanel || !meadow) return;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 14, wordWrap = true };
                title = new GUIStyle(label) { fontSize = 22, fontStyle = FontStyle.Bold };
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 14, wordWrap = true };
            }
            Rect rect = PanelRect;
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 14, rect.y + 12, rect.width - 28, rect.height - 24));
            GUILayout.Label(Navigation.libraryLayout ? "植物资源库 · 试验场" : "草地与水体 · 试验场", title);
            page = GUILayout.Toolbar(page, new[] { "比例", "形状", "颜色/风", "相机" }, button, GUILayout.Height(30));
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Space(8);
            if (meadow.ConfigurationError != null && page != 3) GUILayout.Label(meadow.ConfigurationError, label);
            else if (page == 0) DrawMeadow();
            else if (page == 1) DrawShape();
            else if (page == 2) DrawAppearance();
            else DrawCamera();
            GUILayout.EndScrollView();
            GUILayout.Space(6);
            if (!string.IsNullOrEmpty(meadow.LiveError)) GUILayout.Label("更新失败：" + meadow.LiveError, label);
            else GUILayout.Label(meadow.IsLiveUpdating ? "正在更新草地 · " + (meadow.LiveProgress * 100).ToString("F0") + "%" : "草地已同步", label);
            GUILayout.Label("拖动即更新，松开后自动完成整片草地。\nTab 隐藏面板 · 4 行走 · 5 飞行\nPlay 试调退出后恢复，永久保存请在 Inspector 中编辑。", label);
            if (Navigation.libraryLayout) GUILayout.Label("参数作用于右侧混合草地；左侧展示台保留已保存的资源样本。", label);
            GUILayout.EndArea();
        }
        void DrawMeadow()
        {
            GUILayout.Label(meadow.SpeciesCount + " 种植物 · 比例按草丛数量归一化。", label);
            meadow.totalClumps = IntSlider("总草丛数", meadow.totalClumps, 0, 40000, 100);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("自然混合", button)) meadow.SetMixPreset(0);
            if (GUILayout.Button("低矮草坪", button)) meadow.SetMixPreset(1);
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
                meadow.weights[i] = Slider(names[i] + " " + (meadow.weights[i] / sum * 100).ToString("F1") + "% · 当前 " + counts[i] + " 丛", meadow.weights[i], 0, 100, "F0");
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
            if (grass || s.kind == GrassKind.Daisy) s.tipWidthRatio = Slider("叶尖宽度占比", s.tipWidthRatio, 0, .4f, "F2");
            s.spread = Slider(grass ? "叶尖展开半径（米）" : "植株展开半径（米）", s.spread, .03f, .8f, "F2");
            s.bend = Slider("上部弯曲", s.bend, 0, 1.5f, "F2");
            if (grass)
            {
                s.bladeCount = IntSlider("每丛叶片数", s.bladeCount, 1, 40);
                s.rootSpread = Slider("根束展开比例", s.rootSpread, .025f, .4f, "F3");
                s.heightVariation = Slider("高度随机变化", s.heightVariation, 0, .7f, "F2");
                s.outerBladeFraction = Slider("外围展开叶片占比", s.outerBladeFraction, 0, 1, "F2");
                s.clumpLayering = Slider("草簇高低分层", s.clumpLayering, 0, 1, "F2");
                GUILayout.Label("叶宽让整片更厚；叶尖占比只调末端。根束与叶尖展开越小，草簇越聚拢。", label);
            }
            else
            {
                s.flowerCount = IntSlider("花朵/穗/主叶数量", s.flowerCount, 1, 7);
                s.flowerRadius = Slider("花朵/穗头半径（米）", s.flowerRadius, .025f, .22f, "F3");
                if (s.kind != GrassKind.Daisy)
                {
                    s.bladeCount = IntSlider("叶片/分枝密度", s.bladeCount, 1, 40);
                    s.rootSpread = Slider("根部展开比例", s.rootSpread, .025f, .4f, "F3");
                    s.heightVariation = Slider("高度随机变化", s.heightVariation, 0, .7f, "F2");
                    s.petalCount = IntSlider("花瓣/分枝数量", s.petalCount, 3, 12);
                    s.headLength = Slider("花序/叶段长度（米）", s.headLength, .05f, .8f, "F2");
                    s.leafAspect = Slider("叶片长宽比例", s.leafAspect, .1f, 2, "F2");
                    s.curl = Slider("叶片卷曲", s.curl, 0, 1, "F2");
                }
            }
        }
        void DrawAppearance()
        {
            GrassSpecies s = ChooseSpecies();
            Color root = ColorSliders("根部颜色", s.baseColor), tip = ColorSliders("尖部颜色", s.tipColor);
            if (root != s.baseColor || tip != s.tipColor) { s.baseColor = root; s.tipColor = tip; meadow.ApplyMaterials(); }
            if (!GrassMeadow.IsGrass(s.kind))
            {
                s.flowerColor = ColorSliders("花瓣/花序颜色", s.flowerColor);
                s.centerColor = ColorSliders("花心颜色", s.centerColor);
            }
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
        void OnDestroy() { if (font) Destroy(font); }
    }
}
