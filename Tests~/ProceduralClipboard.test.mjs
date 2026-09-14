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
  if (rule.$ref) return matches(schema.$defs[rule.$ref.split('/').pop()], value);
  if (rule.oneOf) return rule.oneOf.filter(s => matches(s, value)).length === 1;
  if ('const' in rule && value !== rule.const) return false;
  if (rule.enum && !rule.enum.includes(value)) return false;
  if (rule.type === 'object') return value !== null && !Array.isArray(value) && typeof value === 'object' &&
    (rule.required ?? []).every(k => k in value) && Object.entries(value).every(([k, v]) => k in rule.properties && matches(rule.properties[k], v));
  if (rule.type === 'array') return Array.isArray(value) && value.length >= (rule.minItems ?? 0) && value.length <= (rule.maxItems ?? Infinity) &&
    value.every((v, i) => matches(rule.prefixItems?.[i] ?? rule.items, v));
  if (rule.type === 'string') return typeof value === 'string' && value.length >= (rule.minLength ?? 0) && value.length <= (rule.maxLength ?? Infinity);
  if (rule.type === 'boolean') return typeof value === 'boolean';
  if (rule.type === 'number' || rule.type === 'integer') return typeof value === 'number' && Number.isFinite(value) &&
    (rule.type !== 'integer' || Number.isInteger(value)) && value >= rule.minimum && value <= rule.maximum;
  return true;
}
const directory = path.join(root, 'Documentation~/Examples/Clipboard');
for (const file of fs.readdirSync(directory).filter(f => f.endsWith('.json')))
  assert.ok(matches(schema, JSON.parse(fs.readFileSync(path.join(directory, file), 'utf8'))), file + ' does not match the schema');
const guide = read('Documentation~/AI/README.md');
for (const match of guide.matchAll(/```json\s*\n([\s\S]*?)\n```/g))
  assert.ok(matches(schema, JSON.parse(match[1])), 'Guide JSON does not match schema');
for (const [name, file] of Object.entries({ noise: 'Noise', shape: 'Shape', blur: 'Blur', normalMap: 'NormalMap', makeSeamless: 'MakeSeamless' })) {
  const source = read(`src/Automation/WhimTexApi.${file}.cs`);
  const declared = [...source.match(/Keys\(value,([\s\S]*?)\);/)[1].matchAll(/"([^"]+)"/g)].map(m => m[1]).sort();
  assert.deepEqual(Object.keys(schema.$defs[name].properties).sort(), declared, `${name} keys differ from implementation`);
}
const paste = read('src/TextureCompositorWindow.AreaSelection.cs');
assert.ok(paste.indexOf('IsProceduralClipboard(clipboardText)') < paste.indexOf('TextureCompositor copiedLayers = LayerClipboard.Current'));
assert.match(paste, /IsTextInputTarget\(target\)/);
assert.match(paste, /resize && HasPreviewLayers/);
assert.match(paste, /generated\.Compile\(\);\s*PasteCopiedLayers\(generated\.Document, resize\)/);
for (const name of ['README.md', 'README-RU.md']) {
  assert.match(read(name), /^<!--[\s\S]*?AI_AUTHORING\.md[\s\S]*?-->/);
  assert.match(read(name).replace(/<!--[\s\S]*?-->/g, ''), /\]\(AI_AUTHORING\.md\)/);
}
assert.equal(matches(schema, { format: 'whimtex.layers', version: 1, layers: [{ type: 'file' }] }), false);
assert.equal(matches(schema, { format: 'whimtex.layers', version: 1, layers: [{ type: 'noise', properties: { noise: { scale: '3' } } }] }), false);
console.log('Procedural clipboard: schema, documentation examples, shared property keys and paste routing passed.');
