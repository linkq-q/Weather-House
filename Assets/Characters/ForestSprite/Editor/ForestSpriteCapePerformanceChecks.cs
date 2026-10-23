using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ForestSpriteCharacter.Editor
{
    public static class ForestSpriteCapePerformanceChecks
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static string Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run isolated cape checks outside Play mode.");
            Scene active = SceneManager.GetActiveScene();
            bool wasDirty = active.isDirty;
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            Mesh mesh = null, replacementMesh = null;
            try
            {
                root = EditorUtility.CreateGameObjectWithHideFlags("Cape performance checks", HideFlags.HideAndDontSave);
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, preview);
                var contact = root.AddComponent<ForestSpriteCapeContact>();
                CheckVolumes(contact);

                var skin = root.AddComponent<SkinnedMeshRenderer>();
                var cape = root.AddComponent<ForestSpriteCapeCloth>();
                Transform[] bones = new Transform[4];
                for (int i = 0; i < bones.Length; i++)
                {
                    var bone = new GameObject("Bone " + i) { hideFlags = HideFlags.HideAndDontSave };
                    bone.transform.SetParent(root.transform, false);
                    bone.transform.SetLocalPositionAndRotation(new Vector3(i * .1f, .3f - i * .06f, i * .04f), Quaternion.Euler(i * 12, i * -7, i * 3));
                    bones[i] = bone.transform;
                }
                Vector3[] vertices = { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward };
                var weights = new BoneWeight[vertices.Length];
                for (int i = 0; i < weights.Length; i++) weights[i] = new BoneWeight {
                    boneIndex0 = i, boneIndex1 = (i + 1) % 4, boneIndex2 = (i + 2) % 4, boneIndex3 = (i + 3) % 4,
                    weight0 = .4f, weight1 = .3f, weight2 = .2f, weight3 = .1f };
                Matrix4x4[] bindposes = { Matrix4x4.identity, Matrix4x4.Translate(new Vector3(.03f, -.05f, .02f)), Matrix4x4.identity, Matrix4x4.Scale(new Vector3(.9f, 1.1f, 1)) };
                mesh = new Mesh { name = "Cape check mesh", vertices = vertices, boneWeights = weights, bindposes = bindposes };
                skin.sharedMesh = mesh; skin.bones = bones;
                Set(contact, "skin", skin); Set(contact, "sourceMesh", mesh);
                Set(contact, "restVertices", vertices); Set(contact, "weights", weights); Set(contact, "bindposes", bindposes);
                Set(contact, "matrices", new Matrix4x4[bones.Length]); Set(contact, "targets", new Vector3[vertices.Length]);
                Read(contact, bones);
                Vector3[] expected = OriginalTargets(vertices, weights, bindposes, bones);
                Require(TryShared(contact, skin, out Vector3[] shared), "Fresh same-pose targets were not reused.");
                for (int i = 0; i < expected.Length; i++) Require(expected[i].Equals(shared[i]), "Shared skinning changed vertex " + i);

                Set(cape, "<Contact>k__BackingField", contact);
                cape.guideEdgeVertices = new[] { 0, 1, 1, 2, 2, 3 };
                cape.guideEdgeRestLengths = new[] { 1f, 1.5f, .75f };
                typeof(ForestSpriteCapeCloth).GetMethod("MeasureGuideTargetStretch", Private).Invoke(cape, null);
                float[] ratios = new float[3];
                for (int i = 0; i < ratios.Length; i++) ratios[i] = Vector3.Distance(expected[cape.guideEdgeVertices[i * 2]], expected[cape.guideEdgeVertices[i * 2 + 1]]) / cape.guideEdgeRestLengths[i];
                Array.Sort(ratios);
                Require(cape.GuideTargetStretchP95.Equals(ratios[1]) && cape.GuideTargetStretchMaximum.Equals(ratios[2]), "Shared targets changed stretch diagnostics.");
                Require(Get(cape, "guideVertices") == null, "Stretch diagnostics redundantly initialized another skinning mesh.");

                bones[0].localPosition += Vector3.up * .01f;
                Require(!TryShared(contact, skin, out _), "Changed bone pose reused stale targets.");
                Read(contact, bones);
                var replacement = new GameObject("Replacement bone") { hideFlags = HideFlags.HideAndDontSave };
                replacement.transform.SetParent(root.transform, false);
                replacement.transform.SetLocalPositionAndRotation(bones[0].localPosition, bones[0].localRotation);
                var swapped = (Transform[])bones.Clone(); swapped[0] = replacement.transform;
                skin.bones = swapped;
                Require(!TryShared(contact, skin, out _), "Reassigned bone references reused stale targets.");
                Read(contact, swapped);
                Require(TryShared(contact, skin, out shared), "Reassigned bones did not become reusable after reskinning.");
                expected = OriginalTargets(vertices, weights, bindposes, swapped);
                for (int i = 0; i < expected.Length; i++) Require(expected[i].Equals(shared[i]), "Bone replacement changed skinning output.");
                replacementMesh = Object.Instantiate(mesh); skin.sharedMesh = replacementMesh;
                Require(!TryShared(contact, skin, out _), "Changed mesh reused stale targets.");
                skin.sharedMesh = mesh;
                Set(contact, "targetsFrame", Time.frameCount - 1);
                Require(!TryShared(contact, skin, out _), "Prior-frame targets were reused.");
            }
            finally
            {
                if (root) Object.DestroyImmediate(root);
                if (mesh) Object.DestroyImmediate(mesh);
                if (replacementMesh) Object.DestroyImmediate(replacementMesh);
                EditorSceneManager.ClosePreviewScene(preview);
            }
            Require(SceneManager.GetActiveScene() == active && active.isDirty == wasDirty, "Checks changed the active scene or its dirty state.");
            return "PASS cape: 1280 old/new penetration depth, normal and collider comparisons; proxy rename/name reuse; identical skinning/stretch values; pose, bone-reference, mesh and frame cache invalidation; active scene preserved. Native Cloth settings and post-Cloth sampling are unchanged.";
        }

        static void CheckVolumes(ForestSpriteCapeContact contact)
        {
            var setVolume = typeof(ForestSpriteCapeContact).GetMethod("SetVolume", Private);
            var penetration = typeof(ForestSpriteCapeContact).GetMethod("Penetration", Private);
            var random = new System.Random(90123);
            Vector3 a = new Vector3(-.2f, .3f, -.1f);
            Vector3[] ends = { a, a + Vector3.up * .9f, a + new Vector3(.2f, .6f, -.3f), a + Vector3.up * .1f, a + Vector3.right * .3f };
            float[] ra = { .15f, .15f, .1f, .3f, .25f }, rb = { .15f, .15f, .3f, .1f, .15f };
            for (int shape = 0; shape < ends.Length; shape++)
            {
                setVolume.Invoke(contact, new object[] { 0, a, ends[shape], ra[shape], rb[shape], "Body", "Shoulder" });
                for (int sample = 0; sample < 128; sample++)
                    for (int withMargin = 0; withMargin < 2; withMargin++)
                    {
                        Vector3 point = Vector3.Lerp(a, ends[shape], (float)random.NextDouble()) + new Vector3((float)random.NextDouble() - .5f, (float)random.NextDouble() - .5f, (float)random.NextDouble() - .5f) * .7f;
                        float margin = withMargin * .003f;
                        float expected = OriginalPenetration(a, ends[shape], ra[shape], rb[shape], point, margin, out Vector3 normal);
                        object[] args = { point, margin, Vector3.zero, "" };
                        float actual = (float)penetration.Invoke(contact, args);
                        Require(actual.Equals(expected) && normal.Equals((Vector3)args[2]) && (string)args[3] == (expected > 0 ? "Body -> Shoulder" : ""), "Precomputed proxy changed penetration, normal or collider.");
                    }
            }
            object[] atCentre = { a, 0f, Vector3.zero, "" };
            penetration.Invoke(contact, atCentre);
            string first = (string)atCentre[3];
            setVolume.Invoke(contact, new object[] { 0, a, ends[4], ra[4], rb[4], "Body", "Shoulder" });
            penetration.Invoke(contact, atCentre);
            Require(ReferenceEquals(first, atCentre[3]), "Unchanged proxy names allocated a new combined label.");
            setVolume.Invoke(contact, new object[] { 0, a, ends[4], ra[4], rb[4], "Renamed", "Shoulder" });
            penetration.Invoke(contact, atCentre);
            Require((string)atCentre[3] == "Renamed -> Shoulder", "Live collider rename was not reflected in diagnostics.");
        }

        static float OriginalPenetration(Vector3 a, Vector3 b, float ra, float rb, Vector3 point, float margin, out Vector3 normal)
        {
            Vector3 axis = b - a; float length = axis.magnitude, difference = rb - ra;
            Vector3 centre; float radius;
            if (length <= Mathf.Abs(difference) + .00001f)
            {
                bool useB = rb - Vector3.Distance(point, b) > ra - Vector3.Distance(point, a);
                centre = useB ? b : a; radius = useB ? rb : ra;
            }
            else
            {
                float z = Vector3.Dot(point - a, axis / length);
                float radial = (point - a - axis * (z / length)).magnitude;
                float t = Mathf.Clamp01((z + difference * radial / Mathf.Sqrt(length * length - difference * difference)) / length);
                centre = a + axis * t; radius = Mathf.Lerp(ra, rb, t);
            }
            Vector3 delta = point - centre; float distance = delta.magnitude, depth = radius + Mathf.Max(0, margin) - distance;
            normal = depth <= 0 ? Vector3.zero : distance > .000001f ? delta / distance : Vector3.up;
            return Mathf.Max(0, depth);
        }

        static Vector3[] OriginalTargets(Vector3[] vertices, BoneWeight[] weights, Matrix4x4[] bindposes, Transform[] bones)
        {
            var matrices = new Matrix4x4[bones.Length];
            for (int i = 0; i < matrices.Length; i++) matrices[i] = bones[i].localToWorldMatrix * bindposes[i];
            var result = new Vector3[vertices.Length];
            for (int i = 0; i < result.Length; i++)
            {
                BoneWeight w = weights[i]; Vector3 v = vertices[i];
                result[i] = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                    + matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
            }
            return result;
        }
        static object Get(object owner, string name) => owner.GetType().GetField(name, Private).GetValue(owner);
        static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private).SetValue(owner, value);
        static void Read(ForestSpriteCapeContact contact, Transform[] bones) => typeof(ForestSpriteCapeContact).GetMethod("ReadTargets", Private).Invoke(contact, new object[] { bones });
        static bool TryShared(ForestSpriteCapeContact contact, SkinnedMeshRenderer skin, out Vector3[] targets)
        {
            object[] args = { skin, null };
            bool result = (bool)typeof(ForestSpriteCapeContact).GetMethod("TryGetGuideTargets", Private).Invoke(contact, args);
            targets = (Vector3[])args[1]; return result;
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Cape performance check failed: " + message); }
    }
}
