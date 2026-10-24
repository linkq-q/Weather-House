using System;
using UnityEngine;

namespace WeatherHouse.Hillside
{
    // Editor-baked original sole points. Runtime never needs a readable copy of the animal mesh.
    public sealed class WildlifeForageContactData : ScriptableObject
    {
        [Serializable] public sealed class Point
        {
            public Vector3 a, b, c, d;
            public int boneA, boneB, boneC, boneD;
            public Vector4 weight;
        }
        [Serializable] public sealed class Leg
        {
            public string first, last;
            public Point[] sole;
        }
        public string[] bones;
        public Leg[] legs;
    }
}
