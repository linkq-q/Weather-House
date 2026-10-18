using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace WeatherHousePrototype
{
    public class MotionPhotoRecorder : MonoBehaviour
    {
        public const int Width = 512, Height = 288, FrameCount = 18;
        public const float FramesPerSecond = 6f;
        public bool IsRecording { get; private set; }
        public float CaptureProgress { get; private set; }

        private Camera captureCamera;
        private ObservationRecord pending;
        private Action<ObservationRecord> completed;
        private Action<string> failed;
        private Coroutine recording;
        private RenderTexture renderTexture;
        private Texture2D frameTexture;
        private string ownedFolder;

        public bool Capture(Camera camera, ObservationRecord snapshot,
            Action<ObservationRecord> onCompleted, Action<string> onFailed = null)
        {
            if (IsRecording)
            {
                onFailed?.Invoke("正在记录动态照片，请等待本次记录完成。");
                return false;
            }
            if (!isActiveAndEnabled || camera == null || snapshot == null)
            {
                onFailed?.Invoke("动态照片需要有效的相机、观察快照和启用的记录器。");
                return false;
            }

            string id = Guid.TryParse(snapshot.id, out Guid suppliedId) ? suppliedId.ToString("N") : Guid.NewGuid().ToString("N");
            pending = new ObservationRecord
            {
                id = id, animalId = snapshot.animalId, utteranceId = snapshot.utteranceId,
                glyphIds = snapshot.glyphIds == null ? Array.Empty<string>() : (string[])snapshot.glyphIds.Clone(),
                weather = snapshot.weather, clipFolder = "MotionPhotos/" + id,
                capturedUtcTicks = snapshot.capturedUtcTicks > 0 ? snapshot.capturedUtcTicks : DateTime.UtcNow.Ticks
            };
            captureCamera = camera;
            completed = onCompleted;
            failed = onFailed;
            CaptureProgress = 0f;
            try
            {
                string folder = ResolveFolder(pending);
                if (Directory.Exists(folder)) throw new IOException("该动态照片目录已经存在，未覆盖原有记录。");
                Directory.CreateDirectory(folder);
                ownedFolder = folder;
                renderTexture = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
                frameTexture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                IsRecording = true;
                recording = StartCoroutine(RecordFrames());
                return true;
            }
            catch (Exception exception)
            {
                FinishFailure("动态照片无法开始：" + exception.Message);
                return false;
            }
        }

        private IEnumerator RecordFrames()
        {
            // Let the caller finish setting up the observation before the first world render.
            yield return null;
            float startedAt = Time.realtimeSinceStartup;
            for (int frame = 0; frame < FrameCount; frame++)
            {
                while (Time.realtimeSinceStartup < startedAt + frame / FramesPerSecond)
                    yield return null;
                if (!TryWriteFrame(frame, out string error))
                {
                    recording = null;
                    FinishFailure(error);
                    yield break;
                }
                CaptureProgress = (frame + 1f) / FrameCount;
            }
            while (Time.realtimeSinceStartup < startedAt + FrameCount / FramesPerSecond)
                yield return null;

            string completionError = null;
            try
            {
                pending.frameCount = FrameCount;
                pending.framesPerSecond = FramesPerSecond;
                File.WriteAllText(Path.Combine(ownedFolder, "metadata.json"), JsonUtility.ToJson(pending, true));
            }
            catch (Exception exception) { completionError = "动态照片保存失败：" + exception.Message; }
            recording = null;
            if (completionError != null)
            {
                FinishFailure(completionError);
                yield break;
            }
            ObservationRecord result = pending;
            Action<ObservationRecord> callback = completed;
            ownedFolder = null;
            ResetCapture();
            CaptureProgress = 1f;
            callback?.Invoke(result);
        }

        private bool TryWriteFrame(int index, out string error)
        {
            error = null;
            try
            {
                if (captureCamera == null) throw new InvalidOperationException("录制相机已经移除。");
                RenderTexture previousTarget = captureCamera.targetTexture;
                RenderTexture previousActive = RenderTexture.active;
                int previousMask = captureCamera.cullingMask;
                Canvas[] canvases = FindObjectsOfType<Canvas>();
                bool[] canvasEnabled = new bool[canvases.Length];
                try
                {
                    for (int i = 0; i < canvases.Length; i++)
                    {
                        canvasEnabled[i] = canvases[i].enabled;
                        canvases[i].enabled = false;
                    }
                    int uiLayer = LayerMask.NameToLayer("UI");
                    if (uiLayer >= 0) captureCamera.cullingMask &= ~(1 << uiLayer);
                    captureCamera.targetTexture = renderTexture;
                    captureCamera.Render();
                    RenderTexture.active = renderTexture;
                    frameTexture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    frameTexture.Apply(false, false);
                    File.WriteAllBytes(Path.Combine(ownedFolder, FrameName(index)), frameTexture.EncodeToPNG());
                }
                finally
                {
                    if (captureCamera != null)
                    {
                        captureCamera.targetTexture = previousTarget;
                        captureCamera.cullingMask = previousMask;
                    }
                    RenderTexture.active = previousActive;
                    for (int i = 0; i < canvases.Length; i++)
                        if (canvases[i] != null) canvases[i].enabled = canvasEnabled[i];
                }
                return true;
            }
            catch (Exception exception)
            {
                error = "动态照片第 " + (index + 1) + " 帧记录失败：" + exception.Message;
                return false;
            }
        }

        public void CancelCapture()
        {
            if (!IsRecording) return;
            if (recording != null) StopCoroutine(recording);
            recording = null;
            FinishFailure("动态照片已取消，未生成观察记录。");
        }

        private void FinishFailure(string message)
        {
            Action<string> callback = failed;
            if (ownedFolder != null)
            {
                try
                {
                    if (pending == null || !string.Equals(ownedFolder, ResolveFolder(pending), StringComparison.Ordinal))
                        throw new IOException("临时照片目录校验失败。");
                    if (Directory.Exists(ownedFolder)) Directory.Delete(ownedFolder, true);
                }
                catch (Exception exception) { message += " 清理临时帧失败：" + exception.Message; }
            }
            ownedFolder = null;
            ResetCapture();
            CaptureProgress = 0f;
            callback?.Invoke(message);
        }

        private void ResetCapture()
        {
            IsRecording = false;
            if (renderTexture != null) RenderTexture.ReleaseTemporary(renderTexture);
            if (frameTexture != null) Destroy(frameTexture);
            renderTexture = null;
            frameTexture = null;
            captureCamera = null;
            pending = null;
            completed = null;
            failed = null;
        }

        private void OnDisable() { CancelCapture(); }

        public static Texture2D LoadFrame(ObservationRecord record, int index)
        {
            string folder = ResolveFolder(record);
            if (index < 0 || index >= record.frameCount) throw new ArgumentOutOfRangeException(nameof(index), "动态照片帧号越界。");
            string path = Path.Combine(folder, FrameName(index));
            if (!File.Exists(path)) throw new FileNotFoundException("动态照片帧文件缺失。", path);
            var file = new FileInfo(path);
            if (file.Length < 24 || file.Length > 2 * 1024 * 1024)
                throw new InvalidDataException("动态照片帧文件大小无效。");
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71 ||
                bytes[4] != 13 || bytes[5] != 10 || bytes[6] != 26 || bytes[7] != 10 ||
                ReadBigEndian(bytes, 16) != Width || ReadBigEndian(bytes, 20) != Height)
                throw new InvalidDataException("动态照片帧不是预期的 512×288 PNG。");
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                if (!texture.LoadImage(bytes, false)) throw new InvalidDataException("动态照片帧无法解码。");
                return texture;
            }
            catch
            {
                if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture);
                throw;
            }
        }

        private static string ResolveFolder(ObservationRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.clipFolder))
                throw new ArgumentException("动态照片缺少目录信息。", nameof(record));
            string[] segments = record.clipFolder.Replace('\\', '/').Split('/');
            if (segments.Length != 2 || segments[0] != "MotionPhotos" ||
                !Guid.TryParseExact(segments[1], "N", out Guid id) || record.id != id.ToString("N"))
                throw new ArgumentException("动态照片目录必须位于本地 MotionPhotos 目录内。", nameof(record));
            string root = Path.GetFullPath(Path.Combine(Application.persistentDataPath, "MotionPhotos"));
            string folder = Path.GetFullPath(Path.Combine(root, segments[1]));
            if (!folder.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("动态照片目录超出了本地存储范围。", nameof(record));
            return folder;
        }

        private static string FrameName(int index) { return "frame_" + index.ToString("D3") + ".png"; }
        private static int ReadBigEndian(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        }
    }
}
