using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.WatersideV2;
using WeatherHouse.WildlifeJourney;
using Object = UnityEngine.Object;

// Explicit Editor migration. Preflight never initializes animals or edits navigation arrays.
public static class JourneyAlternativeGraphIntegration
{
    const string ScenePath = "Assets/Scenes/WeatherHouse_WatersideV2.unity";
    const string ReportRoot = "JourneyReview/Revision_20261024/Departure/GraphIntegration/Runs";
    [Serializable] public sealed class Graph { public JourneyNode[] nodes; public JourneyLink[] links; }
    [Serializable] public sealed class Body
    {
        public int memberId; public Vector3 center, half; public float radius; public bool measured, canSwim;
        public float wadeDepth, maxSwimDistance;
    }
    [Serializable] public sealed class EdgeCheck
    {
        public int sourceFrom, sourceTo, from, to, bridgeId, memberId;
        public Vector3 start, end; public bool forward, reverse;
        public int forwardSamples, reverseSamples; public string modes;
        public int[] forwardBridgeIds, reverseBridgeIds; public bool bridgeIdentityMatches;
    }
    [Serializable] public sealed class Report
    {
        public string status, error, folder, scenePath, sceneSha256, componentId, componentSha256;
        public string terrainPath, terrainSha256, layoutSha256, runtimeAssemblyId, graphSha256, afterComponentSha256;
        public int oldNodes, oldLinks, alternativeNodes, alternativeLinks, newNodes, newLinks, largestMemberId;
        public string[] affectedProperties = { "JourneyNavigation.nodes", "JourneyNavigation.links" };
        public bool serializedComponentUnchanged, herdUnchanged, sceneFileUnchanged, sceneDirty, saved;
        public List<Body> bodies = new List<Body>();
        public List<EdgeCheck> selection = new List<EdgeCheck>(), checks = new List<EdgeCheck>();
        public Graph alternative, candidate;
    }
    sealed class Context
    {
        public JourneyHerd herd; public JourneyNavigation nav; public JourneyProfile[] profiles;
        public string navJson, herdJson; public Report report;
    }

    public static string Preflight()
    {
        var report = new Report { status = "PREFLIGHT_RUNNING", scenePath = ScenePath,
            folder = Path.Combine(ReportRoot, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6)) };
        Directory.CreateDirectory(report.folder);
        Context context = null;
        try
        {
            context = Current(report);
            Prepare(context);
            report.status = "PREFLIGHT_PASS_CURRENT_MAP_STATIC_EDGES_ONLY";
        }
        catch (Exception error) { report.status = "PREFLIGHT_FAIL_NO_APPLY"; report.error = error.ToString(); }
        finally
        {
            if (context != null)
            {
                report.serializedComponentUnchanged = EditorJsonUtility.ToJson(context.nav) == context.navJson;
                report.herdUnchanged = EditorJsonUtility.ToJson(context.herd) == context.herdJson;
                report.sceneFileUnchanged = FileHash(ScenePath) == report.sceneSha256;
                report.sceneDirty = context.nav.gameObject.scene.isDirty;
                if (!report.serializedComponentUnchanged || !report.herdUnchanged || !report.sceneFileUnchanged || report.sceneDirty)
                { report.status = "PREFLIGHT_FAIL_STATE_CHANGED"; report.error += "\nRead-only invariants failed; do not apply."; }
            }
            Write(report.folder, "preflight.json", report);
        }
        return JsonUtility.ToJson(report, true);
    }

    static Context Current(Report report)
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating, "Stable Edit mode required.");
        var scene = EditorSceneManager.GetActiveScene();
        Require(scene.path == ScenePath && !scene.isDirty && EditorSceneManager.sceneCount == 1, "Expected one saved, clean main scene; no scene will be opened automatically.");
        var herds = Object.FindObjectsOfType<JourneyHerd>().Where(h => h.gameObject.scene == scene).ToArray();
        Require(herds.Length == 1 && herds[0].navigation, "Expected exactly one main JourneyHerd and its existing Navigation.");
        var herd = herds[0]; var nav = herd.navigation;
        Require(nav.gameObject.scene == scene && nav.surfaceSampler == null && nav.terrain && nav.terrain.terrainData, "Real main terrain and physics navigation required; no injected sampler.");
        Require(!EditorUtility.IsDirty(nav.terrain.terrainData), "TerrainData has unsaved edits; save/review its baseline first.");
        Require(nav.nodes != null && nav.links != null && nav.nodes.Length > 0, "Existing graph missing.");
        Require(!nav.nodes.Any(n => n != null && n.id != null && n.id.StartsWith("Alternative ", StringComparison.Ordinal)), "Alternative nodes already present; refusing duplicate append.");
        ValidateGraph(new Graph { nodes = nav.nodes, links = nav.links });
        Require(herd.members.Length > 0 && herd.members.Length <= 64 && herd.members.All(m => m != null && m.root && m.profile != null), "All existing members must have real roots and profiles.");
        var context = new Context { herd = herd, nav = nav, report = report,
            navJson = EditorJsonUtility.ToJson(nav), herdJson = EditorJsonUtility.ToJson(herd) };
        report.sceneSha256 = FileHash(ScenePath);
        report.componentId = GlobalObjectId.GetGlobalObjectIdSlow(nav).ToString();
        report.componentSha256 = Hash(context.navJson);
        report.terrainPath = AssetDatabase.GetAssetPath(nav.terrain.terrainData);
        report.terrainSha256 = FileHash(report.terrainPath);
        report.layoutSha256 = FileHash("Assets/Game/World/WatersideV2/WatersideLayout.cs");
        report.runtimeAssemblyId = typeof(JourneyNavigation).Assembly.ManifestModule.ModuleVersionId.ToString();
        report.oldNodes = nav.nodes.Length; report.oldLinks = nav.links.Length;
        return context;
    }

    static void Prepare(Context context)
    {
        var report = context.report; var nav = context.nav;
        // Identical measurement to JourneyHerd.Initialize, on copies of serialized profiles.
        context.profiles = context.herd.members.Select(member =>
        {
            var profile = JsonUtility.FromJson<JourneyProfile>(JsonUtility.ToJson(member.profile));
            profile.bodyEnvelope = WildlifeBodyEnvelope.Measure(member.root, profile.bodyRadius).WithLength(member.bodyLength);
            profile.bodyHeading = member.root.rotation; profile.bodyOwner = member.root;
            Require(profile.bodyEnvelope.Valid && profile.bodyEnvelope.measured, "A real measured body is required for member " + member.id);
            report.bodies.Add(new Body { memberId = member.id, center = profile.bodyEnvelope.center, half = profile.bodyEnvelope.half,
                radius = profile.bodyEnvelope.HorizontalRadius, measured = profile.bodyEnvelope.measured, canSwim = profile.canSwim,
                wadeDepth = profile.wadeDepth, maxSwimDistance = profile.maxSwimDistance });
            return profile;
        }).ToArray();
        int largest = Enumerable.Range(0, context.profiles.Length).OrderByDescending(i => context.profiles[i].bodyEnvelope.HorizontalRadius).First();
        report.largestMemberId = context.herd.members[largest].id;
        Physics.SyncTransforms();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        report.alternative = Build(nav, context.profiles[largest], report, timer);
        report.alternativeNodes = report.alternative.nodes.Length; report.alternativeLinks = report.alternative.links.Length;
        report.candidate = Merge(new Graph { nodes = nav.nodes, links = nav.links }, report.alternative);
        report.newNodes = report.candidate.nodes.Length; report.newLinks = report.candidate.links.Length;
        report.graphSha256 = Hash(JsonUtility.ToJson(report.candidate));
        // Re-check merged endpoints: XZ reuse can retain the original node's different Y.
        foreach (JourneyLink edge in report.candidate.links.Skip(nav.links.Length))
        {
            for (int i = 0; i < context.profiles.Length; i++)
            {
                CheckBudget(timer);
                var check = Trace(nav, context.profiles[i], report.candidate.nodes[edge.from].position, report.candidate.nodes[edge.to].position);
                check.from = edge.from; check.to = edge.to; check.bridgeId = edge.bridgeId; check.memberId = context.herd.members[i].id;
                check.bridgeIdentityMatches = MatchesBridge(check.forwardBridgeIds, edge.bridgeId) && MatchesBridge(check.reverseBridgeIds, edge.bridgeId);
                report.checks.Add(check);
            }
        }
        Require(report.checks.All(c => c.forward && c.reverse && c.bridgeIdentityMatches), "At least one appended edge failed real-body forward/reverse tracing or bridge identity on the current map; inspect checks. No graph will be changed.");
        ValidateGraph(report.candidate);
    }

    // Same point list, shortcut limits and farthest-clear selection as JourneyAlternativeProbe.Build.
    static Graph Build(JourneyNavigation nav, JourneyProfile profile, Report report, System.Diagnostics.Stopwatch timer)
    {
        var points = new List<Vector3>(WatersideLayout.Routes[1]); var route = WatersideLayout.Routes[0];
        int oldTree = Enumerable.Range(0, route.Length).OrderBy(i => Flat(route[i], WatersideLayout.OldTree)).First();
        for (int i = 1; i <= oldTree; i++) points.Add(route[i] + (i == 25 ? Vector3.right * .4f : Vector3.zero));
        var nodes = new List<JourneyNode> { new JourneyNode { id = "Alternative start", position = points[0] } };
        var links = new List<JourneyLink>(); int current = 0;
        while (current < points.Count - 1)
        {
            int chosen = -1, bridge = 0;
            for (int next = Math.Min(points.Count - 1, current + 12); next > current; next--)
            {
                CheckBudget(timer);
                if (Flat(points[current], points[next]) > 16 || Deviates(points, current, next, .25f)) continue;
                var forward = nav.TraceDirectRoute(points[current], points[next], profile);
                var ids = forward == null ? new int[0] : forward.segments.Select(s => s.bridgeId).Where(id => id > 0).Distinct().ToArray();
                bool usable = forward != null && ids.Length <= 1;
                var backward = usable ? nav.TraceDirectRoute(points[next], points[current], profile) : null;
                report.selection.Add(new EdgeCheck { sourceFrom = current, sourceTo = next, start = points[current], end = points[next],
                    forward = usable, reverse = backward != null, forwardSamples = forward?.segments.Count ?? 0, reverseSamples = backward?.segments.Count ?? 0,
                    memberId = report.largestMemberId, bridgeId = ids.FirstOrDefault() });
                if (!usable || backward == null) continue;
                chosen = next; bridge = ids.FirstOrDefault(); break;
            }
            Require(chosen >= 0, "No safe authored edge at source index " + current + " point=" + points[current].ToString("F6"));
            nodes.Add(new JourneyNode { id = "Alternative source " + chosen, position = points[chosen] });
            links.Add(new JourneyLink { from = nodes.Count - 2, to = nodes.Count - 1, bridgeId = bridge });
            current = chosen;
        }
        return new Graph { nodes = nodes.ToArray(), links = links.ToArray() };
    }

    public static Graph Merge(Graph original, Graph alternative)
    {
        ValidateGraph(original); ValidateGraph(alternative);
        var nodes = new List<JourneyNode>(original.nodes); var links = new List<JourneyLink>(original.links);
        var remap = new int[alternative.nodes.Length];
        for (int i = 0; i < remap.Length; i++)
        {
            JourneyNode node = alternative.nodes[i]; int existing = nodes.FindIndex(n => Flat(n.position, node.position) < .02f);
            remap[i] = existing >= 0 ? existing : nodes.Count;
            if (existing < 0) nodes.Add(node);
        }
        foreach (JourneyLink link in alternative.links)
            links.Add(new JourneyLink { from = remap[link.from], to = remap[link.to], bridgeId = link.bridgeId, bidirectional = link.bidirectional });
        var result = new Graph { nodes = nodes.ToArray(), links = links.ToArray() }; ValidateGraph(result); return result;
    }

    // Reads only a previously generated report; never accepts arrays supplied by a report as trusted input.
    public static string Apply(string preflightPath)
    {
        string full = Path.GetFullPath(preflightPath), root = Path.GetFullPath(ReportRoot) + Path.DirectorySeparatorChar;
        Require(full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full) == "preflight.json", "Use a generated preflight report inside the owned Runs directory.");
        var expected = JsonUtility.FromJson<Report>(File.ReadAllText(full));
        Require(expected.status == "PREFLIGHT_PASS_CURRENT_MAP_STATIC_EDGES_ONLY", "Only a passing preflight may be applied.");
        var fresh = new Report { folder = Path.GetDirectoryName(full), scenePath = ScenePath };
        var context = Current(fresh); var nav = context.nav;
        Require(fresh.sceneSha256 == expected.sceneSha256 && fresh.componentId == expected.componentId && fresh.componentSha256 == expected.componentSha256,
            "Saved scene or Navigation component changed after preflight; no overwrite.");
        Require(fresh.terrainSha256 == expected.terrainSha256 && fresh.layoutSha256 == expected.layoutSha256 && fresh.runtimeAssemblyId == expected.runtimeAssemblyId,
            "Terrain, layout or loaded runtime changed; rerun preflight.");
        Prepare(context);
        Require(fresh.graphSha256 == expected.graphSha256 && EditorJsonUtility.ToJson(nav) == context.navJson && EditorJsonUtility.ToJson(context.herd) == context.herdJson,
            "Fresh physical candidate or read-only state differs; rerun preflight.");
        var beforeNodes = nav.nodes; var beforeLinks = nav.links;
        File.WriteAllText(Path.Combine(fresh.folder, "navigation-before.json"), context.navJson, new UTF8Encoding(false));
        bool changed = false;
        try
        {
            nav.nodes = fresh.candidate.nodes; nav.links = fresh.candidate.links; nav.Rebuild(); changed = true;
            string after = EditorJsonUtility.ToJson(nav);
            // Compare the entire remaining component, including every reference and tuning field.
            nav.nodes = beforeNodes; nav.links = beforeLinks;
            Require(EditorJsonUtility.ToJson(nav) == context.navJson, "Unexpected non-graph property change.");
            nav.nodes = fresh.candidate.nodes; nav.links = fresh.candidate.links;
            Require(FileHash(ScenePath) == fresh.sceneSha256 && EditorJsonUtility.ToJson(context.herd) == context.herdJson, "Scene file or herd changed during Apply.");
            File.WriteAllText(Path.Combine(fresh.folder, "navigation-after.json"), after, new UTF8Encoding(false));
            fresh.afterComponentSha256 = Hash(after); fresh.status = "APPLIED_NAV_ONLY_NOT_SAVED_REQUIRES_MAIN_PLAY";
            fresh.sceneFileUnchanged = true; fresh.herdUnchanged = true; fresh.sceneDirty = true; fresh.saved = false;
            Write(fresh.folder, "apply.json", fresh);
            EditorUtility.SetDirty(nav); EditorSceneManager.MarkSceneDirty(nav.gameObject.scene);
        }
        catch
        {
            if (changed) { nav.nodes = beforeNodes; nav.links = beforeLinks; nav.Rebuild(); }
            throw;
        }
        return JsonUtility.ToJson(fresh, true);
    }

    static EdgeCheck Trace(JourneyNavigation nav, JourneyProfile profile, Vector3 from, Vector3 to)
    {
        var forward = nav.TraceDirectRoute(from, to, profile); var reverse = nav.TraceDirectRoute(to, from, profile);
        return new EdgeCheck { start = from, end = to, forward = forward != null, reverse = reverse != null,
            forwardSamples = forward?.segments.Count ?? 0, reverseSamples = reverse?.segments.Count ?? 0,
            forwardBridgeIds = BridgeIds(forward), reverseBridgeIds = BridgeIds(reverse),
            modes = forward == null ? "" : string.Join(",", forward.segments.Select(s => s.mode.ToString()).Distinct()) };
    }
    static int[] BridgeIds(JourneyRoute route) => route == null ? new int[0] : route.segments.Select(s => s.bridgeId).Where(id => id > 0).Distinct().ToArray();
    static bool MatchesBridge(int[] ids, int expected) => expected == 0 ? ids.Length == 0 : ids.Length == 1 && ids[0] == expected;

    // Explicit rollback is limited to this unsaved Apply and checks the complete current component.
    public static string Rollback(string preflightPath)
    {
        string full = Path.GetFullPath(preflightPath), root = Path.GetFullPath(ReportRoot) + Path.DirectorySeparatorChar;
        Require(full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full) == "preflight.json", "Use the owned preflight report.");
        string folder = Path.GetDirectoryName(full);
        var before = JsonUtility.FromJson<Report>(File.ReadAllText(full));
        var applied = JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(folder, "apply.json")));
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating,
            "Stable Edit mode required.");
        var scene = EditorSceneManager.GetActiveScene();
        Require(scene.path == ScenePath && FileHash(ScenePath) == before.sceneSha256, "Scene was saved/changed after Apply; do not roll it back blindly.");
        var nav = Object.FindObjectsOfType<JourneyNavigation>().Single(n => GlobalObjectId.GetGlobalObjectIdSlow(n).ToString() == before.componentId);
        Require(Hash(EditorJsonUtility.ToJson(nav)) == applied.afterComponentSha256, "Navigation was edited after Apply; preserve those edits.");
        string json = File.ReadAllText(Path.Combine(folder, "navigation-before.json"));
        Require(Hash(json) == before.componentSha256, "Before snapshot signature mismatch.");
        var original = ReadSnapshot(json);
        var afterNodes = nav.nodes; var afterLinks = nav.links;
        try
        {
            nav.nodes = original.nodes; nav.links = original.links; nav.Rebuild();
            Require(EditorJsonUtility.ToJson(nav) == json, "Rollback would change non-graph component data.");
            EditorUtility.SetDirty(nav); EditorSceneManager.MarkSceneDirty(scene);
        }
        catch { nav.nodes = afterNodes; nav.links = afterLinks; nav.Rebuild(); throw; }
        return "ROLLED_BACK_NAV_ARRAYS_ONLY_NOT_SAVED; scene remains dirty for owner review.";
    }
    internal static Graph ReadSnapshot(string json)
    {
        // EditorJsonUtility's Unity-type wrapper is not a normal JsonUtility data field.
        const string prefix = "{\"MonoBehaviour\":";
        string trimmed = json == null ? "" : json.Trim();
        Require(trimmed.StartsWith(prefix, StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal), "Expected the captured EditorJsonUtility MonoBehaviour wrapper.");
        var graph = JsonUtility.FromJson<Graph>(trimmed.Substring(prefix.Length, trimmed.Length - prefix.Length - 1));
        ValidateGraph(graph); return graph;
    }
    static bool Deviates(List<Vector3> points, int from, int to, float tolerance)
    {
        Vector3 a = points[from], b = points[to]; a.y = b.y = 0; Vector3 delta = b - a;
        for (int i = from + 1; i < to; i++)
        {
            Vector3 p = points[i]; p.y = 0;
            float t = delta.sqrMagnitude < .0001f ? 0 : Mathf.Clamp01(Vector3.Dot(p - a, delta) / delta.sqrMagnitude);
            if (Vector3.Distance(p, a + delta * t) > tolerance) return true;
        }
        return false;
    }
    static void ValidateGraph(Graph graph)
    {
        Require(graph != null && graph.nodes != null && graph.links != null && graph.nodes.All(n => n != null && Finite(n.position)), "Graph contains missing/nonfinite nodes.");
        Require(graph.links.All(l => l != null && l.from >= 0 && l.to >= 0 && l.from < graph.nodes.Length && l.to < graph.nodes.Length && l.from != l.to && l.bridgeId >= 0), "Graph contains an invalid link.");
    }
    static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
    static void CheckBudget(System.Diagnostics.Stopwatch timer) { Require(timer.Elapsed.TotalSeconds < 90, "Bounded 90-second physical preflight exceeded; no partial apply."); }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Write(string folder, string name, Report report) => File.WriteAllText(Path.Combine(folder, name), JsonUtility.ToJson(report, true), new UTF8Encoding(false));
    static string FileHash(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }
    static string Hash(string value) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", ""); }
}
