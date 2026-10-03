// Rasterize the approved SVG only; does not invoke Unity or change the logo geometry.
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const require = createRequire(import.meta.url);
const sharp = require(process.argv[2] || 'sharp');
const docs = fileURLToPath(new URL('../', import.meta.url));
const svg = path.join(docs, 'Images/whimtex-logo.svg');
const cropped = await sharp(svg, { density: 288 }).trim().png().toBuffer();
const outputs = [
  ['Images/whimtex-logo.png', 512, 16],
  ['Images/favicon-32.png', 32, 1],
  ['Images/apple-touch-icon.png', 180, 8],
  ['../src/WhimTexIcon.png', 64, 2]
];
for (const [file, size, padding] of outputs) {
  await sharp(cropped)
    .resize(size - padding * 2, size - padding * 2, { fit: 'contain', background: '#00000000' })
    .extend({ top: padding, bottom: padding, left: padding, right: padding, background: '#00000000' })
    .png()
    .toFile(path.join(docs, file));
  console.log(`${file}: ${size} × ${size}, transparent PNG from the approved SVG`);
}

await sharp(path.join(docs, 'Images/whimtex-logo-duotone.svg'), { density: 144 })
  .resize(1024, 1024)
  .png()
  .toFile(path.join(docs, '../src/WhimTexCanvasViewBackdrop.png'));
console.log('../src/WhimTexCanvasViewBackdrop.png: 1024 × 1024, original duotone framing and shadows');
