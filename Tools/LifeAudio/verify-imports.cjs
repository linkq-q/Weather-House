const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const voice = require('./VoiceDesigner/voice-engine.js');
const root = path.resolve(__dirname, '../..');
const imported = path.join(root, 'Assets/Game/Life/Audio/Resources/LifeAudio');
const digest = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, 'Source/derived_manifest.json')));
const clips = [];
for (const item of manifest.clips) {
  const source = fs.readFileSync(path.join(__dirname, 'Source', item.relative_file));
  const copy = fs.readFileSync(path.join(imported, 'Clips', `${item.clip_id}.wav`));
  assert.equal(digest(copy), digest(source), `${item.clip_id}: final import differs from approved derived file`);
  clips.push({ id: item.clip_id, sha256: digest(copy) });
}
for (const id of Object.keys(voice.presets)) {
  const bytes = fs.readFileSync(path.join(imported, 'Clips', `animal_syllable_${id}.wav`));
  assert.equal(digest(bytes), digest(Buffer.from(voice.encodeWav(voice.render(id).samples))), `${id}: stale voice export`);
  clips.push({ id: `animal_syllable_${id}`, sha256: digest(bytes) });
}
assert.equal(clips.length, 26);
const catalog = fs.readFileSync(path.join(imported, 'Catalog.asset'), 'utf8');
for (const clip of clips) {
  const meta = fs.readFileSync(path.join(imported, 'Clips', `${clip.id}.wav.meta`), 'utf8');
  const guid = meta.match(/^guid: ([a-f0-9]+)/m)[1];
  assert(catalog.includes(`fileID: 8300000, guid: ${guid}`), `${clip.id}: catalog binding missing`);
}
const mapping = JSON.parse(fs.readFileSync(path.join(__dirname, 'surface-bindings.json')));
const actual = fs.readFileSync(path.join(__dirname, 'surface-audit.tsv'), 'utf8');
for (const [asset] of [...mapping.terrain, ...mapping.materials]) {
  assert(actual.includes(asset), `${asset}: not evidenced in live scene inventory`);
  const objectType = asset.endsWith('.mat') ? 'Material' : 'TerrainLayer';
  const source = fs.readFileSync(path.join(root, asset), 'utf8');
  const objects = [...source.matchAll(/^--- !u!\d+ &(-?\d+)\r?\n(\w+):/gm)].filter(match => match[2] === objectType);
  assert.equal(objects.length, 1, `${asset}: expected exactly one ${objectType} object`);
  const fileId = objects[0][1];
  const guid = fs.readFileSync(path.join(root, `${asset}.meta`), 'utf8').match(/^guid: ([a-f0-9]+)/m)[1];
  assert(catalog.includes(`fileID: ${fileId}, guid: ${guid}`), `${asset}: wrong serialized object binding`);
}
const retuned = [];
for (const [id, cue] of [['paper_move', 2], ['camera_shutter', 4]]) {
  const bytes = fs.readFileSync(path.join(imported, 'Clips', `${id}.wav`));
  const gain = Number(catalog.match(new RegExp(`  - cue: ${cue}\\r?\\n[\\s\\S]*?    volume: ([\\d.]+)`))[1]);
  let start, size, rate, channels, bits;
  for (let offset = 12; offset < bytes.length;) {
    const type = bytes.toString('ascii', offset, offset + 4), length = bytes.readUInt32LE(offset + 4);
    if (type === 'fmt ') { channels = bytes.readUInt16LE(offset + 10); rate = bytes.readUInt32LE(offset + 12); bits = bytes.readUInt16LE(offset + 22); }
    if (type === 'data') { start = offset + 8; size = length; break; }
    offset += 8 + length + (length & 1);
  }
  assert.equal(bits, 16); assert.equal(channels, 1); assert.equal(rate, 48000);
  let squared = 0, peak = 0, first = -1;
  for (let offset = start; offset < start + size; offset += 2) {
    const sample = bytes.readInt16LE(offset) / 32768, effective = sample * gain;
    squared += effective * effective; peak = Math.max(peak, Math.abs(sample));
    if (first < 0 && Math.abs(effective) >= .01) first = (offset - start) / 2;
  }
  const effectiveRmsDb = 20 * Math.log10(Math.sqrt(squared / (size / 2))), onsetSeconds = first / rate;
  assert(effectiveRmsDb >= -40 && first >= 0 && onsetSeconds < .05 && peak < .99, `${id}: weak, delayed or clipped output`);
  retuned.push({ id, gain, effectiveRmsDb, onsetSeconds, peakDb: 20 * Math.log10(peak), seconds: size / 2 / rate });
}
const report = { result: 'PASS', scope: 'Offline bytes and typed YAML object references; Unity object resolution requires LifeAudioChecks.Run; source/listener output requires LifeAudioAudibilityChecks', imported: clips.length, terrainMappings: mapping.terrain.length, materialMappings: mapping.materials.length, retuned, clips };
fs.writeFileSync(path.join(__dirname, 'import-verification.json'), JSON.stringify(report, null, 2));
console.log(`PASS ${clips.length} exact PCM imports; 4 typed terrain-layer and 8 typed stone-material references. Unity object resolution remains a separate check.`);
