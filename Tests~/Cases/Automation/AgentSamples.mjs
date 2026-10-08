// Independent read-only source/scalar replacement. Does not execute Unity, C#, or GPU code.
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const context = new TestContext('AgentSamples: source/scalar contracts');
const assert = context.assert;

context.case('AgentSamples original assertions and branches', async () => {
    const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../Samples~/AgentTextures');
    const manifest = JSON.parse(fs.readFileSync(path.join(root, 'manifest.json')));
    assert.equal(manifest.format, 'whimtex.agent-samples');
    assert.equal(manifest.version, 2);
    assert.equal(manifest.samples.length, 38);
    assert.match(manifest.previewEncoding, /per-sample outputSrgb/);
    assert.equal(new Set(manifest.samples.map(e => e.id)).size, manifest.samples.length);
    assert.deepEqual(manifest.samples.map(e => e.id), manifest.samples.map(e => e.id).sort());
    const expectedFiles = new Set(['README.md', 'manifest.json']);
    function* walk(layers) { for (const layer of layers) { yield layer; yield* walk(layer.children ?? []); } }
    for (const entry of manifest.samples) {
      assert.equal(typeof entry.outputSrgb, 'boolean');
      assert.equal(entry.outputSrgb, !['Gas_Particle', 'Ring_Distortion', 'Sphere_Distortion'].includes(entry.id));
      assert.match(entry.id, /^[A-Za-z][A-Za-z0-9_]*$/);
      assert.ok(entry.title && entry.category && entry.description && entry.tags.length > 0);
      assert.ok(entry.tags.every(tag => typeof tag === 'string' && tag.length > 0));
      assert.ok(!Object.hasOwn(entry, 'document'));
      assert.equal(entry.recipe, entry.id + '.whimtex.json');
      assert.equal(entry.preview, entry.id + '.png');
      for (const key of ['recipe', 'preview']) {
        assert.equal(path.basename(entry[key]), entry[key]);
        assert.ok(fs.existsSync(path.join(root, entry[key])), entry[key]);
        expectedFiles.add(entry[key]);
      }
      const text = fs.readFileSync(path.join(root, entry.recipe), 'utf8');
      assert.doesNotMatch(text, /Assets\/|Test6\.|[A-Z]:\\|contentOmitted|#include|https?:\/\//);
      const recipe = JSON.parse(text);
      assert.equal(recipe.format, 'whimtex.document');
      if (entry.id === 'Sphere_Distortion') {
        assert.doesNotMatch(text, /GammaToLinearSpace\(encoded\)/, 'Vector field must preserve neutral 0.5 in linear data');
      }
      const canvas = {width: recipe.document.width, height: recipe.document.height, filter: recipe.document.outputFilter};
      assert.deepEqual(canvas, entry.canvas);
      assert.equal(recipe.document.outputSrgb, entry.outputSrgb);
      assert.equal(recipe.kind, undefined);
      assert.equal(Math.max(canvas.width, canvas.height), 256);
      assert.ok(canvas.width > 0 && canvas.height > 0);
      assert.ok(['Point', 'Bilinear', 'Trilinear'].includes(canvas.filter));
      const layers = [...walk(recipe.layers)], namedIds = layers.filter(l => l.id).map(l => l.id), ids = new Set(namedIds);
      assert.equal(layers.length, entry.layers);
      assert.equal(ids.size, namedIds.length);
      for (const l of layers) {
        assert.ok(l.layerName.trim() && !/^Shader Processor \d|^New Layer/.test(l.layerName));
        assert.ok(!['DrawingLayerBehaviour', 'FileLayerBehaviour'].includes(l.behaviour.$type));
        assert.ok(!l.asset && !l.url);
        if (l.behaviour.inputMode === 'Specific') assert.ok(ids.has(l.behaviour.targetLayerId), entry.id + ': missing target');
        for (const fx of l.fx ?? []) {
          if (fx.$ref) continue;
          assert.equal(fx.$type, 'ShaderFX');
          assert.ok(fx.code.trim());
          for (const p of fx.parameters ?? [])
            if (p.textureSource === 'Layer') assert.ok(ids.has(p.textureLayerId));
        }
      }
      const png = fs.readFileSync(path.join(root, entry.preview));
      assert.deepEqual(png.subarray(0, 8), Buffer.from([137,80,78,71,13,10,26,10]));
      assert.equal(png.readUInt32BE(16), canvas.width); assert.equal(png.readUInt32BE(20), canvas.height);
    }
    assert.deepEqual(fs.readdirSync(root).sort(), [...expectedFiles].sort(), 'No stale TIFFs, old recipes, atlas or unindexed files');
    const readme = fs.readFileSync(path.join(root, 'README.md'), 'utf8');
    for (const match of readme.matchAll(/\]\(([^)]+)\)/g))
      assert.ok(fs.existsSync(path.resolve(root, match[1])), 'Broken README link: ' + match[1]);
});

await finish(context);

