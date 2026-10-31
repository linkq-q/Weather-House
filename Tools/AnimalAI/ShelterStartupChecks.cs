// Run in the independent ValidationProject's empty batch scene; routes and roof probes use real physics.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.Life;
using Object=UnityEngine.Object;

public static class ShelterStartupChecks
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static readonly Type Animal=typeof(AnimalAutonomy).GetNestedType("Animal",BindingFlags.NonPublic);
    static object Get(object o,string field)=>o.GetType().GetField(field,Flags).GetValue(o);
    static void Set(object o,string field,object value)=>o.GetType().GetField(field,Flags).SetValue(o,value);
    static object Call(object o,string method,params object[] args)=>o.GetType().GetMethod(method,Flags).Invoke(o,args);
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    sealed class Fixture:IDisposable
    {
        public readonly GameObject root;
        public readonly LifeWorld world;
        public readonly AnimalAutonomy director;
        public readonly WildlifeAgent agent;
        public readonly object state;
        public object query;
        public int units;
        public Fixture()
        {
            root=new GameObject("Shelter startup physical fixture");world=root.AddComponent<LifeWorld>();
            var player=new GameObject("Remote player").transform;player.SetParent(root.transform);player.position=Vector3.left*40;
            var population=root.AddComponent<WildlifePopulation>();population.worldBounds=new Bounds(Vector3.up*10,new Vector3(120,20,120));population.player=player;
            agent=new GameObject("Physical route animal").AddComponent<WildlifeAgent>();agent.transform.SetParent(root.transform);
            agent.scaleRange=Vector2.one;agent.timidity=0;agent.habitatRadius=40;population.agents=new[]{agent};population.Initialize();
            director=root.AddComponent<AnimalAutonomy>();director.Initialize(world,player);
            state=Activator.CreateInstance(Animal,true);Set(state,"agent",agent);Set(state,"root",agent.transform);Set(state,"species","rabbit");Set(state,"id","startup");
            Call(director,"Add",state);Set(state,"nextAI",float.PositiveInfinity);
        }
        public void Known(string id,Vector3 position)
        {
            var obj=new GameObject(id).AddComponent<LifeObject>();obj.transform.SetParent(root.transform);obj.transform.position=position;
            obj.Initialize(new LifeObjectState{id=id,sheltered=true},"Shelter");
            ((Dictionary<string,LifeObject>)Get(world,"objects")).Add(id,obj);
        }
        public void Box(string name,Vector3 position,Vector3 size)
        {
            var box=new GameObject(name).AddComponent<BoxCollider>();box.transform.SetParent(root.transform);box.transform.position=position;box.size=size;
            Physics.SyncTransforms();
        }
        public void Start()
        {
            director.Rain(Vector3.zero,32,true);query=Call(director,"StartShelterSearch",state);Set(state,"shelterQuery",query);Physics.SyncTransforms();
        }
        public bool Selected=>(string)Get(state,"action")=="shelter";
        public Vector3 Goal=>(Vector3)Get(state,"goal");
        public void Advance(){director.Rain(Vector3.zero,32,true);Call(director,"AdvancePlanning",state);units++;}
        public void Dispose(){Object.DestroyImmediate(root);Physics.SyncTransforms();}
    }
    public static IEnumerable<string> Run()
    {
        using(var f=new Fixture())
        {
            f.Known("Far point inserted first",Vector3.right*12);f.Known("Near known covered point",Vector3.forward*3);
            f.Box("Known point roof",new Vector3(0,2.5f,3),new Vector3(3,.2f,3));f.Start();
            for(int i=0;i<4&&!f.Selected;i++)f.Advance();
            Require(f.Selected&&Vector3.Distance(f.Goal,Vector3.forward*3)<.01f,
                "Known near body-safe shelter waited behind all roof probes or used insertion order instead of distance");
            Require((int)Get(f.query,"probe")==0,"Known direct route needlessly spent roof probe work first");
            Require(Get(f.state,"route") is List<Vector3> route&&route.Count>0,"Selected known shelter has no checked executable route");
            yield return "PASS nearest known shelter inserted second gets a real body-checked route in "+f.units+" units with0 roof probes";
        }
        using(var f=new Fixture())
        {
            f.Box("Previously unknown roof",new Vector3(0,2.5f,6),new Vector3(3,.2f,3));f.Start();
            Require(((List<Vector3>)Get(f.query,"candidates")).Count==0,"Unknown roof fixture accidentally supplied a known candidate");
            for(int i=0;i<40&&!f.Selected;i++)f.Advance();
            Require(f.Selected&&Mathf.Abs(f.Goal.x)<1.5f&&f.Goal.z>4.5f&&f.Goal.z<7.5f,
                "No-known-point fallback failed to discover and route to actual overhead roof within40 work units");
            Require((int)Get(f.query,"probe")>16&&(int)Get(f.query,"probe")<112,"Fallback did not use progressive real roof discovery");
            yield return "PASS with no known candidates, actual roof discovery and checked route finish in "+f.units+" units without scanning every distant ring";
        }
        using(var f=new Fixture())
        {
            f.Known("Slow route beyond long wall",Vector3.right*6);
            f.Box("Long solid wall",new Vector3(2,1.5f,0),new Vector3(.25f,3,24));
            f.Box("Unknown nearby roof on safe side",new Vector3(0,2.5f,6),new Vector3(3,.2f,3));f.Start();f.Advance();
            var slow=Get(f.query,"route") as AnimalLocalRoute;Require(slow!=null&&!slow.Finished,"Known difficult candidate was not started first");
            for(int i=0;i<400&&!f.Selected;i++)f.Advance();
            Require(f.Selected&&Mathf.Abs(f.Goal.x)<1.5f&&f.Goal.z>4.5f&&f.Goal.z<7.5f,
                "Sliced slow known route starved discovery of a nearby unknown roof");
            Require(!slow.Finished&&!((Dictionary<Vector3,float>)Get(f.state,"failedShelters")).ContainsKey(Vector3.right*6),
                "Yielding a slow route incorrectly completed/failed it instead of preserving the pending search");
            yield return "PASS long physical wall route is preserved while roof discovery gets fair work; easy unknown fallback wins in "+f.units+" units before slow search ends";
        }
    }
}
