using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WatersideWadingReview
    {
        const float Limit = .4f;
        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        static Vector3 Step(Vector3 point, Vector3 delta, Func<Vector3, float> depth)
        {
            Vector3 accepted = WatersideWaterBoundary.ConstrainWadingStep(point, delta, Limit, depth, out _);
            Require(accepted.magnitude <= delta.magnitude + .0001f, "Boundary introduced a displacement larger than the requested step.");
            Require(depth(point + accepted) <= Mathf.Max(Limit, depth(point)) + .0001f, "Step entered deeper forbidden water.");
            return point + accepted;
        }

        public static string CheckMath()
        {
            Func<Vector3, float> shore = p => Mathf.Max(0, p.x);
            Vector3 point = new Vector3(-.5f, 0, 0);
            for (int i = 0; i < 16; i++) point = Step(point, Vector3.right * .05f, shore);
            Require(Mathf.Abs(point.x - .3f) < .0001f, "Dry-to-shallow walking lost movement.");
            Vector3 stopped = Step(point, Vector3.right * .3f, shore);
            Require(stopped.x > .399f && stopped.x <= Limit, "Deep boundary was not reached continuously.");
            Vector3 slide = Step(new Vector3(.35f, 0, 0), new Vector3(.2f, 0, .2f), shore);
            Require(slide.x > .399f && slide.z > .199f, "Oblique movement failed to slide along shore.");
            for (int i = 0; i < 20; i++) stopped = Step(stopped, Vector3.left * .05f, shore);
            Require(stopped.x < -.59f, "Walking back out of shallow water was blocked.");
            Vector3 deep = new Vector3(1, 0, 0);
            deep = Step(deep, new Vector3(-.1f, 0, .05f), shore);
            Require(Mathf.Abs(deep.x - .9f) < .0001f, "A character placed in deep water cannot escape toward shallower ground.");
            deep = Step(deep, new Vector3(.1f, 0, .05f), shore);
            Require(deep.x <= .9001f && deep.z > .099f, "Deep spawn either moved deeper or lost lateral escape.");
            Func<Vector3, float> trench = p => p.x > .1f && p.x < .2f ? 1 : 0;
            Require(Step(Vector3.zero, Vector3.right * .3f, trench).x <= .1001f,
                "A long step skipped a deep trench between two dry endpoints.");
            Func<Vector3, float> curved = p => Mathf.Max(0, new Vector2(p.x, p.z).magnitude - 1);
            point = new Vector3(1.39f, 0, 0);
            for (int i = 0; i < 60; i++) point = Step(point, new Vector3(.015f, 0, .04f), curved);
            foreach (float dt in new[] { 1f / 120, 1f / 30, .2f })
            {
                point = new Vector3(-.5f, 0, 0);
                for (int i = 0; i < 180; i++) point = Step(point, new Vector3(2.25f * dt, 0, .1f * dt), shore);
                Require(point.x > .399f && point.z > 0, "Frame duration changed the shore boundary or prevented sliding.");
            }
            return "PASS math: continuous entry, exact shallow motion, deep stop, shoreline slide, return to dry land, deep-spawn escape, trench sweep, curved bank, 120/30/5 Hz steps.\n";
        }

        public static string Check()
        {
            var scene = SceneManager.GetActiveScene();
            Require(!Application.isPlaying && scene.path == WatersideSceneBuilder.ScenePath, "Run this read-only check in the V2 scene, outside Play.");
            bool wasDirty = scene.isDirty;
            var boundary = Object.FindObjectsOfType<WatersideWaterBoundary>().Single(b => b.gameObject.scene == scene);
            Vector3 originalPlayer = boundary.player.transform.position;
            Require(Mathf.Abs(boundary.maximumWadingDepth - Limit) < .0001f, "Expected the unchanged 0.4 m wading limit.");
            var report = new StringBuilder(CheckMath());
            var terrain = boundary.terrain;
            Func<Vector3, float> depth = p => boundary.SampleSupportedDepth(p,
                terrain.SampleHeight(p) + terrain.transform.position.y + .03f);
            int paths = 0;
            for (int segment = 1; segment < WatersideLayout.Coastline.Length && paths < 10; segment++)
            for (int sample = 1; sample <= 5 && paths < 10; sample++)
            {
                Vector3 a = WatersideLayout.Coastline[segment - 1], b = WatersideLayout.Coastline[segment];
                Vector3 coast = Vector3.Lerp(a, b, sample / 6f);
                Vector3 outward = Vector3.Cross(Vector3.up, (b - a).normalized);
                if (WatersideLayout.SeaSignedDistance(coast.x + outward.x, coast.z + outward.z) > 0) outward = -outward;
                Vector3 start = coast - outward * 3, destination = coast + outward * 3;
                if (depth(start) > 0 || depth(destination) < .7f) continue;
                Vector3 point = start, shallow = start;
                bool entered = false, blocked = false;
                for (int i = 0; i < 150; i++)
                {
                    Vector3 next = Step(point, outward * .045f, depth);
                    if (depth(next) > .02f) { entered = true; shallow = next; }
                    if ((next - point).magnitude < .001f) { blocked = true; break; }
                    point = next;
                }
                if (!entered || !blocked) continue;
                Vector3 edge = point;
                for (int i = 0; i < 200 && Vector3.Distance(point, start) > .05f; i++)
                    point = Step(point, Vector3.ClampMagnitude(start - point, .045f), depth);
                Require(Vector3.Distance(point, start) < .06f && depth(point) == 0, "A real shoreline path could enter but not return to dry land.");
                paths++;
                report.AppendLine($"PASS coast {paths}: dry={start:F3}, shallow={shallow:F3}, stop={edge:F3}, depth={depth(edge):F4}, return={point:F3}");
            }
            Require(paths > 0, "No dry-to-shallow-to-deep coastal route was exercised; inspect current geometry instead of reporting a pass.");
            int bridgeChecks = 0;
            foreach (var crossing in WatersideLayout.Crossings)
            {
                Vector3 center = (crossing.Start + crossing.End) * .5f;
                if (!boundary.surface.Contains(center)) continue;
                var hits = Physics.RaycastAll(center + Vector3.up * 2, Vector3.down, 4,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                var deck = hits.Where(h => !(h.collider is TerrainCollider) && h.normal.y > .7f &&
                    Mathf.Abs(h.point.y - center.y) < 1).OrderBy(h => Mathf.Abs(h.point.y - center.y)).ToArray();
                if (deck.Length == 0) continue;
                float floor = terrain.SampleHeight(center) + terrain.transform.position.y;
                if (deck[0].point.y - floor < .8f) continue;
                Require(boundary.SampleSupportedDepth(center, deck[0].point.y + .02f) <= Limit,
                    "A reachable bridge deck was treated as submerged riverbed: " + crossing.Name);
                float bedDepth = Mathf.Max(0, boundary.surface.Height(center) - floor);
                Require(Mathf.Abs(boundary.SampleSupportedDepth(center, floor + .02f) - bedDepth) < .03f,
                    "A bridge above the feet incorrectly made the riverbed shallow: " + crossing.Name);
                bridgeChecks++;
                report.AppendLine("PASS deck/riverbed support separation: " + crossing.Name);
            }
            Require(bridgeChecks > 0, "No bridge support separation was exercised.");
            Require(scene.isDirty == wasDirty && boundary.player.transform.position == originalPlayer, "Read-only check changed scene or player.");
            report.AppendLine("Geometry/math evidence only; actual CharacterController collision, falling, animation and rendered wakes require the separate Play check.");
            Directory.CreateDirectory("WatersideV3Review/water-interaction");
            File.WriteAllText("WatersideV3Review/water-interaction/wading-checks.txt", report.ToString());
            return report.ToString();
        }
    }
}
