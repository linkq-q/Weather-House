using UnityEngine;

namespace WeatherHouse.Vegetation
{
    public enum GrassKind
    {
        ShortGrass = 0, TallGrass = 1, Daisy = 2,
        Plume = 3, SeedHead = 4, FlowerSpike = 5, FlowerSpray = 6, Fern = 7,
        Foxtail = 8, Wildflower = 9, Bellflower = 10, LeafyHerb = 11, RoundGroundcover = 12
    }

    [CreateAssetMenu(menuName = "Weather House/Grass Species", fileName = "Grass Species")]
    public sealed class GrassSpecies : ScriptableObject
    {
        public GrassKind kind;
        [Tooltip("资源库和参数面板中显示的中文形态名；改名不改变几何。")]
        public string displayName;
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
        [Tooltip("短草、高草中向外展开的叶片比例。0 全部直立聚拢，1 全部外展；0.25 保持原有草丛分布。")]
        [Range(0f, 1f)] public float outerBladeFraction = 0.25f;
        [Tooltip("短草、高草的内高外低层次。0 保持原有形状，1 缩短外叶并进一步收拢内叶；与外展叶比例共同塑造丛形。")]
        [Range(0f, 1f)] public float clumpLayering = 0f;
        public int seed = 137;

        [Header("草根与草尖颜色")]
        public Color baseColor = new Color(0.34f, 0.44f, 0.11f);
        public Color tipColor = new Color(0.83f, 0.86f, 0.31f);

        [Header("花朵与花穗")]
        [Range(1, 7)] public int flowerCount = 3;
        [Range(0.025f, 0.22f)] public float flowerRadius = 0.09f;
        public Color flowerColor = new Color(0.98f, 0.97f, 0.85f);
        public Color centerColor = new Color(0.94f, 0.65f, 0.14f);
        [Tooltip("新增花卉的单朵花瓣数；原雏菊保留原来的 9–12 瓣随机形状。")]
        [Range(3, 12)] public int petalCount = 5;
        [Tooltip("新增穗状植物的花穗长度，单位米。")]
        [Range(0.05f, 0.8f)] public float headLength = 0.25f;
        [Tooltip("新增植物的叶片长宽比倍率；1 为该形态的基准比例。")]
        [Range(0.1f, 2f)] public float leafAspect = 1f;
        [Tooltip("新增植物的叶片或花瓣弯卷程度；0 平展，1 更明显地弯卷。")]
        [Range(0f, 1f)] public float curl = 0.2f;

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
            outerBladeFraction = Mathf.Clamp01(outerBladeFraction);
            clumpLayering = Mathf.Clamp01(clumpLayering);
            petalCount = Mathf.Clamp(petalCount, 3, 12);
            headLength = Mathf.Clamp(headLength, 0.05f, 0.8f);
            leafAspect = Mathf.Clamp(leafAspect, 0.1f, 2f);
            curl = Mathf.Clamp01(curl);
        }
    }
}
