using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using Object=UnityEngine.Object;

public static class AnimalStorySightlineChecks
{
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    sealed class Fixture:IDisposable
    {
        public readonly Scene scene;
        public readonly Transform actor,group;
        public readonly Vector3 eye=new Vector3(72,1.4f,-49),view=new Vector3(98,.5f,-52);
        public Fixture()
        {
            scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("03 自然台地与连接");SceneManager.MoveGameObjectToScene(root,scene);
            group=new GameObject("石阶 · 跨水连接 · 自然岩岸").transform;group.SetParent(root.transform);
            actor=new GameObject("Fixture sheep").transform;SceneManager.MoveGameObjectToScene(actor.gameObject,scene);actor.position=eye-Vector3.up*.8f;
        }
        public BoxCollider Box(string name,Vector3 position,Vector3 size,Transform parent=null)
        {
            var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);go.transform.SetParent(parent?parent:group);
            go.transform.position=position;var box=go.AddComponent<BoxCollider>();box.size=size;Physics.SyncTransforms();return box;
        }
        public bool Clear(out Collider obstruction){Physics.SyncTransforms();return AnimalStorySightline.IsClear(eye,view,actor,out obstruction);}
        public void Dispose(){EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Physics.SyncTransforms();}
    }
    public static string[] Run()
    {
        Require(Application.isBatchMode && !Application.isPlaying && Application.dataPath.Contains("ValidationProject"),
            "Sightline fixtures require the independent ValidationProject batch editor");
        var results=new List<string>();
        using(var f=new Fixture())
        {
            Require(f.Clear(out _),"Empty validation scene blocked sea view");
            var east=f.Box("地图边界碰撞",new Vector3(91,15,0),new Vector3(2,70,180));
            var west=f.Box("地图边界碰撞",new Vector3(-85,15,0),new Vector3(2,70,180));
            var north=f.Box("地图边界碰撞",new Vector3(0,15,85),new Vector3(180,70,2));
            var south=f.Box("地图边界碰撞",new Vector3(0,15,-91),new Vector3(180,70,2));
            Require(f.scene.GetPhysicsScene().Raycast(f.eye,(f.view-f.eye).normalized,out RaycastHit raw,Vector3.Distance(f.eye,f.view)) && raw.collider==east,
                "Fixture did not reproduce the original raw east-boundary hit");
            Require(f.Clear(out _) && east.enabled && !east.isTrigger,"Viewing through a generated backstop changed or failed its movement collision");
            foreach(var pair in new[]{(west,new Vector3(-70,1,0),new Vector3(-95,1,0)),
                (north,new Vector3(0,1,70),new Vector3(0,1,95)),(south,new Vector3(0,1,-70),new Vector3(0,1,-100))})
                Require(f.scene.GetPhysicsScene().Raycast(pair.Item2,(pair.Item3-pair.Item2).normalized,out raw,Vector3.Distance(pair.Item2,pair.Item3)) &&
                    raw.collider==pair.Item1 && AnimalStorySightline.IsClear(pair.Item2,pair.Item3,f.actor,out _) && pair.Item1.enabled,
                    "A remaining generated backstop still occluded sight or changed collision");
            results.Add("PASS raw physics still hits all authored movement boundaries; only sea visibility treats the four exact invisible backstops as transparent");

            var wall=f.Box("Ordinary invisible wall proxy",new Vector3(82,1,-50),new Vector3(.5f,5,10));
            Require(!f.Clear(out Collider blocker) && blocker==wall,"Ordinary renderer-less wall was ignored");Object.DestroyImmediate(wall.gameObject);
            wall=f.Box("Wall behind invisible boundary",new Vector3(95,1,-51),new Vector3(.5f,5,10));
            Require(!f.Clear(out blocker) && blocker==wall,"First ignored boundary hid a real wall behind it");Object.DestroyImmediate(wall.gameObject);
            wall=f.Box("地图边界碰撞",new Vector3(82,1,-50),new Vector3(.5f,5,10));
            Require(!f.Clear(out blocker) && blocker==wall,"Name alone made a real wall transparent");Object.DestroyImmediate(wall.gameObject);
            results.Add("PASS ordinary invisible proxies, real walls behind backstops, and same-name ordinary colliders still block sea view");

            east.size=new Vector3(2,71,180);Require(!f.Clear(out blocker) && blocker==east,"Modified boundary shape was trusted as invisible");east.size=new Vector3(2,70,180);
            var renderer=east.gameObject.AddComponent<MeshRenderer>();Require(!f.Clear(out blocker) && blocker==east,"Rendered boundary-shaped object was ignored");Object.DestroyImmediate(renderer);
            f.group.name="Other structures";Require(!f.Clear(out blocker) && blocker==east,"Boundary-shaped object outside authored hierarchy was ignored");
            f.group.name="石阶 · 跨水连接 · 自然岩岸";
            Require(f.Clear(out _),"Restoring exact backstop contract did not restore visibility");
            results.Add("PASS the exception requires exact authored hierarchy, dimensions, pose and absence of renderers");

            var self=f.Box("Self body",Vector3.Lerp(f.eye,f.view,.1f),Vector3.one*.25f,f.actor);
            Require(f.Clear(out _),"Animal's own collider blocked its view");
            wall=f.Box("Real wall after self",new Vector3(82,1,-50),new Vector3(.5f,5,10));
            Require(!f.Clear(out blocker) && blocker==wall,"First self hit hid a real wall behind it");Object.DestroyImmediate(wall.gameObject);Object.DestroyImmediate(self.gameObject);
            for(int i=0;i<40;i++)f.Box("Self saturation "+i,Vector3.Lerp(f.eye,f.view,(i+1)/42f),Vector3.one*.1f,f.actor);
            Require(!f.Clear(out _),"Full hit buffer reported clear without checking every potential blocker");
            Require(!AnimalStorySightline.IsClear(f.eye,new Vector3(float.NaN,0,0),f.actor,out _) &&
                !AnimalStorySightline.IsClear(f.eye,new Vector3(float.PositiveInfinity,0,0),f.actor,out _) &&
                !AnimalStorySightline.IsClear(f.eye,f.eye,f.actor,out _),"Invalid or zero-length sightline was accepted");
            results.Add("PASS self hits never hide downstream blockers; a saturated fixed query buffer fails closed");
        }
        return results.ToArray();
    }
}
