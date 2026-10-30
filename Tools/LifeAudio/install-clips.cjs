// Rebuild only the owned LifeAudio import directory and catalog; source recordings remain untouched.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const voice = require('./VoiceDesigner/voice-engine.js');
const root = path.resolve(__dirname, '../..');
const audio = path.join(root, 'Assets/Game/Life/Audio');
const output = path.join(audio, 'Resources/LifeAudio');
const guid = name => crypto.createHash('md5').update(`WeatherHouse.LifeAudio/${name}`).digest('hex');
const write = (target, text) => { fs.mkdirSync(path.dirname(target), { recursive: true }); fs.writeFileSync(target, text); };
function meta(target, body = '') {
  const filename = `${target}.meta`;
  if (!fs.existsSync(filename)) write(filename, `fileFormatVersion: 2\nguid: ${guid(path.relative(root, target).replaceAll('\\', '/'))}\n${body}`);
  return fs.readFileSync(filename, 'utf8').match(/^guid: ([a-f0-9]+)/m)[1];
}
for (const file of ['LifeAudio.cs', 'LifeAudioCatalog.cs', 'LifeAudioGround.cs']) meta(path.join(audio, file));
const scriptGuid = meta(path.join(audio, 'LifeAudioCatalog.cs'));
const sounds = [
  ['ui_soft_tick', .25, .12], ['progress_soft', .36, .5], ['paper_move', .65, .2], ['stroke_soft_loop', .18, 0],
  ['camera_shutter', .7, .25], ['grass_fiber_loop', .38, .2, .26], ['soft_contact', .45, .2],
  [['water_touch_a', 'water_touch_b'], .38, .2], ['water_lap', .45, .25], ['weather_release', .38, .2],
  ['rain_loop', .2, 0], ['wind_loop', .17, 0], ['step_grass_a', .34, .18],
  ['step_dirt_a', .35, .18], ['step_stone_a', .29, .18],
  ['outdoor_quiet_loop', .13, 0], ['shore_soft_loop', .4, 0],
  [['magic_cast_a_loop', 'magic_cast_b_loop'], .3, 0], ['magic_failed', .38, .25]
];
const clipGuids = new Map();
function importClip(name, bytes, loop) {
  const target = path.join(output, 'Clips', `${name}.wav`); write(target, bytes);
  clipGuids.set(name, meta(target, `AudioImporter:\n  externalObjects: {}\n  serializedVersion: 7\n  defaultSettings:\n    serializedVersion: 2\n    loadType: ${loop ? 1 : 0}\n    sampleRateSetting: 0\n    sampleRateOverride: 44100\n    compressionFormat: 1\n    quality: 0.8\n    conversionMode: 0\n    preloadAudioData: 1\n  platformSettingOverrides: {}\n  forceToMono: 1\n  normalize: 0\n  loadInBackground: 0\n  ambisonic: 0\n  3D: 1\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n`));
}
for (const [names] of sounds) for (const name of [names].flat()) {
  const batch = name === 'outdoor_quiet_loop' || name === 'shore_soft_loop' ? 'P1' : 'P0';
  importClip(name, fs.readFileSync(path.join(__dirname, 'Source/Derived', batch, `${name}.wav`)), name.endsWith('_loop'));
}
for (const id of Object.keys(voice.presets)) importClip(`animal_syllable_${id}`, Buffer.from(voice.encodeWav(voice.render(id).samples)), false);
let yaml = `%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: ${scriptGuid}, type: 3}\n  m_Name: Catalog\n  m_EditorClassIdentifier:\n  sounds:\n`;
sounds.forEach(([names, volume, cooldown, shortDuration = 0], cue) => {
  yaml += `  - cue: ${cue}\n    clips:\n`;
  for (const name of [names].flat()) yaml += `    - {fileID: 8300000, guid: ${clipGuids.get(name)}, type: 3}\n`;
  yaml += `    volume: ${volume}\n    cooldown: ${cooldown}\n    shortDuration: ${shortDuration}\n`;
});
yaml += '  voices:\n';
for (const id of Object.keys(voice.presets)) yaml += `  - speciesId: ${id}\n    clip: {fileID: 8300000, guid: ${clipGuids.get(`animal_syllable_${id}`)}, type: 3}\n`;
const mappingPath = path.join(__dirname, 'surface-bindings.json');
const mappings = fs.existsSync(mappingPath) ? JSON.parse(fs.readFileSync(mappingPath, 'utf8')) : { terrain: [], materials: [] };
for (const [property, kind, field] of [['terrainSurfaces', 'terrain', 'layer'], ['materialSurfaces', 'materials', 'material']]) {
  yaml += `  ${property}:${mappings[kind].length ? '' : ' []'}\n`;
  for (const [asset, surface] of mappings[kind]) {
    const assetGuid = fs.readFileSync(path.join(root, `${asset}.meta`), 'utf8').match(/^guid: ([a-f0-9]+)/m)[1];
    const objectType = kind === 'materials' ? 'Material' : 'TerrainLayer';
    const objects = [...fs.readFileSync(path.join(root, asset), 'utf8').matchAll(/^--- !u!\d+ &(-?\d+)\r?\n(\w+):/gm)].filter(match => match[2] === objectType);
    if (objects.length !== 1) throw new Error(`Expected exactly one ${objectType} object: ${asset}`);
    const fileId = objects[0][1];
    yaml += `  - ${field}: {fileID: ${fileId}, guid: ${assetGuid}, type: 2}\n    surface: ${{ grass: 1, dirt: 2, stone: 3 }[surface]}\n`;
  }
}
write(path.join(output, 'Catalog.asset'), yaml); meta(path.join(output, 'Catalog.asset'));
console.log(`Imported ${clipGuids.size} WAVs and catalog. Terrain mappings: ${mappings.terrain.length}; material mappings: ${mappings.materials.length}.`);
