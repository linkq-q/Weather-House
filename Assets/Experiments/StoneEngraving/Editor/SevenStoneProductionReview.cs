using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using WeatherHouse.WatersideV2;
using WeatherHouse.Vegetation;
using ForestSpriteCharacter;
using Object=UnityEngine.Object;

namespace WeatherHouse.StoneEngraving.Editor
{
    // Owned asset installation and reversible review placement. Never saves the shared main scene.
    [InitializeOnLoad]
    public static class SevenStoneProductionReview
    {
        const string Folder="StoneEngravingReview/20261029/SevenStoneProduction/MainScene";
        const string AssetsRoot="Assets/Experiments/StoneEngraving/Seven";
        const string RootName="七块石刻 · 制作预览";
        const string ScenePath="Assets/Scenes/WeatherHouse_WatersideV2.unity";
        const string Key="WeatherHouse.SevenStoneReview";
        static readonly string[] Ids={"wind","wet_dry","strong_weak","grass_tree","first_then","together","here_there"};
        static readonly string[] Titles={"风","湿与干","强与弱","草与树","先与后","一起","这里与那里"};
        static double nextPoll;
        static SevenStoneProductionReview(){EditorApplication.update+=Poll;EditorApplication.playModeStateChanged+=PlayChanged;}
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<nextPoll||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            nextPoll=EditorApplication.timeSinceStartup+.5;string path=Folder+"/command.txt";
            if(!File.Exists(path))return;string command=File.ReadAllText(path).Trim();File.Delete(path);
            try
            {
                if(command=="install")Install();
                else if(command=="inspect")Inspect();
                else if(command=="place")Place();
                else if(command=="checks")StartChecks();
                else if(command=="label")Label();
                else throw new InvalidOperationException("Unknown seven-stone review command.");
                File.WriteAllText(Folder+"/status.txt","OK "+command+" "+DateTime.UtcNow.ToString("O")+
                    "\nPlay="+EditorApplication.isPlaying+" Dirty="+SceneManager.GetActiveScene().isDirty);
            }
            catch(Exception e){File.WriteAllText(Folder+"/status.txt","FAIL "+command+"\n"+e);Debug.LogException(e);}
        }
        static void Install()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,"Install in Edit mode.");
            var report=new StringBuilder();
            foreach(string id in Ids)
            {
                string path=AssetsRoot+"/"+id+"/Stone.prefab";var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if(!root.GetComponent<StoneEngravingReadability>())root.AddComponent<StoneEngravingReadability>();
                    Require(root.GetComponent<MeshFilter>().sharedMesh==root.GetComponent<MeshCollider>().sharedMesh,"Collision mesh mismatch.");
                    var mat=root.GetComponent<Renderer>().sharedMaterial;
                    Require(mat&&mat.GetFloat("_GameplayReadability")==0&&mat.GetFloat("_GrooveDepth")==.08f,"Invalid camera/depth defaults.");
                    Require(!ShaderUtil.ShaderHasError(mat.shader),"Carving shader failed.");
                    PrefabUtility.SaveAsPrefabAsset(root,path);report.AppendLine("PASS "+id+": canonical mesh, collision, readability component, photo default, depth, shader");
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            File.WriteAllText(Folder+"/asset-checks.txt",report.ToString());
        }
        static Terrain Ground=>Object.FindObjectsOfType<Terrain>().First(t=>t.gameObject.scene.path==ScenePath);
        static void Label()
        {
            MainSceneReady();var root=GameObject.Find(RootName);Require(root&&root.transform.childCount==7,"Expected seven review stones.");
            for(int i=0;i<7;i++)
            {
                var stone=root.transform.GetChild(i).gameObject;
                Require(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(stone)==AssetsRoot+"/"+Ids[i]+"/Stone.prefab","Unexpected object in review group.");
                Undo.RecordObject(stone,"标注石刻名称");stone.name=Titles[i]+" · 石刻";PrefabUtility.RecordPrefabInstancePropertyModifications(stone);
            }
            string path=AssetsRoot+"/SevenStoneReviewGroup.prefab";var prefab=PrefabUtility.LoadPrefabContents(path);
            try
            {
                for(int i=0;i<7;i++){var child=prefab.transform.GetChild(i).gameObject;child.name=Titles[i]+" · 石刻";PrefabUtility.RecordPrefabInstancePropertyModifications(child);}
                PrefabUtility.SaveAsPrefabAsset(prefab,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(prefab);}
        }
        static void MainSceneReady()=>Require(!EditorApplication.isPlayingOrWillChangePlaymode&&SceneManager.GetActiveScene().path==ScenePath,"Open Waterside in Edit mode.");
        static void Inspect()
        {
            MainSceneReady();var text=new StringBuilder();
            foreach(var c in Object.FindObjectsOfType<Collider>().Where(c=>c.enabled&&!(c is TerrainCollider)&&!c.isTrigger&&c.bounds.center.x< -20&&c.bounds.center.x> -70&&c.bounds.center.z< -10&&c.bounds.center.z> -60))
                text.AppendLine(c.name+" center="+c.bounds.center+" size="+c.bounds.size);
            File.WriteAllText(Folder+"/nearby-colliders.txt",text.ToString());CaptureOverview("before");
        }
        static bool Safe(Bounds b,GameObject stone)
        {
            for(float z=b.min.z;z<=b.max.z+.01f;z+=.4f)for(float x=b.min.x;x<=b.max.x+.01f;x+=.4f)
                if(WatersideLayout.IsWater(x,z)||WatersideLayout.PathDistance(x,z)-WatersideLayout.WalkableHalfWidth(x,z)<.9f)return false;
            foreach(var c in Object.FindObjectsOfType<Collider>())
            {
                if(!c.enabled||c.isTrigger||c is TerrainCollider||c.gameObject==stone)continue;
                var expanded=b;expanded.Expand(new Vector3(1,0,1));
                if(expanded.Intersects(c.bounds))return false;
            }
            return true;
        }
        [MenuItem("Weather House/石刻/放置七块成品预览（不保存主场景）")]
        public static void Place()
        {
            MainSceneReady();Require(!GameObject.Find(RootName),"Seven-stone review group already exists.");
            var root=new GameObject(RootName);Undo.RegisterCreatedObjectUndo(root,"放置七块石刻预览");
            var log=new StringBuilder();
            try
            {
                foreach(string id in Ids)
                {
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetsRoot+"/"+id+"/Stone.prefab");
                    Require(prefab&&prefab.GetComponent<StoneEngravingReadability>(),"Install seven prefabs first.");
                    var stone=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root.transform);
                    stone.name=Titles[Array.IndexOf(Ids,id)]+" · 石刻";
                    PrefabUtility.RecordPrefabInstancePropertyModifications(stone);
                    var renderer=stone.GetComponent<Renderer>();bool found=false;
                    // Stable nearest-first dry-ground candidates to the west of the entry courtyard.
                    var points=(from z in Enumerable.Range(0,9) from x in Enumerable.Range(0,10)
                        let p=new Vector3(-64+x*4.5f,0,-54+z*4.5f)
                        orderby (p-new Vector3(-47,0,-34)).sqrMagnitude select p).ToArray();
                    foreach(var p in points)
                    {
                        var b=renderer.bounds;stone.transform.position+=new Vector3(p.x-b.center.x,0,p.z-b.center.z);
                        b=renderer.bounds;float min=1000,max=-1000;
                        for(int z=0;z<=4;z++)for(int x=0;x<=4;x++)
                        {
                            float h=WatersideLayout.GroundHeight(Ground,Mathf.Lerp(b.min.x,b.max.x,x/4f),Mathf.Lerp(b.min.z,b.max.z,z/4f));
                            min=Mathf.Min(min,h);max=Mathf.Max(max,h);
                        }
                        if(max-min>.55f)continue;
                        stone.transform.position+=Vector3.up*(min-.14f-renderer.bounds.min.y);Physics.SyncTransforms();
                        if(!Safe(renderer.bounds,stone))continue;found=true;break;
                    }
                    Require(found,"No safe review location for "+id);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(stone.transform);
                    log.AppendLine(id+" center="+renderer.bounds.center+" size="+renderer.bounds.size+" PASS road/water/collider/ground");
                }
                // Persist the complete arrangement as its own prefab, leaving all unrelated scene changes untouched.
                PrefabUtility.SaveAsPrefabAsset(root,AssetsRoot+"/SevenStoneReviewGroup.prefab");
                Selection.activeGameObject=root;File.WriteAllText(Folder+"/placement.txt",log.ToString());
                CaptureOverview("after");
            }
            catch{Undo.DestroyObjectImmediate(root);throw;}
        }
        [MenuItem("Weather House/检查/七块石刻实际相机（独立存档）")]
        public static void StartChecks()
        {
            MainSceneReady();Require(GameObject.Find(RootName),"Place the seven-stone review group first.");
            SessionState.SetString(Key+".save",Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")??"");
            SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Key+".background",Application.runInBackground);SessionState.SetBool(Key+".owned",true);
            Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",Path.GetFullPath(Folder+"/isolated-save"));
            EditorSceneManager.playModeStartScene=null;Application.runInBackground=true;EditorApplication.isPlaying=true;
        }
        static void PlayChanged(PlayModeStateChange state)
        {
            if(!SessionState.GetBool(Key+".owned",false))return;
            if(state==PlayModeStateChange.EnteredPlayMode)Object.FindObjectOfType<ForestSpriteDemoController>().StartCoroutine(CheckPlay());
            if(state!=PlayModeStateChange.EnteredEditMode)return;
            string save=SessionState.GetString(Key+".save","");Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",string.IsNullOrEmpty(save)?null:save);
            string start=SessionState.GetString(Key+".start","");EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(start)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
            Application.runInBackground=SessionState.GetBool(Key+".background",false);SessionState.SetBool(Key+".owned",false);
            File.WriteAllText(Folder+"/handoff.txt","Play=False; Dirty="+SceneManager.GetActiveScene().isDirty+
                "; original player, camera and save override restored; shared scene not saved. "+DateTime.UtcNow.ToString("O"));
        }
        static IEnumerator CheckPlay()
        {
            yield return new WaitForSecondsRealtime(3);
            var player=Object.FindObjectOfType<ForestSpriteDemoController>();var root=GameObject.Find(RootName);
            for(int index=0;index<Ids.Length;index++)
            {
                var stone=root.transform.GetChild(index).gameObject;var b=stone.GetComponent<Renderer>().bounds;
                var p=b.center+Vector3.back*5.2f;p.y=WatersideLayout.GroundHeight(Ground,p.x,p.z)+.15f;
                var controller=player.GetComponent<CharacterController>();controller.enabled=false;
                player.transform.position=p;controller.enabled=true;Physics.SyncTransforms();
                var orbit=player.demoCamera.GetComponent<VegetationTopDownCamera>();orbit.yawOffset=-orbit.baseYaw;orbit.SnapToPlayer();
                yield return new WaitForSecondsRealtime(.6f);
                try{StoneEngravingReadabilityChecks.Run(stone,player.demoCamera,Folder+"/Play/"+Ids[index]);}
                catch(Exception e){File.WriteAllText(Folder+"/play-result.txt","FAIL "+Ids[index]+"\n"+e);EditorApplication.isPlaying=false;yield break;}
            }
            File.WriteAllText(Folder+"/play-result.txt","PASS all seven: actual gameplay contrast; actual photo baseline identical; photo closing isolated; gameplay return; component cleanup.");
            EditorApplication.isPlaying=false;
        }
        static void CaptureOverview(string label)
        {
            var go=new GameObject("Seven stone overview"){hideFlags=HideFlags.HideAndDontSave};
            try
            {
                var camera=go.AddComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.Skybox;
                camera.nearClipPlane=.1f;camera.farClipPlane=180;camera.orthographic=true;camera.orthographicSize=24;
                camera.transform.SetPositionAndRotation(new Vector3(-43,65,-33),Quaternion.Euler(90,0,0));
                go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
                StoneEngravingPlacementReview.Capture(camera,Folder+"/"+label+"-overview.png",1440,1080);
            }
            finally{Object.DestroyImmediate(go);}
        }
    }
}
