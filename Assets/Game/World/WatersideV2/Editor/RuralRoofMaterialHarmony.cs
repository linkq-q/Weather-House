using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.WatersideV2.Editor
{
    // Keep importer rebuilds consistent with the current weather-house roof palette.
    public static class RuralRoofMaterialHarmony
    {
        const string Reference = "Assets/Environment/WeatherHouseV05/Materials/";
        static int Kind(string folder, string semantic)
        {
            string id = Path.GetFileName(folder);
            if (id.Length != 3 || id[0] != 'A' || id[1] != '0' || id[2] < '1' || id[2] > '9' || semantic == null) return 0;
            if (semantic.StartsWith("灰绿旧瓦_", StringComparison.Ordinal)) return 1;
            if (semantic.StartsWith("苔草主材质_", StringComparison.Ordinal)) return 2;
            if (semantic == "WeatherCraft_moss_light_SubpixelPBR") return 3;
            return semantic == "WeatherCraft_moss_dark_SubpixelPBR" ? 4 : 0;
        }
        static Color Tint(Color value, Color tint) => new Color(value.r * tint.r, value.g * tint.g, value.b * tint.b, value.a);
        static Color Variant(Color value, float amount) => new Color(value.r * amount, value.g * amount, value.b * amount, value.a);
        // Existing RoofTiles packed alpha is 23/255 on the tile field; the source atlas is never assigned to different UVs.
        static float Smoothness(float multiplier) => multiplier * (23f / 255f);
        public static void Apply(string folder, string semantic, Material material)
        {
            int kind = Kind(folder, semantic);
            if (kind == 0) return;
            Material source = AssetDatabase.LoadAssetAtPath<Material>(Reference + (kind == 1 ? "WH05_RoofTiles.mat" : "WH05_Moss.mat"));
            if (!source) throw new InvalidOperationException("Current weather-house roof reference is missing.");
            if (kind == 1)
            {
                Color color = Tint(material.GetColor("_BaseColor"), source.GetColor("_BaseColor"));
                material.SetColor("_BaseColor", color); material.SetColor("_Color", color);
                material.SetFloat("_Smoothness", Smoothness(source.GetFloat("_Smoothness")));
                return;
            }
            float cull = material.GetFloat("_Cull");
            material.shader = source.shader;
            if (!material.HasProperty("_Cull")) throw new InvalidOperationException("The shared painterly shader must preserve the original two-sided roof material.");
            material.shaderKeywords = Array.Empty<string>();
            material.SetFloat("_Cull", cull);
            float variant = kind == 3 ? 1.04f : kind == 4 ? .96f : 1f;
            foreach (string property in new[] { "_BaseColor", "_PatchColor", "_ShoreColor" })
                material.SetColor(property, Variant(source.GetColor(property), variant));
            material.SetColor("_Color", material.GetColor("_BaseColor"));
            material.SetColor("_ShadowTint", source.GetColor("_ShadowTint"));
            foreach (string property in new[] { "_WaterLevel", "_PatchScale", "_BoundaryHardness", "_BoundaryBreakup", "_BoundarySize" })
                material.SetFloat(property, source.GetFloat(property));
        }
    }
}
