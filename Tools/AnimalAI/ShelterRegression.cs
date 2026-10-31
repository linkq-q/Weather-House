// Batch-only contracts; stage into an Editor folder for the test run.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.Life;
using Object = UnityEngine.Object;

public static class ShelterRegression
{
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Type AnimalType = typeof(AnimalAutonomy).GetNestedType("Animal", BindingFlags.NonPublic);
    static readonly MethodInfo Tick = typeof(AnimalAutonomy).GetMethod("Tick", Hidden);
    static readonly MethodInfo Advance = typeof(AnimalAutonomy).GetMethod("AdvancePlanning", Hidden);
    static object Get(object state, string name) => AnimalType.GetField(name).GetValue(state);
    static void Set(object state, string name, object value) => AnimalType.GetField(name).SetValue(state, value);
    static void Require(bool ok, string description) { if (!ok) throw new InvalidOperationException(description); }

    sealed class Fixture : IDisposable
    {
        public readonly GameObject root;
        public readonly AnimalAutonomy director;
        public readonly WildlifePopulation population;
        public readonly WildlifeAgent agent;
        public readonly LifeWorld world;
        public readonly Transform player;
        public readonly object state;
        public int units;
        public Fixture()
        {
            root = new GameObject("Shelter contract fixture"); world = root.AddComponent<LifeWorld>();
            player = new GameObject("Distant test player").transform; player.SetParent(root.transform); player.position = new Vector3(-40, 0, -40);
            population = root.AddComponent<WildlifePopulation>(); population.player = player;
            population.worldBounds = new Bounds(new Vector3(0, 10, 0), new Vector3(120, 20, 120));
            agent = new GameObject("Contract animal").AddComponent<WildlifeAgent>(); agent.transform.SetParent(root.transform);
            agent.timidity = 0; agent.scaleRange = Vector2.one; agent.habitatRadius = 40;
            population.agents = new[] { agent }; population.Initialize();
            director = root.AddComponent<AnimalAutonomy>(); director.Initialize(world, player);
            typeof(AnimalAutonomy).GetField("client", Hidden).SetValue(director, root.AddComponent<AnimalPlannerClient>());
            state = Activator.CreateInstance(AnimalType, true);
            Set(state, "agent", agent); Set(state, "root", agent.transform); Set(state, "species", "rabbit"); Set(state, "id", "contract");
            typeof(AnimalAutonomy).GetMethod("Add", Hidden).Invoke(director, new[] { state }); Set(state, "nextAI", float.PositiveInfinity);
        }
        public void Shelter(string id, Vector3 point)
        {
            var obj = new GameObject(id).AddComponent<LifeObject>(); obj.transform.SetParent(root.transform); obj.transform.position = point;
            obj.Initialize(new LifeObjectState { id = id, sheltered = true }, "Shelter");
            var objects = (Dictionary<string, LifeObject>)typeof(LifeWorld).GetField("objects", Hidden).GetValue(world); objects.Add(id, obj);
        }
        public void Box(string name, Vector3 center, Vector3 size)
        {
            var box = new GameObject(name).AddComponent<BoxCollider>(); box.transform.SetParent(root.transform); box.transform.position = center; box.size = size;
        }
        public Vector3 Search()
        {
            Physics.SyncTransforms();
            for (units = 0; units < 20000; units++)
            {
                director.Rain(Vector3.zero, 80, true);
                if (units % 8 == 0) Tick.Invoke(director, new[] { state });
                Advance?.Invoke(director, new[] { state });
                if ((string)Get(state, "action") == "shelter" && Get(state, "route") is List<Vector3>) return (Vector3)Get(state, "goal");
            }
            throw new InvalidOperationException("No reachable shelter plan after 20000 budgeted work units; action=" + Get(state, "action"));
        }
        public void Dispose() => Object.DestroyImmediate(root);
    }
    public static IEnumerable<string> Run()
    {
        using (var fixture = new Fixture())
        {
            Vector3 expected = new Vector3(24, 0, 0); fixture.Shelter("Shelter 24m away", expected);
            Vector3 selected = fixture.Search();
            Require(Vector3.Distance(selected, expected) < .01f, "A safe shelter beyond the old 18m cutoff was not selected.");
            yield return "PASS shelter at 24m accepted with a checked route; work units=" + fixture.units;
        }
        using (var fixture = new Fixture())
        {
            fixture.Shelter("Blocked nearest shelter", Vector3.right * 3);
            fixture.Box("Solid obstacle covering nearest destination", new Vector3(3, 1, 0), new Vector3(2, 2, 2));
            Vector3 expected = Vector3.forward * 6; fixture.Shelter("Reachable alternate", expected);
            Vector3 selected = fixture.Search();
            Require(Vector3.Distance(selected, expected) < .01f, "Unreachable nearest candidate prevented selection of the reachable alternate.");
            yield return "PASS unreachable nearest shelter skipped, reachable alternate selected; work units=" + fixture.units;
        }
        using (var fixture = new Fixture())
        {
            fixture.Box("Nearby fence without cover", new Vector3(3, 1.1f, 0), new Vector3(.2f, 2.2f, 3));
            fixture.Box("Reachable roof 6m away", new Vector3(0, 2.5f, 6), new Vector3(5, .2f, 5));
            Vector3 selected = fixture.Search();
            Require(Mathf.Abs(selected.x) < 2.5f && selected.z > 3.5f && selected.z < 8.5f,
                "Roof search selected a fence or a position outside the roof footprint: " + selected);
            yield return "PASS upward roof search ignores fence top and selects real overhead cover; goal=" + selected + "; work units=" + fixture.units;
        }
        using (var fixture = new Fixture())
        {
            fixture.Box("Wall blocks the moving player follow target", new Vector3(2.5f, 1.5f, 0), new Vector3(.3f, 3, 12));
            // An already-known direct shelter now completes in one unit. Require real discovery here so the
            // competing follow search is repeatedly rebuilt before shelter planning can legitimately finish.
            fixture.Box("Undiscovered safe roof away from the wall", new Vector3(0,2.5f,6), new Vector3(1,.2f,1));
            fixture.player.position=new Vector3(8,0,2);Set(fixture.state,"outgoing",true);
            Set(fixture.state,"action","follow");Set(fixture.state,"until",float.PositiveInfinity);
            Vector3 continuingTarget=Vector3.forward*2;
            Set(fixture.state,"goal",continuingTarget);Set(fixture.state,"actionDuration",20f);
            Physics.SyncTransforms();
            Require(fixture.agent.RequestAutonomousTarget(continuingTarget,false),"Existing movement lease was rejected");
            var targetField=typeof(WildlifeAgent).GetField("lifeTarget",Hidden);
            AnimalLocalRoute previous=null;int replacements=0;bool sheltered=false;Vector3 before=fixture.agent.transform.position;
            for(int unit=0;unit<180;unit++)
            {
                fixture.director.Rain(Vector3.zero,80,true);
                if(unit%2==0)
                {
                    fixture.player.position=new Vector3(8,0,unit%4==0?2:-2);
                    // Edit-mode fixture: release the two-second retarget cooldown without blocking on wall time.
                    Set(fixture.state,"nextRoute",0f);Tick.Invoke(fixture.director,new[]{fixture.state});
                    var pending=Get(fixture.state,"search") as AnimalLocalRoute;
                    if(pending!=null && pending!=previous){replacements++;previous=pending;}
                }
                Advance.Invoke(fixture.director,new[]{fixture.state});
                if((string)Get(fixture.state,"action")=="shelter"){sheltered=true;break;}
                Require(fixture.agent.AutonomousTarget && fixture.agent.HasLifeTarget &&
                    Vector3.Distance((Vector3)targetField.GetValue(fixture.agent),continuingTarget)<.001f,
                    "Pending shelter search replaced or released the current movement lease");
                fixture.population.Simulate(.01f);
            }
            Require(replacements>3,"Fixture did not repeatedly rebuild the old follow search");
            Require(sheltered,"Continuously replanned follow path starved urgent shelter search");
            Require(Vector3.Distance(before,fixture.agent.transform.position)>.05f,"Existing movement froze while shelter planning was pending");
            Require(Vector3.Distance((Vector3)Get(fixture.state,"goal"),Vector3.forward*6)<.01f,"Wrong shelter chosen");
            yield return "PASS moving-player follow replans cannot starve actual roof discovery; current lease and movement continue until shelter route is ready; replacements="+replacements;
        }
        using (var fixture = new Fixture())
        {
            Set(fixture.state,"action","shelter");Set(fixture.state,"wasRain",true);
            Set(fixture.state,"suspendedAction","sleep");Set(fixture.state,"suspendedGoal",Vector3.forward*24);Set(fixture.state,"suspendedRemaining",2f);
            Tick.Invoke(fixture.director,new[]{fixture.state});
            Require((string)Get(fixture.state,"action")=="sleep" && float.IsPositiveInfinity((float)Get(fixture.state,"until")),
                "Restored far-away sleep consumed its remaining duration while still planning");
            Require(Mathf.Abs((float)Get(fixture.state,"actionDuration")-2)<.001f,"Restored action forgot its remaining duration");
            for(int unit=0;unit<12;unit++)Advance.Invoke(fixture.director,new[]{fixture.state});
            Tick.Invoke(fixture.director,new[]{fixture.state});
            Require(Get(fixture.state,"route") is List<Vector3> && Mathf.Abs((float)Get(fixture.state,"until")-Time.time-2)<.01f,
                "Restored action timer did not start with its saved duration when the route became executable");
            yield return "PASS restored sleep preserves remaining duration through planning, starts timer when route is ready";
        }
        foreach(string action in new[]{"approach","follow","avoid"})
        using (var fixture = new Fixture())
        {
            bool outgoing=action!="avoid";Set(fixture.state,"outgoing",outgoing);
            fixture.player.position=Vector3.right*(outgoing?6:2);
            Set(fixture.state,"action","shelter");Set(fixture.state,"wasRain",true);
            Set(fixture.state,"suspendedAction",action);Set(fixture.state,"suspendedGoal",Vector3.left*9);Set(fixture.state,"suspendedRemaining",4f);
            Tick.Invoke(fixture.director,new[]{fixture.state});
            Vector3 expected=fixture.player.position-Vector3.right*((outgoing?2.6f:4.5f)+fixture.agent.Radius);
            Require((string)Get(fixture.state,"action")==action && Vector3.Distance((Vector3)Get(fixture.state,"goal"),expected)<.001f,
                "Rain-stop restoration used the old player position for "+action);
        }
        yield return "PASS restored approach, follow and avoid use the player's current position";
        yield return CheckUpdateScheduling();
    }
    static string CheckUpdateScheduling()
    {
        MethodInfo update=typeof(AnimalAutonomy).GetMethod("Update",Hidden);
        MethodInfo add=typeof(AnimalAutonomy).GetMethod("Add",Hidden);
        MethodInfo clock=typeof(AnimalWorkBudget).GetMethod("SetTestClock",BindingFlags.Static|BindingFlags.NonPublic);
        Require(clock!=null,"Editor budget clock seam missing");
        int frame=0;
        using (var fixture=new Fixture())
        try
        {
            fixture.player.position=Vector3.zero;fixture.agent.transform.position=Vector3.right*30;
            var nearby=new WildlifeAgent[8];var states=new object[8];var all=new WildlifeAgent[9];all[0]=fixture.agent;
            for(int i=0;i<nearby.Length;i++)
            {
                var agent=new GameObject("Continuously busy near animal "+i).AddComponent<WildlifeAgent>();agent.transform.SetParent(fixture.root.transform);
                float angle=i*Mathf.PI/4;agent.transform.position=new Vector3(Mathf.Cos(angle)*8,0,Mathf.Sin(angle)*8);
                agent.timidity=0;agent.scaleRange=Vector2.one;nearby[i]=agent;all[i+1]=agent;
            }
            fixture.population.agents=all;fixture.population.Initialize();
            Set(fixture.state,"externalRevision",fixture.agent.IntentRevision);
            for(int i=0;i<nearby.Length;i++)
            {
                object state=Activator.CreateInstance(AnimalType,true);states[i]=state;
                Set(state,"agent",nearby[i]);Set(state,"root",nearby[i].transform);Set(state,"species","rabbit");Set(state,"id","busy-near-"+i);
                add.Invoke(fixture.director,new[]{state});Set(state,"nextAI",float.PositiveInfinity);Set(state,"due",float.PositiveInfinity);
            }
            Set(fixture.state,"outgoing",true);Set(fixture.state,"action","sleep");Set(fixture.state,"goal",fixture.agent.transform.position+Vector3.forward*18);
            Set(fixture.state,"until",float.PositiveInfinity);Set(fixture.state,"actionDuration",100f);
            bool Blocked(Vector3 from,Vector3 to,out Vector3 foot){foot=to;return false;}
            void KeepNearSearchesBusy()
            {
                for(int i=0;i<states.Length;i++)
                {
                    var search=Get(states[i],"search") as AnimalLocalRoute;
                    if(search==null || search.Finished)
                        Set(states[i],"search",new AnimalLocalRoute(nearby[i].transform.position,nearby[i].transform.position+Vector3.forward*12,nearby[i].Radius,Blocked));
                }
            }
            clock.Invoke(null,new object[]{(Func<double>)(()=>0),(Func<int>)(()=>frame)});
            var leaseUntil=typeof(WildlifeAgent).GetField("lifeUntil",Hidden);
            AnimalLocalRoute farSearch=null;int renewals=0;float lastLease=0;Vector3 before=fixture.agent.transform.position;
            for(int i=0;i<60;i++)
            {
                frame++;KeepNearSearchesBusy();Set(fixture.state,"due",-1f);
                fixture.population.Simulate(.05f);
                update.Invoke(fixture.director,null);
                if(Get(fixture.state,"search") is AnimalLocalRoute pending)farSearch=pending;
                float lease=(float)leaseUntil.GetValue(fixture.agent);if(lease>lastLease){renewals++;lastLease=lease;}
            }
            Require(farSearch!=null && farSearch.Queries>=3,"Eight nearby pending searches starved the >24m animal's real Update/AdvancePlanning");
            Require(renewals>=4 && Vector3.Distance(before,fixture.agent.transform.position)>.2f,
                "Far animal planned but its movement lease was not continuously executed/renewed under nearby search load");
            // Now saturate the same queue with already-planned movement; test the rejected work item's due/cursor.
            void Ready(object state,WildlifeAgent agent)
            {
                agent.ReleaseAutonomousTarget();Vector3 target=agent.transform.position+Vector3.forward*2;
                Set(state,"outgoing",true);Set(state,"action","sleep");Set(state,"goal",target);
                Set(state,"route",new List<Vector3>{target});Set(state,"routeStep",0);Set(state,"search",null);
                Set(state,"until",float.PositiveInfinity);Set(state,"actionDuration",100f);Set(state,"beganMoving",true);Set(state,"due",-1f);
            }
            Ready(fixture.state,fixture.agent);for(int i=0;i<states.Length;i++)Ready(states[i],nearby[i]);
            typeof(AnimalAutonomy).GetField("cursor",Hidden).SetValue(fixture.director,1);
            float waitingFatigue=(float)Get(states[4],"fatigue");
            frame++;update.Invoke(fixture.director,null);
            Require((float)Get(states[4],"due")<Time.time && (int)typeof(AnimalAutonomy).GetField("cursor",Hidden).GetValue(fixture.director)==5,
                "Budget-rejected fifth movement was delayed by .3s or its queue position was lost");
            Require((float)Get(states[4],"fatigue")==waitingFatigue,"Budget-rejected movement accumulated fatigue again before being serviced");
            renewals=0;lastLease=(float)leaseUntil.GetValue(fixture.agent);before=fixture.agent.transform.position;
            for(int i=0;i<30;i++)
            {
                frame++;Set(fixture.state,"due",-1f);foreach(object state in states)Set(state,"due",-1f);
                fixture.population.Simulate(.05f);update.Invoke(fixture.director,null);
                float lease=(float)leaseUntil.GetValue(fixture.agent);if(lease>lastLease){renewals++;lastLease=lease;}
            }
            Require(renewals>=4 && Vector3.Distance(before,fixture.agent.transform.position)>.2f,
                "Eight nearby completed routes starved the >24m animal's movement execution");
            return "PASS real Update queue: >24m animal advances and renews movement under eight busy near searches/routes; budget denial preserves due time and cursor";
        }
        finally{clock.Invoke(null,new object[]{null,null});}
    }
    public static void Batch()
    {
        if (!Application.isBatchMode || EditorApplication.isPlaying) throw new InvalidOperationException("Use an independent batch Editor run.");
        string report = Environment.GetEnvironmentVariable("WEATHERHOUSE_SHELTER_REPORT");
        if (string.IsNullOrEmpty(report)) throw new InvalidOperationException("Set WEATHERHOUSE_SHELTER_REPORT.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var results = new List<string>(); int exit = 0;
        try { results.AddRange(Run()); }
        catch (Exception error) { results.Add("FAIL " + error); exit = 1; }
        File.WriteAllLines(report, results); EditorApplication.Exit(exit);
    }
}
