using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

namespace WeatherHouse.Life.Editor
{
    public static class AlbumPickupChecks
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Get<T>(object o,string f)=>(T)o.GetType().GetField(f,Private).GetValue(o);
        static void Set(object o,string f,object value)=>o.GetType().GetField(f,Private).SetValue(o,value);
        static void Call(object o,string f)=>o.GetType().GetMethod(f,Private).Invoke(o,null);
        static void Check(bool ok,string message) { if(!ok)throw new InvalidOperationException(message); }
        static IEnumerator Settled(GameplayHud hud)
        { var book=hud.GetComponent<LifeJournal3D>(); float due=Time.realtimeSinceStartup+20; do{yield return null;}while(book.IsAnimating&&Time.realtimeSinceStartup<due);Check(!book.IsAnimating,"Book animation timed out.");yield return null; }
        static void Shot(string name)=>ScreenCapture.CaptureScreenshot(Path.GetFullPath("PhotoStillReview/20261029/"+name+".png"));
        static void CheckLockedOnboarding(LifeSession life,GameplayHud hud)
        {
            // A focused isolated stage fixture; the full opening flow is a separate check.
            var tutorial=life.GetComponent<LifeOnboarding>();
            var state=tutorial.State;var oldView=Get<LifeOnboardingView>(tutorial,"view");
            var property=typeof(LifeOnboarding).GetProperty("State");
            var note=new GameObject("Task book onboarding fixture",typeof(RectTransform)).AddComponent<LifeOnboardingView>();note.Build(life.transform);
            try
            {
                Set(tutorial,"view",note);
                property.SetValue(tutorial,new LifeOnboardingState{started=true,stage=LifeOnboardingStage.FirstBook});
                Call(tutorial,"ShowStageNote");Check(tutorial.CurrentHint.Contains("三个书签"),"Locked first-book note must describe three available tabs.");
                tutorial.State.stage=LifeOnboardingStage.PhotoBook;tutorial.State.evidenceViewed=true;
                Call(tutorial,"ShowStageNote");
                Check(!tutorial.CurrentHint.Contains("挑战")&&Get<Text>(note,"actionLabel").text.Contains("去找小狗"),"Locked photo tutorial must not ask for unavailable challenge book.");
                Get<Button>(note,"action").onClick.Invoke();
                Check(tutorial.State.stage==LifeOnboardingStage.FindRainRequest,"Locked photo tutorial must continue to the next real stage.");
            }
            finally{property.SetValue(tutorial,state);Set(tutorial,"view",oldView);Object.Destroy(note.gameObject);}
        }
        public static IEnumerator Run()
        {
            Check(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")),"Use an isolated save.");
            var life=Object.FindObjectOfType<LifeSession>();var game=life.Gameplay;var hud=game.GetComponent<GameplayHud>();
            string progress=JsonUtility.ToJson(life.Progress),notebook=JsonUtility.ToJson(game.Notebook);
            var files=new List<string>();var folders=new List<string>();var instances=new List<GameObject>();
            string root=Path.GetFullPath(Path.Combine(GameplaySession.PersistenceDirectory,"MotionPhotos"));
            try
            {
                Check(!File.ReadAllText("Assets/Game/UI/GameplayHud.cs").Contains("KeyCode.Space"),"HUD must not bind Space to photography.");
                Check(!File.ReadAllText("Assets/Game/Life/UI/LifeGestureInput.cs").Contains("KeyCode.H"),"H guidance binding must be removed.");
                hud.SetCameraMode(true,true);yield return null;
                Check(!game.forestSprite.JumpBlocked&&!game.forestSprite.InputBlocked,"Photo mode must leave jumping available.");
                int before=game.Notebook.observations.Count;float ground=game.PlayerTransform.position.y,peak=ground;
                game.forestSprite.SetScriptedInput(Vector2.zero,false,true);float jumpDue=Time.realtimeSinceStartup+1.6f;
                while(Time.realtimeSinceStartup<jumpDue){peak=Mathf.Max(peak,game.PlayerTransform.position.y);yield return null;}
                game.forestSprite.ReleaseScriptedInput();
                Check(peak>ground+.15f&&game.Notebook.observations.Count==before,"Real photo-mode jump must rise without creating a photograph.");
                var shutter=Get<RectTransform>(hud,"cameraRoot").GetComponentsInChildren<Button>().Single(b=>b.name=="Shutter");
                EventSystem.current.SetSelectedGameObject(shutter.gameObject);
                ExecuteEvents.Execute(shutter.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);yield return null;
                Check(game.Notebook.observations.Count==before&&!game.Recorder.IsRecording,"UI Submit after focusing the shutter must not take a photo.");
                ExecuteEvents.Execute(shutter.gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
                while(game.Recorder.IsRecording)yield return null;yield return null;
                Check(game.Notebook.observations.Count==before+1&&game.Notebook.observations.Last().frameCount==1,"Only the shutter click must create a static photograph.");
                string captured=Path.GetFullPath(Path.Combine(GameplaySession.PersistenceDirectory,game.Notebook.observations.Last().clipFolder));folders.Add(captured);files.Add(Path.Combine(captured,"frame_000.png"));
                Check(string.IsNullOrEmpty(game.Status),"Successful photo should omit the deleted confirmation text.");
                hud.SetCameraMode(false,true);yield return null;
                life.Progress.photoTaskBookCollected=false;life.SaveAndNotify("");yield return null;
                Check(!Get<Transform>(hud,"controlsRoot").Find("Photo tasks icon").gameObject.activeSelf,"Locked HUD icon must be hidden.");
                Check(!Get<RectTransform>(life.GetComponent<LifeGestureInput>(),"root").Find("Optional guidance"),"Default G/H footer must be removed.");
                hud.UseBookShortcut(KeyCode.L);Check(!hud.JournalOpen,"Locked shortcut must not open tasks.");
                hud.ToggleJournal();yield return Settled(hud);
                for(int tab=0;tab<3;tab++)
                {
                    hud.OnboardingSelectBook(tab,null);yield return Settled(hud);
                    Check(!Get<RectTransform>(hud,"albumRoot").Find("Photography tasks tab"),"Task tab must be absent in each locked section.");
                }
                CheckLockedOnboarding(life,hud);
                Shot("album-locked");yield return new WaitForEndOfFrame();yield return null;hud.GetComponent<LifeJournal3D>().Cancel();Call(hud,"CompleteJournalClose");yield return null;
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PhotoTaskBookBuilder.PrefabPath);Check(prefab,"Pickup prefab missing.");
                var pickup=Object.Instantiate(prefab,game.PlayerTransform.position+game.PlayerTransform.forward*.65f,Quaternion.identity);instances.Add(pickup);yield return null;
                Check(hud.PrimaryInteraction==LifePrimaryAction.PickUpBook,"Nearby book should own the E action.");
                using(var locked=new FileStream(life.SavePath,FileMode.Open,FileAccess.Read,FileShare.None))
                { hud.InteractPrimary();Check(!life.Progress.photoTaskBookCollected&&pickup.activeSelf,"Failed save must retain book and locked ownership."); }
                hud.InteractPrimary();yield return null;
                Check(life.Progress.photoTaskBookCollected&&!pickup.activeSelf&&JsonUtility.FromJson<LifeProgress>(File.ReadAllText(life.SavePath)).photoTaskBookCollected,"Pickup must atomically persist and hide its model.");
                Check(Get<Transform>(hud,"controlsRoot").Find("Photo tasks icon").gameObject.activeSelf,"Pickup must reveal the HUD icon.");
                var reloaded=Object.Instantiate(prefab);instances.Add(reloaded);yield return null;yield return null;Check(!reloaded.activeSelf,"Persisted ownership must hide a reloaded pickup.");
                game.Notebook.observations.Clear();
                // Nine distinct existing-resolution photos cover both a full spread and its partial final spread.
                for(int i=0;i<9;i++)
                {
                    string id=Guid.NewGuid().ToString("N"),folder=Path.Combine(root,id);Directory.CreateDirectory(folder);folders.Add(folder);
                    var texture=new Texture2D(512,288,TextureFormat.RGB24,false);
                    texture.SetPixels(Enumerable.Repeat(Color.HSVToRGB(i/12f,.3f,.7f),512*288).ToArray());texture.Apply();
                    string file=Path.Combine(folder,"frame_000.png");File.WriteAllBytes(file,texture.EncodeToPNG());files.Add(file);Object.Destroy(texture);
                    game.Notebook.observations.Add(new ObservationRecord{id=id,clipFolder="MotionPhotos/"+id,frameCount=1,glyphIds=Array.Empty<string>()});
                }
                hud.ToggleJournal();yield return Settled(hud);hud.OnboardingSelectBook(1,null);yield return Settled(hud);
                var album=Get<RectTransform>(hud,"albumRoot");
                Check(album.GetComponentsInChildren<RawImage>().Count(x=>game.Notebook.observations.Any(r => x.name == "Photo " + r.id))==8,"Second tab requires eight photo slots.");
                Check(album.GetComponentsInChildren<LifeJournalWord>().Length==0,"All-photo spread must not keep the dictionary grid.");
                Check(album.GetComponentsInChildren<RawImage>().Where(x=>game.Notebook.observations.Any(r => x.name == "Photo " + r.id)).Count(x=>x.rectTransform.anchoredPosition.x>0)==4,"Four photos must occupy the right page.");
                Shot("album-eight");yield return new WaitForEndOfFrame();yield return null;
                var first=album.GetComponentsInChildren<Button>().First(x=>x.name=="Photo "+game.Notebook.observations[0].id);first.onClick.Invoke();yield return null;
                Check(Get<ObservationRecord>(hud,"expandedPhoto")!=null,"Photo click must open the static detail.");Call(hud,"HideDetails");yield return null;
                var drag=Get<Canvas>(hud,"canvas").GetComponent<LifeAlbumDrag>();var book=hud.GetComponent<LifeJournal3D>();
                foreach(var trial in new[]{new[]{-.25f,0f},new[]{-.7f,1f},new[]{.7f,0f}})
                {
                    album=Get<RectTransform>(hud,"albumRoot");Vector2 start=RectTransformUtility.WorldToScreenPoint(null,album.TransformPoint(new Vector3(260,0,0)));
                    var e=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,pressPosition=start,position=start,eligibleForClick=true};
                    drag.OnBeginDrag(e);e.position=start+Vector2.right*trial[0]*Mathf.Max(160,Screen.width*.35f);drag.OnDrag(e);yield return null;
                    Check(book.IsAnimating&&!e.eligibleForClick,"Dragging must drive the real page mesh and cancel the photo click.");
                    if(trial[0]==-.25f){Check(book.AnimationProgress>.3f&&book.AnimationProgress<.5f,"Page responds promptly to partial mouse movement.");Shot("album-drag");yield return new WaitForEndOfFrame();yield return null;}
                    if(trial[0]==-.7f)
                    {
                        var leaf=Get<Renderer>(book,"turningPage") as SkinnedMeshRenderer;var mesh=new Mesh();leaf.BakeMesh(mesh);
                        float height=mesh.vertices.Max(v=>leaf.transform.TransformPoint(v).y)-Get<Renderer>(book,"pageRight").bounds.center.y;Object.Destroy(mesh);
                        Check(leaf.enabled&&height>.04f,"Held drag must actually raise the skinned page, not just advance a timer.");
                        File.WriteAllText("PhotoStillReview/20261029/drag-mesh.txt","Held page height="+height+"m; progress="+book.AnimationProgress);
                        Shot("album-turn");yield return new WaitForEndOfFrame();yield return null;
                    }
                    drag.OnEndDrag(e);yield return Settled(hud);Check(Get<int>(hud,"page")==Mathf.RoundToInt(trial[1]),"Cancel/next/previous drag lands on the expected page.");
                    if(trial[1]==1){Check(Get<RectTransform>(hud,"albumRoot").GetComponentsInChildren<RawImage>().Count(x=>game.Notebook.observations.Any(r => x.name == "Photo " + r.id))==1,"Final spread must show the ninth photo once.");Shot("album-last");yield return new WaitForEndOfFrame();yield return null;}
                }
                for(int tab=0;tab<4;tab++)
                {
                    hud.OnboardingSelectBook(tab,null);yield return Settled(hud);
                    var symbol=Get<RectTransform>(hud,"albumRoot").Find("Photography tasks tab").GetComponent<LifeJournalTabGraphic>();
                    Check(symbol&&symbol.symbol==LifeJournalTabGraphic.Symbol.Tasks,"All four sections must share the task checklist symbol.");
                }
                Shot("task-book-unlocked");yield return new WaitForEndOfFrame();yield return null;
                File.WriteAllText("PhotoStillReview/20261029/album-pickup-checks.txt","PASS photo-mode real jump without capture; actual shutter callback creates one static photo; no Space/H bindings in HUD; removed success footer; locked sections and shortcut; locked onboarding offers three tabs and continues without challenge book; failed-save rollback; real pickup/persistence/reloaded visibility; 8 slots split 4+4; ninth photo on next spread; detail; partial drag cancellation; next/previous real mesh drag; task icon across four sections.\nInput boundary: runtime scripted jump and UI callback, not an OS keyboard/mouse injection.");
            }
            finally
            {
                if(hud.JournalOpen){hud.GetComponent<LifeJournal3D>().Cancel();Call(hud,"CompleteJournalClose");}
                hud.SetCameraMode(false,true);game.forestSprite.ReleaseScriptedInput();
                foreach(var instance in instances)if(instance)Object.Destroy(instance);
                JsonUtility.FromJsonOverwrite(progress,life.Progress);JsonUtility.FromJsonOverwrite(notebook,game.Notebook);life.SaveAndNotify("");
                foreach(string file in files)if(Path.GetFullPath(file).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&File.Exists(file))File.Delete(file);
                foreach(string folder in folders)if(Path.GetFullPath(folder).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&Directory.Exists(folder)&&!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);
            }
        }
    }
}
