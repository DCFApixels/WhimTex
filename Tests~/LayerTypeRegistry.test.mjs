// Registry contract; no Unity compilation or runtime execution.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = p => readFileSync(new URL('../src/' + p, import.meta.url), 'utf8');
const registry = read('LayerTypeRegistry.cs');
const entries = [...registry.matchAll(/new Entry\("([^"]+)", "([^"]+)", "([^"]+)", "([^"]+)", typeof\((\w+)\), (\d), \(\) => new (\w+)\(\)\)/g)]
    .map(([,id,menu,prefix,inside,type,section,factory]) => ({id,menu,prefix,inside,type,section:+section,factory}));
assert.deepEqual(entries.map(e=>e.id), ['drawing','file','color','gradient','noise','shape','outline','sdf','normalMap','blur','makeSeamless','shaderProcessor','group']);
assert.equal(new Set(entries.map(e=>e.type)).size, 13);
assert.deepEqual(entries.map(e=>e.section), [0,0,0,0,0,0,1,1,1,1,1,1,2]);
for(const entry of entries) {
    assert.equal(entry.type,entry.factory);
    if(entry.id==='drawing') { assert.equal(entry.menu,'Drawing Layer'); assert.equal(entry.prefix,'Layer'); }
    else assert.equal(entry.menu,entry.prefix);
    assert.match(read(`Layers/${entry.type}.cs`), new RegExp(`class ${entry.type} : `));
}
assert.doesNotMatch(registry,/new Entry\("pending"/);
assert.match(registry,/Array.AsReadOnly/);
assert.match(registry,/StringComparer.Ordinal/);
assert.match(read('Automation/WhimTexApi.Layers.cs'),/descriptor.CreateLayer\(\)/);
assert.match(read('Automation/WhimTexApi.Inspect.cs'),/foreach \(var descriptor in LayerTypeRegistry.Entries\)/);
assert.match(read('TextureCompositorWindow.MissingLayers.cs'),/types\[replacement.index\].CreateBehaviour\(\)/);
console.log('Layer registry: all 13 factories, stable labels/order and shared consumers passed (source contracts only).');
