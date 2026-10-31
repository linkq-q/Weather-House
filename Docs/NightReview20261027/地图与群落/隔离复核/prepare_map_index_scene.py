"""Prepare a map-only Git index patch. Never writes Assets or the Git index."""
from pathlib import Path
import collections
import hashlib
import json
import re
import subprocess

root = Path(r'C:\unity project\Weather House')
stage = Path(__file__).parent / 'map-staging'
scene = 'Assets/Scenes/WeatherHouse_WatersideV2.unity'
saved = json.loads((stage / 'landscape-save-result.json').read_text('utf-8-sig'))
plan = json.loads((stage / 'landscape-after.json').read_text('utf-8-sig'))
sha = lambda raw: hashlib.sha256(raw).hexdigest().upper()
before = Path(saved['backupPath']).read_bytes().replace(b'\r\n', b'\n')
after = (root / scene).read_bytes().replace(b'\r\n', b'\n')
head = subprocess.check_output(['git', 'show', 'HEAD:' + scene], cwd=root)
rx = re.compile(rb'^--- !u!(\d+) &(-?\d+)[^\n]*\n.*?(?=^--- !u!|\Z)', re.M | re.S)

def parse(raw):
    matches = list(rx.finditer(raw))
    blocks = {m[2].decode(): (m[1].decode(), m[0]) for m in matches}
    assert len(blocks) == len(matches), 'Duplicate Unity object IDs'
    return raw[:matches[0].start()], [m[2].decode() for m in matches], blocks

bp, bo, bd = parse(before)
ap, ao, ad = parse(after)
hp, ho, hd = parse(head)
assert bp == ap == hp
changed = {i for i in bd.keys() & ad.keys() if bd[i] != ad[i]}
added = ad.keys() - bd.keys()
assert not (bd.keys() - ad.keys()), 'Map Save deleted original blocks'
declared = {move['id'].rsplit('-', 1)[1] for move in plan['moves']}
scene_roots = {i for i in changed if ad[i][0] == '1660057539'}
owned = declared | scene_roots
assert len(declared) == 96 and len(scene_roots) == 1
assert owned <= changed
assert all(ad[i][0] == '1001' for i in declared)
ignored = changed - owned
# Unity Save reordered two unchanged serialized fields in sixteen water components.
assert len(ignored) == 16 and all(ad[i][0] == '114' for i in ignored)
for i in ignored:
    assert sorted(bd[i][1].splitlines()) == sorted(ad[i][1].splitlines()), 'Unexpected non-map content change'
assert all(i in hd and hd[i] == bd[i] for i in owned), 'Owned block overlaps earlier dirty changes'
assert not (added & hd.keys()), 'New map ID collides with HEAD'
assert collections.Counter(ad[i][0] for i in added) == {'1': 35, '4': 35, '23': 34, '33': 34, '64': 8, '65': 3, '205': 23}

# Insert new blocks relative to their next existing HEAD block, retaining HEAD ordering.
buckets = collections.defaultdict(list)
next_head = None
for i in reversed(ao):
    if i in hd: next_head = i
    elif i in added: buckets[next_head].append(i)
for values in buckets.values(): values.reverse()
parts = [hp]
for i in ho:
    parts.extend(ad[j][1] for j in buckets[i])
    parts.append(ad[i][1] if i in owned else hd[i][1])
parts.extend(ad[j][1] for j in buckets[None])
shadow = b''.join(parts)
sp, so, sd = parse(shadow)
assert sp == hp and sd.keys() == hd.keys() | added
assert all(sd[i] == hd[i] for i in hd.keys() - owned)
assert all(sd[i] == ad[i] for i in owned | added)

local_refs = {m.decode() for i in owned | added for m in re.findall(rb'\{fileID: (-?\d+)\}', sd[i][1])} - {'0'}
assert not (local_refs - sd.keys()), 'Map references an object found only in the earlier dirty scene'
def father(i):
    match = re.search(rb'^  m_Father: \{fileID: (-?\d+)\}', sd[i][1], re.M)
    return match[1].decode() if match else None
def children(i):
    match = re.search(rb'^  m_Children:(.*?)^  m_Father:', sd[i][1], re.M | re.S)
    return [x.decode() for x in re.findall(rb'fileID: (-?\d+)', match[1])] if match else []
roots = {x.decode() for x in re.findall(rb'fileID: (-?\d+)', sd[next(iter(scene_roots))][1])}
for i in added:
    if sd[i][0] != '4': continue
    parent = father(i)
    assert parent is not None
    if parent == '0': assert i in roots
    else: assert i in children(parent)
    for child in children(i): assert father(child) == i

guids = {x.decode() for i in owned | added for x in re.findall(rb'guid: ([0-9a-f]{32})', sd[i][1])}
meta_output = subprocess.check_output(['git', 'grep', '-h', '^guid: ', 'HEAD', '--', '*.meta'], cwd=root)
head_guids = {x.decode() for x in re.findall(rb'^guid: ([0-9a-f]{32})', meta_output, re.M)}
builtins = {g for g in guids if re.fullmatch(r'0{16}[ef]0{15}', g)}
assert not (guids - head_guids - builtins), 'New map binding requires an uncommitted source asset'

shadow_path = stage / 'map-index-scene.unity.txt'
head_path = stage / 'map-head-scene.unity.txt'
shadow_path.write_bytes(shadow)
head_path.write_bytes(head)
diff = subprocess.run(['git', 'diff', '--no-index', '--no-ext-diff', '--unified=3', '--', str(head_path), str(shadow_path)], cwd=root, capture_output=True)
assert diff.returncode == 1
out = []
for line in diff.stdout.splitlines(keepends=True):
    if line.startswith(b'diff --git '): line = f'diff --git a/{scene} b/{scene}\n'.encode()
    elif line.startswith(b'--- '): line = f'--- a/{scene}\n'.encode()
    elif line.startswith(b'+++ '): line = f'+++ b/{scene}\n'.encode()
    out.append(line)
patch = stage / 'map-owned-scene-for-head.patch'
patch.write_bytes(b''.join(out))
check = subprocess.run(['git', 'apply', '--cached', '--check', str(patch)], cwd=root, capture_output=True)
assert check.returncode == 0, check.stderr.decode(errors='replace')
report = {'status': 'PASS_MAP_ONLY_INDEX_PATCH_STATIC_GUARDS', 'scene': scene,
          'headSceneSha256': sha(head), 'indexCandidateSha256': sha(shadow), 'actualSavedSceneSha256': saved['savedSceneSha256'],
          'preservedHeadBlocks': len(hd) - len(owned), 'replacedOwnedBlocks': len(owned), 'addedOwnedBlocks': len(added),
          'ignoredSaveReorderBlocks': sorted(ignored), 'localReferencesResolved': len(local_refs), 'assetGuidsResolvedInHead': len(guids),
          'ownedExistingIds': sorted(owned), 'ownedAddedIds': sorted(added), 'patch': str(patch), 'patchSha256': sha(patch.read_bytes()),
          'patchBytes': patch.stat().st_size, 'cachedCheck': 'PASS',
          'boundary': 'Static index isolation only. Play evidence is from the actual saved scene with the preserved earlier dirty baseline. Assets and index were not changed by this script.'}
(stage / 'map-index-isolation-proof.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), 'utf-8')
print(json.dumps({k:v for k,v in report.items() if k not in ['ownedExistingIds','ownedAddedIds','ignoredSaveReorderBlocks']}, ensure_ascii=True))
