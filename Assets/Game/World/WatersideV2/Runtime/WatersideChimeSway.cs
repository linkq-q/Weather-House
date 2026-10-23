using UnityEngine;

namespace WeatherHouse.WatersideV2
{
    public sealed class WatersideChimeSway : MonoBehaviour
    {
        public float phase;
        Quaternion rest;
        void Start() { rest = transform.localRotation; }
        void LateUpdate()
        {
            float t = Time.time;
            transform.localRotation = rest * Quaternion.Euler(Mathf.Sin(t * .8f + phase) * 4, 0, Mathf.Sin(t * .63f + phase) * 3);
        }
    }
}
