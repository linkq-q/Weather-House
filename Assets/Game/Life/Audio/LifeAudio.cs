using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHousePrototype;
using WeatherHouse.Hillside;

namespace WeatherHouse.Life
{
    [DisallowMultipleComponent, DefaultExecutionOrder(300)]
    public sealed class LifeAudio : MonoBehaviour
    {
        public const int SourceBudget = 12;
        static LifeAudio instance;
        public LifeAudioCatalog Catalog { get; private set; }
        public LifeAudioSurface LastSurface { get; private set; }
        public int PlayedCount { get; private set; }
        public int FootstepCount { get; private set; }
        readonly Dictionary<LifeAudioCue, int> playCounts = new Dictionary<LifeAudioCue, int>();
        public int Count(LifeAudioCue cue) => playCounts.TryGetValue(cue, out int count) ? count : 0;
        readonly AudioSource[] sources = new AudioSource[SourceBudget];
        readonly float[] stopAt = new float[SourceBudget];
        readonly bool[] voiceSlot = new bool[SourceBudget];
        readonly Dictionary<LifeAudioCue, float> nextCue = new Dictionary<LifeAudioCue, float>();
        readonly Dictionary<LifeAudioCue, int> variants = new Dictionary<LifeAudioCue, int>();
        readonly Dictionary<int, float> nextVoice = new Dictionary<int, float>();
        readonly LifeAudioGround ground = new LifeAudioGround();
        GameplaySession gameplay; LifeSession life; GameplayHud hud;
        WeatherRegion[] weather = System.Array.Empty<WeatherRegion>();
        WorldWaterSurface[] waters = System.Array.Empty<WorldWaterSurface>();
        Transform player; Vector3 previousPosition, shorePoint;
        float travelled, nextStep, nextEnvironment, strokeUntil, nextAmbientVoice, shoreGain, lastProgress = -10;
        bool wasGrounded, ready, wasCamera, wasRecording, wasDrawing, wasDialogue, focused = true, strokeMoved;
        bool CanPlay => ready && isActiveAndEnabled && focused && player;
        AnimalActor lastSpeaker; LifeSpeaker dialogueSpeaker; LifeRequest lastRequest; int lastRevision; string lastUtterance;
#if UNITY_EDITOR
        public static System.Action<string> StrokeDiagnostic;
#endif

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        void TraceStroke(string stage)
        {
#if UNITY_EDITOR
            if (StrokeDiagnostic == null) return;
            AudioSource source = sources[2]; AudioClip clip = source ? source.clip : null;
            StrokeDiagnostic("TRACE stroke " + stage + " frame=" + Time.frameCount + " time=" + Time.unscaledTime.ToString("F3") +
                " real=" + Time.realtimeSinceStartup.ToString("F3") + " dsp=" + AudioSettings.dspTime.ToString("F3") +
                " instance=" + GetInstanceID() + " singleton=" + (instance ? instance.GetInstanceID() : 0) +
                " ready=" + ready + " enabled=" + isActiveAndEnabled + " focus=" + focused +
                " input=" + (life && life.GestureInputActive) + " pending=" + strokeMoved + " until=" + strokeUntil.ToString("F3") +
                " source=" + (source ? source.GetInstanceID() : 0) + " active=" + (source && source.isActiveAndEnabled) +
                " playing=" + (source && source.isPlaying) + " volume=" + (source ? source.volume : -1) +
                " loop=" + (source && source.loop) + " mute=" + (source && source.mute) +
                " clip=" + (clip ? clip.name + "/" + clip.loadState + "/" + clip.length.ToString("F3") : "none") +
                " listenerPause=" + AudioListener.pause + " listenerVolume=" + AudioListener.volume +
                (stage == "cancel" ? " caller=" + new System.Diagnostics.StackTrace(1, false) : ""));
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic() { instance = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= Loaded; SceneManager.sceneLoaded += Loaded; Attach();
        }
        static void Loaded(Scene scene, LoadSceneMode mode) => Attach();
        static void Attach()
        {
            if (SceneManager.GetActiveScene().name != "WeatherHouse_WatersideV2") return;
            GameplaySession session = FindObjectOfType<GameplaySession>();
            if (session && !session.GetComponent<LifeAudio>()) session.gameObject.AddComponent<LifeAudio>();
        }
        void Start()
        {
            gameplay = GetComponent<GameplaySession>(); life = GetComponent<LifeSession>(); hud = GetComponent<GameplayHud>();
            Catalog = Resources.Load<LifeAudioCatalog>("LifeAudio/Catalog");
            if (!gameplay || !life || !Catalog || !(player = gameplay.PlayerTransform)) { enabled = false; return; }
            instance = this;
            string[] names = { "UI", "Progress", "Drawing", "Footsteps", "World 1", "World 2", "World 3", "Crafting", "Rain", "Wind", "Outdoor", "Shore" };
            for (int i = 0; i < SourceBudget; i++)
            {
                var holder = new GameObject("Life audio " + names[i]); holder.transform.SetParent(transform, false);
                AudioSource source = holder.AddComponent<AudioSource>(); sources[i] = source;
                source.playOnAwake = false; source.dopplerLevel = 0; source.minDistance = 1; source.maxDistance = 9;
                source.rolloffMode = AudioRolloffMode.Linear; source.spatialBlend = (i >= 3 && i <= 7) || i == 11 ? 1 : 0;
                source.priority = i >= 8 ? 200 : 80;
            }
            previousPosition = player.position; wasGrounded = IsGrounded();
            wasCamera = hud && hud.CameraMode; wasRecording = gameplay.Recorder && gameplay.Recorder.IsRecording;
            nextAmbientVoice = Time.unscaledTime + 15; ready = true;
        }
        public static void Play(LifeAudioCue cue) { if (instance && instance.CanPlay) instance.PlayCue(cue, instance.player.position); }
        public static void PlayAt(LifeAudioCue cue, Vector3 position) { if (instance) instance.PlayCue(cue, position); }
        public static void StrokeMoved()
        {
            if (instance) instance.TraceStroke("signal.request");
            if (!instance || !instance.CanPlay) return;
            instance.strokeMoved = true; instance.strokeUntil = Time.unscaledTime + .09f;
            instance.TraceStroke("signal.accept");
        }
        public static void StopStroke() { if (instance) { instance.TraceStroke("cancel"); instance.strokeMoved = false; instance.strokeUntil = 0; instance.Stop(2); } }
        void PlayCue(LifeAudioCue cue, Vector3 position, float gain = 1)
        {
            if (!CanPlay) return;
            LifeAudioCatalog.Sound sound = Catalog.Find(cue);
            if (sound == null || sound.clips.Length == 0 || (nextCue.TryGetValue(cue, out float due) && Time.unscaledTime < due)) return;
            int slot = cue == LifeAudioCue.Progress ? 1 : cue == LifeAudioCue.Ui || cue == LifeAudioCue.Paper || cue == LifeAudioCue.Camera ? 0 :
                cue == LifeAudioCue.GrassStep || cue == LifeAudioCue.DirtStep || cue == LifeAudioCue.StoneStep ? 3 : FreeWorldSlot();
            if (cue == LifeAudioCue.Ui && sources[0].isPlaying) return;
            if (slot < 0) return;
            int variant = variants.TryGetValue(cue, out int previous) ? (previous + 1) % sound.clips.Length : 0;
            variants[cue] = variant; nextCue[cue] = Time.unscaledTime + sound.cooldown;
            if (cue == LifeAudioCue.Progress) lastProgress = Time.unscaledTime;
            StartClip(slot, sound.clips[variant], position, sound.volume * gain, false);
            playCounts[cue] = Count(cue) + 1;
            if (sound.shortDuration > 0) stopAt[slot] = Time.unscaledTime + sound.shortDuration;
        }
        int FreeWorldSlot()
        {
            for (int i = 4; i <= 6; i++) if (!sources[i].isPlaying) return i;
            return 4;
        }
        void StartClip(int slot, AudioClip clip, Vector3 position, float volume, bool loop)
        {
            if (!clip) return;
            AudioSource source = sources[slot]; source.Stop(); stopAt[slot] = 0;
            source.transform.position = position; source.clip = clip; source.volume = volume; source.loop = loop; source.pitch = 1; source.Play(); PlayedCount++;
            if (slot == 2) TraceStroke("source.play");
            if (slot == 3) FootstepCount++;
            voiceSlot[slot] = false;
        }
        void Stop(int slot) { if (slot == 2 && sources[slot] && sources[slot].isPlaying) TraceStroke("source.stop"); if (sources[slot]) sources[slot].Stop(); stopAt[slot] = 0; }
        void Loop(int slot, LifeAudioCue cue, float amount, Vector3 position, float dt)
        {
            LifeAudioCatalog.Sound sound = Catalog.Find(cue); if (sound == null || sound.clips.Length == 0) return;
            AudioSource source = sources[slot]; source.transform.position = position;
            if (amount > .001f && !source.isPlaying) StartClip(slot, sound.clips[0], position, 0, true);
            source.volume = Mathf.MoveTowards(source.volume, sound.volume * amount, dt * .8f);
            if (amount <= .001f && source.volume <= .001f) Stop(slot);
        }
        void Update()
        {
            TraceStroke("update.enter");
            if (!ready || !player || !focused) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f), now = Time.unscaledTime;
            for (int i = 0; i < SourceBudget; i++) if (stopAt[i] > 0 && now >= stopAt[i]) Stop(i);
            UpdateUi(); UpdateSteps();
            // World drawing and journal practice share this state; their input component is added after bootstrap.
            bool drawing = life.GestureInputActive;
            // Consume each movement pulse once even when the next frame exceeds the short release window.
            Loop(2, LifeAudioCue.Stroke, drawing && (strokeMoved || now < strokeUntil) ? 1 : 0, player.position, dt);
            TraceStroke("update.after-loop");
            strokeMoved = false;
            if (!drawing && wasDrawing) StopStroke(); wasDrawing = drawing;
            bool crafting = life.World && life.World.IsCrafting;
            Loop(7, LifeAudioCue.Fiber, crafting ? 1 : 0, player.position, dt);
            if (!crafting) Stop(7);
            if (now >= nextEnvironment)
            {
                nextEnvironment = now + .5f; weather = FindObjectsOfType<WeatherRegion>(); waters = FindObjectsOfType<WorldWaterSurface>(); UpdateShore();
            }
            float rain = 0, wind = 0;
            foreach (WeatherRegion region in weather)
            {
                if (!region || !region.isActiveAndEnabled || !region.SoundActive || !region.Contains(player.position)) continue;
                float amount = region.SoundStrength == 2 ? 1 : .6f;
                if (region.Current == LocalWeather.Rain) rain = Mathf.Max(rain, amount);
                if (region.Current == LocalWeather.Wind) wind = Mathf.Max(wind, amount);
            }
            float duck = hud && (hud.JournalOpen || hud.IsDialogueOpen) ? .55f : 1;
            Loop(8, LifeAudioCue.Rain, rain * duck, player.position, dt); Loop(9, LifeAudioCue.Wind, wind * duck, player.position, dt);
            Loop(10, LifeAudioCue.Outdoor, duck, player.position, dt); Loop(11, LifeAudioCue.Shore, shoreGain * duck, shorePoint, dt);
            if (now >= nextAmbientVoice)
            {
                nextAmbientVoice = now + 18;
                if (!(hud && (hud.IsDialogueOpen || hud.JournalOpen)) && gameplay.Observed && gameplay.Observed.HasAmbientSpeech) Speak(gameplay.Observed, false);
            }
        }
        void UpdateUi()
        {
            bool camera = hud && hud.CameraMode, recording = gameplay.Recorder && gameplay.Recorder.IsRecording;
            if (camera != wasCamera) Play(LifeAudioCue.Ui); wasCamera = camera;
            if (recording && !wasRecording) Play(LifeAudioCue.Camera); wasRecording = recording;
            bool dialogue = hud && hud.IsDialogueOpen;
            AnimalActor speaker = dialogue ? hud.DialogueTarget : null;
            bool changed = CaptureSpeaker(speaker);
            if (dialogue && (!wasDialogue || changed)) Speak(speaker, true);
            if (!dialogue && wasDialogue) Play(LifeAudioCue.Ui);
            wasDialogue = dialogue;
        }
        bool CaptureSpeaker(AnimalActor actor)
        {
            bool changed = actor != lastSpeaker;
            if (changed) dialogueSpeaker = actor ? actor.GetComponent<LifeSpeaker>() : null;
            LifeRequest request = dialogueSpeaker ? dialogueSpeaker.Current : null;
            int revision = dialogueSpeaker ? dialogueSpeaker.Revision : 0;
            string utterance = actor && !dialogueSpeaker ? actor.UtteranceId : null;
            changed |= !ReferenceEquals(request, lastRequest) || revision != lastRevision || utterance != lastUtterance;
            lastSpeaker = actor; lastRequest = request; lastRevision = revision; lastUtterance = utterance;
            return changed;
        }
        public void Listen(AnimalActor actor) => Speak(actor, true);

        void Speak(AnimalActor actor, bool important)
        {
            if (!actor || Vector3.Distance(player.position, actor.transform.position) > 8 || Time.unscaledTime < lastProgress + .5f) return;
            int id = actor.GetInstanceID(); if (nextVoice.TryGetValue(id, out float due) && Time.unscaledTime < due) return;
            LifeSpeaker speaker = actor.GetComponent<LifeSpeaker>(); WildlifeAgent agent = actor.GetComponent<WildlifeAgent>();
            string species = agent ? agent.speciesId : actor.GetComponent<WildlifeInteractionTarget>()?.SpeciesId ?? (speaker ? speaker.key : null);
            AudioClip clip = Catalog.VoiceFor(species); if (!clip) return;
            int voices = 0; for (int i = 4; i <= 6; i++) if (sources[i].isPlaying && voiceSlot[i]) voices++;
            if (voices >= 2 || !important && voices > 0) return;
            int slot = -1;
            for (int i = 5; i <= 6; i++) if (!sources[i].isPlaying) { slot = i; break; }
            if (slot < 0) return;
            nextVoice[id] = Time.unscaledTime + 2;
            StartClip(slot, clip, actor.BubbleAnchor, important ? .48f : .23f, false);
            voiceSlot[slot] = true;
        }
        bool IsGrounded() => gameplay.forestSprite ? gameplay.forestSprite.IsGrounded : player.GetComponent<CharacterController>()?.isGrounded == true;
        void UpdateSteps()
        {
            Vector3 position = player.position, delta = position - previousPosition; previousPosition = position;
            bool grounded = IsGrounded(), blocked = hud && hud.BlocksMovement || life.GestureInputActive || gameplay.forestSprite && gameplay.forestSprite.InputBlocked;
            float distance = new Vector2(delta.x, delta.z).magnitude;
            bool landed = grounded && !wasGrounded;
            bool takeoff = wasGrounded && !grounded && gameplay.forestSprite && gameplay.forestSprite.VerticalSpeed > .1f;
            wasGrounded = grounded;
            if (blocked || distance > 2 || !grounded && !takeoff) { travelled = 0; return; }
            travelled += distance;
            float stride = gameplay.forestSprite && gameplay.forestSprite.CurrentState == "Run" ? .62f : .48f;
            if ((!landed && !takeoff && travelled < stride) || Time.unscaledTime < nextStep) return;
            travelled = 0; nextStep = Time.unscaledTime + .18f;
            LastSurface = ground.Resolve(position, player, Catalog, out Vector3 contact);
            foreach (WorldWaterSurface water in waters)
                if (water && water.TryGetHeight(contact, out float height) && height >= contact.y + .025f && height <= contact.y + .6f)
                {
                    LifeAudioCatalog.Sound splash = Catalog.Find(LifeAudioCue.Water);
                    if (splash != null && splash.clips.Length > 0) StartClip(3, splash.clips[Time.frameCount % splash.clips.Length], contact, splash.volume * .65f, false);
                    return;
                }
            LifeAudioCue cue = LastSurface == LifeAudioSurface.Grass ? LifeAudioCue.GrassStep : LastSurface == LifeAudioSurface.Dirt ? LifeAudioCue.DirtStep : LifeAudioCue.StoneStep;
            if (LastSurface != LifeAudioSurface.Unknown) PlayCue(cue, contact, landed ? 1.25f : 1);
        }
        void UpdateShore()
        {
            shoreGain = 0; float nearest = 24;
            foreach (WorldWaterSurface water in waters)
            {
                if (!water) continue;
                for (int ring = 0; ring <= 4; ring++) for (int angle = 0; angle < (ring == 0 ? 1 : 8); angle++)
                {
                    float radius = ring * 6f;
                    if (radius >= nearest) continue;
                    Vector3 point = player.position + new Vector3(Mathf.Cos(angle * Mathf.PI / 4), 0, Mathf.Sin(angle * Mathf.PI / 4)) * radius;
                    if (!water.TryGetHeight(point, out float height) || Mathf.Abs(height - player.position.y) > 5) continue;
                    nearest = radius; point.y = height; shorePoint = point; shoreGain = 1 - nearest / 24f;
                }
            }
        }
        void OnDisable()
        {
            for (int i = 0; i < sources.Length; i++) Stop(i);
            strokeMoved = false; strokeUntil = 0;
        }
        void OnEnable() { if (ready) SyncPresentation(); }
        void SyncPresentation()
        {
            if (!player || !gameplay) return;
            previousPosition = player.position; travelled = 0; wasGrounded = IsGrounded();
            wasCamera = hud && hud.CameraMode; wasRecording = gameplay.Recorder && gameplay.Recorder.IsRecording;
            wasDialogue = hud && hud.IsDialogueOpen; CaptureSpeaker(wasDialogue ? hud.DialogueTarget : null);
            nextAmbientVoice = Time.unscaledTime + 18;
        }
        void OnApplicationFocus(bool active) { focused = active; if (!active) OnDisable(); else SyncPresentation(); }
        void OnDestroy()
        {
            if (instance == this) instance = null;
            foreach (AudioSource source in sources) if (source) Destroy(source.gameObject);
        }
    }
}
