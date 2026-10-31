using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.EnvironmentLab.Editor
{
    public static class FogStudyBuilder
    {
        public const string ScenePath = "Assets/Scenes/Experimental/02_FogStudy.unity";
        const string Trees = "Assets/Experiments/Vegetation/Generated/TreeSamples/";
        const string Highlands = "Assets/HIGHLANDS - Stylized Environment/";

        [MenuItem("Weather House/Environment Lab/02 雾层次 · 打开样板")]
        public static void Open()
        {
            EnvironmentLabBuilder.RequireSavedScene();
            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            var meshes = new[] { AssetDatabase.LoadAssetAtPath<Mesh>(Trees + "Tree_913.asset"), AssetDatabase.LoadAssetAtPath<Mesh>(Trees + "Tree_521.asset") };
            var bark = AssetDatabase.LoadAssetAtPath<Material>(Trees + "Bark.mat");
            var leaves = AssetDatabase.LoadAssetAtPath<Material>(Trees + "Canopy.mat");
            if (!meshes[0] || !meshes[1] || !bark || !leaves) throw new InvalidOperationException("Saved painterly tree samples are required.");
            Directory.CreateDirectory(EnvironmentLabBuilder.Generated);
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnvironmentLabBuilder.Lighting(true);
            RenderSettings.fogColor = new Color(.64f, .73f, .72f);
            RenderSettings.fogStartDistance = 24;
            RenderSettings.fogEndDistance = 150;
            var floor = EnvironmentLabBuilder.Material("FogValleyFloor", "Universal Render Pipeline/Lit", m => {
                m.SetColor("_BaseColor", new Color(.36f, .43f, .29f)); m.SetFloat("_Smoothness", .04f);
            });
            var hills = EnvironmentLabBuilder.Material("FogValleyHills", "Universal Render Pipeline/Lit", m => {
                m.SetColor("_BaseColor", new Color(.30f, .39f, .31f)); m.SetFloat("_Smoothness", .03f);
            });
            EnvironmentLabBuilder.Box("Walkable valley floor", new Vector3(0, -.3f, 20), new Vector3(120, .6f, 250), floor);
            for (int side = -1; side <= 1; side += 2)
                for (int ridge = 0; ridge < 3; ridge++)
                {
                    var hill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    hill.name = "Valley ridge " + side + " / " + ridge;
                    hill.transform.position = new Vector3(side * (42 + ridge * 3), -1, 12 + ridge * 39);
                    hill.transform.localScale = new Vector3(50, 15 + ridge * 5, 62);
                    hill.GetComponent<Renderer>().sharedMaterial = hills;
                }
            var forest = new GameObject("Existing painterly tree meshes · read only");
            var random = new System.Random(913);
            int count = 0;
            for (int row = 0; row < 6; row++)
                for (int side = -1; side <= 1; side += 2)
                    for (int band = 0; band < 3; band++)
                    {
                        var mesh = meshes[count % meshes.Length];
                        float scale = 1.05f + row * .10f + (float)random.NextDouble() * .35f;
                        float x = side * (9 + band * 6 + (float)random.NextDouble() * 3);
                        float z = -3 + row * 17 + band * 3 + (float)random.NextDouble() * 5;
                        var tree = new GameObject("Forest tree " + (++count));
                        tree.transform.SetParent(forest.transform, false);
                        tree.transform.position = new Vector3(x, -mesh.bounds.min.y * scale, z);
                        tree.transform.rotation = Quaternion.Euler(0, (float)random.NextDouble() * 360, 0);
                        tree.transform.localScale = Vector3.one * scale;
                        tree.AddComponent<MeshFilter>().sharedMesh = mesh;
                        tree.AddComponent<MeshRenderer>().sharedMaterials = new[] { bark, leaves };
                    }
            var stone = AssetDatabase.LoadAssetAtPath<Material>(Highlands + "Materials/Forest/Stones_Rocks(Grass).mat");
            Vector3[] rocks = { new Vector3(-5, 0, 3), new Vector3(7, 0, 13), new Vector3(-8, 0, 29), new Vector3(12, 0, 45), new Vector3(-16, 0, 66) };
            for (int i = 0; i < rocks.Length; i++)
                EnvironmentLabBuilder.Specimen("Rock_1(Grass)", "Valley rock " + i, rocks[i], 3.4f + i * .55f, Quaternion.Euler(i * 11, i * 47, i * 9), stone);

            var cards = new[] {
                Card("FogCard_Left", new Vector3(-12, 8, 62), new Vector2(39, 20), 12, new Color(.62f, .73f, .71f)),
                Card("FogCard_Right", new Vector3(16, 10, 78), new Vector2(43, 24), -18, new Color(.67f, .76f, .74f)),
                Card("FogCard_Distant", new Vector3(-3, 12, 99), new Vector2(57, 29), 7, new Color(.68f, .77f, .76f))
            };
            var view = EnvironmentLabBuilder.CameraAndView(new Vector3(0, 3, 30), 44);
            view.navigation.groundHalfExtents = new Vector2(14, 104);
            view.navigation.flySpeed = 10;
            view.fogCards = cards;
            view.distanceFog = true;
            view.title = "第二批 · 森林远近层次";
            view.description = "距离雾形成整体纵深，三片错开的远景薄雾补充局部层次。沿谷地行走，或飞到雾片附近检查渐隐。";
            view.GetComponent<Camera>().farClipPlane = 230;
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = cards[0].gameObject;
        }

        static FogCard Card(string name, Vector3 position, Vector2 size, float yaw, Color tint)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = new Vector3(size.x, size.y, 1);
            var material = EnvironmentLabBuilder.CopyMaterial(Highlands + "Materials/FX/Fog.mat", name);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var card = go.AddComponent<FogCard>();
            card.targetRenderer = renderer;
            card.tint = tint;
            card.opacity = .18f;
            card.nearFadeStart = 4;
            card.nearFadeEnd = 12;
            card.intersectionFadeDistance = 2;
            card.ApplyTo(material);
            EditorUtility.SetDirty(material);
            return card;
        }
    }
}
