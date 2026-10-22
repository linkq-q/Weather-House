using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHouseSquirrelLab.Editor
{
    sealed class SquirrelWorkbenchRigState : ScriptableObject
    {
        public SquirrelWorkbenchRig.BoneEdit[] edits;
        public bool changed;
    }

    public sealed class SquirrelWorkbenchRig : IDisposable
    {
        [Serializable] public sealed class BoneEdit
        {
            public string path;
            public Vector3 positionOffset;
            public Vector3 rotationOffsetDegrees;
        }

        sealed class MeshData
        {
            public SkinnedMeshRenderer renderer;
            public Mesh original, editable, baked;
            public int[] triangles;
            public int[][] neighbors, seamPeers;
            public Vector3[] worldVertices, worldNormals;
        }

        struct Hit
        {
            public MeshData mesh;
            public int triangle;
            public float distance;
            public Vector3 point;
        }

        struct TriangleRef { public MeshData mesh; public int index; }

        public SkinnedMeshRenderer[] Renderers { get; private set; }
        public Transform[] Bones { get; private set; }
        public int Revision { get; private set; }
        public bool IsDirty => state != null && state.changed;
        public BoneEdit[] Edits => state.edits.Select(CopyEdit).ToArray();

        readonly GameObject root;
        readonly List<MeshData> meshes = new List<MeshData>();
        readonly SquirrelWorkbenchRigState state;
        readonly Vector3[] bindPositions, bindScales;
        readonly Quaternion[] bindRotations;
        readonly string[] boneLabels;
        readonly Dictionary<Transform, int> boneIndices;
        readonly Dictionary<int, List<TriangleRef>> screenTriangles = new Dictionary<int, List<TriangleRef>>();
        readonly List<TriangleRef> nearPlaneTriangles = new List<TriangleRef>();
        Camera snapshotCamera;
        const int ScreenCells = 64;
        int selected, undoGroup = -1, brushControl;
        bool showBones = true, showWeights, paintWeights, strokeActive, disposed;
        float brushRadius = .12f, brushStrength = .2f, targetWeight = 1f;

        public SquirrelWorkbenchRig(GameObject previewRoot)
        {
            root = previewRoot != null ? previewRoot : throw new ArgumentNullException(nameof(previewRoot));
            Renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null).ToArray();
            if (Renderers.Length == 0) throw new InvalidOperationException("Preview has no skinned mesh.");
            Bones = Renderers.SelectMany(r => r.bones).Where(b => b != null).Distinct()
                .OrderBy(Depth).ThenBy(b => AnimationUtility.CalculateTransformPath(b, root.transform)).ToArray();
            if (Bones.Length == 0) throw new InvalidOperationException("Preview has no skin bones.");
            boneIndices = Bones.Select((b, i) => new { b, i }).ToDictionary(p => p.b, p => p.i);
            bindPositions = new Vector3[Bones.Length]; bindRotations = new Quaternion[Bones.Length]; bindScales = new Vector3[Bones.Length];
            boneLabels = Bones.Select(b => b.name).ToArray();
            var worldBind = new Dictionary<Transform, Matrix4x4>();
            foreach (SkinnedMeshRenderer renderer in Renderers)
            {
                Matrix4x4[] poses = renderer.sharedMesh.bindposes;
                if (poses.Length != renderer.bones.Length || renderer.bones.Any(b => b == null))
                    throw new InvalidOperationException("Incomplete mesh binding: " + renderer.name);
                for (int b = 0; b < poses.Length; b++)
                    if (!worldBind.ContainsKey(renderer.bones[b]))
                        worldBind.Add(renderer.bones[b], renderer.transform.localToWorldMatrix * poses[b].inverse);
            }
            for (int i = 0; i < Bones.Length; i++)
            {
                Transform parent = Bones[i].parent;
                Matrix4x4 parentWorld = parent == null ? Matrix4x4.identity
                    : worldBind.TryGetValue(parent, out Matrix4x4 parentBind) ? parentBind : parent.localToWorldMatrix;
                Matrix4x4 local = parentWorld.inverse * worldBind[Bones[i]];
                bindPositions[i] = local.GetColumn(3); bindRotations[i] = local.rotation; bindScales[i] = local.lossyScale;
            }
            state = ScriptableObject.CreateInstance<SquirrelWorkbenchRigState>();
            state.name = "Squirrel temporary rig edits"; state.hideFlags = HideFlags.HideAndDontSave;
            state.edits = Bones.Select(b => new BoneEdit { path = AnimationUtility.CalculateTransformPath(b, root.transform) }).ToArray();
            foreach (SkinnedMeshRenderer renderer in Renderers)
            {
                Mesh original = renderer.sharedMesh;
                Mesh editable = Object.Instantiate(original);
                editable.name = original.name + " (workbench copy)"; editable.hideFlags = HideFlags.HideAndDontSave;
                var data = new MeshData { renderer = renderer, original = original, editable = editable,
                    baked = new Mesh { name = "Workbench skin snapshot", hideFlags = HideFlags.HideAndDontSave }, triangles = editable.triangles };
                data.neighbors = Neighbors(editable, data.triangles, out data.seamPeers);
                renderer.sharedMesh = editable;
                meshes.Add(data);
            }
            RestoreBindPose();
        }

        public void SetEdits(BoneEdit[] edits)
        {
            if (edits == null) throw new ArgumentNullException(nameof(edits));
            var byPath = new Dictionary<string, BoneEdit>();
            foreach (BoneEdit edit in edits)
            {
                if (edit == null || edit.path == null || byPath.ContainsKey(edit.path)
                    || !Finite(edit.positionOffset) || !Finite(edit.rotationOffsetDegrees))
                    throw new ArgumentException("Invalid or duplicate bone edit.", nameof(edits));
                byPath.Add(edit.path, edit);
            }
            if (byPath.Keys.Any(path => !state.edits.Any(e => e.path == path)))
                throw new ArgumentException("Preset refers to a bone absent from this model.", nameof(edits));
            Undo.RecordObject(state, "Load squirrel bone edits");
            foreach (BoneEdit edit in state.edits)
            {
                byPath.TryGetValue(edit.path, out BoneEdit value);
                edit.positionOffset = value == null ? Vector3.zero : value.positionOffset;
                edit.rotationOffsetDegrees = value == null ? Vector3.zero : value.rotationOffsetDegrees;
            }
            state.changed = true;
            RebuildBindPoses(); Revision++;
        }

        public void RestoreBindPose()
        {
            for (int i = 0; i < Bones.Length; i++)
            {
                Bones[i].localPosition = bindPositions[i] + state.edits[i].positionOffset;
                Bones[i].localRotation = bindRotations[i] * Quaternion.Euler(state.edits[i].rotationOffsetDegrees);
                Bones[i].localScale = bindScales[i];
            }
        }

        // Caller samples the source animation immediately before this method.
        public void ApplyOffsets()
        {
            for (int i = 0; i < Bones.Length; i++)
            {
                Bones[i].localPosition += state.edits[i].positionOffset;
                Bones[i].localRotation *= Quaternion.Euler(state.edits[i].rotationOffsetDegrees);
            }
        }

        void RebuildBindPoses()
        {
            RestoreBindPose();
            foreach (MeshData data in meshes)
                data.editable.bindposes = data.renderer.bones.Select(b => b.worldToLocalMatrix * data.renderer.transform.localToWorldMatrix).ToArray();
        }

        public void ReapplyAfterUndo()
        {
            if (disposed) return;
            RebuildBindPoses(); Revision++;
        }

        public bool DrawInspector()
        {
            bool changed = false;
            EditorGUILayout.LabelField("骨骼与蒙皮", EditorStyles.boldLabel);
            selected = EditorGUILayout.Popup("选中骨骼", Mathf.Clamp(selected, 0, Bones.Length - 1), boneLabels);
            BoneEdit edit = state.edits[selected];
            EditorGUI.BeginChangeCheck();
            Vector3 parentScale = ParentAxisScale(Bones[selected]);
            Vector3 centimeters = EditorGUILayout.Vector3Field("局部位置偏移 (世界 cm)", Vector3.Scale(edit.positionOffset, parentScale) * 100f);
            Vector3 rotation = EditorGUILayout.Vector3Field("局部旋转偏移 (°)", edit.rotationOffsetDegrees);
            if (EditorGUI.EndChangeCheck() && Finite(centimeters) && Finite(rotation))
            {
                Undo.RecordObject(state, "Adjust squirrel bone");
                edit.positionOffset = FromWorldCentimeters(centimeters, parentScale, edit.positionOffset);
                edit.rotationOffsetDegrees = rotation;
                state.changed = true; RebuildBindPoses(); Revision++; changed = true;
            }
            if (parentScale.x < .000001f || parentScale.y < .000001f || parentScale.z < .000001f)
                EditorGUILayout.HelpBox("父级有接近零的缩放；该轴保留原位移，避免除零。", MessageType.Warning);
            if (GUILayout.Button("恢复这根骨骼"))
            {
                Undo.RecordObject(state, "Reset squirrel bone");
                edit.positionOffset = Vector3.zero; edit.rotationOffsetDegrees = Vector3.zero;
                state.changed = true; RebuildBindPoses(); Revision++; changed = true;
            }
            showBones = EditorGUILayout.Toggle("显示骨骼", showBones);
            showWeights = EditorGUILayout.Toggle("显示选骨权重热度", showWeights);
            paintWeights = EditorGUILayout.Toggle("左键刷权重", paintWeights);
            if (paintWeights)
            {
                showWeights = true;
                brushRadius = EditorGUILayout.Slider("笔刷半径 (cm)", brushRadius * 100f, .5f, 50f) * .01f;
                brushStrength = EditorGUILayout.Slider("笔刷强度", brushStrength, .01f, 1f);
                targetWeight = EditorGUILayout.Slider("目标权重", targetWeight, 0f, 1f);
                EditorGUILayout.HelpBox("左键涂可见表面；右键旋转视角。蓝色权重低，红色权重高。Ctrl+Z 撤销一笔。", MessageType.None);
            }
            if (GUILayout.Button("恢复本次全部骨骼与权重修改"))
            {
                Undo.RecordObject(state, "Restore original squirrel rig");
                foreach (MeshData data in meshes)
                {
                    Undo.RecordObject(data.editable, "Restore original squirrel rig");
                    data.editable.boneWeights = data.original.boneWeights;
                }
                foreach (BoneEdit item in state.edits) { item.positionOffset = Vector3.zero; item.rotationOffsetDegrees = Vector3.zero; }
                state.changed = false; RebuildBindPoses(); Revision++; changed = true;
            }
            return changed;
        }

        public void DrawOverlay(Rect previewRect, Camera camera, bool inputEnabled)
        {
            if (disposed || camera == null || previewRect.width <= 0 || previewRect.height <= 0) return;
            Event current = Event.current;
            brushControl = GUIUtility.GetControlID("SquirrelWorkbenchWeightBrush".GetHashCode(), FocusType.Passive);
            if (current.type == EventType.MouseUp && current.button == 0 && strokeActive)
            {
                Undo.CollapseUndoOperations(undoGroup); strokeActive = false;
                if (GUIUtility.hotControl == brushControl) GUIUtility.hotControl = 0;
                current.Use();
            }
            bool needsSkin = showWeights || paintWeights;
            if (needsSkin && (current.type == EventType.Repaint || current.type == EventType.MouseDown || current.type == EventType.MouseDrag)) Snapshot(camera);
            bool inside = previewRect.Contains(current.mousePosition);
            if (inputEnabled && paintWeights && inside && current.button == 0
                && (current.type == EventType.MouseDown || current.type == EventType.MouseDrag && strokeActive))
            {
                Vector2 uv = ToViewport(previewRect, current.mousePosition);
                Ray ray = camera.ViewportPointToRay(new Vector3(uv.x, uv.y, 0f));
                if (Raycast(ray, out Hit hit) && Paint(hit, camera))
                {
                    GUIUtility.hotControl = brushControl;
                    current.Use();
                }
            }
            if (current.type != EventType.Repaint) return;
            Handles.BeginGUI();
            try
            {
                if (showWeights) DrawWeights(previewRect, camera);
                if (showBones)
                    for (int i = 0; i < Bones.Length; i++)
                    {
                        if (!Project(previewRect, camera, Bones[i].position, out Vector3 point)) continue;
                        Handles.color = i == selected ? new Color(1f, .8f, .1f, 1f) : new Color(.2f, .9f, 1f, .8f);
                        if (Bones[i].parent != null && boneIndices.ContainsKey(Bones[i].parent)
                            && Project(previewRect, camera, Bones[i].parent.position, out Vector3 parentPoint))
                            Handles.DrawAAPolyLine(i == selected ? 3f : 1.5f, parentPoint, point);
                        Handles.DrawSolidDisc(point, Vector3.forward, i == selected ? 4f : 2f);
                    }
                if (paintWeights && inside)
                {
                    Vector2 uv = ToViewport(previewRect, current.mousePosition);
                    Ray ray = camera.ViewportPointToRay(new Vector3(uv.x, uv.y, 0f));
                    if (Raycast(ray, out Hit hit) && Project(previewRect, camera, hit.point, out Vector3 center)
                        && Project(previewRect, camera, hit.point + camera.transform.right * brushRadius, out Vector3 edge))
                    {
                        Handles.color = new Color(1f, .85f, .2f, .9f);
                        Handles.DrawWireDisc(center, Vector3.forward, Vector3.Distance(center, edge));
                    }
                }
            }
            finally { Handles.EndGUI(); }
        }

        void Snapshot(Camera camera)
        {
            snapshotCamera = camera; screenTriangles.Clear(); nearPlaneTriangles.Clear();
            foreach (MeshData data in meshes)
            {
                data.renderer.BakeMesh(data.baked, false);
                data.worldVertices = data.baked.vertices; data.worldNormals = data.baked.normals;
                Matrix4x4 matrix = Matrix4x4.TRS(data.renderer.transform.position, data.renderer.transform.rotation, Vector3.one);
                for (int i = 0; i < data.worldVertices.Length; i++)
                {
                    data.worldVertices[i] = matrix.MultiplyPoint3x4(data.worldVertices[i]);
                    if (i < data.worldNormals.Length) data.worldNormals[i] = matrix.MultiplyVector(data.worldNormals[i]).normalized;
                }
                var projected = data.worldVertices.Select(camera.WorldToViewportPoint).ToArray();
                for (int triangle = 0; triangle < data.triangles.Length; triangle += 3)
                {
                    Vector3 a = projected[data.triangles[triangle]], b = projected[data.triangles[triangle + 1]], c = projected[data.triangles[triangle + 2]];
                    var reference = new TriangleRef { mesh = data, index = triangle };
                    if (a.z <= 0 || b.z <= 0 || c.z <= 0) { nearPlaneTriangles.Add(reference); continue; }
                    float minX = Mathf.Min(a.x, b.x, c.x), maxX = Mathf.Max(a.x, b.x, c.x);
                    float minY = Mathf.Min(a.y, b.y, c.y), maxY = Mathf.Max(a.y, b.y, c.y);
                    if (maxX < 0 || minX > 1 || maxY < 0 || minY > 1) continue;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(minX * ScreenCells), 0, ScreenCells - 1), x1 = Mathf.Clamp(Mathf.FloorToInt(maxX * ScreenCells), 0, ScreenCells - 1);
                    int y0 = Mathf.Clamp(Mathf.FloorToInt(minY * ScreenCells), 0, ScreenCells - 1), y1 = Mathf.Clamp(Mathf.FloorToInt(maxY * ScreenCells), 0, ScreenCells - 1);
                    for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                    {
                        int cell = y * ScreenCells + x;
                        if (!screenTriangles.TryGetValue(cell, out List<TriangleRef> list)) screenTriangles.Add(cell, list = new List<TriangleRef>());
                        list.Add(reference);
                    }
                }
            }
        }

        bool Paint(Hit hit, Camera camera)
        {
            MeshData data = hit.mesh;
            int target = Array.IndexOf(data.renderer.bones, Bones[selected]);
            if (target < 0) return false;
            BoneWeight[] before = data.editable.boneWeights;
            BoneWeight[] weights = (BoneWeight[])before.Clone();
            int fallback = Array.IndexOf(data.renderer.bones, Bones[selected].parent);
            if (fallback < 0 || fallback == target) fallback = data.renderer.bones.Length > 1 ? (target == 0 ? 1 : 0) : target;
            var distances = Enumerable.Repeat(float.PositiveInfinity, weights.Length).ToArray();
            var pending = new Queue<int>();
            for (int corner = 0; corner < 3; corner++)
            {
                int v = data.triangles[hit.triangle + corner];
                distances[v] = Vector3.Distance(data.worldVertices[v], hit.point); pending.Enqueue(v);
            }
            while (pending.Count > 0)
            {
                int vertex = pending.Dequeue();
                if (distances[vertex] > brushRadius) continue;
                foreach (int neighbor in data.neighbors[vertex])
                {
                    float distance = distances[vertex] + Vector3.Distance(data.worldVertices[vertex], data.worldVertices[neighbor]);
                    if (distance >= distances[neighbor] || distance > brushRadius) continue;
                    distances[neighbor] = distance; pending.Enqueue(neighbor);
                }
            }
            bool changed = false;
            for (int v = 0; v < weights.Length; v++)
            {
                if (distances[v] >= brushRadius) continue;
                Vector3 point = data.worldVertices[v];
                Vector3 view = camera.WorldToViewportPoint(point);
                if (view.z <= 0 || view.x < 0 || view.x > 1 || view.y < 0 || view.y > 1) continue;
                Ray visibility = camera.ViewportPointToRay(new Vector3(view.x, view.y, 0f));
                if (v < data.worldNormals.Length && Vector3.Dot(data.worldNormals[v], -visibility.direction) <= .02f) continue;
                float vertexDistance = Vector3.Dot(point - visibility.origin, visibility.direction);
                if (Raycast(visibility, out Hit occluder) && vertexDistance - occluder.distance > Mathf.Max(.0001f, brushRadius * .01f)) continue;
                float falloff = Mathf.SmoothStep(1f, 0f, distances[v] / brushRadius);
                float desired = Mathf.Lerp(Weight(before[v], target), targetWeight, brushStrength * falloff);
                BoneWeight next = SetWeight(before[v], target, desired, fallback);
                if (Same(weights[v], next)) continue;
                foreach (int seamVertex in data.seamPeers[v]) weights[seamVertex] = next;
                changed = true;
            }
            if (!changed) return false;
            if (!strokeActive)
            {
                Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Paint squirrel skin weights"); strokeActive = true;
            }
            Undo.RecordObject(data.editable, "Paint squirrel skin weights");
            Undo.RecordObject(state, "Paint squirrel skin weights");
            data.editable.boneWeights = weights; state.changed = true; Revision++;
            return true;
        }

        void DrawWeights(Rect rect, Camera camera)
        {
            // Depth bins keep the heat-map dots on the nearest sampled surface in each screen cell.
            var points = new Dictionary<Vector2Int, Vector4>();
            foreach (MeshData data in meshes)
            {
                if (data.worldVertices == null) continue;
                int target = Array.IndexOf(data.renderer.bones, Bones[selected]);
                BoneWeight[] weights = data.editable.boneWeights;
                int step = Mathf.Max(1, data.worldVertices.Length / 3500);
                for (int v = 0; v < data.worldVertices.Length; v += step)
                {
                    if (!Project(rect, camera, data.worldVertices[v], out Vector3 point)) continue;
                    Vector3 viewport = camera.WorldToViewportPoint(data.worldVertices[v]);
                    Ray ray = camera.ViewportPointToRay(new Vector3(viewport.x, viewport.y, 0f));
                    if (v < data.worldNormals.Length && Vector3.Dot(data.worldNormals[v], -ray.direction) <= .02f) continue;
                    var cell = new Vector2Int(Mathf.FloorToInt(point.x / 3f), Mathf.FloorToInt(point.y / 3f));
                    if (!points.TryGetValue(cell, out Vector4 previous) || viewport.z < previous.z)
                        points[cell] = new Vector4(point.x, point.y, viewport.z, target < 0 ? 0f : Weight(weights[v], target));
                }
            }
            foreach (Vector4 point in points.Values)
            {
                Handles.color = Color.Lerp(new Color(.1f, .35f, 1f, .45f), new Color(1f, .15f, .02f, .95f), point.w);
                Handles.DrawSolidDisc(new Vector3(point.x, point.y, 0f), Vector3.forward, 1.6f);
            }
        }

        bool Raycast(Ray ray, out Hit hit)
        {
            hit = new Hit { distance = float.PositiveInfinity };
            if (snapshotCamera == null) return false;
            Vector3 view = snapshotCamera.WorldToViewportPoint(ray.GetPoint(1f));
            int x = Mathf.Clamp(Mathf.FloorToInt(view.x * ScreenCells), 0, ScreenCells - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(view.y * ScreenCells), 0, ScreenCells - 1);
            if (screenTriangles.TryGetValue(y * ScreenCells + x, out List<TriangleRef> candidates))
                foreach (TriangleRef triangle in candidates) ConsiderHit(ray, triangle, ref hit);
            foreach (TriangleRef triangle in nearPlaneTriangles) ConsiderHit(ray, triangle, ref hit);
            return hit.mesh != null;
        }

        static void ConsiderHit(Ray ray, TriangleRef triangle, ref Hit hit)
        {
            MeshData data = triangle.mesh; int i = triangle.index;
            if (Intersect(ray, data.worldVertices[data.triangles[i]], data.worldVertices[data.triangles[i + 1]], data.worldVertices[data.triangles[i + 2]], out float distance)
                && distance < hit.distance)
                hit = new Hit { mesh = data, triangle = i, distance = distance, point = ray.GetPoint(distance) };
        }

        static bool Intersect(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0f;
            Vector3 edge1 = b - a, edge2 = c - a, cross = Vector3.Cross(ray.direction, edge2);
            float determinant = Vector3.Dot(edge1, cross);
            if (Mathf.Abs(determinant) < 1e-10f) return false;
            float inverse = 1f / determinant;
            Vector3 offset = ray.origin - a;
            float u = Vector3.Dot(offset, cross) * inverse;
            if (u < -.00001f || u > 1.00001f) return false;
            Vector3 q = Vector3.Cross(offset, edge1);
            float v = Vector3.Dot(ray.direction, q) * inverse;
            if (v < -.00001f || u + v > 1.00001f) return false;
            distance = Vector3.Dot(edge2, q) * inverse;
            return distance >= 0f;
        }

        static int[][] Neighbors(Mesh mesh, int[] triangles, out int[][] seamPeers)
        {
            int count = mesh.vertexCount;
            var result = Enumerable.Range(0, count).Select(_ => new HashSet<int>()).ToArray();
            var seamLinks = Enumerable.Range(0, count).Select(v => new HashSet<int> { v }).ToArray();
            for (int i = 0; i < triangles.Length; i += 3)
                for (int c = 0; c < 3; c++)
                {
                    int a = triangles[i + c], b = triangles[i + (c + 1) % 3];
                    result[a].Add(b); result[b].Add(a);
                }
            // FBX duplicates vertices at UV/normal seams. Join identical weighted
            // seam positions for brush traversal without welding the actual mesh.
            Vector3[] vertices = mesh.vertices; BoneWeight[] weights = mesh.boneWeights;
            var seams = new Dictionary<Vector3Int, List<int>>();
            for (int v = 0; v < count; v++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(vertices[v].x * 1000000f), Mathf.RoundToInt(vertices[v].y * 1000000f), Mathf.RoundToInt(vertices[v].z * 1000000f));
                if (!seams.TryGetValue(key, out List<int> coincident)) seams.Add(key, coincident = new List<int>());
                foreach (int other in coincident)
                    if (Same(weights[v], weights[other]))
                    {
                        result[v].Add(other); result[other].Add(v);
                        seamLinks[v].Add(other); seamLinks[other].Add(v);
                    }
                coincident.Add(v);
            }
            seamPeers = seamLinks.Select(list => list.ToArray()).ToArray();
            return result.Select(list => list.ToArray()).ToArray();
        }

        static Vector2 ToViewport(Rect rect, Vector2 point) => new Vector2((point.x - rect.x) / rect.width, 1f - (point.y - rect.y) / rect.height);
        static bool Project(Rect rect, Camera camera, Vector3 world, out Vector3 point)
        {
            Vector3 view = camera.WorldToViewportPoint(world);
            point = new Vector3(rect.x + view.x * rect.width, rect.y + (1f - view.y) * rect.height, 0f);
            return view.z > 0 && rect.Contains(point);
        }
        static int Depth(Transform bone) { int depth = 0; while (bone.parent != null) { depth++; bone = bone.parent; } return depth; }
        static BoneEdit CopyEdit(BoneEdit edit) => new BoneEdit { path = edit.path, positionOffset = edit.positionOffset, rotationOffsetDegrees = edit.rotationOffsetDegrees };
        static Vector3 ParentAxisScale(Transform bone)
        {
            Vector3 scale = bone.parent == null ? Vector3.one : bone.parent.lossyScale;
            return new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        }
        static Vector3 FromWorldCentimeters(Vector3 centimeters, Vector3 scale, Vector3 previous)
        {
            for (int axis = 0; axis < 3; axis++)
                if (scale[axis] >= .000001f && Finite(scale[axis])) previous[axis] = centimeters[axis] * .01f / scale[axis];
            return previous;
        }
        static float Weight(BoneWeight value, int bone) => (value.boneIndex0 == bone ? value.weight0 : 0f)
            + (value.boneIndex1 == bone ? value.weight1 : 0f) + (value.boneIndex2 == bone ? value.weight2 : 0f) + (value.boneIndex3 == bone ? value.weight3 : 0f);

        static BoneWeight SetWeight(BoneWeight source, int bone, float target, int fallback)
        {
            target = Mathf.Clamp01(target);
            var others = new Dictionary<int, float>();
            int[] ids = { source.boneIndex0, source.boneIndex1, source.boneIndex2, source.boneIndex3 };
            float[] values = { source.weight0, source.weight1, source.weight2, source.weight3 };
            for (int i = 0; i < 4; i++)
                if (ids[i] != bone && values[i] > 0f)
                    others[ids[i]] = (others.TryGetValue(ids[i], out float current) ? current : 0f) + values[i];
            if (others.Count == 0 && target < 1f && fallback != bone) others.Add(fallback, 1f);
            var selected = others.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).Take(target > 0f ? 3 : 4).ToList();
            float sum = selected.Sum(pair => pair.Value);
            var output = new List<KeyValuePair<int, float>>();
            if (target > 0f) output.Add(new KeyValuePair<int, float>(bone, target));
            if (sum > 0f && target < 1f)
                output.AddRange(selected.Select(pair => new KeyValuePair<int, float>(pair.Key, pair.Value / sum * (1f - target))));
            if (output.Count == 0 || sum <= 0f) output = new List<KeyValuePair<int, float>> { new KeyValuePair<int, float>(bone, 1f) };
            output = output.Where(pair => pair.Value > 0f).OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).ToList();
            while (output.Count < 4) output.Add(new KeyValuePair<int, float>(0, 0f));
            return new BoneWeight { boneIndex0 = output[0].Key, weight0 = output[0].Value,
                boneIndex1 = output[1].Key, weight1 = output[1].Value, boneIndex2 = output[2].Key, weight2 = output[2].Value,
                boneIndex3 = output[3].Key, weight3 = output[3].Value };
        }

        static bool Same(BoneWeight a, BoneWeight b) => a.boneIndex0 == b.boneIndex0 && a.boneIndex1 == b.boneIndex1
            && a.boneIndex2 == b.boneIndex2 && a.boneIndex3 == b.boneIndex3 && Mathf.Abs(a.weight0 - b.weight0) < 1e-6f
            && Mathf.Abs(a.weight1 - b.weight1) < 1e-6f && Mathf.Abs(a.weight2 - b.weight2) < 1e-6f && Mathf.Abs(a.weight3 - b.weight3) < 1e-6f;

        public string Validate()
        {
            foreach (BoneEdit edit in state.edits)
                if (!Finite(edit.positionOffset) || !Finite(edit.rotationOffsetDegrees)) return "Non-finite bone edit: " + edit.path;
            foreach (MeshData data in meshes)
            {
                if (data.editable.bindposes.Length != data.renderer.bones.Length) return "Bind pose count mismatch: " + data.renderer.name;
                BoneWeight[] weights = data.editable.boneWeights;
                if (weights.Length != data.editable.vertexCount) return "Missing skin weights: " + data.renderer.name;
                for (int v = 0; v < weights.Length; v++)
                    if (!ValidWeight(weights[v], data.renderer.bones.Length)) return "Invalid skin weights: " + data.renderer.name + " vertex " + v;
            }
            return "OK";
        }

        static bool ValidWeight(BoneWeight weight, int bones)
        {
            int[] ids = { weight.boneIndex0, weight.boneIndex1, weight.boneIndex2, weight.boneIndex3 };
            float[] values = { weight.weight0, weight.weight1, weight.weight2, weight.weight3 };
            float sum = 0;
            for (int i = 0; i < 4; i++)
            {
                if (!Finite(values[i]) || values[i] < 0 || values[i] > 0 && (ids[i] < 0 || ids[i] >= bones)) return false;
                if (values[i] > 0 && Enumerable.Range(0, i).Any(j => values[j] > 0 && ids[j] == ids[i])) return false;
                sum += values[i];
            }
            return Mathf.Abs(sum - 1f) <= .0001f;
        }

        [Serializable] sealed class InteractionReport
        {
            public bool passed, sourceMeshesUnchanged, normalizedAfterPaint, dirtyRestored, deepCopyIsolated;
            public string bone, renderer;
            public int changedVertices, revisionBefore, revisionAfterPaint, revisionAfterUndo;
            public float maximumPaintWeightDelta, maximumUndoWeightDelta, maximumCentimeterConversionError;
            public string path = "Current preview camera -> skinned triangle ray hit -> production Paint -> Unity Undo -> production ReapplyAfterUndo";
        }

        public string RunInteractionAssertions(Camera camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (strokeActive) throw new InvalidOperationException("Finish the active brush stroke before running interaction assertions.");
            int previousSelected = selected;
            float previousRadius = brushRadius, previousStrength = brushStrength, previousTarget = targetWeight;
            bool previousDirty = state.changed, painted = false, undone = false;
            BoneEdit[] editsBefore = Edits;
            var before = meshes.ToDictionary(m => m, m => m.editable.boneWeights);
            var sourceBefore = meshes.ToDictionary(m => m, m => m.original.boneWeights);
            var report = new InteractionReport { revisionBefore = Revision };
            try
            {
                BoneEdit[] externalCopy = Edits;
                externalCopy[0].positionOffset += Vector3.one;
                report.deepCopyIsolated = state.edits[0].positionOffset == editsBefore[0].positionOffset;
                if (!report.deepCopyIsolated) throw new InvalidOperationException("Edits getter exposed mutable temporary state.");
                foreach (Transform bone in Bones)
                {
                    Vector3 scale = ParentAxisScale(bone);
                    for (int axis = 0; axis < 3; axis++)
                    {
                        if (scale[axis] < .000001f) continue;
                        Vector3 centimeters = Vector3.zero; centimeters[axis] = 1f;
                        Vector3 local = FromWorldCentimeters(centimeters, scale, Vector3.zero);
                        float worldLength = bone.parent == null ? local.magnitude : bone.parent.TransformVector(local).magnitude;
                        report.maximumCentimeterConversionError = Mathf.Max(report.maximumCentimeterConversionError, Mathf.Abs(worldLength - .01f));
                    }
                }
                if (report.maximumCentimeterConversionError > .00001f)
                    throw new InvalidOperationException("A displayed 1 cm offset does not produce 0.01 world units.");

                Snapshot(camera);
                painted = PaintVisibleFootOrTail(camera, out MeshData paintedMesh, out string paintedBone);
                if (!painted) throw new InvalidOperationException("No visible foot/tail skin could be painted from this preview camera.");
                report.bone = paintedBone; report.renderer = paintedMesh.renderer.name;
                report.revisionAfterPaint = Revision;
                BoneWeight[] after = paintedMesh.editable.boneWeights;
                for (int v = 0; v < after.Length; v++)
                {
                    float delta = WeightDifference(before[paintedMesh][v], after[v]);
                    report.maximumPaintWeightDelta = Mathf.Max(report.maximumPaintWeightDelta, delta);
                    if (delta > .000001f) report.changedVertices++;
                }
                report.normalizedAfterPaint = Validate() == "OK";
                report.sourceMeshesUnchanged = meshes.All(m => WeightDifference(sourceBefore[m], m.original.boneWeights) <= .000001f);
                if (report.changedVertices == 0 || !report.normalizedAfterPaint || !report.sourceMeshesUnchanged || Revision <= report.revisionBefore)
                    throw new InvalidOperationException("Real brush failed change/normalization/source-isolation/revision assertions.");

                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
                strokeActive = false;
                Undo.PerformUndo(); undone = true;
                ReapplyAfterUndo();
                report.revisionAfterUndo = Revision;
                report.maximumUndoWeightDelta = meshes.Max(m => WeightDifference(before[m], m.editable.boneWeights));
                report.dirtyRestored = state.changed == previousDirty;
                if (report.maximumUndoWeightDelta > .000001f || !report.dirtyRestored || Revision <= report.revisionAfterPaint)
                    throw new InvalidOperationException("Unity Undo did not restore the real painted mesh/state or advance Revision.");
                report.passed = true;
                return JsonUtility.ToJson(report, true);
            }
            finally
            {
                // Failure cleanup restores only this preview's temporary data.
                // It never writes the imported mesh or clears unrelated Undo history.
                if (painted && !undone)
                {
                    Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(undoGroup);
                    strokeActive = false; Undo.PerformUndo(); ReapplyAfterUndo();
                }
                if (!report.passed)
                {
                    foreach (MeshData data in meshes) data.editable.boneWeights = before[data];
                    state.edits = editsBefore.Select(CopyEdit).ToArray(); state.changed = previousDirty;
                    RebuildBindPoses(); Revision++;
                }
                selected = previousSelected; brushRadius = previousRadius; brushStrength = previousStrength; targetWeight = previousTarget;
                strokeActive = false; undoGroup = -1;
            }
        }

        bool PaintVisibleFootOrTail(Camera camera, out MeshData paintedMesh, out string paintedBone)
        {
            paintedMesh = null; paintedBone = null;
            var candidates = Enumerable.Range(0, Bones.Length).Where(i => Bones[i].name.StartsWith("Foot.", StringComparison.Ordinal)
                || Bones[i].name.StartsWith("Tail", StringComparison.Ordinal)).OrderBy(i => Bones[i].name.StartsWith("Foot.", StringComparison.Ordinal) ? 0 : 1);
            foreach (int selectedBone in candidates)
            {
                selected = selectedBone;
                foreach (MeshData data in meshes)
                {
                    int localBone = Array.IndexOf(data.renderer.bones, Bones[selected]);
                    if (localBone < 0) continue;
                    BoneWeight[] weights = data.editable.boneWeights;
                    float height = data.worldVertices.Max(p => p.y) - data.worldVertices.Min(p => p.y);
                    for (int triangle = 0; triangle < data.triangles.Length; triangle += 3)
                    {
                        int ia = data.triangles[triangle], ib = data.triangles[triangle + 1], ic = data.triangles[triangle + 2];
                        float weight = (Weight(weights[ia], localBone) + Weight(weights[ib], localBone) + Weight(weights[ic], localBone)) / 3f;
                        if (weight < .3f) continue;
                        Vector3 a = data.worldVertices[ia], b = data.worldVertices[ib], c = data.worldVertices[ic];
                        Vector3 center = (a + b + c) / 3f, view = camera.WorldToViewportPoint(center);
                        if (view.z <= 0 || view.x < 0 || view.x > 1 || view.y < 0 || view.y > 1) continue;
                        Ray ray = camera.ViewportPointToRay(new Vector3(view.x, view.y, 0));
                        if (!Raycast(ray, out Hit hit) || hit.mesh != data || Vector3.Distance(hit.point, center) > Mathf.Max(height * .00005f, .00001f)) continue;
                        float edge = Mathf.Max(Vector3.Distance(a, b), Vector3.Distance(b, c), Vector3.Distance(c, a));
                        brushRadius = Mathf.Max(height * .06f, edge * .85f, .015f);
                        brushStrength = .65f; targetWeight = weight > .5f ? .15f : .85f;
                        if (!Paint(hit, camera)) continue;
                        paintedMesh = data; paintedBone = Bones[selected].name; return true;
                    }
                }
            }
            return false;
        }

        static float WeightDifference(BoneWeight a, BoneWeight b)
        {
            int[] ids = { a.boneIndex0, a.boneIndex1, a.boneIndex2, a.boneIndex3, b.boneIndex0, b.boneIndex1, b.boneIndex2, b.boneIndex3 };
            float maximum = 0;
            foreach (int id in ids) maximum = Mathf.Max(maximum, Mathf.Abs(Weight(a, id) - Weight(b, id)));
            return maximum;
        }

        static float WeightDifference(BoneWeight[] a, BoneWeight[] b)
        {
            if (a.Length != b.Length) return float.PositiveInfinity;
            float maximum = 0;
            for (int i = 0; i < a.Length; i++) maximum = Mathf.Max(maximum, WeightDifference(a[i], b[i]));
            return maximum;
        }

        [MenuItem("Weather House/Squirrel Workbench/Check weight brush arithmetic")]
        public static void RunWeightAssertions()
        {
            var source = new BoneWeight { boneIndex0 = 0, weight0 = .4f, boneIndex1 = 1, weight1 = .3f,
                boneIndex2 = 2, weight2 = .2f, boneIndex3 = 3, weight3 = .1f };
            for (int target = 0; target < 6; target++)
                for (int step = 0; step <= 100; step++)
                {
                    float desired = step / 100f;
                    BoneWeight value = SetWeight(source, target, desired, target == 0 ? 1 : 0);
                    if (!ValidWeight(value, 6) || Mathf.Abs(Weight(value, target) - desired) > .00001f)
                        throw new InvalidOperationException("Weight brush normalization/target assertion failed.");
                }
            var rigid = new BoneWeight { boneIndex0 = 2, weight0 = 1f };
            BoneWeight removed = SetWeight(rigid, 2, 0f, 1);
            if (!ValidWeight(removed, 3) || Weight(removed, 1) != 1f || Weight(rigid, 2) != 1f)
                throw new InvalidOperationException("Rigid weight fallback/source preservation assertion failed.");
            BoneWeight repeated = source;
            for (int i = 0; i < 1000; i++) repeated = SetWeight(repeated, i % 6, (i % 101) / 100f, (i + 1) % 6);
            if (!ValidWeight(repeated, 6)) throw new InvalidOperationException("Repeated painting accumulated invalid weights.");
            Debug.Log("Weight brush arithmetic PASS: 606 target cases, rigid fallback, 1000 repeated strokes; nonnegative, normalized, at most four unique bones.");
        }

        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (strokeActive) { Undo.CollapseUndoOperations(undoGroup); if (GUIUtility.hotControl == brushControl) GUIUtility.hotControl = 0; }
            foreach (MeshData data in meshes)
            {
                if (data.renderer != null) data.renderer.sharedMesh = data.original;
                Undo.ClearUndo(data.editable); Object.DestroyImmediate(data.editable); Object.DestroyImmediate(data.baked);
            }
            Undo.ClearUndo(state); Object.DestroyImmediate(state);
        }
    }
}
