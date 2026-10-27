using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WeatherHouse.Life.Editor
{
    public static class LifeGestureChecks
    {
        [MenuItem("Weather House/Life/Check gesture recognition")]
        public static void Run()
        {
            string report = RunAlgorithmChecks();
            RequireShader("LifeGesture/GestureInk");
            RequireShader("LifeGesture/LocalRange");
            RequireShader("LifeGesture/JournalSurface");
            report += "\n" + CheckJournalModel();
            using (var mesh = new VertexHelper())
            {
                LifeStrokeGraphic.PopulateStroke(mesh, new[] { new Vector2(0, 60), new Vector2(0, -60), new Vector2(40, -20) }, 4f, Color.white);
                Require(mesh.currentVertCount == 47 && mesh.currentIndexCount == 120, "Two stroke segments and three round brush joins produce drawable geometry.");
                var vertex = new UIVertex(); mesh.PopulateUIVertex(ref vertex, 0);
                Require(vertex.color.a == 255 && vertex.uv0.y == 0, "Stroke edge retains opaque color and lower UV.");
                mesh.PopulateUIVertex(ref vertex, 1);
                Require(vertex.color.a == 255 && vertex.uv0.y == 1, "Stroke edges span the shader opacity profile.");
                mesh.PopulateUIVertex(ref vertex, 8);
                Require((Vector2)vertex.position == new Vector2(0, 60) && Mathf.Approximately(vertex.uv0.y, .5f),
                    "The first round brush end retains an opaque center at the actual stroke start.");
                mesh.PopulateUIVertex(ref vertex, 9);
                Require(Mathf.Abs(Vector2.Distance(vertex.position, new Vector2(0, 60)) - 4f) < .001f,
                    "Round brush ends keep the same radius as the stroke half-width.");
            }
            string path = "Docs/LifeGameplay/UI/gesture-checks.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, report);
            Debug.Log(report);
        }

        private static void RequireShader(string resource)
        {
            Shader shader = Resources.Load<Shader>(resource);
            Require(shader != null, resource + " is included as a Resource.");
            Require(!ShaderUtil.ShaderHasError(shader), resource + " failed shader compilation. Inspect the Unity shader errors before accepting this check.");
            Require(shader.isSupported, resource + " is unsupported on the current graphics device.");
        }

        public static string CheckJournalModel()
        {
            var prefab = Resources.Load<GameObject>("LifeAssets/JournalBook");
            Require(prefab, "The real Blender journal is included as a Resource.");
            AnimationClip[] clips = Resources.LoadAll<AnimationClip>("LifeAssets/JournalBook");
            AnimationClip open = Array.Find(clips, clip => clip.name.EndsWith("BookOpen", StringComparison.Ordinal));
            AnimationClip turn = Array.Find(clips, clip => clip.name.EndsWith("PageTurn", StringComparison.Ordinal));
            Require(open && turn && open.length > .5f && turn.length > .5f, "Physical opening and page-turn clips are imported.");
            var holder = new GameObject("Journal orientation check") { hideFlags = HideFlags.HideAndDontSave };
            holder.transform.rotation = Quaternion.Euler(0, 180, 0);
            GameObject instance = UnityEngine.Object.Instantiate(prefab, holder.transform); instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var animator = instance.GetComponent<Animator>(); if (animator) animator.enabled = false;
                var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>();
                var left = Array.Find(renderers, r => r.name == "PageLeft");
                var right = Array.Find(renderers, r => r.name == "PageRight");
                var turning = Array.Find(renderers, r => r.name == "TurningPage");
                Require(left && right && turning, "Physical paper meshes exist.");
                foreach (var paper in new[] { left, right, turning })
                    Require(paper.sharedMesh.tangents.Length == paper.sharedMesh.vertexCount, paper.name + " imports tangents for the paper normal map.");
                foreach (string surface in new[] { "PaperAged", "CoverAged" })
                {
                    string folder = "Assets/Game/Life/Art/Resources/LifeAssets/JournalTextures/";
                    var normal = AssetImporter.GetAtPath(folder + surface + "_Normal.png") as TextureImporter;
                    var ao = AssetImporter.GetAtPath(folder + surface + "_AO.png") as TextureImporter;
                    Require(normal && normal.textureType == TextureImporterType.NormalMap && !normal.sRGBTexture,
                        surface + " normal is imported as a linear tangent normal map.");
                    Require(ao && ao.textureType == TextureImporterType.Default && !ao.sRGBTexture,
                        surface + " AO is imported as linear grayscale data.");
                }
                foreach (Material material in left.sharedMaterials)
                    Require(material.GetTexture("_BumpMap") && material.GetTexture("_OcclusionMap"), "Imported paper material binds normal and AO textures.");
                Require(turning.sharedMaterials.Length == 2, "Turning page has distinct front and back textures.");
                open.SampleAnimation(instance, open.length);
                Bounds openLeft = BakedBounds(left);
                var gutter=Array.Find(renderers,r=>r.name=="PaperGutter");
                Require(gutter,"The two paper halves share a physical binding gutter.");
                Bounds gutterBounds=BakedBounds(gutter),openRight=BakedBounds(right);
                Require(gutterBounds.size.y>.004f && Mathf.Abs(gutterBounds.min.x-openLeft.max.x)<.0005f &&
                    Mathf.Abs(gutterBounds.max.x-openRight.min.x)<.0005f,
                    "The continuous binding joins both paper edges with a real curved valley.");
                Require(BakedBounds(right).center.x > openLeft.center.x + .05f, "Imported right paper is on the reader's right after presentation rotation.");
                CheckReadablePage(left, 0); CheckReadablePage(right, 0);
                turn.SampleAnimation(instance, 0); Bounds flat = BakedBounds(turning); CheckReadablePage(turning, 0);
                turn.SampleAnimation(instance, turn.length); CheckReadablePage(turning, 1);
                turn.SampleAnimation(instance, turn.length * .5f); Bounds raised = BakedBounds(turning);
                Require(raised.max.y > flat.max.y + .04f, "Authored page actually bends upward in Unity coordinates.");
                open.SampleAnimation(instance, 0); Bounds shutLeft = BakedBounds(left);
                Require(Vector3.Distance(openLeft.center, shutLeft.center) > .05f, "The opening clip actually moves the left paper.");
                return "PASS physical journal assets, two-sided skinned page, opening/bending, paper sides and front/back reading UV axes.";
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
        }

        private static void CheckReadablePage(SkinnedMeshRenderer renderer, int materialSlot)
        {
            var mesh = new Mesh();
            try
            {
                renderer.BakeMesh(mesh);
                Vector3[] positions = mesh.vertices; Vector2[] uv = mesh.uv;
                var vertices = new HashSet<int>(mesh.GetTriangles(materialSlot));
                Vector3 meanPosition = Vector3.zero; Vector2 meanUv = Vector2.zero;
                foreach (int i in vertices) { meanPosition += renderer.transform.TransformPoint(positions[i]); meanUv += uv[i]; }
                meanPosition /= vertices.Count; meanUv /= vertices.Count;
                float horizontal = 0, vertical = 0;
                foreach (int i in vertices)
                {
                    Vector3 point = renderer.transform.TransformPoint(positions[i]) - meanPosition;
                    Vector2 coordinate = uv[i] - meanUv;
                    horizontal += coordinate.x * point.x; vertical += coordinate.y * point.z;
                }
                Require(horizontal > .001f && vertical > .001f, renderer.name + " slot " + materialSlot + " texture reads left-to-right and bottom-to-top.");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        private static Bounds BakedBounds(SkinnedMeshRenderer renderer)
        {
            var mesh = new Mesh();
            try
            {
                renderer.BakeMesh(mesh); Vector3[] vertices = mesh.vertices;
                Require(vertices.Length > 4, "The book contains real paper geometry.");
                var bounds = new Bounds(renderer.transform.TransformPoint(vertices[0]), Vector3.zero);
                foreach (Vector3 vertex in vertices) bounds.Encapsulate(renderer.transform.TransformPoint(vertex));
                return bounds;
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        public static string RunAlgorithmChecks()
        {
            int positive = 0, negative = 0;
            foreach (GestureDefinition gesture in GestureCatalog.All)
            {
                foreach (float scale in new[] { 55f, 140f, 310f })
                {
                    var stroke = HandTrace(gesture.points, scale);
                    Require(GestureCatalog.TryRecognize(stroke, out string id, out float score) && id == gesture.id,
                        "Translated, uneven-speed, noisy " + gesture.id + " at scale " + scale);
                    positive++;
                }
                var reversed = HandTrace(gesture.points, 120f); reversed.Reverse();
                Require(!GestureCatalog.TryRecognize(reversed, out _, out _), "Reverse direction must fail: " + gesture.id); negative++;
            }
            // Independently drawn controls deliberately differ from the canonical arrow/V/Z.
            Expect("rain", new[] { new Vector2(8, 133), new Vector2(3, -100), new Vector2(-63, -25), new Vector2(0, -101), new Vector2(70, -27) }); positive++;
            Expect("weak", new[] { new Vector2(15, 140), new Vector2(112, 5), new Vector2(217, 147) }); positive++;
            Expect("strong", new[] { new Vector2(-131, -98), new Vector2(-9, 74), new Vector2(111, -90) }); positive++;
            Expect("clear", new[] { new Vector2(-111, 73), new Vector2(114, 80), new Vector2(-103, -81), new Vector2(118, -74) }); positive++;
            var bad = new List<Vector2[]>
            {
                Array.Empty<Vector2>(), new[] { Vector2.zero, Vector2.zero, Vector2.zero },
                new[] { new Vector2(0,0), new Vector2(0,100), new Vector2(0,200) },
                new[] { new Vector2(0,0), new Vector2(100,100), new Vector2(-100,100), new Vector2(90,-95), new Vector2(-90,-95), new Vector2(50,150), new Vector2(-90,60) },
                new[] { new Vector2(float.NaN,0), Vector2.zero, Vector2.one },
                new[] { Vector2.zero, new Vector2(float.PositiveInfinity,0), Vector2.one }
            };
            foreach (var stroke in bad) { Require(!GestureCatalog.TryRecognize(stroke, out _, out _), "Invalid, straight, or scribbled input must fail."); negative++; }
            // A semicircle must not cast the full-circle sunlight spell.
            var half = new List<Vector2>();
            for (int i = 0; i < GestureCatalog.Find("sunlight").points.Length / 2; i++) half.Add(GestureCatalog.Find("sunlight").points[i]);
            Require(!GestureCatalog.TryRecognize(half, out string partial, out _) || partial != "sunlight", "Partial circle must not cast sunlight."); negative++;
            Require(!GestureCatalog.IsUsableStroke(new[] { Vector2.zero, Vector2.one, new Vector2(2,2), new Vector2(3,3), new Vector2(4,4) }), "A tiny mouse twitch must not cast."); negative++;
            return "PASS: " + positive + " positive and " + negative + " negative gesture checks.\nDirection and start point fixed; uniform scale/translation and sampling speed independent.\nThis is algorithm evidence, not visual or human usability acceptance.";
        }

        private static void Expect(string expected, Vector2[] controls)
        { Require(GestureCatalog.TryRecognize(HandTrace(controls, 1f, false), out string id, out _) && id == expected, "Independent example " + expected); }
        private static List<Vector2> HandTrace(Vector2[] controls, float scale, bool noise = true)
        {
            var points = new List<Vector2>();
            for (int segment = 1; segment < controls.Length; segment++)
            {
                int samples = 7 + segment % 5;
                for (int i = 0; i < samples; i++)
                {
                    float t = i / (float)samples; t *= t;
                    Vector2 p = Vector2.Lerp(controls[segment - 1], controls[segment], t) * scale + new Vector2(237, -91);
                    if (noise) p += new Vector2(Mathf.Sin(points.Count * .71f), Mathf.Cos(points.Count * .52f)) * scale * .012f;
                    points.Add(p);
                }
            }
            points.Add(controls[controls.Length - 1] * scale + new Vector2(237, -91)); return points;
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
