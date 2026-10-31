using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.DrawerPrototype.Editor
{
    public static partial class DrawerPrototypeEditor
    {
        static void ImportRefinedArt()
        {
            const string folder=Root+"/Resources/DrawerPrototype/RefinedProps/";
            foreach(string file in new[]{"CompanionPhoto","GrandmaLetterPaper","CameraLeather"})
            {
                string path=folder+file+".png";
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                Require(importer,"Missing refined prop texture: "+path);
                if(importer.maxTextureSize==2048 && importer.textureCompression==TextureImporterCompression.Uncompressed &&
                    importer.wrapMode==(file=="CameraLeather"?TextureWrapMode.Repeat:TextureWrapMode.Clamp) && importer.mipmapEnabled)continue;
                importer.textureType=TextureImporterType.Default; importer.sRGBTexture=true; importer.mipmapEnabled=true;
                importer.maxTextureSize=2048; importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.filterMode=FilterMode.Trilinear; importer.anisoLevel=4;
                importer.wrapMode=file=="CameraLeather"?TextureWrapMode.Repeat:TextureWrapMode.Clamp; importer.SaveAndReimport();
            }
            string content=folder+"GrandmaLetterContent.asset";
            if(!File.Exists(content))AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<DrawerLetterContent>(),content);
            DrawerLookAssets.EnsureLetterPaper(AssetDatabase.LoadAssetAtPath<DrawerLookSettings>(DrawerLookAssets.Folder+"/DrawerLook.asset"));
        }

        static IEnumerator ReviewRefinedProps(DrawerPrototypeController c)
        {
            const string folder=Review+"/props-20261029";
            yield return new WaitForSeconds(3); c.Automation=true;
            var camera=c.Items[2]; var letter=c.Items[4];
            var sheet=letter.transform.Find("Modeled writing paper");
            var photograph=letter.transform.Find("Clipped companion photograph/Printed companion picture");
            Require(sheet.GetComponent<Renderer>().sharedMaterial==c.RuntimeLook.letterPaper && c.RuntimeLook.letterPaper.shader.name=="Weather House/Oil Paint Surface","Letter paper must use its own live oil material.");
            Require(sheet && photograph,"A real sheet and separately clipped photograph must exist.");
            var mesh=sheet.GetComponent<MeshFilter>().sharedMesh;
            Require(mesh.vertexCount>100 && mesh.bounds.size.y>.02f && mesh.subMeshCount==2,"Letter needs modeled thickness and curved edges, not a single quad.");
            Require(sheet.GetComponent<Renderer>().sharedMaterial.GetTexture("_BaseMap")==Resources.Load<Texture2D>("DrawerPrototype/RefinedProps/GrandmaLetterPaper"),"The paper texture must remain on the oil shader's base-map slot.");
            Require(photograph.GetComponent<Renderer>().sharedMaterial.mainTexture==Resources.Load<Texture2D>("DrawerPrototype/RefinedProps/CompanionPhoto"),"The actual generated companion photograph must be bound.");
            Require(letter.GetComponentsInChildren<TextMesh>().Any(t=>t.text==Resources.Load<DrawerLetterContent>("DrawerPrototype/RefinedProps/GrandmaLetterContent").message),"Editable letter content must reach the model.");
            var writing=letter.GetComponentsInChildren<DrawerLetterWriting>().Single(t=>t.section==1);
            var original=writing.content; var fixture=UnityEngine.Object.Instantiate(original);
            try
            {
                fixture.message="这是一段可修改的信。"; writing.content=fixture; writing.Refresh();
                Require(writing.GetComponent<TextMesh>().text==fixture.message,"Edited letter content must update independently of the paper texture.");
            }
            finally { writing.content=original; writing.Refresh(); UnityEngine.Object.Destroy(fixture); }
            Require(letter.transform.Find("Paper clip · upper wire").GetComponent<MeshFilter>(),"The clip must have real geometry.");
            Require(camera.transform.Find("Convex optical glass") && camera.transform.Find("Knurled focus ring"),"Camera needs modeled glass and a textured focus grip.");
            int triangles=camera.GetComponentsInChildren<MeshFilter>().Sum(m=>m.sharedMesh.triangles.Length/3);
            Require(triangles<16000,"Keep the camera appropriate for this small game prop.");
            foreach(var item in c.Items.Concat(c.Papers))item.body.isKinematic=true;
            yield return Capture(Path.GetFullPath(folder+"/01-in-drawer.png"));
            foreach(var p in c.Papers)p.gameObject.SetActive(false);
            foreach(var item in c.Items)item.gameObject.SetActive(false);
            camera.gameObject.SetActive(true); letter.gameObject.SetActive(true);
            Pose(camera,new Vector3(-2.1f,1,.3f),Quaternion.Euler(-18,-8,0));
            Pose(letter,new Vector3(1.35f,1,.25f),Quaternion.Euler(-10,7,0));
            camera.transform.localScale=letter.transform.localScale=Vector3.one*1.25f;
            yield return new WaitForSeconds(.8f);
            yield return Capture(Path.GetFullPath(folder+"/02-two-modeled-props.png"));
            float originalSize=c.ViewCamera.orthographicSize;
            Vector3 originalPosition=c.ViewCamera.transform.position;
            c.GetComponentInChildren<Canvas>().enabled=false;
            foreach(var item in new[]{camera,letter})
            {
                camera.gameObject.SetActive(item==camera); letter.gameObject.SetActive(item==letter);
                Pose(item,DrawerPrototypeController.DrawerCenter+Vector3.up*.8f,Quaternion.Euler(-12,-7,0));
                item.transform.localScale=Vector3.one;
                Vector3 focus=DrawerPrototypeController.DrawerCenter+Vector3.up*.8f;
                c.ViewCamera.transform.position=focus-c.ViewCamera.transform.forward*20;
                c.ViewCamera.orthographicSize=item==camera?1.55f:1.70f;
                yield return new WaitForSeconds(.3f);
                yield return Capture(Path.GetFullPath(folder+(item==camera?"/03-camera-close.png":"/04-letter-close.png")));
            }
            yield return CheckLetterOcclusion(c,letter,folder);
            c.ViewCamera.orthographicSize=originalSize; c.ViewCamera.transform.position=originalPosition;
            File.WriteAllText(folder+"/model-checks.txt",DateTime.UtcNow.ToString("O")+"\nPASS: separate volumetric sheet and photo; generated textures assigned; editable text; modeled wire clip; focus ring and convex glass.\nCamera triangles="+triangles+"\nCaptured in the actual drawer with the saved user lighting/material settings. Visual acceptance remains with the user.");
        }

        static IEnumerator CheckLetterOcclusion(DrawerPrototypeController c,DrawerToken letter,string folder)
        {
            var writing=letter.GetComponentsInChildren<TextMesh>().Select(t=>t.GetComponent<Renderer>()).ToArray();
            Require(writing.All(r=>r.sharedMaterial.shader.name=="Weather House/Drawer Letter Ink"),"World letter text must use the depth-tested font shader.");
            Require(!ShaderUtil.ShaderHasError(writing[0].sharedMaterial.shader),"Letter ink shader must compile.");
            int visible=InkPixelDifference(c.ViewCamera,writing);
            Require(visible>30,"The exposed letter text must render before checking occlusion.");
            var cover=GameObject.CreatePrimitive(PrimitiveType.Cube); cover.name="Letter occlusion test cover";
            cover.transform.SetParent(letter.transform,false); cover.transform.localPosition=new Vector3(0,.32f,0);
            cover.transform.localScale=new Vector3(2.5f,.12f,2.7f); cover.GetComponent<Renderer>().sharedMaterial=c.RuntimeLook.paper;
            try
            {
                foreach(var r in writing)r.sharedMaterial.SetFloat("_ZTest",(float)UnityEngine.Rendering.CompareFunction.Always);
                int oldFailure=InkPixelDifference(c.ViewCamera,writing);
                Require(oldFailure>30,"The old always-visible text behavior must reproduce under the cover.");
                foreach(var r in writing)r.sharedMaterial.SetFloat("_ZTest",(float)UnityEngine.Rendering.CompareFunction.LessEqual);
                int covered=InkPixelDifference(c.ViewCamera,writing);
                Require(covered==0,"Covered letter text must contribute no visible pixels: "+covered);
                yield return Capture(Path.GetFullPath(folder+"/05-letter-fully-covered.png"));
                File.WriteAllText(folder+"/ink-occlusion.txt",DateTime.UtcNow.ToString("O")+"\nPASS: GPU difference with ink toggled. Exposed="+visible+"; old Always-depth behavior="+oldFailure+"; fixed under opaque cover="+covered);
            }
            finally
            {
                foreach(var r in writing)r.sharedMaterial.SetFloat("_ZTest",(float)UnityEngine.Rendering.CompareFunction.LessEqual);
                UnityEngine.Object.Destroy(cover);
            }
        }

        static int InkPixelDifference(Camera camera,Renderer[] writing)
        {
            var before=ReadCameraPixels(camera);
            foreach(var renderer in writing)renderer.enabled=false;
            Color32[] after;
            try { after=ReadCameraPixels(camera); }
            finally { foreach(var renderer in writing)renderer.enabled=true; }
            int changed=0;
            for(int i=0;i<before.Length;i++)
                if(Mathf.Abs(before[i].r-after[i].r)+Mathf.Abs(before[i].g-after[i].g)+Mathf.Abs(before[i].b-after[i].b)>6)changed++;
            return changed;
        }

        static Color32[] ReadCameraPixels(Camera camera)
        {
            var target=RenderTexture.GetTemporary(800,450,24,RenderTextureFormat.ARGB32);
            var pixels=new Texture2D(800,450,TextureFormat.RGB24,false);
            var prior=RenderTexture.active; var rect=camera.rect;
            try
            {
                camera.rect=new Rect(0,0,1,1);
                var request=new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination=target };
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,request);
                RenderTexture.active=target; pixels.ReadPixels(new Rect(0,0,800,450),0,0); pixels.Apply();
                return pixels.GetPixels32();
            }
            finally { camera.rect=rect; RenderTexture.active=prior; UnityEngine.Object.DestroyImmediate(pixels); RenderTexture.ReleaseTemporary(target); }
        }

        static void SyncLetterModel()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,"Update the standalone letter only outside Play.");
            string path=Root+"/Models/GrandmotherLetter.prefab";
            var settings=AssetDatabase.LoadAssetAtPath<DrawerLookSettings>(DrawerLookAssets.Folder+"/DrawerLook.asset");
            DrawerLookAssets.EnsureLetterPaper(settings);
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var renderer=root.transform.Find("Modeled writing paper").GetComponent<Renderer>();
                var materials=renderer.sharedMaterials; materials[0]=settings.letterPaper; renderer.sharedMaterials=materials;
                foreach(var writing in root.GetComponentsInChildren<DrawerLetterWriting>())
                {
                    if(writing.section==1)writing.maximumSize=new Vector2(1.33f,.86f);
                    writing.Refresh();
                }
                Require(PrefabUtility.SaveAsPrefabAsset(root,path),"Could not save the updated letter prefab.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static void Pose(DrawerToken item,Vector3 position,Quaternion rotation)
        {
            item.body.interpolation=RigidbodyInterpolation.None;
            item.body.position=position; item.body.rotation=rotation;
            item.transform.SetPositionAndRotation(position,rotation); Physics.SyncTransforms();
        }
    }
}
