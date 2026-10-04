// Independent read-only source/scalar replacement. Does not execute Unity, C#, or GPU code.
import { readFileSync } from 'node:fs';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const context = new TestContext('EmptyDocumentSave: source/scalar contracts');
const assert = context.assert;

context.case('EmptyDocumentSave original assertions and branches', async () => {
    const read = path => readFileSync(new URL('../../../' + path, import.meta.url), 'utf8');
    const window = read('src/TextureCompositorWindow.cs');
    const expression = window.match(/hasUnsavedChanges = ([\s\S]*?);/)[1];
    const closeWarning = new Function('HasCanvasLayers', 'HasDocumentChanges', 'paintingLayer', 'canvasTransformManipulator', `return ${expression};`);
    for (const hasLayers of [false, true]) for (const dirty of [false, true])
        for (const painting of [null, {}]) for (const transform of [null, { IsDragging: false }, { IsDragging: true }]) {
            assert.equal(closeWarning(hasLayers, () => dirty, painting, transform),
                dirty || painting !== null || transform?.IsDragging === true);
        }
    assert.match(window, /ResolveUnsavedTemporaryDocument\(\)\s*\{\s*PrepareDocumentSave\(\);\s*if \(!HasDocumentChanges\(\)\)/);
    assert.match(window, /public override void SaveChanges\(\)\s*\{\s*if \(!SaveDocument\(\)\)/);
    const dirtyCheck = window.match(/private bool HasDocumentChanges\(\) => ([\s\S]*?);/)[1];
    assert.ok(!dirtyCheck.includes('HasCanvasLayers'), 'Explicit Save still tracks changes when all layers have been deleted');
    const layers = read('src/TextureCompositorWindow.Tools.cs').split('private bool HasCanvasLayers')[1].split('private bool IsCanvasBrushEnabled')[0];
    assert.ok(layers.includes('if (layer != null) return true;'), 'Hidden layers and empty groups still count as layers');
});

await finish(context);

