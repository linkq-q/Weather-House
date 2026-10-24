using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    // Visual-only A/B. The imported materials, original colliders and grass are never edited.
    public static class WatersideDDrivePropsTrial
    {
        const string RootName = "AncientRuins 低墙 A_B 试验";
        const string Backup = "WatersideV2Review/ancient-props-trial.json";
        const string Pack = "Assets/Environment/DDriveLibrary/AncientRuins/PREFABS/Ruins/";

        [Serializable] sealed class RendererState
        {
            public string id;
            public bool enabled;
        }

        [Serializable] sealed class Trial
        {
            public string scene, view, wallId, colliderSignature, grassSignature, asset;
            public RendererState[] renderers;
            public Bounds localEnvelope;
        }

        public static string Apply()
        {
            const string view = "02";
            Scene scene = RequireScene();
            if (File.Exists(Backup) || FindRoot(scene)) throw new InvalidOperationException("Restore the existing props trial first.");
            Vector3 site = new Vector3(-4, 0, -5);
            Transform wall = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name == "断续矮石墙与残柱")
                .OrderBy(t => FlatDistance(t.position, site)).FirstOrDefault();
            if (!wall || FlatDistance(wall.position, site) > 9)
                throw new InvalidOperationException("The reviewed wall is missing near view " + view);
            MeshRenderer[] originals = wall.GetComponentsInChildren<MeshRenderer>(true);
            if (originals.Length == 0 || !originals.Any(r => r.enabled)) throw new InvalidOperationException("The original wall has no visible mesh.");
            string asset = Pack + "SM_wall_stoneblocks_ruins_03.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
            MeshFilter source = prefab ? prefab.GetComponent<MeshFilter>() : null;
            MeshRenderer renderer = prefab ? prefab.GetComponent<MeshRenderer>() : null;
            if (!source || !source.sharedMesh || !renderer || prefab.transform.childCount != 0)
                throw new InvalidOperationException("Expected the inspected single-mesh wall prefab: " + asset);
            if (renderer.sharedMaterials.Length == 0 || renderer.sharedMaterials.Any(m => !m || !m.shader || !m.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal)))
                throw new InvalidOperationException("The trial needs the verified URP materials: " + asset);
            Bounds envelope = InFrame(wall, originals);
            Vector3 sourceSize = source.sharedMesh.bounds.size;
            if (Mathf.Min(sourceSize.x, Mathf.Min(sourceSize.y, sourceSize.z)) < .001f)
                throw new InvalidOperationException("Degenerate imported wall bounds.");
            var trial = new Trial {
                scene = scene.path, view = view, wallId = Id(wall), asset = asset, localEnvelope = envelope,
                colliderSignature = ColliderSignature(wall), grassSignature = GrassSignature(scene),
                renderers = originals.Select(r => new RendererState { id = Id(r), enabled = r.enabled }).ToArray()
            };
            // Store stable scene IDs before hiding anything, so Restore survives a domain reload.
            if (trial.renderers.Any(r => !Resolve<MeshRenderer>(r.id)) || !Resolve<Transform>(trial.wallId))
                throw new InvalidOperationException("Save the existing scene objects before starting the A/B trial.");
            Directory.CreateDirectory(Path.GetDirectoryName(Backup));
            File.WriteAllText(Backup, JsonUtility.ToJson(trial, true));
            try
            {
                var root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.SetPositionAndRotation(wall.position, wall.rotation);
                root.transform.localScale = wall.lossyScale;
                Undo.RegisterCreatedObjectUndo(root, "AncientRuins wall trial");
                var visual = new GameObject("View " + view + " · " + prefab.name, typeof(MeshFilter), typeof(MeshRenderer));
                visual.transform.SetParent(root.transform, false);
                // Match the existing height and footprint, rather than trusting the FBX's unit scale.
                Vector3 size = new Vector3(envelope.size.x * .96f, envelope.size.y, Mathf.Min(envelope.size.z * .9f, .72f));
                visual.transform.localScale = new Vector3(size.x / sourceSize.x, size.y / sourceSize.y, size.z / sourceSize.z);
                visual.transform.localPosition = envelope.center - Vector3.Scale(source.sharedMesh.bounds.center, visual.transform.localScale);
                visual.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                visual.GetComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                foreach (MeshRenderer original in originals)
                {
                    Undo.RecordObject(original, "Hide original wall visual for A/B");
                    original.enabled = false;
                }
                EditorSceneManager.MarkSceneDirty(scene);
                string report = Verify();
                File.WriteAllText("WatersideV2Review/ancient-props-trial-checks.txt", report);
                return report;
            }
            catch
            {
                Restore();
                throw;
            }
        }

        public static string Restore()
        {
            Scene scene = RequireScene();
            if (!File.Exists(Backup))
            {
                if (FindRoot(scene)) throw new InvalidOperationException("Trial root exists without its restoration record.");
                return "No AncientRuins wall trial is active.";
            }
            Trial trial = ReadTrial(scene);
            MeshRenderer[] originals = trial.renderers.Select(r => Resolve<MeshRenderer>(r.id)).ToArray();
            if (originals.Any(r => !r)) throw new InvalidOperationException("The original scene wall changed; restore from the saved scene before deleting the trial record.");
            for (int i = 0; i < originals.Length; i++)
            {
                Undo.RecordObject(originals[i], "Restore original wall visual");
                originals[i].enabled = trial.renderers[i].enabled;
            }
            GameObject root = FindRoot(scene);
            if (root) Undo.DestroyObjectImmediate(root);
            Transform wall = Resolve<Transform>(trial.wallId);
            if (!wall || ColliderSignature(wall) != trial.colliderSignature || GrassSignature(scene) != trial.grassSignature)
                throw new InvalidOperationException("Visuals restored, but colliders or grass changed outside the trial; restoration record retained for inspection.");
            File.Delete(Backup);
            EditorSceneManager.MarkSceneDirty(scene);
            return "RESTORED view " + trial.view + ": original renderer states; no collider or grass changes. Scene not saved.";
        }

        public static string Verify()
        {
            Scene scene = RequireScene();
            Trial trial = ReadTrial(scene);
            Transform wall = Resolve<Transform>(trial.wallId);
            GameObject root = FindRoot(scene);
            if (!wall || !root || root.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException("Trial root/wall missing or trial introduced colliders.");
            if (trial.renderers.Any(s => !Resolve<MeshRenderer>(s.id) || Resolve<MeshRenderer>(s.id).enabled))
                throw new InvalidOperationException("An original wall renderer is still visible.");
            if (ColliderSignature(wall) != trial.colliderSignature || GrassSignature(scene) != trial.grassSignature)
                throw new InvalidOperationException("Original colliders or top grass changed.");
            MeshRenderer[] visuals = root.GetComponentsInChildren<MeshRenderer>();
            if (visuals.Length != 1 || !visuals[0].enabled) throw new InvalidOperationException("Expected one visible trial wall.");
            Bounds candidate = InFrame(wall, visuals), allowed = trial.localEnvelope;
            allowed.Expand(.004f);
            if (!allowed.Contains(candidate.min) || !allowed.Contains(candidate.max) || candidate.size.y < .1f)
                throw new InvalidOperationException("Imported wall is outside the original wall envelope or has invalid scale.");
            Material material = visuals[0].sharedMaterial;
            string materialReport = "\nmaterial=" + AssetDatabase.GetAssetPath(material) + "; shader=" + material.shader.name +
                "; BaseColor=" + material.GetColor("_BaseColor") + "; BaseMap=" + AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")) +
                "; tiling=" + material.GetTextureScale("_BaseMap") + "; normal=" + AssetDatabase.GetAssetPath(material.GetTexture("_BumpMap")) +
                "; normalScale=" + material.GetFloat("_BumpScale") + "; smoothness=" + material.GetFloat("_Smoothness");
            return "PASS visual-only AncientRuins trial view " + trial.view + "\nasset=" + trial.asset +
                "\noriginal=" + trial.localEnvelope + "\ncandidate=" + candidate + materialReport +
                "\nOriginal colliders and all stone-top grass unchanged; no new colliders.\nScene not saved. Compare the same camera/mood before keeping; Restore() returns original visuals.";
        }

        static Trial ReadTrial(Scene scene)
        {
            if (!File.Exists(Backup)) throw new InvalidOperationException("No active trial restoration record.");
            Trial trial = JsonUtility.FromJson<Trial>(File.ReadAllText(Backup));
            if (trial == null || trial.scene != scene.path || trial.renderers == null)
                throw new InvalidOperationException("Trial restoration record does not match this scene.");
            return trial;
        }

        static Scene RequireScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != WatersideSceneBuilder.ScenePath)
                throw new InvalidOperationException("Open the existing saved Waterside V2 scene in Edit mode.");
            return scene;
        }

        static GameObject FindRoot(Scene scene) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
        static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        static string Id(Object value) => GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();
        static T Resolve<T>(string id) where T : Object => GlobalObjectId.TryParse(id, out GlobalObjectId value)
            ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(value) as T : null;

        static Bounds InFrame(Transform frame, MeshRenderer[] renderers)
        {
            Bounds result = default;
            bool first = true;
            foreach (MeshRenderer renderer in renderers)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) throw new InvalidOperationException("Missing mesh on " + renderer.name);
                Bounds b = filter.sharedMesh.bounds;
                Matrix4x4 matrix = frame.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 p = matrix.MultiplyPoint3x4(new Vector3((corner & 1) == 0 ? b.min.x : b.max.x,
                        (corner & 2) == 0 ? b.min.y : b.max.y, (corner & 4) == 0 ? b.min.z : b.max.z));
                    if (first) { result = new Bounds(p, Vector3.zero); first = false; } else result.Encapsulate(p);
                }
            }
            if (first) throw new InvalidOperationException("Empty renderer bounds.");
            return result;
        }

        static string ColliderSignature(Transform wall)
        {
            var report = new StringBuilder();
            foreach (Collider c in wall.GetComponentsInChildren<Collider>(true).OrderBy(Id))
            {
                report.Append(Id(c)).Append(c.enabled).Append(c.isTrigger).Append(c.gameObject.activeSelf).Append(c.transform.localToWorldMatrix.ToString("F6"));
                if (c is MeshCollider mesh) report.Append(Id(mesh.sharedMesh));
                else report.Append(EditorJsonUtility.ToJson(c));
            }
            return Hash128.Compute(report.ToString()).ToString();
        }

        static string GrassSignature(Scene scene)
        {
            var report = new StringBuilder();
            foreach (MeshRenderer r in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true))
                .Where(r => r.name == "石顶土缝短草").OrderBy(Id))
                report.Append(Id(r)).Append(r.enabled).Append(r.gameObject.activeSelf).Append(r.transform.localToWorldMatrix.ToString("F6"));
            return Hash128.Compute(report.ToString()).ToString();
        }
    }
}
