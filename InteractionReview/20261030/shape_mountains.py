"""Offline perimeter landform pass. Protected height samples remain byte-identical."""
from pathlib import Path
import json, numpy as np
from PIL import Image, ImageFilter
P=Path(__file__).resolve().parent
info=json.loads((P/'terrain-info.json').read_text()); n=info['resolution']; spacing=180/(n-1)
before=np.fromfile(P/'terrain-before.bin',dtype='<f4').reshape(n,n)
mask=np.fromfile(P/'mountain-mask.bin',dtype='<f4').reshape(n,n)
t=np.clip((before-8)/10,0,1)
mask=mask*(t*t*(3-2*t))
mask.astype('<f4').tofile(P/'mountain-effective-mask.bin')
rng=np.random.default_rng(43130)
def noise(size):
    grid=rng.random((size,size)).astype('float32')
    return np.asarray(Image.fromarray(grid).resize((n,n),Image.Resampling.BICUBIC)).copy()
def blur(h,radius):
    # Separable binomial smoothing keeps the metre-valued field in floating point.
    result=h.copy()
    for _ in range(radius):
        pad=np.pad(result,1,mode='edge')
        result=(pad[1:-1,1:-1]*4+pad[:-2,1:-1]+pad[2:,1:-1]+pad[1:-1,:-2]+pad[1:-1,2:])/8
    return result
x,z=np.meshgrid(np.linspace(-90,90,n),np.linspace(-90,90,n))
broad=noise(7); medium=noise(18); detail=noise(38)
west_axis=-80 + 4*np.sin(z*.038)+2*np.sin(z*.093+1)
north_axis=80 + 4*np.sin(x*.036+2)+2*np.cos(x*.081)
west_crest=23+5*np.sin(z*.049+.5)+4*np.cos(z*.093)
north_crest=24+6*np.cos(x*.046+1)+3*np.sin(x*.105)
west=west_crest*np.exp(-.5*((x-west_axis)/(12+4*broad))**2)
north=north_crest*np.exp(-.5*((z-north_axis)/(12+4*broad))**2)
ridge=np.maximum(west,north)
# Secondary spurs break up the old uniform walls; broad landforms dominate small detail.
target=5+ridge+((broad-.5)*5+(medium-.5)*3+(detail-.5)*.65)*np.clip(ridge/12,0,1)
target=blur(target,2)
initial=target.copy()
def sample(px,pz):
    ix=px.astype(int);iz=pz.astype(int);u=px-ix;v=pz-iz
    a,b,c,d=target[iz,ix],target[iz,ix+1],target[iz+1,ix],target[iz+1,ix+1]
    return (a*(1-u)+b*u)*(1-v)+(c*(1-u)+d*u)*v, (b-a)*(1-v)+(d-c)*v, (c-a)*(1-u)+(d-b)*u
def scatter(px,pz,amount):
    ix=px.astype(int);iz=pz.astype(int);u=px-ix;v=pz-iz
    for dx,dz,w in [(0,0,(1-u)*(1-v)),(1,0,u*(1-v)),(0,1,(1-u)*v),(1,1,u*v)]:
        np.add.at(target,(iz+dz,ix+dx),amount*w)
for batch in range(12):
    px=rng.uniform(2,n-3,1000);pz=rng.uniform(2,n-3,1000)
    vx=np.zeros(1000);vz=vx.copy();sediment=vx.copy();speed=np.ones(1000);water=np.ones(1000)
    for step in range(55):
        old,gx,gz=sample(px,pz)
        vx=.3*vx-.7*gx;vz=.3*vz-.7*gz
        norm=np.maximum(np.hypot(vx,vz),1e-8);vx/=norm;vz/=norm
        nx=np.clip(px+vx,1,n-2.001);nz=np.clip(pz+vz,1,n-2.001)
        new,_,_=sample(nx,nz);dh=new-old
        capacity=np.maximum(-dh*speed*water*2,.01)
        deposit=np.where(dh>0,np.minimum(dh,sediment),np.maximum(sediment-capacity,0)*.25)
        erode=np.where(dh<0,np.minimum(-dh,np.maximum(capacity-sediment,0)*.18),0)
        erode=np.minimum(erode,.09)
        scatter(px,pz,deposit-erode);sediment+=erode-deposit
        speed=np.sqrt(np.maximum(.01,speed*speed-dh*3));water*=.97
        px,pz=nx,nz
    scatter(px,pz,sediment)
    target=.75*target+.25*blur(target,2)
target=blur(target,3)
after=(before+.65*mask*(target-before)).astype('<f4')
after[mask==0]=before[mask==0]
assert np.isfinite(after).all() and after.min()>=-8 and after.max()<44
assert np.array_equal(after[mask==0],before[mask==0])
assert np.max(np.abs(after-before))<15
after.tofile(P/'terrain-after.bin')
def preview(h,name):
    dz,dx=np.gradient(h,spacing); normal=np.sqrt(1+dx*dx+dz*dz)
    shade=np.clip((.65-dx*.4+dz*.5)/normal+.3,.18,1.15)
    color=np.array([171,180,137],dtype=float)
    rgb=np.clip(color*shade[:,:,None],0,255).astype('uint8')
    Image.fromarray(rgb[::-1]).resize((1026,1026)).save(P/name)
preview(before,'terrain-shape-before.png');preview(after,'terrain-shape-after.png')
summary={'result':'PASS','resolution':n,'changedSamples':int(np.count_nonzero(after!=before)),
 'protectedSamplesUnchanged':int(np.count_nonzero(mask==0)),'maxChangeMeters':float(np.max(np.abs(after-before))),
 'maxHeightBefore':float(before.max()),'maxHeightAfter':float(after.max()),'hydraulicDroplets':12000,
 'reference':'https://www.redblobgames.com/maps/terrain-from-noise/','scope':'west and north perimeter only; roads, water, habitats, structures protected'}
(P/'mountain-checks.json').write_text(json.dumps(summary,indent=2),encoding='utf-8');print(json.dumps(summary))
