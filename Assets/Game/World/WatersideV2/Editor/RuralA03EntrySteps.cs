using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class RuralA03EntrySteps
    {
        const string Name="A03 · 入口半阶";
        const string Stone="Assets/Environment/RuralRebuild20261025/A03/Materials/M002.mat";
        static readonly float[] Tops={.065f,.247f,.414f};
        static readonly float[] Z={3.04f,2.70f,2.365f};

        // Called after the original A03 LODs/colliders are built; never recenter a scene instance.
        public static void Ensure(Transform building)
        {
            if(!building||!(building.name=="A03"||building.name.StartsWith("A03 · ",StringComparison.Ordinal)||building.name.StartsWith("Rural_A03_",StringComparison.Ordinal))||Vector3.Distance(building.lossyScale,Vector3.one)>.0001f||Vector3.Angle(building.up,Vector3.up)>.01f)
                throw new InvalidOperationException("Expected the unchanged unit-scale upright A03.");
            var material=AssetDatabase.LoadAssetAtPath<Material>(Stone);
            var groups=building.GetComponentsInChildren<LODGroup>(true);
            var existing=building.Find(Name);
            if(!material||groups.Length!=1||groups[0].GetLODs().Length!=3||building.GetComponentsInChildren<Collider>(true).Count(c=>!existing||!c.transform.IsChildOf(existing))!=26)
                throw new InvalidOperationException("Original A03 stone material, LODs or colliders changed; review again.");
            var group=groups[0];var lods=group.GetLODs();
            if(existing)
            {
                if(existing.childCount!=3||existing.localPosition!=Vector3.zero||existing.localRotation!=Quaternion.identity||existing.localScale!=Vector3.one)
                    throw new InvalidOperationException("Existing A03 stairs were edited; preserve them for review.");
                for(int i=0;i<3;i++)
                {
                    var step=existing.GetChild(i);var box=step.GetComponent<BoxCollider>();var renderer=step.GetComponent<Renderer>();var filter=step.GetComponent<MeshFilter>();
                    if(step.name!="原石材半阶 "+(i+1)||step.localPosition!=new Vector3(0,Tops[i]*.5f,Z[i])||step.localScale!=new Vector3(2.38f,Tops[i],.22f)||step.localRotation!=Quaternion.identity||!box||!box.enabled||box.isTrigger||box.center!=Vector3.zero||box.size!=Vector3.one||!renderer||!renderer.enabled||renderer.sharedMaterial!=material||!filter||filter.sharedMesh!=Resources.GetBuiltinResource<Mesh>("Cube.fbx")||lods.Any(l=>l.renderers.Count(r=>r==renderer)!=1))
                        throw new InvalidOperationException("Existing A03 step geometry/material/LOD differs; no overwrite.");
                }
                return;
            }
            var holder=new GameObject(Name);holder.transform.SetParent(building,false);
            var added=new Renderer[3];
            for(int i=0;i<3;i++)
            {
                var step=GameObject.CreatePrimitive(PrimitiveType.Cube);step.name="原石材半阶 "+(i+1);step.transform.SetParent(holder.transform,false);
                step.transform.localPosition=new Vector3(0,Tops[i]*.5f,Z[i]);step.transform.localScale=new Vector3(2.38f,Tops[i],.22f);
                added[i]=step.GetComponent<Renderer>();added[i].sharedMaterial=material;
            }
            for(int i=0;i<lods.Length;i++)lods[i].renderers=lods[i].renderers.Concat(added).ToArray();
            group.SetLODs(lods);group.RecalculateBounds();
        }
    }
}
