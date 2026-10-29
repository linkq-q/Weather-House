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
        }

        static IEnumerator ReviewRefinedProps(DrawerPrototypeController c)
        {
            const string folder=Review+"/props-20261029";
            yield return new WaitForSeconds(3); c.Automation=true;
            var camera=c.Items[2]; var letter=c.Items[4];
            var sheet=letter.transform.Find("Modeled writing paper");
            var photograph=letter.transform.Find("Clipped companion photograph/Printed companion picture");
            Require(sheet && photograph,"A real sheet and separately clipped photograph must exist.");
            var mesh=sheet.GetComponent<MeshFilter>().sharedMesh;
            Require(mesh.vertexCount>100 && mesh.bounds.size.y>.02f && mesh.subMeshCount==2,"Letter needs modeled thickness and curved edges, not a single quad.");
            Require(sheet.GetComponent<Renderer>().sharedMaterial.mainTexture==Resources.Load<Texture2D>("DrawerPrototype/RefinedProps/GrandmaLetterPaper"),"The ruled paper texture must be on the sheet.");
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
            c.ViewCamera.orthographicSize=originalSize; c.ViewCamera.transform.position=originalPosition;
            File.WriteAllText(folder+"/model-checks.txt",DateTime.UtcNow.ToString("O")+"\nPASS: separate volumetric sheet and photo; generated textures assigned; editable text; modeled wire clip; focus ring and convex glass.\nCamera triangles="+triangles+"\nCaptured in the actual drawer with the saved user lighting/material settings. Visual acceptance remains with the user.");
        }

        static void Pose(DrawerToken item,Vector3 position,Quaternion rotation)
        {
            item.body.interpolation=RigidbodyInterpolation.None;
            item.body.position=position; item.body.rotation=rotation;
            item.transform.SetPositionAndRotation(position,rotation); Physics.SyncTransforms();
        }
    }
}
