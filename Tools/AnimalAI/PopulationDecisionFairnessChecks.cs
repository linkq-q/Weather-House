using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.Hillside;
using Object=UnityEngine.Object;

public static class PopulationDecisionFairnessChecks
{
    const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    static readonly MethodInfo TestClock=typeof(AnimalWorkBudget).GetMethod("SetTestClock",BindingFlags.Static|BindingFlags.NonPublic);
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    sealed class Fixture:IDisposable
    {
        public readonly GameObject root;
        public readonly WildlifePopulation[] populations=new WildlifePopulation[3];
        public readonly int[] totals=new int[3];
        public int frame;
        double clock;
        readonly double cost;
        public Fixture(double unitCost,int lastSize=3)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            root=new GameObject("Actual multi-population fairness fixture");cost=unitCost;
            TestClock.Invoke(null,new object[]{(Func<double>)(()=>clock+=cost),(Func<int>)(()=>frame)});
            int[] sizes={50,2,lastSize};
            for(int p=0;p<populations.Length;p++)
            {
                var go=new GameObject("Population "+p);go.transform.SetParent(root.transform);
                var population=go.AddComponent<WildlifePopulation>();populations[p]=population;
                population.worldBounds=new Bounds(new Vector3(50,10,0),new Vector3(200,20,200));population.decisionInterval=.2f;
                population.agents=new WildlifeAgent[sizes[p]];
                for(int i=0;i<sizes[p];i++)
                {
                    var animal=new GameObject("Animal "+p+"-"+i).AddComponent<WildlifeAgent>();animal.transform.SetParent(go.transform);
                    animal.transform.position=new Vector3(p*55+(i%10)*2,0,(i/10)*2);
                    animal.spawnId=p*100+i;animal.groupId=p;animal.scaleRange=Vector2.one;animal.timidity=animal.sociability=0;
                    animal.habitatCenter=animal.transform.position;animal.habitatRadius=30;
                    population.agents[i]=animal;
                }
                population.Initialize();
                foreach(var animal in population.agents)
                    typeof(WildlifeAgent).GetMethod("SetState",Flags).Invoke(animal,new object[]{WildlifeState.Idle,0f,float.PositiveInfinity});
            }
            Physics.SyncTransforms();
        }
        public int Advance()
        {
            frame++;int count=0;
            // Fixed order intentionally matches Unity's reproducible starvation trigger: large group always updates first.
            for(int p=0;p<populations.Length;p++)
            {
                WildlifePopulation population=populations[p];if(!population || !population.isActiveAndEnabled)continue;
                population.Simulate(.05f);count+=population.LastDecisionCount;totals[p]+=population.LastDecisionCount;
            }
            Require(count<=4,"Fairness increased the shared four-decision start cap");
            if(cost>.5)Require(count<=2,"Fairness bypassed the existing one-millisecond decision time cap");
            return count;
        }
        public void Dispose()
        {
            TestClock.Invoke(null,new object[]{null,null});Object.DestroyImmediate(root);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        }
    }
    static string SharedWork(double cost,int lastSize=3)
    {
        using(var f=new Fixture(cost,lastSize))
        {
            int[] recent=new int[3];
            for(int i=0;i<120;i++)
            {
                int count=f.Advance();
                if(lastSize==2)Require(count>0,"A stale queued turn wasted a frame although the first real population still has overdue Think work");
                if(i%20!=19)continue;
                for(int p=0;p<3;p++)
                {
                    Require(f.totals[p]>recent[p],"Actual population "+p+" received no Think work during a full second at20FPS; totals="+
                        string.Join(",",f.totals)+"; first population updates before both later populations");
                    recent[p]=f.totals[p];
                }
            }
            foreach(var population in f.populations)
            {
                var due=(float[])typeof(WildlifePopulation).GetField("decisionDue",Flags).GetValue(population);
                foreach(float value in due)Require(value>.2f,"An individual never reached actual Think inside its population's rotating queue");
            }
            return "PASS three real populations (50+2+"+lastSize+"), fixed update order at20FPS: actual Think counts="+string.Join(",",f.totals)+
                "; every population advances every observed second and every individual is served; cost="+cost+"ms/unit preserves4starts/1ms";
        }
    }
    static string RemovedOwner(bool destroy)
    {
        using(var f=new Fixture(.1))
        {
            f.Advance();f.Advance();
            // The first saturated large population also queues itself, so the later population becomes head after frame two.
            var waiting=typeof(AnimalWorkBudget).GetField("decisionWaiters",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null) as IList;
            if(waiting!=null)Require(waiting.Count>0 && (int)waiting[0]==f.populations[1].GetInstanceID() && f.totals[1]==0,
                "Removal fixture must destroy/disable the next queued owner before it receives any actual Think work");
            if(destroy)Object.DestroyImmediate(f.populations[1].gameObject);else f.populations[1].enabled=false;
            int beforeFirst=f.totals[0],beforeLast=f.totals[2];
            for(int i=0;i<20;i++)f.Advance();
            Require(f.totals[0]>beforeFirst && f.totals[2]>beforeLast,
                "A "+(destroy?"destroyed":"disabled")+" queued owner indefinitely blocked other populations");
            // Inspect storage only when this implementation exposes it; old runtime still reaches the actual starvation assertion above.
            var pending=typeof(AnimalWorkBudget).GetField("decisionWaitingFrames",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null) as IDictionary;
            if(pending!=null)Require(pending.Count<=2,"Inactive owner was retained beyond the finite request lifetime");
            return "PASS "+(destroy?"destroyed":"disabled")+" queued population cannot retain a turn; surviving real populations resume and stale owner storage expires";
        }
    }
    static string NestedOwner()
    {
        MethodInfo begin=typeof(AnimalWorkBudget).GetMethod("TryBeginDecision");Require(begin!=null,"Producer-aware decision entry missing");
        int frame=1;double now=0;
        bool Owner(int id,out AnimalWorkBudget.Scope scope)
        {
            object[] arguments={id,null};bool accepted=(bool)begin.Invoke(null,arguments);scope=(AnimalWorkBudget.Scope)arguments[1];return accepted;
        }
        TestClock.Invoke(null,new object[]{(Func<double>)(()=>now),(Func<int>)(()=>frame)});
        try
        {
            for(int i=0;i<4;i++){Require(AnimalWorkBudget.TryBegin(AnimalWorkBudget.Kind.Decision,out var scope),"Setup could not fill decision starts");scope.Dispose();}
            Require(!Owner(20,out _),"Setup failed to queue the waiting population");
            frame++;
            Require(AnimalWorkBudget.TryBegin(AnimalWorkBudget.Kind.Planning,out var outer),"Outer planning scope denied");
            using(outer){Require(Owner(10,out var nested),"Nested producer was wrongly blocked by a different reserved owner");using(nested)now=.1;}
            Require(Owner(20,out var reserved),"Nested producer consumed or altered the waiting population's reserved turn");reserved.Dispose();
            for(int i=0;i<3;i++)
            {
                Require(AnimalWorkBudget.TryBegin(AnimalWorkBudget.Kind.Planning,out var plan),"Nested producer consumed an extra planning start");plan.Dispose();
                Require(AnimalWorkBudget.TryBegin(AnimalWorkBudget.Kind.Decision,out var decision),"Nested producer consumed an extra decision start");decision.Dispose();
            }
            Require(!AnimalWorkBudget.TryBegin(AnimalWorkBudget.Kind.Planning,out _) && !AnimalWorkBudget.TryBegin(AnimalWorkBudget.Kind.Decision,out _),
                "Nested fairness work increased either lane's start allowance");
            return "PASS nested producer work retains outer-lane accounting, does not consume another population's reserved turn, and preserves both four-start caps";
        }
        finally{TestClock.Invoke(null,new object[]{null,null});}
    }
    public static string[] Run()
    {
        Require(Application.isBatchMode && !Application.isPlaying && Application.dataPath.Contains("ValidationProject"),
            "Independent ValidationProject batch editor only");
        Require(TestClock!=null,"Existing deterministic work-budget clock is required");
        return new[]{SharedWork(.1),SharedWork(.6),SharedWork(.1,2),RemovedOwner(false),RemovedOwner(true),NestedOwner()};
    }
    public static void Batch()
    {
        string report=Environment.GetEnvironmentVariable("WEATHERHOUSE_POPULATION_FAIRNESS_REPORT");
        Require(!string.IsNullOrEmpty(report) && !File.Exists(report),"Use a new WEATHERHOUSE_POPULATION_FAIRNESS_REPORT file");
        var results=new List<string>();int exit=0;
        try{results.AddRange(Run());results.Add("RESULT failures=0");}
        catch(Exception error){results.Add("FAIL "+error);exit=1;}
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report)));File.WriteAllLines(report,results);
        EditorApplication.Exit(exit);
    }
}
