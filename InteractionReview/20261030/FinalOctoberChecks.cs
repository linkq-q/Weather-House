using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WeatherHouse.WatersideV2;
public static class FinalOctoberChecks
{
    [Serializable] class FishPositions { public Vector3[] points; }
    public static void Run()
    {
        var root="../../../InteractionReview/20261030/";
        var data=AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/MountainTerrain.asset");
        var original=AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/OriginalTerrain.asset");
        if(data.terrainLayers.Length!=4 || data.terrainLayers.Any(l=>!l || !l.diffuseTexture))throw new Exception("Terrain references lost");
        if(!data.GetAlphamaps(0,0,data.alphamapWidth,data.alphamapHeight).Cast<float>().SequenceEqual(original.GetAlphamaps(0,0,original.alphamapWidth,original.alphamapHeight).Cast<float>()))throw new Exception("Terrain paint changed");
        var heights=data.GetHeights(0,0,513,513);var old=original.GetHeights(0,0,513,513);
        int protectedCount=0;float maxError=0;
        using(var mask=new BinaryReader(File.OpenRead(root+"mountain-effective-mask.bin")))
        using(var requested=new BinaryReader(File.OpenRead(root+"terrain-after.bin")))
            for(int z=0;z<513;z++)for(int x=0;x<513;x++)
            {
                float m=mask.ReadSingle(),expected=requested.ReadSingle();
                maxError=Mathf.Max(maxError,Mathf.Abs(heights[z,x]*52-8-expected));
                if(m==0){if(heights[z,x]!=old[z,x])throw new Exception("Protected height sample changed");protectedCount++;}
            }
        if(maxError>.002f)throw new Exception("Saved terrain height differs from requested height");
        float minDepth=float.MaxValue;int fishCount=0;
        foreach(var p in JsonUtility.FromJson<FishPositions>(File.ReadAllText(root+"fish-spawns.json")).points)
        {
            float ground=data.GetInterpolatedHeight((p.x+90)/180,(p.z+90)/180)-8;
            minDepth=Mathf.Min(minDepth,WatersideLayout.WaterHeight(p.x,p.z)-ground);fishCount++;
        }
        File.WriteAllText(root+"terrain-serialized-checks.txt","PASS 4 terrain layers and textures preserved\nPASS terrain alphamaps unchanged\nPASS protected height samples exactly unchanged: "+protectedCount+"\nPASS saved height error: "+maxError+" m\nFish spawn count="+fishCount+", minimum water depth="+minDepth+" m\n");
        if(fishCount==0 || minDepth<.61f)throw new Exception("Fish spawning depth needs review");
        InteractionRevisionChecks.Run();
        InteractionOctoberChecks.Run();
    }
}
