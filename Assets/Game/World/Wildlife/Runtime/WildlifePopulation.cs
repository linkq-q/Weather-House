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
        public WildlifePlayerCollision PlayerCollision { get; private set; }
        internal WildlifePlayerCollision EnsurePlayerCollision()
        { if (!PlayerCollision) PlayerCollision = WildlifePlayerCollision.Register(this); return PlayerCollision; }

        private const float CellSize = 6f;
        private const int MaxNeighbours = 12;
        private readonly Dictionary<Vector2Int, List<WildlifeAgent>> cells = new Dictionary<Vector2Int, List<WildlifeAgent>>();
        private readonly Dictionary<int, Group> groups = new Dictionary<int, Group>();
        private readonly List<WildlifeAgent> neighbours = new List<WildlifeAgent>(MaxNeighbours);
        private readonly RaycastHit[] obstacleHits = new RaycastHit[24];
        private readonly Collider[] overlapHits = new Collider[24];
        private readonly Collider[] floorCandidates = new Collider[32];
        private readonly WildlifeDeckFootprint floorFootprint = new WildlifeDeckFootprint();
        private readonly List<ExternalBody> externalBodies = new List<ExternalBody>();
        private float decisionClock, nextAmbientAlarm;
        private float[] decisionDue;
        private int decisionCursor;
        public int LastDecisionCount { get; private set; }
        private Vector3 previousPlayerPosition;

        private sealed class Group
        {
            public Vector3 center, robinWaypoint;
            public int count, robinFlightLeg;
            public float robinWaypointUntil;
            public bool hasRobinWaypoint;
        }
        private struct ExternalBody { public UnityEngine.Object owner; public Transform root; public float radius; public bool grounded; public WildlifeBodyEnvelope envelope; }

        // Bodies driven by another controller share physical space without joining this population's decisions or animation.
        public void RegisterExternalBody(UnityEngine.Object owner, Transform bodyRoot, float radius, bool grounded = true, WildlifeBodyEnvelope envelope = default)
        {
            if (!owner || !bodyRoot) return;
            if (float.IsNaN(radius) || float.IsInfinity(radius)) throw new ArgumentOutOfRangeException(nameof(radius));
            var body = new ExternalBody { owner = owner, root = bodyRoot, radius = Mathf.Max(.05f, radius), grounded = grounded, envelope = envelope };
            for (int i = 0; i < externalBodies.Count; i++)
                if (externalBodies[i].root == bodyRoot) { externalBodies[i] = body; return; }
            externalBodies.Add(body);
        }

        public void UnregisterExternalBodies(UnityEngine.Object owner)
        {
            for (int i = externalBodies.Count - 1; i >= 0; i--)
                if (!externalBodies[i].owner || !externalBodies[i].root || externalBodies[i].owner == owner) externalBodies.RemoveAt(i);
        }

        public Vector3 ExternalSeparation(WildlifeAgent agent)
        {
            if (!agent) return Vector3.zero;
            Vector3 result = Vector3.zero, position = agent.transform.position;
            if (PlayerCollision && PlayerCollision.TryObstacle(position, agent.Radius, out Vector3 playerCenter, out float playerRadius, agent.BodyEnvelope))
            {
                Vector3 away = position - playerCenter; away.y = 0;
                float spacing = agent.BodyEnvelope.ContactRadius(agent.Radius) + playerRadius + .45f, distance = away.magnitude;
                if (distance < spacing && distance > .001f) result += away / distance * (1f - distance / spacing);
            }
            for (int i = externalBodies.Count - 1; i >= 0; i--)
            {
                if (!TryExternalBody(i, agent, position, out ExternalBody body)) continue;
                Vector3 away = position - body.root.position; away.y = 0;
                float distance = away.magnitude, spacing = agent.Radius + body.radius + .45f;
                if (distance >= spacing) continue;
                // A coincident placement still needs a stable direction in which to separate.
                Vector3 direction = distance > .001f ? away / distance :
                    new Vector3(Mathf.Cos(agent.spawnId * 2.399963f), 0, Mathf.Sin(agent.spawnId * 2.399963f));
                result += direction * (1 - distance / spacing);
            }
            return result;
        }

        private bool ExternalBodiesAllowMove(WildlifeAgent agent, Vector3 from, Vector3 to)
        {
            for (int i = externalBodies.Count - 1; i >= 0; i--)
            {
                if (!TryExternalBody(i, agent, to, out ExternalBody body)) continue;
                Vector3 before = from - body.root.position, after = to - body.root.position;
                before.y = after.y = 0;
                float distance = after.magnitude, room = agent.Radius + body.radius + .12f;
                // Preserve the same escape rule as the other controller: an existing overlap may separate, never deepen.
                if (distance < room && distance < before.magnitude + .00001f) return false;
            }
            return true;
        }

        private bool TryExternalBody(int index, WildlifeAgent agent, Vector3 position, out ExternalBody body)
        {
            body = externalBodies[index];
            if (!body.owner || !body.root) { externalBodies.RemoveAt(index); return false; }
            if (body.owner is Behaviour behaviour && !behaviour.isActiveAndEnabled) return false;
            bool groundedAgent = agent.locomotion == WildlifeLocomotion.Ground ||
                (agent.locomotion == WildlifeLocomotion.Flying && agent.AirHeight < .01f);
            return body.grounded && groundedAgent && body.root != agent.transform && body.root.gameObject.activeInHierarchy &&
                Mathf.Abs(body.root.position.y - position.y) <= .65f;
        }

        private void Start() { Initialize(); }
        private void OnEnable() { if (Initialized) PlayerCollision = WildlifePlayerCollision.Register(this); }
        private void OnDisable() { ReleasePlayerCollision(); }
        private void OnDestroy() { ReleasePlayerCollision(); }
        private void ReleasePlayerCollision()
        {
            if (PlayerCollision) PlayerCollision.Unregister(this);
            PlayerCollision = null;
        }

        public void Initialize()
        {
            ReleasePlayerCollision();
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
            decisionDue = new float[agents.Length]; decisionCursor = 0;
            for (int i = 0; i < decisionDue.Length; i++) decisionDue[i] = Mathf.Max(.1f, decisionInterval) * i / Mathf.Max(1, agents.Length);
            RebuildSpatialIndex();
            PlayerCollision = WildlifePlayerCollision.Register(this);
        }

        internal Vector3 ConstrainPlayerMove(WildlifePlayerCollision playerContact, Vector3 displacement)
        {
            foreach (WildlifeAgent agent in agents)
                if (agent && agent.isActiveAndEnabled)
                    displacement = playerContact.ConstrainAgainstBody(displacement, agent.transform, agent.Radius, agent.BodyEnvelope);
            foreach (ExternalBody body in externalBodies)
            {
                if (!body.owner || !body.root || (body.owner is Behaviour behaviour && !behaviour.isActiveAndEnabled)) continue;
                displacement = playerContact.ConstrainAgainstBody(displacement, body.root, body.radius, body.envelope);
            }
            return displacement;
        }

        internal CollisionFlags PlayerContactFlags(WildlifePlayerCollision contact, Vector3 displacement, float requestedY)
        {
            CollisionFlags flags = CollisionFlags.None;
            foreach (WildlifeAgent agent in agents)
                if (agent && agent.isActiveAndEnabled)
                    flags |= contact.ContactFlags(displacement, requestedY, agent.transform, agent.Radius, agent.BodyEnvelope);
            foreach (ExternalBody body in externalBodies)
            {
                if (!body.owner || !body.root || (body.owner is Behaviour behaviour && !behaviour.isActiveAndEnabled)) continue;
                flags |= contact.ContactFlags(displacement, requestedY, body.root, body.radius, body.envelope);
            }
            return flags;
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
            }
            LastDecisionCount = 0;
            if (decisionDue == null || decisionDue.Length != agents.Length) decisionDue = new float[agents.Length];
            // A rotating cursor preserves fairness when a slow frame exhausts the shared CPU budget.
            for (int visited = 0; visited < agents.Length; visited++)
            {
                int index = decisionCursor; decisionCursor = (decisionCursor + 1) % agents.Length;
                WildlifeAgent agent = agents[index];
                if (!agent || !agent.isActiveAndEnabled || SimulationTime < decisionDue[index]) continue;
                if (!AnimalWorkBudget.TryBeginDecision(GetInstanceID(),out var decisionWork)) { decisionCursor = index; break; }
                using var decisionScope = decisionWork;
                decisionDue[index] = SimulationTime + Mathf.Max(.1f, decisionInterval);
                FindNeighbours(agent, neighbours);
                agent.Think(SimulationTime, neighbours); LastDecisionCount++;
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

        // Only robin cruise calls this; landing and alarm escape remain individual decisions.
        public Vector3 RobinFlightWaypoint(int groupId, Vector3 fallback)
        {
            if (!groups.TryGetValue(groupId, out Group group)) groups.Add(groupId, group = new Group());
            if (!group.hasRobinWaypoint || SimulationTime >= group.robinWaypointUntil)
            {
                Vector3 center = group.count > 0 ? group.center : fallback;
                var random = new WildlifeRandom(worldSeed, groupId * 977 + group.robinFlightLeg++, "robin_flock_route");
                Vector2 travel = random.InCircle(1);
                if (travel.sqrMagnitude < .001f) travel = Vector2.up;
                travel = travel.normalized * random.Range(12, 17);
                group.robinWaypoint = center + new Vector3(travel.x, 0, travel.y);
                group.robinWaypoint.x = Mathf.Clamp(group.robinWaypoint.x, worldBounds.min.x + 4, worldBounds.max.x - 4);
                group.robinWaypoint.z = Mathf.Clamp(group.robinWaypoint.z, worldBounds.min.z + 4, worldBounds.max.z - 4);
                group.robinWaypointUntil = SimulationTime + random.Range(8, 11);
                group.hasRobinWaypoint = true;
            }
            return group.robinWaypoint;
        }

        public bool TryAmbientAlarm(Vector3 origin, float radius, float intensity, WildlifeAgent source = null)
        {
            if (SimulationTime < nextAmbientAlarm) return false;
            nextAmbientAlarm = SimulationTime + .8f;
            EmitStartle(origin, radius, intensity, source);
            return true;
        }

        public WildlifeAlarm EmitStartle(Vector3 origin, float radius, float intensity, WildlifeAgent source = null)
        {
            var alarm = new WildlifeAlarm
            {
                id = ++AlarmCount, origin = origin, radius = Mathf.Clamp(radius, .1f, 12f),
                intensity = Mathf.Clamp01(intensity), expiresAt = SimulationTime + 2f,
                source = source, sourceSpecies = source != null ? source.speciesId : null
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
            // This checks access to a partner, not permission to walk through its body.
            return TryMoveCore(first, second.transform.position, out _, first.transform.rotation, second);
        }

        internal float BodySpacing(WildlifeAgent first, WildlifeAgent second, Vector3 toward)
        {
            if (first.locomotion != WildlifeLocomotion.Ground || second.locomotion != WildlifeLocomotion.Ground)
                return first.Radius + second.Radius;
            return first.BodyEnvelope.Reach(toward, first.transform.rotation, first.Radius) +
                second.BodyEnvelope.Reach(-toward, second.transform.rotation, second.Radius);
        }

        private bool GroundBodiesAllowMove(WildlifeAgent agent, Vector3 from, Quaternion fromRotation,
            Vector3 to, Quaternion toRotation, WildlifeAgent socialTarget = null)
        {
            if (agent.locomotion != WildlifeLocomotion.Ground) return true;
            foreach (WildlifeAgent other in agents)
            {
                if (!other || other == agent || other == socialTarget || !other.isActiveAndEnabled ||
                    other.locomotion != WildlifeLocomotion.Ground) continue;
                float reach = agent.BodyEnvelope.HorizontalRadius + other.BodyEnvelope.HorizontalRadius + Vector3.Distance(from, to);
                Vector3 delta = other.transform.position - from; delta.y = 0;
                if (delta.sqrMagnitude > reach * reach) continue;
                if (!WildlifeBodyPhysics.ClearOfBody(agent.BodyEnvelope, from, fromRotation, to, toRotation,
                    other.BodyEnvelope, other.transform.position, other.transform.rotation)) return false;
            }
            return true;
        }

        public bool TryProjectPosition(WildlifeAgent agent, Vector3 requested, out Vector3 projected, Quaternion? heading = null)
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
                var body = agent.BodyEnvelope;
                float bodyClearance = Mathf.Max(.18f, body.Valid ? body.Top + .18f : radius * 2.5f);
                float bottomClearance = body.Valid ? Mathf.Max(.08f, .08f - body.Bottom) : radius;
                if (!water.Contains(requested, radius) || (waterSurface && !waterSurface.Contains(requested, -radius)) || ground + bottomClearance >= waterHeight - bodyClearance) return false;
                float depth = agent.speciesId == "fish" ? Mathf.Max(.65f, agent.swimDepth) : agent.swimDepth;
                projected.y = Mathf.Clamp(waterHeight - depth, ground + bottomClearance, waterHeight - bodyClearance);
                return true;
            }
            if (agent.locomotion == WildlifeLocomotion.Flying)
            {
                if (agent.AirHeight < .01f) return TryLandingPosition(agent, requested, out projected, heading);
                float surface = ground;
                if (waterSurface) { if (waterSurface.Contains(requested)) surface = Mathf.Max(surface, waterSurface.Height(requested)); }
                else foreach (HabitatVolume water in waterAreas)
                    if (water.Contains(requested)) surface = Mathf.Max(surface, water.center.y);
                projected.y = surface + agent.AirHeight;
                return true;
            }
            return TryLandingPosition(agent, requested, out projected, heading);
        }

        public bool TryLandingPosition(WildlifeAgent agent, Vector3 requested, out Vector3 projected, Quaternion? heading = null)
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
            return TryFloorSupport(agent, requested, heading ?? agent.transform.rotation, ref projected);
        }

        bool TryFloorSupport(WildlifeAgent agent, Vector3 requested, Quaternion heading, ref Vector3 projected)
        {
            WildlifeBodyEnvelope body=agent.BodyEnvelope;
            if(!body.Valid || obstacleMask.value==0)return true;
            float ground=projected.y, foot=requested.y+body.Bottom;
            float ceiling=Mathf.Max(ground,foot)+.45f;
            Vector3 center=projected+heading*new Vector3(body.center.x,0,body.center.z);
            float guard=WildlifeDeckFootprint.SeparationGuard(center.magnitude,center.magnitude);
            float radius=body.HorizontalRadius+guard;
            int count=Physics.OverlapBoxNonAlloc(new Vector3(center.x,(ground+ceiling)*.5f,center.z),
                new Vector3(radius,(ceiling-ground)*.5f+.002f,radius),floorCandidates,Quaternion.identity,obstacleMask,QueryTriggerInteraction.Ignore);
            if(count==floorCandidates.Length)return false;
            float highest=ground;bool supported=false;
            for(int i=0;i<count;i++)
            {
                var box=floorCandidates[i] as BoxCollider;
                if(!box || box.transform.IsChildOf(agent.transform) || box.GetComponentInParent<WildlifeAgent>() ||
                    (PlayerCollision && PlayerCollision.Owns(box)) || Vector3.Angle(box.transform.up,Vector3.up)>maxSlope)continue;
                if(!floorFootprint.HighestTop(box,center,body.half+new Vector3(guard,0,guard),heading,out float top) ||
                    top<ground-.001f || top>ceiling+.001f || top>ground+.45f && Mathf.Abs(top-foot)>.45f)continue;
                highest=Mathf.Max(highest,top);supported=true;
            }
            // Settle onto the actual floor at initialization as well as during movement. The floor
            // remains in every body sweep, so its sides, undersides and adjacent walls still block.
            if(supported)projected.y=highest-body.Bottom+.001f;
            return true;
        }

        public float GroundHeight(Vector3 point)
        { return terrain != null ? terrain.SampleHeight(point) + terrain.transform.position.y : worldBounds.min.y; }

        private readonly WildlifeBodyPhysics bodyPhysics = new WildlifeBodyPhysics();

        public bool CanRotate(WildlifeAgent agent, Quaternion rotation)
        { return CanRotateAt(agent, agent.transform.position, agent.transform.rotation, rotation); }

        private bool CanRotateAt(WildlifeAgent agent, Vector3 origin, Quaternion fromRotation, Quaternion rotation)
        {
            return agent.locomotion != WildlifeLocomotion.Ground || (bodyPhysics.Clear(agent.BodyEnvelope,
                origin, fromRotation, origin, rotation, obstacleMask,
                owner: agent.transform, player: PlayerCollision, ignoreAnimals: true) &&
                GroundBodiesAllowMove(agent, origin, fromRotation, origin, rotation));
        }

        public bool TryMove(WildlifeAgent agent, Vector3 requested, out Vector3 projected, Quaternion? facing = null)
        { return TryMoveCore(agent, requested, out projected, facing, null); }

        // Layout queries use the real movement rules without moving the animal to the proposed start.
        public bool TryMoveFrom(WildlifeAgent agent, Vector3 origin, Quaternion rotation, Vector3 requested, out Vector3 projected)
        { return TryMoveCore(agent, requested, out projected, null, null, origin, rotation); }

        private bool TryMoveCore(WildlifeAgent agent, Vector3 requested, out Vector3 projected, Quaternion? facing, WildlifeAgent socialTarget,
            Vector3? queryOrigin = null, Quaternion? queryRotation = null)
        {
            Vector3 origin = queryOrigin ?? agent.transform.position;
            Quaternion originRotation = queryRotation ?? agent.transform.rotation;
            projected = origin;
            Vector3 delta = requested - origin;
            bool groundBody = agent.locomotion == WildlifeLocomotion.Ground && agent.BodyEnvelope.Valid;
            Quaternion heading = facing ?? WildlifeBodyEnvelope.Heading(delta, originRotation);
            if (groundBody && !facing.HasValue && !CanRotateAt(agent, origin, originRotation, heading)) return false;
            float horizontalDistance = new Vector2(delta.x, delta.z).magnitude;
            // Check the whole segment: checking only a safe endpoint would permit crossing a narrow pond.
            int steps = Mathf.Max(1, Mathf.CeilToInt(horizontalDistance / Mathf.Max(.12f, agent.Radius)));
            if (steps > 256) return false;
            Vector3 previous = origin;
            Quaternion previousRotation = facing.HasValue ? originRotation : heading;
            for (int i = 1; i <= steps; i++)
            {
                Quaternion rotation = facing.HasValue ? Quaternion.Slerp(originRotation, heading, (float)i / steps) : heading;
                if (!TryProjectPosition(agent, Vector3.Lerp(origin, requested, (float)i / steps), out Vector3 sample, rotation)) return false;
                if (agent.locomotion == WildlifeLocomotion.Ground && Mathf.Abs(sample.y - previous.y) > .45f) return false;
                if(groundBody)
                {
                    if(!BodyMoveClear(agent,previous,previousRotation,sample,rotation,socialTarget) &&
                        !SmallBodyStepClear(agent,previous,previousRotation,sample,rotation,socialTarget))return false;
                }
                else
                {
                    if (PlayerCollision && !PlayerCollision.AllowsAnimalMove(previous, sample, agent.Radius, agent.BodyEnvelope)) return false;
                    if (!ExternalBodiesAllowMove(agent, previous, sample)) return false;
                }
                previousRotation = rotation;
                previous = sample;
            }
            projected = previous;
            if (groundBody) return true;
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

        bool BodyMoveClear(WildlifeAgent agent,Vector3 from,Quaternion before,Vector3 to,Quaternion after,WildlifeAgent socialTarget)
        {
            return (!PlayerCollision || PlayerCollision.AllowsAnimalMove(from,to,agent.Radius,agent.BodyEnvelope)) &&
                ExternalBodiesAllowMove(agent,from,to) &&
                bodyPhysics.Clear(agent.BodyEnvelope,from,before,to,after,obstacleMask,owner:agent.transform,player:PlayerCollision,ignoreAnimals:true) &&
                GroundBodiesAllowMove(agent,from,before,to,after,socialTarget);
        }

        bool SmallBodyStepClear(WildlifeAgent agent,Vector3 from,Quaternion before,Vector3 to,Quaternion after,WildlifeAgent socialTarget)
        {
            float rise=Mathf.Abs(to.y-from.y);
            if(rise<.002f || rise>.45f)return false;
            // A diagonal foot projection can cut a slab's edge. Check a real lift/across/drop
            // volume instead, including clearance above the head and on the far side.
            float height=Mathf.Max(from.y,to.y);
            Vector3 lift=new Vector3(from.x,height,from.z),across=new Vector3(to.x,height,to.z);
            return BodyMoveClear(agent,from,before,lift,before,socialTarget) &&
                BodyMoveClear(agent,lift,before,across,after,socialTarget) &&
                BodyMoveClear(agent,across,after,to,after,socialTarget);
        }

        private bool IsObstacle(Collider collider, WildlifeAgent agent)
        {
            return !(collider is TerrainCollider) && !(PlayerCollision && PlayerCollision.Owns(collider)) && !collider.transform.IsChildOf(agent.transform) &&
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
