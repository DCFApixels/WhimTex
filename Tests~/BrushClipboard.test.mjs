import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
process.argv.push('--check');
await import('../Documentation~/scripts/build-brush-schema.mjs');
process.argv.pop();
const schema = JSON.parse(read('Documentation~/AI/brush.schema.json'));
// Only the vocabulary emitted by the generator; not a general JSON Schema validator.
function matches(rule, value) {
  if (rule.$ref) return matches(schema.$defs[rule.$ref.split('/').pop()], value);
  if (rule.oneOf) return rule.oneOf.filter(s => matches(s, value)).length === 1;
  if ('const' in rule && value !== rule.const) return false;
  if (rule.enum && !rule.enum.includes(value)) return false;
  if (rule.type === 'object') return value !== null && !Array.isArray(value) && typeof value === 'object' &&
    (rule.required ?? []).every(k => k in value) &&
    Object.entries(value).every(([k,v]) => k in rule.properties && matches(rule.properties[k],v));
  if (rule.type === 'array') return Array.isArray(value) && value.length >= (rule.minItems ?? 0) &&
    value.length <= (rule.maxItems ?? Infinity) && value.every((v,i) => matches(rule.prefixItems?.[i] ?? rule.items,v));
  if (rule.type === 'string') return typeof value === 'string' && value.length >= (rule.minLength ?? 0) &&
    value.length <= (rule.maxLength ?? Infinity) && (!rule.pattern || new RegExp(rule.pattern).test(value));
  if (rule.type === 'boolean') return typeof value === 'boolean';
  if (rule.type === 'number' || rule.type === 'integer') return typeof value === 'number' && Number.isFinite(value) &&
    (rule.type !== 'integer' || Number.isInteger(value)) && value >= (rule.minimum ?? -Infinity) && value <= (rule.maximum ?? Infinity);
  return true;
}
for (const file of fs.readdirSync(path.join(root,'Documentation~/Examples/Brushes')).filter(f => f.endsWith('.json')))
  assert.ok(matches(schema,JSON.parse(read('Documentation~/Examples/Brushes/'+file))),file);
const standard={format:'whimtex.brush',version:1,source:'Standard'};
assert.ok(matches(schema,standard));
assert.ok(matches(schema,{...standard,url:'https://example.com/x.png'}));
for (const invalid of [
  {...standard,url:'file:///x.png'}, {...standard,source:'Texture'},
  {...standard,source:'HLSL'}, {...standard,settings:{flow:2}},
  {...standard,settings:{typo:true}}, {...standard,source:'standard'},
]) assert.equal(matches(schema,invalid),false,JSON.stringify(invalid));
const paste=read('src/TextureCompositorWindow.AreaSelection.cs');
assert.match(paste, /catch \(Exception exception\) \{ ReportClipboardPasteError\("Paste failed", exception\); \}/);
assert.match(paste, /Debug\.LogError\("\[WhimTex\] " \+ operation \+ ":\\n" \+ exception, this\)/);
assert.match(read('src/TextureCompositorWindow.BrushClipboard.cs'), /ReportClipboardPasteError\("Brush JSON paste failed", error\)/);
assert.match(read('src/TextureCompositorWindow.ImageUrl.cs'), /ReportClipboardPasteError\("Image paste failed", exception\)/);
assert.ok(paste.indexOf('TryPasteBrushClipboard(clipboardText)') < paste.indexOf('TryPasteImageUrl'));
console.log('Brush clipboard schema and examples passed.');
