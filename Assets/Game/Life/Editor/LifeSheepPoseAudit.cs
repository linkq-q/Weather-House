using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;

namespace WeatherHouse.Life.Editor
{
    [InitializeOnLoad]
    public static class LifeSheepPoseAudit
    {
        const string Request="LifeGameplayReview/20261026/sheep-pose-request.txt";
        static double nextPoll;
        static LifeSheepPoseAudit(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<nextPoll || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)return;
            nextPoll=EditorApplication.timeSinceStartup+.5;
            if(!File.Exists(Request))return;
            File.Delete(Request);
            try{Run();Debug.Log("Sheep pose audit complete: LifeGameplayReview/20261026/photo-flow/sheep-pose-audit.json");}
            catch(Exception error){Debug.LogException(error);}
        }
        [Serializable] sealed class Sample
        {
            public string clip;public float time,visualScale;public bool useScale;
            public Vector3 rendererSize,bakedSize,skinScale,headScale,head,selectedPoint,selectedHeadLocal;
            public Vector3 headRight,headUp,headForward;
            public Vector3 calibratedMouth;public float calibratedSkinDistance,calibratedMuzzleDistance;
            public int selectedVertex;
        }
        [Serializable] sealed class Report
        {
            public string status="RUNNING",utc,error;
            public string boundary="Independent preview-scene clone of the formal sheep prefab. Imported idle/forage clips are sampled without changing the main scene, Play state or production contact. Both BakeMesh modes use the existing TransformPoint coordinate chain. Selected point reproduces current head-neighbourhood/root-forward selection.";
            public List<Sample> samples=new List<Sample>();
        }
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Sheep pose audit requires Edit mode.");
            const string folder="LifeGameplayReview/20261026/photo-flow";
            var report=new Report{utc=DateTime.UtcNow.ToString("O")};
            var verticesCsv=new List<string>{"clip,time,visualScale,useScale,vertex,rootX,rootY,rootZ,headX,headY,headZ"};
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/World/Hillside/Generated/sheep.prefab");
            Scene preview=EditorSceneManager.NewPreviewScene();GameObject instance=null;Mesh mesh=new Mesh();
            try
            {
                if(!prefab)throw new InvalidOperationException("Missing formal sheep prefab.");
                var source=prefab.GetComponent<WildlifeAgent>();
                var mouthMethod=typeof(LifeWorld).GetMethod("TryMouthPoint",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
                foreach(float visualScale in new[]{.82f,1f,1.16f})
                foreach(AnimationClip clip in new[]{source.clips.idle,source.clips.forage})
                {
                    instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,preview);
                    var actor=instance.GetComponent<WildlifeAgent>();actor.enabled=false;actor.animator.applyRootMotion=false;
                    actor.visualRoot.localScale*=visualScale;actor.visualRoot.localPosition*=visualScale;
                    var skin=instance.GetComponentsInChildren<SkinnedMeshRenderer>().Where(s=>s.sharedMesh).OrderByDescending(s=>s.sharedMesh.vertexCount).First();
                    Transform head=instance.GetComponentsInChildren<Transform>().First(t=>t.name=="Head");
                    foreach(float fraction in new[]{0f,.25f,.5f,.75f,.99f})
                    {
                        clip.SampleAnimation(actor.animator.gameObject,clip.length*fraction);
                        foreach(bool useScale in new[]{true,false})
                        {
                            skin.BakeMesh(mesh,useScale);Vector3[] vertices=mesh.vertices;
                            var sample=new Sample{clip=clip.name,time=clip.length*fraction,visualScale=visualScale,useScale=useScale,
                                rendererSize=skin.bounds.size,skinScale=skin.transform.lossyScale,headScale=head.lossyScale,
                                head=instance.transform.InverseTransformPoint(head.position),headRight=head.right,headUp=head.up,headForward=head.forward,
                                selectedVertex=-1};
                            object[] mouthArguments={instance.transform,head,default(Vector3)};
                            if(!(bool)mouthMethod.Invoke(null,mouthArguments))throw new InvalidOperationException("Production sheep mouth lookup failed.");
                            Vector3 calibrated=(Vector3)mouthArguments[2];sample.calibratedMouth=instance.transform.InverseTransformPoint(calibrated);
                            sample.calibratedSkinDistance=sample.calibratedMuzzleDistance=float.MaxValue;
                            Bounds bounds=default;float reach=float.NegativeInfinity;
                            float radius=Mathf.Clamp(skin.bounds.size.magnitude*.22f,.1f,.6f);
                            for(int i=0;i<vertices.Length;i++)
                            {
                                Vector3 world=skin.transform.TransformPoint(vertices[i]);
                                sample.calibratedSkinDistance=Mathf.Min(sample.calibratedSkinDistance,Vector3.Distance(world,calibrated));
                                if(i==5492 || i==5570)sample.calibratedMuzzleDistance=Mathf.Min(sample.calibratedMuzzleDistance,Vector3.Distance(world,calibrated));
                                if(i==0)bounds=new Bounds(world,Vector3.zero);else bounds.Encapsulate(world);
                                if(Vector3.Distance(world,head.position)>radius)continue;
                                if(visualScale==1f && (fraction==0f || fraction==.5f))
                                {
                                    Vector3 rootLocal=instance.transform.InverseTransformPoint(world),headLocal=head.InverseTransformPoint(world);
                                    verticesCsv.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0},{1:R},{2:R},{3},{4},{5:R},{6:R},{7:R},{8:R},{9:R},{10:R}",
                                        clip.name,clip.length*fraction,visualScale,useScale,i,rootLocal.x,rootLocal.y,rootLocal.z,headLocal.x,headLocal.y,headLocal.z));
                                }
                                float forward=Vector3.Dot(world-head.position,instance.transform.forward);
                                if(forward<=reach)continue;
                                reach=forward;sample.selectedVertex=i;sample.selectedPoint=instance.transform.InverseTransformPoint(world);
                                sample.selectedHeadLocal=head.InverseTransformPoint(world);
                            }
                            sample.bakedSize=bounds.size;report.samples.Add(sample);mesh.Clear();
                            if(useScale && sample.calibratedMuzzleDistance>.005f*visualScale)
                                throw new InvalidOperationException("Production sheep mouth leaves its measured lower muzzle vertices: "+clip.name+" time="+sample.time+" distance="+sample.calibratedMuzzleDistance);
                        }
                    }
                    UnityEngine.Object.DestroyImmediate(instance);instance=null;
                }
                report.status="PASS";
            }
            catch(Exception error){report.status="FAIL";report.error=error.ToString();throw;}
            finally
            {
                if(instance)UnityEngine.Object.DestroyImmediate(instance);UnityEngine.Object.DestroyImmediate(mesh);
                EditorSceneManager.ClosePreviewScene(preview);Directory.CreateDirectory(folder);
                File.WriteAllText(folder+"/sheep-pose-audit.json",JsonUtility.ToJson(report,true));
                File.WriteAllLines(folder+"/sheep-head-vertices.csv",verticesCsv);
            }
        }
    }
}
