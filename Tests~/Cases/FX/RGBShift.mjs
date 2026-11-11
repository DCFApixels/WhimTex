// Independent read-only source/scalar replacement. Does not execute Unity, C#, or GPU code.
import { readFileSync } from 'node:fs';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const context = new TestContext('RGB Shift FX: preset metadata');
const assert = context.assert;
const code = readFileSync(new URL('../../../src/FXPresets/RGBShift.hlsl', import.meta.url), 'utf8').replace(/\r\n/g, '\n');

context.case('existing links and mode values are preserved under the RGB Shift name', () => {
    assert.equal(code.split('\n')[0], '// @whimtex-effect Stylization/RGB Shift');
    assert.match(code, /@preset\("418f7e3753414fd3b13889c4f8c3a624", 1\)/);
    assert.match(code, /enum _Mode = 0 \{Radial: 0, Directional: 1, Triangle: 2\}/);
    assert.match(code, /@param px label\(Channel Offset \(px\)\) float _Amount = 2 \[0 \.\. ~16\]/);
    assert.match(code, /@param float _Blend = 1 \[0 \.\. 1\]/);
});

context.case('radial and orientation controls use disjoint visibility conditions', () => {
    const radial = code.match(/@if _Mode == 0\n([\s\S]*?)@endif/)?.[1] ?? '';
    const orientation = code.match(/@if _Mode != 0\n([\s\S]*?)@endif/)?.[1] ?? '';
    assert.match(radial, /@param point _Center = \(0\.5, 0\.5\)/);
    assert.match(radial, /@param float _Falloff/);
    assert.match(orientation, /@param float _Angle/);
});

await finish(context);
