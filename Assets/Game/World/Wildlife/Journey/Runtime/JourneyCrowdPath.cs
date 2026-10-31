using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.WildlifeJourney
{
    public struct JourneyCrowdObstacle
    {
        public Vector3 center;
        public float radius;
    }

    /// <summary>Short XZ paths around body clearances; callers project points onto the actual walking surface.</summary>
    public static class JourneyCrowdPath
    {
        private const int RingPoints = 12, MaximumObstacles = 10;
        private const float NeighbourDistance = 6f, RingClearance = .12f;
        private struct Circle
        {
            public Vector3 center;
            public float radius, startDistanceSquared;
            public int order;
        }

        public static bool TryFindPath(Vector3 start, Vector3 target, float ownRadius,
            IReadOnlyList<JourneyCrowdObstacle> obstacles, Func<Vector3, Vector3, bool> canTraverse,
            out List<Vector3> path, float safetyGap = .12f)
        {
            path = null;
            if (!Finite(start) || !Finite(target) || !Finite(ownRadius) || ownRadius < 0 ||
                !Finite(safetyGap) || safetyGap < 0 || canTraverse == null) return false;
            var circles = new List<Circle>();
            var nearby = new List<Circle>();
            if (obstacles != null)
                for (int i = 0; i < obstacles.Count; i++)
                {
                    JourneyCrowdObstacle obstacle = obstacles[i];
                    if (!Finite(obstacle.center) || !Finite(obstacle.radius) || obstacle.radius < 0) return false;
                    var circle = new Circle { center = obstacle.center, radius = ownRadius + obstacle.radius + safetyGap,
                        startDistanceSquared = DistanceSquared(start, obstacle.center), order = i };
                    if (DistanceSquared(target, circle.center) < circle.radius * circle.radius) return false;
                    circles.Add(circle);
                    if (circle.startDistanceSquared <= NeighbourDistance * NeighbourDistance) nearby.Add(circle);
                }
            if (ClearSegment(start, target, circles, true) && canTraverse(start, target))
            { path = new List<Vector3> { start, target }; return true; }
            nearby.Sort((a, b) => {
                int comparison = a.startDistanceSquared.CompareTo(b.startDistanceSquared);
                return comparison != 0 ? comparison : a.order.CompareTo(b.order);
            });
            if (nearby.Count > MaximumObstacles) nearby.RemoveRange(MaximumObstacles, nearby.Count - MaximumObstacles);
            var points = new List<Vector3> { start, target };
            float routeLengthSquared = DistanceSquared(start, target);
            foreach (Circle circle in nearby)
            {
                // A circumscribed polygon keeps even its chords outside the safety circle.
                float ring = (circle.radius + RingClearance) / (float)Math.Cos(Math.PI / RingPoints);
                for (int i = 0; i < RingPoints; i++)
                {
                    double angle = i * Math.PI * 2 / RingPoints;
                    Vector3 point = new Vector3(circle.center.x + (float)Math.Cos(angle) * ring, start.y,
                        circle.center.z + (float)Math.Sin(angle) * ring);
                    float progress = routeLengthSquared > .000001f ?
                        Mathf.Clamp(Dot(point - start, target - start) / routeLengthSquared, 0, 1) : 0;
                    point.y = start.y + (target.y - start.y) * progress;
                    if (Outside(point, circles)) points.Add(point);
                }
            }
            int count = points.Count;
            var costs = new float[count]; var remaining = new float[count]; var previous = new int[count]; var visited = new bool[count];
            var edges = new sbyte[count, count];
            for (int i = 0; i < count; i++)
            { costs[i] = float.PositiveInfinity; remaining[i] = (float)Math.Sqrt(DistanceSquared(points[i], target)); previous[i] = -1; }
            costs[0] = 0; edges[0, 1] = -1;
            while (true)
            {
                int current = -1; float best = float.PositiveInfinity;
                for (int i = 0; i < count; i++)
                    // Straight-line distance is a consistent lower bound on these Euclidean edges.
                    // Reach the same shortest valid route without sampling unrelated far-side edges first.
                    if (!visited[i] && costs[i] + remaining[i] < best) { best = costs[i] + remaining[i]; current = i; }
                if (current < 0) return false;
                if (current == 1)
                {
                    path = new List<Vector3>();
                    for (int i = 1; i >= 0; i = previous[i]) path.Add(points[i]);
                    path.Reverse(); return true;
                }
                visited[current] = true;
                float travelled = costs[current];
                for (int next = 0; next < count; next++)
                {
                    if (visited[next]) continue;
                    float cost = travelled + (float)Math.Sqrt(DistanceSquared(points[current], points[next]));
                    if (cost >= costs[next]) continue;
                    if (edges[current, next] == 0)
                        edges[current, next] = (sbyte)(ClearSegment(points[current], points[next], circles, current == 0) &&
                            canTraverse(points[current], points[next]) ? 1 : -1);
                    if (edges[current, next] < 0) continue;
                    costs[next] = cost; previous[next] = current;
                }
            }
        }

        private static bool ClearSegment(Vector3 from, Vector3 to, List<Circle> circles, bool fromStart)
        {
            Vector3 delta = to - from;
            float lengthSquared = Dot(delta, delta);
            foreach (Circle circle in circles)
            {
                Vector3 away = from - circle.center;
                float radiusSquared = circle.radius * circle.radius;
                if (fromStart && Dot(away, away) < radiusSquared)
                {
                    // Existing social proximity may separate, but every initial contact must avoid deepening.
                    if (Dot(away, delta) < -0.000001f || DistanceSquared(to, circle.center) < radiusSquared) return false;
                    continue;
                }
                float t = lengthSquared > .000001f ? Mathf.Clamp(-Dot(away, delta) / lengthSquared, 0, 1) : 0;
                if (DistanceSquared(from + delta * t, circle.center) < radiusSquared - .000001f) return false;
            }
            return true;
        }
        private static bool Outside(Vector3 point, List<Circle> circles)
        {
            foreach (Circle circle in circles)
                if (DistanceSquared(point, circle.center) < circle.radius * circle.radius) return false;
            return true;
        }
        private static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.z * b.z;
        private static float DistanceSquared(Vector3 a, Vector3 b) => Dot(a - b, a - b);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
