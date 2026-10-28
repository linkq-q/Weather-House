using UnityEngine;

namespace WeatherHouse.DrawerPrototype
{
    public sealed class DrawerFoliageSway : MonoBehaviour
    {
        public float phase;
        public float strength = 1.5f;
        Quaternion rest;
        void Start() { rest = transform.localRotation; }
        void Update()
        {
            float time = Time.time*.65f+phase;
            transform.localRotation = rest*Quaternion.Euler(Mathf.Sin(time*.71f)*strength*.6f,Mathf.Sin(time*.43f)*strength*.35f,Mathf.Sin(time)*strength);
        }
    }
}
