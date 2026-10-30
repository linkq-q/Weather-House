using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using WeatherHouse.Life;
using WeatherHousePrototype;
using WeatherHouse.DrawerPrototype;

public static class InteractionRevisionChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var log = new System.Collections.Generic.List<string>();
        void Check(bool ok, string message) { log.Add((ok ? "PASS " : "FAIL ") + message); File.WriteAllLines("../interaction-revision-checks.txt", log); if (!ok) throw new Exception(message); }
        var go = new GameObject("Inactive fixture"); go.SetActive(false);
        var game = go.AddComponent<GameplaySession>();
        var catalogPath = Directory.GetFiles("../../../Assets", "WeatherHouseGlyphs.json", SearchOption.AllDirectories).First();
        var catalog = JsonUtility.FromJson<GlyphCatalog>(File.ReadAllText(catalogPath));
        typeof(GameplaySession).GetProperty("Catalog").SetValue(game, catalog);
        var notebook = new LanguageNotebook(); notebook.Normalize(catalog);
        typeof(GameplaySession).GetProperty("Notebook").SetValue(game, notebook);
        var root = new GameObject("Task fixture", typeof(RectTransform)).GetComponent<RectTransform>();
        LifePhotoBook.Build(root, game, 0, Resources.Load<Font>("Fonts/ZCOOLXiaoWei-Regular"), _ => {}, _ => {});
        var glyphs = root.GetComponentsInChildren<AnimalGlyphGraphic>();
        Check(glyphs.Length > 0 && glyphs.All(g => Mathf.Abs(g.rectTransform.rect.height - 61.2f) < .01f), "Task glyph descriptions are 1.2x");
        Check(!root.Find("Previous photography page") && !root.Find("Next photography page"), "No task arrow buttons");
        var canvas = new GameObject("Book canvas", typeof(RectTransform), typeof(Canvas));
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var album = AlbumBookView.Create(canvas.transform, true, null, null, null, null);
        var journal = go.AddComponent<LifeJournal3D>();
        Check(journal.Bind(album), "Real rig binds");
        var surfaces = (System.Collections.Generic.Dictionary<Material,Material>)typeof(LifeJournal3D).GetField("surfaces", Flags).GetValue(journal);
        Check(surfaces.Single(p => p.Key.name == "JournalCover").Value.shader.name == "Weather House/Life/Journal Surface", "Outer front cover restored to original textured material");
        Check(surfaces.Single(p => p.Key.name == "JournalCloth").Value.name == "Journal olive painted cover", "Inner cloth retains current painted material");
        journal.BeginPageDrag(() => {}, () => {}, 1); journal.DragPage(.1f);
        Check(journal.AnimationProgress > .2f, "A short drag immediately raises the authored page");
        journal.ReleasePage(false); journal.Cancel();
        var drawer = go.AddComponent<DrawerPrototypeController>();
        var paperObject = new GameObject("Paper fixture");
        var paper = paperObject.AddComponent<DrawerToken>(); paper.dimensions = new Vector3(1, .02f, .6f);
        paper.hitBox = paperObject.AddComponent<BoxCollider>(); paper.hitBox.size = paper.dimensions;
        paper.body = paperObject.AddComponent<Rigidbody>(); paper.body.useGravity = false;
        paper.body.position = Vector3.zero; paper.body.velocity = new Vector3(10, -.2f, 10);
        drawer.Papers.Add(paper);
        typeof(DrawerPrototypeController).GetMethod("FixedUpdate", Flags).Invoke(drawer, null);
        var v = paper.body.velocity;
        Check(new Vector2(v.x, v.z).magnitude < .65f && Mathf.Abs(v.y + .2f) < .001f, "Horizontal knock speed capped while vertical motion remains");
        Check(Mathf.Abs(paper.body.maxDepenetrationVelocity - .5f) < .001f, "Stack collision ejection speed limited");
        UnityEngine.Object.DestroyImmediate(paperObject); UnityEngine.Object.DestroyImmediate(root.gameObject);
        UnityEngine.Object.DestroyImmediate(canvas); UnityEngine.Object.DestroyImmediate(go);
    }
}
