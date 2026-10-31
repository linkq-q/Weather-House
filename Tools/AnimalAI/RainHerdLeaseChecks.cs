using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.WildlifeJourney;
using Object=UnityEngine.Object;

public static class RainHerdLeaseChecks
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static object Get(object owner,string name)=>owner.GetType().GetField(name,Flags).GetValue(owner);
    static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Flags).SetValue(owner,value);
    static object Call(object owner,string name,params object[] args)=>owner.GetType().GetMethod(name,Flags).Invoke(owner,args);
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Clock(JourneyHerd herd,float now)=>typeof(JourneyHerd).GetProperty("SimulationTime").GetSetMethod(true).Invoke(herd,new object[]{now});
    sealed class Fixture:IDisposable
    {
        public readonly GameObject root;
        public readonly JourneyHerd herd;
        public readonly JourneyNavigation navigation;
        public readonly JourneyMember member,blocker;
        public readonly Vector3 shelter=Vector3.forward*6,normal=Vector3.right*4;
        public Fixture()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            root=new GameObject("Rain herd lease fixture");
            var floor=new GameObject("Supporting floor").AddComponent<BoxCollider>();floor.transform.SetParent(root.transform);
            floor.transform.position=new Vector3(0,-.05f,0);floor.size=new Vector3(40,.1f,40);
            navigation=root.AddComponent<JourneyNavigation>();
            navigation.nodes=new[]{new JourneyNode{id="home",position=Vector3.zero},new JourneyNode{id="normal",position=normal},
                new JourneyNode{id="shelter",position=shelter}};
            navigation.links=new[]{new JourneyLink{from=0,to=1},new JourneyLink{from=0,to=2}};navigation.Rebuild();
            herd=root.AddComponent<JourneyHerd>();herd.navigation=navigation;herd.autoAdvance=false;
            member=Member(1,Vector3.zero);blocker=Member(2,Vector3.forward*.9f);
            herd.members=new[]{member,blocker};herd.sites=new[]{new JourneyActivitySite{name="old activity",center=Vector3.zero,radius=12}};
            Physics.SyncTransforms();
            herd.Initialize();member.Arrived=false;Set(member,"localTarget",normal);Set(member,"destination",normal);
            var old=new JourneyRoute{navigationVersion=navigation.Version};
            old.segments.Add(new JourneySegment{from=Vector3.zero,to=normal,mode=JourneyTravelMode.Walk,linkIndex=-1});Set(member,"route",old);
            Set(member,"segment",0);Physics.SyncTransforms();
        }
        JourneyMember Member(int id,Vector3 at)
        {
            var body=new GameObject("Member "+id).transform;body.SetParent(root.transform);body.position=at;
            return new JourneyMember{id=id,root=body,bodyLength=.6f,profile=new JourneyProfile{bodyRadius=.3f,walkSpeed=1,canSwim=false}};
        }
        public List<Vector3> Crowd=>(List<Vector3>)Get(member,"crowdPath");
        public void Plan(Vector3 desired)
        {
            Set(member,"crowdRetryAt",0f);Physics.SyncTransforms();
            object[] args={member,desired,JourneyTravelMode.Walk,Vector3.zero,.1f};
            Call(herd,"AvoidOthers",args);
            Require(Crowd!=null && Crowd.Count>=2,"Fixture failed to trigger a real crowd detour");
        }
        public object Lease=>((IDictionary)Get(herd,"lifeLeases"))[member.id];
        public string Diagnose()
        {
            var observed=new List<string>();object physics=Get(navigation,"bodyPhysics");
            Set(physics,"ObstructionObserved",new Action<Collider>(c=>observed.Add(c?c.name:"query capacity")));
            try
            {
                JourneySurface surface=navigation.SampleSurface(member.root.position);
                bool projected=navigation.TryProject(member.profile,member.root.position,out Vector3 foot,out JourneyTravelMode ground);
                bool step=navigation.TryStep(member.profile,member.root.position,shelter,out Vector3 target,out JourneyTravelMode mode);
                bool others=(bool)Call(herd,"ClearOfOthers",member,target,null,(JourneyTravelMode?)mode,true);
                return " position="+member.root.position.ToString("R")+" blocker="+blocker.root.position.ToString("R")+
                    " live="+herd.isActiveAndEnabled+" arrived="+member.Arrived+" mode="+member.Mode+" stepPhase="+Get(member,"bodyStepPhase")+
                    " surface="+surface.valid+"/"+surface.groundY.ToString("R")+" project="+projected+"/"+foot.ToString("R")+
                    " step="+step+"/"+target.ToString("R")+"/"+mode+" clearOthers="+others+" blocks="+string.Join(",",observed);
            }
            finally{Set(physics,"ObstructionObserved",null);}
        }
        public void Dispose(){Object.DestroyImmediate(root);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
    }

    public static string[] Run()
    {
        Require(Application.isBatchMode && !Application.isPlaying && Application.dataPath.Contains("ValidationProject"),
            "Rain herd lease checks require the independent ValidationProject batch editor");
        var results=new List<string>();
        using(var f=new Fixture())
        {
            if(!f.herd.RequestAutonomousTarget(1,f.shelter,false,true))throw new InvalidOperationException("Shelter lease failed"+f.Diagnose());
            f.Plan(Vector3.forward*.3f);
            Require(Vector3.Distance(f.Crowd[f.Crowd.Count-1],f.shelter)<.02f && (int)Get(f.member,"crowdResume")==-1,
                "Shelter crowd detour followed the old group/loiter destination instead of the accepted shelter target");
            Require((Vector3)Get(f.member,"destination")==f.normal && (int)Get(f.member,"segment")==0,
                "Independent detour rewrote the old group route");
            List<Vector3> detour=f.Crowd;
            Require(f.herd.RequestAutonomousTarget(1,f.shelter,false,true) && ReferenceEquals(detour,f.Crowd),
                "Renewing the same shelter target discarded useful detour progress");
            Require(f.herd.RequestAutonomousTarget(1,Vector3.left*4,false,true) && f.Crowd==null,
                "A different shelter target inherited the previous crowd path");
            results.Add("PASS a blocked shelter lease plans around its neighbour toward the shelter, leaves the normal group route untouched, preserves same-goal renewals and clears changed-goal detours");
        }
        using(var f=new Fixture())
        {
            f.blocker.root.position=Vector3.right*.9f;f.Plan(Vector3.right*.3f);
            Require(Vector3.Distance(f.Crowd[f.Crowd.Count-1],f.normal)<.02f && (int)Get(f.member,"crowdResume")==0,
                "Ordinary group movement stopped using its normal destination and resume segment");
            Require(f.herd.RequestAutonomousTarget(1,f.shelter,false,true) && f.Crowd==null,
                "Acquiring a new shelter lease retained a crowd path from ordinary play");
            f.blocker.root.position=Vector3.forward*.9f;f.Plan(Vector3.forward*.3f);
            f.herd.ReleaseAutonomousTarget(1);
            Require(f.Crowd==null && !f.herd.HasLifeTarget(1),"Released shelter detour leaked into ordinary movement");
            results.Add("PASS normal group crowd routing still targets and resumes its original route; acquiring and releasing shelter clears only its stale crowd detour");
        }
        using(var f=new Fixture())
        {
            Require(f.herd.RequestAutonomousTarget(1,f.shelter,false,true),"Keep-alive fixture failed");
            f.Plan(Vector3.forward*.3f);List<Vector3> path=f.Crowd;Vector3 before=f.member.root.position;
            Clock(f.herd,1.5f);Require(f.herd.KeepAutonomousTarget(1),"A valid autonomous lease could not be kept alive");
            Require((Vector3)Get(f.Lease,"target")==f.shelter && (float)Get(f.Lease,"until")>=3.5f && ReferenceEquals(path,f.Crowd),
                "Keep-alive changed its target, path, or simulation-time expiry");
            Require(f.herd.KeepAutonomousTarget(1,true) && (Vector3)Get(f.Lease,"target")==before && f.Crowd==null && f.member.root.position==before,
                "Holding during a replan moved the transform or retained an old crowd detour");
            Require(f.herd.RequestLifeTarget(1,f.normal,2),"Manual replacement failed");float manual=(float)Get(f.Lease,"until");
            Require(!f.herd.KeepAutonomousTarget(1) && (float)Get(f.Lease,"until")==manual,
                "Autonomous keep-alive extended a new manual owner's lease");
            f.herd.ReleaseLifeTarget(1);Require(f.herd.RequestAutonomousTarget(1,f.shelter,false,true),"Expiry fixture failed");
            f.Plan(Vector3.forward*.3f);Clock(f.herd,f.herd.SimulationTime+2.1f);
            Require(!f.herd.KeepAutonomousTarget(1),"An already expired lease was resurrected");
            Set(f.herd,"decisionAt",float.PositiveInfinity);Set(f.member,"nextChoice",float.PositiveInfinity);f.herd.Simulate(.01f);
            Require(f.Crowd==null && !((IDictionary)Get(f.herd,"lifeLeases")).Contains(1),"Expired shelter detour survived into the normal simulation");
            results.Add("PASS cheap keep-alive preserves accepted geometry, can hold the current pose without teleporting, rejects manual/expired ownership and clears expired crowd detours");
        }
        return results.ToArray();
    }
}
