using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WeatherHouseV05Placement
    {
        const string Review = "WeatherHouseIntegrationReview";
        const string Name = "天气屋 · v05 苔顶木构";
        [Serializable] sealed class Move { public string id; public Vector3 position; }
        [Serializable] sealed class Backup { public string oldId; public bool oldActive; public List<Move> animals = new List<Move>(); }
        [Serializable] sealed class Report
        {
            public string utc, status;
            public Vector3 position, scale, door, frontStep;
            public Bounds bounds;
            public float frontStepGroundGap, maximumStepRise;
            public int movedAnimals;
        }

        public static void Apply()
        {
            Scene scene = RequireScene();
            if (Find(Name) || File.Exists(Review + "/placement-backup.json")) throw new InvalidOperationException("Restore the existing house trial first.");
            Transform old = Find("天气屋 · HIGHLANDS 原模型");
            if (!old) throw new InvalidOperationException("The inspected old house is missing.");
            Transform stairs = old.GetComponentsInChildren<Transform>(true).Single(t => t.name == "原屋配套台阶");
            Bounds steps = BoundsOf(stairs);
            Terrain terrain = Object.FindObjectOfType<Terrain>();
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(WeatherHouseV05Import.Prefab);
            if (!source) throw new InvalidOperationException("Build and check the imported prefab first.");
            var backup = new Backup { oldId = GlobalObjectId.GetGlobalObjectIdSlow(old).ToString(), oldActive = old.gameObject.activeSelf };
            Directory.CreateDirectory(Review);
            File.WriteAllText(Review + "/placement-backup.json", JsonUtility.ToJson(backup, true));
            var placed = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            try
            {
                placed.name = Name;
                placed.transform.SetParent(old.parent, true);
                Transform ground = Anchor(placed.transform, "WH_GroundOrigin");
                Transform front = Anchor(placed.transform, "WH_FrontDirection");
                Vector3 forward = Vector3.ProjectOnPlane(front.position - ground.position, Vector3.up).normalized;
                placed.transform.rotation = Quaternion.FromToRotation(forward, Vector3.back) * placed.transform.rotation;
                Transform frontStep = Anchor(placed.transform, "WH_FrontStepGround");
                Vector3 target = new Vector3(steps.center.x, 0, steps.min.z);
                target.y = Ground(terrain, target) + .06f;
                placed.transform.position += target - frontStep.position;
                old.gameObject.SetActive(false);
                Physics.SyncTransforms();
                Bounds envelope = BoundsOf(placed.transform);
                foreach (WildlifeAgent agent in Object.FindObjectsOfType<WildlifeAgent>())
                {
                    if (agent.locomotion != WildlifeLocomotion.Ground || !Inside(envelope, agent.transform.position, .4f)) continue;
                    Vector3 original = agent.transform.position;
                    WildlifePopulation population = agent.GetComponentInParent<WildlifePopulation>();
                    bool found = false;
                    for (int radius = 1; radius <= 18 && !found; radius++)
                        for (int sample = 0; sample < 24 && !found; sample++)
                        {
                            float angle = sample * Mathf.PI / 12;
                            Vector3 p = original + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius * .6f;
                            if (Inside(envelope, p, .7f) || Vector2.Distance(new Vector2(p.x, p.z), new Vector2(agent.habitatCenter.x, agent.habitatCenter.z)) > agent.habitatRadius) continue;
                            if (!population.TryProjectPosition(agent, p, out p)) continue;
                            if (Physics.OverlapSphere(p + Vector3.up * .5f, agent.bodyRadius + .15f).Any(c => !(c is TerrainCollider) && !c.transform.IsChildOf(agent.transform))) continue;
                            backup.animals.Add(new Move { id = GlobalObjectId.GetGlobalObjectIdSlow(agent.transform).ToString(), position = original });
                            agent.transform.position = p; found = true;
                        }
                    if (!found) throw new InvalidOperationException("No clear spawn outside the larger house for " + agent.name);
                }
                File.WriteAllText(Review + "/placement-backup.json", JsonUtility.ToJson(backup, true));
                Physics.SyncTransforms();
                Check(placed.transform, terrain, backup.animals.Count);
                Undo.FlushUndoRecordObjects();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the new house placement.");
            }
            catch
            {
                foreach (Move move in backup.animals) { Transform t = Resolve(move.id); if (t) t.position = move.position; }
                old.gameObject.SetActive(backup.oldActive);
                Object.DestroyImmediate(placed);
                File.Delete(Review + "/placement-backup.json");
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                throw;
            }
        }

        public static void Restore()
        {
            Scene scene = RequireScene();
            Backup backup = JsonUtility.FromJson<Backup>(File.ReadAllText(Review + "/placement-backup.json"));
            Transform old = Resolve(backup.oldId);
            if (!old) throw new InvalidOperationException("Cannot resolve the saved original house.");
            Transform current = Find(Name);
            if (current) Object.DestroyImmediate(current.gameObject);
            old.gameObject.SetActive(backup.oldActive);
            foreach (Move move in backup.animals) { Transform t = Resolve(move.id); if (t) t.position = move.position; }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save house restore.");
            File.Delete(Review + "/placement-backup.json");
        }

        static void Check(Transform house, Terrain terrain, int moved)
        {
            Transform step = Anchor(house, "WH_FrontStepGround"), door = Anchor(house, "WH_DoorThreshold");
            Collider[] colliders = house.GetComponentsInChildren<Collider>();
            float previous = Ground(terrain, step.position), maximumRise = 0;
            for (int i = 0; i <= 16; i++)
            {
                Vector3 p = Vector3.Lerp(step.position + Vector3.back * .25f, door.position + Vector3.back * .15f, i / 16f);
                float h = Ground(terrain, p);
                var ray = new Ray(new Vector3(p.x, door.position.y + .35f, p.z), Vector3.down);
                foreach (Collider collider in colliders) if (collider.Raycast(ray, out RaycastHit hit, 4)) h = Mathf.Max(h, hit.point.y);
                maximumRise = Mathf.Max(maximumRise, h - previous); previous = h;
            }
            float gap = step.position.y - Ground(terrain, step.position);
            if (gap > .2f || gap < -.15f || maximumRise > .4f) throw new InvalidOperationException("The new house steps do not meet the existing ground: gap=" + gap + ", rise=" + maximumRise);
            File.WriteAllText(Review + "/placement-checks.json", JsonUtility.ToJson(new Report { utc = DateTime.UtcNow.ToString("O"), status = "PASS",
                position = house.position, scale = house.lossyScale, door = door.position, frontStep = step.position,
                bounds = BoundsOf(house), frontStepGroundGap = gap, maximumStepRise = maximumRise, movedAnimals = moved }, true));
        }
        static Scene RequireScene()
        {
            WatersideSceneBuilder.RequireSaved();
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != WatersideSceneBuilder.ScenePath) throw new InvalidOperationException("Open the saved V2 scene in edit mode.");
            return scene;
        }
        static Transform Find(string name) => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).SingleOrDefault(t => t.name == name);
        static Transform Anchor(Transform root, string name) => root.Find("LOD 0").GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
        static Transform Resolve(string id) => GlobalObjectId.TryParse(id, out GlobalObjectId parsed) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) as Transform : null;
        static float Ground(Terrain terrain, Vector3 p) => terrain.SampleHeight(p) + terrain.transform.position.y;
        static bool Inside(Bounds b, Vector3 p, float margin) => p.x > b.min.x - margin && p.x < b.max.x + margin && p.z > b.min.z - margin && p.z < b.max.z + margin;
        static Bounds BoundsOf(Transform root)
        {
            Renderer[] all = root.GetComponentsInChildren<Renderer>(true);
            Bounds b = all[0].bounds;
            foreach (Renderer r in all.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
