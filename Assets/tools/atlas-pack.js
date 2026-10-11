// Pack face rectangles into a texture atlas. Rectangles are kept at their true
// pixel size so UVs never distort, grouped into rows to stay compact.
//
// Usage: const {packAtlas} = require('./atlas-pack.js')
//   packAtlas([{name, w, h}], 64, 128) -> {rects: {name: [x,y,w,h]}, width, height}

function packAtlas(entries, width, height, padding = 1) {
  // round every rectangle up to whole pixels so UVs land on texel boundaries
  const sorted = entries.map(e => ({ ...e, w: Math.ceil(e.w), h: Math.ceil(e.h) }))
    .sort((a, b) => (b.h - a.h) || (b.w - a.w) || a.name.localeCompare(b.name));
  const rects = {};
  let x = 0;
  let y = 0;
  let rowHeight = 0;
  let worstSlack = 0;

  for (const entry of sorted) {
    if (entry.w > width || entry.h > height) {
      throw new Error(`atlas entry ${entry.name} (${entry.w}x${entry.h}) does not fit in ${width}x${height}`);
    }
    if (x + entry.w > width) {
      // start a new row
      worstSlack = Math.max(worstSlack, width - x);
      x = 0;
      y += rowHeight + padding;
      rowHeight = 0;
    }
    if (y + entry.h > height) {
      throw new Error(`atlas overflow placing ${entry.name} at y=${y} (need ${entry.h})`);
    }
    rects[entry.name] = [x, y, entry.w, entry.h];
    x += entry.w + padding;
    rowHeight = Math.max(rowHeight, entry.h);
  }
  return { rects, width, height, slackInLastRow: width - x, worstSlack };
}

module.exports = { packAtlas };
