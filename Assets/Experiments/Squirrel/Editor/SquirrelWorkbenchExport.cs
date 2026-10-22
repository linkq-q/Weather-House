using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHouseSquirrelLab.Editor
{
    public static class SquirrelWorkbenchExport
    {
        [Serializable] public sealed class ValidationReport
        {
            public bool passed, reloaded, finite, rootTransformPreserved;
            public string kind, assetPath, reportPath, error;
            public int frames, transforms, bones, curves, meshes, vertices;
            public float duration, maxPositionError, maxRotationErrorDegrees, maxScaleError;
            public bool loop;
        }
        public static ValidationReport LastReport { get; private set; }
        static readonly string[] Properties = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
            "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
            "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };

        public static AnimationClip BakeClip(GameObject previewRoot, AnimationClip source,
            Action<float, float, bool> evaluate, string assetPath)
        {
            RequireNewAsset(assetPath, ".anim", ".asset");
            if (previewRoot == null || source == null || evaluate == null || !Finite(source.length) || source.length <= 0)
                throw new ArgumentException("A preview model, non-empty source clip and evaluator are required.");
            Transform[] transforms = previewRoot.GetComponentsInChildren<Transform>(true).Where(t => t != previewRoot.transform).ToArray();
            if (transforms.Length == 0) throw new ArgumentException("The model has no child transforms to animate.");
            Vector3 rootPosition = previewRoot.transform.localPosition, rootScale = previewRoot.transform.localScale;
            Quaternion rootRotation = previewRoot.transform.localRotation;
            string[] paths = transforms.Select(t => AnimationUtility.CalculateTransformPath(t, previewRoot.transform)).ToArray();
            if (paths.Distinct().Count() != paths.Length) throw new InvalidOperationException("Duplicate transform paths cannot be animated safely.");
            Vector3[] oldPositions = transforms.Select(t => t.localPosition).ToArray(), oldScales = transforms.Select(t => t.localScale).ToArray();
            Quaternion[] oldRotations = transforms.Select(t => t.localRotation).ToArray();
            int frames = Mathf.CeilToInt(source.length * 60f - .0001f) + 1;
            var keys = new Keyframe[transforms.Length * 10][];
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe[frames];
            var report = new ValidationReport { kind = "animation", assetPath = assetPath, frames = frames,
                transforms = transforms.Length, bones = CountBones(previewRoot), duration = source.length };
            AnimationClip clip = null;
            try
            {
                float previousTime = 0;
                Quaternion[] previousRotations = new Quaternion[transforms.Length];
                for (int f = 0; f < frames; f++)
                {
                    float time = f == frames - 1 ? source.length : f / 60f;
                    evaluate(time, f == 0 ? 0 : time - previousTime, f == 0);
                    RequireRootUnchanged(previewRoot.transform, rootPosition, rootRotation, rootScale);
                    previousTime = time;
                    for (int b = 0; b < transforms.Length; b++)
                    {
                        Vector3 p = transforms[b].localPosition, s = transforms[b].localScale;
                        Quaternion q = transforms[b].localRotation;
                        if (f > 0 && Quaternion.Dot(q, previousRotations[b]) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        previousRotations[b] = q;
                        float[] values = { p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z };
                        for (int c = 0; c < values.Length; c++)
                        {
                            if (!Finite(values[c])) throw new InvalidOperationException("Non-finite pose at " + paths[b] + ", frame " + f);
                            keys[b * 10 + c][f] = new Keyframe(time, values[c]);
                        }
                    }
                }
                clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(assetPath), frameRate = 60f };
                for (int b = 0; b < transforms.Length; b++)
                    for (int c = 0; c < Properties.Length; c++)
                    {
                        var curve = new AnimationCurve(keys[b * 10 + c]);
                        for (int k = 0; k < curve.length; k++)
                        {
                            AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                            AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                        }
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(paths[b], typeof(Transform), Properties[c]), curve);
                    }
                clip.EnsureQuaternionContinuity();
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                var sourceSettings = AnimationUtility.GetAnimationClipSettings(source);
                settings.loopTime = sourceSettings.loopTime; settings.loopBlend = sourceSettings.loopBlend;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                report.loop = settings.loopTime;
                AssetDatabase.CreateAsset(clip, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                AnimationClip loaded = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
                if (loaded == null) throw new InvalidOperationException("Saved animation did not reload.");
                ValidateClip(loaded, previewRoot, paths, keys, report);
                report.reloaded = true; report.finite = true; report.passed = true;
                WriteReport(report);
                return loaded;
            }
            catch (Exception error)
            {
                report.error = error.Message; report.passed = false; WriteReport(report);
                if (clip != null && !AssetDatabase.Contains(clip)) Object.DestroyImmediate(clip);
                throw;
            }
            finally
            {
                for (int i = 0; i < transforms.Length; i++)
                    if (transforms[i] != null)
                    { transforms[i].localPosition = oldPositions[i]; transforms[i].localRotation = oldRotations[i]; transforms[i].localScale = oldScales[i]; }
                if (previewRoot != null)
                { previewRoot.transform.localPosition = rootPosition; previewRoot.transform.localRotation = rootRotation; previewRoot.transform.localScale = rootScale; }
            }
        }

        public static GameObject SaveModelCopy(GameObject previewRoot, string prefabPath)
        {
            RequireNewAsset(prefabPath, ".prefab");
            if (previewRoot == null) throw new ArgumentNullException(nameof(previewRoot));
            var report = new ValidationReport { kind = "model", assetPath = prefabPath,
                transforms = previewRoot.GetComponentsInChildren<Transform>(true).Length, bones = CountBones(previewRoot) };
            GameObject copy = null;
            try
            {
                // Caller restores the edited bind pose before cloning; source mesh assets remain untouched.
                copy = Object.Instantiate(previewRoot);
                copy.name = Path.GetFileNameWithoutExtension(prefabPath);
                copy.transform.localPosition = previewRoot.transform.localPosition;
                copy.transform.localRotation = previewRoot.transform.localRotation;
                copy.transform.localScale = previewRoot.transform.localScale;
                if (PrefabUtility.IsPartOfPrefabInstance(copy))
                    PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.hideFlags = HideFlags.None;
                    foreach (Component component in t.GetComponents<Component>())
                    {
                        if (component == null) throw new InvalidOperationException("Missing script in exported model.");
                        if (component is MonoBehaviour && ((component.hideFlags & HideFlags.DontSave) != 0
                            || component.GetType().Name == "SquirrelTailWave")) Object.DestroyImmediate(component);
                        else component.hideFlags = HideFlags.None;
                    }
                    if (!Finite(t.localPosition) || !Finite(t.localScale) || !Finite(t.localRotation))
                        throw new InvalidOperationException("Non-finite model transform: " + t.name);
                }
                SkinnedMeshRenderer[] renderers = copy.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (renderers.Length == 0) throw new InvalidOperationException("The model has no skinned mesh.");
                string folder = AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(prefabPath, null) + "_Meshes");
                string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
                var meshes = new Dictionary<Mesh, Mesh>();
                foreach (SkinnedMeshRenderer renderer in renderers)
                {
                    Mesh original = renderer.sharedMesh;
                    if (original == null) throw new InvalidOperationException("Skinned renderer has no mesh.");
                    if (!meshes.TryGetValue(original, out Mesh mesh))
                    {
                        mesh = Object.Instantiate(original); mesh.name = original.name + "_Edited"; mesh.hideFlags = HideFlags.None;
                        string meshPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/Mesh_" + meshes.Count + ".asset");
                        AssetDatabase.CreateAsset(mesh, meshPath); meshes.Add(original, mesh);
                        report.vertices += mesh.vertexCount;
                    }
                    renderer.sharedMesh = mesh;
                }
                report.meshes = meshes.Count;
                PrefabUtility.SaveAsPrefabAsset(copy, prefabPath, out bool success);
                if (!success) throw new InvalidOperationException("Unity did not save the model prefab.");
                AssetDatabase.SaveAssets();
                foreach (Mesh mesh in meshes.Values)
                    AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(mesh), ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                GameObject loaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (loaded == null) throw new InvalidOperationException("Saved model did not reload.");
                ValidateModel(previewRoot, loaded, report);
                report.reloaded = true; report.finite = true; report.passed = true; WriteReport(report);
                return loaded;
            }
            catch (Exception error) { report.error = error.Message; report.passed = false; WriteReport(report); throw; }
            finally { if (copy != null) Object.DestroyImmediate(copy); }
        }

        static void ValidateClip(AnimationClip clip, GameObject original, string[] paths, Keyframe[][] keys, ValidationReport report)
        {
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            report.curves = bindings.Length;
            if (bindings.Length != paths.Length * 10) throw new InvalidOperationException("Saved animation is missing transform curves.");
            if (bindings.Any(b => string.IsNullOrEmpty(b.path))) throw new InvalidOperationException("Animation must not control the model placement transform.");
            foreach (EditorCurveBinding binding in bindings)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length < 2 || curve.keys.Any(k => !Finite(k.time) || !Finite(k.value)
                    || !Finite(k.inTangent) || !Finite(k.outTangent)))
                    throw new InvalidOperationException("Invalid saved animation curve.");
            }
            GameObject replay = Object.Instantiate(original);
            try
            {
                foreach (Animator animator in replay.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                Transform[] bones = paths.Select(p => p.Length == 0 ? replay.transform : replay.transform.Find(p)).ToArray();
                if (bones.Any(b => b == null) || CountBones(replay) != report.bones) throw new InvalidOperationException("Reloaded clip hierarchy does not match.");
                Vector3 rootPosition = replay.transform.localPosition, rootScale = replay.transform.localScale;
                Quaternion rootRotation = replay.transform.localRotation;
                for (int f = 0; f < report.frames; f++)
                {
                    clip.SampleAnimation(replay, keys[0][f].time);
                    RequireRootUnchanged(replay.transform, rootPosition, rootRotation, rootScale);
                    for (int b = 0; b < paths.Length; b++)
                    {
                        int offset = b * 10;
                        Vector3 p = new Vector3(keys[offset][f].value, keys[offset + 1][f].value, keys[offset + 2][f].value);
                        Quaternion q = new Quaternion(keys[offset + 3][f].value, keys[offset + 4][f].value, keys[offset + 5][f].value, keys[offset + 6][f].value);
                        Vector3 s = new Vector3(keys[offset + 7][f].value, keys[offset + 8][f].value, keys[offset + 9][f].value);
                        report.maxPositionError = Mathf.Max(report.maxPositionError, Vector3.Distance(p, bones[b].localPosition));
                        report.maxRotationErrorDegrees = Mathf.Max(report.maxRotationErrorDegrees, Quaternion.Angle(q, bones[b].localRotation));
                        report.maxScaleError = Mathf.Max(report.maxScaleError, Vector3.Distance(s, bones[b].localScale));
                    }
                }
                if (report.maxPositionError > .0001f || report.maxRotationErrorDegrees > .1f || report.maxScaleError > .0001f)
                    throw new InvalidOperationException("Reloaded animation differs from the baked preview poses.");
                if (AnimationUtility.GetAnimationClipSettings(clip).loopTime != report.loop)
                    throw new InvalidOperationException("Saved animation loop setting changed.");
                report.rootTransformPreserved = true;
            }
            finally { Object.DestroyImmediate(replay); }
        }

        static void ValidateModel(GameObject source, GameObject saved, ValidationReport report)
        {
            if (saved.GetComponentsInChildren<Transform>(true).Length != report.transforms || CountBones(saved) != report.bones)
                throw new InvalidOperationException("Saved model hierarchy or bone count changed.");
            foreach (Transform original in source.GetComponentsInChildren<Transform>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(original, source.transform);
                Transform reopened = path.Length == 0 ? saved.transform : saved.transform.Find(path);
                if (reopened == null) throw new InvalidOperationException("Saved transform is missing: " + path);
                report.maxPositionError = Mathf.Max(report.maxPositionError, Vector3.Distance(original.localPosition, reopened.localPosition));
                report.maxRotationErrorDegrees = Mathf.Max(report.maxRotationErrorDegrees, Quaternion.Angle(original.localRotation, reopened.localRotation));
                report.maxScaleError = Mathf.Max(report.maxScaleError, Vector3.Distance(original.localScale, reopened.localScale));
            }
            if (report.maxPositionError > .0001f || report.maxRotationErrorDegrees > .1f || report.maxScaleError > .0001f)
                throw new InvalidOperationException("Saved rest pose differs from the edited preview.");
            SkinnedMeshRenderer[] a = source.GetComponentsInChildren<SkinnedMeshRenderer>(true), b = saved.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (a.Length != b.Length) throw new InvalidOperationException("Saved skinned renderer count changed.");
            for (int r = 0; r < a.Length; r++)
            {
                Mesh x = a[r].sharedMesh, y = b[r].sharedMesh;
                if (x == y || y == null || !AssetDatabase.GetAssetPath(y).EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                    || x.vertexCount != y.vertexCount || x.blendShapeCount != y.blendShapeCount || x.subMeshCount != y.subMeshCount)
                    throw new InvalidOperationException("Saved skinned mesh is not an independent copy.");
                if (!a[r].sharedMaterials.SequenceEqual(b[r].sharedMaterials)) throw new InvalidOperationException("Material references changed.");
                string[] sourceBones = a[r].bones.Select(t => AnimationUtility.CalculateTransformPath(t, source.transform)).ToArray();
                string[] savedBones = b[r].bones.Select(t => AnimationUtility.CalculateTransformPath(t, saved.transform)).ToArray();
                if (!sourceBones.SequenceEqual(savedBones)) throw new InvalidOperationException("Saved bone bindings changed.");
                Matrix4x4[] xp = x.bindposes, yp = y.bindposes;
                if (xp.Length != yp.Length) throw new InvalidOperationException("Saved bind pose count changed.");
                for (int i = 0; i < xp.Length; i++)
                    for (int j = 0; j < 16; j++)
                        if (!Finite(yp[i][j]) || Mathf.Abs(xp[i][j] - yp[i][j]) > .000001f)
                            throw new InvalidOperationException("Saved bind poses changed.");
                var xw = x.GetAllBoneWeights().ToArray(); var yw = y.GetAllBoneWeights().ToArray();
                if (!x.GetBonesPerVertex().ToArray().SequenceEqual(y.GetBonesPerVertex().ToArray()) || xw.Length != yw.Length)
                    throw new InvalidOperationException("Saved vertex influence counts changed.");
                for (int i = 0; i < xw.Length; i++)
                    if (!Finite(yw[i].weight) || xw[i].boneIndex != yw[i].boneIndex || Mathf.Abs(xw[i].weight - yw[i].weight) > .000001f)
                        throw new InvalidOperationException("Saved bone weights changed.");
                if (y.vertices.Any(v => !Finite(v))) throw new InvalidOperationException("Saved mesh has non-finite vertices.");
            }
        }

        static void RequireNewAsset(string path, params string[] extensions)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains('\\')
                || path.Split('/').Any(part => part == ".." || part == ".") || !extensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                throw new ArgumentException("Choose a new asset under Assets with extension " + string.Join(" or ", extensions));
            if (File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new IOException("Export never overwrites an existing asset: " + path);
            if (!AssetDatabase.IsValidFolder(Path.GetDirectoryName(path).Replace('\\', '/')))
                throw new DirectoryNotFoundException("Choose an existing Assets folder.");
        }
        static int CountBones(GameObject model) => model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .SelectMany(r => r.bones).Where(b => b != null).Distinct().Count();
        static void RequireRootUnchanged(Transform root, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (Vector3.Distance(root.localPosition, position) > .000001f || Quaternion.Angle(root.localRotation, rotation) > .01f
                || Vector3.Distance(root.localScale, scale) > .000001f)
                throw new InvalidOperationException("Animation changed the preview placement transform; keep root motion inside the skeleton.");
        }
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        static bool Finite(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w);
        static void WriteReport(ValidationReport report)
        {
            report.reportPath = AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(report.assetPath, ".validation.json"));
            File.WriteAllText(report.reportPath, JsonUtility.ToJson(report, true));
            AssetDatabase.ImportAsset(report.reportPath, ImportAssetOptions.ForceSynchronousImport);
            LastReport = report;
        }
    }
}
