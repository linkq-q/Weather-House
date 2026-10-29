using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using WeatherHouse.AnimalToon;
using WeatherHouse.Hillside;
using WeatherHouse.StoneEngraving.Editor;
using WeatherHouse.WatersideV2;
using WeatherHousePrototype;
using Object=UnityEngine.Object;

namespace WeatherHouse.Life.Editor
{
    public static class AnimalPaperWorldChecks
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
        public static IEnumerator Run()
        {
            var world=Object.FindObjectOfType<AnimalPaperWorld>();var game=Object.FindObjectOfType<GameplaySession>();var hud=game.GetComponent<GameplayHud>();
            Check(world&&world.RendererCount>0,"Real scene animals must be covered.");
            var originals=(Dictionary<Renderer,Material[]>)typeof(AnimalPaperWorld).GetField("originals",Private).GetValue(world);
            var styled=originals.Keys.ToDictionary(r=>r,r=>r.sharedMaterials);
            var log=new StringBuilder();
            foreach(var pair in originals)
            {
                for(int i=0;i<pair.Value.Length;i++)
                {
                    var source=pair.Value[i];var material=pair.Key.sharedMaterials[i];if(!source||source.renderQueue>=3000)continue;
                    bool butterfly=pair.Key.GetComponentInParent<WildlifeAgent>()?.speciesId=="butterfly";
                    bool playerPart=pair.Key.transform.IsChildOf(game.forestSprite.transform);
                    Check(material.GetFloat("_EnvironmentShadowsOnly")== (playerPart?0:1),"Only animals use environment-only shadows.");
                    Check(material.shader.name=="Weather House/Animals/Paper Toon"&&Mathf.Approximately(material.GetFloat("_OutlineWidth"),butterfly?0:8f/3f)&&material.GetFloat("_ToonStrength")==1&&Mathf.Approximately(material.GetFloat("_BandSoftness"),.01f),"Approved animal style parameters must match.");
                    string map=source.HasProperty("_BaseMap")?"_BaseMap":"_MainTex";
                    if(source.HasProperty(map))Check(source.GetTexture(map)==material.GetTexture("_BaseMap"),"Original animal texture must be retained.");
                    if(source.HasProperty("_Cull"))Check(source.GetFloat("_Cull")==material.GetFloat("_Cull"),"Double-sided cape rendering must be retained.");
                }
            }
            Check(game.forestSprite.GetComponentsInChildren<Renderer>(true).Any(originals.ContainsKey),"Player must share the approved paper rendering.");
            Check(originals.Keys.Any(r=>r.GetComponentInParent<WildlifeAgent>()?.speciesId=="butterfly"),"Scene butterflies must be checked.");
            AnimalShadowChecks.Run(game);
            log.AppendLine("PASS butterflies have zero outline; animal-only shadow sampling; environment/ground pixel regression passed.");
            var player=game.PlayerTransform;var controller=player.GetComponent<CharacterController>();var start=player.position;
            var terrain=Object.FindObjectsOfType<Terrain>().First(t=>t.gameObject.scene==game.gameObject.scene);
            Camera playerView=null;
            Directory.CreateDirectory("PhotoStillReview/20261029/Animals");
            void Place(Vector3 p){controller.enabled=false;player.position=p;controller.enabled=true;Physics.SyncTransforms();}
            try
            {
                var life=game.GetComponent<LifeSession>();game.forestSprite.SetScriptedInput(Vector2.zero,false,false);
                playerView=new GameObject("Player paper review camera",typeof(Camera),typeof(UniversalAdditionalCameraData),typeof(AnimalToonView)).GetComponent<Camera>();
                playerView.CopyFrom(game.PlayerViewCamera);playerView.enabled=false;playerView.orthographic=true;playerView.orthographicSize=1.25f;playerView.GetComponent<AnimalToonView>().mergeRadius=12;
                var target=player.position+Vector3.up*.95f;
                playerView.transform.position=target+player.forward*3.5f+player.right*.8f+Vector3.up*.6f;playerView.transform.LookAt(target);
                for(int n=0;n<3;n++)yield return null;
                foreach(string side in new[]{"front","back"})
                {
                    if(side=="back"){playerView.transform.position=target-player.forward*3.5f+player.right*.8f+Vector3.up*.6f;playerView.transform.LookAt(target);}
                    StoneEngravingPlacementReview.Capture(playerView,"PhotoStillReview/20261029/Animals/player-"+side+"-toon.png",960,960);
                    foreach(var material in styled.Values.SelectMany(m=>m).Distinct()){material.SetColor("_ShadeColor",new Color(.62f,.66f,.72f,1));material.SetFloat("_ShadowFloor",0);}
                    try{StoneEngravingPlacementReview.Capture(playerView,"PhotoStillReview/20261029/Animals/player-"+side+"-deep-before.png",960,960);}
                    finally{foreach(var material in styled.Values.SelectMany(m=>m).Distinct()){material.SetColor("_ShadeColor",new Color(.82f,.80f,.76f,1));material.SetFloat("_ShadowFloor",.12f);}}
                }
                log.AppendLine("PASS player front/back captures, same-frame old/new deepest shading; source texture and cape cull mode retained; no emission term.");
                foreach(string species in new[]{"dog","squirrel","fox","sheep","deer"})
                {
                    var speaker=life.World.FindSpeaker(species);Check(speaker,"Missing scene species "+species);
                    var visual=speaker.Actor.VisualRoot;Check(visual,"Missing animal visual "+species);
                    var renderers=visual.GetComponentsInChildren<Renderer>(true).Where(originals.ContainsKey).ToArray();Check(renderers.Length>0,"Species was not converted: "+species);
                    var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                    var at=bounds.center+new Vector3(1.6f,0,-2.4f);at.y=WatersideLayout.GroundHeight(terrain,at.x,at.z)+.15f;Place(at);yield return null;
                    hud.SetCameraMode(true,true);var rig=game.PhotoRig;rig.Zoom((54-rig.FocalLength)/5f);
                    var angles=Quaternion.LookRotation(bounds.center-rig.CaptureCamera.transform.position).eulerAngles;
                    typeof(PhotoCameraRig).GetField("photoYaw",Private).SetValue(rig,angles.y);typeof(PhotoCameraRig).GetField("photoPitch",Private).SetValue(rig,Mathf.DeltaAngle(0,angles.x));
                    for(int n=0;n<4;n++)yield return null;
                    Check(rig.CaptureCamera.GetComponent<AnimalToonView>().mergeRadius==12,"Photo camera must use approved merge=12.");
                    StoneEngravingPlacementReview.Capture(rig.CaptureCamera,"PhotoStillReview/20261029/Animals/"+species+"-toon.png",1280,720);
                    if(species=="deer")
                    {
                        var animalMaterials=styled.Values.SelectMany(m=>m).Distinct().Where(m=>m.HasProperty("_EnvironmentShadowsOnly")&&m.GetFloat("_EnvironmentShadowsOnly")>.5f).ToArray();
                        foreach(var mat in animalMaterials)mat.SetFloat("_EnvironmentShadowsOnly",0);
                        try{StoneEngravingPlacementReview.Capture(rig.CaptureCamera,"PhotoStillReview/20261029/Animals/deer-self-before.png",1280,720);}
                        finally{foreach(var mat in animalMaterials)mat.SetFloat("_EnvironmentShadowsOnly",1);}
                    }
                    // Same camera and same frame, temporarily restore the original materials for an attributable comparison.
                    foreach(var pair in originals)if(pair.Key)pair.Key.sharedMaterials=pair.Value;
                    var view=rig.CaptureCamera.GetComponent<AnimalToonView>();view.enabled=false;
                    try{StoneEngravingPlacementReview.Capture(rig.CaptureCamera,"PhotoStillReview/20261029/Animals/"+species+"-original.png",1280,720);}
                    finally{foreach(var pair in styled)if(pair.Key)pair.Key.sharedMaterials=pair.Value;view.enabled=true;}
                    hud.SetCameraMode(false,true);yield return null;yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.GetFullPath("PhotoStillReview/20261029/Animals/"+species+"-game.png"));yield return new WaitForEndOfFrame();yield return null;
                    log.AppendLine("PASS "+species+": "+renderers.Length+" renderers; original texture and approved material values; same-frame camera comparison.");
                }
                int count=world.RendererCount;world.RefreshAnimals();Check(world.RendererCount==count,"Repeated refresh must not multiply materials/renderers.");
                long bytes=Resources.FindObjectsOfTypeAll<RenderTexture>().Where(t=>t&&t.name.StartsWith("Animal Paper ",StringComparison.Ordinal)).Sum(t=>(long)t.width*t.height*(t.name.Contains("Depth")?4:8));
                log.AppendLine($"PASS covered={count} renderers, shared source materials={world.MaterialCount}; player included; live animation bindings retained. Animal RT allocation estimate={bytes/1048576f:F1} MiB at current resolution. This is an allocation observation, not an FPS benchmark.");
                File.WriteAllText("PhotoStillReview/20261029/toon-world-checks.txt",log.ToString());
            }
            finally{hud.SetCameraMode(false,true);Place(start);game.forestSprite.ReleaseScriptedInput();if(playerView)Object.Destroy(playerView.gameObject);foreach(var pair in styled)if(pair.Key)pair.Key.sharedMaterials=pair.Value;}
        }
    }
}
