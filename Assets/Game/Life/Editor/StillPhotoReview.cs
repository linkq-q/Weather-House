using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using WeatherHouse.StoneEngraving;
using WeatherHouse.StoneEngraving.Editor;
using WeatherHousePrototype;
using WeatherHouse.WatersideV2;
using Object=UnityEngine.Object;

namespace WeatherHouse.Life.Editor
{
    [InitializeOnLoad]
    public static class StillPhotoReview
    {
        const string Folder="PhotoStillReview/20261029";
        const string Key="WeatherHouse.StillPhotoReview";
        static double next;
        static StillPhotoReview(){EditorApplication.update+=Poll;EditorApplication.playModeStateChanged+=PlayChanged;}
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<next||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            next=EditorApplication.timeSinceStartup+.5;string path=Folder+"/command.txt";
            if(!File.Exists(path))return;string command=File.ReadAllText(path).Trim();File.Delete(path);
            try
            {
                if(command=="inspect")Inspect();
                else if(command=="toon-install")AnimalPaperInstall.Install();
                else if(command=="task-book")PhotoTaskBookBuilder.Build();
                else if(command=="photo-static")MotionPhotoChecks.RunStaticChecks();
                else if(command=="move-coast")MoveCoast();
                else if(command=="play-checks"||command=="audio-checks"||command=="shore-checks"||command=="entry-checks"||command=="album-checks"||command=="variants"||command=="toon-checks"||command=="photo-ui-checks"||command=="ui-checks")StartChecks(command);
                else throw new InvalidOperationException("Unknown still photo review command.");
                File.WriteAllText(Folder+"/status.txt","OK "+command+" "+DateTime.UtcNow.ToString("O"));
            }
            catch(Exception e){File.WriteAllText(Folder+"/status.txt","FAIL "+command+"\n"+e);Debug.LogException(e);}
        }
        static void StartChecks(string mode)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Another Play session is active.");
            SessionState.SetString(Key+".save",Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")??"");
            SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Key+".background",Application.runInBackground);SessionState.SetBool(Key+".owned",true);
            SessionState.SetString(Key+".mode",mode);
            SessionState.SetString(Key+".drawer",Environment.GetEnvironmentVariable(WeatherHouse.DrawerPrototype.DrawerPrototypeController.SaveOverride)??"");
            Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",Path.GetFullPath(Folder+"/isolated-save"));
            if(mode=="entry-checks")
            {
                string directory=Path.GetFullPath(Folder+"/entry-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(directory);
                Environment.SetEnvironmentVariable(WeatherHouse.DrawerPrototype.DrawerPrototypeController.SaveOverride,Path.Combine(directory,"drawer.json"));
                Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",directory);
            }
            EditorSceneManager.playModeStartScene=mode=="entry-checks"?AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Experiments/DrawerPrototype/DrawerPrototype.unity"):null;
            Application.runInBackground=true;EditorApplication.isPlaying=true;
        }
        static void PlayChanged(PlayModeStateChange state)
        {
            if(!SessionState.GetBool(Key+".owned",false))return;
            if(state==PlayModeStateChange.EnteredPlayMode)
            {
                MonoBehaviour host=SessionState.GetString(Key+".mode","")=="entry-checks"?(MonoBehaviour)Object.FindObjectOfType<GameMusic>():Object.FindObjectOfType<GameplaySession>();
                host.StartCoroutine(Guard(Checks()));
            }
            if(state!=PlayModeStateChange.EnteredEditMode)return;
            string save=SessionState.GetString(Key+".save","");Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",string.IsNullOrEmpty(save)?null:save);
            string drawer=SessionState.GetString(Key+".drawer","");Environment.SetEnvironmentVariable(WeatherHouse.DrawerPrototype.DrawerPrototypeController.SaveOverride,string.IsNullOrEmpty(drawer)?null:drawer);
            string start=SessionState.GetString(Key+".start","");EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(start)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
            Application.runInBackground=SessionState.GetBool(Key+".background",false);SessionState.SetBool(Key+".owned",false);
            File.WriteAllText(Folder+"/handoff.txt","Play=False; Dirty="+SceneManager.GetActiveScene().isDirty+"; save override and player/cameras restored. "+DateTime.UtcNow.ToString("O"));
        }
        static IEnumerator Guard(IEnumerator routine)
        {
            var stack=new Stack<IEnumerator>();stack.Push(routine);string error=null;
            while(stack.Count>0)
            {
                object value=null;bool more=false;
                try{more=stack.Peek().MoveNext();if(more)value=stack.Peek().Current;}
                catch(Exception e){error=e.ToString();break;}
                if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}
                if(value is IEnumerator nested){stack.Push(nested);continue;}
                yield return value;
            }
            while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();
            File.WriteAllText(Folder+"/play-result.txt",error==null?"PASS "+SessionState.GetString(Key+".mode",""):"FAIL\n"+error);
            EditorApplication.isPlaying=false;
        }
        static IEnumerator Checks()
        {
            yield return new WaitForSecondsRealtime(3);
            if(SessionState.GetString(Key+".mode","")=="audio-checks"){yield return AudioChecks();yield break;}
            if(SessionState.GetString(Key+".mode","")=="variants"){yield return StillPhotoVariants.Run();yield break;}
            if(SessionState.GetString(Key+".mode","")=="shore-checks"){yield return ShoreVoiceChecks.Run();yield break;}
            if(SessionState.GetString(Key+".mode","")=="entry-checks"){yield return EntryFlowChecks.Run();yield break;}
            if(SessionState.GetString(Key+".mode","")=="toon-checks"){yield return AnimalPaperWorldChecks.Run();yield break;}
            if(SessionState.GetString(Key+".mode","")=="album-checks"){yield return AlbumPickupChecks.Run();yield break;}
            if(SessionState.GetString(Key+".mode","")=="ui-checks")
            {string report=null;yield return LifeUiPlayChecks.Run(Object.FindObjectOfType<LifeSession>(),result=>report=result);File.WriteAllText(Folder+"/ui-lifecycle.txt",report??"No result");if(report==null||!report.StartsWith("PASS"))throw new InvalidOperationException(report);yield break;}
            if(SessionState.GetString(Key+".mode","")!="photo-ui-checks")
            {
            yield return AudioChecks();
            yield return MotionPhotoChecks.RunPlayChecks();
            yield return LifePhotoCaptureChecks.RunPlayChecks();
            }
            string ui=null;yield return LifePhotoUiChecks.Run(Object.FindObjectOfType<LifeSession>(),result=>ui=result);
            if(ui==null||!ui.StartsWith("PASS",StringComparison.Ordinal))throw new InvalidOperationException(ui??"UI checks did not finish.");
        }
        static IEnumerator AudioChecks()
        {
            var game=Object.FindObjectOfType<GameplaySession>();var hud=game.GetComponent<GameplayHud>();var audio=game.GetComponent<LifeAudio>();
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            bool focus=(bool)typeof(LifeAudio).GetField("focused",flags).GetValue(audio);
            typeof(LifeAudio).GetMethod("OnApplicationFocus",flags).Invoke(audio,new object[]{true});
            var sources=(AudioSource[])typeof(LifeAudio).GetField("sources",flags).GetValue(audio);var steps=sources[3];
            var report=new StringBuilder();
            try
            {
                foreach(bool photo in new[]{false,true})
                {
                    hud.SetCameraMode(photo,true);LifeAudio.Play(LifeAudioCue.GrassStep);
                    yield return new WaitForSecondsRealtime(.04f);
                    // The runtime photo camera has DontSave, which FindObjectsOfType excludes.
                    var listeners=Resources.FindObjectsOfTypeAll<AudioListener>().Where(x=>x.gameObject.scene.IsValid()&&x.isActiveAndEnabled).ToArray();
                    report.AppendLine("Active listeners in "+(photo?"photo":"gameplay")+": "+string.Join(", ",listeners.Select(x=>x.name)));
                    File.WriteAllText(Folder+"/footstep-checks.txt",report.ToString());
                    if(listeners.Length!=1)throw new InvalidOperationException("Expected one active audio listener; found "+listeners.Length);
                    float distance=Vector3.Distance(listeners[0].transform.position,steps.transform.position);
                    report.AppendLine($"{(photo?"photo":"gameplay")}: listener={listeners[0].name}, distance={distance:F2}m, maxDistance={steps.maxDistance}, spatialBlend={steps.spatialBlend}, volume={steps.volume}, playing={steps.isPlaying}");
                    File.WriteAllText(Folder+"/footstep-checks.txt",report.ToString());
                    if(steps.spatialBlend!=0)throw new InvalidOperationException("Player footsteps must remain audible independently of camera distance.");
                    int count=audio.FootstepCount;var start=game.PlayerTransform.position;
                    game.forestSprite.SetScriptedInput(Vector2.up,false,false);
                    yield return new WaitForSecondsRealtime(1.1f);
                    game.forestSprite.SetScriptedInput(Vector2.zero,false,false);
                    if(audio.FootstepCount<=count||Vector3.Distance(start,game.PlayerTransform.position)<.15f)throw new InvalidOperationException("Actual walking did not produce footsteps in "+(photo?"photo":"gameplay")+" mode.");
                    report.AppendLine("PASS actual walking emits "+(audio.FootstepCount-count)+" unattenuated player footsteps.");
                }
                if(sources[4].spatialBlend!=1||sources[11].spatialBlend!=0)throw new InvalidOperationException("World sources should be spatial; shore ambience uses player-distance gain.");
                File.WriteAllText(Folder+"/footstep-checks.txt",report+"PASS world sources remain spatial; shore ambience uses player-distance gain.");
            }
            finally{hud.SetCameraMode(false,true);game.forestSprite.ReleaseScriptedInput();typeof(LifeAudio).GetMethod("OnApplicationFocus",flags).Invoke(audio,new object[]{focus});}
        }
        static void MoveCoast()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!="Assets/Scenes/WeatherHouse_WatersideV2.unity")throw new InvalidOperationException("Move requires the current Waterside Edit scene.");
            var rocks=Object.FindObjectsOfType<MeshRenderer>().Where(r=>r.name=="Rock_1"&&r.transform.parent&&r.transform.parent.name=="断续岩块与草帽岸线"&&
                r.bounds.center.x>56&&r.bounds.center.x<64&&r.bounds.center.z> -31&&r.bounds.center.z< -12).OrderBy(r=>r.bounds.center.z).ToArray();
            if(rocks.Length!=7)throw new InvalidOperationException("Expected the seven rocks in the lower marked row, found "+rocks.Length);
            if(File.Exists(Folder+"/before-move.unity"))throw new InvalidOperationException("Move already has a backup; inspect before running again.");
            if(!EditorSceneManager.SaveScene(scene,Folder+"/before-move.unity",true))throw new IOException("Could not preserve the current unsaved scene snapshot.");
            var terrain=Object.FindObjectsOfType<Terrain>().First(t=>t.gameObject.scene==scene);
            var log=new StringBuilder();var engravings=Object.FindObjectsOfType<StoneEngravingReadability>().ToDictionary(s=>s,s=>s.transform.localToWorldMatrix);
            Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();
            try
            {
                foreach(var rock in rocks)
                {
                    var before=rock.transform.position;var center=rock.bounds.center;
                    float oldGround=WatersideLayout.GroundHeight(terrain,center.x,center.z);
                    var next=center+new Vector3(5.5f,0,42f);
                    if(WatersideLayout.IsWater(next.x,next.z))throw new InvalidOperationException("Destination lies in water.");
                    float newGround=WatersideLayout.GroundHeight(terrain,next.x,next.z);
                    Undo.RecordObject(rock.transform,"移动海岸排石");rock.transform.position+=new Vector3(5.5f,newGround-oldGround,42f);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(rock.transform);
                    log.AppendLine(GlobalObjectId.GetGlobalObjectIdSlow(rock.transform)+" "+before.ToString("F4")+" -> "+rock.transform.position.ToString("F4"));
                }
                foreach(var pair in engravings)if(pair.Key.transform.localToWorldMatrix!=pair.Value)throw new InvalidOperationException("An engraving pose changed.");
                Physics.SyncTransforms();EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save requested placement.");
                File.WriteAllText(Folder+"/move-checks.txt",log+"\nPASS seven originals moved; rotations/scales retained; all eight engraving poses unchanged; full pre-move snapshot retained.");
                Undo.CollapseUndoOperations(undo);Inspect();
                File.Copy(Folder+"/coast-before.png",Folder+"/coast-after.png",true);
            }
            catch{Undo.RevertAllDownToGroup(undo);throw;}
        }
        static void Inspect()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Inspect in Edit mode.");
            var text=new StringBuilder("scene="+SceneManager.GetActiveScene().path+" dirty="+SceneManager.GetActiveScene().isDirty+"\n");
            foreach(var stone in Object.FindObjectsOfType<StoneEngravingReadability>())
                text.AppendLine("ENGRAVED "+stone.name+" id="+stone.GetInstanceID()+" position="+stone.transform.position+" rotation="+stone.transform.eulerAngles+" scale="+stone.transform.localScale+" bounds="+stone.GetComponent<Renderer>().bounds);
            foreach(var r in Object.FindObjectsOfType<MeshRenderer>().Where(r=>r.bounds.center.x>43&&r.bounds.center.x<70&&r.bounds.center.z> -50&&r.bounds.center.z<32&&
                (r.name.ToLowerInvariant().Contains("stone")||r.name.ToLowerInvariant().Contains("rock")||r.name.Contains("石"))))
                text.AppendLine("COAST "+r.name+" id="+r.gameObject.GetInstanceID()+" bounds="+r.bounds+" parent="+r.transform.parent?.name);
            File.WriteAllText(Folder+"/inspection.txt",text.ToString());
            var terrain=Object.FindObjectsOfType<Terrain>().First(t=>t.gameObject.scene==SceneManager.GetActiveScene());
            var grid=new StringBuilder();
            for(int z=8;z<=40;z+=4)for(int x=56;x<=66;x+=2)
                grid.AppendLine($"{x} {z} ground={WatersideLayout.GroundHeight(terrain,x,z):F2} water={WatersideLayout.IsWater(x,z)}");
            File.WriteAllText(Folder+"/destination-ground.txt",grid.ToString());
            var go=new GameObject("Coast inspection camera"){hideFlags=HideFlags.HideAndDontSave};
            try
            {
                var camera=go.AddComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.Skybox;
                camera.orthographic=true;camera.orthographicSize=55;camera.nearClipPlane=.1f;camera.farClipPlane=220;
                camera.transform.position=new Vector3(58,100,-25);camera.transform.LookAt(new Vector3(58,0,10));
                go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
                StoneEngravingPlacementReview.Capture(camera,Folder+"/coast-before.png",1000,1100);
                camera.orthographicSize=14;camera.transform.position=new Vector3(65,45,5);camera.transform.LookAt(new Vector3(65,0,20));
                StoneEngravingPlacementReview.Capture(camera,Folder+"/coast-close.png",1000,1100);
                Physics.SyncTransforms();var hits=new StringBuilder();
                foreach(var r in Object.FindObjectsOfType<MeshRenderer>().Where(r=>r.name=="Rock_1"&&r.transform.parent&&r.transform.parent.name=="断续岩块与草帽岸线"&&r.transform.position.z>10&&r.transform.position.z<31))
                {
                    var p=r.bounds.center;hits.AppendLine("ROCK "+r.transform.position+" bounds="+r.bounds+" terrain="+WatersideLayout.GroundHeight(terrain,p.x,p.z));
                    foreach(var hit in Physics.RaycastAll(new Vector3(p.x,60,p.z),Vector3.down,70).OrderByDescending(h=>h.point.y))hits.AppendLine("  "+hit.collider.name+" y="+hit.point.y);
                }
                File.WriteAllText(Folder+"/coast-ground-check.txt",hits.ToString());
            }
            finally{Object.DestroyImmediate(go);}
        }
    }
}
