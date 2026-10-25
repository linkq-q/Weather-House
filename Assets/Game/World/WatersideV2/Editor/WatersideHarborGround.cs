using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.WatersideV2.Editor
{
    // One reversible seating apron; keeps the harbor dock and every other region untouched.
    public static class WatersideHarborGround
    {
        const string FilePath="WatersideV3Review/harbor-ground.json";
        static readonly Vector3 Center=new Vector3(53.5f,0,-44.6f);
        [Serializable] sealed class Move { public string id,path;public Vector3 before,after;public Quaternion rotation;public Vector3 scale; }
        [Serializable] sealed class Patch { public bool applied;public string terrainGuid;public int x,z,width,height;public float[] before,after;public Move[] moves; }

        public static void Apply(Terrain terrain,Transform harbor)
        {
            if(File.Exists(FilePath) && Read().applied)throw new InvalidOperationException("Restore the existing harbor ground trial before applying again.");
            TerrainData data=terrain.terrainData;Vector3 origin=terrain.transform.position;int resolution=data.heightmapResolution;
            int x0=Mathf.Clamp(Mathf.FloorToInt((Center.x-3.7f-origin.x)/data.size.x*(resolution-1)),0,resolution-1);
            int z0=Mathf.Clamp(Mathf.FloorToInt((Center.z-3.7f-origin.z)/data.size.z*(resolution-1)),0,resolution-1);
            int x1=Mathf.Clamp(Mathf.CeilToInt((Center.x+3.7f-origin.x)/data.size.x*(resolution-1)),0,resolution-1);
            int z1=Mathf.Clamp(Mathf.CeilToInt((Center.z+3.7f-origin.z)/data.size.z*(resolution-1)),0,resolution-1);
            var patch=new Patch{applied=true,terrainGuid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data)),x=x0,z=z0,width=x1-x0+1,height=z1-z0+1};
            float[,] before=data.GetHeights(x0,z0,patch.width,patch.height),after=(float[,])before.Clone();
            float target=terrain.SampleHeight(Center)/data.size.y;
            for(int z=0;z<patch.height;z++)for(int x=0;x<patch.width;x++)
            {
                float wx=origin.x+(x+x0)/(float)(resolution-1)*data.size.x,wz=origin.z+(z+z0)/(float)(resolution-1)*data.size.z;
                float distance=Vector2.Distance(new Vector2(wx,wz),new Vector2(Center.x,Center.z));
                float weight=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(2.2f,3.7f,distance));
                // Keep the authored shoreline wet and the dock approach outside the seating apron unchanged.
                if(WatersideLayout.IsWater(wx,wz,.12f))weight=0;
                after[z,x]=Mathf.Lerp(before[z,x],target,weight);
            }
            var originals=harbor.Cast<Transform>().Where(t=>t.gameObject.activeInHierarchy &&
                !t.name.Contains("栈台") && !t.name.Contains("划艇") && !t.name.Contains("码头"))
                .Where(t=>t.GetComponentsInChildren<Renderer>().Any(r=>FootprintDistance(r.bounds)<3.7f)).ToArray();
            patch.before=Flatten(before);patch.after=Flatten(after);
            patch.moves=originals.Select(t=>new Move{id=GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(),path=t.name,
                before=t.position,after=t.position,rotation=t.rotation,scale=t.localScale}).ToArray();
            float[] oldGround=originals.Select(t=>terrain.SampleHeight(t.position)).ToArray();
            Write(patch);
            data.SetHeights(x0,z0,after);
            for(int i=0;i<originals.Length;i++)
            {
                patch.moves[i].after=patch.moves[i].before+Vector3.up*(terrain.SampleHeight(originals[i].position)-oldGround[i]);
                originals[i].position=patch.moves[i].after;
            }
            Write(patch);EditorUtility.SetDirty(data);terrain.Flush();Validate(terrain);
        }

        public static bool TryExpected(string id,out Vector3 expected)
        {
            expected=default;if(!File.Exists(FilePath))return false;var patch=Read();if(!patch.applied)return false;
            var move=patch.moves.FirstOrDefault(m=>m.id==id);if(move==null)return false;expected=move.after;return true;
        }

        public static void Validate(Terrain terrain)
        {
            if(!File.Exists(FilePath) || !Read().applied)return;
            var patch=Read();CheckTerrain(terrain,patch);
            foreach(var move in patch.moves)
            {
                var t=Resolve(move.id);
                if(!t || (t.position-move.after).sqrMagnitude>.000001f || Quaternion.Angle(t.rotation,move.rotation)>.01f ||
                    (t.localScale-move.scale).sqrMagnitude>.000001f)throw new InvalidOperationException("Harbor furniture changed outside its ground adjustment: "+move.path);
            }
        }

        // Tide-pool expansion can require moving a storage prop; the two seating pieces remain fixed.
        public static void MoveStorage(Transform prop,Vector3 position)
        {
            if(prop.name=="岸边小桌"||prop.name=="木椅")throw new InvalidOperationException("Preserve the verified seating layout.");
            var patch=Read();string id=GlobalObjectId.GetGlobalObjectIdSlow(prop).ToString();
            var item=patch.moves.FirstOrDefault(m=>m.id==id);
            if(item!=null && (prop.position-item.after).sqrMagnitude>.000001f)throw new InvalidOperationException("Storage changed after apron placement.");
            prop.position=position;
            if(item!=null){item.after=position;Write(patch);}
        }

        public static void Restore(Terrain terrain)
        {
            if(!File.Exists(FilePath) || !Read().applied)return;
            Validate(terrain);var patch=Read();
            terrain.terrainData.SetHeights(patch.x,patch.z,Expand(patch.before,patch.width,patch.height));
            foreach(var move in patch.moves)Resolve(move.id).position=move.before;
            EditorUtility.SetDirty(terrain.terrainData);AssetDatabase.SaveAssetIfDirty(terrain.terrainData);terrain.Flush();
            patch.applied=false;Write(patch);
        }

        static void CheckTerrain(Terrain terrain,Patch patch)
        {
            if(!terrain || AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(terrain.terrainData))!=patch.terrainGuid)
                throw new InvalidOperationException("The harbor ground snapshot belongs to a different terrain.");
            var current=Flatten(terrain.terrainData.GetHeights(patch.x,patch.z,patch.width,patch.height));
            // Unity stores height values at 16-bit precision.
            if(current.Where((v,i)=>Mathf.Abs(v-patch.after[i])>1.6f/65535f).Any())
                throw new InvalidOperationException("New terrain edits appeared inside the harbor seating patch; preserve them.");
        }
        static float FootprintDistance(Bounds b)=>Vector2.Distance(new Vector2(Center.x,Center.z),
            new Vector2(Mathf.Clamp(Center.x,b.min.x,b.max.x),Mathf.Clamp(Center.z,b.min.z,b.max.z)));
        static Transform Resolve(string text){GlobalObjectId id;if(!GlobalObjectId.TryParse(text,out id))throw new InvalidDataException(text);return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Transform;}
        static Patch Read()=>JsonUtility.FromJson<Patch>(File.ReadAllText(FilePath));
        static void Write(Patch patch)=>File.WriteAllText(FilePath,JsonUtility.ToJson(patch,true));
        static float[] Flatten(float[,] values){var result=new float[values.Length];Buffer.BlockCopy(values,0,result,0,result.Length*sizeof(float));return result;}
        static float[,] Expand(float[] values,int width,int height){var result=new float[height,width];Buffer.BlockCopy(values,0,result,0,values.Length*sizeof(float));return result;}
    }
}
