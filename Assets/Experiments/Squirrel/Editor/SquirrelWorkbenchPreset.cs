using UnityEngine;

namespace WeatherHouseSquirrelLab.Editor
{
    [CreateAssetMenu(menuName = "Weather House/Squirrel Workbench Preset")]
    public sealed class SquirrelWorkbenchPreset : ScriptableObject
    {
        public GameObject sourceModel;
        public AnimationClip sourceClip;
        public SquirrelWorkbenchParameters parameters = new SquirrelWorkbenchParameters();
        public SquirrelWorkbenchRig.BoneEdit[] boneOffsets = new SquirrelWorkbenchRig.BoneEdit[0];
    }
}
