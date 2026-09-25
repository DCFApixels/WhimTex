import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../Samples~/AgentTextures');
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'manifest.json')));
assert.equal(manifest.format, 'whimtex.agent-samples');
assert.equal(manifest.samples.length, 12);
function* walk(layers) { for (const layer of layers) { yield layer; yield* walk(layer.children ?? []); } }
for (const [index, entry] of manifest.samples.entries()) {
  assert.equal(entry.id, String(index + 1).padStart(2, '0'));
  for (const key of ['document', 'recipe', 'preview']) {
    assert.equal(path.basename(entry[key]), entry[key]);
    assert.ok(entry[key].startsWith(entry.id + '_'));
    assert.ok(fs.existsSync(path.join(root, entry[key])), entry[key]);
  }
  const text = fs.readFileSync(path.join(root, entry.recipe), 'utf8');
  assert.doesNotMatch(text, /Assets\/|Test6\.|[A-Z]:\\|contentOmitted|#include|https?:\/\//);
  const recipe = JSON.parse(text);
  assert.equal(recipe.format, 'whimtex.layers');
  assert.deepEqual(recipe.canvas, {width:256, height:256, filter:'Bilinear'});
  const layers = [...walk(recipe.layers)], ids = new Set(layers.map(l => l.id));
  assert.equal(layers.length, entry.layers);
  assert.equal(ids.size, layers.length);
  for (const l of layers) {
    assert.ok(l.name.trim() && !/^Shader Processor \d|^New Layer/.test(l.name));
    assert.ok(!['drawing', 'file'].includes(l.type));
    assert.ok(!l.asset && !l.url);
    if (l.target) assert.ok(ids.has(l.target), `${entry.id}: ${l.target}`);
    for (const fx of l.fx ?? []) {
      assert.ok(fx.name.trim());
      for (const t of Object.values(fx.textures ?? {})) if (t.layer) assert.ok(ids.has(t.layer));
    }
  }
  const png = fs.readFileSync(path.join(root, entry.preview));
  assert.equal(png.readUInt32BE(16), 256); assert.equal(png.readUInt32BE(20), 256);
}
console.log('PASS: 12 numbered, self-contained agent samples with 256x256 canvases and previews.');
