using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.WildlifeJourney;
using Object = UnityEngine.Object;

namespace WeatherHouse.Hillside.Editor
{
    public static class WildlifeBodyEnvelopeChecks
    {
        private static readonly Vector3 Half = new Vector3(.25f, .45f, .6f);
        private static readonly Vector3 Center = new Vector3(0, .5f, 0);

        public static void RunMenu() { Debug.Log(RunLogicChecks()); }

        public static string RunLogicChecks()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode &&
                File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath), ".wildlife-journey-validation")),
                "Body-envelope checks create default-physics objects and only run in the marked isolated validation project, outside Play.");
            var report = new List<string>(); int failures = 0;
            Run("front wall at 0.5 m", CheckFrontWall);
            Run("thin wall across complete movement", CheckThinWall);
            Run("narrow real bridge width control", CheckNarrowBridge);
            Run("real Herd rotation beside a rock", CheckTurning);
            Run("measured metre-scale shape and route corner", CheckShapeAndCorner);
            Run("unreadable static mesh calibration", CheckUnreadableMesh);
            Run("scaled skinned mesh calibration", CheckSkinnedScale);
            string result = string.Join("\n", report);
            Directory.CreateDirectory("WildlifeJourneyReview/BodyEnvelope");
            File.WriteAllText("WildlifeJourneyReview/BodyEnvelope/checks.txt", result + "\n");
            if (failures != 0) throw new InvalidOperationException(result);
            return result;

            void Run(string name, Func<string> check)
            {
                try { report.Add("PASS " + name + ": " + check()); }
                catch (Exception error) { failures++; report.Add("FAIL " + name + ": " + error.Message); }
            }
        }

        private static string CheckFrontWall()
        {
            using (var f = new Fixture())
            {
                Collider wall = f.Box("Correct front-wall collider", new Vector3(0, 1, .6f), new Vector3(4, 2, .2f));
                f.SyncAndVerify(wall, new Vector3(0, 1, .6f));
                Require(BodyTouches(f.position, Quaternion.identity, wall), "The configured 1.2 m body did not reach the wall's front face at 0.5 m.");
                f.agent.transform.position = f.position - Vector3.forward;
                bool project = f.nav.TryProject(f.profile, f.position, out _, out _);
                bool step = f.nav.TryStep(f.profile, f.agent.transform.position, f.position, out _, out _);
                bool legacy = f.population.TryMove(f.agent, f.position, out _);
                string evidence = "body length=1.2 width=0.5; full-body overlap=True; nav.Project=" + project + " nav.Step=" + step + " legacy.Move=" + legacy;
                Require(!project && !step && !legacy, evidence);
                return evidence;
            }
        }

        private static string CheckThinWall()
        {
            using (var f = new Fixture())
            {
                Collider wall = f.Box("Thin wall between clear endpoints", new Vector3(0, 1, 0), new Vector3(4, 2, .02f));
                f.SyncAndVerify(wall, new Vector3(0, 1, 0));
                Vector3 start = f.position - Vector3.forward, finish = f.position + Vector3.forward;
                Require(!BodyTouches(start, Quaternion.identity, wall) && !BodyTouches(finish, Quaternion.identity, wall), "Thin-wall endpoints are not clear.");
                f.agent.transform.position = start;
                bool projected = f.nav.TryProject(f.profile, finish, out _, out _);
                bool route = f.nav.TraceDirectRoute(start, finish, f.profile) != null;
                bool step = f.nav.TryStep(f.profile, start, finish, out _, out _);
                bool legacy = f.population.TryMove(f.agent, finish, out _);
                string evidence = "clear endpoints=True; nav.Project=" + projected + " direct.Route=" + route + " nav.Step=" + step + " legacy.Move=" + legacy;
                Require(projected && !route && !step && !legacy, evidence);
                return evidence;
            }
        }

        private static string CheckNarrowBridge()
        {
            using (var f = new Fixture())
            {
                Collider deck = f.Box("0.8 m wide bridge deck", new Vector3(0, .1f, 0), new Vector3(.8f, .2f, 6));
                Collider left = f.Box("Left bridge rail", new Vector3(-.47f, .7f, 0), new Vector3(.1f, 1, 6));
                Collider right = f.Box("Right bridge rail", new Vector3(.47f, .7f, 0), new Vector3(.1f, 1, 6));
                f.nav.bridges = new[] { new JourneyBridgeSurface { id = 1, surface = deck } }; f.nav.Rebuild();
                f.SyncAndVerify(right, new Vector3(.47f, .7f, 0));
                Vector3 start = f.position + new Vector3(0, .2f, -1), finish = f.position + new Vector3(0, .2f, 1);
                Require(!BodyTouches(start, Quaternion.identity, left) && !BodyTouches(start, Quaternion.identity, right), "The narrow bridge does not fit the animal's actual width.");
                bool step = f.nav.TryStep(f.profile, start, finish, out _, out JourneyTravelMode mode, JourneyTravelMode.Bridge, 1);
                JourneyRoute route = f.nav.TraceDirectRoute(start, finish, f.profile);
                bool wideCircleHitsRail = false;
                foreach (Collider c in Physics.OverlapSphere(start + Center, Half.z, 1 << 31, QueryTriggerInteraction.Ignore))
                    wideCircleHitsRail |= c == left || c == right;
                Require(wideCircleHitsRail, "The width control would not reject an incorrectly enlarged 0.6 m circular radius.");
                Require(step && mode == JourneyTravelMode.Bridge && route != null, "The 0.5 m wide, 1.2 m long animal was incorrectly blocked by a 0.8 m deck / 0.84 m rail clearance.");
                return "0.5 m width passes 0.84 m rails; 0.6 m circular radius would hit; route and step agree";
            }
        }

        private static string CheckTurning()
        {
            using (var f = new Fixture())
            {
                Collider rock = f.Box("Rock beside a right-angle turn", new Vector3(.5f, .5f, .42f), new Vector3(.12f, .8f, .14f));
                f.SyncAndVerify(rock, new Vector3(.5f, .5f, .42f));
                Require(!BodyTouches(f.position, Quaternion.identity, rock) && !BodyTouches(f.position, Quaternion.Euler(0, 90, 0), rock),
                    "The turn fixture must have clear initial and final poses.");
                Require(BodyTouches(f.position, Quaternion.Euler(0, 45, 0), rock), "The rock does not intersect the intermediate turn envelope.");
                bool began = f.herd.BeginJourney(1), penetrated = false;
                float hitAt = -1, hitYaw = 0;
                for (int i = 0; began && i < 400; i++)
                {
                    f.herd.Simulate(.02f);
                    if (BodyTouches(f.agent.transform.position, f.agent.transform.rotation, rock))
                    { penetrated = true; hitAt = f.herd.SimulationTime; hitYaw = f.agent.transform.eulerAngles.y; break; }
                }
                Object.DestroyImmediate(rock.gameObject); Physics.SyncTransforms();
                f.agent.transform.SetPositionAndRotation(f.position, Quaternion.identity); f.herd.Initialize();
                Require(f.herd.BeginJourney(1), "Removing the rock did not restore the clear route.");
                for (int i = 0; i < 400; i++) f.herd.Simulate(.02f);
                Require(f.agent.transform.position.x > f.position.x + .4f && Quaternion.Angle(Quaternion.identity, f.agent.transform.rotation) > 75,
                    "The clear right-angle turn control never actually walked and turned after 8s (departure is at most 2.4s): position=" + f.agent.transform.position + " yaw=" + f.agent.transform.eulerAngles.y + " state=" + f.herd.members[0].State);
                Require(!penetrated, "Real Herd walked its root along a clear path but rotated its body into the rock at t=" + hitAt.ToString("F3") + ", yaw=" + hitYaw.ToString("F1"));
                return "unsafe intermediate turn rejected; removing rock restores actual movement and >75 degree turn";
            }
        }

        private static bool BodyTouches(Vector3 feet, Quaternion rotation, Collider wanted)
        {
            foreach (Collider c in Physics.OverlapBox(feet + rotation * Center, Half, rotation, 1 << 31, QueryTriggerInteraction.Ignore))
                if (c == wanted) return true;
            return false;
        }

        private static string CheckShapeAndCorner()
        {
            using (var f = new Fixture())
            {
                WildlifeBodyEnvelope body = f.agent.BodyEnvelope;
                Require(body.Valid && Mathf.Abs(body.half.x * 2 - .5f) < .002f &&
                    Mathf.Abs(body.half.y * 2 - .9f) < .002f && Mathf.Abs(body.half.z * 2 - 1.2f) < .002f &&
                    Mathf.Abs(body.Bottom - .05f) < .002f && Mathf.Abs(body.Top - .95f) < .002f,
                    "Measured shape lost its real dimensions or vertical offset: center=" + body.center + " half=" + body.half);
                Require(body.HorizontalRadius > .6f && body.HorizontalRadius < .7f, "Player contact radius is still the .25m navigation width.");
                Collider rock = f.Box("Corner obstacle", new Vector3(.5f, .5f, .42f), new Vector3(.12f, .8f, .14f));
                f.SyncAndVerify(rock, new Vector3(.5f, .5f, .42f));
                Require(f.nav.TraceDirectRoute(f.position, f.position + Vector3.right * 3, f.profile) != null,
                    "The straight right-facing leg itself should fit; this is specifically a turn collision.");
                Require(!f.nav.CanMoveBody(f.profile, f.position, Quaternion.identity, f.position, Quaternion.Euler(0, 90, 0)),
                    "The rotation sweep skipped the rock between two clear orientations.");
                Require(f.nav.FindRoute(f.position, f.position + Vector3.right * 3, f.profile) == null,
                    "The planner repeatedly returned a leg that the current body cannot turn onto.");
                Require(!f.population.CanRotate(f.agent, Quaternion.Euler(0, 90, 0)), "Legacy animal turns still ignore their measured bodies.");
                return "measured 0.5 x 0.9 x 1.2m; bottom 0.05 top 0.95; clear line but unsafe starting turn rejected by both systems";
            }
        }

        private static string CheckUnreadableMesh()
        {
            using (var f = new Fixture())
            {
                MeshFilter filter = f.agent.visualRoot.GetComponent<MeshFilter>();
                Mesh original = filter.sharedMesh, copy = Object.Instantiate(original);
                try
                {
                    filter.sharedMesh = copy; copy.UploadMeshData(true);
                    Require(!copy.isReadable, "The control mesh still has a readable CPU vertex buffer.");
                    WildlifeBodyEnvelope body = WildlifeBodyEnvelope.Measure(f.agent.transform, .25f);
                    Require(Mathf.Abs(body.half.x * 2 - .5f) < .002f && Mathf.Abs(body.half.y * 2 - .9f) < .002f &&
                        Mathf.Abs(body.half.z * 2 - 1.2f) < .002f, "Unreadable mesh bounds lost the real dimensions.");
                    return "UploadMeshData(true) measured correctly from transformed bounds, no importer change";
                }
                finally { filter.sharedMesh = original; Object.DestroyImmediate(copy); }
            }
        }

        private static string CheckSkinnedScale()
        {
            foreach (Vector2 scales in new[] { new Vector2(1, .5f), new Vector2(1, 2), new Vector2(2, .5f), new Vector2(.5f, 2) })
            {
                var root = new GameObject("Known skinned dimensions") { hideFlags = HideFlags.HideAndDontSave };
                Mesh mesh = null;
                try
                {
                    root.transform.localScale = Vector3.one * scales.x;
                    GameObject model = GameObject.CreatePrimitive(PrimitiveType.Cube); model.transform.SetParent(root.transform, false);
                    model.transform.localScale = Vector3.one * scales.y;
                    mesh = Object.Instantiate(model.GetComponent<MeshFilter>().sharedMesh);
                    Object.DestroyImmediate(model.GetComponent<Collider>()); Object.DestroyImmediate(model.GetComponent<MeshRenderer>());
                    Object.DestroyImmediate(model.GetComponent<MeshFilter>());
                    Vector3[] vertices = mesh.vertices; var weights = new BoneWeight[vertices.Length];
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        vertices[i] = Vector3.Scale(vertices[i], new Vector3(.5f, .9f, 1.2f)) + Vector3.up * .5f;
                        weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
                    }
                    var bone = new GameObject("One known bone"); bone.transform.SetParent(model.transform, false);
                    mesh.vertices = vertices; mesh.boneWeights = weights;
                    mesh.bindposes = new[] { bone.transform.worldToLocalMatrix * model.transform.localToWorldMatrix }; mesh.RecalculateBounds();
                    var skin = model.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh;
                    skin.rootBone = bone.transform; skin.bones = new[] { bone.transform }; skin.updateWhenOffscreen = true;
                    WildlifeBodyEnvelope body = WildlifeBodyEnvelope.Measure(root.transform, .25f);
                    Vector3 expected = new Vector3(.5f, .9f, 1.2f) * scales.x * scales.y;
                    Require((body.half * 2 - expected).magnitude < .001f,
                        "Skinned world dimensions repeated or lost scale " + scales + ": expected " + expected + " measured " + body.half * 2);
                }
                finally { Object.DestroyImmediate(root); if (mesh) Object.DestroyImmediate(mesh); }
            }
            return "known bone-weight cube preserves metre dimensions at root/renderer 0.5x and 2x scale";
        }

        private sealed class Fixture : IDisposable
        {
            public readonly Vector3 position = new Vector3(2200, 0, 2200);
            public readonly GameObject root;
            public readonly TerrainData data;
            public readonly JourneyNavigation nav;
            public readonly JourneyHerd herd;
            public readonly JourneyProfile profile;
            public readonly WildlifePopulation population;
            public readonly WildlifeAgent agent;
            public Fixture()
            {
                root = new GameObject("Isolated default-physics body-envelope checks") { hideFlags = HideFlags.HideAndDontSave };
                Require(SceneManager.GetActiveScene().GetPhysicsScene() == Physics.defaultPhysicsScene, "The active scene is not in the default physics scene.");
                data = new TerrainData { heightmapResolution = 33, size = new Vector3(20, 2, 20) };
                var ground = Terrain.CreateTerrainGameObject(data); ground.transform.SetParent(root.transform, false);
                ground.transform.position = position + new Vector3(-10, 0, -10); ground.layer = 31;
                nav = root.AddComponent<JourneyNavigation>(); nav.terrain = ground.GetComponent<Terrain>();
                nav.groundMask = nav.obstacleMask = 1 << 31; nav.maxSlope = 29;
                nav.nodes = new[] { new JourneyNode { id = "start", position = position }, new JourneyNode { id = "right", position = position + Vector3.right * 3 } };
                nav.links = new[] { new JourneyLink { from = 0, to = 1, bidirectional = true } }; nav.Rebuild();
                var legacy = new GameObject("Legacy population"); legacy.transform.SetParent(root.transform, false);
                population = legacy.AddComponent<WildlifePopulation>(); population.terrain = nav.terrain; population.obstacleMask = 1 << 31;
                population.worldBounds = new Bounds(position + Vector3.up * 10, new Vector3(20, 20, 20));
                var animal = new GameObject("1.2 m body with 0.5 m width"); animal.transform.SetParent(legacy.transform, false); animal.transform.position = position;
                var model = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(model.GetComponent<Collider>());
                model.transform.SetParent(animal.transform, false); model.transform.localPosition = Center; model.transform.localScale = Half * 2;
                agent = animal.AddComponent<WildlifeAgent>(); agent.visualRoot = model.transform; agent.scaleRange = Vector2.one;
                agent.bodyRadius = .25f; agent.timidity = agent.sociability = 0; agent.habitatCenter = position; agent.habitatRadius = 9;
                population.agents = new[] { agent }; population.Initialize();
                profile = new JourneyProfile { bodyRadius = .25f, canSwim = false, walkSpeed = .65f };
                herd = root.AddComponent<JourneyHerd>(); herd.navigation = nav; herd.autoAdvance = false; herd.travelSpread = 0;
                herd.members = new[] { new JourneyMember { id = 1, root = animal.transform, visualRoot = model.transform, bodyLength = 1.2f, profile = profile } };
                herd.sites = new[] { new JourneyActivitySite { name = "Start", center = position, radius = .15f },
                    new JourneyActivitySite { name = "Right", center = position + Vector3.right * 3, radius = .15f } };
                herd.Initialize(); Physics.SyncTransforms();
                Require(nav.SampleSurface(position).valid && nav.SampleSurface(position).supportingCollider is TerrainCollider,
                    "The fixture did not sample the actual flat TerrainCollider.");
            }
            public Collider Box(string name, Vector3 offset, Vector3 size)
            {
                var obstacle = new GameObject(name); obstacle.transform.SetParent(root.transform, false); obstacle.transform.position = position + offset; obstacle.layer = 31;
                var collider = obstacle.AddComponent<BoxCollider>(); collider.size = size; return collider;
            }
            public void SyncAndVerify(Collider collider, Vector3 localCenter)
            {
                Physics.SyncTransforms(); bool seen = false;
                foreach (Collider c in Physics.OverlapSphere(position + localCenter, .01f, 1 << 31, QueryTriggerInteraction.Ignore)) seen |= c == collider;
                Require(seen && collider.enabled && !collider.isTrigger, "The default Physics query cannot see the fixture collider; this would be a false collision failure.");
            }
            public void Dispose() { Object.DestroyImmediate(root); Object.DestroyImmediate(data); Physics.SyncTransforms(); }
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
