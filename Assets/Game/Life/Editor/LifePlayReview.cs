using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    [InitializeOnLoad]
    // Historical 20261026 full review includes retired task-panel reflection; use the current focused UI checks.
    public static class LifePlayReview
    {
        const string Key = "WeatherHouse.Life.Review.Running";
        const string Folder = "LifeGameplayReview/20261026/play";
        enum ReviewMode { Full, FullAi, Ai, World, Photo, Ui, Performance, Book, Dialogue, Persistence, Audio, Photography, PhotographyInteractions, FreshPhotos, Roaming, UiCapture }
        static ReviewMode Mode=>(ReviewMode)SessionState.GetInt(Key+".Mode",0);
        static string ResultPath=>Mode==ReviewMode.UiCapture?Folder+"/ui-capture-play-result.txt":Mode==ReviewMode.Roaming?Folder+"/roaming-play-result.txt":Folder+(Mode==ReviewMode.FreshPhotos?"/fresh-photos-play-result.txt":Mode==ReviewMode.PhotographyInteractions?"/photography-interactions-play-result.txt":Mode==ReviewMode.Audio?"/audio-play-result.txt":Mode==ReviewMode.Photography?"/photography-play-result.txt":Mode==ReviewMode.Photo?"/photo-play-result.txt":Mode==ReviewMode.World?"/world-play-result.txt":Mode==ReviewMode.Ai?"/ai-play-result.txt":Mode==ReviewMode.Ui?"/ui-play-result.txt":Mode==ReviewMode.Performance?"/performance-play-result.txt":Mode==ReviewMode.Book?"/book-play-result.txt":Mode==ReviewMode.Dialogue?"/dialogue-play-result.txt":Mode==ReviewMode.Persistence?"/persistence-play-result.txt":"/result.txt");
        static double started;
        static double nextStage;
        static int stage;
        static readonly List<string> checks = new List<string>();
        static readonly List<string> errors = new List<string>();
        static LifeSession life;
        static GameplayHud hud;
        static string uiResult;
        static Coroutine uiRoutine;
        static LifePlayReview()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Key,false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    started=EditorApplication.timeSinceStartup;nextStage=started+4;stage=0;checks.Clear();errors.Clear();
                    Application.logMessageReceived += Log;
                    Application.runInBackground=true;
                }
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    Application.logMessageReceived -= Log;
                    Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",null);
                    Application.runInBackground=SessionState.GetBool(Key+".Background",false);
                    SessionState.SetBool(Key,false);
                    File.WriteAllText(Folder+"/handoff.txt","Play=False\nDirty="+SceneManager.GetActiveScene().isDirty+"\n"+DateTime.UtcNow.ToString("O"));
                }
            };
        }
        [MenuItem("Weather House/Life/Review Play without changing saves")]
        public static void Begin()=>BeginReview(ReviewMode.Full);
        public static void BeginWithAi()=>BeginReview(ReviewMode.FullAi);
        public static void BeginAiOnly()=>BeginReview(ReviewMode.Ai);
        public static void BeginWorldOnly()=>BeginReview(ReviewMode.World);
        public static void BeginPhotoOnly()=>BeginReview(ReviewMode.Photo);
        public static void BeginUiOnly()=>BeginReview(ReviewMode.Ui);
        public static void BeginPerformanceOnly()=>BeginReview(ReviewMode.Performance);
        public static void BeginBookOnly()=>BeginReview(ReviewMode.Book);
        public static void BeginDialogueOnly()=>BeginReview(ReviewMode.Dialogue);
        public static void BeginPersistenceOnly()=>BeginReview(ReviewMode.Persistence);
        public static void BeginAudioOnly()=>BeginReview(ReviewMode.Audio);
        public static void BeginPhotographyOnly()=>BeginReview(ReviewMode.Photography);
        public static void BeginPhotographyInteractionsOnly()=>BeginReview(ReviewMode.PhotographyInteractions);
        public static void BeginFreshPhotosOnly()=>BeginReview(ReviewMode.FreshPhotos);
        public static void BeginRoamingOnly()=>BeginReview(ReviewMode.Roaming);
        public static void BeginUiCaptureOnly()=>BeginReview(ReviewMode.UiCapture);
        static void BeginReview(ReviewMode mode)
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Review needs Edit mode.");
            var scene=SceneManager.GetActiveScene();
            bool uiReview=mode==ReviewMode.Ui || mode==ReviewMode.UiCapture;
            if(scene.path!="Assets/Scenes/WeatherHouse_WatersideV2.unity" || (scene.isDirty && !uiReview))
                throw new InvalidOperationException("Review needs the clean WatersideV2 scene already open; it never opens or saves scenes.");
            Directory.CreateDirectory(Folder);
            if(scene.isDirty)
            {
                // UI-only validation can preserve an in-progress map without saving over it.
                string snapshot=Folder+"/before-ui-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+".unity";
                if(!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene,snapshot,true) || !scene.isDirty || scene.path!="Assets/Scenes/WeatherHouse_WatersideV2.unity")
                    throw new InvalidOperationException("UI review must preserve the active dirty scene and its original path.");
                File.WriteAllText(Folder+"/ui-dirty-scene-backup.txt",Path.GetFullPath(snapshot)+"\nActive scene remains dirty; original scene was not saved.\n"+DateTime.UtcNow.ToString("O"));
            }
            Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",Path.GetFullPath(Folder+"/save-"+DateTime.UtcNow.ToString("HHmmss")));
            SessionState.SetBool(Key+".Background",Application.runInBackground);
            SessionState.SetInt(Key+".Mode",(int)mode);
            SessionState.SetBool(Key,true);
            File.WriteAllText(ResultPath,"RUNNING\n");
            EditorApplication.isPaused=false;
            EditorApplication.isPlaying=true;
        }
        static void Log(string condition,string stack,LogType type)
        {
            if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) errors.Add(condition+"\n"+stack);
        }
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying || EditorApplication.isPaused || EditorApplication.isCompiling)return;
            double elapsed=EditorApplication.timeSinceStartup-started;
            if(EditorApplication.timeSinceStartup<nextStage)return;
            try
            {
                if(stage==0 && elapsed>4)
                {
                    life=UnityEngine.Object.FindObjectOfType<LifeSession>();
                    Require(life && life.World && life.Gameplay,"Formal scene bootstraps LifeSession and world");
                    if(Mode==ReviewMode.UiCapture)
                    {
                        uiResult=null;uiRoutine=life.StartCoroutine(LifeUiCaptureReview.Run(life,result=>uiResult=result));
                        stage=30;nextStage=EditorApplication.timeSinceStartup+.5;return;
                    }
                    if(Mode==ReviewMode.Roaming)
                    {
                        uiResult=null;uiRoutine=life.StartCoroutine(LifeRoamingPlayChecks.Run(life,result=>uiResult=result));
                        stage=29;nextStage=EditorApplication.timeSinceStartup+.5;return;
                    }
                    if(Mode==ReviewMode.FreshPhotos)
                    {
                        uiResult=null;uiRoutine=life.StartCoroutine(LifeFreshPhotos.Run(life,result=>uiResult=result));
                        stage=28;nextStage=EditorApplication.timeSinceStartup+.5;return;
                    }
                    if(Mode==ReviewMode.PhotographyInteractions)
                    {
                        uiResult=null;uiRoutine=life.StartCoroutine(LifePhotoInteractionChecks.Run(life,result=>uiResult=result));
                        stage=27;nextStage=EditorApplication.timeSinceStartup+.5;return;
                    }
                    if(Mode==ReviewMode.Audio)
                    {
                        checks.Add(LifeAudioChecks.Runtime());
                        uiResult=null;uiRoutine=life.StartCoroutine(RunAudioChecks());
                        stage=26;nextStage=EditorApplication.timeSinceStartup+.5;return;
                    }
                    if(Mode==ReviewMode.Photography)
                    {
                        LifePhotoFlowChecks.Run();
                        LifePhotoWorldChecks.RunDepth(life);
                        LifePhotoWorldChecks.Run(life);
                        uiResult=null;uiRoutine=life.StartCoroutine(RunPhotoCaptureChecks());
                        stage=23;nextStage=EditorApplication.timeSinceStartup+.5;return;
                    }
                    if(Mode==ReviewMode.Persistence)
                    {
                        checks.Add(LifePersistencePlayChecks.Run(life));
                        Require(errors.Count==0,"No runtime errors during actual persistence review");Finish(true,null);return;
                    }
                    if(Mode==ReviewMode.Performance)
                    {
                        LifePerformanceChecks.Begin(life);stage=22;nextStage=EditorApplication.timeSinceStartup+.5;return;
                    }
                    if(Mode==ReviewMode.Ui)
                    {
                        uiResult=null;uiRoutine=life.StartCoroutine(WeatherHouse.SquirrelOga.Editor.SquirrelOgaPlayChecks.RunWithUi(life,result=>{uiResult=result;File.WriteAllText(Folder+"/ui-lifecycle.txt",result);}));
                        stage=21;nextStage=EditorApplication.timeSinceStartup+.5;return;
                    }
                    if(Mode==ReviewMode.Photo)
                    {
                        LifeWorldRouteAudit.Run();
                        LifeDialoguePlayChecks.Begin(life,true);stage=14;nextStage=EditorApplication.timeSinceStartup+.5;return;
                    }
                    if(Mode==ReviewMode.Dialogue)
                    {
                        LifeDialoguePlayChecks.Begin(life);stage=14;nextStage=EditorApplication.timeSinceStartup+.5;return;
                    }
                    if(Mode==ReviewMode.World)
                    {
                        checks.AddRange(LifeWorldPlayChecks.Run(life).Select(label=>"PASS "+label));
                        LifeWorldAsyncChecks.Begin(life);stage=20;nextStage=EditorApplication.timeSinceStartup+.5;return;
                    }
                    if(Mode==ReviewMode.Ai)
                    {
                        LifeAiReview.Begin();stage=13;nextStage=EditorApplication.timeSinceStartup+.5;return;
                    }
                    hud=life.GetComponent<GameplayHud>();
                    foreach(string key in new[]{"dog","squirrel","fox","sheep","deer","companion"})
                        Require(life.World.FindSpeaker(key),"Existing key animal bound: "+key);
                    Require(string.IsNullOrEmpty(life.World.SetupWarning),"World stations are valid: "+life.World.SetupWarning);
                    Require(life.World.Requests.Count>=40,"Full authored phrase set is present");
                    Require(life.World.Objects.Count>=25,"Runtime interaction fixtures are present");
                    Require(!hud.useAlbumDesignSamples,"Journal uses live records");
                    Require(life.GetComponent<LifeGestureInput>(),"Gesture input is mounted");
                    Require(life.Progress.tasks.Count==0,"Fresh review save has no preaccepted help");
                    Require(!TaskPanel().activeSelf && string.IsNullOrEmpty(life.TaskText),"No help panel or placeholder before confirmed need");
                    Require(life.Gameplay.Notebook.GetWord("want").confirmed && life.Gameplay.Notebook.GetWord("reject").confirmed,"Opening vocabulary is known");
                    Require(life.IsGestureUnlocked("rain") && !life.IsGestureLearned("rain") && !life.IsGestureUnlocked("sunlight"),"Tutorial reward and learning gates are separate");
                    Require(!life.TryCast("sunlight",out _),"Locked weather cannot cast");
                    Capture("01-world.png");Advance(1);
                }
                else if(stage==1) {hud.ToggleJournal();Advance(.35);}
                else if(stage==2)
                {
                    var book=life.GetComponent<LifeJournal3D>();
                    Require(book && book.Available && book.IsAnimating && book.HasPageSnapshot,"Real 3D journal opens with text bound to paper");
                    WriteBookState(book);Capture("02a-book-opening.png");Advance(1.3);
                }
                else if(stage==3 && !life.GetComponent<LifeJournal3D>().IsAnimating) {Require(hud.JournalOpen && !hud.IsAlbumOpening,"J journal settles into readable state");Capture("02-journal.png");Advance(1);}
                else if(stage==4)
                {
                    typeof(GameplayHud).GetField("albumGesturesSelected",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(hud,true);
                    typeof(GameplayHud).GetMethod("RebuildJournalPage",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(hud,new object[]{1});Advance(.35);
                }
                else if(stage==5)
                {
                    var book=life.GetComponent<LifeJournal3D>();
                    Require(book.IsAnimating && book.MotionName=="Turning" && book.HasPageSnapshot,"Changing journal section turns physical paper with its text");
                    Capture("03a-book-turning.png");Advance(1.3);
                }
                else if(stage==6 && !life.GetComponent<LifeJournal3D>().IsAnimating) {Capture("03-gesture-book.png");Advance(1);}
                else if(stage==7) {hud.ToggleJournal();Advance(1.3);}
                else if(stage==8 && !life.GetComponent<LifeJournal3D>().IsAnimating)
                {
                    Require(!hud.JournalOpen,"3D book closes and releases reading mode");
                    // Recognition itself is tested by LifeGestureChecks; this exercises its accepted result through the real weather path.
                    Require(life.LearnGesture("rain") && life.TryCast("rain",out _),"Learned tutorial rain reaches local weather");Advance(4);
                }
                else if(stage==9) {Capture("04-local-rain.png");Advance(1);}
                else if(stage==10)
                {
                    Require(life.Progress.tasks.Count==0,"Free casting does not fabricate an understood help task");
                    if(Mode==ReviewMode.Book){Require(errors.Count==0,"No runtime errors while reading, turning and casting");Finish(true,null);return;}
                    checks.AddRange(LifeWorldPlayChecks.Run(life).Select(label=>"PASS "+label));
                    // A deliberate UI fixture, not a simulated claim that the AI service was tested.
                    var request=life.World.Requests.First(r=>r.id=="dog.water");
                    life.Progress.tasks.Add(new LifeTask{requestId=request.id,eventId=request.eventId,animalId=life.World.FindSpeaker("dog").Actor.animalId});
                    Advance(.5);
                }
                else if(stage==11)
                {
                    Require(TaskPanel().activeSelf && life.TaskText.Contains("接水"),"Active request UI fixture displays only its concrete help objective");
                    life.Progress.tasks.RemoveAll(t=>t.requestId=="dog.water");Advance(.5);
                }
                else if(stage==12)
                {
                    Require(!TaskPanel().activeSelf,"Help panel disappears when no active request remains");
                    Require(errors.Count==0,"No runtime errors during review");
                    if(Mode==ReviewMode.FullAi){LifeAiReview.Begin();Advance(.5);}
                    else Finish(true,null);
                }
                else if(stage==13 && !LifeAiReview.Running)
                {
                    Require(LifeAiReview.LastStatus=="PASS","Live provider meaning and 24-word cross-animal confirmation checks pass");
                    Require(errors.Count==0,"No runtime errors during live provider review");
                    LifeDialoguePlayChecks.Begin(life);Advance(.5);
                }
                else if(stage==14 && !LifeDialoguePlayChecks.Running)
                {
                    Require(LifeDialoguePlayChecks.LastStatus=="PASS",Mode==ReviewMode.Photo?"Actual dynamic photo capture, cancellation and frame scheduling pass":"Real dialogue to provider to help and photo pipeline passes");
                    Require(errors.Count==0,"No runtime errors during dialogue and photo review");Finish(true,null);
                }
                else if(stage==20 && !LifeWorldAsyncChecks.Running)
                {
                    Require(LifeWorldAsyncChecks.LastStatus=="PASS","Actual animals arrive, rest, drink and recover through existing controllers");
                    Require(errors.Count==0,"No runtime errors during actual animal review");Finish(true,null);
                }
                else if(stage==21 && uiResult!=null)
                {
                    Require(uiResult.StartsWith("PASS"),"UI lifecycle, content and resource checks pass");
                    Require(errors.Count==0,"No runtime errors during UI review");Finish(true,null);
                }
                else if(stage==22 && !LifePerformanceChecks.Running)
                {
                    Require(LifePerformanceChecks.LastStatus=="PASS","Current scene A/B/A sampling completed and restored its settings");
                    Require(errors.Count==0,"No runtime errors during performance review");Finish(true,null);
                }
                else if(stage==23 && uiResult!=null)
                {
                    Require(uiResult.StartsWith("PASS"),"Free shutter and immutable captured records: "+uiResult);
                    uiResult=null;uiRoutine=life.StartCoroutine(LifePhotoUiChecks.Run(life,result=>uiResult=result));
                    stage=24;nextStage=EditorApplication.timeSinceStartup+.5;
                }
                else if(stage==24 && uiResult!=null)
                {
                    Require(uiResult.StartsWith("PASS"),"Photography tasks, 31 glyphs and photo UI: "+uiResult);
                    Require(errors.Count==0,"No runtime errors during photography review");Finish(true,null);
                }
                else if(stage==26 && uiResult!=null)
                {
                    Require(uiResult.StartsWith("PASS"),"Actual audio trigger and cancellation checks: "+uiResult);
                    Require(errors.Count==0,"No runtime errors during audio review");Finish(true,null);
                }
                else if(stage==27 && uiResult!=null)
                {
                    Require(uiResult.StartsWith("PASS"),"Actual public interaction and reward checks: "+uiResult);
                    Require(errors.Count==0,"No runtime errors during photography interaction review");Finish(true,null);
                }
                else if(stage==28 && uiResult!=null)
                {
                    Require(uiResult.StartsWith("PASS"),"Actual fresh photographs: "+uiResult);
                    Require(errors.Count==0,"No runtime errors during fresh photo capture");Finish(true,null);
                }
                else if(stage==29 && uiResult!=null)
                {
                    Require(uiResult.StartsWith("PASS"),"Actual habitat roaming and task ownership: "+uiResult);
                    Require(errors.Count==0,"No runtime errors during animal roaming review");Finish(true,null);
                }
                else if(stage==30 && uiResult!=null)
                {
                    Require(uiResult.StartsWith("PASS"),"UI capture batch: "+uiResult);
                    Require(errors.Count==0,"No runtime errors during UI capture review");Finish(true,null);
                }
                if(elapsed>(stage>=13?900:120))Finish(false,"Review timeout");
            }
            catch(Exception e){Finish(false,e.ToString());}
        }
        static void Capture(string name)=>ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder+"/"+name));
        static IEnumerator RunAudioChecks()
        {
            string playback=null,output=null;
            yield return LifeAudioPlayChecks.Run(life,result=>playback=result);
            if(playback==null || !playback.StartsWith("PASS",StringComparison.Ordinal))
            {uiResult=playback??"FAIL Audio playback checks did not return a result.";yield break;}
            yield return LifeAudioAudibilityChecks.Run(life,result=>output=result);
            uiResult=(output!=null && output.StartsWith("PASS",StringComparison.Ordinal)?"PASS":"FAIL")+
                "\n"+playback+"\n"+(output??"Audio output checks did not return a result.");
        }
        static IEnumerator RunPhotoCaptureChecks()
        {
            IEnumerator routine=LifePhotoCaptureChecks.RunPlayChecks();
            Exception failure=null;
            try
            {
                while(true)
                {
                    bool more;object next=null;
                    try {more=routine.MoveNext();if(more)next=routine.Current;}
                    catch(Exception error){failure=error;break;}
                    if(!more)break;yield return next;
                }
            }
            finally {(routine as IDisposable)?.Dispose();}
            uiResult=failure==null?"PASS":("FAIL "+failure);
        }
        static GameObject TaskPanel()=>((RectTransform)typeof(LifeGestureInput).GetField("taskRoot",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(life.GetComponent<LifeGestureInput>())).gameObject;
        static void WriteBookState(LifeJournal3D book)
        {
            var rig=book.Model.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="JournalRig");
            var page=book.Model.GetComponentsInChildren<Renderer>().First(r=>r.name=="PageRight");
            File.WriteAllText(Folder+"/book-runtime.txt","Motion="+book.MotionName+"\nProgress="+book.AnimationProgress+
                "\nRig rotation="+(rig?rig.localEulerAngles.ToString("F3"):"missing")+"\nRig scale="+(rig?rig.localScale.ToString("F3"):"missing")+
                "\nPageRight bounds="+page.bounds+"\nView="+book.ViewTexture.width+"x"+book.ViewTexture.height);
        }
        static void Advance(double delay) {stage++;nextStage=EditorApplication.timeSinceStartup+delay;}
        static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);checks.Add("PASS "+label);}
        static void Finish(bool success,string failure)
        {
            if(stage==99)return;stage=99;
            LifeDialoguePlayChecks.Cancel();
            LifeWorldAsyncChecks.Cancel();
            LifePerformanceChecks.Cancel();
            if(life && uiRoutine!=null)life.StopCoroutine(uiRoutine);uiRoutine=null;
            File.WriteAllText(ResultPath,(success?"PASS":"FAIL")+"\n"+string.Join("\n",checks)+"\n"+failure+"\n"+string.Join("\n",errors)+
                "\nBoundary: automated checks use isolated saves. Provider cases use authored replies; human visual acceptance remains separate.\n");
            EditorApplication.isPlaying=false;
        }
    }
}
