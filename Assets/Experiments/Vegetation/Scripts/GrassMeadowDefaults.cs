using System;
using System.Linq;
using UnityEngine;

namespace WeatherHouse.Vegetation
{
    [CreateAssetMenu(menuName = "Weather House/Grass Meadow Defaults")]
    public sealed class GrassMeadowDefaults : ScriptableObject
    {
        public int totalClumps, seed;
        public float[] weights;
        public float windStrength, windSpeed, upNormals;
        [HideInInspector] public int[] kinds;
        [HideInInspector] public string[] plants;

        public void Capture(GrassMeadow meadow)
        {
            if (meadow.ConfigurationError != null) throw new InvalidOperationException(meadow.ConfigurationError);
            totalClumps = meadow.totalClumps; seed = meadow.seed;
            weights = (float[])meadow.weights.Clone();
            windStrength = meadow.windStrength; windSpeed = meadow.windSpeed; upNormals = meadow.upNormals;
            kinds = meadow.species.Select(s => (int)s.kind).ToArray();
            plants = meadow.species.Select(s => JsonUtility.ToJson(s)).ToArray();
        }
        public void Apply(GrassMeadow meadow)
        {
            if (plants == null || kinds == null || weights == null || plants.Length != meadow.SpeciesCount
                || kinds.Length != plants.Length || weights.Length != plants.Length
                || meadow.species.Where((s, i) => !s || (int)s.kind != kinds[i]).Any())
                throw new InvalidOperationException("默认配置与当前草种槽位不一致，请在编辑模式重新保存该区域的默认参数。");
            meadow.totalClumps = totalClumps; meadow.seed = seed;
            meadow.weights = (float[])weights.Clone();
            meadow.windStrength = windStrength; meadow.windSpeed = windSpeed; meadow.upNormals = upNormals;
            for (int i = 0; i < plants.Length; i++) JsonUtility.FromJsonOverwrite(plants[i], meadow.species[i]);
            meadow.ApplyMaterials();
        }
    }
}
