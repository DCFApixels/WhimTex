import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const directory = path.join(root, 'Tests~/Fixtures/Compatibility0125');
const manifest = JSON.parse(fs.readFileSync(path.join(directory, 'manifest.json'), 'utf8'));
assert.equal(manifest.packageVersion, '0.12.5');
assert.equal(manifest.commit, 'a72cc9544d39f93555ca6e9f39c137340e3028f0');
assert.equal(manifest.documents.length, 8); // Original capture, including archived unsupported .asset.
assert.equal(manifest.documents.filter(entry => path.extname(entry.file) !== '.asset').length, 7);
assert.equal(manifest.sha256.length, 30);
const expectedFiles = new Set(manifest.sha256.map(entry => entry.file));
assert.equal(expectedFiles.size, manifest.sha256.length);
for (const entry of manifest.sha256) {
  assert.equal(path.basename(entry.file), entry.file, 'Fixture paths must stay in their directory');
  const bytes = fs.readFileSync(path.join(directory, entry.file));
  assert.equal(createHash('sha256').update(bytes).digest('hex'), entry.sha256, entry.file);
}
const renders = new Set();
for (const entry of manifest.documents) {
  assert(expectedFiles.has(entry.file) && expectedFiles.has(entry.render));
  assert(entry.layers.every(layer => layer.id && layer.type));
  assert.equal(entry.layers.at(-1).name, 'Background', `${entry.file}: background must not cover test content`);
  const bytes = fs.readFileSync(path.join(directory, entry.render));
  assert.equal(bytes.length, entry.width * entry.height * 16);
  let nonuniform = false;
  for (let i = 0; i < bytes.length; i += 16) {
    for (let c = 0; c < 4; c++) assert(Number.isFinite(bytes.readFloatLE(i + c * 4)));
    for (let c = 0; c < 3; c++) if (Math.abs(bytes.readFloatLE(i + c * 4) - bytes.readFloatLE(c * 4)) > .01) nonuniform = true;
  }
  assert(nonuniform, `${entry.file}: uniform renders do not exercise the composition`);
  renders.add(createHash('sha256').update(bytes).digest('hex'));
}
assert.equal(renders.size, 3, 'Procedural, SDR and HDR fixtures need distinct rendered images');
const compositor = fs.readFileSync(path.join(root, 'src/TextureCompositor.cs'), 'utf8');
assert.doesNotMatch(compositor, /\[CreateAssetMenu\(/);
assert(!fs.existsSync(path.join(root, 'src/Editor/TextureCompositorProjectPreview.cs')));
console.log('PASS: 30 frozen 0.12.5 file hashes, seven supported TIFF/JSON documents, archived negative .asset and retired creation/icon paths.');
