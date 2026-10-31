using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouse.EnvironmentLab.Editor
{
    // No action on startup. Only explicit allowlisted requests trigger a saved-scene test.
    [InitializeOnLoad]
    public static class EnvironmentLabSession
    {
        const string Running="WeatherHouse.EnvironmentLab.Running";
        const string ReturnScene="WeatherHouse.EnvironmentLab.ReturnScene";
        static double nextPoll,started;
        static int frames;
        static bool ran;
        static IEnumerator steps;
        static string errors="";
        static EnvironmentLabSession()
        {
            EditorApplication.update+=Poll;
            EditorApplication.playModeStateChanged+=PlayChanged;
            if(SessionState.GetString(Running,"")!="" && EditorApplication.isPlaying) BeginTicks();
        }
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll=EditorApplication.timeSinceStartup+1;
            string path=EnvironmentLabChecks.Output+"/request.txt";
            if(!File.Exists(path)) return;
            string command=File.ReadAllText(path).Trim(); File.Delete(path);
            try
            {
                var current=SceneManager.GetActiveScene();
                if(command=="inspect") { File.WriteAllText(EnvironmentLabChecks.Output+"/state.txt","scene="+current.path+"\nplaying="+EditorApplication.isPlaying+"\ndirty="+current.isDirty); return; }
                if(command=="cancel-check")
                {
                    try { EnvironmentLabCancelCheck.Run(); }
                    catch(Exception error) { File.WriteAllText(EnvironmentLabChecks.Output+"/cancel-check.txt","FAIL: "+error); }
                    return;
                }
                if(command!="moss" && command!="fog" && command!="wind") throw new InvalidOperationException("Unknown environment request: "+command);
                EnvironmentLabBuilder.RequireSavedScene();
                SessionState.SetString(ReturnScene,current.path);
                if(command=="moss") EnvironmentLabBuilder.OpenMoss();
                else if(command=="fog") FogStudyBuilder.Open();
                else WindStudyBuilder.Open();
                File.WriteAllText(EnvironmentLabChecks.Output+"/result.txt","RUNNING "+command);
                SessionState.SetString(Running,command);
                SessionState.SetBool("WeatherHouse.EnvironmentLab.Background",Application.runInBackground);
                SessionState.SetBool("WeatherHouse.EnvironmentLab.Paused",EditorApplication.isPaused);
                SessionState.SetBool("WeatherHouse.EnvironmentLab.SettingsSaved",true);
                Application.runInBackground=true;
                EditorApplication.isPaused=false;
                EditorApplication.isPlaying=true;
            }
            catch(Exception error) { Finish(error); }
        }
        static void PlayChanged(PlayModeStateChange change)
        {
            if(change==PlayModeStateChange.EnteredPlayMode && SessionState.GetString(Running,"")!="") BeginTicks();
            if(change==PlayModeStateChange.ExitingPlayMode && SessionState.GetString(Running,"")!="")
                StopChecks(new OperationCanceledException("Environment checks interrupted by Play exit."));
            if(change==PlayModeStateChange.EnteredEditMode) Restore();
        }
        static void BeginTicks()
        {
            started=EditorApplication.timeSinceStartup; frames=Time.frameCount; ran=false; errors="";
            steps=null;
            Application.logMessageReceived-=Log; Application.logMessageReceived+=Log;
            EditorApplication.update-=Tick; EditorApplication.update+=Tick;
        }
        static void Log(string text,string trace,LogType type)
        { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) errors+=text+"\n"; }
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup-started>120) { Finish(new TimeoutException("Environment Play checks timed out")); return; }
            if(!EditorApplication.isPlaying || Time.frameCount-frames<4 || ran) return;
            try
            {
                if(steps==null) steps=Verify();
                if(steps.MoveNext()) return;
                ran=true;
                EnvironmentLabChecks.Assert(errors.Length==0,errors);
                Finish(null);
            }
            catch(Exception error) { Finish(error); }
        }
        static IEnumerator Verify()
        {
            string batch=SessionState.GetString(Running,"");
            if(batch=="moss") EnvironmentLabChecks.Moss();
            else
            {
                var check=batch=="fog"?FogStudyChecks.Run():WindStudyChecks.Run();
                try { while(check.MoveNext()) yield return null; }
                finally { (check as IDisposable)?.Dispose(); }
            }
            string path=EnvironmentLabChecks.Output+"/"+(batch=="moss"?"01_Moss":batch=="fog"?"02_Fog":"03_Wind")+"_Controls.png";
            DateTime old=File.Exists(path)?File.GetLastWriteTimeUtc(path):DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            while(!File.Exists(path) || File.GetLastWriteTimeUtc(path)<=old) yield return null;
        }
        static void Finish(Exception error)
        {
            bool ownsPlay=SessionState.GetString(Running,"")!="";
            StopChecks(error);
            if(ownsPlay && EditorApplication.isPlaying) EditorApplication.isPlaying=false;
            else if(!EditorApplication.isPlaying) Restore();
        }
        static void StopChecks(Exception error)
        {
            EditorApplication.update-=Tick; Application.logMessageReceived-=Log;
            SessionState.EraseString(Running);
            var pending=steps; steps=null;
            // Dispose while the Play scene is alive, so iterator finally blocks can restore it.
            try { (pending as IDisposable)?.Dispose(); }
            catch(Exception cleanupError) { error=error==null?cleanupError:new AggregateException(error,cleanupError); }
            File.WriteAllText(EnvironmentLabChecks.Output+"/result.txt",error==null?"PASS "+DateTime.UtcNow.ToString("O"):"FAIL "+error);
        }
        static void Restore()
        {
            string path=SessionState.GetString(ReturnScene,""); SessionState.EraseString(ReturnScene);
            if (SessionState.GetBool("WeatherHouse.EnvironmentLab.SettingsSaved",false))
            {
                Application.runInBackground=SessionState.GetBool("WeatherHouse.EnvironmentLab.Background",false);
                EditorApplication.isPaused=SessionState.GetBool("WeatherHouse.EnvironmentLab.Paused",false);
                SessionState.EraseBool("WeatherHouse.EnvironmentLab.SettingsSaved");
            }
            if(!string.IsNullOrEmpty(path) && File.Exists(path) && !SceneManager.GetActiveScene().isDirty) EditorSceneManager.OpenScene(path);
        }
    }
}
