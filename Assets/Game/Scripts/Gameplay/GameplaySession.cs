using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using ForestSpriteCharacter;
using WeatherHouse.Vegetation;
using WeatherHouse.WatersideV2;

namespace WeatherHousePrototype
{
    [DefaultExecutionOrder(-20)]
    [RequireComponent(typeof(MotionPhotoRecorder), typeof(LanguageJudgementClient), typeof(GameplayHud))]
    public sealed class GameplaySession : MonoBehaviour
    {
        public PrototypeWalker walker;
        public ForestSpriteDemoController forestSprite;
        public AnimalActor[] animals;
        public WeatherRegion[] regions;
        [Min(.2f)] public float automaticGreetingDuration = 3.2f;
        [Min(0f)] public float automaticGreetingCooldown = 30f;
        public LanguageNotebook Notebook { get; private set; }
        public GlyphCatalog Catalog { get; private set; }
        public AnimalActor Observed { get; private set; }
        public MotionPhotoRecorder Recorder { get; private set; }
        public PhotoCameraRig PhotoRig { get; private set; }
        public Transform PlayerTransform => forestSprite ? forestSprite.transform : walker ? walker.transform : null;
        public Camera PlayerViewCamera => forestSprite ? forestSprite.demoCamera : walker ? walker.viewCamera : null;
        public Transform PlayerVisualRoot => forestSprite ? forestSprite.visual : walker ? walker.visualRoot : null;
        public Camera CurrentView => PhotoRig != null && PhotoRig.Active ? PhotoRig.CaptureCamera : PlayerViewCamera;
        public bool Judging { get; private set; }
        public string Status { get; private set; } = "走近动物，观察它的动作与字语。C 拍照留证，J 打开手账。";
        public event Action Changed;
        public string SavePath => Path.Combine(Application.persistentDataPath, "language-notebook.json");
        private LanguageJudgementClient judge;
        private GameplayHud hud;
        private float nextObservation;
        private string lastUtterance;
        private VegetationTopDownCamera orbit;

        private void Awake()
        {
            Recorder = GetComponent<MotionPhotoRecorder>();
            judge = GetComponent<LanguageJudgementClient>();
            hud = GetComponent<GameplayHud>();
            Catalog = JsonUtility.FromJson<GlyphCatalog>(Resources.Load<TextAsset>("WeatherHouseGlyphs").text);
            Notebook = new LanguageNotebook();
            try
            {
                if (File.Exists(SavePath)) Notebook = JsonUtility.FromJson<LanguageNotebook>(File.ReadAllText(SavePath)) ?? new LanguageNotebook();
            }
            catch (Exception error) when (error is IOException || error is ArgumentException || error is UnauthorizedAccessException)
            {
                Status = "旧手账暂时无法读取，原文件已保留。";
                // Keep an unreadable save intact while the player uses a new session.
                saveBlocked = true;
            }
            if (!walker && !forestSprite) forestSprite = FindObjectOfType<ForestSpriteDemoController>();
            if (!walker && !forestSprite) walker = FindObjectOfType<PrototypeWalker>();
            if (!PlayerTransform || !PlayerViewCamera) throw new InvalidOperationException("GameplaySession needs the scene's player and view camera.");
            if (forestSprite)
            {
                forestSprite.GameplayInputManaged = true;
                orbit = PlayerViewCamera.GetComponent<VegetationTopDownCamera>();
            }
            PhotoRig = GetComponent<PhotoCameraRig>();
            if (PhotoRig == null) PhotoRig = gameObject.AddComponent<PhotoCameraRig>();
            if (forestSprite) PhotoRig.Initialize(forestSprite); else PhotoRig.Initialize(walker);
            if (walker) walker.ShowLegacyHint = false;
        }

        private bool saveBlocked;
        private void OnEnable()
        {
            if (walker != null) walker.ShowLegacyHint = false;
            if (forestSprite) forestSprite.GameplayInputManaged = true;
        }
        private void Update()
        {
            RefreshInputBlocking();
            if (Time.unscaledTime < nextObservation) return;
            nextObservation = Time.unscaledTime + .2f;
            AnimalActor next = null;
            float best = 10f;
            Camera view = CurrentView;
            AnimalActor dialogueTarget = hud && hud.isActiveAndEnabled ? hud.DialogueTarget : null;
            if (hud && hud.isActiveAndEnabled && hud.IsDialogueOpen)
            {
                if (dialogueTarget && dialogueTarget.IsAvailableForObservation && Vector3.Distance(PlayerTransform.position, dialogueTarget.transform.position) < 12f) next = dialogueTarget;
                else hud.CloseDialogue();
            }
            if (next == null && view && view.enabled)
                foreach (var animal in animals ?? Array.Empty<AnimalActor>())
                {
                    if (animal == null || !animal.IsAvailableForObservation) continue;
                    float distance = Vector3.Distance(PlayerTransform.position, animal.transform.position);
                    Vector3 sightPoint = PhotoRig.Active && PhotoCameraRig.TryGetAnimalBounds(animal, out Bounds bounds)
                        ? bounds.center : animal.BubbleAnchor;
                    Vector3 screen = view.WorldToViewportPoint(sightPoint);
                    if (distance >= best || screen.z <= 0f || screen.x < .03f || screen.x > .97f || screen.y < .04f || screen.y > .95f) continue;
                    Vector3 origin = view.transform.position;
                    if (Physics.Linecast(origin, sightPoint, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                        !hit.transform.IsChildOf(animal.transform) && !hit.transform.IsChildOf(PlayerTransform)) continue;
                    next = animal;
                    best = distance;
                }
            if (Observed != next)
            {
                if (Observed != null) Observed.SetObserved(false);
                Observed = next;
                if (Observed != null) Observed.SetObserved(true);
                lastUtterance = null;
                Changed?.Invoke();
            }
            if (Observed && hud && hud.isActiveAndEnabled && !hud.JournalOpen && !hud.IsDialogueOpen)
                Observed.TryBeginAutomaticGreeting(Time.unscaledTime, automaticGreetingDuration, automaticGreetingCooldown);
            if (Observed != null && hud && hud.IsAnimalSpeechVisible(Observed) && lastUtterance != Observed.UtteranceId)
            {
                lastUtterance = Observed.UtteranceId;
                Notebook.Observe(Observed.animalId, Observed.UtteranceId, Observed.CurrentGlyphIds);
                Save();
                Changed?.Invoke();
            }
        }

        public bool ShouldShowAnimalUtterance(AnimalActor animal) => animal && animal.IsAvailableForObservation &&
            (!animal.UsesAutomaticGreeting || animal.HasAmbientSpeech || animal.HasAutomaticGreetingAt(Time.unscaledTime) ||
             (hud && hud.isActiveAndEnabled && hud.DialogueTarget == animal));

        public void RefreshInputBlocking()
        {
            bool movement = isActiveAndEnabled && hud && hud.isActiveAndEnabled && hud.BlocksMovement;
            bool photo = isActiveAndEnabled && hud && hud.isActiveAndEnabled && hud.CameraMode;
            if (walker) { walker.InputBlocked = movement; walker.JumpBlocked = photo; walker.CameraInputBlocked = photo; }
            if (forestSprite)
            {
                forestSprite.InputBlocked = movement; forestSprite.JumpBlocked = photo;
                forestSprite.CameraInputBlocked = movement || photo;
            }
            if (orbit) orbit.InputBlocked = movement || photo;
        }

        public void SaveGuess(string id, string guess)
        {
            Notebook.SetGuess(id, guess);
            bool saved = Save();
            Notify(saved ? "已记下你的猜测。可以继续寻找别处的线索。" : "猜测保留在本次游戏中，手账暂时无法写入磁盘。");
        }

        public void TakePhoto()
        {
            if (Recorder.IsRecording) return;
            if (!PhotoRig.Active) { Notify("按 C 举起相机，再取景拍照。"); return; }
            if (Observed == null) { Notify("先把附近的一只动物和它的字语放进取景框。"); return; }
            var animal = Observed;
            bool speechVisible = hud && hud.IsAnimalSpeechVisible(animal);
            Recorder.Capture(PhotoRig.CaptureCamera, new ObservationRecord
            {
                animalId = animal.animalId, utteranceId = speechVisible ? animal.UtteranceId : "",
                glyphIds = speechVisible ? (string[])animal.CurrentGlyphIds.Clone() : Array.Empty<string>(),
                weather = animal.region == null ? "" : animal.region.Current.ToString()
            }, record =>
            {
                Notebook.AddObservation(record);
                bool saved = Save();
                Notify(saved ? "动态照片已放进手账。点开照片后可播放。" : "照片已保存，但手账索引写入失败，本次游戏仍可查看。");
            }, error => Notify("拍照未完成：" + error));
        }

        public void Cast(LocalWeather weather)
        {
            WeatherRegion target = Observed != null ? Observed.region : null;
            if (target == null)
                target = (regions ?? Array.Empty<WeatherRegion>()).Where(r => r != null && r.Contains(PlayerTransform.position))
                    .OrderBy(r => (r.transform.position - PlayerTransform.position).sqrMagnitude).FirstOrDefault();
            if (target == null)
            {
                Notify(FindObjectOfType<WatersideMood>() ? "这张地图使用 F8 切换白天、晨雾、黄昏、阴天与夜晚氛围。" : "走近一处动物生活区域，再试着改变天气。");
                return;
            }
            target.SetWeather(weather);
            if (animals.Where(a => !a.background).All(a => a.RequestSatisfied))
            {
                Notify("四处生活的小请求都得到回应了。你可以继续探索森林，把新发现留进手账。");
                return;
            }
            Notify(weather == LocalWeather.Rain ? "雨落在这片地方。再看看动物的动作。" :
                weather == LocalWeather.Sun ? "雨云散去。湿润的东西会慢慢晒干。" : "风吹过这片地方，叶子开始摇动。");
        }

        public void Reply(string chinese)
        {
            if (Judging) return;
            var animal = hud && hud.DialogueTarget ? hud.DialogueTarget : Observed;
            if (!animal || !animal.IsAvailableForObservation || Vector3.Distance(PlayerTransform.position, animal.transform.position) >= 12f)
            { if (hud) hud.CloseDialogue(); Notify("它已经走远了，靠近后再继续交谈。"); return; }
            if (string.IsNullOrWhiteSpace(chinese)) { Notify("写下你想对它说的话。"); return; }
            if (animal.background) { Notify("你的话已经说出。它正在分享眼前的生活，可以继续观察它的动作和字语。"); return; }
            if (animal.RequestSatisfied) { Notify("它的请求已经得到回应。可以继续观察它的生活，把新发现留进手账。"); return; }
            var ids = (string[])animal.CurrentGlyphIds.Clone();
            string context = animal.BehaviourDescription + "。当前天气：" + animal.region.Current + "。本句字义边界：" +
                string.Join("；", Catalog.glyphs.Where(g => Array.IndexOf(ids, g.id) >= 0).Select(g => g.id + "=" + g.meaning + " " + g.boundary));
            StartCoroutine(JudgeReply(new JudgementInput
            {
                animalId = animal.animalId, utteranceId = animal.UtteranceId,
                requestMeaning = animal.requestMeaning, glyphIds = ids, playerReply = chinese, context = context
            }));
        }

        private IEnumerator JudgeReply(JudgementInput input)
        {
            Judging = true;
            Notify("动物在听你说……");
            yield return judge.Judge(input, result =>
            {
                var originalAnimal = Array.Find(animals, a => a != null && a.animalId == input.animalId);
                if (originalAnimal == null || originalAnimal.UtteranceId != input.utteranceId)
                {
                    Status = "动物眼前的情况已经变化。这次判断未确认词义，等它提出新请求再试试。";
                    return;
                }
                int count = 0;
                if (result.success && result.understood)
                    foreach (string id in result.evidenceGlyphIds)
                        if (Notebook.ConfirmFromJudgement(id, input.animalId, input.glyphIds, true)) count++;
                bool saved = count == 0 || Save();
                Status = result.success ? result.feedback + (count > 0 ? " 已确认 " + count + " 个字，可在手账中查看。" : "") : result.error;
                if (!saved) Status += " 这些确认只保留在本次游戏中，尚未写入磁盘。";
            });
            Judging = false;
            Changed?.Invoke();
        }

        private bool Save()
        {
            if (saveBlocked) return false;
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                string temp = SavePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(Notebook, true));
                if (File.Exists(SavePath)) File.Replace(temp, SavePath, null);
                else File.Move(temp, SavePath);
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { Status = "手账保留在本次游戏中，但写入磁盘失败，请检查目录权限。"; return false; }
        }

        private void Notify(string message) { Status = message; Changed?.Invoke(); }
        private void OnDisable()
        {
            if (Judging) StopAllCoroutines();
            if (PhotoRig != null) PhotoRig.SetActive(false);
            if (walker != null) { walker.InputBlocked = false; walker.JumpBlocked = false; walker.CameraInputBlocked = false; walker.ShowLegacyHint = true; }
            if (forestSprite) { forestSprite.InputBlocked = false; forestSprite.JumpBlocked = false; forestSprite.CameraInputBlocked = false; forestSprite.GameplayInputManaged = false; }
            if (orbit) orbit.InputBlocked = false;
            Judging = false;
        }
    }
}
