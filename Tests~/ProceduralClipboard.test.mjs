import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
process.argv.push('--check');
await import('../Documentation~/scripts/build-clipboard-schema.mjs');
process.argv.pop();
const schema = JSON.parse(read('Documentation~/AI/layers.schema.json'));
// Deliberately only the schema vocabulary emitted by our generator, not a general JSON Schema implementation.
function matches(rule, value) {
  if (typeof rule === 'boolean') return rule;
  if (rule.$ref) return matches(schema.$defs[rule.$ref.split('/').pop()], value);
  if (rule.oneOf) return rule.oneOf.filter(s => matches(s, value)).length === 1;
  if ('const' in rule && value !== rule.const) return false;
  if (rule.enum && !rule.enum.includes(value)) return false;
  if (rule.type === 'object') return value !== null && !Array.isArray(value) && typeof value === 'object' &&
    (rule.required ?? []).every(k => k in value) && Object.entries(value).every(([k, v]) =>
      k in (rule.properties ?? {}) ? matches(rule.properties[k], v) : matches(rule.additionalProperties ?? true, v));
  if (rule.type === 'array') return Array.isArray(value) && value.length >= (rule.minItems ?? 0) && value.length <= (rule.maxItems ?? Infinity) &&
    value.every((v, i) => matches(rule.prefixItems?.[i] ?? rule.items, v));
  if (rule.type === 'string') return typeof value === 'string' && value.length >= (rule.minLength ?? 0) &&
    value.length <= (rule.maxLength ?? Infinity) && (!rule.pattern || new RegExp(rule.pattern).test(value));
  if (rule.type === 'boolean') return typeof value === 'boolean';
  if (rule.type === 'number' || rule.type === 'integer') return typeof value === 'number' && Number.isFinite(value) &&
    (rule.type !== 'integer' || Number.isInteger(value)) && value >= rule.minimum && value <= rule.maximum;
  return true;
}
const directory = path.join(root, 'Documentation~/Examples/Clipboard');
for (const file of fs.readdirSync(directory).filter(f => f.endsWith('.json')))
  assert.ok(matches(schema, JSON.parse(fs.readFileSync(path.join(directory, file), 'utf8'))), file + ' does not match the schema');
const samples = path.join(root, 'Samples~/AgentTextures');
for (const file of fs.readdirSync(samples).filter(f => f.endsWith('.layers.json')))
  assert.ok(matches(schema, JSON.parse(fs.readFileSync(path.join(samples, file), 'utf8'))), file + ' does not match the schema');
assert.equal(matches(schema.$defs.fx, {code:'float4 ApplyFX(float2 uv, float4 color) { return color; }', textures:{_Map:{layer:'noise'}}}), true);
assert.equal(matches(schema.$defs.fx, {code:'x', textures:{_Map:{asset:'external'}}}), false);
assert.equal(matches(schema.$defs.fx, {code:'x', gradients:{_Tint:'invalid'}}), false);
const guide = read('Documentation~/AI/README.md');
for (const filter of ['Point', 'Bilinear', 'Trilinear']) {
  assert.ok(matches(schema, { format: 'whimtex.layers', version: 1,
    canvas: { width: 64, height: 96, filter }, layers: [{ type: 'color' }] }));
}
for (const filter of ['Source', 'point', 0, null]) {
  assert.equal(matches(schema, { format: 'whimtex.layers', version: 1,
    canvas: { width: 64, height: 96, filter }, layers: [{ type: 'color' }] }), false);
}
assert.match(read('src/Automation/WhimTexApi.Clipboard.cs'), /result.CanvasFilter = Enum\(canvas, "filter", FilterMode.Bilinear\)/);
assert.match(read('src/TextureCompositorWindow.ImageUrl.cs'), /clipboardPasteResize, data.CanvasFilter/);
for (const match of guide.matchAll(/```json\s*\n([\s\S]*?)\n```/g)) {
  const example = JSON.parse(match[1]);
  if (example.format === 'whimtex.gradient') continue; // Validated by GradientPresetsSmoke in Unity.
  assert.ok(matches(schema, example), 'Guide JSON does not match schema');
}
for (const [name, file] of Object.entries({ noise: 'Noise', shape: 'Shape', blur: 'Blur', normalMap: 'NormalMap', makeSeamless: 'MakeSeamless', fillPattern: 'FillPattern' })) {
  const source = read(`src/Automation/WhimTexApi.${file}.cs`);
  const declared = [...source.match(/Keys\(value,([\s\S]*?)\);/)[1].matchAll(/"([^"]+)"/g)].map(m => m[1]).sort();
  assert.deepEqual(Object.keys(schema.$defs[name].properties).sort(), declared, `${name} keys differ from implementation`);
}
for (const size of [64, [32, 64]])
  assert.ok(matches(schema.$defs.fillPattern, { size, cellColor: 'Random', colorBlend: 'ReplaceRGB', seed: -2147483648, variation: .7 }));
for (const bad of [{ size: [0, 32] }, { variation: 2 }, { cellColor: 'Unknown' }, { seed: 1.5 }])
  assert.equal(matches(schema.$defs.fillPattern, bad), false);
const paste = read('src/TextureCompositorWindow.AreaSelection.cs');
assert.ok(paste.indexOf('IsProceduralClipboard(clipboardText)') < paste.indexOf('TextureCompositor copiedLayers = LayerClipboard.Current'));
assert.match(paste, /IsTextInputTarget\(target\)/);
assert.match(paste, /resize && HasPreviewLayers/);
assert.ok(paste.indexOf('generated.Compile()') < paste.indexOf('PasteProceduralClipboard(generated, resize)'),
  'Effects compile before the tree is handed to the paste');
assert.match(paste, /if \(!handedOver\) generated\.Dispose\(\)/, 'A refused paste releases its temporary document');
const linked = read('src/TextureCompositorWindow.ImageUrl.cs');
assert.match(linked, /TryGetOriginalAspectTransform\(/, 'A downloaded image is fitted, not resampled to the canvas');
assert.ok(!linked.includes('placement.scale'), 'The hand-rolled fit math is gone');
assert.match(linked, /ImageUrlMaximumBytes = 64 \* 1024 \* 1024/);
assert.match(linked, /jsonPasteData|clipboardPasteData/, 'Linked images are pasted by the download batch');
const parser = read('src/Automation/WhimTexApi.Clipboard.cs');
assert.match(parser, /url is only supported on Drawing layers/);
assert.match(linked, /if \(fit && layer.TryGetOriginalAspectTransform/);
assert.match(parser, /link\.Scheme == "http" \|\| link\.Scheme == "https"/);
assert.equal(matches(schema, { format: 'whimtex.layers', version: 1, layers: [{ type: 'drawing', url: 'https://example.com/a.png' }] }), true);
assert.equal(matches(schema, { format: 'whimtex.layers', version: 1, layers: [{ type: 'drawing', url: 'ftp://example.com/a.png' }] }), false);
assert.equal(matches(schema, { format: 'whimtex.layers', version: 1, layers: [{ type: 'drawing', url: 'https://example.com/a.png', transform: { scale: [2, 2] } }] }), true,
  'An explicit scale preserves portable linked-layer placement');
for (const name of ['README.md', 'README-RU.md']) {
  assert.match(read(name), /^<!--[\s\S]*?AI_AUTHORING\.md[\s\S]*?-->/);
  assert.match(read(name).replace(/<!--[\s\S]*?-->/g, ''), /\]\(AI_AUTHORING\.md\)/);
}
assert.equal(matches(schema, { format: 'whimtex.layers', version: 1, layers: [{ type: 'file' }] }), true);
assert.equal(matches(schema, { format: 'whimtex.layers', version: 1, layers: [{ type: 'noise', properties: { noise: { scale: '3' } } }] }), false);
// A linked Drawing layer must be discoverable from every entry point an AI reads first.
for (const file of ['README.md', 'README-RU.md', 'AI_AUTHORING.md', 'AGENTS.md'])
  assert.match(read(file), /url/, file + ' must name the linked Drawing layer (url)');
assert.match(guide, /Drawing layer that has a `url`/, 'The authoring guide must state the linked Drawing layer');
for (const [name, text] of [['AI/README.md', guide],
                            ['en/ai-authoring.md', read('Documentation~/en/ai-authoring.md')],
                            ['ru/ai-authoring.md', read('Documentation~/ru/ai-authoring.md')]]) {
  for (const blocked of ['no external assets', 'self-contained JSON', 'cannot supply texture assets', 'only procedural layers'])
    assert.ok(!text.toLowerCase().includes(blocked), name + ' still tells an AI that images are impossible: ' + blocked);
}
for (const file of ['Documentation~/en/automation.md', 'Documentation~/ru/automation.md'])
  for (const blocked of ['additionally insert images', 'дополнительно умеет вставлять изображения'])
    assert.ok(!read(file).toLowerCase().includes(blocked), file + ' must not reserve image insertion for the connected agent');
console.log('Procedural clipboard: schema, documentation examples, shared property keys and paste routing passed.');
