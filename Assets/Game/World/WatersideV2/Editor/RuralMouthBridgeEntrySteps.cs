using System;
using System.Linq;
using UnityEngine;

namespace WeatherHouse.WatersideV2.Editor
{
    // Authoring only: retain the existing bridge and add three visible bank-side treads.
    public static class RuralMouthBridgeEntrySteps
    {
        public const string HolderName = "河口栈桥 · 东岸接桥木阶";
        const float Tread = .42f, JointOverlap = .015f;

        public static bool Ensure(Transform bridge, Terrain terrain)
        {
            Require(bridge && bridge.name == "河口栈桥" && terrain, "Expected the existing mouth bridge and terrain.");
            Transform board = bridge.Find("踏面 38");
            Require(board && (bridge.lossyScale - Vector3.one).sqrMagnitude < .000001f && Vector3.Dot(bridge.up, Vector3.up) > .99999f,
                "Existing mouth bridge scale or orientation changed.");
            var filter = board.GetComponent<MeshFilter>();
            var renderer = board.GetComponent<MeshRenderer>();
            var box = board.GetComponent<BoxCollider>();
            Require(filter && filter.sharedMesh && renderer && renderer.sharedMaterials.Length > 0 && renderer.sharedMaterials.All(m => m)
                && box && box.enabled && !box.isTrigger && box.center.sqrMagnitude < .000001f
                && (box.size - Vector3.one).sqrMagnitude < .000001f, "Original end tread mesh/material/collider contract changed.");
            Vector3 forward = board.forward;
            Require(Mathf.Abs(forward.y) < .00001f && Vector3.Dot(board.up, Vector3.up) > .99999f, "End tread must be level.");
            Vector3 scale = board.lossyScale;
            Require(Mathf.Abs(scale.x - 2.8f) < .0001f && Mathf.Abs(scale.y - .28f) < .0001f && Mathf.Abs(scale.z - .635f) < .0001f,
                "Original end tread dimensions changed.");
            float deck = board.position.y + scale.y * box.size.y * .5f;
            Vector3 edge = board.position + forward * scale.z * box.size.z * .5f;
            float ground = Ground(terrain, edge + forward * (Tread * 3));
            float rise = deck - ground;
            Transform existing = bridge.Find(HolderName);
            if (rise <= .09f) { Require(!existing, "Existing entry steps do not match the current bank."); return false; }
            Require(rise > .09f && rise <= .36f, "Three bank treads cannot safely cover this height.");
            // Use the actual bank, including footprint corners. Refuse new uneven/wet-low terrain rather than overwrite it.
            for (int along = 0; along <= 3; along++)
                foreach (float side in new[] { -.5f, 0f, .5f })
                {
                    float height = Ground(terrain, edge + forward * (along * Tread) + board.right * (side * scale.x));
                    Require(Mathf.Abs(height - ground) <= .02f && height >= .9f, "The existing dry bank changed; inspect before adding entry steps.");
                }
            var positions = new Vector3[3]; var scales = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                float top = ground + rise * (i + 1) / 4f;
                float thickness = top - ground + .03f;
                Vector3 point = edge + forward * ((2.5f - i) * Tread);
                point.y = top - thickness * .5f;
                positions[i] = point; scales[i] = new Vector3(scale.x, thickness, Tread + JointOverlap);
            }
            if (existing)
            {
                Require(existing.childCount == 3 && existing.localPosition == Vector3.zero && existing.localRotation == Quaternion.identity
                    && existing.localScale == Vector3.one, "Edited entry holder must be preserved, not overwritten.");
                for (int i = 0; i < 3; i++) Verify(existing.GetChild(i), i, positions[i], scales[i], board.rotation, filter.sharedMesh, renderer.sharedMaterials);
                return false;
            }
            var holder = new GameObject(HolderName).transform; holder.SetParent(bridge, false);
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var step = new GameObject("接岸木踏面 " + (i + 1)); step.transform.SetParent(holder, false);
                    step.transform.SetPositionAndRotation(positions[i], board.rotation); step.transform.localScale = scales[i];
                    step.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    var visual = step.AddComponent<MeshRenderer>(); visual.sharedMaterials = renderer.sharedMaterials;
                    visual.shadowCastingMode = renderer.shadowCastingMode; visual.receiveShadows = renderer.receiveShadows;
                    step.AddComponent<BoxCollider>();
                    Verify(step.transform, i, positions[i], scales[i], board.rotation, filter.sharedMesh, renderer.sharedMaterials);
                }
            }
            catch { UnityEngine.Object.DestroyImmediate(holder.gameObject); throw; }
            return true;
        }

        static float Ground(Terrain terrain, Vector3 point) => terrain.SampleHeight(point) + terrain.transform.position.y;
        static void Verify(Transform step, int index, Vector3 position, Vector3 scale, Quaternion rotation, Mesh mesh, Material[] materials)
        {
            var box = step.GetComponent<BoxCollider>(); var filter = step.GetComponent<MeshFilter>(); var renderer = step.GetComponent<MeshRenderer>();
            Require(step.name == "接岸木踏面 " + (index + 1) && step.gameObject.activeSelf && (step.position - position).sqrMagnitude < .00000001f
                && Quaternion.Angle(step.rotation, rotation) < .001f && (step.lossyScale - scale).sqrMagnitude < .00000001f
                && filter && filter.sharedMesh == mesh && renderer && renderer.enabled && renderer.sharedMaterials.SequenceEqual(materials)
                && box && box.enabled && !box.isTrigger && box.center == Vector3.zero && box.size == Vector3.one,
                "Edited entry tread must be preserved, not overwritten.");
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
