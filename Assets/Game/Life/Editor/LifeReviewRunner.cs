using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouse.Life.Editor
{
    // Explicit local commands only; this observer never saves scenes or runs automatically on load.
    [InitializeOnLoad]
    public static class LifeReviewRunner
    {
        private const string Folder = "LifeGameplayReview/20261026";
        private static double nextPoll;
        static LifeReviewRunner() { EditorApplication.update += Poll; }
        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + .5;
            string path = Folder + "/request.txt";
            if (!File.Exists(path)) return;
            string command = File.ReadAllText(path).Trim(); File.Delete(path);
            try
            {
                if (command == "state") State();
                else if (command == "gesture-checks") { LifeGestureChecks.Run(); State(); }
                else if (command.StartsWith("menu:Weather House/Review 20261027/",StringComparison.Ordinal))
                {
                    if(!EditorApplication.ExecuteMenuItem(command.Substring(5))) throw new InvalidOperationException("Review menu was not found.");
                    State();
                }
                else if (command == "spatial") LifeSpatialAudit.Run();
                else if (command == "poses") LifeSquirrelPoseAudit.Run();
                else if (command == "audio-surfaces") LifeAudioSurfaceAudit.Inspect();
                else if (command == "audio-checks") LifeAudioChecks.Run();
                else if (command == "photography-checks") { LifePhotoFlowChecks.Run(); WeatherHousePrototype.Editor.AnimalGlyphChecks.Run(); }
                else if (command == "assets")
                {
                    foreach(string assetPath in Directory.GetFiles("Assets/Game/Life/Art/Resources/LifeAssets","*.fbx",SearchOption.AllDirectories))
                        AssetDatabase.ImportAsset(assetPath.Replace('\\','/'),ImportAssetOptions.ForceUpdate);
                }
                else if (command == "play") LifePlayReview.Begin();
                else if (command == "play-ai") LifePlayReview.BeginWithAi();
                else if (command == "ai-only") LifePlayReview.BeginAiOnly();
                else if (command == "world-only") LifePlayReview.BeginWorldOnly();
                else if (command == "photo-only") LifePlayReview.BeginPhotoOnly();
                else if (command == "ui-only") LifePlayReview.BeginUiOnly();
                else if (command == "performance-only") LifePlayReview.BeginPerformanceOnly();
                else if (command == "book-only") LifePlayReview.BeginBookOnly();
                else if (command == "dialogue-only") LifePlayReview.BeginDialogueOnly();
                else if (command == "persistence-only") LifePlayReview.BeginPersistenceOnly();
                else if (command == "audio-only") LifePlayReview.BeginAudioOnly();
                else if (command == "photography-only") LifePlayReview.BeginPhotographyOnly();
                else if (command == "photography-interactions-only") LifePlayReview.BeginPhotographyInteractionsOnly();
                else if (command == "fresh-photos-only") LifePlayReview.BeginFreshPhotosOnly();
                else if (command == "roaming-only") LifePlayReview.BeginRoamingOnly();
                else if (command == "ui-capture-only") LifePlayReview.BeginUiCaptureOnly();
                else if (command == "ai") LifeAiReview.Begin();
                else if (command == "journey-checks")
                    File.WriteAllText(Folder+"/journey-existing-checks.txt",
                        WeatherHouse.WildlifeJourney.Editor.JourneyNavigationChecks.RunLogicChecks()+"\n"+
                        WeatherHouse.WildlifeJourney.Editor.JourneyHerdChecks.RunLogicChecks());
                else if (command == "checks")
                {
                    LifeCoreChecks.Run(); LifeGestureChecks.Run(); LifeWorldChecks.Run();
                    LifeProgressionChecks.Run(); LifeJourneyMoveChecks.Run();
                    File.WriteAllText(Folder+"/journey-existing-checks.txt",
                        WeatherHouse.WildlifeJourney.Editor.JourneyNavigationChecks.RunLogicChecks()+"\n"+
                        WeatherHouse.WildlifeJourney.Editor.JourneyHerdChecks.RunLogicChecks());
                    WeatherHousePrototype.LanguageJudgementChecks.Run(); State();
                }
                else throw new ArgumentException("Unknown Life review command");
                bool asynchronous=command=="play" || command=="play-ai" || command=="ai" || command.EndsWith("-only",StringComparison.Ordinal);
                File.WriteAllText(Folder + "/last-result.txt", command + (command.StartsWith("menu:",StringComparison.Ordinal) ? " DISPATCHED_FEATURE_REPORT_REQUIRED " : asynchronous ? " STARTED " : " PASS ") + DateTime.UtcNow.ToString("O"));
            }
            catch (Exception e) { File.WriteAllText(Folder + "/last-result.txt",command + " FAIL\n" + e); Debug.LogException(e); }
        }
        private static void State()
        {
            var scene = SceneManager.GetActiveScene();
            File.WriteAllText(Folder + "/editor-state.json", JsonUtility.ToJson(new StateRecord {
                scene=scene.path,playing=EditorApplication.isPlaying,dirty=scene.isDirty,
                compiling=EditorApplication.isCompiling,paused=EditorApplication.isPaused,utc=DateTime.UtcNow.ToString("O")},true));
        }
        [Serializable] private class StateRecord { public string scene,utc; public bool playing,dirty,compiling,paused; }
    }
}
