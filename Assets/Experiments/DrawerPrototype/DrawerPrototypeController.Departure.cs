using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WeatherHouse.DrawerPrototype
{
    public sealed partial class DrawerPrototypeController
    {
        public const string WorldScene = "Assets/Scenes/WeatherHouse_WatersideV2.unity";
        public bool TryRestartOpening()
        {
            if (busy || selected || readOnly || Progress == null || Progress.Count == 0) return false;
            DrawerProgress previous = Progress;
            Progress = new DrawerProgress(); Save();
            if (readOnly) { Progress = previous; RefreshCompletion(); return false; }
            busy = true;
            SceneManager.LoadSceneAsync(gameObject.scene.path, LoadSceneMode.Single);
            return true;
        }
        public bool TryLeave()
        {
            if (busy || selected || Progress == null || !Progress.Complete) return false;
            if (!Application.CanStreamedLevelBeLoaded(WorldScene))
            {
                completePanel.GetComponentInChildren<Text>().text = "暂时无法出发，请稍后重试。";
                Debug.LogError("The playable world must be enabled in Build Settings.", this); return false;
            }
            busy = true; StartCoroutine(Leave()); return true;
        }
        IEnumerator Leave()
        {
            var rect = UiRect("Departure fade", canvas.transform, Vector2.zero, Vector2.zero);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var fade = rect.gameObject.AddComponent<Image>(); fade.color = new Color(.96f, .95f, .88f, 0);
            float elapsed = 0;
            while (elapsed < .45f)
            {
                elapsed += Mathf.Min(Time.unscaledDeltaTime, .05f);
                fade.color = new Color(.96f, .95f, .88f, Mathf.SmoothStep(0, 1, elapsed / .45f)); yield return null;
            }
            yield return SceneManager.LoadSceneAsync(WorldScene, LoadSceneMode.Single);
        }
    }
}
