"""Copy only reviewed squirrel/turtle visual-size overrides from a baked scene copy.

Default writes a candidate beside --output. --apply also updates --target after
checking that the targeted original values still match --baseline.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re

DOC = re.compile(r'^--- !u!(\d+) &(-?\d+)[^\n]*\n.*?(?=^--- !u!|\Z)', re.M | re.S)
ENTRY = re.compile(
    r'^    - target: \{fileID: (-?\d+), guid: ([0-9a-f]+),\s*type: \d+\}\r?\n'
    r'      propertyPath: ([^\r\n]+)\r?\n'
    r'      value:([^\r\n]*)\r?\n'
    r'      objectReference:[^\r\n]*\r?\n', re.M)
ANIMALS = {
    '1008f139711d13d43a5a60cbe3aba1fc': ('squirrel', '4515995458381956832', '952801108737387063', range(14,20)),
    '838b30ea313e6354793f200010002785': ('turtle', '2988978243545320287', '8775068850878143476', range(43,48)),
}

def documents(text):
    return {m[2]: m for m in DOC.finditer(text) if m[1]=='1001'}

def entries(text):
    values = {}
    for m in ENTRY.finditer(text):
        key=(m[1],m[2],m[3])
        if key in values:
            raise ValueError('Duplicate override '+str(key))
        values[key]=m
    return values

def apply_overrides(baked, target, baseline):
    source_docs, target_docs, baseline_docs=documents(baked), documents(target), documents(baseline)
    changes=[]
    replacements=[]
    for identity, src in source_docs.items():
        source=src[0]
        prefab=re.search(r'm_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]+), type: \d+\}',source)
        if not prefab or prefab[1] not in ANIMALS:
            continue
        guid=prefab[1]
        species, agent, visual, expected_ids=ANIMALS[guid]
        values=entries(source)
        spawn=values.get((agent,guid,'spawnId'))
        if not spawn or int(spawn[4]) not in expected_ids:
            raise ValueError('Unexpected '+species+' identity')
        allowed={(agent,guid,'bakedVisualScaleFactor')}
        allowed.update((visual,guid,p+'.'+axis) for p in ('m_LocalScale','m_LocalPosition') for axis in 'xyz')
        factor=values.get((agent,guid,'bakedVisualScaleFactor'))
        if not factor or float(factor[4])<=0:
            raise ValueError('Source has not baked '+species+'/'+spawn[4])
        dst=target_docs[identity]; old=baseline_docs[identity]
        current, before=entries(dst[0]),entries(old[0])
        # Migration must keep the original configured identity, seed-range and root pose.
        for key in set(values)|set(current):
            if key not in allowed and (values.get(key)[4] if key in values else None)!=(current.get(key)[4] if key in current else None):
                raise ValueError('Baked clone differs beyond size override: '+str(key))
        edits=[]; added=[]; copied=[]
        for key in allowed:
            if (current.get(key)[4] if key in current else None)!=(before.get(key)[4] if key in before else None):
                raise ValueError('Targeted value changed since snapshot: '+str(key))
            source_entry=values.get(key); target_entry=current.get(key)
            if source_entry is None:
                # Zero offsets may legitimately remain inherited; never delete a pre-existing override.
                if target_entry is not None: raise ValueError('Missing baked override: '+str(key))
                continue
            source_text=source_entry[0].replace('\r\n','\n')
            newline='\r\n' if '\r\n' in dst[0] else '\n'
            source_text=source_text.replace('\n',newline)
            if target_entry:
                if target_entry[4]==source_entry[4]:continue
                edits.append((target_entry.start(),target_entry.end(),source_text))
            else:added.append(source_text)
            copied.append({'target':key[0],'property':key[2],'before':target_entry[4].strip() if target_entry else None,'after':source_entry[4].strip()})
        updated=dst[0]
        for start,end,text in sorted(edits,reverse=True):updated=updated[:start]+text+updated[end:]
        if added:
            marker='    m_RemovedComponents:'
            offset=updated.find(marker)
            if offset<0:raise ValueError('Missing prefab modification boundary')
            updated=updated[:offset]+''.join(sorted(added))+updated[offset:]
        replacements.append((dst.start(),dst.end(),updated))
        changes.append({'species':species,'spawnId':int(spawn[4]),'prefabInstance':identity,'overrides':copied})
    if sorted((v['species'],v['spawnId']) for v in changes)!=sorted((spec,i) for spec,_,_,ids in ANIMALS.values() for i in ids):
        raise ValueError('Expected exactly six squirrels and five turtles')
    for start,end,text in sorted(replacements,reverse=True):target=target[:start]+text+target[end:]
    return target,changes

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    for flag in ('baked','target','baseline','output'):parser.add_argument('--'+flag,type=Path,required=True)
    parser.add_argument('--apply',action='store_true')
    args=parser.parse_args()
    source=args.baked.read_bytes().decode('utf-8'); original=args.target.read_bytes()
    result,changes=apply_overrides(source,original.decode('utf-8'),args.baseline.read_bytes().decode('utf-8'))
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_bytes(result.encode('utf-8'))
    args.output.with_suffix('.changes.json').write_text(json.dumps(changes,ensure_ascii=False,indent=2),encoding='utf-8')
    if args.apply:
        if args.target.read_bytes()!=original:raise ValueError('Target changed during migration')
        args.target.write_bytes(result.encode('utf-8'))
    print(json.dumps({'animals':len(changes),'overrides':sum(len(v['overrides']) for v in changes),'applied':args.apply,
        'targetBeforeSha256':hashlib.sha256(original).hexdigest(),'candidateSha256':hashlib.sha256(result.encode('utf-8')).hexdigest()},indent=2))

if __name__=='__main__':main()
