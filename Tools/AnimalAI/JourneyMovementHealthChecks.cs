using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.WildlifeJourney;
using Object=UnityEngine.Object;

public static class JourneyMovementHealthChecks
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static Vector3 Point(float x,float z)=>new Vector3(x,0,z);
    static JourneyCrowdObstacle Circle(float x,float z,float radius)=>new JourneyCrowdObstacle{center=Point(x,z),radius=radius};
    static float Length(List<Vector3> points){float result=0;for(int i=1;i<points.Count;i++)result+=Vector3.Distance(points[i-1],points[i]);return result;}
    static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Flags).SetValue(owner,value);
    static void ClearEdges(List<Vector3> points,float radius,JourneyCrowdObstacle[] circles)
    {
        for(int i=1;i<points.Count;i++)foreach(var circle in circles)
        {
            Vector3 edge=points[i]-points[i-1],offset=circle.center-points[i-1];
            float t=Mathf.Clamp01(Vector3.Dot(offset,edge)/Mathf.Max(.000001f,edge.sqrMagnitude));
            Require(Vector3.Distance(points[i-1]+edge*t,circle.center)>=radius+circle.radius+.12f-.00005f,
                "A reduced-query route cut through a supplied body's clearance");
        }
    }
    public static string[] RunPathChecks()
    {
        var results=new List<string>();
        // The exact old implementation's results are retained in Temp/JourneyMovementHealthChecks/Probe.cs.
        // Query limits assert less physical work; the independent path lengths and circle checks protect route quality.
        var cases=new[]{
            ("single neighbour",Point(-3,0),Point(3,0),.4f,new[]{Circle(0,0,.6f)},6.526253f,13),
            ("two-body arrival gap",Point(8.160f,1.742f),Point(13.780f,-.861f),.55f,
                new[]{Circle(9.039f,2.588f,.55f),Circle(9.075f,.878f,.55f)},7.912896f,23)};
        foreach(var test in cases)
        {
            int queries=0;
            Require(JourneyCrowdPath.TryFindPath(test.Item2,test.Item3,test.Item4,test.Item5,
                (a,b)=>{queries++;return true;},out List<Vector3> route),test.Item1+" lost its route");
            Require(Mathf.Abs(Length(route)-test.Item6)<.0001f && queries<test.Item7,
                test.Item1+" changed shortest route length or failed to reduce physical edge checks");
            Require(route[0]==test.Item2 && route[route.Count-1]==test.Item3,"Crowd route lost an endpoint");
            ClearEdges(route,test.Item4,test.Item5);
            Require(JourneyCrowdPath.TryFindPath(test.Item2,test.Item3,test.Item4,test.Item5,(a,b)=>true,out var repeat) &&
                repeat.Count==route.Count && Mathf.Abs(Length(repeat)-Length(route))<.00001f,"Unchanged input became nondeterministic");
            results.Add("PASS "+test.Item1+": physical edge checks "+test.Item7+" -> "+queries+", same shortest length="+Length(route).ToString("F6")+", no body penetration");
        }
        var gap=cases[1];int blockedQueries=0;
        Require(!JourneyCrowdPath.TryFindPath(gap.Item2,gap.Item3,gap.Item4,gap.Item5,(a,b)=>{
            blockedQueries++;return !(Math.Min(a.x,b.x)<10 && Math.Max(a.x,b.x)>=10);
        },out _) && blockedQueries>0,"A physically disconnected route was accepted");
        Require(!JourneyCrowdPath.TryFindPath(gap.Item2,gap.Item5[0].center,gap.Item4,gap.Item5,(a,b)=>true,out _),
            "An occupied goal was accepted");
        int directQueries=0;
        Require(JourneyCrowdPath.TryFindPath(Vector3.zero,Vector3.forward*3,.4f,Array.Empty<JourneyCrowdObstacle>(),
            (a,b)=>{directQueries++;return true;},out var direct) && direct.Count==2 && directQueries==1,"Open ground lost its single checked direct edge");
        results.Add("PASS disconnected/occupied destinations stay rejected; open ground still uses one physical edge check");
        return results.ToArray();
    }
    public static string[] Run()
    {
        Require(Application.isBatchMode && !Application.isPlaying && Application.dataPath.Contains("ValidationProject"),
            "Movement health fixtures require the independent ValidationProject batch editor");
        var results=new List<string>(RunPathChecks());
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=new GameObject("Journey movement health fixture");
        try
        {
            var floor=new GameObject("Supporting floor").AddComponent<BoxCollider>();floor.transform.SetParent(root.transform);
            floor.transform.position=new Vector3(0,-.05f,0);floor.size=new Vector3(40,.1f,40);Physics.SyncTransforms();
            var navigation=root.AddComponent<JourneyNavigation>();navigation.Rebuild();
            var herd=root.AddComponent<JourneyHerd>();herd.navigation=navigation;herd.autoAdvance=false;
            var body=new GameObject("Member").transform;body.SetParent(root.transform);
            var member=new JourneyMember{id=1,root=body,bodyLength=.6f,profile=new JourneyProfile{bodyRadius=.3f,walkSpeed=1}};
            herd.members=new[]{member};herd.sites=new[]{new JourneyActivitySite{name="Home",center=Vector3.zero,radius=12}};
            herd.Initialize();Physics.SyncTransforms();
            Vector3 target=Vector3.forward*6,corner=Vector3.right*2;
            Require(herd.RequestAutonomousTarget(1,target,false),"Real supported detour fixture could not accept its goal");
            Require(navigation.TryStep(member.profile,body.position,corner,out _,out _) && navigation.TryStep(member.profile,corner,target,out _,out _),
                "Detour fixture contains an unsafe body segment");
            // A valid route may remain after its original temporary neighbour has already moved away.
            Set(member,"crowdPath",new List<Vector3>{body.position,corner,target});Set(member,"crowdStep",1);Set(member,"crowdResume",-1);
            var clock=typeof(JourneyHerd).GetProperty("SimulationTime").GetSetMethod(true);
            var move=typeof(JourneyHerd).GetMethod("LifeMove",Flags);
            int movingFrames=0;
            for(int frame=0;frame<120 && body.position.x<1.5f;frame++)
            {
                clock.Invoke(herd,new object[]{herd.SimulationTime+.05f});member.profile.bodyHeading=body.rotation;
                Require(herd.KeepAutonomousTarget(1),"Accepted lease was lost during the actual detour");
                Vector3 before=body.position;move.Invoke(herd,new object[]{member,.05f});Physics.SyncTransforms();
                if(member.ActualSpeed>.05f)
                {
                    movingFrames++;
                    Require(Vector3.Dot(body.forward,Vector3.right)>.95f,
                        "Life movement turned toward its final goal while actually translating along a different crowd waypoint");
                    Require(body.position.x>before.x && Mathf.Abs(body.position.z)<.05f,"First crowd waypoint was not actually followed");
                }
            }
            Require(body.position.x>=1.5f && movingFrames>=20,"Detour did not make continuous physical progress after turning");
            Require(herd.IsAutonomous(1) && !herd.IsAutonomous(999),"Allocation-free ID lookup changed valid/missing member semantics");
            results.Add("PASS accepted life lease turns toward and physically follows its crowd waypoint, with "+movingFrames+" moving frames; no alternating turn back toward final goal");
        }
        finally{Object.DestroyImmediate(root);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        return results.ToArray();
    }
}
