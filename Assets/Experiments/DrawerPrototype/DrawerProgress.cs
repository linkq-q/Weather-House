using System;
using UnityEngine;

namespace WeatherHouse.DrawerPrototype
{
    [Serializable]
    public sealed class DrawerProgress
    {
        public bool[] collected = new bool[5];
        public bool[] read = new bool[5];

        public int Count
        {
            get { int count = 0; foreach (bool value in collected) if (value) count++; return count; }
        }

        public bool Complete
        {
            get
            {
                for (int i = 0; i < 5; i++) if (!collected[i] || !read[i]) return false;
                return true;
            }
        }

        public void Normalize()
        {
            if (collected == null) collected = new bool[5];
            if (read == null) read = new bool[5];
            Array.Resize(ref collected, 5);
            Array.Resize(ref read, 5);
            for (int i = 0; i < 5; i++) read[i] &= collected[i];
        }

        public static DrawerProgress Restore(string json)
        {
            var result = JsonUtility.FromJson<DrawerProgress>(json) ?? new DrawerProgress();
            result.Normalize();
            return result;
        }
    }
}
