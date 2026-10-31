using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace WeatherHouse.Hillside
{
    internal sealed class WildlifeAnimation
    {
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private readonly List<AnimationClip> clips = new List<AnimationClip>();
        private readonly List<AnimationClipPlayable> playables = new List<AnimationClipPlayable>();
        private readonly List<float> weights = new List<float>();
        private int target = -1;
        private bool cycle;

        public void Initialize(Animator animator, WildlifeClips source, float phase, AnimationClip additionalClip = null)
        {
            Dispose();
            if (animator == null || source == null || source.FirstAvailable == null) return;
            foreach (AnimationClip clip in new[] { source.idle, source.forage, source.walk, source.run,
                source.greet, source.alert, source.takeoff, source.fly, source.land, source.restDown, source.restSleep, source.restUp, additionalClip })
                if (clip != null && !clips.Contains(clip)) clips.Add(clip);
            animator.applyRootMotion = false;
            graph = PlayableGraph.Create("Wildlife " + animator.name);
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(graph, clips.Count);
            var output = AnimationPlayableOutput.Create(graph, "Skeleton", animator);
            output.SetSourcePlayable(mixer);
            for (int i = 0; i < clips.Count; i++)
            {
                var playable = AnimationClipPlayable.Create(graph, clips[i]);
                playable.SetApplyFootIK(false);
                playable.SetTime(phase * clips[i].length);
                graph.Connect(playable, 0, mixer, i);
                playables.Add(playable);
                weights.Add(0f);
            }
            graph.Play();
        }

        public void Play(AnimationClip clip, bool repeat, float speed, float startTime = 0f)
        {
            if (!graph.IsValid() || clip == null) return;
            int index = target >= 0 && clips[target] == clip ? target : clips.IndexOf(clip);
            if (index < 0) return;
            if (target != index)
            {
                if (!repeat) playables[index].SetTime(Mathf.Clamp(startTime, 0f, clip.length));
                if (target < 0) { mixer.SetInputWeight(index, 1f); weights[index] = 1f; }
                target = index;
            }
            cycle = repeat;
            playables[target].SetSpeed(speed);
        }

        public void RegisterClip(AnimationClip clip)
        {
            if(!clip || !graph.IsValid() || clips.Contains(clip))return;
            int index=clips.Count;clips.Add(clip);mixer.SetInputCount(clips.Count);
            var playable=AnimationClipPlayable.Create(graph,clip);playable.SetApplyFootIK(false);
            graph.Connect(playable,0,mixer,index);mixer.SetInputWeight(index,0);
            playables.Add(playable);weights.Add(0);
        }

        public void Tick(float dt)
        {
            if (!graph.IsValid() || target < 0) return;
            float blend = 1f - Mathf.Exp(-dt * 12f);
            float weightSum = 0f;
            for (int i = 0; i < clips.Count; i++)
            {
                float weight = Mathf.Lerp(weights[i], i == target ? 1f : 0f, blend);
                if (weight != weights[i]) { mixer.SetInputWeight(i, weight); weights[i] = weight; }
                weightSum += weight;
            }
            Debug.Assert(Mathf.Abs(weightSum - 1f) < .001f, "Wildlife animation blend weights must sum to one.");
            double time = playables[target].GetTime();
            float length = clips[target].length;
            if (!cycle && time < 0d) playables[target].SetTime(0d);
            if (length > 0f && time >= length)
                playables[target].SetTime(cycle ? time % length : Mathf.Max(0f, length - .001f));
        }

        public bool HasReachedBoundary(AnimationClip clip, float direction)
        {
            if (!graph.IsValid() || clip == null) return true;
            if (target < 0 || clips[target] != clip) return false;
            double time = playables[target].GetTime();
            return direction < 0f ? time <= 0d : time >= clip.length - .002d;
        }

        public float WeightOf(AnimationClip clip)
        {
            int index = clip == null ? -1 : clips.IndexOf(clip);
            return index >= 0 && index < weights.Count ? weights[index] : 0f;
        }

        public void SetPaused(bool paused)
        {
            if (!graph.IsValid()) return;
            if (paused) graph.Stop(); else graph.Play();
        }

        public void Dispose()
        {
            if (graph.IsValid()) graph.Destroy();
            clips.Clear(); playables.Clear(); weights.Clear(); target = -1;
        }
    }
}
