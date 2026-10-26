using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.Life.Editor
{
    public static class LifeAudioSurfaceAudit
    {
        public static string Inspect()
        {
            var lines = new System.Collections.Generic.List<string>();
            foreach (Terrain terrain in UnityEngine.Object.FindObjectsOfType<Terrain>())
            {
                lines.Add("TERRAIN\t" + AssetDatabase.GetAssetPath(terrain.terrainData));
                foreach (TerrainLayer layer in terrain.terrainData.terrainLayers)
                    lines.Add("LAYER\t" + AssetDatabase.GetAssetPath(layer));
            }
            foreach (Collider collider in UnityEngine.Object.FindObjectsOfType<Collider>())
            {
                if (collider.isTrigger || collider is TerrainCollider || collider is CharacterController) continue;
                Renderer renderer = collider.GetComponent<Renderer>() ?? collider.GetComponentInParent<Renderer>();
                if (!renderer) continue;
                foreach (Material material in renderer.sharedMaterials.Where(m => m))
                    lines.Add("COLLIDER\t" + collider.GetType().Name + "\t" + collider.name + "\t" + AssetDatabase.GetAssetPath(material));
            }
            string path = "Temp/LifeAudioStaging/surface-audit.tsv";
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllLines(path, lines.Distinct());
            return "PASS read-only audio surface inventory: " + path;
        }
    }
}
