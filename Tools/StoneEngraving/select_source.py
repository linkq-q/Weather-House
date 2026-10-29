"""Read the saved Unity scene without occupying or changing its live editor."""
import json
import re
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SCENE = ROOT / 'Assets/Scenes/WeatherHouse_WatersideV2.unity'
text = SCENE.read_text(encoding='utf-8-sig')
blocks = {int(m.group(2)): (int(m.group(1)), m.group(3)) for m in
          re.finditer(r'^--- !u!(\d+) &(-?\d+)[^\n]*\n(.*?)(?=^--- !u!|\Z)', text, re.M | re.S)}


def ref(body, key):
    m = re.search(r'  '+key+r': \{fileID: (-?\d+)', body)
    return int(m.group(1)) if m else 0


def vec(body, key, count=3):
    m = re.search(r'  '+key+r': \{([^}]+)\}', body)
    return [float(v) for v in re.findall(r'[xyzw]: ([^,}]+)', m.group(1))] if m else [0]*count


transforms = {ref(b, 'm_GameObject'): k for k, (t,b) in blocks.items() if t == 4}
names = {k: re.search(r'  m_Name: (.*)',b).group(1) for k,(t,b) in blocks.items() if t == 1 and '  m_Name:' in b}
renderers = {ref(b,'m_GameObject'): b for t,b in blocks.values() if t == 23}
matrices = {}


def world(tid):
    if not tid or tid not in blocks: return np.eye(4), []
    if tid in matrices: return matrices[tid]
    b = blocks[tid][1]
    p = vec(b,'m_LocalPosition'); s=vec(b,'m_LocalScale'); q=vec(b,'m_LocalRotation',4)
    if not re.search('m_LocalScale:',b): return np.eye(4), []
    x,y,z,w=q
    r=np.array([[1-2*y*y-2*z*z,2*x*y-2*z*w,2*x*z+2*y*w],
                [2*x*y+2*z*w,1-2*x*x-2*z*z,2*y*z-2*x*w],
                [2*x*z-2*y*w,2*y*z+2*x*w,1-2*x*x-2*y*y]])
    m=np.eye(4); m[:3,:3]=r@np.diag(s); m[:3,3]=p
    parent,path=world(ref(b,'m_Father'))
    result=(parent@m,path+[names.get(ref(b,'m_GameObject'),str(tid))])
    matrices[tid]=result
    return result


candidates=[]
for t,b in blocks.values():
    if t!=33: continue
    go=ref(b,'m_GameObject'); name=names.get(go,'')
    if not re.search(r'(Stone_[1-4]|Rock_[12])', name): continue
    mesh=re.search(r'm_Mesh: \{fileID: (-?\d+), guid: ([0-9a-f]+)',b)
    if not mesh: continue
    m,path=world(transforms.get(go,0))
    material=re.search(r'm_Materials:\s*\n\s*- \{fileID: (-?\d+), guid: ([0-9a-f]+)',renderers.get(go,''))
    candidates.append(dict(game_object_id=go,name=name,path='/'.join(path),
                           scale=np.linalg.norm(m[:3,:3],axis=0).tolist(),
                           world_position=m[:3,3].tolist(),world_matrix=m.flatten().tolist(),
                           mesh_file_id=int(mesh[1]),mesh_guid=mesh[2],
                           material_guid=material[2] if material else '',
                           scale_volume=float(abs(np.linalg.det(m[:3,:3])))))
candidates.sort(key=lambda c:c['scale_volume'],reverse=True)
prefab_candidates=[]
for block_id,(t,b) in blocks.items():
    if t!=1001: continue
    props={m[1]:m[2].strip() for m in re.finditer(r'propertyPath: ([^\n]+)\n\s*value: ([^\n]*)',b)}
    name=props.get('m_Name','')
    if not re.search(r'(Rock|Stone|Boulder|Cliff)',name,re.I): continue
    source=re.search(r'm_SourcePrefab: \{fileID: (-?\d+), guid: ([0-9a-f]+)',b)
    if not source: continue
    scale=[float(props.get('m_LocalScale.'+a,1)) for a in 'xyz']
    pos=[float(props.get('m_LocalPosition.'+a,0)) for a in 'xyz']
    rot=[float(props.get('m_LocalRotation.'+a,1 if a=='w' else 0)) for a in 'xyzw']
    materials=re.findall(r'propertyPath: m_Materials.Array.data\[0\]\n\s*value:.*?\n\s*objectReference: \{fileID: [-\d]+, guid: ([0-9a-f]+)',b)
    prefab_candidates.append(dict(prefab_instance_id=block_id,name=name,prefab_guid=source[2],
                                  scale=scale,local_position=pos,local_rotation=rot,
                                  material_guids=sorted(set(materials)),scale_volume=float(np.prod(scale))))
prefab_candidates.sort(key=lambda c:c['scale_volume'],reverse=True)
out=ROOT/'StoneEngravingReview/20261029';out.mkdir(parents=True,exist_ok=True)
(out/'source-candidates.json').write_text(json.dumps(candidates,ensure_ascii=False,indent=2),encoding='utf-8')
(out/'prefab-candidates.json').write_text(json.dumps(prefab_candidates,ensure_ascii=False,indent=2),encoding='utf-8')
print('Saved',len(prefab_candidates),'rock prefab candidates to',out/'prefab-candidates.json')
