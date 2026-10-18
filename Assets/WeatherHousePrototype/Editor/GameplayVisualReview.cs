using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace WeatherHousePrototype.Editor
{
    public static class GameplayVisualReview
    {
        public static void Run()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play for the gameplay visual review.");
            var session = Object.FindObjectOfType<GameplaySession>();
            session.StartCoroutine(Review(session));
        }

        private static IEnumerator Review(GameplaySession session)
        {
            bool background = Application.runInBackground;
            Application.runInBackground = true;
            try
            {
            var hud = session.GetComponent<GameplayHud>();
            var walker = session.walker;
            var animal = Array.Find(session.animals, a => a.animalId == "courtyard-squirrel");
            Vector3 prior = walker.transform.position;
            float priorYaw = walker.yaw, priorPitch = walker.pitch;
            Vector3 point = animal.transform.position + new Vector3(0f, 0f, -4f);
            point.y = Terrain.activeTerrain.SampleHeight(point) + Terrain.activeTerrain.transform.position.y + .1f;
            walker.Teleport(point);
            walker.yaw = 0f; walker.pitch = 20f;
            yield return new WaitForSecondsRealtime(1f);
            Canvas.ForceUpdateCanvases();
            string dump = string.Join("\n", Object.FindObjectsOfType<AnimalGlyphGraphic>().Select(g =>
                g.name + " body=" + g.SvgBody?.Length + " rect=" + g.rectTransform.rect + " color=" + g.color +
                " renderer=" + (g.GetComponent<CanvasRenderer>() != null) + " cull=" + g.canvasRenderer.cull +
                " vertices=" + g.canvasRenderer.GetMesh()?.vertexCount));
            File.WriteAllText("PrototypeReview/gameplay-graphics.txt", dump);
            yield return Capture("Gameplay_Observation");
            hud.SetCameraMode(true);
            yield return Capture("Gameplay_Camera");
            if (session.Observed != null)
            {
                session.TakePhoto();
                while (session.Recorder.IsRecording) yield return null;
            }
            hud.ToggleJournal();
            yield return Capture("Gameplay_Journal");
            var photoButton = Object.FindObjectsOfType<Button>().FirstOrDefault(b => b.name.StartsWith("Photo "));
            if (photoButton != null)
            {
                photoButton.onClick.Invoke();
                yield return Capture("Gameplay_Photo");
            }
            hud.ToggleJournal();
            walker.Teleport(prior); walker.yaw = priorYaw; walker.pitch = priorPitch;
            File.WriteAllText("PrototypeReview/gameplay-visual.txt", "Review captures complete. One real observation clip was added for prototype acceptance. Inspect PNGs visually.\n");
            }
            finally { Application.runInBackground = background; }
        }

        private static IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes("PrototypeReview/" + name + ".png", image.EncodeToPNG());
            Object.Destroy(image);
        }
    }
}
