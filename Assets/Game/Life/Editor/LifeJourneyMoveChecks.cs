using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using WeatherHouse.WildlifeJourney;

namespace WeatherHouse.Life.Editor
{
    public static class LifeJourneyMoveChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        [Serializable] sealed class Measurement{public string scenario;public int originalSamples,optimizedSamples;public Vector3 originalEnd,optimizedEnd;}
        [Serializable] sealed class Report{public string status;public List<Measurement> samples=new List<Measurement>();}
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Movement query check requires Edit mode.");
            Scene originalScene=SceneManager.GetActiveScene();
            Scene fixture=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(fixture);
            var root=new GameObject("Journey query isolated fixture");SceneManager.MoveGameObjectToScene(root,fixture);
            var report=new Report();TerrainData terrainData=null;
            try
            {
                Require(fixture.GetPhysicsScene()==Physics.defaultPhysicsScene,"Collision fixture must share the production default physics scene.");
                int reads=0;var navigation=root.AddComponent<JourneyNavigation>();
                navigation.surfaceSampler=p=>{reads++;return new JourneySurface{valid=true,groundY=0};};navigation.Rebuild();
                var body=new GameObject("moving body");body.transform.SetParent(root.transform);Vector3 start=new Vector3(-1200,0,-1200);body.transform.position=start;
                var herd=root.AddComponent<JourneyHerd>();herd.navigation=navigation;herd.autoAdvance=false;
                var member=new JourneyMember{id=1,root=body.transform,bodyLength=.6f,profile=new JourneyProfile{bodyRadius=.2f,walkSpeed=1}};
                var otherBody=new GameObject("live neighbour");otherBody.transform.SetParent(root.transform);otherBody.transform.position=start+Vector3.right*5;
                var other=new JourneyMember{id=2,root=otherBody.transform,bodyLength=.6f,profile=new JourneyProfile{bodyRadius=.2f,walkSpeed=1}};
                herd.members=new[]{member,other};herd.sites=new[]{new JourneyActivitySite{center=start,radius=6}};herd.Initialize();
                foreach(float yaw in new[]{0f,30f})
                {
                    Quaternion facing=Quaternion.Euler(0,yaw,0);Vector3 target=start+Vector3.forward*.1f;
                    Reset(member,start,facing);reads=0;LegacyMove(herd,member,target,.1f);
                    var sample=new Measurement{scenario="clear floor, yaw "+yaw,originalSamples=reads,originalEnd=member.root.position};
                    Quaternion expectedRotation=member.root.rotation;float expectedSpeed=member.ActualSpeed;
                    Reset(member,start,facing);reads=0;Call(herd,"Move",member,target,.1f,null);
                    sample.optimizedSamples=reads;sample.optimizedEnd=member.root.position;report.samples.Add(sample);
                    Require(sample.originalEnd==sample.optimizedEnd && expectedRotation==member.root.rotation && Mathf.Approximately(expectedSpeed,member.ActualSpeed),"Optimized movement changed the original displacement, heading or speed.");
                    Require(sample.optimizedSamples<sample.originalSamples,"Optimization did not reduce actual surface-sampler calls.");
                }
                // The injectable sampler deliberately skips PhysX; switch to a real flat terrain for collision assertions.
                terrainData=new TerrainData{heightmapResolution=33,size=new Vector3(12,1,12)};
                GameObject ground=Terrain.CreateTerrainGameObject(terrainData);ground.transform.SetParent(root.transform);ground.transform.position=start-new Vector3(6,0,6);
                navigation.terrain=ground.GetComponent<Terrain>();navigation.surfaceSampler=null;navigation.groundMask=0;
                var obstacle=new GameObject("static body obstruction");obstacle.transform.SetParent(root.transform);obstacle.transform.position=start+Vector3.up*.4f;
                var collider=obstacle.AddComponent<BoxCollider>();collider.size=new Vector3(.5f,.8f,.5f);Physics.SyncTransforms();
                Require(Physics.OverlapBox(obstacle.transform.position,Vector3.one*.1f).Contains(collider),"Default physics did not observe the real obstacle fixture.");
                Reset(member,start,Quaternion.identity);reads=0;LegacyMove(herd,member,start+Vector3.forward*.1f,.1f);
                var blocked=new Measurement{scenario="static obstacle",originalSamples=reads,originalEnd=member.root.position};
                Reset(member,start,Quaternion.identity);reads=0;Call(herd,"Move",member,start+Vector3.forward*.1f,.1f,null);
                blocked.optimizedSamples=reads;blocked.optimizedEnd=member.root.position;report.samples.Add(blocked);
                Require(blocked.originalEnd==start && blocked.optimizedEnd==start && member.ActualSpeed==0,"Static collision was weakened.");
                UnityEngine.Object.DestroyImmediate(obstacle);Physics.SyncTransforms();
                Reset(member,start,Quaternion.identity);
                object[] evaluation={member,start+Vector3.forward*.1f,.1f,null};
                Require((bool)typeof(JourneyHerd).GetMethod("EvaluateBodyMove",Hidden).Invoke(herd,evaluation),"Clear movement fixture could not be evaluated.");
                other.root.position=start+Vector3.forward*.2f;
                Call(herd,"CommitBodyMove",member,evaluation[3],.1f,null);
                Require(member.root.position==start && member.ActualSpeed==0,"A body entering after evaluation was not rejected at movement commit.");
                report.status="PASS";
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);if(terrainData)UnityEngine.Object.DestroyImmediate(terrainData);EditorSceneManager.CloseScene(fixture,true);
                if(originalScene.IsValid() && originalScene.isLoaded)SceneManager.SetActiveScene(originalScene);
                string path="Docs/LifeGameplay/World/journey-query-checks.json";Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,JsonUtility.ToJson(report,true));
            }
        }
        static void Reset(JourneyMember member,Vector3 start,Quaternion facing)
        {member.root.SetPositionAndRotation(start,facing);member.State=JourneyMemberState.Idle;member.Mode=JourneyTravelMode.Walk;member.DistanceTravelled=member.ActualSpeed=member.ContinuousSwimDistance=0;}
        // Original pre-optimization Move sequence, kept only in this regression to compare its result and real query count.
        static void LegacyMove(JourneyHerd herd,JourneyMember member,Vector3 next,float dt)
        {
            JourneyNavigation nav=herd.navigation;
            Quaternion facing=(Quaternion)Call(herd,"MovementRotation",member,next,dt);
            if(!nav.TryProject(member.profile,next,facing,out Vector3 checkedPoint,out _) ||
                !(nav.CanMoveSupportedBody(member.profile,member.root.position,member.root.rotation,checkedPoint,facing) ||
                  nav.TryPlanBodyStep(member.profile,member.root.position,member.root.rotation,checkedPoint,facing,out _)))
            {member.ActualSpeed=0;return;}
            facing=(Quaternion)Call(herd,"MovementRotation",member,next,dt);
            if(!nav.TryProject(member.profile,next,facing,out next,out _) || !(bool)Call(herd,"ClearOfOthers",member,next,null,null,false) ||
                !(bool)Call(herd,"ClearOfMemberBodies",member,next,facing)){member.ActualSpeed=0;return;}
            Require(nav.CanMoveSupportedBody(member.profile,member.root.position,member.root.rotation,next,facing),"This comparison fixture must use supported ground; step transitions retain their existing dedicated checks.");
            Call(herd,"ApplyMovement",member,next,facing,dt);
        }
        static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Hidden).Invoke(target,args);
        static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    }
}
