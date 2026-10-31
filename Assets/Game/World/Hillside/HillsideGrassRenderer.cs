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
        [Tooltip("此距离内始终使用原草网格；0 关闭网格 LOD。只简化远处草叶，不改变草丛位置和密度。"), Min(0)]
        public float meshLodDistance = 60;
        [Tooltip("远离时超过上述距离加此余量才切换，靠近时到达上述距离立即恢复原网格。"), Min(0)]
        public float meshLodHysteresis = 5;
        public float maximumSlope = 36;
        public Camera viewCamera;
        [Tooltip("可选独立地图覆盖图：R 为草地覆盖率，G 为水深 / 8，B 为短草踩踏程度。空值保留山林地图规则。")]
        public Texture2D placementMap;
        [Tooltip("可选的场景局部草笔刷；不修改原覆盖图或全局草种参数。")]
        public WeatherHouse.Vegetation.GrassLocalPainter localPainter;
        [NonSerialized] public bool manualCaptureOnly;

        sealed class Cell
        {
            public Bounds bounds;
            public Vector3 viewPosition;
            public float densityFraction;
            public List<Batch> batches = new List<Batch>();
            public List<Batch> spareBatches = new List<Batch>();
            public readonly List<Candidate> candidates = new List<Candidate>();
            public readonly List<PathSegment> paths = new List<PathSegment>();
            public readonly List<Matrix4x4>[] groups = new List<Matrix4x4>[12];
            public Vector2Int key;
            public Vector3 origin, terrainSize, buildView;
            public System.Random random;
            public float edge, totalWeight, low, high;
            public int maximum, generated, attempts, filterIndex, lastUsed;
            public bool ready, building, localEditDirty;
            public float extraWidth;
            public readonly Dictionary<Camera, bool> lowDetailByCamera = new Dictionary<Camera, bool>();
        }
        struct Candidate
        {
            public Vector3 position;
            public float yaw, scale, rank, coverage;
            public int variant, index;
        }
        struct PathSegment { public Vector3 a, b; }
        struct CellRequest
        {
            public Vector2Int key;
            public Bounds bounds;
            public float priority;
            public bool visible;
        }
        sealed class Batch
        {
            public int variant;
            public Matrix4x4[] matrices;
            public int count;
        }

        readonly Dictionary<Vector2Int, Cell> cells = new Dictionary<Vector2Int, Cell>();
        readonly List<Vector2Int> retired = new List<Vector2Int>();
        readonly List<CellRequest> requests = new List<CellRequest>();
        readonly HashSet<Vector2Int> requestedKeys = new HashSet<Vector2Int>();
        readonly Plane[] frustum = new Plane[6];
        readonly HillsideGrassMeshLod meshLods = new HillsideGrassMeshLod();
        readonly Mesh[] drawMeshes = new Mesh[HillsideGrassInstanceBatcher.GroupCount];
        readonly Material[] drawMaterials = new Material[HillsideGrassInstanceBatcher.GroupCount];
        HillsideGrassInstanceBatcher instanceBatcher;
        Camera drawCamera;
        long staticCandidateQueries;
        int requestFrame;
        TerrainData cachedTerrainData;
        Vector3 cachedOrigin, cachedTerrainSize;
        Vector4 cachedSettings;
        Vector3 cachedWeights;
        int cachedSeed, budgetFrame = -1;
        Texture2D cachedPlacementMap;
        long frameDeadline;
        public int CachedCellCount => cells.Count;
        public int VisibleClumpCount { get; private set; }
        public int SubmittedDrawCount { get; private set; }

        void OnEnable()
        {
            TerrainCallbacks.heightmapChanged += TerrainHeightChanged;
#if UNITY_EDITOR
            UnityEditor.SceneView.duringSceneGui += DrawSceneView;
#endif
        }
        void OnDisable()
        {
            cells.Clear();
            meshLods.Dispose();
            instanceBatcher = null; drawCamera = null;
            Array.Clear(drawMeshes, 0, drawMeshes.Length);
            Array.Clear(drawMaterials, 0, drawMaterials.Length);
            TerrainCallbacks.heightmapChanged -= TerrainHeightChanged;
#if UNITY_EDITOR
            UnityEditor.SceneView.duringSceneGui -= DrawSceneView;
#endif
        }
        void OnValidate() { meshLods.Invalidate(); }
        void OnDestroy() { meshLods.Dispose(); }

        void ValidateCache()
        {
            var settings = new Vector4(cellSize, clumpsPerSquareMeter, maximumSlope, visibleRadius);
            var weights = new Vector3(speciesWeights[0], speciesWeights[1], speciesWeights[2]);
            if (cachedTerrainData == terrain.terrainData && cachedOrigin.Equals(terrain.transform.position) &&
                cachedTerrainSize.Equals(terrain.terrainData.size) && cachedSeed == seed &&
                cachedSettings.Equals(settings) && cachedWeights.Equals(weights) && cachedPlacementMap == placementMap) return;
            cells.Clear();
            cachedTerrainData = terrain.terrainData; cachedOrigin = terrain.transform.position;
            cachedTerrainSize = terrain.terrainData.size; cachedSeed = seed;
            cachedSettings = settings; cachedWeights = weights;
            cachedPlacementMap = placementMap;
        }
        void TerrainHeightChanged(Terrain changed, RectInt region, bool synced)
        {
            if (changed == terrain) cells.Clear();
        }

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
            VisibleClumpCount = SubmittedDrawCount = 0;
            if (instanceBatcher == null) instanceBatcher = new HillsideGrassInstanceBatcher(SubmitInstances);
            instanceBatcher.Begin(); drawCamera = camera;
            ValidateCache();
            Camera lodObserver = Application.isPlaying ? (viewCamera ? viewCamera : Camera.main) : camera;
            if (!lodObserver) lodObserver = camera;
            bool immediate = populateImmediately;
            bool mayPopulate = camera.cameraType != CameraType.Reflection &&
                (populateImmediately || !Application.isPlaying || camera == (viewCamera ? viewCamera : Camera.main));
            if (Application.isPlaying && mayPopulate && budgetFrame != Time.frameCount)
            {
                budgetFrame = Time.frameCount;
                frameDeadline = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(System.Diagnostics.Stopwatch.Frequency * .003);
            }
            long deadline = !mayPopulate ? 0 : immediate ? long.MaxValue : Application.isPlaying ? frameDeadline :
                System.Diagnostics.Stopwatch.GetTimestamp() + (long)(System.Diagnostics.Stopwatch.Frequency * .006);
            Vector3 origin = terrain.transform.position, terrainSize = terrain.terrainData.size;
            requestFrame++;
            requests.Clear(); requestedKeys.Clear();
            int minX = Mathf.FloorToInt(origin.x / edge), maxX = Mathf.FloorToInt((origin.x + terrainSize.x - .001f) / edge);
            int minZ = Mathf.FloorToInt(origin.z / edge), maxZ = Mathf.FloorToInt((origin.z + terrainSize.z - .001f) / edge);
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                {
                    var key = new Vector2Int(x, z);
                    Bounds preview = CellBounds(key, edge);
                    if (!GeometryUtility.TestPlanesAABB(frustum, preview)) continue;
                    requests.Add(new CellRequest { key = key, bounds = preview, priority = preview.SqrDistance(focus), visible = true });
                    requestedKeys.Add(key);
                }
            int visibleRequests = requests.Count;
            // Populate a one-cell fringe after visible work, so small camera turns reuse prepared grass.
            for (int i = 0; i < visibleRequests; i++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var key = requests[i].key + new Vector2Int(dx, dz);
                        if (key.x < minX || key.x > maxX || key.y < minZ || key.y > maxZ || !requestedKeys.Add(key)) continue;
                        Bounds preview = CellBounds(key, edge);
                        requests.Add(new CellRequest { key = key, bounds = preview, priority = preview.SqrDistance(focus), visible = false });
                    }
            requests.Sort((a, b) => a.visible != b.visible ? (a.visible ? -1 : 1) : a.priority.CompareTo(b.priority));
            foreach (CellRequest request in requests)
            {
                bool exists = cells.TryGetValue(request.key, out Cell cell);
                if (cell != null && mayPopulate) cell.lastUsed = requestFrame;
                bool hasBudget = mayPopulate && (immediate || System.Diagnostics.Stopwatch.GetTimestamp() < deadline);
                if (!exists && hasBudget) cell = GetOrCreateCell(request.key, edge);
                if (cell == null) continue;
                float density = DensityFraction(Vector3.Distance(camera.transform.position, cell.bounds.center));
                bool stale = cell.localEditDirty || !cell.ready || (immediate ? cell.viewPosition != camera.transform.position :
                    density > cell.densityFraction * 1.2f || density < cell.densityFraction * .8f);
                if (hasBudget && (cell.building || stale))
                {
                    if (!cell.building || immediate && cell.buildView != camera.transform.position)
                        BeginBuild(cell, camera.transform.position);
                    AdvanceBuild(cell, deadline);
                }
                if (!request.visible || !cell.ready || !GeometryUtility.TestPlanesAABB(frustum, cell.bounds)) continue;
                bool lowDetail = UseLowDetail(cell, lodObserver);
                foreach (Batch batch in cell.batches)
                {
                    if (batch.count == 0) continue;
                    Mesh mesh = SelectMesh(batch.variant, lowDetail);
                    Material material = materials[batch.variant / 4];
                    if (!mesh || !material) continue;
                    int group = batch.variant + (mesh == variants[batch.variant] ? 0 : 12);
                    drawMeshes[group] = mesh; drawMaterials[group] = material;
                    instanceBatcher.Append(group, batch.matrices, batch.count);
                    VisibleClumpCount += batch.count;
                }
            }
            instanceBatcher.Flush(); drawCamera = null;
            if (localPainter && localPainter.isActiveAndEnabled) localPainter.DrawForCamera(camera, populateImmediately);
            if (mayPopulate) RetireCells();
        }

        // Retain sampled candidates and the currently visible batches while only affected cells refilter.
        public void InvalidateLocalRegion(Rect worldBounds)
        {
            float edge = Mathf.Max(4, cellSize);
            foreach (var pair in cells)
                if (new Rect(pair.Key.x * edge, pair.Key.y * edge, edge, edge).Overlaps(worldBounds))
                { pair.Value.localEditDirty = true; pair.Value.building = false; }
        }

        void SubmitInstances(int group, Matrix4x4[] matrices, int count)
        {
            Graphics.DrawMeshInstanced(drawMeshes[group], 0, drawMaterials[group], matrices, count,
                null, ShadowCastingMode.Off, true, gameObject.layer, drawCamera, LightProbeUsage.Off);
            SubmittedDrawCount++;
        }

        bool UseLowDetail(Cell cell, Camera observer)
        {
            cell.lowDetailByCamera.TryGetValue(observer, out bool previous);
            bool result = HillsideGrassMeshLod.UseLowDetail(cell.bounds, observer.transform.position,
                previous, meshLodDistance, meshLodHysteresis);
            if (result) cell.lowDetailByCamera[observer] = true;
            else cell.lowDetailByCamera.Remove(observer);
            return result;
        }

        Mesh SelectMesh(int variant, bool lowDetail) => meshLods.Get(variants[variant], variant, lowDetail);

        void RetireCells()
        {
            // Visible/prefetched cells stay resident; retain another 48 recently used cells for turns.
            int capacity = Mathf.Max(96, requestedKeys.Count + 48);
            if (cells.Count <= capacity) return;
            retired.Clear();
            foreach (var pair in cells)
                if (!requestedKeys.Contains(pair.Key)) retired.Add(pair.Key);
            retired.Sort((a, b) => cells[a].lastUsed.CompareTo(cells[b].lastUsed));
            int remove = Mathf.Min(cells.Count - capacity, retired.Count);
            for (int i = 0; i < remove; i++) cells.Remove(retired[i]);
        }

        Bounds CellBounds(Vector2Int key, float edge)
        {
            return new Bounds(new Vector3((key.x + .5f) * edge,
                terrain.transform.position.y + terrain.terrainData.size.y * .5f, (key.y + .5f) * edge),
                new Vector3(edge + 2, terrain.terrainData.size.y + 2, edge + 2));
        }

        internal float DensityFraction(float distance)
        {
            float near = Mathf.Max(12, visibleRadius);
            if (distance <= near) return 1;
            if (distance < near * 2.3f) return Mathf.Lerp(1, .18f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(near, near * 2.3f, distance)));
            if (distance < near * 5) return Mathf.Lerp(.18f, .018f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(near * 2.3f, near * 5, distance)));
            return Mathf.Lerp(.018f, .006f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(near * 5, near * 11, distance)));
        }

        static float WaterDepth(float x, float z, float ground)
        {
            float depth = 0;
            foreach (HillsideLayout.WaterBody water in HillsideLayout.Waters)
                if (water.SignedDistance(x, z) <= 0) depth = Mathf.Max(depth, water.SurfaceY - ground);
            return depth;
        }

        Cell GetOrCreateCell(Vector2Int key, float edge)
        {
            if (cells.TryGetValue(key, out Cell existing)) return existing;
            var result = new Cell
            {
                key = key, edge = edge, bounds = CellBounds(key, edge), lastUsed = requestFrame,
                origin = terrain.transform.position, terrainSize = terrain.terrainData.size,
                maximum = Mathf.CeilToInt(edge * edge * Mathf.Clamp(clumpsPerSquareMeter, 0, 40)),
                totalWeight = Mathf.Max(0, speciesWeights[0]) + Mathf.Max(0, speciesWeights[1]) + Mathf.Max(0, speciesWeights[2]),
                random = new System.Random(unchecked(seed ^ key.x * 73856093 ^ key.y * 19349663))
            };
            for (int i = 0; i < result.groups.Length; i++) result.groups[i] = new List<Matrix4x4>();
            // Width is at most 1.6m and the coverage transition is 1.8m; farther segments cannot affect coverage.
            float minX = key.x * edge - 3.401f, maxX = (key.x + 1) * edge + 3.401f;
            float minZ = key.y * edge - 3.401f, maxZ = (key.y + 1) * edge + 3.401f;
            foreach (Vector3[] route in placementMap ? Array.Empty<Vector3[]>() : HillsideLayout.Routes)
                for (int i = 1; i < route.Length; i++)
                {
                    Vector3 a = route[i - 1], b = route[i];
                    if (Mathf.Max(a.x, b.x) < minX || Mathf.Min(a.x, b.x) > maxX ||
                        Mathf.Max(a.z, b.z) < minZ || Mathf.Min(a.z, b.z) > maxZ) continue;
                    result.paths.Add(new PathSegment { a = a, b = b });
                }
            cells.Add(key, result);
            return result;
        }

        void BeginBuild(Cell cell, Vector3 cameraPosition)
        {
            cell.buildView = cameraPosition;
            float densityLimit = localPainter && localPainter.isActiveAndEnabled ? localPainter.BaseDensityLimit(cell.key) : 1;
            cell.attempts = cell.totalWeight <= 0 ? 0 : Mathf.CeilToInt(cell.maximum *
                DensityFraction(Mathf.Sqrt(CellBounds(cell.key, cell.edge).SqrDistance(cameraPosition))) * densityLimit);
            cell.filterIndex = 0;
            cell.low = float.PositiveInfinity; cell.high = float.NegativeInfinity;
            cell.extraWidth = 0;
            foreach (List<Matrix4x4> group in cell.groups) group.Clear();
            cell.building = true;
        }

        bool AdvanceBuild(Cell cell, long deadline)
        {
            if (System.Diagnostics.Stopwatch.GetTimestamp() >= deadline) return false;
            int work = 0;
            while (cell.generated < cell.attempts)
            {
                GenerateCandidate(cell);
                if (++work % 16 == 0 && System.Diagnostics.Stopwatch.GetTimestamp() >= deadline) return false;
            }
            while (cell.filterIndex < cell.candidates.Count)
            {
                Candidate candidate = cell.candidates[cell.filterIndex];
                if (candidate.index >= cell.attempts) break;
                cell.filterIndex++;
                float distance = Vector3.Distance(cell.buildView, candidate.position);
                var local = localPainter && localPainter.isActiveAndEnabled
                    ? localPainter.SampleBase(candidate.position, candidate.variant / 4) : WeatherHouse.Vegetation.GrassLocalSample.Base;
                if (candidate.rank < DensityFraction(distance) * candidate.coverage * local.density)
                {
                    float scale = candidate.scale * Mathf.Lerp(1, .65f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(75, 260, distance)));
                    cell.groups[candidate.variant].Add(Matrix4x4.TRS(candidate.position, Quaternion.Euler(0, candidate.yaw, 0),
                        new Vector3(scale * local.widthScale, scale * local.heightScale, scale * local.widthScale)));
                    cell.low = Mathf.Min(cell.low, candidate.position.y); cell.high = Mathf.Max(cell.high, candidate.position.y);
                    if ((local.heightScale > 1 || local.widthScale > 1) && variants[candidate.variant])
                    {
                        Bounds bounds = variants[candidate.variant].bounds;
                        cell.high = Mathf.Max(cell.high, candidate.position.y + Mathf.Max(0, bounds.max.y * scale * local.heightScale - 1));
                        cell.extraWidth = Mathf.Max(cell.extraWidth, Mathf.Max(bounds.extents.x, bounds.extents.z) * scale * Mathf.Max(0, local.widthScale - 1));
                    }
                }
                if (++work % 16 == 0 && System.Diagnostics.Stopwatch.GetTimestamp() >= deadline) return false;
            }
            CompleteBuild(cell);
            return true;
        }

        void GenerateCandidate(Cell cell)
        {
            int index = cell.generated++;
            System.Random random = cell.random;
            float x = (cell.key.x + (float)random.NextDouble()) * cell.edge;
            float z = (cell.key.y + (float)random.NextDouble()) * cell.edge;
            float pick = (float)random.NextDouble() * cell.totalWeight;
            int species = pick < speciesWeights[0] ? 0 : pick < speciesWeights[0] + speciesWeights[1] ? 1 : 2;
            int variant = species * 4 + random.Next(4);
            float yaw = (float)random.NextDouble() * 360, scale = .8f + (float)random.NextDouble() * .4f;
            float rank = (index + (float)random.NextDouble()) / Mathf.Max(1, cell.maximum);
            float u = (x - cell.origin.x) / cell.terrainSize.x, v = (z - cell.origin.z) / cell.terrainSize.z;
            if (u <= .004f || u >= .996f || v <= .004f || v >= .996f) return;
            staticCandidateQueries++;
            float height = terrain.SampleHeight(new Vector3(x, 0, z)) + cell.origin.y - .025f;
            Color map = placementMap ? placementMap.GetPixelBilinear(u, v) : Color.white;
            float depth = placementMap ? map.g * 8 : WaterDepth(x, z, height + .025f);
            float coverage = (placementMap ? map.r : Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, 1.8f, LocalPathDistance(cell, x, z) - HillsideLayout.PathHalfWidth(x, z)))) *
                (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(maximumSlope - 8, maximumSlope, terrain.terrainData.GetSteepness(u, v))));
            if (depth > 0)
            {
                float patch = Mathf.PerlinNoise((x + seed * .13f) * .12f, (z - seed * .17f) * .12f);
                float bedDensity = Mathf.Lerp(.06f, .55f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.3f, .7f, patch)));
                coverage *= Mathf.Lerp(1, bedDensity, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.15f, 1.2f, depth)));
                float tallWeight = speciesWeights[1] * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .45f, depth)));
                species = pick >= speciesWeights[0] && pick < speciesWeights[0] + tallWeight ? 1 : 0;
                variant = species * 4 + variant % 4;
            }
            if (placementMap && map.b > 0)
            {
                // Worn paths still grow grass, but only short blades instead of tall flowers.
                variant %= 4;
                scale *= Mathf.Lerp(1, .42f, map.b);
            }
            if (coverage <= 0) return;
            cell.candidates.Add(new Candidate { position = new Vector3(x, height, z), variant = variant, yaw = yaw,
                scale = scale, rank = rank, coverage = coverage, index = index });
        }

        static float LocalPathDistance(Cell cell, float x, float z)
        {
            float distance = float.MaxValue;
            foreach (PathSegment path in cell.paths)
            {
                float dx = path.b.x - path.a.x, dz = path.b.z - path.a.z;
                float t = Mathf.Clamp01(((x - path.a.x) * dx + (z - path.a.z) * dz) / Mathf.Max(.0001f, dx * dx + dz * dz));
                float px = x - Mathf.Lerp(path.a.x, path.b.x, t), pz = z - Mathf.Lerp(path.a.z, path.b.z, t);
                distance = Mathf.Min(distance, Mathf.Sqrt(px * px + pz * pz));
            }
            return distance;
        }

        void CompleteBuild(Cell cell)
        {
            int batchIndex = 0;
            for (int variant = 0; variant < cell.groups.Length; variant++)
                for (int start = 0; start < cell.groups[variant].Count; start += 1023)
                {
                    int count = Math.Min(1023, cell.groups[variant].Count - start);
                    Batch batch;
                    if (batchIndex < cell.spareBatches.Count) batch = cell.spareBatches[batchIndex];
                    else { batch = new Batch(); cell.spareBatches.Add(batch); }
                    batch.variant = variant; batch.count = count;
                    if (batch.matrices == null || batch.matrices.Length < count)
                        batch.matrices = new Matrix4x4[Mathf.Min(1023, Mathf.NextPowerOfTwo(Mathf.Max(16, count)))];
                    cell.groups[variant].CopyTo(start, batch.matrices, 0, count);
                    batchIndex++;
                }
            for (int i = batchIndex; i < cell.spareBatches.Count; i++) cell.spareBatches[i].count = 0;
            List<Batch> previous = cell.batches; cell.batches = cell.spareBatches; cell.spareBatches = previous;
            float low = cell.low, high = cell.high;
            if (float.IsPositiveInfinity(low)) { low = cell.origin.y; high = cell.origin.y + cell.terrainSize.y; }
            cell.bounds = new Bounds(new Vector3((cell.key.x + .5f) * cell.edge, (low + high) * .5f + .4f, (cell.key.y + .5f) * cell.edge),
                new Vector3(cell.edge + 2 + cell.extraWidth * 2, high - low + 2, cell.edge + 2 + cell.extraWidth * 2));
            cell.viewPosition = cell.buildView;
            cell.densityFraction = DensityFraction(Vector3.Distance(cell.viewPosition, cell.bounds.center));
            cell.ready = true; cell.building = false; cell.localEditDirty = false;
        }

        // Synchronous path is retained for explicit review captures and independent Editor checks.
        Cell BuildCell(Vector2Int key, float edge, Vector3 cameraPosition)
        {
            ValidateCache();
            Cell cell = GetOrCreateCell(key, edge);
            if (cell.ready && !cell.building && !cell.localEditDirty && cell.viewPosition == cameraPosition) return cell;
            BeginBuild(cell, cameraPosition);
            AdvanceBuild(cell, long.MaxValue);
            return cell;
        }

#if UNITY_EDITOR
        Cell BuildVerificationCell(Vector2Int key, float edge, Vector3 view)
        {
            Cell source = BuildCell(key, edge, view);
            var snapshot = new Cell();
            foreach (Batch batch in source.batches)
            {
                if (batch.count == 0) continue;
                var matrices = new Matrix4x4[batch.count];
                Array.Copy(batch.matrices, matrices, batch.count);
                snapshot.batches.Add(new Batch { variant = batch.variant, matrices = matrices, count = batch.count });
            }
            return snapshot;
        }

        public string VerifyPlacement()
        {
            int checkedClumps = 0;
            foreach (Vector3 landmark in new[] { HillsideLayout.A, HillsideLayout.B, HillsideLayout.E })
            {
                var key = new Vector2Int(Mathf.FloorToInt(landmark.x / cellSize), Mathf.FloorToInt(landmark.z / cellSize));
                Vector3 nearView = landmark + Vector3.up * 14;
                Cell first = BuildVerificationCell(key, cellSize, nearView), repeated = BuildVerificationCell(key, cellSize, nearView);
                if (first.batches.Count != repeated.batches.Count) throw new InvalidOperationException("Grass seed changed the batch count.");
                for (int i = 0; i < first.batches.Count; i++)
                {
                    Matrix4x4[] a = first.batches[i].matrices, b = repeated.batches[i].matrices;
                    if (a.Length != b.Length) throw new InvalidOperationException("Grass seed changed the placement count.");
                    for (int j = 0; j < a.Length; j++)
                    {
                        if (a[j] != b[j]) throw new InvalidOperationException("Grass placement must repeat for the same cell and seed.");
                        Vector3 p = a[j].GetColumn(3);
                        if (HillsideLayout.PathEdgeDistance(p.x, p.z) < -.001f)
                            throw new InvalidOperationException("Grass entered the clear walking path.");
                        float ground = terrain.SampleHeight(p) + terrain.transform.position.y;
                        if (Mathf.Abs(p.y + .025f - ground) > .001f) throw new InvalidOperationException("Grass does not meet the generated terrain.");
                        checkedClumps++;
                    }
                }
            }
            if (checkedClumps < 100) throw new InvalidOperationException("The three route regions need visible meadow coverage.");
            var distantKey = new Vector2Int(Mathf.FloorToInt(HillsideLayout.B.x / cellSize), Mathf.FloorToInt(HillsideLayout.B.z / cellSize));
            Cell near = BuildVerificationCell(distantKey, cellSize, HillsideLayout.B + Vector3.up * 14);
            Cell far = BuildVerificationCell(distantKey, cellSize, HillsideLayout.B + Vector3.up * 245);
            var nearPoints = new HashSet<Vector3>();
            foreach (Batch batch in near.batches) foreach (Matrix4x4 matrix in batch.matrices) nearPoints.Add(matrix.GetColumn(3));
            int distantClumps = 0;
            foreach (Batch batch in far.batches) foreach (Matrix4x4 matrix in batch.matrices)
            {
                if (!nearPoints.Contains(matrix.GetColumn(3))) throw new InvalidOperationException("Distant grass must retain near-field positions.");
                distantClumps++;
            }
            if (distantClumps == 0 || distantClumps >= nearPoints.Count) throw new InvalidOperationException("Distant grass needs sparse coverage without a hard distance cutoff.");
            int submergedClumps = 0;
            foreach (HillsideLayout.WaterBody water in HillsideLayout.Waters)
            {
                int shallow = 0, bed = 0;
                Vector3 view = new Vector3(water.Center.x, water.SurfaceY + 14, water.Center.y);
                for (int z = Mathf.FloorToInt((water.Center.y - water.Radii.y * 1.1f) / cellSize); z <= Mathf.FloorToInt((water.Center.y + water.Radii.y * 1.1f) / cellSize); z++)
                    for (int x = Mathf.FloorToInt((water.Center.x - water.Radii.x * 1.1f) / cellSize); x <= Mathf.FloorToInt((water.Center.x + water.Radii.x * 1.1f) / cellSize); x++)
                        foreach (Batch batch in BuildVerificationCell(new Vector2Int(x, z), cellSize, view).batches)
                            foreach (Matrix4x4 matrix in batch.matrices)
                            {
                                Vector3 p = matrix.GetColumn(3);
                                if (water.SignedDistance(p.x, p.z) > 0) continue;
                                float ground = terrain.SampleHeight(p) + terrain.transform.position.y;
                                float depth = water.SurfaceY - ground;
                                if (depth <= 0) continue;
                                if (Mathf.Abs(p.y + .025f - ground) > .001f) throw new InvalidOperationException("Submerged grass must meet the actual terrain bed.");
                                if (batch.variant >= 8 || (depth >= .45f && batch.variant >= 4)) throw new InvalidOperationException("Deep water needs short grass without submerged daisies.");
                                if (HillsideLayout.PathEdgeDistance(p.x, p.z) < -.001f) throw new InvalidOperationException("Submerged grass entered the clear walking path.");
                                if (depth < .3f) shallow++;
                                if (depth >= water.Depth * .65f) bed++;
                                submergedClumps++;
                            }
                if (shallow == 0 || bed == 0) throw new InvalidOperationException(water.Name + " needs grass in both shallows and the deeper bed.");
            }
            return "PASS: " + checkedClumps + " deterministic, grounded grass placements; clear paths; " + distantClumps +
                " distant clumps retain near-field positions; " + submergedClumps + " bed-grounded clumps across all three waters, with shallow and deep coverage.";
        }
#endif
    }
}
