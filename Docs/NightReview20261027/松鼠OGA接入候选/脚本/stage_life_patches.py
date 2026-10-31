import hashlib,json
from pathlib import Path
S=Path(__file__).parent;ROOT=Path(r'C:/unity project/Weather House');DEST=S/'source-candidate'
files=['Assets/Game/Life/World/LifeWorld.Setup.cs','Assets/Game/Life/World/LifeWorld.Drinking.cs']
baseline={name:hashlib.sha256((ROOT/name).read_bytes()).hexdigest() for name in files}
setup=(ROOT/files[0]).read_text(encoding='utf-8-sig')
setup=setup.replace('using WeatherHouse.WildlifeJourney;','using WeatherHouse.WildlifeJourney;\nusing WeatherHouse.SquirrelOga;')
old='''                AnimationClip down=Clip("SquirrelRestDown"),sleep=Clip("SquirrelRestLoop"),up=Clip("SquirrelRestUp");
                if(down&&sleep&&up)
                {
                    b.oldIdle=b.wildlife.clips.idle;b.oldDown=b.wildlife.clips.restDown;b.oldSleep=b.wildlife.clips.restSleep;b.oldUp=b.wildlife.clips.restUp;b.changedClips=true;
                    b.wildlife.ConfigureLifeRest(down,sleep,up,Clip("SquirrelIdle"));'''
new='''                SquirrelOgaRig oga=b.wildlife.GetComponentInChildren<SquirrelOgaRig>(true);
                // An installed OGA model must never receive the former 27-bone clips.
                AnimationClip down=oga?oga.restDown:Clip("SquirrelRestDown"),sleep=oga?oga.restLoop:Clip("SquirrelRestLoop"),up=oga?oga.restUp:Clip("SquirrelRestUp");
                if(down&&sleep&&up)
                {
                    b.oldIdle=b.wildlife.clips.idle;b.oldDown=b.wildlife.clips.restDown;b.oldSleep=b.wildlife.clips.restSleep;b.oldUp=b.wildlife.clips.restUp;b.changedClips=true;
                    b.wildlife.ConfigureLifeRest(down,sleep,up,oga?oga.idle:Clip("SquirrelIdle"));'''
assert old in setup;setup=setup.replace(old,new,1)
drink=(ROOT/files[1]).read_text(encoding='utf-8-sig')
drink=drink.replace('using WeatherHouse.Hillside;','using WeatherHouse.Hillside;\nusing WeatherHouse.SquirrelOga;')
drink=drink.replace('public Binding animal;public LifeObject bowl;public Transform mouthBone;','public Binding animal;public LifeObject bowl;public Transform mouthBone;public Vector3 mouthLocal;')
old='''            AnimationClip clip=Clip("SquirrelDrink");
            Transform mouth=animal?.wildlife.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="Head");
            if(animal==null || !clip || !mouth || !bowl.waterVisual)return false;
            SkinnedMeshRenderer skin=animal.wildlife.GetComponentsInChildren<SkinnedMeshRenderer>().Where(s=>s.sharedMesh).OrderByDescending(s=>s.sharedMesh.vertexCount).FirstOrDefault();'''
new='''            SquirrelOgaRig oga=animal?.wildlife.GetComponentInChildren<SquirrelOgaRig>(true);
            AnimationClip clip=oga?oga.drink:Clip("SquirrelDrink");
            Transform mouth=oga?oga.head:animal?.wildlife.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="Head");
            if(animal==null || !clip || !mouth || !bowl.waterVisual)return false;
            // The new muzzle belongs to its own skin, not to the larger body renderer.
            SkinnedMeshRenderer skin=oga?oga.headSkin:animal.wildlife.GetComponentsInChildren<SkinnedMeshRenderer>().Where(s=>s.sharedMesh).OrderByDescending(s=>s.sharedMesh.vertexCount).FirstOrDefault();'''
assert old in drink;drink=drink.replace(old,new,1)
drink=drink.replace('mouthBone=mouth,clip=clip,','mouthBone=mouth,mouthLocal=oga?oga.mouthHeadLocal:DrinkMouthLocal,clip=clip,',1)
old='''            at=approach=default;facing=Quaternion.identity;float rim=.29f*Mathf.Abs(bowl.waterVisual.lossyScale.x)*.72f;
            foreach(float angle'''
new='''            at=approach=default;facing=Quaternion.identity;float rim=.29f*Mathf.Abs(bowl.waterVisual.lossyScale.x)*.72f;
            SquirrelOgaRig oga=animal.wildlife.GetComponentInChildren<SquirrelOgaRig>(true);
            Vector3 contact=oga?Vector3.Scale(oga.drinkRootContact,animal.wildlife.transform.lossyScale):DrinkRootContact;
            foreach(float angle'''
assert old in drink;drink=drink.replace(old,new,1)
drink=drink.replace('facing*(DrinkRootContact*animal.wildlife.ScaleFactor)','facing*(contact*animal.wildlife.ScaleFactor)',1)
drink=drink.replace('d.mouthBone.TransformPoint(DrinkMouthLocal)','d.mouthBone.TransformPoint(d.mouthLocal)',1)
for name,value in zip(files,[setup,drink]):
 path=DEST/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_text(value,encoding='utf-8',newline='\n')
report={'status':'STAGED_ONLY_NO_ACTUAL_ASSETS_CS_WRITE','baselineSha256':baseline,'candidateSha256':{name:hashlib.sha256((DEST/name).read_bytes()).hexdigest() for name in files},'preserved':'legacy rest/drink path, water consumption threshold/contact window, pair approach, return target/rest, animal root and population','change':'OGA same-rig rest/idle and drink; model-specific head skin + calibrated head-local lip and root contact; no old clip fallback for a present OGA profile','pending':'Explicit Unity installer geometry check; true BakeMesh plus TransformPoint retained consistently with this project 2022.3 skinning reference'}
(S/'life-patches-baseline.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2))
