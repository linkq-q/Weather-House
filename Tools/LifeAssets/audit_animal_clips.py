"""Read existing animal exports; this script never writes imported models or Unity scenes."""
import bpy
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
entries = {
    "squirrel": ["Assets/Experiments/Squirrel/Models/" + name + ".fbx" for name in ("turn", "wave", "walk_biped", "run_biped")],
    "sheep": ["Assets/Experiments/QuadrupedMotion/EightAnimals/Models/sheep/" + name + ".fbx" for name in ("idle", "eat", "react", "walk")],
    "deer": ["Assets/Experiments/QuadrupedMotion/EightAnimals/Models/deer/" + name + ".fbx" for name in ("idle", "eat", "react", "walk_v3")],
    "fox": ["Assets/Experiments/QuadrupedMotion/EightAnimals/Models/fox/idle_v3.fbx"] +
           ["Assets/Game/World/Wildlife/Animation/Fox/fox_" + name + ".fbx" for name in ("eat", "restdown", "restsleep", "restup")],
}
report = []
for species, paths in entries.items():
    reference = None
    for relative in paths:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(ROOT / relative))
        rigs = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
        assert len(rigs) == 1, (relative, len(rigs))
        rig = rigs[0]
        signature = [(b.name, b.parent.name if b.parent else '') for b in rig.data.bones]
        if reference is None:
            reference = signature
        actions = [{"name": a.name, "frameRange": list(a.frame_range)} for a in bpy.data.actions]
        assert actions, relative
        assert all(a['frameRange'][1] > a['frameRange'][0] for a in actions), relative
        report.append({"species": species, "path": relative, "boneCount": len(signature),
                       "sameHierarchyAsSpeciesReference": signature == reference,
                       "actions": actions, "bones": [b[0] for b in signature]})
(ROOT / 'Docs/LifeGameplay/Assets/animal-clip-audit.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('ANIMAL_CLIP_AUDIT_PASS',len(report))
