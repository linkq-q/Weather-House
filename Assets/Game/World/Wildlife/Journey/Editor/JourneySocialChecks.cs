using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

namespace WeatherHouse.WildlifeJourney.Editor
{
    public static class JourneySocialChecks
    {
        [MenuItem("Weather House/Journey/Check Social Logic")]
        public static void RunMenu() { Debug.Log(RunLogicChecks()); }

        public static string RunLogicChecks()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run isolated social checks outside Play mode.");
            Scene preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Journey social isolated checks") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(root, preview);
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            try
            {
                CheckRotationAndReplay(root.transform);
                CheckResetSchedule(root.transform);
                CheckPairLockAndCooldown(root.transform);
                CheckInterruptedParticipants(root.transform);
                CheckVisibilityAndRecordedContext(root.transform);
                CheckVocabularyAndListenerClue(root.transform);
                CheckVisiblePairPriority(root.transform);
                CheckCanonicalPresentationAndOcclusion(root.transform);
                CheckReplyPlacement(root.transform);
                CheckCameraFallback(root.transform);
                CheckPresentationFade(root.transform);
                return "PASS 11 social checks: weighted rotation and replay; clock reset and deterministic restart; mutual reservation and cooldown; approach/facing preparation, timeout and interruption; head-anchored visible-only context snapshots; legal glyphs and explicit listeners; visible-pair priority; canonical animal bubble with independent speaker visibility and real occlusion; reply placement clears the listener head without changing the speaker anchor or unobstructed layout; active-camera fallback and explicit capture view; timed speech fade, old-speaker retirement, immediate occlusion and capture visibility threshold.";
            }
            finally
            {
                Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
                UnityEngine.Random.state = randomState;
            }
        }

        private static void CheckRotationAndReplay(Transform parent)
        {
            var participants = Participants(parent, 8);
            JourneySocial first = Social(parent), replay = Social(parent);
            var seen = new HashSet<int>(); var pairs = new HashSet<string>();
            int lastSequence = 0;
            UnityEngine.Random.State state = UnityEngine.Random.state;
            float expected = UnityEngine.Random.value;
            UnityEngine.Random.state = state;
            for (int step = 0; step < 1000; step++)
            {
                float time = step * .2f;
                first.Tick(time, participants); replay.Tick(time, participants);
                Require(first.FirstParticipantId == replay.FirstParticipantId && first.SecondParticipantId == replay.SecondParticipantId,
                    "Equal seeds and participants did not replay the same social schedule.");
                FacePair(first, participants);
                var active = first.ActiveConversation;
                if (active == null || active.sequenceId == lastSequence) continue;
                lastSequence = active.sequenceId;
                seen.Add(active.speakerId); seen.Add(active.listenerId);
                pairs.Add(Math.Min(active.speakerId, active.listenerId) + ":" + Math.Max(active.speakerId, active.listenerId));
            }
            Require(seen.Count == 8 && pairs.Count >= 8, "Repeated opportunities kept selecting the same small set of animals or pairs.");
            Require(UnityEngine.Random.value == expected, "Social scheduling changed Unity's global random stream.");
            Object.DestroyImmediate(first.gameObject); Object.DestroyImmediate(replay.gameObject);
        }

        private static void CheckResetSchedule(Transform parent)
        {
            var participants = Participants(parent, 4);
            JourneySocial social = Social(parent);
            Camera camera = View(parent);
            for (int step = 0; step < 400; step++)
            {
                social.Tick(100f + step * .2f, participants);
                FacePair(social, participants);
                social.ObserveFrom(camera);
                if (social.Completed.Count > 0 && social.ActiveConversation != null && social.ActiveConversation.sequenceId > 1) break;
            }
            Require(social.Completed.Count > 0 && social.ActiveConversation != null,
                "Reset fixture did not establish old-clock history and a live exchange.");
            social.ResetSchedule();
            Require(social.ActiveConversation == null && social.FirstParticipantId == -1 && social.SecondParticipantId == -1 &&
                social.CurrentIntent == "" && social.Completed.Count == 0 && social.VisibleBubbleCount == 0,
                "Restart retained an old conversation, participant lock, observation or visible bubble.");

            JourneySocial fresh = Social(parent);
            float firstSpeech = -1f;
            for (int step = 0; step < 500; step++)
            {
                float time = step * .2f;
                social.Tick(time, participants); fresh.Tick(time, participants);
                Require(social.FirstParticipantId == fresh.FirstParticipantId && social.SecondParticipantId == fresh.SecondParticipantId &&
                    social.CurrentIntent == fresh.CurrentIntent, "Restart retained cooldown, participation weighting or random state from the old clock.");
                var active = social.ActiveConversation; var expected = fresh.ActiveConversation;
                Require((active == null) == (expected == null), "Restart did not reproduce the original speaking schedule.");
                if (active != null)
                {
                    if (firstSpeech < 0f) firstSpeech = time;
                    Require(active.sequenceId == expected.sequenceId && active.turnIndex == expected.turnIndex &&
                        active.speakerId == expected.speakerId && active.listenerId == expected.listenerId &&
                        active.glyphIds[0] == expected.glyphIds[0] && active.startedAt == expected.startedAt && active.endsAt == expected.endsAt,
                        "Equal seeds did not reproduce the same turns and words after restart.");
                }
                FacePair(social, participants);
            }
            Require(firstSpeech >= 0f && firstSpeech < 2f, "An old absolute deadline delayed the first conversation after the clock reset.");
            Object.DestroyImmediate(social.gameObject); Object.DestroyImmediate(fresh.gameObject); Object.DestroyImmediate(camera.gameObject);
        }

        private static void CheckPairLockAndCooldown(Transform parent)
        {
            var participants = Participants(parent, 2);
            JourneySocial social = Social(parent);
            social.turnDuration = .2f; social.turnGap = .1f;
            social.conversationGap = Vector2.one * .1f; social.participantCooldown = 2f; social.pairCooldown = 4f;
            social.Tick(0f, participants); social.Tick(1.2f, participants);
            Require(social.ActiveConversation == null && social.IsParticipating(0) && social.IsParticipating(1),
                "A pair spoke before its preparation phase.");
            social.Tick(1.21f, participants);
            Require(social.ActiveConversation != null, "An eligible pair never started.");
            int firstSpeaker = social.ActiveConversation.speakerId, firstListener = social.ActiveConversation.listenerId;
            social.Tick(1.43f, participants);
            Require(social.ActiveConversation == null && social.IsParticipating(firstSpeaker) && social.IsParticipating(firstListener),
                "The two animals were not reserved through the gap between turns.");
            social.Tick(1.54f, participants);
            Require(social.ActiveConversation.speakerId == firstListener && social.ActiveConversation.listenerId == firstSpeaker &&
                social.ActiveConversation.turnIndex == 1, "The response did not reverse speaker and listener.");
            social.Tick(1.75f, participants);
            Require(!social.IsParticipating(firstSpeaker) && !social.IsParticipating(firstListener), "Finished pair stayed locked.");
            social.Tick(3.6f, participants);
            Require(social.ActiveConversation == null, "Participant cooldown was ignored.");
            social.Tick(5.6f, participants);
            Require(social.ActiveConversation == null, "Pair cooldown was ignored after participant cooldown ended.");
            social.Tick(6.2f, participants);
            social.Tick(6.21f, participants);
            Require(social.ActiveConversation != null, "A pair never became eligible after both cooldowns ended.");
            Object.DestroyImmediate(social.gameObject);
        }

        private static void CheckInterruptedParticipants(Transform parent)
        {
            var participants = Participants(parent, 3);
            participants[2].available = false;
            JourneySocial social = Social(parent);
            social.Tick(0f, participants); social.Tick(1.2f, participants); social.Tick(1.21f, participants);
            Require(social.ActiveConversation != null && !social.IsParticipating(participants[2].id), "Unavailable animal entered an interaction.");
            int reserved = social.FirstParticipantId;
            for (int i = 0; i < participants.Length; i++)
                if (participants[i].id == reserved) participants[i].available = false;
            social.Tick(1.3f, participants);
            Require(social.FirstParticipantId == -1 && social.SecondParticipantId == -1 && social.ActiveConversation == null,
                "Becoming unavailable did not release both participants.");
            Object.DestroyImmediate(social.gameObject);
            participants = Participants(parent, 2); social = Social(parent);
            social.Tick(0f, participants); social.Tick(1.2f, participants); social.Tick(1.21f, participants);
            Object.DestroyImmediate(participants[0].root.gameObject);
            social.Tick(1.3f, participants);
            Require(social.ActiveConversation == null && social.FirstParticipantId == -1 && social.Completed.Count == 0,
                "Destroyed participant left a live pair or an unwitnessed observation.");
            Object.DestroyImmediate(social.gameObject);

            participants = Participants(parent, 2); social = Social(parent);
            participants[0].bodyLength = participants[1].bodyLength = 1.3f;
            participants[0].root.position = Vector3.right * 1.5f;
            participants[1].root.position = Vector3.left * 1.5f;
            social.Tick(0f, participants); social.Tick(1.2f, participants); social.Tick(1.3f, participants);
            Require(social.ActiveConversation == null && social.IsParticipating(0) && social.IsParticipating(1),
                "Distant animals started speaking before approaching.");
            participants[0].root.position = Vector3.right * .8f;
            participants[1].root.position = Vector3.left * .8f;
            participants[0].root.rotation = participants[1].root.rotation = Quaternion.identity;
            social.Tick(1.4f, participants);
            Require(social.ActiveConversation == null, "Animals started speaking before facing each other.");
            FacePair(social, participants); social.Tick(1.5f, participants);
            Require(social.ActiveConversation != null && social.ActiveConversation.startedAt == 1.5f &&
                social.ActiveConversation.endsAt > 3.8f, "Body-length-safe spacing did not permit speech, or preparation consumed speaking time.");
            Object.DestroyImmediate(social.gameObject);

            participants = Participants(parent, 2); social = Social(parent);
            participants[0].root.rotation = participants[1].root.rotation = Quaternion.identity;
            social.Tick(0f, participants); social.Tick(1.2f, participants); social.Tick(7.21f, participants);
            Require(social.FirstParticipantId == -1 && social.SecondParticipantId == -1 && social.ActiveConversation == null &&
                social.Completed.Count == 0, "An unfinished preparation did not release its pair after six seconds.");
            Object.DestroyImmediate(social.gameObject);
        }

        private static void CheckVisibilityAndRecordedContext(Transform parent)
        {
            var participants = Participants(parent, 2);
            JourneySocial social = Social(parent); social.turnDuration = .2f; social.turnGap = .1f;
            Camera camera = View(parent);
            social.Tick(0f, participants); social.Tick(1.2f, participants); social.Tick(1.21f, participants);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.back);
            Require(!social.ObserveFrom(camera), "Off-screen dialogue counted as seen.");
            social.Tick(1.43f, participants);
            Require(social.Completed.Count == 0, "Off-screen words leaked into observation history.");
            social.Tick(1.54f, participants);
            camera.transform.LookAt(new Vector3(0f, .9f, 0f));
            Require(social.ObserveFrom(camera), "The clear, on-screen pair was not observable from the sample camera.");
            participants[0].root.position += Vector3.right * .1f;
            social.Tick(1.6f, participants);
            Require(social.ActiveConversation != null && social.ObserveFrom(camera),
                "A sub-radius body adjustment lost the ongoing exchange or its bubble visibility.");
            Vector3 headPosition = participants[0].head.localPosition;
            participants[0].head.localPosition += Vector3.right * 100f;
            Require(!social.ObserveFrom(camera), "Visibility still used the body root after the head anchor left the view.");
            participants[0].head.localPosition = headPosition;
            Require(social.ObserveFrom(camera), "Restoring the visible head did not restore the dialogue.");
            Transform savedHead = participants[0].head;
            participants[0].head = null;
            Require(social.ObserveFrom(camera), "An animal without a head cue lost its fallback bubble anchor.");
            participants[0].head = savedHead;
            JourneyConversation live = social.ActiveConversation;
            social.Tick(1.75f, participants);
            Require(social.Completed.Count == 1, "Visible dialogue was not recorded exactly once.");
            JourneyConversation recorded = social.Completed[0];
            live.glyphIds[0] = "rain";
            Require(recorded.glyphIds.Length == 2 && recorded.glyphIds[0] == "you" && recorded.glyphIds[1] == "hello" && recorded.speakerId != recorded.listenerId && recorded.turnIndex == 1 &&
                recorded.observed && recorded.speakerPosition != recorded.listenerPosition,
                "Recorded evidence lost its immutable words, participants, turn or spatial context.");
            JourneyConversation restored = JsonUtility.FromJson<JourneyConversation>(JsonUtility.ToJson(recorded));
            Require(restored.sequenceId == recorded.sequenceId && restored.speakerId == recorded.speakerId &&
                restored.listenerId == recorded.listenerId && restored.glyphIds.Length == 2 && restored.glyphIds[0] == "you" && restored.glyphIds[1] == "hello", "Saved context did not retain the original speaker/listener.");
            Object.DestroyImmediate(social.gameObject); Object.DestroyImmediate(camera.gameObject);
        }

        private static void CheckVocabularyAndListenerClue(Transform parent)
        {
            TextAsset source = Resources.Load<TextAsset>("WeatherHouseGlyphs");
            Require(source != null, "Animal glyph catalog is missing.");
            GlyphCatalog catalog = JsonUtility.FromJson<GlyphCatalog>(source.text);
            var ids = new HashSet<string>(); foreach (var definition in catalog.glyphs) ids.Add(definition.id);
            var participants = Participants(parent, 2);
            JourneySocial social = Social(parent); social.turnDuration = .2f; social.turnGap = .1f;
            social.participantCooldown = social.pairCooldown = 0f; social.conversationGap = Vector2.one * .1f;
            Camera camera = View(parent);
            bool sawGreeting = false, sawClue = false;
            for (int step = 0; step < 60; step++)
            {
                social.Tick(step * .1f, participants);
                var active = social.ActiveConversation;
                if (active == null) continue;
                foreach (string id in active.glyphIds) Require(ids.Contains(id), "Social speech invented an undefined glyph.");
                if (!active.isClue) { sawGreeting |= active.glyphIds.Length == 2 && active.glyphIds[0] == "you" && active.glyphIds[1] == "hello"; continue; }
                Require(active.glyphIds.Length == 1 && active.glyphIds[0] == "you", "Listener clue changed into an unauthored sentence.");
                participants[0].root.rotation = participants[1].root.rotation = Quaternion.identity;
                Require(!social.ObserveFrom(camera), "Ambiguous listeners were accepted without facing each other.");
                participants[0].root.LookAt(participants[1].root); participants[1].root.LookAt(participants[0].root);
                Require(social.ObserveFrom(camera), "Clear reciprocal listener cue was hidden.");
                sawClue = true;
            }
            Require(sawGreeting && sawClue, "The sample did not contain both everyday greeting and a listener clue.");
            Object.DestroyImmediate(social.gameObject); Object.DestroyImmediate(camera.gameObject);
        }

        private static void CheckVisiblePairPriority(Transform parent)
        {
            var participants = Participants(parent, 4);
            participants[0].root.position = new Vector3(35f, 0f, -.65f);
            participants[1].root.position = new Vector3(35f, 0f, .65f);
            participants[2].root.position = Vector3.left * .65f;
            participants[3].root.position = Vector3.right * .65f;
            Camera camera = View(parent);
            for (int seed = 1; seed <= 12; seed++)
            {
                JourneySocial social = Social(parent); social.viewCamera = camera; social.randomSeed = seed;
                social.Tick(0f, participants); social.Tick(1.2f, participants);
                Require(social.IsParticipating(2) && social.IsParticipating(3) &&
                    !social.IsParticipating(0) && !social.IsParticipating(1),
                    "An off-screen pair consumed the first opportunity while a clear visible pair was available.");
                FacePair(social, participants); social.Tick(1.3f, participants);
                Require(social.ActiveConversation != null && social.CanPresentFrom(camera),
                    "The visible pair was selected but did not start after its real facing preparation.");
                Object.DestroyImmediate(social.gameObject);
            }
            Object.DestroyImmediate(camera.gameObject);
        }

        private static void CheckCanonicalPresentationAndOcclusion(Transform parent)
        {
            var participants = Participants(parent, 2);
            JourneySocial social = Social(parent); Camera camera = View(parent); social.viewCamera = camera;
            social.Tick(0f, participants); social.Tick(1.2f, participants); social.Tick(1.21f, participants);
            Require(social.ActiveConversation != null, "Presentation fixture did not begin an exchange.");
            int speaker = social.ActiveConversation.speakerId, listener = social.ActiveConversation.listenerId;
            Vector3 listenerHead = participants[listener].head.localPosition;
            participants[listener].head.localPosition += Vector3.right * 100f;
            Require(social.CanPresentFrom(camera) && !social.ObserveFrom(camera) && !social.ActiveConversation.observed,
                "An off-screen listener hid a visible speaker or incorrectly supplied a complete language observation.");
            ShowFully(social);
            var background = social.GetComponentInChildren<ProceduralUiGraphic>();
            var glyph = social.GetComponentInChildren<AnimalGlyphGraphic>();
            var shownGlyphs = social.GetComponentsInChildren<AnimalGlyphGraphic>();
            var canvas = social.GetComponentInChildren<Canvas>();
            Require(social.VisibleBubbleCount == 1 && background != null && background.mark == UiMark.AnimalBubble &&
                background.color == ProceduralUiLibrary.Cream && background.waviness == ProceduralUiStyle.Current.waviness &&
                background.bubbleRadius == ProceduralUiStyle.Current.bubbleRadius && background.tailSize == ProceduralUiStyle.Current.tailSize &&
                social.GetComponentsInChildren<Image>(true).Length == 0,
                "Social presentation did not reuse the game's procedural AnimalBubble, cream color and authored silhouette.");
            Require(glyph != null && glyph.color == ProceduralUiLibrary.Ink && glyph.rectTransform.sizeDelta.y >= 40f &&
                shownGlyphs.Length == 2 && shownGlyphs[0].SvgBody != shownGlyphs[1].SvgBody &&
                shownGlyphs[0].rectTransform.anchoredPosition.x < shownGlyphs[1].rectTransform.anchoredPosition.x &&
                shownGlyphs[1].rectTransform.sizeDelta.y >= 40f && background.rectTransform.sizeDelta.x > background.rectTransform.sizeDelta.y &&
                background.rectTransform.sizeDelta.y >= 70f &&
                canvas != null && canvas.worldCamera == camera &&
                canvas.GetComponent<CanvasScaler>().referenceResolution == new Vector2(1600f, 1000f),
                "The reused animal bubble lost readable glyph dimensions, shared ink or the normal gameplay canvas scale.");

            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(blocker, parent.gameObject.scene); blocker.transform.SetParent(parent, true);
            blocker.name = "Actual sight blocker"; blocker.layer = 30;
            Vector3 anchor = participants[speaker].head.position + Vector3.up * .35f;
            blocker.transform.position = (camera.transform.position + anchor) * .5f;
            blocker.transform.localScale = Vector3.one * 2f; social.occlusionMask = 1 << 30;
            Physics.SyncTransforms();
            Require(!social.CanPresentFrom(camera) && !social.ObserveFrom(camera), "A real collider did not occlude the speaker or language evidence.");
            Advance(social, 0f);
            Require(social.VisibleBubbleCount == 0 && Alpha(social) == 0f && !social.ActiveConversation.observed,
                "An occluded word stayed on screen or was silently recorded.");
            Object.DestroyImmediate(blocker); Physics.SyncTransforms();

            // The listener clue obeys the same strict recording rule even though a partial exchange may still be displayed.
            social.ResetSchedule(); social.participantCooldown = social.pairCooldown = 0f;
            social.turnDuration = .2f; social.turnGap = .1f; social.conversationGap = Vector2.one * .1f;
            participants[listener].head.localPosition = listenerHead;
            for (int step = 0; step < 80; step++)
            {
                social.Tick(step * .1f, participants); FacePair(social, participants);
                if (social.ActiveConversation != null && social.ActiveConversation.isClue) break;
            }
            Require(social.ActiveConversation != null && social.ActiveConversation.isClue, "The presentation fixture did not reach a listener clue.");
            listener = social.ActiveConversation.listenerId;
            listenerHead = participants[listener].head.localPosition;
            participants[listener].head.localPosition += Vector3.right * 100f;
            ShowFully(social);
            Require(social.VisibleBubbleCount == 1 && !social.ActiveConversation.observed && social.Completed.Count == 0,
                "Showing a partial listener clue weakened its two-participant observation requirement.");
            participants[listener].head.localPosition = listenerHead;
            participants[0].root.rotation = participants[1].root.rotation = Quaternion.identity;
            Require(!social.ObserveFrom(camera), "A visible but ambiguous listener clue bypassed the facing requirement.");
            FacePair(social, participants);
            Require(social.ObserveFrom(camera), "A clear reciprocal clue could no longer be observed after presentation was separated.");
            Object.DestroyImmediate(social.gameObject); Object.DestroyImmediate(camera.gameObject);
        }

        private static void CheckReplyPlacement(Transform parent)
        {
            var participants = Participants(parent, 2);
            JourneySocial social = Social(parent); Camera camera = View(parent); social.viewCamera = camera;
            var target = new RenderTexture(1600, 1000, 16);
            try
            {
                camera.targetTexture = target; camera.orthographic = true; camera.orthographicSize = 6f;
                camera.transform.rotation = Quaternion.Euler(30f, 35f, 0f);
                camera.transform.position = Vector3.up - camera.transform.forward * 27f;
                social.Tick(0f, participants); social.Tick(1.2f, participants); social.Tick(1.21f, participants);
                social.Tick(4f, participants); social.Tick(4.5f, participants);
                Require(social.ActiveConversation != null && social.ActiveConversation.turnIndex == 1,
                    "The placement fixture did not reach a real reply turn.");
                int speaker = social.ActiveConversation.speakerId, listener = social.ActiveConversation.listenerId;
                Vector3 forward = camera.transform.forward; forward.y = 0f; forward.Normalize();
                participants[speaker].root.position = Vector3.zero;
                participants[listener].root.position = forward * 1.8f;
                FacePair(social, participants);
                ShowFully(social);
                var background = social.GetComponentInChildren<ProceduralUiGraphic>();
                var glyph = social.GetComponentInChildren<AnimalGlyphGraphic>();
                Rect canvasRect = ((RectTransform)social.GetComponentInChildren<Canvas>().transform).rect;
                Vector3 anchor = camera.WorldToViewportPoint(participants[speaker].head.position + Vector3.up * .35f);
                Vector2 original = new Vector2((anchor.x - .5f) * canvasRect.width,
                    (anchor.y - .5f) * canvasRect.height + background.rectTransform.sizeDelta.y * .5f + 12f);
                Vector3 head = camera.WorldToViewportPoint(participants[listener].head.position);
                Vector2 headPoint = new Vector2((head.x - .5f) * canvasRect.width, (head.y - .5f) * canvasRect.height);
                Require(new Rect(original - background.rectTransform.sizeDelta * .5f, background.rectTransform.sizeDelta).Contains(headPoint),
                    "The main-view reply fixture no longer reproduces the listener head covered by the old placement.");
                Vector3 headTop = camera.WorldToViewportPoint(participants[listener].head.position + camera.transform.up * participants[listener].radius);
                Vector2 placed = background.rectTransform.anchoredPosition;
                Require(placed.y - background.rectTransform.sizeDelta.y * .5f > (headTop.y - .5f) * canvasRect.height + 3f &&
                    placed.y > original.y && placed.y - original.y < 100f && Mathf.Abs(placed.x - original.x) < .01f,
                    "The reply still covers its listener's head, moved sideways from its speaker, or jumped excessively far.");
                Require(background.mark == UiMark.AnimalBubble && background.tailSize == ProceduralUiStyle.Current.tailSize &&
                    glyph.rectTransform.sizeDelta.y >= 40f && social.VisibleBubbleCount == 1,
                    "Avoiding the listener changed the original tail, glyph readability or single-bubble presentation.");

                // At the same height beside the speaker, the head no longer intersects the bubble: keep normal placement.
                participants[listener].root.position = camera.transform.right * 1.8f;
                FacePair(social, participants); Advance(social, 0f);
                anchor = camera.WorldToViewportPoint(participants[speaker].head.position + Vector3.up * .35f);
                original = new Vector2((anchor.x - .5f) * canvasRect.width,
                    (anchor.y - .5f) * canvasRect.height + background.rectTransform.sizeDelta.y * .5f + 12f);
                Require(Vector2.Distance(background.rectTransform.anchoredPosition, original) < .01f,
                    "An unobstructed exchange was displaced even though its listener did not overlap the bubble.");
            }
            finally
            {
                camera.targetTexture = null;
                Object.DestroyImmediate(target); Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(social.gameObject);
            }
        }

        private static void CheckCameraFallback(Transform parent)
        {
            JourneySocial social = Social(parent); Camera original = View(parent), replacement = View(parent);
            replacement.gameObject.tag = "MainCamera";
            var target = new RenderTexture(32, 32, 16);
            original.targetTexture = target; social.viewCamera = original;
            Require(social.CurrentViewCamera == original, "An explicit capture camera was replaced by another main camera.");
            original.targetTexture = null;
            if (Camera.main == replacement)
                Require(social.CurrentViewCamera == replacement, "The active main camera did not replace a stale normal view reference.");
            original.enabled = false;
            Require(social.CurrentViewCamera != original, "A disabled saved camera continued to control animal words.");
            social.viewCamera = replacement;
            Require(social.CurrentViewCamera == replacement, "A valid explicit camera could not take over when the old camera was disabled.");
            replacement.enabled = false;
            Require(social.CurrentViewCamera == null, "A disabled camera or another scene's main camera controlled this social view.");
            Object.DestroyImmediate(target); Object.DestroyImmediate(original.gameObject);
            Object.DestroyImmediate(replacement.gameObject); Object.DestroyImmediate(social.gameObject);
        }

        private static void CheckPresentationFade(Transform parent)
        {
            var participants = Participants(parent, 2);
            JourneySocial social = Social(parent); Camera camera = View(parent); social.viewCamera = camera;
            social.turnDuration = .2f; social.turnGap = .1f;
            social.Tick(0f, participants); social.Tick(1.2f, participants); social.Tick(1.21f, participants);
            Require(social.ActiveConversation != null, "Fade fixture must begin a real conversation.");
            Advance(social, 0f); Canvas.ForceUpdateCanvases(); Advance(social, 0f);
            Require(Alpha(social) == 0f && social.VisibleBubbleCount == 0 && !social.ActiveConversation.observed,
                "A new word appeared or entered observation history without any fade time.");
            RejectInvisibleCapture(social, camera);
            Advance(social, .02f);
            Require(Alpha(social) > 0f && Alpha(social) < .2f && social.VisibleBubbleCount == 0 && !social.ActiveConversation.observed,
                "The first 20 ms must animate visually without treating a nearly transparent glyph as visible evidence.");
            RejectInvisibleCapture(social, camera);
            Advance(social, .16f);
            Require(Mathf.Abs(Alpha(social) - 1f) < .001f && social.VisibleBubbleCount == 1 && social.ActiveConversation.observed,
                "A clear word did not become fully visible after the authored 0.18 s fade.");
            var background = social.GetComponentInChildren<ProceduralUiGraphic>(true);
            Vector2 oldAnchor = background.rectTransform.anchoredPosition;
            int oldSpeaker = social.ActiveConversation.speakerId;
            social.Tick(1.43f, participants); Advance(social, .04f);
            Require(social.ActiveConversation == null && Alpha(social) > .5f && Alpha(social) < 1f &&
                background.gameObject.activeSelf && social.VisibleBubbleCount == 0 && Vector2.Distance(oldAnchor, background.rectTransform.anchoredPosition) < .01f,
                "Natural end must retire the old bubble at its original speaker anchor without claiming a current visible word.");
            social.Tick(1.54f, participants); Advance(social, .04f);
            Require(social.ActiveConversation != null && social.ActiveConversation.speakerId != oldSpeaker && !social.ActiveConversation.observed &&
                social.VisibleBubbleCount == 0 && Vector2.Distance(oldAnchor, background.rectTransform.anchoredPosition) < .01f,
                "Changing speakers reused the old visible alpha or moved the retiring bubble to the new speaker.");
            RejectInvisibleCapture(social, camera);
            Advance(social, .10f);
            Require(Alpha(social) < .001f && social.VisibleBubbleCount == 0 && !social.ActiveConversation.observed,
                "Completing the old fade must not spend the same delta time brightening the next speaker.");
            Advance(social, 0f); Advance(social, .02f);
            Require(Alpha(social) > 0f && Alpha(social) < .2f && !social.ActiveConversation.observed,
                "The reply must start its own fade from zero.");
            Advance(social, .16f); Canvas.ForceUpdateCanvases(); Advance(social, 0f);
            Require(social.VisibleBubbleCount == 1 && Mathf.Abs(Alpha(social) - 1f) < .001f &&
                Vector2.Distance(oldAnchor, background.rectTransform.anchoredPosition) > 1f,
                "The fully entered reply did not reach the new speaker anchor.");
            camera.transform.rotation = Quaternion.LookRotation(Vector3.back);
            Advance(social, 0f);
            Require(Alpha(social) == 0f && social.VisibleBubbleCount == 0 && !background.gameObject.activeSelf,
                "Leaving the view must hide immediately, without a fading word floating in an unrelated view.");
            social.ResetSchedule();
            Require(Alpha(social) == 0f && social.VisibleBubbleCount == 0, "Reset retained a fading word.");
            Object.DestroyImmediate(social.gameObject); Object.DestroyImmediate(camera.gameObject);
        }

        private static void RejectInvisibleCapture(JourneySocial social, Camera camera)
        {
            bool rejected = false;
            try { social.CaptureCurrent(camera, null); }
            catch (InvalidOperationException) { rejected = true; }
            catch (ArgumentException) { } // Old implementation reaches path handling after incorrectly observing the word.
            Require(rejected && !social.ActiveConversation.observed,
                "CaptureCurrent bypassed the visible-alpha threshold or recorded evidence before rejecting an invisible word.");
        }

        private static float Alpha(JourneySocial social)
        {
            var background = social.GetComponentInChildren<ProceduralUiGraphic>(true);
            if (!background || !background.gameObject.activeSelf) return 0f;
            var group = background.GetComponent<CanvasGroup>();
            return group ? group.alpha : 1f;
        }

        private static void ShowFully(JourneySocial social)
        {
            Advance(social, 0f); Canvas.ForceUpdateCanvases(); Advance(social, .18f);
            Canvas.ForceUpdateCanvases(); Advance(social, 0f);
        }

        private static void Advance(JourneySocial social, float deltaTime)
        {
            MethodInfo timed = typeof(JourneySocial).GetMethod("UpdatePresentation", BindingFlags.Instance | BindingFlags.NonPublic);
            // The old version still runs its real presenter, so RED is a visible-behaviour failure, not a missing-method exception.
            if (timed != null) timed.Invoke(social, new object[] { deltaTime });
            else typeof(JourneySocial).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(social, null);
        }

        private static JourneySocial Social(Transform parent)
        {
            var root = new GameObject("Social schedule"); root.transform.SetParent(parent, false);
            var social = root.AddComponent<JourneySocial>(); social.occlusionMask = 0;
            return social;
        }

        private static void FacePair(JourneySocial social, JourneySocialParticipant[] participants)
        {
            Transform first = null, second = null;
            foreach (var participant in participants)
            {
                if (participant.id == social.FirstParticipantId) first = participant.root;
                if (participant.id == social.SecondParticipantId) second = participant.root;
            }
            if (first != null && second != null) { first.LookAt(second); second.LookAt(first); }
        }

        private static JourneySocialParticipant[] Participants(Transform parent, int count)
        {
            var result = new JourneySocialParticipant[count];
            for (int i = 0; i < count; i++)
            {
                var root = new GameObject("Participant " + i).transform; root.SetParent(parent, false);
                float angle = i * Mathf.PI * 2f / count;
                root.position = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * .65f;
                root.LookAt(Vector3.zero);
                var head = new GameObject("Head cue").transform; head.SetParent(root, false);
                head.localPosition = new Vector3(0f, 1.1f, .35f);
                result[i] = new JourneySocialParticipant { id = i, root = root, head = head, available = true, radius = .3f };
            }
            return result;
        }

        private static Camera View(Transform parent)
        {
            var root = new GameObject("Observation camera"); root.transform.SetParent(parent, false);
            var camera = root.AddComponent<Camera>(); camera.aspect = 1.6f; camera.transform.position = new Vector3(0f, 20f, -15f);
            camera.transform.LookAt(new Vector3(0f, .9f, 0f)); camera.nearClipPlane = .1f; camera.farClipPlane = 100f;
            return camera;
        }

        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
    }
}
