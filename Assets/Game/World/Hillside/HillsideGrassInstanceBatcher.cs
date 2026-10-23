using System;
using UnityEngine;

namespace WeatherHouse.Hillside
{
    // Each group must use one mesh/material/LOD/camera and identical draw flags.
    public sealed class HillsideGrassInstanceBatcher
    {
        public const int GroupCount = 24;
        public const int Capacity = 1023;
        readonly Matrix4x4[][] buffers = new Matrix4x4[GroupCount][];
        readonly int[] counts = new int[GroupCount];
        readonly Action<int, Matrix4x4[], int> submit;

        public HillsideGrassInstanceBatcher(Action<int, Matrix4x4[], int> submit)
        {
            this.submit = submit ?? throw new ArgumentNullException(nameof(submit));
            for (int group = 0; group < GroupCount; group++) buffers[group] = new Matrix4x4[Capacity];
        }

        // Discard an unfinished tail when beginning another camera or frame.
        public void Begin() { Array.Clear(counts, 0, counts.Length); }

        public void Append(int group, Matrix4x4[] source, int count)
        {
            if ((uint)group >= GroupCount) throw new ArgumentOutOfRangeException(nameof(group));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (count < 0 || count > source.Length) throw new ArgumentOutOfRangeException(nameof(count));
            int copied = 0;
            while (copied < count)
            {
                int copy = Math.Min(Capacity - counts[group], count - copied);
                Array.Copy(source, copied, buffers[group], counts[group], copy);
                counts[group] += copy; copied += copy;
                if (counts[group] != Capacity) continue;
                counts[group] = 0;
                // The callback consumes the reused buffer synchronously and must not retain it.
                submit(group, buffers[group], Capacity);
            }
        }

        public void Flush()
        {
            for (int group = 0; group < GroupCount; group++)
            {
                int count = counts[group];
                if (count == 0) continue;
                counts[group] = 0;
                submit(group, buffers[group], count);
            }
        }
    }
}
