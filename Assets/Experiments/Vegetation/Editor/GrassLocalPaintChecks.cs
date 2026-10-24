using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.Vegetation.Editor
{
    public static class GrassLocalPaintChecks
    {
        [Serializable] sealed class SavedStrokes { public List<GrassPaintStroke> strokes = new List<GrassPaintStroke>(); }

        [MenuItem("Tools/Weather House/草地笔刷/验证局部笔刷数据")]
        static void Menu() { Debug.Log(Run()); }

        public static string Run()
        {
            var soft = Stroke(new GrassBrushSettings { radius = 2, hardness = 0, strength = 1 });
            Near(soft.Weight(Vector2.zero), 1, "Soft brush center");
            Near(soft.Weight(new Vector2(1, 0)), .5f, "Soft brush edge gradient");
            Near(soft.Weight(new Vector2(2.1f, 0)), 0, "Outside brush");
            var hard = Stroke(new GrassBrushSettings { radius = 2, hardness = 1, strength = 1 });
            Near(hard.Weight(new Vector2(1.9f, 0)), 1, "Hard brush inner circle");
            Near(hard.Weight(new Vector2(2.1f, 0)), 0, "Hard brush exterior");

            var line = Stroke(new GrassBrushSettings { radius = 2, hardness = 1 });
            for (int i = 1; i <= 10000; i++) line.Append(new Vector2(i * .01f, 0));
            Require(line.points.Count <= 3, "Straight dragging must not record one point per mouse event.");
            Near(line.Weight(new Vector2(50, .9f)), 1, "Continuous dragged region");
            Near(line.Weight(new Vector2(50, 2.1f)), 0, "Dragged region exterior");

            var saved = new SavedStrokes();
            saved.strokes.Add(Stroke(new GrassBrushSettings { mode = GrassPaintMode.Erase, sourceId = -1, radius = 3, hardness = 1, density = .25f }));
            saved.strokes.Add(Stroke(new GrassBrushSettings { mode = GrassPaintMode.Adjust, sourceId = 1, radius = 3, hardness = 1,
                heightScale = 2, widthScale = .6f, affectDensity = false }));
            var shortGrass = Sample(saved, Vector2.zero, 0, true); var tallGrass = Sample(saved, Vector2.zero, 1, true);
            Near(shortGrass.density, .25f, "Erase base density"); Near(shortGrass.heightScale, 1, "Unselected base height");
            Near(tallGrass.density, .25f, "Height-only edit keeps density"); Near(tallGrass.heightScale, 2, "Selected base height");
            Near(tallGrass.widthScale, .6f, "Selected base width");
            var outside = Sample(saved, new Vector2(4, 0), 1, true);
            Near(outside.density, 1, "Unpainted density"); Near(outside.heightScale, 1, "Unpainted height"); Near(outside.widthScale, 1, "Unpainted width");

            saved.strokes.Add(Stroke(new GrassBrushSettings { mode = GrassPaintMode.Paint, sourceId = 3, radius = 3, hardness = 1,
                density = 10, heightScale = 1.5f, widthScale = .8f }));
            saved.strokes.Add(Stroke(new GrassBrushSettings { mode = GrassPaintMode.Paint, sourceId = 4, radius = 3, hardness = 1, density = 8 }));
            Near(Sample(saved, Vector2.zero, 3, false).density, 10, "Painting after erase restores only the new species");
            saved.strokes.Add(Stroke(new GrassBrushSettings { mode = GrassPaintMode.Erase, sourceId = 3, radius = 3, hardness = 1, density = .3f }));
            Near(Sample(saved, Vector2.zero, 3, false).density, 3, "Species erase affects overlay");
            Near(Sample(saved, Vector2.zero, 4, false).density, 8, "Other overlay species retained");
            Near(Sample(saved, Vector2.zero, 0, true).density, .25f, "Other base species retained");

            string json = JsonUtility.ToJson(saved);
            var restored = JsonUtility.FromJson<SavedStrokes>(json);
            Require(restored.strokes.Count == saved.strokes.Count, "Saved stroke count");
            for (int source = 0; source < 5; source++)
                for (int x = -8; x <= 8; x++)
                    for (int z = -8; z <= 8; z++)
                    {
                        var p = new Vector2(x * .5f, z * .5f);
                        for (int baseFlag = 0; baseFlag < 2; baseFlag++)
                        {
                            var a = Sample(saved, p, source, baseFlag == 1); var b = Sample(restored, p, source, baseFlag == 1);
                            Near(a.density, b.density, "Reload density"); Near(a.heightScale, b.heightScale, "Reload height"); Near(a.widthScale, b.widthScale, "Reload width");
                        }
                    }
            // Removing the last saved stroke exercises the same data restoration used by Undo.
            restored.strokes.RemoveAt(restored.strokes.Count - 1);
            Near(Sample(restored, Vector2.zero, 3, false).density, 10, "Undo data restoration");
            string report = "PASS brush falloff/hard edge, continuous coalesced dragging, base/overlay species isolation, ordered erase/paint, height-only density preservation, serialization roundtrip and removal restoration.\n" +
                "Serialized strokes=" + saved.strokes.Count + "; 10000 straight input events stored as " + line.points.Count + " points.\n" +
                "No scene, source species, materials or meshes were edited. Actual SceneView Undo/Redo, saved-scene reload, rendering and cache performance need Unity integration review.";
            Directory.CreateDirectory("WatersideV3Review/grass-brush"); File.WriteAllText("WatersideV3Review/grass-brush/data-checks.txt", report);
            return report;
        }

        static GrassPaintStroke Stroke(GrassBrushSettings settings)
        {
            var result = new GrassPaintStroke { settings = settings.Copy() }; result.Append(Vector2.zero); return result;
        }
        static GrassLocalSample Sample(SavedStrokes data, Vector2 point, int source, bool isBase)
        {
            var value = isBase ? GrassLocalSample.Base : GrassLocalSample.Overlay;
            foreach (var stroke in data.strokes) value.Apply(stroke, point, source, isBase);
            return value;
        }
        static void Near(float actual, float expected, string name) { Require(Mathf.Abs(actual - expected) < .00001f, name + ": " + actual + " != " + expected); }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
