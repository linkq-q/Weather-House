using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using WeatherHouse.WatersideV2.Editor;
using Object=UnityEngine.Object;

public static class AnimalScalePreviewChecks
{
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Near(Vector3 actual,Vector3 expected,string message)
    {Require((actual-expected).magnitude<.0001f,message+" actual="+actual+" expected="+expected);}

    public static string[] Run()
    {
        var results=new List<string>();
        Scene scene=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("Animal edit-size regression");SceneManager.MoveGameObjectToScene(root,scene);
        try
        {
            var owner=root.AddComponent<WildlifePopulation>();owner.obstacleMask=0;
            owner.worldBounds=new Bounds(Vector3.up*10,Vector3.one*200);owner.worldSeed=20261023;
            foreach(string species in new[]{"squirrel","turtle"})
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/World/Hillside/Generated/"+species+".prefab");
                Require(prefab,"Missing real animal prefab: "+species);
                var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);go.transform.SetParent(root.transform);
                var animal=go.GetComponent<WildlifeAgent>();animal.spawnId=species=="squirrel"?14:44;
                animal.scaleRange=WatersideAnimalScale.ForNewInstance(species,animal.scaleRange);
                Vector3 authored=animal.visualRoot.localScale;
                Require(animal.BakeVisualScale(owner.worldSeed),"First edit preview did not apply: "+species);
                Vector3 editScale=animal.visualRoot.localScale,editOffset=animal.visualRoot.localPosition;
                Require(species=="squirrel"?editScale.magnitude<authored.magnitude:editScale.magnitude>authored.magnitude,"Edit preview missed requested species size");
                Require(!animal.BakeVisualScale(owner.worldSeed),"Repeated preview accumulated scale");
                // A new object carries only serialized data, as reopening the saved scene does.
                var reopened=Object.Instantiate(go,root.transform).GetComponent<WildlifeAgent>();
                animal.Initialize(owner);Near(animal.visualRoot.localScale,editScale,"Edit/Play mismatch");
                Near(animal.visualRoot.localPosition,editOffset,"Edit/Play foot-offset mismatch");
                WildlifeBodyEnvelope body=animal.BodyEnvelope;float radius=animal.Radius;
                animal.Initialize(owner);Near(animal.visualRoot.localScale,editScale,"Second Initialize changed scale");
                reopened.Initialize(owner);Near(reopened.visualRoot.localScale,editScale,"Serialized/reopened preview applied twice");
                Near(reopened.BodyEnvelope.half,body.half,"Reopened body does not match rendered size");
                Require(Mathf.Abs(reopened.Radius-radius)<.0001f,"Reopened collision radius changed");
                animal.scaleRange*=.8f;Require(animal.BakeVisualScale(owner.worldSeed),"Edited size range failed to update preview");
                Near(animal.visualRoot.localScale,editScale*.8f,"Range edit compounded instead of replacing scale");
                animal.Initialize(owner);Near(animal.visualRoot.localScale,editScale*.8f,"Range edit differed in runtime");
                results.Add("PASS "+species+" real prefab: baked edit/Initialize/reinitialize/serialized clone/body radius/range edit agree");
                Object.DestroyImmediate(reopened.gameObject);Object.DestroyImmediate(go);
            }
        }
        finally{Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
        return results.ToArray();
    }

    public static void Batch()
    {
        string report=Environment.GetEnvironmentVariable("WEATHERHOUSE_SCALE_REPORT");int exit=0;string result;
        try{result=string.Join("\n",Run());}catch(Exception e){exit=1;result="FAIL "+e;}
        if(!string.IsNullOrEmpty(report))File.WriteAllText(report,result);
        Debug.Log(result);if(Application.isBatchMode)EditorApplication.Exit(exit);
    }
}
