using UnityEngine;

namespace WeatherHouse.Hillside
{
    public enum WildlifeRestPhase { Standing, LyingDown, Sleeping, GettingUp }

    // Presentation owns its transitions; movement stays paused until the animal is upright.
    public sealed class WildlifeRestSequence
    {
        public WildlifeRestPhase Phase { get; private set; }
        public AnimationClip Clip { get; private set; }
        public bool BlocksMovement => Phase != WildlifeRestPhase.Standing;
        public float Speed { get; private set; } = 1f;
        public float StartTime { get; private set; }
        private float age;
        private bool reversingCrouch;

        public void Reset()
        { Phase = WildlifeRestPhase.Standing; Clip = null; age = StartTime = 0f; Speed = 1f; reversingCrouch = false; }

        public void Tick(bool wantsRest, bool urgent, float dt, WildlifeClips clips, bool atStandingPose = true)
        {
            if (clips == null || !clips.HasRestSequence) { Reset(); return; }
            if (Phase == WildlifeRestPhase.Standing)
            {
                if (!wantsRest || urgent) return;
                Enter(WildlifeRestPhase.LyingDown, clips.restDown);
                return;
            }
            if (Phase == WildlifeRestPhase.GettingUp)
            {
                Speed = (urgent ? 2f : 1f) * (reversingCrouch ? -1f : 1f);
                age += Mathf.Max(0f, dt) * Speed;
                if ((reversingCrouch ? age <= 0f : age >= Clip.length) && atStandingPose) Reset();
                return;
            }
            if (!wantsRest || urgent)
            {
                // An unfinished crouch unwinds its exact current pose. Fully resting
                // animals use the separately authored rise with its own limb timing.
                bool interrupted = Phase == WildlifeRestPhase.LyingDown;
                float start = interrupted ? Mathf.Clamp(age, 0f, Clip.length) : 0f;
                Enter(WildlifeRestPhase.GettingUp, interrupted ? clips.restDown : clips.restUp, start);
                reversingCrouch = interrupted;
                Speed = (urgent ? 2f : 1f) * (interrupted ? -1f : 1f);
                return;
            }
            age += Mathf.Max(0f, dt);
            if (Phase == WildlifeRestPhase.LyingDown && age >= Clip.length)
                Enter(WildlifeRestPhase.Sleeping, clips.restSleep);
        }

        private void Enter(WildlifeRestPhase phase, AnimationClip clip, float start = 0f)
        { Phase = phase; Clip = clip; age = StartTime = start; Speed = 1f; reversingCrouch = false; }
    }
}
