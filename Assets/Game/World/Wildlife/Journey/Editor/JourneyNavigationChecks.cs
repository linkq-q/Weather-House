using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHouse.WildlifeJourney.Editor
{
    public static class JourneyNavigationChecks
    {
        [MenuItem("Weather House/Wildlife Journey/Check Navigation")]
        public static void RunMenu() { Debug.Log(RunLogicChecks()); }

        // These assertions state the travel contract before the navigation implementation.
        public static string RunLogicChecks()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run isolated navigation checks outside Play.");
            var root = new GameObject("Journey navigation checks") { hideFlags = HideFlags.HideAndDontSave };
            var results = new List<string>();
            var randomState = UnityEngine.Random.state;
            try
            {
                var nav = root.AddComponent<JourneyNavigation>();
                nav.surfaceSampler = River;
                nav.nodes = new[] { Node(-4, 0), Node(4, 0), Node(-4, 4), Node(4, 4) };
                nav.links = new[] { Link(0, 1), Link(0, 2), Link(2, 3, 7), Link(3, 1) };
                nav.Rebuild();
                Vector3 start = nav.nodes[0].position, goal = nav.nodes[1].position;
                var swimmer = new JourneyProfile { canSwim = true, maxSwimDistance = 4, waterAversion = 0, swimSpeed = 1 };
                var dry = new JourneyProfile { canSwim = false };

                Require(nav.TryProject(swimmer, Vector3.zero, out Vector3 afloat, out JourneyTravelMode mode) &&
                    mode == JourneyTravelMode.Swim && Mathf.Abs(afloat.y - nav.floatOffset) < .001f,
                    "Swimming body must follow the water surface, not the river bottom.");
                Require(!nav.TryProject(dry, Vector3.zero, out _, out _), "A non-swimmer accepted deep water.");
                nav.surfaceSampler = p => { JourneySurface s = River(p); s.slope = 80; return s; };
                Require(nav.TryProject(swimmer, Vector3.zero, out _, out _), "Deep swimming incorrectly depends on riverbed slope.");
                nav.surfaceSampler = River;
                results.Add("PASS surface floating and non-swimmer deep-water rejection");

                JourneyRoute shortRoute = nav.FindRoute(start, goal, swimmer);
                Require(shortRoute != null && Has(shortRoute, JourneyTravelMode.Swim) && !Has(shortRoute, JourneyTravelMode.Bridge),
                    "A suitable short crossing was not chosen by a willing swimmer.");
                Require(shortRoute.segments.Count > 8 && shortRoute.totalCost > 0, "Route must expose sampled following segments and cost.");
                JourneyRoute bridgeRoute = nav.FindRoute(start, goal, dry);
                Require(bridgeRoute != null && Has(bridgeRoute, JourneyTravelMode.Bridge) && !Has(bridgeRoute, JourneyTravelMode.Swim),
                    "A dry sheep failed to use the bridge detour.");
                Require(bridgeRoute.totalCost > shortRoute.totalCost, "The direct suitable crossing should cost less than the detour.");
                results.Add("PASS swimmer short crossing and dry sheep bridge detour");

                swimmer.waterAversion = 30;
                Require(Has(nav.FindRoute(start, goal, swimmer), JourneyTravelMode.Bridge), "Water aversion did not affect route cost.");
                JourneyRoute diverted = nav.FindRoute(start, goal, swimmer, new HashSet<int> { 2 });
                Require(diverted != null && Has(diverted, JourneyTravelMode.Swim) && !Has(diverted, JourneyTravelMode.Bridge),
                    "A blocked bridge link did not cause replanning to a valid crossing.");
                Require(nav.FindRoute(start, goal, dry, new HashSet<int> { 2 }) == null, "A dry sheep invented a route past a blocked bridge.");
                results.Add("PASS weighted preference, bridge closure replanning and unreachable route");

                nav.surfaceSampler = p => { JourneySurface s = River(p); s.blocked = p.x > -.2f && p.x < .2f; return s; };
                Require(nav.FindRoute(start, goal, swimmer) == null, "A dynamically blocked surface was not resampled.");
                nav.surfaceSampler = p => { JourneySurface s = River(p); if (p.x >= 2f) s.groundY = 2f; return s; };
                nav.links = new[] { Link(0, 1) };
                Require(nav.FindRoute(start, goal, swimmer) == null, "A swimming sheep climbed a vertical high bank.");
                results.Add("PASS dynamic surface obstruction and unsafe bank exit rejection");

                nav.surfaceSampler = River;
                nav.nodes = new[] { Node(-4, 0), Node(0, 0), Node(4, 0) };
                nav.links = new[] { Link(0, 1), Link(1, 2) };
                swimmer.maxSwimDistance = 1.6f;
                Require(nav.FindRoute(start, goal, swimmer) == null, "Splitting deep water into graph links reset swimming endurance.");
                swimmer.maxSwimDistance = 4;
                Require(nav.FindRoute(start, goal, swimmer) != null, "A continuous crossing inside the swimming limit was rejected.");
                results.Add("PASS continuous swimming distance across graph-link boundaries");

                nav.nodes = new[] { Node(-4, 0), Node(4, 0) };
                nav.links = new[] { Link(0, 1, 99) };
                Require(nav.FindRoute(start, goal, swimmer) == null, "A bridge-labelled link invented a nonexistent bridge deck.");
                nav.links = new[] { Link(0, 1) };
                float expectedRandom = UnityEngine.Random.value;
                UnityEngine.Random.state = randomState;
                JourneyRoute first = nav.FindRoute(start, goal, swimmer), repeat = nav.FindRoute(start, goal, swimmer);
                Require(first != null && repeat != null && first.totalCost == repeat.totalCost &&
                    first.segments.Count == repeat.segments.Count && UnityEngine.Random.value == expectedRandom,
                    "Routing is not deterministic or changed Unity's global random stream.");
                int version = nav.Version; nav.Rebuild();
                Require(nav.Version == version + 1, "Rebuild did not invalidate the navigation version.");
                results.Add("PASS real bridge verification, deterministic routing and version invalidation");

                nav.surfaceSampler = p => {
                    JourneySurface s = River(p);
                    if (s.bridgeId > 0 && Mathf.Abs(p.z - 4f) > .1f) { s.bridgeId = 0; s.groundY = -(2f - Mathf.Abs(p.x)) * .5f; }
                    return s;
                };
                Require(!nav.TryProject(dry, new Vector3(0, .1f, 4), out _, out _),
                    "A narrow bridge supported only the centre point, not the animal's body footprint.");
                nav.surfaceSampler = River;
                Require(nav.TryProject(dry, new Vector3(0, .1f, 4), out _, out _), "A bridge wider than the animal was rejected.");
                results.Add("PASS bridge supporting width respects the animal footprint");

                Require(!nav.TryStep(swimmer, new Vector3(0, nav.floatOffset, 4), new Vector3(.1f, nav.floatOffset, 4), out _, out _),
                    "A swimming animal teleported upward onto an overhead bridge.");
                nav.surfaceSampler = p => new JourneySurface { valid = true, groundY = p.x > 0 ? 1f : 0 };
                Require(nav.TryProject(dry, new Vector3(.1f, 0, 0), out _, out _) &&
                    !nav.TryStep(dry, new Vector3(-.1f, 0, 0), new Vector3(.1f, 0, 0), out _, out _),
                    "Local movement climbed a high step merely because its endpoint was valid.");
                nav.surfaceSampler = p => new JourneySurface { valid = true, groundY = p.x * .2f };
                Require(nav.TryStep(dry, Vector3.zero, new Vector3(.2f, 0, 0), out Vector3 stepped, out _) && Mathf.Abs(stepped.y - .04f) < .001f,
                    "Safe incremental movement no longer follows a walkable slope.");
                results.Add("PASS safe local steps reject overhead snapping and high banks while following gentle slopes");

                nav.surfaceSampler = p => new JourneySurface { valid = true };
                nav.endpointConnectionRadius = 2;
                nav.nodes = new[] { Node(0, 0), Node(1.5f, 1), Node(7, 1), Node(8, 0) };
                nav.links = new[] { Link(1, 2), Link(0, 3) };
                JourneyRoute alternateAttachments = nav.FindRoute(new Vector3(.1f, 0, 0), new Vector3(8.1f, 0, 0), dry, new HashSet<int> { 1 });
                Require(alternateAttachments != null && alternateAttachments.segments.Exists(segment => segment.linkIndex == 0),
                    "Isolated closest endpoint nodes hid a reachable alternative start and goal attachment.");
                Require(nav.FindRoute(nav.nodes[0].position, nav.nodes[3].position, dry, new HashSet<int> { 1 }) == null,
                    "Exact graph endpoints bypassed a closed graph link through implicit attachment shortcuts.");
                results.Add("PASS alternate local endpoint attachments with explicit graph-node identity preserved");

                nav.surfaceSampler = River;
                nav.nodes = new[] { Node(0, 0), Node(4, 0), Node(5, 0) };
                nav.links = new[] { Link(0, 1), Link(1, 2) };
                swimmer.maxSwimDistance = 2;
                Vector3 midstream = new Vector3(0, nav.floatOffset, 0);
                Require(nav.FindRoute(midstream, nav.nodes[1].position, swimmer, null, .4f) != null &&
                    nav.FindRoute(midstream, nav.nodes[1].position, swimmer, null, 1.2f) == null,
                    "Replanning in water restored spent swimming distance instead of using the remaining crossing allowance.");
                Require(nav.FindRoute(nav.nodes[1].position, nav.nodes[2].position, swimmer, null, 100f) != null,
                    "A dry starting surface inherited swimming distance from a finished crossing.");
                nav.surfaceSampler = p => new JourneySurface { valid = true, groundY = p.x < 1 || (p.x > 3 && p.x < 5) ? -1 : 0,
                    waterY = 0, hasWater = p.x < 1 || (p.x > 3 && p.x < 5) };
                nav.nodes = new[] { Node(0, 0), Node(2, 0), Node(6, 0) };
                nav.links = new[] { Link(0, 1), Link(1, 2) };
                swimmer.maxSwimDistance = 3;
                Require(nav.FindRoute(midstream, nav.nodes[2].position, swimmer, null, 1.5f) != null,
                    "Reaching a safe bank did not restore the allowance for the next separate crossing.");
                results.Add("PASS replanning preserves spent swimming distance until a safe shallow or dry surface");

                nav.surfaceSampler = River;
                swimmer.waterlineHeight = .25f;
                Require(nav.TryProject(swimmer, Vector3.zero, out Vector3 smallFloat, out _) && Mathf.Abs(smallFloat.y + .25f) < .001f,
                    "The small animal's own waterline was ignored.");
                swimmer.waterlineHeight = .65f;
                Require(nav.TryProject(swimmer, Vector3.zero, out Vector3 largeFloat, out _) && Mathf.Abs(largeFloat.y + .65f) < .001f,
                    "Different animal sizes used the same floating root height.");
                Require(nav.TryProject(swimmer, new Vector3(1, 0, 0), out Vector3 shallows, out _) && Mathf.Abs(shallows.y + .5f) < .001f,
                    "A tall waterline offset pushed the animal's feet below a shallow bottom.");
                swimmer.waterlineHeight = -1;
                Require(nav.TryProject(swimmer, Vector3.zero, out Vector3 inheritedFloat, out _) && Mathf.Abs(inheritedFloat.y - nav.floatOffset) < .001f,
                    "An unset individual waterline failed to inherit the navigation default.");
                results.Add("PASS individual body waterlines, shallow-bottom support and inherited default");

                // Actual stalled replanning position: every nearby graph node lies behind the new obstruction.
                nav.surfaceSampler = p => new JourneySurface { valid = true,
                    blocked = p.x > -8.1f && p.x < -7.3f && Mathf.Abs(p.z) < 1.2f };
                nav.endpointConnectionRadius = 8;
                nav.nodes = new[] { Node(-11, 0), Node(-4.8f, 0), Node(-11, 5) };
                nav.links = new[] { Link(0, 2) };
                Vector3 corner = new Vector3(-7.671f, 0, -1.2005f);
                JourneyRoute escape = nav.FindRoute(corner, nav.nodes[2].position, dry);
                Require(escape != null && escape.segments.Exists(s => s.to.x < -8.1f && s.to.z < -1.2f),
                    "A valid start beside an obstacle could not take a checked local corner to reach the graph.");
                Require(escape.segments.Exists(s => s.linkIndex == -2) && escape.segments.Exists(s => s.linkIndex == -1),
                    "The two connector legs share an identity, allowing route smoothing to cut across the obstacle again.");
                JourneyRoute cornerGoal = nav.FindRoute(nav.nodes[2].position, corner, dry);
                Require(cornerGoal != null && Vector3.Distance(cornerGoal.segments[cornerGoal.segments.Count - 1].to, corner) < .001f,
                    "A bent goal attachment was found but its actual exit was replaced by a blocked straight line.");
                nav.surfaceSampler = p => new JourneySurface { valid = true, blocked = true };
                Require(nav.FindRoute(corner, nav.nodes[2].position, dry) == null,
                    "Local connector fallback escaped from an invalid or blocked starting surface.");
                results.Add("PASS obstacle-corner start and goal connectors retain both verified legs");

                nav.surfaceSampler = p => new JourneySurface { valid = true, hasWater = true, groundY = -1, waterY = 0,
                    blocked = p.x > .5f && p.x < 1.5f && Mathf.Abs(p.z) < .25f };
                nav.endpointConnectionRadius = 3;
                nav.nodes = new[] { Node(2, 0), Node(4, 0) };
                nav.links = new[] { Link(0, 1) };
                swimmer.maxSwimDistance = 5.5f;
                Require(nav.FindRoute(midstream, nav.nodes[1].position, swimmer, null, .3f) != null &&
                    nav.FindRoute(midstream, nav.nodes[1].position, swimmer, null, 1.5f) == null,
                    "A bent start connector lost spent swimming distance across its two legs or the graph edge.");
                Require(nav.FindRoute(nav.nodes[1].position, midstream, swimmer, null, .3f) != null &&
                    nav.FindRoute(nav.nodes[1].position, midstream, swimmer, null, 1.5f) == null,
                    "The reversed bent goal connector lost swimming distance accumulated before reaching the exit.");
                results.Add("PASS bent start and goal connectors preserve the entire continuous swimming budget");

                nav.surfaceSampler = River;
                nav.nodes = null; nav.links = null;
                swimmer.maxSwimDistance = 4; swimmer.waterAversion = 0;
                JourneyRoute direct = nav.TraceDirectRoute(start, goal, swimmer);
                Require(direct != null && Has(direct, JourneyTravelMode.Swim) && direct.segments.Count > 8 &&
                    direct.totalCost > 0 && direct.navigationVersion == nav.Version &&
                    direct.segments.TrueForAll(s => s.linkIndex == -1 && Mathf.Abs(s.from.z) < .001f && Mathf.Abs(s.to.z) < .001f),
                    "Direct surface validation requires a graph, loses its metadata, or bends away from the requested line.");
                Require(nav.TraceDirectRoute(start, goal, dry) == null, "Direct validation let a non-swimmer cross deep water.");
                swimmer.maxSwimDistance = 1.6f;
                Require(nav.TraceDirectRoute(start, goal, swimmer) == null, "Direct validation exceeded the continuous swimming budget.");
                swimmer.maxSwimDistance = 4;
                nav.surfaceSampler = p => { JourneySurface s = River(p); if (p.x >= 2f) s.groundY = 2f; return s; };
                Require(nav.TraceDirectRoute(start, goal, swimmer) == null, "Direct validation climbed an unsafe high bank.");
                nav.surfaceSampler = River;
                nav.nodes = new[] { Node(-4, 0), Node(4, 0) }; nav.links = new[] { Link(0, 1) };
                JourneyRoute graphLine = nav.FindRoute(start, goal, swimmer);
                Require(graphLine != null && graphLine.segments.Count == direct.segments.Count &&
                    Mathf.Abs(graphLine.totalCost - direct.totalCost) < .0001f, "Direct and graph validation disagree on the same straight crossing.");
                for (int i = 0; i < direct.segments.Count; i++)
                {
                    JourneySegment a = direct.segments[i], b = graphLine.segments[i];
                    Require(a.mode == b.mode && a.bridgeId == b.bridgeId && Vector3.Distance(a.from, b.from) < .0001f &&
                        Vector3.Distance(a.to, b.to) < .0001f, "Direct validation changed a projected surface segment.");
                }
                nav.nodes = new[] { Node(-4, 0), Node(4, 0), Node(-4, 4), Node(4, 4) };
                nav.links = new[] { Link(0, 2), Link(2, 3, 7), Link(3, 1) };
                Require(nav.FindRoute(start, goal, dry) != null && nav.TraceDirectRoute(start, goal, dry) == null,
                    "Direct validation silently took an available graph bridge detour.");
                swimmer.maxSwimDistance = 2;
                Require(nav.TraceDirectRoute(midstream, goal, swimmer, .4f) != null &&
                    nav.TraceDirectRoute(midstream, goal, swimmer, 1.2f) == null &&
                    nav.TraceDirectRoute(goal, goal + new Vector3(1, 0, 0), swimmer, 100f) != null,
                    "Direct validation lost spent swimming distance or retained it after reaching dry land.");
                Require(nav.TraceDirectRoute(new Vector3(float.NaN, 0, 0), goal, swimmer) == null &&
                    nav.TraceDirectRoute(start, goal, swimmer, -1f) == null &&
                    nav.TraceDirectRoute(start, goal, swimmer, float.PositiveInfinity) == null &&
                    nav.TraceDirectRoute(start, goal, null) == null &&
                    nav.TraceDirectRoute(start, goal, new JourneyProfile { walkSpeed = 0 }) == null,
                    "Direct validation accepts inputs rejected by route search.");
                results.Add("PASS graph-independent direct surface validation shares geometry, costs and swimming limits without detours");

                nav.surfaceSampler = p => new JourneySurface { valid = true, groundY = -.1f,
                    hasWater = p.x >= 0 && p.x <= .4f, waterY = 0 };
                Vector3 dryStart = new Vector3(-.5f, -.1f, 0), dryGoal = new Vector3(1, -.1f, 0);
                Require(nav.TryStep(dry, dryStart, dryGoal, out _, out _) &&
                    !nav.TryStep(dry, dryStart, dryGoal, out _, out _, JourneyTravelMode.Walk) &&
                    !nav.TryStep(dry, new Vector3(.1f, -.1f, 0), dryGoal, out _, out _, JourneyTravelMode.Walk),
                    "A walking-only shortcut crossed shallow water or accepted a wet starting surface.");
                nav.surfaceSampler = p => new JourneySurface { valid = true, bridgeId = p.x > .2f && p.x < .6f ? 8 : 7 };
                Require(nav.TryStep(dry, Vector3.zero, new Vector3(1, 0, 0), out _, out _, JourneyTravelMode.Bridge) &&
                    !nav.TryStep(dry, Vector3.zero, new Vector3(1, 0, 0), out _, out _, JourneyTravelMode.Bridge, 7) &&
                    !nav.TryStep(dry, Vector3.zero, new Vector3(-1, 0, 0), out _, out _, JourneyTravelMode.Bridge, 8) &&
                    nav.TryStep(dry, Vector3.zero, new Vector3(-1, 0, 0), out _, out _, JourneyTravelMode.Bridge, 7),
                    "A bridge-specific shortcut crossed another bridge or ignored its starting deck identity.");
                results.Add("PASS optional step constraints validate every surface mode and bridge identity including the start");

                CheckBridgeLookup();
                results.Add("PASS lazy real-collider bridge mapping, replacement, rebuild edits and disabled/destroyed decks");

                CheckTerrainTriangles();
                results.Add("PASS real TerrainCollider thin-triangle rejection and safe-line movement agree with route planning");

                nav.surfaceSampler = null;
                nav.waterAreas = new[] { new JourneyWaterArea { surfaceHeight = 9,
                    bounds = new Bounds(Vector3.zero, new Vector3(20, 20, 20)) } };
                nav.worldWaterSurface = root.AddComponent<JourneyNavigationCheckWater>();
                JourneySurface realWater = nav.SampleSurface(new Vector3(.5f, 0, 0));
                Require(realWater.hasWater && Mathf.Abs(realWater.waterY - 2.05f) < .001f &&
                    !nav.SampleSurface(new Vector3(3, 0, 0)).hasWater,
                    "Configured world water did not exclusively determine both its true boundary and local height.");
                nav.surfaceSampler = p => new JourneySurface { valid = true };
                Require(!nav.SampleSurface(Vector3.zero).hasWater, "World water overrode the injected test surface.");
                nav.surfaceSampler = null; nav.worldWaterSurface = null;
                Require(nav.SampleSurface(new Vector3(3, 0, 0)).waterY == 9,
                    "Standalone rectangular water stopped working when no world water source is configured.");
                results.Add("PASS authoritative world-water boundary and height with standalone and injected fallbacks");
                return string.Join("\n", results);
            }
            finally { UnityEngine.Random.state = randomState; Object.DestroyImmediate(root); }
        }

        private static void CheckBridgeLookup()
        {
            var root = new GameObject("Isolated bridge lookup check") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                root.transform.position = new Vector3(2000, 0, 2000);
                var first = new GameObject("First deck"); first.transform.SetParent(root.transform, false); first.layer = 31;
                var second = new GameObject("Second deck"); second.transform.SetParent(root.transform, false); second.layer = 31;
                second.transform.localPosition = new Vector3(3, 0, 0);
                Collider a = first.AddComponent<BoxCollider>(), b = second.AddComponent<BoxCollider>();
                JourneyNavigation nav = root.AddComponent<JourneyNavigation>(); nav.groundMask = 1 << 31; nav.obstacleMask = 0;
                nav.bridges = new[] { null, new JourneyBridgeSurface { id = 0, surface = a },
                    new JourneyBridgeSurface { id = 4 }, new JourneyBridgeSurface { id = 7, surface = a },
                    new JourneyBridgeSurface { id = 9, surface = a }, new JourneyBridgeSurface { id = 5, surface = b } };
                Physics.SyncTransforms();
                JourneySurface firstHit = nav.SampleSurface(first.transform.position);
                Require(nav.Version == 0 && firstHit.valid && firstHit.bridgeId == 7 && firstHit.supportingCollider == a &&
                    nav.SampleSurface(second.transform.position).bridgeId == 5,
                    "Fresh serialized bridge mappings require Rebuild, lose real collider identity, or replace the first positive duplicate ID.");
                nav.bridges = new[] { new JourneyBridgeSurface { id = 11, surface = b } };
                Require(nav.SampleSurface(first.transform.position).bridgeId == 0 && nav.SampleSurface(second.transform.position).bridgeId == 11,
                    "Replacing the bridge array retained stale collider mappings.");
                nav.bridges[0].id = 13; nav.Rebuild();
                Require(nav.SampleSurface(second.transform.position).bridgeId == 13, "Rebuild ignored an ID edited within the same bridge array.");
                nav.bridges[0].surface = a; nav.Rebuild();
                Require(nav.SampleSurface(first.transform.position).bridgeId == 13 && nav.SampleSurface(second.transform.position).bridgeId == 0,
                    "Rebuild ignored a collider edited within the same bridge array or retained its old mapping.");
                a.enabled = false; Physics.SyncTransforms();
                Require(!nav.SampleSurface(first.transform.position).valid, "A cached disabled collider still supplied a bridge surface.");
                a.enabled = true; Physics.SyncTransforms();
                Require(nav.SampleSurface(first.transform.position).bridgeId == 13, "Re-enabling a bridge collider lost its cached identity.");
                Object.DestroyImmediate(a); Physics.SyncTransforms();
                Require(!nav.SampleSurface(first.transform.position).valid, "A cached destroyed collider still supplied a bridge surface.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void CheckTerrainTriangles()
        {
            const int resolution = 33;
            const float cell = 180f / 512f;
            var data = new TerrainData { heightmapResolution = resolution, size = new Vector3(32 * cell, 20, 32 * cell) };
            GameObject ground = null;
            try
            {
                var heights = new float[resolution, resolution];
                // The four corners reproduce the 31.29/26.66 degree triangles at the observed Waterside stall.
                for (int z = 0; z < resolution; z++)
                    for (int x = 0; x < resolution; x++)
                        heights[z, x] = (z <= 16 ? (x <= 16 ? 9.598364f : 9.809436f) :
                            (x <= 16 ? 9.745956f : 9.842764f)) / 20f;
                data.SetHeights(0, 0, heights);
                ground = Terrain.CreateTerrainGameObject(data);
                ground.name = "Isolated thin-terrain-triangle check"; ground.hideFlags = HideFlags.HideAndDontSave; ground.layer = 31;
                Vector3 origin = new Vector3(1000, 0, 1000);
                ground.transform.position = origin;
                JourneyNavigation nav = ground.AddComponent<JourneyNavigation>();
                nav.terrain = ground.GetComponent<Terrain>(); nav.groundMask = 1 << 31; nav.obstacleMask = 0; nav.maxSlope = 29;
                Physics.SyncTransforms();
                var profile = new JourneyProfile { bodyRadius = .28566015f, canSwim = false };
                Vector3 start = origin + new Vector3(17.045333f * cell, 0, 16.686222f * cell);
                Vector3 badGoal = origin + new Vector3(15 * cell, 0, 17.2f * cell);
                Vector3 goodGoal = origin + new Vector3(17.5f * cell, 0, 19 * cell);
                Require(nav.TryProject(profile, start, out start, out _) && nav.TryProject(profile, badGoal, out badGoal, out _) &&
                    nav.TryProject(profile, goodGoal, out goodGoal, out _), "Terrain regression endpoints must all be walkable.");
                Require(!nav.TryStep(profile, start, Vector3.MoveTowards(start, badGoal, .02f), out _, out _),
                    "The real TerrainCollider fixture no longer reproduces the steep thin triangle between coarse samples.");
                Require(nav.TraceDirectRoute(start, badGoal, profile) == null,
                    "Direct planning skipped a steep terrain triangle that rejects the first real walking step.");
                nav.nodes = new[] { new JourneyNode { position = start }, new JourneyNode { position = badGoal } };
                nav.links = new[] { Link(0, 1) };
                Require(nav.FindRoute(start, badGoal, profile) == null, "Graph planning skipped the same steep terrain triangle.");
                Require(nav.TraceDirectRoute(start, goodGoal, profile) != null, "Triangle sampling rejected the safe northeast alternative.");
                Vector3 current = start;
                for (int step = 0; step < 128 && Vector3.Distance(current, goodGoal) > .001f; step++)
                {
                    Require(nav.TryStep(profile, current, Vector3.MoveTowards(current, goodGoal, .02f), out current, out JourneyTravelMode mode) &&
                        mode == JourneyTravelMode.Walk, "A planned safe terrain alternative fails incremental walking.");
                }
                Require(Vector3.Distance(current, goodGoal) <= .001f, "Incremental walking did not finish the safe terrain alternative.");
            }
            finally { if (ground) Object.DestroyImmediate(ground); Object.DestroyImmediate(data); }
        }

        private static JourneySurface River(Vector3 p)
        {
            bool wet = Mathf.Abs(p.x) < 2f;
            bool bridge = wet && Mathf.Abs(p.z - 4f) < .4f;
            return new JourneySurface { valid = true, groundY = bridge ? .1f : wet ? -(2f - Mathf.Abs(p.x)) * .5f : 0,
                hasWater = wet, waterY = 0, bridgeId = bridge ? 7 : 0 };
        }
        private static JourneyNode Node(float x, float z) { return new JourneyNode { id = x + ":" + z, position = new Vector3(x, 0, z) }; }
        private static JourneyLink Link(int from, int to, int bridge = 0) { return new JourneyLink { from = from, to = to, bidirectional = true, bridgeId = bridge }; }
        private static bool Has(JourneyRoute route, JourneyTravelMode mode)
        { return route != null && route.segments.Exists(segment => segment.mode == mode); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }

    public sealed class JourneyNavigationCheckWater : WeatherHouse.WorldWaterSurface
    {
        public override bool Contains(Vector3 p, float margin = 0) => p.x * p.x + p.z * p.z <= 1;
        public override float Height(Vector3 p) => 2 + p.x * .1f;
    }
}
