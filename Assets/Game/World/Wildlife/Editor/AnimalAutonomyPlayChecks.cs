using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WeatherHouse.Life;

namespace WeatherHouse.Hillside.Editor
{
    [InitializeOnLoad]
    public static class AnimalAutonomyPlayChecks
    {
        const string Pending="WeatherHouse.AnimalAI.PlayCheck";
        static AnimalAutonomy director;
        static WildlifeAgent animal;
        static LifeObject shelter;
        static float began,stageAt;
        static int stage;
        static readonly List<string> results=new List<string>();
        static AnimalAutonomyPlayChecks()
        {
            EditorApplication.playModeStateChanged+=state=>{
                if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending,false))Setup();
            };
            EditorApplication.update+=Tick;
        }
        public static void BatchPlay()
        {
            if(!Application.isBatchMode || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_ANIMAL_REPORT")))
                throw new InvalidOperationException("Play check requires the isolated batch validation project.");
            SessionState.SetBool(Pending,true);EditorApplication.isPlaying=true;
        }
        static void Setup()
        {
            try
            {
                began=stageAt=Time.time;stage=0;results.Clear();
                var root=new GameObject("Isolated animal play fixture");
                var world=root.AddComponent<LifeWorld>();
                var player=new GameObject("Fixture player");player.transform.SetParent(root.transform);player.transform.position=Vector3.right*15;
                var camera=new GameObject("Fixture camera").AddComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(0,2,-6);
                var population=root.AddComponent<WildlifePopulation>();population.obstacleMask=0;population.player=player.transform;
                population.worldBounds=new Bounds(new Vector3(0,10,0),new Vector3(60,20,60));
                animal=new GameObject("Fixture ground animal").AddComponent<WildlifeAgent>();animal.transform.SetParent(root.transform);animal.timidity=0;
                population.agents=new[]{animal};population.Initialize();
                shelter=new GameObject("Fixture canopy target").AddComponent<LifeObject>();shelter.transform.SetParent(root.transform);shelter.transform.position=Vector3.forward*2;
                shelter.Initialize(new LifeObjectState{id="shelter",sheltered=true},"Shelter");
                var objects=(Dictionary<string,LifeObject>)typeof(LifeWorld).GetField("objects",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);objects.Add("shelter",shelter);
                director=root.AddComponent<AnimalAutonomy>();director.Initialize(world,player.transform);
            }
            catch(Exception e){Finish(e.ToString());}
        }
        static void Tick()
        {
            if(!EditorApplication.isPlaying || !director || !SessionState.GetBool(Pending,false))return;
            try
            {
                if(Time.time-began>25){Finish("Play scenario timed out in stage "+stage);return;}
                if(stage==0)
                {
                    director.Rain(Vector3.zero,10,true);
                    if(Time.time-began<6)return;
                    if(director.AnimalCount!=1 || Vector3.Distance(animal.transform.position,shelter.transform.position)>.4f)
                    {Finish("Director Start/Update did not drive animal to shelter: "+animal.transform.position);return;}
                    results.Add("PASS Play: automatic animal registration and budgeted Update reached shelter through real movement");
                    director.Rain(Vector3.zero,10,false);stage=1;stageAt=Time.time;
                }
                else if(stage==1 && Time.time-stageAt>1)
                {
                    if(animal.AutonomousTarget){Finish("Rain-stop lease did not release in Play");return;}
                    results.Add("PASS Play: rain-stop released shelter without a network response");
                    var states=(IList)typeof(AnimalAutonomy).GetField("animals",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(director);
                    object state=states[0];state.GetType().GetField("fatigue").SetValue(state,.95f);state.GetType().GetField("until").SetValue(state,0f);
                    stage=2;stageAt=Time.time;
                }
                else if(stage==2 && Time.time-stageAt>1.5f)
                {
                    if(!animal.Sleeping || animal.GetComponentsInChildren<LineRenderer>().Length!=3){Finish("Sleep or Z lifecycle did not start in Play");return;}
                    results.Add("PASS Play: fatigue selected sleep and created three reused Z symbols");
                    animal.RequestLifeTarget(animal.transform.position,2);stage=3;stageAt=Time.time;
                }
                else if(stage==3 && Time.time-stageAt>.5f)
                {
                    if(animal.Sleeping || animal.AutonomousTarget){Finish("Player interaction failed to wake animal in Play");return;}
                    foreach(LineRenderer line in animal.GetComponentsInChildren<LineRenderer>())if(line.enabled){Finish("Awake animal retained Z");return;}
                    results.Add("PASS Play: player interaction woke the animal and cleared Z");Finish(null);
                }
            }
            catch(Exception e){Finish(e.ToString());}
        }
        static void Finish(string error)
        {
            SessionState.SetBool(Pending,false);if(error!=null)results.Add("FAIL "+error);
            File.WriteAllLines(Environment.GetEnvironmentVariable("WEATHERHOUSE_ANIMAL_REPORT"),results);
            EditorApplication.Exit(error==null?0:1);
        }
    }
}
