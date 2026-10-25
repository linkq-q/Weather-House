using System;
using UnityEngine;

namespace ForestSpriteCharacter
{
    public sealed class MageJumpContinuityData:ScriptableObject
    {
        [Serializable] public struct FootPair {public Vector3 left,right;}
        public float takeoffSpread=.5f;
        public float walkDuration=2f,runDuration=2f;
        public FootPair[] walk=Array.Empty<FootPair>(),run=Array.Empty<FootPair>();
    }
}
