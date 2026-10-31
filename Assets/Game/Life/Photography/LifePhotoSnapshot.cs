using System;
using UnityEngine;

namespace WeatherHouse.Life
{
    [Serializable]
    public sealed class LifePublicContext
    {
        public string id, kind, actorId, listenerId, targetId, description, occurredUtc;
        public string[] glyphIds=Array.Empty<string>();
        public string[] relatedContextIds=Array.Empty<string>();
        public Vector3 anchor, origin;
        public bool completed;
        public LifePublicContext Copy()=>JsonUtility.FromJson<LifePublicContext>(JsonUtility.ToJson(this));
    }

    [Serializable]
    public sealed class LifePhotoEntity
    {
        public string id, kind, speciesId, activityId, activityKind;
        public Vector3 position;
        public bool bodyVisible, mouthVisible, foodVisible, waterSurfaceVisible, wetSurfaceVisible;
        public bool windEffectVisible, rainVisible, sunContrastVisible, environmentVisible;
        public bool sleeping, eating;
        public float wetness, water;
        public int rainStrength, windStrength;
    }

    [Serializable]
    public sealed class LifePhotoSnapshot
    {
        public int version=1;
        public string capturedUtc, visibilityError;
        public Vector3 cameraPosition, cameraForward;
        public LifePhotoEntity[] entities=Array.Empty<LifePhotoEntity>();
        public LifePublicContext[] contexts=Array.Empty<LifePublicContext>();
        public LifePhotoSnapshot Copy()=>JsonUtility.FromJson<LifePhotoSnapshot>(JsonUtility.ToJson(this));
    }

    [Serializable]
    public sealed class LifePhotoTask
    {
        public string id, title, mode, action, accept, recovery;
        public string[] tokens, rewards;
        public bool IsInteraction=>mode=="互动情境";
    }
}
