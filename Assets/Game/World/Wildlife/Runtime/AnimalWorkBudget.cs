using UnityEngine;

namespace WeatherHouse.Hillside
{
    public static class AnimalWorkBudget
    {
        static int frame = -1, work;
        static double start;
        static double Now => System.Diagnostics.Stopwatch.GetTimestamp() * 1000d / System.Diagnostics.Stopwatch.Frequency;
        // Budget governs starting expensive work. A single Unity physics query cannot be preempted.
        public static bool TryTake()
        {
            if (!Application.isPlaying) return true;
            if (frame != Time.frameCount) { frame = Time.frameCount; work = 0; start = Now; }
            if (work >= 8 || work > 0 && Now - start >= 2) return false;
            work++; return true;
        }
    }
}
