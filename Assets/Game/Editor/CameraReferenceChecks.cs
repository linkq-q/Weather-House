using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace WeatherHousePrototype.Editor
{
    // Read the real Play-mode hierarchy after its layout pass; never enter Play or change the scene.
    public static class CameraReferenceChecks
    {
        public static string InspectCurrent()
        {
            Require(Application.isPlaying, "Run this check in Play mode.");
            var hud = Object.FindObjectOfType<GameplayHud>();
            Require(hud && hud.CameraMode, "Photo mode must already be active.");
            Canvas.ForceUpdateCanvases();
            var canvas = hud.GetComponentsInChildren<Canvas>().Single(c => c.name == "Weather House HUD");
            var root = canvas.transform.Cast<Transform>().Single(t => t.name == "Camera mode" && t.gameObject.activeInHierarchy);
            Require(root && root.gameObject.activeInHierarchy, "The actual photo UI exists.");
            var group = root.GetComponent<CanvasGroup>();
            Require(group && group.alpha >= .999f && group.interactable, "Wait until the photo fade has settled before taking the final screenshot.");
            Require(!canvas.transform.Cast<Transform>().Any(t => t.name == "Controls" && t.gameObject.activeInHierarchy), "Every bottom shortcut group is hidden in photo mode, including after a live rebuild.");
            Require(!canvas.transform.Cast<Transform>().Any(t => t.name == "Bottom status" && t.gameObject.activeInHierarchy), "The second status label is hidden.");
            Require(root.GetComponentsInChildren<Button>().Single().name == "Shutter", "Only the shutter is a visible photo button.");
            Require(root.GetComponentsInChildren<RawImage>().Length == 0, "Photo chrome does not substitute an image for the live camera.");
            var session = hud.GetComponent<GameplaySession>();
            Require(session.PhotoRig.Active && session.CurrentView == session.PhotoRig.CaptureCamera && session.CurrentView.enabled,
                "The active image comes from the existing PhotoRig.");

            var frame = root.Find("Viewfinder").GetComponent<ProceduralUiGraphic>();
            var shutter = (RectTransform)root.Find("Shutter");
            var dot = (RectTransform)root.Find("Camera dot");
            var hint = root.Find("Capture hint").GetComponent<Text>();
            Rect screen = ScreenRect((RectTransform)canvas.transform);
            Rect frameRect = ScreenRect(frame.rectTransform), shutterRect = ScreenRect(shutter), dotRect = ScreenRect(dot), hintRect = ScreenRect(hint.rectTransform);
            foreach (Rect rect in new[] { frameRect, shutterRect, dotRect, hintRect })
                Require(Contains(screen, rect), "All photo controls stay inside the actual canvas.");
            Require(Mathf.Abs(shutterRect.center.x - screen.center.x) < 1f && Mathf.Abs(dotRect.center.x - screen.center.x) < 1f && Mathf.Abs(hintRect.center.x - screen.center.x) < 1f,
                "The hint, dot and shutter share the horizontal center.");
            Require(shutterRect.yMax < dotRect.yMin && dotRect.yMax < hintRect.yMin, "Shutter, dot and hint have separate layout space.");
            Require(!string.IsNullOrWhiteSpace(hint.text) && !hint.raycastTarget && hint.alignment == TextAnchor.MiddleCenter,
                "A centered nonblocking hint is visible.");
            int fontSize = hint.cachedTextGenerator.fontSizeUsedForBestFit;
            var settings = hint.GetGenerationSettings(hint.rectTransform.rect.size);
            settings.resizeTextForBestFit = false;
            settings.fontSize = fontSize > 0 ? fontSize : hint.fontSize;
            float textHeight = hint.cachedTextGeneratorForLayout.GetPreferredHeight(hint.text, settings) / hint.pixelsPerUnit;
            Require(textHeight <= hint.rectTransform.rect.height + 1f, "The full hint fits vertically at its actual fitted font size.");

            // Exercise the production mesh, including its rounded corners and end caps.
            using (var vertices = new VertexHelper())
            {
                typeof(ProceduralUiGraphic).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(VertexHelper) }, null).Invoke(frame, new object[] { vertices });
                Require(vertices.currentVertCount > 0, "The real viewfinder mesh is populated.");
                Rect rect = frame.rectTransform.rect;
                Rect middle = new Rect(rect.center - rect.size * .25f, rect.size * .5f);
                var quadrants = new bool[4];
                for (int i = 0; i < vertices.currentVertCount; i++)
                {
                    UIVertex vertex = default; vertices.PopulateUIVertex(ref vertex, i);
                    Vector2 point = vertex.position;
                    Require(rect.Contains(point), "The entire corner mesh stays inside its rect.");
                    Require(!middle.Contains(point), "The center of the live image remains clear.");
                    quadrants[(point.x >= rect.center.x ? 1 : 0) + (point.y >= rect.center.y ? 2 : 0)] = true;
                }
                Require(quadrants.All(v => v), "All four corners are present.");
            }
            return $"PASS {Screen.width}x{Screen.height}; hintFont={settings.fontSize}; shutter={shutterRect}; dot={dotRect}; hint={hintRect}";
        }

        public static string InspectAfterExit()
        {
            Require(Application.isPlaying, "Run this check in Play mode.");
            var hud = Object.FindObjectOfType<GameplayHud>();
            Require(hud && !hud.CameraMode && !hud.JournalOpen, "Exit photo mode and close the album before this check.");
            var canvas = hud.GetComponentsInChildren<Canvas>().Single(c => c.name == "Weather House HUD");
            Require(canvas.transform.Cast<Transform>().Count(t => t.name == "Controls" && t.gameObject.activeInHierarchy) == 1, "Exactly one normal movement/photo/journal group returns after exit.");
            var controls = canvas.transform.Cast<Transform>().Single(t => t.name == "Controls" && t.gameObject.activeInHierarchy);
            Require(!controls.Find("Movement keyboard") && !controls.Find("Movement label") && controls.Find("Photo shortcut") && controls.Find("Journal shortcut"),
                "Exploration keeps C/J and removes only the WASD icon and movement label.");
            var session = hud.GetComponent<GameplaySession>();
            Require(!session.PhotoRig.Active && session.PlayerViewCamera.enabled, "The gameplay camera is restored.");
            return "PASS photo exit restores controls and gameplay camera";
        }

        public static string InspectLiveRebuild()
        {
            Require(Application.isPlaying, "Run this check in Play mode.");
            var hud = Object.FindObjectOfType<GameplayHud>();
            Require(hud && hud.CameraMode && !hud.JournalOpen && !hud.IsDialogueOpen, "Start this regression in photo mode with no modal open.");
            // Uses the real Inspector rebuild with unchanged settings. Only transient HUD objects are replaced.
            typeof(GameplayHud).GetMethod("RebuildInspectorPreview", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, null);
            return "live rebuild: " + InspectCurrent();
        }

        public static string InspectEffectsAndTransitions()
        {
            float hitchAlpha = ProceduralUiLibrary.FadeAlpha(0f, true, ProceduralUiLibrary.FadeFrameDelta(1f));
            Require(hitchAlpha > 0f && hitchAlpha < 1f && ProceduralUiLibrary.FadeFrameDelta(1f / 60f) == 1f / 60f,
                "A one-second hitch preserves an intermediate fade frame; ordinary frame timing is unchanged.");
            InspectCurrent();
            var hud = Object.FindObjectOfType<GameplayHud>();
            var session = hud.GetComponent<GameplaySession>();
            Require(!session.Recorder.IsRecording, "Run transition checks with no recording in progress.");
            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            var advance = typeof(GameplayHud).GetMethod("UpdateCameraTransition", hidden);
            var blend = typeof(GameplayHud).GetField("cameraBlend", hidden);
            var target = typeof(GameplayHud).GetField("cameraRequested", hidden);
            var groupField = typeof(GameplayHud).GetField("cameraGroup", hidden);
            var profile = (VolumeProfile)typeof(PhotoCameraRig).GetField("focusProfile", hidden).GetValue(session.PhotoRig);
            Require(profile.TryGet<Vignette>(out var vignette) && Mathf.Abs(vignette.intensity.value - .084f) < .0001f &&
                Mathf.Abs(vignette.smoothness.value - .8f) < .0001f && !vignette.rounded.value,
                "The real photo Volume has the soft lens vignette, also used by the recording camera.");
            var glow = (BreathingTextGlow)typeof(GameplayHud).GetField("statusGlow", hidden).GetValue(hud);
            var animateGlow = typeof(GameplayHud).GetMethod("AnimateStatusGlow", hidden);
            animateGlow.Invoke(hud, new object[] { .625f });
            Require(Mathf.Abs(glow.Intensity - .375f) < .0001f, "The bottom hint's original .50 peak is reduced by exactly 25 percent.");
            animateGlow.Invoke(hud, new object[] { 1.875f });
            Require(Mathf.Abs(glow.Intensity - .18f) < .0001f, "The ordinary minimum glow intensity is unchanged.");
            animateGlow.Invoke(hud, new object[] { Time.unscaledTime });

            hud.SetCameraMode(false);
            Require(hud.CameraMode && session.PhotoRig.Active, "A normal close keeps the real photo camera active while fading.");
            advance.Invoke(hud, new object[] { .08f });
            float partial = ((CanvasGroup)groupField.GetValue(hud)).alpha;
            Require(partial > 0f && partial < 1f, "The closing UI has an intermediate alpha.");
            hud.SetCameraMode(true);
            Require(Mathf.Abs(((CanvasGroup)groupField.GetValue(hud)).alpha - partial) < .0001f, "Quick reversal continues from the current alpha without flashing.");
            advance.Invoke(hud, new object[] { .04f });
            Require(((CanvasGroup)groupField.GetValue(hud)).alpha > partial, "Reversal fades back toward visible.");
            hud.SetCameraMode(false);
            float beforeRebuild = (float)blend.GetValue(hud);
            typeof(GameplayHud).GetMethod("RebuildInspectorPreview", hidden).Invoke(hud, null);
            Require((float)blend.GetValue(hud) == beforeRebuild && !(bool)target.GetValue(hud), "Live Inspector rebuild preserves fade phase and closing target.");
            advance.Invoke(hud, new object[] { .2f });
            InspectAfterExit();
            hud.SetCameraMode(true);
            advance.Invoke(hud, new object[] { .05f });
            hud.SetCameraMode(false, true);
            InspectAfterExit();
            hud.SetCameraMode(true, true);
            InspectCurrent();
            return "PASS soft real-camera vignette; glow peak/minimum; close/reverse/rebuild/forced-exit; restored to settled photo mode";
        }

        public static string InspectAnimalFade()
        {
            Require(Application.isPlaying, "Run the animal presentation regression in Play mode.");
            var hud = Object.FindObjectOfType<GameplayHud>();
            var session = hud.GetComponent<GameplaySession>();
            var animal = session.Observed;
            Require(animal && !animal.HasAmbientPresentation && !hud.CameraMode && !hud.JournalOpen && !hud.IsDialogueOpen,
                "Use one actually observed animal outside a social exchange, photo mode or modal.");
            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            var observed = typeof(GameplaySession).GetProperty("Observed");
            var update = typeof(GameplayHud).GetMethod("UpdateAnimalBubble", hidden);
            var advance = typeof(GameplayHud).GetMethod("UpdateAnimalPresentation", hidden);
            var clear = typeof(GameplayHud).GetMethod("ClearAnimalBubble", hidden);
            var rootField = typeof(GameplayHud).GetField("bubbleRoot", hidden);
            var ownerField = typeof(GameplayHud).GetField("bubbleAnimal", hidden);
            var view = session.CurrentView;
            Quaternion rotation = view.transform.rotation;
            try
            {
                // Use the current animal presentation without accepting a task or writing progress.
                Require(session.ShouldShowAnimalUtterance(animal), "Use an animal with a currently visible utterance.");
                clear.Invoke(hud, null); update.Invoke(hud, null);
                Canvas.ForceUpdateCanvases(); advance.Invoke(hud, new object[] { 0f });
                var root = (RectTransform)rootField.GetValue(hud);
                Require(root && root.GetComponent<CanvasGroup>().alpha == 0f && !hud.IsAnimalSpeechVisible(animal), "A new HUD word begins invisible and cannot become evidence at zero alpha.");
                advance.Invoke(hud, new object[] { .02f });
                Require(root.GetComponent<CanvasGroup>().alpha > 0f && !hud.IsAnimalSpeechVisible(animal), "The first 20 ms stay below the common visible-word threshold.");
                advance.Invoke(hud, new object[] { .16f });
                Require(hud.IsAnimalSpeechVisible(animal), "A fully entered, unobstructed HUD word becomes visible evidence.");
                Vector2 originalAnchor = root.anchoredPosition;
                observed.SetValue(session, null); update.Invoke(hud, null); advance.Invoke(hud, new object[] { .04f });
                Require((AnimalActor)ownerField.GetValue(hud) == animal && root.GetComponent<CanvasGroup>().alpha > .2f &&
                    Vector2.Distance(originalAnchor, root.anchoredPosition) < .01f && hud.IsAnimalSpeechVisible(animal),
                    "A retiring word stays on its original speaker and remains visible at the same alpha threshold.");
                advance.Invoke(hud, new object[] { .14f });
                Require(!(RectTransform)rootField.GetValue(hud), "The retired HUD word is removed after its fade.");
                observed.SetValue(session, animal); update.Invoke(hud, null); advance.Invoke(hud, new object[] { .18f });
                Require(hud.IsAnimalSpeechVisible(animal), "The same animal can enter again after a completed presentation fade.");
                view.transform.rotation = Quaternion.LookRotation(-view.transform.forward);
                advance.Invoke(hud, new object[] { 0f });
                root = (RectTransform)rootField.GetValue(hud);
                Require(root && !root.gameObject.activeSelf && root.GetComponent<CanvasGroup>().alpha == 0f && !hud.IsAnimalSpeechVisible(animal),
                    "Leaving the actual camera view hides the word immediately, without a lingering off-screen bubble.");
            }
            finally
            {
                view.transform.rotation = rotation;
                observed.SetValue(session, animal);
                if (hud.IsDialogueOpen) hud.CloseDialogue();
                update.Invoke(hud, null); advance.Invoke(hud, new object[] { .18f });
            }
            return "PASS actual HUD bubble fade, visible metadata threshold, original-speaker retirement, re-entry and immediate out-of-view hiding; greeting clocks and Notebook untouched";
        }

        private static Rect ScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - 1f && inner.yMin >= outer.yMin - 1f && inner.xMax <= outer.xMax + 1f && inner.yMax <= outer.yMax + 1f;
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
