using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using ForestSpriteCharacter;
using WeatherHouse.Vegetation;
using WeatherHouse.WatersideV2;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

namespace WeatherHouse.Review
{
    // Owned by RuralMainPlayReview. Inputs use the original movement path; no actor placement or collision overrides.
    public static class RuralMainPlayProbe
    {
        const string RootName="乡村水岸增量 · 20261025";
        [Serializable] public sealed class Asset { public string id; public Vector3 position,scale; public Quaternion rotation; public int lods,colliders; }
        [Serializable] public sealed class Walk
        { public string name,status="pending",basis; public Vector3 target,start,end; public float seconds,travelled; public bool grounded; public List<Detour> detours=new List<Detour>(); }
        [Serializable] public sealed class Detour
        { public string animal,status="walking"; public int pointIndex,attempt; public Vector3 start,animalPosition,originalGoal,end; public Vector3[] points; public float began,seconds,bodyRadius,predictedFraction; }
        [Serializable] public sealed class Shot
        { public string name,path; public Vector3 target,cameraPosition; public Quaternion cameraRotation; public float span,dwellSeconds,yawOffset,pitchDegrees=-1,estimatedRelativeHeight; public string roofId,roofLighting; public int intendedLOD=-1; public bool written; }
        [Serializable] public sealed class Pool { public Vector3 center; public float terrainHeight; public bool declaredWater; }
        [Serializable] public sealed class Report
        {
            public string result="RUNNING",error="",suite,utc;
            public string evidence="Original CharacterController driven by SetScriptedInput through its normal Update/gravity/collision path, not OS keyboard injection. No teleport, collision disabling, direct Move call, model edits or speed/quality changes. Camera inspection shots are real Game captures; framing is temporary. Numeric checks do not assert visual acceptance.";
            public string boatLimit="B01/B02 static berths, colliders disabled by placement; no boarding/door clearance certification.";
            public bool restored,assetTransformsUnchanged;
            public Vector3 playerStart,playerEnd,capsuleCenter; public float capsuleRadius,capsuleHeight,stepOffset,slopeLimit,skinWidth;
            public List<Asset> assets=new List<Asset>();public List<Pool> pools=new List<Pool>();
            public List<Walk> walks=new List<Walk>();public List<Shot> images=new List<Shot>();
        }
        sealed class Leg { public Walk report; public bool direct; }
        static GameplaySession session;static GameplayHud hud;static ForestSpriteDemoController player;static CharacterController capsule;
        static Camera camera;static VegetationTopDownCamera orbit;static Terrain terrain;static Transform root;
        static WatersideWaterBoundary waterBoundary;static WorldWaterSurface water;
        static Report report;static string directory,notebook;static bool running;
        static bool blocked,jumpBlocked,cameraBlocked,external,orbitBlocked,orbitEnabled,cameraEnabled,orthographic;
        static Vector3 cameraPosition;static Quaternion cameraRotation;static float fov,orthoSize;
        static ForestSpriteDemoController.Demonstration demonstration;
        static readonly List<Leg> legs=new List<Leg>();static readonly List<Vector3> nodes=new List<Vector3>();static readonly List<List<int>> edges=new List<List<int>>();
        static readonly List<Vector3> route=new List<Vector3>();static int legIndex,pointIndex,shotIndex,lastFrame,aimedFrame;
        static float began,legBegan,lastProgress,bestDistance,nextTrace,shotBegan;static Vector3 previous;static Shot pending;
        static StreamWriter trace;
        static Detour detour;static int detourPoint,pointAttempts;static Vector3 firstSide;
        public static bool IsComplete { get; private set; }
        public static string Failure=>IsComplete&&report!=null&&report.result!="COMPLETE"?report.error:null;
        static float Flat(Vector3 a,Vector3 b)=>Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
        internal static bool DryFoot(float soleY,float waterY)
        { return !float.IsNaN(soleY)&&!float.IsInfinity(soleY)&&!float.IsNaN(waterY)&&!float.IsInfinity(waterY)&&soleY>=waterY; }
        static Transform Placed(string id)=>root.Cast<Transform>().Single(t=>t.name.StartsWith(id+" · ",StringComparison.Ordinal));
        static Bounds BoundsOf(Transform t){var rs=t.GetComponentsInChildren<Renderer>();if(rs.Length==0)throw new InvalidOperationException("No visible renderers: "+t.name);var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;}
        public static string Begin(string folder,string suite)
        {
            if(running||!EditorApplication.isPlaying||EditorApplication.isPaused)throw new InvalidOperationException("Requires owned unpaused Play.");
            session=Object.FindObjectOfType<GameplaySession>();player=session?session.forestSprite:null;camera=session?session.PlayerViewCamera:null;
            hud=session?session.GetComponent<GameplayHud>():null;orbit=camera?camera.GetComponent<VegetationTopDownCamera>():null;
            capsule=player?player.GetComponent<CharacterController>():null;terrain=Object.FindObjectOfType<Terrain>();
            waterBoundary=player?player.GetComponent<WatersideWaterBoundary>():null;water=waterBoundary?waterBoundary.surface:null;
            root=Object.FindObjectsOfType<Transform>().SingleOrDefault(t=>t.name==RootName);
            if(!session||!player||!camera||!orbit||!capsule||!terrain||!root||!hud||!capsule.enabled||!player.enabled||player.ScriptedInput||!waterBoundary||!waterBoundary.isActiveAndEnabled||!water||waterBoundary.player!=player||waterBoundary.terrain!=terrain)
                throw new InvalidOperationException("Missing applied map/original enabled player/capsule/camera, or another input owner.");
            if(hud.BlocksMovement||hud.CameraMode||hud.IsAlbumOpening||session.Judging||(session.PhotoRig&&session.PhotoRig.Active)||player.InputBlocked)
                throw new InvalidOperationException("Begin with ordinary gameplay, journal/dialogue/photo closed and input available.");
            directory=folder;Directory.CreateDirectory(directory);
            report=new Report{suite=suite,utc=DateTime.UtcNow.ToString("O"),playerStart=player.transform.position,
                capsuleCenter=capsule.center,capsuleRadius=capsule.radius,capsuleHeight=capsule.height,stepOffset=capsule.stepOffset,slopeLimit=capsule.slopeLimit,skinWidth=capsule.skinWidth};
            notebook=JsonUtility.ToJson(session.Notebook);blocked=player.InputBlocked;jumpBlocked=player.JumpBlocked;cameraBlocked=player.CameraInputBlocked;external=player.externalCameraControl;demonstration=player.demonstration;
            orbitBlocked=orbit.InputBlocked;orbitEnabled=orbit.enabled;cameraEnabled=camera.enabled;cameraPosition=camera.transform.position;cameraRotation=camera.transform.rotation;fov=camera.fieldOfView;orthographic=camera.orthographic;orthoSize=camera.orthographicSize;
            legs.Clear();nodes.Clear();edges.Clear();route.Clear();legIndex=pointIndex=shotIndex=0;pending=null;detour=null;pointAttempts=0;lastFrame=-1;began=Time.realtimeSinceStartup;nextTrace=0;previous=player.transform.position;
            trace=new StreamWriter(Path.Combine(directory,"walk-trace.csv"));trace.WriteLine("seconds,leg,x,y,z,targetX,targetZ,grounded,actualSpeed,inputBlocked,soleY,inWater,surfaceY,detourAttempt,detourPoint");
            running=true;IsComplete=false;
            EditorApplication.update+=Tick;RenderPipelineManager.endCameraRendering+=Rendered;
            try
            {
                CheckAssets();if(suite=="views"||suite=="details"||suite=="steps"||suite=="roofs")PrepareShots();PrepareLegs(suite);if(legs.Count>0)BuildRouteGraph();
                if(report.images.Count==0&&legs.Count==0)throw new InvalidOperationException("Review suite has no planned checks: "+suite);
                player.SetScriptedInput(Vector2.zero,false,false);
                File.WriteAllText(Path.Combine(directory,"planned.json"),JsonUtility.ToJson(report,true));
            }
            catch(Exception e){report.error=e.ToString();Finish("FAILED_TO_START");throw;}
            return directory;
        }
        static void CheckAssets()
        {
            string[] ids=Enumerable.Range(1,9).Select(i=>"A"+i.ToString("D2")).Concat(new[]{"B01","B02"}).Concat(Enumerable.Range(1,6).Select(i=>"P"+i.ToString("D2"))).ToArray();
            foreach(string id in ids)
            {
                var t=Placed(id);var groups=t.GetComponentsInChildren<LODGroup>();var cs=t.GetComponentsInChildren<Collider>(true);
                if(!t.gameObject.activeInHierarchy||groups.Length!=1||groups[0].GetLODs().Length!=3||t.GetComponentsInChildren<Renderer>(true).Any(r=>r.sharedMaterials.Any(m=>!m)))throw new InvalidOperationException("Placed asset setup invalid: "+id);
                if(id.StartsWith("B")&&cs.Any(c=>c.enabled))throw new InvalidOperationException("Static berth has active collision: "+id);
                report.assets.Add(new Asset{id=id,position=t.position,rotation=t.rotation,scale=t.localScale,lods=3,colliders=cs.Length});
            }
            if(root.Cast<Transform>().Any(t=>t.name.StartsWith("T01")))throw new InvalidOperationException("Excluded tree present.");
            if(WatersideLayout.TidePools.Length!=6)throw new InvalidOperationException("Expected applied six-pool coastline.");
            foreach(var p in WatersideLayout.TidePools)report.pools.Add(new Pool{center=p.Center,terrainHeight=terrain.SampleHeight(p.Center)+terrain.transform.position.y,declaredWater=WatersideLayout.IsWater(p.Center.x,p.Center.z,.1f)});
            if(report.pools.Any(p=>!p.declaredWater||p.terrainHeight>=WatersideLayout.SeaLevel))throw new InvalidOperationException("A tide pool lacks a declared water depression; inspect terrain before traversal.");
        }
        static void PrepareShots()
        {
            void Add(string name,Vector3 center,float span)=>report.images.Add(new Shot{name=name,path=Path.Combine(directory,name+".png"),target=center,span=span});
            if(report.suite=="roofs")
            {
                // Same saved scene and real Game camera; only temporary framing is changed.
                var reference=Object.FindObjectsOfType<Transform>().Single(t=>t.name=="天气屋 · v05 苔顶木构");
                var wh=BoundsOf(reference);Add("roof_00_WeatherHouse_Game",wh.center,Mathf.Max(wh.size.x,wh.size.y,wh.size.z)*1.7f);
                report.images.Last().roofId="WeatherHouse";
                foreach(string id in Enumerable.Range(1,9).Select(i=>"A"+i.ToString("D2")))
                {
                    var view=BoundsOf(Placed(id));Add("roof_"+id+"_Game",view.center,Mathf.Max(view.size.x,view.size.y,view.size.z)*1.7f);
                    report.images.Last().roofId=id;
                }
                Add("roof_A04_prior_overview_Game",new Vector3(72.5f,1.5f,-58.5f),22);report.images.Last().roofId="A04";
                var a09=Placed("A09");var group=a09.GetComponentInChildren<LODGroup>();var levels=group.GetLODs();
                Vector3 center=group.transform.TransformPoint(group.localReferencePoint),scale=group.transform.lossyScale;
                float size=group.size*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
                for(int i=0;i<3;i++)
                {
                    float height=(levels[i].screenRelativeTransitionHeight+(i==0?1:levels[i-1].screenRelativeTransitionHeight))*.5f;
                    float distance=size*QualitySettings.lodBias/(2*Mathf.Tan(fov*Mathf.Deg2Rad*.5f)*height);
                    Add("roof_A09_natural_LOD"+i+"_Game",center,distance);
                    var shot=report.images.Last();shot.roofId="A09";shot.pitchDegrees=65;shot.intendedLOD=i;
                    shot.estimatedRelativeHeight=size*QualitySettings.lodBias/(2*Mathf.Tan(fov*Mathf.Deg2Rad*.5f)*Mathf.Max(2,distance));
                }
                Add("roof_A09_backface_oblique_Game",center,10);report.images.Last().roofId="A09";report.images.Last().pitchDegrees=25;report.images.Last().yawOffset=180;
                return;
            }
            if(report.suite=="steps")
            {
                var building=Placed("A06");var view=BoundsOf(building);
                Add("A06_stairs_overview_Game",view.center,Mathf.Max(view.size.x,view.size.y,view.size.z)*1.8f);
                Add("A06_stairs_close_Game",building.TransformPoint(new Vector3(0,.15f,2.2f)),7);
                var a03=Placed("A03");float frontYaw=Mathf.DeltaAngle(cameraRotation.eulerAngles.y,a03.eulerAngles.y+180f);
                Add("A03_stairs_front_Game",a03.TransformPoint(new Vector3(0,.25f,2.55f)),7);report.images.Last().yawOffset=frontYaw;
                Add("A03_stairs_oblique_Game",a03.TransformPoint(new Vector3(0,.25f,2.55f)),7);report.images.Last().yawOffset=frontYaw+35f;
                return;
            }
            if(report.suite=="details")
            {
                Add("A04_road_detour_overview_Game",new Vector3(72.5f,1.5f,-58.5f),22);
                Add("A04_road_detour_oblique_Game",new Vector3(72.5f,1.5f,-58.5f),16);report.images.Last().yawOffset=90;
                Add("coast_rock_detour_Game",new Vector3(59f,1.5f,-38f),12);
                Add("mouth_bridge_entry_overview_Game",new Vector3(20.1f,1.2f,-55.8f),12);
                Add("mouth_bridge_entry_close_Game",new Vector3(20.1f,1.2f,-55.8f),6);report.images.Last().yawOffset=90;
                Add("east_tree_detour_overview_Game",new Vector3(38.7f,6.3f,8.1f),18);
                Add("east_tree_detour_oblique_Game",new Vector3(39.2f,6.3f,6.5f),13);report.images.Last().yawOffset=90;
                foreach(string id in new[]{"A08","P01","B02"})
                { var view=BoundsOf(Placed(id));Add("detail_"+id+"_Game",view.center,Mathf.Max(view.size.x,view.size.y,view.size.z)*2f);report.images.Last().yawOffset=180; }
                return;
            }
            var a=BoundsOf(Placed("A03"));Add("01_A03_Game",a.center,Mathf.Max(a.size.x,a.size.y,a.size.z)*1.8f);
            if(report.suite=="views") foreach(string id in new[]{"A01","A02","A05","A06","A07","A08","A09","P01","P02","P03","P04","P05","P06"}) { var view=BoundsOf(Placed(id));Add("asset_"+id+"_Game",view.center,Mathf.Max(view.size.x,view.size.y,view.size.z)*2f); }
            var h=BoundsOf(Placed("A04"));h.Encapsulate(BoundsOf(Placed("B01")));h.Encapsulate(BoundsOf(Placed("B02")));Add("02_rural_harbor_Game",h.center,Mathf.Max(h.size.x,h.size.z)*1.7f);
            var pools=WatersideLayout.TidePools;var b=new Bounds(pools[0].Center,Vector3.zero);foreach(var p in pools)b.Encapsulate(p.Center);
            Add("03_six_pool_coast_Game",b.center,Mathf.Max(b.size.x,b.size.z)*1.9f);
            for(int i=0;i<pools.Length;i++)Add("04_pool_"+(i+1)+"_Game",pools[i].Center,Mathf.Max(pools[i].Radii.x,pools[i].Radii.y)*5);
        }
        static void AddLeg(string name,Vector3 target,string basis,bool direct=false)
        {var w=new Walk{name=name,target=target,basis=basis};report.walks.Add(w);legs.Add(new Leg{report=w,direct=direct});}
        static void Entrance(string id,float x,float outsideZ,float insideZ,float floor)
        {
            var t=Placed(id);string basis=id=="A03"?"Local +Z front confirmed by entry_stone_step at z=2.09..2.76; x=.85 avoids center post x=0,z=1.55. Open porch shown in imported preview.":"Local +Z open stable bay; x=0 between posts +/-1.28,z=1.85; closed tackroom at x>2.5 excluded. Confirmed by manifest and preview.";
            var outside=t.TransformPoint(new Vector3(x,0,outsideZ));var inside=t.TransformPoint(new Vector3(x,floor,insideZ));
            AddLeg(id+"_approach",outside,basis);AddLeg(id+"_enter",inside,basis,true);AddLeg(id+"_exit",outside,basis,true);
        }
        static void PrepareLegs(string suite)
        {
            if(suite=="views")return;
            if(suite=="all"||suite=="entrances"){Entrance("A06",0,3.3f,.7f,.33f);Entrance("A03",.85f,3.65f,.75f,.55f);}
            if(suite=="all"||suite=="coast")
            {
                AddLeg("east_route_start",RuralMapConfiguration.NewCorridors[7][0],"Applied eastern route start; joined through existing authored path network.");
                foreach(var p in RuralMapConfiguration.NewCorridors[7].Skip(1))AddLeg("east_route_"+legs.Count,p,"Consecutive applied NewCorridors[7] waypoint.",true);
                AddLeg("coast_spine_start",RuralMapConfiguration.NewCorridors[5][0],"Connect to new dry coastal spine via authored paths.");
                foreach(var p in RuralMapConfiguration.NewCorridors[5].Skip(1))AddLeg("coast_spine_"+legs.Count,p,"Consecutive applied NewCorridors[5] waypoint; no boat boarding.",true);
            }
        }
        static int Node(Vector3 p){int i=nodes.FindIndex(q=>Flat(q,p)<.15f);if(i>=0)return i;nodes.Add(p);edges.Add(new List<int>());return nodes.Count-1;}
        static void Link(int a,int b){if(a!=b&&!edges[a].Contains(b)){edges[a].Add(b);edges[b].Add(a);}}
        static bool DryLink(Vector3 a,Vector3 b)
        {
            int n=Mathf.Max(1,Mathf.CeilToInt(Flat(a,b)/.5f));
            for(int i=0;i<=n;i++){var p=Vector3.Lerp(a,b,i/(float)n);if(WatersideLayout.IsWater(p.x,p.z,.1f)&&WatersideLayout.CrossingAt(p.x,p.z,.4f)==null)return false;}
            return true;
        }
        static void BuildRouteGraph()
        {
            // Follow authored paths, not a new gameplay navigator. Short joins only; actual collision remains authoritative.
            foreach(var line in WatersideLayout.Routes.Concat(RuralMapConfiguration.NewCorridors))
            {
                int prev=Node(line[0]);
                for(int i=1;i<line.Length;i++){int n=Mathf.Max(1,Mathf.CeilToInt(Flat(line[i-1],line[i])/1.5f));for(int k=1;k<=n;k++){int next=Node(Vector3.Lerp(line[i-1],line[i],k/(float)n));Link(prev,next);prev=next;}}
            }
            for(int i=0;i<nodes.Count;i++)for(int j=i+1;j<nodes.Count;j++)if(Flat(nodes[i],nodes[j])<2&&DryLink(nodes[i],nodes[j]))Link(i,j);
        }
        static List<Vector3> FindRoute(Vector3 from,Vector3 to)
        {
            int start=Node(from),end=Node(to);
            foreach(int index in new[]{start,end})foreach(int near in Enumerable.Range(0,nodes.Count).Where(i=>i!=index&&Flat(nodes[i],nodes[index])<8).OrderBy(i=>Flat(nodes[i],nodes[index])).Take(12))
                if(DryLink(nodes[index],nodes[near]))Link(index,near);
            var distance=Enumerable.Repeat(float.PositiveInfinity,nodes.Count).ToArray();var parent=Enumerable.Repeat(-1,nodes.Count).ToArray();var used=new bool[nodes.Count];distance[start]=0;
            for(int pass=0;pass<nodes.Count;pass++)
            {
                int u=-1;for(int i=0;i<nodes.Count;i++)if(!used[i]&&(u<0||distance[i]<distance[u]))u=i;
                if(u<0||float.IsPositiveInfinity(distance[u]))break;if(u==end)break;used[u]=true;
                foreach(int v in edges[u]){float d=distance[u]+Flat(nodes[u],nodes[v]);if(d<distance[v]){distance[v]=d;parent[v]=u;}}
            }
            if(float.IsPositiveInfinity(distance[end]))throw new InvalidOperationException("No authored-path connection without water shortcut: "+from+" -> "+to);
            var result=new List<Vector3>();for(int at=end;at!=start;at=parent[at]){if(at<0)throw new InvalidOperationException("Broken route chain.");result.Add(nodes[at]);}result.Reverse();return result;
        }
        internal static Vector3[] SidePoints(Vector3 from,Vector3 goal,Vector3 animal,float clearance,Vector3 side)
        {
            Vector3 forward=goal-from;forward.y=0;forward.Normalize();
            float along=Mathf.Min(Flat(from,goal),Mathf.Max(0,Vector3.Dot(animal-from,forward))+clearance);
            return new[]{from+side*clearance,from+forward*along+side*clearance};
        }
        static bool TryAnimalDetour(Walk walk,Vector3 from,Vector3 goal,float now)
        {
            // Only the review driver steers differently. The production contact function is queried without calling its mutating constraint.
            if(legs[legIndex].direct||pointAttempts>=2||walk.detours.Count>=4)return false;
            var contact=player.GetComponent<WeatherHouse.Hillside.WildlifePlayerCollision>();
            if(!contact||!contact.isActiveAndEnabled)return false;
            var fraction=typeof(WeatherHouse.Hillside.WildlifePlayerCollision).GetMethod("ContactFraction",BindingFlags.Static|BindingFlags.NonPublic);
            Vector3 center=capsule.transform.TransformPoint(capsule.center),scale=capsule.transform.lossyScale;
            float playerRadius=capsule.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z));
            float half=Mathf.Max(playerRadius,capsule.height*Mathf.Abs(scale.y)*.5f);
            Vector3 forward=goal-from;forward.y=0;forward.Normalize();
            Transform blocker=null;float bodyRadius=0,blockedFraction=1,nearest=float.PositiveInfinity;
            void Consider(Transform body,float radius,WeatherHouse.Hillside.WildlifeBodyEnvelope envelope)
            {
                if(!body||!body.gameObject.activeInHierarchy)return;
                bool measured=envelope.Valid&&envelope.measured;float actual=measured?envelope.HorizontalRadius:radius;
                float distance=Flat(center,body.position);if(distance>actual+playerRadius+.4f||distance>=nearest)return;
                object[] args={center,forward*.2f,center.y-half,center.y+half,body.position,
                    body.position.y+(measured?envelope.Bottom:0),body.position.y+(measured?envelope.Top:radius*2),actual+playerRadius+.12f,false,false};
                float allowed=(float)fraction.Invoke(null,args);if(allowed>=.99999f||!(bool)args[9])return;
                blocker=body;bodyRadius=actual;blockedFraction=allowed;nearest=distance;
            }
            foreach(var animal in Object.FindObjectsOfType<WeatherHouse.Hillside.WildlifeAgent>())
                if(animal.isActiveAndEnabled)Consider(animal.transform,animal.Radius,animal.BodyEnvelope);
            foreach(var herd in Object.FindObjectsOfType<WeatherHouse.WildlifeJourney.JourneyHerd>())
                if(herd.isActiveAndEnabled)foreach(var member in herd.members)
                    if(member!=null)Consider(member.root,member.profile.bodyRadius,member.profile.bodyEnvelope);
            if(!blocker)return false;
            if(pointAttempts==0){firstSide=Vector3.Cross(Vector3.up,forward);if(Vector3.Dot(firstSide,from-blocker.position)<0)firstSide=-firstSide;}
            while(pointAttempts<2&&walk.detours.Count<4)
            {
                Vector3 side=pointAttempts==0?firstSide:-firstSide;
                var attempt=new Detour{animal=blocker.name+"#"+blocker.GetInstanceID(),pointIndex=pointIndex,attempt=++pointAttempts,
                    start=from,animalPosition=blocker.position,originalGoal=goal,began=now-began,bodyRadius=bodyRadius,predictedFraction=blockedFraction,
                    points=SidePoints(from,goal,blocker.position,bodyRadius+playerRadius+.12f+.35f,side)};
                walk.detours.Add(attempt);
                if(!DryLink(from,attempt.points[0])||!DryLink(attempt.points[0],attempt.points[1])||!DryLink(attempt.points[1],goal))
                {attempt.status="rejected_declared_water";attempt.end=from;continue;}
                detour=attempt;detourPoint=0;bestDistance=float.PositiveInfinity;lastProgress=now;
                File.WriteAllText(Path.Combine(directory,"progress.json"),JsonUtility.ToJson(report,true));return true;
            }
            return false;
        }
        static void EndDetour(string status,float now)
        {if(detour!=null){detour.status=status;detour.end=player.transform.position;detour.seconds=now-began-detour.began;detour=null;}}
        static void Aim(Shot s)
        {
            player.SetScriptedInput(Vector2.zero,false,false);orbit.enabled=false;player.externalCameraControl=true;
            camera.transform.rotation=s.pitchDegrees>=0?Quaternion.Euler(s.pitchDegrees,cameraRotation.eulerAngles.y+s.yawOffset,0):Quaternion.AngleAxis(s.yawOffset,Vector3.up)*cameraRotation;camera.transform.position=s.target-camera.transform.forward*Mathf.Max(report.suite=="roofs"?2:10,s.span);
            camera.orthographic=false;camera.fieldOfView=fov;camera.enabled=true;s.cameraPosition=camera.transform.position;s.cameraRotation=camera.transform.rotation;
            pending=s;aimedFrame=Time.frameCount;shotBegan=Time.realtimeSinceStartup;
        }
        [Serializable] sealed class RoofLighting
        {
            public int sun;public Color color,sky,equator,ground,fogColor;public Quaternion rotation;
            public float intensity,shadow,ambientIntensity,fogStart,fogEnd,fogDensity;public bool enabled,fog;public int ambientMode,fogMode;
        }
        static void RecordRoofLighting(Shot shot)
        {
            var sun=RenderSettings.sun;if(!sun)throw new InvalidOperationException("Roof review requires the actual scene sun.");
            var state=new RoofLighting{sun=sun.GetInstanceID(),color=sun.color,rotation=sun.transform.rotation,intensity=sun.intensity,shadow=sun.shadowStrength,enabled=sun.enabled,
                sky=RenderSettings.ambientSkyColor,equator=RenderSettings.ambientEquatorColor,ground=RenderSettings.ambientGroundColor,ambientIntensity=RenderSettings.ambientIntensity,ambientMode=(int)RenderSettings.ambientMode,
                fog=RenderSettings.fog,fogMode=(int)RenderSettings.fogMode,fogColor=RenderSettings.fogColor,fogStart=RenderSettings.fogStartDistance,fogEnd=RenderSettings.fogEndDistance,fogDensity=RenderSettings.fogDensity};
            shot.roofLighting=JsonUtility.ToJson(state);
            if(report.images[0].roofLighting!=shot.roofLighting)throw new InvalidOperationException("Roof comparison lighting changed during the capture sequence.");
        }
        static void Rendered(ScriptableRenderContext context,Camera c)
        {
            if(!running||c!=camera||pending==null||Time.frameCount<aimedFrame+3||Time.realtimeSinceStartup-shotBegan<5f||pending.written)return;
            try{pending.dwellSeconds=Time.realtimeSinceStartup-shotBegan;if(report.suite=="roofs")RecordRoofLighting(pending);ScreenCapture.CaptureScreenshot(pending.path);pending.written=true;}catch(Exception e){report.error=e.ToString();Finish("FAILED");}
        }
        static void Tick()
        {
            if(!running||!EditorApplication.isPlaying||EditorApplication.isPaused||lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
            try
            {
                float now=Time.realtimeSinceStartup;
                if(now-began>1100)throw new TimeoutException("Traversal watchdog; unvisited legs remain pending.");
                if(shotIndex<report.images.Count)
                {
                    if(pending==null)Aim(report.images[shotIndex]);
                    else if(pending.written&&File.Exists(pending.path)&&new FileInfo(pending.path).Length>1024)
                    {pending=null;shotIndex++;if(shotIndex==report.images.Count){camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);camera.orthographic=orthographic;camera.orthographicSize=orthoSize;camera.fieldOfView=fov;player.externalCameraControl=external;orbit.enabled=orbitEnabled;previous=player.transform.position;}}
                    else if(now-shotBegan>15)throw new TimeoutException("Game capture not written: "+pending.name);
                    return;
                }
                if(legIndex>=legs.Count){Finish("COMPLETE");return;}
                if(!capsule.enabled||!player.enabled||!player.ScriptedInput||hud.BlocksMovement||hud.CameraMode||(session.PhotoRig&&session.PhotoRig.Active)||!waterBoundary.isActiveAndEnabled||waterBoundary.surface!=water)throw new InvalidOperationException("Original movement/input/water ownership changed during traversal.");
                var leg=legs[legIndex];var w=leg.report;
                if(w.status=="pending")
                {
                    w.status="walking";w.start=player.transform.position;legBegan=lastProgress=now;pointIndex=0;pointAttempts=0;detour=null;route.Clear();
                    route.AddRange(leg.direct?new List<Vector3>{w.target}:FindRoute(w.start,w.target));if(route.Count==0)route.Add(w.target);bestDistance=float.PositiveInfinity;
                }
                Vector3 position=player.transform.position;float moved=Vector3.Distance(position,previous);
                if(moved>Mathf.Max(2,player.runSpeed*Time.unscaledDeltaTime*3))throw new InvalidOperationException("Unexpected displacement/reset; cannot certify input traversal.");
                w.travelled+=moved;previous=position;
                if(position.y<-.8f)throw new InvalidOperationException("Player fell below intended dry route.");
                // Certify the actual capsule sole against the same surface used by ordinary movement.
                // A bridge/porch is dry when its real sole is above that local waterline; a riverbed is not.
                Vector3 foot=capsule.bounds.center;foot.y=capsule.bounds.min.y;
                bool inWater=water.Contains(foot);float surfaceY=inWater?water.Height(foot):float.NaN;
                if(inWater&&!DryFoot(foot.y,surfaceY))throw new InvalidOperationException("Dry-route traversal entered water at "+foot+" local surface="+surfaceY+" leg="+w.name);
                if(now-legBegan>240)throw new TimeoutException("Leg timed out: "+w.name);
                Vector3 goal=detour!=null?detour.points[detourPoint]:route[pointIndex];float distance=Flat(position,goal);
                if(distance<.25f&&player.IsGrounded)
                {
                    if(detour!=null)
                    {
                        player.SetScriptedInput(Vector2.zero,false,false);detourPoint++;bestDistance=float.PositiveInfinity;lastProgress=now;
                        if(detourPoint==detour.points.Length)EndDetour("sidepoints_reached_waiting_original_goal",now);
                        return;
                    }
                    foreach(var attempt in w.detours.Where(d=>d.pointIndex==pointIndex&&d.status=="sidepoints_reached_waiting_original_goal"))attempt.status="rejoined_original_route";
                    pointAttempts=0;
                    player.SetScriptedInput(Vector2.zero,false,false);pointIndex++;bestDistance=float.PositiveInfinity;lastProgress=now;
                    if(pointIndex==route.Count)
                    {
                        if(w.name.EndsWith("_enter")&&Mathf.Abs(capsule.bounds.min.y-w.target.y)>.22f)throw new InvalidOperationException("Reached entry XZ without standing on intended interior floor: "+w.name);
                        w.end=position;w.grounded=true;w.seconds=now-legBegan;w.status="passed_original_capsule";
                        if(w.name.EndsWith("_enter")||w.name.EndsWith("_exit"))
                        {
                            string file=Path.Combine(directory,w.name+"_walking_Game.png");ScreenCapture.CaptureScreenshot(file);
                        }
                        legIndex++;File.WriteAllText(Path.Combine(directory,"progress.json"),JsonUtility.ToJson(report,true));
                    }
                    return;
                }
                if(distance<bestDistance-.06f){bestDistance=distance;lastProgress=now;}
                if(detour!=null&&(now-lastProgress>7||now-began-detour.began>12))
                {
                    EndDetour("failed_local_progress",now);
                    if(TryAnimalDetour(w,position,route[pointIndex],now))return;
                    throw new InvalidOperationException("Bounded animal detour failed at "+w.name+" near "+position);
                }
                bool waitingForAnimal=now-lastProgress>7&&(Object.FindObjectsOfType<WeatherHouse.Hillside.WildlifeAgent>().Any(a=>a.gameObject.activeInHierarchy&&Flat(a.transform.position,position)<Mathf.Max(a.Radius,a.BodyEnvelope.HorizontalRadius)+.6f)||Object.FindObjectsOfType<WeatherHouse.WildlifeJourney.JourneyHerd>().Any(h=>h.isActiveAndEnabled&&h.members.Any(m=>m!=null&&m.root&&m.root.gameObject.activeInHierarchy&&Flat(m.root.position,position)<Mathf.Max(m.profile.bodyRadius,m.profile.bodyEnvelope.HorizontalRadius)+.6f)));
                if(detour==null&&now-lastProgress>(waitingForAnimal?30:7))
                {
                    if(waitingForAnimal&&TryAnimalDetour(w,position,goal,now))return;
                    throw new InvalidOperationException("Blocked or no progress at "+w.name+" near "+position+" toward "+goal+". Capsule/scene retained; nearby animal="+waitingForAnimal);
                }
                Vector3 d=goal-position;d.y=0;d.Normalize();var moveCamera=player.MovementCameraOverride?player.MovementCameraOverride:player.demoCamera;
                Vector3 f=Vector3.ProjectOnPlane(moveCamera.transform.forward,Vector3.up).normalized;Vector3 right=Vector3.Cross(Vector3.up,f);
                player.SetScriptedInput(new Vector2(Vector3.Dot(d,right),Vector3.Dot(d,f)),false,false);
                if(now>=nextTrace){nextTrace=now+.25f;trace.WriteLine(FormattableString.Invariant($"{now-began:F3},{w.name},{position.x:F3},{position.y:F3},{position.z:F3},{goal.x:F3},{goal.z:F3},{player.IsGrounded},{player.ActualVelocity.magnitude:F3},{player.InputBlocked},{foot.y:F4},{inWater},{surfaceY:F4},{(detour!=null?detour.attempt:0)},{(detour!=null?detourPoint:-1)}"));trace.Flush();}
            }
            catch(Exception e){report.error=e.ToString();try{RuralTraversalFailureDump.Save(directory,player,capsule,waterBoundary,camera,legIndex<legs.Count?legs[legIndex].report.name:"none",pointIndex,route.Count,detour!=null?detour.points[detourPoint]:(pointIndex<route.Count?route[pointIndex]:player.transform.position),e.ToString());}catch(Exception dumpError){report.error+="\nFailure diagnostics: "+dumpError;}if(legIndex<legs.Count){legs[legIndex].report.status="failed";legs[legIndex].report.end=player.transform.position;}Finish("FAILED");}
        }
        public static string Finish(string status)
        {
            if(!running)return directory;EndDetour("interrupted_"+status,Time.realtimeSinceStartup);running=false;EditorApplication.update-=Tick;RenderPipelineManager.endCameraRendering-=Rendered;
            try
            {
                report.playerEnd=player.transform.position;
                if(hud.JournalOpen)hud.ToggleJournal();if(hud.IsDialogueOpen)hud.CloseDialogue();if(hud.CameraMode)hud.SetCameraMode(false,true);
                if(session.PhotoRig&&session.PhotoRig.Active)session.PhotoRig.SetActive(false);
                JsonUtility.FromJsonOverwrite(notebook,session.Notebook);
                player.SetScriptedInput(Vector2.zero,false,false);player.ReleaseScriptedInput();player.SetDemonstration(demonstration);
                player.InputBlocked=blocked;player.JumpBlocked=jumpBlocked;player.CameraInputBlocked=cameraBlocked;player.externalCameraControl=external;
                camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);camera.fieldOfView=fov;camera.orthographic=orthographic;camera.orthographicSize=orthoSize;camera.enabled=cameraEnabled;
                orbit.enabled=orbitEnabled;orbit.InputBlocked=orbitBlocked;
                report.assetTransformsUnchanged=report.assets.All(a=>{var t=Placed(a.id);return t.position.Equals(a.position)&&t.rotation.Equals(a.rotation)&&t.localScale.Equals(a.scale);});
                report.restored=!player.ScriptedInput&&player.InputBlocked==blocked&&player.JumpBlocked==jumpBlocked&&player.CameraInputBlocked==cameraBlocked&&player.externalCameraControl==external
                    &&orbit.enabled==orbitEnabled&&orbit.InputBlocked==orbitBlocked&&camera.enabled==cameraEnabled&&camera.transform.position.Equals(cameraPosition)&&camera.transform.rotation.Equals(cameraRotation)
                    &&camera.fieldOfView==fov&&camera.orthographic==orthographic&&camera.orthographicSize==orthoSize&&JsonUtility.ToJson(session.Notebook)==notebook&&!hud.JournalOpen&&!hud.IsDialogueOpen&&!hud.CameraMode
                    &&capsule.enabled&&capsule.center.Equals(report.capsuleCenter)&&capsule.radius==report.capsuleRadius&&capsule.height==report.capsuleHeight&&capsule.stepOffset==report.stepOffset&&capsule.slopeLimit==report.slopeLimit&&capsule.skinWidth==report.skinWidth;
            }
            catch(Exception e){report.error+="\nRestore: "+e;report.restored=false;}
            finally{trace?.Dispose();trace=null;}
            report.result=status=="COMPLETE"&&report.restored&&report.assetTransformsUnchanged&&report.walks.All(w=>w.status=="passed_original_capsule")&&report.images.All(s=>s.written&&File.Exists(s.path))&&string.IsNullOrEmpty(report.error)?"COMPLETE":"FAIL";
            if(report.result!="COMPLETE"&&string.IsNullOrEmpty(report.error))report.error="Interrupted/incomplete: "+status;
            IsComplete=true;File.WriteAllText(Path.Combine(directory,"sample.json"),JsonUtility.ToJson(report,true));return directory;
        }
    }
}
