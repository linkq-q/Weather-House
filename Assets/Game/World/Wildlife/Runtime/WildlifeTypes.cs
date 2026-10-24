using System;
using UnityEngine;

namespace WeatherHouse.Hillside
{
    public enum WildlifeLocomotion { Ground, Flying, Swimming }
    public enum WildlifeState { Idle, Forage, Wander, Greet, Alert, Flee, ReturnHome, Takeoff, Fly, Swim, Landing }

    [Serializable]
    public struct HabitatVolume
    {
        public string id;
        public Vector3 center;
        public Vector2 radii;

        public bool Contains(Vector3 point, float inset = 0f)
        {
            float x = radii.x - inset, z = radii.y - inset;
            if (x <= 0f || z <= 0f) return false;
            float dx = (point.x - center.x) / x, dz = (point.z - center.z) / z;
            return dx * dx + dz * dz <= 1f;
        }
    }

    [Serializable]
    public sealed class WildlifeClips
    {
        public AnimationClip idle, forage, walk, run, greet, alert, takeoff, fly, land;
        public AnimationClip restDown, restSleep, restUp;
        public bool HasRestSequence => restDown && restSleep && restUp;

        public AnimationClip FirstAvailable => idle != null ? idle : walk != null ? walk : fly != null ? fly :
            forage != null ? forage : run != null ? run : greet != null ? greet : alert != null ? alert : takeoff != null ? takeoff : land;

        public AnimationClip ForState(WildlifeState state, WildlifeLocomotion locomotion)
        {
            AnimationClip clip;
            switch (state)
            {
                case WildlifeState.Forage: clip = forage != null ? forage : idle; break;
                case WildlifeState.Greet: clip = greet != null ? greet : idle; break;
                case WildlifeState.Alert: clip = alert != null ? alert : idle; break;
                case WildlifeState.Takeoff: clip = takeoff != null ? takeoff : fly; break;
                case WildlifeState.Landing: clip = land != null ? land : fly; break;
                case WildlifeState.Fly: clip = fly != null ? fly : walk; break;
                case WildlifeState.Flee:
                    clip = locomotion == WildlifeLocomotion.Flying && fly != null ? fly : run != null ? run : walk;
                    break;
                case WildlifeState.Wander:
                case WildlifeState.ReturnHome:
                case WildlifeState.Swim: clip = locomotion == WildlifeLocomotion.Flying && fly != null ? fly : walk; break;
                default: clip = idle; break;
            }
            return clip != null ? clip : FirstAvailable;
        }
    }

    public struct WildlifeAlarm
    {
        public int id;
        public Vector3 origin;
        public float radius, intensity, expiresAt;
        public WildlifeAgent source;
        public string sourceSpecies;
    }

    // A local random stream keeps scene rebuilding and individual sizes independent of Unity's global Random.
    public struct WildlifeRandom
    {
        private uint state;
        public WildlifeRandom(int worldSeed, int spawnId, string species)
        {
            uint hash = 2166136261u;
            if (species != null)
                foreach (char c in species) hash = (hash ^ c) * 16777619u;
            state = hash ^ unchecked((uint)worldSeed * 747796405u) ^ unchecked((uint)spawnId * 2891336453u);
            if (state == 0u) state = 0x9e3779b9u;
        }
        public float Value()
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return (state >> 8) * (1f / 16777216f);
        }
        public float Range(float min, float max) { return Mathf.Lerp(min, max, Value()); }
        public Vector2 InCircle(float radius)
        {
            float angle = Value() * Mathf.PI * 2f;
            float distance = Mathf.Sqrt(Value()) * radius;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
        }
    }
}
