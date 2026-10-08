// Independent read-only source/scalar replacement. Does not execute Unity, C#, or GPU code.
import { readFileSync } from 'node:fs';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const context = new TestContext('ChromaticAberration: source/scalar contracts');
const assert = context.assert;

context.case('chromatic aberration is an independent effect with radial and directional controls', async () => {
    const code = readFileSync(new URL('../../../src/FXPresets/ChromaticAberration.hlsl', import.meta.url), 'utf8').replace(/\r\n/g, '\n');
    {
        assert.equal(code.split('\n')[0], '// @whimtex-effect Stylization/Chromatic Aberration');
        assert.match(code, /\{Radial: 0, Directional: 1\}/);
        assert.match(code, /@param point _Center = \(0\.5, 0\.5\)/);
        assert.match(code, /@param float _Falloff/);
        assert.match(code, /@param float _Angle/);
        assert.match(code, /@param label\(Channel Offset \(px\)\) float _Amount = 2 \[0 \.\. ~16\]/);
        assert.match(code, /@param float _Blend/);
    }
});

context.case('only red and blue are offset; green and alpha remain from the source', async () => {
    const code = readFileSync(new URL('../../../src/FXPresets/ChromaticAberration.hlsl', import.meta.url), 'utf8').replace(/\r\n/g, '\n');
    {
        assert.match(code, /SampleInput\(clamp\(uv \+ offset/);
        assert.match(code, /SampleInput\(clamp\(uv - offset/);
        assert.match(code, /float3 shifted = float3\(red, color\.g, blue\)/);
        assert.match(code, /return float4\(lerp\(color\.rgb, shifted, saturate\(_Blend\)\), color\.a\)/);
    }
});

await finish(context);
