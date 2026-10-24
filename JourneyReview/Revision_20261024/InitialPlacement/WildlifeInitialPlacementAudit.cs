using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using WeatherHouse.WildlifeJourney;

namespace WeatherHouse.Review
{
    // External editor DLL. No scene objects are created, moved, initialized or saved.
    public static class WildlifeInitialPlacementAudit
    {
        [Serializable] public sealed class Contact
        {
            public string path, colliderType, category;
            public int layer;
            public bool blocksBody, inRuntimeMask, underFeet;
            public Vector3 boundsCenter, boundsSize;
        }
        [Serializable] public sealed class Candidate
        {
            public Vector3 position, displacement;
            public float yaw, distance, horizontalDistance, heightDifference, maximumSlope, footprintHeightRange;
            public bool translationOnly = true, bodyClear = true, dry = true;
            public string support, meaning = "Independent destination clearance only; no escape path or simultaneous placement is asserted.";
            public string[] triggeringColliders;
        }
        [Serializable] public sealed class Animal
        {
            public string kind, species, path, support, note;
            public int id, staticBlockerCount, queriedStaticOverlapCount;
            public bool active, measured, runtimePredictionReliable, initialGroundProjection;
            public float predictedScale, predictedRadius;
            public Vector3 position, predictedStartPosition, euler, measuredCenter, measuredHalf, bodyCenter, bodyHalf;
            public Contact[] editContacts, contacts;
            public Candidate[] candidates;
        }
        [Serializable] public sealed class Report
        {
            public string generatedUtc, scene, outputDirectory;
            public bool dirtyBefore, dirtyAfter, transformsUnchanged;
            public int animalCount, animalsWithStaticOverlap, animalsWithRuntimeBlocker;
            public float searchRadius = 3f, ringSpacing = .25f, maximumHeightDifference = .45f, footprintSpacing = .25f;
            public string[] rules;
            public Animal[] animals;
        }
        private sealed class Body
        {
            public Transform root;
            public WildlifeAgent agent;
            public JourneyHerd herd;
            public WildlifePopulation population;
            public JourneyNavigation navigation;
            public WildlifeBodyEnvelope shape;
            public Vector3 start;
            public int mask;
            public Animal result;
            public float MaxSlope => navigation ? navigation.maxSlope : population ? population.maxSlope : 29f;
            public float MaxRise => navigation ? Mathf.Min(.45f, navigation.maxStepHeight) : .45f;
        }
        private struct Pose
        {
            public Transform transform;
            public Vector3 position, scale;
            public Quaternion rotation;
        }
        private sealed class World
        {
            public Scene scene;
            public WildlifePopulation[] populations;
            public JourneyNavigation[] navigation;
            public Terrain[] terrain;
            public WeatherHouse.WorldWaterSurface[] water;
            public readonly HashSet<Transform> players = new HashSet<Transform>();
            public readonly HashSet<Collider> decks = new HashSet<Collider>();
            public readonly List<Body> bodies = new List<Body>();
            public bool Player(Collider hit) => players.Any(p => p && hit.transform.IsChildOf(p));
        }

        public static string Run() => RunReport().outputDirectory;

        public static Report RunReport()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Initial placement audit is Edit-mode only.");
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene) ||
                scene.GetPhysicsScene() != Physics.defaultPhysicsScene)
                throw new InvalidOperationException("Audit requires the currently loaded ordinary scene in the default physics scene.");
            int ordinaryScenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
                .Count(s => s.isLoaded && !EditorSceneManager.IsPreviewScene(s));
            if (ordinaryScenes != 1)
                throw new InvalidOperationException("Initial placement audit requires exactly one loaded ordinary scene; shared-physics additive scenes are not silently ignored.");
            GameObject[] roots = scene.GetRootGameObjects();
            T[] Components<T>() where T : Component => roots.SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
            Pose[] before = Components<Transform>().Select(t => new Pose
                { transform = t, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
            var report = new Report { generatedUtc = DateTime.UtcNow.ToString("O"), scene = scene.path, dirtyBefore = scene.isDirty };
            var world = new World { scene = scene, populations = Components<WildlifePopulation>(), navigation = Components<JourneyNavigation>(),
                terrain = Components<Terrain>(), water = Components<WeatherHouse.WorldWaterSurface>() };
            foreach (WildlifePopulation population in world.populations)
                if (population.player) world.players.Add(population.player.GetComponentInParent<CharacterController>()?.transform ?? population.player);
            foreach (CharacterController player in Components<CharacterController>()) world.players.Add(player.transform);
            foreach (JourneyNavigation navigation in world.navigation)
                if (navigation.bridges != null)
                    foreach (JourneyBridgeSurface bridge in navigation.bridges)
                        if (bridge != null && bridge.id > 0 && bridge.surface) world.decks.Add(bridge.surface);
            var seen = new HashSet<Transform>();
            foreach (JourneyHerd herd in Components<JourneyHerd>())
            {
                if (!herd.isActiveAndEnabled) continue;
                foreach (JourneyMember member in herd.members ?? new JourneyMember[0])
                {
                    if (member == null || !member.root || !member.root.gameObject.activeInHierarchy || !seen.Add(member.root)) continue;
                    float radius = member.profile == null ? .3f : member.profile.bodyRadius;
                    WildlifeBodyEnvelope measured = WildlifeBodyEnvelope.Measure(member.root, radius);
                    Add(world, member.root, null, herd, herd.surroundingPopulation, herd.navigation,
                        measured, measured.WithLength(member.bodyLength), member.root.position, true, 1, radius, false, member.id, "JourneyHerd", "sheep", "Herd bodyLength applied; no runtime random scale.");
                }
            }
            foreach (WildlifeAgent agent in Components<WildlifeAgent>())
            {
                if (!agent.isActiveAndEnabled || agent.locomotion != WildlifeLocomotion.Ground || !seen.Add(agent.transform)) continue;
                WildlifePopulation population = world.populations.FirstOrDefault(p => p.agents != null && Array.IndexOf(p.agents, agent) >= 0);
                if (!population) population = agent.GetComponentInParent<WildlifePopulation>();
                WildlifeBodyEnvelope measured = WildlifeBodyEnvelope.Measure(agent.transform, agent.Radius);
                bool reliable = PredictBody(agent, population, measured, out WildlifeBodyEnvelope runtime, out float scale, out string note);
                float radius = Mathf.Max(.05f, agent.bodyRadius * scale);
                Vector3 start = agent.transform.position;
                bool projected = PredictStart(population, radius, ref start);
                Add(world, agent.transform, agent, null, population, null, measured, runtime, start, reliable, scale, radius, projected,
                    agent.spawnId, "WildlifeAgent", agent.speciesId, note);
            }
            Physics.SyncTransforms();
            foreach (Body body in world.bodies)
            {
                WildlifeBodyEnvelope runtime = body.shape;
                body.shape = new WildlifeBodyEnvelope { center = body.result.measuredCenter, half = body.result.measuredHalf, measured = body.result.measured };
                body.result.editContacts = Contacts(world, body, body.root.position, false).ToArray();
                body.shape = runtime;
                var contacts = Contacts(world, body, body.start, false);
                body.result.contacts = contacts.ToArray();
                body.result.queriedStaticOverlapCount = contacts.Count(c => c.blocksBody && c.category != "animal_contact");
                body.result.staticBlockerCount = contacts.Count(c => c.blocksBody && c.category != "animal_contact" && c.inRuntimeMask);
                if (Surface(world, body, body.start, out JourneySurface ground))
                    body.result.support = SupportName(ground);
                bool uncertainNeighbour = world.bodies.Any(other => other != body && !other.result.runtimePredictionReliable);
                if (uncertainNeighbour) body.result.note += " Candidate certification withheld: another active animal has unknown runtime bounds.";
                body.result.candidates = body.result.queriedStaticOverlapCount == 0 || !body.result.runtimePredictionReliable || uncertainNeighbour ?
                    new Candidate[0] : Candidates(world, body).ToArray();
            }
            report.animals = world.bodies.Select(b => b.result).ToArray();
            report.animalCount = report.animals.Length;
            report.animalsWithStaticOverlap = report.animals.Count(a => a.queriedStaticOverlapCount > 0);
            report.animalsWithRuntimeBlocker = report.animals.Count(a => a.staticBlockerCount > 0);
            report.dirtyAfter = scene.isDirty;
            report.transformsUnchanged = before.All(p => p.transform && p.transform.localPosition == p.position &&
                p.transform.localRotation == p.rotation && p.transform.localScale == p.scale);
            report.rules = new[] {
                "Edit geometry is measured with the shipped WildlifeBodyEnvelope.Measure. Ground Agent runtime scale and Terrain projection are predicted without mutating any Transform or calling Initialize.",
                "Predicted scaling is reliable only for a fresh uncaptured visualRoot with all enabled renderers and referenced skin bones under it, or no scaled visualRoot. Mixed/previously initialized cases get no guaranteed candidates.",
                "OverlapBox queries all enabled non-trigger collider layers in the active scene. inRuntimeMask is reported separately.",
                "queriedStaticOverlapCount includes all layers; staticBlockerCount includes only obstacles in the animal controller's runtime mask. Candidate clearance deliberately uses all layers.",
                "Terrain, self and player are excluded from static overlap counts. A deck is exempt only when this body's own JourneyNavigation selects it as its registered supporting collider; Legacy Agent has no deck exemption.",
                "An unregistered collider under the feet remains a blocker, labelled unregistered_ground_contact for human verification; a rock is never silently treated as ground.",
                "Candidates preserve yaw and scale, are independent alternatives within 3m, and avoid other measured ground-animal bodies conservatively.",
                "Each candidate checks the oriented body, a 0.25m footprint grid, dry water clearance, slope, height range and 0.45m maximum original-height difference.",
                "Candidate paths from an already overlapping start are not asserted. No scene object, material, collider, transform or saved scene is changed." };
            string project = Path.GetDirectoryName(Application.dataPath);
            report.outputDirectory = Path.Combine(project, "JourneyReview", "Revision_20261024", "InitialPlacement", "Reports", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(report.outputDirectory);
            File.WriteAllText(Path.Combine(report.outputDirectory, "report.json"), JsonFields(report), new UTF8Encoding(false));
            WriteCsv(report);
            string summary = "Initial placement audit: " + report.animalCount + " ground animals; " + report.animalsWithStaticOverlap +
                " queried static overlaps, " + report.animalsWithRuntimeBlocker + " with runtime-mask blockers; transformsUnchanged=" + report.transformsUnchanged + "; dirty " + report.dirtyBefore + " -> " + report.dirtyAfter +
                "; " + report.outputDirectory;
            File.WriteAllText(Path.Combine(report.outputDirectory, "summary.txt"), summary, new UTF8Encoding(false));
            Debug.Log(summary);
            return report;
        }

        private static void Add(World world, Transform root, WildlifeAgent agent, JourneyHerd herd, WildlifePopulation population,
            JourneyNavigation navigation, WildlifeBodyEnvelope measured, WildlifeBodyEnvelope shape, Vector3 start, bool reliable,
            float scale, float radius, bool projected, int id, string kind, string species, string note)
        {
            world.bodies.Add(new Body { root = root, agent = agent, herd = herd, population = population, navigation = navigation,
                shape = shape, start = start, mask = navigation ? navigation.obstacleMask.value : population ? population.obstacleMask.value : Physics.DefaultRaycastLayers,
                result = new Animal { kind = kind, species = species, id = id, path = PathOf(root), active = root.gameObject.activeInHierarchy,
                    measured = measured.measured, position = root.position, predictedStartPosition = start, euler = root.eulerAngles,
                    measuredCenter = measured.center, measuredHalf = measured.half, bodyCenter = shape.center, bodyHalf = shape.half,
                    runtimePredictionReliable = reliable && measured.measured, predictedScale = scale, predictedRadius = radius, initialGroundProjection = projected,
                    note = note + (measured.measured ? "" : " No mesh bounds; fallback envelope only, no guaranteed candidate.") } });
        }

        private static bool PredictBody(WildlifeAgent agent, WildlifePopulation population, WildlifeBodyEnvelope edit,
            out WildlifeBodyEnvelope predicted, out float scale, out string note)
        {
            predicted = edit; scale = 1;
            if (!population) { note = "No owning population/seed; runtime size is unknown."; return false; }
            var random = new WildlifeRandom(population.worldSeed, agent.spawnId, agent.speciesId);
            scale = random.Range(Mathf.Max(.1f, agent.scaleRange.x), Mathf.Max(.1f, agent.scaleRange.y));
            if (!agent.visualRoot) { note = "No visualRoot: runtime does not scale renderer geometry; radius still uses deterministic scale."; return true; }
            Transform visual = agent.visualRoot;
            FieldInfo captured = typeof(WildlifeAgent).GetField("capturedScale", BindingFlags.Instance | BindingFlags.NonPublic);
            if (visual == agent.transform || !visual.IsChildOf(agent.transform) || captured == null || (bool)captured.GetValue(agent))
            { note = "visualRoot ownership or previously captured scale cannot be safely inferred; predicted body is an Edit-only placeholder."; return false; }
            foreach (Renderer renderer in agent.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                if (!renderer.transform.IsChildOf(visual) || (renderer is SkinnedMeshRenderer skin && skin.bones.Any(b => b && !b.IsChildOf(visual))))
                { note = "Mixed renderer/bone ownership outside visualRoot; predicted body is an Edit-only placeholder."; return false; }
            }
            Vector3 pivot = Quaternion.Inverse(agent.transform.rotation) * (visual.parent.position - agent.transform.position);
            predicted.center = pivot + (edit.center - pivot) * scale;
            predicted.half = edit.half * scale;
            note = "Predicted first deterministic scale around visualRoot parent pivot; bodyCenter/bodyHalf are runtime, measuredCenter/measuredHalf are Edit.";
            return true;
        }

        private static bool PredictStart(WildlifePopulation population, float radius, ref Vector3 point)
        {
            if (!population || point.x < population.worldBounds.min.x + radius || point.x > population.worldBounds.max.x - radius ||
                point.z < population.worldBounds.min.z + radius || point.z > population.worldBounds.max.z - radius) return false;
            if (population.terrain)
            {
                if (!TerrainContains(population.terrain, point)) return false;
                Vector3 local = point - population.terrain.transform.position, size = population.terrain.terrainData.size;
                if (population.terrain.terrainData.GetSteepness(local.x / size.x, local.z / size.z) > population.maxSlope) return false;
            }
            if (population.waterSurface) { if (population.waterSurface.Contains(point, radius)) return false; }
            else if (population.waterAreas != null)
                foreach (HabitatVolume water in population.waterAreas) if (water.Contains(point, -radius)) return false;
            point.y = population.GroundHeight(point);
            return true;
        }

        private static List<Contact> Contacts(World world, Body body, Vector3 position, bool candidate)
        {
            var result = new List<Contact>();
            foreach (Collider hit in Physics.OverlapBox(position + body.root.rotation * body.shape.center, body.shape.half,
                body.root.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit || hit.gameObject.scene != world.scene) continue;
                bool foot = FootSupport(hit, position, body.MaxSlope), blocked = true;
                string category = "static_obstacle";
                if (hit.transform.IsChildOf(body.root)) { category = "excluded_self"; blocked = false; }
                else if (hit is TerrainCollider) { category = "terrain_support"; blocked = false; }
                else if (world.Player(hit)) { category = "player"; blocked = candidate; }
                else if (world.decks.Contains(hit) && foot)
                {
                    bool ownsSupport = body.navigation && body.navigation.bridges != null &&
                        body.navigation.bridges.Any(b => b != null && b.id > 0 && b.surface == hit) &&
                        Surface(world, body, position, out JourneySurface support) && support.bridgeId > 0 && support.supportingCollider == hit;
                    category = ownsSupport ? "registered_bridge_support" : "registered_deck_without_runtime_exemption";
                    blocked = !ownsSupport;
                }
                else if (hit.GetComponentInParent<WildlifeAgent>() || world.bodies.Any(b => b != body && hit.transform.IsChildOf(b.root))) category = "animal_contact";
                else if (foot) category = "unregistered_ground_contact";
                result.Add(new Contact { path = PathOf(hit.transform), colliderType = hit.GetType().Name, category = category,
                    blocksBody = blocked, underFeet = foot, layer = hit.gameObject.layer, inRuntimeMask = (body.mask & (1 << hit.gameObject.layer)) != 0,
                    boundsCenter = hit.bounds.center, boundsSize = hit.bounds.size });
            }
            return result;
        }

        private static bool FootSupport(Collider collider, Vector3 feet, float maxSlope)
        {
            return collider.Raycast(new Ray(feet + Vector3.up * .15f, Vector3.down), out RaycastHit hit, .3f) &&
                Mathf.Abs(hit.point.y - feet.y) <= .12f && Vector3.Angle(hit.normal, Vector3.up) <= maxSlope;
        }

        private static bool Surface(World world, Body body, Vector3 point, out JourneySurface surface)
        {
            if (body.navigation && body.navigation.surfaceSampler == null)
            {
                surface = body.navigation.SampleSurface(point);
                return surface.valid && !surface.blocked;
            }
            surface = default;
            Terrain terrain = body.population ? body.population.terrain : null;
            if (!terrain) terrain = world.terrain.FirstOrDefault(t => TerrainContains(t, point));
            if (!TerrainContains(terrain, point)) return false;
            Vector3 local = point - terrain.transform.position, size = terrain.terrainData.size;
            surface.valid = true; surface.groundY = terrain.SampleHeight(point) + terrain.transform.position.y;
            surface.slope = terrain.terrainData.GetSteepness(local.x / size.x, local.z / size.z);
            surface.supportingCollider = terrain.GetComponent<TerrainCollider>();
            return true;
        }

        private static bool TerrainContains(Terrain terrain, Vector3 point)
        {
            if (!terrain || !terrain.terrainData) return false;
            Vector3 local = point - terrain.transform.position, size = terrain.terrainData.size;
            return local.x >= 0 && local.z >= 0 && local.x <= size.x && local.z <= size.z;
        }

        private static List<Candidate> Candidates(World world, Body body)
        {
            var found = new List<Candidate>();
            for (int ring = 1; ring <= 12; ring++)
            {
                float radius = ring * .25f;
                if (found.Count == 3 && radius > found[2].distance) break;
                int directions = Mathf.Min(64, Mathf.Max(8, Mathf.CeilToInt(2 * Mathf.PI * radius / .25f)));
                for (int i = 0; i < directions; i++)
                {
                    float angle = i * Mathf.PI * 2 / directions;
                    Vector3 point = body.root.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                    if (!Surface(world, body, point, out JourneySurface center)) continue;
                    point.y = center.groundY;
                    Vector3 projectedStart = point;
                    if (body.agent && (!PredictStart(body.population, body.result.predictedRadius, ref projectedStart) ||
                        Mathf.Abs(projectedStart.y - point.y) > .01f)) continue;
                    if (Mathf.Abs(point.y - body.root.position.y) > body.MaxRise || Contacts(world, body, point, true).Any(c => c.blocksBody)) continue;
                    if (world.bodies.Any(other => other != body && other.root.gameObject.activeInHierarchy &&
                        point.y + body.shape.Top > other.start.y + other.shape.Bottom &&
                        point.y + body.shape.Bottom < other.start.y + other.shape.Top &&
                        Flat(point, other.start) < body.shape.HorizontalRadius + other.shape.HorizontalRadius + .12f)) continue;
                    if (!Footprint(world, body, point, out float slope, out float range)) continue;
                    Vector3 displacement = point - body.root.position;
                    found.Add(new Candidate { position = point, displacement = displacement, yaw = body.root.eulerAngles.y,
                        distance = displacement.magnitude, horizontalDistance = radius, heightDifference = displacement.y,
                        maximumSlope = slope, footprintHeightRange = range, support = SupportName(center),
                        triggeringColliders = body.result.contacts.Where(c => c.blocksBody && c.category != "animal_contact").Select(c => c.path).Distinct().ToArray() });
                    found.Sort((a, b) => a.distance.CompareTo(b.distance));
                    if (found.Count > 3) found.RemoveAt(3);
                }
            }
            return found;
        }

        private static bool Footprint(World world, Body body, Vector3 root, out float maxSlope, out float heightRange)
        {
            maxSlope = heightRange = 0;
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            int nx = Mathf.Max(1, Mathf.CeilToInt(body.shape.half.x * 2 / .25f)), nz = Mathf.Max(1, Mathf.CeilToInt(body.shape.half.z * 2 / .25f));
            if (nx > 64 || nz > 64) return false;
            for (int x = 0; x <= nx; x++) for (int z = 0; z <= nz; z++)
            {
                Vector3 local = body.shape.center + new Vector3(Mathf.Lerp(-body.shape.half.x, body.shape.half.x, (float)x / nx),
                    -body.shape.half.y, Mathf.Lerp(-body.shape.half.z, body.shape.half.z, (float)z / nz));
                Vector3 point = root + body.root.rotation * local;
                if (!Surface(world, body, point, out JourneySurface ground) || ground.slope > body.MaxSlope ||
                    Mathf.Abs(ground.groundY - root.y) > body.MaxRise) return false;
                if (body.population && (point.x < body.population.worldBounds.min.x || point.x > body.population.worldBounds.max.x ||
                    point.z < body.population.worldBounds.min.z || point.z > body.population.worldBounds.max.z)) return false;
                // Registered dry bridge decks are valid support above water; actual wet footprint samples are rejected.
                if (ground.hasWater && ground.waterY > ground.groundY + .01f) return false;
                if (body.population && body.population.waterSurface && body.population.waterSurface.Contains(point) &&
                    body.population.waterSurface.Height(point) > ground.groundY + .01f) return false;
                if (world.water.Any(w => w && w.isActiveAndEnabled && w.Contains(point) && w.Height(point) > ground.groundY + .01f)) return false;
                if (body.navigation && body.navigation.waterAreas != null && body.navigation.waterAreas.Any(w => w != null &&
                    point.x >= w.bounds.min.x && point.x <= w.bounds.max.x && point.z >= w.bounds.min.z && point.z <= w.bounds.max.z &&
                    w.surfaceHeight > ground.groundY + .01f)) return false;
                if (body.population && body.population.waterAreas != null && body.population.waterAreas.Any(w =>
                    w.Contains(point) && w.center.y > ground.groundY + .01f)) return false;
                maxSlope = Mathf.Max(maxSlope, ground.slope); low = Mathf.Min(low, ground.groundY); high = Mathf.Max(high, ground.groundY);
            }
            heightRange = high - low;
            return heightRange <= body.MaxRise;
        }

        private static string SupportName(JourneySurface ground) => (ground.bridgeId > 0 ? "bridge:" + ground.bridgeId + ":" : "terrain:") +
            (ground.supportingCollider ? PathOf(ground.supportingCollider.transform) : "sampled height");
        private static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        private static string PathOf(Transform transform)
        {
            var parts = new List<string>();
            for (Transform node = transform; node; node = node.parent) parts.Add(node.name + "[" + node.GetSiblingIndex() + "]");
            parts.Reverse(); return string.Join("/", parts);
        }
        private static string Cell(object value) => "\"" + Convert.ToString(value, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\"";
        // JsonUtility omits custom nested arrays for DLLs loaded after Unity's type-cache scan.
        // This report consists solely of public fields, scalar values and arrays, including Vector3 x/y/z.
        private static string JsonFields(object value)
        {
            if (value == null) return "null";
            if (value is string text)
            {
                var escaped = new StringBuilder("\"");
                foreach (char c in text)
                    if (c == '\\' || c == '"') escaped.Append('\\').Append(c);
                    else if (c < 32) escaped.Append("\\u").Append(((int)c).ToString("x4"));
                    else escaped.Append(c);
                return escaped.Append('"').ToString();
            }
            if (value is bool flag) return flag ? "true" : "false";
            if (value.GetType().IsPrimitive) return Convert.ToString(value, CultureInfo.InvariantCulture);
            if (value is IEnumerable sequence)
                return "[" + string.Join(",", sequence.Cast<object>().Select(JsonFields)) + "]";
            return "{" + string.Join(",", value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Select(field => JsonFields(field.Name) + ":" + JsonFields(field.GetValue(value)))) + "}";
        }
        private static void Row(StringBuilder csv, params object[] values) => csv.AppendLine(string.Join(",", values.Select(Cell)));
        private static void WriteCsv(Report report)
        {
            var animals = new StringBuilder(); var contacts = new StringBuilder(); var candidates = new StringBuilder();
            Row(animals, "kind", "species", "id", "path", "active", "editX", "editY", "editZ", "predictedX", "predictedY", "predictedZ", "yaw",
                "queriedStaticOverlaps", "runtimeMaskBlockers", "predictionReliable", "predictedScale", "predictedRadius", "initialGroundProjection", "support", "candidateCount", "note");
            Row(contacts, "phase", "animal", "id", "collider", "category", "blocksBody", "underFeet", "inRuntimeMask", "type", "layer");
            Row(candidates, "animal", "id", "candidate", "x", "y", "z", "yaw", "translationOnly", "distance", "horizontalDistance", "heightDifference", "maximumSlope", "footprintHeightRange", "support", "triggeringColliders");
            foreach (Animal animal in report.animals)
            {
                Row(animals, animal.kind, animal.species, animal.id, animal.path, animal.active, animal.position.x, animal.position.y, animal.position.z,
                    animal.predictedStartPosition.x, animal.predictedStartPosition.y, animal.predictedStartPosition.z, animal.euler.y,
                    animal.queriedStaticOverlapCount, animal.staticBlockerCount, animal.runtimePredictionReliable, animal.predictedScale,
                    animal.predictedRadius, animal.initialGroundProjection, animal.support, animal.candidates.Length, animal.note);
                foreach (Contact contact in animal.editContacts) Row(contacts, "currentEdit", animal.path, animal.id, contact.path, contact.category,
                    contact.blocksBody, contact.underFeet, contact.inRuntimeMask, contact.colliderType, contact.layer);
                foreach (Contact contact in animal.contacts) Row(contacts, "predictedRuntime", animal.path, animal.id, contact.path, contact.category,
                    contact.blocksBody, contact.underFeet, contact.inRuntimeMask, contact.colliderType, contact.layer);
                for (int i = 0; i < animal.candidates.Length; i++)
                {
                    Candidate c = animal.candidates[i];
                    Row(candidates, animal.path, animal.id, i + 1, c.position.x, c.position.y, c.position.z, c.yaw, c.translationOnly,
                        c.distance, c.horizontalDistance, c.heightDifference, c.maximumSlope, c.footprintHeightRange, c.support, string.Join(";", c.triggeringColliders));
                }
            }
            File.WriteAllText(Path.Combine(report.outputDirectory, "animals.csv"), animals.ToString(), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(report.outputDirectory, "contacts.csv"), contacts.ToString(), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(report.outputDirectory, "candidates.csv"), candidates.ToString(), new UTF8Encoding(true));
        }
    }
}
