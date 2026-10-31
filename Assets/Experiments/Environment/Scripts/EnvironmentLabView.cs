using UnityEngine;
using WeatherHouse.Vegetation;

namespace WeatherHouse.EnvironmentLab
{
    public sealed class EnvironmentLabView : MonoBehaviour
    {
        public MossCoverage moss;
        public EnvironmentWind wind;
        public FogCard[] fogCards = new FogCard[0];
        public bool distanceFog = true;
        public Transform rotatingRock;
        public GrassLabCamera navigation;
        public bool showPanel = true;
        public string title = "第一批 · 朝上苔藓";
        public string description = "同一块石头，旋转后苔藓仍覆盖朝上的表面。";
        public Vector3 overviewTarget = new Vector3(0, 1, 0);
        public float overviewDistance = 19;
        Font font;
        GUIStyle text, heading;
        float angle;
        float fogOpacity = .22f, windHeading = 24;
        Rect panel { get { return new Rect(16, 16, 338, moss ? 445 : wind ? 440 : 430); } }
        void Start()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, 16);
            if (rotatingRock) angle = rotatingRock.eulerAngles.z;
            if (fogCards.Length > 0 && fogCards[0]) fogOpacity = fogCards[0].opacity;
            if (wind) windHeading = Mathf.Atan2(wind.direction.y, wind.direction.x) * Mathf.Rad2Deg;
            distanceFog = RenderSettings.fog;
            Overview();
        }
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab)) showPanel = !showPanel;
            if (Input.GetKeyDown(KeyCode.Home)) Overview();
            navigation.pointerOverPanel = showPanel && panel.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
        }
        public void Overview()
        {
            navigation.SetMode(GrassCameraMode.Orbit);
            navigation.target = overviewTarget; navigation.distance = overviewDistance;
            navigation.yaw = 0; navigation.pitch = 24; navigation.PlaceCamera();
        }
        void OnGUI()
        {
            if (!showPanel) return;
            if (text == null)
            {
                text = new GUIStyle(GUI.skin.label) { font = font, fontSize = 15, wordWrap = true };
                heading = new GUIStyle(text) { fontSize = 21, fontStyle = FontStyle.Bold };
            }
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 15, panel.y + 12, panel.width - 30, panel.height - 24));
            GUILayout.Label(title, heading);
            GUILayout.Label(description, text);
            if (moss)
            {
                moss.coverage = Slider("苔藓覆盖量", moss.coverage, 0, 1);
                moss.edgeSharpness = Slider("边缘清晰度", moss.edgeSharpness, .1f, 2);
                moss.textureScale = Slider("纹理重复次数", moss.textureScale, .25f, 8);
                angle = Slider("中央石块旋转", angle, 0, 360);
                moss.showMoss = GUILayout.Toggle(moss.showMoss, "显示苔藓（关闭看原石）");
                bool reset = GUILayout.Button("恢复初始参数");
                if (reset) { moss.coverage = .62f; moss.edgeSharpness = .7f; moss.textureScale = 2; angle = 90; moss.showMoss = true; }
                if (GUI.changed || reset)
                {
                    if (rotatingRock)
                    {
                        rotatingRock.localRotation = Quaternion.Euler(0, 0, angle);
                        var renderer = rotatingRock.GetComponentInChildren<Renderer>();
                        rotatingRock.position += Vector3.up * (.18f - renderer.bounds.min.y);
                    }
                    moss.Apply();
                }
            }
            if (wind)
            {
                wind.force = Slider("风力倍率", wind.force, 0, 3);
                wind.speed = Slider("风速", wind.speed, 0, 3);
                windHeading = Slider("风向角度", windHeading, -180, 180);
                if (GUILayout.Button("静风")) wind.force = 0;
                if (GUILayout.Button("微风")) { wind.force = 1; wind.speed = 1; }
                if (GUILayout.Button("强风")) { wind.force = 2.5f; wind.speed = 1.7f; }
                if (GUI.changed) { float a = windHeading * Mathf.Deg2Rad; wind.direction = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); wind.Apply(); }
            }
            if (fogCards.Length > 0)
            {
                distanceFog = GUILayout.Toggle(distanceFog, "启用基础距离雾");
                RenderSettings.fog = distanceFog;
                fogOpacity = Slider("远景雾片透明度", fogOpacity, 0, .65f);
                if (GUILayout.Button("只看距离雾")) SetFogOpacity(0);
                if (GUILayout.Button("恢复远景雾片")) SetFogOpacity(.18f);
                if (GUI.changed) SetFogOpacity(fogOpacity);
                GUILayout.Label("雾片用于远处层次，靠近后渐隐。透明水面仍需另行交叠验收。", text);
            }
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("全景 Home")) Overview();
            if (GUILayout.Button("行走 4")) navigation.SetMode(GrassCameraMode.Walk);
            if (GUILayout.Button("飞行 5")) navigation.SetMode(GrassCameraMode.Fly);
            GUILayout.EndHorizontal();
            GUILayout.Label("右键看向 · WASD 移动 · Shift 加速\n飞行 E/Q 升降 · Tab 隐藏面板", text);
            GUILayout.Label("试玩调整在退出后恢复。\n永久参数请在退出后填写并保存。", text);
            GUILayout.EndArea();
        }
        float Slider(string label, float value, float min, float max)
        {
            GUILayout.Label(label + "  " + value.ToString("0.00"), text);
            return GUILayout.HorizontalSlider(value, min, max);
        }
        public void SetFogOpacity(float value)
        {
            fogOpacity = Mathf.Clamp(value, 0, .65f);
            foreach (var card in fogCards)
            {
                if (!card) continue;
                card.opacity = fogOpacity;
                card.ApplySettings();
            }
        }
        void OnDestroy() { if (font) Destroy(font); }
    }
}
