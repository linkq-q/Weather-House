using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WeatherHousePrototype
{
    [RequireComponent(typeof(Text))]
    public sealed class BreathingTextGlow : BaseMeshEffect
    {
        private readonly List<UIVertex> source = new List<UIVertex>();
        private readonly List<UIVertex> output = new List<UIVertex>();
        private float intensity;

        public float Intensity
        {
            get => intensity;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(intensity, value)) return;
                intensity = value;
                if (graphic != null) graphic.SetVerticesDirty();
            }
        }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive()) return;
            source.Clear(); output.Clear();
            mesh.GetUIVertexStream(source);
            // The halo follows each glyph's alpha; no filled rectangle is created.
            for (int ring = 2; ring >= 1; ring--)
            for (int direction = 0; direction < 8; direction++)
            {
                float angle = direction * Mathf.PI * .25f;
                Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * (ring == 1 ? 1.2f : 3.2f);
                foreach (UIVertex original in source)
                {
                    UIVertex vertex = original;
                    vertex.position += offset;
                    vertex.color = new Color32(255, 221, 163,
                        (byte)(original.color.a * intensity * (ring == 1 ? .18f : .10f)));
                    output.Add(vertex);
                }
            }
            output.AddRange(source);
            mesh.Clear();
            mesh.AddUIVertexTriangleStream(output);
        }
    }
}
