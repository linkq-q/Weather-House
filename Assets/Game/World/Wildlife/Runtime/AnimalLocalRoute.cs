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
        readonly Vector2Int minimum, maximum;
        Node expanding;
        List<Vector3> direct = new List<Vector3>();
        Vector3 directPoint;
        int edge = -1, expanded;
        public bool Finished { get; private set; }
        public List<Vector3> Result { get; private set; }
        public int Queries { get; private set; }
        static readonly Vector2Int[] Directions = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down,
            new Vector2Int(1,1),new Vector2Int(-1,1),new Vector2Int(-1,-1),new Vector2Int(1,-1) };

        public AnimalLocalRoute(Vector3 from, Vector3 to, float bodyRadius, Step step)
        {
            start = from; goal = to; traverse = step; cell = Mathf.Clamp(bodyRadius * 2 + .15f, .65f, 1.5f);
            directPoint=from;
            Vector3 delta = (to - from) / cell;
            // Include the destination as well as the original local detour margin.
            minimum = new Vector2Int(Mathf.Min(-24, Mathf.FloorToInt(delta.x) - 8), Mathf.Min(-24, Mathf.FloorToInt(delta.z) - 8));
            maximum = new Vector2Int(Mathf.Max(24, Mathf.CeilToInt(delta.x) + 8), Mathf.Max(24, Mathf.CeilToInt(delta.z) + 8));
            costs[Vector2Int.zero] = 0;
            Push(new Node { point = from, cell = Vector2Int.zero, priority = Distance(from, to) });
        }
        // One edge query per work unit; no unbudgeted smoothing or whole-grid resampling.
        public void Advance()
        {
            if (Finished) return;
            // Most shelter routes are open ground. Check lease-sized segments first, still one query per unit.
            if(direct!=null)
            {
                float distance=Distance(directPoint,goal);
                Vector3 straightTarget=Vector3.Lerp(directPoint,goal,Mathf.Min(1,6/Mathf.Max(.001f,distance)));
                Queries++;
                if(traverse(directPoint,straightTarget,out Vector3 foot))
                {
                    direct.Add(foot);directPoint=foot;
                    if(Distance(foot,goal)<.01f)
                    {
                        Result=new List<Vector3>();
                        for(Node n=expanding;n!=null && n.parent!=null;n=n.parent)Result.Add(n.point);
                        Result.Reverse();Result.AddRange(direct);Finished=true;
                    }
                }
                else direct=null;
                return;
            }
            if (expanding == null)
            {
                if (heap.Count == 0 || expanded >= 512) { Finished = true; return; }
                expanding = Pop(); edge = -1; expanded++;
                if (expanding.cost > costs[expanding.cell]) { expanding = null; return; }
            }
            if (edge == -1)
            {
                edge = 0;
                // A small detour may clear the obstacle. Reconnect with the same bounded body checks.
                // The root already tried this; a failed reconnect leaves this node's neighbours intact.
                if(expanding.parent!=null)
                {
                    direct=new List<Vector3>();directPoint=expanding.point;
                }
                return;
            }
            Vector2Int direction = Directions[edge++];
            Vector2Int key = expanding.cell + direction;
            Node parent = expanding;
            if (edge == Directions.Length) expanding = null;
            if (key.x < minimum.x || key.x > maximum.x || key.y < minimum.y || key.y > maximum.y) return;
            float cost = parent.cost + cell * (direction.x != 0 && direction.y != 0 ? 1.41421356f : 1);
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
