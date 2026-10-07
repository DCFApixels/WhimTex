// Independent Node assertions; no Unity commands or Legacy runtime dependencies.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { readFileSync } from 'node:fs';
const context = new TestContext("PolarCoordinates source/reference tests");
const assert = context.assert;
try {

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
  function frame(position = [.5, .5], size = [1, 1], degrees = 0) {
      const angle = degrees * Math.PI / 180, c = Math.cos(angle), s = Math.sin(angle);
      return {
          toInput: uv => {
              const x = (uv[0] - .5) * size[0], y = (uv[1] - .5) * size[1];
              return [position[0] + c * x - s * y, position[1] + s * x + c * y];
          },
          toLocal: uv => {
              const x = uv[0] - position[0], y = uv[1] - position[1];
              return [.5 + (c * x + s * y) / size[0], .5 + (-s * x + c * y) / size[1]];
          }
      };
  }
  const remap = (uv, mode, input, output, angle = 0, radial = 0) =>
      input.toInput((mode === 0 ? toPolar : fromPolar)(output.toLocal(uv), angle, radial));

  context.case('one polar preset switches both mappings and samples complete RGBA without fade', async () => {
      const code = readFileSync(new URL('../../../src/FXPresets/PolarCoordinates.hlsl', import.meta.url), 'utf8');
      assert.equal(code.split(/\r?\n/)[0], '// @whimtex-effect Distortion/Polar Coordinates');
      assert.match(code, /@group\(Polar Coordinates Mode; _Mode\)/);
      assert.match(code, /@param hidden enum _Mode = 0 \{ToPolar: 0, FromPolar: 1\}/);
      assert.match(code, /if \(_Mode < 0\.5\)/);
      for (const declaration of ['float _AngleOffset = 0', 'float _RadialOffset = 0', 'transform2D _Input', 'transform2D _Output'])
          assert.ok(code.includes(`// @param ${declaration}`));
      assert.ok(code.includes('_Output_ToLocal(uv)'));
      assert.ok(code.includes('_Input_ToInput(mappedUV)'));
      assert.match(code, /float2 localUV = _Output_ToLocal\(uv\);/);
      assert.match(code, /\(localUV - 0\.5\) \* 2\.0/);
      assert.match(code, /mappedUV = 0\.5 \+ float2\(cosine, sine\) \* radius \* 0\.5;/);
      assert.doesNotMatch(code, /_Center|sourceOffset|_Area/);
      assert.match(code, /return SampleInput\(/);
      assert.doesNotMatch(code, /\b(?:clamp|saturate|smoothstep|clip|discard)\s*\(/);
      assert.match(code, /@param hidden float _Amount = 1 \[0 \.\. 1\]/);
      assert.match(code, /if \(_Amount <= 0\.0\) return color;/);
      assert.match(code, /return SampleInput\(lerp\(uv, sampleUV, _Amount\), _Tiling\);/);
      assert.equal((code.match(/\blerp\s*\(/g) ?? []).length, 1, 'Only coordinates are interpolated, never two RGBA results');
      assert.match(code, /if \(radius > 0\.0\) angle = atan2\(p.y, p.x\)/);
      assert.match(code, /frac\(angle - _AngleOffset \/ 360\.0\), radius - _RadialOffset/);
      assert.match(code, /localUV.x \+ _AngleOffset \/ 360\.0/);
      assert.match(code, /localUV.y \+ _RadialOffset/);
  });

  context.case('right/up/left/down cover one counterclockwise turn and center stays finite', async () => {
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

  context.case('forward/inverse coordinates round-trip including offsets and outside the frame', async () => {
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

  context.case('angular seam is periodic and radial offset translates the ring without a mask', async () => {
      const first = fromPolar([0, .7], 35, .2);
      const last = fromPolar([1, .7], 35, .2);
      first.forEach((v, i) => near(v, last[i]));
      const shifted = fromPolar([0, .25], 0, .5);
      near(shifted[0], .875);
      near(toPolar(shifted, 0, .5)[1], .25);
  });

  context.case('independent input/output frames remap source coordinates and invert with swapped frames', async () => {
      const frames = [frame(), frame([.2, .75]), frame([1.2, -.4], [.7, 1.3], 31), frame([.6, .4], [-.8, .6], -17)];
      for (const input of frames) for (const output of frames) {
          const poleSample = remap(output.toInput([.5, .5]), 0, input, output);
          const expected = input.toInput([0, 0]);
          // Away from the polar singularity, the input frame cannot change the output frame's geometry.
          for (const [direction, turn] of [[[.5, 0], 0], [[0, .5], .25], [[-.5, 0], .5], [[0, -.5], .75]]) {
              const point = output.toInput(direction.map(v => .5 + v));
              remap(point, 0, input, output, 37).forEach((v, i) => near(v, input.toInput([frac(turn - 37 / 360), 1])[i]));
          }
          if (input === frames[0] && output === frames[0]) poleSample.forEach((v, i) => near(v, expected[i]));
          for (const angleOffset of [-37, 0, 83]) for (const radialOffset of [-.5, 0, .25])
              for (const [turn, radius] of [[.125, .2], [.49, 1], [.75, 4]]) {
                  const uv = input.toInput([turn, radius - radialOffset]);
                  const circle = remap(uv, 1, output, input, angleOffset, radialOffset);
                  const restored = remap(circle, 0, input, output, angleOffset, radialOffset);
                  restored.forEach((v, i) => near(v, uv[i]));
              }
      }
  });

} catch (error) {
  context.case('Fixture initialization', async () => { throw error; });
}
await finish(context);
