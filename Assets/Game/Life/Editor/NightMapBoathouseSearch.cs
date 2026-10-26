using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.WatersideV2;

namespace WeatherHouse.NightReview20261027
{
    // Read-only search only. A shortlist is not permission to move a building, nor a Play walk test.
    [InitializeOnLoad]
    public static class NightMapBoathouseSearch
    {
        const string Stage = NightMapSnapshot.Staging;
        static double next;
        [Serializable] public sealed class Request { public string expectedSceneSha256; }
        [Serializable] public sealed class Candidate
        {
            public Vector3 rootBefore, rootAfter, doorLanding, exteriorStart, boatCenter, nearestRoad;
            public Quaternion rotationBefore, rotationAfter;
            public Vector3 localScaleBefore, localScaleAfter;
            public List<Vector3> approach = new List<Vector3>();
            public float roadDistance, dockDistance, boatWaterDepth, landingHeight;
            public string status;
        }
        [Serializable] public sealed class Result
        {
            public string status, utc, sceneSha256, sourceId, sourcePath, beforeStateHash, afterStateHash, evidence, error;
            public bool dirtyBefore, dirtyAfter;
            public int tested, dryRejected, wetRejected, supportRejected, collisionRejected, pathRejected;
            public float controllerRadius, controllerHeight, controllerStep;
            public List<Candidate> candidates = new List<Candidate>();
        }
        static NightMapBoathouseSearch() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            next = EditorApplication.timeSinceStartup + 1;
            string path = Path.Combine(Stage, "boathouse-search-request.json"); if (!File.Exists(path)) return;
            Request request = JsonUtility.FromJson<Request>(File.ReadAllText(path)); File.Delete(path);
            try { Search(request); } catch (Exception e) { File.WriteAllText(Path.Combine(Stage, "boathouse-search-error.txt"), e.ToString()); }
        }
        public static string Search(Request request)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || scene.path != NightMapSnapshot.ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Stable existing WatersideV2 Edit scene is required.");
            string disk = HashFile(scene.path);
            if (request == null || !string.Equals(request.expectedSceneSha256, disk, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Scene SHA baseline changed.");
            Transform[] all = All(scene); Terrain terrain = all.Select(t => t.GetComponent<Terrain>()).Single(t => t);
            Transform root = all.Single(t => t.gameObject.activeInHierarchy && PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject) == t.gameObject &&
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) == "Assets/Environment/RuralRebuild20261025/Prefabs/A04.prefab");
            Renderer[] rs = root.GetComponentsInChildren<Renderer>(true).Where(r => r.gameObject.activeInHierarchy && r.enabled).ToArray();
            Bounds bounds = rs[0].bounds; foreach (Renderer r in rs.Skip(1)) bounds.Encapsulate(r.bounds);
            // Model-specific anchors are derived from reviewed four-side views and named baked submeshes.
            Renderer boat = rs.FirstOrDefault(r => r.name == "A04_BakedMesh" && r.transform.parent.name == "LOD0");
            Renderer landingMesh = rs.FirstOrDefault(r => r.name == "A04_BakedMesh.025" && r.transform.parent.name == "LOD0");
            if (!boat || !landingMesh) throw new InvalidOperationException("Reviewed A04 baked anchor meshes changed; do not reuse approximate anchors.");
            CharacterController cc = all.Select(t => t.GetComponent<CharacterController>()).FirstOrDefault(c => c && c.gameObject.activeInHierarchy);
            if (!cc) throw new InvalidOperationException("Actual player CharacterController is unavailable; no guessed capsule is substituted.");
            float radius = cc.radius * Mathf.Max(Mathf.Abs(cc.transform.lossyScale.x), Mathf.Abs(cc.transform.lossyScale.z));
            float height = cc.height * Mathf.Abs(cc.transform.lossyScale.y), step = cc.stepOffset * Mathf.Abs(cc.transform.lossyScale.y);
            var result = new Result { utc = DateTime.UtcNow.ToString("O"), status = "SEARCHING", sceneSha256 = disk, sourceId = Id(root),
                sourcePath = root.name, dirtyBefore = scene.isDirty, beforeStateHash = StateHash(all), controllerRadius = radius, controllerHeight = height, controllerStep = step,
                evidence = "A04-east shows the NE/east door and steps; A04-south shows the integral south-facing boat slot. Anchor positions below are image/mesh-derived approximations, not a live doorway traversal proof. No new route or terrain is authored." };
            Renderer[] obstacles = all.SelectMany(t => t.GetComponents<Renderer>()).Where(r => r.enabled && r.gameObject.activeInHierarchy && !r.transform.IsChildOf(root) &&
                r.bounds.size.y > 1 && !PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(r.gameObject).Contains("/Plants/")).ToArray();
            Vector3 oldLanding = new Vector3(bounds.center.x + 4.5f, landingMesh.bounds.min.y, bounds.center.z + 4);
            Vector3 oldExterior = new Vector3(bounds.max.x + radius + .25f, oldLanding.y, oldLanding.z + .4f);
            string terrainPath = AssetDatabase.GetAssetPath(terrain.terrainData); string terrainHash = HashFile(terrainPath);
            try
            {
                // Southeast dry-shore band around the existing dock; original orientation/scale and water draft are fixed.
                for (float x = 73; x <= 84; x += .5f) for (float z = -79; z <= -56; z += .5f)
                {
                    result.tested++; Vector3 delta = new Vector3(x - bounds.center.x, 0, z - bounds.center.z);
                    Vector3 b = boat.bounds.center + delta;
                    if (!WatersideLayout.IsWater(b.x, b.z, -.45f) || !WatersideLayout.IsWater(b.x, b.z - 1.1f, -.3f)) { result.wetRejected++; continue; }
                    Vector3 landing = oldLanding + delta, exterior = oldExterior + delta;
                    if (WatersideLayout.IsWater(landing.x, landing.z, radius + .2f) || WatersideLayout.IsWater(exterior.x, exterior.z, radius + .2f)) { result.dryRejected++; continue; }
                    float ground = Ground(terrain, landing), outerGround = Ground(terrain, exterior);
                    if (Mathf.Abs(ground - landing.y) > Mathf.Max(.32f, step) || Mathf.Abs(outerGround - ground) > step + .08f) { result.supportRejected++; continue; }
                    Bounds nextBounds = bounds; nextBounds.center += delta;
                    // Keep the entire building outside existing bridge/dock/animal/structure bounds; none are moved.
                    if (obstacles.Any(r => r.bounds.Intersects(nextBounds))) { result.collisionRejected++; continue; }
                    Vector3 road = NearestRoad(exterior); float distance = Flat(exterior, road);
                    if (distance > 20) { result.pathRejected++; continue; }
                    var approach = new List<Vector3>(); bool safe = true; float previous = outerGround;
                    for (int i = 0, n = Mathf.Max(2, Mathf.CeilToInt(distance / .4f)); i <= n; i++)
                    {
                        Vector3 p = Vector3.Lerp(exterior, road, i / (float)n); float y = Ground(terrain, p); p.y = y;
                        Vector3 normal = terrain.terrainData.GetInterpolatedNormal((p.x - terrain.transform.position.x) / terrain.terrainData.size.x, (p.z - terrain.transform.position.z) / terrain.terrainData.size.z);
                        if (WatersideLayout.IsWater(p.x, p.z, radius + .15f) || Vector3.Angle(normal, Vector3.up) > Mathf.Min(cc.slopeLimit, 32) ||
                            Mathf.Abs(y - previous) > Mathf.Max(.2f, .4f * Mathf.Tan(Mathf.Min(cc.slopeLimit, 32) * Mathf.Deg2Rad)) ||
                            Physics.OverlapCapsule(p + Vector3.up * (radius + .035f), p + Vector3.up * Mathf.Max(radius + .035f, height - radius), radius,
                                ~0, QueryTriggerInteraction.Ignore).Any(c => !(c is TerrainCollider) && c != cc)) { safe = false; break; }
                        approach.Add(p); previous = y;
                    }
                    if (!safe) { result.pathRejected++; continue; }
                    result.candidates.Add(new Candidate { rootBefore = root.position, rootAfter = root.position + delta, rotationBefore = root.rotation, rotationAfter = root.rotation,
                        localScaleBefore = root.localScale, localScaleAfter = root.localScale, doorLanding = landing, exteriorStart = exterior, boatCenter = b, nearestRoad = road,
                        roadDistance = distance, dockDistance = Flat(nextBounds.center, new Vector3(86.49f, 0, -67.78f)), boatWaterDepth = WatersideLayout.WaterHeight(b.x,b.z) - Ground(terrain,b),
                        landingHeight = ground, approach = approach, status = "GEOMETRY_SHORTLIST_ACTUAL_DOOR_AND_PLAYER_WALK_PENDING" });
                }
                result.candidates = result.candidates.OrderBy(c => c.dockDistance + c.roadDistance).Take(5).ToList();
            }
            catch (Exception e) { result.error = e.ToString(); }
            finally
            {
                result.afterStateHash = StateHash(All(scene)); result.dirtyAfter = scene.isDirty;
                bool guard = result.beforeStateHash == result.afterStateHash && result.dirtyBefore == result.dirtyAfter && HashFile(scene.path) == disk && HashFile(terrainPath) == terrainHash;
                result.status = !guard ? "FAIL_STATE_GUARD" : result.error != null ? "FAIL_SEARCH_STATE_UNCHANGED" : result.candidates.Count > 0 ? "READ_ONLY_SHORTLIST_VISUAL_AND_WALK_PENDING" : "NO_SAFE_DUAL_SHORE_CANDIDATE_KEEP_A04_PENDING";
                File.WriteAllText(Path.Combine(Stage, "boathouse-search.json"), JsonUtility.ToJson(result, true));
            }
            return Path.Combine(Stage, "boathouse-search.json");
        }
        static Vector3 NearestRoad(Vector3 p)
        {
            Vector3 best = p; float distance = float.MaxValue;
            foreach (Vector3[] line in WatersideLayout.Routes.Concat(RuralMapConfiguration.NewCorridors)) for (int i = 1; i < line.Length; i++)
            { Vector3 a = line[i-1], d = line[i]-a, v = p-a; d.y=0;v.y=0;Vector3 q=a+(line[i]-a)*(d.sqrMagnitude<.001f?0:Mathf.Clamp01(Vector3.Dot(v,d)/d.sqrMagnitude));float test=Flat(p,q);if(test<distance){distance=test;best=q;} }
            return best;
        }
        static float Ground(Terrain t,Vector3 p)=>t.SampleHeight(p)+t.transform.position.y;
        static float Flat(Vector3 a,Vector3 b)=>Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
        static Transform[] All(Scene s)=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).OrderBy(t=>t.GetInstanceID()).ToArray();
        static string StateHash(Transform[] all)
        { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",all.Select(t=>t.GetInstanceID()+"|"+(t.parent?t.parent.GetInstanceID():0)+"|"+t.gameObject.activeSelf+"|"+EditorJsonUtility.ToJson(t)))))).Replace("-",""); }
        static string Id(UnityEngine.Object o)=>GlobalObjectId.GetGlobalObjectIdSlow(o).ToString();
        static string HashFile(string p){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p))).Replace("-","");}
    }
}
