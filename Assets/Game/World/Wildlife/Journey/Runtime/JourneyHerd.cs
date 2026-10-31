using System;
using System.Collections;
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
        [NonSerialized] internal int segment, bridge, bridgeLink = -1, crowdStep, crowdResume, checkedPassage = -1;
        [NonSerialized] internal float phase, pace, nextChoice, retryAt, blockedSince, hunger, fatigue, crowdRetryAt;
        [NonSerialized] internal Vector3 destination, localTarget, visualPosition;
        [NonSerialized] internal List<Vector3> crowdPath;
        [NonSerialized] internal Quaternion visualRotation, headPose;
        [NonSerialized] internal Quaternion[] paddlePoses;
        [NonSerialized] internal bool overlay;
        [NonSerialized] internal WildlifeAnimation animation;
        [NonSerialized] internal JourneyBodyStep bodyStep;
        [NonSerialized] internal JourneyProfile passageProfile;
        [NonSerialized] internal int bodyStepPhase;
        [NonSerialized] internal JourneyMember bodyStepPartner;
        [NonSerialized] internal Dictionary<int, float> failures = new Dictionary<int, float>();
        [NonSerialized] internal int failureNavigationVersion;
        [NonSerialized] internal float groundCheckAt;
        [NonSerialized] internal Dictionary<int, float> routeProgress = new Dictionary<int, float>();
        [NonSerialized] internal Vector3 progressDestination;
        [NonSerialized] internal float newRouteProgress;
        [NonSerialized] internal readonly WildlifeRestSequence rest = new WildlifeRestSequence();
        [NonSerialized] internal bool autonomous, rejoining;
        [NonSerialized] internal int intentRevision;
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
        [Range(.1f, 8f)] public float planningBudgetMilliseconds = 2f;
        public bool IsPlanning => pending != null;
        public double LastPlanningMilliseconds { get; private set; }
        public double MaximumPlanningUnitMilliseconds { get; private set; }
        public double MaximumRouteSampleMilliseconds { get; private set; }
        public int PlanningRequests { get; private set; }
        public int PlanningCompletions { get; private set; }
        public int PlanningCancellations { get; private set; }
        private struct LifeLease { public Vector3 target; public float until; public bool rest,urgent; }
        private readonly Dictionary<int,LifeLease> lifeLeases = new Dictionary<int,LifeLease>();
        private readonly Dictionary<int,float> lifeGreetings = new Dictionary<int,float>();
        private readonly Dictionary<int,float> lifeForage = new Dictionary<int,float>();
        public bool IsLifeForaging(int memberId)=>HasLifeTarget(memberId) && lifeForage.TryGetValue(memberId,out float until) && SimulationTime<until;
        public bool RequestLifeForage(int memberId,float seconds=2f)
        {
            JourneyMember m=FindMember(memberId);
            if(!Live(m) || !HasLifeTarget(m) || IsLifeForaging(memberId) || m.ActualSpeed>.03f || m.animation==null ||
                !m.clips.forage || m.clips.forage==m.clips.idle)return false;
            lifeForage[memberId]=SimulationTime+Mathf.Clamp(seconds,2f,8f);return true;
        }
        public float LifeForageWeight(int memberId)
        {
            JourneyMember m=FindMember(memberId);
            return Live(m) && IsLifeForaging(memberId) && m.animation!=null?m.animation.WeightOf(m.clips.forage):0;
        }
        public void CancelLifeForage(int memberId){lifeForage.Remove(memberId);}
        public void RequestLifeGreeting(int memberId) { lifeGreetings[memberId]=SimulationTime+2f; }
        public void FaceLifePoint(int memberId,Vector3 point,float dt)
        {
            JourneyMember m=FindMember(memberId);
            if(!HasLifeTarget(m)||!Live(m)||m.ActualSpeed>.03f||m.bodyStepPhase>0)return;
            Vector3 delta=point-m.root.position;delta.y=0;if(delta.sqrMagnitude<.01f)return;
            Quaternion rotation=Quaternion.RotateTowards(m.root.rotation,Quaternion.LookRotation(delta),dt*100);
            if(ClearOfMemberBodies(m,m.root.position,rotation) &&
                navigation.CanMoveSupportedBody(m.profile,m.root.position,m.root.rotation,m.root.position,rotation))
                ApplyMovement(m,m.root.position,rotation,dt);
        }

        public bool RequestLifeTarget(int memberId,Vector3 target,float seconds)
        {
            JourneyMember m=FindMember(memberId);
            bool changed=!HasLifeTarget(m) || m.autonomous || (target-lifeLeases[memberId].target).sqrMagnitude>.01f;
            if (!TryLifeTarget(m,target,seconds,false)) return false;
            if(changed)m.intentRevision++;
            m.autonomous=false;
            return true;
        }
        public bool RequestAutonomousTarget(int memberId,Vector3 target,bool rest,bool urgent=false)
        {
            JourneyMember m=FindMember(memberId);
            if(m==null || HasLifeTarget(m) && !m.autonomous)return false;
            // An autonomous trip may leave the current loitering site; its route still needs every normal safety check.
            bool arrived=m.Arrived,accepted=false;m.Arrived=false;
            try { accepted=TryLifeTarget(m,target,2,rest,urgent); }
            finally { if(!accepted)m.Arrived=arrived; }
            if(!accepted)return false;
            m.autonomous=true;return true;
        }
        JourneyMember FindMember(int memberId)
        {
            foreach(JourneyMember member in members)if(member!=null && member.id==memberId)return member;
            return null;
        }
        public bool IsAutonomous(int memberId)=>FindMember(memberId)?.autonomous==true;
        public bool KeepAutonomousTarget(int memberId,bool holdPosition=false)
        {
            JourneyMember m=FindMember(memberId);
            if(!Live(m) || !m.autonomous || !HasLifeTarget(m) || m.Mode!=JourneyTravelMode.Walk ||
                m.bridge!=0 || m.bodyStepPhase>0 || social && social.IsParticipating(m.id))return false;
            LifeLease lease=lifeLeases[m.id];lease.until=SimulationTime+2;
            if(holdPosition){lease.target=m.root.position;lease.rest=false;ClearLifeDetour(m);}
            lifeLeases[m.id]=lease;return true;
        }
        public bool IsSleeping(int memberId)
        {
            JourneyMember m=FindMember(memberId);
            return m!=null && (m.rest.Phase==WildlifeRestPhase.Sleeping ||
                HasLifeTarget(m) && lifeLeases[memberId].rest && m.ActualSpeed<.01f &&
                FlatDistance(m.root.position,lifeLeases[memberId].target)<.25f && !m.clips.HasRestSequence);
        }
        public void ReleaseAutonomousTarget(int memberId)
        {
            JourneyMember m=FindMember(memberId);
            if(m==null || !m.autonomous)return;
            ClearLifeDetour(m);
            lifeLeases.Remove(memberId);m.autonomous=false;m.route=null;m.Arrived=false;m.rejoining=true;
            if(sites!=null && TargetSite>=0 && TargetSite<sites.Length)m.destination=sites[TargetSite].center;
            m.retryAt=SimulationTime;
        }
        private bool TryLifeTarget(JourneyMember m,Vector3 target,float seconds,bool rest,bool urgent=false)
        {
            if(!isActiveAndEnabled || !Live(m) || !navigation || m.Mode!=JourneyTravelMode.Walk || m.bridge!=0 || m.bodyStepPhase>0 ||
                FlatDistance(m.root.position,target)>8f ||
                !navigation.TryStep(m.profile,m.root.position,target,out Vector3 foot,out JourneyTravelMode mode) ||
                mode!=JourneyTravelMode.Walk || !ClearOfOthers(m,foot,endpointOnly:true))return false;
            if(!HasLifeTarget(m) || (lifeLeases[m.id].target-foot).sqrMagnitude>.01f)ClearLifeDetour(m);
            lifeLeases[m.id]=new LifeLease {target=foot,until=SimulationTime+Mathf.Clamp(seconds,.1f,3f),rest=rest,urgent=urgent && !rest};
            return true;
        }
        public void ReleaseLifeTarget(int memberId) { lifeLeases.Remove(memberId);lifeForage.Remove(memberId);
            JourneyMember m=FindMember(memberId);if(m!=null){ClearLifeDetour(m);m.autonomous=false;m.intentRevision++;} }
        void ClearLifeDetour(JourneyMember member)
        {member.crowdPath=null;member.crowdStep=0;member.crowdResume=-1;member.crowdRetryAt=SimulationTime;}
        public bool HasLifeTarget(int memberId) => isActiveAndEnabled && lifeLeases.TryGetValue(memberId,out LifeLease lease) && SimulationTime<lease.until;
        private bool HasLifeTarget(JourneyMember m) => m!=null && HasLifeTarget(m.id);
        private bool UrgentLifeMove(JourneyMember m)=>m.autonomous && HasLifeTarget(m) && lifeLeases[m.id].urgent &&
            !lifeLeases[m.id].rest && m.Mode==JourneyTravelMode.Walk && m.bridge==0;
        private void LifeMove(JourneyMember m,float dt)
        {
            LifeLease lease=lifeLeases[m.id];m.ActualSpeed=0;
            if(IsLifeForaging(m.id)){m.State=JourneyMemberState.Forage;return;}
            if(FlatDistance(m.root.position,lease.target)<.25f)
            { m.State=JourneyMemberState.Idle;m.LastDecision="在附近的生活点等候";return; }
            m.State=JourneyMemberState.Travelling;
            Vector3 target=m.crowdPath!=null && m.crowdStep<m.crowdPath.Count?m.crowdPath[m.crowdStep]:lease.target;
            if(TurnBeforeTravel(m,target,dt))return;
            Vector3 next=Vector3.MoveTowards(m.root.position,target,m.profile.walkSpeed*(UrgentLifeMove(m)?1.6f:1f)*dt);
            if(navigation.TryStep(m.profile,m.root.position,next,out Vector3 foot,out JourneyTravelMode mode) &&
                mode==JourneyTravelMode.Walk && EvaluateBodyMove(m,foot,dt,out BodyMove bodyMove))
            {
                Vector3 checkedPoint=foot;var crowd=m.crowdPath;int crowdStep=m.crowdStep;
                if(AvoidOthers(m,foot,mode,out foot,dt))
                {
                    if(foot==checkedPoint && crowd==m.crowdPath && crowdStep==m.crowdStep)CommitBodyMove(m,bodyMove,dt);
                    else Move(m,foot,dt);
                }
            }
            m.LastDecision=m.ActualSpeed>.02f?"步行到附近的生活点":"通路受阻，等候或重新邀请";
        }

        private sealed class Plan
        {
            public JourneyNavigation navigation;
            public JourneyMember[] sourceMembers, members;
            public JourneyProfile[] profiles;
            public Vector3[] positions, scales;
            public Quaternion[] rotations;
            public JourneyMemberState[] states;
            public JourneyTravelMode[] modes;
            public bool[] live, arrived;
            public int version, seed, site, member = -1;
            public float time, radius;
            public Vector3 center, destination;
            public float spentSwimming;
            public bool success, recovery, finished;
        }
        private Plan pending;
        private IEnumerator planningSteps;
        private double planningDeadline;

        private sealed class Gate
        {
            public readonly List<int> queue = new List<int>();
            public readonly HashSet<int> occupants = new HashSet<int>();
            public Vector3 direction;
            public float nextAdmission;
        }
        private readonly Dictionary<int, Gate> gates = new Dictionary<int, Gate>();
        private readonly List<JourneyCrowdObstacle> crowdObstacles = new List<JourneyCrowdObstacle>();
        private JourneySocialParticipant[] participants;
        private WildlifeRandom random;
        // journeyStarted is the last effective route-progress time; a moving detour renews it.
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
            lifeLeases.Clear();lifeGreetings.Clear();lifeForage.Clear();
            CancelPlanning();
            PlanningRequests = PlanningCompletions = PlanningCancellations = 0;
            MaximumPlanningUnitMilliseconds = MaximumRouteSampleMilliseconds = 0;
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
                m.rest.Reset(); m.autonomous=false; m.rejoining=false; m.intentRevision++;
                m.DistanceTravelled = m.ActualSpeed = m.ContinuousSwimDistance = 0; m.route = null; m.segment = m.bridge = 0; m.bridgeLink = -1;
                m.failures.Clear(); m.routeProgress.Clear(); m.groundCheckAt = 0; m.blockedSince = m.retryAt = m.crowdRetryAt = 0; m.crowdPath = null; m.nextChoice = individual.Range(.5f, 3f);
                m.localTarget = m.root.position;
                m.bodyStepPhase = 0; m.checkedPassage = -1;
                m.bodyStepPartner = null;
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
            foreach (JourneyMember member in members)
                if (Live(member)) PlaceOnInitialSupport(member);
            initialized = navigation && sites.Length > 0;
            RegisterSurroundingBodies();
            GroupDecision = "在出发地自由活动";
        }

        private void PlaceOnInitialSupport(JourneyMember member)
        {
            if (!navigation) return;
            Vector3 from = member.root.position;
            Quaternion rotation = member.root.rotation;
            if (!navigation.TryProject(member.profile, from, rotation, out Vector3 support, out JourneyTravelMode mode) ||
                (mode != JourneyTravelMode.Walk && mode != JourneyTravelMode.Bridge) ||
                Mathf.Abs(support.y - from.y) > navigation.maxStepHeight ||
                !navigation.CanMoveSupportedBody(member.profile, from, rotation, support, rotation) ||
                (navigation.playerCollision && !navigation.playerCollision.AllowsAnimalMove(from, support,
                    member.profile.bodyRadius, member.profile.bodyEnvelope))) return;
            Vector3 delta = Quaternion.Inverse(rotation) * (support - from);
            Bounds sweep = new Bounds(member.profile.bodyEnvelope.center + delta * .5f,
                member.profile.bodyEnvelope.half * 2f + new Vector3(Mathf.Abs(delta.x), Mathf.Abs(delta.y), Mathf.Abs(delta.z)));
            // With unchanged rotation this box contains both endpoints and the entire vertical correction.
            foreach (JourneyMember other in members)
                if (Live(other) && other != member && !JourneySocialGesture.Separated(from, rotation, sweep,
                    other.root.position, other.root.rotation, new Bounds(other.profile.bodyEnvelope.center, other.profile.bodyEnvelope.half * 2f))) return;
            if (surroundingPopulation)
            {
                var others = surroundingPopulation.agents;
                if (others == null || others.Length == 0) others = surroundingPopulation.GetComponentsInChildren<WildlifeAgent>();
                foreach (WildlifeAgent other in others)
                {
                    if (!other || !other.isActiveAndEnabled || other.transform == member.root ||
                        !(other.locomotion == WildlifeLocomotion.Ground ||
                            (other.locomotion == WildlifeLocomotion.Flying && other.AirHeight < .01f))) continue;
                    // Herd starts before ordinary populations; observe their current mesh without initializing them.
                    var body = other.BodyEnvelope.Valid ? other.BodyEnvelope : WildlifeBodyEnvelope.Measure(other.transform, other.Radius);
                    if (!JourneySocialGesture.Separated(from, rotation, sweep,
                        other.transform.position, other.transform.rotation, new Bounds(body.center, body.half * 2f))) return;
                }
            }
            // Scene terrain can change after placement. Resolve support once before movement, without changing authored XZ or heading.
            member.root.position = support; member.localTarget = support; member.Mode = mode;
        }

        public void SetPaused(bool value)
        {
            Paused = value;
            foreach (JourneyMember m in members)
                if (Live(m) && m.animation != null) m.animation.SetPaused(value);
        }

        public bool BeginJourney(int siteIndex)
        {
            CancelPlanning();
            if (!RequestJourney(siteIndex)) return false;
            Plan plan = pending;
            AdvancePlanning(double.PositiveInfinity);
            return plan.success;
        }

        public bool RequestJourney(int siteIndex)
        {
            if (pending != null || !initialized || sites == null || siteIndex < 0 || siteIndex >= sites.Length || sites[siteIndex] == null ||
                (Travelling && HasMemberCrossing()) || Array.Exists(members, m => Live(m) && m.bodyStepPhase > 0)) return false;
            pending = CapturePlan(siteIndex);
            planningSteps = PlanJourneySteps(pending);
            return true;
        }

        private Plan CapturePlan(int siteIndex, int memberIndex = -1)
        {
            PlanningRequests++;
            int count = members.Length;
            var plan = new Plan { navigation = navigation, version = navigation.Version, sourceMembers = members,
                members = (JourneyMember[])members.Clone(), profiles = new JourneyProfile[count], positions = new Vector3[count],
                rotations = new Quaternion[count], scales = new Vector3[count], states = new JourneyMemberState[count],
                modes = new JourneyTravelMode[count], live = new bool[count], arrived = new bool[count], seed = seed,
                site = siteIndex, member = memberIndex, time = SimulationTime,
                center = sites[siteIndex].center, radius = sites[siteIndex].radius };
            for (int i = 0; i < count; i++)
            {
                JourneyMember m = members[i]; plan.live[i] = Live(m) && (memberIndex >= 0 || !HasLifeTarget(m));
                if (!plan.live[i]) continue;
                RefreshBody(m); plan.profiles[i] = JourneyNavigation.CopyProfile(m.profile);
                plan.positions[i] = m.root.position; plan.rotations[i] = m.root.rotation; plan.scales[i] = m.root.lossyScale;
                plan.states[i] = m.State; plan.modes[i] = m.Mode; plan.arrived[i] = m.Arrived;
            }
            if (memberIndex >= 0) { plan.destination = members[memberIndex].destination; plan.spentSwimming = members[memberIndex].ContinuousSwimDistance; }
            return plan;
        }

        private bool PlanUnchanged(Plan plan)
        {
            if (!navigation || navigation != plan.navigation || navigation.Version != plan.version || members != plan.sourceMembers ||
                seed != plan.seed || sites == null || plan.site >= sites.Length || sites[plan.site] == null ||
                !sites[plan.site].center.Equals(plan.center) || sites[plan.site].radius != plan.radius) return false;
            if (plan.member >= 0 && (members[plan.member] == null || !members[plan.member].destination.Equals(plan.destination) ||
                members[plan.member].ContinuousSwimDistance != plan.spentSwimming)) return false;
            for (int i = 0; i < members.Length; i++)
            {
                if (plan.member >= 0 && i != plan.member) continue;
                JourneyMember m = members[i];
                if (m != plan.members[i] || (Live(m) && (plan.member >= 0 || !HasLifeTarget(m))) != plan.live[i]) return false;
                if (!plan.live[i]) continue;
                if (!JourneyNavigation.SameProfile(m.profile, plan.profiles[i], plan.member >= 0) ||
                    (plan.member >= 0 && (!m.root.position.Equals(plan.positions[i]) || !m.root.rotation.Equals(plan.rotations[i]) ||
                        m.State != plan.states[i] || m.Arrived != plan.arrived[i])) || !m.root.lossyScale.Equals(plan.scales[i]) ||
                    m.Mode != plan.modes[i] || m.bodyStepPhase > 0 ||
                    (social && social.IsParticipating(m.id))) return false;
                // Release the planning hold immediately if the player approaches the body's personal space.
                if (PlayerNeedsSpace(m)) return false;
            }
            return true;
        }

        public void CancelPlanning()
        {
            if (pending != null && !pending.finished) PlanningCancellations++;
            (planningSteps as IDisposable)?.Dispose(); planningSteps = null; pending = null;
        }

        private static double PlanningClock => System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        private double RemainingBudget => Math.Max(0, planningDeadline - PlanningClock);
        private void AdvancePlanning(double budget)
        {
            LastPlanningMilliseconds = 0;
            if (pending == null) return;
            double started = PlanningClock;
            planningDeadline = started + budget;
            try
            {
                // Variation and final verification use the same live surface/deck snapshot as search.
                // It is scoped to this uninterrupted slice and is always discarded before the next frame.
                navigation.PlanWithSurfaceSnapshot(() =>
                {
                    if (!PlanUnchanged(pending)) { CancelPlanning(); return false; }
                    do
                    {
                        double unitStarted = PlanningClock;
                        bool more = planningSteps.MoveNext();
                        MaximumPlanningUnitMilliseconds = Math.Max(MaximumPlanningUnitMilliseconds, PlanningClock - unitStarted);
                        if (!more)
                        {
                            Plan finished = pending;
                            finished.finished = true; PlanningCompletions++;
                            CancelPlanning();
                            if (!finished.success && finished.member < 0)
                            {
                                GroupDecision = "目的地容不下全群或并非每位成员可达，继续寻找可会合区域";
                                stayUntil = SimulationTime + 5f;
                                if (finished.recovery) journeyStarted = SimulationTime;
                            }
                            return false;
                        }
                    } while (RemainingBudget > 0);
                    return true;
                });
            }
            catch { CancelPlanning(); throw; }
            finally { LastPlanningMilliseconds = PlanningClock - started; }
        }

        private IEnumerator PlanJourneySteps(Plan plan)
        {
            int siteIndex = plan.site;
            if (!initialized || siteIndex < 0 || siteIndex >= sites.Length) yield break;
            if (Travelling && HasMemberCrossing()) yield break;
            var routes = new JourneyRoute[members.Length];
            var destinations = new Vector3[members.Length];
            int liveCount = 0; float spacing = .1f, bodyRadius = 0; JourneyProfile widest = null;
            for (int i = 0; i < members.Length; i++) if (plan.live[i])
            {
                JourneyMember member = members[i];
                RefreshBody(member);
                liveCount++; spacing = Mathf.Max(spacing, member.bodyLength + .18f);
                bodyRadius = Mathf.Max(bodyRadius, member.profile.bodyRadius, member.bodyLength * .5f);
                if (widest == null || member.profile.bodyRadius > widest.bodyRadius) widest = member.profile;
            }
            if (liveCount == 0) yield break;
            bool planned = false;
            // Choose a shared destination and nearby safe starts; distant corridors remain intentions.
            for (int attempt = 0; attempt < 4 && !planned; attempt++)
            {
                int placementSeed = unchecked(plan.seed + siteIndex * 73856093 + (int)(plan.time * 1000) + attempt * 19349663);
                Vector3[] goals = null;
                IEnumerator operation = JourneyScatter.CreateSteps(plan.center, plan.radius, spacing,
                    liveCount, placementSeed, value => goals = value,
                    point => navigation.TryProject(widest, point, out Vector3 ground, out JourneyTravelMode groundMode) &&
                        groundMode == JourneyTravelMode.Walk && ClearSurroundingPlace(ground, bodyRadius, widest.bodyEnvelope));
                while (operation.MoveNext()) yield return null;
                if (goals == null) continue;
                int selected = 0; planned = true;
                // The widest radius does not cover every member's heading or body shape. Reject the
                // whole placement before doing long route searches for its earlier members.
                for (int i = 0; i < members.Length; i++)
                {
                    JourneyMember m = members[i];
                    if (!plan.live[i]) continue;
                    Vector3 goal = goals[selected++];
                    if (!navigation.TryProject(m.profile, goal, out goal, out JourneyTravelMode mode) || mode != JourneyTravelMode.Walk ||
                        !ClearSurroundingPlace(goal, Mathf.Max(m.profile.bodyRadius, m.bodyLength * .5f), m.profile.bodyEnvelope))
                    { planned = false; break; }
                    destinations[i] = goal;
                    yield return null;
                }
                if (!planned) continue;
                for (int i = 0; i < members.Length; i++)
                {
                    JourneyMember m = members[i];
                    if (!plan.live[i]) continue;
                    Vector3 goal = destinations[i];
                    using (JourneyNavigation.RouteSearch search = navigation.CreateIntentSearch(plan.positions[i], goal, plan.profiles[i]))
                    {
                        while (search.Status == JourneyNavigation.SearchStatus.Pending)
                        { search.Advance(RemainingBudget); yield return null; }
                        MaximumRouteSampleMilliseconds = Math.Max(MaximumRouteSampleMilliseconds, search.MaximumWorkUnitMilliseconds);
                        routes[i] = search.Result;
                    }
                    if (routes[i] == null) { planned = false; break; }
                    operation = JourneyRouteVariation.ApplySteps(navigation, m.profile, routes[i], (m.phase - .5f) * 2f * travelSpread,
                        0f, unchecked(plan.seed * 397 ^ m.id), true);
                    while (operation.MoveNext()) yield return null;
                }
            }
            if (!planned) yield break;
            // Keep departure atomic, but a temporary animal at a distant goal must not hold the whole herd.
            for (int i = 0; i < members.Length; i++)
            {
                JourneyMember m = members[i]; if (!plan.live[i]) continue;
                bool clear = false;
                IEnumerator operation = navigation.ValidateNearbySteps(m.profile, routes[i], m.root.position, 0, routes[i].segments.Count - 1, 1f, 0, value => clear = value);
                while (operation.MoveNext()) yield return null;
                if (!clear) yield break;
            }
            if (!PlanUnchanged(plan)) yield break;
            for (int i = 0; i < members.Length; i++)
                if (plan.live[i] && !CanStartRoute(members[i], routes[i])) yield break;
            previousSite = TargetSite; TargetSite = siteIndex;
            Travelling = true; journeyStarted = SimulationTime; gates.Clear();
            for (int i = 0; i < members.Length; i++)
            {
                JourneyMember m = members[i];
                if (!plan.live[i]) continue;
                m.destination = destinations[i]; m.route = routes[i]; m.segment = 0; m.checkedPassage = -1;
                m.progressDestination = m.destination; m.routeProgress.Clear(); m.newRouteProgress = 0;
                m.routeProgress[-1] = -FlatDistance(m.root.position, m.progressDestination);
                m.Arrived = false; m.bridge = 0; m.bridgeLink = -1;
                m.failures.Clear(); m.groundCheckAt = 0; m.blockedSince = m.crowdRetryAt = 0; m.crowdPath = null;
                m.nextChoice = SimulationTime + random.Range(0f, 2.4f);
                m.State = JourneyMemberState.Travelling;
                m.LastDecision = m.profile.canSwim ? "比较水路与陆路后出发" : "沿可步行路线前往会合点";
            }
            GroupDecision = "前往 " + sites[siteIndex].name + "；允许分队，会合后再继续";
            if (plan.recovery) { recovery = true; GroupDecision = "重新选择全员可达的会合地"; }
            plan.success = true;
        }

        public void Simulate(float deltaTime)
        {
            if (!initialized || Paused || deltaTime <= 0) return;
            float dt = Mathf.Min(deltaTime, .1f);
            foreach (JourneyMember member in members) if (Live(member)) RefreshBody(member);
            SimulationTime += dt;
            RestoreBodyLanguage();
            if (pending != null && !PlanUnchanged(pending))
            { CancelPlanning(); stayUntil = Mathf.Max(stayUntil, SimulationTime + decisionInterval); }
            if (SimulationTime >= decisionAt)
            {
                decisionAt = SimulationTime + Mathf.Max(.1f, decisionInterval);
                Decide();
            }
            AdvancePlanning(Mathf.Clamp(planningBudgetMilliseconds, .1f, 8f));
            for (int i = 0; i < members.Length; i++)
            {
                JourneyMember m = members[i];
                if (!Live(m)) continue;
                if(!HasLifeTarget(m) && lifeLeases.Remove(m.id))ClearLifeDetour(m);
                m.hunger = Mathf.Clamp01(m.hunger + dt * (m.State == JourneyMemberState.Forage ? -.045f : .004f));
                m.fatigue = Mathf.Clamp01(m.fatigue + dt * (m.ActualSpeed > .1f ? .007f : -.012f));
                bool sleeping=HasLifeTarget(m) && lifeLeases[m.id].rest && FlatDistance(m.root.position,lifeLeases[m.id].target)<.25f;
                m.rest.Tick(sleeping,false,dt,m.clips,m.animation==null || m.animation.HasReachedBoundary(m.rest.Clip,m.rest.Speed));
                if (m.bodyStepPhase > 0) AdvanceBodyStep(m, dt);
                else if(m.rest.BlocksMovement)m.ActualSpeed=0;
                else if (HasLifeTarget(m)) LifeMove(m,dt);
                // Group planning is speculative. Only a local safety check holds its own member.
                else if (pending != null && pending.member >= 0 && members[pending.member] == m) m.ActualSpeed = 0;
                else if (social && social.IsParticipating(m.id)) SocialMove(m, dt);
                else if ((Travelling || m.rejoining) && !m.Arrived) Travel(m, dt);
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
            if(HasLifeTarget(member))return false;
            if (pending != null && (pending.member < 0 || members[pending.member] == member)) return false;
            if (!Live(member) || member.sociability <= 0 || member.bridge != 0 || member.bodyStepPhase > 0 ||
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
            if (pending != null) return;
            foreach (Gate gate in gates.Values)
            {
                gate.queue.RemoveAll(id => !IsLiveId(id));
                gate.occupants.RemoveWhere(id => !IsLiveId(id));
            }
            foreach (JourneyMember member in members)
                if (member != null && !Live(member)) { member.bridge = 0; member.bridgeLink = -1; }
            bool allArrived = true;
            foreach (JourneyMember m in members) if (Live(m) && !HasLifeTarget(m) && !m.Arrived) allArrived = false;
            if (Travelling && allArrived)
            {
                Travelling = false; recovery = false; CompletedJourneys++;
                stayUntil = SimulationTime + minimumStay + random.Range(0, 8f);
                GroupDecision = "全员会合，在 " + sites[TargetSite].name + " 吃草和交流";
            }
            if (Travelling && SimulationTime - journeyStarted > rendezvousTimeout)
            {
                // Recover after no new route progress, rather than timing out a long, still-moving detour.
                if (HasMemberCrossing())
                    GroupDecision = "让水中和桥上的同伴先到安全岸边，再调整会合点";
                else if (!recovery)
                {
                    int fallback = previousSite;
                    if (RequestJourney(fallback)) pending.recovery = true;
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
            if (best >= 0 && !RequestJourney(best)) stayUntil = SimulationTime + 5f;
        }

        private void Travel(JourneyMember m, float dt)
        {
            m.ActualSpeed = 0;
            if (m.failureNavigationVersion != navigation.Version)
            {
                m.failures.Clear(); m.failureNavigationVersion = navigation.Version;
                m.routeProgress.TryGetValue(-1, out float connectorProgress);
                m.routeProgress.Clear(); m.routeProgress[-1] = connectorProgress;
            }
            if (SimulationTime < m.nextChoice) return;
            if (m.route != null && m.route.navigationVersion != navigation.Version) { ReleaseBridge(m); m.route = null; }
            if (m.route == null)
            {
                m.State = JourneyMemberState.Regrouping;
                if (SimulationTime < m.retryAt || pending != null) return;
                m.crowdPath = null;
                pending = CapturePlan(TargetSite, Array.IndexOf(members, m));
                planningSteps = ReplanSteps(pending);
                return;
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
            if (!CheckUpcomingPassage(m)) return;
            if (!CheckUpcomingGround(m)) return;
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
                        AvoidOthers(m, retreat, retreatMode, out retreat, dt)) Move(m, retreat, dt);
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
            // The route validates a turn at the waypoint before entering its next corridor.
            // Finish large heading changes here; sideways translation can otherwise use up
            // the bridge-mouth turning room while the animal is still facing backwards.
            if (TurnBeforeTravel(m, target, dt)) { m.blockedSince = 0; return; }
            Vector3 next = Vector3.MoveTowards(m.root.position, target, speed * dt);
            bool safeStep = navigation.TryStep(m.profile, m.root.position, next, out Vector3 projected, out JourneyTravelMode mode);
            if (!safeStep && TryStartSupportedRouteStep(m, segment, dt)) { m.blockedSince = 0; return; }
            BodyMove evaluated=default;
            if (!safeStep || !EvaluateBodyMove(m, projected, dt, out evaluated) ||
                (mode == JourneyTravelMode.Bridge && (segment.bridgeId == 0 || m.checkedPassage < m.segment ||
                    navigation.SampleBodySurface(m.profile, projected).bridgeId != segment.bridgeId)) ||
                ((mode == JourneyTravelMode.Swim || m.Mode == JourneyTravelMode.Swim) &&
                    (m.ContinuousSwimDistance + FlatDistance(m.root.position, projected) > m.profile.maxSwimDistance + .0001f ||
                     (mode == JourneyTravelMode.Swim && (segment.mode != JourneyTravelMode.Swim || m.checkedPassage < m.segment)))))
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
            Vector3 checkedPoint=projected;var checkedCrowd=m.crowdPath;int checkedCrowdStep=m.crowdStep;
            JourneyTravelMode checkedMode=m.Mode;
            if (!AvoidOthers(m, projected, mode, out projected, dt))
            {
                if (m.bridge != 0 || UpcomingBridge(m)) m.State = JourneyMemberState.WaitingForBridge;
                m.LastDecision = "留出身体间距，等前方同伴走开"; return;
            }
            m.blockedSince = 0; m.Mode = mode;
            if(projected==checkedPoint && checkedMode==mode && checkedCrowd==m.crowdPath && checkedCrowdStep==m.crowdStep)
                CommitBodyMove(m,evaluated,dt);
            else Move(m, projected, dt);
            m.LastDecision = mode == JourneyTravelMode.Swim ? "保持水面浮泳，前往可上岸处" :
                mode == JourneyTravelMode.Wade ? "脚踩浅水底缓慢涉水" : m.bridge != 0 ? "沿桥面依次通过" : "沿自己的路线跟进";
        }

        private IEnumerator ReplanSteps(Plan plan)
        {
            JourneyMember m = members[plan.member];
            var blocked = new HashSet<int>();
            foreach (var pair in m.failures) if (pair.Value > plan.time) blocked.Add(pair.Key);
            JourneyRoute route;
            using (JourneyNavigation.RouteSearch search = navigation.CreateIntentSearch(plan.positions[plan.member], plan.destination, m.profile, blocked, plan.spentSwimming))
            {
                while (search.Status == JourneyNavigation.SearchStatus.Pending)
                { search.Advance(RemainingBudget); yield return null; }
                MaximumRouteSampleMilliseconds = Math.Max(MaximumRouteSampleMilliseconds, search.MaximumWorkUnitMilliseconds);
                route = search.Result;
            }
            if (route != null)
            {
                IEnumerator operation = JourneyRouteVariation.ApplySteps(navigation, m.profile, route,
                    (m.phase - .5f) * 2f * travelSpread, m.ContinuousSwimDistance, unchecked(plan.seed * 397 ^ m.id), true);
                while (operation.MoveNext()) yield return null;
                bool clear = false;
                operation = navigation.ValidateNearbySteps(m.profile, route, m.root.position, 0, route.segments.Count - 1,
                    1f, m.ContinuousSwimDistance, value => clear = value);
                while (operation.MoveNext()) yield return null;
                if (!clear) route = null;
            }
            if (!PlanUnchanged(plan)) yield break;
            if (route != null && !CanStartRoute(m, route)) route = null;
            m.route = route; m.retryAt = SimulationTime + 3f; m.segment = 0; m.checkedPassage = -1; m.groundCheckAt = 0;
            if (route == null) { m.LastDecision = "记住受阻路段，等待或寻找其他通路"; yield break; }
            RouteChanges++; m.LastDecision = "找到新的可行路线"; plan.success = true;
        }

        private bool PlayerNeedsSpace(JourneyMember member)
        {
            return navigation.playerCollision && navigation.playerCollision.TryObstacle(member.root.position, member.profile.bodyRadius,
                out Vector3 center, out float radius, member.profile.bodyEnvelope) &&
                FlatDistance(center, member.root.position) < member.profile.bodyEnvelope.ContactRadius(member.profile.bodyRadius) + radius + .45f;
        }

        private bool CheckUpcomingGround(JourneyMember member)
        {
            if (member.Mode != JourneyTravelMode.Walk || member.DistanceTravelled < member.groundCheckAt || PlayerNeedsSpace(member)) return true;
            float distance = Mathf.Max(1.5f, member.bodyLength + 1f), span = 0;
            JourneyRoute preview = member.route;
            int first = member.segment;
            if (member.crowdPath != null)
            {
                // Check the path being executed, including its turn back onto the original route.
                preview = new JourneyRoute { navigationVersion = navigation.Version }; first = 0;
                Vector3 from = member.root.position;
                int cursor = member.crowdStep;
                while (cursor < member.crowdPath.Count && FlatDistance(from, member.crowdPath[cursor]) < .12f) cursor++;
                for (int i = cursor; i < member.crowdPath.Count && span < distance; i++)
                {
                    Vector3 to = member.crowdPath[i];
                    preview.segments.Add(new JourneySegment { from = from, to = to, mode = JourneyTravelMode.Walk, linkIndex = -1 });
                    span += FlatDistance(from, to); from = to;
                }
                for (int i = Mathf.Max(member.segment, member.crowdResume); i < member.route.segments.Count && span < distance; i++)
                {
                    JourneySegment part = member.route.segments[i];
                    if (part.mode != JourneyTravelMode.Walk || part.bridgeId != 0 || part.surfaceBoundary) break;
                    part.from = from; preview.segments.Add(part); span += FlatDistance(from, part.to); from = part.to;
                }
            }
            span = 0;
            int end = first - 1;
            Vector3 previous = member.root.position;
            for (int i = first; i < preview.segments.Count; i++)
            {
                JourneySegment part = preview.segments[i];
                if (part.mode != JourneyTravelMode.Walk || part.bridgeId != 0 || part.surfaceBoundary) break;
                end = i; span += FlatDistance(previous, part.to); previous = part.to;
                if (span >= distance) break;
            }
            if (end < first) return true;
            if (pending != null) return pending.member < 0;
            pending = CapturePlan(TargetSite, Array.IndexOf(members, member));
            planningSteps = GroundSteps(pending, preview, first, end, distance);
            member.LastDecision = "看看前方的路，留出绕开障碍的空间";
            return false;
        }

        private IEnumerator GroundSteps(Plan plan, JourneyRoute preview, int first, int end, float distance)
        {
            JourneyMember member = members[plan.member]; JourneyRoute route = member.route;
            List<Vector3> crowd = member.crowdPath;
            int crowdStep = member.crowdStep, crowdResume = member.crowdResume;
            return Check();
            IEnumerator Check()
            {
                bool clear = false;
                IEnumerator operation = navigation.ValidateNearbySteps(member.profile, preview, member.root.position, first,
                    end, distance, member.ContinuousSwimDistance, value => clear = value);
                while (operation.MoveNext()) yield return null;
                if (!PlanUnchanged(plan) || member.route != route || member.crowdPath != crowd ||
                    member.crowdStep != crowdStep || member.crowdResume != crowdResume) yield break;
                if (clear) { member.groundCheckAt = member.DistanceTravelled + .5f; plan.success = true; yield break; }
                // A far intention or crowd shortcut can cross a prop. Reuse strict local attachments while there is turning room.
                member.route = null; member.crowdPath = null; member.retryAt = SimulationTime;
                member.LastDecision = "前面有障碍，提前寻找绕行的路";
            }
        }

        private bool CheckUpcomingPassage(JourneyMember member)
        {
            JourneyProfile checkedProfile = member.passageProfile, current = member.profile;
            if (checkedProfile == null || checkedProfile.canSwim != current.canSwim || checkedProfile.wadeDepth != current.wadeDepth ||
                checkedProfile.maxSwimDistance != current.maxSwimDistance || checkedProfile.swimSpeed != current.swimSpeed ||
                checkedProfile.waterlineHeight != current.waterlineHeight || checkedProfile.bodyRadius != current.bodyRadius ||
                !checkedProfile.bodyEnvelope.Equals(current.bodyEnvelope)) member.checkedPassage = -1;
            var parts = member.route.segments;
            int first = -1, end = -1;
            for (int i = member.segment; i < parts.Count; i++)
            {
                if (FlatDistance(member.root.position, parts[i].from) > 2f) break;
                if (i > member.checkedPassage && (parts[i].mode == JourneyTravelMode.Swim || parts[i].bridgeId > 0)) { first = i; break; }
            }
            if (first < 0) return true;
            for (end = first; end + 1 < parts.Count; end++)
                if (parts[end].mode == JourneyTravelMode.Walk && parts[end].bridgeId == 0 && !parts[end].surfaceBoundary) break;
            if (pending != null) return false;
            pending = CapturePlan(TargetSite, Array.IndexOf(members, member));
            planningSteps = PassageSteps(pending, end);
            member.LastDecision = "在岸边或桥外确认整段通路，再让同伴依次通过";
            return false;
        }

        private IEnumerator PassageSteps(Plan plan, int end)
        {
            JourneyMember member = members[plan.member];
            JourneyRoute route = member.route;
            bool clear = false, persistent = false; int failedLink = -1;
            IEnumerator operation = navigation.ValidatePassageSteps(member.profile, route, member.root.position, member.segment,
                end, member.ContinuousSwimDistance, (valid, stationary, link) => { clear = valid; persistent = stationary; failedLink = link; });
            while (operation.MoveNext()) yield return null;
            if (!PlanUnchanged(plan) || member.route != route) yield break;
            if (clear) { member.checkedPassage = end; member.passageProfile = JourneyNavigation.CopyProfile(member.profile); plan.success = true; yield break; }
            // A personally checked passage stays blocked for this journey. Long detours must not
            // forget a static obstruction; a new destination or navigation rebuild permits a retry.
            // Animal/Rigidbody blockers and unknown full query buffers keep the short retry window.
            member.failureNavigationVersion = navigation.Version;
            // Keep shared approach/exit links usable; an unowned connector cannot condemn a different link.
            if (failedLink >= 0) member.failures[failedLink] = persistent ? float.PositiveInfinity : SimulationTime + 12f;
            ReleaseBridge(member); member.route = null; member.retryAt = SimulationTime + .5f;
            member.LastDecision = "前方通路受阻，留在岸边寻找另一条路";
        }

        private bool CanStartRoute(JourneyMember member, JourneyRoute route)
        {
            if (route == null) return false;
            if (route.segments.Count == 0) return true;
            JourneySegment first = route.segments[0];
            Quaternion heading = WildlifeBodyEnvelope.Heading(first.to - first.from, member.root.rotation);
            return (navigation.CanMoveSupportedBody(member.profile, member.root.position, member.root.rotation, first.from, heading) ||
                navigation.TryPlanBodyStep(member.profile, member.root.position, member.root.rotation, first.from, heading, out _)) &&
                navigation.TryStep(member.profile, member.root.position, first.to, out _, out _);
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
                    AvoidOthers(m, next, mode, out next, dt)) Move(m, next, dt);
                else if (m.crowdPath == null) m.localTarget = m.root.position;
            }
            m.Mode = JourneyTravelMode.Walk;
        }

        private void SocialMove(JourneyMember m, float dt)
        {
            m.State = JourneyMemberState.Social; m.ActualSpeed = 0; m.localTarget = m.root.position;
            int otherId = social.FirstParticipantId == m.id ? social.SecondParticipantId : social.FirstParticipantId;
            JourneyMember partner = FindMember(otherId);
            if (!Live(partner)) return;
            Vector3 direction = partner.root.position - m.root.position; direction.y = 0;
            float targetSpace = (m.bodyLength + partner.bodyLength) * .5f + (social.CurrentIntent == "greeting" || social.CurrentIntent == "you" ? .25f : .04f);
            if (direction.magnitude > targetSpace)
            {
                Vector3 next = m.root.position + direction.normalized * Mathf.Min(.35f * dt, direction.magnitude - targetSpace);
                if (navigation.TryStep(m.profile, m.root.position, next, out next, out JourneyTravelMode mode, JourneyTravelMode.Walk) && mode == JourneyTravelMode.Walk &&
                    ClearOfOthers(m, next, partner)) Move(m, next, dt, partner);
            }
            if (direction.sqrMagnitude > .001f)
            {
                Quaternion facing = Quaternion.LookRotation(direction);
                var conversation = social.ActiveConversation;
                if (conversation != null && social.CurrentIntent == "greeting")
                {
                    var pose = JourneySocialGesture.Sample("greeting", SimulationTime - conversation.startedAt,
                        conversation.endsAt - conversation.startedAt, conversation.speakerId == m.id, m.id % 2 == 0 ? -1 : 1);
                    facing *= Quaternion.Euler(0, -pose.angles.y, 0);
                }
                Quaternion rotation = Quaternion.RotateTowards(m.root.rotation, facing, dt * 100f);
                if (CanSocialPose(m, rotation) && navigation.CanMoveBody(m.profile, m.root.position, m.root.rotation, m.root.position, rotation)) m.root.rotation = rotation;
            }
            m.LastDecision = "注意交流对象，靠近并聆听";
        }

        private bool AvoidOthers(JourneyMember m, Vector3 desired, JourneyTravelMode mode, out Vector3 result, float dt)
        {
            result = desired;
            bool lifeTarget=HasLifeTarget(m);
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
                    nextMode == JourneyTravelMode.Walk && ClearOfOthers(m, next) &&
                    ClearOfMemberBodies(m, next, MovementRotation(m, next, dt))) { result = next; return true; }
                m.crowdPath = null;
            }
            if (ClearOfOthers(m, desired, null, mode) && ClearOfMemberBodies(m, desired, MovementRotation(m, desired, dt))) return true;
            if (!lifeTarget && mode == JourneyTravelMode.Walk && SimulationTime >= m.crowdRetryAt && TryYieldAtBridge(m))
            { m.crowdRetryAt = SimulationTime + .7f; return false; }
            // Narrow crossings and swimmers retain their queue; open land can plan around a neighbour.
            if (m.bridge != 0 || mode != JourneyTravelMode.Walk || (!lifeTarget && UpcomingBridge(m, 3f) && FollowingSameRoute(m)))
            {
                if (SimulationTime >= m.crowdRetryAt) m.crowdRetryAt = SimulationTime + .7f;
                return false;
            }
            if (SimulationTime < m.crowdRetryAt) return false;
            if (!AnimalWorkBudget.TryBegin(AnimalWorkBudget.Kind.Planning,out var crowdWork)) return false;
            using var crowdScope = crowdWork;
            m.crowdRetryAt = SimulationTime + .6f + m.phase * .2f;
            Vector3 target = lifeTarget?lifeLeases[m.id].target:m.localTarget;
            int resume = -1;
            if (!lifeTarget && !m.Arrived && m.route != null)
            {
                bool remainingWalk = true;
                for (int i = m.segment; i < m.route.segments.Count; i++)
                {
                    JourneySegment candidate = m.route.segments[i];
                    if (candidate.mode != JourneyTravelMode.Walk || candidate.bridgeId != 0) { remainingWalk = false; break; }
                    if (FlatDistance(m.root.position, candidate.to) <= 6f &&
                        navigation.TryProject(m.profile, candidate.to, out Vector3 foot, out JourneyTravelMode ground) && ground == JourneyTravelMode.Walk &&
                        ClearOfOthers(m, foot, endpointOnly: true))
                    { target = foot; resume = i; }
                }
                if (remainingWalk && FlatDistance(m.root.position, m.destination) <= 8f &&
                    navigation.TryProject(m.profile, m.destination, out Vector3 destinationFoot, out JourneyTravelMode destinationMode) && destinationMode == JourneyTravelMode.Walk &&
                    ClearOfOthers(m, destinationFoot, endpointOnly: true))
                { target = destinationFoot; resume = m.route.segments.Count - 1; }
                if (remainingWalk && FlatDistance(m.root.position, m.destination) <= 6f &&
                    (!ClearSurroundingPlace(m.destination, Mathf.Max(m.profile.bodyRadius, m.bodyLength * .5f), m.profile.bodyEnvelope) || RestingMemberOccupiesGoal(m)) &&
                    ChooseNearbyRestingPlace(m)) return false;
                if (resume < 0) { RequestRoom(m); return false; }
            }
            if (FlatDistance(m.root.position, target) < .2f) return false;
            CollectCrowdObstacles(m);
            if (!JourneyCrowdPath.TryFindPath(m.root.position, target, Mathf.Max(m.profile.bodyRadius, m.bodyLength * .5f), crowdObstacles,
                (from, to) => SafeCrowdStep(m, from, to), out List<Vector3> path)) { RequestRoom(m); return false; }
            m.crowdPath = path; m.crowdStep = 1; m.crowdResume = resume; m.groundCheckAt = 0;
            return false;
        }

        private void CollectCrowdObstacles(JourneyMember m)
        {
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
        }

        private bool TryYieldAtBridge(JourneyMember member)
        {
            // An admitted crosser keeps priority through the final boundary; only bridge entrants side-yield.
            if (member.Arrived || member.bridge != 0 || member.Mode != JourneyTravelMode.Walk || !UpcomingBridge(member, 3f) ||
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
                member.crowdStep = 1; member.crowdResume = member.segment; member.groundCheckAt = 0;
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

        private bool RestingMemberOccupiesGoal(JourneyMember member)
        {
            float radius = Mathf.Max(member.profile.bodyRadius, member.bodyLength * .5f);
            foreach (JourneyMember other in members)
                if (Live(other) && other != member && other.Arrived &&
                    Mathf.Abs(other.root.position.y - member.destination.y) <= .65f &&
                    FlatDistance(other.root.position, member.destination) < radius + Mathf.Max(other.profile.bodyRadius, other.bodyLength * .5f) + .12f)
                    return true;
            return false;
        }
        private bool ChooseNearbyRestingPlace(JourneyMember member)
        {
            Vector3 toward = member.root.position - member.destination; toward.y = 0;
            if (toward.sqrMagnitude < .001f) toward = Vector3.forward;
            float radius = Mathf.Max(member.profile.bodyRadius, member.bodyLength * .5f);
            // A resting neighbour may occupy a reserved goal after departure. Pick another nearby patch in the same site.
            CollectCrowdObstacles(member);
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
                    // A vacant endpoint still needs a way around the animals already resting in front of it.
                    if (!JourneyCrowdPath.TryFindPath(member.root.position, candidate, radius, crowdObstacles,
                        (from, to) => SafeCrowdStep(member, from, to), out List<Vector3> path)) continue;
                    member.destination = candidate; member.route = route; member.segment = 0;
                    member.crowdPath = path; member.crowdStep = 1; member.crowdResume = route.segments.Count - 1; member.groundCheckAt = 0;
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

        private bool ClearOfMemberBodies(JourneyMember member, Vector3 next, Quaternion rotation)
        {
            // Radius spacing alone does not cover the corners swept by a turning body.
            foreach (JourneyMember other in members)
                if (other != member && Live(other) && !WildlifeBodyPhysics.ClearOfBody(member.profile.bodyEnvelope,
                    member.root.position, member.root.rotation, next, rotation,
                    other.profile.bodyEnvelope, other.root.position, other.root.rotation)) return false;
            return true;
        }
        private Quaternion MovementRotation(JourneyMember member, Vector3 next, float dt)
        {
            Quaternion rotation = Quaternion.RotateTowards(member.root.rotation,
                WildlifeBodyEnvelope.Heading(next - member.root.position, member.root.rotation), dt * 130f);
            if (member.crowdPath == null || member.crowdStep >= member.crowdPath.Count || member.bridge != 0 ||
                member.Mode != JourneyTravelMode.Walk || member.State == JourneyMemberState.Social ||
                ClearOfMemberBodies(member, next, rotation)) return rotation;
            // An existing crowd detour may first need a small translation to make room for its turn.
            if (navigation.TryProject(member.profile, next, member.root.rotation, out Vector3 foot, out JourneyTravelMode mode) &&
                mode == JourneyTravelMode.Walk && ClearOfMemberBodies(member, foot, member.root.rotation) &&
                navigation.CanMoveSupportedBody(member.profile, member.root.position, member.root.rotation, foot, member.root.rotation))
                return member.root.rotation;
            return rotation;
        }
        private struct BodyMove
        {
            public Vector3 next;
            public Quaternion rotation;
            public bool supported;
            public JourneyBodyStep step;
        }
        private bool EvaluateBodyMove(JourneyMember m, Vector3 next, float dt, out BodyMove move)
        {
            move=new BodyMove {rotation=MovementRotation(m,next,dt)};
            if (!navigation.TryProject(m.profile, next, move.rotation, out move.next, out _)) return false;
            move.supported=navigation.CanMoveSupportedBody(m.profile,m.root.position,m.root.rotation,move.next,move.rotation);
            return move.supported || navigation.TryPlanBodyStep(m.profile,m.root.position,m.root.rotation,move.next,move.rotation,out move.step);
        }

        private bool TryStartSupportedRouteStep(JourneyMember m, JourneySegment segment, float dt)
        {
            // A frame-sized request may land below a deck lip. Keep the existing step in flight
            // to the nearby route support; actual lift/across/landing still advances at the normal speed.
            if (m.crowdPath != null || m.Mode != JourneyTravelMode.Walk || segment.mode != JourneyTravelMode.Walk ||
                segment.bridgeId != 0 || segment.surfaceBoundary || FlatDistance(m.root.position, segment.to) > navigation.sampleSpacing)
                return false;
            Quaternion heading = WildlifeBodyEnvelope.Heading(segment.to - m.root.position, m.root.rotation);
            if (Quaternion.Angle(m.root.rotation, heading) > dt * 130f ||
                !navigation.TryStep(m.profile, m.root.position, segment.to, out Vector3 landing, out _, JourneyTravelMode.Walk) ||
                !ClearOfOthers(m, landing) ||
                !navigation.TryPlanBodyStep(m.profile, m.root.position, m.root.rotation, landing, heading, out JourneyBodyStep step) ||
                step.lift.y <= m.root.position.y + .001f) return false;
            m.bodyStep = step; m.bodyStepPartner = null; m.bodyStepPhase = 1;
            AdvanceBodyStep(m, dt);
            m.LastDecision = "抬脚踏上前方稳固落脚点";
            return true;
        }

        private bool TurnBeforeTravel(JourneyMember m, Vector3 target, float dt)
        {
            Quaternion desired = WildlifeBodyEnvelope.Heading(target - m.root.position, m.root.rotation);
            if (Quaternion.Angle(m.root.rotation, desired) <= dt * 130f) return false;
            Quaternion rotation = Quaternion.RotateTowards(m.root.rotation, desired, dt * 130f);
            if (!navigation.TryProject(m.profile, m.root.position, rotation, out Vector3 foot, out JourneyTravelMode mode) ||
                !ClearOfOthers(m, foot, nextMode: mode) || !ClearOfMemberBodies(m, foot, rotation)) return false;
            if (Mathf.Abs(foot.y - m.root.position.y) > .001f ||
                !navigation.CanMoveSupportedBody(m.profile, m.root.position, m.root.rotation, foot, rotation))
            {
                if (!navigation.TryPlanBodyStep(m.profile, m.root.position, m.root.rotation, foot, rotation, out m.bodyStep)) return false;
                m.bodyStepPartner = null; m.bodyStepPhase = 1; AdvanceBodyStep(m, dt);
            }
            else ApplyMovement(m, foot, rotation, dt);
            m.Mode = mode; m.LastDecision = "在通路入口转好身体再前进";
            return true;
        }

        private void Move(JourneyMember m, Vector3 next, float dt, JourneyMember partner = null)
        {
            if(!EvaluateBodyMove(m,next,dt,out BodyMove move)){m.ActualSpeed=0;return;}
            CommitBodyMove(m,move,dt,partner);
        }
        private void CommitBodyMove(JourneyMember m,BodyMove move,float dt,JourneyMember partner=null)
        {
            // The result is local to this one synchronous step. Neighbour bodies are always rechecked.
            if(!ClearOfOthers(m,move.next,partner) || !ClearOfMemberBodies(m,move.next,move.rotation)){m.ActualSpeed=0;return;}
            if (!move.supported)
            {
                m.bodyStep=move.step;
                m.bodyStepPartner = partner; m.bodyStepPhase = 1; AdvanceBodyStep(m, dt); return;
            }
            ApplyMovement(m, move.next, move.rotation, dt);
        }

        private void AdvanceBodyStep(JourneyMember m, float dt)
        {
            Vector3 target = m.bodyStepPhase == 1 ? m.bodyStep.lift : m.bodyStepPhase == 2 ? m.bodyStep.across : m.bodyStep.landing;
            Quaternion rotation = m.bodyStepPhase == 1 ? m.root.rotation : Quaternion.RotateTowards(m.root.rotation, m.bodyStep.rotation, dt * 130f);
            Vector3 next = Vector3.MoveTowards(m.root.position, target, Mathf.Max(.1f, m.profile.walkSpeed) * dt);
            // Recheck each actual frame, including neighbours that entered the path after it was planned.
            if (!ClearOfOthers(m, next, m.bodyStepPartner) || !ClearOfMemberBodies(m, next, rotation) || !navigation.CanMoveSupportedBody(m.profile, m.root.position, m.root.rotation, next, rotation))
            { m.ActualSpeed = 0; m.LastDecision = "抬脚途中等待前方通道"; return; }
            ApplyMovement(m, next, rotation, dt);
            if ((next - target).sqrMagnitude < .0000001f) m.bodyStepPhase = m.bodyStepPhase == 3 ? 0 : m.bodyStepPhase + 1;
            if (m.bodyStepPhase == 0) m.bodyStepPartner = null;
        }

        private void ApplyMovement(JourneyMember m, Vector3 next, Quaternion rotation, float dt)
        {
            Vector3 direction = next - m.root.position; direction.y = 0;
            float distance = direction.magnitude;
            if (m.Mode == JourneyTravelMode.Swim && m.ContinuousSwimDistance + distance > m.profile.maxSwimDistance + .0001f)
            { m.ActualSpeed = 0; return; }
            m.ActualSpeed = distance / dt; m.DistanceTravelled += distance;
            if (m.Mode == JourneyTravelMode.Swim) m.ContinuousSwimDistance += distance;
            else m.ContinuousSwimDistance = 0;
            if (distance > .0001f) RecordJourneyProgress(m, next);
            m.root.rotation = rotation;
            m.root.position = next;
        }

        private void RecordJourneyProgress(JourneyMember m, Vector3 next)
        {
            if (!Travelling || m.Arrived || m.State != JourneyMemberState.Travelling || m.route == null || m.segment >= m.route.segments.Count) return;
            int linkIndex = m.route.segments[m.segment].linkIndex, key = -1;
            float before = -FlatDistance(m.root.position, m.progressDestination), after = -FlatDistance(next, m.progressDestination);
            if (linkIndex >= 0 && linkIndex < navigation.links.Length)
            {
                JourneyLink link = navigation.links[linkIndex];
                Vector3 origin = navigation.nodes[link.from].position, axis = navigation.nodes[link.to].position - origin; axis.y = 0;
                float length = axis.magnitude;
                if (length > .001f)
                {
                    axis /= length;
                    JourneySegment part = m.route.segments[m.segment];
                    bool reverse = Vector3.Dot(part.to - part.from, axis) < 0;
                    if (reverse) { origin = navigation.nodes[link.to].position; axis = -axis; }
                    key = linkIndex * 2 + (reverse ? 1 : 0);
                    before = Mathf.Clamp(Vector3.Dot(m.root.position - origin, axis), 0, length);
                    after = Mathf.Clamp(Vector3.Dot(next - origin, axis), 0, length);
                }
            }
            // Keep high-water marks across replans: revisiting a small loop or swaying cannot renew the wait.
            if (!m.routeProgress.TryGetValue(key, out float best)) m.routeProgress[key] = best = before;
            if (after <= best) return;
            m.routeProgress[key] = after; m.newRouteProgress += after - best;
            if (m.newRouteProgress < .5f) return;
            m.newRouteProgress = 0; journeyStarted = SimulationTime;
        }

        private void Animate(JourneyMember m, float dt)
        {
            if (m.animation == null) return;
            if(m.rest.BlocksMovement){m.animation.Play(m.rest.Clip,m.rest.Phase==WildlifeRestPhase.Sleeping,m.rest.Speed,m.rest.StartTime);m.animation.Tick(dt);return;}
            if(IsLifeForaging(m.id)){m.animation.Play(m.clips.forage,true,1f);m.animation.Tick(dt);return;}
            if(HasLifeTarget(m) && lifeGreetings.TryGetValue(m.id,out float until) && SimulationTime<until && m.clips.greet)
            {m.animation.Play(m.clips.greet,false,1f);m.animation.Tick(dt);return;}
            bool rushing=UrgentLifeMove(m) && m.ActualSpeed>.03f;
            AnimationClip clip=rushing && m.clips.run?m.clips.run:CurrentClip(m);
            float clipSpeed=m.profile.walkSpeed*(rushing && m.clips.run && m.clips.run!=m.clips.walk?1.6f:1f);
            float playback=m.ActualSpeed>.03f && m.Mode!=JourneyTravelMode.Swim?Mathf.Max(.25f,m.ActualSpeed/Mathf.Max(.1f,clipSpeed)):1f;
            m.animation.Play(clip,true,rushing?playback:Mathf.Min(playback,1.4f));
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
                if (m == null || !m.root || !m.overlay) continue;
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
                else if (m.State == JourneyMemberState.Social && m.headBone && social && social.ActiveConversation != null &&
                    m.ActualSpeed < .03f && CanSocialPose(m, m.root.rotation))
                {
                    var conversation = social.ActiveConversation;
                    var pose = JourneySocialGesture.Sample(social.CurrentIntent, SimulationTime - conversation.startedAt,
                        conversation.endsAt - conversation.startedAt, conversation.speakerId == m.id, m.id % 2 == 0 ? -1 : 1);
                    m.headBone.rotation = JourneySocialGesture.Rotation(m.root, m.headBone.rotation, pose);
                }
                m.overlay = true;
            }
        }

        private bool CanSocialPose(JourneyMember member, Quaternion rotation)
        {
            if (!navigation || member.Mode != JourneyTravelMode.Walk || !member.profile.bodyEnvelope.Valid) return false;
            var envelope = member.profile.bodyEnvelope;
            Bounds body = JourneySocialGesture.RequiredBody(envelope.center, envelope.half);
            body = JourneySocialGesture.SweptBody(body, member.root.rotation, rotation, out Quaternion midpoint);
            var surface = navigation.SampleSurface(member.root.position);
            if (!surface.valid || surface.blocked || surface.hasWater || surface.bridgeId != 0 || !JourneySocialGesture.ClearWorld(member.root, midpoint, body,
                navigation.obstacleMask, surface.supportingCollider)) return false;
            foreach (var other in members)
            {
                if (!Live(other) || other == member) continue;
                var otherEnvelope = other.profile.bodyEnvelope;
                if (!otherEnvelope.Valid || !JourneySocialGesture.Separated(member.root.position, midpoint, body,
                    other.root.position, other.root.rotation, JourneySocialGesture.RequiredBody(otherEnvelope.center, otherEnvelope.half))) return false;
            }
            var queryEnvelope = new WildlifeBodyEnvelope { center = body.center, half = body.extents, measured = true };
            if (navigation.playerCollision && navigation.playerCollision.TryObstacle(member.root.position, queryEnvelope.HorizontalRadius,
                out Vector3 player, out float playerRadius, queryEnvelope) &&
                !JourneySocialGesture.ClearCircle(member.root.position, midpoint, body, player, playerRadius)) return false;
            if (surroundingPopulation && surroundingPopulation.agents != null)
                foreach (var other in surroundingPopulation.agents)
                {
                    if (!other || !other.isActiveAndEnabled || other.transform == member.root ||
                        !(other.locomotion == WildlifeLocomotion.Ground ||
                            (other.locomotion == WildlifeLocomotion.Flying && other.AirHeight < .01f))) continue;
                    var otherEnvelope = other.BodyEnvelope;
                    if (!otherEnvelope.Valid || !JourneySocialGesture.Separated(member.root.position, midpoint, body,
                        other.transform.position, other.transform.rotation, new Bounds(otherEnvelope.center, otherEnvelope.half * 2))) return false;
                }
            return true;
        }

        private void DisposeAnimations()
        { foreach (JourneyMember m in members) if (m != null && m.animation != null) { m.animation.Dispose(); m.animation = null; } }
        private void OnDisable() { lifeLeases.Clear(); lifeGreetings.Clear(); lifeForage.Clear(); CancelPlanning(); UnregisterSurroundingBodies(); RestoreBodyLanguage(); DisposeAnimations(); }
        private void OnDestroy() { CancelPlanning(); UnregisterSurroundingBodies(); DisposeAnimations(); }
    }
}
