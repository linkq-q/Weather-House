using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using Object=UnityEngine.Object;
namespace WeatherHouse.WatersideV2.Editor
{
    // Separate post-integration transaction; never invokes the original Apply or rewrites its frozen sources.
    public static class RuralVisualFollowup
    {
        const string ScenePath="Assets/Scenes/WeatherHouse_WatersideV2.unity";
        const string Review="JourneyReview/Revision_20261024/MapIntegrationPlan/VisualFollowupV1";
        const string Output="Assets/Environment/RuralRebuild20261025/VisualFollowupV1";
        const string OldShelter="海岬开放木棚 · 切角板材";
        [Serializable] public sealed class FileRecord { public string role,path,sha256,metaSha256; }
        [Serializable] public sealed class Baseline { public string sceneHash,alphaHash,grassPixelsHash,heightHash;public List<FileRecord> files=new List<FileRecord>(); }
        [Serializable] public sealed class TransformRecord { public string id,name,parent;public Vector3 position,localPosition,scale;public Quaternion rotation,localRotation;public bool active; }
        [Serializable] public sealed class Binding { public string id;public string[] materials;public string mesh; }
        [Serializable] public sealed class Report
        {
            public string folder,status,beforeSceneHash,savedSceneHash,oldShelterId,seaRendererId;
            public Baseline baseline;public List<TransformRecord> transforms=new List<TransformRecord>();public List<Binding> bindings=new List<Binding>();public List<string> regroundedRocks=new List<string>();
            public int heightChanges,outsideHeightChanges,protectedHeightChanges,wetHeightChanges,routeSamples;
            public float maximumLowering,maxOutsideDelta,maxRouteGrade;public bool alphaUnchanged,grassUnchanged,seaUnchanged,oceanOptionEnabled,originalWaterOptionNeutral;public string routeValidation;
        }
        sealed class Context { public Scene scene;public Transform[] all;public Terrain terrain;public TerrainCollider collider;public HillsideGrassRenderer grass;public MeshFilter sea;public Renderer seaRenderer;public Transform shelter;public List<Bounds> protection;public Func<Vector3,bool> editable; }
        static string Hash(byte[] bytes){using(var s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(bytes)).Replace("-","");}
        static string FileHash(string p)=>Hash(File.ReadAllBytes(p));
        static string ArrayHash(Array a){var bytes=new byte[Buffer.ByteLength(a)];Buffer.BlockCopy(a,0,bytes,0,bytes.Length);return Hash(bytes);}
        static string Pixels(Texture2D t)=>Hash(t.GetRawTextureData());
        static string Id(Object o)=>o?GlobalObjectId.GetGlobalObjectIdSlow(o).ToString():"null";
        static Scene RequireEdit(bool clean)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("Wait for idle Edit mode.");
            var s=SceneManager.GetActiveScene();if(s.path!=ScenePath||SceneManager.sceneCount!=1||(clean&&s.isDirty))throw new InvalidOperationException("Open the clean saved main scene alone.");return s;
        }
        static Context ReadContext(bool clean)
        {
            var c=new Context{scene=RequireEdit(clean)};c.all=c.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            if(c.all.Count(t=>t.name=="乡村水岸增量 · 20261025")!=1)throw new InvalidOperationException("Expected already-integrated map root.");
            c.terrain=c.all.Select(t=>t.GetComponent<Terrain>()).Single(t=>t);c.collider=c.terrain.GetComponent<TerrainCollider>();
            c.grass=c.all.Select(t=>t.GetComponent<HillsideGrassRenderer>()).Single(g=>g&&g.terrain==c.terrain);
            c.sea=c.all.Single(t=>t.name=="海湾").GetComponent<MeshFilter>();c.seaRenderer=c.sea.GetComponent<Renderer>();
            c.shelter=c.all.Single(t=>t.name==OldShelter&&t.parent&&t.parent.name=="5 海岬避雨棚");
            if(!c.collider||!c.grass.placementMap||!c.grass.placementMap.isReadable||c.collider.terrainData!=c.terrain.terrainData||c.seaRenderer.sharedMaterials.Length!=1)throw new InvalidOperationException("Unexpected map bindings.");
            // Reuse the frozen integration's exact protection/edit mask; Prepare only reads scene data and sets its private planning fields.
            const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Static;var type=typeof(RuralMapIntegration);
            var plan=type.GetMethod("ReadPlan",flags).Invoke(null,null);type.GetMethod("Prepare",flags).Invoke(null,new object[]{c.scene,plan});
            c.protection=((IEnumerable)type.GetField("protectedBounds",flags).GetValue(null)).Cast<Bounds>().ToList();
            c.editable=(Func<Vector3,bool>)Delegate.CreateDelegate(typeof(Func<Vector3,bool>),type.GetMethod("Editable",flags));return c;
        }
        static FileRecord Record(string role,Object asset)
        {
            string p=AssetDatabase.GetAssetPath(asset);if(string.IsNullOrEmpty(p)||EditorUtility.IsDirty(asset))throw new InvalidOperationException("Unsaved or nonpersistent asset: "+role);
            return new FileRecord{role=role,path=p,sha256=FileHash(p),metaSha256=FileHash(p+".meta")};
        }
        static FileRecord Role(Baseline b,string role)=>b.files.Single(f=>f.role==role);
        static void FilesUnchanged(Baseline b){foreach(var f in b.files)if(FileHash(f.path)!=f.sha256||FileHash(f.path+".meta")!=f.metaSha256)throw new InvalidOperationException("Source file changed: "+f.role);}
        public static string CaptureBaseline(string expectedSceneHash)
        {
            var c=ReadContext(true);if(FileHash(ScenePath)!=expectedSceneHash)throw new InvalidOperationException("Scene baseline changed.");
            var data=c.terrain.terrainData;var b=new Baseline{sceneHash=expectedSceneHash,alphaHash=ArrayHash(data.GetAlphamaps(0,0,data.alphamapWidth,data.alphamapHeight)),heightHash=ArrayHash(data.GetHeights(0,0,data.heightmapResolution,data.heightmapResolution)),grassPixelsHash=Pixels(c.grass.placementMap)};
            b.files.Add(Record("Terrain",data));b.files.Add(Record("Grass",c.grass.placementMap));b.files.Add(Record("Sea",c.sea.sharedMesh));b.files.Add(Record("SeaMaterial",c.seaRenderer.sharedMaterial));b.files.Add(Record("WaterShader",c.seaRenderer.sharedMaterial.shader));
            if(!c.shelter.gameObject.activeSelf||AssetDatabase.GetAssetPath(data).StartsWith(Output+"/"))throw new InvalidOperationException("Followup already present; do not apply twice.");
            Directory.CreateDirectory(Review);string path=Review+"/baseline-"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff")+".json";File.WriteAllText(path,JsonUtility.ToJson(b,true));return path;
        }
        static TransformRecord Capture(Transform t)=>new TransformRecord{id=Id(t),name=t.name,parent=Id(t.parent),position=t.position,localPosition=t.localPosition,rotation=t.rotation,localRotation=t.localRotation,scale=t.localScale,active=t.gameObject.activeSelf};
        static string OwnedAncestor(TransformRecord record,Dictionary<string,TransformRecord> before,HashSet<string> owned)
        {
            for(int i=0;i<=before.Count;i++){if(owned.Contains(record.id))return record.id;if(!before.TryGetValue(record.parent,out record))return null;}
            throw new InvalidOperationException("Cyclic baseline hierarchy.");
        }
        static bool TransformMatches(TransformRecord before,TransformRecord after,int ownership,Vector3 delta,bool expectedActive)
        {
            if(before.id!=after.id||before.name!=after.name||before.parent!=after.parent||!before.rotation.Equals(after.rotation)||!before.localRotation.Equals(after.localRotation)||!before.scale.Equals(after.scale)||after.active!=expectedActive)return false;
            if(ownership==0)return before.position.Equals(after.position)&&before.localPosition.Equals(after.localPosition);
            if(delta.x!=0||delta.z!=0||delta.y>.002f)return false;
            var expected=before.position+delta;
            if(ownership==1)return Mathf.Abs(after.position.x-expected.x)<1e-6f&&Mathf.Abs(after.position.z-expected.z)<1e-6f&&Mathf.Abs(after.position.y-expected.y)<.002f;
            return before.localPosition.Equals(after.localPosition)&&(after.position-expected).sqrMagnitude<.002f*.002f;
        }
        static Binding CaptureBinding(Transform t){var r=t.GetComponent<Renderer>();var m=t.GetComponent<MeshFilter>();return new Binding{id=Id(t),materials=r?r.sharedMaterials.Select(Id).ToArray():new string[0],mesh=m?Id(m.sharedMesh):""};}
        static Vector3 Point(Terrain t,int x,int z,int width,int height)=>t.transform.position+new Vector3(x/(width-1f)*t.terrainData.size.x,-t.transform.position.y,z/(height-1f)*t.terrainData.size.z);
        static bool Mask(Context c,Vector3 p)=>c.editable(p)&&RuralPoolBankPlan.Weight(p,c.protection)>0;
        static void Write(Report r){File.WriteAllText(r.folder+"/report.json",JsonUtility.ToJson(r,true));File.WriteAllText(Review+"/latest.json",JsonUtility.ToJson(r,true));}
        public static string Apply(string baselinePath)
        {
            var c=ReadContext(true);var b=JsonUtility.FromJson<Baseline>(File.ReadAllText(baselinePath));FilesUnchanged(b);
            if(FileHash(ScenePath)!=b.sceneHash||AssetDatabase.GetAssetPath(c.terrain.terrainData)!=Role(b,"Terrain").path||AssetDatabase.GetAssetPath(c.grass.placementMap)!=Role(b,"Grass").path||AssetDatabase.GetAssetPath(c.sea.sharedMesh)!=Role(b,"Sea").path||AssetDatabase.GetAssetPath(c.seaRenderer.sharedMaterial)!=Role(b,"SeaMaterial").path)throw new InvalidOperationException("Live bindings no longer match baseline.");
            if(Directory.Exists(Output)||!c.shelter.gameObject.activeSelf)throw new InvalidOperationException("Prior followup output exists; inspect before retry.");
            var old=c.terrain.terrainData;var oldMat=c.seaRenderer.sharedMaterial;
            if(!oldMat.HasProperty("_DeepBedOcclusion")||ShaderUtil.GetShaderMessages(oldMat.shader).Any(m=>m.severity.ToString()=="Error")||oldMat.GetFloat("_DeepBedOcclusion")!=0)throw new InvalidOperationException("Install the opt-in shader patch with neutral original material first.");
            var h=old.GetHeights(0,0,old.heightmapResolution,old.heightmapResolution);
            if(ArrayHash(h)!=b.heightHash||ArrayHash(old.GetAlphamaps(0,0,old.alphamapWidth,old.alphamapHeight))!=b.alphaHash||Pixels(c.grass.placementMap)!=b.grassPixelsHash)throw new InvalidOperationException("Unsaved source pixels changed since capture.");
            var r=new Report{folder=Review+"/Runs/"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"),status="backed-up",beforeSceneHash=b.sceneHash,baseline=b,oldShelterId=Id(c.shelter),seaRendererId=Id(c.sea.transform),transforms=c.all.Select(Capture).ToList(),bindings=c.all.Select(CaptureBinding).ToList()};
            Directory.CreateDirectory(r.folder);File.Copy(ScenePath,r.folder+"/before.unity",false);if(FileHash(r.folder+"/before.unity")!=b.sceneHash)throw new IOException("Scene backup hash mismatch.");
            foreach(var f in b.files){File.Copy(f.path,r.folder+"/"+f.role+Path.GetExtension(f.path),false);File.Copy(f.path+".meta",r.folder+"/"+f.role+Path.GetExtension(f.path)+".meta",false);if(FileHash(r.folder+"/"+f.role+Path.GetExtension(f.path))!=f.sha256||FileHash(r.folder+"/"+f.role+Path.GetExtension(f.path)+".meta")!=f.metaSha256)throw new IOException("Backup mismatch: "+f.role);}Write(r);
            try
            {
                Directory.CreateDirectory(Output);AssetDatabase.ImportAsset(Output,ImportAssetOptions.ForceSynchronousImport);
                if(!AssetDatabase.CopyAsset(Role(b,"Terrain").path,Output+"/Terrain.asset"))throw new IOException("Terrain copy failed.");AssetDatabase.ImportAsset(Output+"/Terrain.asset",ImportAssetOptions.ForceSynchronousImport);
                var data=AssetDatabase.LoadAssetAtPath<TerrainData>(Output+"/Terrain.asset");
                if(ArrayHash(data.GetHeights(0,0,data.heightmapResolution,data.heightmapResolution))!=b.heightHash||ArrayHash(data.GetAlphamaps(0,0,data.alphamapWidth,data.alphamapHeight))!=b.alphaHash)throw new InvalidOperationException("Copy baseline mismatch.");
                var result=(float[,])h.Clone();for(int z=0;z<h.GetLength(0);z++)for(int x=0;x<h.GetLength(1);x++){var p=Point(c.terrain,x,z,h.GetLength(1),h.GetLength(0));if(!Mask(c,p))continue;float before=c.terrain.transform.position.y+h[z,x]*old.size.y;float after=RuralPoolBankPlan.Height(p,before,c.protection);result[z,x]=(after-c.terrain.transform.position.y)/old.size.y;if(before-after>1e-5f){r.heightChanges++;r.maximumLowering=Mathf.Max(r.maximumLowering,before-after);}}
                data.SetHeights(0,0,result);c.terrain.terrainData=data;c.collider.terrainData=data;c.terrain.Flush();
                foreach(var t in c.all.Where(t=>t.name.StartsWith("新潮池断续礁石 ·",StringComparison.Ordinal)&&t.parent&&t.parent.name=="乡村水岸增量 · 20261025")){var p=t.position;if(!Mask(c,p))continue;float oldY=old.GetInterpolatedHeight((p.x-c.terrain.transform.position.x)/old.size.x,(p.z-c.terrain.transform.position.z)/old.size.z)+c.terrain.transform.position.y;float dy=c.terrain.SampleHeight(p)+c.terrain.transform.position.y-oldY;if(Mathf.Abs(dy)>.0001f){r.regroundedRocks.Add(Id(t));p.y+=dy;t.position=p;}}
                c.shelter.gameObject.SetActive(false);
                var material=new Material(oldMat){name="海湾深水遮蔽 · 浅潮池保持清透"};material.SetFloat("_DeepBedOcclusion",1);AssetDatabase.CreateAsset(material,Output+"/Ocean.mat");c.seaRenderer.sharedMaterial=material;
                Verify(c,r);AssetDatabase.SaveAssetIfDirty(data);AssetDatabase.SaveAssetIfDirty(material);Physics.SyncTransforms();EditorSceneManager.MarkSceneDirty(c.scene);r.status="applied-unsaved";Write(r);return "PASS followup; height changes="+r.heightChanges+", max route grade="+r.maxRouteGrade+"; scene unsaved. Review then Save(beforeSceneHash).";
            }
            catch(Exception e){c.terrain.terrainData=old;c.collider.terrainData=old;c.terrain.Flush();c.seaRenderer.sharedMaterial=oldMat;foreach(var t in c.all){var rec=r.transforms.Single(v=>v.id==Id(t));if(r.regroundedRocks.Contains(rec.id))t.position=rec.position;if(rec.id==r.oldShelterId)t.gameObject.SetActive(rec.active);}EditorSceneManager.MarkSceneDirty(c.scene);r.status="failed-restored-in-memory: "+e.Message;Write(r);throw;}
        }
        static void Verify(Context c,Report r)
        {
            FilesUnchanged(r.baseline);var old=AssetDatabase.LoadAssetAtPath<TerrainData>(Role(r.baseline,"Terrain").path);var data=c.terrain.terrainData;
            var before=old.GetHeights(0,0,old.heightmapResolution,old.heightmapResolution);var after=data.GetHeights(0,0,data.heightmapResolution,data.heightmapResolution);
            r.outsideHeightChanges=r.protectedHeightChanges=r.wetHeightChanges=0;r.maxOutsideDelta=0;
            for(int z=0;z<after.GetLength(0);z++)for(int x=0;x<after.GetLength(1);x++){var p=Point(c.terrain,x,z,after.GetLength(1),after.GetLength(0));float delta=Mathf.Abs(after[z,x]-before[z,x]);if(!Mask(c,p)){r.maxOutsideDelta=Mathf.Max(r.maxOutsideDelta,delta);if(delta>1.6f/65535)r.outsideHeightChanges++;}if(WatersideLayout.IsWater(p.x,p.z,.05f)&&delta>1.6f/65535)r.wetHeightChanges++;if(c.protection.Any(box=>p.x>=box.min.x&&p.x<=box.max.x&&p.z>=box.min.z&&p.z<=box.max.z)&&delta>1.6f/65535)r.protectedHeightChanges++;if(after[z,x]>before[z,x]+1.6f/65535)throw new InvalidOperationException("Unexpected terrain raising.");}
            r.alphaUnchanged=ArrayHash(data.GetAlphamaps(0,0,data.alphamapWidth,data.alphamapHeight))==r.baseline.alphaHash;
            r.grassUnchanged=AssetDatabase.GetAssetPath(c.grass.placementMap)==Role(r.baseline,"Grass").path&&Pixels(c.grass.placementMap)==r.baseline.grassPixelsHash;
            r.seaUnchanged=AssetDatabase.GetAssetPath(c.sea.sharedMesh)==Role(r.baseline,"Sea").path;
            if(r.outsideHeightChanges!=0||r.protectedHeightChanges!=0||r.wetHeightChanges!=0||!r.alphaUnchanged||!r.grassUnchanged||!r.seaUnchanged)throw new InvalidOperationException("Outside/water/protection/alpha/grass/sea guard failed.");
            if(ArrayHash(before)!=r.baseline.heightHash||ArrayHash(old.GetAlphamaps(0,0,old.alphamapWidth,old.alphamapHeight))!=r.baseline.alphaHash)throw new InvalidOperationException("Original terrain memory changed.");
            var all=c.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToDictionary(Id);
            if(all.Count!=r.transforms.Count)throw new InvalidOperationException("Object count changed.");
            var records=r.transforms.ToDictionary(t=>t.id);var owned=new HashSet<string>(r.regroundedRocks);
            foreach(var rec in r.transforms)
            {
                if(!all.TryGetValue(rec.id,out var t))throw new InvalidOperationException("Transform disappeared.");
                string owner=OwnedAncestor(rec,records,owned);Vector3 delta=Vector3.zero;
                if(owner!=null){var p=records[owner].position;delta.y=c.terrain.SampleHeight(p)-old.GetInterpolatedHeight((p.x-c.terrain.transform.position.x)/old.size.x,(p.z-c.terrain.transform.position.z)/old.size.z);}
                if(!TransformMatches(rec,Capture(t),owner==null?0:owner==rec.id?1:2,delta,rec.id==r.oldShelterId?false:rec.active))throw new InvalidOperationException("Transform guard failed: "+rec.name);
            }
            foreach(var bind in r.bindings){var t=all[bind.id];var current=CaptureBinding(t);if(current.mesh!=bind.mesh)throw new InvalidOperationException("Mesh reference changed: "+t.name);if(bind.id!=r.seaRendererId&&(current.materials==null?bind.materials!=null:bind.materials==null||!current.materials.SequenceEqual(bind.materials)))throw new InvalidOperationException("Unowned material reference changed: "+t.name);}
            var originalMaterial=AssetDatabase.LoadAssetAtPath<Material>(Role(r.baseline,"SeaMaterial").path);r.oceanOptionEnabled=c.seaRenderer.sharedMaterial.GetFloat("_DeepBedOcclusion")==1;r.originalWaterOptionNeutral=originalMaterial.GetFloat("_DeepBedOcclusion")==0;
            if(!r.oceanOptionEnabled||!r.originalWaterOptionNeutral||c.seaRenderer.sharedMaterial.GetFloat("_Opacity")!=originalMaterial.GetFloat("_Opacity"))throw new InvalidOperationException("Ocean material guard failed.");
            r.routeValidation=RuralMapIntegration.Validate();r.routeSamples=0;r.maxRouteGrade=0;
            foreach(var line in RuralMapConfiguration.NewCorridors)for(int i=1;i<line.Length;i++){var a=line[i-1];var b=line[i];a.y=b.y=0;var f=(b-a).normalized;float length=Vector3.Distance(a,b);for(float d=0;d<=length;d+=.75f){var p=Vector3.Lerp(a,b,d/length);float delta=Mathf.Abs(data.GetInterpolatedHeight((p.x-c.terrain.transform.position.x)/data.size.x,(p.z-c.terrain.transform.position.z)/data.size.z)-old.GetInterpolatedHeight((p.x-c.terrain.transform.position.x)/old.size.x,(p.z-c.terrain.transform.position.z)/old.size.z));if(delta>.002f)throw new InvalidOperationException("Authored road centre height changed.");r.routeSamples++;float grade=Mathf.Atan2(Mathf.Abs(c.terrain.SampleHeight(p+f*.3f)-c.terrain.SampleHeight(p-f*.3f)),.6f)*Mathf.Rad2Deg;r.maxRouteGrade=Mathf.Max(r.maxRouteGrade,grade);}}
        }
        public static string Save(string expectedBeforeSceneHash)
        {
            var c=ReadContext(false);var r=JsonUtility.FromJson<Report>(File.ReadAllText(Review+"/latest.json"));if(r.status!="applied-unsaved"||r.beforeSceneHash!=expectedBeforeSceneHash||FileHash(ScenePath)!=expectedBeforeSceneHash||AssetDatabase.GetAssetPath(c.terrain.terrainData)!=Output+"/Terrain.asset")throw new InvalidOperationException("Followup save baseline mismatch.");
            Verify(c,r);if(!EditorSceneManager.SaveScene(c.scene))throw new IOException("Scene save failed.");r.savedSceneHash=FileHash(ScenePath);r.status="saved-awaiting-visual-walk-review";Write(r);return "Saved followup: "+r.savedSceneHash;
        }
    }
}
