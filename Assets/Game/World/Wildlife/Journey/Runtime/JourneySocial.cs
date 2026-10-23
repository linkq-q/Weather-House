using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using WeatherHousePrototype;

namespace WeatherHouse.WildlifeJourney
{
    public struct JourneySocialParticipant
    {
        public int id;
        public Transform root, head;
        public bool available;
        public float radius, bodyLength;
    }

    [Serializable]
    public sealed class JourneyConversation
    {
        public int sequenceId, turnIndex, speakerId, listenerId;
        public string[] glyphIds;
        public bool isClue, observed, interrupted;
        public string intent;
        public float startedAt, endsAt, observedAt;
        public Vector3 speakerPosition, listenerPosition;
    }

    /// <summary>A small, independent social schedule. The herd owns movement and body animation.</summary>
    public sealed class JourneySocial : MonoBehaviour
    {
        public int randomSeed = 23711;
        public Camera viewCamera;
        [Min(.1f)] public float interactionDistance = 3.5f;
        [Min(1f)] public float observationDistance = 45f;
        [Min(.1f)] public float turnDuration = 2.4f;
        [Min(0f)] public float turnGap = .4f;
        [Min(0f)] public float participantCooldown = 18f, pairCooldown = 48f;
        public Vector2 conversationGap = new Vector2(5f, 9f);
        public LayerMask occlusionMask = ~0;

        public JourneyConversation ActiveConversation { get; private set; }
        public IReadOnlyList<JourneyConversation> Completed => completed;
        public int FirstParticipantId => firstId;
        public int SecondParticipantId => secondId;
        public string CurrentIntent { get; private set; } = "";
        public int VisibleBubbleCount => bubble != null && bubble.gameObject.activeSelf ? 1 : 0;

        private readonly List<JourneyConversation> completed = new List<JourneyConversation>();
        private readonly List<JourneySocialParticipant> eligible = new List<JourneySocialParticipant>();
        private readonly Dictionary<int, float> participantReady = new Dictionary<int, float>();
        private readonly Dictionary<int, float> lastParticipation = new Dictionary<int, float>();
        private readonly Dictionary<long, float> pairReady = new Dictionary<long, float>();
        private readonly RaycastHit[] sightHits = new RaycastHit[16];
        private JourneySocialParticipant[] participants = Array.Empty<JourneySocialParticipant>();
        private System.Random random;
        private int firstId = -1, secondId = -1, sequence, turn;
        private Transform firstRoot, secondRoot;
        private float nextConversation, nextTurn, preparationUntil, now;
        private bool clue, preparing;
        private Canvas canvas;
        private RectTransform bubble;
        private AnimalGlyphGraphic glyph;
        private GlyphCatalog catalog;

        public bool IsParticipating(int id) => firstId >= 0 && (id == firstId || id == secondId);

        /// <summary>Restart with the herd's simulation clock while retaining the presentation objects.</summary>
        public void ResetSchedule()
        {
            ActiveConversation = null;
            completed.Clear(); eligible.Clear();
            participantReady.Clear(); lastParticipation.Clear(); pairReady.Clear();
            participants = Array.Empty<JourneySocialParticipant>();
            random = null;
            firstId = secondId = -1; sequence = turn = 0;
            firstRoot = secondRoot = null;
            nextConversation = nextTurn = preparationUntil = now = 0f;
            clue = preparing = false; CurrentIntent = "";
            if (bubble != null) bubble.gameObject.SetActive(false);
        }

        public void Tick(float time, JourneySocialParticipant[] input)
        {
            if (float.IsNaN(time) || float.IsInfinity(time)) throw new ArgumentOutOfRangeException(nameof(time));
            now = time;
            participants = input ?? Array.Empty<JourneySocialParticipant>();
            if (random == null)
            {
                random = new System.Random(randomSeed);
                nextConversation = now + 1.2f;
            }

            if (firstId >= 0)
            {
                if (!TryFind(firstId, out var first) || !TryFind(secondId, out var second) ||
                    first.root != firstRoot || second.root != secondRoot ||
                    !CanParticipate(first) || !CanParticipate(second) ||
                    Vector3.Distance(first.root.position, second.root.position) > interactionDistance * 1.75f)
                {
                    FinishTurn(true);
                    EndPair();
                    return;
                }
                if (preparing)
                {
                    if (now >= preparationUntil) { EndPair(); return; }
                    Vector3 separation = second.root.position - first.root.position; separation.y = 0f;
                    float speakingDistance = Mathf.Max(Mathf.Max(0f, first.radius) + Mathf.Max(0f, second.radius) + .8f,
                        (Mathf.Max(0f, first.bodyLength) + Mathf.Max(0f, second.bodyLength)) * .5f + .35f);
                    if (separation.sqrMagnitude <= speakingDistance * speakingDistance &&
                        Facing(first.root, second.root) && Facing(second.root, first.root))
                    {
                        preparing = false;
                        StartTurn();
                    }
                    return;
                }
                if (ActiveConversation != null && now >= ActiveConversation.endsAt)
                {
                    FinishTurn(false);
                    if (turn == 1) { EndPair(); return; }
                    turn = 1;
                    nextTurn = now + Mathf.Max(0f, turnGap);
                }
                if (ActiveConversation == null && now >= nextTurn) StartTurn();
                return;
            }
            if (now >= nextConversation) TryStartPair();
        }

        private void TryStartPair()
        {
            eligible.Clear();
            foreach (var participant in participants)
                if (CanParticipate(participant) && Ready(participantReady, participant.id)) eligible.Add(participant);
            // Shuffle before weighted sampling, so array order cannot designate the recurring speakers.
            for (int i = eligible.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var swap = eligible[i]; eligible[i] = eligible[j]; eligible[j] = swap;
            }
            JourneySocialParticipant chosenFirst = default, chosenSecond = default;
            float totalWeight = 0f;
            for (int i = 0; i < eligible.Count; i++)
                for (int j = i + 1; j < eligible.Count; j++)
                {
                    var a = eligible[i]; var b = eligible[j];
                    if (a.id == b.id || !Ready(pairReady, PairKey(a.id, b.id)) ||
                        Vector3.Distance(a.root.position, b.root.position) > interactionDistance ||
                        !ClearSight(Anchor(a), Anchor(b), a.root, b.root)) continue;
                    float weight = WaitWeight(a.id) * WaitWeight(b.id);
                    totalWeight += weight;
                    if (random.NextDouble() * totalWeight < weight) { chosenFirst = a; chosenSecond = b; }
                }
            if (totalWeight <= 0f) { nextConversation = now + .5f; return; }
            firstId = chosenFirst.id; secondId = chosenSecond.id;
            firstRoot = chosenFirst.root; secondRoot = chosenSecond.root;
            lastParticipation[firstId] = lastParticipation[secondId] = now;
            sequence++;
            clue = sequence % 3 == 2;
            CurrentIntent = clue ? "you" : new[] { "greeting", "sniff", "nuzzle" }[random.Next(3)];
            turn = 0;
            preparing = true;
            preparationUntil = now + 6f;
        }

        private float WaitWeight(int id)
        {
            return lastParticipation.TryGetValue(id, out float last)
                ? 1f + Mathf.Clamp((now - last) / 12f, 0f, 5f) : 8f;
        }

        private bool Ready<T>(Dictionary<T, float> ready, T key)
        { return !ready.TryGetValue(key, out float until) || now >= until; }

        private static long PairKey(int a, int b)
        { return ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b); }

        private static bool CanParticipate(JourneySocialParticipant participant)
        { return participant.id >= 0 && participant.available && participant.root != null && participant.root.gameObject.activeInHierarchy; }

        private bool TryFind(int id, out JourneySocialParticipant result)
        {
            foreach (var participant in participants)
                if (participant.id == id) { result = participant; return true; }
            result = default;
            return false;
        }

        private void StartTurn()
        {
            ActiveConversation = new JourneyConversation
            {
                sequenceId = sequence, turnIndex = turn,
                speakerId = turn == 0 ? firstId : secondId, listenerId = turn == 0 ? secondId : firstId,
                glyphIds = new[] { clue ? "you" : "hello" }, isClue = clue, intent = CurrentIntent,
                startedAt = now, endsAt = now + Mathf.Max(.1f, turnDuration)
            };
        }

        private void FinishTurn(bool interrupted)
        {
            if (ActiveConversation != null && ActiveConversation.observed)
            {
                var record = Copy(ActiveConversation);
                record.interrupted = interrupted;
                if (interrupted) record.endsAt = now;
                completed.Add(record);
                if (completed.Count > 128) completed.RemoveAt(0);
            }
            ActiveConversation = null;
            if (bubble != null) bubble.gameObject.SetActive(false);
        }

        private void EndPair()
        {
            if (firstId >= 0)
            {
                participantReady[firstId] = participantReady[secondId] = now + Mathf.Max(0f, participantCooldown);
                pairReady[PairKey(firstId, secondId)] = now + Mathf.Max(0f, pairCooldown);
            }
            firstId = secondId = -1; firstRoot = secondRoot = null; CurrentIntent = ""; preparing = false;
            float low = Mathf.Max(.1f, conversationGap.x), high = Mathf.Max(low, conversationGap.y);
            nextConversation = now + low + (float)random.NextDouble() * (high - low);
        }

        /// <summary>Only an unobstructed on-screen exchange counts as a witnessed language example.</summary>
        public bool ObserveFrom(Camera camera)
        {
            var current = ActiveConversation;
            if (current == null || camera == null || !camera.isActiveAndEnabled ||
                !TryFind(current.speakerId, out var speaker) || !TryFind(current.listenerId, out var listener) ||
                !CanParticipate(speaker) || !CanParticipate(listener) ||
                !InView(camera, speaker, listener.root) || !InView(camera, listener, speaker.root)) return false;
            if (current.isClue && (!Facing(speaker.root, listener.root) || !Facing(listener.root, speaker.root))) return false;
            if (!current.observed)
            {
                current.observed = true; current.observedAt = now;
                current.speakerPosition = speaker.root.position; current.listenerPosition = listener.root.position;
            }
            return true;
        }

        private bool InView(Camera camera, JourneySocialParticipant participant, Transform other)
        {
            Vector3 anchor = Anchor(participant);
            if ((camera.transform.position - anchor).sqrMagnitude > observationDistance * observationDistance) return false;
            Vector3 screen = camera.WorldToViewportPoint(anchor);
            return screen.z > camera.nearClipPlane && screen.x > .04f && screen.x < .96f && screen.y > .04f && screen.y < .9f &&
                ClearSight(camera.transform.position, anchor, participant.root, other);
        }

        private bool ClearSight(Vector3 from, Vector3 to, Transform first, Transform second)
        {
            Vector3 ray = to - from;
            if (ray.sqrMagnitude < .0001f) return true;
            int count = Physics.RaycastNonAlloc(from, ray.normalized, sightHits, ray.magnitude, occlusionMask, QueryTriggerInteraction.Ignore);
            if (count == sightHits.Length) return false;
            for (int i = 0; i < count; i++)
                if (!sightHits[i].transform.IsChildOf(first) && !sightHits[i].transform.IsChildOf(second)) return false;
            return true;
        }

        private static bool Facing(Transform speaker, Transform listener)
        {
            Vector3 toward = listener.position - speaker.position; toward.y = 0f;
            return toward.sqrMagnitude < .001f || Vector3.Dot(speaker.forward, toward.normalized) > .7f;
        }

        private static Vector3 Anchor(JourneySocialParticipant participant)
        {
            return participant.head != null ? participant.head.position + Vector3.up * .35f :
                participant.root.position + Vector3.up * Mathf.Max(.85f, participant.radius * 3.2f);
        }

        private void LateUpdate()
        {
            Camera camera = viewCamera != null ? viewCamera : Camera.main;
            bool visible = ObserveFrom(camera);
            if (!visible) { if (bubble != null) bubble.gameObject.SetActive(false); return; }
            Present(camera);
        }

        private void Present(Camera camera)
        {
            if (catalog == null)
            {
                TextAsset source = Resources.Load<TextAsset>("WeatherHouseGlyphs");
                if (source == null) return;
                catalog = JsonUtility.FromJson<GlyphCatalog>(source.text);
            }
            GlyphDefinition definition = Array.Find(catalog.glyphs, item => item.id == ActiveConversation.glyphIds[0]);
            if (definition == null) return;
            if (canvas == null)
            {
                var go = new GameObject("Journey animal words", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                go.transform.SetParent(transform, false); go.layer = 5;
                canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.sortingOrder = 80;
                var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = .5f;
                bubble = new GameObject("One spoken word", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                bubble.SetParent(go.transform, false); bubble.gameObject.layer = 5;
                bubble.sizeDelta = new Vector2(66f, 54f);
                var background = bubble.GetComponent<Image>(); background.color = new Color(1f, .98f, .9f, .94f); background.raycastTarget = false;
                var tail = new GameObject("Tail", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                tail.SetParent(bubble, false); tail.gameObject.layer = 5; tail.sizeDelta = new Vector2(10f, 10f);
                tail.anchoredPosition = new Vector2(0f, -27f); tail.localRotation = Quaternion.Euler(0f, 0f, 45f);
                tail.GetComponent<Image>().color = background.color; tail.GetComponent<Image>().raycastTarget = false;
                glyph = new GameObject("Animal glyph", typeof(RectTransform), typeof(CanvasRenderer), typeof(AnimalGlyphGraphic)).GetComponent<AnimalGlyphGraphic>();
                glyph.transform.SetParent(bubble, false); glyph.gameObject.layer = 5;
                glyph.rectTransform.sizeDelta = new Vector2(30f, 36f); glyph.raycastTarget = false;
                glyph.color = new Color(.24f, .3f, .25f); glyph.lineWidth = 4.4f;
            }
            canvas.worldCamera = camera; canvas.planeDistance = camera.nearClipPlane + .2f;
            bubble.gameObject.SetActive(true); glyph.SvgBody = definition.body;
            TryFind(ActiveConversation.speakerId, out var speaker);
            Vector3 screen = camera.WorldToViewportPoint(Anchor(speaker));
            Rect size = ((RectTransform)canvas.transform).rect;
            bubble.anchoredPosition = new Vector2((screen.x - .5f) * size.width, (screen.y - .5f) * size.height + 40f);
        }

        [Serializable]
        private sealed class PhotoContext
        {
            public string image;
            public long capturedUtcTicks;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public JourneyConversation[] turns;
        }

        /// <summary>Called explicitly by the player's shutter. Stores a sample photo and its original conversation context.</summary>
        public string CaptureCurrent(Camera camera, string path)
        {
            if (!ObserveFrom(camera)) throw new InvalidOperationException("当前没有看清双方的动物交流。");
            string png = Path.ChangeExtension(Path.GetFullPath(path), ".png");
            string json = Path.ChangeExtension(png, ".json");
            if (File.Exists(png) || File.Exists(json)) throw new IOException("照片文件已存在，未覆盖。");
            var turns = new List<JourneyConversation>();
            foreach (var record in completed)
                if (record.sequenceId == ActiveConversation.sequenceId) turns.Add(Copy(record));
            var current = Copy(ActiveConversation);
            TryFind(current.speakerId, out var speaker); TryFind(current.listenerId, out var listener);
            current.speakerPosition = speaker.root.position; current.listenerPosition = listener.root.position;
            turns.Add(current);
            var context = new PhotoContext
            {
                image = Path.GetFileName(png), capturedUtcTicks = DateTime.UtcNow.Ticks,
                cameraPosition = camera.transform.position, cameraRotation = camera.transform.rotation, turns = turns.ToArray()
            };
            Directory.CreateDirectory(Path.GetDirectoryName(png));
            RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
            int previousMask = camera.cullingMask;
            RenderTexture target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            Texture2D texture = null;
            try
            {
                Present(camera);
                camera.targetTexture = target; camera.cullingMask |= 1 << 5;
                Canvas.ForceUpdateCanvases();
                Present(camera);
                Canvas.ForceUpdateCanvases();
                camera.Render(); RenderTexture.active = target;
                texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0); texture.Apply(false, false);
                File.WriteAllBytes(png, texture.EncodeToPNG());
                File.WriteAllText(json, JsonUtility.ToJson(context, true));
                return png;
            }
            finally
            {
                camera.targetTexture = previousTarget; camera.cullingMask = previousMask;
                RenderTexture.active = previousActive; RenderTexture.ReleaseTemporary(target);
                if (texture != null) { if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture); }
            }
        }

        private static JourneyConversation Copy(JourneyConversation source)
        {
            return new JourneyConversation
            {
                sequenceId = source.sequenceId, turnIndex = source.turnIndex, speakerId = source.speakerId, listenerId = source.listenerId,
                glyphIds = (string[])source.glyphIds.Clone(), isClue = source.isClue, observed = source.observed,
                interrupted = source.interrupted, intent = source.intent, startedAt = source.startedAt, endsAt = source.endsAt,
                observedAt = source.observedAt, speakerPosition = source.speakerPosition, listenerPosition = source.listenerPosition
            };
        }

        private void OnDisable()
        {
            FinishTurn(true);
            if (random != null) EndPair();
        }
    }
}
