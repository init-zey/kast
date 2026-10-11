// Build a Blockbench project (.bbmodel 4.10) from the Player definitions:
// bones become groups, cubes get per-face UVs, and the walk cycle becomes
// animation keyframes. Also exports Bedrock geometry/animation companions.
const { LENGTH } = require('./player-walk.js');

let counter = 0;
/** Deterministic UUIDv4-shaped id (keeps regenerated files diffable). */
function uid() {
  counter++;
  const hex = (counter * 0x9e3779b1 >>> 0).toString(16).padStart(8, '0');
  const a = (counter * 2654435761 >>> 0).toString(16).padStart(8, '0');
  return `${hex}-${a.slice(0, 4)}-4${a.slice(4, 7)}-8${hex.slice(0, 3)}-${a}${hex.slice(0, 4)}`;
}
function resetUids() { counter = 0; }
function round(v) { return Math.round(v * 1000) / 1000; }

const FACE_ORDER = ['north', 'east', 'south', 'west', 'up', 'down'];

/**
 * Validate the model definition before writing: every face must reference a real
 * atlas rectangle whose size matches the face's pixel size.
 */
function validateModel(model) {
  const problems = [];
  const ATLAS = model.ATLAS;
  const rects = Object.entries(ATLAS);
  // atlas rectangles must not overlap
  for (let i = 0; i < rects.length; i++) {
    for (let j = i + 1; j < rects.length; j++) {
      const [ka, a] = rects[i], [kb, b] = rects[j];
      const overlap = a[0] < b[0] + b[2] && b[0] < a[0] + a[2] && a[1] < b[1] + b[3] && b[1] < a[1] + a[3];
      if (overlap) problems.push(`atlas overlap: ${ka} and ${kb}`);
    }
  }
  for (const bone of model.BONES) {
    for (const cube of bone.cubes || []) {
      validateCube(cube, ATLAS, model, problems);
    }
  }
  return problems;
}

function validateCube(cube, ATLAS, model, problems) {
  const size = [cube.to[0] - cube.from[0], cube.to[1] - cube.from[1], cube.to[2] - cube.from[2]];
  if (size.some(s => s <= 0)) problems.push(`${cube.name}: non-positive size ${size}`);
  const expected = {
    north: [size[0], size[1]], south: [size[0], size[1]],
    east: [size[2], size[1]], west: [size[2], size[1]],
    up: [size[0], size[2]], down: [size[0], size[2]],
  };
  for (const face of FACE_ORDER) {
    const key = cube.uv[face];
    if (!key) { problems.push(`${cube.name}: missing uv for ${face}`); continue; }
    const rect = ATLAS[key];
    if (!rect) { problems.push(`${cube.name}: unknown atlas key "${key}" for ${face}`); continue; }
    const w = rect[2], h = rect[3];
    const [ew, eh] = expected[face];
    if (Math.abs(w - ew) > 1.01 || Math.abs(h - eh) > 1.01) {
      problems.push(`${cube.name}.${face}: atlas ${key} is ${w}x${h} but face needs ${round(ew)}x${round(eh)}`);
    }
    if (rect[0] < 0 || rect[1] < 0 || rect[0] + w > model.TEXTURE_WIDTH || rect[1] + h > model.TEXTURE_HEIGHT) {
      problems.push(`${cube.name}.${face}: atlas ${key} outside texture`);
    }
  }
}

/** Convert the bone list into outliner nodes + a flat element list. */
function buildHierarchy(bones) {
  const byName = {};
  for (const bone of bones) {
    byName[bone.name] = {
      bone,
      node: {
        name: bone.name,
        origin: bone.origin.slice(),
        rotation: (bone.rotation || [0, 0, 0]).slice(),
        color: 0,
        uuid: uid(),
        export: true,
        mirror_uv: false,
        isOpen: false,
        locked: false,
        visibility: true,
        autouv: 0,
        selected: false,
        children: [],
      },
      elements: [],
    };
  }
  const roots = [];
  for (const bone of bones) {
    const entry = byName[bone.name];
    const parent = bone.parent ? byName[bone.parent] : null;
    if (parent) parent.node.children.push(entry.node);
    else roots.push(entry.node);
  }
  return { byName, roots };
}

function compileCube(cube, origin) {
  const [x0, y0, z0] = cube.from;
  const [x1, y1, z1] = cube.to;
  const faces = {};
  for (const face of FACE_ORDER) faces[face] = { uv: cube.uv[face] };
  return {
    name: cube.name,
    box_uv: false,
    rescale: false,
    locked: false,
    light_emission: 0,
    render_order: 'default',
    allow_mirror_modeling: true,
    from: [round(x0), round(y0), round(z0)],
    to: [round(x1), round(y1), round(z1)],
    autouv: 0,
    color: 0,
    origin: origin.slice(),
    faces,
    type: 'cube',
    uuid: uid(),
  };
}

function uvRect(key, atlas) {
  const [x, y, w, h] = atlas[key];
  return [x, y, x + w, y + h];
}

function compileAnimation(anim, boneNameToUuid) {
  const animators = {};
  for (const [boneName, channels] of Object.entries(anim.channels)) {
    const uuid = boneNameToUuid[boneName];
    if (!uuid) throw new Error(`animation targets unknown bone "${boneName}"`);
    const keyframes = [];
    for (const [channel, keys] of Object.entries(channels)) {
      for (const key of keys) {
        const point = { x: 0, y: 0, z: 0 };
        if (channel === 'rotation') {
          point.x = round(key.v[0]); point.y = round(key.v[1]); point.z = round(key.v[2]);
        } else {
          point.x = round(key.v[0]); point.y = round(key.v[1]); point.z = round(key.v[2]);
        }
        keyframes.push({
          channel,
          time: round(key.t),
          color: -1,
          interpolation: 'linear',
          data_points: [point],
          uuid: uid(),
        });
      }
    }
    animators[uuid] = { name: boneName, type: 'bone', keyframes };
  }
  return {
    uuid: uid(),
    name: anim.name,
    loop: anim.loop,
    override: false,
    length: anim.length,
    snapping: anim.snapping,
    selected: false,
    animators,
  };
}

/**
 * Assemble the full Blockbench project object.
 * @param {object} model  player-model.js exports
 * @param {object} anim   player-walk.js walkAnimation()
 * @param {string} textureDataUrl  data:image/png;base64,...
 */
function buildProject(model, anim, textureDataUrl) {
  resetUids();
  const { BONES, ATLAS, TEXTURE_WIDTH, TEXTURE_HEIGHT, PALETTE } = model;

  const { byName, roots } = buildHierarchy(BONES);
  const elements = [];
  const boneNameToUuid = {};
  const cubeOwner = [];

  for (const bone of BONES) {
    const entry = byName[bone.name];
    boneNameToUuid[bone.name] = entry.node.uuid;
    for (const cube of bone.cubes || []) {
      const compiled = compileCube(cube, bone.origin);
      // remap face keys -> absolute uv rectangles
      for (const face of FACE_ORDER) compiled.faces[face].uv = uvRect(cube.uv[face], ATLAS);
      elements.push(compiled);
      entry.node.children.push(compiled.uuid);
      cubeOwner.push({ cube: compiled.name, bone: bone.name, shade: cube.shade });
    }
  }

  // strip outliner helper fields that Blockbench does not store
  const outliner = roots.map(cleanNode);
  function cleanNode(node) {
    return node;
  }

  // Field set mirrors Texture.getSaveCopy() in Blockbench 4.12.2.
  const texture = {
    name: 'player_robe.png',
    folder: 'entity',
    namespace: '',
    id: '0',
    group: '',
    width: TEXTURE_WIDTH,
    height: TEXTURE_HEIGHT,
    uv_width: TEXTURE_WIDTH,
    uv_height: TEXTURE_HEIGHT,
    particle: false,
    use_as_default: false,
    layers_enabled: false,
    sync_to_project: '',
    render_mode: 'default',
    render_sides: 'auto',
    pbr_channel: 'color',
    frame_time: 1,
    frame_order_type: 'loop',
    frame_order: '',
    frame_interpolate: false,
    visible: true,
    internal: true,
    saved: false,
    uuid: uid(),
    source: textureDataUrl,
  };

  const project = {
    meta: {
      format_version: '4.10',
      model_format: 'free',
      box_uv: false,
    },
    name: 'player',
    model_identifier: '',
    visible_box: [1, 1, 0],
    variable_placeholders: '',
    variable_placeholder_buttons: [],
    timeline_setups: [],
    unhandled_root_fields: {},
    resolution: { width: TEXTURE_WIDTH, height: TEXTURE_HEIGHT },
    elements,
    outliner,
    textures: [texture],
  };
  project.animations = [compileAnimation(anim, boneNameToUuid)];
  return project;
}

/** Bedrock geometry companion (minecraft:geometry 1.12.0). */
function buildBedrockGeometry(model) {
  const { BONES, TEXTURE_WIDTH, TEXTURE_HEIGHT } = model;
  const mc = (v) => round(v);
  const bones = [];
  for (const bone of BONES) {
    if (!bone.cubes || !bone.cubes.length) continue;
    bones.push({
      name: bone.name,
      parent: bone.parent && bone.parent !== 'root' ? bone.parent : undefined,
      pivot: bone.origin.map(mc),
      rotation: (bone.rotation || [0, 0, 0]).some(v => v) ? bone.rotation.map(mc) : undefined,
      cubes: bone.cubes.map(cube => ({
        origin: cube.from.map(mc),
        size: [round(cube.to[0] - cube.from[0]), round(cube.to[1] - cube.from[1]), round(cube.to[2] - cube.from[2])],
        uv: Object.fromEntries(Object.entries(cube.uv).map(([face, key]) => {
          const r = model.ATLAS[key];
          return [face, { uv: [r[0], r[1]], uv_size: [r[2], r[3]] }];
        })),
      })),
    });
  }
  return {
    format_version: '1.12.0',
    'minecraft:geometry': [{
      description: {
        identifier: 'geometry.player',
        texture_width: TEXTURE_WIDTH,
        texture_height: TEXTURE_HEIGHT,
        visible_bounds_width: 2,
        visible_bounds_height: 3,
        visible_bounds_offset: [0, 1.5, 0],
      },
      bones,
    }],
  };
}

/** Bedrock animation companion mirroring the keyframes in the project. */
function buildBedrockAnimation(anim) {
  const bones = {};
  for (const [boneName, channels] of Object.entries(anim.channels)) {
    const out = {};
    for (const [channel, keys] of Object.entries(channels)) {
      const curve = {};
      for (const key of keys) {
        const time = round(key.t);
        const value = key.v.map(v => round(v));
        curve[String(time)] = channel === 'rotation'
          ? value
          : value;
      }
      out[channel] = curve;
    }
    bones[boneName] = out;
  }
  return {
    format_version: '1.8.0',
    animations: {
      'animation.player.walk': {
        loop: true,
        animation_length: LENGTH,
        bones,
      },
    },
  };
}

module.exports = {
  buildProject, buildBedrockGeometry, buildBedrockAnimation, validateModel, uid, resetUids,
};
