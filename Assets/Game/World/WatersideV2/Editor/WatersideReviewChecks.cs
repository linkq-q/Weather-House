using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ForestSpriteCharacter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using WeatherHouse.Vegetation;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    [InitializeOnLoad]
    public static class WatersideReviewChecks
    {
        const string Folder = WatersideSceneBuilder.Review;
        const string PlayKey = "WeatherHouse.WatersideV2.Play";
        const string PlaySettingsKey = PlayKey + ".Settings", BackgroundKey = PlayKey + ".Background", PausedKey = PlayKey + ".Paused";
        static double nextPoll, playStart;
        static Camera photo;
        static int shot, crossing;
        static Vector3 originalPosition;
        static Quaternion originalRotation;
        static ForestSpriteDemoController player;
        static HillsideGrassRenderer grass;
        static bool oldManual;
        static WatersideMood captureMood;
        static WatersideMood.Snapshot originalMood;
        static bool photoPrepared;
        static CaptureView[] captureViews;
        static string captureFolder;
        static bool restoreHouseNormals;
        static bool restoreHouseLod;
        static int phase;
        static Vector3 walkStart;
        static readonly Dictionary<Transform, Quaternion> bones = new Dictionary<Transform, Quaternion>();
        static int changedBones;
        static int soilStamps;
        static double nextTrace;
        static WatersideWaterBoundary waterBoundary;
        static Vector3 shallowEnd, deepEnd;
        static int boundaryBefore, boundaryAfter;
        static float shallowDepth;

        readonly struct CaptureView
        {
            public readonly string Name;
            public readonly Vector3 Stand;
            public readonly float Yaw, Pitch, Distance, Ahead, TargetHeight;
            public readonly WatersideMood.Mode Mood;
            public CaptureView(string name, float x, float z, float yaw, float pitch, float distance, float ahead = 0, WatersideMood.Mode mood = WatersideMood.Mode.Day, float targetHeight = .75f)
            { Name = name; Stand = new Vector3(x, 0, z); Yaw = yaw; Pitch = pitch; Distance = distance; Ahead = ahead; Mood = mood; TargetHeight = targetHeight; }
        }

        // Reference IDs are preserved so saved images can be compared without renumbering.
        static readonly CaptureView[] Views =
        {
            new CaptureView("01_晨雾石桥归家", -34.5f, -30, 45, 43, 21, mood: WatersideMood.Mode.Dawn),
            new CaptureView("02_老树分汊小院", 1.8f, -7.2f, 20, 45, 17.5f),
            new CaptureView("03_苔阶流水石渡", 11, -12.6f, 90, 47, 16.5f, 1.6f, WatersideMood.Mode.Overcast),
            new CaptureView("04_峡口短栈台", 57.5f, -43, 70, 45, 15.5f, -.3f, WatersideMood.Mode.Dusk),
            new CaptureView("05_萤火圆台静潭", -21.6f, 32.4f, 90, 43, 16, .3f, WatersideMood.Mode.Night),
            new CaptureView("10_风铃石岸蓝潭", 0, 32.4f, 270, 45, 16.5f, 1.5f, WatersideMood.Mode.Night),
            new CaptureView("13_老树背风湿地", 3, -5, 20, 45, 15, .8f, WatersideMood.Mode.Overcast),
            new CaptureView("17_礁石潮池石径", 57.45f, -32.6f, 5, 48, 18),
            new CaptureView("18_海岬草棚避雨处", 57.3f, -3.5f, 10, 43, 15, .8f, WatersideMood.Mode.Overcast),
            new CaptureView("20_月湾风铃沙台", -9, -61.2f, 225, 43, 15.5f, mood: WatersideMood.Mode.Night)
        };

        static WatersideReviewChecks()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PlayKey,false))
                { phase=0; crossing=0; playStart=EditorApplication.timeSinceStartup; nextTrace=0; Application.runInBackground=true;EditorApplication.isPaused=false; }
                if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(PlaySettingsKey,false))
                { AbortPlayIfRunning(); RestorePlaySettings(false); }
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(PlaySettingsKey,false))
                { AbortPlayIfRunning(); RestorePlaySettings(true); State(); }
            };
        }
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup<nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll=EditorApplication.timeSinceStartup+.25;
            try
            {
                if (photo) { CaptureNext(); return; }
                if (EditorApplication.isPlaying && SessionState.GetBool(PlayKey,false)) { PlayTick(); return; }
                string path=Folder+"/request.txt";
                if (!File.Exists(path)) return;
                string command=File.ReadAllText(path).Trim(); File.Delete(path); Directory.CreateDirectory(Folder);
                if(command=="inspect") State();
                else if(command=="v3-scene-copy") { if(EditorApplication.isPlaying) throw new InvalidOperationException("Copy only in Edit mode."); Directory.CreateDirectory("JourneyReview/Revision_20261024/MotionReview/SceneSnapshots"); if(!EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "JourneyReview/Revision_20261024/MotionReview/SceneSnapshots/memory-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".unity", true)) throw new IOException("Snapshot failed."); State(); }
                else if(command=="v3-perf-inventory") { System.Reflection.Assembly.LoadFile(Path.GetFullPath("PerformanceAuditReview/20261024/Optimization/PerformanceReadOnlyProbeV3.dll")).GetType("WeatherHouse.Review.PerformanceReadOnlyProbe").GetMethod("Inspect").Invoke(null,null); State(); }
                else if(command=="v3-perf-comparison") { System.Reflection.Assembly.LoadFile(Path.GetFullPath("PerformanceAuditReview/20261024/Optimization/PerformanceComparisonChecksV2.dll")).GetType("WeatherHouse.Review.PerformanceComparisonChecks").GetMethod("Run").Invoke(null,null); State(); }
                else if(command=="v3-motion-baseline") { WeatherHouse.Review.WatersideMotionPlayReview.BeginBaseline(); }
                else if(command=="v3-motion-candidate") { WeatherHouse.Review.WatersideMotionPlayReview.BeginCandidate(); }
                else if(command=="v3-water-baseline") { WeatherHouse.Review.WatersideMotionPlayReview.BeginWaterBaseline(); }
                else if(command=="v3-water-candidate") { WeatherHouse.Review.WatersideMotionPlayReview.BeginWaterCandidate(); }
                else if(command=="v3-prefab-bounds") { System.Reflection.Assembly.LoadFile(Path.GetFullPath("PerformanceAuditReview/20261024/Optimization/CapePrefabBoundsChecksV2.dll")).GetType("WeatherHouse.Review.CapePrefabBoundsChecks").GetMethod("Run").Invoke(null,null); State(); }
                else if(command=="v3-perf-baseline") { WeatherHouse.Review.WatersidePerformancePlayReview.BeginBaseline(); }
                else if(command=="v3-perf-candidate") { WeatherHouse.Review.WatersidePerformancePlayReview.BeginCandidate(); }
                else if(command=="v3-perf-reflection-aba") { WeatherHouse.Review.WatersidePerformancePlayReview.BeginReflectionAba(); }
                else if(command=="v3-reflection-land") { WeatherHouse.Review.WatersideReflectionLandingReview.Apply(); State(); }
                else if(command=="v3-fox-bind") { WeatherHouse.Review.WatersideFoxRestPlayReview.BindReviewedClips(); State(); }
                else if(command=="v3-fox-play") { WeatherHouse.Review.WatersideFoxRestPlayReview.BeginReviewed(); }
                else if(command=="v3-fox-strategy") { File.WriteAllText("JourneyReview/Revision_20261024/AnimationInstall/MainReview/strategy-final.txt", WeatherHouse.Hillside.Editor.WildlifeSolitaryStrategyChecks.RunLogicChecks()); State(); }
                else if(command=="v3-dog-bind") { System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/AnimationInstall/AdditionalSources/DogEating/CandidateV3/DogEatingBindingReview.dll")).GetType("WeatherHouse.Review.DogEatingBindingReview").GetMethod("Bind").Invoke(null,null); State(); }
                else if(command=="v3-dog-contact-code") { WeatherHouse.Review.WatersideRuralLandingReview.InstallReviewedDogContact(); State(); }
                else if(command=="v3-dog-contact-bind") { System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/AnimationInstall/AdditionalSources/DogEating/ForageContactV1/DogEatingContactBindingReviewV2.dll")).GetType("WeatherHouse.Review.DogEatingContactBindingReview").GetMethod("Bind").Invoke(null,null); State(); }
                else if(command=="v3-mage-undo-probe") { System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/MageCandidate/Review/MageMainIntegrationV2.dll")).GetType("MageMainIntegration").GetMethod("ProbeWithUndo").Invoke(null,null); State(); }
                else if(command=="v3-dog-play") { WeatherHouse.Review.WatersideFoxRestPlayReview.BeginReviewedDogEating(); }
                else if(command=="v3-mage-preflight" || command=="v3-mage-apply") { const string review="JourneyReview/Revision_20261024/MageCandidate/Review/MainIntegration/"; var guards=File.ReadAllLines(review+"baseline-guards.txt"); if(guards.Length!=2) throw new InvalidOperationException("Missing frozen Mage baseline guards."); string result=(string)System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/MageCandidate/Review/MageMainIntegrationV2.dll")).GetType("MageMainIntegration").GetMethod(command=="v3-mage-apply"?"Apply":"Preflight").Invoke(null,new object[]{guards[0].Trim(),guards[1].Trim()}); File.WriteAllText(review+command+".txt",result); State(); }
                else if(command=="v3-a06-inspect" || command=="v3-a06-apply") { WatersideSceneBuilder.RequireSaved(); var stairsResult=command=="v3-a06-inspect"?RuralA06EntrySteps.InspectCurrent():RuralA06EntrySteps.ApplyPrefab(); File.WriteAllText("JourneyReview/Revision_20261024/MapIntegrationPlan/A06EntrySteps/"+command+".json",stairsResult); State(); }
                else if(command=="v3-visual-baseline") { var baselineResultPath=RuralVisualFollowup.CaptureBaseline(File.ReadAllText("JourneyReview/Revision_20261024/MainImport/runtime-scene-sha.txt").Trim()); File.WriteAllText("JourneyReview/Revision_20261024/MapIntegrationPlan/VisualFollowupV1/baseline-path.txt",baselineResultPath); State(); }
                else if(command=="v3-visual-apply") { var result=RuralVisualFollowup.Apply(File.ReadAllText("JourneyReview/Revision_20261024/MapIntegrationPlan/VisualFollowupV1/baseline-path.txt").Trim()); File.WriteAllText("JourneyReview/Revision_20261024/MapIntegrationPlan/VisualFollowupV1/apply-result.txt",result); State(); }
                else if(command=="v3-visual-save") { var result=RuralVisualFollowup.Save(File.ReadAllText("JourneyReview/Revision_20261024/MainImport/runtime-scene-sha.txt").Trim()); File.WriteAllText("JourneyReview/Revision_20261024/MapIntegrationPlan/VisualFollowupV1/save-result.txt",result); State(); }
                else if(command=="v3-mage-play") { WeatherHouse.Review.RuralMainPlayReview.Begin(File.ReadAllText("JourneyReview/Revision_20261024/MageCandidate/Review/MainIntegration/saved-scene-sha.txt").Trim(),"mage"); }
                else if(command=="v3-mage-probe") { System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/MageCandidate/Review/MageMainIntegrationV2.dll")).GetType("MageMainIntegration").GetMethod("Probe").Invoke(null,null); State(); }
                else if(command=="v3-journey-first-plan-query") { File.WriteAllText("JourneyReview/Revision_20261024/Departure/MainJourneyReview/latest-query-path.txt",(string)System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/Departure/MainJourneyReview/JourneySpawn32CandidateProbeV1.dll")).GetType("WeatherHouse.Review.JourneySpawn32CandidateProbeV1").GetMethod("Run").Invoke(null,null)); State(); }
                else if(command=="v3-spawn32-apply") { File.WriteAllText("JourneyReview/Revision_20261024/Departure/MainJourneyReview/AuthorSpawn32V1/latest-apply.json",(string)System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/Departure/MainJourneyReview/AuthorSpawn32V1/JourneySpawn32AuthoringApply.dll")).GetType("WeatherHouse.Review.JourneySpawn32AuthoringApply").GetMethod("Apply").Invoke(null,null)); State(); }
                else if(command=="v3-herd-visual-begin") { File.WriteAllText("JourneyReview/Revision_20261024/Departure/HerdVisualObserverV1/latest-begin.txt",(string)System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/Departure/HerdVisualObserverV2/candidate.dll")).GetType("WeatherHouse.Review.JourneyHerdVisualObserver").GetMethod("Begin").Invoke(null,null)); }
                else if(command=="v3-east-tree-query") { File.WriteAllText("JourneyReview/Revision_20261024/MapIntegrationPlan/EastRouteTreeDetourV1/latest-query.json",(string)System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/MapIntegrationPlan/EastRouteTreeDetourV1/EastTreeDetourProbe.dll")).GetType("RuralEastTreeDetourProbe").GetMethod("Run").Invoke(null,new object[]{Path.GetFullPath("JourneyReview/Revision_20261024/MapIntegrationPlan/EastRouteTreeDetourV1/MainPreflightV1")})); State(); }
                else if(command=="v3-east-tree-apply" || command=="v3-east-tree-save") { File.WriteAllText("JourneyReview/Revision_20261024/MapIntegrationPlan/EastRouteTreeDetourV1/"+command+".json",(string)System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/MapIntegrationPlan/EastRouteTreeDetourV1/Candidate/EastTreeDetourPaint.dll")).GetType("RuralEastTreeDetourPaint").GetMethod(command=="v3-east-tree-apply"?"Apply":"Save").Invoke(null,new object[]{File.ReadAllText("JourneyReview/Revision_20261024/MainImport/runtime-scene-sha.txt").Trim()})); State(); }
                else if(command=="v3-mouth-entry-apply" || command=="v3-mouth-entry-save") { File.WriteAllText("JourneyReview/Revision_20261024/MapIntegrationPlan/EastRouteBridgeEntryV1/"+command+".json",(string)System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/MapIntegrationPlan/EastRouteBridgeEntryV1/Candidate/MouthBridgeEntryApply.dll")).GetType("RuralMouthBridgeEntryApply").GetMethod(command=="v3-mouth-entry-apply"?"Apply":"Save").Invoke(null,new object[]{File.ReadAllText("JourneyReview/Revision_20261024/MainImport/runtime-scene-sha.txt").Trim()})); State(); }
                else if(command=="v3-a04-rock-query") { File.WriteAllText("JourneyReview/Revision_20261024/MapIntegrationPlan/A04RouteAndEntryGrassV2/latest-query.json",(string)System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/MapIntegrationPlan/A04RouteAndEntryGrassV2/A04RockDetourProbe.dll")).GetType("RuralA04RockDetourProbe").GetMethod("Run").Invoke(null,new object[]{Path.GetFullPath("JourneyReview/Revision_20261024/MapIntegrationPlan/A04RouteAndEntryGrassV2/MainQueryV1")})); State(); }
                else if(command=="v3-a04-route-query") { File.WriteAllText("JourneyReview/Revision_20261024/MapIntegrationPlan/A04RouteAndEntryGrassV1/latest-query.json",(string)System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/MapIntegrationPlan/A04RouteAndEntryGrassV1/A04RouteProbe.dll")).GetType("RuralA04RouteProbe").GetMethod("Run").Invoke(null,new object[]{Path.GetFullPath("JourneyReview/Revision_20261024/MapIntegrationPlan/A04RouteAndEntryGrassV1/MainPreflightV1")})); State(); }
                else if(command=="v3-a04-route-apply" || command=="v3-a04-route-save") { File.WriteAllText("JourneyReview/Revision_20261024/MapIntegrationPlan/A04RouteAndEntryGrassV1/"+command+".json",(string)System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/MapIntegrationPlan/A04RouteAndEntryGrassV1/Candidate/A04RoutePaint.dll")).GetType("RuralA04RoutePaint").GetMethod(command=="v3-a04-route-apply"?"Apply":"Save").Invoke(null,new object[]{File.ReadAllText("JourneyReview/Revision_20261024/MainImport/runtime-scene-sha.txt").Trim()})); State(); }
                else if(command=="v3-journey-graph-snapshot-check") { File.WriteAllText("JourneyReview/Revision_20261024/Departure/GraphIntegration/native-snapshot-check.txt",(string)System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/Departure/GraphIntegration/GraphMergeChecks.dll")).GetType("JourneyGraphSnapshotChecks").GetMethod("Run").Invoke(null,null)); State(); }
                else if(command=="v3-journey-graph-preflight") { File.WriteAllText("JourneyReview/Revision_20261024/Departure/GraphIntegration/latest-preflight.json",JourneyAlternativeGraphIntegration.Preflight()); State(); }
                else if(command=="v3-journey-graph-apply") { var graphReport=JsonUtility.FromJson<JourneyAlternativeGraphIntegration.Report>(File.ReadAllText("JourneyReview/Revision_20261024/Departure/GraphIntegration/latest-preflight.json")); File.WriteAllText("JourneyReview/Revision_20261024/Departure/GraphIntegration/latest-apply.json",JourneyAlternativeGraphIntegration.Apply(Path.Combine(graphReport.folder,"preflight.json"))); State(); }
                else if(command=="v3-rural-play-all" || command=="v3-rural-play-herd" || command=="v3-rural-play-social" || command=="v3-rural-play-details" || command=="v3-rural-play-steps" || command=="v3-rural-play-views" || command=="v3-rural-play-coast" || command=="v3-rural-play-entrances") { var rural = JsonUtility.FromJson<RuralMapIntegration.Run>(File.ReadAllText("JourneyReview/Revision_20261024/MapIntegrationPlan/latest-execution.json")); if(rural.status!="saved-awaiting-runtime-review") throw new InvalidOperationException("Map must be saved and reviewed before Play."); var runtimeGuard="JourneyReview/Revision_20261024/MainImport/runtime-scene-sha.txt"; WeatherHouse.Review.RuralMainPlayReview.Begin(File.Exists(runtimeGuard)?File.ReadAllText(runtimeGuard).Trim():rural.sceneSavedHash,command.Substring("v3-rural-play-".Length)); }
                else if(command=="v3-rural-play-stop") { WeatherHouse.Review.RuralMainPlayReview.Stop(); }
                else if(command=="v3-rural-code" || command=="v3-rural-preflight" || command=="v3-rural-apply" || command=="v3-rural-validate" || command=="v3-rural-save" || command=="v3-rural-recover" || command=="v3-rural-protection") { WeatherHouse.Review.WatersideRuralLandingReview.Run(command); State(); }
                else if(command=="v3-rural-import") { WatersideSceneBuilder.RequireSaved(); string[] imported = RuralAssetImport.BuildAll(); Directory.CreateDirectory("JourneyReview/Revision_20261024/MapIntegrationPlan/MainImport"); File.WriteAllLines("JourneyReview/Revision_20261024/MapIntegrationPlan/MainImport/prefabs.txt", imported); State(); }
                else if(command=="v3-rural-review") { WatersideSceneBuilder.RequireSaved(); string result = RuralPrefabReview.RunAll(); File.WriteAllText("JourneyReview/Revision_20261024/MapIntegrationPlan/MainImport/prefab-review.txt",result); State(); }
                else if(command=="v3-rural-a03") { WatersideSceneBuilder.RequireSaved(); RuralAssetImport.BuildPrefab("A03"); File.WriteAllText("JourneyReview/Revision_20261024/MapIntegrationPlan/MainImport/prefab-review.txt",RuralPrefabReview.RunAll()); State(); }
                else if(command=="v3-perf-reflection-renderer-aba") { WeatherHouse.Review.WatersidePerformancePlayReview.BeginReflectionRendererAba(); }
                else if(command=="v3-lighting-readiness") { System.Reflection.Assembly.LoadFile(Path.GetFullPath("PerformanceAuditReview/20261024/Optimization/LightingReadinessProbe.dll")).GetType("WeatherHouse.Review.LightingReadinessProbe").GetMethod("Run").Invoke(null,null); State(); }
                else if(command=="v3-water-culling-checks") { System.Reflection.Assembly.LoadFile(Path.GetFullPath("PerformanceAuditReview/20261024/Optimization/ReflectionCulling/WaterCullingEnvelopeChecksV3.dll")).GetType("WeatherHouse.Review.WaterCullingEnvelopeChecks").GetMethod("Run").Invoke(null,null); State(); }
                else if(command=="v3-wind-bounds-checks") { System.Reflection.Assembly.LoadFile(Path.GetFullPath("PerformanceAuditReview/20261024/Optimization/ReflectionCulling/WindBoundsChecks.dll")).GetType("WeatherHouse.Review.WindBoundsChecks").GetMethod("Run").Invoke(null,null); State(); }
                else if(command=="v3-perf-reflection-culling-aba") { WeatherHouse.Review.WatersidePerformancePlayReview.BeginReflectionCullingAba(); }
                else if(command=="v3-fence-source") { WatersideFenceLodRepairChecks.RunSourceChecks(); WatersideFenceLodRepair.Preview(); State(); }
                else if(command=="v3-fence-read") { WatersideFenceLodRepairChecks.RunReadOnly(); State(); }
                else if(command=="v3-fence-apply") { WatersideFenceLodRepairChecks.ApplyChecked(); State(); }
                else if(command=="v3-social-fade-baseline") { string result = (string)System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/CameraReference/SocialFadeBaselineChecks.dll")).GetType("WeatherHouse.WildlifeJourney.Editor.JourneySocialChecks").GetMethod("RunLogicChecks").Invoke(null,null); File.WriteAllText("JourneyReview/Revision_20261024/CameraReference/social-baseline-result.txt",result); State(); }
                else if(command=="v3-greeting-baseline") { System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/GreetingCooldown/GreetingBaselineChecks.dll")).GetType("WeatherHousePrototype.Editor.WildlifeInteractionChecks").GetMethod("RunEditChecks").Invoke(null,null); State(); }
                else if(command=="v3-scale-placement") { System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/AnimalScale/Placement/WatersideScalePlacementAuditExact.dll")).GetType("WeatherHouse.Review.WatersideScalePlacementAudit").GetMethod("Run").Invoke(null,null); State(); }
                else if(command=="v3-animal-scale-checks") { WatersideAnimalScaleChecks.Run(); WatersideAnimalScale.Preview(); State(); }
                else if(command=="v3-animal-scale-apply") { WatersideAnimalScale.Apply(); State(); }
                else if(command=="v3-social-checks") { File.WriteAllText("JourneyReview/Revision_20261024/CameraReference/social-green-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".txt", WeatherHouse.WildlifeJourney.Editor.JourneySocialChecks.RunLogicChecks()); State(); }
                else if(command=="v3-bubble-baseline" || command=="v3-bubble-checks") { string result = (string)System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/AnimalBubble/AnimalBubbleBaselineChecks.dll")).GetType("AnimalBubbleChecks").GetMethod(command=="v3-bubble-baseline" ? "RunBaseline" : "RunChecks").Invoke(null,null); File.WriteAllText("JourneyReview/Revision_20261024/AnimalBubble/" + command + "-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".txt", result); State(); }
                else if(command=="v3-interaction-checks") { WeatherHousePrototype.Editor.WildlifeInteractionChecks.RunEditChecks(); File.WriteAllText("WildlifeInteractionReview/edit-checks.txt", "PASS read-only preview-scene interaction checks, including butterfly exclusion.\n"); State(); }
                else if(command=="v3-save-reviewed") { if(EditorApplication.isPlaying || SceneManager.GetActiveScene().path != WatersideSceneBuilder.ScenePath) throw new InvalidOperationException("Save only the reviewed V2 scene in Edit mode."); Undo.FlushUndoRecordObjects(); Directory.CreateDirectory("WatersideV3Review/baseline"); if(SceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "WatersideV3Review/baseline/user-details-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true); EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); State(); }
                else if(command=="v3-initial-placement") { System.Reflection.Assembly.LoadFile(Path.GetFullPath("JourneyReview/Revision_20261024/InitialPlacement/WildlifeInitialPlacementAudit.dll")).GetType("WeatherHouse.Review.WildlifeInitialPlacementAudit").GetMethod("Run").Invoke(null,null); State(); }
                else if(command=="v3-gameplay-install") { SaveUserDetails(); WeatherHousePrototype.Editor.WatersideGameplayInstaller.Install(); Undo.FlushUndoRecordObjects(); EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); State(); }
                else if(command=="v3-gameplay-check") { File.WriteAllText("WatersideV3Review/gameplay-ui/bindings.txt", WeatherHousePrototype.Editor.WatersideGameplayInstaller.CheckBindings()); State(); }
                else if(command=="v3-gameplay-play") { SaveUserDetails(); WeatherHousePrototype.Editor.WatersideGameplayPlayReview.Begin(); }
                else if(command=="v3-inspect") WatersideLivingSpaces.Inspect();
                else if(command=="v3-cape-sites") WatersideCapeSiteReview.Find();
                else if(command=="v3-extend") { WatersideExtendedDressing.Apply(); State(); }
                else if(command=="v3-wading-checks") WatersideWadingReview.Check();
                else if(command=="v3-wading-play") WatersideWadingPlayReview.Begin();
                else if(command=="v3-cape-inspect") ForestSpriteCharacter.Editor.ForestSpriteCapeMotionReview.Inspect();
                else if(command=="v3-cape-play") ForestSpriteCharacter.Editor.ForestSpriteCapeMotionPlayReview.Begin();
                else if(command=="grass-data-checks") WeatherHouse.Vegetation.Editor.GrassLocalPaintChecks.Run();
                else if(command=="grass-integration-checks") WeatherHouse.Vegetation.Editor.GrassLocalPaintIntegrationChecks.Run();
                else if(command=="grass-editor-checks") WeatherHouse.Hillside.Editor.HillsideGrassBrushChecks.Run();
                else if(command=="grass-open") { SaveUserDetails(); WeatherHouse.Hillside.Editor.HillsideGrassBrushWindow.SetupCurrentSceneAndShow(); Undo.FlushUndoRecordObjects(); EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); State(); }
                else if(command=="v3-cape-apply") { ForestSpriteCharacter.Editor.ForestSpriteCapeMotionReview.RepairNonFiniteBounds(); ForestSpriteCharacter.Editor.ForestSpriteCapeMotionReview.Apply(); ForestSpriteCharacter.Editor.ForestSpriteCapeMotionReview.SavePersisted(); Undo.FlushUndoRecordObjects(); EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); State(); }
                else if(command=="v3-rock-audit") { System.Reflection.Assembly.Load(File.ReadAllBytes("Temp/WatersideRockColliderAuditStaging.dll")).GetType("WeatherHouse.WatersideV2.Editor.WatersideRockColliderAuditStaging").GetMethod("Run").Invoke(null,null); State(); }
                else if(command=="v3-wall-collider") { WatersideDDrivePropsTrial.MatchAcceptedWallCollider(); Undo.FlushUndoRecordObjects(); EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); State(); }
                else if(command=="v3-tide-rims") { WatersideTidePoolExpansion.FormRockyTidePools(); Undo.FlushUndoRecordObjects(); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); State(); }
                else if(command=="v3-tide-banks") { WatersideTidePoolExpansion.RefineBanks(); Undo.FlushUndoRecordObjects(); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); State(); }
                else if(command=="v3-clean-draft") { WatersideLivingSpaces.RemoveUnsavedDraft(); State(); }
                else if(command=="v3-living") { WatersideLivingSpaces.Apply(); State(); }
                else if(command=="v3-check") { WatersideLivingSpaces.Check(); State(); }
                else if(command=="v3-capture") BeginCapture(false, "WatersideV3Review/after");
                else if(command=="v3-wide") BeginCapture(false, "WatersideV3Review/wide");
                else if(command=="v3-flight-play") WatersideFlightPlayReview.Begin();
                else if(command=="v3-water") { WatersideWaterContactReview.Apply(); State(); }
                else if(command=="v3-water-check") { WatersideWaterContactReview.Check(); State(); }
                else if(command=="v3-water-settings") { WatersideWaterAuthoring.ApplyRequested(); State(); }
                else if(command=="v3-water-settings-check") { WatersideWaterAuthoring.Check(); State(); }
                else if(command=="v3-bird-assets") { BirdsButterfliesEnhancement.PrepareAssets(); BirdsButterfliesEnhancement.CheckAssets(); State(); }
                else if(command=="v3-birds") { WatersideSceneBuilder.RequireSaved(); BirdsButterfliesEnhancement.Apply(); Undo.FlushUndoRecordObjects(); EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); State(); }
                else if(command=="v3-birds-check") { BirdsButterfliesEnhancement.Check(); State(); }
                else if(command=="v3-tide") { WatersideSceneBuilder.RequireSaved(); WatersideTidePoolExpansion.Apply(); Undo.FlushUndoRecordObjects(); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); State(); }
                else if(command=="v3-wading-route") { WatersideWadingPlayReview.DiagnoseRoute(); State(); }
                else if(command=="roof-inspect") { WeatherHouseRoofHarmony.Inspect(); State(); }
                else if(command=="roof-before") BeginCapture(true, "WatersideV3Review/roof/before");
                else if(command=="roof-apply") { WeatherHouseRoofHarmony.Apply(); State(); }
                else if(command=="roof-sparse") { WeatherHouseRoofHarmony.ApplySparseMeshes(); Undo.FlushUndoRecordObjects(); EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); State(); }
                else if(command=="v3-fox") { WatersideSceneBuilder.RequireSaved(); WeatherHouse.Hillside.Editor.WildlifeSolitaryIntegration.ApplyCurrentFoxes(Object.FindObjectsOfType<WildlifePopulation>().Single(p => p.agents != null && p.agents.Any(a => a && a.speciesId == "fox" && a.spawnId == 20))); Undo.FlushUndoRecordObjects(); EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); State(); }
                else if(command=="v3-ecology-play") WeatherHouse.WildlifeJourney.Editor.JourneyEcologyPlayReview.Begin();
                else if(command=="roof-after") BeginCapture(true, "WatersideV3Review/roof/after");
                else if(command=="house-inspect") WeatherHousePlacementReview.Inspect();
                else if(command=="house-audit") WeatherHousePlacementReview.AuditDirty();
                else if(command=="house-import") WeatherHouseV05Import.BuildPrefab();
                else if(command=="house-apply") { WeatherHouseV05Placement.Apply(); State(); }
                else if(command=="house-restore") { WeatherHouseV05Placement.Restore(); State(); }
                else if(command=="house-capture") BeginCapture(true);
                else if(command=="house-normal-probe")
                {
                    WatersideSceneBuilder.RequireSaved(); restoreHouseNormals = true;
                    WeatherHouseV05Import.SetNormalScale(0); BeginCapture(true, "WeatherHouseIntegrationReview/normal-flat");
                }
                else if(command=="house-lod-probe")
                {
                    WatersideSceneBuilder.RequireSaved(); restoreHouseLod = true;
                    WeatherHouseV05Import.ForceLod(0); BeginCapture(true, "WeatherHouseIntegrationReview/lod0");
                }
                else if(command=="build") { WatersideSceneBuilder.Build();State(); }
                else if(command=="integrate") { WatersideSceneBuilder.CompleteAnimalIntegration();State(); }
                else if(command=="lanterns") { WatersideSceneBuilder.ApplyLanternDefaults();State(); }
                else if(command=="capture") BeginCapture();
                else if(command=="verify") File.WriteAllText(Folder+"/scene-checks.txt",WatersideSceneBuilder.Verify());
                else if(command=="checks") File.WriteAllText(Folder+"/integration-checks.txt",WatersideGrassMaskChecks.Run()+"\n"+WatersideGrassMaskChecks.VerifySceneWater());
                else if(command=="soilchecks") File.WriteAllText(Folder+"/soil-checks.txt",WatersideSoilChecks.Run());
                else if(command=="props-apply" || command=="props-restore")
                {
                    WatersideSceneBuilder.RequireSaved();
                    string result=command=="props-apply" ? WatersideDDrivePropsTrial.Apply() : WatersideDDrivePropsTrial.Restore();
                    Undo.FlushUndoRecordObjects();
                    if(!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new InvalidOperationException("Could not save the V2 wall trial.");
                    File.WriteAllText(Folder+"/props-operation.txt",result+"\nSaved V2 scene after flushing the trial Undo records.");State();
                }
                else if(command=="props-check") File.WriteAllText(Folder+"/ancient-props-trial-checks.txt",WatersideDDrivePropsTrial.Verify());
                else if(command=="props-save")
                {
                    WatersideDDrivePropsTrial.Verify();Undo.FlushUndoRecordObjects();
                    if(!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new InvalidOperationException("Could not save the checked V2 wall trial.");
                    State();
                }
                else if(command=="plantscapture") File.WriteAllText(Folder+"/plant-preview-checks.txt",WatersidePlantPreview.Capture());
                else if(command=="playcheck")
                {
                    WatersideSceneBuilder.RequireSaved();
                    if(SceneManager.GetActiveScene().path!=WatersideSceneBuilder.ScenePath) throw new InvalidOperationException("Open V2 first.");
                    File.WriteAllText(Folder+"/runtime-checks.txt","RUNNING");
                    File.WriteAllText(Folder+"/runtime-trace.txt",string.Empty);
                    SessionState.SetBool(BackgroundKey,Application.runInBackground);SessionState.SetBool(PausedKey,EditorApplication.isPaused);
                    SessionState.SetBool(PlaySettingsKey,true);
                    SessionState.SetBool(PlayKey,true);EditorApplication.isPlaying=true;
                }
                else if(command=="plants")
                { WatersidePlantBuilder.Build(WatersideSceneBuilder.Output+"/Nature/Plants"); File.WriteAllText(Folder+"/plant-checks.txt",WatersidePlantBuilder.Verify(WatersideSceneBuilder.Output+"/Nature/Plants")); }
                else throw new InvalidOperationException("Unknown V2 command: "+command);
                File.WriteAllText(Folder+"/last-command.txt",command+" OK "+DateTime.UtcNow.ToString("O"));
            }
            catch(Exception e)
            {
                Directory.CreateDirectory(Folder); File.WriteAllText(Folder+"/last-command.txt","FAIL\n"+e);
                if(photo) EndCapture();
                if(restoreHouseNormals) { WeatherHouseV05Import.SetNormalScale(1); restoreHouseNormals=false; }
                if(restoreHouseLod) { WeatherHouseV05Import.ForceLod(-1); restoreHouseLod=false; }
                if(SessionState.GetBool(PlayKey,false)) { File.WriteAllText(Folder+"/runtime-checks.txt","FAIL\n"+e);FinishPlay(); }
                Debug.LogException(e);
            }
        }
        static void SaveUserDetails()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != WatersideSceneBuilder.ScenePath)
                throw new InvalidOperationException("Save the active Waterside scene only outside Play.");
            if (!scene.isDirty) return;
            Undo.FlushUndoRecordObjects();
            Directory.CreateDirectory("WatersideV3Review/baseline");
            if (!EditorSceneManager.SaveScene(scene, "WatersideV3Review/baseline/user-details-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".unity", true))
                throw new IOException("User scene backup could not be saved.");
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Current scene could not be saved.");
        }

        static void State()
        {
            File.WriteAllText(Folder+"/state.txt","scene="+SceneManager.GetActiveScene().path+"\nplaying="+EditorApplication.isPlaying+
                "\ndirty="+SceneManager.GetActiveScene().isDirty+"\ncompiling="+EditorApplication.isCompiling+"\nutc="+DateTime.UtcNow.ToString("O"));
        }
        static void BeginCapture(bool houseOnly = false, string output = null)
        {
            WatersideSceneBuilder.RequireSaved();
            if(SceneManager.GetActiveScene().path!=WatersideSceneBuilder.ScenePath) throw new InvalidOperationException("Capture only V2.");
            captureViews = houseOnly ? new[] { Views[0], new CaptureView("02_天气屋全貌", -47.6f, -33.9f, 25, 32, 24, 7.8f, targetHeight: 3.3f) } : Views;
            if(output=="WatersideV3Review/wide") captureViews=Views.Select(v=>new CaptureView(v.Name,v.Stand.x,v.Stand.z,
                v.Yaw,v.Pitch,v.Distance*1.32f,v.Ahead,v.Mood,v.TargetHeight+.5f)).ToArray();
            captureFolder = output ?? (houseOnly ? "WeatherHouseIntegrationReview/after" : Folder);
            Directory.CreateDirectory(captureFolder);
            captureMood=Object.FindObjectOfType<WatersideMood>();
            if(!captureMood)throw new InvalidOperationException("Add WatersideMood to the V2 directional light before reference capture.");
            originalMood=captureMood.CaptureState();
            player=Object.FindObjectOfType<ForestSpriteDemoController>(); originalPosition=player.transform.position;originalRotation=player.transform.rotation;
            grass=Object.FindObjectOfType<HillsideGrassRenderer>();oldManual=grass.manualCaptureOnly;grass.manualCaptureOnly=true;
            photo=new GameObject("V2 Review Camera").AddComponent<Camera>();photo.enabled=false;photo.CopyFrom(player.demoCamera);
            photo.gameObject.AddComponent<UniversalAdditionalCameraData>();photo.GetUniversalAdditionalCameraData().requiresColorTexture=true;photo.GetUniversalAdditionalCameraData().requiresDepthTexture=true;
            shot=0;photoPrepared=false;File.WriteAllText(Folder+"/capture-state.txt","RUNNING");
            File.WriteAllText(captureFolder+"/capture-layout.tsv","image\tmood\tstand_world\ttarget_world\tyaw\tpitch\tdistance\tfov\tplayer_renderer_viewport_bounds\n");
        }
        static void CaptureNext()
        {
            if(shot>captureViews.Length){EndCapture();File.WriteAllText(Folder+"/capture-state.txt","PASS");return;}
            bool overview=shot==captureViews.Length;
            CaptureView view=overview?new CaptureView("00_新地图总览",0,0,0,70,180):captureViews[shot];
            Terrain terrain=Object.FindObjectOfType<Terrain>();Vector3 p=view.Stand;
            if(!overview)
            {
                p.y=StandingHeight(terrain,p)+.08f;
                player.transform.SetPositionAndRotation(p,Quaternion.Euler(0,view.Yaw,0));
            }
            photo.orthographic=overview;photo.fieldOfView=35;photo.orthographicSize=99;
            Quaternion rotation=Quaternion.Euler(view.Pitch,view.Yaw,0);
            Vector3 ahead=Quaternion.Euler(0,view.Yaw,0)*Vector3.forward*view.Ahead;
            Vector3 target=overview?new Vector3(0,7,0):p+Vector3.up*view.TargetHeight+ahead;
            photo.transform.SetPositionAndRotation(target+rotation*Vector3.back*view.Distance,rotation);
            photo.aspect=16f/9f;
            // Let the editor apply the new ambient probe before rendering the next reference view.
            if(!photoPrepared){captureMood.Apply(view.Mood,photo);photoPrepared=true;return;}
            Render(photo,captureFolder+"/"+view.Name+".png",grass);
            File.AppendAllText(captureFolder+"/capture-layout.tsv",view.Name+"\t"+view.Mood+"\t"+p.ToString("F3")+"\t"+target.ToString("F3")+
                "\t"+view.Yaw+"\t"+view.Pitch+"\t"+view.Distance+"\t"+(overview?"orthographic":photo.fieldOfView.ToString())+
                "\t"+(overview?"overview":PlayerViewportBounds())+"\n");
            photoPrepared=false;shot++;
        }

        static float StandingHeight(Terrain terrain,Vector3 p)
        {
            float ground=WatersideLayout.GroundHeight(terrain,p.x,p.z);
            float waterLevel=WatersideLayout.WaterHeight(p.x,p.z);
            float maximum=Mathf.Max(ground+2,waterLevel+2);
            bool wet=WatersideLayout.IsWater(p.x,p.z);
            float result=ground;bool supported=!wet;
            Physics.SyncTransforms();
            foreach(RaycastHit hit in Physics.RaycastAll(new Vector3(p.x,maximum+.5f,p.z),Vector3.down,
                maximum-ground+2,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            {
                if(hit.transform.IsChildOf(player.transform)||hit.normal.y<.65f||hit.point.y>maximum||(wet&&hit.point.y<waterLevel-.05f))continue;
                if(hit.point.y>result){result=hit.point.y;supported=true;}
            }
            if(!supported)throw new InvalidOperationException("Capture needs a walkable dock/stone at "+p+"; terrain is under water.");
            return result;
        }

        static string PlayerViewportBounds()
        {
            Vector2 min=new Vector2(float.MaxValue,float.MaxValue),max=new Vector2(float.MinValue,float.MinValue);
            foreach(Renderer renderer in player.GetComponentsInChildren<Renderer>())
            {
                if(!renderer.enabled||!renderer.gameObject.activeInHierarchy)continue;
                Bounds b=renderer.bounds;
                for(int i=0;i<8;i++)
                {
                    Vector3 corner=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    Vector3 v=photo.WorldToViewportPoint(corner);
                    if(v.z<=0)continue;
                    min=Vector2.Min(min,new Vector2(v.x,v.y));max=Vector2.Max(max,new Vector2(v.x,v.y));
                }
            }
            return "min="+min.ToString("F3")+" max="+max.ToString("F3")+" height="+(max.y-min.y).ToString("F3");
        }
        static void EndCapture()
        {
            if(restoreHouseLod) { WeatherHouseV05Import.ForceLod(-1); restoreHouseLod=false; }
            if(restoreHouseNormals) { WeatherHouseV05Import.SetNormalScale(1); restoreHouseNormals=false; }
            if(player){player.transform.SetPositionAndRotation(originalPosition,originalRotation);player.demoCamera.GetComponent<VegetationTopDownCamera>().SnapToPlayer();}
            if(grass)grass.manualCaptureOnly=oldManual;
            if(captureMood)captureMood.RestoreState(originalMood);
            originalMood=null;captureMood=null;photoPrepared=false;
            if(photo)Object.DestroyImmediate(photo.gameObject);photo=null;
            if(!EditorApplication.isPlaying&&SceneManager.GetActiveScene().path==WatersideSceneBuilder.ScenePath)EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            State();
        }
        static void Render(Camera camera,string path,HillsideGrassRenderer meadow)
        {
            var target=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
            var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try{camera.targetTexture=target;camera.aspect=target.width/(float)target.height;if(meadow)meadow.DrawForCamera(camera,true);camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());}
            finally{camera.targetTexture=oldTarget;RenderTexture.active=oldActive;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);}
        }
        static void PlayTick()
        {
            double elapsed=EditorApplication.timeSinceStartup-playStart;
            if(elapsed>=nextTrace)
            {
                nextTrace=elapsed+2;
                File.AppendAllText(Folder+"/runtime-trace.txt","t="+elapsed.ToString("F1")+" frame="+Time.frameCount+" phase="+phase+" crossing="+crossing+
                    (phase==1&&crossing<TraversalCount?" route="+TraversalAt(crossing).Name:"")+
                    (player?" position="+player.transform.position.ToString("F3")+" velocity="+player.ActualVelocity.ToString("F2")+" grounded="+player.IsGrounded:"")+"\n");
            }
            if(elapsed>110)throw new TimeoutException("V2 Play traversal timed out: phase="+phase+", crossing="+crossing+", frame="+Time.frameCount+", position="+(player?player.transform.position.ToString("F3"):"none"));
            if(phase==0)
            {
                if(elapsed<5)return;
                player=Object.FindObjectOfType<ForestSpriteDemoController>();grass=Object.FindObjectOfType<HillsideGrassRenderer>();
                var population=Object.FindObjectsOfType<WildlifePopulation>().FirstOrDefault(p=>p.agents!=null&&p.agents.Length==70);
                var herd=Object.FindObjectOfType<WeatherHouse.WildlifeJourney.JourneyHerd>();
                if(!population||!population.Initialized||!herd||herd.members.Length!=10||herd.SimulationTime<=0)
                    throw new InvalidOperationException("70 wildlife and 10 Journey sheep did not initialize.");
                changedBones=0; bones.Clear();foreach(var a in population.agents)foreach(var t in a.animator.GetComponentsInChildren<Transform>())bones[t]=t.localRotation;
                foreach(var member in herd.members)foreach(var t in member.animator.GetComponentsInChildren<Transform>())bones[t]=t.localRotation;
                Render(player.demoCamera,Folder+"/08_实际游戏镜头.png",grass);
                var controller=player.GetComponent<CharacterController>();
                controller.enabled=false;Vector3 start=WatersideLayout.Routes[0][0];
                var ground=Object.FindObjectOfType<Terrain>();start.y=WatersideLayout.GroundHeight(ground,start.x,start.z)+.12f;
                player.transform.position=start;controller.enabled=true;
                player.demoCamera.GetComponent<VegetationTopDownCamera>().SnapToPlayer();
                soilStamps=Object.FindObjectOfType<WatersideSoilInteraction>().StampCount;
                phase=2;return;
            }
            if(phase==2)
            {
                Vector3 delta=WatersideLayout.Routes[0][6]-player.transform.position;delta.y=0;
                if(delta.magnitude<.6f)
                {
                    player.ReleaseScriptedInput();var soil=Object.FindObjectOfType<WatersideSoilInteraction>();
                    int marks=soil.StampCount-soilStamps;
                    if(marks<2||!soil.CurrentTexture||!soil.CurrentTexture.GetPixels32().Any(c=>c.r>20))
                        throw new InvalidOperationException("Actual CharacterController movement did not produce visible soil impressions; marks="+marks);
                    soilStamps=marks;
                    Render(player.demoCamera,Folder+"/09_实际泥土脚印.png",grass);
                    CaptureSoilCloseup(soil);
                    BeginWaterTraversal();return;
                }
                Vector3 forward=Vector3.ProjectOnPlane(player.demoCamera.transform.forward,Vector3.up).normalized,right=Vector3.Cross(Vector3.up,forward);
                player.SetScriptedInput(new Vector2(Vector3.Dot(delta.normalized,right),Vector3.Dot(delta.normalized,forward)),false,false);
                return;
            }
            if(phase==3||phase==4)
            {
                var body=player.GetComponent<CharacterController>();
                Vector3 sole=new Vector3(player.transform.position.x,body.bounds.min.y,player.transform.position.z);
                float depth=waterBoundary.surface.Height(sole)-sole.y;
                Vector3 delta=(phase==3?shallowEnd:deepEnd)-player.transform.position;delta.y=0;
                if(phase==3&&waterBoundary.BlockedStepCount!=boundaryBefore)
                    throw new InvalidOperationException("Shallow-water walking was incorrectly blocked.");
                if(phase==3&&delta.magnitude<.25f&&player.IsGrounded)
                {
                    if(!waterBoundary.HasSafePosition||depth<.02f||depth>waterBoundary.maximumWadingDepth||
                        Vector3.ProjectOnPlane(player.transform.position-walkStart,Vector3.up).magnitude<1)
                        throw new InvalidOperationException("Shallow-water check did not produce a supported underwater walk: depth="+depth);
                    shallowDepth=depth;
                    File.AppendAllText(Folder+"/runtime-trace.txt","PASS shallow water: actual walking depth="+depth.ToString("F3")+" position="+player.transform.position.ToString("F3")+"\n");
                    phase=4;
                }
                else if(phase==4&&waterBoundary.BlockedStepCount>boundaryBefore)
                {
                    if(waterBoundary.LastRejectedDepth<=waterBoundary.maximumWadingDepth||depth>waterBoundary.maximumWadingDepth+.06f||
                        Vector3.Distance(player.transform.position,waterBoundary.LastSafePosition)>.15f)
                        throw new InvalidOperationException("Deep-water movement was not constrained at safe footing.");
                    player.ReleaseScriptedInput();boundaryAfter=waterBoundary.BlockedStepCount;
                    File.AppendAllText(Folder+"/runtime-trace.txt","PASS deep water: constrained depth="+waterBoundary.LastRejectedDepth.ToString("F3")+" position="+player.transform.position.ToString("F3")+" rollbacks="+waterBoundary.RollbackCount+"\n");
                    phase=1;crossing=0;TeleportCrossing();return;
                }
                Vector3 forward=Vector3.ProjectOnPlane(player.demoCamera.transform.forward,Vector3.up).normalized,right=Vector3.Cross(Vector3.up,forward);
                player.SetScriptedInput(new Vector2(Vector3.Dot(delta.normalized,right),Vector3.Dot(delta.normalized,forward)),false,false);
                return;
            }
            if(phase==1)
            {
                if(waterBoundary.BlockedStepCount!=boundaryAfter)throw new InvalidOperationException("Water boundary incorrectly rejected a bridge or stair route.");
                var route=TraversalAt(crossing);bool stairs=crossing>=WatersideLayout.Crossings.Length;
                Vector3 delta=route.End-player.transform.position;delta.y=0;
                foreach(var pair in bones)if(pair.Key&&Quaternion.Angle(pair.Value,pair.Key.localRotation)>2)changedBones++;
                bones.Clear();
                if(delta.magnitude<(stairs?.35f:.7f))
                {
                    if(stairs&&(player.transform.position.y<route.End.y-.22f||player.transform.position.y-walkStart.y<route.End.y-route.Start.y-.35f))
                        throw new InvalidOperationException("Player reached the stair endpoint horizontally without climbing its 3.5 m rise: "+player.transform.position);
                    if(!player.IsGrounded){player.SetScriptedInput(Vector2.zero,false,false);return;}
                    File.AppendAllText(Folder+"/runtime-trace.txt","PASS "+(stairs?"stair":"crossing")+" "+route.Name+" start="+walkStart.ToString("F3")+" end="+player.transform.position.ToString("F3")+"\n");
                    player.ReleaseScriptedInput();crossing++;
                    if(crossing>=TraversalCount)
                    {
                        if(changedBones<8)throw new InvalidOperationException("Skeleton poses did not advance.");
                        File.WriteAllText(Folder+"/runtime-checks.txt","PASS\n80 animals initialized (70 original agents + 10 Journey sheep); skeleton changes="+changedBones+"\nActual walking soil impressions="+soilStamps+
                            "\nWater crossings traversed by the original CharacterController: "+WatersideLayout.Crossings.Length+
                            "\nStair routes traversed by the original CharacterController: "+WatersideLayout.Steps.Length+" (3.5 m ascent verified)."+
                            "\nShallow-water walking allowed at "+shallowDepth.ToString("F3")+" m; deep-water movement constrained ("+boundaryAfter+" step(s)); no rollback; bridges/stairs caused no rejection.\nPlayer, cape and near top-down camera remained active.\n");
                        FinishPlay();return;
                    }
                    TeleportCrossing();return;
                }
                Vector3 forward=Vector3.ProjectOnPlane(player.demoCamera.transform.forward,Vector3.up).normalized,right=Vector3.Cross(Vector3.up,forward);
                player.SetScriptedInput(new Vector2(Vector3.Dot(delta.normalized,right),Vector3.Dot(delta.normalized,forward)),!stairs,false);
                if(player.transform.position.y<Mathf.Min(route.Start.y,route.End.y)-.5f)throw new InvalidOperationException("Player fell under crossing "+route.Name);
            }
        }
        static int TraversalCount => WatersideLayout.Crossings.Length+WatersideLayout.Steps.Length;
        static WatersideLayout.Crossing TraversalAt(int index) => index<WatersideLayout.Crossings.Length
            ? WatersideLayout.Crossings[index] : WatersideLayout.Steps[index-WatersideLayout.Crossings.Length];
        static void CaptureSoilCloseup(WatersideSoilInteraction soil)
        {
            var block=new MaterialPropertyBlock();soil.terrain.GetSplatMaterialPropertyBlock(block);
            Vector4 rect=block.GetVector("_SoilFieldRect");var texture=soil.CurrentTexture;Color32[] pixels=texture.GetPixels32();
            Bounds[] occluders=Object.FindObjectsOfType<MeshRenderer>().Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&
                r.gameObject.layer!=4&&!r.transform.IsChildOf(player.transform)&&
                !r.sharedMaterials.Any(m=>m&&m.shader&&m.shader.name=="Weather House/Painterly Grass"))
                .Select(r=>{Bounds b=r.bounds;b.Expand(new Vector3(.32f,0,.32f));return b;}).ToArray();
            Vector3 center=Vector3.zero;int strongest=0;
            for(int i=0;i<pixels.Length;i++)
            {
                if(pixels[i].r<=strongest)continue;
                Vector3 p=new Vector3(rect.x+(i%texture.width+.5f)/texture.width/rect.z,0,rect.y+(i/texture.width+.5f)/texture.height/rect.w);
                float distance=Vector3.ProjectOnPlane(p-player.transform.position,Vector3.up).magnitude;
                if(distance<1.2f||distance>2.8f||occluders.Any(b=>p.x>=b.min.x&&p.x<=b.max.x&&p.z>=b.min.z&&p.z<=b.max.z))continue;
                center=p;strongest=pixels[i].r;
            }
            if(strongest<40)throw new InvalidOperationException("No recent foot impression 1.2–2.8 m from the player is clear of visible rocks/props for the close-up render.");
            center.y=WatersideLayout.GroundHeight(soil.terrain,center.x,center.z);
            var close=new GameObject("Runtime soil close-up camera").AddComponent<Camera>();
            Renderer[] character=player.GetComponentsInChildren<Renderer>(true);bool[] enabled=character.Select(r=>r.enabled).ToArray();
            float originalStrength=block.GetFloat("_SoilInteractionStrength");
            try
            {
                foreach(Renderer renderer in character)renderer.enabled=false;
                close.enabled=false;close.CopyFrom(player.demoCamera);close.enabled=false;
                close.gameObject.AddComponent<UniversalAdditionalCameraData>();
                close.GetUniversalAdditionalCameraData().requiresColorTexture=true;close.GetUniversalAdditionalCameraData().requiresDepthTexture=true;
                close.orthographic=true;close.orthographicSize=3f/(2f*16f/9f);close.nearClipPlane=.05f;close.farClipPlane=15;
                close.transform.SetPositionAndRotation(center+Vector3.up*5,Quaternion.LookRotation(Vector3.down,Vector3.ProjectOnPlane(player.transform.forward,Vector3.up).normalized));
                // Render the same frozen frame with one property changed. A repeated disabled frame measures rendering noise.
                block.SetFloat("_SoilInteractionStrength",0);soil.terrain.SetSplatMaterialPropertyBlock(block);
                Color32[] warmup=RenderSoilPixels(close,null),before=RenderSoilPixels(close,Folder+"/09c_泥土脚印关闭对照.png");
                block.SetFloat("_SoilInteractionStrength",originalStrength);soil.terrain.SetSplatMaterialPropertyBlock(block);
                Color32[] after=RenderSoilPixels(close,Folder+"/09b_泥土脚印近景.png");
                int noise=ChangedSoilPixels(warmup,before,close,center,texture,rect,out int ignored);
                int changed=ChangedSoilPixels(before,after,close,center,texture,rect,out int peakDifference);
                string report="Soil GPU comparison: center="+center.ToString("F3")+" field="+texture.width+"x"+texture.height+" markPeak="+strongest+
                    " width=3m; changed footprint pixels="+changed+"; repeated-frame noise="+noise+"; maximum RGB delta="+peakDifference+"\n";
                File.WriteAllText(Folder+"/soil-gpu-checks.txt",report);
                File.AppendAllText(Folder+"/runtime-trace.txt",report);
                if(originalStrength<=0||changed<100+noise*3||peakDifference<4)
                    throw new InvalidOperationException("Footprint shader did not measurably change the rendered soil pixels. "+report);
                File.AppendAllText(Folder+"/soil-gpu-checks.txt","PASS: live Terrain shader changes pixels within the actual footprint field.\n");
            }
            finally
            {
                block.SetFloat("_SoilInteractionStrength",originalStrength);soil.terrain.SetSplatMaterialPropertyBlock(block);
                for(int i=0;i<character.Length;i++)if(character[i])character[i].enabled=enabled[i];
                Object.DestroyImmediate(close.gameObject);
            }
        }
        static Color32[] RenderSoilPixels(Camera camera,string path)
        {
            var target=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
            var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=target;camera.aspect=16f/9f;if(grass)grass.DrawForCamera(camera,true);camera.Render();
                RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
                if(path!=null)File.WriteAllBytes(path,pixels.EncodeToPNG());
                return pixels.GetPixels32();
            }
            finally{camera.targetTexture=oldTarget;RenderTexture.active=oldActive;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);}
        }
        static int ChangedSoilPixels(Color32[] before,Color32[] after,Camera camera,Vector3 center,Texture2D marks,Vector4 rect,out int peak)
        {
            int count=0;peak=0;Vector3 right=camera.transform.right*3,up=camera.transform.up*(3f*9f/16f);
            for(int i=0;i<before.Length;i++)
            {
                int difference=Mathf.Max(Mathf.Abs(before[i].r-after[i].r),Mathf.Abs(before[i].g-after[i].g),Mathf.Abs(before[i].b-after[i].b));
                if(difference<3)continue;
                Vector3 p=center+right*((i%1600+.5f)/1600-.5f)+up*((i/1600+.5f)/900-.5f);
                float u=(p.x-rect.x)*rect.z,v=(p.z-rect.y)*rect.w;
                if(u<0||u>1||v<0||v>1||marks.GetPixelBilinear(u,v).r<.05f)continue;
                count++;peak=Mathf.Max(peak,difference);
            }
            return count;
        }
        static void BeginWaterTraversal()
        {
            waterBoundary=player.GetComponent<WatersideWaterBoundary>();
            if(!waterBoundary||!waterBoundary.enabled||!waterBoundary.surface)throw new InvalidOperationException("Player requires the V2 water boundary and full water provider.");
            Terrain ground=waterBoundary.terrain;var body=player.GetComponent<CharacterController>();
            Physics.SyncTransforms();
            // Find a clear coastal strip in the saved terrain; never substitute a mock floor or bypass the controller.
            for(int segment=1;segment<WatersideLayout.Coastline.Length;segment++)
            {
                Vector3 a=WatersideLayout.Coastline[segment-1],b=WatersideLayout.Coastline[segment];
                Vector3 tangent=(b-a).normalized,outward=Vector3.Cross(Vector3.up,tangent);
                for(int sample=1;sample<=7;sample++)
                {
                    Vector3 coast=Vector3.Lerp(a,b,sample/8f);
                    if(WatersideLayout.SeaSignedDistance(coast.x+outward.x,coast.z+outward.z)>0)outward=-outward;
                    for(float offset=.35f;offset<=1.2f;offset+=.1f)
                    {
                        Vector3 start=coast+outward*offset-tangent*.75f,end=start+tangent*1.5f;
                        bool clear=true;
                        for(int s=0;s<=6&&clear;s++)
                        {
                            Vector3 p=Vector3.Lerp(start,end,s/6f);p.y=WatersideLayout.GroundHeight(ground,p.x,p.z);
                            float depth=waterBoundary.surface.Height(p)-p.y;
                            clear=waterBoundary.surface.Contains(p)&&depth>.10f&&depth<.28f&&ClearCapsule(p,body);
                        }
                        Vector3 deep=end+outward*2.5f;deep.y=WatersideLayout.GroundHeight(ground,deep.x,deep.z);
                        if(!clear||waterBoundary.surface.Height(deep)-deep.y<.7f)continue;
                        for(int s=1;s<=6&&clear;s++)
                        {Vector3 p=Vector3.Lerp(end,deep,s/6f);p.y=WatersideLayout.GroundHeight(ground,p.x,p.z);clear=ClearCapsule(p,body);}
                        if(!clear)continue;
                        start.y=WatersideLayout.GroundHeight(ground,start.x,start.z)+.06f;
                        body.enabled=false;player.transform.position=start;body.enabled=true;
                        player.demoCamera.GetComponent<VegetationTopDownCamera>().SnapToPlayer();
                        walkStart=start;shallowEnd=end;deepEnd=deep;boundaryBefore=waterBoundary.BlockedStepCount;phase=3;
                        File.AppendAllText(Folder+"/runtime-trace.txt","Water test strip: start="+start.ToString("F3")+" shallowEnd="+end.ToString("F3")+" deepEnd="+deep.ToString("F3")+"\n");
                        return;
                    }
                }
            }
            throw new InvalidOperationException("No clear .10–.28 m shallow coastal strip found for real-controller water checks.");
        }
        static bool ClearCapsule(Vector3 ground,CharacterController body)
        {
            Vector3 size=body.bounds.size;float radius=Mathf.Max(size.x,size.z)*.5f;
            Vector3 lower=ground+Vector3.up*(radius+.06f),upper=ground+Vector3.up*Mathf.Max(radius+.06f,size.y-radius+.06f);
            foreach(Collider hit in Physics.OverlapCapsule(lower,upper,radius,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                if(!hit.transform.IsChildOf(player.transform)&&!(hit is TerrainCollider))return false;
            return true;
        }
        static void TeleportCrossing()
        {
            var route=TraversalAt(crossing);var controller=player.GetComponent<CharacterController>();
            controller.enabled=false;player.transform.position=route.Start+Vector3.up*.12f;controller.enabled=true;
            player.demoCamera.GetComponent<VegetationTopDownCamera>().SnapToPlayer();walkStart=player.transform.position;
        }
        static void FinishPlay()
        {
            if(player)player.ReleaseScriptedInput();
            RestorePlaySettings(false);
            SessionState.SetBool(PlayKey,false);EditorApplication.isPlaying=false;
        }
        static void AbortPlayIfRunning()
        {
            if(!SessionState.GetBool(PlayKey,false))return;
            File.WriteAllText(Folder+"/runtime-checks.txt","ABORTED\nPlay ended before traversal checks completed; no PASS result.\n");
            SessionState.SetBool(PlayKey,false);
            if(player)player.ReleaseScriptedInput();
        }
        static void RestorePlaySettings(bool complete)
        {
            if(!SessionState.GetBool(PlaySettingsKey,false))return;
            Application.runInBackground=SessionState.GetBool(BackgroundKey,false);
            EditorApplication.isPaused=SessionState.GetBool(PausedKey,false);
            if(complete)SessionState.SetBool(PlaySettingsKey,false);
        }
    }
}
