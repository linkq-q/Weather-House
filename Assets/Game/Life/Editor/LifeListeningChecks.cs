using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.Life.Editor
{
    public static class LifeListeningChecks
    {
        [MenuItem("Weather House/Life/Check listening and task acceptance")]
        public static void Run()
        {
            foreach (string line in CheckModel()) Debug.Log(line);
        }

#if UNITY_EDITOR
        [MenuItem("Weather House/Review 20261027/Check listening isolated")]
        public static void RunPlay()
        {
            var life = UnityEngine.Object.FindObjectOfType<LifeSession>();
            if (!Application.isPlaying || !life || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")) ||
                !System.IO.Path.GetFullPath(WeatherHousePrototype.GameplaySession.PersistenceDirectory).StartsWith(
                    System.IO.Path.GetFullPath("LifeGameplayReview") + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Listening Play checks require an isolated review save.");
            var game = life.Gameplay;
            var hud = game.GetComponent<WeatherHousePrototype.GameplayHud>();
            var dog = life.World.FindSpeaker("dog");
            if (!hud || !dog || hud.BlocksMovement || hud.CameraMode || hud.IsAlbumOpening || game.Recorder.IsRecording || LifeOnboarding.Blocks(game))
                throw new InvalidOperationException("Listening Play checks require idle gameplay.");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var observed = game.GetType().GetProperty("Observed");
            var composition = hud.GetType().GetField("lastCompositionFrame", flags);
            var journal = hud.GetType().GetProperty("JournalOpen");
            string[] sessionFields = { "lastUtterance", "conversationRevision", "listenedAnimal", "listeningUntil" };
            var sessionState = sessionFields.ToDictionary(name => name, name => game.GetType().GetField(name, flags).GetValue(game));
            string progress = JsonUtility.ToJson(life.Progress), world = life.World.CaptureJson(), notebook = JsonUtility.ToJson(game.Notebook), status = game.Status;
            var oldObserved = game.Observed; int oldTopic = dog.topicIndex, oldComposition = (int)composition.GetValue(hud);
            Vector3 position = game.PlayerTransform.position; Quaternion rotation = game.PlayerTransform.rotation;
            var lines = new List<string>(CheckModel());
            void Check(bool value, string label)
            {
                if (!value) throw new InvalidOperationException(label);
                lines.Add("PASS " + label);
            }
            try
            {
                life.Progress.heardRequests.Clear(); life.Progress.understoodRequests.Clear(); life.Progress.tasks.Clear();
                game.Notebook.encounters.Clear();
                composition.SetValue(hud, -10);
                game.PlayerTransform.position = dog.transform.position + Vector3.right * 1.5f;
                Physics.SyncTransforms(); observed.SetValue(game, dog.Actor);
                dog.topicIndex = Array.FindIndex(dog.requests, r => r.id == "dog.hello");
                string confirmed = string.Join(",", game.Notebook.words.Where(w => w.confirmed).Select(w => w.glyphId).OrderBy(id => id));
                hud.InteractPrimary();
                Check(life.Progress.HasHeard("dog.hello") && life.Progress.tasks.Count == 0, "E hears a greeting without creating a help task");
                string utterance = dog.Actor.UtteranceId;
                Check(dog.Current?.id == "dog.hello" && game.Notebook.encounters.All(e => e.utteranceId == utterance &&
                    dog.Current.glyphIds.Contains(e.glyphId)), "First E retains the greeting identity and its actual glyph snapshot");
                Check(!life.Listen(dog.Actor, utterance + ".stale") && life.Progress.tasks.Count == 0, "Stale utterances cannot accept help");
                journal.SetValue(hud, true); hud.InteractPrimary();
                Check(!life.Listen(dog.Actor, utterance) && life.Progress.tasks.Count == 0, "An open journal blocks E and direct listening");
                journal.SetValue(hud, false);
                hud.SetCameraMode(true, true); hud.InteractPrimary();
                Check(!life.Listen(dog.Actor, utterance) && life.Progress.tasks.Count == 0, "Camera mode blocks E and direct listening");
                hud.SetCameraMode(false, true);
                life.SetGestureInput(true); hud.InteractPrimary();
                Check(!life.Listen(dog.Actor, utterance) && life.Progress.tasks.Count == 0, "Gesture drawing blocks E and direct listening");
                life.SetGestureInput(false);
                composition.SetValue(hud, Time.frameCount); hud.InteractPrimary();
                Check(!life.Listen(dog.Actor, utterance) && life.Progress.tasks.Count == 0, "IME commit frames cannot trigger listening");
                composition.SetValue(hud, -10);
                game.PlayerTransform.position = dog.transform.position + Vector3.right * 20f; hud.InteractPrimary();
                Check(!life.Listen(dog.Actor, utterance) && life.Progress.tasks.Count == 0, "A distant animal cannot give a task");
                game.PlayerTransform.position = dog.transform.position + Vector3.right * 1.5f; Physics.SyncTransforms();
                Check(dog.Current?.id == "dog.hello", "Blocked listening never advances the topic");
                hud.InteractPrimary(); game.RefreshInputBlocking();
                var task = life.Progress.FindTask("dog.rain.lesson");
                Check(dog.Current?.id == "dog.rain.lesson" && task != null && task.status == LifeTaskStatus.Active,
                    "A second E reaches and accepts rain without a manually selected topic");
                Check(game.Notebook.encounters.Where(e => e.utteranceId == dog.Actor.UtteranceId).Select(e => e.glyphId).OrderBy(id => id)
                    .SequenceEqual(dog.Current.glyphIds.OrderBy(id => id)) && !game.Notebook.encounters.Any(e => e.utteranceId == utterance && e.glyphId == "rain"),
                    "Advanced listening records rain under its own utterance identity, never under the greeting");
                string baseline = life.World.CaptureJson(); long accepted = task.acceptedUtcTicks;
                hud.InteractPrimary();
                Check(life.Progress.tasks.Count == 1 && task.acceptedUtcTicks == accepted && life.World.CaptureJson() == baseline,
                    "Repeated E preserves task identity and the world action baseline");
                Check(!hud.IsDialogueOpen && hud.DialogueTarget == null && !hud.BlocksMovement && !game.Judging &&
                    (!game.forestSprite || !game.forestSprite.InputBlocked) && (!game.walker || !game.walker.InputBlocked) &&
                    !hud.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Chinese reply" || t.name == "Player reply bubble" || t.name == "Player spoken reply"),
                    "Listening creates no player reply, network judgement or movement lock");
                Check(game.ShouldShowAnimalUtterance(dog.Actor) && life.TaskText.Contains("雨"), "Animal speech and task explanations remain available");
                Check(confirmed == string.Join(",", game.Notebook.words.Where(w => w.confirmed).Select(w => w.glyphId).OrderBy(id => id)) && life.Progress.understoodRequests.Count == 0,
                    "Listening leaves all confirmed word meanings unchanged");
                task.status = LifeTaskStatus.Complete; // Completed-help fixture; this does not claim a physical weather result.
                hud.InteractPrimary();
                Check(dog.Current?.id == "dog.water" && life.Progress.FindTask("dog.water")?.status == LifeTaskStatus.Active &&
                    task.status == LifeTaskStatus.Complete && task.acceptedUtcTicks == accepted,
                    "The next E leaves completed help intact and reaches the newly available request");
                CheckTemporaryGreetings(life, lines);
                lines.Add("Boundary: actual E handler and live world, arranged observed target/player pose; no physical-key or visual-acceptance claim.");
            }
            catch (Exception error) { lines.Add("FAIL " + error); throw; }
            finally
            {
                journal.SetValue(hud, false); hud.SetCameraMode(false, true); life.SetGestureInput(false);
                JsonUtility.FromJsonOverwrite(progress, life.Progress); JsonUtility.FromJsonOverwrite(notebook, game.Notebook);
                life.World.RestoreJson(world); dog.topicIndex = oldTopic; observed.SetValue(game, oldObserved);
                foreach (var entry in sessionState) game.GetType().GetField(entry.Key, flags).SetValue(game, entry.Value);
                composition.SetValue(hud, oldComposition); game.PlayerTransform.SetPositionAndRotation(position, rotation); Physics.SyncTransforms();
                game.SaveNotebook(); life.SaveAndNotify(""); game.Notify(status); game.RefreshInputBlocking();
                string folder = "LifeGameplayReview/20261028/feedback/listening"; System.IO.Directory.CreateDirectory(folder);
                System.IO.File.WriteAllLines(System.IO.Path.Combine(folder, "play-checks.txt"), lines);
            }
            Debug.Log(string.Join("\n", lines));
        }

        static void CheckTemporaryGreetings(LifeSession life, List<string> lines)
        {
            var game = life.Gameplay; var hud = game.GetComponent<WeatherHousePrototype.GameplayHud>();
            var dog = life.World.FindSpeaker("dog"); var sheep = life.World.FindSpeaker("sheep");
            var tutorial = game.GetComponent<LifeOnboarding>();
            if (!tutorial || !sheep) throw new InvalidOperationException("Temporary greeting checks require the tutorial owner and two animals.");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var state = typeof(LifeOnboarding).GetProperty("State"); var oldState = state.GetValue(tutorial);
            var suppressed = typeof(LifeOnboarding).GetField("reviewSuppressed", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var oldSuppressed = suppressed.GetValue(null);
            var observed = game.GetType().GetProperty("Observed"); var journal = hud.GetType().GetProperty("JournalOpen");
            var speakers = new[] { dog, sheep }; int sheepTopic = sheep.topicIndex;
            var speeches = speakers.Select(s => s.GetComponent<LifeOnboardingSpeech>()).ToArray();
            var created = speeches.Select(s => !s).ToArray();
            var speechFields = typeof(LifeOnboardingSpeech).GetFields(flags | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly);
            var snapshots = speeches.Select(s => s ? speechFields.Select(f => f.GetValue(s)).ToArray() : null).ToArray();
            void Check(bool value, string label)
            {
                if (!value) throw new InvalidOperationException(label);
                lines.Add("PASS " + label);
            }
            void Focus(LifeSpeaker speaker)
            {
                game.PlayerTransform.position = speaker.transform.position + Vector3.right * 1.5f;
                Physics.SyncTransforms(); observed.SetValue(game, speaker.Actor);
            }
            try
            {
                state.SetValue(tutorial, new LifeOnboardingState { started = true, stage = LifeOnboardingStage.Complete });
                suppressed.SetValue(null, false);
                life.Progress.heardRequests.Clear(); life.Progress.tasks.Clear(); game.Notebook.encounters.Clear();
                dog.topicIndex = Array.FindIndex(dog.requests, r => r.id == "dog.hello");
                for (int i = 0; i < speeches.Length; i++)
                {
                    if (!speeches[i]) speeches[i] = speakers[i].gameObject.AddComponent<LifeOnboardingSpeech>();
                    speeches[i].owner = tutorial;
                    typeof(LifeOnboardingSpeech).GetField("serial", flags).SetValue(speeches[i], 0);
                    speeches[i].Speak("hello", 9);
                }
                string dogGreeting = speeches[0].Utterance, sheepGreeting = speeches[1].Utterance;
                Check(speeches.All(s => s.Request.glyphIds.SequenceEqual(new[] { "you", "hello" })),
                    "Temporary greetings say you followed by the greeting glyph");
                Check(speeches[0].Request.id == speeches[1].Request.id && dogGreeting != sheepGreeting,
                    "Temporary greeting fixture shares request IDs but retains distinct animal utterance identities");
                Focus(dog); journal.SetValue(hud, true); hud.InteractPrimary(); journal.SetValue(hud, false);
                Check(speeches[0].Active && !life.Progress.HasHeard("dog.hello"), "Blocked E does not consume the temporary greeting");
                hud.InteractPrimary();
                Check(speeches[0].Active && dog.Actor.UtteranceId == dogGreeting && !life.Progress.HasHeard("dog.hello") &&
                    life.Progress.tasks.Count == 0 && game.Notebook.encounters.Any(e => e.utteranceId == dogGreeting),
                    "First E hears only the temporary greeting and does not complete the authored dog greeting");
                CheckGreetingPhotoContext(life, hud, dog.Actor, speeches[0]);
                lines.Add("PASS A composed temporary greeting retains hello photo context and qualifies for P20");
                Focus(sheep); hud.InteractPrimary();
                Check(speeches[1].Active && sheep.Actor.UtteranceId == sheepGreeting && life.Progress.tasks.Count == 0 &&
                    game.Notebook.encounters.Any(e => e.utteranceId == sheepGreeting),
                    "Another animal's equal temporary request ID does not skip its first greeting");
                Focus(dog); hud.InteractPrimary();
                Check(!speeches[0].Active && dog.Current?.id == "dog.hello" && life.Progress.HasHeard("dog.hello") && life.Progress.tasks.Count == 0 &&
                    game.Notebook.encounters.Where(e => e.utteranceId == dog.Actor.UtteranceId).Select(e => e.glyphId).OrderBy(id => id)
                        .SequenceEqual(dog.Current.glyphIds.OrderBy(id => id)),
                    "Second E ends the temporary greeting and explicitly hears the authored dog greeting with its own glyph snapshot");
                hud.InteractPrimary();
                var task = life.Progress.FindTask("dog.rain.lesson");
                Check(dog.Current?.id == "dog.rain.lesson" && task?.status == LifeTaskStatus.Active,
                    "Third E reaches and accepts rain after the temporary and authored greetings");
                long accepted = task.acceptedUtcTicks; string baseline = life.World.CaptureJson();
                speeches[0].Speak("hello", 9);
                Check(!life.Listen(dog.Actor, dogGreeting), "A stale temporary utterance cannot consume a new greeting");
                hud.InteractPrimary();
                Check(speeches[0].Active, "A new temporary utterance still needs its own first E");
                hud.InteractPrimary();
                Check(!speeches[0].Active && dog.Current?.id == "dog.rain.lesson" && task.acceptedUtcTicks == accepted &&
                    life.Progress.tasks.Count == 1 && life.World.CaptureJson() == baseline,
                    "Leaving a temporary greeting resumes active help without resetting its acceptance or world baseline");
            }
            finally
            {
                journal.SetValue(hud, false);
                for (int i = 0; i < speeches.Length; i++)
                    if (created[i]) { if (speeches[i]) UnityEngine.Object.DestroyImmediate(speeches[i]); }
                    else for (int j = 0; j < speechFields.Length; j++) speechFields[j].SetValue(speeches[i], snapshots[i][j]);
                sheep.topicIndex = sheepTopic; state.SetValue(tutorial, oldState); suppressed.SetValue(null, oldSuppressed);
            }
        }

        static void CheckGreetingPhotoContext(LifeSession life, WeatherHousePrototype.GameplayHud hud,
            WeatherHousePrototype.AnimalActor actor, LifeOnboardingSpeech speech)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var contexts = (List<LifePublicContext>)typeof(LifeWorld).GetField("photoContexts", flags).GetValue(life.World);
            var until = (Dictionary<string, float>)typeof(LifeWorld).GetField("photoContextUntil", flags).GetValue(life.World);
            var oldContexts = contexts.ToArray(); var oldUntil = new Dictionary<string, float>(until);
            var view = life.Gameplay.CurrentView; Vector3 position = view.transform.position; Quaternion rotation = view.transform.rotation;
            CanvasGroup group = null;
            try
            {
                // Arrange a visible, fully faded-in bubble; this is a context check, not a screenshot acceptance.
                view.transform.position = actor.BubbleAnchor + Vector3.up * .2f + Vector3.back * 1.5f;
                view.transform.LookAt(actor.BubbleAnchor);
                hud.GetType().GetMethod("ClearAnimalBubble", flags).Invoke(hud, null);
                hud.GetType().GetMethod("UpdateAnimalBubble", flags).Invoke(hud, null);
                group = (CanvasGroup)hud.GetType().GetField("bubbleGroup", flags).GetValue(hud);
                if (!group) throw new InvalidOperationException("The composed greeting fixture did not create its animal bubble.");
                group.alpha = 1f;
                life.World.ObserveOnboardingUtterance(actor, speech);
                var context = contexts.SingleOrDefault(c => c.id == speech.Utterance);
                if (context == null || context.kind != "hello" || !context.glyphIds.SequenceEqual(new[] { "you", "hello" }) ||
                    !LifePhotoRules.Match(new LifePhotoSnapshot { entities = new[] { new LifePhotoEntity { id = actor.animalId, kind = "animal", bodyVisible = true } },
                        contexts = new[] { context } }, Array.Empty<LifePublicContext>()).Contains("P20"))
                    throw new InvalidOperationException("A composed temporary greeting must retain hello photo context and qualify for P20.");
            }
            finally
            {
                if (group) group.alpha = 0f; view.transform.SetPositionAndRotation(position, rotation);
                contexts.Clear(); contexts.AddRange(oldContexts); until.Clear(); foreach (var entry in oldUntil) until.Add(entry.Key, entry.Value);
            }
        }
#endif

        public static string[] CheckModel()
        {
            var passed = new List<string>();
            void Check(bool value, string label)
            {
                if (!value) throw new InvalidOperationException(label);
                passed.Add("PASS " + label);
            }
            // Reflection lets the same checks establish RED against the previous model.
            var hear = typeof(LifeProgress).GetMethod("Hear");
            var hasHeard = typeof(LifeProgress).GetMethod("HasHeard");
            Check(hear != null && hasHeard != null, "Listening has a task entry point independent of semantic judgement");
            bool Hear(LifeProgress progress, LifeRequest request, string animal, bool current = true, bool nearby = true) =>
                (bool)hear.Invoke(progress, new object[] { request, animal, current, nearby });
            bool Heard(LifeProgress progress, string id) => (bool)hasHeard.Invoke(progress, new object[] { id });

            var content = LifeContent.Build();
            var greeting = content.Single(r => r.id == "dog.hello");
            Check(greeting.glyphIds.SequenceEqual(new[] { "you", "hello", "want" }) &&
                content.Single(r => r.id == "fox.hello").glyphIds.SequenceEqual(new[] { "you", "hello" }),
                "Authored dog and fox greetings compose you plus greeting while retaining the dog's wish");
            var rain = content.Single(r => r.id == "dog.rain.lesson");
            var progress = new LifeProgress();
            Check(!Hear(progress, null, "dog") && !Hear(progress, rain, "") &&
                !Hear(progress, rain, "dog", false) && !Hear(progress, rain, "dog", true, false) &&
                !Heard(progress, rain.id) && progress.tasks.Count == 0,
                "Invalid, stale and distant listening cannot record a request or accept help");
            Check(!progress.CanPresent(rain, content), "The unheard greeting still gates the next authored request");
            Check(!Hear(progress, greeting, "dog") && Heard(progress, greeting.id) && progress.tasks.Count == 0,
                "Hearing a greeting advances conversation without creating an action task");
            Check(progress.CanPresent(rain, content), "Hearing the greeting opens the rain request without guessing");
            Check(Hear(progress, rain, "dog") && progress.FindTask(rain.id)?.status == LifeTaskStatus.Active,
                "Hearing the current request accepts its action task without an answer");
            var task = progress.FindTask(rain.id);
            long accepted = task.acceptedUtcTicks;
            Check(!Hear(progress, rain, "dog") && progress.tasks.Count == 1 &&
                ReferenceEquals(task, progress.FindTask(rain.id)) && task.acceptedUtcTicks == accepted,
                "Repeated listening preserves the original task and acceptance time");
            Check(progress.understoodRequests.Count == 0, "Listening does not mark semantic understanding");
            Check(!progress.Complete(rain.id, false, true) && !progress.Complete(rain.id, true, false),
                "Hearing a task does not complete it without the nearby world action");
            Check(progress.Complete(rain.id, true, true) && progress.EventComplete("E0", content),
                "The physical result completes help and opens the next event");
            Check(!Hear(progress, rain, "dog") && task.status == LifeTaskStatus.Complete,
                "Listening again never reopens completed help");
            var restored = JsonUtility.FromJson<LifeProgress>(JsonUtility.ToJson(progress));
            restored.Normalize();
            Check(Heard(restored, greeting.id) && Heard(restored, rain.id) && restored.EventComplete("E0", content),
                "Heard requests and completed help survive serialization");
            var legacy = JsonUtility.FromJson<LifeProgress>("{\"understoodRequests\":[\"dog.hello\",\"dog.hello\",\"\"],\"tasks\":[{\"requestId\":\"dog.rain.lesson\",\"animalId\":\"dog\",\"status\":1,\"acceptedUtcTicks\":123}]}");
            legacy.Normalize(); legacy.Normalize();
            Check(Heard(legacy, greeting.id) && Heard(legacy, rain.id) && legacy.EventComplete("E0", content) &&
                legacy.FindTask(rain.id).acceptedUtcTicks == 123 && legacy.understoodRequests.Count == 3,
                "Legacy understanding and task records migrate without altering their original data");
            Check(LifeInteractionRules.Primary(true, true, true) == LifePrimaryAction.None &&
                LifeInteractionRules.Primary(false, true, true) == LifePrimaryAction.Talk &&
                LifeInteractionRules.Primary(false, false, true) == LifePrimaryAction.Gather,
                "Blocked input stays blocked and listening retains priority over gathering");
            return passed.ToArray();
        }
    }
}
