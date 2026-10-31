"""Compile current sources and stage a small isolated Unity GPU/UI verification project."""
from pathlib import Path
import json
import shutil
import subprocess

root = Path(__file__).resolve().parents[2]
out = root / "SpellInkReview/20261029-firefly"
# Reuse the task's existing package/import cache while keeping each review's evidence separate.
project = root / "SpellInkReview/20261029/UnityProject"
unity = Path("C:/Program Files/Unity/Hub/Editor/2022.3.62f3c1/Editor/Data")
bee = root / "Library/Bee/artifacts/1900b0aE.dag"
for folder in ("Assets/Editor", "Assets/Resources", "Packages", "ProjectSettings"):
    (project / folder).mkdir(parents=True, exist_ok=True)

for editor in (False, True):
    name = "Assembly-CSharp-Editor" if editor else "Assembly-CSharp"
    target = project / ("Assets/Editor/WeatherHouseSpellChecks.dll" if editor else "Assets/WeatherHouseSpellCandidate.dll")
    lines = []
    for line in (bee / (name + ".rsp")).read_text(encoding="utf-8-sig").splitlines():
        if line.startswith("-out:"): line = f'-out:"{target}"'
        elif line.startswith("-refout:"): continue
        elif editor and ("Assembly-CSharp.dll" in line or "Assembly-CSharp.ref.dll" in line):
            line = f'-r:"{project / "Assets/WeatherHouseSpellCandidate.dll"}"'
        elif editor and line.strip('"').startswith("Assets/"): continue
        lines.append(line)
    source = "Assets/Game/Life/Editor/LifeSpellInkChecks.cs" if editor else "Assets/Game/Life/UI/LifeSpellInkGraphic.cs"
    if not any(line.strip('"') == source for line in lines): lines.append(f'"{source}"')
    if editor:
        lines.append('"Assets/Game/Life/Editor/LifeSpellInkPlayReview.cs"')
    if not editor:
        # Unity's cached response can predate new partial files in this shared checkout.
        known = {line.strip('"') for line in lines if line.strip('"').startswith("Assets/")}
        for folder in sorted({(root / name).parent for name in known}):
            for adjacent in sorted(folder.glob("*.cs")):
                relative = adjacent.relative_to(root).as_posix()
                if relative not in known:
                    lines.append(f'"{relative}"'); known.add(relative)
    response = out / (name + ".rsp")
    response.write_text("\n".join(lines), encoding="utf-8")
    run = subprocess.run([str(unity / "NetCoreRuntime/dotnet.exe"), str(unity / "DotNetSdkRoslyn/csc.dll"), "@" + str(response)], cwd=root, capture_output=True, text=True)
    (out / (name + ".compile.log")).write_text(run.stdout + run.stderr, encoding="utf-8")
    print(name, run.returncode, (run.stdout + run.stderr)[-4500:])
    if run.returncode: raise SystemExit(run.returncode)

manifest = json.loads((root / "Packages/manifest.json").read_text())
manifest["dependencies"] = {k:v for k,v in manifest["dependencies"].items() if k.startswith("com.unity.modules.") or k in ("com.unity.ugui", "com.unity.render-pipelines.universal", "com.unity.textmeshpro", "com.unity.timeline", "com.unity.visualscripting")}
(project / "Packages/manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
for name in ("ProjectVersion.txt", "ProjectSettings.asset"):
    shutil.copyfile(root / "ProjectSettings" / name, project / "ProjectSettings" / name)
for source, target in (("Assets/Game/Life/Shaders/Resources/LifeGesture", "LifeGesture"), ("Assets/Game/Resources/HudIcons", "HudIcons")):
    shutil.copytree(root / source, project / "Assets/Resources" / target, dirs_exist_ok=True)
font = "ZCOOLXiaoWei-Regular.ttf"
(project / "Assets/Resources/Fonts").mkdir(exist_ok=True)
shutil.copyfile(root / "Assets/Game/Resources/Fonts" / font, project / "Assets/Resources/Fonts" / font)
print(project)
