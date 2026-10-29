using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.WildlifeJourney;
using Object=UnityEngine.Object;

namespace WeatherHouse.Hillside.Editor
{
    public static class AnimalAutonomyChecks
    {
        static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        [MenuItem("Weather House/Animal AI/Check Behaviour Contracts")]
        public static void RunMenu(){foreach(string line in Run())Debug.Log(line);}
        [MenuItem("Weather House/Animal AI/Show Runtime Status")]
        public static void ShowStatus()
        {
            var director=Object.FindObjectOfType<AnimalAutonomy>();var client=Object.FindObjectOfType<AnimalPlannerClient>();
            Debug.Log(director ? "动物="+director.AnimalCount+" 外向="+director.OutgoingCount+" AI="+director.AIStatus+
                " 请求="+(client?client.Requests:0)+" 有效回复="+(client?client.Accepted:0)+" 应用计划="+director.AppliedPlans :
                "请在 WeatherHouse_WatersideV2 场景进入 Play 后查看。配置位于 UserSettings/WeatherHouseAnimalsAI.json。");
        }
        public static void Batch()
        {
            var results=new List<string>();int exit=0;
            try{results.AddRange(Run());}catch(Exception e){results.Add("FAIL "+e);exit=1;}
            string path=Environment.GetEnvironmentVariable("WEATHERHOUSE_ANIMAL_REPORT");
            if(!string.IsNullOrEmpty(path))File.WriteAllLines(path,results);
            foreach(string line in results)Debug.Log(line);
            if(Application.isBatchMode)EditorApplication.Exit(exit);
        }
        public static void BatchGlyph()
        {
            int exit=0;string result;
            try{CheckGlyph();result="PASS rounded white Z rendered, waking hides all three symbols";}
            catch(Exception e){exit=1;result="FAIL "+e;}
            string report=Environment.GetEnvironmentVariable("WEATHERHOUSE_ANIMAL_REPORT");
            if(!string.IsNullOrEmpty(report))File.WriteAllText(report,result);
            if(Application.isBatchMode)EditorApplication.Exit(exit);
        }
        public static IEnumerable<string> Run()
        {
            var observation=new AnimalObservation {revision=3,options=new[]{new AnimalOption{id="sleep"},new AnimalOption{id="resume"}}};
            Require(AnimalPlan.TryRead("{\"revision\":3,\"choices\":[\"sleep\",\"resume\"]}",observation,out _),"Valid short plan rejected");
            foreach(string invalid in new[]{"{}","{\"revision\":2,\"choices\":[\"sleep\"]}","{\"revision\":3,\"choices\":[\"teleport\"]}",
                "{\"revision\":3,\"choices\":[\"sleep\",\"sleep\"]}","{\"revision\":3,\"choices\":[]}","{\"revision\":3,\"choices\":[\"sleep\",\"resume\",\"sleep\",\"resume\"]}"})
                Require(!AnimalPlan.TryRead(invalid,observation,out _),"Invalid plan accepted: "+invalid);
            Require(AnimalPlannerClient.ReadResponse("{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"{}\"}}]}",observation)==null,"Truncated response accepted");
            string payload=AnimalPlannerClient.Payload(observation,"deepseek-flash");
            Require(payload.Contains("\"type\":\"disabled\"") && payload.Contains("json_object") && !payload.Contains("apiKey"),"Request must disable thinking and exclude secrets");
            yield return "PASS plan whitelist, revision, duplicates, empty/truncated responses, non-thinking JSON payload";
            var gate=new AnimalPlanGate();int ticket=gate.Begin(10);Require(gate.Pending,"Request not pending");
            gate.Invalidate();Require(!gate.Complete(ticket,11,true),"Stale reply applied");
            ticket=gate.Begin(20);Require(!gate.Complete(ticket,36,true),"Late reply applied");
            ticket=gate.Begin(40);Require(!gate.Complete(ticket,41,false),"Interaction overwritten");
            ticket=gate.Begin(50);Require(gate.Complete(ticket,51,true) && !gate.Complete(ticket,51,true),"Reply not single-use");
            yield return "PASS cancelled, late, unavailable and duplicate completion gates";
            int outgoing=0;for(int i=0;i<1000;i++){bool value=AnimalPlan.Outgoing(42,i,"deer");Require(value==AnimalPlan.Outgoing(42,i,"deer"),"Unstable personality");if(value)outgoing++;}
            Require(outgoing>150 && outgoing<250,"Outgoing share outside expected seeded range: "+outgoing);
            yield return "PASS stable personality, "+outgoing+" / 1000 outgoing fixture animals";
            CheckPath();yield return "PASS incremental A* wall detour, swept-segment validation and body-width rejection";
            CheckWildlife();yield return "PASS movement continues while reply pending, player lease priority, sleep state and resume";
            CheckRollingHerd();yield return "PASS moving herd plan stays valid, profile changes invalidate, detached member remains independent";
            CheckRainAndSleep();yield return "PASS actual director rain shelter, rain-stop resume, sleep entry and wake release";
            if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)
            {CheckGlyph();yield return "PASS rounded white Z rendered, waking hides all three symbols";}
            yield return WildlifeChecks.RunLogicChecks();
            yield return WeatherHouse.WildlifeJourney.Editor.JourneyNavigationChecks.RunLogicChecks();
            yield return WeatherHouse.WildlifeJourney.Editor.JourneyCrowdPathChecks.RunLogicChecks();
        }
        static void CheckPath()
        {
            bool Clear(Vector3 from,Vector3 to,out Vector3 projected)
            {
                projected=to;int steps=Mathf.CeilToInt(Vector3.Distance(from,to)/.025f);
                for(int i=0;i<=steps;i++){Vector3 p=Vector3.Lerp(from,to,i/(float)Mathf.Max(1,steps));if(p.x>4 && p.x<5 && p.z<3)return false;}
                return true;
            }
            var search=new AnimalLocalRoute(Vector3.zero,new Vector3(8,0,0),.3f,Clear);
            int previous=0;for(int i=0;i<4000 && !search.Finished;i++){search.Advance();Require(search.Queries-previous<=1,"Unbudgeted queries");previous=search.Queries;}
            Require(search.Finished && search.Result!=null,"Detour not found");Vector3 last=Vector3.zero;
            foreach(Vector3 point in search.Result){Require(Clear(last,point,out _),"Path cut wall corner");last=point;}
            Require(Vector3.Distance(last,new Vector3(8,0,0))<.01f,"Goal shifted");
            bool Narrow(Vector3 from,Vector3 to,out Vector3 projected){projected=to;return false;}
            var large=new AnimalLocalRoute(Vector3.zero,Vector3.right*3,.8f,Narrow);
            for(int i=0;i<100 && !large.Finished;i++)large.Advance();Require(large.Finished && large.Result==null,"Body rejection ignored");
        }
        static void CheckWildlife()
        {
            Scene scene=EditorSceneManager.NewPreviewScene();var root=new GameObject("Animal AI fixture");SceneManager.MoveGameObjectToScene(root,scene);
            try
            {
                var population=root.AddComponent<WildlifePopulation>();population.obstacleMask=0;
                population.worldBounds=new Bounds(new Vector3(0,10,0),new Vector3(60,20,60));
                var animal=new GameObject("Fixture animal").AddComponent<WildlifeAgent>();animal.transform.SetParent(root.transform);
                animal.timidity=0;animal.walkSpeed=1;population.agents=new[]{animal};population.Initialize();
                Require(animal.RequestAutonomousTarget(Vector3.forward*3,false),"Autonomous goal rejected");
                var gate=new AnimalPlanGate();gate.Begin(0);Vector3 before=animal.transform.position;
                for(int i=0;i<10;i++)population.Simulate(.1f);
                Require(gate.Pending && Vector3.Distance(before,animal.transform.position)>.1f,"Pending model request froze locomotion");
                Require(animal.RequestLifeTarget(animal.transform.position,2),"Interaction goal rejected");
                Require(!animal.RequestAutonomousTarget(Vector3.forward*3,false),"AI stole player interaction lease");
                animal.ReleaseLifeTarget();
                Require(animal.RequestAutonomousTarget(animal.transform.position,true),"Sleep rejected");
                for(int i=0;i<5;i++)population.Simulate(.1f);
                Require(animal.Sleeping,"Sleep state not entered");animal.ReleaseAutonomousTarget();Require(!animal.Sleeping,"Sleep state survived release");
            }
            finally{Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
        }
        static void CheckRollingHerd()
        {
            Scene scene=EditorSceneManager.NewPreviewScene();var root=new GameObject("Herd rolling fixture");SceneManager.MoveGameObjectToScene(root,scene);
            try
            {
                var nav=root.AddComponent<JourneyNavigation>();nav.surfaceSampler=p=>new JourneySurface{valid=true,groundY=0};nav.Rebuild();
                var herd=root.AddComponent<JourneyHerd>();herd.navigation=nav;herd.autoAdvance=false;
                herd.sites=new[]{new JourneyActivitySite{center=Vector3.zero},new JourneyActivitySite{center=Vector3.forward*5}};
                var body=new GameObject("Member");body.transform.SetParent(root.transform);
                herd.members=new[]{new JourneyMember{id=1,root=body.transform,sociability=0}};herd.Initialize();
                Require(herd.RequestJourney(1),"Plan did not start");
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                object pending=typeof(JourneyHerd).GetField("pending",flags).GetValue(herd);
                MethodInfo unchanged=typeof(JourneyHerd).GetMethod("PlanUnchanged",flags);
                body.transform.position+=Vector3.right*.1f;
                Require((bool)unchanged.Invoke(herd,new[]{pending}),"Ordinary movement invalidated background herd plan");
                herd.members[0].profile.bodyRadius+=.1f;
                Require(!(bool)unchanged.Invoke(herd,new[]{pending}),"Changed body profile failed to invalidate plan");
                herd.CancelPlanning();
                Require(herd.RequestAutonomousTarget(1,body.transform.position+Vector3.right,false),"Herd temporary leave rejected");
                Require(herd.RequestJourney(1),"Detached member blocked group planning");
                for(int i=0;i<10;i++)herd.Simulate(.1f);
                Require(herd.IsAutonomous(1),"Group plan reclaimed detached member");
                herd.ReleaseAutonomousTarget(1);Require(!herd.IsAutonomous(1),"Rejoin failed to release autonomy");
            }
            finally{Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
        }
        static void CheckRainAndSleep()
        {
            Scene scene=EditorSceneManager.NewPreviewScene();var root=new GameObject("Director fixture");SceneManager.MoveGameObjectToScene(root,scene);
            try
            {
                var world=root.AddComponent<WeatherHouse.Life.LifeWorld>();
                var player=new GameObject("Player");player.transform.SetParent(root.transform);player.transform.position=Vector3.right*15;
                var population=root.AddComponent<WildlifePopulation>();population.obstacleMask=0;
                population.worldBounds=new Bounds(new Vector3(0,10,0),new Vector3(60,20,60));
                var animal=new GameObject("Fixture animal").AddComponent<WildlifeAgent>();animal.transform.SetParent(root.transform);animal.timidity=0;
                population.agents=new[]{animal};population.Initialize();
                var shelter=new GameObject("Roof target").AddComponent<WeatherHouse.Life.LifeObject>();shelter.transform.SetParent(root.transform);
                shelter.transform.position=Vector3.forward*2;
                shelter.Initialize(new WeatherHouse.Life.LifeObjectState{id="shelter",sheltered=true},"Shelter");
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var objects=(Dictionary<string,WeatherHouse.Life.LifeObject>)typeof(WeatherHouse.Life.LifeWorld).GetField("objects",flags).GetValue(world);
                objects.Add("shelter",shelter);
                var director=root.AddComponent<AnimalAutonomy>();director.Initialize(world,player.transform);
                typeof(AnimalAutonomy).GetField("client",flags).SetValue(director,root.AddComponent<AnimalPlannerClient>());
                Type type=typeof(AnimalAutonomy).GetNestedType("Animal",BindingFlags.NonPublic);object state=Activator.CreateInstance(type,true);
                void Set(string field,object value)=>type.GetField(field).SetValue(state,value);
                Set("agent",animal);Set("root",animal.transform);Set("species","rabbit");Set("id","fixture");Set("nextAI",float.PositiveInfinity);
                typeof(AnimalAutonomy).GetMethod("Add",flags).Invoke(director,new[]{state});Set("nextAI",float.PositiveInfinity);
                MethodInfo tick=typeof(AnimalAutonomy).GetMethod("Tick",flags);
                director.Rain(Vector3.zero,10,true);
                for(int i=0;i<50;i++){tick.Invoke(director,new[]{state});population.Simulate(.1f);}
                Require(Vector3.Distance(animal.transform.position,shelter.transform.position)<.4f,"Animal did not reach chosen shelter");
                Require(animal.AutonomousTarget,"Shelter lease missing");
                director.Rain(Vector3.zero,10,false);tick.Invoke(director,new[]{state});
                Require(!animal.AutonomousTarget,"Rain stopped but shelter retained control");
                Set("fatigue",.95f);Set("until",0f);
                for(int i=0;i<5;i++){tick.Invoke(director,new[]{state});population.Simulate(.1f);}
                Require(animal.Sleeping,"Director failed to execute sleep");
                animal.RequestLifeTarget(animal.transform.position,2);tick.Invoke(director,new[]{state});
                Require(!animal.Sleeping && !animal.AutonomousTarget,"Player interaction failed to interrupt sleep");
            }
            finally{Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
        }
        static void CheckGlyph()
        {
            Scene scene=EditorSceneManager.NewPreviewScene();var root=new GameObject("Sleep glyph fixture");SceneManager.MoveGameObjectToScene(root,scene);
            RenderTexture target=null;Texture2D image=null;
            try
            {
                var camera=new GameObject("Fixture camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);
                camera.scene=scene;camera.transform.position=new Vector3(0,1.3f,-4);camera.orthographic=true;camera.orthographicSize=.8f;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.16f,.19f);
                var glyph=root.AddComponent<AnimalSleepGlyph>();glyph.Sleeping=true;
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                typeof(AnimalSleepGlyph).GetField("view",flags).SetValue(glyph,camera);
                typeof(AnimalSleepGlyph).GetMethod("LateUpdate",flags).Invoke(glyph,null);
                target=new RenderTexture(384,384,24);camera.targetTexture=target;camera.Render();
                RenderTexture old=RenderTexture.active;RenderTexture.active=target;
                image=new Texture2D(384,384,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,384,384),0,0);image.Apply();RenderTexture.active=old;
                int white=0;foreach(Color32 pixel in image.GetPixels32())if(pixel.r>170 && pixel.g>170 && pixel.b>170)white++;
                Require(white>40,"White Z did not render");
                string report=Environment.GetEnvironmentVariable("WEATHERHOUSE_ANIMAL_REPORT");
                if(!string.IsNullOrEmpty(report))File.WriteAllBytes(Path.ChangeExtension(report,"png"),image.EncodeToPNG());
                glyph.Sleeping=false;typeof(AnimalSleepGlyph).GetMethod("LateUpdate",flags).Invoke(glyph,null);
                foreach(LineRenderer symbol in root.GetComponentsInChildren<LineRenderer>())Require(!symbol.enabled,"Z persisted while awake");
            }
            finally{if(image)Object.DestroyImmediate(image);if(target)Object.DestroyImmediate(target);Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
