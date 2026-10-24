using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WatersideFenceLodRepairChecks
    {
        const string Output = "JourneyReview/Revision_20261024/FenceZoomDiagnosis/Reports";

        // Read-only on the active scene. Before repair this fails on the displaced actual renderer.
        public static string RunReadOnly()
        {
            Scene scene = SceneManager.GetActiveScene(); bool dirty = scene.isDirty;
            var items = WatersideFenceLodRepair.Plan();
            WatersideFenceLodRepair.VerifyAligned(items);
            CheckBoundsAgainstPrefabs(items);
            Require(items.All(i => i.needsChange.All(c => !c)), "All four reviewed children must already be at their source pose.");
            Require(scene.isDirty == dirty, "Read-only checks changed scene dirty state.");
            return Write("PASS actual nine LOD renderers align with source geometry at retained roots; root collider bounds align; all three recalculated LOD bounds match independent source-prefab clones; repeated plan is a no-op. No scene saved.");
        }

        // Synthetic checks live in an unsaved preview scene, using the real source prefabs.
        public static string RunSourceChecks()
        {
            Scene active = SceneManager.GetActiveScene(); bool dirty = active.isDirty;
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (var spec in WatersideFenceLodRepair.Specs)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HIGHLANDS - Stylized Environment/Prefabs/Witches House/" + spec.prefab + ".prefab");
                    var holder = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                    Transform source = prefab.GetComponent<LODGroup>().GetLODs()[1].renderers[0].transform;
                    foreach (var before in spec.children)
                    {
                        Transform t = holder.transform;
                        t.localPosition = before.position; t.localRotation = before.rotation; t.localScale = Vector3.one;
                        Require(WatersideFenceLodRepair.NeedsChange(t, source, before), "Known original pose must require repair.");
                        t.localPosition = source.localPosition; t.localRotation = source.localRotation; t.localScale = source.localScale;
                        Require(!WatersideFenceLodRepair.NeedsChange(t, source, before), "Target pose must be idempotent.");
                        t.localPosition += Vector3.right * .01f; Reject(() => WatersideFenceLodRepair.NeedsChange(t, source, before), "New position edit must reject.");
                        t.localPosition = source.localPosition; t.localRotation = Quaternion.Euler(0, 1, 0) * source.localRotation;
                        Reject(() => WatersideFenceLodRepair.NeedsChange(t, source, before), "New rotation edit must reject.");
                        t.localRotation = source.localRotation; t.localScale = source.localScale * 1.01f;
                        Reject(() => WatersideFenceLodRepair.NeedsChange(t, source, before), "New scale edit must reject.");
                    }
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            Require(SceneManager.GetActiveScene() == active && active.isDirty == dirty, "Synthetic fixture touched active scene.");
            return Write("PASS four known original poses, four target no-ops, and twelve new-edit rejection cases using source-prefab transforms. Only an isolated preview scene was created and closed.");
        }

        // This is the explicit mutation entry point for the owner; it never saves the scene.
        public static string ApplyChecked()
        {
            var items = WatersideFenceLodRepair.Plan();
            var changed = new HashSet<Transform>(items.SelectMany(i => i.children.Where((_, n) => i.needsChange[n])));
            var allTransforms = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var before = allTransforms.ToDictionary(t => t, t => EditorJsonUtility.ToJson(t));
            var rootsAndColliders = items.SelectMany(i => new Object[] { i.group.transform, i.group.GetComponent<MeshRenderer>(), i.group.GetComponent<MeshCollider>() }).ToDictionary(o => o, EditorJsonUtility.ToJson);
            var lods = items.Select(i => i.group.GetLODs()).ToArray();
            string report = WatersideFenceLodRepair.Apply();
            foreach (Transform t in allTransforms)
                if (!changed.Contains(t)) Require(before[t] == EditorJsonUtility.ToJson(t), "An unlisted Transform changed: " + t.name);
            foreach (var pair in rootsAndColliders) Require(pair.Value == EditorJsonUtility.ToJson(pair.Key), "Root renderer/Transform/collider changed: " + pair.Key.name);
            for (int i = 0; i < items.Length; i++)
            {
                LOD[] after = items[i].group.GetLODs();
                for (int l = 0; l < after.Length; l++) Require(after[l].screenRelativeTransitionHeight == lods[i][l].screenRelativeTransitionHeight &&
                    after[l].fadeTransitionWidth == lods[i][l].fadeTransitionWidth && after[l].renderers.SequenceEqual(lods[i][l].renderers), "LOD renderer membership or thresholds changed.");
            }
            RunReadOnly();
            return report;
        }

        static void CheckBoundsAgainstPrefabs(WatersideFenceLodRepair.Item[] items)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (var item in items)
                {
                    var clone = (GameObject)PrefabUtility.InstantiatePrefab(item.prefab, preview);
                    clone.transform.SetPositionAndRotation(item.group.transform.position, item.group.transform.rotation);
                    clone.transform.localScale = item.group.transform.lossyScale;
                    // The three reviewed roots have no sheared ancestry; reject a changed hierarchy instead of approximating it.
                    Matrix4x4 originalMatrix = item.group.transform.localToWorldMatrix, cloneMatrix = clone.transform.localToWorldMatrix;
                    for (int n = 0; n < 16; n++) Require(Mathf.Abs(originalMatrix[n] - cloneMatrix[n]) < .00001f, "Source bounds oracle cannot represent changed sheared ancestry.");
                    LODGroup expected = clone.GetComponent<LODGroup>(); expected.RecalculateBounds();
                    Require((expected.localReferencePoint - item.group.localReferencePoint).sqrMagnitude < 1e-6f && Mathf.Abs(expected.size - item.group.size) < .001f,
                        "LOD bounds still include old displaced geometry: " + item.group.name);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        static void Reject(Action action, string message)
        { bool failed = false; try { action(); } catch (InvalidOperationException) { failed = true; } Require(failed, message); }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static string Write(string message)
        {
            Directory.CreateDirectory(Output); string path = Path.Combine(Output, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_checks_" + Guid.NewGuid().ToString("N").Substring(0,8) + ".txt");
            File.WriteAllText(path, message); return Path.GetFullPath(path);
        }
    }
}
