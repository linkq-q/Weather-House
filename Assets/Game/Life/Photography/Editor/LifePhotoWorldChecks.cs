using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    public static class LifePhotoWorldChecks
    {
        const string Folder="LifeGameplayReview/20261026/photo-flow";
        public static void WriteWorldLayout(LifeSession life)
        {
            Vector3 center=life.World.AnchorPosition("squirrel.common");var lines=new List<string>{DateTime.UtcNow.ToString("O")};
            foreach(LifeObject obj in life.World.Objects.Where(o=>o && LifeWorld.Flat(o.transform.position,center)<8))
                if(obj.State.kind!=LifeObjectKind.GrassSource)lines.Add("object "+obj.State.id+" "+obj.State.kind+" "+obj.transform.position.ToString("F3")+" sheltered="+obj.State.sheltered);
            var roofs=(Dictionary<string,Renderer[]>)typeof(LifeWorld).GetField("shelterVisuals",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(life.World);
            foreach(var pair in roofs)foreach(Renderer part in pair.Value)
                if(part && part.bounds.SqrDistance(center)<36)lines.Add("shelter "+pair.Key+" "+Hierarchy(part.transform)+" position="+part.transform.position.ToString("F3")+" bounds="+part.bounds.ToString("F3"));
            foreach(Renderer part in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if(!part.enabled || part.gameObject.layer==LayerMask.NameToLayer("UI") || part.bounds.size.magnitude>30 || part.bounds.SqrDistance(center)>20)continue;
                MeshFilter filter=part.GetComponent<MeshFilter>();Mesh mesh=filter?filter.sharedMesh:(part is SkinnedMeshRenderer skin?skin.sharedMesh:null);
                lines.Add("geometry "+Hierarchy(part.transform)+" bounds="+part.bounds.ToString("F3")+" mesh="+UnityEditor.AssetDatabase.GetAssetPath(mesh));
            }
            Directory.CreateDirectory(Folder);File.WriteAllLines(Folder+"/world-layout.txt",lines);
        }
        static string Hierarchy(Transform child)
        {string path=child.name;while(child.parent){child=child.parent;path=child.name+"/"+path;}return path;}
        [Serializable] sealed class Report
        {
            public string status,error,utc;
            public string boundary="Actual formal-scene models and shutter-scoped rendered depth, including opaque geometry without a Collider, cutout holes and transparent surfaces. Camera and temporary occluders are test fixtures, no animal is repositioned. Actual eating/shared-rest snapshots run in world-animation with the original controllers. Human composition and animation quality remain acceptance work.";
            public List<string> checks=new List<string>();
            public List<string> depthDiagnostics=new List<string>();
            public LifePhotoSnapshot portrait,tailOnly,occluded;
        }
        public static void RunDepth(LifeSession life)
        {
            if(!Application.isPlaying || !life || !life.World)throw new InvalidOperationException("Depth checks require the isolated formal scene in Play.");
            Directory.CreateDirectory(Folder);var report=new Report{status="RUNNING",utc=DateTime.UtcNow.ToString("o")};Camera camera=NewCamera();
            try
            {
                CheckRenderedOcclusion(camera,(ok,label)=>
                {
                    report.depthDiagnostics.Add(label+"; "+LifePhotoDepth.LastDiagnostic+"; "+LifePhotoDepth.LastPointDiagnostic);
                    if(!ok)throw new InvalidOperationException(label);report.checks.Add(label);
                });
                report.status="PASS";
            }
            catch(Exception error){report.status="FAIL";report.error=error.ToString();throw;}
            finally{UnityEngine.Object.DestroyImmediate(camera.gameObject);File.WriteAllText(Folder+"/depth-checks.json",JsonUtility.ToJson(report,true));}
        }
        public static void Run(LifeSession life)
        {
            if(!Application.isPlaying || !life || !life.World)throw new InvalidOperationException("Photography world checks require isolated Play mode.");
            Directory.CreateDirectory(Folder);var report=new Report{status="RUNNING",utc=DateTime.UtcNow.ToString("o")};
            Camera camera=NewCamera();GameObject blocker=null;
            Action<bool,string> check=(ok,label)=>{if(!ok)throw new InvalidOperationException(label);report.checks.Add(label);};
            try
            {
                LifePhotoRuleChecks.Run();check(true,"All 22 photo and 4 interaction definitions match approved IDs and reward sets");
                AnimalActor actor=life.World.FindSpeaker("squirrel").Actor;
                check(FindView(life,camera,actor,out LifePhotoSnapshot portrait),"An actual squirrel torso is visible from a nearby accessible camera direction");
                report.portrait=portrait;check(LifePhotoRules.Match(portrait,null).Contains("P02"),"Actual visible portrait earns squirrel without word knowledge or animal dialogue");
                SaveImage(camera,"portrait.png");
                Vector3 cameraPosition=camera.transform.position;Quaternion cameraRotation=camera.transform.rotation;
                Transform tail=actor.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Tail",StringComparison.OrdinalIgnoreCase)).
                    OrderByDescending(t=>Vector3.Distance(t.position,actor.transform.position)).FirstOrDefault();
                check(tail,"The original squirrel has an actual tail feature for the negative framing case");
                camera.orthographic=true;camera.orthographicSize=.045f;camera.transform.position=tail.position+(cameraPosition-actor.transform.position).normalized*.8f;
                camera.transform.LookAt(tail.position);report.tailOnly=LifePhotoCapture.Capture(life,camera);
                check(!HasAnimal(report.tailOnly,actor.animalId),"Tail-tip-only framing cannot count as an animal portrait");SaveImage(camera,"tail-only.png");
                camera.orthographic=false;camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
                Vector3 target=actor.transform.position+Vector3.up*.35f;
                blocker=Blocker(Vector3.Lerp(cameraPosition,target,.55f),camera.transform.rotation,new Vector3(1.8f,2.2f,.12f));
                Physics.SyncTransforms();report.occluded=LifePhotoCapture.Capture(life,camera);
                check(!HasAnimal(report.occluded,actor.animalId),"Opaque foreground geometry prevents a hidden animal body from earning a portrait");SaveImage(camera,"occluded-portrait.png");
                UnityEngine.Object.DestroyImmediate(blocker);blocker=null;Physics.SyncTransforms();
                CheckRenderedOcclusion(camera,check);
                LifePhotoSubjectCatalog catalog=Resources.Load<LifePhotoSubjectCatalog>("LifeContent/PhotoSubjects");
                check(catalog && catalog.treeMeshes.Length>0 && catalog.treeMeshes.All(m=>m),"Tree subjects bind original shared meshes rather than scenery instance names");
                LifeObject grass=life.World.GatheringSources.OrderBy(g=>Vector3.Distance(g.transform.position,actor.transform.position)).First();
                bool plantVisible=false;
                for(int i=0;i<8 && !plantVisible;i++)
                {
                    camera.transform.position=grass.transform.position+Quaternion.Euler(0,i*45,0)*new Vector3(0,1.1f,2.1f);
                    camera.transform.LookAt(grass.transform.position+Vector3.up*.2f);
                    LifePhotoSnapshot snap=LifePhotoCapture.Capture(life,camera);plantVisible=snap.entities.Any(e=>e.id==grass.State.id && e.bodyVisible);
                    if(plantVisible){check(LifePhotoRules.Match(snap,null).Contains("P03"),"A real registered plant from the map is a photographable grass source");SaveImage(camera,"registered-grass.png");}
                }
                check(plantVisible,"An existing registered plant can be photographed without a special quest prop");
                LifePhotoSnapshot copy=portrait.Copy();string original=portrait.entities[0].id;copy.entities[0].id="modified";
                check(portrait.entities[0].id==original,"Captured evidence is a deep value snapshot, independent of later mutations");
                CheckHomeSubject(life,camera,check);
                CheckLocalSun(life,camera,check);
                report.status="PASS";
            }
            catch(Exception error){report.status="FAIL";report.error=error.ToString();throw;}
            finally
            {
                if(blocker)UnityEngine.Object.DestroyImmediate(blocker);UnityEngine.Object.DestroyImmediate(camera.gameObject);
                File.WriteAllText(Folder+"/world-checks.json",JsonUtility.ToJson(report,true));
            }
        }
        static void CheckRenderedOcclusion(Camera camera,Action<bool,string> check)
        {
            GameObject subject=null,blocker=null;Material solid=null,screen=null;Texture2D cutout=null;
            try
            {
                Vector3 center=new Vector3(0,45,0);
                subject=Blocker(center,Quaternion.Euler(0,27,0),new Vector3(.45f,.65f,.35f));
                UnityEngine.Object.DestroyImmediate(subject.GetComponent<Collider>());
                solid=new Material(Shader.Find("Universal Render Pipeline/Lit"));solid.SetColor("_BaseColor",Color.white);
                subject.GetComponent<Renderer>().sharedMaterial=solid;
                camera.transform.position=center+Vector3.back*3;camera.transform.LookAt(center);
                using(LifePhotoDepth.Begin(camera))check(LifePhotoVisibility.Point(camera,center,subject.transform),
                    "The interior of a slanted opaque subject is visible by its own rendered surface, not by a forgiving bone depth tolerance");
                camera.orthographic=true;camera.orthographicSize=1;
                using(LifePhotoDepth.Begin(camera))check(LifePhotoVisibility.Point(camera,center,subject.transform),"The same actual surface remains visible with orthographic depth");
                camera.orthographic=false;
                blocker=Blocker(center+Vector3.back*1.4f,Quaternion.identity,new Vector3(1.6f,1.6f,.025f));
                UnityEngine.Object.DestroyImmediate(blocker.GetComponent<Collider>());
                screen=new Material(solid);blocker.GetComponent<Renderer>().sharedMaterial=screen;
                using(LifePhotoDepth.Begin(camera))check(!LifePhotoVisibility.Point(camera,center,subject.transform),
                    "An actual opaque foreground mesh without any Collider occludes the subject");
                SaveImage(camera,"depth-opaque-no-collider.png");
                using(LifePhotoDepth.Begin(camera))check(LifePhotoVisibility.Point(camera,center,subject.transform,blocker.transform),
                    "A declared visible activity contact surface can cover its own contact point");
                GameObject foreground=Blocker(center+Vector3.back*2,Quaternion.identity,new Vector3(.35f,.35f,.03f));
                try
                {
                    using(LifePhotoDepth.Begin(camera))check(!LifePhotoVisibility.Point(camera,center,subject.transform,blocker.transform),
                        "Unrelated opaque foreground still hides the animal and its declared activity contact");
                }
                finally{UnityEngine.Object.DestroyImmediate(foreground);}
                cutout=new Texture2D(32,32,TextureFormat.RGBA32,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                var texels=new Color[32*32];for(int y=0;y<32;y++)for(int x=0;x<32;x++)texels[y*32+x]=
                    x>=7 && x<25 && y>=7 && y<25?Color.clear:Color.white;
                cutout.SetPixels(texels);cutout.Apply();screen.SetTexture("_BaseMap",cutout);
                screen.SetFloat("_AlphaClip",1);screen.SetFloat("_Cutoff",.5f);screen.EnableKeyword("_ALPHATEST_ON");screen.renderQueue=2450;
                using(LifePhotoDepth.Begin(camera))check(LifePhotoVisibility.Point(camera,center,subject.transform),
                    "The real alpha-cutout opening transmits visibility instead of treating the whole mesh bounds as opaque");
                SaveImage(camera,"depth-cutout-hole.png");
                screen.SetTextureOffset("_BaseMap",new Vector2(.4f,0));
                using(LifePhotoDepth.Begin(camera))check(!LifePhotoVisibility.Point(camera,center,subject.transform),
                    "An opaque part of that same cutout material still blocks visibility");
                screen.SetTexture("_BaseMap",Texture2D.whiteTexture);screen.SetTextureOffset("_BaseMap",Vector2.zero);
                screen.SetFloat("_AlphaClip",0);screen.DisableKeyword("_ALPHATEST_ON");screen.SetFloat("_Surface",1);
                screen.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");screen.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                screen.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);screen.SetFloat("_ZWrite",0);
                screen.SetColor("_BaseColor",new Color(.4f,.7f,1,.15f));screen.renderQueue=3000;
                screen.SetShaderPassEnabled("DepthOnly",false);screen.SetShaderPassEnabled("DepthNormals",false);
                using(LifePhotoDepth.Begin(camera))check(LifePhotoVisibility.Point(camera,center,subject.transform),
                    "A visible transparent non-depth-writing surface does not falsely hide the subject");
                SaveImage(camera,"depth-transparent.png");
                check(!LifePhotoDepth.ActiveFor(camera),"Shutter query resources and current visibility scope are released after each capture");
            }
            finally
            {
                camera.orthographic=false;
                if(blocker)UnityEngine.Object.DestroyImmediate(blocker);if(subject)UnityEngine.Object.DestroyImmediate(subject);
                if(solid)UnityEngine.Object.DestroyImmediate(solid);if(screen)UnityEngine.Object.DestroyImmediate(screen);if(cutout)UnityEngine.Object.DestroyImmediate(cutout);
            }
        }
        static void CheckHomeSubject(LifeSession life,Camera camera,Action<bool,string> check)
        {
            const string id="squirrel.nest";
            var binding=(Dictionary<string,Renderer[]>)typeof(LifeWorld).GetField("shelterVisuals",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(life.World);
            check(binding.TryGetValue(id,out Renderer[] roof) && roof.Any(r=>r),"The real home has explicitly bound shelter renderers");
            Vector3 at=life.World.AnchorPosition(id);GameObject grass=null;
            Renderer[] subject=roof.Concat(life.World.Objects.Where(o=>o && o.State.kind==LifeObjectKind.Mat && o.State.slot==id).
                SelectMany(o=>o.GetComponentsInChildren<Renderer>())).Distinct().ToArray();
            bool[] original=subject.Select(r=>r.enabled).ToArray();
            try
            {
                bool seen=false;
                for(int i=0;i<8 && !seen;i++)
                {
                    camera.transform.position=at+Quaternion.Euler(0,i*45,0)*new Vector3(0,1.5f,3.4f);camera.transform.LookAt(at+Vector3.up*.65f);
                    seen=LifePhotoCapture.Capture(life,camera).entities.Any(e=>e.id==id && e.kind=="home");
                }
                check(seen,"Photographing the actual bound shelter supplies visible home evidence");SaveImage(camera,"actual-home.png");
                foreach(Renderer renderer in subject)renderer.enabled=false;
                grass=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("LifeAssets/GrassBundle"),at,Quaternion.identity,life.World.transform);
                camera.transform.position=at+new Vector3(0,1.2f,2);camera.transform.LookAt(at+Vector3.up*.1f);Physics.SyncTransforms();
                check(grass.GetComponentsInChildren<Renderer>().Any(r=>LifePhotoVisibility.RendererVisible(camera,r)),"The negative home fixture still shows a nearby real grass bundle");
                check(!LifePhotoCapture.Capture(life,camera).entities.Any(e=>e.id==id && e.kind=="home"),"A visible nearby bundle cannot substitute for the hidden home roof or placed mat");
                SaveImage(camera,"nearby-grass-is-not-home.png");
            }
            finally
            {
                for(int i=0;i<subject.Length;i++)if(subject[i])subject[i].enabled=original[i];
                if(grass)UnityEngine.Object.DestroyImmediate(grass);Physics.SyncTransforms();
            }
        }
        public static void CheckSharedRest(LifeSession life,AnimalActor first,AnimalActor second,Action<bool,string> check)
        {
            using var view=new PlayerPhotoView(life);Camera camera=view.camera;
            try
            {
                Vector3 middle=(first.transform.position+second.transform.position)*.5f;LifePhotoSnapshot snapshot=null,best=null;
                Vector3 bestPosition=default;Quaternion bestRotation=Quaternion.identity;int bestScore=-1;
                var views=new List<string>();bool found=false;int candidate=0;
                foreach(Vector2 pose in SharedRestViews(life))
                {
                    int i=candidate++;
                    if(!view.TryPose(middle,pose.x,pose.y,middle+Vector3.up*.25f))
                    {views.Add("view="+i+" radius="+pose.x+" angle="+pose.y+" blocked player capsule");continue;}
                    snapshot=LifePhotoCapture.Capture(life,camera);
                    string[] matches=LifePhotoRules.Match(snapshot,null);
                    int score=snapshot.entities.Count(e=>(e.id==first.animalId || e.id==second.animalId) && e.bodyVisible)*3+
                        snapshot.entities.Count(e=>(e.id==first.animalId || e.id==second.animalId) && e.activityKind=="rest");
                    if(score>bestScore){bestScore=score;best=snapshot;bestPosition=camera.transform.position;bestRotation=camera.transform.rotation;}
                    using(LifePhotoDepth.Begin(camera))views.Add("view="+i+" radius="+pose.x+" angle="+pose.y+" camera="+camera.transform.position.ToString("F3")+" eyeHeight="+view.eyeHeight+
                        " matches="+string.Join(",",matches)+"; "+BodyDiagnostic(camera,first)+"; "+BodyDiagnostic(camera,second));
                    if(HasAnimal(snapshot,first.animalId) && HasAnimal(snapshot,second.animalId) && matches.Contains("P22")){found=true;break;}
                }
                Directory.CreateDirectory(Folder);File.WriteAllLines(Folder+"/shared-rest-views.txt",views);
                if(!found)
                {camera.transform.SetPositionAndRotation(bestPosition,bestRotation);SaveImage(camera,"shared-rest-unmatched.png");WriteSnapshot("shared-rest-unmatched.json",best);}
                check(HasAnimal(snapshot,first.animalId) && HasAnimal(snapshot,second.animalId) && LifePhotoRules.Match(snapshot,null).Contains("P09") && LifePhotoRules.Match(snapshot,null).Contains("P22"),
                    "The real sleeping pair produces sleep and shared-activity photo evidence in one frame");
                SaveImage(camera,"actual-shared-rest.png");WriteSnapshot("actual-shared-rest.json",snapshot);
            }
            finally{}
        }
        static IEnumerable<Vector2> SharedRestViews(LifeSession life)
        {
            // Start at the actual roof's open side, then try nearby normal standing distances.
            float opening=0;
            var shelters=(Dictionary<string,Renderer[]>)typeof(LifeWorld).GetField("shelterVisuals",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(life.World);
            if(shelters.TryGetValue("squirrel.common",out Renderer[] parts) && parts.Length>0 && parts[0])
            {
                Transform roof=parts[0].transform;
                while(roof.parent && roof.parent!=life.World.transform)roof=roof.parent;
                opening=roof.eulerAngles.y;
            }
            foreach(float radius in new[]{2.2f,3.2f,4.5f})
            {
                yield return new Vector2(radius,opening);
                for(int i=1;i<12;i++)
                {yield return new Vector2(radius,opening-i*15);yield return new Vector2(radius,opening+i*15);}
                yield return new Vector2(radius,opening+180);
            }
        }
        static string BodyDiagnostic(Camera camera,AnimalActor animal)
        {
            Transform[] bones=animal.GetComponentsInChildren<Transform>();
            Transform head=bones.FirstOrDefault(t=>t.name=="Head" || t.name=="head");
            Transform torso=bones.FirstOrDefault(t=>t.name=="Spine" || t.name=="Spine1" || t.name=="Pelvis" || t.name=="Hips");
            bool body=LifePhotoVisibility.Body(camera,animal.transform,animal.VisualRoot.GetComponentsInChildren<Renderer>());
            string result=animal.animalId+" body="+body+" near="+camera.nearClipPlane.ToString("R")+" far="+camera.farClipPlane.ToString("R");
            if(head && torso)
            {
                Vector3 mid=Vector3.Lerp(torso.position,head.position,.55f),torsoViewport=camera.WorldToViewportPoint(torso.position),midViewport=camera.WorldToViewportPoint(mid);
                bool torsoInFrame=torsoViewport.z>camera.nearClipPlane && torsoViewport.z<=camera.farClipPlane && torsoViewport.x>=0 && torsoViewport.x<=1 && torsoViewport.y>=0 && torsoViewport.y<=1;
                bool midInFrame=midViewport.z>camera.nearClipPlane && midViewport.z<=camera.farClipPlane && midViewport.x>=0 && midViewport.x<=1 && midViewport.y>=0 && midViewport.y<=1;
                result+=" torso="+LifePhotoVisibility.Point(camera,torso.position,animal.transform)+" ["+(torsoInFrame?LifePhotoDepth.LastPointDiagnostic:"outside near/far/viewport")+"]";
                result+=" mid="+LifePhotoVisibility.Point(camera,mid,animal.transform)+" ["+(midInFrame?LifePhotoDepth.LastPointDiagnostic:"outside near/far/viewport")+"]";
                result+=" torsoViewport="+torsoViewport.ToString("F6")+" midViewport="+midViewport.ToString("F6")+
                    " torsoWorld="+torso.position.ToString("F6")+" headWorld="+head.position.ToString("F6")+
                    " headTorsoViewportDistance="+Vector3.Distance(torsoViewport,camera.WorldToViewportPoint(head.position)).ToString("R");
            }
            return result;
        }
        public static void CheckEating(LifeSession life,AnimalActor animal,Action<bool,string> check)
        {
            using var view=new PlayerPhotoView(life);Camera camera=view.camera;GameObject blocker=null;
            try
            {
                LifePhotoSnapshot snapshot=null,best=null;bool found=false;int bestScore=-1;
                Vector3 bestPosition=default;Quaternion bestRotation=Quaternion.identity;var views=new List<string>();
                object meal=typeof(LifeWorld).GetField("meal",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(life.World);
                Transform head=(Transform)meal.GetType().GetField("head").GetValue(meal);
                Vector3 mouth=head.TransformPoint((Vector3)meal.GetType().GetField("mouthLocal").GetValue(meal));
                LifeObject food=(LifeObject)meal.GetType().GetField("grass").GetValue(meal);
                for(int i=0;i<12;i++)
                {
                    if(!view.TryPose(animal.transform.position,2.4f,i*30,animal.transform.position+Vector3.up*.45f))
                    {views.Add("view="+i+" blocked player capsule");continue;}
                    snapshot=LifePhotoCapture.Capture(life,camera);
                    LifePhotoEntity entity=snapshot.entities.FirstOrDefault(e=>e.id==animal.animalId);
                    int score=(entity?.bodyVisible==true?3:0)+(entity?.mouthVisible==true?2:0)+(entity?.foodVisible==true?2:0);
                    if(score>bestScore){bestScore=score;best=snapshot;bestPosition=camera.transform.position;bestRotation=camera.transform.rotation;}
                    using(LifePhotoDepth.Begin(camera))
                    {
                        string line="view="+i+" camera="+camera.transform.position.ToString("F3")+" mouth="+mouth.ToString("F3")+
                            " entity="+(entity!=null)+" eating="+(entity?.eating==true)+" mouthFlag="+(entity?.mouthVisible==true)+" foodFlag="+(entity?.foodVisible==true)+
                            "; "+BodyDiagnostic(camera,animal);
                        line+="; actualMouth="+LifePhotoVisibility.Point(camera,mouth,animal.transform,food.transform)+" ["+LifePhotoDepth.LastPointDiagnostic+"]";
                        foreach(Renderer renderer in food.GetComponentsInChildren<Renderer>())if(!(renderer is ParticleSystemRenderer))
                            line+="; food="+renderer.name+" bounds="+renderer.bounds.ToString("F3")+" visible="+LifePhotoVisibility.RendererVisible(camera,renderer);
                        views.Add(line);
                    }
                    if(LifePhotoRules.Match(snapshot,null).Contains("P13")) {found=true;break;}
                }
                File.WriteAllLines(Folder+"/eating-views.txt",views);
                if(!found){camera.transform.SetPositionAndRotation(bestPosition,bestRotation);SaveImage(camera,"eating-unmatched.png");WriteSnapshot("eating-unmatched.json",best);}
                check(found && LifePhotoRules.Match(snapshot,null).Contains("P13"),"Real forage playback, visible mouth and the consumed grass prop jointly satisfy the eating photo");
                SaveImage(camera,"actual-eating.png");WriteSnapshot("actual-eating.json",snapshot);
                blocker=Blocker(Vector3.Lerp(camera.transform.position,mouth,.88f),camera.transform.rotation,new Vector3(.16f,.16f,.08f));Physics.SyncTransforms();
                LifePhotoSnapshot blocked=LifePhotoCapture.Capture(life,camera);
                check(!LifePhotoRules.Match(blocked,null).Contains("P13"),"A mouth-level opaque obstacle rejects eating even while the actual forage state continues");
                SaveImage(camera,"occluded-eating.png");WriteSnapshot("occluded-eating.json",blocked);
            }
            finally{if(blocker)UnityEngine.Object.DestroyImmediate(blocker);Physics.SyncTransforms();}
        }
        // One real player viewpoint per animation frame. Results from different frames never combine.
        public sealed class SharedDrinkingScan : IDisposable
        {
            readonly LifeSession life;
            readonly AnimalActor first,second;
            readonly PlayerPhotoView view;
            readonly List<Vector2> poses=new List<Vector2>();
            readonly List<string> views=new List<string>();
            int nextPose,lastFrame=-1,bestScore=-1;
            bool disposed;
            public bool Found {get;private set;}
            public SharedDrinkingScan(LifeSession life,AnimalActor first,AnimalActor second)
            {
                this.life=life;this.first=first;this.second=second;view=new PlayerPhotoView(life);
                // PhotoCameraRig.Zoom exposes a real 24-100mm lens; use its widest legal lens here.
                view.camera.focalLength=24f;
                foreach(float angle in new[]{0f,30f,150f,180f,210f,330f})poses.Add(new Vector2(.9f,angle));
                foreach(float angle in new[]{0f,30f,150f,180f,210f,330f})
                    foreach(float radius in new[]{1.4f,1.5f})poses.Add(new Vector2(radius,angle));
                foreach(float radius in new[]{.9f,1.2f,1.8f,2.6f})
                    for(int i=0;i<12;i++)
                    {Vector2 pose=new Vector2(radius,i*30);if(!poses.Contains(pose))poses.Add(pose);}
                Directory.CreateDirectory(Folder);
            }
            public bool TryCapture()
            {
                if(disposed)throw new ObjectDisposedException(nameof(SharedDrinkingScan));
                if(Found || lastFrame==Time.frameCount)return Found;
                lastFrame=Time.frameCount;
                var mouthQueries=new List<(AnimalActor animal,Transform bone,LifeObject bowl,float age)>();
                var activeDrinks=(System.Collections.IEnumerable)typeof(LifeWorld).GetField("drinks",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(life.World);
                var contact=typeof(LifeWorld).GetMethod("MouthTouchesWater",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
                foreach(object activeDrink in activeDrinks)
                {
                    Type type=activeDrink.GetType();object binding=type.GetField("animal").GetValue(activeDrink);
                    var speaker=(LifeSpeaker)binding.GetType().GetField("speaker").GetValue(binding);
                    if(speaker.Actor!=first && speaker.Actor!=second)continue;
                    if(!(bool)contact.Invoke(null,new[]{activeDrink}))return false;
                    mouthQueries.Add((speaker.Actor,(Transform)type.GetField("mouthBone").GetValue(activeDrink),
                        (LifeObject)type.GetField("bowl").GetValue(activeDrink),Time.time-(float)type.GetField("started").GetValue(activeDrink)));
                }
                if(first==second || mouthQueries.Count!=2 || mouthQueries.Select(q=>q.animal).Distinct().Count()!=2 ||
                    mouthQueries[0].bowl!=mouthQueries[1].bowl)return false;
                Vector2 pose=poses[nextPose++%poses.Count];Camera camera=view.camera;
                Vector3 middle=(first.transform.position+second.transform.position)*.5f;
                string line="frame="+Time.frameCount+" time="+Time.time.ToString("R")+" radius="+pose.x+" angle="+pose.y+" focalLength="+camera.focalLength.ToString("R");
                if(!view.TryPose(middle,pose.x,pose.y,middle+Vector3.up*.12f))
                {views.Add(line+" blocked player capsule");WriteViews();return false;}
                LifePhotoSnapshot snapshot=LifePhotoCapture.Capture(life,camera);
                line+=" camera="+camera.transform.position.ToString("F6")+"; "+
                    string.Join("; ",snapshot.entities.Where(e=>e.id==first.animalId || e.id==second.animalId || e.kind=="bowl")
                        .Select(e=>e.id+" body="+e.bodyVisible+" mouth="+e.mouthVisible+" activity="+e.activityKind+" water="+e.waterSurfaceVisible));
                Vector3 mouthLocal=(Vector3)typeof(LifeWorld).GetField("DrinkMouthLocal",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);
                using(LifePhotoDepth.Begin(camera))
                foreach(var query in mouthQueries)
                {
                    line+="; "+BodyDiagnostic(camera,query.animal);
                    Vector3 mouth=query.bone.TransformPoint(mouthLocal),viewport=camera.WorldToViewportPoint(mouth);
                    line+="; drink="+query.animal.animalId+" contact=True age="+query.age.ToString("R")+" mouth="+mouth.ToString("F6")+" viewport="+viewport.ToString("F6");
                    bool inFrame=viewport.z>camera.nearClipPlane && viewport.z<=camera.farClipPlane && viewport.x>=0 && viewport.x<=1 && viewport.y>=0 && viewport.y<=1;
                    bool animalVisible=LifePhotoVisibility.Point(camera,mouth,query.animal.transform);
                    line+=" animalPoint="+animalVisible+" ["+(inFrame?LifePhotoDepth.LastPointDiagnostic:"outside near/far/viewport")+"]";
                    bool waterVisible=LifePhotoVisibility.Point(camera,mouth,query.bowl.waterVisual);
                    line+=" waterPoint="+waterVisible+" ["+(inFrame?LifePhotoDepth.LastPointDiagnostic:"outside near/far/viewport")+"]";
                }
                views.Add(line);WriteViews();
                Found=HasAnimal(snapshot,first.animalId) && HasAnimal(snapshot,second.animalId) && LifePhotoRules.Match(snapshot,null).Contains("P22") &&
                    snapshot.entities.Count(e=>e.kind=="animal" && e.activityKind=="drink" && (e.id==first.animalId || e.id==second.animalId))==2;
                if(Found)
                {SaveImage(camera,"actual-shared-drinking.png");WriteSnapshot("actual-shared-drinking.json",snapshot);return true;}
                int score=snapshot.entities.Count(e=>e.kind=="animal" && e.bodyVisible && (e.id==first.animalId || e.id==second.animalId))*2+
                    snapshot.entities.Count(e=>e.kind=="animal" && e.activityKind=="drink" && (e.id==first.animalId || e.id==second.animalId))*3+
                    snapshot.entities.Count(e=>e.kind=="bowl" && e.waterSurfaceVisible);
                if(score>bestScore)
                {
                    bestScore=score;
                    // Save now: moving animals make a later rerender inconsistent with this snapshot.
                    SaveImage(camera,"shared-drinking-unmatched.png");WriteSnapshot("shared-drinking-unmatched.json",snapshot);
                }
                return false;
            }
            void WriteViews()=>File.WriteAllLines(Folder+"/shared-drinking-views.txt",views);
            public void Dispose()
            {
                if(disposed)return;disposed=true;
                try{WriteViews();}finally{view.Dispose();}
            }
        }
        // Camera placement is a declared fixture. PhotoRig supplies the actual player-visibility contract;
        // terrain and the real capsule reject viewpoints inside furniture, and no animal is repositioned.
        sealed class PlayerPhotoView : IDisposable
        {
            readonly PhotoCameraRig rig;readonly bool active;
            readonly Transform player;readonly CharacterController capsule;
            public readonly Camera camera;public readonly float eyeHeight;
            public PlayerPhotoView(LifeSession life)
            {
                rig=life.Gameplay.PhotoRig;active=rig && rig.Active;if(rig && !active)rig.SetActive(true);
                camera=NewCamera();if(rig && rig.CaptureCamera){camera.CopyFrom(rig.CaptureCamera);camera.enabled=false;camera.targetTexture=null;}
                camera.cullingMask&=~(1<<LayerMask.NameToLayer("UI"));
                player=life.Gameplay.PlayerTransform;capsule=player.GetComponent<CharacterController>();
                eyeHeight=life.Gameplay.forestSprite?Mathf.Max(.2f,life.Gameplay.forestSprite.cameraHeight):1.55f;
            }
            public bool TryPose(Vector3 middle,float radius,float angle,Vector3 look)
            {
                Vector3 point=middle+Quaternion.Euler(0,angle,0)*Vector3.forward*radius;
                Terrain ground=Terrain.activeTerrain;if(!ground)return false;
                point.y=ground.SampleHeight(point)+ground.transform.position.y;
                if(capsule)
                {
                    Vector3 offset=player.TransformVector(capsule.center);
                    float height=capsule.height*Mathf.Abs(player.lossyScale.y);
                    point.y-=offset.y-height*.5f;
                    float r=capsule.radius*Mathf.Max(Mathf.Abs(player.lossyScale.x),Mathf.Abs(player.lossyScale.z));
                    float half=Mathf.Max(0,height*.5f-r);Vector3 center=point+offset;
                    if(Physics.OverlapCapsule(center+Vector3.up*half,center-Vector3.up*half,r*.95f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)
                        .Any(c=>!(c is TerrainCollider) && !c.transform.IsChildOf(player)))return false;
                }
                camera.transform.position=point+Vector3.up*eyeHeight;camera.transform.LookAt(look);
                camera.transform.position+=camera.transform.forward*.2f;return true;
            }
            public void Dispose(){if(camera)UnityEngine.Object.DestroyImmediate(camera.gameObject);if(rig && !active)rig.SetActive(false);}
        }
        static void CheckLocalSun(LifeSession life,Camera camera,Action<bool,string> check)
        {
            Light global=RenderSettings.sun;bool enabled=global && global.enabled;
            GameObject ground=null,weather=null;
            try
            {
                if(global)global.enabled=false;
                Vector3 center=new Vector3(0,35,0);
                ground=Blocker(center,Quaternion.identity,new Vector3(40,.15f,40));ground.name="Local sun visible surface fixture";
                weather=new GameObject("Local sunshine photograph fixture");weather.SetActive(false);weather.transform.position=center;
                WeatherRegion region=weather.AddComponent<WeatherRegion>();region.radius=LifeSession.CastRadius;region.localPlayerWeather=true;weather.SetActive(true);
                region.SetLocalWeather(LocalWeather.Sun,1,true);
                Light spot=weather.GetComponentInChildren<Light>();spot.intensity=3;
                camera.transform.position=center+new Vector3(4,8,-14);camera.transform.LookAt(center);Physics.SyncTransforms();
                LifePhotoSnapshot lit=LifePhotoCapture.Capture(life,camera);
                check(spot.type==LightType.Spot && spot.shadows==LightShadows.None && LifePhotoRules.Match(lit,null).Contains("P07"),
                    "The production local sunshine Spot creates a same-frame lit/outside-cone surface contrast without the global sun");
                SaveImage(camera,"actual-local-sunlight.png");WriteSnapshot("actual-local-sunlight.json",lit);
                spot.intensity=0;
                check(!LifePhotoRules.Match(LifePhotoCapture.Capture(life,camera),null).Contains("P07"),"The same surface without actual light intensity does not earn sunlight");
            }
            finally
            {
                if(global)global.enabled=enabled;if(weather)UnityEngine.Object.DestroyImmediate(weather);if(ground)UnityEngine.Object.DestroyImmediate(ground);Physics.SyncTransforms();
            }
        }
        static bool FindView(LifeSession life,Camera camera,AnimalActor actor,out LifePhotoSnapshot snapshot)
        {
            snapshot=null;
            for(int i=0;i<12;i++)
            {
                camera.transform.position=actor.transform.position+Quaternion.Euler(0,i*30,0)*new Vector3(0,1.2f,2.7f);
                camera.transform.LookAt(actor.transform.position+Vector3.up*.4f);snapshot=LifePhotoCapture.Capture(life,camera);
                if(HasAnimal(snapshot,actor.animalId))return true;
            }
            return false;
        }
        static bool HasAnimal(LifePhotoSnapshot snapshot,string id)=>snapshot?.entities?.Any(e=>e.id==id && e.bodyVisible)==true;
        static Camera NewCamera()
        {
            var camera=new GameObject("Photo validation camera").AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=52;
            camera.nearClipPlane=.03f;camera.farClipPlane=150;camera.aspect=16f/9;camera.cullingMask=~(1<<LayerMask.NameToLayer("UI"));return camera;
        }
        static GameObject Blocker(Vector3 point,Quaternion rotation,Vector3 size)
        {GameObject block=GameObject.CreatePrimitive(PrimitiveType.Cube);block.name="Temporary opaque photo fixture";block.transform.SetPositionAndRotation(point,rotation);block.transform.localScale=size;return block;}
        static void WriteSnapshot(string file,LifePhotoSnapshot snapshot){Directory.CreateDirectory(Folder);File.WriteAllText(Folder+"/"+file,JsonUtility.ToJson(snapshot,true));}
        static void SaveImage(Camera camera,string name)
        {
            Directory.CreateDirectory(Folder);RenderTexture previous=RenderTexture.active;RenderTexture target=RenderTexture.GetTemporary(960,540,24);
            Texture2D texture=null;
            try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture=new Texture2D(960,540,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,960,540),0,0);texture.Apply();File.WriteAllBytes(Folder+"/"+name,texture.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);if(texture)UnityEngine.Object.DestroyImmediate(texture);}
        }
    }
}
