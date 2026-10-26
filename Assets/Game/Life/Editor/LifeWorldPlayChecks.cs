using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using WeatherHousePrototype;
using WeatherHouse.Hillside;

namespace WeatherHouse.Life.Editor
{
    // Called only by the parent's isolated-save Play review. Never opens/saves a scene or starts Play.
    public static class LifeWorldPlayChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        const string ReportPath="LifeGameplayReview/20261026/play/world-interactions.json";
        [Serializable] sealed class Report
        {
            public string status,utc,boundary,error;
            public string[] checks,diagnostics;
        }
        public static string[] Run(LifeSession life)
        {
            string reviewSave=Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE");
            if(!EditorApplication.isPlaying || !life || !life.World || string.IsNullOrEmpty(reviewSave) ||
                !string.Equals(Path.GetFullPath(reviewSave),Path.GetFullPath(GameplaySession.PersistenceDirectory),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("World Play checks require LifePlayReview's isolated save and a running formal scene.");
            LifeWorld world=life.World;
            Transform player=life.Gameplay.PlayerTransform;
            Vector3 playerPosition=player.position;
            Quaternion playerRotation=player.rotation;
            string worldBefore=world.CaptureJson(),progressBefore=JsonUtility.ToJson(life.Progress),statusBefore=life.Gameplay.Status;
            var originalIds=new HashSet<string>(world.Objects.Select(o=>o.State.id));
            var originalColliders=UnityEngine.Object.FindObjectsOfType<AnimalActor>().SelectMany(a=>a.GetComponentsInChildren<Collider>(true))
                .Distinct().ToDictionary(c=>c,c=>c.enabled);
            byte[] saveBefore=File.Exists(life.SavePath)?File.ReadAllBytes(life.SavePath):null;
            var passed=new List<string>();
            var diagnostics=new List<string>();
            string failure=null;
            Action<bool,string> check=(ok,label)=>{if(!ok)throw new InvalidOperationException(label);passed.Add(label);};
            try
            {
                LifeWorldRouteAudit.Run();
                check(world.GetComponentsInChildren<LifeLabel>(true).All(l=>l.gameObject.layer==LayerMask.NameToLayer("UI")),"All runtime location labels are excluded from motion photos by the UI layer");
                check(world.GetComponentsInChildren<LineRenderer>(true).All(l=>l.gameObject.layer==LayerMask.NameToLayer("UI")),"Runtime location rings use the UI layer as well");
                check(Get<LifeObject>(world,"held")==null,"Review starts with empty hands");
                LifeGatheringPlayChecks.Run(life,check);
                foreach(string resource in new[]{"GrassPatch","GrassBundle","GrassMat","WaterBowl","RibbonFlag","ShadeShelter"})
                    check(Resources.Load<GameObject>("LifeAssets/"+resource),"Imported resource exists: "+resource);
                foreach(LifeObject obj in world.Objects.Where(o=>o.State.kind==LifeObjectKind.GrassSource || o.State.kind==LifeObjectKind.Loom ||
                    o.State.kind==LifeObjectKind.Mat || o.State.kind==LifeObjectKind.Bowl || o.State.kind==LifeObjectKind.Ribbon))
                    check(obj.GetComponentsInChildren<MeshFilter>(true).Any(m=>m.sharedMesh && m.sharedMesh.vertexCount>8),"Runtime object has model geometry: "+obj.State.id);
                foreach(LifeObject bowl in world.Objects.Where(o=>o.State.kind==LifeObjectKind.Bowl))
                    check(bowl.waterVisual,"Runtime bowl binds imported WaterSurface: "+bowl.State.id);
                foreach(LifeObject ribbon in world.Objects.Where(o=>o.State.kind==LifeObjectKind.Ribbon))
                    check(ribbon.ribbon,"Runtime flag binds imported Ribbon: "+ribbon.State.id);
                foreach(string key in new[]{"harborSheep","harborDeer"})
                {
                    LifeSpeaker speaker=world.FindSpeaker(key);
                    check(speaker && speaker.Actor && speaker.Actor.IsAvailableForObservation,"New coastal key is bound and available: "+key);
                    check(speaker.Actor.animalId==(key=="harborSheep"?"life.harbor.sheep":"life.harbor.deer"),"Coastal identity is independent and stable: "+key);
                    check(speaker.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.sharedMesh),"Coastal animal reuses a real skinned model: "+key);
                    WildlifeAgent coastalAnimal=speaker.GetComponent<WildlifeAgent>();
                    check(coastalAnimal && coastalAnimal.RequestLifeTarget(coastalAnimal.transform.position,1),"Coastal animal uses the normal movement controller: "+key);
                    bool wasEnabled=coastalAnimal.enabled;
                    try
                    {
                        coastalAnimal.enabled=false;
                        check(!coastalAnimal.HasLifeTarget,"Play OnDisable releases coastal lease: "+key);
                        coastalAnimal.enabled=true;
                        check(!coastalAnimal.HasLifeTarget,"Play re-enable does not resurrect a released lease: "+key);
                    }
                    finally{coastalAnimal.enabled=wasEnabled;}
                }

                LifeObject dogBowl=Find(world,"dog.bowl"),farBowl=Find(world,"fox.bowl");
                player.position=dogBowl.transform.position;
                dogBowl.State.water=0;
                float farWater=farBowl.State.water,farWet=farBowl.State.wetness;
                check(LifeWorld.Flat(player.position,farBowl.transform.position)>LifeSession.CastRadius,"Out-of-range bowl fixture is outside the player's circle");
                Weather(world,player,LocalWeather.Rain,1,true,6);
                check(dogBowl.State.water>.3f && dogBowl.State.rainStrength==1,"Actual local ApplyWeather fills runtime bowl");
                float filled=dogBowl.State.water;
                Weather(world,player,LocalWeather.Rain,1,false,1);
                check(dogBowl.State.rainStrength==0 && Mathf.Approximately(filled,dogBowl.State.water),"Stopping rain leaves runtime water in the bowl");
                check(Mathf.Approximately(farWater,farBowl.State.water) && Mathf.Approximately(farWet,farBowl.State.wetness),"Out-of-range physical water and wetness remain unchanged");
                dogBowl.RefreshVisual(Time.time);
                check(dogBowl.waterVisual.gameObject.activeSelf && dogBowl.waterVisual.localPosition.y>.06f,"Runtime water level is visibly bound to retained quantity");
                Vector3 localCenter=player.position;
                world.ApplyWeather(farBowl.transform.position,LifeSession.CastRadius,LocalWeather.Rain,2,true,.2f);
                check(Mathf.Approximately(farWater,farBowl.State.water),"Remote weather center cannot affect the far bowl");

                LifeRequest rain=world.Requests.First(r=>r.id=="dog.rain.lesson");
                world.RecordCast("rain",LocalWeather.Rain);
                world.OnTaskAccepted(rain); // Isolates the world half; this is deliberately not an AI test.
                Weather(world,player,LocalWeather.Rain,1,true,1);
                check(!world.IsSatisfied(rain),"Rain already running before acceptance cannot complete new help");
                world.RecordCast("wind",LocalWeather.Wind);
                Weather(world,player,LocalWeather.Rain,1,true,1);
                check(!world.IsSatisfied(rain),"A post-accept wind gesture cannot stand in for a rain gesture");
                world.RecordCast("rain",LocalWeather.Rain);
                Weather(world,player,LocalWeather.Rain,1,true,.2f);
                check(world.IsSatisfied(rain),"A post-accept rain gesture plus local rainfall satisfies the rain world condition");
                LifeRequest water=world.Requests.First(r=>r.id=="dog.water");
                dogBowl.State.water=1;world.OnTaskAccepted(water);world.RecordCast("rain",LocalWeather.Rain);
                Weather(world,player,LocalWeather.Rain,1,true,1);Weather(world,player,LocalWeather.Rain,1,false,.2f);
                check(!world.IsSatisfied(water),"Already-full water bowl cannot count as newly collected water");
                ModifiedInteract(world,true); // Same production Shift+F branch; player is at dog.bowl.
                check(dogBowl.State.water==0,"Actual Shift+F action empties the bowl");
                world.RecordCast("rain",LocalWeather.Rain);Weather(world,player,LocalWeather.Rain,1,true,6);
                Weather(world,player,LocalWeather.Sun,1,true,.2f);
                check(world.IsSatisfied(water) && dogBowl.State.rainStrength==0,"New collection followed by sun stops rain and retains water");
                player.position=localCenter+Vector3.right*(LifeSession.CastRadius+1);
                float waterOutside=dogBowl.State.water;
                Weather(world,player,LocalWeather.Rain,2,true,1);
                check(Mathf.Approximately(waterOutside,dogBowl.State.water) && dogBowl.State.rainStrength==0,"Leaving the circle clears active rain without removing collected water");

                LifeObject source=Find(world,"squirrel.grass"),nest=Find(world,"squirrel.nest");
                source.State.wetness=.72f;
                At(player,source);world.TryGather();
                LifeObject bundle=Get<LifeObject>(world,"held");
                check(bundle && bundle.State.kind==LifeObjectKind.Bundle && Mathf.Approximately(bundle.State.wetness,.72f),"Actual gathering creates a carried grass bundle with source wetness");
                check(bundle.GetComponentsInChildren<MeshFilter>().Any(m=>m.sharedMesh),"Harvested bundle has imported geometry");
                player.position=world.AnchorPosition("squirrel.loom");world.Interact();
                check(Get<string>(world,"craftingId")==bundle.State.id,"Actual F starts carried crafting without a dedicated loom");
                Invoke(world,"FinishCraft"); // Deterministic completion; does not claim the 1.6-second wait was tested.
                LifeObject mat=Get<LifeObject>(world,"held");
                check(mat && mat.State.kind==LifeObjectKind.Mat && Mathf.Approximately(mat.State.wetness,.72f),"Craft completion creates a carried mat and preserves moisture");
                check(mat.GetComponentsInChildren<MeshFilter>().Any(m=>m.sharedMesh),"Crafted mat has imported geometry");
                At(player,nest);world.Interact();
                check(Get<LifeObject>(world,"held")==null && mat.State.slot==nest.State.id && !mat.State.held,"Actual F places the mat in the nest slot");
                LifeRequest matRequest=world.Requests.First(r=>r.id=="squirrel.mat");
                world.OnTaskAccepted(matRequest);
                Weather(world,player,LocalWeather.Sun,1,true,20);
                check(mat.State.IsDry && !world.IsSatisfied(matRequest),"Pre-accepted placement plus later drying cannot replace a new help action");
                ModifiedInteract(world,true);
                check(Get<LifeObject>(world,"held")==mat && mat.State.held && mat.State.slot=="","Actual Shift+F retrieves the placed mat");
                check(mat.State.IsDry,"Retrieving the mat preserves its current moisture");
                world.Interact();
                check(world.IsSatisfied(matRequest),"Post-accept F placement of the dry mat satisfies the object condition");
                CheckLegacyLayout(world,nest,mat,matRequest,check);
                LifeSpeaker squirrel=world.FindSpeaker("squirrel");
                Vector3 squirrelBefore=squirrel.transform.position;
                var animal=squirrel.GetComponent<WildlifeAgent>();
                diagnostics.Add(InvitationSnapshot(life,nest,animal,"before center invitation"));
                world.Interact();
                diagnostics.Add(InvitationSnapshot(life,nest,animal,"after center invitation"));
                // Placing a mat at its center is a fixture convenience. The player must then stand
                // beside the destination so the original animal/player collision checks can pass.
                bool standingFound=PlaceForInteraction(life,nest,animal);
                diagnostics.Add(InvitationSnapshot(life,nest,animal,"beside nest fixture"));
                check(standingFound,"A clear standing point selects the nest and preserves the animal's normal route");
                check(Nearest(world)==nest,"Invitation fixture selects the nest while standing beside it");
                check(LifeWorld.Flat(player.position,squirrelBefore)<=4.5f,"Invitation fixture keeps the actual animal within talking distance");
                check(nest.State.IsWarm && mat.State.IsDry && mat.State.slot==nest.State.id,"Invitation fixture retains a dry mat at a warm nest");
                animal.ReleaseLifeTarget();
                check(!animal.HasLifeTarget,"Previous home lease is released before testing the invitation");
                world.Interact();
                diagnostics.Add(InvitationSnapshot(life,nest,animal,"after beside-nest invitation"));
                check(animal && animal.HasLifeTarget,"Actual F invitation obtains the existing animal movement lease");
                check(squirrel.transform.position==squirrelBefore,"Invitation does not change the animal's authoritative position immediately");
                IDictionary bindings=Get<IDictionary>(world,"bindings");
                object squirrelBinding=bindings["squirrel"];
                Vector3 target=(Vector3)squirrelBinding.GetType().GetField("target").GetValue(squirrelBinding);
                check(LifeWorld.Flat(target,nest.transform.position)<.01f,"Invitation points the normal movement controller at the nest");
                check(originalColliders.All(pair=>pair.Key && pair.Key.enabled==pair.Value),"All existing animal collider states remain unchanged");

                foreach(string key in new[]{"sheep","fox"})
                {
                    LifeObject warm=Find(world,key+".warm"),cold=Find(world,key+".cold");
                    check(warm.State.IsWarm && warm.State.sheltered && !cold.State.IsWarm,"Cold and sheltered warm places retain distinct temperatures: "+key);
                }
                foreach(LifeObject sheltered in world.Objects.Where(o=>o.State.sheltered))
                    check(HasRoof(world,sheltered.transform.position),"Sheltered object has an actual roof surface overhead: "+sheltered.State.id);
                var roofs=Get<Dictionary<string,Renderer[]>>(world,"shelterVisuals");
                check(ReferenceEquals(roofs["squirrel.dry"],roofs["squirrel.nest"]),"The dry demonstration reuses the nest roof instead of walling off the communal seats");
                check((bool)typeof(LifeWorld).GetMethod("SharedBowlSpace",Hidden).Invoke(world,new object[]{Find(world,"squirrel.common.bowl").transform.position}),
                    "The common bowl has clearance from original furniture and level nearby viewpoints");
                LifeLabel label=world.GetComponentsInChildren<LifeLabel>(true).First();
                player.position=label.transform.position;label.Refresh(player,life.Gameplay.CurrentView,true);
                check(!label.gameObject.activeSelf,"World object labels are hidden while the photo rig is active");
                label.Refresh(player,life.Gameplay.CurrentView,false);
                check(label.gameObject.activeSelf,"Leaving photography restores a nearby world object label");
                WalkCoastalRoute(life,check);
                check(life.Progress.tasks.Count==JsonUtility.FromJson<LifeProgress>(progressBefore).tasks.Count,"World-only test does not fabricate AI-understood tasks");
            }
            catch(Exception e){failure=e.ToString();throw;}
            finally
            {
                Set(world,"held",null);Set(world,"craftingId",null);
                var objects=Get<IDictionary>(world,"objects");
                foreach(LifeObject obj in world.GetComponentsInChildren<LifeObject>(true).Where(o=>o && !originalIds.Contains(o.State.id)).ToArray())
                {objects.Remove(obj.State.id);UnityEngine.Object.Destroy(obj.gameObject);}
                world.RestoreJson(worldBefore);
                foreach(LifeObject obj in world.Objects)obj.RefreshVisual(Time.time);
                JsonUtility.FromJsonOverwrite(progressBefore,life.Progress);
                player.SetPositionAndRotation(playerPosition,playerRotation);
                if(saveBefore!=null)File.WriteAllBytes(life.SavePath,saveBefore);else if(File.Exists(life.SavePath))File.Delete(life.SavePath);
                life.Gameplay.Notify(statusBefore);
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath,JsonUtility.ToJson(new Report{status=failure==null?"PASS":"FAIL",utc=DateTime.UtcNow.ToString("O"),checks=passed.ToArray(),diagnostics=diagnostics.ToArray(),error=failure,
                    boundary="Runtime world integration in formal scene using isolated saves. Initial player positions and object states were arranged synchronously and restored. F logic is real; Shift modifier and FinishCraft use reflection. Coastal traversal calls the existing AdvanceMovement with its actual CharacterController, gravity and displacement constraints; it is synchronous collision proof, not keyboard, animation or visual acceptance. No elapsed crafting-time, arrived-animal animation, gesture recognition or live AI acceptance is claimed."},true));
            }
            return passed.ToArray();
        }
        static bool HasRoof(LifeWorld world,Vector3 point)
        {
            foreach(MeshFilter filter in UnityEngine.Object.FindObjectsOfType<MeshFilter>())
            {
                var renderer=filter.GetComponent<MeshRenderer>();
                if(!filter.sharedMesh || !renderer || !renderer.enabled || renderer.bounds.max.y<point.y+1 ||
                    renderer.bounds.min.x>point.x || renderer.bounds.max.x<point.x || renderer.bounds.min.z>point.z || renderer.bounds.max.z<point.z)continue;
                using(var meshes=MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
                using(var vertices=new NativeArray<Vector3>(meshes[0].vertexCount,Allocator.Temp))
                {
                    Mesh.MeshData mesh=meshes[0];mesh.GetVertices(vertices);
                    for(int submesh=0;submesh<mesh.subMeshCount;submesh++)
                    using(var triangles=new NativeArray<int>(mesh.GetSubMesh(submesh).indexCount,Allocator.Temp))
                    {
                        mesh.GetIndices(triangles,submesh);
                        for(int i=0;i<triangles.Length;i+=3)
                        {
                            Vector3 a=filter.transform.TransformPoint(vertices[triangles[i]]),b=filter.transform.TransformPoint(vertices[triangles[i+1]]),c=filter.transform.TransformPoint(vertices[triangles[i+2]]);
                            Vector2 ab=new Vector2(b.x-a.x,b.z-a.z),ac=new Vector2(c.x-a.x,c.z-a.z),ap=new Vector2(point.x-a.x,point.z-a.z);
                            float determinant=ab.x*ac.y-ab.y*ac.x;
                            if(Mathf.Abs(determinant)<.000001f)continue;
                            float u=(ap.x*ac.y-ap.y*ac.x)/determinant,v=(ab.x*ap.y-ab.y*ap.x)/determinant;
                            if(u>=0 && v>=0 && u+v<=1 && a.y+u*(b.y-a.y)+v*(c.y-a.y)>point.y+1)return true;
                        }
                    }
                }
            }
            return false;
        }
        static LifeObject Find(LifeWorld world,string id)=>world.ObjectAt(id)??throw new InvalidOperationException("Missing world object "+id);
        static void CheckLegacyLayout(LifeWorld world,LifeObject nest,LifeObject mat,LifeRequest request,Action<bool,string> check)
        {
            var freeState=new LifeObjectState{id="review.legacy.free.bundle",kind=LifeObjectKind.Bundle,position=nest.transform.position+Vector3.forward*3,wetness=.73f};
            var free=(LifeObject)typeof(LifeWorld).GetMethod("CreateItem",Hidden).Invoke(world,new object[]{freeState});
            string original=world.CaptureJson();Vector3 nestPosition=nest.transform.position,animalPosition=world.FindSpeaker("squirrel").transform.position;
            try
            {
                Type saveType=typeof(LifeWorld).GetNestedType("WorldSave",BindingFlags.NonPublic);
                object old=JsonUtility.FromJson(original,saveType);saveType.GetField("layoutVersion").SetValue(old,0);
                var states=(List<LifeObjectState>)saveType.GetField("objects").GetValue(old);
                Vector3 oldNest=nestPosition+Vector3.right*1.2f;
                states.First(s=>s.id==nest.State.id).position=oldNest;
                states.First(s=>s.id==mat.State.id).position=oldNest+Vector3.up*.045f;
                foreach(object animal in (IList)saveType.GetField("animals").GetValue(old))
                    if((string)animal.GetType().GetField("key").GetValue(animal)=="squirrel")
                    {animal.GetType().GetField("target").SetValue(animal,oldNest);animal.GetType().GetField("rest").SetValue(animal,true);}
                world.RestoreJson(JsonUtility.ToJson(old));
                check(nest.transform.position==nestPosition && mat.transform.position==nestPosition+Vector3.up*.045f,"Legacy fixed stations keep the validated layout and slotted mats follow their station");
                check(free.transform.position==freeState.position && Mathf.Approximately(free.State.wetness,.73f),"Legacy freely placed items retain their own saved position and physical state");
                object binding=Get<IDictionary>(world,"bindings")["squirrel"];
                check((Vector3)binding.GetType().GetField("target").GetValue(binding)==nestPosition && world.FindSpeaker("squirrel").transform.position==animalPosition,
                    "Legacy animal goals map through the old station identity without moving the animal");
                check(world.IsSatisfied(request),"Legacy layout migration preserves accepted help and object action history");
                LifeObject dry=Find(world,"squirrel.dry");Vector3 newDry=dry.transform.position;
                LifeObjectState oldDry=states.First(s=>s.id==dry.State.id);oldDry.position=newDry+Vector3.forward*3;oldDry.actionRevision=0;oldDry.held=false;oldDry.slot="";
                world.RestoreJson(JsonUtility.ToJson(old));
                check(dry.transform.position==newDry,"An untouched old demonstration mat migrates under the reused nest roof");
                oldDry.actionRevision=1;oldDry.position=newDry+Vector3.right*2;
                world.RestoreJson(JsonUtility.ToJson(old));
                check(dry.transform.position==oldDry.position,"A player-moved demonstration mat keeps its saved position across the layout update");
            }
            finally {world.RestoreJson(original);}
        }
        static void At(Transform player,LifeObject obj)=>player.position=obj.transform.position;
        static void Weather(LifeWorld world,Transform player,LocalWeather kind,int strength,bool active,float seconds)
        {for(int i=0;i<Mathf.CeilToInt(seconds/.1f);i++)world.ApplyWeather(player.position,LifeSession.CastRadius,kind,strength,active,.1f);}
        static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,Hidden).GetValue(target);
        static void Set(object target,string name,object value)=>target.GetType().GetField(name,Hidden).SetValue(target,value);
        static void Invoke(object target,string name)=>target.GetType().GetMethod(name,Hidden).Invoke(target,null);
        static LifeObject Nearest(LifeWorld world)=>(LifeObject)typeof(LifeWorld).GetMethod("Nearest",Hidden).Invoke(world,null);
        internal static bool PlaceForInteraction(LifeSession life,LifeObject place,WildlifeAgent walking=null,WildlifeAgent companion=null)
        {
            Transform player=life.Gameplay.PlayerTransform;Vector3 before=player.position;
            CharacterController capsule=player.GetComponent<CharacterController>();Terrain ground=Terrain.activeTerrain;
            float bottom=capsule?player.TransformVector(capsule.center).y-capsule.height*Mathf.Abs(player.lossyScale.y)*.5f:0;
            WildlifePopulation population=walking?Get<WildlifePopulation>(walking,"population"):null;
            foreach(float radius in new[]{1.1f,1.45f,1.8f,.8f})for(int i=0;i<24;i++)
            {
                Vector3 point=place.transform.position+Quaternion.Euler(0,i*15,0)*Vector3.right*radius;
                if(ground)point.y=ground.SampleHeight(point)+ground.transform.position.y;
                point.y-=bottom;player.position=point;Physics.SyncTransforms();
                if(Nearest(life.World)!=place || walking && LifeWorld.Flat(point,walking.transform.position)>4.5f)continue;
                if(companion && LifeWorld.Flat(point,companion.transform.position)>=LifeSession.CastRadius)continue;
                if(capsule)
                {
                    Vector3 center=player.TransformPoint(capsule.center);
                    float r=capsule.radius*Mathf.Max(Mathf.Abs(player.lossyScale.x),Mathf.Abs(player.lossyScale.z));
                    float half=Mathf.Max(0,capsule.height*Mathf.Abs(player.lossyScale.y)*.5f-r);
                    if(Physics.OverlapCapsule(center+Vector3.up*half,center-Vector3.up*half,r*.95f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)
                        .Any(c=>!(c is TerrainCollider) && !c.transform.IsChildOf(player)))continue;
                }
                if(walking && (!population || !population.TryMove(walking,place.transform.position,out _)))continue;
                if(companion)
                {
                    var otherPopulation=Get<WildlifePopulation>(companion,"population");
                    if(!otherPopulation || !otherPopulation.TryMove(companion,life.World.AnchorPosition("squirrel.common.companion"),out _))continue;
                }
                return true;
            }
            player.position=before;Physics.SyncTransforms();return false;
        }
        static string InvitationSnapshot(LifeSession life,LifeObject nest,WildlifeAgent animal,string stage)
        {
            LifeWorld world=life.World;Vector3 player=life.Gameplay.PlayerTransform.position;
            object binding=Get<IDictionary>(world,"bindings")["squirrel"];
            Vector3 target=(Vector3)binding.GetType().GetField("target").GetValue(binding);
            WildlifePopulation population=Get<WildlifePopulation>(animal,"population");
            bool route=population && population.TryMove(animal,nest.transform.position,out _);
            return stage+": player="+player.ToString("F3")+", animal="+animal.transform.position.ToString("F3")+
                ", nest="+nest.transform.position.ToString("F3")+", nearest="+(Nearest(world)?.State.id??"none")+
                ", animalDistance="+LifeWorld.Flat(player,animal.transform.position).ToString("F3")+
                ", warm="+nest.State.IsWarm+", route="+route+", hasLease="+animal.HasLifeTarget+
                ", bindingTarget="+target.ToString("F3")+", leaseTarget="+Get<Vector3>(animal,"lifeTarget").ToString("F3")+
                ", feedback="+life.Gameplay.Status;
        }
        static void ModifiedInteract(LifeWorld world,bool shift)=>typeof(LifeWorld).GetMethod("InteractWithModifiers",Hidden).Invoke(world,new object[]{shift});
        static void WalkCoastalRoute(LifeSession life,Action<bool,string> check)
        {
            var mover=life.Gameplay.forestSprite;
            CharacterController capsule=mover?mover.GetComponent<CharacterController>():null;
            check(mover && capsule && capsule.enabled,"Actual player CharacterController is available for coastal route");
            var state=mover.GetType().GetFields(Hidden|BindingFlags.DeclaredOnly).Where(f=>!f.IsInitOnly && f.FieldType.IsValueType)
                .ToDictionary(f=>f,f=>f.GetValue(mover));
            MethodInfo advance=mover.GetType().GetMethod("AdvanceMovement",Hidden);
            try
            {
                Vector3 here=life.World.AnchorPosition("harbor.sheep.here"),tide=life.World.AnchorPosition("harbor.tide");
                float scaleY=Mathf.Abs(mover.transform.lossyScale.y);
                float bottomOffset=mover.transform.TransformVector(capsule.center).y-capsule.height*scaleY*.5f;
                mover.transform.position=here+Vector3.up*(-bottomOffset+.08f);
                Physics.SyncTransforms();
                for(int i=0;i<20;i++)advance.Invoke(mover,new object[]{Vector3.zero,false,false,.02f});
                LifeRequest go=life.World.Requests.First(r=>r.id=="sheep.go");
                life.World.OnTaskAccepted(go);
                Walk(tide,"Actual player capsule walks from ferry to tide-pool dry anchor");
                check(life.World.IsSatisfied(go),"Coastal go condition follows actual post-accept controller movement");
                LifeRequest come=life.World.Requests.First(r=>r.id=="sheep.here");
                life.World.OnTaskAccepted(come);
                Walk(here,"Actual player capsule returns from tide-pool anchor to ferry");
                check(life.World.IsSatisfied(come),"Coastal come condition follows the return movement");
                check(capsule.enabled,"Coastal movement retained the original collision component");
                void Walk(Vector3 destination,string label)
                {
                    int steps=0;
                    while(LifeWorld.Flat(mover.transform.position,destination)>.55f && steps++<800)
                    {
                        Vector3 direction=destination-mover.transform.position;direction.y=0;direction.Normalize();
                        advance.Invoke(mover,new object[]{direction,false,false,.02f});
                        if(mover.transform.position.y<destination.y-2)break;
                    }
                    check(LifeWorld.Flat(mover.transform.position,destination)<=.55f && Mathf.Abs(mover.transform.position.y-destination.y)<1,label+" (steps="+steps+")");
                }
            }
            finally{foreach(var field in state)field.Key.SetValue(mover,field.Value);}
        }
    }
}
