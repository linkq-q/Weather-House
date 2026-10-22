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
            cloth.sphereColliders = new[] { new ClothSphereColliderPair(upper, lower) };
            var driver = node.AddComponent<ForestSpriteCapeCloth>();
            driver.cloth = cloth;
            driver.simulationRoot = player.transform;
            driver.secondaryMotion = secondary;
            driver.restParticles = particles;
            driver.freedomWeights = coefficients.Select(c => c.maxDistance / .36f).ToArray();
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
