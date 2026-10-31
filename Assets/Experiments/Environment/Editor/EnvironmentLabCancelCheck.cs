using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouse.EnvironmentLab.Editor
{
    // Only the explicit cancel-check command arms this one-shot observer.
    [InitializeOnLoad]
    public static class EnvironmentLabCancelCheck
    {
        const string Key="WeatherHouse.EnvironmentLab.CancelCheck.";
        const string Running="WeatherHouse.EnvironmentLab.Running";
        static EnvironmentLabCancelCheck() { if(SessionState.GetInt(Key+"Stage",0)!=0) Listen(); }

        public static void Run()
        {
            EnvironmentLabBuilder.RequireSavedScene();
            if(SessionState.GetString(Running,"")!="" || SessionState.GetInt(Key+"Stage",0)!=0)
                throw new InvalidOperationException("An environment check is already running.");
            string scene=SceneManager.GetActiveScene().path;
            if(string.IsNullOrEmpty(scene)) throw new InvalidOperationException("Save the return scene before the cancellation check.");
            SessionState.SetString(Key+"Scene",scene);
            SessionState.SetBool(Key+"Background",Application.runInBackground);
            SessionState.SetBool(Key+"Paused",EditorApplication.isPaused);
            SessionState.SetFloat(Key+"Started",(float)EditorApplication.timeSinceStartup);
            SessionState.SetInt(Key+"Stage",1);
            Directory.CreateDirectory(EnvironmentLabChecks.Output);
            File.WriteAllText(EnvironmentLabChecks.Output+"/cancel-check.txt","RUNNING: waiting for this check's wind iterator.");
            File.WriteAllText(EnvironmentLabChecks.Output+"/request.txt","wind");
            Listen();
        }

        static void Listen()
        {
            EditorApplication.update-=Tick; EditorApplication.update+=Tick;
            EditorApplication.playModeStateChanged-=Changed; EditorApplication.playModeStateChanged+=Changed;
        }
        static void Changed(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredEditMode && SessionState.GetInt(Key+"Stage",0)==2)
                SessionState.SetInt(Key+"Stage",3);
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+"Started",0)>45)
                    throw new TimeoutException("Cancellation check timed out; no unrelated Play session was stopped.");
                int stage=SessionState.GetInt(Key+"Stage",0);
                if(stage==1)
                {
                    if(!EditorApplication.isPlaying || SessionState.GetString(Running,"")!="wind" ||
                       SceneManager.GetActiveScene().path!=WindStudyBuilder.ScenePath) return;
                    var view=UnityEngine.Object.FindObjectOfType<EnvironmentLabView>();
                    // WindStudyChecks disables both inside its live try/finally before yielding.
                    if(!view || view.enabled || view.navigation.enabled) return;
                    SessionState.SetString(Key+"PreviousReport",File.GetLastWriteTimeUtc(EnvironmentLabChecks.Output+"/03_Wind_checks.txt").Ticks.ToString());
                    SessionState.SetInt(Key+"Stage",2);
                    EditorApplication.isPlaying=false;
                }
                else if(stage==3)
                {
                    EnvironmentLabChecks.Assert(!EditorApplication.isPlaying && SessionState.GetString(Running,"")=="", "Running must be cleared after early exit.");
                    EnvironmentLabChecks.Assert(SceneManager.GetActiveScene().path==SessionState.GetString(Key+"Scene","") && !SceneManager.GetActiveScene().isDirty, "Saved return scene must be restored without dirty changes.");
                    EnvironmentLabChecks.Assert(Application.runInBackground==SessionState.GetBool(Key+"Background",false) && EditorApplication.isPaused==SessionState.GetBool(Key+"Paused",false), "Editor background and pause settings must be restored.");
                    EnvironmentLabChecks.Assert(File.ReadAllText(EnvironmentLabChecks.Output+"/result.txt").Contains("OperationCanceledException"), "Early exit must be recorded as cancellation.");
                    EnvironmentLabChecks.Assert(File.GetLastWriteTimeUtc(EnvironmentLabChecks.Output+"/03_Wind_checks.txt").Ticks.ToString()!=SessionState.GetString(Key+"PreviousReport",""), "The interruption report must come from this run.");
                    EnvironmentLabChecks.Assert(File.ReadAllText(EnvironmentLabChecks.Output+"/03_Wind_checks.txt").Contains("FAIL or interrupted"), "The pending wind iterator must run its finally block.");
                    Complete("PASS: actual wind Play interrupted; Running cleared; iterator finally executed; saved scene and editor settings restored.");
                }
            }
            catch(Exception error) { Complete("FAIL: "+error); }
        }
        static void Complete(string result)
        {
            EditorApplication.update-=Tick;
            EditorApplication.playModeStateChanged-=Changed;
            SessionState.EraseInt(Key+"Stage");
            SessionState.EraseString(Key+"Scene");
            SessionState.EraseString(Key+"PreviousReport");
            SessionState.EraseBool(Key+"Background"); SessionState.EraseBool(Key+"Paused");
            SessionState.EraseFloat(Key+"Started");
            File.WriteAllText(EnvironmentLabChecks.Output+"/cancel-check.txt",result);
        }
    }
}
