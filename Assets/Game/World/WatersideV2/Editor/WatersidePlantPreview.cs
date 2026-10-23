using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WatersidePlantPreview
    {
        const string Plants = "Assets/Game/World/WatersideV2/Generated/Nature/Plants";
        const string Output = "WatersideV2Review";
        const int Columns = 4, Rows = 6, TileWidth = 520, TileHeight = 440, ImageHeight = 380;

        [MenuItem("Weather House/Checks/Capture Waterside Plant Volumes")]
        public static void CaptureMenu() { Debug.Log(Capture()); }

        public static string Capture()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Capture plants in Edit mode.");
            Scene original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
            if (dirty) throw new InvalidOperationException("Save the current scene before the plant preview.");
            string volumeReport = VerifyVolumes();
            var preview = new PreviewRenderUtility();
            Texture2D sheet = null;
            Mesh floorMesh = null;
            Material floorMaterial = null;
            try
            {
                preview.camera.cameraType = CameraType.Preview;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(.84f, .86f, .80f, 1);
                preview.camera.orthographic = true; preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 30;
                preview.camera.allowHDR = false; preview.camera.allowMSAA = true;
                var cameraData = preview.camera.GetUniversalAdditionalCameraData();
                cameraData.renderPostProcessing = false; cameraData.renderShadows = true;
                cameraData.requiresColorOption = CameraOverrideOption.Off; cameraData.requiresDepthOption = CameraOverrideOption.Off;
                preview.ambientColor = new Color(.48f, .52f, .42f);
                preview.lights[0].type = LightType.Directional;
                preview.lights[0].intensity = 1.15f; preview.lights[0].color = new Color(1, .95f, .83f);
                preview.lights[0].transform.rotation = Quaternion.Euler(42, -35, 0);
                preview.lights[0].shadows = LightShadows.Soft;
                preview.lights[1].type = LightType.Directional;
                preview.lights[1].intensity = .45f; preview.lights[1].color = new Color(.76f, .86f, 1);
                preview.lights[1].transform.rotation = Quaternion.Euler(25, 145, 0);

                var specimen = new GameObject("Plant volume specimen") { hideFlags = HideFlags.HideAndDontSave };
                preview.AddSingleGO(specimen);
                var filter = specimen.AddComponent<MeshFilter>(); var renderer = specimen.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
                var floor = new GameObject("Plant preview ground") { hideFlags = HideFlags.HideAndDontSave };
                preview.AddSingleGO(floor);
                floorMesh = new Mesh { vertices = new[] { new Vector3(-1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0, -1) },
                    triangles = new[] { 0, 1, 2, 0, 2, 3 } };
                floorMesh.RecalculateNormals(); floorMesh.RecalculateBounds();
                floor.AddComponent<MeshFilter>().sharedMesh = floorMesh;
                floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave };
                floorMaterial.SetColor("_BaseColor", new Color(.57f, .62f, .48f)); floorMaterial.SetFloat("_Smoothness", .05f);
                floor.AddComponent<MeshRenderer>().sharedMaterial = floorMaterial;

                int width = Columns * TileWidth, height = Rows * TileHeight;
                var pixels = Enumerable.Repeat(new Color32(230, 233, 220, 255), width * height).ToArray();
                Quaternion angle = Quaternion.Euler(24, 38, 0);
                var manifest = new StringBuilder(volumeReport);
                for (int i = 0; i < WatersidePlantBuilder.Ids.Length; i++)
                {
                    string id = WatersidePlantBuilder.Ids[i];
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Plants + "/Prefabs/" + id + "_v2.prefab");
                    Require(prefab, "Missing saved plant " + id);
                    filter.sharedMesh = prefab.GetComponent<MeshFilter>().sharedMesh;
                    renderer.sharedMaterials = prefab.GetComponent<MeshRenderer>().sharedMaterials;
                    // Use actual vertices, not the wind-expanded mesh bounds, to fill each close-up consistently.
                    Vector3[] vertices = filter.sharedMesh.vertices;
                    Bounds bounds = new Bounds(vertices[0], Vector3.zero);
                    foreach (Vector3 p in vertices) bounds.Encapsulate(p);
                    specimen.transform.position = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                    Vector3 center = new Vector3(0, bounds.size.y * .5f, 0);
                    float halfHeight = .01f, halfWidth = .01f;
                    Quaternion inverse = Quaternion.Inverse(angle);
                    foreach (Vector3 p in vertices)
                    {
                        Vector3 view = inverse * (p + specimen.transform.position - center);
                        halfHeight = Mathf.Max(halfHeight, Mathf.Abs(view.y)); halfWidth = Mathf.Max(halfWidth, Mathf.Abs(view.x));
                    }
                    float aspect = TileWidth / (float)ImageHeight;
                    preview.camera.aspect = aspect;
                    preview.camera.orthographicSize = Mathf.Max(halfHeight, halfWidth / aspect) * 1.20f + .045f;
                    preview.camera.transform.SetPositionAndRotation(center - angle * Vector3.forward * 8, angle);
                    float floorSize = Mathf.Max(bounds.size.x, bounds.size.z, .3f) * 1.6f;
                    floor.transform.localScale = new Vector3(floorSize, 1, floorSize);
                    floor.transform.position = new Vector3(0, -.004f, 0);

                    renderer.enabled = false;
                    Texture2D background = Render(preview);
                    Color32[] empty;
                    try { empty = background.GetPixels32(); }
                    finally { Object.DestroyImmediate(background); renderer.enabled = true; }
                    Texture2D image = Render(preview);
                    try
                    {
                        Color32[] tile = image.GetPixels32();
                        Require(tile.Length == TileWidth * ImageHeight, "Plant capture resolution changed.");
                        int changed = 0, magenta = 0;
                        for (int pixel = 0; pixel < tile.Length; pixel++)
                        {
                            Color32 c = tile[pixel], b = empty[pixel];
                            if (Math.Abs(c.r - b.r) + Math.Abs(c.g - b.g) + Math.Abs(c.b - b.b) > 24) changed++;
                            if (c.r > 220 && c.b > 220 && c.g < 60) magenta++;
                        }
                        Require(changed > 100 && magenta < 20, "Plant capture is empty or uses an error shader: " + id);
                        int left = i % Columns * TileWidth, bottom = height - (i / Columns + 1) * TileHeight;
                        for (int row = 0; row < ImageHeight; row++) Array.Copy(tile, row * TileWidth, pixels, (bottom + 60 + row) * width + left, TileWidth);
                        Text(pixels, width, left + 18, bottom + 34, id.Replace('_', ' ').ToUpperInvariant(), 3);
                        Text(pixels, width, left + 18, bottom + 10, "H " + bounds.size.y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " M / " + filter.sharedMesh.triangles.Length / 3 + " TRI / V2", 2);
                        manifest.AppendLine("Preview " + id + ": variant 2, actual height " + bounds.size.y.ToString("F3") + " m; frame normalized independently.");
                    }
                    finally { Object.DestroyImmediate(image); }
                }
                sheet = new Texture2D(width, height, TextureFormat.RGBA32, false);
                sheet.SetPixels32(pixels); sheet.Apply();
                Directory.CreateDirectory(Output);
                File.WriteAllBytes(Output + "/plants-volume.png", sheet.EncodeToPNG());
                File.WriteAllText(Output + "/plants-volume.txt", "Actual Unity URP preview-camera render; existing saved materials; Edit mode only. Each tile is independently framed, not a common scale.\n" + manifest);
            }
            finally
            {
                preview.Cleanup();
                if (floorMesh) Object.DestroyImmediate(floorMesh);
                if (floorMaterial) Object.DestroyImmediate(floorMaterial);
                if (sheet) Object.DestroyImmediate(sheet);
            }
            Require(SceneManager.GetActiveScene() == original && original.isDirty == dirty, "Plant preview changed the active scene or dirty state.");
            return "PASS 22 three-quarter plant previews: " + Path.GetFullPath(Output + "/plants-volume.png") + "; saved 66-variant volume audit; active scene preserved.";
        }

        public static string VerifyVolumes()
        {
            var report = new StringBuilder("Saved plant mesh volume audit\n");
            foreach (string id in WatersidePlantBuilder.Ids)
                for (int variant = 1; variant <= 3; variant++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Plants + "/Prefabs/" + id + "_v" + variant + ".prefab");
                    Require(prefab, "Missing saved plant variant " + id + "/" + variant);
                    Mesh mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
                    WatersidePlantMesh.Validate(mesh);
                    Vector3[] vertices = mesh.vertices; int[] triangles = mesh.triangles;
                    int[] parent = Enumerable.Range(0, vertices.Length).ToArray();
                    int Find(int at) { while (parent[at] != at) { parent[at] = parent[parent[at]]; at = parent[at]; } return at; }
                    for (int i = 0; i < triangles.Length; i += 3)
                    { parent[Find(triangles[i + 1])] = Find(triangles[i]); parent[Find(triangles[i + 2])] = Find(triangles[i]); }
                    var volume6 = new Dictionary<int, double>(); var area = new Dictionary<int, double>();
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        int root = Find(triangles[i]); Vector3 origin = vertices[root];
                        Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                        if (!volume6.ContainsKey(root)) { volume6[root] = 0; area[root] = 0; }
                        volume6[root] += Vector3.Dot(a - origin, Vector3.Cross(b - origin, c - origin));
                        area[root] += Vector3.Cross(b - a, c - a).magnitude * .5;
                    }
                    double minimum = double.MaxValue;
                    foreach (int root in volume6.Keys)
                    {
                        double thicknessProxy = volume6[root] / (3 * area[root]); // 2V/A: exact thin slab thickness in the broad-face limit.
                        Require(volume6[root] > 1e-14 && thicknessProxy > .00001,
                            id + "/" + variant + " contains inward/zero-volume or collapsed component " + root);
                        minimum = Math.Min(minimum, thicknessProxy);
                    }
                    report.AppendLine("PASS " + id + "/" + variant + ": " + volume6.Count + " closed positive-volume components; minimum 2V/A " + (minimum * 1000).ToString("F3") + " mm.");
                }
            return report.ToString();
        }

        static Texture2D Render(PreviewRenderUtility preview)
        {
            preview.BeginStaticPreview(new Rect(0, 0, TileWidth, ImageHeight));
            preview.Render(true);
            return preview.EndStaticPreview();
        }

        // Small CPU font keeps the saved contact sheet labeled without changing GUI state or importing font assets.
        static readonly string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-/ ";
        static readonly string[] Glyphs = ("7E1111117E 7F49494936 3E41414122 7F4141221C 7F49494941 7F09090901 3E41495172 7F0808087F 00417F4100 204040403F 7F08142241 7F40404040 7F020C027F 7F0408107F 3E4141413E 7F09090906 3E4151215E 7F09192946 4649494931 01017F0101 3F4040403F 1F2040201F 3F4038403F 6314081463 0708700807 6151494543 " +
            "3E5149453E 00427F4000 4261514946 2141454B31 1814127F10 2745454539 3C4A494930 0171090503 3649494936 064949291E 0060600000 0808080808 2010080402 0000000000").Split(' ');
        static void Text(Color32[] pixels, int width, int x, int y, string text, int scale)
        {
            var ink = new Color32(48, 62, 42, 255);
            foreach (char letter in text)
            {
                int glyph = Alphabet.IndexOf(letter);
                if (glyph >= 0)
                    for (int col = 0; col < 5; col++)
                    {
                        int bits = Convert.ToInt32(Glyphs[glyph].Substring(col * 2, 2), 16);
                        for (int row = 0; row < 7; row++)
                            if ((bits & (1 << row)) != 0)
                                for (int dy = 0; dy < scale; dy++)
                                    for (int dx = 0; dx < scale; dx++)
                                        pixels[(y + (6 - row) * scale + dy) * width + x + col * scale + dx] = ink;
                    }
                x += scale * 6;
            }
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
