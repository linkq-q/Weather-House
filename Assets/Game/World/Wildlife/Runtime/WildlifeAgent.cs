using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.Hillside
{
    public sealed class WildlifeAgent : MonoBehaviour
    {
        public string speciesId = "rabbit";
        public int spawnId, groupId;
        public WildlifeLocomotion locomotion;
        public Vector3 habitatCenter;
        public float habitatRadius = 10f;
        public int waterAreaIndex = -1;
        public Transform visualRoot;
        public Transform motionRoot;
        public Animator animator;
        public WildlifeClips clips = new WildlifeClips();
        public WildlifeForageContactData forageContactData;
        public Vector2 scaleRange = new Vector2(.85f, 1.15f);
        public float walkSpeed = 1f, runSpeed = 2.5f, bodyRadius = .3f;
        public float flightHeight = 5f, swimDepth = .4f;
        [Range(0f, 1f)] public float timidity = .6f, sociability = .5f;
        public bool startlesOthers, friendlyAcrossSpecies;
        public WildlifeRoutineMode routineMode;
        public WildlifeSolitaryProfile solitaryProfile = new WildlifeSolitaryProfile();
        public WildlifeState State { get; private set; }
        public float StateAge => population == null ? 0f : population.SimulationTime - stateSince;
        public float ScaleFactor { get; private set; } = 1f;
        public float Radius => Mathf.Max(.05f, bodyRadius * ScaleFactor);
        public WildlifeBodyEnvelope BodyEnvelope { get; private set; }
        public float ActualSpeed { get; private set; }
        public int AcceptedAlarmCount { get; private set; }
        public WildlifeAgent GreetingPartner => partner;
        public Vector3 Destination => destination;
        public float AirHeight => population == null ? flightHeight : altitude;
        public WildlifeSolitaryRoutine SolitaryRoutine => solitary;
        public WildlifeRestPhase RestPhase => restAnimation.Phase;

        private WildlifePopulation population;
        private WildlifeRandom random;
        private readonly WildlifeAnimation animationPlayer = new WildlifeAnimation();
        private readonly WildlifeRestSequence restAnimation = new WildlifeRestSequence();
        private readonly WildlifeForageContact forageContact = new WildlifeForageContact();
        private WildlifeAgent partner;
        private Vector3 baseVisualScale, baseVisualPosition, baseMotionPosition, destination, separation, threat;
        private float stateSince, stateUntil, nextGreeting, nextAlarm, nextDestination, speedVariation, altitude;
        private int lastAlarm;
        private bool capturedScale, capturedMotionPosition, landingApproach, fleeAfterTakeoff;
        private WildlifeSolitaryRoutine solitary;
        private SolitaryReachable solitaryReachable;
        private SolitaryWaterQuery solitaryWater;
        private SolitaryRestQuery solitaryRest;
        private float solitaryHeading;
        private bool solitaryWasWorking;
        private Transform solitaryHead;
        private Quaternion solitaryHeadPose;
        private bool solitaryPoseApplied;

        public void Initialize(WildlifePopulation owner)
        {
            forageContact.Restore();
            RestoreSolitaryPose();
            restAnimation.Reset();
            ReleasePartner();
            population = owner;
            random = new WildlifeRandom(owner.worldSeed, spawnId, speciesId);
            ScaleFactor = random.Range(Mathf.Max(.1f, scaleRange.x), Mathf.Max(.1f, scaleRange.y));
            speedVariation = random.Range(.88f, 1.12f);
            if (visualRoot != null)
            {
                if (!capturedScale)
                { baseVisualScale = visualRoot.localScale; baseVisualPosition = visualRoot.localPosition; capturedScale = true; }
                visualRoot.localScale = baseVisualScale * ScaleFactor;
                visualRoot.localPosition = baseVisualPosition * ScaleFactor;
            }
            if (motionRoot != null && !capturedMotionPosition)
            { baseMotionPosition = motionRoot.localPosition; capturedMotionPosition = true; }
            BodyEnvelope = WildlifeBodyEnvelope.Measure(transform, Radius);
            forageContact.Initialize(transform, clips != null && clips.forage != null && clips.forage != clips.idle ? forageContactData : null, owner.GroundHeight);
            if (animator == null) animator = GetComponentInChildren<Animator>();
            // Editor self-checks exercise decisions without starting a live animation graph.
            if (Application.isPlaying) animationPlayer.Initialize(animator, clips, random.Value());
            nextGreeting = owner.SimulationTime + random.Range(1f, 5f);
            nextAlarm = 0f; lastAlarm = 0; AcceptedAlarmCount = 0; ActualSpeed = 0f;
            separation = Vector3.zero;
            landingApproach = fleeAfterTakeoff = false;
            altitude = locomotion == WildlifeLocomotion.Flying && transform.position.y - owner.GroundHeight(transform.position) > 1f ? flightHeight : 0f;
            SetState(locomotion == WildlifeLocomotion.Flying && altitude > 0f ? WildlifeState.Fly :
                locomotion == WildlifeLocomotion.Swimming ? WildlifeState.Swim : WildlifeState.Idle,
                owner.SimulationTime, locomotion == WildlifeLocomotion.Flying ? random.Range(6f, 12f) : random.Range(1f, 3.5f));
            if (owner.TryProjectPosition(this, transform.position, out Vector3 initial)) transform.position = initial;
            destination = transform.position;
            nextDestination = 0f;
            solitary = locomotion == WildlifeLocomotion.Ground && routineMode == WildlifeRoutineMode.Solitary
                ? new WildlifeSolitaryRoutine(owner.worldSeed, spawnId, solitary == null ? transform.position : solitary.Home, habitatRadius,
                    walkSpeed, solitaryProfile, habitatCenter)
                : null;
            solitaryReachable = TrySolitaryPoint; solitaryWater = TrySolitaryWater;
            solitaryRest = clips != null && clips.HasRestSequence ? IsRestSurface : (SolitaryRestQuery)null;
            solitaryWasWorking = false;
            solitaryHead = null;
            if (solitary != null)
                foreach (Transform bone in GetComponentsInChildren<Transform>(true))
                    if (bone.name == "Head" || bone.name == "head" || (speciesId == "fox" && bone.name == "bone_1"))
                    { solitaryHead = bone; break; }
        }

        internal void Think(float now, List<WildlifeAgent> neighbours)
        {
            separation = population.ExternalSeparation(this);
            foreach (WildlifeAgent other in neighbours)
            {
                Vector3 away = transform.position - other.transform.position;
                away.y = 0f;
                float distance = away.magnitude;
                float spacing = population.BodySpacing(this, other, -away) + .45f;
                if (distance < spacing && distance > .01f) separation += away.normalized * (1f - distance / spacing);
                if (timidity > .15f && other.startlesOthers && other.ActualSpeed > Mathf.Max(.4f, other.walkSpeed * .65f) && distance < 2.8f &&
                    other.locomotion == locomotion && (string.IsNullOrEmpty(other.speciesId) || other.speciesId != speciesId) && now >= nextAlarm)
                    population.TryAmbientAlarm(other.transform.position, 5f, .8f, other);
            }
            if (population.player != null && timidity > .15f && now >= nextAlarm)
            {
                float distance = Vector3.Distance(transform.position, population.player.position);
                if (distance < .8f + timidity * .7f || (population.PlayerSpeed > 2.5f && distance < 3f + timidity * 2.5f))
                    population.TryAmbientAlarm(population.player.position, 5.5f, .9f);
            }

            if (State == WildlifeState.Alert)
            {
                if (now >= stateUntil)
                {
                    if (locomotion == WildlifeLocomotion.Flying && altitude < flightHeight * .5f)
                    { fleeAfterTakeoff = true; SetState(WildlifeState.Takeoff, now, ClipDuration(clips.takeoff, 1.6f)); }
                    else { SetState(WildlifeState.Flee, now, random.Range(2.5f, 4f)); ChooseFleeDestination(now); }
                }
                return;
            }
            if (State == WildlifeState.Flee)
            {
                if (now >= stateUntil)
                {
                    if (solitary != null) { ApplySolitary(now); return; }
                    SetState(locomotion == WildlifeLocomotion.Ground ? WildlifeState.ReturnHome : CruiseState(), now, random.Range(4f, 7f));
                    ChooseDestination(now, true);
                }
                else if (ReachedDestination() || now >= nextDestination) ChooseFleeDestination(now);
                return;
            }
            if (State == WildlifeState.Greet)
            {
                if (partner == null || !partner.isActiveAndEnabled || now >= stateUntil)
                {
                    ReleasePartner();
                    SetState(locomotion == WildlifeLocomotion.Swimming ? WildlifeState.Swim : WildlifeState.Idle, now, random.Range(2f, 4f));
                }
                return;
            }
            if (State == WildlifeState.Takeoff)
            {
                if (now >= stateUntil)
                {
                    altitude = flightHeight;
                    SetState(fleeAfterTakeoff ? WildlifeState.Flee : WildlifeState.Fly, now, fleeAfterTakeoff ? random.Range(2.5f, 4f) : random.Range(9f, 16f));
                    if (fleeAfterTakeoff) ChooseFleeDestination(now); else ChooseDestination(now, false);
                    fleeAfterTakeoff = false;
                }
                return;
            }
            if (State == WildlifeState.Landing)
            {
                if (now >= stateUntil)
                { altitude = 0f; SetState(random.Value() < .7f ? WildlifeState.Forage : WildlifeState.Idle, now, random.Range(4f, 9f)); }
                return;
            }
            if (solitary != null)
            {
                if (CanGreet(now) && random.Value() < sociability * .04f)
                    foreach (WildlifeAgent other in neighbours)
                        if (population.TryGreet(this, other)) return;
                ApplySolitary(now);
                return;
            }
            if (locomotion == WildlifeLocomotion.Ground && HorizontalDistance(transform.position, habitatCenter) > habitatRadius * 1.15f)
            {
                if (State != WildlifeState.ReturnHome) { SetState(WildlifeState.ReturnHome, now, 8f); ChooseDestination(now, true); }
            }
            if (CanGreet(now) && random.Value() < sociability * .16f)
                foreach (WildlifeAgent other in neighbours)
                    if (population.TryGreet(this, other)) return;

            if (locomotion == WildlifeLocomotion.Flying)
            {
                if (State == WildlifeState.Idle || State == WildlifeState.Forage)
                {
                    if (now >= stateUntil) SetState(WildlifeState.Takeoff, now, ClipDuration(clips.takeoff, 1.6f));
                }
                else if (landingApproach)
                {
                    if (ReachedDestination())
                    { landingApproach = false; SetState(WildlifeState.Landing, now, ClipDuration(clips.land, 1.6f)); }
                    else if (now >= nextDestination) FindLanding(now);
                }
                else if (now >= stateUntil) FindLanding(now);
                else if (ReachedDestination() || now >= nextDestination) ChooseDestination(now, false);
                return;
            }
            if (locomotion == WildlifeLocomotion.Swimming)
            {
                if (ReachedDestination() || now >= nextDestination) ChooseDestination(now, false);
                return;
            }
            if (State == WildlifeState.ReturnHome && ReachedDestination())
                SetState(WildlifeState.Idle, now, random.Range(1f, 2f));
            if (now >= stateUntil || (State == WildlifeState.Wander && ReachedDestination()))
            {
                float choice = random.Value();
                WildlifeState next = choice < .25f ? WildlifeState.Idle : choice < .58f ? WildlifeState.Forage : WildlifeState.Wander;
                SetState(next, now, random.Range(2.5f, 6.5f));
                if (next == WildlifeState.Wander) ChooseDestination(now, false);
            }
            if ((State == WildlifeState.Wander || State == WildlifeState.ReturnHome) && now >= nextDestination)
                ChooseDestination(now, State == WildlifeState.ReturnHome);
        }

        private WildlifeState CruiseState()
        { return locomotion == WildlifeLocomotion.Flying ? WildlifeState.Fly : WildlifeState.Swim; }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        { return new Vector2(a.x - b.x, a.z - b.z).magnitude; }

        private bool ReachedDestination() { return HorizontalDistance(transform.position, destination) < Radius + .25f; }

        private static float ClipDuration(AnimationClip clip, float fallback)
        { return clip != null && clip.length > .1f ? clip.length : fallback; }

        private void FindLanding(float now)
        {
            for (int i = 0; i < 18; i++)
            {
                Vector2 offset = random.InCircle(10f);
                Vector3 candidate = transform.position + new Vector3(offset.x, 0f, offset.y);
                if (!population.TryLandingPosition(this, candidate, out _)) continue;
                if (!population.TryMove(this, candidate, out Vector3 approach)) continue;
                destination = approach; landingApproach = true; nextDestination = now + 7f;
                return;
            }
            landingApproach = false; stateUntil = now + 4f; ChooseDestination(now, false);
        }

        private void ChooseDestination(float now, bool home)
        {
            if (solitary != null) { ApplySolitary(now); return; }
            nextDestination = now + random.Range(3.5f, 7f);
            Vector3 center = home ? habitatCenter : population.GroupCenter(groupId, habitatCenter);
            if (locomotion == WildlifeLocomotion.Ground)
                center = Vector3.Lerp(habitatCenter, center, .4f);
            for (int attempt = 0; attempt < 14; attempt++)
            {
                Vector3 candidate;
                if (locomotion == WildlifeLocomotion.Flying)
                {
                    if (speciesId == "robin")
                    {
                        var slot = new WildlifeRandom(population.worldSeed, spawnId, "robin_flock_slot");
                        Vector2 offset = slot.InCircle(1.7f);
                        if (attempt > 0) offset += random.InCircle(1);
                        candidate = population.RobinFlightWaypoint(groupId, habitatCenter) + new Vector3(offset.x, 0f, offset.y);
                    }
                    else
                    {
                        Vector2 offset = random.InCircle(Mathf.Max(12f, habitatRadius));
                        candidate = center + new Vector3(offset.x, 0f, offset.y);
                    }
                    candidate.x = Mathf.Clamp(candidate.x, population.worldBounds.min.x + 2f, population.worldBounds.max.x - 2f);
                    candidate.z = Mathf.Clamp(candidate.z, population.worldBounds.min.z + 2f, population.worldBounds.max.z - 2f);
                }
                else if (locomotion == WildlifeLocomotion.Swimming && waterAreaIndex >= 0 && waterAreaIndex < population.waterAreas.Length)
                {
                    HabitatVolume water = population.waterAreas[waterAreaIndex];
                    Vector2 offset = random.InCircle(.82f);
                    candidate = water.center + new Vector3(offset.x * water.radii.x, 0f, offset.y * water.radii.y);
                }
                else
                {
                    Vector2 offset = random.InCircle(home ? Mathf.Min(2f, habitatRadius) : habitatRadius * .6f);
                    candidate = center + new Vector3(offset.x, 0f, offset.y);
                    if (HorizontalDistance(candidate, habitatCenter) > habitatRadius) continue;
                }
                if (!population.TryMove(this, candidate, out Vector3 valid)) continue;
                destination = valid;
                return;
            }
            destination = transform.position;
            nextDestination = now + .8f;
        }

        private void ChooseFleeDestination(float now)
        {
            Vector3 away = transform.position - threat;
            away.y = 0f;
            if (away.sqrMagnitude < .01f) away = transform.forward;
            away.Normalize();
            for (int i = 0; i < 12; i++)
            {
                float angle = i == 0 ? 0f : ((i + 1) / 2) * 22f * (i % 2 == 0 ? -1f : 1f);
                Vector3 candidate = transform.position + Quaternion.Euler(0f, angle, 0f) * away * random.Range(2.5f, 6f);
                if (locomotion == WildlifeLocomotion.Ground && (solitary == null
                    ? HorizontalDistance(candidate, habitatCenter) > habitatRadius * 1.4f : !solitary.Contains(candidate, 1.4f))) continue;
                if (!population.TryMove(this, candidate, out Vector3 valid)) continue;
                destination = valid; nextDestination = now + 1.5f;
                return;
            }
            destination = transform.position; nextDestination = now + .5f;
        }

        private void ApplySolitary(float now)
        {
            solitary.Decide(now, transform.position, solitaryReachable, solitaryWater, solitaryRest);
            bool working = solitary.IsWorking;
            if (working && !solitaryWasWorking) solitaryHeading = transform.eulerAngles.y;
            solitaryWasWorking = working;
            destination = solitary.HasTarget ? solitary.Target : transform.position;
            WildlifeState state = !solitary.HasTarget || working
                ? solitary.Activity == SolitaryActivity.Forage ? WildlifeState.Forage : WildlifeState.Idle
                : solitary.Activity == SolitaryActivity.Rest ? WildlifeState.ReturnHome : WildlifeState.Wander;
            if (State != state) SetState(state, now, 1000f);
            nextDestination = now + 1000f;
        }

        private bool TrySolitaryPoint(Vector3 requested, out Vector3 projected)
        { return population.TryMove(this, requested, out projected); }

        private bool IsRestSurface(Vector3 point)
        {
            // The full body can finish within Arrival (18 cm) of its target and
            // face any heading. Sample that swept footprint, not only its pivot.
            float radius = BodyEnvelope.HorizontalRadius + .18f;
            float low = population.GroundHeight(point), high = low;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * .25f;
                Vector3 edge = point + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                float height = population.GroundHeight(edge);
                low = Mathf.Min(low, height); high = Mathf.Max(high, height);
            }
            return high - low <= .04f;
        }

        private bool TrySolitaryWater(Vector3 standingPoint, out Vector3 waterPoint)
        {
            waterPoint = default;
            if (!population.waterSurface || population.waterSurface.Contains(standingPoint, Radius + .08f)) return false;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * .25f;
                Vector3 point = standingPoint + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .65f;
                if (!population.waterSurface.Contains(point)) continue;
                point.y = population.waterSurface.Height(point);
                if (Mathf.Abs(point.y - standingPoint.y) > .4f) continue;
                waterPoint = point; return true;
            }
            return false;
        }

        internal void Move(float dt, float now)
        {
            bool resting = solitary != null && solitary.IsWorking && solitary.Activity == SolitaryActivity.Rest && State == WildlifeState.Idle;
            restAnimation.Tick(resting, State == WildlifeState.Alert || State == WildlifeState.Flee, dt, clips, animationPlayer.HasReachedBoundary(restAnimation.Clip, restAnimation.Speed));
            if (restAnimation.BlocksMovement)
            {
                ActualSpeed = 0f;
                solitary?.Advance(dt, transform.position, resting, false);
                animationPlayer.Play(restAnimation.Clip, restAnimation.Phase == WildlifeRestPhase.Sleeping, restAnimation.Speed, restAnimation.StartTime);
                animationPlayer.Tick(dt);
                return;
            }
            if (State == WildlifeState.Takeoff)
                altitude = Mathf.MoveTowards(altitude, flightHeight, flightHeight * dt / Mathf.Max(.1f, stateUntil - stateSince));
            else if (State == WildlifeState.Landing)
                altitude = Mathf.MoveTowards(altitude, 0f, flightHeight * dt / Mathf.Max(.1f, stateUntil - stateSince));
            bool moving = State == WildlifeState.Wander || State == WildlifeState.Flee || State == WildlifeState.ReturnHome ||
                State == WildlifeState.Fly || State == WildlifeState.Swim;
            Vector3 direction = destination - transform.position;
            direction.y = 0f;
            if (State == WildlifeState.Greet && partner != null)
            {
                direction = partner.transform.position - transform.position;
                direction.y = 0f;
                float gap = locomotion == WildlifeLocomotion.Ground ? .025f : .35f;
                moving = direction.magnitude > population.BodySpacing(this, partner, direction) + gap;
            }
            float speed = (State == WildlifeState.Flee ? runSpeed : walkSpeed) * speedVariation;
            Quaternion proposedRotation = transform.rotation;
            if ((moving || State == WildlifeState.Greet) && direction.sqrMagnitude > .001f)
                proposedRotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), dt * (State == WildlifeState.Flee ? 300f : 150f));
            if (moving && direction.sqrMagnitude > .01f)
            {
                Vector3 steering = direction.normalized + separation * 1.3f;
                steering.y = 0f;
                if (steering.sqrMagnitude < .001f) steering = direction.normalized;
                Vector3 displacement = steering.normalized * Mathf.Min(speed * dt, direction.magnitude);
                Vector3 before = transform.position;
                bool accepted = population.TryMove(this, before + displacement, out Vector3 next, proposedRotation);
                // A neighbour may block turning while a short step without turning can safely back out.
                if (!accepted && locomotion == WildlifeLocomotion.Ground && proposedRotation != transform.rotation &&
                    population.TryMove(this, before + displacement, out next, transform.rotation))
                { accepted = true; proposedRotation = transform.rotation; }
                if (accepted)
                {
                    transform.position = next;
                    transform.rotation = proposedRotation;
                    ActualSpeed = HorizontalDistance(before, next) / Mathf.Max(.001f, dt);
                }
                else { ActualSpeed = 0f; nextDestination = Mathf.Min(nextDestination, now + .1f); }
            }
            else
            {
                ActualSpeed = 0f;
                if (locomotion == WildlifeLocomotion.Flying && (State == WildlifeState.Takeoff || State == WildlifeState.Landing || altitude == 0f))
                {
                    if (population.TryMove(this, transform.position, out Vector3 rise)) transform.position = rise;
                    else if (State == WildlifeState.Landing)
                    { landingApproach = false; SetState(WildlifeState.Takeoff, now, ClipDuration(clips.takeoff, 1.6f)); }
                }
            }
            if ((moving || State == WildlifeState.Greet) && direction.sqrMagnitude > .001f)
                if (population.CanRotate(this, proposedRotation)) transform.rotation = proposedRotation;
            if (solitary != null)
            {
                bool available = State != WildlifeState.Alert && State != WildlifeState.Flee && State != WildlifeState.Greet;
                solitary.Advance(dt, transform.position, available, ActualSpeed > .05f);
                if (available && solitary.IsWorking && !(solitary.Activity == SolitaryActivity.Forage && clips != null && clips.forage != null && clips.forage != clips.idle))
                {
                    // Existing idle clips remain idle; small ground-preserving turns suggest looking/sniffing, not a new eating or sleeping animation.
                    float yaw = solitaryHeading + Mathf.Sin(now * .7f + spawnId) * (solitary.Activity == SolitaryActivity.Rest ? 3f : 8f);
                    if (solitary.Activity == SolitaryActivity.Drink)
                    {
                        Vector3 towardWater = solitary.WaterPoint - transform.position; towardWater.y = 0;
                        if (towardWater.sqrMagnitude > .001f) yaw = Quaternion.LookRotation(towardWater).eulerAngles.y;
                    }
                    Quaternion rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0, yaw, 0), dt * 25f);
                    if (population.CanRotate(this, rotation)) transform.rotation = rotation;
                }
            }
            AnimationClip clip = clips == null ? null : clips.ForState(State, locomotion);
            bool repeat = State != WildlifeState.Alert && State != WildlifeState.Takeoff && State != WildlifeState.Landing;
            float clipSpeed = State == WildlifeState.Flee && locomotion != WildlifeLocomotion.Flying && clips != null &&
                clips.run != null && clips.run != clips.walk ? runSpeed : walkSpeed;
            float playback = ActualSpeed > .05f ? Mathf.Clamp(ActualSpeed / Mathf.Max(.1f, clipSpeed), .65f, 1.8f) : 1f;
            animationPlayer.Play(clip, repeat, playback);
            animationPlayer.Tick(dt);
        }

        public bool ReceiveStartle(WildlifeAlarm alarm, float now)
        {
            if (alarm.source == this || (!string.IsNullOrEmpty(alarm.sourceSpecies) && alarm.sourceSpecies == speciesId)) return false;
            if (alarm.id <= lastAlarm || now > alarm.expiresAt || now < nextAlarm || alarm.intensity * timidity <= .12f || timidity <= .15f) return false;
            if ((transform.position - alarm.origin).sqrMagnitude > alarm.radius * alarm.radius) return false;
            lastAlarm = alarm.id;
            AcceptedAlarmCount++;
            nextAlarm = now + random.Range(7f, 11f);
            threat = alarm.origin;
            landingApproach = false;
            solitary?.Interrupt(true); solitaryWasWorking = false;
            ReleasePartner();
            SetState(WildlifeState.Alert, now, random.Range(.25f, .65f));
            return true;
        }

        public bool CanGreet(float now)
        {
            return isActiveAndEnabled && !restAnimation.BlocksMovement && partner == null && sociability > 0f && now >= nextGreeting &&
                State != WildlifeState.Alert && State != WildlifeState.Flee && State != WildlifeState.Takeoff && State != WildlifeState.Landing && State != WildlifeState.ReturnHome &&
                (locomotion != WildlifeLocomotion.Flying || altitude < .01f);
        }

        internal void BeginGreeting(WildlifeAgent other, float now)
        {
            partner = other;
            solitary?.Interrupt(false); solitaryWasWorking = false;
            nextGreeting = now + (solitary == null ? random.Range(10f, 18f) : random.Range(30f, 55f));
            SetState(WildlifeState.Greet, now, 1.8f);
        }

        private void ReleasePartner()
        {
            WildlifeAgent other = partner;
            partner = null;
            if (other != null && other.partner == this)
            {
                other.partner = null;
                if (other.State == WildlifeState.Greet)
                    other.SetState(other.locomotion == WildlifeLocomotion.Swimming ? WildlifeState.Swim : WildlifeState.Idle,
                        other.population == null ? 0f : other.population.SimulationTime, 1.5f);
            }
        }

        private void SetState(WildlifeState state, float now, float duration)
        { State = state; stateSince = now; stateUntil = now + duration; }

        private void Update() { RestoreSolitaryPose(); forageContact.Restore(); }

        private void RestoreSolitaryPose()
        {
            if (solitaryPoseApplied && solitaryHead) solitaryHead.localRotation = solitaryHeadPose;
            solitaryPoseApplied = false;
        }

        private void LateUpdate()
        {
            // Robin clips bake travel into body. World flight owns that travel; retain the wing/leg pose only.
            if (locomotion == WildlifeLocomotion.Flying && motionRoot != null && capturedMotionPosition)
                motionRoot.localPosition = baseMotionPosition;
            if (!restAnimation.BlocksMovement && solitaryHead && solitary != null && solitary.IsWorking && (State == WildlifeState.Forage || State == WildlifeState.Idle) &&
                ((solitary.Activity == SolitaryActivity.Forage && (clips == null || clips.forage == null || clips.forage == clips.idle)) || solitary.Activity == SolitaryActivity.Drink || solitary.Activity == SolitaryActivity.Watch))
            {
                if (solitaryPoseApplied) RestoreSolitaryPose();
                solitaryHeadPose = solitaryHead.localRotation; solitaryPoseApplied = true;
                float sway = Mathf.Sin(population.SimulationTime * 1.7f + spawnId);
                float pitch = solitary.Activity == SolitaryActivity.Watch ? 0 : (solitary.Activity == SolitaryActivity.Drink ? 10f : 6f) + sway * 2f;
                float yaw = solitary.Activity == SolitaryActivity.Watch ? sway * 6f : sway * 2f;
                solitaryHead.rotation = Quaternion.AngleAxis(pitch, transform.right) * Quaternion.AngleAxis(yaw, transform.up) * solitaryHead.rotation;
            }
            if (forageContact.Enabled && clips != null) forageContact.Apply(animationPlayer.WeightOf(clips.forage));
        }

        private void OnEnable()
        {
            if (population != null && Application.isPlaying)
                animationPlayer.Initialize(animator, clips, 0f);
        }
        private void OnDisable() { forageContact.Restore(); RestoreSolitaryPose(); restAnimation.Reset(); ReleasePartner(); animationPlayer.Dispose(); }
        private void OnDestroy() { animationPlayer.Dispose(); }
    }
}
