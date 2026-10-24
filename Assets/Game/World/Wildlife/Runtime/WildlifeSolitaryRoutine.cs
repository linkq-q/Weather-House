using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.Hillside
{
    public enum SolitaryActivity { Patrol, Forage, Drink, Rest, Watch }
    public enum WildlifeRoutineMode { Ambient = 0, Solitary = 1 }
    [Serializable]
    public sealed class WildlifeSolitaryProfile
    {
        [Min(10f)] public float hungerCycleSeconds = 160f, thirstCycleSeconds = 220f, fatigueCycleSeconds = 180f;
        [Min(1f)] public float forageSeconds = 8f, drinkSeconds = 5f, restSeconds = 12f;
    }
    public delegate bool SolitaryReachable(Vector3 requested, out Vector3 projected);
    public delegate bool SolitaryRestQuery(Vector3 restingPoint);
    public delegate bool SolitaryWaterQuery(Vector3 standingPoint, out Vector3 waterPoint);

    // Decisions only: WildlifeAgent remains the sole owner of movement, alarms and its animation graph.
    public sealed class WildlifeSolitaryRoutine
    {
        public Vector3 Home { get; }
        public Vector3 RegionCenter { get; }
        public float Range { get; }
        public WildlifeSolitaryProfile Profile { get; }
        public Vector3 Target { get; private set; }
        public Vector3 WaterPoint { get; private set; }
        public SolitaryActivity Activity { get; private set; } = SolitaryActivity.Watch;
        public bool HasTarget { get; private set; }
        public bool IsWorking { get; private set; }
        public bool ReturningHome => returnHome;
        public float Hunger, Thirst, Fatigue;
        public int FailureCount { get; private set; }
        public int DecisionCount { get; private set; }
        public string Decision => returnWaypoint ? "回家：安全绕行" : HasTarget ? Activity + (IsWorking ? "：停留" : "：前往") : "等待可达目标";

        private struct Failure { public Vector3 point; public float until; }
        private readonly List<Failure> failures = new List<Failure>(8);
        private WildlifeRandom random;
        private readonly float walkingSpeed;
        private readonly float detourSide;
        private const float Arrival = .18f;
        private float nextChoice, workUntil, travelUntil, progressAt, bestDistance, drinkSearchAt;
        private bool returnHome, returnWaypoint;
        private Vector3 restDestination;
        private bool restSiteCached;
        private float restSearchAt;
        private readonly float restSearchAngle;

        public WildlifeSolitaryRoutine(int seed, int id, Vector3 home, float range, float speed,
            WildlifeSolitaryProfile profile = null, Vector3? regionCenter = null)
        {
            Home = Target = restDestination = home; Range = Mathf.Max(1f, range); walkingSpeed = Mathf.Max(.1f, speed);
            RegionCenter = regionCenter ?? home; Profile = profile ?? new WildlifeSolitaryProfile();
            random = new WildlifeRandom(seed, id, "solitary-routine");
            detourSide = (id & 1) == 0 ? 1f : -1f;
            restSearchAngle = id * 2.39996323f;
            Hunger = random.Range(.35f, .7f); Thirst = random.Range(.2f, .55f); Fatigue = random.Range(.2f, .6f);
        }

        public void Advance(float dt, Vector3 position, bool available, bool moving)
        {
            dt = Mathf.Clamp(dt, 0, .1f);
            Hunger = Mathf.Clamp01(Hunger + dt / Mathf.Max(10f, Profile.hungerCycleSeconds));
            Thirst = Mathf.Clamp01(Thirst + dt / Mathf.Max(10f, Profile.thirstCycleSeconds));
            Fatigue = Mathf.Clamp01(Fatigue + dt * (moving ? 1f : .25f) / Mathf.Max(10f, Profile.fatigueCycleSeconds));
            if (!available || !IsWorking || Distance(position, Target) > Arrival) return;
            if (Activity == SolitaryActivity.Forage) Hunger = Mathf.Max(0, Hunger - dt * .65f / Mathf.Max(1f, Profile.forageSeconds));
            if (Activity == SolitaryActivity.Drink) Thirst = Mathf.Max(0, Thirst - dt * .65f / Mathf.Max(1f, Profile.drinkSeconds));
            if (Activity == SolitaryActivity.Rest)
                Fatigue = Mathf.Max(0, Fatigue - dt * .75f / Mathf.Max(1f, Profile.restSeconds));
        }

        public void Interrupt(bool seekHome)
        {
            HasTarget = IsWorking = returnWaypoint = false; returnHome |= seekHome; nextChoice = 0;
        }

        public void Decide(float now, Vector3 position, SolitaryReachable reachable, SolitaryWaterQuery water, SolitaryRestQuery restSite = null)
        {
            if (HasTarget)
            {
                float distance = Distance(position, Target);
                if (returnWaypoint && distance <= Arrival) { HasTarget = returnWaypoint = false; }
                bool drinkValid = Activity != SolitaryActivity.Drink || (water != null && water(Target, out _));
                if (!drinkValid) { Fail(now); return; }
                if (HasTarget && !IsWorking)
                {
                    if (distance <= Arrival)
                    {
                        IsWorking = true;
                        if (Activity == SolitaryActivity.Rest) returnHome = false;
                        float duration = Activity == SolitaryActivity.Rest ? Profile.restSeconds : Activity == SolitaryActivity.Forage ? Profile.forageSeconds :
                            Activity == SolitaryActivity.Drink ? Profile.drinkSeconds : 4f;
                        workUntil = now + Mathf.Max(1f, duration) * random.Range(.85f, 1.15f);
                    }
                    else
                    {
                        if (distance < bestDistance - .08f) { bestDistance = distance; progressAt = now; }
                        if (now >= travelUntil || now - progressAt >= 3f) Fail(now);
                        return;
                    }
                }
                if (HasTarget && distance > Arrival + .05f) { Fail(now); return; }
                if (HasTarget && now < workUntil) return;
                HasTarget = IsWorking = false;
            }
            if (now < nextChoice) return;
            bool mustReturn = returnHome || !Contains(position);
            SolitaryActivity choice = mustReturn || Fatigue >= Mathf.Max(.42f, Hunger) ? SolitaryActivity.Rest :
                Thirst > Mathf.Max(.45f, Hunger) && now >= drinkSearchAt ? SolitaryActivity.Drink :
                Hunger >= .4f ? SolitaryActivity.Forage : random.Value() < .72f ? SolitaryActivity.Patrol : SolitaryActivity.Watch;
            if (TryPlan(choice, now, position, reachable, water, restSite)) return;
            if (choice == SolitaryActivity.Drink)
            {
                drinkSearchAt = now + 20f;
                if (TryPlan(Fatigue > Hunger ? SolitaryActivity.Rest : SolitaryActivity.Forage, now, position, reachable, water, restSite)) return;
            }
            FailureCount++; nextChoice = now + random.Range(1.5f, 2.5f);
            HasTarget = IsWorking = false; Activity = SolitaryActivity.Watch;
        }

        private bool TryPlan(SolitaryActivity activity, float now, Vector3 position, SolitaryReachable reachable, SolitaryWaterQuery water, SolitaryRestQuery restSite = null)
        {
            if (activity == SolitaryActivity.Drink && water == null) return false;
            if (activity == SolitaryActivity.Rest)
            {
                returnHome = true;
                if (restSite != null) return TryRestSite(now, position, reachable, restSite);
            }
            int attempts = activity == SolitaryActivity.Rest || activity == SolitaryActivity.Watch ? 1 : 16;
            for (int i = 0; i < attempts; i++)
            {
                Vector2 offset = random.InCircle(Range * .85f);
                Vector3 request = activity == SolitaryActivity.Rest ? Home : activity == SolitaryActivity.Watch ? position :
                    Home + new Vector3(offset.x, 0, offset.y);
                if (!Contains(request) || Cooling(request, now) ||
                    !reachable(request, out Vector3 candidate) || !Contains(candidate) || Cooling(candidate, now)) continue;
                if ((activity == SolitaryActivity.Patrol || activity == SolitaryActivity.Forage) && Distance(position, candidate) < .7f) continue;
                Vector3 waterPoint = default;
                if (activity == SolitaryActivity.Drink && !water(candidate, out waterPoint)) continue;
                BeginTarget(activity, now, position, candidate, waterPoint);
                return true;
            }
            return activity == SolitaryActivity.Rest && TryReturnWaypoint(now, position, reachable);
        }

        private bool TryRestSite(float now, Vector3 position, SolitaryReachable reachable, SolitaryRestQuery restSite)
        {
            if (restSiteCached && Contains(restDestination) && !Cooling(restDestination, now) && restSite(restDestination))
            {
                if (reachable(restDestination, out Vector3 bed) && Contains(bed) && !Cooling(bed, now) && restSite(bed))
                { BeginTarget(SolitaryActivity.Rest, now, position, bed, default); return true; }
                return TryReturnWaypoint(now, position, reachable);
            }
            restSiteCached = false;
            if (now < restSearchAt) return false;
            restSearchAt = now + 15f;
            // Search once, then reuse a verified bed across rests and interruptions.
            // Individual spoke offsets avoid giving every animal the same first bed.
            // Reject unsuitable ground before the bounded whole-body travel checks.
            bool foundSurface = false; int travelQueries = 0;
            float radius = Mathf.Min(7.5f, Range - Arrival);
            for (int i = 0; i < 49; i++)
            {
                int ring = (i - 1) / 16 + 1, spoke = (i - 1) % 16;
                float angle = restSearchAngle + spoke * Mathf.PI / 8f + ring * Mathf.PI / 16f;
                Vector3 request = i == 0 ? Home : Home + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (radius * ring / 3f);
                if (!Contains(request) || Cooling(request, now) || !restSite(request)) continue;
                if (!foundSurface) { restDestination = request; foundSurface = true; }
                if (++travelQueries > 8) break;
                if (!reachable(request, out Vector3 candidate) || !Contains(candidate) || Cooling(candidate, now) || !restSite(candidate)) continue;
                restDestination = candidate; restSiteCached = true;
                BeginTarget(SolitaryActivity.Rest, now, position, candidate, default);
                return true;
            }
            restSiteCached = foundSurface;
            return foundSurface && TryReturnWaypoint(now, position, reachable);
        }

        private bool TryReturnWaypoint(float now, Vector3 position, SolitaryReachable reachable)
        {
            Vector3 toward = restDestination - position; toward.y = 0;
            float length = Distance(position, restDestination);
            if (length < .4f) return false;
            toward *= 1f / length;
            Vector3 side = new Vector3(-toward.z, 0, toward.x);
            // Keep a preferred side across short legs, checking the real whole segment before every leg.
            for (int opposite = 0; opposite < 2; opposite++)
                for (int ring = 0; ring < 2; ring++)
                    for (int turn = 1; turn <= 3; turn++)
                    {
                        float angle = turn * Mathf.PI * .25f, sign = detourSide * (opposite == 0 ? 1 : -1);
                        float reach = Mathf.Min(Range * .5f, ring == 0 ? 1.5f : 3f);
                        Vector3 request = position + (toward * Mathf.Cos(angle) + side * (Mathf.Sin(angle) * sign)) * reach;
                        if (!Contains(request) || Cooling(request, now) || !reachable(request, out Vector3 candidate) ||
                            !Contains(candidate) || Cooling(candidate, now) || Distance(position, candidate) < .7f) continue;
                        BeginTarget(SolitaryActivity.Patrol, now, position, candidate, default);
                        returnWaypoint = true;
                        return true;
                    }
            return false;
        }

        private void BeginTarget(SolitaryActivity activity, float now, Vector3 position, Vector3 target, Vector3 water)
        {
            Target = target; WaterPoint = water; Activity = activity;
            HasTarget = true; IsWorking = returnWaypoint = false; DecisionCount++;
            bestDistance = Distance(position, Target); progressAt = now;
            travelUntil = now + Mathf.Max(8f, bestDistance / walkingSpeed * 2f + 3f);
        }

        private bool Cooling(Vector3 point, float now)
        {
            for (int i = failures.Count - 1; i >= 0; i--)
            {
                if (failures[i].until <= now) { failures.RemoveAt(i); continue; }
                if (Distance(point, failures[i].point) < .8f) return true;
            }
            return false;
        }

        private void Fail(float now)
        {
            if (failures.Count >= 8) failures.RemoveAt(0);
            failures.Add(new Failure { point = Target, until = now + 6f });
            FailureCount++; HasTarget = IsWorking = returnWaypoint = false; Activity = SolitaryActivity.Watch; nextChoice = now + 1.5f;
        }

        public bool Contains(Vector3 point, float scale = 1f)
        { return Distance(point, Home) <= Range * scale && Distance(point, RegionCenter) <= Range * scale; }

        private static float Distance(Vector3 a, Vector3 b) { return new Vector2(a.x - b.x, a.z - b.z).magnitude; }
    }
}
