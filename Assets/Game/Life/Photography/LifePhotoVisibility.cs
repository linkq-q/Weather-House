using System.Linq;
using UnityEngine;

namespace WeatherHouse.Life
{
    public static class LifePhotoVisibility
    {
        public static bool Point(Camera camera,Vector3 point,Transform subject=null,Transform visibleContact=null)
        {
            Vector3 screen=camera.WorldToViewportPoint(point);
            if(screen.z<=camera.nearClipPlane || screen.z>camera.farClipPlane || screen.x<0 || screen.x>1 || screen.y<0 || screen.y>1)return false;
            // Rendered depth handles opaque meshes without colliders and actual alpha-cutout holes.
            if(LifePhotoDepth.ActiveFor(camera))return LifePhotoDepth.Visible(camera,screen,subject) ||
                (visibleContact && LifePhotoDepth.Visible(camera,screen,visibleContact));
            Vector3 ray=point-camera.transform.position;float length=ray.magnitude;
            foreach(RaycastHit hit in Physics.RaycastAll(camera.transform.position,ray/length,Mathf.Max(0,length-.025f),
                camera.cullingMask & ~(1<<LayerMask.NameToLayer("UI")),QueryTriggerInteraction.Ignore))
                if((!subject || (!hit.transform.IsChildOf(subject) && !subject.IsChildOf(hit.transform))) &&
                    (!visibleContact || (!hit.transform.IsChildOf(visibleContact) && !visibleContact.IsChildOf(hit.transform))))return false;
            return true;
        }
        public static bool RendererVisible(Camera camera,Renderer renderer,int samples=2)
        {
            if(!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                (camera.cullingMask & (1<<renderer.gameObject.layer))==0 || renderer.gameObject.layer==LayerMask.NameToLayer("UI"))return false;
            Bounds bounds=renderer.bounds;
            if(!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera),bounds))return false;
            Vector3 left=camera.WorldToViewportPoint(bounds.center-camera.transform.right*bounds.extents.magnitude*.5f);
            Vector3 right=camera.WorldToViewportPoint(bounds.center+camera.transform.right*bounds.extents.magnitude*.5f);
            if(Mathf.Abs(right.x-left.x)<.012f)return false;
            Vector3[] points={bounds.center,bounds.center+Vector3.up*bounds.extents.y*.5f,bounds.center-Vector3.up*bounds.extents.y*.5f,
                bounds.center+camera.transform.right*bounds.extents.x*.5f,bounds.center-camera.transform.right*bounds.extents.x*.5f};
            return points.Count(p=>Point(camera,p,renderer.transform))>=samples;
        }
        public static bool Body(Camera camera,Transform animal,Renderer[] renderers)
        {
            if(!renderers.Any(r=>RendererVisible(camera,r,1)))return false;
            Transform[] bones=animal.GetComponentsInChildren<Transform>();
            Transform head=bones.FirstOrDefault(t=>t.name=="Head" || t.name=="head");
            Transform torso=bones.FirstOrDefault(t=>t.name=="Spine" || t.name=="Spine1" || t.name=="Pelvis" || t.name=="Hips");
            if(head && torso)
            {
                Vector3 a=torso.position,b=Vector3.Lerp(torso.position,head.position,.55f);
                return Point(camera,a,animal) && Point(camera,b,animal) &&
                    Vector3.Distance(camera.WorldToViewportPoint(a),camera.WorldToViewportPoint(head.position))>.018f;
            }
            // Non-rigged legacy species still need central body visibility, never a tail corner.
            return renderers.Any(r=>RendererVisible(camera,r,3));
        }
    }
}
