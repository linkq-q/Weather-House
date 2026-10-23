using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.Hillside
{
    [ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(250)]
    public sealed class HillsideGrassRenderer : MonoBehaviour
    {
        public Terrain terrain;
        public Mesh[] variants;
        public Material[] materials;
        public float[] speciesWeights = { 78, 6, .8f };
        public int seed = 431;
        public float clumpsPerSquareMeter = 28.0112f;
        public float cellSize = 16;
        [Tooltip("此距离内保持原草地完整密度；远处连续降低密度，不截断为方形草块。")]
        public float visibleRadius = 42;
        public float maximumSlope = 36;
        public Camera viewCamera;
        [NonSerialized] public bool manualCaptureOnly;

        sealed class Cell
        {
            public Bounds bounds;
            public Vector3 viewPosition;
            public float densityFraction;
            public readonly List<Batch> batches = new List<Batch>();
        }
        struct CellRequest
        {
            public Vector2Int key;
            public Bounds bounds;
            public float priority;
        }
        sealed class Batch
        {
            public int variant;
            public Matrix4x4[] matrices;
        }

        readonly Dictionary<Vector2Int, Cell> cells = new Dictionary<Vector2Int, Cell>();
        readonly List<Vector2Int> retired = new List<Vector2Int>();
        readonly List<CellRequest> requests = new List<CellRequest>();
        readonly HashSet<Vector2Int> requestedKeys = new HashSet<Vector2Int>();
        readonly Plane[] frustum = new Plane[6];
        public int CachedCellCount => cells.Count;
        public int VisibleClumpCount { get; private set; }

        void OnEnable()
        {
#if UNITY_EDITOR
            UnityEditor.SceneView.duringSceneGui += DrawSceneView;
#endif
        }
        void OnDisable()
        {
            cells.Clear();
#if UNITY_EDITOR
            UnityEditor.SceneView.duringSceneGui -= DrawSceneView;
#endif
        }
        void OnValidate() { cells.Clear(); }

#if UNITY_EDITOR
        void DrawSceneView(UnityEditor.SceneView view)
        {
            if (!manualCaptureOnly && !Application.isPlaying && Event.current.type == EventType.Repaint) DrawForCamera(view.camera, false);
        }
#endif

        void LateUpdate()
        {
            if (!manualCaptureOnly && Application.isPlaying) DrawForCamera(viewCamera ? viewCamera : Camera.main, false);
        }

        // Camera.Render review captures can populate the same runtime path without waiting for Update.
        public void DrawForCamera(Camera camera, bool populateImmediately = true)
        {
            if (!terrain || !terrain.terrainData || variants == null || variants.Length != 12 ||
                materials == null || materials.Length != 3 || speciesWeights == null || speciesWeights.Length != 3) return;
            if (!camera || !camera.gameObject.activeInHierarchy) return;
            float edge = Mathf.Max(4, cellSize);
            Ray ray = camera.ViewportPointToRay(new Vector3(.5f, .5f));
            Vector3 focus = camera.transform.position + camera.transform.forward * 20;
            var groundCollider = terrain.GetComponent<TerrainCollider>();
            // Trees, characters and additive-scene colliders must not move the grass streaming focus.
            if (groundCollider && groundCollider.Raycast(ray, out RaycastHit hit, Mathf.Max(300, camera.farClipPlane))) focus = hit.point;
            GeometryUtility.CalculateFrustumPlanes(camera, frustum);
            VisibleClumpCount = 0;
            int buildBudget = populateImmediately ? int.MaxValue : Application.isPlaying ? 2 : 8;
            Vector3 origin = terrain.transform.position, terrainSize = terrain.terrainData.size;
            requests.Clear(); requestedKeys.Clear();
            for (int z = Mathf.FloorToInt(origin.z / edge); z <= Mathf.FloorToInt((origin.z + terrainSize.z - .001f) / edge); z++)
                for (int x = Mathf.FloorToInt(origin.x / edge); x <= Mathf.FloorToInt((origin.x + terrainSize.x - .001f) / edge); x++)
                {
                    var key = new Vector2Int(x, z);
                    Bounds preview = CellBounds(key, edge);
                    if (!GeometryUtility.TestPlanesAABB(frustum, preview)) continue;
                    requests.Add(new CellRequest { key = key, bounds = preview, priority = preview.SqrDistance(focus) });
                    requestedKeys.Add(key);
                }
            requests.Sort((a, b) => a.priority.CompareTo(b.priority));
            foreach (CellRequest request in requests)
            {
                bool exists = cells.TryGetValue(request.key, out Cell cell);
                float density = DensityFraction(Vector3.Distance(camera.transform.position,
                    exists ? cell.bounds.center : request.bounds.center));
                bool stale = exists && (populateImmediately ? cell.viewPosition != camera.transform.position :
                    density > cell.densityFraction * 1.2f || density < cell.densityFraction * .8f);
                if ((!exists || stale) && buildBudget > 0)
                {
                    buildBudget--;
                    cell = BuildCell(request.key, edge, camera.transform.position);
                    cells[request.key] = cell;
                }
                if (cell == null || !GeometryUtility.TestPlanesAABB(frustum, cell.bounds)) continue;
                foreach (Batch batch in cell.batches)
                {
                    Mesh mesh = variants[batch.variant];
                    Material material = materials[batch.variant / 4];
                    if (!mesh || !material) continue;
                    Graphics.DrawMeshInstanced(mesh, 0, material, batch.matrices, batch.matrices.Length,
                        null, ShadowCastingMode.Off, true, gameObject.layer, null, LightProbeUsage.Off);
                    VisibleClumpCount += batch.matrices.Length;
                }
            }
            retired.Clear();
            foreach (var pair in cells)
                if (!requestedKeys.Contains(pair.Key)) retired.Add(pair.Key);
            foreach (Vector2Int key in retired) cells.Remove(key);
        }

        Bounds CellBounds(Vector2Int key, float edge)
        {
            return new Bounds(new Vector3((key.x + .5f) * edge,
                terrain.transform.position.y + terrain.terrainData.size.y * .5f, (key.y + .5f) * edge),
                new Vector3(edge + 2, terrain.terrainData.size.y + 2, edge + 2));
        }

        float DensityFraction(float distance)
        {
            float near = Mathf.Max(12, visibleRadius);
            if (distance <= near) return 1;
            if (distance < near * 2.3f) return Mathf.Lerp(1, .18f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(near, near * 2.3f, distance)));
            if (distance < near * 5) return Mathf.Lerp(.18f, .018f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(near * 2.3f, near * 5, distance)));
            return Mathf.Lerp(.018f, .006f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(near * 5, near * 11, distance)));
        }

        Cell BuildCell(Vector2Int key, float edge, Vector3 cameraPosition)
        {
            var result = new Cell();
            var groups = new List<Matrix4x4>[12];
            for (int i = 0; i < groups.Length; i++) groups[i] = new List<Matrix4x4>();
            var random = new System.Random(unchecked(seed ^ key.x * 73856093 ^ key.y * 19349663));
            Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            float totalWeight = Mathf.Max(0, speciesWeights[0]) + Mathf.Max(0, speciesWeights[1]) + Mathf.Max(0, speciesWeights[2]);
            int maximum = Mathf.CeilToInt(edge * edge * Mathf.Clamp(clumpsPerSquareMeter, 0, 40));
            int attempts = Mathf.CeilToInt(maximum * DensityFraction(Mathf.Sqrt(CellBounds(key, edge).SqrDistance(cameraPosition))));
            if (totalWeight <= 0) attempts = 0;
            for (int i = 0; i < attempts; i++)
            {
                float x = (key.x + (float)random.NextDouble()) * edge;
                float z = (key.y + (float)random.NextDouble()) * edge;
                float pick = (float)random.NextDouble() * totalWeight;
                int species = pick < speciesWeights[0] ? 0 : pick < speciesWeights[0] + speciesWeights[1] ? 1 : 2;
                int variant = species * 4 + random.Next(4);
                float yaw = (float)random.NextDouble() * 360, scale = .8f + (float)random.NextDouble() * .4f;
                // Prefix ranks make every distant sample a subset of the same near-field seed.
                float rank = (i + (float)random.NextDouble()) / Mathf.Max(1, maximum);
                float u = (x - origin.x) / size.x, v = (z - origin.z) / size.z;
                if (u <= .004f || u >= .996f || v <= .004f || v >= .996f) continue;
                float height = terrain.SampleHeight(new Vector3(x, 0, z)) + origin.y - .025f;
                float distance = Vector3.Distance(cameraPosition, new Vector3(x, height, z));
                float shore = float.PositiveInfinity;
                foreach (HillsideLayout.WaterBody water in HillsideLayout.Waters)
                    shore = Mathf.Min(shore, water.SignedDistance(x, z));
                float coverage = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.6f, 2, shore)) *
                    Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, 1.8f, HillsideLayout.PathEdgeDistance(x, z))) *
                    (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(maximumSlope - 8, maximumSlope, terrain.terrainData.GetSteepness(u, v))));
                if (rank >= DensityFraction(distance) * coverage) continue;
                scale *= Mathf.Lerp(1, .65f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(75, 260, distance)));
                groups[variant].Add(Matrix4x4.TRS(new Vector3(x, height, z), Quaternion.Euler(0, yaw, 0), Vector3.one * scale));
                low = Mathf.Min(low, height); high = Mathf.Max(high, height);
            }
            if (float.IsPositiveInfinity(low)) { low = origin.y; high = origin.y + size.y; }
            result.bounds = new Bounds(new Vector3((key.x + .5f) * edge, (low + high) * .5f + .4f, (key.y + .5f) * edge),
                new Vector3(edge + 2, high - low + 2, edge + 2));
            result.viewPosition = cameraPosition;
            result.densityFraction = DensityFraction(Vector3.Distance(cameraPosition, result.bounds.center));
            for (int variant = 0; variant < groups.Length; variant++)
                for (int start = 0; start < groups[variant].Count; start += 1023)
                {
                    int count = Math.Min(1023, groups[variant].Count - start);
                    var matrices = new Matrix4x4[count];
                    groups[variant].CopyTo(start, matrices, 0, count);
                    result.batches.Add(new Batch { variant = variant, matrices = matrices });
                }
            return result;
        }

#if UNITY_EDITOR
        public string VerifyPlacement()
        {
            int checkedClumps = 0;
            foreach (Vector3 landmark in new[] { HillsideLayout.A, HillsideLayout.B, HillsideLayout.E })
            {
                var key = new Vector2Int(Mathf.FloorToInt(landmark.x / cellSize), Mathf.FloorToInt(landmark.z / cellSize));
                Vector3 nearView = landmark + Vector3.up * 14;
                Cell first = BuildCell(key, cellSize, nearView), repeated = BuildCell(key, cellSize, nearView);
                if (first.batches.Count != repeated.batches.Count) throw new InvalidOperationException("Grass seed changed the batch count.");
                for (int i = 0; i < first.batches.Count; i++)
                {
                    Matrix4x4[] a = first.batches[i].matrices, b = repeated.batches[i].matrices;
                    if (a.Length != b.Length) throw new InvalidOperationException("Grass seed changed the placement count.");
                    for (int j = 0; j < a.Length; j++)
                    {
                        if (a[j] != b[j]) throw new InvalidOperationException("Grass placement must repeat for the same cell and seed.");
                        Vector3 p = a[j].GetColumn(3);
                        if (HillsideLayout.IsWater(p.x, p.z, .59f) || HillsideLayout.PathEdgeDistance(p.x, p.z) < -.001f)
                            throw new InvalidOperationException("Grass entered a water body or the clear walking path.");
                        float ground = terrain.SampleHeight(p) + terrain.transform.position.y;
                        if (Mathf.Abs(p.y + .025f - ground) > .001f) throw new InvalidOperationException("Grass does not meet the generated terrain.");
                        checkedClumps++;
                    }
                }
            }
            if (checkedClumps < 100) throw new InvalidOperationException("The three route regions need visible meadow coverage.");
            var distantKey = new Vector2Int(Mathf.FloorToInt(HillsideLayout.B.x / cellSize), Mathf.FloorToInt(HillsideLayout.B.z / cellSize));
            Cell near = BuildCell(distantKey, cellSize, HillsideLayout.B + Vector3.up * 14);
            Cell far = BuildCell(distantKey, cellSize, HillsideLayout.B + Vector3.up * 245);
            var nearPoints = new HashSet<Vector3>();
            foreach (Batch batch in near.batches) foreach (Matrix4x4 matrix in batch.matrices) nearPoints.Add(matrix.GetColumn(3));
            int distantClumps = 0;
            foreach (Batch batch in far.batches) foreach (Matrix4x4 matrix in batch.matrices)
            {
                if (!nearPoints.Contains(matrix.GetColumn(3))) throw new InvalidOperationException("Distant grass must retain near-field positions.");
                distantClumps++;
            }
            if (distantClumps == 0 || distantClumps >= nearPoints.Count) throw new InvalidOperationException("Distant grass needs sparse coverage without a hard distance cutoff.");
            return "PASS: " + checkedClumps + " deterministic, grounded grass placements; clear water / paths; " + distantClumps + " distant clumps retain near-field positions.";
        }
#endif
    }
}
