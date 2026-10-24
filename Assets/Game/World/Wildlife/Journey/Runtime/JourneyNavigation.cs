using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WeatherHouse.Hillside;

namespace WeatherHouse.WildlifeJourney
{
    public enum JourneyTravelMode { Walk, Wade, Swim, Bridge }
    [Serializable] public sealed class JourneyProfile
    {
        public float bodyRadius = .3f, wadeDepth = .3f;
        public bool canSwim = true;
        public float maxSwimDistance = 8f, walkSpeed = 1f, swimSpeed = .7f, waterAversion = .5f;
        public float waterlineHeight = -1f;
        [NonSerialized] public WildlifeBodyEnvelope bodyEnvelope;
        [NonSerialized] public Quaternion bodyHeading = Quaternion.identity;
        [NonSerialized] public Transform bodyOwner;
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
        public bool surfaceBoundary;
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
        [NonSerialized] public WeatherHouse.Hillside.WildlifePlayerCollision playerCollision;
        public int Version { get; private set; }
        private readonly RaycastHit[] groundHits = new RaycastHit[64];
        private readonly Collider[] overlaps = new Collider[32];
        private readonly WildlifeBodyPhysics bodyPhysics = new WildlifeBodyPhysics();
        private readonly Dictionary<Collider, int> bridgeIds = new Dictionary<Collider, int>();
        private JourneyBridgeSurface[] cachedBridges;
        private const int MaximumSamples = 4096;
        private readonly List<float> sampleTimes = new List<float>();
        private readonly List<float> terrainCuts = new List<float>();
        private static readonly Vector3[] Footprint = {
            new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1),
            new Vector3(.7071f, 0, .7071f), new Vector3(-.7071f, 0, .7071f),
            new Vector3(.7071f, 0, -.7071f), new Vector3(-.7071f, 0, -.7071f) };
        private static readonly float[] CornerRadii = { .75f, 1.5f, 3f };

        public void Rebuild()
        {
            nodes = nodes ?? new JourneyNode[0]; links = links ?? new JourneyLink[0];
            waterAreas = waterAreas ?? new JourneyWaterArea[0]; bridges = bridges ?? new JourneyBridgeSurface[0];
            cachedBridges = null; // Rebuild also invalidates edits within the existing bridge array.
            Version++;
        }

        private void EnsureBridgeLookup()
        {
            if (ReferenceEquals(cachedBridges, bridges)) return;
            bridgeIds.Clear(); cachedBridges = bridges;
            if (bridges == null) return;
            foreach (JourneyBridgeSurface entry in bridges)
                if (entry != null && entry.id > 0 && entry.surface && !bridgeIds.ContainsKey(entry.surface))
                    bridgeIds.Add(entry.surface, entry.id);
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
            if (count > 0) EnsureBridgeLookup();
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = groundHits[i];
                Collider collider = hit.collider;
                if (!collider || (playerCollision && playerCollision.Owns(collider))) continue;
                bridgeIds.TryGetValue(collider, out int bridge);
                if (terrain != null && !(collider is TerrainCollider) && bridge == 0) continue;
                if (hit.distance >= nearest) continue;
                nearest = hit.distance; sample.groundY = hit.point.y; sample.bridgeId = bridge;
                sample.supportingCollider = collider; sample.valid = true;
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
        public bool TryStep(JourneyProfile profile, Vector3 from, Vector3 requested, out Vector3 projected, out JourneyTravelMode mode,
            JourneyTravelMode? requiredMode = null, int requiredBridgeId = 0)
        {
            projected = from; mode = JourneyTravelMode.Walk;
            Quaternion heading = WildlifeBodyEnvelope.Heading(requested - from, profile == null ? Quaternion.identity : profile.bodyHeading);
            if (!Finite(from) || !Finite(requested) || !Project(profile, from, out Vector3 previous, out JourneyTravelMode previousMode, out JourneySurface previousSurface, heading) ||
                Mathf.Abs(previous.y - from.y) > Mathf.Max(.01f, maxStepHeight) ||
                (requiredMode.HasValue && (previousMode != requiredMode.Value || (requiredBridgeId != 0 && previousSurface.bridgeId != requiredBridgeId)))) return false;
            if (!BuildSamples(from, requested, profile)) return false;
            foreach (float t in sampleTimes)
            {
                if (!Project(profile, Vector3.Lerp(from, requested, t), out Vector3 point, out JourneyTravelMode nextMode, out JourneySurface surface, heading) ||
                    (requiredMode.HasValue && (nextMode != requiredMode.Value || (requiredBridgeId != 0 && surface.bridgeId != requiredBridgeId))) ||
                    !SafeStep(previous, point, previousMode, nextMode) ||
                    !BodyClear(profile, previous, heading, point, heading, previousSurface, surface)) return false;
                previous = point; previousMode = nextMode; previousSurface = surface;
            }
            projected = previous; mode = previousMode;
            return true;
        }

        private bool Project(JourneyProfile profile, Vector3 requested, out Vector3 projected, out JourneyTravelMode mode, out JourneySurface surface, Quaternion? heading = null)
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
            if (profile.bodyEnvelope.Valid)
                return BodyClear(profile, projected, heading ?? profile.bodyHeading, projected, heading ?? profile.bodyHeading, surface, surface);
            float radius = Mathf.Max(.05f, profile.bodyRadius);
            int count = Physics.OverlapSphereNonAlloc(projected + Vector3.up * (radius + .04f), radius * .85f,
                overlaps, obstacleMask, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return false;
            for (int i = 0; i < count; i++)
                if (overlaps[i] != surface.supportingCollider && !(overlaps[i] is TerrainCollider) &&
                    !(playerCollision && playerCollision.Owns(overlaps[i]))) return false;
            return true;
        }

        public bool CanMoveBody(JourneyProfile profile, Vector3 from, Quaternion fromRotation, Vector3 to, Quaternion toRotation)
        {
            return BodyClear(profile, from, fromRotation, to, toRotation, SampleSurface(from), SampleSurface(to));
        }

        private bool BodyClear(JourneyProfile profile, Vector3 from, Quaternion fromRotation, Vector3 to, Quaternion toRotation,
            JourneySurface start, JourneySurface finish)
        {
            return surfaceSampler != null || !profile.bodyEnvelope.Valid || bodyPhysics.Clear(profile.bodyEnvelope,
                from, fromRotation, to, toRotation, obstacleMask, start.supportingCollider, finish.supportingCollider,
                profile.bodyOwner, playerCollision);
        }

        public bool BodyRouteClear(JourneyProfile profile, JourneyRoute route)
        {
            if (route == null) return false;
            Quaternion previous = profile.bodyHeading;
            foreach (JourneySegment segment in route.segments)
            {
                Quaternion next = WildlifeBodyEnvelope.Heading(segment.to - segment.from, previous);
                if (!CanMoveBody(profile, segment.from, previous, segment.from, next) ||
                    !CanMoveBody(profile, segment.from, next, segment.to, next)) return false;
                previous = next;
            }
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
        private bool BuildSamples(Vector3 from, Vector3 to, JourneyProfile profile)
        {
            sampleTimes.Clear();
            int steps = Mathf.Max(1, Mathf.CeilToInt(Horizontal(from, to) / Spacing(profile)));
            if (steps > MaximumSamples) return false;
            for (int i = 1; i <= steps; i++) sampleTimes.Add((float)i / steps);
            if (surfaceSampler != null || !terrain || !terrain.terrainData) return true;
            TerrainData data = terrain.terrainData;
            int cells = data.heightmapResolution - 1;
            Vector3 size = data.size, origin = terrain.transform.position;
            if (cells < 1 || size.x <= 0 || size.z <= 0) return true;
            Vector2 a = new Vector2((from.x - origin.x) * cells / size.x, (from.z - origin.z) * cells / size.z);
            Vector2 b = new Vector2((to.x - origin.x) * cells / size.x, (to.z - origin.z) * cells / size.z);
            if (!Finite(a.x) || !Finite(a.y) || !Finite(b.x) || !Finite(b.y)) return false;
            terrainCuts.Clear(); terrainCuts.Add(0); terrainCuts.Add(1);
            // Grid edges and both possible diagonals partition the line into individual heightfield triangles.
            if (!AddTerrainCuts(a.x, b.x, 0, cells, a, b, cells) || !AddTerrainCuts(a.y, b.y, 0, cells, a, b, cells) ||
                !AddTerrainCuts(a.x + a.y, b.x + b.y, 0, cells * 2, a, b, cells) ||
                !AddTerrainCuts(a.x - a.y, b.x - b.y, -cells, cells, a, b, cells)) return false;
            SortUnique(terrainCuts);
            for (int i = 1; i < terrainCuts.Count; i++)
            {
                sampleTimes.Add(terrainCuts[i]);
                sampleTimes.Add((terrainCuts[i - 1] + terrainCuts[i]) * .5f);
            }
            SortUnique(sampleTimes);
            return sampleTimes.Count <= MaximumSamples;
        }

        private bool AddTerrainCuts(float from, float to, int minimum, int maximum, Vector2 a, Vector2 b, int cells)
        {
            if (from == to || Mathf.Max(from, to) < minimum || Mathf.Min(from, to) > maximum) return true;
            int first = Mathf.CeilToInt(Mathf.Max(minimum, Mathf.Min(from, to)));
            int last = Mathf.FloorToInt(Mathf.Min(maximum, Mathf.Max(from, to)));
            for (int line = first; line <= last; line++)
            {
                float t = (line - from) / (to - from);
                if (t <= 0 || t >= 1) continue;
                float x = a.x + (b.x - a.x) * t, z = a.y + (b.y - a.y) * t;
                if (x < 0 || z < 0 || x > cells || z > cells) continue;
                terrainCuts.Add(t);
                if (terrainCuts.Count > MaximumSamples) return false;
            }
            return true;
        }

        private static void SortUnique(List<float> values)
        {
            values.Sort();
            int count = 0;
            for (int i = 0; i < values.Count; i++)
                if (count == 0 || values[i] != values[count - 1]) values[count++] = values[i];
            if (count < values.Count) values.RemoveRange(count, values.Count - count);
        }

        private bool SafeStep(Vector3 from, Vector3 to, JourneyTravelMode fromMode, JourneyTravelMode toMode)
        {
            float horizontal = Horizontal(from, to), rise = Mathf.Abs(to.y - from.y);
            if (rise > Mathf.Max(.01f, maxStepHeight)) return false;
            return fromMode == JourneyTravelMode.Swim || toMode == JourneyTravelMode.Swim || horizontal < .0001f ||
                rise / horizontal <= Mathf.Tan(Mathf.Clamp(maxSlope, 0, 89) * Mathf.Deg2Rad);
        }

        internal sealed class Edge
        {
            public List<JourneySegment> segments = new List<JourneySegment>();
            public float cost, swimDistance;
            public Vector3? pivot;
            public int arrivalLink = -1;
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
            using (RouteSearch search = CreateSearch(start, goal, profile, blockedLinks, initialSwimDistance))
            {
                search.Advance(double.PositiveInfinity);
                return search.Result;
            }
        }

        public enum SearchStatus { Pending, Found, NoRoute, Invalidated }

        public RouteSearch CreateSearch(Vector3 start, Vector3 goal, JourneyProfile profile, ISet<int> blockedLinks = null, float initialSwimDistance = 0f)
        { return new RouteSearch(this, start, goal, profile, blockedLinks, initialSwimDistance); }

        public sealed class RouteSearch : IDisposable
        {
            public SearchStatus Status { get; private set; } = SearchStatus.Pending;
            public JourneyRoute Result { get; private set; }
            public int WorkUnits { get; private set; }
            public double LastAdvanceMilliseconds { get; private set; }
            public double MaximumWorkUnitMilliseconds { get; private set; }
            internal int slice;
            internal bool sliced;
            internal readonly Dictionary<(int node, int link, float swim), Edge> tracedLinks = new Dictionary<(int, int, float), Edge>();
            private readonly JourneyNavigation navigation;
            private readonly JourneyProfile source, profile;
            private readonly ISet<int> sourceBlocked;
            private readonly HashSet<int> blocked;
            private readonly int version;
            private readonly JourneyNode[] nodes;
            private readonly JourneyLink[] links;
            private readonly JourneyBridgeSurface[] bridges;
            private readonly JourneyWaterArea[] waterAreas;
            private readonly Terrain terrain;
            private readonly WeatherHouse.WorldWaterSurface water;
            private readonly Func<Vector3, JourneySurface> sampler;
            private readonly Vector3 ownerPosition, ownerScale;
            private readonly Quaternion ownerRotation;
            private readonly bool hadOwner;
            private readonly float spacing, slope, step, endpoint, immersion, probe;
            private readonly int groundMask, obstacleMask;
            private IEnumerator steps;

            internal RouteSearch(JourneyNavigation navigation, Vector3 start, Vector3 goal, JourneyProfile source, ISet<int> blockedLinks, float initialSwimDistance)
            {
                this.navigation = navigation; this.source = source; sourceBlocked = blockedLinks;
                profile = CopyProfile(source); blocked = blockedLinks == null ? null : new HashSet<int>(blockedLinks);
                version = navigation.Version; nodes = navigation.nodes; links = navigation.links;
                bridges = navigation.bridges; waterAreas = navigation.waterAreas; terrain = navigation.terrain;
                water = navigation.worldWaterSurface; sampler = navigation.surfaceSampler;
                spacing = navigation.sampleSpacing; slope = navigation.maxSlope; step = navigation.maxStepHeight;
                endpoint = navigation.endpointConnectionRadius; immersion = navigation.floatOffset; probe = navigation.surfaceProbeHeight;
                groundMask = navigation.groundMask; obstacleMask = navigation.obstacleMask;
                hadOwner = source != null && source.bodyOwner;
                if (hadOwner) { ownerPosition = source.bodyOwner.position; ownerRotation = source.bodyOwner.rotation; ownerScale = source.bodyOwner.lossyScale; }
                steps = navigation.SearchSteps(start, goal, profile, blocked, initialSwimDistance, this);
            }

            public void Advance(double budgetMilliseconds)
            {
                if (Status != SearchStatus.Pending || budgetMilliseconds <= 0 || double.IsNaN(budgetMilliseconds)) return;
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    if (!Unchanged()) { Cancel(); return; }
                    // Physics results are reusable only inside this uninterrupted main-thread slice.
                    slice++; tracedLinks.Clear();
                    do
                    {
                        long unitStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                        bool more = steps.MoveNext();
                        MaximumWorkUnitMilliseconds = Math.Max(MaximumWorkUnitMilliseconds, Elapsed(unitStarted));
                        if (!more) { if (Status == SearchStatus.Pending) Complete(null); return; }
                        WorkUnits++;
                    }
                    while (Status == SearchStatus.Pending && Elapsed(started) < budgetMilliseconds);
                    sliced = Status == SearchStatus.Pending;
                }
                finally { LastAdvanceMilliseconds = Elapsed(started); }
            }

            private bool Unchanged()
            {
                if (!navigation || navigation.Version != version || !SameProfile(source, profile) ||
                    navigation.nodes != nodes || navigation.links != links || navigation.bridges != bridges || navigation.waterAreas != waterAreas ||
                    navigation.terrain != terrain || navigation.worldWaterSurface != water || navigation.surfaceSampler != sampler ||
                    navigation.sampleSpacing != spacing || navigation.maxSlope != slope || navigation.maxStepHeight != step ||
                    navigation.endpointConnectionRadius != endpoint || navigation.floatOffset != immersion || navigation.surfaceProbeHeight != probe ||
                    navigation.groundMask.value != groundMask || navigation.obstacleMask.value != obstacleMask ||
                    (sourceBlocked != null && !blocked.SetEquals(sourceBlocked))) return false;
                return !hadOwner || (source.bodyOwner && source.bodyOwner.position.Equals(ownerPosition) &&
                    source.bodyOwner.rotation.Equals(ownerRotation) && source.bodyOwner.lossyScale.Equals(ownerScale));
            }

            internal void Complete(JourneyRoute route) { Result = route; Status = route == null ? SearchStatus.NoRoute : SearchStatus.Found; }
            public void Cancel() { Result = null; Status = SearchStatus.Invalidated; DisposeSteps(); }
            public void Dispose() { if (Status == SearchStatus.Pending) Cancel(); else DisposeSteps(); }
            private void DisposeSteps() { (steps as IDisposable)?.Dispose(); steps = null; tracedLinks.Clear(); }
            private static double Elapsed(long started) => (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        internal static JourneyProfile CopyProfile(JourneyProfile value)
        {
            return value == null ? null : new JourneyProfile { bodyRadius = value.bodyRadius, wadeDepth = value.wadeDepth,
                canSwim = value.canSwim, maxSwimDistance = value.maxSwimDistance, walkSpeed = value.walkSpeed,
                swimSpeed = value.swimSpeed, waterAversion = value.waterAversion, waterlineHeight = value.waterlineHeight,
                bodyEnvelope = value.bodyEnvelope, bodyHeading = value.bodyHeading, bodyOwner = value.bodyOwner };
        }
        internal static bool SameProfile(JourneyProfile a, JourneyProfile b)
        {
            return a == null || b == null ? a == b : a.bodyRadius == b.bodyRadius && a.wadeDepth == b.wadeDepth &&
                a.canSwim == b.canSwim && a.maxSwimDistance == b.maxSwimDistance && a.walkSpeed == b.walkSpeed &&
                a.swimSpeed == b.swimSpeed && a.waterAversion == b.waterAversion && a.waterlineHeight == b.waterlineHeight &&
                a.bodyEnvelope.Equals(b.bodyEnvelope) && a.bodyHeading.Equals(b.bodyHeading) && a.bodyOwner == b.bodyOwner;
        }

        private IEnumerator SearchSteps(Vector3 start, Vector3 goal, JourneyProfile profile, ISet<int> blockedLinks, float initialSwimDistance, RouteSearch search)
        {
            if (!Finite(start) || !Finite(goal) || !Finite(initialSwimDistance) || initialSwimDistance < 0 ||
                profile == null || profile.walkSpeed <= 0 || nodes == null || links == null) yield break;
            var states = new List<SearchState>(); var exits = new List<SearchState>();
            IEnumerator operation = AttachmentSteps(start, profile, initialSwimDistance, states);
            while (operation.MoveNext()) yield return null;
            operation = AttachmentSteps(goal, profile, 0, exits, true);
            while (operation.MoveNext()) yield return null;
            if (states.Count == 0 || exits.Count == 0) yield break;
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
                if (current == null || best >= bestCost)
                {
                    if (bestRoute != null && search.sliced)
                    {
                        bool valid = false;
                        operation = ValidateRouteSteps(profile, bestRoute, initialSwimDistance, value => valid = value);
                        while (operation.MoveNext()) yield return null;
                        if (!valid) bestRoute = null;
                    }
                    search.Complete(bestRoute); yield break;
                }
                current.closed = true;
                foreach (SearchState attachment in exits)
                {
                    if (attachment.node != current.node) continue;
                    Edge exit = null;
                    operation = ConnectorSteps(nodes[current.node].position, goal, profile, current.swimDistance, attachment.edge.pivot, value => exit = value);
                    while (operation.MoveNext()) yield return null;
                    if (exit != null && JoinClear(profile, current, exit) && current.cost + exit.cost < bestCost)
                    { bestCost = current.cost + exit.cost; bestRoute = Assemble(current, exit); }
                    yield return null;
                }
                for (int i = 0; i < links.Length; i++)
                {
                    JourneyLink link = links[i];
                    if (link == null || (blockedLinks != null && blockedLinks.Contains(i))) continue;
                    int next = link.from == current.node ? link.to : link.bidirectional && link.to == current.node ? link.from : -1;
                    if (next < 0 || next >= nodes.Length || nodes[next] == null) continue;
                    var key = (current.node, i, current.swimDistance);
                    if (!search.tracedLinks.TryGetValue(key, out Edge edge))
                    {
                        int slice = search.slice;
                        operation = TraceSteps(nodes[current.node].position, nodes[next].position, profile, i, link.bridgeId, current.swimDistance, value => edge = value);
                        while (operation.MoveNext()) yield return null;
                        if (slice == search.slice) search.tracedLinks[key] = edge;
                    }
                    bool joined = edge != null && JoinClear(profile, current, edge);
                    yield return null;
                    if (!joined) continue;
                    float cost = current.cost + edge.cost; bool dominated = false;
                    // A cheaper path with more swimming fatigue must not erase a viable rested arrival.
                    foreach (SearchState seen in states)
                        if (!seen.discarded && seen.node == next && SameApproach(seen.edge, edge) &&
                            seen.cost <= cost + .0001f && seen.swimDistance <= edge.swimDistance + .0001f)
                        { dominated = true; break; }
                    if (dominated) continue;
                    foreach (SearchState seen in states)
                        if (seen.node == next && SameApproach(seen.edge, edge) && cost <= seen.cost && edge.swimDistance <= seen.swimDistance) seen.discarded = true;
                    states.Add(new SearchState { node = next, cost = cost, swimDistance = edge.swimDistance, parent = current, edge = edge });
                    yield return null;
                }
            }
        }

        private IEnumerator AttachmentSteps(Vector3 position, JourneyProfile profile, float initialSwimDistance, List<SearchState> result, bool exit = false)
        {
            int exact = -1;
            for (int i = 0; i < nodes.Length; i++)
                if (nodes[i] != null && Horizontal(position, nodes[i].position) < .001f) { exact = i; break; }
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] == null || (exact >= 0 && i != exact) ||
                    Horizontal(position, nodes[i].position) > Mathf.Max(0, endpointConnectionRadius)) continue;
                Edge candidate = null;
                IEnumerator operation = TraceSteps(exit ? nodes[i].position : position, exit ? position : nodes[i].position,
                    profile, -1, 0, initialSwimDistance, value => candidate = value);
                while (operation.MoveNext()) yield return null;
                if (candidate == null || (!exit && !StartTurnClear(profile, position, candidate))) continue;
                result.Add(new SearchState { node = i, cost = candidate.cost, swimDistance = candidate.swimDistance, edge = candidate });
                yield return null;
            }
            if (result.Count > 0 || exact >= 0) yield break;
            // A newly blocked straight attachment may still have a safe local route around its corner.
            // Only a disconnected endpoint needs these bounded probes; explicit graph-node identity is retained.
            foreach (float radius in CornerRadii)
                foreach (Vector3 direction in Footprint)
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        if (nodes[i] == null || Horizontal(position, nodes[i].position) > Mathf.Max(0, endpointConnectionRadius)) continue;
                        Edge edge = null;
                        IEnumerator operation = ConnectorSteps(exit ? nodes[i].position : position, exit ? position : nodes[i].position,
                            profile, initialSwimDistance, position + direction * radius, value => edge = value);
                        while (operation.MoveNext()) yield return null;
                        if (edge == null || (!exit && !StartTurnClear(profile, position, edge))) continue;
                        bool dominated = result.Exists(s => s.node == i && SameApproach(s.edge, edge) && s.cost <= edge.cost && s.swimDistance <= edge.swimDistance);
                        if (dominated) continue;
                        result.RemoveAll(s => s.node == i && SameApproach(s.edge, edge) && edge.cost <= s.cost && edge.swimDistance <= s.swimDistance);
                        result.Add(new SearchState { node = i, cost = edge.cost, swimDistance = edge.swimDistance, edge = edge });
                        yield return null;
                    }
        }

        private IEnumerator ConnectorSteps(Vector3 from, Vector3 to, JourneyProfile profile, float priorSwim, Vector3? pivot, Action<Edge> complete)
        {
            if (!pivot.HasValue)
            {
                IEnumerator straight = TraceSteps(from, to, profile, -1, 0, priorSwim, complete);
                while (straight.MoveNext()) yield return null;
                yield break;
            }
            // Distinct virtual legs stop following lookahead and route variation from cutting across this corner.
            Edge first = null, second = null;
            IEnumerator operation = TraceSteps(from, pivot.Value, profile, -2, 0, priorSwim, value => first = value);
            while (operation.MoveNext()) yield return null;
            if (first == null) yield break;
            operation = TraceSteps(pivot.Value, to, profile, -1, 0, first.swimDistance, value => second = value);
            while (operation.MoveNext()) yield return null;
            if (second == null || !TurnClear(profile, first, second, profile.bodyHeading)) yield break;
            first.segments.AddRange(second.segments); first.cost += second.cost;
            first.swimDistance = second.swimDistance; first.pivot = pivot;
            complete(first);
        }

        private Edge Trace(Vector3 from, Vector3 to, JourneyProfile profile, int linkIndex, int bridgeId, float priorSwim)
        {
            Edge result = null;
            IEnumerator steps = TraceSteps(from, to, profile, linkIndex, bridgeId, priorSwim, value => result = value);
            while (steps.MoveNext()) { }
            return result;
        }

        private IEnumerator TraceSteps(Vector3 from, Vector3 to, JourneyProfile profile, int linkIndex, int bridgeId, float priorSwim, Action<Edge> complete)
        {
            yield return null;
            Quaternion heading = WildlifeBodyEnvelope.Heading(to - from, profile.bodyHeading);
            if (!Project(profile, from, out Vector3 previous, out JourneyTravelMode previousMode, out JourneySurface previousSurface, heading)) yield break;
            if (!BuildSamples(from, to, profile)) yield break;
            // Movement queries can overwrite the navigation's scratch lists between these steps.
            float[] times = sampleTimes.ToArray();
            var edge = new Edge { swimDistance = previousMode == JourneyTravelMode.Swim ? priorSwim : 0, arrivalLink = linkIndex };
            bool foundBridge = previousSurface.bridgeId == bridgeId && bridgeId > 0;
            yield return null;
            foreach (float t in times)
            {
                if (!Project(profile, Vector3.Lerp(from, to, t), out Vector3 point, out JourneyTravelMode mode, out JourneySurface surface, heading)) yield break;
                float horizontal = Horizontal(previous, point);
                bool swimming = mode == JourneyTravelMode.Swim || previousMode == JourneyTravelMode.Swim;
                if (!SafeStep(previous, point, previousMode, mode) ||
                    !BodyClear(profile, previous, heading, point, heading, previousSurface, surface)) yield break;
                if (bridgeId > 0)
                {
                    if (surface.bridgeId > 0 && surface.bridgeId != bridgeId) yield break;
                    if (surface.hasWater && surface.waterY > surface.groundY + .01f && surface.bridgeId != bridgeId) yield break;
                    foundBridge |= surface.bridgeId == bridgeId;
                }
                if (swimming)
                {
                    edge.swimDistance += horizontal;
                    if (edge.swimDistance > profile.maxSwimDistance + .0001f) yield break;
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
                        bridgeId = surface.bridgeId > 0 ? surface.bridgeId : previousSurface.bridgeId, linkIndex = linkIndex,
                        surfaceBoundary = previousMode != mode || previousSurface.bridgeId != surface.bridgeId });
                }
                previous = point; previousMode = mode; previousSurface = surface;
                yield return null;
            }
            if (bridgeId == 0 || foundBridge) complete(edge);
        }

        internal IEnumerator ValidateRouteSteps(JourneyProfile profile, JourneyRoute route, float priorSwim, Action<bool> complete)
        {
            Quaternion previousHeading = profile.bodyHeading;
            Vector3? previousPoint = null; JourneyTravelMode previousMode = JourneyTravelMode.Walk;
            foreach (JourneySegment segment in route.segments)
            {
                yield return null;
                Quaternion heading = WildlifeBodyEnvelope.Heading(segment.to - segment.from, previousHeading);
                // Replay the original terrain/water sample endpoints; subdividing them again changes boundary modes.
                // Terrain/water structure edits require Rebuild. Dynamic colliders still receive a fresh continuous sweep.
                if (!Project(profile, segment.from, out Vector3 from, out JourneyTravelMode fromMode, out JourneySurface start, heading) ||
                    !Project(profile, segment.to, out Vector3 to, out JourneyTravelMode toMode, out JourneySurface finish, heading) ||
                    !Near(from, segment.from) || !Near(to, segment.to) || !SafeStep(from, to, fromMode, toMode) ||
                    !CanMoveBody(profile, previousPoint ?? from, previousHeading, from, heading) ||
                    !BodyClear(profile, from, heading, to, heading, start, finish)) yield break;
                bool swimming = fromMode == JourneyTravelMode.Swim || toMode == JourneyTravelMode.Swim;
                JourneyTravelMode travel = swimming ? JourneyTravelMode.Swim :
                    fromMode == JourneyTravelMode.Bridge || toMode == JourneyTravelMode.Bridge ? JourneyTravelMode.Bridge : toMode;
                int bridgeId = finish.bridgeId > 0 ? finish.bridgeId : start.bridgeId;
                bool boundary = fromMode != toMode || start.bridgeId != finish.bridgeId;
                if (travel != segment.mode || bridgeId != segment.bridgeId || boundary != segment.surfaceBoundary) yield break;
                // Trace omits sub-millimetre segments; charge an intervening swim gap before an exit resets fatigue.
                if (previousPoint.HasValue && (previousMode == JourneyTravelMode.Swim || fromMode == JourneyTravelMode.Swim))
                {
                    priorSwim += Horizontal(previousPoint.Value, from);
                    if (priorSwim > profile.maxSwimDistance + .0001f) yield break;
                }
                if (fromMode != JourneyTravelMode.Swim) priorSwim = 0;
                if (swimming)
                {
                    priorSwim += Horizontal(from, to);
                    if (priorSwim > profile.maxSwimDistance + .0001f) yield break;
                }
                if (toMode != JourneyTravelMode.Swim) priorSwim = 0;
                previousHeading = heading; previousPoint = to; previousMode = toMode;
            }
            complete(true);
        }
        private static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < .000001f;

        private bool StartTurnClear(JourneyProfile profile, Vector3 start, Edge edge)
        {
            if (edge.segments.Count == 0) return true;
            JourneySegment first = edge.segments[0];
            return CanMoveBody(profile, start, profile.bodyHeading, start,
                WildlifeBodyEnvelope.Heading(first.to - first.from, profile.bodyHeading));
        }

        // Turning feasibility depends on the arrival edge. Keep its finite graph identity (or bounded
        // endpoint pivot) instead of discarding a longer approach that can make the next turn.
        private static bool SameApproach(Edge first, Edge second)
        { return first.arrivalLink == second.arrivalLink && first.pivot == second.pivot; }

        private bool JoinClear(JourneyProfile profile, SearchState state, Edge outgoing)
        {
            for (SearchState cursor = state; cursor != null; cursor = cursor.parent)
                if (cursor.edge.segments.Count > 0) return TurnClear(profile, cursor.edge, outgoing, profile.bodyHeading);
            return outgoing.segments.Count == 0 || StartTurnClear(profile, outgoing.segments[0].from, outgoing);
        }

        private bool TurnClear(JourneyProfile profile, Edge incoming, Edge outgoing, Quaternion fallback)
        {
            if (outgoing.segments.Count == 0) return true;
            JourneySegment first = outgoing.segments[0];
            Quaternion before = fallback;
            if (incoming.segments.Count > 0)
            {
                JourneySegment last = incoming.segments[incoming.segments.Count - 1];
                before = WildlifeBodyEnvelope.Heading(last.to - last.from, fallback);
            }
            return CanMoveBody(profile, first.from, before, first.from, WildlifeBodyEnvelope.Heading(first.to - first.from, before));
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
