using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.WildlifeJourney;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    // Root review owns Play mode. This runner advances only while that scene is already playing.
    [InitializeOnLoad]
    public static class LifeWorldAsyncChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        const string Folder="LifeGameplayReview/20261026/play";
        [Serializable] sealed class Report
        {
            public string status="RUNNING",utc,error;
            public string boundary="Isolated-save world and animation integration. Player fixture positions are arranged; animals move only through their original population, bodies and collision. Weather is invoked through the local-weather controller. A declared already-understood E7 help fixture enters LifeProgress.Accept and World.OnTaskAccepted before crafting; its active-help lease holds the main animal at its current position, with no claimed AI service judgement. Audio focus input is set true for consumption checks then restored; counts prove dispatched audio requests, not human listening. Bone contact and animation weights supplement, not replace, human visual acceptance.";
            public List<string> checks=new List<string>();
            public List<string> observations=new List<string>();
            public List<DrinkObservation> drinks=new List<DrinkObservation>();
            public List<string> drinkCandidates=new List<string>();
            public List<string> audioObservations=new List<string>();
            public List<string> photoFailures=new List<string>();
            public List<string> drinkStartTransitions=new List<string>();
            public string[] animalIds;
            public float seconds,maximumFrameDistance,firstTravel,secondTravel;
            public Vector3 mouthAtDrink,waterAtDrink;
        }
        [Serializable] sealed class DrinkObservation
        {
            public string animal,state,restPhase;
            public float seconds,distance,rotationError,speed,started,remaining,skinDistance,clipWeight,scale;
            public bool ready,lease,active,consumed,touching,canTurn,routeClear,approaching,planning;
            public Vector3 position,target,controllerTarget,stand,mouth,water,bodyCenter,bodyHalf;
        }
        static IEnumerator runner;
        static LifeSession life;
        static Report report;
        static float started,lastTime,nextObservation;
        static WildlifeAgent main,companion;
        static Vector3 mainBefore,companionBefore,mainLast,companionLast;
        static bool track;
        static string lastAudioState;
        public static bool Running=>runner!=null;
        public static string LastStatus {get;private set;}
        static LifeWorldAsyncChecks()
        {
            EditorApplication.update+=Tick;
            EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.ExitingPlayMode)Cancel();};
        }
        public static void Begin(LifeSession session)
        {
            if(Running)throw new InvalidOperationException("World animation review is already running.");
            string save=Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE");
            if(!EditorApplication.isPlaying || !session || !session.World || string.IsNullOrEmpty(save) ||
                !string.Equals(Path.GetFullPath(save),Path.GetFullPath(GameplaySession.PersistenceDirectory),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("World animation review requires the root review's isolated Play save.");
            life=session;report=new Report{utc=DateTime.UtcNow.ToString("O")};started=lastTime=nextObservation=Time.time;track=false;
            LifePhotoWorldChecks.WriteWorldLayout(life);
            lastAudioState=null;LastStatus="RUNNING";runner=Run();Write();
        }
        public static void Cancel(){if(Running)Finish("INTERRUPTED","Review cancelled; original state and movement targets restored.");}
        static void Tick()
        {
            if(!Running || !EditorApplication.isPlaying || EditorApplication.isPaused)return;
            try
            {
                if(Input.GetKeyDown(KeyCode.F))throw new InvalidOperationException("External keyboard F input interrupted the isolated world review; this run cannot prove an uncontaminated scripted interaction sequence.");
                ObserveAudio("state",false);
                if(Time.time-started>120)throw new InvalidOperationException("World animation review exceeded 120 seconds.");
                if(main && companion && Time.time>=nextObservation){Observe();nextObservation=Time.time+1;Write();}
                if(track && Time.time>lastTime)
                {
                    float distance=Mathf.Max(LifeWorld.Flat(main.transform.position,mainLast),LifeWorld.Flat(companion.transform.position,companionLast));
                    report.maximumFrameDistance=Mathf.Max(report.maximumFrameDistance,distance);
                    if(distance>Mathf.Max(.12f,(Time.time-lastTime)*1.1f))throw new InvalidOperationException("Animal exceeded its walking displacement bound.");
                    mainLast=main.transform.position;companionLast=companion.transform.position;lastTime=Time.time;
                }
                if(!runner.MoveNext())Finish(report.photoFailures.Count==0?"PASS":"FAIL",report.photoFailures.Count==0?null:string.Join("\n",report.photoFailures));
            }
            catch(Exception e){Finish("FAIL",e.ToString());}
        }
        static void Finish(string status,string error)
        {
            IEnumerator ending=runner;runner=null;track=false;
            if(main && companion)Observe();
            try{(ending as IDisposable)?.Dispose();}catch(Exception e){status="FAIL";error+="\nCleanup: "+e;}
            report.status=LastStatus=status;report.error=error;report.seconds=Time.time-started;Write();
        }
        static void Write(){Directory.CreateDirectory(Folder);File.WriteAllText(Folder+"/world-animation.json",JsonUtility.ToJson(report,true));}
        // Read gates only; never invoke contact or physics queries here or change production timing.
        static void TraceDrinkStart(IList active,ref string previous)
        {
            var states=new List<string>();var details=new List<string>();
            foreach(object drink in active)
            {
                Type type=drink.GetType();object binding=type.GetField("animal").GetValue(drink);
                var actor=(WildlifeAgent)binding.GetType().GetField("wildlife").GetValue(binding);
                var speaker=(LifeSpeaker)binding.GetType().GetField("speaker").GetValue(binding);
                Vector3 target=(Vector3)binding.GetType().GetField("target").GetValue(binding);
                Quaternion facing=(Quaternion)type.GetField("facing").GetValue(drink);
                float start=(float)type.GetField("started").GetValue(drink),restart=(float)type.GetField("restartAfter").GetValue(drink);
                float distance=LifeWorld.Flat(actor.transform.position,target),angle=Quaternion.Angle(actor.transform.rotation,facing);
                bool latePartner=active.Cast<object>().Any(other=>other!=drink &&
                    (float)other.GetType().GetField("started").GetValue(other)>=0 &&
                    Time.time-(float)other.GetType().GetField("started").GetValue(other)>.05f);
                string state=speaker.Actor.animalId+" planning="+type.GetField("planning").GetValue(drink)+" approaching="+type.GetField("approaching").GetValue(drink)+
                    " ready="+typeof(LifeWorld).GetMethod("ReadyToDrink",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{drink})+
                    " lease="+actor.HasLifeTarget+" state="+actor.State+" rest="+actor.RestPhase+" distanceOK="+(distance<.045f)+
                    " speedOK="+(actor.ActualSpeed<=.05f)+" facingOK="+(angle<=5)+" started="+(start>=0)+
                    " restartPending="+(Time.time<restart)+" latePartner="+latePartner+" sharedRounds="+type.GetField("sharedRounds").GetValue(drink);
                states.Add(state);
                details.Add(state+" startTime="+start.ToString("R")+" age="+(start>=0?(Time.time-start).ToString("R"):"not-started")+
                    " distance="+distance.ToString("R")+" angle="+angle.ToString("R")+" speed="+actor.ActualSpeed.ToString("R")+
                    " position="+actor.transform.position.ToString("F6")+" target="+target.ToString("F6")+" stand="+((Vector3)type.GetField("stand").GetValue(drink)).ToString("F6"));
            }
            string signature=string.Join(" | ",states);if(signature==previous)return;previous=signature;
            report.drinkStartTransitions.Add("frame="+Time.frameCount+" time="+Time.time.ToString("R")+" "+string.Join(" | ",details));
        }
        static void ObserveAudio(string stage,bool force=true)
        {
            LifeAudio audio=life?life.GetComponent<LifeAudio>():null;
            if(!audio)return;
            var cooldown=Get<Dictionary<LifeAudioCue,float>>(audio,"nextCue");
            cooldown.TryGetValue(LifeAudioCue.Drink,out float due);
            cooldown.TryGetValue(LifeAudioCue.Fiber,out float fiberDue);
            string state="count="+audio.Count(LifeAudioCue.Drink)+" fiberCount="+audio.Count(LifeAudioCue.Fiber)+" focus="+Get<bool>(audio,"focused")+
                " appFocus="+Application.isFocused+" ready="+Get<bool>(audio,"ready")+" enabled="+audio.isActiveAndEnabled+
                " player="+(bool)Get<Transform>(audio,"player")+" listenerPause="+AudioListener.pause+" listenerVolume="+AudioListener.volume;
            if(force || state!=lastAudioState)report.audioObservations.Add((Time.time-started).ToString("F3")+"s "+stage+" "+state+
                " unscaled="+Time.unscaledTime.ToString("F3")+" nextDrink="+due.ToString("F3")+" nextFiber="+fiberDue.ToString("F3"));
            lastAudioState=state;
        }
        static void Observe()
        {
            string line=(Time.time-started).ToString("F2")+"s main="+main.transform.position.ToString("F3")+" "+main.RestPhase+
                "; companion="+companion.transform.position.ToString("F3")+" "+companion.RestPhase;
            if(life && life.World)
            {
                line+="; feedback="+life.Gameplay.Status+"; notebookSaveError="+life.Gameplay.LastSaveError+"; lifeSaveError="+life.LastSaveError;
                IList pending=Get<IList>(life.World,"drinks");
                report.drinkCandidates.Clear();foreach(string candidate in Get<IList>(life.World,"drinkCandidates"))report.drinkCandidates.Add(candidate);
                foreach(object drink in pending)
                {
                    Transform bone=(Transform)drink.GetType().GetField("mouthBone").GetValue(drink);
                    Vector3 local=(Vector3)typeof(LifeWorld).GetField("DrinkMouthLocal",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                    var bowl=(LifeObject)drink.GetType().GetField("bowl").GetValue(drink);
                    object binding=drink.GetType().GetField("animal").GetValue(drink);
                    var actor=(WildlifeAgent)binding.GetType().GetField("wildlife").GetValue(binding);
                    Vector3 target=(Vector3)binding.GetType().GetField("target").GetValue(binding);
                    Quaternion facing=(Quaternion)drink.GetType().GetField("facing").GetValue(drink);
                    var population=Get<WildlifePopulation>(actor,"population");
                    report.drinks.Add(new DrinkObservation{animal=actor.name,seconds=Time.time-started,state=actor.State.ToString(),restPhase=actor.RestPhase.ToString(),
                        position=actor.transform.position,target=target,controllerTarget=Get<Vector3>(actor,"lifeTarget"),distance=LifeWorld.Flat(actor.transform.position,target),
                        rotationError=Quaternion.Angle(actor.transform.rotation,facing),speed=actor.ActualSpeed,active=actor.isActiveAndEnabled,lease=actor.HasLifeTarget,
                        ready=(bool)typeof(LifeWorld).GetMethod("ReadyToDrink",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new[]{drink}),
                        started=(float)drink.GetType().GetField("started").GetValue(drink),remaining=(float)drink.GetType().GetField("expires").GetValue(drink)-Time.time,
                        consumed=(bool)drink.GetType().GetField("consumed").GetValue(drink),touching=(bool)drink.GetType().GetField("touching").GetValue(drink),
                        approaching=(bool)drink.GetType().GetField("approaching").GetValue(drink),stand=(Vector3)drink.GetType().GetField("stand").GetValue(drink),
                        planning=(bool)drink.GetType().GetField("planning").GetValue(drink),
                        skinDistance=(float)drink.GetType().GetField("skinDistance").GetValue(drink),mouth=bone.TransformPoint(local),water=bowl.waterVisual.position,
                        clipWeight=Weight(actor,(AnimationClip)drink.GetType().GetField("clip").GetValue(drink)),scale=actor.ScaleFactor,
                        bodyCenter=actor.BodyEnvelope.center,bodyHalf=actor.BodyEnvelope.half,
                        canTurn=population.CanRotate(actor,facing),routeClear=population.TryMove(actor,target,out _)});
                    line+="; mouth="+bone.TransformPoint(local).ToString("F3")+" water="+bowl.waterVisual.position.ToString("F3")+
                        "; skinDistance="+drink.GetType().GetField("skinDistance").GetValue(drink)+" consumed="+drink.GetType().GetField("consumed").GetValue(drink);
                }
            }
            report.observations.Add(line);
        }
        static IEnumerator Run()
        {
            LifeWorld world=life.World;Transform player=life.Gameplay.PlayerTransform;
            LifeAudio audio=life.GetComponent<LifeAudio>();
            bool audioFocus=audio && Get<bool>(audio,"focused");
            Vector3 position=player.position;Quaternion rotation=player.rotation;
            string worldBefore=world.CaptureJson(),progressBefore=JsonUtility.ToJson(life.Progress),statusBefore=life.Gameplay.Status;
            byte[] saveBefore=File.Exists(life.SavePath)?File.ReadAllBytes(life.SavePath):null;
            var ids=new HashSet<string>(world.Objects.Select(o=>o.State.id));
            var colliders=UnityEngine.Object.FindObjectsOfType<AnimalActor>().SelectMany(a=>a.GetComponentsInChildren<Collider>(true)).Distinct().ToDictionary(c=>c,c=>c.enabled);
            LifeLocalWeather weather=Get<LifeLocalWeather>(life,"weather");
            var weatherState=weather.GetType().GetFields(Hidden).Where(f=>f.FieldType.IsValueType).ToDictionary(f=>f,f=>f.GetValue(weather));
            try
            {
                weather.Cast("clear",out _);
                main=world.FindSpeaker("squirrel").GetComponent<WildlifeAgent>();companion=world.FindSpeaker("companion").GetComponent<WildlifeAgent>();
                report.animalIds=new[]{world.FindSpeaker("squirrel").Actor.animalId,world.FindSpeaker("companion").Actor.animalId};
                Check(main && companion && main!=companion && report.animalIds.Distinct().Count()==2,"E7 uses two distinct existing stable animals");
                Check(main.clips.HasRestSequence && companion.clips.HasRestSequence,"Both squirrels bind the real lie/rest/rise clips");
                LifeObject source=Find(world,"squirrel.grass"),bed=Find(world,"squirrel.common"),bowl=Find(world,"squirrel.common.bowl");
                LifeRequest sequence=world.Requests.First(r=>r.id=="squirrel.sequence");
                bowl.State.water=0; // Declared empty-bowl fixture precedes the real acceptance baseline.
                Check(life.Progress.Accept(sequence,report.animalIds[0],new JudgementResult{success=true,understood=true},true,true),
                    "Declared already-understood E7 help enters the real active-task gate before crafting and weather waits");
                world.OnTaskAccepted(sequence);
                Check(Get<LifeObject>(world,"held")==null,"Async fixture begins with empty hands");
                player.position=source.transform.position;world.TryGather();
                player.position=world.AnchorPosition("squirrel.loom");world.Interact();
                float until=Time.time+4;
                while(Get<string>(world,"craftingId")!=null){Deadline(until,"Real 1.6-second crafting did not finish");yield return null;}
                LifeObject mat=Get<LifeObject>(world,"held");Check(mat && mat.State.kind==LifeObjectKind.Mat,"Actual elapsed crafting produces the mat without FinishCraft reflection");
                player.position=bed.transform.position;world.Interact();
                Check(mat.State.slot==bed.State.id,"Actual F places the crafted mat at the communal bed");
                mat.State.wetness=0; // Declared dry-mat fixture, then real weather advances over frames.
                world.RecordCast("rain",LocalWeather.Rain);weather.Cast("rain",out _);until=Time.time+9;
                while(bowl.State.water<.55f || mat.State.wetness<.3f){Deadline(until,"Rain did not fill and wet the local fixture");yield return null;}
                Check(!world.IsSatisfied(sequence),"Rain-filled bowl and wet mat alone do not complete E7");
                weather.Cast("sunlight",out _);until=Time.time+22;
                while(!mat.State.IsDry){Deadline(until,"Local sun did not dry the communal mat");yield return null;}
                Check(bowl.State.water>=.3f && bowl.State.rainStrength==0,"Actual rain then sun retains water and dries the mat");
                Check(LifeWorldPlayChecks.PlaceForInteraction(life,bed,main,companion),"E7 invitation has an accessible nearby player standing point");
                mainBefore=mainLast=main.transform.position;companionBefore=companionLast=companion.transform.position;lastTime=Time.time;track=true;
                world.Interact();
                IList invited=Get<IList>(world,"invited");
                Check(invited.Count==1 && invited[0] as WildlifeAgent==companion,"Communal invitation prioritizes the stable companion over wandering secondary animals");
                Check(!world.IsSatisfied(sequence),"Invitation does not complete E7 before both animals arrive and rest");
                bool downMain=false,downCompanion=false;until=Time.time+30;
                while(!world.IsSatisfied(sequence))
                {
                    downMain|=main.RestPhase==WildlifeRestPhase.LyingDown;downCompanion|=companion.RestPhase==WildlifeRestPhase.LyingDown;
                    Deadline(until,"Two invited squirrels did not reach the communal bed and settle");yield return null;
                }
                report.firstTravel=LifeWorld.Flat(mainBefore,main.transform.position);report.secondTravel=LifeWorld.Flat(companionBefore,companion.transform.position);
                Check(report.firstTravel>.3f && report.secondTravel>.3f,"Both animals actually walked from their original positions");
                Check(downMain && downCompanion && main.RestPhase==WildlifeRestPhase.Sleeping && companion.RestPhase==WildlifeRestPhase.Sleeping,"Both real rest sequences pass through lying down into the rest loop");
                until=Time.time+3;while(Weight(main,main.clips.restSleep)<.8f || Weight(companion,companion.clips.restSleep)<.8f){Deadline(until,"Rest clips did not gain mixer weight");yield return null;}
                Check(Weight(main,main.clips.restSleep)>.5f && Weight(companion,companion.clips.restSleep)>.5f,"Both imported rest loops carry real animation mixer weight");
                PhotoCheck(()=>LifePhotoWorldChecks.CheckSharedRest(life,world.FindSpeaker("squirrel").Actor,world.FindSpeaker("companion").Actor,Check));
                ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder+"/05-two-resting-squirrels.png"));
                string rested=world.CaptureJson();world.RestoreJson(rested);world.RestoreJson(rested);
                Check(Get<IList>(world,"invited").Count==1 && Get<IList>(world,"invitedTargets").Count==1,"Restoring the same resting state twice keeps one companion invitation");
                Check(main.HasLifeTarget && companion.HasLifeTarget && main.RestPhase==WildlifeRestPhase.Sleeping && companion.RestPhase==WildlifeRestPhase.Sleeping,
                    "Restoring renews both actual leases synchronously without an artificial rise transition");
                until=Time.time+4;while(!world.IsSatisfied(sequence)){Deadline(until,"Restored invitations did not renew normally");yield return null;}
                object mainBinding=Get<IDictionary>(world,"bindings")["squirrel"];
                Get<IList>(world,"invited").Add(companion);Get<IList>(world,"invitedTargets").Add(Get<IList>(world,"invitedTargets")[0]);
                mainBinding.GetType().GetField("rest").SetValue(mainBinding,false);
                Check((int)Call(world,"AtInvitedRest")==1,"Duplicate references cannot make one animal count as two resting animals");
                world.RestoreJson(rested);
                Check(LifeWorldPlayChecks.PlaceForInteraction(life,bowl),"Drinking uses a real selectable bowl interaction");
                Check(audio,"World audio is installed for actual consumption checks");
                ObserveAudio("before isolated focus fixture");
                // Match the separate audio runner's controlled focus input; this proves requests, not a human listening test.
                Call(audio,"OnApplicationFocus",true);ObserveAudio("isolated focus fixture");
                int drinkSounds=audio.Count(LifeAudioCue.Drink);
                float waterBefore=bowl.State.water;world.Interact();
                Check(Get<IList>(world,"drinks").Count==1 && Mathf.Approximately(waterBefore,bowl.State.water),"Bowl interaction starts approach and does not remove water before contact");
                Check(audio.Count(LifeAudioCue.Drink)==drinkSounds,"Approaching a bowl cannot play a drinking contact sound");
                object activeDrink=Get<IList>(world,"drinks")[0];
                var activeBinding=activeDrink.GetType().GetField("animal").GetValue(activeDrink);
                Check((WildlifeAgent)activeBinding.GetType().GetField("wildlife").GetValue(activeBinding)==main,"The drinking fixture selects the main squirrel for the repeat invitation regression");
                Vector3 approach=(Vector3)activeBinding.GetType().GetField("target").GetValue(activeBinding),bowlStanding=player.position;
                Check(LifeWorldPlayChecks.PlaceForInteraction(life,bed),"Repeat invitation uses the actual communal bed interaction");
                world.Interact();
                Check(Get<IList>(world,"drinks").Count==1 && ReferenceEquals(Get<IList>(world,"drinks")[0],activeDrink) &&
                    (Vector3)activeBinding.GetType().GetField("target").GetValue(activeBinding)==approach &&
                    !(bool)activeBinding.GetType().GetField("rest").GetValue(activeBinding) && Mathf.Approximately(waterBefore,bowl.State.water),
                    "Repeated F invitation preserves the same drinking approach and consumes no water");
                Check((bool)activeDrink.GetType().GetField("returnRest").GetValue(activeDrink) &&
                    LifeWorld.Flat((Vector3)activeDrink.GetType().GetField("returnTarget").GetValue(activeDrink),bed.transform.position)<.01f,
                    "The accepted repeat invitation only selects where the animal rests after drinking");
                player.position=bowlStanding;Physics.SyncTransforms();
                bool up=false,standing=false,played=false,contact=false,consumptionObserved=false;until=Time.time+23;
                while(Get<IList>(world,"drinks").Count>0)
                {
                    object drink=Get<IList>(world,"drinks")[0];
                    var binding=drink.GetType().GetField("animal").GetValue(drink);
                    var actor=(WildlifeAgent)binding.GetType().GetField("wildlife").GetValue(binding);
                    if(!consumptionObserved && (bool)drink.GetType().GetField("consumed").GetValue(drink))
                    {consumptionObserved=true;ObserveAudio("first actual consumption");}
                    up|=actor.RestPhase==WildlifeRestPhase.GettingUp;standing|=actor.RestPhase==WildlifeRestPhase.Standing;
                    AnimationClip clip=(AnimationClip)drink.GetType().GetField("clip").GetValue(drink);played|=Weight(actor,clip)>.8f;
                    if((bool)typeof(LifeWorld).GetMethod("MouthTouchesWater",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{drink}))
                    {
                        Transform bone=(Transform)drink.GetType().GetField("mouthBone").GetValue(drink);
                        Vector3 local=(Vector3)typeof(LifeWorld).GetField("DrinkMouthLocal",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                        report.mouthAtDrink=bone.TransformPoint(local);report.waterAtDrink=bowl.waterVisual.position;contact=true;
                    }
                    Deadline(until,"Drinking did not finish or cancel within its movement deadline");yield return null;
                }
                Check(up && standing && played,"Drinking first rises to standing and plays the authored Drink clip");
                Check(contact && bowl.State.water<waterBefore-.1f,"Actual mouth contact precedes measured water consumption");
                ObserveAudio("single drink completed; baseline="+drinkSounds);
                Check(audio.Count(LifeAudioCue.Drink)==drinkSounds+1,"One actual mouth-water consumption plays exactly one drinking sound (before="+drinkSounds+", after="+audio.Count(LifeAudioCue.Drink)+")");
                until=Time.time+18;while(!world.IsSatisfied(sequence)){Deadline(until,"Drinking squirrel did not return to rest through its controller");yield return null;}
                Check(LifeWorldPlayChecks.PlaceForInteraction(life,bowl),"Shared drinking starts from the real bowl interaction");
                object companionBinding=Get<IDictionary>(world,"bindings")["companion"];
                Vector3 mainRestTarget=(Vector3)mainBinding.GetType().GetField("target").GetValue(mainBinding);
                Vector3 companionRestTarget=(Vector3)companionBinding.GetType().GetField("target").GetValue(companionBinding);
                Check((bool)mainBinding.GetType().GetField("rest").GetValue(mainBinding) &&
                    (bool)companionBinding.GetType().GetField("rest").GetValue(companionBinding),"Both shared drinkers begin with a recorded real rest destination");
                float sharedWater=bowl.State.water;world.Interact();world.Interact();
                foreach(string candidate in Get<IList>(world,"drinkCandidates"))report.drinkCandidates.Add(candidate);
                Check(Get<IList>(world,"drinks").Count==2,"A second real F retains the nearby companion's invitation while the route becomes available");
                object waiting=Get<IList>(world,"drinks").Cast<object>().FirstOrDefault(d=>(bool)d.GetType().GetField("planning").GetValue(d));
                Check(waiting!=null,"The reproduced occupied-route case keeps the second invitation pending instead of discarding it");
                object waitingBinding=waiting.GetType().GetField("animal").GetValue(waiting);
                WildlifeAgent waitingAnimal=(WildlifeAgent)waitingBinding.GetType().GetField("wildlife").GetValue(waitingBinding);
                Vector3 waitingPosition=waitingAnimal.transform.position;int beforeWaitingSounds=audio.Count(LifeAudioCue.Drink);
                float settle=Time.time+.2f;while(Time.time<settle)yield return null;
                Check(waitingAnimal.HasLifeTarget && LifeWorld.Flat(waitingPosition,waitingAnimal.transform.position)<.045f &&
                    !(bool)typeof(LifeWorld).GetMethod("ReadyToDrink",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{waiting}) &&
                    Mathf.Approximately(sharedWater,bowl.State.water) && audio.Count(LifeAudioCue.Drink)==beforeWaitingSounds,
                    "Waiting holds a real short lease without walking off, starting Drink, removing water or playing contact audio");
                Vector3 besideBowl=player.position;player.position=bowl.transform.position+Vector3.right*(LifeSession.CastRadius+2);Physics.SyncTransforms();
                until=Time.time+2;while(Get<IList>(world,"drinks").Count>0){Deadline(until,"Leaving the local circle did not cancel pending drinks");yield return null;}
                Check(Mathf.Approximately(sharedWater,bowl.State.water) && (bool)waitingBinding.GetType().GetField("rest").GetValue(waitingBinding),
                    "Leaving the local circle cancels waiting and restores the prior rest intention without consuming water");
                player.position=besideBowl;Physics.SyncTransforms();world.Interact();world.Interact();
                Check(Get<IList>(world,"drinks").Count==2,"Both animals can be invited again after cancellation through the same real bowl interaction");
                bool sharedContact=false,sharedPhoto=false;until=Time.time+35;
                string previousDrinkStartState=null;
                using(var sharedScan=new LifePhotoWorldChecks.SharedDrinkingScan(life,world.FindSpeaker("squirrel").Actor,world.FindSpeaker("companion").Actor))
                while(Get<IList>(world,"drinks").Count>0)
                {
                    IList active=Get<IList>(world,"drinks");
                    TraceDrinkStart(active,ref previousDrinkStartState);
                    if(active.Count==2)
                    {
                        bool both=true;
                        foreach(object drink in active)both&=(bool)typeof(LifeWorld).GetMethod("MouthTouchesWater",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{drink});
                        sharedContact|=both;
                        if(both && !sharedPhoto)sharedPhoto=sharedScan.TryCapture();
                    }
                    Deadline(until,"The two real drinking actions did not finish safely");yield return null;
                }
                Check(sharedContact,"Two different animals actually contact the same water surface at the same time");
                PhotoCheck(()=>Check(sharedPhoto,"The two actual drinkers produce a visible shared-drinking P22 photograph"));
                Check(Mathf.Abs(sharedWater-bowl.State.water-.24f)<.001f,"Repeated shared-drinking animation consumes exactly one measured sip per animal");
                until=Time.time+18;
                while(!RestingAt(main,mainRestTarget) || !RestingAt(companion,companionRestTarget) ||
                    Weight(main,main.clips.restSleep)<.8f || Weight(companion,companion.clips.restSleep)<.8f)
                {Deadline(until,"Shared drinkers did not return to their original rest places");yield return null;}
                Check((bool)mainBinding.GetType().GetField("rest").GetValue(mainBinding) &&
                    (bool)companionBinding.GetType().GetField("rest").GetValue(companionBinding) &&
                    (Vector3)mainBinding.GetType().GetField("target").GetValue(mainBinding)==mainRestTarget &&
                    (Vector3)companionBinding.GetType().GetField("target").GetValue(companionBinding)==companionRestTarget,
                    "Both real controllers return to their original rest destinations and sleeping clips after consuming water");
                world.RecordCast("rain",LocalWeather.Rain);weather.Cast("rain",out _);until=Time.time+9;
                bool riseMain=false,riseCompanion=false;
                while(main.RestPhase!=WildlifeRestPhase.Standing || companion.RestPhase!=WildlifeRestPhase.Standing || mat.State.IsDry)
                {
                    riseMain|=main.RestPhase==WildlifeRestPhase.GettingUp;riseCompanion|=companion.RestPhase==WildlifeRestPhase.GettingUp;
                    Deadline(until,"Wet bedding did not release both resting animals");yield return null;
                }
                Check(riseMain && riseCompanion && !mat.State.IsDry && !RestingAt(main,mainRestTarget) &&
                    !RestingAt(companion,companionRestTarget) && !world.IsSatisfied(sequence),"Wetting the mat triggers both rise clips and invalidates the current rest condition");
                Check(colliders.All(p=>p.Key && p.Key.enabled==p.Value),"All existing animal collision components remain enabled as before");

                object sheepBinding=Get<IDictionary>(world,"bindings")["sheep"];
                var herd=(JourneyHerd)sheepBinding.GetType().GetField("herd").GetValue(sheepBinding);
                int sheepId=(int)sheepBinding.GetType().GetField("member").GetValue(sheepBinding);
                JourneyMember sheep=herd.members.First(m=>m.id==sheepId);
                LifeObject grass=world.GatheringSources.OrderByDescending(o=>LifeWorld.Flat(o.transform.position,source.transform.position)).First();
                Check(LifeWorld.Flat(grass.transform.position,source.transform.position)>20,"Food comes from a different region than the bedding material");
                LifeRequest food=world.Requests.First(r=>r.id=="sheep.grass");
                player.position=grass.transform.position;world.TryGather();ObserveAudio("remote grass gathered");
                LifeObject firstBundle=Get<LifeObject>(world,"held");
                Vector3 feedPosition=sheep.root.position+Vector3.right*1.2f;player.position=feedPosition;Physics.SyncTransforms();
                until=Time.time+6;
                while(!herd.HasLifeTarget(sheepId) || sheep.ActualSpeed>.03f){Deadline(until,"The nearby sheep did not settle for feeding");yield return null;}
                ObserveAudio("feeding fixture arrived from remote gather");
                // The fixture moves the player between remote regions synchronously. A real walk cannot
                // complete within the gather cue's short cooldown, so separate those two actions here.
                var cueTimes=Get<Dictionary<LifeAudioCue,float>>(audio,"nextCue");
                while(cueTimes.TryGetValue(LifeAudioCue.Fiber,out float fiberDue) && Time.unscaledTime<fiberDue)yield return null;
                ObserveAudio("feeding baseline after gather cooldown");
                Check(firstBundle && firstBundle.State.kind==LifeObjectKind.Bundle && LifeWorld.Flat(player.position,sheep.root.position)<4,"Feeding starts with a real harvested bundle beside the original sheep");
                int foodSounds=audio.Count(LifeAudioCue.Fiber);world.OnTaskAccepted(food);world.Interact();world.Interact();
                Check(Get<object>(world,"feeding")!=null && Get<LifeObject>(world,"held")==firstBundle && !world.IsSatisfied(food),
                    "Repeated F reserves one bundle and cannot consume or complete help before real forage playback");
                Check(audio.Count(LifeAudioCue.Fiber)==foodSounds,"Reserving food before forage playback does not play its consumption cue");
                until=Time.time+2;
                while(Get<object>(world,"feeding")!=null){Deadline(until,"Sheep forage animation never began");yield return null;}
                Check(!Get<LifeObject>(world,"held") && world.IsSatisfied(food) && herd.LifeForageWeight(sheepId)>=.5f,
                    "The original forage clip gains mixer weight before the bundle is consumed and food help completes");
                ObserveAudio("actual forage consumption; baseline="+foodSounds);
                Check(audio.Count(LifeAudioCue.Fiber)==foodSounds+1,"Real grass consumption plays its fiber cue once (before="+foodSounds+", after="+audio.Count(LifeAudioCue.Fiber)+")");
                until=Time.time+4.8f;while(Time.time<until)yield return null;
                Check(herd.IsLifeForaging(sheepId) && Get<object>(world,"meal")!=null,"Actual feeding retains its visible food and forage pose for a five-second photography window");
                PhotoCheck(()=>LifePhotoWorldChecks.CheckEating(life,world.FindSpeaker("sheep").Actor,Check));
                player.position=source.transform.position;world.TryGather();LifeObject spareBundle=Get<LifeObject>(world,"held");
                player.position=feedPosition;world.Interact();
                Check(spareBundle && Get<LifeObject>(world,"held")==spareBundle && Get<object>(world,"feeding")==null,
                    "Another F during the same eating action cannot consume a second bundle");
                herd.ReleaseLifeTarget(sheepId);
                Check(!herd.IsLifeForaging(sheepId),"Releasing the life lease immediately cancels the temporary forage action");
                until=Time.time+3;
                while(!herd.HasLifeTarget(sheepId) || MemberWeight(sheep,sheep.clips.forage)>.1f)
                {Deadline(until,"The original idle animation did not return after the feeding lease ended");yield return null;}
                world.OnTaskAccepted(food);world.Interact();
                Check(Get<object>(world,"feeding")!=null,"A later feeding action can start normally");
                until=Time.time+2;
                while(Get<object>(world,"feeding")!=null){Deadline(until,"Second-source forage animation never began");yield return null;}
                Check(!Get<LifeObject>(world,"held") && world.IsSatisfied(food) && herd.LifeForageWeight(sheepId)>=.5f,
                    "Grass from the other map area also feeds the same actual animal through real forage playback");
                player.position=grass.transform.position;world.TryGather();spareBundle=Get<LifeObject>(world,"held");
                player.position=feedPosition;herd.ReleaseLifeTarget(sheepId);until=Time.time+3;
                while(!herd.HasLifeTarget(sheepId) || MemberWeight(sheep,sheep.clips.forage)>.1f)
                {Deadline(until,"The second feeding did not hand control back normally");yield return null;}
                world.OnTaskAccepted(food);world.Interact();
                Check(Get<object>(world,"feeding")!=null,"A third source can be reserved before the cancellation regression");
                herd.ReleaseLifeTarget(sheepId);Call(world,"UpdateFeeding");
                Check(Get<object>(world,"feeding")==null && Get<LifeObject>(world,"held")==spareBundle && !world.IsSatisfied(food),
                    "Cancellation before playback preserves the bundle and does not fabricate food help");
            }
            finally
            {
                if(audio)Call(audio,"OnApplicationFocus",audioFocus);
                track=false;Set(world,"held",null);Set(world,"craftingId",null);
                IDictionary objects=Get<IDictionary>(world,"objects");
                foreach(LifeObject obj in world.Objects.Where(o=>o && !ids.Contains(o.State.id)).ToArray()){objects.Remove(obj.State.id);UnityEngine.Object.Destroy(obj.gameObject);}
                world.RestoreJson(worldBefore);JsonUtility.FromJsonOverwrite(progressBefore,life.Progress);
                foreach(var pair in weatherState)pair.Key.SetValue(weather,pair.Value);
                Get<WeatherRegion>(weather,"sky").SetLocalWeather(Get<LocalWeather>(weather,"skyKind"),Get<int>(weather,"skyStrength"),Get<bool>(weather,"skyActive"));
                Get<WeatherRegion>(weather,"wind").SetLocalWeather(LocalWeather.Wind,Get<int>(weather,"windStrength"),Get<bool>(weather,"windActive"));
                player.SetPositionAndRotation(position,rotation);Physics.SyncTransforms();
                foreach(LifeObject obj in world.Objects)obj.RefreshVisual(Time.time);
                if(saveBefore!=null)File.WriteAllBytes(life.SavePath,saveBefore);else if(File.Exists(life.SavePath))File.Delete(life.SavePath);
                life.Gameplay.Notify(statusBefore);
            }
        }
        static void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);report.checks.Add(label);Write();}
        static void PhotoCheck(Action check)
        {
            try{check();}
            catch(Exception error){report.photoFailures.Add(error.ToString());Write();}
        }
        static void Deadline(float end,string message){if(Time.time>end)throw new InvalidOperationException(message);}
        static bool RestingAt(WildlifeAgent animal,Vector3 target)=>(bool)typeof(LifeWorld).
            GetMethod("IsRestingAt",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{animal,target});
        static LifeObject Find(LifeWorld world,string id)=>world.ObjectAt(id)??throw new InvalidOperationException("Missing world object "+id);
        static T Get<T>(object obj,string name)=>(T)obj.GetType().GetField(name,Hidden).GetValue(obj);
        static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,Hidden).SetValue(obj,value);
        static object Call(object obj,string name,params object[] arguments)=>obj.GetType().GetMethod(name,Hidden).Invoke(obj,arguments);
        static float Weight(WildlifeAgent animal,AnimationClip clip)
        {
            object player=Get<object>(animal,"animationPlayer");
            return (float)player.GetType().GetMethod("WeightOf").Invoke(player,new object[]{clip});
        }
        static float MemberWeight(JourneyMember member,AnimationClip clip)
        {
            object player=Get<object>(member,"animation");
            return (float)player.GetType().GetMethod("WeightOf").Invoke(player,new object[]{clip});
        }
    }
}
