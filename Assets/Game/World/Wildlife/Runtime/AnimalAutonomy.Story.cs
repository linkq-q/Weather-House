using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WeatherHouse.Life;
using WeatherHousePrototype;

namespace WeatherHouse.Hillside
{
    public sealed partial class AnimalAutonomy
    {
        const string DemoAnimalId = "life.harbor.sheep";
        LifeSession life;
        AnimalStoryWord[] storyWords;
        static bool IsStoryDemo(Animal a) => a.story != null && a.story.animalId == DemoAnimalId;

        void InitializeStory(Animal a)
        {
            if (!world) return;
            if (!life) life = world.GetComponent<LifeSession>();
            AnimalActor actor = a.root.GetComponent<AnimalActor>();
            if (!life || !actor || string.IsNullOrEmpty(actor.animalId)) return;
            a.id = actor.animalId;
            a.story = life.Progress.animalStories.Find(c => c.animalId == a.id);
            if (a.story == null)
            {
                a.story = AnimalStoryState.Create(a.id, a.agent ? a.agent.speciesId : "sheep", a.outgoing,
                    a.agent ? a.agent.timidity : .5f, a.agent ? a.agent.sociability : a.member.sociability, a.id == DemoAnimalId);
                life.Progress.animalStories.Add(a.story);
            }
            a.speaker = actor.GetComponent<LifeSpeaker>();
            a.nextStoryActivity = Time.time + (a.story.HasCompleted("watch_sea") ? 45 : 8);
            if (IsStoryDemo(a)) a.nextAI = Time.time + 2;
        }

        void AddStoryOption(Animal a, List<AnimalOption> options, bool shelterNeeded)
        {
            if (!IsStoryDemo(a) || shelterNeeded || !world.TrySeaWatchPoint(out _, out _) ||
                a.action != "watch_sea" && Time.time < a.nextStoryActivity) return;
            float preference = a.story.preferences.Find(p => p.id == "sea")?.value ?? 0;
            options.Add(new AnimalOption { id = "watch_sea", action = "watch_sea", target = "harbor.tide",
                score = .7f + preference + (a.story.wish == "watch_sea" ? .3f : 0) });
        }

        AnimalStoryWord[] StoryVocabulary(Animal a)
        {
            if (!IsStoryDemo(a) || !life || life.Gameplay?.Catalog?.glyphs == null) return null;
            if (storyWords == null) storyWords = life.Gameplay.Catalog.glyphs.Select(g =>
                new AnimalStoryWord { id = g.id, meaning = g.name + "：" + g.boundary }).ToArray();
            return storyWords;
        }

        bool CanShareStory(Animal a)
        {
            if (!IsStoryDemo(a) || !a.speaker || !a.Available || a.Sleeping || NeedsShelter(a) ||
                Time.time < a.nextStorySpeech || Flat(a.root.position, player.position) > 6 ||
                a.speaker.Story != null || LifeOnboarding.Blocks(life.Gameplay) ||
                LifeOnboardingSpeech.Current(a.speaker.Actor) || life.Gameplay.Judging ||
                life.Gameplay.IsPhotoSpeechHeld(a.speaker.Actor)) return false;
            // Wait for the shared alphabet; never publish invisible substitute words.
            AnimalStoryWord[] words = StoryVocabulary(a);
            return words != null && new[] { "like", "look", "sea", "past" }.All(id => words.Any(w => w.id == id));
        }

        bool StoryRequestPriority(Animal a)
        {
            if (IsStoryDemo(a)) return true;
            foreach (Animal other in animals)
                if (IsStoryDemo(other) && other.Live && other.Available && Time.time >= other.nextAI &&
                    !other.gate.Pending && Flat(other.root.position, player.position) < 24) return false;
            return true;
        }

        void BeginStory(Animal a)
        {
            if (!IsStoryDemo(a)) return;
            a.storyEpisode = a.story.Begin("watch_sea", "harbor.tide");
            a.storySampleAt = Time.time; a.storyPlayerLast = player.position;
            a.actionDuration = 120;
            a.until = Time.time + 120;
            life.SaveAnimalStories();
        }

        bool ExecuteStory(Animal a)
        {
            if (!IsStoryDemo(a) || a.story.active == null) { Finish(a); return true; }
            if(Time.time>=a.until){EndStory(a,"blocked","watch_timeout");Finish(a);return true;}
            float dt = Mathf.Clamp(Time.time - a.storySampleAt, 0, .5f);
            bool playerStill = dt > 0 && Flat(player.position, a.storyPlayerLast) / dt < .75f;
            a.storySampleAt = Time.time; a.storyPlayerLast = player.position;
            if (!world.TrySeaWatchPoint(out Vector3 point, out Vector3 view))
            { EndStory(a, "blocked", "place_missing"); Finish(a); return true; }
            bool at = Flat(a.root.position, point) < .3f && Mathf.Abs(a.root.position.y - point.y) < .5f;
            if (!at) { a.story.Observe(a.storyEpisode, dt, false, false); return false; }
            if (AnimalWorkBudget.TryBegin(AnimalWorkBudget.Kind.Planning, out var work))
            {
                using(work){a.MoveTo(a.root.position, false);a.agent.FaceLifePoint(view, dt);}
            }
            Vector3 facing = view - a.root.position; facing.y = 0;
            bool stopped = a.Speed < .05f && Vector3.Dot(a.root.forward, facing.normalized) > .85f;
            if(stopped)stopped=AnimalStorySightline.IsClear(a.root.position+Vector3.up*.8f,view,a.root,out _);
            if (!stopped) { a.story.Observe(a.storyEpisode, dt, false, false); return true; }
            a.story.Arrive(a.storyEpisode);
            bool near = Flat(a.root.position, player.position) <= 3.5f && Mathf.Abs(a.root.position.y - player.position.y) < 1.5f;
            if (near && Physics.Linecast(a.root.position + Vector3.up * .8f, player.position + Vector3.up,
                out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                near = hit.transform.IsChildOf(a.root) || hit.transform.IsChildOf(player);
            a.story.Observe(a.storyEpisode, dt, true, near, playerStill);
            if (a.story.Complete(a.storyEpisode))
            {
                a.storyEpisode = null; a.nextStoryActivity = Time.time + 120;
                a.nextAI = Time.time + 2; life.SaveAnimalStories(); Finish(a);
            }
            return true;
        }

        void EndStory(Animal a, string outcome, string reason)
        {
            if (string.IsNullOrEmpty(a.storyEpisode) || a.story == null) return;
            bool changed = outcome == "blocked" ? a.story.Block(a.storyEpisode, reason) : a.story.Interrupt(a.storyEpisode, reason);
            a.storyEpisode = null; a.nextStoryActivity = Time.time + 45;
            if (a.speaker) a.speaker.StopStory();
            if (changed && life) life.SaveAnimalStories();
        }

        void ApplyStoryPlan(Animal a, AnimalPlan plan)
        {
            if (a.story == null) return;
            bool changed = plan.reflection != null && a.story.TryApplyReflection(plan.reflection, AnimalStoryRules.PreferenceIds,
                IsStoryDemo(a)?AnimalStoryRules.WishIds:AnimalStoryRules.OrdinaryWishIds);
            if (changed) life.SaveAnimalStories();
            AnimalStorySpeech speech = plan.speech;
            if (speech == null || !CanShareStory(a) || !AnimalStoryRules.ValidateSpeech(speech, Observe(a))) return;
            bool heart = speech.emotion == "heart";
            if (heart && !a.expression.TryIntent(AnimalEmotion.Heart, Time.time, a.emotion.IsPlaying || a.Sleeping)) return;
            string serial = a.story.nextEvent.ToString() + "." + a.gate.Revision;
            a.speaker.PublishStory(new LifeRequest { id = "story." + serial, speakerKey = a.speaker.key, listenerKey = "player",
                condition = "conversation", targetId = speech.subject == "sea" ? "harbor.tide" : "",
                glyphIds = (string[])speech.glyphs.Clone(), context = "动物自主表达", meaning = "" }, 8);
            if (heart) a.emotion.Play(AnimalEmotion.Heart);
            a.nextStorySpeech = Time.time + 60;
        }

        public void RecordEncounter(string id)
        {
            Animal a = animals.Find(v => v.id == id);
            if (a?.story == null || Time.time < a.nextEncounter) return;
            a.nextEncounter = Time.time + 30;
            a.story.RecordEncounter("listened"); a.gate.Invalidate(); a.plan.Clear(); a.nextAI = Time.time + 2;
            if (life) life.SaveAnimalStories();
        }
    }
}
