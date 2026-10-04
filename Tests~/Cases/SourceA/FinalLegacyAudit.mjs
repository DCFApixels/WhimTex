// Independent read-only source/scalar replacement. Does not execute Unity, C#, or GPU code.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { auditSources } from '../../Framework/NodeSupportA/AuditSources.mjs';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const context = new TestContext('FinalLegacyAudit: source/scalar contracts');
const assert = context.assert;

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
let inventory;
const audit = () => inventory ??= auditSources();

// Original names are mapping identifiers only; no archived source is opened.
function replacementScenario(original) {
    const mapping = audit().replacements.get(original);
    assert.ok(mapping, 'Pending ACTIVE batch mapping: ' + original);
    assert.notEqual(mapping.state, 'blocked', 'ACTIVE replacement is blocked: ' + original);
    const scenarios = mapping.scenarios.filter(s => s.runner === 'run_script' && s.category !== 'diagnostic');
    assert.equal(scenarios.length, 1, original + ': one independently runnable C# regression');
    const scenario = scenarios[0];
    assert.ok(scenario.file.startsWith('Tests~/Cases/'), original + ': independent new source');
    assert.equal(mapping.newFile, scenario.file, original + ': mapping/source identity');
    assert.equal(scenario.category, 'regression', original + ': explicit classification');
    assert.equal(scenario.result.kind, 'structured', original + ': structured status/counts');
    return scenario;
}
function declaredEntry(scenario, entry) {
    const [className, methodName] = entry.split('.').slice(-2);
    const sources = [scenario.file, ...(scenario.supportFiles ?? [])].map(read).join('\n');
    assert.match(sources, new RegExp('class\\s+' + className + '\\b'), entry + ': class in new sources');
    assert.match(sources, new RegExp('public\\s+static\\s+(?:async\\s+)?(?:[\\w<>.]+)\\s+' + methodName + '\\('),
        entry + ': declared public entry in new sources');
}
function asyncLifecycle(scenario) {
    assert.ok(scenario.async, scenario.id + ': self-contained async scenario');
    assert.ok(scenario.cancel && scenario.cleanup, scenario.id + ': cancellable work and owned cleanup');
    assert.deepEqual(scenario.args, ['$runId'], scenario.id + ': per-run identity');
    for (const phase of [scenario.async, scenario.cancel, scenario.cleanup]) {
        assert.deepEqual(phase.args, ['$runId'], scenario.id + ': same lifecycle identity');
        declaredEntry(scenario, phase.entry);
    }
    assert.ok(scenario.async.pollMs >= 100 && scenario.async.pollMs <= 5000, scenario.id + ': bounded polling');
    assert.ok(scenario.supportFiles.includes('Tests~/Framework/TestApi.cs'), scenario.id + ': shared assertions');
    assert.ok(scenario.supportFiles.every(file => !file.includes('/Legacy/')), scenario.id + ': no archive dependency');
    assert.doesNotMatch(scenario.prerequisites + ' ' + scenario.setup,
        /Run (?:CanvasViewFooterSetup|ContentFillUiSetup|UvUiSetup)/, scenario.id + ': no manual setup dependency');
    declaredEntry(scenario, scenario.entry);
}

context.case('Retired tests and production helper removal', async () => {
    const inventory = audit();
    for (const file of ['LayerPersistenceSetup.cs', 'LayerPersistenceVerify.cs', 'LayerPersistenceCleanup.cs', 'SpriteEditorCompileSmoke.cs'])
      assert.ok(!inventory.tests.some(test => path.basename(test.file) === file) &&
          !inventory.activeCaseFiles.some(source => path.basename(source) === file), file);
    for (const [file, name] of [
      ['src/TextureCompositor.cs', 'CloneEmbeddedShaderFX'],
      ['src/TextureCompositor.cs', 'CloneDrawingLayerTextures'],
      ['src/Layers/SDFLayerBehaviour.cs', 'ConvertDistance'],
      ['src/MissingLayerRecovery.cs', 'GradientTime'],
      ['src/TextureCompositorWindow.UI.cs', 'FillRect'],
      ['src/WhimTexDocumentSerializer.cs', 'ReflectedFieldCount']
    ]) {
      // Reports mention removed symbols too: textual reference counts cannot guard their removal.
      assert.doesNotMatch(read(file), new RegExp('\\b' + name + '\\s*\\('), name);
      assert.ok(!inventory.methodCandidates.some(candidate => candidate.name === name), name);
    }
    
});

// Old Main/multi-step heuristics intentionally become explicit ACTIVE entries/lifecycle.
// Numerical/kernel assertions retain their original inputs and formulas.
context.case('Soft range ACTIVE entry replaces former Main inference', async () => {
    const scenario = replacementScenario('SoftRangeSmoke.cs');
    assert.equal(scenario.file, 'Tests~/Cases/UnityD/SoftRangeTests.cs');
    assert.equal(scenario.entry, 'SoftRangeTests.Run');
    assert.equal(audit().tests.find(test => test.scenario.id === scenario.id).primary, 'SoftRangeTests.Run');
    declaredEntry(scenario, scenario.entry);
});
context.case('Two-choice ACTIVE async entry replaces former Main inference', async () => {
    const scenario = replacementScenario('TwoChoiceDropdownSmoke.cs');
    assert.equal(scenario.file, 'Tests~/Cases/UnityD/TwoChoiceDropdownTests.cs');
    assert.equal(scenario.entry, 'TwoChoiceDropdownTests.Start');
    assert.equal(audit().tests.find(test => test.scenario.id === scenario.id).primary, 'TwoChoiceDropdownTests.Start');
    asyncLifecycle(scenario);
});
for (const original of ['CanvasViewFooterSmoke.cs', 'ContentFillUiSmoke.cs', 'UvUiSmoke.cs']) {
    context.case(original + ': ACTIVE self-contained async UI regression', async () => {
        const scenario = replacementScenario(original);
        assert.equal(audit().tests.find(test => test.scenario.id === scenario.id).lifecycle, 'asynchronous');
        asyncLifecycle(scenario);
    });
}
for (const original of ['GradientClipboardCleanupSmoke.cs', 'RemainingLegacyCleanupSmoke.cs', 'UserSettingsCleanupSmoke.cs']) {
    context.case(original + ': explicit ACTIVE regression classification', async () => {
        const scenario = replacementScenario(original);
        assert.equal(audit().tests.find(test => test.scenario.id === scenario.id).family, 'regression');
        declaredEntry(scenario, scenario.entry);
    });
}

context.case('Shared Gaussian kernel capacity and upload paths', async () => {
    assert.match(read('src/Layers/GaussianKernel.cs'), /const int Capacity = 128/);
    assert.match(read('src/Layers/GaussianKernel.cs'), /buffer\.Length != Capacity/);
    assert.match(read('src/Layers/DrawingLayerBehaviour.BlurBrush.cs'), /new Vector4\[GaussianKernel\.Capacity\]/);
    assert.match(read('src/Layers/DrawingLayerBehaviour.BlurBrush.cs'), /GaussianKernel\.Upload\(blur, \.2f, 5, BlurBrushKernel\)/);
    for (const file of ['GaussianBlurRenderer.cs', 'SharpenRenderer.cs'])
      assert.match(read('src/Layers/' + file), /GaussianKernel\.Set\(/);
    assert.doesNotMatch(read('src/Layers/DrawingLayerBehaviour.BlurBrush.cs'), /SetVectorArray\("_Kernel", new/);
});

await finish(context);
