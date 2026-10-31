using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.WildlifeJourney;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    public sealed partial class LifeWorld
    {
        sealed class Guidance
        {
            public Binding animal;
            public LifeRequest request;
            public JourneyMember member;
            public Vector3 target, leaseTarget, lastPosition;
            public AnimalLocalRoute search;
            public List<Vector3> route;
            public int step, revision;
            public float began, deadline, renewAt, waitingSince = -1, progressAt;
            public bool cueOnly, reminderCue, leased;
        }
        sealed class GuidanceReminder
        {
            public LifeRequest request;
            public Vector3 playerPosition;
            public float due;
        }
        Guidance guidance;
        GuidanceReminder guidanceReminder;
        WeatherRegion[] guidanceWeather = Array.Empty<WeatherRegion>();

        // Called only by an actual, current listening interaction; observations and AI replies never start a tour.
        public bool BeginGuidance(LifeRequest request)
        {
            if (!TryGuidanceTarget(request, out Binding animal, out Vector3 target) ||
                Flat(Player.position, animal.speaker.transform.position) > LifeSession.TalkRadius) return false;
            StopGuidance(false);
            guidanceReminder = null;
            bool cueOnly = request.condition != "arrive" && request.condition != "walk_sequence" && request.condition != "walk_reverse";
            return StartGuidance(animal, request, target, cueOnly);
        }

        bool TryGuidanceTarget(LifeRequest request, out Binding animal, out Vector3 target)
        {
            animal = null; target = default;
            if (!owner || !Player || request == null || !requests.Contains(request) ||
                !owner.Progress.CanPresent(request, requests) ||
                owner.Progress.FindTask(request.id)?.status == LifeTaskStatus.Complete ||
                !bindings.TryGetValue(request.speakerKey ?? "", out animal) || !animal.speaker ||
                animal.speaker.Current != request) return false;
            string id = request.targetId;
            if (request.condition == "walk_sequence" || request.condition == "walk_reverse")
            {
                accepted.TryGetValue(request.id, out Accepted progress);
                bool reverse = request.condition == "walk_reverse", second = progress != null && progress.stage > 0;
                id = reverse == second ? "fox.here" : "fox.tree";
            }
            else if (request.condition != "arrive" && request.condition != "dry_mat" &&
                request.condition != "water_retained" && request.condition != "common_rest") return false;
            if (string.IsNullOrEmpty(id) || invalidTargets.Contains(id) || !anchors.ContainsKey(id) && !objects.ContainsKey(id)) return false;
            target = AnchorPosition(id);
            return Finite(target) && Flat(animal.speaker.transform.position, target) <= 16;
        }

        bool StartGuidance(Binding animal, LifeRequest request, Vector3 target, bool cueOnly, bool reminder = false)
        {
            var g = new Guidance { animal = animal, request = request, target = target, cueOnly = cueOnly, reminderCue = reminder,
                began = Time.time, deadline = Time.time + (cueOnly ? 2 : 45), progressAt = Time.time,
                lastPosition = animal.speaker.transform.position };
            if (animal.herd) g.member = Array.Find(animal.herd.members, m => m != null && m.id == animal.member);
            guidanceWeather = FindObjectsOfType<WeatherRegion>();
            if (GuidanceBusy(g) || reminder && (GuidanceHasLease(g) || GuidanceSpeed(g) > .05f)) return false;
            if (!GuidanceMove(g, g.lastPosition)) return false;
            guidance = g;
            if (!cueOnly)
                g.search = new AnimalLocalRoute(g.lastPosition, target, animal.wildlife ? animal.wildlife.Radius : g.member.profile.bodyRadius,
                    (Vector3 from, Vector3 to, out Vector3 foot) => GuidanceStep(g, from, to, out foot));
            return true;
        }

        void UpdateGuidance()
        {
            if (guidance == null)
            {
                GuidanceReminder reminder = guidanceReminder;
                if (reminder == null || Time.time < reminder.due) return;
                if (!TryGuidanceTarget(reminder.request, out Binding animal, out Vector3 target)) { guidanceReminder = null; return; }
                reminder.due = Time.time + 120;
                bool progressed = Flat(Player.position, reminder.playerPosition) > .8f;
                reminder.playerPosition = Player.position;
                bool arrival = reminder.request.condition == "arrive" || reminder.request.condition == "walk_sequence" || reminder.request.condition == "walk_reverse";
                float radius = reminder.request.condition == "arrive" ? 2.5f : SequenceRegionRadius("fox.here", "fox.tree");
                if (!progressed && Flat(Player.position, animal.speaker.transform.position) < 5 &&
                    (!arrival || !LifeArrivalRegion.Contains(Player, target, radius)))
                    StartGuidance(animal, reminder.request, target, true, true);
                return;
            }
            Guidance g = guidance;
            if (!TryGuidanceTarget(g.request, out Binding _, out Vector3 currentTarget) ||
                Flat(currentTarget, g.target) > .1f || GuidanceBusy(g) || Time.time >= g.deadline ||
                g.leased && GuidanceRevision(g) != g.revision ||
                Flat(Player.position, g.animal.speaker.transform.position) > 8)
            { StopGuidance(true); return; }
            Vector3 position = g.animal.speaker.transform.position;
            if (Flat(position, g.lastPosition) > .08f) { g.lastPosition = position; g.progressAt = Time.time; }
            if (!AnimalWorkBudget.TryBegin(AnimalWorkBudget.Kind.Planning, out var work)) return;
            using (work)
            {
                float age = Time.time - g.began;
                if (g.cueOnly || age < 1.2f)
                {
                    if (!RenewGuidance(g, position)) { StopGuidance(false); return; }
                    FaceGuidance(g, g.cueOnly && age > 1 ? Player.position : g.target);
                    return;
                }
                if (g.search != null)
                {
                    if (!RenewGuidance(g, position)) { StopGuidance(false); return; }
                    g.search.Advance();
                    if (!g.search.Finished) return;
                    g.route = g.search.Result; g.search = null;
                    if (g.route == null) { StopGuidance(false); return; }
                    g.progressAt = Time.time;
                }
                bool reached = Flat(position, g.target) < .4f;
                bool behind = Flat(position, Player.position) > 3.8f;
                if (reached || behind)
                {
                    g.progressAt = Time.time;
                    if (g.waitingSince < 0) g.waitingSince = Time.time;
                    if (!RenewGuidance(g, position)) { StopGuidance(false); return; }
                    FaceGuidance(g, Player.position);
                    if (Time.time - g.waitingSince > (reached ? 2.5f : 4)) StopGuidance(true);
                    return;
                }
                g.waitingSince = -1;
                if (Time.time - g.progressAt > 6) { StopGuidance(false); return; }
                while (g.step < g.route.Count && Flat(position, g.route[g.step]) < .3f) g.step++;
                if (g.step == g.route.Count) { StopGuidance(true); return; }
                Vector3 next = Vector3.MoveTowards(position, g.route[g.step], 1.6f);
                if (!RenewGuidance(g, next)) StopGuidance(false);
            }
        }

        bool GuidanceBusy(Guidance g)
        {
            Binding b = g.animal;
            if (!b.speaker || !b.speaker.gameObject.activeInHierarchy || !Player || b.rest || b.weatherRetreat ||
                Flat(b.target, b.home) > .25f || demonstrationKey == b.speaker.key ||
                feeding != null && feeding.animal == b || drinks.Any(d => d.animal == b) ||
                owner.Gameplay.Judging || LifeOnboarding.Blocks(owner.Gameplay)) return true;
            GameplayHud hud = owner.GetComponent<GameplayHud>();
            if (hud && hud.IsDialogueOpen || Mathf.Abs(Player.position.y - b.speaker.transform.position.y) > 2) return true;
            if (autonomy && autonomy.IsUrgentlySheltering(b.speaker.transform)) return true;
            foreach (WeatherRegion weather in guidanceWeather)
                if (weather && weather.isActiveAndEnabled && weather.SoundActive && weather.Current == LocalWeather.Rain &&
                    weather.SoundStrength >= 2 && weather.Contains(b.speaker.transform.position)) return true;
            if (b.wildlife) return !b.wildlife.isActiveAndEnabled || b.wildlife.State == WildlifeState.Alert ||
                b.wildlife.State == WildlifeState.Flee || b.wildlife.Sleeping;
            return !b.herd || !b.herd.isActiveAndEnabled || g.member == null || g.member.Mode != JourneyTravelMode.Walk ||
                g.member.bridge != 0 || g.member.bodyStepPhase != 0 || b.herd.social && b.herd.social.IsParticipating(b.member);
        }
        bool GuidanceStep(Guidance g, Vector3 from, Vector3 to, out Vector3 foot)
        {
            Binding b = g.animal; foot = from;
            Quaternion rotation = Flat(from, b.speaker.transform.position) < .1f ? b.speaker.transform.rotation :
                WildlifeBodyEnvelope.Heading(to - from, b.speaker.transform.rotation);
            if (b.wildlife) return b.population && b.population.TryMoveFrom(b.wildlife, from, rotation, to, out foot);
            return b.herd && b.herd.navigation && b.herd.navigation.TryStep(g.member.profile, from, to, out foot, out JourneyTravelMode mode) &&
                mode == JourneyTravelMode.Walk && b.herd.navigation.CanMoveSupportedBody(g.member.profile, from, rotation, foot,
                    WildlifeBodyEnvelope.Heading(to - from, rotation));
        }
        bool RenewGuidance(Guidance g, Vector3 target)
        {
            if (Time.time < g.renewAt && Flat(g.leaseTarget, target) < .3f) return true;
            return GuidanceMove(g, target);
        }
        bool GuidanceMove(Guidance g, Vector3 target)
        {
            Binding b = g.animal;
            bool accepted = b.wildlife ? b.wildlife.RequestLifeTarget(target, 1.5f) : b.herd.RequestLifeTarget(b.member, target, 1.5f);
            if (!accepted) return false;
            g.leased = true; g.revision = GuidanceRevision(g); g.leaseTarget = target; g.renewAt = Time.time + .5f;
            return true;
        }
        int GuidanceRevision(Guidance g) => g.animal.wildlife ? g.animal.wildlife.IntentRevision : g.member.intentRevision;
        bool GuidanceHasLease(Guidance g) => g.animal.wildlife ? g.animal.wildlife.HasLifeTarget : g.animal.herd.HasLifeTarget(g.animal.member);
        float GuidanceSpeed(Guidance g) => g.animal.wildlife ? g.animal.wildlife.ActualSpeed : g.member.ActualSpeed;
        void FaceGuidance(Guidance g, Vector3 target)
        {
            if (g.animal.wildlife) g.animal.wildlife.FaceLifePoint(target, Time.deltaTime);
            else g.animal.herd.FaceLifePoint(g.animal.member, target, Time.deltaTime);
        }
        void StopGuidance(bool remind)
        {
            Guidance g = guidance; guidance = null;
            if (g == null) return;
            if (g.leased && (g.animal.wildlife && g.animal.wildlife.IntentRevision == g.revision ||
                g.animal.herd && g.member != null && g.member.intentRevision == g.revision))
            {
                if (g.animal.wildlife && !g.animal.wildlife.AutonomousTarget) g.animal.wildlife.ReleaseLifeTarget();
                else if (g.animal.herd && !g.animal.herd.IsAutonomous(g.animal.member)) g.animal.herd.ReleaseLifeTarget(g.animal.member);
            }
            if (remind && !g.reminderCue && Player)
                guidanceReminder = new GuidanceReminder { request = g.request, playerPosition = Player.position, due = Time.time + 120 };
        }
    }
}
