using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.Hillside.Editor
{
    public static class WildlifeSolitaryStrategyChecks
    {
        public static string RunLogicChecks()
        {
            CheckReplayAndRange(); CheckRecovery(); CheckWater(); CheckInterrupt(); CheckFailureCooldown(); CheckBlockedHome();
            return "PASS solitary strategy: seeded replay and individual ranges\nPASS needs recover only at their activity target\n" +
                "PASS verified water only; missing/lost water never restores thirst\nPASS threat interruption returns home and resumes decisions\n" +
                "PASS stalled-target cooldown, bounded retry and live profile periods\nPASS blocked home uses safe intermediate destinations without restoring fatigue away from home";
        }

        private static bool Reach(Vector3 requested, out Vector3 projected) { projected = requested; return true; }
        private static bool Water(Vector3 point, out Vector3 water) { water = point + new Vector3(.5f, 0, 0); return true; }
        private static bool NoWater(Vector3 point, out Vector3 water) { water = default; return false; }
        private static WildlifeSolitaryRoutine Routine(int id = 20)
        { return new WildlifeSolitaryRoutine(42, id, Vector3.zero, 5, 1); }

        private static void CheckReplayAndRange()
        {
            var a = new WildlifeSolitaryRoutine(42, 20, new Vector3(2, 0, 0), 5, 1, null, Vector3.zero);
            var b = new WildlifeSolitaryRoutine(42, 20, new Vector3(2, 0, 0), 5, 1, null, Vector3.zero);
            var different = Routine(21); var activities = new HashSet<SolitaryActivity>();
            Vector3 pa = a.Home, pb = b.Home;
            for (int tick = 0; tick < 2400; tick++)
            {
                float now = tick * .1f;
                a.Decide(now, pa, Reach, null);
                different.Decide(now, new Vector3(-2, 0, 0), Reach, null);
                b.Decide(now, pb, Reach, null);
                Require(a.Activity == b.Activity && Vector3.Distance(a.Target, b.Target) < .000001f && Mathf.Abs(a.Hunger - b.Hunger) < .000001f,
                    "Another individual changed the seeded solitary decision stream.");
                if (a.HasTarget)
                {
                    Require(a.Contains(a.Target), "A personal target escaped its original habitat region.");
                    activities.Add(a.Activity);
                    pa = Move(pa, a.Target, .1f); pb = Move(pb, b.Target, .1f);
                }
                a.Advance(.1f, pa, true, !a.IsWorking); b.Advance(.1f, pb, true, !b.IsWorking);
            }
            Require(activities.Contains(SolitaryActivity.Forage) && activities.Contains(SolitaryActivity.Rest) &&
                (activities.Contains(SolitaryActivity.Patrol) || activities.Contains(SolitaryActivity.Watch)),
                "The natural routine never progressed beyond foraging into rest and exploratory pauses.");
        }

        private static void CheckRecovery()
        {
            var forage = Routine(); forage.Hunger = .9f; forage.Thirst = forage.Fatigue = .05f;
            forage.Decide(0, Vector3.zero, Reach, null);
            Require(forage.Activity == SolitaryActivity.Forage && !forage.IsWorking, "Hunger did not choose a reachable forage destination.");
            float hungry = forage.Hunger;
            for (int i = 0; i < 10; i++) forage.Advance(.1f, Vector3.zero, true, false);
            Require(forage.Hunger >= hungry, "Foraging restored hunger before arrival.");
            forage.Decide(1, forage.Target, Reach, null); hungry = forage.Hunger;
            for (int i = 0; i < 20; i++) forage.Advance(.1f, forage.Target, true, false);
            Require(forage.Hunger < hungry - .1f, "Arrived foraging did not reduce hunger.");
            hungry = forage.Hunger;
            for (int i = 0; i < 10; i++) forage.Advance(.1f, forage.Target + new Vector3(2, 0, 0), true, false);
            Require(forage.Hunger >= hungry, "A displaced animal kept consuming resources at its old target.");
            var rest = Routine(); rest.Fatigue = .9f; rest.Hunger = rest.Thirst = .05f;
            Vector3 away = new Vector3(3, 0, 0); rest.Decide(0, away, Reach, null);
            float tired = rest.Fatigue;
            for (int i = 0; i < 10; i++) rest.Advance(.1f, away, true, false);
            Require(rest.Activity == SolitaryActivity.Rest && rest.Target == rest.Home && rest.Fatigue >= tired,
                "Rest did not target the personal home or restored fatigue while away.");
            rest.Decide(1, rest.Home, Reach, null); tired = rest.Fatigue;
            for (int i = 0; i < 20; i++) rest.Advance(.1f, rest.Home, true, false);
            Require(rest.Fatigue < tired - .1f, "Resting at home did not restore fatigue.");
        }

        private static void CheckWater()
        {
            var dry = Routine(); dry.Thirst = .9f; dry.Hunger = dry.Fatigue = .05f;
            dry.Decide(0, Vector3.zero, Reach, NoWater);
            Require(dry.Activity != SolitaryActivity.Drink, "An unverified bank became a drinking target.");
            float thirsty = dry.Thirst;
            for (int i = 0; i < 30; i++) dry.Advance(.1f, dry.Target, true, false);
            Require(dry.Thirst >= thirsty, "Unavailable water silently restored thirst.");
            var wet = Routine(); wet.Thirst = .9f; wet.Hunger = wet.Fatigue = .05f;
            wet.Decide(0, Vector3.zero, Reach, Water);
            Require(wet.Activity == SolitaryActivity.Drink && wet.HasTarget, "A reachable verified bank was not selected for thirst.");
            wet.Decide(.1f, wet.Target, Reach, Water); thirsty = wet.Thirst;
            for (int i = 0; i < 20; i++) wet.Advance(.1f, wet.Target, true, false);
            Require(wet.Thirst < thirsty - .2f, "Drinking at a verified bank did not restore thirst.");
            wet.Decide(2.2f, wet.Target, Reach, NoWater); thirsty = wet.Thirst;
            for (int i = 0; i < 10; i++) wet.Advance(.1f, wet.Target, true, false);
            Require(!wet.IsWorking && wet.Thirst >= thirsty, "A vanished water source continued supplying drinking recovery.");
        }

        private static void CheckInterrupt()
        {
            var r = Routine(); r.Hunger = .9f; r.Thirst = r.Fatigue = .05f;
            r.Decide(0, Vector3.zero, Reach, null); Vector3 interruptedAt = r.Target;
            r.Decide(.1f, interruptedAt, Reach, null); r.Interrupt(true);
            float hunger = r.Hunger;
            for (int i = 0; i < 20; i++) r.Advance(.1f, interruptedAt, false, true);
            Require(!r.IsWorking && r.Hunger >= hunger, "An alarm allowed resource recovery to continue.");
            r.Decide(3, interruptedAt, Reach, null);
            Require(r.Activity == SolitaryActivity.Rest && r.Target == r.Home, "Threat recovery did not return to the personal home.");
            r.Decide(4, r.Home, Reach, null); r.Decide(30, r.Home, Reach, null);
            Require(r.Activity == SolitaryActivity.Forage && r.HasTarget, "The interrupted need never resumed after safe rest.");
        }

        private static void CheckFailureCooldown()
        {
            var r = Routine(); r.Hunger = .9f; r.Thirst = r.Fatigue = .05f;
            int queries = 0;
            bool FixedTarget(Vector3 request, out Vector3 result) { queries++; result = new Vector3(2, 0, 0); return true; }
            r.Decide(0, Vector3.zero, FixedTarget, null); r.Decide(3.1f, Vector3.zero, FixedTarget, null);
            Require(!r.HasTarget && r.FailureCount == 1, "A stationary traveller never abandoned its stalled target.");
            int before = queries; r.Decide(3.2f, Vector3.zero, FixedTarget, null);
            Require(queries == before, "Failed travel retried before its cooldown.");
            r.Decide(5, Vector3.zero, FixedTarget, null);
            Require(!r.HasTarget, "The failed point was immediately selected again through projection.");
            r.Decide(10, Vector3.zero, FixedTarget, null);
            Require(r.HasTarget, "A cooled-down target never became eligible again.");
            var slow = Routine(); var fast = Routine(); slow.Hunger = fast.Hunger = .1f;
            slow.Profile.hungerCycleSeconds = 200; fast.Profile.hungerCycleSeconds = 20;
            for (int i = 0; i < 20; i++) { slow.Advance(.1f, Vector3.zero, false, false); fast.Advance(.1f, Vector3.zero, false, false); }
            Require(fast.Hunger - slow.Hunger > .08f, "The exposed hunger period did not affect live needs.");
        }

        private static void CheckBlockedHome()
        {
            // A wall blocks the direct return, while either end has a clear walking route.
            for (int interruption = 0; interruption < 2; interruption++)
            {
                var routine = Routine(); routine.Hunger = routine.Thirst = .05f;
                routine.Fatigue = interruption == 0 ? .8f : .05f;
                if (interruption != 0) routine.Interrupt(true);
                Vector3 position = new Vector3(4, 0, 0);
                bool ReachAroundWall(Vector3 requested, out Vector3 projected)
                {
                    projected = requested;
                    int steps = (int)(Vector3.Distance(position, requested) / .02f) + 1;
                    for (int i = 0; i <= steps; i++)
                    {
                        Vector3 sample = Vector3.Lerp(position, requested, (float)i / steps);
                        if (sample.x > 1.7f && sample.x < 2.3f && Mathf.Abs(sample.z) < 1.1f) return false;
                    }
                    return true;
                }
                Require(!ReachAroundWall(routine.Home, out _), "The return-home fixture did not block its direct line.");
                bool intermediate = false, home = false;
                for (int tick = 0; tick < 450 && !home; tick++)
                {
                    routine.Decide(tick * .1f, position, ReachAroundWall, null);
                    float before = routine.Fatigue;
                    if (routine.HasTarget)
                    {
                        Require(ReachAroundWall(routine.Target, out _) && routine.Contains(routine.Target), "A return detour cut through the wall or left the original region.");
                        intermediate |= Vector3.Distance(routine.Target, routine.Home) > .4f;
                        position = Move(position, routine.Target, .1f);
                    }
                    routine.Advance(.1f, position, true, !routine.IsWorking);
                    if (Vector3.Distance(position, routine.Home) > .4f)
                        Require(routine.Fatigue >= before, "A return detour incorrectly restored fatigue away from home.");
                    home = routine.IsWorking && routine.Activity == SolitaryActivity.Rest && Vector3.Distance(position, routine.Home) <= .18f;
                }
                Require(intermediate && home, "The animal stayed at a blocked home line instead of reaching home through safe intermediate points.");
            }
        }

        private static Vector3 Move(Vector3 from, Vector3 to, float distance)
        { float length = Vector3.Distance(from, to); return length <= distance ? to : Vector3.Lerp(from, to, distance / length); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
