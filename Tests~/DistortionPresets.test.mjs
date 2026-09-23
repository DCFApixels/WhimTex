import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

// CPU reference/contract checks, not a substitute for compiling and rendering HLSL in Unity.
const source = name => readFileSync(new URL(`../src/FXPresets/${name}.hlsl`, import.meta.url), 'utf8');
const spherize = (p, strength) => {
    const scale = Math.pow(Math.max(p[0] ** 2 + p[1] ** 2, 1e-12), .5 * (2 ** strength - 1));
    return p.map(x => x * scale);
};
const sphereSampleRadius = (radius, strength) => {
    const r = Math.min(Math.max(radius, 0), 1);
    const sphereRadius = strength >= 0 ? 2 * Math.asin(r) / Math.PI : Math.sin(r * Math.PI / 2);
    const amount = Math.abs(strength);
    let mapped = radius + (sphereRadius - radius) * Math.min(amount, 1);
    const excess = Math.max(amount - 1, 0);
    mapped = strength >= 0
        ? mapped * 2 ** (-excess * (1 - mapped))
        : 1 - (1 - mapped) * 2 ** (-excess * mapped);
    return mapped;
};
const twirl = (p, degrees) => {
    const a = -degrees * Math.PI / 180 * Math.hypot(...p);
    return [Math.cos(a) * p[0] - Math.sin(a) * p[1], Math.sin(a) * p[0] + Math.cos(a) * p[1]];
};
const near = (a, b, message = '') => assert.ok(Math.abs(a - b) < 1e-9, message || `${a} != ${b}`);

test('classic distortions keep their existing unbounded Transform 2D mapping', () => {
    for (const name of ['Twirl']) {
        const code = source(name);
        assert.equal(code.split(/\r?\n/)[0], `// @whimtex-effect Distortion/${name}`);
        assert.match(code, /@param transform2D _Area/);
        assert.match(code, /_Area_ToLocal\(uv\)/);
        assert.match(code, /_Area_ToInput\(/);
        assert.match(code, /return SampleInput\(/);
        assert.doesNotMatch(code, /\b(?:saturate|clamp|lerp|smoothstep|clip|discard)\s*\(/);
        assert.equal((code.match(/\bif\s*\(/g) ?? []).length, 1, 'Only the zero-strength identity branch');
    }
    const spherize = source('Spherize');
    assert.equal(spherize.split(/\r?\n/)[0], '// @whimtex-effect Distortion/Spherize');
    assert.match(spherize, /@param enum _Mode = 0 \{Classic: 0, Sphere: 1\}/);
    assert.match(spherize, /if \(_Mode < 0\.5\)/);
    assert.match(spherize, /float4 result = color/);
    assert.match(spherize, /exp2\(_Strength\)/);
    assert.match(spherize, /pow\(max\(dot\(p, p\), 1e-12\), 0\.5 \* \(exponent - 1\.0\)\)/);
    assert.match(spherize, /result = SampleInput\(_Area_ToInput\(0\.5 \+ p \* scale \* 0\.5\)\)/);
    assert.equal((spherize.match(/\breturn\b/g) ?? []).length, 1, 'ApplyFX has one initialized return path');
    assert.match(source('Twirl'), /-radians\(_Angle\) \* length\(p\)/);
});

test('Sphere mode maps a circular texture over a sphere and clips its edge', () => {
    const code = source('Spherize');
    assert.match(code, /asin\(clampedRadius\) \* 0\.6366197723675814/);
    assert.match(code, /sin\(clampedRadius \* 1\.5707963267948966\)/);
    assert.match(code, /min\(strength, 1\.0\)/);
    assert.match(code, /float excessStrength = max\(strength - 1\.0, 0\.0\)/);
    assert.match(code, /exp2\(-excessStrength \* \(1\.0 - sampleRadius\)\)/);
    assert.match(code, /exp2\(-excessStrength \* sampleRadius\)/);
    assert.match(code, /float edgeWidth = max\(fwidth\(radius\), 1e-4\)/);
    assert.match(code, /result\.a \*= 1\.0 - smoothstep\(1\.0 - edgeWidth, 1\.0 \+ edgeWidth, radius\)/);

    for (const strength of [-8, -2, -1, -.5, 0, .5, 1, 2, 8]) {
        let previous = -1;
        for (let i = 0; i <= 1000; i++) {
            const radius = i / 1000;
            const mapped = sphereSampleRadius(radius, strength);
            assert.ok(Number.isFinite(mapped) && mapped >= previous, `non-monotonic mapping at ${radius}, ${strength}`);
            previous = mapped;
        }
        near(sphereSampleRadius(0, strength), 0);
        near(sphereSampleRadius(1, strength), 1, 'The source mapping meets the circular edge');
    }
    assert.ok(sphereSampleRadius(.5, 1) < .5, 'Positive strength bulges the source toward the center');
    assert.ok(sphereSampleRadius(.5, -1) > .5, 'Negative strength pinches the source toward the edge');
    assert.ok(sphereSampleRadius(.5, 2) < sphereSampleRadius(.5, 1), 'Positive strength continues past 1');
    assert.ok(sphereSampleRadius(.5, -2) > sphereSampleRadius(.5, -1), 'Negative strength continues past -1');
});

test('spherize is finite at center and monotonic, including outside normalized bounds', () => {
    for (const strength of [-1, -.5, 0, .5, 1]) {
        assert.deepEqual(spherize([0, 0], strength), [0, 0]);
        let previous = -1;
        for (const radius of [1e-9, .01, .25, .5, 1, 1.01, 2, 10, 100]) {
            const [mapped] = spherize([radius, 0], strength);
            assert.ok(Number.isFinite(mapped) && mapped > previous);
            if (radius >= .01) near(mapped, radius ** (2 ** strength));
            previous = mapped;
        }
    }
    assert.ok(spherize([.5, 0], .5)[0] < .5, 'Positive strength samples closer to center (bulge)');
    assert.ok(spherize([.5, 0], -.5)[0] > .5, 'Negative strength pinches');
    assert.ok(spherize([2, 0], .5)[0] > 2, 'No identity fallback outside the frame');
});

test('twirl preserves radius, reverses with angle sign and continues outside the frame', () => {
    for (const p of [[0, 0], [.2, -.4], [1, 0], [2, 3], [-10, 6]]) {
        for (const angle of [-720, -180, 0, 90, 720]) {
            const mapped = twirl(p, angle);
            near(Math.hypot(...mapped), Math.hypot(...p));
            twirl(mapped, -angle).forEach((value, i) => near(value, p[i]));
        }
    }
    const outside = twirl([2, 0], 45);
    near(outside[0], 0);
    near(outside[1], -2);
});

test('displacement map supports a single map with an optional strength mask', () => {
    const code = source('DisplacementMap');
    assert.equal(code.split(/\r?\n/)[0], '// @whimtex-effect Distortion/Displacement Map');
    assert.match(code, /@param texture2D _DisplacementMap = self/);
    assert.match(code, /@param transform2D _MapTransform/);
    assert.match(code, /@param enum _Mode = VectorRG \{VectorRG: 0, Grayscale: 1\}/);
    assert.match(code, /@param enum _MaskSource = Constant1 \{Constant1: 0, MapChannel: 1, InputAlpha: 2, SeparateTexture: 3\}/);
    assert.match(code, /float strengthMask = 1\.0/);
    assert.match(code, /ReadStrengthChannel\(mapSample, _MapMaskChannel\)/);
    assert.match(code, /maskValue = color\.a/);
    assert.match(code, /texture2D _StrengthMask = none/);
    assert.match(code, /tex2D\(_StrengthMask, strengthMaskUV\)/);
    assert.match(code, /displacementPixels \* strengthMask \* _CanvasSize\.zw/);
    assert.match(code, /enum _InputEdge = Clamp \{Clamp: 0, Repeat: 1, Mirror: 2, Transparent: 3\}/);
    assert.match(code, /float2 AddressMapUV\(/);
    assert.match(code, /float2 AddressInputUV\(/);
    assert.match(code, /float4 distorted = SampleInput\(inputUV\) \* inside/);
    const applyFX = code.slice(code.indexOf('float4 ApplyFX('));
    assert.equal((applyFX.match(/\breturn\b/g) ?? []).length, 1, 'ApplyFX has one initialized return path');

    const vectorOffset = (sample, neutral, strength, mask) => sample.map((value, i) => 2 * (value - neutral) * strength[i] * mask);
    const strengthMask = (source, value) => source === 'Constant1' ? 1 : value;
    vectorOffset([.5, .5], .5, [80, -40], strengthMask('Constant1', 0)).forEach(value => near(value, 0));
    vectorOffset([1, 0], .5, [80, -40], strengthMask('Constant1', 0)).forEach((value, i) => near(value, [80, 40][i]));
    vectorOffset([1, 0], .5, [80, -40], strengthMask('MapChannel', .25)).forEach((value, i) => near(value, [20, 10][i]));
});
