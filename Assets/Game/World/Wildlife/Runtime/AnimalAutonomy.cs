using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WeatherHouse.Life;
using WeatherHouse.WildlifeJourney;
using WeatherHouse.WatersideV2;

namespace WeatherHouse.Hillside
{
    [DefaultExecutionOrder(100)]
    public sealed partial class AnimalAutonomy : MonoBehaviour
    {
        sealed class Animal
        {
            public WildlifeAgent agent;
            public JourneyHerd herd;
            public JourneyMember member;
            public Transform root;
            public string id, species, action = "resume", previous = "resume";
            public bool outgoing, wasRain, wasInteraction, night, beganMoving;
            public float fatigue, due, nextAI, until, nextRoute, progressAt, bestDistance = float.PositiveInfinity;
            public int externalRevision, routeStep;
            public Vector3 goal;
            public AnimalLocalRoute search;
            public ShelterQuery shelterQuery;
            public float nextShelter, actionDuration;
            public List<Vector3> route;
            public readonly AnimalPlanGate gate = new AnimalPlanGate();
            public readonly Queue<AnimalOption> plan = new Queue<AnimalOption>();
            public AnimalSleepGlyph glyph;
            public AnimalEmotionGlyph emotion;
            public AnimalExpressionPolicy expression;
            public Vector3 approachExpressionOrigin;
            public float quietSince=-1;
            public AnimalRainReaction rainReaction;
            public bool shelterUrgent;
            public bool shelterExposure, shelterRetry;
            public Vector3 shelterOrigin, progressPosition;
            public AnimalStoryState story;
            public LifeSpeaker speaker;
            public string storyEpisode;
            public float nextStoryActivity, nextStorySpeech, storySampleAt, nextEncounter;
            public Vector3 storyPlayerLast;
            public string suspendedAction;
            public Vector3 suspendedGoal;
            public float suspendedRemaining;
            public readonly Dictionary<Vector3,float> failedShelters=new Dictionary<Vector3,float>();
            public float Radius => agent ? agent.Radius : member.profile.bodyRadius;
            public float Speed => agent ? agent.ActualSpeed : member.ActualSpeed;
            public bool Live => root && root.gameObject.activeInHierarchy && (agent ? agent.isActiveAndEnabled : herd && herd.isActiveAndEnabled);
            public bool Interaction => agent ? agent.HasLifeTarget && !agent.AutonomousTarget : herd.HasLifeTarget(member.id) && !member.autonomous;
            public bool Available => Live && !Interaction && (agent ? agent.State != WildlifeState.Alert && agent.State != WildlifeState.Flee :
                member.Mode == JourneyTravelMode.Walk && member.bridge == 0 && member.bodyStepPhase == 0 && !(herd.social && herd.social.IsParticipating(member.id)));
            public bool Sleeping => agent ? agent.Sleeping : herd.IsSleeping(member.id);
            public int ExternalRevision => agent ? agent.IntentRevision : member.intentRevision;
            public void Release() { if(agent)agent.ReleaseAutonomousTarget();else if(herd)herd.ReleaseAutonomousTarget(member.id); }
            public bool KeepTarget(bool holdPosition=false) => agent ? agent.KeepAutonomousTarget(holdPosition) : herd.KeepAutonomousTarget(member.id,holdPosition);
            public bool MoveTo(Vector3 point, bool rest) => agent ? agent.RequestAutonomousTarget(point,rest,action=="shelter" && shelterUrgent) : herd.RequestAutonomousTarget(member.id,point,rest,action=="shelter" && shelterUrgent);
            public bool Step(Vector3 from,Vector3 to,out Vector3 foot)
            {
                foot=from;
                if(agent)return agent.Population && agent.Population.TryMoveFrom(agent,from,
                    (from-root.position).sqrMagnitude<.01f?root.rotation:WildlifeBodyEnvelope.Heading(to-from,root.rotation),to,out foot);
                foot=from;
                return herd && herd.navigation && herd.navigation.TryStep(member.profile,from,to,out foot,out JourneyTravelMode mode) &&
                    mode==JourneyTravelMode.Walk && herd.navigation.CanMoveSupportedBody(member.profile,from,
                        WildlifeBodyEnvelope.Heading(to-from,root.rotation),foot,WildlifeBodyEnvelope.Heading(to-from,root.rotation));
            }
        }
        struct Shelter { public Transform source; public Vector3 offset; public Vector3 Point => source.position+offset; }
        sealed class ShelterQuery
        {
            public readonly List<Vector3> candidates=new List<Vector3>();
            public readonly Queue<(Vector3 target,AnimalLocalRoute route)> pendingRoutes=new Queue<(Vector3,AnimalLocalRoute)>();
            public Vector3 origin, target;
            public int probe, candidate, routeWork;
            public bool probeNext;
            public AnimalLocalRoute route;
        }
        static readonly float[] ShelterRings={0,3,6,10,16,24,32};
        readonly List<Animal> animals = new List<Animal>();
        readonly List<Shelter> shelters = new List<Shelter>();
        readonly RaycastHit[] roofHits = new RaycastHit[32];
        Transform player;
        LifeWorld world;
        WatersideMood mood;
        WeatherHousePrototype.WeatherRegion[] weatherRegions=Array.Empty<WeatherHousePrototype.WeatherRegion>();
        AnimalPlannerClient client;
        int cursor;
        float nextRequest, rainUntil;
        Vector3 rainCenter;
        float rainRadius;
        int rainStrength;
        public int AnimalCount => animals.Count;
        public int OutgoingCount { get; private set; }
        public int AppliedPlans { get; private set; }
        public int RejectedPlans { get; private set; }
        public string AIStatus => client ? client.LastStatus : "初始化中";

        public void Initialize(LifeWorld source,Transform target) { world=source;player=target; }
        IEnumerator Start()
        {
            yield return null;
            if(!world || !player)yield break;
            client=gameObject.AddComponent<AnimalPlannerClient>();
            mood=FindObjectOfType<WatersideMood>();
            weatherRegions=FindObjectsOfType<WeatherHousePrototype.WeatherRegion>();
            var owned=new HashSet<Transform>();
            foreach(JourneyHerd herd in FindObjectsOfType<JourneyHerd>())
                foreach(JourneyMember member in herd.members)
                    if(member!=null && member.root && owned.Add(member.root))
                        Add(new Animal {herd=herd,member=member,root=member.root,species="herd",
                            id=herd.name+"/"+member.id,outgoing=AnimalPlan.Outgoing(herd.seed,member.id,"herd")});
            foreach(WildlifePopulation population in FindObjectsOfType<WildlifePopulation>())
                foreach(WildlifeAgent agent in population.agents)
                    if(agent && agent.locomotion==WildlifeLocomotion.Ground && owned.Add(agent.transform))
                        Add(new Animal {agent=agent,root=agent.transform,species=agent.speciesId,
                            id=population.name+"/"+agent.speciesId+"/"+agent.spawnId,outgoing=agent.Outgoing});
            // Trees without canopy colliders use their authored renderer canopy footprint.
            // Candidate positions still require a full ground/body route before they become destinations.
            foreach(LODGroup tree in FindObjectsOfType<LODGroup>())
            {
                string name=tree.name.ToLowerInvariant();
                if(!(name.Contains("tree")||name.Contains("fir")||name.Contains("树")))continue;
                Renderer[] parts=tree.GetComponentsInChildren<Renderer>();if(parts.Length==0)continue;
                Bounds bounds=parts[0].bounds;foreach(Renderer part in parts)bounds.Encapsulate(part.bounds);
                float radius=Mathf.Min(bounds.extents.x,bounds.extents.z)*.55f;
                if(bounds.size.y<2 || radius<.65f)continue;
                for(int i=0;i<4;i++)
                {
                    float angle=i*Mathf.PI*.5f;
                    Vector3 point=new Vector3(bounds.center.x+Mathf.Cos(angle)*radius,tree.transform.position.y,bounds.center.z+Mathf.Sin(angle)*radius);
                    shelters.Add(new Shelter{source=tree.transform,offset=point-tree.transform.position});
                }
                if(shelters.Count%64==0)yield return null;
            }
        }
        void Add(Animal animal)
        {
            int seed=animal.agent?animal.agent.Population.worldSeed:animal.herd.seed;
            int individual=animal.agent?animal.agent.spawnId:animal.member.id;
            animal.rainReaction=new AnimalRainReaction(seed,individual,animal.species);
            int expressionSeed=unchecked(seed*397^individual);
            foreach(char c in animal.species)expressionSeed=unchecked(expressionSeed*31+c);
            animal.expression=new AnimalExpressionPolicy(expressionSeed,animal.outgoing,
                animal.agent?animal.agent.timidity:.5f,animal.agent?animal.agent.sociability:animal.member.sociability);
            animal.fatigue=.2f+(animals.Count%7)*.05f;
            animal.due=Time.time+(animals.Count%12)*.025f;animal.nextAI=Time.time+animals.Count*.5f;
            animal.externalRevision=animal.ExternalRevision;
            animal.glyph=animal.root.gameObject.AddComponent<AnimalSleepGlyph>();
            animal.glyph.height=animal.agent?Mathf.Max(.6f,animal.agent.BodyEnvelope.half.y*2+.2f):Mathf.Max(.7f,animal.member.profile.bodyEnvelope.half.y*2+.2f);
            animal.emotion=animal.root.gameObject.AddComponent<AnimalEmotionGlyph>();animal.emotion.height=animal.glyph.height;
            InitializeStory(animal);
            animals.Add(animal);if(animal.outgoing)OutgoingCount++;
        }
        void UpdateExpressions(Animal a)
        {
            a.glyph.Sleeping=a.Sleeping;
            AnimalExpressionSignals signals=a.Sleeping?AnimalExpressionSignals.Sleeping:AnimalExpressionSignals.None;
            if(a.agent)
            {
                if(a.agent.State==WildlifeState.Alert)signals|=AnimalExpressionSignals.Alert;
                if(a.agent.State==WildlifeState.Flee)signals|=AnimalExpressionSignals.Flee;
                if(a.agent.State==WildlifeState.Greet)signals|=AnimalExpressionSignals.Greet;
            }
            else if(a.herd.social && a.herd.social.IsParticipating(a.member.id))signals|=AnimalExpressionSignals.Greet;
            if((a.action=="approach" || a.action=="follow") && Flat(a.root.position,player.position)<4+a.Radius &&
                Flat(a.root.position,a.approachExpressionOrigin)>.35f)signals|=AnimalExpressionSignals.NearPlayer;
            bool quiet=!a.Sleeping && !a.Interaction && !Wet(a) && a.action=="resume" && a.Speed<.03f &&
                (a.agent?a.agent.State==WildlifeState.Idle:a.member.State==JourneyMemberState.Idle);
            if(!quiet)a.quietSince=-1;
            else
            {
                if(a.quietSince<0)a.quietSince=Time.time;
                int identity=a.agent?a.agent.spawnId:a.member.id;
                if(Time.time-a.quietSince>8+(Mathf.Abs(identity)%7)*2)signals|=AnimalExpressionSignals.QuietRest;
            }
            if(a.expression.TryChoose(signals,Time.time,a.emotion.IsPlaying,out AnimalEmotion emotion,out bool urgent))
                a.emotion.Play(emotion);
            if(a.Sleeping)a.emotion.Stop();
        }
        public void Rain(Vector3 center,float radius,bool active,int strength=2)
        { rainCenter=center;rainRadius=radius;rainUntil=active?Time.time+1:0;rainStrength=active?Mathf.Clamp(strength,1,2):0; }
        int RainIntensity(Animal a)
            => RainIntensityAt(a.root.position);
        int RainIntensityAt(Vector3 point)
        {
            int strength=Time.time<rainUntil && Flat(point,rainCenter)<=rainRadius+.001f?rainStrength:0;
            foreach(var region in weatherRegions)
                if(region && region.isActiveAndEnabled && region.SoundActive &&
                    region.Current==WeatherHousePrototype.LocalWeather.Rain && region.Contains(point))strength=Mathf.Max(strength,region.SoundStrength);
            return strength;
        }
        bool Wet(Animal a)=>RainIntensity(a)>0;
        bool NeedsShelter(Animal a)
        {
            bool wasUrgent=a.rainReaction.Urgent;
            int intensity=RainIntensity(a);
            // A roof can lie just outside the rain circle. Stay until rain leaves both this animal and its departure place.
            if(a.shelterExposure)intensity=Mathf.Max(intensity,RainIntensityAt(a.shelterOrigin));
            if(intensity==0)a.shelterExposure=false;
            a.rainReaction.Observe(intensity);a.shelterUrgent=a.rainReaction.Urgent;
            if(a.rainReaction.NeedsShelter && !a.shelterExposure){a.shelterExposure=true;a.shelterOrigin=a.root.position;}
            if(!wasUrgent && a.shelterUrgent)a.nextShelter=Mathf.Min(a.nextShelter,Time.time);
            return a.rainReaction.NeedsShelter;
        }
        public bool IsUrgentlySheltering(Transform animalRoot)
        {
            if(!animalRoot)return false;
            foreach(Animal a in animals)
                if(a.Live && (a.root==animalRoot || animalRoot.IsChildOf(a.root)) &&
                    (a.action=="shelter" || a.shelterQuery!=null || a.shelterExposure))
                    return Mathf.Max(RainIntensity(a),a.shelterExposure?RainIntensityAt(a.shelterOrigin):0)>=2;
            return false;
        }
        static float Flat(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
        bool Night=>mood && mood.Current==WatersideMood.Mode.Night;

        void Update()
        {
            if(!player || !client || animals.Count==0)return;
            // Visual event edges are cheap and must not be skipped when the navigation budget is exhausted.
            foreach(Animal animal in animals)if(animal.Live)
            {
                if(animal.action=="shelter" && animal.Available)animal.KeepTarget();
                UpdateExpressions(animal);
            }
            int decisions=0;
            int start=cursor;
            // One rotating queue for all actors: leaving camera range must not strand an active route.
            for(int visited=0;visited<animals.Count;visited++)
            {
                int i=(start+visited)%animals.Count;
                Animal a=animals[i];if(!a.Live)continue;
                bool decide=Time.time>=a.due;
                if(!decide && a.search==null && a.shelterQuery==null)continue;
                // Cheap intent scoring must not consume the physics slots needed by pending routes.
                if(decide && decisions<8)
                {
                    if(!Tick(a)){cursor=i;return;}
                    a.due=Time.time+.3f;decisions++;
                }
                if(!a.Available || (a.search==null || a.search.Finished) && (a.shelterQuery==null || !NeedsShelter(a)))continue;
                if(!AnimalWorkBudget.TryBegin(AnimalWorkBudget.Kind.Planning,out var planningWork)){cursor=i;return;}
                using var planningScope=planningWork;
                AdvancePlanning(a);
            }
            cursor=(start+1)%animals.Count;
        }
        bool Tick(Animal a)
        {
            bool rain=NeedsShelter(a), interaction=a.Interaction;
            if(rain && a.storyEpisode!=null)EndStory(a,"interrupted","rain");
            if(a.wasRain!=rain || a.wasInteraction!=interaction || a.externalRevision!=a.ExternalRevision || a.night!=Night)
            {
                a.gate.Invalidate();a.plan.Clear();a.search=null;a.shelterQuery=null;
                if(interaction || !a.Available){EndStory(a,"interrupted","interaction");a.Release();a.route=null;a.action="resume";}
                if(a.wasRain && !rain && a.action=="shelter")Finish(a);
                a.wasRain=rain;a.wasInteraction=interaction;a.externalRevision=a.ExternalRevision;a.night=Night;
            }
            if(!a.Available){EndStory(a,"interrupted","unavailable");return true;}
            float previousFatigue=a.fatigue;
            a.fatigue=Mathf.Clamp01(a.fatigue+(a.Sleeping?-.015f:a.Speed>.1f?.0015f:.0005f));
            float distance=Flat(a.root.position,player.position);
            // Weather and personal space never wait for the network.
            if(rain && (a.action!="shelter" || a.shelterRetry) && a.shelterQuery==null && Time.time>=a.nextShelter)
            {
                a.shelterQuery=StartShelterSearch(a);
            }
            else if(!a.outgoing && distance<3 && a.action!="avoid" && a.action!="shelter")
                Begin(a,new AnimalOption{id="avoid",action="avoid"},Away(a,4));
            else if(a.action=="resume" && Time.time>=a.until)
            {
                AnimalObservation local=Observe(a);
                AnimalOption best=local.options[0];foreach(AnimalOption option in local.options)if(option.score>best.score)best=option;
                if(best.action!="resume")Begin(a,best,Target(a,best));
                else a.until=Time.time+3;
            }
            if(!Execute(a)){a.fatigue=previousFatigue;return false;}
            if(!client.Busy && !a.gate.Pending && Time.time>=nextRequest && Time.time>=a.nextAI && distance<24 && a.Available && StoryRequestPriority(a))
            {
                AnimalObservation observation=Observe(a);
                int revision=a.gate.Begin(Time.time);
                nextRequest=Time.time+10;a.nextAI=Time.time+(IsStoryDemo(a)?50:90);
                StartCoroutine(client.Plan(observation,plan=>
                {
                    if(!a.gate.Complete(revision,Time.time,a.Live && a.Available && Wet(a)==observation.raining && Night==observation.night &&
                        a.ExternalRevision==a.externalRevision)){RejectedPlans++;return;}
                    if(plan==null)return;
                    ApplyStoryPlan(a,plan);
                    // Do not let an AI preference interrupt urgent shelter, sleep transitions or a newly started action.
                    if(a.action!="resume"){a.plan.Clear();foreach(string id in plan.choices)a.plan.Enqueue(Array.Find(observation.options,o=>o.id==id));return;}
                    a.plan.Clear();foreach(string id in plan.choices)a.plan.Enqueue(Array.Find(observation.options,o=>o.id==id));
                    AppliedPlans++;Next(a);
                },()=>!isActiveAndEnabled || !a.Live || a.gate.Revision!=revision || a.Interaction || a.ExternalRevision!=a.externalRevision));
            }
            return true;
        }
        AnimalObservation Observe(Animal a)
        {
            var options=new List<AnimalOption>{new AnimalOption{id="resume",action="resume",target="previous",score=1}};
            bool shelterNeeded=NeedsShelter(a);
            float distance=Flat(a.root.position,player.position);
            if(shelterNeeded && a.action=="shelter")options.Add(new AnimalOption{id="shelter",action="shelter",target="shelter",score=10});
            bool nocturnal=a.species=="fox";
            float sleepyAt=(Night!=nocturnal)?.45f:.8f;
            if(a.fatigue>sleepyAt && !shelterNeeded)options.Add(new AnimalOption{id="sleep",action="sleep",target="here",score=2+a.fatigue});
            if(a.outgoing && distance>2.5f && distance<10 && !shelterNeeded)
            {
                options.Add(new AnimalOption{id="approach",action="approach",target="player",score=1.8f});
                options.Add(new AnimalOption{id="follow",action="follow",target="player",score=1.6f});
            }
            if(!a.outgoing && distance<4.5f)options.Add(new AnimalOption{id="avoid",action="avoid",target="away",score=6});
            AddStoryOption(a,options,shelterNeeded);
            return new AnimalObservation {animal=a.id,species=a.species,revision=a.gate.Revision,current=a.action,previous=a.previous,
                outgoing=a.outgoing,raining=Wet(a),night=Night,inHerd=a.herd,fatigue=a.fatigue,playerDistance=distance,options=options.ToArray(),
                character=a.story==null?null:JsonUtility.FromJson<AnimalStoryState>(JsonUtility.ToJson(a.story)),
                vocabulary=StoryVocabulary(a),canShareStory=CanShareStory(a),
                allowedWishes=IsStoryDemo(a)?AnimalStoryRules.WishIds:AnimalStoryRules.OrdinaryWishIds};
        }
        Vector3 Away(Animal a,float distance)
        {
            Vector3 away=a.root.position-player.position;away.y=0;if(away.sqrMagnitude<.01f)away=a.root.right;
            return player.position+away.normalized*distance;
        }
        Vector3 Target(Animal a,AnimalOption option)
        {
            if(option.action=="approach"||option.action=="follow")return Away(a,2.6f+a.Radius);
            if(option.action=="avoid")return Away(a,4.5f+a.Radius);
            if(option.action=="shelter")return a.goal;
            if(option.action=="watch_sea" && world.TrySeaWatchPoint(out Vector3 coast,out _))return coast;
            return a.root.position;
        }
        void Begin(Animal a,AnimalOption option,Vector3 target)
        {
            if(a.action=="watch_sea" && option.action!="watch_sea")EndStory(a,"interrupted",option.action);
            if((option.action=="approach" || option.action=="follow") && a.action!="approach" && a.action!="follow")
                a.approachExpressionOrigin=a.root.position;
            if(option.action=="shelter" && a.action!="shelter")
            {a.suspendedAction=a.action;a.suspendedGoal=a.goal;a.suspendedRemaining=float.IsPositiveInfinity(a.until)?a.actionDuration:Mathf.Max(0,a.until-Time.time);}
            if(a.action!=option.action)a.gate.Invalidate();
            a.previous=a.action;a.action=option.action;a.goal=target;a.route=null;a.routeStep=0;a.search=null;
            a.shelterRetry=false;
            a.actionDuration=option.action=="sleep"?(Night?75:25):option.action=="follow"?20:12;
            a.until=Flat(a.root.position,target)<.3f?Time.time+a.actionDuration:float.PositiveInfinity;
            a.progressAt=Time.time;a.bestDistance=float.PositiveInfinity;a.nextRoute=0;a.beganMoving=false;
            a.progressPosition=a.root.position;
            if(option.action=="watch_sea")BeginStory(a);
        }
        bool Execute(Animal a)
        {
            if(a.action=="resume")return true;
            if(a.action=="watch_sea" && ExecuteStory(a))return true;
            if(a.action=="shelter" && !NeedsShelter(a)){Finish(a);return true;}
            if(a.action!="shelter" && Time.time>=a.until){Finish(a);return true;}
            float distance=Flat(a.root.position,a.goal);
            if(a.action=="follow" && Time.time>=a.nextRoute && Flat(a.goal,Target(a,new AnimalOption{action="follow"}))>1)
            {a.goal=Target(a,new AnimalOption{action="follow"});a.search=null;a.route=null;a.nextRoute=Time.time+2;}
            if(distance<.3f)
            {
                if(a.action=="shelter"||a.action=="sleep"||a.action=="follow")
                {
                    if(!AnimalWorkBudget.TryBegin(AnimalWorkBudget.Kind.Planning,out var restWork))return false;
                    using(restWork)
                        if(!a.MoveTo(a.root.position,a.action=="sleep") && a.action=="shelter")a.KeepTarget(true);
                }
                else Finish(a);
                return true;
            }
            bool progressed=a.action=="shelter"?Flat(a.root.position,a.progressPosition)>.2f:distance<a.bestDistance-.1f;
            if(progressed){a.bestDistance=distance;a.progressPosition=a.root.position;a.progressAt=Time.time;}
            if(a.beganMoving && Time.time-a.progressAt>30){Failed(a);return true;}
            if(a.route==null)
            {
                if(a.search==null)a.search=new AnimalLocalRoute(a.root.position,a.goal,a.Radius,a.Step);
                if(!a.search.Finished)return true;
                a.route=a.search.Result;a.search=null;a.routeStep=0;
                if(a.route==null){Failed(a);return true;}
                if(!a.beganMoving){a.until=Time.time+a.actionDuration;a.progressAt=Time.time;a.beganMoving=true;}
                // Search may finish after the actor moved. Reconnect from the live pose through normal body checks.
            }
            while(a.routeStep<a.route.Count && Flat(a.root.position,a.route[a.routeStep])<.3f)a.routeStep++;
            if(!AnimalWorkBudget.TryBegin(AnimalWorkBudget.Kind.Planning,out var movementWork))return false;
            using var movementScope=movementWork;
            if(a.routeStep>=a.route.Count){a.MoveTo(a.root.position,a.action=="sleep");return true;}
            if(!a.MoveTo(a.route[a.routeStep],false)){a.route=null;a.search=null;}
            return true;
        }
        void Finish(Animal a)
        {
            EndStory(a,"interrupted","ended");
            string resume=a.action=="shelter" && !NeedsShelter(a)?a.suspendedAction:null;
            a.Release();a.previous=a.action;a.action="resume";a.route=null;a.search=null;
            a.gate.Invalidate();a.until=Time.time+8;
            if(!string.IsNullOrEmpty(resume) && resume!="resume" && a.suspendedRemaining>0)
            {
                Vector3 target=a.suspendedGoal;float remaining=a.suspendedRemaining;a.suspendedAction=null;
                float playerDistance=Flat(a.root.position,player.position);
                if(resume=="sleep" || (resume=="follow" || resume=="approach") && a.outgoing && playerDistance<10 ||
                    resume=="avoid" && !a.outgoing && playerDistance<4.5f)
                {
                    var option=new AnimalOption{action=resume};
                    if(resume!="sleep")target=Target(a,option);
                    Begin(a,option,target);a.actionDuration=remaining;
                    if(!float.IsPositiveInfinity(a.until))a.until=Time.time+remaining;
                    return;
                }
            }
            Next(a);
        }
        void Failed(Animal a)
        {
            EndStory(a,"blocked","route");
            if(a.action=="shelter")
            {
                a.failedShelters[a.goal]=Time.time+30;
                if(NeedsShelter(a))
                {
                    // Stop the failed leg in place, retaining the shelter intent while another bounded search runs.
                    a.KeepTarget(true);a.plan.Clear();a.route=null;a.search=null;a.shelterQuery=null;
                    a.goal=a.root.position;a.beganMoving=false;a.shelterRetry=true;a.nextShelter=Time.time;
                    return;
                }
            }
            a.plan.Clear();Finish(a);a.until=a.nextRoute=Time.time+3;
        }
        bool ShelterAvailable(Animal a,Vector3 point)
        {
            if(a.failedShelters.TryGetValue(point,out float until) && Time.time<until)return false;
            foreach(Animal other in animals)
                if(other!=a && other.Live && other.action=="shelter" && Flat(other.goal,point)<other.Radius+a.Radius+.3f)return false;
            return true;
        }
        void Next(Animal a)
        {
            if(a.plan.Count==0 || !a.Available)return;
            AnimalOption next=a.plan.Dequeue();
            AnimalObservation now=Observe(a);
            if(Array.Find(now.options,o=>o.id==next.id)==null){a.plan.Clear();return;}
            if(next.action!="resume")Begin(a,next,Target(a,next));
        }
        ShelterQuery StartShelterSearch(Animal a)
        {
            var query=new ShelterQuery{origin=a.root.position};
            foreach(LifeObject obj in world.Objects)
                if(obj && obj.State.sheltered && !obj.State.held)
                    AddShelterCandidate(a,query,obj.transform.position);
            foreach(Shelter shelter in shelters)
            {
                if(!shelter.source || !shelter.source.gameObject.activeInHierarchy)continue;
                AddShelterCandidate(a,query,shelter.Point);
            }
            return query;
        }
        void AddShelterCandidate(Animal a,ShelterQuery query,Vector3 point)
        {
            if(Flat(query.origin,point)>32)return;
            if(a.agent && a.agent.Population)point.y=a.agent.Population.GroundHeight(point);
            else if(Terrain.activeTerrain)point.y=Terrain.activeTerrain.SampleHeight(point)+Terrain.activeTerrain.transform.position.y;
            if(!ShelterAvailable(a,point))return;
            foreach(Vector3 existing in query.candidates)if(Flat(existing,point)<.5f)return;
            // Keep only the untried tail ordered; probing may add candidates while another route is pending.
            int insert=query.candidate;float distance=Flat(query.origin,point);
            while(insert<query.candidates.Count && Flat(query.origin,query.candidates[insert])<=distance)insert++;
            query.candidates.Insert(insert,point);
        }
        void AdvancePlanning(Animal a)
        {
            if(!a.Available)return;
            ShelterQuery query=a.shelterQuery;
            // Rain search takes priority over a repeatedly retargeted follow plan; the current movement lease continues.
            if(query==null || !NeedsShelter(a))
            {if(a.search!=null && !a.search.Finished)a.search.Advance();return;}
            const int directions=16;
            if(query.probe<ShelterRings.Length*directions &&
                (query.probeNext || query.route==null && query.candidate>=query.candidates.Count && query.pendingRoutes.Count==0))
            {
                query.probeNext=false;
                int index=query.probe++;
                float radius=ShelterRings[index/directions],angle=index%directions*Mathf.PI*2/directions;
                Vector3 probe=query.origin+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);
                if(a.agent && a.agent.Population)probe.y=a.agent.Population.GroundHeight(probe);
                else if(Terrain.activeTerrain)probe.y=Terrain.activeTerrain.SampleHeight(probe)+Terrain.activeTerrain.transform.position.y;
                // Cast upward from above the animal: terrain, fences and rocks below its head are not roofs.
                float height=a.agent?a.agent.BodyEnvelope.half.y*2:a.member.profile.bodyEnvelope.half.y*2;
                int count=Physics.RaycastNonAlloc(probe+Vector3.up*Mathf.Max(.7f,height+.15f),Vector3.up,roofHits,8,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
                for(int h=0;h<count;h++)
                {
                    RaycastHit hit=roofHits[h];if(hit.collider is TerrainCollider || hit.collider.transform.IsChildOf(a.root) ||
                        hit.collider.GetComponentInParent<WildlifeAgent>() || Mathf.Abs(hit.normal.y)<.5f)continue;
                    AddShelterCandidate(a,query,probe);break;
                }
                if(radius==0)query.probe=directions;
                return;
            }
            if(query.route==null)
            {
                while(query.candidate<query.candidates.Count && !ShelterAvailable(a,query.candidates[query.candidate]))query.candidate++;
                if(query.candidate<query.candidates.Count)
                {
                    query.target=query.candidates[query.candidate++];
                    // Reject occupied/unsupported destinations before spending a route search on them.
                    if(!a.Step(query.target,query.target,out Vector3 foot)){query.probeNext=true;return;}
                    query.target=foot;
                    query.route=new AnimalLocalRoute(a.root.position,query.target,a.Radius,a.Step);
                }
                else if(query.pendingRoutes.Count>0)
                    (query.target,query.route)=query.pendingRoutes.Dequeue();
                else
                {a.shelterQuery=null;a.nextShelter=Time.time+5;return;}
                query.routeWork=0;
            }
            query.route.Advance();
            if(!query.route.Finished)
            {
                // Preserve long searches while giving other shelter candidates a chance to finish.
                if(++query.routeWork>=12)
                {query.pendingRoutes.Enqueue((query.target,query.route));query.route=null;query.routeWork=0;query.probeNext=true;}
                return;
            }
            if(query.route.Result==null){a.failedShelters[query.target]=Time.time+15;query.route=null;query.probeNext=true;return;}
            if(!ShelterAvailable(a,query.target)){query.route=null;query.probeNext=true;return;}
            Begin(a,new AnimalOption{id="shelter",action="shelter"},query.target);
            a.route=query.route.Result;a.shelterQuery=null;a.beganMoving=true;
        }
        void OnDisable()
        {
            StopAllCoroutines();if(client)client.enabled=false;
            foreach(Animal a in animals){a.gate.Invalidate();a.rainReaction.Observe(0);a.shelterExposure=false;if(a.Live)a.Release();if(a.glyph)a.glyph.Sleeping=false;if(a.emotion)a.emotion.Stop();}
        }
    }
}
