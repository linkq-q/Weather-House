using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using WeatherHouse.Hillside;
using Object=UnityEngine.Object;

public static class AnimalExpressionIntegrationChecks
{
    const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static object Get(object owner,string name)=>owner.GetType().GetField(name,Members).GetValue(owner);
    static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Members).SetValue(owner,value);
    static object Call(object owner,string name,params object[] args)=>owner.GetType().GetMethod(name,Members).Invoke(owner,args);
    static void Clock(WildlifePopulation population,float now)=>typeof(WildlifePopulation).GetProperty("SimulationTime").GetSetMethod(true).Invoke(population,new object[]{now});
    static void Guard()=>Require(Application.isBatchMode && !Application.isPlaying && Application.dataPath.IndexOf("ValidationProject",StringComparison.OrdinalIgnoreCase)>=0,
        "Expression and clip fixtures require the independent ValidationProject batch editor");
    static int HeartSeed()
    {
        for(int seed=0;seed<4096;seed++)
            if(new AnimalExpressionPolicy(seed,true,.8f,.8f).TryChoose(AnimalExpressionSignals.NearPlayer,0,false,out var emotion,out _) && emotion==AnimalEmotion.Heart)return seed;
        throw new InvalidOperationException("No deterministic heart fixture seed");
    }
    sealed class Fixture:IDisposable
    {
        public readonly GameObject root;
        public readonly WildlifePopulation population;
        public readonly WildlifeAgent agent;
        public readonly AnimalAutonomy autonomy;
        public readonly Transform player;
        public readonly object actor;
        public readonly AnimalEmotionGlyph emotion;
        public readonly AnimalSleepGlyph glyph;
        public readonly AnimalExpressionPolicy policy;
        public Fixture()
        {
            root=new GameObject("Expression integration fixture");population=root.AddComponent<WildlifePopulation>();
            population.worldBounds=new Bounds(Vector3.up*10,Vector3.one*40);
            agent=new GameObject("Animal").AddComponent<WildlifeAgent>();agent.transform.SetParent(root.transform);
            agent.scaleRange=Vector2.one;agent.timidity=agent.sociability=.8f;agent.walkSpeed=1;
            population.agents=new[]{agent};population.Initialize();
            player=new GameObject("Player").transform;player.SetParent(root.transform);player.position=Vector3.forward*3;
            autonomy=root.AddComponent<AnimalAutonomy>();Set(autonomy,"player",player);
            actor=Activator.CreateInstance(typeof(AnimalAutonomy).GetNestedType("Animal",BindingFlags.NonPublic),true);
            Set(actor,"agent",agent);Set(actor,"root",agent.transform);Set(actor,"id","fixture/rabbit/0");Set(actor,"species","rabbit");Set(actor,"outgoing",true);
            Call(autonomy,"Add",actor);
            policy=new AnimalExpressionPolicy(HeartSeed(),true,.8f,.8f);Set(actor,"expression",policy);
            emotion=(AnimalEmotionGlyph)Get(actor,"emotion");glyph=(AnimalSleepGlyph)Get(actor,"glyph");
            Physics.SyncTransforms();
        }
        public void Show()=>Call(autonomy,"UpdateExpressions",actor);
        public void State(WildlifeState state)=>Call(agent,"SetState",state,population.SimulationTime,3f);
        public void Move(int frames)
        {
            for(int i=0;i<frames;i++)
            {Clock(population,population.SimulationTime+.05f);Call(agent,"Move",.05f,population.SimulationTime);Physics.SyncTransforms();}
        }
        public void BeginApproach()
        {
            Call(autonomy,"Begin",actor,new AnimalOption{id="approach",action="approach"},Vector3.forward*6);
            Require(agent.RequestAutonomousTarget(Vector3.forward*6,false),"Fixture autonomous movement rejected");
        }
        public void Startle(int id)
        {
            Clock(population,population.SimulationTime+20);
            Require(agent.ReceiveStartle(new WildlifeAlarm{id=id,origin=agent.transform.position+Vector3.left*2,radius=5,intensity=1,
                expiresAt=population.SimulationTime+1,sourceSpecies="fixture-threat"},population.SimulationTime),"Actual startle event was rejected");
        }
        public AnimalEmotion Current=>(AnimalEmotion)Get(emotion,"emotion");
        public Dictionary<AnimalEmotion,float> Deadlines=>(Dictionary<AnimalEmotion,float>)Get(policy,"nextEmotion");
        public void Dispose(){Object.DestroyImmediate(root);Physics.SyncTransforms();}
    }
    public static string[] Run()
    {
        Guard();var results=new List<string>();
        using(var f=new Fixture())
        {
            f.BeginApproach();f.Show();Require(!f.emotion.IsPlaying,"Approach intent alone produced a heart");
            f.Move(2);f.Show();Require(!f.emotion.IsPlaying,"Less than .35m approach movement produced a heart");
            f.player.position=Vector3.forward*12;f.Move(10);f.Show();
            Require(Vector3.Distance(f.agent.transform.position,(Vector3)Get(f.actor,"approachExpressionOrigin"))>.35f && !f.emotion.IsPlaying,
                "Distant movement produced a heart without the player-distance condition");
            f.player.position=Vector3.forward*3;f.Show();
            Require(f.emotion.IsPlaying && f.Current==AnimalEmotion.Heart,"Actual approach displacement plus nearby player did not reach the renderer");
            Set(f.emotion,"age",.9f);f.Show();Require((float)Get(f.emotion,"age")==.9f,"Held approach reset playback age");
            f.emotion.Stop();f.Deadlines.Clear();Set(f.policy,"nextOrdinary",float.NegativeInfinity);
            for(int i=0;i<1000;i++)f.Show();
            Require(!f.emotion.IsPlaying,"Held approach replayed after playback ended, even with cooldown removed");
            Set(f.actor,"action","follow");f.Show();Require(!f.emotion.IsPlaying,"Approach-to-follow repeated the same proximity event");
            Set(f.actor,"action","resume");f.Show();f.BeginApproach();f.Show();
            Require(!f.emotion.IsPlaying && (Vector3)Get(f.actor,"approachExpressionOrigin")==f.agent.transform.position,
                "New approach reused a previous trip's displacement");
            results.Add("PASS actual UpdateExpressions: intent/sub-.35m/far travel suppressed; real nearby approach plays Heart once; follow and repeated updates do not replay");
        }
        using(var f=new Fixture())
        {
            Require(f.emotion.Play(AnimalEmotion.Happy),"Ordinary renderer fixture failed");
            Require(f.agent.RequestAutonomousTarget(f.agent.transform.position,true),"Sleep target rejected");
            f.Move(1);Require(f.agent.Sleeping,"Sleep signal fixture did not enter the agent's sleep state");
            f.Show();Require(f.glyph.Sleeping && !f.emotion.IsPlaying,"Autonomy sleep did not immediately stop ordinary expression");
            Require(!f.emotion.Play(AnimalEmotion.Exclamation),"Sleeping renderer accepted a competing alert");
            f.agent.ReleaseAutonomousTarget();f.Move(1);f.Show();
            Require(!f.glyph.Sleeping && !f.emotion.IsPlaying,"Wake replayed an interrupted expression");
            results.Add("PASS agent sleep flows through UpdateExpressions into Z priority, stops ordinary playback and does not resume it on wake");
        }
        using(var f=new Fixture())
        {
            f.BeginApproach();f.Move(12);f.Show();Require(f.emotion.IsPlaying && f.Current==AnimalEmotion.Heart,"Alert fixture missing ordinary playback");
            f.Startle(1);f.Show();Require(f.emotion.IsPlaying && f.Current==AnimalEmotion.Exclamation,"Actual Alert did not interrupt ordinary playback/cooldown");
            Require(Mathf.Abs(f.Deadlines[AnimalEmotion.Exclamation]-Time.time-120)<1,"Autonomy alert did not record a 120s same-expression deadline");
            Set(f.emotion,"age",.7f);f.Show();Require((float)Get(f.emotion,"age")==.7f,"Held Alert restarted playback");
            f.State(WildlifeState.Idle);f.Show();Require(f.emotion.Play(AnimalEmotion.Happy),"Second ordinary playback fixture failed");
            f.Startle(2);f.Show();Require(f.Current==AnimalEmotion.Happy,"Second Alert ignored its 120s deadline");
            // The policy clock contract is separately tested. Move its stored deadline to now to exercise the real integration boundary without a two-minute editor wait.
            f.State(WildlifeState.Idle);f.Show();f.Deadlines[AnimalEmotion.Exclamation]=Time.time;
            f.Startle(3);f.Show();Require(f.Current==AnimalEmotion.Exclamation,"New Alert failed after its stored cooldown expired");
            results.Add("PASS real startle-to-Alert wiring interrupts Heart, does not repeat held Alert, and enforces the stored 120s deadline before a later alert");
        }
        return results.ToArray();
    }

    // Real prefab curves and the production Playables graph, advanced manually in an isolated Edit fixture.
    // This verifies binding and transition timing; full-scene Play screenshots remain a separate acceptance step.
    public static string[] RunSleepClips()
    {
        Guard();var results=new List<string>();
        foreach(string species in new[]{"fox","squirrel"})
        {
            var root=new GameObject("Actual sleep clip fixture "+species);
            try
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/World/Hillside/Generated/"+species+".prefab");
                Require(prefab,"Missing actual animal prefab: "+species);
                var agent=Object.Instantiate(prefab,root.transform).GetComponent<WildlifeAgent>();
                var population=root.AddComponent<WildlifePopulation>();population.worldBounds=new Bounds(Vector3.up*10,Vector3.one*200);
                population.agents=new[]{agent};population.Initialize();Physics.SyncTransforms();
                Require(agent.animator && agent.clips.HasRestSequence,"Actual prefab lacks animator or complete rest clips: "+species);
                foreach(AnimationClip clip in new[]{agent.clips.restDown,agent.clips.restSleep,agent.clips.restUp})
                {
                    int bound=0;
                    foreach(var binding in AnimationUtility.GetCurveBindings(clip))
                        if(binding.type==typeof(Transform))
                        {
                            Require(string.IsNullOrEmpty(binding.path) || agent.animator.transform.Find(binding.path),
                                "Rest curve targets a missing actual bone: "+species+"/"+clip.name+"/"+binding.path);
                            bound++;
                        }
                    Require(bound>0 && clip.length>0,"Rest clip has no actual transform curves/duration: "+species+"/"+clip.name);
                }
                object animation=Get(agent,"animationPlayer");Call(animation,"Initialize",agent.animator,agent.clips,0f,null);
                var graph=(PlayableGraph)Get(animation,"graph");Require(graph.IsValid(),"Actual animation graph was not initialized");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                void Step()
                {
                    Clock(population,population.SimulationTime+.05f);
                    Call(agent,"Move",.05f,population.SimulationTime);graph.Evaluate(.05f);Physics.SyncTransforms();
                }
                for(int i=0;i<10;i++)Step();
                Transform[] bones=agent.animator.GetComponentsInChildren<Transform>();
                var rotations=new Quaternion[bones.Length];var positions=new Vector3[bones.Length];
                for(int i=0;i<bones.Length;i++){rotations[i]=bones[i].localRotation;positions[i]=bones[i].localPosition;}
                bool down=false;
                int downFrames=Mathf.CeilToInt((agent.clips.restDown.length+3)*20);
                for(int i=0;i<downFrames && agent.RestPhase!=WildlifeRestPhase.Sleeping;i++)
                {
                    Require(agent.RequestAutonomousTarget(agent.transform.position,true),"Actual prefab rest lease rejected: "+species);
                    Step();down|=agent.RestPhase==WildlifeRestPhase.LyingDown;
                }
                Require(down && agent.RestPhase==WildlifeRestPhase.Sleeping,"Actual restDown did not enter its sleep loop: "+species);
                for(int i=0;i<12;i++){Require(agent.RequestAutonomousTarget(agent.transform.position,true),"Actual sleep renewal rejected");Step();}
                float weight=(float)Call(animation,"WeightOf",agent.clips.restSleep);
                Require(weight>.8f,"Actual restSleep clip lacks mixer weight: "+species);
                int changed=0;
                for(int i=0;i<bones.Length;i++)if(Quaternion.Angle(rotations[i],bones[i].localRotation)>.25f || Vector3.Distance(positions[i],bones[i].localPosition)>.0001f)changed++;
                Require(changed>0,"Sleep graph had weight but did not animate the actual prefab hierarchy: "+species);
                agent.ReleaseAutonomousTarget();bool up=false;
                int upFrames=Mathf.CeilToInt((agent.clips.restUp.length+3)*20);
                for(int i=0;i<upFrames;i++)
                {Step();up|=agent.RestPhase==WildlifeRestPhase.GettingUp;if(up && agent.RestPhase==WildlifeRestPhase.Standing)break;}
                Require(up && agent.RestPhase==WildlifeRestPhase.Standing,"Actual restUp did not return to standing: "+species);
                results.Add("PASS actual "+species+" prefab Playables: "+agent.clips.restDown.name+" -> "+agent.clips.restSleep.name+
                    " (weight="+weight.ToString("F3")+", changed transforms="+changed+") -> "+agent.clips.restUp.name+" -> Standing");
            }
            finally{Object.DestroyImmediate(root);Physics.SyncTransforms();}
        }
        return results.ToArray();
    }
}
