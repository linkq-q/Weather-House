using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using WeatherHouse.Vegetation;
using Object = UnityEngine.Object;

namespace WeatherHouse.EnvironmentLab.Editor
{
    public static class FogStudyChecks
    {
        public static IEnumerator Run()
        {
            EnvironmentLabChecks.Assert(Application.isPlaying, "Fog GPU checks require actual Play mode.");
            yield return null;
            var view = Object.FindObjectOfType<EnvironmentLabView>();
            EnvironmentLabChecks.Assert(view && view.fogCards.Length == 3 && view.fogCards.All(c => c && c.targetRenderer), "Three saved fog cards are required.");
            var camera = view.GetComponent<Camera>();
            EnvironmentLabChecks.Assert(camera.GetUniversalAdditionalCameraData().requiresDepthTexture, "Fog intersections need this camera's depth texture.");
            var nav = view.navigation;
            var savedPosition = camera.transform.position;
            var savedRotation = camera.transform.rotation;
            var savedTarget = nav.target;
            var savedMode = nav.mode;
            float savedYaw = nav.yaw, savedPitch = nav.pitch, savedDistance = nav.distance, savedFov = camera.fieldOfView;
            bool savedNavEnabled = nav.enabled, savedViewEnabled = view.enabled, savedPanel = view.showPanel;
            bool savedFog = RenderSettings.fog, savedFogControl = view.distanceFog;
            bool[] savedRenderers = view.fogCards.Select(c => c.targetRenderer.enabled).ToArray();
            var report = new StringBuilder("Fog study / actual Play / " + SystemInfo.graphicsDeviceName + "\n");
            Directory.CreateDirectory(EnvironmentLabChecks.Output);
            try
            {
                report.AppendLine(FogCardChecks.Run());
                report.AppendLine(FogCardChecks.RunRuntime());
                float originalOpacity = view.fogCards[0].opacity;
                try
                {
                    foreach (float opacity in new[] { 0f, .18f })
                    {
                        view.SetFogOpacity(opacity);
                        EnvironmentLabChecks.Assert(view.fogCards.All(c => Mathf.Abs(c.targetRenderer.sharedMaterial.GetColor("_BaseColor").a - opacity) < .0001f),
                            "Fog preset buttons must apply opacity to all rendered cards immediately.");
                    }
                }
                finally { view.SetFogOpacity(originalOpacity); }
                report.AppendLine("PASS fog preset actions apply immediately to all three actual materials.");
                view.Overview();
                nav.enabled = false;
                view.showPanel = false;
                view.enabled = false;
                // All comparisons run in this frame, keeping the existing tree wind at one shader time.
                SetCards(view.fogCards, false);
                RenderSettings.fog = false;
                var clear = Capture("02_Fog_A_AllOff", camera);
                RenderSettings.fog = true;
                var distance = Capture("02_Fog_B_DistanceOnly", camera);
                SetCards(view.fogCards, true);
                var layered = Capture("02_Fog_C_DistanceAndCards", camera);
                Metric depth = Difference(clear, distance), cards = Difference(distance, layered);
                report.AppendLine("Distance fog: " + depth);
                report.AppendLine("Additional cards at overview: " + cards);
                EnvironmentLabChecks.Assert(depth.changed > 5000 && depth.mean > .002f, "Distance fog must change the rendered forest.");
                EnvironmentLabChecks.Assert(cards.changed > 300 && cards.mean > .0002f, "Saved distant cards must visibly contribute to the overview.");

                camera.transform.position = new Vector3(13, 10, 52);
                camera.transform.LookAt(new Vector3(-2, 8, 81));
                Capture("02_Fog_D_SideView", camera);
                ProbeNearFade(view.fogCards, camera, report);
                report.AppendLine("PASS GPU comparisons and camera-near fade. Texture soft edges use the existing Smoke alpha.");
                report.AppendLine("Limits: flat cards thin out edge-on; transparent sorting remains view-dependent; camera depth excludes transparent water. Walking uses the lab's flat-floor browsing camera, without rock/tree collision.");
            }
            catch (Exception error)
            {
                report.AppendLine("FAIL: " + error.Message);
                throw;
            }
            finally
            {
                for (int i = 0; i < view.fogCards.Length; i++) view.fogCards[i].targetRenderer.enabled = savedRenderers[i];
                RenderSettings.fog = savedFog;
                view.distanceFog = savedFogControl;
                nav.SetMode(savedMode);
                nav.target = savedTarget;
                nav.yaw = savedYaw; nav.pitch = savedPitch; nav.distance = savedDistance;
                camera.fieldOfView = savedFov;
                camera.transform.SetPositionAndRotation(savedPosition, savedRotation);
                nav.enabled = savedNavEnabled;
                view.showPanel = savedPanel;
                view.enabled = savedViewEnabled;
                File.WriteAllText(EnvironmentLabChecks.Output + "/02_Fog_checks.txt", report.ToString());
            }
        }

        static void ProbeNearFade(FogCard[] cards, Camera camera, StringBuilder report)
        {
            SetCards(cards, false);
            RenderSettings.fog = false;
            var card = cards[0];
            float savedNear = card.nearFadeStart, savedFar = card.nearFadeEnd;
            try
            {
                camera.fieldOfView = 55;
                camera.transform.position = card.transform.position - card.transform.forward * 2;
                camera.transform.LookAt(card.transform.position);
                var absent = Capture("02_Fog_E_CloseCardOff", camera, 900, 600);
                card.targetRenderer.enabled = true;
                card.nearFadeStart = 0;
                card.nearFadeEnd = .05f;
                card.ApplySettings();
                var unfaded = Capture("02_Fog_F_CloseFadeBypassed", camera, 900, 600);
                card.nearFadeStart = savedNear;
                card.nearFadeEnd = savedFar;
                card.ApplySettings();
                var faded = Capture("02_Fog_G_CloseNormalFade", camera, 900, 600);
                Metric full = Difference(absent, unfaded), near = Difference(absent, faded);
                report.AppendLine("Near probe, camera 2m from actual saved card; fade bypassed: " + full + "; normal fade: " + near);
                EnvironmentLabChecks.Assert(full.changed > 1000 && full.mean > .001f, "The actual card must render when near fade is bypassed.");
                EnvironmentLabChecks.Assert(near.mean < full.mean * .20f + .0001f, "Near fade must remove at least 80 percent of the actual rendered contribution.");
            }
            finally
            {
                card.nearFadeStart = savedNear;
                card.nearFadeEnd = savedFar;
                card.ApplySettings();
            }
        }

        static void SetCards(FogCard[] cards, bool enabled)
        {
            foreach (var card in cards) card.targetRenderer.enabled = enabled;
        }

        static Color32[] Capture(string name, Camera camera, int width = 1600, int height = 1000)
        {
            var image = EnvironmentLabChecks.Render(camera, width, height);
            try
            {
                File.WriteAllBytes(EnvironmentLabChecks.Output + "/" + name + ".png", image.EncodeToPNG());
                return image.GetPixels32();
            }
            finally { Object.DestroyImmediate(image); }
        }

        struct Metric
        {
            public int changed;
            public float mean;
            public override string ToString() { return changed + " changed pixels, mean RGB difference=" + mean.ToString("F6"); }
        }

        static Metric Difference(Color32[] a, Color32[] b)
        {
            EnvironmentLabChecks.Assert(a.Length == b.Length, "Compared renders must have equal resolution.");
            var result = new Metric();
            long sum = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int delta = Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
                if (delta >= 6) result.changed++;
                sum += delta;
            }
            result.mean = (float)sum / (a.Length * 3f * 255);
            return result;
        }
    }
}
