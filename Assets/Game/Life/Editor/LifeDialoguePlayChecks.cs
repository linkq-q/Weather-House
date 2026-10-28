using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    // Runs the actual E listening/task and photo entry points in the isolated review save.
    public static class LifeDialoguePlayChecks
    {
        [Serializable] sealed class Report
        {
            public string status="RUNNING",utc,error;
            public string boundary="Actual listening and gameplay entry points; no response or semantic judgement request. Player placement and topic selection are test setup; no human keyboard usability claim.";
            public List<string> checks=new List<string>();
            public List<MotionPhotoRecorder.CaptureCost> captureCosts=new List<MotionPhotoRecorder.CaptureCost>();
            public List<MotionPhotoRecorder.CaptureCost> injectedSlowCosts=new List<MotionPhotoRecorder.CaptureCost>();
            public bool injectedSlowCapture;
        }
        static LifeSession owner;
        static Coroutine routine;
        static bool photoOnly;
        public static bool Running {get;private set;}
        public static string LastStatus {get;private set;}
        public static void Begin(LifeSession life,bool onlyPhotos=false)
        {
            if(Running || !EditorApplication.isPlaying || !life || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")))
                throw new InvalidOperationException("Dialogue review requires isolated Life Play mode.");
            photoOnly=onlyPhotos;owner=life;routine=life.StartCoroutine(Run(life));
        }
        public static void Cancel(){if(owner && routine!=null)owner.StopCoroutine(routine);routine=null;Running=false;}
        static IEnumerator Run(LifeSession life)
        {
            Running=true;LastStatus="RUNNING";
            var report=new Report{utc=DateTime.UtcNow.ToString("O")};Write(report);
            if(photoOnly)report.boundary="Actual photo and notebook entry points with an arranged player position; no AI request is sent.";
            GameplaySession game=life.Gameplay;
            GameplayHud hud=game.GetComponent<GameplayHud>();
            LifeSpeaker dog=life.World.FindSpeaker("dog");
            int topic=dog.topicIndex;
            Vector3 position=game.PlayerTransform.position;
            Quaternion rotation=game.PlayerTransform.rotation;
            string progress=JsonUtility.ToJson(life.Progress),world=life.World.CaptureJson(),notebook=JsonUtility.ToJson(game.Notebook);
            var work=Exercise(life,report);
            Action<MotionPhotoRecorder.CaptureCost> measure=cost=>
            {
                if(report.injectedSlowCapture){report.injectedSlowCosts.Add(cost);System.Threading.Thread.Sleep(180);}
                else report.captureCosts.Add(cost);
            };
            game.Recorder.CaptureMeasured+=measure;
            try
            {
                while(true)
                {
                    bool more;
                    try {more=work.MoveNext();}
                    catch(Exception e){report.error=e.ToString();report.status="FAIL";break;}
                    if(!more){report.status="PASS";break;}
                    yield return work.Current;
                }
            }
            finally
            {
                hud.CloseDialogue();hud.SetCameraMode(false,true);
                game.Recorder.CaptureMeasured-=measure;
                if(game.Recorder.IsRecording)game.Recorder.CancelCapture();
                dog.topicIndex=topic;
                JsonUtility.FromJsonOverwrite(progress,life.Progress);
                JsonUtility.FromJsonOverwrite(notebook,game.Notebook);
                life.World.RestoreJson(world);
                game.PlayerTransform.SetPositionAndRotation(position,rotation);
                game.SaveNotebook();life.SaveAndNotify("");
                if(report.status=="RUNNING")report.status="INTERRUPTED";
                LastStatus=report.status;Running=false;Write(report);
            }
        }
        static IEnumerator Exercise(LifeSession life,Report report)
        {
            var game=life.Gameplay;var hud=game.GetComponent<GameplayHud>();
            var dog=life.World.FindSpeaker("dog");
            UnityEngine.Object.FindObjectOfType<LifeLocalWeather>().Cast("clear",out _);
            game.PlayerTransform.position=dog.transform.position+Vector3.right*1.5f;
            Physics.SyncTransforms();
            Action<string> open=id=>
            {
                hud.CloseDialogue();dog.topicIndex=Array.FindIndex(dog.requests,r=>r.id==id);
                Check(report,dog.topicIndex>=0,"Authored dialogue exists: "+id);
                typeof(GameplaySession).GetProperty("Observed").SetValue(game,dog.Actor);
                hud.ToggleDialogue();
                Check(report,!hud.IsDialogueOpen && hud.DialogueTarget==null && !hud.BlocksMovement && life.Progress.HasHeard(id),"E listens through HUD without opening a player reply: "+id);
            };
            if(!photoOnly)
            {
            var greeting=dog.requests.First(r=>r.condition=="conversation");
            open(greeting.id);
            Check(report,life.Progress.HasHeard(greeting.id) && life.Progress.tasks.Count==0,"Listening records greeting without an action task");
            open("dog.rain.lesson");
            var task=life.Progress.FindTask("dog.rain.lesson");
            Check(report,task!=null && task.status==LifeTaskStatus.Active && life.Progress.tasks.Count==1,"Listening accepts only the current nearby request");
            long acceptedAt=task.acceptedUtcTicks;
            string acceptedWorld=life.World.CaptureJson();
            open("dog.rain.lesson");
            Check(report,life.Progress.tasks.Count==1 && task.acceptedUtcTicks==acceptedAt && life.World.CaptureJson()==acceptedWorld,"Repeated E does not reset task or world baseline");
            Check(report,!game.Judging && !hud.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="Player reply bubble" || t.name=="Chinese reply" || t.name=="Player spoken reply"),"Listening creates no reply UI or response request");
            Check(report,game.Notebook.GetWord("rain")?.confirmed!=true,"Listening does not confirm a word meaning");
            string currentUtterance=dog.Actor.UtteranceId;
            Check(report,!life.Listen(dog.Actor,currentUtterance+".stale"),"Stale listening cannot accept a request");
            hud.SetCameraMode(true,true);
            Check(report,!life.Listen(dog.Actor,currentUtterance),"Camera blocks direct listening calls");
            hud.SetCameraMode(false,true);
            Vector3 near=game.PlayerTransform.position;
            game.PlayerTransform.position=dog.transform.position+Vector3.right*20f;
            Check(report,!life.Listen(dog.Actor,currentUtterance),"Distance blocks direct listening calls");
            game.PlayerTransform.position=near;
            hud.CloseDialogue();yield return null;
            var panel=(RectTransform)typeof(LifeGestureInput).GetField("taskRoot",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(life.GetComponent<LifeGestureInput>());
            Check(report,panel && life.TaskText.Contains("雨"),"Heard rain request retains its task panel and explanation");
            ScreenCapture.CaptureScreenshot(Path.GetFullPath("LifeGameplayReview/20261026/play/05-listening-task.png"));
            yield return null;
            Check(report,life.TryCast("rain",out _),"Unlocked rain executes without practice after listening");
            float deadline=Time.realtimeSinceStartup+10;
            while(task.status!=LifeTaskStatus.Complete && Time.realtimeSinceStartup<deadline)yield return null;
            Check(report,task.status==LifeTaskStatus.Complete,"Nearby actual rainfall completes the accepted request through the normal update");
            Check(report,life.Progress.friendlyAnimals.Contains(dog.Actor.animalId),"Physical completion records the animal relationship");
            Check(report,game.Notebook.GetWord("rain")?.confirmed!=true && !life.IsGestureUnlocked("sunlight"),"Heard help does not unlock a photography word or another gesture");
            }
            UnityEngine.Object.FindObjectOfType<LifeLocalWeather>().Cast("clear",out _);

            open("dog.hello");hud.CloseDialogue();hud.SetCameraMode(true,true);
            // Fit the dog and its real speech anchor. A 1.5m close-up cropped the anchor
            // above the screen and correctly produced an ordinary photo without a turn.
            bool speechFramed=false;
            var capsule=game.PlayerTransform.GetComponent<CharacterController>();
            var yaw=typeof(PhotoCameraRig).GetField("photoYaw",BindingFlags.NonPublic|BindingFlags.Instance);
            var pitch=typeof(PhotoCameraRig).GetField("photoPitch",BindingFlags.NonPublic|BindingFlags.Instance);
            var updatePose=typeof(PhotoCameraRig).GetMethod("UpdatePose",BindingFlags.NonPublic|BindingFlags.Instance);
            foreach(float angle in new[]{90f,55f,125f,0f,180f,270f})
            {
                if(!LifeFreshPhotos.TryStand(game,capsule,LifeFreshPhotos.BoundsOf(dog.Actor).center,2.4f,angle,out Vector3 stand))continue;
                game.PlayerTransform.position=stand;Physics.SyncTransforms();yield return null;
                Vector3 center=Vector3.Lerp(LifeFreshPhotos.BoundsOf(dog.Actor).center,dog.Actor.BubbleAnchor,.4f);
                Vector3 poseAngles=Quaternion.LookRotation(center-(stand+Vector3.up*Mathf.Max(.2f,game.forestSprite.cameraHeight))).eulerAngles;
                yaw.SetValue(game.PhotoRig,poseAngles.y);pitch.SetValue(game.PhotoRig,Mathf.DeltaAngle(0,poseAngles.x));updatePose.Invoke(game.PhotoRig,null);
                yield return new WaitForSeconds(.5f);
                if(game.Observed==dog.Actor && hud.IsAnimalSpeechVisible(dog.Actor)){speechFramed=true;break;}
            }
            Check(report,speechFramed,"Actual photo framing includes the visible current speech anchor");
            // Keep this real supported pose for the remaining capture.
            var subjectRenderers=dog.Actor.VisualRoot.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();
            Check(report,subjectRenderers.Length>0,"Photo subject has actual visible render geometry");
            Bounds subjectBounds=subjectRenderers[0].bounds;
            foreach(var renderer in subjectRenderers.Skip(1))subjectBounds.Encapsulate(renderer.bounds);
            Check(report,game.Observed==dog.Actor,"Actual camera observation finds the framed animal without forcing a target");
            game.PhotoRig.FocusAt(new Vector2(Screen.width*.5f,Screen.height*.5f));
            var expected=life.CaptureContext(dog.Actor);
            int before=game.Notebook.observations.Count;
            game.TakePhoto();
            Check(report,game.Recorder.IsRecording,"Formal photo entry starts the real motion recorder");
            while(game.Recorder.IsRecording)yield return null;
            Check(report,game.Notebook.observations.Count==before+1,"Real recording reaches the journal index");
            var photo=game.Notebook.observations.Last();
            Check(report,photo.frameCount==18 && Directory.GetFiles(Path.Combine(GameplaySession.PersistenceDirectory,photo.clipFolder),"*.png").Length==18,"Recorded photo retains all eighteen actual frames");
            Check(report,photo.context!=null && photo.context.speakerId==expected.speakerId && photo.context.anchorPosition==expected.anchorPosition,"Photo keeps the original speaker and anchor snapshot");
            string saved=File.ReadAllText(game.SavePath);
            Check(report,JsonUtility.FromJson<LanguageNotebook>(saved).observations.Any(p=>p.id==photo.id && p.frameCount==18),"Photo and context survive a notebook disk round trip");
            before=game.Notebook.observations.Count;
            typeof(GameplaySession).GetProperty("Observed").SetValue(game,dog.Actor);
            string originalUtterance=dog.Actor.UtteranceId;
            string[] originalGlyphs=(string[])dog.Actor.CurrentGlyphIds.Clone();
            string originalContext=JsonUtility.ToJson(life.CaptureContext(dog.Actor));
            Check(report,hud.IsAnimalSpeechVisible(dog.Actor),"The changed-turn photograph starts with an actual visible utterance");
            game.TakePhoto();dog.SelectNext();
            Check(report,dog.Actor.UtteranceId!=originalUtterance && !dog.Actor.CurrentGlyphIds.SequenceEqual(originalGlyphs),"The following dialogue turn has a different identity and glyph sequence");
            while(game.Recorder.IsRecording)yield return null;
            Check(report,game.Notebook.observations.Count==before+1,"A changed utterance preserves the shutter photograph in the journal");
            var truncated=game.Notebook.observations.Last();
            string truncatedFolder=Path.Combine(GameplaySession.PersistenceDirectory,truncated.clipFolder);
            Check(report,truncated.liveTruncated && truncated.frameCount==1 && File.Exists(Path.Combine(truncatedFolder,"frame_000.png")) &&
                Directory.GetFiles(truncatedFolder,"*.png").Length==1,"An immediate turn change retains exactly the shutter frame and records no later-turn images");
            Check(report,truncated.utteranceId==originalUtterance && truncated.glyphIds.SequenceEqual(originalGlyphs) &&
                JsonUtility.ToJson(truncated.context)==originalContext,"The retained photograph keeps the original utterance, glyph order, participants and anchor context");
            var metadata=JsonUtility.FromJson<ObservationRecord>(File.ReadAllText(Path.Combine(truncatedFolder,"metadata.json")));
            var reloaded=JsonUtility.FromJson<LanguageNotebook>(File.ReadAllText(game.SavePath)).observations.Single(p=>p.id==truncated.id);
            Check(report,metadata.liveTruncated && metadata.frameCount==1 && metadata.utteranceId==originalUtterance && metadata.glyphIds.SequenceEqual(originalGlyphs) &&
                JsonUtility.ToJson(metadata.context)==originalContext && JsonUtility.ToJson(reloaded)==JsonUtility.ToJson(truncated),
                "The original one-frame photograph and context survive both metadata and notebook disk reloads");
            if(photoOnly)
            {
                // Deliberate 180ms per capture load exposes catch-up scheduling; it is not a performance benchmark.
                report.injectedSlowCapture=true;
                typeof(GameplaySession).GetProperty("Observed").SetValue(game,dog.Actor);
                game.TakePhoto();
                while(game.Recorder.IsRecording)yield return null;
                report.injectedSlowCapture=false;
                Check(report,report.injectedSlowCosts.Count==18,"Injected slow capture still completes its photo");
                Check(report,report.injectedSlowCosts.GroupBy(c=>c.unityFrame).All(g=>g.Count()==1),"Even under injected 180ms capture cost, no Unity frame records more than one image");
            }
        }
        static void Check(Report report,bool value,string label){if(!value)throw new InvalidOperationException(label);report.checks.Add(label);Write(report);}
        static void Write(Report report)=>File.WriteAllText("LifeGameplayReview/20261026/play/"+(photoOnly?"photo-only-checks.json":"dialogue-photo-checks.json"),JsonUtility.ToJson(report,true));
    }
}
