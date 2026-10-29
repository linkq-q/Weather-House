using System;
using UnityEngine;

namespace WeatherHousePrototype
{
    // Bounds belong to the uncropped page snapshot, so highlights follow the skinned page.
    public sealed class AlbumPhotoPaper : MonoBehaviour
    {
        public Vector4 bounds;

        public static void Capture(Transform root, Vector4[] regions)
        {
            Array.Clear(regions, 0, regions.Length);
            int index = 0;
            foreach (var photo in root.GetComponentsInChildren<AlbumPhotoPaper>())
                if (index < regions.Length) regions[index++] = photo.bounds;
        }

        public static void Apply(Material material, Vector4[] regions)
        {
            material.SetVectorArray("_PhotoRects", regions);
        }
    }
}
