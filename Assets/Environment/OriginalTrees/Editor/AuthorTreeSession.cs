using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouse.OriginalTrees.Editor
{
    // Explicit requests only. Scene and Play ownership are scoped to this comparison.
    [InitializeOnLoad]
    public static class AuthorTreeSession
    {
        const string Key="WeatherHouse.OriginalTrees.";
        static double nextPoll,started;
        static int frames;
        static string errors="";
        static AuthorTreeSession()
        {
            EditorApplication.update+=Poll;
            EditorApplication.playModeStateChanged+=PlayChanged;
            if(SessionState.GetBool(Key+"Running",false) && EditorApplication.isPlaying) Begin();
        }
        [MenuItem("Weather House/Original Trees/验证并拍摄对照")]
        public static void Verify() => Verify(false);

        [MenuItem("Weather House/Original Trees/重建源参数并验证对照")]
        public static void RebuildAndVerify() => Verify(true);

        [MenuItem("Weather House/Original Trees/应用光照校准并验证对照")]
        public static void CalibrateAndVerify() => Verify(false,true);

        static void Verify(bool rebuild,bool calibrate=false)
        {
            AuthorTreeStudy.RequireSaved();
            AuthorTreeStudy.Assert(!SessionState.GetBool(Key+"Running",false) && !SessionState.GetBool(Key+"Restore",false),"Previous comparison still awaits completion or scene restoration");
            SessionState.SetString(Key+"Return",SceneManager.GetActiveScene().path);
            SessionState.SetBool(Key+"Background",Application.runInBackground);
            SessionState.SetBool(Key+"Paused",EditorApplication.isPaused);
            SessionState.SetBool(Key+"Restore",true);
            try
            {
                if(rebuild) AuthorTreeStudy.Build(); else AuthorTreeStudy.Open();
                if(calibrate) AuthorTreeLighting.ApplySaved();
                Directory.CreateDirectory(AuthorTreeStudy.Output);
                File.WriteAllText(AuthorTreeStudy.Output+"/result.txt","RUNNING");
                SessionState.SetFloat(Key+"RequestedAt",(float)EditorApplication.timeSinceStartup);
                SessionState.SetBool(Key+"Running",true);
                Application.runInBackground=true; EditorApplication.isPaused=false;
                EditorApplication.isPlaying=true;
            }
            catch(Exception e) { Finish(e); }
        }
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<nextPoll) return;
            nextPoll=EditorApplication.timeSinceStartup+1;
            if(SessionState.GetBool(Key+"Running",false) && !EditorApplication.isPlaying)
            {
                double requested=SessionState.GetFloat(Key+"RequestedAt",(float)EditorApplication.timeSinceStartup);
                if(EditorApplication.timeSinceStartup-requested>90)
                { Finish(new TimeoutException("Entering Play for the tree capture timed out"));return; }
                if(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
                { Finish(new OperationCanceledException("Play entry for the tree capture was canceled or refused"));return; }
            }
            if(EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if(!SessionState.GetBool(Key+"Running",false) && !EditorApplication.isPlayingOrWillChangePlaymode) Restore();
            string path=AuthorTreeStudy.Output+"/request.txt";
            if(!File.Exists(path)) return;
            string command=File.ReadAllText(path).Trim();File.Delete(path);
            try
            {
                if(command=="inspect")
                {
                    var s=SceneManager.GetActiveScene();
                    File.WriteAllText(AuthorTreeStudy.Output+"/state.txt","scene="+s.path+"\nplaying="+EditorApplication.isPlaying+"\ndirty="+s.isDirty);
                }
                else if(command=="verify") Verify();
                else if(command=="lighting-verify") CalibrateAndVerify();
                else if(command=="rebuild-verify") RebuildAndVerify();
                else if(command=="open") AuthorTreeStudy.Open();
                else throw new InvalidOperationException("Unsupported original tree request: "+command);
            }
            catch(Exception e) { File.WriteAllText(AuthorTreeStudy.Output+"/result.txt","FAIL "+e); }
        }
        static void PlayChanged(PlayModeStateChange change)
        {
            if(change==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key+"Running",false)) Begin();
            if(change==PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(Key+"Running",false))
                Stop(new OperationCanceledException("Original tree capture interrupted by Play exit."));
            if(change==PlayModeStateChange.EnteredEditMode)
            {
                if(SessionState.GetBool(Key+"Running",false))
                    Stop(new OperationCanceledException("Tree capture returned to Edit mode before completion"));
                Restore();
            }
        }
        static void Begin()
        {
            started=SessionState.GetFloat(Key+"RequestedAt",(float)EditorApplication.timeSinceStartup);frames=Time.frameCount;errors="";
            EditorApplication.update-=Tick;EditorApplication.update+=Tick;
            Application.logMessageReceived-=Log;Application.logMessageReceived+=Log;
        }
        static void Log(string text,string trace,LogType type)
        { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) errors+=text+"\n"; }
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup-started>90) { Finish(new TimeoutException("Tree capture timed out"));return; }
            if(!EditorApplication.isPlaying || Time.frameCount-frames<8) return;
            try
            {
                AuthorTreeStudy.VerifyAndCapture();
                AuthorTreeStudy.Assert(errors.Length==0,errors);
                Finish(null);
            }
            catch(Exception e) { Finish(e); }
        }
        static void Stop(Exception error)
        {
            EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
            SessionState.EraseBool(Key+"Running");
            SessionState.EraseFloat(Key+"RequestedAt");
            Directory.CreateDirectory(AuthorTreeStudy.Output);
            File.WriteAllText(AuthorTreeStudy.Output+"/result.txt",error==null?"PASS "+DateTime.UtcNow.ToString("O"):"FAIL "+error);
        }
        static void Finish(Exception error)
        {
            bool ownsPlay=SessionState.GetBool(Key+"Running",false);
            try { Stop(error); }
            finally
            {
                // Setting isPlaying=false changes the flag before the native Play scene has exited.
                // Let EnteredEditMode restore it, rather than opening a scene in the same callback.
                if(ownsPlay && EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying=false;
                else if(!EditorApplication.isPlayingOrWillChangePlaymode) Restore();
            }
        }
        static void Restore()
        {
            if(!SessionState.GetBool(Key+"Restore",false)) return;
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            Application.runInBackground=SessionState.GetBool(Key+"Background",false);
            EditorApplication.isPaused=SessionState.GetBool(Key+"Paused",false);
            string path=SessionState.GetString(Key+"Return","");
            if(string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if(SceneManager.GetActiveScene().path!=path)
            {
                // Keep recovery information if someone edited the scene after this request.
                if(SceneManager.GetActiveScene().isDirty) return;
                EditorSceneManager.OpenScene(path);
            }
            SessionState.EraseBool(Key+"Restore");SessionState.EraseString(Key+"Return");
        }
    }
}
