using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;

namespace WeatherHouse.Life.Editor
{
    public static class LifeSquirrelPoseAudit
    {
        [Serializable] sealed class Point { public int vertex;public float distance;public Vector3 headLocal,agentLocal; }
        [Serializable] sealed class Bone { public string path;public Vector3 position,scale,agentPosition; }
        [Serializable] sealed class Pose
        {
            public string clip;public float time,scale,falseSkinDistance;public Vector3 mouth,head,meshCenter,meshSize,falseMeshSize,rendererSize;
            public List<Point> nearest=new List<Point>();public List<Bone> bones=new List<Bone>();
        }
        [Serializable] sealed class Report { public string status="RUNNING",utc,boundary,error;public List<Pose> poses=new List<Pose>(); }
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Pose audit requires Edit mode.");
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/World/Hillside/Generated/squirrel.prefab");
            if(!prefab)throw new InvalidOperationException("Missing formal squirrel prefab.");
            Scene preview=EditorSceneManager.NewPreviewScene();GameObject instance=null;Mesh baked=new Mesh();
            var report=new Report{utc=DateTime.UtcNow.ToString("O"),boundary="Isolated clone samples imported clips directly at visual scales 1 and .45 (current scene squirrels use .41-.58). Both BakeMesh scale modes are measured without changing production contact. No main-scene animal, asset, controller or Play state is changed. This diagnoses binding/geometry, not blended runtime acceptance."};
            try
            {
                AnimationClip[] clips=new[]{prefab.GetComponent<WildlifeAgent>().clips.walk,prefab.GetComponent<WildlifeAgent>().clips.run,
                    Clip("SquirrelIdle"),Clip("SquirrelDrink"),Clip("SquirrelAccept"),Clip("SquirrelRestDown"),Clip("SquirrelRestLoop"),Clip("SquirrelRestUp")};
                Vector3 mouthLocal=(Vector3)typeof(LifeWorld).GetField("DrinkMouthLocal",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                Vector3 contact=(Vector3)typeof(LifeWorld).GetField("DrinkRootContact",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                foreach(float scale in new[]{1f,.45f})
                foreach(AnimationClip clip in clips)
                {
                    instance=UnityEngine.Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(instance,preview);
                    var actor=instance.GetComponent<WildlifeAgent>();actor.enabled=false;actor.animator.applyRootMotion=false;
                    actor.visualRoot.localScale*=scale;actor.visualRoot.localPosition*=scale;
                    var skin=instance.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(s=>s.sharedMesh.vertexCount).First();
                    Transform head=instance.GetComponentsInChildren<Transform>().First(t=>t.name=="Head");
                    Transform rig=instance.GetComponentsInChildren<Transform>().First(t=>t.name=="Squirrel_Rig");Vector3 originalRigScale=rig.localScale;
                    foreach(float fraction in new[]{0f,.25f,.5f,.75f,.99f})
                    {
                        clip.SampleAnimation(actor.animator.gameObject,clip.length*fraction);
                        skin.BakeMesh(baked,true);Vector3[] vertices=baked.vertices;
                        Vector3 mouth=head.TransformPoint(mouthLocal);
                        var pose=new Pose{clip=clip.name,time=clip.length*fraction,scale=scale,mouth=instance.transform.InverseTransformPoint(mouth),
                            head=instance.transform.InverseTransformPoint(head.position)};
                        Bounds bounds=default;
                        for(int i=0;i<vertices.Length;i++)
                        {
                            Vector3 point=skin.transform.TransformPoint(vertices[i]);
                            if(i==0)bounds=new Bounds(point,Vector3.zero);else bounds.Encapsulate(point);
                            pose.nearest.Add(new Point{vertex=i,distance=Vector3.Distance(point,mouth),headLocal=head.InverseTransformPoint(point),agentLocal=instance.transform.InverseTransformPoint(point)});
                        }
                        pose.nearest=pose.nearest.OrderBy(p=>p.distance).Take(8).ToList();pose.meshCenter=instance.transform.InverseTransformPoint(bounds.center);pose.meshSize=bounds.size;
                        skin.BakeMesh(baked,false);vertices=baked.vertices;pose.falseSkinDistance=float.MaxValue;
                        for(int i=0;i<vertices.Length;i++)
                        {
                            Vector3 point=skin.transform.TransformPoint(vertices[i]);
                            if(i==0)bounds=new Bounds(point,Vector3.zero);else bounds.Encapsulate(point);
                            pose.falseSkinDistance=Mathf.Min(pose.falseSkinDistance,Vector3.Distance(point,mouth));
                        }
                        pose.falseMeshSize=bounds.size;pose.rendererSize=skin.bounds.size;
                        foreach(Transform bone in instance.GetComponentsInChildren<Transform>().Where(t=>t.name=="Head" || t.name=="Pelvis" || t.name=="Squirrel_Rig" || t.name.Contains("Root") || t==actor.animator.transform))
                            pose.bones.Add(new Bone{path=AnimationUtility.CalculateTransformPath(bone,instance.transform),position=bone.localPosition,scale=bone.localScale,agentPosition=instance.transform.InverseTransformPoint(bone.position)});
                        report.poses.Add(pose);
                        if((rig.localScale-originalRigScale).sqrMagnitude>.0001f)
                            throw new InvalidOperationException(clip.name+" changed the original rig scale and invalidated its skin bind poses.");
                        if(pose.nearest[0].distance>=.035f*scale)
                            throw new InvalidOperationException(clip.name+" mouth point is not on its actual skin at "+pose.time);
                        if(clip.name=="SquirrelDrink" && fraction==.5f && Vector3.Distance(pose.mouth,contact*scale)>.004f*scale)
                            throw new InvalidOperationException("Drink contact geometry does not match the authored mouth position on the original rig.");
                    }
                    UnityEngine.Object.DestroyImmediate(instance);instance=null;
                }
                report.status="PASS";
            }
            catch(Exception e){report.status="FAIL";report.error=e.ToString();throw;}
            finally
            {
                if(instance)UnityEngine.Object.DestroyImmediate(instance);UnityEngine.Object.DestroyImmediate(baked);EditorSceneManager.ClosePreviewScene(preview);
                const string path="Docs/LifeGameplay/World/squirrel-pose-audit.json";Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));
            }
        }
        static AnimationClip Clip(string name)=>Resources.LoadAll<AnimationClip>("LifeAssets/Animations/"+name).First(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal));
    }
}
