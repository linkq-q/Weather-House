using UnityEngine;
using WeatherHouse.Hillside;

namespace WeatherHouse.SquirrelOga
{
    // Clips and contact data belong to this 34-bone model, never to the former squirrel rig.
    public sealed class SquirrelOgaRig : MonoBehaviour
    {
        public AnimationClip idle, walk, run, restDown, restLoop, restUp, drink;
        public Transform head;
        public SkinnedMeshRenderer headSkin;
        public Vector3 mouthHeadLocal, drinkRootContact;
        public int mouthVertexIndex = -1;
        public float mouthSurfaceMaxError;
        public string modelDependencyHash;
        public SkinnedMeshRenderer[] boundedSkins = System.Array.Empty<SkinnedMeshRenderer>();
        public Bounds[] animationLocalBounds = System.Array.Empty<Bounds>();
        public bool HasAnimationBounds => boundedSkins != null && animationLocalBounds != null &&
            boundedSkins.Length == 3 && animationLocalBounds.Length == boundedSkins.Length;

        // Renderer custom local bounds are a runtime override; serialize the measured data explicitly.
        public void ApplyAnimationBounds()
        {
            if (!HasAnimationBounds) return;
            for (int i = 0; i < boundedSkins.Length; i++) if (boundedSkins[i])
            {
                boundedSkins[i].updateWhenOffscreen = false;
                boundedSkins[i].localBounds = animationLocalBounds[i];
            }
        }
        public bool HasLifeClips => idle && walk && run && restDown && restLoop && restUp && drink;

        WildlifeAgent agent;
        int closedEyes = -1;
        float eyeWeight;

        void OnEnable()
        {
            ApplyAnimationBounds();
            agent = GetComponentInParent<WildlifeAgent>();
            closedEyes = headSkin && headSkin.sharedMesh ? headSkin.sharedMesh.GetBlendShapeIndex("EyesClosed") : -1;
            eyeWeight = closedEyes >= 0 ? headSkin.GetBlendShapeWeight(closedEyes) : 0f;
        }

        void LateUpdate()
        {
            if (!agent || !headSkin || closedEyes < 0) return;
            float target = agent.RestPhase == WildlifeRestPhase.Sleeping ? 100f : 0f;
            eyeWeight = Mathf.Lerp(eyeWeight, target, 1f - Mathf.Exp(-Time.deltaTime * (target > 0f ? 7f : 12f)));
            headSkin.SetBlendShapeWeight(closedEyes, eyeWeight);
        }

        void OnDisable()
        {
            if (headSkin && closedEyes >= 0) headSkin.SetBlendShapeWeight(closedEyes, 0f);
            eyeWeight = 0f;
        }
    }
}
