using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    // Explicit capture only; the coordinating task owns import, Play and scene saves.
    public static class BuildingRoofCapture20261027
    {
        const string AssetsFolder = "Assets/Environment/BuildingRoofHarmony20261027";
        const string Output = "LifeGameplayReview/20261027/building-roofs";
        const int Width = 1600, Height = 1000;
        [Serializable] sealed class Manifest { public string sourcePrefab; }
        [Serializable] sealed class SceneRow { public int handle; public string path; public bool dirty; }
        [Serializable] sealed class MeshRow { public int lod; public string name, mesh; public string[] materials; public bool enabled; }
        [Serializable] sealed class ViewRow { public string image; public Vector3 position, target; public Quaternion rotation; public float distance, fieldOfView; public Rect viewportBounds; }
        [Serializable] sealed class BuildingRow { public string id, hierarchy, status; public Bounds bounds; public List<MeshRow> meshes = new List<MeshRow>(); public List<ViewRow> views = new List<ViewRow>(); }
        [Serializable] sealed class Report
        {
            public string status, utc, sourceCamera, error;
            public int width = Width, height = Height;
            public bool allSceneTransformsUnchanged, sceneDirtyStatesUnchanged, mainCameraUnchanged, selectionUnchanged, activeSceneUnchanged, sceneSaved;
            public SceneRow[] scenesBefore, scenesAfter;
            public List<BuildingRow> buildings = new List<BuildingRow>();
        }
        sealed class Frame
        {
            readonly Vector3 position, scale; readonly Quaternion rotation; readonly int parent;
            public Frame(Transform t) { position=t.localPosition; rotation=t.localRotation; scale=t.localScale; parent=t.parent?t.parent.GetInstanceID():0; }
            public bool Matches(Transform t) => t && position.Equals(t.localPosition) && rotation.Equals(t.localRotation) && scale.Equals(t.localScale) && parent==(t.parent?t.parent.GetInstanceID():0);
        }

        [MenuItem("Weather House/Review 20261027/Capture A09 Roof Harmony")]
        static void CaptureA09Menu() => Debug.Log(Capture(new[] { "A09" }, "A09_capture-report.json"));
        [MenuItem("Weather House/Review 20261027/Capture Prepared Building Roofs")]
        static void CaptureAllMenu() => Debug.Log(Capture(new[] { "A01", "A02", "A03", "A04", "A06", "A07", "A09" }, "prepared_capture-report.json"));

        static Transform[] SceneTransforms() => Resources.FindObjectsOfTypeAll<Transform>().Where(t=>t.gameObject.scene.IsValid() && t.gameObject.scene.isLoaded).ToArray();
        static SceneRow[] Scenes() => Enumerable.Range(0,SceneManager.sceneCount).Select(i=>SceneManager.GetSceneAt(i))
            .Where(s=>s.isLoaded).Select(s=>new SceneRow { handle=s.handle, path=s.path, dirty=s.isDirty }).OrderBy(s=>s.handle).ToArray();
        static string Hierarchy(Transform t) => t.parent?Hierarchy(t.parent)+"/"+t.name:t.name;
        static void Require(bool value,string message) { if (!value) throw new InvalidOperationException(message); }
        static GameObject[] Instances(string prefab) => Resources.FindObjectsOfTypeAll<GameObject>().Where(go=>go.scene.IsValid() && go.scene.isLoaded &&
            PrefabUtility.IsAnyPrefabInstanceRoot(go) && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go)==prefab).ToArray();

        public static string Capture(string[] ids,string reportName)
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling,"Capture in edit mode after compilation; do not interrupt Play.");
            Camera source=Camera.main;
            Require(source && source.gameObject.scene.IsValid() && source.gameObject.scene.isLoaded,"No loaded scene Camera.main to copy.");
            var frames=SceneTransforms().ToDictionary(t=>t,t=>new Frame(t));
            int[] selected=Selection.instanceIDs; int activeSelection=Selection.activeInstanceID, activeScene=SceneManager.GetActiveScene().handle;
            string sourceState=EditorJsonUtility.ToJson(source);
            var sourceData=source.GetComponent<UniversalAdditionalCameraData>();
            string dataState=sourceData?EditorJsonUtility.ToJson(sourceData):"";
            Report report=new Report { status="RUNNING",utc=DateTime.UtcNow.ToString("O"),sourceCamera=Hierarchy(source.transform),scenesBefore=Scenes(),sceneSaved=false };
            Directory.CreateDirectory(Output);
            Exception failure=null;
            try
            {
                foreach (string id in ids)
                {
                    Manifest manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(AssetsFolder+"/"+id+"/integration.json"));
                    GameObject[] roots=Instances(manifest.sourcePrefab);
                    var row=new BuildingRow { id=id,status="SKIPPED_NO_EXISTING_INSTANCE" };report.buildings.Add(row);
                    if (roots.Length==0) continue;
                    Require(roots.Length==1,"Multiple "+id+" instances; refusing to overwrite one capture with another.");
                    GameObject root=roots[0];row.hierarchy=Hierarchy(root.transform);
                    LODGroup group=root.GetComponent<LODGroup>();
                    Require(group && group.GetLODs().Length>0,"Expected existing LODGroup on "+id);
                    LOD[] lods=group.GetLODs();
                    for (int level=0;level<lods.Length;level++) foreach (Renderer renderer in lods[level].renderers.Where(r=>r))
                    {
                        MeshFilter mesh=renderer.GetComponent<MeshFilter>();
                        row.meshes.Add(new MeshRow { lod=level,name=renderer.name,mesh=mesh && mesh.sharedMesh?AssetDatabase.GetAssetPath(mesh.sharedMesh):"",materials=renderer.sharedMaterials.Select(m=>m?AssetDatabase.GetAssetPath(m):"").ToArray(),enabled=renderer.enabled });
                    }
                    Renderer[] visible=lods[0].renderers.Where(r=>r && r.enabled && r.gameObject.activeInHierarchy && r.bounds.size.sqrMagnitude>.000001f).ToArray();
                    if (visible.Length==0) { row.status="SKIPPED_NO_ACTIVE_RENDERERS";continue; }
                    Bounds bounds=visible[0].bounds;foreach (Renderer renderer in visible.Skip(1)) bounds.Encapsulate(renderer.bounds);row.bounds=bounds;
                    Vector3 horizontal=root.transform.TransformDirection(new Vector3(.9f,0,-1.2f));horizontal.y=0;
                    Require(horizontal.sqrMagnitude>.001f,"Cannot form a horizontal three-quarter view for "+id);
                    Vector3 direction=(horizontal.normalized+Vector3.up*.65f).normalized;
                    row.views.Add(CaptureView(source,bounds,direction,1,Output+"/"+id+"_close.png"));
                    row.views.Add(CaptureView(source,bounds,direction,2.3f,Output+"/"+id+"_game-distance.png"));
                    row.status="CAPTURED_VISUAL_REVIEW_PENDING";
                }
                report.status="CAPTURED_VISUAL_REVIEW_PENDING";
            }
            catch (Exception error) { failure=error;report.status="FAILED";report.error=error.ToString(); }
            finally
            {
                Transform[] after=SceneTransforms();
                report.allSceneTransformsUnchanged=after.Length==frames.Count && frames.All(kv=>kv.Value.Matches(kv.Key));
                report.scenesAfter=Scenes();
                report.sceneDirtyStatesUnchanged=report.scenesBefore.Length==report.scenesAfter.Length && report.scenesBefore.Zip(report.scenesAfter,(a,b)=>a.handle==b.handle && a.path==b.path && a.dirty==b.dirty).All(v=>v);
                report.mainCameraUnchanged=source && sourceState==EditorJsonUtility.ToJson(source) && dataState==(sourceData?EditorJsonUtility.ToJson(sourceData):"");
                report.selectionUnchanged=activeSelection==Selection.activeInstanceID && selected.SequenceEqual(Selection.instanceIDs);
                report.activeSceneUnchanged=activeScene==SceneManager.GetActiveScene().handle;
                if (!report.allSceneTransformsUnchanged || !report.sceneDirtyStatesUnchanged || !report.mainCameraUnchanged || !report.selectionUnchanged || !report.activeSceneUnchanged)
                { report.status="GUARD_FAILED";report.error+="\nRead-only capture guard failed; inspect the report. No scene was saved or Dirty flag reset."; }
                File.WriteAllText(Output+"/"+reportName,JsonUtility.ToJson(report,true));
            }
            if (failure!=null) throw new InvalidOperationException("Building capture failed; see "+Output+"/"+reportName,failure);
            Require(report.status!="GUARD_FAILED","Capture guard failed; see "+Output+"/"+reportName);
            return Output+"/"+reportName;
        }

        static Vector3[] Corners(Bounds bounds)
        {
            Vector3 min=bounds.min,max=bounds.max;
            return Enumerable.Range(0,8).Select(i=>new Vector3((i&1)==0?min.x:max.x,(i&2)==0?min.y:max.y,(i&4)==0?min.z:max.z)).ToArray();
        }
        static ViewRow CaptureView(Camera source,Bounds bounds,Vector3 direction,float distanceMultiplier,string filename)
        {
            GameObject go=null;Texture2D pixels=null;RenderTexture target=null;RenderTexture previous=RenderTexture.active;
            try
            {
                go=new GameObject("Temporary building roof capture camera") { hideFlags=HideFlags.HideAndDontSave };
                Camera camera=go.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;
                var oldData=source.GetComponent<UniversalAdditionalCameraData>();var data=go.AddComponent<UniversalAdditionalCameraData>();
                if (oldData) EditorUtility.CopySerialized(oldData,data);data.renderType=CameraRenderType.Base;data.cameraStack.Clear();
                camera.targetTexture=null;camera.rect=new Rect(0,0,1,1);camera.aspect=(float)Width/Height;
                camera.orthographic=false;if (source.orthographic) camera.fieldOfView=50;
                camera.nearClipPlane=.1f;camera.ResetProjectionMatrix();camera.ResetWorldToCameraMatrix();
                Quaternion rotation=Quaternion.LookRotation(-direction,Vector3.up);
                Vector3 right=rotation*Vector3.right,up=rotation*Vector3.up,forward=rotation*Vector3.forward;
                float tangent=Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f),distance=.2f;
                foreach (Vector3 corner in Corners(bounds))
                {
                    Vector3 delta=corner-bounds.center;float depth=Vector3.Dot(delta,forward);
                    distance=Mathf.Max(distance,Mathf.Abs(Vector3.Dot(delta,right))/(tangent*camera.aspect*.86f)-depth,Mathf.Abs(Vector3.Dot(delta,up))/(tangent*.86f)-depth,camera.nearClipPlane+.2f-depth);
                }
                distance*=distanceMultiplier;camera.transform.SetPositionAndRotation(bounds.center+direction*distance,rotation);
                camera.farClipPlane=Mathf.Max(source.farClipPlane,distance+bounds.extents.magnitude*4);
                target=RenderTexture.GetTemporary(Width,Height,24,RenderTextureFormat.ARGB32);
                var request=new UniversalRenderPipeline.SingleCameraRequest { destination=target };
                Require(RenderPipeline.SupportsRenderRequest(camera,request),"URP SingleCameraRequest unavailable.");
                RenderPipeline.SubmitRenderRequest(camera,request);RenderTexture.active=target;
                pixels=new Texture2D(Width,Height,TextureFormat.RGB24,false) { hideFlags=HideFlags.HideAndDontSave };
                pixels.ReadPixels(new Rect(0,0,Width,Height),0,0);pixels.Apply(false,false);File.WriteAllBytes(filename,pixels.EncodeToPNG());
                float x0=1,y0=1,x1=0,y1=0;
                foreach (Vector3 corner in Corners(bounds)) { Vector3 v=camera.WorldToViewportPoint(corner);x0=Mathf.Min(x0,v.x);y0=Mathf.Min(y0,v.y);x1=Mathf.Max(x1,v.x);y1=Mathf.Max(y1,v.y); }
                return new ViewRow { image=filename,position=camera.transform.position,rotation=rotation,target=bounds.center,distance=distance,fieldOfView=camera.fieldOfView,viewportBounds=Rect.MinMaxRect(x0,y0,x1,y1) };
            }
            finally { RenderTexture.active=previous;if (pixels) Object.DestroyImmediate(pixels);if (target) RenderTexture.ReleaseTemporary(target);if (go) Object.DestroyImmediate(go); }
        }
    }
}
