using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using WeatherHouse.Hillside.Editor;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    // Exercises candidate generation only: no camera draws, saved scenes, or generated assets.
    public static class WatersideGrassMaskChecks
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Weather House/Checks/Waterside Grass Placement Mask")]
        public static void RunMenu() { Debug.Log(Run()); }

        public static string Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run placement checks in Edit mode.");
            MethodInfo build = typeof(HillsideGrassRenderer).GetMethod("BuildCell", PrivateInstance);
            // The existing independent oracle predates placementMap and still evaluates all original routes/waters.
            MethodInfo legacy = typeof(HillsideGrassPerformanceChecks).GetMethod("Legacy", BindingFlags.Static | BindingFlags.NonPublic);
            Require(build != null && legacy != null, "Grass verification entry points changed.");
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject holder = null;
            TerrainData data = null;
            var textures = new List<Texture2D>();
            try
            {
                holder = new GameObject("Temporary grass mask regression") { hideFlags = HideFlags.HideAndDontSave };
                holder.SetActive(false);
                SceneManager.MoveGameObjectToScene(holder, preview);
                holder.transform.position = new Vector3(-120, -6, -120);
                data = new TerrainData { heightmapResolution = 33, size = new Vector3(240, 64, 240) };
                data.SetHeights(0, 0, new float[33, 33]);
                Terrain terrain = holder.AddComponent<Terrain>(); terrain.terrainData = data;
                var grass = holder.AddComponent<HillsideGrassRenderer>();
                grass.manualCaptureOnly = true; grass.terrain = terrain;
                grass.cellSize = 16; grass.clumpsPerSquareMeter = 4;
                grass.speciesWeights = new[] { 1f, 1f, 1f }; grass.seed = 70519;
                var report = new StringBuilder("Waterside optional grass placement mask\n");

                var keys = new HashSet<Vector2Int>();
                foreach (Vector3 point in new[] { HillsideLayout.A, HillsideLayout.B, HillsideLayout.E, HillsideLayout.PlayerStart }) keys.Add(Key(point));
                foreach (var water in HillsideLayout.Waters)
                {
                    keys.Add(Key(new Vector3(water.Center.x, 0, water.Center.y)));
                    keys.Add(Key(new Vector3(water.Center.x + water.Radii.x, 0, water.Center.y)));
                }
                int comparisons = 0, matrices = 0;
                foreach (Vector2Int key in keys)
                    foreach (float altitude in new[] { 0f, 245f })
                    {
                        Vector3 view = View(key) + Vector3.up * altitude;
                        var expected = (List<Matrix4x4>[])legacy.Invoke(null, new object[] { grass, key, grass.cellSize, view });
                        matrices += Equal(expected, Snapshot(build, grass, key, view), "null mask " + key + " altitude " + altitude);
                        comparisons++;
                    }
                Require(matrices > 100, "Legacy fixture did not exercise visible vegetation.");
                report.AppendLine("PASS null mask: " + comparisons + " near/far land/path/water/shore comparisons; " + matrices + " matrices match the pre-mask oracle.");

                Vector2Int testKey = new Vector2Int(0, 0);
                Vector3 testView = View(testKey);
                var before = Snapshot(build, grass, testKey, testView);
                grass.placementMap = Texture((x, z) => new Color(1, 0, 0, 1), textures);
                var dry = Snapshot(build, grass, testKey, testView);
                Require(Count(dry, 0, 4) > 0 && Count(dry, 4, 8) > 0 && Count(dry, 8, 12) > 0,
                    "A dry explicit mask must retain short grass, tall grass and flowers.");
                Equal(dry, Snapshot(build, grass, testKey, testView), "same mask/seed repeat");
                grass.placementMap = Texture((x, z) => new Color(0, 0, 0, 1), textures);
                Require(Count(Snapshot(build, grass, testKey, testView), 0, 12) == 0, "An explicit empty mask did not clear the cached cell.");
                grass.placementMap = null;
                Equal(before, Snapshot(build, grass, testKey, testView), "restore null after mask swap");
                report.AppendLine("PASS mask replacement invalidates cached cells; clearing it restores the exact legacy placement; repeated seed is stable.");
                CheckTrampledGrass(build, grass, testKey, testView, dry, textures, report);

                grass.placementMap = Texture((x, z) => new Color(Mathf.Abs(x - 8) < 3 ? 0 : 1, 0, 0, 1), textures);
                var road = Snapshot(build, grass, testKey, testView);
                int left = 0, right = 0;
                foreach (var group in road)
                    foreach (Matrix4x4 matrix in group)
                    {
                        Vector3 p = matrix.GetColumn(3);
                        Require(p.x <= 6 || p.x >= 10, "Grass entered the explicit cleared road interior: " + p);
                        if (p.x < 4) left++;
                        if (p.x > 12) right++;
                    }
                Require(left > 50 && right > 50, "Road check needs surviving vegetation on both banks.");
                report.AppendLine("PASS explicit cleared road stays empty while " + left + "/" + right + " clumps remain on its sides.");

                foreach (float depth in new[] { .1f, .8f })
                {
                    grass.placementMap = Texture((x, z) => new Color(1, depth / 8, 0, 1), textures);
                    var bed = Snapshot(build, grass, testKey, testView);
                    Require(Count(bed, 0, 4) > 20, "Water-depth check produced no visible short grass.");
                    Require(Count(bed, 8, 12) == 0, "Flowers were planted underwater at depth " + depth);
                    if (depth > .45f) Require(Count(bed, 4, 8) == 0, "Deep bed retained tall grass.");
                    else Require(Count(bed, 4, 8) > 0, "Shallows unnecessarily removed all tall grass.");
                    foreach (var group in bed)
                        foreach (Matrix4x4 matrix in group)
                        {
                            Vector3 p = matrix.GetColumn(3);
                            float ground = terrain.SampleHeight(p) + terrain.transform.position.y;
                            Require(Mathf.Abs(p.y + .025f - ground) < .00001f, "Bed plants were moved to the water surface.");
                        }
                }
                report.AppendLine("PASS shallow/deep mask water contains no flowers; deep water contains only short grass; all roots remain on the terrain bed.");

                // A dry V2 mask must also override old-map water and route exclusions at the same coordinates.
                grass.placementMap = Texture((x, z) => new Color(1, 0, 0, 1), textures);
                int reclaimed = 0;
                foreach (var water in HillsideLayout.Waters)
                {
                    var key = Key(new Vector3(water.Center.x, 0, water.Center.y));
                    var groups = Snapshot(build, grass, key, View(key));
                    for (int variant = 8; variant < 12; variant++)
                        foreach (Matrix4x4 matrix in groups[variant])
                        { Vector3 p = matrix.GetColumn(3); if (water.SignedDistance(p.x, p.z) < 0) reclaimed++; }
                }
                Require(reclaimed > 10, "Explicit V2 dry mask still inherits old-map water exclusions.");
                report.AppendLine("PASS explicit dry mask overrides old-map water at " + reclaimed + " flower placements.");
                CheckStructureFaces(report);
                CheckWaterCompatibility(holder, terrain, report);
                return report.ToString();
            }
            finally
            {
                if (holder) Object.DestroyImmediate(holder);
                if (data) Object.DestroyImmediate(data);
                foreach (Texture2D texture in textures) if (texture) Object.DestroyImmediate(texture);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        static void CheckTrampledGrass(MethodInfo build, HillsideGrassRenderer grass, Vector2Int key, Vector3 view,
            List<Matrix4x4>[] fullDry, List<Texture2D> textures, StringBuilder report)
        {
            int compared = 0;
            foreach (float coverage in new[] { 1f, .3f })
            {
                grass.placementMap = Texture((x, z) => new Color(coverage, 0, 0, 1), textures);
                var original = Snapshot(build, grass, key, view);
                if (coverage == 1) Equal(fullDry, original, "B=0 preserves ordinary dry mask");
                Require(Count(original, 4, 12) > 20, "Trampling fixture needs tall grass and flowers before applying B.");
                var byPosition = new Dictionary<Vector3, Matrix4x4>();
                for (int variant = 0; variant < original.Length; variant++)
                    foreach (Matrix4x4 matrix in original[variant]) byPosition.Add(matrix.GetColumn(3), matrix);
                foreach (float strength in new[] { .0001f, .5f, 1f })
                {
                    grass.placementMap = Texture((x, z) => new Color(coverage, 0, strength, 1), textures);
                    var trampled = Snapshot(build, grass, key, view);
                    Require(Count(trampled, 4, 12) == 0, "B>0 must select only short grass, including at the feathered edge.");
                    Require(Count(trampled, 0, 4) == byPosition.Count, "B changed coverage or removed path grass instead of shortening it.");
                    float ratio = Mathf.Lerp(1, .42f, strength);
                    foreach (var group in trampled)
                        foreach (Matrix4x4 actual in group)
                        {
                            Vector3 position = actual.GetColumn(3);
                            Require(byPosition.TryGetValue(position, out Matrix4x4 baseline), "B changed seeded grass positions.");
                            Matrix4x4 expected = baseline * Matrix4x4.Scale(Vector3.one * ratio);
                            for (int component = 0; component < 16; component++)
                                Require(Mathf.Abs(expected[component] - actual[component]) <= .00001f,
                                    "B=" + strength + " changed yaw/root position or failed scale ratio " + ratio + " at " + position);
                            compared++;
                        }
                }
                grass.placementMap = Texture((x, z) => new Color(coverage, 0, 0, 1), textures);
                Equal(original, Snapshot(build, grass, key, view), "B=0 restores all species and original scale");
            }
            report.AppendLine("PASS trampled B channel: " + compared + " matrices at R=1/.3 and B=.0001/.5/1 retain coverage, positions and yaw; only short grass uses the exact 1-to-.42 scale; B=0 restores every original species/matrix.");
        }

        static void CheckStructureFaces(StringBuilder report)
        {
            MethodInfo build = typeof(WatersideStructureBuilder).GetMethod("BevelBlock", BindingFlags.Static | BindingFlags.NonPublic);
            Require(build != null, "Structure geometry check entry point changed.");
            Mesh mesh = (Mesh)build.Invoke(null, null);
            try
            {
                Vector3[] vertices = mesh.vertices; int[] triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                    Require(Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3) > .000001f,
                        "Bridge/stair block has inward or degenerate triangle " + i / 3);
                }
                report.AppendLine("PASS all " + triangles.Length / 3 + " bridge/stair block triangles face outward, including top caps.");
            }
            finally { if (mesh) Object.DestroyImmediate(mesh); }
        }

        static void CheckWaterCompatibility(GameObject holder, Terrain terrain, StringBuilder report)
        {
            var surface = holder.AddComponent<WatersideWaterSurface>(); surface.section = -2;
            var population = holder.AddComponent<WildlifePopulation>(); population.terrain = terrain;
            population.worldBounds = new Bounds(Vector3.zero, new Vector3(240, 100, 240));
            population.waterAreas = new[] { new HabitatVolume { center = new Vector3(0, 2, 0), radii = new Vector2(6, 4) } };
            var agent = holder.AddComponent<WildlifeAgent>(); agent.bodyRadius = .12f; agent.waterAreaIndex = 0;
            Vector3 legacyWet = Vector3.zero, legacyDry = new Vector3(20, 0, 20);
            agent.locomotion = WildlifeLocomotion.Ground;
            Require(!population.TryProjectPosition(agent, legacyWet, out _), "Null water provider lost the original ellipse exclusion.");
            Require(population.TryProjectPosition(agent, legacyDry, out Vector3 dry) && Mathf.Abs(dry.y + 6) < .001f,
                "Null water provider changed legacy dry grounding.");
            agent.locomotion = WildlifeLocomotion.Swimming;
            Require(population.TryProjectPosition(agent, legacyWet, out Vector3 fish) && Mathf.Abs(fish.y - 1.6f) < .001f,
                "Null water provider changed the original fish waterline.");
            agent.locomotion = WildlifeLocomotion.Flying;
            Require(population.TryProjectPosition(agent, legacyWet, out Vector3 bird) && Mathf.Abs(bird.y - (2 + agent.flightHeight)) < .001f,
                "Null water provider changed the original bird water clearance.");

            population.waterSurface = surface;
            population.waterAreas[0].radii = Vector2.one * 150;
            var probes = new List<Vector3> { WatersideLayout.Design(95, 75) };
            foreach (var channel in WatersideLayout.WaterChannels)
                for (int i = 1; i < channel.Nodes.Length; i++)
                    for (int j = 1; j < 4; j++) probes.Add(Vector3.Lerp(channel.Nodes[i - 1].Position, channel.Nodes[i].Position, j * .25f));
            foreach (Vector3 p in probes)
            {
                Require(surface.Contains(p), "Connected river/sea probe was outside the V2 provider.");
                agent.locomotion = WildlifeLocomotion.Ground;
                Require(!population.TryProjectPosition(agent, p, out _), "Land animal entered the connected river or sea at " + p);
                agent.locomotion = WildlifeLocomotion.Swimming;
                Require(population.TryProjectPosition(agent, p, out fish) && Mathf.Abs(fish.y - (surface.Height(p) - agent.swimDepth)) < .001f,
                    "Fish did not follow local river water height at " + p);
                agent.locomotion = WildlifeLocomotion.Flying;
                Require(population.TryProjectPosition(agent, p, out bird) && Mathf.Abs(bird.y - (surface.Height(p) + agent.flightHeight)) < .001f,
                    "Bird did not clear local river water height at " + p);
            }
            agent.locomotion = WildlifeLocomotion.Swimming;
            Require(!population.TryProjectPosition(agent, WatersideLayout.Entry, out _), "Fish can move onto V2 dry land.");
            agent.locomotion = WildlifeLocomotion.Ground;
            Require(population.TryProjectPosition(agent, WatersideLayout.Entry, out _), "V2 dry entry is blocked by obsolete ellipse data.");
            report.AppendLine("PASS null water provider preserves ground/fish/bird projections; V2 provider blocks land animals across " + probes.Count + " river/sea probes and uses each local waterline.");

            var waterObject = new GameObject("Temporary water footprint");
            waterObject.transform.SetParent(holder.transform, false); waterObject.transform.position = Vector3.zero;
            var plane = new Mesh { vertices = new[] { new Vector3(-120, 2, -120), new Vector3(-120, 2, 120), new Vector3(120, 2, 120), new Vector3(120, 2, -120) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 } };
            try
            {
                plane.RecalculateBounds();
                waterObject.AddComponent<MeshFilter>().sharedMesh = plane;
                var renderer = waterObject.AddComponent<MeshRenderer>();
                renderer.bounds = new Bounds(new Vector3(0, 2, 0), new Vector3(240, .01f, 240));
                var interaction = waterObject.AddComponent<HillsideWaterInteraction>(); interaction.terrain = terrain; interaction.waterRenderer = renderer;
                Require(interaction.IsWet(WatersideLayout.Entry), "Null interaction provider changed its legacy bounds-based footprint.");
                interaction.surface = surface;
                Require(!interaction.IsWet(WatersideLayout.Entry), "V2 water interaction stamped on dry entry land.");
                foreach (Vector3 p in probes) Require(interaction.IsWet(p), "V2 water interaction missed river/sea footprint at " + p);
                interaction.surface = null;
                Require(interaction.IsWet(WatersideLayout.Entry), "Removing the interaction provider did not restore legacy behavior.");
                report.AppendLine("PASS water interaction uses V2 wet footprints and restores the original bounds-based behavior with a null provider.");
                var ensure = typeof(HillsideWaterInteraction).GetMethod("EnsureResources", BindingFlags.Instance | BindingFlags.NonPublic);
                renderer.bounds = new Bounds(new Vector3(0, 2, 0), new Vector3(1200, .01f, 1200));
                Require((bool)ensure.Invoke(interaction, null), "Legacy wake resources failed to initialize.");
                Require(Mathf.Abs(1 / interaction.WorldRect.z - (1200 + interaction.fieldPadding * 2)) < .01f,
                    "Null provider no longer uses the original renderer bounds for its field.");
                interaction.surface = surface;
                Require((bool)ensure.Invoke(interaction, null), "World-surface wake resources failed to initialize.");
                Require(Mathf.Abs(1 / interaction.WorldRect.z - (terrain.terrainData.size.x + interaction.fieldPadding * 2)) < .01f &&
                    Mathf.Abs(1 / interaction.WorldRect.w - (terrain.terrainData.size.z + interaction.fieldPadding * 2)) < .01f,
                    "Offshore decoration diluted the terrain wake field.");
                report.AppendLine("PASS a 1200 m offshore surface keeps wakes restricted to the terrain footprint; null-provider field size stays unchanged.");
            }
            finally { Object.DestroyImmediate(waterObject); Object.DestroyImmediate(plane); }
        }

        // Call after the generated V2 scene has been saved and reloaded. This reads the real persisted meshes.
        public static string VerifySceneWater()
        {
            int checkedSections = 0, checkedVertices = 0;
            var sectionIds = new HashSet<int>();
            var junctions = new Dictionary<Vector2Int, float>();
            var origins = new List<Vector3> { Vector3.zero };
            var normals = new List<Vector3> { Vector3.up };
            foreach (var channel in WatersideLayout.WaterChannels)
                for (int i = 1; i < channel.Nodes.Length; i++)
                {
                    Vector3 a = channel.Nodes[i - 1].Position, b = channel.Nodes[i].Position, delta = b - a;
                    float square = delta.x * delta.x + delta.z * delta.z;
                    origins.Add((a + b) * .5f);
                    normals.Add(new Vector3(-delta.x * delta.y / square, 1, -delta.z * delta.y / square).normalized);
                }
            foreach (var surface in Object.FindObjectsOfType<WatersideWaterSurface>())
            {
                if (surface.section == -2) continue;
                MeshFilter filter = surface.GetComponent<MeshFilter>();
                Require(filter && filter.sharedMesh, "Water section lacks a persisted surface mesh: " + surface.name);
                int part = surface.section + 1;
                Require(part >= 0 && part < origins.Count && sectionIds.Add(surface.section), "Missing/duplicate water section id.");
                Require(Vector3.Distance(surface.transform.position, origins[part]) < .002f && Vector3.Dot(surface.transform.up, normals[part]) > .9999f,
                    "Reflection plane does not match river section " + surface.section);
                var interaction = surface.GetComponent<HillsideWaterInteraction>();
                Require(interaction && interaction.surface == surface, "Water wake does not use its section provider.");
                Vector3[] vertices = filter.sharedMesh.vertices; int[] triangles = filter.sharedMesh.triangles;
                var world = new Vector3[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 p = world[i] = filter.transform.TransformPoint(vertices[i]);
                    Require(!float.IsNaN(p.x + p.y + p.z) && !float.IsInfinity(p.x + p.y + p.z), "Water mesh has non-finite vertices.");
                    Require(Mathf.Abs(p.y - WatersideLayout.WaterHeight(p.x, p.z)) < .002f, "Saved water geometry drifted from local water height: " + p);
                    var key = new Vector2Int(Mathf.RoundToInt(p.x * 100), Mathf.RoundToInt(p.z * 100));
                    if (junctions.TryGetValue(key, out float existing)) Require(Mathf.Abs(existing - p.y) < .002f, "Water sections have a height crack at " + p);
                    else junctions.Add(key, p.y);
                    checkedVertices++;
                }
                for (int i = 0; i < triangles.Length; i += 3)
                    Require(Vector3.Cross(world[triangles[i + 1]] - world[triangles[i]], world[triangles[i + 2]] - world[triangles[i]]).y > .0001f,
                        "Water triangle faces down or has zero area: " + surface.name + "/" + i / 3);
                checkedSections++;
            }
            Require(checkedSections == origins.Count, "Generated water is missing river/sea sections: " + checkedSections + "/" + origins.Count);
            return "PASS " + checkedSections + " saved river/sea sections; " + checkedVertices + " vertices follow local water heights, meet at common edges, face up, and have matching reflection/wake providers.";
        }

        static Texture2D Texture(Func<float, float, Color> sample, List<Texture2D> owned)
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true) { wrapMode = TextureWrapMode.Clamp };
            owned.Add(texture);
            var pixels = new Color[size * size];
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++) pixels[z * size + x] = sample(-120 + 240f * x / (size - 1), -120 + 240f * z / (size - 1));
            texture.SetPixels(pixels); texture.Apply(false, false); return texture;
        }

        static List<Matrix4x4>[] Snapshot(MethodInfo build, HillsideGrassRenderer grass, Vector2Int key, Vector3 view)
        {
            object cell = build.Invoke(grass, new object[] { key, grass.cellSize, view });
            var groups = Enumerable.Range(0, 12).Select(_ => new List<Matrix4x4>()).ToArray();
            foreach (object batch in (IEnumerable)cell.GetType().GetField("batches").GetValue(cell))
            {
                Type type = batch.GetType();
                int variant = (int)type.GetField("variant").GetValue(batch), count = (int)type.GetField("count").GetValue(batch);
                var matrices = (Matrix4x4[])type.GetField("matrices").GetValue(batch);
                for (int i = 0; i < count; i++) groups[variant].Add(matrices[i]);
            }
            return groups;
        }

        static int Equal(List<Matrix4x4>[] expected, List<Matrix4x4>[] actual, string context)
        {
            for (int variant = 0; variant < 12; variant++)
            {
                Require(expected[variant].Count == actual[variant].Count, context + ": variant " + variant + " count changed.");
                for (int i = 0; i < expected[variant].Count; i++)
                    for (int component = 0; component < 16; component++)
                        Require(Mathf.Abs(expected[variant][i][component] - actual[variant][i][component]) <= .00001f,
                            context + ": matrix " + variant + "/" + i + "/" + component + " changed.");
            }
            return Count(actual, 0, 12);
        }

        static int Count(List<Matrix4x4>[] groups, int first, int last)
        { int count = 0; for (int i = first; i < last; i++) count += groups[i].Count; return count; }
        static Vector2Int Key(Vector3 point) => new Vector2Int(Mathf.FloorToInt(point.x / 16), Mathf.FloorToInt(point.z / 16));
        static Vector3 View(Vector2Int key) => new Vector3((key.x + .5f) * 16, 8, (key.y + .5f) * 16);
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
