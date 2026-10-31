#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

namespace WeatherHouse.Life.Editor
{
    // Run only in the isolated validation project; this fixture never saves a scene.
    public static class InscriptionChecks
    {
        static readonly List<string> checks = new List<string>();
        const int Layer = 31;

        public static void RunBatch()
        {
            string folder = Environment.GetEnvironmentVariable("WEATHERHOUSE_INSCRIPTION_REPORT");
            if (string.IsNullOrWhiteSpace(folder)) folder = "Temp/InscriptionChecks";
            Directory.CreateDirectory(folder);
            int exit = 0;
            try { Run(); checks.Add("RESULT failed=0"); }
            catch (Exception error) { exit = 1; checks.Add("FAIL " + error); Debug.LogException(error); }
            File.WriteAllLines(Path.Combine(folder, "inscription-checks.txt"), checks);
            EditorApplication.Exit(exit);
        }

        public static void Run()
        {
            checks.Clear();
            var catalog = JsonUtility.FromJson<GlyphCatalog>(Resources.Load<TextAsset>("WeatherHouseGlyphs").text);
            Check(new[] { "look", "sea", "past", "like" }.All(id => catalog.glyphs.Any(g => g.id == id)), "The real catalog contains all four new letters");
            GameObject stone = null, cameraObject = null, blocker = null;
            Material material = null;
            try
            {
                Vector3 center = new Vector3(0, 600, 0);
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", Color.white);
                stone = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stone.name = "Inscription fixture"; stone.layer = Layer;
                stone.transform.SetPositionAndRotation(center, Quaternion.identity);
                stone.transform.localScale = new Vector3(3, 3, .3f);
                stone.GetComponent<Renderer>().sharedMaterial = material;
                Object.DestroyImmediate(stone.GetComponent<Collider>());
                var inscription = stone.AddComponent<LifeGlyphInscription>();
                inscription.inscriptionId = "fixture.sea";
                inscription.glyphIds = new[] { "sea", "look", "past" };
                inscription.inscriptionRenderer = stone.GetComponent<Renderer>();
                var face = new GameObject("Picture and letters face").transform;
                face.SetParent(stone.transform, false);
                face.SetPositionAndRotation(center + Vector3.back * .151f, Quaternion.Euler(0, 180, 0));
                // readingSize is authored in readingFace local units, including the parent's scale.
                inscription.readingFace = face; inscription.readingSize = new Vector2(2.2f / 3f, 2.2f / 3f);
                cameraObject = new GameObject("Inscription shutter camera");
                Camera camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                camera.cullingMask = 1 << Layer; camera.aspect = 16f / 9f;
                camera.nearClipPlane = .1f; camera.farClipPlane = 250; camera.fieldOfView = 50;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.gray;
                cameraObject.AddComponent<UniversalAdditionalCameraData>().requiresDepthTexture = true;
                void View(Vector3 offset) { camera.transform.position = center + offset; camera.transform.LookAt(center); }
                LifeGlyphInscription.Reading[] Read() => LifeGlyphInscription.Capture(camera, catalog);
                void Rejected(string label) => Check(Read().Length == 0, label);
                View(new Vector3(0, 0, -5));
                LifeGlyphInscription.Reading[] captured = Read();
                Check(captured.Length == 1 && captured[0].glyphIds.SequenceEqual(inscription.glyphIds), "A front-facing readable inscription records its catalog IDs through real rendered depth");
                Check(captured[0].inscriptionId == "fixture.sea", "The reading preserves an explicit stable source ID");
                CheckNotebook(catalog, captured);

                View(new Vector3(2, .8f, -5));
                Check(Read().Length == 1, "A second oblique camera can read the same unobstructed face");
                View(new Vector3(0, 0, 5)); Rejected("Seeing the stone's back does not discover its inscription");
                View(new Vector3(5, 0, -.2f)); Rejected("A grazing edge view does not discover its inscription");
                View(new Vector3(0, 0, -5)); camera.transform.rotation = Quaternion.Euler(0, 100, 0);
                Rejected("An active nearby stone outside the photograph does not count");
                View(new Vector3(0, 0, -5)); camera.orthographic = true; camera.orthographicSize = .8f;
                Rejected("Clipping the inscription rectangle at the image edges does not count");
                camera.orthographic = false;
                View(new Vector3(0, 0, -180)); Rejected("An inscription too small to read is rejected");
                View(new Vector3(0, 0, -5));
                blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.layer = Layer;
                blocker.name = "Opaque blocker without collider";
                blocker.GetComponent<Renderer>().sharedMaterial = material;
                Object.DestroyImmediate(blocker.GetComponent<Collider>());
                blocker.transform.position = center + Vector3.back * 2.5f;
                blocker.transform.localScale = new Vector3(4, 4, .2f);
                Rejected("Rendered foreground geometry blocks learning even without a Collider");
                blocker.transform.localScale = new Vector3(.5f, .5f, .1f);
                blocker.transform.position = center + new Vector3(.47f, .47f, -2.5f);
                Rejected("Covering an outer picture/letter sample is rejected although the stone center remains visible");
                Object.DestroyImmediate(blocker); blocker = null;
                Check(Read().Length == 1, "Removing an obstruction restores inscription visibility");
                inscription.inscriptionRenderer.enabled = false; Rejected("A disabled inscription renderer cannot award letters");
                inscription.inscriptionRenderer.enabled = true;
                inscription.inscriptionRenderer.forceRenderingOff = true; Rejected("A forced-hidden renderer cannot award letters");
                inscription.inscriptionRenderer.forceRenderingOff = false;
                inscription.enabled = false; Rejected("An inactive inscription component is ignored"); inscription.enabled = true;
                camera.cullingMask = 0; Rejected("The camera's excluded layer cannot award letters"); camera.cullingMask = 1 << Layer;
                inscription.glyphIds[0] = "not-in-the-real-catalog"; Rejected("Unknown glyph IDs fail the entire inscription closed");
                Check(captured[0].glyphIds[0] == "sea", "Shutter readings are immutable with respect to later authoring-array edits");
                inscription.glyphIds[0] = "sea";
                inscription.inscriptionId = ""; Rejected("A source without a stable ID cannot record evidence");
                inscription.inscriptionId = "fixture.sea";
                inscription.readingSize = Vector2.zero; Rejected("An unconfigured empty reading rectangle cannot count");
                inscription.readingSize = new Vector2(float.NaN, 1); Rejected("Invalid authored dimensions fail closed");
                Check(captured[0].inscriptionId == "fixture.sea", "Existing shutter source IDs do not follow later component changes");
            }
            finally
            {
                if (blocker) Object.DestroyImmediate(blocker);
                if (cameraObject) Object.DestroyImmediate(cameraObject);
                if (stone) Object.DestroyImmediate(stone);
                if (material) Object.DestroyImmediate(material);
            }
        }

        static void CheckNotebook(GlyphCatalog catalog, LifeGlyphInscription.Reading[] captured)
        {
            var notebook = new LanguageNotebook(); notebook.Normalize(catalog);
            Check(!notebook.GetWord("sea").encountered, "A visible capture candidate alone does not mutate the notebook");
            string[] speech = { "hello", "hello" };
            string[] photograph = LifeGlyphInscription.PhotoGlyphs(speech, captured);
            Check(photograph.SequenceEqual(new[] { "hello", "hello", "sea", "look", "past" }), "The photograph keeps animal speech order/repetition and appends inscription IDs");
            Check(LifeGlyphInscription.PhotoGlyphs(new[] { "sea" }, captured).SequenceEqual(new[] { "sea", "look", "past" }), "Duplicate stone letters are not repeated in photograph metadata");
            var record = new ObservationRecord { id = "inscription-photo", animalId = "animal-one", utteranceId = "greeting", glyphIds = photograph, frameCount = 1 };
            notebook.AddObservation(record, speech);
            Check(!notebook.GetWord("sea").encountered && notebook.GetWord("hello").encountered, "Saving combined metadata only observes the actual animal speech through AddObservation");
            LifeGlyphInscription.Observe(notebook, captured);
            Check(new[] { "sea", "look", "past" }.All(id => notebook.GetWord(id).encountered && !notebook.GetWord(id).confirmed), "Photographed inscriptions reveal letters without confirming or revealing their meaning");
            Check(notebook.encounters.Where(e => e.glyphId == "sea").All(e => e.animalId == "stone:fixture.sea") &&
                !notebook.encounters.Any(e => e.animalId == "animal-one" && e.glyphId != "hello"), "Stone evidence has its own source and is never attributed to the photographed animal");
            Check(!notebook.GetWord("like").encountered, "This three-stone photograph does not discover the animal-only like letter");
            Check(notebook.FindEvidence("sea").Single().id == record.id && notebook.HasPhotoEvidence("past"), "Each inscription letter retains the actual saved photograph as evidence");
            notebook.SetGuess("sea", "海");
            Check(notebook.GetWord("sea").guess == "海" && !notebook.GetWord("sea").confirmed, "Encountered inscription letters permit a player guess without auto-confirming it");
            LifeGlyphInscription.Observe(notebook, captured);
            Check(notebook.encounters.Count(e => e.animalId == "stone:fixture.sea") == 3, "Repeated photographs do not duplicate the same inscription encounter");
            var restored = JsonUtility.FromJson<LanguageNotebook>(JsonUtility.ToJson(notebook)); restored.Normalize(catalog);
            Check(restored.GetWord("sea").encountered && !restored.GetWord("sea").confirmed && restored.GetWord("sea").guess == "海" &&
                restored.FindEvidence("sea").Single().glyphIds.SequenceEqual(photograph), "Saved and restored notebooks retain discovery, guess, source and photograph IDs");
            var old = new LanguageNotebook(); old.Normalize(catalog);
            old.AddObservation(new ObservationRecord { id = "legacy-photo", animalId = "old-animal", utteranceId = "old-speech", glyphIds = new[] { "rain" } });
            Check(old.GetWord("rain").encountered && old.encounters.Single().animalId == "old-animal", "Existing AddObservation callers retain their original speech observation behavior");
            var stoneOnly = new LanguageNotebook(); stoneOnly.Normalize(catalog);
            stoneOnly.AddObservation(new ObservationRecord { id = "stone-only", animalId = "", utteranceId = "", glyphIds = LifeGlyphInscription.PhotoGlyphs(null, captured), frameCount = 1 }, Array.Empty<string>());
            LifeGlyphInscription.Observe(stoneOnly, captured);
            Check(stoneOnly.GetWord("sea").encountered && stoneOnly.HasPhotoEvidence("sea"), "A photographed stone is discoverable without a nearby animal or visible speech");
        }

        static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            checks.Add("PASS " + label);
        }
    }
}
#endif
