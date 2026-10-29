"""Stage a small isolated Unity project; never open or save the main scene."""
import hashlib
import json
import re
import shutil
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
REVIEW=ROOT/'StoneEngravingReview/20261029'
PROJECT=REVIEW/'UnityProject'
PACKAGE='Assets/Experiments/StoneEngraving'


def copy(relative):
    src=ROOT/relative; dst=PROJECT/relative
    dst.parent.mkdir(parents=True,exist_ok=True)
    shutil.copy2(src,dst)
    if Path(str(src)+'.meta').exists(): shutil.copy2(str(src)+'.meta',str(dst)+'.meta')


def prepare():
    (PROJECT/'Packages').mkdir(parents=True,exist_ok=True)
    (PROJECT/'ProjectSettings').mkdir(parents=True,exist_ok=True)
    manifest=json.loads((ROOT/'Packages/manifest.json').read_text())
    deps={k:v for k,v in manifest['dependencies'].items() if k.startswith('com.unity.modules.')}
    for name in ['com.unity.render-pipelines.universal','com.unity.render-pipelines.core',
                 'com.unity.shadergraph','com.unity.searcher','com.unity.mathematics',
                 'com.unity.burst','com.unity.render-pipelines.universal-config','com.unity.ugui']:
        folders=list((ROOT/'Library/PackageCache').glob(name+'@*'))
        if len(folders)!=1: raise RuntimeError('Ambiguous installed package '+name)
        deps[name]='file:'+folders[0].as_posix()
    (PROJECT/'Packages/manifest.json').write_text(json.dumps({'dependencies':deps},indent=2))
    copy('ProjectSettings/ProjectVersion.txt')
    copy('ProjectSettings/ProjectSettings.asset')
    copy('ProjectSettings/GraphicsSettings.asset')
    copy('ProjectSettings/QualitySettings.asset')
    for f in (ROOT/'Assets/Settings').glob('*.asset'): copy(f.relative_to(ROOT))
    mesh='Assets/HIGHLANDS - Stylized Environment/Meshes/Forest/Rock_1.fbx'
    normal='Assets/HIGHLANDS - Stylized Environment/Textures/Forest/StonesRocks_Normal.png'
    material='Assets/Game/World/WatersideV2/Generated/Props/Props/Stones_Rocks_48235a3b.mat'
    for f in [mesh,normal,material]: copy(f)
    target=PROJECT/PACKAGE; (target/'Textures').mkdir(parents=True,exist_ok=True)
    for f in (REVIEW/'Maps').glob('*.png'): shutil.copy2(f,target/'Textures'/f.name)
    copy(PACKAGE+'/ProjectedEngraving.shader')
    (PROJECT/'Assets/Editor').mkdir(parents=True,exist_ok=True)
    shutil.copy2(ROOT/'Tools/StoneEngraving/StoneEngravingReviewBuilder.cs',PROJECT/'Assets/Editor/StoneEngravingReviewBuilder.cs')
    settings=json.loads((ROOT/'Tools/StoneEngraving/rain_water.json').read_text())
    source=next(c for c in json.loads((REVIEW/'prefab-candidates.json').read_text())
                if c['prefab_instance_id']==settings['source_prefab_instance_id']
                and c['prefab_guid']==settings['source_prefab_guid'])
    scene=ROOT/'Assets/Scenes/WeatherHouse_WatersideV2.unity'
    spec=dict(source_scene=scene.relative_to(ROOT).as_posix(),source_scene_sha256=hashlib.sha256(scene.read_bytes()).hexdigest(),
              source_prefab_instance_id=str(source['prefab_instance_id']),source_name=source['name'],
              mesh_path=mesh,material_path=material,mesh_file_id='-2605931710798131250',
              source_scale=source['scale'],source_position=source['local_position'],
              source_rotation=source['local_rotation'],groove_depth_m=settings['depth_m'],review_path=REVIEW.as_posix())
    (PROJECT/'source.json').write_text(json.dumps(spec,ensure_ascii=False,indent=2),encoding='utf-8')
    (REVIEW/'source-selected.json').write_text(json.dumps(spec,ensure_ascii=False,indent=2),encoding='utf-8')
    print(str(PROJECT))


if __name__=='__main__': prepare()
