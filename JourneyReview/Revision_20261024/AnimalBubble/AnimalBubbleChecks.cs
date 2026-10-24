using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.WildlifeJourney;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

// Invoke from Edit mode. All objects live in a disposable preview scene; GameplaySession stays inactive.
public static class AnimalBubbleChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Vector2 NormalCanvas = new Vector2(1600, 1000);
    static readonly GlyphCatalog Catalog = new GlyphCatalog { glyphs = new[] {
        new GlyphDefinition { id = "fixture", body = "<path d=\"M20 20 L80 20 L80 100 L20 100 Z\"/>" }
    } };

    public static string RunBaseline() => Preview(root =>
    {
        Vector2 standard = Hud(root, new Vector2(380, 110), 1, NormalCanvas);
        Vector2 scene = Hud(root, new Vector2(300, 100), 1, NormalCanvas);
        Vector2 social = Social(root);
        Require(standard == new Vector2(380, 110) && scene == new Vector2(300, 100) && social == new Vector2(180, 84),
            "Baseline no longer reproduces the original fixed-width defect: " + standard + ", " + scene + ", " + social);
        return "REPRODUCED old actual RectTransforms: one glyph HUD default=380x110, authored map=300x100, social=180x84.";
    });

    public static string RunChecks() => Preview(root =>
    {
        Vector2 standard = Hud(root, new Vector2(380, 110), 1, NormalCanvas);
        Vector2 one = Hud(root, new Vector2(300, 100), 1, NormalCanvas);
        Require(Mathf.Abs(standard.x / standard.y - 1f) < .05f && Mathf.Abs(one.x / one.y - 1f) < .05f,
            "A single actual HUD glyph is still in an elongated bubble.");
        Require(Mathf.Abs(standard.y - 110) < .1f && Mathf.Abs(one.y - 100) < .1f,
            "Automatic width changed the authored bubble height.");
        Vector2 two = Hud(root, new Vector2(300, 100), 2, NormalCanvas);
        Require(two.x > one.x + 25f && two.x < 200f && two.y == one.y,
            "Two glyphs should visibly widen the bubble without using the whole authored width limit.");
        Vector2 longLine = Hud(root, new Vector2(300, 100), 12, NormalCanvas);
        Require(longLine.x > two.x && longLine.x <= 300.1f && longLine.y == one.y,
            "Long speech did not grow within its authored width cap.");
        Vector2 small = Hud(root, new Vector2(300, 100), 12, new Vector2(120, 100));
        Require(small.x <= 72.1f && small.y <= 65.1f && small.x > 0 && small.y > 0,
            "Speech exceeds the available small canvas after fitting.");
        Vector2 social = Social(root);
        Require(Mathf.Abs(social.x / social.y - 1f) < .05f && Mathf.Abs(social.y - 84f) < .1f,
            "The actual single-word social presentation is not near-square at its authored height.");
        return "PASS actual HUD and social presentation: single glyph near-square, authored heights retained; two glyphs grow; long speech caps; small canvas fits; every glyph stays inside padding; original AnimalBubble style/tail and player bubble sizing retained.";
    });

    static Vector2 Hud(Transform parent, Vector2 configured, int count, Vector2 canvasSize)
    {
        var host = Child(parent, "Inactive synthetic HUD"); host.SetActive(false);
        var session = host.AddComponent<GameplaySession>();
        var hud = host.GetComponent<GameplayHud>();
        var canvas = new GameObject("Synthetic canvas", typeof(RectTransform)); canvas.transform.SetParent(host.transform, false);
        var rect = (RectTransform)canvas.transform; rect.sizeDelta = canvasSize;
        var animalObject = Child(host.transform, "Inactive synthetic animal");
        var animal = animalObject.AddComponent<AnimalActor>();
        animal.animalId = "bubble-fixture"; animal.requestGlyphIds = Enumerable.Repeat("fixture", count).ToArray();
        SetProperty(session, "Observed", animal); SetProperty(session, "Catalog", Catalog);
        Set(hud, "session", session); Set(hud, "canvasRect", rect);
        hud.animalBubbleSize = configured; hud.animalGlyphHeight = 43; hud.animalGlyphStrokeWidth = 9;
        Require(session.Notebook == null && session.PhotoRig == null, "Inactive fixture unexpectedly ran GameplaySession.Awake.");
        Call(hud, "UpdateAnimalBubble");
        var bubble = host.GetComponentsInChildren<ProceduralUiGraphic>(true).Single(g => g.mark == UiMark.AnimalBubble);
        var glyphs = host.GetComponentsInChildren<AnimalGlyphGraphic>(true);
        Require(glyphs.Length == count, "Actual glyph count differs from the utterance.");
        CheckStyleAndPadding(bubble, glyphs);
        if (canvasSize == NormalCanvas)
        {
            Vector2 player = (Vector2)Call(hud, "FitBubbleSize", new Vector2(500, 100), new Vector2(300, 100));
            Require(player == new Vector2(500, 100), "Animal sizing altered the player's authored input bubble size.");
        }
        Require(session.Notebook == null && session.PhotoRig == null, "Fixture initialized live session state.");
        Vector2 measured = bubble.rectTransform.sizeDelta;
        Set(hud, "canvasRect", null); Set(hud, "session", null);
        Object.DestroyImmediate(host);
        return measured;
    }

    static Vector2 Social(Transform parent)
    {
        var host = Child(parent, "Synthetic social"); var social = host.AddComponent<JourneySocial>();
        social.bubbleSize = new Vector2(180, 84); social.glyphHeight = 43;
        var speaker = Child(host.transform, "speaker"); speaker.transform.position = new Vector3(0, 0, 5);
        var listener = Child(host.transform, "listener"); listener.transform.position = new Vector3(5, 0, 5);
        Set(social, "participants", new[] {
            new JourneySocialParticipant { id = 1, root = speaker.transform, radius = .3f },
            new JourneySocialParticipant { id = 2, root = listener.transform, radius = .3f }
        });
        Set(social, "catalog", Catalog);
        SetProperty(social, "ActiveConversation", new JourneyConversation { speakerId = 1, listenerId = 2, glyphIds = new[] { "fixture" } });
        var camera = Child(host.transform, "camera").AddComponent<Camera>(); camera.enabled = false;
        var target = new RenderTexture(1600, 1000, 16);
        try
        {
            camera.targetTexture = target;
            Call(social, "Present", camera); Canvas.ForceUpdateCanvases(); Call(social, "Present", camera);
            var bubble = host.GetComponentsInChildren<ProceduralUiGraphic>(true).Single(g => g.mark == UiMark.AnimalBubble);
            var glyphs = host.GetComponentsInChildren<AnimalGlyphGraphic>(true);
            Require(glyphs.Length == 1 && glyphs[0].rectTransform.sizeDelta.y >= 40f, "Social glyph readability changed.");
            CheckStyleAndPadding(bubble, glyphs);
            return bubble.rectTransform.sizeDelta;
        }
        finally { camera.targetTexture = null; Object.DestroyImmediate(target); Object.DestroyImmediate(host); }
    }

    static void CheckStyleAndPadding(ProceduralUiGraphic bubble, AnimalGlyphGraphic[] glyphs)
    {
        Require(bubble.mark == UiMark.AnimalBubble && bubble.color == ProceduralUiLibrary.Cream &&
            bubble.waviness == ProceduralUiStyle.Current.waviness && bubble.bubbleRadius == ProceduralUiStyle.Current.bubbleRadius &&
            bubble.tailSize == ProceduralUiStyle.Current.tailSize, "Animal bubble lost its original procedural style or tail.");
        Vector2 size = bubble.rectTransform.sizeDelta;
        foreach (var glyph in glyphs)
        {
            Vector2 center = glyph.rectTransform.anchoredPosition, extent = glyph.rectTransform.sizeDelta * .5f;
            Require(Mathf.Abs(center.x) + extent.x <= size.x * .39f + .1f && center.y + extent.y <= size.y * .5f + .1f &&
                center.y - extent.y >= -size.y * .5f + size.y * ProceduralUiStyle.Current.tailSize - .1f,
                "An actual glyph escapes the bubble's padding or overlaps the tail region.");
            Require(glyph.color == ProceduralUiLibrary.Ink && Mathf.Abs(glyph.lineWidth - 9f) < .01f, "Glyph ink or authored stroke width changed.");
        }
    }

    static string Preview(Func<Transform, string> check)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run from stable Edit mode.");
        Scene scene = EditorSceneManager.NewPreviewScene(); var root = new GameObject("Disposable animal bubble checks");
        SceneManager.MoveGameObjectToScene(root, scene);
        try { return check(root.transform); }
        finally { Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
    }
    static GameObject Child(Transform parent, string name) { var go = new GameObject(name); go.transform.SetParent(parent, false); return go; }
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private).SetValue(owner, value);
    static void SetProperty(object owner, string name, object value) => Set(owner, "<" + name + ">k__BackingField", value);
    static object Call(object owner, string name, params object[] args)
    {
        try { return owner.GetType().GetMethod(name, Private).Invoke(owner, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
    }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
