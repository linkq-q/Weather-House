using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.WildlifeJourney;
using WeatherHouse.WildlifeJourney.Editor;

namespace WeatherHouse.NightReview20261027
{
    // Explicit Edit-only preview fixture. No observer, import, refresh, production mutation or SaveScene.
    public static class JourneyReunionDiagnostic20261027
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const string Folder = "LifeGameplayReview/20261027/journey-reunion";
        [Serializable] public sealed class MemberRow
        {
            public int id, segment, routeCount, bridge;
            public bool arrived, active, lifeTarget;
            public string state, mode, decision;
            public Vector3 position, destination;
            public float speed, nextChoice, retryAt, blockedSince;
        }
        [Serializable] public sealed class Row
        {
            public int journey, tick, expected, target, completed, planningRequests, planningCompletions, planningCancellations;
            public float simulationTime, decisionAt, journeyStarted;
            public bool travelling, planning, recovery, allArrived, notTravellingPredicate, targetPredicate, arrivedPredicate;
            public string groupDecision, pending;
            public List<MemberRow> members = new List<MemberRow>();
        }
        [Serializable] public sealed class Result
        {
            public string status, utc, error, beforeHash, afterHash, sourceHash, boundary;
            public bool dirtyBefore, dirtyAfter;
            public List<Row> trace = new List<Row>(), endings = new List<Row>();
        }

        [MenuItem("Weather House/Review 20261027/Journey reunion diagnostic")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Stable Edit mode required; this diagnostic never stops Play or refreshes.");
            Scene originalScene = SceneManager.GetActiveScene();
            int originalSceneCount = SceneManager.sceneCount;
            var result = new Result { status = "RUNNING", utc = DateTime.UtcNow.ToString("O"), dirtyBefore = originalScene.isDirty,
                beforeHash = MainHash(originalScene), sourceHash = Hash(File.ReadAllBytes("Assets/Game/World/Wildlife/Journey/Runtime/JourneyHerd.cs")),
                boundary = "Exact existing two-swimmer preview fixture: four journeys, 3000 ticks per journey, Simulate(.1f), same speeds and original RequireArrived. No extra settling ticks or changed thresholds." };
            object fixture = null; Exception failure = null;
            string output = Path.Combine(Folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(output);
            try
            {
                Type fixtureType = typeof(JourneyHerdChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
                if (fixtureType == null) throw new MissingMemberException("Existing JourneyHerdChecks.Fixture was not found.");
                fixture = Activator.CreateInstance(fixtureType, true);
                var herd = (JourneyHerd)Get(fixture, "herd");
                herd.members = herd.members.Take(2).ToArray();
                herd.members[0].root.position = JourneyLabBuilder.Position(-5.1f, 0);
                herd.members[1].root.position = JourneyLabBuilder.Position(-6.7f, 0);
                herd.members[0].profile.swimSpeed = .3f; herd.members[1].profile.swimSpeed = 1.4f;
                herd.members[0].profile.walkSpeed = herd.members[1].profile.walkSpeed = .55f;
                herd.Initialize();
                MethodInfo arrived = typeof(JourneyHerdChecks).GetMethod("RequireArrived", BindingFlags.Static | BindingFlags.NonPublic);
                if (arrived == null) throw new MissingMemberException("Original RequireArrived assertion not found.");
                for (int journey = 0; journey < 4; journey++)
                {
                    int expected = journey % 2 == 0 ? 1 : 0;
                    if (!herd.BeginJourney(expected)) throw new InvalidOperationException("Original two-swimmer journey could not start: " + (journey + 1));
                    string lastState = null;
                    for (int tick = 0; tick < 3000; tick++)
                    {
                        herd.Simulate(.1f);
                        string state = herd.Travelling + "/" + herd.TargetSite + "/" + herd.CompletedJourneys + "/" + herd.IsPlanning + "/" +
                            string.Join(";", herd.members.Select(m => m.Arrived + "/" + m.State + "/" + m.Mode + "/" + m.LastDecision));
                        if (tick % 10 == 0 || state != lastState) result.trace.Add(Sample(herd, journey + 1, tick, expected));
                        lastState = state;
                    }
                    result.endings.Add(Sample(herd, journey + 1, 3000, expected));
                    arrived.Invoke(null, new object[] { herd, expected });
                }
                result.status = "PASS_FOUR_JOURNEY_DIAGNOSTIC_NOT_FULL_REGRESSION";
            }
            catch (Exception e)
            {
                failure = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                result.status = "RECORDED_NATIVE_FAILURE_ASSERTION_PRESERVED"; result.error = failure.ToString();
            }
            finally
            {
                (fixture as IDisposable)?.Dispose();
                result.dirtyAfter = originalScene.isDirty; result.afterHash = MainHash(originalScene);
                if (SceneManager.sceneCount != originalSceneCount || SceneManager.GetActiveScene().handle != originalScene.handle ||
                    result.beforeHash != result.afterHash || result.dirtyBefore != result.dirtyAfter)
                {
                    failure = new InvalidOperationException("Diagnostic scene/dirty/transform preservation guard failed.");
                    result.status = "FAIL_SCENE_GUARD"; result.error = failure.ToString();
                }
                File.WriteAllText(Path.Combine(output, "result.json"), JsonUtility.ToJson(result, true));
                File.WriteAllText(Path.Combine(Folder, "latest.txt"), output);
            }
            if (failure != null) throw new InvalidOperationException("Journey diagnostic preserved failure; see " + output, failure);
            Debug.Log("Journey diagnostic recorded: " + output);
        }

        static Row Sample(JourneyHerd herd, int journey, int tick, int expected)
        {
            object pending = Get(herd, "pending");
            var row = new Row { journey = journey, tick = tick, expected = expected, target = herd.TargetSite, completed = herd.CompletedJourneys,
                simulationTime = herd.SimulationTime, travelling = herd.Travelling, planning = herd.IsPlanning, groupDecision = herd.GroupDecision,
                recovery = (bool)Get(herd, "recovery"), decisionAt = (float)Get(herd, "decisionAt"), journeyStarted = (float)Get(herd, "journeyStarted"),
                allArrived = herd.members.All(m => m.Arrived), notTravellingPredicate = !herd.Travelling,
                targetPredicate = herd.TargetSite == expected, arrivedPredicate = herd.members.All(m => m.Arrived),
                planningRequests = herd.PlanningRequests, planningCompletions = herd.PlanningCompletions,
                planningCancellations = herd.PlanningCancellations, pending = pending == null ? "none" :
                    "site=" + Get(pending, "site") + ", member=" + Get(pending, "member") + ", time=" + Get(pending, "time") + ", recovery=" + Get(pending, "recovery") };
            foreach (JourneyMember member in herd.members)
            {
                var route = (JourneyRoute)Get(member, "route");
                row.members.Add(new MemberRow { id = member.id, arrived = member.Arrived, active = member.root.gameObject.activeInHierarchy,
                    lifeTarget = herd.HasLifeTarget(member.id), state = member.State.ToString(), mode = member.Mode.ToString(), decision = member.LastDecision,
                    position = member.root.position, destination = (Vector3)Get(member, "destination"), speed = member.ActualSpeed,
                    segment = (int)Get(member, "segment"), routeCount = route == null ? -1 : route.segments.Count, bridge = (int)Get(member, "bridge"),
                    nextChoice = (float)Get(member, "nextChoice"), retryAt = (float)Get(member, "retryAt"), blockedSince = (float)Get(member, "blockedSince") });
            }
            return row;
        }
        static object Get(object instance, string name)
        {
            FieldInfo field = instance.GetType().GetField(name, Fields);
            if (field == null) throw new MissingFieldException(instance.GetType().FullName, name);
            return field.GetValue(instance);
        }
        static string MainHash(Scene scene)
        {
            var text = new StringBuilder(scene.path);
            foreach (Transform t in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).OrderBy(t => t.GetInstanceID()))
                text.Append('|').Append(t.GetInstanceID()).Append(':').Append(t.parent ? t.parent.GetInstanceID() : 0).Append(':')
                    .Append(t.gameObject.activeSelf).Append(':').Append(t.localPosition.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(t.localRotation.ToString("R", CultureInfo.InvariantCulture)).Append(':').Append(t.localScale.ToString("R", CultureInfo.InvariantCulture));
            return Hash(Encoding.UTF8.GetBytes(text.ToString()));
        }
        static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
    }
}
