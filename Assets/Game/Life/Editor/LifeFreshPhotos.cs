using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.WatersideV2;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    // Only player viewpoints are arranged. Animals, weather, snapshots and awards use normal gameplay.
    public static class LifeFreshPhotos
    {
        const string Folder = "LifeGameplayReview/20261026/photo-replacement-staging";
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly string[] Subjects = { "dog", "squirrel", "fox", "deer" };
        static readonly float[] Distances = { 3.4f, 1.8f, 2.8f, 3.4f };
        static readonly float[] Angles = { 35f, -35f, 65f, -65f };
        [Serializable] sealed class Shot
        {
            public string subject, animalId, firstFrame, lastFrame, metadata;
            public Vector3 playerPosition, cameraPosition, cameraForward;
            public ObservationRecord record;
        }
        [Serializable] sealed class Report
        {
            public string status = "RUNNING", utc = DateTime.UtcNow.ToString("o"), saveDirectory, error;
            public string boundary = "Actual formal-scene PhotoRig/TakePhoto/Recorder capture. Player viewpoints are fixtures; animals and world remain live. PASS means four complete saved Live photos with real subject visibility, not visual composition approval. No real save import is performed. Isolated rewards must not be imported.";
            public float seconds;
            public List<Shot> shots = new List<Shot>();
        }

        public static IEnumerator Run(LifeSession life, Action<string> completed)
        {
            var report = new Report(); float began = Time.realtimeSinceStartup;
            IEnumerator steps = Capture(life, report); Exception failure = null;
            while (true)
            {
                bool more; object next = null;
                try
                {
                    if (Time.realtimeSinceStartup - began > 90) throw new TimeoutException("Fresh photographs exceeded 90 seconds.");
                    more = steps.MoveNext(); if (more) next = steps.Current;
                }
                catch (Exception error) { failure = error; break; }
                if (!more) break;
                yield return next;
            }
            try { (steps as IDisposable)?.Dispose(); } catch (Exception error) { failure = failure ?? error; }
            report.seconds = Time.realtimeSinceStartup - began;
            report.status = failure == null ? "PASS" : "FAIL"; report.error = failure?.ToString();
            Write(report);
            completed?.Invoke(report.status + " fresh photographs: " + report.shots.Count + "/4 complete. Visual review pending. " +
                Path.GetFullPath(Folder + "/fresh-photos.json") + (failure == null ? "" : "\n" + failure));
        }

        static IEnumerator Capture(LifeSession life, Report report)
        {
            string isolated = Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE");
            string reviewRoot = Path.GetFullPath("LifeGameplayReview") + Path.DirectorySeparatorChar;
            Require(Application.isPlaying && life && life.World && life.Gameplay &&
                SceneManager.GetActiveScene().path == "Assets/Scenes/WeatherHouse_WatersideV2.unity", "Fresh photographs need the running formal scene.");
            Require(!string.IsNullOrEmpty(isolated) && Path.GetFullPath(isolated).StartsWith(reviewRoot, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFullPath(isolated) == Path.GetFullPath(GameplaySession.PersistenceDirectory), "Capture requires isolated persistence inside Review.");
            report.saveDirectory = Path.GetFullPath(isolated); Write(report);
            var game = life.Gameplay; var hud = game.GetComponent<GameplayHud>(); var mover = game.forestSprite;
            var capsule = mover ? mover.GetComponent<CharacterController>() : null;
            Require(hud && mover && capsule && capsule.enabled && !mover.ScriptedInput && !game.Judging &&
                !hud.CameraMode && !hud.JournalOpen && !hud.IsDialogueOpen && !game.Recorder.IsRecording, "Player, camera and HUD must start idle.");
            Vector3 originalPosition = mover.transform.position; Quaternion originalRotation = mover.transform.rotation;
            var yaw = typeof(PhotoCameraRig).GetProperty("Yaw", Hidden);
            var pitch = typeof(PhotoCameraRig).GetProperty("Pitch", Hidden);
            var updatePose = typeof(PhotoCameraRig).GetMethod("UpdatePose", Hidden);
            Require(yaw != null && pitch != null && updatePose != null, "Existing PhotoRig pose controls are available.");
            mover.SetScriptedInput(Vector2.zero, false, false);
            try
            {
                hud.SetCameraMode(true, true);
                for (int index = 0; index < Subjects.Length; index++)
                {
                    LifeSpeaker speaker = life.World.FindSpeaker(Subjects[index]);
                    AnimalActor actor = speaker ? speaker.Actor : null;
                    Require(actor && actor.IsAvailableForObservation, "Missing live subject: " + Subjects[index]);
                    bool framed = false;
                    foreach (float offset in new[] { 0f, 35f, -35f, 70f, -70f, 135f, -135f, 180f })
                    {
                        Bounds bounds = BoundsOf(actor);
                        // The courtyard chairs hide the small squirrel from the southern approach.
                        float heading = index == 1 ? 90f : actor.transform.eulerAngles.y + Angles[index];
                        if (!TryStand(game, capsule, bounds.center, Distances[index], heading + offset, out Vector3 position)) continue;
                        mover.transform.position = position; Physics.SyncTransforms();
                        // Let the real controller settle before aiming; do not suspend animal updates.
                        yield return null; yield return null;
                        Vector3 eye = mover.transform.position + Vector3.up * Mathf.Max(.2f, mover.cameraHeight);
                        Vector3 angles = Quaternion.LookRotation(BoundsOf(actor).center - eye).eulerAngles;
                        yaw.SetValue(game.PhotoRig, angles.y); pitch.SetValue(game.PhotoRig, Mathf.DeltaAngle(0, angles.x));
                        updatePose.Invoke(game.PhotoRig, null);
                        yield return null;
                        Camera camera = game.PhotoRig.CaptureCamera;
                        Vector3 focus = camera.WorldToScreenPoint(BoundsOf(actor).center);
                        game.PhotoRig.FocusAt(new Vector2(focus.x, focus.y));
                        float settled = Time.realtimeSinceStartup + .4f;
                        while (Time.realtimeSinceStartup < settled) yield return null;
                        // The live animal and player may move while focusing; verify the final shutter pose.
                        eye = mover.transform.position + Vector3.up * Mathf.Max(.2f, mover.cameraHeight);
                        angles = Quaternion.LookRotation(BoundsOf(actor).center - eye).eulerAngles;
                        yaw.SetValue(game.PhotoRig, angles.y); pitch.SetValue(game.PhotoRig, Mathf.DeltaAngle(0, angles.x));
                        updatePose.Invoke(game.PhotoRig, null);
                        focus = camera.WorldToScreenPoint(BoundsOf(actor).center);
                        game.PhotoRig.FocusAt(new Vector2(focus.x, focus.y));
                        LifePhotoSnapshot view = LifePhotoCapture.Capture(life, camera);
                        Require(string.IsNullOrEmpty(view.visibilityError), "Actual depth recognition failed: " + view.visibilityError);
                        if (view.entities.Any(e => e.id == actor.animalId && e.bodyVisible)) { framed = true; break; }
                    }
                    Require(framed, "No unobstructed supported viewpoint for " + Subjects[index]);
                    // No frame elapses between the visibility check and the ordinary shutter capture.
                    int before = game.Notebook.observations.Count;
                    game.TakePhoto(); Require(game.Recorder.IsRecording, "TakePhoto did not begin for " + Subjects[index]);
                    float deadline = Time.realtimeSinceStartup + 15;
                    while (game.Recorder.IsRecording && Time.realtimeSinceStartup < deadline) yield return null;
                    Require(!game.Recorder.IsRecording && game.Notebook.observations.Count == before + 1, "Photo did not finish for " + Subjects[index]);
                    ObservationRecord record = game.Notebook.observations.Last();
                    Require(record.frameCount == MotionPhotoRecorder.FrameCount && record.framesPerSecond == MotionPhotoRecorder.FramesPerSecond &&
                        !record.liveTruncated && string.IsNullOrEmpty(record.captureWarning) && record.photoSnapshot != null &&
                        string.IsNullOrEmpty(record.photoSnapshot.visibilityError) && record.photoSnapshot.entities.Any(e => e.id == actor.animalId && e.bodyVisible),
                        "Photo is incomplete, warned, or lacks the actual subject: " + Subjects[index] +
                        "; frames=" + record.frameCount + "; fps=" + record.framesPerSecond + "; truncated=" + record.liveTruncated +
                        "; warning=" + record.captureWarning + "; visibility=" + record.photoSnapshot?.visibilityError +
                        "; visible=" + string.Join(",", record.photoSnapshot?.entities?.Where(entity => entity.bodyVisible).Select(entity => entity.id) ?? Array.Empty<string>()));
                    Require(string.IsNullOrEmpty(game.LastSaveError), "Photo index write failed: " + game.LastSaveError);
                    var disk = JsonUtility.FromJson<LanguageNotebook>(File.ReadAllText(game.SavePath));
                    Require(disk.observations.Any(p => p.id == record.id && JsonUtility.ToJson(p) == JsonUtility.ToJson(record)), "Photo is not intact in the saved index.");
                    Require(Guid.TryParseExact(record.id, "N", out _) && record.clipFolder == "MotionPhotos/" + record.id, "Unexpected photo directory.");
                    string directory = Path.Combine(report.saveDirectory, "MotionPhotos", record.id);
                    for (int frame = 0; frame < record.frameCount; frame++) Require(File.Exists(Path.Combine(directory, "frame_" + frame.ToString("D3") + ".png")), "A Live frame is missing.");
                    foreach (int frame in new[] { 0, record.frameCount - 1 })
                    {
                        Texture2D image = MotionPhotoRecorder.LoadFrame(record, frame);
                        Require(image && image.width == MotionPhotoRecorder.Width && image.height == MotionPhotoRecorder.Height, "A review endpoint could not be decoded.");
                        UnityEngine.Object.Destroy(image);
                    }
                    string metadata = Path.Combine(directory, "metadata.json");
                    var savedMetadata = JsonUtility.FromJson<ObservationRecord>(File.ReadAllText(metadata));
                    Require(savedMetadata.id == record.id && savedMetadata.frameCount == record.frameCount, "Capture metadata does not match the actual record.");
                    report.shots.Add(new Shot { subject = Subjects[index], animalId = actor.animalId,
                        playerPosition = mover.transform.position, cameraPosition = record.photoSnapshot.cameraPosition, cameraForward = record.photoSnapshot.cameraForward,
                        firstFrame = Path.Combine(directory, "frame_000.png"), lastFrame = Path.Combine(directory, "frame_017.png"), metadata = metadata,
                        record = JsonUtility.FromJson<ObservationRecord>(JsonUtility.ToJson(record)) });
                    Write(report);
                }
            }
            finally
            {
                game.Recorder.FinishLiveCapture(); hud.SetCameraMode(false, true);
                mover.ReleaseScriptedInput(); mover.transform.SetPositionAndRotation(originalPosition, originalRotation); Physics.SyncTransforms();
            }
        }

        internal static Bounds BoundsOf(AnimalActor actor)
        {
            Renderer[] renderers = actor.VisualRoot ? actor.VisualRoot.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray() : Array.Empty<Renderer>();
            Require(renderers.Length > 0, "Subject has no active rendered body.");
            Bounds bounds = renderers[0].bounds; foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds); return bounds;
        }

        internal static bool TryStand(GameplaySession game, CharacterController capsule, Vector3 target, float distance, float angle, out Vector3 point)
        {
            point = target + Quaternion.Euler(0, angle, 0) * Vector3.forward * distance;
            Terrain terrain = Terrain.activeTerrain; if (!terrain || !terrain.terrainData) return false;
            Vector3 local = point - terrain.transform.position, size = terrain.terrainData.size;
            if (local.x < 0 || local.z < 0 || local.x > size.x || local.z > size.z || terrain.terrainData.GetSteepness(local.x / size.x, local.z / size.z) > 30) return false;
            point.y = terrain.SampleHeight(point) + terrain.transform.position.y;
            var water = game.PlayerTransform.GetComponent<WatersideWaterBoundary>();
            if (water && water.surface && water.terrain && water.SampleSupportedDepth(point, point.y) > water.maximumWadingDepth) return false;
            Transform player = game.PlayerTransform; Vector3 offset = player.TransformVector(capsule.center);
            float height = capsule.height * Mathf.Abs(player.lossyScale.y);
            point.y += -offset.y + height * .5f + .08f;
            float radius = capsule.radius * Mathf.Max(Mathf.Abs(player.lossyScale.x), Mathf.Abs(player.lossyScale.z));
            float half = Mathf.Max(0, height * .5f - radius); Vector3 center = point + offset;
            return !Physics.OverlapCapsule(center + Vector3.up * half, center - Vector3.up * half, radius * .95f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).Any(c => !(c is TerrainCollider) && !c.transform.IsChildOf(player));
        }

        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Write(Report report) { Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/fresh-photos.json", JsonUtility.ToJson(report, true)); }
    }
}
