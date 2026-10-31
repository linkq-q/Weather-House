const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const voice = require('./voice-engine.js');

assert.deepEqual(Object.keys(voice.presets), ['dog', 'squirrel', 'fox', 'sheep', 'deer']);
for (const id of ['squirrel', 'fox', 'deer']) assert.equal(voice.presets[id].defaults.timbre, 100, `${id}: approved brightness`);
assert.equal(voice.presets.dog.defaults.timbre, 70);
assert.equal(voice.presets.sheep.defaults.timbre, 65);
const outputDir = process.argv[2];
if (outputDir) fs.mkdirSync(outputDir, { recursive: true });
const report = [];
const hashes = new Set();
for (const [id, pitch, speed] of [['dog',7,2],['squirrel',0,1.15],['fox',6,1.9],['sheep',7,1.7],['deer',8,1.8]]) {
  assert.equal(voice.presets[id].defaults.pitch, pitch, `${id}: user-selected pitch`);
  assert.equal(voice.presets[id].defaults.speed, speed, `${id}: user-selected speed`);
}
for (const [id, preset] of Object.entries(voice.presets)) {
  const result = voice.render(id, preset.defaults);
  const wav = Buffer.from(voice.encodeWav(result.samples));
  let peak = 0, squareSum = 0, sum = 0;
  for (const value of result.samples) {
    assert(Number.isFinite(value), `${id}: non-finite sample`);
    assert(Math.abs(value) < 1, `${id}: clipped sample`);
    peak = Math.max(peak, Math.abs(value));
    squareSum += value * value;
    sum += value;
  }
  const rms = Math.sqrt(squareSum / result.samples.length);
  assert(peak > 0.6 && peak < 0.81, `${id}: invalid peak ${peak}`);
  assert(rms > 0.04 && rms < 0.4, `${id}: invalid loudness ${rms}`);
  assert(Math.abs(sum / result.samples.length) < 0.002, `${id}: DC offset`);
  assert.equal(result.samples[0], 0);
  assert.equal(result.samples.at(-1), 0);
  assert(result.samples.length / voice.sampleRate > 0.5);
  assert(result.samples.length / voice.sampleRate < 4);
  assert.equal(wav.toString('ascii', 0, 4), 'RIFF');
  assert.equal(wav.readUInt32LE(4), wav.length - 8);
  assert.equal(wav.toString('ascii', 8, 12), 'WAVE');
  assert.equal(wav.readUInt16LE(20), 1, 'PCM format');
  assert.equal(wav.readUInt16LE(22), 1, 'mono');
  assert.equal(wav.readUInt32LE(24), 44100);
  assert.equal(wav.readUInt16LE(34), 16);
  assert.equal(wav.readUInt32LE(40), result.samples.length * 2);
  for (let offset = 44; offset < wav.length; offset += 2) {
    assert(Math.abs(wav.readInt16LE(offset)) < 32767, 'PCM is clipped');
  }
  const hash = crypto.createHash('sha256').update(wav).digest('hex');
  hashes.add(hash);
  assert.deepEqual(voice.render(id, preset.defaults).samples, result.samples, 'repeatable preview');
  const fast = voice.render(id, { ...preset.defaults, speed: 1.5 });
  const slow = voice.render(id, { ...preset.defaults, speed: 0.65 });
  assert(fast.samples.length < slow.samples.length, 'speed changes timing');
  for (const settings of [
    { pitch: -12, speed: 0.65, timbre: 0, variation: 0 },
    { pitch: 12, speed: 3, timbre: 100, variation: 100 }
  ]) {
    const edge = voice.render(id, settings).samples;
    for (const value of edge) assert(Number.isFinite(value) && Math.abs(value) < 1);
    voice.encodeWav(edge);
  }
  report.push({ id, animal: preset.name, seconds: +(result.samples.length / voice.sampleRate).toFixed(3), peak: +peak.toFixed(4), rms: +rms.toFixed(4), pcm: '44100 Hz / mono / 16-bit PCM', sha256: hash });
  if (outputDir) fs.writeFileSync(path.join(outputDir, `${preset.name}_原创短音节.wav`), wav);
}
assert.equal(hashes.size, 5, 'five distinct presets');
const base = voice.render('dog', voice.presets.dog.defaults).samples;
for (const [key, value] of Object.entries({ pitch: 9, speed: 1.4, timbre: 95, variation: 95 })) {
  assert.notDeepEqual(voice.render('dog', { ...voice.presets.dog.defaults, [key]: value }).samples, base, `${key} must affect audio`);
}
assert.throws(() => voice.render('unknown', {}));
assert.throws(() => voice.render('dog', { ...voice.presets.dog.defaults, pitch: NaN }));
assert.throws(() => voice.encodeWav(new Float32Array([NaN])));
assert.throws(() => voice.encodeWav(new Float32Array([1.1])));
if (outputDir) fs.writeFileSync(path.join(outputDir, '音频数据验证.json'), JSON.stringify({ result: 'PASS', presets: report }, null, 2));
console.log(JSON.stringify({ result: 'PASS', presets: report }, null, 2));
