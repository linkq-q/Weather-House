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
        private int target = -1;
        private bool cycle;

        public void Initialize(Animator animator, WildlifeClips source, float phase)
        {
            Dispose();
            if (animator == null || source == null || source.FirstAvailable == null) return;
            foreach (AnimationClip clip in new[] { source.idle, source.forage, source.walk, source.run,
                source.greet, source.alert, source.takeoff, source.fly, source.land })
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
            }
            graph.Play();
        }

        public void Play(AnimationClip clip, bool repeat, float speed)
        {
            if (!graph.IsValid() || clip == null) return;
            int index = clips.IndexOf(clip);
            if (index < 0) return;
            if (target != index)
            {
                if (!repeat) playables[index].SetTime(0d);
                if (target < 0) mixer.SetInputWeight(index, 1f);
                target = index;
            }
            cycle = repeat;
            playables[target].SetSpeed(speed);
        }

        public void Tick(float dt)
        {
            if (!graph.IsValid() || target < 0) return;
            float blend = 1f - Mathf.Exp(-dt * 12f);
            float weightSum = 0f;
            for (int i = 0; i < clips.Count; i++)
            {
                float weight = Mathf.Lerp(mixer.GetInputWeight(i), i == target ? 1f : 0f, blend);
                mixer.SetInputWeight(i, weight);
                weightSum += weight;
            }
            Debug.Assert(Mathf.Abs(weightSum - 1f) < .001f, "Wildlife animation blend weights must sum to one.");
            double time = playables[target].GetTime();
            float length = clips[target].length;
            if (length > 0f && time >= length)
                playables[target].SetTime(cycle ? time % length : Mathf.Max(0f, length - .001f));
        }

        public void Dispose()
        {
            if (graph.IsValid()) graph.Destroy();
            clips.Clear(); playables.Clear(); target = -1;
        }
    }
}
