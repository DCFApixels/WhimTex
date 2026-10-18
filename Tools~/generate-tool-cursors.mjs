// Requires sharp. An optional first argument selects the preview output folder.
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
import assert from 'node:assert/strict';

const sharp = createRequire(import.meta.url)('sharp');
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const output = path.resolve(process.argv[2] || path.join(root, '../../Temp/WhimTex/IconPreviews'));
const icons = path.join(root, 'src/Editor/Icons');
await fs.mkdir(output, { recursive: true });

const arrow = '<path fill="white" stroke="black" stroke-width="1" stroke-linejoin="miter" stroke-miterlimit="2" d="M .5 .5 H 7.5 L 3.5 3.5 .5 7.5 Z"/>';
const directPointers = new Set(['VectorSelect', 'VectorNodes']);
const svg = (glyph, name) => `<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">${glyph}${directPointers.has(name) ? '' : arrow}</svg>`;
const line = (d, width = 2, extra = '') =>
  `<path d="${d}" fill="none" stroke="black" stroke-width="${width + 2}" stroke-linecap="round" stroke-linejoin="round" ${extra}/>` +
  `<path d="${d}" fill="none" stroke="white" stroke-width="${width}" stroke-linecap="round" stroke-linejoin="round" ${extra}/>`;
const filled = d => `<path d="${d}" fill="black" stroke="black" stroke-width="2" stroke-linejoin="round"/><path d="${d}" fill="white"/>`;
const flatFilled = d => `<path d="${d}" fill="black" fill-rule="evenodd" stroke="black" stroke-width="2" stroke-linejoin="miter" stroke-miterlimit="2"/><path d="${d}" fill="white" fill-rule="evenodd"/>`;
const dot = (x, y, r = 2) => `<circle cx="${x}" cy="${y}" r="${r + 1}" fill="black"/><circle cx="${x}" cy="${y}" r="${r}" fill="white"/>`;
const local = (markup, offset = 5) => `<g transform="translate(${offset} ${offset})">${markup}</g>`;
const polygon = (count, star = false) => Array.from({ length: count }, (_, i) => {
  const angle = -Math.PI / 2 + i * Math.PI * 2 / count;
  const radius = star && i % 2 ? 4.05 : 9;
  return `${i ? 'L' : 'M'}${(12 + Math.cos(angle) * radius).toFixed(4)} ${(12 + Math.sin(angle) * radius).toFixed(4)}`;
}).join(' ') + ' Z';

const source = await fs.readFile(path.join(icons, 'Fill.svg'), 'utf8');
const bucket = source.replace(/^[\s\S]*?<title>Fill<\/title>/, '').replace(/<\/svg>\s*$/, '')
  .replace(/13\.2\b/g, '13').replace('stroke-width="1.8"', 'stroke-width="2"');
const bucketOutline = bucket.replace(/#ffffff/g, '#000000').replace(/<path\b[^>]*>/g, p =>
  p.includes('stroke-width=') ? p.replace('stroke-width="2"', 'stroke-width="4"') :
    p.replace('<path', '<path stroke="#000000" stroke-width="2" stroke-linejoin="round"'));
const gradient = '<defs><linearGradient id="gradient"><stop stop-color="#f2f2f2"/><stop offset="1" stop-color="#1a1a1a"/></linearGradient></defs>' +
  '<rect x="6" y="6" width="20" height="16" fill="black"/><rect x="7" y="7" width="18" height="14" fill="white"/><rect x="8" y="8" width="16" height="12" fill="url(#gradient)"/>';
const circle = 'M21 12 A9 9 0 1 1 3 12 A9 9 0 1 1 21 12 Z';
const arc = 'M16.5 4.2058 A9 9 0 1 1 3.5428 8.9218';
const lassoTail = 'M15 16 C16 18 19 18 17 21';
const bezier = 'M3 19 C4 3 19 22 21 5';
const selectionRectangle = 'M2 6 V2 H6 V4 H4 V6 Z M9 2 H15 V4 H9 Z ' +
  'M18 2 H22 V6 H20 V4 H18 Z M20 9 H22 V15 H20 Z ' +
  'M20 18 H22 V22 H18 V20 H20 Z M9 20 H15 V22 H9 Z ' +
  'M2 18 H4 V20 H6 V22 H2 Z M2 9 H4 V15 H2 Z';
const polar = (radius, degrees) => {
  const angle = degrees * Math.PI / 180;
  return `${(12 + Math.cos(angle) * radius).toFixed(4)} ${(12 + Math.sin(angle) * radius).toFixed(4)}`;
};
const selectionCircle = Array.from({ length: 8 }, (_, i) => {
  const start = i * 45 - 13, end = i * 45 + 13;
  return `M${polar(10, start)} A10 10 0 0 1 ${polar(10, end)} L${polar(8, end)} A8 8 0 0 0 ${polar(8, start)} Z`;
}).join(' ');
const pointer = 'M0 0 L13 10 7 11 4 18 Z';
const glyphs = new Map([
  ['Fill', local(bucketOutline + bucket)],
  ['Gradient', gradient],
  ['ShapeRectangle', local(line('M4 4 H20 V20 H4 Z'))],
  ['ShapeEllipse', local(line(circle))],
  ['ShapePolygon', local(line(polygon(5)))],
  ['ShapeStar', local(line(polygon(10, true)))],
  ['ShapeLine', local(line('M4 20 L20 4'))],
  ['ShapeArc', local(line(arc))],
  ['ShapeSector', local(line(arc + ' L12 12 Z'))],
  ['SelectionRectangle', local(flatFilled(selectionRectangle))],
  ['SelectionEllipse', local(flatFilled(selectionCircle))],
  ['SelectionUv', local(line('M3 5 L11 3 14 9 9 15 3 12 Z M17 12 L22 14 20 21 13 20 12 17 Z') + dot(8, 9, 1))],
  ['LassoPolygon', local(line('M15 16 L5 13.5 8.7 10.3 2.5 7.1 11.2 2.7 21.3 6.8 Z ' + lassoTail))],
  ['LassoFreehand', local(line('M15 16 C12 17 5 15 4 11 C2 5 8 2 13 3 C19 2 23 6 20 11 C19 14 17 15 15 16 ' + lassoTail))],
  ['Comment', local(filled('M20 5 V16 H11 L5 21 V5 Z') +
    '<path d="M8 9.5 H17 M8 12.5 H15" fill="none" stroke="black" stroke-width="1"/>', 2)],
  ['Text', local(filled('M4 3 H20 V7 H18 V5 H13 V19 H16 V21 H8 V19 H11 V5 H6 V7 H4 Z'))],
  ['VectorPen', local(line(bezier) + dot(3, 19) + dot(21, 5))],
  ['VectorSelect', flatFilled(pointer)],
  ['VectorNodes', flatFilled(pointer + ' M3.2 5.2 L8.5 9.3 5.4 9.8 4.6 12.1 Z')],
  ['Crop', local(filled('M5 2 H8 V16 H22 V19 H5 Z M2 5 H19 V22 H16 V8 H2 Z'))],
  ['CanvasScale', local(line('M6 6 H16 V16 H6 Z M16 6 L21 1 M16 1 H21 V6 M6 16 L1 21 M1 16 V21 H6'))]
]);

const cursorCode = await fs.readFile(path.join(root, 'src/WhimTexWindow.Cursors.cs'), 'utf8');
const enumNames = cursorCode.match(/private enum CanvasCursorIcon\s*\{([^}]+)\}/)[1]
  .split(',').map(name => name.trim()).filter(name => name && name !== 'None');
assert.deepEqual(new Set(enumNames), new Set(glyphs.keys()), 'Cursor enum and vector sources must match');
const guids = new Set();
for (const name of glyphs.keys()) {
  const meta = await fs.readFile(path.join(icons, `${name}Cursor.png.meta`), 'utf8');
  for (const setting of ['textureType: 7', 'isReadable: 1', 'enableMipMap: 0', 'textureCompression: 0', 'filterMode: 0'])
    assert(meta.includes(setting), `${name}: ${setting}`);
  const guid = meta.match(/^guid: ([a-f0-9]{32})$/m)[1];
  assert(!guids.has(guid), `${name}: duplicate asset GUID`);
  guids.add(guid);
}

const arrowPixels = await sharp(Buffer.from(svg(''))).ensureAlpha().raw().toBuffer();
const images = new Map(), reports = [];
for (const [name, glyph] of glyphs) {
  const sourceSvg = svg(glyph, name);
  const { data, info } = await sharp(Buffer.from(sourceSvg)).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  assert.equal(info.width, 32);
  assert.equal(info.height, 32);
  if (directPointers.has(name)) assert.equal(data[3], 255, `${name}: visible pointer tip`);
  else assert.deepEqual([...data.subarray(0, 4)], [0, 0, 0, 255], `${name}: hotspot`);
  if (!directPointers.has(name))
    for (let i = 0; i < data.length; i += 4)
      if (arrowPixels[i + 3]) assert(data.subarray(i, i + 4).equals(arrowPixels.subarray(i, i + 4)), `${name}: overlapping arrow`);
  for (let i = 0; i < 32; i++) {
    assert.equal(data[(31 * 32 + i) * 4 + 3], 0, `${name}: bottom clipping`);
    assert.equal(data[(i * 32 + 31) * 4 + 3], 0, `${name}: right clipping`);
  }
  let isolatedPixels = 0;
  for (let y = 0; y < 32; y++) for (let x = 0; x < 32; x++) {
    if (!data[(y * 32 + x) * 4 + 3]) continue;
    let adjacent = false;
    for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
      if (!dx && !dy || x + dx < 0 || x + dx >= 32 || y + dy < 0 || y + dy >= 32) continue;
      adjacent ||= data[((y + dy) * 32 + x + dx) * 4 + 3] > 0;
    }
    if (!adjacent) isolatedPixels++;
  }
  assert.equal(isolatedPixels, 0, `${name}: isolated pixels`);
  if (name === 'Fill' || name === 'Gradient') {
    const prior = await sharp(path.join(icons, `${name}Cursor.png`)).ensureAlpha().raw().toBuffer();
    assert(data.equals(prior), `${name}: existing approved cursor must not change`);
  }
  const png = await sharp(data, { raw: info }).png().toBuffer();
  images.set(name, png);
  reports.push({ name, width: 32, height: 32, hotspot: [0, 0], isolatedPixels });
}
const changed = [];
for (const [name, png] of images) {
  const target = path.join(icons, `${name}Cursor.png`);
  const prior = await fs.readFile(target).catch(error => { if (error.code !== 'ENOENT') throw error; return null; });
  if (prior?.equals(png)) continue;
  await fs.writeFile(target, png);
  changed.push(name);
}
for (const [name, glyph] of glyphs) await fs.writeFile(path.join(output, `${name}Cursor.svg`), svg(glyph, name));

async function sheet(names, filename, columns = 4, cellHeight = 208) {
  const width = columns * 208, height = Math.ceil(names.length / columns) * cellHeight;
  let markup = `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}">`;
  for (let i = 0; i < names.length; i++) {
    const name = names[i], png = images.get(name), x = i % columns * 208, y = Math.floor(i / columns) * cellHeight;
    const enlarged = await sharp(png).resize(128, 128, { kernel: 'nearest' }).png().toBuffer();
    markup += `<rect x="${x}" y="${y}" width="208" height="${cellHeight}" fill="#252526"/><rect x="${x}" y="${y + 24}" width="104" height="42" fill="#ededed"/><text x="${x + 8}" y="${y + 17}" font-family="sans-serif" font-size="11" fill="white">${name}</text>`;
    for (const left of [x + 16, x + 120]) {
      markup += `<image x="${left}" y="${y + 29}" width="32" height="32" href="data:image/png;base64,${png.toString('base64')}"/>`;
    }
    markup += `<image x="${x + 40}" y="${y + 74}" width="128" height="128" href="data:image/png;base64,${enlarged.toString('base64')}"/>`;
  }
  await sharp(Buffer.from(markup + '</svg>')).png().toFile(path.join(output, filename));
}
await sheet([...glyphs.keys()], 'tool-cursors-all-preview.png');
await sheet(['ShapeRectangle', 'SelectionRectangle', 'SelectionEllipse', 'LassoPolygon', 'LassoFreehand', 'SelectionUv', 'Comment', 'VectorPen'], 'tool-cursors-preview.png');
await sheet(['Comment'], 'comment-cursor-close-preview.png', 1);
await sheet(['SelectionRectangle', 'SelectionEllipse', 'VectorSelect', 'VectorNodes'], 'selection-pointer-cursors-preview.png');
await fs.writeFile(path.join(output, 'tool-cursors-report.json'), JSON.stringify(reports, null, 2));
console.log(JSON.stringify({ count: reports.length, changed, isolatedPixels: 0, approvedFillAndGradientUnchanged: true, preview: path.join(output, 'tool-cursors-all-preview.png') }));
