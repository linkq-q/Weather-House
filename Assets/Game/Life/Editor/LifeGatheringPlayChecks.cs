using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    public static class LifeGatheringPlayChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        [Serializable] sealed class Report
        {
            public string status,error,boundary="Called inside the isolated-save world review. Player positions, observed target and UI flags are fixtures; no animal is moved. E uses the production HUD entry; synchronous crafting invokes FinishCraft. Actual elapsed crafting and forage playback remain in the async world review.";
            public int matchingInstances,boundSources,maximumNeighbourVisits;
            public string[] checks;
        }
        public static void Run(LifeSession life,Action<bool,string> parentCheck)
        {
            var result=new Report();var passed=new List<string>();
            Action<bool,string> check=(ok,label)=>{parentCheck(ok,label);passed.Add(label);};
            LifeWorld world=life.World;GameplaySession game=life.Gameplay;GameplayHud hud=game.GetComponent<GameplayHud>();
            Transform player=game.PlayerTransform;Vector3 position=player.position;
            string before=world.CaptureJson();var ids=new HashSet<string>(world.Objects.Select(o=>o.State.id));
            AnimalActor observed=game.Observed;bool judging=game.Judging,journal=hud.JournalOpen,camera=hud.CameraMode,gesture=life.GestureInputActive;
            int composition=(int)Field(hud,"lastCompositionFrame").GetValue(hud);
            try
            {
                LifePlantSpecies species=Resources.Load<LifePlantSpecies>("LifeContent/GatheringGrass");
                check(species && species.variants.Length==3 && species.variants.All(m=>m),"Gathering species registers all three original shared meshes");
                var roots=new HashSet<Transform>();
                foreach(MeshFilter filter in UnityEngine.Object.FindObjectsOfType<MeshFilter>(true))
                    if(species.Matches(filter.sharedMesh) && filter.gameObject.scene.IsValid())
                    {LODGroup lod=filter.GetComponentInParent<LODGroup>();roots.Add(lod?lod.transform:filter.transform);}
                result.matchingInstances=roots.Count;result.boundSources=world.GatheringSources.Count;
                check(roots.Count>20 && roots.SetEquals(world.GatheringSources.Select(o=>o.transform)),"Every selected-species instance is bound exactly once, including LOD renderers");
                check(!world.Objects.Any(o=>o.State.kind==LifeObjectKind.Loom) && !world.GetComponentsInChildren<LifeObject>().Any(o=>o.State.kind==LifeObjectKind.GrassSource),
                    "Dedicated grass models and crafting stations are absent");
                check(world.ObjectAt("squirrel.grass") && roots.Contains(world.ObjectAt("squirrel.grass").transform),"Legacy grass observation points to a real plant of the registered species");
                SetProperty(game,"Observed",null);SetProperty(game,"Judging",false);SetProperty(hud,"JournalOpen",false);SetProperty(hud,"CameraMode",false);
                life.SetGestureInput(false);Field(hud,"lastCompositionFrame").SetValue(hud,-10);
                foreach(LifeObject source in world.GatheringSources)
                {
                    if(!source.gameObject.activeInHierarchy)continue;
                    player.position=source.transform.position;
                    LifeObject found=world.GatheringTarget();result.maximumNeighbourVisits=Math.Max(result.maximumNeighbourVisits,world.LastGatheringVisits);
                    check(found==source,"Local index finds the actual plant: "+source.State.id);
                }
                check(result.maximumNeighbourVisits<world.GatheringSources.Count/2,"Near gathering visits local grid cells rather than scanning the entire population");
                LifeObject first=world.GatheringSources.OrderBy(o=>o.transform.position.x).First();
                LifeObject second=world.GatheringSources.OrderByDescending(o=>LifeWorld.Flat(o.transform.position,first.transform.position)).First();
                check(LifeWorld.Flat(first.transform.position,second.transform.position)>20,"Two material sources are in different map areas");
                player.position=first.transform.position;
                check(hud.PrimaryInteraction==LifePrimaryAction.Gather && hud.GatheringHint=="E 采集","A real plant enables the production E gathering action and prompt");
                foreach(string modal in new[]{"JournalOpen","CameraMode"})
                {
                    SetProperty(hud,modal,true);hud.InteractPrimary();
                    check(hud.PrimaryInteraction==LifePrimaryAction.None && Held(world)==null,"E cannot gather while "+modal);
                    SetProperty(hud,modal,false);
                }
                life.SetGestureInput(true);hud.InteractPrimary();
                check(hud.PrimaryInteraction==LifePrimaryAction.None && Held(world)==null,"E cannot gather during gesture input");life.SetGestureInput(false);
                SetProperty(game,"Judging",true);hud.InteractPrimary();
                check(hud.PrimaryInteraction==LifePrimaryAction.None && Held(world)==null,"E cannot gather during AI judgement");SetProperty(game,"Judging",false);
                Field(hud,"lastCompositionFrame").SetValue(hud,Time.frameCount);hud.InteractPrimary();
                check(hud.PrimaryInteraction==LifePrimaryAction.None && Held(world)==null,"E cannot gather during the Chinese composition frame guard");
                Field(hud,"lastCompositionFrame").SetValue(hud,-10);
                AnimalActor animal=world.FindSpeaker("squirrel").Actor;
                player.position=animal.transform.position+Vector3.right;SetProperty(game,"Observed",animal);
                check(hud.PrimaryInteraction==LifePrimaryAction.Talk && hud.GatheringHint=="","Nearby observed animal reserves E for conversation");
                SetProperty(game,"Observed",null);
                foreach(LifeObject source in new[]{first,second})
                {
                    player.position=source.transform.position;source.State.wetness=source==first?.72f:LifeObjectState.DryThreshold;
                    hud.InteractPrimary();LifeObject bundle=Held(world);
                    check(bundle && bundle.State.kind==LifeObjectKind.Bundle && bundle.State.wetness==source.State.wetness,"E creates source-independent material with physical wetness");
                    // A far source is also a valid crafting location; no loom fixture exists here.
                    world.Interact();check(Field(world,"craftingId").GetValue(world) as string==bundle.State.id,"F starts crafting at the harvested location");
                    Call(world,"FinishCraft");LifeObject mat=Held(world);
                    check(mat && mat.State.kind==LifeObjectKind.Mat && mat.State.wetness==source.State.wetness,"Both remote sources craft the same material type without losing wetness");
                    string carried=world.CaptureJson();mat.State.wetness=0;world.RestoreJson(carried);
                    check(Held(world)==mat && mat.State.wetness==source.State.wetness,"Carried cross-source material survives world save and restore");
                    LifeRequest bedding=world.Requests.First(r=>r.condition=="dry_mat");
                    Vector3 animalPoint=world.FindSpeaker(bedding.speakerKey).transform.position;
                    bool free=false;
                    for(int i=0;i<16;i++)
                    {
                        Vector3 at=animalPoint+Quaternion.Euler(0,i*22.5f,0)*Vector3.forward*5;
                        if(Terrain.activeTerrain)at.y=Terrain.activeTerrain.SampleHeight(at)+Terrain.activeTerrain.transform.position.y;
                        player.position=at;
                        if(world.GetType().GetMethod("Nearest",Hidden).Invoke(world,null)==null){free=true;break;}
                    }
                    check(free,"A nearby free placement exists outside dedicated task stations");
                    world.OnTaskAccepted(bedding);world.Interact();
                    check(!Held(world) && mat.State.slot.StartsWith("rest.",StringComparison.Ordinal) && mat.State.slot!=bedding.targetId,
                        "Actual F places either material source at its own usable rest site");
                    if(!mat.State.IsDry)
                    {
                        check(!world.IsSatisfied(bedding),"Wet material at an alternative site does not satisfy dry bedding");
                        for(int step=0;step<90;step++)world.ApplyWeather(player.position,LifeSession.CastRadius,LocalWeather.Sun,2,true,.2f);
                    }
                    check(world.IsSatisfied(bedding),"Dry bedding help accepts the nearby material state without a private task object id");
                    var invalid=(HashSet<string>)Field(world,"invalidTargets").GetValue(world);bool wasInvalid=invalid.Contains(bedding.targetId);
                    invalid.Add(bedding.targetId);
                    try{check(world.IsSatisfied(bedding),"An unavailable authored nest cannot invalidate a legal alternative material location");}
                    finally{if(!wasInvalid)invalid.Remove(bedding.targetId);}
                    string beforeLegacy=world.CaptureJson();
                    try
                    {
                        object accepted=((IDictionary)Field(world,"accepted").GetValue(world))[bedding.id];
                        int oldBaseline=(int)accepted.GetType().GetField("baseline").GetValue(accepted);
                        ((IList)accepted.GetType().GetField("objects").GetValue(accepted)).Clear();
                        LifeObject oldSite=world.ObjectAt(bedding.targetId);oldSite.State.actionRevision=oldBaseline+1;
                        string alternative=mat.State.slot;mat.State.slot=bedding.targetId;mat.State.actionRevision=0;
                        world.RestoreJson(world.CaptureJson());
                        check(world.IsSatisfied(bedding),"An old save retains a post-accept placement recorded on its original slot");
                        mat.State.slot=alternative;
                        check(!world.IsSatisfied(bedding),"Old slot history cannot fabricate a placement on unrelated material");
                    }
                    finally{world.RestoreJson(beforeLegacy);}
                }
                check(world.Objects.Count(o=>o.State.kind==LifeObjectKind.Bowl && !o.transform.IsChildOf(world.transform))>0,"Existing map water containers are bound without duplicating their model or disabling collision");
                result.status="PASS";
            }
            catch(Exception error){result.status="FAIL";result.error=error.ToString();throw;}
            finally
            {
                Field(world,"held").SetValue(world,null);Field(world,"craftingId").SetValue(world,null);
                IDictionary objects=(IDictionary)Field(world,"objects").GetValue(world);
                foreach(LifeObject obj in world.Objects.Where(o=>o && !ids.Contains(o.State.id)).ToArray())
                {objects.Remove(obj.State.id);((ICollection<LifeObject>)Field(world,"interactables").GetValue(world)).Remove(obj);UnityEngine.Object.Destroy(obj.gameObject);}
                world.RestoreJson(before);player.position=position;life.SetGestureInput(gesture);
                SetProperty(game,"Observed",observed);SetProperty(game,"Judging",judging);SetProperty(hud,"JournalOpen",journal);SetProperty(hud,"CameraMode",camera);
                Field(hud,"lastCompositionFrame").SetValue(hud,composition);Physics.SyncTransforms();
                result.checks=passed.ToArray();Directory.CreateDirectory("LifeGameplayReview/20261026/play");
                File.WriteAllText("LifeGameplayReview/20261026/play/gathering-report.json",JsonUtility.ToJson(result,true));
            }
        }
        static FieldInfo Field(object target,string name)=>target.GetType().GetField(name,Hidden)??throw new MissingFieldException(name);
        static void SetProperty(object target,string property,object value)=>Field(target,"<"+property+">k__BackingField").SetValue(target,value);
        static void Call(object target,string method)=>target.GetType().GetMethod(method,Hidden).Invoke(target,null);
        static LifeObject Held(LifeWorld world)=>(LifeObject)Field(world,"held").GetValue(world);
    }
}
