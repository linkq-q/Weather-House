using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

namespace WeatherHouse.StoneEngraving.Editor
{
    // Exercises the real URP camera callbacks and HUD transitions in an isolated-save Play session.
    public static class StoneEngravingReadabilityChecks
    {
        static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }

        public static void Run(GameObject stone, Camera gameplay, string folder)
        {
            Directory.CreateDirectory(folder);
            var report = new StringBuilder();
            var component = stone.GetComponent<StoneEngravingReadability>();
            var renderer = stone.GetComponent<Renderer>();
            var session = Object.FindObjectOfType<GameplaySession>();
            var hud = session.GetComponent<GameplayHud>();
            var block = new MaterialPropertyBlock();
            Require(component && component.enabled, "Placed prefab must inherit the readability component.");
            var material = renderer.sharedMaterial;
            float depth = material.GetFloat("_GrooveDepth");
            try
            {
                component.enabled = false;
                var before = Capture(gameplay, folder+"/01-gameplay-before.png");
                component.enabled = true;
                var after = Capture(gameplay, folder+"/02-gameplay-after.png");
                renderer.GetPropertyBlock(block);
                Require(block.GetFloat("_GameplayReadability") == 1f, "Gameplay render did not enable readable grooves.");
                int changed = before.Where((p,i) => !p.Equals(after[i])).Count();
                Require(changed > 100, "Gameplay carving enhancement is not visible in the render.");
                report.AppendLine("PASS gameplay render changes " + changed + " pixels at 1075x675.");

                hud.SetCameraMode(true, true);
                Require(session.PhotoRig.Active && hud.CameraMode, "Real photo mode did not open.");
                var photo = session.PhotoRig.CaptureCamera;
                // Aim via the real rig's yaw/pitch, then use its actual focus operation.
                Vector3 direction = renderer.bounds.center + Vector3.up * .35f - photo.transform.position;
                Vector3 angles = Quaternion.LookRotation(direction).eulerAngles;
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                typeof(PhotoCameraRig).GetField("photoYaw",flags).SetValue(session.PhotoRig,angles.y);
                typeof(PhotoCameraRig).GetField("photoPitch",flags).SetValue(session.PhotoRig,Mathf.DeltaAngle(0,angles.x));
                session.PhotoRig.FocusAt(new Vector2(Screen.width*.5f,Screen.height*.5f));
                var photoEnabled = Capture(photo,folder+"/03-photo-mode.png");
                renderer.GetPropertyBlock(block);
                Require(block.GetFloat("_GameplayReadability") == 0f, "Photo camera received gameplay-only contrast.");
                component.enabled = false;
                var photoDisabled = Capture(photo,folder+"/04-photo-baseline.png");
                Require(photoEnabled.SequenceEqual(photoDisabled), "Photo render differs from the unenhanced baseline.");
                component.enabled = true;
                report.AppendLine("PASS real photo camera: enabled component and original appearance are pixel-identical.");

                hud.SetCameraMode(false); // The photo camera remains visible during fade-out.
                Require(hud.CameraMode && session.PhotoRig.Active, "Expected a closing photo transition.");
                Capture(photo,folder+"/05-photo-closing.png");
                renderer.GetPropertyBlock(block);
                Require(block.GetFloat("_GameplayReadability") == 0f, "Enhancement leaked into the closing photo transition.");
                hud.SetCameraMode(false,true);
                Capture(gameplay,folder+"/06-gameplay-return.png");
                renderer.GetPropertyBlock(block);
                Require(block.GetFloat("_GameplayReadability") == 1f, "Gameplay readability did not return after photo mode.");
                report.AppendLine("PASS photo fade-out remains unenhanced; closing returns gameplay readability.");

                component.enabled = false;
                renderer.GetPropertyBlock(block);
                Require(block.GetFloat("_GameplayReadability") == 0f, "Disabling the component left a stale property override.");
                component.enabled = true;
                Require(renderer.sharedMaterial == material && material.GetFloat("_GrooveDepth") == depth,
                    "Camera switching modified the shared carving material.");
                report.AppendLine("PASS disable cleanup; original shared material and groove depth preserved.");
                File.WriteAllText(folder+"/checks.txt",report.ToString());
            }
            catch (Exception e)
            {
                File.WriteAllText(folder+"/checks.txt",report+"\nFAIL\n"+e);
                throw;
            }
            finally { hud.SetCameraMode(false,true); component.enabled = true; }
        }

        static Color32[] Capture(Camera camera, string path) =>
            StoneEngravingPlacementReview.Capture(camera,path,1075,675);
    }
}
