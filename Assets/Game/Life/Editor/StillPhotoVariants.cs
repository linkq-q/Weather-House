using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using WeatherHousePrototype;
using WeatherHouse.StoneEngraving;
using WeatherHouse.WatersideV2;
using Object=UnityEngine.Object;

namespace WeatherHouse.Life.Editor
{
    // Review-only material experiments. Never assigned to a production camera, material or saved photograph.
    public static class StillPhotoVariants
    {
        const string Folder="PhotoStillReview/20261029/Variants";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Set(object o,string field,object value)=>o.GetType().GetField(field,Private).SetValue(o,value);
        static T Get<T>(object o,string field)=>(T)o.GetType().GetField(field,Private).GetValue(o);
        static void Call(object o,string method)=>o.GetType().GetMethod(method,Private).Invoke(o,null);
        public static IEnumerator Run()
        {
            if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")))throw new InvalidOperationException("Use isolated-save review.");
            Directory.CreateDirectory(Folder);var game=Object.FindObjectOfType<GameplaySession>();var hud=game.GetComponent<GameplayHud>();
            var book=game.GetComponent<LifeJournal3D>();var player=game.PlayerTransform;var controller=player.GetComponent<CharacterController>();
            string original=JsonUtility.ToJson(game.Notebook);Vector3 originalPosition=player.position;
            var textures=new List<Texture2D>();Material material=null;
            try
            {
                var stone=Object.FindObjectsOfType<StoneEngravingReadability>().First(s=>s.name.StartsWith("先与后",StringComparison.Ordinal));
                var bounds=stone.GetComponent<Renderer>().bounds;var terrain=Object.FindObjectsOfType<Terrain>().First(t=>t.gameObject.scene==game.gameObject.scene);
                var stand=bounds.center+Vector3.back*2.7f;stand.y=WatersideLayout.GroundHeight(terrain,stand.x,stand.z)+.15f;
                controller.enabled=false;player.position=stand;controller.enabled=true;Physics.SyncTransforms();
                game.forestSprite.SetScriptedInput(Vector2.zero,false,false);yield return null;
                hud.SetCameraMode(true,true);game.PhotoRig.Zoom((54-game.PhotoRig.FocalLength)/5f);
                Vector3 angles=Quaternion.LookRotation(bounds.center-game.PhotoRig.CaptureCamera.transform.position).eulerAngles;
                Set(game.PhotoRig,"photoYaw",angles.y);Set(game.PhotoRig,"photoPitch",Mathf.DeltaAngle(0,angles.x));
                game.PhotoRig.FocusAt(new Vector2(Screen.width*.5f,Screen.height*.5f));yield return null;
                game.TakePhoto();while(game.Recorder.IsRecording)yield return null;
                var record=game.Notebook.observations.Last();
                if(record.frameCount!=1)throw new InvalidOperationException("Expected a static production photograph.");
                string source=Path.Combine(GameplaySession.PersistenceDirectory,record.clipFolder,"frame_000.png");
                File.Copy(source,Folder+"/A-original.png",true);var originalPhoto=MotionPhotoRecorder.LoadFrame(record,0);textures.Add(originalPhoto);
                material=new Material(Shader.Find("Hidden/WeatherHouse/PhotoPostReview"));
                // A original; B light clarity; C engraving readability; D softer paper color.
                var settings=new[]{new[]{0f,1f,1f,0f,0f,0f},new[]{.30f,1.05f,.97f,.08f,.015f,0f},
                    new[]{.50f,1.12f,.76f,.28f,.025f,0f},new[]{.18f,1.02f,.88f,.04f,.035f,.009f}};
                var names=new[]{"A-original","B-natural","C-carving","D-paper"};
                var props=new[]{"_Sharpness","_Contrast","_Saturation","_LocalContrast","_ShadowLift","_Warmth"};
                for(int i=1;i<4;i++)
                {
                    for(int n=0;n<props.Length;n++)material.SetFloat(props[n],settings[i][n]);
                    var image=Process(originalPhoto,material);textures.Add(image);File.WriteAllBytes(Folder+"/"+names[i]+".png",image.EncodeToPNG());
                }
                hud.SetCameraMode(false,true);
                // Eight repeated copies are a temporary page-layout fixture, never persisted to the notebook.
                game.Notebook.observations.Clear();for(int i=0;i<8;i++)game.Notebook.observations.Add(record);
                Set(hud,"albumPhotosSelected",true);Set(hud,"albumTasksSelected",false);Set(hud,"albumGesturesSelected",false);Set(hud,"selectedGlyph",null);Set(hud,"page",0);
                hud.ToggleJournal();book=game.GetComponent<LifeJournal3D>();float deadline=Time.realtimeSinceStartup+15;
                while((hud.IsAlbumOpening||book.IsAnimating)&&Time.realtimeSinceStartup<deadline)yield return null;
                if(book.IsAnimating)throw new InvalidOperationException("Book did not finish opening.");
                for(int i=0;i<4;i++)
                {
                    // Live world events can rebuild the page. Rebind the current slots after each Update.
                    for(int settle=0;settle<4;settle++)
                    {
                        var album=Get<RectTransform>(hud,"albumRoot");
                        var photos=album.GetComponentsInChildren<RawImage>(true).Where(x=>x.name=="Photo "+record.id).ToArray();
                        if(photos.Length!=8)throw new InvalidOperationException("Expected eight real book photo slots.");
                        foreach(var image in photos)image.texture=textures[i];book.RequestContentRefresh();
                        yield return null;
                    }
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder+"/"+names[i]+"-book.png"));
                    yield return new WaitForEndOfFrame();yield return null;
                }
                File.WriteAllText(Folder+"/provenance.txt","Source: actual GameplaySession.TakePhoto, static 2560x1440 PNG, first_then stone, 54mm.\nA source untouched; B/C/D shader-only experiments on copies; same original photo and same real 3D book slots.\nNo experimental filter saved to the production photograph or production rendering.\nRecord="+record.id);
            }
            finally
            {
                hud.SetCameraMode(false,true);if(hud.JournalOpen){if(book)book.Cancel();Call(hud,"CompleteJournalClose");}
                JsonUtility.FromJsonOverwrite(original,game.Notebook);controller.enabled=false;player.position=originalPosition;controller.enabled=true;
                game.forestSprite.ReleaseScriptedInput();foreach(var texture in textures)if(texture)Object.Destroy(texture);if(material)Object.Destroy(material);
            }
        }
        static Texture2D Process(Texture source,Material material)
        {
            var target=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;
            var output=new Texture2D(source.width,source.height,TextureFormat.RGB24,false);
            try{Graphics.Blit(source,target,material);RenderTexture.active=target;output.ReadPixels(new Rect(0,0,source.width,source.height),0,0);output.Apply();return output;}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);}
        }
    }
}
