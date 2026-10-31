using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using WeatherHouse.Hillside;
using WeatherHouse.Vegetation;
using WeatherHousePrototype;

[InitializeOnLoad]
public static class InteractionOctoberChecks
{
    static InteractionOctoberChecks() { EditorApplication.playModeStateChanged += PlayState; }
    const string Output = "../../../InteractionReview/20261030/checks.txt";
    static readonly System.Collections.Generic.List<string> results = new System.Collections.Generic.List<string>();
    static void Check(bool ok,string name) { results.Add((ok?"PASS ":"FAIL ")+name); File.WriteAllLines(Output,results); }
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var canvas=new GameObject("Canvas",typeof(RectTransform),typeof(Canvas));
        int right=0,left=0;
        var create=typeof(AlbumBookView).GetMethod("Create");
        var args=new object[] {canvas.transform,true,null,null,null,(UnityEngine.Events.UnityAction)(()=>right++),(UnityEngine.Events.UnityAction)(()=>left++)};
        var book=(RectTransform)create.Invoke(null,args.Take(create.GetParameters().Length).ToArray());
        book.Find("Next symbols").GetComponent<Button>().onClick.Invoke();
        var back=book.Find("Previous symbols"); if(back)back.GetComponent<Button>().onClick.Invoke();
        Check(left==1 && right==1,"Left/right page clicks route separately");
        Check(back && back.GetComponent<RectTransform>().rect.height>600,"Left page has a full-height click area");
        var camera=new GameObject("Camera",typeof(Camera)).GetComponent<Camera>();camera.tag="MainCamera";
        var orbit=camera.gameObject.AddComponent<VegetationTopDownCamera>();orbit.StepOrbit(170,0,0);
        Check(Mathf.Abs(orbit.yawOffset-170)<.01f,"Orbit reaches behind character");orbit.StepOrbit(-340,0,0);
        Check(Mathf.Abs(orbit.yawOffset+170)<.01f,"Orbit reaches both sides at 170 degrees");
        var population=new GameObject("Population").AddComponent<WildlifePopulation>();
        population.worldBounds=new Bounds(Vector3.zero,Vector3.one*100); population.obstacleMask=0;
        var ground=new TerrainData{heightmapResolution=33,size=new Vector3(20,10,20)};
        var groundGo=Terrain.CreateTerrainGameObject(ground);groundGo.transform.position=new Vector3(-10,0,-10);population.terrain=groundGo.GetComponent<Terrain>();
        population.waterAreas=new[]{new HabitatVolume{center=new Vector3(0,2,0),radii=Vector2.one*8}};
        var fish=new GameObject("Fish").AddComponent<WildlifeAgent>();fish.speciesId="fish";fish.locomotion=WildlifeLocomotion.Swimming;fish.bodyRadius=.05f;fish.waterAreaIndex=0;
        typeof(WildlifeAgent).GetProperty("BodyEnvelope").SetValue(fish,new WildlifeBodyEnvelope{center=Vector3.up*.8f,half=new Vector3(.1f,.8f,.1f),measured=true});
        bool projected=population.TryProjectPosition(fish,Vector3.zero,out Vector3 wet);
        Check(projected && wet.y+fish.BodyEnvelope.Top<=1.821f && wet.y+fish.BodyEnvelope.Bottom>=.079f,"Fish entire body stays under surface and above bottom");
        population.waterAreas[0]=new HabitatVolume{center=new Vector3(0,1.5f,0),radii=Vector2.one*8};
        Check(!population.TryProjectPosition(fish,Vector3.zero,out _),"Reject water too shallow for fish body");
        var tree=GameObject.CreatePrimitive(PrimitiveType.Cube);tree.transform.position=Vector3.forward*4;tree.transform.localScale=Vector3.one*4;
        var renderer=tree.GetComponent<Renderer>();renderer.shadowCastingMode=ShadowCastingMode.On;
        tree.AddComponent<LODGroup>().SetLODs(new[]{new LOD(.6f,new[]{renderer}),new LOD(.3f,new[]{renderer}),new LOD(.02f,new[]{renderer})});
        var occlusion=tree.AddComponent<HillsideTreeOcclusion>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        typeof(HillsideTreeOcclusion).GetMethod("Start",flags).Invoke(occlusion,null);
        typeof(HillsideTreeOcclusion).GetField("player",flags)?.SetValue(occlusion,fish.transform);fish.transform.position=Vector3.forward*10;
        typeof(HillsideTreeOcclusion).GetMethod("LateUpdate",flags)?.Invoke(occlusion,null);
        Check(renderer.shadowCastingMode==ShadowCastingMode.On,"Nearby canopy never switches entire tree to ShadowsOnly");
        foreach(var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())UnityEngine.Object.DestroyImmediate(go);
        SessionState.SetBool("OctoberCaptureCheck",true);
        EditorApplication.isPlaying=true;
    }
    static void PlayState(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("OctoberCaptureCheck",false))return;
        SessionState.SetBool("OctoberCaptureCheck",false);
        new GameObject("Capture test").AddComponent<OctoberCaptureProbe>();
    }
    public static void CaptureDone(bool ok,string info)
    {
        var existing=File.ReadAllLines(Output).ToList();existing.Add((ok?"PASS ":"FAIL ")+info);File.WriteAllLines(Output,existing);
        EditorApplication.Exit(existing.Any(l=>l.StartsWith("FAIL"))?1:0);
    }
}
public class OctoberCaptureProbe:MonoBehaviour
{
    void Start()
    {
        try
        {
            var data=new TerrainData{heightmapResolution=33,size=new Vector3(16,5,16)};
            var terrain=Terrain.CreateTerrainGameObject(data).GetComponent<Terrain>();terrain.transform.position=new Vector3(-8,0,-8);
            var main=new GameObject("Primary",typeof(Camera)).GetComponent<Camera>();main.tag="MainCamera";
            var camera=new GameObject("Shutter",typeof(Camera)).GetComponent<Camera>(); camera.enabled=false;
            camera.transform.position=new Vector3(0,12,-10);camera.transform.LookAt(Vector3.zero);camera.farClipPlane=70;
            var grass=new GameObject("Grass").AddComponent<HillsideGrassRenderer>();grass.terrain=terrain;grass.manualCaptureOnly=true;grass.viewCamera=main;
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);var mesh=cube.GetComponent<MeshFilter>().sharedMesh;Destroy(cube);
            var material=new Material(Shader.Find("Universal Render Pipeline/Unlit")){enableInstancing=true};material.SetColor("_BaseColor",Color.green);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.white;
            grass.variants=Enumerable.Repeat(mesh,12).ToArray();grass.materials=Enumerable.Repeat(material,3).ToArray();grass.clumpsPerSquareMeter=.8f;grass.cellSize=4;
            var map=new Texture2D(2,2);map.SetPixels(Enumerable.Repeat(new Color(1,0,0),4).ToArray());map.Apply();grass.placementMap=map;
            var recorder=gameObject.AddComponent<MotionPhotoRecorder>();
            bool ok=recorder.Capture(camera,new ObservationRecord{id=Guid.NewGuid().ToString("N")},_=>{});
            string folder=(string)typeof(MotionPhotoRecorder).GetField("ownedFolder",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(recorder);
            byte[] pixels=File.ReadAllBytes(Path.Combine(folder,"frame_000.png"));
            File.WriteAllBytes("../../../InteractionReview/20261030/photo-grass.png",pixels);
            var texture=new Texture2D(2,2);texture.LoadImage(pixels);
            int green=texture.GetPixels32().Count(c=>c.g>c.r*1.3f && c.g>c.b*1.3f && c.g>10);
            recorder.CancelCapture();
            InteractionOctoberChecks.CaptureDone(ok && green>100 && grass.VisibleClumpCount>0 && grass.SubmittedDrawCount>0,"Shutter renders fresh grass before LateUpdate: clumps="+grass.VisibleClumpCount+", draws="+grass.SubmittedDrawCount+", green pixels="+green);
        }
        catch(Exception e){InteractionOctoberChecks.CaptureDone(false,e.ToString());}
    }
}
