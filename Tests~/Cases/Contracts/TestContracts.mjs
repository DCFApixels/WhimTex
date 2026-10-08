// Current product/entry guards; no migration mappings or historical source scanner.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { TestContext, finish } from '../../Framework/test-api.mjs';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const catalog = JSON.parse(read('Tests~/scripts/test-catalog.json'));
const context = new TestContext('Current test entries and product source contracts');
const assert = context.assert;
function scenario(id) {
    const matches = catalog.scenarios.filter(s => s.id === id);
    assert.equal(matches.length, 1, id + ': exactly one registered scenario');
    const value = matches[0];
    assert.equal(value.runner, 'run_script');
    assert.equal(value.category, 'regression');
    assert.equal(value.result.kind, 'structured');
    return value;
}
function declaredEntry(value, entry) {
    const [className, methodName] = entry.split('.').slice(-2);
    const source = [value.file, ...(value.supportFiles ?? [])].map(read).join('\n');
    assert.match(source, new RegExp('class\\s+' + className + '\\b'), entry + ': class exists');
    assert.match(source, new RegExp('public\\s+static\\s+(?:async\\s+)?(?:[\\w<>.]+)\\s+' + methodName + '\\('), entry + ': public entry exists');
}
function asyncLifecycle(value) {
    assert.ok(value.async && value.cancel && value.cleanup, value.id + ': completion, cancellation and cleanup');
    assert.deepEqual(value.args, ['$runId']);
    for (const phase of [value.async, value.cancel, value.cleanup]) {
        assert.deepEqual(phase.args, ['$runId']);
        declaredEntry(value, phase.entry);
    }
    assert.ok(value.async.pollMs >= 100 && value.async.pollMs <= 5000);
    assert.ok(value.supportFiles.includes('Tests~/Framework/TestApi.cs'));
    assert.doesNotMatch(value.prerequisites + ' ' + value.setup,
        /Run (?:CanvasViewFooterSetup|ContentFillUiSetup|UvUiSetup)/, 'No manual setup dependency');
    declaredEntry(value, value.entry);
}
context.case('Removed production helpers stay removed', () => {
    for (const [file, name] of [
        ['src/WhimTexDocument.cs', 'CloneEmbeddedShaderFX'],
        ['src/WhimTexDocument.cs', 'CloneDrawingLayerTextures'],
        ['src/Layers/SDFLayerBehaviour.cs', 'ConvertDistance'],
        ['src/MissingLayerRecovery.cs', 'GradientTime'],
        ['src/WhimTexWindow.UI.cs', 'FillRect'],
        ['src/WhimTexDocumentSerializer.cs', 'ReflectedFieldCount']
    ]) assert.doesNotMatch(read(file), new RegExp('\\b' + name + '\\s*\\('), name);
    const walk = dir => fs.readdirSync(path.join(root, dir), { withFileTypes: true }).flatMap(entry =>
        entry.isDirectory() ? walk(dir + '/' + entry.name) : [entry.name]);
    const files = walk('Tests~/Cases');
    for (const file of ['LayerPersistenceSetup.cs', 'LayerPersistenceVerify.cs', 'LayerPersistenceCleanup.cs', 'SpriteEditorCompileSmoke.cs'])
        assert.ok(!files.includes(file), file);
});
context.case('Soft range has an explicit runnable entry', () => {
    const value = scenario('soft-range-v2');
    assert.equal(value.file, 'Tests~/Cases/FX/SoftRangeTests.cs');
    assert.equal(value.entry, 'SoftRangeTests.Run');
    declaredEntry(value, value.entry);
});
context.case('Two-choice control has a self-contained async lifecycle', () => {
    const value = scenario('two-choice-dropdown-v2');
    assert.equal(value.file, 'Tests~/Cases/Canvas/TwoChoiceDropdownTests.cs');
    assert.equal(value.entry, 'TwoChoiceDropdownTests.Start');
    asyncLifecycle(value);
});
for (const id of ['canvas-view-footer-v2', 'content-fill-ui-v2', 'uv-ui-v2'])
    context.case(id + ': bounded owned UI lifecycle', () => asyncLifecycle(scenario(id)));
for (const id of ['gradient-clipboard-cleanup-smoke-v2', 'remaining-legacy-cleanup-unity-v2', 'user-settings-cleanup-unity-v2'])
    context.case(id + ': runnable regression entry', () => {
        const value = scenario(id);
        declaredEntry(value, value.entry);
    });
context.case('Shared Gaussian kernel capacity and upload paths', () => {
    assert.match(read('src/Layers/GaussianKernel.cs'), /const int Capacity = 128/);
    assert.match(read('src/Layers/GaussianKernel.cs'), /buffer\.Length != Capacity/);
    assert.match(read('src/Layers/DrawingLayerBehaviour.BlurBrush.cs'), /new Vector4\[GaussianKernel\.Capacity\]/);
    assert.match(read('src/Layers/DrawingLayerBehaviour.BlurBrush.cs'), /GaussianKernel\.Upload\(blur, \.2f, 5, BlurBrushKernel\)/);
    for (const file of ['GaussianBlurRenderer.cs', 'SharpenRenderer.cs'])
        assert.match(read('src/Layers/' + file), /GaussianKernel\.Set\(/);
    assert.doesNotMatch(read('src/Layers/DrawingLayerBehaviour.BlurBrush.cs'), /SetVectorArray\("_Kernel", new/);
});
await finish(context);
