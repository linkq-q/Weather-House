// Read-only runtime probes; stage into an Editor folder with the regression harness.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.Life;

public static class AnimalBehaviorDiagnostics
{
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Field(object value, string name) => value?.GetType().GetField(name, Hidden)?.GetValue(value);
    static string PathOf(Transform value)
    {
        if (!value) return "none";
        string path = value.name;
        for (Transform parent = value.parent; parent; parent = parent.parent) path = parent.name + "/" + path;
        return path;
    }
    static string ColliderDescription(Collider hit)
        => !hit ? "none or fixed query buffer overflow" : PathOf(hit.transform) + " [" + hit.GetType().Name +
            "; layer=" + hit.gameObject.layer + "; center=" + hit.bounds.center.ToString("F3") + "; size=" + hit.bounds.size.ToString("F3") + "]";
    public static void CaptureSelected(AnimalAutonomy director, Transform player, string output)
    {
        var lines = new List<string> { "READ-ONLY physical movement diagnostics at t=" + Time.time.ToString("F3") };
        var animals = (IList)Field(director, "animals");
        foreach (object state in animals)
        {
            string id = (string)Field(state, "id");
            if (!id.EndsWith("/rabbit/12", StringComparison.Ordinal) && !id.EndsWith("/sheep/6101", StringComparison.Ordinal)) continue;
            lines.AddRange(Describe(director, state, player));
        }
        File.WriteAllLines(output, lines);
    }
    public static IEnumerable<string> Describe(AnimalAutonomy director, object state, Transform player)
    {
        var agent = (WildlifeAgent)Field(state, "agent");
        if (!agent || !agent.Population) { yield return "No ordinary WildlifeAgent for diagnostic"; yield break; }
        WildlifePopulation population = agent.Population;
        Vector3 origin = agent.transform.position;
        Quaternion rotation = agent.transform.rotation;
        WildlifeBodyEnvelope body = agent.BodyEnvelope;
        yield return "ANIMAL " + Field(state, "id") + "; action=" + Field(state, "action") + "; state=" + agent.State +
            "; root=" + origin.ToString("F4") + "; rotation=" + rotation.eulerAngles.ToString("F3") + "; localScale=" + agent.transform.localScale.ToString("F4") +
            "; hasLifeTarget=" + agent.HasLifeTarget + "; autonomous=" + agent.AutonomousTarget + "; destination=" + agent.Destination.ToString("F3") +
            "; profile radius=" + agent.Radius.ToString("F4") + "; body.center=" + body.center.ToString("F4") + "; half=" + body.half.ToString("F4") + "; measured=" + body.measured;
        yield return "POPULATION " + PathOf(population.transform) + "; obstacleMask=" + population.obstacleMask.value +
            "; worldBounds=" + population.worldBounds + "; ground=" + population.GroundHeight(origin).ToString("F3") +
            "; terrain=" + (population.terrain ? population.terrain.name : "none") + "; water=" + (population.waterSurface ? population.waterSurface.name : "none");
        var overlaps = Physics.OverlapBox(origin + rotation * body.center, body.half, rotation, population.obstacleMask, QueryTriggerInteraction.Ignore);
        yield return "INITIAL_OVERLAPS count=" + overlaps.Length;
        for (int i = 0; i < Mathf.Min(16, overlaps.Length); i++)
        {
            Collider hit = overlaps[i];
            yield return "  " + ColliderDescription(hit) + "; ignoredTerrain=" + (hit is TerrainCollider) +
                "; ignoredOwn=" + hit.transform.IsChildOf(agent.transform) + "; ignoredWildlife=" + (bool)hit.GetComponentInParent<WildlifeAgent>() +
                "; ignoredPlayer=" + (population.PlayerCollision && population.PlayerCollision.Owns(hit));
        }
        var physics = Field(population, "bodyPhysics");
        FieldInfo observer = physics.GetType().GetField("ObstructionObserved", Hidden);
        object previous = observer.GetValue(physics); Collider lastHit = null; bool rejected = false;
        Action<Collider> observe = hit => { rejected = true; lastHit = hit; };
        observer.SetValue(physics, observe);
        try
        {
            string Probe(string label, Vector3 from, Vector3 target, Quaternion facing, bool keepFacing)
            {
                rejected = false; lastHit = null;
                bool projection = population.TryProjectPosition(agent, target, out Vector3 projected);
                bool accepted = keepFacing ? population.TryMove(agent, target, out Vector3 _, facing) : population.TryMoveFrom(agent, from, facing, target, out _);
                return label + "; from=" + from.ToString("F3") + "; to=" + target.ToString("F3") + "; projected=" + projection + " " + projected.ToString("F3") +
                    "; accepted=" + accepted + "; staticBodyRejected=" + rejected + "; collider=" + (rejected ? ColliderDescription(lastHit) : "none") +
                    "; playerAllows=" + (!population.PlayerCollision || population.PlayerCollision.AllowsAnimalMove(from, projected, agent.Radius, body));
            }
            yield return Probe("HOLD_CURRENT_FACING", origin, origin, rotation, true);
            Vector3 goal = (Vector3)Field(state, "goal");
            yield return Probe("CURRENT_GOAL", origin, goal, rotation, false);
            if (player)
            {
                Vector3 away = origin - player.position; away.y = 0;
                Vector3 approach = player.position + away.normalized * (2.6f + agent.Radius);
                yield return Probe("APPROACH_GOAL", origin, approach, rotation, false);
            }
            for (int i = 0; i < 8; i++)
            {
                Vector3 direction = new Vector3(Mathf.Cos(i * Mathf.PI / 4), 0, Mathf.Sin(i * Mathf.PI / 4));
                Vector3 target = origin + direction * .15f;
                yield return Probe("STEP_" + i + "_KEEP_ROTATION", origin, target, rotation, true);
                yield return Probe("STEP_" + i + "_FACE_MOVEMENT", origin, target, rotation, false);
            }
            var candidates = new List<Vector3>();
            object query = Field(state, "shelterQuery");
            if (query != null)
            {
                yield return "SHELTER_QUERY probe=" + Field(query, "probe") + "; candidateIndex=" + Field(query, "candidate") +
                    "; target=" + Field(query, "target") + "; routeQueries=" + ((AnimalLocalRoute)Field(query, "route"))?.Queries;
                if (Field(query, "candidates") is List<Vector3> queryCandidates) candidates.AddRange(queryCandidates);
            }
            else
            {
                var world = (LifeWorld)Field(director, "world");
                foreach (LifeObject obj in world.Objects)
                    if (obj && obj.State.sheltered && !obj.State.held) candidates.Add(obj.transform.position);
                foreach (object shelter in (IEnumerable)Field(director, "shelters"))
                {
                    Transform source = (Transform)Field(shelter, "source");
                    if (source && source.gameObject.activeInHierarchy) candidates.Add((Vector3)shelter.GetType().GetProperty("Point").GetValue(shelter));
                }
            }
            candidates.Sort((a, b) => (a - origin).sqrMagnitude.CompareTo((b - origin).sqrMagnitude));
            yield return "SHELTER_CANDIDATES total=" + candidates.Count + "; showing nearest 12; failedCache=" + ((IDictionary)Field(state, "failedShelters")).Count;
            for (int i = 0; i < Mathf.Min(12, candidates.Count); i++)
            {
                Vector3 target = candidates[i]; target.y = population.GroundHeight(target);
                yield return Probe("SHELTER_" + i + "_ENDPOINT", target, target, rotation, false);
                yield return Probe("SHELTER_" + i + "_DIRECT_ROUTE", origin, target, rotation, false);
            }
        }
        finally { observer.SetValue(physics, previous); }
    }
}
