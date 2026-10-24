using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    // Opt-in A06-only prefab postprocess. FBX, terrain, player parameters and other models stay untouched.
    public static class RuralA06EntrySteps
    {
        const string Name="A06 · 中央开口矮石阶";
        const string Prefab="Assets/Environment/RuralRebuild20261025/Prefabs/A06.prefab";
        const string Stone="Assets/Environment/RuralRebuild20261025/A06/Materials/M002.mat";
        static readonly Vector3[] Centers={new Vector3(0,.04f,2.53f),new Vector3(0,.08f,2.23f),new Vector3(0,.12f,1.93f)};
        static readonly Vector3[] Sizes={new Vector3(1.8f,.08f,.3f),new Vector3(1.8f,.16f,.3f),new Vector3(1.8f,.24f,.3f)};
        [Serializable] public sealed class Plan
        {public string status;public Vector3 rootPosition;public Vector3[] localCenters=Centers,localSizes=Sizes;public float floorTop,minFirstRise,maxFirstRise,maxOtherRise;public bool alreadyPresent;}
        static Transform Current()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required.");
            return Object.FindObjectsOfType<Transform>().Single(t=>t.name=="A06 · 草坡牧场暖棚"&&t.parent&&t.parent.name=="乡村水岸增量 · 20261025");
        }
        public static string InspectCurrent()=>JsonUtility.ToJson(Inspect(Current()),true);
        static void ValidateModel(Transform building)
        {
            if(!building||!(building.name=="A06"||building.name.StartsWith("A06 · ",StringComparison.Ordinal)||building.name.StartsWith("Rural_A06_",StringComparison.Ordinal))||Vector3.Distance(building.lossyScale,Vector3.one)>.0001f||Vector3.Angle(building.up,Vector3.up)>.01f)
                throw new InvalidOperationException("Expected the unchanged unit-scale upright A06 instance.");
            var floor=building.GetComponentsInChildren<BoxCollider>().Single(c=>c.name=="A06_floor_proxy0");
            var core=building.GetComponentsInChildren<BoxCollider>().Single(c=>c.name=="A06_low_foundation_core_proxy0");
            if(Mathf.Abs(floor.transform.localPosition.y+floor.size.y*.5f-.33f)>.0001f||Mathf.Abs(core.size.z*.5f-1.8f)>.0001f)
                throw new InvalidOperationException("A06 foundation/floor geometry changed; review this plan again.");
            if(!AssetDatabase.LoadAssetAtPath<Material>(Stone)||building.GetComponentsInChildren<LODGroup>().Length!=1)
                throw new InvalidOperationException("Original A06 stone material or LOD group missing.");
        }
        static Plan Inspect(Transform building)
        {
            ValidateModel(building);
            var terrain=Object.FindObjectsOfType<Terrain>().Single(t=>t.gameObject.scene==building.gameObject.scene);
            var plan=new Plan{status="READY_FOR_OWNED_PREFAB_APPLY",rootPosition=building.position,floorTop=.33f,minFirstRise=float.PositiveInfinity,maxFirstRise=float.NegativeInfinity,maxOtherRise=.09f,alreadyPresent=building.Find(Name)};
            // Verify the real terrain at the outer tread, including the approach side; do not flatten it to fit the plan.
            foreach(float x in new[]{-.9f,0,.9f})foreach(float z in new[]{2.38f,2.53f,2.68f,2.73f})
            {
                Vector3 point=building.TransformPoint(new Vector3(x,0,z));float ground=terrain.SampleHeight(point)+terrain.transform.position.y;
                float rise=building.TransformPoint(new Vector3(x,.08f,z)).y-ground;
                plan.minFirstRise=Mathf.Min(plan.minFirstRise,rise);plan.maxFirstRise=Mathf.Max(plan.maxFirstRise,rise);
                if(rise<.015f||rise>.12f)
                    throw new InvalidOperationException("Outer step needs re-fitting to actual terrain at "+point+" rise="+rise);
            }
            return plan;
        }
        static Bounds BoundsOf(Transform root)
        {var renderers=root.GetComponentsInChildren<Renderer>(true);var bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);return bounds;}
        // BuildPrefab calls only for A06, after constructing its original LOD group.
        public static void Ensure(Transform building)
        {
            ValidateModel(building);var existing=building.Find(Name);
            if(existing)
            {
                if(existing.childCount!=3||existing.localPosition!=Vector3.zero||existing.localRotation!=Quaternion.identity||existing.localScale!=Vector3.one)
                    throw new InvalidOperationException("Existing stairs were edited; preserve them for review.");
                for(int i=0;i<3;i++)
                {
                    var step=existing.GetChild(i);var collider=step.GetComponent<BoxCollider>();var renderer=step.GetComponent<Renderer>();
                    if(step.localPosition!=Centers[i]||step.localScale!=Sizes[i]||step.localRotation!=Quaternion.identity||!collider||!collider.enabled||collider.isTrigger||collider.center!=Vector3.zero||collider.size!=Vector3.one||!renderer||renderer.sharedMaterial!=AssetDatabase.LoadAssetAtPath<Material>(Stone)||building.GetComponentInChildren<LODGroup>().GetLODs().Any(l=>!l.renderers.Contains(renderer)))
                        throw new InvalidOperationException("Existing step geometry/material differs; no overwrite.");
                }
                return;
            }
            Bounds before=BoundsOf(building);
            var holder=new GameObject(Name);holder.transform.SetParent(building,false);
            var material=AssetDatabase.LoadAssetAtPath<Material>(Stone);var renderers=new Renderer[3];
            for(int i=0;i<3;i++)
            {
                var step=GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.name="石阶 "+(i+1);step.transform.SetParent(holder.transform,false);step.transform.localPosition=Centers[i];step.transform.localScale=Sizes[i];
                renderers[i]=step.GetComponent<Renderer>();renderers[i].sharedMaterial=material;
            }
            var group=building.GetComponentInChildren<LODGroup>();
            var lods=group.GetLODs();for(int i=0;i<lods.Length;i++)lods[i].renderers=lods[i].renderers.Concat(renderers).ToArray();group.SetLODs(lods);group.RecalculateBounds();
            Bounds after=BoundsOf(building);
            if(Vector3.Distance(before.min,after.min)>.0001f||Vector3.Distance(before.max,after.max)>.0001f)
                throw new InvalidOperationException("Steps changed original model bounds; do not save/reposition the building.");
        }
        public static string ApplyPrefab()
        {
            var plan=Inspect(Current());var contents=PrefabUtility.LoadPrefabContents(Prefab);
            try
            {
                bool already=contents.transform.Find(Name);Ensure(contents.transform);
                if(!already&&!PrefabUtility.SaveAsPrefabAsset(contents,Prefab))throw new InvalidOperationException("A06 prefab save failed.");
            }
            finally{PrefabUtility.UnloadPrefabContents(contents);}
            plan.status="PREFAB_APPLIED_REQUIRES_ORIGINAL_INPUT_PLAY";return JsonUtility.ToJson(plan,true);
        }
    }
}
