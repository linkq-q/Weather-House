using System.Collections.Generic;
using ForestSpriteCharacter;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.Hillside
{
    [DisallowMultipleComponent, DefaultExecutionOrder(150)]
    public sealed class HillsideTreeOcclusion : MonoBehaviour
    {
        sealed class Tree
        {
            public Bounds bounds;
            public Renderer[] renderers;
            public ShadowCastingMode[] original;
            public bool hidden;
            public float clearSince = -1;
        }

        sealed class TreeLod
        {
            public LODGroup group;
            public LOD[] original;
        }

        readonly List<Tree> trees = new List<Tree>();
        readonly List<TreeLod> treeLods = new List<TreeLod>();
        bool initialized, treeLodsApplied;
        Camera viewCamera;
        Transform player;

        void OnEnable()
        {
            if (Application.isPlaying && initialized) ApplyTreeLods();
        }

        void Start()
        {
            viewCamera = Camera.main;
            var controller = FindObjectOfType<ForestSpriteDemoController>();
            if (controller) player = controller.transform;
            // This component lives on the generated tree group, so props and characters are excluded.
            foreach (LODGroup group in GetComponentsInChildren<LODGroup>())
            {
                var renderers = new List<Renderer>();
                foreach (LOD lod in group.GetLODs())
                    foreach (Renderer renderer in lod.renderers)
                        if (renderer && !renderers.Contains(renderer)) renderers.Add(renderer);
                if (renderers.Count == 0) continue;
                var tree = new Tree { renderers = renderers.ToArray(), original = new ShadowCastingMode[renderers.Count], bounds = renderers[0].bounds };
                for (int i = 0; i < renderers.Count; i++)
                {
                    tree.original[i] = renderers[i].shadowCastingMode;
                    tree.bounds.Encapsulate(renderers[i].bounds);
                }
                trees.Add(tree);
            }
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

        void LateUpdate()
        {
            if (!viewCamera || !player) return;
            Vector3 offset = player.position + Vector3.up * 1.1f - viewCamera.transform.position;
            float distance = offset.magnitude;
            if (distance < .1f) return;
            var ray = new Ray(viewCamera.transform.position, offset / distance);
            foreach (Tree tree in trees)
            {
                Bounds bounds = tree.bounds;
                bounds.Expand(tree.hidden ? 1.8f : 1.2f);
                bool blocked = bounds.IntersectRay(ray, out float entry) && entry < distance - .2f;
                if (blocked)
                {
                    tree.clearSince = -1;
                    if (!tree.hidden) SetHidden(tree, true);
                }
                else if (tree.hidden)
                {
                    if (tree.clearSince < 0) tree.clearSince = Time.unscaledTime;
                    else if (Time.unscaledTime - tree.clearSince >= .3f) SetHidden(tree, false);
                }
            }
        }

        static void SetHidden(Tree tree, bool hidden)
        {
            tree.hidden = hidden;
            for (int i = 0; i < tree.renderers.Length; i++)
                if (tree.renderers[i]) tree.renderers[i].shadowCastingMode = hidden ? ShadowCastingMode.ShadowsOnly : tree.original[i];
        }

        void OnDisable()
        {
            foreach (TreeLod lod in treeLods)
                if (lod.group) lod.group.SetLODs(lod.original);
            treeLods.Clear();
            treeLodsApplied = false;
            foreach (Tree tree in trees)
            {
                if (tree.hidden) SetHidden(tree, false);
                tree.clearSince = -1;
            }
        }
    }
}
