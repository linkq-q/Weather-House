using UnityEngine;

namespace WeatherHouse.Vegetation
{
    public enum GrassKind { ShortGrass, TallGrass, Daisy }

    [CreateAssetMenu(menuName = "Weather House/Grass Species", fileName = "Grass Species")]
    public sealed class GrassSpecies : ScriptableObject
    {
        public GrassKind kind;
        [Header("草丛形状（米）")]
        [Min(0.03f)] public float height = 0.38f;
        [Min(0.005f)] public float width = 0.018f;
        [Tooltip("尖帽完整宽度占叶片宽度的比例。0 为尖头，0.11 为轻微钝尖；上限 0.4。接近 0（≤0.001）按尖头生成，避免极小三角形。")]
        [Range(0f, 0.4f)] public float tipWidthRatio = 0.11f;
        [Range(1, 40)] public int bladeCount = 14;
        [Tooltip("草叶顶部的展开半径；根部聚拢为窄束。雏菊则控制花茎根部的范围。")]
        [Min(0.01f)] public float spread = 0.12f;
        [Tooltip("短草、高草根束半径占 Spread 的最大比例。0.025 最聚拢，0.12 为默认，0.4 更松散；Spread 继续控制草叶扇开。雏菊不使用此参数。")]
        [Range(0.025f, 0.4f)] public float rootSpread = 0.12f;
        [Tooltip("草叶向外弯曲的程度；0 为直立斜线，较大值让上半部逐渐展开。")]
        [Range(0f, 1.5f)] public float bend = 0.3f;
        [Range(0f, 0.7f)] public float heightVariation = 0.22f;
        public int seed = 137;

        [Header("草根与草尖颜色")]
        public Color baseColor = new Color(0.34f, 0.44f, 0.11f);
        public Color tipColor = new Color(0.83f, 0.86f, 0.31f);

        [Header("雏菊（仅 Daisy 使用）")]
        [Range(1, 7)] public int flowerCount = 3;
        [Range(0.025f, 0.22f)] public float flowerRadius = 0.09f;
        public Color flowerColor = new Color(0.98f, 0.97f, 0.85f);
        public Color centerColor = new Color(0.94f, 0.65f, 0.14f);

        private void OnValidate()
        {
            height = Mathf.Max(0.03f, height);
            width = Mathf.Max(0.005f, width);
            tipWidthRatio = Mathf.Clamp(tipWidthRatio, 0f, 0.4f);
            spread = Mathf.Max(0.01f, spread);
            rootSpread = Mathf.Clamp(rootSpread, 0.025f, 0.4f);
            bladeCount = Mathf.Clamp(bladeCount, 1, 40);
            flowerCount = Mathf.Clamp(flowerCount, 1, 7);
            flowerRadius = Mathf.Clamp(flowerRadius, 0.025f, 0.22f);
            bend = Mathf.Clamp(bend, 0f, 1.5f);
            heightVariation = Mathf.Clamp(heightVariation, 0f, 0.7f);
        }
    }
}
