using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WeatherHouse.Hillside;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WatersidePropsBuilder
    {
        const string Pack = "Assets/HIGHLANDS - Stylized Environment/";
        public const float LowLanternIntensity = .6f;
        static readonly Dictionary<Material, Material> Materials = new Dictionary<Material, Material>();
        static readonly List<Vector4> Occupied = new List<Vector4>();
        static Terrain ground;
        static string output;
        static Mesh timberMesh;
        static Material timber;
        static Mesh lanternHead;
        static Material[] lanternMaterials;
        static bool reuseMaterials;

        // Incremental dressing uses the approved materials without rebuilding any authored scene group.
        public static void PrepareAdditions(Terrain terrain)
        {
            ground = terrain; output = WatersideSceneBuilder.Output + "/Props/Props";
            Materials.Clear(); reuseMaterials = true;
        }

        public static GameObject CreateAddition(string asset, string label, Vector3 point, float height,
            float yaw, Transform parent, bool grounded = true)
        {
            var go = Instantiate(asset, label, height, yaw, parent);
            Bounds b = Bounds(go);
            go.transform.position += new Vector3(point.x-b.center.x,
                (grounded ? Ground(point)-.015f : point.y)-b.min.y, point.z-b.center.z);
            return go;
        }

        public static GameObject CreateFreeBridge(string filename, Transform parent, string outputFolder)
        {
            if (string.IsNullOrWhiteSpace(filename) || Path.GetFileName(filename) != filename ||
                !filename.StartsWith("EA03_Prop_Pier_", StringComparison.Ordinal) || !filename.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose an EA03_Prop_Pier_*.fbx filename from the audited EmaceBridge folder.", nameof(filename));
            const string source = "Assets/Game/World/WatersideV2/SourceAssets/EmaceBridge/";
            string folder = outputFolder + "/Props";
            Directory.CreateDirectory(folder);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Emace 作者原色表", enableInstancing = true };
            material.SetTexture("_BaseMap", Required<Texture2D>(source + "EA02_LPNP-Slavika_Colorsheet_v1.png"));
            material.SetColor("_BaseColor", Color.white); material.SetFloat("_Smoothness", .12f); material.SetFloat("_Metallic", 0);
            material = Store(material, folder + "/EmaceOriginalPalette.mat");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Required<GameObject>(source + filename), parent.gameObject.scene);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = Path.GetFileNameWithoutExtension(filename) + " · 原模型与色表";
            go.transform.SetParent(parent, false);
            foreach (MeshFilter filter in go.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.sharedMesh) continue;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer) renderer.sharedMaterials = Enumerable.Repeat(material, filter.sharedMesh.subMeshCount).ToArray();
                var collider = filter.GetComponent<MeshCollider>() ?? filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh; filter.gameObject.isStatic = true;
            }
            // Preserve author pivot, UVs and scale; the crossing builder chooses the span and deck alignment.
            return go;
        }

        public static void Build(Terrain terrain, Transform parent, string outputFolder)
        {
            ground = terrain;
            output = outputFolder + "/Props";
            Directory.CreateDirectory(output);
            Materials.Clear(); Occupied.Clear();
            reuseMaterials = false;
            lanternHead = null; lanternMaterials = null;
            timberMesh = MakeTimber();
            timber = Adapt(Required<Material>(Pack + "Materials/Witches House/Fences.mat"));
            Transform root = Group("七处水岸生活场景 · 现有手绘资产", parent);
            Vector3[] centers = { WatersideLayout.Entry, WatersideLayout.OldTree, WatersideLayout.LakeTerrace,
                WatersideLayout.RoundTerrace, WatersideLayout.Cape, WatersideLayout.Harbor, WatersideLayout.MoonBay };
            string[] names = { "归家石桥小院", "老树柴草院", "湖潭西岸休息处", "湖潭东岸小院", "海岬避雨棚", "港湾短码头", "月湾沙岸" };
            var groups = new Transform[centers.Length];
            for (int i = 0; i < groups.Length; i++) groups[i] = Group((i + 1) + " " + names[i], root);

            House(groups[0], centers[0]);
            OldTree(groups[1], centers[1]);
            Shelter(groups[4], centers[4]);
            Dock(groups[5], centers[5]);
            Rowboat(groups[5], new Vector3(60, 0, -46.3f), 90);
            Rowboat(groups[2], new Vector3(-15, 6, 31.3f), 90);
            Vector3[] dressing = { new Vector3(-35.7f,0,-30), new Vector3(-4,0,-5), new Vector3(-23,0,32.4f),
                new Vector3(0,0,32.4f), new Vector3(54.5f,0,1), new Vector3(53.5f,0,-43), new Vector3(-13,0,-61) };
            Vector2[] photoSpots = { new Vector2(-31.5f,-30), new Vector2(-34.5f,-30), new Vector2(1.8f,-7.2f), new Vector2(3,-5),
                new Vector2(-21.6f,32.4f), new Vector2(0,32.4f), new Vector2(58.5f,-6), new Vector2(57.3f,-3.5f), new Vector2(-9,-61.2f) };
            foreach (Vector2 p in photoSpots) Occupied.Add(new Vector4(p.x, p.y, 1.25f, 0));
            foreach (var frame in new[] { new Vector4(-1.8f,35.9f,0,1), new Vector4(-14.6f,-62.6f,-.7071068f,.7071068f) })
                foreach (float side in new[] { -1f, 1f })
                    Occupied.Add(new Vector4(frame.x + frame.z * 1.6f * side, frame.y + frame.w * 1.6f * side, .4f, 0));
            LowLantern(groups[2], new Vector3(-20,0,30.4f));
            LowLantern(groups[3], new Vector3(1.1f,0,34.6f));
            LowLantern(groups[6], new Vector3(-7,0,-62.6f));
            for (int i = 0; i < groups.Length; i++) Dress(groups[i], dressing[i], i);
            Validate(root);
            WritePlacementReport(groups);
            Debug.Log("Waterside props: " + string.Join(", ", groups.Select(g => g.name + "=" + g.childCount)) +
                "; 7 pole lights + 3 low lanterns; source assets preserved.");
        }

        static void Dress(Transform parent, Vector3 center, int index)
        {
            float turn = new[] { 0, -20, 90, -90, 0, 90, 45 }[index];
            if (index == 3 || index == 6)
                ShoreBench(parent, index == 3 ? new Vector3(1.1f,0,29.8f) : new Vector3(-13.2f,0,-59.4f), index == 3 ? 90 : 45);
            if (index != 4)
                Lamp(parent, index == 3 ? new Vector3(4,0,29.4f) : Offset(center, -1, -3.2f, turn), center);
            if (index == 4)
            {
                Place("Forest/Stump_1", "棚旁劈柴桩", new Vector3(57.2f, 0, -.5f), .65f, 30, parent);
                Wall(parent, new Vector3(52.1f, 0, -1.6f), 25, index);
                return;
            }
            Vector3 a = Offset(center, -2.9f, -2.2f, turn), b = Offset(center, 3.2f, 2.2f, turn);
            var barrel = Place("Camp/Barrel_" + (index % 3 + 1), "旧木桶", a, .95f, 17 + turn, parent);
            if (barrel) a = Bounds(barrel).center;
            var crate = Place("Camp Props/Box_1", "补给木箱", a + new Vector3(1.1f, 0, -.35f), .75f, -9 + turn, parent);
            if (crate) StackedCrate(crate.transform, turn + 13);
            Place("Camp Props/Bucket", "取水木桶", a + new Vector3(-.7f, 0, -.9f), .49f, 45 + turn, parent);
            Place("Camp/Box_3", "杂物箱", a + new Vector3(.8f, 0, 1), .6f, 12 + turn, parent);
            if (index == 2) Woodpile(parent, b, 90);
            else if (index != 3 && index != 6)
            {
                var table = Place("Camp/Table_" + (index % 2 + 1), "岸边小桌", b, .85f, turn + 20, parent);
                if (table)
                {
                    Tableware(table.transform, index); b = Bounds(table).center;
                    Vector3 chairPoint = b + Quaternion.Euler(0,turn+20,0) * new Vector3(0,0,-1.05f);
                    var chair = Place("Witches House/Chair", "木椅", chairPoint, 1.05f, turn + 20, parent, maximumShift: 1.5f);
                    if (chair) FacePoint(chair, b);
                }
            }
            Place("Forest/Stump_" + (index % 2 + 1), "旧树桩", Offset(center, 3, -1.5f, turn), .7f, turn, parent);
            var logs = Place("Forest/Log_" + (index % 2 + 1), "倒木坐凳", Offset(center, -3.6f, 2.2f, turn), .65f, turn + 70, parent);
            if (logs) Place("Camp Props/Axe", "劈柴斧", logs.transform.position + new Vector3(.9f, 0, .8f), .65f, turn + 90, parent);
            Woodpile(parent, Offset(center, -3.5f, .4f, turn), turn + 25);
            if (index == 1)
                Place("Camp/Dryer_" + (index % 2 + 1), "晾晒架", Offset(center, -.8f, 3.5f, turn), 2.1f, turn, parent);
            if (index != 5)
                Place("Witches House/Fence_" + (index % 3 + 1), "低矮短栏", Offset(center, 1, -4, turn), .95f, turn + 70, parent);
            Wall(parent, Offset(center, 3.8f, .2f, turn), turn + 100, index);
        }

        static void Lamp(Transform parent, Vector3 desired, Vector3 toward)
        {
            float yaw = Mathf.Atan2(toward.x - desired.x, toward.z - desired.z) * Mathf.Rad2Deg;
            var lamp = Place("Witches House/Lamp", "岸边暖灯", desired, 2.6f, yaw, parent, required: true);
            FacePoint(lamp, toward);
            var light = new GameObject("暖灯光 · 范围4 无阴影").AddComponent<Light>();
            light.transform.SetParent(lamp.transform, false);
            Bounds bounds = Bounds(lamp);
            light.transform.position = bounds.center + Vector3.up * bounds.extents.y * .35f;
            light.type = LightType.Point; light.range = 4; light.intensity = 1.1f;
            light.color = new Color(1, .77f, .43f); light.shadows = LightShadows.None;
        }

        static void LowLantern(Transform parent, Vector3 desired)
        {
            if (!lanternHead) ExtractLanternHead(parent);
            Vector3 site;
            if (!FindSite(desired, .5f, out site, .65f))
                throw new InvalidOperationException("No clear low lantern site near " + desired);
            foreach (LODGroup tree in Object.FindObjectsOfType<LODGroup>())
                if (tree.gameObject.scene == parent.gameObject.scene && tree.name.StartsWith("Fir_Tall_", StringComparison.Ordinal)
                    && FlatDistance(tree.transform.position, site) < 1.2f)
                    throw new InvalidOperationException("A fir trunk intersects the planned low lantern at " + site);
            Transform lamp = Group("圆台低位暖石灯 · 原灯头与风化石", parent); lamp.position = site;
            float top = site.y;
            for (int i = 0; i < 2; i++)
            {
                var stone = Instantiate("Forest/Stone_" + (i == 0 ? 1 : 3), "原石灯墩", 1, 0, lamp);
                Bounds b = Bounds(stone);
                Vector3 size = i == 0 ? new Vector3(.64f,.52f,.62f) : new Vector3(.7f,.17f,.68f);
                stone.transform.localScale = Vector3.Scale(stone.transform.localScale,
                    new Vector3(size.x / b.size.x, size.y / b.size.y, size.z / b.size.z));
                b = Bounds(stone);
                stone.transform.position += new Vector3(site.x - b.center.x, top - b.min.y, site.z - b.center.z);
                top += size.y - .025f;
            }
            var head = new GameObject("原 Lamp 骨骼灯笼头", typeof(MeshFilter), typeof(MeshRenderer));
            head.transform.SetParent(lamp, false); head.transform.position = new Vector3(site.x,top,site.z);
            head.transform.localScale = Vector3.one * (.66f / lanternHead.bounds.size.y);
            head.GetComponent<MeshFilter>().sharedMesh = lanternHead;
            head.GetComponent<MeshRenderer>().sharedMaterials = lanternMaterials; head.isStatic = true;
            var light = new GameObject("近人物暖光 · 范围4.5 无阴影").AddComponent<Light>();
            light.transform.SetParent(lamp, false); light.transform.position = new Vector3(site.x, top + .3f, site.z);
            light.type = LightType.Point; light.range = 4.5f; light.intensity = LowLanternIntensity;
            light.color = new Color(1,.72f,.36f); light.shadows = LightShadows.None;
            Occupied.Add(new Vector4(site.x,site.z,.5f,0));
        }

        static void ExtractLanternHead(Transform parent)
        {
            var source = Instantiate("Witches House/Lamp", "原灯头提取临时物件", 3.5815f, 0, parent);
            var baked = new Mesh();
            try
            {
                var skin = source.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r => r.sharedMesh.vertexCount).First();
                int bone = Array.FindIndex(skin.bones, t => t && t.name == "Bone_1");
                if (bone < 0) throw new InvalidOperationException("The source lantern head bone Bone_1 is missing.");
                BoneWeight[] weights = skin.sharedMesh.boneWeights;
                skin.BakeMesh(baked);
                Vector3[] sourceVertices = baked.vertices, sourceNormals = baked.normals; Vector2[] sourceUv = baked.uv;
                if (weights.Length != sourceVertices.Length) throw new InvalidOperationException("Lantern skin and baked mesh do not match.");
                Vector3[] worldVertices = sourceVertices.Select(v => skin.transform.TransformPoint(v)).ToArray();
                int glassPart = Array.FindIndex(skin.sharedMaterials,m => m.name.IndexOf("Glass",StringComparison.OrdinalIgnoreCase) >= 0);
                if (glassPart < 0) throw new InvalidOperationException("The source lantern glass submesh is missing.");
                int[] glassTriangles = baked.GetTriangles(glassPart);
                Bounds glassBounds = new Bounds(worldVertices[glassTriangles[0]],Vector3.zero);
                foreach (int vertex in glassTriangles) glassBounds.Encapsulate(worldVertices[vertex]);
                // Original Bone_1 also carries a hanging cord. Keep its roof/handle, omit the cord above it.
                float top = glassBounds.max.y + glassBounds.size.y * .5f;
                var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>();
                int[] remap = Enumerable.Repeat(-1,weights.Length).ToArray();
                for (int i = 0; i < weights.Length; i++)
                {
                    BoneWeight w = weights[i];
                    float weight = (w.boneIndex0 == bone ? w.weight0 : 0) + (w.boneIndex1 == bone ? w.weight1 : 0)
                        + (w.boneIndex2 == bone ? w.weight2 : 0) + (w.boneIndex3 == bone ? w.weight3 : 0);
                    if (weight < .99f || worldVertices[i].y > top) continue;
                    remap[i] = vertices.Count; vertices.Add(worldVertices[i]);
                    normals.Add(skin.transform.TransformDirection(sourceNormals[i]).normalized); uv.Add(sourceUv[i]);
                }
                var mesh = new Mesh { name = "HIGHLANDS Lamp 原灯笼头 · Bone_1", subMeshCount = baked.subMeshCount };
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0,uv);
                int triangleCount = 0;
                for (int sub = 0; sub < baked.subMeshCount; sub++)
                {
                    int[] original = baked.GetTriangles(sub); var triangles = new List<int>();
                    for (int i = 0; i < original.Length; i += 3)
                        if (remap[original[i]] >= 0 && remap[original[i+1]] >= 0 && remap[original[i+2]] >= 0)
                            triangles.AddRange(new[] { remap[original[i]],remap[original[i+1]],remap[original[i+2]] });
                    mesh.SetTriangles(triangles,sub); triangleCount += triangles.Count / 3;
                }
                if (triangleCount < 50) throw new InvalidOperationException("The original lantern head could not be isolated.");
                mesh.RecalculateBounds(); Bounds bounds = mesh.bounds;
                Vector3 origin = new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
                mesh.SetVertices(vertices.Select(v => v-origin).ToList()); mesh.RecalculateBounds(); mesh.RecalculateTangents();
                lanternHead = Store(mesh, output + "/OriginalLanternHead.asset");
                lanternMaterials = skin.sharedMaterials.Select(material =>
                {
                    if (material.name.IndexOf("Glass",StringComparison.OrdinalIgnoreCase) < 0) return material;
                    var glass = new Material(material) { name = "低位灯原玻璃 · 暖光" };
                    glass.SetColor("_BaseColor",new Color(1,.75f,.36f,.8f));
                    glass.SetColor("_EmissionColor",new Color(1,.55f,.18f) * 1.5f); glass.EnableKeyword("_EMISSION");
                    return Store(glass,output + "/LowLanternGlass.mat");
                }).ToArray();
            }
            finally { Object.DestroyImmediate(baked); Object.DestroyImmediate(source); }
        }

        static void House(Transform parent, Vector3 center)
        {
            // Includes the author's attached door, stairway and their original doorway collision.
            var house = Place("Witches House/House", "天气屋 · HIGHLANDS 原模型", new Vector3(-41.5f, 0, -28),
                6.8f, 180, parent, maximumShift: .5f, required: true);
            // The existing house has a mesh collider with its doorway shape; never add a solid box around it.
            Transform stairs = house.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "House_Stairs");
            Transform door = house.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "House_Door");
            if (!stairs || !door) throw new InvalidOperationException("The source house must retain its fitted stairway and door.");
            stairs.name = "原屋配套台阶"; door.name = "原屋配套门";
            Bounds b = Bounds(stairs.gameObject);
            if (b.size.x < .7f) throw new InvalidOperationException("The fitted house stairway is too narrow.");
            Vector3 approach = house.transform.forward;
            // Reserve the actual stair approach; a separate height-normalized stair was too small and misaligned.
            for (int i = 1; i <= 3; i++)
            {
                Vector3 clear = b.center + approach * (b.extents.z + i * .7f);
                Occupied.Add(new Vector4(clear.x, clear.z, .9f, 0));
            }
        }

        static void OldTree(Transform parent, Vector3 center)
        {
            var treeGroup = Group("主草地杉树地标与旧树桩", parent);
            var tree = Place("Forest/Fir_Tall_1", "老树 · 主杉树替代（非空心阔叶）", new Vector3(-6.6f, 0, -5),
                12.5f, 32, treeGroup, .85f, 2, true);
            tree.AddComponent<HillsideTreeOcclusion>();
            Vector3 p = tree.transform.position;
            Place("Forest/Stump_1", "老树根旁残桩", p + new Vector3(-1.1f, 0, -.9f), 1.1f, 25, treeGroup);
            Place("Forest/Branches_1", "树根旁枯枝", p + new Vector3(.8f, 0, 1.1f), .35f, 45, treeGroup);
        }

        static void Tableware(Transform parent, int index)
        {
            Bounds table = Bounds(parent.gameObject);
            var pot = Instantiate("Camp Props/" + (index % 2 == 0 ? "Jug" : "Pot"), "桌上陶器", .32f, index * 37, parent);
            Bounds p = Bounds(pot);
            pot.transform.position += new Vector3(table.center.x - p.center.x, table.max.y - p.min.y, table.center.z - p.center.z);
            foreach (Collider c in pot.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        }

        static void StackedCrate(Transform parent, float yaw)
        {
            Bounds lower = Bounds(parent.gameObject);
            var upper = Instantiate("Camp Props/Box_2", "斜放上层小箱", .43f, yaw, parent);
            Bounds bounds = Bounds(upper);
            upper.transform.position += new Vector3(lower.center.x - bounds.center.x + .06f,
                lower.max.y - bounds.min.y - .015f, lower.center.z - bounds.center.z + .04f);
        }

        static void Woodpile(Transform parent, Vector3 desired, float yaw)
        {
            Vector3 site;
            if (!FindSite(desired, 1.05f, out site)) return;
            var pile = Group("错落柴堆", parent); pile.position = site; pile.rotation = Quaternion.Euler(0, yaw, 0);
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < 3 - row; i++)
                {
                    var log = Instantiate("Camp/Log_" + ((i + row) % 3 + 1), "短柴", .3f, yaw, pile);
                    Bounds b = Bounds(log);
                    Vector3 p = pile.TransformPoint(new Vector3((i - 1 + row * .5f) * .38f, row * .25f, (i % 2) * .09f));
                    log.transform.position += new Vector3(p.x - b.center.x, p.y - b.min.y, p.z - b.center.z);
                }
            Occupied.Add(new Vector4(site.x, site.z, 1.05f, 0));
        }

        static void Wall(Transform parent, Vector3 desired, float yaw, int index)
        {
            Vector3 site;
            if (!FindSite(desired, 1.6f, out site)) return;
            var wall = Group("断续矮石墙与残柱", parent); wall.position = site; wall.rotation = Quaternion.Euler(0, yaw, 0);
            for (int i = 0; i < 4; i++)
            {
                var rock = Instantiate("Forest/Stone_" + ((i + index) % 4 + 1), "砌墙风化石", .48f + (i % 2) * .1f, yaw + i * 31, wall);
                Vector3 p = wall.TransformPoint(new Vector3((i - 1.5f) * .66f, 0, (i % 2) * .1f));
                p.y = Ground(p) - .06f;
                Bounds b = Bounds(rock);
                rock.transform.position += new Vector3(p.x - b.center.x, p.y - b.min.y, p.z - b.center.z);
                if (i == 0 || i == 1)
                {
                    var upper = Instantiate("Forest/Stone_3", "墙顶残石", .4f, yaw + 17, wall);
                    Bounds top = Bounds(upper); b = Bounds(rock);
                    upper.transform.position += new Vector3(p.x - top.center.x, b.max.y - top.min.y - .08f, p.z - top.center.z);
                }
            }
            Occupied.Add(new Vector4(site.x, site.z, 1.6f, 0));
        }

        static void Shelter(Transform parent, Vector3 center)
        {
            Vector3 site;
            if (!FindSite(new Vector3(54.5f, 0, 1), 2.65f, out site, .5f))
                throw new InvalidOperationException("Cape shelter's reviewed inland site is obstructed.");
            Transform shelter = Group("海岬开放木棚 · 切角板材", parent); shelter.position = site;
            const float halfX = 1.65f, halfZ = 1.35f;
            foreach (float x in new[] { -halfX, halfX })
                foreach (float z in new[] { -halfZ, halfZ })
                {
                    float top = z > 0 ? 2.85f : 2.4f;
                    float foot = Ground(shelter.TransformPoint(new Vector3(x, 0, z))) - site.y - .12f;
                    Beam("木柱", new Vector3(x, foot, z), new Vector3(x, top, z), .2f, .2f, shelter);
                    Beam("斜撑", new Vector3(x, top - .7f, z), new Vector3(x - Mathf.Sign(x) * .65f, top, z), .12f, .12f, shelter);
                }
            for (int i = 0; i < 9; i++)
            {
                float x = -1.9f + i * .47f;
                Beam("错边斜顶木板", new Vector3(x, 2.44f, -1.65f - (i % 3) * .04f),
                    new Vector3(x, 2.99f, 1.65f + (i % 2) * .04f), .45f, .12f, shelter);
            }
            Beam("后侧横梁", new Vector3(-halfX, 2.74f, halfZ), new Vector3(halfX, 2.74f, halfZ), .2f, .2f, shelter);
            Beam("前侧横梁", new Vector3(-halfX, 2.3f, -halfZ), new Vector3(halfX, 2.3f, -halfZ), .2f, .2f, shelter);
            Bench(shelter, new Vector3(0, 0, .9f));
            // These are fitted furnishings inside the reviewed shelter, outside its central 1.2 m entry aisle.
            FurnishShelter(shelter, "Camp/Barrel_1", "棚内雨水桶", new Vector3(-1.08f,0,-.45f), .82f, -12);
            var crate = FurnishShelter(shelter, "Camp Props/Box_1", "棚内补给箱", new Vector3(1.08f,0,-.48f), .58f, 7);
            StackedCrate(crate.transform, -8);
            FurnishShelter(shelter, "Camp Props/Bucket", "棚旁取水桶", new Vector3(-1.05f,0,.28f), .36f, 20);
            for (int i = 0; i < 3; i++)
                FurnishShelter(shelter, "Camp/Log_1", "棚内干柴", new Vector3(.92f + (i % 2) * .3f, 0, .3f + (i / 2) * .25f), .21f, 90);
            // Fit the lamp beside the shelter before reserving its coarse circular footprint.
            // Ground here is 0.39 m higher than the shelter floor, so Lamp still samples the terrain.
            Lamp(parent, new Vector3(52.1f,0,.3f), new Vector3(52.1f,0,-3));
            Place("Witches House/Fence_2", "棚侧短栏", new Vector3(57.2f, 0, 1.1f), .8f, 90, parent, maximumShift: .6f);
            Occupied.Add(new Vector4(site.x, site.z, 2.65f, 0));
        }

        static GameObject FurnishShelter(Transform shelter, string asset, string label, Vector3 local, float height, float yaw)
        {
            var go = Instantiate(asset, label, height, yaw, shelter);
            Bounds bounds = Bounds(go);
            Vector3 point = shelter.TransformPoint(local);
            go.transform.position += new Vector3(point.x - bounds.center.x, Ground(point) - bounds.min.y, point.z - bounds.center.z);
            return go;
        }

        static void Dock(Transform parent, Vector3 center)
        {
            Vector3 shore = center + new Vector3(6, 0, -1.6f);
            shore.y = Mathf.Max(.7f, Ground(shore) + .12f);
            Transform dock = Group("港湾短栈台 · 切角板材", parent); dock.position = shore;
            dock.rotation = Quaternion.Euler(0, 90, 0);
            for (int i = 0; i < 15; i++)
            {
                float z = i * .44f;
                Beam("错边栈板", new Vector3(-1.38f - (i % 3) * .025f, 0, z),
                    new Vector3(1.38f + (i % 2) * .04f, (i % 2) * .012f, z), .42f, .16f, dock);
            }
            foreach (float x in new[] { -1.3f, 1.3f })
            {
                Beam("承重纵梁", new Vector3(x, -.18f, -.2f), new Vector3(x, -.18f, 6.5f), .22f, .22f, dock);
                foreach (float z in new[] { .2f, 3.1f, 6.1f })
                {
                    Vector3 point = dock.TransformPoint(new Vector3(x, 0, z));
                    float bottom = Mathf.Min(-.5f, Ground(point) - shore.y - .3f);
                    Beam("栈台木桩", new Vector3(x, bottom, z), new Vector3(x, .72f, z), .22f, .22f, dock);
                }
                Beam("低侧扶手", new Vector3(x, .62f, 3.1f), new Vector3(x, .62f, 6.1f), .10f, .11f, dock);
            }
            // Leave both deck ends open. Only side rails and small end posts bound the walkway.
            for (int i = 0; i < 7; i++)
            {
                Vector3 p = dock.TransformPoint(new Vector3(0, 0, i));
                Occupied.Add(new Vector4(p.x, p.z, 1.55f, 0));
            }
        }

        static void Rowboat(Transform parent, Vector3 site, float yaw)
        {
            const string source = "Assets/Game/World/WatersideV2/SourceAssets/Boat/";
            float water = WatersideLayout.WaterHeight(site.x, site.z);
            if (!WatersideLayout.IsWater(site.x, site.z, -.9f) || water - Ground(site) < .35f ||
                WatersideLayout.CrossingAt(site.x, site.z, 2) != null)
                throw new InvalidOperationException("The rowboat requires clear water beside the shore: " + site);
            Transform boat = Group("靠岸木划艇 · Kenney CC0", parent);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(Required<GameObject>(source + "boat-row-small.fbx"), parent.gameObject.scene);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.transform.SetParent(boat, false);
            var hull = model.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r => r.name == "boat-row-small");
            if (!hull) throw new InvalidOperationException("Kenney boat-row-small hull mesh is missing.");
            model.transform.localScale *= 2.9f / hull.bounds.size.z;
            Bounds bounds = hull.bounds;
            model.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Kenney 原木舟色表", enableInstancing = true };
            material.SetTexture("_BaseMap", Required<Texture2D>(source + "Textures/colormap.png"));
            material.SetColor("_BaseColor", new Color(.82f,.82f,.78f));
            material.SetFloat("_Smoothness", .08f); material.SetFloat("_Metallic", 0); material.SetFloat("_Cull", 0);
            material = Store(material, output + "/KenneyBoatOriginalPalette.mat");
            foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>())
            {
                renderer.sharedMaterial = material; renderer.gameObject.isStatic = true;
            }
            foreach (Animator animator in model.GetComponentsInChildren<Animator>()) animator.enabled = false;
            hull.gameObject.AddComponent<MeshCollider>().sharedMesh = hull.GetComponent<MeshFilter>().sharedMesh;
            // The stream's surface slopes locally. Fit that plane, keeping water below the original inner floor.
            float dx = WatersideLayout.WaterHeight(site.x + .5f, site.z) - WatersideLayout.WaterHeight(site.x - .5f, site.z);
            float dz = WatersideLayout.WaterHeight(site.x, site.z + .5f) - WatersideLayout.WaterHeight(site.x, site.z - .5f);
            Vector3 forward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            forward.y = dx * forward.x + dz * forward.z;
            boat.rotation = Quaternion.LookRotation(forward, new Vector3(-dx, 1, -dz));
            boat.position = new Vector3(site.x, water - .18f, site.z);
            foreach (Vector3 local in new[] { Vector3.zero, new Vector3(0,0,1.4f), new Vector3(0,0,-1.4f),
                new Vector3(.8f,0,0), new Vector3(-.8f,0,0) })
            {
                Vector3 p = boat.TransformPoint(local);
                if (!WatersideLayout.IsWater(p.x,p.z) || WatersideLayout.WaterHeight(p.x,p.z) - Ground(p) < .22f)
                    throw new InvalidOperationException("A rowboat hull touches the bank or bottom at " + p);
            }
        }

        static void Bench(Transform parent, Vector3 p)
        {
            foreach (float z in new[] { -.18f, .18f })
                Beam("长凳座板", p + new Vector3(-1.2f, .52f, z), p + new Vector3(1.2f, .52f, z), .31f, .12f, parent);
            foreach (float x in new[] { -.85f, .85f })
                Beam("长凳腿", p + new Vector3(x, 0, 0), p + new Vector3(x, .5f, 0), .23f, .48f, parent);
        }

        static void ShoreBench(Transform parent, Vector3 desired, float yaw)
        {
            Vector3 site;
            if (!FindSite(desired, 1.3f, out site, 2.5f))
                throw new InvalidOperationException("No clear waterside bench site near " + desired);
            Transform bench = Group("临水休息长凳", parent); bench.position = site; bench.rotation = Quaternion.Euler(0,yaw,0);
            Bench(bench, Vector3.zero);
            Occupied.Add(new Vector4(site.x, site.z, 1.3f, 0));
        }

        static void FacePoint(GameObject go, Vector3 toward)
        {
            Bounds before = Bounds(go);
            Vector3 direction = toward - before.center; direction.y = 0;
            if (direction.sqrMagnitude < .01f) return;
            go.transform.rotation = Quaternion.LookRotation(direction);
            Bounds after = Bounds(go);
            go.transform.position += before.center - after.center;
        }

        static GameObject Place(string asset, string label, Vector3 desired, float height, float yaw, Transform parent,
            float footprint = 0, float maximumShift = 4, bool required = false)
        {
            var go = Instantiate(asset, label, height, yaw, parent);
            Bounds bounds = Bounds(go);
            float radius = footprint > 0 ? footprint : Mathf.Sqrt(bounds.extents.x * bounds.extents.x + bounds.extents.z * bounds.extents.z);
            Vector3 site;
            if (!FindSite(desired, radius, out site, maximumShift))
            {
                Object.DestroyImmediate(go);
                string message = "No clear waterside site within " + maximumShift + " m: " + label;
                if (required) throw new InvalidOperationException(message);
                Debug.LogWarning(message); return null;
            }
            go.transform.position += new Vector3(site.x - bounds.center.x, site.y - bounds.min.y - .015f, site.z - bounds.center.z);
            Occupied.Add(new Vector4(site.x, site.z, radius, 0));
            return go;
        }

        static GameObject Instantiate(string relative, string label, float height, float yaw, Transform parent)
        {
            GameObject prefab = Required<GameObject>(Pack + "Prefabs/" + relative + ".prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = label; go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(0, yaw, 0) * go.transform.rotation;
            Bounds b = Bounds(go); go.transform.localScale *= height / Mathf.Max(.01f, b.size.y);
            float widthLimit = relative.StartsWith("Forest/Log_", StringComparison.Ordinal) ? 3f :
                relative.StartsWith("Forest/Branches_", StringComparison.Ordinal) ? 1.5f : 0;
            if (widthLimit > 0)
            {
                Vector3 size = Bounds(go).size;
                go.transform.localScale *= Mathf.Min(1, widthLimit / Mathf.Max(size.x, size.z));
            }
            foreach (Animator a in go.GetComponentsInChildren<Animator>()) a.enabled = false;
            foreach (Rigidbody body in go.GetComponentsInChildren<Rigidbody>()) Object.DestroyImmediate(body);
            foreach (Light light in go.GetComponentsInChildren<Light>()) Object.DestroyImmediate(light);
            foreach (ParticleSystem fx in go.GetComponentsInChildren<ParticleSystem>()) fx.gameObject.SetActive(false);
            foreach (Renderer renderer in VisibleRenderers(go))
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(Adapt).ToArray();
                renderer.gameObject.isStatic = true;
            }
            if (go.GetComponentsInChildren<Collider>().Length == 0)
            {
                var lod = go.GetComponentInChildren<LODGroup>();
                IEnumerable<MeshFilter> filters = lod && lod.GetLODs().Length > 0
                    ? lod.GetLODs()[0].renderers.Select(r => r.GetComponent<MeshFilter>()).Where(f => f)
                    : go.GetComponentsInChildren<MeshFilter>();
                foreach (MeshFilter filter in filters)
                    if (filter.sharedMesh) filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            }
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>())
            {
                LOD[] levels = lod.GetLODs();
                if (levels.Length > 0) { levels[levels.Length - 1].screenRelativeTransitionHeight = .003f; lod.SetLODs(levels); }
            }
            return go;
        }

        static bool FindSite(Vector3 desired, float radius, out Vector3 site, float maximumShift = 4)
        {
            for (int i = 0; i < 160; i++)
            {
                float angle = i * 2.399963f, shift = maximumShift * Mathf.Sqrt(i / 159f);
                Vector3 p = desired + new Vector3(Mathf.Cos(angle) * shift, 0, Mathf.Sin(angle) * shift);
                if (Mathf.Abs(p.x) + radius > 87 || Mathf.Abs(p.z) + radius > 87) continue;
                if (WatersideLayout.WalkablePathEdgeDistance(p.x, p.z) < radius + .4f || WatersideLayout.IsWater(p.x, p.z, radius * .6f)) continue;
                if (WatersideLayout.Points.Values.Any(c => FlatDistance(c, p) < 1.2f + radius)) continue;
                if (Occupied.Any(o => Vector2.Distance(new Vector2(o.x, o.y), new Vector2(p.x, p.z)) < o.z + radius + .13f)) continue;
                p.y = Ground(p);
                float span = 0;
                foreach (Vector3 d in new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                    span = Mathf.Max(span, Mathf.Abs(Ground(p + d * radius) - p.y));
                if (span > Mathf.Max(.35f, radius * .35f)) continue;
                site = p; return true;
            }
            site = desired; return false;
        }

        static Material Adapt(Material source)
        {
            if (!source) throw new InvalidOperationException("A HIGHLANDS renderer has a missing material.");
            if (source.name == "Tall_Fir_Branch")
                return Required<Material>("Assets/Experiments/Vegetation/Generated/Backdrop_Tall_Fir_Branch.mat");
            Material found;
            if (Materials.TryGetValue(source, out found)) return found;
            string existingGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            if (reuseMaterials && existingGuid.Length >= 8)
            {
                found = AssetDatabase.LoadAssetAtPath<Material>(output + "/" + source.name + "_" + existingGuid.Substring(0,8) + ".mat");
                if (found) { Materials[source] = found; return found; }
            }
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = source.name, enableInstancing = true };
            Texture albedo = Texture(source, "_Albedo", "_BaseMap", "_MainTex");
            mat.SetTexture("_BaseMap", albedo);
            mat.SetColor("_BaseColor", source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
            Texture normal = Texture(source, "_Normal", "_BumpMap");
            if (normal) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
            mat.SetFloat("_BumpScale", source.HasProperty("_NormalScale") ? source.GetFloat("_NormalScale") : 1);
            mat.SetFloat("_Smoothness", .12f); mat.SetFloat("_Metallic", 0);
            if (source.name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mat.SetFloat("_Surface", 1); mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); mat.SetFloat("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.renderQueue = (int)RenderQueue.Transparent;
            }
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            found = Store(mat, output + "/" + source.name + "_" + guid.Substring(0, 8) + ".mat");
            Materials[source] = found; return found;
        }

        static Texture Texture(Material material, params string[] names)
        {
            foreach (string name in names) if (material.HasProperty(name) && material.GetTexture(name)) return material.GetTexture(name);
            return null;
        }

        static void Beam(string label, Vector3 from, Vector3 to, float width, float thickness, Transform parent)
        {
            var go = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            go.transform.SetParent(parent, false); go.transform.localPosition = (from + to) * .5f;
            Vector3 direction = to - from;
            go.transform.localRotation = Quaternion.LookRotation(direction, Mathf.Abs(direction.normalized.y) > .95f ? Vector3.forward : Vector3.up);
            go.transform.localScale = new Vector3(width, thickness, direction.magnitude);
            go.GetComponent<MeshFilter>().sharedMesh = timberMesh; go.GetComponent<MeshRenderer>().sharedMaterial = timber;
            go.GetComponent<MeshCollider>().sharedMesh = timberMesh; go.isStatic = true;
        }

        static Mesh MakeTimber()
        {
            // Eight-sided section and bevelled end rings keep thin planks readable without cube silhouettes.
            Vector2[] section = { new Vector2(-.42f,-.5f), new Vector2(.42f,-.5f), new Vector2(.5f,-.42f), new Vector2(.5f,.42f),
                new Vector2(.42f,.5f), new Vector2(-.42f,.5f), new Vector2(-.5f,.42f), new Vector2(-.5f,-.42f) };
            float[] zs = { -.5f, -.47f, .47f, .5f };
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            Action<Vector3, Vector3, Vector3> triangle = (a, b, c) =>
            {
                int first = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
                foreach (Vector3 p in new[] { a, b, c }) uv.Add(new Vector2(.865f + (p.x + .5f) * .07f, .16f + (p.z + .5f) * .63f));
                triangles.AddRange(new[] { first, first + 1, first + 2 });
            };
            Func<int, int, Vector3> point = (ring, side) =>
            {
                float scale = ring == 0 || ring == 3 ? .88f : 1;
                return new Vector3(section[side % 8].x * scale, section[side % 8].y * scale, zs[ring]);
            };
            for (int ring = 0; ring < 3; ring++)
                for (int side = 0; side < 8; side++)
                {
                    Vector3 a = point(ring, side), b = point(ring, side + 1), c = point(ring + 1, side), d = point(ring + 1, side + 1);
                    triangle(a, b, c); triangle(b, d, c);
                }
            for (int side = 0; side < 8; side++)
            {
                triangle(new Vector3(0, 0, -.5f), point(0, side + 1), point(0, side));
                triangle(new Vector3(0, 0, .5f), point(3, side), point(3, side + 1));
            }
            var mesh = new Mesh { name = "手工切角板材 · 原木纹图集" };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (mesh.vertexCount != 192) throw new InvalidOperationException("Bevelled timber topology changed unexpectedly.");
            return Store(mesh, output + "/BevelledTimber.asset");
        }

        static void Validate(Transform root)
        {
            if (root.childCount != 7) throw new InvalidOperationException("The seven waterside prop groups are required.");
            if (root.GetComponentsInChildren<Light>().Length != 10)
                throw new InvalidOperationException("Seven pole lamps and three nearby low lanterns are required.");
            foreach (Light light in root.GetComponentsInChildren<Light>())
                if (light.type != LightType.Point || (light.range != 4 && light.range != 4.5f) || light.shadows != LightShadows.None)
                    throw new InvalidOperationException("A waterside prop light exceeds the agreed budget.");
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
                if (filter.sharedMesh && !AssetDatabase.Contains(filter.sharedMesh))
                    throw new InvalidOperationException("A waterside prop mesh was not persisted.");
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (!renderer.sharedMesh || !AssetDatabase.Contains(renderer.sharedMesh))
                    throw new InvalidOperationException("An original skinned prop lost its source mesh.");
        }

        static void WritePlacementReport(Transform[] groups)
        {
            var report = new StringBuilder("group,name,center_x,bottom_y,center_z,size_x,size_y,size_z,walkable_edge_distance,renderers,colliders,yaw\n");
            foreach (Transform group in groups)
                foreach (Transform prop in group.Cast<Transform>().Concat(group.GetComponentsInChildren<Transform>()
                    .Where(t => t.name == "原屋配套台阶" || t.name == "原屋配套门" || t.name.StartsWith("棚内", StringComparison.Ordinal))))
                {
                    Renderer[] renderers = VisibleRenderers(prop.gameObject);
                    if (renderers.Length == 0) continue;
                    Bounds b = Bounds(prop.gameObject);
                    report.Append('"').Append(group.name.Replace("\"", "\"\"")).Append("\",\"")
                        .Append(prop.name.Replace("\"", "\"\"")).Append("\",");
                    report.AppendFormat(CultureInfo.InvariantCulture, "{0:F3},{1:F3},{2:F3},{3:F3},{4:F3},{5:F3},{6:F3},{7},{8},{9:F1}\n",
                        b.center.x, b.min.y, b.center.z, b.size.x, b.size.y, b.size.z,
                        WatersideLayout.WalkablePathEdgeDistance(b.center.x, b.center.z), renderers.Length,
                        prop.GetComponentsInChildren<Collider>().Length, prop.eulerAngles.y);
                }
            File.WriteAllText(output + "/placement-report.csv", report.ToString(), new UTF8Encoding(false));
        }

        static float Ground(Vector3 p) { return WatersideLayout.GroundHeight(ground, p.x, p.z); }
        static float FlatDistance(Vector3 a, Vector3 b) { return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z)); }
        static Vector3 Offset(Vector3 p, float x, float z, float yaw) { return p + Quaternion.Euler(0, yaw, 0) * new Vector3(x, 0, z); }
        static Transform Group(string name, Transform parent) { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
        static Bounds Bounds(GameObject go)
        {
            Renderer[] renderers = VisibleRenderers(go);
            if (renderers.Length == 0) throw new InvalidOperationException("No visible mesh in " + go.name);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
        static Renderer[] VisibleRenderers(GameObject go)
        { return go.GetComponentsInChildren<Renderer>().Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray(); }
        static T Required<T>(string path) where T : Object
        { T asset = AssetDatabase.LoadAssetAtPath<T>(path); if (!asset) throw new FileNotFoundException(path); return asset; }
        static T Store<T>(T created, string path) where T : Object
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!existing) { AssetDatabase.CreateAsset(created, path); return created; }
            EditorUtility.CopySerialized(created, existing); Object.DestroyImmediate(created); EditorUtility.SetDirty(existing); return existing;
        }
    }
}
