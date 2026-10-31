using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.WatersideV2;

namespace WeatherHouse.NightReview20261027
{
    // Explicit coordinator entry points. Build is read-only; Apply remains unsaved and Restore rejects later edits.
    [InitializeOnLoad]
    public static class NightMapLandscapeCandidate
    {
        const string Stage = NightMapSnapshot.Staging;
        static double nextBuildPoll;
        [Serializable] public sealed class BuildRequest { public string command, expectedSceneSha256; }
        [Serializable] public sealed class BuildResult { public string status, utc, plan, error; }
        static NightMapLandscapeCandidate() { EditorApplication.update += PollBuild; }
        static void PollBuild()
        {
            if (EditorApplication.timeSinceStartup < nextBuildPoll || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextBuildPoll = EditorApplication.timeSinceStartup + 1;
            string requestPath = Path.Combine(Stage, "landscape-build-request.json"); if (!File.Exists(requestPath)) return;
            var result = new BuildResult { utc = DateTime.UtcNow.ToString("O") };
            try
            {
                BuildRequest request = JsonUtility.FromJson<BuildRequest>(File.ReadAllText(requestPath)); File.Delete(requestPath);
                if (request == null || request.command != "build" || !string.Equals(Hash(NightMapSnapshot.ScenePath), request.expectedSceneSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Only explicit build is supported; current scene SHA baseline is required. No Apply/Restore is exposed by this observer.");
                result.plan = Build(); result.status = "PASS_READ_ONLY_BUILD";
            }
            catch (Exception e) { result.status = "FAIL_READ_ONLY_BUILD"; result.error = e.ToString(); }
            File.WriteAllText(Path.Combine(Stage, "landscape-build-result.json"), JsonUtility.ToJson(result, true));
        }
        const string PlantFolder = "Assets/Game/World/WatersideV2/Generated/Nature/Plants/Prefabs/";
        const string BuildingFolder = "Assets/Environment/RuralRebuild20261025/Prefabs/";
        [Serializable] public sealed class Row
        {
            public string id, parentId, path;
            public Vector3 position, localPosition, localScale;
            public Quaternion rotation, localRotation;
            public bool active;
        }
        [Serializable] public sealed class Binding { public string id, mesh; public string[] materials; public bool enabled; }
        [Serializable] public sealed class Move
        {
            public string id, path, reason, node, source;
            public Vector3 before, after;
            public Bounds beforeBounds, afterBounds;
        }
        [Serializable] public sealed class Addition
        {
            public string sourceId, sourcePath, name, node, purpose, createdId;
            public Vector3 position;
            public Bounds sourceBounds, targetBounds;
            public int transforms, renderers, colliders;
        }
        [Serializable] public sealed class Node
        {
            public string id, name, source, note;
            public int plannedFurniture;
            public Vector3 entryDirection;
            public string entryBasis, approachStatus;
            public List<Vector3> approach = new List<Vector3>();
            public Bounds buildingBounds;
            public Vector3 roadPoint;
            public float roadDistance;
        }
        [Serializable] public sealed class Plan
        {
            public string version = "night-map-reuse-v2", status, utc, sceneSha256, terrainPath, terrainSha256, sourceSnapshot;
            public bool dirtyBefore;
            public int rendererCount, colliderCount, plantMoves, furnitureMoves, undoGroup = -1;
            public List<string> protectionRoots = new List<string>();
            public List<Row> before = new List<Row>(), after = new List<Row>();
            public List<Binding> bindings = new List<Binding>();
            public List<Move> moves = new List<Move>();
            public List<Node> nodes = new List<Node>();
            public List<Addition> additions = new List<Addition>();
            public List<Binding> afterBindings = new List<Binding>();
            public string createdRootId, serializedAfterHash;
            public int addedRenderers, addedTransforms, addedColliders;
            public List<string> notes = new List<string>();
        }
        sealed class Context
        {
            public Scene scene; public Terrain terrain; public Transform[] all;
            public List<Transform> protectedRoots; public List<Bounds> protectedBounds; public Renderer[] rendererCache;
        }
        public static string Build()
        {
            Context c = ContextFor(true);
            string latest = Path.Combine(Stage, "latest-snapshot.txt");
            if (!File.Exists(latest)) throw new InvalidOperationException("Capture the coordinated Unity snapshot first.");
            string snapshotPath = File.ReadAllText(latest).Trim();
            var snapshot = JsonUtility.FromJson<NightMapSnapshot.Snapshot>(File.ReadAllText(snapshotPath));
            string sceneHash = Hash(NightMapSnapshot.ScenePath);
            if (snapshot.status != "PASS_READ_ONLY_SNAPSHOT" || snapshot.dirtyBefore || snapshot.sceneSha256 != sceneHash)
                throw new InvalidOperationException("The latest snapshot is not a clean, guarded capture of this saved scene.");
            var current = c.all.Select(Capture).ToList();
            if (snapshot.transforms.Count != current.Count || snapshot.transforms.Any(s => !current.Any(t => t.id == s.id &&
                t.parentId == s.parentId && Close(t.position, s.position) && Close(t.localPosition, s.localPosition) &&
                Close(t.localScale, s.localScale) && SameRotation(t.rotation, s.rotation) &&
                SameRotation(t.localRotation, s.localRotation) && t.active == s.activeSelf)))
                throw new InvalidOperationException("Current hierarchy/TRS differs from the captured snapshot; recapture before planning.");
            var plan = new Plan { utc = DateTime.UtcNow.ToString("O"), status = "PLANNED_NOT_APPLIED", sceneSha256 = sceneHash,
                sourceSnapshot = snapshotPath, dirtyBefore = c.scene.isDirty, before = current, terrainPath = AssetDatabase.GetAssetPath(c.terrain.terrainData),
                rendererCount = Renderers(c).Length, colliderCount = Colliders(c).Length, bindings = CaptureBindings(c),
                protectionRoots = c.protectedRoots.Select(Id).ToList() };
            plan.terrainSha256 = Hash(plan.terrainPath);
            var roots = c.all.Where(t => PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject) == t.gameObject && t.gameObject.activeInHierarchy).ToArray();
            foreach (Transform t in roots.Where(t => Source(t).StartsWith(BuildingFolder, StringComparison.Ordinal) &&
                Path.GetFileNameWithoutExtension(Source(t)).StartsWith("A0", StringComparison.Ordinal)))
            {
                Bounds b = BoundsOf(t); Vector3 road = NearestRoad(b.center);
                plan.nodes.Add(new Node { id = Id(t), name = t.name, source = Source(t), buildingBounds = b, roadPoint = road,
                    roadDistance = Flat(b.center, road), note = "Existing building; its transform and internal functionality are preserved." });
            }
            Transform[] plants = roots.Where(t => Source(t).StartsWith(PlantFolder, StringComparison.Ordinal) &&
                !Path.GetFileName(Source(t)).StartsWith("G2_Upright", StringComparison.Ordinal) &&
                !t.GetComponentsInChildren<MonoBehaviour>(true).Any() && !t.GetComponentsInChildren<Collider>(true).Any() &&
                !Protected(c, t, BoundsOf(t))).OrderBy(Id).ToArray();
            var used = new HashSet<string>();
            // Each group has two leading flower families rather than a uniform mixture over the whole map.
            AddBuildingFlowers(c, plan, plants, used, "A01", new[] { "R06_Daisy", "R12_LilacBell" }, 10);
            AddPatch(c, plan, plants, used, "A07", "林屋北侧蓝紫药草花境", new Vector3(-57.8f, 0, -3), new Vector2(3.2f, 2),
                new[] { "R07_BlueFlower", "R12_LilacBell", "R13_LeafyHerb" }, 12, 0);
            AddBuildingFlowers(c, plan, plants, used, "A02", new[] { "R06_Daisy", "R11_YellowButton" }, 10);
            AddBuildingFlowers(c, plan, plants, used, "A06", new[] { "R06_Daisy", "R11_YellowButton" }, 14);
            AddBuildingFlowers(c, plan, plants, used, "A03", new[] { "R10_OrangeFlower", "R05_WhiteSpray" }, 10);
            plan.notes.Add("A04 is excluded from old-site flower/furniture dressing while its coherent dock relocation is investigated.");
            AddPatch(c, plan, plants, used, "reed-ground", "芦苇湾陆侧草群", new Vector3(-32, 0, -73), new Vector2(12, 4),
                new[] { "G1_TallBroad", "G3_OpenLow", "G4_DenseShort", "R01_Plume", "R02_BrownSeed", "R09_CreamFoxtail" }, 40, 2);
            AddPatch(c, plan, plants, used, "forest-understory", "林下低层群落", new Vector3(-59, 0, 16), new Vector2(5, 8),
                new[] { "R08_Fern", "R13_LeafyHerb", "R14_RoundGroundcover" }, 12, 0);
            AddFurniture(c, plan);
            AddExteriorNodes(c, plan);
            plan.notes.Add("Existing static bench, crate and short-fence assemblies may be cloned only into the named exterior candidate root. Original furniture collider types/meshes are preserved in each clone, with addedCollider budget 48. No meshes, materials, lights, animals or resources are created. Terrain, roads, water and harvestable G2 grass remain untouched. Counts below are measured bounds, not FPS evidence.");
            plan.notes.Add("Buildings retain their authored transforms. Proposed functionality is outdoor visual organization only; survival, business and indoor systems are planning topics, not implemented features.");
            if (plan.plantMoves < 60) plan.notes.Add("Fewer than 60 safe plant moves were available. Inspect skipped regions before judging visual completeness.");
            string path = Path.Combine(Stage, "landscape-plan.json"); Write(path, plan);
            if (c.scene.isDirty || Hash(NightMapSnapshot.ScenePath) != sceneHash || !SameRows(plan.before, c.all.Select(Capture).ToList()))
                throw new InvalidOperationException("Read-only planning guard failed.");
            return path;
        }
        static void AddBuildingFlowers(Context c, Plan p, Transform[] plants, HashSet<string> used, string asset, string[] species, int count)
        {
            Node node = p.nodes.FirstOrDefault(n => Path.GetFileNameWithoutExtension(n.source) == asset);
            if (node == null) { p.notes.Add(asset + " is absent from the active authored scene; no building is restored or invented."); return; }
            Vector3 toward = node.roadPoint - node.buildingBounds.center; toward.y = 0;
            if (toward.sqrMagnitude < .01f) toward = Vector3.forward; toward.Normalize();
            Vector3 side = new Vector3(-toward.z, 0, toward.x);
            float reach = Mathf.Max(node.buildingBounds.extents.x, node.buildingBounds.extents.z) + 2;
            Vector3 center = node.buildingBounds.center + side * reach;
            int region = Array.FindIndex(RuralMapConfiguration.Regions, poly => RuralMapConfiguration.Contains(node.buildingBounds.center, poly));
            AddPatch(c, p, plants, used, asset, node.name + " 路侧花境", center, new Vector2(3.7f, 2.2f), species, count, region);
        }
        static void AddPatch(Context c, Plan p, Transform[] plants, HashSet<string> used, string node, string reason,
            Vector3 center, Vector2 radii, string[] families, int wanted, int region)
        {
            Transform[] pool = plants.Where(t => !used.Contains(Id(t)) && families.Any(f => Path.GetFileName(Source(t)).StartsWith(f + "_", StringComparison.Ordinal)))
                .OrderBy(t => Flat(BoundsOf(t).center, center)).ThenBy(Id).ToArray();
            int placed = 0; var reserved = p.moves.Select(m => m.afterBounds).ToList();
            for (int trial = 0; trial < 240 && placed < wanted && placed < pool.Length; trial++)
            {
                float angle = trial * 2.39996323f;
                float r = Mathf.Sqrt(((trial * 37) % 101 + .5f) / 101f);
                Vector3 target = center + new Vector3(Mathf.Cos(angle) * radii.x * r, 0, Mathf.Sin(angle) * radii.y * r);
                if (region >= 0 && !RuralMapConfiguration.Contains(target, RuralMapConfiguration.Regions[region])) continue;
                Transform source = pool[placed]; Bounds old = BoundsOf(source);
                if (!SafePlantPoint(c, target, old, reserved, out Vector3 delta)) continue;
                Bounds plantNext = old; plantNext.center += delta;
                if (p.nodes.Any(n => n.buildingBounds.Intersects(plantNext))) continue;
                Vector3 after = source.position + delta; Bounds next = old; next.center += delta;
                if (Protected(c, source, next) || Flat(source.position, after) < .5f) continue;
                p.moves.Add(new Move { id = Id(source), path = PathOf(source), source = Source(source), node = node, reason = reason,
                    before = source.position, after = after, beforeBounds = old, afterBounds = next });
                used.Add(Id(source)); reserved.Add(next); placed++; p.plantMoves++;
            }
            p.notes.Add(node + ": " + placed + "/" + wanted + " reusable plants planned; failed ground, water, route, structure or protection checks are skipped.");
        }
        static bool SafePlantPoint(Context c, Vector3 point, Bounds old, List<Bounds> reserved, out Vector3 delta)
        {
            delta = Vector3.zero; Vector3 o = c.terrain.transform.position, size = c.terrain.terrainData.size;
            if (point.x < o.x + 2 || point.x > o.x + size.x - 2 || point.z < o.z + 2 || point.z > o.z + size.z - 2 ||
                WatersideLayout.IsWater(point.x, point.z, .8f) || WatersideLayout.WalkablePathEdgeDistance(point.x, point.z) < 1.25f ||
                RuralMapConfiguration.NewCorridors.Any(line => RuralMapConfiguration.LineDistance(point, line) < 2.8f)) return false;
            float height = c.terrain.SampleHeight(point) + o.y;
            Vector3 n = c.terrain.terrainData.GetInterpolatedNormal((point.x - o.x) / size.x, (point.z - o.z) / size.z);
            if (Vector3.Angle(n, Vector3.up) > 28) return false;
            point.y = height;
            if (Physics.OverlapSphere(point + Vector3.up * .35f, .3f, ~0, QueryTriggerInteraction.Ignore).Any(hit => !(hit is TerrainCollider))) return false;
            delta = new Vector3(point.x - old.center.x, point.y - old.min.y - .015f, point.z - old.center.z);
            Bounds next = old; next.center += delta;
            if (reserved.Any(b => Flat(b.center, next.center) < Mathf.Clamp(Mathf.Min(b.extents.x + next.extents.x, b.extents.z + next.extents.z) * .35f, .4f, 1.1f))) return false;
            return true;
        }
        static void AddFurniture(Context c, Plan p)
        {
            foreach (Transform t in c.all.Where(t => t.name.Contains("四人桌席")).OrderBy(Id))
            {
                Bounds old = BoundsOf(t);
                if (Protected(c, t, old) || old.size.x > 7 || old.size.z > 7 || t.GetComponentsInChildren<MonoBehaviour>(true).Length > 0) continue;
                Node node = p.nodes.OrderBy(n => Flat(old.center, n.buildingBounds.center)).FirstOrDefault();
                if (node == null || Flat(old.center, node.buildingBounds.center) > 14) continue;
                Vector3 facing = node.buildingBounds.center - node.roadPoint; facing.y = 0;
                if (facing.sqrMagnitude < .1f) continue; facing.Normalize();
                Vector3 target = node.roadPoint + new Vector3(-facing.z, 0, facing.x) * 4.4f + facing * 2;
                float travel = Flat(old.center, target);
                if (travel < 1.5f || travel > 7 || WatersideLayout.IsWater(target.x, target.z, 2) || WatersideLayout.WalkablePathEdgeDistance(target.x, target.z) < 2.5f) continue;
                float[] heights = new[] { new Vector3(-old.extents.x, 0, -old.extents.z), new Vector3(old.extents.x, 0, -old.extents.z),
                    new Vector3(-old.extents.x, 0, old.extents.z), new Vector3(old.extents.x, 0, old.extents.z) }
                    .Select(v => c.terrain.SampleHeight(target + v) + c.terrain.transform.position.y).ToArray();
                if (heights.Max() - heights.Min() > .22f) continue;
                Vector3 delta = new Vector3(target.x - old.center.x, heights.Max() - old.min.y + .02f, target.z - old.center.z);
                Bounds next = old; next.center += delta;
                if (Protected(c, t, next) || Renderers(c).Any(r => !r.transform.IsChildOf(t) && r.enabled && r.gameObject.activeInHierarchy &&
                    r.bounds.size.y > .45f && r.bounds.Intersects(next))) continue;
                p.moves.Add(new Move { id = Id(t), path = PathOf(t), source = "existing complete table/chair group", node = node.name,
                    reason = "Link an existing complete rest setting to the nearby building and road without separating its furniture.",
                    before = t.position, after = t.position + delta, beforeBounds = old, afterBounds = next }); p.furnitureMoves++;
            }
        }
        static Vector3 DoorDirection(Node node)
        {
            string asset = Path.GetFileNameWithoutExtension(node.source);
            if (asset == "A01" || asset == "A07") return Vector3.right; // East views show actual doors.
            if (asset == "A02") return Vector3.back; // South view shows steps.
            Vector3 direction = node.roadPoint - node.buildingBounds.center; direction.y = 0; return direction;
        }
        static bool SafeCloneSource(Transform t) => t.gameObject.activeInHierarchy &&
            t.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 &&
            t.GetComponentsInChildren<Rigidbody>(true).Length == 0 && t.GetComponentsInChildren<Light>(true).Length == 0 &&
            t.GetComponentsInChildren<Animator>(true).Length == 0 && t.GetComponentsInChildren<AudioSource>(true).Length == 0;
        static void AddExteriorNodes(Context c, Plan p)
        {
            // Reuse small complete authored groups. Source groups stay byte-for-byte unchanged, even inside protected core.
            Transform bench = c.all.Where(t => t.name == "成组临水倒木凳" && SafeCloneSource(t))
                .OrderBy(t => t.GetComponentsInChildren<Renderer>(true).Length).ThenBy(Id).FirstOrDefault();
            Transform supply = c.all.Where(t => t.name == "成组木箱" && SafeCloneSource(t))
                .Where(t => BoundsOf(t).size.x < 2 && BoundsOf(t).size.z < 2).OrderBy(Id).FirstOrDefault();
            Transform fence = c.all.Where(t => t.name.StartsWith("连续木栏 ", StringComparison.Ordinal) && SafeCloneSource(t))
                .Where(t => BoundsOf(t).size.x > 1.5f && BoundsOf(t).size.x < 2.3f && BoundsOf(t).size.z < .4f && BoundsOf(t).size.y < 1.25f).OrderBy(Id).FirstOrDefault();
            foreach (Transform source in new[] { bench, supply, fence }.Where(t => t))
                p.notes.Add("Static source " + PathOf(source) + ": " + string.Join(", ", source.GetComponentsInChildren<Component>(true).Where(component => component).GroupBy(component => component.GetType().Name).Select(group => group.Key + "=" + group.Count())));
            foreach (Node node in p.nodes.OrderBy(n => n.name))
            {
                if (Path.GetFileNameWithoutExtension(node.source) == "A04") { node.note = "Boathouse relocation pending dry-door / wet-boat-slot preflight; no furniture is placed at the old isolated site."; continue; }
                Vector3 direction = DoorDirection(node);
                node.entryDirection = direction.normalized;
                string asset = Path.GetFileNameWithoutExtension(node.source);
                node.entryBasis = asset == "A01" || asset == "A07" ? "Observed east-facing door in coordinated four-side views" : asset == "A02" ? "Observed south-facing steps in coordinated four-side views" : "Nearest-road approximation; actual entrance visual review pending";
                BuildApproach(c, node);
                if (direction.sqrMagnitude < .1f) direction = Vector3.back; direction.Normalize();
                Vector3 side = new Vector3(-direction.z, 0, direction.x);
                float reach = Mathf.Abs(direction.x) * node.buildingBounds.extents.x + Mathf.Abs(direction.z) * node.buildingBounds.extents.z;
                Vector3 forecourt = node.buildingBounds.center + direction * (reach + 3.2f);
                AddStatic(c, p, node, bench, forecourt + side * 3.8f, "停步坐凳", "Entrance rest point outside the continuous route");
                AddStatic(c, p, node, supply, forecourt - side * 3.5f, "侧边材料木箱", "Small work/supply cue; no new pickup or storage mechanics");
                // A pair marks a side edge, never a closed animal enclosure or a fence across the entrance.
                AddStatic(c, p, node, fence, forecourt + side * 5.8f + direction * 2.5f, "外侧短栏", "Open outer edge; existing animal activity and route remain clear");
                node.note = "Original building retained. " + node.plannedFurniture + "/3 static exterior elements passed geometry checks; entry basis: " + node.entryBasis + "; approach: " + node.approachStatus + ".";
            }
            p.notes.Add("Exterior furniture is a visible organizational candidate, not new interaction, route navigation, animal AI, building entrance or survival/business functionality. A04 placement and actual entry directions require the coordinated four-view review.");
        }
        static void BuildApproach(Context c, Node node)
        {
            CharacterController body = c.all.Select(t => t.GetComponent<CharacterController>()).FirstOrDefault(b => b && b.gameObject.activeInHierarchy);
            if (!body) { node.approachStatus = "PENDING_ACTUAL_PLAYER_CAPSULE"; return; }
            Vector3 direction = node.entryDirection; float reach = Mathf.Abs(direction.x)*node.buildingBounds.extents.x + Mathf.Abs(direction.z)*node.buildingBounds.extents.z;
            Vector3 start = node.buildingBounds.center + direction*(reach+1.2f); Vector3 end = NearestRoad(start);
            float radius = body.radius*Mathf.Max(Mathf.Abs(body.transform.lossyScale.x),Mathf.Abs(body.transform.lossyScale.z));
            float height = body.height*Mathf.Abs(body.transform.lossyScale.y); float previous = c.terrain.SampleHeight(start)+c.terrain.transform.position.y;
            int count = Mathf.Max(2,Mathf.CeilToInt(Flat(start,end)/.4f));
            for(int i=0;i<=count;i++)
            {
                Vector3 point=Vector3.Lerp(start,end,i/(float)count);point.y=c.terrain.SampleHeight(point)+c.terrain.transform.position.y;
                if(WatersideLayout.IsWater(point.x,point.z,radius+.15f) || Mathf.Abs(point.y-previous)>.3f ||
                    Physics.OverlapCapsule(point+Vector3.up*(radius+.035f),point+Vector3.up*Mathf.Max(radius+.035f,height-radius),radius,~0,QueryTriggerInteraction.Ignore)
                    .Any(hit=>!(hit is TerrainCollider)&&hit!=body))
                {node.approachStatus="STRAIGHT_APPROACH_FAILED_BENT_ROUTE_OR_DOOR_REVIEW_REQUIRED";node.approach.Clear();return;}
                node.approach.Add(point);previous=point.y;
            }
            node.approachStatus="PASS_STRAIGHT_GROUND_AND_ACTUAL_CAPSULE_DOOR_TRAVERSAL_PENDING";
        }
        static void CheckAppliedApproaches(Context c, Plan p)
        {
            CharacterController body = c.all.Select(t=>t.GetComponent<CharacterController>()).FirstOrDefault(b=>b&&b.gameObject.activeInHierarchy);
            if(!body)return;
            float radius=body.radius*Mathf.Max(Mathf.Abs(body.transform.lossyScale.x),Mathf.Abs(body.transform.lossyScale.z)),height=body.height*Mathf.Abs(body.transform.lossyScale.y);
            foreach(Node node in p.nodes)foreach(Vector3 point in node.approach)
                if(Physics.OverlapCapsule(point+Vector3.up*(radius+.035f),point+Vector3.up*Mathf.Max(radius+.035f,height-radius),radius,~0,QueryTriggerInteraction.Ignore)
                    .Any(hit=>!(hit is TerrainCollider)&&hit!=body))throw new InvalidOperationException("A candidate collider blocks the checked approach: "+node.name);
        }
        static void AddStatic(Context c, Plan p, Node node, Transform source, Vector3 target, string label, string purpose)
        {
            if (!source) { p.notes.Add(node.name + ": missing safe static source for " + label); return; }
            Bounds old = BoundsOf(source); Vector3 o = c.terrain.transform.position, size = c.terrain.terrainData.size;
            // Small local search keeps the node readable without forcing a prop into a slope or current path.
            for (int trial = 0; trial < 25; trial++)
            {
                float a = trial * 2.39996323f, radius = trial == 0 ? 0 : .45f * Mathf.Sqrt(trial);
                Vector3 point = target + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
                if (point.x < o.x + 3 || point.x > o.x + size.x - 3 || point.z < o.z + 3 || point.z > o.z + size.z - 3 ||
                    WatersideLayout.IsWater(point.x, point.z, 1.2f) || WatersideLayout.WalkablePathEdgeDistance(point.x, point.z) < 2.2f ||
                    RuralMapConfiguration.NewCorridors.Any(line => RuralMapConfiguration.LineDistance(point, line) < 2.5f)) continue;
                float[] ys = new[] { new Vector3(-old.extents.x,0,-old.extents.z), new Vector3(old.extents.x,0,-old.extents.z),
                    new Vector3(-old.extents.x,0,old.extents.z), new Vector3(old.extents.x,0,old.extents.z) }.Select(v => c.terrain.SampleHeight(point + v) + o.y).ToArray();
                if (ys.Max() - ys.Min() > .24f) continue;
                Vector3 delta = new Vector3(point.x - old.center.x, ys.Max() - old.min.y + .015f, point.z - old.center.z);
                Bounds next = old; next.center += delta;
                // Source may be protected; only the clone destination is tested here.
                if (Protected(c, c.terrain.transform, next) || node.approach.Any(point => Mathf.Abs(point.x-next.center.x) < next.extents.x+.65f && Mathf.Abs(point.z-next.center.z) < next.extents.z+.65f) || p.additions.Any(x => x.targetBounds.Intersects(next)) ||
                    p.moves.Any(x => x.afterBounds.Intersects(next))) continue;
                Bounds clearance = next; clearance.Expand(.35f);
                if (Renderers(c).Any(r => r.enabled && r.gameObject.activeInHierarchy && r.bounds.size.y > .6f && r.bounds.Intersects(clearance))) continue;
                if (Physics.OverlapBox(next.center, next.extents + Vector3.one * .15f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Any(hit => !(hit is TerrainCollider))) continue;
                int rc = source.GetComponentsInChildren<Renderer>(true).Length, tc = source.GetComponentsInChildren<Transform>(true).Length;
                int cc = source.GetComponentsInChildren<Collider>(true).Length;
                if (p.addedRenderers + rc > 120 || p.addedTransforms + tc > 240 || p.addedColliders + cc > 48 || p.additions.Count >= 32) return;
                p.additions.Add(new Addition { sourceId = Id(source), sourcePath = PathOf(source), name = node.name + " · " + label,
                    node = node.name, purpose = purpose, position = source.position + delta, sourceBounds = old, targetBounds = next, renderers = rc, transforms = tc, colliders = cc });
                p.addedRenderers += rc; p.addedTransforms += tc; p.addedColliders += cc; node.plannedFurniture++; return;
            }
            p.notes.Add(node.name + ": " + label + " skipped; no flat clear ground outside protection/road/water/objects. This node remains incomplete pending manual composition review.");
        }
        public static string Apply(string planPath)
        {
            Plan p = JsonUtility.FromJson<Plan>(File.ReadAllText(planPath)); Context c = ContextFor(true); GuardBaseline(c, p, p.before);
            if (p.version != "night-map-reuse-v2" || p.status != "PLANNED_NOT_APPLIED" || p.moves.Count + p.additions.Count == 0 || p.moves.Count > 180 || p.additions.Count > 32 || p.addedRenderers > 120 || p.addedTransforms > 240 || p.addedColliders > 48 || p.moves.Select(m => m.id).Distinct().Count() != p.moves.Count)
                throw new InvalidOperationException("Invalid, empty, duplicate or oversized move list.");
            var byId = c.all.ToDictionary(Id);
            foreach (Move move in p.moves)
            {
                if (!byId.TryGetValue(move.id, out Transform t) || !Close(t.position, move.before) || Protected(c, t, move.beforeBounds) || Protected(c, t, move.afterBounds))
                    throw new InvalidOperationException("A proposed move crosses a protected core or no longer matches its baseline: " + move.path);
                if (p.moves.Any(other => other.id != move.id && t.IsChildOf(byId[other.id])))
                    throw new InvalidOperationException("Nested owned roots are refused; child ownership must stay unambiguous.");
            }
            Undo.IncrementCurrentGroup(); p.undoGroup = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("天气屋外区布局与群落候选");
            try
            {
                foreach (Move move in p.moves)
                {
                    Transform t = byId[move.id]; Undo.RecordObject(t, "外区布局候选"); t.position = move.after;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                }
                if (p.additions.Count > 0)
                {
                    var group = new GameObject("外区完整功能节点候选 · 20261027"); Undo.RegisterCreatedObjectUndo(group, "外区家具组候选");
                    p.createdRootId = Id(group.transform);
                    foreach (Addition addition in p.additions)
                    {
                        Transform source = byId[addition.sourceId];
                        if (!SafeCloneSource(source)) throw new InvalidOperationException("Clone source gained behaviour, light or rigidbody: " + addition.sourcePath);
                        GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, source.parent);
                        Undo.RegisterCreatedObjectUndo(clone, "复用完整静态家具组");
                        clone.name = addition.name; clone.transform.SetParent(group.transform, true); clone.transform.position = addition.position;
                        addition.createdId = Id(clone.transform);
                        Bounds actualBounds = BoundsOf(clone.transform);
                        if (Vector3.Distance(actualBounds.center,addition.targetBounds.center)>.003f || Vector3.Distance(actualBounds.size,addition.targetBounds.size)>.003f)
                            throw new InvalidOperationException("Reparented clone world bounds differ from the reviewed source-preserving placement: "+addition.name);
                        if (clone.GetComponentsInChildren<Transform>(true).Length != addition.transforms || clone.GetComponentsInChildren<Renderer>(true).Length != addition.renderers || clone.GetComponentsInChildren<Collider>(true).Length != addition.colliders)
                            throw new InvalidOperationException("Cloned assembly count differs from the manifest.");
                    }
                }
                Physics.SyncTransforms();
                CheckDescendants(c, p);
                CheckAppliedApproaches(c, p);
                if (Renderers(c).Length != p.rendererCount || Colliders(c).Length != p.colliderCount || !SameBindings(p.bindings, CaptureBindings(c)) || Hash(p.terrainPath) != p.terrainSha256)
                    throw new InvalidOperationException("Renderer/collider count, mesh/material binding or terrain source changed.");
                Transform[] applied = AllTransforms(c.scene);
                var appliedById = applied.ToDictionary(Id);
                foreach(Addition addition in p.additions)
                {
                    Transform clone = appliedById[addition.createdId]; Bounds bounds = BoundsOf(clone);
                    if(Physics.OverlapBox(bounds.center,bounds.extents+Vector3.one*.05f,Quaternion.identity,~0,QueryTriggerInteraction.Ignore)
                        .Any(hit=>!(hit is TerrainCollider)&&!hit.transform.IsChildOf(clone)))
                        throw new InvalidOperationException("Final cloned furniture collider clearance failed: "+addition.name);
                }
                if (applied.Length != p.before.Count + (p.additions.Count == 0 ? 0 : 1) + p.addedTransforms ||
                    applied.SelectMany(t => t.GetComponents<Renderer>()).Count() != p.rendererCount + p.addedRenderers ||
                    applied.SelectMany(t => t.GetComponents<Collider>()).Count() != p.colliderCount + p.addedColliders)
                    throw new InvalidOperationException("New object ownership/count guard failed.");
                p.after = applied.Select(Capture).ToList();
                p.afterBindings = CaptureBindings(new Context { all = applied }); p.status = "APPLIED_UNSAVED_VISUAL_AND_PLAY_PENDING";
                Undo.CollapseUndoOperations(p.undoGroup); EditorSceneManager.MarkSceneDirty(c.scene);
                p.serializedAfterHash = SerializedSceneHash(applied);
                string receipt = Path.Combine(Stage, "landscape-after.json"); Write(receipt, p); return receipt;
            }
            catch
            {
                Undo.RevertAllDownToGroup(p.undoGroup); p.status = "FAILED_REVERTED_CHECK_EDITOR_STATE"; Write(Path.Combine(Stage, "landscape-failure.json"), p); throw;
            }
        }
        public static string Restore(string receiptPath)
        {
            Plan p = JsonUtility.FromJson<Plan>(File.ReadAllText(receiptPath)); Context c = ContextFor(false);
            if (p.status != "APPLIED_UNSAVED_VISUAL_AND_PLAY_PENDING") throw new InvalidOperationException("No matching unsaved transaction.");
            GuardBaseline(c, p, p.after);
            if (Undo.GetCurrentGroup() != p.undoGroup || string.IsNullOrEmpty(p.serializedAfterHash) || SerializedSceneHash(c.all) != p.serializedAfterHash)
                throw new InvalidOperationException("Undo history or any serialized scene component changed after Apply; refuse to undo later work.");
            Undo.RevertAllDownToGroup(p.undoGroup); Physics.SyncTransforms();
            if (!SameRows(p.before, AllTransforms(c.scene).Select(Capture).ToList())) throw new InvalidOperationException("Undo was unavailable or a later edit exists; inspect without saving.");
            p.status = c.scene.isDirty == p.dirtyBefore ? "RESTORED_BASELINE" : "RESTORED_TRANSFORMS_DIRTY_REVIEW_REQUIRED";
            string path = Path.Combine(Stage, "landscape-restored.json"); Write(path, p); return path;
        }
        [Serializable] public sealed class PreSaveResult
        { public string status, utc, sceneSha256, receipt; public int originalTransforms, addedTransforms, rendererCount, colliderCount, protectionRoots; public bool dirty, playing; }
        public static string VerifyForSave(string receiptPath)
        {
            Plan p = JsonUtility.FromJson<Plan>(File.ReadAllText(receiptPath)); Context c = ContextFor(false);
            if (p.version != "night-map-reuse-v2" || p.status != "APPLIED_UNSAVED_VISUAL_AND_PLAY_PENDING") throw new InvalidOperationException("A matching v2 unsaved receipt is required.");
            GuardBaseline(c, p, p.after);
            if (SerializedSceneHash(c.all) != p.serializedAfterHash) throw new InvalidOperationException("A scene component changed after Apply; review later edits before saving.");
            var originalIds = new HashSet<string>(p.before.Select(row => row.id));
            var original = new Context { all = c.all.Where(t => originalIds.Contains(Id(t))).ToArray(), scene = c.scene, terrain = c.terrain,
                protectedRoots = c.protectedRoots, protectedBounds = c.protectedBounds };
            CheckDescendants(original, p); CheckAppliedApproaches(c, p);
            if (Renderers(original).Length != p.rendererCount || Colliders(original).Length != p.colliderCount || !SameBindings(p.bindings,CaptureBindings(original)))
                throw new InvalidOperationException("Original shared bindings or component counts changed.");
            var result = new PreSaveResult { status = "PASS_STATE_GUARDS_COORDINATOR_SAVE_REVIEW_PENDING", utc = DateTime.UtcNow.ToString("O"), sceneSha256 = Hash(NightMapSnapshot.ScenePath),
                receipt = receiptPath, originalTransforms = p.before.Count, addedTransforms = p.addedTransforms+(p.additions.Count>0?1:0), rendererCount = Renderers(c).Length,
                colliderCount = Colliders(c).Length, protectionRoots = c.protectedRoots.Count, dirty = c.scene.isDirty, playing = EditorApplication.isPlaying };
            string output = Path.Combine(Stage,"landscape-presave.json"); File.WriteAllText(output,JsonUtility.ToJson(result,true)); return output;
        }
        static Transform[] AllTransforms(Scene scene) => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).OrderBy(Id).ToArray();
        static Context ContextFor(bool clean)
        {
            Scene s = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || s.path != NightMapSnapshot.ScenePath || SceneManager.sceneCount != 1 || clean && s.isDirty)
                throw new InvalidOperationException("Coordinator needs the stable, already open WatersideV2 Edit scene; this method never opens, plays or saves it.");
            var c = new Context { scene = s, all = AllTransforms(s) };
            c.terrain = c.all.Select(t => t.GetComponent<Terrain>()).Single(t => t);
            c.protectedRoots = c.all.Where(t => t.name.Contains("天气屋") || t.name.Contains("归家石桥小院") || t.name.Contains("归家院") || t.name.Contains("归家桥头")).ToList();
            if (!c.protectedRoots.Any(t => t.name.Contains("天气屋"))) throw new InvalidOperationException("Weather House protection root missing.");
            c.protectedBounds = c.protectedRoots.Select(VisibleBoundsOf).ToList();
            c.rendererCache = c.all.SelectMany(t => t.GetComponents<Renderer>()).ToArray();
            return c;
        }
        static bool Protected(Context c, Transform t, Bounds bounds)
        {
            if (c.protectedRoots.Any(root => t == root || t.IsChildOf(root))) return true;
            foreach (Bounds b in c.protectedBounds)
                if (bounds.min.x < b.max.x + 4 && bounds.max.x > b.min.x - 4 && bounds.min.z < b.max.z + 4 && bounds.max.z > b.min.z - 4) return true;
            foreach (Vector3[] poly in new[] { RuralMapConfiguration.Regions[3], RuralMapConfiguration.Regions[6] })
            {
                if (RuralMapConfiguration.Contains(bounds.center, poly) || poly.Any(p => p.x >= bounds.min.x - 4 && p.x <= bounds.max.x + 4 && p.z >= bounds.min.z - 4 && p.z <= bounds.max.z + 4)) return true;
                for (int i = 0; i < poly.Length; i++) if (RuralMapConfiguration.SegmentDistance(bounds.center, poly[i], poly[(i + 1) % poly.Length]) < Mathf.Max(bounds.extents.x, bounds.extents.z) + 4) return true;
            }
            return false;
        }
        static void GuardBaseline(Context c, Plan p, List<Row> expected)
        {
            if (Hash(NightMapSnapshot.ScenePath) != p.sceneSha256 || Hash(p.terrainPath) != p.terrainSha256 ||
                !SameRows(expected, c.all.Select(Capture).ToList()) || !SameBindings(expected == p.after && p.afterBindings.Count > 0 ? p.afterBindings : p.bindings, CaptureBindings(c)))
                throw new InvalidOperationException("Scene/terrain file, hierarchy, transform or shared-asset baseline changed; refuse to overwrite later work.");
        }
        static void CheckDescendants(Context c, Plan p)
        {
            var before = p.before.ToDictionary(r => r.id); var all = c.all.ToDictionary(Id); var owned = p.moves.ToDictionary(m => m.id);
            if (before.Count != all.Count) throw new InvalidOperationException("An original transform was created or deleted.");
            foreach (Transform t in c.all)
            {
                Row a = before[Id(t)], b = Capture(t); Transform ancestor = t; Move owner = null;
                while (ancestor) { if (owned.TryGetValue(Id(ancestor), out owner)) break; ancestor = ancestor.parent; }
                Vector3 delta = owner == null ? Vector3.zero : owner.after - owner.before;
                if (Protected(c, t, BoundsOf(t)) && owner != null) throw new InvalidOperationException("Protected spatial descendant acquired an owner: " + a.path);
                if (a.parentId != b.parentId || a.path != b.path || a.active != b.active || !Close(a.localScale, b.localScale) ||
                    !SameRotation(a.localRotation, b.localRotation) || !SameRotation(a.rotation, b.rotation) ||
                    !Close(a.position + delta, b.position) || (owner == null || owner.id != a.id) && !Close(a.localPosition, b.localPosition))
                    throw new InvalidOperationException("Original/descendant transform guard failed: " + a.path);
            }
        }
        static Vector3 NearestRoad(Vector3 p)
        {
            Vector3 best = p; float distance = float.MaxValue;
            foreach (Vector3[] route in WatersideLayout.Routes.Concat(RuralMapConfiguration.NewCorridors))
                for (int i = 1; i < route.Length; i++)
                {
                    Vector3 a = route[i - 1], b = route[i], d = b - a; d.y = 0; Vector3 v = p - a; v.y = 0;
                    Vector3 q = a + (b - a) * (d.sqrMagnitude < .00001f ? 0 : Mathf.Clamp01(Vector3.Dot(v, d) / d.sqrMagnitude));
                    float test = Flat(p, q); if (test < distance) { distance = test; best = q; }
                }
            return best;
        }
        static Row Capture(Transform t) => new Row { id = Id(t), parentId = t.parent ? Id(t.parent) : "", path = PathOf(t), position = t.position,
            rotation = t.rotation, localPosition = t.localPosition, localRotation = t.localRotation, localScale = t.localScale, active = t.gameObject.activeSelf };
        static Renderer[] Renderers(Context c) => c.rendererCache ?? c.all.SelectMany(t => t.GetComponents<Renderer>()).ToArray();
        static Collider[] Colliders(Context c) => c.all.SelectMany(t => t.GetComponents<Collider>()).ToArray();
        static List<Binding> CaptureBindings(Context c) => Renderers(c).OrderBy(r => Id(r)).Select(r =>
        {
            MeshFilter filter = r.GetComponent<MeshFilter>();
            return new Binding { id = Id(r), enabled = r.enabled,
                mesh = Id(r is SkinnedMeshRenderer s ? s.sharedMesh : filter ? filter.sharedMesh : null), materials = r.sharedMaterials.Select(Id).ToArray() };
        }).ToList();
        static bool SameBindings(List<Binding> a, List<Binding> b) => JsonUtility.ToJson(new BindingList { rows = a }) == JsonUtility.ToJson(new BindingList { rows = b });
        [Serializable] sealed class BindingList { public List<Binding> rows; }
        static bool SameRows(List<Row> a, List<Row> b) => a.Count == b.Count && a.Zip(b, (x, y) => JsonUtility.ToJson(x) == JsonUtility.ToJson(y)).All(x => x);
        static Bounds VisibleBoundsOf(Transform t)
        {
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            Bounds b = rs[0].bounds; foreach (Renderer r in rs.Skip(1)) b.Encapsulate(r.bounds); return b;
        }
        static Bounds BoundsOf(Transform t) { Renderer[] rs = t.GetComponentsInChildren<Renderer>(true); if (rs.Length == 0) return new Bounds(t.position, Vector3.zero); Bounds b = rs[0].bounds; foreach (Renderer r in rs.Skip(1)) b.Encapsulate(r.bounds); return b; }
        static string SerializedSceneHash(Transform[] all)
        {
            string text = string.Join("\n", all.Select(t => Id(t) + "|" + EditorJsonUtility.ToJson(t.gameObject) + "|" +
                string.Join("|", t.GetComponents<Component>().Select(component => component ? Id(component) + ":" + EditorJsonUtility.ToJson(component) : "missing-script"))));
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }
        static string Source(Transform t) => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) ?? "";
        static string Id(UnityEngine.Object o) => o ? GlobalObjectId.GetGlobalObjectIdSlow(o).ToString() : "";
        static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name + "[" + t.GetSiblingIndex() + "]" : t.name + "[" + t.GetSiblingIndex() + "]";
        static bool Close(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < .0001f;
        static bool SameRotation(Quaternion a, Quaternion b) =>
            Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z) + Mathf.Abs(a.w - b.w) < .00001f ||
            Mathf.Abs(a.x + b.x) + Mathf.Abs(a.y + b.y) + Mathf.Abs(a.z + b.z) + Mathf.Abs(a.w + b.w) < .00001f;
        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
        static void Write(string path, Plan plan) => File.WriteAllText(path, JsonUtility.ToJson(plan, true));
    }
}
