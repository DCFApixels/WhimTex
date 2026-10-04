import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

// CPU reference and source contracts only; Unity/HLSL rendering is a separate check.
const tau = 2 * Math.PI;
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-9, `${a} != ${b}`);
const frac = v => v - Math.floor(v);
function toPolar(local, angleOffset = 0, radialOffset = 0) {
    const p = local.map(v => (v - .5) * 2);
    const radius = Math.hypot(...p);
    const angle = radius > 0 ? Math.atan2(p[1], p[0]) / tau : 0;
    return [frac(angle - angleOffset / 360), radius - radialOffset];
}
function fromPolar(uv, angleOffset = 0, radialOffset = 0) {
    const angle = (uv[0] + angleOffset / 360) * tau;
    const radius = uv[1] + radialOffset;
    return [.5 + Math.cos(angle) * radius * .5, .5 + Math.sin(angle) * radius * .5];
}

test('one polar preset switches both mappings and samples complete RGBA without fade', () => {
    const code = readFileSync(new URL('../src/FXPresets/PolarCoordinates.hlsl', import.meta.url), 'utf8');
    assert.equal(code.split(/\r?\n/)[0], '// @whimtex-effect Distortion/Polar Coordinates');
    assert.match(code, /@param enum _Mode = 0 \{ToPolar: 0, FromPolar: 1\}/);
    assert.match(code, /if \(_Mode < 0\.5\)/);
    for (const declaration of ['float _AngleOffset = 0', 'float _RadialOffset = 0', 'transform2D _Area'])
        assert.ok(code.includes(`// @param ${declaration}`));
    assert.ok(code.includes('_Area_ToLocal(uv)'));
    assert.ok(code.includes('_Area_ToInput(localUV)'));
    assert.match(code, /return SampleInput\(/);
    assert.doesNotMatch(code, /\b(?:clamp|saturate|lerp|smoothstep|clip|discard)\s*\(/);
    assert.match(code, /if \(radius > 0\.0\) angle = atan2\(p.y, p.x\)/);
    assert.match(code, /frac\(angle - _AngleOffset \/ 360\.0\), radius - _RadialOffset/);
    assert.match(code, /uv.x \+ _AngleOffset \/ 360\.0/);
    assert.match(code, /uv.y \+ _RadialOffset/);
});

test('right/up/left/down cover one counterclockwise turn and center stays finite', () => {
    for (const [point, turn] of [[[1, .5], 0], [[.5, 1], .25], [[0, .5], .5], [[.5, 0], .75]]) {
        const uv = toPolar(point);
        near(uv[0], turn); near(uv[1], 1);
    }
    assert.deepEqual(toPolar([.5, .5]), [0, 0]);
    for (const offset of [-1080, -90, 0, 45, 720])
        assert.ok(toPolar([.5, .5], offset, .2).every(Number.isFinite));
    near(toPolar([2, .5])[1], 3); // No clipping outside the frame.
    near(fromPolar([0, 3])[0], 2);
    near(toPolar([.75, .5], 0, 1)[1], -.5); // Negative sampling radii aren't clamped.
});

test('forward/inverse coordinates round-trip including offsets and outside the frame', () => {
    for (const angleOffset of [-720, -37, 0, 83, 1080])
        for (const radialOffset of [-2, 0, .25, 2])
            for (const radius of [.001, .25, 1, 1.5, 4])
                for (const turn of [.001, .125, .49, .75, .999]) {
                    const uv = [turn, radius - radialOffset];
                    const local = fromPolar(uv, angleOffset, radialOffset);
                    const restored = toPolar(local, angleOffset, radialOffset);
                    near(restored[0], uv[0]); near(restored[1], uv[1]);
                }
});

test('angular seam is periodic and radial offset translates the ring without a mask', () => {
    const first = fromPolar([0, .7], 35, .2);
    const last = fromPolar([1, .7], 35, .2);
    first.forEach((v, i) => near(v, last[i]));
    const shifted = fromPolar([0, .25], 0, .5);
    near(shifted[0], .875);
    near(toPolar(shifted, 0, .5)[1], .25);
});
