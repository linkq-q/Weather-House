using System;
using System.Collections.Generic;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    // Eight album covers and one short clip. Ownership ends when the journal closes.
    public sealed class LifePhotoCache
    {
        const int CoverLimit = 8, FrameLimit = 18;
        readonly Dictionary<string, Texture2D> covers = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        readonly List<string> coverOrder = new List<string>();
        readonly Dictionary<int, Texture2D> frames = new Dictionary<int, Texture2D>();
        readonly List<int> frameOrder = new List<int>();
        string currentClip;
        public int DecodedFrames { get; private set; }
        public int TextureCount => covers.Count + frames.Count;

        public Texture2D Cover(ObservationRecord record)
        {
            if (record == null || record.frameCount < 1) throw new ArgumentException("照片没有可读取的画面。", nameof(record));
            string key = record.id + "\n" + record.clipFolder;
            if (covers.TryGetValue(key, out Texture2D cached)) { coverOrder.Remove(key); coverOrder.Add(key); return cached; }
            Texture2D texture = Decode(record, 0);
            if (covers.Count == CoverLimit)
            { string oldest = coverOrder[0]; Release(covers[oldest]); covers.Remove(oldest); coverOrder.RemoveAt(0); }
            covers.Add(key, texture); coverOrder.Add(key); return texture;
        }

        public Texture2D Frame(ObservationRecord record, int index)
        {
            if (record == null || index < 0 || index >= record.frameCount) throw new ArgumentOutOfRangeException(nameof(index));
            string key = record.id + "\n" + record.clipFolder;
            if (currentClip != key)
            { foreach (Texture2D value in frames.Values) Release(value); frames.Clear(); frameOrder.Clear(); currentClip = key; }
            if (index == 0) return Cover(record);
            if (frames.TryGetValue(index, out Texture2D cached)) { frameOrder.Remove(index); frameOrder.Add(index); return cached; }
            Texture2D texture = Decode(record, index);
            if (frames.Count == FrameLimit)
            { int oldest = frameOrder[0]; Release(frames[oldest]); frames.Remove(oldest); frameOrder.RemoveAt(0); }
            frames.Add(index, texture); frameOrder.Add(index); return texture;
        }

        Texture2D Decode(ObservationRecord record, int index)
        {
            Texture2D texture = MotionPhotoRecorder.LoadFrame(record, index);
            texture.name = "Journal photo " + record.id + " " + index; DecodedFrames++; return texture;
        }

        public void Clear()
        {
            foreach (Texture2D value in covers.Values) Release(value);
            foreach (Texture2D value in frames.Values) Release(value);
            covers.Clear(); coverOrder.Clear(); frames.Clear(); frameOrder.Clear(); currentClip = null;
        }

        static void Release(Texture2D value)
        { if (!value) return; if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
}
