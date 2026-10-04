// Independent read-only source/scalar replacement. Does not execute Unity, C#, or GPU code.
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const context = new TestContext('GradientClipboardCleanup: source/scalar contracts');
const assert = context.assert;

context.case('GradientClipboardCleanup original assertions and branches', async () => {
    const read = p => readFileSync(new URL('../../../' + p, import.meta.url), 'utf8').replace(/\r\n/g, '\n');
    const clipboard = read('src/Editor/WhimTexGradientClipboard.cs');
    assert.doesNotMatch(clipboard, /LegacyPrefix|WhimTex\.Gradient\/1|ConvertEnum|transition|JsonUtility/);
    assert.match(clipboard, /return WhimTexApi\.ReadGradient\(data, WhimTexGradientMode\.Perceptual, 65504f\)/);
    const writer = text => text.slice(text.indexOf('        internal static string Write('), text.indexOf('        internal static WhimTexGradient Read(')).trim();
    // SHA-256 of the LF-normalized, trimmed Write method in tag v0.12.5, commit a72cc9544d39f93555ca6e9f39c137340e3028f0.
    assert.equal(createHash('sha256').update(writer(clipboard)).digest('hex'),
      'eb2506c494c4dc779c80b178331461db9b96fedd8fd2d4896b5c3ddbb24ed64b', '0.12.5 preset writer unchanged');
    assert.doesNotMatch(read('src/WhimTexGradient.cs'), /\btransition\b|\bTransition\b/);
    assert.doesNotMatch(read('src/WhimTexDocumentSerializer.cs'), /name == "transition"/);
    assert.doesNotMatch(read('src/Automation/WhimTexApi.Layers.cs'), /"transition"/);
    const fields = JSON.parse(read('Documentation~/AI/agent-fields.schema.json'));
    const gradientObject = fields.$defs.gradient.oneOf.find(v => v.type === 'object');
    assert.equal(gradientObject.additionalProperties, false);
    assert.equal(gradientObject.properties.transition, undefined);
    for (const name of ['mode', 'wrapMode', 'colorSpace']) assert.equal(gradientObject.properties[name].type, 'string');
    const frozen = JSON.parse(read('Tests~/Fixtures/Compatibility0125/gradient.json'));
    assert.equal(frozen.format, 'whimtex.gradient');
    assert.equal(frozen.version, 1);
    assert.equal(frozen.gradient.transition, undefined);
    for (const name of ['mode', 'wrapMode', 'colorSpace']) assert.equal(typeof frozen.gradient[name], 'string');
    for (const key of frozen.gradient.colors) assert.ok(Array.isArray(key.color) && key.color.length === 4);
});

await finish(context);

