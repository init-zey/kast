// "Player" — an armless, hooded, hunched figure in a grey robe with the face
// hidden in shadow. Geometry, UV atlas and cloth-shading data live here so the
// texture painter and the .bbmodel writer always agree.
//
// Coordinate space is Blockbench pixels: 16 px = 1 block, +Y up, -Z forward.
//
// UV areas are NOT hand-authored: every face is keyed by (material, pixel size)
// and areas are generated from those groups, so a UV can never stretch and the
// atlas can never drift away from the geometry.
const { packAtlas } = require('./atlas-pack.js');

const TEXTURE_WIDTH = 64;
const TEXTURE_HEIGHT = 128;

// ---------------------------------------------------------------------------
// palette — weathered grey cloth
// ---------------------------------------------------------------------------
const PALETTE = {
  robeMid: '#787881',
  robeDark: '#5b5b65',
  robeDeep: '#484851',
  robeLight: '#8e8e98',
  robePale: '#a6a6b0',
  hemDark: '#4f4f58',
  hemTrim: '#9c9ca6',
  hoodMid: '#6b6b75',
  hoodDark: '#4a4a54',
  hoodDeep: '#2c2c34',
  voidBlack: '#06060a',
  voidEdge: '#17171d',
};

// ---------------------------------------------------------------------------
// materials: how each group of faces gets painted
// ---------------------------------------------------------------------------
// gradient runs top -> bottom, `folds` adds vertical creases, `hem` adds the
// worn light band along the bottom edge, `seams` darkens the border.
const MATERIALS = {
  hood:    { base: 'hoodMid',  top: 1.20, bottom: 0.92, noise: 0.05, folds: 2, seams: true },
  hood2:   { base: 'hoodMid',  top: 1.28, bottom: 1.00, noise: 0.045, folds: 2, seams: true },
  linen:   { base: 'hoodDeep', top: 0.90, bottom: 0.70, noise: 0.03 },
  robe:    { base: 'robeMid',  top: 1.14, bottom: 0.86, noise: 0.055, folds: 3, seams: true },
  robe2:   { base: 'robeMid',  top: 1.08, bottom: 0.82, noise: 0.05, folds: 2, seams: true },
  robetop: { base: 'robeLight', top: 1.26, bottom: 0.98, noise: 0.04, seams: true },
  waist:   { base: 'robeDark', top: 1.06, bottom: 0.80, noise: 0.05, folds: 2 },
  pelvis:  { base: 'robeDark', top: 1.06, bottom: 0.80, noise: 0.05, folds: 2 },
  skirt:   { base: 'robeMid',  top: 1.12, bottom: 0.78, noise: 0.06, folds: 3, hem: true, seams: true },
  sash:    { base: 'robePale', top: 1.04, bottom: 0.82, noise: 0.03, seams: true },
  hem:     { base: 'hemDark',  top: 1.12, bottom: 0.80, noise: 0.05, folds: 2, hem: true },
  hemfold: { base: 'robeMid',  top: 1.08, bottom: 0.78, noise: 0.06, folds: 3, hem: true, seams: true },
  sleeve:  { base: 'robeLight', top: 1.14, bottom: 0.84, noise: 0.06, folds: 2, hem: true, seams: true },
  sleeve2: { base: 'robeMid',  top: 1.00, bottom: 0.74, noise: 0.05, folds: 2, hem: true },
  leg:     { base: 'robeDark', top: 1.00, bottom: 0.72, noise: 0.05, folds: 2 },
  leg2:    { base: 'robeDeep', top: 0.98, bottom: 0.70, noise: 0.05, folds: 2 },
  boot:    { base: 'hemDark',  top: 1.00, bottom: 0.70, noise: 0.055, hem: true },
  void:    { base: 'voidBlack', top: 1.00, bottom: 1.00, noise: 0.015 },
  rim:     { base: 'voidEdge', top: 1.00, bottom: 0.88, noise: 0.0 },
  under:   { base: 'robeDeep', top: 1.00, bottom: 0.84, noise: 0.04 },
};

// Faces that sit in front of a cavity read as recessed; applied by the renderer.
const FACE_TINTS = {
  north: 1.0, south: 0.84, east: 0.92, west: 0.92, up: 1.05, down: 0.8,
};

// ---------------------------------------------------------------------------
// skeleton
// ---------------------------------------------------------------------------
function faces(mat) {
  if (typeof mat === 'string') {
    return { north: mat, south: mat, east: mat, west: mat, up: mat, down: mat };
  }
  return mat;
}

const BONES = [
  { name: 'root', origin: [0, 0, 0], rotation: [0, 0, 0], cubes: [] },

  {
    name: 'pelvis', parent: 'root', origin: [0, 10, 0], rotation: [0, 0, 0],
    cubes: [{ name: 'pelvis', from: [-2.5, 10, -2.5], to: [2.5, 14.5, 2.5], mat: 'pelvis', shade: 0.88 }],
  },

  {
    name: 'skirt', parent: 'root', origin: [0, 10, 0], rotation: [0, 0, 0],
    cubes: [
      { name: 'robe_skirt', from: [-4.5, 2.0, -4.5], to: [4.5, 10.5, 4.5],
        mat: { north: 'skirt', south: 'skirt', east: 'skirt', west: 'skirt', up: 'waist', down: 'hem' }, shade: 0.94 },
      { name: 'robe_sash', from: [-4.75, 10.0, -4.75], to: [4.75, 11.5, 4.75], mat: 'sash', shade: 1.0 },
      { name: 'robe_hem', from: [-5.0, 0.4, -5.0], to: [5.0, 2.4, 5.0], mat: 'hem', shade: 0.82 },
    ],
  },
  {
    name: 'hem_front', parent: 'skirt', origin: [0, 3.5, -3.5], rotation: [0, 0, 0],
    cubes: [{ name: 'hem_fold_front', from: [-4.5, 0.3, -5.1], to: [4.5, 5.0, -3.6],
      mat: { north: 'hemfold', south: 'hemfold', east: 'hem', west: 'hem', up: 'hem', down: 'hem' }, shade: 0.88 }],
  },
  {
    name: 'hem_back', parent: 'skirt', origin: [0, 3.5, 3.5], rotation: [0, 0, 0],
    cubes: [{ name: 'hem_fold_back', from: [-4.5, 0.3, 3.6], to: [4.5, 5.0, 5.1],
      mat: { north: 'hemfold', south: 'hemfold', east: 'hem', west: 'hem', up: 'hem', down: 'hem' }, shade: 0.84 }],
  },

  {
    name: 'spine', parent: 'root', origin: [0, 11.5, 0], rotation: [0, 0, 0],
    cubes: [{ name: 'waist', from: [-2.75, 11.5, -2.75], to: [2.75, 15.0, 2.75], mat: 'waist', shade: 0.9 }],
  },

  {
    name: 'chest', parent: 'spine', origin: [0, 14.5, 0], rotation: [0, 0, 0],
    cubes: [
      { name: 'robe_body', from: [-5.0, 14.5, -3.5], to: [5.0, 20.0, 3.5],
        mat: { north: 'robe', south: 'robe2', east: 'robe2', west: 'robe2', up: 'robetop', down: 'under' }, shade: 1.0 },
      { name: 'hunch', from: [-3.25, 18.0, 0.0], to: [3.25, 21.5, 5.0], mat: 'robe', shade: 0.92 },
      { name: 'shoulders', from: [-6.5, 18.0, -3.5], to: [6.5, 20.5, 3.5],
        mat: { north: 'robe', south: 'robe2', east: 'robe2', west: 'robe2', up: 'robetop', down: 'under' }, shade: 0.96 },
    ],
  },

  // The hood is a frame of panels around a real opening. The dark void sits in
  // the frame's own plane so the opening reads as a hole, not a raised slab.
  {
    name: 'head', parent: 'chest', origin: [0, 19.0, -0.5], rotation: [0, 0, 0],
    cubes: [
      // The shell's front face sits behind the void plate by a clear margin: the
      // camera dips slightly, which makes lower geometry read as nearer, so an
      // exactly coplanar plate would lose the depth test.
      { name: 'hood_back', from: [-5.0, 19.0, -4.0], to: [5.0, 27.0, 4.8], mat: 'hood', shade: 0.98 },
      { name: 'hood_top', from: [-5.0, 25.4, -5.2], to: [5.0, 27.0, -2.0], mat: 'hood2', shade: 1.1 },
      { name: 'hood_brow', from: [-5.0, 24.0, -5.2], to: [5.0, 25.4, -4.4],
        mat: { north: 'hood2', south: 'linen', east: 'hood', west: 'hood', up: 'hood2', down: 'linen' }, shade: 1.0 },
      { name: 'hood_jaw', from: [-5.0, 19.4, -5.2], to: [5.0, 20.9, -4.4],
        mat: { north: 'hood2', south: 'linen', east: 'hood', west: 'hood', up: 'linen', down: 'under' }, shade: 0.9 },
      { name: 'hood_side_l', from: [-5.0, 20.9, -5.2], to: [-3.3, 24.0, -4.4], mat: 'hood', shade: 0.94 },
      { name: 'hood_side_r', from: [3.3, 20.9, -5.2], to: [5.0, 24.0, -4.4], mat: 'hood', shade: 0.94 },
      // The cavity is a thin dark plate filling the opening, set just inside the
      // frame's front plane. Keeping it shallow stops its own top face from
      // showing as a lit sliver when the camera looks slightly downward.
      { name: 'face_void', from: [-2.8, 21.3, -5.0], to: [2.8, 24.0, -4.85],
        mat: { north: 'void', south: 'linen', east: 'rim', west: 'rim', up: 'rim', down: 'rim' }, shade: 0.7 },
      { name: 'hood_dome', from: [-3.9, 27.0, -4.4], to: [3.9, 28.4, 3.8], mat: 'hood2', shade: 1.06 },
      { name: 'hood_tip', from: [-2.2, 28.4, -2.8], to: [2.2, 29.4, 2.8], mat: 'hood2', shade: 1.12 },
    ],
  },

  {
    name: 'sleeve_l', parent: 'chest', origin: [7.25, 19.25, 0], rotation: [0, 0, 0],
    cubes: [{ name: 'sleeve_left', from: [-8.25, 7.5, -2.75], to: [-5.25, 20.0, 2.75],
      mat: { north: 'sleeve', south: 'sleeve', east: 'sleeve', west: 'sleeve2', up: 'sleeve', down: 'sleeve2' }, shade: 0.9 }],
  },
  {
    name: 'sleeve_r', parent: 'chest', origin: [-7.25, 19.25, 0], rotation: [0, 0, 0],
    cubes: [{ name: 'sleeve_right', from: [5.25, 7.5, -2.75], to: [8.25, 20.0, 2.75],
      mat: { north: 'sleeve', south: 'sleeve', east: 'sleeve2', west: 'sleeve', up: 'sleeve', down: 'sleeve2' }, shade: 0.9 }],
  },

  {
    name: 'hip_l', parent: 'root', origin: [2.25, 10.5, 0], rotation: [0, 0, 0],
    cubes: [{ name: 'thigh_left', from: [-3.5, 4.5, -2.25], to: [-1.0, 10.5, 2.25],
      mat: { north: 'leg', south: 'leg2', east: 'leg', west: 'leg2', up: 'under', down: 'under' }, shade: 0.86 }],
  },
  {
    name: 'hip_r', parent: 'root', origin: [-2.25, 10.5, 0], rotation: [0, 0, 0],
    cubes: [{ name: 'thigh_right', from: [1.0, 4.5, -2.25], to: [3.5, 10.5, 2.25],
      mat: { north: 'leg', south: 'leg2', east: 'leg2', west: 'leg', up: 'under', down: 'under' }, shade: 0.86 }],
  },
  {
    name: 'knee_l', parent: 'hip_l', origin: [-2.25, 4.75, 0], rotation: [0, 0, 0],
    cubes: [{ name: 'shin_left', from: [-3.35, 0.9, -2.1], to: [-1.15, 4.9, 2.1],
      mat: { north: 'leg2', south: 'leg2', east: 'leg2', west: 'leg2', up: 'under', down: 'under' }, shade: 0.78 }],
  },
  {
    name: 'knee_r', parent: 'hip_r', origin: [2.25, 4.75, 0], rotation: [0, 0, 0],
    cubes: [{ name: 'shin_right', from: [1.15, 0.9, -2.1], to: [3.35, 4.9, 2.1],
      mat: { north: 'leg2', south: 'leg2', east: 'leg2', west: 'leg2', up: 'under', down: 'under' }, shade: 0.78 }],
  },
  {
    name: 'ankle_l', parent: 'knee_l', origin: [-2.25, 1.2, 0], rotation: [0, 0, 0],
    cubes: [{ name: 'foot_left', from: [-3.3, 0, -2.11], to: [-1.2, 1.3, 3.0], mat: 'boot', shade: 0.92 }],
  },
  {
    name: 'ankle_r', parent: 'knee_r', origin: [2.25, 1.2, 0], rotation: [0, 0, 0],
    cubes: [{ name: 'foot_right', from: [1.2, 0, -2.11], to: [3.3, 1.3, 3.0], mat: 'boot', shade: 0.92 }],
  },
];

// ---------------------------------------------------------------------------
// derive UV areas: one per (material, exact face size) group
// ---------------------------------------------------------------------------
const FACE_ORDER = ['north', 'east', 'south', 'west', 'up', 'down'];
const warnings = [];
const AREA_INFO = {};

function faceSize(cube, face) {
  const sx = cube.to[0] - cube.from[0];
  const sy = cube.to[1] - cube.from[1];
  const sz = cube.to[2] - cube.from[2];
  return {
    north: [sx, sy], south: [sx, sy], east: [sz, sy],
    west: [sz, sy], up: [sx, sz], down: [sx, sz],
  }[face];
}

for (const bone of BONES) {
  for (const cube of bone.cubes || []) {
    const mats = faces(cube.mat);
    cube.uv = {};
    for (const face of FACE_ORDER) {
      const material = mats[face];
      if (!MATERIALS[material]) throw new Error(`${cube.name}.${face}: unknown material "${material}"`);
      const [w, h] = faceSize(cube, face);
      const key = `${material}@${w}x${h}`;
      if (!AREA_INFO[key]) AREA_INFO[key] = { name: key, material, w, h, usedBy: [] };
      AREA_INFO[key].usedBy.push(`${cube.name}.${face}`);
      cube.uv[face] = key;
    }
  }
}

const AREA_LIST = Object.values(AREA_INFO);
const packedAtlas = packAtlas(
  AREA_LIST.map(a => ({ name: a.name, w: a.w, h: a.h })),
  TEXTURE_WIDTH, TEXTURE_HEIGHT,
);
const ATLAS = packedAtlas.rects;

// painting recipes keyed by atlas area name
const REGIONS = {};
for (const area of AREA_LIST) REGIONS[area.name] = MATERIALS[area.material];

module.exports = {
  TEXTURE_WIDTH, TEXTURE_HEIGHT, PALETTE, ATLAS, FACE_TINTS, BONES, REGIONS,
  MATERIALS, AREA_INFO, AREA_LIST, packed: packedAtlas, UV_WARNINGS: warnings,
};
