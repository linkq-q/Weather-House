using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.WildlifeJourney
{
    public enum JourneyTravelMode { Walk, Wade, Swim, Bridge }
    [Serializable] public sealed class JourneyProfile
    {
        public float bodyRadius = .3f, wadeDepth = .3f;
        public bool canSwim = true;
        public float maxSwimDistance = 8f, walkSpeed = 1f, swimSpeed = .7f, waterAversion = .5f;
        public float waterlineHeight = -1f;
    }
    public struct JourneySurface
    {
        public float groundY, waterY, slope;
        public bool hasWater, blocked, valid;
        public int bridgeId;
        public Collider supportingCollider;
    }
    [Serializable] public sealed class JourneyNode { public Vector3 position; public string id; }
    [Serializable] public sealed class JourneyLink { public int from, to, bridgeId; public bool bidirectional = true; }
    [Serializable] public sealed class JourneyWaterArea { public Bounds bounds; public float surfaceHeight; }
    [Serializable] public sealed class JourneyBridgeSurface { public int id; public Collider surface; }
    [Serializable] public struct JourneySegment
    {
        public Vector3 from, to;
        public JourneyTravelMode mode;
        public int bridgeId, linkIndex;
    }
    public sealed class JourneyRoute
    {
        public List<JourneySegment> segments = new List<JourneySegment>();
        public float totalCost;
        public int navigationVersion;
    }

    public sealed class JourneyNavigation : MonoBehaviour
    {
        public JourneyNode[] nodes = new JourneyNode[0];
        public JourneyLink[] links = new JourneyLink[0];
        public Terrain terrain;
        public WeatherHouse.WorldWaterSurface worldWaterSurface;
        public LayerMask groundMask = Physics.DefaultRaycastLayers, obstacleMask = Physics.DefaultRaycastLayers;
        public JourneyWaterArea[] waterAreas = new JourneyWaterArea[0];
        public JourneyBridgeSurface[] bridges = new JourneyBridgeSurface[0];
        public float sampleSpacing = .35f, maxSlope = 35f, maxStepHeight = .45f;
        public float endpointConnectionRadius = 8f, floatOffset = -.42f, surfaceProbeHeight = 100f;
        [NonSerialized] public Func<Vector3, JourneySurface> surfaceSampler;
        public int Version { get; private set; }
        private readonly RaycastHit[] groundHits = new RaycastHit[64];
        private readonly Collider[] overlaps = new Collider[32];
        private static readonly Vector3[] Footprint = {
            new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1),
            new Vector3(.7071f, 0, .7071f), new Vector3(-.7071f, 0, .7071f),
            new Vector3(.7071f, 0, -.7071f), new Vector3(-.7071f, 0, -.7071f) };
        private static readonly float[] CornerRadii = { .75f, 1.5f, 3f };

        public void Rebuild()
        {
            nodes = nodes ?? new JourneyNode[0]; links = links ?? new JourneyLink[0];
            waterAreas = waterAreas ?? new JourneyWaterArea[0]; bridges = bridges ?? new JourneyBridgeSurface[0];
            Version++;
        }

        public JourneySurface SampleSurface(Vector3 point)
        {
            if (!Finite(point)) return default;
            if (surfaceSampler != null) return surfaceSampler(point);
            var sample = new JourneySurface();
            if (terrain != null && terrain.terrainData != null)
            {
                Vector3 local = point - terrain.transform.position, size = terrain.terrainData.size;
                if (local.x >= 0 && local.z >= 0 && local.x <= size.x && local.z <= size.z)
                {
                    sample.groundY = terrain.SampleHeight(point) + terrain.transform.position.y; sample.valid = true;
                    sample.slope = terrain.terrainData.GetSteepness(local.x / size.x, local.z / size.z);
                }
            }
            float height = Mathf.Max(1f, surfaceProbeHeight);
            int count = Physics.RaycastNonAlloc(point + Vector3.up * height, Vector3.down, groundHits,
                height * 2f, groundMask, QueryTriggerInteraction.Ignore);
            if (count == groundHits.Length) { sample.blocked = true; return sample; }
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = groundHits[i]; int bridge = 0;
                foreach (JourneyBridgeSurface entry in bridges)
                    if (entry != null && entry.id > 0 && entry.surface == hit.collider) { bridge = entry.id; break; }
                if (terrain != null && !(hit.collider is TerrainCollider) && bridge == 0) continue;
                if (hit.distance >= nearest) continue;
                nearest = hit.distance; sample.groundY = hit.point.y; sample.bridgeId = bridge;
                sample.supportingCollider = hit.collider; sample.valid = true;
                sample.slope = Vector3.Angle(hit.normal, Vector3.up);
            }
            if (worldWaterSurface != null)
            {
                sample.hasWater = worldWaterSurface.Contains(point);
                if (sample.hasWater) sample.waterY = worldWaterSurface.Height(point);
            }
            else foreach (JourneyWaterArea water in waterAreas)
            {
                if (water == null || point.x < water.bounds.min.x || point.x > water.bounds.max.x ||
                    point.z < water.bounds.min.z || point.z > water.bounds.max.z) continue;
                if (!sample.hasWater || water.surfaceHeight > sample.waterY) sample.waterY = water.surfaceHeight;
                sample.hasWater = true;
            }
            return sample;
        }

        public bool TryProject(JourneyProfile profile, Vector3 requested, out Vector3 projected, out JourneyTravelMode mode)
        { return Project(profile, requested, out projected, out mode, out _); }

        // Placement may project onto a surface; movement must also reach it safely from the current feet position.
        public bool TryStep(JourneyProfile profile, Vector3 from, Vector3 requested, out Vector3 projected, out JourneyTravelMode mode)
        {
            projected = from; mode = JourneyTravelMode.Walk;
            if (!Finite(from) || !Finite(requested) || !Project(profile, from, out Vector3 previous, out JourneyTravelMode previousMode, out _) ||
                Mathf.Abs(previous.y - from.y) > Mathf.Max(.01f, maxStepHeight)) return false;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Horizontal(from, requested) / Spacing(profile)));
            if (steps > 4096) return false;
            for (int i = 1; i <= steps; i++)
            {
                if (!Project(profile, Vector3.Lerp(from, requested, (float)i / steps), out Vector3 point, out JourneyTravelMode nextMode, out _) ||
                    !SafeStep(previous, point, previousMode, nextMode)) return false;
                previous = point; previousMode = nextMode;
            }
            projected = previous; mode = previousMode;
            return true;
        }

        private bool Project(JourneyProfile profile, Vector3 requested, out Vector3 projected, out JourneyTravelMode mode, out JourneySurface surface)
        {
            projected = requested; mode = JourneyTravelMode.Walk; surface = SampleSurface(requested);
            if (profile == null || !surface.valid || surface.blocked || !Finite(surface.groundY) ||
                (surface.hasWater && !Finite(surface.waterY))) return false;
            float depth = surface.hasWater ? Mathf.Max(0, surface.waterY - surface.groundY) : 0;
            if (surface.bridgeId > 0)
            {
                if (depth > .01f) return false;
                mode = JourneyTravelMode.Bridge;
            }
            else if (depth > Mathf.Max(0, profile.wadeDepth))
            {
                if (!profile.canSwim || profile.maxSwimDistance <= 0 || profile.swimSpeed <= 0) return false;
                mode = JourneyTravelMode.Swim;
            }
            else if (depth > .01f) mode = JourneyTravelMode.Wade;
            if (mode != JourneyTravelMode.Swim && surface.slope > maxSlope) return false;
            if (mode == JourneyTravelMode.Bridge && !BridgeSupports(profile, requested, surface)) return false;
            float immersion = profile.waterlineHeight >= 0 ? -profile.waterlineHeight : floatOffset;
            projected.y = mode == JourneyTravelMode.Swim ? Mathf.Max(surface.groundY, surface.waterY + immersion) : surface.groundY;
            if (surfaceSampler != null) return true;
            float radius = Mathf.Max(.05f, profile.bodyRadius);
            int count = Physics.OverlapSphereNonAlloc(projected + Vector3.up * (radius + .04f), radius * .85f,
                overlaps, obstacleMask, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return false;
            for (int i = 0; i < count; i++)
                if (overlaps[i] != surface.supportingCollider && !(overlaps[i] is TerrainCollider)) return false;
            return true;
        }

        private bool BridgeSupports(JourneyProfile profile, Vector3 point, JourneySurface center)
        {
            foreach (Vector3 direction in Footprint)
            {
                JourneySurface support = SampleSurface(point + direction * Mathf.Max(.05f, profile.bodyRadius));
                if (!support.valid || support.blocked || !Finite(support.groundY) || support.slope > maxSlope ||
                    Mathf.Abs(support.groundY - center.groundY) > Mathf.Max(.01f, maxStepHeight)) return false;
                if (support.hasWater && support.waterY > support.groundY + .01f) return false;
            }
            return true;
        }

        private float Spacing(JourneyProfile profile) => Mathf.Clamp(Mathf.Min(sampleSpacing, Mathf.Max(.05f, profile.bodyRadius)), .04f, 1f);
        private bool SafeStep(Vector3 from, Vector3 to, JourneyTravelMode fromMode, JourneyTravelMode toMode)
        {
            float horizontal = Horizontal(from, to), rise = Mathf.Abs(to.y - from.y);
            if (rise > Mathf.Max(.01f, maxStepHeight)) return false;
            return fromMode == JourneyTravelMode.Swim || toMode == JourneyTravelMode.Swim || horizontal < .0001f ||
                rise / horizontal <= Mathf.Tan(Mathf.Clamp(maxSlope, 0, 89) * Mathf.Deg2Rad);
        }

        private sealed class Edge
        {
            public List<JourneySegment> segments = new List<JourneySegment>();
            public float cost, swimDistance;
            public Vector3? pivot;
        }
        private sealed class SearchState
        {
            public int node;
            public float cost, swimDistance;
            public bool closed, discarded;
            public SearchState parent;
            public Edge edge;
        }

        // Validate this exact surface-sampled line without requiring or searching the connectivity graph.
        public JourneyRoute TraceDirectRoute(Vector3 start, Vector3 goal, JourneyProfile profile, float initialSwimDistance = 0f)
        {
            if (!Finite(start) || !Finite(goal) || !Finite(initialSwimDistance) || initialSwimDistance < 0 ||
                profile == null || profile.walkSpeed <= 0) return null;
            Edge edge = Trace(start, goal, profile, -1, 0, initialSwimDistance);
            return edge == null ? null : new JourneyRoute { segments = edge.segments, totalCost = edge.cost, navigationVersion = Version };
        }

        // Graph links describe candidate connectivity. Their actual geometry is checked on every search.
        public JourneyRoute FindRoute(Vector3 start, Vector3 goal, JourneyProfile profile, ISet<int> blockedLinks = null, float initialSwimDistance = 0f)
        {
            if (!Finite(start) || !Finite(goal) || !Finite(initialSwimDistance) || initialSwimDistance < 0 ||
                profile == null || profile.walkSpeed <= 0 || nodes == null || links == null) return null;
            List<SearchState> states = Attachments(start, profile, initialSwimDistance), exits = Attachments(goal, profile, 0);
            if (states.Count == 0 || exits.Count == 0) return null;
            JourneyRoute bestRoute = null;
            float bestCost = float.PositiveInfinity;
            float fastest = Mathf.Max(.01f, Mathf.Max(profile.walkSpeed, profile.canSwim ? profile.swimSpeed : 0));
            while (true)
            {
                SearchState current = null; float best = float.PositiveInfinity;
                foreach (SearchState candidate in states)
                {
                    if (candidate.closed || candidate.discarded) continue;
                    float score = candidate.cost + Horizontal(nodes[candidate.node].position, goal) / fastest;
                    if (score < best) { current = candidate; best = score; }
                }
                if (current == null || best >= bestCost) return bestRoute;
                current.closed = true;
                foreach (SearchState attachment in exits)
                {
                    if (attachment.node != current.node) continue;
                    Edge exit = Connector(nodes[current.node].position, goal, profile, current.swimDistance, attachment.edge.pivot);
                    if (exit != null && current.cost + exit.cost < bestCost)
                    { bestCost = current.cost + exit.cost; bestRoute = Assemble(current, exit); }
                }
                for (int i = 0; i < links.Length; i++)
                {
                    JourneyLink link = links[i];
                    if (link == null || (blockedLinks != null && blockedLinks.Contains(i))) continue;
                    int next = link.from == current.node ? link.to : link.bidirectional && link.to == current.node ? link.from : -1;
                    if (next < 0 || next >= nodes.Length || nodes[next] == null) continue;
                    Edge edge = Trace(nodes[current.node].position, nodes[next].position, profile, i, link.bridgeId, current.swimDistance);
                    if (edge == null) continue;
                    float cost = current.cost + edge.cost; bool dominated = false;
                    // A cheaper path with more swimming fatigue must not erase a viable rested arrival.
                    foreach (SearchState seen in states)
                        if (!seen.discarded && seen.node == next && seen.cost <= cost + .0001f && seen.swimDistance <= edge.swimDistance + .0001f)
                        { dominated = true; break; }
                    if (dominated) continue;
                    foreach (SearchState seen in states)
                        if (seen.node == next && cost <= seen.cost && edge.swimDistance <= seen.swimDistance) seen.discarded = true;
                    states.Add(new SearchState { node = next, cost = cost, swimDistance = edge.swimDistance, parent = current, edge = edge });
                }
            }
        }

        private List<SearchState> Attachments(Vector3 position, JourneyProfile profile, float initialSwimDistance)
        {
            var result = new List<SearchState>();
            int exact = -1;
            for (int i = 0; i < nodes.Length; i++)
                if (nodes[i] != null && Horizontal(position, nodes[i].position) < .001f) { exact = i; break; }
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] == null || (exact >= 0 && i != exact) ||
                    Horizontal(position, nodes[i].position) > Mathf.Max(0, endpointConnectionRadius)) continue;
                Edge candidate = Trace(position, nodes[i].position, profile, -1, 0, initialSwimDistance);
                if (candidate == null) continue;
                result.Add(new SearchState { node = i, cost = candidate.cost, swimDistance = candidate.swimDistance, edge = candidate });
            }
            if (result.Count > 0 || exact >= 0) return result;
            // A newly blocked straight attachment may still have a safe local route around its corner.
            // Only a disconnected endpoint needs these bounded probes; explicit graph-node identity is retained.
            foreach (float radius in CornerRadii)
                foreach (Vector3 direction in Footprint)
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        if (nodes[i] == null || Horizontal(position, nodes[i].position) > Mathf.Max(0, endpointConnectionRadius)) continue;
                        Edge edge = Connector(position, nodes[i].position, profile, initialSwimDistance, position + direction * radius);
                        if (edge == null) continue;
                        bool dominated = result.Exists(s => s.node == i && s.cost <= edge.cost && s.swimDistance <= edge.swimDistance);
                        if (dominated) continue;
                        result.RemoveAll(s => s.node == i && edge.cost <= s.cost && edge.swimDistance <= s.swimDistance);
                        result.Add(new SearchState { node = i, cost = edge.cost, swimDistance = edge.swimDistance, edge = edge });
                    }
            return result;
        }

        private Edge Connector(Vector3 from, Vector3 to, JourneyProfile profile, float priorSwim, Vector3? pivot)
        {
            if (!pivot.HasValue) return Trace(from, to, profile, -1, 0, priorSwim);
            // Distinct virtual legs stop following lookahead and route variation from cutting across this corner.
            Edge first = Trace(from, pivot.Value, profile, -2, 0, priorSwim);
            if (first == null) return null;
            Edge second = Trace(pivot.Value, to, profile, -1, 0, first.swimDistance);
            if (second == null) return null;
            first.segments.AddRange(second.segments); first.cost += second.cost;
            first.swimDistance = second.swimDistance; first.pivot = pivot;
            return first;
        }

        private Edge Trace(Vector3 from, Vector3 to, JourneyProfile profile, int linkIndex, int bridgeId, float priorSwim)
        {
            if (!Project(profile, from, out Vector3 previous, out JourneyTravelMode previousMode, out JourneySurface previousSurface)) return null;
            float distance = Horizontal(from, to);
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / Spacing(profile)));
            if (steps > 4096) return null;
            var edge = new Edge { swimDistance = previousMode == JourneyTravelMode.Swim ? priorSwim : 0 };
            bool foundBridge = previousSurface.bridgeId == bridgeId && bridgeId > 0;
            for (int i = 1; i <= steps; i++)
            {
                if (!Project(profile, Vector3.Lerp(from, to, (float)i / steps), out Vector3 point, out JourneyTravelMode mode, out JourneySurface surface)) return null;
                float horizontal = Horizontal(previous, point);
                bool swimming = mode == JourneyTravelMode.Swim || previousMode == JourneyTravelMode.Swim;
                if (!SafeStep(previous, point, previousMode, mode)) return null;
                if (bridgeId > 0)
                {
                    if (surface.bridgeId > 0 && surface.bridgeId != bridgeId) return null;
                    if (surface.hasWater && surface.waterY > surface.groundY + .01f && surface.bridgeId != bridgeId) return null;
                    foundBridge |= surface.bridgeId == bridgeId;
                }
                if (swimming)
                {
                    edge.swimDistance += horizontal;
                    if (edge.swimDistance > profile.maxSwimDistance + .0001f) return null;
                }
                if (mode != JourneyTravelMode.Swim) edge.swimDistance = 0;
                JourneyTravelMode travel = swimming ? JourneyTravelMode.Swim :
                    mode == JourneyTravelMode.Bridge || previousMode == JourneyTravelMode.Bridge ? JourneyTravelMode.Bridge : mode;
                if ((point - previous).sqrMagnitude > .000001f)
                {
                    float speed = travel == JourneyTravelMode.Swim ? profile.swimSpeed : profile.walkSpeed;
                    float aversion = Mathf.Max(0, profile.waterAversion);
                    float penalty = travel == JourneyTravelMode.Swim ? 1 + aversion : travel == JourneyTravelMode.Wade ? 1 + aversion * .5f : 1;
                    edge.cost += Vector3.Distance(previous, point) / Mathf.Max(.01f, speed) * penalty;
                    edge.segments.Add(new JourneySegment { from = previous, to = point, mode = travel,
                        bridgeId = surface.bridgeId > 0 ? surface.bridgeId : previousSurface.bridgeId, linkIndex = linkIndex });
                }
                previous = point; previousMode = mode; previousSurface = surface;
            }
            return bridgeId > 0 && !foundBridge ? null : edge;
        }

        private JourneyRoute Assemble(SearchState last, Edge exit)
        {
            var path = new Stack<Edge>();
            for (SearchState cursor = last; cursor != null; cursor = cursor.parent) path.Push(cursor.edge);
            var route = new JourneyRoute { totalCost = last.cost + exit.cost, navigationVersion = Version };
            while (path.Count > 0) route.segments.AddRange(path.Pop().segments);
            route.segments.AddRange(exit.segments);
            return route;
        }
        private static float Horizontal(Vector3 a, Vector3 b) { return new Vector2(a.x - b.x, a.z - b.z).magnitude; }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
    }
}
