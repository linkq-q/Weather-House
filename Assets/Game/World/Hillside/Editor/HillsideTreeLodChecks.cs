using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Hillside.Editor
{
    public static class HillsideTreeLodChecks
    {
        public static string Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run isolated tree LOD checks outside Play mode.");
            Scene active = SceneManager.GetActiveScene();
            bool wasDirty = active.isDirty;
            float lodBias = QualitySettings.lodBias;
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = EditorUtility.CreateGameObjectWithHideFlags("Tree LOD checks", HideFlags.HideAndDontSave);
                SceneManager.MoveGameObjectToScene(root, preview);
                var controller = root.AddComponent<HillsideTreeOcclusion>();
                LODGroup normal = Group(root.transform, new[] { .6f, .2f, .001f });
                LODGroup capped = Group(root.transform, new[] { .6f, .45f, .001f });
                LODGroup close = Group(root.transform, new[] { .6f, .59f, .001f });
                LODGroup two = Group(root.transform, new[] { .6f, .001f });
                LODGroup empty = Group(root.transform, Array.Empty<float>());
                var groups = new[] { normal, capped, close, two, empty };
                var originals = new LOD[groups.Length][];
                for (int i = 0; i < groups.Length; i++) originals[i] = groups[i].GetLODs();
                Require(normal.GetLODs()[1].screenRelativeTransitionHeight.Equals(.2f), "Edit mode OnEnable changed tree LODs.");
                Invoke(controller, "ApplyTreeLods");
                CheckUnchangedFields(groups, originals);
                Near(normal.GetLODs()[1].screenRelativeTransitionHeight, .3f, "Normal middle threshold was not multiplied by 1.5.");
                Near(capped.GetLODs()[1].screenRelativeTransitionHeight, .57f, "Middle threshold was not capped below the first threshold.");
                Require(capped.GetLODs()[1].screenRelativeTransitionHeight < originals[1][0].screenRelativeTransitionHeight, "Capped threshold crossed first threshold.");
                Require(close.GetLODs()[1].screenRelativeTransitionHeight.Equals(originals[2][1].screenRelativeTransitionHeight), "Boundary threshold was lowered.");
                Require(two.GetLODs()[1].screenRelativeTransitionHeight.Equals(originals[3][1].screenRelativeTransitionHeight) && empty.GetLODs().Length == 0, "Short LOD groups were changed.");
                Invoke(controller, "ApplyTreeLods");
                Near(normal.GetLODs()[1].screenRelativeTransitionHeight, .3f, "Repeated initialization multiplied the threshold twice.");
                Invoke(controller, "OnDisable");
                for (int i = 0; i < groups.Length; i++) RequireSameLods(groups[i].GetLODs(), originals[i]);
                Invoke(controller, "ApplyTreeLods");
                Near(normal.GetLODs()[1].screenRelativeTransitionHeight, .3f, "Re-enable lifecycle did not reapply the threshold.");
                Invoke(controller, "OnDisable");
                for (int i = 0; i < groups.Length; i++) RequireSameLods(groups[i].GetLODs(), originals[i]);
                Require(QualitySettings.lodBias.Equals(lodBias), "Global LOD bias changed.");
            }
            finally
            {
                if (root) Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
            }
            Require(SceneManager.GetActiveScene() == active && active.isDirty == wasDirty, "Checks changed the active scene or its dirty state.");
            return "PASS tree LOD: 1.5x middle threshold; strict first-threshold cap; close/short/empty groups; unchanged first/cull/renderers/fades/global bias; idempotent initialization; disable restoration and repeated reapplication; active scene preserved. No scene/prefab/material assets were edited.";
        }

        static LODGroup Group(Transform root, float[] thresholds)
        {
            var item = new GameObject("Tree group") { hideFlags = HideFlags.HideAndDontSave };
            item.transform.SetParent(root, false);
            var group = item.AddComponent<LODGroup>();
            var lods = new LOD[thresholds.Length];
            for (int i = 0; i < lods.Length; i++)
            {
                var model = new GameObject("Tree model " + i) { hideFlags = HideFlags.HideAndDontSave };
                model.transform.SetParent(item.transform, false);
                lods[i] = new LOD(thresholds[i], new Renderer[] { model.AddComponent<MeshRenderer>() }) { fadeTransitionWidth = .05f + i * .1f };
            }
            group.SetLODs(lods);
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;
            return group;
        }
        static void CheckUnchangedFields(LODGroup[] groups, LOD[][] originals)
        {
            for (int group = 0; group < groups.Length; group++)
            {
                LOD[] current = groups[group].GetLODs();
                Require(current.Length == originals[group].Length, "LOD count changed.");
                Require(groups[group].fadeMode == LODFadeMode.CrossFade && groups[group].animateCrossFading, "Group fade settings changed.");
                for (int i = 0; i < current.Length; i++)
                {
                    if (i != 1 || current.Length < 3) Require(current[i].screenRelativeTransitionHeight.Equals(originals[group][i].screenRelativeTransitionHeight), "First/cull threshold changed.");
                    Require(current[i].fadeTransitionWidth.Equals(originals[group][i].fadeTransitionWidth), "LOD fade width changed.");
                    Require(current[i].renderers.Length == originals[group][i].renderers.Length, "Renderer count changed.");
                    for (int renderer = 0; renderer < current[i].renderers.Length; renderer++)
                        Require(current[i].renderers[renderer] == originals[group][i].renderers[renderer], "Renderer reference changed.");
                }
            }
        }
        static void RequireSameLods(LOD[] actual, LOD[] expected)
        {
            Require(actual.Length == expected.Length, "Restoration changed LOD count.");
            for (int i = 0; i < actual.Length; i++)
            {
                Require(actual[i].screenRelativeTransitionHeight.Equals(expected[i].screenRelativeTransitionHeight) && actual[i].fadeTransitionWidth.Equals(expected[i].fadeTransitionWidth), "Disable did not restore original thresholds/fades.");
                for (int renderer = 0; renderer < actual[i].renderers.Length; renderer++) Require(actual[i].renderers[renderer] == expected[i].renderers[renderer], "Disable changed a renderer reference.");
            }
        }
        static void Invoke(HillsideTreeOcclusion owner, string name) => typeof(HillsideTreeOcclusion).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, null);
        static void Near(float actual, float expected, string message) => Require(Mathf.Abs(actual - expected) < .000001f, message);
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Tree LOD check failed: " + message); }
    }
}
