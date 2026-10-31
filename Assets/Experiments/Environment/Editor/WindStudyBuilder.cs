using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.EnvironmentLab.Editor
{
    public static class WindStudyBuilder
    {
        public const string ScenePath = "Assets/Scenes/Experimental/03_WindStudy.unity";
        const string TreeAssets = "Assets/Experiments/Vegetation/Generated/TreeSamples/";
        const string Plants = "Assets/Experiments/Vegetation/PlantLibrary/";

        [MenuItem("Weather House/Environment Lab/03 风动 · 打开样板")]
        public static void Open()
        {
            EnvironmentLabBuilder.RequireSavedScene();
            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            Mesh treeMesh = Load<Mesh>(TreeAssets + "Tree_913.asset");
            Load<Material>(TreeAssets + "Bark.mat"); Load<Material>(TreeAssets + "Canopy.mat");
            string[] kinds = { "G4_DenseShort", "G1_TallBroad", "R09_CreamFoxtail" };
            foreach (string kind in kinds)
                for (int i = 1; i <= 4; i++) Load<GameObject>(Plants + "Prefabs/" + kind + "_v" + i + ".prefab");
            Directory.CreateDirectory(EnvironmentLabBuilder.Generated); AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnvironmentLabBuilder.Lighting(true);
            var ground = EnvironmentLabBuilder.Material("Wind_Ground", "Weather House/Painterly Ground", m => {
                m.SetColor("_BaseColor", new Color(.41f, .47f, .25f));
                m.SetColor("_PatchColor", new Color(.55f, .59f, .32f));
                m.SetFloat("_WaterLevel", -5);
            });
            EnvironmentLabBuilder.Box("Quiet meadow ground", new Vector3(0, -.18f, 4), new Vector3(70, .36f, 70), ground);
            Path();
            Material bark = EnvironmentLabBuilder.CopyMaterial(TreeAssets + "Bark.mat", "Wind_Bark");
            Material leaves = EnvironmentLabBuilder.CopyMaterial(TreeAssets + "Canopy.mat", "Wind_Canopy");
            var targets = new List<Renderer>();
            targets.Add(Tree("Wind tree · near left", treeMesh, bark, leaves, new Vector3(-3.8f, 0, 4.8f), 1.08f, -18));
            targets.Add(Tree("Wind tree · far right", treeMesh, bark, leaves, new Vector3(5.8f, 0, 8.5f), 1.3f, 43));
            targets.Add(Tree("Wind tree · far left", treeMesh, bark, leaves, new Vector3(-8.4f, 0, 10.5f), .94f, 109));
            targets.Add(Grass(kinds[0], 480, 21, false));
            targets.Add(Grass(kinds[1], 160, 42, true));
            targets.Add(Grass(kinds[2], 32, 63, true));
            var wind = new GameObject("03 统一背景风 · 进入 Play 试调").AddComponent<EnvironmentWind>();
            wind.targets = targets.ToArray(); wind.force = 1.35f; wind.speed = 1.05f;
            wind.direction = new Vector2(1, .35f);
            var view = EnvironmentLabBuilder.CameraAndView(new Vector3(0, 2, 2), 19.5f);
            view.wind = wind;
            view.title = "第三批 · 草树共用一阵风";
            view.description = "草与树共用风力、风速和世界风向，保留各自的摆幅与波形。风力归零可看静风对照。";
            view.navigation.groundHalfExtents = new Vector2(15, 15);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = wind.gameObject;
        }

        static Renderer Tree(string name, Mesh mesh, Material bark, Material leaves, Vector3 position, float scale, float yaw)
        {
            var go = new GameObject(name);
            go.transform.position = position - Vector3.up * mesh.bounds.min.y * scale;
            go.transform.localScale = Vector3.one * scale;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { bark, leaves };
            renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            return renderer;
        }

        static Renderer Grass(string kind, int count, int seed, bool foreground)
        {
            string path = EnvironmentLabBuilder.Generated + "/Wind_" + kind + ".asset";
            Mesh combined = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            var prefab = Load<GameObject>(Plants + "Prefabs/" + kind + "_v1.prefab");
            if (!combined)
            {
                var source = new Mesh[4];
                for (int i = 0; i < source.Length; i++)
                    source[i] = Load<GameObject>(Plants + "Prefabs/" + kind + "_v" + (i + 1) + ".prefab").GetComponent<MeshFilter>().sharedMesh;
                var random = new System.Random(seed);
                var instances = new List<CombineInstance>();
                while (instances.Count < count)
                {
                    float x = Range(random, -6, 6), z = Range(random, -4.5f, foreground ? 2 : 5);
                    if (x * x / 36 + z * z / 25 > 1 || Mathf.Abs(x - PathX(z)) < .78f) continue;
                    float scale = Range(random, foreground ? .72f : .9f, foreground ? 1.05f : 1.6f);
                    instances.Add(new CombineInstance { mesh = source[instances.Count % 4],
                        transform = Matrix4x4.TRS(new Vector3(x, .012f, z), Quaternion.Euler(0, Range(random, 0, 360), 0), Vector3.one * scale) });
                }
                combined = new Mesh { name = "Wind " + kind + " · " + count + " clumps", indexFormat = IndexFormat.UInt32 };
                combined.CombineMeshes(instances.ToArray(), true, true);
                combined.RecalculateBounds();
                Bounds bounds = combined.bounds; bounds.Expand(.8f); combined.bounds = bounds;
                AssetDatabase.CreateAsset(combined, path);
            }
            var go = new GameObject("Wind grass batch · " + kind);
            go.AddComponent<MeshFilter>().sharedMesh = combined;
            var renderer = go.AddComponent<MeshRenderer>();
            string materialPath = AssetDatabase.GetAssetPath(prefab.GetComponent<Renderer>().sharedMaterial);
            renderer.sharedMaterial = EnvironmentLabBuilder.CopyMaterial(materialPath, "Wind_" + kind);
            renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            return renderer;
        }

        static void Path()
        {
            string path = EnvironmentLabBuilder.Generated + "/Wind_Path.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh)
            {
                const int segments = 34;
                var vertices = new Vector3[(segments + 1) * 2];
                var triangles = new int[segments * 6];
                for (int i = 0; i <= segments; i++)
                {
                    float z = Mathf.Lerp(-13, 21, i / (float)segments), x = PathX(z);
                    vertices[i * 2] = new Vector3(x - .68f, .006f, z);
                    vertices[i * 2 + 1] = new Vector3(x + .68f, .006f, z);
                    if (i == segments) continue;
                    int v = i * 2, t = i * 6;
                    triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                    triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
                }
                mesh = new Mesh { name = "Meadow path", vertices = vertices, triangles = triangles };
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, path);
            }
            var material = EnvironmentLabBuilder.Material("Wind_Path", "Universal Render Pipeline/Lit", m => {
                m.SetColor("_BaseColor", new Color(.57f, .53f, .36f)); m.SetFloat("_Smoothness", .03f);
            });
            var go = new GameObject("Warm winding path");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        static float PathX(float z) { return .7f + Mathf.Sin((z + 4) * .24f) * 1.45f; }
        static float Range(System.Random random, float min, float max) { return Mathf.Lerp(min, max, (float)random.NextDouble()); }
        static T Load<T>(string path) where T : UnityEngine.Object
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!value) throw new InvalidOperationException("Missing wind study asset: " + path);
            return value;
        }
    }
}
