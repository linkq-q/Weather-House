using UnityEngine;

namespace WeatherHouse.Life
{
    // Profiles keep native Particle System curves/materials editable; gameplay only selects a profile.
    public sealed class LifeWindEffect : MonoBehaviour
    {
        public Transform weakProfile;
        public Transform strongProfile;
        private ParticleSystem[] weak, strong;
        private ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[0];
        private float radius = 8f;
        private int current;

        private void Awake()
        {
            weak = weakProfile ? weakProfile.GetComponentsInChildren<ParticleSystem>(true) : new ParticleSystem[0];
            strong = strongProfile ? strongProfile.GetComponentsInChildren<ParticleSystem>(true) : new ParticleSystem[0];
            Stop(weak); Stop(strong);
        }
        public void SetWeather(bool active, int strength, float castRadius)
        {
            radius = castRadius;
            int next = active ? Mathf.Clamp(strength, 1, 2) : 0;
            if (next == current) return;
            Stop(weak); Stop(strong); current = next;
            if (current != 0)
                foreach (var ps in current == 1 ? weak : strong) ps.Play(false);
        }
        private static void Stop(ParticleSystem[] systems)
        {
            if (systems == null) return;
            foreach (var ps in systems) if (ps) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        private void LateUpdate()
        {
            if (current == 0) return;
            foreach (var ps in current == 1 ? weak : strong)
            {
                if (!ps) continue;
                if (buffer.Length < ps.main.maxParticles) buffer = new ParticleSystem.Particle[ps.main.maxParticles];
                int count = ps.GetParticles(buffer);
                var main = ps.main;
                Transform space = main.simulationSpace == ParticleSystemSimulationSpace.Custom ? main.customSimulationSpace : ps.transform;
                for (int i = 0; i < count; i++)
                {
                    Vector3 world = main.simulationSpace == ParticleSystemSimulationSpace.World ? buffer[i].position :
                        (space ? space.TransformPoint(buffer[i].position) : buffer[i].position);
                    Vector3 delta = world - transform.position;
                    if (delta.x * delta.x + delta.z * delta.z > radius * radius) buffer[i].remainingLifetime = 0;
                }
                ps.SetParticles(buffer, count);
            }
        }
        private void OnDisable() { Stop(weak); Stop(strong); current = 0; }
    }
}
