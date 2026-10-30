using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.Hillside
{
    [DisallowMultipleComponent, DefaultExecutionOrder(150)]
    public sealed class HillsideTreeOcclusion : MonoBehaviour
    {
        sealed class TreeLod
        {
            public LODGroup group;
            public LOD[] original;
        }

        readonly List<TreeLod> treeLods = new List<TreeLod>();
        bool initialized, treeLodsApplied;

        void OnEnable()
        {
            if (Application.isPlaying && initialized) ApplyTreeLods();
        }

        void Start()
        {
            // Keep distance LODs; crossing a canopy bound must not hide the entire tree.
            initialized = true;
            if (Application.isPlaying) ApplyTreeLods();
        }

        void ApplyTreeLods()
        {
            if (treeLodsApplied) return;
            treeLodsApplied = true;
            foreach (LODGroup group in GetComponentsInChildren<LODGroup>())
            {
                LOD[] original = group.GetLODs();
                if (original.Length < 3) continue;
                float previous = original[1].screenRelativeTransitionHeight;
                float earlier = Mathf.Min(previous * 1.5f, original[0].screenRelativeTransitionHeight * .95f);
                if (!(earlier > previous)) continue;
                var adjusted = (LOD[])original.Clone();
                LOD middle = adjusted[1];
                middle.screenRelativeTransitionHeight = earlier;
                adjusted[1] = middle;
                treeLods.Add(new TreeLod { group = group, original = original });
                group.SetLODs(adjusted);
            }
        }

        void OnDisable()
        {
            foreach (TreeLod lod in treeLods)
                if (lod.group) lod.group.SetLODs(lod.original);
            treeLods.Clear();
            treeLodsApplied = false;
        }
    }
}
