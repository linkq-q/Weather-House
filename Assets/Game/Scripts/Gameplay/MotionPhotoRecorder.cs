using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace WeatherHousePrototype
{
    public class MotionPhotoRecorder : MonoBehaviour
    {
        // Keep the component and on-disk folder names so existing scenes and photos remain readable.
        public const int Width = 2560, Height = 1440, FrameCount = 1;
        public const float FramesPerSecond = 0f;
        public bool IsRecording { get; private set; }
        public float CaptureProgress { get; private set; }
#if UNITY_EDITOR
        [Serializable] public struct CaptureCost
        {
            public int frame,unityFrame;
            public double renderMs,readPixelsMs,applyMs,encodeMs,writeMs,totalMs;
        }
        public event Action<CaptureCost> CaptureMeasured;
#endif

        private Camera captureCamera;
        private ObservationRecord pending;
        private Action<ObservationRecord> completed;
        private Action<string> failed;
        private Coroutine recording;
        private RenderTexture renderTexture;
        private Texture2D frameTexture;
        private string ownedFolder;

        public bool Capture(Camera camera, ObservationRecord snapshot,
            Action<ObservationRecord> onCompleted, Action<string> onFailed = null, Func<bool> isSnapshotCurrent = null)
        {
            if (IsRecording)
            {
                onFailed?.Invoke("正在保存照片，请稍候。");
                return false;
            }
            if (!isActiveAndEnabled || camera == null || snapshot == null)
            {
                onFailed?.Invoke("拍照需要有效的相机、观察快照和启用的记录器。");
                return false;
            }

            string id = Guid.TryParse(snapshot.id, out Guid suppliedId) ? suppliedId.ToString("N") : Guid.NewGuid().ToString("N");
            pending = new ObservationRecord
            {
                id = id, animalId = snapshot.animalId, utteranceId = snapshot.utteranceId,
                glyphIds = snapshot.glyphIds == null ? Array.Empty<string>() : (string[])snapshot.glyphIds.Clone(),
                weather = snapshot.weather, clipFolder = "MotionPhotos/" + id, context = snapshot.context?.Copy(),
                photoSnapshot = snapshot.photoSnapshot?.Copy(),
                capturedUtcTicks = snapshot.capturedUtcTicks > 0 ? snapshot.capturedUtcTicks : DateTime.UtcNow.Ticks
            };
            captureCamera = camera;
            completed = onCompleted;
            failed = onFailed;
            CaptureProgress = 0f;
            try
            {
                string folder = ResolveFolder(pending);
                if (Directory.Exists(folder)) throw new IOException("该照片目录已经存在，未覆盖原有记录。");
                Directory.CreateDirectory(folder);
                ownedFolder = folder;
                renderTexture = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 4);
                frameTexture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                IsRecording = true;
                // The ordinary photo belongs to this shutter instant, before any later dialogue turn.
                if (!TryWriteFrame(0, out string error)) throw new IOException(error);
                pending.frameCount = 1;
                CaptureProgress = 1f / FrameCount;
                recording = StartCoroutine(RecordFrames());
                return true;
            }
            catch (Exception exception)
            {
                FinishFailure("照片无法保存：" + exception.Message);
                return false;
            }
        }

        private IEnumerator RecordFrames()
        {
            // Render only at the shutter instant; publish on the next tick to preserve caller callback ordering.
            yield return null;
            recording = null;
            CompleteCapture();
        }

        private void CompleteCapture()
        {
            try
            {
                pending.framesPerSecond = FramesPerSecond;
                File.WriteAllText(Path.Combine(ownedFolder, "metadata.json"), JsonUtility.ToJson(pending, true));
            }
            catch (Exception exception)
            {
                // The index also contains the full snapshot; keep successful image files even if this sidecar fails.
                pending.captureWarning = "照片元数据保存失败：" + exception.Message;
            }
            recording = null;
            ObservationRecord result = pending;
            Action<ObservationRecord> callback = completed;
            ownedFolder = null;
            ResetCapture();
            CaptureProgress = 1f;
            callback?.Invoke(result);
        }

        // Legacy entry point: finish the pending still when the camera closes or the component disables.
        public void FinishLiveCapture()
        {
            if (!IsRecording) return;
            if (recording != null) StopCoroutine(recording);
            recording = null;
            CompleteCapture();
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
#if UNITY_EDITOR
                    long costStart=System.Diagnostics.Stopwatch.GetTimestamp(),section=costStart;
                    var cost=new CaptureCost{frame=index,unityFrame=Time.frameCount};
#endif
                    captureCamera.Render();
#if UNITY_EDITOR
                    cost.renderMs=Elapsed(ref section);
#endif
                    RenderTexture.active = renderTexture;
                    frameTexture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
#if UNITY_EDITOR
                    cost.readPixelsMs=Elapsed(ref section);
#endif
                    frameTexture.Apply(false, false);
#if UNITY_EDITOR
                    cost.applyMs=Elapsed(ref section);
#endif
                    byte[] png=frameTexture.EncodeToPNG();
#if UNITY_EDITOR
                    cost.encodeMs=Elapsed(ref section);
#endif
                    File.WriteAllBytes(Path.Combine(ownedFolder, FrameName(index)), png);
#if UNITY_EDITOR
                    cost.writeMs=Elapsed(ref section);cost.totalMs=Elapsed(ref costStart);CaptureMeasured?.Invoke(cost);
#endif
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
                error = "照片画面保存失败：" + exception.Message;
                return false;
            }
        }

        public void CancelCapture()
        {
            if (!IsRecording) return;
            if (recording != null) StopCoroutine(recording);
            recording = null;
            FinishFailure("照片已取消，未生成观察记录。");
        }
#if UNITY_EDITOR
        static double Elapsed(ref long start)
        {
            long end=System.Diagnostics.Stopwatch.GetTimestamp();double elapsed=(end-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;start=end;return elapsed;
        }
#endif

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

        private void OnDisable() { FinishLiveCapture(); }
        private void OnApplicationQuit() { FinishLiveCapture(); }

        public static Texture2D LoadFrame(ObservationRecord record, int index)
        {
            string folder = ResolveFolder(record);
            if (index < 0 || index >= record.frameCount) throw new ArgumentOutOfRangeException(nameof(index), "照片画面序号越界。");
            string path = Path.Combine(folder, FrameName(index));
            if (!File.Exists(path)) throw new FileNotFoundException("照片文件缺失。", path);
            var file = new FileInfo(path);
            if (file.Length < 24 || file.Length > 24 * 1024 * 1024)
                throw new InvalidDataException("照片文件大小无效。");
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71 ||
                bytes[4] != 13 || bytes[5] != 10 || bytes[6] != 26 || bytes[7] != 10 ||
                !((ReadBigEndian(bytes, 16) == Width && ReadBigEndian(bytes, 20) == Height) ||
                  (ReadBigEndian(bytes, 16) == 512 && ReadBigEndian(bytes, 20) == 288)))
                throw new InvalidDataException("照片不是受支持的 2560×1440 或旧版 512×288 PNG。");
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                if (!texture.LoadImage(bytes, false)) throw new InvalidDataException("照片无法解码。");
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
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
            string root = Path.GetFullPath(Path.Combine(GameplaySession.PersistenceDirectory, "MotionPhotos"));
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
