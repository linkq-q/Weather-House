using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Run in an isolated project copy: fixes only A01's three atlas meshes and their prefab references.
public static class GlasshouseJoinRepair
{
    const string PrefabPath="Assets/Environment/RuralRebuild20261025/Prefabs/A01.prefab";
    const string Folder="Assets/Environment/GlasshouseJoinRepair20261030";
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static void Batch()
    {
        Require(Application.isBatchMode,"Repair is restricted to the isolated batch project.");
        string report=Environment.GetEnvironmentVariable("WEATHERHOUSE_GLASSHOUSE_REPORT")??"Temp/GlasshouseJoinRepair";
        Directory.CreateDirectory(report);
        var lines=new List<string>();int exit=0;
        try{Run(report,lines);CaptureComparison(report,lines);}catch(Exception e){lines.Add("FAIL "+e);exit=1;}
        File.WriteAllLines(Path.Combine(report,"checks.txt"),lines);
        foreach(string line in lines)Debug.Log(line);
        if(Application.isBatchMode)EditorApplication.Exit(exit);
    }
    public static void CaptureComparisonBatch()
    {
        Require(Application.isBatchMode,"Comparison capture is restricted to the isolated batch project.");
        string report=Environment.GetEnvironmentVariable("WEATHERHOUSE_GLASSHOUSE_REPORT")??"Temp/GlasshouseJoinRepair";
        Directory.CreateDirectory(report);var lines=new List<string>();int exit=0;
        try{CaptureComparison(report,lines);}
        catch(Exception e){exit=1;lines.Add("FAIL "+e);}
        File.WriteAllLines(Path.Combine(report,"capture-checks.txt"),lines);
        foreach(string line in lines)Debug.Log(line);
        EditorApplication.Exit(exit);
    }
    static void CaptureComparison(string report,List<string> lines)
    {
        Require(Application.isBatchMode,"Comparison capture is restricted to the isolated batch project.");
        {
            // Prefab contents use a preview-stage culling mask; use an ordinary scene for evidence cameras.
            Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),scene);
            var lod=root.GetComponent<LODGroup>();Require(lod,"Missing LODGroup");
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.5f,.5f,.5f);
            var parts=new MeshFilter[3];var repaired=new Mesh[3];
            for(int level=0;level<3;level++)
            {
                parts[level]=root.transform.Find("LOD"+level).GetComponentsInChildren<MeshFilter>(true).Single(m=>m.name=="A01_BakedMesh.001");
                repaired[level]=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/A01_JoinedWalls_LOD"+level+".asset");
                Require(repaired[level],"Missing repaired mesh LOD"+level);
                string original="Assets/Environment/BuildingRoofHarmony20261027/A01/A01_RoofHarmony_LOD"+level+".fbx";
                parts[level].sharedMesh=AssetDatabase.LoadAssetAtPath<GameObject>(original).GetComponentsInChildren<MeshFilter>(true).Single(m=>m.name=="A01_BakedMesh.001").sharedMesh;
            }
            Capture(root,lod,report,"before");
            for(int level=0;level<3;level++)parts[level].sharedMesh=repaired[level];
            Capture(root,lod,report,"after");
            lines.Add("PASS eight nonblank original PNGs captured from a normal scene, both front corners; only atlas mesh changed between before/after");
            lines.Add("PASS no prefab, mesh, material or source scene files saved during comparison capture");
        }
    }
    static void Run(string report,List<string> lines)
    {
        GameObject root=PrefabUtility.LoadPrefabContents(PrefabPath);
        var meshes=new List<Mesh>();var parts=new List<MeshFilter>();
        try
        {
            var lod=root.GetComponentInChildren<LODGroup>();Require(lod,"A01 LODGroup missing");
            for(int level=0;level<3;level++)
            {
                Transform branch=root.transform.Find("LOD"+level);Require(branch,"LOD branch missing: "+level);
                MeshFilter part=branch.GetComponentsInChildren<MeshFilter>(true).Single(m=>m.name=="A01_BakedMesh.001");
                string baseline="Assets/Environment/BuildingRoofHarmony20261027/A01/A01_RoofHarmony_LOD"+level+".fbx";
                Mesh source=AssetDatabase.LoadAssetAtPath<GameObject>(baseline).GetComponentsInChildren<MeshFilter>(true).Single(m=>m.name=="A01_BakedMesh.001").sharedMesh;
                Require(AssetDatabase.GetAssetPath(source).Contains("BuildingRoofHarmony20261027"),"Unexpected source mesh; repair must preserve roof harmony");
                Mesh fixedMesh=UnityEngine.Object.Instantiate(source);fixedMesh.name="A01_JoinedWalls_LOD"+level;meshes.Add(fixedMesh);
                Vector3[] vertices=fixedMesh.vertices,original=source.vertices;
                Matrix4x4 toRoot=root.transform.worldToLocalMatrix*part.transform.localToWorldMatrix;
                Matrix4x4 toMesh=toRoot.inverse;
                float before=OverlapArea(source,toRoot);Require(before>.02f,"Expected pillar/wall overlap missing at LOD"+level+": "+before);
                int changed=0;
                for(int i=0;i<vertices.Length;i++)
                {
                    Vector3 p=toRoot.MultiplyPoint3x4(vertices[i]);
                    bool height=p.y>.66f && p.y<2.96f;
                    bool gable=height && Near(Mathf.Abs(p.x),1.775f) && (Near(Mathf.Abs(p.z),2.525f)||Near(Mathf.Abs(p.z),2.725f));
                    bool side=height && (Near(Mathf.Abs(p.x),1.675f)||Near(Mathf.Abs(p.x),1.875f)) && Near(Mathf.Abs(p.z),2.625f);
                    if(!gable && !side)continue;
                    // Tuck the plaster 5 mm into the post's inner edge; its visible front no longer overlaps.
                    if(gable)p.x=Mathf.Sign(p.x)*1.680f;
                    if(side)p.z=Mathf.Sign(p.z)*2.530f;
                    vertices[i]=toMesh.MultiplyPoint3x4(p);changed++;
                }
                Require(changed>=48 && changed<500,"Unexpected wall endpoint count: "+changed);
                fixedMesh.vertices=vertices;fixedMesh.RecalculateBounds();
                float after=OverlapArea(fixedMesh,toRoot);Require(after<.00001f,"Coplanar wall/post overlap remains at LOD"+level+": "+after);
                Require(source.vertexCount==fixedMesh.vertexCount && source.uv.SequenceEqual(fixedMesh.uv) && source.normals.SequenceEqual(fixedMesh.normals),"Mesh attributes changed beyond endpoints");
                for(int sub=0;sub<source.subMeshCount;sub++)Require(source.GetIndices(sub).SequenceEqual(fixedMesh.GetIndices(sub)),"Topology changed");
                Require(vertices.Where((v,i)=>v!=original[i]).Count()==changed,"Unexpected unrelated vertex edits");
                part.sharedMesh=fixedMesh;parts.Add(part);
                lines.Add("PASS LOD"+level+" endpoints="+changed+" coplanar_overlap_m2="+before.ToString("F6")+" -> "+after.ToString("F6")+" vertices="+source.vertexCount+" topology/UV/normals preserved");
            }
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/Environment","GlasshouseJoinRepair20261030");
            for(int level=0;level<meshes.Count;level++)
            {
                Mesh mesh=meshes[level];
                string path=Folder+"/"+mesh.name+".asset";
                Mesh existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(existing)
                {
                    Require(existing.name==mesh.name,"Unexpected mesh at repair output path");
                    EditorUtility.CopySerialized(mesh,existing);EditorUtility.SetDirty(existing);AssetDatabase.SaveAssetIfDirty(existing);parts[level].sharedMesh=existing;
                }
                else{Require(!File.Exists(path),"Unexpected non-mesh repair asset");AssetDatabase.CreateAsset(mesh,path);AssetDatabase.SaveAssetIfDirty(mesh);}
            }
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            lines.Add("PASS saved only 3 mesh assets and A01 prefab mesh references; original FBX, materials, colliders and scene untouched");
        }
        finally{PrefabUtility.UnloadPrefabContents(root);foreach(Mesh mesh in meshes)if(mesh && !AssetDatabase.Contains(mesh))UnityEngine.Object.DestroyImmediate(mesh);}
    }
    static bool Near(float a,float b)=>Mathf.Abs(a-b)<.002f;
    static float OverlapArea(Mesh mesh,Matrix4x4 matrix)
    {
        Vector3[] points=mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();float area=0;
        int[] tri=mesh.triangles;
        for(int i=0;i<tri.Length;i+=3)
        {
            Vector3 a=points[tri[i]],b=points[tri[i+1]],c=points[tri[i+2]];
            for(int sign=-1;sign<=1;sign+=2)
            {
                if(Near(a.z,sign*2.725f)&&Near(b.z,sign*2.725f)&&Near(c.z,sign*2.725f) && Mathf.Max(a.x,b.x,c.x)-Mathf.Min(a.x,b.x,c.x)>.3f)
                    for(int corner=-1;corner<=1;corner+=2)area+=ClipArea(new[]{new Vector2(a.x,a.y),new Vector2(b.x,b.y),new Vector2(c.x,c.y)},corner*1.775f-.077f,corner*1.775f+.077f);
                for(int side=-1;side<=1;side+=2)
                    if(Near(a.x,side*1.875f)&&Near(b.x,side*1.875f)&&Near(c.x,side*1.875f) && Mathf.Max(a.z,b.z,c.z)-Mathf.Min(a.z,b.z,c.z)>.3f)
                        area+=ClipArea(new[]{new Vector2(a.z,a.y),new Vector2(b.z,b.y),new Vector2(c.z,c.y)},sign*2.625f-.077f,sign*2.625f+.077f);
            }
        }
        return area;
    }
    static float ClipArea(Vector2[] triangle,float lo,float hi)
    {
        var polygon=new List<Vector2>(triangle);
        for(int edge=0;edge<4 && polygon.Count>0;edge++)
        {
            var clipped=new List<Vector2>();int axis=edge<2?0:1;float limit=edge==0?lo:edge==1?hi:edge==2?1.023f:2.927f;bool lower=edge%2==0;
            Vector2 previous=polygon[polygon.Count-1];bool was=lower?previous[axis]>=limit:previous[axis]<=limit;
            foreach(Vector2 current in polygon)
            {
                bool inside=lower?current[axis]>=limit:current[axis]<=limit;
                if(inside!=was)clipped.Add(Vector2.Lerp(previous,current,(limit-previous[axis])/(current[axis]-previous[axis])));
                if(inside)clipped.Add(current);previous=current;was=inside;
            }
            polygon=clipped;
        }
        float area=0;
        for(int i=0;i<polygon.Count;i++){Vector2 a=polygon[i],b=polygon[(i+1)%polygon.Count];area+=a.x*b.y-b.x*a.y;}
        return Mathf.Abs(area)*.5f;
    }
    static void Capture(GameObject root,LODGroup lod,string report,string phase)
    {
        var camera=new GameObject("Repair evidence camera").AddComponent<Camera>();SceneManager.MoveGameObjectToScene(camera.gameObject,root.scene);camera.scene=root.scene;
        var light=new GameObject("Repair evidence light").AddComponent<Light>();SceneManager.MoveGameObjectToScene(light.gameObject,root.scene);
        RenderTexture target=null;Texture2D pixels=null;
        try
        {
            light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(45,-35,0);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.35f,.39f,.36f);camera.orthographic=true;camera.orthographicSize=1.15f;
            target=new RenderTexture(1280,720,24);camera.targetTexture=target;pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
            lod.ForceLOD(0);
            for(int angle=0;angle<4;angle++)
            {
                float side=angle<2?-1:1;
                Vector3 focus=new Vector3(side*1.65f,1.95f,2.625f);
                camera.transform.position=focus+(angle%2==0?new Vector3(side*2,1.2f,4):new Vector3(side*3,1,3));camera.transform.LookAt(focus);camera.Render();
                RenderTexture previous=RenderTexture.active;RenderTexture.active=target;
                try{pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();}finally{RenderTexture.active=previous;}
                var colors=new HashSet<int>();
                foreach(Color32 pixel in pixels.GetPixels32())
                {colors.Add(pixel.r<<16|pixel.g<<8|pixel.b);if(colors.Count>64)break;}
                Require(colors.Count>64,"Blank or incomplete evidence render: "+phase+" angle "+angle);
                File.WriteAllBytes(Path.Combine(report,"pillar-"+phase+"-"+angle+".png"),pixels.EncodeToPNG());
            }
        }
        finally{lod.ForceLOD(-1);if(target)UnityEngine.Object.DestroyImmediate(target);if(pixels)UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(light.gameObject);}
    }
}
