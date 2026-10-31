using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    // Author the readable picture-and-letter rectangle, not the whole stone or its base.
    // Its local +Z points out of the inscribed face; local X/Y describe readingSize.
    [DisallowMultipleComponent]
    public sealed class LifeGlyphInscription : MonoBehaviour
    {
        public string inscriptionId;
        public string[] glyphIds = Array.Empty<string>();
        public Renderer inscriptionRenderer;
        public Transform readingFace;
        public Vector2 readingSize = Vector2.one;

        public sealed class Reading
        {
            public readonly string inscriptionId;
            public readonly string[] glyphIds;
            internal Reading(string id, string[] glyphs)
            { inscriptionId = id; glyphIds = (string[])glyphs.Clone(); }
        }

        public static Reading[] Capture(Camera camera, GlyphCatalog catalog)
        {
            if (!camera || catalog?.glyphs == null) return Array.Empty<Reading>();
            var known = new HashSet<string>(catalog.glyphs.Where(g => g != null).Select(g => g.id), StringComparer.Ordinal);
            var candidates = FindObjectsOfType<LifeGlyphInscription>().Where(item => item.CanRead(camera, known)).ToArray();
            if (candidates.Length == 0) return Array.Empty<Reading>();
            var found = new List<Reading>();
            try
            {
                // Only a real shutter opens this render query. No proximity/update discovery.
                using (LifePhotoDepth.Begin(camera))
                    foreach (var item in candidates)
                        if (item.Visible(camera)) found.Add(new Reading(item.inscriptionId, item.glyphIds));
            }
            catch (Exception error)
            {
                // Keep the photograph, but never award unseen letters after a failed visibility query.
                Debug.LogWarning("Inscription visibility capture failed; no inscription was learned. " + error);
                return Array.Empty<Reading>();
            }
            return found.ToArray();
        }

        bool CanRead(Camera camera, HashSet<string> known)
        {
            if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(inscriptionId) || !inscriptionRenderer ||
                !inscriptionRenderer.enabled || inscriptionRenderer.forceRenderingOff || !inscriptionRenderer.gameObject.activeInHierarchy ||
                !readingFace || glyphIds == null || glyphIds.Length == 0 ||
                glyphIds.Any(id => string.IsNullOrWhiteSpace(id) || !known.Contains(id)) ||
                !Finite(readingSize.x) || !Finite(readingSize.y) || readingSize.x <= 0 || readingSize.y <= 0 ||
                (camera.cullingMask & (1 << inscriptionRenderer.gameObject.layer)) == 0 ||
                inscriptionRenderer.gameObject.layer == LayerMask.NameToLayer("UI")) return false;
            Vector3 towardCamera = camera.orthographic ? -camera.transform.forward : (camera.transform.position - readingFace.position).normalized;
            if (Vector3.Dot(readingFace.forward, towardCamera) < .35f ||
                !GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), inscriptionRenderer.bounds)) return false;

            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
            for (int y = -1; y <= 1; y += 2)
                for (int x = -1; x <= 1; x += 2)
                {
                    Vector3 screen = camera.WorldToViewportPoint(FacePoint(x * .5f, y * .5f));
                    if (!Finite(screen.x) || !Finite(screen.y) || !Finite(screen.z) || screen.z <= camera.nearClipPlane ||
                        screen.z > camera.farClipPlane || screen.x < 0 || screen.x > 1 || screen.y < 0 || screen.y > 1) return false;
                    min = Vector2.Min(min, screen); max = Vector2.Max(max, screen);
                }
            // At the saved 1440px height, 3.5% is about 50px. Long inscriptions also need room per letter.
            return max.y - min.y >= .035f && max.x - min.x >= Mathf.Max(.035f, glyphIds.Length * .012f);
        }

        bool Visible(Camera camera)
        {
            if (!LifePhotoVisibility.RendererVisible(camera, inscriptionRenderer)) return false;
            // Sample the authored picture and lettering area, including its outer rows/columns.
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                    if (!LifePhotoVisibility.Point(camera, FacePoint(x * .42f, y * .42f), inscriptionRenderer.transform)) return false;
            return true;
        }

        Vector3 FacePoint(float x, float y) => readingFace.TransformPoint(new Vector3(x * readingSize.x, y * readingSize.y, 0));
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static string[] PhotoGlyphs(string[] animalSpeech, Reading[] readings)
        {
            // Preserve the animal's original order and repetitions; only appended stone letters are deduplicated.
            var result = new List<string>(animalSpeech ?? Array.Empty<string>());
            var present = new HashSet<string>(result, StringComparer.Ordinal);
            foreach (Reading reading in readings ?? Array.Empty<Reading>())
                foreach (string id in reading.glyphIds)
                    if (present.Add(id)) result.Add(id);
            return result.ToArray();
        }

        public static void Observe(LanguageNotebook notebook, Reading[] readings)
        {
            if (notebook == null) return;
            foreach (Reading reading in readings ?? Array.Empty<Reading>())
                notebook.Observe("stone:" + reading.inscriptionId, "inscription:v1", reading.glyphIds);
        }
    }
}
