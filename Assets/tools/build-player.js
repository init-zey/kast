// Build the Player model: paints the texture, validates the geometry, writes the
// .bbmodel project plus Bedrock companions, and renders preview sheets so the
// result can be inspected without opening Blockbench.
const fs = require('fs');
const path = require('path');

const model = require('./player-model.js');
const { walkAnimation } = require('./player-walk.js');
const { paintTexture } = require('./paint-texture.js');
const { encodePNG } = require('./png.js');
const { buildProject, buildBedrockGeometry, buildBedrockAnimation, validateModel } = require('./bbmodel.js');
const { renderSheet } = require('./raster.js');

const ROOT = path.resolve(__dirname, '..');
const OUT = path.join(ROOT, 'player');
const PREVIEW = path.join(ROOT, 'previews');

fs.mkdirSync(OUT, { recursive: true });
fs.mkdirSync(PREVIEW, { recursive: true });

// ---------- validate ----------
const problems = validateModel(model);
if (problems.length) {
  console.error('MODEL VALIDATION FAILED');
  for (const p of problems) console.error('  - ' + p);
  process.exit(1);
}

// ---------- texture ----------
const canvas = paintTexture();
const png = encodePNG(canvas.data, model.TEXTURE_WIDTH, model.TEXTURE_HEIGHT);
fs.writeFileSync(path.join(OUT, 'player_texture.png'), png);
const dataUrl = 'data:image/png;base64,' + png.toString('base64');

// ---------- project ----------
const anim = walkAnimation();
const project = buildProject(model, anim, dataUrl);
project.description = '无臂驼背、灰袍遮面的 Player 人物模型，含行走动画 (walk)。';
project.metadata = { author: 'kast', purpose: 'player-character' };

fs.writeFileSync(path.join(OUT, 'Player.bbmodel'), JSON.stringify(project, null, 2));
fs.writeFileSync(path.join(OUT, 'player.geo.json'), JSON.stringify(buildBedrockGeometry(model), null, 2));
fs.writeFileSync(path.join(OUT, 'player.animation.json'), JSON.stringify(buildBedrockAnimation(anim), null, 2));

// ---------- previews ----------
const poses = [
  { label: 'T-pose', pose: {} },
  { label: 'walk f0', pose: poseAt(0.0) },
  { label: 'walk f0.3', pose: poseAt(0.3) },
  { label: 'walk f0.6', pose: poseAt(0.6) },
];
const views = [
  { label: 'front', yaw: 0, pitch: 0 },
  { label: 'side', yaw: 90, pitch: 0 },
  { label: 'three-quarter', yaw: 35, pitch: 8 },
  { label: 'back', yaw: 180, pitch: 4 },
];

const sheetPose = renderSheet(model, canvas.data, [{}], views, 200);
fs.writeFileSync(path.join(PREVIEW, 'player-views.png'), sheetPose);

// tight head study (front, 3/4, side) to check the hood opening and the void
const { render } = require('./raster.js');
const headViews = [
  { yaw: 0, pitch: 4 },
  { yaw: 30, pitch: 10 },
  { yaw: 75, pitch: 6 },
];
const HEAD_SIZE = 260;
const headSheet = new Uint8ClampedArray(HEAD_SIZE * 3 * HEAD_SIZE * 4);
for (let i = 0; i < headSheet.length; i += 4) {
  headSheet[i] = 18; headSheet[i + 1] = 19; headSheet[i + 2] = 24; headSheet[i + 3] = 255;
}
headViews.forEach((view, col) => {
  const img = render(model, canvas.data, {
    ...view, size: HEAD_SIZE, ambient: 0.88, diffuse: 0.28,
    center: [0, 23.6, 0], zoom: 13,
  });
  for (let y = 0; y < HEAD_SIZE; y++) {
    for (let x = 0; x < HEAD_SIZE; x++) {
      const di = (y * HEAD_SIZE * 3 + col * HEAD_SIZE + x) * 4;
      const si = (y * HEAD_SIZE + x) * 4;
      headSheet[di] = img.pixels[si];
      headSheet[di + 1] = img.pixels[si + 1];
      headSheet[di + 2] = img.pixels[si + 2];
      headSheet[di + 3] = 255;
    }
  }
});
fs.writeFileSync(path.join(PREVIEW, 'player-head.png'), encodePNG(headSheet, HEAD_SIZE * 3, HEAD_SIZE));

const sheetWalk = renderSheet(model, canvas.data, poses.map(p => p.pose), [views[0], views[1]], 190);
fs.writeFileSync(path.join(PREVIEW, 'player-walk.png'), sheetWalk);

// contact-sheet of the raw texture, upscaled 6x
const px = 6;
const tex = new Uint8ClampedArray(model.TEXTURE_WIDTH * px * model.TEXTURE_HEIGHT * px * 4);
for (let y = 0; y < model.TEXTURE_HEIGHT * px; y++) {
  for (let x = 0; x < model.TEXTURE_WIDTH * px; x++) {
    const si = (Math.floor(y / px) * model.TEXTURE_WIDTH + Math.floor(x / px)) * 4;
    const di = (y * model.TEXTURE_WIDTH * px + x) * 4;
    tex[di] = canvas.data[si]; tex[di + 1] = canvas.data[si + 1];
    tex[di + 2] = canvas.data[si + 2]; tex[di + 3] = 255;
  }
}
fs.writeFileSync(path.join(PREVIEW, 'player-texture.png'),
  encodePNG(tex, model.TEXTURE_WIDTH * px, model.TEXTURE_HEIGHT * px));

/** Sample the walk animation's channels at a time to build a pose object. */
function poseAt(time) {
  const pose = {};
  const roundTime = (t) => Math.round(t * 1000) / 1000;
  for (const [bone, channels] of Object.entries(anim.channels)) {
    const rotation = channels.rotation;
    if (!rotation) continue;
    const t = roundTime(time);
    let before = rotation[0], after = rotation[rotation.length - 1];
    for (let i = 0; i < rotation.length; i++) {
      if (rotation[i].t <= t) before = rotation[i];
      if (rotation[i].t >= t) { after = rotation[i]; break; }
    }
    const span = after.t - before.t;
    const f = span > 0 ? (t - before.t) / span : 0;
    pose[bone] = before.v.map((v, i) => v + (after.v[i] - v) * f);
  }
  return pose;
}

// ---------- report ----------
const cubeCount = model.BONES.reduce((n, b) => n + (b.cubes ? b.cubes.length : 0), 0);
const animators = Object.keys(project.animations[0].animators).length;
const keyframes = Object.values(project.animations[0].animators)
  .reduce((n, a) => n + a.keyframes.length, 0);

console.log('Player model built');
console.log('  bones      :', model.BONES.length);
console.log('  cubes      :', cubeCount);
console.log('  texture    :', `${model.TEXTURE_WIDTH}x${model.TEXTURE_HEIGHT} (${(png.length / 1024).toFixed(1)} KiB)`);
console.log('  animation  :', `${project.animations[0].name} length=${project.animations[0].length}s loop=${project.animations[0].loop}`);
console.log('  animators  :', animators, '->', keyframes, 'keyframes');
const boundsMin = [Infinity, Infinity, Infinity], boundsMax = [-Infinity, -Infinity, -Infinity];
for (const bone of model.BONES) {
  for (const cube of bone.cubes || []) {
    for (const point of [cube.from, cube.to]) {
      for (let i = 0; i < 3; i++) {
        boundsMin[i] = Math.min(boundsMin[i], point[i]);
        boundsMax[i] = Math.max(boundsMax[i], point[i]);
      }
    }
  }
}
console.log('  bounds     :', boundsMin.map(v => v.toFixed(2)).join(','), '->', boundsMax.map(v => v.toFixed(2)).join(','));
console.log('  files      :', fs.readdirSync(OUT).join(', '));
