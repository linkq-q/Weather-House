using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[System.Serializable]
[TrackBindingType(typeof(DirectionalLightController))]
[TrackColor(0.53f, 0.0f, 0.08f)]
public class DirectionalLightTrack : TrackAsset
{
    public DirectionalLightTrackBehaviour template = new();

    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
    {
        TODSystemManager.directionalLightTB = template;
        return ScriptPlayable<DirectionalLightTrackBehaviour>.Create(graph, template, inputCount);
    }
}
