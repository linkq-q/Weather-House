using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using WeatherHouse.Life;
using Plan = WeatherHouse.NightReview20261027.NightMapLandscapeCandidate.Plan;
using Addition = WeatherHouse.NightReview20261027.NightMapLandscapeCandidate.Addition;

namespace WeatherHouse.NightReview20261027
{
    // Read-only extension of a reviewed v2 plan. Never replaces that plan, applies, opens, plays or saves.
    [InitializeOnLoad]
    public static class NightMapFlowerExtension
    {
        const string Stage = NightMapSnapshot.Staging;
        const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
        static double nextPoll;
        [Serializable] public sealed class Request { public string action, frozenPlanSha256, expectedSceneSha256; }
        [Serializable] public sealed class SourceRow { public string id, path, prefab, mesh; public int transforms, renderers, colliders, lodGroups; public int[] lodLevels; }
        [Serializable] public sealed class FilterRow { public string filter; public int survivors; }
        [Serializable] public sealed class Result
        {
            public string status, utc, error, frozenPlanSha256, output, outputSha256, beforeHash, afterHash;
            public bool dirtyBefore, dirtyAfter;
            public int plannedA01Blue, plannedA03Blue, plannedA06Orange, totalGroups, addedRenderers, addedTransforms, addedColliders;
            public List<SourceRow> sources = new List<SourceRow>();
            public List<FilterRow> sourceFilters = new List<FilterRow>();
            public List<string> sourceSamples = new List<string>();
        }
        static NightMapFlowerExtension() { EditorApplication.update += Poll; }
        static object Call(string name, params object[] args)
        {
            MethodInfo method = typeof(NightMapLandscapeCandidate).GetMethod(name, Hidden);
            if (method == null) throw new MissingMethodException(name);
            if (method.GetParameters().Length != args.Length)
                throw new TargetParameterCountException(name + " expects " + method.GetParameters().Length + " parameters, received " + args.Length + ". Array-valued single arguments must be boxed as object.");
            try { return method.Invoke(null, args); }
            catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        }
        static object Field(object context, string name) => context.GetType().GetField(name).GetValue(context);
        static string Id(UnityEngine.Object obj) => GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
        static Bounds BoundsOf(Transform t) => (Bounds)Call("BoundsOf", t);
        static string Source(Transform t) => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) ?? "";
        static string Hash(string path) { using (var sha = SHA256.Create()) using (var f = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", ""); }
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            string requestPath = Path.Combine(Stage, "flower-extension-build-request.json"); if (!File.Exists(requestPath)) return;
            var result = new Result { utc = DateTime.UtcNow.ToString("O") };
            try
            {
                var request = JsonUtility.FromJson<Request>(File.ReadAllText(requestPath)); File.Delete(requestPath);
                string frozen = Path.Combine(Stage, "landscape-plan.json");
                if (request == null || request.action != "build-read-only-color-extension" || Hash(frozen) != request.frozenPlanSha256 || Hash(NightMapSnapshot.ScenePath) != request.expectedSceneSha256)
                    throw new InvalidOperationException("Explicit request plus exact frozen plan and scene hashes are required.");
                result.frozenPlanSha256 = request.frozenPlanSha256;
                Plan plan = JsonUtility.FromJson<Plan>(File.ReadAllText(frozen));
                if (plan.version != "night-map-reuse-v2" || plan.status != "PLANNED_NOT_APPLIED" || plan.dirtyBefore || plan.sceneSha256 != request.expectedSceneSha256)
                    throw new InvalidOperationException("Only the reviewed, unapplied clean v2 baseline may be extended.");
                object context = Call("ContextFor", true);
                var all = (Transform[])Field(context, "all"); var terrain = (Terrain)Field(context, "terrain");
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene(); result.dirtyBefore = scene.isDirty;
                result.beforeHash = (string)Call("SerializedSceneHash", (object)all);
                Call("GuardBaseline", context, plan, plan.before);
                LifePlantSpecies gathering = Resources.Load<LifePlantSpecies>("LifeContent/GatheringGrass");
                if (!gathering) throw new InvalidOperationException("Gathering mesh whitelist is unavailable; refuse unknown resource impact.");
                var ownedMoves = new HashSet<string>(plan.moves.Select(m => m.id));
                foreach (Transform t in all.Where(t => t.name.StartsWith("R07_BlueFlower", StringComparison.Ordinal) || t.name.StartsWith("R10_OrangeFlower", StringComparison.Ordinal)).Take(8))
                    result.sourceSamples.Add(t.name + " | " + Source(t) + " | nearestRootSelf=" + (PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject) == t.gameObject) + " | components=" +
                        string.Join(",", t.GetComponentsInChildren<Component>(true).Select(c => c ? c.GetType().FullName : "MISSING")) + " | LODlevels=" +
                        string.Join(",",t.GetComponentsInChildren<LODGroup>(true).Select(g=>g.GetLODs().Length.ToString())) + " | size=" + BoundsOf(t).size);
                Transform[] sources = Filter(result, "active", all, t => t.gameObject.activeInHierarchy);
                sources = Filter(result, "generated_source_folder", sources, t => Source(t).StartsWith("Assets/Game/World/WatersideV2/Generated/Nature/Plants/Prefabs/", StringComparison.Ordinal));
                sources = Filter(result, "R07_or_R10_family", sources, t => Path.GetFileName(Source(t)).StartsWith("R07_BlueFlower_", StringComparison.Ordinal) || Path.GetFileName(Source(t)).StartsWith("R10_OrangeFlower_", StringComparison.Ordinal));
                sources = Filter(result, "nearest_prefab_root", sources, t => PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject) == t.gameObject);
                sources = Filter(result, "not_owned_move", sources, t => !ownedMoves.Contains(Id(t)));
                sources = Filter(result, "root_mesh_filter", sources, t => t.GetComponent<MeshFilter>());
                sources = Filter(result, "existing_static_clone_guard", sources, t => (bool)Call("SafeCloneSource", t));
                sources = Filter(result, "only_transform_filter_renderer_LOD", sources, t => t.GetComponentsInChildren<Component>(true).All(c => c && (c is Transform || c is MeshFilter || c is MeshRenderer || c is LODGroup)));
                sources = Filter(result, "LOD_renderers_owned_and_not_missing", sources, LodReferencesSafe);
                sources = Filter(result, "no_missing_or_G2_harvest_mesh", sources, t => t.GetComponentsInChildren<MeshFilter>(true).All(m => m.sharedMesh && !gathering.Matches(m.sharedMesh)));
                sources = Filter(result, "small_source_bounds", sources, t => BoundsOf(t).size.y <= .8f && BoundsOf(t).size.x <= 1.5f && BoundsOf(t).size.z <= 1.5f).OrderBy(Id).ToArray();
                foreach (Transform source in sources)
                    result.sources.Add(new SourceRow { id = Id(source), path = (string)Call("PathOf", source), prefab = Source(source), mesh = AssetDatabase.GetAssetPath(source.GetComponent<MeshFilter>().sharedMesh),
                        transforms = source.GetComponentsInChildren<Transform>(true).Length, renderers = source.GetComponentsInChildren<Renderer>(true).Length, colliders = source.GetComponentsInChildren<Collider>(true).Length,
                        lodGroups = source.GetComponentsInChildren<LODGroup>(true).Length, lodLevels = source.GetComponentsInChildren<LODGroup>(true).Select(g=>g.GetLODs().Length).ToArray() });
                result.plannedA01Blue = Add(context, terrain, plan, sources, "A01", "R07_BlueFlower", "浅蓝花小群", 4);
                result.plannedA03Blue = Add(context, terrain, plan, sources, "A03", "R07_BlueFlower", "浅蓝花小群", 4);
                result.plannedA06Orange = Add(context, terrain, plan, sources, "A06", "R10_OrangeFlower", "橙花小群", 4);
                plan.notes.Add("Independent color extension: full existing generated R07/R10 prefab instances are cloned unchanged; sources are not moved. No PlantSpecimen or other component is stripped, and no mesh/material is created. Native GatheringGrass mesh whitelist rejects all selected source meshes. Failed placement points are skipped.");
                plan.notes.Add("Frozen v2 doorway/approach metadata is retained as historical data, not corrected door traversal evidence. A03 east, A06 south and A09 open-sided entrances were observed in later views. Actual traversal remains pending.");
                Call("GuardBaseline", context, plan, plan.before);
                result.afterHash = (string)Call("SerializedSceneHash", (object)all); result.dirtyAfter = scene.isDirty;
                if (result.beforeHash != result.afterHash || result.dirtyAfter || Hash(frozen) != request.frozenPlanSha256)
                    throw new InvalidOperationException("Read-only state/frozen-plan guard failed.");
                result.totalGroups = plan.additions.Count; result.addedRenderers = plan.addedRenderers; result.addedTransforms = plan.addedTransforms; result.addedColliders = plan.addedColliders;
                if (plan.additions.Count > 32 || plan.addedRenderers > 120 || plan.addedTransforms > 240 || plan.addedColliders > 48) throw new InvalidOperationException("Extended budget exceeded.");
                result.output = Path.Combine(Stage, "landscape-plan-color-extension.json"); File.WriteAllText(result.output, JsonUtility.ToJson(plan, true)); result.outputSha256 = Hash(result.output);
                result.status = result.plannedA01Blue == 4 && result.plannedA03Blue == 4 && result.plannedA06Orange == 4 ? "PASS_READ_ONLY_12_COLOR_CLONES_REVIEW_PENDING" : "PARTIAL_READ_ONLY_COLOR_CLONES_REVIEW_REQUIRED";
            }
            catch (Exception e) { result.status = "FAIL_READ_ONLY_COLOR_EXTENSION"; result.error = e.ToString(); result.dirtyAfter = UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty; }
            File.WriteAllText(Path.Combine(Stage, "flower-extension-build-result.json"), JsonUtility.ToJson(result, true));
        }
        static Transform[] Filter(Result result, string label, Transform[] pool, Func<Transform,bool> keep)
        {
            Transform[] next = pool.Where(keep).ToArray(); result.sourceFilters.Add(new FilterRow { filter = label, survivors = next.Length }); return next;
        }
        static bool LodReferencesSafe(Transform root)
        {
            foreach (LODGroup group in root.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] levels = group.GetLODs();
                if (levels.Length == 0 || !levels.Any(level => level.renderers.Length > 0) ||
                    levels.Any(level => level.renderers.Any(renderer => !renderer || !(renderer.transform == root || renderer.transform.IsChildOf(root))))) return false;
            }
            return true;
        }
        static int Add(object context, Terrain terrain, Plan plan, Transform[] sources, string asset, string family, string label, int wanted)
        {
            var node = plan.nodes.Single(n => Path.GetFileNameWithoutExtension(n.source) == asset);
            var patch = plan.moves.Where(m => m.node == asset).Select(m => m.afterBounds.center).ToArray();
            if (patch.Length == 0) { plan.notes.Add(asset + ": no reviewed flower patch; no arbitrary extension location chosen."); return 0; }
            Vector3 center = patch.Aggregate(Vector3.zero, (a, b) => a + b) / patch.Length;
            var pool = sources.Where(t => Path.GetFileName(Source(t)).StartsWith(family + "_", StringComparison.Ordinal))
                .OrderBy(t => (BoundsOf(t).center - center).sqrMagnitude).ThenBy(Id).ToArray();
            var reserved = plan.moves.Select(m => m.afterBounds).Concat(plan.additions.Select(a => a.targetBounds)).ToList();
            int placed = 0;
            for (int trial = 0; trial < 360 && placed < wanted && pool.Length > 0; trial++)
            {
                float angle = trial * 2.39996323f, radius = Mathf.Sqrt(((trial * 37) % 101 + .5f) / 101f);
                Vector3 target = center + new Vector3(Mathf.Cos(angle) * 3.7f * radius, 0, Mathf.Sin(angle) * 2.2f * radius);
                Transform source = pool[placed % pool.Length]; Bounds old = BoundsOf(source); object[] args = { context, target, old, reserved, Vector3.zero };
                if (!(bool)Call("SafePlantPoint", args)) continue;
                Vector3 delta = (Vector3)args[4]; Bounds next = old; next.center += delta;
                if ((bool)Call("Protected", context, terrain.transform, next) || plan.nodes.Any(n => n.buildingBounds.Intersects(next)) ||
                    plan.additions.Any(a => a.targetBounds.Intersects(next)) || plan.moves.Any(m => m.afterBounds.Intersects(next)) ||
                    node.approach.Any(p => Mathf.Abs(p.x-next.center.x)<next.extents.x+.65f && Mathf.Abs(p.z-next.center.z)<next.extents.z+.65f) ||
                    Physics.OverlapBox(next.center, next.extents + Vector3.one * .05f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Any(c => !(c is TerrainCollider))) continue;
                int rc = source.GetComponentsInChildren<Renderer>(true).Length, tc = source.GetComponentsInChildren<Transform>(true).Length;
                if (plan.additions.Count >= 32 || plan.addedRenderers + rc > 120 || plan.addedTransforms + tc > 240) break;
                plan.additions.Add(new Addition { sourceId = Id(source), sourcePath = (string)Call("PathOf", source), name = node.name + " · " + label + " " + (placed + 1), node = node.name,
                    purpose = "Full existing " + family + " prefab clone; color accent outside existing white flower patch; non-harvestable mesh whitelist checked.",
                    position = source.position + delta, sourceBounds = old, targetBounds = next, transforms = tc, renderers = rc, colliders = 0 });
                plan.addedRenderers += rc; plan.addedTransforms += tc; reserved.Add(next); placed++;
            }
            plan.notes.Add(asset + ": " + placed + "/" + wanted + " " + family + " complete static clone accents; no threshold was relaxed."); return placed;
        }
    }
}
