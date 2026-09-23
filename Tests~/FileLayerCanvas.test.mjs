import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL('../' + path, import.meta.url), 'utf8');
const file = read('src/Layers/FileLayerBehaviour.cs');
assert.match(file, /\[SerializeField\] private bool sourceAssigned;/);
assert.match(file, /bool initializeCanvas = false/);
assert.match(file, /sourceAssigned \|= sourceTexture != null \|\| texture != null;/);
assert.ok(file.indexOf('owner.width =') < file.indexOf('TryGetOriginalAspectTransform'), 'Fit uses the initialized canvas dimensions');
assert.match(file, /owner.width = Mathf.Max\(1, sourceForSizing != null \? sourceForSizing.width : texture.width\);\s*owner.height = Mathf.Max\(1, sourceForSizing != null \? sourceForSizing.height : texture.height\);/,
  'Canvas initialization prefers the decoded original source dimensions');
assert.match(file, /sourceForSizing = owner.ResolveOriginalFileTexture\(texture\)/,
  'Original source texture is resolved before assigning the canvas size');

const condition = file.match(/if \((initializeCanvas .*CanInitializeCanvas\(owner\))\)/)[1];
const canInitializeSource = file.split('private bool CanInitializeCanvas(TextureCompositor owner)')[1]
  .split('public override Texture2D')[0].trim()
  .replace('foreach (Layer layer in owner.layers)', 'for (const layer of owner.layers)')
  .replace('!ReferenceEquals(layer, Owner)', 'layer !== this.Owner');
const canInitialize = new Function('owner', canInitializeSource);
const eligible = new Function('initializeCanvas', 'wasEmpty', 'sourceAssigned', 'texture', 'owner', 'CanInitializeCanvas', `return ${condition};`);
const layer = {};
layer.Owner = layer;
const texture = { width: 2048, height: 1024 };
const empty = { layers: [] };
const sole = { layers: [layer] };
const check = (owner, wasEmpty = true, assigned = false, input = texture, enabled = true) =>
  eligible(enabled, wasEmpty, assigned, input, owner, value => canInitialize.call(layer, value));
assert.equal(check(empty), true, 'Drop before inserting the first layer');
assert.equal(check(sole), true, 'First source on the sole File layer');
assert.equal(check({ layers: [null, layer] }), true);
assert.equal(check({ layers: [layer, { enabled: false }] }), false, 'Hidden layers still prevent resizing');
assert.equal(check({ layers: [{ layers: [layer] }] }), false, 'A group is an existing layer');
assert.equal(check(null), false);
assert.equal(check(sole, false), false, 'Replacing a source');
assert.equal(check(sole, true, true), false, 'Clearing and reassigning a source');
assert.equal(check(sole, true, false, null), false, 'Clearing the field');
assert.equal(check(empty, true, false, texture, false), false, 'API canvas dimensions remain explicit');
const second = {};
assert.equal(canInitialize.call(second, sole), false, 'Only the first item of a multi-file drop sets the size');

assert.match(read('src/TextureCompositorWindow.TextureDrop.cs'), /AssignSourceTexture\(texture, owner.compositor, initializeCanvas: true\)/);
assert.match(read('src/Layers/Editors/FileLayerEditorWindow.cs'), /AssignSourceTexture\(evt.newValue as Texture2D, compositor, initializeCanvas: true\)/);
assert.match(read('src/Automation/WhimTexApi.Layers.cs'), /file.AssignSourceTexture\(texture, document\);/);
console.log('File layer canvas initialization: extracted eligibility and source checks passed (Unity not executed).');
