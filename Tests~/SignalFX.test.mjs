import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

const source = name => readFileSync(new URL(`../src/FXPresets/${name}.hlsl`, import.meta.url), 'utf8').replace(/\r\n/g, '\n');

test('CRT preset exposes screen controls and deterministic time/seed inputs', () => {
    const code = source('CRT');
    assert.equal(code.split('\n')[0], '// @whimtex-effect Stylization/CRT');
    for (const parameter of ['_Curvature', '_ScanlineStrength', '_PhosphorStrength', '_Vignette', '_ChromaticAberration', '_NoiseAmount', '_Flicker', '_Seed', '_EffectTime'])
        assert.match(code, new RegExp(`@param float ${parameter}\\b`));
    assert.match(code, /#include ".*Dither\.cginc"/);
    assert.match(code, /HashNoise\(pixel \+ float2\(_Seed/);
    assert.match(code, /floor\(max\(_EffectTime, 0\.0\) \* 24\.0\)/);
    assert.doesNotMatch(code, /\b_Time\b/, 'Never depend on Unity’s implicit time uniform');
});

test('VHS preset exposes seeded line noise and an explicit moving tracking band', () => {
    const code = source('VHS');
    assert.equal(code.split('\n')[0], '// @whimtex-effect Stylization/VHS');
    for (const parameter of ['_ChromaBleed', '_LineJitter', '_ColorLoss', '_NoiseAmount', '_TrackingStrength', '_TrackingHeight', '_TrackingOffset', '_TrackingSpeed', '_Seed', '_EffectTime'])
        assert.match(code, new RegExp(`@param float ${parameter}\\b`));
    assert.match(code, /HashNoise\(float2\(lineIndex \+ _Seed/);
    assert.match(code, /frac\(_EffectTime \* _TrackingSpeed \+ bandSeed\)/);
    assert.match(code, /SampleInput\(clamp\(safeUV \+ chromaOffset/);
    assert.match(code, /SampleInput\(clamp\(safeUV - chromaOffset/);
    assert.doesNotMatch(code, /\b_Time\b/, 'Never depend on Unity’s implicit time uniform');
});
