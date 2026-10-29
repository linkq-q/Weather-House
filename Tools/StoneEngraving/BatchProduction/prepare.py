"""Reproducible technical maps: approved fit, bounded AI chips, canonical glyph strokes."""
from pathlib import Path
import sys, json, shutil, hashlib
import numpy as np
from PIL import Image, ImageFilter

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[2]
sys.path.insert(0,str(HERE.parent))
from build_maps import make_maps, sparse_chips, stencil_coverage
PROJECT=ROOT/'StoneEngravingReview/20261029/UnityProject'
OUTPUT=ROOT/'StoneEngravingReview/20261029/SevenStoneProduction'

def distance(mask,cap=32,border=0):
    d=np.where(mask,cap,0).astype(np.float32)
    for _ in range(cap):
        p=np.pad(d,1,constant_values=border);n=d.copy()
        for dy,dx,cost in ((-1,0,1),(1,0,1),(0,-1,1),(0,1,1),(-1,-1,2**.5),(-1,1,2**.5),(1,-1,2**.5),(1,1,2**.5)):
            np.minimum(n,p[1+dy:1+dy+d.shape[0],1+dx:1+dx+d.shape[1]]+cost,out=n)
        if np.array_equal(n,d):break
        d=n
    return d

def stroke_width(alpha):
    d=distance(np.asarray(alpha)>.5);p=np.pad(d,1);ridge=d>=2
    for dy,dx in ((-1,0),(1,0),(0,-1),(0,1),(-1,-1),(-1,1),(1,-1),(1,1)):
        ridge &= d>=p[1+dy:1+dy+d.shape[0],1+dx:1+dx+d.shape[1]]-.001
    return float(np.median(2*d[ridge])) if ridge.any() else 0.

def topology(alpha):
    """Component and enclosed-gap counts on a small glyph, ignoring antialias specks."""
    mask=np.pad(np.asarray(alpha)>.5,1)
    counts=[]
    for positive in (True,False):
        left=(mask==positive).copy();n=0
        for y,x in zip(*np.where(left)):
            if not left[y,x]:continue
            stack=[(y,x)];left[y,x]=False;area=0
            while stack:
                yy,xx=stack.pop();area+=1
                for dy,dx in ((-1,0),(1,0),(0,-1),(0,1)):
                    ny,nx=yy+dy,xx+dx
                    if 0<=ny<left.shape[0] and 0<=nx<left.shape[1] and left[ny,nx]:
                        left[ny,nx]=False;stack.append((ny,nx))
            n+=area>=5
        counts.append(n if positive else n-1)
    return tuple(counts)

def match_glyph(alpha,target):
    original=topology(alpha);before=stroke_width(alpha)
    image=Image.fromarray(np.uint8(np.clip(alpha,0,1)*255))
    for radius in range(0,18):
        candidate=np.asarray(image if radius==0 else image.filter(ImageFilter.MaxFilter(2*radius+1)),np.float32)/255
        width=stroke_width(candidate)
        if topology(candidate)!=original:raise ValueError('Glyph dilation would merge strokes or close a gap.')
        if width>=target:return candidate,dict(before=before,after=width,target=target,radius=radius,topology=list(original))
    raise ValueError('Unable to reach picture stroke width within safe glyph padding.')

def bounded_chips(base,candidate,seed):
    """Only alter a narrow edge band. Core and distant background remain identical."""
    inside=distance(base>.5,8)
    outside=distance(base<=.5,8,border=8)
    candidate=np.where(inside>2,base,candidate)
    candidate=np.where(outside>5,base,candidate)
    return sparse_chips(base,candidate,.23,seed)

def prepare():
    OUTPUT.mkdir(exist_ok=True)
    source=HERE/'Source';source.mkdir(exist_ok=True)
    generation=json.loads((HERE/'generation.json').read_text(encoding='utf-8'))
    entries={e['id']:e for e in generation['entries']}
    dimensions={e['id']:e for e in json.loads((OUTPUT/'dimensions.json').read_text())['items']}
    layout=json.loads((HERE/'approved-layout.json').read_text(encoding='utf-8'))
    report=[]
    for index,item in enumerate(layout['items']):
        name=item['id'];src=HERE.parent/'BatchPreview/Source'/f'{name}.png'
        candidate=source/f'{name}.png'
        if not candidate.exists():shutil.copy2(entries[name]['source'],candidate)
        base=stencil_coverage(src)
        altered=stencil_coverage(candidate)
        if altered.shape!=base.shape:
            altered=np.asarray(Image.fromarray(altered).resize((base.shape[1],base.shape[0]),Image.Resampling.LANCZOS))
        bbox=Image.fromarray(np.uint8(base*255)).getbbox()
        def resize(a):
            im=Image.fromarray(a).crop(bbox)
            return np.clip(np.asarray(im.resize((1536,round(im.height*1536/im.width)),Image.Resampling.LANCZOS)),0,1)
        art=resize(base);rough=bounded_chips(art,resize(altered),20261029+index)
        target=stroke_width(art)
        top=250 if name=='grass_tree' else 90
        coverage=np.zeros((top+art.shape[0]+290,1716),np.float32)
        assert abs(1716/coverage.shape[0]-item['aspect'])<1e-6
        coverage[top:top+art.shape[0],90:1626]=rough
        glyph_report=[]
        for glyph in item['glyphs']:
            key=glyph['id'];path=ROOT/'Assets/Game/Art/AnimalGlyphsV04'/f'{key}.png'
            sha=hashlib.sha256(path.read_bytes()).hexdigest()
            if sha!=glyph['sha256']:raise ValueError('Canonical glyph changed since approval: '+key)
            g=Image.open(path).convert('RGBA').getchannel('A');g=g.crop(g.getbbox())
            x,y,w,h=glyph['rect'];g=g.resize((w,h),Image.Resampling.LANCZOS)
            padded=np.pad(np.asarray(g,np.float32)/255,20)
            thick,metrics=match_glyph(padded,target)
            # One-pixel edge variation only; glyph cores and disconnected parts are protected.
            expanded=np.asarray(Image.fromarray(np.uint8(thick*255)).filter(ImageFilter.MaxFilter(3)),np.float32)/255
            distressed=sparse_chips(thick,expanded,.17,20261029+index+len(glyph_report)*31,24)
            assert topology(distressed)==topology(thick)
            coverage[y-20:y+h+20,x-20:x+w+20]=distressed
            glyph_report.append(dict(id=key,sha256=sha,rect=glyph['rect'],**metrics))
        assert np.isfinite(coverage).all()
        bbox=Image.fromarray(np.uint8(coverage*255)).getbbox()
        assert bbox[0]>=40 and bbox[1]>=20 and bbox[2]<=coverage.shape[1]-40 and bbox[3]<=coverage.shape[0]-20
        dim=dimensions[name]
        height,normal,mask=make_maps(coverage,dim['width'],.08,4,height_m=dim['height'])
        out=PROJECT/'Assets/Experiments/StoneEngraving/Seven'/name;out.mkdir(parents=True,exist_ok=True)
        Image.fromarray(np.uint8(np.rint((normal*.5+.5)*255))).save(out/'Normal.png')
        Image.fromarray(np.uint8(np.rint(mask*255))).save(out/'Mask.png')
        Image.fromarray(np.uint16(np.rint((1+height/.08)*65535))).save(out/'Height.png')
        technical=OUTPUT/name;technical.mkdir(exist_ok=True)
        Image.fromarray(np.uint8(np.rint((1-coverage)*255))).save(technical/'stencil.png')
        data=dict(id=name,title=item['title'],picture_width_px=target,glyphs=glyph_report,dimensions=dim,
            changed_art_pixels=int(np.count_nonzero(abs(rough-art)>.05)),core_preserved=bool(np.all(rough[distance(art>.5,8)>2]==art[distance(art>.5,8)>2])),
            depth_m=.08,seed=20261029+index,chip_probability=.23,
            source_sha256=hashlib.sha256(src.read_bytes()).hexdigest(),candidate_sha256=hashlib.sha256(candidate.read_bytes()).hexdigest())
        assert data['core_preserved'] and data['changed_art_pixels']>0
        report.append(data)
        print(name, 'picture',round(target,1),'glyphs',[(g['id'],round(g['before'],1),round(g['after'],1)) for g in glyph_report],flush=True)
    (OUTPUT/'maps-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')

if __name__=='__main__':prepare()
