using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouse.Hillside.Editor
{
    public static class HillsideStaticPropUpgrade
    {
        [MenuItem("Weather House/Hillside/Replace Basic Props With Highlands")]
        public static void Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != HillsideSceneBuilder.ScenePath)
                throw new InvalidOperationException("Open the Hillside scene outside Play before replacing static props.");
            string[] names = { "花园短篱笆", "南侧短篱笆", "台地短篱笆", "苔藓床旁岸石", "石阵倒伏残石" };
            var all = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            var targets = names.Select(name => all.Single(t => t.name == name).gameObject).ToArray();
            var assets = new[] { "Fence", "Stone" }.Select(id => AssetDatabase.LoadAssetAtPath<GameObject>(HillsideSceneBuilder.Output + "/Hillside_Pack" + id + ".prefab")).ToArray();
            if (assets.Any(asset => !asset)) throw new InvalidOperationException("Highlands replacement prefabs are missing.");
            // Preserve user components instead of silently dropping them during a mesh replacement.
            foreach (var target in targets)
                if (target.GetComponentsInChildren<Component>(true).Any(c => !c || !(c is Transform || c is MeshFilter || c is Renderer || c is Collider || c is LODGroup)))
                    throw new InvalidOperationException("This prop has additional components; review it before replacing: " + target.name);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Replace basic props with Highlands");
            try
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    var old = targets[i];
                    var asset = assets[i < 3 ? 0 : 1];
                    if (PrefabUtility.GetCorrespondingObjectFromSource(old) == asset) continue;
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(old);
                    if (!source) throw new InvalidOperationException("Expected original prefab instance: " + old.name);
                    var before = HillsideSceneBuilder.Bounds(old);
                    var replacement = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                    Undo.RegisterCreatedObjectUndo(replacement, "Create Highlands prop");
                    replacement.name = old.name;
                    replacement.transform.SetParent(old.transform.parent, true);
                    replacement.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
                    replacement.transform.rotation = old.transform.rotation * Quaternion.Inverse(source.transform.rotation) * asset.transform.rotation;
                    replacement.SetActive(old.activeSelf);
                    var size = HillsideSceneBuilder.Bounds(replacement).size;
                    float horizontal = Mathf.Max(before.size.x, before.size.z) / Mathf.Max(.001f, Mathf.Max(size.x, size.z));
                    replacement.transform.localScale = Vector3.Scale(replacement.transform.localScale, new Vector3(horizontal, before.size.y / Mathf.Max(.001f, size.y), horizontal));
                    var fitted = HillsideSceneBuilder.Bounds(replacement);
                    replacement.transform.position += new Vector3(before.center.x - fitted.center.x, before.min.y - fitted.min.y, before.center.z - fitted.center.z);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(replacement.transform);
                    var after = HillsideSceneBuilder.Bounds(replacement);
                    if (Vector2.Distance(new Vector2(before.center.x, before.center.z), new Vector2(after.center.x, after.center.z)) > .005f ||
                        Mathf.Abs(before.min.y - after.min.y) > .005f || Mathf.Abs(before.size.y - after.size.y) > .005f ||
                        Mathf.Abs(Mathf.Max(before.size.x, before.size.z) - Mathf.Max(after.size.x, after.size.z)) > .005f ||
                        replacement.GetComponentsInChildren<Collider>().Length == 0)
                        throw new InvalidOperationException("Replacement bounds or collider check failed: " + old.name);
                    Undo.DestroyObjectImmediate(old);
                }
                Undo.CollapseUndoOperations(group);
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            // The current scene can already contain unrelated unsaved edits; saving remains explicit.
        }
    }
}
