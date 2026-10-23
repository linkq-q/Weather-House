using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.WatersideV2;
using Object = UnityEngine.Object;

namespace WeatherHouse.WildlifeJourney.Editor
{
    /// <summary>Transfers the existing Waterside sheep instances after Animals() has finished building them.</summary>
    public static class JourneyWatersideIntegration
    {
        public static JourneyHerd Integrate(WildlifePopulation population, Camera viewCamera = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !population || !population.terrain || !population.waterSurface)
                throw new InvalidOperationException("Build the Journey sheep outside Play with the actual terrain and world water surface.");
            if (EditorUtility.IsPersistent(population) || population.Initialized)
                throw new InvalidOperationException("Integrate newly built scene instances before WildlifePopulation.Initialize().");
            JourneyHerd existing = population.GetComponentInChildren<JourneyHerd>(true);
            if (existing) return existing;
            string reportPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "JourneyReview", "waterside-integration.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, "RUNNING " + DateTime.UtcNow.ToString("O") + "\n");
            bool diagnosticWritten = false, transferStarted = false;
            WildlifeAgent[] sheep = population.agents.Where(a => a && a.speciesId == "sheep").OrderBy(a => a.spawnId).ToArray();
            if (sheep.Length != 10 || sheep.Any(a => EditorUtility.IsPersistent(a) || a.gameObject.scene != population.gameObject.scene || !a.visualRoot || !a.animator))
                throw new InvalidOperationException("Expected ten existing sheep scene instances with valid animation bindings.");
            AnimationClip swim = JourneySwimImport.Load();
            var root = new GameObject("Journey 羊群 · 草地与老树会合");
            root.transform.SetParent(population.transform, false);
            try
            {
                var navigation = root.AddComponent<JourneyNavigation>();
                navigation.terrain = population.terrain;
                navigation.worldWaterSurface = population.waterSurface;
                navigation.groundMask = navigation.obstacleMask = Physics.DefaultRaycastLayers;
                navigation.endpointConnectionRadius = 12;
                navigation.surfaceProbeHeight = 65;
                navigation.maxSlope = population.maxSlope;
                navigation.bridges = BridgeSurfaces(population);
                Physics.SyncTransforms();

                var herd = root.AddComponent<JourneyHerd>();
                herd.navigation = navigation; herd.seed = population.worldSeed; herd.surroundingPopulation = population;
                // This corridor spans the real map, so its travel deadline is longer than the compact lab's.
                herd.rendezvousTimeout = 360; herd.minimumStay = 24; herd.autoAdvance = true;
                herd.sites = new[] {
                    // The lake terrace has 10 m / 7 m radii; its inner clearing lies directly on the authored main route.
                    new JourneyActivitySite { name = "湖阶羊群草地", center = Ground(population.terrain, WatersideLayout.LakeTerrace), radius = 5.5f, forage = 1, rest = .8f },
                    new JourneyActivitySite { name = "老树旁草地", center = Ground(population.terrain, WatersideLayout.OldTree), radius = 5.5f, forage = .9f, rest = 1 }
                };
                var scales = new float[sheep.Length];
                herd.members = sheep.Select((agent, i) => Member(agent, i, population.worldSeed, swim, out scales[i])).ToArray();
                ConfigureCorridor(navigation, herd.sites, herd.members);
                float spacing = herd.members.Max(m => m.bodyLength) + .18f;
                var widest = new JourneyProfile { canSwim = false, bodyRadius = herd.members.Max(m => m.profile.bodyRadius) };
                Vector3[] home = Place(navigation, herd.sites[0], widest, spacing, sheep.Length, population.worldSeed);
                Vector3[] away = Place(navigation, herd.sites[1], widest, spacing, sheep.Length, population.worldSeed + 1);
                int bridgeRoutes = 0, swimRoutes = 0;
                for (int i = 0; i < herd.members.Length; i++)
                {
                    JourneyRoute outward = navigation.FindRoute(home[i], away[i], herd.members[i].profile);
                    JourneyRoute returnRoute = navigation.FindRoute(away[i], home[i], herd.members[i].profile);
                    if (outward == null || returnRoute == null)
                    {
                        string failure = "The real Waterside corridor cannot safely carry sheep " + herd.members[i].id +
                            " both ways. outward=" + (outward != null) + ", return=" + (returnRoute != null) + ". Existing sheep have not been transferred.";
                        File.WriteAllText(reportPath, DescribeRouteFailure(navigation, herd, home, away, i, failure));
                        diagnosticWritten = true;
                        throw new InvalidOperationException(failure + " Diagnostic: " + reportPath);
                    }
                    if (outward.segments.Any(s => s.bridgeId > 0)) bridgeRoutes++;
                    if (outward.segments.Any(s => s.mode == JourneyTravelMode.Swim)) swimRoutes++;
                }
                if (bridgeRoutes == 0) throw new InvalidOperationException("The proposed Waterside corridor did not exercise any real bridge surface.");
                if (!viewCamera)
                    viewCamera = population.gameObject.scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Camera>(true))
                        .FirstOrDefault(camera => camera.CompareTag("MainCamera"));
                var social = root.AddComponent<JourneySocial>();
                social.viewCamera = viewCamera; social.occlusionMask = Physics.DefaultRaycastLayers; herd.social = social;

                // Commit only after complete placement and both-way route validation. Keep every other species untouched.
                population.agents = population.agents.Where(agent => agent && agent.speciesId != "sheep").ToArray();
                transferStarted = true;
                for (int i = 0; i < sheep.Length; i++)
                {
                    WildlifeAgent agent = sheep[i];
                    agent.enabled = false;
                    agent.visualRoot.localScale *= scales[i]; agent.visualRoot.localPosition *= scales[i];
                    agent.transform.position = home[i];
                    Object.DestroyImmediate(agent);
                }
                string summary = "Waterside Journey integrated: 10 existing sheep; " + population.agents.Length + " original agents retained; " +
                    navigation.nodes.Length + " route nodes; safe swim entries=" +
                    navigation.nodes.Count(node => node.id.StartsWith("East fork optional swim ", StringComparison.Ordinal)) / 2 +
                    "; planned outward bridge users=" + bridgeRoutes + ", swimmers=" + swimRoutes + ".";
                File.WriteAllText(reportPath, "PASS " + DateTime.UtcNow.ToString("O") + "\n" + summary);
                Debug.Log(summary);
                return herd;
            }
            catch (Exception error)
            {
                string recovery = transferStarted
                    ? "Transfer started: Journey components retained for repair; original agents may already have been transferred."
                    : "Pre-transfer rollback: temporary Journey components removed; original agents remain unchanged.";
                try
                {
                    if (diagnosticWritten) File.AppendAllText(reportPath, "\n" + recovery + "\n");
                    else File.WriteAllText(reportPath, "FAIL " + DateTime.UtcNow.ToString("O") + "\n" + recovery + "\n" + error);
                }
                catch (Exception reportError)
                { Debug.LogWarning(recovery + " Could not write the integration failure report: " + reportError.Message); }
                finally
                { if (!transferStarted) Object.DestroyImmediate(root); }
                throw;
            }
        }

        private static string DescribeRouteFailure(JourneyNavigation nav, JourneyHerd herd, Vector3[] home, Vector3[] away, int failed, string failure)
        {
            var report = new StringBuilder("FAIL " + DateTime.UtcNow.ToString("O") + "\n" + failure + "\n");
            JourneyProfile profile = herd.members[failed].profile;
            report.AppendLine("SCENE " + nav.gameObject.scene.path + " terrain=" + nav.terrain.name + " water=" + nav.worldWaterSurface.GetType().Name +
                " maxSlope=" + N(nav.maxSlope) + " maxStepHeight=" + N(nav.maxStepHeight) + " connectionRadius=" + N(nav.endpointConnectionRadius) +
                " groundMask=" + nav.groundMask.value + " obstacleMask=" + nav.obstacleMask.value);
            for (int i = 0; i < herd.members.Length; i++)
            {
                JourneyMember m = herd.members[i];
                report.AppendLine("MEMBER id=" + m.id + " home=" + V(home[i]) + " away=" + V(away[i]) + " canSwim=" + m.profile.canSwim +
                    " radius=" + N(m.profile.bodyRadius) + " bodyLength=" + N(m.bodyLength) + " wade=" + N(m.profile.wadeDepth) +
                    " swimLimit=" + N(m.profile.maxSwimDistance) + " walkSpeed=" + N(m.profile.walkSpeed) + " swimSpeed=" + N(m.profile.swimSpeed));
            }
            MethodInfo trace = typeof(JourneyNavigation).GetMethod("Trace", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo attachments = typeof(JourneyNavigation).GetMethod("Attachments", BindingFlags.Instance | BindingFlags.NonPublic);
            if (trace == null || attachments == null) throw new InvalidOperationException("Navigation diagnostic entrypoints changed.");
            for (int i = 0; i < nav.nodes.Length; i++)
                report.AppendLine("NODE " + i + " " + nav.nodes[i].id + " " + PointStatus(nav, profile, nav.nodes[i].position));
            var traversable = new List<Tuple<int, int>>();
            for (int i = 0; i < nav.links.Length; i++)
            {
                JourneyLink link = nav.links[i];
                CheckEdge(link.from, link.to, i, link.bridgeId);
                if (link.bidirectional) CheckEdge(link.to, link.from, i, link.bridgeId);
            }
            HashSet<int> start = CheckAttachments("HOME", home[failed]);
            HashSet<int> goal = CheckAttachments("AWAY", away[failed]);
            Reachable("HOME->AWAY", start, goal);
            Reachable("AWAY->HOME", goal, start);
            report.AppendLine("Connectivity above tests each edge with initialSwimDistance=0; route search additionally carries continuous swimming distance between edges.");
            return report.ToString();

            bool Trace(Vector3 from, Vector3 to, int link, int bridge)
            { return trace.Invoke(nav, new object[] { from, to, profile, link, bridge, 0f }) != null; }
            void CheckEdge(int from, int to, int link, int bridge)
            {
                Vector3 a = nav.nodes[from].position, b = nav.nodes[to].position;
                bool legal = Trace(a, b, link, bridge);
                if (legal) traversable.Add(Tuple.Create(from, to));
                report.AppendLine("LINK " + link + " " + from + "->" + to + " bridgeId=" + bridge + " length=" + N(Flat(a, b)) +
                    " direction=" + V((b - a).normalized) + " trace=" + legal + " " + EdgeSamples(nav, profile, a, b));
            }
            HashSet<int> CheckAttachments(string label, Vector3 point)
            {
                var found = new HashSet<int>();
                var actual = (System.Collections.IEnumerable)attachments.Invoke(nav, new object[] { point, profile, 0f });
                foreach (object state in actual) found.Add((int)state.GetType().GetField("node").GetValue(state));
                report.AppendLine(label + " " + PointStatus(nav, profile, point) + " actualAttachedNodes=" + string.Join(",", found.OrderBy(i => i)));
                for (int i = 0; i < nav.nodes.Length; i++)
                {
                    Vector3 node = nav.nodes[i].position;
                    if (Flat(point, node) > nav.endpointConnectionRadius) continue;
                    report.AppendLine(label + " ATTACH " + i + " actual=" + found.Contains(i) + " directIn=" + Trace(point, node, -1, 0) +
                        " directOut=" + Trace(node, point, -1, 0) + " distance=" + N(Flat(point, node)) + " " + EdgeSamples(nav, profile, point, node));
                }
                return found;
            }
            void Reachable(string label, HashSet<int> entry, HashSet<int> exit)
            {
                var reached = new HashSet<int>(entry); var queue = new Queue<int>(entry);
                while (queue.Count > 0)
                {
                    int node = queue.Dequeue();
                    foreach (var edge in traversable) if (edge.Item1 == node && reached.Add(edge.Item2)) queue.Enqueue(edge.Item2);
                }
                report.AppendLine(label + " reachedNodes=" + string.Join(",", reached.OrderBy(i => i)) +
                    " reachableExitNodes=" + string.Join(",", exit.Where(reached.Contains).OrderBy(i => i)));
            }
        }

        private static string EdgeSamples(JourneyNavigation nav, JourneyProfile profile, Vector3 from, Vector3 to)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Flat(from, to) / .15f));
            float maxSlope = 0, maxDepth = 0, maxRise = 0; string firstFailure = "none";
            bool previousValid = nav.TryProject(profile, from, out Vector3 previous, out _);
            for (int i = 0; i <= steps; i++)
            {
                Vector3 point = Vector3.Lerp(from, to, (float)i / steps);
                JourneySurface surface = nav.SampleSurface(point);
                maxSlope = Mathf.Max(maxSlope, surface.slope);
                if (surface.hasWater) maxDepth = Mathf.Max(maxDepth, surface.waterY - surface.groundY);
                bool valid = nav.TryProject(profile, point, out Vector3 projected, out _);
                bool step = i == 0 || (previousValid && nav.TryStep(profile, previous, point, out _, out _));
                if (i > 0 && valid && previousValid) maxRise = Mathf.Max(maxRise, Mathf.Abs(projected.y - previous.y));
                if ((!valid || !step) && firstFailure == "none") firstFailure = "sample=" + i + " step=" + step + " " + PointStatus(nav, profile, point);
                previous = projected; previousValid = valid;
            }
            return "maxSlope=" + N(maxSlope) + " maxDepth=" + N(maxDepth) + " maxRisePer0.15m=" + N(maxRise) + " firstFailure={" + firstFailure + "}";
        }

        private static string PointStatus(JourneyNavigation nav, JourneyProfile profile, Vector3 point)
        {
            JourneySurface surface = nav.SampleSurface(point);
            bool projected = nav.TryProject(profile, point, out Vector3 feet, out JourneyTravelMode mode);
            Vector3 probe = new Vector3(point.x, surface.groundY + profile.bodyRadius + .04f, point.z);
            string collisions = string.Join("|", Physics.OverlapSphere(probe, Mathf.Max(.05f, profile.bodyRadius) * .85f,
                nav.obstacleMask, QueryTriggerInteraction.Ignore).Where(c => !(c is TerrainCollider) && c != surface.supportingCollider)
                .Select(c => c.name + "@" + V(c.bounds.center)));
            return "at=" + V(point) + " project=" + projected + " feet=" + V(feet) + " mode=" + mode + " valid=" + surface.valid +
                " blocked=" + surface.blocked + " slope=" + N(surface.slope) + " ground=" + N(surface.groundY) +
                " water=" + surface.hasWater + ":" + N(surface.waterY) + " bridgeId=" + surface.bridgeId +
                " support=" + (surface.supportingCollider ? surface.supportingCollider.name : "none") + " overlaps=[" + collisions + "]";
        }

        private static string N(float value) { return value.ToString("F3", CultureInfo.InvariantCulture); }
        private static string V(Vector3 value) { return value.ToString("F3"); }

        private static JourneyMember Member(WildlifeAgent agent, int index, int seed, AnimationClip swim, out float scale)
        {
            var random = new WildlifeRandom(seed, agent.spawnId, agent.speciesId);
            scale = random.Range(Mathf.Max(.1f, agent.scaleRange.x), Mathf.Max(.1f, agent.scaleRange.y));
            Transform[] bones = agent.animator.GetComponentsInChildren<Transform>();
            string[] paddle = { "UpperArm.L", "UpperArm.R", "Thigh.L", "Thigh.R" };
            Transform head = bones.FirstOrDefault(t => t.name == "Head");
            Transform[] legs = paddle.Select(name => bones.FirstOrDefault(t => t.name == name)).Where(t => t).ToArray();
            if (!head || legs.Length != 4 || agent.clips == null || !agent.clips.idle || !agent.clips.walk)
                throw new InvalidOperationException("The Waterside sheep rig does not match the verified sheep animation bindings.");
            return new JourneyMember { id = agent.spawnId + 1, root = agent.transform, visualRoot = agent.visualRoot,
                animator = agent.animator, clips = agent.clips, swimClip = swim, headBone = head, paddleBones = legs,
                sociability = agent.sociability, bodyLength = 1.25f * scale,
                profile = new JourneyProfile { bodyRadius = agent.bodyRadius * scale, canSwim = index < 4,
                    waterlineHeight = scale * (agent.visualRoot.localPosition.y + .64f * agent.visualRoot.localScale.y),
                    walkSpeed = agent.walkSpeed, swimSpeed = .78f, wadeDepth = .28f, maxSwimDistance = 8, waterAversion = .1f } };
        }

        private static JourneyBridgeSurface[] BridgeSurfaces(WildlifePopulation population)
        {
            Collider[] colliders = population.gameObject.scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Collider>(true)).ToArray();
            var result = new List<JourneyBridgeSurface>();
            for (int i = 0; i < WatersideLayout.Crossings.Length; i++)
            {
                string name = WatersideLayout.Crossings[i].Name;
                Collider[] deck = colliders.Where(c => c.enabled && c.transform.parent && c.transform.parent.name == name &&
                    (c.name.StartsWith("踏面 ", StringComparison.Ordinal) || c.name.StartsWith("错缝石桥面 ", StringComparison.Ordinal))).ToArray();
                if (deck.Length == 0) throw new InvalidOperationException("No actual walkable deck colliders were found for " + name);
                result.AddRange(deck.Select(c => new JourneyBridgeSurface { id = i + 1, surface = c }));
            }
            return result.ToArray();
        }

        private static void ConfigureCorridor(JourneyNavigation nav, JourneyActivitySite[] sites, JourneyMember[] members)
        {
            var nodes = new List<JourneyNode>();
            var links = new List<JourneyLink>();
            Vector3[] authored = WatersideLayout.Routes[0];
            int first = Nearest(authored, WatersideLayout.OldTree), last = Nearest(authored, WatersideLayout.Cape);
            if (first >= last) throw new InvalidOperationException("The authored OldTree-to-Cape route order changed; review the Journey adapter.");
            int previous = -1;
            for (int i = first; i <= last; i++)
            {
                int current = AddNode("Authored path " + i, authored[i]);
                if (previous >= 0)
                {
                    JourneySurface middle = nav.SampleSurface((nodes[previous].position + nodes[current].position) * .5f);
                    int bridge = middle.bridgeId;
                    if (bridge == 0) bridge = nav.SampleSurface(nodes[previous].position).bridgeId;
                    if (bridge == 0) bridge = nav.SampleSurface(nodes[current].position).bridgeId;
                    links.Add(new JourneyLink { from = previous, to = current, bridgeId = bridge });
                }
                previous = current;
            }
            int authoredCount = nodes.Count;
            foreach (JourneyActivitySite site in sites) Connect(AddNode(site.name, site.center));

            // The authored channel guides the search; actual water, slopes and colliders decide every entry.
            Vector3 crossingCenter = (WatersideLayout.Crossings[3].Start + WatersideLayout.Crossings[3].End) * .5f;
            var candidates = new List<(Vector3 center, Vector3 side, float shore)>();
            foreach (WatersideLayout.WaterChannel channel in WatersideLayout.WaterChannels)
                for (int i = 1; i < channel.Nodes.Length; i++)
                {
                    var a = channel.Nodes[i - 1]; var b = channel.Nodes[i];
                    Vector3 along = b.Position - a.Position; along.y = 0;
                    if (along.sqrMagnitude < .01f) continue;
                    Vector3 side = Vector3.Cross(Vector3.up, along.normalized);
                    int samples = Mathf.Max(1, Mathf.CeilToInt(along.magnitude / 2f));
                    for (int sample = 0; sample <= samples; sample++)
                    {
                        float t = sample / (float)samples;
                        Vector3 center = Vector3.Lerp(a.Position, b.Position, t);
                        if (Flat(center, crossingCenter) > 18 || !nav.worldWaterSurface.Contains(center)) continue;
                        candidates.Add((center, side, Mathf.Lerp(a.HalfWidth, b.HalfWidth, t) + 1.8f));
                    }
                }
            JourneyProfile[] swimmers = members.Where(m => m.profile.canSwim).Select(m => m.profile).ToArray();
            var dry = new JourneyProfile { canSwim = false, bodyRadius = members.Max(m => m.profile.bodyRadius) };
            var accepted = new List<Vector3>();
            foreach (var candidate in candidates.OrderBy(c => Flat(c.center, crossingCenter)))
            {
                if (swimmers.Length == 0 || accepted.Count == 2) break;
                if (accepted.Any(center => Flat(center, candidate.center) < 6f)) continue;
                if (!TryBank(candidate.center, -candidate.side, candidate.shore, out Vector3 left, out List<int> leftJoins) ||
                    !TryBank(candidate.center, candidate.side, candidate.shore, out Vector3 right, out List<int> rightJoins)) continue;
                bool swimmable = false;
                foreach (JourneyProfile swimmer in swimmers)
                {
                    JourneyRoute outward = nav.TraceDirectRoute(left, right, swimmer);
                    if (!SwimmingCrossing(outward)) continue;
                    if (SwimmingCrossing(nav.TraceDirectRoute(right, left, swimmer))) { swimmable = true; break; }
                }
                if (!swimmable) continue;
                int a = AddNode("East fork optional swim " + (accepted.Count + 1) + " bank A", left);
                int b = AddNode("East fork optional swim " + (accepted.Count + 1) + " bank B", right);
                foreach (int target in leftJoins) links.Add(new JourneyLink { from = a, to = target });
                foreach (int target in rightJoins) links.Add(new JourneyLink { from = b, to = target });
                links.Add(new JourneyLink { from = a, to = b });
                accepted.Add(candidate.center);
            }
            nav.nodes = nodes.ToArray(); nav.links = links.ToArray(); nav.Rebuild();

            bool TryBank(Vector3 center, Vector3 direction, float shore, out Vector3 point, out List<int> joins)
            {
                point = default; joins = null;
                for (int step = 0; step <= 4; step++)
                {
                    Vector3 requested = center + direction * (shore + step * .75f);
                    if (!nav.TryProject(dry, requested, out Vector3 projected, out JourneyTravelMode mode) || mode != JourneyTravelMode.Walk) continue;
                    var connected = new List<int>();
                    foreach (int target in Enumerable.Range(0, authoredCount)
                        .Where(i => Flat(nodes[i].position, projected) <= 18f)
                        .OrderBy(i => Flat(nodes[i].position, projected)))
                    {
                        JourneyRoute forward = nav.TraceDirectRoute(projected, nodes[target].position, dry);
                        if (forward == null || forward.segments.Any(s => s.mode != JourneyTravelMode.Walk)) continue;
                        JourneyRoute back = nav.TraceDirectRoute(nodes[target].position, projected, dry);
                        if (back == null || back.segments.Any(s => s.mode != JourneyTravelMode.Walk)) continue;
                        connected.Add(target);
                        if (connected.Count == 3) break;
                    }
                    if (connected.Count == 0) continue;
                    point = projected; joins = connected; return true;
                }
                return false;
            }
            bool SwimmingCrossing(JourneyRoute route)
            { return route != null && route.segments.Any(s => s.mode == JourneyTravelMode.Swim) && route.segments.All(s => s.bridgeId == 0); }

            int AddNode(string name, Vector3 position)
            {
                JourneySurface surface = nav.SampleSurface(position);
                if (!surface.valid) throw new InvalidOperationException("No real ground under route node " + name);
                position.y = surface.groundY;
                nodes.Add(new JourneyNode { id = name, position = position });
                return nodes.Count - 1;
            }
            void Connect(int index)
            {
                foreach (int target in Enumerable.Range(0, index)
                    .Where(i => nav.SampleSurface(nodes[i].position).bridgeId == 0 && !nav.worldWaterSurface.Contains(nodes[i].position))
                    .OrderBy(i => Flat(nodes[i].position, nodes[index].position)).Take(3))
                    if (Flat(nodes[target].position, nodes[index].position) <= 18)
                        links.Add(new JourneyLink { from = index, to = target });
            }
        }

        private static Vector3[] Place(JourneyNavigation nav, JourneyActivitySite site, JourneyProfile profile, float spacing, int count, int seed)
        {
            for (int attempt = 0; attempt < 4; attempt++)
                if (JourneyScatter.TryCreate(site.center, site.radius * .95f, spacing, count, unchecked(seed + attempt * 19349663), out Vector3[] points,
                    p => nav.TryProject(profile, p, out _, out JourneyTravelMode mode) && mode == JourneyTravelMode.Walk))
                {
                    for (int i = 0; i < points.Length; i++) nav.TryProject(profile, points[i], out points[i], out _);
                    return points;
                }
            throw new InvalidOperationException("No safely spaced dry-ground herd placement fits " + site.name);
        }

        private static int Nearest(Vector3[] points, Vector3 target)
        { return Enumerable.Range(0, points.Length).OrderBy(i => Flat(points[i], target)).First(); }
        private static float Flat(Vector3 a, Vector3 b)
        { a.y = b.y = 0; return Vector3.Distance(a, b); }
        private static Vector3 Ground(Terrain terrain, Vector3 position)
        { position.y = terrain.SampleHeight(position) + terrain.transform.position.y; return position; }
    }
}
