using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouse.WatersideV2.Editor
{
    // Only the four reviewed child transforms; the root model and collider keep their authored pose.
    public static class WatersideFenceLodRepair
    {
        internal const string ScenePath = "Assets/Scenes/WeatherHouse_WatersideV2.unity";
        const string SceneGuid = "0d5e02f552d97b34a8c818aceb185e42";
        const string Prefabs = "Assets/HIGHLANDS - Stylized Environment/Prefabs/Witches House/";
        const string Output = "JourneyReview/Revision_20261024/FenceZoomDiagnosis/Reports";

        internal sealed class Child
        {
            public ulong id; public Vector3 position; public Quaternion rotation;
            public Child(ulong id, Vector3 position, Quaternion rotation) { this.id = id; this.position = position; this.rotation = rotation; }
        }
        internal sealed class Spec
        {
            public ulong id; public string prefab; public Child[] children;
            public Spec(ulong id, string prefab, params Child[] children) { this.id = id; this.prefab = prefab; this.children = children; }
        }
        internal static readonly Spec[] Specs = {
            new Spec(1367981517313148989, "Fence_2", new Child(1337758303260693788, new Vector3(1.32f, .1f, .03f), new Quaternion(.003515143f, .31871074f, .0053251497f, .94783056f))),
            new Spec(1449887266701542226, "Fence_2", new Child(1473714116994883187, new Vector3(.36f, .17f, 2.08f), Quaternion.identity)),
            new Spec(8081516425864493565, "Fence_1",
                new Child(2787165626907305525, new Vector3(4.25f, .1159999f, 3.18f), new Quaternion(0, .13027255f, 0, .99147826f)),
                new Child(1602414679628147922, new Vector3(-9.92f, 1.61f, -24.94f), new Quaternion(0, .036980417f, 0, .999316f)))
        };
        internal sealed class Item
        {
            public LODGroup group; public GameObject prefab; public Transform[] children, sources; public bool[] needsChange;
        }
        [Serializable] public sealed class ChildReport
        {
            public string objectId, name; public Vector3 beforePosition, afterPosition, beforeScale, afterScale;
            public Quaternion beforeRotation, afterRotation; public bool changed;
        }
        [Serializable] public sealed class GroupReport
        {
            public string objectId, name; public Vector3 rootPosition, lodCenterBefore, lodCenterAfter;
            public float lodSizeBefore, lodSizeAfter; public Bounds[] rendererBounds; public Bounds colliderBounds;
        }
        [Serializable] public sealed class Report
        {
            public string utc, status; public bool saved; public GroupReport[] groups; public ChildReport[] children;
        }

        [MenuItem("Weather House/水岸/预览三组栏杆 LOD 修复")]
        public static void PreviewMenu() => Debug.Log(Preview());
        [MenuItem("Weather House/水岸/修复三组栏杆 LOD（不保存）")]
        public static void ApplyMenu() => Debug.Log(Apply());
        public static string Preview() => Write(CreateReport(Plan(), "PREVIEW"));

        public static string Apply()
        {
            Item[] items = Plan(); Scene scene = SceneManager.GetActiveScene();
            if (scene.isDirty) throw new InvalidOperationException("Resolve the dirty scene before this scoped fence repair.");
            Report report = CreateReport(items, "ALREADY_APPLIED");
            if (items.All(i => i.needsChange.All(v => !v))) return Write(report);
            Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("对齐三组栏杆的 LOD 子模型");
            Undo.RecordObjects(items.Select(i => (UnityEngine.Object)i.group).Concat(items.SelectMany(i => i.children)).ToArray(), "对齐栏杆 LOD");
            try
            {
                foreach (Item item in items)
                {
                    for (int c = 0; c < item.children.Length; c++)
                    {
                        if (!item.needsChange[c]) continue;
                        Transform target = item.children[c], source = item.sources[c];
                        target.localPosition = source.localPosition; target.localRotation = source.localRotation; target.localScale = source.localScale;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                    }
                    item.group.RecalculateBounds();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(item.group);
                }
                VerifyAligned(items);
                Undo.CollapseUndoOperations(undo);
            }
            catch { Undo.RevertAllDownToGroup(undo); throw; }
            EditorSceneManager.MarkSceneDirty(scene);
            report.status = "APPLIED_UNSAVED";
            for (int i = 0; i < items.Length; i++) FillAfter(report.groups[i], items[i]);
            return Write(report);
        }

        internal static Item[] Plan()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Fence migration requires Edit mode.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || AssetDatabase.AssetPathToGUID(scene.path) != SceneGuid) throw new InvalidOperationException("Expected the reviewed WatersideV2 scene.");
            var items = new List<Item>();
            foreach (Spec spec in Specs)
            {
                LODGroup group = Resolve<LODGroup>(spec.id); GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + spec.prefab + ".prefab");
                if (!group || group.gameObject.scene != scene || !prefab) throw new InvalidOperationException("Reviewed fence or source prefab is missing.");
                LOD[] actual = group.GetLODs(), sourceLods = prefab.GetComponent<LODGroup>().GetLODs();
                if (actual.Length != 3 || sourceLods.Length != 3) throw new InvalidOperationException("Fence LOD layout changed.");
                var item = new Item { group = group, prefab = prefab, children = new Transform[spec.children.Length], sources = new Transform[spec.children.Length], needsChange = new bool[spec.children.Length] };
                for (int c = 0; c < spec.children.Length; c++)
                {
                    Child before = spec.children[c]; Transform child = Resolve<Transform>(before.id), source = child ? prefab.transform.Find(child.name) : null;
                    if (!child || child.parent != group.transform || !source) throw new InvalidOperationException("Reviewed child identity or hierarchy changed.");
                    item.children[c] = child; item.sources[c] = source; item.needsChange[c] = NeedsChange(child, source, before);
                }
                for (int l = 0; l < 3; l++)
                {
                    if (actual[l].renderers.Length != 1 || sourceLods[l].renderers.Length != 1) throw new InvalidOperationException("Fence renderer layout changed.");
                    Renderer renderer = actual[l].renderers[0], source = sourceLods[l].renderers[0];
                    if (!renderer || !source || !renderer.GetComponent<MeshFilter>() || renderer.GetComponent<MeshFilter>().sharedMesh != source.GetComponent<MeshFilter>().sharedMesh ||
                        (l == 0 ? renderer.transform != group.transform : renderer.transform.parent != group.transform)) throw new InvalidOperationException("Fence LOD mesh or ownership changed.");
                    if (l > 0 && !item.children.Contains(renderer.transform) && !SamePose(renderer.transform, source.transform.localPosition, source.transform.localRotation, source.transform.localScale))
                        throw new InvalidOperationException("An unlisted LOD child was edited; nothing applied.");
                }
                if (item.children.Any(c => !actual.Skip(1).Any(l => l.renderers[0].transform == c))) throw new InvalidOperationException("A reviewed child is no longer used by its LODGroup.");
                MeshCollider collider = group.GetComponent<MeshCollider>(), sourceCollider = prefab.GetComponent<MeshCollider>();
                if (!collider || !sourceCollider || collider.sharedMesh != sourceCollider.sharedMesh || !collider.enabled || collider.isTrigger)
                    throw new InvalidOperationException("The existing root collider no longer matches the reviewed source.");
                items.Add(item);
            }
            return items.ToArray();
        }

        internal static bool NeedsChange(Transform child, Transform source, Child before)
        {
            if (SamePose(child, source.localPosition, source.localRotation, source.localScale)) return false;
            if (!SamePose(child, before.position, before.rotation, Vector3.one)) throw new InvalidOperationException("Child has new user edits; nothing applied: " + child.name);
            return true;
        }

        internal static void VerifyAligned(Item[] items)
        {
            foreach (Item item in items)
            {
                LOD[] actual = item.group.GetLODs(), source = item.prefab.GetComponent<LODGroup>().GetLODs();
                for (int l = 0; l < 3; l++)
                {
                    Renderer renderer = actual[l].renderers[0], original = source[l].renderers[0];
                    Matrix4x4 expected = item.group.transform.localToWorldMatrix * item.prefab.transform.worldToLocalMatrix * original.transform.localToWorldMatrix;
                    Bounds expectedBounds = TransformBounds(original.GetComponent<MeshFilter>().sharedMesh.bounds, expected);
                    if (!SameBounds(renderer.bounds, expectedBounds)) throw new InvalidOperationException("LOD remains displaced: " + renderer.name);
                }
                MeshCollider collider = item.group.GetComponent<MeshCollider>();
                if (!SameBounds(collider.bounds, TransformBounds(collider.sharedMesh.bounds, item.group.transform.localToWorldMatrix)))
                    throw new InvalidOperationException("Root collider bounds changed unexpectedly.");
            }
        }
        internal static bool SamePose(Transform t, Vector3 p, Quaternion q, Vector3 s) =>
            (t.localPosition - p).sqrMagnitude <= 1e-10f && (t.localScale - s).sqrMagnitude <= 1e-10f &&
            Mathf.Min(QuaternionDistance(t.localRotation, q), QuaternionDistance(t.localRotation, new Quaternion(-q.x, -q.y, -q.z, -q.w))) <= 1e-10f;
        static float QuaternionDistance(Quaternion a, Quaternion b) => (a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z)+(a.w-b.w)*(a.w-b.w);
        internal static bool SameBounds(Bounds a, Bounds b) => (a.center-b.center).sqrMagnitude < 1e-6f && (a.size-b.size).sqrMagnitude < 1e-6f;
        internal static Bounds TransformBounds(Bounds local, Matrix4x4 matrix)
        {
            Bounds result = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
            for (int c = 0; c < 8; c++) result.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                new Vector3((c&1)==0?-1:1, (c&2)==0?-1:1, (c&4)==0?-1:1))));
            return result;
        }
        static T Resolve<T>(ulong id) where T : UnityEngine.Object
        {
            if (!GlobalObjectId.TryParse("GlobalObjectId_V1-2-" + SceneGuid + "-" + id + "-0", out GlobalObjectId key)) throw new InvalidOperationException("Invalid reviewed GlobalObjectId.");
            T value = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(key) as T;
            if (!value || GlobalObjectId.GetGlobalObjectIdSlow(value).ToString() != key.ToString()) throw new InvalidOperationException("Reviewed GlobalObjectId is missing: " + key);
            return value;
        }
        static Report CreateReport(Item[] items, string status)
        {
            return new Report { utc = DateTime.UtcNow.ToString("O"), status = status,
                groups = items.Select(i => { var r = new GroupReport { objectId = GlobalObjectId.GetGlobalObjectIdSlow(i.group).ToString(), name = i.group.name,
                    rootPosition = i.group.transform.position, lodCenterBefore = i.group.localReferencePoint, lodSizeBefore = i.group.size }; FillAfter(r, i); return r; }).ToArray(),
                children = items.SelectMany(i => i.children.Select((c, n) => new ChildReport { objectId = GlobalObjectId.GetGlobalObjectIdSlow(c).ToString(), name = c.name,
                    beforePosition = c.localPosition, beforeRotation = c.localRotation, beforeScale = c.localScale,
                    afterPosition = i.sources[n].localPosition, afterRotation = i.sources[n].localRotation, afterScale = i.sources[n].localScale, changed = i.needsChange[n] })).ToArray() };
        }
        static void FillAfter(GroupReport r, Item i)
        { r.lodCenterAfter = i.group.localReferencePoint; r.lodSizeAfter = i.group.size; r.rendererBounds = i.group.GetLODs().Select(l => l.renderers[0].bounds).ToArray(); r.colliderBounds = i.group.GetComponent<MeshCollider>().bounds; }
        static string Write(Report report)
        {
            Directory.CreateDirectory(Output); string path = Path.Combine(Output, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + report.status + "_" + Guid.NewGuid().ToString("N").Substring(0,8) + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true)); return Path.GetFullPath(path);
        }
    }
}
