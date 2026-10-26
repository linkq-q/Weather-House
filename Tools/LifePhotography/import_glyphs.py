"""Import the approved v04 alphabet; never replace the original 24 semantic records."""
import hashlib
import json
from pathlib import Path
import shutil
import struct
import uuid

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path(r"C:\Users\31483\Desktop\天气屋\字符设计\动物语字符设计_v04_摄影扩展")
catalog_path = ROOT / "Assets/Game/Resources/WeatherHouseGlyphs.json"
catalog = json.loads(catalog_path.read_text(encoding="utf-8-sig"))
approved = json.loads((SOURCE / "字库.json").read_text(encoding="utf-8-sig"))
by_id = {item["id"]: item for item in approved}
assert len(by_id) == len(approved) == 31
old_ids = [item["id"] for item in catalog["glyphs"]]
fields = ("id", "name", "meaning", "boundary", "body")
for item in catalog["glyphs"]:
    for field in fields:
        assert item[field] == by_id[item["id"]][field], (item["id"], field, "existing record drift")
for item in approved:
    if item["id"] not in old_ids:
        catalog["glyphs"].append({field: item[field] for field in fields})
assert len(catalog["glyphs"]) == 31
catalog_path.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

destination = ROOT / "Assets/Game/Art/AnimalGlyphsV04"
destination.mkdir(parents=True, exist_ok=True)
texture_meta = (ROOT / "Assets/Game/Resources/HudIcons/camera.png.meta").read_text(encoding="utf-8")
checks = []
for index, item in enumerate(approved, 1):
    stem = f'{index:02}_{item["name"]}'
    for suffix, source_folder in ((".png", "单字PNG"), (".svg", "单字SVG")):
        source = SOURCE / source_folder / (stem + suffix)
        target = destination / (item["id"] + suffix)
        shutil.copyfile(source, target)
        if suffix == ".png":
            data = source.read_bytes()
            width, height = struct.unpack(">II", data[16:24])
            assert (width, height, data[25]) == (500, 600, 6), (source.name, "expected RGBA PNG")
        meta = Path(str(target) + ".meta")
        if not meta.exists():
            guid = uuid.uuid4().hex
            if suffix == ".png":
                import re
                body = re.sub(r"guid: [0-9a-f]+", "guid: " + guid, texture_meta, count=1)
                body = body.replace("maxTextureSize: 512", "maxTextureSize: 1024")
            else:
                body = f"fileFormatVersion: 2\nguid: {guid}\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
            meta.write_text(body, encoding="utf-8")
        checks.append({"id": item["id"], "file": str(target.relative_to(ROOT)), "sha256": hashlib.sha256(target.read_bytes()).hexdigest()})
report = ROOT / "LifeGameplayReview/20261026/photo-flow/glyph-import.json"
report.parent.mkdir(parents=True, exist_ok=True)
report.write_text(json.dumps({"count": 31, "originalRecordsPreserved": old_ids[:24], "assets": checks}, ensure_ascii=False, indent=2), encoding="utf-8")
print("PASS 31 stable IDs; all existing records exact; 31 RGBA PNGs at 500x600 and 31 SVGs copied verbatim.")
