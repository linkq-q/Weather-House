using UnityEngine;
using UnityEngine.Rendering.Universal;
using WeatherHouse.MaterialLab;

namespace WeatherHouse.DrawerPrototype
{
    public sealed partial class DrawerPrototypeController
    {
        public DrawerLookSettings lookDefaults;
        public UniversalRenderPipelineAsset lookPipeline;
        public DrawerLookSettings RuntimeLook { get; private set; }
        public OilPaintPostSettings OilPost { get; private set; }

        void InitializeLook()
        {
            if (!lookDefaults || !lookPipeline) throw new System.InvalidOperationException("Rebuild the drawer scene to bind its appearance settings.");
            RuntimeLook = Instantiate(lookDefaults); generated.Add(RuntimeLook);
            Material[] materials = lookDefaults.Materials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (!materials[i]) throw new System.InvalidOperationException("A drawer appearance material is missing.");
                materials[i] = new Material(materials[i]); generated.Add(materials[i]);
            }
            RuntimeLook.SetMaterials(materials);
            meadowMaterials = RuntimeLook.grass; meadowGround = RuntimeLook.ground;
            var scope = gameObject.AddComponent<MaterialLabPipelineScope>();
            scope.enabled = false; scope.pipeline = lookPipeline; scope.enabled = true;
            ApplyLookSettings();
        }

        public void ApplyLookSettings()
        {
            if (!RuntimeLook) return;
            sunlight = RuntimeLook.sunlight; sunElevation = RuntimeLook.sunElevation; sunAzimuth = RuntimeLook.sunAzimuth;
            shadowStrength = RuntimeLook.shadowStrength; sunlightColor = RuntimeLook.sunlightColor; ambientColor = RuntimeLook.ambientColor;
            ApplyLighting();
            if (Meadow)
            {
                Meadow.clumpsPerSquareMeter = Mathf.Clamp(RuntimeLook.density,0,80); Meadow.seed = RuntimeLook.seed;
                Meadow.speciesWeights = new[] { Mathf.Max(0,RuntimeLook.speciesWeights.x),Mathf.Max(0,RuntimeLook.speciesWeights.y),Mathf.Max(0,RuntimeLook.speciesWeights.z) };
            }
            if (OilPost)
            {
                OilPost.oilEnabled = RuntimeLook.oilEnabled; OilPost.oilStrength = RuntimeLook.oilStrength;
                OilPost.radius = RuntimeLook.oilRadius; OilPost.brushAspect = RuntimeLook.oilAspect;
                OilPost.brushAngle = RuntimeLook.oilAngle; OilPost.colorSteps = RuntimeLook.oilColorSteps;
                OilPost.splitScreen = RuntimeLook.oilCompare; OilPost.pixelEnabled = false;
            }
        }
    }
}
