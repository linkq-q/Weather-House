using System;
using UnityEngine;

namespace ForestSpriteCharacter
{
    // The existing hat/hair spring (-100) and cape guide (-90) run after this
    // pose transfer. Their bones, components, collision proxies and settings
    // remain the same objects from the original prefab.
    [DefaultExecutionOrder(-120), DisallowMultipleComponent]
    public sealed class MageAccessoryPose : MonoBehaviour
    {
        [Serializable]
        public struct Binding
        {
            public Transform source, target;
            public Vector3 positionInSource;
            public Quaternion rotationInSource;
        }

        public Binding[] bindings = Array.Empty<Binding>();

        void LateUpdate() => ApplyPose();

        public void ApplyPose()
        {
            // Bindings are in hierarchy order. Reading the Mage rig and
            // writing only the original rig prevents feedback accumulation.
            foreach (var binding in bindings)
            {
                binding.target.SetPositionAndRotation(
                    binding.source.TransformPoint(binding.positionInSource),
                    binding.source.rotation * binding.rotationInSource);
            }
        }
    }
}
