using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
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
                return "PASS 6 social checks: weighted rotation and replay; clock reset and deterministic restart; mutual reservation and cooldown; approach/facing preparation, timeout and interruption; head-anchored visible-only context snapshots; legal glyphs and explicit listeners.";
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
            Require(recorded.glyphIds[0] == "hello" && recorded.speakerId != recorded.listenerId && recorded.turnIndex == 1 &&
                recorded.observed && recorded.speakerPosition != recorded.listenerPosition,
                "Recorded evidence lost its immutable words, participants, turn or spatial context.");
            JourneyConversation restored = JsonUtility.FromJson<JourneyConversation>(JsonUtility.ToJson(recorded));
            Require(restored.sequenceId == recorded.sequenceId && restored.speakerId == recorded.speakerId &&
                restored.listenerId == recorded.listenerId && restored.glyphIds[0] == "hello", "Saved context did not retain the original speaker/listener.");
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
                if (!active.isClue) { sawGreeting |= active.glyphIds.Length == 1 && active.glyphIds[0] == "hello"; continue; }
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
            var camera = root.AddComponent<Camera>(); camera.transform.position = new Vector3(0f, 20f, -15f);
            camera.transform.LookAt(new Vector3(0f, .9f, 0f)); camera.nearClipPlane = .1f; camera.farClipPlane = 100f;
            return camera;
        }

        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
    }
}
