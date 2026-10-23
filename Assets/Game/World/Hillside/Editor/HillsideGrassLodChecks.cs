using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Hillside.Editor
{
    public static class HillsideGrassLodChecks
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        const string Folder = "Assets/Game/World/Hillside/Generated/Nature/";

        [MenuItem("Weather House/Checks/Grass Mesh LOD")]
        public static void RunMenu() { Debug.Log(Run()); }

        public static string Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run isolated LOD checks outside Play mode.");
            Scene preview = EditorSceneManager.NewPreviewScene();
            var temporary = new List<Object>();
            var generated = new List<Mesh>();
            GameObject root = null;
            try
            {
                root = new GameObject("Grass LOD isolated checks") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, preview);
                var grass = root.AddComponent<HillsideGrassRenderer>(); grass.manualCaptureOnly = true;
                grass.variants = new Mesh[12]; grass.materials = new Material[3];
                string[] species = { "ShortGrass", "TallGrass", "WhiteDaisy" };
                MethodInfo select = typeof(HillsideGrassRenderer).GetMethod("SelectMesh", Private);
                Require(select != null, "Grass draw path has no mesh LOD selector.");
                for (int variant = 0; variant < 12; variant++)
                {
                    Mesh source = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + species[variant / 4] + "_Variant" + variant % 4 + ".asset");
                    Require(source, "Missing original mesh variant " + variant);
                    grass.variants[variant] = source;
                    string before = EditorJsonUtility.ToJson(source);
                    Require(Select(grass, select, variant, false) == source, "Near grass lost its original mesh reference.");
                    Mesh low = Select(grass, select, variant, true);
                    if (variant >= 8) Require(low == source, "Daisy geometry should remain unchanged.");
                    else
                    {
                        generated.Add(low);
                        Require(low != source && Select(grass, select, variant, true) == low, "Far mesh was missing or rebuilt on reuse.");
                        CheckReduction(source, low, variant < 4 ? 104 : 88);
                    }
                    Require(EditorJsonUtility.ToJson(source) == before, "LOD generation modified a source mesh.");
                }
                var tangentSource = Object.Instantiate(grass.variants[0]); temporary.Add(tangentSource);
                var tangents = new Vector4[tangentSource.vertexCount];
                for (int i = 0; i < tangents.Length; i++) tangents[i] = new Vector4(1, 0, 0, -1);
                tangentSource.tangents = tangents;
                Mesh replaced = generated[0]; grass.variants[0] = tangentSource;
                Mesh tangentLod = Select(grass, select, 0, true); generated.Add(tangentLod);
                Require(!replaced, "Replacing the source leaked its former temporary LOD mesh.");
                CheckReduction(tangentSource, tangentLod, 104);

                var malformed = Object.Instantiate(tangentSource); temporary.Add(malformed);
                int[] triangles = malformed.triangles; int first = triangles[0]; triangles[0] = triangles[1]; triangles[1] = first;
                malformed.triangles = triangles; grass.variants[0] = malformed;
                Require(Select(grass, select, 0, true) == malformed, "Unknown topology must fall back to the original mesh.");
                var wrongUV = Object.Instantiate(tangentSource); temporary.Add(wrongUV);
                Vector2[] uv = wrongUV.uv; uv[0] = new Vector2(.1f, .1f); wrongUV.uv = uv; grass.variants[0] = wrongUV;
                Require(Select(grass, select, 0, true) == wrongUV, "Unknown UV layout must fall back to the original mesh.");
                var unreadable = Object.Instantiate(tangentSource); temporary.Add(unreadable);
                unreadable.UploadMeshData(true); grass.variants[0] = unreadable;
                Require(Select(grass, select, 0, true) == unreadable, "Unreadable meshes must fall back without reading vertex arrays.");
                grass.variants[0] = tangentSource;
                generated.Add(Select(grass, select, 0, true));

                CheckSelection(grass, select, root.transform);
                Mesh invalidated = Select(grass, select, 0, true);
                typeof(HillsideGrassRenderer).GetMethod("OnValidate", Private).Invoke(grass, null);
                Mesh regenerated = Select(grass, select, 0, true); generated.Add(regenerated);
                Require(!invalidated && regenerated && regenerated != invalidated, "Mesh-cache invalidation did not release and rebuild temporary LODs.");
                grass.enabled = false;
                foreach (Mesh mesh in generated) Require(!mesh, "Disabling grass leaked a generated LOD mesh.");
                return "PASS grass mesh LOD: 8 variants short 208->104 / tall 176->88 triangles; original near references and source meshes preserved; exact retained vertex/normal/UV/wind/color/tangent data and bounds; daisies, invalid topology/UV and unreadable sources retain originals; 60m near zone, 5m hysteresis, independent cameras, instance matrices/materials/cache unchanged; temporary meshes released";
            }
            finally
            {
                if (root) Object.DestroyImmediate(root);
                foreach (Object item in temporary) if (item) Object.DestroyImmediate(item);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        static void CheckSelection(HillsideGrassRenderer grass, MethodInfo select, Transform parent)
        {
            Type cellType = typeof(HillsideGrassRenderer).GetNestedType("Cell", BindingFlags.NonPublic);
            object cell = Activator.CreateInstance(cellType, true);
            cellType.GetField("bounds").SetValue(cell, new Bounds(Vector3.zero, new Vector3(20, 2, 20)));
            cellType.GetField("generated").SetValue(cell, 17);
            var cells = (IDictionary)typeof(HillsideGrassRenderer).GetField("cells", Private).GetValue(grass);
            cells.Add(new Vector2Int(1, 1), cell);
            Type batchType = typeof(HillsideGrassRenderer).GetNestedType("Batch", BindingFlags.NonPublic);
            object batch = Activator.CreateInstance(batchType, true);
            var matrices = new[] { Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(0, 35, 0), Vector3.one * .9f), Matrix4x4.identity };
            Matrix4x4[] copy = (Matrix4x4[])matrices.Clone();
            batchType.GetField("variant").SetValue(batch, 0); batchType.GetField("count").SetValue(batch, 2);
            batchType.GetField("matrices").SetValue(batch, matrices);
            ((IList)cellType.GetField("batches").GetValue(cell)).Add(batch);
            MethodInfo use = typeof(HillsideGrassRenderer).GetMethod("UseLowDetail", Private);
            Camera first = Camera(parent, "First observer"), second = Camera(parent, "Second observer");
            first.transform.position = new Vector3(80, 0, 0);
            Require((bool)use.Invoke(grass, new[] { cell, first }), "Far grass should enter reduced detail.");
            second.transform.position = new Vector3(20, 0, 0);
            Require(!(bool)use.Invoke(grass, new[] { cell, second }), "Nearest cell bounds must preserve near detail.");
            first.transform.position = second.transform.position = new Vector3(72, 0, 0);
            Require((bool)use.Invoke(grass, new[] { cell, first }) && !(bool)use.Invoke(grass, new[] { cell, second }),
                "SceneView cameras shared hysteresis state or the 60-65m band did not retain prior detail.");
            first.transform.position = new Vector3(70, 0, 0);
            Require(!(bool)use.Invoke(grass, new[] { cell, first }), "All geometry within 60m must recover the original mesh.");
            first.transform.position = new Vector3(75, 0, 0);
            Require(!(bool)use.Invoke(grass, new[] { cell, first }), "Far LOD switched before leaving the 5m hysteresis band.");
            first.transform.position = new Vector3(75.1f, 0, 0);
            Require((bool)use.Invoke(grass, new[] { cell, first }), "Far LOD did not switch beyond 65m.");
            grass.meshLodDistance = 0;
            Require(!(bool)use.Invoke(grass, new[] { cell, first }), "Distance 0 must disable mesh LOD.");
            grass.meshLodDistance = 60;
            Material[] materials = grass.materials;
            Select(grass, select, 0, true); Select(grass, select, 0, false);
            typeof(HillsideGrassRenderer).GetMethod("OnValidate", Private).Invoke(grass, null);
            Require(cells.Contains(new Vector2Int(1, 1)) && ReferenceEquals(cells[new Vector2Int(1, 1)], cell), "LOD settings rebuilt the cell cache.");
            Require(ReferenceEquals(batchType.GetField("matrices").GetValue(batch), matrices) && matrices[0] == copy[0] && matrices[1] == copy[1] &&
                (int)batchType.GetField("count").GetValue(batch) == 2 && ReferenceEquals(grass.materials, materials) &&
                (int)cellType.GetField("generated").GetValue(cell) == 17,
                "Mesh LOD changed grass instance parameters or generation progress.");
        }

        static Camera Camera(Transform parent, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var camera = go.AddComponent<Camera>(); camera.enabled = false; return camera;
        }

        static Mesh Select(HillsideGrassRenderer grass, MethodInfo method, int variant, bool low) =>
            (Mesh)method.Invoke(grass, new object[] { variant, low });

        static void CheckReduction(Mesh source, Mesh low, int expectedTriangles)
        {
            Require(low && low.triangles.Length / 3 == expectedTriangles && low.triangles.Length * 2 == source.triangles.Length &&
                low.vertexCount * 15 == source.vertexCount * 9 && low.bounds.Equals(source.bounds), "Grass reduction count or bounds mismatch.");
            int[] kept = { 0, 1, 2, 6, 7, 8, 12, 13, 14 };
            CheckAttribute(source.vertices, low.vertices, kept); CheckAttribute(source.normals, low.normals, kept);
            CheckAttribute(source.uv, low.uv, kept); CheckAttribute(source.uv2, low.uv2, kept); CheckAttribute(source.colors, low.colors, kept);
            if (source.tangents.Length > 0) CheckAttribute(source.tangents, low.tangents, kept);
            else Require(low.tangents.Length == 0, "LOD invented tangent attributes.");
            foreach (int index in low.triangles) Require(index >= 0 && index < low.vertexCount, "Reduced topology references an invalid vertex.");
        }

        static void CheckAttribute<T>(T[] source, T[] low, int[] kept)
        {
            for (int i = 0; i < low.Length; i++)
                Require(Equals(source[i / kept.Length * 15 + kept[i % kept.Length]], low[i]), "A retained vertex attribute changed.");
        }

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
