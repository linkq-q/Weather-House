using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WatersideSoilChecks
    {
        public static string Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run the isolated soil field checks in Edit mode.");
            Shader shader = Shader.Find("Weather House/Waterside Soil Terrain");
            Require(shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Soil Terrain shader is missing, unsupported, or has compile errors.");
            Scene original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
            Scene preview = EditorSceneManager.NewPreviewScene();
            TerrainData data = null; GameObject holder = null;
            var layers = new TerrainLayer[4];
            try
            {
                data = new TerrainData { heightmapResolution = 33, alphamapResolution = 32, size = new Vector3(180, 52, 180) };
                var heights = new float[33, 33];
                for (int z = 0; z < 33; z++) for (int x = 0; x < 33; x++) heights[z, x] = 8f / 52;
                data.SetHeights(0, 0, heights);
                for (int i = 0; i < 4; i++) layers[i] = new TerrainLayer();
                data.terrainLayers = layers;
                var alpha = new float[32, 32, 4];
                for (int z = 0; z < 32; z++) for (int x = 0; x < 32; x++) alpha[z, x, x < 16 ? 1 : 0] = 1;
                data.SetAlphamaps(0, 0, alpha);
                holder = new GameObject("Temporary soil checks") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(holder, preview);
                holder.transform.position = new Vector3(-90, -8, -90);
                Terrain terrain = holder.AddComponent<Terrain>(); terrain.terrainData = data; terrain.drawHeightmap = false;
                var soil = holder.AddComponent<WatersideSoilInteraction>(); soil.terrain = terrain;
                Vector3 point = WatersideLayout.Entry; point.y = 0;
                for (int i = 0; i < 12; i++) soil.Advance(point, 0, true, .1f);
                Require(soil.StampCount == 0 && Peak(soil) == 0, "Stationary feet created soil marks.");
                for (int i = 0; i < 14; i++) { point.z += .12f; soil.Advance(point, 0, true, .1f); }
                Require(soil.StampCount >= 3 && Peak(soil) > .3f, "Walking on actual soil produced no visible impression field.");
                int walkingStamps = soil.StampCount;
                var block = new MaterialPropertyBlock(); terrain.GetSplatMaterialPropertyBlock(block);
                Require(block.GetTexture("_SoilFootprints") == soil.CurrentTexture && block.GetFloat("_SoilInteractionStrength") == 1,
                    "The live field was not bound to Terrain's property block.");
                for (int i = 0; i < 10; i++) soil.Advance(point, 0, true, 1);
                Require(Peak(soil) == 0 && soil.StampCount == walkingStamps, "Idle marks did not fade completely, or idle added marks.");

                point = WatersideLayout.Design(74, 40); soil.Advance(point, 0, true, .1f);
                Require(soil.TeleportSkipCount == 1, "Teleport was not rejected.");
                for (int i = 0; i < 12; i++) { point.z += .12f; soil.Advance(point, 0, true, .1f); }
                Require(soil.StampCount == walkingStamps && Peak(soil) == 0, "Walking on the meadow layer stamped soil.");
                point = WatersideLayout.Entry; soil.Advance(point, 0, false, .1f);
                for (int i = 0; i < 12; i++) { point.z += .12f; soil.Advance(point, 0, false, .1f); }
                Require(soil.StampCount == walkingStamps, "Airborne feet stamped soil.");
                for (int i = 0; i < 12; i++) { point.z += .12f; soil.Advance(point, 1, true, .1f); }
                Require(soil.StampCount == walkingStamps, "Feet on a raised structure stamped the terrain below.");
                point = WatersideLayout.WaterChannels[0].Nodes[2].Position; soil.Advance(point, 0, true, .1f);
                for (int i = 0; i < 12; i++) { point.z += .10f; soil.Advance(point, 0, true, .1f); }
                Require(soil.StampCount == walkingStamps && Peak(soil) == 0, "Water footprint accepted soil marks.");

                Texture2D field = soil.CurrentTexture;
                typeof(WatersideSoilInteraction).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(soil, null);
                terrain.GetSplatMaterialPropertyBlock(block);
                Require(!soil.CurrentTexture && !field && block.GetFloat("_SoilInteractionStrength") == 0,
                    "Disabling soil interaction retained a runtime texture or active binding.");
            }
            finally
            {
                if (holder) Object.DestroyImmediate(holder);
                if (data) Object.DestroyImmediate(data);
                foreach (TerrainLayer layer in layers) if (layer) Object.DestroyImmediate(layer);
                EditorSceneManager.ClosePreviewScene(preview);
            }
            Require(SceneManager.GetActiveScene() == original && original.isDirty == dirty, "Soil checks changed the active scene state.");
            return "PASS soil field: valid URP shader; stationary/meadow/water/air/raised surfaces rejected; walking soil marks bind, fade, skip teleports, and release on disable; preview scene fully cleaned.";
        }

        static float Peak(WatersideSoilInteraction soil)
        {
            float peak = 0;
            foreach (Color32 color in soil.CurrentTexture.GetPixels32()) peak = Mathf.Max(peak, color.r / 255f);
            return peak;
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
