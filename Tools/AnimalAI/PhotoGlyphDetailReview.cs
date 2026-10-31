#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

namespace WeatherHouse.Life.Editor
{
    // Copy this and the two checks files into ValidationProject/Assets/.../Editor.
    // Real production UI components, authored grass asset, invented P03 fixture metadata.
    // This is not a Play walk-through or a claim about any photograph in the user's save.
    public static class PhotoGlyphDetailReview
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        const int Width = 1440, Height = 1000, UiLayer = 31, ModelLayer = 30;
        const string GrassPrefab = "Assets/Experiments/DrawerPrototype/Models/GrassSprig.prefab";
        static readonly List<string> summary = new List<string>();

        public static void Batch()
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            if (!Application.isBatchMode || Application.isPlaying || !string.Equals(new DirectoryInfo(project).Name, "ValidationProject", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PhotoGlyphDetailReview runs only in the isolated ValidationProject batch editor, outside Play mode.");
            string report = Environment.GetEnvironmentVariable("WEATHERHOUSE_PHOTO_DETAIL_REPORT");
            if (string.IsNullOrWhiteSpace(report)) report = Path.Combine(project, "PhotoGlyphDetailEvidence", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            report = Path.GetFullPath(report);
            string userSaves = Path.GetFullPath(Application.persistentDataPath);
            Require(!Inside(report, userSaves), "Evidence must not be written inside the player's persistent save directory");
            Directory.CreateDirectory(report);
            Require(!File.Exists(Path.Combine(report, "summary.txt")), "Choose a fresh evidence folder instead of overwriting an earlier result");
            summary.Clear();
            summary.Add("Scope: isolated Edit-mode fixture, real GameplayHud.BuildPhoto / LifePhotoBook.TokenLine / AnimalGlyphGraphic.");
            summary.Add("Photo: actual Unity render of authored GrassSprig prefab; P03 shutter metadata is a labelled fixture, not a user's old photograph.");
            string previousSave = Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE");
            string before = SaveFingerprint(userSaves);
            int failed = 0;
            GameObject host = null;
            try
            {
                string fixtureSave = Path.Combine(report, "fixture-save");
                Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE", fixtureSave);
                Require(Path.GetFullPath(GameplaySession.PersistenceDirectory) == Path.GetFullPath(fixtureSave), "Fixture persistence override is active");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var catalog = JsonUtility.FromJson<GlyphCatalog>(Resources.Load<TextAsset>("WeatherHouseGlyphs").text);
                host = new GameObject("Inactive isolated GameplaySession fixture"); host.SetActive(false);
                var game = host.AddComponent<GameplaySession>();
                var book = new LanguageNotebook(); book.Normalize(catalog);
                Set(game, "<Catalog>k__BackingField", catalog); Set(game, "<Notebook>k__BackingField", book);
                Require(!game.Recorder && !game.PhotoRig, "The gameplay startup and user-save loading path have not run");
                Font font = Resources.Load<Font>("Fonts/ZCOOLXiaoWei-Regular"); Require(font, "The project UI font exists");

                Stage("photo-model", report, () => PhotoGlyphDetailChecks.RunModel(catalog), ref failed);
                Stage("photo-ui", report, () => PhotoGlyphDetailChecks.RunUi(game, font), ref failed);
                Stage("photo-detail-render", report, () => RenderDetails(game, font, report), ref failed);
                Stage("inscription", report, () =>
                {
                    try { InscriptionChecks.Run(); }
                    finally
                    {
                        var log = (List<string>)typeof(InscriptionChecks).GetField("checks", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                        File.WriteAllLines(Path.Combine(report, "inscription-checks.txt"), log);
                    }
                    return File.ReadAllText(Path.Combine(report, "inscription-checks.txt"));
                }, ref failed);
            }
            catch (Exception error) { failed++; summary.Add("FAIL harness setup: " + error); Debug.LogException(error); }
            finally
            {
                if (host) Object.DestroyImmediate(host);
                Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE", previousSave);
                string after = SaveFingerprint(userSaves);
                bool unchanged = before == after;
                File.WriteAllText(Path.Combine(report, "save-protection.txt"), "Checked top-level player-save JSON files; no user photo files were opened or written.\n" +
                    "before=" + before + "\nafter=" + after + "\n" + (unchanged ? "PASS unchanged" : "FAIL user-save JSON changed") +
                    "\nGameplay host remained inactive; only fixture-save was used; no scene was saved.\n");
                if (!unchanged) failed++;
                summary.Add("RESULT failed=" + failed);
                File.WriteAllLines(Path.Combine(report, "summary.txt"), summary);
            }
            Debug.Log("PhotoGlyphDetailReview " + (failed == 0 ? "PASS " : "FAIL ") + report);
            EditorApplication.Exit(failed == 0 ? 0 : 1);
        }

        static string RenderDetails(GameplaySession game, Font font, string report)
        {
            var checks = new List<string>();
            GameObject canvasObject = null, cameraObject = null;
            RenderTexture target = null;
            GameplayHud hud = game.GetComponent<GameplayHud>();
            try
            {
                string photoPath = Path.Combine(report, "fixture-grass-photo.png");
                RenderGrass(photoPath);
                string id = Guid.NewGuid().ToString("N");
                string folder = Path.Combine(GameplaySession.PersistenceDirectory, "MotionPhotos", id);
                Directory.CreateDirectory(folder); File.Copy(photoPath, Path.Combine(folder, "frame_000.png"));
                var photo = new ObservationRecord { id = id, clipFolder = "MotionPhotos/" + id, frameCount = 1,
                    glyphIds = Array.Empty<string>(), matchedPhotoTaskIds = new[] { "P03" } };
                File.WriteAllText(Path.Combine(folder, "metadata.json"), JsonUtility.ToJson(photo, true));
                // Explicitly emulate persisted old task evidence. Do not fake a spoken word into the photo.
                game.Notebook.AddObservation(photo); game.Notebook.GetWord("grass").encountered = true;
                game.Notebook.completedPhotoTasks.Add("P03");
                Set(hud, "session", game); Set(hud, "font", font);
                cameraObject = new GameObject("Photo detail UI evidence camera");
                Camera camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                camera.cullingMask = 1 << UiLayer; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.14f, .18f, .16f); camera.nearClipPlane = .1f; camera.farClipPlane = 10;
                cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                canvasObject = new GameObject("Actual photo detail canvas", typeof(RectTransform), typeof(Canvas));
                Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera; canvas.planeDistance = 1; canvas.pixelPerfect = false;
                var canvasRect = canvasObject.GetComponent<RectTransform>();
                var modal = new GameObject("Photo modal fixture", typeof(RectTransform)).GetComponent<RectTransform>();
                modal.SetParent(canvasRect, false); modal.anchorMin = Vector2.zero; modal.anchorMax = Vector2.one; modal.sizeDelta = Vector2.zero;
                Set(hud, "canvas", canvas); Set(hud, "canvasRect", canvasRect); Set(hud, "modalRoot", modal);
                Set(hud, "expandedPhoto", game.Notebook.observations.Single(o => o.id == id));
                Canvas.ForceUpdateCanvases();
                Require(canvasRect.rect.width > 1000 && canvasRect.rect.height > 800, "The ScreenSpaceCamera canvas uses the evidence render target dimensions");
                var legacyRow = new GameObject("Original raw-glyphIds display path", typeof(RectTransform));
                legacyRow.transform.SetParent(modal, false);
                LifePhotoBook.TokenLine(legacyRow.transform, game, photo.glyphIds, Vector2.zero, 38, 950, font);
                Require(legacyRow.GetComponentsInChildren<AnimalGlyphGraphic>().Length == 0,
                    "The same old P03 fixture reproduces the missing token when passed through the original raw-glyphIds UI path");
                checks.Add("REPRODUCED old display path: this P03 photograph has raw glyphIds=[], so the previous TokenLine input creates zero tokens despite matched P03.");
                Object.DestroyImmediate(legacyRow);
                for (int state = 0; state < 2; state++)
                {
                    game.Notebook.GetWord("grass").confirmed = state == 1;
                    Call(hud, "BuildPhoto");
                    RectTransform detail = Get<RectTransform>(hud, "detailRoot");
                    var image = Get<RawImage>(hud, "expandedImage");
                    Require(image && image.texture && image.texture != Resources.Load<Texture2D>("AlbumBook/PhotoMissing"), "The real photo loader decoded the saved fixture PNG");
                    Require(hud.PhotoDecodeCount == 1, "Reopening a photo reuses the real texture cache");
                    AddFixtureLabel(canvasRect, font, state == 0 ? "组件验收 fixture · 测试照片 · 词义未确认" : "组件验收 fixture · 测试照片 · 词义已确认");
                    SetLayer(canvasObject, UiLayer);
                    Canvas.ForceUpdateCanvases();
                    AnimalGlyphGraphic[] glyphs = detail.GetComponentsInChildren<AnimalGlyphGraphic>();
                    Require(glyphs.Length == 1 && glyphs[0].name == "Token grass", "GameplayHud.BuildPhoto displays the old P03 task's grass glyph with empty original glyphIds");
                    Require(glyphs[0].canvasRenderer.GetMesh() && glyphs[0].canvasRenderer.GetMesh().vertexCount > 0, "The photograph's grass glyph has CanvasRenderer geometry");
                    Text[] meanings = detail.GetComponentsInChildren<Text>().Where(t => t.name.StartsWith("Meaning ", StringComparison.Ordinal)).ToArray();
                    Require(state == 0 ? meanings.Length == 0 : meanings.Length == 1 && meanings[0].name == "Meaning grass" && meanings[0].text == "草",
                        "Only a confirmed grass word is shown with its Chinese meaning");
                    string file = state == 0 ? "photo-p03-unconfirmed.png" : "photo-p03-confirmed.png";
                    Capture(camera, target, Path.Combine(report, file), pixels => AssertDetailPixels(pixels, camera, glyphs[0]));
                    checks.Add("PASS " + file + ": real GameplayHud.BuildPhoto, original glyphIds empty, P03 match retained, grass glyph rendered, Chinese=" + (state == 1));
                    Object.DestroyImmediate(Get<RectTransform>(hud, "detailLayer").gameObject);
                    foreach (Transform label in canvasRect.Cast<Transform>().Where(t => t.name == "Fixture provenance label").ToArray()) Object.DestroyImmediate(label.gameObject);
                }
                Require(photo.glyphIds.Length == 0 && game.Notebook.observations.Single(o => o.id == id).glyphIds.Length == 0 && game.Notebook.encounters.Count == 0,
                    "Rendering never rewrote original photo glyphs or fabricated animal encounters");
                checks.Add("PASS original speech array and evidence sources unchanged; only the fixture toggled grass confirmation between screenshots.");
                checks.Add("Fixture provenance: " + GrassPrefab + "; Unity rendered the saved photograph; P03 metadata is authored test data, not a real gameplay shutter check.");
                return string.Join("\n", checks);
            }
            finally
            {
                Call(hud, "ReleasePhotoTextures");
                Set(hud, "canvas", null); Set(hud, "canvasRect", null); Set(hud, "modalRoot", null); Set(hud, "detailRoot", null); Set(hud, "detailLayer", null);
                if (canvasObject) Object.DestroyImmediate(canvasObject);
                if (cameraObject) Object.DestroyImmediate(cameraObject);
                if (target) { target.Release(); Object.DestroyImmediate(target); }
            }
        }

        static void RenderGrass(string path)
        {
            GameObject root = null, cameraObject = null, lightObject = null;
            RenderTexture target = null;
            try
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GrassPrefab); Require(prefab, "The authored grass fixture prefab exists");
                root = new GameObject("Grass photo fixture"); root.SetActive(false);
                var grass = Object.Instantiate(prefab, root.transform);
                foreach (MonoBehaviour behavior in grass.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behavior);
                root.SetActive(true); SetLayer(root, ModelLayer);
                Renderer[] renderers = grass.GetComponentsInChildren<Renderer>(); Require(renderers.Length > 0, "The grass model has visible mesh renderers");
                Bounds bounds = renderers[0].bounds; foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                cameraObject = new GameObject("Grass photograph fixture camera"); Camera camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                camera.cullingMask = 1 << ModelLayer; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.77f, .82f, .70f); camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(bounds.size.y, bounds.size.x / (16f / 9f)) * .72f;
                camera.nearClipPlane = .01f; camera.farClipPlane = 100;
                camera.transform.position = bounds.center + new Vector3(.25f, .1f, -1).normalized * Mathf.Max(3, bounds.size.magnitude * 3);
                camera.transform.LookAt(bounds.center);
                cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
                lightObject = new GameObject("Grass fixture key light"); Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.2f; light.shadows = LightShadows.None; light.cullingMask = 1 << ModelLayer;
                light.transform.rotation = Quaternion.Euler(35, -30, 0);
                target = new RenderTexture(512, 288, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                Capture(camera, target, path, pixels =>
                {
                    Color32[] colors = pixels.GetPixels32(); Color32 corner = colors[0];
                    int varied = colors.Count(c => Math.Abs(c.r - corner.r) + Math.Abs(c.g - corner.g) + Math.Abs(c.b - corner.b) > 35);
                    Require(varied > colors.Length / 100, "The saved fixture photograph contains a visible grass model, not a blank background");
                    Require(colors.Count(c => c.r > 200 && c.b > 200 && c.g < 80) < colors.Length / 100, "The grass material is not a missing-shader magenta placeholder");
                });
            }
            finally
            {
                if (root) Object.DestroyImmediate(root); if (cameraObject) Object.DestroyImmediate(cameraObject); if (lightObject) Object.DestroyImmediate(lightObject);
                if (target) { target.Release(); Object.DestroyImmediate(target); }
            }
        }

        static void Capture(Camera camera, RenderTexture target, string path, Action<Texture2D> validate)
        {
            RenderTexture prior = RenderTexture.active; Texture2D pixels = null;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                validate(pixels); File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = prior; if (pixels) Object.DestroyImmediate(pixels); }
        }

        static void AssertDetailPixels(Texture2D image, Camera camera, AnimalGlyphGraphic glyph)
        {
            var corners = new Vector3[4]; glyph.rectTransform.GetWorldCorners(corners);
            Vector3[] screen = corners.Select(camera.WorldToScreenPoint).ToArray();
            int x0 = Mathf.Clamp(Mathf.FloorToInt(screen.Min(v => v.x)), 0, image.width - 1), x1 = Mathf.Clamp(Mathf.CeilToInt(screen.Max(v => v.x)), 0, image.width);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(screen.Min(v => v.y)), 0, image.height - 1), y1 = Mathf.Clamp(Mathf.CeilToInt(screen.Max(v => v.y)), 0, image.height);
            int ink = 0, paper = 0;
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
            { Color p = image.GetPixel(x, y); if (Mathf.Max(p.r, p.g, p.b) < .7f) ink++; if (Mathf.Min(p.r, p.g, p.b) > .8f) paper++; }
            Require(ink >= 8 && paper >= 30, "The actual grass-glyph image region contains both ink and paper pixels");
        }

        static void AddFixtureLabel(Transform parent, Font font, string text)
        {
            var go = new GameObject("Fixture provenance label", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2(0, -27); rect.sizeDelta = new Vector2(1300, 44);
            Text label = go.GetComponent<Text>(); label.font = font; label.fontSize = 22; label.text = text;
            label.color = Color.white; label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
        }
        static void Stage(string name, string report, Func<string> execute, ref int failed)
        {
            try { string log = execute(); File.WriteAllText(Path.Combine(report, name + ".txt"), log + "\nRESULT failed=0\n"); summary.Add("PASS " + name); }
            catch (Exception error) { failed++; File.WriteAllText(Path.Combine(report, name + ".txt"), "FAIL " + error); summary.Add("FAIL " + name + ": " + error.Message); Debug.LogException(error); }
        }
        static string SaveFingerprint(string folder)
        {
            if (!Directory.Exists(folder)) return "missing";
            var manifest = new StringBuilder();
            foreach (string file in Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.Ordinal))
            {
                using (var sha = SHA256.Create()) using (var stream = File.OpenRead(file))
                    manifest.Append(Path.GetFileName(file)).Append(':').Append(BitConverter.ToString(sha.ComputeHash(stream))).Append('\n');
            }
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(manifest.ToString()))).Replace("-", "");
        }
        static bool Inside(string path, string parent) => string.Equals(path, parent, StringComparison.OrdinalIgnoreCase) || path.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        static void SetLayer(GameObject root, int layer) { foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer; }
        static void Set(object instance, string field, object value) => instance.GetType().GetField(field, Private).SetValue(instance, value);
        static T Get<T>(object instance, string field) => (T)instance.GetType().GetField(field, Private).GetValue(instance);
        static void Call(object instance, string method) => instance.GetType().GetMethod(method, Private).Invoke(instance, null);
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
#endif
