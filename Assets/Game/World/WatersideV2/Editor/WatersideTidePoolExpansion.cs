using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    // Only the three coastal pools are patched; existing scene edits and the rest of the heightmap stay intact.
    public static class WatersideTidePoolExpansion
    {
        const string RootName = "V3 潮池群 · 宽水湾与断续礁岸";
        const string Folder = "Assets/Game/World/WatersideV2/Generated/TidePoolV3";
        const string Review = "WatersideV3Review";
        static readonly Rect Patch = Rect.MinMaxRect(49, -44, 68, -20);
        const float Bank = .85f;
        const string BankRootName = "岸坡修整 · 草帽岩层与缓坡";
        const float RefinedBank = 2.7f;

        [Serializable] sealed class HarborWindow
        { public bool applied; public string terrainGuid; public int x, z, width, height; }

        // Follow-up to Apply: preserve the saved scene and grade only the already excavated pool margins.
        public static string RefineBanks()
        {
            WatersideSceneBuilder.RequireSaved();
            Scene scene = RequireScene();
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (!root) throw new InvalidOperationException("Expand the tide pools before refining their banks.");
            if (root.transform.Find(BankRootName)) return "Bank refinement already exists; it was not applied twice.";
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            Terrain terrain = all.Select(t => t.GetComponent<Terrain>()).FirstOrDefault(t => t);
            HillsideGrassRenderer grass = all.Select(t => t.GetComponent<HillsideGrassRenderer>()).FirstOrDefault(g => g && g.terrain == terrain);
            var rock = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HIGHLANDS - Stylized Environment/Prefabs/Forest/Rock_1.prefab");
            if (!terrain || !grass || !grass.placementMap || !rock) throw new InvalidOperationException("The existing terrain, grass and shoreline rocks are required.");
            var data = terrain.terrainData;
            var harbor = JsonUtility.FromJson<HarborWindow>(File.ReadAllText(Review + "/harbor-ground.json"));
            if (!harbor.applied || harbor.terrainGuid != AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data)))
                throw new InvalidOperationException("The saved harbor ground window must belong to this terrain.");
            Vector2 harborMin = World(terrain, harbor.x, harbor.z, data.heightmapResolution, data.heightmapResolution);
            Vector2 harborMax = World(terrain, harbor.x + harbor.width - 1, harbor.z + harbor.height - 1, data.heightmapResolution, data.heightmapResolution);
            Rect harborBounds = Rect.MinMaxRect(harborMin.x, harborMin.y, harborMax.x, harborMax.y);
            float[,] before = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
            float[,,] beforeAlpha = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            Color[] beforeGrass = grass.placementMap.GetPixels();
            var attached = all.Where(t => t.parent && t.gameObject.activeInHierarchy &&
                (t.parent.name == "断续岩块与草帽岸线" || t.parent.name == "水岸群落 · 实体叶片与厚花穗" ||
                 (t.parent == root.transform && (t.name.StartsWith("潮池断续大礁") || t.name.StartsWith("礁缝碎石")))))
                .Where(t => Patch.Contains(new Vector2(t.position.x, t.position.z)) && !Inside(harborBounds, new Vector2(t.position.x, t.position.z)))
                .ToDictionary(t => t, t => WatersideLayout.GroundHeight(terrain, t.position.x, t.position.z));
            RectInt window = Samples(terrain, data.heightmapResolution, data.heightmapResolution);
            var heights = (float[,])before.Clone();
            var influence = new float[window.height, window.width];
            for (int z = 0; z < window.height; z++)
                for (int x = 0; x < window.width; x++)
                {
                    int hx = window.x + x, hz = window.y + z;
                    Vector2 p = World(terrain, hx, hz, data.heightmapResolution, data.heightmapResolution);
                    float edge = PoolDistance(p.x, p.y, out _);
                    float mask = BankInfluence(p, edge, harborBounds);
                    influence[z, x] = mask;
                    if (mask <= 0 || edge < 0) continue;
                    float old = before[hz, hx] * data.size.y + terrain.transform.position.y;
                    float lower = Mathf.Min(old, Mathf.Lerp(.12f, old, Smooth(0, RefinedBank, edge)));
                    heights[hz, hx] = (Mathf.Lerp(old, lower, mask) - terrain.transform.position.y) / data.size.y;
                }
            // Round the cut on both sides of the waterline; the shallow toe can rise while the upper bank descends.
            int stride = Mathf.Max(1, Mathf.RoundToInt(.65f / data.heightmapScale.x));
            for (int pass = 0; pass < 6; pass++)
            {
                var next = (float[,])heights.Clone();
                for (int z = 0; z < window.height; z++)
                    for (int x = 0; x < window.width; x++)
                    {
                        float weight = influence[z, x]; if (weight <= 0) continue;
                        int hx = window.x + x, hz = window.y + z;
                        float mean = (heights[hz, hx] * 2 + heights[hz, hx - stride] + heights[hz, hx + stride] +
                            heights[hz - stride, hx] + heights[hz + stride, hx]) / 6;
                        next[hz, hx] = Mathf.Lerp(heights[hz, hx], mean, weight * .8f);
                    }
                heights = next;
            }
            var local = new float[window.height, window.width];
            int changedHeight = 0;
            for (int z = 0; z < window.height; z++)
                for (int x = 0; x < window.width; x++)
                {
                    local[z, x] = heights[window.y + z, window.x + x];
                    if (local[z, x] != before[window.y + z, window.x + x]) changedHeight++;
                }
            if (changedHeight == 0) throw new InvalidOperationException("No pool-bank samples were selected.");
            Undo.IncrementCurrentGroup(); int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Refine tide pool banks and embedded rock groups");
            Undo.RegisterCompleteObjectUndo(data, "Refine only the expanded pool margins");
            Undo.RegisterCompleteObjectUndo(grass.placementMap, "Keep grasses through shallow pool margins");
            try
            {
                data.SetHeights(window.x, window.y, local); terrain.Flush();
                RectInt a = Samples(terrain, data.alphamapWidth, data.alphamapHeight);
                float[,,] alpha = data.GetAlphamaps(a.x, a.y, a.width, a.height);
                int rockPixels = 0;
                for (int z = 0; z < a.height; z++)
                    for (int x = 0; x < a.width; x++)
                    {
                        Vector2 p = World(terrain, a.x + x, a.y + z, data.alphamapWidth, data.alphamapHeight);
                        float mask = BankInfluence(p, PoolDistance(p.x, p.y, out _), harborBounds);
                        float slope = data.GetSteepness((p.x - terrain.transform.position.x) / data.size.x, (p.y - terrain.transform.position.z) / data.size.z);
                        float weight = Mathf.Clamp01(mask * 4) * Smooth(23, 44, slope);
                        if (weight <= 0) continue;
                        for (int layer = 0; layer < data.alphamapLayers; layer++)
                            alpha[z, x, layer] = Mathf.Lerp(alpha[z, x, layer], layer == 2 ? .88f : layer == 0 ? .12f : 0, weight);
                        rockPixels++;
                    }
                data.SetAlphamaps(a.x, a.y, alpha);
                Texture2D map = grass.placementMap; Color[] pixels = (Color[])beforeGrass.Clone();
                int grassPixels = 0;
                for (int z = 0; z < map.height; z++)
                    for (int x = 0; x < map.width; x++)
                    {
                        Vector2 p = World(terrain, x, z, map.width, map.height);
                        if (!Patch.Contains(p)) continue;
                        float mask = BankInfluence(p, PoolDistance(p.x, p.y, out _), harborBounds);
                        if (mask <= 0) continue;
                        float ground = WatersideLayout.GroundHeight(terrain, p.x, p.y);
                        int index = z * map.width + x; Color color = pixels[index];
                        color.g = Mathf.Clamp01(Mathf.Max(0, -ground) / 8);
                        float slope = data.GetSteepness((p.x - terrain.transform.position.x) / data.size.x, (p.y - terrain.transform.position.z) / data.size.z);
                        float fringe = Smooth(-.20f, .18f, ground) * (1 - Smooth(43, 63, slope)) * mask;
                        color.r = Mathf.Max(color.r, .48f * fringe);
                        pixels[index] = color; grassPixels++;
                    }
                map.SetPixels(pixels); map.Apply(); EditorUtility.SetDirty(map);
                int moved = 0;
                foreach (var item in attached)
                {
                    Transform t = item.Key;
                    float delta = WatersideLayout.GroundHeight(terrain, t.position.x, t.position.z) - item.Value;
                    if (Mathf.Abs(delta) < .00001f) continue;
                    Undo.RecordObject(t, "Keep existing shoreline vegetation and rocks rooted");
                    t.position += Vector3.up * delta; moved++;
                }
                var bankRoot = new GameObject(BankRootName); bankRoot.transform.SetParent(root.transform);
                Undo.RegisterCreatedObjectUndo(bankRoot, "Embed grouped rock strata in the pool bank");
                int rocks = AddBankRocks(terrain, bankRoot.transform, rock, harborBounds);
                AssertOutsideUnchanged(terrain, before, beforeAlpha, beforeGrass, map);
                float[,] after = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
                for (int z = 0; z < after.GetLength(0); z++)
                    for (int x = 0; x < after.GetLength(1); x++)
                    {
                        Vector2 p = World(terrain, x, z, after.GetLength(1), after.GetLength(0));
                        if (!Patch.Contains(p)) continue;
                        bool protect = Inside(harborBounds, p) || WatersideLayout.WalkablePathEdgeDistance(p.x, p.y) <= .1f;
                        if (protect && before[z, x] != after[z, x]) throw new InvalidOperationException("A protected road or harbor height sample changed.");
                    }
                Check();
                EditorUtility.SetDirty(data); EditorSceneManager.MarkSceneDirty(scene); Undo.CollapseUndoOperations(undoGroup);
                string report = "PASS local bank refinement invariants\nHeight samples=" + changedHeight + "; rock-layer samples=" + rockPixels +
                    "; grass pixels=" + grassPixels + "; original shore rocks/plants regrounded=" + moved + "; embedded rocks=" + rocks +
                    "\nBank width=2.7 m with rounded shallow toes. All height/alpha/grass values outside original rectangle exactly preserved." +
                    "\nMain walkable road + 0.1 m and the full saved harbor height window exactly preserved; furniture transforms untouched." +
                    "\nExisting grass density was never reduced; shallow depth channel refreshed. Scene not saved. Actual composition/traversal still require review.";
                File.WriteAllText(Review + "/tide-bank-checks.txt", report); return report;
            }
            catch { Undo.RevertAllDownToGroup(undoGroup); throw; }
        }

        static bool Inside(Rect rect, Vector2 p)
        { return p.x >= rect.xMin && p.x <= rect.xMax && p.y >= rect.yMin && p.y <= rect.yMax; }

        static float BankInfluence(Vector2 p, float edge, Rect harbor)
        {
            if (!Patch.Contains(p) || Inside(harbor, p)) return 0;
            float core = 1;
            foreach (var pool in WatersideLayout.TidePools)
                core = Mathf.Min(core, Smooth(.7f, 1.3f, Vector2.Distance(p, new Vector2(pool.Center.x, pool.Center.z))));
            float boundary = Mathf.Min(p.x - Patch.xMin, Patch.xMax - p.x, p.y - Patch.yMin, Patch.yMax - p.y);
            float harborDistance = Vector2.Distance(p, new Vector2(Mathf.Clamp(p.x, harbor.xMin, harbor.xMax), Mathf.Clamp(p.y, harbor.yMin, harbor.yMax)));
            return (1 - Smooth(1.5f, RefinedBank, edge)) * Smooth(-1.25f, -.45f, edge) *
                Smooth(.1f, 1.15f, WatersideLayout.WalkablePathEdgeDistance(p.x, p.y)) *
                Smooth(.65f, 1.2f, WatersideLayout.TidePool.TrailDistance(p.x, p.y)) * Smooth(0, .75f, boundary) * Smooth(0, .8f, harborDistance) * core;
        }

        static int AddBankRocks(Terrain terrain, Transform root, GameObject source, Rect harbor)
        {
            var random = new System.Random(174224); var placed = new List<Vector2>(); int count = 0;
            for (float z = Patch.yMin + 1.2f; z < Patch.yMax - 1.2f; z += 1.25f)
                for (float x = Patch.xMin + 1.2f; x < Patch.xMax - 1.2f; x += 1.15f)
                {
                    Vector2 p = new Vector2(x, z); float edge = PoolDistance(x, z, out _);
                    if (BankInfluence(p, edge, harbor) < .2f || edge < -.45f || edge > 2.1f || placed.Any(q => Vector2.Distance(p, q) < 2.1f)) continue;
                    Vector3 normal = terrain.terrainData.GetInterpolatedNormal((x - terrain.transform.position.x) / terrain.terrainData.size.x,
                        (z - terrain.transform.position.z) / terrain.terrainData.size.z);
                    if (Vector3.Angle(normal, Vector3.up) < 32 || WatersideLayout.GroundHeight(terrain, x, z) < .05f) continue;
                    Vector2 tangent = new Vector2(-normal.z, normal.x).normalized;
                    for (int part = 0; part < 2; part++)
                    {
                        Vector2 point = p + tangent * (part == 0 ? -.42f : .48f);
                        float width = part == 0 ? 1.65f : 1.25f;
                        if (WatersideLayout.WalkablePathEdgeDistance(point.x, point.y) < width * .5f + .15f ||
                            WatersideLayout.TidePool.TrailDistance(point.x, point.y) < width * .5f + .55f ||
                            !Patch.Contains(point - Vector2.one * width * .65f) || !Patch.Contains(point + Vector2.one * width * .65f) ||
                            Inside(new Rect(harbor.xMin - width, harbor.yMin - width, harbor.width + width * 2, harbor.height + width * 2), point)) continue;
                        float rise = Mathf.Sqrt(1 - normal.y * normal.y) / Mathf.Max(.12f, normal.y) * width;
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(source, root);
                        go.name = "草帽嵌岩 " + (++count); go.transform.rotation = Quaternion.Euler(-5 + random.Next(11), random.Next(360), -4 + random.Next(9));
                        Bounds b = RenderBounds(go); go.transform.localScale *= width / Mathf.Max(b.size.x, b.size.z);
                        b = RenderBounds(go); Vector3 scale = go.transform.localScale; scale.y *= Mathf.Clamp(rise * .8f, .7f, 2.5f) / b.size.y;
                        go.transform.localScale = scale; b = RenderBounds(go);
                        float ground = WatersideLayout.GroundHeight(terrain, point.x, point.y);
                        go.transform.position += new Vector3(point.x - b.center.x, ground - b.min.y - b.size.y * .52f, point.y - b.center.z);
                        go.isStatic = true;
                    }
                    placed.Add(p);
                }
            return count;
        }

        const string RockyPoolRoot = "潮池空间修整 · 浅床石脊与厚礁岸";

        // A follow-up on the saved local patch: lift a shallow east basin and dry reef sills.
        // The sea mesh remains the same single surface; raised terrain occludes it naturally.
        public static string FormRockyTidePools()
        {
            WatersideSceneBuilder.RequireSaved();
            Scene scene = RequireScene();
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (!root || !root.transform.Find(BankRootName)) throw new InvalidOperationException("Refine the existing tide banks before forming the rocky pools.");
            if (root.transform.Find(RockyPoolRoot)) return CheckRockyTidePools();
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            Terrain terrain = all.Select(t => t.GetComponent<Terrain>()).FirstOrDefault(t => t);
            HillsideGrassRenderer grass = all.Select(t => t.GetComponent<HillsideGrassRenderer>()).FirstOrDefault(g => g && g.terrain == terrain);
            MeshFilter sea = all.Where(t => t.name == "海湾").Select(t => t.GetComponent<MeshFilter>()).FirstOrDefault(f => f);
            var rocks = new[] {
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HIGHLANDS - Stylized Environment/Prefabs/Forest/Rock_1.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HIGHLANDS - Stylized Environment/Prefabs/Forest/Rock_2.prefab") };
            var paving = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HIGHLANDS - Stylized Environment/Prefabs/Forest/Stone_2.prefab");
            if (!terrain || !grass || !grass.placementMap || !sea || !sea.sharedMesh || rocks.Any(r => !r) || !paving)
                throw new InvalidOperationException("Existing terrain, grass, sea, rock and paving assets are required.");
            var data = terrain.terrainData;
            Rect harbor = RockyHarborWindow(terrain);
            var beforeH = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
            var beforeA = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            var beforeP = grass.placementMap.GetPixels();
            var beforeTransforms = all.ToDictionary(t => t, t => (position: t.position, rotation: t.rotation, scale: t.localScale));
            Mesh originalSea = sea.sharedMesh;
            RectInt window = Samples(terrain, data.heightmapResolution, data.heightmapResolution);
            var local = data.GetHeights(window.x, window.y, window.width, window.height);
            int changedHeight = 0, raisedHeight = 0;
            for (int z = 0; z < window.height; z++)
                for (int x = 0; x < window.width; x++)
                {
                    Vector2 p = World(terrain, window.x + x, window.y + z, data.heightmapResolution, data.heightmapResolution);
                    float mask = RockyProtection(p, harbor); if (mask <= 0) continue;
                    float old = local[z, x] * data.size.y + terrain.transform.position.y;
                    float target = old;
                    float eastEdge = WatersideLayout.TidePools[1].SignedDistance(p.x, p.y);
                    float shallow = 1 - Smooth(-.12f, .50f, eastEdge);
                    if (shallow > 0)
                    {
                        float bed = -.56f + .28f * Smooth(-2.4f, -.8f, eastEdge);
                        target = Mathf.Lerp(target, Mathf.Max(target, bed), shallow);
                    }
                    float crest; float rim = RockyRim(p, out crest);
                    target = Mathf.Lerp(target, Mathf.Max(target, crest), rim);
                    float result = Mathf.Lerp(old, target, mask);
                    if (Mathf.Abs(result - old) < .00001f) continue;
                    local[z, x] = (result - terrain.transform.position.y) / data.size.y;
                    changedHeight++; if (result > old) raisedHeight++;
                }
            if (changedHeight == 0) throw new InvalidOperationException("The rocky pool follow-up did not select any height samples.");
            Undo.IncrementCurrentGroup(); int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Shape shallow tide pools and grouped reef sills");
            Undo.RegisterCompleteObjectUndo(data, "Raise only the local pool bed and reef sills");
            Undo.RegisterCompleteObjectUndo(grass.placementMap, "Retain grasses in the shallow tide fringe");
            try
            {
                data.SetHeights(window.x, window.y, local); terrain.Flush();
                RectInt aw = Samples(terrain, data.alphamapWidth, data.alphamapHeight);
                var alpha = data.GetAlphamaps(aw.x, aw.y, aw.width, aw.height);
                int changedAlpha = 0;
                for (int z = 0; z < aw.height; z++)
                    for (int x = 0; x < aw.width; x++)
                    {
                        Vector2 p = World(terrain, aw.x + x, aw.y + z, data.alphamapWidth, data.alphamapHeight);
                        float mask = RockyProtection(p, harbor); if (mask <= 0) continue;
                        float rim = RockyRim(p, out _);
                        float floor = 1 - Smooth(-.12f, .50f, WatersideLayout.TidePools[1].SignedDistance(p.x, p.y));
                        float weight = Mathf.Max(rim, floor * .8f) * mask; if (weight <= 0) continue;
                        float rock = Mathf.Lerp(.16f, .82f, rim);
                        for (int layer = 0; layer < data.alphamapLayers; layer++)
                            alpha[z, x, layer] = Mathf.Lerp(alpha[z, x, layer], layer == 2 ? rock : layer == 3 ? 1 - rock : 0, weight);
                        changedAlpha++;
                    }
                data.SetAlphamaps(aw.x, aw.y, alpha);
                var map = grass.placementMap; var pixels = (Color[])beforeP.Clone(); int changedGrass = 0;
                for (int z = 0; z < map.height; z++)
                    for (int x = 0; x < map.width; x++)
                    {
                        Vector2 p = World(terrain, x, z, map.width, map.height);
                        float mask = RockyProtection(p, harbor); if (mask <= 0) continue;
                        float rim = RockyRim(p, out _);
                        float floor = 1 - Smooth(-.12f, .5f, WatersideLayout.TidePools[1].SignedDistance(p.x, p.y));
                        if (Mathf.Max(rim, floor) <= 0) continue;
                        float ground = WatersideLayout.GroundHeight(terrain, p.x, p.y);
                        int index = z * map.width + x; Color color = pixels[index];
                        color.g = Mathf.Clamp01(Mathf.Max(0, -ground) / 8);
                        float fringe = Smooth(-.36f, -.12f, ground) * (1 - Smooth(.2f, .6f, ground));
                        color.r = Mathf.Max(color.r, .26f * fringe * mask * (1 - rim * .65f));
                        pixels[index] = color; changedGrass++;
                    }
                map.SetPixels(pixels); map.Apply(); EditorUtility.SetDirty(map);
                var dressing = new GameObject(RockyPoolRoot); dressing.transform.SetParent(root.transform);
                Undo.RegisterCreatedObjectUndo(dressing, "Add grouped tide pool reef edges and stone spine");
                int rockCount = AddRockyPoolEdges(terrain, dressing.transform, rocks, harbor);
                int slabCount = AddRockyPoolPaving(terrain, dressing.transform, paving, harbor);
                AssertOutsideUnchanged(terrain, beforeH, beforeA, beforeP, map);
                AssertRockyProtected(terrain, harbor, beforeH, beforeA, beforeP, map);
                foreach (var item in beforeTransforms)
                    if (!item.Key || item.Key.position != item.Value.position || item.Key.rotation != item.Value.rotation || item.Key.localScale != item.Value.scale)
                        throw new InvalidOperationException("An existing object transform changed during the rocky pool patch.");
                if (sea.sharedMesh != originalSea) throw new InvalidOperationException("The existing single sea mesh changed.");
                string report = CheckRockyTidePools() + "\nChanged height=" + changedHeight + "; raised=" + raisedHeight +
                    "; alpha=" + changedAlpha + "; grass=" + changedGrass + "; added rocks=" + rockCount + "; stone slabs=" + slabCount +
                    "\nOriginal rectangle exterior, main road + 0.1m and complete HarborWindow preserved in height/alpha/grass." +
                    "\nExisting transforms preserved=" + beforeTransforms.Count + "; existing sea mesh and water material unchanged; grass density never decreased." +
                    "\nScene is not saved. Visual composition, traversal and shallow-water interaction still require actual Unity review.";
                File.WriteAllText(Review + "/tide-rocky-pools-checks.txt", report);
                EditorUtility.SetDirty(data); EditorSceneManager.MarkSceneDirty(scene); Undo.CollapseUndoOperations(undoGroup);
                return report;
            }
            catch { Undo.RevertAllDownToGroup(undoGroup); throw; }
        }

        static Rect RockyHarborWindow(Terrain terrain)
        {
            var harbor = JsonUtility.FromJson<HarborWindow>(File.ReadAllText(Review + "/harbor-ground.json"));
            var data = terrain.terrainData;
            if (!harbor.applied || harbor.terrainGuid != AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data)))
                throw new InvalidOperationException("The harbor protection window belongs to a different terrain.");
            Vector2 lo = World(terrain, harbor.x, harbor.z, data.heightmapResolution, data.heightmapResolution);
            Vector2 hi = World(terrain, harbor.x + harbor.width - 1, harbor.z + harbor.height - 1, data.heightmapResolution, data.heightmapResolution);
            return Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
        }

        static float RockyProtection(Vector2 p, Rect harbor)
        {
            if (!Patch.Contains(p) || Inside(harbor, p)) return 0;
            float boundary = Mathf.Min(p.x - Patch.xMin, Patch.xMax - p.x, p.y - Patch.yMin, Patch.yMax - p.y);
            float harborDistance = Vector2.Distance(p, new Vector2(Mathf.Clamp(p.x, harbor.xMin, harbor.xMax), Mathf.Clamp(p.y, harbor.yMin, harbor.yMax)));
            return Smooth(.1f, 1.05f, WatersideLayout.WalkablePathEdgeDistance(p.x, p.y)) *
                Smooth(0, .75f, boundary) * Smooth(0, .8f, harborDistance);
        }

        static float RockyRawEdge(WatersideLayout.TidePool pool, Vector2 p, out float angle)
        {
            float dx = (p.x - pool.Center.x) / pool.Radii.x, dz = (p.y - pool.Center.z) / pool.Radii.y;
            angle = Mathf.Atan2(dz, dx);
            float contour = 1 + .14f * Mathf.Sin(angle * 3 + pool.Phase) + .07f * Mathf.Cos(angle * 5 - pool.Phase);
            return (Mathf.Sqrt(dx * dx + dz * dz) - contour) * Mathf.Min(pool.Radii.x, pool.Radii.y);
        }

        static Vector2 RockyContourPoint(WatersideLayout.TidePool pool, float angle, float edge)
        {
            float radial = 1 + .14f * Mathf.Sin(angle * 3 + pool.Phase) + .07f * Mathf.Cos(angle * 5 - pool.Phase) + edge / Mathf.Min(pool.Radii.x, pool.Radii.y);
            return new Vector2(pool.Center.x + pool.Radii.x * radial * Mathf.Cos(angle), pool.Center.z + pool.Radii.y * radial * Mathf.Sin(angle));
        }

        static float RockyArc(int poolIndex, float angle)
        {
            float facing = Mathf.Cos(angle);
            float arc = poolIndex == 1 ? Smooth(-.95f, -.72f, facing) : Smooth(-.45f, .15f, facing);
            float opening = poolIndex == 0 ? .55f : poolIndex == 1 ? -.85f : .15f;
            float gap = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, opening * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
            return arc * Smooth(.10f, .23f, gap);
        }

        static float RockyRim(Vector2 p, out float crest)
        {
            float strongest = 0; crest = .35f;
            for (int i = 0; i < WatersideLayout.TidePools.Length; i++)
            {
                var pool = WatersideLayout.TidePools[i]; float angle;
                float edge = RockyRawEdge(pool, p, out angle);
                float value = (1 - Smooth(.38f, 1.02f, Mathf.Abs(edge + .72f))) * RockyArc(i, angle);
                if (value > strongest) { strongest = value; crest = .36f + .13f * Mathf.Sin(angle * 2.3f + pool.Phase); }
            }
            return strongest;
        }

        static bool RockyFootprintSafe(Vector2 p, float width, Rect harbor)
        {
            for (int i = 0; i < 9; i++)
            {
                Vector2 q = i == 8 ? p : p + new Vector2(Mathf.Cos(i * Mathf.PI / 4), Mathf.Sin(i * Mathf.PI / 4)) * width * .60f;
                if (!Patch.Contains(q) || Inside(harbor, q) || WatersideLayout.WalkablePathEdgeDistance(q.x, q.y) <= .12f) return false;
            }
            return true;
        }

        static int AddRockyPoolEdges(Terrain terrain, Transform root, GameObject[] sources, Rect harbor)
        {
            var random = new System.Random(174225); int count = 0;
            var placed = new List<Vector2>();
            for (int index = 0; index < WatersideLayout.TidePools.Length; index++)
            {
                var pool = WatersideLayout.TidePools[index];
                for (int sample = 0; sample < 15; sample++)
                {
                    float angle = (sample + .17f * index) * Mathf.PI * 2 / 15;
                    bool seaRim = RockyArc(index, angle) > .55f;
                    Vector2 p = RockyContourPoint(pool, angle, seaRim ? -.72f : .52f);
                    if (!seaRim && Mathf.Cos(angle) > -.25f) continue;
                    if (placed.Any(q => Vector2.Distance(q, p) < 1.25f)) continue;
                    float border = Mathf.Min(p.x - Patch.xMin, Patch.xMax - p.x, p.y - Patch.yMin, Patch.yMax - p.y);
                    float road = WatersideLayout.WalkablePathEdgeDistance(p.x, p.y) - .2f;
                    float width = Mathf.Min(2.4f + (float)random.NextDouble() * .8f, border * 1.55f, road * 1.55f);
                    if (width < .95f || !RockyFootprintSafe(p, width, harbor)) continue;
                    float ground = WatersideLayout.GroundHeight(terrain, p.x, p.y);
                    if (seaRim && ground < .08f) continue;
                    float height = seaRim ? 1.1f + (float)random.NextDouble() * .6f : 1.7f + (float)random.NextDouble() * 1.1f;
                    PlaceRockyPiece(sources[count % sources.Length], root, p, width, height, ground - height * .43f, random,
                        seaRim ? "半浸潮池厚礁 " + (++count) : "陆岸嵌入岩层 " + (++count));
                    placed.Add(p);
                }
            }
            return count;
        }

        static void PlaceRockyPiece(GameObject source, Transform root, Vector2 p, float width, float height, float bottom, System.Random random, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source, root); go.name = name;
            go.transform.rotation = Quaternion.Euler(-5 + random.Next(11), random.Next(360), -4 + random.Next(9));
            Bounds bounds = RenderBounds(go); go.transform.localScale *= width / Mathf.Max(bounds.size.x, bounds.size.z);
            bounds = RenderBounds(go); Vector3 scale = go.transform.localScale; scale.y *= height / bounds.size.y; go.transform.localScale = scale;
            bounds = RenderBounds(go);
            go.transform.position += new Vector3(p.x - bounds.center.x, bottom - bounds.min.y, p.y - bounds.center.z);
            go.isStatic = true;
        }

        static int AddRockyPoolPaving(Terrain terrain, Transform root, GameObject source, Rect harbor)
        {
            var random = new System.Random(174226); int count = 0;
            var anchors = new[] { new Vector2(51.5f, -31.3f), new Vector2(57.2f, -31.7f), new Vector2(57.1f, -35.8f), new Vector2(57.4f, -41.5f) };
            for (int segment = 1; segment < anchors.Length; segment++)
            {
                int steps = Mathf.CeilToInt(Vector2.Distance(anchors[segment - 1], anchors[segment]) / 1.7f);
                for (int i = 0; i < steps; i++)
                {
                    Vector2 p = Vector2.Lerp(anchors[segment - 1], anchors[segment], (i + .5f) / steps);
                    p += new Vector2((float)random.NextDouble() - .5f, (float)random.NextDouble() - .5f) * .22f;
                    float width = 1.55f + (float)random.NextDouble() * .55f;
                    if (!RockyFootprintSafe(p, width, harbor)) continue;
                    float ground = WatersideLayout.GroundHeight(terrain, p.x, p.y); if (ground < .04f) continue;
                    float height = .22f + (float)random.NextDouble() * .06f;
                    PlaceRockyPiece(source, root, p, width, height, ground - .055f, random, "宽薄石脊踏面 " + (++count));
                }
            }
            return count;
        }

        static void AssertRockyProtected(Terrain terrain, Rect harbor, float[,] h, float[,,] a, Color[] p, Texture2D map)
        {
            var data = terrain.terrainData; var ah = data.GetHeights(0, 0, h.GetLength(1), h.GetLength(0));
            var aa = data.GetAlphamaps(0, 0, a.GetLength(1), a.GetLength(0)); var ap = map.GetPixels();
            for (int z = 0; z < h.GetLength(0); z++) for (int x = 0; x < h.GetLength(1); x++)
                if (RockyProtection(World(terrain, x, z, h.GetLength(1), h.GetLength(0)), harbor) == 0 && h[z, x] != ah[z, x])
                    throw new InvalidOperationException("A protected terrain height changed.");
            for (int z = 0; z < a.GetLength(0); z++) for (int x = 0; x < a.GetLength(1); x++)
                if (RockyProtection(World(terrain, x, z, a.GetLength(1), a.GetLength(0)), harbor) == 0)
                    for (int layer = 0; layer < a.GetLength(2); layer++) if (a[z, x, layer] != aa[z, x, layer])
                        throw new InvalidOperationException("A protected terrain material sample changed.");
            for (int z = 0; z < map.height; z++) for (int x = 0; x < map.width; x++)
                if (RockyProtection(World(terrain, x, z, map.width, map.height), harbor) == 0 && p[z * map.width + x] != ap[z * map.width + x])
                    throw new InvalidOperationException("A protected grass sample changed.");
        }

        public static string CheckRockyTidePools()
        {
            Scene scene = RequireScene();
            var terrain = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Terrain>()).First();
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (!root || !root.transform.Find(RockyPoolRoot)) throw new InvalidOperationException("The rocky tide pool follow-up is missing.");
            var report = new StringBuilder("PASS actual tide pool depths and dry rim directions");
            for (int i = 0; i < WatersideLayout.TidePools.Length; i++)
            {
                var pool = WatersideLayout.TidePools[i];
                float depth = -WatersideLayout.GroundHeight(terrain, pool.Center.x, pool.Center.z);
                if (depth < .35f || depth > .9f || (i == 1 && (depth < .45f || depth > .65f)))
                    throw new InvalidOperationException("Pool " + i + " actual depth is outside its intended shallow basin: " + depth);
                var directions = new List<float>(); bool eastDry = false;
                for (int j = 0; j < 36; j++)
                {
                    float angle = j * Mathf.PI / 18; Vector2 p = RockyContourPoint(pool, angle, -.72f);
                    float height = WatersideLayout.GroundHeight(terrain, p.x, p.y);
                    if (height <= .12f) continue;
                    directions.Add(angle * Mathf.Rad2Deg); if (Mathf.Cos(angle) > .6f) eastDry = true;
                }
                bool distinct = directions.Any(a => directions.Any(b => Mathf.Abs(Mathf.DeltaAngle(a, b)) >= 55));
                if (!distinct || !eastDry) throw new InvalidOperationException("Pool " + i + " lacks a visible dry rim on different sides, including its sea-facing side.");
                report.Append("\nPool ").Append(i + 1).Append(" center=").Append(pool.Center).Append(" actual depth=").Append(depth.ToString("F3"))
                    .Append("; rim >0.12m directions=").Append(string.Join(",", directions.Select(a => a.ToString("F0"))))
                    .Append("; east-facing dry rim=true");
            }
            return report.ToString();
        }


        public static string Apply()
        {
            Scene scene = RequireScene();
            if (scene.GetRootGameObjects().Any(g => g.name == RootName)) return Check();
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            Terrain terrain = all.Select(t => t.GetComponent<Terrain>()).FirstOrDefault(t => t);
            HillsideGrassRenderer grass = all.Select(t => t.GetComponent<HillsideGrassRenderer>()).FirstOrDefault(g => g && g.terrain == terrain);
            MeshFilter sea = all.Where(t => t.name == "海湾").Select(t => t.GetComponent<MeshFilter>()).FirstOrDefault(m => m);
            if (!terrain || !grass || !grass.placementMap || !sea || !sea.sharedMesh)
                throw new InvalidOperationException("Existing terrain, calibrated grass and sea mesh are required.");
            var sourceRock = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HIGHLANDS - Stylized Environment/Prefabs/Forest/Rock_1.prefab");
            if (!sourceRock) throw new InvalidOperationException("The original shoreline rock prefab is missing.");
            var data = terrain.terrainData;
            float[,] heightsBefore = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
            float[,,] alphaBefore = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            Color[] grassBefore = grass.placementMap.GetPixels();
            var shoreRockGround = all.Where(t => t.parent && t.parent.name == "断续岩块与草帽岸线" &&
                t.name != "潮池低矮礁石" && t.GetComponentsInChildren<Renderer>().Length > 0)
                .Where(t => Patch.Contains(new Vector2(t.position.x, t.position.z)))
                .ToDictionary(t => t, t => {
                    Vector3 center = RenderBounds(t.gameObject).center;
                    return WatersideLayout.GroundHeight(terrain, center.x, center.z);
                });
            ValidateShapes();
            Directory.CreateDirectory(Folder); Directory.CreateDirectory(Review);
            AssetDatabase.Refresh();
            Undo.RegisterCompleteObjectUndo(data, "Expand three coastal tide pools");
            Undo.RegisterCompleteObjectUndo(grass.placementMap, "Update tide pool grass depth");
            Undo.RecordObject(sea, "Extend existing sea into tide pools");
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "Expand coastal tide pool composition");

            RectInt h = Samples(terrain, data.heightmapResolution, data.heightmapResolution);
            var heights = data.GetHeights(h.x, h.y, h.width, h.height);
            int changedHeight = 0;
            for (int z = 0; z < h.height; z++)
                for (int x = 0; x < h.width; x++)
                {
                    Vector2 p = World(terrain, h.x + x, h.y + z, data.heightmapResolution, data.heightmapResolution);
                    float edge = PoolDistance(p.x, p.y, out float depth);
                    if (edge >= Bank || WatersideLayout.WalkablePathEdgeDistance(p.x, p.y) < .7f) continue;
                    float current = heights[z, x] * data.size.y + terrain.transform.position.y;
                    float target = edge < 0 ? -.025f - depth * Smooth(0, 1.5f, -edge) :
                        Mathf.Lerp(.10f, current, Smooth(0, Bank, edge));
                    float value = (Mathf.Min(current, target) - terrain.transform.position.y) / data.size.y;
                    if (heights[z, x] != value) changedHeight++;
                    heights[z, x] = value;
                }
            data.SetHeights(h.x, h.y, heights);
            terrain.Flush();

            RectInt a = Samples(terrain, data.alphamapWidth, data.alphamapHeight);
            var alpha = data.GetAlphamaps(a.x, a.y, a.width, a.height);
            int changedAlpha = 0;
            for (int z = 0; z < a.height; z++)
                for (int x = 0; x < a.width; x++)
                {
                    Vector2 p = World(terrain, a.x + x, a.y + z, data.alphamapWidth, data.alphamapHeight);
                    float edge = PoolDistance(p.x, p.y, out _);
                    if (edge >= Bank || WatersideLayout.WalkablePathEdgeDistance(p.x, p.y) < .7f) continue;
                    float weight = (1 - Smooth(0, Bank, edge)) * (edge < 0 ? .82f : .58f);
                    for (int layer = 0; layer < data.alphamapLayers; layer++)
                        alpha[z, x, layer] = Mathf.Lerp(alpha[z, x, layer], layer == 3 ? .88f : layer == 2 ? .12f : 0, weight);
                    changedAlpha++;
                }
            data.SetAlphamaps(a.x, a.y, alpha);

            Texture2D map = grass.placementMap;
            var pixels = (Color[])grassBefore.Clone();
            int changedGrass = 0;
            for (int z = 0; z < map.height; z++)
                for (int x = 0; x < map.width; x++)
                {
                    Vector2 p = World(terrain, x, z, map.width, map.height);
                    if (!Patch.Contains(p) || PoolDistance(p.x, p.y, out _) >= Bank) continue;
                    float depth = Mathf.Max(0, -WatersideLayout.GroundHeight(terrain, p.x, p.y));
                    int index = z * map.width + x;
                    Color c = pixels[index]; c.g = Mathf.Clamp01(depth / 8);
                    // Keep grass in the shallow fringe; deeper marine sand becomes readable water.
                    c.r *= Mathf.Lerp(1, .03f, Smooth(.18f, .62f, depth));
                    pixels[index] = c; changedGrass++;
                }
            map.SetPixels(pixels); map.Apply(); EditorUtility.SetDirty(map);
            sea.sharedMesh = PatchSeaMesh(sea);
            int hidden = 0, regrounded = 0;
            foreach (Transform t in all)
            {
                if (!Patch.Contains(new Vector2(t.position.x, t.position.z))) continue;
                if (t.name == "潮池低矮礁石" && t.gameObject.activeSelf)
                {
                    Undo.RecordObject(t.gameObject, "Replace tiny tide pool ring rocks");
                    t.gameObject.SetActive(false); hidden++;
                }
                else if (t.parent && t.parent.name == "水岸群落 · 实体叶片与厚花穗" && t.gameObject.activeSelf &&
                    PoolDistance(t.position.x, t.position.z, out _) < Bank)
                {
                    Undo.RecordObject(t, "Keep existing tide pool vegetation rooted in the new bank");
                    Vector3 p = t.position; p.y = WatersideLayout.GroundHeight(terrain, p.x, p.z) - .02f; t.position = p; regrounded++;
                }
            }
            foreach (var original in shoreRockGround)
            {
                Vector3 center = RenderBounds(original.Key.gameObject).center;
                if (PoolDistance(center.x, center.z, out _) >= Bank) continue;
                Undo.RecordObject(original.Key, "Keep original shore rocks seated on expanded pool banks");
                original.Key.position += Vector3.up * (WatersideLayout.GroundHeight(terrain, center.x, center.z) - original.Value);
            }
            AddReefs(terrain, root.transform, sourceRock);
            AddStoneSpine(terrain, root.transform);
            AssertOutsideUnchanged(terrain, heightsBefore, alphaBefore, grassBefore, map);
            EditorUtility.SetDirty(data); EditorUtility.SetDirty(sea);
            EditorSceneManager.MarkSceneDirty(scene);
            string report = Check() + "\nChanged height samples=" + changedHeight + "; alpha samples=" + changedAlpha +
                "; grass pixels=" + changedGrass + "; original tiny ring rocks hidden=" + hidden + "; existing plants regrounded=" + regrounded +
                "\nAll height/alpha/grass values outside x[49,68], z[-44,-20] exactly preserved.\nSea renderer, reflection, interaction and material reused. Scene not saved.";
            File.WriteAllText(Review + "/tide-pool-checks.txt", report);
            return report;
        }

        public static string Check()
        {
            Scene scene = RequireScene();
            ValidateShapes();
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (!root) throw new InvalidOperationException("The expanded tide pool scene root is missing.");
            Terrain terrain = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Terrain>()).First();
            var report = new StringBuilder("PASS local tide pool geometry checks");
            foreach (var pool in WatersideLayout.TidePools)
            {
                float depth = -WatersideLayout.GroundHeight(terrain, pool.Center.x, pool.Center.z);
                if (depth < .35f) throw new InvalidOperationException("A tide pool is not excavated to its authored depth: " + pool.Center);
                report.Append("\nPool ").Append(pool.Center).Append("; depth=").Append(depth.ToString("F3"));
            }
            foreach (var route in WatersideLayout.Routes)
                foreach (Vector3 point in route)
                    if (Patch.Contains(new Vector2(point.x, point.z)) && WatersideLayout.IsWater(point.x, point.z, .05f))
                        throw new InvalidOperationException("The existing coastal route was flooded.");
            report.Append("\nExisting coastal route stays outside water; player focal point (57.45,-32.6) stays dry.");
            report.Append("\nReef and paving renderers=").Append(root.GetComponentsInChildren<MeshRenderer>().Length);
            report.Append("\nVisual composition and controller traversal still need actual captures/Play review.");
            return report.ToString();
        }

        static void ValidateShapes()
        {
            float area = 0;
            for (float z = Patch.yMin; z < Patch.yMax; z += .2f)
                for (float x = Patch.xMin; x < Patch.xMax; x += .2f)
                    if (PoolDistance(x, z, out _) < 0) area += .04f;
            if (area < 100 || area > 150) throw new InvalidOperationException("Unexpected expanded pool area: " + area);
            if (PoolDistance(57.45f, -32.6f, out _) < .4f)
                throw new InvalidOperationException("The central photographic footing must stay clear of the pools.");
        }

        static Mesh PatchSeaMesh(MeshFilter sea)
        {
            Mesh original = sea.sharedMesh;
            var vertices = original.vertices.ToList(); var uv = original.uv.ToList();
            int[] old = original.triangles; var triangles = new List<int>(old.Length + 10000);
            for (int i = 0; i < old.Length; i += 3)
            {
                Vector3 center = sea.transform.TransformPoint((vertices[old[i]] + vertices[old[i + 1]] + vertices[old[i + 2]]) / 3);
                if (!Patch.Contains(new Vector2(center.x, center.z)))
                    triangles.AddRange(new[] { old[i], old[i + 1], old[i + 2] });
            }
            const float step = 1;
            for (float z = Patch.yMin; z < Patch.yMax; z += step)
                for (float x = Patch.xMin; x < Patch.xMax; x += step)
                {
                    var corners = new[] { new Vector3(x, 0, z), new Vector3(x, 0, z + step),
                        new Vector3(x + step, 0, z + step), new Vector3(x + step, 0, z) };
                    if (!corners.Any(p => WatersideLayout.WaterSignedDistance(p.x, p.z) < .5f)) continue;
                    int first = vertices.Count;
                    foreach (Vector3 p in corners)
                    { vertices.Add(sea.transform.InverseTransformPoint(p)); uv.Add(new Vector2((p.x + 120) / 240, (p.z + 120) / 240)); }
                    triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
                }
            var result = new Mesh { name = "海湾 · V3 局部潮池拓展", indexFormat = IndexFormat.UInt32 };
            result.SetVertices(vertices); result.SetUVs(0, uv); result.SetTriangles(triangles, 0);
            result.RecalculateNormals(); result.RecalculateBounds();
            return WatersideNatureBuilder.Store(result, Folder + "/SeaWithExpandedPools.asset");
        }

        static void AddReefs(Terrain terrain, Transform root, GameObject source)
        {
            Vector3[] pieces = {
                new Vector3(53.1f,-23.3f,2.1f), new Vector3(56.6f,-22.9f,2.6f), new Vector3(58.2f,-26.9f,1.8f),
                new Vector3(53.4f,-29.1f,1.6f), new Vector3(59.6f,-29.2f,2.3f), new Vector3(65.5f,-31.1f,2.8f),
                new Vector3(64.1f,-36.4f,2.3f), new Vector3(60.0f,-36.3f,1.9f), new Vector3(51.9f,-35.2f,2.0f),
                new Vector3(55.8f,-35.2f,2.5f), new Vector3(57.0f,-40.4f,2.1f), new Vector3(52.5f,-41.6f,2.4f)
            };
            var random = new System.Random(174220);
            for (int i = 0; i < pieces.Length; i++)
            {
                Vector3 item = pieces[i];
                if (WatersideLayout.WalkablePathEdgeDistance(item.x, item.y) < item.z * .5f + .45f) continue;
                if (WatersideLayout.TidePool.TrailDistance(item.x, item.y) < item.z * .5f + .5f) continue;
                PlaceRock(terrain, root, source, item.x, item.y, item.z, random, "潮池断续大礁 " + (i + 1));
                if (i % 3 != 0) continue;
                for (int j = 0; j < 3; j++)
                {
                    float x = item.x + (float)(random.NextDouble() - .5) * 2.6f;
                    float z = item.y + (float)(random.NextDouble() - .5) * 2.6f;
                    if (WatersideLayout.WalkablePathEdgeDistance(x, z) < .9f || WatersideLayout.TidePool.TrailDistance(x, z) < 1.1f) continue;
                    PlaceRock(terrain, root, source, x, z, .28f + (float)random.NextDouble() * .5f, random, "礁缝碎石 " + i + "-" + j);
                }
            }
        }

        static void AddStoneSpine(Terrain terrain, Transform parent)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HIGHLANDS - Stylized Environment/Prefabs/Forest/Stone_2.prefab");
            MeshFilter filter = source.GetComponentsInChildren<MeshFilter>().First(f => f.sharedMesh && f.name.Contains("LOD0"));
            var anchors = new[] { new Vector3(51.5f,0,-31.3f), new Vector3(57.2f,0,-31.7f),
                new Vector3(57.1f,0,-35.8f), new Vector3(57.4f,0,-41.5f), new Vector3(49.5f,0,-43.4f) };
            var random = new System.Random(174221);
            for (int segment = 1; segment < anchors.Length; segment++)
            {
                int count = Mathf.CeilToInt(Vector3.Distance(anchors[segment - 1], anchors[segment]) / 1.35f);
                for (int i = 0; i < count; i++)
                {
                    if (i % 5 == 4) continue;
                    Vector3 p = Vector3.Lerp(anchors[segment - 1], anchors[segment], (i + .5f) / count);
                    float ground = WatersideLayout.GroundHeight(terrain, p.x, p.z);
                    if (ground < .04f) continue;
                    var go = new GameObject("池间埋入地表的石径 " + segment + "-" + i, typeof(MeshFilter), typeof(MeshRenderer));
                    go.transform.SetParent(parent);
                    go.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    go.GetComponent<MeshRenderer>().sharedMaterials = filter.GetComponent<MeshRenderer>().sharedMaterials;
                    Bounds b = filter.sharedMesh.bounds;
                    go.transform.localScale = new Vector3((.8f + (float)random.NextDouble() * .35f) / b.size.x, .105f / b.size.y, .78f / b.size.z);
                    go.transform.rotation = Quaternion.Euler(0, random.Next(360), 0);
                    go.transform.position = new Vector3(p.x, ground - .035f, p.z) - go.transform.TransformVector(new Vector3(b.center.x, b.min.y, b.center.z));
                    go.isStatic = true;
                }
            }
        }

        static void PlaceRock(Terrain terrain, Transform parent, GameObject source, float x, float z, float width, System.Random random, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            go.name = name; go.transform.rotation = Quaternion.Euler(-8 + random.Next(17), random.Next(360), -5 + random.Next(11));
            Bounds bounds = RenderBounds(go);
            go.transform.localScale *= width / Mathf.Max(bounds.size.x, bounds.size.z);
            bounds = RenderBounds(go);
            float ground = WatersideLayout.GroundHeight(terrain, x, z);
            go.transform.position += new Vector3(x - bounds.center.x, ground - bounds.min.y - bounds.size.y * .45f, z - bounds.center.z);
            go.isStatic = true;
        }

        static Bounds RenderBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static void AssertOutsideUnchanged(Terrain terrain, float[,] heights, float[,,] alpha, Color[] pixels, Texture2D map)
        {
            var data = terrain.terrainData;
            var afterH = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
            var afterA = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            var afterP = map.GetPixels();
            for (int z = 0; z < afterH.GetLength(0); z++)
                for (int x = 0; x < afterH.GetLength(1); x++)
                    if (!Patch.Contains(World(terrain, x, z, afterH.GetLength(1), afterH.GetLength(0))) && heights[z, x] != afterH[z, x])
                        throw new InvalidOperationException("A terrain sample outside the tide pool patch changed.");
            for (int z = 0; z < afterA.GetLength(0); z++)
                for (int x = 0; x < afterA.GetLength(1); x++)
                    if (!Patch.Contains(World(terrain, x, z, afterA.GetLength(1), afterA.GetLength(0))))
                        for (int layer = 0; layer < afterA.GetLength(2); layer++)
                            if (alpha[z, x, layer] != afterA[z, x, layer]) throw new InvalidOperationException("A surface sample outside the tide pool patch changed.");
            for (int z = 0; z < map.height; z++)
                for (int x = 0; x < map.width; x++)
                    if (!Patch.Contains(World(terrain, x, z, map.width, map.height)) && pixels[z * map.width + x] != afterP[z * map.width + x])
                        throw new InvalidOperationException("Grass outside the tide pool patch changed.");
        }

        static RectInt Samples(Terrain terrain, int width, int height)
        {
            Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
            int x = Mathf.CeilToInt((Patch.xMin - origin.x) / size.x * (width - 1));
            int z = Mathf.CeilToInt((Patch.yMin - origin.z) / size.z * (height - 1));
            int right = Mathf.FloorToInt((Patch.xMax - origin.x) / size.x * (width - 1));
            int top = Mathf.FloorToInt((Patch.yMax - origin.z) / size.z * (height - 1));
            return new RectInt(x, z, right - x + 1, top - z + 1);
        }
        static Vector2 World(Terrain terrain, int x, int z, int width, int height)
        { return new Vector2(terrain.transform.position.x + x / (width - 1f) * terrain.terrainData.size.x,
            terrain.transform.position.z + z / (height - 1f) * terrain.terrainData.size.z); }
        static float PoolDistance(float x, float z, out float depth)
        {
            float distance = float.MaxValue; depth = 0;
            foreach (var pool in WatersideLayout.TidePools)
            {
                float d = pool.SignedDistance(x, z);
                if (d < distance) { distance = d; depth = pool.Depth; }
            }
            return distance;
        }
        static float Smooth(float min, float max, float value)
        { return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(min, max, value)); }
        static Scene RequireScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != WatersideSceneBuilder.ScenePath)
                throw new InvalidOperationException("Open the saved Waterside V2 scene in Edit mode before applying the local tide pool patch.");
            return scene;
        }
    }
}
