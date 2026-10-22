using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    public static class GrassLiveChecks
    {
        const string Report = "VegetationLabReview/live-grass-checks.txt";

        public static IEnumerator Verify(GrassMeadow source)
        {
            Assert(Application.isPlaying && source, "Checks require Play mode and a configured meadow");
            Directory.CreateDirectory("VegetationLabReview");
            File.WriteAllText(Report, "Grass live checks use the real GrassMeadow.Update in Play mode.\n");
            var go = new GameObject("Grass Live Check") { hideFlags = HideFlags.DontSave };
            go.SetActive(false);
            go.transform.position = Vector3.down * 1000;
            var test = go.AddComponent<GrassMeadow>();
            test.species = (GrassSpecies[])source.species.Clone();
            test.materials = (Material[])source.materials.Clone();
            test.weights = new[] { 60f, 30f, 10f };
            test.totalClumps = 4096;
            test.seed = 731;
            string[] originalSpecies = source.species.Select(JsonUtility.ToJson).ToArray();
            int id = test.GetInstanceID();
            try
            {
                go.SetActive(true);
                IEnumerator wait = Converge(test, "initial 4096 clumps");
                while (wait.MoveNext()) yield return null;
                Assert(test.species[0] != source.species[0] && test.materials[0] != source.materials[0], "Play clones are isolated");
                Mesh[] untouched = SpeciesMeshes(test, 1).Concat(SpeciesMeshes(test, 2)).ToArray();
                Mesh first = SpeciesMeshes(test, 0)[0];
                Vector3[] originalVertices = first.vertices;
                float originalHeight = test.species[0].height;
                test.species[0].height = originalHeight + .25f;
                float deadline = Time.realtimeSinceStartup + 20;
                while (SpeciesMeshes(test, 0)[0] == first)
                {
                    Assert(Time.realtimeSinceStartup < deadline, "A direct shape edit starts replacing geometry");
                    yield return null;
                }
                Assert(test.IsLiveUpdating, "Large shape edit is distributed across frames");
                test.species[0].height = originalHeight;
                wait = Converge(test, "cancel partial shape edit by returning to original value");
                while (wait.MoveNext()) yield return null;
                SameVertices(originalVertices, SpeciesMeshes(test, 0)[0].vertices, "Returning to the old value removes partial stale geometry");
                Assert(untouched.SequenceEqual(SpeciesMeshes(test, 1).Concat(SpeciesMeshes(test, 2))), "A shape edit preserves the other two species meshes");

                Mesh originalTall = SpeciesMeshes(test, 1)[0];
                bool changedDuringDrag = false;
                for (int drag = 0; drag < 12; drag++)
                {
                    test.species[1].height = .45f + drag * .025f;
                    wait = Frames(1); while (wait.MoveNext()) yield return null;
                    changedDuringDrag |= SpeciesMeshes(test, 1)[0] != originalTall;
                }
                Assert(changedDuringDrag, "Tall-grass meshes update during continuous dragging, before release");
                wait = Converge(test, "continuous tall-grass dragging");
                while (wait.MoveNext()) yield return null;

                Mesh[] originalFlowers = SpeciesMeshes(test, 2);
                int originalFlowerCount = test.Counts[2];
                bool flowersChangedDuringDrag = false;
                test.totalClumps = 28000;
                for (int drag = 0; drag < 24; drag++)
                {
                    test.weights = new[] { 90f, 8f, 2f + drag * .25f };
                    wait = Frames(1); while (wait.MoveNext()) yield return null;
                    flowersChangedDuringDrag |= !originalFlowers.SequenceEqual(SpeciesMeshes(test, 2)) && test.Counts[2] != originalFlowerCount;
                }
                Assert(flowersChangedDuringDrag, "White flower meshes AND population respond while proportions keep changing at 28000 clumps");
                Note("PASS continuous white-flower proportion dragging: visible population updates before release at 28000 requested clumps");

                test.totalClumps = 5000; test.seed = 991;
                test.weights = new[] { 10f, 20f, 70f }; test.species[0].height = .8f;
                wait = Frames(1); while (wait.MoveNext()) yield return null;
                test.totalClumps = 777; test.seed = 992;
                test.weights = new[] { 0f, 1f, 0f }; test.species[0].height = .3f;
                wait = Frames(1); while (wait.MoveNext()) yield return null;
                test.totalClumps = 600; test.seed = 993;
                test.weights = new[] { 50f, 25f, 25f };
                test.species[0].height = .62f; test.species[0].width = .047f;
                test.species[2].flowerRadius = .12f;
                wait = Converge(test, "continuous density, weights, seed and shape edits: latest wins");
                while (wait.MoveNext()) yield return null;
                Assert(test.Counts.SequenceEqual(new[] { 300, 150, 150 }), "Latest exact population and proportions");
                var liveVertices = Enumerable.Range(0, 3).Select(k => SpeciesMeshes(test, k).Select(m => m.vertices).ToArray()).ToArray();
                // Compare with the existing production rebuild, without a duplicate placement algorithm in the check.
                test.Rebuild(false);
                wait = Frames(2); while (wait.MoveNext()) yield return null;
                for (int k = 0; k < 3; k++)
                {
                    Mesh[] canonical = SpeciesMeshes(test, k);
                    Assert(liveVertices[k].Length == canonical.Length, "Live and saved rebuild chunk counts agree");
                    for (int i = 0; i < canonical.Length; i++) SameVertices(liveVertices[k][i], canonical[i].vertices, "Latest live geometry equals production rebuild");
                }
                Note("PASS latest geometry matches synchronous production rebuild vertex-for-vertex");

                Mesh[] beforeClear = AllMeshes(test);
                test.weights = new[] { 0f, 0f, 0f };
                wait = Converge(test, "all weights zero clears all grass");
                while (wait.MoveNext()) yield return null;
                Assert(test.Counts.All(n => n == 0) && AllMeshes(test).Length == 0, "All-zero proportions clear meshes and counts");
                Assert(beforeClear.All(m => !m), "Cleared owned meshes are destroyed");
                test.totalClumps = 257; test.weights = new[] { 0f, 1f, 0f };
                wait = Converge(test, "repopulate only tall grass");
                while (wait.MoveNext()) yield return null;
                Assert(test.Counts.SequenceEqual(new[] { 0, 257, 0 }) && AllMeshes(test).Length == 3, "Single species repopulates exactly");
                Assert(test.transform.Cast<Transform>().Count(t => t.name == "Generated Grass") == 1, "Only one generated root");
                Assert(OwnedLiveMeshes(id).Length == AllMeshes(test).Length, "No retired or temporary live meshes remain");
                for (int k = 0; k < 3; k++) Assert(JsonUtility.ToJson(source.species[k]) == originalSpecies[k], "Source species are unchanged");
                Mesh[] finalMeshes = AllMeshes(test);
                Material[] finalMaterials = (Material[])test.materials.Clone();
                GrassSpecies[] finalSpecies = (GrassSpecies[])test.species.Clone();
                Object.Destroy(go);
                wait = Frames(3); while (wait.MoveNext()) yield return null;
                Assert(!go && finalMeshes.All(m => !m) && finalMaterials.All(m => !m) && finalSpecies.All(s => !s), "Destroy cleans root, meshes and Play clones");
                Assert(OwnedLiveMeshes(id).Length == 0, "No live mesh survives component destruction");
                Note("PASS cancellation; latest parameters; species isolation; exact proportions; clear/repopulate; mesh/root/clone cleanup");
            }
            finally
            {
                if (go) { go.SetActive(false); Object.Destroy(go); }
            }
        }

        static IEnumerator Converge(GrassMeadow meadow, string label)
        {
            int frame = Time.frameCount;
            float started = Time.realtimeSinceStartup;
            do
            {
                Assert(string.IsNullOrEmpty(meadow.LiveError), meadow.LiveError);
                Assert(Time.realtimeSinceStartup - started < 45, label + " timed out");
                yield return null;
            } while (Time.frameCount - frame < 2 || meadow.IsLiveUpdating);
            Assert(string.IsNullOrEmpty(meadow.LiveError), meadow.LiveError);
            Assert(meadow.RequestedRevision == meadow.CompletedRevision, label + " revision converged");
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
        static Mesh[] AllMeshes(GrassMeadow meadow) { return Enumerable.Range(0, 3).SelectMany(k => SpeciesMeshes(meadow, k)).ToArray(); }
        static Mesh[] OwnedLiveMeshes(int id)
        {
            return Resources.FindObjectsOfTypeAll<Mesh>().Where(m => m.name.StartsWith("LiveGrass_" + id + "_") || m.name.StartsWith("LiveGrassVariant_" + id + "_")).ToArray();
        }
        static void SameVertices(Vector3[] expected, Vector3[] actual, string message)
        {
            Assert(expected.Length == actual.Length, message + " (vertex count)");
            for (int i = 0; i < expected.Length; i++) Assert((expected[i] - actual[i]).sqrMagnitude < .00000001f, message + " at vertex " + i);
        }
        static void Assert(bool passed, string message)
        {
            if (!passed) { Note("FAIL " + message); throw new InvalidOperationException("Grass live check: " + message); }
        }
        static void Note(string message) { File.AppendAllText(Report, message + "\n"); }
    }
}
