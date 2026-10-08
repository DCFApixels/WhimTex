// Independent Node assertions; no Unity commands or Legacy runtime dependencies.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { readFileSync } from 'node:fs';
const context = new TestContext("LayerTypeRegistry source/reference tests");
const assert = context.assert;
context.case("LayerTypeRegistry original assertion inputs and source contracts", async () => {
  // Registry contract; no Unity compilation or runtime execution.
  const read = p => readFileSync(new URL('../../../src/' + p, import.meta.url), 'utf8');
  const registry = read('LayerTypeRegistry.cs');
  const entries = [...registry.matchAll(/new Entry\("([^"]+)", "([^"]+)", "([^"]+)", "([^"]+)", typeof\((\w+)\), (\d), (?:\(\) => new (\w+)\(\)|(\w+)\.CreateDefault)\)/g)]
      .map(([,id,menu,prefix,inside,type,section,constructor,defaults]) => ({id,menu,prefix,inside,type,section:+section,factory:constructor ?? defaults,defaults:!!defaults}));
  assert.deepEqual(entries.map(e=>e.id), ['drawing','file','color','gradient','noise','shape','outline','sdf','normalMap','blur','sharpen','makeSeamless','shaderProcessor','group']);
  assert.equal(new Set(entries.map(e=>e.type)).size, 14);
  assert.deepEqual(entries.map(e=>e.section), [0,0,0,0,0,0,1,1,1,1,1,1,1,2]);
  for(const entry of entries) {
      assert.equal(entry.type,entry.factory);
      assert.equal(entry.defaults, entry.id === 'makeSeamless', 'Only Make Seamless uses its configured default factory');
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

});
await finish(context);

