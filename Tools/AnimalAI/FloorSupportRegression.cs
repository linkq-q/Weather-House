using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.Hillside;
using Object=UnityEngine.Object;

public static class FloorSupportRegression
{
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    sealed class Fixture:IDisposable
    {
        public GameObject root;
        public WildlifePopulation population;
        public WildlifeAgent animal;
        public Fixture()
        {
            root=new GameObject("Floor support regression");population=root.AddComponent<WildlifePopulation>();
            population.worldBounds=new Bounds(new Vector3(0,10,0),new Vector3(40,20,40));
            animal=new GameObject("Rabbit-sized body").AddComponent<WildlifeAgent>();animal.transform.SetParent(root.transform);
            animal.bodyRadius=.3f;animal.scaleRange=Vector2.one;animal.timidity=0;animal.sociability=0;
            population.agents=new[]{animal};
        }
        public BoxCollider Box(string name,Vector3 position,Vector3 size)
        {
            var box=new GameObject(name).AddComponent<BoxCollider>();box.transform.SetParent(root.transform);box.transform.position=position;box.size=size;
            Physics.SyncTransforms();return box;
        }
        public void Initialize(){Physics.SyncTransforms();population.Initialize();Physics.SyncTransforms();}
        public void MoveWithoutThinking(float dt)
        {
            float now=population.SimulationTime+dt;
            typeof(WildlifePopulation).GetProperty("SimulationTime").GetSetMethod(true).Invoke(population,new object[]{now});
            typeof(WildlifeAgent).GetMethod("Move",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(animal,new object[]{dt,now});
            Physics.SyncTransforms();
        }
        public void Dispose(){Object.DestroyImmediate(root);Physics.SyncTransforms();}
    }
    public static IEnumerable<string> Run()
    {
        Require(Application.isBatchMode && !Application.isPlaying && Application.dataPath.IndexOf("ValidationProject",StringComparison.OrdinalIgnoreCase)>=0,
            "Default-physics floor fixtures require the independent ValidationProject batch editor.");
        using(var f=new Fixture())
        {
            f.Initialize();
            Vector3 first=new Vector3(.8f,0,0),start=f.animal.transform.position;
            Require(f.animal.RequestAutonomousTarget(first,false),"First autonomous waypoint rejected");
            f.MoveWithoutThinking(.05f);
            Require(f.animal.transform.position.x>start.x+.01f && f.animal.State==WildlifeState.Wander,
                "Accepted first waypoint waited for a budgeted Think before moving");
            for(int i=0;i<30 && f.animal.State!=WildlifeState.Idle;i++)f.MoveWithoutThinking(.05f);
            Require(Vector3.Distance(f.animal.transform.position,first)<.22f && f.animal.State==WildlifeState.Idle,
                "Movement without Think did not stop at first waypoint");
            start=f.animal.transform.position;Vector3 second=start+Vector3.forward;
            Require(f.animal.RequestAutonomousTarget(second,false),"Second autonomous waypoint rejected");
            f.MoveWithoutThinking(.05f);
            Require(f.animal.transform.position.z>start.z+.01f && f.animal.State==WildlifeState.Wander,
                "Changing the accepted waypoint retained the old destination until Think");
            for(int i=0;i<35 && f.animal.State!=WildlifeState.Idle;i++)f.MoveWithoutThinking(.05f);
            Require(Vector3.Distance(f.animal.transform.position,second)<.22f && f.animal.State==WildlifeState.Idle,
                "Movement without Think did not stop at second waypoint");
            start=f.animal.transform.position;
            Require(f.animal.RequestAutonomousTarget(start,true),"Rest at current waypoint rejected");
            f.MoveWithoutThinking(.05f);
            Require(f.animal.State==WildlifeState.Idle && f.animal.Sleeping && f.animal.ActualSpeed==0 &&
                Vector3.Distance(f.animal.transform.position,start)<.001f,"Rest depended on Think or moved the resting animal");
            Vector3 blocked=start+Vector3.right;
            Require(f.animal.RequestAutonomousTarget(blocked,false),"Unobstructed goal rejected before wall was added");
            f.Box("Wall inserted after waypoint acceptance",start+new Vector3(.5f,1,0),new Vector3(.1f,2,3));
            for(int i=0;i<30;i++)f.MoveWithoutThinking(.05f);
            Require(f.animal.transform.position.x<start.x+.22f && f.animal.ActualSpeed==0,
                "Per-frame target handoff bypassed collision against a newly inserted wall");
            Require(!f.animal.RequestAutonomousTarget(blocked,false),"New waypoint request bypassed the wall");
            yield return "PASS zero Think calls: first and replacement waypoints move immediately, arrival/rest sleeps, new wall blocks accepted movement";
        }
        using(var f=new Fixture())
        {
            f.Box("Raised building floor",new Vector3(0,.305f,0),new Vector3(7.43f,.1f,3.58f));f.Initialize();
            Require(Mathf.Abs(f.animal.transform.position.y-.356f)<.002f,"Initialization snapped animal under the .355m floor");
            Require(f.population.TryMove(f.animal,f.animal.transform.position,out _),"Standing on floor rejected");
            Require(f.animal.RequestAutonomousTarget(new Vector3(1,.355f,0),false),"Safe floor goal rejected");
            Vector3 start=f.animal.transform.position;
            for(int i=0;i<10;i++)f.population.Simulate(.1f);
            Require(f.animal.transform.position.x>start.x+.2f,"Autonomous animal did not walk across floor");
            Require(Mathf.Abs(f.animal.transform.position.y-.356f)<.002f,"Movement sank through floor");
            f.Box("Wall on the floor",new Vector3(1.4f,1,0),new Vector3(.12f,2,3));
            Require(!f.population.TryMove(f.animal,new Vector3(2,.355f,0),out _),"Raised support ignored an adjacent wall");
            f.population.waterAreas=new[]{new HabitatVolume{center=new Vector3(-1,1,0),radii=Vector2.one*.5f}};
            Require(!f.population.TryLandingPosition(f.animal,new Vector3(-1,.355f,0),out _),"Floor projection bypassed water exclusion");
            yield return "PASS .355m raised floor: initialization on top, standing/real autonomous movement, wall blocked, water excluded";
        }
        using(var f=new Fixture())
        {
            f.Box("Small walkable floor",new Vector3(0,.305f,0),new Vector3(2,.1f,4));
            f.animal.transform.position=new Vector3(-2,0,0);f.Initialize();
            bool onFloor=false;
            for(int i=0;i<45;i++)
            {
                Require(f.animal.RequestAutonomousTarget(new Vector3(.6f,.355f,0),false),"Step onto floor rejected at "+f.animal.transform.position);
                f.population.Simulate(.1f);onFloor|=f.animal.transform.position.y>.35f;
            }
            Require(onFloor && f.animal.transform.position.x>.3f,"Animal failed to walk onto raised floor");
            for(int i=0;i<35;i++)
            {
                Require(f.animal.RequestAutonomousTarget(new Vector3(2,0,0),false),"Step down off floor rejected at "+f.animal.transform.position);
                f.population.Simulate(.1f);
            }
            Require(f.animal.transform.position.x>1.5f && Mathf.Abs(f.animal.transform.position.y)<.002f,"Animal did not return to terrain after walking off floor");
            yield return "PASS actual repeated autonomous locomotion steps onto and off .355m floor, returning to terrain";
        }
        using(var f=new Fixture())
        {
            f.Box("Step beneath low ceiling",new Vector3(0,.305f,0),new Vector3(2,.1f,4));
            f.Box("Low ceiling over step entry",new Vector3(-1.4f,.85f,0),new Vector3(.8f,.1f,4));
            f.animal.transform.position=new Vector3(-2,0,0);f.Initialize();
            Require(!f.population.TryMove(f.animal,new Vector3(.6f,.355f,0),out _),"Step traversal passed the animal's head through low ceiling");
            yield return "PASS lift/across/drop step rejects overhead ceiling";
        }
        using(var f=new Fixture())
        {
            f.Box("High solid ledge",new Vector3(1,.55f,0),new Vector3(1,1.1f,3));f.Initialize();
            Require(Mathf.Abs(f.animal.transform.position.y)<.001f,"Distant/high wall changed initial ground height");
            Require(!f.population.TryMove(f.animal,new Vector3(1,0,0),out _),"High ledge became an instant climb");
            var roof=f.Box("Overhead low roof",new Vector3(-2,.4f,0),new Vector3(1,.1f,2));
            Require(!f.population.TryMove(f.animal,new Vector3(-2,0,0),out _),"Raised floor support teleported through a slab side/underside");
            yield return "PASS high ledge and low slab side remain solid; no movement teleport";
        }
        using(var f=new Fixture())
        {
            var floor=f.Box("Too-steep floor",new Vector3(0,.3f,0),new Vector3(4,.1f,4));floor.transform.rotation=Quaternion.Euler(0,0,45);f.Initialize();
            Require(Mathf.Abs(f.animal.transform.position.y)<.001f,"Too-steep box was accepted as floor");
            Require(!f.population.TryMove(f.animal,Vector3.zero,out _),"Too-steep floor overlap was ignored");
            yield return "PASS slope limit preserved and intersecting steep surface remains blocked";
        }
    }
    public static void Batch()
    {
        int exit=0;var lines=new List<string>();
        try
        {
            Require(Application.isBatchMode && Application.dataPath.IndexOf("ValidationProject",StringComparison.OrdinalIgnoreCase)>=0,"Independent clone required");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            lines.AddRange(Run());
        }
        catch(Exception e){exit=1;lines.Add("FAIL "+e);}
        string report=Environment.GetEnvironmentVariable("WEATHERHOUSE_FLOOR_REPORT");if(!string.IsNullOrEmpty(report))File.WriteAllLines(report,lines);
        foreach(string line in lines)Debug.Log(line);if(Application.isBatchMode)EditorApplication.Exit(exit);
    }
}
