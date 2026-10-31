"""Stage the fixed approved selection without consulting or changing a loaded scene."""
from pathlib import Path
import json,shutil
from prepare import ROOT,HERE,PROJECT

def copy(path):
    dest=PROJECT/path;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(ROOT/path,dest)
    if Path(str(ROOT/path)+'.meta').exists():shutil.copy2(str(ROOT/path)+'.meta',str(dest)+'.meta')

def stage():
    (PROJECT/'Packages').mkdir(parents=True,exist_ok=True)
    manifest=json.loads((ROOT/'Packages/manifest.json').read_text())
    deps={k:v for k,v in manifest['dependencies'].items() if k.startswith('com.unity.modules.')}
    for name in ['com.unity.render-pipelines.universal','com.unity.render-pipelines.core','com.unity.shadergraph',
                 'com.unity.searcher','com.unity.mathematics','com.unity.burst','com.unity.render-pipelines.universal-config','com.unity.ugui']:
        folders=list((ROOT/'Library/PackageCache').glob(name+'@*'))
        if len(folders)!=1:raise RuntimeError('Ambiguous installed package '+name)
        deps[name]='file:'+folders[0].as_posix()
    (PROJECT/'Packages/manifest.json').write_text(json.dumps({'dependencies':deps},indent=2))
    for name in ['ProjectVersion.txt','ProjectSettings.asset','GraphicsSettings.asset','QualitySettings.asset']:copy(Path('ProjectSettings')/name)
    for path in (ROOT/'Assets/Settings').glob('*.asset'):copy(path.relative_to(ROOT))
    selection=json.loads((HERE/'approved-selection.json').read_text(encoding='utf-8'))
    for item in selection['selected']:copy(Path(item['fit']['path']))
    copy(Path('Assets/Experiments/StoneEngraving/ProjectedEngraving.shader'))
    (PROJECT/'Assets/Editor').mkdir(parents=True,exist_ok=True)
    shutil.copy2(HERE/'StoneProductionBuilder.cs',PROJECT/'Assets/Editor/StoneProductionBuilder.cs')
    shutil.copy2(HERE/'approved-selection.json',PROJECT/'approved-selection.json')
    print('Staged seven approved meshes in isolated project.')

if __name__=='__main__':stage()
