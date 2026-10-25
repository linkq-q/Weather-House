using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    /// <summary>Local repair of the old mouth after the rural coastline moved south.</summary>
    public static class WatersideEstuaryRepair
    {
        static readonly Rect Area = Rect.MinMaxRect(4, -104, 46, -64);
        static readonly Vector3[] Centre = { new Vector3(16.2f, 0, -66.6f),
            new Vector3(23.4f, 0, -79.2f), new Vector3(25.2f, 0, -86.4f), new Vector3(27, 0, -99) };
        static float Ease(float a, float b, float v) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, v));
        static bool Inside(Vector3 p) => Area.Contains(new Vector2(p.x, p.z));
        static float MouthDistance(Vector3 p)
        {
            float result = float.MaxValue;
            float[] widths = { 5.5f, 6, 9.5f, 14 };
            for (int i = 1; i < Centre.Length; i++)
            {
                Vector3 d = Centre[i] - Centre[i - 1], q = p - Centre[i - 1]; q.y = 0;
                float t = Mathf.Clamp01(Vector3.Dot(q, d) / d.sqrMagnitude);
                result = Mathf.Min(result, (q - d * t).magnitude - Mathf.Lerp(widths[i - 1], widths[i], t));
            }
            return result;
        }
        static Transform[] Objects() => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        static MeshFilter[] Water(Transform[] all) => all.Select(t => t.GetComponent<MeshFilter>())
            .Where(m => m && m.sharedMesh && m.GetComponent<WatersideWaterSurface>() &&
                m.GetComponent<WatersideWaterSurface>().section >= -1).ToArray();

        [MenuItem("Weather House/Waterside/Repair rural river mouth")]
        public static void Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || EditorApplication.isCompiling ||
                scene.path != "Assets/Scenes/WeatherHouse_WatersideV2.unity")
                throw new InvalidOperationException("Open the main Waterside scene in idle Edit mode.");
            if (scene.isDirty) throw new InvalidOperationException("Save current scene edits before the local repair.");
            var all = Objects(); var terrain = all.Select(t => t.GetComponent<Terrain>()).Single(t => t);
            var data = terrain.terrainData; var origin = terrain.transform.position; var size = data.size;
            var grass = all.Select(t => t.GetComponent<HillsideGrassRenderer>()).FirstOrDefault(g => g && g.terrain == terrain);
            var waters = Water(all);
            if (data.alphamapLayers < 4 || waters.Length != 16 || !grass || !grass.placementMap || !grass.placementMap.isReadable)
                throw new InvalidOperationException("Unexpected terrain, grass map or water sections; no repair applied.");
            var affected = waters.Where(m => m.GetComponent<Renderer>().bounds.Intersects(
                new Bounds(new Vector3(Area.center.x, 0, Area.center.y), new Vector3(Area.width, 4, Area.height)))).ToArray();
            var assets = new List<Object> { data, grass.placementMap }; assets.AddRange(affected.Select(m => (Object)m.sharedMesh));
            string backup = "JourneyReview/EstuaryRepair20261025/Backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(backup);
            File.Copy(scene.path, backup + "/scene.unity");
            foreach (var asset in assets)
            {
                AssetDatabase.SaveAssetIfDirty(asset);
                string path = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Expected persisted asset: " + asset.name);
                File.Copy(path, backup + "/" + path.Replace('/', '_'));
            }
            var snapshots = assets.Select(Object.Instantiate).ToArray();
            var heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
            var alpha = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            var map = grass.placementMap; var pixels = map.GetPixels();
            var beforeHeights = (float[,])heights.Clone(); var beforeAlpha = (float[,,])alpha.Clone();
            var beforePixels = (Color[])pixels.Clone();
            var outsideMeshes = affected.Select(m => OutsideMeshSignature(m, Area)).ToArray();
            int heightChanges = 0, alphaChanges = 0, grassChanges = 0;
            try
            {
                for (int z = 0; z < heights.GetLength(0); z++) for (int x = 0; x < heights.GetLength(1); x++)
                {
                    var p = new Vector3(origin.x + x * size.x / (heights.GetLength(1) - 1), 0,
                        origin.z + z * size.z / (heights.GetLength(0) - 1));
                    if (!Inside(p)) continue;
                    float d = WatersideLayout.WaterSignedDistance(p.x, p.z), mouth = MouthDistance(p);
                    float blend = (1 - Ease(1.5f, 4, mouth)) * (1 - Ease(-70, -66, p.z));
                    float old = origin.y + heights[z, x] * size.y;
                    float bank = d < 0 ? -Mathf.Lerp(.08f, 2.4f, Ease(0, 4, -d)) : Mathf.Lerp(.08f, 1, Ease(0, 3, d));
                    // Preserve the user's deeper excavation; only lower the remaining sill and steep shoulders.
                    if (blend <= 0) continue;
                    float value = Mathf.Min(old, WatersideLayout.WaterHeight(p.x, p.z) + bank + 12 * (1 - blend));
                    if (Mathf.Abs(value - old) < .0001f) continue;
                    heights[z, x] = Mathf.Clamp01((value - origin.y) / size.y); heightChanges++;
                }
                data.SetHeights(0, 0, heights);
                for (int z = 0; z < data.alphamapHeight; z++) for (int x = 0; x < data.alphamapWidth; x++)
                {
                    var p = new Vector3(origin.x + x * size.x / (data.alphamapWidth - 1), 0,
                        origin.z + z * size.z / (data.alphamapHeight - 1));
                    if (!Inside(p) || p.z > -70) continue;
                    float sand = (1 - Ease(.2f, 2, MouthDistance(p))) * (1 - Ease(-74, -70, p.z));
                    if (sand <= alpha[z, x, 3] + .002f) continue;
                    float remaining = (1 - sand) / Mathf.Max(.0001f, 1 - alpha[z, x, 3]);
                    for (int l = 0; l < data.alphamapLayers; l++) alpha[z, x, l] = l == 3 ? sand : alpha[z, x, l] * remaining;
                    alphaChanges++;
                }
                data.SetAlphamaps(0, 0, alpha);
                for (int z = 0; z < map.height; z++) for (int x = 0; x < map.width; x++)
                {
                    var p = new Vector3(origin.x + x * size.x / (map.width - 1), 0, origin.z + z * size.z / (map.height - 1));
                    if (!Inside(p) || MouthDistance(p) > 3) continue;
                    float d = WatersideLayout.WaterSignedDistance(p.x, p.z);
                    Color c = pixels[z * map.width + x];
                    c.r = Mathf.Min(c.r, Ease(-.15f, 1.4f, d));
                    c.g = WatersideLayout.IsWater(p.x, p.z) ? Mathf.Clamp01((WatersideLayout.WaterHeight(p.x, p.z) - terrain.SampleHeight(p) - origin.y) / 8) : 0;
                    pixels[z * map.width + x] = c; grassChanges++;
                }
                map.SetPixels(pixels); map.Apply(false, false);
                foreach (var water in affected) PatchWater(water, Area, terrain);
                terrain.Flush(); grass.InvalidateLocalRegion(Area);
                string result = Check();
                // Compare every height/paint/grass sample outside the repair against the live pre-edit assets.
                VerifyOutside(data, beforeHeights, beforeAlpha, map, beforePixels, origin, size);
                for (int i = 0; i < affected.Length; i++)
                    if (outsideMeshes[i] != OutsideMeshSignature(affected[i], Area)) throw new Exception("Water mesh changed outside mouth: " + affected[i].name);
                File.WriteAllText(backup + "/result.txt", result + "\nheightChanges=" + heightChanges + " alphaChanges=" + alphaChanges + " grassChanges=" + grassChanges);
                foreach (var asset in assets) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
                Debug.Log(result); SceneView.RepaintAll();
            }
            catch
            {
                for (int i = 0; i < assets.Count; i++) EditorUtility.CopySerialized(snapshots[i], assets[i]);
                data.SetHeights(0, 0, beforeHeights); data.SetAlphamaps(0, 0, beforeAlpha);
                map.SetPixels(beforePixels); map.Apply(false, false);
                foreach (var asset in assets)
                {
                    string path = AssetDatabase.GetAssetPath(asset);
                    File.Copy(backup + "/" + path.Replace('/', '_'), path, true);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                }
                File.WriteAllText(backup + "/result.txt", "FAILED / ROLLED BACK"); terrain.Flush(); grass.InvalidateLocalRegion(Area); throw;
            }
            finally { foreach (var copy in snapshots) Object.DestroyImmediate(copy); }
        }

        static float SurfaceEdge(Vector2 p, Terrain terrain)
        {
            float design = WatersideLayout.WaterSignedDistance(p.x, p.y);
            var origin = terrain.transform.position; var size = terrain.terrainData.size;
            if (p.x < origin.x || p.x > origin.x + size.x || p.y < origin.z || p.y > origin.z + size.z)
                return design - 1.2f;
            float bank = terrain.SampleHeight(new Vector3(p.x, 0, p.y)) + origin.y - WatersideLayout.WaterHeight(p.x, p.y) - .15f;
            // The authoring footprint supplies the water level. Extend its hidden skirt to the
            // actual edited bank so exposing a previously submerged grid edge cannot cut off water.
            return Mathf.Max(design - 6, Mathf.Min(design - 1.2f, bank));
        }

        [MenuItem("Weather House/Waterside/Repair junction water edges")]
        public static void RepairJunctionShore()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.isDirty || scene.path != "Assets/Scenes/WeatherHouse_WatersideV2.unity")
                throw new InvalidOperationException("Save the main scene in Edit mode first.");
            var all = Objects(); var terrain = all.Select(t => t.GetComponent<Terrain>()).Single(t => t);
            var waters = Water(all);
            var regions = new[] { Area, Rect.MinMaxRect(-16, -58, 26, -26) };
            var affected = waters.Where(w => regions.Any(r => w.GetComponent<Renderer>().bounds.Intersects(
                new Bounds(new Vector3(r.center.x, 0, r.center.y), new Vector3(r.width, 8, r.height))))).ToArray();
            string backup = "JourneyReview/EstuaryRepair20261025/Shore-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(backup);
            foreach (var w in affected)
            {
                string path = AssetDatabase.GetAssetPath(w.sharedMesh); AssetDatabase.SaveAssetIfDirty(w.sharedMesh);
                File.Copy(path, backup + "/" + path.Replace('/', '_'));
            }
            try
            {
                foreach (var area in regions) foreach (var w in affected)
                {
                    if (!w.GetComponent<Renderer>().bounds.Intersects(new Bounds(new Vector3(area.center.x, 0, area.center.y), new Vector3(area.width, 8, area.height)))) continue;
                    string before = OutsideMeshSignature(w, area); PatchWater(w, area, terrain);
                    if (before != OutsideMeshSignature(w, area)) throw new Exception("Exterior water changed: " + w.name);
                }
                File.WriteAllText(backup + "/checks.txt", Check() + "\n" + CheckShoreCoverage(terrain, waters, regions));
                foreach (var w in affected) { EditorUtility.SetDirty(w.sharedMesh); AssetDatabase.SaveAssetIfDirty(w.sharedMesh); }
                SceneView.RepaintAll();
            }
            catch
            {
                foreach (var w in affected)
                {
                    string path = AssetDatabase.GetAssetPath(w.sharedMesh);
                    File.Copy(backup + "/" + path.Replace('/', '_'), path, true); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                }
                File.WriteAllText(backup + "/checks.txt", "FAILED / ROLLED BACK"); throw;
            }
        }

        static string CheckShoreCoverage(Terrain terrain, MeshFilter[] waters, Rect[] regions)
        {
            int samples = 0;
            foreach (var area in regions)
            {
                var triangles = new List<Vector3[]>();
                foreach (var w in waters)
                {
                    var v = w.sharedMesh.vertices; var t = w.sharedMesh.triangles;
                    for (int i = 0; i < t.Length; i += 3)
                    {
                        var a = w.transform.TransformPoint(v[t[i]]); var b = w.transform.TransformPoint(v[t[i + 1]]); var c = w.transform.TransformPoint(v[t[i + 2]]);
                        if (area.Contains(new Vector2((a.x + b.x + c.x) / 3, (a.z + b.z + c.z) / 3))) triangles.Add(new[] { a, b, c });
                    }
                }
                for (float z = area.yMin + .173f; z < area.yMax; z += .5f) for (float x = area.xMin + .217f; x < area.xMax; x += .5f)
                {
                    if (z < terrain.transform.position.z || WatersideLayout.WaterSignedDistance(x, z) > 5.5f) continue;
                    var p = new Vector3(x, 0, z); float level = WatersideLayout.WaterHeight(x, z);
                    if (terrain.SampleHeight(p) + terrain.transform.position.y > level - .04f) continue;
                    if (!triangles.Any(v => Covers(p, v[0], v[1], v[2]))) throw new Exception("Exposed water edge at " + p);
                    samples++;
                }
            }
            return "PASS " + samples + " edited-bank wet samples covered by actual water triangles; exterior mesh signatures preserved.";
        }

        static void PatchWater(MeshFilter filter, Rect area, Terrain terrain)
        {
            var mesh = filter.sharedMesh; var vertices = mesh.vertices.ToList(); var uv = mesh.uv.ToList();
            var triangles = new List<int>(); var old = mesh.triangles;
            for (int i = 0; i < old.Length; i += 3)
            {
                var a = filter.transform.TransformPoint(vertices[old[i]]); var b = filter.transform.TransformPoint(vertices[old[i + 1]]); var c = filter.transform.TransformPoint(vertices[old[i + 2]]);
                // The existing surface is an integer grid. Refuse unfamiliar triangles crossing the cut.
                float minX = Mathf.Min(a.x, b.x, c.x), maxX = Mathf.Max(a.x, b.x, c.x), minZ = Mathf.Min(a.z, b.z, c.z), maxZ = Mathf.Max(a.z, b.z, c.z);
                bool overlap = maxX > area.xMin + .0001f && minX < area.xMax - .0001f && maxZ > area.yMin + .0001f && minZ < area.yMax - .0001f;
                if (overlap && (minX < area.xMin - .0001f || maxX > area.xMax + .0001f || minZ < area.yMin - .0001f || maxZ > area.yMax + .0001f))
                    throw new InvalidOperationException("Water triangle crosses local repair boundary: " + filter.name);
                var centre = (a + b + c) / 3;
                if (!area.Contains(new Vector2(centre.x, centre.z))) triangles.AddRange(new[] { old[i], old[i + 1], old[i + 2] });
            }
            int section = filter.GetComponent<WatersideWaterSurface>().section;
            const float step = .5f;
            for (float z = area.yMin; z < area.yMax; z += step) for (float x = area.xMin; x < area.xMax; x += step)
            {
                if (WatersideLayout.WaterSection(x + step / 2, z + step / 2) != section) continue;
                var corners = new[] { new Vector2(x, z), new Vector2(x, z + step), new Vector2(x + step, z + step), new Vector2(x + step, z) };
                var polygon = new List<Vector2>();
                for (int edge = 0; edge < 4; edge++)
                {
                    Vector2 a = corners[edge], b = corners[(edge + 1) % 4];
                    bool wa = SurfaceEdge(a, terrain) < 0, wb = SurfaceEdge(b, terrain) < 0;
                    if (wa) polygon.Add(a);
                    if (wa == wb) continue;
                    Vector2 wet = wa ? a : b, dry = wa ? b : a;
                    for (int j = 0; j < 16; j++) { Vector2 mid = (wet + dry) * .5f; if (SurfaceEdge(mid, terrain) < 0) wet = mid; else dry = mid; }
                    polygon.Add((wet + dry) * .5f);
                }
                int first = vertices.Count;
                foreach (var p in polygon)
                {
                    vertices.Add(filter.transform.InverseTransformPoint(new Vector3(p.x, WatersideLayout.WaterHeight(p.x, p.y), p.y)));
                    uv.Add(new Vector2((p.x + 120) / 240, (p.y + 120) / 240));
                }
                for (int i = 1; i + 1 < polygon.Count; i++) triangles.AddRange(new[] { first, first + i, first + i + 1 });
            }
            // Drop abandoned vertices so a second repair does not grow the mesh.
            var indices = new Dictionary<int, int>(); var compact = new List<Vector3>(); var compactUV = new List<Vector2>();
            for (int i = 0; i < triangles.Count; i++)
            {
                int source = triangles[i];
                if (!indices.TryGetValue(source, out int target)) { target = compact.Count; indices.Add(source, target); compact.Add(vertices[source]); compactUV.Add(uv[source]); }
                triangles[i] = target;
            }
            mesh.Clear(); mesh.indexFormat = IndexFormat.UInt32; mesh.SetVertices(compact); mesh.SetUVs(0, compactUV);
            mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        }

        static string OutsideMeshSignature(MeshFilter filter, Rect area)
        {
            var mesh = filter.sharedMesh; var v = mesh.vertices; var uv = mesh.uv; var t = mesh.triangles;
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            {
                for (int i = 0; i < t.Length; i += 3)
                {
                    var centre = filter.transform.TransformPoint((v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3); if (area.Contains(new Vector2(centre.x, centre.z))) continue;
                    for (int j = 0; j < 3; j++) { int n = t[i + j]; writer.Write(v[n].x); writer.Write(v[n].y); writer.Write(v[n].z); writer.Write(uv[n].x); writer.Write(uv[n].y); }
                }
                writer.Flush(); using (var sha = System.Security.Cryptography.SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(stream.ToArray()));
            }
        }

        public static string Check()
        {
            var all = Objects(); var terrain = all.Select(t => t.GetComponent<Terrain>()).Single(t => t);
            var triangles = new List<Vector3[]>();
            foreach (var water in Water(all))
            {
                var v = water.sharedMesh.vertices; var t = water.sharedMesh.triangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    var a = water.transform.TransformPoint(v[t[i]]); var b = water.transform.TransformPoint(v[t[i + 1]]); var c = water.transform.TransformPoint(v[t[i + 2]]);
                    if (Inside((a + b + c) / 3)) triangles.Add(new[] { a, b, c });
                }
            }
            int checkedSamples = 0;
            for (int segment = 1; segment < Centre.Length; segment++) for (float t = .013f; t < 1; t += .027f)
            {
                var forward = (Centre[segment] - Centre[segment - 1]).normalized;
                for (int side = -1; side <= 1; side++)
                {
                    var p = Vector3.Lerp(Centre[segment - 1], Centre[segment], t) + new Vector3(-forward.z, 0, forward.x) * (side * 2.03f);
                    if (!Inside(p)) continue;
                    if (!WatersideLayout.IsWater(p.x, p.z)) throw new InvalidOperationException("Dry gap in mouth at " + p);
                    float water = WatersideLayout.WaterHeight(p.x, p.z);
                    if (p.z >= terrain.transform.position.z && water - terrain.SampleHeight(p) - terrain.transform.position.y < .25f)
                        throw new InvalidOperationException("River bed blocks outlet at " + p);
                    if (!triangles.Any(v => Covers(p, v[0], v[1], v[2]))) throw new InvalidOperationException("Missing water mesh at " + p);
                    checkedSamples++;
                }
            }
            return "PASS " + checkedSamples + " mouth cross-section samples: connected water footprint, submerged bed and real mesh coverage.";
        }
        static bool Covers(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            float ab = (b.x - a.x) * (p.z - a.z) - (b.z - a.z) * (p.x - a.x);
            float bc = (c.x - b.x) * (p.z - b.z) - (c.z - b.z) * (p.x - b.x);
            float ca = (a.x - c.x) * (p.z - c.z) - (a.z - c.z) * (p.x - c.x);
            return ab <= .00001f && bc <= .00001f && ca <= .00001f;
        }
        static void VerifyOutside(TerrainData data, float[,] bh, float[,,] ba, Texture2D map, Color[] bp, Vector3 origin, Vector3 size)
        {
            var h = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
            for (int z = 0; z < h.GetLength(0); z++) for (int x = 0; x < h.GetLength(1); x++)
                if (!Inside(new Vector3(origin.x + x * size.x / (h.GetLength(1) - 1), 0, origin.z + z * size.z / (h.GetLength(0) - 1))) && h[z, x] != bh[z, x]) throw new Exception("Height changed outside mouth.");
            var a = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            for (int z = 0; z < data.alphamapHeight; z++) for (int x = 0; x < data.alphamapWidth; x++)
                if (!Inside(new Vector3(origin.x + x * size.x / (data.alphamapWidth - 1), 0, origin.z + z * size.z / (data.alphamapHeight - 1))))
                    for (int l = 0; l < data.alphamapLayers; l++) if (a[z, x, l] != ba[z, x, l]) throw new Exception("Paint changed outside mouth.");
            var p = map.GetPixels();
            for (int z = 0; z < map.height; z++) for (int x = 0; x < map.width; x++)
                if (!Inside(new Vector3(origin.x + x * size.x / (map.width - 1), 0, origin.z + z * size.z / (map.height - 1))) && p[z * map.width + x] != bp[z * map.width + x]) throw new Exception("Grass changed outside mouth.");
        }
    }
}
