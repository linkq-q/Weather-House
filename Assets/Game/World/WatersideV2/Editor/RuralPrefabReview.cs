using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    // Read-only review of imported prefabs. No import/save/refresh, asset setters or main-scene mutations.
    public static class RuralPrefabReview
    {
        public const string Output = "JourneyReview/Revision_20261024/MapIntegrationPlan/PrefabReview_20261025";
        [Serializable] sealed class Manifest
        { public RuralAssetImport.BoundsEntry bounds,doorOpeningBounds;public RuralAssetImport.MaterialEntry[] materials;public RuralAssetImport.CollisionEntry[] colliders; }
        [Serializable] public sealed class LodReport
        { public int level,renderers,uniqueMeshes,materialSlots,uniqueMaterials,glassRenderers;public long triangles;public Vector3 dimensions;public bool dimensionsMatch; }
        [Serializable] public sealed class Item
        {
            public string id,prefab,structuralStatus,doorStatus,previewStatus,previewPath;
            public int colliderCount,boxColliders,doorProbeCount;public float doorProbeRadius=.2f;
            public List<LodReport> lods=new List<LodReport>();public List<string> failures=new List<string>();
        }
        [Serializable] public sealed class Report
        {
            public string utc,scope="Imported prefab metadata and sampled doorway collision clearance; preview lighting is not main-map acceptance.";
            public bool renderRequested,mainSceneStatePreserved;public int passed,failed,previews;
            public List<Item> assets=new List<Item>();
        }
        static void Check(bool condition,string reason,Item item){if(!condition)item.failures.Add(reason);}
        static Vector3 V(float[] a)=>new Vector3(a[0],a[1],a[2]);
        static Bounds BoundsOf(Renderer[] renderers)
        { if(renderers.Length==0)throw new InvalidDataException("LOD has no renderer.");var b=renderers[0].bounds;foreach(var r in renderers.Skip(1))b.Encapsulate(r.bounds);return b; }
        static string SceneState()
        {
            var active=SceneManager.GetActiveScene();var b=new StringBuilder(active.handle+"|");
            for(int i=0;i<SceneManager.sceneCount;i++){var s=SceneManager.GetSceneAt(i);b.Append(s.handle).Append('|').Append(s.path).Append('|').Append(s.isDirty).Append(';');}return b.ToString();
        }
        public static string RunAll(bool renderPreviews = true)
        { return Run(RuralAssetImport.Ids,renderPreviews); }
        public static string RunOne(string id,bool renderPreview = true)
        { if(!RuralAssetImport.Ids.Contains(id))throw new ArgumentException("Unknown asset ID",nameof(id));return Run(new[]{id},renderPreview); }
        static string Run(string[] ids,bool render)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("Review in edit mode after compilation. No Unity state changed.");
            string before=SceneState();Directory.CreateDirectory(Output);
            var report=new Report{utc=DateTime.UtcNow.ToString("o"),renderRequested=render};
            try
            {
                foreach(string id in ids)
                {
                    var item=new Item{id=id,prefab=RuralAssetImport.PrefabPath(id),doorStatus="not_checked",previewStatus=render?"not_attempted":"not_requested"};
                    report.assets.Add(item);Scene previewScene=default;GameObject instance=null;
                    try
                    {
                        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(item.prefab);if(!prefab)throw new FileNotFoundException("Imported prefab missing",item.prefab);
                        string manifestPath=RuralAssetImport.TargetRoot+"/"+id+"/import.json";
                        var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
                        previewScene=EditorSceneManager.NewPreviewScene();
                        instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,previewScene);
                        Audit(instance,manifest,item);
                        if(render)
                        {
                            try { item.previewPath=RenderPreview(instance,id);item.previewStatus="rendered_nonuniform_image; visual_review_pending";report.previews++; }
                            catch(Exception e){item.previewStatus="unavailable: "+e.GetType().Name+": "+e.Message;item.previewPath=null;}
                        }
                    }
                    catch(Exception e){item.failures.Add(e.GetType().Name+": "+e.Message);}
                    finally
                    {
                        if(instance)Object.DestroyImmediate(instance);
                        if(previewScene.IsValid())EditorSceneManager.ClosePreviewScene(previewScene);
                    }
                    item.structuralStatus=item.failures.Count==0?"passed":"failed";
                    if(item.failures.Count==0)report.passed++;else report.failed++;
                }
            }
            finally
            {
                report.mainSceneStatePreserved=before==SceneState();
                string suffix=ids.Length==1?ids[0]:"all";
                File.WriteAllText(Output+"/review-"+suffix+".json",JsonUtility.ToJson(report,true));
                WriteGallery(report,Output+"/review-"+suffix+".html");
            }
            if(!report.mainSceneStatePreserved)throw new InvalidOperationException("Main scene state changed during review; inspect report. No auto-save or reset was attempted.");
            return "Prefab review: "+report.passed+" passed, "+report.failed+" failed; "+report.previews+" previews. Report="+Output+"/review-"+(ids.Length==1?ids[0]:"all")+".json";
        }
        static void Audit(GameObject instance,Manifest manifest,Item item)
        {
            if(manifest==null||manifest.bounds==null||manifest.materials==null||manifest.colliders==null)throw new InvalidDataException("Incomplete import manifest.");
            var groups=instance.GetComponentsInChildren<LODGroup>(true);Check(groups.Length==1,"Expected one LODGroup",item);if(groups.Length!=1)return;
            var lods=groups[0].GetLODs();Check(lods.Length==3,"Expected three LOD levels",item);
            var glass=new HashSet<Material>();var declaredMaterials=new HashSet<Material>();
            for(int i=0;i<manifest.materials.Length;i++)
            {
                string path=RuralAssetImport.TargetRoot+"/"+item.id+"/Materials/M"+i.ToString("D3")+".mat";
                var mat=AssetDatabase.LoadAssetAtPath<Material>(path);Check(mat,"Missing declared material "+path,item);
                if(mat){declaredMaterials.Add(mat);if(manifest.materials[i].isGlass)glass.Add(mat);}
            }
            Vector3 expected=V(manifest.bounds.dimensions);
            var rendererOwners=new HashSet<Renderer>();
            for(int level=0;level<lods.Length;level++)
            {
                var rs=lods[level].renderers.Where(r=>r).ToArray();Check(rs.Length==lods[level].renderers.Length,"Null LOD renderer",item);
                var result=new LodReport{level=level,renderers=rs.Length};item.lods.Add(result);
                var meshes=new HashSet<Mesh>();var mats=new HashSet<Material>();var seenGlass=new HashSet<Material>();
                foreach(var r in rs)
                {
                    Check(rendererOwners.Add(r),"Renderer reused by multiple LOD levels: "+r.name,item);
                    var mf=r.GetComponent<MeshFilter>();Mesh mesh=mf?mf.sharedMesh:(r as SkinnedMeshRenderer)?.sharedMesh;
                    Check(mesh,"Missing mesh: "+r.name,item);if(mesh){meshes.Add(mesh);for(int s=0;s<mesh.subMeshCount;s++){Check(mesh.GetTopology(s)==MeshTopology.Triangles,"Non-triangle submesh: "+r.name,item);result.triangles+=(long)mesh.GetIndexCount(s)/3;}}
                    var materials=r.sharedMaterials;result.materialSlots+=materials.Length;
                    foreach(var m in materials){Check(m,"Missing material: "+r.name,item);if(m){mats.Add(m);Check(declaredMaterials.Contains(m),"Renderer uses non-manifest material reference: "+r.name+" -> "+AssetDatabase.GetAssetPath(m),item);}}
                    bool hasGlass=materials.Any(m=>m&&glass.Contains(m));
                    if(hasGlass)
                    {
                        result.glassRenderers++;foreach(var m in materials.Where(m=>m&&glass.Contains(m)))seenGlass.Add(m);
                        Check(materials.All(m=>m&&glass.Contains(m)),"Glass and opaque material share Renderer: "+r.name,item);
                        Check(r.shadowCastingMode==ShadowCastingMode.Off,"Glass Renderer casts shadow: "+r.name,item);
                    }
                }
                foreach(var g in glass)
                {
                    Check(seenGlass.Contains(g),"LOD "+level+" missing declared glass "+g.name,item);
                    Check(g.HasProperty("_Surface")&&g.GetFloat("_Surface")==1&&g.renderQueue>=3000,"Glass not transparent: "+g.name,item);
                    Check(g.HasProperty("_ZWrite")&&g.GetFloat("_ZWrite")==0,"Glass writes opaque depth: "+g.name,item);
                }
                result.uniqueMeshes=meshes.Count;result.uniqueMaterials=mats.Count;result.dimensions=BoundsOf(rs).size;
                result.dimensionsMatch=Enumerable.Range(0,3).All(axis=>Mathf.Abs(result.dimensions[axis]-expected[axis])<=Mathf.Max(.05f,expected[axis]*.02f));
                Check(result.dimensionsMatch,"LOD "+level+" dimensions differ from verified source by >2%/5cm",item);
            }
            var colliders=instance.GetComponentsInChildren<Collider>(true);item.colliderCount=colliders.Length;item.boxColliders=colliders.OfType<BoxCollider>().Count();
            Check(item.colliderCount==manifest.colliders.Length,"Collider count differs from structural manifest",item);
            Check(item.boxColliders==item.colliderCount,"Unexpected non-box collider; doorway sampler cannot certify it",item);
            if(item.id.StartsWith("A"))Check(item.boxColliders>0,"Building lacks structural collision",item);
            var door=manifest.doorOpeningBounds;
            if(door==null||door.min==null||door.max==null)
            { item.doorStatus=item.id.StartsWith("A")?"open_structure_no_declared_door; player_walkthrough_required":"no_declared_door";return; }
            Vector3 lo=V(door.min),hi=V(door.max),center=(lo+hi)*.5f;int thin=hi.x-lo.x<hi.z-lo.z?0:2;
            var blocked=new HashSet<string>();
            foreach(float offset in new[]{-.3f,0,.3f})foreach(float t in new[]{.2f,.5f,.8f})
            {
                Vector3 p=center;p.y=Mathf.Lerp(lo.y+.2f,hi.y-.2f,t);p[thin]+=offset;p=instance.transform.TransformPoint(p);item.doorProbeCount++;
                foreach(var box in colliders.OfType<BoxCollider>().Where(c=>c.enabled&&!c.isTrigger&&c.gameObject.activeInHierarchy))
                {
                    Vector3 local=box.transform.InverseTransformPoint(p)-box.center,half=box.size*.5f;
                    Vector3 q=new Vector3(Mathf.Clamp(local.x,-half.x,half.x),Mathf.Clamp(local.y,-half.y,half.y),Mathf.Clamp(local.z,-half.z,half.z));
                    float distance=Vector3.Distance(p,box.transform.TransformPoint(q+box.center));if(distance<item.doorProbeRadius-.001f)blocked.Add(box.name);
                }
            }
            item.doorStatus=blocked.Count==0?"sampled_0.4m_diameter_center_corridor_clear; full_player_capsule_not_tested":"blocked: "+string.Join(", ",blocked);
            Check(blocked.Count==0,"Doorway sample intersects structural proxy: "+string.Join(", ",blocked),item);
        }
        static string RenderPreview(GameObject instance,string id)
        {
            PreviewRenderUtility preview=null;Texture2D image=null;
            try
            {
                preview=new PreviewRenderUtility();preview.AddSingleGO(instance);
                var group=instance.GetComponentInChildren<LODGroup>();group.ForceLOD(0);
                var lods=group.GetLODs();for(int i=0;i<lods.Length;i++)foreach(var r in lods[i].renderers)r.enabled=i==0;
                Bounds b=BoundsOf(lods[0].renderers);var camera=preview.camera;
                camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.76f,.79f,.74f,1);
                camera.transform.position=b.center+new Vector3(1,1,1).normalized*(b.size.magnitude*2+2);camera.transform.LookAt(b.center);
                float extent=.2f;for(int mask=0;mask<8;mask++){var c=new Vector3((mask&1)==0?b.min.x:b.max.x,(mask&2)==0?b.min.y:b.max.y,(mask&4)==0?b.min.z:b.max.z);var p=camera.transform.InverseTransformPoint(c);extent=Mathf.Max(extent,Mathf.Abs(p.x),Mathf.Abs(p.y));}
                camera.orthographicSize=extent*1.12f;camera.nearClipPlane=.01f;camera.farClipPlane=b.size.magnitude*8+50;
                preview.ambientColor=new Color(.55f,.58f,.55f);
                preview.lights[0].intensity=1.1f;preview.lights[0].color=new Color(1,.95f,.83f);preview.lights[0].transform.rotation=Quaternion.Euler(45,-35,0);
                preview.lights[1].intensity=.45f;preview.lights[1].color=new Color(.78f,.85f,1);preview.lights[1].transform.rotation=Quaternion.Euler(25,140,0);
                preview.BeginStaticPreview(new Rect(0,0,640,640));preview.Render(true);image=preview.EndStaticPreview();
                if(!image)throw new InvalidOperationException("PreviewRenderUtility returned no texture; static audit remains available.");
                var pixels=image.GetPixels32();byte min=255,max=0;
                for(int i=0;i<pixels.Length;i+=97){var p=pixels[i];byte l=(byte)((p.r+p.g+p.b)/3);min=Math.Min(min,l);max=Math.Max(max,l);}
                if(max-min<8)throw new InvalidOperationException("Preview is nearly uniform; URP preview may be unsupported. No successful image claimed.");
                string filename=Output+"/"+id+"_isometric.png";File.WriteAllBytes(filename,image.EncodeToPNG());return filename;
            }
            finally {if(image)Object.DestroyImmediate(image);if(preview!=null)preview.Cleanup();}
        }
        static string Escape(string s)=>System.Net.WebUtility.HtmlEncode(s??"");
        static void WriteGallery(Report report,string filename)
        {
            var h=new StringBuilder("<!doctype html><meta charset='utf-8'><meta name='viewport' content='width=device-width'><title>Prefab只读审查</title><style>body{font:16px system-ui;background:#eeeadd;color:#25342c;max-width:1100px;margin:24px auto;padding:16px}article{margin:28px 0}img{width:min(100%,640px);height:auto}pre{white-space:pre-wrap}</style><h1>Prefab只读审查</h1><p>等距预览使用原材质与独立灯光；不代表主地图视觉验收。门洞仅做局部采样。</p>");
            foreach(var item in report.assets){h.Append("<article><h2>").Append(Escape(item.id+" · "+item.structuralStatus)).Append("</h2><p>").Append(Escape(item.doorStatus)).Append("</p><p>").Append(Escape(item.previewStatus)).Append("</p>");if(item.previewPath!=null)h.Append("<img alt='").Append(item.id).Append("等距预览' src='").Append(Escape(Path.GetFileName(item.previewPath))).Append("'>");if(item.failures.Count>0)h.Append("<pre>").Append(Escape(string.Join("\n",item.failures))).Append("</pre>");h.Append("</article>");}
            File.WriteAllText(filename,h.ToString());
        }
    }
}
