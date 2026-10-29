using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.Hillside
{
    // Bounded local A*: Voxel-Resort's grid/heap approach, with actual body sweeps on every edge.
    // It supplements nearby ground goals; JourneyNavigation still owns bridges and swimming.
    public sealed class AnimalLocalRoute
    {
        public delegate bool Step(Vector3 from, Vector3 to, out Vector3 projected);
        sealed class Node { public Vector3 point; public Vector2Int cell; public Node parent; public float cost, priority; }
        readonly List<Node> heap = new List<Node>();
        readonly Dictionary<Vector2Int, float> costs = new Dictionary<Vector2Int, float>();
        readonly Step traverse;
        readonly Vector3 start, goal;
        readonly float cell;
        Node expanding;
        int edge = -1, expanded;
        public bool Finished { get; private set; }
        public List<Vector3> Result { get; private set; }
        public int Queries { get; private set; }
        static readonly Vector2Int[] Directions = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

        public AnimalLocalRoute(Vector3 from, Vector3 to, float bodyRadius, Step step)
        {
            start = from; goal = to; traverse = step; cell = Mathf.Clamp(bodyRadius * 2 + .15f, .65f, 1.5f);
            costs[Vector2Int.zero] = 0;
            Push(new Node { point = from, cell = Vector2Int.zero, priority = Distance(from, to) });
        }
        // One edge query per work unit; no unbudgeted smoothing or whole-grid resampling.
        public void Advance()
        {
            if (Finished) return;
            if (expanding == null)
            {
                if (heap.Count == 0 || expanded >= 512) { Finished = true; return; }
                expanding = Pop(); edge = -1; expanded++;
                if (expanding.cost > costs[expanding.cell]) { expanding = null; return; }
            }
            if (edge == -1)
            {
                edge = 0;
                if (Distance(expanding.point, goal) <= cell * 2.1f || expanding.parent == null && Distance(start, goal) <= 8)
                {
                    Queries++;
                    if (traverse(expanding.point, goal, out Vector3 end))
                    {
                        Result = new List<Vector3> { end };
                        for (Node n = expanding; n.parent != null; n = n.parent) Result.Add(n.point);
                        Result.Reverse(); Finished = true;
                    }
                }
                return;
            }
            Vector2Int key = expanding.cell + Directions[edge++];
            Node parent = expanding;
            if (edge == Directions.Length) expanding = null;
            if (Mathf.Abs(key.x) > 24 || Mathf.Abs(key.y) > 24) return;
            float cost = parent.cost + cell;
            if (costs.TryGetValue(key, out float old) && old <= cost) return;
            Vector3 target = start + new Vector3(key.x * cell, 0, key.y * cell);
            target.y = parent.point.y;
            Queries++;
            if (!traverse(parent.point, target, out Vector3 point)) return;
            costs[key] = cost;
            Push(new Node { point = point, cell = key, parent = parent, cost = cost, priority = cost + Distance(point, goal) });
        }
        static float Distance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        void Push(Node n)
        {
            heap.Add(n); int i = heap.Count - 1;
            while (i > 0) { int p = (i - 1) / 2; if (heap[p].priority <= n.priority) break; heap[i] = heap[p]; i = p; }
            heap[i] = n;
        }
        Node Pop()
        {
            Node result = heap[0], last = heap[heap.Count - 1]; heap.RemoveAt(heap.Count - 1);
            if (heap.Count == 0) return result;
            int i = 0;
            while (i * 2 + 1 < heap.Count)
            {
                int child = i * 2 + 1;
                if (child + 1 < heap.Count && heap[child + 1].priority < heap[child].priority) child++;
                if (heap[child].priority >= last.priority) break;
                heap[i] = heap[child]; i = child;
            }
            heap[i] = last; return result;
        }
    }
}
