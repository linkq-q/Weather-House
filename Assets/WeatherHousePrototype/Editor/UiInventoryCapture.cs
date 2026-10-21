using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace WeatherHousePrototype.Editor
{
    public static class UiInventoryCapture
    {
        private const string Output = "PrototypeReview/UiInventory";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static bool running;
        [Serializable] private sealed class Entry
        {
            public int index, width, height;
            public string filename, title, trigger, state, provenance;
            [NonSerialized] public Action prepare;
        }
        [Serializable] private sealed class Manifest
        {
            public string scene, utc, result;
            public List<Entry> items = new List<Entry>();
        }

        public static void Run()
        {
            Require(!running && EditorApplication.isPlaying, "UI inventory requires Play and no other inventory capture.");
            Require(SceneManager.GetActiveScene().path == "Assets/WeatherHousePrototype/WeatherHouse_MapDraft.unity", "Open the actual WeatherHouse_MapDraft gameplay scene.");
            var session = Object.FindObjectOfType<GameplaySession>();
            Require(session != null && session.isActiveAndEnabled && !session.Judging && !session.Recorder.IsRecording,
                "Wait for the gameplay session to start and for any real judgement/capture to finish.");
            var hud = session.GetComponent<GameplayHud>();
            Require(hud != null && hud.enabled && Get<Canvas>(hud, "canvas") != null, "Wait for GameplayHud.Start.");
            Directory.CreateDirectory(Output);
            var manifest = new Manifest { scene = SceneManager.GetActiveScene().path, utc = DateTime.UtcNow.ToString("O"), result = "RUNNING" };
            File.WriteAllText(Output + "/metadata.json", JsonUtility.ToJson(manifest, true));
            running = true;
            session.StartCoroutine(Guard(CaptureAll(session, hud, manifest), manifest));
        }

        private static IEnumerator Guard(IEnumerator capture, Manifest manifest)
        {
            bool background = Application.runInBackground, done = false;
            Application.runInBackground = true;
            float start = Time.realtimeSinceStartup;
            Exception failure = null;
            try
            {
                while (!done && failure == null)
                {
                    object next = null;
                    try
                    {
                        Require(Time.realtimeSinceStartup - start < 90f, "UI inventory exceeded 90 seconds.");
                        done = !capture.MoveNext();
                        if (!done) next = capture.Current;
                    }
                    catch (Exception error) { failure = error; }
                    if (!done && failure == null) yield return next;
                }
            }
            finally
            {
                try { (capture as IDisposable)?.Dispose(); }
                catch (Exception error) { if (failure == null) failure = error; }
                if (!done && failure == null) failure = new OperationCanceledException("UI inventory interrupted.");
                Application.runInBackground = background;
                running = false;
                manifest.result = failure == null ? "PASS: actual gameplay Canvas captured; simulated states are identified per entry." : "FAIL: " + failure;
                File.WriteAllText(Output + "/metadata.json", JsonUtility.ToJson(manifest, true));
                File.AppendAllText(Output + "/capture.txt", manifest.result + "\n");
                if (failure == null) Debug.Log("UI inventory captured: " + Output + "/metadata.json");
                else Debug.LogError(manifest.result);
            }
        }

        private static IEnumerator CaptureAll(GameplaySession session, GameplayHud hud, Manifest manifest)
        {
            var walker = session.walker;
            var rig = session.PhotoRig;
            var recorder = session.Recorder;
            string[] hudNames = { "<JournalOpen>k__BackingField", "selectedGlyph", "guessGlyphId", "guessDraft", "replyDraft", "lastReply",
                "page", "glyphPage", "albumPhotosSelected", "glyphDetailOpen", "dialogueOpen", "expandedPhoto", "currentFrame", "playingPhoto",
                "nextFrameAt", "echoUntil", "focusMarkUntil", "dirty", "inspectorDirty", "useAlbumDesignSamples" };
            var hudState = Snapshot(hud, hudNames);
            var sessionState = Snapshot(session, "<Notebook>k__BackingField", "<Status>k__BackingField", "<Judging>k__BackingField", "<Observed>k__BackingField", "nextObservation");
            var recorderState = Snapshot(recorder, "<IsRecording>k__BackingField", "<CaptureProgress>k__BackingField");
            var rigState = Snapshot(rig, "<FocalLength>k__BackingField", "<FocusDistance>k__BackingField", "focusedOnSurface",
                "previousYaw", "previousPitch", "previousViewEnabled", "previousMapEnabled", "previousViewAudio", "previousMapAudio");
            var walkerState = Snapshot(walker, "yaw", "pitch", "verticalSpeed", "overview", "<InputBlocked>k__BackingField",
                "<JumpBlocked>k__BackingField", "<CameraInputBlocked>k__BackingField", "<ShowLegacyHint>k__BackingField");
            bool cameraMode = hud.CameraMode, hudEnabled = hud.enabled, sessionEnabled = session.enabled, walkerEnabled = walker.enabled;
            Vector3 position = walker.transform.position, viewPosition = walker.viewCamera.transform.position;
            Quaternion rotation = walker.transform.rotation, viewRotation = walker.viewCamera.transform.rotation;
            bool viewEnabled = walker.viewCamera.enabled, mapEnabled = walker.mapCamera.enabled;
            AudioListener viewAudio = walker.viewCamera.GetComponent<AudioListener>(), mapAudio = walker.mapCamera.GetComponent<AudioListener>();
            bool viewAudioEnabled = viewAudio != null && viewAudio.enabled, mapAudioEnabled = mapAudio != null && mapAudio.enabled;
            RectTransform originalFocus = Get<RectTransform>(hud, "focusMark");
            Vector2 focusPosition = originalFocus == null ? Vector2.zero : originalFocus.anchoredPosition;
            bool focusVisible = originalFocus != null && originalFocus.gameObject.activeSelf;
            var originalNotebook = session.Notebook;
            string notebookJson = JsonUtility.ToJson(originalNotebook);
            byte[] save = File.Exists(session.SavePath) ? File.ReadAllBytes(session.SavePath) : null;
            string selectedName = EventSystem.current?.currentSelectedGameObject?.name;
            try
            {
                session.enabled = false;
                hud.enabled = false;
                walker.enabled = false;
                walker.InputBlocked = true;
                walker.ShowLegacyHint = false;
                Set(session, "nextObservation", float.PositiveInfinity);
                var notebook = JsonUtility.FromJson<LanguageNotebook>(notebookJson);
                Set(session, "<Notebook>k__BackingField", notebook);
                var records = new List<ObservationRecord>(notebook.observations);
                ObservationRecord realPhoto = ReadablePhoto(records);
                AnimalActor animal = session.animals.FirstOrDefault(a => a != null && !a.background) ?? session.animals.FirstOrDefault(a => a != null);
                Require(animal != null, "The gameplay scene contains no animal to show its authored utterance.");
                GlyphDefinition glyph = session.Catalog.glyphs[0];
                string originalStatus = session.Status;
                hud.useAlbumDesignSamples = false;
                Set(hud, "dirty", false);
                Set(hud, "inspectorDirty", false);

                Action reset = () =>
                {
                    Set(recorder, "<IsRecording>k__BackingField", false);
                    Set(recorder, "<CaptureProgress>k__BackingField", 0f);
                    hud.SetCameraMode(false);
                    Set(hud, "<JournalOpen>k__BackingField", false);
                    Set(hud, "dialogueOpen", false); Set(hud, "expandedPhoto", null); Set(hud, "glyphDetailOpen", false);
                    Set(hud, "playingPhoto", false); Set(hud, "selectedGlyph", null); Set(hud, "page", 0); Set(hud, "glyphPage", 0);
                    Set(session, "<Judging>k__BackingField", false);
                    Set(session, "<Observed>k__BackingField", null);
                    var echo = Get<RectTransform>(hud, "echoRoot"); if (echo != null) echo.gameObject.SetActive(false);
                    Call(hud, "ClearFocus"); Call(hud, "RebuildModal"); Call(hud, "UpdateAnimalBubble");
                    Status(session, hud, originalStatus);
                };
                Action observe = () =>
                {
                    Set(session, "<Observed>k__BackingField", animal);
                    walker.SetOverview(false);
                    Vector3 point = animal.transform.position + new Vector3(0f, 0f, -4f);
                    if (Terrain.activeTerrain != null) point.y = Terrain.activeTerrain.SampleHeight(point) + Terrain.activeTerrain.transform.position.y + .1f;
                    walker.Teleport(point); walker.yaw = 0f; walker.pitch = 20f;
                    Call(walker, "LateUpdate"); Call(hud, "UpdateAnimalBubble"); Call(hud, "PositionAnimalBubble");
                };
                Action album = () =>
                {
                    reset(); Set(hud, "<JournalOpen>k__BackingField", true); Set(hud, "albumPhotosSelected", true);
                    Call(hud, "RebuildModal");
                };
                Action detail = () =>
                {
                    album(); Set(hud, "selectedGlyph", glyph.id); Set(hud, "guessGlyphId", glyph.id);
                    Set(hud, "glyphDetailOpen", true); Call(hud, "RebuildModal");
                };
                Action photoDetail = () =>
                {
                    notebook.observations = new List<ObservationRecord>(records);
                    album(); Set(hud, "expandedPhoto", realPhoto); Set(hud, "currentFrame", 0); Call(hud, "RebuildModal");
                    Require(Find<RawImage>(hud, "Motion photo frame").texture != null, "Saved photo first frame failed to load.");
                };
                var shots = new List<Entry>();
                Add(shots, 1, "正常HUD与快捷键", "进入游戏", "顶部状态条、左下键位提示", "实际HUD；保留当前会话状态文案", reset);
                Add(shots, 2, "动物字语与交谈提示", "走近动物", "原始字形气泡、动物名和E交谈", "真实场景动物和字语；临时摆放玩家视角，无观察入档", () => { reset(); observe(); });
                Add(shots, 3, "中文回应空输入", "E交谈", "回应标题、输入占位、发送、关闭", "真实BuildDialogue；模拟打开状态，无发送", () =>
                { Set(hud, "replyDraft", ""); Set(hud, "dialogueOpen", true); Call(hud, "RebuildModal"); });
                Add(shots, 4, "中文回应输入草稿", "在回应框输入", "有文字的输入框和发送按钮", "实际InputField草稿；无发送、无网络", () =>
                    Find<InputField>(hud, "Chinese reply").text = "你希望雨停下来，让湿松果晒干，对吗？");
                Add(shots, 5, "发送后人物回显", "回应提交后", "人物说话气泡", "模拟已发送状态，直接调用BuildReplyEcho；未调用Reply或SubmitReply", () =>
                {
                    reset(); observe(); Set(hud, "lastReply", "你希望雨停下来，让湿松果晒干，对吗？");
                    Set(hud, "echoUntil", Time.unscaledTime + 120f); Call(hud, "BuildReplyEcho");
                });
                Add(shots, 6, "相机取景35毫米", "C举起相机", "取景框、中心点、快门、焦距与操作提示、关闭", "真实相机模式与场景渲染；无拍照", () =>
                {
                    reset(); observe(); hud.SetCameraMode(true); rig.Zoom((35f - rig.FocalLength) / 5f);
                    CaptureHint(hud, rig, recorder); Call(hud, "PositionAnimalBubble");
                });
                Add(shots, 7, "相机对焦标记", "点击取景画面", "方形对焦反馈与距离提示", "真实FocusAt射线；仅临时显示现有对焦控件", () =>
                {
                    rig.FocusAt(new Vector2(Screen.width * .5f, Screen.height * .5f));
                    var focus = Get<RectTransform>(hud, "focusMark"); focus.anchoredPosition = Vector2.zero; focus.gameObject.SetActive(true);
                    Set(hud, "focusMarkUntil", Time.unscaledTime + 120f); CaptureHint(hud, rig, recorder);
                });
                Add(shots, 8, "录制进度", "拍照录制中", "记录中60%的提示", "模拟Recorder属性驱动真实Capture hint；未开始录制、未写照片", () =>
                {
                    Set(recorder, "<IsRecording>k__BackingField", true); Set(recorder, "<CaptureProgress>k__BackingField", .6f);
                    CaptureHint(hud, rig, recorder);
                });
                Add(shots, 9, "实际相片页空状态", "相片页签且无照片", "空相框、空状态文案、上一页下一页和页码", "仅清空克隆Notebook照片列表；新书页作为未美化控件的上下文", () => { notebook.observations.Clear(); album(); });
                Add(shots, 10, "未遇见字符详情", "点击未遇见字符", "遮罩、矩形详情卡、关闭与提示文案", "仅在克隆Notebook移除此字知识，真实BuildGlyphDetail", () =>
                { notebook.words.RemoveAll(w => w.glyphId == glyph.id); detail(); });
                Add(shots, 11, "字符猜测与证据按钮", "点击已遇见未确认字符", "我的猜测、输入框、记下来、查看相关照片", "克隆Notebook临时遇见状态，输入草稿不保存", () =>
                {
                    notebook.words.Add(new GlyphKnowledge { glyphId = glyph.id, encountered = true, confirmed = false, guess = "" });
                    Set(hud, "guessDraft", "它可能在说下雨。"); detail();
                });
                Add(shots, 12, "已确认字符释义", "点击已确认字符", "已确认含义、相关照片与关闭", "仅克隆Notebook确认此字，未调用判定接口或存档", () =>
                { notebook.GetWord(glyph.id).confirmed = true; detail(); });
                Add(shots, 13, "动态照片详情暂停", "点击已保存照片", "详情底板、观察记录标题、真实首帧、字语与播放按钮", realPhoto == null ? "SKIP：无可读已有动态照片；未创建示例记录" : "真实已有照片 " + realPhoto.id, realPhoto == null ? null : photoDetail);
                Add(shots, 14, "动态照片播放中", "详情点击播放", "暂停按钮、帧号与总帧数", realPhoto == null ? "SKIP：无可读已有动态照片" : "真实已有首帧；调用实际播放按钮，无新录制", realPhoto == null ? null : (Action)(() =>
                    Find<Button>(hud, "Play motion photo").onClick.Invoke()));
                Add(shots, 15, "照片缺失错误文案", "相册索引存在但图片文件缺失", "照片暂时无法读取", "模拟缺失文件的临时内存记录；不创建或删除文件", () =>
                {
                    string id = Guid.NewGuid().ToString("N");
                    notebook.observations = new List<ObservationRecord> { new ObservationRecord { id = id, clipFolder = "MotionPhotos/" + id, frameCount = 1, framesPerSecond = 6f, glyphIds = new[] { glyph.id } } };
                    album(); Require(Find<Text>(hud, "Missing frame").text == "照片暂时无法读取", "The missing-photo UI message is absent.");
                });
                Add(shots, 16, "回应判断中状态", "动物等待AI判断", "动物在听你说……", "模拟Judging及Status，真实顶部状态控件；无AI请求", () =>
                { reset(); observe(); Set(session, "<Judging>k__BackingField", true); Status(session, hud, "动物在听你说……"); });
                Add(shots, 17, "回应失败状态", "AI请求失败或超时", "可重试错误提示", "使用LanguageJudgementClient现有错误文案模拟Status；无网络请求", () =>
                { Set(session, "<Judging>k__BackingField", false); Status(session, hud, "AI请求失败或超时，本次未确认任何词义，可以重试。"); });
                Add(shots, 18, "相关照片为空", "字符详情点击查看相关照片", "还没有这个字的照片、空相框与分页控件", "清空克隆照片列表后点击真实Word evidence按钮；未修改存档", () =>
                {
                    notebook.observations.Clear(); detail(); Find<Button>(hud, "Word evidence").onClick.Invoke();
                    Require(Find<Text>(hud, "No photos").text == "还没有这个字的照片。", "The empty glyph-evidence message is absent.");
                });
                Add(shots, 19, "字符猜测空输入", "点击已遇见未确认字符", "空猜测输入及占位文案、记下来与相关照片按钮", "仅克隆Notebook未确认状态和空草稿；无保存操作", () =>
                {
                    notebook.GetWord(glyph.id).confirmed = false; Set(hud, "guessDraft", ""); detail();
                    Require(Find<InputField>(hud, "Word guess").text == "", "The guess input is not empty.");
                });

                File.WriteAllText(Output + "/capture.txt", "Actual Play Canvas inventory. No TakePhoto, Reply, SubmitReply or SaveGuess call.\n");
                foreach (Entry shot in shots)
                {
                    manifest.items.Add(shot);
                    if (shot.prepare == null) { shot.filename = ""; continue; }
                    shot.prepare();
                    Canvas.ForceUpdateCanvases();
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    yield return new WaitForEndOfFrame();
                    Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
                    try { shot.width = image.width; shot.height = image.height; File.WriteAllBytes(Output + "/" + shot.filename, image.EncodeToPNG()); }
                    finally { Object.Destroy(image); }
                    File.WriteAllText(Output + "/metadata.json", JsonUtility.ToJson(manifest, true));
                }
            }
            finally
            {
                try
                {
                    Restore(recorder, recorderState);
                    hud.SetCameraMode(false);
                    Restore(session, sessionState);
                    walker.Teleport(position); walker.transform.rotation = rotation; Restore(walker, walkerState);
                    walker.viewCamera.transform.SetPositionAndRotation(viewPosition, viewRotation);
                    Restore(hud, hudState);
                    Set(hud, "replyInput", null); Set(hud, "guessInput", null);
                    hud.SetCameraMode(cameraMode);
                    Restore(rig, rigState);
                    rig.CaptureCamera.focalLength = rig.FocalLength;
                    var focus = Get<DepthOfField>(rig, "focusEffect");
                    focus.focusDistance.Override(rig.FocusDistance); focus.focalLength.Override(rig.FocalLength);
                    Call(hud, "RebuildInspectorPreview");
                    Restore(hud, hudState);
                    Status(session, hud, session.Status); CaptureHint(hud, rig, recorder);
                    Call(hud, "UpdatePlaybackLabel");
                    RectTransform restoredFocus = Get<RectTransform>(hud, "focusMark");
                    if (restoredFocus != null) { restoredFocus.anchoredPosition = focusPosition; restoredFocus.gameObject.SetActive(focusVisible); }
                    Call(hud, "PositionAnimalBubble");
                    walker.viewCamera.enabled = viewEnabled; walker.mapCamera.enabled = mapEnabled;
                    if (viewAudio != null) viewAudio.enabled = viewAudioEnabled;
                    if (mapAudio != null) mapAudio.enabled = mapAudioEnabled;
                    if (EventSystem.current != null)
                        EventSystem.current.SetSelectedGameObject(hud.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == selectedName)?.gameObject);
                }
                finally
                {
                    hud.enabled = hudEnabled; session.enabled = sessionEnabled; walker.enabled = walkerEnabled;
                    Restore(walker, walkerState);
                }
                bool unchanged = JsonUtility.ToJson(originalNotebook) == notebookJson &&
                    (save == null ? !File.Exists(session.SavePath) : File.Exists(session.SavePath) && save.SequenceEqual(File.ReadAllBytes(session.SavePath)));
                File.AppendAllText(Output + "/capture.txt", "RESTORED: original HUD/session/recorder/camera/player state. Notebook and save unchanged: " + unchanged + "\n");
                Require(unchanged, "The original notebook or saved file changed during UI inventory capture.");
            }
        }

        private static void Add(List<Entry> entries, int sequence, string title, string trigger, string state, string provenance, Action prepare)
        { entries.Add(new Entry { index = sequence, filename = sequence.ToString("D2") + ".png", title = title, trigger = trigger, state = state, provenance = provenance, prepare = prepare }); }
        private static void Status(GameplaySession session, GameplayHud hud, string text)
        { Set(session, "<Status>k__BackingField", text); Get<Text>(hud, "statusLabel").text = text ?? ""; }
        private static void CaptureHint(GameplayHud hud, PhotoCameraRig rig, MotionPhotoRecorder recorder)
        {
            Text label = Get<Text>(hud, "captureLabel");
            if (label != null) label.text = recorder.IsRecording ? "记录中  " + Mathf.RoundToInt(recorder.CaptureProgress * 100f) + "%" :
                rig.FocalLength.ToString("F0") + " mm  ·  " + rig.FocusLabel + "\n滚轮变焦 · 点击对焦 · 空格拍照 · ESC 返回";
        }
        private static ObservationRecord ReadablePhoto(List<ObservationRecord> records)
        {
            foreach (ObservationRecord record in records)
            {
                Texture2D frame = null;
                try { frame = MotionPhotoRecorder.LoadFrame(record, 0); if (frame != null) return record; }
                catch (Exception) { }
                finally { if (frame != null) Object.Destroy(frame); }
            }
            return null;
        }
        private static Dictionary<string, object> Snapshot(object target, params string[] names) => names.ToDictionary(name => name, name => Get<object>(target, name));
        private static void Restore(object target, Dictionary<string, object> values) { foreach (var value in values) Set(target, value.Key, value.Value); }
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
        private static void Call(object target, string name) => target.GetType().GetMethod(name, Flags | BindingFlags.Static).Invoke(target, null);
        private static T Find<T>(GameplayHud hud, string name) where T : Component { var value = hud.GetComponentsInChildren<T>().FirstOrDefault(c => c.name == name); Require(value != null, "Missing actual UI control: " + name); return value; }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
