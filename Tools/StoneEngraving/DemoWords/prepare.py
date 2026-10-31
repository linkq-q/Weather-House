"""Stage an isolated stone render; derive technical maps, never repaint source PNGs."""
from pathlib import Path
import hashlib, json, shutil, sys, importlib.util
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
PROJECT = ROOT / 'StoneEngravingReview/20261030/DemoWords/UnityProject'
DEST = Path('Assets/Experiments/StoneEngraving/DemoWords')
sys.path.insert(0, str(HERE.parent))
from build_maps import make_maps
helpersSpec=importlib.util.spec_from_file_location('approved_stroke_helpers',HERE.parent/'BatchProduction/prepare.py')
helpers=importlib.util.module_from_spec(helpersSpec); helpersSpec.loader.exec_module(helpers)
CANONICAL=Path('C:/Users/31483/Desktop/天气屋/字符设计/动物语字符设计_v05_四字扩展_20261030')

SPECS = [
    dict(id='sea', title='海', width=8., height=2.6, depth=1.05,
         faceWidth=7.2, faceHeight=1.85, glyphX=.5, artBottom=.27,
         source='exec-b5111fa2-5dca-4a2f-aa03-701c427e0dc4.png'),
    dict(id='look', title='看', width=3.9, height=4.8, depth=1.,
         faceWidth=2.5, faceHeight=3.9, glyphX=.5, artBottom=.30,
         source='exec-7fdba7fb-98c6-41ca-abaa-71efd4b8c28b.png'),
    dict(id='past', title='以前', width=4.6, height=5.2, depth=1.20,
         faceWidth=3.8, faceHeight=4.1, glyphX=.5, artBottom=.23,
         source='exec-eb425cec-bf17-45b7-bd88-c7736f4a6336.png'),
]
# User selected the western lake terrace ruins: intact arch, round paving and shore.

def copy(relative):
    source=ROOT/relative; dest=PROJECT/relative
    dest.parent.mkdir(parents=True,exist_ok=True); shutil.copy2(source,dest)
    if Path(str(source)+'.meta').exists(): shutil.copy2(str(source)+'.meta',str(dest)+'.meta')

def prepare():
    (PROJECT/'Packages').mkdir(parents=True,exist_ok=True)
    manifest=json.loads((ROOT/'Packages/manifest.json').read_text())
    deps={k:v for k,v in manifest['dependencies'].items() if k.startswith('com.unity.modules.')}
    for name in ['com.unity.render-pipelines.universal','com.unity.render-pipelines.core','com.unity.shadergraph',
                 'com.unity.searcher','com.unity.mathematics','com.unity.burst','com.unity.render-pipelines.universal-config','com.unity.ugui']:
        folders=list((ROOT/'Library/PackageCache').glob(name+'@*'))
        assert len(folders)==1, name
        deps[name]='file:'+folders[0].as_posix()
    (PROJECT/'Packages/manifest.json').write_text(json.dumps({'dependencies':deps},indent=2))
    for name in ['ProjectVersion.txt','ProjectSettings.asset','GraphicsSettings.asset','QualitySettings.asset']:
        copy(Path('ProjectSettings')/name)
    for relative in ['Assets/Experiments/StoneEngraving/SevenPainted/SevenPainted.shader',
                     'Assets/Experiments/StoneEngraving/StoneEngravingReadability.cs',
                     'Assets/Experiments/StoneEngraving/TabletStudy/PaintGrain.png',
                     'Assets/HIGHLANDS - Stylized Environment/Textures/Forest/StonesRocks_Normal.png']:
        copy(Path(relative))
    (PROJECT/'Assets/Editor').mkdir(parents=True,exist_ok=True)
    shutil.copy2(HERE/'DemoStoneBuilder.cs',PROJECT/'Assets/Editor/DemoStoneBuilder.cs')
    # Render-only dependency shim. Never exported to the real project.
    (PROJECT/'Assets/ReviewSessionShim.cs').write_text('''using UnityEngine;
namespace WeatherHousePrototype {
public class ReviewPhotoRig : MonoBehaviour { public bool Active; }
public class GameplaySession : MonoBehaviour { public Camera PlayerViewCamera; public ReviewPhotoRig PhotoRig; }
}''')
    # Only serialized authoring fields are needed in isolation. The actual production script GUID is preserved.
    # This shim is excluded from publication; shutter/visibility runtime tests belong to the integration task.
    inscription=Path('Assets/Game/Life/Photography/LifeGlyphInscription.cs')
    copy(inscription)
    fields=['public string inscriptionId;', 'public string[] glyphIds = Array.Empty<string>();',
            'public Renderer inscriptionRenderer;', 'public Transform readingFace;', 'public Vector2 readingSize = Vector2.one;']
    production=(ROOT/inscription).read_text(encoding='utf-8-sig')
    assert all(field in production for field in fields), 'Inscription authoring contract changed'
    (PROJECT/inscription).write_text('using System; using UnityEngine; namespace WeatherHouse.Life { public sealed class LifeGlyphInscription : MonoBehaviour {'+'\n'.join(fields)+'}}')
    report=[]
    for spec in SPECS:
        folder=PROJECT/DEST/spec['id']; folder.mkdir(parents=True,exist_ok=True)
        source=Path('C:/Users/31483/.codex/generated_images/01a1855d-40ec-7da2-920d-bfb41bc2a999')/spec['source']
        shutil.copy2(source,folder/'Illustration.png')
        a=np.asarray(Image.open(source).convert('RGBA'),dtype=np.float32)/255
        rgb,alpha=a[...,:3],a[...,3]
        # Only actual opaque ivory strokes become grooves; invisible glow RGB is ignored.
        # Classify ivory as one continuous recess: pigment grain must not turn into noisy micro-craters.
        threshold=.81 if spec['id']=='sea' else .66
        ivory=((rgb.min(axis=2)>threshold)&(np.ptp(rgb,axis=2)<.32)).astype(np.float32)
        line=ivory*np.clip((alpha-.2)/.5,0,1)
        ys,xs=np.where(alpha>.5)
        box=[int(xs.min()),int(ys.min()),int(xs.max()+1),int(ys.max()+1)]
        cw,ch=box[2]-box[0],box[3]-box[1]
        # Fit the unmodified artwork isotropically above the glyph reservation.
        metrePerPixel=min(spec['faceWidth']*.96/cw,spec['faceHeight']*(.97-spec['artBottom'])/ch)
        rectW=cw*metrePerPixel/spec['faceWidth']; rectH=ch*metrePerPixel/spec['faceHeight']
        left=(1-rectW)/2; bottom=spec['artBottom']+(.97-spec['artBottom']-rectH)/2
        w=1024 if spec['id']=='look' else 2048
        h=round(w*spec['faceHeight']/spec['faceWidth'])
        x=round(left*w); y=round((1-bottom-rectH)*h); rw=round(rectW*w); rh=round(rectH*h)
        coverage=np.zeros((h,w),np.float32)
        coverage[y:y+rh,x:x+rw]=np.asarray(Image.fromarray(line[box[1]:box[3],box[0]:box[2]]).resize((rw,rh),Image.Resampling.LANCZOS))
        coverage=np.clip(coverage,0,1)
        glyph=CANONICAL/'PNG'/f"{spec['id']}.png"
        svg=CANONICAL/'SVG'/f"{spec['id']}.svg"
        glyphStatus='pending canonical glyph; no invented substitute'
        # Canonical source only. Its existing topology is kept; line thickness can be reviewed separately.
        if glyph.exists():
            ga=np.asarray(Image.open(glyph).convert('RGBA'),np.float32)[...,3]/255
            gy,gx=np.where(ga>.1); ga=ga[gy.min():gy.max()+1,gx.min():gx.max()+1]
            gh=round(h*(.22 if spec['id']=='sea' else .155)); gw=round(gh*ga.shape[1]/ga.shape[0])
            gx0=round(w*spec['glyphX']-gw/2); gy0=h-max(26,round(h*.035))-gh
            target=helpers.stroke_width(coverage)
            resized=np.asarray(Image.fromarray(ga).resize((gw,gh),Image.Resampling.LANCZOS))
            thick,glyphCheck=helpers.match_glyph(np.pad(resized,20),target)
            coverage[gy0-20:gy0+gh+20,gx0-20:gx0+gw+20]=np.maximum(coverage[gy0-20:gy0+gh+20,gx0-20:gx0+gw+20],thick)
            glyphStatus=dict(source=str(svg),sha256=hashlib.sha256(svg.read_bytes()).hexdigest(),stroke=glyphCheck)
            shutil.copy2(glyph,folder/'CanonicalGlyph.png')
            shutil.copy2(svg,folder/'CanonicalGlyph.svg')
        yy,xx=np.where(coverage>.2)
        spec['readX']=((xx.max()+xx.min()+1)/(2*w)-.5)*spec['faceWidth']
        spec['readY']=(.5-(yy.max()+yy.min()+1)/(2*h))*spec['faceHeight']+spec['height']*.49
        spec['readWidth']=(xx.max()-xx.min()+1)/w*spec['faceWidth']+.025
        spec['readHeight']=(yy.max()-yy.min()+1)/h*spec['faceHeight']+.025
        _,normal,packed=make_maps(coverage,spec['faceWidth'],.08,2.5,spec['faceHeight'])
        Image.fromarray(np.rint((normal*.5+.5)*255).astype(np.uint8)).save(folder/'Normal.png')
        Image.fromarray(np.rint(np.clip(packed,0,1)*255).astype(np.uint8)).save(folder/'Mask.png')
        # Map projection UV directly back into original source, with matching raster placement.
        rx,rhUV=rw/w,rh/h; lx,by=x/w,1-(y+rh)/h
        sx=(cw/a.shape[1])/rx; sy=(ch/a.shape[0])/rhUV
        spec['fillUV']=[sx,sy,box[0]/a.shape[1]-sx*lx,1-box[3]/a.shape[0]-sy*by]
        spec['glyphReady']=glyph.exists()
        assert coverage.max()>.9 and np.mean(coverage>.5)>.01
        assert np.max(coverage[[0,-1],:])==0 and np.max(coverage[:,[0,-1]])==0, 'clipped border'
        report.append(dict(id=spec['id'],source=spec['source'],sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                           glyph=glyphStatus,artBBox=box,fillUV=spec['fillUV'],size=[w,h],lineFraction=float(np.mean(coverage>.5))))
    (PROJECT/'specs.json').write_text(json.dumps({'items':SPECS},ensure_ascii=False,indent=2),encoding='utf-8')
    (HERE/'map-checks.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(report,ensure_ascii=False))

if __name__=='__main__': prepare()
