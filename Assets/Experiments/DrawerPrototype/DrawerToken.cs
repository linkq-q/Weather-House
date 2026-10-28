using UnityEngine;

namespace WeatherHouse.DrawerPrototype
{
    public sealed class DrawerToken : MonoBehaviour
    {
        public int item = -1;
        public Rigidbody body;
        public BoxCollider hitBox;
        public Vector3 dimensions;
        public bool docked;
        public bool IsPaper => item < 0;
    }
}
