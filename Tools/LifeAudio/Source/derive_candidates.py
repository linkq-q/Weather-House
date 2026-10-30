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
 ("step_grass_a", "grass_steps_fission9_public_hq.mp3", .095, .385, -12, .003, .035, False, "A13 recorded grass contact a"),
 ("step_grass_b", "grass_steps_fission9_public_hq.mp3", .795, 1.005, -12, .003, .035, False, "A13 recorded grass contact b"),
 ("step_dirt_a", "dirt_walk_aglinder_public_hq.mp3", 9.12, 9.40, -12, .004, .025, False, "A13 ordinary dirt walking variant a"),
 ("step_dirt_b", "dirt_walk_aglinder_public_hq.mp3", 9.64, 9.91, -12, .004, .025, False, "A13 ordinary dirt walking variant b"),
 ("step_stone_a", "StoneOriginal/Fantozzi-footsteps/ogg/Fantozzi-StoneL1.ogg", 0.0, None, -12, .003, .025, False, "A13 stone left-step variant a"),
 ("step_stone_b", "StoneOriginal/Fantozzi-footsteps/ogg/Fantozzi-StoneR1.ogg", 0.0, None, -12, .003, .025, False, "A13 stone right-step variant b"),
 ("ui_soft_tick", "ui_tick_001.ogg", 0.0, None, -18, .001, .004, False, "A01 shared interface feedback"),
 ("progress_soft", "progress_confirmation_001.ogg", 0.0, None, -16, .004, .035, False, "A02 shared progress confirmation"),
 ("paper_move", "paper_bookFlip1.ogg", .275, .575, -10, .004, .035, False, "A03 book and actual page motion"),
 ("stroke_soft_loop", "stroke_pencil_write.ogg", .10, 1.30, -22, .08, 0, True, "A04 moving pen loop"),
 ("camera_shutter", "camera_photo.ogg", .112, .32, -8, .004, .035, False, "A05 camera capture start"),
 ("grass_fiber_loop", "grass_rustle_clusman_public_hq.mp3", 2.10, 3.30, -20, .10, 0, True, "A06 grass fiber harvest and weaving"),
 ("soft_contact", "soft_contact_000.ogg", 0.0, None, -16, .002, .018, False, "A07 shared soft material contact"),
 ("water_touch_a", "water_splash_09.ogg", 0.0, None, -16, .005, .035, False, "A08 water contact variant a"),
 ("water_touch_b", "water_splash_10.ogg", 0.0, None, -16, .005, .035, False, "A08 water contact variant b"),
 ("water_lap", "liquid_lap_cat_public_hq.mp3", 5.10, 5.62, -19, .015, .045, False, "A09 mouth-liquid contact; source cat drinking milk, no vocal call"),
 ("weather_release", "wind_woosh_loop.ogg", 2.85, 3.30, -17, .045, .20, False, "A10 accepted weather gesture short air cue"),
 ("rain_loop", "rain_1_loop_candidate.ogg", 0.0, 27.0, -20, .15, 0, True, "A11 local rain loop"),
 ("wind_loop", "wind_woosh_loop.ogg", 0.0, None, -22, .15, 0, True, "A12 local wind loop"),
 ("outdoor_quiet_loop", "outdoor_cricket_augmentality.ogg", 0.0, None, -28, .25, 0, True, "B03 quiet outdoor insect ambience"),
 ("shore_soft_loop", "shore_jasinski_alkaibeach.flac", 0.0, None, -12, .6, 0, True, "B04 ocean surf from jasinski Alki Beach CC0"),
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
manifest = dict(date="2026-10-26", status="21 PCM exports ready for runtime integration and in-game listening",
    source_files_preserved=True, lossless_master_claim=False,
    note="WAV exports are derived from lossy OGG/MP3 sources; WAV does not restore lost source quality. Human listening pending.",
    decoder=str(FFMPEG), decoder_version="ffmpeg 3.0.1 (existing installed executable; no new software installed)",
    clip_count=len(records), total_bytes=sum(r["bytes"] for r in records), clips=records)
if previous:
    # Preserve source attribution and untouched entries when retuning individual effects.
    updates = {record["clip_id"]: record for record in records}
    manifest = previous
    manifest["clips"] = [{**record, **updates.get(record["clip_id"], {})} for record in previous["clips"]]
    manifest["clip_count"] = len(manifest["clips"])
    manifest["total_bytes"] = sum(record["bytes"] for record in manifest["clips"])
manifest_path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")

