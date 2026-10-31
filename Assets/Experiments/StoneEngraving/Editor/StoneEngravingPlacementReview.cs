using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using ForestSpriteCharacter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using WeatherHouse.WatersideV2;
using WeatherHouse.Vegetation;
using Object = UnityEngine.Object;

namespace WeatherHouse.StoneEngraving.Editor
{
    // Bounded review commands for the confirmed rain/water sample, not a general scene editor.
    [InitializeOnLoad]
    public static class StoneEngravingPlacementReview
    {
        const string ScenePath = "Assets/Scenes/WeatherHouse_WatersideV2.unity";
        const string PrefabPath = "Assets/Experiments/StoneEngraving/RainWater_ExistingRock.prefab";
        const string Folder = "StoneEngravingReview/20261029/MainScene";
        const string StoneName = "雨与水石刻 · 天气屋旁";
        const string Key = "WeatherHouse.StonePlacement";
        static double nextPoll;

        static StoneEngravingPlacementReview()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += PlayChanged;
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + .5;
            string path = Folder + "/command.txt";
            if (!File.Exists(path)) return;
            string command = File.ReadAllText(path).Trim(); File.Delete(path);
            try
            {
                switch (command)
                {
                    case "inspect": Inspect(); break;
                    case "place": Place(); break;
                    case "checks": StartPlay(true); break;
                    case "readability-checks": StartPlay(true, true); break;
                    case "play": StartPlay(false); break;
                    case "stop":
                        Require(SessionState.GetBool(Key+".owned",false),"No stone preview owns Play.");
                        EditorApplication.isPlaying = false; break;
                    default: throw new InvalidOperationException("Unknown stone placement command: " + command);
                }
                File.WriteAllText(Folder + "/status.txt", "OK " + command + "\n" + State());
            }
            catch (Exception e) { File.WriteAllText(Folder + "/status.txt", "FAIL " + command + "\n" + e + "\n" + State()); Debug.LogException(e); }
        }

        static string State() => DateTime.UtcNow.ToString("O") + "\nscene=" + SceneManager.GetActiveScene().path +
            "\nplaying=" + EditorApplication.isPlaying + "\ndirty=" + SceneManager.GetActiveScene().isDirty;

        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static ForestSpriteDemoController Player => Object.FindObjectOfType<ForestSpriteDemoController>();
        static Terrain Ground => Object.FindObjectsOfType<Terrain>().First(t => t.gameObject.scene.path == ScenePath);
        static GameObject Stone => GameObject.Find(StoneName);

        static void Place()
        {
            Require(SceneManager.GetActiveScene().path == ScenePath && !EditorApplication.isPlayingOrWillChangePlaymode,
                "Placement requires the main scene in Edit mode.");
            Require(!SceneManager.GetActiveScene().isDirty && !Stone,"Preserve existing scene changes; do not duplicate the sample.");
            string backup = Folder + "/before-placement.unity";
            Require(!File.Exists(backup),"The original placement backup already exists; inspect it before another placement.");
            Directory.CreateDirectory(Folder); File.Copy(ScenePath,backup);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Require(prefab,"Missing confirmed stone prefab.");
            var stone = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                stone.name = StoneName;
                var renderer = stone.GetComponent<MeshRenderer>(); var mesh = stone.GetComponent<MeshFilter>().sharedMesh;
                stone.transform.rotation = Quaternion.identity;
                Vector3 center = new Vector3(-36.5f,0,-40.5f);
                stone.transform.position += center - renderer.bounds.center;
                float floor = float.MaxValue;
                Bounds bounds = renderer.bounds;
                for (int z=0;z<=4;z++) for (int x=0;x<=4;x++)
                {
                    Vector3 p = new Vector3(Mathf.Lerp(bounds.min.x,bounds.max.x,x/4f),0,Mathf.Lerp(bounds.min.z,bounds.max.z,z/4f));
                    floor = Mathf.Min(floor,WatersideLayout.GroundHeight(Ground,p.x,p.z));
                }
                // Seat the unchanged boulder in the existing ground rather than flattening the terrain.
                stone.transform.position += Vector3.up * (floor - .35f - bounds.min.y);
                var collider = stone.AddComponent<MeshCollider>(); collider.sharedMesh = mesh; collider.convex = false;
                Physics.SyncTransforms();
                ValidatePlacement(stone);
                PrefabUtility.RecordPrefabInstancePropertyModifications(stone);
                PrefabUtility.RecordPrefabInstancePropertyModifications(stone.transform);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Require(EditorSceneManager.SaveScene(SceneManager.GetActiveScene()),"Could not save the stone placement.");
                File.Copy(ScenePath,Folder + "/after-placement.unity",true);
                Selection.activeGameObject = stone;
                File.WriteAllText(Folder+"/placement.txt",PlacementReport(stone));
            }
            catch { Object.DestroyImmediate(stone); throw; }
        }

        static void ValidatePlacement(GameObject stone)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var mesh = stone.GetComponent<MeshFilter>().sharedMesh; var collider = stone.GetComponent<MeshCollider>();
            Require(mesh == prefab.GetComponent<MeshFilter>().sharedMesh &&
                stone.GetComponent<Renderer>().sharedMaterial == prefab.GetComponent<Renderer>().sharedMaterial &&
                stone.transform.localScale == prefab.transform.localScale,"Keep the confirmed mesh, scale, and material.");
            Require(collider && collider.enabled && !collider.isTrigger && !collider.convex && collider.sharedMesh == mesh,"Static collision must use the real rock mesh.");
            Bounds b = stone.GetComponent<Renderer>().bounds; int surface = 0; float clearance = float.MaxValue;
            for (float z=b.min.z;z<=b.max.z;z+=.25f) for (float x=b.min.x;x<=b.max.x;x+=.25f)
            {
                if (!collider.Raycast(new Ray(new Vector3(x,b.max.y+1,z),Vector3.down),out var hit,b.size.y+2)) continue;
                if (hit.point.y < WatersideLayout.GroundHeight(Ground,x,z)) continue;
                surface++; Require(!WatersideLayout.IsWater(x,z),"The exposed stone must stay on land.");
                clearance = Mathf.Min(clearance,WatersideLayout.PathDistance(x,z)-WatersideLayout.WalkableHalfWidth(x,z));
            }
            Require(surface > 50 && clearance > .5f,"The exposed rock must leave the authored road clear; clearance="+clearance);
            Require(b.min.y < WatersideLayout.GroundHeight(Ground,b.center.x,b.center.z),"The rock must be seated in the ground.");
        }

        static string PlacementReport(GameObject stone) =>
            State()+"\nposition="+stone.transform.position.ToString("F4")+"\nrotation="+stone.transform.eulerAngles.ToString("F2")+
            "\nscale="+stone.transform.localScale.ToString("F5")+"\nbounds="+stone.GetComponent<Renderer>().bounds+
            "\nPASS unchanged source mesh / material / scale; mesh collision; exposed footprint clear of road and water; grounded";

        [MenuItem("Weather House/试玩/雨与水石刻（独立存档）")]
        public static void Preview() => StartPlay(false);

        [MenuItem("Weather House/检查/石刻俯视可辨识度")]
        public static void CheckReadability() => StartPlay(true, true);

        static void StartPlay(bool checks, bool readability = false)
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && (readability || !SceneManager.GetActiveScene().isDirty),
                "Finish the current Play and preserve unsaved edits before stone preview.");
            Require(SceneManager.GetActiveScene().path == ScenePath && Stone,"Open the placed Waterside scene first.");
            if (!readability) ValidatePlacement(Stone);
            else
            {
                Require(Stone.GetComponent<StoneEngravingReadability>(),"Current stone must inherit the readability component.");
                File.WriteAllText(Folder+"/readability-start.txt",State()+"\nstoneScale="+Stone.transform.localScale+
                    "\nstoneBounds="+Stone.GetComponent<Renderer>().bounds+"\nplayer="+Player.transform.position);
            }
            SessionState.SetString(Key+".save",Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE") ?? "");
            SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Key+".background",Application.runInBackground);
            SessionState.SetBool(Key+".checks",checks); SessionState.SetBool(Key+".owned",true);
            SessionState.SetBool(Key+".readability",readability);
            Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",Path.GetFullPath(Folder+"/isolated-save"));
            // Current-scene Play preserves unsaved editor state; never reload/save it for a material check.
            EditorSceneManager.playModeStartScene = readability ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Application.runInBackground = true; EditorApplication.isPlaying = true;
        }

        static void PlayChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key+".owned",false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) Player.StartCoroutine(ReviewPlay());
            if (state != PlayModeStateChange.EnteredEditMode) return;
            string save = SessionState.GetString(Key+".save","");
            Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",string.IsNullOrEmpty(save)?null:save);
            string start = SessionState.GetString(Key+".start","");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(start)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
            Application.runInBackground = SessionState.GetBool(Key+".background",false);
            SessionState.SetBool(Key+".owned",false);
            File.WriteAllText(Folder+"/handoff.txt",State()+"\nPreview save override restored; original player/camera restored by exiting Play.");
        }

        static IEnumerator ReviewPlay()
        {
            // Let normal map startup finish before arranging only the player's review viewpoint.
            yield return new WaitForSecondsRealtime(3);
            var player = Player; bool checks = SessionState.GetBool(Key+".checks",false);
            double deadline = EditorApplication.timeSinceStartup + 180;
            while (ShaderUtil.anythingCompiling && EditorApplication.timeSinceStartup < deadline) yield return null;
            try
            {
                Require(!ShaderUtil.anythingCompiling && !ShaderUtil.ShaderHasError(Stone.GetComponent<Renderer>().sharedMaterial.shader),"Stone shader must finish successfully.");
                if (!SessionState.GetBool(Key+".readability",false)) ValidatePlacement(Stone);
                if (checks) Capture(player.demoCamera,Folder+"/01-play-arrival.png");
                var b = Stone.GetComponent<Renderer>().bounds;
                Vector3 stand = b.center + Vector3.back * 5.2f;
                stand.y = WatersideLayout.GroundHeight(Ground,stand.x,stand.z)+.15f;
                var capsule = player.GetComponent<CharacterController>(); capsule.enabled = false;
                player.transform.position = stand; capsule.enabled = true; Physics.SyncTransforms();
                var orbit = player.demoCamera.GetComponent<VegetationTopDownCamera>();
                orbit.yawOffset = -orbit.baseYaw; orbit.SnapToPlayer();
                var gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                if (gameView != null) EditorWindow.GetWindow(gameView).Focus();
            }
            catch (Exception e)
            {
                File.WriteAllText(Folder+"/play-checks.txt","FAIL\n"+e); Debug.LogException(e);
                EditorApplication.isPlaying = false; yield break;
            }
            yield return new WaitForSecondsRealtime(1);
            if (!checks) yield break;
            try
            {
                if (SessionState.GetBool(Key+".readability",false))
                {
                    StoneEngravingReadabilityChecks.Run(Stone, player.demoCamera, Folder+"/Readability");
                    yield break;
                }
                Capture(player.demoCamera,Folder+"/02-play-overhead.png");
                var go = new GameObject("Stone close review camera (temporary)") { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    var camera = go.AddComponent<Camera>(); camera.CopyFrom(player.demoCamera); camera.enabled = false;
                    Vector3 target = Stone.GetComponent<Renderer>().bounds.center + Vector3.up * .45f;
                    Quaternion rotation = Quaternion.Euler(48,0,0);
                    camera.transform.SetPositionAndRotation(target+rotation*Vector3.back*13,rotation);
                    go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
                    Capture(camera,Folder+"/03-play-close.png");
                }
                finally { Object.DestroyImmediate(go); }
                File.WriteAllText(Folder+"/play-checks.txt","PASS\n"+PlacementReport(Stone)+
                    "\nActual main scene Play: original arrival camera, nearby player camera, temporary close review camera.\n"+
                    "No glyph rewards or puzzle recognition were added. Player photo framing is not an acceptance test in this review.");
            }
            catch (Exception e) { File.WriteAllText(Folder+"/play-checks.txt","FAIL\n"+e); Debug.LogException(e); }
            finally { EditorApplication.isPlaying = false; }
        }

        static void Inspect()
        {
            Require(SceneManager.GetActiveScene().path == ScenePath && !EditorApplication.isPlayingOrWillChangePlaymode,
                "Inspect requires the actual Waterside scene in Edit mode.");
            Require(!SceneManager.GetActiveScene().isDirty, "Preserve unsaved main scene edits before placement review.");
            Directory.CreateDirectory(Folder);
            var player = Player; Require(player && player.demoCamera, "Missing current player/camera.");
            var text = new StringBuilder(State()).AppendLine().AppendLine("player=" + player.transform.position.ToString("F3"))
                .AppendLine("camera=" + player.demoCamera.transform.position.ToString("F3"))
                .AppendLine("rotation=" + player.demoCamera.transform.eulerAngles.ToString("F2"));
            var center = player.transform.position;
            foreach (var collider in Object.FindObjectsOfType<Collider>().Where(c => c.enabled &&
                (c.bounds.ClosestPoint(center) - center).sqrMagnitude < 400 && !(c is TerrainCollider)))
                text.AppendLine("collider " + collider.name + " " + collider.bounds.center.ToString("F2") + " size=" + collider.bounds.size.ToString("F2"));
            text.AppendLine("candidate center X Z ground pathDistance halfWidth water");
            for (float z = center.z - 12; z <= center.z + 12; z += 4)
                for (float x = center.x - 12; x <= center.x + 12; x += 4)
                    text.AppendLine($"{x:F2} {z:F2} {WatersideLayout.GroundHeight(Ground,x,z):F2} {WatersideLayout.PathDistance(x,z):F2} {WatersideLayout.WalkableHalfWidth(x,z):F2} {WatersideLayout.IsWater(x,z)}");
            File.WriteAllText(Folder + "/inspect.txt", text.ToString());
            Capture(player.demoCamera, Folder + "/00-before-game-camera.png");
            var go = new GameObject("Stone placement overview (temporary)") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var camera = go.AddComponent<Camera>(); camera.CopyFrom(player.demoCamera); camera.enabled = false;
                camera.orthographic = true; camera.orthographicSize = 23;
                camera.transform.SetPositionAndRotation(center + Vector3.up * 70, Quaternion.Euler(90,0,0));
                go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
                Capture(camera, Folder + "/00-before-overview.png");
            }
            finally { Object.DestroyImmediate(go); }
            Require(!SceneManager.GetActiveScene().isDirty, "Inspection dirtied the scene.");
        }

        internal static Color32[] Capture(Camera camera, string path, int width = 1600, int height = 900)
        {
            Require(!ShaderUtil.anythingCompiling, "Wait for shader compilation before capturing.");
            var target = new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active; float oldAspect = camera.aspect;
            var pixels = new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture = target; camera.aspect = target.width / (float)target.height;
                foreach (var meadow in Object.FindObjectsOfType<HillsideGrassRenderer>()) meadow.DrawForCamera(camera,true);
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0); pixels.Apply();
                File.WriteAllBytes(path,pixels.EncodeToPNG());
                return pixels.GetPixels32();
            }
            finally
            {
                camera.targetTexture = oldTarget; camera.aspect = oldAspect; RenderTexture.active = oldActive;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }
    }
}
