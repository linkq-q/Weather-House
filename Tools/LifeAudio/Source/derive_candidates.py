"""Small, offline audio export for Weather House. Source files remain unchanged."""
from pathlib import Path
import hashlib, json, subprocess, sys, wave
import numpy as np

ROOT = Path(__file__).resolve().parent
FFMPEG = Path(r"C:\Users\31483\AppData\Roaming\bilibili\ffmpeg\ffmpeg.exe")
RATE = 48000
OUT = ROOT / "Derived" / "P0"
OUT.mkdir(parents=True, exist_ok=True)
cache = {}

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def decode(name):
    if name not in cache:
        p = ROOT / "Selected" / name
        result = subprocess.run([str(FFMPEG), "-hide_banner", "-loglevel", "error",
            "-i", str(p), "-vn", "-ac", "1", "-ar", str(RATE),
            "-f", "f32le", "pipe:1"], capture_output=True, check=True)
        x = np.frombuffer(result.stdout, dtype="<f4").astype(np.float64)
        if not len(x) or not np.isfinite(x).all():
            raise ValueError("Empty or invalid decoded audio: " + name)
        cache[name] = x
    return cache[name]

# ID, source, start/end seconds, peak dBFS, fade in/out or loop overlap, use.
SPECS = [
 ("step_grass_a", "Approved20261030/step_grass_a.wav", 0.000000, None, -7, 0.003, 0.025, False, "A13 recorded grass contact a"),
 ("step_dirt_a", "Approved20261030/step_dirt_a.wav", 0.000000, None, -12, 0.003, 0.025, False, "A13 ordinary dirt walking variant a"),
 ("step_stone_a", "Approved20261030/step_stone_a.wav", 0.000000, None, -8, 0.003, 0.025, False, "A13 selected short stone step"),
 ("ui_soft_tick", "ui_tick_001.ogg", 0.0, None, -18, .001, .004, False, "A01 shared interface feedback"),
 ("progress_soft", "progress_confirmation_001.ogg", 0.0, None, -16, .004, .035, False, "A02 shared progress confirmation"),
 ("paper_move", "Approved20261030/paper_move.wav", 0.079208, None, -6, 0.003, 0.035, False, "A03 book and actual page motion"),
 ("stroke_soft_loop", "stroke_pencil_write.ogg", .10, 1.30, -22, .08, 0, True, "A04 moving pen loop"),
 ("camera_shutter", "Approved20261030/camera_shutter.wav", 0.107229, None, -6, 0.003, 0.035, False, "A05 camera capture start"),
 ("grass_fiber_loop", "Approved20261030/grass_fiber_loop.wav", 0.000000, None, -20, 0.1, 0, True, "A06 grass fiber harvest and weaving"),
 ("soft_contact", "Approved20261030/soft_contact.wav", 0.000000, None, -12, 0.002, 0.018, False, "A07 shared soft material contact"),
 ("water_touch_a", "Approved20261030/water_touch_a.wav", 0.000000, None, -10, 0.003, 0.035, False, "A08 water contact variant a"),
 ("water_touch_b", "Approved20261030/water_touch_b.wav", 0.000000, None, -10, 0.003, 0.035, False, "A08 water contact variant b"),
 ("water_lap", "Approved20261030/water_lap.wav", 0.000000, None, -14, 0.005, 0.035, False, "A09 mouth-liquid contact; source cat drinking milk, no vocal call"),
 ("weather_release", "ApprovedMagic20261030/07.wav", 0, None, -8, .005, .06, False, "A10 selected 07 casting success"),
 ("magic_cast_a_loop", "ApprovedMagic20261030/03.wav", 0, None, -10, .12, 0, True, "A10 selected 03 moving world gesture"),
 ("magic_cast_b_loop", "ApprovedMagic20261030/04.wav", 0, None, -10, .12, 0, True, "A10 selected 04 moving world gesture alternate"),
 ("magic_failed", "ApprovedMagic20261030/06.wav", 0, None, -10, .005, .06, False, "A10 selected 06 casting failure"),
 ("rain_loop", "Approved20261030/rain_loop.wav", 0.000000, None, -22, 0.15, 0, True, "A11 local rain loop"),
 ("wind_loop", "Approved20261030/wind_loop.wav", 0.000000, None, -18, 0.4, 0, True, "A12 local wind loop"),
 ("outdoor_quiet_loop", "outdoor_cricket_augmentality.ogg", 0.0, None, -28, .25, 0, True, "B03 quiet outdoor insect ambience"),
 ("shore_soft_loop", "Approved20261030/shore_soft_loop.wav", 0.000000, None, -6, 0.25, 0, True, "B04 selected shore wave by transitking CC0"),
]
selected = set(sys.argv[1:])
if selected - {spec[0] for spec in SPECS}:
    raise ValueError("Unknown clip IDs: " + ", ".join(sorted(selected - {spec[0] for spec in SPECS})))
manifest_path = ROOT/"derived_manifest.json"
previous = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() else None
if selected and previous is None:
    raise ValueError("Partial export requires the existing manifest")
records = []
for clip_id, name, begin, end, peak_db, fade_in, fade_out, loop, use in SPECS:
    if selected and clip_id not in selected:
        continue
    original = decode(name)
    finish = min(len(original), round(end*RATE)) if end is not None else len(original)
    x = original[round(begin*RATE):finish].copy()
    assert len(x) > 20, clip_id
    x -= np.mean(x)
    processing = ["decode source; mono downmix; resample 48000 Hz", "remove segment DC"]
    if loop:
        n = round(fade_in*RATE)
        assert len(x) > n*3
        alpha = np.linspace(0, 1, n, endpoint=False)
        x = np.concatenate([x[n:-n], x[-n:]*(1-alpha)+x[:n]*alpha])
        processing.append(f"cyclic crossfade {fade_in:.3f}s, shorten by overlap")
    else:
        n_in, n_out = round(fade_in*RATE), round(fade_out*RATE)
        x[:n_in] *= np.linspace(0, 1, n_in)
        x[-n_out:] *= np.linspace(1, 0, n_out)
        processing.append(f"fade in {fade_in:.3f}s; fade out {fade_out:.3f}s")
    raw_peak = float(np.max(np.abs(x)))
    assert raw_peak > 1e-7
    gain = 10**(peak_db/20)/raw_peak
    x *= gain
    pcm = np.round(x*32767).astype("<i2")
    destination = OUT.parent/"P1" if use.startswith("B") else OUT
    destination.mkdir(parents=True, exist_ok=True)
    path = destination/(clip_id+".wav")
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE); w.writeframes(pcm.tobytes())
    assert np.max(np.abs(pcm.astype(np.int32))) < 32767
    assert loop or (pcm[0] == 0 and pcm[-1] == 0)
    rec = dict(clip_id=clip_id, purpose=use, file=str(path), relative_file=path.relative_to(ROOT).as_posix(),
        source_file=str(ROOT/"Selected"/name), source_sha256=sha(ROOT/"Selected"/name),
        source_start_seconds=begin, source_end_seconds=round(finish/RATE,6),
        source_is_lossy=Path(name).suffix.lower() in (".ogg", ".mp3"), license="CC0-1.0", processing=processing+[f"peak target {peak_db} dBFS"],
        processing_gain=round(gain,7), output_format="PCM signed 16-bit WAV; mono; 48000 Hz",
        duration_seconds=round(len(pcm)/RATE,6), loop=loop, peak_dbfs=round(20*np.log10(np.max(np.abs(pcm.astype(np.float64)))/32767),3),
        rms_dbfs=round(20*np.log10(np.sqrt(np.mean((pcm.astype(np.float64)/32767)**2))),3),
        seam_sample_jump=int(abs(int(pcm[-1])-int(pcm[0]))), clipped_samples=int(np.sum(np.abs(pcm.astype(np.int32))>=32767)),
        bytes=path.stat().st_size, sha256=sha(path), decoded_export_verified=True,
        human_listening_passed=False, unity_imported_by_this_task=False)
    records.append(rec)
    print(f"{clip_id}: {rec['duration_seconds']}s, {rec['bytes']} bytes")
manifest = dict(date="2026-10-26", status="PCM exports ready for runtime integration and in-game listening",
    source_files_preserved=True, lossless_master_claim=False,
    note="WAV exports are derived from lossy OGG/MP3 sources; WAV does not restore lost source quality. Human listening pending.",
    decoder=str(FFMPEG), decoder_version="ffmpeg 3.0.1 (existing installed executable; no new software installed)",
    clip_count=len(records), total_bytes=sum(r["bytes"] for r in records), clips=records)
if previous:
    # Preserve source attribution and untouched entries when retuning individual effects.
    updates = {record["clip_id"]: record for record in records}
    existing_ids = {record["clip_id"] for record in previous["clips"]}
    manifest = previous
    manifest["clips"] = [{**record, **updates.get(record["clip_id"], {})} for record in previous["clips"]]
    manifest["clips"].extend(record for record in records if record["clip_id"] not in existing_ids)
    manifest["clip_count"] = len(manifest["clips"])
    manifest["total_bytes"] = sum(record["bytes"] for record in manifest["clips"])
manifest_path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")

