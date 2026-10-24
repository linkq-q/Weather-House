using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;

namespace WeatherHouse.WatersideV2.Editor
{
    // One-shot migration of the inspected WatersideV2 instances. Never changes the shared prefabs.
    public static class WatersideAnimalScale
    {
        const string ScenePath = "Assets/Scenes/WeatherHouse_WatersideV2.unity";
        const string Output = "JourneyReview/Revision_20261024/AnimalScale/Reports";
        static readonly Vector2 Baseline = new Vector2(.82f, 1.16f);
        // The report-only whole-footprint and 48-neighbour review used this unchanged scene.
        const string PlacementSceneSha256 = "4B40889FC456EC6D9539F896FE2F582F84C397E4B9C0941DE1B2C00467B6EBCC";

        [Serializable] public sealed class Entry
        {
            public string species, objectId, footprintFailure;
            public int spawnId;
            public Vector2 current, before, after;
            public float multiplier, scaleBefore, scaleAfter, radiusBefore, radiusAfter;
            public Vector3 rootPosition, rootPositionAfter, runtimeStartBefore, runtimeStartAfter, bodySizeBefore, bodySizeAfter;
            public bool needsChange, needsMove, staticClearBefore, staticClearAfter, insideWorld, dry, groundProjectionBefore, groundProjectionAfter, footprintValid, footprintFlat;
        }
        [Serializable] public sealed class Report
        {
            public string utc, scene, status;
            public bool dirtyBefore, dirtyAfter, saved;
            public Entry[] entries;
        }

        public static Vector2 ForNewInstance(string species, Vector2 authoredRange)
        {
            float multiplier = species == "turtle" ? 3f : species == "squirrel" ? .5f : 1f;
            if (multiplier == 1f) return authoredRange;
            Vector2 result = authoredRange * multiplier;
            if (float.IsNaN(result.x) || float.IsNaN(result.y) || float.IsInfinity(result.x) || float.IsInfinity(result.y) ||
                result.x < .1f || result.y < result.x)
                throw new InvalidOperationException("Animal size range would be clamped or invalid: " + species);
            return result;
        }

        public static Vector2 TargetForExisting(string species, int spawnId, Vector2 current)
        {
            bool known = species == "turtle" && spawnId >= 43 && spawnId <= 47 ||
                         species == "squirrel" && spawnId >= 14 && spawnId <= 19;
            if (!known) throw new InvalidOperationException("Unexpected animal identity: " + species + "/" + spawnId);
            Vector2 target = ForNewInstance(species, Baseline);
            if (!Same(current, Baseline) && !Same(current, target))
                throw new InvalidOperationException("Individual size override changed; nothing applied: " + species + "/" + spawnId + " " + current);
            return target;
        }

        public static string Preview() => Write(BuildPlan(out _, out _));

        // Only the two reviewed, pre-existing overlaps may move. All other transforms are identity mappings.
        public static Vector3 TargetPositionForExisting(string species, int spawnId, Vector3 position, Quaternion rotation)
        {
            Vector3 original, target; float yaw;
            if (species == "squirrel" && spawnId == 14)
            { original = new Vector3(-2.4301033f, 4.48882f, -1.58714676f); target = new Vector3(-2.5551033f, 4.489524f, -1.58714676f); yaw = 223f; }
            else if (species == "turtle" && spawnId == 44)
            { original = new Vector3(-6.695938f, 4.48918152f, -11.4543962f); target = new Vector3(-5.823142f, 4.47956467f, -10.5595627f); yaw = 131f; }
            else return position;
            if (Quaternion.Angle(rotation, Quaternion.Euler(0, yaw, 0)) > .01f ||
                (position - original).sqrMagnitude > 1e-10f && (position - target).sqrMagnitude > 1e-10f)
                throw new InvalidOperationException("Reviewed animal pose changed; nothing moved: " + species + "/" + spawnId);
            return target;
        }

        public static string Apply()
        {
            Report report = BuildPlan(out Scene scene, out WildlifeAgent[] animals);
            if (scene.isDirty) throw new InvalidOperationException("Back up and resolve the dirty scene before applying animal size changes.");
            if (report.entries.All(e => !e.needsChange && !e.needsMove)) { report.status = "ALREADY_APPLIED"; return Write(report); }
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(ScenePath))
                if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") != PlacementSceneSha256)
                    throw new InvalidOperationException("Scene changed since the full-footprint and neighbour placement review; review again before applying.");
            if (report.entries.Any(e => !e.staticClearAfter || !e.insideWorld || !e.dry || !e.groundProjectionAfter || !e.footprintValid ||
                e.needsMove && (!e.footprintFlat || e.staticClearBefore)))
            {
                report.status = "BLOCKED_BY_PLACEMENT";
                throw new InvalidOperationException("Larger bodies need placement review; no animals moved or changed. " + Write(report));
            }
            // Complete the identity, override and placement checks before changing the first animal.
            Undo.RecordObjects(animals.Cast<UnityEngine.Object>().Concat(animals.Where((a, i) => report.entries[i].needsMove)
                .Select(a => (UnityEngine.Object)a.transform)).ToArray(), "调整水岸乌龟与松鼠尺寸及两处既有重叠");
            for (int i = 0; i < animals.Length; i++)
            {
                if (report.entries[i].needsChange)
                {
                    animals[i].scaleRange = report.entries[i].after;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(animals[i]);
                }
                if (report.entries[i].needsMove)
                {
                    animals[i].transform.position = report.entries[i].rootPositionAfter;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(animals[i].transform);
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            report.status = "APPLIED_UNSAVED"; report.dirtyAfter = scene.isDirty;
            return Write(report);
        }

        static Report BuildPlan(out Scene scene, out WildlifeAgent[] animals)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Animal size migration requires Edit mode.");
            scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || !scene.isLoaded || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Open only the existing WatersideV2 scene.");
            animals = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WildlifeAgent>(true))
                .Where(a => a.speciesId == "turtle" || a.speciesId == "squirrel").OrderBy(a => a.spawnId).ToArray();
            if (animals.Length != 11 || animals.Count(a => a.speciesId == "turtle") != 5 ||
                animals.Select(a => a.spawnId).Distinct().Count() != 11)
                throw new InvalidOperationException("Expected exactly the inspected 5 turtles and 6 squirrels.");
            var entries = new Entry[animals.Length];
            var physics = new WildlifeBodyPhysics();
            FieldInfo captured = typeof(WildlifeAgent).GetField("capturedScale", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < animals.Length; i++)
            {
                WildlifeAgent animal = animals[i];
                Vector2 target = TargetForExisting(animal.speciesId, animal.spawnId, animal.scaleRange);
                if (animal.locomotion != WildlifeLocomotion.Ground) throw new InvalidOperationException("Expected ground animals.");
                WildlifePopulation population = animal.GetComponentInParent<WildlifePopulation>();
                if (!population || population.Initialized || captured == null || (bool)captured.GetValue(animal))
                    throw new InvalidOperationException("Prediction requires a fresh, uninitialized scene animal.");
                if (AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(animal)) !=
                    "Assets/Game/World/Hillside/Generated/" + animal.speciesId + ".prefab")
                    throw new InvalidOperationException("The inspected animal prefab was replaced: " + animal.spawnId);
                Transform visual = animal.visualRoot;
                if (!visual || visual == animal.transform || !visual.IsChildOf(animal.transform))
                    throw new InvalidOperationException("Animal visual ownership is invalid.");
                foreach (Renderer renderer in animal.GetComponentsInChildren<Renderer>())
                    if (renderer.enabled && (!renderer.transform.IsChildOf(visual) ||
                        renderer is SkinnedMeshRenderer skin && skin.bones.Any(b => b && !b.IsChildOf(visual))))
                        throw new InvalidOperationException("Renderer or skin bones lie outside the scaled visual.");
                var randomBefore = new WildlifeRandom(population.worldSeed, animal.spawnId, animal.speciesId);
                var randomAfter = new WildlifeRandom(population.worldSeed, animal.spawnId, animal.speciesId);
                float before = randomBefore.Range(Baseline.x, Baseline.y), after = randomAfter.Range(target.x, target.y);
                WildlifeBodyEnvelope measured = WildlifeBodyEnvelope.Measure(animal.transform, animal.bodyRadius);
                if (!measured.measured || !measured.Valid) throw new InvalidOperationException("Cannot measure the actual animal mesh.");
                Vector3 pivot = Quaternion.Inverse(animal.transform.rotation) * (visual.parent.position - animal.transform.position);
                WildlifeBodyEnvelope Body(float scale) => new WildlifeBodyEnvelope
                    { center = pivot + (measured.center - pivot) * scale, half = measured.half * scale, measured = true };
                WildlifeBodyEnvelope oldBody = Body(before), newBody = Body(after);
                Vector3 position = animal.transform.position; Quaternion heading = animal.transform.rotation;
                Vector3 positionAfter = TargetPositionForExisting(animal.speciesId, animal.spawnId, position, heading);
                string objectId = GlobalObjectId.GetGlobalObjectIdSlow(animal).ToString();
                if (animal.spawnId == 14 && objectId != "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-4515995458381956832-421725884" ||
                    animal.spawnId == 44 && objectId != "GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-2988978243545320287-1649933174")
                    throw new InvalidOperationException("Reviewed relocation identity changed.");
                float oldRadius = Mathf.Max(.05f, animal.bodyRadius * before), radius = Mathf.Max(.05f, animal.bodyRadius * after);
                bool projectedBefore = PredictStart(population, oldRadius, position, out Vector3 startBefore);
                bool projectedAfter = PredictStart(population, radius, positionAfter, out Vector3 startAfter);
                Vector3 center = positionAfter + heading * newBody.center;
                Vector3 right = heading * new Vector3(newBody.half.x, 0, 0), forward = heading * new Vector3(0, 0, newBody.half.z);
                float extentX = Mathf.Abs(right.x) + Mathf.Abs(forward.x), extentZ = Mathf.Abs(right.z) + Mathf.Abs(forward.z);
                bool inWorld = center.x - extentX >= population.worldBounds.min.x && center.x + extentX <= population.worldBounds.max.x &&
                    center.z - extentZ >= population.worldBounds.min.z && center.z + extentZ <= population.worldBounds.max.z;
                bool dry = population.waterSurface ? !population.waterSurface.Contains(positionAfter, radius) :
                    !population.waterAreas.Any(w => w.Contains(positionAfter, -radius));
                bool footprintValid = Footprint(population, newBody, startAfter, heading, out string footprintFailure, out bool footprintFlat);
                entries[i] = new Entry { species = animal.speciesId, spawnId = animal.spawnId,
                    objectId = objectId, current = animal.scaleRange, before = Baseline, after = target,
                    multiplier = after / before, scaleBefore = before, scaleAfter = after,
                    radiusBefore = oldRadius, radiusAfter = radius,
                    rootPosition = position, rootPositionAfter = positionAfter, runtimeStartBefore = startBefore, runtimeStartAfter = startAfter,
                    groundProjectionBefore = projectedBefore, groundProjectionAfter = projectedAfter,
                    bodySizeBefore = oldBody.half * 2, bodySizeAfter = newBody.half * 2,
                    needsChange = !Same(animal.scaleRange, target), needsMove = (positionAfter - position).sqrMagnitude > 1e-10f,
                    insideWorld = inWorld, dry = dry, footprintValid = footprintValid, footprintFailure = footprintFailure, footprintFlat = footprintFlat,
                    staticClearBefore = physics.Clear(oldBody, startBefore, heading, startBefore, heading, population.obstacleMask, owner: animal.transform, ignoreAnimals: true),
                    staticClearAfter = physics.Clear(newBody, startAfter, heading, startAfter, heading, population.obstacleMask, owner: animal.transform, ignoreAnimals: true) };
            }
            return new Report { utc = DateTime.UtcNow.ToString("O"), scene = scene.path, status = "PREVIEW",
                dirtyBefore = scene.isDirty, dirtyAfter = scene.isDirty, entries = entries };
        }

        static bool Footprint(WildlifePopulation population, WildlifeBodyEnvelope body, Vector3 root, Quaternion heading, out string failure, out bool flat)
        {
            flat = false;
            failure = "Unsupported terrain or body orientation";
            if (!population.terrain || !population.terrain.terrainData || Vector3.Angle(heading * Vector3.up, Vector3.up) > .01f) return false;
            Terrain terrain = population.terrain; Vector3 size = terrain.terrainData.size; Bounds bounds = population.worldBounds;
            int nx = Mathf.Max(1, Mathf.CeilToInt(body.half.x * 2 / .125f)), nz = Mathf.Max(1, Mathf.CeilToInt(body.half.z * 2 / .125f));
            if (nx > 100 || nz > 100) { failure = "Footprint sampling limit"; return false; }
            float margin = new Vector2(body.half.x / nx, body.half.z / nz).magnitude, low = float.PositiveInfinity, high = float.NegativeInfinity, maximumHeightDifference = 0f;
            for (int x = 0; x <= nx; x++) for (int z = 0; z <= nz; z++)
            {
                Vector3 point = root + heading * (body.center + new Vector3(Mathf.Lerp(-body.half.x, body.half.x, (float)x / nx), -body.half.y,
                    Mathf.Lerp(-body.half.z, body.half.z, (float)z / nz)));
                Vector3 local = point - terrain.transform.position;
                if (point.x < bounds.min.x || point.x > bounds.max.x || point.z < bounds.min.z || point.z > bounds.max.z ||
                    local.x < 0 || local.z < 0 || local.x > size.x || local.z > size.z ||
                    terrain.terrainData.GetSteepness(local.x / size.x, local.z / size.z) > population.maxSlope)
                { failure = "Outside world/terrain or excessive slope at " + point.ToString("R"); return false; }
                if (population.waterSurface ? population.waterSurface.Contains(point, margin) : population.waterAreas.Any(w => w.Contains(point, -margin)))
                { failure = "Water intersects footprint at " + point.ToString("R") + " margin=" + margin.ToString("R"); return false; }
                float height = population.GroundHeight(point);
                maximumHeightDifference = Mathf.Max(maximumHeightDifference, Mathf.Abs(height - root.y));
                low = Mathf.Min(low, height); high = Mathf.Max(high, height);
            }
            // Flatness is an extra constraint on the two new placements, not a ban on existing animals living on slopes.
            // Preserve it in every report so enlarged bodies on slopes still receive actual Play visual review.
            flat = high - low <= .12f && maximumHeightDifference <= .12f;
            failure = flat ? "" : "Visual ground review: range=" + (high - low).ToString("R") + "; max root difference=" + maximumHeightDifference.ToString("R");
            return true;
        }

        // Match Initialize -> TryLandingPosition without changing ScaleFactor or any Transform.
        internal static bool PredictStart(WildlifePopulation population, float radius, Vector3 position, out Vector3 start)
        {
            start = position;
            Bounds bounds = population.worldBounds;
            if (position.x < bounds.min.x + radius || position.x > bounds.max.x - radius ||
                position.z < bounds.min.z + radius || position.z > bounds.max.z - radius) return false;
            if (population.terrain)
            {
                Vector3 local = position - population.terrain.transform.position, size = population.terrain.terrainData.size;
                if (local.x < 0 || local.x > size.x || local.z < 0 || local.z > size.z ||
                    population.terrain.terrainData.GetSteepness(local.x / size.x, local.z / size.z) > population.maxSlope) return false;
            }
            if (population.waterSurface ? population.waterSurface.Contains(position, radius) :
                population.waterAreas.Any(w => w.Contains(position, -radius))) return false;
            start.y = population.GroundHeight(position);
            return true;
        }

        static bool Same(Vector2 a, Vector2 b) => Mathf.Abs(a.x - b.x) < .00001f && Mathf.Abs(a.y - b.y) < .00001f;
        static string Write(Report report)
        {
            Directory.CreateDirectory(Output);
            string path = Path.Combine(Output, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + report.status + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            return path;
        }
    }
}
