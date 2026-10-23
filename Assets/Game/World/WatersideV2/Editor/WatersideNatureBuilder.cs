using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WeatherHouse.Hillside;
using WeatherHouse.Vegetation;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WatersideNatureBuilder
    {
        const string Meadow = "Assets/Experiments/Vegetation/Generated/";
        const string Pack = "Assets/HIGHLANDS - Stylized Environment/Prefabs/Forest/";
        public static void Build(Terrain terrain, Transform parent, string folder)
        {
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            Grass(terrain, parent, folder);
            Water(terrain, parent, folder);
            Trees(terrain, parent);
            var plants = WatersidePlantBuilder.Build(folder + "/Plants");
            Plants(terrain, parent, plants);
        }

        static void Grass(Terrain terrain, Transform parent, string folder)
        {
            var defaults = AssetDatabase.LoadAssetAtPath<GrassMeadowDefaults>(Meadow + "MeadowDefaults/Region0.asset");
            var grass = new GameObject("连续主草地 · 原调校与新图覆盖").AddComponent<HillsideGrassRenderer>();
            grass.transform.SetParent(parent);
            grass.terrain = terrain; grass.seed = defaults.seed;
            grass.speciesWeights = (float[])defaults.weights.Clone();
            grass.clumpsPerSquareMeter = defaults.totalClumps / (42f * 34f);
            grass.maximumSlope = 48;
            grass.variants = new Mesh[12]; grass.materials = new Material[3];
            string[] ids = { "ShortGrass", "TallGrass", "WhiteDaisy" };
            for (int i = 0; i < 3; i++)
            {
                var species = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GrassSpecies>(Meadow + ids[i] + ".asset"));
                JsonUtility.FromJsonOverwrite(defaults.plants[i], species);
                for (int v = 0; v < 4; v++) grass.variants[i * 4 + v] = Store(GrassMeshFactory.Build(species, v), folder + "/" + ids[i] + v + ".asset");
                var mat = new Material(AssetDatabase.LoadAssetAtPath<Material>(Meadow + ids[i] + ".mat"));
                mat.enableInstancing = true;
                mat.SetColor("_BaseColor", species.baseColor); mat.SetColor("_TipColor", species.tipColor);
                mat.SetFloat("_WindStrength", defaults.windStrength); mat.SetFloat("_WindSpeed", defaults.windSpeed);
                mat.SetFloat("_UpNormals", defaults.upNormals);
                grass.materials[i] = Store(mat, folder + "/" + ids[i] + ".mat");
                Object.DestroyImmediate(species);
            }
            const int size = 512;
            var map = new Texture2D(size, size, TextureFormat.RGBA32, false, true) { name = "新图草地与水深", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            Vector3 origin = terrain.transform.position, extent = terrain.terrainData.size;
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                {
                    float wx = origin.x + x / (size - 1f) * extent.x, wz = origin.z + z / (size - 1f) * extent.z;
                    float patch = Mathf.PerlinNoise(wx * .72f + 13, wz * .72f + 27);
                    float edge = WatersideLayout.PathEdgeDistance(wx, wz) + (patch - .5f) * .8f;
                    float wear = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.5f, 1.05f, edge));
                    float roadGrass = Mathf.Lerp(.08f, .58f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.28f, .72f, patch)));
                    float coverage = Mathf.Lerp(1, roadGrass, wear);
                    foreach (Vector3 p in WatersideLayout.Points.Values)
                    {
                        float clearing = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.4f, 3.6f, Vector2.Distance(new Vector2(wx, wz), new Vector2(p.x, p.z))));
                        coverage *= Mathf.Lerp(1, .24f + patch * .28f, clearing);
                        wear = Mathf.Max(wear, clearing * .7f);
                    }
                    float trodden = WatersideLayout.CourtyardWear(wx, wz);
                    coverage *= Mathf.Lerp(1, .5f, trodden);
                    wear = Mathf.Max(wear, trodden * .95f);
                    float depth = Mathf.Max(0, WatersideLayout.WaterHeight(wx, wz) - WatersideLayout.GroundHeight(terrain, wx, wz));
                    if (!WatersideLayout.IsWater(wx, wz)) depth = 0;
                    // Marine beds stay sandy; river shallows retain the original submerged meadow treatment.
                    if (depth > 2.4f || (depth > .6f && WatersideLayout.WaterHeight(wx, wz) < .05f)) coverage = 0;
                    pixels[z * size + x] = new Color(coverage, Mathf.Clamp01(depth / 8), wear, 1);
                }
            map.SetPixels(pixels); map.Apply();
            grass.placementMap = Store(map, folder + "/PlacementMap.asset");
        }

        static void Water(Terrain terrain, Transform parent, string folder)
        {
            var material = Store(new Material(AssetDatabase.LoadAssetAtPath<Material>(Meadow + "PainterlyWater.mat")), folder + "/Water.mat");
            // Dense submerged grass needs a softer foam band than the small meadow test pond.
            material.SetFloat("_ShoreStrength", .42f);
            material.SetFloat("_ShoreWidth", .11f);
            EditorUtility.SetDirty(material);
            // Partition the connected surface by river segment; each segment has its own reflection plane.
            const float step = 1;
            const int cells = 240;
            const float half = cells * step * .5f;
            int sections = 1 + WatersideLayout.WaterChannels.Sum(c => c.Nodes.Length - 1);
            var vertices = new List<Vector3>[sections]; var uv = new List<Vector2>[sections]; var triangles = new List<int>[sections];
            for (int i = 0; i < sections; i++) { vertices[i] = new List<Vector3>(); uv[i] = new List<Vector2>(); triangles[i] = new List<int>(); }
            for (int z = 0; z < cells; z++)
                for (int x = 0; x < cells; x++)
                {
                    float px = -half + x * step, pz = -half + z * step;
                    float[] dx = { 0, 0, step, step }; float[] dz = { 0, step, step, 0 };
                    bool wet = false;
                    for (int j = 0; j < 4; j++) wet |= WatersideLayout.WaterSignedDistance(px + dx[j], pz + dz[j]) < .5f;
                    if (!wet) continue;
                    int part = WatersideWaterSurface.Section(px + step * .5f, pz + step * .5f) + 1;
                    int first = vertices[part].Count;
                    for (int j = 0; j < 4; j++)
                    {
                        float wx = px + dx[j], wz = pz + dz[j];
                        vertices[part].Add(new Vector3(wx, WatersideLayout.WaterHeight(wx, wz), wz));
                        uv[part].Add(new Vector2((wx + half) / (half * 2), (wz + half) / (half * 2)));
                    }
                    triangles[part].AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
                }
            // Offshore extension: keep the authored shore grid and its UV scale, with a coarse sea-only apron.
            const float outerHalf = 600, outerStep = 20;
            int offshoreVertexStart = vertices[0].Count, offshoreIndexStart = triangles[0].Count;
            for (float pz = -outerHalf; pz < outerHalf; pz += outerStep)
                for (float px = -outerHalf; px < outerHalf; px += outerStep)
                {
                    if (px >= -half && px < half && pz >= -half && pz < half) continue;
                    var corners = new[] { new Vector2(px,pz), new Vector2(px,pz+outerStep),
                        new Vector2(px+outerStep,pz+outerStep), new Vector2(px+outerStep,pz) };
                    var outline = new List<Vector2>();
                    for (int edge = 0; edge < 4; edge++)
                    {
                        Vector2 a = corners[edge], b = corners[(edge+1)%4];
                        bool innerEdge = (a.x == b.x && Mathf.Abs(a.x) == half && Mathf.Abs(a.y) <= half && Mathf.Abs(b.y) <= half) ||
                            (a.y == b.y && Mathf.Abs(a.y) == half && Mathf.Abs(a.x) <= half && Mathf.Abs(b.x) <= half);
                        int subdivisions = innerEdge ? Mathf.RoundToInt(outerStep / step) : 1;
                        for (int j = 0; j < subdivisions; j++) outline.Add(Vector2.Lerp(a,b,j/(float)subdivisions));
                    }
                    var clipped = new List<Vector2>();
                    for (int edge = 0; edge < outline.Count; edge++)
                    {
                        Vector2 a = outline[edge], b = outline[(edge+1)%outline.Count];
                        bool wetA = WatersideLayout.SeaSignedDistance(a.x,a.y) <= 0;
                        bool wetB = WatersideLayout.SeaSignedDistance(b.x,b.y) <= 0;
                        if (wetA) clipped.Add(a);
                        if (wetA == wetB) continue;
                        // Beyond the heightmap the mountain boundary is extended; its signed distance magnitude
                        // is not distance to that extension, so locate the crossing by its sign, not interpolation.
                        Vector2 wet = wetA ? a : b, dry = wetA ? b : a;
                        for (int j = 0; j < 24; j++)
                        {
                            Vector2 midpoint = (wet+dry)*.5f;
                            if (WatersideLayout.SeaSignedDistance(midpoint.x,midpoint.y) <= 0) wet=midpoint;
                            else dry=midpoint;
                        }
                        clipped.Add(wet);
                    }
                    if (clipped.Count < 3) continue;
                    Vector2 center = Vector2.zero;
                    foreach (Vector2 p in clipped) center += p;
                    center /= clipped.Count;
                    int first = vertices[0].Count;
                    vertices[0].Add(new Vector3(center.x,WatersideLayout.SeaLevel,center.y));
                    uv[0].Add(new Vector2((center.x+half)/(half*2),(center.y+half)/(half*2)));
                    foreach (Vector2 p in clipped)
                    {
                        vertices[0].Add(new Vector3(p.x,WatersideLayout.SeaLevel,p.y));
                        uv[0].Add(new Vector2((p.x+half)/(half*2),(p.y+half)/(half*2)));
                    }
                    for (int j = 0; j < clipped.Count; j++)
                    {
                        Vector2 a = clipped[j]-center, b = clipped[(j+1)%clipped.Count]-center;
                        if (a.y*b.x-a.x*b.y <= .000001f) continue;
                        triangles[0].AddRange(new[] { first, first+j+1, first+(j+1)%clipped.Count+1 });
                    }
                }
            Debug.Log($"Waterside offshore sea: +{vertices[0].Count-offshoreVertexStart} vertices, +{(triangles[0].Count-offshoreIndexStart)/3} triangles; outer extent +/-{outerHalf} m.");
            var origins = new List<Vector3> { Vector3.zero }; var normals = new List<Vector3> { Vector3.up };
            foreach (var channel in WatersideLayout.WaterChannels)
                for (int i = 1; i < channel.Nodes.Length; i++)
                {
                    Vector3 a = channel.Nodes[i-1].Position, b = channel.Nodes[i].Position, delta = b-a;
                    float square = delta.x*delta.x + delta.z*delta.z;
                    origins.Add((a+b)*.5f); normals.Add(new Vector3(-delta.x*delta.y/square,1,-delta.z*delta.y/square).normalized);
                }
            for (int part = 0; part < sections; part++)
            {
                if (vertices[part].Count == 0) continue;
                var water = new GameObject(part == 0 ? "海湾" : "连通溪潭 " + part); water.transform.SetParent(parent); water.layer = 4;
                water.transform.SetPositionAndRotation(origins[part],Quaternion.FromToRotation(Vector3.up,normals[part]));
                for (int i=0;i<vertices[part].Count;i++) vertices[part][i] = water.transform.InverseTransformPoint(vertices[part][i]);
                var mesh = new Mesh { name = water.name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices[part]); mesh.SetUVs(0,uv[part]); mesh.SetTriangles(triangles[part],0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                water.AddComponent<MeshFilter>().sharedMesh = Store(mesh,folder + "/WaterSection" + part + ".asset");
                var renderer = water.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material; renderer.shadowCastingMode=ShadowCastingMode.Off;
                var reflection = water.AddComponent<PainterlyWaterReflection>(); reflection.resolution=1024;reflection.resolutionScale=.5f;
                var surface = water.AddComponent<WatersideWaterSurface>();surface.section=part-1;
                var interaction=water.AddComponent<HillsideWaterInteraction>();interaction.terrain=terrain;interaction.waterRenderer=renderer;interaction.surface=surface;
                interaction.simulationShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Experiments/Vegetation/Shaders/WaterInteractionNormals.shader");
            }
        }

        static void Trees(Terrain terrain, Transform parent)
        {
            var group = new GameObject("林冠围合 · 保留通路与水面视线"); group.transform.SetParent(parent);
            group.AddComponent<HillsideTreeOcclusion>();
            var foliage = AssetDatabase.LoadAssetAtPath<Material>(Meadow + "Backdrop_Tall_Fir_Branch.mat");
            var random = new System.Random(20261023);
            var placed = new List<Vector3>();
            Vector3[] courtyards={new Vector3(-34.5f,0,-30),new Vector3(1.8f,0,-7.2f),new Vector3(11,0,-12.6f),
                new Vector3(57.5f,0,-43),new Vector3(-21.6f,0,32.4f),new Vector3(0,0,32.4f),
                new Vector3(57.6f,0,-30.6f),new Vector3(57.3f,0,-3.5f),new Vector3(-9,0,-61.2f)};
            float[] viewYaws={45,20,90,70,90,270,5,10,225};
            for (int i = 0; i < 1800 && placed.Count < 145; i++)
            {
                Vector3 p = WatersideLayout.Design(R(random, 9, 85), R(random, 10, 88));
                if (WatersideLayout.IsWater(p.x, p.z, 2.6f) || WatersideLayout.PathEdgeDistance(p.x, p.z) < 3) continue;
                if (WatersideLayout.Points.Values.Any(point => Flat(p, point) < 7.5f)) continue;
                if (Flat(p, new Vector3(-41.5f, 0, -28)) < 6.7f || Flat(p, new Vector3(54.5f, 0, 1)) < 4.5f) continue;
                bool blocksView=false;
                for(int view=0;view<courtyards.Length;view++)
                {
                    Vector3 local=Quaternion.Euler(0,-viewYaws[view],0)*(p-courtyards[view]);
                    if(local.z>-15&&local.z<2.5f&&Mathf.Abs(local.x)<4.2f){blocksView=true;break;}
                }
                if(blocksView)continue;
                if (placed.Any(point => Flat(p, point) < 5.5f)) continue;
                p.y = WatersideLayout.GroundHeight(terrain, p.x, p.z);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "Fir_Tall_" + (random.Next(3) + 1) + ".prefab");
                var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                tree.transform.position = p; tree.transform.rotation = Quaternion.Euler(0, R(random, 0, 360), 0);
                tree.transform.localScale *= R(random, .65f, 1.1f);
                foreach (Renderer renderer in tree.GetComponentsInChildren<Renderer>())
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m && m.name.IndexOf("Branch", StringComparison.OrdinalIgnoreCase) >= 0 ? foliage : m).ToArray();
                placed.Add(p);
            }
        }

        static void Plants(Terrain terrain, Transform parent, Dictionary<string, GameObject[]> library)
        {
            var root = new GameObject("水岸群落 · 实体叶片与厚花穗"); root.transform.SetParent(parent);
            var random = new System.Random(70519);
            var broad = library.Where(k => k.Key.Contains("Fern") || k.Key.Contains("Leafy") || k.Key.Contains("Groundcover") || k.Key.Contains("Shrub") || k.Key.Contains("Ivy") || k.Key.StartsWith("G")).Select(k => k.Value).ToArray();
            var reeds = library.Where(k => k.Key.Contains("Plume") || k.Key.Contains("BrownSeed") || k.Key.Contains("Upright") || k.Key.Contains("Foxtail") || k.Key.Contains("LowSeed")).Select(k => k.Value).ToArray();
            var flowers = library.Where(k => k.Key.Contains("Flower") || k.Key.Contains("Daisy") || k.Key.Contains("Bell") || k.Key.Contains("Spike") || k.Key.Contains("Spray") || k.Key.Contains("Button")).Select(k => k.Value).ToArray();
            var lilies = library.Where(k => k.Key.IndexOf("Lily", StringComparison.OrdinalIgnoreCase) >= 0).Select(k => k.Value).ToArray();
            for (int i = 0; i < 5400; i++)
            {
                Vector3 p = WatersideLayout.Design(R(random, 12, 86), R(random, 15, 90));
                float shore = WatersideLayout.WaterSignedDistance(p.x, p.z);
                if (WatersideLayout.PathEdgeDistance(p.x, p.z) < .7f || WatersideLayout.Points.Values.Any(n => Flat(n, p) < 3.4f)) continue;
                float patch = Mathf.PerlinNoise(p.x * .12f + 70, p.z * .12f + 30);
                if (patch < .46f) continue;
                float ground = WatersideLayout.GroundHeight(terrain, p.x, p.z);
                float depth = WatersideLayout.WaterHeight(p.x, p.z) - ground;
                GameObject[][] choices;
                if (shore < -1.2f && depth > .12f && depth < 1.5f && lilies.Length > 0 && WatersideLayout.WaterHeight(p.x, p.z) > .1f)
                { if (random.NextDouble() > .16) continue; choices = lilies; p.y = WatersideLayout.WaterHeight(p.x, p.z) + .025f; }
                else if (shore > -1 && shore < 2.8f) { choices = reeds; p.y = ground - .02f; }
                else if (shore >= 2.8f && random.NextDouble() < .45) { choices = random.NextDouble() < .62 ? broad : flowers; p.y = ground - .02f; }
                else continue;
                if (choices.Length == 0) continue;
                var variants = choices[random.Next(choices.Length)];
                var go = (GameObject)PrefabUtility.InstantiatePrefab(variants[random.Next(variants.Length)], root.transform);
                go.transform.position = p; go.transform.rotation = Quaternion.Euler(0, R(random, 0, 360), 0);
                go.transform.localScale *= R(random, .72f, 1.25f);
                var lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(.018f, go.GetComponentsInChildren<Renderer>()) }); lod.RecalculateBounds();
            }
            var clustered=new List<Vector3>();
            foreach(Vector3 sample in WatersideLayout.ShoreSamples)
            {
                if(WatersideLayout.WaterHeight(sample.x,sample.z)<.15f||clustered.Any(p=>Flat(p,sample)<4)||
                    WatersideLayout.WalkablePathEdgeDistance(sample.x,sample.z)<1.2f||random.NextDouble()>.42)continue;
                clustered.Add(sample);
                for(int i=0;i<3;i++)
                {
                    Vector3 p=sample+new Vector3(R(random,-.6f,.6f),0,R(random,-.6f,.6f));
                    p.y=WatersideLayout.GroundHeight(terrain,p.x,p.z)-.025f;
                    if(WatersideLayout.WaterHeight(p.x,p.z)-p.y>.5f||WatersideLayout.WalkablePathEdgeDistance(p.x,p.z)<.7f)continue;
                    string id=i%2==0?"R01_Plume":"R02_BrownSeed";
                    var reed=(GameObject)PrefabUtility.InstantiatePrefab(library[id][random.Next(3)],root.transform);
                    reed.name="湿岸芦苇丛";reed.transform.position=p;reed.transform.rotation=Quaternion.Euler(0,R(random,0,360),0);
                    reed.transform.localScale*=R(random,.75f,1.05f);
                }
            }
        }

        public static T Store<T>(T value, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!existing) { AssetDatabase.CreateAsset(value, path); return value; }
            if (value is Mesh source && existing is Mesh target) GrassMeshFactory.UpdateStoredMesh(source, target);
            else EditorUtility.CopySerialized(value, existing);
            Object.DestroyImmediate(value); EditorUtility.SetDirty(existing); return existing;
        }
        static float R(System.Random random, float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}
