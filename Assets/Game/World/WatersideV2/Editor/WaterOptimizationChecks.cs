using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ForestSpriteCharacter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using WeatherHouse.Vegetation;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    public sealed class CountingWaterSurface : WorldWaterSurface
    {
        public int containsCalls, heightCalls;
        public override bool Contains(Vector3 position, float margin = 0) { containsCalls++; return true; }
        public override float Height(Vector3 position) { heightCalls++; return 1; }
    }

    public static class WaterOptimizationChecks
    {
        public static void Run()
        {
            string output = "PerformanceAuditReview/20261026/WaterOptimization/checks.txt";
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-waterCheckOutput");
            if (index >= 0 && index + 1 < args.Length) output = args[index + 1];
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = new StringBuilder();
            var engineErrors = new System.Collections.Generic.List<string>();
            Application.LogCallback collectErrors = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) engineErrors.Add(message);
            };
            Application.logMessageReceived += collectErrors;
            try
            {
                CheckDistantWaterQueries();
                report.AppendLine("PASS distant water skips precise queries; nearby contact samples height once.");
                CheckWaterQueryEquivalence(report);
                CheckReflectionSharing(report);
                CheckWaveLifecycle(report);
                int referenceIndex = Array.IndexOf(args, "-waterReferenceShader");
                if (referenceIndex >= 0 && referenceIndex + 1 < args.Length)
                    CheckShaderPixels(args[referenceIndex + 1], Path.GetDirectoryName(output), report);
                foreach (string name in new[] { "Weather House/Painterly Water", "Hidden/WeatherHouse/WaterInteractionNormals" })
                {
                    Shader shader = Shader.Find(name);
                    Require(shader && shader.isSupported, "Unsupported water shader: " + name);
                    var errors = ShaderUtil.GetShaderMessages(shader).Where(m => m.severity.ToString() == "Error").ToArray();
                    Require(errors.Length == 0, "Shader error: " + string.Join("; ", errors.Select(e => e.message)));
                }
                report.AppendLine("PASS water shaders supported; imported/used variants have no compiler errors.");
                Require(engineErrors.Count == 0, "Unity logged errors during the checks: " + string.Join("; ", engineErrors));
                File.WriteAllText(output, report.ToString());
                Debug.Log("Water optimization checks PASS");
            }
            catch (Exception error)
            {
                File.WriteAllText(output, report + "FAIL\n" + error);
                throw;
            }
            finally { Application.logMessageReceived -= collectErrors; }
        }

        static void CheckWaterQueryEquivalence(StringBuilder report)
        {
            int checkedPoints = 0;
            for (int x = -100; x <= 100; x += 4)
                for (int z = -110; z <= 100; z += 4)
                {
                    bool wet = WatersideLayout.IsWater(x, z);
                    float height = WatersideLayout.WaterHeight(x, z);
                    int section = WatersideLayout.WaterSection(x, z);
                    foreach (int candidate in new[] { -2, section, section + 1 })
                    {
                        bool actual = WatersideLayout.TryWaterHeight(x, z, candidate, out float actualHeight);
                        Require(actual == (wet && (candidate == -2 || candidate == section)), "Combined query changed membership.");
                        Require(actualHeight == height, "Combined query changed water height.");
                        checkedPoints++;
                    }
                }
            report.AppendLine("PASS " + checkedPoints + " combined-query membership/height comparisons against existing queries.");
        }

        static void CheckWaveLifecycle(StringBuilder report)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            TerrainData data = null;
            try
            {
                var ground = new GameObject("Wave test terrain"); SceneManager.MoveGameObjectToScene(ground, preview);
                ground.transform.position = new Vector3(-8, 0, -8);
                data = new TerrainData { heightmapResolution = 33, size = new Vector3(16, 2, 16) };
                Terrain terrain = ground.AddComponent<Terrain>(); terrain.terrainData = data;
                var water = GameObject.CreatePrimitive(PrimitiveType.Plane); SceneManager.MoveGameObjectToScene(water, preview);
                water.transform.position = Vector3.up;
                var interaction = water.AddComponent<HillsideWaterInteraction>();
                interaction.waterRenderer = water.GetComponent<Renderer>(); interaction.terrain = terrain;
                var surface = water.AddComponent<CountingWaterSurface>(); interaction.surface = surface;
                interaction.resolution = 64; interaction.decay = 1.25f;
                var actor = new GameObject("Wave test actor"); SceneManager.MoveGameObjectToScene(actor, preview);
                var body = actor.AddComponent<CharacterController>(); body.center = Vector3.up; body.height = 2; body.radius = .25f;
                interaction.player = actor.AddComponent<ForestSpriteDemoController>();
                actor.transform.position = new Vector3(100, 0, 100); Physics.SyncTransforms();
                interaction.Advance(1f / 60);
                Require(!interaction.CurrentTexture && surface.containsCalls == 0 && surface.heightCalls == 0, "Idle distant water allocated or queried.");
                actor.transform.position = Vector3.zero; Physics.SyncTransforms(); interaction.Advance(1f / 60);
                Require(interaction.LastStampCount == 0, "Teleport stamped a streak.");
                actor.transform.position = Vector3.right * .12f; Physics.SyncTransforms();
                surface.containsCalls = surface.heightCalls = 0; interaction.Advance(1f / 60);
                Require(surface.containsCalls == 1 && surface.heightCalls == 1, "Movement queried contact height twice.");
                Require(interaction.CurrentTexture && interaction.LastStampCount > 0, "Walking failed to allocate/stamp water.");
                RenderTexture retained = interaction.CurrentTexture;
                Require(Peak(retained) > 0, "Actual GPU wake remained flat.");
                int stamps = interaction.TotalStampCount;
                for (int i = 0; i < 660; i++) interaction.Advance(1f / 60);
                var block = new MaterialPropertyBlock(); interaction.waterRenderer.GetPropertyBlock(block);
                Require(!Field<bool>(interaction, "hasDisturbance") && block.GetFloat("_InteractionStrength") == 0, "Calm water never slept.");
                Require(interaction.CurrentTexture == retained && retained.IsCreated(), "Sleep reallocated/released the reusable field.");
                Require(Peak(retained) == 0 && interaction.TotalStampCount == stamps, "Sleep retained a non-neutral field or reset statistics.");
                actor.transform.position += Vector3.right * .12f; Physics.SyncTransforms(); interaction.Advance(1f / 60);
                Require(Field<bool>(interaction, "hasDisturbance") && interaction.CurrentTexture == retained && Peak(retained) > 0, "Wake did not resume on the retained texture.");
                interaction.ClearField(); Require(Peak(retained) == 0 && interaction.TotalStampCount == 0, "Explicit reset failed.");
                // Edit-mode fixtures invoke lifecycle explicitly; the gameplay component is not ExecuteAlways.
                typeof(WaterInteraction).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(interaction, null);
                Require(!interaction.CurrentTexture && (!retained || !retained.IsCreated()), "Disable leaked the field.");
                report.AppendLine("PASS actual GPU wake: lazy allocation, single contact query, teleport suppression, decay/sleep, retained-texture wake, clear and release.");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); if (data) Object.DestroyImmediate(data); Blitter.Cleanup(); }
        }

        static float Peak(RenderTexture source)
        {
            RenderTexture old = RenderTexture.active;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = source; copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); copy.Apply();
                float peak = 0;
                foreach (Color color in copy.GetPixels()) peak = Mathf.Max(peak, Mathf.Abs(color.r - .5f), Mathf.Abs(color.g - .5f));
                return peak;
            }
            finally { RenderTexture.active = old; Object.DestroyImmediate(copy); }
        }

        static T Field<T>(object instance, string name) => (T)typeof(WaterInteraction)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);

        static void CheckReflectionSharing(StringBuilder report)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            var priorQuality = QualitySettings.renderPipeline;
            var priorGraphics = GraphicsSettings.defaultRenderPipeline;
            UniversalRendererData rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            UniversalRenderPipelineAsset pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            RenderTexture target = new RenderTexture(128, 128, 24), target2 = new RenderTexture(128, 128, 24);
            Material material = new Material(Shader.Find("Weather House/Painterly Water"));
            try
            {
                QualitySettings.renderPipeline = pipeline; GraphicsSettings.defaultRenderPipeline = pipeline;
                Camera camera = new GameObject("Sharing test camera").AddComponent<Camera>();
                SceneManager.MoveGameObjectToScene(camera.gameObject, preview); camera.scene = preview;
                camera.transform.position = new Vector3(0, 6, -8); camera.transform.LookAt(Vector3.zero);
                camera.aspect = 1; camera.cullingMask = ~0; camera.farClipPlane = 50;
                camera.GetUniversalAdditionalCameraData();
                var first = MakeReflector(preview, material, new Vector3(-2, 0, 0));
                var second = MakeReflector(preview, material, new Vector3(2, 0, 0));
                Render(camera, target);
                RenderTexture shared = ReflectionTexture(first);
                Require(shared && shared.IsCreated() && shared == ReflectionTexture(second), "Exact coplanar surfaces did not share a capture.");
                // Two source renders in one frame must refresh, even with unchanged source pose.
                var groups = (System.Collections.IDictionary)typeof(PainterlyWaterReflection).GetField("captures", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                var entries = (System.Collections.IDictionary)groups[camera];
                object entry = entries.Values.Cast<object>().Single();
                entry.GetType().GetField("viewProjection").SetValue(entry, Matrix4x4.zero);
                Render(camera, target);
                Require(!((Matrix4x4)entry.GetType().GetField("viewProjection").GetValue(entry)).Equals(Matrix4x4.zero), "Same-frame source render reused a stale capture.");
                second.transform.position += Vector3.up * .5f; Render(camera, target);
                Require(ReflectionTexture(first) != ReflectionTexture(second), "Different water heights shared a capture.");
                second.transform.position -= Vector3.up * .5f; second.reflectionLayers = 1; Render(camera, target);
                Require(ReflectionTexture(first) != ReflectionTexture(second), "Different layer masks shared a capture.");
                second.reflectionLayers = first.reflectionLayers; Render(camera, target); shared = ReflectionTexture(second);
                Object.DestroyImmediate(first.gameObject);
                Require(shared && shared.IsCreated(), "Disabling one member released another member's capture.");
                Render(camera, target); Require(ReflectionTexture(second) == shared, "Remaining member lost its shared capture.");
                Camera other = new GameObject("Second source camera").AddComponent<Camera>();
                SceneManager.MoveGameObjectToScene(other.gameObject, preview); other.CopyFrom(camera); other.scene = preview;
                other.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
                Render(other, target2); Require(ReflectionTexture(second) != shared, "Different source cameras shared capture state.");
                Render(camera, target); Require(ReflectionTexture(second) == shared, "Alternating sources lost the original capture.");
                shared.Release(); Render(camera, target);
                Require(ReflectionTexture(second) && ReflectionTexture(second).IsCreated() && ReflectionTexture(second) != shared,
                    "Lost native texture was not recreated.");
                shared = ReflectionTexture(second); second.resolutionScale = .5f; Render(camera, target);
                Require(ReflectionTexture(second).width == 128 && (!shared || !shared.IsCreated()), "Resize retained an obsolete capture.");
                shared = ReflectionTexture(second);
                Object.DestroyImmediate(second.gameObject);
                Require(!shared || !shared.IsCreated(), "Last member leaked its capture.");
                report.AppendLine("PASS actual URP renders: coplanar sharing; height/mask/source isolation; same-frame refresh; member-disable ownership; final release.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
                QualitySettings.renderPipeline = priorQuality; GraphicsSettings.defaultRenderPipeline = priorGraphics;
                Object.DestroyImmediate(material); Object.DestroyImmediate(target); Object.DestroyImmediate(target2);
                Object.DestroyImmediate(pipeline); Object.DestroyImmediate(rendererData);
            }
            // The following GPU field check also needs the blitter without a running game camera.
            Blitter.Cleanup();
            Blitter.Initialize(Shader.Find("Hidden/Universal/CoreBlit"), Shader.Find("Hidden/Universal/CoreBlitColorAndDepth"));
        }

        static PainterlyWaterReflection MakeReflector(Scene scene, Material material, Vector3 position)
        {
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane); SceneManager.MoveGameObjectToScene(water, scene);
            water.transform.position = position; water.transform.localScale = Vector3.one * .35f; water.layer = 4;
            water.GetComponent<Renderer>().sharedMaterial = material;
            var reflection = water.AddComponent<PainterlyWaterReflection>(); reflection.resolution = 256; reflection.resolutionScale = .25f;
            return reflection;
        }

        static void Render(Camera camera, RenderTexture target) => RenderPipeline.SubmitRenderRequest(camera,
            new UniversalRenderPipeline.SingleCameraRequest { destination = target });

        static RenderTexture ReflectionTexture(PainterlyWaterReflection reflection)
        {
            var block = new MaterialPropertyBlock(); reflection.GetComponent<Renderer>().GetPropertyBlock(block);
            return block.GetTexture("_PlanarReflectionTex") as RenderTexture;
        }

        static void CheckShaderPixels(string referencePath, string output, StringBuilder report)
        {
            Shader reference = ShaderUtil.CreateShaderAsset(File.ReadAllText(referencePath)
                .Replace("Shader \"Weather House/Painterly Water\"", "Shader \"Hidden/Water audit reference\"").Replace("_Time.y", "1.25"));
            Require(reference && reference.isSupported, "Reference shader could not compile.");
            // URP uses realtimeSinceStartup in Edit mode; freeze only the clock in both comparison copies.
            Shader candidate = ShaderUtil.CreateShaderAsset(File.ReadAllText(AssetDatabase.GetAssetPath(Shader.Find("Weather House/Painterly Water")))
                .Replace("Shader \"Weather House/Painterly Water\"", "Shader \"Hidden/Water audit candidate\"").Replace("_Time.y", "1.25"));
            Require(candidate && candidate.isSupported, "Candidate comparison shader could not compile.");
            Scene preview = EditorSceneManager.NewPreviewScene();
            var priorQuality = QualitySettings.renderPipeline; var priorGraphics = GraphicsSettings.defaultRenderPipeline;
            var data = ScriptableObject.CreateInstance<UniversalRendererData>(); var pipeline = UniversalRenderPipelineAsset.Create(data);
            var target = new RenderTexture(192, 192, 24);
            var material = new Material(candidate);
            var baseline = new Material(material) { shader = reference };
            var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var atlas = new Texture2D(64, 64, TextureFormat.RGBA32, true, true);
            var reflection = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
            var normals = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
            try
            {
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    float u = (x % 32 + .5f) / 32, v = (y % 32 + .5f) / 32;
                    float ink = Mathf.Clamp01((.25f - (u - .5f) * (u - .5f) - (v - .5f) * (v - .5f)) * 6) * (.6f + .4f * Mathf.Sin(x * 1.7f + y));
                    atlas.SetPixel(x, y, new Color(ink, ink, ink, 1));
                    reflection.SetPixel(x, y, (x / 8 + y / 8) % 2 == 0 ? new Color(.1f, .5f, .2f) : new Color(.7f, .4f, .2f));
                }
                atlas.Apply(); reflection.Apply();
                normals.SetPixels(Enumerable.Repeat(new Color(.55f, .43f, .99f, 1), 4).ToArray()); normals.Apply();
                QualitySettings.renderPipeline = pipeline; GraphicsSettings.defaultRenderPipeline = pipeline;
                Camera camera = new GameObject("Shader comparison camera").AddComponent<Camera>();
                SceneManager.MoveGameObjectToScene(camera.gameObject, preview); camera.scene = preview;
                camera.transform.position = new Vector3(0, 5, -6); camera.transform.LookAt(Vector3.zero);
                camera.aspect = 1; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.gray;
                var cameraData = camera.GetUniversalAdditionalCameraData(); cameraData.requiresColorOption = CameraOverrideOption.On;
                cameraData.requiresDepthOption = CameraOverrideOption.On;
                var bed = GameObject.CreatePrimitive(PrimitiveType.Plane); SceneManager.MoveGameObjectToScene(bed, preview);
                bed.transform.position = Vector3.down * .5f; floorMaterial.SetColor("_BaseColor", new Color(.3f, .4f, .2f));
                floorMaterial.SetTexture("_BaseMap", reflection);
                bed.GetComponent<Renderer>().sharedMaterial = floorMaterial;
                var water = GameObject.CreatePrimitive(PrimitiveType.Plane); SceneManager.MoveGameObjectToScene(water, preview);
                var renderer = water.GetComponent<Renderer>();
                Matrix4x4 projection = Matrix4x4.zero;
                projection.m00 = .1f; projection.m03 = .5f; projection.m12 = .1f; projection.m13 = .5f; projection.m33 = 1;
                for (int scenario = 0; scenario < 4; scenario++)
                {
                    bed.transform.position = Vector3.down * (scenario == 2 ? .01f : .5f);
                    foreach (Material value in new[] { material, baseline })
                    {
                        value.SetFloat("_HasSceneColor", 1); value.SetFloat("_HasPlanarReflection", scenario == 3 ? 0 : 1);
                        value.SetFloat("_UseWaterBrushAtlas", scenario == 2 ? 0 : 1);
                        value.SetTexture("_WaterBrushAtlas", atlas); value.SetMatrix("_PlanarReflectionVP", projection);
                        value.SetTexture("_PlanarReflectionTex", scenario == 0 ? Texture2D.grayTexture : reflection);
                        value.SetTexture("_InteractionNormalTex", normals); value.SetFloat("_InteractionStrength", 1);
                        value.SetVector("_InteractionWorldRect", new Vector4(-2, -2, .25f, .25f));
                    }
                    renderer.sharedMaterial = baseline; Render(camera, target); Render(camera, target); Texture2D before = ReadPixels(target);
                    renderer.sharedMaterial = material; Render(camera, target); Texture2D after = ReadPixels(target);
                    try
                    {
                        Color32[] a = before.GetPixels32(), b = after.GetPixels32(); int max = 0, changed = 0;
                        Require(a.Distinct().Count() > 32 && b.Distinct().Count() > 32,
                            "Shader comparison rendered a blank or uniform image; pixel equality would be meaningless.");
                        for (int i = 0; i < a.Length; i++)
                        {
                            int difference = Math.Max(Math.Abs(a[i].r - b[i].r), Math.Max(Math.Abs(a[i].g - b[i].g), Math.Abs(a[i].b - b[i].b)));
                            max = Math.Max(max, difference); if (difference > 1) changed++;
                        }
                        File.WriteAllBytes(Path.Combine(output, "shader-" + scenario + "-before.png"), before.EncodeToPNG());
                        File.WriteAllBytes(Path.Combine(output, "shader-" + scenario + "-after.png"), after.EncodeToPNG());
                        Require(changed == 0, "Shader changed pixels beyond 1/255: scenario=" + scenario + ", pixels=" + changed + ", max=" + max);
                        report.AppendLine("PASS shader pixel comparison " + scenario + ": max channel difference=" + max + "/255.");
                    }
                    finally { Object.DestroyImmediate(before); Object.DestroyImmediate(after); }
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
                QualitySettings.renderPipeline = priorQuality; GraphicsSettings.defaultRenderPipeline = priorGraphics;
                foreach (Object value in new Object[] { target, material, baseline, floorMaterial, atlas, reflection, normals, reference, candidate, pipeline, data })
                    if (value) Object.DestroyImmediate(value);
            }
        }

        static Texture2D ReadPixels(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try { RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply(); return texture; }
            finally { RenderTexture.active = previous; }
        }

        static void CheckDistantWaterQueries()
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            TerrainData data = null;
            try
            {
                var ground = new GameObject("Query test ground");
                SceneManager.MoveGameObjectToScene(ground, preview);
                ground.transform.position = new Vector3(-8, 0, -8);
                data = new TerrainData { heightmapResolution = 33, size = new Vector3(16, 2, 16) };
                var terrain = ground.AddComponent<Terrain>(); terrain.terrainData = data;
                var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
                SceneManager.MoveGameObjectToScene(water, preview);
                water.transform.position = Vector3.up;
                var surface = water.AddComponent<CountingWaterSurface>();
                var interaction = water.AddComponent<HillsideWaterInteraction>();
                interaction.terrain = terrain; interaction.surface = surface; interaction.waterRenderer = water.GetComponent<Renderer>();
                Require(!interaction.IsWet(new Vector3(100, 0, 100)), "Distant point became wet.");
                Require(surface.containsCalls == 0 && surface.heightCalls == 0,
                    "Distant water still queried the full surface: contains=" + surface.containsCalls + ", height=" + surface.heightCalls);
                Require(interaction.IsWet(Vector3.zero), "Nearby wet ground was rejected.");
                Require(surface.containsCalls == 1 && surface.heightCalls == 1, "Nearby contact should sample each quantity only once.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
                if (data) Object.DestroyImmediate(data);
            }
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
