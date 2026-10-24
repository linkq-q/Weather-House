using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.WildlifeJourney.Editor
{
    public static class JourneyEcologyChecks
    {
        public static void Batch()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (!Application.isBatchMode || !File.Exists(Path.Combine(project, ".wildlife-journey-validation")))
                throw new InvalidOperationException("Use the independent validation project.");
            string path = Path.Combine(project, "WildlifeJourneyReview", "ecology-checks.txt");
            try { File.WriteAllText(path, RunLogicChecks()); }
            catch (Exception error) { File.WriteAllText(path, "FAIL\n" + error); throw; }
        }

        public static string RunLogicChecks()
        {
            using (var fixture = new Fixture(false))
            {
                Require(fixture.herd.BeginJourney(1), "Could not start open-land travel.");
                bool chatted = false, stillTravelling = false;
                for (int i = 0; i < 180; i++)
                {
                    fixture.herd.Simulate(.1f);
                    if (fixture.social.ActiveConversation == null) continue;
                    chatted = true;
                    stillTravelling |= !fixture.herd.members[0].Arrived && !fixture.herd.members[1].Arrived;
                }
                Require(chatted && stillTravelling, "Nearby walking sheep never paused to exchange a greeting before arrival.");
                for (int i = 0; i < 1600 && fixture.herd.Travelling; i++) fixture.herd.Simulate(.1f);
                Require(!fixture.herd.Travelling && fixture.herd.CompletedJourneys == 1, "A roadside greeting prevented reunion.");
            }
            using (var fixture = new Fixture(true))
            {
                Require(fixture.herd.BeginJourney(1), "Could not start bridge approach travel.");
                for (int i = 0; i < 45; i++)
                {
                    fixture.herd.Simulate(.1f);
                    Require(fixture.social.FirstParticipantId < 0, "Sheep stopped to chat on the bridge approach.");
                }
            }
            return "PASS roadside conversation before arrival, route resume and reunion; bridge approach remains conversation-free";
        }

        private sealed class Fixture : IDisposable
        {
            public readonly JourneyHerd herd;
            public readonly JourneySocial social;
            private readonly Scene scene;
            private readonly GameObject root;
            public Fixture(bool bridge)
            {
                scene = EditorSceneManager.NewPreviewScene();
                root = new GameObject("Ecology integration check");
                SceneManager.MoveGameObjectToScene(root, scene);
                var nav = root.AddComponent<JourneyNavigation>();
                nav.surfaceSampler = p => new JourneySurface { valid = true, bridgeId = bridge && p.x >= 4 && p.x <= 12 ? 1 : 0 };
                nav.nodes = new[] { new JourneyNode { position = Vector3.zero }, new JourneyNode { position = new Vector3(40, 0, 0) } };
                nav.links = new[] { new JourneyLink { from = 0, to = 1 } };
                nav.Rebuild();
                social = root.AddComponent<JourneySocial>(); social.occlusionMask = 0;
                herd = root.AddComponent<JourneyHerd>();
                herd.navigation = nav; herd.social = social; herd.autoAdvance = false;
                herd.sites = new[] { new JourneyActivitySite { center = Vector3.zero, radius = 3 },
                    new JourneyActivitySite { center = new Vector3(40, 0, 0), radius = 3 } };
                herd.members = new JourneyMember[2];
                for (int i = 0; i < 2; i++)
                {
                    var animal = new GameObject("Walking sheep " + i);
                    SceneManager.MoveGameObjectToScene(animal, scene);
                    animal.transform.SetParent(root.transform);
                    animal.transform.position = new Vector3(0, 0, i * 1.5f);
                    herd.members[i] = new JourneyMember { id = i + 1, root = animal.transform, bodyLength = 1,
                        profile = new JourneyProfile { bodyRadius = .25f, canSwim = false, walkSpeed = .85f } };
                }
                herd.Initialize();
            }
            public void Dispose() { Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
