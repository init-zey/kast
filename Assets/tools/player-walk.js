// Walk cycle for the armless robed figure: a heavy, slightly laboured trudge.
// Legs carry the cycle; the robe hem swings, the shoulders rock, and the hood
// dips on each footfall. All channels return to their first value so the clip
// loops seamlessly.

const T = 0.05;            // seconds per timeline key (20 keys per second)
const CYCLE = 0.1;         // half cycle: 0.1s stance, mirrored for the other leg
const LENGTH = 1.2;        // full 24-frame cycle

/** Build the leg channels: right leg leads, left leg is the mirrored half-cycle. */
function walkPoses() {
  // Rotations are degrees around each bone's origin. Positive X rotates the
  // bone's lower end backwards, negative swings it forwards.
  const rightLeg = [
    { t: 0.00, hip: -22, knee: 4, ankle: 4 },
    { t: 0.10, hip: -9, knee: 12, ankle: -2 },
    { t: 0.20, hip: 2, knee: 9, ankle: -8 },
    { t: 0.30, hip: 11, knee: 4, ankle: -12 },
    { t: 0.40, hip: 22, knee: 2, ankle: -8 },
    { t: 0.50, hip: 13, knee: 26, ankle: 4 },
    { t: 0.60, hip: -2, knee: 46, ankle: 12 },
    { t: 0.70, hip: -18, knee: 30, ankle: 8 },
  ];
  const leftLeg = rightLeg.map(p => ({
    t: p.t < 0.6 ? p.t + 0.6 : p.t - 0.6, hip: -p.hip, knee: p.knee, ankle: p.ankle,
  })).sort((a, b) => a.t - b.t);

  const channels = {
    hip_r: {
      rotation: rightLeg.map(p => ({ t: p.t, v: [p.hip, 0, 0] })),
    },
    knee_r: {
      rotation: rightLeg.map(p => ({ t: p.t, v: [p.knee, 0, 0] })),
    },
    ankle_r: {
      rotation: rightLeg.map(p => ({ t: p.t, v: [p.ankle, 0, 0] })),
    },
    hip_l: {
      rotation: leftLeg.map(p => ({ t: p.t, v: [p.hip, 0, 0] })),
    },
    knee_l: {
      rotation: leftLeg.map(p => ({ t: p.t, v: [p.knee, 0, 0] })),
    },
    ankle_l: {
      rotation: leftLeg.map(p => ({ t: p.t, v: [p.ankle, 0, 0] })),
    },
  };

  return channels;
}

/** Per-key secondary motion: torso, hood, sleeves, hem and vertical bob. */
function secondaryPoses() {
  const keys = [0.0, 0.15, 0.3, 0.45, 0.6, 0.75, 0.9, 1.05, 1.2];
  const sample = (fn) => keys.map(t => ({ t, v: fn(t) }));

  return {
    root: {
      position: sample(t => [0, -0.55 + Math.cos((t / 1.2) * Math.PI * 4) * 0.55, 0]),
    },
    spine: {
      rotation: sample(t => [10 + Math.sin((t / 1.2) * Math.PI * 2) * 0.7, Math.sin((t / 1.2) * Math.PI * 2 + 0.6) * 3.2, Math.sin((t / 1.2) * Math.PI * 2) * 1.4]),
      position: sample(t => [0, -0.35 + Math.cos((t / 1.2) * Math.PI * 4) * 0.35, 0]),
    },
    chest: {
      rotation: sample(t => [8.5 + Math.cos((t / 1.2) * Math.PI * 2) * 1.1, Math.sin((t / 1.2) * Math.PI * 2 + 0.4) * -3.6, Math.sin((t / 1.2) * Math.PI * 2) * -1.6]),
    },
    head: {
      rotation: sample(t => [23 + Math.cos((t / 1.2) * Math.PI * 4) * 2.6, Math.sin((t / 1.2) * Math.PI * 2 + 0.3) * -2.2, Math.sin((t / 1.2) * Math.PI * 4) * 1.5]),
    },
    sleeve_l: {
      rotation: sample(t => [Math.sin((t / 1.2) * Math.PI * 2 + 0.5) * 5.5, 0, Math.cos((t / 1.2) * Math.PI * 2) * 3.5]),
    },
    sleeve_r: {
      rotation: sample(t => [Math.sin((t / 1.2) * Math.PI * 2 + 3.6) * 5.5, 0, Math.cos((t / 1.2) * Math.PI * 2 + 3.1) * 3.5]),
    },
    hem_front: {
      rotation: sample(t => [Math.sin((t / 1.2) * Math.PI * 2 + 0.2) * 6.5, 0, Math.sin((t / 1.2) * Math.PI * 2) * 2.5]),
    },
    hem_back: {
      rotation: sample(t => [Math.sin((t / 1.2) * Math.PI * 2 + 3.3) * 5.0, 0, Math.cos((t / 1.2) * Math.PI * 2) * 2.0]),
    },
    skirt: {
      rotation: sample(t => [Math.sin((t / 1.2) * Math.PI * 2) * 1.2, Math.sin((t / 1.2) * Math.PI * 2 + 1.2) * 2.4, 0]),
      position: sample(t => [0, -0.2 + Math.cos((t / 1.2) * Math.PI * 4) * 0.2, 0]),
    },
  };
}

function round(v) { return Math.round(v * 1000) / 1000; }

/** Full walk animation description consumed by the .bbmodel writer. */
function walkAnimation() {
  const channels = Object.assign(walkPoses(), secondaryPoses());
  return {
    name: 'walk',
    loop: 'loop',
    length: LENGTH,
    snapping: 20,
    channels,
    duration: LENGTH,
    keyInterval: T,
  };
}

module.exports = { walkAnimation, LENGTH, T };
