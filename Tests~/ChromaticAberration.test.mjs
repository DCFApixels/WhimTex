import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

const code = readFileSync(new URL('../src/FXPresets/ChromaticAberration.hlsl', import.meta.url), 'utf8').replace(/\r\n/g, '\n');

test('chromatic aberration is an independent effect with radial and directional controls', () => {
    assert.equal(code.split('\n')[0], '// @whimtex-effect Stylization/Chromatic Aberration');
    assert.match(code, /\{Radial: 0, Directional: 1\}/);
    assert.match(code, /@param point _Center = \(0\.5, 0\.5\)/);
    assert.match(code, /@param float _Falloff/);
    assert.match(code, /@param float _Angle/);
    assert.match(code, /@param float _Amount/);
    assert.match(code, /@param float _Blend/);
});

test('only red and blue are offset; green and alpha remain from the source', () => {
    assert.match(code, /SampleInput\(clamp\(uv \+ offset/);
    assert.match(code, /SampleInput\(clamp\(uv - offset/);
    assert.match(code, /float3 shifted = float3\(red, color\.g, blue\)/);
    assert.match(code, /return float4\(lerp\(color\.rgb, shifted, saturate\(_Blend\)\), color\.a\)/);
});
