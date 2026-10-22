using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace WeatherHouseSquirrelLab.Editor
{
    public static class SquirrelWorkbenchChecks
    {
        const string Review = "SquirrelLabReview/Workbench";
        [Serializable] sealed class Report
        {
            public bool passed, editorWindowLive, originalAssetUnchanged, normalizedWeights, boneEditChangedBind, restMeshPreserved, presetReloaded;
            public int evaluatedFrames, bones, vertices;
            public float evaluationMilliseconds, restMeshMaxError;
            public string error, preview, sourceAsset, sourceHashBefore, sourceHashAfter;
            public SquirrelWorkbenchExport.ValidationReport animation, model;
        }

        [MenuItem("Weather House/Squirrel Lab/Check Animation Workbench")]
        public static void Run()
        {
            Directory.CreateDirectory(Review);
            var report = new Report();
            SquirrelAnimationWorkbench window = SquirrelAnimationWorkbench.Open();
            try
            {
                window.SetPreviewForCheck(.43f);
                var rig = window.Rig;
                report.evaluatedFrames = window.EvaluatedFrames;
                report.evaluationMilliseconds = window.EvaluationMilliseconds;
                report.editorWindowLive = window.PreviewModel != null && window.Motion != null;
                report.bones = rig.Bones.Length; report.vertices = rig.Renderers.Sum(r => r.sharedMesh.vertexCount);
                string sourcePath = AssetDatabase.GetAssetPath(window.SourceClip);
                report.sourceAsset = sourcePath;
                report.sourceHashBefore = Hash(sourcePath);
                SquirrelWorkbenchRig.RunWeightAssertions();
                report.normalizedWeights = rig.Validate() == "OK";
                rig.RestoreBindPose();
                File.WriteAllText(Review + "/brush_undo_verification.json", rig.RunInteractionAssertions(window.PreviewCamera));
                SquirrelWorkbenchMotionChecks.Run(window.PreviewModel, window.SourceClip);

                rig.RestoreBindPose();
                var skin = rig.Renderers[0];
                var scratch = new Mesh();
                Vector3[] before;
                try
                {
                    skin.BakeMesh(scratch, false); before = scratch.vertices;
                    var prior = skin.sharedMesh.bindposes;
                    Transform bone = rig.Bones.First(b => b.name == "Tail04");
                    var edit = new SquirrelWorkbenchRig.BoneEdit
                    {
                        path = AnimationUtility.CalculateTransformPath(bone, window.PreviewModel.transform),
                        positionOffset = Vector3.zero, rotationOffsetDegrees = new Vector3(0, 0, 8)
                    };
                    rig.SetEdits(new[] { edit }); rig.RestoreBindPose();
                    var after = skin.sharedMesh.bindposes;
                    report.boneEditChangedBind = prior.Where((matrix, i) => matrix != after[i]).Any();
                    skin.BakeMesh(scratch, false);
                    Vector3[] points = scratch.vertices;
                    for (int i = 0; i < before.Length; i++) report.restMeshMaxError = Mathf.Max(report.restMeshMaxError, Vector3.Distance(before[i], points[i]));
                    report.restMeshPreserved = report.restMeshMaxError < .0005f;

                    string folder = SquirrelLabBuilder.Generated + "/Workbench/Verification";
                    Directory.CreateDirectory(folder); AssetDatabase.Refresh();
                    string modelPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/松鼠_骨骼权重测试.prefab");
                    SquirrelWorkbenchExport.SaveModelCopy(window.PreviewModel, modelPath);
                    report.model = SquirrelWorkbenchExport.LastReport;

                    rig.SetEdits(new SquirrelWorkbenchRig.BoneEdit[0]); rig.RestoreBindPose();
                    using (var motion = new SquirrelWorkbenchMotion(window.PreviewModel, window.SourceClip))
                    {
                        string clipPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/松鼠_工具烘焙测试.anim");
                        SquirrelWorkbenchExport.BakeClip(window.PreviewModel, window.SourceClip,
                            (t, dt, reset) => motion.Evaluate(t, dt, window.Parameters, reset), clipPath);
                        report.animation = SquirrelWorkbenchExport.LastReport;
                    }
                    var preset = ScriptableObject.CreateInstance<SquirrelWorkbenchPreset>();
                    preset.sourceClip = window.SourceClip;
                    preset.sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(SquirrelLabBuilder.Root + "/Models/walk_biped.fbx");
                    preset.parameters = JsonUtility.FromJson<SquirrelWorkbenchParameters>(JsonUtility.ToJson(window.Parameters));
                    preset.parameters.strideLength = .55f; preset.boneOffsets = new[] { edit };
                    string presetPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/松鼠_参数测试.asset");
                    AssetDatabase.CreateAsset(preset, presetPath); AssetDatabase.SaveAssets();
                    AssetDatabase.ImportAsset(presetPath, ImportAssetOptions.ForceSynchronousImport);
                    var loaded = AssetDatabase.LoadAssetAtPath<SquirrelWorkbenchPreset>(presetPath);
                    report.presetReloaded = loaded != null && Mathf.Abs(loaded.parameters.strideLength - .55f) < .00001f
                        && loaded.boneOffsets.Length == 1 && loaded.boneOffsets[0].path == edit.path && loaded.sourceClip == window.SourceClip;
                }
                finally { UnityEngine.Object.DestroyImmediate(scratch); }
                report.sourceHashAfter = Hash(sourcePath);
                report.originalAssetUnchanged = report.sourceHashBefore == report.sourceHashAfter;
                report.passed = report.editorWindowLive && report.originalAssetUnchanged && report.normalizedWeights
                    && report.boneEditChangedBind && report.restMeshPreserved && report.presetReloaded
                    && report.animation.passed && report.model.passed;
                if (!report.passed) throw new InvalidOperationException("Workbench acceptance failed; inspect report.");
            }
            catch (Exception exception) { report.error = exception.ToString(); throw; }
            finally
            {
                File.WriteAllText(Review + "/verification.json", JsonUtility.ToJson(report, true));
                window.ResetAfterCheck();
            }
            Debug.Log("Animation workbench save/reload, bind edit, weights and motion checks passed.");
        }

        public static void CaptureWindow()
        {
            Directory.CreateDirectory(Review);
            var window = SquirrelAnimationWorkbench.Open();
            Rect rect = window.position;
            int width = Mathf.RoundToInt(rect.width), height = Mathf.RoundToInt(rect.height);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                texture.SetPixels(UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(rect.position, width, height));
                texture.Apply();
                File.WriteAllBytes(Review + "/workbench_window_" + DateTime.Now.ToString("HHmmss") + ".png", texture.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            var preview = window.CapturePreview(1024, 768);
            try { File.WriteAllBytes(Review + "/workbench_preview.png", preview.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(preview); }
        }

        public static void CheckWalk()
        {
            var window = SquirrelAnimationWorkbench.Open();
            if (window.PreviewModel == null) window.SetPreviewForCheck(0);
            var walk = AssetDatabase.LoadAllAssetsAtPath(SquirrelLabBuilder.Root + "/Models/walk_biped.fbx")
                .OfType<AnimationClip>().Single(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            SquirrelWorkbenchMotionChecks.Run(window.PreviewModel, walk);
        }

        static string Hash(string path)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
    }
}
