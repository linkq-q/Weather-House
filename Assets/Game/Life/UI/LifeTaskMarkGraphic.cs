using UnityEngine;
using UnityEngine.UI;

namespace WeatherHouse.Life
{
    public sealed class LifeTaskMarkGraphic : MaskableGraphic
    {
        public bool arrow, right;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Vector2[] points = arrow
                ? new[] { new Vector2(-.48f, 0f), new Vector2(.28f, .46f), new Vector2(.44f, .35f), new Vector2(.32f, -.42f), new Vector2(.15f, -.46f) }
                : new[] { new Vector2(0f, .5f), new Vector2(.16f, .17f), new Vector2(.5f, 0f), new Vector2(.16f, -.17f), new Vector2(0f, -.5f), new Vector2(-.16f, -.17f), new Vector2(-.5f, 0f), new Vector2(-.16f, .17f) };
            Rect rect = GetPixelAdjustedRect(); mesh.AddVert(rect.center, color, Vector2.zero);
            foreach (Vector2 point in points)
                mesh.AddVert(rect.center + Vector2.Scale(new Vector2(arrow && right ? -point.x : point.x, point.y), rect.size), color, Vector2.zero);
            for (int i = 0; i < points.Length; i++) mesh.AddTriangle(0, i + 1, (i + 1) % points.Length + 1);
        }
    }
}
