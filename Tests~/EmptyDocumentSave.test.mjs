import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL('../' + path, import.meta.url), 'utf8');
const window = read('src/TextureCompositorWindow.cs');
const expression = window.match(/hasUnsavedChanges = ([\s\S]*?);/)[1];
const closeWarning = new Function('HasPreviewLayers', 'HasDocumentChanges', 'paintingLayer', 'previewTransformManipulator', `return ${expression};`);
for (const hasLayers of [false, true]) for (const dirty of [false, true])
    for (const painting of [null, {}]) for (const transform of [null, { IsDragging: false }, { IsDragging: true }]) {
        assert.equal(closeWarning(hasLayers, () => dirty, painting, transform),
            dirty || painting !== null || transform?.IsDragging === true);
    }
assert.match(window, /ResolveUnsavedTemporaryDocument\(\)\s*\{\s*PrepareDocumentSave\(\);\s*if \(!HasDocumentChanges\(\)\)/);
assert.match(window, /public override void SaveChanges\(\)\s*\{\s*if \(!SaveDocument\(\)\)/);
const dirtyCheck = window.match(/private bool HasDocumentChanges\(\) => ([\s\S]*?);/)[1];
assert.ok(!dirtyCheck.includes('HasPreviewLayers'), 'Explicit Save still tracks changes when all layers have been deleted');
const layers = read('src/TextureCompositorWindow.Tools.cs').split('private bool HasPreviewLayers')[1].split('private bool IsPreviewBrushEnabled')[0];
assert.ok(layers.includes('if (layer != null) return true;'), 'Hidden layers and empty groups still count as layers');
console.log('Empty-document save prompt source/control-flow checks passed (Unity not executed).');
