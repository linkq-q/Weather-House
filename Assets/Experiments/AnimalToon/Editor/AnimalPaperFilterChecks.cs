using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.AnimalToon.Editor
{
    // Exercise the actual GPU merge shader with known islands, a jagged band and a depth boundary.
    public static class AnimalPaperFilterChecks
    {
        public static void Run()
        {
            const int size = 64;
            var seed = new Texture2D(size, size, TextureFormat.RGBAHalf, false, true);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                int boundary = 30 + (y % 4 < 2 ? 2 : -2);
                pixels[y * size + x] = new Color(x >= 56 ? .15f : x < boundary ? .65f : .98f,
                    x >= 44 && x < 46 && y >= 30 && y < 32 ? 0 : 1, x < 56 ? 5 : 8, y < 4 ? 0 : 1);
            }
            seed.SetPixels(pixels); seed.Apply(); pixels = seed.GetPixels();
            var desc = new RenderTextureDescriptor(size, size, RenderTextureFormat.ARGBHalf, 0);
            var a = RTHandles.Alloc(desc, name: "Merge test A");
            var b = RTHandles.Alloc(desc, name: "Merge test B");
            var source = RTHandles.Alloc(seed);
            var material = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/Weather House/Animal Paper Screen"));
            var cmd = new CommandBuffer();
            Texture2D result = null;
            RenderTexture old = RenderTexture.active;
            try
            {
                cmd.SetGlobalVector("_AnimalTexel", new Vector4(1f / size, 1f / size, 1, 0));
                cmd.SetGlobalFloat("_AnimalMergeRadius", 8);
                cmd.SetGlobalVector("_AnimalFilterAxis", Vector2.right);
                Blitter.BlitCameraTexture(cmd, source, a, material, 0);
                cmd.SetGlobalVector("_AnimalFilterAxis", Vector2.up);
                Blitter.BlitCameraTexture(cmd, a, b, material, 0);
                Graphics.ExecuteCommandBuffer(cmd);
                RenderTexture.active = b.rt;
                result = new Texture2D(size, size, TextureFormat.RGBAHalf, false, true);
                result.ReadPixels(new Rect(0, 0, size, size), 0, 0); result.Apply();
                var merged = result.GetPixels();
                int before = Roughness(pixels, size), after = Roughness(merged, size);
                Check(after < before / 2, "Jagged band boundary merges", before + " -> " + after);
                Check(merged[30 * size + 44].g > .8f, "Small shadow island merges", merged[30 * size + 44].g.ToString("F3"));
                Check(merged[2 * size + 20] == pixels[2 * size + 20], "Uncovered pixels remain unchanged", "mask=0");
                Check(Mathf.Abs(merged[30 * size + 56].r - .15f) < .005f, "No shading bleed across depth boundary", merged[30 * size + 56].r.ToString("F3"));
            }
            finally
            {
                RenderTexture.active = old; cmd.Release();
                a.Release(); b.Release(); source.Release();
                UnityEngine.Object.DestroyImmediate(seed); UnityEngine.Object.DestroyImmediate(result);
                CoreUtils.Destroy(material);
            }
        }
        static int Roughness(Color[] pixels, int size)
        {
            int roughness = 0, last = -1;
            for (int y = 12; y < 52; y++)
            {
                int boundary = 0;
                for (int x = 10; x < 50; x++) if (pixels[y * size + x].r >= .82f) { boundary = x; break; }
                if (last >= 0) roughness += Math.Abs(boundary - last);
                last = boundary;
            }
            return roughness;
        }
        static void Check(bool ok, string label, string detail)
        {
            File.AppendAllText("../checks.txt", (ok ? "PASS " : "FAIL ") + label + ": " + detail + "\n");
            if (!ok) throw new InvalidOperationException(label + ": " + detail);
        }
    }
}
