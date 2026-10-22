using UnityEngine;

namespace WeatherHouse.MaterialLab
{
    public sealed class MaterialLabControls : MonoBehaviour
    {
        public Camera viewCamera;
        public OilPaintPostSettings post;
        public Renderer[] paintedSamples;
        public Light keyLight;
        public bool showPanel = true;
        public bool rotateSamples;
        public int selected;
        [Range(0, 4)] public int selectedColor = 1;
        public float yaw, pitch = 20, distance = 17;
        public Vector3 target = new Vector3(0, 1.3f, 1.4f);
        Material[] originals, runtimeMaterials;
        Vector2 scroll;
        GUIStyle heading, small;
        bool colorsOpen = true, shapeOpen = true, lightingOpen, postOpen;
        Material colorMaterial;
        int cachedColor = -1;
        Color shownColor;
        float colorHue, colorSaturation, colorValue;
        static readonly string[] Names = { "奶油 · 柔和色块", "奶油 · 定向笔触", "奶油 · 厚涂", "青绿 · 柔和色块", "青绿 · 定向笔触", "青绿 · 厚涂" };
        static readonly string[] ColorNames = { "底色", "笔触", "阴影", "亮部", "交界色" };
        static readonly string[] ColorProperties = { "_BaseColor", "_StrokeColor", "_ShadowColor", "_HighlightColor", "_ShadowEdgeColor" };

        public Material SelectedMaterial => runtimeMaterials != null && runtimeMaterials.Length > 0 ? runtimeMaterials[Mathf.Clamp(selected, 0, runtimeMaterials.Length - 1)] : null;

        void Awake()
        {
            originals = new Material[paintedSamples.Length];
            runtimeMaterials = new Material[paintedSamples.Length];
            for (int i = 0; i < paintedSamples.Length; i++)
            {
                originals[i] = paintedSamples[i].sharedMaterial;
                runtimeMaterials[i] = new Material(originals[i]) { name = originals[i].name + " (Play)" };
                paintedSamples[i].sharedMaterial = runtimeMaterials[i];
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.H)) showPanel = !showPanel;
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetPostMode(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetPostMode(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetPostMode(2);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SetPostMode(3);
            bool overPanel = showPanel && Input.mousePosition.x > Screen.width - 346;
            if (!overPanel)
            {
                if (Input.GetMouseButton(1))
                {
                    yaw += Input.GetAxis("Mouse X") * 2;
                    pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 2, 4, 75);
                }
                distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y, 2.8f, 28);
            }
            if (rotateSamples)
                foreach (var sample in paintedSamples) sample.transform.Rotate(Vector3.up, 15 * Time.deltaTime, Space.Self);
            PlaceCamera();
        }

        public void PlaceCamera()
        {
            if (!viewCamera) return;
            viewCamera.transform.position = target + Quaternion.Euler(pitch, yaw, 0) * Vector3.back * distance;
            viewCamera.transform.LookAt(target);
        }

        public void SetPostMode(int mode)
        {
            post.oilEnabled = mode != 0;
            post.pixelEnabled = mode >= 2;
            post.splitScreen = mode == 3;
        }

        public void ResetSelected()
        {
            if (SelectedMaterial) SelectedMaterial.CopyPropertiesFromMaterial(originals[selected]);
            colorMaterial = null;
        }

        public void ApplyWideBrushPreset()
        {
            Material material = SelectedMaterial;
            if (!Application.isPlaying || !material) return;
            material.SetFloat("_BrushScale", 1 / .32f);
            material.SetFloat("_StrokeDensity", 1.25f);
            material.SetFloat("_StrokeAspect", .45f);
            material.SetFloat("_BrushStrength", .72f);
        }

        public void ApplyLargeDensePreset()
        {
            Material material = SelectedMaterial;
            if (!Application.isPlaying || !material) return;
            material.SetFloat("_BrushScale", 1 / .58f);
            material.SetFloat("_StrokeDensity", 1.8f);
            material.SetFloat("_StrokeAspect", .7f);
            material.SetFloat("_BrushStrength", .78f);
        }

        public void SetSelectedColorHSV(float hue, float saturation, float value)
        {
            Material material = SelectedMaterial;
            if (!Application.isPlaying || !material) return;
            selectedColor = Mathf.Clamp(selectedColor, 0, ColorProperties.Length - 1);
            string property = ColorProperties[selectedColor];
            if (!material.HasProperty(property)) return;
            colorHue = Mathf.Clamp01(hue); colorSaturation = Mathf.Clamp01(saturation); colorValue = Mathf.Clamp01(value);
            Color color = Color.HSVToRGB(colorHue, colorSaturation, colorValue);
            color.a = material.GetColor(property).a;
            material.SetColor(property, color);
            colorMaterial = material; cachedColor = selectedColor; shownColor = color;
        }

        public void FocusSelected()
        {
            target = paintedSamples[selected].bounds.center;
            distance = 4.8f; pitch = 12; yaw = 16;
            PlaceCamera();
        }
        public void Overview()
        {
            target = new Vector3(0, 1.3f, 1.4f); distance = 18.5f; pitch = 27; yaw = 0;
            PlaceCamera();
        }

        void OnGUI()
        {
            if (heading == null)
            {
                heading = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = true };
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            }
            GUI.Label(new Rect(15, 12, 640, 27), "油画材质实验  |  1 原图  2 油画  3 油画＋微像素  4 左右对比  H 面板", small);
            if (post.splitScreen)
            {
                GUI.Label(new Rect(16, 43, 200, 22), "左侧：原图", small);
                GUI.Label(new Rect(Screen.width * post.splitPosition + 12, 43, 220, 22), "右侧：油画＋微像素", small);
            }
            if (!showPanel) return;
            float width = Mathf.Min(328, Screen.width - 20);
            GUILayout.BeginArea(new Rect(Screen.width - width - 10, 72, width, Screen.height - 88), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("油画材质与笔触", heading);
            GUILayout.Label("右键环绕 · 滚轮远近\n参数在 Play 中即时生效，退出恢复。", small);
            selected = GUILayout.SelectionGrid(selected, Names, 2);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("近看当前材质")) FocusSelected();
            if (GUILayout.Button("返回总览")) Overview();
            GUILayout.EndHorizontal();
            Material material = SelectedMaterial;
            if (material)
            {
                colorsOpen = Foldout("颜色 · 选一块实时调色", colorsOpen);
                if (colorsOpen) DrawColors(material);
                shapeOpen = Foldout("笔触形态", shapeOpen);
                if (shapeOpen)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("宽笔触")) ApplyWideBrushPreset();
                    if (GUILayout.Button("大且密")) ApplyLargeDensePreset();
                    GUILayout.EndHorizontal();
                    float size = 1 / Mathf.Max(.5f, material.GetFloat("_BrushScale"));
                    float newSize = Slider("笔触尺寸（物体空间）", size, 1f / 24, 2);
                    if (newSize != size) material.SetFloat("_BrushScale", 1 / newSize);
                    Property(material, "笔触密度（0 = 无笔触）", "_StrokeDensity", 0, 2);
                    Property(material, "笔触强度", "_BrushStrength", 0, 1);
                    Property(material, "长宽比（小于 1 为宽笔触）", "_StrokeAspect", .25f, 8);
                    Property(material, "笔触方向", "_StrokeAngle", -180, 180);
                    Property(material, "方向散差", "_StrokeScatter", 0, 90);
                }
                lightingOpen = Foldout("明暗过渡与厚涂", lightingOpen);
                if (lightingOpen)
                {
                    Property(material, "阴影填充", "_ShadowFill", 0, 1);
                    Property(material, "明暗交界宽度", "_ShadowEdgeWidth", .02f, .4f);
                    Property(material, "明暗交界着色", "_ShadowEdgeStrength", 0, 1);
                    Property(material, "色阶数量", "_ColorSteps", 2, 12);
                    Property(material, "厚涂起伏", "_Impasto", 0, 2);
                    Property(material, "表面光滑度", "_Smoothness", 0, 1);
                    if (keyLight) keyLight.intensity = Slider("日光强度", keyLight.intensity, .4f, 1.8f);
                }
                if (GUILayout.Button("恢复当前材质预设")) ResetSelected();
            }
            postOpen = Foldout("后处理 · 油画与微像素", postOpen);
            if (postOpen)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("原图")) SetPostMode(0);
                if (GUILayout.Button("油画")) SetPostMode(1);
                if (GUILayout.Button("组合")) SetPostMode(2);
                if (GUILayout.Button("对比")) SetPostMode(3);
                GUILayout.EndHorizontal();
                post.oilEnabled = GUILayout.Toggle(post.oilEnabled, "启用油画后处理");
                post.oilStrength = Slider("油画融合", post.oilStrength, 0, 1);
                post.radius = Slider("笔触半径（渲染像素）", post.radius, 1, 6);
                post.brushAspect = Slider("后处理笔触长宽比", post.brushAspect, 1, 3);
                post.brushAngle = Slider("后处理笔触方向", post.brushAngle, -180, 180);
                post.colorSteps = Mathf.RoundToInt(Slider("后处理色阶（0关闭）", post.colorSteps, 0, 32));
                post.pixelEnabled = GUILayout.Toggle(post.pixelEnabled, "启用微像素化");
                post.pixelSize = Slider("像素块（渲染像素）", post.pixelSize, 1, 5);
                post.pixelStrength = Slider("像素融合", post.pixelStrength, 0, 1);
                post.splitScreen = GUILayout.Toggle(post.splitScreen, "左右原图对比");
                if (post.splitScreen) post.splitPosition = Slider("分界位置", post.splitPosition, .1f, .9f);
            }
            rotateSamples = GUILayout.Toggle(rotateSamples, "缓慢旋转材质球");
            GUILayout.Label("每列上方为奶油色，下方为青绿。最左列是常规 Lit 对照。\n永久编辑请退出 Play 后修改 Generated/Materials。", small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawColors(Material material)
        {
            selectedColor = Mathf.Clamp(selectedColor, 0, ColorProperties.Length - 1);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < ColorProperties.Length; i++)
            {
                GUILayout.BeginVertical();
                Swatch(material.GetColor(ColorProperties[i]), 12);
                if (GUILayout.Toggle(selectedColor == i, ColorNames[i], "Button")) selectedColor = i;
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
            Color actual = material.GetColor(ColorProperties[selectedColor]);
            if (colorMaterial != material || cachedColor != selectedColor || actual != shownColor)
            {
                Color.RGBToHSV(actual, out colorHue, out colorSaturation, out colorValue);
                colorMaterial = material; cachedColor = selectedColor; shownColor = actual;
            }
            Swatch(actual, 22);
            float hue = Slider("色相", colorHue * 360, 0, 360) / 360;
            float saturation = Slider("饱和度", colorSaturation, 0, 1);
            float value = Slider("明度", colorValue, 0, 1);
            if (hue != colorHue || saturation != colorSaturation || value != colorValue)
                SetSelectedColorHSV(hue, saturation, value);
        }

        static void Swatch(Color color, float height)
        {
            Rect area = GUILayoutUtility.GetRect(10, height, GUILayout.ExpandWidth(true));
            Color previous = GUI.color; GUI.color = color;
            GUI.DrawTexture(area, Texture2D.whiteTexture); GUI.color = previous;
        }
        static bool Foldout(string label, bool open)
        {
            GUILayout.Space(5);
            return GUILayout.Button((open ? "▼ " : "▶ ") + label) ? !open : open;
        }

        static float Slider(string label, float value, float min, float max)
        {
            GUILayout.Label(label + "  " + value.ToString("0.00"));
            return GUILayout.HorizontalSlider(value, min, max);
        }
        static void Property(Material material, string label, string property, float min, float max)
        {
            if (material.HasProperty(property)) material.SetFloat(property, Slider(label, material.GetFloat(property), min, max));
        }
        void OnDestroy()
        {
            if (runtimeMaterials == null) return;
            for (int i = 0; i < runtimeMaterials.Length; i++)
            {
                if (paintedSamples[i]) paintedSamples[i].sharedMaterial = originals[i];
                if (runtimeMaterials[i]) Destroy(runtimeMaterials[i]);
            }
        }
    }
}
