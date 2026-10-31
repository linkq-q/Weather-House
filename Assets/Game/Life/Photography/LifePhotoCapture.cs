using System;
using UnityEngine;

namespace WeatherHouse.Life
{
    public static partial class LifePhotoCapture
    {
#if UNITY_EDITOR
        internal static Action BeforeDepthQuery;
#endif
        public static LifePhotoSnapshot Capture(LifeSession session,Camera camera)
        {
            var snapshot=new LifePhotoSnapshot{capturedUtc=DateTime.UtcNow.ToString("o")};
            if(!session || !session.World || !camera)return snapshot;
            snapshot.cameraPosition=camera.transform.position;snapshot.cameraForward=camera.transform.forward;
            try
            {
#if UNITY_EDITOR
                BeforeDepthQuery?.Invoke();
#endif
                using(LifePhotoDepth.Begin(camera))CaptureWorld(session,camera,snapshot);
            }
            catch(Exception error)
            {
                snapshot.entities=Array.Empty<LifePhotoEntity>();snapshot.contexts=Array.Empty<LifePublicContext>();
                snapshot.visibilityError=error.Message;
                Debug.LogWarning("Photograph visibility capture failed; the photograph can still be saved. "+error);
            }
            return snapshot;
        }
        static partial void CaptureWorld(LifeSession session,Camera camera,LifePhotoSnapshot snapshot);
    }
}
