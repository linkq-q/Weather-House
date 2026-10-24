using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Review
{
    public static class PerformanceComparisonChecks
    {
        const string Folder = "PerformanceAuditReview/20261024/Optimization";
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        [Serializable] public sealed class Report
        {
            public string utc, result, error;
            public string evidence = "Isolated Edit preview-scene fixture, actual RenderTexture allocation and actual prefab guide/contact response. Baseline water allocation helper is an exact extracted copy of its current source allocation block. No full scene FPS, native Cloth simulation, actual SRP reflection rendering, or visual acceptance claim.";
            public int baselineTextureCreations, candidateTextureCreations, responseComparisons, particles, edges;
            public bool oldHiddenDiagnosticsRan, newHiddenDiagnosticsSkipped, explicitDiagnosticsRan, markersRan, panelRan, scenePreserved;
            public double baselineResponseMs, candidateHiddenResponseMs, candidateDiagnosticResponseMs, explicitContactSampleMs;
            public int baselineSamplers, candidateSamplers, nativeParticles;
            public float maximumCorrection;
        }
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            var active = SceneManager.GetActiveScene(); bool dirty = active.isDirty;
            var scene = EditorSceneManager.NewPreviewScene();
            var host = new GameObject("Performance isolated checks") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(host, scene); host.SetActive(false);
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), result = "FAIL" };
            string path = Path.GetFullPath(Folder + "/Runs/" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_checks.json");
            try
            {
                Assembly baseline = Assembly.LoadFile(Path.GetFullPath(Folder + "/PerformanceBaseline.dll"));
                Assembly candidate = Assembly.LoadFile(Path.GetFullPath(Folder + "/PerformanceCandidate.dll"));
                CheckTextures(host.transform, baseline, candidate, report);
                CheckCape(host.transform, baseline, candidate, report);
                report.result = "PASS";
            }
            catch (Exception e) { report.error = e.ToString(); }
            finally
            {
                foreach (var contact in host.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c && c.GetType().Name == "ForestSpriteCapeContact"))
                    Call(contact, "OnDisable");
                Object.DestroyImmediate(host); EditorSceneManager.ClosePreviewScene(scene);
                report.scenePreserved = SceneManager.GetActiveScene() == active && active.isDirty == dirty;
                if (!report.scenePreserved) { report.result = "FAIL"; report.error += "\nMain scene changed."; }
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true));
            }
            return path;
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static object Get(object value, string name) => value.GetType().GetField(name, All)?.GetValue(value)
            ?? value.GetType().GetProperty(name, All)?.GetValue(value);
        static void Set(object value, string name, object content)
        {
            var f = value.GetType().GetField(name, All);
            if (f != null) f.SetValue(value, content); else value.GetType().GetProperty(name, All).SetValue(value, content);
        }
        static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, All).Invoke(value, args);
        static GameObject Child(Transform parent, string name)
        { var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave }; go.transform.SetParent(parent, false); return go; }
        static RenderTexture Texture(Component reflection, Camera camera, bool old)
        {
            if (old) return (RenderTexture)Call(reflection, "GetTextureForCheck", camera);
            return (RenderTexture)Get(Call(reflection, "GetCapture", camera), "texture");
        }
        static void CheckTextures(Transform parent, Assembly baseline, Assembly candidate, Report report)
        {
            var oldGo = Child(parent, "Old water"); oldGo.AddComponent<MeshRenderer>();
            var newGo = Child(parent, "New water"); newGo.AddComponent<MeshRenderer>();
            var old = oldGo.AddComponent(baseline.GetType("WeatherHouse.Vegetation.PainterlyWaterReflection"));
            var next = newGo.AddComponent(candidate.GetType("WeatherHouse.Vegetation.PainterlyWaterReflection"));
            var a = Child(parent, "Wide camera").AddComponent<Camera>(); a.aspect = 16f / 9;
            var b = Child(parent, "Square camera").AddComponent<Camera>(); b.aspect = 1;
            var oldIds = new HashSet<int>(); var newIds = new HashSet<int>();
            for (int i = 0; i < 10; i++)
            {
                oldIds.Add(Texture(old, a, true).GetInstanceID()); oldIds.Add(Texture(old, b, true).GetInstanceID());
                newIds.Add(Texture(next, a, false).GetInstanceID()); newIds.Add(Texture(next, b, false).GetInstanceID());
            }
            report.baselineTextureCreations = oldIds.Count; report.candidateTextureCreations = newIds.Count;
            Require(oldIds.Count == 20, "Baseline did not reproduce alternating-aspect texture churn.");
            Require(newIds.Count == 2, "Candidate did not retain two independent texture allocations.");
            var aBefore = Texture(next, a, false); var bBefore = Texture(next, b, false);
            Require(aBefore.width == 512 && aBefore.height == 288 && bBefore.width == 512 && bBefore.height == 512, "Resolution changed.");
            object captureA = Call(next, "GetCapture", a), captureB = Call(next, "GetCapture", b);
            Matrix4x4 ma = Matrix4x4.Translate(Vector3.right), mb = Matrix4x4.Translate(Vector3.up);
            Set(captureA, "viewProjection", ma); Set(captureB, "viewProjection", mb);
            Require(((Matrix4x4)Get(Call(next, "GetCapture", a), "viewProjection")).Equals(ma)
                && ((Matrix4x4)Get(Call(next, "GetCapture", b), "viewProjection")).Equals(mb), "Projection matrices crossed cameras.");
            b.aspect = .5f; var bResized = Texture(next, b, false);
            Require(!bBefore && bResized.width == 256 && bResized.height == 512 && Texture(next, a, false) == aBefore, "Resize invalidated the wrong camera.");
            bResized.Release(); var bRecovered = Texture(next, b, false);
            Require(!bResized && bRecovered.IsCreated() && Texture(next, a, false) == aBefore, "Lost native surface was not independently recovered.");
            Object.DestroyImmediate(b.gameObject); Texture(next, a, false);
            Require(!bRecovered && ((IDictionary)Get(next, "captures")).Count == 1, "Destroyed source camera retained its native texture.");
            Call(next, "OnDisable"); Call(old, "OnDisable");
            Require(!aBefore && ((IDictionary)Get(next, "captures")).Count == 0, "Disable leaked source textures.");
        }

        sealed class CapeFixture
        {
            public GameObject root;
            public Component cape, contact;
            public Transform[] guides;
            public Vector3[] rest;
            public Type contactType;
            public CapeFixture(Transform parent, Assembly assembly, bool diagnostics)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/ForestSprite/Generated/ForestSprite.prefab");
                root = Object.Instantiate(prefab, parent, false); root.SetActive(false);
                var original = root.GetComponentsInChildren<MonoBehaviour>(true).Single(x => x && x.GetType().FullName == "ForestSpriteCharacter.ForestSpriteCapeCloth");
                var values = original.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).ToDictionary(f => f.Name, f => f.GetValue(original));
                var location = original.gameObject;
                foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true)) if (script) script.enabled = false;
                Object.DestroyImmediate(original);
                cape = location.AddComponent(assembly.GetType("ForestSpriteCharacter.ForestSpriteCapeCloth"));
                foreach (var f in cape.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!values.TryGetValue(f.Name, out object value)) continue;
                    f.SetValue(cape, value == null || f.FieldType.IsInstanceOfType(value) ? value : JsonUtility.FromJson(JsonUtility.ToJson(value), f.FieldType));
                }
                contactType = assembly.GetType("ForestSpriteCharacter.ForestSpriteCapeContact");
                contact = location.AddComponent(contactType); Set(contact, "cape", cape);
                Set(cape, "<Contact>k__BackingField", contact);
                cape.GetType().GetProperty("DiagnosticsEnabled")?.SetValue(cape, diagnostics);
                guides = (Transform[])Get(cape, "guideNodes"); rest = (Vector3[])Get(cape, "guideRestPoints");
                // Keep the fixture renderer finite without mutating the source prefab's existing dirty bounds.
                location.GetComponent<SkinnedMeshRenderer>().localBounds = new Bounds(Vector3.zero, Vector3.one * 5);
                root.SetActive(true);
            }
            public void Pose(int sample)
            {
                Call(contact, "ResetResponse");
                for (int i = 0; i < guides.Length; i++)
                {
                    float amount = (i % 7) / 6f;
                    guides[i].localPosition = rest[i] + new Vector3(Mathf.Sin(sample * .4f + i) * .02f, -.01f * amount, .03f * amount);
                    guides[i].localRotation = Quaternion.Euler(sample * .1f * amount, 0, i % 7);
                }
            }
            public void ResetSample() { Set(contact, "<LastSampleFrame>k__BackingField", -1); }
            public void SampleAll() { contactType.GetMethod("SampleAll", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null); }
            public int LastSample => (int)Get(contact, "LastSampleFrame");
        }
        static void CheckCape(Transform parent, Assembly baseline, Assembly candidate, Report report)
        {
            var old = new CapeFixture(parent, baseline, true);
            var next = new CapeFixture(parent, candidate, false);
            var recorded = new CapeFixture(parent, candidate, true);
            parent.gameObject.SetActive(true);
            // Non-ExecuteAlways behaviours do not receive runtime OnEnable in an Edit preview scene.
            // Exercise the exact registration entry point explicitly, and pair it with OnDisable in finally.
            foreach (var fixture in new[] { old, next, recorded }) Call(fixture.contact, "OnEnable");
            report.baselineSamplers = (int)Get(old.contactType.GetField("samplers", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), "Count");
            report.candidateSamplers = (int)Get(next.contactType.GetField("samplers", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), "Count");
            report.nativeParticles = ((Cloth)Get(old.cape, "cloth")).vertices.Length;
            report.particles = ((Vector3[])Get(next.cape, "restParticles")).Length;
            report.edges = ((float[])Get(next.cape, "guideEdgeRestLengths")).Length;
            Require(report.particles > 0 && report.edges > 0, "Real cape data was not loaded.");
            for (int sample = 0; sample < 32; sample++)
            {
                foreach (var f in new[] { old, next, recorded }) { f.Pose(sample); Call(f.contact, "ApplyGuideResponse", 1f / 60); }
                report.maximumCorrection = Mathf.Max(report.maximumCorrection, (float)Get(old.contact, "MaximumCorrection"));
                for (int i = 0; i < old.guides.Length; i++)
                {
                    foreach (var f in new[] { next, recorded })
                    {
                        Require(old.guides[i].localPosition.Equals(f.guides[i].localPosition), "Diagnostics changed guide positions.");
                        Require(old.guides[i].localRotation.Equals(f.guides[i].localRotation), "Diagnostics changed guide rotations.");
                        Require(((Vector3)Call(old.contact, "GetGuideResponseWorld", i)).Equals((Vector3)Call(f.contact, "GetGuideResponseWorld", i)), "Diagnostics changed correction offsets.");
                        Require(((Vector3)Call(old.contact, "GetGuideResponseVelocityWorld", i)).Equals((Vector3)Call(f.contact, "GetGuideResponseVelocityWorld", i)), "Diagnostics changed response velocities.");
                        report.responseComparisons += 4;
                    }
                }
            }
            old.ResetSample(); next.ResetSample(); recorded.ResetSample();
            old.SampleAll(); next.SampleAll();
            report.oldHiddenDiagnosticsRan = old.LastSample == Time.frameCount;
            report.newHiddenDiagnosticsSkipped = next.LastSample == -1;
            report.explicitDiagnosticsRan = recorded.LastSample == Time.frameCount;
            Require(report.oldHiddenDiagnosticsRan && report.newHiddenDiagnosticsSkipped && report.explicitDiagnosticsRan, "Automatic contact diagnostics gate failed.");
            Set(next.contact, "ShowMarkers", true); next.SampleAll(); report.markersRan = next.LastSample == Time.frameCount;
            Require(report.markersRan, "Visible markers did not request diagnostics.");
            Set(next.contact, "ShowMarkers", false); next.ResetSample();
            var panel = next.cape.gameObject.AddComponent(candidate.GetType("ForestSpriteCharacter.ForestSpriteCapePanel"));
            Set(panel, "cape", next.cape); Set(panel, "expanded", true); Call(panel, "OnEnable"); Set(panel, "expanded", true);
            next.SampleAll(); report.panelRan = next.LastSample == Time.frameCount;
            Require(report.panelRan, "Expanded tuning panel did not request diagnostics.");
            ((Behaviour)panel).enabled = false; Call(panel, "OnDisable"); next.ResetSample(); next.SampleAll();
            Require(next.LastSample == -1, "Disabled panel kept diagnostic scans active.");
            report.baselineResponseMs = TimeResponse(old);
            report.candidateHiddenResponseMs = TimeResponse(next);
            report.candidateDiagnosticResponseMs = TimeResponse(recorded);
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) { old.ResetSample(); Call(old.contact, "SampleActualContacts"); }
            timer.Stop(); report.explicitContactSampleMs = timer.Elapsed.TotalMilliseconds / 20;
        }
        static double TimeResponse(CapeFixture f)
        {
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < 40; i++) { f.Pose(i); Call(f.contact, "ApplyGuideResponse", 1f / 60); }
            timer.Stop(); return timer.Elapsed.TotalMilliseconds / 40;
        }
    }
}
