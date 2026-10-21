using System;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.Vegetation.Editor
{
    public static class PlantRegionChecks
    {
        // Uses the real selection/navigation entrypoints without rebuilding saved grass meshes.
        public static void Verify(GrassLabView view)
        {
            Assert(view && view.regions != null && view.regions.Length > 1, "requires selectable regions");
            GrassMeadow original = view.meadow, main = view.regions[0];
            GrassLabCamera nav = view.Navigation;
            Camera renderCamera = nav.GetComponent<Camera>();
            float savedFarClip = renderCamera.farClipPlane;
            string savedState = EditorJsonUtility.ToJson(nav);
            Vector3 position = nav.transform.position;
            Quaternion rotation = nav.transform.rotation;
            bool viewDirty = EditorUtility.IsDirty(view), cameraDirty = EditorUtility.IsDirty(nav), transformDirty = EditorUtility.IsDirty(nav.transform);
            CursorLockMode cursorLock = Cursor.lockState;
            bool cursorVisible = Cursor.visible;
            try
            {
                for (int i = 0; i < view.regions.Length; i++)
                {
                    GrassMeadow region = view.regions[i];
                    Assert(region, "region exists");
                    for (int x = -1; x <= 1; x++)
                        for (int z = -1; z <= 1; z++)
                        {
                            Vector3 local = new Vector3(x * region.size.x * .35f, 0, z * region.size.y * .35f);
                            Vector3 world = region.transform.TransformPoint(local);
                            Vector3 mainLocal = main.transform.InverseTransformPoint(world);
                            Near(region.GetGroundHeight(local.x, local.z) + region.transform.position.y,
                                main.GetGroundHeight(mainLocal.x, mainLocal.z) + main.transform.position.y, "translated patch follows original ground");
                            Near(region.PondDistance(new Vector2(local.x, local.z)), main.PondDistance(new Vector2(mainLocal.x, mainLocal.z)), "translated shoreline matches original");
                        }

                    view.SelectRegion(i);
                    Assert(view.meadow == region && nav.meadow == region, "selection connects UI and navigation");
                    Near(nav.target.x, region.transform.position.x, "focus horizontal position");
                    Near(nav.target.z, region.transform.position.z, "focus depth position");
                    Near(nav.target.y, region.GetGroundHeight(0, 0) + region.transform.position.y + .5f, "focus ground height");
                    Near(nav.distance, Mathf.Max(10, Mathf.Max(region.size.x, region.size.y) * 1.15f), "focus distance follows region size");
                    Near(nav.pitch, 35, "focus pitch");
                    Assert(renderCamera.farClipPlane >= nav.distance + nav.groundHalfExtents.magnitude * 2, "focus extends far clipping to cover ground");
                    Assert(nav.mode == GrassCameraMode.Orbit, "focus enters orbit");
                    Vector3 movedTarget = nav.target + new Vector3(.7f, .2f, -.3f);
                    nav.target = movedTarget;
                    Assert(view.Navigation.target == movedTarget, "reading navigation preserves user camera movement");

                    if (i > 0)
                    {
                        Vector3 center = region.transform.position;
                        center.y = 20;
                        nav.SetMode(GrassCameraMode.Fly);
                        nav.transform.position = center;
                        nav.SetMode(GrassCameraMode.Walk);
                        Near(nav.transform.position.x, center.x, "walk retains distant region x");
                        Near(nav.transform.position.z, center.z, "walk retains distant region z");
                        Near(nav.transform.position.y, WorldHeight(region, nav.transform.position) + nav.eyeHeight, "walk uses translated ground");
                        nav.yaw = 0;
                        nav.StepNavigation(Vector3.forward, Vector2.zero, false, .25f);
                        Near(nav.transform.position.z, center.z + nav.moveSpeed * .25f, "walking moves inside distant region");
                        nav.SetMode(GrassCameraMode.Fly);
                        center.y = -20; nav.transform.position = center;
                        nav.StepNavigation(Vector3.zero, Vector2.zero, false, 0);
                        Near(nav.transform.position.x, center.x, "fly retains distant region x");
                        Near(nav.transform.position.z, center.z, "fly retains distant region z");
                        Near(nav.transform.position.y, WorldHeight(region, nav.transform.position) + .15f, "fly uses translated ground");
                        nav.pitch = 0;
                        nav.StepNavigation(Vector3.right, Vector2.zero, false, .25f);
                        Near(nav.transform.position.x, center.x + nav.flySpeed * .25f, "flying moves inside distant region");
                        nav.transform.position = new Vector3(nav.groundHalfExtents.x + 5, 20, nav.groundHalfExtents.y + 5);
                        nav.StepNavigation(Vector3.zero, Vector2.zero, false, 0);
                        Near(nav.transform.position.x, nav.groundHalfExtents.x, "fly respects configured x boundary");
                        Near(nav.transform.position.z, nav.groundHalfExtents.y, "fly respects configured z boundary");
                        Vector3 pond = region.transform.TransformPoint(new Vector3(region.pondCenter.x, 8, region.pondCenter.y));
                        nav.transform.position = pond;
                        nav.SetMode(GrassCameraMode.Walk);
                        Assert(WorldHeight(region, nav.transform.position) > region.waterLevel + region.transform.position.y + .20f, "walk leaves translated pond");
                    }
                    foreach (GrassSpecies species in region.species) VerifyControls(species);
                }
                Debug.Log("Plant region checks PASS: selection; offset terrain/shore; focus; camera movement; walking/flying; meaningful density controls.");
            }
            finally
            {
                view.meadow = original;
                EditorJsonUtility.FromJsonOverwrite(savedState, nav);
                nav.SetMode(nav.mode);
                EditorJsonUtility.FromJsonOverwrite(savedState, nav);
                nav.transform.SetPositionAndRotation(position, rotation);
                renderCamera.farClipPlane = savedFarClip;
                if (!viewDirty) EditorUtility.ClearDirty(view);
                if (!cameraDirty) EditorUtility.ClearDirty(nav);
                if (!transformDirty) EditorUtility.ClearDirty(nav.transform);
                Cursor.lockState = cursorLock; Cursor.visible = cursorVisible;
            }
        }

        static void VerifyControls(GrassSpecies species)
        {
            GrassKind kind = species.kind;
            if (kind == GrassKind.Fern || kind == GrassKind.LeafyHerb || kind == GrassKind.RoundGroundcover)
                foreach (string field in new[] { "petalCount", "flowerColor", "centerColor" })
                    Assert(PlantControlRules.Label(kind, field) == null, "leaf-only species hide unused flower controls");
            if (PlantControlRules.Label(kind, "bladeCount") == null) return;
            PlantControlRules.DensityRange(kind, out int min, out int max);
            for (int value = min; value <= max; value++)
                Assert(PlantControlRules.DensityValue(kind, PlantControlRules.DensityRaw(kind, value)) == value, "effective density round-trips");
            GrassSpecies copy = UnityEngine.Object.Instantiate(species);
            Mesh low = null, high = null;
            try
            {
                copy.bladeCount = PlantControlRules.DensityRaw(kind, min);
                low = GrassMeshFactory.Build(copy, 0);
                copy.bladeCount = PlantControlRules.DensityRaw(kind, max);
                high = GrassMeshFactory.Build(copy, 0);
                Assert(low.vertexCount != high.vertexCount, "density changes generated geometry: " + species.name);
            }
            finally
            {
                if (low) UnityEngine.Object.DestroyImmediate(low);
                if (high) UnityEngine.Object.DestroyImmediate(high);
                UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        static float WorldHeight(GrassMeadow meadow, Vector3 world)
        {
            Vector3 local = meadow.transform.InverseTransformPoint(world);
            return meadow.GetGroundHeight(local.x, local.z) + meadow.transform.position.y;
        }
        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Plant region check failed: " + message);
        }
        static void Near(float actual, float expected, string message)
        {
            Assert(Mathf.Abs(actual - expected) < .003f, message + " (" + actual + " vs " + expected + ")");
        }
    }
}
