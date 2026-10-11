// Strict .bbmodel format validator.
//
// Cross-checks a generated project against a real Blockbench 4.10 project
// (MC/MC.bbmodel) to confirm every cube / group / texture / animation field uses
// the same names, types and shapes that Blockbench itself writes.
//
// Usage: node validate-bbmodel.js <generated.bbmodel> [reference.bbmodel]
const fs = require('fs');
const path = require('path');

const target = process.argv[2] || path.resolve(__dirname, '..', 'player', 'Player.bbmodel');
const reference = process.argv[3] || path.resolve(__dirname, '..', 'MC', 'MC.bbmodel');

const problems = [];
const notes = [];
function fail(msg) { problems.push(msg); }
function ok(msg) { notes.push(msg); }

function typeOf(value) {
  if (Array.isArray(value)) return 'array';
  if (value === null) return 'null';
  return typeof value;
}

/** Compare an object's key set against a reference shape; extra keys are errors. */
function checkShape(label, obj, shape, allowedExtra = []) {
  for (const [key, expected] of Object.entries(shape)) {
    if (!(key in obj)) { fail(`${label}: missing field "${key}"`); continue; }
    const actual = typeOf(obj[key]);
    if (actual !== expected) fail(`${label}.${key}: expected ${expected}, found ${actual}`);
  }
  for (const key of Object.keys(obj)) {
    if (key in shape || allowedExtra.includes(key)) continue;
    fail(`${label}: unexpected field "${key}" (${typeOf(obj[key])})`);
  }
}

const gen = JSON.parse(fs.readFileSync(target, 'utf8'));
const refExists = fs.existsSync(reference);
const ref = refExists ? JSON.parse(fs.readFileSync(reference, 'utf8')) : null;

// ---------------- root ----------------
const META = { format_version: 'string', model_format: 'string', box_uv: 'boolean' };
if (!gen.meta) fail('root: missing "meta"');
else checkShape('meta', gen.meta, META);

for (const key of ['name', 'model_identifier', 'variable_placeholders']) {
  if (typeof gen[key] !== 'string') fail(`root.${key}: expected string, found ${typeOf(gen[key])}`);
}
if (!Array.isArray(gen.visible_box)) fail('root.visible_box: expected array');
if (!Array.isArray(gen.elements)) fail('root.elements: expected array');
if (!Array.isArray(gen.outliner)) fail('root.outliner: expected array');
if (!Array.isArray(gen.textures)) fail('root.textures: expected array');
if (!gen.resolution || typeof gen.resolution.width !== 'number' || typeof gen.resolution.height !== 'number') {
  fail('root.resolution: expected {width, height} numbers');
}

if (refExists) {
  // the metadata block must use the same key set as a real project
  const refMetaKeys = Object.keys(ref.meta).sort().join(',');
  const genMetaKeys = Object.keys(gen.meta).sort().join(',');
  if (genMetaKeys !== refMetaKeys) fail(`meta keys differ from reference: "${genMetaKeys}" vs "${refMetaKeys}"`);
  else ok(`meta keys match reference (${refMetaKeys})`);
}

// ---------------- elements ----------------
const CUBE_SHAPE = {
  name: 'string', box_uv: 'boolean', rescale: 'boolean', locked: 'boolean',
  light_emission: 'number', render_order: 'string', allow_mirror_modeling: 'boolean',
  from: 'array', to: 'array', autouv: 'number', color: 'number',
  origin: 'array', faces: 'object', type: 'string', uuid: 'string',
};
const CUBE_OPTIONAL = ['rotation', 'uv_offset', 'inflate', 'visibility', 'export', 'shade', 'mirror_uv', 'stretch'];

let refCubeFields = null;
if (refExists && ref.elements && ref.elements[0]) refCubeFields = Object.keys(ref.elements[0]);

gen.elements.forEach((el, i) => {
  const label = `elements[${i}](${el.name})`;
  if (el.type !== 'cube') { fail(`${label}: only cube types are expected, found "${el.type}"`); return; }
  checkShape(label, el, CUBE_SHAPE, CUBE_OPTIONAL);
  for (const key of ['from', 'to', 'origin']) {
    if (!Array.isArray(el[key]) || el[key].length !== 3 || el[key].some(v => typeof v !== 'number')) {
      fail(`${label}.${key}: expected [x, y, z] numbers`);
    }
  }
  const faces = Object.keys(el.faces).sort().join(',');
  if (faces !== 'down,east,north,south,up,west') fail(`${label}.faces: wrong face set "${faces}"`);
  for (const [face, data] of Object.entries(el.faces)) {
    if (!Array.isArray(data.uv) || data.uv.length !== 4 || data.uv.some(v => typeof v !== 'number')) {
      fail(`${label}.faces.${face}.uv: expected [x1, y1, x2, y2] numbers`);
      continue;
    }
    const [x1, y1, x2, y2] = data.uv;
    if (x2 <= x1 || y2 <= y1) fail(`${label}.faces.${face}.uv: inverted rectangle ${JSON.stringify(data.uv)}`);
    if (x1 < 0 || y1 < 0 || x2 > gen.resolution.width || y2 > gen.resolution.height) {
      fail(`${label}.faces.${face}.uv: outside the ${gen.resolution.width}x${gen.resolution.height} texture`);
    }
  }
  if (refCubeFields && i === 0) {
    const missing = refCubeFields.filter(f => !(f in el) && !CUBE_OPTIONAL.includes(f));
    if (missing.length) fail(`elements[0]: missing fields present in the reference cube: ${missing.join(', ')}`);
    else ok('cube fields cover the reference project\'s cube fields');
  }
  if (!/^[0-9a-f-]{36}$/i.test(el.uuid)) fail(`${label}.uuid: not a uuid (${el.uuid})`);
});

// unique uuids
const uuids = gen.elements.map(e => e.uuid);
if (new Set(uuids).size !== uuids.length) fail('elements: duplicate uuids');

// ---------------- outliner ----------------
const elementUuids = new Set(uuids);
const seenInOutliner = new Set();
const GROUP_SHAPE = { name: 'string', origin: 'array', uuid: 'string', export: 'boolean', mirror_uv: 'boolean', isOpen: 'boolean', locked: 'boolean', visibility: 'boolean', autouv: 'number', selected: 'boolean', children: 'array' };
const GROUP_OPTIONAL = ['rotation', 'color', 'shade', 'bedrock_binding'];

function walkOutliner(nodes, depth = 0) {
  for (const node of nodes) {
    if (typeof node === 'string') {
      if (!elementUuids.has(node)) fail(`outliner: references unknown element uuid ${node}`);
      if (seenInOutliner.has(node)) fail(`outliner: element ${node} listed twice`);
      seenInOutliner.add(node);
      continue;
    }
    const label = `outliner(${node.name})`;
    checkShape(label, node, GROUP_SHAPE, GROUP_OPTIONAL);
    if (node.rotation && (!Array.isArray(node.rotation) || node.rotation.length !== 3)) {
      fail(`${label}.rotation: expected [x, y, z]`);
    }
    walkOutliner(node.children, depth + 1);
  }
}
walkOutliner(gen.outliner);
const unlisted = [...elementUuids].filter(u => !seenInOutliner.has(u));
if (unlisted.length) fail(`outliner: ${unlisted.length} element(s) never referenced`);

// every cube should sit inside a bone group (this model uses a bone rig)
const rootNodes = gen.outliner.filter(n => typeof n === 'object');
if (rootNodes.length !== 1) fail(`outliner: expected a single root bone, found ${rootNodes.length}`);

// ---------------- textures ----------------
// mirrors Texture.getSaveCopy() in Blockbench 4.12.2
const TEXTURE_SHAPE = {
  name: 'string', folder: 'string', namespace: 'string', id: 'string', group: 'string',
  width: 'number', height: 'number', uv_width: 'number', uv_height: 'number',
  particle: 'boolean', use_as_default: 'boolean', layers_enabled: 'boolean',
  sync_to_project: 'string', render_mode: 'string', render_sides: 'string',
  pbr_channel: 'string', frame_time: 'number', frame_order_type: 'string',
  frame_order: 'string', frame_interpolate: 'boolean',
  visible: 'boolean', internal: 'boolean', saved: 'boolean', uuid: 'string',
};
gen.textures.forEach((tex, i) => {
  const label = `textures[${i}]`;
  checkShape(label, tex, TEXTURE_SHAPE, ['source', 'path']);
  if (!tex.source && !tex.path) fail(`${label}: needs either "source" (embedded) or "path"`);
  if (typeof tex.source !== 'string' || !tex.source.startsWith('data:image/png;base64,')) {
    fail(`${label}.source: expected an embedded PNG data URL`);
  } else {
    const bytes = Buffer.from(tex.source.split(',')[1], 'base64');
    const isPng = bytes.subarray(0, 8).equals(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]));
    if (!isPng) fail(`${label}.source: base64 payload is not a PNG`);
    else {
      const w = bytes.readUInt32BE(16), h = bytes.readUInt32BE(20);
      if (w !== gen.resolution.width || h !== gen.resolution.height) {
        fail(`${label}.source: PNG is ${w}x${h} but resolution says ${gen.resolution.width}x${gen.resolution.height}`);
      } else if (tex.width !== w || tex.height !== h || tex.uv_width !== w || tex.uv_height !== h) {
        fail(`${label}: width/height/uv_width/uv_height must match the PNG (${w}x${h})`);
      } else ok(`embedded texture is a valid ${w}x${h} PNG and its metadata agrees`);
    }
  }
});
if (!gen.textures.length) fail('root.textures: at least one texture is required for a textured model');

// ---------------- animations ----------------
if (!Array.isArray(gen.animations)) {
  fail('root.animations: expected an array');
} else {
  gen.animations.forEach((anim, ai) => {
    const label = `animations[${ai}](${anim.name})`;
    for (const key of ['uuid', 'name', 'loop']) {
      if (typeof anim[key] !== 'string') fail(`${label}.${key}: expected string`);
    }
    if (!['once', 'loop', 'hold'].includes(anim.loop)) fail(`${label}.loop: invalid value "${anim.loop}"`);
    if (typeof anim.length !== 'number' || anim.length <= 0) fail(`${label}.length: expected a positive number`);
    if (typeof anim.snapping !== 'number') fail(`${label}.snapping: expected number`);
    if (typeof anim.override !== 'boolean') fail(`${label}.override: expected boolean`);
    if (!anim.animators || typeof anim.animators !== 'object') { fail(`${label}.animators: missing`); return; }

    const groupNames = new Set();
    (function collect(nodes) {
      for (const node of nodes) {
        if (typeof node === 'string') continue;
        groupNames.add(node.name);
        collect(node.children);
      }
    })(gen.outliner);

    const animatorKeys = Object.keys(anim.animators);
    let keyframeCount = 0;
    const channelsSeen = {};
    for (const [key, bone] of Object.entries(anim.animators)) {
      const blabel = `${label}.animators[${key}]`;
      if (typeof bone.name !== 'string') fail(`${blabel}.name: expected string`);
      if (bone.type !== 'bone') fail(`${blabel}.type: expected "bone"`);
      if (!groupNames.has(bone.name)) fail(`${blabel}: name "${bone.name}" is not a bone in the outliner`);
      if (key !== bone.name && !/^[0-9a-f-]{36}$/i.test(key)) fail(`${blabel}: key must be the bone uuid or name`);
      if (!Array.isArray(bone.keyframes)) { fail(`${blabel}.keyframes: expected array`); continue; }
      for (const kf of bone.keyframes) {
        keyframeCount++;
        const klabel = `${blabel}.keyframes[${kf.time}]`;
        if (!['rotation', 'position', 'scale'].includes(kf.channel)) fail(`${klabel}.channel: invalid "${kf.channel}"`);
        channelsSeen[kf.channel] = (channelsSeen[kf.channel] || 0) + 1;
        if (typeof kf.time !== 'number' || kf.time < 0 || kf.time > anim.length + 1e-6) {
          fail(`${klabel}: time ${kf.time} outside 0..${anim.length}`);
        }
        if (!['linear', 'catmullrom', 'bezier', 'step'].includes(kf.interpolation)) {
          fail(`${klabel}.interpolation: invalid "${kf.interpolation}"`);
        }
        if (!Array.isArray(kf.data_points) || !kf.data_points.length) { fail(`${klabel}.data_points: empty`); continue; }
        for (const dp of kf.data_points) {
          for (const axis of ['x', 'y', 'z']) {
            if (typeof dp[axis] !== 'number' && typeof dp[axis] !== 'string') {
              fail(`${klabel}.data_points.${axis}: expected number or molang string`);
            }
          }
        }
        if (kf.interpolation === 'bezier') {
          for (const key2 of ['bezier_left_value', 'bezier_right_value']) {
            if (!(key2 in kf)) fail(`${klabel}: bezier keyframe is missing ${key2}`);
          }
        } else {
          for (const key2 of ['bezier_left_time', 'bezier_left_value', 'bezier_right_time', 'bezier_right_value', 'bezier_linked']) {
            if (key2 in kf) fail(`${klabel}: non-bezier keyframe should not carry ${key2}`);
          }
        }
      }
    }
    // loop continuity: first and last key of every channel must match
    for (const [key, bone] of Object.entries(anim.animators)) {
      const byChannel = {};
      for (const kf of bone.keyframes) (byChannel[kf.channel] = byChannel[kf.channel] || []).push(kf);
      for (const [channel, list] of Object.entries(byChannel)) {
        list.sort((a, b) => a.time - b.time);
        const first = list[0], last = list[list.length - 1];
        if (anim.loop === 'loop' && Math.abs(first.time) < 1e-6 && Math.abs(last.time - anim.length) < 1e-6) {
          for (const axis of ['x', 'y', 'z']) {
            if (Math.abs((first.data_points[0][axis] || 0) - (last.data_points[0][axis] || 0)) > 1e-6) {
              fail(`${label}: ${bone.name}.${channel} does not loop (${axis}: ${first.data_points[0][axis]} != ${last.data_points[0][axis]})`);
            }
          }
        }
      }
    }
    ok(`animation "${anim.name}": loop=${anim.loop} length=${anim.length}s animators=${animatorKeys.length} keyframes=${keyframeCount} channels=${JSON.stringify(channelsSeen)}`);
  });
}

// ---------------- geometry scale sanity ----------------
let gMin = [Infinity, Infinity, Infinity], gMax = [-Infinity, -Infinity, -Infinity];
for (const el of gen.elements) {
  for (const p of [el.from, el.to]) {
    for (let i = 0; i < 3; i++) { gMin[i] = Math.min(gMin[i], p[i]); gMax[i] = Math.max(gMax[i], p[i]); }
  }
}
const size = gMax.map((v, i) => +(v - gMin[i]).toFixed(2));
if (Math.abs(gMin[1]) > 0.01) fail(`geometry: lowest point is y=${gMin[1]}, expected the feet to rest on y=0`);
if (size[1] < 24 || size[1] > 34) fail(`geometry: height ${size[1]}px is outside the expected 24-34px range`);
ok(`geometry: ${gen.elements.length} cubes, bounds ${gMin.join(',')} .. ${gMax.join(',')} (${size.join(' x ')} px)`);

// ---------------- report ----------------
console.log('== .bbmodel validation ==');
console.log('file     : ' + target);
console.log('reference: ' + (refExists ? reference : '(none)'));
console.log('');
for (const n of notes) console.log('  ok   ' + n);
for (const p of problems) console.log('  FAIL ' + p);
console.log('');
console.log(problems.length ? `${problems.length} problem(s) found` : 'all checks passed');
process.exit(problems.length ? 1 : 0);
