// Tiny software renderer: rasterizes the assembled model (rotated by the walk
// poses) from arbitrary camera angles so the silhouette and texture can be
// checked visually. Orthographic, z-buffered, nearest-neighbour sampling.
const { encodePNG } = require('./png.js');

const DEG = Math.PI / 180;

function rotPoint(p, rot, origin) {
  const [rx, ry, rz] = rot.map(v => v * DEG);
  let x = p[0] - origin[0], y = p[1] - origin[1], z = p[2] - origin[2];
  // Z, then Y, then X (Blockbench's default rotation order)
  let c = Math.cos(rz), s = Math.sin(rz);
  [x, y] = [x * c - y * s, x * s + y * c];
  c = Math.cos(ry); s = Math.sin(ry);
  [x, z] = [x * c + z * s, -x * s + z * c];
  c = Math.cos(rx); s = Math.sin(rx);
  [y, z] = [y * c - z * s, y * s + z * c];
  return [x + origin[0], y + origin[1], z + origin[2]];
}

// Face definitions matching Blockbench's face winding + outward normals.
const FACE_DEFS = {
  north: { verts: [[0, 0, 0], [0, 1, 0], [1, 1, 0], [1, 0, 0]], normal: [0, 0, -1] },
  south: { verts: [[1, 0, 1], [1, 1, 1], [0, 1, 1], [0, 0, 1]], normal: [0, 0, 1] },
  east:  { verts: [[1, 0, 0], [1, 1, 0], [1, 1, 1], [1, 0, 1]], normal: [1, 0, 0] },
  west:  { verts: [[0, 0, 1], [0, 1, 1], [0, 1, 0], [0, 0, 0]], normal: [-1, 0, 0] },
  up:    { verts: [[0, 1, 1], [1, 1, 1], [1, 1, 0], [0, 1, 0]], normal: [0, 1, 0] },
  down:  { verts: [[0, 0, 0], [1, 0, 0], [1, 0, 1], [0, 0, 1]], normal: [0, -1, 0] },
};
// UV corner order per face: [u0,v0] corresponds to verts[0], [u1,v1] to verts[2].
const UV_ORDER = {
  north: [[0, 1], [1, 1], [1, 0], [0, 0]],
  south: [[0, 1], [1, 1], [1, 0], [0, 0]],
  east:  [[0, 1], [1, 1], [1, 0], [0, 0]],
  west:  [[0, 1], [1, 1], [1, 0], [0, 0]],
  up:    [[0, 1], [1, 1], [1, 0], [0, 0]],
  down:  [[0, 1], [1, 1], [1, 0], [0, 0]],
};

/**
 * Transform every cube into world space for a given pose.
 * @param {object} model player-model exports
 * @param {Object<string,number[]>} pose bone name -> [x,y,z] rotation in degrees
 */
function posedCubes(model, pose = {}) {
  const out = [];
  const bonesByName = Object.fromEntries(model.BONES.map(b => [b.name, b]));
  function worldRotation(name) {
    const bone = bonesByName[name];
    const own = pose[name] || bone.rotation || [0, 0, 0];
    if (!bone.parent || bone.parent === 'root' && !pose[bone.parent]) return own;
    const parentRot = worldRotation(bone.parent);
    return [own[0] + parentRot[0], own[1] + parentRot[1], own[2] + parentRot[2]];
  }
  for (const bone of model.BONES) {
    if (!bone.cubes) continue;
    const rot = worldRotation(bone.name);
    for (const cube of bone.cubes) {
      const corners = [];
      for (let i = 0; i < 8; i++) {
        const p = [
          i & 1 ? cube.to[0] : cube.from[0],
          i & 2 ? cube.to[1] : cube.from[1],
          i & 4 ? cube.to[2] : cube.from[2],
        ];
        corners.push(rotPoint(p, rot, bone.origin));
      }
      out.push({ cube, bone, corners, rot });
    }
  }
  return out;
}

function buildTriangles(posed, model) {
  const tris = [];
  for (const item of posed) {
    const { cube, corners, rot } = item;
    const [x0, y0, z0] = cube.from, [x1, y1, z1] = cube.to;
    const shade = cube.shade ?? 1;
    for (const [face, def] of Object.entries(FACE_DEFS)) {
      const [rx, ry, rw, rh] = uvRect(model.ATLAS[cube.uv[face]]);
      const worldNormal = rotPoint(def.normal, rot, [0, 0, 0]);
      const p = def.verts.map(v => corners[(v[0] ? 1 : 0) | (v[1] ? 2 : 0) | (v[2] ? 4 : 0)].slice());
      const uv = UV_ORDER[face];
      const uvs = [
        [rx + uv[0][0] * rw, ry + uv[0][1] * rh],
        [rx + uv[1][0] * rw, ry + uv[1][1] * rh],
        [rx + uv[2][0] * rw, ry + uv[2][1] * rh],
        [rx + uv[3][0] * rw, ry + uv[3][1] * rh],
      ];
      tris.push({ p: [p[0], p[1], p[2]], uv: [uvs[0], uvs[1], uvs[2]], normal: worldNormal, shade });
      tris.push({ p: [p[0], p[2], p[3]], uv: [uvs[0], uvs[2], uvs[3]], normal: worldNormal, shade });
    }
  }
  return tris;
}
function uvRect(r) { return r; }

/**
 * Render the model.
 * @param {object} model
 * @param {Uint8ClampedArray} texture RGBA
 * @param {object} options { yaw, pitch, size, pose, background }
 */
function render(model, texture, options = {}) {
  const size = options.size || 256;
  const pad = options.pad ?? 12;
  const yaw = (options.yaw ?? 0) * DEG;
  const pitch = (options.pitch ?? 0) * DEG;
  const pose = options.pose || {};
  const bg = options.background || [26, 28, 34];

  const posed = posedCubes(model, pose);
  const tris = buildTriangles(posed, model);

  // The model faces -Z, so the camera sits in front of it at +Z looking back
  // along -Z. In camera space a larger z is therefore nearer the viewer.
  const cy = Math.cos(yaw), sy = Math.sin(yaw), cp = Math.cos(pitch), sp = Math.sin(pitch);
  function toCam(p) {
    const [x, y, z] = p;
    const nx = x * cy - z * sy;
    const nz = x * sy + z * cy;
    const ny = y * cp - nz * sp;
    return [nx, ny, -(-y * sp + nz * cp)];
  }
  for (const tri of tris) {
    tri.cp = tri.p.map(toCam);
    tri.cn = (() => {
      const [x, y, z] = tri.normal;
      const nx = x * cy - z * sy;
      const nz = x * sy + z * cy;
      const ny = y * cp - nz * sp;
      return [nx, ny, -(-y * sp + nz * cp)];
    })();
  }

  // bounds in camera space (screen x right, y up, z toward viewer)
  let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
  for (const tri of tris) for (const p of tri.cp) {
    minX = Math.min(minX, p[0]); maxX = Math.max(maxX, p[0]);
    minY = Math.min(minY, p[1]); maxY = Math.max(maxY, p[1]);
  }
  // options.center picks a world-space focus point, options.zoom sets px per unit
  let scale, cx, cyM;
  if (options.center) {
    const focus = toCam(options.center);
    scale = options.zoom || 12;
    cx = focus[0];
    cyM = focus[1];
  } else {
    const drawW = size - pad * 2, drawH = size - pad * 2;
    scale = Math.min(drawW / (maxX - minX || 1), drawH / (maxY - minY || 1));
    cx = (minX + maxX) / 2; cyM = (minY + maxY) / 2;
  }
  const toScreen = (p) => [size / 2 + (p[0] - cx) * scale, size / 2 - (p[1] - cyM) * scale, p[2]];

  const colour = new Uint8ClampedArray(size * size * 4);
  const depth = new Float32Array(size * size).fill(-Infinity);
  for (let i = 0; i < size * size; i++) {
    colour[i * 4] = bg[0]; colour[i * 4 + 1] = bg[1]; colour[i * 4 + 2] = bg[2]; colour[i * 4 + 3] = 255;
  }

  // light from the upper front-left (the model faces -Z)
  const light = [-0.42, 0.75, 0.51];
  const ambient = options.ambient ?? 0.82;
  const diffuse = options.diffuse ?? 0.3;

  for (const tri of tris) {
    const s = tri.cp.map(toScreen);
    const area = (s[1][0] - s[0][0]) * (s[2][1] - s[0][1]) - (s[2][0] - s[0][0]) * (s[1][1] - s[0][1]);
    if (Math.abs(area) < 1e-9) continue;
    // in screen space (y down) a front-facing triangle has positive area
    if (area <= 0) continue;
    const minSX = Math.max(0, Math.floor(Math.min(s[0][0], s[1][0], s[2][0])));
    const maxSX = Math.min(size - 1, Math.ceil(Math.max(s[0][0], s[1][0], s[2][0])));
    const minSY = Math.max(0, Math.floor(Math.min(s[0][1], s[1][1], s[2][1])));
    const maxSY = Math.min(size - 1, Math.ceil(Math.max(s[0][1], s[1][1], s[2][1])));
    const ndl = Math.max(0, tri.cn[0] * light[0] + tri.cn[1] * light[1] + tri.cn[2] * light[2]);
    const bright = (ambient + diffuse * ndl) * (tri.shade ?? 1);
    for (let py = minSY; py <= maxSY; py++) {
      for (let px = minSX; px <= maxSX; px++) {
        const w0 = ((s[1][0] - px) * (s[2][1] - py) - (s[2][0] - px) * (s[1][1] - py)) / area;
        const w1 = ((s[2][0] - px) * (s[0][1] - py) - (s[0][0] - px) * (s[2][1] - py)) / area;
        const w2 = 1 - w0 - w1;
        if (w0 < -0.0001 || w1 < -0.0001 || w2 < -0.0001) continue;
        const z = w0 * s[0][2] + w1 * s[1][2] + w2 * s[2][2];
        const idx = py * size + px;
        if (z <= depth[idx]) continue;
        depth[idx] = z;
        const u = w0 * tri.uv[0][0] + w1 * tri.uv[1][0] + w2 * tri.uv[2][0];
        const v = w0 * tri.uv[0][1] + w1 * tri.uv[1][1] + w2 * tri.uv[2][1];
        const tx = Math.max(0, Math.min(model.TEXTURE_WIDTH - 1, Math.floor(u)));
        const ty = Math.max(0, Math.min(model.TEXTURE_HEIGHT - 1, Math.floor(v)));
        const ti = (ty * model.TEXTURE_WIDTH + tx) * 4;
        colour[idx * 4] = texture[ti] * bright;
        colour[idx * 4 + 1] = texture[ti + 1] * bright;
        colour[idx * 4 + 2] = texture[ti + 2] * bright;
        colour[idx * 4 + 3] = 255;
      }
    }
  }
  return { pixels: colour, size };
}

/** Render several angles side by side into one PNG. */
function renderSheet(model, texture, poses, views, size = 240) {
  const scale = Math.ceil(size / 120); // integer upscale for crisp pixels
  const cols = views.length, rows = poses.length;
  const cell = size * scale;
  const sheet = new Uint8ClampedArray(cell * cols * cell * rows * 4);
  for (let i = 0; i < sheet.length; i += 4) {
    sheet[i] = 18; sheet[i + 1] = 19; sheet[i + 2] = 24; sheet[i + 3] = 255;
  }
  poses.forEach((pose, row) => {
    views.forEach((view, col) => {
      const img = render(model, texture, { ...view, pose, size, pad: 10 });
      for (let y = 0; y < size; y++) {
        for (let x = 0; x < size; x++) {
          for (let sy = 0; sy < scale; sy++) {
            for (let sx = 0; sx < scale; sx++) {
              const dx = col * cell + x * scale + sx;
              const dy = row * cell + y * scale + sy;
              const di = (dy * cell * cols + dx) * 4;
              const si = (y * size + x) * 4;
              sheet[di] = img.pixels[si];
              sheet[di + 1] = img.pixels[si + 1];
              sheet[di + 2] = img.pixels[si + 2];
              sheet[di + 3] = 255;
            }
          }
        }
      }
    });
  });
  return encodePNG(sheet, cell * cols, cell * rows);
}

module.exports = { render, renderSheet, posedCubes };
