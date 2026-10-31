using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.EnvironmentLab.Editor
{
    public static class WindStudyChecks
    {
        public static IEnumerator Run()
        {
            EnvironmentLabChecks.Assert(Application.isPlaying && SceneManager.GetActiveScene().path == WindStudyBuilder.ScenePath,
                "Wind GPU checks require actual Play in 03_WindStudy.unity.");
            EnvironmentWindChecks.Run();
            var wind = Object.FindObjectOfType<EnvironmentWind>();
            var view = Object.FindObjectOfType<EnvironmentLabView>();
            EnvironmentLabChecks.Assert(wind && wind.isActiveAndEnabled && view && view.wind == wind, "Wind and preview UI must be bound.");
            EnvironmentLabChecks.Assert(wind.targets.Length == 6 && wind.targets.All(r => r && r.gameObject.scene == wind.gameObject.scene),
                "Exactly six local tree/grass renderers must be bound.");
            var grass = wind.targets.Where(r => IsShader(r, "Weather House/Painterly Grass")).ToArray();
            var trees = wind.targets.Where(r => IsShader(r, "Weather House/Painterly Tree")).ToArray();
            EnvironmentLabChecks.Assert(grass.Length == 3 && trees.Length == 3, "Three grass batches and three trees are required.");
            var camera = view.GetComponent<Camera>();
            var nav = view.navigation;
            float force = wind.force, speed = wind.speed, fov = camera.fieldOfView;
            Vector2 direction = wind.direction;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            bool panel = view.showPanel, viewEnabled = view.enabled, navEnabled = nav.enabled;
            bool[] visible = wind.targets.Select(r => r.enabled).ToArray();
            var textures = new List<Texture2D>();
            var report = new StringBuilder("Actual Play wind study checks\n");
            bool complete = false;
            Directory.CreateDirectory(EnvironmentLabChecks.Output);
            try
            {
                view.showPanel = false; view.enabled = false; nav.enabled = false;
                wind.force = 0; wind.speed = 1.25f; wind.direction = new Vector2(1, .35f); wind.Apply();
                for (int i = 0; i < wind.targets.Length; i++) wind.targets[i].enabled = true;
                camera.fieldOfView = 45;
                Vector3 look = new Vector3(0, 2, 2);
                camera.transform.rotation = Quaternion.Euler(24, 0, 0);
                camera.transform.position = look - camera.transform.forward * 19.5f;
                yield return null;
                yield return null;
                Texture2D stillA = Frame("03_Wind_Still_A", camera, textures);
                double until = Time.realtimeSinceStartupAsDouble + .3;
                while (Time.realtimeSinceStartupAsDouble < until) yield return null;
                Texture2D stillB = Frame("03_Wind_Still_B", camera, textures);
                Difference still = Compare(stillA, stillB);
                report.AppendLine("force=0; 0.3 s apart; " + still);
                EnvironmentLabChecks.Assert(still.fraction < .001 && still.mean < .1,
                    "Zero-wind frames are not stable: " + still);

                wind.force = 2; wind.Apply();
                yield return null;
                Texture2D allA = Frame("03_Wind_On_A", camera, textures);
                until = Time.realtimeSinceStartupAsDouble + .5;
                while (Time.realtimeSinceStartupAsDouble < until) yield return null;
                Texture2D allB = Frame("03_Wind_On_B", camera, textures);
                Difference all = Compare(allA, allB);
                report.AppendLine("force=2; overview 0.5 s apart; " + all);
                EnvironmentLabChecks.Assert(all.fraction > .001, "Wind overview has no measurable motion: " + all);

                // Isolate each group so grass motion cannot pass the tree check, or vice versa.
                foreach (Renderer tree in trees) tree.enabled = false;
                camera.transform.position = new Vector3(-4.4f, 1.15f, -6.5f);
                camera.transform.LookAt(new Vector3(-3, .48f, -2.9f));
                yield return null;
                Texture2D grassA = Frame("03_Wind_Grass_Probe_A", camera, textures);
                until = Time.realtimeSinceStartupAsDouble + .5;
                while (Time.realtimeSinceStartupAsDouble < until) yield return null;
                Texture2D grassB = Frame("03_Wind_Grass_Probe_B", camera, textures);
                Difference grassMotion = Compare(grassA, grassB);
                report.AppendLine("force=2; grass-only probe 0.5 s apart; " + grassMotion);
                EnvironmentLabChecks.Assert(grassMotion.fraction > .001, "Grass has no measurable GPU motion: " + grassMotion);

                foreach (Renderer item in grass) item.enabled = false;
                foreach (Renderer tree in trees) tree.enabled = true;
                Bounds crown = trees[0].bounds;
                Vector3 crownCenter = crown.center + Vector3.up * crown.size.y * .15f;
                camera.transform.position = crownCenter + new Vector3(0, .1f, -8.4f);
                camera.transform.LookAt(crownCenter);
                yield return null;
                Texture2D treeA = Frame("03_Wind_Tree_Probe_A", camera, textures);
                until = Time.realtimeSinceStartupAsDouble + .5;
                while (Time.realtimeSinceStartupAsDouble < until) yield return null;
                Texture2D treeB = Frame("03_Wind_Tree_Probe_B", camera, textures);
                Difference treeMotion = Compare(treeA, treeB);
                report.AppendLine("force=2; tree-only probe 0.5 s apart; " + treeMotion);
                EnvironmentLabChecks.Assert(treeMotion.fraction > .001, "Trees have no measurable GPU motion: " + treeMotion);
                foreach (Renderer item in grass) item.enabled = true;
                camera.transform.rotation = Quaternion.Euler(24, 0, 0);
                camera.transform.position = look - camera.transform.forward * 19.5f;
                string framesPath = EnvironmentLabChecks.Output + "/WindFrames";
                Directory.CreateDirectory(framesPath);
                var captureTimes = new List<string>();
                for (int frame = 0; frame < 30; frame++)
                {
                    until = Time.realtimeSinceStartupAsDouble + .1;
                    while (Time.realtimeSinceStartupAsDouble < until) yield return null;
                    captureTimes.Add(Time.realtimeSinceStartupAsDouble.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    var rendered = EnvironmentLabChecks.Render(camera, 800, 600);
                    File.WriteAllBytes(framesPath + "/" + frame.ToString("D3") + ".png", rendered.EncodeToPNG());
                    Object.DestroyImmediate(rendered);
                }
                File.WriteAllLines(framesPath + "/timing.txt", captureTimes);
                report.AppendLine("Saved 30 actual GPU frames for the wind preview, separated by at least 0.1 seconds.");
                report.AppendLine("PASS runtime material checks and fixed-camera GPU frame differences. Shared parameters; grass/tree waveforms and spatial frequencies remain distinct.");
                report.AppendLine("GPU: " + SystemInfo.graphicsDeviceName);
                complete = true;
            }
            finally
            {
                wind.force = force; wind.speed = speed; wind.direction = direction; wind.Apply();
                for (int i = 0; i < wind.targets.Length; i++) if (wind.targets[i]) wind.targets[i].enabled = visible[i];
                camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = fov;
                view.showPanel = panel; view.enabled = viewEnabled; nav.enabled = navEnabled;
                foreach (Texture2D texture in textures) if (texture) Object.DestroyImmediate(texture);
                if (!complete) report.AppendLine("FAIL or interrupted; see the exception for the stopping check. Parameters and camera restored.");
                File.WriteAllText(EnvironmentLabChecks.Output + "/03_Wind_checks.txt", report.ToString());
            }
        }

        static bool IsShader(Renderer renderer, string shader)
        {
            return renderer.sharedMaterials.Any(m => m && m.shader && m.shader.name == shader);
        }
        static Texture2D Frame(string name, Camera camera, List<Texture2D> owned)
        {
            Texture2D texture = EnvironmentLabChecks.Render(camera, 1200, 800);
            owned.Add(texture);
            File.WriteAllBytes(EnvironmentLabChecks.Output + "/" + name + ".png", texture.EncodeToPNG());
            return texture;
        }
        struct Difference
        {
            public double fraction, mean;
            public override string ToString() { return "changed pixels=" + fraction.ToString("P3") + ", mean RGB byte difference=" + mean.ToString("F4"); }
        }
        static Difference Compare(Texture2D a, Texture2D b)
        {
            Color32[] first = a.GetPixels32(), second = b.GetPixels32();
            EnvironmentLabChecks.Assert(first.Length == second.Length, "Image sizes must agree.");
            long sum = 0; int changed = 0;
            for (int i = 0; i < first.Length; i++)
            {
                int r = Math.Abs(first[i].r - second[i].r), g = Math.Abs(first[i].g - second[i].g), blue = Math.Abs(first[i].b - second[i].b);
                sum += r + g + blue;
                if (Math.Max(r, Math.Max(g, blue)) > 2) changed++;
            }
            return new Difference { fraction = changed / (double)first.Length, mean = sum / (3.0 * first.Length) };
        }
    }
}
