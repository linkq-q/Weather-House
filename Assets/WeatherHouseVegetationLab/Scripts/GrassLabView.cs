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
        readonly string[] names = { "短草", "高草", "白色雏菊" };
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
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 14 };
            }
            Rect rect = PanelRect;
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 14, rect.y + 12, rect.width - 28, rect.height - 24));
            GUILayout.Label("草地与水体 · 试验场", title);
            page = GUILayout.Toolbar(page, new[] { "草地", "草形", "颜色/风", "相机" }, button, GUILayout.Height(30));
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Space(8);
            if (page == 0) DrawMeadow();
            else if (page == 1) DrawShape();
            else if (page == 2) DrawAppearance();
            else DrawCamera();
            GUILayout.EndScrollView();
            GUILayout.Space(6);
            if (!string.IsNullOrEmpty(meadow.LiveError)) GUILayout.Label("更新失败：" + meadow.LiveError, label);
            else GUILayout.Label(meadow.IsLiveUpdating ? "正在更新草地 · " + (meadow.LiveProgress * 100).ToString("F0") + "%" : "草地已同步", label);
            GUILayout.Label("拖动即更新，松开后自动完成整片草地。\nTab 隐藏面板 · 4 行走 · 5 飞行\nPlay 试调退出后恢复，永久保存请在 Inspector 中编辑。", label);
            GUILayout.EndArea();
        }
        void DrawMeadow()
        {
            GUILayout.Label("比例按草丛数量归一化。", label);
            float sum = Mathf.Max(.001f, meadow.weights[0] + meadow.weights[1] + meadow.weights[2]);
            int[] counts = meadow.Counts;
            for (int i = 0; i < 3; i++)
                meadow.weights[i] = Slider(names[i] + " " + (meadow.weights[i] / sum * 100).ToString("F1") + "% · 当前 " + counts[i] + " 丛", meadow.weights[i], 0, 100, "F0");
            meadow.totalClumps = IntSlider("总草丛数", meadow.totalClumps, 0, 40000, 100);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("自然混合", button)) Preset(78, 20, 2);
            if (GUILayout.Button("低矮草坪", button)) Preset(94, 4, 2);
            if (GUILayout.Button("花草地", button)) Preset(52, 30, 18);
            GUILayout.EndHorizontal();
            GUILayout.Label("分布种子 " + meadow.seed, label);
            if (GUILayout.Button("换一组草地位置", button)) meadow.seed = unchecked(meadow.seed + 1);
        }
        GrassSpecies ChooseSpecies()
        {
            selectedSpecies = GUILayout.Toolbar(selectedSpecies, names, button, GUILayout.Height(28));
            return meadow.species[selectedSpecies];
        }
        void DrawShape()
        {
            GrassSpecies s = ChooseSpecies();
            s.height = Slider("高度（米）", s.height, .08f, 1.4f, "F2");
            s.width = Slider(s.kind == GrassKind.Daisy ? "花茎/侧叶尺度（米）" : "叶片宽度（米）", s.width, .005f, .12f, "F3");
            s.tipWidthRatio = Slider("叶尖宽度占比", s.tipWidthRatio, 0, .4f, "F2");
            s.spread = Slider(s.kind == GrassKind.Daisy ? "花茎分布半径（米）" : "叶尖展开半径（米）", s.spread, .03f, .55f, "F2");
            s.bend = Slider("上部弯曲", s.bend, 0, 1.5f, "F2");
            if (s.kind != GrassKind.Daisy)
            {
                s.bladeCount = IntSlider("每丛叶片数", s.bladeCount, 1, 40);
                s.rootSpread = Slider("根束展开比例", s.rootSpread, .025f, .4f, "F3");
                s.heightVariation = Slider("高度随机变化", s.heightVariation, 0, .7f, "F2");
                GUILayout.Label("叶宽让整片更厚；叶尖占比只调末端。根束与叶尖展开越小，草簇越聚拢。", label);
            }
            else
            {
                s.flowerCount = IntSlider("每丛花朵数", s.flowerCount, 1, 7);
                s.flowerRadius = Slider("花朵半径（米）", s.flowerRadius, .025f, .22f, "F3");
            }
        }
        void DrawAppearance()
        {
            GrassSpecies s = ChooseSpecies();
            Color root = ColorSliders("根部颜色", s.baseColor), tip = ColorSliders("尖部颜色", s.tipColor);
            if (root != s.baseColor || tip != s.tipColor) { s.baseColor = root; s.tipColor = tip; meadow.ApplyMaterials(); }
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
            if (GUILayout.Button("2 草近景", button)) CloseGrass();
            if (GUILayout.Button("3 水岸", button)) Shore();
            GUILayout.EndHorizontal();
            nav.moveSpeed = Slider("行走速度（米/秒）", nav.moveSpeed, .5f, 12, "F1");
            nav.flySpeed = Slider("飞行速度（米/秒）", nav.flySpeed, 1, 25, "F1");
            nav.sprintMultiplier = Slider("Shift 加速倍率", nav.sprintMultiplier, 1, 5, "F1");
            nav.lookSensitivity = Slider("鼠标灵敏度", nav.lookSensitivity, .2f, 5, "F1");
            nav.eyeHeight = Slider("行走视点高度（米）", nav.eyeHeight, .35f, 2.2f, "F2");
            Camera camera = GetComponent<Camera>();
            camera.fieldOfView = Slider("视野角度 FOV", camera.fieldOfView, 35, 85, "F0");
            GUILayout.Label("WASD 移动 · 按住右键看向\nShift 加速 · 松开右键或 Esc 释放鼠标\n飞行：E / Space 上升，Q / Ctrl 下降\n行走跟随地面并停在岸边；这是浏览相机，树木和石头不阻挡。", label);
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
        void Preset(float low, float high, float flowers)
        {
            meadow.weights = new[] { low, high, flowers };
        }
        void OnDestroy() { if (font) Destroy(font); }
    }
}
