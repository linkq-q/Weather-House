using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.WildlifeJourney;
using Object = UnityEngine.Object;

namespace WeatherHouse.Hillside.Editor
{
    public static class WildlifeBodyHeadingChecks
    {
        public static void RunMenu() { Debug.Log(RunLogicChecks()); }

        public static string RunLogicChecks()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode &&
                File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath), ".wildlife-journey-validation")),
                "Heading checks only run outside Play in the marked isolated validation project.");
            Require(SceneManager.GetActiveScene().GetPhysicsScene() == Physics.defaultPhysicsScene,
                "Heading checks require the default physics scene.");
            string result;
            try { result = "PASS distinct arrival headings: " + CheckCompetingArrivals(); }
            catch (Exception error)
            {
                result = "FAIL distinct arrival headings: " + error.Message;
                Write(result); throw new InvalidOperationException(result, error);
            }
            Write(result); return result;
        }

        private static void Write(string result)
        {
            Directory.CreateDirectory("WildlifeJourneyReview/BodyEnvelope");
            File.WriteAllText("WildlifeJourneyReview/BodyEnvelope/heading-checks.txt", result + "\n");
        }

        private static string CheckCompetingArrivals()
        {
            using (var f = new Fixture())
            {
                JourneyRoute shortRoute = Join(f, 0, 1, 3, 4);
                JourneyRoute longRoute = Join(f, 0, 2, 3, 4);
                Require(shortRoute.totalCost + 1f < longRoute.totalCost,
                    "The east-facing arrival must be strictly cheaper than the north-facing arrival.");
                Require(!f.navigation.BodyRouteClear(f.profile, shortRoute),
                    "The short route unexpectedly turns through the rock; this would not exercise arrival-heading dominance.");
                Require(f.navigation.BodyRouteClear(f.profile, longRoute),
                    "The longer north-facing route is not physically clear, including every intermediate turn.");
                Vector3 junction = f.nodes[3].position;
                Require(!f.navigation.CanMoveBody(f.profile, junction, Quaternion.Euler(0, 90, 0), junction, Quaternion.identity),
                    "The junction rock does not block the specific east-to-north turn.");
                Require(f.navigation.CanMoveBody(f.profile, junction, Quaternion.identity, junction, Quaternion.identity),
                    "The safe north-facing junction pose is blocked.");

                JourneyRoute route = f.navigation.FindRoute(f.nodes[0].position, f.nodes[4].position, f.profile);
                Require(route != null,
                    "FindRoute discarded the legal longer arrival after the cheaper arrival reached the same node facing east.");
                Require(route.segments.Exists(s => s.linkIndex == 2) && route.segments.Exists(s => s.linkIndex == 3) &&
                    !route.segments.Exists(s => s.linkIndex == 1),
                    "FindRoute did not retain the route through the southern approach node.");
                Require(f.navigation.BodyRouteClear(f.profile, route), "The selected route contains an unsafe translation or turn.");
                Require(Mathf.Abs(route.totalCost - longRoute.totalCost) < .02f,
                    "The returned route cost does not match the known legal longer route.");

                Object.DestroyImmediate(f.rock.gameObject); Physics.SyncTransforms();
                JourneyRoute clearControl = f.navigation.FindRoute(f.nodes[0].position, f.nodes[4].position, f.profile);
                Require(clearControl != null && clearControl.segments.Exists(s => s.linkIndex == 1) &&
                    Mathf.Abs(clearControl.totalCost - shortRoute.totalCost) < .02f,
                    "Removing the rock did not restore the original cheaper route.");
                return "5 directed nodes; shorter east arrival=" + shortRoute.totalCost.ToString("F3") +
                    "s cannot turn; longer north arrival=" + route.totalCost.ToString("F3") +
                    "s retained; removing real rock restores shorter route";
            }
        }

        private static JourneyRoute Join(Fixture f, params int[] nodes)
        {
            var route = new JourneyRoute();
            for (int i = 1; i < nodes.Length; i++)
            {
                JourneyRoute leg = f.navigation.TraceDirectRoute(f.nodes[nodes[i - 1]].position, f.nodes[nodes[i]].position, f.profile);
                Require(leg != null && leg.segments.Count > 0,
                    "An individual straight leg is blocked: " + nodes[i - 1] + " -> " + nodes[i]);
                route.segments.AddRange(leg.segments); route.totalCost += leg.totalCost;
            }
            return route;
        }

        private sealed class Fixture : IDisposable
        {
            public readonly GameObject root;
            public readonly TerrainData terrainData;
            public readonly JourneyNavigation navigation;
            public readonly JourneyNode[] nodes;
            public readonly JourneyProfile profile;
            public readonly Collider rock;

            public Fixture()
            {
                Vector3 origin = new Vector3(2300, 0, 2300);
                root = new GameObject("Isolated heading dominance checks");
                terrainData = new TerrainData { heightmapResolution = 33, size = new Vector3(20, 2, 20) };
                GameObject ground = Terrain.CreateTerrainGameObject(terrainData);
                ground.transform.SetParent(root.transform, false); ground.transform.position = origin + new Vector3(-10, 0, -10); ground.layer = 31;
                navigation = root.AddComponent<JourneyNavigation>(); navigation.terrain = ground.GetComponent<Terrain>();
                navigation.groundMask = navigation.obstacleMask = 1 << 31; navigation.endpointConnectionRadius = 0;
                nodes = new[]
                {
                    Node("S", -3, -1), Node("W", -3, 0), Node("D", 0, -3), Node("N", 0, 0), Node("G", 0, 3)
                };
                navigation.nodes = nodes;
                navigation.links = new[] { Link(0, 1), Link(1, 3), Link(0, 2), Link(2, 3), Link(3, 4) };
                profile = new JourneyProfile
                {
                    bodyRadius = .25f, walkSpeed = 1, canSwim = false, bodyHeading = Quaternion.identity,
                    bodyEnvelope = new WildlifeBodyEnvelope { center = new Vector3(0, .5f, 0), half = new Vector3(.25f, .45f, .6f) }
                };
                var obstacle = new GameObject("Real rock at the arrival corner"); obstacle.transform.SetParent(root.transform, false);
                obstacle.transform.position = origin + new Vector3(.5f, .5f, .42f); obstacle.layer = 31;
                var collider = obstacle.AddComponent<BoxCollider>(); collider.size = new Vector3(.12f, .8f, .14f); rock = collider;
                navigation.Rebuild(); Physics.SyncTransforms();
                bool visible = false;
                foreach (Collider hit in Physics.OverlapSphere(obstacle.transform.position, .01f, 1 << 31, QueryTriggerInteraction.Ignore))
                    visible |= hit == rock;
                Require(visible, "The real rock is absent from default Physics queries.");
                Require(navigation.SampleSurface(origin).supportingCollider is TerrainCollider,
                    "The junction is not supported by the actual TerrainCollider.");

                JourneyNode Node(string id, float x, float z) => new JourneyNode { id = id, position = origin + new Vector3(x, 0, z) };
                JourneyLink Link(int from, int to) => new JourneyLink { from = from, to = to, bidirectional = false };
            }

            public void Dispose()
            {
                Object.DestroyImmediate(root); Object.DestroyImmediate(terrainData); Physics.SyncTransforms();
            }
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
