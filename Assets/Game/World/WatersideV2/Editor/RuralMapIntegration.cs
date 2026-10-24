using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    // Installed only in the coordinated integration window. No automatic startup or scene callbacks.
    public static class RuralMapIntegration
    {
        const string Review = "JourneyReview/Revision_20261024/MapIntegrationPlan";
        const string ScenePath = "Assets/Scenes/WeatherHouse_WatersideV2.unity";
        const string RootName = "乡村水岸增量 · 20261025";
        const string Generated = "Assets/Environment/RuralRebuild20261025/Map";
        const string PlacementPath = "Assets/Game/World/WatersideV2/RuralMapPlacements20261025.json";
        [Serializable] public sealed class Placement { public string id,name,sourceSha256;public float x,z,yaw,width,depth,height;public bool isBoat; }
        [Serializable] public sealed class Placements { public string version; public Placement[] assets; }
        [Serializable] public sealed class TransformRecord { public string id,name,parentId;public Vector3 position,localPosition,scale;public Quaternion rotation,localRotation;public bool active; }
        [Serializable] public sealed class Run { public string folder,sceneBeforeHash,sceneSavedHash,originalTerrain,originalGrass,originalSea,status;public int heightChanges,alphaChanges,grassChanges;public List<TransformRecord> objects=new List<TransformRecord>(); }
        sealed class Site { public Placement p;public Bounds bounds;public float ground; }
        static Terrain terrain;
        static TerrainData originalData;
        static float[,] originalHeights;
        static Vector3 origin,size;
        static readonly List<Bounds> protectedBounds = new List<Bounds>();
        static readonly List<Site> sites = new List<Site>();
        static readonly Dictionary<Transform,string> existingIds = new Dictionary<Transform,string>();
        static Run run;

        static Scene RequireScene()
        {
            WatersideSceneBuilder.RequireSaved();
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath||SceneManager.sceneCount!=1)throw new InvalidOperationException("Open the saved main WatersideV2 scene alone; no scene was changed.");
            return scene;
        }
        static string Hash(string p) { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p))).Replace("-",""); }
        static Transform[] All(Scene s)=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
        static string Id(Object o)=>GlobalObjectId.GetGlobalObjectIdSlow(o).ToString();
        static float Smooth(float a,float b,float v)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,v));
        static Vector3 XZ(float x,float z)=>new Vector3(x,0,z);
        static Vector3 SamplePoint(int x,int z,int w,int h)=>new Vector3(origin.x+x/(w-1f)*size.x,0,origin.z+z/(h-1f)*size.z);
        static float OldHeight(Vector3 p)
        {
            float x=Mathf.Clamp01((p.x-origin.x)/size.x)*(originalHeights.GetLength(1)-1),z=Mathf.Clamp01((p.z-origin.z)/size.z)*(originalHeights.GetLength(0)-1);
            int ix=Mathf.FloorToInt(x),iz=Mathf.FloorToInt(z),jx=Mathf.Min(ix+1,originalHeights.GetLength(1)-1),jz=Mathf.Min(iz+1,originalHeights.GetLength(0)-1);
            return origin.y+Mathf.Lerp(Mathf.Lerp(originalHeights[iz,ix],originalHeights[iz,jx],x-ix),Mathf.Lerp(originalHeights[jz,ix],originalHeights[jz,jx],x-ix),z-iz)*size.y;
        }
        static Bounds BoundsOf(GameObject go)
        {
            var rs=go.GetComponentsInChildren<Renderer>(true);if(rs.Length==0)throw new InvalidOperationException("No renderers: "+go.name);
            Bounds b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;
        }
        static bool WithinTerrain(Vector3 p,float margin=0)=>p.x>=origin.x+margin&&p.x<=origin.x+size.x-margin&&p.z>=origin.z+margin&&p.z<=origin.z+size.z-margin;
        static bool Protected(Vector3 p)
        {
            foreach(var b in protectedBounds)if(p.x>=b.min.x&&p.x<=b.max.x&&p.z>=b.min.z&&p.z<=b.max.z)return true;
            // The central island is a fixed natural anchor. No proposed route grading enters it.
            return RuralMapConfiguration.Contains(p,RuralMapConfiguration.Regions[3]);
        }
        static float CorridorDistance(Vector3 p)
        { float d=float.MaxValue;foreach(var l in RuralMapConfiguration.NewCorridors)d=Mathf.Min(d,RuralMapConfiguration.LineDistance(p,l));return d; }
        static bool OldPools(Vector3 p)=>p.x>=49&&p.x<=68&&p.z>=-44&&p.z<=-20;
        static float PoolDistance(Vector3 p)
        { float d=float.MaxValue;foreach(var pool in WatersideLayout.TidePools)d=Mathf.Min(d,pool.SignedDistance(p.x,p.z));return d; }
        static float FoundationDistance(Vector3 p,Bounds b)
        { return Vector2.Distance(new Vector2(p.x,p.z),new Vector2(Mathf.Clamp(p.x,b.min.x,b.max.x),Mathf.Clamp(p.z,b.min.z,b.max.z))); }
        static float RoadProtectionBlend(Vector3 p)
        {
            float distance=float.MaxValue;
            foreach(var b in protectedBounds)distance=Mathf.Min(distance,FoundationDistance(p,b));
            var island=RuralMapConfiguration.Regions[3];
            for(int i=0;i<island.Length;i++)distance=Mathf.Min(distance,RuralMapConfiguration.SegmentDistance(p,island[i],island[(i+1)%island.Length]));
            return Smooth(0,3,distance);
        }
        static float FoundationRun(Vector3 anchor,Vector3 forward)
        {
            float run=0;
            foreach(var s in sites)
            {
                if(s.p.id!="A01"&&s.p.id!="A08")continue;
                if(FoundationDistance(anchor,s.bounds)>.001f)continue;
                float x=Mathf.Abs(forward.x)<.0001f?float.MaxValue:((forward.x>0?s.bounds.max.x:s.bounds.min.x)-anchor.x)/forward.x;
                float z=Mathf.Abs(forward.z)<.0001f?float.MaxValue:((forward.z>0?s.bounds.max.z:s.bounds.min.z)-anchor.z)/forward.z;
                run=Mathf.Max(run,Mathf.Min(x,z)+.15f);
            }
            return run;
        }
        static bool ChangedShore(Vector3 p)
        {
            float old=RuralMapConfiguration.OldShoreDistance(p),now=WatersideLayout.SeaSignedDistance(p.x,p.z);
            return (old<7&&now>-.8f&&Mathf.Abs(old-now)>.3f)||OldPools(p)||PoolDistance(p)<1.1f;
        }
        static bool Editable(Vector3 p)=>WithinTerrain(p)&&!Protected(p)&&(ChangedShore(p)||CorridorDistance(p)<3.8f||sites.Any(s=>!s.p.isBoat&&s.p.id.StartsWith("A")&&FoundationDistance(p,s.bounds)<2.2f));
        static float TargetHeight(Vector3 p)
        {
            float old=OldHeight(p),h=old;
            float sea=WatersideLayout.SeaSignedDistance(p.x,p.z),oldSea=RuralMapConfiguration.OldShoreDistance(p);
            if(ChangedShore(p))
            {
                // Feather onto existing land; use only the changed coastal corridor, not the full terrain formula.
                float t=1-Smooth(3,7,oldSea);
                if(OldPools(p))t=Mathf.Max(t,Smooth(0,2,Mathf.Min(Mathf.Min(p.x-49,68-p.x),Mathf.Min(p.z+44,-20-p.z))));
                float coastal=sea<0?Mathf.Max(-2.5f,sea*.55f):Mathf.Lerp(.15f,1.5f,Smooth(0,5,sea));
                h=Mathf.Lerp(old,coastal,t);
                // Existing river-mouth geometry remains authoritative where it meets the coast.
                if(WatersideLayout.WaterSection(p.x,p.z)>=0&&WatersideLayout.IsWater(p.x,p.z))h=Mathf.Min(h,WatersideLayout.Height(p.x,p.z));
                float pool=PoolDistance(p);
                if(pool<1.1f)h=Mathf.Min(h,Mathf.Lerp(WatersideLayout.Height(p.x,p.z),h,Smooth(.1f,1.1f,pool)));
            }
            float best=float.MaxValue,grade=h;bool protectionTransition=false;
            foreach(var l in RuralMapConfiguration.NewCorridors)
                for(int i=1;i<l.Length;i++)
                {
                    Vector3 a=l[i-1],b=l[i],d=b-a;d.y=0;Vector3 delta=p-a;delta.y=0;
                    float t=d.sqrMagnitude<.001f?0:Mathf.Clamp01(Vector3.Dot(delta,d)/d.sqrMagnitude);
                    float distance=RuralMapConfiguration.SegmentDistance(p,a,b);
                    if(distance>=best)continue;best=distance;protectionTransition=l==RuralMapConfiguration.NewCorridors[0];
                    float ha=RuralMapConfiguration.OldShoreDistance(a)<3?Mathf.Max(1.2f,a.y):OldHeight(a);
                    float hb=RuralMapConfiguration.OldShoreDistance(b)<3?Mathf.Max(1.2f,b.y):OldHeight(b);
                    // Keep the foundation level; distribute the climb over the exposed approach, not the building centre.
                    float length=d.magnitude;var forward=d.normalized;
                    t=Mathf.InverseLerp(FoundationRun(a,forward),length-FoundationRun(b,-forward),t*length);
                    grade=Mathf.Lerp(ha,hb,t);
                }
            // Fade road grading before fixed bridge/house/habitat boundaries instead of cutting it off at the mask edge.
            if(best<3.8f&&!WatersideLayout.IsWater(p.x,p.z,.15f))h=Mathf.Lerp(h,grade,(1-Smooth(1.15f,3.8f,best))*(protectionTransition?RoadProtectionBlend(p):1));
            foreach(var s in sites)
                if(!s.p.isBoat&&s.p.id.StartsWith("A"))
                { float distance=FoundationDistance(p,s.bounds);if(distance<2.2f)h=Mathf.Lerp(h,s.ground,1-Smooth(.15f,2.2f,distance)); }
            return h;
        }
        static void Prepare(Scene scene,Placements plan)
        {
            var all=All(scene);terrain=all.Select(t=>t.GetComponent<Terrain>()).Single(t=>t);
            originalData=terrain.terrainData;origin=terrain.transform.position;size=originalData.size;
            if(Mathf.Abs(size.x-180)>.01f||Mathf.Abs(size.z-180)>.01f)throw new InvalidOperationException("Expected current 180m terrain; preserve unfamiliar extent.");
            originalHeights=originalData.GetHeights(0,0,originalData.heightmapResolution,originalData.heightmapResolution);
            protectedBounds.Clear();sites.Clear();
            var house=all.SingleOrDefault(t=>t.name=="天气屋 · v05 苔顶木构");if(!house)throw new InvalidOperationException("Fixed Weather House not found.");
            Bounds hb=BoundsOf(house.gameObject);hb.Expand(new Vector3(6,0,6));protectedBounds.Add(hb);
            foreach(var crossing in WatersideLayout.Crossings)
            {
                var bridge=all.SingleOrDefault(t=>t.name==crossing.Name);
                if(!bridge)throw new InvalidOperationException("Fixed bridge missing: "+crossing.Name);
                Bounds b=BoundsOf(bridge.gameObject);b.Expand(new Vector3(7,0,7));protectedBounds.Add(b);
            }
            foreach(var t in all.Where(t=>t.name=="岸边小桌"||t.name=="木椅"))
            { if(t.GetComponentsInChildren<Renderer>().Length==0)continue;Bounds b=BoundsOf(t.gameObject);b.Expand(new Vector3(1,0,1));protectedBounds.Add(b); }
            foreach(var fox in all.Select(t=>t.GetComponent<WildlifeAgent>()).Where(a=>a&&a.speciesId=="fox"))
            {
                // Preserve the coordinated fox home/rest terrain; do not flatten habitat to mask locomotion issues.
                protectedBounds.Add(new Bounds(fox.habitatCenter,new Vector3(12,1,12)));
                protectedBounds.Add(new Bounds(fox.transform.position,new Vector3(6,1,6)));
            }
            foreach(var p in plan.assets)
            {
                if(p.id=="T01")throw new InvalidOperationException("Tree is explicitly excluded.");
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(p.id));if(!prefab)throw new InvalidOperationException("Import prefab first: "+p.id);
                Vector3 center=XZ(p.x,p.z);if(!WithinTerrain(center))throw new InvalidOperationException("Placement center outside terrain: "+p.id);
                var b=new Bounds(center,new Vector3(p.width,1,p.depth));
                if(p.yaw%180!=0)b.size=new Vector3(p.depth,1,p.width);
                float ground=RuralMapConfiguration.OldShoreDistance(center)<3?1.5f:OldHeight(center);
                sites.Add(new Site{p=p,bounds=b,ground=ground});
                if(!p.isBoat&&p.id!="P03"&&WatersideLayout.IsWater(p.x,p.z,.1f))throw new InvalidOperationException("Dry placement is wet in new layout: "+p.id);
                if(p.isBoat&&!WatersideLayout.IsWater(p.x,p.z))throw new InvalidOperationException("Boat is on land: "+p.id);
            }
        }
        static string PrefabPath(string id)=>"Assets/Environment/RuralRebuild20261025/Prefabs/"+id+".prefab";
        static Placements ReadPlan()
        { var p=JsonUtility.FromJson<Placements>(File.ReadAllText(PlacementPath));if(p.version!=RuralMapConfiguration.Version||p.assets.Length!=17||p.assets.Select(a=>a.id).Distinct().Count()!=17)throw new InvalidDataException("Expected 17 unique approved placements.");return p; }
        public static string Preflight(string expectedSceneSha256)
        {
            var scene=RequireScene();if(!string.Equals(Hash(ScenePath),expectedSceneSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Scene changed; refresh coordinator baseline before apply.");
            if(All(scene).Any(t=>t.name==RootName))return "Already applied; run Validate instead of duplicating.";
            Prepare(scene,ReadPlan());
            if(Directory.Exists(Generated)&&Directory.GetFiles(Generated,"*",SearchOption.AllDirectories).Any(p=>!p.EndsWith(".meta")))throw new InvalidOperationException("Owned map output already exists; inspect prior run before retry.");
            var grass=All(scene).Select(t=>t.GetComponent<HillsideGrassRenderer>()).Single(g=>g&&g.terrain==terrain);
            if(!grass.placementMap||!grass.placementMap.isReadable)throw new InvalidOperationException("Readable existing grass map required.");
            var sea=All(scene).Single(t=>t.name=="海湾").GetComponent<MeshFilter>();if(!sea||!sea.sharedMesh)throw new InvalidOperationException("Existing sea surface required.");
            return "PASS preflight: 17 assets, fixed house/bridges, 180m terrain, existing sea/grass. No objects changed. Ground/visual checks still required after Apply.";
        }
        static void Remember(Transform t)
        {
            string id=existingIds[t];if(run.objects.Any(r=>r.id==id))return;
            run.objects.Add(CaptureTransform(t));
        }
        static TransformRecord CaptureTransform(Transform t)=>new TransformRecord{id=existingIds[t],name=t.name,
            parentId=t.parent?existingIds[t.parent]:"",position=t.position,localPosition=t.localPosition,
            rotation=t.rotation,localRotation=t.localRotation,scale=t.localScale,active=t.gameObject.activeSelf};
        static void VerifyUnownedTransforms(Transform[] all,Dictionary<Transform,string> ids,Dictionary<string,TransformRecord> before,HashSet<string> owned)
        {
            foreach(var t in all)
            {
                if(!t)throw new InvalidOperationException("An existing transform was destroyed.");
                string id=ids[t];if(owned.Contains(id))continue;
                var b=before[id];string parentId="";
                if(t.parent&&!ids.TryGetValue(t.parent,out parentId))throw new InvalidOperationException("Unowned transform reparented: "+t.name);
                if(parentId!=b.parentId)throw new InvalidOperationException("Unowned transform reparented: "+t.name);
                bool followsOwned=false;
                for(var parent=t.parent;parent;parent=parent.parent)
                    if(ids.TryGetValue(parent,out var ancestorId)&&owned.Contains(ancestorId)){followsOwned=true;break;}
                bool moved=followsOwned
                    ?t.localPosition!=b.localPosition||Quaternion.Angle(t.localRotation,b.localRotation)>.001f
                    :t.position!=b.position||Quaternion.Angle(t.rotation,b.rotation)>.001f;
                if(moved||t.localScale!=b.scale||t.gameObject.activeSelf!=b.active)
                    throw new InvalidOperationException("Unowned transform changed: "+t.name);
            }
        }
        static void WriteRun()=>File.WriteAllText(Review+"/latest-execution.json",JsonUtility.ToJson(run,true));
        public static string Apply(string expectedSceneSha256)
        {
            string check=Preflight(expectedSceneSha256);if(check.StartsWith("Already"))return check;
            var scene=SceneManager.GetActiveScene();var all=All(scene);
            var grass=all.Select(t=>t.GetComponent<HillsideGrassRenderer>()).Single(g=>g&&g.terrain==terrain);
            var sea=all.Single(t=>t.name=="海湾").GetComponent<MeshFilter>();
            var originalGrass=grass.placementMap;var originalSea=sea.sharedMesh;var terrainCollider=terrain.GetComponent<TerrainCollider>();
            if(!terrainCollider)throw new InvalidOperationException("Missing existing terrain collider.");
            run=new Run{folder=Review+"/Runs/"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"),sceneBeforeHash=Hash(ScenePath),status="preparing",originalTerrain=AssetDatabase.GetAssetPath(originalData),originalGrass=AssetDatabase.GetAssetPath(originalGrass),originalSea=AssetDatabase.GetAssetPath(originalSea)};
            Directory.CreateDirectory(run.folder);File.Copy(ScenePath,run.folder+"/before.unity",false);
            existingIds.Clear();foreach(var t in all)existingIds.Add(t,Id(t));
            var beforeObjects=all.ToDictionary(t=>existingIds[t],CaptureTransform);
            File.WriteAllText(run.folder+"/existing-transforms.json",JsonUtility.ToJson(new TransformList{items=beforeObjects.Values.ToArray()},true));WriteRun();
            Directory.CreateDirectory(Generated);AssetDatabase.ImportAsset(Generated,ImportAssetOptions.ForceSynchronousImport);
            var root=new GameObject(RootName);SceneManager.MoveGameObjectToScene(root,scene);
            try
            {
                var beforeAlpha=originalData.GetAlphamaps(0,0,originalData.alphamapWidth,originalData.alphamapHeight);
                // In the isolated Unity 2022.3.62f3c1 repro, CreateAsset resets an instantiated TerrainData's alphamaps.
                string terrainPath=Generated+"/Terrain.asset";
                if(!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(originalData),terrainPath))throw new InvalidOperationException("Terrain asset copy failed.");
                AssetDatabase.ImportAsset(terrainPath,ImportAssetOptions.ForceSynchronousImport);
                var data=AssetDatabase.LoadAssetAtPath<TerrainData>(terrainPath);
                if(!data)throw new InvalidOperationException("Copied TerrainData missing.");
                var alpha=data.GetAlphamaps(0,0,data.alphamapWidth,data.alphamapHeight);
                if(alpha.GetLength(0)!=beforeAlpha.GetLength(0)||alpha.GetLength(1)!=beforeAlpha.GetLength(1)||alpha.GetLength(2)!=beforeAlpha.GetLength(2))throw new InvalidOperationException("Copied terrain alpha dimensions changed.");
                for(int z=0;z<data.alphamapHeight;z++)for(int x=0;x<data.alphamapWidth;x++)for(int l=0;l<data.alphamapLayers;l++)
                    if(alpha[z,x,l]!=beforeAlpha[z,x,l])throw new InvalidOperationException("Copied terrain alpha differs from original before editing.");
                var copiedHeights=data.GetHeights(0,0,data.heightmapResolution,data.heightmapResolution);
                if(copiedHeights.GetLength(0)!=originalHeights.GetLength(0)||copiedHeights.GetLength(1)!=originalHeights.GetLength(1))throw new InvalidOperationException("Copied terrain height dimensions changed.");
                for(int z=0;z<copiedHeights.GetLength(0);z++)for(int x=0;x<copiedHeights.GetLength(1);x++)
                    if(copiedHeights[z,x]!=originalHeights[z,x])throw new InvalidOperationException("Copied terrain height differs from original before editing.");
                data.name="乡村岸线局部调整";
                var heights=(float[,])originalHeights.Clone();
                for(int z=0;z<heights.GetLength(0);z++)for(int x=0;x<heights.GetLength(1);x++)
                { var p=SamplePoint(x,z,heights.GetLength(1),heights.GetLength(0));if(!Editable(p))continue;float value=Mathf.Clamp01((TargetHeight(p)-origin.y)/size.y);if(Mathf.Abs(value-heights[z,x])>1e-7f)run.heightChanges++;heights[z,x]=value; }
                data.SetHeights(0,0,heights);
                if(data.alphamapLayers<4)throw new InvalidOperationException("Expected existing meadow/path/rock/sand layer mapping.");
                for(int z=0;z<data.alphamapHeight;z++)for(int x=0;x<data.alphamapWidth;x++)
                {
                    var p=SamplePoint(x,z,data.alphamapWidth,data.alphamapHeight);if(!Editable(p))continue;
                    float coast=WatersideLayout.SeaSignedDistance(p.x,p.z),pool=PoolDistance(p),road=CorridorDistance(p);
                    int layer=coast<2||pool<1?3:road<1.5f?1:0;float weight=road<2?1-Smooth(.9f,2,road):.8f;
                    for(int l=0;l<data.alphamapLayers;l++)alpha[z,x,l]=Mathf.Lerp(alpha[z,x,l],l==layer?1:0,weight);run.alphaChanges++;
                }
                data.SetAlphamaps(0,0,alpha);terrain.terrainData=data;terrainCollider.terrainData=data;terrain.Flush();
                var map=Object.Instantiate(originalGrass);map.name="乡村路线与岸线草图";var pixels=map.GetPixels();var beforePixels=(Color[])pixels.Clone();
                for(int z=0;z<map.height;z++)for(int x=0;x<map.width;x++)
                {
                    var p=SamplePoint(x,z,map.width,map.height);if(!Editable(p))continue;int k=z*map.width+x;var c=pixels[k];
                    float depth=Mathf.Max(0,WatersideLayout.WaterHeight(p.x,p.z)-(terrain.SampleHeight(p)+origin.y));
                    float road=CorridorDistance(p);c.g=Mathf.Clamp01(depth/8);c.r=depth>.25f?.03f:Mathf.Lerp(.12f,.82f,Smooth(.7f,2.3f,road));
                    foreach(var s in sites)if(!s.p.isBoat&&FoundationDistance(p,s.bounds)<.2f)c.r=0;pixels[k]=c;run.grassChanges++;
                }
                map.SetPixels(pixels);map.Apply(false,false);AssetDatabase.CreateAsset(map,Generated+"/Grass.asset");grass.placementMap=map;
                sea.sharedMesh=PatchSea(sea);AssetDatabase.CreateAsset(sea.sharedMesh,Generated+"/Sea.asset");
                HideReplacedAndRegroundNature(all,root.transform);
                foreach(var s in sites)Place(s,root.transform,scene);
                DressRegions(all,root.transform,scene);
                AddPoolRocks(root.transform,scene);
                AddShortPier(root.transform);
                VerifyOutside(data,beforeAlpha,beforePixels,map);
                VerifyUnownedTransforms(all,existingIds,beforeObjects,new HashSet<string>(run.objects.Select(r=>r.id)));
                foreach(var asset in new Object[]{data,map,sea.sharedMesh})AssetDatabase.SaveAssetIfDirty(asset);
                Physics.SyncTransforms();EditorSceneManager.MarkSceneDirty(scene);run.status="applied-unsaved";WriteRun();
                string result=Validate();File.WriteAllText(run.folder+"/checks.txt",result);return result+"\nScene is deliberately unsaved. Review then call Save(expected baseline hash).";
            }
            catch(Exception e)
            {
                terrain.terrainData=originalData;terrainCollider.terrainData=originalData;grass.placementMap=originalGrass;sea.sharedMesh=originalSea;
                foreach(var r in run.objects){GlobalObjectId id;if(GlobalObjectId.TryParse(r.id,out id)){var t=GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Transform;if(t){t.position=r.position;t.rotation=r.rotation;t.localScale=r.scale;t.gameObject.SetActive(r.active);}}}
                Object.DestroyImmediate(root);terrain.Flush();run.status="failed-restored-in-memory: "+e.Message;WriteRun();
                // Owned generated files remain for inspection; never delete or overwrite them automatically.
                EditorSceneManager.MarkSceneDirty(scene);throw;
            }
        }
        [Serializable] sealed class TransformList { public TransformRecord[] items; }
        static Mesh PatchSea(MeshFilter sea)
        {
            // Existing sea uses a one-metre grid. Replace only matching complete integer cells around changed coast/pools.
            var mesh=sea.sharedMesh;var vs=mesh.vertices.ToList();var uv=mesh.uv.ToList();var tris=new List<int>();var replace=new HashSet<Vector2Int>();
            for(int z=-90;z<90;z++)for(int x=-90;x<90;x++)if(ChangedShore(XZ(x+.5f,z+.5f))&&!Protected(XZ(x+.5f,z+.5f)))replace.Add(new Vector2Int(x,z));
            var old=mesh.triangles;
            for(int i=0;i<old.Length;i+=3){var p=sea.transform.TransformPoint((vs[old[i]]+vs[old[i+1]]+vs[old[i+2]])/3);if(!replace.Contains(new Vector2Int(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.z))))tris.AddRange(new[]{old[i],old[i+1],old[i+2]});}
            foreach(var cell in replace)
            {
                var corners=new[]{XZ(cell.x,cell.y),XZ(cell.x,cell.y+1),XZ(cell.x+1,cell.y+1),XZ(cell.x+1,cell.y)};
                if(!corners.Any(p=>WatersideLayout.WaterSection(p.x,p.z)==-1&&WatersideLayout.IsWater(p.x,p.z,.5f)))continue;
                int first=vs.Count;foreach(var p in corners){vs.Add(sea.transform.InverseTransformPoint(p));uv.Add(new Vector2((p.x+120)/240,(p.z+120)/240));}tris.AddRange(new[]{first,first+1,first+2,first,first+2,first+3});
            }
            var result=new Mesh{name="乡村岸线海面局部网格",indexFormat=IndexFormat.UInt32};result.SetVertices(vs);result.SetUVs(0,uv);result.SetTriangles(tris,0);result.RecalculateNormals();result.RecalculateBounds();return result;
        }
        static void HideReplacedAndRegroundNature(Transform[] all,Transform root)
        {
            foreach(var t in all)
            {
                if(t.name=="V3 潮池群 · 宽水湾与断续礁岸"){Remember(t);t.gameObject.SetActive(false);continue;}
                if(t.name=="潮池低矮礁石"&&OldPools(t.position)){Remember(t);t.gameObject.SetActive(false);continue;}
                if(t.parent&&t.parent.name=="5 海岬避雨棚"&&t.name.Contains("草棚")){Remember(t);t.gameObject.SetActive(false);continue;}
                // Only the procedural nature groups are eligible for re-grounding. Furniture and tuned structures retain transforms.
                if(!t.parent||!t.gameObject.activeSelf)continue;
                if(t.parent.name!="林冠围合 · 保留通路与水面视线"&&t.parent.name!="水岸群落 · 实体叶片与厚花穗"&&t.parent.name!="断续岩块与草帽岸线")continue;
                Vector3 p=t.position;if(!Editable(p))continue;Remember(t);
                if(WatersideLayout.IsWater(p.x,p.z,.2f)||sites.Any(s=>FoundationDistance(p,s.bounds)<.8f)){t.gameObject.SetActive(false);continue;}
                p.y+=terrain.SampleHeight(p)+origin.y-OldHeight(p);t.position=p;
            }
        }
        static void Place(Site site,Transform parent,Scene scene)
        {
            var p=site.p;var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(p.id)),scene);
            go.name=p.id+" · "+p.name;go.transform.SetParent(parent,false);go.transform.rotation=Quaternion.Euler(0,p.yaw,0);
            var b=BoundsOf(go);Vector3 target=XZ(p.x,p.z);
            float bottom=p.isBoat?-(p.id=="B02"?.65f:.20f):p.id=="P03"?1.18f:terrain.SampleHeight(target)+origin.y+.025f;
            go.transform.position+=new Vector3(target.x-b.center.x,bottom-b.min.y,target.z-b.center.z);
            // Boats are static visual berths; their deck is not yet an approved boarding route.
            if(p.isBoat)foreach(var c in go.GetComponentsInChildren<Collider>())c.enabled=false;
        }
        static void DressRegions(Transform[] all,Transform root,Scene scene)
        {
            var forest=all.SingleOrDefault(t=>t.name=="林冠围合 · 保留通路与水面视线");if(!forest)return;
            var originals=forest.Cast<Transform>().Where(t=>t.gameObject.activeSelf&&t.GetComponentsInChildren<Renderer>().Length>0).ToArray();
            foreach(var t in originals)
            {
                if(Protected(t.position)||!RuralMapConfiguration.Contains(t.position,RuralMapConfiguration.Regions[1]))continue;
                float edge=RuralMapConfiguration.LineDistance(t.position,RuralMapConfiguration.Regions[1]);if(edge<5)continue;
                Remember(t);t.gameObject.SetActive(false);
            }
            var random=new System.Random(20261025);int placed=0;
            for(int trial=0;trial<300&&placed<24&&originals.Length>0;trial++)
            {
                Vector3 p=XZ(-76+(float)random.NextDouble()*50,-15+(float)random.NextDouble()*90);
                if(!RuralMapConfiguration.Contains(p,RuralMapConfiguration.Regions[0])||Protected(p)||WatersideLayout.IsWater(p.x,p.z,3)||WatersideLayout.PathEdgeDistance(p.x,p.z)<3||sites.Any(s=>FoundationDistance(p,s.bounds)<3))continue;
                if(originals.Any(t=>Vector2.Distance(new Vector2(t.position.x,t.position.z),new Vector2(p.x,p.z))<3))continue;
                var source=originals[placed%originals.Length];var go=Object.Instantiate(source.gameObject,root);go.name="林地补植 · "+placed;go.SetActive(true);
                var b=BoundsOf(go);p.y=terrain.SampleHeight(p)+origin.y;go.transform.position+=new Vector3(p.x-b.center.x,p.y-b.min.y-.08f,p.z-b.center.z);placed++;
            }
            var reed=all.FirstOrDefault(t=>t.name=="湿岸芦苇丛"&&t.gameObject.activeSelf);
            if(reed)
            {
                int count=0;
                for(int trial=0;trial<450&&count<36;trial++)
                {
                    Vector3 p=XZ(-67+(float)random.NextDouble()*72,-88+(float)random.NextDouble()*42);
                    if(!RuralMapConfiguration.Contains(p,RuralMapConfiguration.Regions[2])||Protected(p)||WatersideLayout.PathEdgeDistance(p.x,p.z)<2||sites.Any(s=>FoundationDistance(p,s.bounds)<2))continue;
                    float shore=WatersideLayout.SeaSignedDistance(p.x,p.z);if(shore<-.4f||shore>7)continue;
                    var go=Object.Instantiate(reed.gameObject,root);go.name="芦苇湾连续湿岸 · "+count;
                    go.transform.position=new Vector3(p.x,terrain.SampleHeight(p)+origin.y-.03f,p.z);go.transform.rotation=Quaternion.Euler(0,(float)random.NextDouble()*360,0);count++;
                }
            }
        }
        static void AddShortPier(Transform parent)
        {
            var mat=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/World/WatersideV2/Generated/WeatheredTimber.mat");
            if(!mat)throw new InvalidOperationException("Existing timber material missing; do not create unrelated replacement.");
            Vector3 a=XZ(79.8f,-63),b=XZ(86.4f,-68.4f);a.y=b.y=1.1f;float length=Vector3.Distance(a,b);Quaternion q=Quaternion.LookRotation(b-a);
            int count=Mathf.CeilToInt(length/.55f);
            for(int i=0;i<count;i++)
            {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="渡口短栈道 "+i;go.transform.SetParent(parent,false);go.transform.SetPositionAndRotation(Vector3.Lerp(a,b,(i+.5f)/count),q);go.transform.localScale=new Vector3(1.65f,.16f,length/count+.015f);go.GetComponent<Renderer>().sharedMaterial=mat;}
        }
        static void AddPoolRocks(Transform parent,Scene scene)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HIGHLANDS - Stylized Environment/Prefabs/Forest/Rock_1.prefab");
            if(!source)throw new InvalidOperationException("Existing shoreline rock asset missing.");
            int index=0;
            foreach(var pool in WatersideLayout.TidePools)
                foreach(float degree in new[]{25f,112f,238f})
                {
                    float a=(degree+index*19)*Mathf.Deg2Rad;
                    Vector3 p=pool.Center+new Vector3(Mathf.Cos(a)*pool.Radii.x*1.15f,0,Mathf.Sin(a)*pool.Radii.y*1.15f);
                    if(Protected(p)||WatersideLayout.WalkablePathEdgeDistance(p.x,p.z)<.8f)continue;
                    var go=(GameObject)PrefabUtility.InstantiatePrefab(source,scene);go.name="新潮池断续礁石 · "+index++;go.transform.SetParent(parent,false);
                    go.transform.rotation=Quaternion.Euler(0,degree+index*23,0);var b=BoundsOf(go);go.transform.localScale*=1.1f/Mathf.Max(b.size.x,b.size.z);
                    b=BoundsOf(go);go.transform.position+=new Vector3(p.x-b.center.x,terrain.SampleHeight(p)+origin.y-b.min.y-b.size.y*.35f,p.z-b.center.z);
                }
        }
        static void VerifyOutside(TerrainData data,float[,,] alpha,Color[] grass,Texture2D map)
        {
            var h=data.GetHeights(0,0,data.heightmapResolution,data.heightmapResolution);
            for(int z=0;z<h.GetLength(0);z++)for(int x=0;x<h.GetLength(1);x++)if(!Editable(SamplePoint(x,z,h.GetLength(1),h.GetLength(0)))&&Mathf.Abs(h[z,x]-originalHeights[z,x])>1.6f/65535)throw new InvalidOperationException("Height outside patch changed.");
            var a=data.GetAlphamaps(0,0,data.alphamapWidth,data.alphamapHeight);
            for(int z=0;z<a.GetLength(0);z++)for(int x=0;x<a.GetLength(1);x++)if(!Editable(SamplePoint(x,z,a.GetLength(1),a.GetLength(0))))for(int l=0;l<a.GetLength(2);l++)if(Mathf.Abs(a[z,x,l]-alpha[z,x,l])>1.1f/255)throw new InvalidOperationException("Alpha outside patch changed.");
            var c=map.GetPixels();for(int z=0;z<map.height;z++)for(int x=0;x<map.width;x++)if(!Editable(SamplePoint(x,z,map.width,map.height))&&c[z*map.width+x]!=grass[z*map.width+x])throw new InvalidOperationException("Grass outside patch changed.");
        }
        public static string Validate()
        {
            var scene=SceneManager.GetActiveScene();if(scene.path!=ScenePath)throw new InvalidOperationException("Wrong scene.");
            var root=All(scene).SingleOrDefault(t=>t.name==RootName);if(!root)throw new InvalidOperationException("No applied root.");
            var ids=root.Cast<Transform>().Where(t=>t.name.Length>3&&t.name[3]==' ').Select(t=>t.name.Substring(0,3)).ToArray();
            var plan=ReadPlan();foreach(var p in plan.assets)if(ids.Count(id=>id==p.id)!=1)throw new InvalidOperationException("Missing or duplicate asset: "+p.id);
            if(root.GetComponentsInChildren<Renderer>(true).Any(r=>r.sharedMaterials.Any(m=>!m)))throw new InvalidOperationException("Missing placed material.");
            if(All(scene).Any(t=>t.IsChildOf(root)&&t.name.StartsWith("T01")))throw new InvalidOperationException("Excluded tree present.");
            var ground=All(scene).Select(t=>t.GetComponent<Terrain>()).Single(t=>t);
            foreach(var p in plan.assets.Where(p=>!p.isBoat&&p.id!="P03"))
            {
                var placed=root.Cast<Transform>().Single(t=>t.name.StartsWith(p.id+" · "));var b=BoundsOf(placed.gameObject);
                float h=ground.SampleHeight(b.center)+ground.transform.position.y;
                if(Mathf.Abs(b.min.y-h)>.12f)throw new InvalidOperationException("Grounding mismatch: "+p.id);
                if(p.id.StartsWith("A")&&placed.GetComponentsInChildren<Collider>().Length==0)throw new InvalidOperationException("Missing structural collision: "+p.id);
            }
            float maxGrade=0;int checkedSamples=0;
            foreach(var line in RuralMapConfiguration.NewCorridors)
                for(int i=1;i<line.Length;i++)
                {
                    Vector3 a=line[i-1],b=line[i];a.y=b.y=0;float length=Vector3.Distance(a,b);Vector3 forward=(b-a).normalized;
                    for(float d=0;d<=length;d+=.75f)
                    {
                        Vector3 p=Vector3.Lerp(a,b,length<.01f?0:d/length);
                        // The last metres at an asset terminate at its doorway; doorway passage is reviewed with the collision proxy.
                        if(plan.assets.Any(s=>!s.isBoat&&Mathf.Abs(p.x-s.x)<(s.yaw%180==0?s.width:s.depth)*.5f+1&&Mathf.Abs(p.z-s.z)<(s.yaw%180==0?s.depth:s.width)*.5f+1))continue;
                        if(WatersideLayout.IsWater(p.x,p.z,.1f)&&WatersideLayout.CrossingAt(p.x,p.z,.5f)==null)throw new InvalidOperationException("New path enters unplanned water at "+p);
                        float rise=Mathf.Abs(ground.SampleHeight(p+forward*.3f)-ground.SampleHeight(p-forward*.3f));float grade=Mathf.Atan2(rise,.6f)*Mathf.Rad2Deg;
                        maxGrade=Mathf.Max(maxGrade,grade);checkedSamples++;
                        if(grade>34)throw new InvalidOperationException("New path exceeds 34 degrees at "+p+" grade="+grade);
                    }
                }
            return "PASS structural check: 17 distinct asset placements, ground alignment, architectural colliders, no excluded tree, mapped materials; new-route samples="+checkedSamples+", maximum grade="+maxGrade.ToString("F2")+". Outside-mask terrain/alpha/grass checked during Apply. Unity visual and doorway/player walking checks remain required.";
        }
        public static string Save(string expectedOriginalSceneHash)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("Wait for edit mode.");
            if(run==null)run=JsonUtility.FromJson<Run>(File.ReadAllText(Review+"/latest-execution.json"));
            if(run.status!="applied-unsaved"||Hash(ScenePath)!=expectedOriginalSceneHash||run.sceneBeforeHash!=expectedOriginalSceneHash)throw new InvalidOperationException("Scene disk version changed; do not overwrite.");
            string result=Validate();if(!EditorSceneManager.SaveScene(SceneManager.GetActiveScene()))throw new IOException("SaveScene failed.");
            run.sceneSavedHash=Hash(ScenePath);run.status="saved-awaiting-runtime-review";WriteRun();return result+"\nSaved hash="+run.sceneSavedHash;
        }
        public static void RollbackSaved(string expectedCurrentSceneHash)
        {
            RequireScene();var saved=JsonUtility.FromJson<Run>(File.ReadAllText(Review+"/latest-execution.json"));
            if(saved.sceneSavedHash!=expectedCurrentSceneHash||Hash(ScenePath)!=expectedCurrentSceneHash)throw new InvalidOperationException("New work exists; automatic rollback refused.");
            File.Copy(saved.folder+"/before.unity",ScenePath,true);AssetDatabase.ImportAsset(ScenePath);EditorSceneManager.OpenScene(ScenePath);
            saved.status="scene-restored; coordinator must reverse Layout source patch before Play";run=saved;WriteRun();
        }
    }
}
