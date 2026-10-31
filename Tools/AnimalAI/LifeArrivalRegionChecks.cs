#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.Life;
using Object = UnityEngine.Object;

public static class LifeArrivalRegionChecks
{
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static string[] Run()
    {
        Require(Application.isBatchMode && Application.dataPath.Contains("ValidationProject") && !Application.isPlaying,
            "Arrival checks use only the isolated batch Edit-mode project");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var results = new List<string>();
        var player = new GameObject("Arrival player").transform;
        GameObject wall = null, worldHost = null;
        try
        {
            Vector3 target = new Vector3(2.2f, 0, 0);
            Require(Vector3.Distance(player.position, target) > 1.15f && LifeArrivalRegion.Contains(player, target),
                "A clear position reasonably near a tree should succeed outside the old precision circle");
            Require(!LifeArrivalRegion.Contains(player, new Vector3(2.6f, 0, 0)) &&
                !LifeArrivalRegion.Contains(player, new Vector3(0, 1.6f, 0)), "Far or another-floor positions must fail");
            results.Add("PASS a clear 2.2m arrival is accepted; 2.6m and another floor are not");
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(1.1f, .8f, 0);
            wall.transform.localScale = new Vector3(.2f, 2, 2);
            Physics.SyncTransforms();
            Require(!LifeArrivalRegion.Contains(player, target), "A nearby point behind a real wall must not complete arrival");
            wall.GetComponent<Collider>().isTrigger = true; Physics.SyncTransforms();
            Require(LifeArrivalRegion.Contains(player, target), "A trigger-only interaction zone is not a wall");
            wall.GetComponent<Collider>().isTrigger = false;
            var body = player.gameObject.AddComponent<BoxCollider>(); body.center = new Vector3(.4f, .8f, 0); body.size = Vector3.one * .2f;
            Physics.SyncTransforms();
            Require(!LifeArrivalRegion.Contains(player, target), "Ignoring the player's body must not skip the wall behind it");
            Object.DestroyImmediate(wall); wall = null; Physics.SyncTransforms();
            Require(LifeArrivalRegion.Contains(player, target), "The player's own body must not block arrival");
            results.Add("PASS wall occlusion remains after an ignored self hit; trigger-only volumes do not obstruct");
            Require(!LifeArrivalRegion.Contains(player, target, float.NaN) && !LifeArrivalRegion.Contains(player, target, 0) &&
                !LifeArrivalRegion.Contains(player, new Vector3(float.NaN, 0, 0)), "Invalid arrival geometry is rejected");
            results.Add("PASS invalid geometry cannot count as arrival");
            worldHost = new GameObject("Inactive arrival world"); worldHost.SetActive(false);
            var world = worldHost.AddComponent<LifeWorld>();
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var anchors = (Dictionary<string, Vector3>)typeof(LifeWorld).GetField("anchors", flags).GetValue(world);
            anchors.Add("first", Vector3.zero); anchors.Add("last", new Vector3(3.6f, 0, 0));
            float radius = (float)typeof(LifeWorld).GetMethod("SequenceRegionRadius", flags).Invoke(world, new object[] { "first", "last" });
            Require(radius > 1.15f && radius * 2 < 3.6f, "Two-step regions should be wider without overlapping");
            player.position = new Vector3(1.8f, 0, 0); Physics.SyncTransforms();
            Require(!LifeArrivalRegion.Contains(player, anchors["first"], radius) &&
                !LifeArrivalRegion.Contains(player, anchors["last"], radius), "Standing between the two goals cannot satisfy both steps");
            Require(!(bool)typeof(LifeWorld).GetMethod("PlayerInArrivalRegion", flags).Invoke(world, new object[] { "missing", 2.5f }),
                "A missing target does not silently fall back to the world origin");
            results.Add("PASS two-step destinations stay separate and an absent target never becomes a valid origin");
        }
        finally
        {
            if (wall) Object.DestroyImmediate(wall);
            if (worldHost) Object.DestroyImmediate(worldHost);
            Object.DestroyImmediate(player.gameObject);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
        return results.ToArray();
    }
}
#endif
