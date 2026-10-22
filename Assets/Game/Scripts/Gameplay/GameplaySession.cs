using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace WeatherHousePrototype
{
    [DefaultExecutionOrder(-20)]
    [RequireComponent(typeof(MotionPhotoRecorder), typeof(LanguageJudgementClient), typeof(GameplayHud))]
    public sealed class GameplaySession : MonoBehaviour
    {
        public PrototypeWalker walker;
        public AnimalActor[] animals;
        public WeatherRegion[] regions;
        public LanguageNotebook Notebook { get; private set; }
        public GlyphCatalog Catalog { get; private set; }
        public AnimalActor Observed { get; private set; }
        public MotionPhotoRecorder Recorder { get; private set; }
        public PhotoCameraRig PhotoRig { get; private set; }
        public Camera CurrentView => PhotoRig != null && PhotoRig.Active ? PhotoRig.CaptureCamera : walker.viewCamera;
        public bool Judging { get; private set; }
        public string Status { get; private set; } = "走近动物，观察它的动作与字语。C 拍照留证，J 打开手账。";
        public event Action Changed;
        public string SavePath => Path.Combine(Application.persistentDataPath, "language-notebook.json");
        private LanguageJudgementClient judge;
        private GameplayHud hud;
        private float nextObservation;
        private string lastUtterance;

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
            if (walker == null) walker = FindObjectOfType<PrototypeWalker>();
            PhotoRig = GetComponent<PhotoCameraRig>();
            if (PhotoRig == null) PhotoRig = gameObject.AddComponent<PhotoCameraRig>();
            PhotoRig.Initialize(walker);
            walker.ShowLegacyHint = false;
        }

        private bool saveBlocked;
        private void OnEnable()
        {
            if (walker != null) walker.ShowLegacyHint = false;
        }
        private void Update()
        {
            walker.InputBlocked = hud.BlocksMovement;
            walker.JumpBlocked = hud.CameraMode;
            walker.CameraInputBlocked = hud.CameraMode;
            if (Time.unscaledTime < nextObservation) return;
            nextObservation = Time.unscaledTime + .2f;
            AnimalActor next = null;
            float best = 10f;
            Camera view = CurrentView;
            if (view.enabled)
                foreach (var animal in animals)
                {
                    if (animal == null) continue;
                    float distance = Vector3.Distance(walker.transform.position, animal.transform.position);
                    Vector3 sightPoint = PhotoRig.Active && PhotoCameraRig.TryGetAnimalBounds(animal, out Bounds bounds)
                        ? bounds.center : animal.BubbleAnchor;
                    Vector3 screen = view.WorldToViewportPoint(sightPoint);
                    if (distance >= best || screen.z <= 0f || screen.x < .03f || screen.x > .97f || screen.y < .04f || screen.y > .95f) continue;
                    Vector3 origin = view.transform.position;
                    if (Physics.Linecast(origin, sightPoint, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                        !hit.transform.IsChildOf(animal.transform) && !hit.transform.IsChildOf(walker.transform)) continue;
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
            if (Observed != null && lastUtterance != Observed.UtteranceId)
            {
                lastUtterance = Observed.UtteranceId;
                Notebook.Observe(Observed.animalId, Observed.UtteranceId, Observed.CurrentGlyphIds);
                Save();
                Changed?.Invoke();
            }
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
            Recorder.Capture(PhotoRig.CaptureCamera, new ObservationRecord
            {
                animalId = animal.animalId, utteranceId = animal.UtteranceId,
                glyphIds = (string[])animal.CurrentGlyphIds.Clone(),
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
                target = regions.Where(r => r != null && r.Contains(walker.transform.position))
                    .OrderBy(r => (r.transform.position - walker.transform.position).sqrMagnitude).FirstOrDefault();
            if (target == null) { Notify("走近一处动物生活区域，再试着改变天气。"); return; }
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
            if (Observed == null) { Notify("靠近动物后再回答。"); return; }
            if (Observed.background || Observed.RequestSatisfied) { Notify("它正在分享眼前的生活。换一只有请求的动物试试。"); return; }
            if (string.IsNullOrWhiteSpace(chinese)) { Notify("写下你对它请求的理解。"); return; }
            var animal = Observed;
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
            Judging = false;
        }
    }
}
