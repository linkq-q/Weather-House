using System;
using UnityEngine;

namespace WeatherHouseSquirrelLab.Editor
{
    [Serializable]
    public sealed class SquirrelWorkbenchParameters
    {
        [Range(0f, 2.5f)] public float bodyAmplitude = 1.35f;
        [Range(0f, 3f)] public float armAmplitude = 1.6f;
        [Range(0f, 2f)] public float legAmplitude = 1.2f;
        public bool strideEnabled = true;
        [Tooltip("Half-stride as a fraction of the thigh + shin length; leg reach remains constrained.")]
        [Range(.05f, .82f)] public float strideLength = .69f;
        [Tooltip("Swing sole lift as a fraction of leg length.")]
        [Range(0f, .6f)] public float footLift = .30f;
        [Range(0f, .18f)] public float hipBounce = .12f;
        [Range(0f, 2f)] public float weightShift = 1f;
        [Range(0f, 2f)] public float upperBody = 1f;
        public bool bodyElasticity = true;
        public bool tailElasticity = true;
        [Range(20f, 400f)] public float bodyStiffness = 130f;
        [Range(0f, 50f)] public float bodyDamping = 16f;
        [Range(5f, 300f)] public float tailStiffness = 65f;
        [Range(0f, 40f)] public float tailDamping = 9f;
        [Tooltip("Lateral S-wave angle in degrees at the tail tip.")]
        [Range(0f, 65f)] public float tailAmplitude = 44f;
        [Range(.05f, 2f)] public float tailFrequency = .65f;
        public bool collisionEnabled = true;
        [Tooltip("Approximate body collision sphere radius, as a fraction of bind mesh height. This is not mesh collision.")]
        [Range(.05f, .4f)] public float collisionRadius = .20f;
    }
}
