using System;
using UnityEngine;

namespace WeatherHouse.Life
{
    public enum LifeAudioCue { Ui, Progress, Paper, Stroke, Camera, Fiber, Contact, Water, Drink, Cast, Rain, Wind, GrassStep, DirtStep, StoneStep, Outdoor, Shore }
    public enum LifeAudioSurface { Unknown, Grass, Dirt, Stone }

    [CreateAssetMenu(menuName = "Weather House/Life audio catalog")]
    public sealed class LifeAudioCatalog : ScriptableObject
    {
        [Serializable] public sealed class Sound
        {
            public LifeAudioCue cue;
            public AudioClip[] clips = Array.Empty<AudioClip>();
            [Range(0, 1)] public float volume = .4f;
            [Min(0)] public float cooldown = .15f;
            [Min(0)] public float shortDuration;
        }
        [Serializable] public sealed class Voice { public string speciesId; public AudioClip clip; }
        [Serializable] public sealed class TerrainSurface { public TerrainLayer layer; public LifeAudioSurface surface; }
        [Serializable] public sealed class MaterialSurface { public Material material; public LifeAudioSurface surface; }
        public Sound[] sounds = Array.Empty<Sound>();
        public Voice[] voices = Array.Empty<Voice>();
        public TerrainSurface[] terrainSurfaces = Array.Empty<TerrainSurface>();
        public MaterialSurface[] materialSurfaces = Array.Empty<MaterialSurface>();
        public Sound Find(LifeAudioCue cue) { foreach (Sound entry in sounds) if (entry != null && entry.cue == cue) return entry; return null; }
        public AudioClip VoiceFor(string species) { foreach (Voice entry in voices) if (entry != null && entry.speciesId == species) return entry.clip; return null; }
        public LifeAudioSurface Surface(TerrainLayer layer) { if (layer) foreach (TerrainSurface entry in terrainSurfaces) if (entry.layer == layer) return entry.surface; return LifeAudioSurface.Unknown; }
        public LifeAudioSurface Surface(Material material) { if (material) foreach (MaterialSurface entry in materialSurfaces) if (entry.material == material) return entry.surface; return LifeAudioSurface.Unknown; }
    }
}
