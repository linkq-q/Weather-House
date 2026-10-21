using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WeatherHouse.Vegetation.Editor
{
    [CustomEditor(typeof(PainterlyTree))]
    public sealed class PainterlyTreeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var tree = (PainterlyTree)target;
            Mesh current = tree.GetComponent<MeshFilter>().sharedMesh;
            if (current) EditorGUILayout.LabelField("当前网格", current.vertexCount + " 顶点 / " + current.triangles.Length / 3 + " 三角形");
            EditorGUILayout.HelpBox("修改树形后点更新。只更新当前树的持久网格，树干与叶色在两个材质中调整。完成后保存场景。", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
                if (GUILayout.Button("更新当前树 · 保存网格", GUILayout.Height(32))) SaveMesh(tree);
        }

        public static void SaveMesh(PainterlyTree tree)
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("请退出 Play 后保存树形。");
            Mesh created = tree.CreateMesh();
            var filter = tree.GetComponent<MeshFilter>();
            Mesh old = filter.sharedMesh;
            string path = old ? AssetDatabase.GetAssetPath(old) : "";
            bool shared = old && Resources.FindObjectsOfTypeAll<PainterlyTree>()
                .Any(other => other != tree && other.GetComponent<MeshFilter>().sharedMesh == old);
            try
            {
                Undo.RecordObject(filter, "更新油画树网格");
                if (!shared && path.StartsWith("Assets/WeatherHouseVegetationLab/Generated/") && path.EndsWith(".asset"))
                {
                    Undo.RecordObject(old, "更新油画树网格");
                    EditorUtility.CopySerialized(created, old);
                    EditorUtility.SetDirty(old);
                    filter.sharedMesh = old;
                }
                else
                {
                    const string folder = "Assets/WeatherHouseVegetationLab/Generated/TreeSamples";
                    if (!AssetDatabase.IsValidFolder(folder))
                    {
                        Directory.CreateDirectory(folder);
                        AssetDatabase.Refresh();
                    }
                    string destination = AssetDatabase.GenerateUniqueAssetPath(folder + "/Tree_" + tree.seed + ".asset");
                    AssetDatabase.CreateAsset(created, destination);
                    filter.sharedMesh = created;
                    created = null;
                }
                EditorUtility.SetDirty(filter);
                EditorSceneManager.MarkSceneDirty(tree.gameObject.scene);
                AssetDatabase.SaveAssets();
            }
            finally { if (created) Object.DestroyImmediate(created); }
        }

        // Explicit upgrade hook: only the two persisted lab samples are eligible.
        public static void RefreshLabSamples()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("请退出 Play 后刷新树木样本。");
            const string scenePath = "Assets/WeatherHouseVegetationLab/VegetationLab.unity";
            const string folder = "Assets/WeatherHouseVegetationLab/Generated/TreeSamples/";
            var trees = Resources.FindObjectsOfTypeAll<PainterlyTree>().Where(tree =>
                tree.gameObject.scene.path == scenePath &&
                (tree.seed == 913 || tree.seed == 521) &&
                AssetDatabase.GetAssetPath(tree.GetComponent<MeshFilter>().sharedMesh) == folder + "Tree_" + tree.seed + ".asset").ToArray();
            if (trees.Length != 2 || trees.Select(tree => tree.seed).Distinct().Count() != 2)
                throw new System.InvalidOperationException("请打开包含两个原始树木样本的 VegetationLab 场景。");
            Material bark = AssetDatabase.LoadAssetAtPath<Material>(folder + "Bark.mat");
            Material canopy = AssetDatabase.LoadAssetAtPath<Material>(folder + "Canopy.mat");
            if (!bark || !canopy) throw new System.InvalidOperationException("树木样本材质不完整。");
            foreach (PainterlyTree tree in trees) SaveMesh(tree);
            for (int i = 0; i < 2; i++)
            {
                Material material = i == 0 ? bark : canopy;
                Material defaults = PainterlyTree.CreateMaterial(i == 1);
                try
                {
                    Undo.RecordObject(material, "更新树木样本材质");
                    material.shader = defaults.shader;
                    material.CopyPropertiesFromMaterial(defaults);
                    EditorUtility.SetDirty(material);
                }
                finally { Object.DestroyImmediate(defaults); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Refreshed two persisted lab tree samples and their Bark/Canopy material defaults. Save the lab scene after review.");
        }
    }
}
