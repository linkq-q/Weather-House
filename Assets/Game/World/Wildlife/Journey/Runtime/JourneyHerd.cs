using System;
using System.Collections.Generic;
using UnityEngine;
using WeatherHouse.Hillside;

namespace WeatherHouse.WildlifeJourney
{
    public enum JourneyMemberState { Idle, Forage, Travelling, WaitingForBridge, Regrouping, Social }

    [Serializable]
    public sealed class JourneyActivitySite
    {
        public string name;
        public Vector3 center;
        public float radius = 4f, forage = 1f, rest = .6f;
    }

    [Serializable]
    public sealed class JourneyMember
    {
        public int id;
        public Transform root, visualRoot, headBone;
        public Transform[] paddleBones = new Transform[0];
        public Animator animator;
        public AnimationClip swimClip;
        [Min(.1f)] public float bodyLength = 1.2f;
        public WildlifeClips clips = new WildlifeClips();
        public JourneyProfile profile = new JourneyProfile();
        [Range(0, 1)] public float sociability = .7f;
        [NonSerialized] public JourneyMemberState State;
        [NonSerialized] public JourneyTravelMode Mode;
        [NonSerialized] public bool Arrived;
        [NonSerialized] public float DistanceTravelled, ActualSpeed, ContinuousSwimDistance;
        [NonSerialized] public string LastDecision;
        [NonSerialized] internal JourneyRoute route;
        [NonSerialized] internal int segment, bridge, bridgeLink = -1, crowdStep, crowdResume;
        [NonSerialized] internal float phase, pace, nextChoice, retryAt, blockedSince, hunger, fatigue, crowdRetryAt;
        [NonSerialized] internal Vector3 destination, localTarget, visualPosition;
        [NonSerialized] internal List<Vector3> crowdPath;
        [NonSerialized] internal Quaternion visualRotation, headPose;
        [NonSerialized] internal Quaternion[] paddlePoses;
        [NonSerialized] internal bool overlay;
        [NonSerialized] internal WildlifeAnimation animation;
        [NonSerialized] internal Dictionary<int, float> failures = new Dictionary<int, float>();
    }

    /// <summary>Shared activity and rendezvous; each member keeps its own route and movement rhythm.</summary>
    public sealed class JourneyHerd : MonoBehaviour
    {
        public JourneyNavigation navigation;
        public JourneySocial social;
        public WildlifePopulation surroundingPopulation;
        public JourneyMember[] members = new JourneyMember[0];
        public JourneyActivitySite[] sites = new JourneyActivitySite[0];
        public int seed = 20261023;
        public bool autoAdvance = true;
        public float decisionInterval = .25f, minimumStay = 16f, rendezvousTimeout = 120f;
        [Range(0f, 5f)] public float travelSpread = 2.8f;
        public float SimulationTime { get; private set; }
        public int CompletedJourneys { get; private set; }
        public bool Travelling { get; private set; }
        public int TargetSite { get; private set; }
        public int RouteChanges { get; private set; }
        public string GroupDecision { get; private set; }
        public bool Paused { get; private set; }

        private sealed class Gate
        {
            public readonly List<int> queue = new List<int>();
            public readonly HashSet<int> occupants = new HashSet<int>();
            public Vector3 direction;
            public float nextAdmission;
        }
        private readonly Dictionary<int, Gate> gates = new Dictionary<int, Gate>();
        private readonly HashSet<int> excluded = new HashSet<int>();
        private readonly List<JourneyCrowdObstacle> crowdObstacles = new List<JourneyCrowdObstacle>();
        private JourneySocialParticipant[] participants;
        private WildlifeRandom random;
        private float decisionAt, stayUntil, journeyStarted;
        private int previousSite;
        private bool initialized, recovery;
        private WildlifePopulation registeredPopulation;

        private void Start() { if (!initialized) Initialize(); }
        private void OnEnable()
        {
            if (!initialized || !Application.isPlaying) return;
            RegisterSurroundingBodies();
            foreach (JourneyMember m in members)
                if (Live(m) && m.animator)
                {
                    m.animation = new WildlifeAnimation();
                    m.animation.Initialize(m.animator, m.clips, m.phase, m.swimClip);
                    m.animation.SetPaused(Paused);
                }
        }
        private void Update() { if (!Paused) Simulate(Time.deltaTime); }
        private void LateUpdate() { ApplyBodyLanguage(); }

        public void Initialize()
        {
            UnregisterSurroundingBodies();
            if (!surroundingPopulation) surroundingPopulation = GetComponentInParent<WildlifePopulation>();
            if (navigation) navigation.playerCollision = surroundingPopulation ? surroundingPopulation.EnsurePlayerCollision() : null;
            RestoreBodyLanguage();
            DisposeAnimations();
            if (social) social.ResetSchedule();
            gates.Clear();
            SimulationTime = 0; CompletedJourneys = RouteChanges = 0;
            TargetSite = previousSite = 0; Travelling = recovery = false;
            decisionAt = 0; stayUntil = Mathf.Max(4f, minimumStay * .5f);
            random = new WildlifeRandom(seed, 0, "journey");
            participants = new JourneySocialParticipant[members.Length];
            for (int i = 0; i < members.Length; i++)
            {
                JourneyMember m = members[i];
                if (m == null || !m.root) continue;
                m.profile.bodyEnvelope = WildlifeBodyEnvelope.Measure(m.root, m.profile.bodyRadius).WithLength(m.bodyLength);
                m.profile.bodyHeading = m.root.rotation; m.profile.bodyOwner = m.root;
                var individual = new WildlifeRandom(seed, m.id, "sheep");
                m.phase = individual.Value(); m.pace = individual.Range(.88f, 1.12f);
                m.hunger = individual.Range(.35f, .8f); m.fatigue = individual.Range(.1f, .4f);
                m.Arrived = true; m.State = JourneyMemberState.Idle; m.Mode = JourneyTravelMode.Walk;
                m.DistanceTravelled = m.ActualSpeed = m.ContinuousSwimDistance = 0; m.route = null; m.segment = m.bridge = 0; m.bridgeLink = -1;
                m.failures.Clear(); m.blockedSince = m.retryAt = m.crowdRetryAt = 0; m.crowdPath = null; m.nextChoice = individual.Range(.5f, 3f);
                m.localTarget = m.root.position;
                if (m.visualRoot) { m.visualPosition = m.visualRoot.localPosition; m.visualRotation = m.visualRoot.localRotation; }
                m.paddlePoses = new Quaternion[m.paddleBones.Length];
                if (Application.isPlaying && m.animator)
                {
                    m.animation = new WildlifeAnimation();
                    m.animation.Initialize(m.animator, m.clips, m.phase, m.swimClip);
                    m.animation.SetPaused(Paused);
                }
                m.LastDecision = "观察同伴与草地";
            }
            initialized = navigation && sites.Length > 0;
            RegisterSurroundingBodies();
            GroupDecision = "在出发地自由活动";
        }

        public void SetPaused(bool value)
        {
            Paused = value;
            foreach (JourneyMember m in members)
                if (Live(m) && m.animation != null) m.animation.SetPaused(value);
        }

        public bool BeginJourney(int siteIndex)
        {
            if (!initialized || siteIndex < 0 || siteIndex >= sites.Length) return false;
            if (Travelling && HasMemberCrossing()) return false;
            var routes = new JourneyRoute[members.Length];
            var destinations = new Vector3[members.Length];
            int liveCount = 0; float spacing = .1f, bodyRadius = 0; JourneyProfile widest = null;
            foreach (JourneyMember member in members) if (Live(member))
            {
                RefreshBody(member);
                liveCount++; spacing = Mathf.Max(spacing, member.bodyLength + .18f);
                bodyRadius = Mathf.Max(bodyRadius, member.profile.bodyRadius, member.bodyLength * .5f);
                if (widest == null || member.profile.bodyRadius > widest.bodyRadius) widest = member.profile;
            }
            if (liveCount == 0) return false;
            bool planned = false;
            // Place an irregular, evenly spaced group, then verify everyone's independent route before committing anyone.
            for (int attempt = 0; attempt < 4 && !planned; attempt++)
            {
                int placementSeed = unchecked(seed + siteIndex * 73856093 + (int)(SimulationTime * 1000) + attempt * 19349663);
                if (!JourneyScatter.TryCreate(sites[siteIndex].center, sites[siteIndex].radius, spacing,
                    liveCount, placementSeed, out Vector3[] goals,
                    point => navigation.TryProject(widest, point, out Vector3 ground, out JourneyTravelMode groundMode) &&
                        groundMode == JourneyTravelMode.Walk && ClearSurroundingPlace(ground, bodyRadius, widest.bodyEnvelope))) continue;
                int selected = 0; planned = true;
                for (int i = 0; i < members.Length; i++)
                {
                    JourneyMember m = members[i];
                    if (!Live(m)) continue;
                    Vector3 goal = goals[selected++];
                    if (!navigation.TryProject(m.profile, goal, out goal, out JourneyTravelMode mode) || mode != JourneyTravelMode.Walk ||
                        !ClearSurroundingPlace(goal, Mathf.Max(m.profile.bodyRadius, m.bodyLength * .5f), m.profile.bodyEnvelope))
                    { planned = false; break; }
                    routes[i] = navigation.FindRoute(m.root.position, goal, m.profile);
                    if (routes[i] == null) { planned = false; break; }
                    var original = new List<JourneySegment>(routes[i].segments);
                    JourneyRouteVariation.Apply(navigation, m.profile, routes[i], (m.phase - .5f) * 2f * travelSpread, 0f, unchecked(seed * 397 ^ m.id));
                    if (!navigation.BodyRouteClear(m.profile, routes[i])) routes[i].segments = original;
                    destinations[i] = goal;
                }
            }
            if (!planned) { GroupDecision = "目的地容不下全群或并非每位成员可达，继续寻找可会合区域"; return false; }
            previousSite = TargetSite; TargetSite = siteIndex;
            Travelling = true; journeyStarted = SimulationTime; gates.Clear();
            for (int i = 0; i < members.Length; i++)
            {
                JourneyMember m = members[i];
                if (!Live(m)) continue;
                m.destination = destinations[i]; m.route = routes[i]; m.segment = 0;
                m.Arrived = false; m.bridge = 0; m.bridgeLink = -1;
                m.failures.Clear(); m.blockedSince = m.crowdRetryAt = 0; m.crowdPath = null;
                m.nextChoice = SimulationTime + random.Range(0f, 2.4f);
                m.State = JourneyMemberState.Travelling;
                m.LastDecision = m.profile.canSwim ? "比较水路与陆路后出发" : "沿可步行路线前往会合点";
            }
            GroupDecision = "前往 " + sites[siteIndex].name + "；允许分队，会合后再继续";
            return true;
        }

        public void Simulate(float deltaTime)
        {
            if (!initialized || Paused || deltaTime <= 0) return;
            float dt = Mathf.Min(deltaTime, .1f);
            foreach (JourneyMember member in members) if (Live(member)) RefreshBody(member);
            SimulationTime += dt;
            RestoreBodyLanguage();
            if (SimulationTime >= decisionAt)
            {
                decisionAt = SimulationTime + Mathf.Max(.1f, decisionInterval);
                Decide();
            }
            for (int i = 0; i < members.Length; i++)
            {
                JourneyMember m = members[i];
                if (!Live(m)) continue;
                m.hunger = Mathf.Clamp01(m.hunger + dt * (m.State == JourneyMemberState.Forage ? -.045f : .004f));
                m.fatigue = Mathf.Clamp01(m.fatigue + dt * (m.ActualSpeed > .1f ? .007f : -.012f));
                if (social && social.IsParticipating(m.id)) SocialMove(m, dt);
                else if (Travelling && !m.Arrived) Travel(m, dt);
                else Loiter(m, dt);
                Animate(m, dt);
            }
            for (int i = 0; i < members.Length; i++)
            {
                JourneyMember m = members[i];
                participants[i] = new JourneySocialParticipant { id = m == null ? -1 : m.id, root = m == null ? null : m.root,
                    radius = m == null ? .3f : m.profile.bodyRadius, head = m == null ? null : m.headBone, bodyLength = m == null ? 0 : m.bodyLength,
                    available = CanSocialize(m) };
            }
            if (social) social.Tick(SimulationTime, participants);
            RegisterSurroundingBodies();
        }

        private bool CanSocialize(JourneyMember member)
        {
            if (!Live(member) || member.sociability <= 0 || member.bridge != 0 ||
                member.Mode != JourneyTravelMode.Walk || member.crowdPath != null) return false;
            if (member.Arrived) return true;
            if (!Travelling || member.route == null || member.segment >= member.route.segments.Count || UpcomingBridge(member)) return false;
            // A pair may pause on open ground; nobody stops to chat while entering water or a narrow crossing.
            for (int i = member.segment; i < member.route.segments.Count; i++)
            {
                JourneySegment part = member.route.segments[i];
                if (FlatDistance(member.root.position, part.from) > 3.5f) break;
                if (part.surfaceBoundary || part.mode != JourneyTravelMode.Walk || part.bridgeId != 0) return false;
            }
            return true;
        }

        private void Decide()
        {
            foreach (Gate gate in gates.Values)
            {
                gate.queue.RemoveAll(id => !IsLiveId(id));
                gate.occupants.RemoveWhere(id => !IsLiveId(id));
            }
            foreach (JourneyMember member in members)
                if (member != null && !Live(member)) { member.bridge = 0; member.bridgeLink = -1; }
            bool allArrived = true;
            foreach (JourneyMember m in members) if (Live(m) && !m.Arrived) allArrived = false;
            if (Travelling && allArrived)
            {
                Travelling = false; recovery = false; CompletedJourneys++;
                stayUntil = SimulationTime + minimumStay + random.Range(0, 8f);
                GroupDecision = "全员会合，在 " + sites[TargetSite].name + " 吃草和交流";
            }
            if (Travelling && SimulationTime - journeyStarted > rendezvousTimeout)
            {
                // A stranded member changes the rendezvous; nobody is teleported or silently removed.
                if (HasMemberCrossing())
                    GroupDecision = "让水中和桥上的同伴先到安全岸边，再调整会合点";
                else if (!recovery)
                {
                    int fallback = previousSite;
                    if (BeginJourney(fallback)) { recovery = true; GroupDecision = "重新选择全员可达的会合地"; }
                    else { journeyStarted = SimulationTime; GroupDecision = "同伴受阻，留在安全处等待通路恢复"; }
                }
                else journeyStarted = SimulationTime;
            }
            if (Travelling || !autoAdvance || SimulationTime < stayUntil || sites.Length < 2) return;
            if (social && social.FirstParticipantId >= 0) return;
            float hunger = 0, fatigue = 0; int count = 0;
            foreach (JourneyMember m in members) if (Live(m)) { hunger += m.hunger; fatigue += m.fatigue; count++; }
            if (count == 0) return;
            hunger /= count; fatigue /= count;
            int best = -1; float bestScore = float.NegativeInfinity;
            for (int i = 0; i < sites.Length; i++)
            {
                if (i == TargetSite) continue;
                float score = sites[i].forage * (.4f + hunger) + sites[i].rest * fatigue -
                    FlatDistance(sites[i].center, sites[TargetSite].center) * .008f + random.Range(0, .15f);
                if (score > bestScore) { bestScore = score; best = i; }
            }
            if (best >= 0 && !BeginJourney(best)) stayUntil = SimulationTime + 5f;
        }

        private void Travel(JourneyMember m, float dt)
        {
            m.ActualSpeed = 0;
            if (SimulationTime < m.nextChoice) return;
            if (m.route == null)
            {
                m.State = JourneyMemberState.Regrouping;
                if (SimulationTime < m.retryAt) return;
                m.crowdPath = null;
                excluded.Clear();
                foreach (var pair in m.failures) if (pair.Value > SimulationTime) excluded.Add(pair.Key);
                m.route = navigation.FindRoute(m.root.position, m.destination, m.profile, excluded, m.ContinuousSwimDistance);
                m.retryAt = SimulationTime + 3f; m.segment = 0;
                if (m.route == null) { m.LastDecision = "记住受阻路段，等待或寻找其他通路"; return; }
                var original = new List<JourneySegment>(m.route.segments);
                JourneyRouteVariation.Apply(navigation, m.profile, m.route, (m.phase - .5f) * 2f * travelSpread, m.ContinuousSwimDistance, unchecked(seed * 397 ^ m.id));
                if (!navigation.BodyRouteClear(m.profile, m.route)) m.route.segments = original;
                RouteChanges++; m.LastDecision = "找到新的可行路线";
            }
            while (m.segment < m.route.segments.Count && (m.segment == m.route.segments.Count - 1
                ? FlatDistance(m.root.position, m.route.segments[m.segment].to) < (m.route.segments[m.segment].surfaceBoundary ? .001f : .18f)
                : PassedWaypoint(m, m.route.segments[m.segment], m.route.segments[m.segment + 1]))) m.segment++;
            if (m.segment >= m.route.segments.Count)
            {
                ReleaseBridge(m); m.Arrived = true; m.route = null;
                m.State = JourneyMemberState.Regrouping; m.localTarget = m.root.position;
                m.nextChoice = SimulationTime + random.Range(1f, 3f); m.LastDecision = "已经抵达，吃草并等候同伴"; return;
            }
            JourneySegment segment = m.route.segments[m.segment];
            if (m.bridge != 0 && segment.bridgeId != m.bridge) ReleaseBridge(m);
            if (segment.bridgeId != 0 && !AdmitBridge(m, segment))
            {
                m.State = JourneyMemberState.WaitingForBridge; m.LastDecision = "在桥外留出出口，等候前方同伴通过";
                Vector3 entryDirection = BridgeDirection(m, segment);
                if (gates.TryGetValue(segment.bridgeId, out Gate occupied) && occupied.occupants.Count > 0 &&
                    Vector3.Dot(entryDirection.normalized, occupied.direction) < .5f)
                {
                    Vector3 waiting = segment.from - entryDirection.normalized * (m.bodyLength + .5f);
                    Vector3 retreat = Vector3.MoveTowards(m.root.position, waiting, m.profile.walkSpeed * .6f * dt);
                    if (navigation.TryStep(m.profile, m.root.position, retreat, out retreat, out JourneyTravelMode retreatMode) && retreatMode == JourneyTravelMode.Walk &&
                        AvoidOthers(m, retreat, retreatMode, out retreat)) Move(m, retreat, dt);
                }
                return;
            }
            m.State = JourneyMemberState.Travelling;
            float speed = (segment.mode == JourneyTravelMode.Swim ? m.profile.swimSpeed : m.profile.walkSpeed) * m.pace;
            if (segment.mode == JourneyTravelMode.Wade) speed *= .7f;
            Vector3 target = segment.to;
            int targetSegment = m.segment;
            // Smooth fine samples only when the entire shortcut is safe on this same surface.
            for (int ahead = m.segment + 1; !segment.surfaceBoundary && ahead < m.route.segments.Count; ahead++)
            {
                JourneySegment candidate = m.route.segments[ahead];
                if (candidate.surfaceBoundary || candidate.mode != segment.mode || candidate.bridgeId != segment.bridgeId || candidate.linkIndex != segment.linkIndex ||
                    FlatDistance(m.root.position, candidate.to) > 1.3f) break;
                target = candidate.to;
                targetSegment = ahead;
            }
            if (target != segment.to && !navigation.TryStep(m.profile, m.root.position, target, out _, out _, segment.mode, segment.bridgeId))
                target = segment.to;
            else if (m.crowdPath == null)
                // The validated shortcut replaces those legs; keep the cursor on the endpoint we actually pursue.
                m.segment = targetSegment;
            if (m.crowdPath != null && m.crowdStep < m.crowdPath.Count) target = m.crowdPath[m.crowdStep];
            Vector3 next = Vector3.MoveTowards(m.root.position, target, speed * dt);
            if (!navigation.TryStep(m.profile, m.root.position, next, out Vector3 projected, out JourneyTravelMode mode) || !CanMoveBody(m, projected, dt))
            {
                if (m.blockedSince == 0) m.blockedSince = SimulationTime;
                if (SimulationTime - m.blockedSince > 1.5f)
                {
                    if (segment.linkIndex >= 0) m.failures[segment.linkIndex] = SimulationTime + 12f;
                    if (m.Mode == JourneyTravelMode.Bridge) { m.LastDecision = "桥面受阻，保留通行方向等待前方恢复"; return; }
                    ReleaseBridge(m); m.route = null; m.retryAt = SimulationTime + .5f; m.blockedSince = 0;
                }
                return;
            }
            // A crowd is temporary: yield or take a small safe side step without blacklisting the route.
            if (!AvoidOthers(m, projected, mode, out projected))
            {
                if (m.bridge != 0 || UpcomingBridge(m)) m.State = JourneyMemberState.WaitingForBridge;
                m.LastDecision = "留出身体间距，等前方同伴走开"; return;
            }
            m.blockedSince = 0; m.Mode = mode;
            Move(m, projected, dt);
            m.LastDecision = mode == JourneyTravelMode.Swim ? "保持水面浮泳，前往可上岸处" :
                mode == JourneyTravelMode.Wade ? "脚踩浅水底缓慢涉水" : m.bridge != 0 ? "沿桥面依次通过" : "沿自己的路线跟进";
        }

        private bool AdmitBridge(JourneyMember m, JourneySegment segment)
        {
            if (!gates.TryGetValue(segment.bridgeId, out Gate gate)) gates.Add(segment.bridgeId, gate = new Gate());
            if (m.bridge == segment.bridgeId && gate.occupants.Contains(m.id)) return true;
            if (!gate.queue.Contains(m.id)) gate.queue.Add(m.id);
            if (gate.queue[0] != m.id || SimulationTime < gate.nextAdmission) return false;
            Vector3 direction = BridgeDirection(m, segment);
            if (gate.occupants.Count > 0 && Vector3.Dot(direction, gate.direction) < .5f) return false;
            if (gate.occupants.Count == 0) gate.direction = direction;
            gate.queue.RemoveAt(0); gate.occupants.Add(m.id);
            gate.nextAdmission = SimulationTime + (m.bodyLength + .35f) / Mathf.Max(.1f, m.profile.walkSpeed * m.pace);
            m.bridge = segment.bridgeId; m.bridgeLink = segment.linkIndex; return true;
        }

        private static Vector3 BridgeDirection(JourneyMember member, JourneySegment current)
        {
            int first = member.segment, last = first;
            while (first > 0 && member.route.segments[first - 1].bridgeId == current.bridgeId) first--;
            while (last + 1 < member.route.segments.Count && member.route.segments[last + 1].bridgeId == current.bridgeId) last++;
            Vector3 direction = member.route.segments[last].to - member.route.segments[first].from;
            direction.y = 0;
            return direction.normalized;
        }

        private void ReleaseBridge(JourneyMember m)
        {
            foreach (Gate gate in gates.Values) { gate.occupants.Remove(m.id); gate.queue.Remove(m.id); }
            m.bridge = 0; m.bridgeLink = -1;
        }

        private void Loiter(JourneyMember m, float dt)
        {
            m.ActualSpeed = 0;
            if (SimulationTime >= m.nextChoice && m.crowdPath == null)
            {
                m.nextChoice = SimulationTime + random.Range(3f, 7f);
                // Hunger, rest and room to move compete; nearby animals retain independent clocks.
                float forageScore = .25f + m.hunger + random.Range(0, .3f);
                float restScore = m.fatigue + random.Range(0, .45f);
                float wanderScore = random.Range(.1f, 1f);
                m.State = forageScore > restScore && forageScore > wanderScore ? JourneyMemberState.Forage : JourneyMemberState.Idle;
                m.localTarget = m.root.position;
                if (wanderScore > Mathf.Max(forageScore, restScore))
                {
                    Vector2 drift = random.InCircle(1.5f);
                    Vector3 candidate = m.root.position + new Vector3(drift.x, 0, drift.y);
                    if (FlatDistance(candidate, sites[TargetSite].center) < sites[TargetSite].radius &&
                        navigation.TryProject(m.profile, candidate, out Vector3 safe, out JourneyTravelMode mode) && mode == JourneyTravelMode.Walk)
                        m.localTarget = safe;
                }
            }
            if (m.crowdPath != null || FlatDistance(m.root.position, m.localTarget) > .1f)
            {
                Vector3 target = m.crowdPath != null && m.crowdStep < m.crowdPath.Count ? m.crowdPath[m.crowdStep] : m.localTarget;
                Vector3 next = Vector3.MoveTowards(m.root.position, target, m.profile.walkSpeed * .5f * dt);
                if (navigation.TryStep(m.profile, m.root.position, next, out next, out JourneyTravelMode mode, JourneyTravelMode.Walk) && mode == JourneyTravelMode.Walk &&
                    AvoidOthers(m, next, mode, out next)) Move(m, next, dt);
                else if (m.crowdPath == null) m.localTarget = m.root.position;
            }
            m.Mode = JourneyTravelMode.Walk;
        }

        private void SocialMove(JourneyMember m, float dt)
        {
            m.State = JourneyMemberState.Social; m.ActualSpeed = 0; m.localTarget = m.root.position;
            int otherId = social.FirstParticipantId == m.id ? social.SecondParticipantId : social.FirstParticipantId;
            JourneyMember partner = Array.Find(members, p => p != null && p.id == otherId);
            if (!Live(partner)) return;
            Vector3 direction = partner.root.position - m.root.position; direction.y = 0;
            float targetSpace = (m.bodyLength + partner.bodyLength) * .5f + (social.CurrentIntent == "greeting" || social.CurrentIntent == "you" ? .25f : .04f);
            if (direction.magnitude > targetSpace)
            {
                Vector3 next = m.root.position + direction.normalized * Mathf.Min(.35f * dt, direction.magnitude - targetSpace);
                if (navigation.TryStep(m.profile, m.root.position, next, out next, out JourneyTravelMode mode, JourneyTravelMode.Walk) && mode == JourneyTravelMode.Walk &&
                    ClearOfOthers(m, next, partner)) Move(m, next, dt);
            }
            if (direction.sqrMagnitude > .001f)
            {
                Quaternion rotation = Quaternion.RotateTowards(m.root.rotation, Quaternion.LookRotation(direction), dt * 100f);
                if (navigation.CanMoveBody(m.profile, m.root.position, m.root.rotation, m.root.position, rotation)) m.root.rotation = rotation;
            }
            m.LastDecision = "注意交流对象，靠近并回应";
        }

        private bool AvoidOthers(JourneyMember m, Vector3 desired, JourneyTravelMode mode, out Vector3 result)
        {
            result = desired;
            float step = FlatDistance(m.root.position, desired);
            if (m.crowdPath != null)
            {
                while (m.crowdStep < m.crowdPath.Count && FlatDistance(m.root.position, m.crowdPath[m.crowdStep]) < .12f) m.crowdStep++;
                if (m.crowdStep >= m.crowdPath.Count)
                {
                    if (m.crowdResume >= 0) m.segment = Mathf.Max(m.segment, m.crowdResume);
                    m.crowdPath = null;
                    return false; // Re-evaluate the resumed route next frame, without stepping toward its old waypoint.
                }
                Vector3 next = Vector3.MoveTowards(m.root.position, m.crowdPath[m.crowdStep], step);
                if (navigation.TryStep(m.profile, m.root.position, next, out next, out JourneyTravelMode nextMode, JourneyTravelMode.Walk) &&
                    nextMode == JourneyTravelMode.Walk && ClearOfOthers(m, next)) { result = next; return true; }
                m.crowdPath = null;
            }
            if (ClearOfOthers(m, desired, null, mode)) return true;
            if (mode == JourneyTravelMode.Walk && SimulationTime >= m.crowdRetryAt && TryYieldAtBridge(m))
            { m.crowdRetryAt = SimulationTime + .7f; return false; }
            // Narrow crossings and swimmers retain their queue; open land can plan around a neighbour.
            if (m.bridge != 0 || mode != JourneyTravelMode.Walk || (UpcomingBridge(m, 3f) && FollowingSameRoute(m)))
            {
                if (SimulationTime >= m.crowdRetryAt) m.crowdRetryAt = SimulationTime + .7f;
                return false;
            }
            if (SimulationTime < m.crowdRetryAt) return false;
            m.crowdRetryAt = SimulationTime + .6f + m.phase * .2f;
            Vector3 target = m.localTarget;
            int resume = -1;
            if (!m.Arrived && m.route != null)
            {
                bool remainingWalk = true;
                for (int i = m.segment; i < m.route.segments.Count; i++)
                {
                    JourneySegment candidate = m.route.segments[i];
                    if (candidate.mode != JourneyTravelMode.Walk || candidate.bridgeId != 0) { remainingWalk = false; break; }
                    if (FlatDistance(m.root.position, candidate.to) <= 6f && ClearOfOthers(m, candidate.to, endpointOnly: true))
                    { target = candidate.to; resume = i; }
                }
                if (remainingWalk && FlatDistance(m.root.position, m.destination) <= 8f && ClearOfOthers(m, m.destination, endpointOnly: true))
                { target = m.destination; resume = m.route.segments.Count - 1; }
                if (remainingWalk && FlatDistance(m.root.position, m.destination) <= 6f &&
                    !ClearSurroundingPlace(m.destination, Mathf.Max(m.profile.bodyRadius, m.bodyLength * .5f), m.profile.bodyEnvelope) &&
                    ChooseNearbyRestingPlace(m)) return false;
                if (resume < 0) { RequestRoom(m); return false; }
            }
            if (FlatDistance(m.root.position, target) < .2f) return false;
            crowdObstacles.Clear();
            if (navigation.playerCollision && navigation.playerCollision.TryObstacle(m.root.position,
                Mathf.Max(m.profile.bodyRadius, m.bodyLength * .5f), out Vector3 playerCenter, out float playerRadius, m.profile.bodyEnvelope))
                crowdObstacles.Add(new JourneyCrowdObstacle { center = playerCenter, radius = playerRadius +
                    Mathf.Max(0, m.profile.bodyEnvelope.ContactRadius(Mathf.Max(m.profile.bodyRadius, m.bodyLength * .5f)) -
                        Mathf.Max(m.profile.bodyRadius, m.bodyLength * .5f)) });
            foreach (JourneyMember other in members)
                if (Live(other) && other != m && Mathf.Abs(other.root.position.y - m.root.position.y) < .65f)
                    crowdObstacles.Add(new JourneyCrowdObstacle { center = other.root.position,
                        radius = Mathf.Max(other.profile.bodyRadius, other.bodyLength * .5f) });
            if (surroundingPopulation && surroundingPopulation.agents != null)
                foreach (WildlifeAgent other in surroundingPopulation.agents)
                    if (GroundNeighbour(m, other, m.root.position, JourneyTravelMode.Walk))
                        crowdObstacles.Add(new JourneyCrowdObstacle { center = other.transform.position, radius = other.Radius });
            if (!JourneyCrowdPath.TryFindPath(m.root.position, target, Mathf.Max(m.profile.bodyRadius, m.bodyLength * .5f), crowdObstacles,
                (from, to) => SafeCrowdStep(m, from, to), out List<Vector3> path)) { RequestRoom(m); return false; }
            m.crowdPath = path; m.crowdStep = 1; m.crowdResume = resume;
            return false;
        }

        private bool TryYieldAtBridge(JourneyMember member)
        {
            if (member.Arrived || member.Mode != JourneyTravelMode.Walk || !UpcomingBridge(member, 3f) ||
                !BridgeApproach(member, out int bridge, out Vector3 entry, out Vector3 direction)) return false;
            JourneyMember first = null;
            int ownDistance = Mathf.FloorToInt(FlatDistance(member.root.position, entry) * 10f);
            int ownRank = member.bridge != 0 ? 1 : 0, bestRank = -1, bestDistance = int.MaxValue;
            foreach (JourneyMember other in members)
            {
                if (!Live(other) || other == member || other.Arrived ||
                    !BridgeApproach(other, out int otherBridge, out Vector3 otherEntry, out Vector3 otherDirection) ||
                    bridge != otherBridge || Vector3.Dot(direction, otherDirection) < .7f) continue;
                float gap = FlatDistance(member.root.position, other.root.position);
                float room = Mathf.Max(member.profile.bodyRadius, member.bodyLength * .5f) +
                    Mathf.Max(other.profile.bodyRadius, other.bodyLength * .5f) + .12f;
                if (gap > room + .4f) continue;
                int otherDistance = Mathf.FloorToInt(FlatDistance(other.root.position, otherEntry) * 10f);
                int rank = other.Mode == JourneyTravelMode.Bridge ? 2 : other.bridge != 0 ? 1 : 0;
                // One total order prevents three close arrivals from yielding to each other in a cycle.
                bool ahead = rank > ownRank || (rank == ownRank &&
                    (otherDistance < ownDistance || (otherDistance == ownDistance && other.id < member.id)));
                if (ahead && (rank > bestRank || (rank == bestRank &&
                    (otherDistance < bestDistance || (otherDistance == bestDistance && (first == null || other.id < first.id))))))
                { first = other; bestRank = rank; bestDistance = otherDistance; }
            }
            if (first == null) return false;
            Vector3 away = member.root.position - first.root.position; away.y = 0;
            if (away.sqrMagnitude < .0001f) away = -direction;
            for (int turn = 0; turn < 7; turn++)
            {
                float angle = turn == 0 ? 0 : ((turn + 1) / 2) * 30f * (turn % 2 == 0 ? -1 : 1);
                Vector3 candidate = member.root.position + Quaternion.Euler(0, angle, 0) * away.normalized * .9f;
                Vector3 previous = member.root.position;
                bool safe = true;
                for (int step = 1; step <= 6; step++)
                {
                    if (!navigation.TryStep(member.profile, previous, Vector3.Lerp(member.root.position, candidate, step / 6f),
                        out Vector3 next, out JourneyTravelMode nextMode, JourneyTravelMode.Walk) || nextMode != JourneyTravelMode.Walk || !ClearOfOthers(member, next))
                    { safe = false; break; }
                    previous = next;
                }
                if (!safe) continue;
                member.crowdPath = new List<Vector3> { member.root.position, previous };
                member.crowdStep = 1; member.crowdResume = member.segment;
                member.LastDecision = "在干燥桥口侧让，让更靠前的同伴先通过";
                return true;
            }
            return false;
        }

        private static bool BridgeApproach(JourneyMember member, out int bridge, out Vector3 entry, out Vector3 direction)
        {
            bridge = 0; entry = direction = default;
            if (member.route == null || member.segment >= member.route.segments.Count) return false;
            for (int i = member.segment; i < member.route.segments.Count; i++)
            {
                JourneySegment part = member.route.segments[i];
                if (part.bridgeId == 0) continue;
                entry = part.from;
                if (member.Mode != JourneyTravelMode.Bridge && FlatDistance(member.root.position, entry) > 3f) return false;
                int last = i;
                while (last + 1 < member.route.segments.Count && member.route.segments[last + 1].bridgeId == part.bridgeId) last++;
                direction = member.route.segments[last].to - entry; direction.y = 0; direction.Normalize();
                bridge = part.bridgeId; return true;
            }
            return false;
        }

        private void RequestRoom(JourneyMember traveller)
        {
            if (traveller.Arrived) return;
            JourneyMember yielding = null;
            Vector3 destination = default;
            float best = float.PositiveInfinity;
            foreach (JourneyMember other in members)
            {
                if (!Live(other) || !other.Arrived || other.bridge != 0 || other.Mode != JourneyTravelMode.Walk ||
                    (social && social.IsParticipating(other.id)) || FlatDistance(other.root.position, other.localTarget) > .1f) continue;
                Vector3 away = other.root.position - traveller.root.position; away.y = 0;
                float distance = away.magnitude;
                if (distance < .01f || distance > (traveller.bodyLength + other.bodyLength) * .5f + .8f ||
                    Mathf.Abs(other.root.position.y - traveller.root.position.y) > .65f) continue;
                // Ask one resting neighbour to make room only after the traveller has exhausted safe detours.
                // Every proposed step must separate bodies and stay on the same walkable activity ground.
                for (int turn = 0; turn < 5; turn++)
                {
                    float angle = turn == 0 ? 0 : ((turn + 1) / 2) * 45f * (turn % 2 == 0 ? -1 : 1);
                    Vector3 candidate = other.root.position + Quaternion.Euler(0, angle, 0) * away.normalized * 1.2f;
                    if (FlatDistance(candidate, sites[TargetSite].center) > sites[TargetSite].radius) continue;
                    Vector3 previous = other.root.position;
                    bool safe = true;
                    for (int step = 1; step <= 6; step++)
                    {
                        if (!navigation.TryStep(other.profile, previous, Vector3.Lerp(other.root.position, candidate, step / 6f),
                            out Vector3 next, out JourneyTravelMode mode, JourneyTravelMode.Walk) || mode != JourneyTravelMode.Walk || !ClearOfOthers(other, next))
                        { safe = false; break; }
                        previous = next;
                    }
                    float score = distance + turn * .1f;
                    if (safe && score < best) { yielding = other; destination = previous; best = score; }
                }
            }
            if (yielding == null) return;
            yielding.localTarget = destination; yielding.crowdPath = null;
            yielding.nextChoice = SimulationTime + 1.2f / Mathf.Max(.05f, yielding.profile.walkSpeed * .5f) + 2f;
            yielding.State = JourneyMemberState.Regrouping;
            yielding.LastDecision = "注意到赶来的同伴，挪开一点让它通过";
        }

        private bool SafeCrowdStep(JourneyMember member, Vector3 from, Vector3 to)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(FlatDistance(from, to) / .3f));
            Vector3 previous = from;
            // The visibility graph is planar; each edge still follows the actual ground and must remain on dry land.
            if (!navigation.TryProject(member.profile, previous, out previous, out JourneyTravelMode initial) || initial != JourneyTravelMode.Walk) return false;
            for (int i = 1; i <= steps; i++)
            {
                if (!navigation.TryStep(member.profile, previous, Vector3.Lerp(from, to, (float)i / steps), out Vector3 next, out JourneyTravelMode mode, JourneyTravelMode.Walk) ||
                    mode != JourneyTravelMode.Walk || (member.Arrived && FlatDistance(next, sites[TargetSite].center) > sites[TargetSite].radius)) return false;
                previous = next;
            }
            return true;
        }

        private bool FollowingSameRoute(JourneyMember member)
        {
            if (member.Arrived || member.route == null || member.segment >= member.route.segments.Count) return false;
            JourneySegment own = member.route.segments[member.segment];
            if (own.linkIndex < 0 || own.linkIndex >= navigation.links.Length) return false;
            JourneyLink link = navigation.links[own.linkIndex];
            Vector3 direction = navigation.nodes[link.to].position - navigation.nodes[link.from].position; direction.y = 0;
            direction.Normalize();
            if (Vector3.Dot(direction, own.to - own.from) < 0) direction = -direction;
            foreach (JourneyMember other in members)
            {
                if (!Live(other) || other == member || other.Arrived || other.route == null || other.segment >= other.route.segments.Count) continue;
                JourneySegment next = other.route.segments[other.segment];
                Vector3 tangent = next.to - next.from; tangent.y = 0;
                if (next.linkIndex != own.linkIndex || Vector3.Dot(tangent.normalized, direction) < .85f) continue;
                Vector3 offset = other.root.position - member.root.position; offset.y = 0;
                float ahead = Vector3.Dot(offset, direction), room = (member.bodyLength + other.bodyLength) * .5f + .12f;
                if (ahead < -.01f || (Mathf.Abs(ahead) <= .01f && other.id > member.id) || ahead > room + .75f) continue;
                if ((offset - direction * ahead).sqrMagnitude < room * room) return true;
            }
            return false;
        }

        private static bool UpcomingBridge(JourneyMember m, float distance = 8f)
        {
            if (m.Arrived || m.route == null) return false;
            for (int i = m.segment; i < m.route.segments.Count; i++)
            {
                JourneySegment segment = m.route.segments[i];
                if (FlatDistance(m.root.position, segment.from) > distance) return false;
                if (segment.bridgeId != 0) return true;
            }
            return false;
        }

        private bool ClearOfOthers(JourneyMember m, Vector3 position, JourneyMember partner = null, JourneyTravelMode? nextMode = null, bool endpointOnly = false)
        {
            float ownRadius = Mathf.Max(m.profile.bodyRadius, m.bodyLength * .5f);
            // A detour can reach an unoccupied endpoint even when the straight segment is blocked.
            if (endpointOnly)
            {
                if (!ClearSurroundingPlace(position, ownRadius, m.profile.bodyEnvelope)) return false;
            }
            else if (navigation.playerCollision && !navigation.playerCollision.AllowsAnimalMove(m.root.position, position,
                ownRadius, m.profile.bodyEnvelope)) return false;
            if (m.Arrived && FlatDistance(position, sites[TargetSite].center) > sites[TargetSite].radius &&
                FlatDistance(position, sites[TargetSite].center) >= FlatDistance(m.root.position, sites[TargetSite].center)) return false;
            foreach (JourneyMember other in members)
            {
                if (!Live(other) || other == m || other == partner || Mathf.Abs(position.y - other.root.position.y) > .65f) continue;
                Vector3 offset = position - other.root.position; offset.y = 0;
                float distance = offset.magnitude;
                // A circumscribed body footprint stays safe during turns as well as straight movement.
                float room = Mathf.Max(m.profile.bodyRadius, m.bodyLength * .5f) + Mathf.Max(other.profile.bodyRadius, other.bodyLength * .5f) + .12f;
                // An existing overlap may separate, but cannot deepen.
                if (distance < room && distance < FlatDistance(m.root.position, other.root.position) + .00001f) return false;
            }
            if (surroundingPopulation && surroundingPopulation.agents != null)
                foreach (WildlifeAgent other in surroundingPopulation.agents)
                {
                    if (!GroundNeighbour(m, other, position, nextMode ?? m.Mode)) continue;
                    float distance = FlatDistance(position, other.transform.position);
                    float room = Mathf.Max(m.profile.bodyRadius, m.bodyLength * .5f) + other.Radius + .12f;
                    if (distance < room && distance < FlatDistance(m.root.position, other.transform.position) + .00001f) return false;
                }
            return true;
        }

        private static bool GroundNeighbour(JourneyMember member, WildlifeAgent other, Vector3 position, JourneyTravelMode mode)
        {
            return mode != JourneyTravelMode.Swim && GroundBody(other, position) && other.transform != member.root;
        }

        private static bool GroundBody(WildlifeAgent other, Vector3 position)
        {
            return other && other.isActiveAndEnabled && Mathf.Abs(other.transform.position.y - position.y) <= .65f &&
                (other.locomotion == WildlifeLocomotion.Ground ||
                    (other.locomotion == WildlifeLocomotion.Flying && other.AirHeight < .01f));
        }

        private bool ClearSurroundingPlace(Vector3 position, float radius, WildlifeBodyEnvelope envelope = default)
        {
            if (navigation && navigation.playerCollision && navigation.playerCollision.TryObstacle(position, radius,
                out Vector3 playerCenter, out float playerRadius, envelope) &&
                FlatDistance(position, playerCenter) < envelope.ContactRadius(radius) + playerRadius + .12f) return false;
            if (surroundingPopulation && surroundingPopulation.agents != null)
                foreach (WildlifeAgent other in surroundingPopulation.agents)
                    if (GroundBody(other, position) && FlatDistance(position, other.transform.position) < radius + other.Radius + .12f) return false;
            return true;
        }

        private bool ChooseNearbyRestingPlace(JourneyMember member)
        {
            Vector3 toward = member.root.position - member.destination; toward.y = 0;
            if (toward.sqrMagnitude < .001f) toward = Vector3.forward;
            float radius = Mathf.Max(member.profile.bodyRadius, member.bodyLength * .5f);
            // A resting neighbour may occupy a reserved goal after departure. Pick another nearby patch in the same site.
            for (int ring = 1; ring <= 3; ring++)
                for (int turn = 0; turn < 12; turn++)
                {
                    float angle = ((turn + 1) / 2) * 30f * (turn % 2 == 0 ? -1 : 1);
                    Vector3 candidate = member.destination + Quaternion.Euler(0, angle, 0) * toward.normalized * (ring * 1.2f);
                    if (FlatDistance(candidate, sites[TargetSite].center) > sites[TargetSite].radius ||
                        !navigation.TryProject(member.profile, candidate, out candidate, out JourneyTravelMode mode) || mode != JourneyTravelMode.Walk ||
                        !ClearSurroundingPlace(candidate, radius, member.profile.bodyEnvelope)) continue;
                    bool clear = true;
                    foreach (JourneyMember other in members)
                    {
                        if (!Live(other) || other == member) continue;
                        float room = radius + Mathf.Max(other.profile.bodyRadius, other.bodyLength * .5f) + .12f;
                        if (FlatDistance(candidate, other.root.position) < room || (!other.Arrived && FlatDistance(candidate, other.destination) < room))
                        { clear = false; break; }
                    }
                    if (!clear) continue;
                    JourneyRoute route = navigation.TraceDirectRoute(member.root.position, candidate, member.profile);
                    if (route == null || route.segments.Exists(part => part.mode != JourneyTravelMode.Walk || part.bridgeId != 0)) continue;
                    member.destination = candidate; member.route = route; member.segment = 0; member.crowdPath = null;
                    RouteChanges++; member.LastDecision = "落脚处已有动物休息，在同一片草地另找空位";
                    return true;
                }
            return false;
        }

        private void RegisterSurroundingBodies()
        {
            if (navigation) navigation.playerCollision = surroundingPopulation ? surroundingPopulation.PlayerCollision : null;
            if (registeredPopulation != surroundingPopulation)
            {
                if (registeredPopulation) registeredPopulation.UnregisterExternalBodies(this);
                registeredPopulation = surroundingPopulation;
            }
            if (!registeredPopulation) return;
            foreach (JourneyMember member in members)
                if (member != null && member.root)
                    registeredPopulation.RegisterExternalBody(this, member.root,
                        Mathf.Max(member.profile.bodyRadius, member.bodyLength * .5f), member.Mode != JourneyTravelMode.Swim, member.profile.bodyEnvelope);
        }

        private void UnregisterSurroundingBodies()
        {
            if (registeredPopulation) registeredPopulation.UnregisterExternalBodies(this);
            registeredPopulation = null;
        }

        private bool PassedWaypoint(JourneyMember m, JourneySegment segment, JourneySegment next)
        {
            float distance = FlatDistance(m.root.position, segment.to);
            // At the actual corner, hand an obstructed next leg to normal blocked-route recovery.
            if (distance < .001f) return true;
            if (distance >= .12f)
            {
                if (segment.bridgeId != 0 || segment.mode != m.Mode) return false;
                Vector3 along = segment.to - segment.from; along.y = 0;
                Vector3 offset = m.root.position - segment.to; offset.y = 0;
                if (distance >= 1.6f || Vector3.Dot(offset, along) < 0) return false;
            }
            // Reach water/land and bridge boundaries physically; a skipped bank must not erase a planned rest.
            return !segment.surfaceBoundary && !next.surfaceBoundary && segment.mode == next.mode && segment.bridgeId == next.bridgeId &&
                navigation.TryStep(m.profile, m.root.position, next.to, out _, out _, segment.mode, segment.bridgeId);
        }

        private static bool Live(JourneyMember m) => m != null && m.root && m.root.gameObject.activeInHierarchy;
        private bool IsLiveId(int id) => Array.Exists(members, m => m != null && m.id == id && Live(m));
        private bool HasMemberCrossing() => Array.Exists(members, m => Live(m) && !m.Arrived && (m.Mode == JourneyTravelMode.Bridge || m.Mode == JourneyTravelMode.Swim));
        private static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        private static void RefreshBody(JourneyMember member)
        {
            member.profile.bodyEnvelope = member.profile.bodyEnvelope.WithLength(member.bodyLength);
            member.profile.bodyHeading = member.root.rotation;
        }

        private bool CanMoveBody(JourneyMember m, Vector3 next, float dt)
        {
            Quaternion rotation = Quaternion.RotateTowards(m.root.rotation,
                WildlifeBodyEnvelope.Heading(next - m.root.position, m.root.rotation), dt * 130f);
            return navigation.CanMoveBody(m.profile, m.root.position, m.root.rotation, next, rotation);
        }

        private void Move(JourneyMember m, Vector3 next, float dt)
        {
            if (!CanMoveBody(m, next, dt)) { m.ActualSpeed = 0; return; }
            Vector3 direction = next - m.root.position; direction.y = 0;
            float distance = direction.magnitude;
            m.ActualSpeed = distance / dt; m.DistanceTravelled += distance;
            if (m.Mode == JourneyTravelMode.Swim) m.ContinuousSwimDistance += distance;
            else m.ContinuousSwimDistance = 0;
            if (distance > .0001f) m.root.rotation = Quaternion.RotateTowards(m.root.rotation, Quaternion.LookRotation(direction), dt * 130f);
            m.root.position = next;
        }

        private void Animate(JourneyMember m, float dt)
        {
            if (m.animation == null) return;
            m.animation.Play(CurrentClip(m), true,
                m.ActualSpeed > .03f && m.Mode != JourneyTravelMode.Swim ? Mathf.Clamp(m.ActualSpeed / Mathf.Max(.1f, m.profile.walkSpeed), .25f, 1.4f) : 1f);
            m.animation.Tick(dt);
        }

        private static AnimationClip CurrentClip(JourneyMember m)
        {
            AnimationClip clip = m.Mode == JourneyTravelMode.Swim ? (m.swimClip ? m.swimClip : m.clips.idle) : m.ActualSpeed > .03f ? m.clips.walk :
                m.State == JourneyMemberState.Forage ? m.clips.forage : m.clips.idle;
            return clip ? clip : m.clips.FirstAvailable;
        }

        private void RestoreBodyLanguage()
        {
            foreach (JourneyMember m in members)
            {
                if (!Live(m) || !m.overlay) continue;
                if (m.headBone) m.headBone.localRotation = m.headPose;
                for (int i = 0; i < m.paddleBones.Length; i++) if (m.paddleBones[i]) m.paddleBones[i].localRotation = m.paddlePoses[i];
                if (m.visualRoot) m.visualRoot.SetLocalPositionAndRotation(m.visualPosition, m.visualRotation);
                m.overlay = false;
            }
        }

        public void ApplyBodyLanguage()
        {
            foreach (JourneyMember m in members)
            {
                if (!Live(m) || m.overlay) continue;
                if (m.headBone) m.headPose = m.headBone.localRotation;
                for (int i = 0; i < m.paddleBones.Length; i++) if (m.paddleBones[i]) m.paddlePoses[i] = m.paddleBones[i].localRotation;
                float cycle = SimulationTime * 5.2f + m.phase * Mathf.PI * 2;
                if (m.Mode == JourneyTravelMode.Swim)
                {
                    // Authored clips own the skeleton. The paddle is only a fallback for unbound species.
                    if (!m.swimClip)
                    {
                        if (m.visualRoot) m.visualRoot.SetLocalPositionAndRotation(m.visualPosition + Vector3.up * (Mathf.Sin(cycle) * .018f), m.visualRotation * Quaternion.Euler(-5, 0, Mathf.Sin(cycle * .5f) * 2));
                        for (int i = 0; i < m.paddleBones.Length; i++) if (m.paddleBones[i])
                            m.paddleBones[i].localRotation *= Quaternion.Euler(Mathf.Sin(cycle + i * Mathf.PI) * 22f, 0, 0);
                        if (m.headBone) m.headBone.localRotation *= Quaternion.Euler(-8, 0, 0);
                    }
                }
                else if (m.State == JourneyMemberState.Social && m.headBone)
                {
                    string intent = social && social.ActiveConversation != null ? social.CurrentIntent : "greeting";
                    float response = social && social.ActiveConversation != null && social.ActiveConversation.speakerId == m.id ? 1f : .45f;
                    if (intent == "sniff") m.headBone.localRotation *= Quaternion.Euler(9 + Mathf.Sin(cycle) * 3, Mathf.Sin(cycle * .5f) * 5, 0);
                    else if (intent == "nuzzle")
                    {
                        m.headBone.localRotation *= Quaternion.Euler(3, Mathf.Sin(cycle * .45f) * 12, Mathf.Sin(cycle * .45f) * 5);
                        if (m.visualRoot) m.visualRoot.localRotation = m.visualRotation * Quaternion.Euler(0, 0, Mathf.Sin(cycle * .45f) * 2);
                    }
                    else m.headBone.localRotation *= Quaternion.Euler(Mathf.Sin(cycle * .5f) * 6 * response, Mathf.Sin(cycle * .3f) * 3, 0);
                }
                m.overlay = true;
            }
        }

        private void DisposeAnimations()
        { foreach (JourneyMember m in members) if (m != null && m.animation != null) { m.animation.Dispose(); m.animation = null; } }
        private void OnDisable() { UnregisterSurroundingBodies(); RestoreBodyLanguage(); DisposeAnimations(); }
        private void OnDestroy() { UnregisterSurroundingBodies(); DisposeAnimations(); }
    }
}
