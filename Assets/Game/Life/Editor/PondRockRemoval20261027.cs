using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.NightReview20261027
{
    // Explicit coordinator calls only. No initializer, polling, menu mutation or SaveScene.
    public static class PondRockRemoval20261027
    {
        public const string Stage = "LifeGameplayReview/20261027/pond-rock-removal";
        public const string ScenePath = "Assets/Scenes/WeatherHouse_WatersideV2.unity";
        public const int ExpectedCount = 44;
        const string P1 = "P1 潮洞石围 · 复用原厚礁岸 20261025", Rural = "乡村水岸增量 · 20261025";
        const string RockPrefab = "Assets/HIGHLANDS - Stylized Environment/Prefabs/Forest/Rock_1.prefab";
        static readonly string[,] Allowed = new string[,] {
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-1232310724-0", "潮洞 4 · 半浸潮池厚礁 1", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-1248807630-0", "潮洞 2 · 半浸潮池厚礁 1", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-1341092348-0", "潮洞 1 · 半浸潮池厚礁 2", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-137706970-0", "潮洞 3 · 半浸潮池厚礁 1", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-1384739551-0", "潮洞 4 · 半浸潮池厚礁 2", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-139289699-0", "潮洞 1 · 半浸潮池厚礁 2", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-1396010504-0", "潮洞 6 · 半浸潮池厚礁 1", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-1509363311-0", "潮洞 1 · 半浸潮池厚礁 1", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-1594464214-0", "潮洞 2 · 半浸潮池厚礁 1", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-1681487737-0", "潮洞 3 · 半浸潮池厚礁 2", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-1690502332-0", "潮洞 4 · 半浸潮池厚礁 2", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-169550666-0", "潮洞 6 · 半浸潮池厚礁 2", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-1722978163-0", "潮洞 5 · 半浸潮池厚礁 1", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-1724499643-0", "潮洞 5 · 半浸潮池厚礁 1", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-1795401480-0", "潮洞 4 · 半浸潮池厚礁 1", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-1803783260-0", "潮洞 1 · 半浸潮池厚礁 2", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-1963417832-0", "潮洞 3 · 半浸潮池厚礁 2", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-1984542537-0", "潮洞 2 · 半浸潮池厚礁 2", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-2022298063-0", "潮洞 6 · 半浸潮池厚礁 1", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-2117841470-0", "潮洞 4 · 半浸潮池厚礁 1", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-438189041-0", "潮洞 1 · 半浸潮池厚礁 1", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-5610936729227722695-1182790019", "新潮池断续礁石 · 4", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-566012408-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-5610936729227722695-1193079583", "新潮池断续礁石 · 13", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-566012408-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-5610936729227722695-1203681962", "新潮池断续礁石 · 1", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-566012408-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-5610936729227722695-1397713159", "新潮池断续礁石 · 15", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-566012408-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-5610936729227722695-1508830091", "新潮池断续礁石 · 9", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-566012408-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-5610936729227722695-163899940", "新潮池断续礁石 · 3", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-566012408-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-5610936729227722695-17940990", "新潮池断续礁石 · 11", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-566012408-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-5610936729227722695-185114511", "新潮池断续礁石 · 7", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-566012408-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-5610936729227722695-2129967159", "新潮池断续礁石 · 0", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-566012408-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-5610936729227722695-262756253", "新潮池断续礁石 · 5", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-566012408-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-5610936729227722695-481071587", "新潮池断续礁石 · 14", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-566012408-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-5610936729227722695-49152213", "新潮池断续礁石 · 2", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-566012408-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-5610936729227722695-557142659", "新潮池断续礁石 · 6", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-566012408-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-5610936729227722695-77177043", "新潮池断续礁石 · 8", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-566012408-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-621377067-0", "潮洞 3 · 半浸潮池厚礁 2", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-63766011-0", "潮洞 2 · 半浸潮池厚礁 2", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-702593139-0", "潮洞 5 · 半浸潮池厚礁 1", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-846794210-0", "潮洞 5 · 半浸潮池厚礁 2", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-860389508-0", "潮洞 3 · 半浸潮池厚礁 1", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-885120083-0", "潮洞 2 · 半浸潮池厚礁 1", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-927268977-0", "潮洞 1 · 半浸潮池厚礁 1", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-971946328-0", "潮洞 5 · 半浸潮池厚礁 2", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
            { "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-99521820-0", "潮洞 3 · 半浸潮池厚礁 1", "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-173843765-0" },
        };
        [Serializable] public sealed class Row
        {
            public string id, parentId, name, path, prefab, objectState;
            public int instanceId, descendants, renderers, colliders;
            public Vector3 position, localPosition, scale;
            public Quaternion rotation;
            public Bounds bounds;
            public Rect topViewportBounds;
            public bool active;
        }
        [Serializable] public sealed class TerrainRow { public string id, path, sha256; }
        [Serializable] public sealed class PreviewResult
        {
            public string status, utc, scene, sceneSha256, selectionHash, allStateHash, unownedStateHash, image, error;
            public bool playing, dirtyBefore, dirtyAfter;
            public int expectedCount;
            public List<Row> rows = new List<Row>();
            public List<TerrainRow> terrains = new List<TerrainRow>();
        }
        [Serializable] public sealed class ApplyResult
        {
            public string status, utc, previewPath, previewSha256, sceneSha256Before, sceneSha256After, unownedBefore, unownedAfter, image, error;
            public bool dirtyBefore, dirtyAfter, saveCalled;
            public int removedRoots;
            public List<string> removed = new List<string>();
        }
        static readonly HashSet<Type> StaticComponents = new HashSet<Type> {
            typeof(Transform), typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider), typeof(BoxCollider),
            typeof(SphereCollider), typeof(CapsuleCollider), typeof(LODGroup)
        };

        [MenuItem("Weather House/Review 20261027/Pond preview")]
        public static void PreviewMenu() => Preview(HashFile(ScenePath));

        [MenuItem("Weather House/Review 20261027/Pond apply reviewed preview")]
        public static void ApplyMenu()
        {
            Scene scene = Guard();
            string path = File.ReadAllText(Path.Combine(Stage, "latest-preview.txt")).Trim();
            string backup = Path.Combine(Stage, "before-removal-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".unity");
            bool wasDirty = scene.isDirty;
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, backup, true) || scene.isDirty != wasDirty)
                throw new InvalidOperationException("Could not preserve the current scene before deletion.");
            Apply(path, HashFile(path));
        }

        public static string Preview(string expectedSceneSha256, int expectedCount = ExpectedCount, bool captureTop = true)
        {
            Scene scene = Guard();
            Require(expectedCount == ExpectedCount && Allowed.GetLength(0) == ExpectedCount, "Only the fixed current 44-root whitelist is permitted.");
            Require(HashFile(ScenePath) == expectedSceneSha256, "Saved scene changed; obtain a new baseline instead of using an old map.");
            Directory.CreateDirectory(Stage);
            var result = new PreviewResult { status = "PREVIEWING", utc = DateTime.UtcNow.ToString("O"), scene = scene.path,
                sceneSha256 = expectedSceneSha256, playing = false, dirtyBefore = scene.isDirty, expectedCount = expectedCount };
            Transform[] all = All(scene), targets = Targets(scene);
            result.rows = targets.Select(RowOf).OrderBy(row => row.id, StringComparer.Ordinal).ToList();
            var excluded = DescendantIds(targets);
            result.allStateHash = StateHash(all, new HashSet<string>());
            result.unownedStateHash = StateHash(all, excluded);
            result.selectionHash = HashText(JsonUtility.ToJson(new Rows { rows = result.rows }));
            result.terrains = Terrains(all);
            string folder = Path.Combine(Stage, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-preview"); Directory.CreateDirectory(folder);
            try
            {
                if (captureTop) result.image = CaptureTop(scene, Path.Combine(folder, "top-before.png"), result.rows);
                Require(HashFile(ScenePath) == result.sceneSha256, "Scene was saved during Preview; rerun with the new baseline.");
                Require(StateHash(All(scene), new HashSet<string>()) == result.allStateHash, "Preview scene-state guard failed; no deletion was attempted.");
                result.dirtyAfter = scene.isDirty;
                Require(result.dirtyAfter == result.dirtyBefore, "Preview dirty state changed unexpectedly; no deletion was attempted.");
                result.status = "PASS_READ_ONLY_44_ROOTS_PREVIEW_APPLY_NOT_CALLED";
            }
            catch (Exception e) { result.status = "FAIL_PREVIEW_NO_APPLY"; result.error = e.ToString(); result.dirtyAfter = scene.isDirty; }
            string path = Path.Combine(folder, "preview.json"); File.WriteAllText(path, JsonUtility.ToJson(result, true));
            WriteHtml(Path.Combine(folder, "preview.html"), result);
            File.WriteAllText(Path.Combine(Stage, "latest-preview.txt"), path);
            if (result.status.StartsWith("FAIL", StringComparison.Ordinal)) throw new InvalidOperationException(result.error + "\nReceipt: " + path);
            return path;
        }

        public static string Apply(string previewPath, string expectedPreviewSha256, int expectedCount = ExpectedCount)
        {
            Scene scene = Guard();
            string full = Path.GetFullPath(previewPath), stage = Path.GetFullPath(Stage) + Path.DirectorySeparatorChar;
            Require(full.StartsWith(stage, StringComparison.OrdinalIgnoreCase), "Preview must be inside the owned pond-rock-removal staging directory.");
            Require(HashFile(full) == expectedPreviewSha256, "Preview manifest bytes changed; Apply refused.");
            PreviewResult preview = JsonUtility.FromJson<PreviewResult>(File.ReadAllText(full));
            Require(preview != null && preview.status == "PASS_READ_ONLY_44_ROOTS_PREVIEW_APPLY_NOT_CALLED", "A passing explicit Preview is required.");
            Require(expectedCount == ExpectedCount && preview.expectedCount == ExpectedCount && preview.rows.Count == ExpectedCount, "Exact reviewed count must remain 44.");
            Require(preview.scene == scene.path && HashFile(ScenePath) == preview.sceneSha256, "Scene disk baseline changed; Preview again.");
            Transform[] all = All(scene), targets = Targets(scene);
            List<Row> now = targets.Select(RowOf).OrderBy(row => row.id, StringComparer.Ordinal).ToList();
            Require(HashText(JsonUtility.ToJson(new Rows { rows = now })) == preview.selectionHash, "A selected rock or its parent/geometry/transform changed; Preview again.");
            Require(StateHash(all, new HashSet<string>()) == preview.allStateHash && scene.isDirty == preview.dirtyAfter, "Scene memory changed after Preview; Preview again.");
            Require(JsonUtility.ToJson(new TerrainRows { rows = Terrains(all) }) == JsonUtility.ToJson(new TerrainRows { rows = preview.terrains }), "Terrain baseline changed; Apply refused.");
            var excluded = DescendantIds(targets);
            var result = new ApplyResult { status = "APPLYING", utc = DateTime.UtcNow.ToString("O"), previewPath = full,
                previewSha256 = expectedPreviewSha256, sceneSha256Before = preview.sceneSha256, dirtyBefore = scene.isDirty,
                unownedBefore = StateHash(all, excluded), saveCalled = false };
            Require(result.unownedBefore == preview.unownedStateHash, "Unowned baseline changed.");
            Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("删除截图六处浅塘周边石头");
            try
            {
                // The list contains exact complete static rock roots, never a whole environment parent.
                foreach (Transform target in targets) { result.removed.Add(Id(target)); Undo.DestroyObjectImmediate(target.gameObject); }
                result.removedRoots = targets.Length;
                Require(AllowedIds().All(id => Resolve(id) == null), "At least one selected root remains after deletion.");
                result.unownedAfter = StateHash(All(scene), excluded);
                Require(result.unownedAfter == result.unownedBefore, "An unowned object changed; reverting this tool's Undo group.");
                Require(JsonUtility.ToJson(new TerrainRows { rows = Terrains(All(scene)) }) == JsonUtility.ToJson(new TerrainRows { rows = preview.terrains }), "Terrain changed; reverting this tool's Undo group.");
                result.sceneSha256After = HashFile(ScenePath);
                Require(result.sceneSha256After == result.sceneSha256Before, "Scene was unexpectedly saved; inspect before proceeding.");
                Undo.CollapseUndoOperations(undo);
                result.status = "PASS_APPLIED_44_ROCK_ROOTS_UNSAVED_UNOWNED_UNCHANGED";
            }
            catch (Exception e)
            {
                Undo.RevertAllDownToGroup(undo); result.status = "FAIL_OWN_UNDO_GROUP_REVERTED"; result.error = e.ToString();
                result.unownedAfter = StateHash(All(scene), excluded); result.sceneSha256After = HashFile(ScenePath);
            }
            result.dirtyAfter = scene.isDirty;
            string folder = Path.GetDirectoryName(full);
            if (result.status.StartsWith("PASS", StringComparison.Ordinal))
            {
                try { result.image = CaptureTop(scene, Path.Combine(folder, "top-after.png"), null); }
                catch (Exception e) { result.error = "Deletion guards passed; optional after capture failed: " + e.Message; }
            }
            string path = Path.Combine(folder, "apply-result.json"); File.WriteAllText(path, JsonUtility.ToJson(result, true));
            if (result.status.StartsWith("FAIL", StringComparison.Ordinal)) throw new InvalidOperationException(result.error + "\nReceipt: " + path);
            return path;
        }

        static Scene Guard()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating,
                "Stable Edit mode required. Only the root coordinator may stop Play or refresh; this tool never does.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.path == ScenePath && SceneManager.sceneCount == 1, "The sole formal WatersideV2 scene must be active.");
            return scene;
        }
        static Transform[] Targets(Scene scene)
        {
            var targets = new List<Transform>();
            for (int i = 0; i < Allowed.GetLength(0); i++)
            {
                Transform item = Resolve(Allowed[i, 0]);
                Require(item && item.gameObject.scene == scene, "Whitelisted rock missing: " + Allowed[i, 0]);
                Require(item.name == Allowed[i, 1] && item.parent && Id(item.parent) == Allowed[i, 2], "Rock name/parent changed: " + Allowed[i, 1]);
                bool rural = item.name.StartsWith("新潮池断续礁石 · ", StringComparison.Ordinal);
                Require(item.parent.name == (rural ? Rural : P1) && item.parent.parent == null, "Unexpected environment parent for " + item.name);
                Require(item.gameObject.activeInHierarchy && item.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled && r.gameObject.activeInHierarchy), "Rock was disabled; Preview requires a fresh reviewed whitelist.");
                if (rural) Require(PrefabUtility.GetNearestPrefabInstanceRoot(item.gameObject) == item.gameObject && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(item.gameObject) == RockPrefab, "Unexpected rock prefab.");
                else Require(item.name.StartsWith("潮洞 ", StringComparison.Ordinal) && item.name.Contains("半浸潮池厚礁"), "Unexpected P1 non-rock child.");
                Require(item.GetComponentsInChildren<Component>(true).All(component => component && StaticComponents.Contains(component.GetType())), "Rock contains unsupported interactive/water/plant components: " + item.name);
                Bounds b = BoundsOf(item);
                Require(b.min.x >= 58 && b.max.x <= 88 && b.min.z >= -55 && b.max.z <= -10 && b.min.y >= -3 && b.max.y <= 5,
                    "Rock left the screenshot's six-pool envelope: " + item.name);
                targets.Add(item);
            }
            Require(targets.Select(Id).Distinct().Count() == ExpectedCount && targets.All(t => !targets.Any(other => t != other && t.IsChildOf(other))), "Duplicate or nested targets.");
            return targets.ToArray();
        }
        static IEnumerable<string> AllowedIds() { for (int i = 0; i < Allowed.GetLength(0); i++) yield return Allowed[i, 0]; }
        static Transform Resolve(string id) { GlobalObjectId parsed; return GlobalObjectId.TryParse(id, out parsed) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) as Transform : null; }
        static string Id(Object obj) => GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
        static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
        static Transform[] All(Scene scene) => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).Where(t => (t.gameObject.hideFlags & HideFlags.DontSave) == 0).ToArray();
        static HashSet<string> DescendantIds(IEnumerable<Transform> roots) => new HashSet<string>(roots.SelectMany(t => t.GetComponentsInChildren<Transform>(true)).Select(Id));
        static Bounds BoundsOf(Transform t)
        { Renderer[] renderers = t.GetComponentsInChildren<Renderer>(true); Require(renderers.Length > 0, "No rock geometry: " + t.name); Bounds b = renderers[0].bounds; foreach (Renderer r in renderers.Skip(1)) b.Encapsulate(r.bounds); return b; }
        static Row RowOf(Transform t) => new Row { id = Id(t), parentId = Id(t.parent), name = t.name, path = PathOf(t),
            instanceId = t.GetInstanceID(), prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject),
            position = t.position, localPosition = t.localPosition, scale = t.localScale, rotation = t.rotation, bounds = BoundsOf(t),
            descendants = t.GetComponentsInChildren<Transform>(true).Length, renderers = t.GetComponentsInChildren<Renderer>(true).Length,
            colliders = t.GetComponentsInChildren<Collider>(true).Length, active = t.gameObject.activeInHierarchy,
            objectState = StateHash(t.GetComponentsInChildren<Transform>(true), new HashSet<string>()) };
        static List<TerrainRow> Terrains(IEnumerable<Transform> all) => all.Select(t => t.GetComponent<Terrain>()).Where(t => t && t.terrainData)
            .Select(t => new TerrainRow { id = Id(t), path = AssetDatabase.GetAssetPath(t.terrainData), sha256 = HashFile(AssetDatabase.GetAssetPath(t.terrainData)) }).OrderBy(t => t.id, StringComparer.Ordinal).ToList();
        static string StateHash(IEnumerable<Transform> all, HashSet<string> excluded)
        {
            string[] rows = all.Where(t => t && !excluded.Contains(Id(t))).OrderBy(Id, StringComparer.Ordinal).Select(t => {
                GameObject g = t.gameObject;
                // Ignore only removed children. Surviving sibling order, all TRS and components remain guarded.
                string children = string.Join(",", t.Cast<Transform>().Select(Id).Where(id => !excluded.Contains(id)));
                string components = string.Join("|", t.GetComponents<Component>().Where(c => c && !(c is Transform)).Select(c => c.GetType().FullName + ":" + EditorJsonUtility.ToJson(c)));
                return Id(t) + "|" + (t.parent ? Id(t.parent) : "") + "|" + g.name + "|" + g.activeSelf + "|" + g.layer + "|" + g.tag + "|" + g.isStatic + "|" +
                    JsonUtility.ToJson(new Pose { p = t.localPosition, q = t.localRotation, s = t.localScale }) + "|" + children + "|" + components;
            }).ToArray();
            return HashText(string.Join("\n", rows));
        }
        [Serializable] sealed class Pose { public Vector3 p, s; public Quaternion q; }
        [Serializable] sealed class Rows { public List<Row> rows; }
        [Serializable] sealed class TerrainRows { public List<TerrainRow> rows; }
        static string HashFile(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
        static string HashText(string value) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", ""); }
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

        static string CaptureTop(Scene scene, string filename, List<Row> rows)
        {
            GameObject go = null; Texture2D pixels = null; RenderTexture target = null; RenderTexture previous = RenderTexture.active;
            try
            {
                Camera source = SceneView.lastActiveSceneView ? SceneView.lastActiveSceneView.camera : All(scene).Select(t => t.GetComponent<Camera>()).FirstOrDefault(c => c && c.enabled);
                Require(source, "No existing camera to copy for the optional top view.");
                go = new GameObject("Temporary pond-rock verification camera") { hideFlags = HideFlags.HideAndDontSave };
                Camera camera = go.AddComponent<Camera>(); camera.CopyFrom(source); camera.enabled = false;
                var oldData = source.GetComponent<UniversalAdditionalCameraData>(); var data = go.AddComponent<UniversalAdditionalCameraData>();
                if (oldData) EditorUtility.CopySerialized(oldData, data); data.renderType = CameraRenderType.Base; data.cameraStack.Clear();
                camera.transform.position = new Vector3(73, 85, -33); camera.transform.rotation = Quaternion.Euler(90, 0, 0);
                camera.orthographic = true; camera.orthographicSize = 25; camera.aspect = 4f / 3; camera.nearClipPlane = .2f; camera.farClipPlane = 200;
                target = RenderTexture.GetTemporary(1600, 1200, 24, RenderTextureFormat.ARGB32);
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                Require(RenderPipeline.SupportsRenderRequest(camera, request), "URP SingleCameraRequest unavailable.");
                RenderPipeline.SubmitRenderRequest(camera, request); RenderTexture.active = target;
                pixels = new Texture2D(1600, 1200, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                pixels.ReadPixels(new Rect(0, 0, 1600, 1200), 0, 0); pixels.Apply(false, false); File.WriteAllBytes(filename, pixels.EncodeToPNG());
                if (rows != null) foreach (Row row in rows)
                {
                    Vector3 min = row.bounds.min, max = row.bounds.max; float x0 = 1, y0 = 1, x1 = 0, y1 = 0;
                    for (int i = 0; i < 8; i++) { Vector3 v = camera.WorldToViewportPoint(new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z)); x0 = Mathf.Min(x0, v.x); y0 = Mathf.Min(y0, v.y); x1 = Mathf.Max(x1, v.x); y1 = Mathf.Max(y1, v.y); }
                    row.topViewportBounds = Rect.MinMaxRect(x0, y0, x1, y1);
                }
                return filename;
            }
            finally { RenderTexture.active = previous; if (pixels) Object.DestroyImmediate(pixels); if (target) RenderTexture.ReleaseTemporary(target); if (go) Object.DestroyImmediate(go); }
        }
        static void WriteHtml(string filename, PreviewResult result)
        {
            var html = new StringBuilder("<!doctype html><meta charset='utf-8'><title>浅塘石头删除预览</title><style>body{font:16px/1.8 sans-serif;max-width:1200px;margin:30px auto;padding:0 20px;color:#183e31}table{border-collapse:collapse;width:100%}td,th{padding:8px;border-bottom:1px solid #ddd;text-align:left}code{overflow-wrap:anywhere}img{width:100%}.map{position:relative}.map svg{position:absolute;inset:0;width:100%;height:100%}svg rect{fill:none;stroke:#ffb000;stroke-width:3}svg text{fill:#fff;stroke:#1b3025;stroke-width:3;paint-order:stroke;font-size:20px}</style><h1>截图浅塘石头删除预览</h1><p>仅删除下列完整静态石头根物件；水面、地形、植物和其他岸石保留。当前为预览，未执行删除或保存。</p>");
            html.Append("<p>状态：" + result.status + "；物件数：" + result.rows.Count + "；场景 SHA <code>" + result.sceneSha256 + "</code></p>");
            if (!string.IsNullOrEmpty(result.image))
            {
                html.Append("<div class='map'><img src='top-before.png'><svg viewBox='0 0 1600 1200'>");
                for (int i = 0; i < result.rows.Count; i++) { Rect r = result.rows[i].topViewportBounds; html.AppendFormat(CultureInfo.InvariantCulture, "<rect x='{0:F1}' y='{1:F1}' width='{2:F1}' height='{3:F1}'/><text x='{0:F1}' y='{1:F1}'>{4}</text>", r.xMin * 1600, (1 - r.yMax) * 1200, r.width * 1600, r.height * 1200, i + 1); }
                html.Append("</svg></div>");
            }
            html.Append("<p><a href='preview.json'>完整清单与包围框 JSON</a></p><table><tr><th>序号</th><th>Hierarchy 路径</th><th>位置</th><th>GlobalObjectId</th></tr>");
            for (int i = 0; i < result.rows.Count; i++) { Row r = result.rows[i]; html.Append("<tr><td>" + (i + 1) + "</td><td>" + System.Security.SecurityElement.Escape(r.path) + "</td><td>" + r.position.ToString("F3") + "</td><td><code>" + r.id + "</code></td></tr>"); }
            html.Append("</table><p>Apply 需要本 preview.json 的 SHA、固定 44 个白名单物件、完整选中状态与场景内存/磁盘均未变化；删除后仍不保存场景。</p>");
            File.WriteAllText(filename, html.ToString());
        }
    }
}
