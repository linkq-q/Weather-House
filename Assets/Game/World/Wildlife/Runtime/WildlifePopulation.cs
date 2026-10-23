using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.Hillside
{
    [DefaultExecutionOrder(50)]
    public sealed class WildlifePopulation : MonoBehaviour
    {
        public int worldSeed = 20261023;
        public Terrain terrain;
        public Transform player;
        public Bounds worldBounds = new Bounds(Vector3.zero, new Vector3(300f, 100f, 300f));
        public HabitatVolume[] waterAreas = new HabitatVolume[0];
        public WorldWaterSurface waterSurface;
        public WildlifeAgent[] agents = new WildlifeAgent[0];
        [Range(.1f, 1f)] public float decisionInterval = .2f;
        public float maxSlope = 29f;
        public LayerMask obstacleMask = Physics.DefaultRaycastLayers;
        public bool Initialized { get; private set; }
        public float SimulationTime { get; private set; }
        public int AlarmCount { get; private set; }
        public int GreetingCount { get; private set; }
        public int LastNeighbourVisits { get; private set; }
        public float PlayerSpeed { get; private set; }

        private const float CellSize = 6f;
        private const int MaxNeighbours = 12;
        private readonly Dictionary<Vector2Int, List<WildlifeAgent>> cells = new Dictionary<Vector2Int, List<WildlifeAgent>>();
        private readonly Dictionary<int, Group> groups = new Dictionary<int, Group>();
        private readonly List<WildlifeAgent> neighbours = new List<WildlifeAgent>(MaxNeighbours);
        private readonly RaycastHit[] obstacleHits = new RaycastHit[24];
        private readonly Collider[] overlapHits = new Collider[24];
        private float decisionClock, nextAmbientAlarm;
        private Vector3 previousPlayerPosition;

        private sealed class Group { public Vector3 center; public int count; }

        private void Start() { Initialize(); }

        public void Initialize()
        {
            SimulationTime = decisionClock = nextAmbientAlarm = 0f;
            AlarmCount = GreetingCount = LastNeighbourVisits = 0;
            PlayerSpeed = 0f;
            if (player != null) previousPlayerPosition = player.position;
            if (terrain == null) terrain = GetComponentInChildren<Terrain>();
            if (agents == null || agents.Length == 0) agents = GetComponentsInChildren<WildlifeAgent>();
            if (waterAreas == null) waterAreas = new HabitatVolume[0];
            cells.Clear(); groups.Clear();
            for (int i = 0; i < agents.Length; i++)
                if (agents[i] != null) agents[i].Initialize(this);
            Initialized = true;
            RebuildSpatialIndex();
        }

        private void Update() { Simulate(Time.deltaTime); }

        public void Simulate(float deltaTime)
        {
            if (!Initialized || deltaTime <= 0f) return;
            float dt = Mathf.Min(deltaTime, .1f);
            SimulationTime += dt;
            if (player != null)
            {
                Vector3 travel = player.position - previousPlayerPosition;
                travel.y = 0f;
                PlayerSpeed = Mathf.Lerp(PlayerSpeed, travel.magnitude / dt, Mathf.Min(1f, dt * 8f));
                previousPlayerPosition = player.position;
            }
            decisionClock -= dt;
            if (decisionClock <= 0f)
            {
                decisionClock = Mathf.Max(.1f, decisionInterval);
                RebuildSpatialIndex();
                LastNeighbourVisits = 0;
                foreach (WildlifeAgent agent in agents)
                {
                    if (agent == null || !agent.isActiveAndEnabled) continue;
                    FindNeighbours(agent, neighbours);
                    agent.Think(SimulationTime, neighbours);
                }
            }
            foreach (WildlifeAgent agent in agents)
                if (agent != null && agent.isActiveAndEnabled) agent.Move(dt, SimulationTime);
        }

        private static Vector2Int Cell(Vector3 point)
        { return new Vector2Int(Mathf.FloorToInt(point.x / CellSize), Mathf.FloorToInt(point.z / CellSize)); }

        private void RebuildSpatialIndex()
        {
            foreach (List<WildlifeAgent> bucket in cells.Values) bucket.Clear();
            foreach (Group group in groups.Values) { group.center = Vector3.zero; group.count = 0; }
            foreach (WildlifeAgent agent in agents)
            {
                if (agent == null || !agent.isActiveAndEnabled) continue;
                Vector2Int key = Cell(agent.transform.position);
                if (!cells.TryGetValue(key, out List<WildlifeAgent> bucket))
                    cells.Add(key, bucket = new List<WildlifeAgent>());
                bucket.Add(agent);
                if (!groups.TryGetValue(agent.groupId, out Group group)) groups.Add(agent.groupId, group = new Group());
                group.center += agent.transform.position;
                group.count++;
            }
            foreach (Group group in groups.Values)
                if (group.count > 0) group.center /= group.count;
        }

        private void FindNeighbours(WildlifeAgent agent, List<WildlifeAgent> result)
        {
            result.Clear();
            Vector2Int home = Cell(agent.transform.position);
            // Bounded candidate visits also cap dense-cell work; this is a local social sample, not a nearest-N sort.
            int visits = 0;
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                {
                    if (!cells.TryGetValue(home + new Vector2Int(x, z), out List<WildlifeAgent> bucket)) continue;
                    int offset = bucket.Count == 0 ? 0 : (int)((uint)agent.spawnId % (uint)bucket.Count);
                    for (int n = 0; n < bucket.Count; n++)
                    {
                        if (visits++ >= 48 || result.Count >= MaxNeighbours) { LastNeighbourVisits += visits; return; }
                        WildlifeAgent other = bucket[(n + offset) % bucket.Count];
                        if (other == agent || other == null || !other.isActiveAndEnabled) continue;
                        if ((other.transform.position - agent.transform.position).sqrMagnitude <= CellSize * CellSize)
                            result.Add(other);
                    }
                }
            LastNeighbourVisits += visits;
        }

        public Vector3 GroupCenter(int groupId, Vector3 fallback)
        { return groups.TryGetValue(groupId, out Group group) && group.count > 0 ? group.center : fallback; }

        public bool TryAmbientAlarm(Vector3 origin, float radius, float intensity)
        {
            if (SimulationTime < nextAmbientAlarm) return false;
            nextAmbientAlarm = SimulationTime + .8f;
            EmitStartle(origin, radius, intensity);
            return true;
        }

        public WildlifeAlarm EmitStartle(Vector3 origin, float radius, float intensity)
        {
            var alarm = new WildlifeAlarm
            {
                id = ++AlarmCount, origin = origin, radius = Mathf.Clamp(radius, .1f, 12f),
                intensity = Mathf.Clamp01(intensity), expiresAt = SimulationTime + 2f
            };
            // An alarm is dispatched once from its original location. Recipients never rebroadcast it.
            foreach (WildlifeAgent agent in agents)
                if (agent != null && agent.isActiveAndEnabled) agent.ReceiveStartle(alarm, SimulationTime);
            return alarm;
        }

        public bool TryGreet(WildlifeAgent first, WildlifeAgent second)
        {
            if (first == null || second == null || first == second || !first.CanGreet(SimulationTime) || !second.CanGreet(SimulationTime)) return false;
            if (first.locomotion != second.locomotion) return false;
            if (first.speciesId != second.speciesId && !(first.friendlyAcrossSpecies && second.friendlyAcrossSpecies)) return false;
            if ((first.transform.position - second.transform.position).sqrMagnitude > 3.5f * 3.5f) return false;
            if (!HasClearSocialPath(first, second)) return false;
            first.BeginGreeting(second, SimulationTime);
            second.BeginGreeting(first, SimulationTime);
            GreetingCount++;
            return true;
        }

        private bool HasClearSocialPath(WildlifeAgent first, WildlifeAgent second)
        {
            return TryMove(first, second.transform.position, out _);
        }

        public bool TryProjectPosition(WildlifeAgent agent, Vector3 requested, out Vector3 projected)
        {
            projected = requested;
            float radius = agent.Radius;
            if (requested.x < worldBounds.min.x + radius || requested.x > worldBounds.max.x - radius ||
                requested.z < worldBounds.min.z + radius || requested.z > worldBounds.max.z - radius) return false;
            float ground = GroundHeight(requested);
            if (agent.locomotion == WildlifeLocomotion.Swimming)
            {
                if (agent.waterAreaIndex < 0 || agent.waterAreaIndex >= waterAreas.Length) return false;
                HabitatVolume water = waterAreas[agent.waterAreaIndex];
                float waterHeight = waterSurface ? waterSurface.Height(requested) : water.center.y;
                float bodyClearance = Mathf.Max(.12f, radius * 2.5f);
                if (!water.Contains(requested, radius) || (waterSurface && !waterSurface.Contains(requested, -radius)) || ground + radius >= waterHeight - bodyClearance) return false;
                projected.y = Mathf.Clamp(waterHeight - agent.swimDepth, ground + radius, waterHeight - bodyClearance);
                return true;
            }
            if (agent.locomotion == WildlifeLocomotion.Flying)
            {
                if (agent.AirHeight < .01f) return TryLandingPosition(agent, requested, out projected);
                float surface = ground;
                if (waterSurface) { if (waterSurface.Contains(requested)) surface = Mathf.Max(surface, waterSurface.Height(requested)); }
                else foreach (HabitatVolume water in waterAreas)
                    if (water.Contains(requested)) surface = Mathf.Max(surface, water.center.y);
                projected.y = surface + agent.AirHeight;
                return true;
            }
            return TryLandingPosition(agent, requested, out projected);
        }

        public bool TryLandingPosition(WildlifeAgent agent, Vector3 requested, out Vector3 projected)
        {
            projected = requested;
            float radius = agent.Radius;
            if (requested.x < worldBounds.min.x + radius || requested.x > worldBounds.max.x - radius ||
                requested.z < worldBounds.min.z + radius || requested.z > worldBounds.max.z - radius) return false;
            if (terrain != null)
            {
                Vector3 local = requested - terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                if (local.x < 0f || local.x > size.x || local.z < 0f || local.z > size.z) return false;
                if (terrain.terrainData.GetSteepness(local.x / size.x, local.z / size.z) > maxSlope) return false;
            }
            if (waterSurface) { if (waterSurface.Contains(requested, radius)) return false; }
            else foreach (HabitatVolume water in waterAreas)
                if (water.Contains(requested, -radius)) return false;
            projected.y = GroundHeight(requested);
            return true;
        }

        public float GroundHeight(Vector3 point)
        { return terrain != null ? terrain.SampleHeight(point) + terrain.transform.position.y : worldBounds.min.y; }

        public bool TryMove(WildlifeAgent agent, Vector3 requested, out Vector3 projected)
        {
            Vector3 origin = agent.transform.position;
            projected = origin;
            Vector3 delta = requested - origin;
            float horizontalDistance = new Vector2(delta.x, delta.z).magnitude;
            // Check the whole segment: checking only a safe endpoint would permit crossing a narrow pond.
            int steps = Mathf.Max(1, Mathf.CeilToInt(horizontalDistance / Mathf.Max(.12f, agent.Radius)));
            if (steps > 256) return false;
            Vector3 previous = origin;
            for (int i = 1; i <= steps; i++)
            {
                if (!TryProjectPosition(agent, Vector3.Lerp(origin, requested, (float)i / steps), out Vector3 sample)) return false;
                if (agent.locomotion == WildlifeLocomotion.Ground && Mathf.Abs(sample.y - previous.y) > .45f) return false;
                previous = sample;
            }
            projected = previous;
            Vector3 actualDelta = projected - origin;
            Vector3 lift = agent.locomotion == WildlifeLocomotion.Ground ? Vector3.up * (agent.Radius + .08f) : Vector3.zero;
            if (agent.locomotion == WildlifeLocomotion.Flying && agent.AirHeight < .01f) lift = Vector3.up * (agent.Radius + .08f);
            int overlaps = Physics.OverlapSphereNonAlloc(projected + lift, Mathf.Max(.05f, agent.Radius * .8f),
                overlapHits, obstacleMask, QueryTriggerInteraction.Ignore);
            if (overlaps == overlapHits.Length) return false;
            for (int i = 0; i < overlaps; i++)
                if (IsObstacle(overlapHits[i], agent)) return false;
            if (actualDelta.sqrMagnitude < .000001f) return true;
            int hits = Physics.SphereCastNonAlloc(origin + lift, Mathf.Max(.05f, agent.Radius * .8f), actualDelta.normalized,
                obstacleHits, actualDelta.magnitude, obstacleMask, QueryTriggerInteraction.Ignore);
            if (hits == obstacleHits.Length) return false;
            for (int i = 0; i < hits; i++)
            {
                if (IsObstacle(obstacleHits[i].collider, agent)) return false;
            }
            return true;
        }

        private static bool IsObstacle(Collider collider, WildlifeAgent agent)
        {
            return !(collider is TerrainCollider) && !collider.transform.IsChildOf(agent.transform) &&
                collider.GetComponentInParent<WildlifeAgent>() == null;
        }

        public string ValidateConfiguration()
        {
            var errors = new List<string>();
            var ids = new HashSet<int>();
            if (terrain == null) errors.Add("Terrain is missing.");
            foreach (WildlifeAgent agent in agents)
            {
                if (agent == null) { errors.Add("Null animal entry."); continue; }
                if (!ids.Add(agent.spawnId)) errors.Add("Duplicate spawnId: " + agent.spawnId);
                if (agent.animator == null || agent.clips == null || agent.clips.FirstAvailable == null)
                    errors.Add(agent.name + ": skeleton animator or animation clips missing.");
                if (!TryMove(agent, agent.transform.position, out _)) errors.Add(agent.name + ": invalid habitat position or overlapping obstacle.");
            }
            return string.Join("\n", errors);
        }
    }
}
