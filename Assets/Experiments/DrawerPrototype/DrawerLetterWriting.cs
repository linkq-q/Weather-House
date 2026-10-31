using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.DrawerPrototype
{
    [ExecuteAlways, RequireComponent(typeof(TextMesh))]
    public sealed class DrawerLetterWriting : MonoBehaviour
    {
        public DrawerLetterContent content;
        [Range(0,2)] public int section;
        public Vector2 maximumSize;
        string displayed;
        Font inkFont;
        Material ink;
        string Words => !content ? "" : section==0 ? content.salutation : section==1 ? content.message : content.signature;

        void OnEnable() { Font.textureRebuilt += FontRebuilt; Refresh(); }
        void OnDisable() => Font.textureRebuilt -= FontRebuilt;
        void OnDestroy()
        {
            if(!ink)return;
            if(Application.isPlaying)Destroy(ink); else DestroyImmediate(ink);
        }
        void FontRebuilt(Font font) { if(font==inkFont && ink)ink.mainTexture=font.material.mainTexture; }
        void OnValidate() => Refresh();
        void Update() { if(displayed!=Words)Refresh(); }

        public void Refresh()
        {
            if(!content || maximumSize.x<=0 || maximumSize.y<=0)return;
            var text=GetComponent<TextMesh>(); var font=Resources.Load<Font>("Fonts/ZCOOLXiaoWei-Regular");
            if(!font)return;
            text.font=font; text.fontSize=64; text.characterSize=.10f; text.anchor=TextAnchor.UpperLeft;
            text.alignment=TextAlignment.Left; text.lineSpacing=1.55f; text.color=new Color(.31f,.28f,.22f);
            text.text=Words; displayed=Words; font.RequestCharactersInTexture(Words,64,FontStyle.Normal);
            if(!ink)ink=new Material(Resources.Load<Shader>("DrawerPrototype/RefinedProps/LetterInk")) { name="Depth-tested letter ink",hideFlags=HideFlags.HideAndDontSave };
            inkFont=font; ink.mainTexture=font.material.mainTexture;
            var renderer=GetComponent<MeshRenderer>(); renderer.sharedMaterial=ink;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            Vector3 bounds=renderer.localBounds.size;
            if(bounds.x>.001f && bounds.y>.001f)
                transform.localScale=Vector3.one*Mathf.Min(maximumSize.x/bounds.x,maximumSize.y/bounds.y);
        }
    }
}
