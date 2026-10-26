using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.WatersideV2;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    // Use only from the root's existing isolated formal-scene Play lifecycle.
    public static class LifeRoamingPlayChecks
    {
        public static IEnumerator Run(LifeSession life, Action<string> completed)
        {
            var checks = new List<string>(); Exception failure = null;
            IEnumerator steps = Steps(life, checks);
            while (true)
            {
                bool more; object next = null;
                try { more = steps.MoveNext(); if (more) next = steps.Current; }
                catch (Exception error) { failure = error; break; }
                if (!more) break;
                yield return next;
            }
            try { (steps as IDisposable)?.Dispose(); } catch (Exception error) { failure = failure ?? error; }
            string report = (failure == null ? "PASS" : "FAIL") + " animal roaming\n" + string.Join("\n", checks) + (failure == null ? "" : "\n" + failure);
            string folder = "LifeGameplayReview/20261027/animal-roaming";
            Directory.CreateDirectory(folder); File.WriteAllText(folder + "/play-checks.txt", report);
            completed?.Invoke(report);
        }

        static IEnumerator Steps(LifeSession life, List<string> checks)
        {
            Require(Application.isPlaying && life && life.World && life.Gameplay &&
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")), "Checks require the root's isolated Play save", checks);
            GameplayHud hud = life.GetComponent<GameplayHud>();
            LifeSpeaker speaker = life.World.FindSpeaker("dog");
            WildlifeAgent dog = speaker ? speaker.GetComponent<WildlifeAgent>() : null;
            var mover = life.Gameplay.forestSprite;
            var capsule = mover ? mover.GetComponent<CharacterController>() : null;
            Require(dog && mover && capsule && capsule.enabled && !mover.ScriptedInput && !hud.IsDialogueOpen && !life.Gameplay.Judging &&
                !life.Progress.tasks.Any(task => task != null && task.status == LifeTaskStatus.Active),
                "Fresh idle world starts with its real idle controller, no dialogue or active help", checks);
            WildlifePopulation population = UnityEngine.Object.FindObjectsOfType<WildlifePopulation>()
                .FirstOrDefault(candidate => candidate.agents.Contains(dog));
            Require(population, "The key dog belongs to a running wildlife simulation", checks);
            Vector3 playerPosition = mover.transform.position; Quaternion playerRotation = mover.transform.rotation;
            string progress = JsonUtility.ToJson(life.Progress);
            var secondary = UnityEngine.Object.FindObjectsOfType<WildlifeAgent>().Where(animal => animal.isActiveAndEnabled &&
                animal.locomotion == WildlifeLocomotion.Ground && !animal.GetComponent<LifeSpeaker>()).Take(12).ToArray();
            var starts = secondary.ToDictionary(animal => animal, animal => animal.transform.position);
            var moved = new HashSet<WildlifeAgent>();
            try
            {
                mover.SetScriptedInput(Vector2.zero, false, false);
                if (LifeWorld.Flat(mover.transform.position, dog.transform.position) <= 14)
                {
                    Require(TryFarPlayerPoint(life, dog, capsule, out Vector3 point), "A supported capsule-clear player viewpoint exists away from the unchanged dog", checks);
                    mover.transform.position = point; Physics.SyncTransforms();
                    yield return null; yield return null;
                }
                Require(LifeWorld.Flat(mover.transform.position, dog.transform.position) > 14, "The player is safely outside the key dog's nearby interaction range", checks);
                foreach (object tick in WaitSimulation(population, 2.5f)) yield return tick;
                Require(!dog.HasLifeTarget, "An idle key dog releases the fixed station lease instead of remaining pinned", checks);
                Vector3 origin = dog.transform.position; float furthest = 0;
                float began = population.SimulationTime, wallBegan = Time.realtimeSinceStartup, nextTrace = began;
                checks.Add("Dog habitat center=" + dog.habitatCenter.ToString("F3") + ", radius=" + dog.habitatRadius.ToString("F3") + ", routine=" + dog.routineMode);
                while (population.SimulationTime - began < 24)
                {
                    if (Time.realtimeSinceStartup - wallBegan > 192)
                        throw new TimeoutException("Wildlife simulation did not advance 24 seconds within the wall-clock guard");
                    furthest = Mathf.Max(furthest, LifeWorld.Flat(origin, dog.transform.position));
                    if (population.SimulationTime >= nextTrace)
                    {
                        checks.Add("TRACE sim=" + (population.SimulationTime - began).ToString("F2") + " position=" + dog.transform.position.ToString("F3") +
                            " destination=" + dog.Destination.ToString("F3") + " state=" + dog.State + " speed=" + dog.ActualSpeed.ToString("F3") + " lease=" + dog.HasLifeTarget);
                        nextTrace = population.SimulationTime + 1;
                    }
                    RequireWithinHabitat(dog);
                    foreach (WildlifeAgent animal in secondary)
                    {
                        RequireWithinHabitat(animal);
                        if (LifeWorld.Flat(starts[animal], animal.transform.position) > .75f) moved.Add(animal);
                    }
                    yield return null;
                }
                checks.Add("Observed simulation=" + (population.SimulationTime - began).ToString("F2") + " s, wall=" + (Time.realtimeSinceStartup - wallBegan).ToString("F2") + " s, furthest=" + furthest.ToString("F6") + " m, secondary moved=" + moved.Count);
                var task = new LifeTask { requestId = "dog.rain.lesson", animalId = speaker.Actor.animalId, eventId = "E0", status = LifeTaskStatus.Active };
                life.Progress.tasks.Add(task);
                foreach (object tick in WaitSimulation(population, .8f)) yield return tick;
                Require(dog.HasLifeTarget, "An actual active-help state preempts roaming through the existing station lease", checks);
                life.Progress.tasks.Remove(task);
                foreach (object tick in WaitSimulation(population, 2.1f)) yield return tick;
                Require(!dog.HasLifeTarget, "Removing active help restores ordinary life after the short lease expires", checks);
                Require(furthest > .75f, "The key dog actually changes its ground position while remaining in its existing local habitat (max travel=" + furthest.ToString("F6") + " m)", checks);
                Require(moved.Count >= 3, "At least three secondary animals visibly move within their own habitats during the observation (count=" + moved.Count + ")", checks);
            }
            finally
            {
                JsonUtility.FromJsonOverwrite(progress, life.Progress);
                mover.transform.SetPositionAndRotation(playerPosition, playerRotation);
                mover.ReleaseScriptedInput(); Physics.SyncTransforms();
            }
        }
        static IEnumerable<object> WaitSimulation(WildlifePopulation population, float seconds)
        {
            float start = population.SimulationTime, wallStart = Time.realtimeSinceStartup;
            while (population.SimulationTime - start < seconds)
            {
                if (Time.realtimeSinceStartup - wallStart > Mathf.Max(20, seconds * 8))
                    throw new TimeoutException("Wildlife simulation clock stopped during a roaming check");
                yield return null;
            }
        }
        static bool TryFarPlayerPoint(LifeSession life, WildlifeAgent dog, CharacterController capsule, out Vector3 point)
        {
            point = default; Terrain terrain = Terrain.activeTerrain;
            if (!terrain || !terrain.terrainData) return false;
            Transform player = life.Gameplay.PlayerTransform;
            var water = player.GetComponent<WatersideWaterBoundary>();
            Vector3 offset = player.TransformVector(capsule.center), size = terrain.terrainData.size;
            float height = capsule.height * Mathf.Abs(player.lossyScale.y);
            float radius = capsule.radius * Mathf.Max(Mathf.Abs(player.lossyScale.x), Mathf.Abs(player.lossyScale.z));
            float half = Mathf.Max(0, height * .5f - radius);
            foreach (float distance in new[] { 20f, 24f, 28f })
            for (int step = 0; step < 24; step++)
            {
                Vector3 candidate = dog.transform.position + Quaternion.Euler(0, step * 15, 0) * Vector3.forward * distance;
                Vector3 local = candidate - terrain.transform.position;
                if (local.x < 0 || local.z < 0 || local.x > size.x || local.z > size.z ||
                    terrain.terrainData.GetSteepness(local.x / size.x, local.z / size.z) > 30) continue;
                float ground = terrain.SampleHeight(candidate) + terrain.transform.position.y;
                if (water && water.surface && water.terrain && water.SampleSupportedDepth(candidate, ground) > water.maximumWadingDepth) continue;
                candidate.y = ground - offset.y + height * .5f + .08f;
                Vector3 center = candidate + offset;
                if (Physics.OverlapCapsule(center + Vector3.up * half, center - Vector3.up * half, radius * .95f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).Any(collider => !(collider is TerrainCollider) && !collider.transform.IsChildOf(player))) continue;
                point = candidate; return true;
            }
            return false;
        }
        static void RequireWithinHabitat(WildlifeAgent animal)
        {
            bool inside = animal.SolitaryRoutine != null ? animal.SolitaryRoutine.Contains(animal.transform.position, 1.45f) :
                LifeWorld.Flat(animal.transform.position, animal.habitatCenter) <= animal.habitatRadius * 1.45f;
            if (!inside) throw new InvalidOperationException("Animal left its own habitat including the existing alarm allowance: " + animal.speciesId + "/" + animal.spawnId);
        }
        static void Require(bool condition, string message, List<string> checks)
        { if (!condition) throw new InvalidOperationException(message); checks.Add("PASS " + message); }
    }
}
