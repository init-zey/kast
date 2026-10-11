// Paint the Player model's 64x128 texture. Every face rectangle gets a vertical
// gradient, soft cloth noise, optional vertical folds, and a bottom hem band so
// the robe reads as fabric rather than flat grey.
const { PALETTE, ATLAS, REGIONS, TEXTURE_WIDTH, TEXTURE_HEIGHT } = require('./player-model.js');

function hexToRgb(hex) {
  const n = parseInt(hex.slice(1), 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}
function mix(a, b, t) {
  return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
}
function clamp255(v) { return Math.max(0, Math.min(255, Math.round(v))); }

// deterministic value noise so the texture is reproducible
function hash2(x, y) {
  let h = x * 374761393 + y * 668265263;
  h = (h ^ (h >>> 13)) * 1274126177;
  return ((h ^ (h >>> 16)) >>> 0) / 4294967295;
}
function smoothNoise(x, y) {
  const xi = Math.floor(x), yi = Math.floor(y);
  const xf = x - xi, yf = y - yi;
  const sx = xf * xf * (3 - 2 * xf), sy = yf * yf * (3 - 2 * yf);
  const n00 = hash2(xi, yi), n10 = hash2(xi + 1, yi);
  const n01 = hash2(xi, yi + 1), n11 = hash2(xi + 1, yi + 1);
  return (n00 * (1 - sx) + n10 * sx) * (1 - sy) + (n01 * (1 - sx) + n11 * sx) * sy;
}

function createCanvas() {
  const data = new Uint8ClampedArray(TEXTURE_WIDTH * TEXTURE_HEIGHT * 4);
  return {
    data,
    set(x, y, [r, g, b], a = 255) {
      if (x < 0 || y < 0 || x >= TEXTURE_WIDTH || y >= TEXTURE_HEIGHT) return;
      const i = (y * TEXTURE_WIDTH + x) * 4;
      data[i] = clamp255(r); data[i + 1] = clamp255(g); data[i + 2] = clamp255(b); data[i + 3] = a;
    },
    get(x, y) {
      const i = (y * TEXTURE_WIDTH + x) * 4;
      return [data[i], data[i + 1], data[i + 2], data[i + 3]];
    },
  };
}

function paintRegion(canvas, key, rect) {
  const spec = REGIONS[key];
  if (!spec) throw new Error('no paint recipe for region ' + key);
  const [rx, ry, rw, rh] = rect;
  const base = hexToRgb(PALETTE[spec.base]);
  const dark = mix(base, [0, 0, 0], 0.42);
  const light = mix(base, [255, 255, 255], 0.34);
  const top = spec.top ?? 1.0;
  const bottom = spec.bottom ?? 1.0;

  for (let y = 0; y < rh; y++) {
    const t = rh > 1 ? y / (rh - 1) : 0;
    const grad = top + (bottom - top) * t;
    for (let x = 0; x < rw; x++) {
      let colour = base.map(c => c * grad);

      // soft cloth grain
      if (spec.noise) {
        const n = smoothNoise((rx + x) * 0.55, (ry + y) * 0.55) - 0.5;
        const n2 = smoothNoise((rx + x) * 1.7 + 11, (ry + y) * 1.7 + 7) - 0.5;
        const amt = (n * 0.7 + n2 * 0.3) * spec.noise * 255;
        colour = colour.map(c => c + amt);
      }

      // vertical folds: a few darker columns with a gentle curve
      if (spec.folds) {
        const foldCount = 3;
        for (let f = 0; f < foldCount; f++) {
          const centre = ((f + 0.5) / foldCount) * rw + Math.sin((y / rh) * Math.PI * 1.5 + f * 2.1) * 0.9;
          const dist = Math.abs(x + 0.5 - centre);
          if (dist < 1.35) {
            const strength = (1 - dist / 1.35) * 0.5 * (0.55 + 0.45 * (1 - t));
            colour = mix(colour, dark, strength);
          } else if (dist < 2.6) {
            const strength = (1 - (dist - 1.35) / 1.25) * 0.2;
            colour = mix(colour, light, strength * 0.55);
          }
        }
      }

      // side highlight so the cylinder-like forms get a lit edge
      const edge = Math.min(x, rw - 1 - x) / Math.max(1, (rw - 1) / 2);
      if (spec.highlight) {
        // broad soft highlight across the middle of the region
        const cx = (rw - 1) / 2;
        const dist = Math.abs(x - cx) / Math.max(1, cx);
        const vertical = 1 - Math.abs((y / Math.max(1, rh - 1)) - 0.42) * 0.9;
        colour = mix(colour, light, Math.max(0, (1 - dist) * 0.22 * vertical));
      }
      if (edge < 0.12) colour = mix(colour, dark, 0.35 * (1 - edge / 0.12));

      // shoulder seams / hood seams
      if (spec.seams) {
        if (y === 0 || x === 0 || x === rw - 1) colour = mix(colour, dark, 0.45);
      }

      // bottom hem band
      if (spec.hem && y >= rh - 2) {
        const trim = hexToRgb(PALETTE.hemTrim);
        colour = mix(colour, trim, y === rh - 1 ? 0.4 : 0.18);
      }
      if (spec.trim && y <= 0) {
        const trim = hexToRgb(PALETTE.hemTrim);
        colour = mix(colour, trim, 0.3);
      }

      canvas.set(rx + x, ry + y, colour);
    }
  }

  // 1px dark outline around the region, keeps faces reading as panels
  const outline = mix(dark, [0, 0, 0], 0.35);
  for (let x = 0; x < rw; x++) {
    canvas.set(rx + x, ry, mix(canvas.get(rx + x, ry), outline, 0.35));
    canvas.set(rx + x, ry + rh - 1, mix(canvas.get(rx + x, ry + rh - 1), outline, 0.3));
  }
  for (let y = 0; y < rh; y++) {
    canvas.set(rx, ry + y, mix(canvas.get(rx, ry + y), outline, 0.35));
    canvas.set(rx + rw - 1, ry + y, mix(canvas.get(rx + rw - 1, ry + y), outline, 0.35));
  }
}

/** Paint the whole texture: fill unused space with a dark robe tone, then each region. */
function paintTexture() {
  const canvas = createCanvas();
  const filler = hexToRgb(PALETTE.robeDeep);
  for (let y = 0; y < TEXTURE_HEIGHT; y++) {
    for (let x = 0; x < TEXTURE_WIDTH; x++) {
      const n = smoothNoise(x * 0.5, y * 0.5) - 0.5;
      canvas.set(x, y, filler.map(c => c * (1 + n * 0.12)));
    }
  }
  for (const [key, rect] of Object.entries(ATLAS)) paintRegion(canvas, key, rect);
  return canvas;
}

module.exports = { paintTexture, createCanvas, hexToRgb };
