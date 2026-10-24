using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.Hillside.Editor;
using WeatherHouse.WildlifeJourney;
using WeatherHouse.WildlifeJourney.Editor;

public static class JourneyEdgeMemoChecks
{
    public static void Run()
    {
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath), ".wildlife-journey-validation"))) throw new InvalidOperationException("marker required");
        int exit = 1;
        try
        {
            RepeatedEdge(); SwimArrivals(); DynamicAndProfiles();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Debug.Log(JourneyNavigationChecks.RunLogicChecks());
            Debug.Log(WildlifeBodyHeadingChecks.RunLogicChecks());
            Debug.Log("PASS call-local edge memo: each direction once, independent fatigue and incoming turn, next-call obstacle/profile/blocked-link changes."); exit = 0;
        }
        catch (Exception error) { Debug.LogError("FAIL " + error); }
        finally { EditorApplication.Exit(exit); }
    }
    private static void RepeatedEdge()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var nav = new GameObject("repeated edge").AddComponent<JourneyNavigation>(); nav.endpointConnectionRadius = 0; nav.sampleSpacing = .25f;
        nav.nodes = new[] { N(0, 0), N(2, 0), N(0, 2), N(2, 2), N(4, 2) };
        nav.links = new[] { L(0, 1), L(0, 2), L(1, 2), L(1, 3), L(2, 3), L(3, 4) };
        int midpointQueries = 0;
        nav.surfaceSampler = p => { if (p.x == 2 && p.z == 1) midpointQueries++; return new JourneySurface { valid = true, blocked = p.x > 2.8f && p.x < 3.2f }; };
        nav.Rebuild();
        Require(nav.FindRoute(nav.nodes[0].position, nav.nodes[4].position, new JourneyProfile { canSwim = false }) == null, "blocked final edge was traversed");
        Debug.Log("EDGE_MEMO midpointQueries=" + midpointQueries);
        Require(midpointQueries == 2, "same directed edge was resampled for multiple incoming approaches");
    }
    private static void SwimArrivals()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var nav = new GameObject("fatigue arrivals").AddComponent<JourneyNavigation>(); nav.endpointConnectionRadius = 0; nav.sampleSpacing = .25f;
        nav.nodes = new[] { N(-4, 0), N(0, 2), N(0, 0), N(3, 0) };
        nav.links = new[] { L(0, 2), L(0, 1), L(1, 2), L(2, 3) };
        nav.surfaceSampler = p => { bool water = p.z < .5f && p.x > -3.9f && p.x < 2.9f; return new JourneySurface { valid = true, hasWater = water, groundY = water ? -1 : 0, waterY = 0 }; };
        nav.Rebuild();
        var profile = new JourneyProfile { canSwim = true, maxSwimDistance = 4, swimSpeed = 1, waterAversion = 0 };
        var route = nav.FindRoute(nav.nodes[0].position, nav.nodes[3].position, profile);
        Require(route != null && route.segments.Exists(s => s.linkIndex == 2), "failed fatigued traversal erased the later rested arrival");
        Require(nav.FindRoute(nav.nodes[0].position, nav.nodes[3].position, profile, new HashSet<int> { 2 }) == null, "blocked-link set reused a prior route");
        Require(nav.FindRoute(nav.nodes[0].position, nav.nodes[3].position, profile) != null, "removed blocked-link set remained cached");
        profile.maxSwimDistance = 7;
        var stronger = nav.FindRoute(nav.nodes[0].position, nav.nodes[3].position, profile);
        Require(stronger != null && stronger.totalCost < route.totalCost && !stronger.segments.Exists(s => s.linkIndex == 2), "new profile retained the prior fatigue result");
    }
    private static void DynamicAndProfiles()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var data = new TerrainData { heightmapResolution = 33, size = new Vector3(20, 1, 20) };
        var terrain = Terrain.CreateTerrainGameObject(data).GetComponent<Terrain>(); terrain.transform.position = new Vector3(-10, 0, -10);
        var nav = new GameObject("dynamic physical edge").AddComponent<JourneyNavigation>(); nav.terrain = terrain; nav.endpointConnectionRadius = 0;
        nav.nodes = new[] { N(-2, 0), N(2, 0) }; nav.links = new[] { L(0, 1) }; nav.Rebuild();
        var profile = new JourneyProfile { canSwim = false, bodyRadius = .2f, bodyHeading = Quaternion.Euler(0, 90, 0),
            bodyEnvelope = new WildlifeBodyEnvelope { center = Vector3.up * .5f, half = new Vector3(.2f, .5f, .45f), measured = true } };
        Physics.SyncTransforms(); Require(Route() != null, "clear physical route failed");
        var obstacle = Box("new obstruction", new Vector3(0, .5f, 0), new Vector3(.3f, 1, 2)); Physics.SyncTransforms();
        Require(Route() == null, "new collider was hidden by old success");
        UnityEngine.Object.DestroyImmediate(obstacle.gameObject); Physics.SyncTransforms(); Require(Route() != null, "removed collider retained old failure");
        Box("left rail", new Vector3(0, .5f, -.7f), new Vector3(1.5f, 1, .2f));
        Box("right rail", new Vector3(0, .5f, .7f), new Vector3(1.5f, 1, .2f)); Physics.SyncTransforms();
        Require(Route() != null, "small body did not fit corridor");
        profile.bodyEnvelope = new WildlifeBodyEnvelope { center = Vector3.up * .5f, half = new Vector3(.7f, .5f, .45f), measured = true };
        Require(Route() == null, "larger body reused smaller-body certificate");
        UnityEngine.Object.DestroyImmediate(data);
        JourneyRoute Route() => nav.FindRoute(nav.nodes[0].position, nav.nodes[1].position, profile);
    }
    private static JourneyNode N(float x, float z) => new JourneyNode { position = new Vector3(x, 0, z) };
    private static JourneyLink L(int a, int b) => new JourneyLink { from = a, to = b, bidirectional = true };
    private static BoxCollider Box(string name, Vector3 p, Vector3 size) { var b = new GameObject(name).AddComponent<BoxCollider>(); b.transform.position = p; b.size = size; return b; }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
}
