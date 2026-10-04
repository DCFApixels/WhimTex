import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = file => readFileSync(new URL('../' + file, import.meta.url), 'utf8');
const parser = read('src/Automation/WhimTexApi.MakeSeamless.cs');
const model = read('src/Layers/MakeSeamlessLayerBehaviour.cs');
const inspect = read('src/Automation/WhimTexApi.Inspect.cs');
const properties = JSON.parse(read('Documentation~/AI/agent-fields.schema.json')).$defs.makeSeamless.properties;
const reference = read('Documentation~/AgentAPI.md').split('### Make Seamless settings')[1].split('### Normal Map settings')[0];
const keys = [...parser.match(/Keys\(value,([\s\S]*?)\);/)[1].matchAll(/"([^"]+)"/g)].map(m => m[1]);
const snapshotKeys = [...parser.split('private static JObject MakeSeamlessSnapshot')[1].matchAll(/\["([^"]+)"\] =/g)].map(m => m[1]);
assert.deepEqual(Object.keys(properties).sort(), [...keys].sort());
assert.deepEqual(snapshotKeys.sort(), [...keys].sort());
for (const key of keys) {
  assert.ok(reference.includes('`' + key + '`'), `Missing API field ${key}`);
  assert.ok(Object.hasOwn(properties[key], 'default'), `Missing schema default ${key}`);
}
const enumFields = {
  mode: ['SeamlessMode', 'makeSeamlessModes'],
  horizontal: ['HorizontalDirection', 'makeSeamlessHorizontal'],
  vertical: ['VerticalDirection', 'makeSeamlessVertical'],
  poissonEdges: ['PoissonEdges', 'makeSeamlessPoissonEdges'],
  mirrorPoissonEdges: ['PoissonEdges', 'makeSeamlessPoissonEdges'],
  offsetPoissonEdges: ['PoissonEdges', 'makeSeamlessPoissonEdges'],
  quiltingEdges: ['PoissonEdges', 'makeSeamlessPoissonEdges'],
  quiltingPoissonEdges: ['PoissonEdges', 'makeSeamlessPoissonEdges'],
  quiltingQuality: ['QuiltingQuality', 'makeSeamlessQuiltingQuality'],
  quiltingChannels: ['QuiltingChannels', 'makeSeamlessQuiltingChannels']
};
for (const [field, [type, discovery]] of Object.entries(enumFields)) {
  const body = model.match(new RegExp('enum ' + type + ' \\{([^}]+)\\}'))[1];
  const names = body.replace(/\[[^\]]*\]/g, '').split(',').map(s => s.trim().split(/[\s=]/)[0]);
  assert.deepEqual(properties[field].enum, names, field);
  assert.ok(inspect.includes(`result["${discovery}"] = new JArray(System.Enum.GetNames(typeof(MakeSeamlessLayerBehaviour.${type})))`), discovery);
  assert.ok(reference.includes('`' + discovery + '`'), `Missing discovery ${discovery}`);
}
const scalar = s => s === 'int.MinValue' ? -2147483648 : s === 'int.MaxValue' ? 2147483647 : Number(s.replace(/f$/, ''));
const checkedNumeric = [];
for (const match of parser.matchAll(/(?:SeamlessNumber|Int)\(value,\s*"([^"]+)",\s*layer\.\w+,\s*([^,]+),\s*([^)]+)\)/g)) {
  const [, field, min, max] = match;
  checkedNumeric.push(field);
  assert.equal(properties[field].minimum, scalar(min), field + ' minimum');
  assert.equal(properties[field].maximum, scalar(max), field + ' maximum');
}
assert.deepEqual(checkedNumeric.sort(), Object.keys(properties).filter(k => ['number', 'integer'].includes(properties[k].type)).sort());
assert.equal(properties.quiltingFeather.default, 50);
assert.match(properties.quiltingFeather.description, /not a 0\.\.1 fraction or pixels/);
assert.match(reference, /changing Transition Start does\s+not toggle it/);
assert.doesNotMatch(reference, /inspect.*all four parameters|Both Off bypass the operation/);
for (const lang of ['en', 'ru', 'zh'])
  assert.ok(read(`Documentation~/${lang}/effects.md`).includes('../AgentAPI.md#make-seamless-settings'));
console.log(`Make Seamless contract checked: ${keys.length} fields, schema/parser/snapshot/docs, numeric bounds and ${Object.keys(enumFields).length} enum fields. Runtime defaults are checked by MakeSeamlessContractSmoke.`);
