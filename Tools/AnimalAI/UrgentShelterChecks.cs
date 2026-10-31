using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.WildlifeJourney;
using Object=UnityEngine.Object;

public static class UrgentShelterChecks
{
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Clock(object owner,float now)=>owner.GetType().GetProperty("SimulationTime").GetSetMethod(true).Invoke(owner,new object[]{now});
    static object Call(object owner,string name,params object[] args)=>owner.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner,args);
    static object MemberField(JourneyMember member,string name)=>typeof(JourneyMember).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(member);
    static BoxCollider Box(Transform parent,string name,Vector3 center,Vector3 size)
    {
        var box=new GameObject(name).AddComponent<BoxCollider>();box.transform.SetParent(parent);box.transform.position=center;box.size=size;
        Physics.SyncTransforms();return box;
    }
    sealed class AnimalFixture:IDisposable
    {
        public readonly GameObject root;
        public readonly WildlifePopulation population;
        public readonly WildlifeAgent animal;
        public AnimalFixture()
        {
            root=new GameObject("Urgent animal fixture");population=root.AddComponent<WildlifePopulation>();
            population.worldBounds=new Bounds(Vector3.up*10,new Vector3(40,20,40));
            animal=new GameObject("Animal").AddComponent<WildlifeAgent>();animal.transform.SetParent(root.transform);
            animal.scaleRange=Vector2.one;animal.bodyRadius=.3f;animal.walkSpeed=1;animal.runSpeed=2.5f;
            animal.timidity=animal.sociability=0;population.agents=new[]{animal};population.Initialize();Physics.SyncTransforms();
            Require(Mathf.Abs(animal.transform.position.y)<.001f,"Animal fixture must stand at the same y=0 ground as its wall");
        }
        public float Move(int frames=10)
        {
            Vector3 start=animal.transform.position;
            for(int i=0;i<frames;i++)
            {Clock(population,population.SimulationTime+.05f);Call(animal,"Move",.05f,population.SimulationTime);Physics.SyncTransforms();}
            return Vector3.Distance(start,animal.transform.position);
        }
        public void Dispose(){Object.DestroyImmediate(root);Physics.SyncTransforms();}
    }
    sealed class HerdFixture:IDisposable
    {
        public readonly GameObject root;
        public readonly JourneyNavigation navigation;
        public readonly JourneyHerd herd;
        public readonly JourneyMember member;
        public HerdFixture()
        {
            root=new GameObject("Urgent herd fixture");
            Box(root.transform,"Flat supporting floor",new Vector3(0,-.05f,0),new Vector3(40,.1f,40));
            navigation=root.AddComponent<JourneyNavigation>();
            navigation.nodes=new[]{new JourneyNode{id="Home",position=Vector3.zero},new JourneyNode{id="Shelter",position=Vector3.forward*6}};
            navigation.links=new[]{new JourneyLink{from=0,to=1,bidirectional=true}};navigation.Rebuild();
            herd=root.AddComponent<JourneyHerd>();herd.navigation=navigation;
            var body=new GameObject("Herd member");body.transform.SetParent(root.transform);
            member=new JourneyMember{id=1,root=body.transform,bodyLength=.6f,profile=new JourneyProfile{bodyRadius=.3f,walkSpeed=1}};
            // The 6m speed-comparison route must remain inside this fixture's activity site.
            herd.members=new[]{member};herd.sites=new[]{new JourneyActivitySite{name="Fixture home",center=Vector3.zero,radius=10}};
            herd.Initialize();Physics.SyncTransforms();
        }
        public float Move(int frames=10)
        {
            Vector3 start=member.root.position;
            for(int i=0;i<frames;i++)
            {
                Clock(herd,herd.SimulationTime+.05f);
                Require(herd.HasLifeTarget(member.id),"Herd fixture accidentally outlived its lease");
                Call(herd,"LifeMove",member,.05f);Physics.SyncTransforms();
            }
            return Vector3.Distance(start,member.root.position);
        }
        public bool Urgent=>(bool)Call(herd,"UrgentLifeMove",member);
        public void Dispose(){Object.DestroyImmediate(root);Physics.SyncTransforms();}
    }
    public static IEnumerable<string> Run()
    {
        Require(Application.isBatchMode && !Application.isPlaying && Application.dataPath.IndexOf("ValidationProject",StringComparison.OrdinalIgnoreCase)>=0,
            "Urgent movement fixtures require the independent ValidationProject batch editor");
        Vector3 goal=Vector3.forward*6;
        float walkDistance,walkSpeed;
        using(var f=new AnimalFixture())
        {
            Require(f.animal.RequestAutonomousTarget(goal,false),"Normal animal route rejected");
            walkDistance=f.Move();walkSpeed=f.animal.ActualSpeed;
            Require(walkDistance>.3f && walkSpeed>.5f,"Normal animal fixture did not actually move");
        }
        using(var f=new AnimalFixture())
        {
            Require(f.animal.RequestAutonomousTarget(goal,false,true),"Urgent animal route rejected");
            float fastDistance=f.Move();
            Require(Mathf.Abs(fastDistance/walkDistance-2.5f)<.02f && Mathf.Abs(f.animal.ActualSpeed/walkSpeed-2.5f)<.02f,
                "Urgent animal did not use existing runSpeed on the identical route");
            Require(f.animal.State==WildlifeState.Wander,"Urgent shelter changed animal into Flee");
            f.animal.ReleaseAutonomousTarget();
            Require(f.animal.RequestAutonomousTarget(goal,false),"Normal target after release rejected");
            Require(Mathf.Abs(f.Move()/walkDistance-1)<.02f,"Urgency survived release and a normal autonomous target");
            Require(f.animal.RequestAutonomousTarget(goal,false,true),"Second urgent animal target rejected");
            Clock(f.population,f.population.SimulationTime+2.01f);
            Require(!f.animal.HasLifeTarget,"Expired animal lease stayed active");
            f.Move(1);Require(Mathf.Abs(f.animal.ActualSpeed/walkSpeed-1)<.02f,"Expired animal lease continued running");
            Require(f.animal.RequestAutonomousTarget(goal,false,true),"Third urgent animal target rejected");
            Require(f.animal.RequestLifeTarget(goal,2),"Manual target could not replace autonomy");
            f.Move(1);Require(Mathf.Abs(f.animal.ActualSpeed/walkSpeed-1)<.02f,"Manual life target inherited urgent speed");
            yield return "PASS animal identical route: urgent distance/speed 2.5x, state stays Wander, release/expiry/manual replacement restore walking";
        }
        using(var f=new AnimalFixture())
        {
            Require(f.animal.RequestAutonomousTarget(goal,false,true),"Urgent animal wall fixture target rejected");
            Box(f.root.transform,"Wall added after target acceptance",new Vector3(0,1,1.5f),new Vector3(4,2,.1f));
            f.Move(25);
            Require(f.animal.transform.position.z<1.2f && f.animal.ActualSpeed==0,"Running animal crossed a newly inserted wall: position="+
                f.animal.transform.position+", speed="+f.animal.ActualSpeed);
            Require(!f.animal.RequestAutonomousTarget(goal,false,true),"Urgent animal target ignored a wall");
            yield return "PASS urgent animal remains blocked by actual wall sweeps after its target was accepted";
        }
        foreach(bool urgent in new[]{false,true})
        using(var f=new HerdFixture())
        {
            f.herd.sites[0].radius=2;
            Require(f.member.Arrived && !f.herd.RequestLifeTarget(1,goal,2) && f.member.Arrived,
                "Manual target changed its existing arrived-site boundary rule");
            for(int i=0;i<4;i++)
            {
                Require(f.herd.RequestAutonomousTarget(1,goal,false,urgent) && !f.member.Arrived,"Autonomous target retained the old site boundary");
                f.Move(15);
            }
            Require(f.member.root.position.z>2.5f,"Autonomous herd member did not actually leave its old 2m activity site");
            float away=f.member.root.position.z;
            f.herd.ReleaseAutonomousTarget(1);
            Require(!f.herd.HasLifeTarget(1) && !f.member.Arrived && (bool)MemberField(f.member,"rejoining") &&
                (Vector3)MemberField(f.member,"destination")==f.herd.sites[0].center,"Releasing independent shelter travel did not restore the home rejoin target");
            for(int i=0;i<100 && f.member.root.position.z>away-.3f;i++){f.herd.Simulate(.1f);Physics.SyncTransforms();}
            Require(f.member.root.position.z<away-.2f,"Released herd member did not actually move back toward its activity site: position="+
                f.member.root.position+", state="+f.member.State+", route="+(MemberField(f.member,"route")!=null)+", segment="+MemberField(f.member,"segment")+
                ", retry="+MemberField(f.member,"retryAt")+", time="+f.herd.SimulationTime+", requests="+f.herd.PlanningRequests+
                ", completions="+f.herd.PlanningCompletions+", cancellations="+f.herd.PlanningCancellations+", decision="+f.member.LastDecision);
            yield return "PASS "+(urgent?"urgent":"ordinary")+" autonomous herd trip leaves a 2m site toward 6m goal, then release restores rejoining and movement home";
        }
        using(var f=new HerdFixture())
        {
            f.herd.sites[0].radius=2;
            Require(!f.herd.RequestAutonomousTarget(1,Vector3.forward*9,false,true) && f.member.Arrived && !f.herd.HasLifeTarget(1),
                "Failed out-of-range autonomous target lost Arrived or acquired a lease");
            Box(f.root.transform,"Wall before autonomous acceptance",new Vector3(0,1,1.5f),new Vector3(4,2,.1f));
            Require(!f.herd.RequestAutonomousTarget(1,goal,false,true) && f.member.Arrived && !f.herd.HasLifeTarget(1),
                "Failed wall-blocked autonomous target lost Arrived or bypassed the wall");
            yield return "PASS invalid and wall-blocked independent targets restore Arrived=true and leave no lease";
        }
        float herdWalk;
        using(var f=new HerdFixture())
        {
            Require(f.herd.RequestAutonomousTarget(1,goal,false),"Normal herd route rejected");herdWalk=f.Move();
            Require(herdWalk>.4f,"Normal herd fixture did not actually move");
        }
        using(var f=new HerdFixture())
        {
            Require(f.herd.RequestAutonomousTarget(1,goal,false,true),"Urgent herd route rejected");
            Require(Mathf.Abs(f.Move()/herdWalk-1.6f)<.02f && Mathf.Abs(f.member.ActualSpeed-1.6f)<.02f,"Urgent herd did not walk 1.6x on identical route");
            f.herd.ReleaseAutonomousTarget(1);Require(!f.Urgent,"Herd release retained urgency");
            Require(f.herd.RequestLifeTarget(1,goal,2),"Manual herd target after release rejected");
            Require(Mathf.Abs(f.Move()/herdWalk-1)<.02f,"Manual herd lease inherited urgent speed");
            f.herd.ReleaseLifeTarget(1);Require(f.herd.RequestAutonomousTarget(1,goal,false,true),"Renewed urgent herd target rejected");
            Clock(f.herd,f.herd.SimulationTime+2.01f);Require(!f.herd.HasLifeTarget(1) && !f.Urgent,"Expired herd lease stayed urgent");
            Require(f.herd.RequestAutonomousTarget(1,goal,false),"Normal herd route after expiry rejected");
            Require(Mathf.Abs(f.Move()/herdWalk-1)<.02f,"Normal herd movement after expiry kept urgent speed");
            f.herd.ReleaseAutonomousTarget(1);f.member.Mode=JourneyTravelMode.Swim;
            Require(!f.herd.RequestAutonomousTarget(1,goal,false,true),"Urgent lease bypassed swim ownership");
            f.member.Mode=JourneyTravelMode.Bridge;
            Require(!f.herd.RequestAutonomousTarget(1,goal,false,true),"Urgent lease bypassed bridge ownership");
            yield return "PASS herd identical route: urgent distance/speed 1.6x; release, expiry and manual targets restore walking; swim/bridge leases rejected";
        }
        using(var f=new HerdFixture())
        {
            Require(f.herd.RequestAutonomousTarget(1,goal,false,true),"Urgent herd wall fixture target rejected");
            Box(f.root.transform,"Wall added after herd target acceptance",new Vector3(0,1,1.5f),new Vector3(4,2,.1f));
            f.Move(30);
            Require(f.member.root.position.z<1.2f && f.member.ActualSpeed==0,"Urgent herd crossed a newly inserted wall");
            yield return "PASS urgent herd retains the normal physical body sweep against a wall";
        }
    }
    public static void Batch()
    {
        Require(Application.isBatchMode && Application.dataPath.IndexOf("ValidationProject",StringComparison.OrdinalIgnoreCase)>=0,"Independent clone required");
        string report=Environment.GetEnvironmentVariable("WEATHERHOUSE_URGENT_REPORT");
        Require(!string.IsNullOrEmpty(report),"Set WEATHERHOUSE_URGENT_REPORT");
        var lines=new List<string>();int exit=0;
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            using(var f=new HerdFixture())
            {
                Vector3 goal=Vector3.forward*6;
                var nodes=f.navigation.nodes;var links=f.navigation.links;
                f.navigation.nodes=Array.Empty<JourneyNode>();f.navigation.links=Array.Empty<JourneyLink>();f.navigation.Rebuild();
                bool emptyGraph=f.navigation.FindRoute(goal,Vector3.zero,f.member.profile)!=null;
                f.navigation.nodes=nodes;f.navigation.links=links;f.navigation.Rebuild();
                JourneyRoute returnRoute=f.navigation.FindRoute(goal,Vector3.zero,f.member.profile);
                lines.Add("DIAG rejoin graph: empty graph route="+emptyGraph+", two-node graph segments="+(returnRoute==null?-1:returnRoute.segments.Count));
                Require(!emptyGraph && returnRoute!=null,"Herd fixture graph does not support real rejoining");
                bool step=f.navigation.TryStep(f.member.profile,f.member.root.position,goal,out Vector3 foot,out JourneyTravelMode mode);
                f.herd.sites[0].radius=2;bool manual=f.herd.RequestLifeTarget(1,goal,2);
                bool autonomous=f.herd.RequestAutonomousTarget(1,goal,false);
                lines.Add("DIAG herd fixture: position="+f.member.root.position+", body.center="+f.member.profile.bodyEnvelope.center+
                    ", body.half="+f.member.profile.bodyEnvelope.half+", TryStep="+step+", foot="+foot+", mode="+mode+
                    ", Arrived="+f.member.Arrived+", radius2 manual accepted="+manual+", radius2 autonomous accepted="+autonomous);
                Require(step && mode==JourneyTravelMode.Walk && !manual && autonomous,"Autonomous travel failed to leave the loitering boundary while preserving the manual contract");
            }
            lines.AddRange(Run());
        }
        catch(Exception e){exit=1;lines.Add("FAIL "+e);}
        File.WriteAllLines(report,lines);foreach(string line in lines)Debug.Log(line);EditorApplication.Exit(exit);
    }
}
