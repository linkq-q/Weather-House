using System;
using System.Collections;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHousePrototype
{
    public static class MotionPhotoChecks
    {
        public static void RunStaticChecks()
        {
            Require(MotionPhotoRecorder.FrameCount == 1 && MotionPhotoRecorder.Width == 2560 && MotionPhotoRecorder.Height == 1440,
                "New captures must be one 2560 x 1440 still photograph.");
            string id = Guid.NewGuid().ToString("N");
            Expect<ArgumentException>(() => MotionPhotoRecorder.LoadFrame(new ObservationRecord
                { id = id, clipFolder = "../MotionPhotos/" + id, frameCount = 1 }, 0));
            Expect<ArgumentException>(() => MotionPhotoRecorder.LoadFrame(new ObservationRecord
                { id = id, clipFolder = Path.Combine(Application.persistentDataPath, "MotionPhotos", id), frameCount = 1 }, 0));
            Expect<ArgumentException>(() => MotionPhotoRecorder.LoadFrame(new ObservationRecord
                { id = Guid.NewGuid().ToString("N"), clipFolder = "MotionPhotos/" + id, frameCount = 1 }, 0));
            var missing = new ObservationRecord { id = id, clipFolder = "MotionPhotos/" + id, frameCount = 1 };
            Expect<ArgumentOutOfRangeException>(() => MotionPhotoRecorder.LoadFrame(missing, -1));
            Expect<FileNotFoundException>(() => MotionPhotoRecorder.LoadFrame(missing, 0));
            Directory.CreateDirectory("PrototypeReview");
            File.WriteAllText("PrototypeReview/motion-photo-static.txt",
                "PASS: parent traversal, absolute path, mismatched ID and out-of-range index rejected; missing frame reported.\n");
        }

        public static IEnumerator RunPlayChecks()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Motion photo render checks require Play mode.");
            RunStaticChecks();
            var fixture = new GameObject("Motion photo isolated render check");
            fixture.transform.position = new Vector3(10000f, 10000f, 10000f);
            Material material = null;
            Texture2D first = null, last = null;
            ObservationRecord saved = null;
            string clipId = Guid.NewGuid().ToString("N");
            string cancelledId = Guid.NewGuid().ToString("N");
            string disabledId = Guid.NewGuid().ToString("N");
            string failedId = Guid.NewGuid().ToString("N");
            try
            {
                var cameraObject = new GameObject("Motion photo check camera");
                cameraObject.transform.SetParent(fixture.transform, false);
                cameraObject.transform.localPosition = new Vector3(0f, 0f, -5f);
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = 2f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.cullingMask = 1 << 29;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 10f;
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Moving color proof";
                cube.layer = 29;
                cube.transform.SetParent(fixture.transform, false);
                cube.GetComponent<Collider>().enabled = false;
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.SetColor("_BaseColor", new Color(.05f, 1f, .15f));
                cube.GetComponent<Renderer>().sharedMaterial = material;
                var recorder = fixture.AddComponent<MotionPhotoRecorder>();
                string error = null;
                var metadata = new ObservationRecord
                {
                    id = clipId, animalId = "render-check-animal", utteranceId = "render-check-request",
                    glyphIds = new[] { "rain", "want" }, weather = "Rain"
                };
                Require(recorder.Capture(camera, metadata, record => saved = record, message => error = message), "Capture did not start.");
                metadata.glyphIds[0] = "changed-after-capture";
                bool busyRejected = false;
                Require(!recorder.Capture(camera, metadata, null, message => busyRejected = true) && busyRejected,
                    "Overlapping capture was not rejected.");
                float startedAt = Time.realtimeSinceStartup;
                while (recorder.IsRecording && Time.realtimeSinceStartup - startedAt < 10f)
                {
                    cube.transform.localPosition = new Vector3(Mathf.Lerp(-1f, 1f,
                        (Time.realtimeSinceStartup - startedAt) / 3f), 0f, 0f);
                    yield return null;
                }
                if (recorder.IsRecording) recorder.CancelCapture();
                Require(error == null && saved != null, "Capture failed: " + error);
                Require(saved.frameCount == 1 && saved.framesPerSecond == 0f && !saved.liveTruncated, "Static photo metadata is incorrect.");
                Require(saved.glyphIds[0] == "rain" && saved.weather == "Rain", "Observation metadata was not snapshotted.");
                Require(camera.targetTexture == null, "Camera render target was not restored.");
                first = MotionPhotoRecorder.LoadFrame(saved, 0);
                cube.transform.localPosition = Vector3.right * 100;
                yield return new WaitForSecondsRealtime(.25f);
                last = MotionPhotoRecorder.LoadFrame(saved, 0);
                Color32[] a = first.GetPixels32(), b = last.GetPixels32();
                int changed = 0;
                for (int i = 0; i < a.Length; i++)
                    if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > 30) changed++;
                Require(first.width == 2560 && first.height == 1440 && changed == 0, "The high-resolution shutter image changed after the world moved.");
                Require(Array.Exists(a, pixel => pixel.g > 150 && pixel.g > pixel.r * 2), "The rendered photo contains no visible fixture.");
                string folder = TestFolder(clipId);
                Require(Directory.GetFiles(folder, "frame_*.png").Length == 1 && File.Exists(Path.Combine(folder, "metadata.json")),
                    "The single photograph was not saved to disk.");

                bool cancelled = false, wronglyCompleted = false;
                recorder.Capture(camera, new ObservationRecord { id = cancelledId },
                    record => wronglyCompleted = true, message => cancelled = true);
                while (recorder.IsRecording && recorder.CaptureProgress == 0f) yield return null;
                Require(File.Exists(Path.Combine(TestFolder(cancelledId), "frame_000.png")), "Cancellation test did not write a partial frame.");
                recorder.CancelCapture();
                Require(cancelled && !wronglyCompleted && !recorder.IsRecording && !Directory.Exists(TestFolder(cancelledId)),
                    "Cancellation did not remove the incomplete clip.");
                ObservationRecord disabled = null;
                recorder.Capture(camera, new ObservationRecord { id = disabledId },
                    record => disabled = record, message => error = message);
                while (recorder.IsRecording && recorder.CaptureProgress == 0f) yield return null;
                Require(File.Exists(Path.Combine(TestFolder(disabledId), "frame_000.png")), "Disable test did not write a partial frame.");
                recorder.enabled = false;
                Require(disabled != null && disabled.frameCount == 1 && !disabled.liveTruncated && error == null && File.Exists(Path.Combine(TestFolder(disabledId), "metadata.json")),
                    "Disabling the recorder did not preserve the shutter and finalize its metadata.");
                recorder.enabled = true;
                ObservationRecord recovered = null;
                recorder.Capture(camera, new ObservationRecord { id = failedId },
                    record => recovered = record, message => error = message);
                while (recorder.IsRecording && recorder.CaptureProgress == 0f) yield return null;
                Object.Destroy(camera);
                float failureStartedAt = Time.realtimeSinceStartup;
                while (recorder.IsRecording && Time.realtimeSinceStartup - failureStartedAt < 2f) yield return null;
                Require(recovered != null && recovered.frameCount == 1 && string.IsNullOrEmpty(recovered.captureWarning) && !recorder.IsRecording &&
                    File.Exists(Path.Combine(TestFolder(failedId), "frame_000.png")), "Removing the camera after the shutter damaged the completed still.");
                File.WriteAllText("PrototypeReview/motion-photo-play.txt",
                    "PASS: one 2560x1440 still remains unchanged after world motion; changed pixels=" + changed +
                    "; metadata snapshot, overlap rejection, explicit cancellation cleanup, and disable/camera-failure still preservation passed.\n" +
                    "The fixture checks actual rendering and storage, not the user's scene composition.\n");
            }
            finally
            {
                if (fixture != null)
                {
                    fixture.GetComponent<MotionPhotoRecorder>()?.CancelCapture();
                    Object.Destroy(fixture);
                }
                if (material != null) Object.Destroy(material);
                if (first != null) Object.Destroy(first);
                if (last != null) Object.Destroy(last);
                foreach (string id in new[] { clipId, cancelledId, disabledId, failedId })
                {
                    string path = TestFolder(id);
                    if (Directory.Exists(path)) Directory.Delete(path, true);
                }
            }
        }

        private static string TestFolder(string id)
        {
            if (!Guid.TryParseExact(id, "N", out Guid _)) throw new ArgumentException("Invalid test clip ID.");
            string root = Path.GetFullPath(Path.Combine(GameplaySession.PersistenceDirectory, "MotionPhotos"));
            string path = Path.GetFullPath(Path.Combine(root, id));
            Require(path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Test directory escaped its root.");
            return path;
        }

        private static void Expect<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
