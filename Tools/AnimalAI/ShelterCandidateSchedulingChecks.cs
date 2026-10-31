// Run in an empty Unity batch scene; verifies the real shelter scheduler with a controlled slow route.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.Life;
using Object = UnityEngine.Object;

public static class ShelterCandidateSchedulingChecks
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly Type AnimalType=typeof(AnimalAutonomy).GetNestedType("Animal",BindingFlags.NonPublic);
    static readonly Type QueryType=typeof(AnimalAutonomy).GetNestedType("ShelterQuery",BindingFlags.NonPublic);
    static object Get(object target,string name)=>target.GetType().GetField(name,Flags).GetValue(target);
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,Flags).SetValue(target,value);
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static readonly Vector3 SlowGoal=Vector3.right*28;
    static AnimalLocalRoute SlowRoute()=>new AnimalLocalRoute(Vector3.zero,SlowGoal,.3f,SlowTraverse);
    // A long thin wall needs a real detour. It is intentionally independent of the easy candidate's physics.
    static bool SlowTraverse(Vector3 from,Vector3 to,out Vector3 foot)
    {
        foot=to;float enter=0,leave=1;
        return !(Clip(from.x,to.x-from.x,1,2,ref enter,ref leave) && Clip(from.z,to.z-from.z,-5,5,ref enter,ref leave));
    }
    static bool Clip(float origin,float direction,float min,float max,ref float enter,ref float leave)
    {
        if(Math.Abs(direction)<.000001f)return origin>=min && origin<=max;
        float a=(min-origin)/direction,b=(max-origin)/direction;
        if(a>b){float swap=a;a=b;b=swap;}
        enter=Math.Max(enter,a);leave=Math.Min(leave,b);return enter<=leave;
    }
    sealed class Fixture:IDisposable
    {
        public readonly GameObject root;
        public readonly AnimalAutonomy director;
        public readonly object animal,query;
        public readonly AnimalLocalRoute slow=SlowRoute();
        public int units;
        public Fixture(bool alternate)
        {
            root=new GameObject("Shelter scheduling fixture");
            var world=root.AddComponent<LifeWorld>();
            var player=new GameObject("Distant fixture player").transform;player.SetParent(root.transform);player.position=new Vector3(-40,0,-40);
            var population=root.AddComponent<WildlifePopulation>();population.player=player;
            population.worldBounds=new Bounds(new Vector3(0,10,0),new Vector3(120,20,120));
            var agent=new GameObject("Scheduler animal").AddComponent<WildlifeAgent>();agent.transform.SetParent(root.transform);
            agent.timidity=0;agent.scaleRange=Vector2.one;agent.habitatRadius=40;
            population.agents=new[]{agent};population.Initialize();
            director=root.AddComponent<AnimalAutonomy>();director.Initialize(world,player);
            animal=Activator.CreateInstance(AnimalType,true);Set(animal,"agent",agent);Set(animal,"root",agent.transform);
            Set(animal,"species","rabbit");Set(animal,"id","scheduler-fixture");
            typeof(AnimalAutonomy).GetMethod("Add",Flags).Invoke(director,new[]{animal});
            query=Activator.CreateInstance(QueryType,true);Set(query,"origin",Vector3.zero);Set(query,"target",SlowGoal);
            var rings=(float[])typeof(AnimalAutonomy).GetField("ShelterRings",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            Set(query,"probe",rings.Length*16);Set(query,"route",slow);
            if(alternate)((List<Vector3>)Get(query,"candidates")).Add(Vector3.forward*6);
            Set(animal,"shelterQuery",query);Physics.SyncTransforms();
        }
        public void Advance()
        {
            director.Rain(Vector3.zero,8,true,2);
            typeof(AnimalAutonomy).GetMethod("AdvancePlanning",Flags).Invoke(director,new[]{animal});units++;
        }
        public bool Selected=>(string)Get(animal,"action")=="shelter";
        public void PendingIsPreserved()
        {
            Require(ReferenceEquals(Get(animal,"shelterQuery"),query),"Slow-only query was discarded while it still had work");
            Require((float)Get(animal,"nextShelter")==0,"Slow-only search entered retry cooldown prematurely");
            Require(!((Dictionary<Vector3,float>)Get(animal,"failedShelters")).ContainsKey(SlowGoal),"Unfinished slow candidate was marked failed");
            bool retained=ReferenceEquals(Get(query,"route"),slow);
            foreach(object pending in (IEnumerable)Get(query,"pendingRoutes"))
                retained|=ReferenceEquals(pending.GetType().GetField("Item2").GetValue(pending),slow);
            Require(retained,"Scheduler lost the original slow route state");
        }
        public void Dispose()=>Object.DestroyImmediate(root);
    }
    public static IEnumerable<string> Run()
    {
        AnimalLocalRoute baseline=SlowRoute();int baselineWork=0;
        for(;!baseline.Finished && baselineWork<20000;baselineWork++)baseline.Advance();
        Require(baseline.Result!=null && baselineWork>36,"Fixture must have a reachable route requiring more than three 12-unit slices");
        yield return "PASS controlled difficult candidate is reachable and takes "+baselineWork+" work units (more than36)";
        using(var fixture=new Fixture(true))
        {
            for(int i=0;i<24 && !fixture.Selected;i++)fixture.Advance();
            Require(fixture.Selected && Vector3.Distance((Vector3)Get(fixture.animal,"goal"),Vector3.forward*6)<.01f,
                "Difficult first candidate prevented the easy alternate from being accepted within24 units");
            Require(!fixture.slow.Finished,"Easy candidate waited until the difficult candidate finished");
            Require(!((Dictionary<Vector3,float>)Get(fixture.animal,"failedShelters")).ContainsKey(SlowGoal),
                "Yielding the difficult candidate incorrectly marked it failed");
            yield return "PASS easy alternate accepted before difficult route finishes; work units="+fixture.units;
        }
        using(var fixture=new Fixture(false))
        {
            for(int i=0;i<12;i++)fixture.Advance();fixture.PendingIsPreserved();int first=fixture.slow.Queries;
            for(int i=0;i<12;i++)fixture.Advance();fixture.PendingIsPreserved();
            Require(fixture.slow.Queries>first,"Only slow candidate stopped making progress after its first slice");
            yield return "PASS sole slow candidate continues the same route across slices without failure or cooldown";
            for(int i=0;i<20000 && !fixture.Selected;i++)fixture.Advance();
            Require(fixture.Selected && Vector3.Distance((Vector3)Get(fixture.animal,"goal"),SlowGoal)<.01f &&
                ReferenceEquals(Get(fixture.animal,"route"),fixture.slow.Result),"Sole slow candidate was abandoned or restarted instead of completed");
            yield return "PASS sole slow candidate eventually completes its original search; work units="+fixture.units;
        }
    }
}
