using System;
using UnityEngine;
using UnityEngine.UI;

namespace WeatherHousePrototype
{
    [Serializable]
    public sealed class ProceduralUiStyle
    {
        [HideInInspector] public int version = 1;
        [InspectorName("控件线条粗细倍数"), Range(.4f, 2.5f)] public float lineScale = 1f;
        [InspectorName("中文与提示文字大小倍数"), Range(.7f, 1.6f)] public float fontScale = 1f;
        [InspectorName("文字粗细（400 普通，700 粗体）")] public int fontWeight = 400;
        [InspectorName("气泡边缘起伏"), Range(0f, .05f)] public float waviness = .025f;
        [InspectorName("气泡圆润程度"), Range(.15f, .5f)] public float bubbleRadius = .47f;
        [InspectorName("动物气泡尖尾大小"), Range(0f, .24f)] public float tailSize = .13f;
        [InspectorName("取景框圆角"), Range(0f, 40f)] public float cornerRadius = 18f;
        [InspectorName("快门内圈大小"), Range(.22f, .38f)] public float shutterInnerRatio = .318f;
        [InspectorName("快门外圈粗细倍数"), Range(.5f, 2f)] public float shutterOuterLineScale = 1f;
        [InspectorName("快门内圈粗细倍数"), Range(.2f, 1f)] public float shutterInnerLineScale = .44f;
        [InspectorName("关闭圆圈缺口"), Range(10f, 80f)] public float closeGap = 35f;
        [InspectorName("线条文字颜色（HEX）")] public string ink = "#3b4a3d";
        [InspectorName("动物气泡颜色（HEX）")] public string cream = "#f7f5db";
        [InspectorName("人物气泡颜色（HEX）")] public string player = "#c7d4a6";
        private static ProceduralUiStyle current;
        public static ProceduralUiStyle Current => current ?? (current = ReadResource());
        public static void Use(ProceduralUiStyle style) { current = style ?? new ProceduralUiStyle(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reload() { current = null; }

        private static ProceduralUiStyle ReadResource()
        {
            var asset = Resources.Load<TextAsset>("WeatherHouseUiStyle");
            if (asset == null) return new ProceduralUiStyle();
            try { return Parse(asset.text); }
            catch (ArgumentException error)
            {
                Debug.LogError("UI 样式配置未采用：" + error.Message + "。请检查 WeatherHouseUiStyle.json，当前使用默认样式。");
                return new ProceduralUiStyle();
            }
        }

        public static ProceduralUiStyle Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("配置为空");
            var value = JsonUtility.FromJson<ProceduralUiStyle>(json);
            if (value == null || value.version != 1) throw new ArgumentException("配置版本需要为 1");
            Range(value.lineScale, .4f, 2.5f, "lineScale");
            Range(value.fontScale, .7f, 1.6f, "fontScale");
            if (value.fontWeight != 400 && value.fontWeight != 700) throw new ArgumentException("字体粗细只能为普通 400 或粗体 700");
            Range(value.waviness, 0f, .05f, "waviness");
            Range(value.bubbleRadius, .15f, .5f, "bubbleRadius");
            Range(value.tailSize, 0f, .24f, "tailSize");
            Range(value.cornerRadius, 0f, 40f, "cornerRadius");
            Range(value.shutterInnerRatio, .22f, .38f, "shutterInnerRatio");
            Range(value.shutterOuterLineScale, .5f, 2f, "shutterOuterLineScale");
            Range(value.shutterInnerLineScale, .2f, 1f, "shutterInnerLineScale");
            Range(value.closeGap, 10f, 80f, "closeGap");
            ParseColor(value.ink); ParseColor(value.cream); ParseColor(value.player);
            return value;
        }

        private static void Range(float value, float min, float max, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
                throw new ArgumentException(name + " 超出支持范围");
        }

        public static Color ParseColor(string value)
        {
            if (value == null || value.Length != 7 || value[0] != '#' || !ColorUtility.TryParseHtmlString(value, out Color color))
                throw new ArgumentException("颜色需要为 #RRGGBB");
            return color;
        }

        public void Apply(ProceduralUiGraphic graphic, float baseLineWidth)
        {
            graphic.lineWidth = baseLineWidth * lineScale;
            graphic.waviness = waviness; graphic.bubbleRadius = bubbleRadius; graphic.tailSize = tailSize;
            graphic.cornerRadius = cornerRadius; graphic.shutterInnerRatio = shutterInnerRatio;
            graphic.shutterOuterLineScale = shutterOuterLineScale; graphic.shutterInnerLineScale = shutterInnerLineScale;
            graphic.closeGap = closeGap;
            graphic.SetVerticesDirty();
        }

        public void ApplyText(Text text, int baseFontSize)
        {
            text.fontSize = Mathf.RoundToInt(baseFontSize * fontScale);
            text.fontStyle = fontWeight == 700 ? FontStyle.Bold : FontStyle.Normal;
        }
    }
}
