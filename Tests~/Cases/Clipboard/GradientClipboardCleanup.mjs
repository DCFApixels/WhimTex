// Independent read-only source/scalar replacement. Does not execute Unity, C#, or GPU code.
import { readFileSync } from 'node:fs';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const context = new TestContext('GradientClipboardCleanup: source/scalar contracts');
const assert = context.assert;

context.case('GradientClipboardCleanup original assertions and branches', async () => {
    const read = p => readFileSync(new URL('../../../' + p, import.meta.url), 'utf8').replace(/\r\n/g, '\n');
    const clipboard = read('src/Editor/WhimTexGradientClipboard.cs');
    assert.doesNotMatch(clipboard, /LegacyPrefix|WhimTex\.Gradient\/1|ConvertEnum|transition|JsonUtility/);
    assert.match(clipboard, /return WhimTexApi\.ReadGradient\(data, WhimTexGradientColorMode\.Perceptual, 65504f\)/);
    assert.doesNotMatch(read('src/WhimTexGradient.cs'), /\btransition\b|\bTransition\b/);
    assert.doesNotMatch(read('src/WhimTexDocumentSerializer.cs'), /name == "transition"/);
    assert.doesNotMatch(read('src/Automation/WhimTexApi.Layers.cs'), /"transition"/);
    const fields = JSON.parse(read('Documentation~/AI/agent-fields.schema.json'));
    const gradientObject = fields.$defs.gradient.oneOf.find(v => v.type === 'object');
    assert.equal(gradientObject.additionalProperties, false);
    assert.equal(gradientObject.properties.transition, undefined);
    for (const name of ['colorMode', 'interpolation', 'huePath', 'wrapMode', 'colorSpace']) assert.equal(gradientObject.properties[name].type, 'string');
    const frozen = JSON.parse(read('Tests~/Fixtures/Compatibility0125/gradient.json'));
    assert.equal(frozen.format, 'whimtex.gradient');
    assert.equal(frozen.version, 1);
    assert.equal(frozen.gradient.transition, undefined);
    for (const name of ['mode', 'wrapMode', 'colorSpace']) assert.equal(typeof frozen.gradient[name], 'string');
    for (const key of frozen.gradient.colors) assert.ok(Array.isArray(key.color) && key.color.length === 4);
});

await finish(context);

