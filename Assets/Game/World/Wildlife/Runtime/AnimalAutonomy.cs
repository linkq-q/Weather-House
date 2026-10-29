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
    public sealed class AnimalAutonomy : MonoBehaviour
    {
        sealed class Animal
        {
            public WildlifeAgent agent;
            public JourneyHerd herd;
            public JourneyMember member;
            public Transform root;
            public string id, species, action = "resume", previous = "resume";
            public bool outgoing, wasRain, wasInteraction, night;
            public float fatigue, due, nextAI, until, nextRoute, progressAt, bestDistance = float.PositiveInfinity;
            public int externalRevision, routeStep;
            public Vector3 goal;
            public AnimalLocalRoute search;
            public List<Vector3> route;
            public readonly AnimalPlanGate gate = new AnimalPlanGate();
            public readonly Queue<AnimalOption> plan = new Queue<AnimalOption>();
            public AnimalSleepGlyph glyph;
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
            public bool MoveTo(Vector3 point, bool rest) => agent ? agent.RequestAutonomousTarget(point,rest) : herd.RequestAutonomousTarget(member.id,point,rest);
            public bool Step(Vector3 from,Vector3 to,out Vector3 foot)
            {
                foot=from;
                if(agent)return agent.Population && agent.Population.TryMoveFrom(agent,from,
                    WildlifeBodyEnvelope.Heading(to-from,root.rotation),to,out foot);
                foot=from;
                return herd && herd.navigation && herd.navigation.TryStep(member.profile,from,to,out foot,out JourneyTravelMode mode) &&
                    mode==JourneyTravelMode.Walk && herd.navigation.CanMoveSupportedBody(member.profile,from,
                        WildlifeBodyEnvelope.Heading(to-from,root.rotation),foot,WildlifeBodyEnvelope.Heading(to-from,root.rotation));
            }
        }
        struct Shelter { public Transform source; public Vector3 offset; public Vector3 Point => source.position+offset; }
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
            animal.fatigue=.2f+(animals.Count%7)*.05f;
            animal.due=Time.time+(animals.Count%12)*.025f;animal.nextAI=Time.time+animals.Count*.5f;
            animal.externalRevision=animal.ExternalRevision;
            animal.glyph=animal.root.gameObject.AddComponent<AnimalSleepGlyph>();
            animal.glyph.height=animal.agent?Mathf.Max(.6f,animal.agent.BodyEnvelope.half.y*2+.2f):Mathf.Max(.7f,animal.member.profile.bodyEnvelope.half.y*2+.2f);
            animals.Add(animal);if(animal.outgoing)OutgoingCount++;
        }
        public void Rain(Vector3 center,float radius,bool active)
        { rainCenter=center;rainRadius=radius;rainUntil=active?Time.time+1:0; }
        bool Wet(Animal a)
        {
            if(Time.time<rainUntil && Flat(a.root.position,rainCenter)<rainRadius)return true;
            foreach(var region in weatherRegions)
                if(region && region.isActiveAndEnabled && !region.localPlayerWeather && region.SoundActive &&
                    region.Current==WeatherHousePrototype.LocalWeather.Rain && region.SoundStrength>=2 && region.Contains(a.root.position))return true;
            return false;
        }
        static float Flat(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
        bool Night=>mood && mood.Current==WatersideMood.Mode.Night;

        void Update()
        {
            if(!player || !client || animals.Count==0)return;
            for(int visited=0;visited<animals.Count;visited++)
            {
                int i=cursor;cursor=(cursor+1)%animals.Count;
                Animal a=animals[i];if(!a.Live)continue;
                a.glyph.Sleeping=a.Sleeping;
                if(Time.time<a.due)continue;
                if(!AnimalWorkBudget.TryTake()){cursor=i;break;}
                Tick(a);a.due=Time.time+.3f;
            }
        }
        void Tick(Animal a)
        {
            bool rain=Wet(a), interaction=a.Interaction;
            if(a.wasRain!=rain || a.wasInteraction!=interaction || a.externalRevision!=a.ExternalRevision || a.night!=Night)
            {
                a.gate.Invalidate();a.plan.Clear();a.search=null;
                if(interaction || !a.Available){a.Release();a.route=null;a.action="resume";}
                if(a.wasRain && !rain && a.action=="shelter")Finish(a);
                a.wasRain=rain;a.wasInteraction=interaction;a.externalRevision=a.ExternalRevision;a.night=Night;
            }
            if(!a.Available)return;
            a.fatigue=Mathf.Clamp01(a.fatigue+(a.Sleeping?-.015f:a.Speed>.1f?.0015f:.0005f));
            float distance=Flat(a.root.position,player.position);
            // Weather and personal space never wait for the network.
            if(rain && a.action!="shelter" && Time.time>=a.nextRoute)
            {
                if(FindShelter(a,out Vector3 shelter))Begin(a,new AnimalOption{id="shelter",action="shelter"},shelter);
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
            Execute(a);
            if(!client.Busy && !a.gate.Pending && Time.time>=nextRequest && Time.time>=a.nextAI && distance<24 && a.Available)
            {
                AnimalObservation observation=Observe(a);
                int revision=a.gate.Begin(Time.time);
                nextRequest=Time.time+10;a.nextAI=Time.time+90;
                StartCoroutine(client.Plan(observation,plan=>
                {
                    if(!a.gate.Complete(revision,Time.time,a.Live && a.Available && Wet(a)==observation.raining && Night==observation.night &&
                        a.ExternalRevision==a.externalRevision)){RejectedPlans++;return;}
                    if(plan==null)return;
                    // Do not let an AI preference interrupt urgent shelter, sleep transitions or a newly started action.
                    if(a.action!="resume"){a.plan.Clear();foreach(string id in plan.choices)a.plan.Enqueue(Array.Find(observation.options,o=>o.id==id));return;}
                    a.plan.Clear();foreach(string id in plan.choices)a.plan.Enqueue(Array.Find(observation.options,o=>o.id==id));
                    AppliedPlans++;Next(a);
                },()=>!isActiveAndEnabled || !a.Live || a.gate.Revision!=revision || a.Interaction || a.ExternalRevision!=a.externalRevision));
            }
        }
        AnimalObservation Observe(Animal a)
        {
            var options=new List<AnimalOption>{new AnimalOption{id="resume",action="resume",target="previous",score=1}};
            float distance=Flat(a.root.position,player.position);
            if(Wet(a) && FindShelter(a,out _))options.Add(new AnimalOption{id="shelter",action="shelter",target="shelter",score=10});
            bool nocturnal=a.species=="fox";
            float sleepyAt=(Night!=nocturnal)?.45f:.8f;
            if(a.fatigue>sleepyAt && !Wet(a))options.Add(new AnimalOption{id="sleep",action="sleep",target="here",score=2+a.fatigue});
            if(a.outgoing && distance>2.5f && distance<10 && !Wet(a))
            {
                options.Add(new AnimalOption{id="approach",action="approach",target="player",score=1.8f});
                options.Add(new AnimalOption{id="follow",action="follow",target="player",score=1.6f});
            }
            if(!a.outgoing && distance<4.5f)options.Add(new AnimalOption{id="avoid",action="avoid",target="away",score=6});
            return new AnimalObservation {animal=a.id,species=a.species,revision=a.gate.Revision,current=a.action,previous=a.previous,
                outgoing=a.outgoing,raining=Wet(a),night=Night,inHerd=a.herd,fatigue=a.fatigue,playerDistance=distance,options=options.ToArray()};
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
            if(option.action=="shelter" && FindShelter(a,out Vector3 point))return point;
            return a.root.position;
        }
        void Begin(Animal a,AnimalOption option,Vector3 target)
        {
            if(option.action=="shelter" && a.action!="shelter")
            {a.suspendedAction=a.action;a.suspendedGoal=a.goal;a.suspendedRemaining=Mathf.Max(0,a.until-Time.time);}
            if(a.action!=option.action)a.gate.Invalidate();
            a.previous=a.action;a.action=option.action;a.goal=target;a.route=null;a.routeStep=0;a.search=null;
            a.until=Time.time+(option.action=="sleep"?(Night?75:25):option.action=="follow"?20:12);
            a.progressAt=Time.time;a.bestDistance=float.PositiveInfinity;a.nextRoute=0;
        }
        void Execute(Animal a)
        {
            if(a.action=="resume")return;
            if(a.action=="shelter" && !Wet(a)){Finish(a);return;}
            if(a.action!="shelter" && Time.time>=a.until){Finish(a);return;}
            float distance=Flat(a.root.position,a.goal);
            if(a.action=="follow" && Time.time>=a.nextRoute && Flat(a.goal,Target(a,new AnimalOption{action="follow"}))>1)
            {a.goal=Target(a,new AnimalOption{action="follow"});a.search=null;a.route=null;a.nextRoute=Time.time+2;}
            if(distance<.3f)
            {
                if(a.action=="shelter"||a.action=="sleep"||a.action=="follow")a.MoveTo(a.root.position,a.action=="sleep");
                else Finish(a);
                return;
            }
            if(distance<a.bestDistance-.1f){a.bestDistance=distance;a.progressAt=Time.time;}
            if(Time.time-a.progressAt>30){Failed(a);return;}
            if(a.route==null)
            {
                if(a.search==null)a.search=new AnimalLocalRoute(a.root.position,a.goal,a.Radius,a.Step);
                for(int i=0;i<4 && !a.search.Finished;i++)
                {if(!AnimalWorkBudget.TryTake())break;a.search.Advance();}
                if(!a.search.Finished)return;
                a.route=a.search.Result;a.search=null;a.routeStep=0;
                if(a.route==null){Failed(a);return;}
                // Search may finish after the actor moved. Reconnect from the live pose through normal body checks.
            }
            while(a.routeStep<a.route.Count && Flat(a.root.position,a.route[a.routeStep])<.3f)a.routeStep++;
            if(a.routeStep>=a.route.Count){a.MoveTo(a.root.position,a.action=="sleep");return;}
            if(!a.MoveTo(a.route[a.routeStep],false)){a.route=null;a.search=null;}
        }
        void Finish(Animal a)
        {
            string resume=a.action=="shelter" && !Wet(a)?a.suspendedAction:null;
            a.Release();a.previous=a.action;a.action="resume";a.route=null;a.search=null;
            a.gate.Invalidate();a.until=Time.time+8;
            if(!string.IsNullOrEmpty(resume) && resume!="resume" && a.suspendedRemaining>0)
            {
                Vector3 target=a.suspendedGoal;float remaining=a.suspendedRemaining;a.suspendedAction=null;
                if(resume=="sleep" || resume=="follow" && Flat(a.root.position,player.position)<10)
                {Begin(a,new AnimalOption{action=resume},target);a.until=Time.time+remaining;return;}
            }
            Next(a);
        }
        void Failed(Animal a)
        {
            if(a.action=="shelter")a.failedShelters[a.goal]=Time.time+30;
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
        bool FindShelter(Animal a,out Vector3 point)
        {
            point=a.root.position;float best=18;bool found=false;
            foreach(LifeObject obj in world.Objects)
                if(obj && obj.State.sheltered && !obj.State.held)
                {
                    float d=Flat(a.root.position,obj.transform.position);
                    if(d<best && ShelterAvailable(a,obj.transform.position)){best=d;point=obj.transform.position;found=true;}
                }
            foreach(Shelter shelter in shelters)
            {
                if(!shelter.source || !shelter.source.gameObject.activeInHierarchy)continue;
                float d=Flat(a.root.position,shelter.Point);
                if(d<best && ShelterAvailable(a,shelter.Point)){best=d;point=shelter.Point;found=true;}
            }
            // Eight nearby roof probes also cover ordinary eaves that are not task objects.
            for(int i=0;i<8;i++)
            {
                float angle=i*Mathf.PI*.25f;Vector3 probe=a.root.position+new Vector3(Mathf.Cos(angle)*3,0,Mathf.Sin(angle)*3);
                int count=Physics.RaycastNonAlloc(probe+Vector3.up*8,Vector3.down,roofHits,7,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
                for(int h=0;h<count;h++)
                {
                    RaycastHit hit=roofHits[h];if(hit.collider is TerrainCollider || hit.collider.transform.IsChildOf(a.root) ||
                        hit.collider.GetComponentInParent<WildlifeAgent>() || hit.normal.y<.5f)continue;
                    float d=Flat(a.root.position,probe);if(d<best && ShelterAvailable(a,probe)){point=probe;best=d;found=true;}
                }
            }
            return found;
        }
        void OnDisable()
        {
            StopAllCoroutines();if(client)client.enabled=false;
            foreach(Animal a in animals){a.gate.Invalidate();if(a.Live)a.Release();if(a.glyph)a.glyph.Sleeping=false;}
        }
    }
}
