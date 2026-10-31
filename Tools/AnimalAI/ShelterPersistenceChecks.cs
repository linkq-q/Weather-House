// Copy only to an independent ValidationProject Editor folder. No scene or user save is written.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.Life;
using WeatherHousePrototype;
using Object=UnityEngine.Object;

public static class ShelterPersistenceChecks
{
    static readonly BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    static readonly Type Animal=typeof(AnimalAutonomy).GetNestedType("Animal",BindingFlags.NonPublic);
    static object Get(object o,string name)=>o.GetType().GetField(name,F).GetValue(o);
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,F).SetValue(o,value);
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,F).Invoke(o,args);
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static float Flat(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}

    sealed class Fixture:IDisposable
    {
        public readonly GameObject root;
        public readonly AnimalAutonomy director;
        public readonly WildlifePopulation population;
        public readonly WildlifeAgent agent;
        public readonly object state;
        public readonly Transform player;
        bool publishRain, rainActive;
        Vector3 rainCenter;
        int rainStrength;
        public Fixture()
        {
            root=new GameObject("Shelter persistence fixture");
            player=new GameObject("Remote player").transform;player.SetParent(root.transform);player.position=Vector3.left*40;
            var world=root.AddComponent<LifeWorld>();
            population=root.AddComponent<WildlifePopulation>();population.player=player;
            population.worldBounds=new Bounds(Vector3.up*10,new Vector3(160,20,160));
            agent=new GameObject("Actual moving animal").AddComponent<WildlifeAgent>();agent.transform.SetParent(root.transform);
            agent.scaleRange=Vector2.one;agent.timidity=agent.sociability=0;agent.walkSpeed=1;agent.runSpeed=2.5f;agent.habitatRadius=50;
            population.agents=new[]{agent};population.Initialize();Physics.SyncTransforms();
            director=root.AddComponent<AnimalAutonomy>();director.Initialize(world,player);
            Set(director,"client",root.AddComponent<AnimalPlannerClient>());
            state=Activator.CreateInstance(Animal,true);Set(state,"agent",agent);Set(state,"root",agent.transform);
            Set(state,"species","rabbit");Set(state,"id","persistence");Call(director,"Add",state);
            // No client configuration or network request is used by this fixture.
            Set(state,"nextAI",float.PositiveInfinity);Set(director,"nextRequest",float.PositiveInfinity);
        }
        public string Action=>(string)Get(state,"action");
        public bool Needed=>(bool)Call(director,"NeedsShelter",state);
        public Vector3 Target=>(Vector3)Get(agent,"lifeTarget");
        public void Rain(Vector3 center,int strength=2,bool active=true)
        {publishRain=true;rainCenter=center;rainStrength=strength;rainActive=active;director.Rain(center,8,active,strength);}
        public BoxCollider Box(string name,Vector3 center,Vector3 size)
        {
            var box=new GameObject(name).AddComponent<BoxCollider>();box.transform.SetParent(root.transform);
            box.transform.position=center;box.size=size;Physics.SyncTransforms();return box;
        }
        public void Begin(Vector3 goal)
        {
            Require(Needed,"Fixture has no shelter intention");Set(state,"wasRain",true);
            Call(director,"Begin",state,new AnimalOption{id="shelter",action="shelter"},goal);
        }
        public void Frame(bool update=false)
        {
            if(publishRain)director.Rain(rainCenter,8,rainActive,rainStrength);
            Set(state,"due",-1f);
            if(update)Call(director,"Update");
            else {Call(director,"Tick",state);Call(director,"AdvancePlanning",state);}
            population.Simulate(.05f);Physics.SyncTransforms();
        }
        public void Arrive(Vector3 goal)
        {
            for(int i=0;i<500 && Flat(agent.transform.position,goal)>.28f;i++)
            {
                Frame();Require(Action=="shelter","Animal released shelter while still travelling; position="+agent.transform.position);
            }
            Require(Flat(agent.transform.position,goal)<.3f,"Animal failed to reach its physically checked shelter goal");
        }
        public void Dispose(){Object.DestroyImmediate(root);Physics.SyncTransforms();}
    }

    static string BeyondRainCircle()
    {
        using(var f=new Fixture())
        {
            Vector3 goal=Vector3.forward*10;
            f.Box("Real roof outside 8m circle",goal+Vector3.up*2.5f,new Vector3(3,.2f,3));
            f.Rain(Vector3.zero);f.Begin(goal);f.Arrive(goal);
            Require(!(bool)Call(f.director,"Wet",f.state),"Fixture never crossed the rain boundary");
            Require(Physics.Raycast(f.agent.transform.position+Vector3.up,Vector3.up,3),"Final point is not under the actual roof collider");
            Vector3 arrived=f.agent.transform.position;
            for(int i=0;i<240;i++)
            {
                f.Frame();Require(f.Action=="shelter"&&f.agent.HasLifeTarget&&f.agent.AutonomousTarget,"Rain-covered departure point did not retain the shelter lease");
            }
            Require(Flat(arrived,f.agent.transform.position)<.12f,"Sheltered animal wandered back into the still-active rain");
            Require(((AnimalRainReaction)Get(f.state,"rainReaction")).Episode==1,"Crossing into shelter rerolled the rain exposure");
            f.Rain(Vector3.right*80);f.Frame();
            Require(f.Action!="shelter"&&!f.Needed,"Moving rain away from both animal and departure point did not release shelter");
            return "PASS actual route crosses 8m edge to roof at10m; 12 simulated seconds waiting preserves one exposure and lease; moving rain off both places releases";
        }
    }
    static string RoofAndRainEnd()
    {
        using(var f=new Fixture())
        {
            var region=new GameObject("Actual static rain region").AddComponent<WeatherRegion>();region.transform.SetParent(f.root.transform);
            region.radius=8;region.SetLocalWeather(LocalWeather.Rain,2,true);Set(f.director,"weatherRegions",new[]{region});
            Vector3 goal=Vector3.forward*4;f.Box("Roof inside rain",goal+Vector3.up*2.5f,new Vector3(3,.2f,3));
            f.Begin(goal);f.Arrive(goal);
            for(int i=0;i<100;i++){f.Frame();Require(f.Needed&&f.Action=="shelter","Roof occupancy was mistaken for stopped rain");}
            region.SetLocalWeather(LocalWeather.Rain,2,false);f.Frame();
            Require(f.Action!="shelter"&&!f.Needed,"Actually stopping region rain did not resume ordinary behaviour");
            // A distant, unrelated rain region must never hold an animal here.
            region.transform.position=Vector3.right*60;region.SetLocalWeather(LocalWeather.Rain,2,true);
            Require(!f.Needed,"Unrelated distant rain kept the animal sheltered");
            return "PASS actual WeatherRegion remains rainy under physical roof; clear releases; unrelated distant rain cannot retain shelter";
        }
    }
    static string WeakEpisode()
    {
        int chosen=0,total=2000;
        for(int i=0;i<total;i++)
        {
            var reaction=new AnimalRainReaction(i/100,i%100,"rabbit");reaction.Observe(1);
            bool initial=reaction.WeakShelterChosen;if(initial)chosen++;
            for(int sample=0;sample<40;sample++)reaction.Observe(1);
            Require(reaction.Episode==1&&reaction.NeedsShelter==initial,"Weak rain rerolled without leaving exposure");
            reaction.Observe(2);Require(reaction.NeedsShelter&&reaction.Urgent,"Strong rain failed to require urgent shelter");
            reaction.Observe(1);Require(reaction.NeedsShelter&&!reaction.Urgent&&reaction.WeakShelterChosen==initial,
                "Weakening rain cancelled this exposure's already-selected shelter");
            reaction.Observe(0);Require(!reaction.NeedsShelter,"Dry observation retained shelter");
            reaction.Observe(1);Require(reaction.Episode==2,"New exposure did not get a new independent draw");
        }
        Require(chosen>total*.45f&&chosen<total*.55f,"Initial weak choice distribution changed: "+chosen);
        return "PASS weak first exposure chooses "+chosen+"/2000; no repeated draw; strong commits shelter until dry even after weakening; reentry starts new episode";
    }
    static string GuidanceRainQuery()
    {
        using(var f=new Fixture())
        {
            f.Rain(Vector3.zero);f.Begin(Vector3.forward*10);
            f.agent.transform.position=Vector3.forward*10;Physics.SyncTransforms();
            var reaction=(AnimalRainReaction)Get(f.state,"rainReaction");int episode=reaction.Episode,revision=((AnimalPlanGate)Get(f.state,"gate")).Revision;
            MethodInfo query=typeof(AnimalAutonomy).GetMethod("IsUrgentlySheltering");
            Require(query!=null,"Guidance cannot query the current shelter urgency");
            bool Urgent()=>(bool)query.Invoke(f.director,new object[]{f.agent.transform});
            Require(!(bool)Call(f.director,"Wet",f.state)&&Urgent(),"Outside-circle shelter origin did not keep the guidance urgency guard");
            for(int i=0;i<20;i++)Require(Urgent(),"Repeated read changed urgency");
            Require(reaction.Episode==episode&&((AnimalPlanGate)Get(f.state,"gate")).Revision==revision&&reaction.Intensity==2,
                "Read-only guidance query mutated rain exposure or intent revision");
            f.Rain(Vector3.zero,1);Require(!Urgent(),"Weak rain wrongly retained the strong-rain guidance guard");
            f.Rain(Vector3.zero,2,false);Require(!Urgent(),"Clear was ignored until a later autonomy Tick");
            f.Rain(Vector3.right*60);Require(!Urgent(),"Distant unrelated strong rain blocked guidance");
            return "PASS read-only guidance urgency includes rainy departure outside current circle; weaker/cleared/moved rain releases immediately without Tick or exposure mutation";
        }
    }
    static string BudgetWaiting()
    {
        using(var f=new Fixture())
        {
            f.Rain(Vector3.zero);f.Begin(Vector3.zero);Set(f.state,"beganMoving",true);
            Require(f.agent.RequestAutonomousTarget(Vector3.zero,false),"Initial waiting lease rejected");
            MethodInfo clock=typeof(AnimalWorkBudget).GetMethod("SetTestClock",BindingFlags.Static|BindingFlags.NonPublic);int frame=0;
            clock.Invoke(null,new object[]{(Func<double>)(()=>0),(Func<int>)(()=>frame)});
            try
            {
                for(int i=0;i<160;i++)
                {
                    frame++;
                    for(int work=0;work<4;work++)
                    {Require(AnimalWorkBudget.TryBegin(AnimalWorkBudget.Kind.Planning,out var scope),"Could not saturate fixture budget");scope.Dispose();}
                    f.Frame(true);
                    Require(f.agent.HasLifeTarget&&f.agent.AutonomousTarget&&f.Action=="shelter","Planning denial expired a valid shelter lease");
                }
                Require(f.agent.transform.position.sqrMagnitude<.001f,"Waiting animal moved while planning budget was denied");
            }
            finally{clock.Invoke(null,new object[]{null,null});}
            return "PASS real director.Update keeps arrived shelter lease through8s of saturated planning budget without acquiring new targets";
        }
    }
    static string RejectedPlacement()
    {
        using(var f=new Fixture())
        {
            f.Rain(Vector3.zero);f.Begin(Vector3.zero);Set(f.state,"beganMoving",true);
            Require(f.agent.RequestAutonomousTarget(Vector3.zero,false),"Initial waiting lease rejected");
            // A later obstacle makes full placement validation fail. Keeping an existing pose must not teleport it away or release it.
            f.Box("New occupancy obstruction",Vector3.up*.6f,new Vector3(.3f,1,.3f));
            Require(!f.agent.RequestAutonomousTarget(Vector3.zero,false),"Physical obstruction failed to reject repeated placement");
            for(int i=0;i<120;i++){f.Frame(true);Require(f.agent.HasLifeTarget&&f.Action=="shelter","Rejected placement discarded the already-held shelter pose");}
            Require(f.agent.transform.position.sqrMagnitude<.001f,"Holding an existing pose moved the animal");
            return "PASS actual collider rejects repeated body placement; existing shelter owner remains stationary for6s without bypassing motion checks";
        }
    }
    static string Ownership()
    {
        using(var f=new Fixture())
        {
            Require(f.agent.RequestAutonomousTarget(Vector3.forward*3,false),"Lease lifecycle initial request failed");
            bool Keep(bool hold=false)
            {
                MethodInfo keep=typeof(WildlifeAgent).GetMethod("KeepAutonomousTarget");
                return keep!=null&&(bool)keep.Invoke(f.agent,new object[]{hold});
            }
            Vector3 position=f.agent.transform.position;Require(Keep(true),"Valid own lease cannot hold");
            Require(f.Target==position&&f.agent.transform.position==position,"Hold changed transform or kept old destination");
            Require(f.agent.RequestLifeTarget(Vector3.right*2,2),"External target fixture failed");
            float until=(float)Get(f.agent,"lifeUntil");Require(!Keep()&&(float)Get(f.agent,"lifeUntil")==until,"Keep stole external target");
            f.agent.ReleaseLifeTarget();Require(f.agent.RequestAutonomousTarget(Vector3.forward*3,false),"Fresh lease failed");
            typeof(WildlifePopulation).GetProperty("SimulationTime").GetSetMethod(true).Invoke(f.population,new object[]{f.population.SimulationTime+3});
            Require(!Keep(),"Keep resurrected an expired lease");
            Require(f.agent.RequestAutonomousTarget(Vector3.forward*3,false),"Disabled lifecycle setup failed");
            f.agent.enabled=false;Require(!Keep(),"Disabled animal lease was renewed");f.agent.enabled=true;
            typeof(WildlifeAgent).GetProperty("State").GetSetMethod(true).Invoke(f.agent,new object[]{WildlifeState.Alert});
            Require(!Keep(),"Alert animal could not interrupt shelter ownership");
            return "PASS holding changes only own target, not transform; external, expired, disabled and Alert leases are never renewed";
        }
    }
    static string DetourProgress()
    {
        using(var f=new Fixture())
        {
            Vector3 goal=Vector3.right*6,corner=Vector3.forward*4;
            f.Box("Wall forcing detour",new Vector3(3,1.5f,0),new Vector3(.2f,3,6));
            f.Rain(Vector3.zero);f.Begin(goal);
            Require(!f.population.TryMove(f.agent,goal,out _),"Fixture wall did not block direct path");
            var route=new List<Vector3>{corner,corner+goal,goal};Vector3 from=f.agent.transform.position;
            foreach(Vector3 point in route)
            {
                Require(f.population.TryMoveFrom(f.agent,from,WildlifeBodyEnvelope.Heading(point-from,Quaternion.identity),point,out Vector3 foot),"Detour fixture contains an unsafe segment");
                from=foot;
            }
            Set(f.state,"route",route);Set(f.state,"beganMoving",true);Set(f.state,"bestDistance",6f);
            Require(f.agent.RequestAutonomousTarget(corner,false),"Detour first leg rejected");
            for(int i=0;i<12;i++)f.population.Simulate(.05f);
            Require(f.agent.transform.position.z>.3f&&Flat(f.agent.transform.position,goal)>6,"Actual movement did not initially go away from final goal");
            Set(f.state,"progressAt",Time.time-31);Call(f.director,"Execute",f.state);
            Require(f.Action=="shelter"&&(bool)Get(f.state,"beganMoving")&&(float)Get(f.state,"progressAt")>=Time.time-.01f,
                "Real detour progress was falsely treated as30s stalled because distance to final goal increased");
            return "PASS actual body-safe wall detour increases final-goal distance but resets progress timeout after real movement";
        }
    }
    static string StalledRetry()
    {
        using(var f=new Fixture())
        {
            Vector3 goal=Vector3.forward*5;f.Rain(Vector3.zero);f.Begin(goal);
            Require(f.agent.RequestAutonomousTarget(goal,false),"Initial shelter leg rejected");
            f.Box("Wall inserted after accepting shelter leg",new Vector3(0,1.5f,2),new Vector3(8,3,.2f));
            Set(f.state,"route",new List<Vector3>{goal});Set(f.state,"beganMoving",true);Set(f.state,"bestDistance",5f);
            Animal.GetField("progressPosition",F)?.SetValue(f.state,f.agent.transform.position+Vector3.right*.02f);Set(f.state,"progressAt",Time.time-31);
            Call(f.director,"Execute",f.state);
            Require(f.Action=="shelter"&&!(bool)Get(f.state,"beganMoving")&&f.agent.HasLifeTarget&&f.Target==f.agent.transform.position,
                "Truly blocked shelter leg released ordinary activity instead of holding and retrying");
            Require(((Dictionary<Vector3,float>)Get(f.state,"failedShelters")).ContainsKey(goal),"Failed shelter destination was not cooled down");
            Call(f.director,"Tick",f.state);Require(Get(f.state,"shelterQuery")!=null,"Blocked shelter did not start another bounded candidate search");
            Vector3 before=f.agent.transform.position;
            for(int i=0;i<20;i++)f.Frame(true);
            Require(f.Action=="shelter"&&Flat(before,f.agent.transform.position)<.05f,"Retrying blocked shelter resumed wandering or pursued old blocked target");
            return "PASS2cm jitter cannot hide30s stall; blocked leg holds current pose, cools old destination and starts bounded retry without wandering";
        }
    }
    static (string name,Func<string> run)[] Cases()=>new (string,Func<string>)[]{
        ("roof outside rain circle",BeyondRainCircle),("roof and actual rain end",RoofAndRainEnd),("weak episode",WeakEpisode),("guidance rain query",GuidanceRainQuery),
        ("budget waiting",BudgetWaiting),("rejected placement",RejectedPlacement),("ownership",Ownership),
        ("detour progress",DetourProgress),("stalled retry",StalledRetry)};
    public static IEnumerable<string> Run(){foreach(var test in Cases())yield return test.run();}
    public static void Batch()
    {
        Require(Application.isBatchMode&&!Application.isPlaying&&Application.dataPath.Contains("ValidationProject"),"Independent ValidationProject batch only");
        string path=Environment.GetEnvironmentVariable("WEATHERHOUSE_SHELTER_PERSISTENCE_REPORT");
        Require(!string.IsNullOrEmpty(path),"Set WEATHERHOUSE_SHELTER_PERSISTENCE_REPORT to a new text file");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var lines=new List<string>();int failures=0;
        foreach(var test in Cases())try{lines.Add(test.run());}catch(Exception e){failures++;lines.Add("FAIL "+test.name+": "+e);}
        lines.Add("RESULT failed="+failures);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));File.WriteAllLines(path,lines);
        EditorApplication.Exit(failures==0?0:1);
    }
}
