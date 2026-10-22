using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ForestSpriteCharacter.Editor
{
    static class ForestSpriteClothBuilder
    {
        public static ForestSpriteCapeCloth Build(GameObject player, GameObject visual, ForestSpriteSecondaryMotion secondary)
        {
            var original = visual.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "Cape");
            var source = original.sharedMesh;
            var vertices = source.vertices;
            var weights = source.boneWeights;
            var matrices = original.bones.Select((b, i) => b.localToWorldMatrix * source.bindposes[i]).ToArray();
            var worldVertices = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var w = weights[i];
                worldVertices[i] = matrices[w.boneIndex0].MultiplyPoint3x4(vertices[i]) * w.weight0
                    + matrices[w.boneIndex1].MultiplyPoint3x4(vertices[i]) * w.weight1
                    + matrices[w.boneIndex2].MultiplyPoint3x4(vertices[i]) * w.weight2
                    + matrices[w.boneIndex3].MultiplyPoint3x4(vertices[i]) * w.weight3;
            }
            // Cloth is authored in metres at unit scale, independent of FBX unit conversion.
            var node = new GameObject("Cape Cloth");
            node.transform.SetParent(player.transform, false);
            var mesh = UnityEngine.Object.Instantiate(source);
            mesh.name = "CapeClothMetres";
            mesh.vertices = worldVertices.Select(p => node.transform.InverseTransformPoint(p)).ToArray();
            mesh.bindposes = original.bones.Select(b => b.worldToLocalMatrix * node.transform.localToWorldMatrix).ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            string path = ForestSpriteDemoBuilder.Root + "/Generated/CapeClothMetres.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) AssetDatabase.CreateAsset(mesh, path);
            else { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; }
            var skin = node.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = original.bones;
            skin.rootBone = node.transform;
            skin.sharedMaterials = original.sharedMaterials;
            skin.updateWhenOffscreen = true;
            skin.localBounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.one * 1.2f);
            original.enabled = false;
            WidenWingCape(mesh);
            var guides = BuildGuides(player.transform, skin, original.bones, weights);
            OpenWingFront(mesh, guides, skin);
            skin.localBounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.one * 1.2f);
            var cloth = node.AddComponent<Cloth>();
            cloth.useGravity = true;
            cloth.stretchingStiffness = .1f;
            cloth.bendingStiffness = .12f;
            cloth.damping = .14f;
            cloth.friction = .05f;
            cloth.worldVelocityScale = .2f;
            cloth.worldAccelerationScale = .15f;
            cloth.clothSolverFrequency = 480;
            cloth.useTethers = false;
            cloth.useVirtualParticles = 1f;
            cloth.enableContinuousCollision = true;
            cloth.externalAcceleration = Vector3.up * 7.1f;
            cloth.randomAcceleration = Vector3.zero;
            cloth.sleepThreshold = .001f;
            Vector3[] particles = cloth.vertices;
            Vector3[] normalizedVertices = mesh.vertices;
            var coefficients = cloth.coefficients;
            ForestSpriteDemoBuilder.Require(particles.Length == coefficients.Length && particles.Length > 100, "Cloth particles available");
            float top = particles.Max(p => p.y), bottom = particles.Min(p => p.y);
            var selfCollision = new List<uint>();
            var particleForVertex = new int[mesh.vertexCount];
            for (int i = 0; i < particles.Length; i++)
            {
                // Cloth welds UV duplicates; map by position rather than sharing mesh indices.
                int nearest = 0;
                float distance = float.MaxValue;
                for (int j = 0; j < mesh.vertexCount; j++)
                {
                    float d = (particles[i] - normalizedVertices[j]).sqrMagnitude;
                    if (d < distance) { distance = d; nearest = j; }
                    if (d < .00000001f) particleForVertex[j] = i;
                }
                ForestSpriteDemoBuilder.Require(distance < .000001f, "Cloth particle maps to normalized cape mesh: " + distance);
                float free = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.07f, .95f, (top - particles[i].y) / (top - bottom)));
                coefficients[i].maxDistance = .36f * free;
                coefficients[i].collisionSphereDistance = 0;
            }
            // The separate button island stays rigid. Only the narrow collar band anchors the fabric.
            var parent = Enumerable.Range(0, particles.Length).ToArray();
            var triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Union(particleForVertex[triangles[i]], particleForVertex[triangles[i + 1]]);
                Union(particleForVertex[triangles[i]], particleForVertex[triangles[i + 2]]);
            }
            var componentSizes = Enumerable.Range(0, particles.Length).GroupBy(Find).ToDictionary(g => g.Key, g => g.Count());
            for (int i = 0; i < particles.Length; i++)
            {
                if (componentSizes[Find(i)] < 60) coefficients[i].maxDistance = 0;
                if (coefficients[i].maxDistance > .02f) selfCollision.Add((uint)i);
            }
            cloth.coefficients = coefficients;
            float minimumSpacing = float.MaxValue;
            for (int i = 0; i < selfCollision.Count; i++)
                for (int j = i + 1; j < selfCollision.Count; j++)
                    minimumSpacing = Mathf.Min(minimumSpacing, Vector3.Distance(particles[selfCollision[i]], particles[selfCollision[j]]));
            cloth.selfCollisionDistance = Mathf.Min(.006f, minimumSpacing * .7f);
            cloth.selfCollisionStiffness = .4f;
            cloth.SetSelfAndInterCollisionIndices(selfCollision);
            // Small explicit body volumes, attached to animated bones; no hard outward plane.
            var bones = visual.GetComponentsInChildren<Transform>().ToDictionary(t => t.name);
            var upper = Sphere("Cape chest collision", bones["Chest"], new Vector3(0, .588f, .0065f), .132f);
            var lower = Sphere("Cape hips collision", bones["Hips"], new Vector3(0, .34f, -.02f), .249f);
            var bodyPairs = new List<ClothSphereColliderPair> { new ClothSphereColliderPair(upper, lower) };
            foreach (string side in new[] { "L", "R" })
            {
                // Names and cross sections were measured from the shipped FBX, not inferred from humanoid conventions.
                foreach (string name in new[] { "UpperArm." + side, "Forearm." + side, "Hand." + side })
                    ForestSpriteDemoBuilder.Require(bones.ContainsKey(name), "Cape arm collider bone: " + name);
                Transform shoulderBone = bones["UpperArm." + side], elbowBone = bones["Forearm." + side], wristBone = bones["Hand." + side];
                Vector3 shoulder = player.transform.InverseTransformPoint(shoulderBone.position);
                Vector3 elbow = player.transform.InverseTransformPoint(elbowBone.position);
                Vector3 wrist = player.transform.InverseTransformPoint(wristBone.position);
                // Start below the shoulder: the .092 m upper radius leaves the fixed collar clear in the fitted rest pose.
                var armUpper = Sphere("Cape arm " + side + " upper", shoulderBone, Vector3.Lerp(shoulder, elbow, .2f), .092f);
                // A separate middle sphere covers the wider upper-arm surface without enlarging the collar-end sphere.
                var armMiddle = Sphere("Cape arm " + side + " middle", shoulderBone, Vector3.Lerp(shoulder, elbow, .5f), .100f);
                var armElbow = Sphere("Cape arm " + side + " elbow", elbowBone, elbow, .065f);
                var armWrist = Sphere("Cape arm " + side + " wrist", wristBone, wrist, .063f);
                bodyPairs.Add(new ClothSphereColliderPair(armUpper, armMiddle));
                bodyPairs.Add(new ClothSphereColliderPair(armMiddle, armElbow));
                bodyPairs.Add(new ClothSphereColliderPair(armElbow, armWrist));
            }
            cloth.sphereColliders = bodyPairs.ToArray();
            var driver = node.AddComponent<ForestSpriteCapeCloth>();
            driver.cloth = cloth;
            driver.simulationRoot = player.transform;
            driver.secondaryMotion = secondary;
            driver.restParticles = particles;
            driver.freedomWeights = coefficients.Select(c => c.maxDistance / .36f).ToArray();
            driver.guideFrame = guides.frame;
            driver.guideNodes = guides.nodes;
            driver.guideRestPoints = guides.restPoints;
            driver.guideSegments = 6;
            driver.guideEdgeVertices = guides.edgeVertices;
            driver.guideEdgeRestLengths = guides.edgeLengths;
            string[] directions = { "Back", "Left", "Right", "Front" };
            driver.flowUpper = directions.Select(d => bones["Cape_" + d + "_01"]).ToArray();
            driver.flowLower = directions.Select(d => bones["Cape_" + d + "_02"]).ToArray();
            driver.flowAxes = driver.flowUpper.Select(b => player.transform.InverseTransformDirection(
                Vector3.Cross(Vector3.down, Vector3.ProjectOnPlane(b.position - bones["Chest"].position, Vector3.up).normalized).normalized)).ToArray();
            // Light outward support keeps the closed front panel outside the torso collision volume.
            driver.flowInfluence = new[] { 1f, .65f, .65f, .12f };
            driver.ApplySettings();
            return driver;

            int Find(int index) { while (parent[index] != index) { parent[index] = parent[parent[index]]; index = parent[index]; } return index; }
            void Union(int a, int b) { parent[Find(b)] = Find(a); }
            SphereCollider Sphere(string name, Transform bone, Vector3 playerLocal, float radius)
            {
                var obj = new GameObject(name);
                obj.transform.position = player.transform.TransformPoint(playerLocal);
                obj.transform.SetParent(bone, true);
                var sphere = obj.AddComponent<SphereCollider>();
                sphere.radius = radius / Mathf.Abs(obj.transform.lossyScale.x);
                sphere.isTrigger = true;
                return sphere;
            }
        }

        sealed class GuideRig
        {
            public Transform frame;
            public Transform[] nodes;
            public Vector3[] restPoints;
            public int[] edgeVertices;
            public float[] edgeLengths;
        }

        static GuideRig BuildGuides(Transform player, SkinnedMeshRenderer skin, Transform[] originalBones, BoneWeight[] originalWeights)
        {
            const int segments = 6, pointsPerGuide = segments + 1;
            string[] directions = { "Back", "Left", "Right", "Front" };
            var mesh = skin.sharedMesh;
            var vertices = mesh.vertices;
            var points = new List<Vector3>();
            var representatives = new List<int>();
            var welded = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                int nearest = points.FindIndex(p => (p - vertices[i]).sqrMagnitude < 1e-10f);
                if (nearest < 0) { nearest = points.Count; points.Add(vertices[i]); representatives.Add(i); }
                welded[i] = nearest;
            }
            var adjacency = points.Select(p => new List<int>()).ToArray();
            var edgeCounts = new Dictionary<long, int>();
            int[] triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = welded[triangles[i + e]], b = welded[triangles[i + (e + 1) % 3]];
                    if (a == b) continue;
                    long key = Edge(a, b);
                    if (!edgeCounts.ContainsKey(key))
                    {
                        adjacency[a].Add(b); adjacency[b].Add(a); edgeCounts[key] = 0;
                    }
                    edgeCounts[key]++;
                }
            var component = Enumerable.Repeat(-1, points.Count).ToArray();
            var groups = new List<List<int>>();
            for (int i = 0; i < points.Count; i++)
            {
                if (component[i] >= 0) continue;
                var members = new List<int>(); var queue = new Queue<int>(); queue.Enqueue(i); component[i] = groups.Count;
                while (queue.Count > 0)
                {
                    int a = queue.Dequeue(); members.Add(a);
                    foreach (int b in adjacency[a]) if (component[b] < 0) { component[b] = groups.Count; queue.Enqueue(b); }
                }
                groups.Add(members);
            }
            var main = groups.OrderByDescending(g => g.Count).First();
            int mainId = component[main[0]];
            var boundary = points.Select(p => new List<int>()).ToArray();
            foreach (var entry in edgeCounts)
            {
                int a = (int)(entry.Key >> 32), b = (int)entry.Key;
                ForestSpriteDemoBuilder.Require(entry.Value <= 2, "Cape guide source is a manifold sheet");
                if (entry.Value == 1 && component[a] == mainId) { boundary[a].Add(b); boundary[b].Add(a); }
            }
            var seen = new HashSet<int>(); var loops = new List<List<int>>();
            foreach (int vertex in main)
            {
                if (boundary[vertex].Count == 0 || !seen.Add(vertex)) continue;
                var loop = new List<int>(); var queue = new Queue<int>(); queue.Enqueue(vertex);
                while (queue.Count > 0)
                {
                    int a = queue.Dequeue(); loop.Add(a);
                    foreach (int b in boundary[a]) if (seen.Add(b)) queue.Enqueue(b);
                }
                loops.Add(loop);
            }
            ForestSpriteDemoBuilder.Require(loops.Count == 2, "Cape guides use the collar and hem boundary loops");
            var collar = loops.OrderByDescending(g => g.Average(i => points[i].y)).First();
            var hem = loops.Single(g => g != collar);
            float[] fromCollar = Distances(collar, out int[] predecessor);
            float[] fromHem = Distances(hem, out _);
            var chest = originalBones.Single(b => b.name == "Chest");
            Vector3 scale = chest.lossyScale;
            ForestSpriteDemoBuilder.Require(Mathf.Abs(scale.x - scale.y) < .0001f && Mathf.Abs(scale.x - scale.z) < .0001f,
                "Guide frame requires the existing uniform character scale");
            var frame = new GameObject("Cape Guide Frame").transform;
            frame.SetPositionAndRotation(player.position, player.rotation);
            frame.SetParent(chest, true);
            var guidePoints = new Vector3[4 * pointsPerGuide];
            var nodes = new Transform[guidePoints.Length];
            Vector3 centre = player.InverseTransformPoint(chest.position);
            // FBX Left is +X here. Names cannot be used to infer physical side.
            var radial = directions.Select(d => Vector3.ProjectOnPlane(player.InverseTransformPoint(
                originalBones.Single(b => b.name == "Cape_" + d + "_01").position) - centre, Vector3.up).normalized).ToArray();
            ForestSpriteDemoBuilder.Require(radial.All(v => v.sqrMagnitude > .99f) && radial[1].x * radial[2].x < -.5f,
                "Cape directional guides follow the actual FBX bone sides");
            for (int d = 0; d < 4; d++)
            {
                int end = hem.OrderByDescending(i => Vector3.Dot(points[i] - centre, radial[d])).First();
                var path = new List<Vector3>();
                for (int index = end; index >= 0; index = predecessor[index]) path.Add(points[index]);
                path.Reverse();
                ForestSpriteDemoBuilder.Require(path.Count >= 3, "Nonempty cape guide surface path " + directions[d]);
                // Remove triangle-edge zigzags before equal-arc sampling; this never changes the mesh vertices.
                for (int pass = 0; pass < 2; pass++)
                {
                    var previous = path.ToArray();
                    for (int j = 1; j < path.Count - 1; j++) path[j] = previous[j - 1] * .25f + previous[j] * .5f + previous[j + 1] * .25f;
                }
                float[] lengths = new float[path.Count];
                for (int j = 1; j < lengths.Length; j++) lengths[j] = lengths[j - 1] + Vector3.Distance(path[j - 1], path[j]);
                for (int j = 0; j <= segments; j++)
                {
                    float distance = lengths[lengths.Length - 1] * j / segments;
                    int k = 1; while (k < lengths.Length - 1 && lengths[k] < distance) k++;
                    Vector3 point = Vector3.Lerp(path[k - 1], path[k], Mathf.InverseLerp(lengths[k - 1], lengths[k], distance));
                    int index = d * pointsPerGuide + j;
                    guidePoints[index] = point;
                    var bone = new GameObject("Cape_" + directions[d] + "_Guide_" + j.ToString("00")).transform;
                    bone.SetParent(frame, false); bone.localPosition = point;
                    nodes[index] = bone;
                    if (j > 0) ForestSpriteDemoBuilder.Require(Vector3.Distance(point, guidePoints[index - 1]) > .001f,
                        "Positive cape guide segment length " + directions[d] + " " + j);
                }
            }
            var allBones = new[] { chest }.Concat(nodes).ToArray();
            var newWeights = new BoneWeight[vertices.Length];
            var particleWeights = new BoneWeight[points.Count];
            float top = points.Max(p => p.y), bottom = points.Min(p => p.y);
            var longitudinal = main.ToDictionary(i => i, i => fromCollar[i] / (fromCollar[i] + fromHem[i]));
            float collarEnd = main.Where(i => (top - points[i].y) / (top - bottom) <= .07f).Max(i => longitudinal[i]);
            ForestSpriteDemoBuilder.Require(collarEnd < .95f, "Cape guide has a free arc below the collar");
            int[] sectorForBone = originalBones.Select(b => Array.FindIndex(directions,
                d => b.name.StartsWith("Cape_" + d + "_", StringComparison.Ordinal))).ToArray();
            for (int i = 0; i < points.Count; i++)
            {
                float depth = (top - points[i].y) / (top - bottom);
                float freedom = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.07f, .95f, depth));
                // This is the dimensionless freedom mask, not a one-millimetre motion radius.
                if (component[i] != mainId || freedom <= .001f)
                {
                    particleWeights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 }; continue;
                }
                // Material weights and guide nodes share the original collar-to-hem arc coordinate.
                float u = longitudinal[i];
                int lower = Mathf.Min(segments - 1, Mathf.FloorToInt(u * segments));
                float t = Mathf.Clamp01(u * segments - lower);
                var sectorWeights = new float[4]; var old = originalWeights[representatives[i]];
                Accumulate(old.boneIndex0, old.weight0); Accumulate(old.boneIndex1, old.weight1);
                Accumulate(old.boneIndex2, old.weight2); Accumulate(old.boneIndex3, old.weight3);
                if (sectorWeights.Sum() < .00001f)
                {
                    // Angular fallback only for original all-Chest vertices; use cyclic order, not directions[] order.
                    float tau = Mathf.PI * 2f;
                    var angles = radial.Select(v => Mathf.Repeat(Mathf.Atan2(v.x, -v.z), tau)).ToArray();
                    int[] ring = Enumerable.Range(0, 4).OrderBy(d => angles[d]).ToArray();
                    float angle = Mathf.Repeat(Mathf.Atan2(points[i].x - centre.x, centre.z - points[i].z), tau);
                    for (int sector = 0; sector < 4; sector++)
                    {
                        int sectorA = ring[sector], sectorB = ring[(sector + 1) % 4];
                        float span = Mathf.Repeat(angles[sectorB] - angles[sectorA], tau), offset = Mathf.Repeat(angle - angles[sectorA], tau);
                        if (offset > span + .00001f) continue;
                        float fraction = Mathf.Clamp01(offset / span);
                        sectorWeights[sectorA] = 1f - fraction; sectorWeights[sectorB] = fraction; break;
                    }
                }
                int[] order = Enumerable.Range(0, 4).OrderByDescending(d => sectorWeights[d]).ToArray();
                float sum = sectorWeights[order[0]] + sectorWeights[order[1]];
                float a = sectorWeights[order[0]] / sum, b = sectorWeights[order[1]] / sum;
                int first = 1 + order[0] * pointsPerGuide + lower, second = 1 + order[1] * pointsPerGuide + lower;
                particleWeights[i] = new BoneWeight { boneIndex0 = first, weight0 = a * (1f - t), boneIndex1 = first + 1, weight1 = a * t,
                    boneIndex2 = second, weight2 = b * (1f - t), boneIndex3 = second + 1, weight3 = b * t };
                // Unity's four-weight representation expects descending weights, including across direction blends.
                var unsorted = particleWeights[i];
                int[] indices = { unsorted.boneIndex0, unsorted.boneIndex1, unsorted.boneIndex2, unsorted.boneIndex3 };
                float[] values = { unsorted.weight0, unsorted.weight1, unsorted.weight2, unsorted.weight3 };
                int[] slots = Enumerable.Range(0, 4).OrderByDescending(slot => values[slot]).ToArray();
                particleWeights[i] = new BoneWeight { boneIndex0 = indices[slots[0]], weight0 = values[slots[0]],
                    boneIndex1 = indices[slots[1]], weight1 = values[slots[1]], boneIndex2 = indices[slots[2]], weight2 = values[slots[2]],
                    boneIndex3 = indices[slots[3]], weight3 = values[slots[3]] };
                void Accumulate(int bone, float value) { if (sectorForBone[bone] >= 0) sectorWeights[sectorForBone[bone]] += value; }
            }
            for (int i = 0; i < vertices.Length; i++) newWeights[i] = particleWeights[welded[i]];
            foreach (int d in new[] { 1, 2 })
            {
                var sidePoints = main.Where(i => DirectionWeight(particleWeights[i], d) > .6f).ToArray();
                ForestSpriteDemoBuilder.Require(sidePoints.Length > 0, "Cape has dominant side weights " + directions[d]);
                float meshSide = sidePoints.Average(i => points[i].x - centre.x);
                float guideSide = Enumerable.Range(0, pointsPerGuide).Average(j => guidePoints[d * pointsPerGuide + j].x - centre.x);
                ForestSpriteDemoBuilder.Require(meshSide * guideSide > .001f, "Cape guide and material weights occupy the same side: " + directions[d]);
            }
            mesh.boneWeights = newWeights;
            mesh.bindposes = allBones.Select(b => b.worldToLocalMatrix * skin.transform.localToWorldMatrix).ToArray();
            skin.bones = allBones;
            skin.quality = SkinQuality.Bone4;
            float restError = 0;
            Matrix4x4[] matrices = allBones.Select((b, i) => b.localToWorldMatrix * mesh.bindposes[i]).ToArray();
            for (int i = 0; i < vertices.Length; i++)
            {
                var w = newWeights[i]; Vector3 v = vertices[i];
                Vector3 rebuilt = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                    + matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
                restError = Mathf.Max(restError, Vector3.Distance(rebuilt, skin.transform.TransformPoint(v)));
            }
            ForestSpriteDemoBuilder.Require(restError < .00001f, "Guide bindposes preserve the sculpted rest mesh: " + restError);
            EditorUtility.SetDirty(mesh);
            var mainEdges = edgeCounts.Keys.Where(key => component[(int)(key >> 32)] == mainId).ToArray();
            return new GuideRig { frame = frame, nodes = nodes, restPoints = guidePoints,
                edgeVertices = mainEdges.SelectMany(key => new[] { representatives[(int)(key >> 32)], representatives[(int)key] }).ToArray(),
                edgeLengths = mainEdges.Select(key => Vector3.Distance(points[(int)(key >> 32)], points[(int)key])).ToArray() };

            float DirectionWeight(BoneWeight w, int direction)
            {
                float sum = 0;
                if (w.boneIndex0 > 0 && (w.boneIndex0 - 1) / pointsPerGuide == direction) sum += w.weight0;
                if (w.boneIndex1 > 0 && (w.boneIndex1 - 1) / pointsPerGuide == direction) sum += w.weight1;
                if (w.boneIndex2 > 0 && (w.boneIndex2 - 1) / pointsPerGuide == direction) sum += w.weight2;
                if (w.boneIndex3 > 0 && (w.boneIndex3 - 1) / pointsPerGuide == direction) sum += w.weight3;
                return sum;
            }

            float[] Distances(List<int> seeds, out int[] previous)
            {
                var distance = Enumerable.Repeat(float.PositiveInfinity, points.Count).ToArray();
                previous = Enumerable.Repeat(-1, points.Count).ToArray();
                var visited = new bool[points.Count];
                foreach (int i in seeds) distance[i] = 0;
                for (int step = 0; step < main.Count; step++)
                {
                    int a = -1; float best = float.PositiveInfinity;
                    foreach (int i in main) if (!visited[i] && distance[i] < best) { best = distance[i]; a = i; }
                    if (a < 0) break;
                    visited[a] = true;
                    foreach (int b in adjacency[a])
                    {
                        float candidate = best + Vector3.Distance(points[a], points[b]);
                        if (candidate < distance[b]) { distance[b] = candidate; previous[b] = a; }
                    }
                }
                return distance;
            }
            long Edge(int a, int b) => ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
        }

        // These operations affect the generated copy only. UVs and the source FBX remain unchanged.
        static void WidenWingCape(Mesh mesh)
        {
            var topology = new CapeTopology(mesh);
            Vector3[] vertices = mesh.vertices;
            float top = vertices.Max(v => v.y), bottom = vertices.Min(v => v.y);
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!topology.main.Contains(topology.welded[i])) continue;
                float depth = (top - vertices[i].y) / (top - bottom);
                vertices[i].x *= 1f + .15f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.09f, .35f, depth));
            }
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }

        static void OpenWingFront(Mesh mesh, GuideRig guides, SkinnedMeshRenderer skin)
        {
            ForestSpriteDemoBuilder.Require(mesh.subMeshCount == 1, "Wing variant preserves the single cape material");
            var topology = new CapeTopology(mesh);
            var points = topology.points;
            var loops = topology.BoundaryLoops();
            ForestSpriteDemoBuilder.Require(loops.Count == 2, "Wing cutting starts from collar and hem only");
            var collar = loops.OrderByDescending(g => g.Average(i => points[i].y)).First();
            var hem = loops.Single(g => g != collar);
            int start = collar.OrderByDescending(i => points[i].z - 2f * Mathf.Abs(points[i].x)).First();
            int end = hem.OrderByDescending(i => points[i].z - 2f * Mathf.Abs(points[i].x)).First();
            float top = points.Max(v => v.y), bottom = points.Min(v => v.y);
            float Depth(Vector3 p) => (top - p.y) / (top - bottom);
            var distance = Enumerable.Repeat(float.PositiveInfinity, points.Count).ToArray();
            var previous = Enumerable.Repeat(-1, points.Count).ToArray();
            var visited = new HashSet<int>(); distance[start] = 0;
            foreach (int unused in topology.main)
            {
                int at = -1; float best = float.PositiveInfinity;
                foreach (int i in topology.main) if (!visited.Contains(i) && distance[i] < best) { at = i; best = distance[i]; }
                if (at < 0 || at == end) break;
                visited.Add(at);
                foreach (int next in topology.adjacency[at])
                {
                    Vector3 mid = (points[at] + points[next]) * .5f;
                    float cost = Vector3.Distance(points[at], points[next]) * (1f + 16f * Mathf.Abs(mid.x) + (mid.z < 0 ? 20f : 0));
                    if (best + cost < distance[next]) { distance[next] = best + cost; previous[next] = at; }
                }
            }
            var path = new List<int>();
            for (int at = end; at >= 0; at = previous[at]) path.Add(at);
            path.Reverse();
            ForestSpriteDemoBuilder.Require(path[0] == start, "Connected front-centre cutting path");
            int tipIndex = path.FindIndex(i => Depth(points[i]) >= .18f);
            ForestSpriteDemoBuilder.Require(tipIndex > 0 && tipIndex < path.Count - 2, "Open front begins below the fixed collar");
            path = path.Skip(tipIndex).ToList();
            float tipDepth = Depth(points[path[0]]);
            // Preserve the closed-sheet material atlas. Each opened lip gets its own nearby rotation axis,
            // instead of rotating front material around the distant side-wing guide.
            var nodes = guides.nodes.ToList();
            var restPoints = guides.restPoints.ToList();
            for (int lip = 0; lip < 2; lip++)
            {
                float lipSign = lip == 0 ? 1f : -1f;
                string direction = lip == 0 ? "FrontLipPositiveX" : "FrontLipNegativeX";
                for (int j = 0; j < 7; j++)
                {
                    Vector3 point = OpenFrontPoint(guides.restPoints[21 + j], lipSign);
                    var bone = new GameObject("Cape_" + direction + "_Guide_" + j.ToString("00")).transform;
                    bone.SetParent(guides.frame, false); bone.localPosition = point;
                    if (j > 0) ForestSpriteDemoBuilder.Require(Vector3.Distance(point, restPoints[restPoints.Count - 1]) > .001f,
                        "Positive front lip guide length: " + direction + " " + j);
                    nodes.Add(bone); restPoints.Add(point);
                }
            }
            guides.nodes = nodes.ToArray(); guides.restPoints = restPoints.ToArray();
            skin.bones = new[] { skin.bones[0] }.Concat(guides.nodes).ToArray();
            mesh.bindposes = skin.bones.Select(b => b.worldToLocalMatrix * skin.transform.localToWorldMatrix).ToArray();
            ForestSpriteDemoBuilder.Require(guides.nodes.Length == 42 && skin.bones.Length == 43,
                "Four original guides and two independent front lip guides");
            var cutEdges = new HashSet<long>();
            for (int i = 1; i < path.Count; i++) cutEdges.Add(CapeTopology.Edge(path[i - 1], path[i]));
            var splitFaces = new Dictionary<int, HashSet<int>>();
            float chosenSide = 0;
            for (int k = 1; k < path.Count; k++)
            {
                int vertex = path[k], a = path[k - 1], b = vertex;
                List<int> edgeFaces = topology.edgeFaces[CapeTopology.Edge(a, b)];
                ForestSpriteDemoBuilder.Require(edgeFaces.Count == 2, "Cut follows an interior edge until the hem");
                int seed = edgeFaces.Single(f => topology.HasDirectedEdge(f, a, b));
                HashSet<int> fan = topology.FaceFan(vertex, seed, cutEdges);
                var incident = topology.incidentFaces[vertex];
                ForestSpriteDemoBuilder.Require(fan.Count > 0 && fan.Count < incident.Count, "Cut splits the local face fan");
                int otherSeed = incident.First(f => !fan.Contains(f));
                var otherFan = topology.FaceFan(vertex, otherSeed, cutEdges);
                ForestSpriteDemoBuilder.Require(fan.Count + otherFan.Count == incident.Count, "Exactly two incident fans at each cut point");
                splitFaces.Add(vertex, fan);
                foreach (int face in fan)
                    for (int j = 0; j < 3; j++) chosenSide += points[topology.welded[topology.triangles[face * 3 + j]]].x;
            }
            chosenSide = chosenSide < 0 ? -1f : 1f;
            var vertices = mesh.vertices.ToList();
            var uv = mesh.uv.ToList();
            var weights = mesh.boneWeights.ToList();
            ForestSpriteDemoBuilder.Require(uv.Count == vertices.Count, "Wing copy keeps every source UV");
            var seamSide = Enumerable.Repeat(0f, vertices.Count).ToList();
            var copyOf = new Dictionary<int, int>();
            int[] triangles = (int[])topology.triangles.Clone();
            for (int i = 0; i < triangles.Length; i++)
            {
                int sourceIndex = triangles[i], weldedIndex = topology.welded[sourceIndex];
                if (!splitFaces.TryGetValue(weldedIndex, out HashSet<int> fan)) continue;
                seamSide[sourceIndex] = -chosenSide;
                if (!fan.Contains(i / 3)) continue;
                if (!copyOf.TryGetValue(sourceIndex, out int duplicate))
                {
                    duplicate = vertices.Count; copyOf[sourceIndex] = duplicate;
                    vertices.Add(vertices[sourceIndex]); uv.Add(uv[sourceIndex]); weights.Add(weights[sourceIndex]); seamSide.Add(chosenSide);
                }
                triangles[i] = duplicate;
            }
            // The original two-loop atlas is already calculated. A slit must not become a new hem for that atlas.
            var mainRender = new HashSet<int>(Enumerable.Range(0, topology.welded.Length).Where(i => topology.main.Contains(topology.welded[i])));
            foreach (int i in copyOf.Values) mainRender.Add(i);
            for (int i = 0; i < vertices.Count; i++)
            {
                if (!mainRender.Contains(i)) continue;
                Vector3 p = vertices[i];
                float sign = seamSide[i] == 0 ? (p.x < 0 ? -1f : 1f) : seamSide[i];
                vertices[i] = OpenFrontPoint(p, seamSide[i]);
                // Transfer Front to its nearby lip; mirrored seam copies also keep residual Side weight on their physical side.
                BoneWeight w = weights[i];
                int[] ids = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
                float[] values = { w.weight0, w.weight1, w.weight2, w.weight3 };
                var merged = new Dictionary<int, float>();
                int lipDirection = sign > 0 ? 4 : 5;
                for (int slot = 0; slot < 4; slot++)
                {
                    if (values[slot] <= 0) continue;
                    int id = ids[slot];
                    if (id > 0 && (id - 1) / 7 == 3) id = 1 + lipDirection * 7 + (id - 1) % 7;
                    else if (seamSide[i] != 0 && id > 0)
                    {
                        int direction = (id - 1) / 7;
                        if ((direction == 1 || direction == 2) && guides.restPoints[direction * 7 + 6].x * sign < 0)
                            id = 1 + (direction == 1 ? 2 : 1) * 7 + (id - 1) % 7;
                    }
                    if (!merged.ContainsKey(id)) merged[id] = 0;
                    merged[id] += values[slot];
                }
                var sorted = merged.OrderByDescending(pair => pair.Value).ToArray();
                ForestSpriteDemoBuilder.Require(sorted.Length <= 4, "Front-lip transfer preserves four-weight skinning");
                var result = new BoneWeight();
                if (sorted.Length > 0) { result.boneIndex0 = sorted[0].Key; result.weight0 = sorted[0].Value; }
                if (sorted.Length > 1) { result.boneIndex1 = sorted[1].Key; result.weight1 = sorted[1].Value; }
                if (sorted.Length > 2) { result.boneIndex2 = sorted[2].Key; result.weight2 = sorted[2].Value; }
                if (sorted.Length > 3) { result.boneIndex3 = sorted[3].Key; result.weight3 = sorted[3].Value; }
                weights[i] = result;
            }
            // UV seams may leave unreferenced originals after fan splitting. Remove only those unused render records.
            var used = triangles.Distinct().OrderBy(i => i).ToArray();
            var remap = used.Select((old, index) => new { old, index }).ToDictionary(p => p.old, p => p.index);
            var bindposes = mesh.bindposes;
            mesh.Clear();
            mesh.vertices = used.Select(i => vertices[i]).ToArray();
            mesh.uv = used.Select(i => uv[i]).ToArray();
            mesh.boneWeights = used.Select(i => weights[i]).ToArray();
            mesh.bindposes = bindposes;
            mesh.triangles = triangles.Select(i => remap[i]).ToArray();
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            var finalTopology = new CapeTopology(mesh);
            ForestSpriteDemoBuilder.Require(finalTopology.main.Count > topology.main.Count && finalTopology.components == topology.components,
                "Front lips are separate while the cape and button retain their connected components");
            ForestSpriteDemoBuilder.Require(finalTopology.BoundaryLoops().Count == 2, "Open front joins the hem and leaves the collar unchanged");
            var edges = finalTopology.edgeFaces.Keys.Where(key => finalTopology.main.Contains((int)(key >> 32))).ToArray();
            guides.edgeVertices = edges.SelectMany(key => new[] { finalTopology.representatives[(int)(key >> 32)], finalTopology.representatives[(int)key] }).ToArray();
            guides.edgeLengths = edges.Select(key => Vector3.Distance(finalTopology.points[(int)(key >> 32)], finalTopology.points[(int)key])).ToArray();
            EditorUtility.SetDirty(mesh);
            Debug.Log("Wing cape: 15% side width, open-front tip depth=" + tipDepth + ", split path points=" + path.Count
                + ", render vertices=" + mesh.vertexCount + ", material points=" + finalTopology.points.Count);

            Vector3 OpenFrontPoint(Vector3 p, float forcedSign)
            {
                float sign = forcedSign == 0 ? (p.x < 0 ? -1f : 1f) : forcedSign;
                if (forcedSign != 0) p.x = sign * Mathf.Max(.0006f, Mathf.Abs(p.x));
                float theta = Mathf.Atan2(p.x, -p.z);
                float frontness = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Mathf.PI * .5f, Mathf.PI, Mathf.Abs(theta)));
                float opening = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(tipDepth, .75f, Depth(p)));
                float radius = new Vector2(p.x, p.z).magnitude;
                theta -= sign * (.20f * Mathf.PI) * frontness * opening;
                p.x = radius * Mathf.Sin(theta); p.z = -radius * Mathf.Cos(theta);
                return p;
            }
        }

        sealed class CapeTopology
        {
            public readonly List<Vector3> points = new List<Vector3>();
            public readonly List<int> representatives = new List<int>();
            public readonly int[] welded, triangles;
            public readonly Dictionary<long, List<int>> edgeFaces = new Dictionary<long, List<int>>();
            public readonly List<int>[] adjacency, incidentFaces;
            public readonly HashSet<int> main;
            public readonly int components;
            public static long Edge(int a, int b) => ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
            public CapeTopology(Mesh mesh)
            {
                Vector3[] vertices = mesh.vertices; triangles = mesh.triangles; welded = new int[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    int index = points.FindIndex(p => (p - vertices[i]).sqrMagnitude < 1e-10f);
                    if (index < 0) { index = points.Count; points.Add(vertices[i]); representatives.Add(i); }
                    welded[i] = index;
                }
                adjacency = points.Select(p => new List<int>()).ToArray();
                incidentFaces = points.Select(p => new List<int>()).ToArray();
                for (int f = 0; f < triangles.Length / 3; f++)
                {
                    int a = welded[triangles[f * 3]], b = welded[triangles[f * 3 + 1]], c = welded[triangles[f * 3 + 2]];
                    ForestSpriteDemoBuilder.Require(a != b && a != c && b != c && Vector3.Cross(points[b] - points[a], points[c] - points[a]).sqrMagnitude > 1e-14f,
                        "Wing topology has no degenerate triangles");
                    foreach (int v in new[] { a, b, c }) incidentFaces[v].Add(f);
                    Add(a, b, f); Add(b, c, f); Add(c, a, f);
                }
                ForestSpriteDemoBuilder.Require(edgeFaces.Values.All(f => f.Count <= 2), "Wing topology remains manifold");
                var seen = new HashSet<int>(); var groups = new List<HashSet<int>>();
                for (int i = 0; i < points.Count; i++)
                {
                    if (!seen.Add(i)) continue;
                    var group = new HashSet<int>(); var queue = new Queue<int>(); queue.Enqueue(i);
                    while (queue.Count > 0)
                    {
                        int at = queue.Dequeue(); group.Add(at);
                        foreach (int next in adjacency[at]) if (seen.Add(next)) queue.Enqueue(next);
                    }
                    groups.Add(group);
                }
                components = groups.Count; main = groups.OrderByDescending(g => g.Count).First();
            }
            void Add(int a, int b, int face)
            {
                long key = Edge(a, b);
                if (!edgeFaces.TryGetValue(key, out List<int> faces)) { faces = new List<int>(); edgeFaces[key] = faces; adjacency[a].Add(b); adjacency[b].Add(a); }
                faces.Add(face);
            }
            public bool HasDirectedEdge(int face, int a, int b)
            {
                for (int j = 0; j < 3; j++) if (welded[triangles[face * 3 + j]] == a && welded[triangles[face * 3 + (j + 1) % 3]] == b) return true;
                return false;
            }
            public HashSet<int> FaceFan(int vertex, int seed, HashSet<long> cuts)
            {
                var seen = new HashSet<int> { seed }; var queue = new Queue<int>(); queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    int face = queue.Dequeue();
                    for (int j = 0; j < 3; j++)
                    {
                        int other = welded[triangles[face * 3 + j]];
                        if (other == vertex || cuts.Contains(Edge(vertex, other))) continue;
                        foreach (int next in edgeFaces[Edge(vertex, other)]) if (seen.Add(next)) queue.Enqueue(next);
                    }
                }
                return seen;
            }
            public List<List<int>> BoundaryLoops()
            {
                var boundary = points.Select(p => new List<int>()).ToArray();
                foreach (var edge in edgeFaces.Where(p => p.Value.Count == 1))
                {
                    int a = (int)(edge.Key >> 32), b = (int)edge.Key;
                    if (!main.Contains(a)) continue;
                    boundary[a].Add(b); boundary[b].Add(a);
                }
                ForestSpriteDemoBuilder.Require(boundary.All(v => v.Count == 0 || v.Count == 2), "Wing boundary is a collection of simple loops");
                var seen = new HashSet<int>(); var result = new List<List<int>>();
                foreach (int i in main)
                {
                    if (boundary[i].Count == 0 || !seen.Add(i)) continue;
                    var loop = new List<int>(); var queue = new Queue<int>(); queue.Enqueue(i);
                    while (queue.Count > 0)
                    {
                        int at = queue.Dequeue(); loop.Add(at);
                        foreach (int next in boundary[at]) if (seen.Add(next)) queue.Enqueue(next);
                    }
                    result.Add(loop);
                }
                return result;
            }
        }

        public static void AddDemoFloor(ForestSpriteCapeCloth cape)
        {
            // Cloth accepts spheres/capsules only. A local floor proxy follows horizontally;
            // keeping its radius moderate avoids unreliable very-large-sphere contacts.
            var ground = new GameObject("Cloth floor collision");
            ground.transform.position = new Vector3(0, -25f, 0);
            var sphere = ground.AddComponent<SphereCollider>();
            sphere.radius = 25f;
            sphere.isTrigger = true;
            cape.floorCollision = sphere;
            cape.cloth.sphereColliders = cape.cloth.sphereColliders.Concat(new[] { new ClothSphereColliderPair(sphere) }).ToArray();
        }
    }
}
