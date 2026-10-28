using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        [Tooltip("参考画布中的最大宽度和高度；气泡按本轮字数调整。")]
        public Vector2 bubbleSize = new Vector2(180f, 84f);
        [Range(16f, 80f)] public float glyphHeight = 43f;
        [Range(.5f, 14f)] public float glyphStrokeWidth = 9f;

        public JourneyConversation ActiveConversation { get; private set; }
        public JourneyConversation PresentationConversation => photoConversation ??
            (ActiveConversation != null && ActiveConversation.speakerId == photoTargetId ? null : ActiveConversation);
        public IReadOnlyList<JourneyConversation> Completed => completed;
        public int FirstParticipantId => firstId;
        public int SecondParticipantId => secondId;
        public string CurrentIntent { get; private set; } = "";
        public int VisibleBubbleCount => HasVisibleCurrentBubble(ResolveViewCamera()) ? 1 : 0;
        public Camera CurrentViewCamera => ResolveViewCamera();

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
        private CanvasGroup bubbleGroup;
        private JourneyConversation presentedConversation;
        private JourneySocialParticipant presentedSpeaker, presentedListener;
        private readonly List<AnimalGlyphGraphic> glyphs = new List<AnimalGlyphGraphic>();
        private GlyphCatalog catalog;
        private JourneyConversation photoConversation;
        private int photoTargetId = -1;

        public void SetPhotoTarget(int id, bool keepCurrent)
        {
            photoTargetId = id;
            photoConversation = keepCurrent && ActiveConversation?.speakerId == id ? Copy(ActiveConversation) : null;
        }
        public void ClearPhotoTarget() { photoConversation = null; photoTargetId = -1; }

        public bool IsParticipating(int id) => firstId >= 0 && (id == firstId || id == secondId);

        // Presentation ownership includes a retiring word; language evidence still requires the current turn.
        public bool HasPresentedBubbleFor(int id)
        {
            Camera camera = ResolveViewCamera();
            return presentedConversation != null && presentedSpeaker.id == id && bubble && bubble.gameObject.activeInHierarchy &&
                bubbleGroup && bubbleGroup.alpha > 0f && canvas && canvas.enabled && canvas.worldCamera == camera && PresentedSpeakerInView(camera);
        }

        /// <summary>Restart with the herd's simulation clock while retaining the presentation objects.</summary>
        public void ResetSchedule()
        {
            ClearPhotoTarget();
            ActiveConversation = null;
            completed.Clear(); eligible.Clear();
            participantReady.Clear(); lastParticipation.Clear(); pairReady.Clear();
            participants = Array.Empty<JourneySocialParticipant>();
            random = null;
            firstId = secondId = -1; sequence = turn = 0;
            firstRoot = secondRoot = null;
            nextConversation = nextTurn = preparationUntil = now = 0f;
            clue = preparing = false; CurrentIntent = "";
            HidePresentation();
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
            Camera camera = ResolveViewCamera();
            int bestVisibility = -1;
            for (int i = 0; i < eligible.Count; i++)
                for (int j = i + 1; j < eligible.Count; j++)
                {
                    var a = eligible[i]; var b = eligible[j];
                    if (a.id == b.id || !Ready(pairReady, PairKey(a.id, b.id)) ||
                        Vector3.Distance(a.root.position, b.root.position) > interactionDistance ||
                        !ClearSight(Anchor(a), Anchor(b), a.root, b.root)) continue;
                    // Prefer a nearby exchange the player can notice, while retaining weighted rotation within that group.
                    int visibility = camera == null ? 0 :
                        (InView(camera, a, b.root) ? 1 : 0) + (InView(camera, b, a.root) ? 1 : 0);
                    if (visibility < bestVisibility) continue;
                    if (visibility > bestVisibility) { bestVisibility = visibility; totalWeight = 0f; }
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
            CurrentIntent = clue ? "you" : new[] { "greeting", "sniff", "nuzzle", "rub" }[random.Next(4)];
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
                glyphIds = clue ? new[] { "you" } : new[] { "you", "hello" }, isClue = clue, intent = CurrentIntent,
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
            if (interrupted && photoConversation == null) HidePresentation();
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
            var current = PresentationConversation;
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

        /// <summary>A visible speaker can be heard even when the listener is outside the frame; this does not record evidence.</summary>
        public bool CanPresentFrom(Camera camera)
        {
            var current = PresentationConversation;
            return current != null && camera != null && camera.isActiveAndEnabled &&
                TryFind(current.speakerId, out var speaker) && TryFind(current.listenerId, out var listener) &&
                (photoConversation != null ? speaker.root && listener.root && speaker.root.gameObject.activeInHierarchy && listener.root.gameObject.activeInHierarchy :
                    CanParticipate(speaker) && CanParticipate(listener)) && InView(camera, speaker, listener.root);
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
            int count = gameObject.scene.GetPhysicsScene().Raycast(from, ray.normalized, sightHits, ray.magnitude,
                occlusionMask, QueryTriggerInteraction.Ignore);
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

        private void LateUpdate() => UpdatePresentation(ProceduralUiLibrary.FadeFrameDelta(Time.unscaledDeltaTime));

        private void UpdatePresentation(float deltaTime)
        {
            Camera camera = ResolveViewCamera();
            if (presentedConversation != null && !PresentedSpeakerInView(camera)) HidePresentation();
            if (presentedConversation != null && !MatchesCurrentPresentation())
            {
                bubbleGroup.alpha = ProceduralUiLibrary.FadeAlpha(bubbleGroup.alpha, false, deltaTime);
                if (bubbleGroup.alpha <= 0f) HidePresentation();
                else PositionPresented(camera);
                return;
            }
            if (!CanPresentFrom(camera)) { HidePresentation(); return; }
            Present(camera);
            if (presentedConversation == null) return;
            bubbleGroup.alpha = ProceduralUiLibrary.FadeAlpha(bubbleGroup.alpha, true, deltaTime);
            if (photoConversation == null && HasVisibleCurrentBubble(camera)) ObserveFrom(camera);
        }

        private bool HasVisibleCurrentBubble(Camera camera)
        {
            return MatchesCurrentPresentation() &&
                bubble && bubble.gameObject.activeInHierarchy && bubbleGroup &&
                bubbleGroup.alpha >= ProceduralUiLibrary.SpeechVisibleAlpha &&
                canvas && canvas.enabled && canvas.worldCamera == camera && CanPresentFrom(camera);
        }

        private bool MatchesCurrentPresentation()
        {
            var current = PresentationConversation;
            var shown = presentedConversation;
            if (current == null || shown == null || current.sequenceId != shown.sequenceId || current.turnIndex != shown.turnIndex ||
                current.speakerId != shown.speakerId || current.listenerId != shown.listenerId || current.glyphIds == null ||
                shown.glyphIds == null || current.glyphIds.Length != shown.glyphIds.Length) return false;
            for (int i = 0; i < current.glyphIds.Length; i++)
                if (current.glyphIds[i] != shown.glyphIds[i]) return false;
            return true;
        }

        private bool PresentedSpeakerInView(Camera camera)
        {
            return camera && camera.isActiveAndEnabled &&
                TryFind(presentedSpeaker.id, out var speaker) && TryFind(presentedListener.id, out var listener) &&
                speaker.root == presentedSpeaker.root && listener.root == presentedListener.root &&
                speaker.root && listener.root && speaker.root.gameObject.activeInHierarchy && listener.root.gameObject.activeInHierarchy &&
                InView(camera, speaker, listener.root);
        }

        private void HidePresentation()
        {
            if (bubbleGroup) bubbleGroup.alpha = 0f;
            if (bubble) bubble.gameObject.SetActive(false);
            presentedConversation = null;
        }

        private Camera ResolveViewCamera()
        {
            // A deliberate capture view remains authoritative; normal play follows the currently enabled main view.
            if (viewCamera != null && viewCamera.isActiveAndEnabled && viewCamera.targetTexture != null) return viewCamera;
            Camera main = Camera.main;
            if (main != null && main.isActiveAndEnabled && main.gameObject.scene == gameObject.scene) return main;
            return viewCamera != null && viewCamera.isActiveAndEnabled ? viewCamera : null;
        }

        private void Present(Camera camera)
        {
            var current = PresentationConversation;
            if (current?.glyphIds == null || current.glyphIds.Length == 0) { HidePresentation(); return; }
            if (catalog == null)
            {
                TextAsset source = Resources.Load<TextAsset>("WeatherHouseGlyphs");
                if (source == null) return;
                catalog = JsonUtility.FromJson<GlyphCatalog>(source.text);
            }
            string[] ids = current.glyphIds;
            if (Array.Exists(ids, id => Array.Find(catalog.glyphs, item => item.id == id) == null)) { HidePresentation(); return; }
            if (canvas == null)
            {
                var go = new GameObject("Journey animal words", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                go.transform.SetParent(transform, false); go.layer = 5;
                canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.sortingOrder = 80;
                var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1600f, 1000f); scaler.matchWidthOrHeight = .5f;
                bubble = ProceduralUiLibrary.Create(go.transform, "Animal bubble", UiMark.AnimalBubble,
                    Vector2.zero, bubbleSize, ProceduralUiLibrary.Cream).rectTransform;
                bubbleGroup = bubble.gameObject.AddComponent<CanvasGroup>();
                bubbleGroup.alpha = 0f; bubbleGroup.interactable = bubbleGroup.blocksRaycasts = false;
            }
            if (!MatchesCurrentPresentation())
            {
                presentedConversation = Copy(current);
                TryFind(current.speakerId, out presentedSpeaker);
                TryFind(current.listenerId, out presentedListener);
                bubbleGroup.alpha = 0f;
            }
            canvas.worldCamera = camera; canvas.planeDistance = camera.nearClipPlane + .2f;
            bubble.sizeDelta = ProceduralUiLibrary.AnimalBubbleSize(bubbleSize, ids.Length, glyphHeight, ((RectTransform)canvas.transform).rect.size);
            var background = bubble.GetComponent<ProceduralUiGraphic>();
            background.color = ProceduralUiLibrary.Cream;
            float height = Mathf.Min(glyphHeight, bubble.sizeDelta.y * .52f, bubble.sizeDelta.x * .78f / (ids.Length * .95f));
            for (int i = 0; i < ids.Length; i++)
            {
                if (i == glyphs.Count)
                {
                    var graphic = new GameObject("Animal glyph " + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(AnimalGlyphGraphic)).GetComponent<AnimalGlyphGraphic>();
                    graphic.transform.SetParent(bubble, false); graphic.gameObject.layer = 5; graphic.raycastTarget = false;
                    glyphs.Add(graphic);
                }
                var glyph = glyphs[i]; glyph.gameObject.SetActive(true);
                glyph.rectTransform.sizeDelta = new Vector2(height * 5f / 6f, height);
                glyph.rectTransform.anchoredPosition = new Vector2((i - (ids.Length - 1) * .5f) * height * .95f, bubble.sizeDelta.y * ProceduralUiStyle.Current.tailSize * .5f);
                glyph.color = ProceduralUiLibrary.Ink;
                if (glyph.lineWidth != glyphStrokeWidth) { glyph.lineWidth = glyphStrokeWidth; glyph.SetVerticesDirty(); }
                glyph.SvgBody = Array.Find(catalog.glyphs, item => item.id == ids[i]).body;
            }
            for (int i = ids.Length; i < glyphs.Count; i++) glyphs[i].gameObject.SetActive(false);
            bubble.gameObject.SetActive(true);
            PositionPresented(camera);
        }

        private void PositionPresented(Camera camera)
        {
            canvas.worldCamera = camera; canvas.planeDistance = camera.nearClipPlane + .2f;
            Vector3 screen = camera.WorldToViewportPoint(Anchor(presentedSpeaker));
            Rect size = ((RectTransform)canvas.transform).rect;
            Vector2 position = new Vector2((screen.x - .5f) * size.width, (screen.y - .5f) * size.height + bubble.sizeDelta.y * .5f + 12f);
            if (size.width > bubble.sizeDelta.x + 24f && size.height > bubble.sizeDelta.y + 24f)
            {
                position.x = Mathf.Clamp(position.x, size.xMin + bubble.sizeDelta.x * .5f + 12f, size.xMax - bubble.sizeDelta.x * .5f - 12f);
                position.y = Mathf.Clamp(position.y, size.yMin + bubble.sizeDelta.y * .5f + 12f, size.yMax - bubble.sizeDelta.y * .5f - 12f);
                position = RaiseAboveListenerHead(camera, size, position, presentedListener);
                position.y = Mathf.Min(position.y, size.yMax - bubble.sizeDelta.y * .5f - 12f);
            }
            bubble.anchoredPosition = position;
        }

        private Vector2 RaiseAboveListenerHead(Camera camera, Rect size, Vector2 position, JourneySocialParticipant listener)
        {
            if (listener.root == null) return position;
            Vector3 head = listener.head != null ? listener.head.position : Anchor(listener) - Vector3.up * .35f;
            Vector3 screen = camera.WorldToViewportPoint(head);
            if (screen.z <= 0f || screen.x < 0f || screen.x > 1f || screen.y < 0f || screen.y > 1f) return position;
            float radius = Mathf.Max(.18f, listener.radius);
            Vector3 right = camera.WorldToViewportPoint(head + camera.transform.right * radius);
            Vector3 top = camera.WorldToViewportPoint(head + camera.transform.up * radius);
            Vector2 center = new Vector2((screen.x - .5f) * size.width, (screen.y - .5f) * size.height);
            Vector2 extent = new Vector2(Mathf.Abs(right.x - screen.x) * size.width + 4f,
                Mathf.Abs(top.y - screen.y) * size.height + 4f);
            Rect protectedHead = new Rect(center - extent, extent * 2f);
            Rect speech = new Rect(position - bubble.sizeDelta * .5f, bubble.sizeDelta);
            // Keep the original silhouette and speaker's horizontal anchor; only lift a reply that covers its listener.
            if (speech.Overlaps(protectedHead)) position.y = protectedHead.yMax + bubble.sizeDelta.y * .5f + 4f;
            return position;
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
            if (!HasVisibleCurrentBubble(camera) || !ObserveFrom(camera)) throw new InvalidOperationException("当前没有看清双方的动物交流。");
            string png = Path.ChangeExtension(Path.GetFullPath(path), ".png");
            string json = Path.ChangeExtension(png, ".json");
            if (File.Exists(png) || File.Exists(json)) throw new IOException("照片文件已存在，未覆盖。");
            var turns = new List<JourneyConversation>();
            foreach (var record in completed)
                if (record.sequenceId == PresentationConversation.sequenceId) turns.Add(Copy(record));
            var current = Copy(PresentationConversation);
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
            ClearPhotoTarget();
            FinishTurn(true);
            if (random != null) EndPair();
        }
    }
}
