// Scalar/source checks only: no Unity compilation, asset changes or GPU execution.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL('../' + path, import.meta.url), 'utf8');
const dynamics = read('src/BrushDynamics.cs');
const brush = read('src/Layers/DrawingLayerBehaviour.Brush.cs');
const shader = read('src/Shaders/PaintBrush.shader');
const ui = read('src/TextureCompositorWindow.Brushes.cs');
const sampleBody = dynamics.match(/internal bool SampleFlip\([^)]*\)\s*\{([^}]+)\}/)[1];
const flip = new Function('state', 'stampIndex', 'dimension', 'probability', 'SampleRandom',
  sampleBody.replace(/(\d)f\b/g, '$1').replace(/ref state/g, 'state'));
let samples = 0;
const random = () => { samples++; return .25; };
assert.equal(flip(0, 0, 5, 0, random), false);
assert.equal(flip(0, 0, 6, 1, random), true);
assert.equal(samples, 0, 'Never/always do not consume randomness');
assert.equal(flip(0, 0, 5, .25, random), false);
assert.equal(flip(0, 0, 5, .25001, random), true);
assert.equal(samples, 2);
for (let bits = 0; bits < 4; bits++) for (const degrees of [-180,-90,-35,0,27,90,180]) {
  const a = degrees * Math.PI / 180, c = Math.cos(a), s = Math.sin(a);
  const fy = bits >= 1.5 ? 1 : 0, fx = bits - 2 * fy;
  for (const aspect of [[1,1],[1,.3],[.4,1]]) for (const [u,v] of [[.13,.28],[.83,.61]]) {
    const x = (u * 2 - 1) * aspect[0] * (1 - 2 * fx);
    const y = (v * 2 - 1) * aspect[1] * (1 - 2 * fy);
    const rx = c*x - s*y, ry = s*x + c*y;
    const actualU = (c*rx + s*ry) * (1 - 2*fx) / aspect[0] * .5 + .5;
    const actualV = (-s*rx + c*ry) * (1 - 2*fy) / aspect[1] * .5 + .5;
    assert.ok(Math.abs(actualU-u)<1e-10 && Math.abs(actualV-v)<1e-10);
  }
}
for (const axis of ['X','Y']) {
  assert.ok(dynamics.includes(`flip${axis} = Unit(flip${axis}, 0f);`));
  assert.ok(ui.includes(`new Slider("Flip ${axis}", 0f, 1f)`));
  assert.ok(ui.includes(`flip${axis}.SetEnabled(paintSettings.dynamics.tip != null)`));
  assert.ok(read('src/PaintToolSettings.cs').includes(`dynamics.flip${axis} = defaults.dynamics.flip${axis}`));
}
assert.ok(brush.includes('SampleFlip(ref brushRandomState, stampIndex, 5, dynamics.flipX)'));
assert.ok(brush.includes('SampleFlip(ref brushRandomState, stampIndex, 6, dynamics.flipY)'));
assert.ok(brush.indexOf('int flip = 0;') < brush.indexOf('BuildPatternStamps(point'));
assert.ok(brush.includes('stamp.flip = flip;'));
assert.ok(read('src/Layers/DrawingLayerBehaviour.cs').includes('new Vector4(dabSize, dabColor.a, stamp.rotation, stamp.flip)'));
assert.ok(read('src/Layers/DrawingLayerBehaviour.BrushMesh.cs').includes('List<Vector4> meshStamps'));
assert.ok(shader.includes('float4 size : TEXCOORD6;'));
assert.ok(shader.includes('float4 shape : TEXCOORD7;'));
assert.ok(shader.includes('sin(input.size.z), input.size.w)'));
assert.ok(shader.includes('float flipY = step(1.5, input.shape.w);'));
assert.ok(shader.includes('float flipX = input.shape.w - 2.0 * flipY;'));
assert.ok(shader.indexOf('brushDelta *= 1.0 - 2.0 * float2(flipX, flipY);') < shader.indexOf('float2 tipUv'));
console.log('Brush Flip probability, local-axis arithmetic and source checks passed (Unity/GPU not executed).');
