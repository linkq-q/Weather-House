// Actual body/obstacle regression for avoiding the duplicate post-move rotation query.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using WeatherHouse.Hillside;
using Object=UnityEngine.Object;

public static class AnimalTurnChecks
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,Flags).SetValue(target,value);
    sealed class Fixture:IDisposable
    {
        public readonly GameObject root;
        public readonly WildlifePopulation population;
        public readonly WildlifeAgent animal;
        public Fixture(float yaw)
        {
            root=new GameObject("Actual turn body fixture");population=root.AddComponent<WildlifePopulation>();
            population.worldBounds=new Bounds(Vector3.up*10,new Vector3(40,20,40));
            animal=new GameObject("Long animal body").AddComponent<WildlifeAgent>();animal.transform.SetParent(root.transform);
            var visual=GameObject.CreatePrimitive(PrimitiveType.Cube);visual.transform.SetParent(animal.transform,false);
            visual.transform.localPosition=Vector3.up*.3f;visual.transform.localScale=new Vector3(.4f,.6f,2);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            animal.scaleRange=Vector2.one;animal.walkSpeed=2;animal.timidity=animal.sociability=0;
            population.agents=new[]{animal};population.Initialize();animal.transform.rotation=Quaternion.Euler(0,yaw,0);
            Set(animal,"speedVariation",1f);Set(animal,"separation",Vector3.zero);
            Require(animal.BodyEnvelope.measured&&animal.BodyEnvelope.half.z>.9f,"Fixture did not measure the real elongated renderer body");
            Physics.SyncTransforms();
        }
        public void Target(Vector3 target)
        {
            typeof(WildlifeAgent).GetProperty("State").GetSetMethod(true).Invoke(animal,new object[]{WildlifeState.Wander});
            Set(animal,"destination",target);
        }
        public void Wall(Vector3 center,Vector3 size)
        {
            var wall=new GameObject("Solid turn blocker").AddComponent<BoxCollider>();wall.transform.SetParent(root.transform);
            wall.transform.position=center;wall.size=size;Physics.SyncTransforms();
        }
        public void Move()=>typeof(WildlifeAgent).GetMethod("Move",Flags).Invoke(animal,new object[]{.1f,1f});
        public void Dispose(){Object.DestroyImmediate(root);Physics.SyncTransforms();}
    }
    public static IEnumerable<string> Run()
    {
        using(var f=new Fixture(30))
        {
            f.Target(Vector3.forward*5);Vector3 before=f.animal.transform.position;f.Move();
            Require(f.animal.ActualSpeed>1.9f&&Vector3.Distance(before,f.animal.transform.position)>.19f&&
                Quaternion.Angle(f.animal.transform.rotation,Quaternion.Euler(0,15,0))<.05f,
                "Accepted physical movement lost its already-validated and applied15-degree heading");
            yield return "PASS open ground: measured long body moves0.2m and applies its validated15-degree heading";
        }
        using(var f=new Fixture(30))
        {
            f.Target(Vector3.forward*5);f.Wall(new Vector3(0,1,1.15f),new Vector3(8,2,.1f));
            Vector3 before=f.animal.transform.position;Quaternion proposed=Quaternion.Euler(0,15,0);
            Require(!f.population.TryMove(f.animal,before+Vector3.forward*.2f,out _,proposed)&&
                !f.population.TryMove(f.animal,before+Vector3.forward*.2f,out _,f.animal.transform.rotation),
                "Fixture does not block both translated poses");
            Require(f.population.CanRotate(f.animal,proposed),"Fixture also blocked the intended safe stationary turn");
            f.Move();Require(f.animal.ActualSpeed==0&&Vector3.Distance(before,f.animal.transform.position)<.001f&&
                Quaternion.Angle(f.animal.transform.rotation,proposed)<.05f,
                "A blocked translation no longer permits a separately checked safe turn in place");
            yield return "PASS actual wall blocks both movement attempts but safe in-place turn still runs body checks and changes heading";
        }
        using(var f=new Fixture(0))
        {
            f.Target(Vector3.right*5);f.Wall(new Vector3(.4f,1,0),new Vector3(.1f,2,8));
            Vector3 before=f.animal.transform.position;Quaternion original=f.animal.transform.rotation,proposed=Quaternion.Euler(0,15,0);
            Require(!f.population.CanRotate(f.animal,proposed),"Fixture does not obstruct the elongated body's turn");
            f.Move();Require(f.animal.ActualSpeed==0&&Vector3.Distance(before,f.animal.transform.position)<.001f&&
                Quaternion.Angle(f.animal.transform.rotation,original)<.05f,"Blocked animal turned or translated its full body through the wall");
            yield return "PASS actual close side wall rejects the body's turn as well as translation; optimization never applies an unchecked heading";
        }
    }
}
