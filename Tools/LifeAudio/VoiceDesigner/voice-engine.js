(function (root) {
  'use strict';
  const sampleRate = 44100;
  const presets = {
    dog: { name: '狗', character: '圆润、短促，句尾轻轻下落', frequency: 195, duration: 0.16, formant: 0.88, breath: 0.018, wobble: 0.06, vowels: [0, 1, 2, 0, 3, 1], notes: [0, 2, -1, 1, 3, -3], rhythm: [1, 0.8, 1.2, 0.8, 1, 1.25], defaults: { pitch: 5, speed: 2, timbre: 70, variation: 45 } },
    squirrel: { name: '松鼠', character: '轻巧、跳跃，小音节紧凑相接', frequency: 335, duration: 0.115, formant: 1.12, breath: 0.012, wobble: 0.09, vowels: [4, 3, 1, 4, 2, 3], notes: [1, 4, -2, 3, 0, 5], rhythm: [0.8, 0.85, 1.25, 0.8, 0.9, 1.1], defaults: { pitch: 4, speed: 2.3, timbre: 100, variation: 60 } },
    fox: { name: '狐', character: '清软、略带鼻音，抑扬有留白', frequency: 260, duration: 0.175, formant: 1.04, breath: 0.028, wobble: 0.08, vowels: [1, 3, 0, 4, 3, 0], notes: [-2, 3, 1, -1, 4, -2], rhythm: [1.1, 0.85, 1.2, 0.75, 1, 1.3], defaults: { pitch: 4, speed: 1.9, timbre: 100, variation: 55 } },
    sheep: { name: '羊', character: '蓬松、轻快，尾音带柔和颤动', frequency: 225, duration: 0.205, formant: 0.94, breath: 0.04, wobble: 0.32, vowels: [2, 0, 3, 0, 2, 1], notes: [0, 1, -2, 2, 0, -2], rhythm: [1.2, 0.8, 1.25, 0.9, 1.1, 1.4], defaults: { pitch: 5, speed: 1.7, timbre: 65, variation: 40 } },
    deer: { name: '鹿', character: '温和、轻亮，呼吸感轻', frequency: 175, duration: 0.195, formant: 0.84, breath: 0.032, wobble: 0.04, vowels: [1, 0, 2, 1, 3, 0], notes: [-1, 1, 2, 0, 1, -3], rhythm: [1.1, 0.9, 1.05, 1.2, 0.85, 1.35], defaults: { pitch: 6, speed: 1.8, timbre: 100, variation: 30 } }
  };
  const vowels = [[450, 900, 2400], [340, 820, 2200], [730, 1180, 2700], [500, 1780, 2600], [300, 2100, 2900]];

  function bandpass(frequency, bandwidth) {
    const angle = 2 * Math.PI * frequency / sampleRate;
    const alpha = Math.sin(angle) / (2 * frequency / bandwidth);
    const b = alpha / (1 + alpha), a1 = -2 * Math.cos(angle) / (1 + alpha), a2 = (1 - alpha) / (1 + alpha);
    let x1 = 0, x2 = 0, y1 = 0, y2 = 0;
    return value => {
      const output = b * (value - x2) - a1 * y1 - a2 * y2;
      x2 = x1; x1 = value; y2 = y1; y1 = output;
      return output;
    };
  }

  function render(id, settings = {}) {
    const preset = presets[id];
    if (!preset) throw new Error('请选择已有动物预设。');
    const params = { ...preset.defaults, ...settings };
    for (const [key, min, max] of [['pitch', -12, 12], ['speed', 0.65, 3], ['timbre', 0, 100], ['variation', 0, 100]]) {
      if (!Number.isFinite(params[key]) || params[key] < min || params[key] > max) throw new Error(`参数 ${key} 超出范围。`);
    }
    const brightness = params.timbre / 100, expression = params.variation / 100;
    const baseFrequency = preset.frequency * 2 ** (params.pitch / 12);
    const durations = preset.rhythm.map(value => preset.duration * value / params.speed);
    const gaps = [0.045, 0.055, 0.16, 0.04, 0.085, 0.14].map(value => value / params.speed);
    const total = 0.07 + durations.reduce((sum, duration, i) => sum + duration + gaps[i], 0);
    const samples = new Float32Array(Math.ceil(total * sampleRate));
    let seed = 7261 + Object.keys(presets).indexOf(id) * 997;
    const noise = () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed / 2147483648 - 1; };
    const table = new Float32Array(2049);
    // Limit harmonics before synthesis so raising the voice does not fold high tones into the audible band.
    const harmonics = Math.min(24, Math.floor(18000 / (baseFrequency * 2 ** (8 / 12))));
    for (let i = 0; i < 2048; i++) {
      for (let h = 1; h <= harmonics; h++) table[i] += Math.sin(2 * Math.PI * i / 2048 * h) / h ** (1.65 - brightness * 0.35);
    }
    table[2048] = table[0];
    let cursor = Math.round(0.035 * sampleRate);
    for (let syllable = 0; syllable < durations.length; syllable++) {
      const duration = durations[syllable], length = Math.round(duration * sampleRate);
      const formants = vowels[preset.vowels[syllable]].map(value => value * preset.formant * (0.88 + brightness * 0.25));
      const filters = formants.map((frequency, i) => bandpass(frequency, [110, 170, 240][i]));
      const nasal = bandpass(950, 160);
      let phase = 0, softened = 0;
      const lowpass = 1 - Math.exp(-2 * Math.PI * (2300 + brightness * 2100) / sampleRate);
      for (let i = 0; i < length; i++) {
        const t = i / sampleRate, progress = i / (length - 1);
        const contour = expression * (preset.notes[syllable] + 2.5 * Math.sin(Math.PI * progress) - 2 * progress);
        const vibrato = preset.wobble * expression * Math.sin(2 * Math.PI * 6.2 * t);
        phase = (phase + baseFrequency * 2 ** ((contour + vibrato) / 12) / sampleRate) % 1;
        const position = phase * 2048, index = Math.floor(position);
        const source = table[index] + (table[index + 1] - table[index]) * (position - index);
        const air = noise() * preset.breath * (0.5 + Math.exp(-t * 45));
        const voiced = source + air;
        const vowel = filters[0](voiced) * 2.8 + filters[1](voiced) * 1.65 + filters[2](voiced) * 0.65;
        const sound = vowel + source * 0.13 + air * 0.22 + nasal(voiced) * (id === 'fox' ? 0.35 : 0.05);
        softened += lowpass * (sound - softened);
        const attack = Math.min(1, t / 0.018), release = Math.min(1, (duration - t) / 0.038);
        const envelope = Math.sin(Math.PI * 0.5 * attack) ** 2 * Math.sin(Math.PI * 0.5 * release) ** 2 * (0.9 + 0.1 * Math.sin(Math.PI * progress));
        samples[cursor + i] = softened * envelope;
      }
      cursor += length + Math.round(gaps[syllable] * sampleRate);
    }
    // Remove the tiny filter bias, fade both ends, and leave export headroom.
    let previous = 0, filtered = 0, peak = 0;
    for (let i = 0; i < samples.length; i++) {
      filtered = samples[i] - previous + 0.995 * filtered;
      previous = samples[i];
      const edge = Math.min(1, i / 128, (samples.length - 1 - i) / 128);
      samples[i] = filtered * edge;
      peak = Math.max(peak, Math.abs(samples[i]));
    }
    if (!Number.isFinite(peak) || peak === 0) throw new Error('音频生成失败，请重置预设后重试。');
    const gain = 0.78 / peak;
    for (let i = 0; i < samples.length; i++) samples[i] *= gain;
    samples[0] = 0; samples[samples.length - 1] = 0;
    return { samples, sampleRate, params, id };
  }

  function encodeWav(samples) {
    const buffer = new ArrayBuffer(44 + samples.length * 2), view = new DataView(buffer);
    const text = (offset, value) => { for (let i = 0; i < value.length; i++) view.setUint8(offset + i, value.charCodeAt(i)); };
    text(0, 'RIFF'); view.setUint32(4, buffer.byteLength - 8, true); text(8, 'WAVE');
    text(12, 'fmt '); view.setUint32(16, 16, true); view.setUint16(20, 1, true); view.setUint16(22, 1, true);
    view.setUint32(24, sampleRate, true); view.setUint32(28, sampleRate * 2, true); view.setUint16(32, 2, true); view.setUint16(34, 16, true);
    text(36, 'data'); view.setUint32(40, samples.length * 2, true);
    for (let i = 0; i < samples.length; i++) {
      if (!Number.isFinite(samples[i]) || Math.abs(samples[i]) > 1) throw new Error('音频包含无效或削波样本，未导出。');
      view.setInt16(44 + i * 2, Math.round(samples[i] * (samples[i] < 0 ? 32768 : 32767)), true);
    }
    return buffer;
  }
  const api = { presets, sampleRate, render, encodeWav };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.VoiceDesigner = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
