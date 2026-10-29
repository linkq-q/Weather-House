using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    [DefaultExecutionOrder(250)]
    public sealed class LifeOnboarding : MonoBehaviour
    {
        public const string ReviewOptIn = "WEATHERHOUSE_ONBOARDING_REVIEW";
        static bool bootCaptured, priorNotebook, priorLife, reviewSuppressed;
        static string bootDirectory;
        public LifeOnboardingState State { get; private set; }
        public bool TutorialActive => State != null && State.started && State.stage != LifeOnboardingStage.Complete && !reviewSuppressed;
        public bool SocialGreetingsEnabled => State != null && State.started && !reviewSuppressed;
        public bool IsOpeningPresentationActive => TutorialActive && State.stage == LifeOnboardingStage.Opening && openingActive;
        public bool IsBlockingInput => IsOpeningPresentationActive;
        public string SaveError { get; private set; }
        public string StatePath => Path.Combine(GameplaySession.PersistenceDirectory, "onboarding.json");
        public string CurrentHint { get; private set; }
        LifeSession life; GameplaySession game; GameplayHud hud; LifeOnboardingView view;
        LifeOpeningCinematic opening; LifeGlyphGuessJudge guessJudge;
        bool ready, wasBookOpen, readOnly, judging, openingActive;
        int revision; string selectedWord, feedback = "", selectedBookFocus;
        float feedbackUntil, nextConfirmationSave;
        sealed class Encounter
        {
            public AnimalActor actor; public LifeOnboardingSpeech speech;
            public bool inside, published; public float enteredAt = -1, nextHello;
        }
        readonly List<Encounter> encounters = new List<Encounter>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetBootstrap() { bootCaptured = false; priorNotebook = priorLife = reviewSuppressed = false; bootDirectory = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void CaptureBeforeFirstSave()
        {
            bootDirectory = GameplaySession.PersistenceDirectory;
            bool review = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE"));
            bool opted = Environment.GetEnvironmentVariable(ReviewOptIn) == "1";
            string reviewRoot = Path.GetFullPath("LifeGameplayReview") + Path.DirectorySeparatorChar;
            reviewSuppressed = review && (!opted || !Path.GetFullPath(bootDirectory).StartsWith(reviewRoot, StringComparison.OrdinalIgnoreCase));
            priorNotebook = File.Exists(Path.Combine(bootDirectory, "language-notebook.json"));
            priorLife = File.Exists(Path.Combine(bootDirectory, "life-progress.json")); bootCaptured = true;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= Loaded; SceneManager.sceneLoaded += Loaded; Attach(SceneManager.GetActiveScene());
        }
        static void Loaded(Scene scene, LoadSceneMode mode) => Attach(scene);
        static void Attach(Scene scene)
        {
            if (scene.name != "WeatherHouse_WatersideV2") return;
            var session = FindObjectOfType<GameplaySession>();
            if (session && !session.GetComponent<LifeOnboarding>()) session.gameObject.AddComponent<LifeOnboarding>();
        }
        IEnumerator Start()
        {
            game = GetComponent<GameplaySession>(); hud = GetComponent<GameplayHud>();
            while (game && (!(life = GetComponent<LifeSession>()) || !life.World || !hud || !hud.OnboardingReady)) yield return null;
            if (!game || !life || reviewSuppressed) yield break;
            if (!bootCaptured || !string.Equals(Path.GetFullPath(bootDirectory), Path.GetFullPath(GameplaySession.PersistenceDirectory), StringComparison.OrdinalIgnoreCase)) yield break;
            if (File.Exists(StatePath))
            {
                try
                { State = JsonUtility.FromJson<LifeOnboardingState>(File.ReadAllText(StatePath));
                  if (State == null || State.version != 1 || !Enum.IsDefined(typeof(LifeOnboardingStage), State.stage)) { readOnly = true; State = null; yield break; }
                  State.Normalize(); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
                { SaveError = "教程记录暂时无法读取，原记录已保留。"; readOnly = true; yield break; }
            }
            else if (LifeOnboardingState.ShouldStart(priorNotebook, priorLife,
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")),
                Environment.GetEnvironmentVariable(ReviewOptIn) == "1", GameplaySession.PersistenceDirectory))
            { State = new LifeOnboardingState { started = true, stage = LifeOnboardingStage.Opening }; Persist(); }
            if (State == null || !State.started) yield break;
            guessJudge = new LifeGlyphGuessJudge(GetComponent<LanguageJudgementClient>(), game.Catalog);
            view = new GameObject("Grandmother onboarding note", typeof(RectTransform)).AddComponent<LifeOnboardingView>(); view.Build(transform);
            ready = true;
            // HUD/gesture canvases and the formal orbit camera finish their initial LateUpdate first.
            yield return null; yield return new WaitForEndOfFrame();
            if (State.stage == LifeOnboardingStage.Opening)
            {
                openingActive = true;
                opening = GetComponent<LifeOpeningCinematic>() ?? gameObject.AddComponent<LifeOpeningCinematic>();
                if (!opening.Begin(game, () => SetStage(LifeOnboardingStage.FirstBook)))
                { feedback = "奶奶的书还在，按 J 看看她留下的批注。"; SetStage(LifeOnboardingStage.FirstBook); }
            }
        }
        void Update()
        {
            if (!ready || State == null) return;
            if (Time.unscaledTime >= nextConfirmationSave && game.Notebook.words.Any(word => word != null && word.confirmed && word.glyphId != "want" && word.glyphId != "reject" && !State.earnedWords.Contains(word.glyphId)))
            {
                nextConfirmationSave = Time.unscaledTime + 1;
                if (game.SaveNotebook() && State.TrackConfirmed(game.Notebook)) Persist();
            }
            // The owner must also recover if an active movie is cancelled outside its Skip callback.
            if (State.stage == LifeOnboardingStage.Opening && openingActive && opening && !opening.IsPlaying)
            { openingActive = false; SetStage(LifeOnboardingStage.FirstBook); }
            if (IsOpeningPresentationActive)
            { if (Input.GetKeyDown(KeyCode.Escape)) opening?.Skip(); RenderNote("", null, null); return; }
            if (!TutorialActive) { hud.OnboardingHighlight(); if (SocialGreetingsEnabled) TickEncounters(); view?.Show("", null, null, true); return; }
            bool closed = !hud.JournalOpen && wasBookOpen;
            bool opened = hud.JournalOpen && !wasBookOpen; wasBookOpen = hud.JournalOpen;
            if (closed && State.stage == LifeOnboardingStage.FirstBook && State.firstBookPage >= 2)
            { State.firstBookPage = 3; SetStage(LifeOnboardingStage.ObserveAnimals); }
            if (opened) BookOpened();
            TickEncounters();
            string word = hud.OnboardingSelectedWord;
            if (word != selectedWord)
            {
                selectedWord = word; revision++;
            }
            switch (State.stage)
            {
                case LifeOnboardingStage.ObserveAnimals:
                    if (State.animalsSeen.Count >= 3 && State.helloSeen && State.goodbyeSeen) SetStage(LifeOnboardingStage.GuessBook);
                    break;
                case LifeOnboardingStage.GuessBook:
                    if (Known("hello") && Known("goodbye") && game.SaveNotebook()) { SetStage(LifeOnboardingStage.TakeSpeechPhoto); break; }
                    string focus = Known("hello") ? "goodbye" : "hello";
                    if (hud.JournalOpen && !hud.IsAlbumOpening && !hud.OnboardingDetailOpen && selectedBookFocus != focus)
                    { selectedBookFocus = focus; hud.OnboardingSelectBook(0, focus); }
                    break;
                case LifeOnboardingStage.TakeSpeechPhoto:
                    if (game.Notebook.observations.Any(p => p != null && p.photoSnapshot != null && (p.glyphIds?.Length ?? 0) > 0 && p.frameCount > 0)) SetStage(LifeOnboardingStage.PhotoBook);
                    break;
                case LifeOnboardingStage.FindRainRequest:
                    var dog = life.World.FindSpeaker("dog");
                    if (dog && LifeSession.InsideCircle(game.PlayerTransform.position, dog.transform.position, LifeSession.TalkRadius))
                    {
                        SilenceAll();
                        if (!life.Progress.HasHeard("dog.hello"))
                        { int greeting = Array.FindIndex(dog.requests, r => r.id == "dog.hello"); if (greeting >= 0) dog.topicIndex = greeting; break; }
                        SelectRainRequest(dog);
                        if (game.Observed == dog.Actor && hud.IsAnimalSpeechVisible(dog.Actor) && Array.IndexOf(dog.Actor.CurrentGlyphIds, "rain") >= 0)
                            SetStage(LifeOnboardingStage.WeatherBook);
                    }
                    break;
                case LifeOnboardingStage.WeatherBook:
                    if (State.booksIntroduced.Contains("weather") && !hud.JournalOpen) SetStage(LifeOnboardingStage.UnderstandRain);
                    break;
                case LifeOnboardingStage.UnderstandRain:
                    var speaker = life.World.FindSpeaker("dog"); if (speaker) SelectRainRequest(speaker);
                    if (life.Progress.FindTask("dog.rain.lesson") != null) SetStage(LifeOnboardingStage.CastRain);
                    break;
                case LifeOnboardingStage.CastRain:
                    if (State.rainCast && life.Progress.FindTask("dog.rain.lesson")?.status == LifeTaskStatus.Complete) SetStage(LifeOnboardingStage.ClearWeather);
                    break;
                case LifeOnboardingStage.ClearWeather:
                    if (State.clearCast && !life.LocalRainActive) SetStage(LifeOnboardingStage.ReturnVisit);
                    break;
                case LifeOnboardingStage.ReturnVisit:
                    var friend = life.World.FindSpeaker("dog");
                    if (friend)
                    {
                        float distance = LifeWorld.Flat(game.PlayerTransform.position, friend.transform.position);
                        if (!State.returnAway && distance > 5.2f) { State.returnAway = true; Persist(); }
                        var speech = LifeOnboardingSpeech.Current(friend.Actor);
                        if (State.returnAway && distance < 3.1f && speech && speech.Request.glyphIds.Contains("hello") && hud.IsAnimalSpeechVisible(friend.Actor))
                        { SetStage(LifeOnboardingStage.Complete); game.Notify("这次相遇已经记下。以后可以自由拍照、照料，也可以随时再来。"); }
                    }
                    break;
            }
            ShowStageNote();
        }
        void BookOpened()
        {
            string id = State.stage == LifeOnboardingStage.FirstBook ? "first" : State.stage == LifeOnboardingStage.GuessBook ? "guess" :
                State.stage == LifeOnboardingStage.PhotoBook ? "photos" : State.stage == LifeOnboardingStage.WeatherBook ? "weather" : null;
            if (id == null) return;
            if (!State.booksIntroduced.Contains(id)) { State.booksIntroduced.Add(id); Persist(); }
            selectedBookFocus = null;
            int tab = id == "photos" ? 1 : id == "weather" ? 2 : 0;
            StartCoroutine(SelectBookWhenReady(tab, id == "guess" ? (Known("hello") ? "goodbye" : "hello") : null));
        }
        IEnumerator SelectBookWhenReady(int tab, string word)
        {
            int submitted = revision;
            while (hud && hud.JournalOpen && hud.IsAlbumOpening) yield return null;
            if (hud && hud.JournalOpen && submitted == revision) { hud.OnboardingSelectBook(tab, word); selectedBookFocus = word; }
        }
        void ShowStageNote()
        {
            if (Time.unscaledTime < feedbackUntil) { RenderNote(feedback, State.teachingHintAvailable && !State.teachingHintRead ? "看线索" : null, () => ReadHint("hello")); return; }
            string text = "", button = null; Action action = null;
            switch (State.stage)
            {
                case LifeOnboardingStage.FirstBook:
                    if (!hud.JournalOpen) text = "按 J，看看奶奶留下的手账。";
                    else
                    {
                        text = State.firstBookPage == 0 ? (hud.HasPhotoTaskBook ? "上面四个书签：动物字、照片、天气、摄影挑战。" : "上面三个书签：动物字、照片、天气。") :
                            State.firstBookPage == 1 ? "这两个字是“想要”和“不想要”。其它字，留给你来猜。" :
                            "黄色表示已经懂了。按 J 合上手账，去找朋友吧。";
                        button = State.firstBookPage < 2 ? "继续" : "合上手账";
                        action = () => { State.firstBookPage++; Persist(); if (State.firstBookPage >= 3) { SetStage(LifeOnboardingStage.ObserveAnimals); if (hud.JournalOpen) hud.ToggleJournal(); } else hud.OnboardingSelectBook(0, null); };
                    }
                    break;
                case LifeOnboardingStage.ObserveAnimals:
                    text = hud.JournalOpen ? "按 J 合上书，去找朋友吧。" :
                        !State.helloSeen ? "靠近一位朋友，看看它说的字。" : !State.goodbyeSeen ? "走开一点，再转身看看它说什么。" :
                        "再认识几位朋友，比较它们说的字。已观察 " + Mathf.Min(3, State.animalsSeen.Count) + "/3。";
                    break;
                case LifeOnboardingStage.GuessBook:
                    text = !hud.JournalOpen ? "按 J，把刚才听见的字找出来。" : judging ? "正在核对你的猜法……" :
                        hud.OnboardingSelectedWord != null ? "写下你的猜法，点“提交猜测”。" : "点圈出的字，猜猜它是什么意思。";
                    break;
                case LifeOnboardingStage.TakeSpeechPhoto:
                    text = hud.JournalOpen ? "按 J 合上书，再试试拍照。" : "按 C，对准正在说话的动物。"; break;
                case LifeOnboardingStage.PhotoBook:
                    text = !hud.JournalOpen ? "按 J，看看刚拍下的照片。" : State.photoBookPage > 0 && hud.HasPhotoTaskBook ? "奶奶的摄影挑战用动物语写成。先找找你认识的字。" :
                        State.evidenceViewed ? (hud.HasPhotoTaskBook ? "同一个字的照片都在这里。再看看奶奶的摄影挑战吧。" : "同一个字的照片都在这里。") :
                        hud.OnboardingSelectedWord != null ? "点“查看相关照片”，找找这个字还出现在哪里。" :
                        hud.OnboardingDetailOpen ? "点照片下面的动物字。" : "点金圈里的照片，看看刚才留下了什么。";
                    if (hud.JournalOpen && (State.evidenceViewed || State.photoBookPage > 0))
                    {
                        button = hud.HasPhotoTaskBook && State.photoBookPage == 0 ? "看挑战" : "去找小狗";
                        action = () => { if (hud.HasPhotoTaskBook && State.photoBookPage++ == 0) { hud.OnboardingSelectBook(3, null); Persist(); } else SetStage(LifeOnboardingStage.FindRainRequest); };
                    }
                    break;
                case LifeOnboardingStage.ReturnVisit: text = State.returnAway ? "再去看看小狗，它还会怎样招呼你？" : "走开一点，听听它的道别。然后再靠近看看。"; break;
            }
            RenderNote(text, button, action);
        }
        void UpdateBookFocus()
        {
            if (!hud.JournalOpen || hud.IsAlbumOpening || !TutorialActive) { hud.OnboardingHighlight(); return; }
            switch (State.stage)
            {
                case LifeOnboardingStage.FirstBook:
                    if (State.firstBookPage == 0) hud.OnboardingHighlight("Symbols tab", "Photos tab", "Gesture learning tab", "Photography tasks tab");
                    else if (State.firstBookPage == 1) hud.OnboardingHighlight("Word want", "Word reject");
                    else hud.OnboardingHighlight("Close album");
                    break;
                case LifeOnboardingStage.GuessBook:
                    if (hud.OnboardingSelectedWord != null) hud.OnboardingHighlight();
                    else hud.OnboardingHighlight("Word " + (Known("hello") ? "goodbye" : "hello"));
                    break;
                case LifeOnboardingStage.PhotoBook:
                    if (State.photoBookPage > 0 && hud.HasPhotoTaskBook) hud.OnboardingHighlight("Photography tasks tab");
                    else if (State.evidenceViewed) hud.OnboardingHighlight();
                    else if (hud.OnboardingSelectedWord != null) hud.OnboardingHighlight("Word evidence");
                    else if (hud.OnboardingDetailOpen) hud.OnboardingHighlight("First photo word");
                    else hud.OnboardingHighlight("First photo");
                    break;
                case LifeOnboardingStage.WeatherBook:
                    hud.OnboardingHighlight("Close album");
                    break;
                default: hud.OnboardingHighlight(); break;
            }
        }
        void RenderNote(string text, string button, Action action)
        { UpdateBookFocus(); CurrentHint = text; view?.Show(text, button, action, IsOpeningPresentationActive || hud.CameraMode || life.GestureInputActive || hud.IsDialogueOpen || (hud.JournalOpen && hud.IsAlbumOpening)); }
        void Feedback(string text, float seconds = 7) { feedback = text; feedbackUntil = Time.unscaledTime + seconds; game.Notify(text); }
        public void ContinueNote()
        {
            if (State.stage == LifeOnboardingStage.FirstBook && hud.JournalOpen) { State.firstBookPage++; if (State.firstBookPage >= 3) SetStage(LifeOnboardingStage.ObserveAnimals); else Persist(); }
        }
        void SetStage(LifeOnboardingStage value)
        { if (State == null || State.stage == value) return; revision++; hud?.OnboardingHighlight(); State.stage = value; selectedBookFocus = null; feedbackUntil = 0; Persist(); }
        bool Known(string id) => game.Notebook.GetWord(id)?.confirmed == true;
        void Persist()
        {
            if (State == null || readOnly || reviewSuppressed) return;
            try { Directory.CreateDirectory(GameplaySession.PersistenceDirectory); LifeSaveFile.Write(StatePath, JsonUtility.ToJson(State, true)); SaveError = null; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { SaveError = "教程进度暂未写入磁盘，本次仍可继续。"; }
        }
        void TickEncounters()
        {
            bool social = State.stage == LifeOnboardingStage.ObserveAnimals || State.stage == LifeOnboardingStage.GuessBook || State.stage == LifeOnboardingStage.TakeSpeechPhoto || State.stage == LifeOnboardingStage.PhotoBook || State.stage == LifeOnboardingStage.ReturnVisit || State.stage == LifeOnboardingStage.Complete;
            if (!social) return;
            foreach (var actor in game.animals ?? Array.Empty<AnimalActor>())
            {
                if (!actor || !actor.IsAvailableForObservation) continue;
                var entry = encounters.FirstOrDefault(e => e.actor == actor);
                if (entry == null)
                {
                    var speech = actor.GetComponent<LifeOnboardingSpeech>() ?? actor.gameObject.AddComponent<LifeOnboardingSpeech>(); speech.owner = this;
                    entry = new Encounter { actor = actor, speech = speech }; encounters.Add(entry);
                }
                float distance = LifeWorld.Flat(game.PlayerTransform.position, actor.transform.position);
                if (!hud.JournalOpen && !hud.IsDialogueOpen)
                {
                    if (!entry.inside && distance < 3f)
                    {
                        if (entry.enteredAt < 0) entry.enteredAt = Time.unscaledTime;
                        if (Time.unscaledTime - entry.enteredAt > .5f && Time.unscaledTime >= entry.nextHello && !actor.HasAmbientPresentation)
                        { entry.inside = true; entry.published = false; entry.speech.Speak("hello", 9); }
                    }
                    else if (distance > 3.4f) entry.enteredAt = -1;
                    if (entry.inside && distance > 4.8f)
                    { entry.inside = false; entry.enteredAt = -1; entry.nextHello = Time.unscaledTime + (State.stage == LifeOnboardingStage.ReturnVisit ? 1 : 8); entry.published = false; entry.speech.Speak("goodbye", 9); }
                }
                if (entry.speech.Active && hud.IsAnimalSpeechVisible(actor))
                {
                    if (State.Observe(actor.animalId, entry.speech.Request.glyphIds)) Persist();
                    if (!entry.published) { entry.published = true; life.World.ObserveOnboardingUtterance(actor, entry.speech); }
                }
            }
        }
        void SilenceAll() { foreach (var entry in encounters) if (entry.speech) entry.speech.Silence(); }
        static void SelectRainRequest(LifeSpeaker speaker)
        { int index = Array.FindIndex(speaker.requests, r => r.id == "dog.rain.lesson"); if (index >= 0) speaker.topicIndex = index; }
        public static LifeOnboarding For(GameplaySession session) => session ? session.GetComponent<LifeOnboarding>() : null;
        public static bool Blocks(GameplaySession session) => For(session)?.IsBlockingInput == true;
        public static bool TrySubmitGuess(GameplaySession session, string id, string guess)
        {
            if (!session || session.Notebook == null) return false;
            var tutorial = For(session);
            if (!tutorial) tutorial = session.gameObject.AddComponent<LifeOnboarding>();
            tutorial.game = session; tutorial.hud = session.GetComponent<GameplayHud>();
            if (!tutorial.hud) return false;
            // Dictionary judgement also serves saves that never started the opening tutorial.
            if (tutorial.guessJudge == null) tutorial.guessJudge = new LifeGlyphGuessJudge(session.GetComponent<LanguageJudgementClient>(), session.Catalog);
            tutorial.SubmitGuess(id, guess); return true;
        }
        void SubmitGuess(string id, string guess)
        {
            var word = game.Notebook.GetWord(id);
            if (word == null || !word.encountered || word.confirmed) return;
            if (judging || string.IsNullOrWhiteSpace(guess)) return;
            game.Notebook.SetGuess(id, guess);
            if (!game.SaveNotebook()) Feedback("猜测保留在本次游戏中，手账暂时无法写入磁盘。", 8);
            int submitted = ++revision; judging = true;
            StartCoroutine(JudgeGuess(id, guess, submitted));
        }
        IEnumerator JudgeGuess(string id, string guess, int submitted)
        {
            LifeGlyphGuessResult result = null;
            yield return guessJudge.Judge(id, guess, value => result = value, () => submitted != revision || !hud.JournalOpen || hud.OnboardingSelectedWord != id);
            judging = false;
            if (submitted != revision || !hud.JournalOpen || hud.OnboardingSelectedWord != id || result == null) yield break;
            if (Known(id)) yield break;
            if (result.Status == LifeGlyphGuessStatus.Accepted)
            {
                game.Notebook.GetWord(id).confirmed = true;
                bool saved = game.SaveNotebook();
                if (saved && State != null) { State.TrackConfirmed(game.Notebook); Persist(); }
                LifeAudio.Play(LifeAudioCue.Progress);
                Feedback(saved ? "猜对了。" : "猜对了。手账暂时无法写入磁盘，本次游戏仍保留结果。", 7);
            }
            else if (result.Status == LifeGlyphGuessStatus.Rejected)
            { if (State != null && id == "hello" && !State.teachingHintRead) { State.teachingHintAvailable = true; Persist(); } }
            else Feedback("语义判断暂不可用。猜测已保留，可以稍后重试。", 8);
        }
        void ReadHint(string id)
        {
            if (!State.TryReadHint(id, id == "hello")) { Feedback("线索用完了。每认出三个新字，会多一次机会。", 7); return; }
            Persist(); Feedback(Hint(id), 14);
        }
        static string Hint(string id)
        {
            switch (id)
            {
                case "hello": return "朋友刚见面时会说什么？";
                case "goodbye": return "准备分开时，走开的人和留下的人都可能说。";
                case "rain": return "看向天空，什么正从云里落到碗中？";
                case "water": return "天已经放晴，碗里留下了什么？";
                case "wet": return "比较淋过雨的草和晒过太阳的草，表面有什么不同？";
                case "dry": return "雨停后再晒一会儿，草垫发生了什么变化？";
                case "grass": return "看看羊吃的、松鼠铺垫用的，是哪种植物？";
                case "tree": return "它指向的是有树干和枝叶的东西，还是住处本身？";
                case "home": return "有的在树旁，有的在地上；离开后，它们还会去那里休息。";
                default: return "再看含这个字的几张照片，找出每次都相同的行为、对象或关系。";
            }
        }
        public static void DecorateWord(GameplaySession game, RectTransform card, Font font, string id, bool confirmed)
        {
            var tutorial = For(game); if (!tutorial || tutorial.State == null || !tutorial.State.started || reviewSuppressed || confirmed) return;
            var panel = ProceduralUiLibrary.WhitePanel(card, "Word hint", new Vector2(143, -64), new Vector2(48, 32), true);
            var button = panel.gameObject.AddComponent<Button>(); button.targetGraphic = panel;
            string hintLabel = tutorial.State.hintsRead.Contains(id) ? "↺" : id == "hello" && tutorial.State.teachingHintAvailable && !tutorial.State.teachingHintRead ? "?" : "?" + tutorial.State.HintsAvailable;
            LifeUi.Text(panel.transform, "Hint icon", hintLabel, Vector2.zero, new Vector2(44, 28), font, 23, LifeUi.Ink, TextAnchor.MiddleCenter);
            button.onClick.AddListener(() => tutorial.ReadHint(id));
        }
        public static void EvidenceOpened(GameplaySession game, string id)
        {
            var tutorial = For(game); if (!tutorial || tutorial.State == null || game.Notebook.FindEvidence(id).Count == 0) return;
            tutorial.State.evidenceViewed = true; tutorial.Persist();
        }
        public static void SuccessfulCast(LifeSession life, string id)
        {
            var tutorial = life ? life.GetComponent<LifeOnboarding>() : null; if (!tutorial || !tutorial.TutorialActive) return;
            if (id == "rain" && tutorial.State.stage == LifeOnboardingStage.CastRain) tutorial.State.rainCast = true;
            if (id == "clear" && tutorial.State.stage == LifeOnboardingStage.ClearWeather) tutorial.State.clearCast = true;
            tutorial.Persist();
        }
        void OnApplicationPause(bool paused) { if (paused) Persist(); }
        void OnApplicationQuit() => Persist();
        void OnDisable() { hud?.OnboardingHighlight(); }
        void OnDestroy() { hud?.OnboardingHighlight(); revision++; if (opening && opening.IsPlaying) opening.Cancel(); if (view) Destroy(view.gameObject); SilenceAll(); }
    }
}
