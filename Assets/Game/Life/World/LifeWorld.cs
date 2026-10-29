using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WeatherHousePrototype;
using WeatherHouse.Hillside;
using WeatherHouse.WildlifeJourney;

namespace WeatherHouse.Life
{
    public sealed partial class LifeWorld : MonoBehaviour
    {
        [Serializable] sealed class Accepted
        {
            public string id; public int baseline; public Vector3 player;
            public int stage; public bool weatherSeen;
            public float waterBaseline; public int castBaseline;
            public int localRain,localWind;
            public bool legacyObjectHistory;
            public List<ObjectBaseline> objects=new List<ObjectBaseline>();
        }
        [Serializable] sealed class ObjectBaseline {public string id;public int action;public float water;public bool collected,rainSoaked;}
        [Serializable] sealed class WorldSave
        {
            public List<LifeObjectState> objects=new List<LifeObjectState>();
            public List<Accepted> accepted=new List<Accepted>();
            public List<string> fed=new List<string>();
            public int serial,castSerial,rainCastSerial,windCastSerial,layoutVersion;
            public string commonRestId;
            public List<string> friendlyPresented=new List<string>();
            public List<BindingSave> animals=new List<BindingSave>();
        }
        [Serializable] sealed class BindingSave {public string key;public Vector3 target;public bool rest;}
        sealed class Binding
        {
            public LifeSpeaker speaker;public WildlifeAgent wildlife;public WildlifePopulation population;public JourneyHerd herd;public int member;
            public Vector3 home,target;public bool rest;public float lastRefresh;
            public Behaviour tailWave;public bool oldTailEnabled;
            public AnimationClip oldIdle,oldDown,oldSleep,oldUp;public bool changedClips;
            public bool weatherRetreat;public Vector3 priorTarget;
        }
        LifeSession owner;
        readonly Dictionary<string,LifeObject> objects=new Dictionary<string,LifeObject>();
        readonly HashSet<LifeObject> interactables=new HashSet<LifeObject>();
        readonly Dictionary<string,Vector3> anchors=new Dictionary<string,Vector3>();
        readonly Dictionary<string,Binding> bindings=new Dictionary<string,Binding>();
        readonly Dictionary<string,Accepted> accepted=new Dictionary<string,Accepted>();
        readonly List<string> fed=new List<string>();
        readonly HashSet<string> friendlyPresented=new HashSet<string>();
        readonly HashSet<string> invalidTargets=new HashSet<string>();
        LifeRequest[] requests=Array.Empty<LifeRequest>();
        WildlifeAgent[] secondary=Array.Empty<WildlifeAgent>();
        LifeObject held;
        int serial,castSerial,rainCastSerial,windCastSerial;
        string commonRestId="squirrel.common";
        float nextLifeTick,nextVisual;
        string craftingId;
        Vector3 craftStart;
        float craftEnd;
        Material markerMaterial;
        readonly List<LifeLabel> labels=new List<LifeLabel>();
        readonly List<WildlifeAgent> invited=new List<WildlifeAgent>();
        readonly List<Vector3> invitedTargets=new List<Vector3>();
        readonly Dictionary<WildlifeAgent,float> reactions=new Dictionary<WildlifeAgent,float>();
        AnimalAutonomy autonomy;
        Transform Player => owner && owner.Gameplay?owner.Gameplay.PlayerTransform:null;
        public IReadOnlyList<LifeRequest> Requests=>requests;
        public IReadOnlyCollection<LifeObject> Objects=>objects.Values;
        public string SetupWarning {get;private set;}="";

        public void Initialize(LifeSession session)
        {
            if(owner)return;owner=session;requests=LifeContent.Build();
            CreateCoastalActors();BindActors();FindSceneContainers();BuildStations();BindRemainingContainers();BindGatheringPlants();
            secondary=FindObjectsOfType<WildlifeAgent>().Where(a=>a.isActiveAndEnabled && a.locomotion==WildlifeLocomotion.Ground && !a.GetComponent<LifeSpeaker>()).ToArray();
            if(Application.isPlaying){autonomy=gameObject.AddComponent<AnimalAutonomy>();autonomy.Initialize(this,Player);}
        }
        public LifeSpeaker FindSpeaker(string key)=>!string.IsNullOrEmpty(key) && bindings.TryGetValue(key,out Binding b)?b.speaker:null;
        public Vector3 AnchorPosition(string id)
        {
            if(string.IsNullOrEmpty(id))return transform.position;
            LifeObject obj=ObjectAt(id);if(obj)return obj.transform.position;
            return anchors.TryGetValue(id,out Vector3 point)?point:transform.position;
        }
        public string Describe(LifeRequest request)
        {
            if(request==null)return "";
            LifeObject item=ObjectAt(request.targetId);string state=item?item.JudgementDescription:"固定指示点";
            LifeSpeaker s=FindSpeaker(request.speakerKey),l=FindSpeaker(request.listenerKey);
            return "说话者="+(s?s.Actor.displayName+" ["+s.Actor.animalId+"]":request.speakerKey)+
                "；听者="+(request.listenerKey=="player"?"玩家":l?l.Actor.displayName+" ["+l.Actor.animalId+"]":request.listenerKey)+
                "；指示位置="+AnchorPosition(request.targetId).ToString("F2")+"；"+state+"；"+request.context+"；"+DemonstrationDescription;
        }
        public string VisibleObjectState(LifeRequest request)
        {
            LifeObject obj=request==null?null:ObjectAt(request.targetId);string result=obj?obj.Description:"固定指示点";
            if(request!=null && request.id==demonstrationRequest && !string.IsNullOrEmpty(DemonstrationDescription))
                result+=" · "+VisibleDemonstration(demonstrationKind,demonstrationStrength,demonstrationWasActive);
            return result;
        }
        public void OnTaskAccepted(LifeRequest request)
        {
            if(request==null || !Player)return;
            accepted[request.id]=new Accepted{id=request.id,baseline=Revision(request.targetId,NeedsObjectAction(request)),player=Player.position,waterBaseline=BowlAt("squirrel.common.bowl"),castBaseline=castSerial};
            foreach(LifeObject obj in objects.Values)
                if(obj && (obj.State.kind==LifeObjectKind.Bowl || obj.State.kind==LifeObjectKind.Mat || obj.State.kind==LifeObjectKind.Rest))
                    accepted[request.id].objects.Add(new ObjectBaseline{id=obj.State.id,action=obj.State.actionRevision,water=obj.State.water});
            fed.Remove(request.id);
            if(request.condition=="animal_arrived") Invite(request.speakerKey,request.targetId);
        }
        static bool NeedsObjectAction(LifeRequest r)=>r.condition=="dry_mat"||r.condition=="warm_rest";
        public void RecordCast(string id,LocalWeather channel)
        {
            // A running weather effect is not a new act of helping after understanding.
            if(id!="rain" && id!="wind" && id!="weak" && id!="strong")return;
            if(channel==LocalWeather.Rain)rainCastSerial=++castSerial;
            else if(channel==LocalWeather.Wind)windCastSerial=++castSerial;
        }
        int Revision(string id,bool action=false)=>id!=null && objects.TryGetValue(id,out LifeObject o)?action?o.State.actionRevision:o.State.revision:0;
        public bool IsSatisfied(LifeRequest request)
        {
            if(request==null || !Player || !accepted.TryGetValue(request.id,out Accepted a))return false;
            if(IsLocationCondition(request.condition) && invalidTargets.Contains(request.targetId??""))return false;
            LifeObject obj=null;if(request.targetId!=null)objects.TryGetValue(request.targetId,out obj);
            bool changed=obj && (NeedsObjectAction(request)?obj.State.actionRevision:obj.State.revision)>a.baseline;
            switch(request.condition)
            {
                case "conversation":return true;
                case "rain_seen":return NearTaskAnimal(request) && a.weatherSeen && a.localRain>0;
                case "water_retained":return objects.Values.Any(o=>o && o.State.kind==LifeObjectKind.Bowl && NearTaskAnimal(request,o.transform.position) &&
                    o.State.water>=.3f && o.State.rainStrength==0 && CollectedAfter(a,o));
                case "dry_mat":return objects.Values.Any(o=>o && o.State.kind==LifeObjectKind.Mat && !o.State.held && o.State.IsDry &&
                    NearTaskAnimal(request,o.transform.position) && ActedAfter(a,o,request));
                case "fed":return fed.Contains(request.id);
                case "wind_weak":return NearTaskAnimal(request) && a.weatherSeen && a.localWind==1;
                case "rain_weak":return NearTaskAnimal(request) && a.weatherSeen && a.localRain==1;
                case "warm_rest":return objects.Values.Any(o=>o && o.State.kind==LifeObjectKind.Rest && o.State.IsWarm &&
                    NearTaskAnimal(request,o.transform.position) && DryMatAt(o.State.id) && ActedAfter(a,o,request) && AnimalAt(request.speakerKey,o.State.id));
                case "arrive":return Flat(Player.position,a.player)>.5f && Flat(Player.position,AnchorPosition(request.targetId))<1.15f;
                case "animal_arrived":return a.stage>0 && AnimalAt(request.speakerKey,request.targetId);
                case "walk_sequence":case "walk_reverse":return a.stage>=2;
                case "common_rest":return CommonReady(a,commonRestId) && AtInvitedRest()>=2;
                default:return false;
            }
        }
        float BowlAt(string id)=>objects.TryGetValue(id,out LifeObject o)?o.State.water:0;
        bool DryMatAt(string slot)=>objects.Values.Any(o=>o && o.State.kind==LifeObjectKind.Mat && !o.State.held && o.State.slot==slot && o.State.IsDry);
        bool AnimalAt(string key,string id)=>bindings.TryGetValue(key,out Binding b) && Flat(b.speaker.transform.position,AnchorPosition(id))<1.4f;

        void Update()
        {
            if(!owner || !Player)return;
            UpdateDemonstrations();
            if(held)
            {
                Transform visual=owner.Gameplay.PlayerVisualRoot;
                Vector3 forward=visual?visual.forward:Player.forward;
                held.transform.position=Player.position+Vector3.up*.85f+forward*.55f;
                held.transform.rotation=Quaternion.LookRotation(forward);
            }
            if(craftingId!=null)
            {
                if(!held || held.State.id!=craftingId || Flat(Player.position,craftStart)>1.1f)
                {craftingId=null;owner.SaveAndNotify("编垫已中断，草束仍在手中。");}
                else if(Time.time>=craftEnd)FinishCraft();
            }
            foreach(LifeObject obj in objects.Values)
            {
                if(!obj)continue;
                obj.State.position=obj.transform.position;
                if(obj.State.rainStrength==0) obj.State.wetness=Mathf.Max(0,obj.State.wetness-Time.deltaTime/240f);
            }
            if(Time.time>=nextVisual)
            {
                nextVisual=Time.time+.06f;
                foreach(LifeObject obj in objects.Values)if(obj)obj.RefreshVisual(Time.time);
                foreach(LifeLabel label in labels)if(label)label.Refresh(Player,owner.Gameplay.CurrentView,owner.Gameplay.PhotoRig && owner.Gameplay.PhotoRig.Active);
            }
            UpdateSequences();
            UpdatePhotoInteractions();
            UpdateFeeding();
            UpdateDrinks();
            if(Time.time<nextLifeTick)return;nextLifeTick=Time.time+.5f;
            foreach(Binding b in bindings.Values)
            {
                if(!b.speaker)continue;
                if(b.rest)
                {
                    LifeObject bed=objects.Values.FirstOrDefault(o=>o.State.kind==LifeObjectKind.Rest && Flat(o.transform.position,b.target)<.1f);
                    if(bed && bed.State.id!="fox.home" && (!DryMatAt(bed.State.id)||!bed.State.IsWarm))
                    {b.rest=false;b.target=b.home;}
                }
                bool hold=TryLifeStation(b,out Vector3 stationTarget);
                bool ok=!hold || (b.wildlife?b.wildlife.RequestLifeTarget(stationTarget,1.5f,b.rest,drinks.Any(d=>d.animal==b)?.045f:.22f):b.herd && b.herd.RequestLifeTarget(b.member,stationTarget,1.5f));
                if(!ok && b.speaker.Current!=null) b.speaker.Current.context="当前通路受阻，可以靠近并重新邀请；动物不会瞬移。";
                else if(b.speaker.Current!=null)b.speaker.Current.context="关键动物在附近生活点活动，可以再去看望它。";
                LifeRequest turn=b.speaker.Current;
                LifeSpeaker listener=turn==null?null:FindSpeaker(turn.listenerKey);
                Vector3 look=listener?listener.transform.position:Player.position;
                if(!listener && turn!=null && (turn.eventId=="E3"||turn.eventId=="E4"||turn.eventId=="E6b"))look=AnchorPosition(turn.targetId);
                if(hold && b.wildlife && b.wildlife.ActualSpeed<.05f && !drinks.Any(d=>d.animal==b) && Flat(b.speaker.transform.position,Player.position)<8)b.wildlife.FaceLifePoint(look,.5f);
                if(b.herd && Flat(b.speaker.transform.position,Player.position)<8)b.herd.FaceLifePoint(b.member,look,.5f);
                if(owner.Progress.friendlyAnimals.Contains(b.speaker.Actor.animalId) && friendlyPresented.Add(b.speaker.Actor.animalId))
                {
                    if(b.wildlife)b.wildlife.RequestLifeAction(b.wildlife.clips.greet,2);
                    if(b.herd)b.herd.RequestLifeGreeting(b.member);
                }
            }
            if(!DryMatAt(commonRestId) || CommonWater(commonRestId)<.12f)
            {
                foreach(WildlifeAgent animal in invited)if(animal)animal.ReleaseLifeTarget();
                invited.Clear();invitedTargets.Clear();
                if(bindings.TryGetValue("companion",out Binding companion)){companion.rest=false;companion.target=companion.home;}
            }
            for(int i=0;i<invited.Count;i++)if(invited[i] && !drinks.Any(d=>d.animal.wildlife==invited[i]))invited[i].RequestLifeTarget(invitedTargets[i],1.5f,true);
        }
        void UpdateSequences()
        {
            foreach(Accepted a in accepted.Values)
            {
                LifeRequest r=Array.Find(requests,v=>v.id==a.id);if(r==null)continue;
                if(r.condition=="animal_arrived" && Flat(Player.position,a.player)>.25f)a.stage=1;
                if(r.condition!="walk_sequence" && r.condition!="walk_reverse")continue;
                string first=r.condition=="walk_sequence"?"fox.here":"fox.tree";
                string last=r.condition=="walk_sequence"?"fox.tree":"fox.here";
                if(a.stage==0 && Flat(Player.position,a.player)>.5f && Flat(Player.position,AnchorPosition(first))<1.15f)a.stage=1;
                else if(a.stage==1 && Flat(Player.position,AnchorPosition(last))<1.15f)a.stage=2;
            }
        }
        public void ApplyWeather(Vector3 center,float radius,LocalWeather kind,int strength,bool active,float deltaTime)
        {
            if(!Player || Flat(center,Player.position)>.1f || radius<=0 || deltaTime<=0)return;
            if(autonomy && kind!=LocalWeather.Wind)autonomy.Rain(center,radius,active && kind==LocalWeather.Rain && strength>=2);
            if(active && demonstrationKey!=null && kind==demonstrationKind)StopDemonstration();
            if(kind==LocalWeather.Wind)playerWindActive=active;else playerSkyActive=active;
            foreach(Accepted a in accepted.Values)
            {
                LifeRequest r=Array.Find(requests,v=>v.id==a.id);if(r==null)continue;
                LifeSpeaker speaker=FindSpeaker(r.speakerKey);
                bool inside=speaker && InPlayerWeatherRange(Player.position,center,speaker.transform.position,radius);
                if(kind==LocalWeather.Wind)a.localWind=active && inside?strength:0;
                else a.localRain=kind==LocalWeather.Rain && active && inside?strength:0;
                bool matching=kind==LocalWeather.Wind?r.condition=="wind_weak":kind==LocalWeather.Rain && (r.condition=="rain_seen"||r.condition=="rain_weak");
                if(active && inside && matching && (kind==LocalWeather.Wind?windCastSerial:rainCastSerial)>a.castBaseline)a.weatherSeen=true;
            }
            foreach(LifeObject obj in objects.Values)
            {
                if(!obj)continue;
                bool inside=InPlayerWeatherRange(Player.position,center,obj.transform.position,radius);
                float beforeWater=obj.State.water;
                obj.State.ApplyWeather(kind,strength,active && inside,deltaTime);
                if(inside && active)
                {
                    obj.State.revision++;
                    foreach(Accepted a in accepted.Values)
                    {
                        LifeRequest r=Array.Find(requests,v=>v.id==a.id);
                        bool castAfter=(kind==LocalWeather.Wind?windCastSerial:rainCastSerial)>a.castBaseline;
                        if(castAfter && r!=null && r.condition=="water_retained" && kind==LocalWeather.Rain &&
                            obj.State.kind==LifeObjectKind.Bowl && obj.State.water>beforeWater && NearTaskAnimal(r,obj.transform.position))
                        {
                            ObjectBaseline baseline=a.objects.Find(v=>v.id==obj.State.id);
                            if(baseline==null){baseline=new ObjectBaseline{id=obj.State.id};a.objects.Add(baseline);}baseline.collected=true;
                        }
                        if(castAfter && r!=null && r.targetId==obj.State.id &&
                            ((r.condition=="wind_weak" && kind==LocalWeather.Wind) || (r.condition!="wind_weak" && kind==LocalWeather.Rain)) &&
                            (r.condition!="water_retained" || obj.State.water>beforeWater))a.weatherSeen=true;
                        if(castAfter && r!=null && r.condition=="common_rest" && kind==LocalWeather.Rain &&
                            (obj.State.kind==LifeObjectKind.Bowl || obj.State.kind==LifeObjectKind.Mat) && NearTaskAnimal(r,obj.transform.position))
                        {
                            ObjectBaseline baseline=a.objects.Find(v=>v.id==obj.State.id);
                            if(baseline==null){baseline=new ObjectBaseline{id=obj.State.id,water=beforeWater};a.objects.Add(baseline);}
                            if(obj.State.kind==LifeObjectKind.Bowl && obj.State.water>beforeWater && obj.State.water>=Mathf.Min(.95f,baseline.water+.08f))baseline.collected=true;
                            if(obj.State.kind==LifeObjectKind.Mat && !obj.State.held && obj.State.wetness>.25f)baseline.rainSoaked=true;
                        }
                    }
                }
            }
            if(active)ReactSecondary(center,radius,kind,strength);
            if(kind==LocalWeather.Wind && bindings.TryGetValue("sheep",out Binding sheep))
            {
                bool exposed=active && strength==2 && InPlayerWeatherRange(Player.position,center,sheep.speaker.transform.position,radius);
                if(exposed && !sheep.rest && !sheep.weatherRetreat && objects.ContainsKey("sheep.warm"))
                {sheep.priorTarget=sheep.target;sheep.target=AnchorPosition("sheep.warm");sheep.weatherRetreat=true;}
                else if(!exposed && sheep.weatherRetreat){sheep.target=sheep.priorTarget;sheep.weatherRetreat=false;}
            }
        }
        void ReactSecondary(Vector3 center,float radius,LocalWeather kind,int strength)
        {
            if(autonomy && kind==LocalWeather.Rain)return;
            foreach(WildlifeAgent animal in secondary)
            {
                if(!animal || !animal.isActiveAndEnabled || Flat(animal.transform.position,center)>radius || invited.Contains(animal))continue;
                if(reactions.TryGetValue(animal,out float until) && Time.time<until)continue;
                reactions[animal]=Time.time+5;
                bool likes=animal.speciesId=="deer"?kind==LocalWeather.Rain&&strength==1:
                    animal.speciesId=="rabbit"?kind==LocalWeather.Sun:animal.spawnId%2==0&&kind==LocalWeather.Wind&&strength==1;
                if(likes)
                {
                    if(animal.SolitaryRoutine!=null){animal.SolitaryRoutine.Hunger=.8f;animal.SolitaryRoutine.Interrupt(false);}
                    continue;
                }
                if(kind==LocalWeather.Rain || kind==LocalWeather.Wind&&strength==2)
                {
                    LifeObject shelter=objects.Values.Where(o=>o && o.State.sheltered && Flat(o.transform.position,animal.transform.position)<6)
                        .OrderBy(o=>Flat(o.transform.position,animal.transform.position)).FirstOrDefault();
                    if(shelter)animal.RequestLifeTarget(shelter.transform.position,3,true);
                    else if(animal.SolitaryRoutine!=null){animal.SolitaryRoutine.Fatigue=.9f;animal.SolitaryRoutine.Interrupt(true);}
                }
            }
        }
        int AtInvitedRest()
        {
            var resting=new HashSet<WildlifeAgent>();
            for(int i=0;i<invited.Count;i++)if(IsRestingAt(invited[i],invitedTargets[i]))resting.Add(invited[i]);
            if(bindings.TryGetValue("squirrel",out Binding main) && main.rest && IsRestingAt(main.wildlife,AnchorPosition(commonRestId)))resting.Add(main.wildlife);
            return resting.Count;
        }
        static bool IsRestingAt(WildlifeAgent animal,Vector3 target)=>animal && animal.HasLifeTarget &&
            Flat(animal.transform.position,target)<.35f && animal.ActualSpeed<.05f && animal.State==WildlifeState.Idle &&
            (!animal.clips.HasRestSequence || animal.RestPhase==WildlifeRestPhase.Sleeping);
        public string CaptureJson()
        {
            var data=new WorldSave {layoutVersion=2,commonRestId=commonRestId,serial=serial,castSerial=castSerial,rainCastSerial=rainCastSerial,windCastSerial=windCastSerial,accepted=accepted.Values.ToList(),fed=fed.ToList(),friendlyPresented=friendlyPresented.ToList()};
            foreach(LifeObject obj in objects.Values)if(obj){obj.State.position=obj.transform.position;data.objects.Add(obj.State);}
            foreach(var pair in bindings)
            {
                Drink pending=drinks.FirstOrDefault(d=>d.animal==pair.Value);
                data.animals.Add(new BindingSave{key=pair.Key,target=pending!=null?pending.returnTarget:pair.Value.target,rest=pending!=null?pending.returnRest:pair.Value.rest});
            }
            return JsonUtility.ToJson(data);
        }
        public void RestoreJson(string json)
        {
            if(string.IsNullOrEmpty(json))return;
            WorldSave data;try{data=JsonUtility.FromJson<WorldSave>(json);}catch(ArgumentException){return;}
            if(data==null)return;serial=Mathf.Max(0,data.serial);castSerial=Mathf.Max(0,data.castSerial);
            commonRestId=string.IsNullOrEmpty(data.commonRestId)?"squirrel.common":data.commonRestId;
            var relocations=new List<(Vector3 oldPosition,Vector3 position)>();
            foreach(LifeObjectState state in data.objects??new List<LifeObjectState>())
                if(state!=null && state.kind!=LifeObjectKind.Bundle && state.kind!=LifeObjectKind.Mat &&
                    objects.TryGetValue(state.id??"",out LifeObject station) && station)
                    relocations.Add((state.position,station.transform.position));
            CancelFeed();CancelDrinks();
            foreach(WildlifeAgent animal in invited)if(animal)animal.ReleaseLifeTarget();
            invited.Clear();invitedTargets.Clear();held=null;
            rainCastSerial=Mathf.Clamp(data.rainCastSerial,0,castSerial);windCastSerial=Mathf.Clamp(data.windCastSerial,0,castSerial);
            foreach(LifeObjectState state in data.objects??new List<LifeObjectState>())
            {
                if(state==null || string.IsNullOrEmpty(state.id) || !Finite(state.position) || !Enum.IsDefined(typeof(LifeObjectKind),state.kind))continue;
                state.water=Mathf.Clamp01(state.water);state.wetness=Mathf.Clamp01(state.wetness);
                state.rainStrength=state.windStrength=0;
                if(objects.TryGetValue(state.id,out LifeObject current))
                {
                    if(data.layoutVersion<2 && state.id=="squirrel.dry" && state.actionRevision==0 && !state.held && string.IsNullOrEmpty(state.slot))
                        state.position=current.transform.position;
                    if(state.kind!=LifeObjectKind.Bundle && state.kind!=LifeObjectKind.Mat && !state.id.StartsWith("rest.",StringComparison.Ordinal))state.position=current.transform.position;
                    current.State=state;current.transform.position=state.position;
                }
                else if(state.kind==LifeObjectKind.Bundle || state.kind==LifeObjectKind.Mat)current=CreateItem(state);
                else if(state.kind==LifeObjectKind.Rest && state.id.StartsWith("rest.",StringComparison.Ordinal))
                {current=Station(state.id,"草垫休息处",LifeObjectKind.Rest,state.position);current.State=state;}
                if(state.held && current && !held){held=current;}
                else if(state.held)state.held=false;
            }
            foreach(LifeObject item in objects.Values)
                if(item && item.State.kind==LifeObjectKind.Mat && !item.State.held && !string.IsNullOrEmpty(item.State.slot) && objects.TryGetValue(item.State.slot,out LifeObject station))
                    item.State.position=item.transform.position=station.transform.position+Vector3.up*.045f;
            accepted.Clear();foreach(Accepted a in data.accepted??new List<Accepted>())if(a!=null && Array.Exists(requests,r=>r.id==a.id))
            {
                if(a.objects==null || a.objects.Count==0)
                {
                    a.objects=new List<ObjectBaseline>();a.legacyObjectHistory=true;LifeRequest request=Array.Find(requests,r=>r.id==a.id);
                    foreach(LifeObject obj in objects.Values)
                        if(obj && (obj.State.kind==LifeObjectKind.Bowl || obj.State.kind==LifeObjectKind.Mat || obj.State.kind==LifeObjectKind.Rest))
                            a.objects.Add(new ObjectBaseline{id=obj.State.id,action=obj.State.actionRevision,
                                collected=request.condition=="water_retained" && request.targetId==obj.State.id && a.weatherSeen});
                }
                a.localRain=a.localWind=0;accepted[a.id]=a;
            }
            fed.Clear();fed.AddRange(data.fed??new List<string>());
            friendlyPresented.Clear();foreach(string id in data.friendlyPresented??new List<string>())friendlyPresented.Add(id);
            foreach(BindingSave state in data.animals??new List<BindingSave>())
                if(state!=null && Finite(state.target) && bindings.TryGetValue(state.key??"",out Binding binding) && Flat(binding.home,state.target)<=8)
                {
                    Vector3 target=state.target;bool mapped=false;
                    foreach(var move in relocations)if(Flat(state.target,move.oldPosition)<.1f){target=move.position;mapped=true;break;}
                    if(state.key=="companion" && state.rest && commonRestId=="squirrel.common")target=AnchorPosition("squirrel.common.companion");
                    else if(data.layoutVersion==0 && !mapped && !state.rest)target=binding.home;
                    binding.target=target;binding.rest=state.rest;
                    if(state.key=="companion" && state.rest && binding.wildlife){invited.Add(binding.wildlife);invitedTargets.Add(target);}
                    // Restoring is synchronous: do not expose a frame without a lease to the rest animator.
                    if(binding.wildlife)binding.wildlife.RequestLifeTarget(binding.target,1.5f,binding.rest);
                    if(binding.herd)binding.herd.RequestLifeTarget(binding.member,binding.target,1.5f);
                }
        }
        static bool Finite(Vector3 v)=>!float.IsNaN(v.x)&&!float.IsInfinity(v.x)&&!float.IsNaN(v.y)&&!float.IsInfinity(v.y)&&!float.IsNaN(v.z)&&!float.IsInfinity(v.z);
        public static bool InPlayerWeatherRange(Vector3 player,Vector3 center,Vector3 item,float radius)=>
            Finite(player)&&Finite(center)&&Finite(item)&&radius>0&&Flat(player,center)<=.1f&&Flat(center,item)<=radius;
        public static float Flat(Vector3 a,Vector3 b)=>new Vector2(a.x-b.x,a.z-b.z).magnitude;
        void OnDestroy()
        {
            UnbindGatheringPlants();
            UnbindSceneContainers();
            CancelFeed();CancelDrinks();
            foreach(Binding b in bindings.Values)
            {
                if(b.wildlife)
                {
                    b.wildlife.ReleaseLifeTarget();
                    if(b.changedClips){b.wildlife.clips.idle=b.oldIdle;b.wildlife.clips.restDown=b.oldDown;b.wildlife.clips.restSleep=b.oldSleep;b.wildlife.clips.restUp=b.oldUp;}
                }
                if(b.tailWave)b.tailWave.enabled=b.oldTailEnabled;
                if(b.herd)b.herd.ReleaseLifeTarget(b.member);
            }
            foreach(WildlifeAgent a in invited)if(a)a.ReleaseLifeTarget();
            if(coastalRoot)Destroy(coastalRoot);
            if(markerMaterial)Destroy(markerMaterial);
        }
    }
}
