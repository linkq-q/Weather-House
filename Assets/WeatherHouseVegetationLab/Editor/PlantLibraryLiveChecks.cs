using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    public static class PlantLibraryLiveChecks
    {
        const string Report = "VegetationLabReview/plant-library-live-checks.txt";

        public static IEnumerator Verify(GrassMeadow source)
        {
            Directory.CreateDirectory("VegetationLabReview");
            File.WriteAllText(Report, "Plant library checks use the real GrassMeadow.Update in Play mode.\n");
            Assert(Application.isPlaying && source && source.SpeciesCount == 19, "A configured 19-preset source is required in Play mode");
            Assert(source.ConfigurationError == null, source.ConfigurationError);
            string[] originalSpecies = source.species.Select(JsonUtility.ToJson).ToArray();
            var go = new GameObject("Plant Library Live Check") { hideFlags = HideFlags.DontSave };
            go.SetActive(false);
            go.transform.position = Vector3.down * 1000;
            var test = go.AddComponent<GrassMeadow>();
            test.species = (GrassSpecies[])source.species.Clone();
            test.materials = (Material[])source.materials.Clone();
            test.weights = Enumerable.Repeat(1f, 19).ToArray();
            test.totalClumps = 380;
            test.seed = 842;
            int id = test.GetInstanceID();
            try
            {
                go.SetActive(true);
                IEnumerator wait = Converge(test, "all 19 slots generate");
                while (wait.MoveNext()) yield return null;
                Assert(test.Counts.Length == 19 && test.Counts.All(n => n == 20), "All 19 species receive exact equal counts");
                Assert(AllMeshes(test).Length == 19, "Each species has a live mesh");
                Assert(test.species[18] != source.species[18] && test.materials[18] != source.materials[18], "Last slot has independent Play clones");
                for (int preset = 0; preset < 3; preset++)
                {
                    test.SetMixPreset(preset);
                    Assert(test.weights.Length == 19 && test.weights.All(w => w > 0), "Mix presets retain every library slot");
                    float grassShare = Enumerable.Range(0, 19).Where(i => GrassMeadow.IsGrass(test.species[i].kind)).Sum(i => test.weights[i]);
                    float expected = preset == 0 ? 85 : preset == 1 ? 95 : 65;
                    Assert(Mathf.Abs(grassShare - expected) < .001f, "Mix presets reserve the intended share for ordinary grass");
                }

                test.weights = new float[19]; test.weights[18] = 1;
                test.totalClumps = 257;
                wait = Converge(test, "last-slot-only distribution");
                while (wait.MoveNext()) yield return null;
                Assert(test.Counts.Take(18).All(n => n == 0) && test.Counts[18] == 257, "Weights affect slot 19 without truncation");
                Assert(SpeciesMeshes(test, 18).Length == 3 && AllMeshes(test).Length == 3, "Only the last species remains");
                Vector3[] beforeShape = SpeciesMeshes(test, 18)[0].vertices;
                for (int edit = 0; edit < 6; edit++)
                {
                    test.species[18].height = .45f + edit * .21f;
                    wait = Frames(1); while (wait.MoveNext()) yield return null;
                }
                wait = Converge(test, "continuous last-slot height edits");
                while (wait.MoveNext()) yield return null;
                Vector3[] afterShape = SpeciesMeshes(test, 18)[0].vertices;
                Assert(!beforeShape.SequenceEqual(afterShape), "The final species visibly changes geometry");

                GrassSpecies last = test.species[18];
                Action[] geometryEdits = {
                    () => last.petalCount = last.petalCount == 12 ? 5 : last.petalCount + 1,
                    () => last.headLength = last.headLength > .4f ? .2f : .6f,
                    () => last.leafAspect = last.leafAspect > 1 ? .5f : 1.6f,
                    () => last.curl = last.curl > .5f ? .1f : .8f,
                    () => test.species[0].outerBladeFraction = test.species[0].outerBladeFraction > .5f ? .1f : .8f,
                    () => test.species[0].clumpLayering = test.species[0].clumpLayering > .5f ? .1f : .8f
                };
                foreach (Action edit in geometryEdits)
                {
                    int revision = test.RequestedRevision;
                    edit();
                    wait = Frames(2); while (wait.MoveNext()) yield return null;
                    Assert(test.RequestedRevision > revision, "Every new geometry field is observed by the real Update");
                }
                wait = Converge(test, "all new geometry fields tracked");
                while (wait.MoveNext()) yield return null;
                Vector3[][] liveVertices = SpeciesMeshes(test, 18).Select(mesh => mesh.vertices).ToArray();
                test.Rebuild(false);
                wait = Frames(2); while (wait.MoveNext()) yield return null;
                Mesh[] canonical = SpeciesMeshes(test, 18);
                Assert(liveVertices.Length == canonical.Length, "Live and synchronous chunk counts agree for slot 19");
                for (int chunk = 0; chunk < canonical.Length; chunk++)
                {
                    Vector3[] vertices = canonical[chunk].vertices;
                    Assert(vertices.Length == liveVertices[chunk].Length, "Slot 19 vertex counts agree");
                    for (int i = 0; i < vertices.Length; i++)
                        Assert((vertices[i] - liveVertices[chunk][i]).sqrMagnitude < .00000001f, "Slot 19 latest live geometry agrees with production rebuild");
                }

                float[] validWeights = test.weights;
                test.weights = new float[3];
                wait = Frames(2); while (wait.MoveNext()) yield return null;
                Assert(!string.IsNullOrEmpty(test.LiveError), "Runtime slot count edits produce an explanatory message");
                test.weights = validWeights;
                wait = Converge(test, "restoring valid slot configuration");
                while (wait.MoveNext()) yield return null;
                Mesh[] beforeClear = AllMeshes(test);
                test.weights = new float[19];
                wait = Converge(test, "clear every library species");
                while (wait.MoveNext()) yield return null;
                Assert(test.Counts.Length == 19 && test.Counts.All(n => n == 0) && AllMeshes(test).Length == 0, "All 19 empty weights clear geometry and counts");
                Assert(beforeClear.All(m => !m), "Clearing destroys replaced runtime meshes");
                test.weights[18] = 1; test.totalClumps = 129;
                wait = Converge(test, "restore slot 19 after clearing");
                while (wait.MoveNext()) yield return null;
                Assert(test.Counts[18] == 129 && AllMeshes(test).Length == 2, "Slot 19 can be restored");
                Assert(test.transform.Cast<Transform>().Count(t => t.name == "Generated Grass") == 1, "One generated root after repeated updates");
                Assert(OwnedMeshes(id).Length == AllMeshes(test).Length, "No obsolete live or variant mesh remains");
                for (int i = 0; i < 19; i++) Assert(JsonUtility.ToJson(source.species[i]) == originalSpecies[i], "Source plant presets remain unchanged");
                Mesh[] finalMeshes = AllMeshes(test);
                Material[] finalMaterials = (Material[])test.materials.Clone();
                GrassSpecies[] finalSpecies = (GrassSpecies[])test.species.Clone();
                Object.Destroy(go);
                wait = Frames(3); while (wait.MoveNext()) yield return null;
                Assert(!go && finalMeshes.All(m => !m) && finalMaterials.All(m => !m) && finalSpecies.All(s => !s), "Destroy releases meshes, materials and all 19 preset clones");
                Assert(OwnedMeshes(id).Length == 0, "No live mesh survives destruction");
                Note("PASS 19 species; complete presets; last-slot weights and shape; new geometry fields; clear/repopulate; runtime configuration guard; resource cleanup");
            }
            finally { if (go) { go.SetActive(false); Object.Destroy(go); } }
        }

        static IEnumerator Converge(GrassMeadow meadow, string label)
        {
            int frame = Time.frameCount;
            float started = Time.realtimeSinceStartup;
            do
            {
                Assert(Time.realtimeSinceStartup - started < 45, label + " timed out");
                yield return null;
            } while (Time.frameCount - frame < 2 || meadow.IsLiveUpdating);
            Assert(string.IsNullOrEmpty(meadow.LiveError), meadow.LiveError);
            Assert(meadow.RequestedRevision == meadow.CompletedRevision, label + " converges to the latest revision");
            IEnumerator cleanup = Frames(2);
            while (cleanup.MoveNext()) yield return null;
            Assert(string.IsNullOrEmpty(meadow.LiveError), meadow.LiveError);
            Note("PASS " + label + ": " + (Time.frameCount - frame) + " frames, " + (Time.realtimeSinceStartup - started).ToString("F3") + " s");
        }
        static IEnumerator Frames(int amount)
        {
            int frame = Time.frameCount;
            float deadline = Time.realtimeSinceStartup + 20;
            while (Time.frameCount - frame < amount)
            {
                Assert(Time.realtimeSinceStartup < deadline, "Play frames advance");
                yield return null;
            }
        }
        static Mesh[] SpeciesMeshes(GrassMeadow meadow, int kind)
        {
            Transform root = meadow.transform.Find("Generated Grass");
            if (!root) return new Mesh[0];
            return root.GetComponentsInChildren<MeshFilter>().Where(f => f.GetComponent<MeshRenderer>().sharedMaterial == meadow.materials[kind]).Select(f => f.sharedMesh).ToArray();
        }
        static Mesh[] AllMeshes(GrassMeadow meadow) { return Enumerable.Range(0, meadow.SpeciesCount).SelectMany(k => SpeciesMeshes(meadow, k)).ToArray(); }
        static Mesh[] OwnedMeshes(int id)
        {
            return Resources.FindObjectsOfTypeAll<Mesh>().Where(m => m.name.StartsWith("LiveGrass_" + id + "_") || m.name.StartsWith("LiveGrassVariant_" + id + "_")).ToArray();
        }
        static void Assert(bool passed, string message)
        {
            if (!passed) { Note("FAIL " + message); throw new InvalidOperationException("Plant library live check: " + message); }
        }
        static void Note(string message) { File.AppendAllText(Report, message + "\n"); }
    }
}
