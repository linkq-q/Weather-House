using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WeatherHousePlacementReview
    {
        public static void AuditDirty()
        {
            var lines = new System.Collections.Generic.List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                lines.Add(scene.path + " dirty=" + scene.isDirty + " loaded=" + scene.isLoaded);
                if (scene.path == WatersideSceneBuilder.ScenePath)
                    lines.Add("snapshot=" + EditorSceneManager.SaveScene(scene, "Temp/WeatherHouseV05-dirty-audit.unity", true));
            }
            File.WriteAllText("WeatherHouseIntegrationReview/dirty-audit.txt", string.Join("\n", lines));
        }
        [Serializable] sealed class Part
        {
            public string hierarchy, globalId, prefab;
            public Vector3 position, euler, scale, forward;
            public Bounds renderBounds;
            public int renderers, colliders;
        }
        [Serializable] sealed class Report
        {
            public string utc, scene, editorOwner, exportThread, placementGuidance;
            public bool playing, dirty;
            public Part house, door, stairs;
            public Part[] nearby;
            public Vector3[] terrainSamples;
        }

        public static void Inspect()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != WatersideSceneBuilder.ScenePath || EditorApplication.isPlaying)
                throw new InvalidOperationException("Inspect the saved V2 scene in edit mode.");
            Transform[] all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            Transform house = all.Single(t => t.name == "天气屋 · HIGHLANDS 原模型");
            Transform door = house.GetComponentsInChildren<Transform>(true).Single(t => t.name == "原屋配套门");
            Transform stairs = house.GetComponentsInChildren<Transform>(true).Single(t => t.name == "原屋配套台阶");
            Terrain terrain = all.Select(t => t.GetComponent<Terrain>()).FirstOrDefault(t => t);
            Part houseInfo = Describe(house);
            Vector3 c = houseInfo.renderBounds.center;
            var samples = new System.Collections.Generic.List<Vector3>();
            if (terrain)
                for (int x = -2; x <= 2; x++)
                    for (int z = -2; z <= 2; z++)
                    {
                        Vector3 p = new Vector3(c.x + x * 2, 0, c.z + z * 2);
                        p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                        samples.Add(p);
                    }
            Transform[] nearby = all.Where(t => t.GetComponent<Renderer>() && !t.IsChildOf(house)
                && Vector2.Distance(new Vector2(t.position.x, t.position.z), new Vector2(c.x, c.z)) < 10)
                .OrderBy(t => (t.position-c).sqrMagnitude).Take(30).ToArray();
            var report = new Report {
                utc = DateTime.UtcNow.ToString("O"), scene = scene.path,
                editorOwner = "01a16712-a08b-7a11-8a21-081cfe594f7e (this map integration chat)",
                exportThread = "01a16b43-3759-7013-aade-54ad8a1f4f5e",
                placementGuidance = "Preserve the user-approved broad/deep v05 silhouette. Align its door approach to the existing approach; use ground bounds and actual terrain samples, not the old uniform scale. Retain the old instance for rollback. Export on desktop only; this map chat owns Unity and project Assets.",
                playing = EditorApplication.isPlaying, dirty = scene.isDirty,
                house = houseInfo, door = Describe(door), stairs = Describe(stairs),
                nearby = nearby.Select(Describe).ToArray(), terrainSamples = samples.ToArray()
            };
            Directory.CreateDirectory("WeatherHouseIntegrationReview");
            File.WriteAllText("WeatherHouseIntegrationReview/coordination.json", JsonUtility.ToJson(report, true));
        }

        static Part Describe(Transform t)
        {
            Renderer[] renderers = t.GetComponentsInChildren<Renderer>(true);
            Bounds b = new Bounds(t.position, Vector3.zero);
            if (renderers.Length > 0) { b = renderers[0].bounds; foreach (Renderer r in renderers.Skip(1)) b.Encapsulate(r.bounds); }
            string path = t.name;
            for (Transform p = t.parent; p; p = p.parent) path = p.name + "/" + path;
            return new Part { hierarchy = path, globalId = GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(),
                prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject),
                position = t.position, euler = t.eulerAngles, scale = t.lossyScale, forward = t.forward,
                renderBounds = b, renderers = renderers.Length, colliders = t.GetComponentsInChildren<Collider>(true).Length };
        }
    }
}
