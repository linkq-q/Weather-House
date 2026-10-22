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
            var hud = session.GetComponent<GameplayHud>();
            var walker = session.walker;
            Vector3 prior = walker.transform.position;
            float priorYaw = walker.yaw, priorPitch = walker.pitch;
            Application.runInBackground = true;
            try
            {
            var animal = Array.Find(session.animals, a => a.animalId == "courtyard-squirrel");
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
            session.PhotoRig.Zoom((35f - session.PhotoRig.FocalLength) / 5f);
            Vector3 target = animal.transform.position + Vector3.up * .65f;
            Vector3 direction = target - (walker.transform.position + Vector3.up * 1.55f);
            walker.yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            walker.pitch = -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            yield return new WaitForSecondsRealtime(.4f);
            session.PhotoRig.FocusAt(session.PhotoRig.CaptureCamera.WorldToScreenPoint(target));
            yield return null;
            if (walker.visualRoot.GetComponentsInChildren<Renderer>().Any(r => r.enabled))
                throw new InvalidOperationException("Avatar is still rendered in photo mode.");
            if (session.CurrentView != session.PhotoRig.CaptureCamera)
                throw new InvalidOperationException("Observation is not using the first-person camera.");
            yield return Capture("Gameplay_Camera");
            session.PhotoRig.Zoom(7f);
            yield return new WaitForSecondsRealtime(.3f);
            if (session.Observed != animal) throw new InvalidOperationException("Zoom lost the animal while its body remained in frame.");
            yield return Capture("Gameplay_Camera_Zoom70mm");
            session.PhotoRig.Zoom(-7f);
            yield return new WaitForSecondsRealtime(.3f);
            if (session.Observed != animal) throw new InvalidOperationException("The first-person camera did not observe the squirrel.");
            int previousPhotos = session.Notebook.observations.Count;
            session.TakePhoto();
            while (session.Recorder.IsRecording) yield return null;
            if (session.Notebook.observations.Count != previousPhotos + 1)
                throw new InvalidOperationException("The first-person photograph did not reach the notebook.");
            var latest = session.Notebook.observations.Last();
            if (latest.frameCount != 18) throw new InvalidOperationException("The first-person photograph did not contain 18 frames.");
            hud.ToggleJournal();
            for (int i = 0; i < previousPhotos / 4; i++)
                hud.SendMessage("TurnJournalPage", 1, SendMessageOptions.RequireReceiver);
            yield return null;
            yield return Capture("Gameplay_Journal");
            var photoButton = Object.FindObjectsOfType<Button>().FirstOrDefault(b => b.name == "Photo " + latest.id);
            if (photoButton != null)
            {
                photoButton.onClick.Invoke();
                yield return Capture("Gameplay_Photo");
            }
            hud.ToggleJournal();
            float closeDeadline = Time.realtimeSinceStartup + AlbumBookOpening.Duration + .5f;
            while (hud.JournalOpen && Time.realtimeSinceStartup < closeDeadline) yield return null;
            if (hud.JournalOpen) throw new InvalidOperationException("Journal reverse playback did not finish closing.");
            File.WriteAllText("PrototypeReview/gameplay-visual.txt", "PASS: first-person capture uses photo camera; avatar hidden; 35/70 mm review images; 18-frame real clip added and opened in notebook. Inspect PNGs visually.\n");
            }
            finally
            {
                if (hud.JournalOpen && !hud.GetComponentsInChildren<AlbumBookOpening>().Any(a => a.IsClosing)) hud.ToggleJournal();
                hud.SetCameraMode(false);
                walker.Teleport(prior); walker.yaw = priorYaw; walker.pitch = priorPitch;
                Application.runInBackground = background;
            }
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
