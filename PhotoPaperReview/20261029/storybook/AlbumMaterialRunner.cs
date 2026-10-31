using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using WeatherHouse.Life;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class AlbumMaterialReview
{
    public static readonly string Output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../storybook"));
    public static readonly string Source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../.."));
    static bool started;
    static AlbumMaterialReview() { EditorApplication.update += Tick; }
    public static void Run()
    {
        if (!Application.dataPath.Replace('\\','/').Contains("PhotoPaperReview/20261029/UnityProject/"))
            throw new InvalidOperationException("This test only runs in its isolated project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(renderer, "Assets/ReviewRenderer.asset");
        var pipeline = UniversalRenderPipelineAsset.Create(renderer);
        pipeline.msaaSampleCount = 1;
        AssetDatabase.CreateAsset(pipeline, "Assets/ReviewPipeline.asset");
        GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
        RenderSettings.fog = false;
        SessionState.SetBool("AlbumMaterialReview", true);
        EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        if (!SessionState.GetBool("AlbumMaterialReview", false) || !EditorApplication.isPlaying || started) return;
        started = true;
        try { new GameObject("Album material review").AddComponent<AlbumMaterialRunner>().Begin(); }
        catch (Exception error) { Finish("FAIL " + error, 1); }
    }
    public static void Finish(string result, int code)
    {
        File.WriteAllText(Path.Combine(Output, "checks.txt"), result);
        Debug.Log(result);
        SessionState.SetBool("AlbumMaterialReview", false);
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }
}

public class AlbumMaterialRunner : MonoBehaviour
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly List<string> checks = new List<string>();
    LifeJournal3D journal;
    RectTransform album;
    Transform canvas;
    Texture2D[] photos;
    static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Hidden).GetValue(value);
    static void Call(object value, string name) => value.GetType().GetMethod(name, Hidden).Invoke(value, null);
    void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks.Add("PASS " + message); }
    public void Begin() => StartCoroutine(Guard(Run()));
    IEnumerator Guard(IEnumerator routine)
    {
        while (true)
        {
            bool more; object value = null;
            try { more = routine.MoveNext(); if (more) value = routine.Current; }
            catch (Exception e) { AlbumMaterialReview.Finish(string.Join("\n", checks) + "\nFAIL " + e, 1); yield break; }
            if (!more) break;
            yield return value;
        }
        AlbumMaterialReview.Finish(string.Join("\n", checks) + "\nPlay=True during capture; isolated project; main scene/save untouched.\n", 0);
    }
    void Build(int count)
    {
        if (album) { album.gameObject.SetActive(false); Destroy(album.gameObject); }
        album = AlbumBookView.Create(canvas, true, null, null, null, null);
        for (int i = 0; i < 8; i++) AlbumBookView.Photo(album, i, i < count ? photos[i % photos.Length] : null, "Photo " + i);
        Check(journal.Bind(album), "Real JournalBook rig binds for " + count + " photos");
    }
    void Shot(string name)
    {
        Call(journal, "RenderBook");
        var previous = RenderTexture.active; RenderTexture.active = journal.ViewTexture;
        var t = new Texture2D(journal.ViewTexture.width, journal.ViewTexture.height, TextureFormat.RGBA32, false);
        t.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0); t.Apply();
        Check(t.GetPixels32().Count(p => p.a > 200 && p.r > 30 && p.g > 30) > t.width * t.height / 10, name + " contains a visible GPU-rendered book");
        File.WriteAllBytes(Path.Combine(AlbumMaterialReview.Output, name + ".png"), t.EncodeToPNG());
        Destroy(t); RenderTexture.active = previous;
    }
    static Color32[] Pixels(string name)
    {
        var image = new Texture2D(2, 2);
        image.LoadImage(File.ReadAllBytes(Path.Combine(AlbumMaterialReview.Output, name + ".png")));
        var pixels = image.GetPixels32(); Destroy(image); return pixels;
    }
    void CheckDelicateCoating()
    {
        var oldPhoto = Pixels("before-open"); var oldMatte = Pixels("before-matte");
        var newPhoto = Pixels("01-after-open"); var newMatte = Pixels("photo-matte-control");
        long oldLift = 0, newLift = 0; int oldBright = 0, newBright = 0;
        for (int i = 0; i < oldPhoto.Length; i++)
        {
            if (oldPhoto[i].a < 200 || newPhoto[i].a < 200) continue;
            int oldValue = Math.Max(0, oldPhoto[i].r - oldMatte[i].r) + Math.Max(0, oldPhoto[i].g - oldMatte[i].g) + Math.Max(0, oldPhoto[i].b - oldMatte[i].b);
            int newValue = Math.Max(0, newPhoto[i].r - newMatte[i].r) + Math.Max(0, newPhoto[i].g - newMatte[i].g) + Math.Max(0, newPhoto[i].b - newMatte[i].b);
            oldLift += oldValue; newLift += newValue;
            if (oldValue > 30) oldBright++; if (newValue > 30) newBright++;
        }
        File.WriteAllText(Path.Combine(AlbumMaterialReview.Output, "coating-metrics.txt"), "Brightness lift: " + oldLift + " -> " + newLift + "\nBright pixels: " + oldBright + " -> " + newBright);
        Check(newLift > 0 && newLift < oldLift * .65, "Coating adds less than 65% of the previous whitening");
        Check(newBright < oldBright * .65, "Broad bright area shrinks below 65% of the previous coating");
    }
    IEnumerator Run()
    {
        yield return null;
        var backdrop = new GameObject("Review camera", typeof(Camera)).GetComponent<Camera>();
        backdrop.cullingMask = 0;
        yield return null;
        var canvasObject = new GameObject("Review canvas", typeof(RectTransform), typeof(Canvas));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        canvas = canvasObject.transform;
        journal = gameObject.AddComponent<LifeJournal3D>();
        string[] paths = { "PhotoStillReview/20261029/coast-original.png", "PhotoStillReview/20261029/Animals/fox-original.png", "PhotoStillReview/20261029/Animals/sheep-original.png", "PhotoStillReview/20261029/Animals/squirrel-original.png" };
        photos = paths.Select(path => { var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(Path.Combine(AlbumMaterialReview.Source, path))); return t; }).ToArray();
        var shader = Resources.Load<Shader>("LifeGesture/JournalSurface");
        Check(shader && !ShaderUtil.ShaderHasError(shader), "Journal shader imports without errors");
        Check(new Material(shader).HasProperty("_PhotoSheen"), "Photo sheen property exists (absent in baseline shader)");
        Build(8); yield return null; yield return null;
        var before = Field<Vector4[]>(journal, "beforePhotos");
        Check(before.All(r => r.z > r.x && r.w > r.y), "Eight real photo masks cover both pages");
        Check(before.Count(r => r.x > .5f) == 4, "Right page keeps four independent photo bounds");
        var runtime = Field<Dictionary<Renderer, Material[]>>(journal, "runtime");
        var surfaces = Field<Dictionary<Material, Material>>(journal, "surfaces");
        var covers = surfaces.Where(p => p.Key.name == "JournalCloth" || p.Key.name == "JournalCover").ToArray();
        Check(covers.Length == 2 && covers.All(p => p.Value.shader.name == "Weather House/Oil Paint Surface"), "Cover and spine both use the existing oil-paint shader");
        var preset = Resources.Load<Material>("AlbumBook/CoverPaint");
        Check(preset.GetFloat("_Impasto") == 2 && Mathf.Approximately(preset.GetFloat("_BrushScale"), 2.118644f) && Mathf.Approximately(preset.GetFloat("_Smoothness"), .272f), "MagicBook impasto/brush scale/sheen preserved");
        Check(preset.GetFloat("_TextureInfluence") == 0 && preset.GetFloat("_StudioLighting") == 1, "Solid olive base and fixed studio lighting enabled");
        Shot("01-after-open");
        foreach (var pair in runtime) foreach (var material in pair.Value) AlbumPhotoPaper.Apply(material, new Vector4[8]);
        Shot("photo-matte-control");
        CheckDelicateCoating();
        Call(journal, "RefreshPaper");
        Shot("photo-static-repeat");
        Check(File.ReadAllBytes(Path.Combine(AlbumMaterialReview.Output, "01-after-open.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(AlbumMaterialReview.Output, "photo-static-repeat.png"))), "Stationary sheen is stable without time-driven sweeping");
        Check(!File.ReadAllBytes(Path.Combine(AlbumMaterialReview.Output, "01-after-open.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(AlbumMaterialReview.Output, "photo-matte-control.png"))), "Photo treatment produces a real GPU image difference");
        var legacy = new List<Material>();
        foreach (var pair in covers)
        {
            var material = new Material(shader); legacy.Add(material);
            foreach (string name in new[] { "_BaseMap", "_BumpMap", "_OcclusionMap" })
            { material.SetTexture(name, pair.Key.GetTexture(name)); material.SetTextureScale(name, pair.Key.GetTextureScale(name)); material.SetTextureOffset(name, pair.Key.GetTextureOffset(name)); }
            material.SetColor("_BaseColor", pair.Key.GetColor("_BaseColor"));
            material.SetFloat("_BumpScale", pair.Key.GetFloat("_BumpScale")); material.SetFloat("_OcclusionStrength", pair.Key.GetFloat("_OcclusionStrength"));
            material.SetFloat("_ShadeStrength", .42f); material.SetFloat("_MinimumShade", .72f);
            foreach (var renderer in journal.Model.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m == pair.Value ? material : m).ToArray();
        }
        foreach (var pair in runtime) foreach (var material in pair.Value) AlbumPhotoPaper.Apply(material, new Vector4[8]);
        Shot("00-before-open");
        for (int i = 0; i < covers.Length; i++)
            foreach (var renderer in journal.Model.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m == legacy[i] ? covers[i].Value : m).ToArray();
        Call(journal, "RefreshPaper");
        journal.BeginPageDrag(() => Build(1), () => Build(8), 1);
        foreach (float progress in new[] { .15f, .32f, .55f, .75f })
        { journal.DragPage(progress); yield return null; yield return null; Shot("turn-" + Mathf.RoundToInt(progress * 100)); }
        var leaf = Field<Renderer>(journal, "turningPage");
        Check(leaf.sharedMaterials[0].GetVectorArray("_PhotoRects").Count(r => r.z > r.x) == 8 && leaf.sharedMaterials[1].GetVectorArray("_PhotoRects").Count(r => r.z > r.x) == 1, "Turning front/back retain their own 8-photo and 1-photo masks");
        journal.ReleasePage(true);
        float deadline = Time.realtimeSinceStartup + 20;
        while (journal.IsAnimating && Time.realtimeSinceStartup < deadline) yield return null;
        Check(!journal.IsAnimating, "Drag completes naturally");
        Check(Field<Vector4[]>(journal, "beforePhotos").Count(r => r.z > r.x) == 1, "Partial last page clears seven old masks");
        Shot("02-last-page");
        Build(0); yield return null; Shot("03-empty-page");
        Check(Field<Vector4[]>(journal, "beforePhotos").All(r => r == Vector4.zero), "Empty placeholders remain matte");
        Build(8); journal.Open(intro => AlbumBookView.Photo(intro, 0, null, "Empty intro"));
        Check(journal.OpeningFan.Pages.All(p => p.sharedMaterials[0].GetVectorArray("_PhotoRects").All(r => r == Vector4.zero)), "Opening intro fan remains matte");
        Check(journal.OpeningFan.Pages.Last().sharedMaterials[1].GetVectorArray("_PhotoRects").Count(r => r.z > r.x) == 8, "Last opening leaf reveals the target photo material");
        deadline = Time.realtimeSinceStartup + 20;
        while (journal.IsAnimating && Time.realtimeSinceStartup < deadline) yield return null;
        Check(!journal.IsAnimating, "Opening sequence completes");
        Shader[] shaders = { shader, preset.shader };
        Check(shaders.All(s => !ShaderUtil.ShaderHasError(s)), "Both shaders compiled successfully after real GPU draws");
        Shot("04-final-open");
    }
}
