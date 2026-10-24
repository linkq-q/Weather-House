using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WeatherHouse.Hillside;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation
{
    // Scene-owned edits leave the shared meadow definitions, placement-map channels and source meshes intact.
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class GrassLocalPainter : MonoBehaviour, ISerializationCallbackReceiver
    {
        public HillsideGrassRenderer grass;
        public int seed = 73193;
        [SerializeField, HideInInspector] List<GrassPaintStroke> strokes = new List<GrassPaintStroke>();
        [SerializeField, HideInInspector] List<GrassPaintSource> sources = new List<GrassPaintSource>();
        public int StrokeCount => strokes.Count;
        public int VisibleOverlayCount { get; private set; }
        public int SubmittedOverlayDraws { get; private set; }
        public int CachedOverlayCells => cells.Count;

        sealed class Prototype
        {
            public Mesh[] meshes;
            public Material material;
            public GrassSpecies definition;
            public Material sourceMaterial;
            public bool owned, ownedMeshes;
        }
        sealed class Batch
        {
            public int sourceId, variant, count;
            public Matrix4x4[] matrices;
        }
        sealed class OverlayCell
        {
            public Vector2Int key;
            public Bounds bounds;
            public bool ready, dirty = true, building;
            public int sourceIndex, candidateIndex, attempts;
            public float densityFraction;
            public Vector3 buildView;
            public readonly List<int> sourceIds = new List<int>();
            public readonly List<Batch> batches = new List<Batch>(), pending = new List<Batch>();
            public readonly List<Matrix4x4>[] groups = { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
        }
        readonly Dictionary<Vector2Int, List<int>> index = new Dictionary<Vector2Int, List<int>>();
        readonly Dictionary<Vector2Int, OverlayCell> cells = new Dictionary<Vector2Int, OverlayCell>();
        readonly Dictionary<int, Prototype> prototypes = new Dictionary<int, Prototype>();
        readonly List<Vector2Int> visibleKeys = new List<Vector2Int>(), removedKeys = new List<Vector2Int>();
        readonly Plane[] frustum = new Plane[6];
        bool indexDirty = true, prototypesDirty;
        TerrainData cachedTerrainData;
        Vector3 cachedTerrainOrigin, cachedTerrainSize;
        Vector2 cachedOverlaySettings;
        int cachedSeed;
        float indexedCellSize;
        Rect previousBounds;
        bool hadBounds;
        int activeStroke = -1, budgetFrame = -1;
        long frameDeadline;

        void OnEnable()
        {
            if (!grass) grass = GetComponent<HillsideGrassRenderer>();
            if (grass) grass.localPainter = this;
            TerrainCallbacks.heightmapChanged += TerrainChanged;
            NotifyEdited();
        }
        void OnDisable()
        {
            TerrainCallbacks.heightmapChanged -= TerrainChanged;
            if (grass && grass.localPainter == this)
            {
                grass.localPainter = null;
                if (hadBounds) grass.InvalidateLocalRegion(previousBounds);
            }
            DisposePrototypes(); cells.Clear(); index.Clear(); indexDirty = true;
            VisibleOverlayCount = SubmittedOverlayDraws = 0; budgetFrame = -1;
        }
        void OnDestroy() { DisposePrototypes(); }
        // Serialized inspector/Undo bookkeeping can validate while a mouse drag is still recording.
        void OnValidate() { RefreshEdited(false); }
        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() { indexDirty = true; activeStroke = -1; }

        public GrassPaintSourceInfo[] GetSources()
        {
            var result = new GrassPaintSourceInfo[3 + sources.Count];
            string[] labels = { "原主草地 · 短草", "原主草地 · 高草", "原主草地 · 雏菊" };
            for (int i = 0; i < 3; i++) result[i] = new GrassPaintSourceInfo { id = i, label = labels[i], isBase = true,
                material = grass && grass.materials != null && i < grass.materials.Length ? grass.materials[i] : null };
            for (int i = 0; i < sources.Count; i++) result[i + 3] = new GrassPaintSourceInfo { id = i + 3,
                label = GrassMeadow.SpeciesLabel(sources[i].definition), definition = sources[i].definition, material = sources[i].material };
            return result;
        }

        public int AddSource(GrassSpecies definition, Material material, Mesh[] existingVariants = null)
        {
            if (!definition || !material) throw new ArgumentException("新增植物需要已有草种定义和材质。");
            for (int i = 0; i < sources.Count; i++)
                if (sources[i].definition == definition && sources[i].material == material && SameMeshes(sources[i].variants, existingVariants)) return i + 3;
            if (existingVariants != null && Array.Exists(existingVariants, mesh => !mesh))
                throw new ArgumentException("现有植物变体网格不能包含空引用。");
            sources.Add(new GrassPaintSource { definition = definition, material = material,
                variants = existingVariants == null ? null : (Mesh[])existingVariants.Clone() });
            return sources.Count + 2;
        }
        static bool SameMeshes(Mesh[] a, Mesh[] b)
        {
            int count = a == null ? 0 : a.Length;
            if (count != (b == null ? 0 : b.Length)) return false;
            for (int i = 0; i < count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        public int BeginStroke(GrassBrushSettings settings)
        {
            if (!grass || !grass.terrain) throw new InvalidOperationException("请先指定当前场景的主草地及 Terrain。");
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (activeStroke >= 0) throw new InvalidOperationException("请先结束当前笔触。");
            var copy = settings.Copy();
            if (copy.sourceId < -1 || copy.sourceId >= 3 + sources.Count || copy.mode != GrassPaintMode.Erase && copy.sourceId < 0)
                throw new ArgumentOutOfRangeException(nameof(settings), "铺草与局部调整需要选择一种植物。");
            strokes.Add(new GrassPaintStroke { settings = copy }); activeStroke = strokes.Count - 1;
            return activeStroke;
        }

        public bool AppendStrokePoint(int strokeId, Vector3 world)
        {
            RequireActive(strokeId);
            if (!IsFinite(world.x) || !IsFinite(world.z)) return false;
            Terrain terrain = grass.terrain; Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
            if (world.x < origin.x || world.x > origin.x + size.x || world.z < origin.z || world.z > origin.z + size.z) return false;
            var stroke = strokes[strokeId];
            if (!stroke.Append(new Vector2(world.x, world.z))) return false;
            Invalidate(stroke.bounds); return true;
        }

        public void EndStroke(int strokeId)
        {
            RequireActive(strokeId);
            if (strokes[strokeId].points.Count == 0) strokes.RemoveAt(strokeId);
            activeStroke = -1; EnsureIndex();
        }
        public void CancelStroke(int strokeId)
        {
            RequireActive(strokeId); Rect bounds = strokes[strokeId].bounds;
            strokes.RemoveAt(strokeId); activeStroke = -1; Invalidate(bounds);
        }
        public void BrushAt(Vector3 world, GrassBrushSettings settings)
        {
            int id = BeginStroke(settings);
            try { AppendStrokePoint(id, world); EndStroke(id); }
            catch { if (activeStroke == id) CancelStroke(id); throw; }
        }
        void RequireActive(int id)
        {
            if (id != activeStroke || id < 0 || id != strokes.Count - 1) throw new InvalidOperationException("此笔触已结束或撤销。");
        }
        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        // The Editor calls this after Undo/Redo. The previous bounds also restore areas removed by Undo.
        public void NotifyEdited()
        {
            RefreshEdited(true);
        }

        void RefreshEdited(bool endStroke)
        {
            if (endStroke) activeStroke = -1;
            prototypesDirty = true;
            Rect next = default; bool found = false;
            foreach (var stroke in strokes)
                if (stroke != null && stroke.points.Count > 0)
                { next = found ? GrassPaintStroke.Union(next, stroke.bounds) : stroke.bounds; found = true; }
            if (hadBounds || found) Invalidate(hadBounds && found ? GrassPaintStroke.Union(previousBounds, next) : hadBounds ? previousBounds : next);
            previousBounds = next; hadBounds = found; indexDirty = true;
        }

        void Invalidate(Rect bounds)
        {
            indexDirty = true;
            if (bounds.width <= 0 || bounds.height <= 0) return;
            previousBounds = hadBounds ? GrassPaintStroke.Union(previousBounds, bounds) : bounds; hadBounds = true;
            if (grass) grass.InvalidateLocalRegion(bounds);
            float edge = CellSize;
            foreach (var pair in cells)
                if (CellRect(pair.Key, edge).Overlaps(bounds)) { pair.Value.dirty = true; pair.Value.building = false; }
        }
        void TerrainChanged(Terrain changed, RectInt region, bool synced)
        {
            if (!grass || changed != grass.terrain) return;
            var data = changed.terrainData; Vector3 origin = changed.transform.position;
            float grid = data.heightmapResolution - 1;
            Invalidate(Rect.MinMaxRect(origin.x + (region.xMin - 1) / grid * data.size.x,
                origin.z + (region.yMin - 1) / grid * data.size.z,
                origin.x + (region.xMax + 1) / grid * data.size.x, origin.z + (region.yMax + 1) / grid * data.size.z));
        }
        float CellSize => grass ? Mathf.Max(4, grass.cellSize) : 16;
        static Rect CellRect(Vector2Int key, float edge) => new Rect(key.x * edge, key.y * edge, edge, edge);
        Vector2Int Key(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.y / CellSize));

        void EnsureIndex()
        {
            float edge = CellSize;
            if (!indexDirty && indexedCellSize == edge) return;
            if (indexedCellSize != edge) cells.Clear();
            index.Clear(); indexedCellSize = edge; indexDirty = false;
            for (int i = 0; i < strokes.Count; i++)
            {
                var stroke = strokes[i]; if (stroke == null || stroke.points.Count == 0) continue;
                for (int z = Mathf.FloorToInt(stroke.bounds.yMin / edge); z <= Mathf.FloorToInt(stroke.bounds.yMax / edge); z++)
                    for (int x = Mathf.FloorToInt(stroke.bounds.xMin / edge); x <= Mathf.FloorToInt(stroke.bounds.xMax / edge); x++)
                    {
                        var key = new Vector2Int(x, z);
                        if (!index.TryGetValue(key, out var list)) index.Add(key, list = new List<int>());
                        list.Add(i);
                    }
            }
            removedKeys.Clear();
            foreach (var pair in cells) if (!index.ContainsKey(pair.Key)) removedKeys.Add(pair.Key);
            foreach (var key in removedKeys) cells.Remove(key);
        }

        public GrassLocalSample SampleBase(Vector3 position, int species)
        {
            return Sample(new Vector2(position.x, position.z), species, true);
        }
        public GrassLocalSample SampleOverlay(Vector3 position, int sourceId)
        {
            return Sample(new Vector2(position.x, position.z), sourceId, false);
        }

        // Extra candidates are needed only in edited cells. The original candidate prefix never changes.
        internal float BaseDensityLimit(Vector2Int key)
        {
            EnsureIndex();
            if (index.TryGetValue(key, out var list))
                foreach (int i in list)
                {
                    var settings = strokes[i].settings;
                    if (settings.mode == GrassPaintMode.Adjust && settings.sourceId < 3 &&
                        settings.affectDensity && settings.density > 1 && settings.strength > 0) return 2;
                }
            return 1;
        }
        GrassLocalSample Sample(Vector2 point, int sourceId, bool isBase)
        {
            EnsureIndex(); var value = isBase ? GrassLocalSample.Base : GrassLocalSample.Overlay;
            if (index.TryGetValue(Key(point), out var list))
                foreach (int i in list) value.Apply(strokes[i], point, sourceId, isBase);
            return value;
        }

        Prototype GetPrototype(int id)
        {
            if (id < 0 || id >= 3 + sources.Count) return null;
            if (id < 3)
            {
                if (!grass || grass.variants == null || grass.variants.Length != 12 || grass.materials == null || grass.materials.Length != 3) return null;
                if (!prototypes.TryGetValue(id, out var baseItem)) prototypes[id] = baseItem = new Prototype { meshes = new Mesh[4] };
                for (int v = 0; v < 4; v++) baseItem.meshes[v] = grass.variants[id * 4 + v];
                baseItem.material = grass.materials[id]; return baseItem;
            }
            var source = sources[id - 3];
            if (!source.definition || !source.material) return null;
            bool useExisting = source.variants != null && source.variants.Length > 0;
            if (prototypes.TryGetValue(id, out var existing))
            {
                bool matchingMeshes = existing.ownedMeshes != useExisting;
                if (useExisting) for (int i = 0; i < 4; i++) matchingMeshes &= existing.meshes[i] == source.variants[i % source.variants.Length];
                if (existing.definition == source.definition && existing.sourceMaterial == source.material && matchingMeshes) return existing;
                Release(existing); prototypes.Remove(id);
            }
            var result = new Prototype { meshes = new Mesh[4], definition = source.definition, sourceMaterial = source.material, owned = true, ownedMeshes = !useExisting };
            try
            {
                for (int i = 0; i < 4; i++) result.meshes[i] = useExisting ? source.variants[i % source.variants.Length] : GrassMeshFactory.Build(source.definition, i);
                result.material = new Material(source.material) { name = source.material.name + " · Local grass", hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
                prototypes.Add(id, result); return result;
            }
            catch { Release(result); throw; }
        }

        public void DrawForCamera(Camera camera, bool populateImmediately = true)
        {
            VisibleOverlayCount = SubmittedOverlayDraws = 0;
            if (!isActiveAndEnabled || !grass || !grass.terrain || !camera || strokes.Count == 0) return;
            ValidateOverlayCache();
            EnsureIndex(); GeometryUtility.CalculateFrustumPlanes(camera, frustum);
            bool mayPopulate = camera.cameraType != CameraType.Reflection && (!Application.isPlaying || camera == (grass.viewCamera ? grass.viewCamera : Camera.main));
            bool immediate = populateImmediately && !Application.isPlaying;
            if (Application.isPlaying && mayPopulate && budgetFrame != Time.frameCount)
            {
                budgetFrame = Time.frameCount;
                frameDeadline = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(System.Diagnostics.Stopwatch.Frequency * .0015);
            }
            long deadline = !mayPopulate ? 0 : immediate ? long.MaxValue : Application.isPlaying ? frameDeadline :
                System.Diagnostics.Stopwatch.GetTimestamp() + (long)(System.Diagnostics.Stopwatch.Frequency * .004);
            visibleKeys.Clear();
            foreach (var pair in index)
                if (GeometryUtility.TestPlanesAABB(frustum, PreviewBounds(pair.Key))) visibleKeys.Add(pair.Key);
            visibleKeys.Sort((a, b) => PreviewBounds(a).SqrDistance(camera.transform.position).CompareTo(PreviewBounds(b).SqrDistance(camera.transform.position)));
            foreach (Vector2Int key in visibleKeys)
            {
                cells.TryGetValue(key, out var cell);
                bool hasBudget = mayPopulate && (immediate || System.Diagnostics.Stopwatch.GetTimestamp() < deadline);
                if (cell == null && hasBudget) { cell = new OverlayCell { key = key, bounds = PreviewBounds(key) }; cells.Add(key, cell); }
                if (cell == null) continue;
                float density = grass.DensityFraction(Vector3.Distance(camera.transform.position, cell.bounds.center));
                bool stale = cell.dirty || !cell.ready || density > cell.densityFraction * 1.2f || density < cell.densityFraction * .8f;
                if (hasBudget && (cell.building || stale))
                {
                    if (!cell.building || immediate && cell.buildView != camera.transform.position) BeginBuild(cell, camera.transform.position);
                    AdvanceBuild(cell, deadline);
                }
                if (!cell.ready) continue;
                foreach (Batch batch in cell.batches)
                {
                    if (batch.count == 0) continue;
                    // Reflection cameras consume only already prepared meshes and matrices.
                    if (!prototypes.TryGetValue(batch.sourceId, out var prototype) || !prototype.material || !prototype.meshes[batch.variant]) continue;
                    Graphics.DrawMeshInstanced(prototype.meshes[batch.variant], 0, prototype.material, batch.matrices, batch.count,
                        null, ShadowCastingMode.Off, true, gameObject.layer, camera, LightProbeUsage.Off);
                    VisibleOverlayCount += batch.count; SubmittedOverlayDraws++;
                }
            }
        }

        void ValidateOverlayCache()
        {
            var terrain = grass.terrain;
            var settings = new Vector2(CellSize, grass.visibleRadius);
            if (cachedTerrainData != terrain.terrainData || cachedTerrainOrigin != terrain.transform.position ||
                cachedTerrainSize != terrain.terrainData.size || cachedOverlaySettings != settings || cachedSeed != seed)
            {
                cells.Clear(); cachedTerrainData = terrain.terrainData; cachedTerrainOrigin = terrain.transform.position;
                cachedTerrainSize = terrain.terrainData.size; cachedOverlaySettings = settings; cachedSeed = seed;
            }
            if (prototypesDirty) { DisposePrototypes(); prototypesDirty = false; }
        }

        Bounds PreviewBounds(Vector2Int key)
        {
            var terrain = grass.terrain;
            return new Bounds(new Vector3((key.x + .5f) * CellSize, terrain.transform.position.y + terrain.terrainData.size.y * .5f + 4,
                (key.y + .5f) * CellSize), new Vector3(CellSize + 8, terrain.terrainData.size.y + 16, CellSize + 8));
        }
        void BeginBuild(OverlayCell cell, Vector3 view)
        {
            cell.sourceIds.Clear();
            foreach (int i in index[cell.key])
            {
                var stroke = strokes[i];
                if (stroke.settings.mode == GrassPaintMode.Paint && !cell.sourceIds.Contains(stroke.settings.sourceId)) cell.sourceIds.Add(stroke.settings.sourceId);
            }
            cell.sourceIds.Sort(); cell.sourceIndex = cell.candidateIndex = 0; cell.pending.Clear();
            foreach (var group in cell.groups) group.Clear();
            cell.buildView = view; cell.building = true;
            cell.attempts = Mathf.CeilToInt(40 * CellSize * CellSize * grass.DensityFraction(Mathf.Sqrt(PreviewBounds(cell.key).SqrDistance(view))));
        }
        void AdvanceBuild(OverlayCell cell, long deadline)
        {
            int work = 0;
            while (cell.sourceIndex < cell.sourceIds.Count)
            {
                int sourceId = cell.sourceIds[cell.sourceIndex]; Prototype prototype = GetPrototype(sourceId);
                if (prototype == null) { cell.sourceIndex++; cell.candidateIndex = 0; continue; }
                while (cell.candidateIndex < cell.attempts)
                {
                    if (++work % 16 == 0 && System.Diagnostics.Stopwatch.GetTimestamp() >= deadline) return;
                    int i = cell.candidateIndex++;
                    uint state = unchecked((uint)(seed ^ cell.key.x * 73856093 ^ cell.key.y * 19349663 ^ sourceId * 83492791 ^ i * 104729));
                    float x = (cell.key.x + Random01(ref state)) * CellSize, z = (cell.key.y + Random01(ref state)) * CellSize;
                    float yaw = Random01(ref state) * 360, randomScale = .8f + Random01(ref state) * .4f;
                    int variant = Mathf.Min(3, Mathf.FloorToInt(Random01(ref state) * 4));
                    float rank = (i + Random01(ref state)) / (40 * CellSize * CellSize);
                    var point = new Vector2(x, z); var sample = Sample(point, sourceId, false);
                    if (sample.density <= 0) continue;
                    var terrain = grass.terrain; Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
                    if (x < origin.x || z < origin.z || x >= origin.x + size.x || z >= origin.z + size.z) continue;
                    float height = terrain.SampleHeight(new Vector3(x, 0, z)) + origin.y - .025f;
                    Vector3 position = new Vector3(x, height, z);
                    float distance = Vector3.Distance(position, cell.buildView);
                    if (rank >= sample.density / 40 * grass.DensityFraction(distance)) continue;
                    float scale = randomScale * Mathf.Lerp(1, .65f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(75, 260, distance)));
                    cell.groups[variant].Add(Matrix4x4.TRS(position, Quaternion.Euler(0, yaw, 0),
                        new Vector3(scale * sample.widthScale, scale * sample.heightScale, scale * sample.widthScale)));
                }
                for (int variant = 0; variant < 4; variant++)
                {
                    var group = cell.groups[variant];
                    for (int start = 0; start < group.Count; start += 1023)
                    {
                        int amount = Mathf.Min(1023, group.Count - start); var matrices = new Matrix4x4[amount];
                        group.CopyTo(start, matrices, 0, amount);
                        cell.pending.Add(new Batch { sourceId = sourceId, variant = variant, matrices = matrices, count = amount });
                    }
                    group.Clear();
                }
                cell.sourceIndex++; cell.candidateIndex = 0;
            }
            cell.batches.Clear(); cell.batches.AddRange(cell.pending); cell.pending.Clear();
            cell.densityFraction = grass.DensityFraction(Vector3.Distance(cell.buildView, cell.bounds.center));
            cell.ready = true; cell.dirty = cell.building = false;
        }

        static float Random01(ref uint state)
        {
            state += 0x9e3779b9; uint value = state;
            value = (value ^ (value >> 16)) * 0x85ebca6b; value = (value ^ (value >> 13)) * 0xc2b2ae35;
            return ((value ^ (value >> 16)) & 0x00ffffff) / 16777216f;
        }
        void DisposePrototypes() { foreach (var item in prototypes.Values) Release(item); prototypes.Clear(); }
        static void Release(Prototype prototype)
        {
            if (!prototype.owned) return;
            if (prototype.ownedMeshes && prototype.meshes != null) foreach (var mesh in prototype.meshes) Dispose(mesh);
            Dispose(prototype.material);
        }
        static void Dispose(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
