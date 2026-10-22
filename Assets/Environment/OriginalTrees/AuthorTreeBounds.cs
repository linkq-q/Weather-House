using UnityEngine;

namespace WeatherHouse.OriginalTrees
{
    // Renderer bounds overrides are not serialized; restore the GPU leaf displacement margin on load.
    [ExecuteAlways]
    public sealed class AuthorTreeBounds : MonoBehaviour
    {
        void OnEnable()
        {
            foreach(var renderer in GetComponentsInChildren<MeshRenderer>())
            {
                var filter=renderer.GetComponent<MeshFilter>();
                if(!filter || !filter.sharedMesh) continue;
                float margin=0;
                foreach(var material in renderer.sharedMaterials)
                    if(material && material.HasProperty("_IsFluffy") && material.GetFloat("_IsFluffy")>.5f)
                        margin=Mathf.Max(margin,Mathf.Abs(material.GetFloat("_LeavesSize"))*.01f*1.5f);
                var bounds=filter.sharedMesh.bounds;bounds.Expand(margin*2);renderer.localBounds=bounds;
            }
        }
        void OnDisable()
        {
            foreach(var renderer in GetComponentsInChildren<MeshRenderer>()) renderer.ResetLocalBounds();
        }
    }
}
