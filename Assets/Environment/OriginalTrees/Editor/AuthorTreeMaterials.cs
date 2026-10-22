using UnityEditor;
using UnityEngine;

namespace WeatherHouse.OriginalTrees.Editor
{
    // Numeric values below come directly from the author MaterialInstanceConstant assets.
    public static class AuthorTreeMaterials
    {
        public static void Create()
        {
            Material m;
            m=AuthorTreeStudy.Material("Foliage_Birch","Weather House/Author Foliage Static");
            m.SetFloat("_LeavesSize",40f);
            m.SetFloat("_TopColorGradientHeight",0.375990987f);
            m.SetFloat("_TopColorGradientWidth",0.287999988f);
            m.SetFloat("_ColorVariationNoiseTiling",1056.72681f);
            m.SetFloat("_Opacity",0.0500000007f);
            m.SetVector("_TopColor",new Vector4(0.408333004f,0.186116993f,0.0389069989f,1f));
            m.SetVector("_NoiseColor",new Vector4(0.6875f,0.391232997f,0.0205109995f,1f));
            m.SetVector("_SubsurfaceColor",new Vector4(0.420832992f,0.0564710014f,0f,1f));
            m.SetFloat("_IsFluffy",1);
            m.SetFloat("_Cutoff",.3333f);
            m.SetTexture("_LeavesAlpha",Texture("TXT_Leaves_Birch"));
            m.SetTexture("_ColorVariationNoise",Texture("TilingNoise05"));
            m.SetVector("_UEWorldX",new Vector4(0,0,1,0));
            m.SetVector("_UEWorldY",new Vector4(1,0,0,0));
            m.SetVector("_UEWorldZ",new Vector4(0,1,0,0));
            EditorUtility.SetDirty(m);
            m=AuthorTreeStudy.Material("Trunk_Birch","Weather House/Author Trunk");
            m.SetTexture("_BaseMap",Texture("TXT_Trunk_Birch_BaseColor"));
            m.SetTexture("_OcclusionMap",Texture("TXT_Trunk_Birch_AO"));
            EditorUtility.SetDirty(m);
            m=AuthorTreeStudy.Material("Foliage_Beech","Weather House/Author Foliage Static");
            m.SetFloat("_LeavesSize",30f);
            m.SetFloat("_TopColorGradientHeight",0.199981004f);
            m.SetFloat("_TopColorGradientWidth",1.34400105f);
            m.SetFloat("_ColorVariationNoiseTiling",407.989105f);
            m.SetFloat("_Opacity",0.0500000007f);
            m.SetVector("_TopColor",new Vector4(0.462500006f,0f,0.0347039998f,1f));
            m.SetVector("_NoiseColor",new Vector4(0.366665989f,0.225316003f,0f,1f));
            m.SetVector("_SubsurfaceColor",new Vector4(0.512499988f,0.33930701f,0f,1f));
            m.SetFloat("_IsFluffy",1);
            m.SetFloat("_Cutoff",.3333f);
            m.SetTexture("_LeavesAlpha",Texture("TXT_Leaves_Beech"));
            m.SetTexture("_ColorVariationNoise",Texture("TilingNoise05"));
            m.SetVector("_UEWorldX",new Vector4(0,0,1,0));
            m.SetVector("_UEWorldY",new Vector4(1,0,0,0));
            m.SetVector("_UEWorldZ",new Vector4(0,1,0,0));
            EditorUtility.SetDirty(m);
            m=AuthorTreeStudy.Material("Trunk_Beech","Weather House/Author Trunk");
            m.SetTexture("_BaseMap",Texture("TXT_Trunk_Beech_BaseColor"));
            m.SetTexture("_OcclusionMap",Texture("TXT_Trunk_Beech_AO"));
            EditorUtility.SetDirty(m);
            m=AuthorTreeStudy.Material("Foliage_FirTree","Weather House/Author Foliage Static");
            m.SetFloat("_LeavesSize",-1.27226305f);
            m.SetFloat("_TopColorGradientHeight",1.60799098f);
            m.SetFloat("_TopColorGradientWidth",5f);
            m.SetFloat("_ColorVariationNoiseTiling",744.496216f);
            m.SetFloat("_Opacity",0.0500000007f);
            m.SetVector("_TopColor",new Vector4(0.0583329983f,0.0264369994f,0f,1f));
            m.SetVector("_NoiseColor",new Vector4(0.134871006f,0.150000006f,0.0538799986f,1f));
            m.SetVector("_SubsurfaceColor",new Vector4(0.0245210007f,0.0791670009f,0f,1f));
            m.SetFloat("_IsFluffy",0);
            m.SetFloat("_Cutoff",.3333f);
            m.SetTexture("_LeavesAlpha",Texture("TXT_Leaves_FirTree"));
            m.SetTexture("_ColorVariationNoise",Texture("TilingNoise05"));
            m.SetVector("_UEWorldX",new Vector4(0,0,1,0));
            m.SetVector("_UEWorldY",new Vector4(1,0,0,0));
            m.SetVector("_UEWorldZ",new Vector4(0,1,0,0));
            EditorUtility.SetDirty(m);
            m=AuthorTreeStudy.Material("Trunk_FirTree","Weather House/Author Trunk");
            m.SetTexture("_BaseMap",Texture("TXT_Trunk_FirTree_BaseColor"));
            m.SetTexture("_OcclusionMap",Texture("TXT_Trunk_FirTree_AO"));
            EditorUtility.SetDirty(m);
        }
        static Texture2D Texture(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(AuthorTreeStudy.Root+"/Textures/"+name+".png");
    }
}
