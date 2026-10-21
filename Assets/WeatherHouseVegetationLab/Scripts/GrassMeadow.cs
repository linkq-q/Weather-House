using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WeatherHouse.Vegetation
{
    public partial class GrassMeadow : MonoBehaviour
    {
        public GrassSpecies[] species = new GrassSpecies[3];
        public Material[] materials = new Material[3];
        [Tooltip("按草丛数量归一化；70/24/6 表示 70%、24%、6%，不是覆盖面积。")]
        public float[] weights = { 70, 24, 6 };
        [Range(0, 40000)] public int totalClumps = 28000;
        public int seed = 431;
        [HideInInspector] public Vector2 size = new Vector2(42, 34);
        [HideInInspector] public Vector2 pondCenter = new Vector2(6, 4);
        [HideInInspector] public float pondRadius = 9;
        [HideInInspector] public float waterLevel;
        [Range(0, .6f)] public float windStrength = .12f;
        [Range(0, 3)] public float windSpeed = .8f;
        [Tooltip("1：法线统一朝上，形成柔软色块；0：真实叶片法线，用于对照。")]
        [Range(0, 1)] public float upNormals = 1;
        [SerializeField, HideInInspector] int[] counts = new int[3];
        public int[] Counts { get { return (int[])counts.Clone(); } }
        readonly List<Mesh> runtimeMeshes = new List<Mesh>();
        readonly List<Material> runtimeMaterials = new List<Material>();
        GrassSpecies[] runtimeSpecies;

        void Awake()
        {
            if (!Application.isPlaying) return;
            if (species.Length != 3 || materials.Length != 3 || Array.Exists(species, s => !s) || Array.Exists(materials, m => !m))
            { Debug.LogError("草种与材质需要完整配置三个槽位。", this); enabled = false; return; }
            runtimeSpecies = new GrassSpecies[species.Length];
            for (int i = 0; i < species.Length; i++)
            {
                runtimeSpecies[i] = Instantiate(species[i]);
                species[i] = runtimeSpecies[i];
                var original = materials[i];
                materials[i] = Instantiate(original);
                runtimeMaterials.Add(materials[i]);
                foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
                    if (renderer.sharedMaterial == original) renderer.sharedMaterial = materials[i];
            }
            ApplyMaterials();
            ResetLiveTracking();
        }

        public float PondDistance(Vector2 p)
        {
            Vector2 delta = p - pondCenter;
            float angle = Mathf.Atan2(delta.y / .74f, delta.x);
            return new Vector2(delta.x, delta.y / .74f).magnitude / Mathf.Max(1, pondRadius)
                + .035f * Mathf.Sin(angle * 5) + .02f * Mathf.Cos(angle * 3);
        }

        public float GetGroundHeight(float x, float z)
        {
            return waterLevel + Mathf.Clamp((PondDistance(new Vector2(x, z)) - 1) * 5, -2, 2.8f)
                + .22f * Mathf.Sin(x * .22f) * Mathf.Sin(z * .19f);
        }

        public bool IsPlantable(Vector2 p)
        {
            return Mathf.Abs(p.x) < size.x * .5f - .4f && Mathf.Abs(p.y) < size.y * .5f - .4f
                && GetGroundHeight(p.x, p.y) > waterLevel + .22f;
        }

        public static int[] Allocate(int total, float[] values)
        {
            int[] result = new int[values.Length];
            double sum = 0;
            foreach (float value in values) if (!float.IsNaN(value) && !float.IsInfinity(value)) sum += Math.Max(0, value);
            if (sum <= 0 || total <= 0) return result;
            double[] remainder = new double[values.Length];
            int assigned = 0;
            for (int i = 0; i < values.Length; i++)
            {
                double weight = float.IsNaN(values[i]) || float.IsInfinity(values[i]) ? 0 : Math.Max(0, values[i]);
                double exact = total * weight / sum;
                result[i] = (int)Math.Floor(exact);
                remainder[i] = exact - result[i];
                assigned += result[i];
            }
            while (assigned < total)
            {
                int best = 0;
                for (int i = 1; i < remainder.Length; i++) if (remainder[i] > remainder[best]) best = i;
                result[best]++; remainder[best] = -1; assigned++;
            }
            return result;
        }

        public void ApplyMaterials()
        {
            for (int i = 0; i < materials.Length && i < species.Length; i++)
            {
                if (!materials[i] || !species[i]) continue;
                materials[i].SetColor("_BaseColor", species[i].baseColor);
                materials[i].SetColor("_TipColor", species[i].tipColor);
                materials[i].SetFloat("_WindStrength", windStrength);
                materials[i].SetFloat("_WindSpeed", windSpeed);
                materials[i].SetFloat("_UpNormals", upNormals);
            }
        }

        [ContextMenu("重新生成草地 / Rebuild Meadow")]
        public void Rebuild() { Rebuild(true); }

        public void Rebuild(bool saveMeshes)
        {
            CancelLiveBuild();
            if (species.Length != 3 || materials.Length != 3 || weights.Length != 3)
                throw new InvalidOperationException("草地需要短草、高草和雏菊三个槽位。");
            for (int i = 0; i < 3; i++)
                if (!species[i] || !materials[i]) throw new InvalidOperationException("草种与材质尚未配置。");
            totalClumps = Mathf.Clamp(totalClumps, 0, 40000);
            var requested = Allocate(totalClumps, weights);
            if (Array.Exists(requested, n => n > 0))
            {
                bool hasLand = false;
                for (int x = 0; x <= 20 && !hasLand; x++)
                    for (int z = 0; z <= 20 && !hasLand; z++)
                        hasLand = IsPlantable(new Vector2((x / 20f - .5f) * size.x, (z / 20f - .5f) * size.y));
                if (!hasLand) throw new InvalidOperationException("当前范围没有可种植陆地；已保留原草地。");
            }
#if UNITY_EDITOR
            string storageFolder = null;
            if (!Application.isPlaying && saveMeshes)
            {
                if (string.IsNullOrEmpty(gameObject.scene.path)) throw new InvalidOperationException("请先保存测试场景，再生成草地。");
                storageFolder = "Assets/WeatherHouseVegetationLab/Generated/MeadowMeshes/" + GlobalObjectId.GetGlobalObjectIdSlow(this);
                System.IO.Directory.CreateDirectory(storageFolder);
            }
#endif
            var old = transform.Find("Generated Grass");
            if (old) { old.name = "Retired Grass"; old.gameObject.SetActive(false); Dispose(old.gameObject); }
            foreach (Mesh mesh in runtimeMeshes) if (mesh) Dispose(mesh);
            runtimeMeshes.Clear();
            var root = new GameObject("Generated Grass").transform;
            root.SetParent(transform, false);
            counts = requested;
            ApplyMaterials();
            for (int kind = 0; kind < 3; kind++)
            {
                var meshes = new Mesh[4];
                for (int v = 0; v < 4; v++) meshes[v] = GrassMeshFactory.Build(species[kind], v);
                try
                {
                    int placed = 0, chunkIndex = 0;
                    while (placed < counts[kind])
                    {
                        int amount = Math.Min(128, counts[kind] - placed);
                        var random = PlacementRandom(seed, kind, chunkIndex);
                        var combine = new CombineInstance[amount];
                        for (int j = 0; j < amount; j++)
                        {
                            combine[j] = NextInstance(random, meshes);
                        }
                        var mesh = new Mesh { name = species[kind].name + "_" + chunkIndex, indexFormat = IndexFormat.UInt32 };
                        mesh.CombineMeshes(combine, true, true);
                        Bounds bounds = mesh.bounds; bounds.Expand(1.4f); mesh.bounds = bounds;
#if UNITY_EDITOR
                        if (!Application.isPlaying && saveMeshes)
                        {
                            string path = storageFolder + "/Species" + kind + "_" + chunkIndex + ".asset";
                            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                            if (existing) { EditorUtility.CopySerialized(mesh, existing); DestroyImmediate(mesh); mesh = existing; EditorUtility.SetDirty(mesh); }
                            else AssetDatabase.CreateAsset(mesh, path);
                        }
                        else
#endif
                            runtimeMeshes.Add(mesh);
                        var child = new GameObject(species[kind].name + " · " + chunkIndex, typeof(MeshFilter), typeof(MeshRenderer));
                        child.transform.SetParent(root, false);
                        child.GetComponent<MeshFilter>().sharedMesh = mesh;
                        var renderer = child.GetComponent<MeshRenderer>();
                        renderer.sharedMaterial = materials[kind];
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        renderer.receiveShadows = true;
                        placed += amount; chunkIndex++;
                    }
                }
                finally { foreach (Mesh mesh in meshes) Dispose(mesh); }
            }
#if UNITY_EDITOR
            if (!Application.isPlaying && saveMeshes) AssetDatabase.SaveAssets();
#endif
            ResetLiveTracking();
        }

        static System.Random PlacementRandom(int distributionSeed, int kind, int chunk)
        {
            // Independent chunks can update fairly while sliders keep changing, without replaying earlier placements.
            return new System.Random(unchecked(distributionSeed + kind * 92821 + chunk * 104729));
        }

        CombineInstance NextInstance(System.Random random, Mesh[] variants)
        {
            Vector2 point = Vector2.zero;
            bool found = false;
            for (int attempt = 0; attempt < 10000; attempt++)
            {
                point = new Vector2(((float)random.NextDouble() - .5f) * size.x,
                    ((float)random.NextDouble() - .5f) * size.y);
                if (IsPlantable(point)) { found = true; break; }
            }
            if (!found) throw new InvalidOperationException("当前范围没有足够的陆地区域可种草。");
            float scale = .8f + (float)random.NextDouble() * .4f;
            int variation = random.Next(4);
            float rotation = (float)random.NextDouble() * 360;
            return new CombineInstance {
                mesh = variants == null ? null : variants[variation],
                transform = Matrix4x4.TRS(new Vector3(point.x, GetGroundHeight(point.x, point.y) - .025f, point.y),
                    Quaternion.Euler(0, rotation, 0), Vector3.one * scale)
            };
        }

        static void Dispose(UnityEngine.Object obj)
        {
            if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }
        void OnDestroy()
        {
            CancelLiveBuild();
            foreach (var mesh in runtimeMeshes) if (mesh) Dispose(mesh);
            foreach (var material in runtimeMaterials) if (material) Dispose(material);
            if (runtimeSpecies != null) foreach (var item in runtimeSpecies) if (item) Dispose(item);
        }
    }
}
